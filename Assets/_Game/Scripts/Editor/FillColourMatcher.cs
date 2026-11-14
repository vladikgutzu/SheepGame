using System.Collections.Generic;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // The suitcases all share one material and get their colour from where their UVs land on
    // a palette texture, so a filler object cannot just be given the body's material — a
    // built-in cube's UVs cover the whole palette, not one swatch. This reads the colour the
    // body actually shows, and bakes it into a plain material per piece.
    //
    // Run it again after recolouring a case: it prefers the flat shell material left by
    // SheepGame > Make Case Colour Editable over the palette, so the filler follows.
    public static class FillColourMatcher
    {
        const string ChildName = "Fill";
        const string MaterialDir = "Assets/_Game/Materials/Fill";

        [MenuItem("SheepGame/Match Fill Colour")]
        public static void Match()
        {
            var prefabs = TargetPrefabs();
            if (prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("Match Fill Colour",
                    "Select the piece prefabs in the Project window, or open a scene whose " +
                    "Board points at a PieceModelLibrary.", "OK");
                return;
            }

            int matched = 0;
            foreach (var prefab in prefabs)
                if (MatchOne(prefab)) matched++;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Matched the {ChildName} colour on {matched} of {prefabs.Count} prefab(s).");
        }

        static bool MatchOne(GameObject prefab)
        {
            // Read from the prefab asset first and edit it after. Sampling can re-import the
            // model to make its mesh readable, and a re-import while a prefab is open for
            // editing invalidates the very mesh that prefab points at.
            var fillOnAsset = PaletteSampler.FindChild(prefab.transform, ChildName);
            if (fillOnAsset == null)
            {
                Debug.LogWarning($"{prefab.name}: no child called '{ChildName}'.");
                return false;
            }

            var body = PaletteSampler.FindBodyRenderer(prefab, fillOnAsset);
            if (fillOnAsset.GetComponent<MeshRenderer>() == null || body == null)
            {
                Debug.LogWarning($"{prefab.name}: nothing to read a colour from.");
                return false;
            }

            var colour = BodyColour(body, out var template, out var report);
            if (!colour.HasValue)
            {
                Debug.LogWarning($"{prefab.name}: could not read the body colour. {report}");
                return false;
            }

            var material = MaterialFor(prefab.name, template, colour.Value);

            string path = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fill = PaletteSampler.FindChild(contents.transform, ChildName);
                var renderer = fill != null ? fill.GetComponent<MeshRenderer>() : null;
                if (renderer == null) return false;

                renderer.sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Debug.Log($"{prefab.name}: {ChildName} -> {material.name} " +
                      $"#{ColorUtility.ToHtmlStringRGB(colour.Value)}   {report}");
            return true;
        }

        // ---- colour ------------------------------------------------------------------

        // A flat material on the body — the one the shell splitter leaves behind — is the
        // case's colour outright, and is authored, so it wins over anything sampled. Only
        // when there is no such slot does the palette have to be read.
        static Color? BodyColour(MeshRenderer body, out Material template, out string report)
        {
            template = body.sharedMaterial;
            report = "";

            foreach (var material in body.sharedMaterials)
            {
                if (material == null) continue;
                bool mapped = material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null;
                if (mapped || !material.HasProperty("_BaseColor")) continue;

                template = material;
                report = $"(took {material.name}'s Base Color)";
                return PaletteSampler.FromMaterialAsset(material.GetColor("_BaseColor"));
            }

            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            var reading = PaletteSampler.Read(mesh, template, out var error);
            if (reading == null) { report = error; return null; }

            report = reading.Report();
            return PaletteSampler.ToColour(reading.Dominant);
        }

        // ---- material ----------------------------------------------------------------

        static Material MaterialFor(string prefabName, Material template, Color colour)
        {
            EnsureFolder();
            string path = $"{MaterialDir}/Fill_{prefabName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                // Copied from the body so the fill takes its smoothness and metallic too,
                // then stripped of the palette — the colour is flat now, not mapped.
                material = new Material(template) { name = $"Fill_{prefabName}" };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);

            var stored = PaletteSampler.ForMaterialAsset(colour);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", stored);
            if (material.HasProperty("_Color")) material.SetColor("_Color", stored);

            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Materials"))
                AssetDatabase.CreateFolder("Assets/_Game", "Materials");
            if (!AssetDatabase.IsValidFolder(MaterialDir))
                AssetDatabase.CreateFolder("Assets/_Game/Materials", "Fill");
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
    }
}
