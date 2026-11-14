using System.Collections.Generic;
using System.IO;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // The Koper suitcases are modelled as one-sided shells: whole panels between the ribs
    // have no surface at all, so from the side a piece faces away from you look straight
    // through it into the scene. Nothing can be filled by capping boundary edges — the mesh
    // has only four small open slots and the missing panels are not holes in a surface,
    // they are surface that was never built.
    //
    // What closes them is a back layer: every triangle copied, wound the other way and
    // pushed a hair inwards along its normal. Where the model already has a front face the
    // copy is behind it and culled; where it has none, the copy is what you see, lit by a
    // normal that points at you instead of away. It costs one extra triangle per triangle
    // and needs no decision about which panels are missing.
    public static class PieceMeshSolidifier
    {
        const string OutputDir = "Assets/_Game/Meshes/Solid";
        const string ManifestPath = OutputDir + "/SolidifyManifest.asset";

        // Share of the model's diagonal the back layer is sunk in by. Zero, and deliberately:
        // a triangle and its reversed twin are never both visible — whichever one faces the
        // camera is drawn and the other is culled before it can reach the depth test — so
        // there is nothing to fight and no reason to move it. Sinking it even slightly opens
        // a hairline of background along every silhouette seam, which is what the first
        // attempt did. Left as a knob in case a model turns up whose twin needs the room.
        const float SinkFraction = 0f;

        // Some openings go clean through — the panel is missing on both faces, so the two
        // gaps line up and a back layer has nothing to show. Those are closed by a pair of
        // plates set just inside the two largest faces, sized to the model's own footprint.
        // Share of the footprint the plates are pulled in by, so they stay behind the rim
        // instead of poking through it.
        const float PlateInset = 0.08f;

        // How far in from the face the plate sits, as a share of the model's half-thickness.
        // Far enough not to z-fight the shell, near enough that an opening does not read as
        // a deep box.
        const float PlateDepth = 0.55f;

        [MenuItem("SheepGame/Close Piece Mesh Gaps")]
        public static void Solidify()
        {
            var prefabs = TargetPrefabs();
            if (prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("Close Piece Mesh Gaps",
                    "Select the piece prefabs in the Project window, or open a scene whose " +
                    "Board points at a PieceModelLibrary.", "OK");
                return;
            }

            var manifest = LoadOrCreateManifest();
            int meshes = 0;

            foreach (var prefab in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                var contents = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;

                foreach (var filter in contents.GetComponentsInChildren<MeshFilter>(true))
                {
                    string childPath = ChildPath(contents.transform, filter.transform);
                    var entry = Find(manifest, prefab, childPath);

                    // Re-running must build from the original art, not from a mesh that has
                    // already been doubled.
                    var source = entry != null && entry.original != null ? entry.original : filter.sharedMesh;
                    if (source == null) continue;

                    var generated = Build(source);
                    string assetPath = $"{OutputDir}/{SafeName(prefab.name)}_{SafeName(filter.name)}.asset";
                    SaveMesh(generated, assetPath);
                    var saved = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

                    filter.sharedMesh = saved;
                    changed = true;
                    meshes++;

                    if (entry == null)
                    {
                        entry = new SolidifyManifest.Entry { prefab = prefab, childPath = childPath };
                        manifest.entries.Add(entry);
                    }
                    entry.original = source;
                    entry.generated = saved;

                    Debug.Log($"{prefab.name}/{filter.name}: {source.triangles.Length / 3} tris -> " +
                              $"{saved.triangles.Length / 3} ({assetPath})");
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Closed gaps on {meshes} mesh(es) across {prefabs.Count} prefab(s).");
        }

        [MenuItem("SheepGame/Restore Piece Meshes")]
        public static void Restore()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<SolidifyManifest>(ManifestPath);
            if (manifest == null)
            {
                EditorUtility.DisplayDialog("Restore Piece Meshes", "Nothing was replaced.", "OK");
                return;
            }

            foreach (var entry in manifest.entries)
            {
                if (entry.prefab == null || entry.original == null) continue;
                string path = AssetDatabase.GetAssetPath(entry.prefab);
                var contents = PrefabUtility.LoadPrefabContents(path);
                var target = string.IsNullOrEmpty(entry.childPath)
                    ? contents.transform
                    : contents.transform.Find(entry.childPath);
                var filter = target != null ? target.GetComponent<MeshFilter>() : null;
                if (filter != null)
                {
                    filter.sharedMesh = entry.original;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Debug.Log($"Restored {manifest.entries.Count} mesh reference(s) to the imported art.");
        }

        // ---- mesh work ---------------------------------------------------------------

        static Mesh Build(Mesh source)
        {
            var verts = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var colours = source.colors;
            var tangents = source.tangents;

            if (normals == null || normals.Length != verts.Length)
            {
                source.RecalculateNormals();
                normals = source.normals;
            }

            var plate = BuildPlates(source, verts, uv);

            float sink = source.bounds.size.magnitude * SinkFraction;
            int n = verts.Length;
            int m = plate.positions.Count;
            int front = n + m;              // one full layer: the art, then the plates
            int total = front * 2;          // and the same again, facing the other way

            var outVerts = new Vector3[total];
            var outNormals = new Vector3[total];
            var outUv = uv.Length == n ? new Vector2[total] : null;
            var outColours = colours.Length == n ? new Color[total] : null;
            var outTangents = tangents.Length == n ? new Vector4[total] : null;

            void Put(int i, Vector3 position, Vector3 normal, Vector2 texcoord, Color colour, Vector4 tangent)
            {
                outVerts[i] = position;
                outNormals[i] = normal;
                outVerts[front + i] = position - normal.normalized * sink;
                outNormals[front + i] = -normal;

                if (outUv != null) { outUv[i] = texcoord; outUv[front + i] = texcoord; }
                if (outColours != null) { outColours[i] = colour; outColours[front + i] = colour; }
                if (outTangents != null)
                {
                    outTangents[i] = tangent;
                    outTangents[front + i] = new Vector4(tangent.x, tangent.y, tangent.z, -tangent.w);
                }
            }

            for (int i = 0; i < n; i++)
                Put(i, verts[i], normals[i],
                    uv.Length == n ? uv[i] : Vector2.zero,
                    colours.Length == n ? colours[i] : Color.white,
                    tangents.Length == n ? tangents[i] : new Vector4(1f, 0f, 0f, 1f));

            for (int i = 0; i < m; i++)
                Put(n + i, plate.positions[i], plate.normals[i], plate.uvs[i], Color.white,
                    new Vector4(1f, 0f, 0f, 1f));

            var mesh = new Mesh
            {
                name = source.name,
                indexFormat = total > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : source.indexFormat,
                vertices = outVerts,
                normals = outNormals,
            };
            if (outUv != null) mesh.uv = outUv;
            if (outColours != null) mesh.colors = outColours;
            if (outTangents != null) mesh.tangents = outTangents;

            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = new List<int>(source.GetTriangles(s));
                // The plates join the first submesh, which is the one the body material is on.
                if (s == 0)
                    foreach (int index in plate.triangles) tris.Add(n + index);

                int count = tris.Count;
                for (int i = 0; i < count; i += 3)
                {
                    // Reversed, so the copy faces the way the original does not.
                    tris.Add(front + tris[i + 2]);
                    tris.Add(front + tris[i + 1]);
                    tris.Add(front + tris[i]);
                }
                mesh.SetTriangles(tris, s);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        class Plate
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }

        // Two quads just inside the model's largest pair of faces. Sized from percentiles
        // rather than the bounds, because a handle or a wheel sticking out would otherwise
        // set the size and the plate would poke through the shell.
        static Plate BuildPlates(Mesh source, Vector3[] verts, Vector2[] uv)
        {
            var plate = new Plate();
            if (verts.Length == 0) return plate;

            Vector3 low = Percentile(verts, 0.02f);
            Vector3 high = Percentile(verts, 0.98f);
            Vector3 size = high - low;
            Vector3 centre = (high + low) * 0.5f;

            // The thin axis is the one the two big faces look along.
            int thin = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);
            int u = (thin + 1) % 3, v = (thin + 2) % 3;

            float halfU = size[u] * 0.5f * (1f - PlateInset);
            float halfV = size[v] * 0.5f * (1f - PlateInset);
            float depth = size[thin] * 0.5f * PlateDepth;

            Vector2 swatch = SampleSwatch(source, verts, uv, thin);

            foreach (int sign in new[] { 1, -1 })
            {
                int start = plate.positions.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float du = (corner == 0 || corner == 3) ? -halfU : halfU;
                    float dv = corner < 2 ? -halfV : halfV;

                    var p = centre;
                    p[thin] += depth * sign;
                    p[u] += du;
                    p[v] += dv;

                    var normal = Vector3.zero;
                    normal[thin] = sign;

                    plate.positions.Add(p);
                    plate.normals.Add(normal);
                    plate.uvs.Add(swatch);
                }

                // Corners run anticlockwise in the (u, v) basis, and u, v, thin are a cyclic
                // permutation of x, y, z — so that order already faces +thin, and the far
                // plate takes the reverse.
                plate.triangles.AddRange(sign > 0
                    ? new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                    : new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            return plate;
        }

        static Vector3 Percentile(Vector3[] verts, float q)
        {
            var result = Vector3.zero;
            for (int axis = 0; axis < 3; axis++)
            {
                var values = new float[verts.Length];
                for (int i = 0; i < verts.Length; i++) values[i] = verts[i][axis];
                System.Array.Sort(values);
                result[axis] = values[Mathf.Clamp(Mathf.RoundToInt((values.Length - 1) * q), 0, values.Length - 1)];
            }
            return result;
        }

        // The palette texture these models use puts each colour in one small swatch, so a
        // plate only needs the UV of the biggest triangle already facing that way to come
        // out the same colour as the shell around it.
        static Vector2 SampleSwatch(Mesh source, Vector3[] verts, Vector2[] uv, int thin)
        {
            if (uv == null || uv.Length != verts.Length) return Vector2.zero;

            var tris = source.triangles;
            float best = 0f;
            Vector2 result = uv.Length > 0 ? uv[0] : Vector2.zero;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                if (area <= best) continue;

                var n = cross.normalized;
                if (Mathf.Abs(n[thin]) < 0.85f) continue;

                best = area;
                result = (uv[tris[i]] + uv[tris[i + 1]] + uv[tris[i + 2]]) / 3f;
            }
            return result;
        }

        // ---- plumbing ----------------------------------------------------------------

        static List<GameObject> TargetPrefabs()
        {
            var list = new List<GameObject>();

            foreach (var obj in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
                if (PrefabUtility.IsPartOfPrefabAsset(obj)) list.Add(obj);
            if (list.Count > 0) return list;

            var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
            var library = board != null ? board.modelLibrary : null;
            if (library == null) return list;

            foreach (var variant in library.variants)
                if (variant != null && !list.Contains(variant)) list.Add(variant);
            return list;
        }

        static SolidifyManifest LoadOrCreateManifest()
        {
            EnsureFolder();
            var manifest = AssetDatabase.LoadAssetAtPath<SolidifyManifest>(ManifestPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<SolidifyManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
            }
            return manifest;
        }

        static SolidifyManifest.Entry Find(SolidifyManifest manifest, GameObject prefab, string childPath)
        {
            foreach (var entry in manifest.entries)
                if (entry.prefab == prefab && entry.childPath == childPath) return entry;
            return null;
        }

        static void SaveMesh(Mesh mesh, string path)
        {
            EnsureFolder();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Overwritten in place so every prefab and scene already pointing at it
                // keeps working.
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Meshes"))
                AssetDatabase.CreateFolder("Assets/_Game", "Meshes");
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/_Game/Meshes", "Solid");
        }

        static string ChildPath(Transform root, Transform child)
        {
            if (child == root) return "";
            var stack = new List<string>();
            for (var t = child; t != null && t != root; t = t.parent) stack.Insert(0, t.name);
            return string.Join("/", stack);
        }

        static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
