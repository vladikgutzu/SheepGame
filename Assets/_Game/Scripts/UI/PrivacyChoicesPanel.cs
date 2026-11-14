using SheepGame.Ads;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SheepGame.Core
{
    public sealed class PrivacyChoicesPanel : MonoBehaviour
    {
        public GameObject root;
        public Button personalizedButton;
        public Button limitedButton;
        public Button closeButton;

        SettingsPanel _returnTo;

        void Awake()
        {
            if (root == null) root = gameObject;
            personalizedButton?.onClick.AddListener(() => Choose(true));
            limitedButton?.onClick.AddListener(() => Choose(false));
            closeButton?.onClick.AddListener(Close);
        }

        public void Open(SettingsPanel returnTo)
        {
            _returnTo = returnTo;
            root.SetActive(true);
            if (closeButton != null) closeButton.gameObject.SetActive(LevelPlayPrivacy.HasChoice);
            if (EventSystem.current != null && personalizedButton != null)
                EventSystem.current.SetSelectedGameObject(personalizedButton.gameObject);
        }

        void Choose(bool personalized)
        {
            root.SetActive(false);
            LevelPlayPrivacy.SetChoice(personalized);
            ReturnToSettings();
        }

        void Close()
        {
            if (!LevelPlayPrivacy.HasChoice) return;
            root.SetActive(false);
            ReturnToSettings();
        }

        void ReturnToSettings()
        {
            SettingsPanel target = _returnTo;
            _returnTo = null;
            if (target != null && target.IsOpen) target.FocusAfterPrivacy();
        }
    }
}
