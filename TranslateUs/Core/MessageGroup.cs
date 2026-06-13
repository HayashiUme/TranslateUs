namespace TranslateUs.Core
{
    public class MessageGroup
    {
        public string OriginalMessage { get; }
        public string TranslatedMessage { get; private set; }
        public PlayerControl SourcePlayer { get; }
        public ChatBubble? Bubble { get; }
        public bool IsTranslated { get; private set; }
        
        public static Dictionary<ChatBubble, MessageGroup> BubbleGroups { get; } = new();
        public static (string original, string translated)? PendingSend { get; set; }

        public MessageGroup(string text, PlayerControl sourcePlayer, ChatBubble? bubble = null)
        {
            OriginalMessage = text;
            TranslatedMessage = text;
            SourcePlayer = sourcePlayer;
            Bubble = bubble;
            IsTranslated = false;

            if (bubble != null)
                BubbleGroups[bubble] = this;
        }

        public MessageGroup(string original, string translated, PlayerControl sourcePlayer, ChatBubble? bubble = null)
        {
            OriginalMessage = original;
            TranslatedMessage = translated;
            SourcePlayer = sourcePlayer;
            Bubble = bubble;
            IsTranslated = true;

            if (bubble != null)
                BubbleGroups[bubble] = this;
        }

        public void CompleteTranslation(string translated)
        {
            TranslatedMessage = translated;
            IsTranslated = true;
        }

        public bool Toggle()
        {
            if (Bubble == null || !IsTranslated)
                return false;

            bool showOriginal = Bubble.TextArea.text != TranslatedMessage;
            Bubble.SetText(showOriginal ? OriginalMessage : TranslatedMessage);
            Bubble.AlignChildren();

            var chat = DestroyableSingleton<HudManager>.Instance?.Chat;
            if (chat != null)
                HarmonyLib.Traverse.Create(chat).Method("AlignAllBubbles").GetValue();

            return showOriginal;
        }

        public static MessageGroup? FindByBubble(ChatBubble bubble)
        {
            BubbleGroups.TryGetValue(bubble, out var group);
            return group;
        }

        public static void RemoveBubble(ChatBubble bubble)
        {
            BubbleGroups.Remove(bubble);
        }
    }
}
