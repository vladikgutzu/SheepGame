using System;
using System.Collections.Generic;
using UnityEngine;
#if SHEEP_GAMEANALYTICS
using GameAnalyticsSDK;
#endif

namespace SheepGame.Analytics
{
    public enum ProgressionStep { Start, Complete, Fail }

    public enum AdKind { RewardedVideo, Interstitial, Banner }

    public enum AdStep { Request, Loaded, Show, Clicked, FailedShow, RewardReceived }

    /// <summary>
    /// The one place that talks to GameAnalytics. Knows the SDK's calls and nothing about the
    /// game: what an event means, and when it is sent, is <c>SheepGame.Core.Telemetry</c>'s.
    /// </summary>
    // Its own assembly, like the ads, so the game compiles with the SDK or without it: the
    // SHEEP_GAMEANALYTICS define comes from a version define on the package. Without the
    // package every call is a no-op apart from the console echo.
    public static class AnalyticsBridge
    {
#if SHEEP_GAMEANALYTICS
        public const bool Compiled = true;
#else
        public const bool Compiled = false;
#endif

        /// <summary>
        /// Echo every event to the console. On in the editor and in development builds, so an
        /// integration can be checked without waiting for the dashboard.
        /// </summary>
        public static bool LogEvents = Application.isEditor || Debug.isDebugBuild;

        public static bool Initialized { get; private set; }
        public static bool SdkReady { get; private set; }

#if SHEEP_GAMEANALYTICS
        // GameAnalytics initializes asynchronously on a device. The first level can start before
        // its callback, so keep those early events instead of silently dropping them.
        static readonly List<Action> PendingEvents = new List<Action>();
        static bool SdkInitializationFinished;
#endif

        // Before the first scene, so nothing a scene does on its first frame is sent to an SDK
        // that has not started -- GameAnalytics drops those with an error.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Initialize();

        public static void Initialize()
        {
            if (Initialized) return;
            Initialized = true;
#if SHEEP_GAMEANALYTICS
            GameAnalytics.onInitialize -= OnSdkInitialized;
            GameAnalytics.onInitialize += OnSdkInitialized;

            // Keep production logs concise, but expose the SDK's request/validation details
            // in Development Builds so Android logcat explains why an event was rejected.
            GameAnalytics.SettingsGA.InfoLogBuild = true;
            GameAnalytics.SettingsGA.VerboseLogBuild = Debug.isDebugBuild;

            // The SDK's own component is what hooks Unity's log for automatic error reporting,
            // and it only does that while it exists in a scene. It moves itself into
            // DontDestroyOnLoad in Awake, so one made here outlives every scene change.
            if (UnityEngine.Object.FindFirstObjectByType<GameAnalytics>() == null)
                new GameObject("GameAnalytics").AddComponent<GameAnalytics>();

            Echo($"initializing GameAnalytics {GameAnalyticsSDK.Setup.Settings.VERSION} " +
                 $"for {Application.platform}, build {Application.version}");
            GameAnalytics.Initialize();
#else
            if (LogEvents) Debug.Log("[Analytics] GameAnalytics is not installed; events are only logged.");
#endif
        }

#if SHEEP_GAMEANALYTICS
        static void OnSdkInitialized(object _, bool success)
        {
            SdkInitializationFinished = true;
            SdkReady = success;
            if (success)
            {
                Echo("GameAnalytics initialized successfully; event delivery is enabled.");
                var pending = PendingEvents.ToArray();
                PendingEvents.Clear();
                foreach (Action send in pending) send();
            }
            else
            {
                PendingEvents.Clear();
                Debug.LogWarning("[Analytics] GameAnalytics initialization failed; events will only be echoed locally.");
            }
        }

        static void SendWhenReady(Action send)
        {
            if (SdkReady) send();
            else if (!SdkInitializationFinished) PendingEvents.Add(send);
        }
#endif

