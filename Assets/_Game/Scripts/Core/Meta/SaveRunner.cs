using UnityEngine;

namespace SheepGame.Core.Meta
{
    /// <summary>
    /// Invisible object that flushes the save. Created on demand by GameState; nothing
    /// needs to place it in a scene.
    /// </summary>
    // Writes are debounced rather than immediate because a single level clear touches the
    // wallet, the inventory and the progress record, and each one alone would be a file
    // write. The important part is not the timer but the pause hook: on a phone the app is
    // very often killed straight out of the background and OnApplicationQuit never runs,
    // so OnApplicationPause(true) is the last moment anything is guaranteed to happen.
    [DefaultExecutionOrder(-100)]
    public class SaveRunner : MonoBehaviour
    {
        [Tooltip("Seconds between background flushes while something is queued.")]
        public float interval = 5f;

        static SaveRunner _instance;
        float _timer;

        public static void Ensure()
        {
            if (_instance != null || !Application.isPlaying) return;

            var go = new GameObject("~SaveRunner") { hideFlags = HideFlags.HideAndDontSave };
            _instance = go.AddComponent<SaveRunner>();
            DontDestroyOnLoad(go);
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        void Update()
        {
            // Unscaled: a paused game with a win panel up is exactly when a save matters.
            _timer += Time.unscaledDeltaTime;
            if (_timer < interval) return;
            _timer = 0f;
            GameState.FlushNow();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) GameState.FlushNow();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) GameState.FlushNow();
        }

        void OnApplicationQuit() => GameState.FlushNow();

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            GameState.FlushNow();
        }
    }
}
