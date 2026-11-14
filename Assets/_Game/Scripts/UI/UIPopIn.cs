using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Scale-and-fade entrance played whenever the object is enabled. Panels are shown by
    /// SetActive from game code, so the animation hangs off OnEnable rather than needing
    /// anyone to call it.
    /// </summary>
    public class UIPopIn : MonoBehaviour
    {
        public float duration = 0.28f;
        public float fromScale = 0.82f;
        [Tooltip("How far past 1 the scale swings before settling. 0 is a plain ease-out.")]
        public float overshoot = 0.12f;
        public CanvasGroup fadeGroup;

        RectTransform _rect;
        float _t;

        void Awake() => _rect = (RectTransform)transform;

        void OnEnable()
        {
            _t = 0f;
            ApplyFrame(0f);
        }

        void Update()
        {
            if (_t >= 1f) return;
            _t = duration <= 0f ? 1f : Mathf.Min(1f, _t + Time.unscaledDeltaTime / duration);
            ApplyFrame(_t);
        }

        void ApplyFrame(float t)
        {
            if (_rect == null) _rect = (RectTransform)transform;

            float ease = 1f - Mathf.Pow(1f - t, 3f);
            float pop = Mathf.Sin(t * Mathf.PI) * overshoot;
            float s = Mathf.LerpUnclamped(fromScale, 1f, ease) + pop;
            _rect.localScale = new Vector3(s, s, 1f);

            if (fadeGroup != null) fadeGroup.alpha = Mathf.Clamp01(t * 2f);
        }
    }
}