        public static void Progression(ProgressionStep step, string p1, string p2)
        {
            Echo($"progression {step}:{p1}:{p2}");
#if SHEEP_GAMEANALYTICS
            SendWhenReady(() => GameAnalytics.NewProgressionEvent(ToStatus(step), p1, p2));
#endif
        }

        public static void Design(string eventId, IDictionary<string, object> fields = null)
        {
            Echo(fields == null || fields.Count == 0 ? $"design {eventId}" : $"design {eventId} {Describe(fields)}");
#if SHEEP_GAMEANALYTICS
            if (fields == null || fields.Count == 0)
            {
                SendWhenReady(() => GameAnalytics.NewDesignEvent(eventId));
            }
            else
            {
                var capturedFields = new Dictionary<string, object>(fields);
                SendWhenReady(() => GameAnalytics.NewDesignEvent(eventId, capturedFields));
            }
#endif
        }

        public static void Ad(AdKind kind, AdStep step, string sdkName, string placement)
        {
            Echo($"ad {kind}/{step} {sdkName} '{placement}'");
#if SHEEP_GAMEANALYTICS
            string capturedPlacement = placement ?? string.Empty;
            SendWhenReady(() => GameAnalytics.NewAdEvent(ToAction(step), ToType(kind), sdkName, capturedPlacement));
#endif
        }

        /// <param name="amountInCents">The localised price times 100, rounded.</param>
        /// <param name="receipt">The store receipt. Sent on iOS only, which is where
        /// GameAnalytics validates one without a signature.</param>
        public static void Business(string currency, int amountInCents, string itemType, string itemId,
                                    string cartType, string receipt)
        {
            Echo($"business {currency} {amountInCents} {itemType}/{itemId} cart={cartType}");
#if SHEEP_GAMEANALYTICS
            // The SDK only compiles its receipt overload into iOS and tvOS builds.
#if UNITY_IOS || UNITY_TVOS
            if (!string.IsNullOrEmpty(receipt))
            {
                SendWhenReady(() => GameAnalytics.NewBusinessEventIOS(currency, amountInCents, itemType, itemId, cartType, receipt));
                return;
            }
#endif
            SendWhenReady(() => GameAnalytics.NewBusinessEvent(currency, amountInCents, itemType, itemId, cartType));
#endif
        }

        static void Echo(string line)
        {
            if (LogEvents) Debug.Log("[Analytics] " + line);
        }

        static string Describe(IDictionary<string, object> fields)
        {
            var parts = new List<string>(fields.Count);
            foreach (var pair in fields) parts.Add(pair.Key + "=" + pair.Value);
            return "{" + string.Join(", ", parts) + "}";
        }

#if SHEEP_GAMEANALYTICS
        static GAProgressionStatus ToStatus(ProgressionStep step) => step switch
        {
            ProgressionStep.Start => GAProgressionStatus.Start,
            ProgressionStep.Complete => GAProgressionStatus.Complete,
            _ => GAProgressionStatus.Fail
        };

        static GAAdType ToType(AdKind kind) => kind switch
        {
            AdKind.RewardedVideo => GAAdType.RewardedVideo,
            AdKind.Interstitial => GAAdType.Interstitial,
            _ => GAAdType.Banner
        };

        static GAAdAction ToAction(AdStep step) => step switch
        {
            AdStep.Request => GAAdAction.Request,
            AdStep.Loaded => GAAdAction.Loaded,
            AdStep.Show => GAAdAction.Show,
            AdStep.Clicked => GAAdAction.Clicked,
            AdStep.FailedShow => GAAdAction.FailedShow,
            _ => GAAdAction.RewardReceived
        };
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
#if SHEEP_GAMEANALYTICS
            GameAnalytics.onInitialize -= OnSdkInitialized;
#endif
            Initialized = false;
            SdkReady = false;
#if SHEEP_GAMEANALYTICS
            PendingEvents.Clear();
            SdkInitializationFinished = false;
#endif
        }
    }
}
