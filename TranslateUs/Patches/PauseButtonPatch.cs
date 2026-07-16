using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using TranslateUs.Resources;

namespace TranslateUs.Patches
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Awake))]
    public class PauseButtonPatch
    {
        private static Sprite? _pauseSprite;
        private static Sprite? _resumeSprite;
        private static GameObject? _pauseButton;

        public static void Postfix(ChatController __instance)
        {
            if (_pauseButton != null && _pauseButton)
                return;

            var source = __instance.openKeyboardButton;
            if (source == null) return;

            _pauseButton = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            _pauseButton.name = "TranslatePauseButton";

            var localPos = _pauseButton.transform.localPosition;
            _pauseButton.transform.localPosition = new Vector3(-3.9f, -2.7f, localPos.z);

            _pauseSprite = ResourcesUtils.LoadSpriteFromAssembly("Stop.png",50f);
            _resumeSprite = ResourcesUtils.LoadSpriteFromAssembly("Resume.png",50f);

            var passive = _pauseButton.GetComponent<PassiveButton>();
            if (passive != null)
            {
                passive.OnClick.RemoveAllListeners();
                passive.OnClick.AddListener((UnityAction)Main.TogglePause);
            }

            Main.OnPauseChanged += OnPauseChanged;
            SetButtonSprite(Main.IsPaused);
        }

        private static void OnPauseChanged(bool isPaused)
        {
            SetButtonSprite(isPaused);
        }
        
        private static void SetButtonSprite(bool isPaused)
        {
            if (_pauseButton == null || !_pauseButton) return;

            var sprite = isPaused ? _resumeSprite : _pauseSprite;
            if (sprite == null) return;

            foreach (var sr in _pauseButton.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.sprite = sprite;
            }
        }
    }
}
