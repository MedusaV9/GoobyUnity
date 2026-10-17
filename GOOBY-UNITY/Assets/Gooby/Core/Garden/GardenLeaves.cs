using System;
using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    /// <summary>
    /// Leaves ("Blätter"), the second pill of boards 05/08 — a currency of the new look; the Godot game has none.
    /// Every day a few fallen leaves lie on the garden lawn (many in autumn, few in winter, now and then a golden one
    /// worth <see cref="GoldenValue"/>), picked by a tap or by Gooby walking over them; caring for the beds adds a
    /// capped handful. They buy garden things at the leaf exchange (<see cref="Offers"/>), which go to the storage
    /// like any purchase — shops still take coins only. Slice "leaves": {v, count, total, day, picked[], care}.
    /// </summary>
    public static class GardenLeaves
    {
        public const string SliceId = "leaves";
        public const int GoldenValue = 5;
        public const int CareCapPerDay = 12;
        public const long SpotSeed = 0x1EAF;

        /// <summary>Candidates drawn per day; the first free ones become the day's spots (stable ids "b&lt;index&gt;").</summary>
        public const int Candidates = 80;

        public const string ResultOk = "";
        public const string ResultUnknown = "unknown";
        public const string ResultTooFew = "too_few";
        public const string ResultStorageFull = "storage_full";

        public readonly struct Spot
        {
            public readonly string Id;
            /// <summary>Grid units of the garden room (x east, y south, fractional — the leaf's centre).</summary>
            public readonly double X, Y;
            public readonly bool Golden;
            /// <summary>Colour slot 0..3 of the season's palette (the view picks the colours).</summary>
            public readonly int Tint;
            public readonly double YawDeg;

            public Spot(string id, double x, double y, bool golden, int tint, double yawDeg)
            {
                Id = id; X = x; Y = y; Golden = golden; Tint = tint; YawDeg = yawDeg;
            }

            public int Value => Golden ? GoldenValue : 1;
        }

        public readonly struct Offer
        {
            public readonly string ItemId;
            public readonly int Price;
            public Offer(string itemId, int price) { ItemId = itemId; Price = price; }
        }

        private static bool _registered;

        public static void RegisterSlice()
        {
            if (_registered) return;
            _registered = true;
            Save.SaveSchema.RegisterSlice(SliceId, DefaultSlice, NormalizeSlice);
        }

        public static JObject DefaultSlice() => new JObject
        {
            ["v"] = 1, ["count"] = 0, ["total"] = 0, ["day"] = "", ["picked"] = new JArray(), ["care"] = 0,
        };

        public static JObject NormalizeSlice(JToken raw)
        {
            var slice = raw as JObject ?? DefaultSlice();
            slice["v"] = 1;
            slice["count"] = Math.Max(0L, J.Long(slice["count"]));
            slice["total"] = Math.Max(J.Long(slice["count"]), J.Long(slice["total"]));
            slice["day"] = J.Str(slice["day"]);
            if (!(slice["picked"] is JArray)) slice["picked"] = new JArray();
            slice["care"] = Math.Max(0, J.Int(slice["care"]));
            return slice;
        }

        public static long Count(GameStateStore gs) => J.Long(J.Path(gs.State, SliceId, "count"));
        public static long Total(GameStateStore gs) => J.Long(J.Path(gs.State, SliceId, "total"));

        /// <summary>Leaves on the lawn per day: autumn (Sep–Nov) 9, winter (Dec–Feb) 3, spring 5, summer 6.</summary>
        public static int CountFor(string day)
        {
            var m = SoulWetter.MonatVon(day);
            if (m >= 9 && m <= 11) return 9;
            if (m == 12 || m <= 2) return 3;
            return m <= 5 ? 5 : 6;
        }

        /// <summary>The season's palette for the view: "herbst" | "winter" | "fruehling" | "sommer".</summary>
        public static string Season(string day)
        {
            var m = SoulWetter.MonatVon(day);
            if (m >= 9 && m <= 11) return "herbst";
            if (m == 12 || m <= 2) return "winter";
            return m <= 5 ? "fruehling" : "sommer";
        }

        /// <summary>Chance of one golden leaf among the day's leaves (higher in autumn).</summary>
        public static double GoldenChance(string day) => Season(day) == "herbst" ? 0.5 : 0.3;

        /// <summary>
        /// The day's leaves: <see cref="Candidates"/> deterministic draws over the lawn (one cell clear of the fence),
        /// the first <see cref="CountFor"/> that land on a free cell — <paramref name="free"/> says which cells are
        /// (no furniture, no bed, not Gooby's spot); two leaves never share a cell. Every draw consumes the same numbers,
        /// so a spot keeps its id whatever else is built.
        /// </summary>
        public static List<Spot> Spots(string day, int gridW, int gridH, Func<int, int, bool> free)
        {
            var n = CountFor(day);
            var rng = new GoobyRng(RanchWetter.TagesSeed(day ?? "", SpotSeed));
            var golden = rng.Next() < GoldenChance(day) ? rng.RangeI(0, n - 1) : -1;
            var spots = new List<Spot>(n);
            var taken = new HashSet<(int, int)>();
            for (var i = 0; i < Candidates && spots.Count < n; i++)
            {
                var cx = rng.RangeI(1, Math.Max(1, gridW - 2));
                var cy = rng.RangeI(1, Math.Max(1, gridH - 2));
                var jx = rng.RangeF(0.25, 0.75);
                var jy = rng.RangeF(0.25, 0.75);
                var tint = rng.RangeI(0, 3);
                var yaw = rng.RangeF(0.0, 360.0);
                if (taken.Contains((cx, cy)) || (free != null && !free(cx, cy))) continue;
                taken.Add((cx, cy));
                spots.Add(new Spot("b" + i, cx + jx, cy + jy, spots.Count == golden, tint, yaw));
            }
            return spots;
        }

        /// <summary>The ids already picked today (an empty list once the day has turned).</summary>
        public static HashSet<string> Picked(GameStateStore gs, string day)
        {
            var set = new HashSet<string>();
            var slice = J.Dict(gs.State[SliceId]);
            if (J.Str(slice["day"]) != day) return set;
            foreach (var t in slice["picked"] as JArray ?? new JArray()) set.Add(J.Str(t));
            return set;
        }

        /// <summary>Leaves still lying on the lawn today.</summary>
        public static List<Spot> Lying(GameStateStore gs, string day, int gridW, int gridH, Func<int, int, bool> free)
        {
            var picked = Picked(gs, day);
            var lying = new List<Spot>();
            if (picked.Count >= CountFor(day)) return lying;
            foreach (var s in Spots(day, gridW, gridH, free)) if (!picked.Contains(s.Id)) lying.Add(s);
            return lying;
        }

        /// <summary>
        /// Picks one of today's leaves: +1 (golden +5) once per id and day, at most <see cref="CountFor"/> picks a day
        /// (a leaf that slid in after building over another does not make the lawn endless). Returns the leaves gained.
        /// </summary>
        public static int Pick(GameStateStore gs, string day, Spot spot)
        {
            var gained = 0;
            gs.Update(state =>
            {
                var slice = SliceOf(state, day);
                var picked = (JArray)slice["picked"];
                foreach (var t in picked) if (J.Str(t) == spot.Id) return;
                if (picked.Count >= CountFor(day)) return;
                picked.Add(spot.Id);
                gained = spot.Value;
                Add(slice, gained);
            });
            if (gained > 0) gs.NotifySliceChanged(SliceId);
            return gained;
        }

        /// <summary>Leaves for caring for a bed — plant 1, water 1, harvest 2 — up to <see cref="CareCapPerDay"/> a day.</summary>
        public static int ForCare(GameStateStore gs, string kind)
        {
            var want = kind == "harvest" ? 2 : kind == "plant" || kind == "water" ? 1 : 0;
            if (want <= 0) return 0;
            var day = gs.Clock.LocalDay();
            var gained = 0;
            gs.Update(state =>
            {
                var slice = SliceOf(state, day);
                var care = J.Int(slice["care"]);
                gained = Math.Max(0, Math.Min(want, CareCapPerDay - care));
                if (gained <= 0) return;
                slice["care"] = care + gained;
                Add(slice, gained);
            });
            if (gained > 0) gs.NotifySliceChanged(SliceId);
            return gained;
        }

        public static bool Spend(GameStateStore gs, int amount)
        {
            if (amount <= 0 || Count(gs) < amount) return false;
            gs.Update(state =>
            {
                var slice = J.EnsureDict(state, SliceId);
                slice["count"] = J.Long(slice["count"]) - amount;
            });
            gs.NotifySliceChanged(SliceId);
            return true;
        }

        /// <summary>Board 05's "Alle" row first (bush, tulips, sapling, bench, stepping stone, lantern, crate), then more flowers and the fountain.</summary>
        public static readonly string[] OfferItems =
        {
            "gardenBush", "flowerRed", "flowerWhite", "treeSapling", "gardenBench", "pathStonesShort", "gardenLantern", "crateWood",
            "flowerYellow", "flowerPurple", "flowerBedPink", "mushroomRed", "treeStump", "potSmall", "gardenFountain",
        };

        /// <summary>A fifth of the coin price, at least two leaves.</summary>
        public static int PriceOf(FurnitureDef def) =>
            def == null ? 0 : Math.Max(2, (int)Math.Round(def.Preis / 5.0, MidpointRounding.AwayFromZero));

        /// <summary>What the leaf exchange offers (catalog items only — a missing id is skipped).</summary>
        public static List<Offer> Offers()
        {
            var list = new List<Offer>();
            foreach (var id in OfferItems)
            {
                var def = FurnitureCatalog.Def(id);
                if (def != null) list.Add(new Offer(id, PriceOf(def)));
            }
            return list;
        }

        /// <summary>Trades leaves for an offer: the item goes to the storage, the leaves only leave when it fits.</summary>
        public static string Buy(GameStateStore gs, string itemId)
        {
            var offer = Offers().Find(o => o.ItemId == itemId);
            if (offer.ItemId == null) return ResultUnknown;
            if (Count(gs) < offer.Price) return ResultTooFew;
            if (!HomeState.StoreItem(gs, itemId)) return ResultStorageFull;
            Spend(gs, offer.Price);
            return ResultOk;
        }

        private static void Add(JObject slice, int amount)
        {
            slice["count"] = J.Long(slice["count"]) + amount;
            slice["total"] = J.Long(slice["total"]) + amount;
        }

        /// <summary>The slice with today's day: a new day clears the picked leaves and the care counter.</summary>
        private static JObject SliceOf(JObject state, string day)
        {
            var slice = J.EnsureDict(state, SliceId);
            if (!(slice["picked"] is JArray)) slice["picked"] = new JArray();
            if (J.Str(slice["day"]) != day)
            {
                slice["day"] = day;
                slice["picked"] = new JArray();
                slice["care"] = 0;
            }
            return slice;
        }
    }
}
