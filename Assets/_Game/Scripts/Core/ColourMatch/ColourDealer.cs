using System.Collections.Generic;
using UnityEngine;

namespace SheepGame.Core
{
    /// <summary>
    /// Colours a level that was built without colours -- every generated level and the
    /// daily -- and deals the carts that go with them.
    /// </summary>
    // It works from an order the board can actually be cleared in. The cases are coloured in
    // threes along that order, one cart per three, and then neighbours within a window are
    // allowed to trade colours. With no trading at all a player who follows the order never
    // queues anything; each trade pulls a case out of its cart's turn, and how far it may
    // travel is how much the player has to plan ahead. The window is the difficulty dial.
    //
    // The result is checked, not trusted: the order is replayed and the most cases it ever
    // parks in the queue has to leave a place spare. If it does not, the window shrinks and
    // the colours are dealt again, down to no trading at all, which parks nothing.
    public static class ColourDealer
    {
        public static bool Deal(LevelData level, int colours, int window, int queueSlots, int seed)
        {
            // Compared as a plain reference: the dealer only ever works on data, and this lets it
            // be run outside the engine, where every ScriptableObject reads as destroyed.
            if ((object)level == null || level.pieces == null) return false;

            var order = ColourSolver.EscapeOrder(level.pieces, level.gridWidth, level.gridHeight);
            if (order == null) return false;

            var rng = new System.Random(seed);
            int capacity = level.CartCapacity;
            colours = Mathf.Clamp(colours, 1, PieceColours.Count);

            // Seats come in whole carts, so the board does too. The cases dropped are the last
            // to leave in the order, and taking a case away never blocks another, so what is
            // left can still be cleared.
            int extra = level.pieces.Count % capacity;
            if (extra > 0)
            {
                var drop = new HashSet<int>(order.GetRange(order.Count - extra, extra));
                var kept = new List<LevelPieceEntry>();
                var remap = new Dictionary<int, int>();
                for (int i = 0; i < level.pieces.Count; i++)
                {
                    if (drop.Contains(i)) continue;
                    remap[i] = kept.Count;
                    kept.Add(level.pieces[i]);
                }

                var newOrder = new List<int>();
                foreach (int i in order) if (remap.TryGetValue(i, out int j)) newOrder.Add(j);
                level.pieces = kept;
                order = newOrder;
            }

            int carts = order.Count / capacity;
            if (carts == 0) return false;

            var cartColours = PickCarts(carts, colours, rng);

            for (int w = Mathf.Max(0, window); w >= 0; w--)
            {
                var inOrder = new List<int>(order.Count);
                foreach (int c in cartColours)
                    for (int s = 0; s < capacity; s++) inOrder.Add(c);

                for (int i = 0; i < inOrder.Count && w > 0; i++)
                {
                    int j = Mathf.Min(inOrder.Count - 1, i + rng.Next(w + 1));
                    (inOrder[i], inOrder[j]) = (inOrder[j], inOrder[i]);
                }

                // One place kept free: a level that needs every place is a level lost to the
                // first careless tap, which is a hand-made level's call to make, not the dealer's.
                if (w > 0 && ColourSolver.PeakQueue(inOrder, cartColours, capacity) > queueSlots - 2)
                    continue;

                for (int k = 0; k < order.Count; k++)
                {
                    var entry = level.pieces[order[k]];
                    entry.colour = inOrder[k];
                    level.pieces[order[k]] = entry;
                }

                level.carts = cartColours;
                level.coloured = true;
                if (level.queueSlots <= 0) level.queueSlots = queueSlots;
                return true;
            }

            return false;
        }

        // Balanced, and never the same colour twice running: two carts of one colour in a row
        // read as one cart that did not leave.
        static List<int> PickCarts(int count, int colours, System.Random rng)
        {
            var result = new List<int>(count);
            var used = new int[colours];

            for (int i = 0; i < count; i++)
            {
                int last = result.Count > 0 ? result[result.Count - 1] : -1;
                int least = int.MaxValue;
                for (int c = 0; c < colours; c++)
                    if (c != last || colours == 1) least = Mathf.Min(least, used[c]);

                var pool = new List<int>();
                for (int c = 0; c < colours; c++)
                    if ((c != last || colours == 1) && used[c] <= least + 1) pool.Add(c);

                int pick = pool[rng.Next(pool.Count)];
                result.Add(pick);
                used[pick]++;
            }

            return result;
        }
    }
}
