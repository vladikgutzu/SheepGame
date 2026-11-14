using System;
using System.Collections;
using SheepGame.Ads;
using SheepGame.Core.Meta;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Decides whether finishing a level is followed by an interstitial, and holds the game
    /// while one is on screen.
    /// </summary>
    // The ad plays between the win panel and the next level, on the player's own press of Next,
    // rather than over the win itself: the reward is the thing the player is looking at, and
    // an ad that covers it is an ad that took something away.
    // Ads begin booting before ordinary scene scripts. LevelPlay does not permit LoadAd before
    // OnInitSuccess, so LevelPlayRewardedBoosters performs all three LoadAd calls immediately
    // inside that callback; this order makes Init itself happen at the first possible moment.
    [DefaultExecutionOrder(-10000)]
    public class AdsManager : MonoBehaviour
    {
        public AdsConfig config;

        [Tooltip("Longest the game waits for an ad to report that it closed before carrying " +
                 "on without it. An SDK that never calls back must not strand the player.")]
        public float giveUpAfterSeconds = 75f;

        float _lastShown = float.NegativeInfinity;
        float _waitingSince = -1f;
        bool _audioWasPaused;
        bool _initializationQueued;
        Action _then;

        public event Action<string> RewardedAvailabilityChanged;

        void Awake()
        {
            if (config == null) return;
            LevelPlayInterstitials.Lifecycle += ReportAd;
            LevelPlayRewardedBoosters.Lifecycle += ReportRewardedAd;
            LevelPlayRewardedBoosters.AvailabilityChanged += HandleRewardedAvailability;
            LevelPlayRewardedBoosters.Initialize(ItemIds.Hint, config.HintRewardedId,
                                                 ItemIds.Shuffle, config.ShuffleRewardedId,
                                                 ItemIds.Remove, config.RemoveRewardedId);
            LevelPlayPrivacy.ChoiceChanged += QueueInitialization;
            if (LevelPlayPrivacy.HasChoice)
            {
                LevelPlayPrivacy.ApplyStoredChoice();
                QueueInitialization();
            }
        }

        void OnDestroy()
        {
            LevelPlayInterstitials.Lifecycle -= ReportAd;
            LevelPlayRewardedBoosters.Lifecycle -= ReportRewardedAd;
            LevelPlayRewardedBoosters.AvailabilityChanged -= HandleRewardedAvailability;
            LevelPlayPrivacy.ChoiceChanged -= QueueInitialization;
        }

        void QueueInitialization()
        {
            if (_initializationQueued || LevelPlayInterstitials.Initialized || config == null) return;
            _initializationQueued = true;
            StartCoroutine(InitializeNextFrame());
        }

        IEnumerator InitializeNextFrame()
        {
            yield return null;
            _initializationQueued = false;
            LevelPlayInterstitials.Initialize(config.AppKey, config.InterstitialId,
                                              config.validateIntegration && Debug.isDebugBuild,
                                              config.launchTestSuite && Debug.isDebugBuild);
        }

        // The one placement this game shows interstitials in; a load has no placement of its
        // own, so it is reported under the same one it will be shown in.
        void ReportAd(string step)
        {
            // An empty placement is LevelPlay's own default, and GameAnalytics wants a name.
            string placement = string.IsNullOrEmpty(config.placement) ? "DefaultInterstitial" : config.placement;
            if (Enum.TryParse(step, out SheepGame.Analytics.AdStep adStep))
                Telemetry.Ad(SheepGame.Analytics.AdKind.Interstitial, adStep, placement);
        }

        void ReportRewardedAd(string itemId, string step)
        {
            if (!Enum.TryParse(step, out SheepGame.Analytics.AdStep adStep)) return;
            Telemetry.Ad(SheepGame.Analytics.AdKind.RewardedVideo, adStep, RewardedPlacement(itemId));
        }

        void HandleRewardedAvailability(string itemId) => RewardedAvailabilityChanged?.Invoke(itemId);

        public bool IsRewardedConfigured(string itemId) => LevelPlayRewardedBoosters.IsConfigured(itemId);
        public bool IsRewardedReady(string itemId) => LevelPlayRewardedBoosters.IsReady(itemId);
        public void RequestRewarded(string itemId) => LevelPlayRewardedBoosters.RequestLoad(itemId);

        public bool ShowRewardedBooster(string itemId, Action onRewarded, Action onClosed)
        {
            return LevelPlayRewardedBoosters.Show(itemId, () =>
            {
                GameState.GrantItem(itemId, 1);
                Telemetry.BoosterRewarded(itemId);
                onRewarded?.Invoke();
            }, onClosed);
        }

        static string RewardedPlacement(string itemId)
        {
            if (itemId == ItemIds.Hint) return "BaggageJamBooster1";
            if (itemId == ItemIds.Shuffle) return "BaggageJamBooster2";
            if (itemId == ItemIds.Remove) return "BaggageJamBooster3";
            return "BaggageJamBooster";
        }

        /// <summary>Whether completing this level is one that an interstitial follows.</summary>
        public bool ShouldShowAfter(int completedLevel)
        {
            if (config == null) return false;
            if (completedLevel < config.firstInterstitialLevel) return false;
            if ((completedLevel - config.firstInterstitialLevel) % Mathf.Max(1, config.everyLevels) != 0) return false;
            if (!string.IsNullOrEmpty(config.noAdsEntitlement) && GameState.HasEntitlement(config.noAdsEntitlement)) return false;
            if (config.minSecondsBetween > 0f && Time.realtimeSinceStartup - _lastShown < config.minSecondsBetween) return false;
            return true;
        }

        /// <summary>
        /// Plays an interstitial if this level earns one and one is loaded, then runs
        /// <paramref name="then"/>. With no ad to play, runs it straight away.
        /// </summary>
        public void AfterLevel(int completedLevel, Action then)
        {
            if (_waitingSince >= 0f || !ShouldShowAfter(completedLevel))
            {
                then?.Invoke();
                return;
            }

            _audioWasPaused = AudioListener.pause;
            AudioListener.pause = true;
            _waitingSince = Time.realtimeSinceStartup;
            _then = then;

            bool shown = LevelPlayInterstitials.Show(config.placement, Done);
            if (!shown) Done();
            else _lastShown = Time.realtimeSinceStartup;
        }

        void Done()
        {
            if (_waitingSince < 0f) return;
            _waitingSince = -1f;
            AudioListener.pause = _audioWasPaused;

            var then = _then;
            _then = null;
            then?.Invoke();
        }

        void Update()
        {
            if (_waitingSince < 0f) return;
            if (Time.realtimeSinceStartup - _waitingSince < giveUpAfterSeconds) return;

            Debug.LogWarning("[Ads] The interstitial never reported closing; carrying on without it.");
            LevelPlayInterstitials.Abandon();
            Done();
        }
    }
}
