using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Save
{
    /// <summary>
    /// Save-file transfer ("Umzugskoffer") — port of scripts/state/moving_box_import.gd. Accepts two formats:
    ///   1. the raw JSON of any older save (web v0–v4 → full migration chain, Godot/Unity v5 → normalize);
    ///   2. the compact <c>GOOBY5.&lt;base64url(gzip(json))&gt;.&lt;crc32hex&gt;</c> code produced by ExportCode,
    ///      byte-compatible with the Godot version so codes travel Godot ↔ Unity in both directions.
    /// Pure logic: text in, {Ok, State, Error, Report} out. Callers back up the current save before applying.
    /// </summary>
    public static class SaveTransfer
    {
        public const string CodePrefix = "GOOBY5";
        public const int MaxDecompressedBytes = 16 * 1024 * 1024;

        public struct Result
        {
            public bool Ok;
            public JObject State;
            public string Error;
            public JObject Report;
        }

        public struct Decoded
        {
            public bool Ok;
            public string Json;
            public string Error;
        }

        /// <summary>Import from pasted text (raw JSON or GOOBY5 code).</summary>
        public static Result ImportText(string text, long nowMs)
        {
            var trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) return Err("empty input");
            var jsonText = trimmed;
            if (trimmed.StartsWith(CodePrefix + ".", StringComparison.Ordinal))
            {
                var decoded = DecodeCode(trimmed);
                if (!decoded.Ok) return Err(decoded.Error);
                jsonText = decoded.Json;
            }
            JToken parsed;
            try { parsed = JToken.Parse(jsonText); }
            catch (Exception e) { return Err("invalid JSON: " + e.Message); }
            if (!(parsed is JObject obj)) return Err("save is not an object");
            var v = obj["v"];
            if (J.IsNum(v) && (int)v.Value<double>() == SaveSchema.SchemaVersion)
            {
                var normalized = SaveSchema.Normalize(obj, nowMs);
                if (!normalized.Ok) return Err(normalized.Error);
                var s = normalized.State;
                return new Result
                {
                    Ok = true, State = s, Error = "",
                    Report = new JObject
                    {
                        ["importedFrom"] = "godot-v5",
                        ["level"] = J.Int(J.Path(s, "progression", "level"), 1),
                        ["coins"] = J.Long(J.Path(s, "economy", "coins")),
                        ["stickers"] = (J.Path(s, "stickers", "unlocked") as JContainer)?.Count ?? 0,
                        ["outfits"] = (J.Path(s, "cosmetics", "outfits", "owned") as JContainer)?.Count ?? 0,
                    },
                };
            }
            if (J.IsNum(v) && v.Value<double>() > SaveSchema.SchemaVersion)
                return Err($"forward version {(int)v.Value<double>()} — bitte App updaten");
            var migrated = SaveMigration.MigrateAny(obj, nowMs);
            return new Result { Ok = migrated.Ok, State = migrated.State, Error = migrated.Error ?? "", Report = migrated.Report };
        }

        /// <summary>Export a v5 state as <c>GOOBY5.&lt;base64url(gzip(json))&gt;.&lt;crc32hex&gt;</c>.</summary>
        public static string ExportCode(JObject state)
        {
            var json = state.ToString(Newtonsoft.Json.Formatting.None);
            var bytes = Gzip(Encoding.UTF8.GetBytes(json));
            return CodePrefix + "." + Base64Url(bytes) + "." + Crc32(bytes).ToString("x8");
        }

        /// <summary>Strict GOOBY5-code decode (format, base64url, CRC32 over the gzip bytes, gzip).</summary>
        public static Decoded DecodeCode(string code)
        {
            var parts = (code ?? "").Trim().Split('.');
            if (parts.Length != 3 || parts[0] != CodePrefix) return new Decoded { Error = "malformed GOOBY5 code" };
            var bytes = Base64UrlDecode(parts[1]);
            if (bytes == null || bytes.Length == 0) return new Decoded { Error = "invalid base64url payload" };
            var want = parts[2].Trim().ToLowerInvariant();
            var got = Crc32(bytes).ToString("x8");
            if (got != want) return new Decoded { Error = $"CRC mismatch ({got} != {want})" };
            var raw = Gunzip(bytes, MaxDecompressedBytes);
            if (raw == null || raw.Length == 0) return new Decoded { Error = "gzip payload did not decompress" };
            return new Decoded { Ok = true, Json = Encoding.UTF8.GetString(raw), Error = "" };
        }

        private static byte[] Gzip(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Gunzip(byte[] data, int max)
        {
            try
            {
                using (var input = new MemoryStream(data))
                using (var gz = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[16 * 1024];
                    int read;
                    while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        if (output.Length > max) return null;
                    }
                    return output.ToArray();
                }
            }
            catch { return null; }
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static byte[] Base64UrlDecode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length % 4 == 1) return null;
            foreach (var c in text)
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_')) return null;
            var b64 = text.Replace('-', '+').Replace('_', '/');
            while (b64.Length % 4 != 0) b64 += "=";
            try { return Convert.FromBase64String(b64); }
            catch { return null; }
        }

        private static uint[] _crcTable;

        /// <summary>CRC-32 (IEEE 802.3, like zlib.crc32 / Godot's moving_box_import.crc32).</summary>
        public static uint Crc32(byte[] bytes)
        {
            if (_crcTable == null)
            {
                var table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    var c = i;
                    for (var bit = 0; bit < 8; bit++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[i] = c;
                }
                _crcTable = table;
            }
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static Result Err(string message) => new Result { Ok = false, State = null, Error = message, Report = new JObject() };
    }
}
