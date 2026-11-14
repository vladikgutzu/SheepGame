using SheepGame.Audio;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Wires the game's one AudioManager to the clips dropped in Assets/_Game/Audio. Find-or-
    // create rather than destroy-and-rebuild, so re-running it after a new clip is added
    // never disturbs the volume sliders the user has already tuned.
    public static class AudioManagerBuilder
    {
        const string AudioDir = "Assets/_Game/Audio";
        const string TrimmedDir = "Assets/_Game/Audio/Trimmed";

        [MenuItem("SheepGame/Build Audio Manager")]
        public static void Build()
        {
            var manager = Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                var root = new GameObject("AudioManager");
                var parent = GameObject.Find("MainSystemn");
                if (parent != null) root.transform.SetParent(parent.transform, false);
                manager = root.AddComponent<AudioManager>();
            }

            // Music and the belt loop play straight from the source mp3 -- a fade-in start
            // does not matter for a bed that is already running before anyone is listening
            // for its first sample. The one-shot SFX below load the trimmed copies: several
            // of the source clips carry real, audible leading silence (up to 245ms), which
            // read as a delayed sound even though it plays the instant it is called. See
            // AudioTrimBuilder -- run "SheepGame > Trim SFX Leading Silence" first if the
            // Trimmed folder is missing or a source clip has changed.
            manager.menuMusic = LoadSource("bgMusic_1");
            manager.gameplayMusic = LoadSource("bgMusic_2");
            manager.conveyorLoop = LoadSource("conveyr");

            manager.tap = LoadTrimmed("click_1");
            manager.blockedTap = LoadTrimmed("click2");
            manager.pieceLeave = LoadTrimmed("jumpOnConveer");
            manager.pieceLand = LoadTrimmed("landOnConveer");
            manager.win = LoadTrimmed("success");
            manager.lose = LoadTrimmed("fail_1");
            manager.panelClose = LoadTrimmed("close");
            manager.modeSwoosh = LoadTrimmed("swoosh");
            manager.timerTick = LoadTrimmed("tickking");

            EditorUtility.SetDirty(manager);
            Selection.activeGameObject = manager.gameObject;
            Debug.Log("Audio manager wired to Assets/_Game/Audio.");
        }

        static AudioClip LoadSource(string fileName)
        {
            string path = $"{AudioDir}/{fileName}.mp3";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"AudioManagerBuilder: no clip at {path}");
            return clip;
        }

        static AudioClip LoadTrimmed(string fileName)
        {
            string path = $"{TrimmedDir}/{fileName}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) return clip;

            Debug.LogWarning($"AudioManagerBuilder: no trimmed clip at {path} -- " +
                              "run SheepGame > Trim SFX Leading Silence first. Falling back to the source mp3.");
            return LoadSource(fileName);
        }
    }
}
