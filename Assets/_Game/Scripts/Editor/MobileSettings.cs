using System.Text;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // The device-shape settings that live in Player Settings rather than in code. Separate
    // from the release-hygiene pass (names, bundle id, icons) on purpose: these change how the
    // game is laid out on a phone, and getting them wrong is not visible in the editor, where
    // the Game view is whatever aspect it was last left at.
    public static class MobileSettings
    {
        [MenuItem("SheepGame/Apply Mobile Player Settings")]
        public static void Apply()
        {
            var log = new StringBuilder();

            // ---- orientation ----------------------------------------------------------------

            // Portrait only. The board is a 7x10 field framed for a portrait phone and the HUD
            // is anchored top and bottom; in landscape the framer pulls back far enough that
            // the cases stop being tappable. The project shipped with all four orientations
            // allowed, which is Unity's default and was never a decision.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            // Upside-down is not offered on any notched iPhone anyway, and on the ones where it
            // is, it puts the home indicator across the booster row.
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            log.AppendLine("orientation: portrait only");

            // ---- what the system draws over us ----------------------------------------------

            // The status bar would otherwise sit over the top of the frame, next to the
            // Dynamic Island, on top of the level card.
            PlayerSettings.statusBarHidden = true;

            // The home indicator stays visible. Hiding it does not give the game the strip
            // back — the swipe still works — it only removes the one hint the player has that
            // the gesture is there, and Apple's guidance is against it outside of full-screen
            // media. The HUD clears the strip through SafeAreaFitter instead.
            PlayerSettings.iOS.hideHomeButton = false;

            // System gestures are not deferred. Deferring would mean a swipe up from the
            // bottom needs a second attempt to leave the game, which review notices and
            // players resent; the game is tap-only, so nothing it wants collides with them.
            // Board answers taps on release, which is what stops those swipes spending one.
            PlayerSettings.iOS.deferSystemGesturesMode = UnityEngine.iOS.SystemGestureDeferMode.None;
            log.AppendLine("status bar hidden, home indicator shown, system gestures not deferred");

            // ---- being interrupted ----------------------------------------------------------

            // Suspend on background. Nothing here needs to keep running, and a puzzle that
            // carries on ticking in the player's pocket is a battery complaint waiting to be
            // written. AppLifecycle puts the pause panel up; this makes the suspension real.
            PlayerSettings.iOS.appInBackgroundBehavior = iOSAppInBackgroundBehavior.Suspend;

            // One more frame is drawn as the app goes away, so the picture iOS keeps for the
            // app switcher is the pause panel rather than whatever the board looked like
            // halfway through a case leaving.
            bool extraFrame = SetProjectSetting("iOSRenderExtraFrameOnPause", true);
            log.AppendLine("background: suspend" + (extraFrame
                ? ", extra frame drawn for the app-switcher snapshot"
                : " (could not set the extra pause frame — check it by hand in Player Settings)"));

            // The player's own music keeps playing. This game has no soundtrack to defend, and
            // taking somebody's podcast away to play a puzzle in silence is the fastest way to
            // be closed. Turn it on only if music ships and has to be heard.
            PlayerSettings.muteOtherAudioSources = false;
            log.AppendLine("other apps' audio: left playing");

            // ---- launch ----------------------------------------------------------------------

            // Unity's default spinner over the launch image reads as the game having hung.
            // The boot scene is what says the game is loading.
            PlayerSettings.iOS.showActivityIndicatorOnLoading = iOSShowActivityIndicatorOnLoading.DontShow;
            PlayerSettings.Android.showActivityIndicatorOnLoading = AndroidShowActivityIndicatorOnLoading.DontShow;

            AssetDatabase.SaveAssets();

            Debug.Log("[Mobile] Player settings applied.\n" + log +
                      "Frame rate, sleep timeout, Low Power Mode and the memory warning are " +
                      "set in code, by AppSetup and DeviceRuntime.");
        }

        // Some of what a phone build needs has no scripting API. Reached through the settings
        // asset instead of left to be remembered, because a checkbox nobody ticks is the same
        // as a checkbox that does not exist.
        static bool SetProjectSetting(string property, bool value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0 || assets[0] == null) return false;

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty(property);
            if (prop == null) return false;

            // Unity has serialised these as both across versions, so neither is assumed.
            if (prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = value;
            else if (prop.propertyType == SerializedPropertyType.Integer) prop.intValue = value ? 1 : 0;
            else return false;

            so.ApplyModifiedProperties();
            return true;
        }
    }
}
