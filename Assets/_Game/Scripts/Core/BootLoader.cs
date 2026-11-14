using System.Collections;
using SheepGame.Core.Meta;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SheepGame.Core
{
    /// <summary>
    /// The whole of the boot scene: warms the save, loads the game scene in the background
    /// and shows how far along it is.
    /// </summary>
    // A separate scene exists because the game scene is not small — a generated hall, the
    // belt dressing, the effects and the HUD — and loading it on a phone is a visible pause.
    // Without a boot scene that pause is a black screen with nothing to say for itself.
    public class BootLoader : MonoBehaviour
    {
        [Tooltip("Scene to hand over to. Must be in Build Settings.")]
        public string gameSceneName = "TerminalSandbox";

        [Header("Parts")]
        public UIProgressBar progressBar;
        public TMP_Text percentLabel;
        [Tooltip("{0} is the percentage, 0-100.")]
        public string percentFormat = "{0}%";

        [Header("Timing")]
        [Tooltip("Shortest time the loading screen stays up. A screen that flashes past on a " +
                 "fast machine reads as a glitch rather than as loading.")]
        public float minimumSeconds = 1.2f;
        [Tooltip("Beat to hold once the bar is full, so it is seen to finish.")]
        public float holdWhenDone = 0.2f;

        void Start()
        {
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            // Reading the save here rather than in the game scene means the first frame of the
            // menu already knows the balance and the level — nothing pops in a moment later.
            // The file read is the point; the value is not wanted here.
            _ = GameState.Data;
            Report(0f);

            if (string.IsNullOrWhiteSpace(gameSceneName))
            {
                Debug.LogError("[Boot] No game scene set; the boot screen has nowhere to go.");
                yield break;
            }

            var load = SceneManager.LoadSceneAsync(gameSceneName);
            if (load == null)
            {
                Debug.LogError($"[Boot] '{gameSceneName}' is not in Build Settings.");
                yield break;
            }

            // Held back so the bar can finish and be seen to finish, rather than the scene
            // swapping under it at 90%.
            load.allowSceneActivation = false;

            float started = Time.unscaledTime;
            while (true)
            {
                // Unity runs 0..0.9 while loading and parks at 0.9 waiting for activation, so
                // the raw figure has to be rescaled or the bar never fills.
                float loaded = Mathf.Clamp01(load.progress / 0.9f);
                float elapsed = minimumSeconds <= 0f
                    ? 1f
                    : Mathf.Clamp01((Time.unscaledTime - started) / minimumSeconds);

                // The lower of the two: the bar neither races ahead of the real work nor
                // claims to be done before the screen has been up long enough to read.
                Report(Mathf.Min(loaded, elapsed));

                if (loaded >= 1f && elapsed >= 1f) break;
                yield return null;
            }

            Report(1f);
            if (holdWhenDone > 0f) yield return new WaitForSecondsRealtime(holdWhenDone);

            load.allowSceneActivation = true;
        }

        void Report(float value)
        {
            if (progressBar != null) progressBar.SetProgress(value, instant: true);
            if (percentLabel != null)
                percentLabel.text = string.Format(percentFormat, Mathf.RoundToInt(value * 100f));
        }
    }
}
