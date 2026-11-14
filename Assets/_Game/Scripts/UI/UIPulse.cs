using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Slow breathing scale, used on the ring that marks a booster as armed so the state
    /// reads as "waiting for you" rather than as decoration.
    /// </summary>
    public class UIPulse : MonoBehaviour
    {
        public float amplitude = 0.08f;
        public float cyclesPerSecond = 1.1f;

        RectTransform _rect;
        float _base = 1f;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _base = _rect.localScale.x;
        }

        void OnEnable()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            _rect.localScale = new Vector3(_base, _base, 1f);
        }

        void Update()
        {
            float s = _base + Mathf.Sin(Time.unscaledTime * cyclesPerSecond * Mathf.PI * 2f) * amplitude;
            _rect.localScale = new Vector3(s, s, 1f);
        }
    }
}
