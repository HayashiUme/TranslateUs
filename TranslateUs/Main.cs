using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TranslateUs.Core;
#if WINDOWS
using ULMLoader.API;
#endif
using UnityEngine;

namespace TranslateUs
{
    [BepInPlugin("ume.transalte.us", "Translate Us", "1.3.0")]
#if WINDOWS
    [BepInDependency("ume.ulm.loader", BepInDependency.DependencyFlags.HardDependency)]
#endif
    public class Main : BasePlugin
    {
        public static ManualLogSource Logger = null!;
        public static Harmony Harmony = null!;

        /// <summary>
        /// API Key for the cloud AI translation service.
        /// </summary>
        public static ConfigEntry<string> ApiKey { get; private set; } = null!;

        /// <summary>
        /// API endpoint URL (OpenAI-compatible).
        /// </summary>
        public static ConfigEntry<string> ApiUrl { get; private set; } = null!;

        /// <summary>
        /// Model name to use for cloud AI translation.
        /// </summary>
        public static ConfigEntry<string> Model { get; private set; } = null!;

        /// <summary>
        /// Extra prompt appended to every translation request.
        /// </summary>
        public static ConfigEntry<string> ExtraPrompt { get; private set; } = null!;

        /// <summary>
        /// Manual override for the room's spoken language.
        /// "Auto" uses the host's Keywords setting.
        /// </summary>
        public static ConfigEntry<string> RoomLanguage { get; private set; } = null!;

        /// <summary>
        /// If true, your own chat bubble shows translated text.
        /// </summary>
        public static ConfigEntry<bool> TranslateOwnBubbles { get; private set; } = null!;

        /// <summary>
        /// Enable local GGUF model translation via LLamaSharp.
        /// When enabled, takes priority over all other backends.
        /// </summary>
        public static ConfigEntry<bool> UseLocalAI { get; private set; } = null!;

        /// <summary>
        /// Path to the .gguf model file. Leave empty to auto-download.
        /// </summary>
        public static ConfigEntry<string> LocalModelPath { get; private set; } = null!;

        /// <summary>
        /// Download URL for the GGUF model file.
        /// </summary>
        public static ConfigEntry<string> LocalModelUrl { get; private set; } = null!;

#if !WINDOWS
        public static ConfigEntry<string> NativeLibUrl { get; private set; } = null!;
#endif

        /// <summary>
        /// Maximum tokens to generate per translation request.
        /// </summary>
        public static ConfigEntry<int> MaxInferenceTokens { get; private set; } = null!;

        /// <summary>
        /// Temperature for inference (0.0-2.0). Lower = more deterministic.
        /// </summary>
        public static ConfigEntry<float> InferenceTemperature { get; private set; } = null!;

        /// <summary>
        /// CPU threads for local model inference.
        /// </summary>
        public static ConfigEntry<int> InferenceThreads { get; private set; } = null!;

        /// <summary>KV-cache context size for the local model.</summary>
        public static ConfigEntry<int> ContextSize { get; private set; } = null!;

        public static ConfigEntry<string> TranslationBackend { get; private set; } = null!;

        /// <summary>
        /// Currently active translation backend. Set once in Load().
        /// </summary>
        public static BackendType Backend { get; private set; }
        public static bool IsPaused { get; private set; }
        public static event Action<bool>? OnPauseChanged;
        public static bool IsAuthFix;
        public static bool IsLocalizeUs;

        public static void TogglePause()
        {
            IsPaused = !IsPaused;
            OnPauseChanged?.Invoke(IsPaused);
            Logger.LogInfo($"TranslateUs: Translation {(IsPaused ? "PAUSED" : "RESUMED")}");
        }

        public override void Load()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("TranslateUs");
            Logger.LogInfo("=== Loading Translate Us v1.3.0 ===");

            TranslationBackend = Config.Bind(
                "AI",
                "Backend",
                "Auto",
                "Which translation backend to use. Options:\n" +
                "  Auto    — LocalAI if enabled, otherwise CloudAI when available\n" +
                "  CloudAI — OpenAI-compatible cloud API (requires ApiKey)\n" +
                "  LocalAI — Local GGUF model via llama.cpp (privacy-first)");
            
            UseLocalAI = Config.Bind(
                "LocalAI",
                "UseLocalAI",
                false,
                "Enable local GGUF model translation (privacy-first, no network). " +
                "When enabled, takes priority over the cloud AI backend.");

            LocalModelPath = Config.Bind(
                "LocalAI",
                "LocalModelPath",
                "",
                "Absolute path to the .gguf model file. Leave empty to auto-download " +
                "to the plugin's models/ directory.");

            LocalModelUrl = Config.Bind(
                "LocalAI",
                "LocalModelUrl",
                "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
                "Download URL for the GGUF model file when LocalModelPath is empty.");

            MaxInferenceTokens = Config.Bind(
                "LocalAI",
                "MaxInferenceTokens",
                128,
                "Maximum tokens to generate per translation request.");

