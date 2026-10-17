using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Snail Mail — port of snail_mail_logic.gd (web snailMail verbatim): level generator (houses/puddles/flowers),
    /// Catmull-Rom track + arc-length resampling, eased arc speed, auto-router (puddle detours) and the release bot.
    /// </summary>
    public static class SnailMail
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0, FieldHalfW = 2.2, FieldYMin = -2.8, FieldYMax = 2.8, PostX = 0.0, PostY = -2.35;
            public double[] HouseSlotsX = { -1.5, 0.0, 1.5 };
            public double HouseJitterX = 0.22, HouseYMin = 1.75, HouseYMax = 2.4, DoorOffsetY = 0.32;
            public double Speed = 2.1, SpeedEaseDist = 0.45, SpeedMinFrac = 0.4, SnailRadius = 0.16, ResampleStep = 0.22, MinInputSpacing = 0.12;
            public double StartRadius = 0.8, DeliverRadius = 0.55;
            public int MaxInputPoints = 160;
            public double PuddleRMin = 0.34, PuddleRMax = 0.5, PuddleEdge = 1.0;
            public int PuddlesStart = 2, PuddlesMax = 5, PuddleRampEvery = 2;
            public double PuddleYMin = -1.7, PuddleYMax = 1.15, PuddleGap = 0.45, PuddleKeepout = 0.85;
            public int FlowersPerRound = 3;
            public double FlowerPickRadius = 0.42, FlowerLaneOffset = 0.9, FlowerMinSpacing = 0.35;
            public int DeliverPts = 4, DryBonus = 2, FlowerPts = 1;
            public double RetreatSec = 2.0, RoundBeatSec = 0.55;
            public bool Endless = false;
            public int EndlessMaxSplashes = 3, GenMaxTries = 40;
            public double RouteClearance = 0.14, RouteDetourPad = 0.34;
            public int RouteMaxPasses = 6;
            public double RouteSampleStep = 0.1, BotDrawSec = 1.1, BotWetRate = 0.08, BotTimeCapSec = 600.0;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Snail = new Tune();
        public const int ArcSamplesPerSeg = 32;

        public struct Pt { public double X, Y; public Pt(double x, double y) { X = x; Y = y; } }
        public sealed class House { public double X, Y; public string Kind; }
        public sealed class Puddle { public double X, Y, R; }

        public sealed class Level
        {
            public Pt Post;
            public List<House> Houses;
            public int TargetIdx, Round;
            public List<Puddle> Puddles;
            public List<Pt> Flowers;
        }

        public sealed class SmoothPath
        {
            public List<Pt> Pts;
            public List<double> Cum;
            public double Length;
        }

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double speedMult, durationMult, puddleEdge, drawSec, botWet;
            switch (mode)
            {
                case "easy": speedMult = 0.85; durationMult = 1.2; puddleEdge = 0.85; drawSec = 1.15; botWet = 0.03; break;
                case "hard":
                case "endless": speedMult = 1.2; durationMult = 1.0; puddleEdge = 1.12; drawSec = 0.95; botWet = 0.3; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * durationMult;
            o.Speed = tune.Speed * speedMult;
            o.PuddleEdge = puddleEdge;
            o.BotDrawSec = drawSec;
            o.BotWetRate = botWet;
            o.Endless = mode == "endless";
            o.Mode = mode;
            return o;
        }

        public static int PuddlesForRound(int roundIndex, Tune tune)
        {
            var extra = (int)Math.Floor(Math.Max(0, roundIndex) / (double)tune.PuddleRampEvery);
            return Math.Min(tune.PuddlesMax, tune.PuddlesStart + extra);
        }

        public static Pt DoorOf(House h, Tune tune) => new Pt(h.X, h.Y - tune.DoorOffsetY);

        public static double PuddleEffR(Puddle p, Tune tune) => p.R * tune.PuddleEdge + tune.SnailRadius;

        public static int PuddleHitAt(double x, double y, List<Puddle> puddles, Tune tune)
        {
            for (var i = 0; i < puddles.Count; i++)
                if (Hypot(x - puddles[i].X, y - puddles[i].Y) < PuddleEffR(puddles[i], tune)) return i;
            return -1;
        }

        public static bool PathClear(List<Pt> pts, List<Puddle> puddles, Tune tune)
        {
            foreach (var pt in pts) if (PuddleHitAt(pt.X, pt.Y, puddles, tune) >= 0) return false;
            return true;
        }

        private static double Dist3(double[] a, double[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static double[] CatmullRom(double[] p0, double[] p1, double[] p2, double[] p3, double u)
        {
            var u2 = u * u;
            var u3 = u2 * u;
            var o = new double[3];
            for (var i = 0; i < 3; i++)
                o[i] = 0.5 * (2.0 * p1[i] + (-p0[i] + p2[i]) * u + (2.0 * p0[i] - 5.0 * p1[i] + 4.0 * p2[i] - p3[i]) * u2 + (-p0[i] + 3.0 * p1[i] - 3.0 * p2[i] + p3[i]) * u3);
            return o;
        }

        private sealed class Track
        {
            public double Length;
            public List<double[]> Waypoints;
            public List<double> S = new List<double>();
            public List<double[]> Pos = new List<double[]>();
            public List<int> Seg = new List<int>();
            public List<double> U = new List<double>();
        }

        private static double[] Wp(List<double[]> pts, int i) => pts[Math.Max(0, Math.Min(pts.Count - 1, i))];

        private static Track BuildTrack(List<double[]> waypoints)
        {
            var n = waypoints.Count;
            var segCount = n - 1;
            var t = new Track { Waypoints = waypoints };
            t.S.Add(0.0);
            t.Pos.Add(CatmullRom(Wp(waypoints, -1), Wp(waypoints, 0), Wp(waypoints, 1), Wp(waypoints, 2), 0.0));
            t.Seg.Add(0);
            t.U.Add(0.0);
            var acc = 0.0;
            var prev = t.Pos[0];
            for (var seg = 0; seg < segCount; seg++)
            for (var k = 1; k <= ArcSamplesPerSeg; k++)
            {
                var u = k / (double)ArcSamplesPerSeg;
                var p = CatmullRom(Wp(waypoints, seg - 1), Wp(waypoints, seg), Wp(waypoints, seg + 1), Wp(waypoints, seg + 2), u);
                acc += Dist3(prev, p);
                t.S.Add(acc);
                t.Pos.Add(p);
                t.Seg.Add(seg);
                t.U.Add(u);
                prev = p;
            }
            t.Length = acc;
            return t;
        }

        private static double[] TrackPosAt(Track track, double s)
        {
            var c = Math.Max(0.0, Math.Min(track.Length, s));
            var i = SampleIndex(track.S, c);
            var span = track.S[i + 1] - track.S[i];
            var f = span > 0.0 ? (c - track.S[i]) / span : 0.0;
            var seg = track.Seg[i];
            var u0 = track.U[i];
            var u1 = track.Seg[i + 1] == seg ? track.U[i + 1] : 1.0;
            var u = u0 + (u1 - u0) * f;
            return CatmullRom(Wp(track.Waypoints, seg - 1), Wp(track.Waypoints, seg), Wp(track.Waypoints, seg + 1), Wp(track.Waypoints, seg + 2), u);
        }

        public static SmoothPath Smooth(List<Pt> rawPts, Tune tune)
        {
            var waypoints = new List<double[]>();
            var capped = rawPts.Count > tune.MaxInputPoints ? rawPts.GetRange(0, tune.MaxInputPoints) : rawPts;
            foreach (var p in capped)
            {
                var v = new[] { p.X, p.Y, 0.0 };
                if (waypoints.Count == 0 || Dist3(waypoints[waypoints.Count - 1], v) >= tune.MinInputSpacing) waypoints.Add(v);
            }
            if (capped.Count >= 2)
            {
                var last = capped[capped.Count - 1];
                var tip = new[] { last.X, last.Y, 0.0 };
                var tail = waypoints[waypoints.Count - 1];
                var d = Dist3(tail, tip);
                if (d > 1e-6)
                {
                    if (d < tune.MinInputSpacing && waypoints.Count > 1) waypoints[waypoints.Count - 1] = tip;
                    else waypoints.Add(tip);
                }
            }
            if (waypoints.Count < 2) return null;
            var track = BuildTrack(waypoints);
            var n = Math.Max(2, (int)Math.Ceiling(track.Length / tune.ResampleStep) + 1);
            var pts = new List<Pt>();
            var cum = new List<double>();
            double[] prev = null;
            var acc = 0.0;
            for (var i = 0; i < n; i++)
            {
                var pos = TrackPosAt(track, track.Length * i / (n - 1));
                if (prev != null) acc += Dist3(prev, pos);
                pts.Add(new Pt(pos[0], pos[1]));
                cum.Add(acc);
                prev = pos;
            }
            return new SmoothPath { Pts = pts, Cum = cum, Length = acc };
        }

        public static double SpeedAt(double s, double length, Tune tune)
        {
            var ease = Math.Min(tune.SpeedEaseDist, length * 0.25);
            var k = 1.0;
            if (ease > 1e-6)
            {
                var edge = Math.Min(Math.Max(0.0, s), Math.Max(0.0, length - s));
                k = Math.Min(1.0, Math.Max(0.0, edge / ease));
            }
            return tune.Speed * (tune.SpeedMinFrac + (1.0 - tune.SpeedMinFrac) * k);
        }

        public static double AdvanceArc(double s, double dt, double length, Tune tune) => Math.Min(length, s + SpeedAt(s, length, tune) * Math.Max(0.0, dt));

        public static (double x, double y, double angle) FollowAt(SmoothPath path, double s)
        {
            var c = Math.Min(path.Length, Math.Max(0.0, s));
            var lo = SampleIndexF(path.Cum, c);
            var i = Math.Min(lo, path.Pts.Count - 2);
            var span = path.Cum[i + 1] - path.Cum[i];
            var f = span > 1e-9 ? (c - path.Cum[i]) / span : 0.0;
            var a = path.Pts[i];
            var b = path.Pts[i + 1];
            return (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, Math.Atan2(b.Y - a.Y, b.X - a.X));
        }

        public static List<int> FlowersOnPath(SmoothPath path, List<Pt> flowers, Tune tune)
        {
            var picked = new List<int>();
            for (var i = 0; i < flowers.Count; i++)
                foreach (var pt in path.Pts)
                    if (Dist2(pt, flowers[i]) <= tune.FlowerPickRadius) { picked.Add(i); break; }
            return picked;
        }

        public static bool StartsAtPost(Pt pt, Tune tune) => Dist2(pt, new Pt(tune.PostX, tune.PostY)) <= tune.StartRadius;

        public static int EndHouse(SmoothPath path, Level level, Tune tune)
        {
            var tip = path.Pts[path.Pts.Count - 1];
            var best = -1;
            var bestD = double.PositiveInfinity;
            for (var i = 0; i < level.Houses.Count; i++)
            {
                var d = Dist2(tip, DoorOf(level.Houses[i], tune));
                if (d <= tune.DeliverRadius && d < bestD) { best = i; bestD = d; }
            }
            return best;
        }

        public static int DeliveryPoints(bool wet, int flowersPicked, Tune tune) => tune.DeliverPts + (wet ? 0 : tune.DryBonus) + Math.Max(0, flowersPicked) * tune.FlowerPts;

        public static int ApplyScore(int score, int delta) => Math.Max(0, score + delta);

        public static bool EndlessShouldEnd(int splashes, Tune tune) => tune.Endless && splashes >= tune.EndlessMaxSplashes;

        public static (bool ok, List<Pt> pts, SmoothPath smooth) AutoRoute(Level level, Tune tune)
        {
            var door = DoorOf(level.Houses[level.TargetIdx], tune);
            var pts = new List<Pt> { new Pt(level.Post.X, level.Post.Y), new Pt(door.X, door.Y) };
            var xLim = tune.FieldHalfW - 0.12;
            var yLo = tune.FieldYMin + 0.12;
            var yHi = tune.FieldYMax - 0.12;
            for (var pass = 0; pass < tune.RouteMaxPasses; pass++)
            {
                var hit = FirstBlocked(pts, level.Puddles, tune);
                if (hit.seg < 0) break;
                var p = level.Puddles[hit.puddle];
                var a = pts[hit.seg];
                var b = pts[hit.seg + 1];
                var abx = b.X - a.X;
                var aby = b.Y - a.Y;
                var abLen2 = abx * abx + aby * aby;
                if (abLen2 == 0.0) abLen2 = 1.0;
                var t = Math.Min(1.0, Math.Max(0.0, ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / abLen2));
                var cx = a.X + abx * t;
                var cy = a.Y + aby * t;
                var wx = cx - p.X;
                var wy = cy - p.Y;
                var wLen = Hypot(wx, wy);
                if (wLen < 1e-6)
                {
                    wx = -aby; wy = abx;
                    var nLen = Hypot(wx, wy);
                    if (nLen == 0.0) nLen = 1.0;
                    wx /= nLen; wy /= nLen;
                }
                else { wx /= wLen; wy /= wLen; }
                var reach = PuddleEffR(p, tune) + tune.RouteClearance + tune.RouteDetourPad;
                var dx = Math.Min(xLim, Math.Max(-xLim, p.X + wx * reach));
                var dy = Math.Min(yHi, Math.Max(yLo, p.Y + wy * reach));
                if (Hypot(dx - p.X, dy - p.Y) < reach - 1e-6)
                {
                    dx = Math.Min(xLim, Math.Max(-xLim, p.X - wx * reach));
                    dy = Math.Min(yHi, Math.Max(yLo, p.Y - wy * reach));
                }
                pts.Insert(hit.seg + 1, new Pt(dx, dy));
            }
            var smooth = Smooth(pts, tune);
            var ok = smooth != null && PathClear(smooth.Pts, level.Puddles, tune) && EndHouse(smooth, level, tune) == level.TargetIdx;
            return (ok, pts, smooth);
        }

        public static Level GenerateLevel(GoobyRng rng, int roundIndex, Tune tune)
        {
            var post = new Pt(tune.PostX, tune.PostY);
            var kinds = new[] { "house", "burrow", "house" };
            var halfW = tune.FieldHalfW;
            var houses = new List<House>();
            for (var i = 0; i < tune.HouseSlotsX.Length; i++)
            {
                var x = Math.Min(halfW - 0.45, Math.Max(-halfW + 0.45, tune.HouseSlotsX[i] + (rng.Next() * 2.0 - 1.0) * tune.HouseJitterX));
                var y = tune.HouseYMin + rng.Next() * (tune.HouseYMax - tune.HouseYMin);
                houses.Add(new House { X = x, Y = y, Kind = kinds[i] });
            }
            var targetIdx = Math.Min(houses.Count - 1, (int)Math.Floor(rng.Next() * houses.Count));
            var door = DoorOf(houses[targetIdx], tune);
            var wantPuddles = PuddlesForRound(roundIndex, tune);
            var attempt = 0;
            while (true)
            {
                if (attempt > 0 && attempt % tune.GenMaxTries == 0 && wantPuddles > 0) wantPuddles--;
                attempt++;
                var puddles = RollPuddles(rng, wantPuddles, houses, post, tune);
                if (puddles.Count == 0 && wantPuddles > 0) continue;
                var flowers = RollFlowers(rng, post, door, puddles, tune);
                var level = new Level { Post = post, Houses = houses, TargetIdx = targetIdx, Puddles = puddles, Flowers = flowers, Round = roundIndex };
                if (AutoRoute(level, tune).ok) return level;
            }
        }

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1)
        {
            var tune = ApplyDifficulty(Snail, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            int score = 0, deliveries = 0, splashes = 0, flowersPicked = 0;
            var limit = tune.Endless ? tune.BotTimeCapSec : tune.DurationSec;
            while (elapsed < limit && !EndlessShouldEnd(splashes, tune))
            {
                var level = GenerateLevel(rng, deliveries, tune);
                var path = AutoRoute(level, tune).smooth;
                var length = path.Length;
                var s = 0.0;
                var travel = 0.0;
                while (s < length && travel < 60.0)
                {
                    s = AdvanceArc(s, 1.0 / 30.0, length, tune);
                    travel += 1.0 / 30.0;
                }
                var wet = rng.Next() < tune.BotWetRate;
                var picked = FlowersOnPath(path, level.Flowers, tune).Count;
                score = ApplyScore(score, DeliveryPoints(wet, picked, tune));
                deliveries++;
                flowersPicked += picked;
                if (wet) splashes++;
                elapsed += tune.BotDrawSec + travel + (wet ? tune.RetreatSec : 0.0) + tune.RoundBeatSec;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["deliveries"] = deliveries, ["splashes"] = splashes, ["flowersPicked"] = flowersPicked, ["elapsed"] = elapsed };
        }

        private static List<Puddle> RollPuddles(GoobyRng rng, int want, List<House> houses, Pt post, Tune tune)
        {
            var puddles = new List<Puddle>();
            var placed = true;
            var halfW = tune.FieldHalfW;
            var rMin = tune.PuddleRMin;
            var rSpan = tune.PuddleRMax - rMin;
            var yMin = tune.PuddleYMin;
            var ySpan = tune.PuddleYMax - yMin;
            var keepout = tune.PuddleKeepout;
            var i = 0;
            while (i < want && placed)
            {
                placed = false;
                for (var roll = 0; roll < 20; roll++)
                {
                    var r = rMin + rng.Next() * rSpan;
                    var x = (rng.Next() * 2.0 - 1.0) * (halfW - r - 0.15);
                    var y = yMin + rng.Next() * ySpan;
                    if (Hypot(x - post.X, y - post.Y) < r + keepout) continue;
                    var clear = true;
                    foreach (var h in houses)
                    {
                        var d = DoorOf(h, tune);
                        if (Hypot(x - d.X, y - d.Y) < r + keepout) { clear = false; break; }
                    }
                    if (!clear) continue;
                    foreach (var p in puddles)
                        if (Hypot(x - p.X, y - p.Y) < r + p.R + tune.PuddleGap) { clear = false; break; }
                    if (!clear) continue;
                    puddles.Add(new Puddle { X = x, Y = y, R = r });
                    placed = true;
                    break;
                }
                i++;
            }
            return placed ? puddles : new List<Puddle>();
        }

        private static List<Pt> RollFlowers(GoobyRng rng, Pt post, Pt door, List<Puddle> puddles, Tune tune)
        {
            var flowers = new List<Pt>();
            var count = tune.FlowersPerRound;
            var halfW = tune.FieldHalfW;
            var dirX = door.X - post.X;
            var dirY = door.Y - post.Y;
            var dirLen = Hypot(dirX, dirY);
            if (dirLen == 0.0) dirLen = 1.0;
            for (var i = 0; i < count; i++)
            {
                var frac = 0.28 + (i / (double)Math.Max(1, count - 1)) * 0.5;
                var lx = post.X + dirX * frac;
                var ly = post.Y + dirY * frac;
                for (var roll = 0; roll < 10; roll++)
                {
                    var jitter = (rng.Next() * 2.0 - 1.0) * tune.FlowerLaneOffset;
                    var x = Math.Min(halfW - 0.2, Math.Max(-halfW + 0.2, lx + (-dirY / dirLen) * jitter));
                    var y = Math.Min(tune.FieldYMax - 0.6, Math.Max(tune.FieldYMin + 0.3, ly + (dirX / dirLen) * jitter));
                    if (PuddleHitAt(x, y, puddles, tune) >= 0) continue;
                    var spaced = true;
                    foreach (var f in flowers) if (Hypot(x - f.X, y - f.Y) < tune.FlowerMinSpacing) { spaced = false; break; }
                    if (!spaced) continue;
                    flowers.Add(new Pt(x, y));
                    break;
                }
            }
            return flowers;
        }

        private static (int seg, int puddle) FirstBlocked(List<Pt> pts, List<Puddle> puddles, Tune tune)
        {
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var length = Dist2(a, b);
                var steps = Math.Max(1, (int)Math.Ceiling(length / tune.RouteSampleStep));
                for (var k = 0; k <= steps; k++)
                {
                    var x = a.X + (b.X - a.X) * k / steps;
                    var y = a.Y + (b.Y - a.Y) * k / steps;
                    for (var j = 0; j < puddles.Count; j++)
                        if (Hypot(x - puddles[j].X, y - puddles[j].Y) < PuddleEffR(puddles[j], tune) + tune.RouteClearance) return (i, j);
                }
            }
            return (-1, -1);
        }

        private static int SampleIndex(List<double> sArr, double s)
        {
            var lo = 0;
            var hi = sArr.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) >> 1;
                if (sArr[mid] <= s) lo = mid;
                else hi = mid - 1;
            }
            return Math.Min(lo, sArr.Count - 2);
        }

        private static int SampleIndexF(List<double> cum, double s)
        {
            var lo = 0;
            var hi = cum.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) >> 1;
                if (cum[mid] <= s) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        private static double Dist2(Pt a, Pt b) => Hypot(a.X - b.X, a.Y - b.Y);
        private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
    }
}
