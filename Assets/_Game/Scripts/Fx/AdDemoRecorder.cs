using System.Collections.Generic;
using System.IO;
using System.Linq;
using SheepGame.Core;
using UnityEngine;

namespace SheepGame.Fx
{
    // Plays the game by itself and writes one PNG per frame, for a vertical ad video.
    //
    // Runtime rather than editor tooling because there is no other way to get motion: an
    // editor script runs inside a single frame, and the belt, the hop onto it and the
    // departure all live in coroutines that need frames to advance. The taps are the game's
    // own — HandlePieceClicked is exactly what a finger on the screen calls — so what is
    // recorded is the real game, not a mock-up of it.
    //
    // Time.captureFramerate makes each frame advance by exactly 1/fps of game time however
    // long the encode of the previous one took, so the clip plays back at the right speed
    // even though this records far slower than real time.
    public class AdDemoRecorder : MonoBehaviour
    {
        public int width = 1080;
        public int height = 1920;
        public int fps = 30;
        public int frameCount = 300;
        public int level = 11;
        public float firstTapDelay = 0.7f;
        public float tapInterval = 0.38f;
        public int seed = 7;
        public string directory = "";

        [Tooltip("0 leaves the capture to Unity Recorder and only plays the game — which is " +
                 "the only way to get an encoded file, and stops two things fighting over " +
                 "Time.captureDeltaTime.")]
        public bool capture = true;

        Board _board;
        GameManager _manager;
        Camera _camera;
        RenderTexture _rt;
        Texture2D _readback;
        System.Random _random;
        float _nextTap;
        float _previousTimeScale = 1f;
        int _frame;
        bool _done;

        public static AdDemoRecorder Begin(string settingsFile)
        {
            var go = new GameObject("~AdDemoRecorder");
            var recorder = go.AddComponent<AdDemoRecorder>();
            recorder.Configure(settingsFile);
            return recorder;
        }

        void Configure(string settingsFile)
        {
            if (!File.Exists(settingsFile)) { Debug.LogError("No recorder settings: " + settingsFile); return; }

            foreach (var raw in File.ReadAllLines(settingsFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "width": int.TryParse(value, out width); break;
                    case "height": int.TryParse(value, out height); break;
                    case "fps": int.TryParse(value, out fps); break;
                    case "frames": int.TryParse(value, out frameCount); break;
                    case "level": int.TryParse(value, out level); break;
                    // Invariant, or nothing with a decimal point in it survives: this editor
                    // runs under a culture that writes 0,0333, so "0.8" parsed as zero and
                    // every frame of the first recordings fired a tap.
                    case "firstTapDelay": firstTapDelay = ParseFloat(value, firstTapDelay); break;
                    case "tapInterval": tapInterval = ParseFloat(value, tapInterval); break;
                    case "seed": int.TryParse(value, out seed); break;
                    case "dir": directory = value; break;
                    case "capture": capture = value != "0"; break;
                }
            }
        }

        static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? parsed : fallback;
        }

        void Start()
        {
            _board = FindFirstObjectByType<Board>();
            _manager = FindFirstObjectByType<GameManager>();
            _camera = Camera.main;
            if (_board == null || _manager == null || _camera == null)
            {
                Debug.LogError("AdDemoRecorder: no Board, GameManager or main camera.");
                enabled = false;
                return;
            }

            if (capture)
            {
                Directory.CreateDirectory(directory);
                foreach (var stale in Directory.GetFiles(directory, "frame_*.png")) File.Delete(stale);
            }

            _random = new System.Random(seed);

            // captureFramerate alone is not enough: it fixes how much time each frame is
            // worth, but timeScale still multiplies it, and the first recording came out at
            // twice the intended pace because the scene was left running at 2. Both are put
            // back in OnDestroy so the editor session is not left rescaled.
            Debug.Log($"AdDemoRecorder: timeScale was {Time.timeScale}, captureFramerate was {Time.captureFramerate}");

            // Only when this component is doing the capturing. Unity Recorder parks timeScale
            // at zero while it gets ready and puts it back itself; forcing it to 1 underneath
            // fights that, and restoring the zero it was found at on the way out would leave
            // the game frozen.
            if (capture)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 1f;
            }

            if (capture)
            {
                // captureDeltaTime, not captureFramerate: the latter was set and ignored —
                // the first recordings ran the whole level out in a third of the frames,
                // which is what a real-time clock looks like when each frame takes an eighth
                // of a second to encode. This one pins the step directly. Unity Recorder
                // pins the same field itself, so in driver-only mode it is left alone.
                Time.captureDeltaTime = 1f / fps;

                _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing)
                };
                _readback = new Texture2D(width, height, TextureFormat.RGB24, false);
            }

            // Framed for the aspect being recorded, not the aspect the Game view happens to
            // be set to: Board.FrameLevel fits the board to whatever the camera says when the
            // level loads, and every one of those numbers moves with the aspect.
            _camera.aspect = (float)width / height;
            _manager.LoadLevel(level);

            _nextTap = Time.time + firstTapDelay;
        }

        // Counted in frames rather than seconds. The cadence has to be right against the
        // frames that end up in the file, and that is the one clock here that cannot drift.
        void Update()
        {
            if (_done) return;

            int firstTapFrame = Mathf.RoundToInt(firstTapDelay * fps);
            int every = Mathf.Max(1, Mathf.RoundToInt(tapInterval * fps));
            if (_frame < firstTapFrame || (_frame - firstTapFrame) % every != 0) return;

            var escapable = _board.AllPieces.Where(_board.HasClearPath).ToList();
            if (escapable.Count > 0)
                _board.HandlePieceClicked(escapable[_random.Next(escapable.Count)]);
        }

        void LateUpdate()
        {
            if (_done) return;

            if (!capture)
            {
                // Still counted: the tap schedule is in frames, and in driver-only mode these
                // are the frames Recorder is writing.
                _frame++;
                if (_frame >= frameCount) Finish();
                return;
            }

            var previousTarget = _camera.targetTexture;
            float previousAspect = _camera.aspect;

            _camera.targetTexture = _rt;
            _camera.aspect = (float)width / height;
            _camera.Render();

            var previousActive = RenderTexture.active;
            RenderTexture.active = _rt;
            _readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            _readback.Apply();
            RenderTexture.active = previousActive;

            File.WriteAllBytes(Path.Combine(directory, $"frame_{_frame:0000}.png"),
                               _readback.EncodeToPNG());

            _camera.targetTexture = previousTarget;
            _camera.aspect = previousAspect;

            _frame++;
            if (_frame >= frameCount) Finish();
        }

        void Finish()
        {
            _done = true;
            if (capture)
            {
                Time.captureFramerate = 0;
                Time.captureDeltaTime = 0f;
                Time.timeScale = _previousTimeScale;
            }
            _camera.ResetAspect();
            Debug.Log($"AdDemoRecorder: {_frame} frames at {width}x{height}, {fps} fps -> {directory}");
        }

        void OnDestroy()
        {
            if (capture)
            {
                Time.captureFramerate = 0;
                Time.captureDeltaTime = 0f;
                Time.timeScale = _previousTimeScale;
            }
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_readback != null) Destroy(_readback);
        }
    }
}
