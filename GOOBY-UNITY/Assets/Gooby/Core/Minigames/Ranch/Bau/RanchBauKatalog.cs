using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The ranch build catalogue (port of ranch_bau_katalog.gd over minigames/ranch/bau_balance.json — the Godot file
    /// 1:1): costs, levels and effects are data. Facilities (anlage) have levels with a felt use and stand once on the
    /// grid — the pasture-fence upgrade has no placement and lifts every fence; decoration (deko) is 1×1 jewellery,
    /// every five pieces a style point; ground (boden) are paths and surfaces; fences (zaun) sit on edges, 3 m each.
    /// Building costs gold, never energy. A content pack's overrides merge in deeply (<see cref="MitPack"/>). Unity lays
    /// the ranch quests' reward decoration (<see cref="BelohnungenPfad"/>) over the Godot file: free, not for sale, from
    /// the build store only, five style pieces each.
    /// </summary>
    public static class RanchBauKatalog
    {
        public const string Pfad = "minigames/ranch/bau_balance", BelohnungenPfad = "minigames/ranch/bau_belohnungen";
        public static readonly string[] Kategorien = { "anlage", "deko", "boden", "zaun" };

        private static JObject _balance;
        private static readonly ConditionalWeakTable<JObject, Dictionary<string, RanchBauDef>> DefsCache = new ConditionalWeakTable<JObject, Dictionary<string, RanchBauDef>>();

        /// <summary>The built-in balance with the reward decoration (cached).</summary>
        public static JObject Balance => _balance ?? (_balance = MitPack(ContentSource.LoadObject(Pfad), ContentSource.LoadObject(BelohnungenPfad)));

        public static void ResetCache() => _balance = null;

        /// <summary>A copy of <paramref name="basis"/> with a content pack's values merged in deeply.</summary>
        public static JObject MitPack(JObject basis, JObject overrides)
        {
            var o = (JObject)(basis ?? new JObject()).DeepClone();
            if (overrides != null) DeepMerge(o, overrides);
            return o;
        }

        private static void DeepMerge(JObject basis, JObject overrides)
        {
            foreach (var p in overrides.Properties())
            {
                if (basis[p.Name] is JObject b && p.Value is JObject ov) DeepMerge(b, ov);
                else basis[p.Name] = p.Value.DeepClone();
            }
        }

        /// <summary>Every item (id → def) of a balance, facilities first in file order.</summary>
        public static IReadOnlyDictionary<string, RanchBauDef> Defs(JObject balance)
        {
            balance = balance ?? Balance;
            if (DefsCache.TryGetValue(balance, out var cached)) return cached;
            var o = new Dictionary<string, RanchBauDef>();
            foreach (var p in J.Dict(balance["anlagen"]).Properties())
            {
                var roh = J.Dict(p.Value);
                var stufen = roh["stufen"] is JArray s ? s.Select(t => Math.Max(0, (int)J.NumOr(t, 0.0))).ToArray() : new[] { 0 };
                var fp = roh["footprint"] is JArray f && f.Count >= 2 ? new Zelle(J.Int(f[0]), J.Int(f[1])) : new Zelle(1, 1);
                o[p.Name] = new RanchBauDef
                {
                    Id = p.Name, Kategorie = "anlage", Layer = RanchGridData.Layer.Objekt, Footprint = fp, Kante = false,
                    Kosten = stufen.Length > 0 ? stufen[0] : 0, Stufen = stufen, Upgrade = J.IsTrue(roh["upgrade"]), NameKey = "rbau.item." + p.Name,
                };
            }
            foreach (var (json, kategorie, layer, kante) in new[]
            {
                ("deko", "deko", RanchGridData.Layer.Objekt, false), ("boeden", "boden", RanchGridData.Layer.Boden, false),
                ("zaeune", "zaun", RanchGridData.Layer.Objekt, true),
            })
            {
                foreach (var p in J.Dict(balance[json]).Properties())
                {
                    var roh = J.Dict(p.Value);
                    o[p.Name] = new RanchBauDef
                    {
                        Id = p.Name, Kategorie = kategorie, Layer = layer, Footprint = new Zelle(1, 1), Kante = kante,
                        Kosten = Math.Max(0, (int)J.NumOr(roh["kosten"], 0.0)), NameKey = "rbau.item." + p.Name,
                        Belohnung = J.IsTrue(roh["belohnung"]), Stil = Math.Max(1, (int)J.NumOr(roh["stil"], 1.0)),
                    };
                }
            }
            DefsCache.Add(balance, o);
            return o;
        }

        public static RanchBauDef Def(JObject balance, string id) => Defs(balance).TryGetValue(id ?? "", out var d) ? d : null;

        /// <summary>The ids of a category, sorted (a stable UI order).</summary>
        public static List<string> Ids(JObject balance, string kategorie) =>
            Defs(balance).Values.Where(d => d.Kategorie == kategorie).Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

        /// <summary>The quest-reward pieces of a balance (file order).</summary>
        public static List<string> Belohnungen(JObject balance) => Defs(balance).Values.Where(d => d.Belohnung).Select(d => d.Id).ToList();

        /// <summary>A facility's level prices ([] = no facility).</summary>
        public static int[] StufenKosten(JObject balance, string anlageId) =>
            J.Dict(J.Dict((balance ?? Balance)["anlagen"])[anlageId ?? ""])["stufen"] is JArray s ? s.Select(t => Math.Max(0, (int)J.NumOr(t, 0.0))).ToArray() : new int[0];

        public static int MaxStufe(JObject balance, string anlageId) => StufenKosten(balance, anlageId).Length;

        /// <summary>The price of the NEXT level (current level 1-based; -1 = fully built).</summary>
        public static int NaechsteStufeKosten(JObject balance, string anlageId, int stufe)
        {
            var stufen = StufenKosten(balance, anlageId);
            return stufe < 0 || stufe >= stufen.Length ? -1 : stufen[stufe];
        }

        public static Zelle GridGroesse(JObject balance)
        {
            var grid = J.Dict((balance ?? Balance)["grid"]);
            return new Zelle((int)J.NumOr(grid["breite"], 16.0), (int)J.NumOr(grid["tiefe"], 16.0));
        }

        public sealed class Zone
        {
            public string Id;
            public ZellRect Rect;
            public int Kosten;
        }

        /// <summary>The zones in file order (start, north meadow, east field).</summary>
        public static List<Zone> Zonen(JObject balance)
        {
            var o = new List<Zone>();
            foreach (var p in J.Dict(J.Dict((balance ?? Balance)["grid"])["zonen"]).Properties())
            {
                var z = J.Dict(p.Value);
                var r = z["rect"] as JArray;
                var rect = r != null && r.Count >= 4 ? new ZellRect(J.Int(r[0]), J.Int(r[1]), J.Int(r[2]), J.Int(r[3])) : new ZellRect(0, 0, 0, 0);
                o.Add(new Zone { Id = p.Name, Rect = rect, Kosten = Math.Max(0, (int)J.NumOr(z["kosten"], 0.0)) });
            }
            return o;
        }

        public static Zone ZoneVon(JObject balance, string id) => Zonen(balance).FirstOrDefault(z => z.Id == id);

        /// <summary>The share (0..1) of the paid price a demolition gives back.</summary>
        public static double AbrissErstattung(JObject balance) => J.Clamp(J.NumOr((balance ?? Balance)["abriss_erstattung"], 0.5), 0.0, 1.0);

        public static JObject Effekte(JObject balance) => J.Dict((balance ?? Balance)["effekte"]);

        /// <summary>The economy model (income estimate and grind limits) the balance test keeps honest.</summary>
        public static JObject WirtschaftModell(JObject balance) => J.Dict((balance ?? Balance)["wirtschaft_modell"]);

        public static int EinnahmenProStunde(JObject balance) =>
            J.Dict(WirtschaftModell(balance)["einnahmen_pro_stunde"]).Properties().Sum(p => Math.Max(0, (int)J.NumOr(p.Value, 0.0)));

        public static double GrindGrenze(JObject balance, string key) => J.NumOr(J.Dict(WirtschaftModell(balance)["grind_grenzen_stunden"])[key ?? ""], 0.0);
    }
}
