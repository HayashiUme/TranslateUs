using System.Runtime.InteropServices;

namespace TranslateUs.Native;

/// <summary>
/// P/Invoke declarations for llama.cpp C API.
/// Targets llama.cpp b4980+. Struct layouts match the C ABI of this version.
/// </summary>
public static unsafe class LlamaCppApi
{
    #region Constants

    public const string LibName = "llama";

    #endregion

    #region Structs

    /// <summary>llama_model_params — default via llama_model_default_params().</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct llama_model_params
    {
        public IntPtr devices;             // llama_device**
        public int n_gpu_layers;
        public int split_mode;
        public int main_gpu;
        public IntPtr tensor_split;        // float*
        public IntPtr rpc_servers;         // char**
        public IntPtr progress_callback;
        public IntPtr progress_callback_user_data;
        public IntPtr kv_overrides;
        [MarshalAs(UnmanagedType.I1)] public bool vocab_only;
        [MarshalAs(UnmanagedType.I1)] public bool use_mmap;
        [MarshalAs(UnmanagedType.I1)] public bool use_mlock;
        [MarshalAs(UnmanagedType.I1)] public bool check_tensors;
    }

    /// <summary>llama_context_params — default via llama_context_default_params().</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct llama_context_params
    {
        public uint n_ctx;
        public uint n_batch;
        public uint n_ubatch;
        public uint n_seq_max;
        public int n_threads;
        public int n_threads_batch;
        public int rope_scaling_type;
        public int pooling_type;
        public float rope_freq_base;
        public float rope_freq_scale;
        public float yarn_ext_factor;
        public float yarn_attn_factor;
        public float yarn_beta_fast;
        public float yarn_beta_slow;
        public uint yarn_orig_ctx;
        public float defrag_thold;
        public ulong sparse_attn_holes;      // bitmask
        public IntPtr cb_eval;               // llama_eval_callback
        public IntPtr cb_eval_user_data;
        public int type_k;                   // GGML_TYPE
        public int type_v;
        // --- cache config ---
        public int cache_type_k;
        public int cache_type_v;
        // --- attention ---
        [MarshalAs(UnmanagedType.I1)] public bool offload_kqv;
        [MarshalAs(UnmanagedType.I1)] public bool flash_attn;
        [MarshalAs(UnmanagedType.I1)] public bool no_perf;
        // --- Abort callback ---
        public IntPtr abort_callback;
        public IntPtr abort_callback_user_data;
    }

    /// <summary>llama_batch — used via llama_batch_get_one().</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct llama_batch
    {
        public int n_tokens;
        public IntPtr token;       // llama_token*
        public IntPtr embd;        // float*
        public IntPtr pos;         // llama_pos*
        public IntPtr n_seq_id;    // int32_t*
        public IntPtr seq_id;      // llama_seq_id**
        public IntPtr logits;      // int8_t*
        // … the struct is larger but we only READ it, never create from scratch
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
    public static extern void llama_free_model(IntPtr model);

    #endregion

    #region Context

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern llama_context_params llama_context_default_params();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr llama_new_context_with_model(
        IntPtr model, llama_context_params @params);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void llama_free(IntPtr ctx);

    #endregion

    #region Model info

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_n_vocab(IntPtr model);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_n_ctx_train(IntPtr model);

    #endregion

    #region Tokenize

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_tokenize(
        IntPtr model,
        byte* text,
        int text_len,
        int* tokens,
        int n_tokens_max,
        [MarshalAs(UnmanagedType.I1)] bool add_bos,
        [MarshalAs(UnmanagedType.I1)] bool special);

    #endregion

    #region Batch

    /// <summary>Create a single-sequence batch (most common use-case).</summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern llama_batch llama_batch_get_one(
        int* tokens,
        int n_tokens);

    #endregion

    #region Decode

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_decode(IntPtr ctx, llama_batch batch);

    #endregion

    #region Logits & sampling

    /// <summary>Get pointer to logits for the i-th output.</summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float* llama_get_logits_ith(IntPtr ctx, int i);

    #endregion

    #region Token → string

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_token_to_piece(
        IntPtr model,
        int token,
        byte* buf,
        int length,
        int lstrip,
        [MarshalAs(UnmanagedType.I1)] bool special);

    #endregion

    #region Special tokens

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_token_eos(IntPtr model);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int llama_token_bos(IntPtr model);

    #endregion
}
