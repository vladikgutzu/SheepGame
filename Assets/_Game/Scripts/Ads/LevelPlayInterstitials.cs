using System;
using UnityEngine;
#if SHEEP_LEVELPLAY
using Unity.Services.LevelPlay;
#endif

namespace SheepGame.Ads
{
    /// <summary>
    /// The one place the game talks to the LevelPlay SDK: start it, keep an interstitial
    /// loaded, and show it when asked.
    /// </summary>
    // In an assembly of its own so the game still compiles when the package is absent -- a
    // fresh clone before the package manager has resolved, or a build with ads stripped. The
    // SHEEP_LEVELPLAY define comes from this assembly's version defines and is only set while
    // com.unity.services.levelplay is installed; without it every call is a no-op that
    // reports no ad ready, and the game simply carries on to the next level.
    public static class LevelPlayInterstitials
    {
        /// <summary>Whether the SDK is compiled in at all.</summary>
#if SHEEP_LEVELPLAY
        public const bool Compiled = true;
#else
        public const bool Compiled = false;
#endif

        public static bool Initialized { get; private set; }

        /// <summary>An ad went on screen. Sound and anything timed should stop.</summary>
        public static event Action Opened;
        /// <summary>The ad is gone, closed or failed. The game can go on.</summary>
        public static event Action Closed;
        /// <summary>
        /// Every step of an interstitial's life, by the names analytics uses: Request, Loaded,
        /// Show, Clicked, FailedShow. Raised here and reported elsewhere, so this assembly
        /// knows nothing about analytics.
        /// </summary>
        public static event Action<string> Lifecycle;

        static Action _onFinished;
        static float _retryAt = -1f;
        static int _failures;

#if SHEEP_LEVELPLAY
        static LevelPlayInterstitialAd _ad;
        static string _adUnitId;
#endif

        public static void Initialize(string appKey, string adUnitId, bool validateIntegration,
                                      bool launchTestSuite = false)
        {
#if SHEEP_LEVELPLAY
            if (Initialized || string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(adUnitId)) return;
            Initialized = true;
            _adUnitId = adUnitId;

            // This metadata has to be present before Init. It is used only by the dedicated
            // diagnostic APK and is never enabled in a normal/internal Play build.
            if (launchTestSuite) LevelPlay.SetMetaData("is_test_suite", "enable");

            LevelPlay.OnInitSuccess += _ =>
            {
                _ad = new LevelPlayInterstitialAd(_adUnitId);
                _ad.OnAdLoaded += __ => { _failures = 0; Lifecycle?.Invoke("Loaded"); };
                _ad.OnAdLoadFailed += error =>
                {
                    // Backed off, capped at a minute: a phone with no signal should not spend
                    // its battery asking for an ad every second.
                    _failures++;
                    _retryAt = Time.realtimeSinceStartup + Mathf.Min(60f, 5f * _failures);
                    Debug.Log($"[Ads] Interstitial did not load ({error}); retrying.");
                };
                _ad.OnAdDisplayed += __ => { Lifecycle?.Invoke("Show"); Opened?.Invoke(); };
                _ad.OnAdClicked += __ => Lifecycle?.Invoke("Clicked");
                _ad.OnAdClosed += __ => Finish();
                _ad.OnAdDisplayFailed += (__, error) =>
                {
                    Debug.LogWarning($"[Ads] Interstitial failed to show: {error}");
                    Lifecycle?.Invoke("FailedShow");
                    Finish();
                };
                Load();
                EnsureRunner();
                if (launchTestSuite)
                {
                    Debug.Log("[Ads] Launching the LevelPlay on-device test suite.");
                    LevelPlay.LaunchTestSuite();
                }
            };
            LevelPlay.OnInitFailed += error => Debug.LogWarning($"[Ads] LevelPlay did not start: {error}");

            // iOS only (Android logs a warning for it): holds the whole game still while an ad
            // is up and lets it go when the ad closes. Called once per session, as it asks.
            if (Application.platform == RuntimePlatform.IPhonePlayer) LevelPlay.SetPauseGame(true);

            // The LevelPlay app is configured as "Not directed". Keep the runtime signal in
            // sync with the dashboard, and send it before Init so every network sees it.
            LevelPlayPrivacySettings.SetCOPPA(false);

            if (validateIntegration) LevelPlay.ValidateIntegration();
            LevelPlay.Init(appKey);
#else
            if (!string.IsNullOrEmpty(appKey))
                Debug.Log("[Ads] LevelPlay is not installed; interstitials are off.");
#endif
        }

        public static bool IsReady
        {
            get
            {
#if SHEEP_LEVELPLAY
                return _ad != null && _ad.IsAdReady();
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Shows the loaded interstitial. Returns false, and never calls back, if there was
        /// none to show -- the caller carries on at once in that case.
        /// </summary>
        public static bool Show(string placement, Action onFinished)
        {
#if SHEEP_LEVELPLAY
            if (!IsReady) return false;
            _onFinished = onFinished;
            _ad.ShowAd(string.IsNullOrEmpty(placement) ? null : placement);
            return true;
#else
            return false;
#endif
        }

        /// <summary>Gives up waiting on an ad that never reported back.</summary>
        public static void Abandon() => Finish();

        static void Finish()
        {
            var callback = _onFinished;
            _onFinished = null;
            Closed?.Invoke();
            callback?.Invoke();

            // The next one starts loading as soon as this one is done with.
            Load();
        }

        static void Load()
        {
#if SHEEP_LEVELPLAY
            if (_ad == null) return;
            Lifecycle?.Invoke("Request");
            _ad.LoadAd();
#endif
        }

        // Retries a failed load on the main thread, which needs something that ticks.
        static void EnsureRunner()
        {
            if (UnityEngine.Object.FindFirstObjectByType<Runner>() != null) return;
            var go = new GameObject("~LevelPlayRunner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                if (_retryAt < 0f || Time.realtimeSinceStartup < _retryAt) return;
                _retryAt = -1f;
                Load();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Initialized = false;
            Opened = null;
            Closed = null;
            Lifecycle = null;
            _onFinished = null;
            _retryAt = -1f;
            _failures = 0;
#if SHEEP_LEVELPLAY
            _ad = null;
#endif
        }
    }
}
