using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // The five case colours, in one place and re-runnable.
    //
    // `CaseShellSplitter` gave each suitcase's shell a material of its own — `Case_Koper N`,
    // a flat colour, shared by the shell and by the `Fill` cube inside it — so the cases can
    // be pushed without touching the palette texture that the wheels, handle, latches and
    // ribs sample. Doing it the other way round, through the grade, would drag the hall's
    // floor with it, and the floor is most of the frame.
    //
    // Authored is what the user picked; the boost is applied in HSV so hue and brightness
    // stay put and only the colour's purity moves. Idempotent: the table below is always the
    // starting point, so running this twice is the same as running it once, and a boost of 1
    // puts the authored colours back.
    public static class CaseColours
    {
        const string MaterialDir = "Assets/_Game/Materials/Case";

        // 1.0 leaves them as authored. Past about 1.8 the green and the blue lose their
        // last neutral channel and start to read as pure hue rather than as luggage.
        const float Boost = 1.55f;

        static readonly (string material, Color authored)[] Cases =
        {
            ("Case_Koper 1", new Color(1.000f, 0.500f, 0.000f)),   // orange, already at full
            ("Case_Koper 2", new Color(0.784f, 0.533f, 0.533f)),   // rose
            ("Case_Koper 3", new Color(0.439f, 0.612f, 0.220f)),   // green
            ("Case_Koper 4", new Color(0.376f, 0.518f, 0.580f)),   // blue
            // Was a neutral grey, kept as something to read the coloured cases against —
            // until the brief became "only coloured shades". Violet because the other four
            // are orange, rose, green and blue, and because yellow would fight the arrows.
            ("Case_Koper 5", new Color(0.549f, 0.400f, 0.749f)),
        };

        [MenuItem("SheepGame/Saturate Case Colours")]
        public static void Apply()
        {
            foreach (var (name, authored) in Cases)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{name}.mat");
                if (material == null) { Debug.LogWarning($"No material at {MaterialDir}/{name}.mat"); continue; }

                Color.RGBToHSV(authored, out float h, out float s, out float v);
                var boosted = Color.HSVToRGB(h, Mathf.Clamp01(s * Boost), v);

                // Straight sRGB into a material asset: the file stores sRGB and Unity
                // converts on load, so a .linear here would land about half as bright.
                material.SetColor("_BaseColor", boosted);
                EditorUtility.SetDirty(material);

                Debug.Log($"{name}: {authored} -> {boosted} (saturation {s:F2} -> {Mathf.Clamp01(s * Boost):F2})");
            }

            AssetDatabase.SaveAssets();
        }
    }
}
