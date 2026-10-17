using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The horses' day and the time the app was closed (port of ranch_offline.gd): they sleep in the stable from 21 to
    /// 7 o'clock and graze by day. Offline the pasture decays at 0.3× and at most 480 simulated minutes, stable nights run
    /// in full; the bond fades after its grace; values that slipped under 25 meanwhile are reported. Pure — only the
    /// <c>ranch.tiere</c> object is simulated.
    /// </summary>
    public static class RanchOffline
    {
        public const int NachtAb = 21, MorgenAb = 7;
        public const double WeideRateMult = 0.3, WeideCapMin = 480.0;
        public const long ElapsedCapMs = 7L * 86_400_000L;

        public static bool IstNacht(double stunde)
        {
            var h = FPosMod(stunde, 24.0);
            return h >= NachtAb || h < MorgenAb;
        }

        /// <summary>Where the horses stand: "stall" at night, "weide" by day.</summary>
        public static string Wohin(double stunde) => IstNacht(stunde) ? "stall" : "weide";

        public static double StundeLokal(long ms, int offsetMin = 0)
        {
            var localSecs = (long)Math.Floor(ms / 1000.0) + offsetMin * 60L;
            var daySecs = ((localSecs % 86400L) + 86400L) % 86400L;
            return daySecs / 3600.0;
        }

        /// <summary>Milliseconds to the next switch (7:00 or 21:00).</summary>
        public static long MsBisPhasenwechsel(long ms, int offsetMin = 0)
        {
            var h = StundeLokal(ms, offsetMin);
            var ziel = IstNacht(h) ? (double)MorgenAb : NachtAb;
            var deltaH = FPosMod(ziel - h, 24.0);
            if (deltaH <= 0.0) deltaH = 24.0;
            return (long)Math.Ceiling(deltaH * 3_600_000.0);
        }

        public sealed class Ergebnis
        {
            public JObject Tiere;
            public List<string> Events = new List<string>();
        }

        /// <summary>
        /// Simulates the closed time up to <paramref name="nowMs"/> on a copy of <paramref name="tiere"/>. The first run
        /// (no stamp) and a clock that ran backwards only set the stamp. Events: "hungrig|durstig|stinkig:&lt;id&gt;" for
        /// every value that slipped under 25 meanwhile and "stallDreckig" when the stable dropped under 40.
        /// </summary>
        public static Ergebnis Simulate(JObject tiere, long nowMs, int offsetMin = 0, double sauberkeitMult = 1.0, RanchBauEffekte.Weide boni = null)
        {
            boni = boni ?? new RanchBauEffekte.Weide();
            var res = new Ergebnis { Tiere = (JObject)(tiere ?? new JObject()).DeepClone() };
            var t = res.Tiere;
            var last = (long)J.NumOr(t["lastTickAt"], 0.0);
            if (last <= 0 || nowMs - last <= 0)
            {
                t["lastTickAt"] = nowMs;
                return res;
            }
            var elapsed = Math.Min(nowMs - last, ElapsedCapMs);
            var start = nowMs - elapsed;
            var pferde = t["pferde"] as JObject ?? new JObject();
            var stall = J.EnsureDict(t, "stall");
            var vorher = pferde.Properties().ToDictionary(p => p.Name, p => (JObject)(J.Dict(p.Value["werte"]).DeepClone()));
            var stallVorher = J.NumOr(stall["sauberkeit"], 100.0);

            var cursor = start;
            var weideBudget = WeideCapMin;
            for (var guard = 0; cursor < nowMs && guard < 64; guard++)
            {
                var segEnde = Math.Min(nowMs, cursor + MsBisPhasenwechsel(cursor, offsetMin));
                var segMin = (segEnde - cursor) / 60000.0;
                if (IstNacht(StundeLokal(cursor, offsetMin)))
                {
                    var s = J.NumOr(stall["sauberkeit"], 100.0);
                    foreach (var p in pferde.Properties())
                        if (p.Value is JObject pferd) pferd["werte"] = RanchCare.Tick(J.Dict(pferd["werte"]), segMin, schlaeft: true, stallSauberkeit: s);
                    stall["sauberkeit"] = RanchCare.StallTick(s, segMin, pferde.Count);
                }
                else
                {
                    var simMin = Math.Min(segMin, weideBudget);
                    weideBudget -= simMin;
                    if (simMin > 0.0)
                        foreach (var p in pferde.Properties())
                            if (p.Value is JObject pferd) pferd["werte"] = Weiden(RanchCare.Tick(J.Dict(pferd["werte"]), simMin, rateMult: WeideRateMult, sauberkeitMult: sauberkeitMult,
                                hungerMult: boni.Hunger, durstMult: boni.Durst), simMin, boni);
                }
                cursor = segEnde;
            }

            var dtMin = (nowMs - start) / 60000.0;
            foreach (var p in pferde.Properties())
            {
                if (!(p.Value is JObject pferd)) continue;
                var pflegeAt = (long)J.NumOr(pferd["letztePflegeAt"], 0.0);
                var seitMin = pflegeAt > 0 ? Math.Max(0.0, (start - pflegeAt) / 60000.0) : 0.0;
                pferd["bindung"] = RanchCare.BondVerfall(J.NumOr(pferd["bindung"], 0.0), seitMin, dtMin);
            }
            t["lastTickAt"] = nowMs;

            var namen = new Dictionary<string, string> { ["hunger"] = "hungrig", ["durst"] = "durstig", ["sauberkeit"] = "stinkig" };
            var ids = pferde.Properties().Select(p => p.Name).OrderBy(id => id, StringComparer.Ordinal).ToList();
            foreach (var k in RanchCare.Keys)
                foreach (var id in ids)
                {
                    if (!vorher.TryGetValue(id, out var davorWerte)) continue;
                    var davor = J.NumOr(davorWerte[k], 0.0);
                    var danach = J.NumOr(J.Dict(pferde[id]?["werte"])[k], 0.0);
                    if (davor >= RanchCare.Niedrig && danach < RanchCare.Niedrig) res.Events.Add(namen[k] + ":" + id);
                }
            if (stallVorher >= RanchCare.StallDreckigUnter && J.NumOr(stall["sauberkeit"], 100.0) < RanchCare.StallDreckigUnter)
                res.Events.Add("stallDreckig");
            return res;
        }

        /// <summary>After a grazing stretch: the herb pasture lifts every value under its cap, the self-drinker keeps thirst up.</summary>
        private static JObject Weiden(JObject werte, double simMin, RanchBauEffekte.Weide boni)
        {
            if (boni.KraeuterProH > 0.0)
                foreach (var k in RanchCare.Keys)
                {
                    var v = J.NumOr(werte[k], 0.0);
                    if (v < RanchBauEffekte.KraeuterDeckel) werte[k] = Math.Min(RanchBauEffekte.KraeuterDeckel, v + boni.KraeuterProH * simMin / 60.0);
                }
            if (boni.DurstBoden > 0.0 && J.NumOr(werte["durst"], 0.0) < boni.DurstBoden) werte["durst"] = boni.DurstBoden;
            return werte;
        }

        private static double FPosMod(double x, double y)
        {
            var v = x % y;
            if ((v < 0 && y > 0) || (v > 0 && y < 0)) v += y;
            return v + 0.0;
        }
    }
}
