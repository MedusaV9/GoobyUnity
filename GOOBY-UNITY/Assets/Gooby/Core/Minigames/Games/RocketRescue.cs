using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Raketen-Rettung — port of GOOBY/src/minigames/games/rocketRescue.logic.js (PLAN3 §C10.1 #3): lander physics
    /// with thrust + tilt, 5 seeded platforms each with a stranded bunny, tank 100 / burn 8 per second,
    /// telegraphed gusts from the 3rd rescue leg, hard landing = bounce −10 fuel (NEVER death),
    /// score = 30·rescued + fuel/2 + 5 per soft landing. Engine (state machine) + PD autopilot (bot).
    /// </summary>
    public static class RocketRescue
    {
        public sealed class Tune
        {
            public double DurationSec = 120.0, WorldHalfW = 8.0, CeilingY = 11.0, Gravity = 2.4, ThrustAccel = 5.6, TiltMaxRad = 0.5, TiltRate = 3.2, WallRestitution = 0.3;
            public double FuelMax = 100.0, FuelBurnPerSec = 8.0;
            public int FuelPickupCount = 8;
            public double FuelPickupAmount = 30.0, FuelPickupRadius = 0.85, FuelRespawnSec = 9.0;
            public int PlatformCount = 5;
            public double PlatformHalfW = 1.05, PadX = 0.0, PadY = 0.0, PadHalfW = 1.6, LandMaxVy = 1.2, SoftMaxVy = 0.5;
            public int RescuePoints = 30, SoftLandingBonus = 5;
            public double FuelScoreDivisor = 2.0, DepartClearM = 0.4, HardFuelPenalty = 10.0, BounceRestitution = 0.45;
            public int WindFromRescues = 2;
            public double WindTelegraphSec = 1.0, WindGustSec = 1.6, WindAccel = 1.7, WindEveryMinSec = 6.0, WindEveryMaxSec = 10.0, TowSpeed = 3.4, MaxDt = 1.0 / 20.0;
            public double BotCruiseClearanceM = 1.2, BotAlignXM = 0.45, BotAlignVx = 0.8, BotMaxVx = 2.7, BotVxGain = 1.6, BotLatAccelEff = 1.0, BotDescendVx = 0.7, BotTiltDeadband = 0.22;
            public double BotVyGain = 0.85, BotMaxRise = 2.6, BotMaxDescend = 1.1, BotSoftDescend = 0.42, BotFlareBelowM = 1.2, BotRefuelEnter = 45.0, BotRefuelExit = 75.0, BotRefuelRangeM = 12.0, BotRefuelSkipBelowM = 2.5;
            public bool Endless = false;
            public double EndlessThinPerRescue = 0.1, PickupRate = 1.0;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Rocket = new Tune();
        public static readonly double[] Bands = { 2.4, 3.9, 5.4, 6.9, 8.3 };
        public const int PadId = -1;

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double tol;
            bool endless;
            switch (mode)
            {
                case "easy": tol = 1.25; endless = false; break;
                case "hard": tol = 0.8; endless = false; break;
                case "endless": tol = 0.8; endless = true; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.LandMaxVy = tune.LandMaxVy * tol;
            o.SoftMaxVy = tune.SoftMaxVy * tol;
            o.PlatformHalfW = tune.PlatformHalfW * tol;
            o.Endless = endless;
            o.Mode = mode;
            return o;
        }

        public static Tune ApplyModifier(Tune tune, string type, double coinRate)
        {
            if (type != "muenzregen") return tune;
            var raw = coinRate;
            var rate = Math.Max(0.1, !double.IsNaN(raw) && !double.IsInfinity(raw) && raw != 0.0 ? raw : 1.0);
            var o = tune.Clone();
            o.FuelPickupCount = (int)Math.Round(tune.FuelPickupCount * rate, MidpointRounding.AwayFromZero);
            o.FuelRespawnSec = tune.FuelRespawnSec / rate;
            o.PickupRate = rate;
            return o;
        }

        /// <summary>V8-accurate Math.hypot (Kahan summation on values normalised by the largest magnitude).</summary>
        public static double Hypot(double x, double y)
        {
            if (double.IsInfinity(x) || double.IsInfinity(y)) return double.PositiveInfinity;
            var ax = Math.Abs(x);
            var ay = Math.Abs(y);
            var scale = Math.Max(ax, ay);
            if (scale == 0.0) scale = 1.0;
            var total = 0.0;
            var comp = 0.0;
            foreach (var value in new[] { ax / scale, ay / scale })
            {
                var summand = value * value - comp;
                var prelim = total + summand;
                comp = (prelim - total) - summand;
                total = prelim;
            }
            return Math.Sqrt(total) * scale;
        }

        public sealed class Platform
        {
            public double X, Y, HalfW;
            public bool Bunny;
        }

        public sealed class FuelPickup
        {
            public double X, Y;
            public bool Taken;
            public double RespawnT;
        }

        public sealed class Layout
        {
            public List<Platform> Platforms = new List<Platform>();
            public List<FuelPickup> FuelPickups = new List<FuelPickup>();
            public double PadX, PadY, PadHalfW;
        }

        public static Layout CreateLayout(Func<double> rng, Tune tune)
        {
            var layout = new Layout { PadX = tune.PadX, PadY = tune.PadY, PadHalfW = tune.PadHalfW };
            var bands = (double[])Bands.Clone();
            for (var i = bands.Length - 1; i > 0; i--)
            {
                var j = (int)Math.Floor(rng() * (i + 1));
                (bands[i], bands[j]) = (bands[j], bands[i]);
            }
            for (var i = 0; i < tune.PlatformCount; i++)
            {
                var y = bands[i % bands.Length] + (rng() - 0.5) * 0.6;
                var x = 0.0;
                var ok = false;
                var attempt = 0;
                while (attempt < 24 && !ok)
                {
                    x = (rng() * 2.0 - 1.0) * (tune.WorldHalfW - 1.4);
                    ok = (Math.Abs(x) >= layout.PadHalfW + 1.0 || y >= 4.5) && ClearOfPlatforms(layout.Platforms, x, y);
                    attempt += 1;
                }
                if (!ok) x = (i % 2 == 0 ? 1.0 : -1.0) * (2.4 + i * 1.1);
                layout.Platforms.Add(new Platform { X = x, Y = y, HalfW = tune.PlatformHalfW, Bunny = true });
            }
            for (var i = 0; i < tune.FuelPickupCount; i++)
            {
                var x = 0.0;
                var y = 0.0;
                var ok = false;
                var attempt = 0;
                while (attempt < 24 && !ok)
                {
                    x = (rng() * 2.0 - 1.0) * (tune.WorldHalfW - 1.1);
                    y = 1.6 + rng() * (tune.CeilingY - 2.6);
                    ok = ClearOfPickupSpots(layout.Platforms, x, y);
                    attempt += 1;
                }
                layout.FuelPickups.Add(new FuelPickup { X = x, Y = y });
            }
            return layout;
        }

        private static bool ClearOfPlatforms(List<Platform> platforms, double x, double y)
        {
            foreach (var p in platforms) if (Math.Abs(p.X - x) < 2.3 && Math.Abs(p.Y - y) < 1.5) return false;
            return true;
        }

        private static bool ClearOfPickupSpots(List<Platform> platforms, double x, double y)
        {
            foreach (var p in platforms) if (Math.Abs(p.X - x) <= 1.4 && Math.Abs(p.Y - y) <= 1.2) return false;
            return true;
        }

        public static string ClassifyLanding(double vyAbs, Tune tune)
        {
            if (vyAbs <= tune.SoftMaxVy) return "soft";
            if (vyAbs <= tune.LandMaxVy) return "ok";
            return "hard";
        }

        public static int RoundScore(int rescued, double fuelRemaining, int softLandings, Tune tune) =>
            Math.Max(0, (int)Math.Floor(tune.RescuePoints * rescued + Math.Max(0.0, fuelRemaining) / tune.FuelScoreDivisor + tune.SoftLandingBonus * softLandings));

        public static int TiltCommandFor(double nx, bool hasTouch = true)
        {
            if (!hasTouch) return 0;
            if (nx < -1.0 / 3.0) return -1;
            if (nx > 1.0 / 3.0) return 1;
            return 0;
        }

        // ── engine ─────────────────────────────────────────────────────────

        public struct Input
        {
            public bool Thrust;
            public int TiltDir;
        }

        public sealed class Event
        {
            public string Type;
            public int? Where;
            public double Vy, Amount;
            public string Kind, Reason;
            public int Index, Dir, Count;
            public bool BonusEligible;
        }

        public sealed class Engine
        {
            public Tune Tune;
            public Layout Layout;
            public double X, Y, Vx, Vy, Tilt, Fuel;
            public bool Carrying;
            public int Rescued, SoftLandings, HardLandings;
            public int? LandedOn = PadId, DepartedFrom, LastLandedOn = PadId;
            public bool Towing, Ended;
            public string EndReason;
            public double Elapsed;
            public bool FuelLowFired;
            public string WindPhase = "idle";
            public int WindDir = 1;
            public double WindT, WindNextAt;
            private readonly Func<double> _rng;

            public Engine(Func<double> rng, Tune tune)
            {
                _rng = rng;
                Tune = tune;
                Layout = CreateLayout(rng, tune);
                X = Layout.PadX;
                Y = Layout.PadY;
                Fuel = tune.FuelMax;
                var span = tune.WindEveryMaxSec - tune.WindEveryMinSec;
                WindNextAt = tune.WindEveryMinSec + _rng() * span;
            }

            public struct Surface
            {
                public int Id;
                public double X, Y, HalfW;
            }

            public List<Surface> Surfaces()
            {
                var list = new List<Surface> { new Surface { Id = PadId, X = Layout.PadX, Y = Layout.PadY, HalfW = Layout.PadHalfW } };
                for (var i = 0; i < Layout.Platforms.Count; i++)
                {
                    var p = Layout.Platforms[i];
                    list.Add(new Surface { Id = i, X = p.X, Y = p.Y, HalfW = p.HalfW });
                }
                return list;
            }

            public int Score() => RoundScore(Rescued, Fuel, SoftLandings, Tune);

            public List<Event> Step(Input input, double dt)
            {
                var events = new List<Event>();
                if (Ended) return events;
                var stepDt = Math.Min(Tune.MaxDt, Math.Max(0.0, dt));
                Elapsed += stepDt;
                if (!Tune.Endless && Elapsed >= Tune.DurationSec)
                {
                    EndRun("time", events);
                    return events;
                }
                TickRespawns(stepDt);
                if (Towing)
                {
                    Tow(stepDt, events);
                    return events;
                }
                SlewTilt(input, stepDt);
                var thrusting = input.Thrust && Fuel > 0.0;
                if (LandedOn.HasValue && !thrusting)
                {
                    Vx = 0.0;
                    Vy = 0.0;
                }
                else Fly(thrusting, stepDt, events);
                TickWind(stepDt, events);
                CheckFuel(events);
                return events;
            }

            private void EndRun(string reason, List<Event> events)
            {
                if (Ended) return;
                Ended = true;
                EndReason = reason;
                events.Add(new Event { Type = "ended", Reason = reason });
            }

            private void TickRespawns(double dt)
            {
                foreach (var f in Layout.FuelPickups)
                {
                    if (!f.Taken) continue;
                    f.RespawnT -= dt;
                    if (f.RespawnT <= 0.0) f.Taken = false;
                }
            }

            private void Tow(double dt, List<Event> events)
            {
                var dx = Layout.PadX - X;
                var dy = Layout.PadY - Y;
                var dist = Hypot(dx, dy);
                var stepM = Tune.TowSpeed * dt;
                if (dist <= stepM || dist < 0.05)
                {
                    X = Layout.PadX;
                    Y = Layout.PadY;
                    events.Add(new Event { Type = "towed" });
                    EndRun("fuel", events);
                }
                else
                {
                    X += (dx / dist) * stepM;
                    Y += (dy / dist) * stepM;
                }
            }

            private void SlewTilt(Input input, double dt)
            {
                var target = Math.Max(-1.0, Math.Min(1.0, (double)input.TiltDir)) * Tune.TiltMaxRad;
                var dTilt = target - Tilt;
                var maxSlew = Tune.TiltRate * dt;
                Tilt += Math.Abs(dTilt) <= maxSlew ? dTilt : Math.Sign(dTilt) * maxSlew;
            }

            private void Fly(bool thrusting, double dt, List<Event> events)
            {
                if (LandedOn.HasValue && thrusting)
                {
                    DepartedFrom = LandedOn;
                    LandedOn = null;
                    events.Add(new Event { Type = "liftoff" });
                }
                var ax = 0.0;
                var ay = -Tune.Gravity;
                if (thrusting)
                {
                    ax += Math.Sin(Tilt) * Tune.ThrustAccel;
                    ay += Math.Cos(Tilt) * Tune.ThrustAccel;
                    Fuel = Math.Max(0.0, Fuel - Tune.FuelBurnPerSec * dt);
                }
                if (WindPhase == "gust") ax += WindDir * Tune.WindAccel;
                Vx += ax * dt;
                Vy += ay * dt;
                var prevY = Y;
                X += Vx * dt;
                Y += Vy * dt;
                ClampWalls();
                CollectFuel(events);
                ReleaseDeparted();
                if (Vy <= 0.0) TryLand(prevY, events);
            }

            private void ClampWalls()
            {
                var wallX = Tune.WorldHalfW - 0.4;
                if (X < -wallX)
                {
                    X = -wallX;
                    Vx = Math.Abs(Vx) * Tune.WallRestitution;
                }
                else if (X > wallX)
                {
                    X = wallX;
                    Vx = -Math.Abs(Vx) * Tune.WallRestitution;
                }
                if (Y > Tune.CeilingY)
                {
                    Y = Tune.CeilingY;
                    Vy = Math.Min(0.0, Vy);
                }
            }

            private void CollectFuel(List<Event> events)
            {
                for (var i = 0; i < Layout.FuelPickups.Count; i++)
                {
                    var f = Layout.FuelPickups[i];
                    if (f.Taken) continue;
                    var d = Hypot(f.X - X, f.Y - Y);
                    if (d > Tune.FuelPickupRadius) continue;
                    f.Taken = true;
                    f.RespawnT = Tune.FuelRespawnSec;
                    var thin = 1.0;
                    if (Tune.Endless) thin = Math.Max(0.0, 1.0 - Tune.EndlessThinPerRescue * Rescued);
                    var amount = Tune.FuelPickupAmount * thin;
                    Fuel = Math.Min(Tune.FuelMax, Fuel + amount);
                    FuelLowFired = false;
                    events.Add(new Event { Type = "fuelPickup", Index = i, Amount = amount });
                }
            }

            private void ReleaseDeparted()
            {
                if (!DepartedFrom.HasValue) return;
                double sx, sy, shw;
                if (DepartedFrom.Value == PadId) { sx = Layout.PadX; sy = Layout.PadY; shw = Layout.PadHalfW; }
                else { var p = Layout.Platforms[DepartedFrom.Value]; sx = p.X; sy = p.Y; shw = p.HalfW; }
                var cleared = Y >= sy + Tune.DepartClearM;
                if (cleared || Math.Abs(X - sx) > shw) DepartedFrom = null;
            }

            private void TryLand(double prevY, List<Event> events)
            {
                foreach (var s in Surfaces())
                {
                    if (DepartedFrom.HasValue && DepartedFrom.Value == s.Id) continue;
                    var overlaps = Math.Abs(X - s.X) <= s.HalfW;
                    if (!(prevY >= s.Y && Y <= s.Y && overlaps)) continue;
                    var vyAbs = Math.Abs(Vy);
                    var kind = ClassifyLanding(vyAbs, Tune);
                    if (kind == "hard") Bounce(s, vyAbs, events);
                    else TouchDown(s, vyAbs, kind, events);
                    return;
                }
            }

            private void Bounce(Surface s, double vyAbs, List<Event> events)
            {
                Y = s.Y + 0.02;
                Vy = vyAbs * Tune.BounceRestitution;
                Vx *= 0.6;
                HardLandings += 1;
                Fuel = Math.Max(0.0, Fuel - Tune.HardFuelPenalty);
                events.Add(new Event { Type = "hardLanding", Vy = vyAbs, Where = s.Id });
            }

            private void TouchDown(Surface s, double vyAbs, string kind, List<Event> events)
            {
                Y = s.Y;
                Vx = 0.0;
                Vy = 0.0;
                LandedOn = s.Id;
                DepartedFrom = null;
                var rescueWork = false;
                if (s.Id >= 0 && Layout.Platforms[s.Id].Bunny && !Carrying)
                {
                    Layout.Platforms[s.Id].Bunny = false;
                    Carrying = true;
                    rescueWork = true;
                }
                else if (s.Id == PadId && Carrying)
                {
                    Carrying = false;
                    Rescued += 1;
                    rescueWork = true;
                }
                var eligible = rescueWork || !(LastLandedOn.HasValue && LastLandedOn.Value == s.Id);
                LastLandedOn = s.Id;
                if (kind == "soft" && eligible) SoftLandings += 1;
                events.Add(new Event { Type = "landing", Where = s.Id, Vy = vyAbs, Kind = kind, BonusEligible = kind == "soft" && eligible });
                if (!rescueWork) return;
                if (Carrying)
                {
                    events.Add(new Event { Type = "bunnyPickup", Where = s.Id });
                    return;
                }
                events.Add(new Event { Type = "rescue", Count = Rescued });
                if (Tune.Endless) RearmBunnies(events);
                else if (Rescued >= Tune.PlatformCount) EndRun("complete", events);
            }

            private void RearmBunnies(List<Event> events)
            {
                foreach (var p in Layout.Platforms) if (p.Bunny) return;
                foreach (var p in Layout.Platforms) p.Bunny = true;
                events.Add(new Event { Type = "bunnyRespawn" });
            }

            private void TickWind(double dt, List<Event> events)
            {
                if (Rescued < Tune.WindFromRescues || Ended) return;
                if (WindPhase == "idle")
                {
                    var due = Elapsed >= WindNextAt;
                    if (due && !LandedOn.HasValue)
                    {
                        WindPhase = "telegraph";
                        WindDir = _rng() < 0.5 ? -1 : 1;
                        WindT = 0.0;
                        events.Add(new Event { Type = "windTelegraph", Dir = WindDir });
                    }
                    return;
                }
                WindT += dt;
                if (WindPhase == "telegraph")
                {
                    if (WindT >= Tune.WindTelegraphSec)
                    {
                        WindPhase = "gust";
                        WindT = 0.0;
                        events.Add(new Event { Type = "windGust", Dir = WindDir });
                    }
                }
                else if (WindT >= Tune.WindGustSec)
                {
                    WindPhase = "idle";
                    var span = Tune.WindEveryMaxSec - Tune.WindEveryMinSec;
                    WindNextAt = Elapsed + Tune.WindEveryMinSec + _rng() * span;
                }
            }

            private void CheckFuel(List<Event> events)
            {
                if (Fuel <= 0.0 && !Towing && !Ended)
                {
                    if (LandedOn.HasValue && LandedOn.Value == PadId) EndRun("fuel", events);
                    else
                    {
                        Towing = true;
                        LandedOn = null;
                        events.Add(new Event { Type = "outOfFuel" });
                    }
                }
                else if (!FuelLowFired && Fuel > 0.0 && Fuel <= 20.0)
                {
                    FuelLowFired = true;
                    events.Add(new Event { Type = "fuelLow" });
                }
            }
        }

        // ── PD autopilot ───────────────────────────────────────────────────

        public sealed class Bot
        {
            public Tune Tune;
            private bool _refueling;

            public Bot(Tune tune) { Tune = tune; }

            public Input Control(Engine e)
            {
                if (e.Ended || e.Towing) return new Input();
                if (e.LandedOn.HasValue) return ParkedCommand(e);
                var (tx, ty, isCanister) = PickTarget(e);
                var dx = tx - e.X;
                var heightAbove = e.Y - ty;
                var aligned = !isCanister && Math.Abs(dx) <= Tune.BotAlignXM && Math.Abs(e.Vx) <= Tune.BotAlignVx;
                var (spVy, spVx) = aligned && heightAbove >= -0.1 ? DescendSetpoint(dx, heightAbove) : AlignSetpoint(e, dx, ty, isCanister);
                var dvx = spVx - e.Vx;
                var vy = e.Vy;
                var thrust = vy < spVy || (Math.Abs(dvx) > 1.2 && vy < Tune.BotMaxRise);
                var deadband = Tune.BotTiltDeadband;
                var tiltDir = dvx > deadband ? 1 : (dvx < -deadband ? -1 : 0);
                return new Input { Thrust = thrust, TiltDir = tiltDir };
            }

            private Input ParkedCommand(Engine e)
            {
                var bunniesLeft = false;
                foreach (var p in e.Layout.Platforms) if (p.Bunny) { bunniesLeft = true; break; }
                var shouldGo = e.Carrying ? !(e.LandedOn.HasValue && e.LandedOn.Value == PadId) : bunniesLeft;
                return new Input { Thrust = shouldGo && e.Fuel > 0.0, TiltDir = 0 };
            }

            private (double x, double y, bool canister) PickTarget(Engine e)
            {
                var tx = e.Layout.PadX;
                var ty = e.Layout.PadY;
                if (!e.Carrying)
                {
                    var bestD = double.PositiveInfinity;
                    foreach (var p in e.Layout.Platforms)
                    {
                        if (!p.Bunny) continue;
                        var d = Math.Abs(p.X - e.X) + Math.Abs(p.Y - e.Y) * 0.6;
                        if (d < bestD) { bestD = d; tx = p.X; ty = p.Y; }
                    }
                }
                var finalDescent = Math.Abs(tx - e.X) <= Tune.BotAlignXM * 2.0 && e.Y - ty < Tune.BotRefuelSkipBelowM;
                var fuel = e.Fuel;
                if (!_refueling && fuel < Tune.BotRefuelEnter) _refueling = true;
                else if (_refueling && fuel >= Tune.BotRefuelExit) _refueling = false;
                if (!_refueling || finalDescent) return (tx, ty, false);
                var bestDist = double.PositiveInfinity;
                FuelPickup best = null;
                foreach (var f in e.Layout.FuelPickups)
                {
                    if (f.Taken) continue;
                    var d = Hypot(f.X - e.X, f.Y - e.Y);
                    if (d < bestDist) { bestDist = d; best = f; }
                }
                if (best == null || bestDist >= Tune.BotRefuelRangeM) return (tx, ty, false);
                return (best.X, best.Y, true);
            }

            private (double vy, double vx) AlignSetpoint(Engine e, double dx, double ty, bool isCanister)
            {
                var yDes = isCanister ? ty : Math.Max(ty + Tune.BotCruiseClearanceM, e.Y - 1.4);
                var vyDes = Math.Max(-1.1, Math.Min(Tune.BotMaxRise, (yDes - e.Y) * Tune.BotVyGain));
                var brake = Math.Sqrt(2.0 * Tune.BotLatAccelEff * Math.Abs(dx)) * 0.85;
                var vxDes = Math.Sign(dx) * Math.Min(Math.Min(Tune.BotMaxVx, brake), Math.Abs(dx) * Tune.BotVxGain + 0.15);
                return (vyDes, vxDes);
            }

            private (double vy, double vx) DescendSetpoint(double dx, double heightAbove)
            {
                var drop = Math.Min(Tune.BotMaxDescend, 0.45 * heightAbove + 0.25);
                var vyDes = heightAbove < Tune.BotFlareBelowM ? -Tune.BotSoftDescend : -drop;
                var limit = Tune.BotDescendVx;
                return (vyDes, Math.Max(-limit, Math.Min(limit, dx * 2.0)));
            }
        }

        public static JObject SimulateRound(int seed, Tune tune, double dt = 1.0 / 60.0)
        {
            var guard = (int)Math.Ceiling((tune.DurationSec + 30.0) / dt);
            return Run(seed, tune, dt, guard);
        }

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1, double maxSec = 1800.0)
        {
            var tune = ApplyDifficulty(Rocket, mode);
            var dt = 1.0 / 60.0;
            return Run(seed, tune, dt, (int)Math.Ceiling(maxSec / dt));
        }

        private static JObject Run(int seed, Tune tune, double dt, int guard)
        {
            var rng = new GoobyRng(seed);
            var engine = new Engine(() => rng.Next(), tune);
            var bot = new Bot(tune);
            var left = guard;
            while (!engine.Ended && left > 0)
            {
                engine.Step(bot.Control(engine), dt);
                left -= 1;
            }
            return new JObject
            {
                ["score"] = RoundScore(engine.Rescued, engine.Fuel, engine.SoftLandings, tune),
                ["rescued"] = engine.Rescued, ["softLandings"] = engine.SoftLandings, ["hardLandings"] = engine.HardLandings,
                ["fuelLeft"] = engine.Fuel, ["elapsed"] = engine.Elapsed, ["endReason"] = engine.EndReason,
            };
        }
    }
}
