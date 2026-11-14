using System.Text;
using System.Text.Json;
using AmongUs.Data;
using AmongUs.GameOptions;
using UnityEngine;

namespace TranslateUs.Core;

public class Translator
{
    #region Public API

    public static async Task<(string original, string translated)> TranslateToRoomLanguage(
        string text, SupportedLangs roomLang)
    {
        var myLang = DataManager.Settings.Language.CurrentLanguage;
        return await TranslateInternal(text, myLang, roomLang);
    }

    public static async Task<(string original, string translated)> TranslateToMyLanguage(
        string text, SupportedLangs myLang, SupportedLangs roomLang)
    {
        return await TranslateInternal(text, roomLang, myLang);
    }

    public static async Task<List<(string original, string translated)>> BatchTranslateToMyLanguage(
        List<string> messages, SupportedLangs myLang, SupportedLangs roomLang)
        => await BatchTranslateInternal(messages, roomLang, myLang);

    #endregion

    #region Internal translation dispatch

    private static async Task<(string original, string translated)> TranslateInternal(
        string originalMessage, SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        string diagSource = GetClientLanguageName(sourceLang);
        string diagTarget = GetClientLanguageName(targetLang);
        Main.Logger.LogInfo($"[DIAG] Translate: {diagSource}→{diagTarget}, Backend={Main.Backend}, IsPaused={Main.IsPaused}, Input=\"{originalMessage}\"");

        if (Main.IsPaused)
            return (originalMessage, originalMessage);

        switch (Main.Backend)
        {
            case BackendType.LocalAI:
                if (!LocalAiBackend.Instance.IsCacheReady)
                {
                    if (!LocalAiBackend.Instance.IsWarmingUp)
                        _ = LocalAiBackend.Instance.WarmupAsync(sourceLang, targetLang);

                    if (!LocalAiBackend.WarmupNotificationShown)
                    {
                        LocalAiBackend.WarmupNotificationShown = true;
                        ShowWarmupNotification();
                    }
                    return (originalMessage, originalMessage);
                }

                try
                {
                    string localResult = await TranslateViaLocalAi(originalMessage, sourceLang, targetLang);
                    return (originalMessage, localResult);
                }
                catch (Exception ex)
                {
                    Main.Logger.LogWarning($"TranslateUs: LocalAI translation failed: {ex.Message}");
                    return (originalMessage, originalMessage);
                }

            case BackendType.CloudAI:
                try
                {
                    string aiResult = await TranslateViaAI(originalMessage, sourceLang, targetLang);
                    return (originalMessage, aiResult);
                }
                catch (Exception ex)
                {
                    Main.Logger.LogWarning($"TranslateUs: AI translation failed: {ex.Message}");
                    return (originalMessage, originalMessage);
                }

            case BackendType.None:
            default:
                return (originalMessage, originalMessage);
        }
    }

    private static async Task<List<(string original, string translated)>> BatchTranslateInternal(
        List<string> messages, SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        if (messages.Count == 0)
            return new List<(string, string)>();

        string diagSource = GetClientLanguageName(sourceLang);
        string diagTarget = GetClientLanguageName(targetLang);
        Main.Logger.LogInfo($"[DIAG] BatchTranslate: {diagSource}→{diagTarget}, Count={messages.Count}, Backend={Main.Backend}");

        switch (Main.Backend)
        {
            case BackendType.LocalAI:
                if (!LocalAiBackend.Instance.IsCacheReady)
                {
                    if (!LocalAiBackend.Instance.IsWarmingUp)
                        _ = LocalAiBackend.Instance.WarmupAsync(sourceLang, targetLang);

                    if (!LocalAiBackend.WarmupNotificationShown)
                    {
                        LocalAiBackend.WarmupNotificationShown = true;
                        ShowWarmupNotification();
                    }
                    return messages.Select(m => (m, m)).ToList();
                }

                try
                {
                    return await BatchTranslateViaLocalAi(messages, sourceLang, targetLang);
                }
                catch (Exception ex)
                {
                    Main.Logger.LogWarning($"TranslateUs: LocalAI batch translation failed: {ex.Message}");
                    return messages.Select(m => (m, m)).ToList();
                }

            case BackendType.CloudAI:
                try
                {
                    return await BatchTranslateViaAI(messages, sourceLang, targetLang);
                }
                catch (Exception ex)
                {
                    Main.Logger.LogWarning($"TranslateUs: AI batch translation failed: {ex.Message}");
                    return messages.Select(m => (m, m)).ToList();
                }

            case BackendType.None:
            default:
                return messages.Select(m => (m, m)).ToList();
        }
    }

    #endregion

    #region AI Translation

    /// <summary>Get pre-baked XML terminology guides for both source and target languages.</summary>
    private static (string? sourceGuide, string? targetGuide) GetTerminologyGuides(
        SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        return (SlangCache.Get(sourceLang), SlangCache.Get(targetLang));
    }

