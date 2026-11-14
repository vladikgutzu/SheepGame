using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// The clock during a Time Rush: its own chip under the level card, on screen only while
    /// a round is timed. It never writes the number — GameManager owns the text.
    /// </summary>
    // A chip of its own rather than the line at the foot of the level card. Sharing that line
    // with the tap counter meant the same number in the same place meant two different things
    // depending on the level, and a player mid-round has no reason to work out which — the
    // whole point of a mode is that you can see you are in it.
    public class UITimerPill : MonoBehaviour
    {
        public GameManager gameManager;

        [Header("Parts")]
        [Tooltip("Switched on and off with the mode. Usually this object.")]
        public GameObject root;
        [Tooltip("Seconds left. Written by GameManager, never here.")]
        public TMP_Text label;
        [Tooltip("The pill's fill, recoloured as the clock runs down.")]
        public Image fill;
        public Image clock;
        [Tooltip("Scaled on each of the closing seconds.")]
        public RectTransform popTarget;

        [Header("Warning")]
        [Tooltip("Seconds left at or below which the pill turns and starts ticking.")]
        public int warnAtSeconds = 10;
        public Color normalFill = new Color(1f, 0.776f, 0.208f);      // amber
        public Color warningFill = new Color(1f, 0.42f, 0.353f);      // coral
        public Color normalInk = new Color(0.137f, 0.196f, 0.29f);    // ink
        public Color warningInk = Color.white;

        [Header("Pop")]
        public float popScale = 1.16f;
        public float popTime = 0.24f;

        float _pop;
        int _lastSecond = int.MinValue;

        void Awake()
        {
            if (root == null) root = gameObject;
            // Not hidden here: this sits on the object it hides, and on a panel that starts
            // inactive the hiding would fight whatever activated it. The builder authors it off.
        }

        void OnEnable()
        {
            if (gameManager == null) return;
            gameManager.ModeChanged += HandleMode;
            gameManager.TimeChanged += HandleTime;
        }

        void OnDisable()
        {
            if (gameManager == null) return;
            gameManager.ModeChanged -= HandleMode;
            gameManager.TimeChanged -= HandleTime;
        }

        void Start()
        {
            // The mode is announced when a level loads, which on the first level happens
            // before this has been enabled. Ask once rather than wait for the next change.
            if (gameManager != null) HandleMode(gameManager.Mode);
        }

        void HandleMode(LevelMode mode)
        {
            _lastSecond = int.MinValue;
            _pop = 0f;
            if (popTarget != null) popTarget.localScale = Vector3.one;

            Paint(warn: false);
            if (root != null) root.SetActive(mode == LevelMode.TimeRush);
        }

        void HandleTime(float secondsLeft, float budget)
        {
            bool warn = secondsLeft <= warnAtSeconds;
            Paint(warn);

            // Only the closing seconds tick. A pill that pops every second of a minute-long
            // round is constant motion, and constant motion stops being a warning.
            int whole = Mathf.CeilToInt(secondsLeft);
            if (whole != _lastSecond && warn)
            {
                _pop = 1f;
                SheepGame.Audio.AudioManager.TimerTick();
            }
            _lastSecond = whole;
        }

        void Paint(bool warn)
        {
            if (fill != null) fill.color = warn ? warningFill : normalFill;
            if (label != null) label.color = warn ? warningInk : normalInk;
            if (clock != null) clock.color = warn ? warningInk : normalInk;
        }

        void Update()
        {
            if (popTarget == null || _pop <= 0f) return;

            _pop = Mathf.Max(0f, _pop - Time.unscaledDeltaTime / Mathf.Max(0.01f, popTime));
            float scale = 1f + (popScale - 1f) * Mathf.Sin(_pop * Mathf.PI);
            popTarget.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
