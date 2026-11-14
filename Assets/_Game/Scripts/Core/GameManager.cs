using System;
using System.Collections.Generic;
using SheepGame.Core.Meta;
using TMPro;
using UnityEngine;

namespace SheepGame.Core
{
    public class GameManager : MonoBehaviour
    {
        public Board board;
        public BoosterManager boosterManager;
        [Tooltip("Prices, rewards and the tap budget. Leaving it empty plays the game with " +
                 "no tap limit and no rewards, which is useful for level testing.")]
        public EconomyConfig economy;

        [Header("Authored levels")]
        [Tooltip("Levels drawn in SheepGame > Level Editor. THIS LIST IS THE PLAY ORDER — " +
                 "level 1 is the first entry. Drag to reorder. Past the end of the list " +
                 "the game falls back to random generation.")]
        public List<LevelData> levels = new List<LevelData>();

        [Header("Fallback generation")]
        public int gridWidth = 7;
        public int gridHeight = 10;
        [Tooltip("Share of the Daily Run's pieces asked to be two cells long. Asked, not " +
                 "promised: a long piece needs a free cell behind it as well as a clear run " +
                 "ahead, so a crowded board quietly makes fewer than this. 0 turns them off. " +
                 "The ladder's own generated levels grow their share instead — that ramp is " +
                 "on EconomyConfig — and authored levels say their own sizes.")]
        [Range(0f, 1f)] public float longPieceShare = 0.15f;
        [Tooltip("Pieces in the Daily Run. Fixed rather than growing: the daily is the same " +
                 "size for everyone every day, so it can be compared, and a player deep in " +
                 "the ladder gets no harder a daily than one who started this morning.")]
        [Range(8, 40)] public int dailyPieceCount = 30;
        [Tooltip("Colours on the Daily Run.")]
        [Range(2, 6)] public int dailyColours = 5;
        [Tooltip("What the level readout says during the Daily Run, where a number would " +
                 "name a level the player is not on.")]
        public string dailyLabel = "Daily";

        [Header("Start")]
        [Tooltip("Level a save with no progress begins on.")]
        public int startingLevel = 1;
        [Tooltip("Always start at Starting Level, whatever the save says. Editor convenience — " +
                 "leave it off in a build or the game never resumes.")]
        public bool ignoreSavedProgress;

        [Header("UI")]
        public GameObject winPanel;
        [Tooltip("Shown when the queue overflows, or the clock runs out.")]
        public GameObject losePanel;
        public TMP_Text levelLabel;
        public TMP_Text remainingLabel;
        [Tooltip("Taps left on this attempt.")]
        public TMP_Text tapsLabel;
        [Tooltip("Seconds left during a Time Rush. Its own chip, not the tap counter's slot: " +
                 "one number in one place must not mean two different things.")]
        public TMP_Text timeLabel;
        // The coin balance is not here: it is shown in three places at once and each one
        // carries a UIWalletLabel that keeps itself right. One reference could only feed one.
        [Tooltip("Coins won, on the win panel.")]
        public TMP_Text rewardLabel;
        [Tooltip("Price of the next continue, on the lose panel.")]
        public TMP_Text continuePriceLabel;
        [Tooltip("Optional. The coin beside the continue price, hidden while the continue is " +
                 "paid for by a free rescue instead of coins.")]
        public GameObject continuePriceIcon;
        [Tooltip("Price of the shuffle rescue, on the lose panel.")]
        public TMP_Text shufflePriceLabel;
        // What each rescue hands over is a number out of EconomyConfig, so the buttons read
        // it from there rather than carrying a figure that a balance pass would leave stale.
        [Tooltip("Label on the continue button.")]
        public TMP_Text continueAmountLabel;
        [Tooltip("Label on the shuffle rescue button.")]
        public TMP_Text shuffleAmountLabel;
        [Tooltip("Optional. Heading on the lose panel, which says what ran out.")]
        public TMP_Text loseTitleLabel;
        [Tooltip("Optional. The line under it.")]
        public TMP_Text loseSubtitleLabel;
        [Tooltip("Optional bar filling as the board is cleared. Built by SheepGame > Build Game UI.")]
        public UIProgressBar progressBar;
        [Tooltip("Announces a Time Rush before it starts. The clock waits for it to finish; " +
                 "leave it empty and the clock simply starts at once.")]
        public UIModeBanner modeBanner;
        [Tooltip("Optional. The menu the player arrives at. A level is dealt behind it before " +
                 "Play is ever pressed, so nothing that belongs to a round in progress may " +
                 "start until it closes.")]
        public MainMenuPanel mainMenu;

        [Header("Colours")]
        [Tooltip("The cart and the queue. Built by SheepGame > Add Colour Match HUD.")]
        public UIColourMatchHud colourHud;
        [Tooltip("The hand that shows the first level, and each new booster, where to tap.")]
        public UITutorialHand tutorialHand;
        [Tooltip("The HUD's buttons, for the hand to point at a booster being handed over.")]
        public UIBindings uiBindings;
        [Tooltip("Optional. Plays an interstitial between finishing a level and the next one.")]
        public AdsManager ads;
        [Tooltip("Seconds between the last cart leaving and the win panel, so the player sees it go.")]
        public float winDelay = 0.45f;
        [Tooltip("Seconds between the queue overflowing and the lose panel, so the player sees why.")]
        public float loseDelay = 0.8f;

        [Header("Hold to speed up")]
        [Tooltip("Game speed while the player holds a stationary finger on the playfield.")]
        [Range(1.1f, 3f)] public float holdSpeedMultiplier = 2f;

