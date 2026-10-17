using System;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// GvZ campaign progress as the additive save slice "gvz" (port of gvz_progress.gd, byte-compatible with save v5):
    /// {v, stars, best, cleared, goldi}, keys are the level ids as strings. Stars 1–3 by panic Goobys used, best
    /// scores, first-clear flags for the bonus, and the Goldi code flag.
    /// </summary>
    public static class GvzProgress
    {
        public const string SliceId = "gvz";
        private static bool _registered;

        public static void RegisterSlice()
        {
            if (_registered) return;
            _registered = true;
            Save.SaveSchema.RegisterSlice(SliceId, DefaultSlice, NormalizeSlice);
        }

        public static JObject DefaultSlice() => new JObject
        {
            ["v"] = 1, ["stars"] = new JObject(), ["best"] = new JObject(), ["cleared"] = new JObject(), ["goldi"] = false,
        };

        public static JObject NormalizeSlice(JToken raw)
        {
            var slice = raw as JObject ?? DefaultSlice();
            slice["v"] = Math.Max(1, (int)Number(slice["v"], 1.0));
            foreach (var key in new[] { "stars", "best", "cleared" })
                if (!(slice[key] is JObject)) slice[key] = new JObject();
            slice["goldi"] = slice["goldi"] is JValue g && g.Type == JTokenType.Boolean && (bool)g;
            return slice;
        }

        /// <summary>Stars of a win: 3 without a panic Gooby, 2 with one, else 1.</summary>
        public static int StarsFor(int mowersUsed) => mowersUsed <= 0 ? 3 : mowersUsed == 1 ? 2 : 1;

        /// <summary>Total score of a win: the run's kill score + star bonus + level bonus + one-time first-clear bonus.</summary>
        public static int FinalScore(int runScore, int levelId, int stars, bool firstClear, JObject balance)
        {
            var score = balance?["score"] as JObject;
            var total = runScore + stars * GvzJson.I(score, "star_bonus", 10) + levelId * GvzJson.I(score, "level_bonus", 4);
            if (firstClear) total += GvzJson.I(score, "first_clear_bonus", 50);
            return total;
        }

        public static int LevelStars(JObject state, int levelId) => state == null ? 0 : (int)Number(J.Path(state, SliceId, "stars", levelId.ToString()), 0.0);

        public static bool IsCleared(JObject state, int levelId) =>
            state != null && J.Path(state, SliceId, "cleared", levelId.ToString()) is JValue v && v.Type == JTokenType.Boolean && (bool)v;

        public static int MaxUnlocked(JObject state)
        {
            for (var id = 1; id <= GvzData.LevelCount; id++) if (!IsCleared(state, id)) return id;
            return GvzData.LevelCount;
        }

        public static int TotalStars(JObject state)
        {
            var total = 0;
            for (var id = 1; id <= GvzData.LevelCount; id++) total += LevelStars(state, id);
            return total;
        }

        public static int ClearedCount(JObject state)
        {
            var total = 0;
            for (var id = 1; id <= GvzData.LevelCount; id++) if (IsCleared(state, id)) total += 1;
            return total;
        }

        public static bool GoldiUnlocked(JObject state) => state != null && J.Path(state, SliceId, "goldi") is JValue v && v.Type == JTokenType.Boolean && (bool)v;

        public sealed class Booking
        {
            public bool FirstClear, NewBest;
            public int StarsBefore;
        }

        public static Booking RecordWin(JObject state, int levelId, int stars, int score)
        {
            var booking = new Booking { FirstClear = !IsCleared(state, levelId), StarsBefore = LevelStars(state, levelId) };
            if (state == null) { booking.NewBest = true; return booking; }
            var key = levelId.ToString();
            var bestBefore = (int)Number(J.Path(state, SliceId, "best", key), 0.0);
            booking.NewBest = score > bestBefore;
            var slice = NormalizeSlice(state[SliceId] as JObject ?? DefaultSlice());
            ((JObject)slice["cleared"])[key] = true;
            ((JObject)slice["stars"])[key] = Math.Max(booking.StarsBefore, stars);
            ((JObject)slice["best"])[key] = Math.Max(bestBefore, score);
            state[SliceId] = slice;
            return booking;
        }

        public static Booking RecordWin(GameStateStore store, int levelId, int stars, int score)
        {
            Booking booking = null;
            store.Update(s => booking = RecordWin(s, levelId, stars, score));
            store.NotifySliceChanged(SliceId);
            return booking;
        }

        public const int NetSeenCap = 60;

        /// <summary>
        /// Books an online PvP result once (GoobyPal pattern: the server keeps it pending until the ACK — a live push and the
        /// WELCOME after a crash may both deliver it, the rewardId makes the repeat harmless): the server's coins into
        /// economy.coins, the win or loss into gvz.netz {seen, wins, losses}. False = already booked / no id.
        /// </summary>
        public static bool BookNetResult(JObject state, string rewardId, bool won, int coins)
        {
            if (state == null || string.IsNullOrEmpty(rewardId)) return false;
            var slice = NormalizeSlice(state[SliceId] as JObject ?? DefaultSlice());
            var netz = slice["netz"] as JObject ?? new JObject();
            var seen = netz["seen"] as JArray ?? new JArray();
            foreach (var t in seen) if (t.Type == JTokenType.String && (string)t == rewardId) return false;
            seen.Add(rewardId);
            while (seen.Count > NetSeenCap) seen.RemoveAt(0);
            netz["seen"] = seen;
            netz["wins"] = (int)Number(netz["wins"], 0.0) + (won ? 1 : 0);
            netz["losses"] = (int)Number(netz["losses"], 0.0) + (won ? 0 : 1);
            slice["netz"] = netz;
            state[SliceId] = slice;
            if (coins > 0) Gooby.Core.Life.Economy.Award(J.EnsureDict(state, "economy"), coins);
            return true;
        }

        public static bool BookNetResult(GameStateStore store, string rewardId, bool won, int coins)
        {
            var booked = false;
            store.Update(s => booked = BookNetResult(s, rewardId, won, coins));
            if (booked) store.NotifySliceChanged(SliceId);
            return booked;
        }

        public static int NetWins(JObject state) => (int)Number(J.Path(state, SliceId, "netz", "wins"), 0.0);
        public static int NetLosses(JObject state) => (int)Number(J.Path(state, SliceId, "netz", "losses"), 0.0);

        private static double Number(JToken t, double fallback) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<double>() : fallback;
    }
}
