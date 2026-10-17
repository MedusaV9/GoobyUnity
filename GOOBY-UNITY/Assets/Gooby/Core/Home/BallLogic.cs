using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// Throwing the ball in the living room (port of ball_logic.gd ← web interactions.js flickToVelocity / stepBall /
    /// maybeFetch, CARE_TUNING.BALL): a flick on the ball throws it in an arc, it bounces with damping and friction
    /// inside a box around its spot; once it lies within reach Gooby runs over, nudges it back with his head (+3 fun,
    /// −0.2 weight, the balls counter) and there is a 15 s cooldown before he fetches again. Pure and deterministic;
    /// positions are local to the ball's spot (x right, y up, z away from the camera).
    /// </summary>
    public sealed class BallLogic
    {
        public const string Ruht = "RUHT";
        public const string Fliegt = "FLIEGT";
        public const string GoobyHolt = "GOOBY_HOLT";
        public const string BringtZurueck = "BRINGT_ZURUECK";
        public const double Gravity = 6.5, Restitution = 0.55, Friction = 1.6, Radius = 0.11, FlickVelScale = 0.0035, MaxSpeed = 6.0;
        public const double BoundX = 1.55, BoundZMin = -1.35, BoundZMax = 1.15, RestSpeed = 0.18, MinBounceVy = 0.25;
        public const double BallFun = 3.0, BallWeight = -0.2;
        public const int CooldownSec = 15;
        public const double ChaseMinDist = 0.05, ChaseMaxDist = 3.2;
        public const double ReturnVxScale = 260.0, ReturnVy = -640.0, MaxDt = 0.08;
        public const string LandungApport = "apport", LandungLiegen = "liegen", LandungHeim = "heim";
        public static readonly string[] SpruchKeys = { "ball.apport1", "ball.apport2", "ball.apport3" };

        public string Zustand = Ruht;
        public double X, Y = Radius, Z;
        public double Vx, Vy, Vz;
        public long CooldownBisMs;

        /// <summary>Screen flick (px/s, screen y down) → throw velocity (m/s): always at least 0.8 up, capped.</summary>
        public static (double x, double y, double z) FlickToVelocity(double fx, double fy)
        {
            var x = fx * FlickVelScale;
            var y = Math.Max(0.8, -fy * FlickVelScale);
            var z = Math.Abs(fy * FlickVelScale) * 0.35;
            var mag = Math.Sqrt(x * x + y * y + z * z);
            if (mag > MaxSpeed) { var k = MaxSpeed / mag; x *= k; y *= k; z *= k; }
            return (x, y, z);
        }

        public bool Werfen(double flickX, double flickY)
        {
            if (Zustand != Ruht) return false;
            (Vx, Vy, Vz) = FlickToVelocity(flickX, flickY);
            Zustand = Fliegt;
            return true;
        }

        /// <summary>One ballistic step (gravity, damped floor bounce with friction, walls of the box). Returns (bounced, resting).</summary>
        public (bool bounced, bool resting) Step(double dt)
        {
            if (Zustand != Fliegt && Zustand != BringtZurueck) return (false, false);
            var t = Math.Min(dt, MaxDt);
            var bounced = false;
            Vy -= Gravity * t;
            X += Vx * t; Y += Vy * t; Z += Vz * t;
            if (Y < Radius)
            {
                Y = Radius;
                if (Vy < 0.0)
                {
                    Vy = -Vy * Restitution;
                    if (Vy < MinBounceVy) Vy = 0.0;
                    else bounced = true;
                }
                var drag = Math.Max(0.0, 1.0 - Friction * t);
                Vx *= drag;
                Vz *= drag;
            }
            if (Math.Abs(X) > BoundX) { X = Math.Sign(X) * BoundX; Vx = -Vx * Restitution; bounced = true; }
            if (Z < BoundZMin) { Z = BoundZMin; Vz = -Vz * Restitution; bounced = true; }
            else if (Z > BoundZMax) { Z = BoundZMax; Vz = -Vz * Restitution; bounced = true; }
            var onFloor = Y - Radius < 0.02;
            var resting = onFloor && Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz) < RestSpeed;
            if (resting) { Vx = Vy = Vz = 0.0; }
            return (bounced, resting);
        }

        /// <summary>The ball came to rest: fetch, just lie there, or it is back home.</summary>
        public string LandungVerarbeiten(double distZuGooby, long nowMs)
        {
            if (Zustand == BringtZurueck) { Zustand = Ruht; return LandungHeim; }
            if (Zustand != Fliegt) return LandungLiegen;
            if (nowMs < CooldownBisMs || distZuGooby < ChaseMinDist || distZuGooby > ChaseMaxDist)
            {
                Zustand = Ruht;
                return LandungLiegen;
            }
            Zustand = GoobyHolt;
            return LandungApport;
        }

        /// <summary>Gooby is there: a head-nudge back toward the spot; the cooldown starts.</summary>
        public bool Kopfstoss(long nowMs)
        {
            if (Zustand != GoobyHolt) return false;
            (Vx, Vy, Vz) = FlickToVelocity(-X * ReturnVxScale, ReturnVy);
            Vz = -Vz;
            Zustand = BringtZurueck;
            CooldownBisMs = nowMs + CooldownSec * 1000L;
            return true;
        }

        public void ApportAbgebrochen()
        {
            if (Zustand == GoobyHolt) Zustand = Ruht;
        }

        public void Reset()
        {
            Zustand = Ruht;
            X = 0.0; Y = Radius; Z = 0.0;
            Vx = Vy = Vz = 0.0;
        }

        /// <summary>The fetch reward on the state: fun +3 (clamped), weight −0.2, balls +1. Returns the fun gained.</summary>
        public static double ApplyApportReward(JObject state)
        {
            var gooby = J.EnsureDict(state, "gooby");
            var before = J.Dict(gooby["stats"]);
            var was = Stats.ClampStat(before["fun"]);
            gooby["stats"] = Stats.ApplyDeltas(before, new Dictionary<string, double> { { "fun", BallFun } });
            var gained = Stats.ClampStat(J.Dict(gooby["stats"])["fun"]) - was;
            var weight = J.Finite(J.LeafNum(gooby["weight"])) ? J.LeafNum(gooby["weight"]) : 50.0;
            gooby["weight"] = Math.Max(5.0, Math.Min(95.0, weight + BallWeight));
            var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
            counters["balls"] = J.Int(counters["balls"]) + 1;
            return gained;
        }
    }
}
