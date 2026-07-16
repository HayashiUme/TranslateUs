using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TranslateUs.Core;

namespace TranslateUs.Patches
{
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat))]
    public class PlayerControlPatch
    {
        private static bool _isSendingTranslated;

        public static bool Prefix(PlayerControl __instance, string chatText)
        {
            if (__instance != PlayerControl.LocalPlayer)
                return true;

            if (_isSendingTranslated)
                return true;

            if (string.IsNullOrWhiteSpace(chatText))
                return false;

            _isSendingTranslated = true;
            __instance.StartCoroutine(
                TranslateAndSendCoroutine(__instance, chatText).WrapToIl2Cpp());
            return false;
        }

        private static IEnumerator TranslateAndSendCoroutine(PlayerControl player, string originalText)
        {
            var roomLang = LanguageDetector.ResolveRoomLanguage();
            var task = Translator.TranslateToRoomLanguage(originalText, roomLang);

            while (!task.IsCompleted)
                yield return null;

            string textToSend = originalText;

            try
            {
                if (task.IsCompletedSuccessfully
                    && !string.IsNullOrWhiteSpace(task.Result.translated)
                    && task.Result.translated != originalText)
                {
                    MessageGroup.PendingSend = (originalText, task.Result.translated);
                    textToSend = task.Result.translated;
                    Main.Logger.LogInfo(
                        $"TranslateUs: [Send] \"{originalText}\" → \"{textToSend}\"");
                }
                else if (task.IsFaulted)
                {
                    Main.Logger.LogWarning(
                        $"TranslateUs: [Send] Translation faulted for \"{originalText}\": " +
                        $"{task.Exception?.InnerException?.Message}");
                }
            }
            catch (Exception ex)
            {
                Main.Logger.LogError($"TranslateUs: [Send] Unexpected error: {ex.Message}");
            }
            finally
            {
                player.RpcSendChat(textToSend);
                _isSendingTranslated = false;
            }
        }
    }
}