        // Wording lives here rather than in code because the same GameManager drives
        // both the airport and the pirate scene, which want different words for it.
        [Header("Wording")]
        [Tooltip("{0} is the level number.")]
        public string levelFormat = "Level {0}";
        [Tooltip("{0} is how many pieces are still on the board.")]
        public string remainingFormat = "{0} left";
        [Tooltip("{0} is how many taps are left.")]
        public string tapsFormat = "{0}";
        [Tooltip("{0} is the coins won.")]
        public string rewardFormat = "+{0}";
        [Tooltip("{0} is how many taps the continue hands back.")]
        public string continueFormat = "+{0} taps";
        [Tooltip("{0} is how many taps the shuffle rescue hands back.")]
        public string shuffleRescueFormat = "Shuffle +{0}";
        [Tooltip("{0} is how many places a continue adds to the queue.")]
        public string continueSlotFormat = "+{0} space";
        [Tooltip("{0} is how many free rescues are left. Shown in place of the continue's price.")]
        public string freeContinueFormat = "Free x{0}";
        [Tooltip("{0} is how many places the shuffle rescue adds to the queue.")]
        public string shuffleRescueSlotFormat = "Shuffle +{0}";
        public string queueFullTitle = "No space left!";
        public string queueFullSubtitle = "Make room in the queue and keep going.";
        public string outOfTimeTitle = "Out of time!";
        public string outOfTimeSubtitle = "Take a few more seconds, or start again.";
        [Tooltip("Seconds left, during a Time Rush. {0} is whole seconds.")]
        public string timeFormat = "{0}";
        [Tooltip("Heading on the banner that announces a Time Rush.")]
        public string timeRushTitle = "Time Rush";
        [Tooltip("Line under it. Says what changed, in one breath.")]
        public string timeRushSubtitle = "Beat the clock!";
        [Tooltip("{0} is how many seconds a continue hands back during a Time Rush.")]
        public string continueTimeFormat = "+{0}s";
        [Tooltip("{0} is how many seconds the shuffle rescue hands back during a Time Rush.")]
        public string shuffleRescueTimeFormat = "Shuffle +{0}s";
        public string removeIntroTitle = "New: Remove";
        public string removeIntroSubtitle = "Pull out any case, even a stuck one.";
        public string shuffleIntroTitle = "New: Shuffle";
        public string shuffleIntroSubtitle = "Turn every case a new way.";
        public string hintIntroTitle = "New: Hint";
        public string hintIntroSubtitle = "Shows the best case to tap.";

        /// <summary>Level number, taps left, tap budget. Budget 0 means the limit is off.</summary>
        public event Action<int, int, int> LevelStateChanged;
        public event Action<int> LevelWon;
        public event Action LevelLost;

        /// <summary>Raised when a level starts, saying which rules it runs under.</summary>
        public event Action<LevelMode> ModeChanged;
        /// <summary>Seconds left and the level's whole allowance. Only fires in a Time Rush.</summary>
        public event Action<float, float> TimeChanged;

        int _currentLevel;
        bool _levelOver;
        bool _daily;
        int _levelPieceCount;

        int _tapBudget;
        int _tapsLeft;
        int _continuesUsed;
        // Analytics: a level counts as started when the player is looking at it, not when it
        // is dealt behind the menu; and a lose panel left without a revive is a skipped revive.
        bool _startPending;
        bool _reviveOffered;
        bool _autoHint;

        LevelMode _mode;
        float _timeBudget;
        float _timeLeft;
        // The clock is armed by the level and started by the banner, so an announcement of
        // the clock never runs on the clock it is announcing.
        bool _clockRunning;
        int _lastWholeSecond = -1;
        int _tapsUsed;
        bool _announcePending;
        bool _holdSpeedActive;
        float _holdBaseTimeScale = 1f;

        // ---- colours ----
        LevelData _dealt;
        LevelData _ownedLevel;          // a runtime copy or a generated level, destroyed on the next deal
        ColourMatchRules _rules;
        readonly Dictionary<int, Color> _bagColours = new Dictionary<int, Color>();
        Vector3 _arrivalScreen;
        bool _handTutorial;
        bool _skipping;
        UITerminalTutorial _terminalTutorial;
        public bool TerminalTutorialActive => _terminalTutorial != null && _terminalTutorial.IsOpen;

        enum Ending { None, Win, Lose }
        Ending _ending;
        float _endingAt;
        bool _lostToTime;

        // A booster being handed over: the hand points at its button, then (for Remove) at a
        // case to use it on, then goes.
        string _introItem;
        bool _introPending;
        bool _introPointingAtCase;

        // The level is dealt while the menu is still up -- that is deliberate, it is what lets
        // Play simply get out of the way of a board that is already there. It also means every
        // moving part of a round would otherwise start behind the menu: the announcement played
        // to nobody and the clock ran while the player was reading the title.
        bool PlayHeld => mainMenu != null && mainMenu.IsOpen;

        public int CurrentLevel => _currentLevel;
        public LevelMode Mode => _mode;

        /// <summary>A Time Rush whose announcement has not finished yet: the round has not begun.</summary>
        public bool TimeRushNotStarted => _mode == LevelMode.TimeRush && !_clockRunning && !_levelOver &&
                                          (_announcePending || (modeBanner != null && modeBanner.IsPlaying));
        public float TimeLeft => _timeLeft;
        public float TimeBudget => _timeBudget;
        public int TapsLeft => _tapsLeft;
        public int TapBudget => _tapBudget;
        public bool LevelOver => _levelOver;
        public ColourMatchRules Rules => _rules;
        public int NextContinuePrice =>
            economy != null ? economy.ContinuePrice(_continuesUsed) : 0;
        public int NextShufflePrice =>
            economy != null ? economy.ShuffleRescuePrice(_continuesUsed) : 0;

        void Awake()
        {
            _terminalTutorial = gameObject.AddComponent<UITerminalTutorial>();
            _terminalTutorial.Setup(this);
            board.SetGameManager(this);
            if (boosterManager != null)
            {
                boosterManager.Setup(board);
                boosterManager.hintPriority = HintPriority;
            }

            if (uiBindings == null) uiBindings = FindFirstObjectByType<UIBindings>(FindObjectsInactive.Include);

            // The opening stock has to land before the first HUD refresh, or the booster
            // buttons spend a frame reading zero on a brand new save.
            if (economy != null) economy.ApplyFirstLaunchGrants();
        }

        void OnEnable()
        {
            board.OnBoardEmpty += HandleBoardEmpty;
            board.OnBoardChanged += HandleBoardChanged;
            board.PieceArrived += HandlePieceArrived;
            board.SpeedHoldChanged += HandleSpeedHold;
            GameState.Changed += RefreshWallet;
            if (boosterManager != null)
            {
                boosterManager.Used += HandleBoosterUsed;
                boosterManager.RemoveModeChanged += HandleRemoveMode;
            }
        }

        void OnDisable()
        {
            board.OnBoardEmpty -= HandleBoardEmpty;
            board.OnBoardChanged -= HandleBoardChanged;
            board.PieceArrived -= HandlePieceArrived;
            board.SpeedHoldChanged -= HandleSpeedHold;
            CancelHoldSpeedUp();
            GameState.Changed -= RefreshWallet;
            if (boosterManager != null)
            {
                boosterManager.Used -= HandleBoosterUsed;
                boosterManager.RemoveModeChanged -= HandleRemoveMode;
            }
        }

