using System.Linq;
using System.Text;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.EditorTools
{
    // The third question that has to be re-asked, alongside "is anything standing in the
    // playfield" and "what does the phone frame contain": will this survive an actual iPhone.
    //
    // None of what it checks is visible in the editor. The Game view has no notch, never loses
    // focus, never runs out of memory and never enters Low Power Mode, so every one of these
    // is a thing that is fine until it is on a device, at which point it is fine on the one
    // device the developer owns.
    public static class DeviceAudit
    {
        // Portrait point sizes and the safe-area insets iOS reports with the status bar
        // hidden. The insets are the reason the list is written out rather than derived: they
        // do not follow from the screen size, and the Dynamic Island's are larger than the
        // notch's on a phone that is barely bigger.
        struct Device
        {
            public string Name;
            public float Width, Height, Top, Bottom;
        }

        static readonly Device[] Devices =
        {
            new Device { Name = "iPhone SE (3rd gen)",  Width = 375, Height = 667, Top = 0,  Bottom = 0 },
            new Device { Name = "iPhone 13 mini",       Width = 375, Height = 812, Top = 50, Bottom = 34 },
            new Device { Name = "iPhone 14 / 13",       Width = 390, Height = 844, Top = 47, Bottom = 34 },
            new Device { Name = "iPhone 14 Plus",       Width = 428, Height = 926, Top = 47, Bottom = 34 },
            new Device { Name = "iPhone 16 (Island)",   Width = 393, Height = 852, Top = 59, Bottom = 34 },
            new Device { Name = "iPhone 16 Pro",        Width = 402, Height = 874, Top = 62, Bottom = 34 },
            new Device { Name = "iPhone 16 Pro Max",    Width = 440, Height = 956, Top = 62, Bottom = 34 },
            new Device { Name = "iPad 10.9",            Width = 820, Height = 1180, Top = 24, Bottom = 20 },
        };

        // Below this much vertical room, in canvas units, the level card at the top and the
        // booster row at the bottom are closing on the board rather than sitting either side
        // of it. Chosen from what the HUD actually occupies at 1080x1920, not from taste.
        const float TightHeight = 1450f;

        [MenuItem("SheepGame/Audit iPhone Fit")]
        public static void Audit()
        {
            var sb = new StringBuilder();
            void P(string t) => sb.AppendLine("PROBLEM  " + t);
            void N(string t) => sb.AppendLine("note     " + t);

            // ---- player settings --------------------------------------------------------------

            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
                P("default orientation is not Portrait — the framing and the HUD are portrait only");
            if (PlayerSettings.allowedAutorotateToLandscapeLeft ||
                PlayerSettings.allowedAutorotateToLandscapeRight ||
                PlayerSettings.allowedAutorotateToPortraitUpsideDown)
                P("an orientation other than Portrait is allowed — run SheepGame > Apply Mobile Player Settings");
            if (!PlayerSettings.statusBarHidden)
                P("the status bar is shown — it lands on the level card, beside the Dynamic Island");
            if (PlayerSettings.iOS.hideHomeButton)
                N("the home indicator is hidden; the swipe still works, the player just cannot see it");
            if (PlayerSettings.iOS.deferSystemGesturesMode != UnityEngine.iOS.SystemGestureDeferMode.None)
                N("system gestures are deferred — leaving the app takes two swipes, which review notices");
            if (PlayerSettings.iOS.appInBackgroundBehavior != iOSAppInBackgroundBehavior.Suspend)
                P("the app does not suspend in the background — it will keep drawing battery in a pocket");
            if (PlayerSettings.muteOtherAudioSources)
                N("other apps' audio is muted; this game has no soundtrack to put in its place");

            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings != null && settings.Length > 0 && settings[0] != null)
            {
                var prop = new SerializedObject(settings[0]).FindProperty("iOSRenderExtraFrameOnPause");
                if (prop != null && prop.propertyType == SerializedPropertyType.Boolean && !prop.boolValue)
                    N("no extra frame is drawn on pause — the app-switcher thumbnail will show the live board");
                else if (prop != null && prop.propertyType == SerializedPropertyType.Integer && prop.intValue == 0)
                    N("no extra frame is drawn on pause — the app-switcher thumbnail will show the live board");
            }

            // ---- the scene as built -----------------------------------------------------------

            var lifecycle = Object.FindFirstObjectByType<AppLifecycle>(FindObjectsInactive.Include);
            if (lifecycle == null)
                P("no AppLifecycle in the scene — pulling the shade down will not pause the game");
            else
            {
                if (lifecycle.pausePanel == null)
                    P("AppLifecycle.pausePanel is empty — an interruption saves but never pauses");
                if (lifecycle.purchaser == null)
                    N("AppLifecycle.purchaser is empty — a purchase sheet will be treated as an interruption " +
                      "and put a pause panel over the payment");
                if (lifecycle.gameManager == null)
                    N("AppLifecycle.gameManager is empty — the win and lose panels can be pauseable over");
            }

            var framer = Object.FindFirstObjectByType<CameraFramer>(FindObjectsInactive.Include);
            if (framer == null)
                N("no CameraFramer — the board is framed by hand and will not answer to the safe area");
            else if (!framer.respectSafeArea)
                P("CameraFramer.respectSafeArea is off — cases can be drawn under the Dynamic Island");

            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None))
            {
                if (canvas.rootCanvas != canvas) continue;

                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler == null)
                {
                    P($"'{canvas.name}' has no CanvasScaler — its UI is in raw pixels and will be " +
                      "half the size it should be on a phone");
                    continue;
                }

                if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    P($"'{canvas.name}' does not scale with screen size");
                if (canvas.GetComponent<CanvasScalerFitter>() == null)
                    P($"'{canvas.name}' has no CanvasScalerFitter — run SheepGame > Fit Canvases To Device");

                // The overlays each carry their own; a canvas with none has its whole HUD
                // running to the glass.
                if (canvas.GetComponentsInChildren<SafeAreaFitter>(true).Length == 0)
                    P($"'{canvas.name}' has no SafeAreaFitter anywhere under it");
            }

            var loose = Object.FindObjectsByType<SafeAreaFitter>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None);
            if (loose.Length == 0) P("no SafeAreaFitter in the scene at all");
            else N($"{loose.Length} safe-area fitters: " +
                   string.Join(", ", loose.Select(f => Path(f.transform)).Take(12)));

            // ---- the shape of every phone it has to survive -------------------------------------

            var reference = FindReference();
            N($"reference resolution {reference.x:0}x{reference.y:0}; rows below are canvas units, " +
              "after the safe area and the fitters' own padding");

            float topPad = loose.Length > 0 ? loose[0].topPadding : 0f;
            float bottomPad = loose.Length > 0 ? loose[0].bottomPadding : 0f;

            foreach (var d in Devices)
            {
                float aspect = d.Width / d.Height;
                float match = Mathf.Clamp01((aspect - 0.5625f) / (0.75f - 0.5625f));
                float scale = ScaleFactor(d.Width, d.Height, reference, match);

                float usable = (d.Height - d.Top - d.Bottom) / scale - topPad - bottomPad;
                float width = d.Width / scale;

                string row = $"{d.Name,-22} {width,6:0} x {usable,6:0}   match {match:0.00}";
                if (usable < TightHeight) P(row + "   <- tight; the HUD will crowd the board here");
                else N(row);
            }

            Debug.Log("[iPhone fit]\n" + sb);
        }

        // Additive and nothing else: the HUD is hand-tuned, and rebuilding it to add one
        // component would throw that away.
        [MenuItem("SheepGame/Fit Canvases To Device")]
        public static void FitCanvases()
        {
            int added = 0;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None))
            {
                if (canvas.rootCanvas != canvas) continue;
                if (canvas.GetComponent<CanvasScaler>() == null) continue;
                if (canvas.GetComponent<CanvasScalerFitter>() != null) continue;

                Undo.AddComponent<CanvasScalerFitter>(canvas.gameObject);
                added++;
            }

            Debug.Log(added == 0
                ? "[iPhone fit] Every canvas already has a CanvasScalerFitter."
                : $"[iPhone fit] Added CanvasScalerFitter to {added} canvas(es).");
        }

        // Unity's own formula, so the figures in the report are the ones the game will use.
        static float ScaleFactor(float w, float h, Vector2 reference, float match)
        {
            float logWidth = Mathf.Log(w / reference.x, 2f);
            float logHeight = Mathf.Log(h / reference.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, match));
        }

        static Vector2 FindReference()
        {
            foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include,
                                                                          FindObjectsSortMode.None))
                if (scaler.referenceResolution.x > 0f && scaler.referenceResolution.y > 0f)
                    return scaler.referenceResolution;
            return new Vector2(1080f, 1920f);
        }

        static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
