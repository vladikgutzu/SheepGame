using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // Builds a level by placing animals in the reverse of an escape order: each new
    // animal is dropped onto an empty cell with a direction whose path-to-the-edge is
    // currently clear of already-placed animals. Escaping animals in the exact reverse
    // of placement order is therefore always legal, guaranteeing at least one solvable
    // click order exists for the generated board.
    public static class LevelGenerator
    {
        public static LevelData Generate(int pieceCount, int gridWidth, int gridHeight, int variantCount = 1,
                                         int? seed = null, float longShare = 0f)
        {
            var rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            var placements = GeneratePositionsInternal(pieceCount, gridWidth, gridHeight, rng, longShare);

            var data = ScriptableObject.CreateInstance<LevelData>();
            data.gridWidth = gridWidth;
            data.gridHeight = gridHeight;

            foreach (var (pos, dir, cells) in placements)
            {
                data.pieces.Add(new LevelPieceEntry
                {
                    position = pos,
                    direction = dir,
                    // Left at 0 for a long piece: the board picks a model of the right size
                    // itself, and a random index here could name a one-cell suitcase.
                    variantIndex = cells <= 1 && variantCount > 1 ? rng.Next(variantCount) : 0,
                    cells = cells,
                });
            }

            return data;
        }

        public static List<(Vector2Int pos, Direction dir, int cells)> GeneratePositions(
            int count, int gridWidth, int gridHeight, int? seed = null, float longShare = 0f)
        {
            var rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            return GeneratePositionsInternal(count, gridWidth, gridHeight, rng, longShare);
        }

        // longShare is the chance each piece is asked to be two cells long. Asked, not
        // promised: a long piece needs a free cell behind it as well as a clear run ahead, and
        // on a crowded board there is often nowhere for one, so it falls back to a single
        // rather than giving up the placement.
        static List<(Vector2Int, Direction, int)> GeneratePositionsInternal(
            int count, int gridWidth, int gridHeight, System.Random rng, float longShare = 0f)
        {
            var emptyCells = new List<Vector2Int>();
            for (int x = 0; x < gridWidth; x++)
                for (int y = 0; y < gridHeight; y++)
                    emptyCells.Add(new Vector2Int(x, y));

            if (count > emptyCells.Count) count = emptyCells.Count;

            var occupied = new HashSet<Vector2Int>();
            var placements = new List<(Vector2Int, Direction, int)>();
            var empties = new HashSet<Vector2Int>(emptyCells);

            // Which cell to take next is the whole difference between a board with holes in
            // it and a board that is full.
            //
            // Taken at random, the deal saturates at about 77% of the cells however many
            // pieces are asked for -- measured over forty deals on a 7x10, where asking for
            // seventy pieces gives exactly the same board as asking for fifty. The reason is
            // the placement rule itself: an inner cell can only be filled while it still has
            // a clear run to an edge, whereas an edge cell facing outwards has a run of no
            // cells at all and can be filled at any point, right up to the last one. Picked
            // at random the edge cells go early, and the holes left behind are inner ones
            // with nothing clear left to run through, which nothing can ever fill.
            //
            // Taking the most hemmed-in cell first, and the deepest among equals, is the
            // same rule read the other way round: spend the cells that are about to stop
            // being usable and leave the ones that never will. That fills the board --
            // seventy cells of seventy in 58 deals out of 60, and never fewer than 69.
            //
            // Only for a deal that wants most of the board, though: below roughly three
            // fifths the random order has room to waste, and a level that is not meant to be
            // full should not look like one packed from the middle outwards.
            bool packing = count * (1f + Mathf.Clamp01(longShare)) > emptyCells.Count * 0.6f;

            for (int i = 0; i < count; i++)
            {
                if (emptyCells.Count == 0) break;

                // Two cells if the dice say so and the board can take one; one otherwise. The
                // long attempt comes first so a crowded board degrades to singles instead of
                // stalling.
                int wanted = rng.NextDouble() < longShare ? 2 : 1;
                bool placed = false;

                for (int size = wanted; size >= 1 && !placed; size--)
                {
                    if (packing)
                    {
                        placed = PlaceTightest(size, gridWidth, gridHeight, rng,
                                               occupied, emptyCells, empties, placements);
                        continue;
                    }

                    int maxAttempts = emptyCells.Count * 2;
                    for (int attempt = 0; attempt < maxAttempts && !placed; attempt++)
                    {
                        var cell = emptyCells[rng.Next(emptyCells.Count)];
                        var dirs = ValidDirectionsFrom(cell, gridWidth, gridHeight, occupied, size, empties);
                        if (dirs.Count == 0) continue;

                        Place(cell, Varied(dirs, placements, rng), size,
                              occupied, emptyCells, empties, placements);
                        placed = true;
                    }

                    // The random darts missed; sweep for anywhere at all that works.
                    if (placed) continue;
                    foreach (var cell in emptyCells)
                    {
                        var dirs = ValidDirectionsFrom(cell, gridWidth, gridHeight, occupied, size, empties);
                        if (dirs.Count == 0) continue;

                        Place(cell, dirs[0], size, occupied, emptyCells, empties, placements);
                        placed = true;
                        break;
                    }
                }

                if (!placed) break;
            }

            return placements;
        }

        // Takes the cell with the fewest ways out left, and the deepest one among those.
        // Both halves earn their keep: the count is what is about to run out, and the depth
        // is the tie-break on a board that is still empty, where every cell has all four
        // ways out and the count says nothing at all. Anything still tied goes to the dice,
        // so a packed board is still a different board on every seed.
        static bool PlaceTightest(int size, int width, int height, System.Random rng,
                                  HashSet<Vector2Int> occupied, List<Vector2Int> emptyCells,
                                  HashSet<Vector2Int> empties,
                                  List<(Vector2Int, Direction, int)> placements)
        {
            int bestWays = int.MaxValue, bestDepth = -1, seen = 0;
            Vector2Int bestCell = default;
            List<Direction> bestDirs = null;

            foreach (var cell in emptyCells)
            {
                var dirs = ValidDirectionsFrom(cell, width, height, occupied, size, empties);
                if (dirs.Count == 0) continue;

                int depth = Mathf.Min(Mathf.Min(cell.x, width - 1 - cell.x),
                                      Mathf.Min(cell.y, height - 1 - cell.y));

                if (dirs.Count > bestWays) continue;
                if (dirs.Count == bestWays && depth < bestDepth) continue;

                // Reservoir sampling across the run of equals: uniform among them in one
                // pass, and without a second list to hold them.
                if (dirs.Count == bestWays && depth == bestDepth)
                {
                    seen++;
                    if (rng.Next(seen) != 0) continue;
                }
                else seen = 1;

                bestWays = dirs.Count;
                bestDepth = depth;
                bestCell = cell;
                bestDirs = dirs;
            }

            if (bestDirs == null) return false;

            Place(bestCell, Varied(bestDirs, placements, rng), size,
                  occupied, emptyCells, empties, placements);
            return true;
        }

        // Of the ways a cell could face, the one the board has least of so far. Left to the
        // dice, a packed board is mostly the few directions the tightest cells allow and reads
        // as stripes; taking the rarest spreads all eight across it.
        static Direction Varied(List<Direction> dirs, List<(Vector2Int, Direction, int)> placements,
                                System.Random rng)
        {
            var used = new int[System.Enum.GetValues(typeof(Direction)).Length];
            foreach (var p in placements) used[(int)p.Item2]++;

            int least = int.MaxValue, seen = 0;
            Direction pick = dirs[0];
            foreach (var d in dirs)
            {
                int n = used[(int)d];
                if (n > least) continue;
                if (n < least) { least = n; seen = 0; }
                if (rng.Next(++seen) == 0) pick = d;
            }
            return pick;
        }

        // Marks every cell the piece stands on. Its front is where it leaves from, and the
        // rest trails backwards, which is exactly how the board reads a level back.
        static void Place(Vector2Int front, Direction dir, int cells,
                          HashSet<Vector2Int> occupied, List<Vector2Int> emptyCells,
                          HashSet<Vector2Int> empties, List<(Vector2Int, Direction, int)> placements)
        {
            Vector2Int back = -DirectionUtil.ToVector(dir);
            for (int i = 0; i < cells; i++)
            {
                Vector2Int cell = front + back * i;
                occupied.Add(cell);
                emptyCells.Remove(cell);
                empties.Remove(cell);
            }

            placements.Add((front, dir, cells));
        }

        static List<Direction> ValidDirectionsFrom(Vector2Int cell, int width, int height,
                                                  HashSet<Vector2Int> occupied, int cells = 1,
                                                  HashSet<Vector2Int> empties = null)
        {
            var result = new List<Direction>();
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                if (!DirectionUtil.AllowedFor(dir, cells)) continue;
                var delta = DirectionUtil.ToVector(dir);

                // Room for the piece itself first: a long one needs the cells behind its front
                // free and on the board, and that depends on which way it is facing.
                if (cells > 1)
                {
                    bool room = true;
                    for (int i = 1; i < cells && room; i++)
                    {
                        Vector2Int tail = cell - delta * i;
                        if (!InBounds(tail, width, height)) room = false;
                        else if (empties != null && !empties.Contains(tail)) room = false;
                        else if (occupied.Contains(tail)) room = false;
                    }
                    if (!room) continue;
                }

                var pos = cell + delta;
                bool clear = true;
                while (InBounds(pos, width, height))
                {
                    if (occupied.Contains(pos)) { clear = false; break; }
                    pos += delta;
                }
                if (clear) result.Add(dir);
            }
            return result;
        }

        static bool InBounds(Vector2Int pos, int width, int height) =>
            pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
    }
}
