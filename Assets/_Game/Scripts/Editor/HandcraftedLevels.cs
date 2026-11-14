using System.Collections.Generic;
using System.Text;
using SheepGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // The first twenty levels, written out by hand, and the tool that turns them into assets
    // and makes them the play order. Level 21 onwards is generated.
    //
    // Each level is drawn as text, top row first, one token per cell:
    //
    //   R^  a red case leaving upwards        ..  an empty cell
    //   Bv  a blue case leaving downwards     r>  the tail of a long red case whose front is
    //   G<  green, leaving left                   the next cell to the right, facing right
    //
    // Colours are B blue, R red, G green, Y yellow, P purple, K pink (see PieceColours). The
    // carts string is the colour of each cart in the order they pull in, three seats a cart.
    //
    // How they were made: the first five were drawn cell by cell. From six on the silhouette
    // was drawn by hand and the facings and colours searched for against a target, then kept
    // only if a solver proved the level can be finished with the five places the queue has,
    // and if its difficulty sat where it belongs on the curve. The curve is a saw, not a ramp:
    // 8, 13 and 18 are deliberate breathers after harder levels, and 6, 12 and 15 are where a
    // booster is handed over.
    //
    // 2026-09-19: re-coloured (silhouettes and facings untouched) against two simulated
    // players, 600 games each, each tapping a random free case 15% of the time by mistake:
    //   greedy -- takes the cart's colour when one is free, otherwise parks the colour the
    //             soonest cart wants. This is the curve: 1.00 to level 4, about 0.7 at 10,
    //             about 0.3 by 19-20.
    //   reader -- the same, but parks whichever case frees the most of the cart's colour.
    //             Kept at 0.9 or better through 19 (0.88 on 20), so a player who reads the
    //             board is never beaten by the level itself.
    // Before this the greedy curve was ragged: 4 was harder than 5-8, 10 was a wall (0.34)
    // just before the first interstitial, 11 was nearly free, the breathers were harder than
    // their neighbours, and 20 beat even the reading player half the time.
    //
    // Numbers from a simulation are not play-testing. Play them.
    public static class HandcraftedLevels
    {
        const string Folder = "Assets/_Game/Data/Levels/Handcrafted";
        const string LibraryPath = "Assets/_Game/Data/SuitcaseLibrary.asset";
        const int QueueSlots = 5;

        readonly struct Def
        {
            public readonly int Number;
            public readonly string Carts;
            public readonly bool Tutorial;
            public readonly string[] Rows;

            public Def(int number, string carts, bool tutorial, params string[] rows)
            {
                Number = number;
                Carts = carts;
                Tutorial = tutorial;
                Rows = rows;
            }
        }

        static readonly Def[] Levels =
        {
            // 1. Three red cases, nothing in the way, one red cart. The hand shows each tap. (greedy 1.00, reader 1.00)
            new Def(1, "R", true,
                "R^ R^ R^"),

            // 2. Two colours. One case waits behind another; a blue tapped first just waits in the queue. (greedy 1.00, reader 1.00)
            new Def(2, "RB", false,
                "B^ R^ B^",
                "Rv B^ Rv"),

            // 3. Three colours, three carts. The arrows start to matter. (greedy 1.00, reader 1.00)
            new Def(3, "RBY", false,
                "Y^ R^ B^",
                "R< B^ Y>",
                "Bv Yv Rv"),

            // 4. Twelve cases, and a colour that comes round twice. (greedy 1.00, reader 1.00)
            new Def(4, "RYBR", false,
                ".. Y^ R^ ..",
                "B< R^ B^ Y>",
                "R< R^ R^ R>",
                ".. Bv Yv .."),

            // 5. Four colours. The order of the carts makes the queue earn its place. (greedy 0.98, reader 1.00)
            new Def(5, "RYGBR", false,
                "R^ G^ B^ G^ R^",
                "Y< Y^ G^ B^ R>",
                "Rv Rv Bv Yv Rv"),

            // 6. Remove is handed over here, on the first board where planning the queue matters. (greedy 0.88, reader 0.98)
            new Def(6, "GYBRB", false,
                ".. R^ B^ Bv ..",
                "G^ B^ B> Yv R^",
                "B^ Y< Rv G> G>",
                ".. Y> .. B> .."),

            // 7. Heart. Four colours, a little planning. (greedy 0.84, reader 0.96)
            new Def(7, "GBYRGB", false,
                "Bv G< .. G< Bv",
                "B< R^ G^ Bv Gv",
                "Bv Rv Y> Yv Bv",
                ".. Gv Y> Rv ..",
                ".. .. G> .. .."),

            // 8. Breather: a plus sign that mostly plays itself. (greedy 0.97, reader 1.00)
            new Def(8, "BYGRYB", false,
                ".. .. R> G> .. ..",
                ".. .. Y< B> .. ..",
                "Y^ Y< B^ Y^ Y^ Bv",
                "Y< Gv R> R^ G^ Bv",
                ".. .. B> B^ .. .."),

            // 9. Long cases arrive: they only move along their length. (greedy 0.75, reader 1.00)
            new Def(9, "YBYBGYR", false,
                ".. .. B< B< B> .. ..",
                ".. G^ B^ B^ Y> G> ..",
                "Yv Y< y< G> r> R> Bv",
                ".. Y< Y< Rv Y> Yv ..",
                ".. .. Rv Y< Yv .. .."),

            // 10. Last level before interstitials start. Tighter queue use. (greedy 0.67, reader 0.99)
            new Def(10, "BYRGYBGB", false,
                "Y^ G^ Bv Bv G< Y> Y^",
                "y^ R< Gv Bv B^ G> y^",
                "Yv Y< Bv .. G< B> R>",
                ".. B> Bv Yv R> G> ..",
                ".. .. .. B< .. .. .."),

            // 11. Five colours. (greedy 0.62, reader 0.95)
            new Def(11, "BRYGYRPGB", false,
                ".. Gv P< .. Y^ Bv ..",
                "Bv Yv Y< G^ G< Gv Rv",
                "Rv Bv Pv P> G^ Y> Rv",
                ".. Bv Bv Bv Gv Yv ..",
                ".. .. Y< R< R< .. ..",
                ".. .. .. R> .. .. .."),

            // 12. Shuffle is handed over here: a frame with holes, most of its cases walled in. (greedy 0.57, reader 0.92)
            new Def(12, "PYBGGPRPBG", false,
                "Bv Gv B< B< P< Y^ G<",
                "Bv .. P< G< P< .. G<",
                "Yv Pv P^ .. B< G^ G<",
                "B< .. Y< P< G< .. R<",
                "R< R< G< G< P^ P^ Pv"),

            // 13. Breather, with long cases. (greedy 0.87, reader 1.00)
            new Def(13, "PGYPRYGR", false,
                ".. .. P< .. P> .. ..",
                ".. .. G< R^ G> .. ..",
                ".. P^ Y< y< Y> Y^ ..",
                "Pv R< R^ .. R^ P^ R^",
                ".. G< R> y> Y> Gv ..",
                ".. .. G< Yv Y> .. ..",
                ".. .. G< .. Pv .. .."),

            // 14. Two rows of long cases standing up. (greedy 0.54, reader 0.98)
            new Def(14, "GBYPBGYPRYP", false,
                "yv P< B^ pv Y< P< P^",
                "Yv P< Y^ Pv Y< Y< p^",
                "Bv R> P^ Rv Y> B> Yv",
                "Yv p> P> .. r> R> Yv",
                "Gv P< G< G< G< B> Gv",
                ".. B< G< .. P< B< .."),

            // 15. Hint is handed over here. (greedy 0.48, reader 0.92)
            new Def(15, "PBRPGYRGPRG", false,
                "G< .. R^ R^ R< .. G<",
                "R^ P< G> R^ Pv Y> Pv",
                "Bv R< r< G^ g> G> B>",
                "Pv Y< R< Pv Rv G> Rv",
                ".. Gv G> Yv Gv P> ..",
                ".. .. Pv Pv Bv .. ..",
                ".. .. .. Pv .. .. .."),

            // 16. (greedy 0.52, reader 0.97)
            new Def(16, "GRBGYPYRPRYP", false,
                "B^ Rv R^ .. P^ Yv B>",
                "G^ Y< R^ R> Pv P> R>",
                "P^ Rv Yv P< Yv Yv G^",
                "p^ Rv P> R> Pv Gv g^",
                "Rv Yv P> Gv Yv Yv Gv",
                ".. Gv P> .. Y> B> .."),

            // 17. Six colours. (greedy 0.39, reader 0.97)
            new Def(17, "GKGYBPKYRBRPB", false,
                ".. Y> G> G^ K> G> ..",
                "K^ B< R^ B> Y^ B^ G^",
                "G< P< p< P< B< b< B>",
                "G^ B^ R^ B^ Pv Rv Pv",
                "Y^ Kv R< Rv R> Kv B>",
                ".. Pv Y< K< Y> B> ..",
                ".. .. K< Yv Pv .. .."),

            // 18. Breather: a round board, five colours. (greedy 0.78, reader 1.00)
            new Def(18, "PGRPRGYBRY", false,
                ".. .. P< R^ R< .. ..",
                ".. P< B> R^ B> P^ ..",
                "P^ P^ Y^ Yv Yv Gv Rv",
                "Pv Y< Rv Y< R> R> Gv",
                ".. G< B> G> Gv Rv ..",
                ".. .. Y> R> G> .. .."),

            // 19. (greedy 0.30, reader 0.97)
            new Def(19, "BKGPYBGRPBRYGR", false,
                "G^ R^ B< B^ G^ B^ P^",
                "Y< P^ R< R^ K^ rv Y^",
                "Yv p^ Y< Gv P^ Rv P^",
                "K< B< Gv Gv G^ Rv R>",
                "R< r< Bv .. P> b> B>",
                "Bv Bv G< Pv Yv R> R>",
                ".. Gv Bv Kv G> Y> .."),

            // 20. The last hand-made level. The generator carries on from its size. (greedy 0.31, reader 0.88)
            new Def(20, "YBGRKGBKPYKRYGR", false,
                "Pv B^ .. Y> .. G^ Bv",
                "Pv Y< R> R> R^ Yv G>",
                "Kv K^ K< Gv Y> Rv Gv",
                "bv K< k< Gv k> K> pv",
                "Bv R< R> Rv Gv Rv Pv",
                "Yv Y< Gv Y> K> Y> Y>",
                ".. K^ Rv B> Bv Gv ..",
                ".. .. B< Kv K> .. ..",
                ".. .. .. G< .. .. ..")
        };

        [MenuItem("SheepGame/Build Handcrafted Levels")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Levels] Stop play mode first. Nothing was changed.");
                return;
            }

            EnsureFolder();
            ApplyPalette();

            var built = new List<LevelData>();
            var report = new StringBuilder();
            bool allGood = true;

            foreach (var def in Levels)
            {
                if (!TryParse(def, out var level, out string error))
                {
                    Debug.LogError($"[Levels] Level {def.Number}: {error}");
                    allGood = false;
                    continue;
                }

                string path = $"{Folder}/Level_H{def.Number:00}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<LevelData>(path);

                // Rewritten in place, never replaced: GameManager.levels holds levels by GUID.
                if (asset == null)
                {
                    AssetDatabase.CreateAsset(level, path);
                    asset = level;
                }
                else
                {
                    EditorUtility.CopySerialized(level, asset);
                    asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
                    Object.DestroyImmediate(level);
                }

                EditorUtility.SetDirty(asset);
                built.Add(asset);

                bool facings = LevelSolver.TrySolve(asset, out var stuck);
                bool counts = ColourSolver.CountsMatch(asset.pieces, asset.carts, asset.CartCapacity, out string countProblem);
                bool? solved = facings && counts ? ColourSolver.Solve(asset, QueueSlots) : false;

                report.AppendLine($"L{def.Number:00}: {asset.pieces.Count} cases, {asset.carts.Count} carts, " +
                                  $"{asset.gridWidth}x{asset.gridHeight} -- " +
                                  (!facings ? $"WEDGED ({stuck.Count} stuck)"
                                   : !counts ? "COLOURS DO NOT MATCH CARTS: " + countProblem
                                   : solved == true ? "solvable"
                                   : solved == false ? "NOT SOLVABLE with " + QueueSlots + " places"
                                   : "solver ran out of budget"));

                if (!facings || !counts || solved == false) allGood = false;
            }

            AssetDatabase.SaveAssets();

            var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gm != null && built.Count == Levels.Length)
            {
                Undo.RecordObject(gm, "Build Handcrafted Levels");
                gm.levels = built;
                EditorUtility.SetDirty(gm);
                EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
                report.AppendLine("GameManager.levels is now these twenty. Save the scene to keep it.");
            }
            else if (gm == null)
            {
                report.AppendLine("No GameManager in the open scene: the play order was not changed.");
            }

            if (allGood) Debug.Log("[Levels] Built the handcrafted levels.\n" + report);
            else Debug.LogError("[Levels] Built with problems.\n" + report);
        }

        static bool TryParse(Def def, out LevelData level, out string error)
        {
            level = ScriptableObject.CreateInstance<LevelData>();
            error = null;

            int height = def.Rows.Length;
            var grid = new string[height][];
            for (int r = 0; r < height; r++)
                grid[r] = def.Rows[r].Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            int width = grid[0].Length;
            for (int r = 1; r < height; r++)
            {
                if (grid[r].Length == width) continue;
                error = $"row {r} has {grid[r].Length} cells, the first row has {width}";
                return false;
            }

            var fronts = new Dictionary<Vector2Int, LevelPieceEntry>();
            var tails = new List<(Vector2Int cell, int colour, Direction dir)>();

            for (int r = 0; r < height; r++)
            {
                int y = height - 1 - r;
                for (int x = 0; x < width; x++)
                {
                    string token = grid[r][x];
                    if (token == "..") continue;
                    if (token.Length != 2) { error = $"bad token '{token}' at {x},{y}"; return false; }

                    int colour = PieceColours.FromLetter(token[0]);
                    if (colour < 0) { error = $"unknown colour '{token[0]}' at {x},{y}"; return false; }
                    if (!TryDirection(token[1], out var dir)) { error = $"unknown direction '{token[1]}' at {x},{y}"; return false; }

                    var cell = new Vector2Int(x, y);
                    if (char.IsUpper(token[0]))
                        fronts[cell] = new LevelPieceEntry { position = cell, direction = dir, colour = colour, cells = 1 };
                    else
                        tails.Add((cell, colour, dir));
                }
            }

            // A tail names the way to its own front, which is also the way the case faces.
            foreach (var (cell, colour, dir) in tails)
            {
                var front = cell + DirectionUtil.ToVector(dir);
                if (!fronts.TryGetValue(front, out var entry) || entry.colour != colour || entry.direction != dir || entry.cells != 1)
                {
                    error = $"the tail at {cell.x},{cell.y} has no matching front at {front.x},{front.y}";
                    return false;
                }
                entry.cells = 2;
                fronts[front] = entry;
            }

            level.gridWidth = width;
            level.gridHeight = height;
            level.pieces = new List<LevelPieceEntry>(fronts.Values);
            level.coloured = true;
            level.cartCapacity = 3;
            level.queueSlots = QueueSlots;
            level.carts = new List<int>();
            foreach (char c in def.Carts)
            {
                int colour = PieceColours.FromLetter(c);
                if (colour < 0) { error = $"unknown cart colour '{c}'"; return false; }
                level.carts.Add(colour);
            }

            level.tutorialHand = def.Tutorial;
            level.tutorialHints = def.Tutorial;
            level.tapBudget = 0;
            level.mode = LevelModeOverride.AlwaysTaps;
            level.frameOnPieces = false;
            return true;
        }

        static bool TryDirection(char c, out Direction dir)
        {
            switch (c)
            {
                case '^': dir = Direction.Up; return true;
                case 'v': dir = Direction.Down; return true;
                case '<': dir = Direction.Left; return true;
                case '>': dir = Direction.Right; return true;
                default: dir = Direction.Up; return false;
            }
        }

        // The case colours are the rule now, so they are set to colours chosen to be told apart
        // at a glance rather than to go well together. See PieceColours.
        static void ApplyPalette()
        {
            var library = AssetDatabase.LoadAssetAtPath<PieceModelLibrary>(LibraryPath);
            if (library == null) return;

            Undo.RecordObject(library, "Build Handcrafted Levels");
            library.palette = (Color[])PieceColours.Default.Clone();
            EditorUtility.SetDirty(library);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Data/Levels"))
                AssetDatabase.CreateFolder("Assets/_Game/Data", "Levels");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Game/Data/Levels", "Handcrafted");
        }
    }
}
