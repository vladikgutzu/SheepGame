using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// A button that stays on screen before it is available, dimmed, with a lock in place of
    /// its icon and the level it opens at in place of its name. Pressing it then shakes it
    /// rather than doing nothing. <see cref="UIUnlockGate"/> drives it.
    /// </summary>
    // Shown locked rather than hidden so the player knows the thing exists and has a level to
    // reach for it. It lives on the button's slot, which is always on, and shakes the button
    // inside the slot, so the slot's own layout position is never touched.
    public class UILockableButton : MonoBehaviour
    {
        public Button button;
        [Tooltip("The button's face, dimmed while locked.")]
        public Image face;
        [Tooltip("The button's own icon, swapped for the lock while locked.")]
        public Graphic icon;
        [Tooltip("Shown in place of the icon while locked.")]
        public GameObject lockMark;
        public TMP_Text label;
        [Tooltip("The label while open.")]
        public string openText = "DAILY RUN";
        [Tooltip("The label while locked. {0} is the level it opens at.")]
        public string lockedFormat = "LEVEL {0}";
        public Color lockedTint = new Color(0.62f, 0.66f, 0.74f, 1f);

        [Header("Refused press")]
        public float shakeTime = 0.35f;
        public float shakeDistance = 14f;

        bool _locked;
        float _shake;
        RectTransform _shaken;
        Color _openTint = Color.white;
        bool _tintKnown;

        public bool Locked => _locked;

        public void SetLocked(bool locked, int level)
        {
            // The face carries its colour as a tint on a white frame, so "open" is whatever
            // tint it was authored with, not white.
            if (!_tintKnown && face != null) { _openTint = face.color; _tintKnown = true; }
            _locked = locked;
            if (face != null) face.color = locked ? _openTint * lockedTint : _openTint;
            if (icon != null) icon.enabled = !locked;
            if (lockMark != null) lockMark.SetActive(locked);
            if (label != null) label.text = locked ? string.Format(lockedFormat, level) : openText;
        }

        /// <summary>The player pressed it while locked: a short shake and a knock.</summary>
        public void Refuse()
        {
            _shake = 1f;
            Haptics.Impact(Haptics.ImpactStyle.Rigid);
        }

        void Update()
        {
            if (_shake <= 0f) return;
            if (_shaken == null && button != null) _shaken = button.transform as RectTransform;
            if (_shaken == null) { _shake = 0f; return; }

            _shake = Mathf.Max(0f, _shake - Time.unscaledDeltaTime / Mathf.Max(0.05f, shakeTime));
            // Three swings that die away, easing to rest instead of stopping on a frame.
            float x = Mathf.Sin((1f - _shake) * Mathf.PI * 6f) * shakeDistance * _shake * _shake;
            var p = _shaken.anchoredPosition;
            p.x = x;
            _shaken.anchoredPosition = p;
        }
    }
}