    private static async Task<string> TranslateViaAI(
        string text, SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        string sourceLanguage = GetClientLanguageName(sourceLang);
        string targetLanguage = GetClientLanguageName(targetLang);
        var (sourceGuide, targetGuide) = GetTerminologyGuides(sourceLang, targetLang);
        string prompt = PromptBuilder.BuildSinglePrompt(text, sourceLanguage, targetLanguage, sourceGuide, targetGuide);
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
        List<string> messages, SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        string sourceLanguage = GetClientLanguageName(sourceLang);
        string targetLanguage = GetClientLanguageName(targetLang);
        var (sourceGuide, targetGuide) = GetTerminologyGuides(sourceLang, targetLang);
        string prompt = PromptBuilder.BuildBatchPrompt(messages, sourceLanguage, targetLanguage, sourceGuide, targetGuide);
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

    #region Local AI Translation

    private static async Task<string> TranslateViaLocalAi(
        string text, SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        string sourceLanguage = GetClientLanguageName(sourceLang);
        string targetLanguage = GetClientLanguageName(targetLang);
        var (sourceGuide, targetGuide) = GetTerminologyGuides(sourceLang, targetLang);

        string prefix = PromptBuilder.BuildLocalModelPrefix(
            sourceLanguage, targetLanguage, sourceGuide, targetGuide);
        string suffix = PromptBuilder.BuildLocalModelSuffix(text);

        // Check if backend is ready; wait briefly if still initializing
        if (!LocalAiBackend.Instance.IsReady)
        {
            if (LocalAiBackend.Instance.IsInitializing)
            {
                bool ready = await LocalAiBackend.Instance.WaitForReady(TimeSpan.FromSeconds(5));
                if (!ready)
                {
                    Main.Logger.LogWarning("[LocalAI] Model not yet ready, returning original text");
                    return text;
                }
            }
            else
            {
                Main.Logger.LogWarning("[LocalAI] Model failed to load, returning original text");
                return text;
            }
        }

        string? result = await LocalAiBackend.Instance.TranslateAsync(
            prefix, suffix,
            maxTokens: Main.MaxInferenceTokens?.Value ?? 128,
            temperature: Main.InferenceTemperature?.Value ?? 0.1f);

        if (result == null)
        {
            Main.Logger.LogWarning("[LocalAI] Translation returned null, returning original text");
            return text;
        }

        Main.Logger.LogInfo($"TranslateUs: [LocalAI] \"{text}\" → \"{result}\"");
        return result;
    }

    private static async Task<List<(string original, string translated)>> BatchTranslateViaLocalAi(
        List<string> messages, SupportedLangs sourceLang, SupportedLangs targetLang)
    {
        string sourceLanguage = GetClientLanguageName(sourceLang);
        string targetLanguage = GetClientLanguageName(targetLang);
        var (sourceGuide, targetGuide) = GetTerminologyGuides(sourceLang, targetLang);

        string prefix = PromptBuilder.BuildLocalModelPrefix(
            sourceLanguage, targetLanguage, sourceGuide, targetGuide);
        string suffix = PromptBuilder.BuildLocalModelSuffix(
            PromptBuilder.BuildLocalModelBatchUserContent(messages));

        // Check backend readiness
        if (!LocalAiBackend.Instance.IsReady)
        {
            if (LocalAiBackend.Instance.IsInitializing)
            {
                bool ready = await LocalAiBackend.Instance.WaitForReady(TimeSpan.FromSeconds(5));
                if (!ready)
                {
                    Main.Logger.LogWarning("[LocalAI] Model not yet ready for batch, returning originals");
                    return messages.Select(m => (m, m)).ToList();
                }
            }
            else
            {
                Main.Logger.LogWarning("[LocalAI] Model not loaded, returning originals");
                return messages.Select(m => (m, m)).ToList();
            }
        }

        int batchTokens = Math.Min(128 * messages.Count, 1024);
        string? responseText = await LocalAiBackend.Instance.TranslateAsync(
            prefix, suffix,
            maxTokens: batchTokens,
            temperature: Main.InferenceTemperature?.Value ?? 0.1f);

        if (responseText == null)
        {
            Main.Logger.LogWarning("[LocalAI] Batch translation returned null");
            return messages.Select(m => (m, m)).ToList();
        }

        var results = ParseBatchResponse(messages, responseText);
        int count = results.Count(r => r.translated != r.original);
        Main.Logger.LogInfo($"TranslateUs: [LocalAI Batch] Translated {count}/{messages.Count} messages");
        return results;
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

    #region Warmup notification

    private static void ShowWarmupNotification()
    {
        try
        {
            var hud = DestroyableSingleton<HudManager>.Instance;
            if (hud?.Chat == null) return;

            hud.Chat.AddChat(
                PlayerControl.LocalPlayer,
                "Local AI is preparing its prompt cache (15-30s). Messages will remain untranslated until it is ready.");
            Main.Logger.LogInfo("[DIAG] Warmup notification shown in chat");
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[DIAG] Failed to show warmup notification: {ex.Message}");
        }
    }

    #endregion
}
