using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// Slides the canvas scaler's match between width and height as the screen's shape
    /// changes, so a screen shorter than the reference does not run the HUD off the bottom.
    /// </summary>
    // Matching on width alone is right for phones and only for phones. The HUD is authored at
    // 1080x1920, and every iPhone since the X is taller than that ratio, so matching width
    // gives the layout more vertical room than it asked for — which is why the game looks
    // right on them. An iPad is the other way round: 3:4 matched on width leaves 1440 units of
    // height for a layout drawn for 1920, and the level card at the top and the booster row at
    // the bottom close on the board from both sides until they meet over it.
    //
    // The blend, rather than a switch, because the awkward sizes are in between: an iPad mini
    // and an old 4:3 iPad are not the same shape, and a hard cut at one of them puts a visible
    // jump between two devices that ought to look alike.
    [RequireComponent(typeof(CanvasScaler))]
    [ExecuteAlways]
    public class CanvasScalerFitter : MonoBehaviour
    {
        [Tooltip("Aspect (width / height) at or below which the match stays on width. The " +
                 "reference resolution's own aspect, so every phone keeps today's layout.")]
        public float tallAspect = 0.5625f;

        [Tooltip("Aspect at which the match has moved fully onto height. 0.75 is a 4:3 iPad.")]
        public float wideAspect = 0.75f;

        CanvasScaler _scaler;
        Vector2Int _screen;

        void OnEnable()
        {
            _scaler = GetComponent<CanvasScaler>();
            Apply(true);
        }

        void Update() => Apply(false);

        void Apply(bool force)
        {
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null) return;

            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && size == _screen) return;
            if (size.x <= 0 || size.y <= 0) return;
            _screen = size;

            // Only meaningful for the mode it is describing; anything else is left alone
            // rather than quietly overwritten.
            if (_scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
            if (_scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight) return;

            float aspect = (float)size.x / size.y;
            float span = Mathf.Max(0.0001f, wideAspect - tallAspect);
            _scaler.matchWidthOrHeight = Mathf.Clamp01((aspect - tallAspect) / span);
        }
    }
}
