using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // The route a plane drives once it has left the taxiway ring: out to the runway,
    // down it, and away. Waypoints are this object's children, so the trajectory is
    // reshaped by dragging them in the scene view like any other GameObject.
    //
    // A Catmull-Rom curve is fitted through them and then flattened into a polyline, so
    // travel can be measured in metres rather than in curve parameter — otherwise
    // planes would visibly speed up through tight corners and crawl along straights.
    public class DeparturePath : MonoBehaviour
    {
        [Tooltip("Curve smoothness. Higher is smoother but costs a little more to rebuild.")]
        [Range(2, 40)] public int samplesPerSegment = 12;

        [Header("Where the run changes character (fraction along the path)")]
        [Tooltip("Throttle up and retract the gear from here on.")]
        [Range(0f, 1f)] public float rollStart = 0.35f;
        [Tooltip("Nose lifts here; the plane climbs away for the rest of the path.")]
        [Range(0f, 1f)] public float climbStart = 0.65f;

        [Header("Climb-out")]
        public float climbHeight = 80f;
        public float climbPitch = 18f;

        readonly List<Vector3> _points = new List<Vector3>();
        readonly List<Vector3> _poly = new List<Vector3>();
        readonly List<float> _travelled = new List<float>();

        public float Length { get; private set; }
        public bool IsValid => _poly.Count >= 2;
        public Vector3 StartPosition => IsValid ? _poly[0] : transform.position;

        void Awake() => Rebuild();

        public void Rebuild()
        {
            _points.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.gameObject.activeSelf) _points.Add(child.position);
            }

            _poly.Clear();
            _travelled.Clear();
            Length = 0f;

            if (_points.Count < 2) return;

            _poly.Add(_points[0]);
            for (int i = 0; i < _points.Count - 1; i++)
            {
                Vector3 p0 = _points[Mathf.Max(i - 1, 0)];
                Vector3 p1 = _points[i];
                Vector3 p2 = _points[i + 1];
                Vector3 p3 = _points[Mathf.Min(i + 2, _points.Count - 1)];

                for (int s = 1; s <= samplesPerSegment; s++)
                    _poly.Add(CatmullRom(p0, p1, p2, p3, s / (float)samplesPerSegment));
            }

            _travelled.Add(0f);
            for (int i = 1; i < _poly.Count; i++)
            {
                Length += Vector3.Distance(_poly[i - 1], _poly[i]);
                _travelled.Add(Length);
            }
        }

        public Vector3 PositionAt(float distance)
        {
            if (!IsValid) return transform.position;
            if (distance <= 0f) return _poly[0];
            if (distance >= Length) return _poly[_poly.Count - 1];

            int i = IndexAt(distance);
            float span = _travelled[i + 1] - _travelled[i];
            float t = span > 0.0001f ? (distance - _travelled[i]) / span : 0f;
            return Vector3.Lerp(_poly[i], _poly[i + 1], t);
        }

        public Vector3 DirectionAt(float distance)
        {
            if (!IsValid) return transform.forward;
            int i = Mathf.Clamp(IndexAt(Mathf.Clamp(distance, 0f, Length)), 0, _poly.Count - 2);
            Vector3 d = _poly[i + 1] - _poly[i];
            return d.sqrMagnitude > 0.000001f ? d.normalized : transform.forward;
        }

        int IndexAt(float distance)
        {
            int lo = 0, hi = _travelled.Count - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (_travelled[mid] <= distance) lo = mid; else hi = mid;
            }
            return Mathf.Min(lo, _poly.Count - 2);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                         + (-p0 + p2) * t
                         + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                         + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        void OnDrawGizmos()
        {
            Rebuild();

            for (int i = 0; i < _points.Count; i++)
            {
                Gizmos.color = i == 0 ? Color.green : Color.white;
                Gizmos.DrawSphere(_points[i], 2f);
            }

            if (!IsValid) return;

            // Colour each leg so the two thresholds are visible without opening the
            // inspector: yellow taxi, orange takeoff roll, cyan climb-out.
            for (int i = 0; i < _poly.Count - 1; i++)
            {
                float t = _travelled[i] / Length;
                Gizmos.color = t < rollStart ? new Color(1f, 0.85f, 0.2f)
                             : t < climbStart ? new Color(1f, 0.5f, 0.1f)
                                              : new Color(0.3f, 0.85f, 1f);

                Vector3 a = _poly[i];
                Vector3 b = _poly[i + 1];
                if (t >= climbStart)
                {
                    a += Vector3.up * ClimbOffset(t);
                    b += Vector3.up * ClimbOffset(_travelled[i + 1] / Length);
                }
                Gizmos.DrawLine(a, b);
            }

            // Arrowheads every so often so the direction of travel is unambiguous.
            int step = Mathf.Max(1, _poly.Count / 12);
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            for (int i = step; i < _poly.Count - 1; i += step)
            {
                Vector3 dir = (_poly[i + 1] - _poly[i]).normalized;
                Vector3 side = Vector3.Cross(dir, Vector3.up) * 2.5f;
                Gizmos.DrawLine(_poly[i], _poly[i] - dir * 5f + side);
                Gizmos.DrawLine(_poly[i], _poly[i] - dir * 5f - side);
            }
        }

        public float ClimbOffset(float t)
        {
            float k = Mathf.InverseLerp(climbStart, 1f, t);
            return climbHeight * k * k;
        }
    }
}