            InferenceTemperature = Config.Bind(
                "LocalAI",
                "InferenceTemperature",
                0.1f,
                "Temperature for inference (0.0-2.0). Lower values produce more deterministic translations.");

#if !WINDOWS
            NativeLibUrl = Config.Bind("LocalAI", "NativeLibUrl",
                "https://github.com/ggerganov/llama.cpp/releases/download",
                "Base URL for llama.cpp runtime downloads.");
            InferenceThreads = Config.Bind(
                "LocalAI",
                "InferenceThreads",
                2,
                "CPU threads for local model inference. Keep low (2-4) to avoid frame drops.");

            ContextSize = Config.Bind(
                "LocalAI",
                "ContextSize",
                4096,
                "KV-cache context size in tokens. Larger values allow longer prompts " +
                "(terminology guides + system prompt) but use more memory. 4096 is a safe default.");
#endif

            ApiKey = Config.Bind(
                "Cloud AI",
                "ApiKey",
                "",
                "API Key for AI translation. Required for CloudAI.");

            ApiUrl = Config.Bind(
                "Cloud AI",
                "ApiUrl",
                "https://open.bigmodel.cn/api/paas/v4/chat/completions",
                "API endpoint URL (OpenAI-compatible). Default is Zhipu AI (智谱) GLM-4-Flash.");

            Model = Config.Bind(
                "Cloud AI",
                "Model",
                "glm-4-flash",
                "Model name. Change this to use a different model (e.g. gpt-4o, claude-3-haiku, etc).");

            ExtraPrompt = Config.Bind(
                "Cloud AI",
                "ExtraPrompt",
                "",
                "Extra instructions appended to the translation prompt. " +
                "Use this to fine-tune translation behavior (e.g. 'Use informal tone', 'Keep names untranslated').");

            RoomLanguage = Config.Bind(
                "Translation",
                "RoomLanguage",
                "Auto",
                "Override the room's spoken language. 'Auto' uses the host's Keywords setting. " +
                "Set to a language name if Keywords are wrong (e.g. 'SChinese', 'English', 'Japanese').");

            TranslateOwnBubbles = Config.Bind(
                "Translation",
                "TranslateOwnBubbles",
                false,
                "If true, your own chat bubble shows translated text. If false (default), shows your original typed text.");
            
            bool isAndroid = OperatingSystem.IsAndroid();
            string choice = TranslationBackend.Value;

            if (choice == "LocalAI")
            {
                Backend = BackendType.LocalAI;
                Logger.LogInfo("Using LOCAL AI translation (GGUF model)");
                // SplashManagerPatch handles init during splash screen on all platforms
            }
            else if (choice == "CloudAI")
            {
                if (isAndroid)
                {
                    Logger.LogWarning("CloudAI is disabled on Android. Translation is disabled; use LocalAI instead.");
                    Backend = UseLocalAI.Value ? BackendType.LocalAI : BackendType.None;
                }
                else
                {
                    Backend = BackendType.CloudAI;
                    Logger.LogInfo($"Using CLOUD AI translation (Model: {Model.Value})");
                }
            }
            else
            {
                if (UseLocalAI.Value)
                {
                    Backend = BackendType.LocalAI;
                    Logger.LogInfo("Auto → LOCAL AI (UseLocalAI=true)");
                    // SplashManagerPatch handles init during splash screen on all platforms
                }
                else if (isAndroid)
                {
                    Backend = BackendType.None;
                    Logger.LogInfo("Auto → translation disabled on Android; enable LocalAI to translate offline");
                }
                else if (!string.IsNullOrWhiteSpace(ApiKey.Value) && ApiKey.Value != "0")
                {
                    Backend = BackendType.CloudAI;
                    Logger.LogInfo($"Auto → CloudAI (Model: {Model.Value})");
                }
                else
                {
                    Backend = BackendType.None;
                    Logger.LogInfo("Auto → translation disabled (no API key or LocalAI enabled)");
                }
            }

#if WINDOWS
            if (Backend == BackendType.LocalAI)
            {
                ULMLoader.API.LocalModel.Configure(new ModelRequest(
                    "qwen2.5-1.5b-instruct-q4_k_m",
                    LocalModelUrl.Value,
                    string.IsNullOrWhiteSpace(LocalModelPath.Value) ? null : LocalModelPath.Value));
            }
#endif

            Harmony = new Harmony("ume.transalte.us");
            Harmony.PatchAll();

            // Android retains its legacy in-process runtime cleanup.
            Application.add_quitting(new System.Action(OnGameQuit));

            Logger.LogInfo("=== Loaded Translate Us v1.3.0 ===");
        }

        private static void OnGameQuit()
        {
            try
            {
                Logger.LogInfo("[Main] Game quitting — cleaning up LocalAI...");
#if !WINDOWS
                LocalAiBackend.Instance.Dispose();
                Core.Manager.ServerProcessManager.KillAll();
#endif
                Logger.LogInfo("[Main] Cleanup complete.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Main] Cleanup error (ignored): {ex.Message}");
            }
        }

    }
}
