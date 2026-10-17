using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Einkaufsfahrt (cityDrive) — port of city_drive_logic.gd, the Godot round version of the web §C4.7 coin run:
    /// 90 s, 26 live coins (respawn on pickup, 3 m radius), auto-throttle 9 → 15 m/s (ramp after 20 s), a crash costs
    /// speed × 0.3 + 2 s grace, the third crash tows Gooby home (strike-out). Godot-own derivations kept verbatim:
    /// points ×10 (1 coin = 10), checkpoint rings +30, zero-crash bonus +50, difficulty rows after the deliveryRush
    /// pattern. Carries its own compact 7×7 ring-and-cross town.
    /// </summary>
    public static class CityDriveArcade
    {
        public sealed class Tune
        {
            public double DurationSec = 90.0;
            public int CoinsActive = 26;
            public double PickupRadiusM = 3.0;
            public int PickupPoints = 10, CheckpointPoints = 30, ZeroCrashBonus = 50;
            public double CheckpointRadiusM = 4.0;
            public double CrashSpeedMult = 0.3, CrashInvulnSec = 2.0;
            public double BaseSpeed = 9.0, MaxSpeed = 15.0, RampDelaySec = 20.0, SpeedRampSec = 22.0;
            public double SteerRate = 1.9, AccelUp = 5.5, AccelDown = 9.0;
            public double SpeedMult = 1.0, TrafficDensityMult = 1.0;
            public double CarSpeedMult = 1.0, CarHandlingMult = 1.0, CarBoostMult = 1.0;
            public int StrikeLimit = 3;
            public bool Endless;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Arcade = new Tune();

        /// <summary>A checkpoint spawns at least this far away — otherwise an "arrival" would be free.</summary>
        public const double CheckpointMinDistM = 40.0;

        // ── compact town (7×7 ring + cross, tiles like the 9×9 city) ──
        public const int Grid = 7;
        public const double TileM = 20.0;
        public const int Center = 3;
        public const int RingMin = 1;
        public const int RingMax = Grid - 2;

        /// <summary>CityCarFeel.MAX_SPEED: the steering damp saturates at this speed.</summary>
        public const double FeelMaxSpeed = 13.0;

        public struct Box
        {
            public double MinX, MaxX, MinZ, MaxZ;
        }

        /// <summary>Tile (r, c) → world centre; x grows east (+c), z grows south (+r).</summary>
        public static (double x, double z) TileToWorld(int r, int c) => ((c - Center) * TileM, (r - Center) * TileM);

        public static (int r, int c) WorldToTile(double x, double z) =>
            (FrameworkLogic.JsRound(z / TileM + Center), FrameworkLogic.JsRound(x / TileM + Center));

        public static bool IsRoad(int r, int c)
        {
            if (r < RingMin || r > RingMax || c < RingMin || c > RingMax) return false;
            var onRing = r == RingMin || r == RingMax || c == RingMin || c == RingMax;
            var onCross = r == Center || c == Center;
            return onRing || onCross;
        }

        public static List<(int r, int c)> RoadTiles()
        {
            var tiles = new List<(int r, int c)>();
            for (var r = 0; r < Grid; r++)
            for (var c = 0; c < Grid; c++)
                if (IsRoad(r, c)) tiles.Add((r, c));
            return tiles;
        }

        /// <summary>Building colliders per built block tile (axis-aligned, 0.34 tile half-size).</summary>
        public static List<Box> LayoutColliders()
        {
            var boxes = new List<Box>();
            var half = TileM * 0.34;
            for (var r = 0; r < Grid; r++)
            for (var c = 0; c < Grid; c++)
            {
                if (IsRoad(r, c) || r < RingMin || r > RingMax || c < RingMin || c > RingMax) continue;
                var w = TileToWorld(r, c);
                boxes.Add(new Box { MinX = w.x - half, MaxX = w.x + half, MinZ = w.z - half, MaxZ = w.z + half });
            }
            return boxes;
        }

        /// <summary>
        /// §G5 modes after the deliveryRush pattern: normal = web arcade semantics, easy/hard turn speed and traffic,
        /// endless drops the timer (the round then only ends with the third strike).
        /// </summary>
        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var t = tune.Clone();
            t.SpeedMult = hard ? 1.2 : 0.85;
            t.TrafficDensityMult = hard ? 1.15 : 0.85;
            t.Endless = mode == "endless";
            t.Mode = mode;
            return t;
        }

        /// <summary>The garage's car multipliers (CarStats) mixed in; values outside 0.5–1.5 are clamped.</summary>
        public static Tune WithCar(Tune tune, double speed, double handling, double boost)
        {
            var t = tune.Clone();
            t.CarSpeedMult = Clamp(speed, 0.5, 1.5);
            t.CarHandlingMult = Clamp(handling, 0.5, 1.5);
            t.CarBoostMult = Clamp(boost, 0.5, 1.5);
            return t;
        }

        /// <summary>Target speed at round time t: BASE until RAMP_DELAY, then linear to MAX over SPEED_RAMP — × difficulty × car.</summary>
        public static double TargetSpeed(double elapsed, Tune tune)
        {
            var ramp = Clamp((elapsed - tune.RampDelaySec) / tune.SpeedRampSec, 0.0, 1.0);
            var speed = tune.BaseSpeed + (tune.MaxSpeed - tune.BaseSpeed) * ramp;
            return speed * tune.SpeedMult * tune.CarSpeedMult;
        }

        /// <summary>One speed step towards the target (5.5 up / 9 down m/s²); the boost multiplier only speeds up.</summary>
        public static double StepSpeed(double speed, double target, double dt, Tune tune)
        {
            var accel = speed < target ? tune.AccelUp * tune.CarBoostMult : tune.AccelDown;
            return speed + Math.Sign(target - speed) * Math.Min(Math.Abs(target - speed), accel * dt);
        }

        public static double SteerRate(Tune tune) => tune.SteerRate * tune.CarHandlingMult;

        /// <summary>CityCarFeel.speed_damp: steering softens by up to 25 % towards top speed.</summary>
        public static double SpeedDamp(double speed) => 1.0 - 0.25 * Math.Min(1.0, speed / FeelMaxSpeed);

        public static int ApplyPickup(int score, Tune tune) => score + tune.PickupPoints;
        public static int ApplyCheckpoint(int score, Tune tune) => score + tune.CheckpointPoints;
        public static int ZeroCrash(int crashes, Tune tune) => crashes <= 0 ? tune.ZeroCrashBonus : 0;
        public static double TimeLeft(double elapsed, Tune tune) => Math.Max(0.0, tune.DurationSec - elapsed);
        public static bool RoundOver(double elapsed, Tune tune) => !tune.Endless && elapsed >= tune.DurationSec;

        /// <summary>Seeded coin scatter: random road tiles with ±6 m jitter (stays on the 13-m asphalt band).</summary>
        public static List<(double x, double z)> ScatterCoins(GoobyRng rng, int count)
        {
            var tiles = RoadTiles();
            var coins = new List<(double x, double z)>();
            for (var i = 0; i < count; i++)
            {
                var tile = tiles[(int)(rng.Next() * tiles.Count) % tiles.Count];
                var w = TileToWorld(tile.r, tile.c);
                var jx = (rng.Next() - 0.5) * 12.0;
                var jz = (rng.Next() - 0.5) * 12.0;
                coins.Add((w.x + jx, w.z + jz));
            }
            return coins;
        }

        /// <summary>Seeded checkpoint: a road tile at least CheckpointMinDistM away — the farthest candidate otherwise.</summary>
        public static (double x, double z) NextCheckpoint(GoobyRng rng, (double x, double z) from)
        {
            var tiles = RoadTiles();
            var best = from;
            var bestD = -1.0;
            for (var guard = 0; guard < 12; guard++)
            {
                var tile = tiles[(int)(rng.Next() * tiles.Count) % tiles.Count];
                var w = TileToWorld(tile.r, tile.c);
                var d = Dist(from, w);
                if (d >= CheckpointMinDistM) return w;
                if (d > bestD) { bestD = d; best = w; }
            }
            return best;
        }

        public static int RoundScore(int pickups, int checkpoints, int crashes, Tune tune)
        {
            var score = 0;
            for (var i = 0; i < pickups; i++) score = ApplyPickup(score, tune);
            for (var i = 0; i < checkpoints; i++) score = ApplyCheckpoint(score, tune);
            return score + ZeroCrash(crashes, tune);
        }

        /// <summary>Closed-form certification model: hard drives faster (more coins) but crashes more; easy &lt; normal &lt; hard per seed.</summary>
        public static JObject SimulateAutoplay(int seed, string mode = "normal")
        {
            var tune = ApplyDifficulty(Arcade, mode);
            var jitter = seed % 5 - 2;
            int pickups = 20, checkpoints = 4, crashes = 1;
            if (mode == "easy") { pickups = 16; checkpoints = 3; crashes = 0; }
            else if (mode == "hard" || mode == "endless") { pickups = 25; checkpoints = 5; crashes = 2; }
            pickups = Math.Max(0, pickups + jitter);
            return new JObject
            {
                ["seed"] = seed, ["mode"] = mode, ["score"] = RoundScore(pickups, checkpoints, crashes, tune),
                ["pickups"] = pickups, ["checkpoints"] = checkpoints, ["crashes"] = crashes,
            };
        }

        public static double Dist((double x, double z) a, (double x, double z) b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    }

    /// <summary>
    /// Autohaus stats (1–10, base 3) → drive multipliers — port of car_stats_logic.gd: speed 3 %/step (0.85–1.25),
    /// handling 2.5 %/step (0.85–1.2), boost 3 %/step (0.85–1.3).
    /// </summary>
    public static class CarStats
    {
        public const int StatBase = 3, StatMin = 1, StatMax = 10;

        public static int ClampStat(int value) => Math.Max(StatMin, Math.Min(StatMax, value));
        public static double SpeedMult(int speed) => Mult(speed, 0.03, 0.85, 1.25);
        public static double HandlingMult(int handling) => Mult(handling, 0.025, 0.85, 1.2);
        public static double BoostMult(int boost) => Mult(boost, 0.03, 0.85, 1.3);

        private static double Mult(int value, double step, double lo, double hi) =>
            Math.Max(lo, Math.Min(hi, 1.0 + (ClampStat(value) - StatBase) * step));
    }
}
