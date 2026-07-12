using HarmonyLib;
using UnityEngine;
using TranslateUs.Resources;

namespace TranslateUs.Patches
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Awake))]
    public class PauseButtonPatch
    {
        private static Sprite? _pauseSprite;
        private static Sprite? _resumeSprite;
        private static SpriteRenderer? _buttonRenderer;
        private static GameObject? _pauseButton;

        public static void Postfix(ChatController __instance)
        {
            if (_pauseButton != null) return;

            var source = __instance.openKeyboardButton;

            if (source == null) return;

            _pauseButton = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            _pauseButton.name = "TranslatePauseButton";

            var localPos = _pauseButton.transform.localPosition;
            _pauseButton.transform.localPosition = new Vector3(localPos.x - 0.7f, localPos.y, localPos.z);

            _pauseSprite = ResourcesUtils.LoadSpriteFromAssembly("Stop.png");
            _resumeSprite = ResourcesUtils.LoadSpriteFromAssembly("Resume.png");

            var passive = _pauseButton.GetComponent<PassiveButton>();
            if (passive != null)
            {
                passive.OnClick.RemoveAllListeners();
                passive.OnClick.AddListener(new System.Action(Main.TogglePause));
            }

            _buttonRenderer = _pauseButton.GetComponentInChildren<SpriteRenderer>();

            Main.OnPauseChanged += OnPauseChanged;
            OnPauseChanged(Main.IsPaused);
        }

        private static void OnPauseChanged(bool isPaused)
        {
            if (_buttonRenderer == null) return;
            var sprite = isPaused ? _resumeSprite : _pauseSprite;
            if (sprite != null)
                _buttonRenderer.sprite = sprite;
        }
    }
}
