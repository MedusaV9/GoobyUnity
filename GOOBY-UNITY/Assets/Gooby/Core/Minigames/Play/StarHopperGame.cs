using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Sternenhüpfer round — port of star_hopper.gd (_advance/_spawn_row/_step_showers/_step_wormhole/
    /// _collect/_check_hits) and star_hopper_bot.gd's survivable row generator over the certified StarHopper rules,
    /// engine-free. Three lanes; the ship flies on, +5 % speed every 10 s; meteor rows come every 27 → 18 m (never a
    /// row chain that cannot be dodged), stars +3 and golden carrots +10 sit in free lanes, a shield appears at score
    /// 60 and saves one hit, a wormhole pays a point every 0.2 s for two seconds; every 14 s a telegraphed shower
    /// rains fast meteors down two lanes. One hit ends the run. Score = metres / 10 + pickups.
    /// </summary>
    public sealed class StarHopperGame
    {
        public const double IntroSec = 1.5;
        public const double ViewAheadM = 62.0;

        public sealed class Meteor
        {
            public int Lane;
            public double M, Spin, Approach;
        }

        public sealed class Pickup
        {
            public string Kind;   // star | gold | shield
            public int Points, Lane;
            public double M;
        }

        public sealed class Row
        {
            public bool[] Blocked;
            public double Gap;
        }

        public StarHopper.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public double Traveled { get; private set; }
        public double Elapsed { get; private set; }
        public int Lane { get; private set; } = 1;
        public double LaneVisual { get; private set; } = 1.0;
        public int PickupPoints { get; private set; }
        public bool Shielded { get; private set; }
        public bool ShieldSpawned { get; private set; }
        public double Invuln { get; private set; }
        public double WormholeLeft { get; private set; }
        public bool WormholeSpawned { get; private set; }
        public string ShowerState { get; private set; } = "idle";
        public double ShowerLeft { get; private set; }
        public int ShowerSafeLane { get; private set; } = -1;
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public bool Crashed { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Meteor> Meteors = new List<Meteor>();
        public readonly List<Pickup> Pickups = new List<Pickup>();
        private readonly List<Row> _rows = new List<Row>();
        private double _nextRowM = 30.0;
        private double _showerAt;
        private double _showerDrop;
        private readonly List<int> _showerDanger = new List<int>();

        public event Action<Pickup> Collected;
        public event Action ShieldGained;
        public event Action ShieldPopped;
        public event Action WormholeOpened;
        public event Action ShowerWarned;
        public event Action<int> SpeedStep;   // the new step
        public event Action Crashed_;
        public event Action Finished_;

        public StarHopperGame(string mode, int seed)
        {
            Tune = StarHopper.ApplyDifficulty(StarHopper.Hopper, mode);
            Rng = new GoobyRng(seed);
            _showerAt = Tune.ShowerEverySec;   // the first shower after the first 14 s (star_hopper.gd setup)
        }

        public int Score => StarHopper.HopperScore(Traveled, PickupPoints, Tune);
        public double Speed => StarHopper.SpeedAt(Elapsed, Tune);
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        private double Roll() => Rng.Next();

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            var stepBefore = (int)Math.Floor(Elapsed / Tune.SpeedRampEverySec);
            Elapsed += dt;
            Invuln = Math.Max(0.0, Invuln - dt);
            LaneVisual = MoveToward(LaneVisual, Lane, dt / Tune.LaneChangeSec);
            var dm = Speed * dt;
            StepWormhole(dt);
            Advance(dm, dt);
            if (Finished) return;
            if (!Tune.Endless && Elapsed >= Tune.DurationSec) { Finish(); return; }
            var stepAfter = (int)Math.Floor(Elapsed / Tune.SpeedRampEverySec);
            if (stepAfter > stepBefore) SpeedStep?.Invoke(stepAfter);
        }

        /// <summary>A tap on the left/right half of the field: one lane over.</summary>
        public bool Tap(string side)
        {
            if (Finished || IntroLeft > 0.0) return false;
            var next = StarHopper.LaneAfterTap(Lane, side, Tune);
            if (next == Lane) return false;
            Lane = next;
            return true;
        }

        /// <summary>A sideways swipe: two lanes over.</summary>
        public bool Swipe(string dir)
        {
            if (Finished || IntroLeft > 0.0) return false;
            var next = StarHopper.LaneAfterSwipe(Lane, dir, Tune);
            if (next == Lane) return false;
            Lane = next;
            return true;
        }

        private void Advance(double dm, double dt)
        {
            var before = Traveled;
            Traveled += dm;
            StepShowers(dt);
            while (Traveled + ViewAheadM >= _nextRowM) SpawnRow();
            Collect(before, dm);
            if (Invuln <= 0.0) CheckHits(before, dm);
            if (Finished) return;
            Cull(dt);
            if (StarHopper.ShouldSpawnShield(Score, ShieldSpawned, Tune))
            {
                ShieldSpawned = true;
                Pickups.Add(new Pickup { Kind = "shield", Points = 0, Lane = Lane, M = Traveled + ViewAheadM * 0.8 });
            }
        }

        // ── rows (star_hopper_bot.gd) ───────────────────────────────────────

        /// <summary>Lanes the ship can change over gap metres at speed (0..2).</summary>
        public int MaxLaneShift(double gapM, double speed)
        {
            var time = gapM / Math.Max(0.001, speed);
            var perChange = Tune.LaneChangeSec + 0.22;
            return Math.Max(0, Math.Min(Tune.Lanes - 1, (int)Math.Floor(time / perChange)));
        }

        /// <summary>Reachability over consecutive rows: is there ONE path through?</summary>
        public bool IsChainSurvivable(List<Row> rows, double speed)
        {
            var reachable = new bool[Tune.Lanes];
            for (var i = 0; i < Tune.Lanes; i++) reachable[i] = true;
            foreach (var row in rows)
            {
                var shift = MaxLaneShift(row.Gap, speed);
                var next = new bool[Tune.Lanes];
                for (var to = 0; to < Tune.Lanes; to++)
                {
                    if (row.Blocked[to]) continue;
                    for (var from = 0; from < Tune.Lanes; from++)
                        if (reachable[from] && Math.Abs(to - from) <= shift) { next[to] = true; break; }
                }
                reachable = next;
                if (Array.IndexOf(reachable, true) < 0) return false;
            }
            return true;
        }

        /// <summary>The web's `[0,1,2].sort(() => rng() - 0.5)` under V8's TimSort: 2–4 draws, the same orders.</summary>
        private int[] ShuffledLaneOrder()
        {
            var c1 = Roll() - 0.5; var c2 = Roll() - 0.5;
            if (c1 < 0.0 && c2 < 0.0) return new[] { 2, 1, 0 };
            if (c1 >= 0.0 && c2 >= 0.0) return new[] { 0, 1, 2 };
            var head = c1 < 0.0 ? new[] { 1, 0 } : new[] { 0, 1 };
            var c3 = Roll() - 0.5;
            if (c3 >= 0.0) return new[] { head[0], head[1], 2 };
            var c4 = Roll() - 0.5;
            if (c4 < 0.0) return new[] { 2, head[0], head[1] };
            return new[] { head[0], 2, head[1] };
        }

        private Row GenerateRow()
        {
            var d = StarHopper.DifficultyAt(Elapsed, Tune);
            var speed = Speed;
            var gap = StarHopper.RowGapAt(d, Tune) * (0.9 + Roll() * 0.25);
            var window = _rows.GetRange(Math.Max(0, _rows.Count - 4), Math.Min(4, _rows.Count));
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var blocked = new bool[Tune.Lanes];
                var blockCount = Roll() < StarHopper.Ramp(Tune.DoubleBlockStart, Tune.DoubleBlockEnd, d) ? 2 : 1;
                var order = ShuffledLaneOrder();
                for (var b = 0; b < blockCount; b++) blocked[order[b]] = true;
                var row = new Row { Blocked = blocked, Gap = gap };
                var chain = new List<Row>(window) { row };
                if (IsChainSurvivable(chain, speed)) return row;
            }
            // the fallback row: a single meteor on the outside — trivially survivable
            var fallback = new bool[Tune.Lanes];
            fallback[0] = true;
            return new Row { Blocked = fallback, Gap = gap };
        }

        private void SpawnRow()
        {
            var row = GenerateRow();
            _rows.Add(row);
            if (_rows.Count > 6) _rows.RemoveAt(0);
            var at = _nextRowM;
            _nextRowM += row.Gap;
            for (var i = 0; i < Tune.Lanes; i++)
                if (row.Blocked[i]) Meteors.Add(new Meteor { Lane = i, M = at, Spin = Roll() * Math.PI * 2.0, Approach = 0.0 });
            var (kind, points) = StarHopper.RollPickup(Roll, Tune);
            if (kind == null) return;
            var free = new List<int>();
            for (var i = 0; i < Tune.Lanes; i++) if (!row.Blocked[i]) free.Add(i);
            if (free.Count == 0) return;
            var slot = free[Math.Min(free.Count - 1, (int)(Roll() * free.Count))];
            Pickups.Add(new Pickup { Kind = kind, Points = points, Lane = slot, M = at });
        }

        // ── showers, wormhole, pickups, hits ───────────────────────────────

        private void StepShowers(double dt)
        {
            switch (ShowerState)
            {
                case "idle":
                    if (Elapsed >= _showerAt)
                    {
                        var (safe, danger) = StarHopper.PickShowerLanes(Roll, Tune);
                        ShowerSafeLane = safe;
                        _showerDanger.Clear(); _showerDanger.AddRange(danger);
                        ShowerState = "warn";
                        ShowerLeft = Tune.ShowerTelegraphSec;
                        ShowerWarned?.Invoke();
                    }
                    break;
                case "warn":
                    ShowerLeft -= dt;
                    if (ShowerLeft <= 0.0) { ShowerState = "active"; ShowerLeft = Tune.ShowerDurationSec; _showerDrop = 0.0; }
                    break;
                case "active":
                    ShowerLeft -= dt;
                    _showerDrop -= dt;
                    if (_showerDrop <= 0.0)
                    {
                        _showerDrop = Tune.ShowerDropEverySec;
                        foreach (var l in _showerDanger)
                            Meteors.Add(new Meteor { Lane = l, M = Traveled + ViewAheadM, Spin = Roll() * Math.PI * 2.0, Approach = Tune.ShowerMeteorSpeed });
                    }
                    if (ShowerLeft <= 0.0) { ShowerState = "idle"; ShowerSafeLane = -1; _showerAt = Elapsed + Tune.ShowerEverySec; }
                    break;
            }
        }

        private void StepWormhole(double dt)
        {
            if (WormholeLeft > 0.0)
            {
                var before = Tune.WormholeSec - WormholeLeft;
                WormholeLeft = Math.Max(0.0, WormholeLeft - dt);
                var after = Tune.WormholeSec - WormholeLeft;
                var awarded = StarHopper.WormholeAwards(before, after, Tune);
                if (awarded > 0) PickupPoints += awarded * Tune.WormholeTickPoints;
                return;
            }
            if (StarHopper.ShouldSpawnWormhole(Roll, Elapsed, WormholeSpawned, false, Tune))
            {
                WormholeSpawned = true;
                WormholeLeft = Tune.WormholeSec;
                WormholeOpened?.Invoke();
            }
        }

        private void Collect(double before, double dm)
        {
            for (var i = Pickups.Count - 1; i >= 0; i--)
            {
                var p = Pickups[i];
                var hit = p.Lane == Lane && p.M >= before - 3.0 && p.M <= before + dm + 3.0;
                if (!hit) continue;
                Pickups.RemoveAt(i);
                if (p.Kind == "shield") { Shielded = true; ShieldGained?.Invoke(); continue; }
                PickupPoints += p.Points;
                Collected?.Invoke(p);
            }
        }

        private void CheckHits(double before, double dm)
        {
            foreach (var meteor in Meteors)
            {
                if (!StarHopper.SweepHitsMeteor(Lane, before, meteor.Lane, meteor.M, dm, Tune)) continue;
                var (ended, _) = StarHopper.ResolveHit(Shielded);
                if (ended) { Crashed = true; Crashed_?.Invoke(); Finish(); return; }
                Shielded = false;
                Invuln = Tune.ShieldPopInvulnSec;
                ShieldPopped?.Invoke();
                return;
            }
        }

        private void Cull(double dt)
        {
            for (var i = Meteors.Count - 1; i >= 0; i--)
            {
                Meteors[i].M -= Meteors[i].Approach * dt;
                if (Meteors[i].M <= Traveled - 12.0) Meteors.RemoveAt(i);
            }
            for (var i = Pickups.Count - 1; i >= 0; i--) if (Pickups[i].M <= Traveled - 8.0) Pickups.RemoveAt(i);
        }

        private static double MoveToward(double a, double b, double step) => Math.Abs(b - a) <= step ? b : a + Math.Sign(b - a) * step;

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Finished_?.Invoke();
        }
    }
}
