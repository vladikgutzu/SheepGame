using System.Collections.Generic;
using System.IO;
using System.Linq;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Off-screen frames for ad creatives. Separate from SceneAudit.RenderPhoneShot, which is
    // fixed at the 1290x2430 phone the framing is tuned for and always shoots the same three
    // levels: an ad needs its own aspects (9:16, 4:5, 1:1 all crop the board differently) and
    // a board that is part-cleared rather than freshly dealt.
    //
    // The shot list is a file rather than fields on this class because editing the class
    // recompiles, and a recompile drops out of play mode — which is the one state the camera
    // is actually placed in. Write the plan, then run this.
    //
    // The plan is plain text, not JSON: JsonUtility read the plan's own fields but always
    // came back with an empty shot list, nested or top level. Six comma-separated fields do
    // not need a serializer.
    //
    //   dir=C:/somewhere
    //   size=1080x1920
    //   name,level,remove,seed[,width,height[,force]]
    public class CreativeShot
    {
        public string name = "shot";
        public int level = 1;
        // Pieces to clear before the frame is taken. A freshly dealt board is the least
        // interesting moment of a level: nothing has happened yet and the belt is empty.
        public int remove = 0;
        public int seed = 0;
        public int width = 0;   // 0 = fall back to the plan's size
        public int height = 0;
        public bool force = false;
    }

    public static class CreativeShots
    {
        // Written by whoever is driving this; kept out of Assets so it is not an asset.
        public static string PlanPath =>
            Path.Combine(Path.GetTempPath(), "sheepgame_shots.txt");

        [MenuItem("SheepGame/Render Ad Frames")]
        public static void Execute()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("Render Ad Frames needs play mode — the camera is placed by the framer.");
                return;
            }
            if (!File.Exists(PlanPath))
            {
                Debug.LogError("No shot plan at " + PlanPath);
                return;
            }

            string dir = "";
            int planWidth = 1080, planHeight = 1920;
            var shots = new List<CreativeShot>();

            foreach (var raw in File.ReadAllLines(PlanPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("dir=")) { dir = line.Substring(4).Trim(); continue; }
                if (line.StartsWith("size="))
                {
                    var wh = line.Substring(5).Split('x');
                    if (wh.Length == 2) { int.TryParse(wh[0], out planWidth); int.TryParse(wh[1], out planHeight); }
                    continue;
                }

                var f = line.Split(',');
                if (f.Length < 2) continue;

                var shot = new CreativeShot { name = f[0].Trim() };
                int.TryParse(f[1], out shot.level);
                if (f.Length > 2) int.TryParse(f[2], out shot.remove);
                if (f.Length > 3) int.TryParse(f[3], out shot.seed);
                if (f.Length > 5) { int.TryParse(f[4], out shot.width); int.TryParse(f[5], out shot.height); }
                if (f.Length > 6) shot.force = f[6].Trim() == "1";
                shots.Add(shot);
            }

            if (dir.Length == 0 || shots.Count == 0) { Debug.LogError("Empty shot plan."); return; }

            var board = Object.FindFirstObjectByType<Board>();
            var manager = Object.FindFirstObjectByType<GameManager>();
            if (board == null || manager == null) { Debug.LogError("No Board/GameManager."); return; }

            Directory.CreateDirectory(dir);

            foreach (var shot in shots)
            {
                manager.LoadLevel(shot.level);
                int dealt = board.AllPieces.Count;
                Clear(board, shot.remove, shot.seed, shot.force);
                Sweep(board);

                int live = Object.FindObjectsByType<PuzzlePiece>(FindObjectsSortMode.None)
                                 .Count(p => p.gameObject.activeInHierarchy);
                Debug.Log($"{shot.name}: dealt {dealt}, removed {dealt - board.AllPieces.Count} " +
                          $"(force={shot.force}), on the board {board.AllPieces.Count}, in the frame {live}");

                int w = shot.width > 0 ? shot.width : planWidth;
                int h = shot.height > 0 ? shot.height : planHeight;
                Capture(board, w, h, Path.Combine(dir, shot.name + ".png"));
            }

            manager.LoadLevel(1);
            Debug.Log($"Ad frames written to {dir} ({shots.Count} shots).");
        }

        // Takes pieces off the board the way the player would — whichever have a clear path,
        // so the arrows and the escapable highlighting left behind are a state the game can
        // actually be in. The piece is then hidden outright: ForceRemove starts a coroutine
        // that flies it to the belt, and no frame runs between here and the capture, so it
        // would otherwise still be sitting on its cell.
        //
        // With `force`, a piece is taken whether or not it could legally leave. That is not
        // a reachable position — greedy removal wedges on these levels around thirty pieces
        // in — but it is a consistent one: ForceRemove is the board's own call, so occupancy
        // and the escapable flags are recomputed exactly as in play. It is how a near-empty
        // board gets photographed for an ad.
        static void Clear(Board board, int count, int seed, bool force)
        {
            if (count <= 0) return;
            var random = new System.Random(seed);

            for (int i = 0; i < count; i++)
            {
                var takeable = board.AllPieces.Where(board.HasClearPath).ToList();
                if (takeable.Count == 0 && force) takeable = board.AllPieces.ToList();
                if (takeable.Count == 0) break;

                var piece = takeable[random.Next(takeable.Count)];
                board.ForceRemove(piece);
                piece.gameObject.SetActive(false);
            }
        }

        // Everything with a PuzzlePiece on it that the board is not currently holding, gone
        // for good. Two things put pieces there and neither clears up in time for a capture
        // taken inside a single editor call: Board.Retire destroys, and Destroy only takes
        // effect at the end of the frame; and pieces mid-departure were seen coming back
        // active in later frames, so they pile up across runs — a hundred and fifty of them
        // were in the frame while the board reported an empty level.
        //
        // Board's coroutines are stopped first: they hold the departing pieces, and one that
        // resumed on a destroyed transform would throw every frame afterwards.
        static void Sweep(Board board)
        {
            board.StopAllCoroutines();

            var held = new HashSet<PuzzlePiece>(board.AllPieces);
            foreach (var piece in Object.FindObjectsByType<PuzzlePiece>(FindObjectsInactive.Include,
                                                                       FindObjectsSortMode.None))
                if (piece != null && !held.Contains(piece))
                    Object.DestroyImmediate(piece.gameObject);
        }

        public static void Capture(Board board, int width, int height, string file)
        {
            var camera = Camera.main;

            // ARGB32 rather than an HDR target, for the reason SceneAudit.Capture spells out:
            // the pipeline's last blit is what converts to sRGB, and a float target skips it
            // and writes linear values into the PNG. The cost is no headroom over 1, so this
            // frame understates bloom — which at the current threshold does nothing anyway.
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing)
            };
            var previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;

            camera.targetTexture = rt;
            camera.aspect = (float)width / height;
            Frame(board, camera);
            camera.Render();

            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previousActive;
            File.WriteAllBytes(file, image.EncodeToPNG());

            // Where every piece ended up in this exact frame. A tap hint drawn over the shot
            // has to land on a suitcase, and the aspect decides both the framing distance and
            // the projection — measuring it off the finished PNG by eye is guesswork that has
            // to be redone for every size.
            WritePieceMap(board, camera, width, height, Path.ChangeExtension(file, ".pieces.txt"));

            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            camera.ResetAspect();
            Object.DestroyImmediate(image);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        // One line per piece: pixel position in the image (y already flipped to top-down, the
        // way an image editor counts), which way it faces, and whether its path is clear.
        static void WritePieceMap(Board board, Camera camera, int width, int height, string file)
        {
            var lines = new List<string> { "# x,y,direction,escapable" };

            foreach (var piece in board.AllPieces)
            {
                Vector3 v = camera.WorldToViewportPoint(piece.transform.position);
                if (v.z <= 0f) continue;

                lines.Add($"{v.x * width:F0},{(1f - v.y) * height:F0}," +
                          $"{piece.Direction},{(board.HasClearPath(piece) ? 1 : 0)}");
            }

            File.WriteAllLines(file, lines);
        }

        // The same rectangle Board.FrameLevel hands the framer — the fixed field plus its
        // lane and padding — snapped rather than glided, since no frame runs before the
        // render. Re-fitted per aspect: every one of these sizes frames from a different
        // distance.
        static void Frame(Board board, Camera camera)
        {
            var framer = board.cameraFramer;
            if (framer == null || camera == null) return;

            float pad = board.roadMargin + board.cellSize * Mathf.Max(0f, board.framePadding);
            framer.Frame(board.transform, board.transform.position,
                         board.fieldSize.x * 0.5f * board.cellSize + pad,
                         board.fieldSize.y * 0.5f * board.cellSize + pad, immediate: true);
        }
    }
}
