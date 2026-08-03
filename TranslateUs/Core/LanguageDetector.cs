using AmongUs.Data;
using AmongUs.GameOptions;

namespace TranslateUs.Core;


public static class LanguageDetector
{
    public static SupportedLangs ResolveRoomLanguage()
    {
        string configured = Main.RoomLanguage.Value.Trim();

        if (!string.IsNullOrWhiteSpace(configured)
            && !configured.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = ParseLanguageName(configured);
            if (parsed != null)
            {
                Main.Logger.LogInfo($"[LangDetect] Using configured room language: {parsed.Value}");
                return parsed.Value;
            }
            Main.Logger.LogWarning($"[LangDetect] Unrecognized RoomLanguage '{configured}', falling back to Auto.");
        }

        try
        {
            var instance = GameOptionsManager.Instance;
            if (instance == null)
            {
                Main.Logger.LogWarning("[DIAG] LanguageDetector: GameOptionsManager.Instance is null (not in game/splash), falling back to client language");
                return DataManager.Settings.Language.CurrentLanguage;
            }

            var options = instance.CurrentGameOptions;
            if (options == null)
            {
                Main.Logger.LogWarning("[DIAG] LanguageDetector: CurrentGameOptions is null (not in game/splash), falling back to client language");
                return DataManager.Settings.Language.CurrentLanguage;
            }

            var keywords = options.Keywords;
            Main.Logger.LogInfo($"[DIAG] LanguageDetector: Keywords={keywords}");
            var detected = Translator.GameKeywordToSupportedLangs(keywords);
            Main.Logger.LogInfo($"[DIAG] LanguageDetector: Resolved roomLang={detected} (from Keywords={keywords})");
            return detected;
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[DIAG] LanguageDetector: Error reading game options ({ex.GetType().Name}: {ex.Message}), falling back to client language");
            return DataManager.Settings.Language.CurrentLanguage;
        }
    }

    private static SupportedLangs? ParseLanguageName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "english" or "en" => SupportedLangs.English,
            "schinese" or "simplified chinese" or "chinese" or "zh-cn" or "zh" => SupportedLangs.SChinese,
            "tchinese" or "traditional chinese" or "zh-tw" => SupportedLangs.TChinese,
            "japanese" or "ja" or "jp" => SupportedLangs.Japanese,
            "korean" or "ko" or "kr" => SupportedLangs.Korean,
            "spanish" or "es" or "spain" => SupportedLangs.Spanish,
            "latam" or "latin american spanish" or "es-la" => SupportedLangs.Latam,
            "russian" or "ru" => SupportedLangs.Russian,
            "portuguese" or "pt" => SupportedLangs.Portuguese,
            "brazilian" or "brazilian portuguese" or "pt-br" => SupportedLangs.Brazilian,
            "french" or "fr" => SupportedLangs.French,
            "italian" or "it" => SupportedLangs.Italian,
            "german" or "de" => SupportedLangs.German,
            "dutch" or "nl" => SupportedLangs.Dutch,
            "filipino" or "tl" => SupportedLangs.Filipino,
            "irish" or "ga" => SupportedLangs.Irish,
            _ => null
        };
    }
}
