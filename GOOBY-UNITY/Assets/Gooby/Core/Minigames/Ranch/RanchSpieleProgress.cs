using System;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Progress of the two ranch arcade games (port of spiele_progress.gd) under <c>ranch.spiele.&lt;spiel&gt;</c> of the
    /// ranch slice (save v5 layout of RanchPlaySlices): "parcours" and "herde", 10 levels each, keys "k&lt;id&gt;" in
    /// stars/best/cleared. Readers take a plain state object; booking goes through the store.
    /// </summary>
    public static class RanchSpieleProgress
    {
        public const string Parcours = "parcours", Herde = "herde";
        public const int LevelCount = 10;

        public static string LevelKey(int levelId) => "k" + levelId;

        public static JObject DefaultSpiele() => new JObject
        {
            ["v"] = 1,
            ["parcours"] = new JObject { ["stars"] = new JObject(), ["best"] = new JObject(), ["cleared"] = new JObject() },
            ["herde"] = new JObject { ["stars"] = new JObject(), ["best"] = new JObject(), ["cleared"] = new JObject() },
        };

        public static JObject NormalizeSpiele(JToken raw)
        {
            var spiele = raw as JObject ?? DefaultSpiele();
            spiele["v"] = Math.Max(1, (int)Number(spiele["v"], 1.0));
            foreach (var spiel in new[] { Parcours, Herde })
            {
                var eintrag = spiele[spiel] as JObject ?? new JObject();
                foreach (var key in new[] { "stars", "best", "cleared" })
                    if (!(eintrag[key] is JObject)) eintrag[key] = new JObject();
                spiele[spiel] = eintrag;
            }
            return spiele;
        }

        public static int LevelStars(JObject state, string spiel, int id) => state == null ? 0 : (int)Number(J.Path(state, "ranch", "spiele", spiel, "stars", LevelKey(id)), 0.0);

        public static int LevelBest(JObject state, string spiel, int id) => state == null ? 0 : (int)Number(J.Path(state, "ranch", "spiele", spiel, "best", LevelKey(id)), 0.0);

        public static bool IsCleared(JObject state, string spiel, int id) =>
            state != null && J.Path(state, "ranch", "spiele", spiel, "cleared", LevelKey(id)) is JValue v && v.Type == JTokenType.Boolean && (bool)v;

        public static int MaxUnlocked(JObject state, string spiel)
        {
            for (var id = 1; id <= LevelCount; id++) if (!IsCleared(state, spiel, id)) return id;
            return LevelCount;
        }

        public static int TotalStars(JObject state, string spiel)
        {
            var total = 0;
            for (var id = 1; id <= LevelCount; id++) total += LevelStars(state, spiel, id);
            return total;
        }

        public sealed class Booking
        {
            public bool FirstClear, NewBest;
            public int StarsBefore;
        }

        public static Booking RecordWin(JObject state, string spiel, int id, int stars, int score)
        {
            var booking = new Booking { FirstClear = !IsCleared(state, spiel, id), StarsBefore = LevelStars(state, spiel, id) };
            var bestBefore = LevelBest(state, spiel, id);
            booking.NewBest = score > bestBefore;
            if (state == null) return booking;
            var key = LevelKey(id);
            var ranch = state["ranch"] as JObject ?? new JObject();
            ranch["spiele"] = NormalizeSpiele(ranch["spiele"]);
            var eintrag = (JObject)ranch["spiele"][spiel];
            ((JObject)eintrag["cleared"])[key] = true;
            ((JObject)eintrag["stars"])[key] = Math.Max(booking.StarsBefore, stars);
            ((JObject)eintrag["best"])[key] = Math.Max(bestBefore, score);
            state["ranch"] = ranch;
            return booking;
        }

        public static Booking RecordWin(GameStateStore store, string spiel, int id, int stars, int score)
        {
            Booking booking = null;
            store.Update(s => booking = RecordWin(s, spiel, id, stars, score));
            store.NotifySliceChanged("ranch");
            return booking;
        }

        private static double Number(JToken t, double fallback) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<double>() : fallback;
    }
}
