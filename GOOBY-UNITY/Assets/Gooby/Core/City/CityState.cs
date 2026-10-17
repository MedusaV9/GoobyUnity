using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// Persistent city flags (save v5 `city.flags`, port of city_state.gd): dialog effects that outlive a visit,
    /// e.g. the vet's prescription `rezept_tropfen` the GOOBYTHEKE redeems. Dialog marker flags (`vet_*`) never
    /// land here — the place screen consumes them.
    /// </summary>
    public static class CityState
    {
        public const string MarkerPrefix = "vet_";

        public static bool IsMarker(string flag) => flag != null && flag.StartsWith(MarkerPrefix, System.StringComparison.Ordinal);

        public static Dictionary<string, bool> Flags(GameStateStore gs)
        {
            var outp = new Dictionary<string, bool>();
            if (!(J.Path(gs?.State, "city", "flags") is JObject flags)) return outp;
            foreach (var kv in flags)
                if (J.IsTrue(kv.Value)) outp[kv.Key] = true;
            return outp;
        }

        public static bool HasFlag(GameStateStore gs, string flag) => J.IsTrue(J.Path(gs?.State, "city", "flags", flag));

        public static void SetFlag(GameStateStore gs, string flag, bool value)
        {
            if (gs == null || string.IsNullOrEmpty(flag)) return;
            gs.Update(state =>
            {
                var flags = J.EnsureDict(J.EnsureDict(state, "city"), "flags");
                if (value) flags[flag] = true;
                else flags.Remove(flag);
            });
            gs.NotifySliceChanged("city");
        }

        /// <summary>One item into inventory.items (dialog effect "item:&lt;id&gt;", e.g. medicine from the pharmacy).</summary>
        public static int GiveItem(GameStateStore gs, string item)
        {
            if (gs == null || string.IsNullOrEmpty(item)) return 0;
            var count = 0;
            gs.Update(state =>
            {
                var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
                count = J.Int(items[item]) + 1;
                items[item] = count;
            });
            gs.NotifySliceChanged("inventory");
            return count;
        }
    }
}
