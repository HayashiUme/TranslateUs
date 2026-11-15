using System.Runtime.InteropServices;

namespace TranslateUs.Native;

/// <summary>
/// P/Invoke declarations for llama.cpp C API.
///
/// Legacy Android P/Invoke bindings; Windows builds delegate the runtime to ULMLoader.
/// Struct layouts and signatures MUST match this exact build — the API changed substantially
/// from older tags, and a mismatch here is silent (it corrupts memory / returns garbage):
/// </summary>
public static class LlamaCppApi
{
    #region Constants

    public const string LibName = "llama";

    // enum llama_load_mode
    public const int LLAMA_LOAD_MODE_NONE      = 0; // no special loading mode (read into RAM, no mmap)
    public const int LLAMA_LOAD_MODE_MMAP      = 1; // memory map the model
    public const int LLAMA_LOAD_MODE_MLOCK     = 2; // mmap + mlock
    public const int LLAMA_LOAD_MODE_DIRECT_IO = 3; // direct I/O

    // enum llama_split_mode
    public const int LLAMA_SPLIT_MODE_NONE   = 0;
    public const int LLAMA_SPLIT_MODE_LAYER  = 1;
    public const int LLAMA_SPLIT_MODE_ROW    = 2;
    public const int LLAMA_SPLIT_MODE_TENSOR = 3;

    #endregion

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct llama_model_params
    {
        public IntPtr devices;               // ggml_backend_dev_t*
        public IntPtr tensor_buft_overrides; // llama_model_tensor_buft_override*
        public int n_gpu_layers;             // -1 = all layers, 0 = CPU-only
        public int split_mode;               // enum llama_split_mode
        public int load_mode;                // enum llama_load_mode
        public int main_gpu;
        public IntPtr tensor_split;          // const float*
        public IntPtr progress_callback;
        public IntPtr progress_callback_user_data;
        public IntPtr kv_overrides;
        [MarshalAs(UnmanagedType.I1)] public bool vocab_only;
        [MarshalAs(UnmanagedType.I1)] public bool check_tensors;
        [MarshalAs(UnmanagedType.I1)] public bool use_extra_bufts;
        [MarshalAs(UnmanagedType.I1)] public bool no_host;
        [MarshalAs(UnmanagedType.I1)] public bool no_alloc;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct llama_context_params
    {
        public uint n_ctx;
        public uint n_batch;
        public uint n_ubatch;
        public uint n_seq_max;
        public uint n_rs_seq;
        public uint n_outputs_max;
        public int n_threads;
        public int n_threads_batch;

        public int ctx_type;
        public int rope_scaling_type;
        public int pooling_type;
        public int attention_type;
        public int flash_attn_type;

        public float rope_freq_base;
        public float rope_freq_scale;
        public float yarn_ext_factor;
        public float yarn_attn_factor;
        public float yarn_beta_fast;
        public float yarn_beta_slow;
        public uint yarn_orig_ctx;
        public float defrag_thold;

        public IntPtr cb_eval; 
        public IntPtr cb_eval_user_data;
        public int type_k;
        public int type_v;

        public IntPtr abort_callback;
        public IntPtr abort_callback_data;

        [MarshalAs(UnmanagedType.I1)] public bool embeddings;
        [MarshalAs(UnmanagedType.I1)] public bool offload_kqv;
        [MarshalAs(UnmanagedType.I1)] public bool no_perf;
        [MarshalAs(UnmanagedType.I1)] public bool op_offload;
        [MarshalAs(UnmanagedType.I1)] public bool swa_full;
        [MarshalAs(UnmanagedType.I1)] public bool kv_unified;

        public IntPtr samplers;
        public UIntPtr n_samplers;
        public IntPtr ctx_other;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct llama_batch
    {
        public int n_tokens;
        public IntPtr token;
        public IntPtr embd;
        public IntPtr pos;
        public IntPtr n_seq_id;
        public IntPtr seq_id;
        public IntPtr logits;
    }

    #endregion

    #region Model

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern llama_model_params llama_model_default_params();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_model_load_from_file(
        [MarshalAs(UnmanagedType.LPStr)] string path_model,
        llama_model_params @params);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_model_free(IntPtr model);
    
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_model_get_vocab(IntPtr model);

    #endregion

    #region GGML backend loading

    /// <summary>
    /// Load a single GGML backend from the given absolute .so path and register it
    /// (dlopens the file and calls its <c>ggml_backend_init</c>). Returns true on success.
    /// Exported by libggml.so, not libllama.so.
    /// </summary>
    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool ggml_backend_load(
        [MarshalAs(UnmanagedType.LPStr)] string path);

    /// <summary>
    /// Load every backend from the <c>libggml-*.so</c> files found next to libggml.so.
    /// b10142 no longer autoload the CPU backend inside <c>llama_model_load_from_file</c>.
    /// On Android the directory auto-detection can fail, so prefer <see cref="ggml_backend_load"/>
    /// with an explicit absolute path instead.
    /// </summary>
    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ggml_backend_load_all();

    #endregion

    #region Context

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern llama_context_params llama_context_default_params();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_init_from_model(IntPtr model, llama_context_params @params);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_free(IntPtr ctx);

    #endregion

    #region Memory (KV cache / recurrent state)
    
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_memory_clear(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.I1)] bool clear);
    
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_memory_seq_rm(IntPtr ctx, int seq_id, int p0, int p1);

