using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Tortenwerkstatt (purblePlace) — numerically identical port of purble_place_logic.gd + purble_place_bot.gd (web
    /// purblePlace.logic.js, PLAN4-GAMES §G1). The PLAYER drives the 6-m belt with ◀/▶ (0.9 m/s forward, 0.7 back,
    /// 6 m/s² ramp). Ingredients are PHYSICAL drops (0.55 m fall in 0.45 s — a pan only catches if |pan − nozzle| ≤
    /// ±0.24 m at the impact). The oven is a belt tunnel (2.25–3.15 m): green 2.25–3.0 s = +5, singed at 3.6 s = −3.
    /// Shipping at 5.95 ± 0.30 serves the best-matching open order (+20/+8/−5, combo +2…+10, speed +4).
    /// Coin row /5, 5..30, target 120.
    /// </summary>
    public static class PurblePlace
    {
        public const double Eps = 1e-9;

        public sealed class Tune
        {
            public double DurationSec = 210.0;
            public int MaxTickets = 3;
            public double PatienceStartSec = 45.0, PatienceStepSec = 1.5, PatienceFloorSec = 30.0, PatienceMult = 1.0;
            public int ExpirePts = -5;
            public double OrderIntervalStartSec = 30.0, OrderIntervalStepSec = 2.0, OrderIntervalMinSec = 14.0;
            public int ComplexAfterServes = 4, MaxCandles = 4;
            public double BeltLengthM = 6.0, BeltFwdSpeed = 0.9, BeltRevSpeed = 0.7, BeltSlew = 6.0;
            public double FallSec = 0.45, FallM = 0.55, CatchHalfM = 0.24, LockoutSec = 0.5, CandleGapSec = 0.18;
            public int SplatPts = -2;
            public double SplatTtlSec = 4.0, SpawnS = 0.15, SpawnClearM = 0.7;
            public double OvenStartS = 2.25, OvenEndS = 3.15, BakeGreenStartSec = 2.25, BakeGreenEndSec = 3.0, SingeSec = 3.6;
            public int BakePerfectPts = 5, BakeSingedPts = -3;
            public double ShipS = 5.95, ShipHalfM = 0.3;
            public int PanCapMax = 3, PanCapEveryServes = 3;
            public int PerfectPts = 20, OneWrongPts = 8, RejectPts = -5, ComboStep = 2, ComboCap = 10, SpeedBonusPts = 4;
            public double SpeedBonusMinFrac = 0.5;
            public bool Endless;
            public int EndlessFailCount = 3;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Cake = new Tune();

        public static readonly string[] Shapes = { "round", "square", "heart" };
        public static readonly string[] Sponges = { "vanilla", "chocolate", "strawberry" };
        public static readonly string[] Icings = { "white", "pink", "chocolate", "none" };
        public static readonly string[] Toppings = { "cherry", "sprinkles", "berries", "none" };
        public static readonly Dictionary<string, string> SpongeHex = new Dictionary<string, string> { ["vanilla"] = "#F5E6C8", ["chocolate"] = "#6B4A2F", ["strawberry"] = "#F2B8C6" };
        public static readonly Dictionary<string, string> IcingHex = new Dictionary<string, string> { ["white"] = "#FFF8F0", ["pink"] = "#F781B0", ["chocolate"] = "#4E3524" };

        public sealed class Station
        {
            public string Id, Kind, Value;
            public double S, S0, S1;
            public bool Button, Drop;
        }

        /// <summary>§G1.5 station table (belt position s, binding).</summary>
        public static readonly Station[] Stations =
        {
            new Station { Id = "spawn", Kind = "spawn", S = 0.15, Button = true },
            new Station { Id = "trash", Kind = "trash", S = 0.15 },
            new Station { Id = "teig.vanilla", Kind = "teig", Value = "vanilla", S = 0.9, Button = true, Drop = true },
            new Station { Id = "teig.chocolate", Kind = "teig", Value = "chocolate", S = 1.35, Button = true, Drop = true },
            new Station { Id = "teig.strawberry", Kind = "teig", Value = "strawberry", S = 1.8, Button = true, Drop = true },
            new Station { Id = "ofen", Kind = "ofen", S = 2.7, S0 = 2.25, S1 = 3.15 },
            new Station { Id = "guss.white", Kind = "guss", Value = "white", S = 3.5, Button = true, Drop = true },
            new Station { Id = "guss.pink", Kind = "guss", Value = "pink", S = 3.95, Button = true, Drop = true },
            new Station { Id = "guss.chocolate", Kind = "guss", Value = "chocolate", S = 4.4, Button = true, Drop = true },
            new Station { Id = "deko.cherry", Kind = "deko", Value = "cherry", S = 4.7, Button = true, Drop = true },
            new Station { Id = "deko.sprinkles", Kind = "deko", Value = "sprinkles", S = 5.0, Button = true, Drop = true },
            new Station { Id = "deko.berries", Kind = "deko", Value = "berries", S = 5.3, Button = true, Drop = true },
            new Station { Id = "kerzen", Kind = "kerzen", S = 5.6, Button = true, Drop = true },
            new Station { Id = "versand", Kind = "versand", S = 5.95, Button = true },
        };

        public static Station StationById(string id) => Stations.FirstOrDefault(s => s.Id == id);

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            var id = mode == "easy" || mode == "hard" || mode == "endless" ? mode : "normal";
            var t = tune.Clone();
            switch (id)
            {
                case "easy": t.PatienceMult = 1.3; t.OrderIntervalMinSec = 18.0; t.CatchHalfM = 0.3; t.SingeSec = 4.2; t.PanCapEveryServes = 3; t.Endless = false; break;
                case "normal": t.PatienceMult = 1.0; t.OrderIntervalMinSec = 14.0; t.CatchHalfM = 0.24; t.SingeSec = 3.6; t.PanCapEveryServes = 3; t.Endless = false; break;
                case "hard": t.PatienceMult = 0.8; t.OrderIntervalMinSec = 12.0; t.CatchHalfM = 0.19; t.SingeSec = 3.2; t.PanCapEveryServes = 2; t.Endless = false; break;
                default: t.PatienceMult = 0.8; t.OrderIntervalMinSec = 10.0; t.CatchHalfM = 0.19; t.SingeSec = 3.2; t.PanCapEveryServes = 2; t.Endless = true; break;
            }
            t.Mode = id;
            return t;
        }

        public static double PatienceFor(int serves, double mult = 1.0) =>
            Math.Max(Cake.PatienceFloorSec, Cake.PatienceStartSec - Cake.PatienceStepSec * Math.Max(0, serves)) * mult;

        public static double OrderIntervalAt(int serves, double floorSec) =>
            Math.Max(floorSec, Cake.OrderIntervalStartSec - Cake.OrderIntervalStepSec * Math.Max(0, serves));

        public static int PanCapAt(int serves, Tune tune) => Math.Min(tune.PanCapMax, 1 + (int)Math.Floor((double)Math.Max(0, serves) / tune.PanCapEveryServes));

        public static string BakeResultAt(double t, Tune tune)
        {
            if (t >= tune.SingeSec) return "singed";
            if (t >= tune.BakeGreenStartSec && t <= tune.BakeGreenEndSec) return "perfect";
            return t < tune.BakeGreenStartSec ? "pale" : "over";
        }

        public static int BakePoints(string result) => result == "perfect" ? Cake.BakePerfectPts : result == "singed" ? Cake.BakeSingedPts : 0;

        public static bool CatchWindow(double panS, double nozzleS, Tune tune) => Math.Abs(panS - nozzleS) <= tune.CatchHalfM + Eps;

        public static double DropImpactS(double pressedAtS, IList<(double v, double? dur)> segs, Tune tune)
        {
            var s = pressedAtS;
            var left = tune.FallSec;
            foreach (var seg in segs)
            {
                if (left <= 0.0) break;
                var d = Math.Min(seg.dur ?? left, left);
                s += seg.v * d;
                left -= d;
            }
            return s;
        }

        private static T WeightedPick<T>(GoobyRng rng, T[] items, double[] weights)
        {
            var total = 0.0;
            foreach (var w in weights) total += w;
            var roll = rng.Next() * total;
            for (var i = 0; i < items.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0.0) return items[i];
            }
            return items[items.Length - 1];
        }

        public sealed class Spec
        {
            public string Shape, Sponge, Icing, Topping;
            public int Candles;
            public override bool Equals(object o) => o is Spec s && s.Shape == Shape && s.Sponge == Sponge && s.Icing == Icing && s.Topping == Topping && s.Candles == Candles;
            public override int GetHashCode() => (Shape + Sponge + Icing + Topping).GetHashCode() ^ Candles;
            public override string ToString() => $"{Shape}/{Sponge}/{Icing}/{Topping}/{Candles}";
        }

        /// <summary>Seeded order generator with the §C9.4 weights (verbatim).</summary>
        public static Spec MakeTicket(GoobyRng rng, int serves)
        {
            var complex = serves >= Cake.ComplexAfterServes;
            var shape = Shapes[Math.Min(Shapes.Length - 1, (int)Math.Floor(rng.Next() * Shapes.Length))];
            var sponge = Sponges[Math.Min(Sponges.Length - 1, (int)Math.Floor(rng.Next() * Sponges.Length))];
            var icing = WeightedPick(rng, Icings, complex ? new[] { 3.0, 3.0, 3.0, 2.0 } : new[] { 1.0, 1.0, 1.0, 0.0 });
            var topping = WeightedPick(rng, Toppings, new[] { 3.0, 3.0, 3.0, 2.0 });
            var candles = WeightedPick(rng, new[] { 0, 1, 2, 3, 4 }, complex ? new[] { 2.0, 3.0, 3.0, 2.0, 1.0 } : new[] { 4.0, 3.0, 2.0, 0.0, 0.0 });
            return new Spec { Shape = shape, Sponge = sponge, Icing = icing, Topping = topping, Candles = candles };
        }

        public sealed class Pan
        {
            public int Id;
            public string Shape;
            public double S;
            public string Sponge, Bake, Icing, Topping;
            public double BakeT;
            public bool InOven, PerfectCounted;
            public int Candles;
        }

        public static int WrongCount(Pan cake, Spec ticket)
        {
            var wrong = 0;
            if (cake.Shape != ticket.Shape) wrong++;
            if ((cake.Sponge ?? "") != ticket.Sponge) wrong++;
            if ((cake.Icing ?? "none") != ticket.Icing) wrong++;
            if ((cake.Topping ?? "none") != ticket.Topping) wrong++;
            if (cake.Candles != ticket.Candles) wrong++;
            if (cake.Bake == "singed") wrong++;
            return wrong;
        }

        public static string ServeOutcome(int wrong) => wrong == 0 ? "perfect" : wrong == 1 ? "oneWrong" : "rejected";

        public struct ServeScore
        {
            public string Outcome;
            public int Points, Base, ComboBonus, SpeedBonus, ComboAfter;
        }

        public static ServeScore ScoreServe(int wrong, int combo, double patienceFrac)
        {
            var outcome = ServeOutcome(wrong);
            var rejected = outcome == "rejected";
            var b = outcome == "perfect" ? Cake.PerfectPts : outcome == "oneWrong" ? Cake.OneWrongPts : Cake.RejectPts;
            var comboBonus = rejected ? 0 : Math.Min(Cake.ComboCap, Cake.ComboStep * Math.Max(0, combo));
            var speedBonus = !rejected && patienceFrac >= Cake.SpeedBonusMinFrac ? Cake.SpeedBonusPts : 0;
            return new ServeScore { Outcome = outcome, Points = b + comboBonus + speedBonus, Base = b, ComboBonus = comboBonus, SpeedBonus = speedBonus, ComboAfter = rejected ? 0 : combo + 1 };
        }

        public sealed class Ticket
        {
            public int Id;
            public Spec Spec;
            public double Remain, Patience;
        }

        /// <summary>Best matching open order (fewest mistakes, ties → oldest).</summary>
        public static int BestTicketIndex(Pan cake, IList<Ticket> tickets)
        {
            var best = -1;
            var bestWrong = 1 << 30;
            for (var i = 0; i < tickets.Count; i++)
            {
                var w = WrongCount(cake, tickets[i].Spec);
                if (w < bestWrong) { best = i; bestWrong = w; }
            }
            return best;
        }

        public sealed class Drop
        {
            public string Station, Kind, Value;
            public double NozzleS, FiredAt, ImpactAt;
        }

        public sealed class Splat
        {
            public double S, Ttl;
        }

        public struct Ev
        {
            public string Type, Station, Reason, Shape, Kind, Value, Outcome, Bake, Result;
            public int PanId, TicketId, Points, Base, ComboBonus, SpeedBonus, Wrong;
            public double S, ImpactAt, PatienceFrac, BakeT, Patience;
            public bool Auto;
            public Spec Spec;
        }

        public struct Input
        {
            public int Belt;
            public string Press, SpawnShape;
            public bool Ship;
        }

        public sealed class Line
        {
            public string Mode;
            public Tune Tune;
            public GoobyRng Rng;
            public double T;
            public int Score, Combo, Serves, CakesServed, PerfectCakes, Rejected, Expired, PerfectBakes, SplatCount, BuzzCount, Trashed;
            public readonly List<Ticket> Tickets = new List<Ticket>();
            public readonly List<Pan> Pans = new List<Pan>();
            public readonly List<Drop> Drops = new List<Drop>();
            public readonly List<Splat> Splats = new List<Splat>();
            public readonly Dictionary<string, double> Lockouts = new Dictionary<string, double>();
            public double BeltV, OrderT;
            public int NextTicketId = 1, NextPanId = 1;
            public bool Over;
        }

        public static Line CreateLine(GoobyRng rng, string difficulty = "normal")
        {
            var tune = ApplyDifficulty(Cake, difficulty);
            return new Line { Mode = tune.Mode, Tune = tune, Rng = rng };
        }

        public static (bool ok, string reason) CanSpawn(Line line)
        {
            var tune = line.Tune;
            if (line.Pans.Count >= PanCapAt(line.Serves, tune)) return (false, "cap");
            foreach (var pan in line.Pans) if (Math.Abs(pan.S - tune.SpawnS) < tune.SpawnClearM) return (false, "blocked");
            return (true, "");
        }

        /// <summary>
        /// purble_place.gd needed_station: what the OLDEST pan needs next for the first ticket — "spawn" (tickets but no
        /// pan), a nozzle id, "ship" for a finished cake, "" while it is still unbaked or nothing is ordered. The host
        /// puts the gold frame on that button.
        /// </summary>
        public static string NeededStation(Line line)
        {
            if (line.Pans.Count == 0) return line.Tickets.Count > 0 ? "spawn" : "";
            if (line.Tickets.Count == 0) return "";
            var pan = line.Pans[0];
            foreach (var p in line.Pans) if (p.Id < pan.Id) pan = p;
            var spec = line.Tickets[0].Spec;
            if (pan.Sponge == null) return "teig." + spec.Sponge;
            if (pan.Bake == null) return "";
            if (spec.Icing != "none" && pan.Icing == null) return "guss." + spec.Icing;
            if (spec.Topping != "none" && pan.Topping == null) return "deko." + spec.Topping;
            if (pan.Candles < spec.Candles) return "kerzen";
            return "ship";
        }

        /// <summary>purble_place.gd _ship_armed: a baked pan stands in the shipping hatch.</summary>
        public static bool ShipArmed(Line line)
        {
            foreach (var pan in line.Pans)
                if (Math.Abs(pan.S - line.Tune.ShipS) <= line.Tune.ShipHalfM + Eps && pan.Bake != null) return true;
            return false;
        }

        private static void AddScore(Line line, int points) => line.Score = Math.Max(0, line.Score + points);

        private static void PushBuzz(Line line, List<Ev> events, string station, int panId, string reason)
        {
            line.BuzzCount += 1;
            events.Add(new Ev { Type = "buzz", Station = station, PanId = panId, Reason = reason });
        }

        private static void CheckEndlessOver(Line line)
        {
            if (line.Tune.Endless && line.Rejected + line.Expired >= line.Tune.EndlessFailCount) line.Over = true;
        }

        private static void TrySpawn(Line line, string shape, List<Ev> events)
        {
            if (Array.IndexOf(Shapes, shape) < 0) return;
            var gate = CanSpawn(line);
            if (!gate.ok) { PushBuzz(line, events, "spawn", 0, gate.reason); return; }
            var pan = new Pan { Id = line.NextPanId, Shape = shape, S = line.Tune.SpawnS };
            line.NextPanId += 1;
            line.Pans.Add(pan);
            events.Add(new Ev { Type = "panSpawn", PanId = pan.Id, Shape = shape, S = pan.S });
        }

        private static void TryPress(Line line, string stationId, List<Ev> events)
        {
            var st = StationById(stationId);
            if (st == null || !st.Drop) return;
            if (line.Lockouts.TryGetValue(stationId, out var lo) && lo > 0.0) return;
            var tune = line.Tune;
            line.Lockouts[stationId] = st.Kind == "kerzen" ? tune.CandleGapSec : tune.LockoutSec;
            var drop = new Drop { Station = stationId, Kind = st.Kind, Value = st.Value, NozzleS = st.S, FiredAt = line.T, ImpactAt = line.T + tune.FallSec };
            line.Drops.Add(drop);
            events.Add(new Ev { Type = "drop", Station = stationId, Kind = st.Kind, Value = st.Value, S = st.S, ImpactAt = drop.ImpactAt });
        }

        private static void ServePan(Line line, Pan pan, List<Ev> events)
        {
            var idx = BestTicketIndex(pan, line.Tickets);
            var ticket = line.Tickets[idx];
            var wrong = WrongCount(pan, ticket.Spec);
            var frac = ticket.Remain / ticket.Patience;
            var r = ScoreServe(wrong, line.Combo, frac);
            AddScore(line, r.Points);
            line.Combo = r.ComboAfter;
            line.Serves += 1;
            line.CakesServed += 1;
            if (r.Outcome == "perfect") line.PerfectCakes += 1;
            if (r.Outcome == "rejected") line.Rejected += 1;
            line.Tickets.RemoveAt(idx);
            line.Pans.Remove(pan);
            events.Add(new Ev
            {
                Type = r.Outcome == "rejected" ? "reject" : "serve", Outcome = r.Outcome, Points = r.Points, Base = r.Base,
                ComboBonus = r.ComboBonus, SpeedBonus = r.SpeedBonus, Wrong = wrong, PatienceFrac = frac, TicketId = ticket.Id,
                PanId = pan.Id, Bake = pan.Bake, Spec = ticket.Spec,
            });
            CheckEndlessOver(line);
        }

        private static void TryShip(Line line, List<Ev> events)
        {
            var tune = line.Tune;
            var shipS = tune.ShipS;
            var inZone = line.Pans.Where(p => Math.Abs(p.S - shipS) <= tune.ShipHalfM + Eps).ToList();
            inZone.Sort((a, b) =>
            {
                var da = Math.Abs(a.S - shipS);
                var db = Math.Abs(b.S - shipS);
                if (da != db) return da.CompareTo(db);
                return a.Id.CompareTo(b.Id);
            });
            var baked = inZone.Where(p => p.Bake != null).ToList();
            if (baked.Count == 0)
            {
                PushBuzz(line, events, "versand", inZone.Count > 0 ? inZone[0].Id : 0, inZone.Count > 0 ? "raw" : "empty");
                return;
            }
            if (line.Tickets.Count == 0) { PushBuzz(line, events, "versand", baked[0].Id, "noTicket"); return; }
            ServePan(line, baked[0], events);
        }

        /// <summary>Commit a bake result; the points are the DIFFERENCE to the last committed result (path-independent).</summary>
        private static void CommitBake(Line line, Pan pan, string result, bool auto, List<Ev> events)
        {
            var previous = pan.Bake ?? "";
            var delta = BakePoints(result) - BakePoints(previous);
            if (result == "perfect" && previous != "perfect" && !pan.PerfectCounted)
            {
                line.PerfectBakes += 1;
                pan.PerfectCounted = true;
            }
            pan.Bake = result;
            if (delta != 0) AddScore(line, delta);
            events.Add(new Ev { Type = "bakeCommit", PanId = pan.Id, Result = result, Points = delta, BakeT = pan.BakeT, Auto = auto });
        }

        private static void OvenStep(Line line, Pan pan, double s0, double s1, double segDt, List<Ev> events)
        {
            var tune = line.Tune;
            if (pan.Sponge == null || pan.Bake == "singed") { pan.InOven = false; return; }
            var startS = tune.OvenStartS;
            var endS = tune.OvenEndS;
            var inside1 = s1 >= startS && s1 <= endS;
            double frac;
            if (s0 == s1) frac = inside1 ? 1.0 : 0.0;
            else
            {
                var lo = Math.Min(s0, s1);
                var hi = Math.Max(s0, s1);
                var overlap = Math.Min(hi, endS) - Math.Max(lo, startS);
                frac = overlap > 0.0 ? Math.Min(1.0, overlap / (hi - lo)) : 0.0;
            }
            var wasIn = pan.InOven;
            if (!wasIn && frac > 0.0) events.Add(new Ev { Type = "bakeStart", PanId = pan.Id, BakeT = pan.BakeT });
            if (frac > 0.0)
            {
                pan.BakeT += frac * segDt;
                if (pan.BakeT >= tune.SingeSec)
                {
                    pan.BakeT = tune.SingeSec;
                    CommitBake(line, pan, "singed", true, events);
                    pan.InOven = false;
                    return;
                }
            }
            if ((wasIn || frac > 0.0) && !inside1) CommitBake(line, pan, BakeResultAt(pan.BakeT, tune), false, events);
            pan.InOven = inside1;
        }

        /// <summary>Closed-form belt motion under the 6-m/s² ramp (trapezoid profile).</summary>
        public static (double disp, double v1) BeltAdvance(double v0, double vt, double slew, double dur)
        {
            if (v0 == vt) return (v0 * dur, v0);
            var dir = vt > v0 ? 1.0 : -1.0;
            var tRamp = Math.Abs(vt - v0) / slew;
            if (dur < tRamp)
            {
                var v1 = v0 + dir * slew * dur;
                return ((v0 + v1) / 2.0 * dur, v1);
            }
            return ((v0 + vt) / 2.0 * tRamp + vt * (dur - tRamp), vt);
        }

        private static void MoveLine(Line line, double disp, double segDt, List<Ev> events)
        {
            var tune = line.Tune;
            for (var i = line.Splats.Count - 1; i >= 0; i--)
            {
                var sp = line.Splats[i];
                sp.S += disp;
                sp.Ttl -= segDt;
                if (sp.Ttl <= 0.0) line.Splats.RemoveAt(i);
            }
            for (var i = line.Pans.Count - 1; i >= 0; i--)
            {
                var pan = line.Pans[i];
                var s0 = pan.S;
                var s1 = Math.Min(tune.BeltLengthM, s0 + disp);
                pan.S = s1;
                OvenStep(line, pan, s0, s1, segDt, events);
                if (pan.S < 0.0)
                {
                    line.Pans.RemoveAt(i);
                    line.Trashed += 1;
                    events.Add(new Ev { Type = "trash", PanId = pan.Id });
                }
            }
        }

        private static void ResolveImpact(Line line, Drop drop, List<Ev> events)
        {
            var tune = line.Tune;
            var nozzle = drop.NozzleS;
            var candidates = line.Pans.Where(p => CatchWindow(p.S, nozzle, tune)).ToList();
            candidates.Sort((a, b) =>
            {
                var da = Math.Abs(a.S - nozzle);
                var db = Math.Abs(b.S - nozzle);
                if (da != db) return da.CompareTo(db);
                return a.Id.CompareTo(b.Id);
            });
            if (candidates.Count == 0)
            {
                AddScore(line, tune.SplatPts);
                line.SplatCount += 1;
                line.Splats.Add(new Splat { S = nozzle, Ttl = tune.SplatTtlSec });
                events.Add(new Ev { Type = "splat", Station = drop.Station, S = nozzle, Points = tune.SplatPts });
                return;
            }
            var pan = candidates[0];
            var kind = drop.Kind;
            bool legal;
            if (kind == "teig") legal = pan.Sponge == null;
            else if (kind == "guss") legal = pan.Bake != null && pan.Icing == null;
            else if (kind == "deko") legal = pan.Bake != null && pan.Topping == null;
            else legal = pan.Bake != null && pan.Candles < tune.MaxCandles;
            if (!legal) { PushBuzz(line, events, drop.Station, pan.Id, "illegal"); return; }
            if (kind == "teig") pan.Sponge = drop.Value;
            else if (kind == "guss") pan.Icing = drop.Value;
            else if (kind == "deko") pan.Topping = drop.Value;
            else pan.Candles += 1;
            events.Add(new Ev { Type = "catch", Station = drop.Station, Kind = kind, PanId = pan.Id, Value = kind == "kerzen" ? pan.Candles.ToString() : drop.Value });
        }

        private static void IntegrateLine(Line line, double dt, int dir, List<Ev> events)
        {
            var tune = line.Tune;
            var vt = dir > 0 ? tune.BeltFwdSpeed : dir < 0 ? -tune.BeltRevSpeed : 0.0;
            var t = line.T;
            var tEnd = line.T + dt;
            var guard = 0;
            while (t < tEnd - Eps && guard < 64)
            {
                guard++;
                var tNext = tEnd;
                foreach (var d in line.Drops) if (d.ImpactAt > t + Eps && d.ImpactAt < tNext) tNext = d.ImpactAt;
                var beltV = line.BeltV;
                if (beltV != 0.0 && vt * beltV < 0.0)
                {
                    var tz = t + Math.Abs(beltV) / tune.BeltSlew;
                    if (tz > t + Eps && tz < tNext) tNext = tz;
                }
                var segDt = tNext - t;
                var step = BeltAdvance(beltV, vt, tune.BeltSlew, segDt);
                MoveLine(line, step.disp, segDt, events);
                line.BeltV = step.v1;
                t = tNext;
                for (var i = line.Drops.Count - 1; i >= 0; i--)
                {
                    if (line.Drops[i].ImpactAt <= t + Eps)
                    {
                        var drop = line.Drops[i];
                        line.Drops.RemoveAt(i);
                        ResolveImpact(line, drop, events);
                    }
                }
            }
            line.T = tEnd;
        }

        private static void TickTickets(Line line, double dt, List<Ev> events)
        {
            var tune = line.Tune;
            line.OrderT -= dt;
            if (line.OrderT <= 0.0)
            {
                if (line.Tickets.Count < tune.MaxTickets)
                {
                    var spec = MakeTicket(line.Rng, line.Serves);
                    var patience = PatienceFor(line.Serves, tune.PatienceMult);
                    var ticket = new Ticket { Id = line.NextTicketId, Spec = spec, Remain = patience, Patience = patience };
                    line.NextTicketId += 1;
                    line.Tickets.Add(ticket);
                    line.OrderT = OrderIntervalAt(line.Serves, tune.OrderIntervalMinSec);
                    events.Add(new Ev { Type = "ticketNew", TicketId = ticket.Id, Spec = spec, Patience = patience });
                }
                else line.OrderT = 0.0;
            }
            for (var i = line.Tickets.Count - 1; i >= 0; i--)
            {
                var tk = line.Tickets[i];
                tk.Remain -= dt;
                if (tk.Remain <= 0.0)
                {
                    line.Tickets.RemoveAt(i);
                    AddScore(line, tune.ExpirePts);
                    line.Combo = 0;
                    line.Expired += 1;
                    events.Add(new Ev { Type = "expire", TicketId = tk.Id, Points = tune.ExpirePts });
                    CheckEndlessOver(line);
                }
            }
        }

        /// <summary>Advance the line by dt; returns this step's events.</summary>
        public static List<Ev> StepLine(Line line, double dt, Input input)
        {
            var events = new List<Ev>();
            if (line.Over || !(dt > 0.0)) return events;
            foreach (var id in line.Lockouts.Keys.ToList())
            {
                line.Lockouts[id] -= dt;
                if (line.Lockouts[id] <= 0.0) line.Lockouts.Remove(id);
            }
            if (!string.IsNullOrEmpty(input.SpawnShape)) TrySpawn(line, input.SpawnShape, events);
            if (!string.IsNullOrEmpty(input.Press)) TryPress(line, input.Press, events);
            if (input.Ship) TryShip(line, events);
            IntegrateLine(line, dt, Math.Sign(input.Belt), events);
            TickTickets(line, dt, events);
            return events;
        }

        public struct RoundResult
        {
            public int Score, CakesServed, PerfectCakes, Rejected, Expired, Serves, PerfectBakes, Splats, Trashed;
            public double TSec;
            public bool Over;
            public string Mode;
        }

        /// <summary>§G1.9 certificate run: line + bot at fixed 30 Hz.</summary>
        public static RoundResult SimulateRound(int seed, string difficulty = "normal", double durationSec = -1.0)
        {
            var line = CreateLine(new GoobyRng(seed), difficulty);
            var bot = new Bot(new GoobyRng(unchecked((uint)seed ^ 0x9E3779B9u)));
            const double dt = 1.0 / 30.0;
            var duration = durationSec < 0.0 ? (line.Tune.Endless ? 900.0 : line.Tune.DurationSec) : durationSec;
            var t = 0.0;
            while (t < duration && !line.Over)
            {
                StepLine(line, dt, bot.Plan(line, dt));
                t += dt;
            }
            return new RoundResult
            {
                Score = line.Score, CakesServed = line.CakesServed, PerfectCakes = line.PerfectCakes, Rejected = line.Rejected,
                Expired = line.Expired, Serves = line.Serves, PerfectBakes = line.PerfectBakes, Splats = line.SplatCount,
                Trashed = line.Trashed, TSec = line.T, Over = line.Over, Mode = line.Mode,
            };
        }

        /// <summary>
        /// §G1.9 autoplay (createBot): drives the belt like a person — spawn → park under the sponge nozzle and STOP → oven
        /// → wait for the meter → out → icing → topping → candles → ship. The error model (park jitter, wrong neighbour
        /// nozzle, oven early/late, a candle short) and the order of its rng() calls are binding.
        /// </summary>
        public sealed class Bot
        {
            private const double ArriveTolM = 0.05, ParkJitterM = 0.06, SloppyChance = 0.28, SloppyJitterM = 0.27;
            private const double ReactMinSec = 0.15, ReactMaxSec = 0.55, WrongChance = 0.2, ShapeWrongChance = 0.16;
            private const double OvenEarlyChance = 0.35, OvenLateChance = 0.11, OvenParkS = 2.85, OvenExitMeterSec = 2.4;
            private const double CandleShortChance = 0.18, HesitateMinSec = 0.8, HesitateMaxSec = 3.0;

            private sealed class DropRoll { public bool Wrong; public double Park, React; }
            private sealed class Oven { public string Mode; public double? ExitMeter; }
            private sealed class PlanRoll { public DropRoll Teig, Guss, Deko; public int KerzenShort; public double KerzenPark, ShipPark; public Oven Oven; }
            private sealed class Cfg { public string StationId; public double Target, React; }

            private readonly GoobyRng _rng;
            private int _panId;
            private Spec _spec;
            private PlanRoll _plan;
            private string _stage = "", _phase = "idle";
            private Cfg _cfg;
            private double _reactT, _awaitUntil, _hesitateT;
            private bool _spawnIssued, _sideSpawned;

            public Bot(GoobyRng rng) { _rng = rng; }

            public Input Plan(Line line, double dt)
            {
                var input = new Input();
                if (line.Over) return input;
                if (_hesitateT > 0.0) { _hesitateT -= dt; return input; }
                var pan = ResolvePan(line);
                if (pan == null) return OpenPan(line, input);
                var stage = NextStage(pan);
                if (stage != _stage)
                {
                    _stage = stage;
                    _phase = "drive";
                    _cfg = stage == "oven" ? null : MakeCfg(stage);
                }
                return stage == "oven" ? OvenPlan(line, pan, input) : StationPlan(line, pan, input, dt);
            }

            private Pan ResolvePan(Line line)
            {
                var pans = line.Pans;
                if (_spawnIssued)
                {
                    _spawnIssued = false;
                    Pan newest = null;
                    foreach (var p in pans) if (newest == null || p.Id > newest.Id) newest = p;
                    if (newest != null) AdoptPan(line, newest);
                }
                foreach (var p in pans) if (p.Id == _panId) return p;
                Pan spare = null;
                foreach (var p in pans) if (spare == null || p.Id < spare.Id) spare = p;
                if (spare != null) AdoptPan(line, spare);
                return spare;
            }

            private Input OpenPan(Line line, Input input)
            {
                if (line.Tickets.Count == 0 || !CanSpawn(line).ok) return input;
                input.SpawnShape = PickShape(line.Tickets[0].Spec);
                _spawnIssued = true;
                return input;
            }

            private Input StationPlan(Line line, Pan pan, Input input, double dt)
            {
                if (_phase == "drive")
                {
                    var dir = DriveToward(line, pan, _cfg.Target);
                    if (dir != 0 || Math.Abs(line.BeltV) > 0.001) { input.Belt = dir; return input; }
                    _phase = "react";
                    _reactT = _cfg.React;
                    return input;
                }
                if (_phase == "react")
                {
                    _reactT -= dt;
                    if (_reactT > 0.0) return input;
                    return Fire(line, input);
                }
                if (_phase == "await") return AwaitPlan(line, input);
                _phase = "drive";
                return input;
            }

            private Input AwaitPlan(Line line, Input input)
            {
                if (line.T < _awaitUntil) return input;
                var b = string.IsNullOrEmpty(_cfg.StationId) ? Cake.ShipS : StationById(_cfg.StationId).S;
                _cfg.Target = b + CarefulJitter();
                _cfg.React = ReactSec();
                _phase = "drive";
                return input;
            }

            private Input Fire(Line line, Input input)
            {
                if (_stage == "ship")
                {
                    if (line.Tickets.Count == 0) return input;
                    input.Ship = true;
                    _panId = 0;
                    _stage = "";
                    _phase = "idle";
                    _hesitateT = HesitateSec();
                    return input;
                }
                var stationId = _cfg.StationId;
                if (line.Lockouts.TryGetValue(stationId, out var lo) && lo > 0.0) return input;
                input.Press = stationId;
                _phase = "await";
                _awaitUntil = line.T + Cake.FallSec + 0.1;
                return input;
            }

            private Input OvenPlan(Line line, Pan pan, Input input)
            {
                var oven = _plan.Oven;
                if (_phase == "wait")
                {
                    if (!_sideSpawned && line.Tickets.Count >= 2 && CanSpawn(line).ok)
                    {
                        _sideSpawned = true;
                        input.SpawnShape = PickShape(line.Tickets[1].Spec);
                        return input;
                    }
                    if (oven.Mode == "late") return input;
                    var exitMeter = oven.ExitMeter ?? OvenExitMeterSec;
                    if (pan.BakeT >= exitMeter) _phase = "exit";
                    return input;
                }
                if (_phase == "exit") { input.Belt = 1; return input; }
                var dir = DriveToward(line, pan, OvenParkS);
                if (dir != 0 || Math.Abs(line.BeltV) > 0.001) { input.Belt = dir; return input; }
                _phase = "wait";
                return input;
            }

            private static int DriveToward(Line line, Pan pan, double target)
            {
                var err = target - pan.S;
                if (Math.Abs(err) <= ArriveTolM) return 0;
                var v = line.BeltV;
                if (v != 0.0 && (v > 0.0) == (err > 0.0))
                {
                    var stopDist = v * v / (2.0 * Cake.BeltSlew);
                    if (stopDist >= Math.Abs(err)) return 0;
                }
                return err > 0.0 ? 1 : -1;
            }

            private string NextStage(Pan pan)
            {
                var spec = _spec ?? new Spec { Icing = "none", Topping = "none", Candles = 0 };
                if (pan.Sponge == null) return "teig";
                if (pan.Bake == null) return "oven";
                if (spec.Icing != "none" && pan.Icing == null) return "guss";
                if (spec.Topping != "none" && pan.Topping == null) return "deko";
                var want = Math.Max(0, spec.Candles - _plan.KerzenShort);
                if (pan.Candles < want) return "kerzen";
                return "ship";
            }

            private Cfg MakeCfg(string stage)
            {
                if (stage == "ship") return new Cfg { StationId = "", Target = Cake.ShipS + _plan.ShipPark, React = ReactSec() };
                if (stage == "kerzen") return new Cfg { StationId = "kerzen", Target = StationById("kerzen").S + _plan.KerzenPark, React = ReactSec() };
                var roll = stage == "teig" ? _plan.Teig : stage == "guss" ? _plan.Guss : _plan.Deko;
                string value;
                if (stage == "teig") value = roll.Wrong ? WrongPick(Sponges, _spec.Sponge) : _spec.Sponge;
                else if (stage == "guss") value = roll.Wrong ? WrongPick(Icings.Take(3).ToArray(), _spec.Icing) : _spec.Icing;
                else value = roll.Wrong ? WrongPick(Toppings.Take(3).ToArray(), _spec.Topping) : _spec.Topping;
                var stationId = stage + "." + value;
                return new Cfg { StationId = stationId, Target = StationById(stationId).S + roll.Park, React = roll.React };
            }

            private void AdoptPan(Line line, Pan pan)
            {
                _panId = pan.Id;
                if (line.Tickets.Count > 0) _spec = line.Tickets[0].Spec;
                _plan = RollPlan();
                _stage = "";
                _phase = "drive";
                _sideSpawned = false;
            }

            private PlanRoll RollPlan()
            {
                var teig = RollDrop();
                var guss = RollDrop();
                var deko = RollDrop();
                var kerzenShort = _rng.Next() < CandleShortChance ? 1 : 0;
                var kerzenPark = ParkJitter();
                Oven oven;
                if (_rng.Next() < OvenLateChance) oven = new Oven { Mode = "late" };
                else if (_rng.Next() < OvenEarlyChance) oven = new Oven { Mode = "early", ExitMeter = 1.2 + _rng.Next() * 0.85 };
                else oven = new Oven { Mode = "green", ExitMeter = OvenExitMeterSec + _rng.Next() * 0.15 };
                var shipPark = (_rng.Next() * 2.0 - 1.0) * 0.1;
                return new PlanRoll { Teig = teig, Guss = guss, Deko = deko, KerzenShort = kerzenShort, KerzenPark = kerzenPark, Oven = oven, ShipPark = shipPark };
            }

            private DropRoll RollDrop()
            {
                var wrong = _rng.Next() < WrongChance;
                var park = ParkJitter();
                var react = ReactSec();
                return new DropRoll { Wrong = wrong, Park = park, React = react };
            }

            private string PickShape(Spec spec)
            {
                if (_rng.Next() < ShapeWrongChance)
                {
                    var pool = Shapes.Where(s => s != spec.Shape).ToArray();
                    return pool[Math.Min(pool.Length - 1, (int)Math.Floor(_rng.Next() * pool.Length))];
                }
                return spec.Shape;
            }

            private string WrongPick(string[] pool, string correct)
            {
                var others = pool.Where(v => v != correct).ToArray();
                return others[Math.Min(others.Length - 1, (int)Math.Floor(_rng.Next() * others.Length))];
            }

            private double ReactSec() => ReactMinSec + _rng.Next() * (ReactMaxSec - ReactMinSec);
            private double HesitateSec() => HesitateMinSec + _rng.Next() * (HesitateMaxSec - HesitateMinSec);

            private double ParkJitter()
            {
                if (_rng.Next() < SloppyChance) return (_rng.Next() * 2.0 - 1.0) * SloppyJitterM;
                return (_rng.Next() * 2.0 - 1.0) * ParkJitterM;
            }

            private double CarefulJitter() => (_rng.Next() * 2.0 - 1.0) * ParkJitterM;
        }
    }
}
