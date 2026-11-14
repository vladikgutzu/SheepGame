using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Every shape the HUD is drawn from is generated here rather than imported, so the UI
    // carries no dependency on a sprite pack and can be regenerated at any size. Shapes are
    // defined as point-inside tests and sampled 4x4 per pixel; that costs nothing at these
    // sizes and gives cleaner edges than drawing into a texture with lines.
    public static class UISpriteFactory
    {
        public const string Folder = "Assets/_Game/UI/Sprites";

        // One rounded sprite serves every card, chip and pill: the corner radius on screen
        // is set per Image through pixelsPerUnitMultiplier, so nothing needs a second asset
        // just because it wants tighter corners. See GameUIBuilder.SetCorner.
        public const int RoundRadius = 48;
        const int RoundBorder = 52;
        const int Size = 128;

        public static Sprite Round => Load("UI_Round", () => RoundedRect(Size, RoundRadius), RoundBorder);
        public static Sprite Circle => Load("UI_Circle", () => Disc(Size), 0);
        public static Sprite Ring => Load("UI_Ring", () => RingShape(Size, 0.96f, 0.80f), 0);
        public static Sprite IconShuffle => Load("Icon_Shuffle", ShuffleIcon, 0);
        public static Sprite IconHammer => Load("Icon_Hammer", HammerIcon, 0);
        public static Sprite IconRestart => Load("Icon_Restart", RestartIcon, 0);
        public static Sprite IconChevron => Load("Icon_Chevron", ChevronIcon, 0);
        public static Sprite IconCheck => Load("Icon_Check", CheckIcon, 0);
        public static Sprite IconPlus => Load("Icon_Plus", PlusIcon, 0);
        public static Sprite IconBulb => Load("Icon_Bulb", BulbIcon, 0);
        public static Sprite IconClose => Load("Icon_Close", CloseIcon, 0);
        public static Sprite IconPause => Load("Icon_Pause", PauseIcon, 0);
        public static Sprite IconClock => Load("Icon_Clock", ClockIcon, 0);

        // Each comes as a pair: the shape, and the same shape grown by the outline width. A
        // sticker's outline cannot be the fill sprite drawn larger -- a scaled handle is a
        // thicker handle, not an outlined one -- so the outline is drawn as a shape of its own.
        public static Sprite IconSuitcase => Load("Icon_Suitcase", () => SuitcaseShape(0f), 0);
        public static Sprite IconSuitcaseOutline => Load("Icon_SuitcaseOutline", () => SuitcaseShape(7f), 0);
        public static Sprite IconHand => Load("Icon_Hand", () => HandShape(0f), 0);
        public static Sprite IconHandOutline => Load("Icon_HandOutline", () => HandShape(7f), 0);

        /// <summary>Where the fingertip is on the hand sprites, as a pivot.</summary>
        public static readonly Vector2 HandFingertip = new Vector2((64f - 14f) / 128f, (64f + 56f) / 128f);

        /// <summary>A horizontal capsule in the loading screen's own colours. For progress fills.</summary>
        // Wide rather than square, and used unsliced: a 9-sliced sprite repeats its middle,
        // which would repeat the gradient along with it. The bar reveals this picture behind a
        // mask instead of stretching it, so the ramp means the same thing at every fill.
        public static Sprite ProgressPill => LoadColoured("UI_ProgressPill", ProgressPillShape, 512, 128);

        /// <summary>Deletes the generated sprites so the next access redraws them.</summary>
        public static void Clear()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        // The colour-capable twin of Load. The plain one writes white and varies only alpha,
        // which is all an icon needs; anything with colour of its own comes through here.
        static Sprite LoadColoured(string name, Func<Func<Vector2, Color>> shape, int width, int height)
        {
            string path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            EnsureFolder();
            var tex = RenderColoured(width, height, shape());
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // Same 4x4 sampling as Render, but the shape hands back a colour with the alpha in it.
        static Texture2D RenderColoured(int width, int height, Func<Vector2, Color> inside)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            const int sub = 4;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < sub; sy++)
                    {
                        for (int sx = 0; sx < sub; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / sub - width * 0.5f,
                                                y + (sy + 0.5f) / sub - height * 0.5f);
                            var c = inside(p);
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }

                    const float n = sub * sub;
                    // Colour averaged straight rather than weighted by coverage: every sample
                    // inside the shape carries the same colour anyway, and the ones outside
                    // contribute nothing but a zero alpha.
                    float alpha = a / n;
                    var colour = alpha > 0.0001f
                        ? new Color(r / a, g / a, b / a, alpha)
                        : new Color(0f, 0f, 0f, 0f);
                    pixels[y * width + x] = colour;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // Measured off the loading artwork itself, not chosen by eye: the three colours that
        // cover the most of it once the floor, the glass and the shadows are excluded. Teal is
        // the carousel, amber is the wordmark and the signage, coral is the suitcases. A bar
        // painted in them belongs to the picture it is drawn on instead of sitting on top of it.
        //
        // A full rainbow was tried first, off the plane pack's palette atlas, and it read as a
        // test card: sixteen hues nothing else in the scene shares.
        //
        // Ordered cool to warm so the bar warms up as it fills, and three stops rather than two
        // so the middle lands exactly on the amber -- which is the lightest of them, and the
        // centred percentage sits there.
        static readonly Color32[] Ramp =
        {
            new Color32(0x5D, 0xB3, 0xB2, 255),   // carousel teal
            new Color32(0xFD, 0xBF, 0x04, 255),   // signage amber
            new Color32(0xFD, 0x92, 0x7D, 255),   // suitcase coral
        };

        // Blended between the stops rather than stepped: hard bands would read as a ruler, and
        // what is wanted is one sweep of colour.
        static Func<Vector2, Color> ProgressPillShape()
        {
            const float w = 512f, h = 128f, r = h * 0.5f;

            return p =>
            {
                bool inside = InCapsule(p, new Vector2(-w * 0.5f + r, 0f), new Vector2(w * 0.5f - r, 0f), r);
                if (!inside) return new Color(0f, 0f, 0f, 0f);

                float t = Mathf.Clamp01((p.x + w * 0.5f) / w) * (Ramp.Length - 1);
                int i = Mathf.Min(Mathf.FloorToInt(t), Ramp.Length - 2);
                Color mix = Color.Lerp(Ramp[i], Ramp[i + 1], t - i);
                mix.a = 1f;
                return mix;
            };
        }

        static Sprite Load(string name, Func<Func<Vector2, bool>> shape, int border)
        {
            string path = $"{Folder}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            EnsureFolder();
            var tex = Render(Size, shape());
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/UI"))
                AssetDatabase.CreateFolder("Assets/_Game", "UI");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Game/UI", "Sprites");
        }

        // ---- rasteriser -------------------------------------------------------------

        // Shapes are written in a centred space running -size/2..size/2, which keeps the
        // icon definitions readable and independent of the texture resolution.
        static Texture2D Render(int size, Func<Vector2, bool> inside)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            const int sub = 4;
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < sub; sy++)
                    {
                        for (int sx = 0; sx < sub; sx++)
                        {
                            var p = new Vector2(
                                x + (sx + 0.5f) / sub - half,
                                y + (sy + 0.5f) / sub - half);
                            if (inside(p)) hits++;
                        }
                    }

                    byte a = (byte)Mathf.RoundToInt(255f * hits / (sub * sub));
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ---- primitives -------------------------------------------------------------

        static bool InCapsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 ab = b - a, ap = p - a;
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-4f, Vector2.Dot(ab, ab)));
            return (ap - ab * t).sqrMagnitude <= r * r;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p - a, b - a), d2 = Cross(p - b, c - b), d3 = Cross(p - c, a - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool InRoundedBox(Vector2 p, Vector2 centre, Vector2 half, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(-rad), sin = Mathf.Sin(-rad);
            Vector2 d = p - centre;
            var local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            Vector2 q = new Vector2(Mathf.Abs(local.x), Mathf.Abs(local.y)) - (half - Vector2.one * radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) <= radius;
        }

        static bool InArc(Vector2 p, float outer, float inner, float fromDeg, float toDeg)
        {
            float d = p.magnitude;
            if (d > outer || d < inner) return false;
            float ang = Mathf.Repeat(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg, 360f);
            float from = Mathf.Repeat(fromDeg, 360f);
            float sweep = Mathf.Repeat(toDeg - fromDeg, 360f);
            return Mathf.Repeat(ang - from, 360f) <= sweep;
        }

        // Arrow head sitting on top of a stroke that ends at `tip` travelling in `dir`.
        static Func<Vector2, bool> Head(Vector2 tip, Vector2 dir, float length, float halfWidth)
        {
            dir = dir.normalized;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 baseP = tip - dir * length;
            Vector2 a = tip, b = baseP + side * halfWidth, c = baseP - side * halfWidth;
            return p => InTriangle(p, a, b, c);
        }

        static Func<Vector2, bool> Union(params Func<Vector2, bool>[] parts) =>
            p =>
            {
                foreach (var part in parts)
                    if (part(p)) return true;
                return false;
            };

        // ---- shapes -----------------------------------------------------------------

        static Func<Vector2, bool> RoundedRect(int size, int radius)
        {
            float half = size * 0.5f;
            return p => InRoundedBox(p, Vector2.zero, new Vector2(half, half), 0f, radius);
        }

        static Func<Vector2, bool> Disc(int size)
        {
            float r = size * 0.5f;
            return p => p.sqrMagnitude <= r * r;
        }

        static Func<Vector2, bool> RingShape(int size, float outerFrac, float innerFrac)
        {
            float r = size * 0.5f;
            float outer = r * outerFrac, inner = r * innerFrac;
            return p =>
            {
                float d = p.magnitude;
                return d <= outer && d >= inner;
            };
        }

        // A body with a carry handle on top. Chunky on purpose: it is read at 80 units across,
        // flying, and what has to survive that is "suitcase", not detail.
        static Func<Vector2, bool> SuitcaseShape(float grow)
        {
            var bodyCentre = new Vector2(0f, -12f);
            var bodyHalf = new Vector2(48f, 36f) + Vector2.one * grow;
            float bodyRadius = 13f + grow;
            var handleCentre = new Vector2(0f, 24f);
            float outer = 22f + grow, inner = Mathf.Max(0f, 12f - grow);
            return Union(
                p => InRoundedBox(p, bodyCentre, bodyHalf, 0f, bodyRadius),
                p => InArc(p - handleCentre, outer, inner, 0f, 180f),
                p => InRoundedBox(p, handleCentre + new Vector2(-15.5f, 0f), new Vector2(4.5f + grow, 8f + grow), 0f, 2f),
                p => InRoundedBox(p, handleCentre + new Vector2(15.5f, 0f), new Vector2(4.5f + grow, 8f + grow), 0f, 2f));
        }

        // A hand pointing up: index finger, palm, thumb. The fingertip is HandFingertip.
        static Func<Vector2, bool> HandShape(float grow)
        {
            return Union(
                p => InCapsule(p, new Vector2(-14f, 44f), new Vector2(-14f, 0f), 12f + grow),
                p => InRoundedBox(p, new Vector2(4f, -26f), new Vector2(30f, 28f) + Vector2.one * grow, 0f, 14f + grow),
                p => InCapsule(p, new Vector2(4f, -4f), new Vector2(4f, 2f), 10f + grow),
                p => InCapsule(p, new Vector2(20f, -6f), new Vector2(20f, -2f), 10f + grow),
                p => InCapsule(p, new Vector2(-30f, -24f), new Vector2(-44f, -2f), 10f + grow));
        }

        static Func<Vector2, bool> ShuffleIcon()
        {
            const float r = 8f;
            var a0 = new Vector2(-46f, -22f); var a1 = new Vector2(26f, 22f);
            var b0 = new Vector2(-46f, 22f); var b1 = new Vector2(26f, -22f);
            return Union(
                p => InCapsule(p, a0, a1, r),
                p => InCapsule(p, b0, b1, r),
                Head(new Vector2(48f, 35f), a1 - a0, 26f, 17f),
                Head(new Vector2(48f, -35f), b1 - b0, 26f, 17f));
        }

        static Func<Vector2, bool> HammerIcon()
        {
            const float headAngle = 32f;
            var headCentre = new Vector2(-12f, 28f);
            float rad = headAngle * Mathf.Deg2Rad;
            var down = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
            return Union(
                p => InRoundedBox(p, headCentre, new Vector2(36f, 13f), headAngle, 5f),
                p => InCapsule(p, headCentre + down * 6f, headCentre + down * 74f, 7.5f));
        }

        static Func<Vector2, bool> RestartIcon()
        {
            return Union(
                p => InArc(p, 44f, 30f, 20f, 340f),
                Head(new Vector2(37f, 34f), new Vector2(0.35f, 1f), 26f, 17f));
        }

        static Func<Vector2, bool> CheckIcon()
        {
            const float r = 10f;
            var corner = new Vector2(-8f, -26f);
            return Union(
                p => InCapsule(p, new Vector2(-38f, 4f), corner, r),
                p => InCapsule(p, corner, new Vector2(38f, 30f), r));
        }

        // Both are two capsules; the only difference is the angle. Drawn a touch shorter
        // than the other icons because they are used at half the size, inside a badge.
        static Func<Vector2, bool> PlusIcon()
        {
            const float r = 11f, arm = 30f;
            return Union(
                p => InCapsule(p, new Vector2(-arm, 0f), new Vector2(arm, 0f), r),
                p => InCapsule(p, new Vector2(0f, -arm), new Vector2(0f, arm), r));
        }

        // Glass, screw base, contact. Drawn as three stacked blocks rather than an outlined
        // bulb: at 96 units on a coloured disc a solid silhouette reads and an outline fills in.
        static Func<Vector2, bool> BulbIcon()
        {
            return Union(
                p => (p - new Vector2(0f, 14f)).sqrMagnitude <= 33f * 33f,
                p => InRoundedBox(p, new Vector2(0f, -24f), new Vector2(16f, 15f), 0f, 5f),
                p => InRoundedBox(p, new Vector2(0f, -45f), new Vector2(10f, 7f), 0f, 4f));
        }

        // Two bars, rounded like every other icon here. Deliberately the universal shape and
        // not a cog: the button stops the game, and a cog promises settings that are not there.
        static Func<Vector2, bool> PauseIcon()
        {
            const float gap = 16f, halfW = 9f, halfH = 30f;
            return Union(
                p => InRoundedBox(p, new Vector2(-gap, 0f), new Vector2(halfW, halfH), 0f, 7f),
                p => InRoundedBox(p, new Vector2(gap, 0f), new Vector2(halfW, halfH), 0f, 7f));
        }

        // A ring with two hands, at ten past ten -- the angle every watch advertisement uses,
        // because it frames the face instead of covering it and reads as a clock at any size.
        static Func<Vector2, bool> ClockIcon()
        {
            const float r = 6f, outer = 48f, inner = 38f;
            // Written out rather than through InArc: a full turn there is fromDeg 0 to toDeg
            // 360, and Mathf.Repeat(360, 360) is 0 -- the sweep collapses and the ring
            // disappears. A band between two radii has no such edge.
            return Union(
                p => p.sqrMagnitude <= outer * outer && p.sqrMagnitude >= inner * inner,
                p => InCapsule(p, Vector2.zero, new Vector2(0f, 26f), r),
                p => InCapsule(p, Vector2.zero, new Vector2(17f, 6f), r));
        }

        static Func<Vector2, bool> CloseIcon()
        {
            const float r = 10f, arm = 26f;
            return Union(
                p => InCapsule(p, new Vector2(-arm, -arm), new Vector2(arm, arm), r),
                p => InCapsule(p, new Vector2(-arm, arm), new Vector2(arm, -arm), r));
        }

        static Func<Vector2, bool> ChevronIcon()
        {
            const float r = 9f;
            var mid = new Vector2(14f, 0f);
            return Union(
                p => InCapsule(p, new Vector2(-16f, 30f), mid, r),
                p => InCapsule(p, new Vector2(-16f, -30f), mid, r));
        }
    }
}
