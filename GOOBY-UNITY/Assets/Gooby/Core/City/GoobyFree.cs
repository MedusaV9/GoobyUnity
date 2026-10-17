using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// GOOBY FREE, the duty-free at the gate (port of flughafen.gd W13B + W19): exclusive space deco (gfree_*, catalog
    /// price 0, so no other shop sells it) and travel snacks at airport prices. In Godot it opens while the taxi to a
    /// booked trip is on its way; Unity books and boards in one go, so the gate stays open for <see cref="BoardingMs"/>
    /// after the booking — the boarding pass sends Gooby on board. The 24-hour souvenir window after a pickup opens it
    /// too, with the two souvenirs Gooby did not bring (<see cref="Urlaub.FensterSortiment"/>).
    /// </summary>
    public static class GoobyFree
    {
        public const long BoardingMs = 20 * 60_000L;
        public const string Ok = "ok", Zu = "geschlossen", Pleite = "pleite", Voll = "lager_voll", Unbekannt = "unbekannt";
        public const string Reason = "gooby_free";
        public const string TypMoebel = "moebel", TypSnack = "snack", TypMitbringsel = "mitbringsel";

        public sealed class Ware
        {
            public string Id, Typ, ZielId = "", MitbringselTyp = "";
            public int Preis;
        }

        /// <summary>flughafen.gd GOOBY_FREE_SORTIMENT: airport prices on purpose (sandwich 16 → 24, cookie 8 → 13).</summary>
        public static readonly IReadOnlyList<Ware> Sortiment = new[]
        {
            new Ware { Id = "gfree_shuttle", Typ = TypMoebel, Preis = 149 },
            new Ware { Id = "gfree_flitzer", Typ = TypMoebel, Preis = 129 },
            new Ware { Id = "gfree_meteorit", Typ = TypMoebel, Preis = 99 },
            new Ware { Id = "gfree_sternmoehre", Typ = TypMoebel, Preis = 88 },
            new Ware { Id = "gfree_mondstein", Typ = TypMoebel, Preis = 66 },
            new Ware { Id = "sandwich", Typ = TypSnack, Preis = 24 },
            new Ware { Id = "cookie", Typ = TypSnack, Preis = 13 },
        };

        /// <summary>Gooby waits at the gate: a trip is booked (phase away) and boarding has not closed yet.</summary>
        public static bool AmGate(JObject state, long nowMs)
        {
            var v = Vacation.SliceOf(state);
            if (J.Str(v["phase"]) != Vacation.PhaseAway) return false;
            var booked = J.Long(v["bookedAt"]);
            return booked > 0 && nowMs >= booked && nowMs < booked + BoardingMs;
        }

        /// <summary>Minutes (rounded up) until boarding closes; 0 when Gooby is not at the gate.</summary>
        public static int BoardingRestMin(JObject state, long nowMs)
        {
            if (!AmGate(state, nowMs)) return 0;
            var booked = J.Long(Vacation.SliceOf(state)["bookedAt"]);
            return (int)Math.Ceiling((booked + BoardingMs - nowMs) / 60_000.0);
        }

        public static bool Offen(JObject state, long nowMs) => AmGate(state, nowMs) || Urlaub.FensterOffen(state, nowMs);

        /// <summary>What the shelf offers now: the souvenir window's pieces first (W19), then the gate assortment.</summary>
        public static List<Ware> Angebot(JObject state, long nowMs)
        {
            var outp = Urlaub.FensterSortiment(state, nowMs)
                .Select(w => new Ware { Id = w.Id, Typ = TypMitbringsel, ZielId = w.ZielId, MitbringselTyp = w.Typ, Preis = w.Preis }).ToList();
            if (AmGate(state, nowMs)) outp.AddRange(Sortiment);
            return outp;
        }

        /// <summary>
        /// Buys one piece, atomically: coins out AND the piece in, or nothing. Deco goes straight into the storage (it
        /// travels home in the luggage, no delivery van), snacks into inventory.food, souvenirs into inventory.items.
        /// </summary>
        public static string Kaufe(JObject state, string wareId, long nowMs)
        {
            if (state == null) return Zu;
            var ware = Angebot(state, nowMs).FirstOrDefault(w => w.Id == wareId);
            if (ware == null) return Offen(state, nowMs) ? Unbekannt : Zu;
            if (ware.Typ == TypMitbringsel)
            {
                var r = Urlaub.Kaufe(state, ware.Id, nowMs);
                return r == Urlaub.KaufOk ? Ok : r == Urlaub.KaufPleite ? Pleite : Zu;
            }
            if (J.Int(J.Path(state, "economy", "coins")) < ware.Preis) return Pleite;
            if (ware.Typ == TypMoebel)
            {
                var storage = J.Path(state, HomeState.SliceId, "storage") as JArray ?? new JArray();
                var capacity = J.Int(J.Path(state, HomeState.SliceId, "storageCapacity"), ShedLogic.BasisKapazitaet);
                if (!StorageLogic.CanAdd(storage, ware.Id, HomeState.DefsAsJson(), capacity)) return Voll;
            }
            if (!Economy.Spend(J.EnsureDict(state, "economy"), ware.Preis, Reason)) return Pleite;
            if (ware.Typ == TypMoebel)
            {
                var home = J.EnsureDict(state, HomeState.SliceId);
                if (!(home["storage"] is JArray storage)) home["storage"] = storage = new JArray();
                StorageLogic.Add(storage, ware.Id);
            }
            else
            {
                var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                food[ware.Id] = J.Int(food[ware.Id]) + 1;
            }
            return Ok;
        }

        // ── the boarding pass ──

        public sealed class Bordkarte
        {
            public string ZielId = "", Von = "SON", Nach = "", Flug = "", Gate = "", Sitz = "";
            public long GebuchtAt, BoardingBis;
        }

        /// <summary>Three-letter codes on the pass (Flughafen Sonnental → the destination's German name).</summary>
        public static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>
        {
            ["beach"] = "GLM", ["harbor"] = "HAF", ["meadowTrip"] = "BLW", ["spookGarden"] = "SPK", ["bakery"] = "BCK",
            ["bigCity"] = "RSS", ["nightSky"] = "NHT", ["toyRoom"] = "SPZ", ["space"] = "ALL",
        };

        /// <summary>The pass of the booked trip (null without one): flight, gate and seat follow from the booking, never from chance.</summary>
        public static Bordkarte Karte(JObject state)
        {
            var v = Vacation.SliceOf(state);
            if (J.Str(v["phase"]) == Vacation.PhaseNone) return null;
            var ziel = J.Str(v["destId"]);
            var booked = J.Long(v["bookedAt"]);
            if (ziel.Length == 0 || booked <= 0) return null;
            var h = Hash(ziel + "|" + booked);
            return new Bordkarte
            {
                ZielId = ziel,
                Nach = Codes.TryGetValue(ziel, out var code) ? code : ziel.Substring(0, Math.Min(3, ziel.Length)).ToUpperInvariant(),
                Flug = "GB " + (100 + h % 900),
                Gate = "ABC"[(int)(h / 900 % 3)] + ((h / 2700) % 9 + 1).ToString(),
                Sitz = ((h / 24300) % 24 + 1) + "ABCDEF"[(int)(h / 583200 % 6)].ToString(),
                GebuchtAt = booked,
                BoardingBis = booked + BoardingMs,
            };
        }

        /// <summary>FNV-1a over the text (the same booking always prints the same pass).</summary>
        private static uint Hash(string text)
        {
            var h = 2166136261u;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(text)) { h ^= b; h *= 16777619u; }
            return h;
        }
    }
}
