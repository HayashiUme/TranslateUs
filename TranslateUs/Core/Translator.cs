using System.Text;
using System.Text.Json;
using AmongUs.Data;
using AmongUs.GameOptions;

namespace TranslateUs.Core;

public class Translator
{
    public static async Task<(string original, string translated)> Translate(
        string originalMessage, PlayerControl sourcePlayer, bool forSending)
    {
        if (!IsApiConfigured())
            return (originalMessage, originalMessage);

        string targetLanguage = forSending
            ? GetLobbyLanguageName()
            : GetClientLanguageName();

        string prompt = BuildSinglePrompt(originalMessage, targetLanguage);
        var payload = BuildPayload(prompt);

        try
        {
            string responseText = await SendApiRequest(payload);
            return (originalMessage, responseText);
        }
        catch (Exception ex)
        {
            Main.Logger.LogError($"TranslateUs: Single translation failed: {ex.Message}");
            return (originalMessage, originalMessage);
        }
    }

    public static async Task<List<(string original, string translated)>> BatchTranslate(
        List<string> messages, bool forSending)
    {
        if (!IsApiConfigured() || messages.Count == 0)
            return messages.Select(m => (m, m)).ToList();

        string targetLanguage = forSending
            ? GetLobbyLanguageName()
            : GetClientLanguageName();

        string prompt = BuildBatchPrompt(messages, targetLanguage);
        var payload = BuildPayload(prompt);

        try
        {
            string responseText = await SendApiRequest(payload);
            return ParseBatchResponse(messages, responseText);
        }
        catch (Exception ex)
        {
            Main.Logger.LogError($"TranslateUs: Batch translation failed: {ex.Message}");
            return messages.Select(m => (m, m)).ToList();
        }
    }

    private static bool IsApiConfigured()
        => Main.ApiUrl.Value != "0" && Main.ApiKey.Value != "0";

    private static object BuildPayload(string prompt) => new
    {
        model = Main.Model.Value,
        messages = new[] { new { role = "user", content = prompt } }
    };

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

    private static string BuildSystemPrompt(string targetLanguage)
    {
        string extra = Main.ExtraPrompt.Value;
        string extraLine = string.IsNullOrWhiteSpace(extra) ? "" : $" {extra}";

        return "You are an AI translation module dedicated to the game Among Us. " +
               $"Your task is to normalize and translate in-game chat messages into the target language defined by `{targetLanguage}`. " +
               "These messages frequently contain Among Us-specific terminology and common misspellings/typos. " +
               "You must accurately map such terms to the correct localized form. " +
               "For example, if the target language is Chinese, the verb \"vent\" should be rendered as \"跳关\". " +
               "You are expected to independently recognize and resolve variant or misspelled inputs like \"跳管道\" or \"钻管\" " +
               "and convert them to the standard \"跳管/使用通风口/钻管道\". " +
               "Always consider the player's chat input as the source, and produce output strictly in the specified target language. " +
               $"Output only the final translated text with no additional commentary, explanation, or formatting.{extraLine}";
    }

    private static string BuildSinglePrompt(string message, string targetLanguage)
    {
        return $"{BuildSystemPrompt(targetLanguage)}\n\nMessage to translate: {message}";
    }

    private static string BuildBatchPrompt(List<string> messages, string targetLanguage)
    {
        string jsonArray = JsonSerializer.Serialize(messages);
        return $"{BuildSystemPrompt(targetLanguage)}\n\n" +
               "I will give you a JSON array of messages. Translate each one independently. " +
               "Return ONLY a JSON array of the translated strings, in the exact same order. " +
               "Do NOT include any other text, explanation, or markdown formatting.\n\n" +
               $"Input: {jsonArray}\n\n" +
               "Output (JSON array only):";
    }
    
    private static List<(string original, string translated)> ParseBatchResponse(
        List<string> originals, string responseText)
    {
        try
        {
            // Strip markdown code fences if present
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
                string translated;
                if (i < root.GetArrayLength())
                {
                    translated = root[i].GetString() ?? originals[i];
                }
                else
                {
                    translated = originals[i]; // Missing entry
                }
                results.Add((originals[i], translated));
            }

            Main.Logger.LogInfo($"TranslateUs: Batch translated {originals.Count} messages, got {root.GetArrayLength()} back");
            return results;
        }
        catch (JsonException ex)
        {
            Main.Logger.LogWarning($"TranslateUs: Failed to parse batch response: {ex.Message}\nResponse: {responseText}");
            return originals.Select(m => (m, m)).ToList();
        }
    }
    
    private static string GetClientLanguageName()
    {
        try
        {
            return DataManager.Settings.Language.CurrentLanguage switch
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
        catch
        {
            return "English";
        }
    }

    private static string GetLobbyLanguageName()
    {
        try
        {
            var manager = GameOptionsManager.Instance;
            // CurrentGameOptions works for both host and client;
            // GameHostOptions is only set when YOU are the host
            var keywords = manager?.CurrentGameOptions?.Keywords
                        ?? manager?.GameHostOptions?.Keywords;

            Main.Logger.LogInfo($"TranslateUs: Lobby keywords = {keywords?.ToString() ?? "NULL"}");

            if (keywords == null || keywords == GameKeywords.Other || keywords == GameKeywords.All)
            {
                Main.Logger.LogWarning("TranslateUs: Lobby language unavailable, falling back to client language");
                return GetClientLanguageName();
            }

            return keywords switch
            {
                GameKeywords.English => "English",
                GameKeywords.SpanishLA => "Spanish",
                GameKeywords.Brazilian => "Portuguese",
                GameKeywords.Portuguese => "Portuguese",
                GameKeywords.Korean => "Korean (한국어)",
                GameKeywords.Russian => "Russian (Русский)",
                GameKeywords.Dutch => "Dutch (Nederlands)",
                GameKeywords.Filipino => "Filipino",
                GameKeywords.French => "French (Français)",
                GameKeywords.German => "German (Deutsch)",
                GameKeywords.Italian => "Italian (Italiano)",
                GameKeywords.Japanese => "Japanese (日本語)",
                GameKeywords.SpanishEU => "Spanish",
                GameKeywords.Arabic => "Arabic (العربية)",
                GameKeywords.Polish => "Polish (Polski)",
                GameKeywords.SChinese => "Simplified Chinese (简体中文)",
                GameKeywords.TChinese => "Traditional Chinese (繁體中文)",
                GameKeywords.Irish => "Irish (Gaeilge)",
                _ => GetClientLanguageName()
            };
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"TranslateUs: Failed to get lobby language: {ex.Message}");
            return GetClientLanguageName();
        }
    }
}
