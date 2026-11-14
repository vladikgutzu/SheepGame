using System.Collections.Generic;
using System.IO;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Closes actual holes in an imported mesh by capping their rims.
    //
    // This is the opposite repair to PieceMeshSolidifier's, and the two are not
    // interchangeable. The Koper cases have panels that were never modelled: absent surface
    // has no boundary, nothing can be capped, and what fixes them is a reversed copy of every
    // triangle sitting behind the original. The wide case's gaps are the other kind — Unity's
    // importer reported "a polygon of Mesh 'Suitcase_Horizontal_2Cell' is self-intersecting
    // and has been discarded" four times, and a discarded polygon leaves a real hole with a
    // real rim. The mesh measures 56 open edges in 8 loops: four ten-sided (the discarded
    // faces) and four four-sided. Each loop gets a fan of triangles and the hole is gone.
    //
    // The source .fbx is never touched. A new mesh is written beside the project's own art and
    // the prefab is pointed at it, with the original kept in a manifest so Restore can put it
    // back after the artist re-exports.
    public static class MeshHoleCapper
    {
        const string OutputDir = "Assets/_Game/Meshes/Capped";
        const string ManifestPath = OutputDir + "/CapManifest.asset";

        // Positions are welded before edges are counted: an .fbx splits a corner into one
        // vertex per normal and per UV island, so without this every smoothing seam and every
        // texture seam reads as an open edge and the mesh comes back looking like lace.
        const float Weld = 10000f;

        [MenuItem("SheepGame/Cap Mesh Holes")]
        public static void Cap()
        {
            var prefabs = TargetPrefabs();
            if (prefabs.Count == 0)
            {
                Debug.LogWarning("Cap Mesh Holes: select the piece prefabs in the Project " +
                                 "window, or open a scene whose Board points at a library.");
                return;
            }

            Directory.CreateDirectory(OutputDir);
            var manifest = LoadOrCreateManifest();
            int capped = 0, holes = 0;

            foreach (var prefab in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(prefab);

                // Edited through the loaded asset rather than through LoadPrefabContents. The
                // contents API opens the prefab in a preview scene, and this project's wide
                // case comes back from one with its MeshFilter present but its mesh null,
                // which reads as "the prefab has no meshes" and silently does nothing.
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;

                bool changed = false;

                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    string childPath = ChildPath(root.transform, filter.transform);
                    var entry = Find(manifest, prefab, childPath);

                    // Re-running builds from the original art, never from a mesh already capped.
                    var source = entry != null && entry.original != null ? entry.original : filter.sharedMesh;
                    if (source == null)
                    {
                        Debug.LogWarning($"Cap Mesh Holes: {prefab.name}/{filter.name} has no mesh.");
                        continue;
                    }

                    var built = Build(source, out int loops);
                    if (loops == 0)
                    {
                        Debug.Log($"{prefab.name}/{filter.name}: no open boundary, left alone.");
                        Object.DestroyImmediate(built);
                        continue;
                    }

                    string assetPath = $"{OutputDir}/{SafeName(prefab.name)}_{SafeName(filter.name)}.asset";
                    SaveMesh(built, assetPath);
                    var saved = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

                    filter.sharedMesh = saved;
                    changed = true;
                    capped++;
                    holes += loops;

                    if (entry == null)
                    {
                        entry = new SolidifyManifest.Entry { prefab = prefab, childPath = childPath };
                        manifest.entries.Add(entry);
                    }
                    entry.original = source;
                    entry.generated = saved;

                    Debug.Log($"{prefab.name}/{filter.name}: capped {loops} hole(s), " +
                              $"{source.triangles.Length / 3} tris -> {saved.triangles.Length / 3} ({assetPath})");
                }

                if (changed) PrefabUtility.SavePrefabAsset(root);
            }

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            Debug.Log($"Capped {holes} hole(s) across {capped} mesh(es).");
        }

        [MenuItem("SheepGame/Restore Capped Meshes")]
        public static void Restore()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<SolidifyManifest>(ManifestPath);
            if (manifest == null) { Debug.LogWarning("Restore Capped Meshes: nothing was replaced."); return; }

            foreach (var entry in manifest.entries)
            {
                if (entry.prefab == null || entry.original == null) continue;

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(entry.prefab));
                if (root == null) continue;

                var target = string.IsNullOrEmpty(entry.childPath)
                    ? root.transform : root.transform.Find(entry.childPath);
                var filter = target != null ? target.GetComponent<MeshFilter>() : null;
                if (filter == null) continue;

                filter.sharedMesh = entry.original;
                PrefabUtility.SavePrefabAsset(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Restored {manifest.entries.Count} mesh reference(s) to the imported art.");
        }

        // ---- mesh work ---------------------------------------------------------------

        // Every triangle is kept as it was; the caps are appended. Loop corners reuse the
        // existing vertices, so a cap inherits the rim's UVs and lands on the same patch of
        // whatever the material samples; only the fan's centre is a new vertex, and it takes
        // the average of the rim it closes.
        static Mesh Build(Mesh source, out int loopCount)
        {
            var verts = new List<Vector3>(source.vertices);
            var normals = new List<Vector3>(source.normals);
            var uvs = new List<Vector2>(source.uv);
            bool hasNormals = normals.Count == verts.Count;
            bool hasUvs = uvs.Count == verts.Count;

            var loops = FindLoops(source);
            loopCount = loops.Count;

            var built = new Mesh { name = source.name + "_Capped" };
            built.indexFormat = verts.Count + loops.Count > 65000
                              ? UnityEngine.Rendering.IndexFormat.UInt32
                              : UnityEngine.Rendering.IndexFormat.UInt16;

            // The caps join the first submesh: these models draw their shell from it, and a
            // hole in the shell is the only kind this repair is for.
            var submeshes = new List<int[]>();
            for (int s = 0; s < source.subMeshCount; s++) submeshes.Add(source.GetTriangles(s));

            var extra = new List<int>();

            foreach (var loop in loops)
            {
                Vector3 centre = Vector3.zero;
                Vector3 normal = Vector3.zero;
                Vector2 uv = Vector2.zero;

                foreach (int v in loop)
                {
                    centre += verts[v];
                    if (hasNormals) normal += normals[v];
                    if (hasUvs) uv += uvs[v];
                }

                centre /= loop.Count;
                if (hasUvs) uv /= loop.Count;
                normal = hasNormals && normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;

                int hub = verts.Count;
                verts.Add(centre);
                if (hasNormals) normals.Add(normal);
                if (hasUvs) uvs.Add(uv);

                // The rim is walked the way its half-edges run, and each cap triangle is wound
                // the other way round, which is the winding the missing face had: a half-edge
                // a to b is only a boundary because nothing supplies b to a.
                for (int i = 0; i < loop.Count; i++)
                {
                    int a = loop[i], b = loop[(i + 1) % loop.Count];
                    extra.Add(hub);
                    extra.Add(b);
                    extra.Add(a);
                }
            }

            submeshes[0] = Concat(submeshes[0], extra);

            built.SetVertices(verts);
            if (hasNormals) built.SetNormals(normals);
            if (hasUvs) built.SetUVs(0, uvs);

            built.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) built.SetTriangles(submeshes[s], s);

            built.RecalculateBounds();
            return built;
        }

        static int[] Concat(int[] head, List<int> tail)
        {
            var all = new int[head.Length + tail.Count];
            head.CopyTo(all, 0);
            for (int i = 0; i < tail.Count; i++) all[head.Length + i] = tail[i];
            return all;
        }

        // Boundary half-edges chained rim by rim. Indices come back in the original vertex
        // list, not the welded one, so the caps can reuse the real vertices and their UVs.
        static List<List<int>> FindLoops(Mesh mesh)
        {
            var verts = mesh.vertices;
            var weld = new Dictionary<Vector3, int>();
            var map = new int[verts.Length];
            var firstOf = new Dictionary<int, int>();

            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3(Mathf.Round(verts[i].x * Weld) / Weld,
                                      Mathf.Round(verts[i].y * Weld) / Weld,
                                      Mathf.Round(verts[i].z * Weld) / Weld);
                if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; firstOf[id] = i; }
                map[i] = id;
            }

            // Directed and welded. A half-edge with no opposite is a rim.
            var directed = new HashSet<long>();
            var tris = mesh.triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                directed.Add(Key(map[tris[t]], map[tris[t + 1]]));
                directed.Add(Key(map[tris[t + 1]], map[tris[t + 2]]));
                directed.Add(Key(map[tris[t + 2]], map[tris[t]]));
            }

            var next = new Dictionary<int, int>();
            foreach (long edge in directed)
            {
                int a = (int)(edge >> 32), b = (int)(edge & 0xffffffff);
                if (!directed.Contains(Key(b, a))) next[a] = b;
            }

            var loops = new List<List<int>>();
            var visited = new HashSet<int>();

            foreach (int start in next.Keys)
            {
                if (!visited.Add(start)) continue;

                var loop = new List<int> { firstOf[start] };
                int at = start;

                while (next.TryGetValue(at, out int step) && step != start)
                {
                    if (!visited.Add(step)) break;
                    loop.Add(firstOf[step]);
                    at = step;
                }

                // Two edges is a crease, not a hole.
                if (loop.Count >= 3) loops.Add(loop);
            }

            return loops;
        }

        static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        // ---- plumbing ------------------------------------------------------------------

        static void SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
            }
            else AssetDatabase.CreateAsset(mesh, path);
        }

        static SolidifyManifest LoadOrCreateManifest()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<SolidifyManifest>(ManifestPath);
            if (manifest != null) return manifest;

            Directory.CreateDirectory(OutputDir);
            manifest = ScriptableObject.CreateInstance<SolidifyManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
            return manifest;
        }

        static SolidifyManifest.Entry Find(SolidifyManifest manifest, GameObject prefab, string childPath)
        {
            foreach (var entry in manifest.entries)
                if (entry.prefab == prefab && entry.childPath == childPath) return entry;
            return null;
        }

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

        static string ChildPath(Transform root, Transform child)
        {
            if (child == root) return "";
            var stack = new Stack<string>();
            for (var t = child; t != null && t != root; t = t.parent) stack.Push(t.name);
            return string.Join("/", stack);
        }

        static string SafeName(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name;
        }
    }
}
