using System.Text;
using System.Text.Json;
using TranslateUs.Core.Manager;
using TranslateUs.Native;

namespace TranslateUs.Core;

/// <summary>
/// Local AI translation backend with dual-mode architecture:
///
///   Windows  → <see cref="ServerProcessManager"/> spawns llama-server.exe (64-bit)
///              then HTTP POST to /completion.
///   Android  → Direct P/Invoke into libllama.so (in-process).
///
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
    private IntPtr _ctx;
    private int _vocabSize;
    private int _eosToken;

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
        string prompt, int maxTokens = 128, float temperature = 0.1f,
        CancellationToken ct = default)
    {
        if (!IsReady || _disposed) return Task.FromResult<string?>(null);

        if (_isAndroid)
            return Task.Run(() => GenerateAndroid(prompt, maxTokens), ct);

        return GenerateWindows(prompt, maxTokens, temperature, ct);
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

            string warmupPrompt = PromptBuilder.BuildSinglePromptForLocalModel(
                "hello", sourceLanguage, targetLanguage, sourceGuide, targetGuide);

            Main.Logger.LogInfo($"[LocalAI] Warming up KV cache for {sourceLanguage}→{targetLanguage}...");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? result = await TranslateAsync(warmupPrompt, maxTokens: 8, temperature: 0.0f, cts.Token);

            IsCacheReady = true;
            Main.Logger.LogInfo(
                result != null
                    ? $"[LocalAI] Warmup complete — KV cache primed for {sourceLanguage}→{targetLanguage}"
                    : "[LocalAI] Warmup returned null, but KV cache should still be primed");
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

            var mParams = LlamaCppApi.llama_model_default_params();
            mParams.n_gpu_layers = 0;

            _model = LlamaCppApi.llama_model_load_from_file(modelPath, mParams);
            if (_model == IntPtr.Zero)
                throw new InvalidOperationException("llama_model_load_from_file returned NULL");

            _vocabSize = LlamaCppApi.llama_n_vocab(_model);
            _eosToken = LlamaCppApi.llama_token_eos(_model);

            var cParams = LlamaCppApi.llama_context_default_params();
            cParams.n_ctx = (uint)contextSize;
            cParams.n_threads = threads;
            cParams.n_threads_batch = threads;
            cParams.n_batch = 512;

            _ctx = LlamaCppApi.llama_new_context_with_model(_model, cParams);
            if (_ctx == IntPtr.Zero)
                throw new InvalidOperationException("llama_new_context_with_model returned NULL");

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

    private unsafe string? GenerateAndroid(string prompt, int maxTokens)
    {
        if (!IsReady || _disposed) return null;

        _inferenceLock.Wait();
        try
        {
            if (_disposed) return null;

            int utf8Len = Encoding.UTF8.GetByteCount(prompt);
            byte* utf8 = stackalloc byte[utf8Len + 1];
            fixed (char* pc = prompt)
                Encoding.UTF8.GetBytes(pc, prompt.Length, utf8, utf8Len);
            utf8[utf8Len] = 0;

            int maxPrompt = 1024;
            int* promptTokens = stackalloc int[maxPrompt];
            int nPrompt = LlamaCppApi.llama_tokenize(
                _model, utf8, utf8Len, promptTokens, maxPrompt,
                add_bos: true, special: true);

            if (nPrompt < 0)
            {
                Main.Logger.LogWarning("[LocalAI] Tokenisation failed — prompt too long");
                return null;
            }

            var batch = LlamaCppApi.llama_batch_get_one(promptTokens, nPrompt);
            if (LlamaCppApi.llama_decode(_ctx, batch) != 0)
            {
                Main.Logger.LogWarning("[LocalAI] Prompt decode failed");
                return null;
            }

            var sb = new StringBuilder(256);
            byte* pieceBuf = stackalloc byte[64];
            int generated = 0;
            int prevToken = -1;

            while (generated < maxTokens)
            {
                float* logits = LlamaCppApi.llama_get_logits_ith(_ctx, 0);
                if (logits == null) break;

                int best = 0;
                float bestScore = float.NegativeInfinity;
                for (int i = 0; i < _vocabSize; i++)
                    if (logits[i] > bestScore) { bestScore = logits[i]; best = i; }

                if (best == _eosToken) break;
                if (best == prevToken && generated > 2) break;
                prevToken = best;

                int nBytes = LlamaCppApi.llama_token_to_piece(
                    _model, best, pieceBuf, 64, 0, special: false);
                if (nBytes > 0)
                {
                    string piece = Encoding.UTF8.GetString(pieceBuf, nBytes);
                    sb.Append(piece);
                    if (piece.Contains("<|im_end|>") || piece.Contains("<|im_start|>"))
                        break;
                }

                generated++;
                batch = LlamaCppApi.llama_batch_get_one(&best, 1);
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
        finally { _inferenceLock.Release(); }
    }

    private void ReleaseAndroidResources()
    {
        if (_ctx != IntPtr.Zero) { LlamaCppApi.llama_free(_ctx); _ctx = IntPtr.Zero; }
        if (_model != IntPtr.Zero) { LlamaCppApi.llama_free_model(_model); _model = IntPtr.Zero; }
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
