using System;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>home.storage helpers — port of scripts/home/storage_logic.gd ([{item, variant, count}] rows).</summary>
    public static class StorageLogic
    {
        public const string UnknownItem = "__unknown__";
        public const string DefaultVariant = "default";

        public static int PointsUsed(JArray storage, JObject defs)
        {
            var total = 0;
            foreach (var entry in storage)
            {
                if (!(entry is JObject e)) continue;
                total += ItemWeight(J.Str(e["item"]), defs) * Math.Max(0, J.Int(e["count"]));
            }
            return total;
        }

        public static int ItemWeight(string itemId, JObject defs)
        {
            if (defs != null && defs[itemId] is JObject def) return J.ClampI(J.Int(def["lagerwert"], 1), 1, 4);
            return 1;
        }

        public static bool CanAdd(JArray storage, string itemId, JObject defs, int capacity) =>
            PointsUsed(storage, defs) + ItemWeight(itemId, defs) <= capacity;

        public static void Add(JArray storage, string itemId, string variant = "default")
        {
            foreach (var entry in storage)
            {
                if (entry is JObject e && J.Str(e["item"]) == itemId && J.Str(e["variant"], DefaultVariant) == variant)
                {
                    e["count"] = J.Int(e["count"]) + 1;
                    return;
                }
            }
            storage.Add(new JObject { ["item"] = itemId, ["variant"] = variant, ["count"] = 1 });
        }

        public static bool Take(JArray storage, string itemId, string variant = "default")
        {
            for (var i = 0; i < storage.Count; i++)
            {
                if (!(storage[i] is JObject e)) continue;
                if (J.Str(e["item"]) != itemId || J.Str(e["variant"], DefaultVariant) != variant) continue;
                var count = J.Int(e["count"]);
                if (count <= 0) return false;
                if (count == 1) storage.RemoveAt(i);
                else e["count"] = count - 1;
                return true;
            }
            return false;
        }

        /// <summary>Takes one piece in any colour — the plain one ("default"/"natur") first; returns the colour taken or null.</summary>
        public static string TakeAny(JArray storage, string itemId)
        {
            foreach (var plain in new[] { DefaultVariant, "natur" })
                if (Take(storage, itemId, plain)) return plain;
            foreach (var entry in storage)
            {
                if (!(entry is JObject e) || J.Str(e["item"]) != itemId || J.Int(e["count"]) <= 0) continue;
                var v = J.Str(e["variant"], DefaultVariant);
                if (Take(storage, itemId, v)) return v;
            }
            return null;
        }

        public static int CountOf(JArray storage, string itemId)
        {
            var total = 0;
            foreach (var entry in storage)
                if (entry is JObject e && J.Str(e["item"]) == itemId) total += Math.Max(0, J.Int(e["count"]));
            return total;
        }
    }
}
