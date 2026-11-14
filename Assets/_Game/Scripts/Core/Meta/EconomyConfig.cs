using UnityEngine;

namespace SheepGame.Core.Meta
{
    /// <summary>
    /// Every number the economy is tuned with, in one asset so balancing never means
    /// editing code. Referenced by GameManager; the shop reads it through there.
    /// </summary>
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "SheepGame/Economy Config")]
    public class EconomyConfig : ScriptableObject
    {
        [Header("First launch")]
        [Tooltip("Granted once, the first time the game is opened. Enough to feel the " +
                 "boosters before being asked to pay for them.")]
        public int startingCoins = 150;
        // Zero by default: each booster is handed over when it unlocks (see Unlocks), and a
        // stock of something the HUD does not show yet is a stock nobody knows they have.
        public int startingShuffles = 0;
        public int startingRemoves = 0;
        public int startingHints = 0;

        [Header("Level rewards")]
        [Tooltip("Paid for clearing a level, every time it is cleared.")]
        public int coinsPerLevel = 20;
        [Tooltip("Paid once, the first time a level is cleared. Keeps replaying old levels " +
                 "from being a coin faucet without making a replay worthless.")]
        public int firstClearBonus = 30;
        [Tooltip("Paid per tap left over. Rewards playing tight rather than brute-forcing.")]
        public int coinsPerUnusedTap = 2;

        [Header("Booster prices, in coins")]
        public int shufflePrice = 120;
        public int removePrice = 150;
        [Tooltip("Cheapest of the three: it saves one wasted tap, where the other two save " +
                 "several or take a piece outright.")]
        public int hintPrice = 60;

        [Header("Out of taps")]
        [Tooltip("Taps handed back by one Extra Taps booster, and by the continue button.")]
        public int extraTapsAmount = 5;
        [Tooltip("Coin price of continuing when the taps run out. Rises with each continue " +
                 "inside one attempt, so continuing forever costs more than restarting.")]
        public int continuePrice = 100;
        [Tooltip("Multiplier applied to the continue price for each continue already used " +
                 "on this attempt. 1 keeps it flat.")]
        [Range(1f, 4f)] public float continuePriceStep = 2f;

        // The second rescue: fewer taps back, but every remaining piece is turned to a new
        // facing. It is worth more than the taps it hands over because a reshuffled board
        // wastes fewer of them — which is the whole reason a tap budget makes shuffling
        // valuable in the first place.
        [Tooltip("Taps handed back by the shuffle rescue. Deliberately fewer than a plain " +
                 "top-up: it also hands back an easier board.")]
        public int shuffleRescueTaps = 3;
        [Tooltip("Price of the shuffle rescue, as a multiple of the plain continue price.")]
        [Range(1f, 4f)] public float shuffleRescuePriceScale = 1.6f;

        [Header("Tap budget")]
        // A piece needs exactly one successful tap to leave, so the piece count is the
        // theoretical floor and everything above it is room to guess wrong. Slack is the
        // entire difficulty dial: tighten it and the same board demands you read it first.
        [Tooltip("Extra taps as a share of the piece count on level 1.")]
        [Range(0f, 2f)] public float earlySlack = 0.55f;
        [Tooltip("Extra taps as a share of the piece count once the ramp is over.")]
        [Range(0f, 2f)] public float lateSlack = 0.22f;
        [Tooltip("Level at which the slack has finished falling from early to late.")]
        public int slackTightensBy = 30;
        [Tooltip("Floor, so a small level is never down to a couple of spare taps.")]
        public int minimumSlack = 4;

        /// <summary>
        /// Taps allowed for a level of this size. Levels may override it in the asset.
        /// </summary>
        public int TapBudget(int pieceCount, int levelNumber)
        {
            if (pieceCount <= 0) return 0;

            float t = slackTightensBy <= 1
                ? 1f
                : Mathf.Clamp01((levelNumber - 1) / (float)(slackTightensBy - 1));
            float slack = Mathf.Lerp(earlySlack, lateSlack, t);

            return pieceCount + Mathf.Max(minimumSlack, Mathf.CeilToInt(pieceCount * slack));
        }

        [Header("Generated levels")]
        // How full the board is, and how much of it is long luggage. The authored ladder
        // ends on a board three quarters full of one-cell cases, and the generator picks up
        // from exactly there rather than starting its own count from zero -- which is what
        // it used to do, dealing 32 pieces after an authored level of 53.
        //
        // Written as a share of the field rather than as a number of pieces, because a piece
        // is not a fixed amount of board: a long one takes two cells. A count would quietly
        // empty the board out as the long share rose.
        [Tooltip("Share of the field's cells the first generated level fills.")]
        [Range(0.2f, 1f)] public float earlyFill = 0.75f;
        [Tooltip("Share of the cells a generated level fills once the ramp is over. 1 packs " +
                 "the board solid — no empty cells at all.")]
        [Range(0.2f, 1f)] public float lateFill = 1f;
        [Tooltip("Generated levels the fill takes to get from the first share to the last.")]
        public int fillPacksOver = 12;

        [Tooltip("Share of the pieces asked to be two cells long on the first generated level.")]
        [Range(0f, 1f)] public float earlyLongShare = 0.15f;
        [Tooltip("Share asked for once that ramp is over. The one dial with no ceiling in " +
                 "front of it: the board cannot hold more than every cell, but it can hold " +
                 "the same cells as fewer, longer, more awkward pieces.")]
        [Range(0f, 1f)] public float lateLongShare = 0.4f;
        [Tooltip("Generated levels the long share takes to get from the first to the last.")]
        public int longShareGrowsOver = 28;

        /// <summary>Share of the pieces a generated level asks to be two cells long.</summary>
        public float LongShareFor(int levelNumber, int authoredLevels) =>
            Mathf.Lerp(earlyLongShare, lateLongShare, Ramp(levelNumber, authoredLevels, longShareGrowsOver));

        /// <summary>
        /// How many pieces a generated level is dealt, for a field of this many cells.
        /// </summary>
        // Derived from the fill rather than set directly, so the two ramps do not fight: a
        // deal of the same share of the board is fewer pieces once more of them are long.
        public int PieceCountFor(int levelNumber, int cells, int authoredLevels)
        {
            if (cells <= 0) return 0;

            float fill = Mathf.Lerp(earlyFill, lateFill, Ramp(levelNumber, authoredLevels, fillPacksOver));
            float perPiece = 1f + LongShareFor(levelNumber, authoredLevels);

            return Mathf.Clamp(Mathf.RoundToInt(cells * fill / perPiece), 8, cells);
        }

        // 0 on the first level the generator deals, 1 once the ramp is spent. Measured from
        // where the authored ladder ends, so adding a hand-drawn level does not leave a step
        // in the middle of the generated ones.
        static float Ramp(int levelNumber, int authoredLevels, int over) =>
            over <= 0 ? 1f : Mathf.Clamp01((levelNumber - authoredLevels - 1) / (float)over);

        [Header("Colours and the queue")]
        [Tooltip("Places in the queue on a level that does not set its own.")]
        [Range(1, 7)] public int queueSlots = 5;
        [Tooltip("Most places a continue can widen the queue to. The HUD has room for seven.")]
        [Range(1, 7)] public int maxQueueSlots = 7;
        [Tooltip("Places a continue adds to the queue.")]
        public int extraSlotsAmount = 1;

        [Tooltip("Colours on the first generated level.")]
        [Range(2, 6)] public int earlyColours = 5;
        [Tooltip("Colours once that ramp is over.")]
        [Range(2, 6)] public int lateColours = 6;
        [Tooltip("Generated levels the colour count takes to grow.")]
        public int coloursGrowOver = 15;
        // How far a case may be dealt out of its cart's turn. See ColourDealer.
        [Tooltip("How far out of turn a case may be dealt on the first generated level. " +
                 "0 means the cases come out in cart order and nothing ever waits.")]
        [Range(0, 10)] public int earlyShuffleWindow = 4;
        [Tooltip("The same, once the ramp is over.")]
        [Range(0, 10)] public int lateShuffleWindow = 7;
        [Tooltip("Generated levels the window takes to widen.")]
        public int shuffleWindowGrowsOver = 30;

        public int ColoursFor(int levelNumber, int authoredLevels) =>
            Mathf.RoundToInt(Mathf.Lerp(earlyColours, lateColours, Ramp(levelNumber, authoredLevels, coloursGrowOver)));

        public int ShuffleWindowFor(int levelNumber, int authoredLevels) =>
            Mathf.RoundToInt(Mathf.Lerp(earlyShuffleWindow, lateShuffleWindow,
                                        Ramp(levelNumber, authoredLevels, shuffleWindowGrowsOver)));

        [Header("Unlocks")]
        // A new player is shown the board and nothing else. Each booster arrives on a level
        // built to need it, with a couple in hand, so the first one is spent on the problem it
        // solves rather than discovered in a menu.
        [Tooltip("Level the Remove booster is handed over on.")]
        public int removeUnlockLevel = 15;
        [Tooltip("Level the Shuffle booster is handed over on.")]
        public int shuffleUnlockLevel = 12;
        [Tooltip("Level the Hint booster is handed over on.")]
        public int hintUnlockLevel = 6;
        [Tooltip("How many of a booster come with it when it unlocks.")]
        public int unlockGrant = 2;
        [Tooltip("Level from which the coin balance is on the HUD. The first level is " +
                 "nothing but the board.")]
        public int coinsUnlockLevel = 2;
        [Tooltip("Level from which the Daily Run is offered on the menu.")]
        public int dailyUnlockLevel = 8;

        /// <summary>The level a booster, or other piece of HUD, becomes available on.</summary>
        public int UnlockLevelFor(string itemId)
        {
            if (itemId == ItemIds.Remove) return removeUnlockLevel;
            if (itemId == ItemIds.Shuffle) return shuffleUnlockLevel;
            if (itemId == ItemIds.Hint) return hintUnlockLevel;
            return 1;
        }

        [Header("Time Rush")]
        // A change of pressure, not more of it: a Time Rush level has no tap limit, so the
        // player is asked to move fast instead of to read carefully. Mixing the two would ask
        // for both at once.
        [Tooltip("First level that can be a Time Rush. Kept clear of the opening levels so " +
                 "the ordinary rules are learned before anything varies them.")]
        public int timeRushFirstLevel = 25;
        [Tooltip("How often one comes round after that. 0 turns the mode off entirely.")]
        public int timeRushEvery = 5;

        [Tooltip("Seconds allowed per piece on the board. The clock is sized to the level for " +
                 "the same reason the tap budget is: a flat number is generous on a small " +
                 "board and impossible on a big one.")]
        public float secondsPerPiece = 1.6f;
        [Tooltip("Floor, so a small board is never a handful of seconds.")]
        public int minimumSeconds = 20;
        [Tooltip("Seconds handed back by a continue or an Extra Taps booster during a Time " +
                 "Rush. Taps are worth nothing there, so they are paid out as time instead.")]
        public int extraSecondsAmount = 15;
        [Tooltip("Coins per whole second left on the clock. The counterpart of coins per " +
                 "unused tap, so clearing fast pays like clearing tight does.")]
        public int coinsPerUnusedSecond = 1;

        /// <summary>Seconds allowed for a level of this size.</summary>
        public float TimeBudget(int pieceCount)
        {
            if (pieceCount <= 0) return 0f;
            return Mathf.Max(minimumSeconds, Mathf.CeilToInt(pieceCount * Mathf.Max(0.1f, secondsPerPiece)));
        }

        /// <summary>Whether the progression puts a Time Rush on this level number.</summary>
        // Every nth from a first appearance rather than at random: a player who has met one
        // should be able to feel the next coming, and a mode that arrives unpredictably reads
        // as the game misbehaving rather than as a variation.
        public bool IsTimeRushLevel(int levelNumber)
        {
            if (timeRushEvery <= 0 || levelNumber < timeRushFirstLevel) return false;
            return (levelNumber - timeRushFirstLevel) % timeRushEvery == 0;
        }

        /// <summary>Coins for clearing a Time Rush, given the seconds still on the clock.</summary>
        public int TimeRushReward(float secondsLeft, bool firstClear)
        {
            int reward = coinsPerLevel + Mathf.Max(0, Mathf.FloorToInt(secondsLeft)) * coinsPerUnusedSecond;
            if (firstClear) reward += firstClearBonus;
            return reward;
        }

        /// <summary>Coins for a clear, given how much of the budget went unused.</summary>
        public int ClearReward(int tapsLeft, bool firstClear)
        {
            int reward = coinsPerLevel + Mathf.Max(0, tapsLeft) * coinsPerUnusedTap;
            if (firstClear) reward += firstClearBonus;
            return reward;
        }

        /// <summary>Price of the next continue on an attempt that has already used some.</summary>
        public int ContinuePrice(int continuesUsed) =>
            Mathf.RoundToInt(continuePrice * Mathf.Pow(continuePriceStep, Mathf.Max(0, continuesUsed)));

        /// <summary>Price of the shuffle rescue. Rides the same ramp as the plain continue.</summary>
        public int ShuffleRescuePrice(int continuesUsed) =>
            Mathf.RoundToInt(ContinuePrice(continuesUsed) * shuffleRescuePriceScale);

        public int BoosterPrice(string itemId)
        {
            if (itemId == ItemIds.Shuffle) return shufflePrice;
            if (itemId == ItemIds.Remove) return removePrice;
            if (itemId == ItemIds.Hint) return hintPrice;
            return 0;
        }

        /// <summary>Runs once ever, on a save that has never been granted its opening stock.</summary>
        public void ApplyFirstLaunchGrants()
        {
            if (GameState.Data.firstLaunchGrantsApplied) return;

            GameState.AddCoins(startingCoins);
            GameState.GrantItem(ItemIds.Shuffle, startingShuffles);
            GameState.GrantItem(ItemIds.Remove, startingRemoves);
            GameState.GrantItem(ItemIds.Hint, startingHints);

            GameState.Data.firstLaunchGrantsApplied = true;
            GameState.MarkDirty();
        }
    }
}
