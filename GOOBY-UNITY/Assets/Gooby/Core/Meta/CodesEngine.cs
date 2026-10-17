using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    public sealed class CodeDef
    {
        public string Id, Secret, SecretSha256;
        public JObject Effect;
        public bool Once = true, DevOnly;

        public bool Matches(string normalized)
        {
            if (!string.IsNullOrEmpty(Secret)) return Secret == normalized;
            if (!string.IsNullOrEmpty(SecretSha256)) return string.Equals(SecretSha256, CodesEngine.Sha256Hex(normalized), StringComparison.OrdinalIgnoreCase);
            return false;
        }
    }

    /// <summary>
    /// Bonus codes — port of codes_engine.gd (built-in codes) merged with the content/codes pack (sha256 secrets),
    /// attempt window + lockout, once-latch, buff/coins/sticker/unlock_flag effects (codes_screen.gd apply path).
    /// </summary>
    public static class CodesEngine
    {
        public const int LockAfter = 5;
        public const int LockWindowSec = 60;
        public const int LockSec = 30;
        public const string PackPath = "content/codes/data/codes";

        /// <summary>Built-in codes (codes_engine.gd CODES) — the pack can add more.</summary>
        public static readonly CodeDef[] Builtin =
        {
            new CodeDef { Id = "updateLiebe", Secret = "updateliebe", Effect = new JObject { ["buff"] = "doubleCoins", ["minutes"] = 10 }, Once = true },
            new CodeDef { Id = "herzGooby", Secret = "ichlie3bdich", Effect = new JObject { ["sticker"] = "herzGooby", ["coins"] = 50 }, Once = true },
            new CodeDef { Id = "goldiGold", Secret = "goldigold", Effect = new JObject { ["unlock_flag"] = "gvz.goldi" }, Once = true },
        };

        public static bool IncludeDevCodes { get; set; }

        private static List<CodeDef> _all;

        public static IReadOnlyList<CodeDef> All()
        {
            if (_all != null) return _all;
            var list = new List<CodeDef>(Builtin);
            foreach (var t in ContentSource.LoadItems(PackPath))
            {
                if (!(t is JObject o)) continue;
                var def = new CodeDef
                {
                    Id = J.Str(o["id"]), Secret = J.Str(o["secret"]), SecretSha256 = J.Str(o["secret_sha256"]),
                    Effect = NormalizePackEffect(o["effect"] as JObject), Once = o["once"]?.Type == JTokenType.Boolean ? o["once"].Value<bool>() : true, DevOnly = J.IsTrue(o["dev_only"]),
                };
                if (def.Id.Length == 0 || (def.DevOnly && !IncludeDevCodes)) continue;
                if (list.Any(c => c.Id == def.Id)) continue;
                list.Add(def);
            }
            return _all = list;
        }

        public static void ResetCache() => _all = null;

        /// <summary>Pack effects use {type, amount|sticker_id|flag}; map them onto the engine's flat effect keys.</summary>
        private static JObject NormalizePackEffect(JObject e)
        {
            if (e == null) return new JObject();
            switch (J.Str(e["type"]))
            {
                case "coins": return new JObject { ["coins"] = J.Int(e["amount"]) };
                case "sticker": return new JObject { ["sticker"] = J.Str(e["sticker_id"]) };
                case "unlock_flag": return new JObject { ["unlock_flag"] = J.Str(e["flag"]) };
                case "buff": return new JObject { ["buff"] = J.Str(e["buff"], "doubleCoins"), ["minutes"] = J.Int(e["minutes"], 10) };
            }
            return (JObject)e.DeepClone();
        }

        public static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Normalize(JToken input) => input != null && input.Type == JTokenType.String ? Normalize(input.Value<string>()) : "";

        public static string Normalize(string input)
        {
            if (input == null) return "";
            var sb = new StringBuilder();
            foreach (var ch in input.Trim().ToLowerInvariant())
                if (!(ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r' || ch == '\v' || ch == '\f')) sb.Append(ch);
            return sb.ToString();
        }

        public static CodeDef CodeBySecret(string secret) => string.IsNullOrEmpty(secret) ? null : All().FirstOrDefault(c => c.Matches(secret));
        public static CodeDef CodeById(string id) => All().FirstOrDefault(c => c.Id == id);

        public struct RedeemResult
        {
            public bool Ok;
            public string Reason;
            public CodeDef Code;
        }

        /// <summary>Pure redeem (mutates state.codes + attempts). Reasons: locked / unknown / already.</summary>
        public static RedeemResult Redeem(JObject state, string input, long nowMs, List<long> attempts)
        {
            var codes = EnsureCodesSlice(state);
            if (J.Num(codes["lockUntil"]) > nowMs) return new RedeemResult { Ok = false, Reason = "locked" };
            var secret = Normalize(input);
            var code = secret.Length > 0 ? CodeBySecret(secret) : null;
            if (code == null)
            {
                var windowStart = nowMs - LockWindowSec * 1000L;
                while (attempts.Count > 0 && attempts[0] < windowStart) attempts.RemoveAt(0);
                attempts.Add(nowMs);
                if (attempts.Count >= LockAfter)
                {
                    codes["lockUntil"] = nowMs + LockSec * 1000L;
                    attempts.Clear();
                }
                return new RedeemResult { Ok = false, Reason = "unknown" };
            }
            var redeemed = (JObject)codes["redeemed"];
            if (code.Once && redeemed.ContainsKey(code.Id)) return new RedeemResult { Ok = false, Reason = "already" };
            redeemed[code.Id] = nowMs;
            return new RedeemResult { Ok = true, Reason = "", Code = code };
        }

        /// <summary>Redeem through the store and apply the effect (coins / double-coins buff / unlock flag / counter).</summary>
        public static RedeemResult RedeemAndApply(GameStateStore gs, string input, List<long> attempts)
        {
            if (Normalize(input).Length == 0) return new RedeemResult { Ok = false, Reason = "empty" };
            var now = gs.Clock.NowMs();
            var result = default(RedeemResult);
            gs.Update(state =>
            {
                result = Redeem(state, input, now, attempts);
                if (!result.Ok) return;
                ApplyEffect(state, result.Code.Effect ?? new JObject(), now, "code");
            });
            gs.NotifySliceChanged("codes");
            if (result.Ok && result.Code.Effect?["unlock_flag"] != null)
                gs.NotifySliceChanged(J.Str(result.Code.Effect["unlock_flag"]).Split('.')[0]);
            return result;
        }

        /// <summary>
        /// An online code's reward (codes.js: the panel's free JSON — coins, sticker, buff + minutes, unlock_flag) through the
        /// same effect path as the built-in codes; it lands in the history as "srv:CODE". A sticker the catalog knows is
        /// granted directly. Returns the code as a <see cref="CodeDef"/> for the success lines.
        /// </summary>
        public static CodeDef ApplyServerReward(GameStateStore gs, string code, JObject reward)
        {
            var def = new CodeDef { Id = "srv:" + (code ?? "").Trim().ToUpperInvariant(), Effect = reward ?? new JObject(), Once = true };
            var now = gs.Clock.NowMs();
            gs.Update(state =>
            {
                ((JObject)EnsureCodesSlice(state)["redeemed"])[def.Id] = now;
                ApplyEffect(state, def.Effect, now, "code_redeem");
                var sticker = J.Str(def.Effect["sticker"]);
                if (sticker.Length > 0 && StickerCatalog.ById(sticker) != null && !StickerRules.IsUnlocked(state, sticker))
                    J.EnsureDict(J.EnsureDict(state, "stickers"), "unlocked")[sticker] = now;
            });
            gs.NotifySliceChanged("codes");
            if (def.Effect["sticker"] != null) gs.NotifySliceChanged("stickers");
            if (def.Effect["unlock_flag"] != null) gs.NotifySliceChanged(J.Str(def.Effect["unlock_flag"]).Split('.')[0]);
            return def;
        }

        /// <summary>coins (one money path) · doubleCoins buff for n minutes · unlock flag · the codesRedeemed counter.</summary>
        private static void ApplyEffect(JObject state, JObject effect, long now, string reason)
        {
            if (effect["coins"] != null && state["economy"] is JObject econ) Economy.Award(econ, J.Int(effect["coins"]), reason);
            if (J.Str(effect["buff"]) == "doubleCoins")
                J.EnsureDict(EnsureCodesSlice(state), "buffs")["doubleCoinsUntil"] = now + J.Int(effect["minutes"]) * 60_000L;
            if (effect["unlock_flag"] != null) SetFlag(state, J.Str(effect["unlock_flag"]));
            var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
            counters["codesRedeemed"] = J.Int(counters["codesRedeemed"]) + 1;
        }

        /// <summary>"gvz.goldi" → state.gvz.flags.goldi = true (codes_screen._setze_flag).</summary>
        private static void SetFlag(JObject state, string path)
        {
            var parts = path.Split('.');
            if (parts.Length < 2) return;
            var slice = J.EnsureDict(state, parts[0]);
            J.EnsureDict(slice, "flags")[parts[1]] = true;
        }

        public static bool IsFlagSet(JObject state, string path)
        {
            var parts = path.Split('.');
            return parts.Length >= 2 && J.IsTrue(J.Path(state, parts[0], "flags", parts[1]));
        }

        public static bool IsDoubleCoinsActive(JObject state, long nowMs) => RemainingMs(state, nowMs) > 0;

        public static long RemainingMs(JObject state, long nowMs) => Math.Max(0L, (long)J.Num(J.Path(state, "codes", "buffs", "doubleCoinsUntil")) - nowMs);

        public static long LockRemainingMs(JObject state, long nowMs) => Math.Max(0L, (long)J.Num(J.Path(state, "codes", "lockUntil")) - nowMs);

        public static List<(string id, long atMs)> RedeemedEntries(JObject state)
        {
            if (!(J.Path(state, "codes", "redeemed") is JObject redeemed)) return new List<(string, long)>();
            return redeemed.Properties().Select(p => (p.Name, (long)J.Num(p.Value))).OrderByDescending(e => e.Item2).ToList();
        }

        private static JObject EnsureCodesSlice(JObject state)
        {
            if (!(state["codes"] is JObject)) state["codes"] = new JObject { ["redeemed"] = new JObject(), ["lockUntil"] = 0, ["buffs"] = new JObject { ["doubleCoinsUntil"] = 0 } };
            var codes = (JObject)state["codes"];
            if (!(codes["redeemed"] is JObject)) codes["redeemed"] = new JObject();
            if (!(codes["buffs"] is JObject)) codes["buffs"] = new JObject { ["doubleCoinsUntil"] = 0 };
            return codes;
        }
    }
}
