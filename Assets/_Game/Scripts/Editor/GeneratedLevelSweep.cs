using System.Collections.Generic;
using System.IO;
using System.Text;
using SheepGame.Core;
using SheepGame.Core.Meta;
using UnityEditor;
using UnityEngine;

namespace SheepGame.EditorTools
{
    // Audits the levels the game deals past the end of the authored list.
    //
    // Worth having only because those levels are dealt from their number rather than from
    // chance: level 200 is a thing that exists and can be looked at before a player meets it.
    // The sweep deals each one exactly as GameManager would -- same seed, same piece count --
    // and reports the ones that look wrong, so a bad board can be found and replaced instead
    // of arriving as "stuck on 200" in a support message.
    //
    // Nothing is spawned. The board's own rule for whether a piece may leave is
    // LevelSolver.PathClear, and playing a level here is just that rule applied over a set of
    // occupied cells.
    public static class GeneratedLevelSweep
    {
        // A player is simulated several times and the median taken, because one run of
        // anything with a die in it says very little.
        const int Runs = 25;

        // How far ahead the simulated player checks before tapping. Two cells: they read the
        // arrow and glance at what sits next to the piece, and are caught out by a blocker
        // further down the lane. A player who traced the whole lane would never waste a tap and
        // would tell us nothing -- every level would cost exactly one tap per piece.
        const int Foresight = 2;

        // A step with one free piece is only interesting while there is still a board. At the
        // very end one piece remains and it is free by definition, which flagged every level
        // ever generated and made the measure worthless.
        const int TailPieces = 3;

        // Forced steps -- moments where the player has no choice at all -- above which the
        // level reads as a search for the one piece that moves rather than as a puzzle.
        //
        // A share of the level's own length, with the old flat 3 kept as the floor for small
        // ones. Three forced moments in a board of thirty-four is one tap in eleven and is
        // what the flat number was measuring; the same three in a board of fifty is one tap
        // in seventeen and is not the same level at all. Once the generator started packing
        // the board the flat number flagged a third of every level ever dealt, all of them
        // with four to nine pieces free at every step -- which is no kind of search.
        const int ForcedLimit = 3;
        const float ForcedShare = 0.08f;

        // Openness above which a level has so many pieces free at once that no order has to be
        // found -- as a share of the pieces still on the board.
        const float TrivialShare = 0.55f;

        [MenuItem("SheepGame/Sweep Generated Levels")]
        public static void Sweep()
        {
            var manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogWarning("Sweep Generated Levels: open the game scene first, or call Run " +
                                 "with the board's settings.");
                return;
            }

            int authored = manager.AuthoredLevels;
            Run(authored + 1, 300, manager.gridWidth, manager.gridHeight, manager.longPieceShare,
                manager.economy, authored);
        }

        // Takes the board's settings rather than fetching them, so the sweep can be run against
        // a scene that is not the one open -- which is most of the time, since the loading scene
        // is where a session usually starts and it has no GameManager in it at all.
        public static void Run(int from, int to, int gridWidth, int gridHeight, float longShare,
                               EconomyConfig economy, int authored)
        {
            if (from <= authored)
            {
                Debug.LogWarning($"Sweep Generated Levels: levels 1..{authored} are authored assets " +
                                 $"and are not dealt by the generator. Sweeping from {authored + 1}.");
                from = authored + 1;
            }

            var report = new StringBuilder();
            report.AppendLine($"Generated levels {from}..{to}, dealt exactly as the game deals them.");
            report.AppendLine();
            report.AppendLine("level  pieces  cells  fill%  budget  forced  avgFree   taps  flags");

            int unsolvable = 0, tight = 0, bottleneck = 0, trivial = 0;
            int boardCells = gridWidth * gridHeight;
            float fillSum = 0f;
            int leanest = int.MaxValue;

            for (int level = from; level <= to; level++)
            {
                // Both dealt the way the game deals them, which since the ramp moved onto
                // EconomyConfig means asking it rather than repeating its arithmetic here.
                float share = economy != null ? economy.LongShareFor(level, authored) : longShare;
                int pieces = economy != null
                    ? economy.PieceCountFor(level, boardCells, authored)
                    : Mathf.Clamp(8 + level * 2, 8, boardCells);

                var data = LevelGenerator.Generate(pieces, gridWidth, gridHeight, 1,
                                                   GameManager.SeedFor(level), share);

                int budget = economy != null ? economy.TapBudget(data.pieces.Count, level) : 0;

                // Cells, not pieces: a long one takes two, and how full the board looks is
                // the thing the fill ramp is actually steering.
                int used = 0;
                foreach (var entry in data.pieces) used += Mathf.Max(1, entry.cells);
                fillSum += used / (float)boardCells;
                leanest = Mathf.Min(leanest, used);

                bool solvable = LevelSolver.TrySolve(data, out var stuck);
                Openness(data, out int forced, out float avgFree);
                int taps = PlayerTaps(data, level);

                var flags = new List<string>();
                if (!solvable) { flags.Add($"UNSOLVABLE ({stuck.Count} stuck)"); unsolvable++; }
                if (budget > 0 && taps > budget) { flags.Add("TIGHT"); tight++; }
                if (forced >= Mathf.Max(ForcedLimit, data.pieces.Count * ForcedShare))
                { flags.Add("FORCED"); bottleneck++; }
                if (avgFree > data.pieces.Count * TrivialShare) { flags.Add("TRIVIAL"); trivial++; }

                if (flags.Count > 0)
                    report.AppendLine($"{level,5}  {data.pieces.Count,6}  {used,5}  " +
                                      $"{100f * used / boardCells,5:0}  {budget,6}  {forced,6}  " +
                                      $"{avgFree,7:0.0}  {taps,5}  {string.Join(", ", flags)}");
            }

            report.AppendLine();
            report.AppendLine($"fill: {100f * fillSum / (to - from + 1):0.0}% of the board on average, " +
                              $"leanest deal {leanest} cells of {boardCells}.");
            report.AppendLine($"unsolvable {unsolvable}   tight {tight}   bottleneck {bottleneck}   trivial {trivial}");
            report.AppendLine();
            report.AppendLine("UNSOLVABLE  the deal cannot be cleared at all -- a generator bug, not a hard level.");
            report.AppendLine("TIGHT       a player who reads the arrows but not the whole lane runs out of taps.");
            report.AppendLine("FORCED      steps where exactly one piece could move, counted while more than");
            report.AppendLine("            three remain, against a share of the level's own length. A level");
            report.AppendLine("            made of these is a search, not a puzzle.");
            report.AppendLine("TRIVIAL     so many pieces are free at once that no order has to be found.");

            string path = Path.Combine(Path.GetTempPath(), "generated_level_sweep.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log($"Swept {to - from + 1} generated levels: {unsolvable} unsolvable, {tight} tight, " +
                      $"{bottleneck} bottleneck, {trivial} trivial.\nReport: {path}");
        }

