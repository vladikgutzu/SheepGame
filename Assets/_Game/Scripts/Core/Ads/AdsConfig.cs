using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// LevelPlay keys and when interstitials play. Every number here is a balancing decision,
    /// so none of them is in code.
    /// </summary>
    [CreateAssetMenu(fileName = "AdsConfig", menuName = "SheepGame/Ads Config")]
    public class AdsConfig : ScriptableObject
    {
        [Header("LevelPlay")]
        [Tooltip("App key from the LevelPlay dashboard, Android app.")]
        public string androidAppKey = "";
        [Tooltip("App key from the LevelPlay dashboard, iOS app.")]
        public string iosAppKey = "";
        [Tooltip("Interstitial ad unit id, Android.")]
        public string androidInterstitialId = "";
        [Tooltip("Interstitial ad unit id, iOS.")]
        public string iosInterstitialId = "";
        [Tooltip("Rewarded ad unit id for one Hint, Android.")]
        public string androidHintRewardedId = "";
        [Tooltip("Rewarded ad unit id for one Shuffle, Android.")]
        public string androidShuffleRewardedId = "";
        [Tooltip("Rewarded ad unit id for one Remove, Android.")]
        public string androidRemoveRewardedId = "";
        [Tooltip("Rewarded ad unit id for one Hint, iOS.")]
        public string iosHintRewardedId = "";
        [Tooltip("Rewarded ad unit id for one Shuffle, iOS.")]
        public string iosShuffleRewardedId = "";
        [Tooltip("Rewarded ad unit id for one Remove, iOS.")]
        public string iosRemoveRewardedId = "";
        [Tooltip("Optional placement name reported with each show.")]
        public string placement = "";
        [Tooltip("Runs LevelPlay's integration check on start and logs what is missing. " +
                 "Development builds only.")]
        public bool validateIntegration;
        [Tooltip("Launches LevelPlay's on-device test suite after initialization. Keep off in " +
                 "normal builds; the dedicated diagnostic APK enables it only for that build.")]
        public bool launchTestSuite;

        [Header("Interstitials")]
        [Tooltip("The first level whose completion is followed by an interstitial. Everything " +
                 "before it is the player learning the game, and an ad there costs players.")]
        public int firstInterstitialLevel = 10;
        [Tooltip("1 plays one after every level from then on; 2 after every other one.")]
        [Min(1)] public int everyLevels = 1;
        [Tooltip("Seconds that must pass between two interstitials, whatever the level count " +
                 "says. 0 turns the limit off.")]
        [Min(0)] public float minSecondsBetween = 0f;
        [Tooltip("An entitlement that switches interstitials off -- the id a Remove Ads " +
                 "product grants. Empty means nothing can.")]
        public string noAdsEntitlement = "no_ads";

        public string AppKey =>
            Application.platform == RuntimePlatform.IPhonePlayer ? iosAppKey : androidAppKey;

        public string InterstitialId =>
            Application.platform == RuntimePlatform.IPhonePlayer ? iosInterstitialId : androidInterstitialId;

        public string HintRewardedId =>
            Application.platform == RuntimePlatform.IPhonePlayer ? iosHintRewardedId : androidHintRewardedId;

        public string ShuffleRewardedId =>
            Application.platform == RuntimePlatform.IPhonePlayer ? iosShuffleRewardedId : androidShuffleRewardedId;

        public string RemoveRewardedId =>
            Application.platform == RuntimePlatform.IPhonePlayer ? iosRemoveRewardedId : androidRemoveRewardedId;
    }
}
