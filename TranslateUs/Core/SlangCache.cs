using System.Text;
using System.Text.Json;

namespace TranslateUs.Core;

/// <summary>
/// Caches AI-generated terminology/glossary guides per target language.
/// On first use of a language, the AI generates a guide based on its training
/// data about how real players in that language community actually talk.
/// Shows system notifications via AddChat when generation starts/completes.
/// </summary>
public static class SlangCache
{
    private static readonly Dictionary<SupportedLangs, string> Cache = new();
    private static readonly Dictionary<SupportedLangs, Task<string>> PendingTasks = new();
    private static readonly object PendingLock = new();

    /// <summary>Get cached terminology guide, or null if not yet generated.</summary>
    public static string? Get(SupportedLangs targetLang)
    {
        Cache.TryGetValue(targetLang, out var guide);
        return guide;
    }

    /// <summary>
    /// Generate a terminology guide for the target language via AI.
    /// If another caller already started generation for this language,
    /// waits for that task instead of duplicating the request.
    /// </summary>
    public static async Task<string> Generate(SupportedLangs targetLang)
    {
        // Already cached: return immediately
        if (Cache.TryGetValue(targetLang, out var cached))
            return cached;

        // Check if another caller is already generating for this language
        Task<string>? pending;
        lock (PendingLock)
        {
            if (PendingTasks.TryGetValue(targetLang, out pending))
            {
                // Another task is generating — release lock and await it
            }
            else
            {
                // We're the first: create the generation task
                pending = GenerateInternal(targetLang);
                PendingTasks[targetLang] = pending;
            }
        }

        string result = await pending;

        // Clean up pending task entry (only if it's still our task)
        lock (PendingLock)
        {
            if (PendingTasks.TryGetValue(targetLang, out var existing) && existing == pending)
                PendingTasks.Remove(targetLang);
        }

        return result;
    }

    /// <summary>
    /// Does the actual AI call to generate the terminology guide.
    /// Runs only once per language.
    /// </summary>
    private static async Task<string> GenerateInternal(SupportedLangs targetLang)
    {
        string langName = Translator.GetClientLanguageName(targetLang);

        // Notify player: generation started (translated to their game language)
        ShowSystemMessage(
            $"正在初始化 TranslateUs 术语表 ({langName})，完成后即可获得最佳翻译体验...");

        Main.Logger.LogInfo($"[SlangCache] Generating terminology guide for {langName}...");

        string genPrompt = BuildGenerationPrompt(langName);
        var payload = new
        {
            model = Main.Model.Value,
            messages = new[] { new { role = "user", content = genPrompt } }
        };

        try
        {
            string response = await SendSlangRequest(payload);
            Cache[targetLang] = response;
            Main.Logger.LogInfo($"[SlangCache] Terminology guide generated for {langName} ({response.Length} chars)");

            // Notify player: generation complete
            ShowSystemMessage(
                $"SlangCache 术语表 ({langName}) 已构建完毕，今后的翻译会得到增强。");

            return response;
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[SlangCache] Failed to generate guide for {langName}: {ex.Message}");
            Cache[targetLang] = "";
            return "";
        }
    }

    /// <summary>
    /// Shows a system notification message in the chat.
    /// Translates it to the player's game language before displaying.
    /// Uses AI if configured, otherwise Google Translate.
    /// </summary>
    private static async void ShowSystemMessage(string message)
    {
        try
        {
            var myLang = AmongUs.Data.DataManager.Settings.Language.CurrentLanguage;
            string displayText;

            if (Main.UseAI)
            {
                string myLangName = Translator.GetClientLanguageName(myLang);
                var simplePrompt =
                    $"Translate this system notification to {myLangName}. " +
                    $"Output ONLY the translation, no explanation or formatting:\n\n{message}";

                var payload = new
                {
                    model = Main.Model.Value,
                    messages = new[] { new { role = "user", content = simplePrompt } }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Main.ApiKey.Value}");
                client.Timeout = TimeSpan.FromSeconds(10);

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(Main.ApiUrl.Value, content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseJson);
                    displayText = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString()!
                        .Trim();
                }
                else
                {
                    displayText = message;
                }
            }
            else
            {
                string targetCode = Translator.ClientLangToGoogleCodeStatic(myLang);
                displayText = await TranslateViaGoogleSimple(message, targetCode);
            }

            // Display via AddChat (local-only, not networked)
            var hud = DestroyableSingleton<HudManager>.Instance;
            if (hud?.Chat != null && PlayerControl.LocalPlayer != null)
            {
                hud.Chat.AddChat(PlayerControl.LocalPlayer, displayText);
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogWarning($"[SlangCache] Failed to show notification: {ex.Message}");
        }
    }

    private static async Task<string> TranslateViaGoogleSimple(string text, string targetCode)
    {
        try
        {
            string url = $"https://translate.googleapis.com/translate_a/single" +
                         $"?client=dict-chrome-ex&sl=auto&tl={targetCode}&dt=t&q={Uri.EscapeDataString(text)}";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string responseJson = await client.GetStringAsync(url);

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
                root[0].ValueKind == JsonValueKind.Array && root[0].GetArrayLength() > 0 &&
                root[0][0].ValueKind == JsonValueKind.Array && root[0][0].GetArrayLength() > 0)
            {
                return root[0][0][0].GetString()?.Trim() ?? text;
            }
        }
        catch { }
        return text;
    }

    private static string BuildGenerationPrompt(string targetLanguage)
    {
        return $@"You are an expert in multilingual gaming communities. Generate a glossary of Among Us terminology and slang as actually used by {targetLanguage} players.

Include:
1. Role names: impostor, crewmate, shapeshifter, engineer, scientist, guardian angel, phantom, tracker, noisemaker, etc.
2. Actions: vent, report, scan, sabotage, kill, vote, skip, self-report, camp, clear, etc.
3. Locations: admin, electrical/elec, medbay, security/cams, reactor, comms, o2, nav, weapons, storage, cafeteria/cafe, etc.
4. Common chat abbreviations and slang: sus, afk, gg, gl, ty/thx, np, lol, lmao, xd, wtf, brb, idk, nvm, tbf, imo, etc.
5. Any community-specific slang or expressions unique to {targetLanguage} Among Us players.

Format: one term per line in the format: original term/abbreviation = {targetLanguage} equivalent
Keep it compact and accurate. Only include terms you are confident about. Output ONLY the glossary, no introduction or explanation.";
    }

    private static async Task<string> SendSlangRequest(object payload)
    {
        string jsonPayload = JsonSerializer.Serialize(payload);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Main.ApiKey.Value}");
        client.Timeout = TimeSpan.FromSeconds(15);

        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(Main.ApiUrl.Value, content);
        string responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"API returned {response.StatusCode}: {responseJson}");

        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()!
            .Trim();
    }
}
