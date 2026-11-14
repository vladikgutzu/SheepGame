using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Replays a coloured level without a scene: finds an order the cases can leave in,
    /// measures how much queue that order needs, and searches for any way through at all.
    /// </summary>
    // The player never has to decide whether to take a case that matches the cart: taking it
    // can only help, because a removal never blocks anything and the case has to board that
    // colour eventually. So every search here takes matching cases greedily and only branches
    // on the one real decision -- which case to park in the queue when nothing matches.
    public static class ColourSolver
    {
        /// <summary>
        /// An order every case can leave in, ignoring colour. Null if the facings wedge.
        /// </summary>
        public static List<int> EscapeOrder(IList<LevelPieceEntry> pieces, int width, int height)
        {
            var occupied = new HashSet<Vector2Int>();
            foreach (var p in pieces)
                foreach (var c in LevelSolver.CellsOf(p)) occupied.Add(c);

            var order = new List<int>(pieces.Count);
            var gone = new bool[pieces.Count];

            bool progressed = true;
            while (progressed && order.Count < pieces.Count)
            {
                progressed = false;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (gone[i]) continue;
                    if (!LevelSolver.PathClear(pieces[i].position, pieces[i].direction, width, height, occupied))
                        continue;

                    foreach (var c in LevelSolver.CellsOf(pieces[i])) occupied.Remove(c);
                    gone[i] = true;
                    order.Add(i);
                    progressed = true;
                }
            }

            return order.Count == pieces.Count ? order : null;
        }

        /// <summary>
        /// The most cases that sit in the queue at once if the board is cleared in exactly this
        /// order. A level whose peak is under its queue size can always be finished.
        /// </summary>
        public static int PeakQueue(IList<int> colourInOrder, IList<int> carts, int capacity)
        {
            var queue = new List<int>();
            int cart = 0, fill = 0, peak = 0;

            foreach (int colour in colourInOrder)
            {
                if (cart < carts.Count && colour == carts[cart])
                {
                    fill++;
                    while (fill >= capacity && cart < carts.Count)
                    {
                        cart++;
                        fill = 0;
                        if (cart >= carts.Count) break;
                        for (int i = 0; i < queue.Count && fill < capacity;)
                        {
                            if (queue[i] == carts[cart]) { queue.RemoveAt(i); fill++; }
                            else i++;
                        }
                    }
                }
                else
                {
                    queue.Add(colour);
                    peak = Mathf.Max(peak, queue.Count);
                }
            }

            return peak;
        }

        /// <summary>Whether each colour's cases fill exactly the carts asked of it.</summary>
        public static bool CountsMatch(IList<LevelPieceEntry> pieces, IList<int> carts, int capacity, out string problem)
        {
            var have = new Dictionary<int, int>();
            var want = new Dictionary<int, int>();
            foreach (var p in pieces) have[p.colour] = (have.TryGetValue(p.colour, out int h) ? h : 0) + 1;
            foreach (int c in carts) want[c] = (want.TryGetValue(c, out int w) ? w : 0) + capacity;

            var sb = new StringBuilder();
            foreach (var kv in have)
                if (!want.TryGetValue(kv.Key, out int w) || w != kv.Value)
                    sb.Append($"colour {kv.Key}: {kv.Value} cases for {(want.TryGetValue(kv.Key, out int x) ? x : 0)} seats. ");
            foreach (var kv in want)
                if (!have.ContainsKey(kv.Key))
                    sb.Append($"colour {kv.Key}: no cases for {kv.Value} seats. ");

            problem = sb.ToString();
            return problem.Length == 0;
        }

        /// <summary>
        /// Searches for any way through the level with this much queue. Null when the search
        /// ran out of budget without an answer.
        /// </summary>
        // Editor tooling only: a big board with a small queue can take a while to prove
        // impossible, which is what the budget is for. Nothing at run time depends on it.
        public static bool? Solve(LevelData level, int queueCapacity, int budget = 200000)
        {
            var pieces = level.pieces;
            int n = pieces.Count;
            if (n > 64) return null;

            var cells = new List<Vector2Int>[n];
            for (int i = 0; i < n; i++) cells[i] = new List<Vector2Int>(LevelSolver.CellsOf(pieces[i]));

            int capacity = level.CartCapacity;
            var carts = level.carts;
            var seen = new HashSet<string>();
            int nodes = 0;
            bool outOfBudget = false;

            bool Free(ulong remaining, int i)
            {
                var occupied = new HashSet<Vector2Int>();
                for (int j = 0; j < n; j++)
                    if ((remaining & (1UL << j)) != 0)
                        foreach (var c in cells[j]) occupied.Add(c);
                return LevelSolver.PathClear(pieces[i].position, pieces[i].direction,
                                             level.gridWidth, level.gridHeight, occupied);
            }

            // Takes every matching free case and pours the queue into arriving carts.
            void Settle(ref ulong remaining, ref int cart, ref int fill, List<int> queue)
            {
                while (cart < carts.Count)
                {
                    int want = carts[cart];
                    for (int q = 0; q < queue.Count && fill < capacity;)
                    {
                        if (queue[q] == want) { queue.RemoveAt(q); fill++; }
                        else q++;
                    }
                    if (fill >= capacity) { cart++; fill = 0; continue; }

                    int match = -1;
                    for (int i = 0; i < n && match < 0; i++)
                        if ((remaining & (1UL << i)) != 0 && pieces[i].colour == want && Free(remaining, i))
                            match = i;

                    if (match < 0) return;
                    remaining &= ~(1UL << match);
                    fill++;
                }
            }

            bool Recurse(ulong remaining, int cart, int fill, List<int> queue)
            {
                if (cart >= carts.Count) return true;
                if (++nodes > budget) { outOfBudget = true; return false; }

                queue.Sort();
                string key = remaining + ":" + cart + ":" + fill + ":" + string.Join(",", queue);
                if (!seen.Add(key)) return false;
                if (queue.Count >= queueCapacity) return false;

                for (int i = 0; i < n; i++)
                {
                    if ((remaining & (1UL << i)) == 0 || !Free(remaining, i)) continue;

                    ulong r = remaining & ~(1UL << i);
                    int c = cart, f = fill;
                    var q = new List<int>(queue) { pieces[i].colour };
                    Settle(ref r, ref c, ref f, q);
                    if (Recurse(r, c, f, q)) return true;
                    if (outOfBudget) return false;
                }
                return false;
            }

            ulong all = n == 64 ? ulong.MaxValue : (1UL << n) - 1;
            int startCart = 0, startFill = 0;
            var startQueue = new List<int>();
            Settle(ref all, ref startCart, ref startFill, startQueue);

            bool solved = Recurse(all, startCart, startFill, startQueue);
            if (solved) return true;
            return outOfBudget ? (bool?)null : false;
        }
    }
}
