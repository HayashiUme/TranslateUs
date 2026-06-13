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
            var task = Translator.Translate(originalText, player, forSending: true);

            while (!task.IsCompleted)
                yield return null;

            string textToSend;
            if (task.IsCompletedSuccessfully && task.Result.translated != task.Result.original)
            {
                MessageGroup.PendingSend = (task.Result.original, task.Result.translated);
                textToSend = task.Result.translated;
                Main.Logger.LogInfo(
                    $"TranslateUs: [Send] \"{originalText}\" → \"{textToSend}\"");
            }
            else
            {
                if (!task.IsCompletedSuccessfully)
                    Main.Logger.LogWarning($"TranslateUs: [Send] Failed for \"{originalText}\"");
                textToSend = originalText;
            }

            player.RpcSendChat(textToSend);
            _isSendingTranslated = false;
        }
    }
}
