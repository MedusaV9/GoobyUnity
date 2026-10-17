using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Spielzeug-Rennen — numerically identical port of toy_racer_logic.gd + toy_racer_track.gd (web toyRacer.logic.js,
    /// PLAN3 §C10.1 #1). Three laps on a seeded toy track (2 templates × seeds) against three rubber-band AI karts.
    /// Holding charges a drift, releasing gives a 1.2 s boost. Item boxes every third of a lap (turbo / shield / block).
    /// Off the track 40 % slower. Points = place bonus + 2 per overtake + drift metres / 10. Coin row /6, 5..30, target 150.
    /// </summary>
    public static class ToyRacer
    {
        public sealed class Tune
        {
            public int Laps = 3, Karts = 4, PiecesPerLoop = 8;
            public double TrackHalfW = 0.5, LatMax = 0.36, LatHardMax = 0.78;
            public double TargetLapSec = 47.0, MaxRaceSec = 240.0, WorldScale = 2.6;
            public double SteerRate = 1.1, DriftSteerMult = 1.6, SlipGain = 0.5, DriftSlipMult = 0.25;
            public double DriftBoostSec = 1.2, DriftBoostMult = 1.45, DriftMinCharge = 0.35;
            public double DriftChargeRateCurve = 0.55, DriftChargeRateStraight = 0.12, DriftMinKappa = 0.12;
            public double OfftrackMult = 0.6;
            public int ItemRowsPerLap = 3;
            public double[] ItemRowFractions = { 0.18, 0.5, 0.82 };
            public double[] ItemBoxLats = { -0.3, 0.0, 0.3 };
            public double ItemRespawnSec = 2.5, PickupSWindow = 0.35, PickupLatWindow = 0.24;
            public string[] ItemKinds = { "turbo", "shield", "block" };
            public double[] ItemWeights = { 0.4, 0.3, 0.3 };
            public double TurboSec = 2.0, TurboMult = 1.5;
            public double BlockDropBehind = 0.8, BlockStunSec = 0.9, BlockStunMult = 0.25, BlockHitS = 0.28, BlockHitLat = 0.22;
            public int MaxBlocks = 6;
            public double RubberDist = 6.0, RubberGain = 0.1, RubberMin = 0.88, RubberMax = 1.12;
            public double AiSpread = 0.04, AccelRate = 2.0, BrakeRate = 5.0;
            public int[] PositionBonus = { 120, 80, 50, 30 };
            public int OvertakePoints = 2;
            public double DriftMetersDiv = 10.0, OvertakeCooldownSec = 1.5;
            public double BotDriftMinDeg = 45.0, BotCornerLookahead = 1.0;
            public double SampleStep = 0.25, GridGap = 0.85, MaxSubstep = 1.0 / 30.0;
            public double AiEdge = 0.0, SpeedMult = 1.0, ScoreMult = 1.0, ItemRate = 1.0;
            public bool Endless;
            public int EndlessChainMaxRank = 2;
            public double EndlessChainEdgeStep = 0.02;
            public double BotLapseEverySec = 0.0, BotLapseSec = 1.6;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Racer = new Tune();

        private struct DiffRow
        {
            public double Speed, AiSpread, AiEdge, RubberMin, RubberMax, BotLapse;
        }

        private static readonly Dictionary<string, DiffRow> Difficulty = new Dictionary<string, DiffRow>
        {
            ["easy"] = new DiffRow { Speed = 0.85, AiSpread = 0.08, AiEdge = -0.02, RubberMin = 0.85, RubberMax = 1.08, BotLapse = 0.0 },
            ["normal"] = new DiffRow { Speed = 1.0, AiSpread = 0.04, AiEdge = 0.0, RubberMin = 0.88, RubberMax = 1.12, BotLapse = 0.0 },
            ["hard"] = new DiffRow { Speed = 1.2, AiSpread = 0.022, AiEdge = 0.015, RubberMin = 0.92, RubberMax = 1.12, BotLapse = 18.0 },
            ["endless"] = new DiffRow { Speed = 1.2, AiSpread = 0.022, AiEdge = 0.015, RubberMin = 0.92, RubberMax = 1.12, BotLapse = 18.0 },
        };

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode == "normal" || mode == null || !Difficulty.TryGetValue(mode, out var row)) return tune;
            var t = tune.Clone();
            t.TargetLapSec = tune.TargetLapSec / row.Speed;
            t.AiSpread = row.AiSpread;
            t.AiEdge = row.AiEdge;
            t.RubberMin = row.RubberMin;
            t.RubberMax = row.RubberMax;
            t.SpeedMult = row.Speed;
            t.BotLapseEverySec = row.BotLapse;
            t.Endless = mode == "endless";
            t.Mode = mode;
            return t;
        }

        // ── track (toy_racer_track.gd) ─────────────────────────────────────

        private struct PieceDef
        {
            public string Kind;
            public double Len, Dy, R, Shift, Entry, OriginOffset;
            public bool HasEntry;
        }

        private static readonly Dictionary<string, PieceDef> PieceLib = new Dictionary<string, PieceDef>
        {
            ["straight"] = new PieceDef { Kind = "straight", Len = 4.0, Dy = 0.0 },
            ["bumpUp"] = new PieceDef { Kind = "straight", Len = 4.0, Dy = 0.5 },
            ["bumpDown"] = new PieceDef { Kind = "straight", Len = 4.0, Dy = -0.5 },
            ["cornerS"] = new PieceDef { Kind = "corner", R = 2.0 },
            ["cornerL"] = new PieceDef { Kind = "corner", R = 4.0 },
            ["curve"] = new PieceDef { Kind = "shift", Len = 4.0, Shift = 2.0 },
            ["loop"] = new PieceDef { Kind = "loop", Len = 4.0, Shift = 1.0, R = 1.7, Entry = 2.0, HasEntry = true, OriginOffset = 2.0 },
        };

        private static readonly (string id, string[] pieces, int[] bumpPair)[] Templates =
        {
            ("rugRing", new[] { "straight", "straight", "cornerL", "cornerL", "straight", "straight", "cornerL", "cornerL" }, new[] { 4, 5 }),
            ("loopBoulevard", new[] { "loop", "loop", "cornerS", "cornerL", "straight", "curve", "cornerS", "cornerL" }, new int[0]),
        };

        /// <summary>Grid directions (x, z): 0=+z · 1=−x · 2=−z · 3=+x.</summary>
        private static readonly double[][] Dirs = { new[] { 0.0, 1.0 }, new[] { -1.0, 0.0 }, new[] { 0.0, -1.0 }, new[] { 1.0, 0.0 } };
        private const double Fine = 0.06;

        public struct V3
        {
            public double X, Y, Z;
            public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static V3 Lerp(V3 a, V3 b, double f) => new V3(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);
            public double Len => Math.Sqrt(X * X + Y * Y + Z * Z);
            public V3 Norm() { var n = Len; if (n == 0.0) n = 1.0; return new V3(X / n, Y / n, Z / n); }
        }

        public sealed class Sample
        {
            public V3 P, Up, T;
            public double Kappa;
        }

        public struct Piece
        {
            public string Type;
            public double X, Y, Z, OriginOffset;
            public int Dir;
        }

        public struct Zone
        {
            public double S0, S1, TurnDeg;
        }

        public sealed class Box
        {
            public double Lat, RespawnT;
        }

        public sealed class ItemRow
        {
            public double S;
            public List<Box> Boxes = new List<Box>();
        }

        public sealed class Track
        {
            public string TemplateId;
            public bool HasBumps;
            public List<Piece> Pieces = new List<Piece>();
            public List<Sample> Samples = new List<Sample>();
            public double Step, LapLen;
            public List<Zone> CornerZones = new List<Zone>();
            public List<Zone> LoopZones = new List<Zone>();
            public List<ItemRow> ItemRows = new List<ItemRow>();
        }

        public struct Frame
        {
            public V3 P, T, Up, Right;
            public double Kappa;
        }

        private static int LeftOf(int d) => (d + 1) % 4;
        private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

        private struct Cursor
        {
            public double X, Y, Z;
            public int Dir;
        }

        private static Cursor EmitPiece(PieceDef def, Cursor cur, List<(V3 p, V3 up)> points)
        {
            var h = Dirs[cur.Dir];
            var l = Dirs[LeftOf(cur.Dir)];
            double cx = cur.X, cy = cur.Y, cz = cur.Z;
            var up = new V3(0.0, 1.0, 0.0);
            if (def.Kind == "straight")
            {
                var n = (int)Math.Ceiling(def.Len / Fine);
                for (var i = 0; i < n; i++)
                {
                    var u = (double)i / n * def.Len;
                    var t = u / def.Len;
                    points.Add((new V3(cx + h[0] * u, cy + def.Dy * Smooth(t), cz + h[1] * u), up));
                }
                return new Cursor { X = cx + h[0] * def.Len, Y = cy + def.Dy, Z = cz + h[1] * def.Len, Dir = cur.Dir };
            }
            if (def.Kind == "corner")
            {
                var r = def.R;
                var ccx = cx + l[0] * r;
                var ccz = cz + l[1] * r;
                var arc = Math.PI / 2.0 * r;
                var n = (int)Math.Ceiling(arc / Fine);
                for (var i = 0; i < n; i++)
                {
                    var phi = (double)i / n * (Math.PI / 2.0);
                    points.Add((new V3(ccx - l[0] * r * Math.Cos(phi) + h[0] * r * Math.Sin(phi), cy, ccz - l[1] * r * Math.Cos(phi) + h[1] * r * Math.Sin(phi)), up));
                }
                return new Cursor { X = cx + h[0] * r + l[0] * r, Y = cy, Z = cz + h[1] * r + l[1] * r, Dir = LeftOf(cur.Dir) };
            }
            if (def.Kind == "shift")
            {
                var n = (int)Math.Ceiling(def.Len / Fine);
                for (var i = 0; i < n; i++)
                {
                    var u = (double)i / n * def.Len;
                    var t = u / def.Len;
                    points.Add((new V3(cx + h[0] * u + l[0] * def.Shift * Smooth(t), cy, cz + h[1] * u + l[1] * def.Shift * Smooth(t)), up));
                }
                return new Cursor { X = cx + h[0] * def.Len + l[0] * def.Shift, Y = cy, Z = cz + h[1] * def.Len + l[1] * def.Shift, Dir = cur.Dir };
            }
            // vertical loop: entry → 2π circle in the (travel, y) plane drifting one unit to the left → exit
            var radius = def.R;
            var total = def.Len;
            var loopShift = def.Shift;
            var entry = def.HasEntry ? def.Entry : total / 2.0;
            var exitLen = total - entry;
            var nE = (int)Math.Ceiling(entry / Fine);
            for (var i = 0; i < nE; i++)
            {
                var u = (double)i / nE * entry;
                points.Add((new V3(cx + h[0] * u, cy, cz + h[1] * u), up));
            }
            var c0x = cx + h[0] * entry;
            var c0z = cz + h[1] * entry;
            var nC = (int)Math.Ceiling(2.0 * Math.PI * radius / Fine);
            for (var i = 0; i < nC; i++)
            {
                var th = (double)i / nC * Math.PI * 2.0;
                var drift = th / (Math.PI * 2.0) * loopShift;
                points.Add((new V3(c0x + h[0] * radius * Math.Sin(th) + l[0] * drift, cy + radius * (1.0 - Math.Cos(th)), c0z + h[1] * radius * Math.Sin(th) + l[1] * drift),
                    new V3(-h[0] * Math.Sin(th), Math.Cos(th), -h[1] * Math.Sin(th))));
            }
            var exX = c0x + l[0] * loopShift;
            var exZ = c0z + l[1] * loopShift;
            var nX = (int)Math.Ceiling(exitLen / Fine);
            for (var i = 0; i < nX; i++)
            {
                var u = (double)i / nX * exitLen;
                points.Add((new V3(exX + h[0] * u, cy, exZ + h[1] * u), up));
            }
            return new Cursor { X = cx + h[0] * total + l[0] * loopShift, Y = cy, Z = cz + h[1] * total + l[1] * loopShift, Dir = cur.Dir };
        }

        /// <summary>Seeded track (2 templates × seeds) with its arc-length table.</summary>
        public static Track BuildTrack(int seed, Tune tune)
        {
            var rng = new GoobyRng(unchecked((uint)seed ^ 0x51AB7E0Du));
            var tpl = Templates[rng.Next() < 0.5 ? 0 : 1];
            var useBumps = tpl.bumpPair.Length > 0 && rng.Next() < 0.6;
            var types = new List<string>();
            for (var i = 0; i < tpl.pieces.Length; i++)
            {
                if (useBumps && i == tpl.bumpPair[0]) types.Add("bumpUp");
                else if (useBumps && i == tpl.bumpPair[1]) types.Add("bumpDown");
                else types.Add(tpl.pieces[i]);
            }

            var track = new Track { TemplateId = tpl.id, HasBumps = useBumps, Step = tune.SampleStep };
            var cur = new Cursor();
            var fine = new List<(V3 p, V3 up)>();
            var ranges = new List<(string type, int from, int to, double turnDeg)>();
            foreach (var type in types)
            {
                var def = PieceLib[type];
                track.Pieces.Add(new Piece { Type = type, X = cur.X, Y = cur.Y, Z = cur.Z, Dir = cur.Dir, OriginOffset = def.OriginOffset });
                var from = fine.Count;
                cur = EmitPiece(def, cur, fine);
                ranges.Add((type, from, fine.Count, def.Kind == "corner" ? 90.0 : 0.0));
            }

            // even arc-length sampling around the closed ring
            var step = tune.SampleStep;
            var samples = track.Samples;
            var acc = 0.0;
            var prev = fine[0];
            samples.Add(new Sample { P = prev.p, Up = prev.up });
            var fineS = new List<double> { 0.0 };
            for (var i = 1; i < fine.Count + 1; i++)
            {
                var pt = fine[i % fine.Count];
                var pp = prev.p;
                var np = pt.p;
                var d = Math.Sqrt((np.X - pp.X) * (np.X - pp.X) + (np.Y - pp.Y) * (np.Y - pp.Y) + (np.Z - pp.Z) * (np.Z - pp.Z));
                var segStart = acc;
                acc += d;
                fineS.Add(acc);
                while (samples.Count * step <= acc && d > 0.0)
                {
                    var target = samples.Count * step;
                    var f = (target - segStart) / d;
                    samples.Add(new Sample { P = V3.Lerp(pp, np, f), Up = V3.Lerp(prev.up, pt.up, f).Norm() });
                }
                prev = pt;
            }
            track.LapLen = acc;

            // tangents (central difference on the closed ring) and yaw curvature
            var n = samples.Count;
            for (var i = 0; i < n; i++)
            {
                var a = samples[(i - 1 + n) % n].P;
                var b = samples[(i + 1) % n].P;
                samples[i].T = new V3(b.X - a.X, b.Y - a.Y, b.Z - a.Z).Norm();
            }
            for (var i = 0; i < n; i++)
            {
                var t0 = samples[i].T;
                var t1 = samples[(i + 1) % n].T;
                var h0 = Math.Sqrt(t0.X * t0.X + t0.Z * t0.Z);
                var h1 = Math.Sqrt(t1.X * t1.X + t1.Z * t1.Z);
                if (h0 < 0.5 || h1 < 0.5) { samples[i].Kappa = 0.0; continue; }
                var cross = t0.Z * t1.X - t0.X * t1.Z;
                var dot = (t0.X * t1.X + t0.Z * t1.Z) / (h0 * h1);
                samples[i].Kappa = Math.Atan2(cross, Math.Max(-1.0, Math.Min(1.0, dot))) / step;
            }

            foreach (var r in ranges)
            {
                var s0 = fineS[r.from];
                var s1 = fineS[Math.Min(r.to, fineS.Count - 1)];
                if (r.turnDeg > 0.0) track.CornerZones.Add(new Zone { S0 = s0, S1 = s1, TurnDeg = r.turnDeg });
                if (r.type == "loop") track.LoopZones.Add(new Zone { S0 = s0, S1 = s1 });
            }

            foreach (var frac in tune.ItemRowFractions)
            {
                var s = frac * track.LapLen;
                foreach (var z in track.LoopZones)
                    if (s > z.S0 - 1.0 && s < z.S1 + 0.5) s = FMod(z.S1 + 0.8, track.LapLen);
                var row = new ItemRow { S = s };
                foreach (var lat in tune.ItemBoxLats) row.Boxes.Add(new Box { Lat = lat });
                track.ItemRows.Add(row);
            }
            return track;
        }

        /// <summary>Centre spline at arc length s (wraps lap after lap).</summary>
        public static Frame PointAt(Track track, double s)
        {
            var samples = track.Samples;
            var n = samples.Count;
            var u = FMod(s, track.LapLen) / track.Step;
            if (u < 0.0) u += n;
            var i0 = (int)Math.Floor(u) % n;
            var i1 = (i0 + 1) % n;
            var f = u - Math.Floor(u);
            var a = samples[i0];
            var b = samples[i1];
            var t = V3.Lerp(a.T, b.T, f).Norm();
            var up = V3.Lerp(a.Up, b.Up, f).Norm();
            var right = new V3(t.Y * up.Z - t.Z * up.Y, t.Z * up.X - t.X * up.Z, t.X * up.Y - t.Y * up.X).Norm();
            return new Frame { P = V3.Lerp(a.P, b.P, f), T = t, Up = up, Right = right, Kappa = a.Kappa };
        }

        public static bool InLoopZone(Track track, double s)
        {
            var sm = FMod(FMod(s, track.LapLen) + track.LapLen, track.LapLen);
            foreach (var z in track.LoopZones) if (sm >= z.S0 && sm <= z.S1) return true;
            return false;
        }

        /// <summary>Corner zone (with look-ahead) of at least minDeg, or null.</summary>
        public static Zone? CornerZoneAt(Track track, double s, double lookahead = 0.0, double minDeg = 0.0)
        {
            var sm = FMod(FMod(s + lookahead, track.LapLen) + track.LapLen, track.LapLen);
            foreach (var z in track.CornerZones) if (z.TurnDeg >= minDeg && sm >= z.S0 && sm <= z.S1) return z;
            return null;
        }

        // ── race ───────────────────────────────────────────────────────────

        public sealed class Kart
        {
            public int Id;
            public bool IsPlayer;
            public double S, Progress, Lateral, TargetLateral, Speed;
            public bool Drifting;
            public double DriftCharge, DriftMeters, BoostT, BoostMult = 1.0, StunT;
            public bool OffTrack, Shield;
            public string Item = "";
            public double Personality = 1.0, LaneBias;
            public int PassSign;
            public double PassCooldown;
        }

        public sealed class Block
        {
            public double S, Lat;
            public int By;
        }

        public struct Ev
        {
            public string Type;
            public int Kart, Lap, Rank, Total, Races, Banked;
            public string Item;
            public double S, Lat, Charge;
            public bool Final;
        }

        public struct Input
        {
            public double? Steer;
            public bool Drifting, UseItem;
        }

        public sealed class Race
        {
            public int Seed;
            public Tune Tune;
            public Track Track;
            public GoobyRng Rng;
            public double BaseSpeed;
            public readonly List<Kart> Karts = new List<Kart>();
            public readonly List<Block> Blocks = new List<Block>();
            public double Time;
            public int Overtakes;
            public bool Ended;
            public int FinishRank;
            public int LastLapBanner;
            public readonly List<Ev> Events = new List<Ev>();
            public double RaceStartT;
            public int ChainRaces, ChainWins, ChainScore;
            public double ChainEdge;
        }

        public static double ComputeRubber(double gap, Tune tune) =>
            Math.Min(tune.RubberMax, Math.Max(tune.RubberMin, 1.0 + gap / tune.RubberDist * tune.RubberGain));

        public static string RollItem(GoobyRng rng, Tune tune)
        {
            var r = rng.Next();
            for (var i = 0; i < tune.ItemKinds.Length; i++)
            {
                r -= tune.ItemWeights[i];
                if (r < 0.0) return tune.ItemKinds[i];
            }
            return tune.ItemKinds[tune.ItemKinds.Length - 1];
        }

        /// <summary>§C10.1 score: place bonus + 2·overtakes + drift metres / 10 (floored).</summary>
        public static int RaceScore(int rank, int overtakes, double driftMeters, Tune tune)
        {
            var idx = Math.Min(tune.PositionBonus.Length, Math.Max(1, rank)) - 1;
            return tune.PositionBonus[idx] + tune.OvertakePoints * overtakes + (int)Math.Floor(driftMeters / tune.DriftMetersDiv);
        }

        public static Race CreateRace(int seed, Tune tune)
        {
            var track = BuildTrack(seed, tune);
            var race = new Race
            {
                Seed = seed, Tune = tune, Track = track,
                Rng = new GoobyRng(unchecked((uint)seed ^ 0x9E3779B9u)),
                BaseSpeed = track.LapLen / tune.TargetLapSec,
            };
            var laneSpread = new[] { -0.26, 0.0, 0.26 };
            var lapLen = track.LapLen;
            for (var i = 0; i < tune.Karts; i++)
            {
                var isPlayer = i == 0;
                var gridPos = isPlayer ? tune.Karts - 1 : i - 1;
                var gridOffset = 1.0 + gridPos * tune.GridGap;
                var personality = 1.0;
                var laneBias = 0.0;
                if (!isPlayer)
                {
                    personality = 1.0 + tune.AiEdge - race.Rng.Next() * tune.AiSpread;
                    laneBias = laneSpread[i - 1] + (race.Rng.Next() * 2.0 - 1.0) * 0.05;
                }
                race.Karts.Add(new Kart
                {
                    Id = i, IsPlayer = isPlayer,
                    S = FMod(FMod(lapLen - gridOffset, lapLen) + lapLen, lapLen),
                    Progress = -gridOffset,
                    Lateral = gridPos % 2 == 0 ? -0.22 : 0.22,
                    Personality = personality, LaneBias = laneBias,
                });
            }
            return race;
        }

        public static int PlayerRank(Race race)
        {
            var p = race.Karts[0].Progress;
            var rank = 1;
            for (var i = 1; i < race.Karts.Count; i++) if (race.Karts[i].Progress > p) rank++;
            return rank;
        }

        public static int PlayerLap(Race race)
        {
            var laps = (int)Math.Floor(race.Karts[0].Progress / race.Track.LapLen) + 1;
            return Math.Min(race.Tune.Laps, Math.Max(1, laps));
        }

        /// <summary>Signed shortest s difference on the ring.</summary>
        public static double SDelta(double a, double b, double lapLen)
        {
            var d = a - b;
            while (d > lapLen / 2.0) d -= lapLen;
            while (d < -lapLen / 2.0) d += lapLen;
            return d;
        }

        private static void UseItem(Race race, Kart kart)
        {
            var tune = race.Tune;
            var kind = kart.Item;
            if (string.IsNullOrEmpty(kind)) return;
            kart.Item = "";
            if (kind == "turbo")
            {
                kart.BoostT = Math.Max(kart.BoostT, tune.TurboSec);
                kart.BoostMult = tune.TurboMult;
                race.Events.Add(new Ev { Type = "turbo", Kart = kart.Id });
            }
            else if (kind == "shield")
            {
                kart.Shield = true;
                race.Events.Add(new Ev { Type = "shield", Kart = kart.Id });
            }
            else
            {
                var lapLen = race.Track.LapLen;
                var s = FMod(FMod(kart.S - tune.BlockDropBehind, lapLen) + lapLen, lapLen);
                race.Blocks.Add(new Block { S = s, Lat = kart.Lateral, By = kart.Id });
                if (race.Blocks.Count > tune.MaxBlocks) race.Blocks.RemoveAt(0);
                race.Events.Add(new Ev { Type = "blockDrop", Kart = kart.Id, S = s, Lat = kart.Lateral });
            }
        }

        /// <summary>AI decision (centre spline + racing line + item hunt + block dodge).</summary>
        public static Input AiInput(Race race, Kart kart)
        {
            var tune = race.Tune;
            var track = race.Track;
            var lapLen = track.LapLen;
            var steer = kart.LaneBias;
            var zone = CornerZoneAt(track, kart.S, tune.BotCornerLookahead, 0.0);
            if (zone.HasValue) steer = -0.2 + kart.LaneBias * 0.6;
            if (string.IsNullOrEmpty(kart.Item))
            {
                foreach (var row in track.ItemRows)
                {
                    var d = SDelta(row.S, kart.S, lapLen);
                    if (d > 0.0 && d < 3.0)
                    {
                        var best = 0.0;
                        var hasBest = false;
                        foreach (var box in row.Boxes)
                        {
                            if (box.RespawnT > 0.0) continue;
                            if (!hasBest || Math.Abs(box.Lat - kart.Lateral) < Math.Abs(best - kart.Lateral)) { best = box.Lat; hasBest = true; }
                        }
                        if (hasBest) steer = best;
                    }
                }
            }
            foreach (var block in race.Blocks)
            {
                var d = SDelta(block.S, kart.S, lapLen);
                if (d > 0.0 && d < 2.2 && Math.Abs(block.Lat - kart.Lateral) < 0.3)
                    steer = block.Lat > 0.0 ? block.Lat - 0.45 : block.Lat + 0.45;
            }
            var drifting = zone.HasValue && zone.Value.TurnDeg >= tune.BotDriftMinDeg;
            return new Input { Steer = steer, Drifting = drifting, UseItem = !string.IsNullOrEmpty(kart.Item) };
        }

        /// <summary>One integration step of a kart.</summary>
        public static void StepKart(Race race, Kart kart, double dt, Input input)
        {
            var tune = race.Tune;
            var track = race.Track;
            var lapLen = track.LapLen;
            var sample = PointAt(track, kart.S);

            var wasDrifting = kart.Drifting;
            kart.Drifting = input.Drifting && kart.StunT <= 0.0;
            if (wasDrifting && !kart.Drifting)
            {
                if (kart.IsPlayer && kart.DriftCharge >= tune.DriftMinCharge)
                {
                    kart.BoostT = Math.Max(kart.BoostT, tune.DriftBoostSec);
                    kart.BoostMult = tune.DriftBoostMult;
                    race.Events.Add(new Ev { Type = "boost", Kart = kart.Id, Charge = kart.DriftCharge });
                }
                kart.DriftCharge = 0.0;
            }
            if (kart.Drifting && kart.Speed > 0.2)
            {
                var curved = Math.Abs(sample.Kappa) >= tune.DriftMinKappa;
                var rateC = curved ? tune.DriftChargeRateCurve : tune.DriftChargeRateStraight;
                kart.DriftCharge = Math.Min(1.0, kart.DriftCharge + rateC * dt);
                kart.DriftMeters += kart.Speed * dt * tune.WorldScale;
            }

            var target = race.BaseSpeed * kart.Personality;
            if (kart.BoostT > 0.0) target *= kart.BoostMult;
            if (kart.StunT > 0.0) target *= tune.BlockStunMult;
            if (kart.OffTrack) target *= tune.OfftrackMult;
            if (!kart.IsPlayer) target *= ComputeRubber(race.Karts[0].Progress - kart.Progress, tune);
            var rate = target < kart.Speed ? tune.BrakeRate : tune.AccelRate;
            kart.Speed += (target - kart.Speed) * Math.Min(1.0, dt * rate);

            var inLoop = InLoopZone(track, kart.S);
            var hard = tune.LatHardMax;
            if (input.Steer.HasValue && !inLoop) kart.TargetLateral = Math.Max(-hard, Math.Min(hard, input.Steer.Value));
            if (inLoop) kart.TargetLateral = 0.0;
            var steerRate = tune.SteerRate * (kart.Drifting ? tune.DriftSteerMult : 1.0);
            var dLat = kart.TargetLateral - kart.Lateral;
            var maxStep = steerRate * dt;
            kart.Lateral += Math.Max(-maxStep, Math.Min(maxStep, dLat));
            var slip = sample.Kappa * kart.Speed * kart.Speed * tune.SlipGain * (kart.Drifting ? tune.DriftSlipMult : 1.0);
            kart.Lateral += slip * dt;
            kart.Lateral = Math.Max(-hard, Math.Min(hard, kart.Lateral));
            var wasOff = kart.OffTrack;
            kart.OffTrack = Math.Abs(kart.Lateral) > tune.TrackHalfW && !inLoop;
            if (kart.OffTrack && !wasOff && kart.IsPlayer) race.Events.Add(new Ev { Type = "offtrack", Kart = kart.Id });

            kart.S = FMod(FMod(kart.S + kart.Speed * dt, lapLen) + lapLen, lapLen);
            kart.Progress += kart.Speed * dt;

            foreach (var row in track.ItemRows)
            {
                if (Math.Abs(SDelta(row.S, kart.S, lapLen)) > tune.PickupSWindow) continue;
                foreach (var box in row.Boxes)
                {
                    if (box.RespawnT > 0.0) continue;
                    if (Math.Abs(box.Lat - kart.Lateral) > tune.PickupLatWindow) continue;
                    box.RespawnT = tune.ItemRespawnSec;
                    if (string.IsNullOrEmpty(kart.Item))
                    {
                        kart.Item = RollItem(race.Rng, tune);
                        race.Events.Add(new Ev { Type = "pickup", Kart = kart.Id, Item = kart.Item, S = row.S, Lat = box.Lat });
                    }
                    else race.Events.Add(new Ev { Type = "boxBump", Kart = kart.Id, S = row.S, Lat = box.Lat });
                    break;
                }
            }

            if (kart.StunT <= 0.0)
            {
                for (var i = race.Blocks.Count - 1; i >= 0; i--)
                {
                    var block = race.Blocks[i];
                    var dist = Math.Abs(SDelta(block.S, kart.S, lapLen));
                    if (block.By == kart.Id && dist < 1.2) continue;
                    if (dist > tune.BlockHitS) continue;
                    if (Math.Abs(block.Lat - kart.Lateral) > tune.BlockHitLat) continue;
                    race.Blocks.RemoveAt(i);
                    if (kart.Shield)
                    {
                        kart.Shield = false;
                        race.Events.Add(new Ev { Type = "shieldPop", Kart = kart.Id });
                    }
                    else
                    {
                        kart.StunT = tune.BlockStunSec;
                        kart.DriftCharge = 0.0;
                        race.Events.Add(new Ev { Type = "blockHit", Kart = kart.Id });
                    }
                    break;
                }
            }

            if (input.UseItem) UseItem(race, kart);
            kart.BoostT = Math.Max(0.0, kart.BoostT - dt);
            kart.StunT = Math.Max(0.0, kart.StunT - dt);
        }

        /// <summary>§G5.4 endless chain: bank the race just driven and line the field up again.</summary>
        public static void ResetRaceForChain(Race race, int rank)
        {
            var tune = race.Tune;
            var lapLen = race.Track.LapLen;
            race.ChainScore += RaceScore(rank, race.Overtakes, race.Karts[0].DriftMeters, tune);
            race.ChainRaces += 1;
            if (rank == 1) race.ChainWins += 1;
            race.ChainEdge += tune.EndlessChainEdgeStep;
            for (var i = 0; i < race.Karts.Count; i++)
            {
                var kart = race.Karts[i];
                var gridPos = kart.IsPlayer ? tune.Karts - 1 : i - 1;
                var gridOffset = 1.0 + gridPos * tune.GridGap;
                kart.S = FMod(FMod(lapLen - gridOffset, lapLen) + lapLen, lapLen);
                kart.Progress = -gridOffset;
                kart.Lateral = gridPos % 2 == 0 ? -0.22 : 0.22;
                kart.TargetLateral = 0.0;
                kart.Speed = 0.0;
                kart.Drifting = false;
                kart.DriftCharge = 0.0;
                kart.DriftMeters = 0.0;
                kart.BoostT = 0.0;
                kart.BoostMult = 1.0;
                kart.StunT = 0.0;
                kart.OffTrack = false;
                kart.Shield = false;
                kart.Item = "";
                kart.PassSign = 0;
                kart.PassCooldown = 0.0;
                if (!kart.IsPlayer) kart.Personality += tune.EndlessChainEdgeStep;
            }
            race.Blocks.Clear();
            race.Overtakes = 0;
            race.LastLapBanner = 0;
            race.RaceStartT = race.Time;
            foreach (var row in race.Track.ItemRows) foreach (var box in row.Boxes) box.RespawnT = 0.0;
            race.Events.Add(new Ev { Type = "chainRace", Races = race.ChainRaces, Rank = rank, Banked = race.ChainScore });
        }

        /// <summary>Advance the race by dt (internally in sub-steps).</summary>
        public static void StepRace(Race race, double dt, Input input)
        {
            if (race.Ended) return;
            var tune = race.Tune;
            var track = race.Track;
            var karts = race.Karts;
            var lapLen = track.LapLen;
            var pUseItem = input.UseItem;
            var remaining = Math.Min(dt, 0.25);
            while (remaining > 1e-9 && !race.Ended)
            {
                var h = Math.Min(tune.MaxSubstep, remaining);
                remaining -= h;
                race.Time += h;
                foreach (var row in track.ItemRows) foreach (var box in row.Boxes) box.RespawnT = Math.Max(0.0, box.RespawnT - h);

                var player = karts[0];
                StepKart(race, player, h, new Input { Steer = input.Steer, Drifting = input.Drifting, UseItem = pUseItem });
                pUseItem = false;
                for (var i = 1; i < karts.Count; i++)
                {
                    var kart = karts[i];
                    StepKart(race, kart, h, AiInput(race, kart));
                    kart.PassCooldown = Math.Max(0.0, kart.PassCooldown - h);
                    var sign = Math.Sign(player.Progress - kart.Progress);
                    if (sign > 0 && kart.PassSign < 0 && kart.PassCooldown <= 0.0 && race.Time > 1.0)
                    {
                        race.Overtakes += 1;
                        kart.PassCooldown = tune.OvertakeCooldownSec;
                        race.Events.Add(new Ev { Type = "overtake", Total = race.Overtakes });
                    }
                    if (sign != 0) kart.PassSign = sign;
                }

                var lap = (int)Math.Floor(player.Progress / lapLen);
                if (lap > race.LastLapBanner && lap < tune.Laps)
                {
                    race.LastLapBanner = lap;
                    race.Events.Add(new Ev { Type = "lap", Lap = lap + 1, Final = lap + 1 == tune.Laps });
                }

                var timedOut = race.Time - race.RaceStartT >= tune.MaxRaceSec;
                if (player.Progress >= tune.Laps * lapLen || timedOut)
                {
                    var rank = PlayerRank(race);
                    if (tune.Endless && !timedOut && rank <= tune.EndlessChainMaxRank) ResetRaceForChain(race, rank);
                    else
                    {
                        race.Ended = true;
                        race.FinishRank = rank;
                        race.Events.Add(new Ev { Type = "finish", Rank = rank });
                    }
                }
            }
        }

        /// <summary>§C10.1 bot: follows the centre spline, drifts corners over 45°, uses items at once.</summary>
        public static Input BotInput(Race race)
        {
            var player = race.Karts[0];
            var b = AiInput(race, player);
            var wobble = Math.Sin(race.Time * 1.7) * 0.03;
            return new Input { Steer = b.Steer.Value + wobble, Drifting = b.Drifting, UseItem = !string.IsNullOrEmpty(player.Item) };
        }

        /// <summary>Final points of a running or finished race (chain bank + this race, × score multiplier).</summary>
        public static int RunScore(Race race)
        {
            var rank = race.Ended ? race.FinishRank : PlayerRank(race);
            var cur = RaceScore(rank, race.Overtakes, race.Karts[0].DriftMeters, race.Tune);
            return (int)Math.Round((race.ChainScore + cur) * race.Tune.ScoreMult, MidpointRounding.AwayFromZero);
        }

        public static (int races, int wins, int overtakes) RunMeta(Race race)
        {
            var rank = race.Ended ? race.FinishRank : PlayerRank(race);
            return (1 + race.ChainRaces, race.ChainWins + (rank == 1 ? 1 : 0), race.Overtakes);
        }

        /// <summary>§G5.4 certificate run: one full bot race (or an endless chain) at fixed 30 Hz with seeded lapses.</summary>
        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1, double maxSec = 3600.0)
        {
            var tune = ApplyDifficulty(Racer, mode);
            var race = CreateRace(seed, tune);
            var lapse = new GoobyRng(unchecked((uint)seed ^ 0xC0FFEE11u));
            var lapseT = 0.0;
            var lapseEvery = tune.BotLapseEverySec;
            var nextLapse = lapseEvery > 0.0 ? lapseEvery * (0.5 + lapse.Next()) : double.PositiveInfinity;
            const double dt = 1.0 / 30.0;
            var releaseT = 0.0;
            while (!race.Ended && race.Time < maxSec)
            {
                var b = BotInput(race);
                var player = race.Karts[0];
                releaseT = Math.Max(0.0, releaseT - dt);
                if (player.DriftCharge >= 0.95 && releaseT <= 0.0) releaseT = 2.0 * dt;
                var input = new Input { Steer = b.Steer, Drifting = releaseT <= 0.0, UseItem = b.UseItem };
                if (lapseEvery > 0.0)
                {
                    nextLapse -= dt;
                    if (nextLapse <= 0.0 && lapseT <= 0.0)
                    {
                        lapseT = tune.BotLapseSec * (0.6 + lapse.Next() * 0.8);
                        nextLapse = lapseEvery * (0.5 + lapse.Next());
                    }
                    if (lapseT > 0.0)
                    {
                        lapseT -= dt;
                        input = new Input { Steer = input.Steer, Drifting = false, UseItem = false };
                    }
                }
                StepRace(race, dt, input);
            }
            if (!race.Ended)
            {
                race.Ended = true;
                race.FinishRank = PlayerRank(race);
            }
            var meta = RunMeta(race);
            return new JObject
            {
                ["score"] = RunScore(race), ["rank"] = race.FinishRank, ["races"] = meta.races, ["wins"] = meta.wins,
                ["overtakes"] = race.Overtakes, ["driftMeters"] = race.Karts[0].DriftMeters, ["time"] = race.Time,
            };
        }

        /// <summary>GDScript fmod: C-style remainder (sign follows the dividend).</summary>
        public static double FMod(double a, double b) => a % b;
    }
}
