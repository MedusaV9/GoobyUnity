using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Korbjagd — port of GOOBY/src/minigames/games/basketBounce.logic.js (§C6.1 #7 / §C10.2): a flick throws
    /// the ball, the ballistic arc bounces off rim + board, basket +3, bank basket +2 extra, swish streak +2
    /// (×2 while the hoop slides). All positions are 3D metres (double precision — the web parity depends on it).
    /// </summary>
    public static class BasketBounce
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0;
            public int PointsBasket = 3, PointsBankExtra = 2, PointsSwishExtra = 2, SwishStreakFrom = 2, SlideAfterBaskets = 10;
            public double SlideAmplitude = 1.0, SlidePeriodSec = 3.6;
            public int MovingSwishMult = 2;
            public double DistStart = 5.2, DistPerBasket = 0.35, DistMax = 8.0;
            public double BallR = 0.24, RimR = 0.46, RimTube = 0.035, RimY = 2.6, BoardGap = 0.62, BoardW = 1.9, BoardH = 1.35, BoardBottomY = 2.35;
            public double SpawnX = 0.0, SpawnY = 1.1, SpawnZ = 4.6, Gravity = 9.8, RimRestitution = 0.5, BoardRestitution = 0.55;
            public double FlickVelYScale = 0.0042, FlickVelZScale = 0.003, FlickVelXScale = 0.0035, FlickMinUpVel = 320.0, FlickMaxSpeed = 13.5;
            public double SimDt = 1.0 / 120.0, SimTimeoutSec = 5.0, MaxSweepStepM = 0.1, FloorY = 0.0;
            public double SpawnIntervalMult = 1.0, WindowMult = 1.0, DurationMult = 1.0, ScoreRadiusScale = 1.0;
            public double ShotResetSec = 0.55, AutoIntervalMinSec = 0.5, AutoIntervalRangeSec = 0.5;
            public bool Endless = false;
            public int EndlessConsecutiveMisses = 3;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Basket = new Tune();
        public const int OnFireFrom = 3;

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double spawn, windowRow, duration;
            bool endless;
            switch (mode)
            {
                case "easy": spawn = 1.2; windowRow = 1.25; duration = 1.2; endless = false; break;
                case "hard": spawn = 0.85; windowRow = 0.8; duration = 1.0; endless = false; break;
                case "endless": spawn = 0.85; windowRow = 0.8; duration = 1.0; endless = true; break;
                default: return tune;
            }
            var window = Math.Max(0.55, windowRow);
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * duration;
            o.ScoreRadiusScale = window;
            o.ShotResetSec = tune.ShotResetSec * spawn;
            o.AutoIntervalMinSec = tune.AutoIntervalMinSec * spawn;
            o.AutoIntervalRangeSec = tune.AutoIntervalRangeSec * spawn;
            o.SpawnIntervalMult = spawn;
            o.WindowMult = window;
            o.DurationMult = duration;
            o.Endless = endless;
            o.Mode = mode;
            return o;
        }

        public static double HoopSlideX(double elapsedSlide, int basketsMade, Tune tune)
        {
            if (basketsMade < tune.SlideAfterBaskets) return 0.0;
            var phase = (elapsedSlide / tune.SlidePeriodSec) * Math.PI * 2.0;
            return tune.SlideAmplitude * Math.Sin(phase);
        }

        public static double HoopDistance(int basketsMade, Tune tune) => Math.Min(tune.DistMax, tune.DistStart + basketsMade * tune.DistPerBasket);

        public struct Vel
        {
            public double X, Y, Z;
            public bool Valid;
        }

        public static Vel FlickToVelocity(double vx, double vy, Tune tune)
        {
            var up = -vy;
            if (up < tune.FlickMinUpVel) return default;
            var o = new Vel { X = vx * tune.FlickVelXScale, Y = up * tune.FlickVelYScale, Z = -up * tune.FlickVelZScale, Valid = true };
            var speed = Hypot3(o.X, o.Y, o.Z);
            if (speed > tune.FlickMaxSpeed)
            {
                var k = tune.FlickMaxSpeed / speed;
                o.X *= k; o.Y *= k; o.Z *= k;
            }
            return o;
        }

        public sealed class Ball
        {
            public double Px, Py, Pz, Vx, Vy, Vz;
            public bool Rim, Board;
        }

        public struct Hoop
        {
            public double X, Z;
        }

        public struct Events
        {
            public bool Rim, Board, BasketMade, Dead;
        }

        public static Ball MakeBall(Vel vel, Tune tune) => new Ball { Px = tune.SpawnX, Py = tune.SpawnY, Pz = tune.SpawnZ, Vx = vel.X, Vy = vel.Y, Vz = vel.Z };

        public static (double dist, double nx, double ny, double nz) RingDistance(Ball ball, Hoop hoop, Tune tune)
        {
            var dx = ball.Px - hoop.X;
            var dz = ball.Pz - hoop.Z;
            var horiz = Math.Sqrt(dx * dx + dz * dz);
            if (horiz < 1e-9) { dx = 1.0; dz = 0.0; horiz = 1.0; }
            var cx = hoop.X + (dx / horiz) * tune.RimR;
            var cz = hoop.Z + (dz / horiz) * tune.RimR;
            var nx = ball.Px - cx;
            var ny = ball.Py - tune.RimY;
            var nz = ball.Pz - cz;
            var dist = Hypot3(nx, ny, nz);
            if (dist == 0.0) dist = 1e-9;
            return (dist, nx / dist, ny / dist, nz / dist);
        }

        public static Events StepBall(Ball ball, double dt, Hoop hoop, Tune tune)
        {
            var ev = new Events();
            var yBefore = ball.Py;
            var rBefore = Hypot2(ball.Px - hoop.X, ball.Pz - hoop.Z);
            ball.Vy -= tune.Gravity * dt;
            ball.Px += ball.Vx * dt;
            ball.Py += ball.Vy * dt;
            ball.Pz += ball.Vz * dt;
            var rAfter = Hypot2(ball.Px - hoop.X, ball.Pz - hoop.Z);
            var rimY = tune.RimY;
            if (yBefore > rimY && ball.Py <= rimY && ball.Vy < 0.0)
            {
                var rAtCross = (rBefore + rAfter) / 2.0;
                var gate = tune.RimR * tune.ScoreRadiusScale - tune.BallR * 0.35;
                if (rAtCross < gate)
                {
                    ev.BasketMade = true;
                    return ev;
                }
            }
            BounceRim(ball, ref ev, hoop, tune);
            BounceBoard(ball, ref ev, hoop, tune);
            var boardZ = hoop.Z - tune.BoardGap;
            if (ball.Py < tune.FloorY + tune.BallR || ball.Pz < boardZ - 3.0 || Math.Abs(ball.Px) > 8.0) ev.Dead = true;
            return ev;
        }

        public static Events StepBallSwept(Ball ball, double dt, Hoop hoop, Tune tune)
        {
            var travel = Hypot3(ball.Vx, ball.Vy, ball.Vz) * Math.Max(0.0, dt);
            var steps = Math.Max(1, (int)Math.Ceiling(travel / tune.MaxSweepStepM));
            var h = dt / steps;
            var total = new Events();
            for (var i = 0; i < steps; i++)
            {
                var ev = StepBall(ball, h, hoop, tune);
                total.Rim |= ev.Rim; total.Board |= ev.Board; total.BasketMade |= ev.BasketMade; total.Dead |= ev.Dead;
                if (ev.BasketMade || ev.Dead) break;
            }
            return total;
        }

        public struct ShotResult
        {
            public string Result;
            public bool Bank, Swish;
            public double FlightSec;
        }

        public static ShotResult SimulateShot(Vel vel, Hoop hoop, Tune tune)
        {
            var ball = MakeBall(vel, tune);
            var t = 0.0;
            while (t < tune.SimTimeoutSec)
            {
                var ev = StepBallSwept(ball, tune.SimDt, hoop, tune);
                t += tune.SimDt;
                if (ev.BasketMade) return new ShotResult { Result = "basket", Bank = ball.Board, Swish = !ball.Rim && !ball.Board, FlightSec = t };
                if (ev.Dead) break;
            }
            return new ShotResult { Result = "miss", FlightSec = t };
        }

        public static (int points, int swishStreak) ScoreShot(bool made, bool swish, bool bank, int swishStreak, bool moving, Tune tune)
        {
            if (!made) return (0, 0);
            var points = tune.PointsBasket;
            if (bank) points += tune.PointsBankExtra;
            var streak = 0;
            if (swish)
            {
                streak = swishStreak + 1;
                if (streak >= tune.SwishStreakFrom) points += tune.PointsSwishExtra;
            }
            if (moving && swish) points *= tune.MovingSwishMult;
            return (points, streak);
        }

        public static bool IsMovingHoop(int basketsMade, Tune tune) => basketsMade >= tune.SlideAfterBaskets;

        public static Vel SolveBasketVelocity(Hoop hoop, GoobyRng rng, Tune tune)
        {
            var dist = tune.SpawnZ - hoop.Z;
            var rise = tune.RimY - tune.SpawnY;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var upBase = 5.6 + dist * 0.62;
                var vy = upBase * (0.92 + rng.Next() * 0.16);
                var disc = vy * vy - 2.0 * tune.Gravity * rise;
                if (disc <= 0.0) continue;
                var flight = (vy + Math.Sqrt(disc)) / tune.Gravity;
                var vz = -(dist / Math.Max(0.3, flight)) * (0.97 + rng.Next() * 0.06);
                var vx = ((hoop.X - tune.SpawnX) / Math.Max(0.3, flight)) * (0.95 + rng.Next() * 0.1);
                var cand = new Vel { X = vx, Y = vy, Z = vz, Valid = true };
                if (SimulateShot(cand, hoop, tune).Result == "basket") return cand;
            }
            return default;
        }

        public static bool IsRoundOver(double elapsed, int missStreak, Tune tune) =>
            tune.Endless ? missStreak >= tune.EndlessConsecutiveMisses : elapsed >= tune.DurationSec;

        public static bool IsOnFire(int swishStreak) => swishStreak >= OnFireFrom;

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1)
        {
            var tune = ApplyDifficulty(Basket, mode);
            var rng = new GoobyRng(seed);
            var accuracy = mode == "easy" ? 0.94 : (mode == "hard" || mode == "endless" ? 0.78 : 0.87);
            var elapsed = 0.0;
            int missStreak = 0, swishStreak = 0, baskets = 0, score = 0;
            while (!IsRoundOver(elapsed, missStreak, tune) && elapsed < 240.0)
            {
                var made = rng.Next() < accuracy;
                var swish = made && rng.Next() < 0.62;
                var bank = made && !swish && rng.Next() < 0.35;
                var (points, streak) = ScoreShot(made, swish, bank, swishStreak, IsMovingHoop(baskets, tune), tune);
                swishStreak = streak;
                score += points;
                if (made) { baskets += 1; missStreak = 0; }
                else missStreak += 1;
                elapsed += 1.35 + tune.ShotResetSec + tune.AutoIntervalMinSec + rng.Next() * tune.AutoIntervalRangeSec;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["elapsed"] = elapsed, ["missStreak"] = missStreak, ["baskets"] = baskets };
        }

        private static void BounceRim(Ball ball, ref Events ev, Hoop hoop, Tune tune)
        {
            var (dist, nx, ny, nz) = RingDistance(ball, hoop, tune);
            var reach = tune.BallR + tune.RimTube;
            if (dist >= reach) return;
            var vn = ball.Vx * nx + ball.Vy * ny + ball.Vz * nz;
            if (vn >= 0.0) return;
            var k = -(1.0 + tune.RimRestitution) * vn;
            ball.Vx += nx * k; ball.Vy += ny * k; ball.Vz += nz * k;
            var push = reach - dist + 0.005;
            ball.Px += nx * push; ball.Py += ny * push; ball.Pz += nz * push;
            ball.Rim = true;
            ev.Rim = true;
        }

        private static void BounceBoard(Ball ball, ref Events ev, Hoop hoop, Tune tune)
        {
            var boardZ = hoop.Z - tune.BoardGap;
            var within = Math.Abs(ball.Px - hoop.X) < tune.BoardW / 2.0 && ball.Py > tune.BoardBottomY && ball.Py < tune.BoardBottomY + tune.BoardH;
            if (!within) return;
            if (ball.Pz - tune.BallR >= boardZ || ball.Vz >= 0.0) return;
            ball.Pz = boardZ + tune.BallR;
            ball.Vz = -ball.Vz * tune.BoardRestitution;
            ball.Board = true;
            ev.Board = true;
        }

        private static double Hypot2(double a, double b) => Math.Sqrt(a * a + b * b);
        private static double Hypot3(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);
    }
}
