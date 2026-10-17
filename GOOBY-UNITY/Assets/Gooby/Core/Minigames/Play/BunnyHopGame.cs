using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Hasenhüpfer round — port of bunny_hop.gd's _process/_gust_tick/_pillar_tick/_spawn_pillar/
    /// _coin_tick/_crashed/_unhandled_input over the certified BunnyHop rules, engine-free. World frame of the
    /// web: half height 3.9 (+0.3 margin), x = 0 at the left edge, Gooby hovers at 28 % of the view width. Before
    /// the first tap Gooby floats (no scroll, no gates); a tap hops, the world scrolls faster with every gate,
    /// pillars spawn as their slot enters the right margin, coins sit in gap centres, the wind shoves from second 6.
    /// </summary>
    public sealed class BunnyHopGame
    {
        public const double IntroSec = 1.5;
        public const double WorldHalfH = 3.9, ViewMarginH = 0.6;
        public const double GoobyXFrac = 0.28;
        public const double SpawnMargin = 1.6;
        public const double HoverY = 0.4, HoverAmp = 0.12;
        public const double ViewW = 390.0, ViewH = 844.0;

        public sealed class Pillar
        {
            public double X, GapCenterY, GapHeight;
            public bool Passed;
        }

        public sealed class Coin
        {
            public double X, Y;
        }

        public BunnyHop.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Gates { get; private set; }
        public int Score { get; private set; }
        public double Elapsed { get; private set; }
        public double GoobyY { get; private set; }
        public double GoobyVy { get; private set; }
        public double Scroll { get; private set; }
        public bool Started { get; private set; }
        public bool Crashed { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public readonly List<Pillar> Pillars = new List<Pillar>();
        public readonly List<Coin> Coins = new List<Coin>();

        private double _nextPillarX = double.PositiveInfinity;
        private double _lastGapCenter = double.PositiveInfinity;
        private int _lastGustIndex = -1;
        private double _aspect = ViewW / ViewH;

        public event Action<int, bool> GatePassed;     // points, gusting
        public event Action CoinTaken;
        public event Action Narrowed;
        public event Action<int> Gust;                  // direction
        public event Action Crashed_;
        public event Action Finished_;

        public BunnyHopGame(string mode, int seed)
        {
            Tune = BunnyHop.ApplyDifficulty(BunnyHop.Hop, mode);
            Rng = new GoobyRng(seed);
            GoobyY = HoverY;
        }

        public void SetAspect(double widthOverHeight) { if (widthOverHeight > 0.05) _aspect = widthOverHeight; }

        /// <summary>World units per view height: the view shows 2·3.9 + 0.6 units vertically.</summary>
        public double WorldH => WorldHalfH * 2.0 + ViewMarginH;
        public double ViewWidthWorld => WorldH * _aspect;
        public double GoobyX => ViewWidthWorld * GoobyXFrac;
        public string GustPhase => BunnyHop.GustPhaseAt(Elapsed, Tune).phase;
        public int GustDirection => BunnyHop.GustPhaseAt(Elapsed, Tune).direction;
        public int LiveScore => BunnyHop.FinalHopScore(Score, Tune);

        public void Tick(double dt)
        {
            if (Finished || Crashed || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            if (!Started)
            {
                GoobyY = HoverY + Math.Sin(Elapsed * 3.0) * HoverAmp;   // pre-start hover: no scroll, no gates, no crash
                return;
            }
            var speed = BunnyHop.SpeedAtGate(Gates, Tune);
            Scroll += speed * dt;
            var (y, vy) = BunnyHop.StepPhysics(GoobyY, GoobyVy, dt, Tune);
            GoobyY = y; GoobyVy = vy;
            GustTick();
            PillarTick();
            CoinTick();
            if (IsCrashed()) Crash();
        }

        /// <summary>A tap: the first one starts the run, every one hops.</summary>
        public bool Hop()
        {
            if (Finished || Crashed || IntroLeft > 0.0) return false;
            Started = true;
            GoobyVy = Tune.HopVy;
            return true;
        }

        private void GustTick()
        {
            var (phase, index, direction) = BunnyHop.GustPhaseAt(Elapsed, Tune);
            if (phase != "gust" || index == _lastGustIndex) return;
            _lastGustIndex = index;
            GoobyY = BunnyHop.ApplyGustShift(GoobyY, direction, Tune);
            Gust?.Invoke(direction);
        }

        private void PillarTick()
        {
            var gx = GoobyX;
            foreach (var p in Pillars)
            {
                if (p.Passed || p.X - Scroll > gx) continue;
                p.Passed = true;
                Gates++;
                var gusting = GustPhase == "gust";
                var points = BunnyHop.GatePoints(gusting);
                Score += points;
                GatePassed?.Invoke(points, gusting);
                if (BunnyHop.GapNarrowsAtGate(Gates, Tune)) Narrowed?.Invoke();
            }
            Pillars.RemoveAll(p => p.X - Scroll <= -2.0);
            SpawnDuePillars();
        }

        /// <summary>Web parity: the next pillar is born when its slot enters the margin right of the view — never a stockpile.</summary>
        private void SpawnDuePillars()
        {
            var edge = ViewWidthWorld + SpawnMargin;
            if (double.IsInfinity(_nextPillarX)) _nextPillarX = edge;
            while (_nextPillarX - Scroll < edge) SpawnPillar();
        }

        private void SpawnPillar()
        {
            var gap = BunnyHop.GapAtGate(Gates, Tune);
            var center = BunnyHop.RollGapCenter(Rng, gap, _lastGapCenter, Tune);
            _lastGapCenter = center;
            var atX = _nextPillarX;
            _nextPillarX += Tune.PillarSpacingX;
            Pillars.Add(new Pillar { X = atX, GapCenterY = center, GapHeight = gap });
            if (BunnyHop.CoinSpawns(Rng.Next(), Tune)) Coins.Add(new Coin { X = atX, Y = center });
        }

        private void CoinTick()
        {
            var gx = GoobyX;
            for (var i = Coins.Count - 1; i >= 0; i--)
            {
                var c = Coins[i];
                var cx = c.X - Scroll;
                if (cx < -2.0) { Coins.RemoveAt(i); continue; }
                if (Math.Abs(cx - gx) < 0.42 && Math.Abs(c.Y - GoobyY) < 0.5)
                {
                    Coins.RemoveAt(i);
                    Score += 1;
                    CoinTaken?.Invoke();
                }
            }
        }

        private bool IsCrashed()
        {
            var gx = GoobyX;
            foreach (var p in Pillars)
                if (BunnyHop.Collides(gx, GoobyY, p.X - Scroll, p.GapCenterY, p.GapHeight, Tune)) return true;
            return false;
        }

        private void Crash()
        {
            Crashed = true;
            Crashed_?.Invoke();
            Finish();
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = LiveScore;
            Finished_?.Invoke();
        }
    }
}
