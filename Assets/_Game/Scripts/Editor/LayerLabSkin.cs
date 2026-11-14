using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // The Layer Lab "GUI Pro - Casual Game" pack as the HUD's skin. GameUIBuilder still decides
    // what goes where; this decides what each piece is drawn with. Every lookup is by file name
    // inside the pack, so a pack that has moved or been removed shows up as a warning and a
    // fallback to the generated shapes rather than as a scene full of missing sprites.
    public static class LayerLabSkin
    {
        const string Root = "Assets/Layer Lab/GUI Pro-CasualGame";
        const string Sprites = Root + "/ResourcesData/Sprites/Components";
        const string FontPath = Root + "/ResourcesData/Fonts/LilitaOne-Regular Outline 54 SDF.asset";

        public static bool Available => AssetDatabase.IsValidFolder(Sprites);

        // The pack's own palette: navy panels, and text that is white with the outline baked
        // into the font's material.
        public static readonly Color Navy = Hex("1B3B6F");
        public static readonly Color NavyDeep = Hex("0F2347");
        public static readonly Color TextMuted = Hex("9EC3F0");

        public static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>A sprite from the pack by its file name, without extension.</summary>
        public static Sprite Get(string folder, string name)
        {
            string key = folder + "/" + name;
            if (Cache.TryGetValue(key, out var hit) && hit != null) return hit;

            Sprite found = null;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Sprite", new[] { Sprites + "/" + folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                found = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (found != null) break;
            }

            if (found == null) Debug.LogWarning($"LayerLabSkin: no sprite '{name}' in {folder}.");
            Cache[key] = found;
            return found;
        }

        static Sprite Picto(string name) => Get("Icon_PictoIcons/128", "Pictoicon_" + name);

        // ---- icons ------------------------------------------------------------------

        public static Sprite IconPause => Picto("Control_Pause");
        public static Sprite IconPlay => Picto("Control_Play");
        public static Sprite IconRestart => Picto("Refresh");
        public static Sprite IconCheck => Picto("Check");
        public static Sprite IconPlus => Picto("Plus");
        public static Sprite IconClose => Picto("Close");
        public static Sprite IconClock => Picto("Timer");
        public static Sprite IconBulb => Picto("Bulb");
        public static Sprite IconHammer => Picto("Hammer");
        public static Sprite IconShuffle => Picto("Arrows");
        public static Sprite IconChevron => Picto("Arrow_Next");
        public static Sprite IconShop => Picto("Shop_0");
        public static Sprite Coin => Get("UI_Etc", "ResourceBar_Icon_Coin");
        public static Sprite TutorialHand => Get("IconMisc", "Icon_ImageIcon_Tutorial_Hand");

        // ---- shapes -----------------------------------------------------------------

        public static Sprite PopupNavy => Get("Popup", "Popup01_Single_Navy");
        public static Sprite ChipFrame => Get("Frame", "BasicFrame_Round20");
        public static Sprite CircleNavy => Get("Button", "Button_Circle147_Navy");
        public static Sprite CircleWhite => Get("Button", "Button_Circle147_White");
        public static Sprite HexBlue => Get("Button", "Button_Hexagon199_Blue");
        public static Sprite HexRed => Get("Button", "Button_Hexagon199_Red");
        public static Sprite TitleRibbon => Get("Label", "Title_Ribbon_Bg_Orange");
        public static Sprite ResourceBar => Get("UI_Etc", "ResourceBar_Bg");
        public static Sprite SliderTrack => Get("Slider", "Slider_Basic01_Bg");
        public static Sprite SliderFill => Get("Slider", "Slider_Basic01_Fill_Blue");

        /// <summary>The pack's chunky button in the nearest of its colours.</summary>
        public static Sprite Button(Color colour) => Get("Button", "Button01_175_" + ButtonColour(colour));

        static string ButtonColour(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.25f) return v > 0.8f ? "White" : v > 0.45f ? "BlueGray" : "Gray";

            float deg = h * 360f;
            if (deg < 18f || deg >= 330f) return "Red";
            if (deg < 40f) return "Orange";
            if (deg < 70f) return "Yellow";
            if (deg < 165f) return "Green";
            if (deg < 205f) return "Sky";
            if (deg < 245f) return "Blue";
            return "Purple";
        }

        public static bool IsPale(Color c)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            return s < 0.2f && v > 0.8f;
        }

        public static bool IsDark(Color c)
        {
            Color.RGBToHSV(c, out _, out _, out float v);
            return v < 0.35f;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
