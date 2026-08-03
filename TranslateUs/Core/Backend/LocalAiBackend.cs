using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TranslateUs.Core.Manager;
using TranslateUs.Native;

namespace TranslateUs.Core;

/// <summary>
/// Local AI translation backend with dual-mode architecture:
/// The public API is identical for both platforms.
/// </summary>
public class LocalAiBackend : IDisposable
{
    #region Singleton

    public static LocalAiBackend Instance { get; } = new();
    private LocalAiBackend() { }

    #endregion

    #region Shared state

    private bool _isAndroid;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private bool _isInitializing;
    private Task? _initTask;
    private volatile bool _disposed;

    public bool IsReady { get; private set; }
    public bool IsInitializing => _isInitializing;

    /// <summary>True while the KV cache warmup prompt is being processed (~15-30s).</summary>
    public bool IsWarmingUp { get; private set; }

    /// <summary>True after at least one warmup has completed this session.</summary>
    public bool IsCacheReady { get; private set; }

    /// <summary>Set to true after the one-time warmup notification has been shown in chat.</summary>
    public static bool WarmupNotificationShown;

    #endregion

    #region Windows state

    private ServerProcessManager? _server;

    #endregion

    #region Android state (P/Invoke)

    private IntPtr _model;
    private IntPtr _vocab;
    private IntPtr _ctx;
    private int _vocabSize;
    private int _eosToken;
    private int _contextSize;
    private int _batchSize;
    private LlamaCppApi.llama_context_params _cParams;

    private byte[]? _prefixState;
    private string? _cachedPrefix;

    #endregion

    #region Initialization

    public void BeginInitialize(string modelPath, int threads = 2, int contextSize = 1024)
    {
        if (_isInitializing || IsReady || _disposed) return;

        if (!File.Exists(modelPath))
        {
            Main.Logger.LogError($"[LocalAI] Model file not found: {modelPath}");
            return;
        }

        _isAndroid = OperatingSystem.IsAndroid();
        _isInitializing = true;

        if (_isAndroid)
            _initTask = Task.Run(() => InitializeAndroid(modelPath, threads, contextSize));
        else
            _initTask = Task.Run(() => InitializeWindows(modelPath, threads, contextSize));
    }

    public async Task<bool> WaitForReady(TimeSpan timeout)
    {
        if (IsReady) return true;
        if (_initTask == null) return false;
        try
        {
            await Task.WhenAny(_initTask, Task.Delay(timeout));
            return IsReady;
        }
        catch { return false; }
    }

    #endregion

    #region Translation (public API)

    public Task<string?> TranslateAsync(
        string prefix, string suffix, int maxTokens = 128, float temperature = 0.1f,
        CancellationToken ct = default)
    {
        if (!IsReady || _disposed) return Task.FromResult<string?>(null);

        if (_isAndroid)
            return Task.Run(() => GenerateAndroid(prefix, suffix, maxTokens), ct);

        return GenerateWindows(prefix + suffix, maxTokens, temperature, ct);
    }

    /// <summary>
    /// Prime the KV cache by evaluating the full system prompt + terminology guide
    /// with a simple translation. This reduces the first real translation from ~28s
    /// to ~0.3s because the system prompt is already cached.
    /// </summary>
    /// <param name="sourceLang">Source language for the warmup prompt.</param>
    /// <param name="targetLang">Target language for the warmup prompt.</param>
    public async Task WarmupAsync(SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        if (!IsReady || _disposed || IsWarmingUp || IsCacheReady) return;

        IsWarmingUp = true;
        try
        {
            string sourceLanguage = Translator.GetClientLanguageName(sourceLang);
            string targetLanguage = Translator.GetClientLanguageName(targetLang);
            string? sourceGuide = SlangCache.Get(sourceLang);
            string? targetGuide = SlangCache.Get(targetLang);

            string prefix = PromptBuilder.BuildLocalModelPrefix(
                sourceLanguage, targetLanguage, sourceGuide, targetGuide);
            string suffix = PromptBuilder.BuildLocalModelSuffix("hello");

            Main.Logger.LogInfo($"[LocalAI] Warming up prompt cache for {sourceLanguage}→{targetLanguage}...");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? result = await TranslateAsync(prefix, suffix, maxTokens: 8, temperature: 0.0f, cts.Token);

            IsCacheReady = true;
            Main.Logger.LogInfo(
                result != null
                    ? $"[LocalAI] Warmup complete — prompt cache primed for {sourceLanguage}→{targetLanguage}"
                    : "[LocalAI] Warmup returned null, but prompt cache should still be primed");
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[LocalAI] Warmup failed (non-critical): {ex.Message}");
        }
        finally
        {
            IsWarmingUp = false;
        }
    }

