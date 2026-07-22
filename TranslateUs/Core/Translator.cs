using System.Text;
using System.Text.Json;
using AmongUs.Data;
using AmongUs.GameOptions;

namespace TranslateUs.Core;

public class Translator
{
    #region Public API
    
    public static async Task<(string original, string translated)> TranslateToRoomLanguage(
        string text, SupportedLangs roomLang)
        => await TranslateInternal(text, roomLang);
    
    public static async Task<(string original, string translated)> TranslateToMyLanguage(
        string text, SupportedLangs myLang)
        => await TranslateInternal(text, myLang);
    
    public static async Task<List<(string original, string translated)>> BatchTranslateToMyLanguage(
        List<string> messages, SupportedLangs myLang)
        => await BatchTranslateInternal(messages, myLang);

    #endregion

    #region Internal translation dispatch

    private static async Task<(string original, string translated)> TranslateInternal(
        string originalMessage, SupportedLangs targetLang)
    {
        string diagTargetLang = GetClientLanguageName(targetLang);
        Main.Logger.LogInfo($"[DIAG] Translate: TargetLang={diagTargetLang}, UseAI={Main.UseAI}, IsPaused={Main.IsPaused}, Input=\"{originalMessage}\"");

        if (Main.IsPaused)
            return (originalMessage, originalMessage);

        if (Main.UseAI)
        {
            try
            {
                string result = await TranslateViaAI(originalMessage, targetLang);
                return (originalMessage, result);
            }
            catch (Exception ex)
            {
                Main.Logger.LogWarning($"TranslateUs: AI translation failed: {ex.Message}");
                return (originalMessage, originalMessage);
            }
        }
        else
        {
            try
            {
                string result = await TranslateViaGoogle(originalMessage, targetLang);
                return (originalMessage, result);
            }
            catch (Exception ex)
            {
                Main.Logger.LogError($"TranslateUs: Google translation failed: {ex.Message}");
                return (originalMessage, originalMessage);
            }
        }
    }

    private static async Task<List<(string original, string translated)>> BatchTranslateInternal(
        List<string> messages, SupportedLangs targetLang)
    {
        if (messages.Count == 0)
            return new List<(string, string)>();

        string diagTargetLang = GetClientLanguageName(targetLang);
        Main.Logger.LogInfo($"[DIAG] BatchTranslate: TargetLang={diagTargetLang}, Count={messages.Count}, UseAI={Main.UseAI}");

        if (Main.UseAI)
        {
            try
            {
                return await BatchTranslateViaAI(messages, targetLang);
            }
            catch (Exception ex)
            {
                Main.Logger.LogWarning($"TranslateUs: AI batch translation failed: {ex.Message}");
                return messages.Select(m => (m, m)).ToList();
            }
        }
        else
        {
            try
            {
                return await BatchTranslateViaGoogle(messages, targetLang);
            }
            catch (Exception ex)
            {
                Main.Logger.LogError($"TranslateUs: Google batch translation failed: {ex.Message}");
                return messages.Select(m => (m, m)).ToList();
            }
        }
    }

    #endregion

    #region AI Translation

    /// <summary>Get the pre-baked XML terminology guide for a target language.</summary>
    private static string? GetTerminologyGuide(SupportedLangs targetLang)
    {
        return SlangCache.Get(targetLang);
    }

    private static async Task<string> TranslateViaAI(string text, SupportedLangs targetLang)
    {
        string targetLanguage = GetClientLanguageName(targetLang);
        string? guide = GetTerminologyGuide(targetLang);
        string prompt = PromptBuilder.BuildSinglePrompt(text, targetLanguage, guide);
        var payload = new
        {
            model = Main.Model.Value,
            messages = new[] { new { role = "user", content = prompt } }
        };

        string responseText = await SendApiRequest(payload);
        Main.Logger.LogInfo($"TranslateUs: [AI] \"{text}\" → \"{responseText}\"");
        return responseText;
    }

    private static async Task<List<(string original, string translated)>> BatchTranslateViaAI(
        List<string> messages, SupportedLangs targetLang)
    {
        string targetLanguage = GetClientLanguageName(targetLang);
        string? guide = GetTerminologyGuide(targetLang);
        string prompt = PromptBuilder.BuildBatchPrompt(messages, targetLanguage, guide);
        var payload = new
        {
            model = Main.Model.Value,
            messages = new[] { new { role = "user", content = prompt } }
        };

        string responseText = await SendApiRequest(payload);
        var results = ParseBatchResponse(messages, responseText);
        int count = results.Count(r => r.translated != r.original);
        Main.Logger.LogInfo($"TranslateUs: [AI Batch] Translated {count}/{messages.Count} messages");
        return results;
    }

