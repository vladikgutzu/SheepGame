using SheepGame.Core.Meta;
using SheepGame.Ads;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// The first screen of the game scene: a title over the hall, the level to resume at,
    /// and the way in.
    /// </summary>
    // It lives in the game scene rather than in a scene of its own so the terminal behind it
    // is the menu's backdrop — a second scene would mean loading the hall twice, once to look
    // at and once to play in. The board is already dealt underneath, so pressing Play reveals
    // a game rather than starting one.
    public class MainMenuPanel : MonoBehaviour
    {
        public GameManager gameManager;
        public ShopPanel shopPanel;
        public SettingsPanel settingsPanel;
        public PrivacyChoicesPanel privacyPanel;

        [Header("Parts")]
        [Tooltip("The overlay switched on and off. Usually this object.")]
        public GameObject root;
        [Tooltip("The in-game HUD, hidden while the menu is up. Boosters and the level card " +
                 "behind a menu read as a screen that has not finished loading.")]
        public GameObject hudRoot;
        public Button playButton;
        public Button shopButton;
        public Button dailyButton;
        public Button settingsButton;
        public TMP_Text levelLabel;

        [Tooltip("{0} is the level the player will resume at.")]
        public string levelFormat = "Level {0}";

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            if (root == null) root = gameObject;

            if (playButton != null) playButton.onClick.AddListener(Play);
            if (shopButton != null) shopButton.onClick.AddListener(OpenShop);
            if (dailyButton != null) dailyButton.onClick.AddListener(PlayDaily);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        }

        void OnEnable()
        {
            GameState.Changed += Refresh;
            if (gameManager != null) gameManager.LevelStateChanged += HandleLevelState;
            Refresh();
        }

        void OnDisable()
        {
            GameState.Changed -= Refresh;
            if (gameManager != null) gameManager.LevelStateChanged -= HandleLevelState;
        }

        void Start()
        {
            // The menu is what the player arrives at, so it opens itself rather than waiting
            // to be opened by something that would have to know to do it.
            Show();
            if (privacyPanel != null && !LevelPlayPrivacy.HasChoice)
                privacyPanel.Open(null);
        }

        public void Show()
        {
            if (root != null) root.SetActive(true);
            if (hudRoot != null) hudRoot.SetActive(false);
            SheepGame.Audio.AudioManager.PlayMenuMusic();
            Refresh();
        }

        public void Play()
        {
            if (root != null) root.SetActive(false);
            if (hudRoot != null) hudRoot.SetActive(true);

            // The level behind this menu was dealt before the player ever pressed anything, so
            // whatever belongs to the start of a round has been waiting for this moment.
            if (gameManager != null) gameManager.BeginPlay();
        }

        /// <summary>
        /// Starts today's board instead of resuming the ladder. Dealt here rather than waiting
        /// on the board already behind the menu, which is the player's own next level.
        /// </summary>
        public void PlayDaily()
        {
            // Shown locked before its level, so a press has to be refused out loud.
            if (gameManager != null && !gameManager.IsUnlocked(UIUnlockGate.Daily))
            {
                var lockable = dailyButton != null ? dailyButton.GetComponentInParent<UILockableButton>() : null;
                if (lockable != null) lockable.Refuse();
                return;
            }

            if (gameManager != null) gameManager.LoadDaily();
            Play();
        }

        void OpenShop()
        {
            if (shopPanel != null) shopPanel.Open();
        }

        void OpenSettings()
        {
            if (settingsPanel != null) settingsPanel.Open();
        }

        void HandleLevelState(int level, int tapsLeft, int budget) => Refresh();

        void Refresh()
        {
            if (levelLabel == null) return;

            // The daily deals under GameState.DayCode(today) as its "level number" — a
            // calendar-date int like 20260907 — and CurrentLevel holds whatever was last
            // dealt, daily included, since SkipLevel and the save-on-complete path both
            // need the real thing that's on the board. It just must never reach this label:
            // once the player has been to the daily, CurrentLevel keeps the day code even
            // back on the menu, and showed as "Level 20260907" until this checked IsDaily.
            // The ladder's own resume point is what GameState.LastLevelPlayed already is,
            // untouched by a daily deal by design.
            int level = gameManager != null && !gameManager.IsDaily && gameManager.CurrentLevel > 0
                ? gameManager.CurrentLevel
                : Mathf.Max(1, GameState.LastLevelPlayed);

            levelLabel.text = string.Format(levelFormat, level);
        }
    }
}
