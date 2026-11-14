using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core.Meta
{
    /// <summary>Ids of the counted things a player can own. Also the shop's product payloads.</summary>
    public static class ItemIds
    {
        public const string Shuffle = "booster.shuffle";
        public const string Remove = "booster.remove";
        public const string Hint = "booster.hint";
        public const string ExtraTaps = "booster.extra_taps";
    }

    /// <summary>
    /// The player's wallet, inventory and progress: one in-memory copy of the save, one
    /// event when it changes, and a debounced write behind it.
    /// </summary>
    // Static rather than a scene singleton because it has to answer before any scene
    // loads (the main menu needs the coin count on its first frame) and has to outlive
    // scene changes without a DontDestroyOnLoad object that a second scene load would
    // duplicate.
    public static class GameState
    {
        const string SoundPreference = "BaggageJam.Settings.Sound";
        const string MusicPreference = "BaggageJam.Settings.Music";
        const string HapticsPreference = "BaggageJam.Settings.Vibration";

        static SaveData _data;
        static bool _dirty;

        /// <summary>Raised after any change, so HUDs can mirror the wallet without polling.</summary>
        public static event Action Changed;

        public static SaveData Data
        {
            get
            {
                if (_data == null)
                {
                    _data = SaveSystem.Load();
                    _data.launchCount++;
                    if (_data.firstLaunchUnix <= 0)
                        _data.firstLaunchUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (_data.reportedTransactions == null) _data.reportedTransactions = new List<string>();
                    _dirty = true;
                }
                return _data;
            }
        }

        // ---- wallet -------------------------------------------------------------

        public static int Coins => Data.coins;

        public static void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Data.coins += amount;
            MarkDirty();
        }

        public static bool CanAfford(int price) => price <= 0 || Data.coins >= price;

        /// <summary>Spends only if the whole price is there; never leaves a partial charge.</summary>
        public static bool TrySpendCoins(int price)
        {
            if (price <= 0) return true;
            if (Data.coins < price) return false;
            Data.coins -= price;
            MarkDirty();
            return true;
        }

        // ---- inventory ----------------------------------------------------------

        public static int ItemCount(string id)
        {
            var items = Data.items;
            for (int i = 0; i < items.Count; i++)
                if (items[i].id == id) return items[i].count;
            return 0;
        }

        public static void GrantItem(string id, int count)
        {
            if (string.IsNullOrEmpty(id) || count <= 0) return;
            SetItem(id, ItemCount(id) + count);
        }

        public static bool TryConsumeItem(string id, int count = 1)
        {
            int have = ItemCount(id);
            if (have < count) return false;
            SetItem(id, have - count);
            return true;
        }

        static void SetItem(string id, int count)
        {
            var items = Data.items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].id != id) continue;
                items[i] = new CountEntry { id = id, count = Mathf.Max(0, count) };
                MarkDirty();
                return;
            }
            items.Add(new CountEntry { id = id, count = Mathf.Max(0, count) });
            MarkDirty();
        }

        // ---- entitlements (non-consumable purchases) -----------------------------

        public static bool HasEntitlement(string id) =>
            !string.IsNullOrEmpty(id) && Data.entitlements.Contains(id);

        public static void GrantEntitlement(string id)
        {
            if (string.IsNullOrEmpty(id) || Data.entitlements.Contains(id)) return;
            Data.entitlements.Add(id);
            MarkDirty();
        }

        // ---- progress -----------------------------------------------------------

        public static int HighestLevelReached => Data.highestLevelReached;
        public static int LastLevelPlayed => Data.lastLevelPlayed;

        // ---- the daily run ------------------------------------------------------------

        /// <summary>A calendar day as yyyymmdd, which is what the save keeps.</summary>
        public static int DayCode(System.DateTime day) => day.Year * 10000 + day.Month * 100 + day.Day;

        public static int DailyStreak => Data.dailyStreak;

        public static bool DailyClearedOn(System.DateTime day) => Data.dailyLastCleared == DayCode(day);

        /// <summary>Records today's clear and carries the streak, which yesterday's clear extends.</summary>
        public static void RecordDailyClear(System.DateTime day)
        {
            int today = DayCode(day);
            if (Data.dailyLastCleared == today) return;      // already counted; a replay is free

            // Yesterday by the calendar rather than by arithmetic on the code, so the turn of a
            // month or a year does not break the streak.
            Data.dailyStreak = Data.dailyLastCleared == DayCode(day.AddDays(-1))
                ? Data.dailyStreak + 1
                : 1;

            Data.dailyLastCleared = today;
            MarkDirty();
        }

        public static void SetCurrentLevel(int level)
        {
            if (level < 1 || Data.lastLevelPlayed == level) return;
            Data.lastLevelPlayed = level;
            if (level > Data.highestLevelReached) Data.highestLevelReached = level;
            MarkDirty();
        }

        /// <summary>Records a clear. Keeps the fewest taps ever used on that level.</summary>
        public static void RecordLevelComplete(int level, int tapsUsed)
        {
            var levels = Data.levels;
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].level != level) continue;
                if (tapsUsed > 0 && (levels[i].bestTaps <= 0 || tapsUsed < levels[i].bestTaps))
                    levels[i] = new LevelRecord { level = level, bestTaps = tapsUsed };
                MarkDirty();
                return;
            }
            levels.Add(new LevelRecord { level = level, bestTaps = tapsUsed });
            MarkDirty();
        }

        public static bool IsLevelCleared(int level) => BestTaps(level) > 0;

        public static int BestTaps(int level)
        {
            var levels = Data.levels;
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].level == level) return levels[i].bestTaps;
            return 0;
        }

        // ---- one-time introductions --------------------------------------------------

        public static bool IntroSeen(string id) =>
            !string.IsNullOrEmpty(id) && Data.introsSeen != null && Data.introsSeen.Contains(id);

        public static void MarkIntroSeen(string id)
        {
            if (string.IsNullOrEmpty(id) || IntroSeen(id)) return;
            if (Data.introsSeen == null) Data.introsSeen = new List<string>();
            Data.introsSeen.Add(id);
            MarkDirty();
        }

        // ---- settings -----------------------------------------------------------

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundPreference, 1) != 0;
            set => SetPreference(SoundPreference, value);
        }

        public static bool MusicOn
        {
            get => PlayerPrefs.GetInt(MusicPreference, 1) != 0;
            set => SetPreference(MusicPreference, value);
        }

        public static bool HapticsOn
        {
            get => PlayerPrefs.GetInt(HapticsPreference, 1) != 0;
            set => SetPreference(HapticsPreference, value);
        }

        static void SetPreference(string key, bool value)
        {
            int stored = value ? 1 : 0;
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetInt(key) == stored) return;
            PlayerPrefs.SetInt(key, stored);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // ---- persistence --------------------------------------------------------

        /// <summary>Queues a write and tells the UI to refresh. Cheap; call it freely.</summary>
        public static void MarkDirty()
        {
            _dirty = true;
            SaveRunner.Ensure();
            Changed?.Invoke();
        }

        /// <summary>Writes now if anything is queued. Called on pause, focus loss and quit.</summary>
        public static void FlushNow()
        {
            if (!_dirty || _data == null) return;
            SaveSystem.Save(_data);
            _dirty = false;
        }

        /// <summary>Throws the save away and starts clean. Development only.</summary>
        public static void ResetAll()
        {
            SaveSystem.Delete();
            _data = new SaveData();
            _dirty = true;
            Changed?.Invoke();
        }

        // Editor domain reloads keep static state alive between play sessions, which
        // would leave the previous run's wallet in memory over a file that has since
        // been edited or deleted.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _data = null;
            _dirty = false;
            Changed = null;
        }
    }
}
