using SheepGame.Core.Meta;
using SheepGame.Core.Store;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Pauses the game when the system takes the player's attention away — the notification
    /// shade, Control Centre, an incoming call, or the app being backgrounded.
    /// </summary>
    // This lives on the canvas, which is always active, rather than on the pause panel, which
    // is not: OnApplicationPause and OnApplicationFocus only reach enabled behaviours, so a
    // panel that switches itself on would never hear the thing that is supposed to switch it on.
    public class AppLifecycle : MonoBehaviour
    {
        [Tooltip("Shown when the game is interrupted. Leave empty to pause silently.")]
        public PausePanel pausePanel;
        [Tooltip("Menu and shop are not gameplay; an interruption over them needs no pause.")]
        public MainMenuPanel mainMenu;
        public ShopPanel shopPanel;
        public GameManager gameManager;
        [Tooltip("The store, so a purchase is not treated as an interruption. Buying opens a " +
                 "system sheet, which resigns the app — pausing there would put a pause panel " +
                 "over the payment the player is in the middle of.")]
        public StorePurchaser purchaser;

        // iOS distinguishes the two, and both matter here. Backgrounding the app raises
        // OnApplicationPause; pulling the shade or Control Centre down over it only resigns
        // active, which arrives as OnApplicationFocus(false). Handling just the first misses
        // exactly the case a player runs into most.
        void OnApplicationPause(bool paused)
        {
            if (paused) Interrupt();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) Interrupt();
        }

        void Interrupt()
        {
            // The save is the part that must happen whatever else is going on: on a phone the
            // app is very often killed straight out of the background.
            GameState.FlushNow();

            if (!ShouldPause()) return;
            pausePanel.Show();
        }

        bool ShouldPause()
        {
            if (pausePanel == null || pausePanel.IsOpen) return false;

            // A system sheet the player asked for is not an interruption.
            if (purchaser != null && purchaser.Busy) return false;

            // Nothing to come back to: these already hold the game still.
            if (mainMenu != null && mainMenu.IsOpen) return false;
            if (shopPanel != null && shopPanel.IsOpen) return false;
            if (gameManager != null && gameManager.LevelOver) return false;

            return true;
        }
    }
}
