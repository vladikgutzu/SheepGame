using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    // Direction assignment and solvability checking for hand-authored boards.
    //
    // Removing a piece only ever clears an obstacle, so a piece that can escape now can
    // still escape later. That makes greedy removal both safe and complete: if a board
    // cannot be emptied by repeatedly taking whatever is currently escapable, no other
    // order would have worked either.
    public static class LevelSolver
    {
        static readonly Direction[] AllDirections =
            (Direction[])System.Enum.GetValues(typeof(Direction));

        /// <summary>A piece as a set of cells, before it has been given a facing.</summary>
        // Orientation-free on purpose: a designer paints where a piece sits, and which way it
        // then leaves is the solver's answer. For a piece longer than one cell that also
        // decides which of its cells is the front, so the shape cannot carry that either.
        public readonly struct PieceShape
        {
            public readonly Vector2Int[] Cells;
            public PieceShape(params Vector2Int[] cells) { Cells = cells; }

            /// <summary>
            /// The directions this shape could leave in: any axis its cells lie along. A
            /// single cell may go all four ways; a domino only the two along its length,
            /// because a piece cannot travel sideways.
            /// </summary>
            public bool Allows(Direction d)
            {
                if (Cells.Length <= 1) return true;

                Vector2Int step = DirectionUtil.ToVector(d);
                Vector2Int first = Cells[0];
                foreach (var c in Cells)
                {
                    Vector2Int delta = c - first;
                    // Collinear with the direction: the off-axis component must be zero.
                    if (step.x != 0 && delta.y != 0) return false;
                    if (step.y != 0 && delta.x != 0) return false;
                }
                return true;
            }

            /// <summary>The cell that leads when travelling this way.</summary>
            public Vector2Int Front(Direction d)
            {
                Vector2Int step = DirectionUtil.ToVector(d);
                Vector2Int best = Cells[0];
                int bestScore = best.x * step.x + best.y * step.y;

                foreach (var c in Cells)
                {
                    int score = c.x * step.x + c.y * step.y;
                    if (score > bestScore) { best = c; bestScore = score; }
                }
                return best;
            }
        }

        // Hands every piece a facing that leaves the board clearable.
        //
        // It plays the level backwards: pick a piece still on the board whose path out is
        // clear of the pieces not yet picked, record that facing, and lift it off. Removing
        // pieces only ever frees space, so the reverse of the order found is a solution.
        //
        // For single cells a pick always exists -- the topmost remaining cell can always go
        // up -- so this cannot fail on a plain silhouette. With longer pieces it can, because
        // a piece may only travel along its own length: a horizontal pair boxed in on both
        // sides has nowhere to go. That is what the false return is for; the caller reseeds.
        public static bool TryAssignDirections(IList<PieceShape> pieces, int width, int height, int seed,
                                               out List<LevelPieceEntry> assigned)
        {
            var rng = new System.Random(seed);
            assigned = new List<LevelPieceEntry>();

            var live = new List<int>();
            var occupied = new HashSet<Vector2Int>();
            for (int i = 0; i < pieces.Count; i++)
            {
                bool inside = true;
                foreach (var c in pieces[i].Cells) if (!InBounds(c, width, height)) inside = false;
                if (!inside) continue;

                live.Add(i);
                foreach (var c in pieces[i].Cells) occupied.Add(c);
            }

            var options = new List<KeyValuePair<int, Direction>>();

            while (live.Count > 0)
            {
                options.Clear();
                foreach (int i in live)
                {
                    var shape = pieces[i];
                    foreach (var dir in AllDirections)
                    {
                        if (!shape.Allows(dir)) continue;
                        // The run starts one cell ahead of the front, so a piece never meets
                        // its own cells and they need no excluding.
                        if (PathClear(shape.Front(dir), dir, width, height, occupied))
                            options.Add(new KeyValuePair<int, Direction>(i, dir));
                    }
                }

                if (options.Count == 0) return false;

                var pick = options[rng.Next(options.Count)];
                var picked = pieces[pick.Key];

                assigned.Add(new LevelPieceEntry
                {
                    position = picked.Front(pick.Value),
                    direction = pick.Value,
                    cells = picked.Cells.Length,
                });

                live.Remove(pick.Key);
                foreach (var c in picked.Cells) occupied.Remove(c);
            }

            return true;
        }

        /// <summary>The old single-cell form, kept for callers that paint plain silhouettes.</summary>
        public static bool TryAssignDirections(IList<Vector2Int> cells, int width, int height, int seed,
                                               out Dictionary<Vector2Int, Direction> assignment)
        {
            var shapes = new List<PieceShape>(cells.Count);
            foreach (var c in cells) shapes.Add(new PieceShape(c));

            assignment = new Dictionary<Vector2Int, Direction>();
            if (!TryAssignDirections(shapes, width, height, seed, out var assigned)) return false;

            foreach (var entry in assigned) assignment[entry.position] = entry.direction;
            return true;
        }

        // Replays the board with the facings as authored. Returns the pieces that could
        // never leave, so the editor can point at exactly what is wedged.
        public static bool TrySolve(LevelData level, out List<Vector2Int> stuck)
        {
            // Walked as a list of pieces rather than as a set of cells, because a piece is no
            // longer the same thing as a cell: a long one covers several, and all of them have
            // to leave together. Keying by position, as this did, also quietly collapsed two
            // entries that shared a cell into one.
            var pieces = level.pieces;
            var occupied = new HashSet<Vector2Int>();
            foreach (var piece in pieces)
                foreach (var cell in CellsOf(piece)) occupied.Add(cell);

            var gone = new bool[pieces.Count];
            int remaining = pieces.Count;

            bool progressed = true;
            while (progressed && remaining > 0)
            {
                progressed = false;

                for (int i = 0; i < pieces.Count; i++)
                {
                    if (gone[i]) continue;

                    // The run starts one cell ahead of the front, so a piece never meets its
                    // own tail on the way out and needs no special case for itself.
                    if (!PathClear(pieces[i].position, pieces[i].direction,
                                   level.gridWidth, level.gridHeight, occupied)) continue;

                    foreach (var cell in CellsOf(pieces[i])) occupied.Remove(cell);
                    gone[i] = true;
                    remaining--;
                    progressed = true;
                }
            }

            stuck = new List<Vector2Int>();
            for (int i = 0; i < pieces.Count; i++)
                if (!gone[i]) stuck.Add(pieces[i].position);

            return stuck.Count == 0;
        }

        /// <summary>Every cell an entry stands on: its own, then backwards away from its facing.</summary>
        public static IEnumerable<Vector2Int> CellsOf(LevelPieceEntry entry)
        {
            Vector2Int back = -DirectionUtil.ToVector(entry.direction);
            for (int i = 0; i < entry.Cells; i++) yield return entry.position + back * i;
        }

        public static bool PathClear(Vector2Int cell, Direction dir, int width, int height,
                                     ICollection<Vector2Int> occupied)
        {
            Vector2Int delta = DirectionUtil.ToVector(dir);
            Vector2Int pos = cell + delta;

            while (InBounds(pos, width, height))
            {
                if (occupied.Contains(pos)) return false;
                pos += delta;
            }
            return true;
        }

        static bool InBounds(Vector2Int pos, int width, int height) =>
            pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
    }
}
