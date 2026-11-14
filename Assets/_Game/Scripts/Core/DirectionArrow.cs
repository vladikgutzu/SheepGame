using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // A flat arrow badge parented to a piece, built to always point local +Z — the same
    // axis DirectionUtil.ToYRotation treats as "Up" — so it silently tracks whatever
    // direction the piece is rotated to without knowing about Direction itself.
    //
    // Drawn as a bright fill over a dark outline rather than a single flat shape: a board
    // of pastel suitcases has no colour a plain arrow reads against on all of them, and
    // the badge sits directly on a piece's lid, where the piece behind it is what it would
    // otherwise be seen against.
    //
    // Sized and positioned in world units rather than the piece's own local space, so
    // one shared piece of code reads the same whether a theme's prefab root is scaled
    // by 1 (planes) or by 9 (the suitcase pack).
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class DirectionArrow : MonoBehaviour
    {
        static Material _fillMaterial;
        static Material _outlineMaterial;

        // Shaft and head as fractions of the whole arrow. The head takes half the length
        // and the shaft under half the width, which is what makes the badge read as an
        // arrow at the size a piece is actually drawn on a phone — a slimmer head reads
        // as a line and a longer shaft reads as a stripe.
        const float HeadFraction = 0.5f;
        const float ShaftFraction = 0.44f;

        public void Build(float worldLength, float worldWidth, float worldOutline,
                          Vector3 parentLossyScale, Material fill, Material outline)
        {
            // Cancels the parent's scale, so every length this component is handed is in
            // world units and the badge is the same size on a 1x prefab and a 9x one.
            transform.localScale = new Vector3(
                Safe(parentLossyScale.x), Safe(parentLossyScale.y), Safe(parentLossyScale.z));

            GetComponent<MeshFilter>().sharedMesh = BuildMesh(worldLength, worldWidth, worldOutline);

            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                outline != null ? outline : OutlineMaterial(),
                fill != null ? fill : FillMaterial(),
            };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static float Safe(float s) => Mathf.Abs(s) > 0.0001f ? 1f / s : 1f;

        // Submesh 0 is the outline, submesh 1 the fill sitting a hair above it, so the two
        // never z-fight however the piece is tilted mid-hop.
        static Mesh BuildMesh(float length, float width, float outline)
        {
            var shape = Outline(length, width);

            var verts = new List<Vector3>();
            var tris = new List<int>[] { new List<int>(), new List<int>() };

            AddShape(Offset(shape, outline), 0f, verts, tris[0]);
            AddShape(shape, Mathf.Max(0.01f, outline * 0.25f), verts, tris[1]);

            var mesh = new Mesh { name = "DirectionArrow" };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris[0], 0);
            mesh.SetTriangles(tris[1], 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // The badge as a closed loop, anticlockwise seen from above, starting at the back
        // left of the shaft. Kept as a loop rather than as triangles so the outline can be
        // derived from it by offsetting each corner.
        static Vector3[] Outline(float length, float width)
        {
            float headLen = length * HeadFraction;
            float shaftHalf = width * ShaftFraction * 0.5f;
            float backZ = -length * 0.5f;
            float midZ = backZ + (length - headLen);
            float tipZ = length * 0.5f;

            return new[]
            {
                new Vector3(-shaftHalf, 0f, backZ),
                new Vector3( shaftHalf, 0f, backZ),
                new Vector3( shaftHalf, 0f, midZ),
                new Vector3( width * 0.5f, 0f, midZ),
                new Vector3(0f, 0f, tipZ),
                new Vector3(-width * 0.5f, 0f, midZ),
                new Vector3(-shaftHalf, 0f, midZ),
            };
        }

        // Grows the loop by a constant width all the way round, by pushing every corner
        // along the bisector of its two edges. Scaling the whole shape instead would thin
        // the outline off towards the tip, which is exactly where it is needed most.
        static Vector3[] Offset(Vector3[] loop, float amount)
        {
            if (amount <= 0.0001f) return loop;

            var grown = new Vector3[loop.Length];
            for (int i = 0; i < loop.Length; i++)
            {
                Vector3 prev = loop[(i - 1 + loop.Length) % loop.Length];
                Vector3 next = loop[(i + 1) % loop.Length];

                Vector2 nIn = OutwardNormal(prev, loop[i]);
                Vector2 nOut = OutwardNormal(loop[i], next);

                Vector2 sum = nIn + nOut;
                float scale = 1f + Vector2.Dot(nIn, nOut);
                // The barbs where the head meets the shaft turn back on themselves; a
                // mitre there runs away to infinity, so it is capped and the corner comes
                // out slightly rounded off instead of spiking across the badge.
                Vector2 mitre = scale > 0.15f ? sum / scale : sum.normalized * 3f;

                grown[i] = loop[i] + new Vector3(mitre.x, 0f, mitre.y) * amount;
            }
            return grown;
        }

        // Anticlockwise loop, so the right-hand normal of each edge points out of it.
        static Vector2 OutwardNormal(Vector3 from, Vector3 to)
        {
            var e = new Vector2(to.x - from.x, to.z - from.z);
            return e.sqrMagnitude < 0.000001f ? Vector2.zero : new Vector2(e.y, -e.x).normalized;
        }

        // Shaft quad plus head triangle. Matches ApronSurface's (a, c, b) / (a, d, c)
        // winding so the face lands right-side-up without a per-shape sign check.
        static void AddShape(Vector3[] loop, float y, List<Vector3> verts, List<int> tris)
        {
            int at = verts.Count;
            foreach (var p in loop) verts.Add(new Vector3(p.x, y, p.z));

            tris.Add(at); tris.Add(at + 2); tris.Add(at + 1);
            tris.Add(at); tris.Add(at + 6); tris.Add(at + 2);
            tris.Add(at + 5); tris.Add(at + 4); tris.Add(at + 3);
        }

        static Material FillMaterial()
        {
            // Near-white, over the dark outline.
            //
            // It was signage yellow — chosen when the luggage was pastel and nothing else in
            // the frame was yellow. Two things have changed: the cases are now fully
            // saturated, and one of them is orange, which a yellow badge sits on without
            // much to say; and the HUD has taken amber for its progress bar and its Next
            // button, so a board of yellow arrows competes with the interface for the same
            // colour. White reads against all five case colours and against the teal belt,
            // and leaves amber to mean "the thing you press".
            if (_fillMaterial == null) _fillMaterial = Unlit("DirectionArrow Fill", new Color(0.97f, 0.98f, 1f));
            return _fillMaterial;
        }

        static Material OutlineMaterial()
        {
            if (_outlineMaterial == null)
                _outlineMaterial = Unlit("DirectionArrow Outline", new Color(0.13f, 0.12f, 0.15f));
            return _outlineMaterial;
        }

        // Shader.Find has no static reference to point a build's shader stripper at, since
        // this material never becomes an asset — a build that has genuinely never needed
        // "Universal Render Pipeline/Unlit" anywhere else strips it, and Shader.Find comes
        // back null. That used to reach new Material(null), which throws: unhandled, that
        // exception unwound out of PuzzlePiece.Init through the middle of Board.LoadLevel's
        // spawn loop and aborted it, so only the one piece already instantiated existed —
        // never added to _allPieces, so tapping it read as permanently blocked. One missing
        // shader on a cosmetic badge should never be able to take the board down with it, so
        // this now degrades to a plain flat shader instead, and worst case draws no badge at
        // all rather than throw. See GraphicsSettings' Always Included Shaders for the actual
        // fix — this is the net under it.
        static Material Unlit(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("UI/Default")
                      ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning($"DirectionArrow: no unlit shader available; {name} will be missing.");
                return null;
            }

            var mat = new Material(shader) { name = name + " (auto)", color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            // Flat ground meshes get built by hand all over this project and the winding
            // that reads right-side-up from one camera angle isn't worth re-deriving per
            // shape — both sides visible sidesteps it entirely.
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            return mat;
        }
    }
}
