using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Food catalog + pure feeding logic — port of scripts/logic/food_catalog.gd (web foods.js subset that the
    /// game can actually reach: starter fridge, REHWEI range, garden crops, Funkelpark stalls, treats).
    /// Deltas use the web stat keys verbatim; junk feeds weight + junkScore, favourites give the extra squeak.
    /// Unknown ids (hostile saves) fall back to a generic snack — feeding must NEVER crash.
    /// </summary>
    public static class FoodCatalog
    {
        public sealed class Food
        {
            public double Hunger, Fun, Energy, Hygiene;
            public bool Junk, Favorit;
        }

        /// <summary>Gooby is practically full → politely refuses (no food wasted).</summary>
        public const double SattAb = 99.5;

        private static Food F(double hunger = 0, double fun = 0, double energy = 0, double hygiene = 0, bool junk = false, bool favorit = false) =>
            new Food { Hunger = hunger, Fun = fun, Energy = energy, Hygiene = hygiene, Junk = junk, Favorit = favorit };

        public static readonly IReadOnlyDictionary<string, Food> Foods = new Dictionary<string, Food>
        {
            { "carrot", F(10, 2, favorit: true) }, { "apple", F(10, 1) }, { "cupcake", F(10, 9, hygiene: -1, junk: true) },
            { "banana", F(11) }, { "tomato", F(12, 1) }, { "strawberry", F(6, 6) }, { "cookie", F(5, 8, junk: true) },
            { "grapes", F(8, 5) }, { "chocolate", F(5, 9, junk: true) }, { "bread", F(18) }, { "corn", F(15, 2) },
            { "cheese", F(16, 2) }, { "watermelon", F(14, 4) }, { "croissant", F(14, 4, 2, -1) }, { "muffin", F(10, 8, junk: true) },
            { "salad", F(20, hygiene: 2) }, { "fries", F(12, 9, hygiene: -1, junk: true) }, { "sandwich", F(24, 3) },
            { "burger", F(40, 6) }, { "pizza", F(45, 8, hygiene: -2, junk: true) },
            { "donut-sprinkles", F(10, 10, junk: true) }, { "hot-dog", F(25, 4) }, { "pancakes", F(28, 6) },
            { "cottonCandy", F(5, 16, 2, -2, junk: true) }, { "softServe", F(7, 15, 4, -1, junk: true) }, { "waffle", F(22, 8, 3, -1) },
            { "tomate", F(12, 1) }, { "melone", F(14, 4) }, { "salat", F(20, hygiene: 2) }, { "pilz", F(12, 2, 1) },
            { "ananas", F(14, 7) }, { "chili", F(6, 8, 4) },
            { "ice-cream", F(6, 15, 5, junk: true) }, { "cake", F(30, 20, junk: true) }, { "pumpkin", F(26, 4) },
            { "sundae", F(7, 14, 3, junk: true) }, { "cinnamonRoll", F(16, 8, 3, -2, junk: true) }, { "cupcakePink", F(10, 10, 2, -2, junk: true) },
            { "nutella", F(18, 6, 2, -4, junk: true) }, { "candy-bar", F(4, 11, junk: true) }, { "lollypop", F(2, 8, junk: true) },
            { "weltraumMoehre", F(12, 6, 2, favorit: true) }, { "radish", F(8, 1) }, { "eggplant", F(16, 1) },
        };

        public static readonly Food Fallback = F(10, 2);

        public static readonly string[] Kategorien = { "gemuese", "suesses", "warm", "getraenke" };

        public static readonly IReadOnlyDictionary<string, string> FoodKategorie = new Dictionary<string, string>
        {
            { "carrot", "gemuese" }, { "apple", "gemuese" }, { "banana", "gemuese" }, { "tomato", "gemuese" }, { "strawberry", "gemuese" },
            { "grapes", "gemuese" }, { "watermelon", "gemuese" }, { "corn", "gemuese" }, { "salad", "gemuese" }, { "pumpkin", "gemuese" },
            { "tomate", "gemuese" }, { "melone", "gemuese" }, { "salat", "gemuese" }, { "pilz", "gemuese" }, { "ananas", "gemuese" },
            { "chili", "gemuese" }, { "weltraumMoehre", "gemuese" }, { "radish", "gemuese" }, { "eggplant", "gemuese" },
            { "cupcake", "suesses" }, { "cookie", "suesses" }, { "chocolate", "suesses" }, { "muffin", "suesses" }, { "donut-sprinkles", "suesses" },
            { "cottonCandy", "suesses" }, { "softServe", "suesses" }, { "ice-cream", "suesses" }, { "cake", "suesses" }, { "sundae", "suesses" },
            { "cinnamonRoll", "suesses" }, { "cupcakePink", "suesses" }, { "nutella", "suesses" }, { "candy-bar", "suesses" }, { "lollypop", "suesses" },
            { "bread", "warm" }, { "cheese", "warm" }, { "croissant", "warm" }, { "fries", "warm" }, { "sandwich", "warm" }, { "burger", "warm" },
            { "pizza", "warm" }, { "hot-dog", "warm" }, { "pancakes", "warm" }, { "waffle", "warm" },
        };

        /// <summary>Album hook (CollectionsLogic.award_in_state "treats") — installed once collections are ported.</summary>
        public static Action<JObject, string> TreatAwarder { get; set; }

        public static Food Def(string foodId) => foodId != null && Foods.TryGetValue(foodId, out var f) ? f : Fallback;

        public static Dictionary<string, double> Deltas(string foodId)
        {
            var d = Def(foodId);
            return new Dictionary<string, double> { { "hunger", d.Hunger }, { "fun", d.Fun }, { "energy", d.Energy }, { "hygiene", d.Hygiene } };
        }

        public static bool IsJunk(string foodId) => Def(foodId).Junk;
        public static bool IsFavorite(string foodId) => Def(foodId).Favorit;

        public static string Kategorie(string foodId)
        {
            if (foodId != null && FoodKategorie.TryGetValue(foodId, out var k)) return k;
            return IsJunk(foodId) ? "suesses" : "gemuese";
        }

        public struct Entry
        {
            public string Id;
            public int Count;
        }

        /// <summary>Stock as a sorted list (count &gt; 0; most first, then alphabetical) — stable for the fridge panel.</summary>
        public static List<Entry> InventoryEntries(JObject state)
        {
            var outp = new List<Entry>();
            if (!(J.Dict(state?["inventory"])["food"] is JObject food)) return outp;
            foreach (var p in food.Properties())
            {
                var count = (int)J.Num(p.Value);
                if (count > 0) outp.Add(new Entry { Id = p.Name, Count = count });
            }
            outp.Sort((a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Id, b.Id));
            return outp;
        }

        public static bool TooFull(JObject state)
        {
            if (!(J.Dict(state?["gooby"])["stats"] is JObject stats)) return false;
            return J.Num(stats["hunger"]) >= SattAb;
        }

        public sealed class FeedResult
        {
            public string Id;
            public Dictionary<string, double> Deltas;
            public double HungerGain;
            public bool Junk, Favorit;
            public int Feeds;
        }

        /// <summary>PURE feeding: mutates state (stock −1, stats +deltas, junk weight/health, feeds counter). null = nothing fed.</summary>
        public static FeedResult ApplyFeed(JObject state, string foodId)
        {
            if (!(state?["inventory"] is JObject inventory) || !(inventory["food"] is JObject food)) return null;
            if ((int)J.Num(food[foodId]) <= 0) return null;
            if (TooFull(state)) return null;
            var rest = (int)J.Num(food[foodId]) - 1;
            if (rest <= 0) food.Remove(foodId);
            else food[foodId] = rest;
            var gooby = state["gooby"] as JObject;
            var before = gooby != null ? J.Dict(gooby["stats"]) : new JObject();
            var d = Deltas(foodId);
            var after = Stats.ApplyDeltas(before, d);
            var junk = IsJunk(foodId);
            if (gooby != null)
            {
                gooby["stats"] = after;
                gooby["weight"] = Weight.OnEat(J.NumOr(gooby["weight"], Weight.Default), junk);
                gooby["health"] = Health.OnEat(gooby["health"], junk);
            }
            var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
            counters["feeds"] = (int)J.Num(counters["feeds"]) + 1;
            TreatAwarder?.Invoke(state, foodId);
            return new FeedResult
            {
                Id = foodId, Deltas = d,
                HungerGain = J.Num(after["hunger"]) - J.Num(before["hunger"]),
                Junk = junk, Favorit = IsFavorite(foodId), Feeds = J.Int(counters["feeds"]),
            };
        }
    }
}
