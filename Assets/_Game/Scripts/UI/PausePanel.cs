using UnityEngine;
using UnityEngine.UI;

namespace SheepGame.Core
{
    /// <summary>
    /// Shown when the game is interrupted. Holds the game still until the player says to
    /// carry on.
    /// </summary>
    // It does not resume by itself when the app comes back. The interruption was somebody
    // else's — a call, a notification — and the player is very often not looking at the phone
    // at the moment it ends. Dropping them straight back onto a board they have to read is how
    // a tap gets wasted on a case they never chose.
    public class PausePanel : MonoBehaviour
    {
        public GameManager gameManager;
        public MainMenuPanel mainMenu;

        [Header("Parts")]
        [Tooltip("The overlay switched on and off. Usually this object.")]
        public GameObject root;
        public Button resumeButton;
        public Button restartButton;
        public Button menuButton;

        float _resumeTimeScale = 1f;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            if (root == null) root = gameObject;

            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (menuButton != null) menuButton.onClick.AddListener(ToMenu);
        }

        public void Show()
        {
            if (IsOpen) return;

            // A long press may currently own timeScale. Release that ownership before the
            // pause remembers what speed it should resume to.
            if (gameManager != null) gameManager.CancelHoldSpeedUp();

            // Remembered rather than assumed to be 1: something else may have slowed time,
            // and resuming into the wrong scale is a bug that only shows up in combination.
            _resumeTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            // Nobody is reading a board that is not moving. A phone left face-up on a table
            // with this panel over it would otherwise hold its screen on until the battery
            // went — the never-sleep flag exists for the part of the game being played.
            DeviceRuntime.KeepAwake = false;

            if (root != null) root.SetActive(true);
        }

        public void Resume()
        {
            Time.timeScale = _resumeTimeScale;
            DeviceRuntime.KeepAwake = true;

            // The interruption is over because the player said so. Whatever grace was left on
            // the touch that dismissed the shade has already been spent on this button.
            DeviceRuntime.UnblockInput();

            if (root != null) root.SetActive(false);
        }

        void Restart()
        {
            Resume();
            if (gameManager != null) gameManager.RestartLevel();
        }

        void ToMenu()
        {
            Resume();
            if (mainMenu != null) mainMenu.Show();
        }

        // Time.timeScale is global state that outlives this object: a scene change or a
        // domain reload with the panel up would leave the next scene frozen.
        void OnDisable()
        {
            if (Time.timeScale == 0f) Time.timeScale = _resumeTimeScale;
            DeviceRuntime.KeepAwake = true;
        }
    }
}
