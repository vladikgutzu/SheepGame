using SheepGame.Core.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SheepGame.Core
{
    public sealed class SettingsPanel : MonoBehaviour
    {
        public GameObject root;
        public Button soundButton;
        public Button musicButton;
        public Button vibrationButton;
        public Button privacyButton;
        public Button closeButton;
        public TMP_Text soundValue;
        public TMP_Text musicValue;
        public TMP_Text vibrationValue;
        public PrivacyChoicesPanel privacyPanel;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            if (root == null) root = gameObject;
            soundButton?.onClick.AddListener(ToggleSound);
            musicButton?.onClick.AddListener(ToggleMusic);
            vibrationButton?.onClick.AddListener(ToggleVibration);
            privacyButton?.onClick.AddListener(OpenPrivacy);
            closeButton?.onClick.AddListener(Close);
        }

        void OnEnable()
        {
            GameState.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => GameState.Changed -= Refresh;

        public void Open()
        {
            root.SetActive(true);
            Refresh();
            Select(soundButton);
        }

        public void Close()
        {
            SheepGame.Audio.AudioManager.PanelClose();
            root.SetActive(false);
        }

        void ToggleSound()
        {
            GameState.SoundOn = !GameState.SoundOn;
            SheepGame.Audio.AudioManager.Tap();
        }

        void ToggleMusic()
        {
            GameState.MusicOn = !GameState.MusicOn;
            SheepGame.Audio.AudioManager.Tap();
        }

        void ToggleVibration()
        {
            GameState.HapticsOn = !GameState.HapticsOn;
            if (GameState.HapticsOn) Haptics.Selection();
            SheepGame.Audio.AudioManager.Tap();
        }

        void OpenPrivacy()
        {
            if (privacyPanel != null) privacyPanel.Open(this);
        }

        public void FocusAfterPrivacy() => Select(privacyButton);

        void Refresh()
        {
            SetValue(soundValue, GameState.SoundOn);
            SetValue(musicValue, GameState.MusicOn);
            SetValue(vibrationValue, GameState.HapticsOn);
        }

        static void SetValue(TMP_Text label, bool enabled)
        {
            if (label == null) return;
            label.text = enabled ? "ON" : "OFF";
            label.color = enabled ? Color.white : new Color32(255, 224, 216, 255);
        }

        static void Select(Selectable selectable)
        {
            if (EventSystem.current != null && selectable != null)
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }
}
