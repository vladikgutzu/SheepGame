using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // The playfield and the bay around it, generated from the same numbers Board uses to
    // place pieces. Rebuilt whenever a level loads, so a 5x5 level gets a 5x5 bay rather
    // than rattling around inside one sized for the largest level in the set.
    //
    // Three nested rings, each an offset of the one outside it:
    //   cells      the parking grid, square so paint lines up with piece positions
    //   channel    the lane pieces travel along to leave
    //   shore      the enclosing bank, broken by one gap where the channel leads out
    // How the playfield's cells are drawn. The grid has to be legible — a player works out
    // a move by counting cells — but a chequerboard is the loudest way there is of saying
    // so, and it fights any setting that is not a board game. A tiled floor says the same
    // thing quietly: one tile to a cell, and the seam is enough to count along.
    public enum CellPattern
    {
        Checkerboard, // alternating light and dark squares
        Tiles,        // one slab seamed at every cell boundary, like a laid floor
        Plain,        // one slab, nothing drawn: for grounds that carry their own markings
    }

    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ApronSurface : MonoBehaviour
    {
        [Header("Cells")]
        public CellPattern pattern = CellPattern.Checkerboard;
        [Tooltip("Width of a seam or painted line, as a fraction of a cell. A floor wants " +
                 "the thinnest that still survives being drawn a few pixels wide on a phone.")]
        [Range(0.005f, 0.25f)] public float lineWidth = 0.025f;

        [Header("Materials")]
        public Material lightCell;
        public Material darkCell;
        public Material channel;
        public Material marking;
        public Material shore;

        [Header("Bay")]
        [Tooltip("Corner rounding at the field boundary. Grows outwards through the rings.")]
        public float cornerRadius = 12f;
        [Tooltip("Width of the enclosing bank outside the channel.")]
        public float shoreWidth = 26f;
        public float shoreHeight = 0.45f;
        [Tooltip("Draw the surround as one solid slab carrying the whole board — a raised " +
                 "island, the way a casual puzzle sits its board above the scenery — " +
                 "instead of a bank ring around the outside of it.")]
        public bool island = false;
        [Tooltip("How far the island stands proud of the ground it rests on.")]
        public float islandDepth = 1.2f;
        [Tooltip("Width of the opening in the shore, as a fraction of the field width.")]
        [Range(0f, 0.9f)] public float exitWidth = 0.3f;
        [Range(4, 16)] public int cornerSegments = 8;

        [Header("Editor preview")]
        public Vector2Int previewGrid = new Vector2Int(9, 11);
        public float previewCellSize = 10f;
        public float previewRoadMargin = 13f;

        Mesh _mesh;

        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int> _light = new List<int>();
        readonly List<int> _dark = new List<int>();
        readonly List<int> _channel = new List<int>();
        readonly List<int> _mark = new List<int>();
        readonly List<int> _shore = new List<int>();

        readonly List<Vector2> _fieldEdge = new List<Vector2>();
        readonly List<Vector2> _channelEdge = new List<Vector2>();
        readonly List<Vector2> _shoreEdge = new List<Vector2>();
        readonly List<Vector3> _shoreLine = new List<Vector3>();

        // Centre line of the shore ring in local space, for anything that wants to place
        // props along the bank. Excludes the stretch removed by the exit gap.
        public IReadOnlyList<Vector3> ShoreCentreLine => _shoreLine;

        // Bumped on every rebuild so anything caching the vertex layout — the wave
        // follower, for one — knows the mesh underneath it has been replaced.
        public int Revision { get; private set; }

        // Last rebuild's dimensions, kept so the boundary can be resampled on demand
        // without recomputing what the mesh was built from.
        float _halfW, _halfH, _margin, _fieldR, _gapHalf;

        void OnEnable() => RebuildPreview();

        public void RebuildPreview()
        {
            var board = GetComponentInParent<Board>();
            if (board != null)
                Rebuild(previewGrid.x, previewGrid.y, board.cellSize, board.roadMargin);
            else
                Rebuild(previewGrid.x, previewGrid.y, previewCellSize, previewRoadMargin);
        }

        public void Rebuild(int width, int height, float cellSize, float roadMargin)
        {
            if (width < 1 || height < 1 || cellSize <= 0f) return;

            _verts.Clear(); _uvs.Clear();
            _light.Clear(); _dark.Clear(); _channel.Clear(); _mark.Clear(); _shore.Clear();
            _shoreLine.Clear();

            float halfW = width * cellSize * 0.5f;
            float halfH = height * cellSize * 0.5f;

            if (pattern == CellPattern.Checkerboard)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        // Mirrors Board.GridToLocal so paint and pieces cannot drift apart.
                        float cx = (x - (width - 1) * 0.5f) * cellSize;
                        float cz = (y - (height - 1) * 0.5f) * cellSize;
                        Quad(cx - cellSize * 0.5f, cz - cellSize * 0.5f,
                             cx + cellSize * 0.5f, cz + cellSize * 0.5f,
                             ((x + y) & 1) == 0 ? _light : _dark);
                    }
            }
            else
            {
                Quad(-halfW, -halfH, halfW, halfH, _light);

                // Seams on every boundary including the outer one, so the floor reads as
                // laid rather than as a slab with a grid floating inside it. Drawn a hair
                // above the slab, the same lift the lane marking uses.
                if (pattern == CellPattern.Tiles)
                {
                    float half = cellSize * lineWidth * 0.5f;

                    for (int x = 0; x <= width; x++)
                    {
                        float cx = -halfW + x * cellSize;
                        Quad(cx - half, -halfH, cx + half, halfH, _mark, 0.02f);
                    }
                    for (int y = 0; y <= height; y++)
                    {
                        float cz = -halfH + y * cellSize;
                        Quad(-halfW, cz - half, halfW, cz + half, _mark, 0.02f);
                    }
                }
            }

            // Radii grow outwards so every ring keeps the same corner centres and the
            // offsets stay parallel.
            float fieldR = Mathf.Min(cornerRadius, Mathf.Min(halfW, halfH));
            Contour(_fieldEdge, halfW, halfH, fieldR);
            Contour(_channelEdge, halfW + roadMargin, halfH + roadMargin, fieldR + roadMargin);
            Contour(_shoreEdge, halfW + roadMargin + shoreWidth, halfH + roadMargin + shoreWidth,
                    fieldR + roadMargin + shoreWidth);

            Ribbon(_fieldEdge, _channelEdge, _channel, 0f);

            // The lane marking rides the middle of the channel.
            float lane = 0.35f;
            var midInner = new List<Vector2>();
            var midOuter = new List<Vector2>();
            Contour(midInner, halfW + roadMargin * 0.5f - lane, halfH + roadMargin * 0.5f - lane,
                    fieldR + roadMargin * 0.5f - lane);
            Contour(midOuter, halfW + roadMargin * 0.5f + lane, halfH + roadMargin * 0.5f + lane,
                    fieldR + roadMargin * 0.5f + lane);
            Ribbon(midInner, midOuter, _mark, 0.02f);

            _halfW = halfW; _halfH = halfH; _margin = roadMargin; _fieldR = fieldR;
            _gapHalf = halfW * exitWidth;

            if (island)
            {
                // A slab the whole board stands on, its top a hair under the cells so the
                // two never fight for depth, and a wall dropped from its edge to the
                // ground. Nothing is cut out of it: the mouth is where pieces leave the
                // board, not where they leave the table.
                Cap(_shoreEdge, _shore, -0.01f);
                Skirt(_shoreEdge, _shore, -0.01f, -Mathf.Max(0.05f, islandDepth));
            }
            else
            {
                // The shore, minus the mouth of the bay. The gap is cut by position rather
                // than by index so it stays put whatever the corner sampling is.
                Ribbon(_channelEdge, _shoreEdge, _shore, shoreHeight, InMouth, _shoreLine);
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Apron (generated)" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            _mesh.indexFormat = _verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.subMeshCount = 5;
            _mesh.SetTriangles(_light, 0);
            _mesh.SetTriangles(_dark, 1);
            _mesh.SetTriangles(_channel, 2);
            _mesh.SetTriangles(_mark, 3);
            _mesh.SetTriangles(_shore, 4);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            Revision++;

            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { lightCell, darkCell, channel, marking, shore };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // This component owns the playfield's appearance, so it also guarantees the
            // thing is drawn. A disabled renderer here has twice been mistaken for a
            // material or geometry fault, because the mesh looks perfectly fine.
            renderer.enabled = true;

            var fence = GetComponent<PenFence>();
            if (fence != null) fence.Populate(this);

            var decor = GetComponent<ShoreDecor>();
            if (decor != null) decor.Populate(this);

            var land = GetComponent<LandFill>();
            if (land != null)
                land.Populate(this,
                              halfW + roadMargin + shoreWidth,
                              halfH + roadMargin + shoreWidth,
                              fieldR + roadMargin + shoreWidth);
        }

        // The single definition of where the opening is. The shore mesh and anything
        // fencing the boundary have to agree about it, and they only do if they ask the
        // same question rather than each restating the rule.
        bool InMouth(Vector2 p) => p.y < -_halfH && Mathf.Abs(p.x) < _gapHalf;

        // The pen boundary — the outer edge of the channel — resampled at an even spacing
        // and split into runs wherever the mouth interrupts it. Returned as runs rather
        // than one list because a barrier drawn along it must stop at the opening and
        // start again on the far side.
        //
        // Contour samples corners densely and leaves each straight as a single long
        // segment, so walking it needs interpolation within segments; stepping sample to
        // sample would bunch everything into the corners.
        public List<List<Vector3>> BoundaryRuns(float step)
        {
            var runs = new List<List<Vector3>>();
            if (_halfW <= 0f || step <= 0.01f) return runs;

            var ring = new List<Vector2>();
            Contour(ring, _halfW + _margin, _halfH + _margin, _fieldR + _margin, 16);
            if (ring.Count < 4) return runs;

            var current = new List<Vector3>();
            float carry = 0f;

            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % ring.Count];
                float len = Vector2.Distance(a, b);
                if (len < 0.0001f) continue;

                for (float d = carry; d < len; d += step)
                {
                    Vector2 p = Vector2.Lerp(a, b, d / len);
                    if (InMouth(p))
                    {
                        // The run ends here and a new one begins past the opening.
                        if (current.Count > 1) runs.Add(current);
                        current = new List<Vector3>();
                        continue;
                    }
                    current.Add(new Vector3(p.x, 0f, p.y));
                }

                carry = (carry - len) % step;
                if (carry < 0f) carry += step;
            }

            if (current.Count > 1) runs.Add(current);

            // The walk starts mid-run, so the first and last runs are two halves of one
            // stretch unless the opening happens to fall exactly at the seam.
            if (runs.Count > 1)
            {
                var first = runs[0];
                var last = runs[runs.Count - 1];
                if (Vector3.Distance(first[0], last[last.Count - 1]) < step * 1.5f)
                {
                    last.AddRange(first);
                    runs.RemoveAt(0);
                }
            }

            return runs;
        }

        // Rounded rectangle walked anticlockwise. Both rings of a ribbon are sampled the
        // same way, so matching indices are genuine offsets of one another.
        static void Contour(List<Vector2> into, float halfW, float halfH, float radius, int segments = 8)
        {
            into.Clear();
            radius = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(halfW, halfH)));
            float cx = halfW - radius, cz = halfH - radius;

            Vector2[] centres = { new Vector2(cx, cz), new Vector2(-cx, cz), new Vector2(-cx, -cz), new Vector2(cx, -cz) };

            for (int corner = 0; corner < 4; corner++)
                for (int s = 0; s <= segments; s++)
                {
                    float angle = (corner * 90f + 90f * s / segments) * Mathf.Deg2Rad;
                    into.Add(centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
        }

        void Contour(List<Vector2> into, float halfW, float halfH, float radius) =>
            Contour(into, halfW, halfH, radius, cornerSegments);

        void Ribbon(List<Vector2> inner, List<Vector2> outer, List<int> tris, float y,
                    System.Func<Vector2, bool> skip = null, List<Vector3> centreLine = null)
        {
            int n = inner.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 mid = (inner[i] + outer[i] + inner[j] + outer[j]) * 0.25f;
                if (skip != null && skip(mid)) continue;

                AddQuad(inner[i], outer[i], outer[j], inner[j], tris, y);
                centreLine?.Add(new Vector3(mid.x, y, mid.y));
            }
        }

        // Fills a convex ring as a fan from its middle. Wound like AddQuad, so it faces up
        // for the same anticlockwise contours everything else here is built from.
        void Cap(List<Vector2> ring, List<int> tris, float y)
        {
            if (ring.Count < 3) return;

            Vector2 mid = Vector2.zero;
            foreach (var p in ring) mid += p;
            mid /= ring.Count;

            int centre = _verts.Count;
            _verts.Add(new Vector3(mid.x, y, mid.y));
            _uvs.Add(new Vector2(0.5f, 0.5f));

            int first = _verts.Count;
            foreach (var p in ring)
            {
                _verts.Add(new Vector3(p.x, y, p.y));
                _uvs.Add(new Vector2(0.5f, 0.5f));
            }

            for (int i = 0; i < ring.Count; i++)
            {
                tris.Add(centre);
                tris.Add(first + (i + 1) % ring.Count);
                tris.Add(first + i);
            }
        }

        // The wall under a cap: one quad per contour segment, dropped from the top edge.
        void Skirt(List<Vector2> ring, List<int> tris, float top, float bottom)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 p = ring[i];
                Vector2 q = ring[(i + 1) % ring.Count];

                int at = _verts.Count;
                _verts.Add(new Vector3(p.x, top, p.y));
                _verts.Add(new Vector3(q.x, top, q.y));
                _verts.Add(new Vector3(q.x, bottom, q.y));
                _verts.Add(new Vector3(p.x, bottom, p.y));
                for (int u = 0; u < 4; u++) _uvs.Add(new Vector2(0.5f, 0.5f));

                tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
                tris.Add(at); tris.Add(at + 2); tris.Add(at + 3);
            }
        }

        void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, List<int> tris, float y)
        {
            int at = _verts.Count;
            _verts.Add(new Vector3(a.x, y, a.y));
            _verts.Add(new Vector3(b.x, y, b.y));
            _verts.Add(new Vector3(c.x, y, c.y));
            _verts.Add(new Vector3(d.x, y, d.y));
            _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f));
            _uvs.Add(new Vector2(1f, 1f)); _uvs.Add(new Vector2(0f, 1f));
            tris.Add(at); tris.Add(at + 2); tris.Add(at + 1);
            tris.Add(at); tris.Add(at + 3); tris.Add(at + 2);
        }

        void Quad(float x0, float z0, float x1, float z1, List<int> tris, float y = 0f)
        {
            AddQuad(new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1), tris, y);
        }
    }
}
