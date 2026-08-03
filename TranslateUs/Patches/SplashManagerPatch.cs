using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TMPro;
using TranslateUs.Core;
using TranslateUs.Resources;
using UnityEngine;

namespace TranslateUs.Patches;

[HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Update))]
[HarmonyPriority(Priority.VeryLow)]
public static class SplashManagerLocalAiPatch
{
    private static bool _intercepted;
    private static bool _completed;
    private static bool _failed;
    private static TextMeshPro? _statusText;
    private static GameObject? _logoObject;
    private static long _lastLoggedKB;

    private const float FadeDuration = 0.4f;

    public static bool Prefix(SplashManager __instance)
    {
        if (Main.Backend != BackendType.LocalAI) return true;

        if (!__instance.doneLoadingRefdata) return true;

        if (_completed) return true;

        if (!_intercepted)
        {
            _intercepted = true;
            __instance.StartCoroutine(
                CoLoadLocalAi(__instance).WrapToIl2Cpp());
        }

        return false;
    }

    private static IEnumerator CoLoadLocalAi(SplashManager __instance)
    {
        {
            var logoSprite = ResourcesUtils.LoadSpriteFromAssembly("Logo_Splash.png",450f);
            if (logoSprite != null)
            {
                _logoObject = new GameObject("TranslateUsLogo");
                _logoObject.transform.SetParent(null);
                _logoObject.transform.localPosition = new Vector3(0f, 0.5f, -10f);
                _logoObject.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
                var sr = _logoObject.AddComponent<SpriteRenderer>();
                sr.sprite = logoSprite;
                sr.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        _statusText = UnityEngine.Object.Instantiate(__instance.errorPopup.InfoText);
        _statusText.transform.SetParent(null);
        _statusText.transform.localPosition = new Vector3(0f, -0.4f, -10f);
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.fontSize = 0.55f;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.color = new Color(1f, 1f, 1f, 0f);

        yield return FadeLogoAndText(fadeIn: true);

        DownloadProgress.Label = "AI Runtime";
        {
            bool ok = false;
            string err = "";
            var task = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await NativeLibraryManager.EnsureAsync();
                    ok = true;
                }
                catch (System.Exception ex) { err = ex.Message; }
            });
            _lastLoggedKB = 0;
            while (!task.IsCompleted)
            {
                UpdateStatusText(DownloadProgress.GetDisplayText());
                yield return null;
            }
            if (task.IsFaulted && string.IsNullOrEmpty(err))
                err = task.Exception?.InnerException?.Message ?? "Unknown error";

            if (!ok)
            {
                DownloadProgress.Reset();
                Main.Logger.LogError($"[Splash] Native lib download failed: {err}");
                SetStatusFull("AI Runtime download failed.\nCheck logs for details.");
                _failed = true;
                yield return new WaitForSeconds(4f);
                yield return FinishAndExit(__instance);
                yield break;
            }
        }