        void Start()
        {
            LoadLevel(ResolveStartLevel());
            RefreshWallet();
            // The belt scrolls on unscaled time from the moment the scene loads, whether the
            // menu or a round is on screen, so its ambience starts once here and is never
            // stopped.
            SheepGame.Audio.AudioManager.PlayConveyor();
        }

        int ResolveStartLevel()
        {
            if (ignoreSavedProgress) return Mathf.Max(1, startingLevel);
            // A save that has never been played resumes at level 1, which is also what
            // startingLevel says on a fresh project — so the two only disagree deliberately.
            return Mathf.Max(1, GameState.LastLevelPlayed);
        }

        public void LoadLevel(int levelNumber) => Deal(levelNumber, ResolveLevel(levelNumber), daily: false);

        /// <summary>
        /// Today's board. Dealt from the calendar date, so every player has the same one on the
        /// same day and can be asked how they did on it -- and so that closing the game and
        /// coming back returns the board rather than a new one, exactly as the ladder does.
        /// </summary>
        public void LoadDaily()
        {
            var today = System.DateTime.Now;
            var level = LevelGenerator.Generate(dailyPieceCount, gridWidth, gridHeight, 1,
                                                SeedFor(GameState.DayCode(today)), longPieceShare);

            // Taps, always. Time Rush on a daily would make the one board everyone shares also
            // the one board a slow phone loses on.
            level.mode = LevelModeOverride.AlwaysTaps;

            Deal(GameState.DayCode(today), level, daily: true);
        }

        void Deal(int levelNumber, LevelData dealt, bool daily)
        {
            if (_terminalTutorial != null) _terminalTutorial.Cancel();
            // Dealt again straight off the lose panel -- its Replay -- without a revive.
            if (_reviveOffered)
            {
                _reviveOffered = false;
                Telemetry.ReviveSkipped(_currentLevel, _daily);
            }

            _daily = daily;
            _currentLevel = levelNumber;
            _levelOver = false;
            _continuesUsed = 0;
            _ending = Ending.None;
            _lostToTime = false;
            _skipping = false;

            if (winPanel != null) winPanel.SetActive(false);
            if (losePanel != null) losePanel.SetActive(false);
            if (tutorialHand != null) tutorialHand.Hide();
            if (boosterManager != null) boosterManager.SetRemoveMode(false);

            var level = PrepareColours(dealt, levelNumber, daily);
            _dealt = level;
            board.LoadLevel(level);

            // Progress is measured against the level as it was dealt, so it has to be read
            // once the board has spawned rather than taken from the level asset (which may
            // be generated, and is padded into a fixed field).
            _levelPieceCount = board.AllPieces.Count;

            StartRules(level);

            _mode = ResolveMode(level, levelNumber);

            // The queue is the limit now. A tap on a case that cannot move costs nothing but
            // the moment -- the cost of a careless tap is a case parked in the wrong place.
            _tapBudget = 0;
            _tapsLeft = 0;

            _timeBudget = _mode == LevelMode.TimeRush && economy != null
                ? economy.TimeBudget(_levelPieceCount)
                : 0f;
            _timeLeft = _timeBudget;
            _clockRunning = false;
            _lastWholeSecond = -1;
            _tapsUsed = 0;
            _announcePending = false;

            _autoHint = level != null && level.tutorialHints;
            _handTutorial = level != null && level.tutorialHand;

            // The daily is not a rung on the ladder, so it must not move the player along it:
            // finishing it and coming back has to resume where they actually were.
            if (!daily) GameState.SetCurrentLevel(levelNumber);

            if (levelLabel != null)
                levelLabel.text = daily ? dailyLabel : string.Format(levelFormat, levelNumber);
            UpdateRemainingLabel(true);
            ModeChanged?.Invoke(_mode);
            PublishLevelState();
            PublishTime();

            if (modeBanner != null && modeBanner.IsPlaying) modeBanner.Cancel();
            if (!PlayHeld && _terminalTutorial != null) _terminalTutorial.TryShow();
            if (_mode == LevelMode.TimeRush) AnnounceTimeRush();

            PrepareBoosterIntro();

            // The tutorial teaches by pointing rather than by explaining: the first thing on
            // screen is a case swelling, and tapping it works. No text, so nothing to
            // translate and nothing to read on a phone held one-handed.
            if (_autoHint) board.ShowHint(HintPriority);
            RefreshTutorialHand();

            _startPending = true;
            if (!PlayHeld) ReportStart();
        }

        void ReportStart()
        {
            _startPending = false;
            Telemetry.LevelStarted(_currentLevel, _daily);
        }

        // ---- colours ---------------------------------------------------------------------

        // A level with hand-set colours plays as it is. Anything else -- a generated level, the
        // daily, a level drawn before colours existed -- is copied and coloured here, so no
        // asset on disk is ever changed by playing it.
        LevelData PrepareColours(LevelData level, int levelNumber, bool daily)
        {
            if (_ownedLevel != null && _ownedLevel != level)
            {
                Destroy(_ownedLevel);
                _ownedLevel = null;
            }

            if (level == null) return null;

            string problem = "no carts";
            if (level.coloured && level.carts != null && level.carts.Count > 0 &&
                ColourSolver.CountsMatch(level.pieces, level.carts, level.CartCapacity, out problem))
                return level;

            if (level.coloured)
                Debug.LogError($"Level {levelNumber}: colours do not match carts ({problem}). Dealing new ones.");

            // A generated level is already a throwaway instance; an asset has to be copied first.
            bool isAsset = levels != null && levels.Contains(level);
            var copy = isAsset ? Instantiate(level) : level;
            copy.coloured = false;

            int slots = QueueSlotsFor(copy);
            int colours = daily ? dailyColours
                        : economy != null ? economy.ColoursFor(levelNumber, AuthoredLevels) : 5;
            int window = economy != null ? economy.ShuffleWindowFor(levelNumber, AuthoredLevels) : 4;
            int seed = SeedFor(levelNumber) ^ 0x5BD1E995 & 0x7fffffff;

            if (!ColourDealer.Deal(copy, colours, window, slots, seed))
            {
                // Only a level whose facings wedge can get here. Colour it anyway so it plays.
                Debug.LogError($"Level {levelNumber}: could not deal colours; the board may not be solvable.");
                copy.carts = new List<int>();
                int n = copy.pieces.Count - copy.pieces.Count % copy.CartCapacity;
                copy.pieces.RemoveRange(n, copy.pieces.Count - n);
                for (int i = 0; i < n; i++)
                {
                    var e = copy.pieces[i];
                    e.colour = (i / copy.CartCapacity) % Mathf.Max(1, colours);
                    copy.pieces[i] = e;
                    if (i % copy.CartCapacity == 0) copy.carts.Add(e.colour);
                }
                copy.coloured = true;
            }

            _ownedLevel = copy;
            return copy;
        }

