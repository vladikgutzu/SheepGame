using UnityEngine;

namespace SheepGame.Core
{
    // The interchangeable visual models a level may spawn. Pieces reference a model by
    // index rather than by a themed enum, so the same puzzle core drives any setting
    // (a pasture of sheep, an apron full of planes) purely by pointing the board at a
    // different library asset.
    [CreateAssetMenu(fileName = "PieceModelLibrary", menuName = "SheepGame/Piece Model Library")]
    public class PieceModelLibrary : ScriptableObject
    {
        public GameObject[] variants;

        [Tooltip("Cells each variant covers along the way it faces, index-matched to " +
                 "Variants. Leave empty, short, or 0 for a model that fits one cell. This " +
                 "is only so the board can pick a model the right size for a piece; the " +
                 "piece's own footprint comes from the level.")]
        public int[] variantCells;

        [Tooltip("Quarter turns about the model's up axis, in degrees, index-matched to " +
                 "Variants. Leave empty or 0 for a model that already faces the right way. A " +
                 "piece at rest faces Up, which is +Z, so the board reads a model's length " +
                 "off its Z and its width off its X -- a model drawn long across X is fitted " +
                 "as though it were one cell long and comes out tiny. Meant for right angles: " +
                 "the trim below and the belt pose both resolve to whichever of the model's " +
                 "own axes lines up, and nothing does at 45 degrees.")]
        public float[] variantYaw;

        [Tooltip("Trim applied to a variant's model, index-matched to Variants, in the " +
                 "piece's own axes: X across, Y up, Z along the way it faces. Leave empty, " +
                 "short, or 0 on an axis to keep the model as it was built. A pack's models " +
                 "are not drawn to one scale, and the board's fit only matches footprints -- " +
                 "so a model that happens to be built tall or fat is out of scale beside its " +
                 "neighbours on the same grid. This brings it back into line.")]
        public Vector3[] variantModelScale;

        [Tooltip("Which material on a variant is its shell, index-matched to Variants, " +
                 "matched by name or by prefix. Leave empty or blank to paint every material " +
                 "the model has. A pack names its shells per model -- this one has Case_Koper " +
                 "1..5 and Sumka -- so one name cannot serve the whole library, and painting " +
                 "everything would put the palette on the wheels, the handle and the zip.")]
        public string[] variantBodyMaterial;

        [Tooltip("Colours the board paints pieces in, on whichever part of the model the " +
                 "piece calls its body. A theme with one well-made model gets its variety " +
                 "from here instead of from a shelf of near-identical prefabs; leave empty " +
                 "to let every model keep the colours it was built with.")]
        public Color[] palette;

        public int VariantCount => variants != null ? variants.Length : 0;

        public int PaletteCount => palette != null ? palette.Length : 0;

        /// <summary>Cells the variant at this index covers. Never less than one.</summary>
        public int GetCells(int index)
        {
            if (variantCells == null || index < 0 || index >= variantCells.Length) return 1;
            return Mathf.Max(1, variantCells[index]);
        }

        /// <summary>Turn about up, in degrees, that points the variant at this index the right way.</summary>
        public float GetYaw(int index)
        {
            if (variantYaw == null || index < 0 || index >= variantYaw.Length) return 0f;
            return variantYaw[index];
        }

        /// <summary>How much of its built size the variant at this index keeps, per piece axis.</summary>
        public Vector3 GetModelScale(int index)
        {
            if (variantModelScale == null || index < 0 || index >= variantModelScale.Length) return Vector3.one;
            return variantModelScale[index];
        }

        /// <summary>The material name, or name prefix, the variant at this index wears as its shell.</summary>
        public string GetBodyMaterial(int index)
        {
            if (variantBodyMaterial == null || index < 0 || index >= variantBodyMaterial.Length) return "";
            return variantBodyMaterial[index] ?? "";
        }

        public GameObject GetVariant(int index)
        {
            if (variants == null || variants.Length == 0) return null;
            return variants[Mathf.Clamp(index, 0, variants.Length - 1)];
        }

        public Color GetColour(int index)
        {
            if (palette == null || palette.Length == 0) return Color.white;
            return palette[Mathf.Clamp(index, 0, palette.Length - 1)];
        }
    }
}
