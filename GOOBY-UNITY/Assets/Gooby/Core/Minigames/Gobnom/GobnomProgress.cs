using System;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gobnom
{
    /// <summary>
    /// GOB NOM progress as the additive save slice "gobnom" (port of gobnom_progress.gd, byte-compatible with save v5):
    /// {v, stars, best, cleared} keyed "c&lt;id&gt;" (campaign, 15) and "n&lt;id&gt;" (coop, 10). Stars 0–3 = jars of
    /// the best run, best scores, first-clear flags for the first-clear bonus. The functions read a plain state object so
    /// tests need no store; <see cref="RecordWin(GameStateStore,string,int,int,int)"/> books through the store.
    /// </summary>
    public static class GobnomProgress
    {
        public const string SliceId = "gobnom";
        private static bool _registered;

        public static void RegisterSlice()
        {
            if (_registered) return;
            _registered = true;
            Save.SaveSchema.RegisterSlice(SliceId, DefaultSlice, NormalizeSlice);
        }

        public static JObject DefaultSlice() => new JObject { ["v"] = 1, ["stars"] = new JObject(), ["best"] = new JObject(), ["cleared"] = new JObject() };

        /// <summary>Self-heal: repair types, keep valid data verbatim.</summary>
        public static JObject NormalizeSlice(JToken raw)
        {
            var slice = raw as JObject ?? DefaultSlice();
            slice["v"] = Math.Max(1, (int)Number(slice["v"], 1.0));
            foreach (var key in new[] { "stars", "best", "cleared" })
                if (!(slice[key] is JObject)) slice[key] = new JObject();
            return slice;
        }

        public static string LevelKey(string track, int levelId) => (track == GobnomData.TrackCoop ? "n" : "c") + levelId;

        public static int LevelCount(string track) => track == GobnomData.TrackCoop ? GobnomData.CoopCount : GobnomData.CampaignCount;

        /// <summary>Score of a win: base + jar bonus + level bonus + one-time first-clear bonus (all from the balance).</summary>
        public static int FinalScore(int levelId, int jars, bool firstClear, JObject balance)
        {
            var score = balance?["score"] as JObject;
            var total = GobnomLogic.Int(score, "win_base", 40);
            total += jars * GobnomLogic.Int(score, "jar_bonus", 25);
            total += levelId * GobnomLogic.Int(score, "level_bonus", 4);
            if (firstClear) total += GobnomLogic.Int(score, "first_clear_bonus", 50);
            return total;
        }

        public static int LevelStars(JObject state, string track, int levelId) =>
            state == null ? 0 : (int)Number(J.Path(state, SliceId, "stars", LevelKey(track, levelId)), 0.0);

        public static bool IsCleared(JObject state, string track, int levelId) =>
            state != null && J.Path(state, SliceId, "cleared", LevelKey(track, levelId)) is JValue v && v.Type == JTokenType.Boolean && (bool)v;

        /// <summary>Highest playable level: everything up to the first one not cleared yet (L1/CN1 always open).</summary>
        public static int MaxUnlocked(JObject state, string track)
        {
            for (var id = 1; id <= LevelCount(track); id++)
                if (!IsCleared(state, track, id)) return id;
            return LevelCount(track);
        }

        public static int TotalStars(JObject state, string track)
        {
            var total = 0;
            for (var id = 1; id <= LevelCount(track); id++) total += LevelStars(state, track, id);
            return total;
        }

        public static int ClearedCount(JObject state, string track)
        {
            var total = 0;
            for (var id = 1; id <= LevelCount(track); id++) if (IsCleared(state, track, id)) total += 1;
            return total;
        }

        public sealed class Booking
        {
            public bool FirstClear, NewBest;
            public int StarsBefore;
        }

        /// <summary>Books a win into a plain state object (cleared, stars and best only ever grow).</summary>
        public static Booking RecordWin(JObject state, string track, int levelId, int stars, int score)
        {
            var booking = new Booking { FirstClear = !IsCleared(state, track, levelId), StarsBefore = LevelStars(state, track, levelId) };
            if (state == null) { booking.NewBest = true; return booking; }
            var key = LevelKey(track, levelId);
            var bestBefore = (int)Number(J.Path(state, SliceId, "best", key), 0.0);
            booking.NewBest = score > bestBefore;
            var slice = NormalizeSlice(state[SliceId] as JObject ?? DefaultSlice());
            ((JObject)slice["cleared"])[key] = true;
            ((JObject)slice["stars"])[key] = Math.Max(booking.StarsBefore, stars);
            ((JObject)slice["best"])[key] = Math.Max(bestBefore, score);
            state[SliceId] = slice;
            return booking;
        }

        /// <summary>Books a win through the store and announces the slice change.</summary>
        public static Booking RecordWin(GameStateStore store, string track, int levelId, int stars, int score)
        {
            Booking booking = null;
            store.Update(s => booking = RecordWin(s, track, levelId, stars, score));
            store.NotifySliceChanged(SliceId);
            return booking;
        }

        private static double Number(JToken t, double fallback) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<double>() : fallback;
    }
}