        int QueueSlotsFor(LevelData level)
        {
            if (level != null && level.queueSlots > 0) return level.queueSlots;
            return economy != null ? economy.queueSlots : 5;
        }

        void StartRules(LevelData level)
        {
            _bagColours.Clear();
            _rules = null;
            if (level == null || level.carts == null)
            {
                if (board.checkIn != null) board.checkIn.Begin(null, null, 0);
                return;
            }

            _rules = new ColourMatchRules(level.carts, level.CartCapacity, QueueSlotsFor(level));
            _rules.BoardedCart += (bag, cart, seat) =>
            {
                if (colourHud != null) colourHud.BagToCart(bag.Id, ColourOfBag(bag), cart, seat, _arrivalScreen);
            };
            _rules.Queued += (bag, slot) =>
            {
                if (colourHud != null) colourHud.BagToQueue(bag.Id, ColourOfBag(bag), slot, _arrivalScreen);
            };
            _rules.LeftQueueForCart += (bag, cart, seat) =>
            {
                if (colourHud != null) colourHud.QueueToCart(bag.Id, cart, seat);
            };
            _rules.QueueChanged += queue =>
            {
                if (colourHud == null) return;
                var ids = new List<int>(queue.Count);
                foreach (var b in queue) ids.Add(b.Id);
                colourHud.QueueReordered(ids);
            };
            _rules.CartFilled += cart =>
            {
                if (colourHud != null) colourHud.CartFilled(cart);
            };
            _rules.Overflowed += bag =>
            {
                if (colourHud != null) colourHud.Overflow(bag.Id, ColourOfBag(bag), _arrivalScreen);
                BeginEnding(Ending.Lose, loseDelay);
            };
            _rules.Completed += () => BeginEnding(Ending.Win, winDelay);

            var colours = new List<Color>(level.carts.Count);
            foreach (int c in level.carts) colours.Add(board.ColourOf(c));

            if (colourHud != null)
            {
                // The check-in lane draws the cart and the queue in the hall itself.
                colourHud.SetDrawBags(!(board.checkIn != null && board.rideBelt));
                colourHud.Begin(colours, level.CartCapacity, _rules.QueueCapacity);
            }
            if (board.checkIn != null) board.checkIn.Begin(_rules, colours, level.CartCapacity);
        }

        Color ColourOfBag(ColourMatchRules.Bag bag) =>
            _bagColours.TryGetValue(bag.Id, out var c) ? c : board.ColourOf(bag.Colour);

        void HandlePieceArrived(PuzzlePiece piece, Vector3 world)
        {
            if (_rules == null || _skipping || piece == null) return;

            var cam = board.ViewCamera;
            _arrivalScreen = cam != null ? cam.WorldToScreenPoint(world + Vector3.up * board.cellSize * 0.5f)
                                         : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            _bagColours[piece.Id] = board.ColourOf(piece.ColourIndex);

            _rules.Arrive(piece.Id, piece.ColourIndex);
            CheckStuck();
            RefreshTutorialHand();
        }

        // The queue is full and nothing that can move right now would board the cart. Cases
        // still on their way are waited for -- one of them may be the colour that frees it --
        // and an unspent Remove booster is a way out the player still has.
        void CheckStuck()
        {
            if (_rules == null || _levelOver || _ending != Ending.None) return;
            if (_rules.IsComplete || !_rules.QueueFull) return;
            if (board.DepartingCount > 0) return;
            if (board.AnyFreeOfColour(_rules.CurrentColour)) return;

            if (IsUnlocked(ItemIds.Remove) && GameState.ItemCount(ItemIds.Remove) > 0 && board.AllPieces.Count > 0)
            {
                if (tutorialHand != null && uiBindings != null && uiBindings.removeButton != null)
                    tutorialHand.PointAt((RectTransform)uiBindings.removeButton.transform);
                return;
            }

            BeginEnding(Ending.Lose, loseDelay * 0.5f);
        }

        void BeginEnding(Ending ending, float delay)
        {
            if (_ending == Ending.Win) return;
            if (ending == Ending.Lose && _ending == Ending.Lose) return;

            _ending = ending;
            _endingAt = Time.unscaledTime + delay;

            // No more taps once the round is decided, though cases already moving still land.
            _levelOver = true;
            if (tutorialHand != null) tutorialHand.Hide();
            if (boosterManager != null) boosterManager.SetRemoveMode(false);
        }

        void TickEnding()
        {
            if (_ending == Ending.None || Time.unscaledTime < _endingAt) return;

            if (_ending == Ending.Win)
            {
                // Held until the last cart has visibly gone.
                if (colourHud != null && !colourHud.Idle) return;
                if (board.checkIn != null && board.rideBelt && !board.checkIn.Idle) return;
                _ending = Ending.None;
                WinLevel();
            }
            else
            {
                _ending = Ending.None;
                _lostToTime = false;
                FailLevel();
            }
        }

        // What a hint, and the first level's hand, point at: a case that boards the cart now,
        // then one for the cart after, then one the carts still want soonest.
        int HintPriority(PuzzlePiece piece)
        {
            if (_rules == null || _rules.IsComplete) return 0;
            if (piece.ColourIndex == _rules.CurrentColour) return 100;

            var carts = _rules.Carts;
            for (int i = _rules.CartIndex + 1, rank = 90; i < carts.Count && rank > 0; i++, rank -= 10)
                if (carts[i] == piece.ColourIndex) return rank;
            return 0;
        }

        // ---- the hand --------------------------------------------------------------------

        void RefreshTutorialHand()
        {
            if (tutorialHand == null) return;

            if (_levelOver || PlayHeld || TerminalTutorialActive)
            {
                tutorialHand.Hide();
                return;
            }

            if (_introItem != null && !_introPending)
            {
                PointIntro();
                return;
            }

            if (!_handTutorial) return;

            var pick = board.PickFree(p => _rules != null && p.ColourIndex == _rules.CurrentColour ? 1 : 0);
            if (pick != null && _rules != null && pick.ColourIndex != _rules.CurrentColour)
                pick = null;

            if (pick != null) tutorialHand.PointAt(pick.transform, board.ViewCamera, Vector3.up * board.cellSize * 0.55f);
            else tutorialHand.Hide();
        }

