using System;
using System.Collections.Generic;
using UnityEngine;
#if SHEEP_LEVELPLAY
using Unity.Services.LevelPlay;
#endif

namespace SheepGame.Ads
{
    /// <summary>
    /// Owns one rewarded ad object per booster. The game only receives a reward from the
    /// SDK's reward callback; closing an ad, even normally, never grants inventory.
    /// </summary>
    public static class LevelPlayRewardedBoosters
    {
        public static event Action<string, string> Lifecycle;
        public static event Action<string> AvailabilityChanged;

        static bool _registered;
        static bool _sdkReady;

#if SHEEP_LEVELPLAY
        static readonly Dictionary<string, RewardSlot> Slots = new Dictionary<string, RewardSlot>();
        static readonly Dictionary<string, string> UnitIds = new Dictionary<string, string>();
#endif

        public static void Initialize(string hintKey, string hintId, string shuffleKey, string shuffleId,
                                      string removeKey, string removeId)
        {
#if SHEEP_LEVELPLAY
            if (_registered) return;
            _registered = true;
            Register(hintKey, hintId);
            Register(shuffleKey, shuffleId);
            Register(removeKey, removeId);
            LevelPlay.OnInitSuccess += OnSdkReady;
            LevelPlay.OnInitFailed += OnSdkFailed;
#endif
        }

        public static bool IsConfigured(string key)
        {
#if SHEEP_LEVELPLAY
            return !string.IsNullOrEmpty(key) && UnitIds.ContainsKey(key);
#else
            return false;
#endif
        }

        public static bool IsReady(string key)
        {
#if SHEEP_LEVELPLAY
            return Slots.TryGetValue(key, out var slot) && slot.IsReady;
#else
            return false;
#endif
        }

        public static bool Show(string key, Action onRewarded, Action onClosed)
        {
#if SHEEP_LEVELPLAY
            if (!Slots.TryGetValue(key, out var slot) || !slot.IsReady)
            {
                RequestLoad(key);
                return false;
            }
            return slot.Show(onRewarded, onClosed);
#else
            return false;
#endif
        }

