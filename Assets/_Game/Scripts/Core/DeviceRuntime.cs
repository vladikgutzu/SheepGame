using System;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// The device's own state, as opposed to the game's: refresh rate, Low Power Mode, memory
    /// pressure, the shape of the screen, and the moment right after the system hands control
    /// back. One place asks the phone these questions, so nothing else has to.
    /// </summary>
    // Split from AppLifecycle on purpose. AppLifecycle knows about panels and lives in a
    // scene; this knows nothing about the game and outlives every scene. On a phone the two
    // get confused constantly — "the shade came down" is a lifecycle event, "the shade came
    // down and the tap that dismissed it must not reach the board" is a device one.
    public static class DeviceRuntime
    {
        // 60 rather than the display's own rate. Every iPhone since the 13 Pro runs at 120,
        // and a tap puzzle gains nothing from it while paying for it in heat: a phone that has
        // thermally throttled drops frames unevenly, which reads far worse than a steady 60
        // ever did. Raise it only with a device in hand and the battery graph open.
        public const int DefaultPreferredFrameRate = 60;
        static int _preferredFrameRate = DefaultPreferredFrameRate;

        public static int PreferredFrameRate => _preferredFrameRate;

        // Low Power Mode is the player saying they care about battery more than anything on
        // screen. iOS caps the display at 60 there anyway; going to 30 on top of that is the
        // game agreeing with them rather than fighting the cap.
        public const int LowPowerFrameRate = 30;

        // Long enough to cover the frame the touch-up lands on and the settling after it,
        // short enough that a player who genuinely taps straight back in is not ignored.
        public const float ReentryGrace = 0.35f;

        static float _inputBlockedUntil;
        static bool _lowPower;
        static float _lowPowerChecked;
        static bool _keepAwake = true;

        static Rect _safeArea;
        static Vector2Int _screen;
        static ScreenOrientation _orientation;

        /// <summary>
        /// Raised when the shape of the screen changes — rotation, the safe area settling a
        /// frame or two after launch, or the resolution changing under a device simulator.
        /// </summary>
        // Polled rather than pushed because Unity has no callback for the safe area, and the
        // case that matters most is exactly the one with no event behind it: on a notched
        // iPhone the first frames report the full screen, so anything that read the safe area
        // once at startup has laid itself out underneath the Dynamic Island.
        public static event Action ViewportChanged;

        /// <summary>True while a tap must be ignored because the system just handed control back.</summary>
        // The shade, Control Centre and the app switcher are all dismissed with a touch that
        // ends over the game. Without this the release lands on the board and takes a case the
        // player never chose — and taps are the currency this game charges for.
        public static bool InputBlocked => Time.unscaledTime < _inputBlockedUntil;

        /// <summary>True when the player has the phone in Low Power Mode.</summary>
        public static bool LowPowerMode => _lowPower;

        /// <summary>
        /// Whether the screen is held awake. Off while the game is not being played, so a
        /// pause panel left up on a table does not hold the display on until the battery goes.
        /// </summary>
        public static bool KeepAwake
        {
            get => _keepAwake;
            set
            {
                if (_keepAwake == value) return;
                _keepAwake = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        /// <summary>Ignores taps for the next few moments. Called when focus comes back.</summary>
        public static void BlockInput(float seconds = ReentryGrace)
        {
            float until = Time.unscaledTime + Mathf.Max(0f, seconds);
            if (until > _inputBlockedUntil) _inputBlockedUntil = until;
        }

        /// <summary>Lets input through again. For a resume the player deliberately asked for.</summary>
        public static void UnblockInput() => _inputBlockedUntil = 0f;

        /// <summary>
        /// True when a screen point is inside the strips iOS reserves for its own gestures —
        /// the Dynamic Island's row and the home indicator's.
        /// </summary>
        // A swipe down from the top right is how Control Centre is opened, and a swipe up from
        // the bottom is how the app is left. Both begin as a touch on the game. Refusing the
        // ones that start inside the reserved strips costs a band of screen the HUD already
        // stays out of, and stops the gesture from spending a tap on its way past.
        public static bool InSystemGestureEdge(Vector2 screenPoint)
        {
            Rect safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) return false;

            // A screen with no insets at all — SE, iPad, the editor — has no reserved strips
            // worth speaking of, and clipping them there would only lose honest taps.
            if (safe.height >= Screen.height - 1f) return false;

            return screenPoint.y > safe.yMax || screenPoint.y < safe.yMin;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Install()
        {
            _safeArea = Screen.safeArea;
            _screen = new Vector2Int(Screen.width, Screen.height);
            _orientation = Screen.orientation;
            _inputBlockedUntil = 0f;

            Application.lowMemory -= OnLowMemory;
            Application.lowMemory += OnLowMemory;

            RefreshFrameRate(force: true);
            Screen.sleepTimeout = _keepAwake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            DeviceRunner.Ensure();
        }

        /// <summary>Re-reads Low Power Mode and the display rate, and sets the cap from them.</summary>
        // Re-read rather than set once: Low Power Mode switches itself on at 20% while the game
        // is open, and the player can switch it off from the shade without ever leaving.
        public static void RefreshFrameRate(bool force = false)
        {
            bool low = ReadLowPowerMode();
            if (!force && low == _lowPower && Application.targetFrameRate > 0) return;
            _lowPower = low;

            // vSync has to be off or targetFrameRate is ignored outright. Set here as well as
            // at boot because changing quality level puts it back.
            QualitySettings.vSyncCount = 0;

            int wanted = low ? LowPowerFrameRate : PreferredFrameRate;

            // Never above what the panel can actually show: asking for more than the display
            // rate only burns frames nobody sees.
            int display = DisplayRefreshRate();
            if (display > 0) wanted = Mathf.Min(wanted, display);

            Application.targetFrameRate = wanted;
        }

        /// <summary>Changes the normal frame cap selected by the adaptive device profile.</summary>
        public static void SetPreferredFrameRate(int frameRate)
        {
            _preferredFrameRate = Mathf.Clamp(frameRate, 30, 60);
            RefreshFrameRate(force: true);
        }

        static int DisplayRefreshRate()
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            // Unity reports 0 before the display is ready and 1 on some simulators; neither is
            // a cap worth honouring.
            return hz > 20.0 ? Mathf.RoundToInt((float)hz) : 0;
        }

        static bool ReadLowPowerMode()
        {
            // Guarded rather than called flat: off-device the property is a stub, and the
            // conditional keeps the editor from pretending to a state it cannot be in.
#if UNITY_IOS && !UNITY_EDITOR
            return UnityEngine.iOS.Device.lowPowerModeEnabled;
#else
            return false;
#endif
        }

        /// <summary>Polls the slow-moving device state. Called by the runner, not by the game.</summary>
        internal static void Tick()
        {
            if (Time.unscaledTime - _lowPowerChecked >= 3f)
            {
                _lowPowerChecked = Time.unscaledTime;
                RefreshFrameRate();
            }

            var size = new Vector2Int(Screen.width, Screen.height);
            Rect safe = Screen.safeArea;
            var orientation = Screen.orientation;
            if (size == _screen && safe == _safeArea && orientation == _orientation) return;

            _screen = size;
            _safeArea = safe;
            _orientation = orientation;
            if (size.x > 0 && size.y > 0) ViewportChanged?.Invoke();
        }

        internal static void OnFocusRegained()
        {
            BlockInput();
            // Low Power Mode is very often exactly what was changed while the game was away —
            // the shade is where the toggle lives.
            RefreshFrameRate(force: true);
        }

        static void OnLowMemory()
        {
            // iOS does not ask twice. The warning is the only notice before the app is killed,
            // so the save goes out first and the housekeeping second.
            GameState.FlushNow();
            AdaptiveDeviceQuality.HandleLowMemory();
            Resources.UnloadUnusedAssets();
            GC.Collect();
        }
    }

    /// <summary>Invisible object that gives DeviceRuntime a frame and the app callbacks.</summary>
    // Same shape as SaveRunner and for the same reason: the work is global, so putting it on a
    // scene object would tie it to a scene that is unloaded halfway through the game.
    [DefaultExecutionOrder(-200)]
    internal class DeviceRunner : MonoBehaviour
    {
        static DeviceRunner _instance;

        internal static void Ensure()
        {
            if (_instance != null || !Application.isPlaying) return;

            var go = new GameObject("~DeviceRunner") { hideFlags = HideFlags.HideAndDontSave };
            _instance = go.AddComponent<DeviceRunner>();
            DontDestroyOnLoad(go);
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        void Update() => DeviceRuntime.Tick();

        // Both, because iOS raises them for different things: backgrounding the app gives a
        // pause, pulling the shade over it only gives a focus loss.
        void OnApplicationPause(bool paused)
        {
            if (!paused) DeviceRuntime.OnFocusRegained();
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused) DeviceRuntime.OnFocusRegained();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