        // ---- boosters handed over ----------------------------------------------------------

        /// <summary>Whether a piece of interface is available yet. See UIUnlockGate.</summary>
        public bool IsUnlocked(string key)
        {
            if (economy == null || string.IsNullOrEmpty(key)) return true;

            int reached = Mathf.Max(GameState.HighestLevelReached, _daily ? 0 : _currentLevel);
            return reached >= UnlockLevelOf(key);
        }

        /// <summary>The level a piece of interface opens at, for showing it locked.</summary>
        public int UnlockLevelOf(string key)
        {
            if (economy == null || string.IsNullOrEmpty(key)) return 0;
            return key == UIUnlockGate.Coins ? economy.coinsUnlockLevel
                 : key == UIUnlockGate.Daily ? economy.dailyUnlockLevel
                 : economy.UnlockLevelFor(key);
        }

        // Matches the HUD from left to right, so the tutorial grows in the same direction
        // the player reads the booster row: Hint (6) -> Shuffle (12) -> Remove (15).
        static readonly string[] IntroOrder = { ItemIds.Hint, ItemIds.Shuffle, ItemIds.Remove };

        void PrepareBoosterIntro()
        {
            _introItem = null;
            _introPending = false;
            _introPointingAtCase = false;
            if (economy == null || _daily) return;

            foreach (var item in IntroOrder)
            {
                if (!IsUnlocked(item) || GameState.IntroSeen("unlock." + item)) continue;

                // Handed over the first time it is reached, and on the level built around it.
                GameState.MarkIntroSeen("unlock." + item);
                GameState.GrantItem(item, economy.unlockGrant);
                _introItem = item;
                break;
            }

            if (_introItem == null) return;

            if (PlayHeld || TerminalTutorialActive) _introPending = true;
            else AnnounceIntro();
        }

        void AnnounceIntro()
        {
            _introPending = false;
            if (_introItem == null) return;

            string title = _introItem == ItemIds.Remove ? removeIntroTitle
                         : _introItem == ItemIds.Shuffle ? shuffleIntroTitle : hintIntroTitle;
            string line = _introItem == ItemIds.Remove ? removeIntroSubtitle
                        : _introItem == ItemIds.Shuffle ? shuffleIntroSubtitle : hintIntroSubtitle;

            var button = BoosterButton(_introItem);
            Sprite icon = null;
            if (button != null)
            {
                var images = button.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                foreach (var image in images)
                    if (image.name == "Icon") { icon = image.sprite; break; }
            }

            if (modeBanner != null && !modeBanner.IsPlaying)
            {
                _introPending = true;
                modeBanner.Show(title, line, () =>
                {
                    _introPending = false;
                    RefreshTutorialHand();
                }, icon);
            }
            else RefreshTutorialHand();
        }

        UnityEngine.UI.Button BoosterButton(string item)
        {
            if (uiBindings == null) return null;
            if (item == ItemIds.Remove) return uiBindings.removeButton;
            if (item == ItemIds.Shuffle) return uiBindings.shuffleButton;
            if (item == ItemIds.Hint) return uiBindings.hintButton;
            return null;
        }

        void PointIntro()
        {
            if (_introPointingAtCase)
            {
                // A case the booster is really for: one the cart wants that cannot get out.
                PuzzlePiece target = null;
                foreach (var p in board.AllPieces)
                    if (_rules != null && p.ColourIndex == _rules.CurrentColour && !p.CanEscape) { target = p; break; }
                if (target == null && board.AllPieces.Count > 0) target = board.AllPieces[0];

                if (target != null) tutorialHand.PointAt(target.transform, board.ViewCamera, Vector3.up * board.cellSize * 0.55f);
                else tutorialHand.Hide();
                return;
            }

            var button = BoosterButton(_introItem);
            if (button != null) tutorialHand.PointAt((RectTransform)button.transform);
            else EndIntro();
        }

        void EndIntro()
        {
            _introItem = null;
            _introPointingAtCase = false;
            if (tutorialHand != null) tutorialHand.Hide();
            RefreshTutorialHand();
        }

        void HandleRemoveMode(bool armed)
        {
            if (_introItem != ItemIds.Remove || _introPending) return;
            _introPointingAtCase = armed;
            RefreshTutorialHand();
        }

        void HandleBoosterUsed(string item)
        {
            Telemetry.BoosterUsed(item, _currentLevel, _daily);
            if (item == _introItem) EndIntro();
            UpdateRemainingLabel();
            CheckStuck();
        }

        // ---- levels ------------------------------------------------------------------------

        // Level N is simply the Nth entry of the Levels list — reorder the list to
        // reorder the game. Anything past the end of the list keeps the generator going
        // so the game never runs out.
        LevelData ResolveLevel(int levelNumber)
        {
            int index = levelNumber - 1;
            if (levels != null && index >= 0 && index < levels.Count && levels[index] != null)
                return levels[index];

            // Board spreads the library across the cells itself, so generated levels need
            // no opinion about which model goes where either.
            return LevelGenerator.Generate(PieceCountFor(levelNumber), gridWidth, gridHeight, 1,
                                          seed: SeedFor(levelNumber), longShare: LongShareFor(levelNumber));
        }

        /// <summary>Levels drawn by hand. The generator carries on from the one after this.</summary>
        public int AuthoredLevels => levels != null ? levels.Count : 0;

        /// <summary>How many pieces a generated level of this number is dealt.</summary>
        //
        // Both of these are on EconomyConfig, with the rest of the difficulty ramp, and are
        // reached through here so the sweep that audits generated levels deals exactly what
        // the game deals. A tool that re-derives either for itself is a tool that goes
        // quietly out of step the day one of them changes.
        public int PieceCountFor(int levelNumber) =>
            economy != null
                ? economy.PieceCountFor(levelNumber, gridWidth * gridHeight, AuthoredLevels)
                : Mathf.Clamp(8 + levelNumber * 2, 8, gridWidth * gridHeight);

        /// <summary>Share of a generated level's pieces asked to be two cells long.</summary>
        public float LongShareFor(int levelNumber) =>
            economy != null ? economy.LongShareFor(levelNumber, AuthoredLevels) : longPieceShare;