    #endregion

    #region Windows — ServerProcessManager + HTTP

    private async Task InitializeWindows(string modelPath, int threads, int contextSize)
    {
        try
        {
            await NativeLibraryManager.EnsureAsync();

            string? exePath = NativeLibraryManager.ServerExePath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                throw new FileNotFoundException("llama-server.exe not found after EnsureAsync");

            _server = new ServerProcessManager();
            await _server.StartAsync(exePath, modelPath, threads, contextSize);
            IsReady = true;
        }
        catch (Exception ex)
        {
            Main.Logger.LogError($"[LocalAI] Windows init failed: {ex.Message}");
            CleanupWindows();
        }
        finally { _isInitializing = false; }
    }

    private async Task<string?> GenerateWindows(
        string prompt, int maxTokens, float temperature, CancellationToken ct)
    {
        if (_server?.Client == null || !_server.IsRunning)
            return null;

        await _inferenceLock.WaitAsync(ct);
        try
        {
            if (_disposed || _server?.Client == null) return null;

            var payload = new
            {
                prompt,
                n_predict = maxTokens,
                temperature,
                stop = new[] { "<|im_end|>", "<|im_start|>" }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using var response = await _server.Client.PostAsync("completion", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                Main.Logger.LogWarning(
                    $"[LocalAI] HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
                return null;
            }

            string responseJson = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            string? result = doc.RootElement.GetProperty("content").GetString();

            if (result != null)
            {
                result = result
                    .Replace("<|im_end|>", "")
                    .Replace("<|im_start|>", "")
                    .Trim();
            }

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[LocalAI] Windows inference error: {ex.Message}");
            if (!_server!.IsRunning)
            {
                Main.Logger.LogError("[LocalAI] Server died during inference.");
                CleanupWindows();
            }
            return null;
        }
        finally { _inferenceLock.Release(); }
    }

    private void CleanupWindows()
    {
        IsReady = false;
        _server?.Dispose();
        _server = null;
    }

    #endregion

    #region Android — P/Invoke in-process

    private void InitializeAndroid(string modelPath, int threads, int contextSize)
    {
        try
        {
            Main.Logger.LogInfo($"[LocalAI] Loading model: {modelPath}");
            NativeLibraryManager.EnsureAsync().GetAwaiter().GetResult();
            LlamaCppApi.SetLogCallback();

            var mParams = LlamaCppApi.llama_model_default_params();
            mParams.n_gpu_layers = 0;
            mParams.load_mode = LlamaCppApi.LLAMA_LOAD_MODE_NONE;

            _model = LlamaCppApi.llama_model_load_from_file(modelPath, mParams);
            if (_model == IntPtr.Zero)
                throw new InvalidOperationException("llama_model_load_from_file returned NULL");

            _vocab = LlamaCppApi.llama_model_get_vocab(_model);
            _vocabSize = LlamaCppApi.llama_vocab_n_tokens(_vocab);
            _eosToken = LlamaCppApi.llama_vocab_eos(_vocab);

            _batchSize = 512;

            var cParams = LlamaCppApi.llama_context_default_params();
            cParams.n_ctx = (uint)contextSize;
            cParams.n_threads = threads;
            cParams.n_threads_batch = threads;
            cParams.n_batch = (uint)_batchSize;

            _cParams = cParams;
            _ctx = LlamaCppApi.llama_init_from_model(_model, cParams);
            if (_ctx == IntPtr.Zero)
                throw new InvalidOperationException("llama_init_from_model returned NULL");

            _contextSize = contextSize;
            IsReady = true;
            Main.Logger.LogInfo(
                $"[LocalAI] Model ready. vocab={_vocabSize} eos={_eosToken} " +
                $"ctx={contextSize} threads={threads}");
        }
        catch (Exception ex)
        {
            Main.Logger.LogError($"[LocalAI] Android init failed: {ex.Message}");
            ReleaseAndroidResources();
        }
        finally { _isInitializing = false; }
    }

    private string? GenerateAndroid(string prefix, string suffix, int maxTokens)
    {
        if (!IsReady || _disposed) return null;

        _inferenceLock.Wait();
        IntPtr pieceBuf = IntPtr.Zero;
        IntPtr singleToken = IntPtr.Zero;
        try
        {
            if (_disposed) return null;

            bool havePrefixState = _prefixState != null &&
                                   string.Equals(prefix, _cachedPrefix, StringComparison.Ordinal);

            if (havePrefixState)
            {
               
                if (RestoreState(_prefixState!))
                {
                    int nSuffix = TokenizeAndDecode(suffix, addSpecial: false);
                    if (nSuffix < 0) return null;
                }
                else
                {
                    Main.Logger.LogWarning("[LocalAI] State restore failed; full re-decode");
                    ReinitContext();
                    int nFull = TokenizeAndDecode(prefix + suffix, addSpecial: true);
                    if (nFull <= 0) return null;
                }
            }
            else
            {
                ReinitContext();
                int nPrefix = TokenizeAndDecode(prefix, addSpecial: true);
                if (nPrefix <= 0) return null;

                byte[]? snap = SnapshotState();
                if (snap != null)
                {
                    _prefixState = snap;
                    _cachedPrefix = prefix;
                    Main.Logger.LogInfo(
                        $"[LocalAI] Prompt-cache snapshot: {snap.Length} bytes for prefix ({nPrefix} tokens)");
                }

                int nSuffix = TokenizeAndDecode(suffix, addSpecial: false);
                if (nSuffix < 0) return null;
            }
            
            var sb = new StringBuilder(256);
            pieceBuf = Marshal.AllocHGlobal(256);
            singleToken = Marshal.AllocHGlobal(sizeof(int));
            int generated = 0;
            int prevToken = -1;

            while (generated < maxTokens)
            {
                // -1 = logits of the last token in the batch (the one we want to sample from).
                IntPtr logits = LlamaCppApi.llama_get_logits_ith(_ctx, -1);
                if (logits == IntPtr.Zero) break;

                int best = 0;
                float bestScore = float.NegativeInfinity;
                for (int i = 0; i < _vocabSize; i++)
                {
                    float value = BitConverter.Int32BitsToSingle(
                        Marshal.ReadInt32(logits, i * sizeof(float)));
                    if (value > bestScore) { bestScore = value; best = i; }
                }

                if (best == _eosToken) break;
                if (best == prevToken && generated > 2) break;
                prevToken = best;

                int nBytes = LlamaCppApi.llama_token_to_piece(
                    _vocab, best, pieceBuf, 256, 0, special: false);
                if (nBytes < 0)
                {
                    int required = -nBytes + 1;
                    Marshal.FreeHGlobal(pieceBuf);
                    pieceBuf = Marshal.AllocHGlobal(required);
                    nBytes = LlamaCppApi.llama_token_to_piece(
                        _vocab, best, pieceBuf, required, 0, special: false);
                }
                if (nBytes > 0)
                {
                    byte[] pieceBytes = new byte[nBytes];
                    Marshal.Copy(pieceBuf, pieceBytes, 0, nBytes);
                    string piece = Encoding.UTF8.GetString(pieceBytes);
                    sb.Append(piece);
                    if (piece.Contains("<|im_end|>") || piece.Contains("<|im_start|>"))
                        break;
                }

                generated++;
                Marshal.WriteInt32(singleToken, best);
                var batch = LlamaCppApi.llama_batch_get_one(singleToken, 1);
                if (LlamaCppApi.llama_decode(_ctx, batch) != 0) break;
            }

            var result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[LocalAI] Android inference error: {ex.Message}");
            return null;
        }
        finally
        {
            if (pieceBuf != IntPtr.Zero) Marshal.FreeHGlobal(pieceBuf);
            if (singleToken != IntPtr.Zero) Marshal.FreeHGlobal(singleToken);
            _inferenceLock.Release();
        }
    }
    
    private void ReinitContext()
    {
        LlamaCppApi.llama_free(_ctx);
        _ctx = LlamaCppApi.llama_init_from_model(_model, _cParams);
        if (_ctx == IntPtr.Zero)
            throw new InvalidOperationException("[LocalAI] Context re-init failed");
    }

    /// <summary>
    /// Serialize the current context state (KV cache + recurrent hidden state) into a byte array.
    /// Returns null if the state size looks bogus or the read is short.
    /// </summary>
    private byte[]? SnapshotState()
    {
        UIntPtr size = LlamaCppApi.llama_state_get_size(_ctx);
        ulong bytes = size.ToUInt64();
        if (bytes == 0 || bytes > (64UL << 20)) // sanity: 1 byte .. 64 MB
        {
            Main.Logger.LogWarning($"[LocalAI] Unexpected state size {bytes}; skipping cache");
            return null;
        }

        int n = (int)bytes;
        byte[] buf = new byte[n];
        IntPtr ptr = Marshal.AllocHGlobal(n);
        try
        {
            UIntPtr written = LlamaCppApi.llama_state_get_data(_ctx, ptr, size);
            if (written != size)
            {
                Main.Logger.LogWarning($"[LocalAI] State get_data wrote {written} != {size}");
                return null;
            }
            Marshal.Copy(ptr, buf, 0, n);
            return buf;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
    
    private bool RestoreState(byte[] state)
    {
        IntPtr ptr = Marshal.AllocHGlobal(state.Length);
        try
        {
            Marshal.Copy(state, 0, ptr, state.Length);
            UIntPtr read = LlamaCppApi.llama_state_set_data(_ctx, ptr, (UIntPtr)state.Length);
            return read == (UIntPtr)state.Length;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
    
    private int TokenizeAndDecode(string text, bool addSpecial)
    {
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(text);
        int utf8Len = utf8Bytes.Length;
        IntPtr utf8 = Marshal.AllocHGlobal(utf8Len + 1);
        IntPtr tokens = IntPtr.Zero;
        try
        {
            Marshal.Copy(utf8Bytes, 0, utf8, utf8Len);
            Marshal.WriteByte(utf8, utf8Len, 0);
            
            int maxTokens = Math.Max(_contextSize, 1024);
            tokens = Marshal.AllocHGlobal(maxTokens * sizeof(int));
            int n = LlamaCppApi.llama_tokenize(
                _vocab, utf8, utf8Len, tokens, maxTokens,
                add_special: addSpecial, parse_special: true);

            if (n < 0)
            {
                Main.Logger.LogWarning(
                    $"[LocalAI] Tokenisation failed — text needs more than {maxTokens} tokens " +
                    $"(context={_contextSize})");
                return -1;
            }

            DecodeTokensInChunks(tokens, n);
            return n;
        }
        finally
        {
            if (utf8 != IntPtr.Zero) Marshal.FreeHGlobal(utf8);
            if (tokens != IntPtr.Zero) Marshal.FreeHGlobal(tokens);
        }
    }
    
    private void DecodeTokensInChunks(IntPtr tokens, int count)
    {
        int nDone = 0;
        while (nDone < count)
        {
            int nChunk = Math.Min(_batchSize, count - nDone);
            IntPtr chunkTokens = IntPtr.Add(tokens, nDone * sizeof(int));
            var chunk = LlamaCppApi.llama_batch_get_one(chunkTokens, nChunk);
            if (LlamaCppApi.llama_decode(_ctx, chunk) != 0)
            {
                throw new InvalidOperationException(
                    $"[LocalAI] Prompt decode failed at token {nDone}/{count}");
            }
            nDone += nChunk;
        }
    }

    private void ReleaseAndroidResources()
    {
        if (_ctx != IntPtr.Zero) { LlamaCppApi.llama_free(_ctx); _ctx = IntPtr.Zero; }
        if (_model != IntPtr.Zero) { LlamaCppApi.llama_model_free(_model); _model = IntPtr.Zero; }
        _vocab = IntPtr.Zero;
        _prefixState = null;
        _cachedPrefix = null;
        IsReady = false;
    }

    #endregion

    #region Cleanup

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _inferenceLock.Wait();
        try
        {
            if (_isAndroid) ReleaseAndroidResources();
            else CleanupWindows();
        }
        finally { _inferenceLock.Release(); }

        _inferenceLock.Dispose();
    }

    #endregion
}
