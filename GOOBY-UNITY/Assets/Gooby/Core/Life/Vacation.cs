using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Vacation/airport phase machine — port of GOOBY/src/systems/vacation.js / scripts/logic/vacation.gd.
    /// Pure slice math; used by <see cref="Offline"/> catch-up and the live ticker.
    /// </summary>
    public static class Vacation
    {
        public const string PhaseNone = "none";
        public const string PhaseAway = "away";
        public const string PhaseReturnReady = "returnReady";
        public const string PhaseOverdue = "overdue";
        public static readonly string[] Phases = { PhaseNone, PhaseAway, PhaseReturnReady, PhaseOverdue };

        public const long MsPerDay = 86400000L;
        public const long PickupWindowMs = 86400000L;
        public const int TaxiFee = 60;
        public const double PickupStatFill = 100.0;
        public const long ErholungsBoostMs = 48L * 3_600_000L;
        public const double ErholungsDrainFaktor = 0.8;

        public struct Destination
        {
            public string Id;
            public int Price;
            public int Days;
            public int SouvenirCoins;
        }

        /// <summary>Catalog ids + prices (web data/vacations.js, frozen numbers). Order matters (9/9 → Weltengooby).</summary>
        public static readonly Destination[] Catalog =
        {
            new Destination { Id = "beach", Price = 180, Days = 3, SouvenirCoins = 30 },
            new Destination { Id = "meadowTrip", Price = 220, Days = 3, SouvenirCoins = 40 },
            new Destination { Id = "bigCity", Price = 280, Days = 4, SouvenirCoins = 55 },
            new Destination { Id = "space", Price = 350, Days = 4, SouvenirCoins = 70 },
            new Destination { Id = "harbor", Price = 200, Days = 3, SouvenirCoins = 35 },
            new Destination { Id = "spookGarden", Price = 240, Days = 3, SouvenirCoins = 45 },
            new Destination { Id = "bakery", Price = 260, Days = 3, SouvenirCoins = 50 },
            new Destination { Id = "nightSky", Price = 300, Days = 4, SouvenirCoins = 60 },
            new Destination { Id = "toyRoom", Price = 320, Days = 4, SouvenirCoins = 65 },
        };

        public static bool TryGetDestination(string id, out Destination dest)
        {
            foreach (var d in Catalog)
                if (d.Id == id) { dest = d; return true; }
            dest = default;
            return false;
        }

        /// <summary>Hook for the postcard archive processor (postkarten_logic.gd); null = no archive generation.</summary>
        public interface IPostcardArchive
        {
            /// <summary>Returns (archive, lastPostcardDayProcessed, added).</summary>
            (JArray archive, int lastDay, int added) ProcessUpTo(JObject vacationSlice, long nowMs);
        }

        public static IPostcardArchive PostcardArchive { get; set; }

        public static JObject DefaultSlice() => new JObject
        {
            ["phase"] = PhaseNone,
            ["destId"] = "",
            ["bookedAt"] = 0,
            ["returnAt"] = 0,
            ["pickupBy"] = 0,
            ["postcards"] = 0,
            ["trips"] = 0,
            ["archive"] = new JArray(),
            ["lastPostcardDayProcessed"] = 0,
            ["visited"] = new JObject(),
            ["erholtBis"] = 0,
            ["weltengoobyAt"] = 0,
            ["weltengoobyGefeiert"] = false,
            ["besuche"] = new JObject(),
            ["souvenirTag"] = 0,
            ["heimkehrZiel"] = "",
            ["heimkehrAbflug"] = 0,
            ["heimkehrAt"] = 0,
            ["heimkehrGefeiert"] = false,
        };

        /// <summary>Normalized copy of the vacation slice of a state (never mutates; junk → defaults).</summary>
        public static JObject SliceOf(JObject state)
        {
            var d = DefaultSlice();
            if (!(state?["vacation"] is JObject raw)) return d;
            var phase = J.Str(raw["phase"], null);
            // Same key order as DefaultSlice() so normalize() is byte-stable.
            d["phase"] = phase != null && Array.IndexOf(Phases, phase) >= 0 ? phase : PhaseNone;
            d["destId"] = J.Str(raw["destId"]);
            d["bookedAt"] = J.Num(raw["bookedAt"]);
            d["returnAt"] = J.Num(raw["returnAt"]);
            d["pickupBy"] = J.Num(raw["pickupBy"]);
            d["postcards"] = Math.Max(0, (int)Math.Floor(J.Num(raw["postcards"])));
            d["trips"] = Math.Max(0, (int)Math.Floor(J.Num(raw["trips"])));
            d["archive"] = raw["archive"] is JArray arr ? (JArray)arr.DeepClone() : new JArray();
            d["lastPostcardDayProcessed"] = Math.Max(0, (int)Math.Floor(J.Num(raw["lastPostcardDayProcessed"])));
            d["visited"] = NormalizeVisited(raw["visited"]);
            d["erholtBis"] = Math.Max(0L, (long)J.Num(raw["erholtBis"]));
            d["weltengoobyAt"] = Math.Max(0L, (long)J.Num(raw["weltengoobyAt"]));
            d["weltengoobyGefeiert"] = J.IsTrue(raw["weltengoobyGefeiert"]);
            d["besuche"] = NormalizeVisited(raw["besuche"]);
            d["souvenirTag"] = Math.Max(0, (int)J.Num(raw["souvenirTag"]));
            d["heimkehrZiel"] = J.Str(raw["heimkehrZiel"]);
            d["heimkehrAbflug"] = Math.Max(0L, (long)J.Num(raw["heimkehrAbflug"]));
            d["heimkehrAt"] = Math.Max(0L, (long)J.Num(raw["heimkehrAt"]));
            d["heimkehrGefeiert"] = J.IsTrue(raw["heimkehrGefeiert"]);
            return d;
        }

        public static bool IsAway(JObject state) => J.Str(SliceOf(state)["phase"]) != PhaseNone;

        public static string PhaseAt(JObject v, long nowMs)
        {
            if (v == null || !v.HasValues || J.Str(v["phase"]) == PhaseNone || !(J.Num(v["returnAt"]) > 0.0)) return PhaseNone;
            if (nowMs < J.Num(v["returnAt"])) return PhaseAway;
            if (nowMs < J.Num(v["pickupBy"])) return PhaseReturnReady;
            return PhaseOverdue;
        }

        /// <summary>Postcards due: one per FULL day away, capped at days − 1.</summary>
        public static int PostcardsDue(JObject v, long nowMs)
        {
            var booked = J.Num(v["bookedAt"]);
            var returnAt = J.Num(v["returnAt"]);
            if (J.Str(v["phase"]) == PhaseNone || !(booked > 0.0) || !(returnAt > booked)) return 0;
            var totalDays = (int)Math.Round((returnAt - booked) / MsPerDay, MidpointRounding.AwayFromZero);
            var maxCards = Math.Max(0, totalDays - 1);
            var fullDays = (int)Math.Floor((Math.Min(nowMs, returnAt) - booked) / MsPerDay);
            return Math.Max(0, Math.Min(fullDays, maxCards));
        }

        public static long RemainingMs(JObject state, long nowMs)
        {
            var v = SliceOf(state);
            var phase = J.Str(v["phase"]);
            if (phase == PhaseAway) return Math.Max(0L, (long)J.Num(v["returnAt"]) - nowMs);
            if (phase == PhaseReturnReady) return Math.Max(0L, (long)J.Num(v["pickupBy"]) - nowMs);
            return 0;
        }

        public sealed class VacationEvent
        {
            public string Type;
            public string DestId;
            public int N;
        }

        /// <summary>The pure engine tick: changes (or null) + ordered events; idempotent across any gap.</summary>
        public static (JObject changes, List<VacationEvent> events) Tick(JObject state, long nowMs)
        {
            var basePresent = state?["vacation"] is JObject;
            var v = SliceOf(state);
            var events = new List<VacationEvent>();
            var changed = !basePresent;
            if (J.Str(v["phase"]) == PhaseNone) return (changed ? v : null, events);
            var due = PostcardsDue(v, nowMs);
            var have = J.Int(v["postcards"]);
            if (due > have)
            {
                for (var n = have + 1; n <= due; n++)
                    events.Add(new VacationEvent { Type = "postcard", DestId = J.Str(v["destId"]), N = n });
                v["postcards"] = due;
                changed = true;
            }
            if (PostcardArchive != null)
            {
                var (archive, lastDay, added) = PostcardArchive.ProcessUpTo(v, nowMs);
                if (added > 0 || lastDay != J.Int(v["lastPostcardDayProcessed"]))
                {
                    v["archive"] = archive;
                    v["lastPostcardDayProcessed"] = lastDay;
                    changed = true;
                }
            }
            var phase = PhaseAt(v, nowMs);
            var cur = J.Str(v["phase"]);
            if (phase != cur)
            {
                if (cur == PhaseAway && phase != PhaseAway)
                    events.Add(new VacationEvent { Type = "returnReady", DestId = J.Str(v["destId"]) });
                if (phase == PhaseOverdue)
                    events.Add(new VacationEvent { Type = "overdue", DestId = J.Str(v["destId"]) });
                v["phase"] = phase;
                changed = true;
            }
            return (changed ? v : null, events);
        }

        public static bool ErholungsBoostAktiv(JObject v, long nowMs) => J.Num(v?["erholtBis"]) > nowMs;

        public static double EnergieDrainFaktor(JObject v, long nowMs) =>
            ErholungsBoostAktiv(v, nowMs) ? ErholungsDrainFaktor : 1.0;

        public static bool AlleZieleBesucht(JObject v) => NormalizeVisited(v?["visited"]).Count >= Catalog.Length;

        public static bool Weltengooby(JObject v) => J.Num(v?["weltengoobyAt"]) > 0.0;

        /// <summary>Whitelist-normalize a visited map: known ids with strict true only.</summary>
        public static JObject NormalizeVisited(JToken raw)
        {
            var o = new JObject();
            if (!(raw is JObject r)) return o;
            foreach (var d in Catalog)
                if (J.IsTrue(r[d.Id])) o[d.Id] = true;
            return o;
        }
    }
}
