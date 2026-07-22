using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace TranslateUs.Core;

public static class TerminologyLoader
{
    private static readonly Dictionary<SupportedLangs, string?> Cache = new();
    private static readonly object Lock = new();
    
    public static string? Load(SupportedLangs targetLang)
    {
        // Check cache first
        lock (Lock)
        {
            if (Cache.TryGetValue(targetLang, out var cached))
                return cached;
        }

        string resourcePath = LangToResourcePath(targetLang);
        string? result = null;

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourcePath);

            if (stream != null)
            {
                var doc = XDocument.Load(stream);
                result = FormatTerminology(doc);
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning(
                $"TerminologyLoader: Failed to load {resourcePath}: {ex.Message}");
        }

        lock (Lock)
        {
            Cache[targetLang] = result;
        }

        if (result != null)
            Main.Logger.LogInfo(
                $"TerminologyLoader: Loaded terminology guide for {targetLang} ({result.Length} chars)");

        return result;
    }
    
    private static string LangToResourcePath(SupportedLangs lang)
    {
        string locale = lang switch
        {
            SupportedLangs.English => "en",
            SupportedLangs.Spanish => "es",
            SupportedLangs.Korean => "ko",
            SupportedLangs.Russian => "ru",
            SupportedLangs.Portuguese => "pt",
            SupportedLangs.Brazilian => "pt-BR",
            SupportedLangs.Filipino => "tl",
            SupportedLangs.French => "fr",
            SupportedLangs.Italian => "it",
            SupportedLangs.German => "de",
            SupportedLangs.Dutch => "nl",
            SupportedLangs.Japanese => "ja",
            SupportedLangs.Latam => "es-LA",
            SupportedLangs.Irish => "ga",
            SupportedLangs.SChinese => "zh-CN",
            SupportedLangs.TChinese => "zh-TW",
            _ => "en"
        };

        return $"TranslateUs.Resources.Terminology.Terminology_{locale}.xml";
    }
    
    private static string FormatTerminology(XDocument doc)
    {
        var sb = new StringBuilder();

        foreach (var category in doc.Root!.Elements("category"))
        {
            var catName = category.Attribute("name")?.Value ?? "";
            sb.AppendLine($"# {catName}");

            foreach (var term in category.Elements("term"))
            {
                var en = term.Attribute("en")?.Value ?? "";
                var local = term.Attribute("local")?.Value ?? en;
                sb.AppendLine($"{en} = {local}");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
