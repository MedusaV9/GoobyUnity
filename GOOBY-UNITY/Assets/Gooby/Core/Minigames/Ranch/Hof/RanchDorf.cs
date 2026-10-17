using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The riding village Hufingen (port of dorf_state.gd and dorf_katalog.gd with the wares of dorf_waren.json): the
    /// additive key <c>ranch.dorf</c> (discovered, oats and treats, horseshoes owned and per horse, today's sold trader
    /// horses, statistics), healed on read like the Godot self-heal, and the five shops' catalogue.
    /// </summary>
    public static class RanchDorf
    {
        public const string Key = "dorf";
        public const string Reitladen = "reitladen", Futterhof = "futterhof", Moebelscheune = "moebelscheune",
            Pferdehaendlerin = "pferdehaendlerin", Schmiede = "schmiede";
        public static readonly string[] Laeden = { Reitladen, Futterhof, Moebelscheune, Pferdehaendlerin, Schmiede };

        public sealed class Ware
        {
            public string Id;
            public int Preis, Menge = 1;
        }

        public sealed class PoolPferd
        {
            public string Id, NameKey, FellId, Farbe, Maehne;
            public int Preis;
        }

        public static readonly Ware[] FutterWaren =
        {
            new Ware { Id = "heu", Preis = 8 }, new Ware { Id = "heu_bund", Preis = 35, Menge = 5 },
            new Ware { Id = "hafer", Preis = 12 }, new Ware { Id = "leckerli", Preis = 6 },
        };

        /// <summary>The feed yard buys the harvest below the shop price (buying and selling never makes money).</summary>
        public static readonly IReadOnlyDictionary<string, int> Ankauf = new Dictionary<string, int> { ["heu"] = 5, ["apfel"] = 4 };

        /// <summary>The furniture barn's ranch decoration (prices from the build catalogue — one truth).</summary>
        public static readonly string[] RanchDeko = { "beet_blumen", "bank_holz", "laterne", "fahne", "blumenkuebel", "vogelhaus", "wegweiser", "strohpuppe" };

        public static readonly Ware[] Moebel =
        {
            new Ware { Id = "loungeChair", Preis = 120 }, new Ware { Id = "table", Preis = 150 }, new Ware { Id = "rugRound", Preis = 100 },
            new Ware { Id = "pottedPlant", Preis = 60 }, new Ware { Id = "lampRoundFloor", Preis = 70 }, new Ware { Id = "bookcaseOpen", Preis = 110 },
        };

        public static readonly Ware[] SchmiedeWaren =
        {
            new Ware { Id = "hufeisen_bronze", Preis = 60 }, new Ware { Id = "hufeisen_silber", Preis = 300 }, new Ware { Id = "hufeisen_gold", Preis = 900 },
        };

        public const int AngebotAnzahl = 3, BasisWert = 300;
        public const double VerkaufAnteil = 0.5;

        public static readonly PoolPferd[] Pool =
        {
            new PoolPferd { Id = "hf_keks", NameKey = "rdorf.pferde.keks", FellId = "braun", Farbe = "#B98A5E", Maehne = "#6E4E30", Preis = 300 },
            new PoolPferd { Id = "hf_muschel", NameKey = "rdorf.pferde.muschel", FellId = "weiss", Farbe = "#F2E9DC", Maehne = "#D8C7AC", Preis = 420 },
            new PoolPferd { Id = "hf_pfeffer", NameKey = "rdorf.pferde.pfeffer", FellId = "schwarz", Farbe = "#4A4149", Maehne = "#2E282E", Preis = 540 },
            new PoolPferd { Id = "hf_toffee", NameKey = "rdorf.pferde.toffee", FellId = "palomino", Farbe = "#E3B778", Maehne = "#F4E8CE", Preis = 660 },
            new PoolPferd { Id = "hf_flocke", NameKey = "rdorf.pferde.flocke", FellId = "schecke", Farbe = "#E8D9C8", Maehne = "#8A6A4E", Preis = 800 },
            new PoolPferd { Id = "hf_kastanie", NameKey = "rdorf.pferde.kastanie", FellId = "fuchs", Farbe = "#C26E45", Maehne = "#7E3F24", Preis = 950 },
            new PoolPferd { Id = "hf_lavendel", NameKey = "rdorf.pferde.lavendel", FellId = "schecke", Farbe = "#C9B0D8", Maehne = "#8A6FA0", Preis = 1200 },
            new PoolPferd { Id = "hf_sternchen", NameKey = "rdorf.pferde.sternchen", FellId = "schwarz", Farbe = "#3E3A52", Maehne = "#B8B2E0", Preis = 1500 },
        };

        public static JObject Default() => new JObject
        {
            ["v"] = 1,
            ["entdeckt"] = false,
            ["entdecktAm"] = 0,
            ["futter"] = new JObject { ["hafer"] = 0, ["leckerli"] = 0 },
            ["hufeisen"] = new JObject { ["owned"] = new JArray(), ["proPferd"] = new JObject() },
            ["verkauft"] = new JObject { ["tag"] = "", ["angebote"] = new JArray() },
            ["pferdeGekauft"] = 0,
            ["pferdeVerkauft"] = 0,
        };

        /// <summary>Self-heal: types repaired, valid data kept verbatim (normalize_dorf).</summary>
        public static JObject Normalize(JToken raw)
        {
            if (!(raw is JObject src)) return Default();
            var dorf = (JObject)src.DeepClone();
            dorf["v"] = Math.Max(1, J.Int(dorf["v"], 1));
            dorf["entdeckt"] = J.IsTrue(dorf["entdeckt"]);
            dorf["entdecktAm"] = Math.Max(0L, J.Long(dorf["entdecktAm"]));
            var futter = J.Dict(dorf["futter"]);
            dorf["futter"] = new JObject { ["hafer"] = Math.Max(0, J.Int(futter["hafer"])), ["leckerli"] = Math.Max(0, J.Int(futter["leckerli"])) };
            var hufeisen = J.Dict(dorf["hufeisen"]);
            var owned = new JArray();
            if (hufeisen["owned"] is JArray ownedRaw)
                foreach (var id in ownedRaw)
                    if (id.Type == JTokenType.String && !owned.Any(o => J.Str(o) == J.Str(id))) owned.Add(J.Str(id));
            dorf["hufeisen"] = new JObject { ["owned"] = owned, ["proPferd"] = hufeisen["proPferd"] is JObject pro ? pro.DeepClone() : new JObject() };
            var verkauft = J.Dict(dorf["verkauft"]);
            dorf["verkauft"] = new JObject
            {
                ["tag"] = verkauft["tag"]?.Type == JTokenType.String ? J.Str(verkauft["tag"]) : "",
                ["angebote"] = verkauft["angebote"] is JArray a ? a.DeepClone() : new JArray(),
            };
            dorf["pferdeGekauft"] = Math.Max(0, J.Int(dorf["pferdeGekauft"]));
            dorf["pferdeVerkauft"] = Math.Max(0, J.Int(dorf["pferdeVerkauft"]));
            return dorf;
        }

        public static JObject Lese(JObject state) => Normalize(J.Path(state, RanchHof.SliceId, Key));

        /// <summary><c>ranch.dorf</c> inside a write, healed and stored back.</summary>
        public static JObject Im(JObject state)
        {
            var ranch = J.EnsureDict(state, RanchHof.SliceId);
            var dorf = Normalize(ranch[Key]);
            ranch[Key] = dorf;
            return dorf;
        }

        public static bool IstEntdeckt(JObject state) => J.IsTrue(J.Path(state, RanchHof.SliceId, Key, "entdeckt"));

        /// <summary>The first ride into the village (idempotent); true = newly discovered, fast travel is free from now on.</summary>
        public static bool Entdecke(JObject state, long nowMs)
        {
            if (state == null || IstEntdeckt(state)) return false;
            var dorf = Im(state);
            dorf["entdeckt"] = true;
            dorf["entdecktAm"] = nowMs;
            return true;
        }

        public static int Futter(JObject state, string id) => J.Int(J.Path(state, RanchHof.SliceId, Key, "futter", id ?? ""));

        public static bool HufeisenBesitzt(JObject state, string id) =>
            J.Path(state, RanchHof.SliceId, Key, "hufeisen", "owned") is JArray owned && owned.Any(o => J.Str(o) == id);

        public static string HufeisenAn(JObject state, string pferdId) =>
            J.Str(J.Path(state, RanchHof.SliceId, Key, "hufeisen", "proPferd", pferdId ?? ""), null);
    }

    /// <summary>
    /// The shops of Hufingen (port of dorf_wirtschaft.gd): check first, then coins and the change in one step — or
    /// nothing. The feed yard puts hay into the farm store and oats/treats into <c>ranch.dorf.futter</c> and buys hay
    /// and apples back below the price; the furniture barn's pieces come home with the delivery van; the smithy sells
    /// horseshoes once per kind, which any horse can wear. Gear (the saddlery) goes through <see cref="RanchWirtschaft"/>.
    /// </summary>
    public static class DorfWirtschaft
    {
        public const string Ok = "ok", Unbekannt = "unbekannt", ZuTeuer = "zuTeuer", LagerVoll = "lagerVoll", LagerLeer = "lagerLeer",
            SchonGekauft = "schonGekauft", KeinPferd = "keinPferd", NichtGekauft = "nichtGekauft";
        private const string Reason = "ranch_dorf";

        private static int Coins(JObject state) => J.Int(J.Path(state, "economy", "coins"));

        private static bool Spend(JObject state, int preis) => preis <= 0 || Economy.Spend(J.EnsureDict(state, "economy"), preis, Reason);

        private static RanchDorf.Ware Ware(IEnumerable<RanchDorf.Ware> liste, string id) => liste.FirstOrDefault(w => w.Id == id);

        public static (string code, int preis, int menge) FutterKaufen(JObject state, string wareId)
        {
            var ware = Ware(RanchDorf.FutterWaren, wareId);
            if (state == null || ware == null) return (Unbekannt, 0, 0);
            var preis = RanchNpc.MitRabatt(ware.Preis, RanchNpc.Rabatt(state, RanchDorf.Futterhof));
            if (wareId.StartsWith("heu", StringComparison.Ordinal) && !RanchWirtschaft.HeuPasst(state, ware.Menge)) return (LagerVoll, preis, 0);
            if (Coins(state) < preis) return (ZuTeuer, preis, 0);
            RanchHof.Slice(state);
            if (!Spend(state, preis)) return (ZuTeuer, preis, 0);
            if (wareId.StartsWith("heu", StringComparison.Ordinal))
            {
                var lager = RanchHof.Lager(state);
                lager["heu"] = J.Int(lager["heu"]) + ware.Menge;
            }
            else
            {
                var futter = (JObject)RanchDorf.Im(state)["futter"];
                futter[wareId] = J.Int(futter[wareId]) + ware.Menge;
            }
            return (Ok, preis, ware.Menge);
        }

        /// <summary>Sells hay or apples from the farm store to the feed yard.</summary>
        public static (string code, int erloes) ErnteVerkaufen(JObject state, string art, int anzahl)
        {
            if (state == null || anzahl <= 0 || !RanchDorf.Ankauf.TryGetValue(art ?? "", out var stueck)) return (Unbekannt, 0);
            RanchHof.Slice(state);
            var lager = RanchHof.Lager(state);
            if (J.Int(lager[art]) < anzahl) return (LagerLeer, 0);
            var erloes = stueck * anzahl;
            lager[art] = J.Int(lager[art]) - anzahl;
            if (erloes > 0) Economy.Award(J.EnsureDict(state, "economy"), erloes, Reason);
            return (Ok, erloes);
        }

        /// <summary>The furniture barn's ranch decoration: bought into the build store, then placed for free on the grid.</summary>
        public static (string code, int preis) DekoKaufen(JObject state, string dekoId)
        {
            var def = RanchBauKatalog.Def(null, dekoId);
            if (state == null || def == null || def.Kategorie != "deko" || Array.IndexOf(RanchDorf.RanchDeko, dekoId) < 0) return (Unbekannt, 0);
            if (Coins(state) < def.Kosten || !Spend(state, def.Kosten)) return (ZuTeuer, def.Kosten);
            RanchBauState.LagerHinzu(RanchBauState.Im(state), dekoId);
            return (Ok, def.Kosten);
        }

        public static (string code, int preis) MoebelKaufen(JObject state, string itemId)
        {
            var ware = Ware(RanchDorf.Moebel, itemId);
            if (state == null || ware == null || FurnitureCatalog.Def(itemId) == null) return (Unbekannt, 0);
            if (StorageLogic.ItemWeight(itemId, HomeState.DefsAsJson()) > MoebelShop.StorageFree(state)) return (LagerVoll, ware.Preis);
            if (Coins(state) < ware.Preis || !Spend(state, ware.Preis)) return (ZuTeuer, ware.Preis);
            Lieferung.Bestellen(state, itemId);
            return (Ok, ware.Preis);
        }

        public static (string code, int preis) SchmiedeKaufen(JObject state, string wareId)
        {
            var ware = Ware(RanchDorf.SchmiedeWaren, wareId);
            if (state == null || ware == null) return (Unbekannt, 0);
            if (RanchDorf.HufeisenBesitzt(state, wareId)) return (SchonGekauft, 0);
            var preis = RanchNpc.MitRabatt(ware.Preis, RanchNpc.Rabatt(state, RanchDorf.Schmiede));
            if (Coins(state) < preis || !Spend(state, preis)) return (ZuTeuer, preis);
            ((JArray)RanchDorf.Im(state)["hufeisen"]["owned"]).Add(wareId);
            return (Ok, preis);
        }

        /// <summary>Puts bought horseshoes on a horse (<paramref name="wareId"/> "" or null takes them off). Free.</summary>
        public static string HufeisenAnlegen(JObject state, string pferdId, string wareId)
        {
            if (state == null) return Unbekannt;
            if (RanchHof.Pferd(state, pferdId) == null) return KeinPferd;
            if (!string.IsNullOrEmpty(wareId) && !RanchDorf.HufeisenBesitzt(state, wareId)) return NichtGekauft;
            var pro = (JObject)RanchDorf.Im(state)["hufeisen"]["proPferd"];
            if (string.IsNullOrEmpty(wareId)) pro.Remove(pferdId);
            else pro[pferdId] = wareId;
            return Ok;
        }
    }

    /// <summary>
    /// The horse trader Wilma (port of dorf_haendler.gd): three horses a day out of the pool, drawn exactly like Godot
    /// (Godot's RandomNumberGenerator seeded with <c>hash("hufingen|" + day)</c>, Fisher–Yates) — the same day gives the
    /// same offer, every horse comes back on other days (no FOMO). Owned horses and today's bought ones drop out. Buying
    /// needs a free box; the price travels with the horse and sets the resale value (half, start horses count 300). The
    /// last horse is never for sale.
    /// </summary>
    public static class DorfHaendler
    {
        public const string Ok = "ok", Unbekannt = "unbekannt", NichtImAngebot = "nichtImAngebot", StallVoll = "stallVoll",
            ZuTeuer = "zuTeuer", LetztesPferd = "letztesPferd", KeinPferd = "keinPferd";
        private const string Reason = "ranch_dorf";

        /// <summary>The pure daily rotation (tages_angebot).</summary>
        public static List<RanchDorf.PoolPferd> TagesAngebot(string tag, IReadOnlyList<RanchDorf.PoolPferd> pool, int anzahl)
        {
            var out_ = new List<RanchDorf.PoolPferd>();
            if (pool == null || pool.Count == 0 || anzahl <= 0) return out_;
            var indizes = Enumerable.Range(0, pool.Count).ToArray();
            var rng = new GodotRng(GodotRng.StringHash("hufingen|" + (tag ?? "")));
            for (var i = indizes.Length - 1; i > 0; i--)
            {
                var j = rng.RandIRange(0, i);
                (indizes[i], indizes[j]) = (indizes[j], indizes[i]);
            }
            for (var i = 0; i < Math.Min(anzahl, indizes.Length); i++) out_.Add(pool[indizes[i]]);
            return out_;
        }

        private static JObject Pferde(JObject state) => J.Path(state, RanchHof.SliceId, "tiere", "pferde") as JObject ?? new JObject();

        /// <summary>Today's offer for this save: the rotation minus owned horses and horses bought today.</summary>
        public static List<RanchDorf.PoolPferd> Angebot(JObject state, string tag)
        {
            var pferde = Pferde(state);
            var dorf = RanchDorf.Lese(state);
            var gesperrt = J.Str(dorf["verkauft"]["tag"]) == tag ? ((JArray)dorf["verkauft"]["angebote"]).Select(t => J.Str(t)).ToList() : new List<string>();
            return TagesAngebot(tag, RanchDorf.Pool, RanchDorf.AngebotAnzahl).Where(p => !pferde.ContainsKey(p.Id) && !gesperrt.Contains(p.Id)).ToList();
        }

        public static int Kapazitaet(JObject state) => RanchWirtschaft.BoxenKapazitaet(state);

        public static bool StallVollFuer(JObject state) => Pferde(state).Count >= Kapazitaet(state);

        public static (string code, int preis) PferdKaufen(JObject state, string poolId, string tag)
        {
            if (state == null) return (Unbekannt, 0);
            var eintrag = Angebot(state, tag).FirstOrDefault(p => p.Id == poolId);
            if (eintrag == null) return (NichtImAngebot, 0);
            if (StallVollFuer(state)) return (StallVoll, 0);
            var preis = Math.Max(0, RanchNpc.MitRabatt(eintrag.Preis, RanchNpc.Rabatt(state, RanchDorf.Pferdehaendlerin)));
            if (J.Int(J.Path(state, "economy", "coins")) < preis) return (ZuTeuer, preis);
            var ranch = RanchHof.Slice(state);
            if (preis > 0 && !Economy.Spend(J.EnsureDict(state, "economy"), preis, Reason)) return (ZuTeuer, preis);
            ((JObject)ranch["tiere"]["pferde"])[poolId] = PferdAusPool(eintrag, preis);
            var dorf = RanchDorf.Im(state);
            var verkauft = (JObject)dorf["verkauft"];
            if (J.Str(verkauft["tag"]) != tag)
            {
                verkauft["tag"] = tag ?? "";
                verkauft["angebote"] = new JArray();
            }
            ((JArray)verkauft["angebote"]).Add(poolId);
            dorf["pferdeGekauft"] = J.Int(dorf["pferdeGekauft"]) + 1;
            return (Ok, preis);
        }

        public static int Verkaufspreis(JObject state, string pferdId)
        {
            var pferd = Pferde(state)[pferdId ?? ""] as JObject;
            if (pferd == null) return 0;
            var wert = pferd["kaufpreis"] != null && pferd["kaufpreis"].Type != JTokenType.Null ? J.Int(pferd["kaufpreis"]) : RanchDorf.BasisWert;
            return (int)Math.Floor(wert * RanchDorf.VerkaufAnteil);
        }

        public static (string code, int erloes) PferdVerkaufen(JObject state, string pferdId)
        {
            if (state == null) return (Unbekannt, 0);
            var pferde = Pferde(state);
            if (!pferde.ContainsKey(pferdId ?? "")) return (KeinPferd, 0);
            if (pferde.Count <= 1) return (LetztesPferd, 0);
            var erloes = Verkaufspreis(state, pferdId);
            var ranch = RanchHof.Slice(state);
            ((JObject)ranch["tiere"]["pferde"]).Remove(pferdId);
            if (J.Path(ranch, "wirtschaft", "gear", "equippedByHorse") is JObject equipped) equipped.Remove(pferdId);
            var dorf = RanchDorf.Im(state);
            ((JObject)dorf["hufeisen"]["proPferd"]).Remove(pferdId);
            dorf["pferdeVerkauft"] = J.Int(dorf["pferdeVerkauft"]) + 1;
            if (erloes > 0) Economy.Award(J.EnsureDict(state, "economy"), erloes, Reason);
            return (Ok, erloes);
        }

        /// <summary>A pool entry → a horse of the farm (neues_pferd + the trader's metadata).</summary>
        public static JObject PferdAusPool(RanchDorf.PoolPferd eintrag, int preis)
        {
            var name = eintrag.NameKey.Substring(eintrag.NameKey.LastIndexOf('.') + 1);
            var pferd = RanchHof.NeuesPferd(char.ToUpperInvariant(name[0]) + name.Substring(1), eintrag.FellId);
            pferd["packId"] = eintrag.Id;
            pferd["nameKey"] = eintrag.NameKey;
            pferd["farbeHex"] = eintrag.Farbe;
            pferd["maehneHex"] = eintrag.Maehne;
            pferd["kaufpreis"] = preis;
            return pferd;
        }
    }
}
