using AmongUs.GameOptions;

namespace TranslateUs.Core;

/// <summary>
/// Resolves the room's spoken language. Uses manual config override if set;
/// otherwise falls back to the host's Keywords setting.
/// </summary>
public static class LanguageDetector
{
    /// <summary>
    /// Resolves the room language. Checks RoomLanguage config first,
    /// then falls back to GameHostOptions.Keywords.
    /// </summary>
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

        // Auto: use host Keywords
        var keywords = GameOptionsManager.Instance.GameHostOptions.Keywords;
        var detected = Translator.GameKeywordToSupportedLangs(keywords);
        Main.Logger.LogInfo($"[LangDetect] Auto-detected room language from Keywords ({keywords}): {detected}");
        return detected;
    }

    /// <summary>
    /// Parses a human-readable language name to SupportedLangs.
    /// Accepts various common forms for user convenience.
    /// </summary>
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
