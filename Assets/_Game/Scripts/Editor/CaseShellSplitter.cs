using System.Collections.Generic;
using System.IO;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // The case's colour cannot be edited on its material, because the whole model — shell,
    // trim, wheels, handle — shares one material and gets its colours from where each
    // triangle's UVs land on a palette texture. Changing anything there changes everything.
    //
    // This splits the shell into a submesh of its own: the triangles sampling the palette
    // colour that covers most of the model. That submesh gets a second material slot with a
    // plain colour, so the case can be recoloured from the inspector while the wheels and
    // fittings keep reading from the palette.
    //
    // It also gives the piece the shape the game's own tinting expects — one named material
    // for the body — so `PieceModelLibrary.palette` can colour pieces per board position
    // rather than per prefab, if that is ever wanted.
    public static class CaseShellSplitter
    {
        const string MeshDir = "Assets/_Game/Meshes/Case";
        const string MaterialDir = "Assets/_Game/Materials/Case";
        const string ManifestPath = MeshDir + "/CaseShellManifest.asset";
        const string ShellPrefix = "Case_";

        [MenuItem("SheepGame/Make Case Colour Editable")]
        public static void Split()
        {
            var prefabs = TargetPrefabs();
            if (prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("Make Case Colour Editable",
                    "Select the piece prefabs in the Project window, or open a scene whose " +
                    "Board points at a PieceModelLibrary.", "OK");
                return;
            }

            var manifest = LoadOrCreateManifest();
            int done = 0;
            foreach (var prefab in prefabs)
                if (SplitOne(prefab, manifest)) done++;

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Split the shell off {done} of {prefabs.Count} prefab(s). " +
                      $"Their colour is now the Base Color on the {ShellPrefix}* materials.");
        }

        [MenuItem("SheepGame/Restore Case Shell")]
        public static void Restore()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<CaseShellManifest>(ManifestPath);
            if (manifest == null)
            {
                EditorUtility.DisplayDialog("Restore Case Shell", "Nothing was split.", "OK");
                return;
            }

            int done = 0;
            foreach (var entry in manifest.entries)
            {
                if (entry.prefab == null || entry.original == null) continue;

                string path = AssetDatabase.GetAssetPath(entry.prefab);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var target = Resolve(contents.transform, entry.childPath);
                    var renderer = target != null ? target.GetComponent<MeshRenderer>() : null;
                    var filter = target != null ? target.GetComponent<MeshFilter>() : null;
                    if (renderer == null || filter == null) continue;

                    filter.sharedMesh = entry.original;
                    renderer.sharedMaterials = entry.originalMaterials;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    done++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            Debug.Log($"Restored {done} prefab(s) to the imported mesh and its single material. " +
                      $"The {ShellPrefix}* materials were left in place.");
        }

        // ---- one prefab ---------------------------------------------------------------

        static bool SplitOne(GameObject prefab, CaseShellManifest manifest)
        {
            // Read and sample first, edit after: sampling can re-import the model, and a
            // re-import while the prefab is open for editing invalidates the mesh it holds.
            var fill = PaletteSampler.FindChild(prefab.transform, "Fill");
            var body = PaletteSampler.FindBodyRenderer(prefab, fill);
            if (body == null)
            {
                Debug.LogWarning($"{prefab.name}: no mesh renderer to split.");
                return false;
            }

            string childPath = ChildPath(prefab.transform, body.transform);
            var entry = Find(manifest, prefab, childPath);

            var filter = body.GetComponent<MeshFilter>();
            var source = entry != null && entry.original != null ? entry.original : filter.sharedMesh;
            var sourceMaterials = entry != null && entry.originalMaterials != null && entry.originalMaterials.Length > 0
                ? entry.originalMaterials
                : body.sharedMaterials;
            var paletteMaterial = sourceMaterials.Length > 0 ? sourceMaterials[0] : null;

            var reading = PaletteSampler.Read(source, paletteMaterial, out var error);
            if (reading == null)
            {
                Debug.LogWarning($"{prefab.name}: {error}.");
                return false;
            }

            var shellColour = reading.Dominant;
            var mesh = BuildSplitMesh(reading, shellColour, out int shellTris, out int restTris);
            if (shellTris == 0)
            {
                Debug.LogWarning($"{prefab.name}: found no shell triangles.");
                return false;
            }

            string meshPath = $"{MeshDir}/{SafeName(prefab.name)}.asset";
            SaveMesh(mesh, meshPath);
            var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            // Only ever coloured on creation: a re-run must not undo a colour the user chose.
            var shellMaterial = ShellMaterial(prefab.name, paletteMaterial,
                                              PaletteSampler.ToColour(shellColour), out bool created);

            var materials = new List<Material>(sourceMaterials) { shellMaterial };

            string path = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var target = Resolve(contents.transform, childPath);
                var targetFilter = target != null ? target.GetComponent<MeshFilter>() : null;
                var targetRenderer = target != null ? target.GetComponent<MeshRenderer>() : null;
                if (targetFilter == null || targetRenderer == null) return false;

                targetFilter.sharedMesh = savedMesh;
                targetRenderer.sharedMaterials = materials.ToArray();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            if (entry == null)
            {
                entry = new CaseShellManifest.Entry { prefab = prefab, childPath = childPath };
                manifest.entries.Add(entry);
            }
            entry.original = source;
            entry.generated = savedMesh;
            entry.originalMaterials = sourceMaterials;
            entry.shellMaterial = shellMaterial;

            Debug.Log($"{prefab.name}: shell {shellTris} tris on '{shellMaterial.name}' " +
                      $"#{ColorUtility.ToHtmlStringRGB(PaletteSampler.ToColour(shellColour))}" +
                      $"{(created ? "" : " (kept your colour)")}, {restTris} tris left on the palette " +
                      $"{reading.Report()}");
            return true;
        }

        // ---- mesh ----------------------------------------------------------------------

        // Same vertices and the same triangles, only sorted into submeshes: the shell's go
        // into one of their own at the end, everything else stays in the submesh it was in,
        // so the original material slots keep their meaning.
        static Mesh BuildSplitMesh(PaletteSampler.Reading reading, Color32 shellColour,
                                   out int shellTris, out int restTris)
        {
            var mesh = new Mesh
            {
                name = reading.meshName,
                indexFormat = reading.vertices.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
                vertices = reading.vertices,
            };
            if (reading.normals != null && reading.normals.Length == reading.vertices.Length)
                mesh.normals = reading.normals;
            if (reading.uv != null && reading.uv.Length == reading.vertices.Length)
                mesh.uv = reading.uv;
            if (reading.colors != null && reading.colors.Length == reading.vertices.Length)
                mesh.colors = reading.colors;
            if (reading.tangents != null && reading.tangents.Length == reading.vertices.Length)
                mesh.tangents = reading.tangents;

            var shell = new List<int>();
            var kept = new List<int[]>();
            int triangleIndex = 0;
            restTris = 0;

            foreach (var tris in reading.subMeshTriangles)
            {
                var rest = new List<int>(tris.Length);

                for (int i = 0; i < tris.Length; i += 3, triangleIndex++)
                {
                    bool isShell = triangleIndex < reading.perTriangle.Length &&
                                   reading.perTriangle[triangleIndex].r == shellColour.r &&
                                   reading.perTriangle[triangleIndex].g == shellColour.g &&
                                   reading.perTriangle[triangleIndex].b == shellColour.b;

                    var target = isShell ? shell : rest;
                    target.Add(tris[i]);
                    target.Add(tris[i + 1]);
                    target.Add(tris[i + 2]);
                }

                restTris += rest.Count / 3;
                kept.Add(rest.ToArray());
            }

            shellTris = shell.Count / 3;

            mesh.subMeshCount = kept.Count + 1;
            for (int s = 0; s < kept.Count; s++) mesh.SetTriangles(kept[s], s);
            mesh.SetTriangles(shell, kept.Count);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- material -------------------------------------------------------------------

        static Material ShellMaterial(string prefabName, Material template, Color colour, out bool created)
        {
            EnsureFolder(MaterialDir);
            string path = $"{MaterialDir}/{ShellPrefix}{prefabName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = material == null;

            if (created)
            {
                // Copied from the palette material so the shell keeps its smoothness and
                // metallic, then stripped of the map — its colour is flat now, not mapped.
                material = new Material(template) { name = $"{ShellPrefix}{prefabName}" };
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);

                var stored = PaletteSampler.ForMaterialAsset(colour);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", stored);
                if (material.HasProperty("_Color")) material.SetColor("_Color", stored);

                AssetDatabase.CreateAsset(material, path);
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        // ---- plumbing ---------------------------------------------------------------------

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

        static CaseShellManifest LoadOrCreateManifest()
        {
            EnsureFolder(MeshDir);
            var manifest = AssetDatabase.LoadAssetAtPath<CaseShellManifest>(ManifestPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<CaseShellManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
            }
            return manifest;
        }

        static CaseShellManifest.Entry Find(CaseShellManifest manifest, GameObject prefab, string childPath)
        {
            foreach (var entry in manifest.entries)
                if (entry.prefab == prefab && entry.childPath == childPath) return entry;
            return null;
        }

        static void SaveMesh(Mesh mesh, string path)
        {
            EnsureFolder(MeshDir);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Overwritten in place so every prefab already pointing at it keeps working.
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

        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            string built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }

        static Transform Resolve(Transform root, string childPath) =>
            string.IsNullOrEmpty(childPath) ? root : root.Find(childPath);

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