        /// <summary>
        /// Makes a fresh load request when the shop is opened or the player taps an offer
        /// before it is ready. It is deliberately safe to call repeatedly: RewardSlot keeps
        /// an in-flight request from being duplicated.
        /// </summary>
        public static void RequestLoad(string key)
        {
#if SHEEP_LEVELPLAY
            if (Slots.TryGetValue(key, out var slot)) slot.Load();
#endif
        }

#if SHEEP_LEVELPLAY
        static void Register(string key, string unitId)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(unitId)) return;
            UnitIds[key] = unitId;
        }

        static void OnSdkReady(LevelPlayConfiguration _)
        {
            if (_sdkReady) return;
            _sdkReady = true;
            Debug.Log($"[Ads] LevelPlay initialized. Preparing {UnitIds.Count} rewarded booster ad(s).");
            foreach (var pair in UnitIds)
            {
                var slot = new RewardSlot(pair.Key, pair.Value);
                Slots[pair.Key] = slot;
                slot.Load();
            }
            EnsureRunner();
        }

        static void OnSdkFailed(LevelPlayInitError error)
        {
            Debug.LogWarning($"[Ads] Rewarded ads unavailable because LevelPlay initialization failed " +
                             $"({error.ErrorCode}: {error.ErrorMessage}).");
            foreach (var key in UnitIds.Keys) AvailabilityChanged?.Invoke(key);
        }

        sealed class RewardSlot
        {
            readonly string _key;
            readonly LevelPlayRewardedAd _ad;
            Action _onRewarded;
            Action _onClosed;
            bool _loading;
            bool _showing;
            bool _rewardGranted;
            int _failures;

            public float RetryAt { get; private set; } = -1f;
            public bool IsReady => !_showing && _ad != null && _ad.IsAdReady();

            public RewardSlot(string key, string unitId)
            {
                _key = key;
                _ad = new LevelPlayRewardedAd(unitId);
                _ad.OnAdLoaded += OnLoaded;
                _ad.OnAdLoadFailed += OnLoadFailed;
                _ad.OnAdDisplayed += OnDisplayed;
                _ad.OnAdDisplayFailed += OnDisplayFailed;
                _ad.OnAdRewarded += OnRewarded;
                _ad.OnAdClicked += OnClicked;
                _ad.OnAdClosed += OnClosed;
                _ad.OnAdInfoChanged += OnInfoChanged;
            }

            public void Load()
            {
                if (_loading || _showing || _ad.IsAdReady()) return;
                _loading = true;
                RetryAt = -1f;
                Debug.Log($"[Ads] Requesting rewarded booster '{_key}'.");
                Lifecycle?.Invoke(_key, "Request");
                _ad.LoadAd();
            }

            public void RetryIfDue(float now)
            {
                if (RetryAt < 0f || now < RetryAt) return;
                Load();
            }

            public bool Show(Action onRewarded, Action onClosed)
            {
                if (!IsReady) return false;
                _showing = true;
                _rewardGranted = false;
                _onRewarded = onRewarded;
                _onClosed = onClosed;
                AvailabilityChanged?.Invoke(_key);
                _ad.ShowAd();
                return true;
            }

            void OnLoaded(LevelPlayAdInfo _)
            {
                _loading = false;
                _failures = 0;
                Debug.Log($"[Ads] Rewarded booster '{_key}' loaded and is ready.");
                Lifecycle?.Invoke(_key, "Loaded");
                AvailabilityChanged?.Invoke(_key);
            }

            void OnLoadFailed(LevelPlayAdError error)
            {
                _loading = false;
                _failures++;
                RetryAt = Time.realtimeSinceStartup + Mathf.Min(60f, 5f * _failures);
                Debug.LogWarning($"[Ads] Rewarded booster '{_key}' did not load " +
                                 $"({error.ErrorCode}: {error.ErrorMessage}); retrying.");
                AvailabilityChanged?.Invoke(_key);
            }

            void OnDisplayed(LevelPlayAdInfo _)
            {
                Lifecycle?.Invoke(_key, "Show");
            }

            void OnDisplayFailed(LevelPlayAdInfo _, LevelPlayAdError error)
            {
                Debug.LogWarning($"[Ads] Rewarded booster '{_key}' failed to show: {error}");
                Lifecycle?.Invoke(_key, "FailedShow");
                Finish();
            }

            void OnRewarded(LevelPlayAdInfo _, LevelPlayReward __)
            {
                // LevelPlay explicitly documents that OnAdRewarded and OnAdClosed are
                // asynchronous and may arrive in either order. Finish() deliberately keeps
                // this callback alive after close; checking _showing here used to discard a
                // valid late reward and leave the player without the promised booster.
                if (_rewardGranted || _onRewarded == null) return;
                _rewardGranted = true;
                Lifecycle?.Invoke(_key, "RewardReceived");
                _onRewarded?.Invoke();
            }

            void OnClicked(LevelPlayAdInfo _)
            {
                Lifecycle?.Invoke(_key, "Clicked");
            }

            void OnInfoChanged(LevelPlayAdInfo info)
            {
                Debug.Log($"[Ads] Rewarded booster '{_key}' ad info changed: {info}");
            }

            void OnClosed(LevelPlayAdInfo _)
            {
                Finish();
            }

            void Finish()
            {
                _showing = false;
                var closed = _onClosed;
                _onClosed = null;
                // Keep the reward callback until the next show: LevelPlay documents that the
                // reward callback may arrive just after close. A new show replaces it.
                closed?.Invoke();
                AvailabilityChanged?.Invoke(_key);
                Load();
            }

            public void Dispose()
            {
                _ad.OnAdLoaded -= OnLoaded;
                _ad.OnAdLoadFailed -= OnLoadFailed;
                _ad.OnAdDisplayed -= OnDisplayed;
                _ad.OnAdDisplayFailed -= OnDisplayFailed;
                _ad.OnAdRewarded -= OnRewarded;
                _ad.OnAdClicked -= OnClicked;
                _ad.OnAdClosed -= OnClosed;
                _ad.OnAdInfoChanged -= OnInfoChanged;
                _ad.DestroyAd();
            }
        }

        static void EnsureRunner()
        {
            if (UnityEngine.Object.FindAnyObjectByType<Runner>() != null) return;
            var go = new GameObject("~LevelPlayRewardedRunner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        sealed class Runner : MonoBehaviour
        {
            void Update()
            {
                float now = Time.realtimeSinceStartup;
                foreach (var slot in Slots.Values) slot.RetryIfDue(now);
            }

            void OnApplicationFocus(bool hasFocus)
            {
                if (!hasFocus) return;
                foreach (var slot in Slots.Values) slot.Load();
            }
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
#if SHEEP_LEVELPLAY
            LevelPlay.OnInitSuccess -= OnSdkReady;
            LevelPlay.OnInitFailed -= OnSdkFailed;
            foreach (var slot in Slots.Values) slot.Dispose();
            Slots.Clear();
            UnitIds.Clear();
#endif
            _registered = false;
            _sdkReady = false;
            Lifecycle = null;
            AvailabilityChanged = null;
        }
    }
}
