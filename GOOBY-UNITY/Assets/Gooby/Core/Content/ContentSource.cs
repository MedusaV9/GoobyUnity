using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Content
{
    /// <summary>
    /// Engine-agnostic reader for the JSON content packs shipped under Resources/gooby/ (content/*, home/*).
    /// The Unity host installs a Resources.Load reader; tests install a file-system reader.
    /// Paths are relative to the gooby/ root and given WITHOUT extension (Resources semantics),
    /// e.g. "content/achievements/data/achievements".
    /// </summary>
    public static class ContentSource
    {
        private static Func<string, string> _read;
        private static readonly Dictionary<string, JObject> Cache = new Dictionary<string, JObject>();

        public static void Install(Func<string, string> read)
        {
            _read = read;
            Cache.Clear();
        }

        public static bool Installed => _read != null;

        /// <summary>Parsed JSON object at path or an empty object when missing/broken (packs must never crash the game).</summary>
        public static JObject LoadObject(string path)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
            JObject result = null;
            try
            {
                var text = _read?.Invoke(path);
                if (!string.IsNullOrEmpty(text)) result = JToken.Parse(text) as JObject;
            }
            catch (Exception)
            {
                result = null;
            }
            result = result ?? new JObject();
            Cache[path] = result;
            return result;
        }

        /// <summary>Like LoadObject, but null when the file is missing or broken (for optional/localized variants).</summary>
        public static JObject TryLoadObject(string path)
        {
            var o = LoadObject(path);
            return o.HasValues ? o : null;
        }

        /// <summary>Items array of a content pack data file ({"schema":…, "items":[…]}).</summary>
        public static JArray LoadItems(string path)
        {
            var o = LoadObject(path);
            return o["items"] as JArray ?? new JArray();
        }

        public static void ResetCache() => Cache.Clear();
    }
}
