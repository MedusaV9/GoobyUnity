using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>
    /// Collections album (web collections.js verbatim sets) — port of scripts/ui/album/collections_logic.gd.
    /// Pure slice transforms ({entries, claimedSets}); ApplyClaim books coins + XP + the furniture reward into storage.
    /// </summary>
    public static class CollectionsLogic
    {
        public const int XpSetComplete = 50;

        public sealed class SetDef
        {
            public string Id, Furniture;
            public string[] Entries;
            public int Coins;
        }

        public static readonly IReadOnlyDictionary<string, string> VeggieByCrop = new Dictionary<string, string> { { "tomate", "tomato" }, { "melone", "watermelon" }, { "salat", "salad" } };
        public static readonly IReadOnlyDictionary<string, string> TreatByFood = new Dictionary<string, string> { { "cupcakePink", "cupcake" } };

        public static readonly SetDef[] Sets =
        {
            new SetDef { Id = "fish", Entries = new[] { "sunnyCarp", "blueDace", "pinkKoi", "stripeBass", "tinyMinnow", "bigWhopper", "nightEel", "goldenFish" }, Coins = 200, Furniture = "proc:goldfishBowl" },
            new SetDef { Id = "veggies", Entries = new[] { "radish", "carrot", "salad", "tomato", "corn", "eggplant", "pumpkin", "watermelon" }, Coins = 150, Furniture = "proc:goldenWateringCan" },
            new SetDef { Id = "landmarks", Entries = new[] { "shop", "vetClinic", "fountain", "skyTower", "parkGazebo", "windmillCafe" }, Coins = 150, Furniture = "proc:toyCity" },
            new SetDef { Id = "treats", Entries = new[] { "donut-sprinkles", "cupcake", "ice-cream", "cake", "cookie", "candy-bar", "lollypop", "sundae", "chocolate", "muffin" }, Coins = 150, Furniture = "proc:candyJar" },
        };

        public static SetDef GetSet(string setId) => Sets.FirstOrDefault(s => s.Id == setId);

        public static string EntryKey(string setId, string entryId) => setId + "." + entryId;

        public struct AwardResult
        {
            public JObject C;
            public bool First;
        }

        /// <summary>Pure award: returns a NEW slice with the entry count raised (same reference when nothing changes).</summary>
        public static AwardResult Award(JObject c, string setId, string entryId, int n = 1)
        {
            if (string.IsNullOrEmpty(setId) || string.IsNullOrEmpty(entryId) || n <= 0) return new AwardResult { C = c, First = false };
            var prev = CountOf(c, setId, entryId);
            var next = NormalizeSlice(c);
            ((JObject)next["entries"])[EntryKey(setId, entryId)] = prev + n;
            return new AwardResult { C = next, First = prev == 0 };
        }

        public static bool AwardInState(JObject state, string setId, string entryId, bool firstOnly = false)
        {
            if (string.IsNullOrEmpty(entryId)) return false;
            var c = NormalizeSlice(state["collections"]);
            if (firstOnly && CountOf(c, setId, entryId) > 0) return false;
            var res = Award(c, setId, entryId);
            state["collections"] = res.C;
            return res.First;
        }

        /// <summary>Minigame result payload {"collections": {setId: [entryId,…]}} → album entries (landmarks first-only).</summary>
        public static void AwardReport(JObject state, JObject result)
        {
            if (!(result?["collections"] is JObject payload)) return;
            foreach (var p in payload.Properties())
            {
                if (!(p.Value is JArray entries)) continue;
                foreach (var e in entries) AwardInState(state, p.Name, e.ToString(), p.Name == "landmarks");
            }
        }

        public static string VeggieEntryForCrop(string cropId) => EntryInSet("veggies", VeggieByCrop.TryGetValue(cropId ?? "", out var m) ? m : cropId);

        public static string TreatEntryForFood(string foodId) => EntryInSet("treats", TreatByFood.TryGetValue(foodId ?? "", out var m) ? m : foodId);

        private static string EntryInSet(string setId, string entryId)
        {
            var def = GetSet(setId);
            return def != null && Array.IndexOf(def.Entries, entryId) >= 0 ? entryId : "";
        }

        public static JObject NormalizeSlice(JToken raw)
        {
            var outp = new JObject { ["entries"] = new JObject(), ["claimedSets"] = new JObject() };
            if (!(raw is JObject r)) return outp;
            if (r["entries"] is JObject entries)
                foreach (var p in entries.Properties())
                {
                    var count = (int)J.NumOr(p.Value, 0.0);
                    if (p.Name.Length > 0 && count >= 1) ((JObject)outp["entries"])[p.Name] = count;
                }
            if (r["claimedSets"] is JObject claimed)
                foreach (var p in claimed.Properties())
                    if (p.Name.Length > 0) ((JObject)outp["claimedSets"])[p.Name] = (long)J.NumOr(p.Value, 0.0);
            return outp;
        }

        public static int CountOf(JObject c, string setId, string entryId)
        {
            if (!(c?["entries"] is JObject entries)) return 0;
            return (int)J.NumOr(entries[EntryKey(setId, entryId)], 0.0);
        }

        public static bool IsSetComplete(JObject c, string setId)
        {
            var def = GetSet(setId);
            if (def == null || def.Entries.Length == 0) return false;
            return def.Entries.All(id => CountOf(c, setId, id) >= 1);
        }

        public static (int have, int total) SetProgress(JObject c, string setId)
        {
            var def = GetSet(setId);
            if (def == null) return (0, 0);
            return (def.Entries.Count(id => CountOf(c, setId, id) >= 1), def.Entries.Length);
        }

        public static (int have, int total) TotalProgress(JObject c)
        {
            int have = 0, total = 0;
            foreach (var def in Sets)
            {
                var p = SetProgress(c, def.Id);
                have += p.have;
                total += p.total;
            }
            return (have, total);
        }

        public static bool IsClaimed(JObject c, string setId) => c?["claimedSets"] is JObject claimed && claimed.ContainsKey(setId);

        public sealed class Reward
        {
            public int Coins, Xp;
            public string Furniture;
        }

        public static Reward RewardOf(string setId)
        {
            var def = GetSet(setId);
            return def == null ? null : new Reward { Coins = def.Coins, Furniture = def.Furniture, Xp = XpSetComplete };
        }

        public struct ClaimResult
        {
            public bool Ok;
            public JObject C;
            public Reward Reward;
        }

        public static ClaimResult ClaimSet(JObject c, string setId, long nowMs)
        {
            if (IsClaimed(c, setId) || !IsSetComplete(c, setId)) return new ClaimResult { Ok = false };
            var next = NormalizeSlice(c);
            ((JObject)next["claimedSets"])[setId] = nowMs;
            return new ClaimResult { Ok = true, C = next, Reward = RewardOf(setId) };
        }

        /// <summary>Books the set reward (coins via Economy, +50 XP via Leveling, furniture into home.storage). null when denied.</summary>
        public static Reward ApplyClaim(GameStateStore gs, string setId, long nowMs, string day = "")
        {
            if (gs == null) return null;
            var res = ClaimSet(NormalizeSlice(gs.State["collections"]), setId, nowMs);
            if (!res.Ok) return null;
            var reward = res.Reward;
            gs.Update(state =>
            {
                state["collections"] = res.C;
                StoreFurniture(state, reward.Furniture);
                if (state["economy"] is JObject econ)
                {
                    Economy.Award(econ, reward.Coins, "album", day);
                    GrantXp(state, econ, reward.Xp, day);
                }
            });
            gs.NotifySliceChanged("collections");
            return reward;
        }

        private static void GrantXp(JObject state, JObject econ, int xp, string day)
        {
            if (xp <= 0 || !(state["progression"] is JObject prog)) return;
            var res = Leveling.ApplyXp(J.NumOr(prog["xp"], 0.0), (int)J.NumOr(prog["level"], 1.0), xp);
            prog["xp"] = res.Xp;
            prog["level"] = res.Level;
            if (res.CoinsAwarded > 0) Economy.Award(econ, res.CoinsAwarded, "levelUp", day);
        }

        private static void StoreFurniture(JObject state, string furnitureId)
        {
            if (string.IsNullOrEmpty(furnitureId) || !(state["home"] is JObject home)) return;
            if (!(home["storage"] is JArray)) home["storage"] = new JArray();
            StorageLogic.Add((JArray)home["storage"], furnitureId);
        }
    }
}