        // A generated level is dealt from its number, not from chance, so reaching it twice
        // deals the same board: the same one after a loss and Restart, and the same one after
        // the game is closed and opened again. Nothing new has to be written to the save for
        // that -- the level number is already kept there, and the layout follows from it.
        //
        // It was an unseeded System.Random, which meant level 30 was not a level at all but
        // "some thirty-piece board", different on every visit. A player who lost could not
        // apply what the attempt had taught them, and no two players' level 30 were the same
        // board, so neither support nor analytics could say anything about one.
        //
        // The number is scrambled rather than passed straight through, so that neighbouring
        // levels cannot deal near-identical boards, and masked to 31 bits because
        // System.Random takes the absolute value of its seed and int.MinValue has none.
        public static int SeedFor(int levelNumber)
        {
            unchecked
            {
                int h = levelNumber * 73856093;
                h ^= h >> 13;
                h *= 19349663;
                h ^= h >> 16;
                return h & 0x7fffffff;
            }
        }

        // A level says what it wants first; otherwise the schedule in EconomyConfig decides.
        LevelMode ResolveMode(LevelData level, int levelNumber)
        {
            if (level != null && level.mode == LevelModeOverride.AlwaysTimeRush) return LevelMode.TimeRush;
            if (level != null && level.mode == LevelModeOverride.AlwaysTaps) return LevelMode.Taps;

            // A level with the tap limit switched off is the tutorial or something like it,
            // and a clock is the last thing it wants.
            if (level != null && (level.tapBudget < 0 || level.tutorialHand)) return LevelMode.Taps;

            return economy != null && economy.IsTimeRushLevel(levelNumber)
                ? LevelMode.TimeRush
                : LevelMode.Taps;
        }

        void AnnounceTimeRush()
        {
            // Held until the player is actually looking at the board. BeginPlay lets it go.
            if (PlayHeld || TerminalTutorialActive)
            {
                _announcePending = true;
                return;
            }

            if (modeBanner == null)
            {
                _clockRunning = true;
                return;
            }

            modeBanner.Show(timeRushTitle, timeRushSubtitle, () =>
            {
                // Only if the player is still on the level the banner was about: a restart
                // during the announcement would otherwise start a clock for a dead round.
                if (!_levelOver && _mode == LevelMode.TimeRush) _clockRunning = true;
            });
        }

        /// <summary>
        /// The player has left the menu and is looking at the board. Plays anything the
        /// opening of the level put off.
        /// </summary>
        public void BeginPlay()
        {
            SheepGame.Audio.AudioManager.PlayGameplayMusic();

            if (_startPending) ReportStart();
            if (_terminalTutorial != null && _terminalTutorial.TryShow()) return;

            if (_announcePending)
            {
                _announcePending = false;
                if (_mode == LevelMode.TimeRush && !_levelOver) AnnounceTimeRush();
            }

            if (_introPending && _introItem != null) AnnounceIntro();
            RefreshTutorialHand();
        }

        void Update()
        {
            if (_holdSpeedActive && !CanUseHoldSpeed()) CancelHoldSpeedUp();
            if (TerminalTutorialActive) return;
            TickEnding();

            if (!_clockRunning || _levelOver || _mode != LevelMode.TimeRush) return;

            // Going back to the menu mid-round holds the clock. It does not touch timeScale --
            // the board behind it goes on animating -- so without this the round would drain
            // while the player sat on the title screen.
            if (PlayHeld) return;

            // Scaled, so a pause really does stop the clock. The banner runs unscaled
            // instead, because it plays over a board that is being held still.
            _timeLeft -= Time.deltaTime;

            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                _clockRunning = false;
                PublishTime();
                _lostToTime = true;
                FailLevel();
                return;
            }

            PublishTime();
        }

        void HandleSpeedHold(bool held)
        {
            if (!held)
            {
                CancelHoldSpeedUp();
                return;
            }

            if (_holdSpeedActive || !CanUseHoldSpeed()) return;

            _holdBaseTimeScale = Mathf.Max(0.01f, Time.timeScale);
            Time.timeScale = _holdBaseTimeScale * Mathf.Max(1f, holdSpeedMultiplier);
            _holdSpeedActive = true;
        }

        bool CanUseHoldSpeed()
        {
            if (_levelOver || _ending != Ending.None || _startPending || PlayHeld || TerminalTutorialActive)
                return false;
            if (modeBanner != null && modeBanner.IsPlaying) return false;
            return Time.timeScale > 0f;
        }

        /// <summary>Returns global time to normal before a pause or overlay takes ownership.</summary>
        public void CancelHoldSpeedUp()
        {
            if (!_holdSpeedActive) return;
            _holdSpeedActive = false;
            if (Time.timeScale > 0f) Time.timeScale = _holdBaseTimeScale;
        }

        // Published on the whole second rather than every frame: the label only ever shows
        // whole seconds, and the meter pops on every change it is told about.
        void PublishTime()
        {
            if (_mode != LevelMode.TimeRush)
            {
                _lastWholeSecond = -1;
                return;
            }

            int whole = Mathf.CeilToInt(_timeLeft);
            if (whole == _lastWholeSecond) return;
            _lastWholeSecond = whole;

            if (timeLabel != null) timeLabel.text = string.Format(timeFormat, whole);
            TimeChanged?.Invoke(_timeLeft, _timeBudget);
        }

        public void OnPieceClicked(PuzzlePiece piece)
        {
            if (_terminalTutorial != null && _terminalTutorial.BlocksInput) return;
            if (_levelOver) return;

            // A Time Rush starts when its announcement ends, not before: no case leaves while
            // the banner is up or still waiting for the menu to close. The banner's dim already
            // covers the board, but a touch is not always seen as over UI, so the rule is here.
            if (TimeRushNotStarted) return;

            // A booster tap is not a guess, so it never costs a tap from the budget —
            // otherwise spending a booster to survive would cost the very thing it is
            // bought to save.
            if (boosterManager != null && boosterManager.ConsumeRemoveModeForClick(piece))
            {
                SheepGame.Audio.AudioManager.Tap();
                Haptics.Impact(Haptics.ImpactStyle.Medium);
                UpdateRemainingLabel();
                return;
            }

            if (_tapBudget > 0 && _tapsLeft <= 0) return;

            SpendTap();

            // The two answers differ in the hand as well as on screen. A light knock for a
            // case that left, a rigid one for a case that could not: the difference is felt
            // before the nudge animation has finished, which is the point of it.
            if (board.TryEscape(piece))
            {
                SheepGame.Audio.AudioManager.Tap();
                Haptics.Impact(Haptics.ImpactStyle.Light);
            }
            else
            {
                piece.PlayBlockedNudge();
                Haptics.Impact(Haptics.ImpactStyle.Rigid);
            }

            UpdateRemainingLabel();

            if (!_levelOver && _tapBudget > 0 && _tapsLeft <= 0) FailLevel();

            // Board.RemovePieceInternal drops the hint on every removal, so the tutorial has
            // to put the next one up itself. It points again after a wrong tap too — that is
            // the moment the lesson is needed.
            if (!_levelOver && _autoHint) board.ShowHint(HintPriority);
            RefreshTutorialHand();
        }

