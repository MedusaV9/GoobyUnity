using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Blasen-Platzer round — port of bubble_pop.gd's _spawn_tick/_rise_tick/_bubble_at/_pop/_chain_burst,
    /// engine-free over the certified BubblePop rules. World frame is the web one: half height 4.142 units
    /// (tan(45°/2)·10), half width from the view's aspect; y grows upwards, bubbles enter below the bottom edge and
    /// leave above the top. Deterministic for a fixed dt, seed and tap sequence.
    /// </summary>
    public sealed class BubblePopGame
    {
        public const double WorldHalfH = 4.142135623730951;
        public const double IntroSec = 1.5;
        public const double ViewW = 390.0, ViewH = 844.0;
        public const double BubbleR = 0.42, SpikyR = 0.5;

        public sealed class Bubble
        {
            public string Kind, Food;   // "food" with a Foods entry, or "spiky"
            public double X, Y, Wobble;
        }

        public BubblePop.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Streak { get; private set; }
        public int Matches { get; private set; }
        public int SpikyPops { get; private set; }
        public int Chains { get; private set; }
        public double Elapsed { get; private set; }
        public double StunUntil { get; private set; } = -1.0;
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Bubble> Bubbles = new List<Bubble>();
        private readonly List<string> _targets;
        private double _nextSpawn;
        private double _aspect = ViewW / ViewH;

        // pop chain (BubblePopLogic.create_pop_chain): same food thrice within the window → the neighbours burst too
        private string _chainStyle = "";
        private int _chainCount;
        private double _chainLastAt = double.NegativeInfinity;

        public event Action<Bubble, string, int> Popped;   // bubble, "match" | "wrong" | "spiky", delta
        public event Action<Bubble, int> Chain;             // the bubble that closed the chain, bubbles burst with it
        public event Action<int> StreakMilestone;
        public event Action Finished_;

        public BubblePopGame(string mode, int seed)
        {
            Tune = BubblePop.ApplyDifficulty(BubblePop.Bubble, mode);
            Rng = new GoobyRng(seed);
            var duration = Tune.Endless ? 90.0 : Tune.DurationSec;
            _targets = BubblePop.TargetOrder(Rng, (int)Math.Ceiling(duration / Tune.TargetRotateSec) + 2);
            _nextSpawn = BubblePop.SpawnIntervalAt(0.0, Tune.DurationSec, Tune);
        }

        /// <summary>The view's width/height ratio: sets the visible half width (the web camera shows more sideways on wide screens).</summary>
        public void SetAspect(double widthOverHeight) { if (widthOverHeight > 0.05) _aspect = widthOverHeight; }

        public double HalfW => WorldHalfH * _aspect;
        public double HalfH => WorldHalfH;
        public bool Stunned => Elapsed < StunUntil;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);

        /// <summary>The food to pop right now (rotates every TargetRotateSec; the order is seeded, every food once before a repeat).</summary>
        public string Target
        {
            get
            {
                var idx = BubblePop.TargetIndexAt(Elapsed, Tune);
                return _targets[Math.Min(_targets.Count - 1, idx)];
            }
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
            SpawnTick(dt);
            RiseTick(dt);
            if (BubblePop.EndlessShouldEnd(SpikyPops, Tune) || (!Tune.Endless && Elapsed >= Tune.DurationSec)) Finish();
        }

        private void SpawnTick(double dt)
        {
            _nextSpawn -= dt;
            if (_nextSpawn > 0.0) return;
            _nextSpawn = BubblePop.SpawnIntervalAt(Elapsed, Tune.DurationSec, Tune);
            var rolled = BubblePop.RollBubble(Rng, Target, Tune);
            Bubbles.Add(new Bubble
            {
                Kind = rolled.Kind, Food = rolled.Food,
                X = (Rng.Next() * 2.0 - 1.0) * Math.Max(0.2, HalfW - 0.6),
                Y = -HalfH - 0.6,
                Wobble = Rng.Next() * Math.PI * 2.0,
            });
        }

        private void RiseTick(double dt)
        {
            var speed = BubblePop.RiseSpeedAt(Elapsed, Tune.DurationSec, Tune);
            var top = HalfH + 0.7;
            var kept = new List<Bubble>(Bubbles.Count);
            foreach (var b in Bubbles)
            {
                b.Y += speed * dt;
                if (b.Y <= top) kept.Add(b);
            }
            Bubbles.Clear();
            Bubbles.AddRange(kept);
        }

        /// <summary>The bubble under a world point (nearest within its touch radius), or null.</summary>
        public Bubble BubbleAt(double wx, double wy)
        {
            Bubble best = null;
            var bestD = double.PositiveInfinity;
            foreach (var b in Bubbles)
            {
                var radius = BubblePop.TouchRadiusFor(b.Kind);
                var d = Math.Sqrt((b.X - wx) * (b.X - wx) + (b.Y - wy) * (b.Y - wy));
                if (d <= radius && d < bestD) { best = b; bestD = d; }
            }
            return best;
        }

        /// <summary>A tap at a world point: pops the bubble there (nothing while stunned or in the intro). Returns the result or null.</summary>
        public string Tap(double wx, double wy)
        {
            if (Finished || IntroLeft > 0.0 || Stunned) return null;
            var hit = BubbleAt(wx, wy);
            return hit == null ? null : Pop(hit);
        }

        /// <summary>Tap in view space (0..1 across and down the play field) → world.</summary>
        public string TapNormalized(double nx, double ny) => Tap((nx - 0.5) * 2.0 * HalfW, (0.5 - ny) * 2.0 * HalfH);

        private string Pop(Bubble bubble)
        {
            var target = Target;
            var (result, delta, stunSec, pops) = BubblePop.PopResult(new BubblePop.BubbleKind { Kind = bubble.Kind, Food = bubble.Food }, target);
            var before = Score;
            Score = BubblePop.ApplyScore(Score, delta);
            // removed first, celebrated after — like the web original, so the chain does not count the popped bubble twice
            if (pops) Bubbles.Remove(bubble);
            switch (result)
            {
                case "match":
                    Streak++;
                    Matches++;
                    break;
                case "wrong":
                    Streak = 0;
                    StunUntil = Elapsed + stunSec;
                    break;
                default:
                    Streak = 0;
                    SpikyPops++;
                    break;
            }
            Popped?.Invoke(bubble, result, Score - before);
            if (result == "match")
            {
                if (RecordChain(bubble.Food)) ChainBurst(bubble);
                if (BubblePop.MatchStreakMilestone(Streak)) StreakMilestone?.Invoke(Streak);
            }
            return result;
        }

        /// <summary>BubblePopLogic.record_pop_chain: the same food ChainCount times within ChainWindowSec triggers a chain.</summary>
        private bool RecordChain(string style)
        {
            var continues = _chainStyle == style && Elapsed - _chainLastAt <= Tune.ChainWindowSec;
            _chainStyle = style;
            _chainCount = continues ? _chainCount + 1 : 1;
            _chainLastAt = Elapsed;
            if (_chainCount < Tune.ChainCount) return false;
            _chainCount = 0;
            Chains++;
            return true;
        }

        /// <summary>Kettenreaktion: every bubble of the same food within ChainRadius bursts too, MatchPts each.</summary>
        private void ChainBurst(Bubble at)
        {
            var burst = new List<Bubble>();
            foreach (var b in Bubbles)
            {
                if (b.Kind != "food" || b.Food != at.Food) continue;
                var d = Math.Sqrt((b.X - at.X) * (b.X - at.X) + (b.Y - at.Y) * (b.Y - at.Y));
                if (d <= Tune.ChainRadius) burst.Add(b);
            }
            foreach (var b in burst) Bubbles.Remove(b);
            if (burst.Count > 0) Score = BubblePop.ApplyScore(Score, burst.Count * Tune.MatchPts);
            Chain?.Invoke(at, burst.Count);
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
