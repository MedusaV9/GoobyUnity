using System;
using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Liefer-Hetze round — port of delivery_rush.gd's _process/_step_van/_check_drop/_autopilot over the
    /// certified DeliveryRush rules, engine-free. Setup deals 3 distinct landmark targets and the fragile parcel
    /// before the 1.5 s intro (RNG like Godot). Intro gates the clock, traffic and steering. The van always
    /// accelerates; drag left/right steers. A 4 m drop ring is +50 (+15 if the fragile parcel is clean); a crash
    /// is −5 (−20 extra on the fragile). Time bonus +max(0, 120 − s) after the third drop.
    /// </summary>
    public sealed class DeliveryRushGame
    {
        public const double IntroSec = 1.5;
        public const double VanTopSpeed = 13.0;
        public const double VanAccel = 9.0;
        public const double VanTurnRate = 2.4;
        public const double OffroadFactor = 0.45;
        public const double TrafficSpeed = 7.5;
        public const double TrafficHitM = 3.4;
        public const double CrashCooldownSec = 1.5;
        public const double BotSteerGain = 1.6;
        public const double BotLookaheadM = 7.0;
        public const int TrafficBase = 6;
        public const int RouteTiles = 9;

        public sealed class Car
        {
            public double S, Speed, Lane;
            public bool Cross;
        }

        public DeliveryRush.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public string[,] Grid { get; }
        public List<DeliveryRush.Box> Colliders { get; }
        public List<string> Targets { get; private set; }
        public List<(double x, double z)> DropPoints { get; private set; }
        public readonly List<Car> Traffic = new List<Car>();
        public readonly List<(double x, double z)> Route = new List<(double x, double z)>();
        public readonly List<string> Visited = new List<string>();
        public DeliveryRush.EndlessState Endless { get; }

        public double VanX { get; private set; }
        public double VanZ { get; private set; }
        public double Heading { get; private set; }
        public double Speed { get; private set; }
        public double Steer { get; private set; }
        public double Elapsed { get; private set; }
        public double LegElapsed { get; private set; }
        public int Score { get; private set; }
        public int Drops { get; private set; }
        public int Parcel { get; private set; }
        public int Crashes { get; private set; }
        public int FragileIndex { get; }
        public bool FragileDamaged { get; private set; }
        public int Streak { get; private set; }
        public double CrashCool { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<int> Dropped;          // points this drop
        public event Action<int> Crashed;          // penalty
        public event Action<int> FragileBroken;    // extra
        public event Action<int> TimeBonused;      // bonus
        public event Action Expired;
        public event Action Finished_;

        private double _routeT;

        public DeliveryRushGame(string mode, int seed)
        {
            Tune = DeliveryRush.ApplyDifficulty(DeliveryRush.Delivery, mode);
            Rng = new GoobyRng(seed);
            Grid = DeliveryRush.BuildGrid();
            Colliders = DeliveryRush.LayoutColliders();
            var ids = new List<string>();
            foreach (var lm in DeliveryRush.Landmarks) ids.Add(lm.Id);
            Targets = DeliveryRush.PickDeliveries(Rng, ids);
            FragileIndex = DeliveryRush.PickFragileParcel(Rng);
            RebuildDrops();
            Endless = DeliveryRush.CreateEndlessState(Tune.EndlessExpiredLimit);
            var shop = DeliveryRush.TileToWorld(DeliveryRush.ShopTile.r, DeliveryRush.ShopTile.c);
            VanX = shop.x + 6.5;
            VanZ = shop.z;
            Heading = -Math.PI * 0.5;
            SpawnTraffic();
            RecomputeRoute();
        }

        public (double x, double z) CurrentDrop => Parcel < DropPoints.Count ? DropPoints[Parcel] : (0.0, 0.0);
        public string CurrentTarget => Parcel < Targets.Count ? Targets[Parcel] : "";
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.TimeBonusFromSec - Elapsed);

        public bool SetSteer(double value)
        {
            if (Finished || IntroLeft > 0.0) return false;
            Steer = Math.Max(-1.0, Math.Min(1.0, value));
            return true;
        }

        /// <summary>Certified road-path autopilot — does not touch the seeded RNG.</summary>
        public bool ApplyBot()
        {
            if (Finished || IntroLeft > 0.0) return false;
            RecomputeRoute();
            var to = Lookahead();
            var dx = to.x - VanX;
            var dz = to.z - VanZ;
            if (Math.Sqrt(dx * dx + dz * dz) < 0.5) { Steer = 0.0; return true; }
            var want = Math.Atan2(dx, -dz);
            var diff = WrapPi(want - Heading);
            Steer = Math.Max(-1.0, Math.Min(1.0, diff * BotSteerGain));
            return true;
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            dt = Math.Min(dt, 0.1);
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            LegElapsed += dt;
            CrashCool = Math.Max(0.0, CrashCool - dt);
            _routeT -= dt;
            if (_routeT <= 0.0) { _routeT = 0.25; RecomputeRoute(); }
            var beforeX = VanX;
            var beforeZ = VanZ;
            StepVan(dt);
            StepTraffic(dt);
            CheckDrop(beforeX, beforeZ);
            if (DeliveryRush.ParcelExpired(LegElapsed, Tune)) ExpireParcel();
        }

        private void StepVan(double dt)
        {
            Heading += Steer * VanTurnRate * dt;
            var top = VanTopSpeed * Tune.SpeedMult;
            var tile = DeliveryRush.WorldToTile(VanX, VanZ);
            if (!DeliveryRush.IsRoad(tile.r, tile.c)) top *= OffroadFactor;
            if (Speed < top) Speed = Math.Min(top, Speed + VanAccel * dt);
            else Speed = Math.Max(top, Speed - VanAccel * dt);
            var dirX = Math.Sin(Heading);
            var dirZ = -Math.Cos(Heading);
            var nx = VanX + dirX * Speed * dt;
            var nz = VanZ + dirZ * Speed * dt;
            if (Blocked(nx, nz)) Speed *= 0.3;
            else { VanX = nx; VanZ = nz; }
            var limit = DeliveryRush.TileM * (DeliveryRush.Grid * 0.5);
            VanX = Math.Max(-limit, Math.Min(limit, VanX));
            VanZ = Math.Max(-limit, Math.Min(limit, VanZ));
        }

        private bool Blocked(double x, double z)
        {
            foreach (var b in Colliders)
                if (x > b.MinX && x < b.MaxX && z > b.MinZ && z < b.MaxZ) return true;
            return false;
        }

        private void StepTraffic(double dt)
        {
            foreach (var car in Traffic)
            {
                car.S = Mod(car.S + car.Speed * dt, 480.0);
                var p = TrafficPos(car);
                var dx = p.x - VanX;
                var dz = p.z - VanZ;
                if (CrashCool <= 0.0 && Math.Sqrt(dx * dx + dz * dz) <= TrafficHitM) Crash();
            }
        }

        public static (double x, double z) TrafficPos(Car car)
        {
            var lane = car.Lane;
            if (car.Cross)
            {
                var s = car.S % 240.0;
                if (s < 0.0) s += 240.0;
                var x = (s <= 120.0 ? s : 240.0 - s) - 60.0;
                return (x, lane);
            }
            var s2 = car.S;
            if (s2 < 120.0) return (-60.0 + s2, -60.0 + lane);
            if (s2 < 240.0) return (60.0 - lane, -60.0 + (s2 - 120.0));
            if (s2 < 360.0) return (60.0 - (s2 - 240.0), 60.0 - lane);
            return (-60.0 + lane, 60.0 - (s2 - 360.0));
        }

        private void Crash()
        {
            CrashCool = CrashCooldownSec;
            Crashes += 1;
            Score = DeliveryRush.ApplyCrash(Score);
            var extra = DeliveryRush.FragileCrashPenalty(FragileIndex, Parcel, FragileDamaged);
            if (extra > 0)
            {
                FragileDamaged = true;
                Score = Math.Max(0, Score - extra);
                FragileBroken?.Invoke(extra);
            }
            else Crashed?.Invoke(Tune.CrashPenalty);
            Speed *= 0.2;
            Streak = 0;
        }

        private void CheckDrop(double beforeX, double beforeZ)
        {
            if (Parcel >= Targets.Count) return;
            var drop = CurrentDrop;
            if (!DeliveryRush.SegmentHitsDrop(beforeX, beforeZ, VanX, VanZ, drop.x, drop.z, Tune.DropRadiusM)) return;
            var prev = Score;
            Score = DeliveryRush.ApplyDrop(Score);
            Score += DeliveryRush.FragileDeliveryBonus(FragileIndex, Parcel, FragileDamaged);
            Visited.Add(Targets[Parcel]);
            Drops += 1;
            Parcel += 1;
            Streak += 1;
            LegElapsed = 0.0;
            Dropped?.Invoke(Score - prev);
            if (Drops >= Tune.Parcels)
            {
                if (Tune.Endless)
                {
                    var ids = new List<string>();
                    foreach (var lm in DeliveryRush.Landmarks) ids.Add(lm.Id);
                    Targets = DeliveryRush.PickDeliveries(Rng, ids);
                    RebuildDrops();
                    Parcel = 0;
                    Drops = 0;
                    FragileDamaged = false;
                    return;
                }
                FinishWithBonus();
            }
        }

        private void ExpireParcel()
        {
            LegElapsed = 0.0;
            Streak = 0;
            Expired?.Invoke();
            if (DeliveryRush.RecordExpiry(Endless)) { Finish(); return; }
            Parcel = Math.Min(Parcel + 1, Targets.Count - 1);
        }

        private void FinishWithBonus()
        {
            var bonus = DeliveryRush.TimeBonus(Elapsed, Tune);
            Score += bonus;
            TimeBonused?.Invoke(bonus);
            Finish();
        }

        private void RebuildDrops()
        {
            DropPoints = new List<(double x, double z)>();
            foreach (var id in Targets)
            {
                var lm = LandmarkOf(id);
                DropPoints.Add(DeliveryRush.DropPoint(lm.X, lm.Z, Colliders));
            }
        }

        private static DeliveryRush.Landmark LandmarkOf(string id)
        {
            foreach (var lm in DeliveryRush.Landmarks) if (lm.Id == id) return lm;
            return default;
        }

        private void SpawnTraffic()
        {
            var count = FrameworkLogic.JsRound(TrafficBase * Tune.TrafficDensityMult);
            for (var i = 0; i < count; i++)
            {
                Traffic.Add(new Car
                {
                    S = Rng.Next() * 480.0,
                    Speed = TrafficSpeed * (0.8 + Rng.Next() * 0.5),
                    Cross = i % 3 == 2,
                    Lane = Rng.Next() < 0.5 ? 2.5 : -2.5,
                });
            }
        }

        private void RecomputeRoute()
        {
            Route.Clear();
            if (Parcel >= DropPoints.Count) return;
            var drop = CurrentDrop;
            var goal = DeliveryRush.NearestRoadTile(Grid, DeliveryRush.WorldToTile(drop.x, drop.z).r, DeliveryRush.WorldToTile(drop.x, drop.z).c);
            var startTile = DeliveryRush.WorldToTile(VanX, VanZ);
            var start = DeliveryRush.NearestRoadTile(Grid, startTile.r, startTile.c);
            if (start == goal) { Route.Add(drop); return; }
            var path = DeliveryRush.RoadPathBetween(Grid, start, goal);
            if (path.Count == 0) { Route.Add(drop); return; }
            for (var i = 1; i < path.Count && Route.Count < RouteTiles; i++)
            {
                var w = DeliveryRush.TileToWorld(path[i].r, path[i].c);
                Route.Add(w);
            }
            Route.Add(drop);
        }

        private (double x, double z) Lookahead()
        {
            foreach (var p in Route)
            {
                var dx = p.x - VanX;
                var dz = p.z - VanZ;
                if (Math.Sqrt(dx * dx + dz * dz) >= BotLookaheadM) return p;
            }
            return Route.Count > 0 ? Route[Route.Count - 1] : CurrentDrop;
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Finished_?.Invoke();
        }

        /// <summary>Godot wrapf(a, −π, π) — range is [−π, π).</summary>
        private static double WrapPi(double a)
        {
            var range = Math.PI * 2.0;
            a = (a + Math.PI) % range;
            if (a < 0.0) a += range;
            return a - Math.PI;
        }

        private static double Mod(double v, double m)
        {
            var r = v % m;
            return r < 0.0 ? r + m : r;
        }
    }
}
