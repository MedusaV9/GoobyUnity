using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Pipe Flow — port of pipe_flow_logic.gd (web pipeFlow verbatim): seeded board generator (DFS path with the
    /// V8 Array.sort shuffle replica, tee/decoy rolls, scramble guard), water BFS, exact minimal-tap solver
    /// (relaxed Dijkstra heuristic + DFS with node budget), scoring and the release-bot autoplay.
    /// </summary>
    public static class PipeFlow
    {
        public sealed class Tune
        {
            public int Grid = 5;
            public double DurationSec = 90.0;
            public int SolvePoints = 25, BonusMax = 10, BonusFullExtra = 3, BonusZeroExtra = 15;
            public double TeeChance = 0.28;
            public int LeakFromPuzzle = 3;
            public double LeakSec = 25.0;
            public int LeakPenalty = 5;
            public double PreviewSpeedMult = 1.0, RotateSec = 0.16, FillStepSec = 0.09, FillEndDelaySec = 1.1;
            public bool Endless = false;
            public int EndlessFailureLimit = 3;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Pipe = new Tune();
        public const double DecoyStraight = 0.38, DecoyBend = 0.42;
        public const int DirN = 0, DirE = 1, DirS = 2, DirW = 3;
        private static readonly (int x, int y)[] Delta = { (0, -1), (1, 0), (0, 1), (-1, 0) };
        private static readonly int[] BaseMasks = { 0b0101, 0b0011, 0b0111 }; // straight, bend, tee
        private const int NoTaps = 9;
        private const int Unreachable = 1 << 28;
        private const int SolverNodeBudget = 200000;
        private static byte[] _minTaps;

        public struct Tile
        {
            public int Shape; // 0 straight, 1 bend, 2 tee
            public int Rot;
        }

        public sealed class Board
        {
            public int Size, SrcCol, GoalCol, OptimalTaps, Seed;
            public Tile[] Tiles;
        }

        public static string ShapeName(int shape) => shape == 0 ? "straight" : shape == 1 ? "bend" : "tee";

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode == "normal" || (mode != "easy" && mode != "hard" && mode != "endless")) return tune;
            var hard = mode == "hard" || mode == "endless";
            var previewMult = hard ? 1.15 : 0.85;
            var windowMult = hard ? 0.8 : 1.25;
            var o = tune.Clone();
            o.PreviewSpeedMult = previewMult;
            o.RotateSec = tune.RotateSec / previewMult;
            o.FillStepSec = tune.FillStepSec / previewMult;
            o.FillEndDelaySec = tune.FillEndDelaySec / previewMult;
            o.LeakSec = Math.Max(0.35, tune.LeakSec * windowMult);
            o.LeakFromPuzzle = Math.Max(1, tune.LeakFromPuzzle + (hard ? -1 : 1));
            o.Endless = mode == "endless";
            return o;
        }

        public static int Opposite(int d) => (d + 2) % 4;

        public static uint Hash32(string text)
        {
            var h = (uint)(1779033703 ^ text.Length);
            foreach (var ch in text)
            {
                h = unchecked((h ^ ch) * 3432918353u);
                h = (h << 13) | (h >> 19);
            }
            h = unchecked((h ^ (h >> 16)) * 2246822507u);
            h = unchecked((h ^ (h >> 13)) * 3266489909u);
            return h ^ (h >> 16);
        }

        public static int MaskOf(int shape, int rot)
        {
            var b = BaseMasks[shape];
            var r = rot & 3;
            return ((b << r) | (b >> (4 - r))) & 0b1111;
        }

        public static bool HasConnection(Tile t, int dir) => (MaskOf(t.Shape, t.Rot) & (1 << dir)) != 0;

        public static Tile RotateTile(Tile t) => new Tile { Shape = t.Shape, Rot = (t.Rot + 1) % 4 };

        public static int LeakJointFor(Board board, int puzzleNo, Tune tune)
        {
            if (puzzleNo < tune.LeakFromPuzzle) return -1;
            return (int)(Hash32($"leak:{board.Seed}:{puzzleNo}") % (uint)board.Tiles.Length);
        }

        public static bool LeakPenaltyDue(double puzzleElapsed, bool alreadyApplied, Tune tune) => !alreadyApplied && puzzleElapsed >= tune.LeakSec;

        private static void EnsureTapsTable()
        {
            if (_minTaps != null) return;
            var table = new byte[3 * 64];
            for (var i = 0; i < table.Length; i++) table[i] = NoTaps;
            for (var shape = 0; shape < 3; shape++)
            {
                var b = BaseMasks[shape];
                for (var rot = 0; rot < 4; rot++)
                for (var need = 0; need < 16; need++)
                for (var k = 0; k < 4; k++)
                {
                    var r = (rot + k) & 3;
                    var mask = ((b << r) | (b >> (4 - r))) & 0b1111;
                    if ((mask & need) == need) { table[shape * 64 + rot * 16 + need] = (byte)k; break; }
                }
            }
            _minTaps = table;
        }

        public static int TapsLookup(int shape, int rot, int needMask)
        {
            EnsureTapsTable();
            return _minTaps[shape * 64 + (rot & 3) * 16 + needMask];
        }

        public static double MinTapsFor(Tile tile, IEnumerable<int> dirs)
        {
            var need = 0;
            foreach (var d in dirs) need |= 1 << d;
            var taps = TapsLookup(tile.Shape, tile.Rot, need);
            return taps == NoTaps ? double.PositiveInfinity : taps;
        }

        public static (bool solved, Dictionary<int, int> depths) WaterReach(Board board)
        {
            var size = board.Size;
            var masks = new int[board.Tiles.Length];
            for (var i = 0; i < masks.Length; i++) masks[i] = MaskOf(board.Tiles[i].Shape, board.Tiles[i].Rot);
            var depths = new Dictionary<int, int>();
            var src = board.SrcCol;
            if ((masks[src] & (1 << DirN)) == 0) return (false, depths);
            depths[src] = 0;
            var queue = new List<int> { src };
            var head = 0;
            while (head < queue.Count)
            {
                var idx = queue[head++];
                var col = idx % size;
                var row = (idx - col) / size;
                var depth = depths[idx];
                var mask = masks[idx];
                for (var dir = 0; dir < 4; dir++)
                {
                    if ((mask & (1 << dir)) == 0) continue;
                    var nc = col + Delta[dir].x;
                    var nr = row + Delta[dir].y;
                    if (nc < 0 || nc >= size || nr < 0 || nr >= size) continue;
                    var n = nr * size + nc;
                    if (depths.ContainsKey(n)) continue;
                    if ((masks[n] & (1 << Opposite(dir))) == 0) continue;
                    depths[n] = depth + 1;
                    queue.Add(n);
                }
            }
            var goal = (size - 1) * size + board.GoalCol;
            var solved = depths.ContainsKey(goal) && (masks[goal] & (1 << DirS)) != 0;
            return (solved, depths);
        }

        public static bool IsSolved(Board board) => WaterReach(board).solved;

        public static Board GenerateBoard(int seed, Tune tune)
        {
            var rng = new GoobyRng((int)Hash32($"pipe:{seed}"));
            var size = tune.Grid;
            var srcCol = (int)Math.Floor(rng.Next() * size);
            var goalCol = (int)Math.Floor(rng.Next() * size);
            var path = RandomPath(rng, size, srcCol, goalCol);
            var tiles = new Tile[size * size];
            var onPath = new HashSet<int>(path);
            for (var i = 0; i < path.Count; i++)
            {
                var idx = path[i];
                var inDir = DirN;
                if (i > 0)
                {
                    var prev = path[i - 1];
                    inDir = Opposite(DeltaIndex(idx % size - prev % size, idx / size - prev / size));
                }
                var outDir = DirS;
                if (i < path.Count - 1)
                {
                    var next = path[i + 1];
                    outDir = DeltaIndex(next % size - idx % size, next / size - idx / size);
                }
                var shape = ShapeForOpenings(rng, inDir, outDir, tune);
                tiles[idx] = new Tile { Shape = shape, Rot = SolvedRotFor(shape, new[] { inDir, outDir }, rng) };
            }
            for (var idx = 0; idx < size * size; idx++)
            {
                if (onPath.Contains(idx)) continue;
                tiles[idx] = new Tile { Shape = DecoyShape(rng), Rot = (int)Math.Floor(rng.Next() * 4.0) };
            }
            var board = new Board { Size = size, Tiles = tiles, SrcCol = srcCol, GoalCol = goalCol, OptimalTaps = 0, Seed = seed };
            for (var guard = 0; guard < 8; guard++)
            {
                for (var idx = 0; idx < size * size; idx++)
                {
                    var t = tiles[idx];
                    tiles[idx] = new Tile { Shape = t.Shape, Rot = (t.Rot + (int)Math.Floor(rng.Next() * 4.0)) % 4 };
                }
                if (!IsSolved(board)) break;
            }
            board.OptimalTaps = SolveBoard(board).taps.Count;
            return board;
        }

        public static (List<int> taps, bool solvable) SolveBoard(Board board)
        {
            if (IsSolved(board)) return (new List<int>(), true);
            var legs = SearchBestPath(board);
            if (legs.Count == 0) return (new List<int>(), false);
            var taps = new List<int>();
            foreach (var (idx, k) in legs) for (var i = 0; i < k; i++) taps.Add(idx);
            var copy = new Board { Size = board.Size, SrcCol = board.SrcCol, GoalCol = board.GoalCol, Seed = board.Seed, Tiles = (Tile[])board.Tiles.Clone() };
            foreach (var idx in taps) copy.Tiles[idx] = RotateTile(copy.Tiles[idx]);
            return (taps, IsSolved(copy));
        }

        public static int TapEfficiencyBonus(int totalTaps, int optimalTaps, Tune tune)
        {
            var extra = Math.Max(0, totalTaps - optimalTaps);
            if (extra <= tune.BonusFullExtra) return tune.BonusMax;
            if (extra >= tune.BonusZeroExtra) return 0;
            var span = (double)(tune.BonusZeroExtra - tune.BonusFullExtra);
            return (int)Math.Round(tune.BonusMax * (tune.BonusZeroExtra - extra) / span, MidpointRounding.AwayFromZero);
        }

        public static int PipeScore(int solved, int totalTaps, int optimalTaps, Tune tune, int leaks = 0)
        {
            if (solved <= 0) return 0;
            return Math.Max(0, tune.SolvePoints * solved + TapEfficiencyBonus(totalTaps, optimalTaps, tune) - Math.Max(0, leaks) * tune.LeakPenalty);
        }

        public static bool EndlessShouldEnd(int failures, Tune tune) => tune.Endless && failures >= tune.EndlessFailureLimit;

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Pipe, mode);
            var duration = tune.Endless ? 150.0 : tune.DurationSec;
            var elapsed = 0.0;
            int puzzle = 0, solved = 0, failures = 0, taps = 0, optimal = 0;
            while (elapsed < duration && failures < tune.EndlessFailureLimit)
            {
                puzzle++;
                var board = GenerateBoard(seed * 1009 + puzzle, tune);
                var solution = SolveBoard(board).taps;
                var solveSec = (3.2 + solution.Count * 0.34) * tune.PreviewSpeedMult;
                var leaking = puzzle >= tune.LeakFromPuzzle && solveSec >= tune.LeakSec;
                elapsed += solveSec + tune.FillEndDelaySec;
                if (elapsed > duration)
                {
                    if (tune.Endless) failures++;
                    break;
                }
                if (leaking) failures++;
                solved++;
                taps += solution.Count;
                optimal += solution.Count;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = PipeScore(solved, taps, optimal, tune, failures), ["solved"] = solved, ["failures"] = failures };
        }

        // ── generator internals ──────────────────────────────────────────────

        private static int DeltaIndex(int dc, int dr)
        {
            for (var i = 0; i < 4; i++) if (Delta[i].x == dc && Delta[i].y == dr) return i;
            return -1;
        }

        /// <summary>V8 Array.prototype.sort((a,b) => rng()-0.5) replica for 4 elements (binary insertion sort).</summary>
        private static int[] ShuffleDirsV8(GoobyRng rng)
        {
            var a = new[] { 0, 1, 2, 3 };
            var run = CountAndMakeRun(a, rng);
            if (run < a.Length) BinaryInsertionSort(a, 0, run, a.Length, rng);
            return a;
        }

        private static int CountAndMakeRun(int[] a, GoobyRng rng)
        {
            var high = a.Length;
            if (high <= 1) return high;
            var run = 2;
            var descending = (rng.Next() - 0.5) < 0.0;
            for (var i = 2; i < high; i++)
            {
                var order = rng.Next() - 0.5;
                if (descending) { if (order >= 0.0) break; }
                else if (order < 0.0) break;
                run++;
            }
            if (descending) Array.Reverse(a, 0, run);
            return run;
        }

        private static void BinaryInsertionSort(int[] a, int low, int startArg, int high, GoobyRng rng)
        {
            var start = low == startArg ? startArg + 1 : startArg;
            while (start < high)
            {
                var left = low;
                var right = start;
                var pivot = a[start];
                while (left < right)
                {
                    var mid = left + ((right - left) >> 1);
                    if ((rng.Next() - 0.5) < 0.0) right = mid;
                    else left = mid + 1;
                }
                for (var i = start; i > left; i--) a[i] = a[i - 1];
                a[left] = pivot;
                start++;
            }
        }

        private static List<int> RandomPath(GoobyRng rng, int size, int srcCol, int goalCol)
        {
            var visited = new HashSet<int>();
            var path = new List<int>();
            PathDfs(rng, size, (size - 1) * size + goalCol, visited, path, srcCol);
            return path;
        }

        private static bool PathDfs(GoobyRng rng, int size, int goal, HashSet<int> visited, List<int> path, int idx)
        {
            visited.Add(idx);
            path.Add(idx);
            if (idx == goal) return true;
            var col = idx % size;
            var row = (idx - col) / size;
            foreach (var dir in ShuffleDirsV8(rng))
            {
                var nc = col + Delta[dir].x;
                var nr = row + Delta[dir].y;
                if (nc < 0 || nc >= size || nr < 0 || nr >= size) continue;
                var n = nr * size + nc;
                if (visited.Contains(n)) continue;
                if (PathDfs(rng, size, goal, visited, path, n)) return true;
            }
            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static int ShapeForOpenings(GoobyRng rng, int inDir, int outDir, Tune tune)
        {
            var straightFits = Opposite(inDir) == outDir;
            if (rng.Next() < tune.TeeChance) return 2;
            return straightFits ? 0 : 1;
        }

        private static int SolvedRotFor(int shape, int[] dirs, GoobyRng rng)
        {
            var fits = new List<int>();
            for (var rot = 0; rot < 4; rot++)
            {
                var mask = MaskOf(shape, rot);
                var all = true;
                foreach (var d in dirs) if ((mask & (1 << d)) == 0) { all = false; break; }
                if (all) fits.Add(rot);
            }
            return fits[(int)Math.Floor(rng.Next() * fits.Count)];
        }

        private static int DecoyShape(GoobyRng rng)
        {
            var r = rng.Next();
            if (r < DecoyStraight) return 0;
            if (r < DecoyStraight + DecoyBend) return 1;
            return 2;
        }

        // ── solver ───────────────────────────────────────────────────────────

        private static byte[] BoardTapCosts(Tile[] tiles)
        {
            EnsureTapsTable();
            var outp = new byte[tiles.Length * 16];
            for (var i = 0; i < tiles.Length; i++)
            {
                var offset = tiles[i].Shape * 64 + (tiles[i].Rot & 3) * 16;
                for (var need = 0; need < 16; need++) outp[i * 16 + need] = _minTaps[offset + need];
            }
            return outp;
        }

        private static List<(int idx, int taps)> SearchBestPath(Board board)
        {
            var s = new PathSearch
            {
                Size = board.Size,
                Goal = (board.Size - 1) * board.Size + board.GoalCol,
                Cost = BoardTapCosts(board.Tiles),
            };
            s.H = RelaxedToGoal(board, s.Cost);
            s.Visited = new byte[board.Size * board.Size];
            s.Visited[board.SrcCol] = 1;
            s.Dfs(board.SrcCol, DirN, 0);
            return s.BestLegs;
        }

        private static int[] RelaxedToGoal(Board board, byte[] cost)
        {
            var size = board.Size;
            var stateCount = size * size * 4;
            var goalIdx = (size - 1) * size + board.GoalCol;
            var h = new int[stateCount];
            for (var i = 0; i < stateCount; i++) h[i] = Unreachable;
            var settled = new byte[stateCount];
            for (var entry = 0; entry < 4; entry++)
            {
                if (entry == DirS) continue;
                int k = cost[goalIdx * 16 + ((1 << entry) | (1 << DirS))];
                if (k != NoTaps) h[goalIdx * 4 + entry] = k;
            }
            while (true)
            {
                var u = -1;
                var uCost = Unreachable;
                for (var st = 0; st < stateCount; st++)
                    if (settled[st] == 0 && h[st] < uCost) { u = st; uCost = h[st]; }
                if (u == -1) break;
                settled[u] = 1;
                var entryDir = u % 4;
                var idx = (u - entryDir) / 4;
                var col = idx % size;
                var row = (idx - col) / size;
                var exitDir = Opposite(entryDir);
                var pc = col - Delta[exitDir].x;
                var pr = row - Delta[exitDir].y;
                if (pc < 0 || pc >= size || pr < 0 || pr >= size) continue;
                var pIdx = pr * size + pc;
                for (var pEntry = 0; pEntry < 4; pEntry++)
                {
                    if (pEntry == exitDir) continue;
                    int k = cost[pIdx * 16 + ((1 << pEntry) | (1 << exitDir))];
                    if (k == NoTaps) continue;
                    var v = pIdx * 4 + pEntry;
                    if (uCost + k < h[v]) h[v] = uCost + k;
                }
            }
            return h;
        }

        private sealed class PathSearch
        {
            public int Size, Goal;
            public byte[] Cost;
            public int[] H;
            public byte[] Visited;
            public int Best = Unreachable;
            public List<(int, int)> BestLegs = new List<(int, int)>();
            private readonly List<(int, int)> _legs = new List<(int, int)>();
            private int _nodes;

            public void Dfs(int idx, int entry, int g)
            {
                _nodes++;
                if (_nodes > SolverNodeBudget) return;
                if (g + H[idx * 4 + entry] >= Best) return;
                var col = idx % Size;
                var row = (idx - col) / Size;
                var baseIdx = idx * 16;
                var entryBit = 1 << entry;
                for (var exitDir = 0; exitDir < 4; exitDir++)
                {
                    if (exitDir == entry) continue;
                    int k = Cost[baseIdx + (entryBit | (1 << exitDir))];
                    if (k == NoTaps) continue;
                    if (idx == Goal && exitDir == DirS)
                    {
                        if (g + k < Best)
                        {
                            Best = g + k;
                            BestLegs = new List<(int, int)>(_legs) { (idx, k) };
                        }
                        continue;
                    }
                    var nc = col + Delta[exitDir].x;
                    var nr = row + Delta[exitDir].y;
                    if (nc < 0 || nc >= Size || nr < 0 || nr >= Size) continue;
                    var n = nr * Size + nc;
                    if (Visited[n] == 1) continue;
                    Visited[n] = 1;
                    _legs.Add((idx, k));
                    Dfs(n, Opposite(exitDir), g + k);
                    _legs.RemoveAt(_legs.Count - 1);
                    Visited[n] = 0;
                }
            }
        }
    }
}
