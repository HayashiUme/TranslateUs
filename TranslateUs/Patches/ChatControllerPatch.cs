using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TranslateUs.Core;
using UnityEngine;

namespace TranslateUs.Patches
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    public class ChatControllerAddChatPatch
    {
        public static void Postfix(ChatController __instance, PlayerControl sourcePlayer, string chatText)
        {
            var scroller = __instance.GetComponentInChildren<Scroller>(true);
            if (scroller?.Inner == null || scroller.Inner.childCount == 0) return;

            var bubble = scroller.Inner.GetChild(scroller.Inner.childCount - 1).GetComponent<ChatBubble>();
            if (bubble == null) return;

            if (sourcePlayer == PlayerControl.LocalPlayer)
            {
                var pending = MessageGroup.PendingSend;
                if (pending != null)
                {
                    new MessageGroup(pending.Value.original, pending.Value.translated, sourcePlayer, bubble);
                    Main.Logger.LogInfo($"TranslateUs: [AddChat] Self: \"{pending.Value.original}\" → \"{pending.Value.translated}\"");
                    MessageGroup.PendingSend = null;
                }
            }
            else
            {
                string original = chatText;
                Main.Logger.LogInfo($"TranslateUs: [AddChat] Received: \"{original}\"");
                __instance.StartCoroutine(TranslateAndUpdate(__instance, bubble, original, sourcePlayer).WrapToIl2Cpp());
            }
        }

        private static IEnumerator TranslateAndUpdate(
            ChatController chat, ChatBubble bubble, string original, PlayerControl sourcePlayer)
        {
            var task = Translator.Translate(original, sourcePlayer, forSending: false);
            while (!task.IsCompleted) yield return null;

            if (!task.IsCompletedSuccessfully || task.Result.translated == original)
                yield break;

            string translated = task.Result.translated;
            Main.Logger.LogInfo($"TranslateUs: [AddChat] \"{original}\" → \"{translated}\"");

            new MessageGroup(original, translated, sourcePlayer, bubble);
            bubble.SetText(translated);
            bubble.AlignChildren();
            Traverse.Create(chat).Method("AlignAllBubbles").GetValue();
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Toggle))]
    public class ChatControllerTogglePatch
    {
        public static void Postfix(ChatController __instance)
        {
            if (!__instance.IsOpenOrOpening) return;

            var scroller = __instance.GetComponentInChildren<Scroller>(true);
            if (scroller?.Inner == null) return;

            var pairs = new List<(ChatBubble bubble, MessageGroup group, string original)>();
            for (int i = 0; i < scroller.Inner.childCount; i++)
            {
                var bubble = scroller.Inner.GetChild(i).GetComponent<ChatBubble>();
                if (bubble == null) continue;

                var group = MessageGroup.FindByBubble(bubble);
                if (group == null || group.IsTranslated) continue;

                pairs.Add((bubble, group, group.OriginalMessage));
            }

            if (pairs.Count == 0) return;

            Main.Logger.LogInfo($"TranslateUs: [Toggle] Batch translating {pairs.Count} messages");
            __instance.StartCoroutine(BatchTranslate(__instance, pairs).WrapToIl2Cpp());
        }

        private static IEnumerator BatchTranslate(
            ChatController chat, List<(ChatBubble, MessageGroup, string)> pairs)
        {
            var messages = pairs.ConvertAll(p => p.Item3);
            var task = Translator.BatchTranslate(messages, forSending: false);
            while (!task.IsCompleted) yield return null;

            if (!task.IsCompletedSuccessfully) yield break;

            var results = task.Result;
            bool any = false;
            for (int i = 0; i < pairs.Count; i++)
            {
                var (bubble, group, original) = pairs[i];
                string translated = results[i].translated;
                if (translated == original) continue;

                Main.Logger.LogInfo($"TranslateUs: [Toggle] \"{original}\" → \"{translated}\"");
                group.CompleteTranslation(translated);
                bubble.SetText(translated);
                bubble.AlignChildren();
                any = true;
            }
            if (any)
                Traverse.Create(chat).Method("AlignAllBubbles").GetValue();
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public class HudManagerRightClickPatch
    {
        public static void Postfix()
        {
            if (!Input.GetMouseButtonDown(1)) return;

            var hud = DestroyableSingleton<HudManager>.Instance;
            if (hud == null || hud.Chat == null || !hud.Chat.IsOpenOrOpening) return;
            if (hud.UICamera == null) return;

            Vector3 pos = hud.UICamera.ScreenToWorldPoint(Input.mousePosition);
            pos.z = 0f;

            var scroller = hud.Chat.GetComponentInChildren<Scroller>(true);
            if (scroller?.Inner == null) return;

            for (int i = scroller.Inner.childCount - 1; i >= 0; i--)
            {
                var bubble = scroller.Inner.GetChild(i).GetComponent<ChatBubble>();
                if (bubble?.Background == null) continue;
                if (!bubble.Background.bounds.Contains(pos)) continue;

                var group = MessageGroup.FindByBubble(bubble);
                if (group == null) break;

                if (!group.IsTranslated)
                {
                    Main.Logger.LogInfo($"TranslateUs: [RC] Translating: \"{group.OriginalMessage}\"");
                    hud.StartCoroutine(TranslateOne(hud.Chat, bubble, group).WrapToIl2Cpp());
                }
                else
                {
                    bool showOrig = group.Toggle();
                    Main.Logger.LogInfo($"TranslateUs: [RC] → {(showOrig ? "Original" : "Translated")}");
                }
                break;
            }
        }

        private static IEnumerator TranslateOne(ChatController chat, ChatBubble bubble, MessageGroup group)
        {
            var task = Translator.Translate(group.OriginalMessage, null!, forSending: false);
            while (!task.IsCompleted) yield return null;
            if (!task.IsCompletedSuccessfully || task.Result.translated == group.OriginalMessage) yield break;

            Main.Logger.LogInfo($"TranslateUs: [RC] \"{group.OriginalMessage}\" → \"{task.Result.translated}\"");
            group.CompleteTranslation(task.Result.translated);
            bubble.SetText(task.Result.translated);
            bubble.AlignChildren();
            Traverse.Create(chat).Method("AlignAllBubbles").GetValue();
        }
    }
}
