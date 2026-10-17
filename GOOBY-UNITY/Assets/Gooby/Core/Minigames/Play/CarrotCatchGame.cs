using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime CarrotCatch round — port of carrot_catch.gd's _process/_spawn_tick/_move_items/_catch/_finish,
    /// engine-free: world units are the Godot ones (WORLD_HALF_W = 3.25, basket 130 px above the bottom of a
    /// 390×844 reference view). Deterministic for a fixed dt and seed (the balance functions are the certified sim).
    /// </summary>
    public sealed class CarrotCatchGame
    {
        public const double WorldHalfW = 3.25;
        public const double IntroSec = 1.5;
        public const double ViewW = 390.0, ViewH = 844.0;
        public const double BasketPxFromBottom = 130.0, CatchBandPx = 46.0, SpawnYPx = -30.0;

        public sealed class FallingItem
        {
            public string Kind, Key;
            public int Value;
            public double X, Y; // X in world units, Y in reference pixels
        }

        public CarrotCatch.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MissedCarrots { get; private set; }
        public double Elapsed { get; private set; }
        public double BasketX { get; private set; }
        public double TargetX { get; set; }
        public double DizzyUntil { get; private set; } = -1.0;
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<FallingItem> Items = new List<FallingItem>();

        public event Action<FallingItem, int> Caught;   // item, delta
        public event Action<FallingItem> Missed;
        public event Action<int> ComboMilestone;
        public event Action Finished_;

        private double _nextSpawn;
        private double _goldenAt = -1.0;
        private bool _goldenSpawned;

        public CarrotCatchGame(string mode, int seed)
        {
            Tune = CarrotCatch.ApplyDifficulty(CarrotCatch.Catch, mode);
            Rng = new GoobyRng(seed);
            if (!Tune.Endless) _goldenAt = CarrotCatch.GoldenSpawnAt(Rng, Tune.DurationSec, Tune);
            _nextSpawn = CarrotCatch.SpawnIntervalAt(0.0, Tune.DurationSec, Tune);
        }

        public bool Dizzy => Elapsed < DizzyUntil;
        public double PxPerUnit => ViewW / (WorldHalfW * 2.0);
        public double VisibleHalfW => WorldHalfW;
        public double BasketY => ViewH - BasketPxFromBottom;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);

        /// <summary>Screen x (0..1 of the play-area width) → world target for the basket.</summary>
        public void SetTargetFromNormalizedX(double nx) => TargetX = (nx - 0.5) * WorldHalfW * 2.0;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            if (Elapsed >= DizzyUntil) BasketX = Lerp(BasketX, TargetX, Math.Min(1.0, dt * 14.0));
            var half = VisibleHalfW;
            BasketX = Math.Min(half - 0.5, Math.Max(-half + 0.5, BasketX));
            SpawnTick(dt);
            MoveItems(dt);
            if (CarrotCatch.IsRoundOver(Elapsed, MissedCarrots, Tune)) Finish();
        }

        private void SpawnTick(double dt)
        {
            if (!_goldenSpawned && _goldenAt >= 0.0 && Elapsed >= _goldenAt)
            {
                _goldenSpawned = true;
                Items.Add(new FallingItem { Kind = "golden", Key = "carrot", Value = Tune.GoldenPoints, X = CarrotCatch.SpawnXForRoll(Rng.Next(), VisibleHalfW), Y = SpawnYPx });
            }
            _nextSpawn -= dt;
            if (_nextSpawn > 0.0) return;
            _nextSpawn = CarrotCatch.SpawnIntervalAt(Elapsed, Tune.DurationSec, Tune);
            var rolled = CarrotCatch.RollItem(Rng, Elapsed, Tune);
            Items.Add(new FallingItem { Kind = rolled.Kind, Key = rolled.Key, Value = rolled.Value, X = CarrotCatch.SpawnXForRoll(Rng.Next(), VisibleHalfW), Y = SpawnYPx });
        }

        private void MoveItems(double dt)
        {
            var basketY = BasketY;
            var kept = new List<FallingItem>(Items.Count);
            foreach (var item in Items)
            {
                var speed = CarrotCatch.ItemFallSpeed(Elapsed, item.Kind, Tune);
                item.Y += speed * PxPerUnit * dt;
                if (item.Y >= basketY && item.Y < basketY + CatchBandPx && CarrotCatch.BasketCatchesX(item.X, BasketX, Tune))
                {
                    Catch(item);
                    continue;
                }
                if (item.Y > ViewH + 30.0)
                {
                    OnMissed(item);
                    continue;
                }
                kept.Add(item);
            }
            Items.Clear();
            Items.AddRange(kept);
        }

        private void OnMissed(FallingItem item)
        {
            if (item.Kind == "junk" || item.Kind == "rotten") return;
            if (Tune.Endless && item.Kind == "good" && item.Key == "carrot") MissedCarrots++;
            Missed?.Invoke(item);
        }

        private void Catch(FallingItem item)
        {
            var before = Score;
            Score = CarrotCatch.ApplyCatch(Score, item.Value);
            var good = item.Kind == "good" || item.Kind == "golden";
            Combo = good ? Combo + 1 : 0;
            if (item.Kind == "junk" || item.Kind == "rotten") DizzyUntil = Elapsed + Tune.DizzySec;
            Caught?.Invoke(item, Score - before);
            if (CarrotCatch.ComboMilestone(Combo)) ComboMilestone?.Invoke(Combo);
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = CarrotCatch.FinalScore(Score, Tune);
            Finished_?.Invoke();
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
