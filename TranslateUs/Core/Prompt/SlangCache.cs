namespace TranslateUs.Core;

/// <summary>
/// Caches pre-baked terminology/glossary guides per target language.
/// Guides are loaded from embedded XML resources via TerminologyLoader.
/// Previously this used runtime AI generation — now replaced with static XML data.
/// </summary>
public static class SlangCache
{
    private static readonly Dictionary<SupportedLangs, string?> Cache = new();
    public static string? Get(SupportedLangs targetLang)
    {
        if (Cache.TryGetValue(targetLang, out var cached))
            return cached;

        var guide = TerminologyLoader.Load(targetLang);
        Cache[targetLang] = guide;
        return guide;
    }
}
