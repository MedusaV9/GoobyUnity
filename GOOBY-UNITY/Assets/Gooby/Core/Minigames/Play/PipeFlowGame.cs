using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Rohr-Wirrwarr round — port of pipe_flow.gd's _process/_unhandled_input/_solve/_fill_tick/_apply_leak
    /// over the certified PipeFlow rules, engine-free. Setup deals puzzle 1 (seed·1009+n) before the 1.5 s intro, so
    /// the board matches Godot. A tap rotates a tile 90°; a path from the tap (north of row 0) to the sprinkler
    /// (south of the last row) starts the fill wave (BFS depths × FillStepSec) then the next puzzle. From puzzle 3
    /// a joint leaks after LeakSec (−5). 90 s; endless ends on three leaks. Score = 25·solved + tap bonus − 5·leaks.
    /// </summary>
    public sealed class PipeFlowGame
    {
        public const double IntroSec = 1.5;

        public PipeFlow.Tune Tune { get; }
        public int RunSeed { get; }
        public PipeFlow.Board Board { get; private set; }
        public int PuzzleNo { get; private set; }
        public int Solved { get; private set; }
        public int Failures { get; private set; }
        public int TotalTaps { get; private set; }
        public int OptimalTaps { get; private set; }
        public int SolveStreak { get; private set; }
        public double Elapsed { get; private set; }
        public double PuzzleElapsed { get; private set; }
        public int LeakIndex { get; private set; } = -1;
        public bool LeakApplied { get; private set; }
        public bool Filling { get; private set; }
        public int FillDepth { get; private set; }
        public double FillLeft { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public Dictionary<int, int> Reach { get; private set; } = new Dictionary<int, int>();
        public Dictionary<int, int> Depths { get; private set; } = new Dictionary<int, int>();
        public readonly double[] Spin;

        public event Action<int> Tapped;          // tile
        public event Action Solved_;
        public event Action Leaked;
        public event Action PuzzleStarted;
        public event Action Finished_;

        public PipeFlowGame(string mode, int seed)
        {
            Tune = PipeFlow.ApplyDifficulty(PipeFlow.Pipe, mode);
            RunSeed = seed;
            Spin = new double[Tune.Grid * Tune.Grid];
            NextPuzzle();
        }

        public int Size => Board.Size;
        public int Score => PipeFlow.PipeScore(Solved, TotalTaps, OptimalTaps, Tune, Failures);
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public bool LeakPending => LeakIndex >= 0 && !LeakApplied && !Filling;

        public bool TileWatered(int i) => Filling && Depths.ContainsKey(i) && Depths[i] < FillDepth;
        public bool TileReached(int i) => Reach.ContainsKey(i);

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            for (var i = 0; i < Spin.Length; i++) Spin[i] = Math.Max(0.0, Spin[i] - dt);
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            PuzzleElapsed += dt;
            if (Filling) FillTick(dt);
            else if (LeakIndex >= 0 && PipeFlow.LeakPenaltyDue(PuzzleElapsed, LeakApplied, Tune)) ApplyLeak();
            if (!Tune.Endless && Elapsed >= Tune.DurationSec) { Finish(); return; }
            if (PipeFlow.EndlessShouldEnd(Failures, Tune)) Finish();
        }

        /// <summary>Rotate tile <paramref name="index"/> 90° clockwise. False when gated / out of range.</summary>
        public bool Tap(int index)
        {
            if (Finished || IntroLeft > 0.0 || Filling) return false;
            if (index < 0 || index >= Board.Tiles.Length) return false;
            Board.Tiles[index] = PipeFlow.RotateTile(Board.Tiles[index]);
            TotalTaps += 1;
            Spin[index] = Tune.RotateSec;
            Tapped?.Invoke(index);
            var (solved, depths) = PipeFlow.WaterReach(Board);
            Reach = depths;
            if (solved) Solve(depths);
            return true;
        }

        /// <summary>Apply the certified solver taps in order (tests / the auto-plumber).</summary>
        public int ApplySolution()
        {
            var taps = PipeFlow.SolveBoard(Board).taps;
            var n = 0;
            foreach (var idx in taps)
            {
                if (Filling || Finished) break;
                if (Tap(idx)) n++;
            }
            return n;
        }

        private void Solve(Dictionary<int, int> depths)
        {
            Solved += 1;
            SolveStreak += 1;
            Depths = depths;
            Filling = true;
            FillDepth = 0;
            FillLeft = 0.0;
            Solved_?.Invoke();
        }

        private void FillTick(double dt)
        {
            FillLeft -= dt;
            if (FillLeft > 0.0) return;
            var max = 0;
            foreach (var d in Depths.Values) if (d > max) max = d;
            if (FillDepth <= max)
            {
                FillDepth += 1;
                FillLeft = Tune.FillStepSec;
                return;
            }
            Filling = false;
            FillLeft = 0.0;
            NextPuzzle();
        }

        private void ApplyLeak()
        {
            LeakApplied = true;
            Failures += 1;
            SolveStreak = 0;
            Leaked?.Invoke();
        }

        private void NextPuzzle()
        {
            PuzzleNo += 1;
            Board = PipeFlow.GenerateBoard(RunSeed * 1009 + PuzzleNo, Tune);
            OptimalTaps += Board.OptimalTaps;
            LeakIndex = PipeFlow.LeakJointFor(Board, PuzzleNo, Tune);
            LeakApplied = false;
            PuzzleElapsed = 0.0;
            Filling = false;
            FillDepth = 0;
            Depths = new Dictionary<int, int>();
            Reach = PipeFlow.WaterReach(Board).depths;
            if (PuzzleNo > 1) PuzzleStarted?.Invoke();
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Finished_?.Invoke();
        }
    }
}
