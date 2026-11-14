using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Rounded fill bar driven by a 0..1 value. The fill is a sliced sprite resized by its
    /// anchor rather than an Image set to Filled, so a nearly empty bar is still a pill
    /// with two round ends instead of a clipped rectangle.
    /// </summary>
    public class UIProgressBar : MonoBehaviour
    {
        public RectTransform fill;
        [Tooltip("Seconds the bar takes to travel to a new value. 0 snaps.")]
        public float travelTime = 0.25f;
        [Tooltip("Fraction below which the fill is hidden — a sliced sprite squeezed to a " +
                 "few pixels renders as a smear.")]
        public float hideBelow = 0.02f;

        float _shown;
        float _target;

        public void SetProgress(float value, bool instant = false)
        {
            _target = Mathf.Clamp01(value);
            if (instant || travelTime <= 0f) _shown = _target;
            Apply();
        }

        void Update()
        {
            if (Mathf.Approximately(_shown, _target)) return;
            _shown = Mathf.MoveTowards(_shown, _target, Time.deltaTime / Mathf.Max(0.01f, travelTime));
            Apply();
        }

        void Apply()
        {
            if (fill == null) return;

            bool visible = _shown > hideBelow;
            if (fill.gameObject.activeSelf != visible) fill.gameObject.SetActive(visible);
            if (!visible) return;

            var max = fill.anchorMax;
            max.x = _shown;
            fill.anchorMax = max;
            fill.offsetMax = new Vector2(0f, fill.offsetMax.y);
            fill.offsetMin = new Vector2(0f, fill.offsetMin.y);
        }
    }
}
