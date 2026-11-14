using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Pins a RectTransform to the device's safe area, so the HUD clears notches, the Dynamic
    /// Island and home indicators. Everything the player touches lives under one of these.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public class SafeAreaFitter : MonoBehaviour
    {
        [Header("Edges")]
        [Tooltip("Edges to inset. Turn an edge off for a panel meant to run full bleed to it — " +
                 "a dimming sheet or a background wants the whole screen, not the safe part.")]
        public bool insetTop = true;
        public bool insetBottom = true;
        public bool insetLeft = true;
        public bool insetRight = true;

        [Header("Breathing room")]
        // The safe area is where the system promises nothing of its own will be drawn. It is
        // not where a control is comfortable: on a Dynamic Island phone the inset ends a few
        // points below the pill, so a card pinned flush to it sits directly against the black
        // capsule and reads as though it is hanging off it. Apple's own apps leave a gap; so
        // does this. In canvas units, so it scales with the rest of the HUD.
        [Tooltip("Extra gap below the safe area's top edge, in canvas units.")]
        public float topPadding = 28f;
        [Tooltip("Extra gap above the safe area's bottom edge, in canvas units. Keeps the " +
                 "booster row clear of the home indicator's swipe strip.")]
        public float bottomPadding = 16f;
        [Tooltip("Extra gap at the sides, in canvas units.")]
        public float sidePadding = 0f;

        [Tooltip("Least gap kept at the top even on a phone with no inset at all — an SE, or " +
                 "an iPad. Without it the level card sits on the very edge of the glass.")]
        public float minimumTopGap = 28f;

        RectTransform _rect;
        Canvas _canvas;
        Rect _applied;
        Vector2Int _screen;
        float _scale = -1f;

        void OnEnable()
        {
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            Apply(true);
        }

        void OnTransformParentChanged() => _canvas = GetComponentInParent<Canvas>();

        void Update()
        {
            // The safe area changes on rotation and on some devices a frame or two after
            // launch, so it is cheaper to compare than to guess when to re-read it.
            Apply(false);
        }

        float ScaleFactor()
        {
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            // The root canvas is the one that carries the scaler; a nested canvas inherits it.
            if (_canvas == null) return 1f;
            var root = _canvas.rootCanvas != null ? _canvas.rootCanvas : _canvas;
            return root.scaleFactor > 0f ? root.scaleFactor : 1f;
        }

        void Apply(bool force)
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();

            Rect safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            float scale = ScaleFactor();

            if (!force && safe == _applied && size == _screen && Mathf.Approximately(scale, _scale))
                return;
            if (size.x <= 0 || size.y <= 0) return;

            _applied = safe;
            _screen = size;
            _scale = scale;

            // Padding is authored in canvas units so it matches the rest of the HUD; the safe
            // area is in pixels. One multiply is the whole conversion, and it has to be redone
            // whenever the scaler changes its mind — which it does on every resolution change.
            float top = insetTop ? size.y - safe.yMax : 0f;
            float bottom = insetBottom ? safe.yMin : 0f;
            float left = insetLeft ? safe.xMin : 0f;
            float right = insetRight ? size.x - safe.xMax : 0f;

            if (insetTop) top = Mathf.Max(top + topPadding * scale, minimumTopGap * scale);
            if (insetBottom) bottom += bottomPadding * scale;
            if (insetLeft) left += sidePadding * scale;
            if (insetRight) right += sidePadding * scale;

            // A padding big enough to cross the screen would invert the rect and flip the
            // whole HUD inside out, which is exactly the sort of thing that only shows up on
            // the one device nobody has. Clamped instead.
            float maxV = size.y * 0.4f;
            float maxH = size.x * 0.4f;
            top = Mathf.Clamp(top, 0f, maxV);
            bottom = Mathf.Clamp(bottom, 0f, maxV);
            left = Mathf.Clamp(left, 0f, maxH);
            right = Mathf.Clamp(right, 0f, maxH);

            var min = new Vector2(left / size.x, bottom / size.y);
            var max = new Vector2(1f - right / size.x, 1f - top / size.y);

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
