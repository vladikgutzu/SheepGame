using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The sorter loop's belt, base, outer rail and the island inside it, built as meshes for a
    /// given length so the loop can grow smoothly when the queue gains a place.
    /// </summary>
    // A stadium: a lower run (the sorter) and an upper run (the collector) joined by a half circle
    // at each end, centred on the junction. The bays are separate objects standing on the island.
    //
    // Built at run time rather than as saved meshes, because its length follows the number of
    // places the level has and animates when a continue adds one. Rebuilt only when the length
    // changes. All in the local frame of this object, which sits at the board's frame.
    [ExecuteAlways]
    public class CheckInLoop : MonoBehaviour
    {
        public Material treadMaterial;
        public Material baseMaterial;
        public Material railMaterial;
        public Material islandMaterial;

        [Tooltip("Middle of the loop across the board.")]
        public float centreX;
        public float sorterZ;
        public float collectorZ;
        [Tooltip("Width of the belt, rails and all.")]
        public float width = 3.7f;
        [Tooltip("Width of the moving tread.")]
        public float tread = 3.0f;
        [Tooltip("Height of the belt surface.")]
        public float surface = 2.15f;
        [Tooltip("Openings in the outer rail at the middle: where the merge comes in and the feed goes out.")]
        public float opening = 1.85f;

        [Header("Plinth")]
        [Tooltip("The block the bays are cut into. Falls back to the rail's material.")]
        public Material plinthMaterial;
        [Tooltip("How far the plinth stands above the belt surface.")]
        public float plinthRise = 0.22f;
        [Tooltip("Half the width of the slot a bay sits in. A little wider than the bay's lamp rails " +
                 "stand apart, so their inner faces show and do not fight the slot's wall.")]
        public float slotHalfWidth = 1.72f;

        [SerializeField] float _halfLength = -1f;
        MeshFilter _tread, _base, _rail, _island, _plinth;
        readonly List<float> _slots = new List<float>();

        public float HalfLength => _halfLength;
        public float Radius => (collectorZ - sorterZ) * 0.5f;
        public float Mid => (sorterZ + collectorZ) * 0.5f;

        /// <summary>Sets how far the loop's end arcs sit from its middle, and rebuilds if that changed.</summary>
        public void SetHalfLength(float halfLength, bool force = false) => SetLayout(halfLength, null, force);

        /// <summary>
        /// Sets the loop's half length and where across it the shown bays stand (local x, any order;
        /// null keeps the last), and rebuilds if either changed.
        /// </summary>
        public void SetLayout(float halfLength, IList<float> slots, bool force = false)
        {
            halfLength = Mathf.Max(0.01f, halfLength);
            bool changed = force || _tread == null || Mathf.Abs(halfLength - _halfLength) >= 0.001f;
            if (slots != null)
            {
                if (slots.Count != _slots.Count) changed = true;
                else for (int i = 0; i < slots.Count && !changed; i++) changed = Mathf.Abs(slots[i] - _slots[i]) >= 0.001f;
                if (changed) { _slots.Clear(); _slots.AddRange(slots); _slots.Sort(); }
            }
            if (!changed) return;
            _halfLength = halfLength;
            Rebuild();
        }

        /// <summary>
        /// Points along the loop's centre line from the sorter's middle, round the end on
        /// <paramref name="side"/> (+1 right, -1 left), to the collector's middle. Local frame.
        /// </summary>
        public List<Vector3> CentreLine(float side, float y, int arcSteps = 12)
        {
            var points = new List<Vector3> { new Vector3(centreX, y, sorterZ) };
            float L = _halfLength, R = Radius, mid = Mid;
            for (int i = 0; i <= arcSteps; i++)
            {
                float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)arcSteps);
                points.Add(new Vector3(centreX + side * (L + R * Mathf.Cos(a)), y, mid + R * Mathf.Sin(a)));
            }
            points.Add(new Vector3(centreX, y, collectorZ));
            return points;
        }

        void Rebuild()
        {
            _tread = Part("Loop Tread", treadMaterial, _tread, scroll: true);
            _base = Part("Loop Base", baseMaterial, _base, scroll: false);
            _rail = Part("Loop Rail", railMaterial, _rail, scroll: false);
            _island = Part("Loop Island", islandMaterial, _island, scroll: false);
            _plinth = Part("Loop Plinth", plinthMaterial != null ? plinthMaterial : railMaterial, _plinth, scroll: false);

            float baseH = surface - 0.04f;
            float half = width * 0.5f;

            var tread = new MeshBuilder();
            var body = new MeshBuilder();
            var rail = new MeshBuilder();
            foreach (float side in new[] { 1f, -1f })
            {
                // Offsets are measured outwards from the centre line: -half is the island's edge.
                tread.Strip(Path(side, 0f), -this.tread * 0.5f, this.tread * 0.5f, surface + 0.02f, surface + 0.02f, vFromDistance: true);
                body.Strip(Path(side, 0f), -half, half, baseH, baseH, vFromDistance: false);
                body.Strip(Path(side, 0f), half, half, baseH, 0f, vFromDistance: false);
                var railPath = Path(side, opening);
                rail.Strip(railPath, half - 0.36f, half, surface + 0.37f, surface + 0.37f, vFromDistance: false);
                rail.Strip(railPath, half, half, surface + 0.37f, surface - 0.05f, vFromDistance: false);
                rail.Strip(railPath, half - 0.36f, half - 0.36f, surface - 0.05f, surface + 0.37f, vFromDistance: false);
            }
            Assign(_tread, tread, "Loop Tread");
            Assign(_base, body, "Loop Base");
            Assign(_rail, rail, "Loop Rail");
            Assign(_island, Island(baseH), "Loop Island");
            Assign(_plinth, Plinth(baseH), "Loop Plinth");
        }

        // One block over the whole island, standing a little above the belt, with a slot cut across
        // it for each bay: the bays read as one sorter unit rather than stands dotted on a floor. The
        // solid between two slots is the divider the two bays' lamp rails are set into; the ends are
        // the island's half discs. Front and back stay open where the cases ride in and out.
        MeshBuilder Plinth(float floor)
        {
            var plinth = new MeshBuilder();
            float L = _halfLength, inner = Radius - width * 0.5f, mid = Mid;
            float top = surface + plinthRise;
            float front = mid - inner, back = mid + inner;
            if (inner <= 0f) return plinth;

            // The solid spans across the straight part, between the slots.
            var solids = new List<(float a, float b)>();
            float from = centreX - L;
            foreach (float x in _slots)
            {
                float a = x - slotHalfWidth, b = x + slotHalfWidth;
                if (a > from + 0.001f) solids.Add((from, Mathf.Min(a, centreX + L)));
                from = Mathf.Max(from, b);
            }
            if (centreX + L > from + 0.001f) solids.Add((from, centreX + L));

            foreach (var (a, b) in solids)
            {
                plinth.Quad(new Vector3(a, top, front), new Vector3(b, top, front),
                            new Vector3(b, top, back), new Vector3(a, top, back));
                // The long faces to the belt.
                plinth.Wall(new Vector3(a, 0f, front), new Vector3(b, 0f, front), floor, top, Vector3.back);
                plinth.Wall(new Vector3(a, 0f, back), new Vector3(b, 0f, back), floor, top, Vector3.forward);
                // The slot faces, down to the belt surface. Not where a solid meets the half disc.
                if (a > centreX - L + 0.001f)
                    plinth.Wall(new Vector3(a, 0f, front), new Vector3(a, 0f, back), floor, top, Vector3.left);
                if (b < centreX + L - 0.001f)
                    plinth.Wall(new Vector3(b, 0f, front), new Vector3(b, 0f, back), floor, top, Vector3.right);
            }

            // A half disc at each end, top and rim.
            const int steps = 20;
            foreach (float side in new[] { 1f, -1f })
            {
                var centre = new Vector3(centreX + side * L, top, mid);
                for (int i = 0; i < steps; i++)
                {
                    float a0 = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)steps);
                    float a1 = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (i + 1) / (float)steps);
                    var d0 = new Vector3(side * Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var d1 = new Vector3(side * Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    plinth.Triangle(centre, centre + d0 * inner, centre + d1 * inner);
                    var p0 = centre + d0 * inner; p0.y = 0f;
                    var p1 = centre + d1 * inner; p1.y = 0f;
                    plinth.Wall(p0, p1, floor, top, (d0 + d1).normalized);
                }
            }
            return plinth;
        }

        // The centre line for one side, each point with the outward direction at it. Starts at the
        // sorter's middle (offset by gap), runs out, round the end, and back along the collector.
        List<(Vector3 p, Vector3 outward)> Path(float side, float gap)
        {
            const int arcSteps = 20;
            float L = _halfLength, R = Radius, mid = Mid;
            var path = new List<(Vector3, Vector3)>();
            path.Add((new Vector3(centreX + side * gap, 0f, sorterZ), Vector3.back));
            for (int i = 0; i <= arcSteps; i++)
            {
                float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)arcSteps);
                var outward = new Vector3(side * Mathf.Cos(a), 0f, Mathf.Sin(a));
                path.Add((new Vector3(centreX + side * L, 0f, mid) + outward * R, outward));
            }
            path.Add((new Vector3(centreX + side * gap, 0f, collectorZ), Vector3.forward));
            return path;
        }

        MeshBuilder Island(float y)
        {
            var island = new MeshBuilder();
            float L = _halfLength, inner = Radius - width * 0.5f, mid = Mid;
            // The straight part.
            island.Quad(new Vector3(centreX - L, y, mid - inner), new Vector3(centreX + L, y, mid - inner),
                        new Vector3(centreX + L, y, mid + inner), new Vector3(centreX - L, y, mid + inner));
            // A half disc at each end.
            foreach (float side in new[] { 1f, -1f })
            {
                var centre = new Vector3(centreX + side * L, y, mid);
                const int steps = 20;
                for (int i = 0; i < steps; i++)
                {
                    float a0 = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)steps);
                    float a1 = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (i + 1) / (float)steps);
                    island.Triangle(centre,
                                    centre + new Vector3(side * Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * inner,
                                    centre + new Vector3(side * Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * inner);
                }
            }
            return island;
        }

        MeshFilter Part(string name, Material material, MeshFilter existing, bool scroll)
        {
            if (existing != null) return existing;
            var child = transform.Find(name);
            if (child == null)
            {
                child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                child.SetParent(transform, false);
            }
            var renderer = child.GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
            if (scroll && child.GetComponent<SheepGame.Fx.ScrollingBelt>() == null)
                child.gameObject.AddComponent<SheepGame.Fx.ScrollingBelt>().speed = 0.5f;
            return child.GetComponent<MeshFilter>();
        }

        static void Assign(MeshFilter filter, MeshBuilder builder, string name)
        {
            var mesh = filter.sharedMesh;
            if (mesh == null || mesh.name != name) { mesh = new Mesh { name = name }; filter.sharedMesh = mesh; }
            builder.Fill(mesh);
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _t = new List<int>();

            // A strip following a path, from offset a at height ya to offset b at height yb.
            // Faces are wound to face up for flat strips and outward for walls.
            public void Strip(List<(Vector3 p, Vector3 outward)> path, float a, float b, float ya, float yb, bool vFromDistance)
            {
                int start = _v.Count;
                float travelled = 0f;
                for (int i = 0; i < path.Count; i++)
                {
                    if (i > 0) travelled += Vector3.Distance(path[i - 1].p, path[i].p);
                    var (p, outward) = path[i];
                    var pa = p + outward * a; pa.y = ya;
                    var pb = p + outward * b; pb.y = yb;
                    _v.Add(pa); _v.Add(pb);
                    float v = vFromDistance ? travelled : i;
                    _uv.Add(new Vector2(0f, v)); _uv.Add(new Vector2(1f, v));
                }

                int first = _t.Count;
                for (int i = 1; i < path.Count; i++)
                {
                    int k = start + (i - 1) * 2;
                    _t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                }

                // Which way is "out" for this strip: up if flat, along the path's outward if a wall
                // on the outer side, inward if a wall on the inner side.
                // Walls are given top first when they face out, bottom first when they face in.
                Vector3 want = Mathf.Approximately(ya, yb) ? Vector3.up
                             : path[path.Count / 2].outward * (ya > yb ? 1f : -1f);
                var n = Vector3.Cross(_v[_t[first + 1]] - _v[_t[first]], _v[_t[first + 2]] - _v[_t[first]]);
                if (Vector3.Dot(n, want) < 0f)
                    for (int t = first; t < _t.Count; t += 3) (_t[t + 1], _t[t + 2]) = (_t[t + 2], _t[t + 1]);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            // An upright quad between two floor points, from height y0 to y1, facing outward.
            // Its own vertices, so the edge along the top stays crisp.
            public void Wall(Vector3 p0, Vector3 p1, float y0, float y1, Vector3 outward)
            {
                int i = _v.Count;
                _v.Add(new Vector3(p0.x, y0, p0.z)); _v.Add(new Vector3(p1.x, y0, p1.z));
                _v.Add(new Vector3(p1.x, y1, p1.z)); _v.Add(new Vector3(p0.x, y1, p0.z));
                float u = Vector3.Distance(p0, p1);
                _uv.Add(new Vector2(0f, y0)); _uv.Add(new Vector2(u, y0)); _uv.Add(new Vector2(u, y1)); _uv.Add(new Vector2(0f, y1));
                var n = Vector3.Cross(_v[i + 1] - _v[i], _v[i + 2] - _v[i]);
                // Wound as Triangle does: kept when the cross product already points out.
                if (Vector3.Dot(n, outward) > 0f) _t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else _t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                _uv.Add(new Vector2(a.x, a.z)); _uv.Add(new Vector2(b.x, b.z)); _uv.Add(new Vector2(c.x, c.z));
                // Facing up: Unity draws clockwise seen from the front, which is a positive y here.
                if (Vector3.Cross(b - a, c - a).y > 0f) _t.AddRange(new[] { i, i + 1, i + 2 });
                else _t.AddRange(new[] { i, i + 2, i + 1 });
            }

            public void Fill(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(_v);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }
    }
}
