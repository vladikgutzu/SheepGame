using System.IO;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Lifts every grey and every near-black swatch out of the two palette atlases into
    // colour, and points the materials at the recoloured copies. The originals are left
    // alone — the copies live in _Game/Textures, so a re-run rebuilds them from source and
    // never compounds.
    //
    // These atlases are where the scene's dark mass comes from: the belt's black top, the
    // black strips down the check-in desks, the wheels and handles on every suitcase. No
    // material tint can reach them, because a tint multiplies and black stays black.
    //
    // Grey is mapped onto one hue family rather than a random one per swatch, so the room
    // still reads as a room. Brightness order is kept — a swatch that was darker than its
    // neighbour stays darker — otherwise the shading modelled into the pack's texturing
    // flattens out.
    public static class PackPalette
    {
        const string OutputDir = "Assets/_Game/Textures";

        static readonly (string source, string output, float hue)[] Atlases =
        {
            // Teal, to sit with the belt rails and the check-in desks that are already blue.
            ("Assets/Airport/source/airport-inside-building-pack/textures/Texture_airport256b.png",
             "Airport_Palette_Colour.png", 0.52f),
            // Indigo for the luggage furniture — wheels, handles, latches, ribs — so it is
            // clearly not the same colour as any case shell.
            ("Assets/Airport/source/Suitcase/color palette .png",
             "Suitcase_Palette_Colour.png", 0.62f),
        };

        // What counts as needing colour, and where it lands.
        const float GreyBelow = 0.20f;   // saturation under this is a grey
        const float DarkBelow = 0.32f;   // value under this is a dark, hue or no hue
        const float FloorValue = 0.42f;  // nothing comes out darker than this
        const float CeilingValue = 0.92f;

        [MenuItem("SheepGame/Recolour Dark Swatches")]
        public static void Recolour()
        {
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/_Game", "Textures");

            foreach (var (source, output, hue) in Atlases)
            {
                if (!File.Exists(source)) { Debug.LogWarning("No atlas at " + source); continue; }

                // Decoded from the file rather than read off the imported texture: flipping
                // isReadable re-imports the asset, and this only needs the pixels.
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(File.ReadAllBytes(source))) { Debug.LogWarning("Cannot read " + source); continue; }

                var pixels = texture.GetPixels();
                int touched = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    bool grey = s < GreyBelow;
                    bool dark = v < DarkBelow;
                    if (!grey && !dark) continue;

                    // Whites are already bright and carry no gloom; leaving them alone keeps
                    // the pack's highlights and its paper-white surfaces.
                    if (grey && v > 0.86f) continue;

                    // The darker it was, the more colour it takes, so black becomes a deep
                    // saturated tone and a mid grey a light one.
                    float lifted = Mathf.Lerp(FloorValue, CeilingValue, Mathf.Clamp01(v / 0.86f));
                    float saturation = Mathf.Lerp(0.78f, 0.34f, Mathf.Clamp01(v / 0.86f));
                    if (!grey) saturation = Mathf.Max(saturation, s);   // a dark colour keeps its own

                    var recoloured = Color.HSVToRGB(grey ? hue : h, saturation, lifted);
                    pixels[i] = new Color(recoloured.r, recoloured.g, recoloured.b, c.a);
                    touched++;
                }

                var result = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                result.SetPixels(pixels);
                result.Apply();

                string path = $"{OutputDir}/{output}";
                File.WriteAllBytes(path, result.EncodeToPNG());
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(result);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;  // swatch grid: no bleed
                importer.SaveAndReimport();

                Debug.Log($"{output}: {100f * touched / pixels.Length:F1}% of the atlas recoloured.");
            }

            Repoint("Assets/Airport/source/airport-inside-building-pack/materials/AIRPORT 1.mat",
                    $"{OutputDir}/Airport_Palette_Colour.png");
            Repoint("Assets/Airport/source/Suitcase/materials/Material.001.mat",
                    $"{OutputDir}/Suitcase_Palette_Colour.png");

            AssetDatabase.SaveAssets();
        }

        static void Repoint(string materialPath, string texturePath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (material == null || texture == null)
            {
                Debug.LogWarning($"Cannot repoint {materialPath} at {texturePath}");
                return;
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
            Debug.Log($"{Path.GetFileName(materialPath)} now uses {Path.GetFileName(texturePath)}");
        }
    }
}