        DownloadProgress.Label = "AI Model";
        string modelPath = "";
        {
            bool ok = false;
            string err = "";
            var task = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    modelPath = await ModelManager.AcquireModelAsync(
                        Main.LocalModelPath.Value,
                        Main.LocalModelUrl.Value);
                    ok = true;
                }
                catch (System.Exception ex) { err = ex.Message; }
            });
            _lastLoggedKB = 0;
            while (!task.IsCompleted)
            {
                UpdateStatusText(DownloadProgress.GetDisplayText());
                yield return null;
            }
            if (task.IsFaulted && string.IsNullOrEmpty(err))
                err = task.Exception?.InnerException?.Message ?? "Unknown error";

            if (!ok || string.IsNullOrEmpty(modelPath))
            {
                DownloadProgress.Reset();
                Main.Logger.LogError($"[Splash] Model download failed: {err}");
                SetStatusFull("AI Model download failed.\nCheck logs for details.");
                _failed = true;
                yield return new WaitForSeconds(4f);
                yield return FinishAndExit(__instance);
                yield break;
            }
        }

        SetStatusFull("Loading AI Model...\n(This may take 15-30 seconds)");
        LocalAiBackend.Instance.BeginInitialize(
            modelPath,
            threads: Main.InferenceThreads?.Value ?? 2,
            contextSize: Main.ContextSize?.Value ?? 4096);

        float waited = 0f;
        const float maxWait = 120f;
        while (!LocalAiBackend.Instance.IsReady && waited < maxWait)
        {
            if (!LocalAiBackend.Instance.IsInitializing)
            {
                Main.Logger.LogError("[Splash] Model initialization failed.");
                SetStatusFull("AI Model failed to load.\nCheck logs for details.");
                _failed = true;
                yield return new WaitForSeconds(4f);
                yield return FinishAndExit(__instance);
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            waited += 0.5f;
        }

        if (!LocalAiBackend.Instance.IsReady)
        {
            Main.Logger.LogError($"[Splash] Model loading timed out after {maxWait}s.");
            SetStatusFull("AI Model load timed out.\nCheck logs for details.");
            _failed = true;
            yield return new WaitForSeconds(4f);
            yield return FinishAndExit(__instance);
            yield break;
        }

        SetStatusFull("AI Model Ready!");
        yield return new WaitForSeconds(0.8f);

        yield return FinishAndExit(__instance);
    }

    private static void UpdateStatusText(string text)
    {
        if (_statusText != null)
            _statusText.text = text;

        if (DownloadProgress.Active)
        {
            long currentKB = DownloadProgress.BytesDownloaded / 1024;
            if (currentKB - _lastLoggedKB >= 1024)
            {
                _lastLoggedKB = currentKB;
                Main.Logger.LogInfo($"[Splash] {text.Replace("\n", " | ")}");
            }
        }
    }

    private static void SetStatusFull(string text)
    {
        if (_statusText != null)
            _statusText.text = text;
        Main.Logger.LogInfo($"[Splash] {text.Replace("\n", " | ")}");
    }

    private static IEnumerator FadeLogoAndText(bool fadeIn)
    {
        float elapsed = 0f;
        Color textStart = _statusText?.color ?? Color.clear;
        Color textEnd = fadeIn
            ? new Color(1f, 1f, 1f, 0.7f)
            : new Color(1f, 1f, 1f, 0f);

        SpriteRenderer? logoSr = _logoObject?.GetComponent<SpriteRenderer>();
        Color logoStart = logoSr?.color ?? Color.clear;
        Color logoEnd = fadeIn
            ? new Color(1f, 1f, 1f, 1f)
            : new Color(1f, 1f, 1f, 0f);

        while (elapsed < FadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / FadeDuration;
            if (_statusText != null) _statusText.color = Color.Lerp(textStart, textEnd, t);
            if (logoSr != null) logoSr.color = Color.Lerp(logoStart, logoEnd, t);
            yield return null;
        }
        if (_statusText != null) _statusText.color = textEnd;
        if (logoSr != null) logoSr.color = logoEnd;
    }

    private static IEnumerator FadeText(TMP_Text text, float duration, bool fadeIn)
    {
        float elapsed = 0f;
        Color start = text.color;
        Color end = fadeIn
            ? new Color(1f, 1f, 1f, 0.7f)
            : new Color(1f, 1f, 1f, 0f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            text.color = Color.Lerp(start, end, elapsed / duration);
            yield return null;
        }
        text.color = end;
    }

    private static IEnumerator FinishAndExit(SplashManager __instance)
    {
        if (_logoObject != null || _statusText != null)
        {
            if (_failed && _statusText != null)
            {
                _statusText.color = new Color(1f, 0.4f, 0.4f, 0.7f);
                yield return new WaitForSeconds(1f);
            }
            yield return FadeLogoAndText(fadeIn: false);

            if (_logoObject != null)
            {
                UnityEngine.Object.Destroy(_logoObject);
                _logoObject = null;
            }
            if (_statusText != null)
            {
                UnityEngine.Object.Destroy(_statusText.gameObject);
                _statusText = null;
            }
        }

        __instance.sceneChanger.AllowFinishLoadingScene();
        __instance.startedSceneLoad = true;
        _completed = true;
    }
}
