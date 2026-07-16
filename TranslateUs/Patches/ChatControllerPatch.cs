using System.Collections;
using AmongUs.Data;
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
                    var group = new MessageGroup(pending.Value.original, sourcePlayer, bubble);
                    group.CompleteTranslation(pending.Value.translated);

                    if (!Main.TranslateOwnBubbles.Value)
                    {
                        bubble.SetText(pending.Value.original);
                        bubble.AlignChildren();
                        Traverse.Create(__instance).Method("AlignAllBubbles").GetValue();
                    }

                    Main.Logger.LogInfo($"TranslateUs: [AddChat-Self] \"{pending.Value.original}\" → \"{pending.Value.translated}\"");
                    MessageGroup.PendingSend = null;
                }
                else
                {
                    new MessageGroup(chatText, sourcePlayer, bubble);
                }
            }
            else
            {
                var group = new MessageGroup(chatText, sourcePlayer, bubble);
                Main.Logger.LogInfo($"TranslateUs: [AddChat-Other] Received: \"{chatText}\"");
                __instance.StartCoroutine(
                    TranslateAndUpdateCoroutine(__instance, group).WrapToIl2Cpp());
            }
        }

        private static IEnumerator TranslateAndUpdateCoroutine(ChatController chat, MessageGroup group)
        {
            var myLang = DataManager.Settings.Language.CurrentLanguage;
            var task = Translator.TranslateToMyLanguage(group.OriginalMessage, myLang);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted) yield break;
            if (task.Result.translated == group.OriginalMessage) yield break;

            string translated = task.Result.translated;
            Main.Logger.LogInfo($"TranslateUs: [AddChat-Other] \"{group.OriginalMessage}\" → \"{translated}\"");

            group.CompleteTranslation(translated);
            if (group.Bubble != null)
            {
                group.Bubble.SetText(translated);
                group.Bubble.AlignChildren();
                Traverse.Create(chat).Method("AlignAllBubbles").GetValue();
            }
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

            var toTranslate = new List<(ChatBubble bubble, MessageGroup group)>();

            for (int i = 0; i < scroller.Inner.childCount; i++)
            {
                var bubble = scroller.Inner.GetChild(i).GetComponent<ChatBubble>();
                if (bubble == null) continue;

                var group = MessageGroup.FindByBubble(bubble);
                if (group == null)
                {
                    group = new MessageGroup(bubble.TextArea.text, null!, bubble);
                    Main.Logger.LogInfo($"TranslateUs: [Toggle] Registered orphan: \"{group.OriginalMessage}\"");
                }
                
                if (!group.IsTranslated && !group.IsLocalPlayer)
                {
                    toTranslate.Add((bubble, group));
                }
            }

            if (toTranslate.Count == 0) return;

            Main.Logger.LogInfo($"TranslateUs: [Toggle] Batch translating {toTranslate.Count} messages");
            __instance.StartCoroutine(
                ToggleBatchTranslateCoroutine(__instance, toTranslate).WrapToIl2Cpp());
        }

        private static IEnumerator ToggleBatchTranslateCoroutine(
            ChatController chat, List<(ChatBubble bubble, MessageGroup group)> pairs)
        {
            var myLang = DataManager.Settings.Language.CurrentLanguage;
            var messages = pairs.ConvertAll(p => p.group.OriginalMessage);
            var task = Translator.BatchTranslateToMyLanguage(messages, myLang);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted) yield break;

            var results = task.Result;
            bool any = false;
            for (int i = 0; i < pairs.Count; i++)
            {
                var (bubble, group) = pairs[i];
                string translated = results[i].translated;
                if (translated == group.OriginalMessage) continue;

                Main.Logger.LogInfo($"TranslateUs: [Toggle] \"{group.OriginalMessage}\" → \"{translated}\"");
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
                if (group == null)
                {
                    group = new MessageGroup(bubble.TextArea.text, null!, bubble);
                    Main.Logger.LogInfo($"TranslateUs: [RC] Registered orphan for translation");
                }

                if (!group.IsTranslated)
                {
                    Main.Logger.LogInfo($"TranslateUs: [RC] Translating: \"{group.OriginalMessage}\"");
                    hud.StartCoroutine(
                        RightClickTranslateCoroutine(hud.Chat, group).WrapToIl2Cpp());
                }
                else
                {
                    bool showingOriginal = group.Toggle();
                    Main.Logger.LogInfo($"TranslateUs: [RC] Toggled → {(showingOriginal ? "Original" : "Translated")}");
                }
                break;
            }
        }

        private static IEnumerator RightClickTranslateCoroutine(ChatController chat, MessageGroup group)
        {
            var myLang = DataManager.Settings.Language.CurrentLanguage;
            var task = Translator.TranslateToMyLanguage(group.OriginalMessage, myLang);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted) yield break;
            if (task.Result.translated == group.OriginalMessage) yield break;

            Main.Logger.LogInfo($"TranslateUs: [RC] \"{group.OriginalMessage}\" → \"{task.Result.translated}\"");
            group.CompleteTranslation(task.Result.translated);
            if (group.Bubble != null)
            {
                group.Bubble.SetText(task.Result.translated);
                group.Bubble.AlignChildren();
                Traverse.Create(chat).Method("AlignAllBubbles").GetValue();
            }
        }
    }
}
