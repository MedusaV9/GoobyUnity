using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The ranch region's save keys (port of ranch_welt_state.gd), additive in the <c>ranch</c> slice and healed on read:
    /// <c>ranch.welt</c> (the zones ridden through — the farm always — plus <c>funde</c> and <c>karteGesehen</c> of the
    /// discoveries) and <c>ranch.wetter</c> (the weather seed, stable per save, so the farm, the village and the region
    /// share one deterministic day plan, offline).
    /// </summary>
    public static class RanchWeltState
    {
        /// <summary>The region map's seed (ranch_karte.json), the default weather seed of every save.</summary>
        public const long KartenSeed = 20260726;

        public static long WetterSeed(JObject state)
        {
            var seed = J.Path(state, RanchHof.SliceId, "wetter", "seed");
            return seed != null && (seed.Type == JTokenType.Integer || seed.Type == JTokenType.Float) ? (long)(double)seed : KartenSeed;
        }

        /// <summary>The ranch weather at a local date ("YYYY-MM-DD") and hour.</summary>
        public static RanchWetter.Zustand Wetter(JObject state, string datum, double stunde) =>
            RanchWetter.ZustandBei(datum, stunde, WetterSeed(state));

        /// <summary>Is it raining on the farm right now (the save's weather plan at the local hour)?</summary>
        public static bool RegnetJetzt(JObject state, long nowMs, int utcOffsetMinuten)
        {
            var lokal = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToOffset(TimeSpan.FromMinutes(utcOffsetMinuten));
            return Wetter(state, lokal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), lokal.Hour + lokal.Minute / 60.0).Regnet;
        }

        /// <summary>The healed <c>ranch.welt</c> object (created on demand).</summary>
        public static JObject Welt(JObject state)
        {
            var ranch = J.EnsureDict(state, RanchHof.SliceId);
            var welt = J.EnsureDict(ranch, "welt");
            welt["v"] = System.Math.Max(1, J.Int(welt["v"], 1));
            var zonen = new HashSet<string>(RanchKarte.ZonenIds());
            var entdeckt = new List<string> { "hof" };
            foreach (var id in (welt["entdeckt"] as JArray ?? new JArray()).Select(t => J.Str(t)))
                if (zonen.Contains(id) && !entdeckt.Contains(id)) entdeckt.Add(id);
            welt["entdeckt"] = new JArray(entdeckt);
            return welt;
        }

        /// <summary>The zones ridden through so far (the farm first).</summary>
        public static List<string> EntdeckteZonen(JObject state) =>
            (Welt(state)["entdeckt"] as JArray).Select(t => J.Str(t)).ToList();

        /// <summary>A zone ridden into: true when it is new (the toast's moment).</summary>
        public static bool EntdeckeZone(JObject state, string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId) || RanchKarte.ZoneVon(zoneId) == null) return false;
            var welt = Welt(state);
            var arr = (JArray)welt["entdeckt"];
            if (arr.Any(t => J.Str(t) == zoneId)) return false;
            arr.Add(zoneId);
            return true;
        }

        /// <summary>A healed id list under <c>ranch.welt</c> (unknown discoveries dropped, no duplicates).</summary>
        internal static List<string> Liste(JObject state, string key)
        {
            var out_ = new List<string>();
            var welt = J.Path(state, RanchHof.SliceId, "welt") as JObject;
            foreach (var id in (welt?[key] as JArray ?? new JArray()).Select(t => J.Str(t)))
                if (RanchEntdeckungen.Fundort(id) != null && !out_.Contains(id)) out_.Add(id);
            return out_;
        }
    }
}
