#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using SheepGame.Core;
using SheepGame.Core.Meta;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SheepGame.EditorTools
{
    /// <summary>
    /// Records a vertical, automatically played Baggage Jam clip directly from Play Mode.
    /// The tool is intentionally opt-in and only exists in the Editor.
    /// </summary>
    public sealed class BaggageJamContentCaptureAutomation : MonoBehaviour
    {
        const string ArmedKey = "SpikyGames.BaggageJam.ContentCapture.Armed";
        const string LevelKey = "SpikyGames.BaggageJam.ContentCapture.Level";
        const string ScenarioKey = "SpikyGames.BaggageJam.ContentCapture.Scenario";

        RecorderController _recorder;
        GameManager _game;
        Board _board;
        int _previousPieceCount = -1;
        float _lastMoveAt;
        float _startedAt;
        float _recordingStartedAt;
        int _scenario;
        ContentCaptureOverlay _overlay;
        bool _stopping;
        bool _capturedIntroReview;
        bool _capturedGameplayReview;
        bool _capturedOutroReview;
        int _oneSpaceStage;
        float _oneSpaceStageAt;
        bool _audioOverridesApplied;
        bool _previousSoundOn;
        bool _previousMusicOn;
        bool _previousAudioPause;

        [MenuItem("SheepGame/Content Capture/Record Vertical Autoplay Clip")]
        static void RecordVerticalClip()
        {
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetInt(LevelKey, 12);
            SessionState.SetInt(ScenarioKey, 0);

            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
        }

        [MenuItem("SheepGame/Content Capture/Record Scenario 02 - Look At The Arrows")]
        static void RecordLookAtTheArrows()
        {
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetInt(LevelKey, 13);
            SessionState.SetInt(ScenarioKey, 2);

            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
        }

        [MenuItem("SheepGame/Content Capture/Record Scenario 04 - The Perfect Flow")]
        static void RecordPerfectFlow()
        {
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetInt(LevelKey, 13);
            SessionState.SetInt(ScenarioKey, 4);

            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
        }

        [MenuItem("SheepGame/Content Capture/Record Scenario 05 - One Space Left (No Text)")]
        static void RecordOneSpaceLeft()
        {
            SessionState.SetBool(ArmedKey, true);
            SessionState.SetInt(LevelKey, 13);
            SessionState.SetInt(ScenarioKey, 5);

            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!SessionState.GetBool(ArmedKey, false)) return;
            if (FindAnyObjectByType<BaggageJamContentCaptureAutomation>() != null) return;

            var go = new GameObject("BaggageJam Content Capture");
            DontDestroyOnLoad(go);
            go.AddComponent<BaggageJamContentCaptureAutomation>();
        }

        IEnumerator Start()
        {
            _startedAt = Time.realtimeSinceStartup;
            Application.targetFrameRate = 60;

            yield return WaitForGame();
            if (_game == null || _board == null)
            {
                Debug.LogError("[ContentCapture] GameManager or Board was not found.");
                StopCapture();
                yield break;
            }

            int level = Mathf.Max(1, SessionState.GetInt(LevelKey, 12));
            _scenario = Mathf.Max(0, SessionState.GetInt(ScenarioKey, 0));
            ApplyCaptureAudio();
            _game.ignoreSavedProgress = true;
            _game.startingLevel = level;
            _game.LoadLevel(level);
            if (_game.mainMenu != null && _game.mainMenu.IsOpen)
                _game.mainMenu.Play();

            // Automated clips must start directly in gameplay. The normal one-time
            // terminal lesson is intentionally skipped here; otherwise it blocks the
            // screen while the bot continues moving pieces underneath it.
            var terminalTutorial = _game.GetComponent<UITerminalTutorial>();
            if (terminalTutorial != null)
            {
                terminalTutorial.Cancel();
                terminalTutorial.enabled = false;
            }
            if (_game.tutorialHand != null)
                _game.tutorialHand.Hide();

            // The Editor routinely loses focus while the automation/encoder runs. That is
            // not a real mobile interruption, so do not let AppLifecycle cover the clip with
            // the pause panel. If focus was lost during startup, resume it immediately.
            var lifecycle = FindAnyObjectByType<AppLifecycle>();
            if (lifecycle != null) lifecycle.enabled = false;
            if (_game.uiBindings != null && _game.uiBindings.pausePanel != null &&
                _game.uiBindings.pausePanel.IsOpen)
                _game.uiBindings.pausePanel.Resume();

            yield return null;
            StartRecorder(level);
            _recordingStartedAt = Time.realtimeSinceStartup;
            _lastMoveAt = _recordingStartedAt;

            if (_scenario == 2)
            {
                _overlay = gameObject.AddComponent<ContentCaptureOverlay>();
                _overlay.Begin(_recordingStartedAt, _scenario);

                // Keep the board still while the opening hook is on screen.
                const float openingHold = 1.8f;
                _lastMoveAt += openingHold - MoveInterval;
            }
            else if (_scenario == 4)
            {
                _overlay = gameObject.AddComponent<ContentCaptureOverlay>();
                _overlay.Begin(_recordingStartedAt, _scenario);

                // Scenario 04 starts its first clean move 0.3 seconds after the hook appears.
                _lastMoveAt -= MoveInterval - 0.3f;
            }
            else if (_scenario == 5)
            {
                _oneSpaceStage = 0;
                _oneSpaceStageAt = _recordingStartedAt;
            }
        }

        IEnumerator WaitForGame()
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _game = FindAnyObjectByType<GameManager>();
                if (_game != null && _game.board != null)
                {
                    _board = _game.board;
                    yield break;
                }
                yield return null;
            }
        }

        void Update()
        {
            if (_recorder == null || _stopping || _game == null || _board == null) return;

            if (_scenario == 2 || _scenario == 4 || _scenario == 5)
                CaptureScenarioReviewFrames(Time.realtimeSinceStartup - _recordingStartedAt);

            if (_scenario == 2 && Time.realtimeSinceStartup - _recordingStartedAt >= 16f)
            {
                StopCapture();
                return;
            }

            if (_scenario == 4)
            {
                float elapsed = Time.realtimeSinceStartup - _recordingStartedAt;
                if (elapsed >= 14f)
                {
                    StopCapture();
                    return;
                }

                // Hold the factual result after the clean eight-move sequence.
                if (elapsed >= 9f) return;
            }

            if (_scenario == 5 && Time.realtimeSinceStartup - _recordingStartedAt >= 15f)
            {
                StopCapture();
                return;
            }

            if (_game.uiBindings != null && _game.uiBindings.pausePanel != null &&
                _game.uiBindings.pausePanel.IsOpen)
                _game.uiBindings.pausePanel.Resume();

            if (_game.LevelOver || _board.AllPieces.Count == 0)
            {
                if (Time.realtimeSinceStartup - _lastMoveAt > 2.5f) StopCapture();
                return;
            }

            if (Time.realtimeSinceStartup - _startedAt > 35f)
            {
                Debug.LogWarning("[ContentCapture] Safety timeout reached; stopping the clip.");
                StopCapture();
                return;
            }

            if (_board.AllPieces.Count != _previousPieceCount)
            {
                _previousPieceCount = _board.AllPieces.Count;
                _lastMoveAt = Time.realtimeSinceStartup;
                return;
            }

            if (Time.realtimeSinceStartup - _lastMoveAt < MoveInterval) return;

            var clear = _board.AllPieces.Where(_board.HasClearPath).ToList();
            if (clear.Count == 0)
            {
                _board.ShuffleRemaining();
                _lastMoveAt = Time.realtimeSinceStartup;
                return;
            }

            int wantedColour = _game.Rules != null ? _game.Rules.CurrentColour : -1;
            if (_scenario == 5)
            {
                TickOneSpaceLeft(clear, wantedColour);
                return;
            }
            var next = clear.FirstOrDefault(piece => piece.ColourIndex == wantedColour) ?? clear[0];

            // Use the board's real removal path so all motion, belt routing, colour rules,
            // effects and win handling remain exactly the same as normal gameplay.
            if (_board.TryEscape(next))
                _lastMoveAt = Time.realtimeSinceStartup;
        }

        float MoveInterval => _scenario == 2 ? 1.15f : _scenario == 4 ? 1.1f : _scenario == 5 ? 1.05f : 0.75f;

        void TickOneSpaceLeft(System.Collections.Generic.List<PuzzlePiece> clear, int wantedColour)
        {
            var rules = _game.Rules;
            if (rules == null || clear.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            int targetQueue = Mathf.Max(0, rules.QueueCapacity - 1);

            if (_oneSpaceStage == 0 && rules.QueueCount >= targetQueue)
            {
                _oneSpaceStage = 1;
                _oneSpaceStageAt = now;
                return;
            }

            // Let the nearly-full queue read clearly before starting the rescue sequence.
            if (_oneSpaceStage == 1)
            {
                if (now - _oneSpaceStageAt < 2.2f) return;
                _oneSpaceStage = 2;
                _oneSpaceStageAt = now;
            }

            if (_oneSpaceStage == 2 && rules.QueueCount < targetQueue)
            {
                _oneSpaceStage = 3;
                _oneSpaceStageAt = now;
                return;
            }

            if (_oneSpaceStage == 3) return;
            if (now - _lastMoveAt < MoveInterval) return;

            PuzzlePiece next;
            if (_oneSpaceStage == 0)
            {
                // Real, legal taps of upcoming colours fill all but the final waiting place.
                next = clear.FirstOrDefault(piece => piece.ColourIndex != wantedColour);
                if (next == null) return;
            }
            else
            {
                // Resolve the pressure with the terminal's actual requested colour.
                next = clear.FirstOrDefault(piece => piece.ColourIndex == wantedColour);
                if (next == null) return;
            }

            if (_board.TryEscape(next))
                _lastMoveAt = now;
        }

        void CaptureScenarioReviewFrames(float elapsed)
        {
            if (!_capturedIntroReview && elapsed >= 0.7f)
            {
                _capturedIntroReview = true;
                CaptureReviewFrame("01_intro");
            }
            float gameplayAt = _scenario == 4 ? 5f : _scenario == 5 ? 7f : 6f;
            float outroAt = _scenario == 4 ? 10.5f : _scenario == 5 ? 12f : 12f;
            if (!_capturedGameplayReview && elapsed >= gameplayAt)
            {
                _capturedGameplayReview = true;
                CaptureReviewFrame("02_gameplay");
            }
            if (!_capturedOutroReview && elapsed >= outroAt)
            {
                _capturedOutroReview = true;
                CaptureReviewFrame("03_outro");
            }
        }

        static void CaptureReviewFrame(string name)
        {
            int scenario = SessionState.GetInt(ScenarioKey, 0);
            string reviewFolder = scenario == 5 ? "Scenario05Review"
                                : scenario == 4 ? "Scenario04Review"
                                : "Scenario02Review";
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ContentCaptures",
                "BaggageJam", reviewFolder));
            Directory.CreateDirectory(folder);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"{name}.png"));
        }

        void StartRecorder(int level)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ContentCaptures", "BaggageJam"));
            Directory.CreateDirectory(folder);

            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Baggage Jam TikTok Capture";
            movie.Enabled = true;
            movie.CaptureAudio = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
                Codec = CoreEncoderSettings.OutputCodec.MP4
            };
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 1080,
                OutputHeight = 1920
            };
            string prefix = _scenario switch
            {
                2 => "baggagejam_scenario-02_look-at-the-arrows",
                4 => "baggagejam_scenario-04_the-perfect-flow",
                5 => "baggagejam_scenario-05_one-space-left_no-text",
                _ => $"baggagejam_level-{level}"
            };
            movie.OutputFile = Path.Combine(folder, $"{prefix}_{stamp}");

            controllerSettings.AddRecorderSettings(movie);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = 60f;

            RecorderOptions.VerboseMode = false;
            _recorder = new RecorderController(controllerSettings);
            _recorder.PrepareRecording();
            if (!_recorder.StartRecording())
            {
                Debug.LogError("[ContentCapture] Unity Recorder could not start.");
                StopCapture();
                return;
            }

            Debug.Log($"[ContentCapture] Recording Baggage Jam to {movie.OutputFile}.mp4");
        }

        void StopCapture()
        {
            if (_stopping) return;
            _stopping = true;
            SessionState.SetBool(ArmedKey, false);
            SessionState.SetInt(ScenarioKey, 0);
            RestoreCaptureAudio();

            if (_recorder != null && _recorder.IsRecording())
                _recorder.StopRecording();

            Debug.Log("[ContentCapture] Recording finished.");
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
        }

        void OnDisable()
        {
            if (_recorder != null && _recorder.IsRecording())
                _recorder.StopRecording();
            SessionState.SetBool(ArmedKey, false);
            SessionState.SetInt(ScenarioKey, 0);
            RestoreCaptureAudio();
        }

        void ApplyCaptureAudio()
        {
            if (_audioOverridesApplied) return;
            _audioOverridesApplied = true;
            _previousSoundOn = GameState.SoundOn;
            _previousMusicOn = GameState.MusicOn;
            _previousAudioPause = AudioListener.pause;
            GameState.SoundOn = true;
            GameState.MusicOn = true;
            AudioListener.pause = false;
        }

        void RestoreCaptureAudio()
        {
            if (!_audioOverridesApplied) return;
            _audioOverridesApplied = false;
            GameState.SoundOn = _previousSoundOn;
            GameState.MusicOn = _previousMusicOn;
            AudioListener.pause = _previousAudioPause;
        }
    }

    /// <summary>
    /// Draws marketing copy over the real Game view without altering gameplay.
    /// The timing follows scenario 02 in the approved short-form content plan.
    /// </summary>
    sealed class ContentCaptureOverlay : MonoBehaviour
    {
        static readonly Color Ink = new Color32(35, 50, 74, 255);
        float _startedAt;
        int _scenario;
        GUIStyle _style;

        public void Begin(float startedAt, int scenario)
        {
            _startedAt = startedAt;
            _scenario = scenario;
        }

        void OnGUI()
        {
            float elapsed = Time.realtimeSinceStartup - _startedAt;
            string text;
            Color fill;
            float localTime;

            if (_scenario == 4 && elapsed < 1.5f)
            {
                text = "WATCH THIS\nFLOW";
                fill = Color.white;
                localTime = elapsed;
            }
            else if (_scenario == 4 && elapsed >= 9f && elapsed < 14f)
            {
                text = "THAT WAS\nCLEAN";
                fill = new Color32(255, 209, 71, 255);
                localTime = elapsed - 9f;
            }
            else if (_scenario == 2 && elapsed < 1.8f)
            {
                text = "LOOK AT\nTHE ARROWS";
                fill = Color.white;
                localTime = elapsed;
            }
            else if (_scenario == 2 && elapsed >= 11f && elapsed < 16f)
            {
                text = "DID YOU\nNOTICE?";
                fill = new Color32(255, 209, 71, 255);
                localTime = elapsed - 11f;
            }
            else
            {
                return;
            }

            EnsureStyle();
            float unit = Screen.height / 1920f;
            _style.fontSize = Mathf.RoundToInt(88f * unit);

            float pop = PopScale(localTime);
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.315f);
            var oldMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), center);

            var rect = new Rect(Screen.width * 0.08f, Screen.height * 0.255f,
                                Screen.width * 0.84f, Screen.height * 0.14f);
            float outline = Mathf.Max(3f, 8f * unit);
            _style.normal.textColor = Ink;
            foreach (var offset in new[]
                     {
                         new Vector2(-outline, 0), new Vector2(outline, 0),
                         new Vector2(0, -outline), new Vector2(0, outline),
                         new Vector2(-outline, -outline), new Vector2(outline, -outline),
                         new Vector2(-outline, outline), new Vector2(outline, outline)
                     })
            {
                GUI.Label(new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height), text, _style);
            }

            _style.normal.textColor = fill;
            GUI.Label(rect, text, _style);
            GUI.matrix = oldMatrix;
        }

        void EnsureStyle()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                clipping = TextClipping.Overflow
            };
        }

        static float PopScale(float time)
        {
            if (time < 0.12f) return Mathf.Lerp(0.9f, 1.06f, time / 0.12f);
            if (time < 0.22f) return Mathf.Lerp(1.06f, 1f, (time - 0.12f) / 0.1f);
            return 1f;
        }
    }

    /// <summary>
    /// Watches for a tiny local request file so an external scheduler can start a capture
    /// without clicking through the Unity UI. The request contains the level number.
    /// </summary>
    [InitializeOnLoad]
    static class BaggageJamContentCaptureTrigger
    {
        static readonly string RequestPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "ContentCapture.request"));
        static double _nextCheck;

        static BaggageJamContentCaptureTrigger()
        {
            EditorApplication.update += Check;
        }

        static void Check()
        {
            if (EditorApplication.timeSinceStartup < _nextCheck) return;
            _nextCheck = EditorApplication.timeSinceStartup + 1d;
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;

            int level = 12;
            int.TryParse(File.ReadAllText(RequestPath).Trim(), out level);
            if (level < 1) level = 12;

            File.Delete(RequestPath);
            SessionState.SetBool("SpikyGames.BaggageJam.ContentCapture.Armed", true);
            SessionState.SetInt("SpikyGames.BaggageJam.ContentCapture.Level", level);
            Debug.Log($"[ContentCapture] Request received for Baggage Jam level {level}.");
            EditorApplication.isPlaying = true;
        }
    }
}
#endif
