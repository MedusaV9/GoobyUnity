using System;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Rewards of the ranch's random events (port of ranch_event_rewards.gd): only existing mechanisms — coins into the
    /// economy, hay and apples into the farm store, carrots into the fridge, bond on the horse that was helped, and the
    /// farrier's hoof check as an expiry stamp <c>ranch.events.hufCheckUntil</c>. The events themselves (time window,
    /// timeout, cooldown, one at a time) run on <see cref="RandomEvents"/> with context "ranch".
    /// </summary>
    public static class RanchEvents
    {
        public const string Context = "ranch";
        private const string Reason = "ranch_event";

        /// <summary>Books the def's <c>ranch_reward</c>; <paramref name="gesammelt"/> counts carrots picked up in the rain.</summary>
        public static JObject Belohnen(JObject state, JObject def, string pferdId, long nowMs, int gesammelt = 0)
        {
            var r = J.Dict(def?["ranch_reward"]);
            var outp = new JObject();
            var muenzen = J.Int(r["muenzen"]);
            if (muenzen > 0)
            {
                Economy.Award(J.EnsureDict(state, "economy"), muenzen, Reason);
                outp["muenzen"] = muenzen;
            }
            var bindung = J.NumOr(r["bindung"], 0.0);
            var pferd = RanchHof.Pferd(state, pferdId);
            if (bindung > 0 && pferd != null)
            {
                pferd["bindung"] = RanchCare.ClampWert(J.NumOr(pferd["bindung"], 0.0) + bindung);
                outp["bindung"] = bindung;
            }
            if (r["items"] is JObject items)
            {
                var lager = RanchHof.Lager(state);
                foreach (var p in items.Properties())
                {
                    lager[p.Name] = J.Int(lager[p.Name]) + Math.Max(0, J.Int(p.Value));
                    outp[p.Name] = J.Int(p.Value);
                }
            }
            var stunden = J.NumOr(r["huf_check_h"], 0.0);
            if (stunden > 0)
            {
                J.EnsureDict(J.EnsureDict(state, RanchHof.SliceId), "events")["hufCheckUntil"] = nowMs + (long)(stunden * 3_600_000L);
                outp["huf_check_h"] = stunden;
            }
            var maxKarotten = J.Int(r["karotten_max"]);
            if (maxKarotten > 0 && gesammelt > 0)
            {
                var n = Math.Min(maxKarotten, gesammelt);
                var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                food["carrot"] = J.Int(food["carrot"]) + n;
                outp["karotten"] = n;
            }
            return outp;
        }

        public static bool HufCheckAktiv(JObject state, long nowMs) => J.Long(J.Path(state, RanchHof.SliceId, "events", "hufCheckUntil")) > nowMs;
    }
}