    private static async Task<string> SendApiRequest(object payload)
    {
        string jsonPayload = JsonSerializer.Serialize(payload);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Main.ApiKey.Value}");
        client.Timeout = TimeSpan.FromSeconds(15);

        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(Main.ApiUrl.Value, content);
        string responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Main.Logger.LogWarning($"TranslateUs: API error {response.StatusCode}: {responseJson}");
            throw new HttpRequestException($"API returned {response.StatusCode}");
        }

        using JsonDocument doc = JsonDocument.Parse(responseJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()!
            .Trim();
    }

    private static List<(string original, string translated)> ParseBatchResponse(
        List<string> originals, string responseText)
    {
        try
        {
            responseText = responseText
                .Replace("```json", "").Replace("```", "").Trim();

            using JsonDocument doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                Main.Logger.LogWarning($"TranslateUs: Batch response is not an array: {responseText}");
                return originals.Select(m => (m, m)).ToList();
            }

            var results = new List<(string, string)>();
            for (int i = 0; i < originals.Count; i++)
            {
                string translated = i < root.GetArrayLength()
                    ? root[i].GetString() ?? originals[i]
                    : originals[i];
                results.Add((originals[i], translated));
            }
            return results;
        }
        catch (JsonException ex)
        {
            Main.Logger.LogWarning($"TranslateUs: Failed to parse batch response: {ex.Message}");
            return originals.Select(m => (m, m)).ToList();
        }
    }

    #endregion

    #region Google Translate

    // Semaphore to prevent burst requests from triggering rate limits
    private static readonly SemaphoreSlim GoogleSemaphore = new(1, 1);
    private static DateTime _lastGoogleRequest = DateTime.MinValue;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(500);

    private static async Task<string> TranslateViaGoogle(string text, SupportedLangs targetLang)
    {
        string targetCode = ClientLangToGoogleCode(targetLang);

        // Rate-limit guard: ensure minimum interval between requests
        await GoogleSemaphore.WaitAsync();
        try
        {
            var timeSinceLast = DateTime.UtcNow - _lastGoogleRequest;
            if (timeSinceLast < MinRequestInterval)
                await Task.Delay(MinRequestInterval - timeSinceLast);

            string result = await GoogleRequestWithRetry(text, targetCode);
            _lastGoogleRequest = DateTime.UtcNow;

            if (result != text)
                Main.Logger.LogInfo($"TranslateUs: [Google] \"{text}\" → \"{result}\"");

            return result;
        }
        finally
        {
            GoogleSemaphore.Release();
        }
    }

    private static async Task<List<(string original, string translated)>> BatchTranslateViaGoogle(
        List<string> messages, SupportedLangs targetLang)
    {
        string targetCode = ClientLangToGoogleCode(targetLang);
        var results = new List<(string original, string translated)>();

        foreach (var msg in messages)
        {
            try
            {
                string translated = await TranslateViaGoogle(msg, targetLang);
                results.Add((msg, translated));
            }
            catch
            {
                results.Add((msg, msg));
            }
        }

        int count = results.Count(r => r.translated != r.original);
        Main.Logger.LogInfo($"TranslateUs: [Google Batch] Translated {count}/{messages.Count} messages");
        return results;
    }
    
    private static async Task<string> GoogleRequestWithRetry(string text, string targetLangCode)
    {
        // Use a more stable client identifier than "gtx"
        string url = $"https://translate.googleapis.com/translate_a/single" +
                     $"?client=dict-chrome-ex&sl=auto&tl={targetLangCode}&dt=t&q={Uri.EscapeDataString(text)}";

        int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                // Add a realistic User-Agent to avoid being blocked
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                string responseJson = await client.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array &&
                    root.GetArrayLength() > 0 &&
                    root[0].ValueKind == JsonValueKind.Array &&
                    root[0].GetArrayLength() > 0 &&
                    root[0][0].ValueKind == JsonValueKind.Array &&
                    root[0][0].GetArrayLength() > 0)
                {
                    string translated = root[0][0][0].GetString() ?? text;
                    return translated.Trim();
                }

                return text;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries - 1)
            {
                int delay = (int)Math.Pow(2, attempt) * 1000; // 1s, 2s, 4s
                Main.Logger.LogWarning($"TranslateUs: Google request failed (attempt {attempt + 1}/{maxRetries}), retrying in {delay}ms: {ex.Message}");
                await Task.Delay(delay);
            }
            catch (TaskCanceledException) when (attempt < maxRetries - 1)
            {
                int delay = (int)Math.Pow(2, attempt) * 1000;
                Main.Logger.LogWarning($"TranslateUs: Google request timed out (attempt {attempt + 1}/{maxRetries}), retrying in {delay}ms");
                await Task.Delay(delay);
            }
        }

        Main.Logger.LogError($"TranslateUs: Google translation failed after {maxRetries} attempts");
        return text;
    }

    /// <summary>Public accessor for SlangCache notifications.</summary>
    public static string ClientLangToGoogleCodeStatic(SupportedLangs lang)
        => ClientLangToGoogleCode(lang);

    private static string ClientLangToGoogleCode(SupportedLangs lang)
    {
        return lang switch
        {
            SupportedLangs.English => "en",
            SupportedLangs.Spanish => "es",
            SupportedLangs.Korean => "ko",
            SupportedLangs.Russian => "ru",
            SupportedLangs.Portuguese => "pt",
            SupportedLangs.Brazilian => "pt",
            SupportedLangs.Filipino => "tl",
            SupportedLangs.French => "fr",
            SupportedLangs.Italian => "it",
            SupportedLangs.German => "de",
            SupportedLangs.Dutch => "nl",
            SupportedLangs.Japanese => "ja",
            SupportedLangs.Latam => "es",
            SupportedLangs.Irish => "ga",
            SupportedLangs.SChinese => "zh-CN",
            SupportedLangs.TChinese => "zh-TW",
            _ => "en"
        };
    }

    #endregion

    #region Language Helpers

    public static string GetClientLanguageName(SupportedLangs lang)
    {
        return lang switch
        {
            SupportedLangs.English => "English",
            SupportedLangs.Spanish => "Spanish",
            SupportedLangs.Korean => "Korean (한국어)",
            SupportedLangs.Russian => "Russian (Русский)",
            SupportedLangs.Portuguese => "Portuguese (Português)",
            SupportedLangs.Brazilian => "Brazilian Portuguese (Português Brasileiro)",
            SupportedLangs.Filipino => "Filipino",
            SupportedLangs.French => "French (Français)",
            SupportedLangs.Italian => "Italian (Italiano)",
            SupportedLangs.German => "German (Deutsch)",
            SupportedLangs.Dutch => "Dutch (Nederlands)",
            SupportedLangs.Japanese => "Japanese (日本語)",
            SupportedLangs.Latam => "Latin American Spanish (Español Latino)",
            SupportedLangs.Irish => "Irish (Gaeilge)",
            SupportedLangs.SChinese => "Simplified Chinese (简体中文)",
            SupportedLangs.TChinese => "Traditional Chinese (繁體中文)",
            _ => DataManager.Settings.Language.CurrentLanguage.ToString()
        };
    }

    public static SupportedLangs GameKeywordToSupportedLangs(GameKeywords gameKeyword)
    {
        return gameKeyword switch
        {
            GameKeywords.English => SupportedLangs.English,
            GameKeywords.SpanishEU => SupportedLangs.Spanish,
            GameKeywords.Korean => SupportedLangs.Korean,
            GameKeywords.Russian => SupportedLangs.Russian,
            GameKeywords.Portuguese => SupportedLangs.Portuguese,
            GameKeywords.Brazilian => SupportedLangs.Brazilian,
            GameKeywords.Filipino => SupportedLangs.Filipino,
            GameKeywords.French => SupportedLangs.French,
            GameKeywords.Italian => SupportedLangs.Italian,
            GameKeywords.German => SupportedLangs.German,
            GameKeywords.Dutch => SupportedLangs.Dutch,
            GameKeywords.Japanese => SupportedLangs.Japanese,
            GameKeywords.SpanishLA => SupportedLangs.Latam,
            GameKeywords.Irish => SupportedLangs.Irish,
            GameKeywords.SChinese => SupportedLangs.SChinese,
            GameKeywords.TChinese => SupportedLangs.TChinese,
            _ => SupportedLangs.English
        };
    }

    #endregion
}
