using System;
using System.Collections.Generic;

namespace SheepGame.Core.Meta
{
    /// <summary>
    /// Everything the player keeps between sessions, in one object written as JSON.
    /// </summary>
    // JsonUtility cannot serialise a dictionary, so counted things are lists of entries
    // with lookup helpers rather than Dictionary<string,int>. That is not only a
    // workaround: an id this build does not know about survives a load and a re-save
    // instead of being silently dropped, so a player who downgrades does not lose the
    // boosters a newer build sold them.
    [Serializable]
    public class SaveData
    {
        // Bumped whenever a field changes meaning. SaveSystem.Migrate handles the step.
        // 2: the level ladder was redrawn for the colour rules, so level numbers from 1 name
        // different boards and progress was reset (see SaveSystem.Migrate).
        public const int CurrentSchema = 2;

        public int schemaVersion = CurrentSchema;

        // Progress. highestLevelReached is what the player may jump to; lastLevelPlayed
        // is where the game resumes, and they differ once a level select exists.
        public int highestLevelReached = 1;
        public int lastLevelPlayed = 1;

        // The Daily Run. The date is kept as yyyymmdd rather than as a DateTime because the
        // save is JSON and a plain int survives every culture and every serialiser; the streak
        // is kept beside it because it cannot be recomputed from one date alone.
        public int dailyLastCleared;
        public int dailyStreak;

        public int coins;

        public List<CountEntry> items = new List<CountEntry>();
        public List<string> entitlements = new List<string>();
        public List<LevelRecord> levels = new List<LevelRecord>();

        // Written before a purchase is granted and cleared after, so a crash between the
        // two is recoverable rather than a swallowed product. See StorePurchaser.
        public PurchaseJournalEntry pendingPurchase;

        public bool soundOn = true;
        public bool musicOn = true;
        public bool hapticsOn = true;
        public string language = "";

        // Ids of the one-time introductions already played -- a booster being handed over,
        // for instance -- so they are shown once per player rather than once per visit.
        public List<string> introsSeen = new List<string>();

        public bool firstLaunchGrantsApplied;
        public int launchCount;
        public long lastSavedUnix;

        // Analytics milestones. The "first" events are once per save, so whether they have
        // gone has to live in the save; a save older than these fields reads 0 / false, and
        // its first launch is taken as the launch that first saw it.
        public long firstLaunchUnix;
        public bool firstLoseReported;
        public bool firstPurchaseReported;
        // Transactions already reported as revenue, so a purchase finished again after a crash
        // (StorePurchaser.RecoverInterruptedPurchase) is not counted twice.
        public List<string> reportedTransactions = new List<string>();
    }

    [Serializable]
    public struct CountEntry
    {
        public string id;
        public int count;
    }

    [Serializable]
    public struct LevelRecord
    {
        public int level;
        // Fewest taps the level has ever been cleared in. The floor is the piece count —
        // one successful tap per piece — so this doubles as a "played it clean" flag.
        public int bestTaps;
    }

    [Serializable]
    public class PurchaseJournalEntry
    {
        public string productId;
        public string transactionId;
        public long startedUnix;
    }
}
