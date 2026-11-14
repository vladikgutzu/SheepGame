using System;
using TMPro;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The card that announces a change of rules вЂ” "Time Rush" вЂ” swings in, holds long
    /// enough to be read, and then lets go. Purely informative: nothing to press.
    /// </summary>
    // It reports when it has finished, and the round waits for that. A clock that starts
    // under the announcement of the clock would spend the player's first seconds telling them
    // they are being timed, which is the one thing an announcement must not do.
    public class UIModeBanner : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("The overlay switched on and off. Usually this object.")]
        public GameObject root;
        [Tooltip("The card that moves. Everything else fades with the group.")]
        public RectTransform card;
        public CanvasGroup group;
        public TMP_Text title;
        public TMP_Text subtitle;
        [Tooltip("Optional. The picture in the badge. A banner handing over a booster shows " +
                 "that booster; everything else keeps whatever the badge was built with.")]
        public UnityEngine.UI.Image badgeIcon;

        [Header("Timing")]
        [Tooltip("Swing in.")]
        public float inTime = 0.34f;
        [Tooltip("Held still, to be read. Two short words at this size take about a second.")]
        public float holdTime = 1.35f;
        [Tooltip("Let go.")]
        public float outTime = 0.28f;

        [Header("Motion")]
        [Tooltip("Scale it starts from, coming in.")]
        public float fromScale = 0.6f;
        [Tooltip("How far past 1 it swings before settling.")]
        public float overshoot = 0.14f;
        [Tooltip("Scale it grows to on the way out. Larger than 1 on purpose: shrinking " +
                 "reads as the card being dismissed, growing reads as the thing it announces " +
                 "taking effect.")]
        public float toScale = 1.12f;

        /// <summary>Raised once the banner is gone. The round starts here, not before.</summary>
        public event Action Finished;

        public bool IsPlaying { get; private set; }

        float _elapsed;
        Action _onFinished;
        Sprite _defaultIcon;
        bool _iconCached;

        void Awake()
        {
            if (root == null) root = gameObject;
            if (card == null) card = (RectTransform)transform;

            // Deliberately does NOT hide the root. This component sits on the overlay it
            // controls, so the object is inactive until Show activates it -- and activating it
            // is what runs Awake. Hiding here undid the very call that woke the component: the
            // banner reported itself playing while its own root sat switched off. The panel is
            // authored inactive by the builder instead, which is where that belongs.
        }

        /// <summary>Shows the banner. The callback and the event both fire when it is done.</summary>
        public void Show(string heading, string line, Action onFinished = null, Sprite icon = null)
        {
            if (badgeIcon != null)
            {
                if (!_iconCached) { _defaultIcon = badgeIcon.sprite; _iconCached = true; }
                badgeIcon.sprite = icon != null ? icon : _defaultIcon;
            }

            if (title != null && !string.IsNullOrEmpty(heading)) title.text = heading;
            if (subtitle != null && line != null) subtitle.text = line;

            _onFinished = onFinished;
            _elapsed = 0f;
            IsPlaying = true;

            if (root != null) root.SetActive(true);
            SheepGame.Audio.AudioManager.ModeSwoosh();
            Apply(0f);
        }

        /// <summary>
        /// Takes it off screen without reporting finished. For a level that ended, or was
        /// restarted, while its own announcement was still playing.
        /// </summary>
        // Deliberately silent: whatever was waiting on this banner belongs to a round that is
        // no longer being played, and starting its clock now would run it over the next one.
        public void Cancel()
        {
            IsPlaying = false;
            _onFinished = null;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Ends it early and still reports finished, so nothing is left waiting.</summary>
        public void Skip()
        {
            if (!IsPlaying) return;
            _elapsed = inTime + holdTime + outTime;
            Tick(0f);
        }

        // Unscaled time, so a slowed-down game does not slow the announcement, but it stops
        // outright while the game is paused.
        void Update()
        {
            if (!IsPlaying) return;
            // Held while the game is paused (PausePanel stops time). Otherwise a pause that
            // opens during the announcement -- the notification shade pulled down -- lets it
            // play out unseen underneath, and the clock starts on Resume with no warning.
            if (Time.timeScale <= 0f) return;
            Tick(Time.unscaledDeltaTime);
        }

        void Tick(float delta)
        {
            _elapsed += delta;

            float total = inTime + holdTime + outTime;
            if (_elapsed >= total)
            {
                IsPlaying = false;
                if (root != null) root.SetActive(false);

                var callback = _onFinished;
                _onFinished = null;
                callback?.Invoke();
                Finished?.Invoke();
                return;
            }

            Apply(_elapsed);
        }

        void Apply(float t)
        {
            float scale, alpha;

            if (t < inTime)
            {
                float k = inTime <= 0f ? 1f : t / inTime;
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                scale = Mathf.LerpUnclamped(fromScale, 1f, ease) + Mathf.Sin(k * Mathf.PI) * overshoot;
                alpha = Mathf.Clamp01(k * 2.2f);
            }
            else if (t < inTime + holdTime)
            {
                scale = 1f;
                alpha = 1f;
            }
            else
            {
                float k = outTime <= 0f ? 1f : (t - inTime - holdTime) / outTime;
                // Eased in rather than out on the way back: it sits still for a beat and then
                // goes, instead of drifting away from the moment it stopped.
                float ease = k * k;
                scale = Mathf.LerpUnclamped(1f, toScale, ease);
                alpha = 1f - ease;
            }

            if (card != null) card.localScale = new Vector3(scale, scale, 1f);
            if (group != null) group.alpha = alpha;
        }
    }
}
