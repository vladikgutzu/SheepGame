using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// A hand that taps, over a case on the board or a button on the HUD. The first level
    /// teaches with it instead of with words.
    /// </summary>
    // The component sits on an object that is always on and switches a child, because a
    // component on the thing it hides would never be woken to show it again.
    public class UITutorialHand : MonoBehaviour
    {
        [Tooltip("What is switched on and off.")]
        public GameObject body;
        [Tooltip("The hand. Its pivot is the fingertip.")]
        public RectTransform hand;
        [Tooltip("Optional. A ring that spreads from the fingertip on every tap.")]
        public RectTransform ring;
        public CanvasGroup ringGroup;

        [Tooltip("Seconds of one press-and-lift.")]
        public float cycle = 1.3f;
        [Tooltip("How far below and right of the target the hand rests between taps, in canvas units.")]
        public Vector2 restOffset = new Vector2(34f, -46f);
        [Tooltip("How deep a press goes, as a scale.")]
        public float pressScale = 0.86f;
        [Tooltip("Seconds the hand takes to glide to a new target instead of jumping there.")]
        public float glideTime = 0.22f;
        [Tooltip("Seconds the hand takes to fade in when it first appears.")]
        public float fadeIn = 0.25f;

        // The beats of one cycle: reach in, press, lift back to rest.
        const float ReachEnd = 0.38f;
        const float PressEnd = 0.58f;

        Transform _world;
        Vector3 _worldOffset;
        Camera _camera;
        RectTransform _ui;
        float _time;
        float _shownFor;
        Canvas _canvas;
        CanvasGroup _group;
        Vector3 _anchor;
        Vector3 _anchorVelocity;
        bool _hasAnchor;

        public bool Visible => body != null && body.activeSelf;

        /// <summary>Points at something in the scene -- a case -- and follows it.</summary>
        public void PointAt(Transform target, Camera camera, Vector3 worldOffset = default)
        {
            bool moved = target != _world;
            _world = target;
            _worldOffset = worldOffset;
            _camera = camera;
            _ui = null;
            Show(moved);
        }

        /// <summary>Points at something on the HUD.</summary>
        public void PointAt(RectTransform target)
        {
            bool moved = target != _ui;
            _ui = target;
            _world = null;
            Show(moved);
        }

        public void Hide()
        {
            _world = null;
            _ui = null;
            _hasAnchor = false;
            if (body != null) body.SetActive(false);
        }

        void Show(bool newTarget)
        {
            if (body != null && !body.activeSelf)
            {
                body.SetActive(true);
                _hasAnchor = false;
                _shownFor = 0f;
            }
            // A new target starts the cycle from rest, so the hand glides over and then reaches
            // in, rather than pressing mid-flight.
            if (newTarget) _time = 0f;
            Place(Mathf.Repeat(_time / Mathf.Max(0.1f, cycle), 1f), 0f);
        }

        void LateUpdate()
        {
            if (!Visible) return;

            // The target went away -- the case left, the button was hidden.
            if ((_world == null && _ui == null) ||
                (_world != null && !_world.gameObject.activeInHierarchy) ||
                (_ui != null && !_ui.gameObject.activeInHierarchy))
            {
                Hide();
                return;
            }

            // Capped, because the frame a level loads in is a long one: uncapped, the hand's
            // very first frame lands half way through a press and the fade-in is skipped.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _time += dt;
            _shownFor += dt;
            Place(Mathf.Repeat(_time / Mathf.Max(0.1f, cycle), 1f), dt);
        }

        // Ease in and out with zero velocity and acceleration at both ends (smootherstep):
        // SmoothStep still starts and stops with a visible jolt in acceleration.
        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        void Place(float phase, float dt)
        {
            if (hand == null) return;

            if (_group == null && body != null)
            {
                _group = body.GetComponent<CanvasGroup>();
                if (_group == null) _group = body.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
            if (_group != null) _group.alpha = fadeIn > 0f ? Ease(_shownFor / fadeIn) : 1f;

            Vector3 screen;
            if (_world != null)
            {
                var cam = _camera != null ? _camera : Camera.main;
                if (cam == null) return;
                screen = cam.WorldToScreenPoint(_world.position + _worldOffset);
                if (screen.z < 0f) return;
            }
            else if (_ui != null) screen = _ui.position;
            else return;

            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            float scale = _canvas != null ? _canvas.rootCanvas.scaleFactor : 1f;

            // The point the hand works around glides to a new target instead of jumping, and
            // follows a moving one without jitter.
            var target = new Vector3(screen.x, screen.y, 0f);
            if (!_hasAnchor || dt <= 0f)
            {
                if (!_hasAnchor) { _anchor = target; _anchorVelocity = Vector3.zero; _hasAnchor = true; }
            }
            else
            {
                _anchor = Vector3.SmoothDamp(_anchor, target, ref _anchorVelocity,
                                             Mathf.Max(0.01f, glideTime), Mathf.Infinity, dt);
            }

            // Rests off the target, reaches in, presses, lifts back.
            float reach = phase < ReachEnd ? Ease(phase / ReachEnd)
                        : phase < PressEnd ? 1f
                        : 1f - Ease((phase - PressEnd) / (1f - PressEnd));
            Vector3 offset = (Vector3)(restOffset * (1f - reach)) * scale;
            hand.position = _anchor + offset;

            // One smooth dip and back while the finger is down, not a two-frame snap.
            float press = phase >= ReachEnd && phase < PressEnd
                ? Mathf.Sin((phase - ReachEnd) / (PressEnd - ReachEnd) * Mathf.PI)
                : 0f;
            hand.localScale = Vector3.one * Mathf.Lerp(1f, pressScale, press);

            if (ring != null)
            {
                // Fired at the bottom of the press, spreading with an ease-out and fading in
                // over its first tenth rather than appearing at full strength.
                const float fire = (ReachEnd + PressEnd) * 0.5f;
                float spread = Mathf.Clamp01((phase - fire) / 0.42f);
                float grow = 1f - (1f - spread) * (1f - spread);
                ring.position = _anchor;
                ring.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.4f, grow);
                if (ringGroup != null)
                    ringGroup.alpha = phase < fire ? 0f
                                    : spread < 0.1f ? spread / 0.1f
                                    : 1f - Ease((spread - 0.1f) / 0.9f);
            }
        }
    }
}
