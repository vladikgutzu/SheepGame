using System.Collections.Generic;
using System.Linq;
using System.Text;
using SheepGame.Core;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Redraws the early levels on the field the game actually has.
    //
    // Levels 01-08 were authored back when the field sized itself to each level, on grids
    // from 5x5 to 7x9. The field is pinned at 7x10 now, so a smaller board is centred in it
    // and the level plays out as a clump in the middle of an otherwise empty floor — the
    // pieces are the right size, there are just eleven of them huddled in the middle of
    // seventy cells. The names promise shapes, so the shapes are what gets redrawn.
    //
    // Each motif is a silhouette drawn at full 7x10, and a level is the N cells of it
    // furthest from its edge — so the shape is **filled**, from the middle outwards, and
    // the count decides how far out it reaches before the field's corners are left bare.
    //
    // It was the other way round first, N cells nearest the edge, which drew each motif as
    // an outline with a hole in the middle. The user's answer to that was the point of the
    // exercise: "нужно заполнить пространство чемоданами". Filling from the core also means
    // the cells a lower count gives up are the rim ones, spread evenly, rather than a bite
    // out of one side.
    //
    // The facings come from LevelSolver.TryAssignDirections, which plays the board backwards
    // and therefore cannot produce an unsolvable one; TrySolve then says so independently.
    public static class LevelShapeBuilder
    {
        const string LevelDir = "Assets/_Game/Data/Levels";

        // Each count is the exact cell count of its silhouette, so no level is a partly
        // filled shape — the ramp is built by drawing the shapes at the sizes it needs
        // rather than by trimming one down. The motifs that appear twice have a small and a
        // large drawing. It ramps into the 42 / 46 / 53 of the three levels already on this
        // field, and a filled shape that still spans it cannot be small: thirty is about
        // the floor for one that reads as a shape rather than a blob.
        static readonly (string asset, string shape, int count)[] Plan =
        {
            ("Level_01_Diamond", "diamond small", 30),
            ("Level_02_Star",    "star",          32),
            ("Level_03_Cross",   "cross",         35),
            ("Level_04_Square",  "square small",  36),
            ("Level_05_Diamond", "diamond large", 38),
            ("Level_06_Circle",  "circle",        39),
            ("Level_07_Heart",   "heart",         40),
            ("Level_08_Square",  "square large",  42),
        };

        // Silhouettes, drawn top row first. Seven wide and ten tall, the field's own shape.
        static readonly Dictionary<string, string[]> Shapes = new Dictionary<string, string[]>
        {
            ["diamond small"] = new[]        // 30
            {
                ".......",
                "...#...",
                "..###..",
                ".#####.",
                ".#####.",
                "#######",
                ".#####.",
                "..###..",
                "...#...",
                ".......",
            },
            ["diamond large"] = new[]        // 38
            {
                ".......",
                "...#...",
                "..###..",
                ".#####.",
                "#######",
                "#######",
                "#######",
                ".#####.",
                "..###..",
                ".......",
            },
            ["star"] = new[]                 // 32
            {
                "...#...",
                "..###..",
                "#######",
                ".#####.",
                "..###..",
                ".#####.",
                ".##.##.",
                ".#...#.",
                ".#...#.",
                ".......",
            },
            ["cross"] = new[]                // 35
            {
                "..###..",
                "..###..",
                "..###..",
                "..###..",
                "#######",
                "#######",
                "..###..",
                "..###..",
                "..###..",
                ".......",
            },
            ["square small"] = new[]         // 36
            {
                ".......",
                ".......",
                ".#####.",
                "#######",
                "#######",
                "#######",
                ".#####.",
                ".#####.",
                ".......",
                ".......",
            },
            ["square large"] = new[]         // 42
            {
                ".......",
                ".......",
                "#######",
                "#######",
                "#######",
                "#######",
                "#######",
                "#######",
                ".......",
                ".......",
            },
            ["circle"] = new[]               // 39
            {
                ".......",
                "..###..",
                ".#####.",
                "#######",
                "#######",
                "#######",
                "#######",
                "..###..",
                ".......",
                ".......",
            },
            ["heart"] = new[]                // 40
            {
                ".##.##.",
                "#######",
                "#######",
                "#######",
                ".#####.",
                ".#####.",
                "..###..",
                "...#...",
                "...#...",
                ".......",
            },
        };

        [MenuItem("SheepGame/Redraw Shape Levels")]
        public static void Redraw()
        {
            var board = Object.FindFirstObjectByType<Board>(FindObjectsInactive.Include);
            int width = board != null && board.fieldSize.x > 0 ? board.fieldSize.x : 7;
            int height = board != null && board.fieldSize.y > 0 ? board.fieldSize.y : 10;

            var report = new StringBuilder($"Redrawing levels on the {width}x{height} field:\n");

            foreach (var (assetName, shape, count) in Plan)
            {
                string path = $"{LevelDir}/{assetName}.asset";
                var level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
                if (level == null) { report.AppendLine($"{assetName}: MISSING at {path}"); continue; }

                var cells = Fill(Shapes[shape], width, height, count);
                if (cells.Count < count)
                    report.AppendLine($"{assetName}: silhouette '{shape}' only holds {cells.Count} cells");

                // Seeded on the level number so a re-run reproduces the same board, and so
                // two levels of the same motif do not come out with identical facings.
                int seed = 1000 + System.Array.FindIndex(Plan, p => p.asset == assetName);
                if (!LevelSolver.TryAssignDirections(cells, width, height, seed, out var facings))
                {
                    report.AppendLine($"{assetName}: could not assign facings — left untouched");
                    continue;
                }

                // Written into the existing asset rather than a fresh one: GameManager holds
                // these by reference, and a replacement asset is a new GUID and an empty slot.
                level.gridWidth = width;
                level.gridHeight = height;
                level.pieces = cells.Select(c => new LevelPieceEntry
                {
                    position = c,
                    direction = facings[c],
                    variantIndex = 0            // the model mix is GameManager.modelVariety's job
                }).ToList();
                EditorUtility.SetDirty(level);

                bool solved = LevelSolver.TrySolve(level, out var stuck);
                report.AppendLine($"{assetName}: {shape}, {level.pieces.Count} pieces, " +
                                  (solved ? "solvable" : $"STUCK at {stuck.Count} cells"));
                report.AppendLine(Preview(cells, facings, width, height));
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        // The count cells of the silhouette furthest inside it — a solid shape grown from
        // its core out towards its edge. Depth is measured to the first cell that is not
        // part of the shape, with everything off the grid counting as not part of it, so a
        // shape that runs to the board edge is not treated as deep there.
        static List<Vector2Int> Fill(string[] rows, int width, int height, int count)
        {
            bool Inside(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return false;
                int row = height - 1 - y;                      // the art is drawn top row first
                if (row < 0 || row >= rows.Length) return false;
                var line = rows[row];
                return x < line.Length && line[x] == '#';
            }

            var scored = new List<(Vector2Int cell, float depth, float radius)>();
            var centre = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (!Inside(x, y)) continue;

                    float depth = float.MaxValue;
                    for (int ox = -width; ox <= width; ox++)
                        for (int oy = -height; oy <= height; oy++)
                            if (!Inside(x + ox, y + oy))
                                depth = Mathf.Min(depth, Mathf.Sqrt(ox * ox + oy * oy));

                    scored.Add((new Vector2Int(x, y), depth,
                                Vector2.Distance(new Vector2(x, y), centre)));
                }

            // Deepest first, and within one ring the cells nearest the middle, so a count
            // that lands part way through a ring gives up rim cells evenly all the way
            // round instead of taking a bite out of one side.
            var ranked = scored
                .OrderByDescending(s => s.depth)
                .ThenBy(s => s.radius)
                .ThenBy(s => s.cell.y).ThenBy(s => s.cell.x)
                .Select(s => s.cell)
                .ToList();

            // Grown one cell at a time, and only onto a cell touching what is already
            // taken. Ranking alone leaves stragglers: a count that stops part way through
            // the outermost ring picks a few of its cells and drops the rest, and a lone
            // suitcase sitting a cell off the shape reads as a mistake rather than as a
            // point of a star.
            var taken = new List<Vector2Int>();
            var chosen = new HashSet<Vector2Int>();
            foreach (var seed in ranked)
            {
                if (taken.Count >= count) break;
                if (chosen.Count > 0 && !Touches(seed, chosen))
                {
                    // Held back until something next to it is taken; the sweep below picks
                    // it up. Only a genuinely detached piece of the silhouette is skipped.
                    continue;
                }
                taken.Add(seed);
                chosen.Add(seed);

                bool grew = true;
                while (grew && taken.Count < count)
                {
                    grew = false;
                    foreach (var cell in ranked)
                    {
                        if (chosen.Contains(cell) || !Touches(cell, chosen)) continue;
                        taken.Add(cell);
                        chosen.Add(cell);
                        grew = true;
                        break;
                    }
                }
            }

            return taken;
        }

        static bool Touches(Vector2Int cell, HashSet<Vector2Int> set) =>
            set.Contains(cell + Vector2Int.up) || set.Contains(cell + Vector2Int.down) ||
            set.Contains(cell + Vector2Int.left) || set.Contains(cell + Vector2Int.right);

        static string Preview(List<Vector2Int> cells, Dictionary<Vector2Int, Direction> facings,
                              int width, int height)
        {
            var taken = new HashSet<Vector2Int>(cells);
            var sb = new StringBuilder();
            for (int y = height - 1; y >= 0; y--)
            {
                sb.Append("    ");
                for (int x = 0; x < width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!taken.Contains(cell)) { sb.Append(" ."); continue; }
                    var d = DirectionUtil.ToVector(facings[cell]);
                    sb.Append(d.y > 0 ? " ^" : d.y < 0 ? " v" : d.x > 0 ? " >" : " <");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
