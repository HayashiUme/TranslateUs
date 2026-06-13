using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace TranslateUs
{
    [BepInPlugin("ume.transalte.us", "Translate Us", "1.0.0")]
    public class Main : BasePlugin
    {
        public static ManualLogSource Logger = null!;
        public static Harmony Harmony = null!;
        
        /// <summary>API Key for the translation service.</summary>
        public static ConfigEntry<string> ApiKey { get; private set; } = null!;
        /// <summary>API endpoint URL.</summary>
        public static ConfigEntry<string> ApiUrl { get; private set; } = null!;
        /// <summary>Model name to use for translation.</summary>
        public static ConfigEntry<string> Model { get; private set; } = null!;
        /// <summary>Extra prompt appended to every translation request.</summary>
        public static ConfigEntry<string> ExtraPrompt { get; private set; } = null!;

        public override void Load()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("TranslateUs");
            Logger.LogInfo("=== Loading Translate Us ===");

            ApiKey = Config.Bind(
                "AI",
                "ApiKey",
                "0",
                "API Key. Get one from your translation service provider.");

            ApiUrl = Config.Bind(
                "AI",
                "ApiUrl",
                "https://open.bigmodel.cn/api/paas/v4/chat/completions",
                "API endpoint URL. Default is Zhipu AI (智谱) GLM-4-Flash.");

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

            Harmony = new Harmony("ume.transalte.us");
            Harmony.PatchAll();
            Logger.LogInfo("=== Loaded Translate Us ===");
        }
    }
}
