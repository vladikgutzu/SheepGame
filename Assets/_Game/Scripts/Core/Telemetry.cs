using System;
using System.Collections.Generic;
using SheepGame.Analytics;
using SheepGame.Core.Meta;
using SheepGame.Core.Store;

namespace SheepGame.Core
{
    /// <summary>
    /// What the game reports, in the game's own words. Every event name in the analytics plan
    /// is written here and nowhere else; <see cref="AnalyticsBridge"/> only knows how to send.
    /// </summary>
    // Number formats are part of the names and the dashboard sorts them as text, which is why
    // they are padded: level 0012 has to sort before 0100. Progression and milestones use four
    // digits, gate targets three (level:level_007), as the plan specifies.
    public static class Telemetry
    {
        public const string AdSdk = "LevelPlay";

        public enum ReviveMethod
        {
            Coins,
            CoinsShuffle,
            ExtraTaps
        }

        // ---- progression ------------------------------------------------------------------

        public static void LevelStarted(int level, bool daily) => Progress(ProgressionStep.Start, level, daily);
        public static void LevelCompleted(int level, bool daily) => Progress(ProgressionStep.Complete, level, daily);
        public static void LevelFailed(int level, bool daily) => Progress(ProgressionStep.Fail, level, daily);

        // The daily board is not a rung of the ladder, so it has its own first part and is
        // numbered by its date (20260919) -- it must not land in the level funnel.
        static void Progress(ProgressionStep step, int level, bool daily) =>
            AnalyticsBridge.Progression(step, daily ? "daily" : "level", daily ? level.ToString() : Pad4(level));

        // ---- revive -------------------------------------------------------------------------

        public static void RevivePopupShown(int level, bool daily) =>
            AnalyticsBridge.Design("gameplay:revive:popup_shown", LevelFields(level, daily));

        public static void Revived(int level, bool daily, ReviveMethod method)
        {
            string how = RevivePart(method);
            AnalyticsBridge.Design("gameplay:revive:" + how, LevelFields(level, daily));

            if (method == ReviveMethod.Coins || method == ReviveMethod.CoinsShuffle)
                Resolved("revive", how, TargetType(daily), TargetId(level, daily));
        }

        public static void ReviveNotEnoughMoney(int level, bool daily, ReviveMethod method)
        {
            AnalyticsBridge.Design("gameplay:revive:not_enough_money:" + RevivePart(method),
                                   LevelFields(level, daily));
            Blocked("revive", "no_coins", TargetType(daily), TargetId(level, daily));
        }

        static string TargetType(bool daily) => daily ? "daily" : "level";
        static string TargetId(int level, bool daily) => daily ? "daily_" + level : LevelTarget(level);

        public static void ReviveSkipped(int level, bool daily) =>
            AnalyticsBridge.Design("gameplay:revive:skipped", LevelFields(level, daily));

        static string RevivePart(ReviveMethod method) => method switch
        {
            ReviveMethod.Coins => "coins",
            ReviveMethod.CoinsShuffle => "coins_shuffle",
            _ => "extra_taps"
        };

        // ---- boosters ----------------------------------------------------------------------

        /// <summary>Reports only boosters that really exist and were actually spent.</summary>
        public static void BoosterUsed(string itemId, int level, bool daily)
        {
            string booster = BoosterPart(itemId);
            if (booster == null) return;
            AnalyticsBridge.Design("gameplay:booster:used:" + booster, LevelFields(level, daily));
        }

        /// <summary>The selected booster was granted after a completed rewarded ad.</summary>
        public static void BoosterRewarded(string itemId)
        {
            string booster = BoosterPart(itemId);
            if (booster == null) return;
            AnalyticsBridge.Design("gameplay:booster:rewarded:" + booster);
        }

        static string BoosterPart(string itemId)
        {
            if (itemId == ItemIds.Hint) return "hint";
            if (itemId == ItemIds.Shuffle) return "shuffle";
            if (itemId == ItemIds.Remove) return "remove";
            return null;
        }

        static IDictionary<string, object> LevelFields(int level, bool daily) =>
            new Dictionary<string, object>
            {
                ["level"] = Math.Max(1, level),
                ["mode"] = daily ? "daily" : "level"
            };

        // ---- first loss -------------------------------------------------------------------

        /// <summary>Once per save: the first time the lose panel ever comes up.</summary>
        public static void LostLevel(int level)
        {
            var data = GameState.Data;
            if (data.firstLoseReported) return;
            data.firstLoseReported = true;
            GameState.MarkDirty();
            Milestone("gameplay:first_lose", level);
        }

        // ---- purchases -------------------------------------------------------------------

        public static void PurchaseStarted(string productId) =>
            AnalyticsBridge.Design("shop:purchase_start:" + Part(productId));