        void SpendTap()
        {
            // Counted before the limit is consulted, because a level without a limit still
            // spends taps -- and what goes in the save as this level's best score is the
            // number spent, not the number left. Without this a Time Rush would record a
            // perfect zero for every clear.
            _tapsUsed++;

            if (_tapBudget <= 0) return;
            _tapsLeft = Mathf.Max(0, _tapsLeft - 1);
            PublishLevelState();
        }

        /// <summary>Buys a rescue with coins after a loss. The price climbs per continue.</summary>
        public bool TryContinueWithCoins()
        {
            if (economy == null || !_levelOver || _ending != Ending.None) return false;

            int price = economy.ContinuePrice(_continuesUsed);
            if (!GameState.TrySpendCoins(price))
            {
                Telemetry.ReviveNotEnoughMoney(_currentLevel, _daily, Telemetry.ReviveMethod.Coins);
                return false;
            }

            _continuesUsed++;
            ReportRevive(Telemetry.ReviveMethod.Coins);
            GrantRescue(economy.extraTapsAmount, economy.extraSecondsAmount, economy.extraSlotsAmount);
            return true;
        }

        /// <summary>
        /// The second rescue: pays coins, turns every remaining piece to a new facing and
        /// hands back a little of whatever ran out.
        /// </summary>
        // Worth having alongside the plain top-up because the two fix different things. Room
        // forgives the mistakes already made; a reshuffle changes how many of the next ones
        // there will be. Board.ShuffleRemaining assigns facings through LevelSolver, so the
        // rescue can never hand back a board that cannot be finished.
        public bool TryShuffleRescueWithCoins()
        {
            if (economy == null || !_levelOver || _ending != Ending.None) return false;

            int price = economy.ShuffleRescuePrice(_continuesUsed);
            if (!GameState.TrySpendCoins(price))
            {
                Telemetry.ReviveNotEnoughMoney(_currentLevel, _daily, Telemetry.ReviveMethod.CoinsShuffle);
                return false;
            }

            _continuesUsed++;
            ReportRevive(Telemetry.ReviveMethod.CoinsShuffle);
            board.ShuffleRemaining();
            GrantRescue(economy.shuffleRescueTaps, economy.extraSecondsAmount, economy.extraSlotsAmount);
            return true;
        }

        /// <summary>Spends an Extra Taps booster instead of coins. Same effect, no price ramp.</summary>
        public bool TryContinueWithBooster()
        {
            if (economy == null || !_levelOver || _ending != Ending.None) return false;
            if (!GameState.TryConsumeItem(ItemIds.ExtraTaps)) return false;

            ReportRevive(Telemetry.ReviveMethod.ExtraTaps);
            GrantRescue(economy.extraTapsAmount, economy.extraSecondsAmount, economy.extraSlotsAmount);
            return true;
        }

        void ReportRevive(Telemetry.ReviveMethod method)
        {
            _reviveOffered = false;
            Telemetry.Revived(_currentLevel, _daily, method);
        }

        // One entry point for every rescue, because what a rescue hands back depends on what
        // ran out. Handing room to a player who ran out of time would be charging for nothing.
        void GrantRescue(int taps, int seconds, int slots)
        {
            _levelOver = false;
            if (losePanel != null) losePanel.SetActive(false);

            if (_lostToTime && _mode == LevelMode.TimeRush)
            {
                _lostToTime = false;
                _timeLeft += Mathf.Max(1, seconds);
                _timeBudget = Mathf.Max(_timeBudget, _timeLeft);
                _lastWholeSecond = -1;
                _clockRunning = true;
                PublishTime();
                return;
            }

            if (_rules != null)
            {
                int cap = economy != null ? economy.maxQueueSlots : 7;
                // At least one place per case left waiting with nowhere to go. Cases are still
                // riding the belt when the queue fills, so a second one can overflow before the
                // lose panel is even up -- and one place for two left the second stuck at the
                // lane entry for good, blocking everything behind it.
                int widest = Mathf.Max(cap, _rules.QueueCapacity + _rules.OverflowCount);
                // Never more places than the check-in sorter has bays: a case queued with no bay to
                // go to waits at the sorter's entry and blocks every case behind it.
                if (board.checkIn != null && board.rideBelt && board.checkIn.PlaceCount > 0)
                    widest = Mathf.Min(widest, board.checkIn.PlaceCount);
                int add = Mathf.Clamp(Mathf.Max(1, Mathf.Max(slots, _rules.OverflowCount)), 0,
                                      Mathf.Max(0, widest - _rules.QueueCapacity));
                // At the widest queue the HUD can draw, a continue still has to buy something:
                // the cases that overflowed are taken off the player's hands instead.
                if (add > 0) _rules.AddQueueSlots(add);
                if (colourHud != null) colourHud.SetQueueCapacity(_rules.QueueCapacity);
                if (_mode == LevelMode.TimeRush) _clockRunning = true;
                CheckStuck();
                RefreshTutorialHand();
                return;
            }

            _tapsLeft += Mathf.Max(1, taps);
            PublishLevelState();
        }

        void UpdateRemainingLabel(bool instant = false)
        {
            int remaining = board.AllPieces.Count;

            if (remainingLabel != null)
                remainingLabel.text = string.Format(remainingFormat, remaining);

            if (progressBar != null)
            {
                float cleared = _levelPieceCount <= 0
                    ? 0f
                    : 1f - remaining / (float)_levelPieceCount;
                progressBar.SetProgress(cleared, instant);
            }
        }

        void PublishLevelState()
        {
            if (tapsLabel != null)
                tapsLabel.text = _tapBudget > 0 ? string.Format(tapsFormat, _tapsLeft) : string.Empty;

            LevelStateChanged?.Invoke(_currentLevel, _tapsLeft, _tapBudget);
        }

