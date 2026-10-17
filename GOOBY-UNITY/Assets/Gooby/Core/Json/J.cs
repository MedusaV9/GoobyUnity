using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Json
{
    /// <summary>
    /// Lenient JSON helpers that reproduce the web/Godot value semantics the save
    /// format relies on (JS <c>Number()</c> coercion, strict-bool reads, dict-or-empty).
    /// The save state is kept as <see cref="JObject"/> on purpose: it stays
    /// byte-compatible with the Godot/web v5 saves and additive pack slices.
    /// </summary>
    public static class J
    {
        /// <summary>Numeric read for int/float tokens only; everything else → fallback.</summary>
        public static double NumOr(JToken value, double fallback)
        {
            if (value == null) return fallback;
            switch (value.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return value.Value<double>();
                default:
                    return fallback;
            }
        }

        /// <summary>Numeric read for int/float tokens; everything else → 0.</summary>
        public static double Num(JToken value) => NumOr(value, 0.0);

        /// <summary>Numeric read that also counts bools (1/0) and yields NaN otherwise (stats.gd _num).</summary>
        public static double NumStats(JToken value)
        {
            if (value == null) return double.NaN;
            switch (value.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return value.Value<double>();
                case JTokenType.Boolean:
                    return value.Value<bool>() ? 1.0 : 0.0;
                default:
                    return double.NaN;
            }
        }

        /// <summary>
        /// JS <c>Number()</c> replica for leaf values (save_schema.gd _leaf_num): numbers pass,
        /// numeric strings are parsed, everything else (and non-finite) → NaN.
        /// </summary>
        public static double LeafNum(JToken value)
        {
            if (value == null) return double.NaN;
            switch (value.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return value.Value<double>();
                case JTokenType.String:
                    var text = value.Value<string>().Trim();
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                        && !double.IsNaN(n) && !double.IsInfinity(n))
                        return n;
                    return double.NaN;
                default:
                    return double.NaN;
            }
        }

        /// <summary>LeafNum with NaN → 0 (save_schema.gd _num).</summary>
        public static double LeafNumOrZero(JToken value)
        {
            var n = LeafNum(value);
            return double.IsNaN(n) ? 0.0 : n;
        }

        /// <summary>LeafNum with NaN → fallback and 0 → fallback (save_schema.gd _num_or).</summary>
        public static double LeafNumOr(JToken value, double fallback)
        {
            var n = LeafNum(value);
            if (double.IsNaN(n)) return fallback;
            return n != 0.0 ? n : fallback;
        }

        /// <summary>Strict numeric type check (int/float tokens only).</summary>
        public static bool IsNum(JToken value) =>
            value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float);

        /// <summary>Web <c>=== true</c>: only a real boolean true counts.</summary>
        public static bool IsTrue(JToken value) =>
            value != null && value.Type == JTokenType.Boolean && value.Value<bool>();

        /// <summary>Walks nested objects by key segments; null when any segment is missing or not an object.</summary>
        public static JToken Path(JToken root, params string[] parts)
        {
            JToken cur = root;
            foreach (var part in parts)
            {
                if (!(cur is JObject o) || !o.ContainsKey(part)) return null;
                cur = o[part];
            }
            return cur;
        }

        /// <summary>Returns the token as JObject, or a NEW empty JObject when it is not one.</summary>
        public static JObject Dict(JToken value) => value as JObject ?? new JObject();

        /// <summary>Returns the child object, creating and attaching it when missing/not an object.</summary>
        public static JObject EnsureDict(JObject parent, string key)
        {
            if (parent[key] is JObject o) return o;
            var created = new JObject();
            parent[key] = created;
            return created;
        }

        public static string Str(JToken value, string fallback = "") =>
            value != null && value.Type == JTokenType.String ? value.Value<string>() : fallback;

        public static bool IsString(JToken value) => value != null && value.Type == JTokenType.String;

        public static int Int(JToken value, int fallback = 0)
        {
            if (!IsNum(value)) return fallback;
            return (int)Math.Floor(value.Value<double>());
        }

        public static long Long(JToken value, long fallback = 0)
        {
            if (!IsNum(value)) return fallback;
            return (long)Math.Floor(value.Value<double>());
        }

        public static JToken Clone(JToken value) => value?.DeepClone();

        public static JObject CloneObj(JObject value) => (JObject)value.DeepClone();

        /// <summary>Dot-path read ("economy.coins"); only "." splits (keys with ":" are safe).</summary>
        public static JToken GetPath(JObject root, string path)
        {
            JToken node = root;
            foreach (var part in path.Split('.'))
            {
                if (node is JObject o && o.TryGetValue(part, out var next)) node = next;
                else return null;
            }
            return node;
        }

        /// <summary>Dot-path write creating intermediate objects.</summary>
        public static void SetPath(JObject root, string path, JToken value)
        {
            var parts = path.Split('.');
            var node = root;
            for (var i = 0; i < parts.Length - 1; i++)
                node = EnsureDict(node, parts[i]);
            node[parts[parts.Length - 1]] = value ?? JValue.CreateNull();
        }

        /// <summary>Structural equality helper for watched-value diffs.</summary>
        public static bool Same(JToken a, JToken b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return JToken.DeepEquals(a, b);
        }

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        public static int ClampI(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
