using UnityEngine;
using UnityEngine.EventSystems;

namespace SheepGame.Core
{
    /// <summary>
    /// Press-and-release scale on a UI button. A tap on a flat sprite otherwise gives no
    /// sign it registered, which on a touch screen reads as a missed tap.
    /// </summary>
    public class UIButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public float pressedScale = 0.92f;
        public float speed = 16f;

        RectTransform _rect;
        float _target = 1f;

        void Awake() => _rect = (RectTransform)transform;

        void OnDisable()
        {
            _target = 1f;
            if (_rect != null) _rect.localScale = Vector3.one;
        }

        void Update()
        {
            float current = _rect.localScale.x;
            float next = Mathf.Lerp(current, _target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            _rect.localScale = new Vector3(next, next, 1f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _target = pressedScale;
            SheepGame.Audio.AudioManager.Tap();
        }

        public void OnPointerUp(PointerEventData eventData) => _target = 1f;
    }
}
