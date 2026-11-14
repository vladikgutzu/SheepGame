using System;
using System.Runtime.InteropServices;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The phone's haptic motor. A tap that moves a case is answered in the hand as well as
    /// on the screen, which is most of what makes a touch control feel like a control.
    /// </summary>
    // Not Handheld.Vibrate. On iOS that call is the old AudioServices buzz: the whole phone
    // shakes for half a second, it cannot be tuned, and it is the sound a phone makes when
    // something has gone wrong. UIFeedbackGenerator is what every native app uses and what a
    // player's hand already recognises — a light knock for a case leaving, a harder one for a
    // case that cannot.
    public static class Haptics
    {
        public enum ImpactStyle { Light = 0, Medium = 1, Heavy = 2, Soft = 3, Rigid = 4 }
        public enum NoticeKind { Success = 0, Warning = 1, Error = 2 }

        // Two taps inside this window are one event as far as a hand is concerned, and firing
        // twice reads as a rattle rather than as two answers. A puzzle where pieces can leave
        // in a chain is exactly where that happens.
        const float MinimumGap = 0.04f;

        static float _lastFired;

        static bool Allowed
        {
            get
            {
                if (!GameState.HapticsOn) return false;
                if (Time.unscaledTime - _lastFired < MinimumGap) return false;
                _lastFired = Time.unscaledTime;
                return true;
            }
        }

        /// <summary>
        /// Warms the taptic engine so the next knock lands without delay. Called on the way
        /// down, before the tap it belongs to is answered on the way up.
        /// </summary>
        // The engine takes a moment to spin up from idle, and a haptic that arrives late is
        // worse than none: it stops belonging to the thing that caused it. Preparing at
        // touch-down is free — iOS lets it fall asleep again on its own a moment later.
        public static void Prepare()
        {
            if (!GameState.HapticsOn) return;
            Native.Prepare();
        }

        /// <summary>A knock. The everyday one, for a tap that did something.</summary>
        public static void Impact(ImpactStyle style = ImpactStyle.Light)
        {
            if (!Allowed) return;
            Native.Impact((int)style);
        }

        /// <summary>The three-part pattern iOS reserves for an outcome: won, warned, failed.</summary>
        public static void Notice(NoticeKind kind)
        {
            if (!Allowed) return;
            Native.Notice((int)kind);
        }

        /// <summary>The tick used for moving between choices, not for confirming one.</summary>
        public static void Selection()
        {
            if (!Allowed) return;
            Native.Selection();
        }

        // Each platform gets its own deliberately short response. iOS uses the native feedback
        // generators; Android uses a one-shot VibrationEffect rather than Handheld.Vibrate(),
        // whose long generic buzz is much too heavy for a frequently tapped puzzle piece.
        static class Native
        {
#if UNITY_IOS && !UNITY_EDITOR
            [DllImport("__Internal")] static extern void _SheepHapticsPrepare();
            [DllImport("__Internal")] static extern void _SheepHapticsImpact(int style);
            [DllImport("__Internal")] static extern void _SheepHapticsNotice(int kind);
            [DllImport("__Internal")] static extern void _SheepHapticsSelection();

            internal static void Prepare() => _SheepHapticsPrepare();
            internal static void Impact(int style) => _SheepHapticsImpact(style);
            internal static void Notice(int kind) => _SheepHapticsNotice(kind);
            internal static void Selection() => _SheepHapticsSelection();
#elif UNITY_ANDROID && !UNITY_EDITOR
            static AndroidJavaObject _vibrator;
            static AndroidJavaObject _decorView;
            static int _sdkInt = -1;
            static bool _unavailable;

            internal static void Prepare() { }

            internal static void Impact(int style)
            {
                // Let the vendor haptic engine answer first. Xiaomi/POCO devices in
                // particular can suppress very short raw vibrator pulses, while Android's
                // view feedback is mapped to the motor profile chosen by the manufacturer.
                int feedback = (ImpactStyle)style == ImpactStyle.Rigid ? 17 // REJECT
                             : (ImpactStyle)style == ImpactStyle.Heavy ? 16 // CONFIRM
                             : 1;                                         // VIRTUAL_KEY
                if (PerformFeedback(feedback)) return;

                // Noticeable fallback for devices/ROMs that do not implement view feedback.
                // Still short enough that repeated suitcase taps feel crisp rather than buzzy.
                switch ((ImpactStyle)style)
                {
                    case ImpactStyle.Soft:   Vibrate(22, 110); break;
                    case ImpactStyle.Medium: Vibrate(30, 175); break;
                    case ImpactStyle.Heavy:  Vibrate(42, 235); break;
                    case ImpactStyle.Rigid:  Vibrate(38, 220); break;
                    default:                 Vibrate(26, 150); break;
                }
            }

            internal static void Notice(int kind)
            {
                int feedback = (NoticeKind)kind == NoticeKind.Success ? 16 : 17;
                if (PerformFeedback(feedback)) return;
                switch ((NoticeKind)kind)
                {
                    case NoticeKind.Success: Vibrate(38, 185); break;
                    case NoticeKind.Warning: Vibrate(46, 210); break;
                    default:                 Vibrate(56, 245); break;
                }
            }

            internal static void Selection()
            {
                if (!PerformFeedback(4)) Vibrate(20, 100); // CLOCK_TICK
            }

            static bool PerformFeedback(int feedbackConstant)
            {
                if (_unavailable) return false;
                try
                {
                    var view = GetDecorView();
                    // FLAG_IGNORE_VIEW_SETTING: game haptics have their own in-game switch,
                    // so a non-clickable Unity surface must not silently swallow the effect.
                    return view != null && view.Call<bool>("performHapticFeedback", feedbackConstant, 1);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Haptics] Android view feedback failed; using vibrator fallback: " +
                                     exception.Message);
                    return false;
                }
            }

            static void Vibrate(int durationMs, int amplitude)
            {
                if (_unavailable) return;

                try
                {
                    var vibrator = GetVibrator();
                    if (vibrator == null) return;

                    if (SdkInt >= 26)
                    {
                        using (var vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (var effect = vibrationEffect.CallStatic<AndroidJavaObject>(
                                   "createOneShot", (long)durationMs, amplitude))
                        {
                            vibrator.Call("vibrate", effect);
                        }
                    }
                    else
                    {
                        vibrator.Call("vibrate", (long)durationMs);
                    }
                }
                catch (Exception exception)
                {
                    // Haptics are polish, never a reason to interrupt gameplay on an unusual
                    // Android device or vendor ROM. Disable only this session after one failure.
                    _unavailable = true;
                    Debug.LogWarning("[Haptics] Android vibration is unavailable: " + exception.Message);
                }
            }

            static AndroidJavaObject GetVibrator()
            {
                if (_vibrator != null) return _vibrator;

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null)
                    {
                        _unavailable = true;
                        return null;
                    }

                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }

                if (_vibrator == null || !_vibrator.Call<bool>("hasVibrator"))
                {
                    _unavailable = true;
                    _vibrator = null;
                }

                return _vibrator;
            }

            static AndroidJavaObject GetDecorView()
            {
                if (_decorView != null) return _decorView;
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var window = activity?.Call<AndroidJavaObject>("getWindow"))
                    _decorView = window?.Call<AndroidJavaObject>("getDecorView");
                return _decorView;
            }

            static int SdkInt
            {
                get
                {
                    if (_sdkInt >= 0) return _sdkInt;
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                        _sdkInt = version.GetStatic<int>("SDK_INT");
                    return _sdkInt;
                }
            }
#else
            internal static void Prepare() { }
            internal static void Impact(int style) { }
            internal static void Notice(int kind) { }
            internal static void Selection() { }
#endif
        }
    }
}
