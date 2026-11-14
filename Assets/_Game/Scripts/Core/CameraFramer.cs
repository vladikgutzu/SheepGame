using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // Pulls the camera in or out so each level's apron fills the same share of the
    // screen. Only the distance along the camera's own forward axis is touched — the
    // pitch and yaw stay exactly as they were dialled in by hand.
    public class CameraFramer : MonoBehaviour
    {
        public Camera target;

        // Past 1 the apron is allowed to run off that edge of the frame, which is how the
        // board is pushed close enough for the pieces to be worth tapping: the rectangle
        // being fitted is the whole field plus its belt lane, and holding all of that on
        // screen leaves the pieces small. The sliders reach past 1 because the scene relies
        // on it — clamped at 1 an inspector edit would silently pull the camera back out.
        [Tooltip("Share of the frame width the apron should occupy. Over 1 crops its sides.")]
        [Range(0.4f, 1.6f)] public float fillWidth = 0.95f;
        [Tooltip("Share of the frame height the apron should occupy. Over 1 crops its ends.")]
        [Range(0.4f, 1.6f)] public float fillHeight = 0.92f;

        [Tooltip("Shifts the aim point away from the camera as a fraction of the apron's " +
                 "depth, which slides the board down-screen to leave room for UI. A " +
                 "fraction rather than a fixed distance, or small levels — framed from " +
                 "much closer — get shoved off the bottom of the screen.")]
        [Range(-0.5f, 0.5f)] public float focusShift = 0.16f;

        [Tooltip("Extra world-space nudge on top of the shift, for hand tuning.")]
        public Vector3 focusOffset = Vector3.zero;

        [Tooltip("Fit the board inside the safe area rather than the whole screen, so no case " +
                 "is ever drawn under the Dynamic Island or the home indicator. Costs roughly " +
                 "a tenth of the board's size on a notched iPhone — buy it back on the fill " +
                 "sliders above, not by turning this off.")]
        public bool respectSafeArea = true;

        [Header("Extra points")]
        [Tooltip("Points handed in besides the rectangle -- the check-in lane and its terminal -- are " +
                 "held fully on screen, this far in from the sides, in clip units (2 = the whole width).")]
        public float includeSide = 0.06f;
        [Tooltip("How far above the bottom edge they are held, for the booster buttons.")]
        public float includeBottom = 0.2f;
        [Tooltip("How far below the top edge, for the level card.")]
        public float includeTop = 0.22f;

        public float minDistance = 30f;
        public float maxDistance = 600f;

        [Tooltip("Seconds to ease between levels. 0 snaps immediately.")]
        public float glideTime = 0.35f;

        Vector3 _desired;
        Vector3 _velocity;
        bool _hasTarget;
        readonly Vector3[] _corners = new Vector3[4];

        // The last frame that was asked for, so it can be asked for again when the shape of
        // the screen changes underneath it. Rotating the phone, or the safe area arriving a
        // frame or two after launch on a notched device, both leave a board framed for a
        // screen that no longer exists.
        Transform _lastBoard;
        Vector3 _lastCentre;
        float _lastHalfWidth, _lastHalfDepth;
        readonly List<Vector3> _include = new List<Vector3>();
        Vector2 _safeCentre = Vector2.zero;
        Vector2 _safeHalf = Vector2.one;

        // Worked out once per Frame call rather than inside the search, which runs the test
        // two dozen times over.
        Vector2 _boxCentre = Vector2.zero;
        Vector2 _boxHalf = Vector2.one;

        void Reset() => target = Camera.main;

        void OnEnable() => DeviceRuntime.ViewportChanged += Refit;
        void OnDisable() => DeviceRuntime.ViewportChanged -= Refit;

        void Refit()
        {
            if (_lastBoard == null || _lastHalfWidth <= 0f) return;
            Frame(_lastBoard, _lastCentre, _lastHalfWidth, _lastHalfDepth, immediate: false,
                  include: new List<Vector3>(_include));
        }

        static Vector3 Ground(Vector3 axis) => Vector3.ProjectOnPlane(axis, Vector3.up).normalized;

        // Closes in on the nearest distance that still keeps all four corners inside the
        // fill box. Projected through the camera rather than solved in closed form: the
        // old single sin(pitch) correction is only right for a flat-on view, and at this
        // game's 62° tilt the near edge of the board sits far closer to the lens than the
        // far edge and projects much larger — the board overran the bottom of a 9:16 phone
        // frame while the arithmetic reported it comfortably inside.
        //
        // Monotonic (further away is always smaller on screen), so a bisection converges
        // without needing to care about aspect, tilt or field of view.
        float Fit(Camera cam, Vector3 focus, Vector3 forward)
        {
            float lo = minDistance, hi = Mathf.Max(minDistance, maxDistance);
            if (Fits(cam, focus - forward * lo)) return lo;

            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Fits(cam, focus - forward * mid)) hi = mid; else lo = mid;
            }
            return hi;
        }

        // The box the corners have to land inside, in clip space, where -1..1 is the whole
        // frame. Without a safe area it is the symmetric box the fill sliders describe; with
        // one it is that box shrunk onto the safe rectangle and re-centred on it — which on a
        // notched iPhone is a little smaller and sits a little low, exactly as the glass does.
        void FitBox(out Vector2 centre, out Vector2 half) => FitBox(out centre, out half, fillWidth, fillHeight);

        void FitBox(out Vector2 centre, out Vector2 half, float fillWidth, float fillHeight)
        {
            centre = Vector2.zero;
            half = new Vector2(fillWidth, fillHeight);
            if (!respectSafeArea) return;

            float w = Screen.width, h = Screen.height;
            if (w <= 0f || h <= 0f) return;

            Rect safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) return;

            // Pixels to clip space. A screen with no insets gives back exactly the symmetric
            // box above, so a device without a notch is framed as it always was.
            float xMin = safe.xMin / w * 2f - 1f;
            float xMax = safe.xMax / w * 2f - 1f;
            float yMin = safe.yMin / h * 2f - 1f;
            float yMax = safe.yMax / h * 2f - 1f;

            centre = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            half = new Vector2((xMax - xMin) * 0.5f * fillWidth,
                               (yMax - yMin) * 0.5f * fillHeight);
        }

        bool Fits(Camera cam, Vector3 from)
        {
            // Projected by hand from a candidate position: actually moving the camera to
            // test each one would have it stutter through every step of the search.
            Matrix4x4 view = Matrix4x4.TRS(from, cam.transform.rotation,
                                           new Vector3(1f, 1f, -1f)).inverse;
            Matrix4x4 viewProjection = cam.projectionMatrix * view;

            foreach (var corner in _corners)
            {
                Vector4 clip = viewProjection * new Vector4(corner.x, corner.y, corner.z, 1f);
                if (clip.w <= 0.0001f) return false;
                if (Mathf.Abs(clip.x / clip.w - _boxCentre.x) > _boxHalf.x) return false;
                if (Mathf.Abs(clip.y / clip.w - _boxCentre.y) > _boxHalf.y) return false;
            }

            // Held to the real screen rather than the fill box, which is allowed past the edge.
            foreach (var point in _include)
            {
                Vector4 clip = viewProjection * new Vector4(point.x, point.y, point.z, 1f);
                if (clip.w <= 0.0001f) return false;
                float x = clip.x / clip.w - _safeCentre.x;
                float y = clip.y / clip.w - _safeCentre.y;
                if (Mathf.Abs(x) > _safeHalf.x - includeSide) return false;
                if (y < -_safeHalf.y + includeBottom || y > _safeHalf.y - includeTop) return false;
            }
            return true;
        }

        // How unevenly the extra points sit between the top and bottom limits, seen from here: zero
        // when the room left above them equals the room left below.
        float VerticalBalance(Camera cam, Vector3 from)
        {
            Matrix4x4 view = Matrix4x4.TRS(from, cam.transform.rotation, new Vector3(1f, 1f, -1f)).inverse;
            Matrix4x4 viewProjection = cam.projectionMatrix * view;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var point in _include)
            {
                Vector4 clip = viewProjection * new Vector4(point.x, point.y, point.z, 1f);
                if (clip.w <= 0.0001f) continue;
                float y = clip.y / clip.w - _safeCentre.y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            float above = (_safeHalf.y - includeTop) - maxY;
            float below = minY - (-_safeHalf.y + includeBottom);
            return Mathf.Abs(above - below);
        }

        // The rectangle to fill is handed in rather than worked out from the grid, because
        // what has to be on screen is not the board: it is whatever the player has to reach.
        // Board frames the pieces, which on a small level drawn inside a fixed field is a
        // fraction of it.
        public void Frame(Transform board, Vector3 centre, float halfWidth, float halfDepth,
                          bool immediate = false, IList<Vector3> include = null)
        {
            var cam = target != null ? target : Camera.main;
            if (cam == null || board == null || halfWidth <= 0f || halfDepth <= 0f) return;

            _lastBoard = board;
            _lastCentre = centre;
            _lastHalfWidth = halfWidth;
            _lastHalfDepth = halfDepth;
            _include.Clear();
            if (include != null) _include.AddRange(include);

            FitBox(out _boxCentre, out _boxHalf);
            FitBox(out _safeCentre, out _safeHalf, 1f, 1f);

            Vector3 forward = cam.transform.forward;
            Vector3 groundForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;

            // The four ground corners of the apron, in the board's own ground axes. They
            // were measured in the camera's axes before, which is only the same rectangle
            // while the two agree: turn the board a quarter turn under a camera looking in
            // diagonally and the fit is solving for a board that is not there.
            Vector3 across = Ground(board.right);
            Vector3 along = Ground(board.forward);

            for (int i = 0; i < 4; i++)
                _corners[i] = centre
                            + across * ((i & 1) == 0 ? -halfWidth : halfWidth)
                            + along * ((i & 2) == 0 ? -halfDepth : halfDepth);

            // Aimed at the middle of everything that has to be seen, not of the board alone:
            // with a lane running out of the front of the board, aiming at the board would
            // spend the top of the frame on the hall to make room for the lane at the bottom.
            float lo = -halfDepth, hi = halfDepth;
            foreach (var p in _include)
            {
                float a = Vector3.Dot(p - centre, along);
                lo = Mathf.Min(lo, a);
                hi = Mathf.Max(hi, a);
            }
            Vector3 aim = centre + along * ((lo + hi) * 0.5f);
            Vector3 focus = aim + focusOffset + groundForward * ((hi - lo) * focusShift);

            float distance = Fit(cam, focus, forward);

            // With extra points the middle is rarely the best aim: the board may crop past the
            // top of the frame (the fill box allows it) while the terminal has to sit clear of
            // the booster row. Try aims along the board's length and keep the closest camera.
            if (_include.Count > 0)
            {
                const int tries = 40;
                var distances = new float[tries + 1];
                var aims = new Vector3[tries + 1];
                for (int i = 0; i <= tries; i++)
                {
                    aims[i] = centre + along * Mathf.Lerp(lo, hi, i / (float)tries) + focusOffset;
                    distances[i] = Fit(cam, aims[i], forward);
                    distance = Mathf.Min(distance, distances[i]);
                }

                // Several aims often fit at the same distance -- the width decides it and the height
                // has room to spare. Of those, take the one that leaves the room evenly above and
                // below, or the spare all collects at one end as bare floor.
                float bestBalance = float.MaxValue;
                for (int i = 0; i <= tries; i++)
                {
                    // A few percent further back is worth it: exactly the nearest aim is often the one
                    // pinned against the top of the frame with all the spare room at the bottom.
                    if (distances[i] > distance * 1.06f + 0.05f) continue;
                    float balance = VerticalBalance(cam, aims[i] - forward * distances[i]);
                    if (balance < bestBalance) { bestBalance = balance; focus = aims[i]; }
                }
                distance = Fit(cam, focus, forward);
            }

            _desired = focus - forward * distance;
            _hasTarget = true;

            if (immediate || glideTime <= 0f)
            {
                cam.transform.position = _desired;
                _velocity = Vector3.zero;
            }
        }

        void LateUpdate()
        {
            if (!_hasTarget || glideTime <= 0f) return;
            var cam = target != null ? target : Camera.main;
            if (cam == null) return;

            cam.transform.position = Vector3.SmoothDamp(
                cam.transform.position, _desired, ref _velocity, glideTime);
        }
    }
}
