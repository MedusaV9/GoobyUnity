using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Einkaufsfahrt round — port of city_drive.gd's _process/_step_van/_step_traffic/_crash/_check_pickups/
    /// _check_checkpoint/_finish_time_up/_autopilot over <see cref="CityDriveArcade"/>, engine-free. The car rolls on
    /// auto-throttle from the south ring heading east; drag left/right steers (softened at speed). Off the asphalt
    /// the target speed drops to 45 %. Coins respawn elsewhere when collected (+10), the pink checkpoint ring (+30)
    /// moves on. Traffic crash = speed × 0.3, 2 s grace and a strike; the third strike tows Gooby home and ends the
    /// round. When the 90 s run out without a crash: +50.
    /// </summary>
    public sealed class CityDriveGame
    {
        public const double IntroSec = 1.5;
        public const double RingM = 40.0;
        public const double RingLapM = 320.0;
        public const double CrossLapM = 160.0;
        public const int TrafficBase = 5;
        public const double TrafficSpeed = 6.5;
        public const double TrafficHitM = 3.2;
        public const double OffroadFactor = 0.45;
        public const double BotSteerGain = 1.6;
        public const double BumpCoolSec = 0.8;

        public sealed class Car
        {
            public double S, Speed, Lane;
            public bool Cross;
            public int Tint;
        }

        public CityDriveArcade.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public List<CityDriveArcade.Box> Colliders { get; }
        public readonly List<(double x, double z)> Coins;
        public readonly List<Car> Traffic = new List<Car>();
        public (double x, double z) Checkpoint { get; private set; }

        public double X { get; private set; }
        public double Z { get; private set; }
        public double Heading { get; private set; }
        public double Speed { get; private set; }
        public double Steer { get; private set; }
        public double Elapsed { get; private set; }
        public int Score { get; private set; }
        public int Pickups { get; private set; }
        public int Checkpoints { get; private set; }
        public int Crashes { get; private set; }
        public int Strikes { get; private set; }
        public bool Offroad { get; private set; }
        public bool Towed { get; private set; }
        public double CrashCool { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<int, double, double> CoinPicked;        // points, x, z
        public event Action<int, double, double> CheckpointReached; // points, x, z
        public event Action<int> Crashed;                            // strikes so far
        public event Action Bumped;                                  // hit a house (speed × 0.3, no strike)
        public event Action WentOffroad;                             // first time on the lawn
        public event Action<int> ZeroCrashBonused;
        public event Action TowedHome;                               // third strike
        public event Action Finished_;

        private double _bumpCool;
        private bool _offroadWarned;

        public CityDriveGame(string mode, int seed, double carSpeed = 1.0, double carHandling = 1.0, double carBoost = 1.0)
        {
            Tune = CityDriveArcade.WithCar(CityDriveArcade.ApplyDifficulty(CityDriveArcade.Arcade, mode), carSpeed, carHandling, carBoost);
            Rng = new GoobyRng(seed);
            Colliders = CityDriveArcade.LayoutColliders();
            Coins = CityDriveArcade.ScatterCoins(Rng, Tune.CoinsActive);
            X = 0.0;
            Z = RingM;
            Heading = Math.PI * 0.5;
            Checkpoint = CityDriveArcade.NextCheckpoint(Rng, (X, Z));
            SpawnTraffic();
        }

        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : CityDriveArcade.TimeLeft(Elapsed, Tune);

        public bool SetSteer(double value)
        {
            if (Finished || IntroLeft > 0.0) return false;
            Steer = Math.Max(-1.0, Math.Min(1.0, value));
            return true;
        }

        /// <summary>Autopilot (QA/tests): chases the nearest coin, takes checkpoints on the way. Never touches the RNG.</summary>
        public bool ApplyBot()
        {
            if (Finished || IntroLeft > 0.0) return false;
            var target = NearestCoin();
            var dx = target.x - X;
            var dz = target.z - Z;
            if (Math.Sqrt(dx * dx + dz * dz) < 0.5) return true;
            var want = Math.Atan2(dx, -dz);
            Steer = Math.Max(-1.0, Math.Min(1.0, WrapPi(want - Heading) * BotSteerGain));
            return true;
        }

        public (double x, double z) NearestCoin()
        {
            var best = Checkpoint;
            var bestD = CityDriveArcade.Dist((X, Z), Checkpoint);
            foreach (var coin in Coins)
            {
                var d = CityDriveArcade.Dist((X, Z), coin);
                if (d < bestD) { bestD = d; best = coin; }
            }
            return best;
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
            CrashCool = Math.Max(0.0, CrashCool - dt);
            _bumpCool = Math.Max(0.0, _bumpCool - dt);
            StepCar(dt);
            StepTraffic(dt);
            if (Finished) return;
            CheckPickups();
            CheckCheckpoint();
            if (CityDriveArcade.RoundOver(Elapsed, Tune)) FinishTimeUp();
        }

        private void StepCar(double dt)
        {
            Heading += Steer * CityDriveArcade.SteerRate(Tune) * CityDriveArcade.SpeedDamp(Speed) * dt;
            var target = CityDriveArcade.TargetSpeed(Elapsed, Tune);
            var tile = CityDriveArcade.WorldToTile(X, Z);
            Offroad = !CityDriveArcade.IsRoad(tile.r, tile.c);
            if (Offroad)
            {
                target *= OffroadFactor;
                if (!_offroadWarned) { _offroadWarned = true; WentOffroad?.Invoke(); }
            }
            Speed = CityDriveArcade.StepSpeed(Speed, target, dt, Tune);
            var dirX = Math.Sin(Heading);
            var dirZ = -Math.Cos(Heading);
            var nx = X + dirX * Speed * dt;
            var nz = Z + dirZ * Speed * dt;
            if (Blocked(nx, nz))
            {
                Speed *= 0.3;
                if (_bumpCool <= 0.0) { _bumpCool = BumpCoolSec; Bumped?.Invoke(); }
            }
            else { X = nx; Z = nz; }
            var limit = CityDriveArcade.TileM * (CityDriveArcade.Grid * 0.5);
            X = Math.Max(-limit, Math.Min(limit, X));
            Z = Math.Max(-limit, Math.Min(limit, Z));
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
                car.S = Mod(car.S + car.Speed * dt, RingLapM);
                var p = TrafficPos(car);
                if (CrashCool <= 0.0 && CityDriveArcade.Dist(p, (X, Z)) <= TrafficHitM)
                {
                    Crash();
                    if (Finished) return;
                }
            }
        }

        /// <summary>Position of a traffic car on its closed course (ring ±40 m or the cross street).</summary>
        public static (double x, double z) TrafficPos(Car car)
        {
            var lane = car.Lane;
            if (car.Cross)
            {
                var s = Mod(car.S, CrossLapM);
                var half = CrossLapM * 0.5;
                var x = (s <= half ? s : CrossLapM - s) - RingM;
                return (x, lane);
            }
            var s2 = car.S;
            var leg = RingLapM * 0.25;
            if (s2 < leg) return (-RingM + s2, -RingM + lane);
            if (s2 < leg * 2.0) return (RingM - lane, -RingM + (s2 - leg));
            if (s2 < leg * 3.0) return (RingM - (s2 - leg * 2.0), RingM - lane);
            return (-RingM + lane, RingM - (s2 - leg * 3.0));
        }

        /// <summary>Unit direction a traffic car drives in (for drawing it nose first).</summary>
        public static (double x, double z) TrafficDir(Car car)
        {
            var probe = new Car { S = Mod(car.S + 0.4, RingLapM), Lane = car.Lane, Cross = car.Cross };
            var a = TrafficPos(car);
            var b = TrafficPos(probe);
            var dx = b.x - a.x;
            var dz = b.z - a.z;
            var len = Math.Sqrt(dx * dx + dz * dz);
            return len > 0.001 ? (dx / len, dz / len) : (0.0, 1.0);
        }

        private void Crash()
        {
            CrashCool = Tune.CrashInvulnSec;
            Crashes += 1;
            Speed *= Tune.CrashSpeedMult;
            var strike = FrameworkLogic.ApplyStrike(Strikes);
            Strikes = strike.strikes;
            Crashed?.Invoke(Strikes);
            if (strike.teleport)
            {
                Towed = true;
                TowedHome?.Invoke();
                Finish();
            }
        }

        private void CheckPickups()
        {
            for (var i = 0; i < Coins.Count; i++)
            {
                if (CityDriveArcade.Dist((X, Z), Coins[i]) > Tune.PickupRadiusM) continue;
                var prev = Score;
                Score = CityDriveArcade.ApplyPickup(Score, Tune);
                Pickups += 1;
                var collected = Coins[i];
                var fresh = CityDriveArcade.ScatterCoins(Rng, 1);
                if (fresh.Count > 0) Coins[i] = fresh[0];
                CoinPicked?.Invoke(Score - prev, collected.x, collected.z);
            }
        }

        private void CheckCheckpoint()
        {
            if (CityDriveArcade.Dist((X, Z), Checkpoint) > Tune.CheckpointRadiusM) return;
            var prev = Score;
            Score = CityDriveArcade.ApplyCheckpoint(Score, Tune);
            Checkpoints += 1;
            var reached = Checkpoint;
            Checkpoint = CityDriveArcade.NextCheckpoint(Rng, (X, Z));
            CheckpointReached?.Invoke(Score - prev, reached.x, reached.z);
        }

        private void FinishTimeUp()
        {
            var bonus = CityDriveArcade.ZeroCrash(Crashes, Tune);
            if (bonus > 0)
            {
                Score += bonus;
                ZeroCrashBonused?.Invoke(bonus);
            }
            Finish();
        }

        private void SpawnTraffic()
        {
            var count = FrameworkLogic.JsRound(TrafficBase * Tune.TrafficDensityMult);
            for (var i = 0; i < count; i++)
            {
                Traffic.Add(new Car
                {
                    S = Rng.Next() * RingLapM,
                    Speed = TrafficSpeed * (0.8 + Rng.Next() * 0.5),
                    Cross = i % 3 == 2,
                    Lane = Rng.Next() < 0.5 ? 2.5 : -2.5,
                    Tint = i % 2,
                });
            }
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Finished_?.Invoke();
        }

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
