using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // The conveyor pieces ride once they leave their cell, fitted onto the rectangle Board
    // actually drives them along.
    //
    // Belongs to the board rather than standing beside it in the scene: the two are derived
    // from different things — the belt from modules dragged into place, the route from
    // cellSize / roadMargin / fieldSize — and they came apart every single time either side
    // changed. Now Board re-fits it as it loads a level, the same way it rebuilds the apron,
    // so a board moved onto new ground or scaled to a new cell brings its belt with it.
    //
    // Everything is measured off renderer bounds rather than read from transforms: this
    // kit's modules have pivots parked at a corner of their footprint (the outfeed portal's
    // is eight units off in Z), so placing by pivot lands them visibly askew.
    [ExecuteAlways]
    public class BeltRing : MonoBehaviour
    {
        [Header("Module naming")]
        [Tooltip("The kit's own prefixes, kept as the classifier because it is what the " +
                 "modules arrive called. Anything matching none of them is scenery parked " +
                 "under the belt for convenience: welcome as a child, but it does not vote " +
                 "on where the lanes are.")]
        public string straightPrefix = "12.2";
        public string outfeedPrefix = "12.3";
        public string cornerPrefix = "12.4";

        // Height of the lane surface, in world space. Board turns it into its own local
        // height, which is what the departure route travels at.
        public float SurfaceHeight { get; private set; }

        public IReadOnlyList<Transform> Outfeeds => _outfeeds;

        readonly List<Transform> _outfeeds = new List<Transform>();
        readonly List<Transform> _alongZ = new List<Transform>();
        readonly List<Transform> _alongX = new List<Transform>();
        readonly List<Transform> _gates = new List<Transform>();

        // Fits the ring around a board of this size, centred on that board. Idempotent:
        // once fitted the measured lanes already sit on the route, the scale factors come
        // out at 1 and a second call moves nothing.
        public bool Fit(Transform board, int width, int height, float cellSize, float roadMargin)
        {
            if (board == null || width < 2 || height < 2) return false;

            Classify();
            if (_alongZ.Count == 0 || _alongX.Count == 0) return false;

            Vector3 at = board.position;
            float halfAcross = (width - 1) * 0.5f * cellSize + roadMargin;
            float halfAlong = (height - 1) * 0.5f * cellSize + roadMargin;

            // The route is measured in the board's columns and rows; the belt is laid out
            // in world X and Z. A board stood on its side — which is how a portrait field
            // is made to fill a landscape yard — swaps the two, and a ring built without
            // noticing comes out the short way round.
            bool quarterTurned = Mathf.Abs(Mathf.Sin(board.eulerAngles.y * Mathf.Deg2Rad)) > 0.5f;
            float halfX = quarterTurned ? halfAlong : halfAcross;
            float halfZ = quarterTurned ? halfAcross : halfAlong;

            float ringLeft = at.x - halfX, ringRight = at.x + halfX;
            float ringFront = at.z - halfZ, ringBack = at.z + halfZ;

            // Where the belt's own lanes sit today, taken as the mean of each side's
            // straights so one module nudged out of line does not drag the whole side.
            //
            // Sides are told apart by the belt's own middle, never by the board's. Judged
            // against the board, a belt that has not been fitted yet — sitting off where it
            // was assembled — has every one of its lanes on the same side of it, so two of
            // the four sides come back empty and the fit stretches the ring to a length it
            // invented.
            if (!LaneCentre(out Vector3 middle)) return false;

            float beltLeft = MeanSide(_alongZ, middle.x, true, false);
            float beltRight = MeanSide(_alongZ, middle.x, false, false);
            float beltFront = MeanSide(_alongX, middle.z, true, true);
            float beltBack = MeanSide(_alongX, middle.z, false, true);

            if (beltRight - beltLeft < 0.01f || beltBack - beltFront < 0.01f) return false;

            // Two lane positions per axis pin down a scale and an offset exactly, so all
            // four sides land on the route rather than two matching and two drifting.
            float scaleX = (ringRight - ringLeft) / (beltRight - beltLeft);
            float scaleZ = (ringBack - ringFront) / (beltBack - beltFront);

            Vector3 p = transform.position;
            Vector3 s = transform.localScale;

            // The root may carry a yaw, and at 90 or 270 its local X drives world Z.
            // Scaling the wrong local axis stretches the ring across the short way round.
            bool swapped = Mathf.Abs(Mathf.Sin(transform.eulerAngles.y * Mathf.Deg2Rad)) > 0.5f;
            // Height goes with the footprint. Left alone, a belt shrunk to a board a third
            // of the size stands three times too tall over it — a wall around the board
            // rather than a conveyor beside it.
            float scaleY = (scaleX + scaleZ) * 0.5f;

            transform.localScale = swapped
                ? new Vector3(s.x * scaleZ, s.y * scaleY, s.z * scaleX)
                : new Vector3(s.x * scaleX, s.y * scaleY, s.z * scaleZ);

            transform.position = new Vector3(
                ringLeft - scaleX * (beltLeft - p.x), p.y, ringFront - scaleZ * (beltFront - p.z));

            GroundOn(board);

            // The lane surface, so pieces travel on the belt. Taken from a straight rather
            // than from the whole belt, whose tallest thing is the portal's signage.
            float surface = board.position.y;
            foreach (var t in _alongX)
                if (WorldBounds(t, out var b)) surface = Mathf.Max(surface, b.max.y);
            SurfaceHeight = surface;

            PlaceOutfeeds(surface);
            return true;
        }

        // Sits the belt on the same ground the board stands on. Fitting only ever solved
        // the two axes the ring is drawn in, so without this a belt keeps the height it was
        // assembled at and a board moved to new ground leaves it hanging over the old.
        void GroundOn(Transform board)
        {
            if (!WorldBounds(transform, out var bounds)) return;
            transform.position += new Vector3(0f, board.position.y - bounds.min.y, 0f);
        }

        // One marker per outfeed module, left to right, placed where that module actually
        // stands rather than where this would have put it — the openings get nudged by hand
        // and the markers should follow, not fight back. Board reads each lane's turn-off
        // straight from its marker, so that follows too.
        void PlaceOutfeeds(float surface)
        {
            _gates.Sort((a, b) =>
            {
                WorldBounds(a, out var ba);
                WorldBounds(b, out var bb);
                return ba.center.x.CompareTo(bb.center.x);
            });

            _outfeeds.Clear();
            for (int i = 0; i < _gates.Count; i++)
            {
                if (!WorldBounds(_gates[i], out var b)) continue;

                string name = $"Outfeed {i + 1}";
                var marker = transform.Find(name);
                if (marker == null)
                {
                    var go = new GameObject(name);
                    marker = go.transform;
                    marker.SetParent(transform, false);
                }

                marker.position = new Vector3(b.center.x, surface, b.center.z);
                _outfeeds.Add(marker);
            }
        }

        void Classify()
        {
            _alongZ.Clear();
            _alongX.Clear();
            _gates.Clear();

            foreach (Transform child in transform)
            {
                if (!WorldBounds(child, out var b)) continue;

                if (Prefixed(child.name, outfeedPrefix)) _gates.Add(child);
                else if (Prefixed(child.name, cornerPrefix)) continue;
                else if (Prefixed(child.name, straightPrefix))
                    (b.size.z > b.size.x ? _alongZ : _alongX).Add(child);
            }
        }

        static bool Prefixed(string name, string prefix) =>
            !string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, System.StringComparison.Ordinal);

        // The middle of the lanes themselves — corners, portals and any scenery parked
        // under the belt left out, so none of them can drag the split off centre.
        bool LaneCentre(out Vector3 centre)
        {
            Bounds bounds = default;
            bool any = false;

            foreach (var group in new[] { _alongZ, _alongX })
                foreach (var t in group)
                {
                    if (!WorldBounds(t, out var b)) continue;
                    if (!any) { bounds = b; any = true; }
                    else bounds.Encapsulate(b);
                }

            centre = any ? bounds.center : Vector3.zero;
            return any;
        }

        static float MeanSide(List<Transform> group, float centre, bool below, bool useZ)
        {
            float sum = 0f;
            int n = 0;
            foreach (var t in group)
            {
                if (!WorldBounds(t, out var b)) continue;
                float v = useZ ? b.center.z : b.center.x;
                if (below == v < centre) { sum += v; n++; }
            }
            return n > 0 ? sum / n : centre;
        }

        static bool WorldBounds(Transform t, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }
    }
}
