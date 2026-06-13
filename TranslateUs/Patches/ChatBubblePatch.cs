using HarmonyLib;
using TranslateUs.Core;

namespace TranslateUs.Patches
{
    [HarmonyPatch(typeof(ChatBubble), nameof(ChatBubble.Reset))]
    public class ChatBubblePatch
    {
        public static void Prefix(ChatBubble __instance)
        {
            MessageGroup.RemoveBubble(__instance);
        }
    }
}
