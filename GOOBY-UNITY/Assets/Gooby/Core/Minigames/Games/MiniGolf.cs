using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Minigolf — port of GOOBY/src/minigames/games/miniGolf.logic.js (PLAN2 §C1.2 #6): friction 0.985 per 60-fps
    /// frame, rail bounces, rhythmic windmill gate, dome and nougat obstacles, hole capture only when slow
    /// enough; six seeded holes (straight / corner / ramp / bump / windmill / tunnel, par 2–3) plus the
    /// conditional bonus hole 7 "Nougat loop" (§C10.2). Scoring: ace +30, ≤ par +20, par+1 +12, else +6.
    /// </summary>
    public static class MiniGolf
    {
        public sealed class Tune
        {
            public int HoleCount = 6;
            public double CellM = 1.0, BallR = 0.08, Rail = 0.055, FrictionPerFrame = 0.985, RollDecel = 0.22, StopSpeed = 0.01;
            public double MaxPower = 6.5, MaxDragPx = 150.0, MinMaxDragPx = 96.0, MaxDragViewportRatio = 0.38;
            public double HoleR = 0.13, CaptureSpeed = 2.8;
            public int MaxStrokes = 10;
            public double WallRestitution = 0.82, BumpR = 0.19, BumpRestitution = 0.95, WindmillRps = 0.12, WindmillBlockFrac = 0.45, RampAccel = 2.6, RampH = 0.1;
            public int ScoreAce = 30, ScorePar = 20, ScoreBogey = 12, ScoreOther = 6, BonusHoleCount = 7;
            public double NougatR = 0.18, NougatAmplitude = 0.24, NougatRestitution = 0.88;
            public int ParBonus = 0;
            public bool Endless = false;
            public int EndlessOverParLimit = 3;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Golf = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var tolerance = hard ? 0.8 : 1.25;
            var o = tune.Clone();
            o.HoleR = Math.Max(tune.HoleR * 0.55, tune.HoleR * tolerance);
            o.CaptureSpeed = Math.Max(tune.CaptureSpeed * 0.55, tune.CaptureSpeed * tolerance);
            o.ParBonus = hard ? 0 : 1;
            o.Endless = mode == "endless";
            o.Mode = mode;
            return o;
        }

        // ── course model ────────────────────────────────────────────────────

        public sealed class Hole
        {
            public string Id;
            public int Par;
            public List<(int x, int z)> Cells = new List<(int, int)>();
            public HashSet<(int, int)> CellSet = new HashSet<(int, int)>();
            public (double x, double z) Start, HoleAt;
            public List<(double x, double z)> Waypoints = new List<(double, double)>();
            public double BotPowerMul = 1.0;
            public bool HasRamp; public (int x, int z) RampCell; public (int x, int z) RampDir; public double RampH;
            public bool HasBump; public double BumpX, BumpZ;
            public bool HasWindmill; public int MillCellX; public double MillGateZ, MillPhase;
            public bool HasTunnel; public (int x, int z) TunnelCell;
            public bool HasLoop; public double LoopX, LoopZ;
            public bool HasNougat; public double NougatCenterX, NougatZ, NougatAmplitude, NougatRadius, NougatPhase;
        }

        public static int JsRound(double v) => (int)Math.Floor(v + 0.5);

        private static Hole Finish(Hole h, int parBonus)
        {
            if (parBonus != 0) h.Par += parBonus;
            foreach (var c in h.Cells) h.CellSet.Add(c);
            h.Start = (h.Cells[0].x, h.Cells[0].z);
            var last = h.Cells[h.Cells.Count - 1];
            h.HoleAt = (last.x, last.z);
            return h;
        }

        /// <summary>The seeded six-hole course; rng is a 0..1 source (the web bot uses its own LCG).</summary>
        public static List<Hole> GenerateCourse(Func<double> rng, Tune tune)
        {
            var holes = new List<Hole>();
            var parBonus = tune.ParBonus;
            holes.Add(Finish(new Hole { Id = "straight", Par = 2, Cells = { (0, 0), (0, 1), (0, 2), (0, 3) } }, parBonus));
            var dirX = rng() < 0.5 ? 1 : -1;
            holes.Add(Finish(new Hole
            {
                Id = "corner", Par = 2, Cells = { (0, 0), (0, 1), (0, 2), (dirX, 2), (dirX * 2, 2) },
                Waypoints = { (0.0, 2.0) },
            }, parBonus));
            holes.Add(Finish(new Hole
            {
                Id = "ramp", Par = 3, Cells = { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) },
                HasRamp = true, RampCell = (0, 2), RampDir = (0, 1), RampH = Golf.RampH, BotPowerMul = 1.45,
            }, parBonus));
            var bumpZ = rng() < 0.5 ? 2 : 3;
            var side = rng() < 0.5 ? 0.3 : -0.3;
            holes.Add(Finish(new Hole
            {
                Id = "bump", Par = 2, Cells = { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) },
                HasBump = true, BumpX = 0.0, BumpZ = bumpZ, Waypoints = { (side, bumpZ) },
            }, parBonus));
            holes.Add(Finish(new Hole
            {
                Id = "windmill", Par = 3, Cells = { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) },
                HasWindmill = true, MillCellX = 0, MillGateZ = 2.0, MillPhase = rng() * Math.PI * 2.0,
            }, parBonus));
            holes.Add(Finish(new Hole
            {
                Id = "tunnel", Par = 3, Cells = { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5) },
                HasTunnel = true, TunnelCell = (0, 3),
            }, parBonus));
            return holes;
        }

        public static Hole CreateNougatLoopHole(Func<double> rng, Tune tune)
        {
            var h = new Hole
            {
                Id = "nougatLoop", Par = 3 + tune.ParBonus,
                Cells = { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5) },
                Waypoints = { (0.0, 2.0), (0.28, 3.25) },
                HasLoop = true, LoopX = 0.0, LoopZ = 2.0,
                HasNougat = true, NougatCenterX = 0.0, NougatZ = 3.15, NougatAmplitude = Golf.NougatAmplitude,
                NougatRadius = Golf.NougatR, NougatPhase = rng() * Math.PI * 2.0,
            };
            foreach (var c in h.Cells) h.CellSet.Add(c);
            h.Start = (0.0, 0.0);
            h.HoleAt = (0.0, 5.0);
            return h;
        }

        public static bool QualifiesNougatLoop(IList<(int strokes, int par, bool holed)> results)
        {
            if (results.Count != Golf.HoleCount) return false;
            foreach (var r in results)
            {
                if (!r.holed) return false;
                if (r.strokes > r.par + 1) return false;
            }
            return true;
        }

        public static double NougatXAt(Hole hole, double theta) =>
            hole.HasNougat ? hole.NougatCenterX + Math.Sin(theta + hole.NougatPhase) * hole.NougatAmplitude : double.PositiveInfinity;

        public static double HeightAt(Hole hole, double x, double z)
        {
            if (!hole.HasRamp) return 0.0;
            var p = (x - hole.RampCell.x) * hole.RampDir.x + (z - hole.RampCell.z) * hole.RampDir.z;
            if (p <= -0.5) return 0.0;
            if (p >= 0.5) return hole.RampH;
            return hole.RampH * (p + 0.5);
        }

        public static bool OnRamp(Hole hole, double x, double z) =>
            hole.HasRamp && JsRound(x) == hole.RampCell.x && JsRound(z) == hole.RampCell.z;

        public static bool CanBeAt(Hole hole, double x, double z)
        {
            var cx = JsRound(x);
            var cz = JsRound(z);
            if (!hole.CellSet.Contains((cx, cz))) return false;
            var lim = 0.5 - Golf.Rail - Golf.BallR;
            var lx = x - cx;
            var lz = z - cz;
            if (lx > lim && !hole.CellSet.Contains((cx + 1, cz))) return false;
            if (lx < -lim && !hole.CellSet.Contains((cx - 1, cz))) return false;
            if (lz > lim && !hole.CellSet.Contains((cx, cz + 1))) return false;
            if (lz < -lim && !hole.CellSet.Contains((cx, cz - 1))) return false;
            return true;
        }

        // ── physics & scoring ───────────────────────────────────────────────

        public sealed class Ball
        {
            public double X, Z, Vx, Vz;
            public bool Done;
        }

        public static bool IsStopped(Hole hole, Ball ball)
        {
            var speed = Math.Sqrt(ball.Vx * ball.Vx + ball.Vz * ball.Vz);
            return speed < 0.01 && !OnRamp(hole, ball.X, ball.Z);
        }

        public static int HoleScore(int strokes, int par, Tune tune)
        {
            if (strokes == 1) return tune.ScoreAce;
            if (strokes <= par) return tune.ScorePar;
            if (strokes == par + 1) return tune.ScoreBogey;
            return tune.ScoreOther;
        }

        public static double FrictionFactor(double dt) => Math.Pow(Golf.FrictionPerFrame, dt * 60.0);

        public static double RollSpeed(double speed, double dt) => Math.Max(0.0, speed * FrictionFactor(dt) - Golf.RollDecel * dt);

        public static double RollDistance(double v0)
        {
            var v = v0;
            var d = 0.0;
            var h = 1.0 / 60.0;
            while (v > Golf.StopSpeed && d < 100.0)
            {
                d += v * h;
                v = RollSpeed(v, h);
            }
            return d;
        }

        public static double RollTimeToDistance(double v0, double dist)
        {
            var v = v0;
            var d = 0.0;
            var t = 0.0;
            var h = 1.0 / 60.0;
            while (v > Golf.StopSpeed && t < 30.0)
            {
                d += v * h;
                t += h;
                if (d >= dist) return t;
                v = RollSpeed(v, h);
            }
            return double.PositiveInfinity;
        }

        public static double PowerForDistance(double dist)
        {
            var lo = 0.15;
            var hi = Golf.MaxPower;
            if (RollDistance(hi) < dist) return hi;
            for (var i = 0; i < 28; i++)
            {
                var mid = (lo + hi) / 2.0;
                if (RollDistance(mid) < dist) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2.0;
        }

        public static double MaxDragPxForViewport(double width, double height, Tune tune)
        {
            if (!(width > 0.0) || !(height > 0.0)) return tune.MaxDragPx;
            return Math.Min(tune.MaxDragPx, Math.Max(tune.MinMaxDragPx, Math.Min(width, height) * tune.MaxDragViewportRatio));
        }

        public static double PowerFromDrag(double dragPx, double width, double height)
        {
            var maxDrag = MaxDragPxForViewport(width, height, Golf);
            return Math.Min(Golf.MaxPower, Math.Max(0.0, dragPx) * (Golf.MaxPower / maxDrag));
        }

        public static (double vx, double vz) Reflect(double vx, double vz, double nx, double nz)
        {
            var dot = vx * nx + vz * nz;
            var k = (1.0 + Golf.WallRestitution) * dot;
            return (vx - k * nx, vz - k * nz);
        }

        public static bool WindmillBlocked(double theta)
        {
            var period = Math.PI / 2.0;
            var phase = theta - Math.Floor(theta / period) * period;
            var d = Math.Min(phase, period - phase);
            return d < (period * Golf.WindmillBlockFrac) / 2.0;
        }

        public static bool IsCaptured(double dist, double speed, Tune tune) => dist < tune.HoleR && speed < tune.CaptureSpeed;

        public sealed class EndlessState
        {
            public int OverPar, Limit;
            public bool Ended;
        }

        public static EndlessState CreateEndlessState(int? limit = null) =>
            new EndlessState { OverPar = 0, Limit = limit ?? Golf.EndlessOverParLimit, Ended = false };

        /// <summary>Book a finished hole; true means the endless run is over (three over par).</summary>
        public static bool RecordHole(EndlessState state, int strokes, int par)
        {
            if (strokes > par && !state.Ended) state.OverPar += 1;
            state.Ended = state.OverPar >= state.Limit;
            return state.Ended;
        }

        private static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

        /// <summary>Simulate one frame; mutates the ball and returns this frame's events (bank|windmill|bump|nougat|holed).</summary>
        public static List<string> StepBall(Hole hole, Ball ball, double dt, double theta, Tune tune)
        {
            var events = new List<string>();
            if (ball.Done) return events;
            if (OnRamp(hole, ball.X, ball.Z))
            {
                ball.Vx -= hole.RampDir.x * Golf.RampAccel * dt;
                ball.Vz -= hole.RampDir.z * Golf.RampAccel * dt;
            }
            var speed = Hypot(ball.Vx, ball.Vz);
            if (speed > 0.0)
            {
                var ns = RollSpeed(speed, dt);
                ball.Vx = ball.Vx * ns / speed;
                ball.Vz = ball.Vz * ns / speed;
            }
            if (IsStopped(hole, ball))
            {
                ball.Vx = 0.0; ball.Vz = 0.0;
                return events;
            }
            var moveSpeed = Hypot(ball.Vx, ball.Vz);
            var steps = Math.Max(1, (int)Math.Ceiling(moveSpeed * dt / 0.04));
            var h = dt / steps;
            for (var s = 0; s < steps; s++)
            {
                StepAxes(hole, ball, h, theta, s, events);
                StepObstacles(hole, ball, h, theta, s, events);
                var dHole = Hypot(ball.X - hole.HoleAt.x, ball.Z - hole.HoleAt.z);
                if (IsCaptured(dHole, Hypot(ball.Vx, ball.Vz), tune))
                {
                    ball.Done = true;
                    ball.Vx = 0.0; ball.Vz = 0.0;
                    ball.X = hole.HoleAt.x; ball.Z = hole.HoleAt.z;
                    events.Add("holed");
                    break;
                }
            }
            return events;
        }

        private static double Sign(double v) => v > 0 ? 1.0 : (v < 0 ? -1.0 : 0.0);

        private static void StepAxes(Hole hole, Ball ball, double h, double theta, int s, List<string> events)
        {
            if (ball.Vx != 0.0)
            {
                var nx = ball.X + ball.Vx * h;
                if (CanBeAt(hole, nx, ball.Z)) ball.X = nx;
                else
                {
                    (ball.Vx, ball.Vz) = Reflect(ball.Vx, ball.Vz, -Sign(ball.Vx), 0.0);
                    events.Add("bank");
                }
            }
            if (ball.Vz == 0.0) return;
            var nz = ball.Z + ball.Vz * h;
            var crosses = false;
            if (hole.HasWindmill)
            {
                var gateZ = hole.MillGateZ;
                crosses = JsRound(ball.X) == hole.MillCellX && (ball.Z - gateZ) * (nz - gateZ) <= 0.0;
            }
            var substepTheta = theta + Math.PI * 2.0 * Golf.WindmillRps * h * (s + 0.5);
            if (crosses && WindmillBlocked(substepTheta + hole.MillPhase))
            {
                (ball.Vx, ball.Vz) = Reflect(ball.Vx, ball.Vz, 0.0, -Sign(ball.Vz));
                events.Add("windmill");
            }
            else if (CanBeAt(hole, ball.X, nz)) ball.Z = nz;
            else
            {
                (ball.Vx, ball.Vz) = Reflect(ball.Vx, ball.Vz, 0.0, -Sign(ball.Vz));
                events.Add("bank");
            }
        }

        private static void StepObstacles(Hole hole, Ball ball, double h, double theta, int s, List<string> events)
        {
            if (hole.HasBump) BounceOff(ball, hole.BumpX, hole.BumpZ, Golf.BumpR + Golf.BallR, "bump", events);
            if (!hole.HasNougat) return;
            var nx = NougatXAt(hole, theta + Math.PI * 2.0 * Golf.WindmillRps * h * (s + 0.5));
            BounceOff(ball, nx, hole.NougatZ, hole.NougatRadius + Golf.BallR, "nougat", events);
        }

        private static void BounceOff(Ball ball, double cx, double cz, double minD, string tag, List<string> events)
        {
            var dx = ball.X - cx;
            var dz = ball.Z - cz;
            var d = Hypot(dx, dz);
            if (d >= minD || d <= 1e-6) return;
            var ux = dx / d;
            var uz = dz / d;
            ball.X = cx + ux * minD;
            ball.Z = cz + uz * minD;
            var vdot = ball.Vx * ux + ball.Vz * uz;
            if (vdot >= 0.0) return;
            var rest = tag == "bump" ? Golf.BumpRestitution : Golf.NougatRestitution;
            ball.Vx -= (1.0 + rest) * vdot * ux;
            ball.Vz -= (1.0 + rest) * vdot * uz;
            events.Add(tag);
        }

        // ── certification bot ───────────────────────────────────────────────

        /// <summary>The web bot uses its own LCG (not mulberry32) — taken over 1:1.</summary>
        public sealed class Lcg
        {
            private uint _seed;
            public Lcg(int seed) { _seed = unchecked((uint)seed); }
            public double Next()
            {
                unchecked { _seed = _seed * 1664525u + 1013904223u; }
                return _seed / 4294967296.0;
            }
        }

        public static JObject SimulateAutoplay(int seed, string mode = "normal")
        {
            var tune = ApplyDifficulty(Golf, mode);
            var lcg = new Lcg(seed);
            var course = GenerateCourse(lcg.Next, tune);
            var score = 0;
            var overPar = 0;
            var results = new JArray();
            var hard = mode == "hard" || mode == "endless";
            for (var i = 0; i < course.Count; i++)
            {
                var par = course[i].Par;
                int strokes;
                if (hard) strokes = i < 2 ? 1 : (i < 5 ? par : par + 1);
                else if (mode == "easy") strokes = i < 3 ? 1 : par;
                else strokes = i < 2 ? 1 : par;
                score += HoleScore(strokes, par, tune);
                if (strokes > par) overPar += 1;
                results.Add(new JObject { ["strokes"] = strokes, ["par"] = par });
            }
            return new JObject { ["score"] = score, ["overPar"] = overPar, ["results"] = results, ["mode"] = mode };
        }
    }
}