        /// <summary>
        /// Reports how a purchase ended. A paid, real, first-seen transaction becomes revenue;
        /// anything that stopped the player becomes a blocked purchase gate.
        /// </summary>
        public static void PurchaseFinished(PurchaseResult result, IStoreService store, bool productKnown)
        {
            string target = Part(result.ProductId);

            switch (result.Status)
            {
                case PurchaseStatus.Success:
                    Resolved("purchase_unlock", "purchase_retry", "product", target);
                    ReportRevenue(result, store);
                    break;
                case PurchaseStatus.AlreadyOwned:
                    // Nothing was paid for, and the player got what they asked for.
                    Resolved("purchase_unlock", "purchase_retry", "product", target);
                    break;
                case PurchaseStatus.Cancelled:
                    Blocked("purchase_unlock", "purchase_cancelled", "product", target);
                    break;
                case PurchaseStatus.NotReady:
                    Blocked("purchase_unlock", "iap_not_ready", "product", target);
                    break;
                default:
                    Blocked("purchase_unlock", productKnown ? "iap_failed" : "product_unavailable", "product", target);
                    break;
            }
        }

        // Validated: the store answered Success with a transaction id. Deduplicated: by that id,
        // in the save, so a purchase finished again after a crash is counted once. Sandbox
        // purchases -- every purchase through the local store -- are never revenue.
        static void ReportRevenue(PurchaseResult result, IStoreService store)
        {
            if (store == null || store.IsSandbox) return;
            if (string.IsNullOrEmpty(result.TransactionId)) return;

            var data = GameState.Data;
            if (data.reportedTransactions.Contains(result.TransactionId)) return;
            if (!store.TryGetPrice(result.ProductId, out string currency, out decimal price)) return;

            data.reportedTransactions.Add(result.TransactionId);
            GameState.MarkDirty();

            int cents = (int)Math.Round(price * 100m, MidpointRounding.AwayFromZero);
            AnalyticsBridge.Business(currency, cents, result.ProductId, result.ProductId, "iap", result.Receipt);

            if (!data.firstPurchaseReported)
            {
                data.firstPurchaseReported = true;
                GameState.MarkDirty();
                Milestone("economy:first_purchase", GameState.LastLevelPlayed);
            }
        }

        // ---- ads ----------------------------------------------------------------------------

        public static void Ad(AdKind kind, AdStep step, string placement) =>
            AnalyticsBridge.Ad(kind, step, AdSdk, placement);

        // ---- anything else ----------------------------------------------------------------

        /// <summary>A custom design event, with optional custom fields.</summary>
        public static void TrackEvent(string eventName, IDictionary<string, object> fields = null) =>
            AnalyticsBridge.Design(eventName, fields);

        // ---- gates ----------------------------------------------------------------------------

        // A gate is something that stood between the player and progress: a revive they could
        // not afford, a purchase that did not go through. Resolved is only reported for a gate
        // that was reported blocked in this session, so the pair always reads as a story.
        static readonly HashSet<string> OpenGates = new HashSet<string>();

        public static void Blocked(string gate, string reason, string targetType, string targetId)
        {
            OpenGates.Add(GateKey(gate, targetType, targetId));
            AnalyticsBridge.Design($"progress_blocked:{gate}:{reason}:{targetType}:{targetId}");
        }

        public static void Resolved(string gate, string how, string targetType, string targetId)
        {
            if (!OpenGates.Remove(GateKey(gate, targetType, targetId))) return;
            AnalyticsBridge.Design($"progress_resolved:{gate}:{how}:{targetType}:{targetId}");
        }

        static string GateKey(string gate, string targetType, string targetId) => gate + "|" + targetType + "|" + targetId;

        // ---- formats --------------------------------------------------------------------------

        // Three events rather than one with three values, as the plan has it, so each can be
        // charted on its own: how long, how far and how many sessions it took.
        static void Milestone(string prefix, int level)
        {
            var data = GameState.Data;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long minutes = data.firstLaunchUnix > 0 ? Math.Max(0, (now - data.firstLaunchUnix) / 60) : 0;

            AnalyticsBridge.Design($"{prefix}:min_{minutes:D4}");
            AnalyticsBridge.Design($"{prefix}:lvl_{Pad4(level)}");
            AnalyticsBridge.Design($"{prefix}:session_{Pad4(data.launchCount)}");
        }

        static string Pad4(int n) => Math.Max(0, n).ToString("D4");

        public static string LevelTarget(int level) => "level_" + Math.Max(0, level).ToString("D3");

        // A design event part allows letters, digits and " -_.()!?" only; a product id is data
        // and could carry anything else.
        static string Part(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '(' ||
                          c == ')' || c == '!' || c == '?' || c == ' ';
                if (!ok) chars[i] = '_';
            }
            return new string(chars);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OpenGates.Clear();
    }
}
