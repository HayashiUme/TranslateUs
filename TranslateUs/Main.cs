using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace TranslateUs
{
    [BepInPlugin("ume.transalte.us", "Translate Us", "1.2.1")]
    public class Main : BasePlugin
    {
        public static ManualLogSource Logger = null!;
        public static Harmony Harmony = null!;

        /// <summary>
        /// API Key for the AI translation service. Leave empty to use Google Translate.
        /// </summary>
        public static ConfigEntry<string> ApiKey { get; private set; } = null!;
        
        /// <summary>
        /// API endpoint URL (OpenAI-compatible).
        /// </summary>
        public static ConfigEntry<string> ApiUrl { get; private set; } = null!;
        
        /// <summary>
        /// Model name to use for AI translation.
        /// </summary>
        public static ConfigEntry<string> Model { get; private set; } = null!;
        
        /// <summary>
        /// Extra prompt appended to every translation request.
        /// </summary>
        public static ConfigEntry<string> ExtraPrompt { get; private set; } = null!;
        
        /// <summary>
        /// Manual override for the room's spoken language.
        /// "Auto" uses the host's Keywords setting.
        /// Set to "SChinese", "English", etc. if the Keywords don't match the actual spoken language.
        /// </summary>
        public static ConfigEntry<string> RoomLanguage { get; private set; } = null!;
        
        /// <summary>
        /// If true, your own chat bubble shows translated text. If false, shows your original typed text.
        /// </summary>
        public static ConfigEntry<bool> TranslateOwnBubbles { get; private set; } = null!;
        
        /// <summary>
        /// Set once in Load(). True = use AI API, False = use Google Translate. Never changes at runtime.
        /// </summary>
        public static bool UseAI { get; private set; }
        public static bool IsPaused { get; private set; }
        public static event Action<bool>? OnPauseChanged;
        public static bool IsAuthFix;

        public static void TogglePause()
        {
            IsPaused = !IsPaused;
            OnPauseChanged?.Invoke(IsPaused);
            Logger.LogInfo($"TranslateUs: Translation {(IsPaused ? "PAUSED" : "RESUMED")}");
        }

        public override void Load()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("TranslateUs");
            Logger.LogInfo("=== Loading Translate Us v1.2.1 ===");

            ApiKey = Config.Bind(
                "AI",
                "ApiKey",
                "",
                "API Key for AI translation. Leave empty to use free Google Translate instead.");

            ApiUrl = Config.Bind(
                "AI",
                "ApiUrl",
                "https://open.bigmodel.cn/api/paas/v4/chat/completions",
                "API endpoint URL (OpenAI-compatible). Default is Zhipu AI (智谱) GLM-4-Flash.");

            Model = Config.Bind(
                "AI",
                "Model",
                "glm-4-flash",
                "Model name. Change this to use a different model (e.g. gpt-4o, claude-3-haiku, etc).");

            ExtraPrompt = Config.Bind(
                "AI",
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

            // For publish on Starlight, now disable using AI translation for Starlight (not Android) Players
            // if you use a Google-fix version of FusionCore play TranslateUs is okay
            bool isStarlight = OperatingSystem.IsAndroid() && IsAuthFix;
            UseAI = !isStarlight && !string.IsNullOrWhiteSpace(ApiKey.Value) && ApiKey.Value != "0";

            if (isStarlight)
                Logger.LogInfo("Starlight detected — AI translation disabled, using Google Translate only.");
            else
                Logger.LogInfo(UseAI
                    ? $"Using AI translation (Model: {Model.Value})"
                    : "Using Google Translate (no API key configured)");

            Harmony = new Harmony("ume.transalte.us");
            Harmony.PatchAll();
            Logger.LogInfo("=== Loaded Translate Us v1.2.1 ===");
        }
    }
}
