using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>
    /// Anonymous TOFU device identity for GOOBY-SERVER (auth.js): deviceId + deviceSecret
    /// (the server stores only a hash), the friend code learned from WELCOME, and the ids of
    /// GoobyPal deliveries that were already credited locally (dedupe for re-deliveries).
    /// Persisted as JSON with an atomic tmp+replace write.
    /// </summary>
    public sealed class NetIdentity
    {
        /// <summary>Most recent credited PAL delivery ids kept for dedupe.</summary>
        public const int CreditedPalCap = 200;

        private static readonly Regex DeviceIdRe = new Regex("^[A-Za-z0-9._-]{8,64}$");
        private static readonly Regex SecretRe = new Regex("^[0-9a-fA-F]{32,64}$");

        private readonly List<string> _creditedOrder = new List<string>();

        public string Path { get; private set; }
        public string DeviceId { get; private set; }
        public string DeviceSecret { get; private set; }
        /// <summary>"GOOBY-XXXX" from the last WELCOME ("" until the first successful connect).</summary>
        public string FriendCode { get; set; } = "";
        /// <summary>PAL delivery ids already credited (use <see cref="MarkPalCredited"/> to keep the cap).</summary>
        public HashSet<string> CreditedPal { get; } = new HashSet<string>();

        /// <summary>REST header value: "Bearer deviceId:deviceSecret".</summary>
        public string Bearer => "Bearer " + DeviceId + ":" + DeviceSecret;

        private NetIdentity() { }

        /// <summary>Fresh random identity ("un-" + 32 hex, 64 hex secret). Not saved yet.</summary>
        public static NetIdentity CreateNew(string path = null)
        {
            return new NetIdentity
            {
                Path = path,
                DeviceId = "un-" + NetRandom.Hex(16),
                DeviceSecret = NetRandom.Hex(32),
                FriendCode = "",
            };
        }

        /// <summary>Loads the identity at <paramref name="path"/>; missing/corrupt → new identity (saved).</summary>
        public static NetIdentity LoadOrCreate(string path)
        {
            var loaded = TryLoad(path);
            if (loaded != null) return loaded;
            var fresh = CreateNew(path);
            fresh.Save();
            return fresh;
        }

        private static NetIdentity TryLoad(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                var o = NetJson.ParseObject(File.ReadAllText(path, Encoding.UTF8));
                if (o == null) return null;
                var id = NetJson.Str(o["deviceId"]);
                var secret = NetJson.Str(o["deviceSecret"]);
                if (!DeviceIdRe.IsMatch(id) || !SecretRe.IsMatch(secret)) return null;
                var identity = new NetIdentity
                {
                    Path = path,
                    DeviceId = id,
                    DeviceSecret = secret,
                    FriendCode = NetJson.Str(o["friendCode"]),
                };
                if (o["creditedPal"] is JArray arr)
                {
                    foreach (var t in arr)
                    {
                        var s = NetJson.Str(t);
                        if (s.Length > 0) identity.MarkPalCredited(s);
                    }
                }
                return identity;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool IsPalCredited(string id) => !string.IsNullOrEmpty(id) && CreditedPal.Contains(id);

        /// <summary>The account moved away from this device (its secret is void on the server): start over as a fresh guest.</summary>
        public void Renew()
        {
            DeviceId = "un-" + NetRandom.Hex(16);
            DeviceSecret = NetRandom.Hex(32);
            FriendCode = "";
            CreditedPal.Clear();
            _creditedOrder.Clear();
            Save();
        }

        /// <summary>
        /// Account move (MOVE_RESULT of move.js): this device takes over the server identity of the old one — its deviceId,
        /// the freshly rotated secret and the friend code — and saves it. False (nothing changed) for malformed values.
        /// </summary>
        public bool Adopt(string deviceId, string deviceSecret, string friendCode)
        {
            if (!DeviceIdRe.IsMatch(deviceId ?? "") || !SecretRe.IsMatch(deviceSecret ?? "")) return false;
            DeviceId = deviceId;
            DeviceSecret = deviceSecret;
            FriendCode = friendCode ?? "";
            CreditedPal.Clear();
            _creditedOrder.Clear();
            Save();
            return true;
        }

        /// <summary>Records a credited PAL delivery id (keeps the newest <see cref="CreditedPalCap"/>). False if already known.</summary>
        public bool MarkPalCredited(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            SyncOrder();
            if (!CreditedPal.Add(id)) return false;
            _creditedOrder.Add(id);
            while (_creditedOrder.Count > CreditedPalCap)
            {
                CreditedPal.Remove(_creditedOrder[0]);
                _creditedOrder.RemoveAt(0);
            }
            return true;
        }

        // Callers may have touched the public HashSet directly — keep the order list consistent.
        private void SyncOrder()
        {
            if (_creditedOrder.Count == CreditedPal.Count) return;
            _creditedOrder.RemoveAll(x => !CreditedPal.Contains(x));
            var known = new HashSet<string>(_creditedOrder);
            foreach (var id in CreditedPal)
                if (!known.Contains(id)) _creditedOrder.Add(id);
        }

        /// <summary>Atomic save (tmp + replace). Returns false when the file could not be written.</summary>
        public bool Save()
        {
            if (string.IsNullOrEmpty(Path)) return false;
            SyncOrder();
            var o = new JObject
            {
                ["v"] = 1,
                ["deviceId"] = DeviceId,
                ["deviceSecret"] = DeviceSecret,
                ["friendCode"] = FriendCode ?? "",
                ["creditedPal"] = new JArray(_creditedOrder.ToArray()),
            };
            return NetFiles.WriteAtomic(Path, o.ToString(Formatting.None));
        }
    }

    /// <summary>Crypto-random helpers (netstandard2.1-safe).</summary>
    public static class NetRandom
    {
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        public static byte[] Bytes(int count)
        {
            var b = new byte[count];
            lock (Rng) Rng.GetBytes(b);
            return b;
        }

        /// <summary>Lower-case hex of <paramref name="byteCount"/> random bytes (2 chars per byte).</summary>
        public static string Hex(int byteCount) => ToHex(Bytes(byteCount));

        public static string ToHex(byte[] bytes)
        {
            const string digits = "0123456789abcdef";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(digits[b >> 4]);
                sb.Append(digits[b & 0xF]);
            }
            return sb.ToString();
        }
    }

    /// <summary>Atomic small-file writes (write .tmp, then replace/move).</summary>
    public static class NetFiles
    {
        public static bool WriteAtomic(string path, string text)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch (Exception)
                    {
                        // Some platforms/filesystems lack Replace — fall back to delete + move.
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
