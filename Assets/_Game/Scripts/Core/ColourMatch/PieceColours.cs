using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>The colours a case can be, and the letters hand-made levels spell them with.</summary>
    // The board paints from PieceModelLibrary.palette; this is the fallback when a library has
    // none, and the order both follow. The order is not arbitrary: each colour also picks the
    // suitcase model at its own index, and the sixth reuses the first model -- so the sixth
    // colour is the one furthest from the first, pink against blue, rather than two warm
    // colours on one shape.
    public static class PieceColours
    {
        public const int Count = 6;

        public const string Letters = "BRGYPK";

        public static readonly Color[] Default =
        {
            new Color32(0x2F, 0x80, 0xED, 255),   // B blue
            new Color32(0xEB, 0x3B, 0x3B, 255),   // R red
            new Color32(0x3C, 0xC4, 0x5A, 255),   // G green
            new Color32(0xFF, 0xC6, 0x1A, 255),   // Y yellow
            new Color32(0x9B, 0x51, 0xE0, 255),   // P purple
            new Color32(0xFF, 0x6F, 0xC8, 255),   // K pink
        };

        /// <summary>Index for a letter, either case. -1 if it names no colour.</summary>
        public static int FromLetter(char letter) => Letters.IndexOf(char.ToUpperInvariant(letter));
    }
}