        // ---- measuring a deal ------------------------------------------------------------

        // How much choice the level offers, walked once from start to finish. The free set is
        // counted before each removal, and the piece taken is always the first free one, so the
        // walk is one definite play-through rather than an average over all of them.
        static void Openness(LevelData level, out int forced, out float avgFree)
        {
            var occupied = Occupancy(level, out var cells);
            var gone = new bool[level.pieces.Count];
            int remaining = level.pieces.Count;

            forced = 0;
            float total = 0f;
            int steps = 0;

            while (remaining > 0)
            {
                int free = 0, take = -1;
                for (int i = 0; i < level.pieces.Count; i++)
                {
                    if (gone[i] || !Clear(level, i, occupied)) continue;
                    free++;
                    if (take < 0) take = i;
                }

                if (free == 0) break;      // TrySolve reports this properly; nothing to measure

                if (free == 1 && remaining > TailPieces) forced++;
                total += free;
                steps++;

                foreach (var cell in cells[take]) occupied.Remove(cell);
                gone[take] = true;
                remaining--;
            }

            avgFree = steps > 0 ? total / steps : 0f;
        }

        // Taps a plausible player spends. They read the arrows, so they only try pieces with
        // room in front of them, but they check only a couple of cells ahead and are caught by
        // a blocker further down the lane -- which is exactly where a wasted tap comes from.
        // When nothing looks free they try anything, as a stuck player does.
        static int PlayerTaps(LevelData level, int levelNumber)
        {
            var results = new List<int>(Runs);
            var rng = new System.Random(GameManager.SeedFor(levelNumber) ^ 0x5f3759df);

            for (int run = 0; run < Runs; run++)
            {
                var occupied = Occupancy(level, out var cells);
                var gone = new bool[level.pieces.Count];
                int remaining = level.pieces.Count;
                int taps = 0;

                // Generous, so a hopeless run ends rather than spins: far more taps than any
                // budget would ever allow.
                int ceiling = level.pieces.Count * 40;

                var candidates = new List<int>(level.pieces.Count);

                while (remaining > 0 && taps < ceiling)
                {
                    candidates.Clear();
                    for (int i = 0; i < level.pieces.Count; i++)
                        if (!gone[i] && LooksFree(level, i, occupied)) candidates.Add(i);

                    // Nothing looks promising, so they tap something and hope.
                    if (candidates.Count == 0)
                        for (int i = 0; i < level.pieces.Count; i++)
                            if (!gone[i]) candidates.Add(i);

                    int pick = candidates[rng.Next(candidates.Count)];

                    taps++;
                    if (!Clear(level, pick, occupied)) continue;

                    foreach (var cell in cells[pick]) occupied.Remove(cell);
                    gone[pick] = true;
                    remaining--;
                }

                results.Add(taps);
            }

            results.Sort();
            return results[results.Count / 2];
        }

        // What the player can tell at a glance: the first few cells in front of the piece.
        static bool LooksFree(LevelData level, int index, HashSet<Vector2Int> occupied)
        {
            var entry = level.pieces[index];
            Vector2Int delta = DirectionUtil.ToVector(entry.direction);
            Vector2Int pos = entry.position + delta;

            for (int step = 0; step < Foresight; step++)
            {
                if (pos.x < 0 || pos.x >= level.gridWidth || pos.y < 0 || pos.y >= level.gridHeight)
                    return true;                 // already off the board: plainly clear
                if (occupied.Contains(pos)) return false;
                pos += delta;
            }
            return true;
        }

        static bool Clear(LevelData level, int index, HashSet<Vector2Int> occupied) =>
            LevelSolver.PathClear(level.pieces[index].position, level.pieces[index].direction,
                                  level.gridWidth, level.gridHeight, occupied);

        static HashSet<Vector2Int> Occupancy(LevelData level, out List<Vector2Int>[] cells)
        {
            var occupied = new HashSet<Vector2Int>();
            cells = new List<Vector2Int>[level.pieces.Count];

            for (int i = 0; i < level.pieces.Count; i++)
            {
                cells[i] = new List<Vector2Int>(LevelSolver.CellsOf(level.pieces[i]));
                foreach (var cell in cells[i]) occupied.Add(cell);
            }

            return occupied;
        }
    }
}
