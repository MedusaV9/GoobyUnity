using System;
using Gooby.Core.Json;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch.Mp
{
    /// <summary>
    /// Save side of the ranch multiplayer (rmp_state.gd, RW-6): additive in the ranch slice under <c>ranch.mp</c>, no
    /// version bump, healed when read. Only keepsakes and statistics land in the save — the server books no coins for these
    /// friendly rounds, Godot did not either:
    /// <c>{v, statistik:{rennen|fangen|parcours:{teilnahmen, siege}}, verlauf:[{rewardId, mode, kurs, rank, zeitMs, ranked, datum}] (≤ 20), gesehen:[rewardId] (≤ 60)}</c>.
    /// </summary>
    public static class RmpState
    {
        public const int VerlaufMax = 20, GesehenMax = 60;
        public static readonly string[] Modi = { RmpKurse.Rennen, RmpKurse.Fangen, RmpKurse.Parcours };

        public static JObject DefaultMp()
        {
            var statistik = new JObject();
            foreach (var m in Modi) statistik[m] = new JObject { ["teilnahmen"] = 0, ["siege"] = 0 };
            return new JObject { ["v"] = 1, ["statistik"] = statistik, ["verlauf"] = new JArray(), ["gesehen"] = new JArray() };
        }

        public static JObject Lese(JObject state) => Normalize(state?["ranch"]?["mp"]);

        public static void Schreibe(JObject state, JObject mp)
        {
            if (state == null) return;
            var ranch = state["ranch"] as JObject;
            if (ranch == null) state["ranch"] = ranch = new JObject();
            ranch["mp"] = mp;
        }

        /// <summary>Self-heal: repair types, keep the caps, never lose foreign keys.</summary>
        public static JObject Normalize(JToken raw)
        {
            var mp = raw is JObject o ? (JObject)o.DeepClone() : DefaultMp();
            mp["v"] = Math.Max(1, J.Int(mp["v"], 1));
            var roh = mp["statistik"] as JObject ?? new JObject();
            var statistik = new JObject();
            foreach (var m in Modi)
            {
                var e = roh[m] as JObject ?? new JObject();
                statistik[m] = new JObject { ["teilnahmen"] = Math.Max(0, J.Int(e["teilnahmen"])), ["siege"] = Math.Max(0, J.Int(e["siege"])) };
            }
            mp["statistik"] = statistik;
            var verlauf = new JArray();
            if (mp["verlauf"] is JArray vr)
                foreach (var e in vr)
                    if (e is JObject row && J.Str(row["rewardId"]).Length > 0) verlauf.Add(row);
            while (verlauf.Count > VerlaufMax) verlauf.RemoveAt(0);
            mp["verlauf"] = verlauf;
            var gesehen = new JArray();
            if (mp["gesehen"] is JArray gr)
                foreach (var e in gr)
                    if (e.Type == JTokenType.String && !Contains(gesehen, (string)e)) gesehen.Add((string)e);
            while (gesehen.Count > GesehenMax) gesehen.RemoveAt(0);
            mp["gesehen"] = gesehen;
            return mp;
        }

        /// <summary>Books a result once (rewardId dedupe, GoobyPal pattern): true = new, false = repeat after a reconnect.</summary>
        public static bool ErgebnisVerbuchen(JObject state, RmpResult result, string datum)
        {
            if (state == null || result == null || string.IsNullOrEmpty(result.RewardId)) return false;
            var mp = Lese(state);
            var gesehen = (JArray)mp["gesehen"];
            if (Contains(gesehen, result.RewardId)) return false;
            gesehen.Add(result.RewardId);
            while (gesehen.Count > GesehenMax) gesehen.RemoveAt(0);
            if (Array.IndexOf(Modi, result.Mode) >= 0)
            {
                var e = (JObject)mp["statistik"][result.Mode];
                e["teilnahmen"] = J.Int(e["teilnahmen"]) + 1;
                if (result.Rank == 1 && !result.Dnf) e["siege"] = J.Int(e["siege"]) + 1;
            }
            var verlauf = (JArray)mp["verlauf"];
            verlauf.Add(new JObject
            {
                ["rewardId"] = result.RewardId, ["mode"] = result.Mode, ["kurs"] = result.Kurs, ["rank"] = result.Rank,
                ["zeitMs"] = result.ZeitMs, ["ranked"] = result.Ranked, ["datum"] = datum ?? "",
            });
            while (verlauf.Count > VerlaufMax) verlauf.RemoveAt(0);
            Schreibe(state, mp);
            return true;
        }

        public static (int teilnahmen, int siege) Statistik(JObject state, string mode)
        {
            var e = Lese(state)["statistik"]?[mode] as JObject;
            return e == null ? (0, 0) : (J.Int(e["teilnahmen"]), J.Int(e["siege"]));
        }

        private static bool Contains(JArray a, string s)
        {
            foreach (var t in a) if (t.Type == JTokenType.String && (string)t == s) return true;
            return false;
        }
    }
}
