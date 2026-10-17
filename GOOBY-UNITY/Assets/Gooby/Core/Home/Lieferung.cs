using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// Furniture deliveries (port of delivery_cutscene.gd's order contract): a furniture shop has already taken the
    /// coins and puts the order on <c>home.lieferungen</c> ({item, variant, count}); the delivery van brings it the next
    /// time Gooby is at home, then everything goes into the storage — never straight into a room. Orders count against
    /// the storage room while they are on the way, so a delivery always fits.
    /// </summary>
    public static class Lieferung
    {
        private static JArray Liste(JObject state)
        {
            var home = J.EnsureDict(state, HomeState.SliceId);
            if (!(home["lieferungen"] is JArray l)) { l = new JArray(); home["lieferungen"] = l; }
            return l;
        }

        /// <summary>Puts an order on the way (the shop has been paid).</summary>
        public static void Bestellen(JObject state, string itemId, string variant = "default", int menge = 1)
        {
            if (state == null || string.IsNullOrEmpty(itemId) || menge <= 0) return;
            var liste = Liste(state);
            var v = string.IsNullOrEmpty(variant) ? "default" : variant;
            foreach (var e in liste.OfType<JObject>())
                if (J.Str(e["item"]) == itemId && J.Str(e["variant"], "default") == v) { e["count"] = J.Int(e["count"], 1) + menge; return; }
            liste.Add(new JObject { ["item"] = itemId, ["variant"] = v, ["count"] = menge });
        }

        /// <summary>The orders on the way as (item, variant, count).</summary>
        public static List<(string item, string variant, int count)> Offene(JObject state) =>
            (J.Path(state, HomeState.SliceId, "lieferungen") as JArray ?? new JArray()).OfType<JObject>()
                .Select(e => (J.Str(e["item"]), J.Str(e["variant"], "default"), Math.Max(1, J.Int(e["count"], 1))))
                .Where(e => e.Item1.Length > 0).ToList();

        public static int Stueck(JObject state) => Offene(state).Sum(e => e.count);

        /// <summary>Storage points the orders on the way will take.</summary>
        public static int PunkteUnterwegs(JObject state, JObject defs) =>
            Offene(state).Sum(e => StorageLogic.ItemWeight(e.item, defs) * e.count);

        /// <summary>The van has unloaded: every piece into the storage (in its colour), the list is empty. Returns the pieces.</summary>
        public static int Einlagern(JObject state)
        {
            var offene = Offene(state);
            if (offene.Count == 0) return 0;
            var home = J.EnsureDict(state, HomeState.SliceId);
            if (!(home["storage"] is JArray storage)) { storage = new JArray(); home["storage"] = storage; }
            var n = 0;
            foreach (var (item, variant, count) in offene)
                for (var i = 0; i < count; i++) { StorageLogic.Add(storage, item, variant); n++; }
            home["lieferungen"] = new JArray();
            return n;
        }
    }
}