    #endregion

    #region State save/restore
    
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr llama_state_get_size(IntPtr ctx);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr llama_state_get_data(IntPtr ctx, IntPtr dst, UIntPtr size);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr llama_state_set_data(IntPtr ctx, IntPtr src, UIntPtr size);

    #endregion

    #region Vocab / token info

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_n_tokens(IntPtr vocab);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_eos(IntPtr vocab);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_vocab_bos(IntPtr vocab);

    #endregion

    #region Tokenize

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_tokenize(
        IntPtr vocab,
        IntPtr text,
        int text_len,
        IntPtr tokens,
        int n_tokens_max,
        [MarshalAs(UnmanagedType.I1)] bool add_special,
        [MarshalAs(UnmanagedType.I1)] bool parse_special);

    #endregion

    #region Batch

    /// <summary>Create a single-sequence batch (most common use-case).</summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern llama_batch llama_batch_get_one(IntPtr tokens, int n_tokens);

    #endregion

    #region Decode

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_decode(IntPtr ctx, llama_batch batch);

    #endregion

    #region Logits & sampling

    /// <summary>Get pointer to logits for the i-th output.</summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_get_logits_ith(IntPtr ctx, int i);

    #endregion

    #region Token → string

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_token_to_piece(
        IntPtr vocab,
        int token,
        IntPtr buf,
        int length,
        int lstrip,
        [MarshalAs(UnmanagedType.I1)] bool special);

    #endregion

    #region Logging

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ggml_log_callback(int level, IntPtr text, IntPtr user_data);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void llama_log_set(ggml_log_callback callback, IntPtr user_data);

    // Kept as a static field so the delegate is never GC'd while llama.cpp holds the pointer.
    private static ggml_log_callback? _logCallback;
    
    public static void SetLogCallback()
    {
        if (_logCallback != null) return;
        _logCallback = OnLlamaLog;
        llama_log_set(_logCallback, IntPtr.Zero);
    }

    private static void OnLlamaLog(int level, IntPtr text, IntPtr userData)
    {
        string msg = Marshal.PtrToStringAnsi(text) ?? "";
        if (string.IsNullOrWhiteSpace(msg)) return;
        // level: 0=none 1=debug 2=info 3=warn 4=error 5=cont
        if (level >= 3)
            Main.Logger.LogWarning($"[llama:{level}] {msg.TrimEnd()}");
        else
            Main.Logger.LogInfo($"[llama:{level}] {msg.TrimEnd()}");
    }

    #endregion
}
