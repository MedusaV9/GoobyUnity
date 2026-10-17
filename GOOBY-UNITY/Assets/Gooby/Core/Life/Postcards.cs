using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Postcard archive + souvenir set bonus — port of scripts/ui/postkarten/postkarten_logic.gd
    /// (web postcards.js verbatim: FNV-1a hash, mulberry32 variant pick, FIFO archive of 36).
    /// Installs itself as Vacation.PostcardArchive so Vacation.Tick fills the archive during a trip.
    /// </summary>
    public sealed class Postcards : Vacation.IPostcardArchive
    {
        public const long MsPerDay = 86_400_000L;
        public const int MaxArchive = 36;
        public const int Variants = 5;
        public static readonly string[] DestIds = { "beach", "meadowTrip", "bigCity", "space", "harbor", "spookGarden", "bakery", "nightSky", "toyRoom" };
        public static readonly (int n, int coins)[] SetStufen = { (3, 150), (6, 300), (9, 600) };

        public static void Install() => Vacation.PostcardArchive = new Postcards();

        public static uint HashStr(string text)
        {
            var h = 2166136261u;
            foreach (var ch in text)
            {
                h ^= ch;
                h = unchecked(h * 16777619u);
            }
            return h;
        }

        public static uint TripSeed(string destId, long bookedAt) => HashStr(destId) ^ unchecked((uint)bookedAt);

        public static int VariantOf(string destId, long bookedAt, int dayIndex)
        {
            var k = (uint)Math.Max(1, dayIndex);
            var seed = TripSeed(destId, bookedAt) ^ unchecked(k * 0x9e3779b1u);
            var rnd = Mulberry32(seed);
            return 1 + (int)Math.Floor(rnd * Variants) % Variants;
        }

        private static double Mulberry32(uint seed)
        {
            var a = unchecked(seed + 0x6D2B79F5u);
            var t = unchecked((a ^ (a >> 15)) * (1u | a));
            t = unchecked(t + (t ^ (t >> 7)) * (61u | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }

        public static string TextKey(JObject entry)
        {
            var variant = J.ClampI((int)Math.Floor(J.NumOr(entry["variant"], 1.0)), 1, Variants);
            return $"postkarten.text.{J.Str(entry["destId"])}.{Math.Max(1, variant)}";
        }

        public static JObject NormalizeEntry(JToken raw)
        {
            if (!(raw is JObject e)) return null;
            var destId = J.Str(e["destId"]);
            if (destId.Length == 0 || Array.IndexOf(DestIds, destId) < 0) return null;
            var day = J.LeafNum(e["dayIndex"]);
            var var_ = J.LeafNum(e["variant"]);
            var at = J.LeafNum(e["atMs"]);
            if (!J.IsNum(e["dayIndex"]) || !J.IsNum(e["variant"]) || !J.IsNum(e["atMs"])) return null;
            var dayIndex = (int)Math.Floor(day);
            var variant = (int)Math.Floor(var_);
            if (dayIndex < 1 || variant < 1 || at < 0.0) return null;
            return new JObject { ["destId"] = destId, ["dayIndex"] = dayIndex, ["variant"] = variant, ["atMs"] = (long)at };
        }

        public static JArray NormalizeArchive(JToken raw)
        {
            if (!(raw is JArray arr)) return new JArray();
            var clean = new List<JObject>();
            var seen = new HashSet<string>();
            foreach (var item in arr)
            {
                var entry = NormalizeEntry(item);
                if (entry == null) continue;
                var key = $"{J.Str(entry["destId"])}|{J.Long(entry["atMs"])}|{J.Int(entry["dayIndex"])}";
                if (!seen.Add(key)) continue;
                clean.Add(entry);
            }
            var sorted = clean.OrderBy(e => J.Long(e["atMs"])).ToList();
            if (sorted.Count > MaxArchive) sorted = sorted.Skip(sorted.Count - MaxArchive).ToList();
            return new JArray(sorted);
        }

        public static (JArray archive, int lastDay, int added) ProcessPostcardsUpTo(JObject v, long nowMs)
        {
            var archive = NormalizeArchive(v["archive"]);
            var last = Math.Max(0, (int)Math.Floor(J.Num(v["lastPostcardDayProcessed"])));
            var destId = J.Str(v["destId"]);
            var bookedAt = J.Num(v["bookedAt"]);
            var returnAt = J.Num(v["returnAt"]);
            if (destId.Length == 0 || Array.IndexOf(DestIds, destId) < 0) return (archive, last, 0);
            if (!(bookedAt > 0.0) || !(returnAt > bookedAt)) return (archive, last, 0);
            var totalDays = (int)Math.Round((returnAt - bookedAt) / MsPerDay, MidpointRounding.AwayFromZero);
            var maxCards = Math.Max(0, totalDays - 1);
            var fullDays = (int)Math.Floor((Math.Min(nowMs, returnAt) - bookedAt) / MsPerDay);
            var due = Math.Max(0, Math.Min(fullDays, maxCards));
            var added = 0;
            for (var k = last + 1; k <= due; k++)
            {
                archive.Add(new JObject { ["destId"] = destId, ["dayIndex"] = k, ["variant"] = VariantOf(destId, (long)bookedAt, k), ["atMs"] = (long)bookedAt + k * MsPerDay });
                added++;
            }
            if (archive.Count > MaxArchive) archive = new JArray(archive.Skip(archive.Count - MaxArchive));
            return (archive, Math.Max(last, due), added);
        }

        (JArray archive, int lastDay, int added) Vacation.IPostcardArchive.ProcessUpTo(JObject vacationSlice, long nowMs) => ProcessPostcardsUpTo(vacationSlice, nowMs);

        public static JArray ArchiveOf(JObject state) => NormalizeArchive(J.Dict(state["vacation"])["archive"]);

        public static List<string> SouvenirsVon(JObject state)
        {
            var visited = J.Dict(state["vacation"])["visited"] as JObject;
            var outp = new List<string>();
            if (visited == null) return outp;
            foreach (var id in DestIds) if (J.IsTrue(visited[id])) outp.Add(id);
            return outp;
        }

        public static int BesuchtAnzahl(JObject state) => SouvenirsVon(state).Count;

        public struct Stufe
        {
            public int N, Coins;
            public bool Erreicht, Abgeholt;
        }

        public static List<Stufe> SetStufenOf(JObject state)
        {
            var besucht = BesuchtAnzahl(state);
            var claimed = Claimed(state);
            return SetStufen.Select(s => new Stufe { N = s.n, Coins = s.coins, Erreicht = besucht >= s.n, Abgeholt = claimed.ContainsKey(s.n.ToString()) }).ToList();
        }

        /// <summary>Marks the tier claimed and returns its coins (0 when not reached / already claimed). Caller books the coins.</summary>
        public static int ClaimSetBonus(JObject state, int n, long nowMs)
        {
            var besucht = BesuchtAnzahl(state);
            var claimed = Claimed(state);
            foreach (var s in SetStufen)
            {
                if (s.n != n) continue;
                if (besucht < n || claimed.ContainsKey(n.ToString())) return 0;
                if (!(state["vacation"] is JObject v)) return 0;
                J.EnsureDict(v, "setBonusClaimed")[n.ToString()] = nowMs;
                return s.coins;
            }
            return 0;
        }

        private static JObject Claimed(JObject state) => J.Dict(state["vacation"])["setBonusClaimed"] as JObject ?? new JObject();
    }
}
