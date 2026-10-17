using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Möhrenwache round — port of carrot_guard.gd's _spawn_tick/_spawn_king/_mole_tick/_unhandled_input/
    /// _bonk/_tap_king over the certified CarrotGuard rules, engine-free. Nine mounds; moles pop up (one, later often
    /// two at once), stay for a shrinking window and steal a carrot when they get away; a tap on a mole bonks it (+1,
    /// every fifth in a row +3), a tap on an empty mound breaks the combo. Every 20 bonks the mole king rises: three
    /// taps for +8 and two coins. 45 s or until the bed is empty; endless until three carrots are stolen.
    /// </summary>
    public sealed class CarrotGuardGame
    {
        public const double IntroSec = 1.5;

        public sealed class Mole
        {
            public int Hole;
            public double Left;   // seconds until it dives
            public double Up;     // 0 → 1 pop-up
        }

        public sealed class King
        {
            public int Hole, Hp;
            public double Up;
        }

        public CarrotGuard.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int Bonks { get; private set; }
        public int KingsSpawned { get; private set; }
        public int Carrots { get; private set; }
        public double Elapsed { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Mole> Moles = new List<Mole>();
        public King TheKing { get; private set; }

        private double _nextSpawn;
        private double _lastTapAt = double.NegativeInfinity;

        public event Action<int, int, int> Bonked;      // hole, delta, combo
        public event Action<int> Whiffed;               // hole
        public event Action<int, int> Stolen;           // hole, carrots left
        public event Action<int> KingRises;             // hole
        public event Action<int, int> KingHit;          // hole, hp left
        public event Action<int, int> KingDown;         // hole, delta
        public event Action Finished_;

        public CarrotGuardGame(string mode, int seed)
        {
            Tune = CarrotGuard.ApplyDifficulty(CarrotGuard.Guard, mode);
            Rng = new GoobyRng(seed);
            Carrots = Tune.Carrots;
        }

        public int Holes => Tune.Grid * Tune.Grid;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public int StolenCount => Tune.Carrots - Carrots;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            SpawnTick(dt);
            MoleTick(dt);
            if (CarrotGuard.IsRoundOver(Elapsed, Carrots, Tune.DurationSec, Tune)) Finish();
        }

        private void SpawnTick(double dt)
        {
            if (TheKing != null) return;
            _nextSpawn -= dt;
            if (_nextSpawn > 0.0) return;
            _nextSpawn = CarrotGuard.SpawnIntervalAt(Elapsed, Tune.DurationSec, Tune);
            if (CarrotGuard.IsKingDue(Bonks, KingsSpawned)) { SpawnKing(); return; }
            var count = Rng.Next() < CarrotGuard.DoubleChanceAt(Elapsed, Tune.DurationSec, Tune) ? 2 : 1;
            for (var i = 0; i < count; i++)
            {
                var free = FreeHoles();
                if (free.Count == 0) return;
                var hole = free[Math.Min(free.Count - 1, (int)(Rng.Next() * free.Count))];
                Moles.Add(new Mole { Hole = hole, Left = CarrotGuard.UpTimeAt(Elapsed, Tune.DurationSec, Tune), Up = 0.0 });
            }
        }

        private void SpawnKing()
        {
            KingsSpawned++;
            var free = FreeHoles();
            if (free.Count == 0) return;
            TheKing = new King { Hole = free[Math.Min(free.Count - 1, (int)(Rng.Next() * free.Count))], Hp = Tune.KingTaps, Up = 0.0 };
            KingRises?.Invoke(TheKing.Hole);
        }

        private void MoleTick(double dt)
        {
            if (TheKing != null)
            {
                TheKing.Up = Math.Min(1.0, TheKing.Up + dt / Tune.PopSec);
                return;
            }
            for (var i = Moles.Count - 1; i >= 0; i--)
            {
                var m = Moles[i];
                m.Up = Math.Min(1.0, m.Up + dt / Tune.PopSec);
                m.Left -= dt;
                if (m.Left > 0.0) continue;
                // got away: a carrot is gone and the combo with it
                Moles.RemoveAt(i);
                var (carrots, combo) = CarrotGuard.ApplyEscape(Carrots, Combo);
                Carrots = carrots; Combo = combo;
                Stolen?.Invoke(m.Hole, Carrots);
            }
        }

        public List<int> FreeHoles()
        {
            var used = new HashSet<int>();
            foreach (var m in Moles) used.Add(m.Hole);
            if (TheKing != null) used.Add(TheKing.Hole);
            var outp = new List<int>();
            for (var i = 0; i < Holes; i++) if (!used.Contains(i)) outp.Add(i);
            return outp;
        }

        /// <summary>A tap on a mound. Returns "bonk", "king-hit", "king-down", "whiff" or null (debounced / not playing).</summary>
        public string Tap(int hole)
        {
            if (Finished || IntroLeft > 0.0 || hole < 0 || hole >= Holes) return null;
            if (!CarrotGuard.AcceptsTapAfter(Elapsed - _lastTapAt, Tune.TapDebounceSec)) return null;
            _lastTapAt = Elapsed;
            if (TheKing != null && TheKing.Hole == hole) return TapKing();
            for (var i = 0; i < Moles.Count; i++)
            {
                if (Moles[i].Hole != hole) continue;
                Moles.RemoveAt(i);
                var (score, combo, bonus) = CarrotGuard.ApplyBonk(Score, Combo);
                var delta = score - Score;
                Score = score; Combo = combo; Bonks++;
                Bonked?.Invoke(hole, delta, Combo);
                return "bonk";
            }
            // a swing at an empty mound: no points lost, the combo is gone
            Combo = 0;
            Whiffed?.Invoke(hole);
            return "whiff";
        }

        private string TapKing()
        {
            var (score, combo, hp, complete, bonus, gained) = CarrotGuard.ApplyKingTap(Score, Combo, TheKing.Hp);
            TheKing.Hp = hp;
            if (!complete) { KingHit?.Invoke(TheKing.Hole, hp); return "king-hit"; }
            var hole = TheKing.Hole;
            Score = score; Combo = combo; Bonks++;
            TheKing = null;
            KingDown?.Invoke(hole, gained);
            return "king-down";
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
