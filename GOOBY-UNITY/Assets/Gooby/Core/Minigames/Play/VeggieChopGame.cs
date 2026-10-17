using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Gemüse-Schnippler round — port of veggie_chop.gd's _process/_frenzy_tick/_spawn_tick/_throw/_fly_tick/
    /// _cut_segment/_resolve_hit over the certified VeggieChop rules, engine-free. Veggies fly up in arcs (one to
    /// three at once, ramping), a swipe segment that crosses one cuts it (+2, +1 more for every further piece in the
    /// same swipe), junk (can, boot) costs 3 and stuns for half a second, a veggie that falls back uncut is a miss —
    /// three end the round early. Every 25 s a frenzy throws exactly eight veggies. 60 s; endless until three junk hits.
    /// World: half height 4.142, the ground at −4.142, throws start 0.6 below it.
    /// </summary>
    public sealed class VeggieChopGame
    {
        public const double IntroSec = 1.5;
        public const double HalfH = 4.142135623730951;
        public const double GroundY = -HalfH, SpawnY = -HalfH - 0.6;

        public sealed class Flying
        {
            public VeggieChop.Item Item;
            public VeggieChop.Arc Arc;
            public double T, X, Y, PrevX, PrevY, Spin;
        }

        public VeggieChop.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public double Elapsed { get; private set; }
        public int Misses { get; private set; }
        public int JunkHits { get; private set; }
        public int SwipeCombo { get; private set; }
        public double StunUntil { get; private set; } = -1.0;
        public int Frenzies { get; private set; }
        public double FrenzyUntil { get; private set; } = -1.0;
        public int FrenzyLeft { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Flying> Items = new List<Flying>();

        private double _nextSpawn;
        private double _aspect = 390.0 / 844.0;

        public event Action<Flying, int, int> Chopped;   // the piece, points, swipe combo
        public event Action<Flying> JunkHit;
        public event Action<int> Missed;                 // misses left
        public event Action Frenzy;
        public event Action Finished_;

        public VeggieChopGame(string mode, int seed)
        {
            Tune = VeggieChop.ApplyDifficulty(VeggieChop.Chop, mode);
            Rng = new GoobyRng(seed);
        }

        public void SetAspect(double widthOverHeight) { if (widthOverHeight > 0.05) _aspect = widthOverHeight; }

        /// <summary>Half the visible width in world units (the view is 2·HalfH tall).</summary>
        public double HalfW => Math.Max(0.9, _aspect * HalfH);
        public bool Stunned => Elapsed < StunUntil;
        public bool InFrenzy => FrenzyLeft > 0 && Elapsed <= FrenzyUntil;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public int MissesLeft => Math.Max(0, Tune.MaxMisses - Misses);

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            var step = dt * Tune.SpeedMult;
            Elapsed += step;
            FrenzyTick();
            SpawnTick(step);
            FlyTick(step);
            if (RoundOver()) Finish();
        }

        private bool RoundOver()
        {
            if (VeggieChop.EndlessShouldEnd(JunkHits, Tune)) return true;
            if (Misses >= Tune.MaxMisses) return true;
            return !Tune.Endless && Elapsed >= Tune.DurationSec;
        }

        private void FrenzyTick()
        {
            var due = VeggieChop.FrenzyCountAt(Elapsed);
            if (due <= Frenzies) return;
            Frenzies = due;
            FrenzyUntil = Elapsed + Tune.FrenzyDurationSec;
            FrenzyLeft = Tune.FrenzyItems;
            _nextSpawn = 0.0;
            Frenzy?.Invoke();
        }

        private void SpawnTick(double step)
        {
            _nextSpawn -= step;
            if (_nextSpawn > 0.0) return;
            if (FrenzyLeft > 0 && Elapsed <= FrenzyUntil)
            {
                _nextSpawn = VeggieChop.FrenzySpawnInterval();
                FrenzyLeft--;
                Throw(VeggieChop.RollVeggie(Rng));
                return;
            }
            _nextSpawn = VeggieChop.SpawnIntervalAt(Elapsed, Tune.DurationSec, Tune);
            var size = VeggieChop.WaveSizeAt(Rng, Elapsed);
            for (var i = 0; i < size; i++) Throw(VeggieChop.RollItem(Rng, Elapsed, Tune));
        }

        private void Throw(VeggieChop.Item item)
        {
            var arc = VeggieChop.MakeArc(Rng, HalfW, SpawnY, Tune.Gravity);
            var (x, y) = VeggieChop.ArcPos(arc, 0.0, Tune.Gravity);
            Items.Add(new Flying { Item = item, Arc = arc, T = 0.0, X = x, Y = y, PrevX = x, PrevY = y, Spin = Rng.Next() * Math.PI * 2.0 });
        }

        private void FlyTick(double step)
        {
            var g = Tune.Gravity;
            var floorY = SpawnY - 0.3;
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                var e = Items[i];
                e.PrevX = e.X; e.PrevY = e.Y;
                e.T += step;
                var (x, y) = VeggieChop.ArcPos(e.Arc, e.T, g);
                e.X = x; e.Y = y;
                if (e.T <= e.Arc.Vy / g || y >= floorY) continue;
                Items.RemoveAt(i);
                if (e.Item.Kind == "veggie") RegisterMiss();
            }
        }

        private void RegisterMiss()
        {
            Misses++;
            SwipeCombo = 0;
            Missed?.Invoke(MissesLeft);
        }

        /// <summary>Finger down: a new swipe, its combo starts from nothing.</summary>
        public void BeginSwipe()
        {
            if (Finished) return;
            SwipeCombo = 0;
        }

        /// <summary>A stretch of the swipe in world coordinates: every piece it crosses is cut (or smashed).</summary>
        public int Cut(double ax, double ay, double bx, double by)
        {
            if (Finished || IntroLeft > 0.0 || Stunned) return 0;
            if ((bx - ax) * (bx - ax) + (by - ay) * (by - ay) < 1e-6) return 0;
            var hits = 0;
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                var e = Items[i];
                if (!SegmentHitsMovingCircle(ax, ay, bx, by, e.PrevX, e.PrevY, e.X, e.Y, Tune.HitRadius)) continue;
                Items.RemoveAt(i);
                ResolveHit(e);
                hits++;
                if (Finished) break;
            }
            return hits;
        }

        /// <summary>
        /// QA autoplay (<c>mgbot</c>): slices one veggie at a time once it has risen into the upper part of the view, with a
        /// short stroke that keeps clear of junk — it never smashes a can or a boot.
        /// </summary>
        public void ApplyBot()
        {
            if (Finished || IntroLeft > 0.0 || Stunned) return;
            foreach (var e in Items)
            {
                if (e.Item.Kind == "junk" || e.Y < -HalfH * 0.35 || e.T < 0.35) continue;
                var clear = true;
                foreach (var j in Items)
                    if (j.Item.Kind == "junk" && Math.Abs(j.X - e.X) < 1.4 && Math.Abs(j.Y - e.Y) < 1.4) { clear = false; break; }
                if (!clear) continue;
                BeginSwipe();
                Cut(e.X - 0.45, e.Y + 0.08, e.X + 0.45, e.Y - 0.08);
                return;
            }
        }

        /// <summary>A stretch of the swipe in view space (0..1 across and down the play field).</summary>
        public int CutNormalized(double nx0, double ny0, double nx1, double ny1)
        {
            var (ax, ay) = ToWorld(nx0, ny0);
            var (bx, by) = ToWorld(nx1, ny1);
            return Cut(ax, ay, bx, by);
        }

        /// <summary>View space (0..1) → world: x across the half width, y from the ground at the bottom up.</summary>
        public (double x, double y) ToWorld(double nx, double ny) => ((nx - 0.5) * 2.0 * HalfW, GroundY + (1.0 - ny) * 2.0 * HalfH);

        /// <summary>World → view space (0..1).</summary>
        public (double nx, double ny) ToView(double x, double y) => (0.5 + x / (2.0 * HalfW), 1.0 - (y - GroundY) / (2.0 * HalfH));

        private void ResolveHit(Flying e)
        {
            var kind = e.Item.Kind;
            SwipeCombo = VeggieChop.ComboAfterHit(SwipeCombo, kind);
            if (kind == "junk")
            {
                JunkHits++;
                StunUntil = Elapsed + Tune.StunSec;
                Score = VeggieChop.ApplyPoints(Score, Tune.JunkPts);
                JunkHit?.Invoke(e);
                if (RoundOver()) Finish();
                return;
            }
            var points = VeggieChop.ChopPoints(SwipeCombo);
            Score = VeggieChop.ApplyPoints(Score, points);
            Chopped?.Invoke(e, points, SwipeCombo);
        }

        /// <summary>veggie_chop_logic.gd segment_hits_moving_circle: the swipe against the piece's path over the step.</summary>
        public static bool SegmentHitsMovingCircle(double ax, double ay, double bx, double by, double c0x, double c0y, double c1x, double c1y, double r)
        {
            if (SegmentsIntersect(ax, ay, bx, by, c0x, c0y, c1x, c1y)) return true;
            return VeggieChop.SegmentHitsCircle(ax, ay, bx, by, c0x, c0y, r) || VeggieChop.SegmentHitsCircle(ax, ay, bx, by, c1x, c1y, r)
                || VeggieChop.SegmentHitsCircle(c0x, c0y, c1x, c1y, ax, ay, r) || VeggieChop.SegmentHitsCircle(c0x, c0y, c1x, c1y, bx, by, r);
        }

        private static double Cross(double ax, double ay, double bx, double by, double px, double py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            var abC = Cross(ax, ay, bx, by, cx, cy); var abD = Cross(ax, ay, bx, by, dx, dy);
            var cdA = Cross(cx, cy, dx, dy, ax, ay); var cdB = Cross(cx, cy, dx, dy, bx, by);
            const double eps = 1e-9;
            var straddlesAb = (abC > eps && abD < -eps) || (abC < -eps && abD > eps);
            var straddlesCd = (cdA > eps && cdB < -eps) || (cdA < -eps && cdB > eps);
            if (straddlesAb && straddlesCd) return true;
            return OnSegment(abC, cx, cy, ax, ay, bx, by) || OnSegment(abD, dx, dy, ax, ay, bx, by) || OnSegment(cdA, ax, ay, cx, cy, dx, dy) || OnSegment(cdB, bx, by, cx, cy, dx, dy);
        }

        private static bool OnSegment(double cross, double px, double py, double ax, double ay, double bx, double by) =>
            Math.Abs(cross) <= 1e-9 && px >= Math.Min(ax, bx) - 1e-9 && px <= Math.Max(ax, bx) + 1e-9 && py >= Math.Min(ay, by) - 1e-9 && py <= Math.Max(ay, by) + 1e-9;

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = VeggieChop.FinalScore(Score, Tune);
            Finished_?.Invoke();
        }
    }
}
