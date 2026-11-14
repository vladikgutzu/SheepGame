using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // A box with its edges taken off.
    //
    // The single biggest difference between a generated room and a toy one is that nothing
    // in a toy has a sharp arris: a bevel catches the light along every edge and gives the
    // silhouette a highlight instead of a hard line. Unity's cube primitive has none, and
    // the whole hall was built from cubes.
    //
    // One chamfer segment rather than a rounded fillet, on purpose: at 44 triangles it
    // costs nothing on a phone, and at this camera distance a 45-degree cut reads exactly
    // like a rounded edge while a subdivided fillet only adds vertices.
    //
    // A chamfered box's faces are octagons, joined by a quad along each of the twelve edges
    // and a triangle at each of the eight corners. Vertices are not shared between faces —
    // every face wants its own hard normal, which is what makes the bevel read.
    public static class RoundedBox
    {
        public static Mesh Create(Vector3 size, float bevel)
        {
            var half = size * 0.5f;
            // Never eat more than a third of the smallest side, or a thin slab turns into a
            // wedge — the skirting boards are 0.4 units thick.
            bevel = Mathf.Clamp(bevel, 0.001f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.66f);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // The inner extent along each axis: how far a vertex sits from the middle on the
            // axes it is not pinned to.
            var inner = half - Vector3.one * bevel;

            for (int axis = 0; axis < 3; axis++)
                for (int side = -1; side <= 1; side += 2)
                    AddFace(axis, side, half, inner, vertices, normals, uvs, triangles);

            AddEdgesAndCorners(half, inner, vertices, normals, uvs, triangles);

            var mesh = new Mesh { name = $"RoundedBox {size.x:0.##}x{size.y:0.##}x{size.z:0.##}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // One octagon, pinned to `half` on `axis` and reaching `inner` on the other two.
        static void AddFace(int axis, int side, Vector3 half, Vector3 inner,
                            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            var normal = Vector3.zero;
            normal[axis] = side;

            // Eight corners of the octagon, walked round.
            var ring = new (float u, float v)[]
            {
                (-inner[u], -half[v]), ( inner[u], -half[v]),
                ( half[u],  -inner[v]), ( half[u],   inner[v]),
                ( inner[u],  half[v]), (-inner[u],  half[v]),
                (-half[u],   inner[v]), (-half[u],  -inner[v]),
            };

            int first = vertices.Count;
            foreach (var (a, b) in ring)
            {
                var p = Vector3.zero;
                p[axis] = side * half[axis];
                p[u] = a;
                p[v] = b;
                vertices.Add(p);
                normals.Add(normal);
                // Planar, in world-ish units so a tiled texture keeps its scale whatever the
                // box is sized to.
                uvs.Add(new Vector2(a, b));
            }

            for (int i = 1; i < 7; i++)
            {
                if (side > 0) { triangles.Add(first); triangles.Add(first + i); triangles.Add(first + i + 1); }
                else { triangles.Add(first); triangles.Add(first + i + 1); triangles.Add(first + i); }
            }
        }

        // The chamfer itself: a quad down each edge and a triangle over each corner, built
        // from the 24 points that the three face rings share.
        static void AddEdgesAndCorners(Vector3 half, Vector3 inner,
                                       List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            // Point pinned on two axes and inner on the third.
            Vector3 Point(int pinnedA, int signA, int pinnedB, int signB, int free, float freeValue)
            {
                var p = Vector3.zero;
                p[pinnedA] = signA * half[pinnedA];
                p[pinnedB] = signB * half[pinnedB];
                p[free] = freeValue;
                return p;
            }

            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                for (int su = -1; su <= 1; su += 2)
                    for (int sv = -1; sv <= 1; sv += 2)
                    {
                        // The edge running along `axis` at this corner of the u/v plane.
                        var a0 = Point(u, su, v, sv, axis, -inner[axis]);
                        var a1 = Point(u, su, v, sv, axis, inner[axis]);

                        // Its two neighbours on the adjoining faces.
                        var b0 = a0; b0[u] = su * inner[u]; b0[v] = sv * half[v];
                        var b1 = a1; b1[u] = su * inner[u]; b1[v] = sv * half[v];
                        var c0 = a0; c0[u] = su * half[u]; c0[v] = sv * inner[v];
                        var c1 = a1; c1[u] = su * half[u]; c1[v] = sv * inner[v];

                        Quad(b0, b1, c1, c0, vertices, normals, uvs, triangles);
                    }
            }

            // A triangle across each corner, closing the three chamfers that meet there.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var px = new Vector3(sx * inner.x, sy * half.y, sz * half.z);
                        var py = new Vector3(sx * half.x, sy * inner.y, sz * half.z);
                        var pz = new Vector3(sx * half.x, sy * half.y, sz * inner.z);
                        Triangle(px, py, pz, sx * sy * sz > 0, vertices, normals, uvs, triangles);
                    }
        }

        static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(normal, (a + b + c + d) * 0.25f) < 0f)
            {
                (a, b, c, d) = (d, c, b, a);
                normal = -normal;
            }

            int first = vertices.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                vertices.Add(p);
                normals.Add(normal);
                uvs.Add(new Vector2(p.x + p.z, p.y));
            }
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }

        static void Triangle(Vector3 a, Vector3 b, Vector3 c, bool flip,
                             List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            if (flip) (a, c) = (c, a);
            var normal = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(normal, (a + b + c) / 3f) < 0f) { (a, c) = (c, a); normal = -normal; }

            int first = vertices.Count;
            foreach (var p in new[] { a, b, c })
            {
                vertices.Add(p);
                normals.Add(normal);
                uvs.Add(new Vector2(p.x + p.z, p.y));
            }
            triangles.AddRange(new[] { first, first + 1, first + 2 });
        }
    }
}
