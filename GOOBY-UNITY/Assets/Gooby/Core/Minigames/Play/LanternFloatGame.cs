using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Sternenlaterne round — port of lantern_float.gd's _process/_steer/_fill_rings/_check_pass/_bump
    /// over the certified LanternFloat rules, engine-free. After a 1.5 s intro the lantern rises at 2.6 m/s; a
    /// sideways drag is the steer target (lerp 6.5 /s, then a telegraphed gust shove). Rings +2 (every 5th gold +5),
    /// fireflies +1, cloud bumps −3 with 1.2 s invulnerability (endless: three bumps end). Setup burns 60×3 rng
    /// draws so the ring stream matches the 2D Godot seed, then fills to SPAWN_LEAD = 11 m.
    /// </summary>
    public sealed class LanternFloatGame
    {
        public const double IntroSec = 1.5;
        public const double HalfH = 5.4;
        public const double SpawnLead = 11.0;
        public const double SteerLerp = 6.5;
        public const double FireflyLead = 1.6;
        public const double CloudLead = 0.8;
        public const double CullBehind = 3.0;

        public sealed class Ring
        {
            public int Index;
            public double X, Y;
            public bool Gold, Done;
            public int Points;
            public bool Firefly, FireflyDone;
            public double FireflyX;
            public bool Cloud, CloudDone;
            public double CloudX;
        }

        public LanternFloat.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Hits { get; private set; }
        public int Golds { get; private set; }
        public int Fireflies { get; private set; }
        public int Bumps { get; private set; }
        public int RingsPassed { get; private set; }
        public int Streak { get; private set; }
        public double Elapsed { get; private set; }
        public double Travel { get; private set; }
        public double LanternX { get; private set; }
        public double SteerTarget { get; private set; }
        public double Invuln { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Ring> Rings = new List<Ring>();

        private int _nextIndex;
        private double _nextY;

        public event Action<int, bool> RingHit;          // points, gold
        public event Action RingMissed;
        public event Action FireflyCaught;
        public event Action Bumped;
        public event Action Finished_;

        public LanternFloatGame(string mode, int seed)
        {
            Tune = LanternFloat.ApplyDifficulty(LanternFloat.Lantern, mode);
            Rng = new GoobyRng(seed);
            // lantern_float.gd setup: 60 star rolls × 3 draws stay in the seed stream so later rings match 2D
            for (var i = 0; i < 60 * 3; i++) Rng.Next();
            _nextY = LanternFloat.RingSpacingAt(0.0, Tune);
            FillRings();
        }

        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public (LanternFloat.Gust gust, string phase) Gust => LanternFloat.GustPhaseAt(Elapsed, Tune);

        /// <summary>Finger X in −1…1 of the field (Godot `_normalized_x`).</summary>
        public void SteerNormalized(double nx)
        {
            if (Finished || IntroLeft > 0.0) return;
            SteerTarget = LanternFloat.SteerTargetFrom(nx, Tune);
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            Invuln = Math.Max(0.0, Invuln - dt);
            if (!Tune.Endless && Elapsed >= Tune.DurationSec) { Finish(); return; }
            Travel += Tune.RiseSpeed * dt;
            Steer(dt);
            FillRings();
            CheckPass();
        }

        private void Steer(double dt)
        {
            var t = Math.Min(1.0, SteerLerp * dt);
            LanternX += (SteerTarget - LanternX) * t;
            var (gust, phase) = LanternFloat.GustPhaseAt(Elapsed, Tune);
            if (phase == "push") LanternX += gust.Dir * Tune.GustForce * dt;
            LanternX = LanternFloat.ClampLanternX(LanternX, Tune);
        }

        private void FillRings()
        {
            while (_nextY < Travel + SpawnLead)
            {
                var rolled = LanternFloat.RollRing(Rng, _nextIndex, Tune);
                var firefly = Rng.Next() < Tune.FireflyChance;
                var fireflyX = (Rng.Next() * 2.0 - 1.0) * (Tune.HalfW - Tune.RingMargin);
                var (cloud, cloudX) = LanternFloat.RollCloud(Rng, _nextIndex, Tune);
                Rings.Add(new Ring
                {
                    Index = rolled.Index, X = rolled.X, Y = _nextY, Gold = rolled.Gold, Points = rolled.Points,
                    Firefly = firefly, FireflyX = fireflyX, Cloud = cloud, CloudX = cloudX,
                });
                _nextIndex += 1;
                _nextY += LanternFloat.RingSpacingAt(Elapsed, Tune);
            }
        }

        private void CheckPass()
        {
            for (var i = Rings.Count - 1; i >= 0; i--)
            {
                var ring = Rings[i];
                if (!ring.FireflyDone && ring.Firefly && Travel >= ring.Y - FireflyLead)
                {
                    ring.FireflyDone = true;
                    if (Math.Abs(LanternX - ring.FireflyX) <= Tune.FireflyRadius)
                    {
                        Award(Tune.FireflyPts);
                        Fireflies += 1;
                        FireflyCaught?.Invoke();
                    }
                }
                if (!ring.CloudDone && ring.Cloud && Travel >= ring.Y - CloudLead)
                {
                    ring.CloudDone = true;
                    if (LanternFloat.CloudHit(LanternX, ring.CloudX, Tune) && Invuln <= 0.0) Bump();
                }
                if (!ring.Done && Travel >= ring.Y)
                {
                    ring.Done = true;
                    RingsPassed += 1;
                    var hit = new LanternFloat.Ring { Index = ring.Index, X = ring.X, Gold = ring.Gold, Points = ring.Points };
                    if (LanternFloat.RingHit(LanternX, hit, Tune))
                    {
                        Hits += 1;
                        if (ring.Gold) Golds += 1;
                        Award(ring.Points);
                        RingHit?.Invoke(ring.Points, ring.Gold);
                    }
                    else
                    {
                        Streak = 0;
                        RingMissed?.Invoke();
                    }
                }
                if (ring.Y <= Travel - CullBehind) Rings.RemoveAt(i);
            }
        }

        private void Award(int points)
        {
            Score = LanternFloat.ApplyScore(Score, points);
            Streak += 1;
        }

        private void Bump()
        {
            Bumps += 1;
            Streak = 0;
            Invuln = Tune.BumpInvulnSec;
            Bumped?.Invoke();
            if (Tune.Endless)
            {
                if (LanternFloat.EndlessShouldEnd(Bumps, Tune)) Finish();
                return;
            }
            Score = LanternFloat.ApplyScore(Score, -Tune.BumpPenalty);
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
