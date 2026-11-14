using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Floor tiles and painted markings, drawn in code.
    //
    // The pack has no floor at all, and a flat colour is what made the hall read as a
    // backdrop rather than a place. Generated rather than authored so the tile pitch can
    // follow the board's cell size, and so nothing here depends on a texture pack.
    public static class TerminalTextures
    {
        const string Dir = "Assets/_Game/Textures";

        public static Texture2D Tiles(int size = 256)
        {
            return Build("Terminal_Tiles", size, (x, y, u, v) =>
            {
                // Two large tiles across the texture, so the grout falls on quarters and the
                // pattern still reads when the material tiles it every couple of units.
                float gx = Mathf.Min(Frac(u * 2f), 1f - Frac(u * 2f));
                float gy = Mathf.Min(Frac(v * 2f), 1f - Frac(v * 2f));
                float grout = Mathf.Min(gx, gy);

                var tile = new Color(0.94f, 0.95f, 0.96f);          // near white, faintly cool
                var line = new Color(0.80f, 0.86f, 0.89f);          // light, never a dark seam

                // A whisper of variation per tile so a big floor is not one flat field.
                int tx = Mathf.FloorToInt(u * 2f), ty = Mathf.FloorToInt(v * 2f);
                float shade = 1f - 0.02f * Mathf.Abs(Mathf.Sin((tx * 13 + ty * 7) * 1.7f));

                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.004f, 0.016f, grout));
                return Color.Lerp(line, tile * shade, edge);
            });
        }

        // The belt's tread: slats across the band, with a rail down each edge. V runs along
        // the ring, so this is what scrolls.
        public static Texture2D Tread(int size = 256)
        {
            return Build("Belt_Tread", size, (x, y, u, v) =>
            {
                var slat = new Color(0.16f, 0.62f, 0.66f);
                var gap = new Color(0.11f, 0.48f, 0.53f);
                var rail = new Color(0.62f, 0.88f, 0.92f);

                if (u < 0.07f || u > 0.93f) return rail;              // side rails
                float t = Frac(v * 4f);                               // four slats per repeat
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.14f, Mathf.Min(t, 1f - t)));
                return Color.Lerp(gap, slat, edge);
            });
        }

        // A departures board: rows of blocks that slide sideways when the texture is
        // scrolled. Rows run across the texture's U and the blocks vary along V, because V
        // is the axis that runs along a sign's length — so scrolling it reads as a ticker
        // rather than as a board sliding off its own frame.
        public static Texture2D Ticker(int size = 256)
        {
            var background = new Color(0.88f, 0.95f, 0.98f);
            var ink = new Color(0.15f, 0.55f, 0.62f);
            var amber = new Color(1f, 0.76f, 0.20f);
            var coral = new Color(1f, 0.45f, 0.38f);

            return Build("Terminal_Ticker", size, (x, y, u, v) =>
            {
                const int rows = 4;
                int row = Mathf.Clamp((int)(u * rows), 0, rows - 1);
                float withinRow = Frac(u * rows);
                if (withinRow < 0.22f || withinRow > 0.82f) return background;   // gaps between rows

                // Blocks of a few widths along the row, from a hash so they do not repeat
                // in an obvious rhythm. The last column of each row is a status pip.
                int cell = (int)(v * 26f);
                int hash = (cell * 73 + row * 131) % 97;
                if (hash % 5 == 0) return background;                            // word gaps

                var colour = hash % 17 == 0 ? amber : hash % 23 == 0 ? coral : ink;
                float within = Frac(v * 26f);
                return within < 0.12f ? background : colour;                     // letter spacing
            });
        }

        // A chevron, pointing up the texture, on a transparent ground.
        public static Texture2D Arrow(int size = 256)
        {
            return Build("Marking_Arrow", size, (x, y, u, v) =>
            {
                var paint = new Color(1f, 0.78f, 0.10f);            // signal yellow
                float a = Chevron(u, v, 0.52f) + Chevron(u, v, 0.16f);
                return new Color(paint.r, paint.g, paint.b, Mathf.Clamp01(a));
            });
        }

        // A dashed walking lane, running up the texture.
        public static Texture2D Lane(int size = 256)
        {
            return Build("Marking_Lane", size, (x, y, u, v) =>
            {
                var paint = new Color(0.42f, 0.85f, 0.88f);         // turquoise, to the palette
                bool inStripe = Mathf.Abs(u - 0.5f) < 0.16f;
                bool inDash = Frac(v * 4f) < 0.62f;
                float a = inStripe && inDash ? 1f : 0f;
                return new Color(paint.r, paint.g, paint.b, a);
            });
        }

        // A four-point star for the tap burst, and a soft dot for the landing puff. Both
        // are white with the shape in the alpha, so one additive material tints them.
        // Round, and soft all the way to nothing at the rim.
        //
        // The first pair drew their shapes with a threshold — `distance < k ? 1 : 0` — which
        // has no gradient at all, so at a few dozen pixels on screen the sprite is simply
        // its quad: hard-edged yellow rectangles rather than sparks. Anything meant to read
        // as a spark or a puff has to fade out, and fade out before it reaches the edge of
        // the texture, or bilinear filtering clips it square again.
        public static Texture2D Spark(int size = 128)
        {
            return Build("Fx_Spark", size, (x, y, u, v) =>
            {
                float du = (u - 0.5f) * 2f, dv = (v - 0.5f) * 2f;
                float r = Mathf.Sqrt(du * du + dv * dv);

                // A broad solid core with a soft halo round it. The core has to be most of
                // the sprite, not a pinprick in the middle of one: what carries the colour
                // is the opaque part, and at the game's distance a tight core leaves a
                // particle that is technically there and practically invisible.
                float core = Mathf.Pow(Mathf.Clamp01(1f - r / 0.58f), 1.6f);
                float halo = Mathf.Pow(Mathf.Clamp01(1f - r / 1.0f), 2.5f);
                return new Color(1f, 1f, 1f, Mathf.Clamp01(core + halo * 0.6f));
            });
        }

        public static Texture2D Puff(int size = 128)
        {
            return Build("Fx_Puff", size, (x, y, u, v) =>
            {
                float du = (u - 0.5f) * 2f, dv = (v - 0.5f) * 2f;
                float r = Mathf.Sqrt(du * du + dv * dv);
                float a = Mathf.Pow(Mathf.Clamp01(1f - r / 0.95f), 2.4f);
                return new Color(1f, 1f, 1f, a * 0.8f);
            });
        }

        static float Chevron(float u, float v, float centre)
        {
            // Two strokes meeting at the middle, thickness in texture space.
            float d = Mathf.Abs(u - 0.5f);
            float wanted = centre + d * 0.55f;
            return Mathf.Abs(v - wanted) < 0.075f && d < 0.36f ? 1f : 0f;
        }

        static float Frac(float f) => f - Mathf.Floor(f);

        static Texture2D Build(string name, int size, Func<int, int, float, float, Color> shade)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game", "Textures");
            string path = $"{Dir}/{name}.png";

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = shade(x, y, (x + 0.5f) / size, (y + 0.5f) / size);
            texture.SetPixels(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
