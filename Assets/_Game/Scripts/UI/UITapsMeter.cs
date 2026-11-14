using TMPro;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The tap counter at the foot of the level card. Colours and pops it as the budget runs
    /// low, and takes it off screen on a level that has no budget. It never writes the
    /// number: GameManager owns the text, so there is only ever one writer.
    /// </summary>
    // Running out has to be visible several taps, or several seconds, before it happens, or
    // losing reads as the game stopping for no reason. The colour change is the warning and
    // the pop is what catches the eye on a screen where the player is looking at the board.
    public class UITapsMeter : MonoBehaviour
    {
        public GameManager gameManager;
        public TMP_Text label;
        [Tooltip("The word beside the number. Hidden with it, because a caption standing on " +
                 "its own over an empty space is a label for nothing.")]
        public TMP_Text caption;

        [Tooltip("Scaled on every change, so the counter is noticed at all.")]
        public RectTransform popTarget;

        [Header("Warning")]
        [Tooltip("Taps left at or below which the counter turns to the warning colour.")]
        public int warnAt = 5;
        public Color normalColour = new Color(0.36f, 0.45f, 0.57f);
        public Color warningColour = new Color(1f, 0.42f, 0.35f);

        [Header("Pop")]
        public float popScale = 1.22f;
        public float popTime = 0.22f;

        float _pop;
        int _lastTaps = int.MinValue;

        void OnEnable()
        {
            if (gameManager != null) gameManager.LevelStateChanged += HandleLevelState;
        }

        void OnDisable()
        {
            if (gameManager != null) gameManager.LevelStateChanged -= HandleLevelState;
        }

        void HandleLevelState(int level, int tapsLeft, int budget)
        {
            // A level with no tap limit -- the tutorial -- has nothing to count, so the whole
            // readout goes, number and word together. Hiding only the number left "TAPS" alone
            // at the end of the card with nothing beside it, which reads as a bug rather than
            // as an absence.
            bool hidden = budget <= 0;
            Show(!hidden);
            if (hidden) return;

            if (label != null)
                label.color = tapsLeft <= warnAt ? warningColour : normalColour;

            // Only a spend pops. A level load setting the counter to its full budget
            // would otherwise pop on every restart, which makes the pop mean nothing.
            if (_lastTaps != int.MinValue && tapsLeft < _lastTaps) _pop = 1f;
            _lastTaps = tapsLeft;
        }

        void Show(bool visible)
        {
            if (label != null) label.gameObject.SetActive(visible);
            if (caption != null) caption.gameObject.SetActive(visible);
        }

        void Update()
        {
            if (popTarget == null || _pop <= 0f) return;

            _pop = Mathf.Max(0f, _pop - Time.unscaledDeltaTime / Mathf.Max(0.01f, popTime));
            // Ease out of the pop rather than into it: the size lands the moment the tap
            // is spent and settles afterwards.
            float scale = 1f + (popScale - 1f) * Mathf.Sin(_pop * Mathf.PI);
            popTarget.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