        void RefreshWallet()
        {
            // A held rescue is spent before any coins (UIBindings.Continue), so while there is
            // one the button has to say it is free -- a coin price on it would be a price the
            // player is never charged, and would hide that the rescues exist at all.
            int free = GameState.ItemCount(ItemIds.ExtraTaps);
            if (continuePriceLabel != null)
                continuePriceLabel.text = free > 0 ? string.Format(freeContinueFormat, free)
                                                   : NextContinuePrice.ToString();
            if (continuePriceIcon != null) continuePriceIcon.SetActive(free <= 0);
            if (shufflePriceLabel != null) shufflePriceLabel.text = NextShufflePrice.ToString();

            if (economy == null) return;

            // The lose panel is the same panel whatever ran out, so its buttons have to name
            // what they hand back. A button promising room to a player who ran out of time is
            // charging them for something they cannot use.
            bool time = _lostToTime && _mode == LevelMode.TimeRush;
            bool room = !time && _rules != null;

            if (continueAmountLabel != null)
                continueAmountLabel.text = time ? string.Format(continueTimeFormat, economy.extraSecondsAmount)
                                         : room ? string.Format(continueSlotFormat, economy.extraSlotsAmount)
                                                : string.Format(continueFormat, economy.extraTapsAmount);

            if (shuffleAmountLabel != null)
                shuffleAmountLabel.text = time ? string.Format(shuffleRescueTimeFormat, economy.extraSecondsAmount)
                                        : room ? string.Format(shuffleRescueSlotFormat, economy.extraSlotsAmount)
                                               : string.Format(shuffleRescueFormat, economy.shuffleRescueTaps);

            if (loseTitleLabel != null) loseTitleLabel.text = time ? outOfTimeTitle : queueFullTitle;
            if (loseSubtitleLabel != null) loseSubtitleLabel.text = time ? outOfTimeSubtitle : queueFullSubtitle;
        }

        void HandleBoardChanged() => UpdateRemainingLabel();

        // Only the old rules win on an empty board. With carts, the board empties a moment
        // before the last case lands, and the win is the rules' to call.
        void HandleBoardEmpty()
        {
            if (_rules != null) return;
            if (_levelOver) return;
            WinLevel();
        }

        void WinLevel()
        {
            _levelOver = true;
            _clockRunning = false;
            if (tutorialHand != null) tutorialHand.Hide();

            int reward = 0;
            if (economy != null)
            {
                bool firstClear = _daily
                    ? !GameState.DailyClearedOn(System.DateTime.Now)
                    : !GameState.IsLevelCleared(_currentLevel);
                // Paid in the currency the level was played in: seconds saved on a Time Rush,
                // taps saved everywhere else. Otherwise a Time Rush -- which has no taps to
                // leave over -- would always pay the bare minimum for clearing it.
                reward = _mode == LevelMode.TimeRush
                    ? economy.TimeRushReward(_timeLeft, firstClear)
                    : economy.ClearReward(_tapsLeft, firstClear);
                GameState.AddCoins(reward);
            }

            int tapsUsed = Mathf.Max(1, _tapsUsed);
            if (_daily) GameState.RecordDailyClear(System.DateTime.Now);
            else GameState.RecordLevelComplete(_currentLevel, tapsUsed);
            // A clear is the one moment worth spending a disk write on immediately:
            // players close the game on the win screen.
            GameState.FlushNow();

            Telemetry.LevelCompleted(_currentLevel, _daily);

            if (rewardLabel != null) rewardLabel.text = string.Format(rewardFormat, reward);
            if (winPanel != null) winPanel.SetActive(true);
            SheepGame.Audio.AudioManager.Win();
            Haptics.Notice(Haptics.NoticeKind.Success);
            LevelWon?.Invoke(reward);
        }

        void FailLevel()
        {
            _levelOver = true;
            _clockRunning = false;
            if (boosterManager != null) boosterManager.SetRemoveMode(false);
            if (tutorialHand != null) tutorialHand.Hide();

            // Fail is sent each time the lose panel comes up, a revive after it or not: it is
            // the moment the level beat the player. The panel is the revive offer.
            Telemetry.LevelFailed(_currentLevel, _daily);
            Telemetry.LostLevel(_daily ? GameState.LastLevelPlayed : _currentLevel);
            Telemetry.RevivePopupShown(_currentLevel, _daily);
            _reviveOffered = true;

            RefreshWallet();
            if (losePanel != null) losePanel.SetActive(true);
            SheepGame.Audio.AudioManager.Lose();
            Haptics.Notice(Haptics.NoticeKind.Error);
            LevelLost?.Invoke();
        }

        /// <summary>
        /// Clears the level outright, as though the player had played it well.
        /// </summary>
        // A testing shortcut, for reaching the level after the one being worked on. It runs
        // through the ordinary clear rather than around it, so the win panel, the reward,
        // the unlock and the save all behave exactly as they would have.
        public void SkipLevel()
        {
            if (board == null || _levelOver) return;

            // The cases leave as usual but the cart and the queue are not asked about them:
            // this is a way past the level, not a way to lose it.
            _skipping = true;

            for (int guard = board.AllPieces.Count * 2; guard > 0 && board.AllPieces.Count > 0; guard--)
            {
                PuzzlePiece free = null;
                foreach (var piece in board.AllPieces)
                    if (piece.CanEscape) { free = piece; break; }

                SpendTap();
                if (free != null) board.TryEscape(free);
                else board.ForceRemove(board.AllPieces[0]);
            }

            UpdateRemainingLabel();
            if (colourHud != null) colourHud.Clear();
            if (!_levelOver) WinLevel();
        }

        public void RestartLevel()
        {
            if (_daily) LoadDaily();
            else LoadLevel(_currentLevel);
        }

        public void NextLevel()
        {
            // There is no level after today's. Sending the player back to the menu is the only
            // honest answer, and it is where the daily was started from.
            if (_daily)
            {
                if (mainMenu != null) mainMenu.Show();
                return;
            }

            int completed = _currentLevel;
            if (winPanel != null) winPanel.SetActive(false);

            if (ads != null) ads.AfterLevel(completed, () => LoadLevel(completed + 1));
            else LoadLevel(completed + 1);
        }

        /// <summary>Whether the board on screen is today's daily rather than a rung of the ladder.</summary>
        public bool IsDaily => _daily;
    }
}
