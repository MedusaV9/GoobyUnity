using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>
    /// GOOBY lobby protocol envelope <c>{"v":1,"t":"TYPE","seq":n,"ts":ms,"d":{}}</c>
    /// (GOOBY-SERVER src/protocol.js). Server replies carry <c>re</c> = request seq, pushes don't.
    /// </summary>
    public sealed class NetEnvelope
    {
        public const int ProtocolVersion = 1;

        public int V { get; private set; }
        public string Type { get; private set; }
        /// <summary>Request seq this message answers (null for pushes).</summary>
        public long? Re { get; private set; }
        public long? Seq { get; private set; }
        public long Ts { get; private set; }
        /// <summary>Payload; never null (non-object d → empty object).</summary>
        public JObject Data { get; private set; }

        public bool IsError => Type == "ERROR";

        /// <summary>Serializes a client → server message (compact JSON).</summary>
        public static string Build(string t, long seq, long tsMs, JObject d)
        {
            var o = new JObject
            {
                ["v"] = ProtocolVersion,
                ["t"] = t ?? "",
                ["seq"] = seq,
                ["ts"] = tsMs,
                ["d"] = d ?? new JObject(),
            };
            return o.ToString(Formatting.None);
        }

        /// <summary>Parses a server frame; null for garbage (bad JSON, no object, missing/empty t).</summary>
        public static NetEnvelope Parse(string text)
        {
            var o = NetJson.ParseObject(text);
            if (o == null) return null;
            var t = o["t"];
            if (t == null || t.Type != JTokenType.String) return null;
            var type = t.Value<string>();
            if (string.IsNullOrEmpty(type)) return null;
            return new NetEnvelope
            {
                V = (int)NetJson.Long(o["v"], 0),
                Type = type,
                Re = NetJson.OptLong(o["re"]),
                Seq = NetJson.OptLong(o["seq"]),
                Ts = NetJson.Long(o["ts"], 0),
                Data = o["d"] as JObject ?? new JObject(),
            };
        }
    }

    /// <summary>Small JSON helpers for the net layer (no date coercion, never throws).</summary>
    public static class NetJson
    {
        /// <summary>Parses a JSON object; null on any error or non-object root. Date-like strings stay strings.</summary>
        public static JObject ParseObject(string text)
        {
            var token = ParseToken(text);
            return token as JObject;
        }

        /// <summary>Parses any JSON value; null on error. Date-like strings stay strings.</summary>
        public static JToken ParseToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(text)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    var token = JToken.ReadFrom(reader);
                    // Trailing garbage after the root value → reject.
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment) return null;
                    }
                    return token;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string Str(JToken value, string fallback = "") =>
            value != null && value.Type == JTokenType.String ? value.Value<string>() : fallback;

        public static bool Bool(JToken value, bool fallback = false) =>
            value != null && value.Type == JTokenType.Boolean ? value.Value<bool>() : fallback;

        public static long Long(JToken value, long fallback = 0)
        {
            var v = OptLong(value);
            return v ?? fallback;
        }

        public static int Int(JToken value, int fallback = 0)
        {
            var v = OptLong(value);
            if (!v.HasValue) return fallback;
            if (v.Value > int.MaxValue) return int.MaxValue;
            if (v.Value < int.MinValue) return int.MinValue;
            return (int)v.Value;
        }

        public static double Num(JToken value, double fallback = 0)
        {
            if (value == null) return fallback;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                try { return value.Value<double>(); } catch (Exception) { return fallback; }
            }
            return fallback;
        }

        /// <summary>Integer (or integral float) token → long; everything else → null.</summary>
        public static long? OptLong(JToken value)
        {
            if (value == null) return null;
            try
            {
                if (value.Type == JTokenType.Integer) return value.Value<long>();
                if (value.Type == JTokenType.Float)
                {
                    var d = value.Value<double>();
                    if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                    if (d > long.MaxValue || d < long.MinValue) return null;
                    return (long)Math.Floor(d);
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }
    }
}
