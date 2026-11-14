using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Sweeps a bright bar across a button every few seconds, the way a hyper-casual game
    /// keeps its primary action alive without moving it.
    /// </summary>
    /// <remarks>
    /// The bar is a child of the button and the button carries a RectMask2D, so the sweep is
    /// clipped to the button's own rectangle: there is no soft mask, no extra material and
    /// no second draw beyond the bar itself.
    ///
    /// It runs on unscaled time and it idles between passes — a shine that never stops
    /// reads as a loading spinner rather than as an invitation.
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class UIShine : MonoBehaviour
    {
        [Tooltip("Seconds from one sweep to the next.")]
        public float period = 3.2f;

        [Tooltip("Seconds a single sweep takes.")]
        public float duration = 0.55f;

        [Tooltip("How far past each edge the bar starts and ends, as a fraction of the width.")]
        public float overshoot = 0.7f;

        RectTransform _rect;
        RectTransform _parent;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _parent = _rect.parent as RectTransform;
        }

        void OnEnable()
        {
            if (_rect == null) Awake();
            Place(0f);
        }

        void Update()
        {
            if (_parent == null) return;

            float t = Mathf.Repeat(Time.unscaledTime, Mathf.Max(duration, period));
            if (t > duration) { Place(0f); return; }

            // Eased, so the bar arrives and leaves quickly and lingers across the face.
            float k = t / duration;
            Place(Mathf.SmoothStep(0f, 1f, k));
        }

        void Place(float k)
        {
            float width = _parent.rect.width;
            float from = -width * (0.5f + overshoot);
            float to = width * (0.5f + overshoot);
            _rect.anchoredPosition = new Vector2(Mathf.Lerp(from, to, k), 0f);
            // Hidden while it waits off the edge, rather than parked visibly at one end.
            _rect.localScale = new Vector3(1f, 1f, 1f);
        }
    }
}
