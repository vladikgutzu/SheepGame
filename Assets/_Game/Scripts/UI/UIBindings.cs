using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// The single place every HUD button is wired to what it does. Built and rewired by
    /// SheepGame > Build Game UI.
    /// </summary>
    public class UIBindings : MonoBehaviour
    {
        public GameManager gameManager;
        public BoosterManager boosterManager;
        [Tooltip("Opened by the coin pill, by an empty booster and by a continue the " +
                 "player cannot afford.")]
        public ShopPanel shopPanel;

        [Tooltip("Opened by the pause button. The same panel the system raises on an " +
                 "interruption, so there is one place a held game can be resumed from.")]
        public PausePanel pausePanel;

        [Header("HUD")]
        [Tooltip("Stops the game deliberately. Restart lives inside the panel it opens: the " +
                 "top row of the HUD holds one button, and pausing is the one a player needs " +
                 "at a moment they did not plan for.")]
        public Button pauseButton;
        [Tooltip("Optional. The HUD no longer carries one — the win, lose and pause panels " +
                 "each have their own — but the wiring stays for a layout that wants it back.")]
        public Button restartButton;
        public Button shuffleButton;
        public Button removeButton;
        public Button hintButton;
        [Tooltip("The coin balance itself is the way into the shop.")]
        public Button coinsButton;
        [Tooltip("Clears the board outright, for reaching a later level without playing " +
                 "the ones before it. A testing shortcut and not a feature: it switches " +
                 "itself off in anything but the editor and a development build.")]
        public Button skipLevelButton;

        [Header("Win panel")]
        public Button nextLevelButton;
        public Button winRestartButton;

        [Header("Lose panel")]
        [Tooltip("Buys more taps with coins.")]
        public Button continueButton;
        [Tooltip("Buys a reshuffle of every remaining facing, plus a few taps.")]
        public Button shuffleRescueButton;
        public Button loseRestartButton;
        public Button loseShopButton;

        [Header("Remove mode")]
        [Tooltip("Ring shown around the remove booster while it is armed.")]
        public GameObject removeArmedRing;
        [Tooltip("Chip telling the player what an armed remove booster is waiting for.")]
        public GameObject removeArmedHint;

        void Awake()
        {
            PrepareRemoveArmedRing();
        }

        // The button face has a stencil Mask for its moving shine. The armed ring is wider
        // than that face, so leaving it under the button clips its pulse exactly at the edge.
        // Keep it in the button slot instead: same centre and press area, but rendered above
        // the masked face and allowed to breathe to its full size.
        void PrepareRemoveArmedRing()
        {
            if (removeArmedRing == null || removeButton == null) return;

            var slot = removeButton.transform.parent as RectTransform;
            var ring = removeArmedRing.transform as RectTransform;
            if (slot == null || ring == null) return;

            if (ring.parent != slot) ring.SetParent(slot, false);
            ring.anchorMin = ring.anchorMax = ring.pivot = new Vector2(0.5f, 0.5f);
            ring.anchoredPosition = Vector2.zero;
            ring.SetAsLastSibling();

            var graphic = ring.GetComponent<UnityEngine.UI.Graphic>();
            if (graphic != null) graphic.raycastTarget = false;
        }

        void Start()
        {
            if (restartButton != null) restartButton.onClick.AddListener(() => gameManager.RestartLevel());
            if (pauseButton != null) pauseButton.onClick.AddListener(Pause);
            if (nextLevelButton != null) nextLevelButton.onClick.AddListener(() => gameManager.NextLevel());
            if (winRestartButton != null) winRestartButton.onClick.AddListener(() => gameManager.RestartLevel());
            if (loseRestartButton != null) loseRestartButton.onClick.AddListener(() => gameManager.RestartLevel());

            if (shuffleButton != null) shuffleButton.onClick.AddListener(() => boosterManager.ActivateShuffle());
            if (removeButton != null) removeButton.onClick.AddListener(() => boosterManager.ToggleRemoveMode());
            if (hintButton != null) hintButton.onClick.AddListener(() => boosterManager.ActivateHint());

            if (skipLevelButton != null)
            {
                // The whole slot goes, not just the listener: a button left on the HUD that
                // answers nothing is worse than no button at all.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                skipLevelButton.onClick.AddListener(() => gameManager.SkipLevel());
#else
                skipLevelButton.transform.parent.gameObject.SetActive(false);
#endif
            }

            if (coinsButton != null) coinsButton.onClick.AddListener(OpenShop);
            if (loseShopButton != null) loseShopButton.onClick.AddListener(OpenShop);
            if (continueButton != null) continueButton.onClick.AddListener(Continue);
            if (shuffleRescueButton != null) shuffleRescueButton.onClick.AddListener(ShuffleRescue);

            ShowRemoveArmed(boosterManager != null && boosterManager.RemoveModeActive);
        }

        void OnEnable()
        {
            if (boosterManager != null)
            {
                boosterManager.RemoveModeChanged += ShowRemoveArmed;
                boosterManager.OutOfStock += HandleOutOfStock;
            }
        }

        void OnDisable()
        {
            if (boosterManager != null)
            {
                boosterManager.RemoveModeChanged -= ShowRemoveArmed;
                boosterManager.OutOfStock -= HandleOutOfStock;
            }
        }

        // Arming a booster changes what the next tap on the board does, so it has to be
        // visible on the button itself — otherwise a tap that deletes a piece looks like a bug.
        void ShowRemoveArmed(bool armed)
        {
            if (removeArmedRing != null) removeArmedRing.SetActive(armed);
            if (removeArmedHint != null) removeArmedHint.SetActive(armed);
        }

        // Pressing a booster you have none of has to lead somewhere. A button that simply
        // does nothing is indistinguishable from a broken one, and this is the moment the
        // player is most interested in having more of it.
        void HandleOutOfStock(string itemId) => OpenShop();

        void OpenShop()
        {
            if (shopPanel != null) shopPanel.Open();
        }

        // Nothing to pause over a panel that is already holding the game still, and a pause
        // over the shop would put two overlays on top of each other with only one way out.
        void Pause()
        {
            if (pausePanel == null || pausePanel.IsOpen) return;
            if (shopPanel != null && shopPanel.IsOpen) return;
            if (gameManager != null && gameManager.LevelOver) return;

            pausePanel.Show();
        }

        // Continuing is a coin purchase, so failing it means the player is short — which
        // is exactly when the shop is worth opening rather than a silent no-op.
        void Continue()
        {
            if (gameManager == null) return;

            if (gameManager.TryContinueWithBooster()) return;
            if (gameManager.TryContinueWithCoins()) return;

            OpenShop();
        }

        void ShuffleRescue()
        {
            if (gameManager == null) return;
            if (gameManager.TryShuffleRescueWithCoins()) return;

            OpenShop();
        }
    }
}
