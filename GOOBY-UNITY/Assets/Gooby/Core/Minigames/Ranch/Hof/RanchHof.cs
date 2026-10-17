using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The ranch farm's save slice (port of ranch_state.gd and ranch_play_slices.gd): bought flag, the farm animals,
    /// <c>tiere</c> (horses id → horse, the stable, the last tick) and <c>wirtschaft</c> (feed store, extensions, gear,
    /// the hay field and three apple trees). Healed in place; every other key of <c>ranch</c> (comp, mp, spiele, welt …)
    /// stays verbatim. The start animals come from the Godot pack content/ranch/data/ranch.json.
    /// </summary>
    public static class RanchHof
    {
        public const string SliceId = "ranch";
        public const int Preis = 2500, FreischaltLevel = 15;
        public static readonly string[] Fellfarben = { "braun", "schwarz", "weiss", "fuchs", "palomino", "schecke", "apricot", "rauchgrau" };
        public static readonly string[] GearSlots = { "sattel", "decke", "halfter" };
        public static readonly string[] AlterPhasen = { "fohlen", "jaehrling", "jungpferd", "ausgewachsen" };
        public static readonly string[] StatKeys = RanchPferd.StatKeys;

        public sealed class StartTier
        {
            public string Id, Art, NameKey, Farbe, Maehne, FellId;
        }

        public static readonly StartTier[] StartTiere =
        {
            new StartTier { Id = "tier_pferd_karamell", Art = "pferd", NameKey = "ranch.tiere.karamell", Farbe = "#D9A066", Maehne = "#8A5A33", FellId = "palomino" },
            new StartTier { Id = "tier_pferd_wolke", Art = "pferd", NameKey = "ranch.tiere.wolke", Farbe = "#F2E9DC", Maehne = "#C9B79C", FellId = "weiss" },
            new StartTier { Id = "tier_pferd_beere", Art = "pferd", NameKey = "ranch.tiere.beere", Farbe = "#C98BB9", Maehne = "#8A5A7A", FellId = "schecke" },
            new StartTier { Id = "tier_kuh_frieda", Art = "kuh", NameKey = "ranch.tiere.frieda", Farbe = "#F5EFE4" },
            new StartTier { Id = "tier_kuh_paula", Art = "kuh", NameKey = "ranch.tiere.paula", Farbe = "#E8D9C4" },
            new StartTier { Id = "tier_schaf_wolli", Art = "schaf", NameKey = "ranch.tiere.wolli", Farbe = "#F7F3EA" },
            new StartTier { Id = "tier_schaf_lotte", Art = "schaf", NameKey = "ranch.tiere.lotte", Farbe = "#EFE3D0" },
            new StartTier { Id = "tier_schaf_milo", Art = "schaf", NameKey = "ranch.tiere.milo", Farbe = "#E4DACB" },
            new StartTier { Id = "tier_huhn_kiki", Art = "huhn", NameKey = "ranch.tiere.kiki", Farbe = "#F2C14E" },
        };

        /// <summary>A fresh horse's care values.</summary>
        public static readonly (string key, double value)[] StartWerte = { ("hunger", 80.0), ("durst", 85.0), ("sauberkeit", 75.0) };

        public static JObject Slice(JObject state)
        {
            var ranch = J.EnsureDict(state, SliceId);
            ranch["v"] = Math.Max(1, J.Int(ranch["v"], 1));
            ranch["gekauft"] = J.IsTrue(ranch["gekauft"]);
            ranch["gekauftAm"] = Math.Max(0L, J.Long(ranch["gekauftAm"]));
            ranch["angebotGesehen"] = J.IsTrue(ranch["angebotGesehen"]);
            ranch["angebotVerschoben"] = J.IsTrue(ranch["angebotVerschoben"]);
            if (!(ranch["hoftiere"] is JArray)) ranch["hoftiere"] = new JArray();
            var ausbau = J.Dict(ranch["ausbau"]);
            ranch["ausbau"] = new JObject { ["stall"] = Math.Max(1, J.Int(ausbau["stall"], 1)), ["koppel"] = Math.Max(1, J.Int(ausbau["koppel"], 1)), ["reitplatz"] = Math.Max(1, J.Int(ausbau["reitplatz"], 1)) };
            ranch["tiere"] = NormalizeTiere(ranch["tiere"]);
            ranch["wirtschaft"] = NormalizeWirtschaft(ranch["wirtschaft"]);
            return ranch;
        }

        public static bool IstGekauft(JObject state) => J.IsTrue(J.Path(state, SliceId, "gekauft"));

        public static bool IstFreigeschaltet(JObject state) => J.Int(J.Path(state, "progression", "level"), 1) >= FreischaltLevel;

        /// <summary>The horses in a stable order (ids sorted, like the Godot care screen's first pick).</summary>
        public static List<string> PferdIds(JObject state) =>
            (J.Path(state, SliceId, "tiere", "pferde") as JObject)?.Properties().Select(p => p.Name).OrderBy(id => id, StringComparer.Ordinal).ToList() ?? new List<string>();

        public static JObject Pferd(JObject state, string id) =>
            string.IsNullOrEmpty(id) ? null : J.Path(state, SliceId, "tiere", "pferde", id) as JObject;

        public static JObject Lager(JObject state) => J.Dict(J.Path(state, SliceId, "wirtschaft", "lager"));

        /// <summary>The horse the farm's own games ride (the one last picked at the farm; else the first).</summary>
        public static string ReitPferd(JObject state)
        {
            var id = J.Str(J.Path(state, SliceId, "reitPferd"));
            return Pferd(state, id) != null ? id : PferdIds(state).FirstOrDefault();
        }

        public static void SetzeReitPferd(JObject state, string id)
        {
            if (Pferd(state, id) != null) J.EnsureDict(state, SliceId)["reitPferd"] = id;
        }

        public static double Stall(JObject state) => J.NumOr(J.Path(state, SliceId, "tiere", "stall", "sauberkeit"), RanchCare.StallMax);

        /// <summary>A new horse (RanchPlaySlices.neues_pferd) with the neutral breed fields of a level-1 Puschelhufer.</summary>
        public static JObject NeuesPferd(string name, string farbe)
        {
            var werte = new JObject();
            foreach (var (k, v) in StartWerte) werte[k] = v;
            var stats = new JObject();
            var frische = new JObject();
            var trainiert = new JObject();
            foreach (var k in StatKeys)
            {
                stats[k] = 10;
                frische[k] = 100.0;
                trainiert[k] = 0;
            }
            return new JObject
            {
                ["name"] = name ?? "",
                ["farbe"] = Array.IndexOf(Fellfarben, farbe) >= 0 ? farbe : "braun",
                ["werte"] = werte,
                ["bindung"] = 0.0,
                ["bondHeute"] = 0.0,
                ["bondTag"] = "",
                ["letztePflegeAt"] = 0,
                ["ausruestung"] = new JObject { ["sattel"] = null, ["decke"] = null, ["halfter"] = null },
                ["rasse"] = RanchPferd.FallbackRasse,
                ["gene"] = new JObject(),
                ["abzeichen"] = new JObject(),
                ["charakter"] = new JArray(),
                ["groesse"] = 1.0,
                ["stimmPitch"] = 1.0,
                ["phasenOffset"] = 0.0,
                ["stats"] = stats,
                ["trainiert"] = trainiert,
                ["statXp"] = new JObject(),
                ["frische"] = frische,
                ["frischeTag"] = "",
                ["mashTag"] = new JObject(),
                ["xp"] = 0.0,
                ["level"] = 1,
                ["rittXpHeute"] = 0.0,
                ["alter"] = "ausgewachsen",
                ["eltern"] = new JArray(),
                ["geborenAm"] = 0,
            };
        }

        public static JObject NormalizeTiere(JToken raw)
        {
            var src = raw as JObject ?? new JObject();
            var pferde = new JObject();
            if (src["pferde"] is JObject roh)
                foreach (var p in roh.Properties())
                    if (p.Value is JObject pferd) pferde[p.Name] = NormalizePferd(pferd);
            var stall = J.Dict(src["stall"]);
            src["v"] = Math.Max(1, J.Int(src["v"], 1));
            src["pferde"] = pferde;
            src["stall"] = new JObject { ["sauberkeit"] = J.Clamp(J.NumOr(stall["sauberkeit"], 100.0), 0.0, 100.0) };
            src["lastTickAt"] = Math.Max(0L, J.Long(src["lastTickAt"]));
            return src;
        }

        /// <summary>One horse healed: types repaired, values clamped, every unknown key (pack data, colours) kept.</summary>
        public static JObject NormalizePferd(JObject raw)
        {
            var p = NeuesPferd("", "braun");
            p["name"] = J.Str(raw["name"]);
            if (raw["farbe"] is JValue f && f.Type == JTokenType.String && Array.IndexOf(Fellfarben, (string)f) >= 0) p["farbe"] = (string)f;
            p["werte"] = RanchCare.ClampWerte(J.Dict(raw["werte"]));
            p["bindung"] = J.Clamp(J.NumOr(raw["bindung"], 0.0), 0.0, 100.0);
            p["bondHeute"] = J.Clamp(J.NumOr(raw["bondHeute"], 0.0), 0.0, RanchCare.BondTagesDeckel);
            p["bondTag"] = J.Str(raw["bondTag"]);
            p["letztePflegeAt"] = Math.Max(0L, J.Long(raw["letztePflegeAt"]));
            var ausr = J.Dict(raw["ausruestung"]);
            foreach (var slot in GearSlots) p["ausruestung"][slot] = J.IsString(ausr[slot]) ? ausr[slot].DeepClone() : JValue.CreateNull();
            if (J.Str(raw["rasse"]) != "") p["rasse"] = raw["rasse"].DeepClone();
            foreach (var k in new[] { "gene", "abzeichen", "statXp", "mashTag" })
                if (raw[k] is JObject o) p[k] = o.DeepClone();
            if (raw["charakter"] is JArray zuege) p["charakter"] = new JArray(zuege.Where(J.IsString).Select(z => z.DeepClone()));
            p["groesse"] = J.Clamp(J.NumOr(raw["groesse"], 1.0), 0.4, 1.6);
            p["stimmPitch"] = J.Clamp(J.NumOr(raw["stimmPitch"], 1.0), 0.6, 1.4);
            p["phasenOffset"] = J.Clamp(J.NumOr(raw["phasenOffset"], 0.0), 0.0, 1.0);
            var rs = J.Dict(raw["stats"]);
            var rt = J.Dict(raw["trainiert"]);
            var rf = J.Dict(raw["frische"]);
            foreach (var k in StatKeys)
            {
                p["stats"][k] = J.ClampI((int)J.NumOr(rs[k], 10.0), 1, 20);
                p["trainiert"][k] = J.ClampI((int)J.NumOr(rt[k], 0.0), 0, 10);
                p["frische"][k] = J.Clamp(J.NumOr(rf[k], 100.0), 0.0, 100.0);
            }
            if (J.IsString(raw["frischeTag"])) p["frischeTag"] = raw["frischeTag"].DeepClone();
            p["xp"] = J.Clamp(J.NumOr(raw["xp"], 0.0), 0.0, RanchPferd.XpSummeBis(RanchPferd.LevelMax));
            p["level"] = RanchPferd.LevelFuerXp((double)p["xp"]);
            p["rittXpHeute"] = J.Clamp(J.NumOr(raw["rittXpHeute"], 0.0), 0.0, RanchPferd.XpRittTagesDeckel);
            var alter = J.Str(raw["alter"], "ausgewachsen");
            p["alter"] = Array.IndexOf(AlterPhasen, alter) >= 0 ? alter : "ausgewachsen";
            if (raw["eltern"] is JArray eltern) p["eltern"] = eltern.DeepClone();
            p["geborenAm"] = Math.Max(0L, J.Long(raw["geborenAm"]));
            foreach (var prop in raw.Properties())
                if (!p.ContainsKey(prop.Name)) p[prop.Name] = prop.Value.DeepClone();
            return p;
        }

        public static JObject NormalizeWirtschaft(JToken raw)
        {
            var w = raw as JObject ?? new JObject { ["lager"] = new JObject { ["heu"] = 4, ["apfel"] = 2 } };
            w["v"] = Math.Max(1, J.Int(w["v"], 1));
            var lager = J.Dict(w["lager"]);
            w["lager"] = new JObject { ["heu"] = Math.Max(0, J.Int(lager["heu"])), ["apfel"] = Math.Max(0, J.Int(lager["apfel"])) };
            var ausbau = J.Dict(w["ausbau"]);
            w["ausbau"] = new JObject
            {
                ["boxen"] = J.ClampI(J.Int(ausbau["boxen"], 1), 1, 3),
                ["reitplatz"] = J.IsTrue(ausbau["reitplatz"]),
                ["weidezaun"] = J.IsTrue(ausbau["weidezaun"]),
            };
            var gear = J.Dict(w["gear"]);
            var owned = new JArray();
            foreach (var id in gear["owned"] as JArray ?? new JArray())
                if (J.IsString(id) && !owned.Any(o => (string)o == (string)id)) owned.Add((string)id);
            w["gear"] = new JObject { ["owned"] = owned, ["equippedByHorse"] = J.Dict(gear["equippedByHorse"]).DeepClone() };
            if (gear["sets"] is JArray sets)
            {
                var heil = new JArray();
                foreach (var set in sets.Take(RanchWirtschaft.OutfitSetMax))
                {
                    var o = new JObject();
                    if (set is JObject roh)
                        foreach (var slot in GearSlots)
                            o[slot] = J.IsString(roh[slot]) ? roh[slot].DeepClone() : JValue.CreateNull();
                    heil.Add(set is JObject ? (JToken)o : JValue.CreateNull());
                }
                w["gear"]["sets"] = heil;
            }
            var felder = J.Dict(w["felder"]);
            var baeumeRoh = felder["baeume"] as JArray ?? new JArray();
            var baeume = new JArray();
            for (var i = 0; i < 3; i++) baeume.Add(Math.Max(0L, i < baeumeRoh.Count ? J.Long(baeumeRoh[i]) : 0L));
            w["felder"] = new JObject { ["heuBereitAt"] = Math.Max(0L, J.Long(felder["heuBereitAt"])), ["baeume"] = baeume };
            return w;
        }
    }

    /// <summary>Buying the ranch (port of ranch_kauf.gd): coins, the bought flag and the start animals in one step — or nothing.</summary>
    public static class RanchKauf
    {
        public const string Ok = "ok", Gesperrt = "level_zu_niedrig", Schon = "schon_gekauft", Pleite = "not_enough_coins";

        public static string Check(JObject state)
        {
            if (state == null) return Gesperrt;
            if (RanchHof.IstGekauft(state)) return Schon;
            if (!RanchHof.IstFreigeschaltet(state)) return Gesperrt;
            return J.Int(J.Path(state, "economy", "coins")) < RanchHof.Preis ? Pleite : Ok;
        }

        public static string Kaufe(JObject state, long nowMs)
        {
            var grund = Check(state);
            if (grund != Ok) return grund;
            var ranch = RanchHof.Slice(state);
            if (!Economy.Spend(J.EnsureDict(state, "economy"), RanchHof.Preis, "ranch")) return Pleite;
            ranch["gekauft"] = true;
            ranch["gekauftAm"] = nowMs;
            ranch["angebotGesehen"] = true;
            ranch["angebotVerschoben"] = false;
            var hoftiere = new JArray();
            var pferde = (JObject)ranch["tiere"]["pferde"];
            foreach (var t in RanchHof.StartTiere)
            {
                if (t.Art != "pferd")
                {
                    hoftiere.Add(new JObject { ["id"] = t.Id, ["art"] = t.Art, ["name_key"] = t.NameKey, ["farbe"] = t.Farbe });
                    continue;
                }
                if (pferde.ContainsKey(t.Id)) continue;
                var name = t.NameKey.Substring(t.NameKey.LastIndexOf('.') + 1);
                var pferd = RanchHof.NeuesPferd(char.ToUpperInvariant(name[0]) + name.Substring(1), t.FellId);
                pferd["packId"] = t.Id;
                pferd["nameKey"] = t.NameKey;
                pferd["farbeHex"] = t.Farbe;
                pferd["maehneHex"] = t.Maehne;
                pferde[t.Id] = pferd;
            }
            ranch["hoftiere"] = hoftiere;
            ranch["tiere"]["lastTickAt"] = nowMs;
            return Ok;
        }
    }

    /// <summary>
    /// The ranch economy (port of ranch_wirtschaft.gd with the balance of wirtschaft.json): hay bought by the bale or
    /// mown from the field (3 bales, regrows in 6 h), apples from three trees (2 each, 4 h), the extensions (more
    /// boxes, riding arena, pasture fence) and gear in five colours. Every call changes the state in place and returns
    /// a result code; nothing is spent on a failure.
    /// </summary>
    public static class RanchWirtschaft
    {
        public const string Ok = "ok", Unbekannt = "unbekannt", Schon = "schonGekauft", ZuTeuer = "zuTeuer", Vorstufe = "vorstufeFehlt",
            NichtReif = "nochNichtReif", LagerLeer = "lagerLeer", NichtGekauft = "nichtGekauft", LagerVoll = "lagerVoll", Gesperrt = "gesperrt",
            Leer = "leer";

        public const int HeuKaufPreis = 8, HeuBallen = 3, HeuRegrowMin = 360, ApfelProBaum = 2, ApfelRegrowMin = 240, OutfitSetMax = 3;
        public const double GoldMult = 2.0, WeidezaunSauberkeitMult = 0.8, ReitplatzParcoursCoinMult = 1.1;
        public static readonly string[] AusbauIds = { "boxen2", "boxen3", "reitplatz", "weidezaun" };
        public static readonly string[] GearFarben = { "rot", "blau", "gruen", "lila", "gold" };
        public static readonly int[] BoxenKapazitaeten = { 2, 4, 6 };

        /// <summary>Gear no shop sells — the ranch quests' rewards: the potato-sack saddle and the saddle pad with a bow.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> GearSonder = new Dictionary<string, string[]>
        {
            ["sattel"] = new[] { "kartoffel" }, ["decke"] = new[] { "schleife" },
        };

        private static readonly Dictionary<string, int> GearBasis = new Dictionary<string, int> { ["sattel"] = 120, ["decke"] = 80, ["halfter"] = 60 };
        private static readonly Dictionary<string, int> AusbauPreise = new Dictionary<string, int> { ["boxen2"] = 400, ["boxen3"] = 900, ["reitplatz"] = 300, ["weidezaun"] = 250 };

        private static JObject W(JObject state) => (JObject)RanchHof.Slice(state)["wirtschaft"];

        private static int Coins(JObject state) => J.Int(J.Path(state, "economy", "coins"));

        private static bool Spend(JObject state, int preis, string reason) => preis <= 0 || Economy.Spend(J.EnsureDict(state, "economy"), preis, reason);

        public static int GearPreis(string slot, string farbe) =>
            GearBasis.TryGetValue(slot ?? "", out var basis) ? (int)Math.Round(basis * (farbe == "gold" ? GoldMult : 1.0), MidpointRounding.AwayFromZero) : 0;

        /// <summary>What a piece of gear costs this player: a shop's friendship discount plus the saddle room's.</summary>
        public static int GearPreisFuer(JObject state, string slot, string farbe, int rabattProzent = 0) =>
            RanchNpc.MitRabatt(GearPreis(slot, farbe), rabattProzent + RanchBauEffekte.GearRabatt(state));

        public static int AusbauPreis(string id) => AusbauPreise.TryGetValue(id ?? "", out var p) ? p : 0;

        public static bool AusbauAktiv(JObject state, string id) => J.IsTrue(J.Path(state, RanchHof.SliceId, "wirtschaft", "ausbau", id));

        public static int Boxen(JObject state) => J.ClampI(J.Int(J.Path(state, RanchHof.SliceId, "wirtschaft", "ausbau", "boxen"), 1), 1, 3);

        /// <summary>Horses the farm holds: the old boxes or the build grid's stable boxes, whichever is more.</summary>
        public static int BoxenKapazitaet(JObject state) => RanchBauEffekte.BoxenKapazitaet(state, BoxenKapazitaeten[Boxen(state) - 1]);

        public static double WeideSauberkeitMult(JObject state) =>
            RanchBauEffekte.SauberkeitMult(state, AusbauAktiv(state, "weidezaun") ? WeidezaunSauberkeitMult : 1.0);

        /// <summary>Does the farm store take <paramref name="menge"/> more bales (the hay barn's room)?</summary>
        public static bool HeuPasst(JObject state, int menge) =>
            J.Int(J.Path(state, RanchHof.SliceId, "wirtschaft", "lager", "heu")) + Math.Max(0, menge) <= RanchBauEffekte.HeuKapazitaet(state);

        public static string HeuKaufen(JObject state, int anzahl)
        {
            if (anzahl <= 0) return Unbekannt;
            var preis = HeuKaufPreis * anzahl;
            if (!HeuPasst(state, anzahl)) return LagerVoll;
            if (Coins(state) < preis) return ZuTeuer;
            var w = W(state);
            if (!Spend(state, preis, "ranch_heu")) return ZuTeuer;
            w["lager"]["heu"] = J.Int(w["lager"]["heu"]) + anzahl;
            return Ok;
        }

        /// <summary>Milliseconds until the hay field is ripe (0 = ready to mow).</summary>
        public static long HeuBereitIn(JObject state, long nowMs) => Math.Max(0L, J.Long(J.Path(state, RanchHof.SliceId, "wirtschaft", "felder", "heuBereitAt")) - nowMs);

        public static long ApfelBereitIn(JObject state, int baum, long nowMs) =>
            J.Path(state, RanchHof.SliceId, "wirtschaft", "felder", "baeume") is JArray a && baum >= 0 && baum < a.Count ? Math.Max(0L, J.Long(a[baum]) - nowMs) : 0L;

        public static (string code, int menge) HeuErnten(JObject state, long nowMs)
        {
            var w = W(state);
            var felder = (JObject)w["felder"];
            if (nowMs < J.Long(felder["heuBereitAt"])) return (NichtReif, 0);
            w["lager"]["heu"] = J.Int(w["lager"]["heu"]) + HeuBallen;
            felder["heuBereitAt"] = nowMs + HeuRegrowMin * 60_000L;
            return (Ok, HeuBallen);
        }

        public static (string code, int menge) ApfelPfluecken(JObject state, int baum, long nowMs)
        {
            var w = W(state);
            var baeume = (JArray)w["felder"]["baeume"];
            if (baum < 0 || baum >= baeume.Count) return (Unbekannt, 0);
            if (nowMs < J.Long(baeume[baum])) return (NichtReif, 0);
            w["lager"]["apfel"] = J.Int(w["lager"]["apfel"]) + ApfelProBaum;
            baeume[baum] = nowMs + ApfelRegrowMin * 60_000L;
            return (Ok, ApfelProBaum);
        }

        public static string AusbauKaufen(JObject state, string id)
        {
            if (Array.IndexOf(AusbauIds, id) < 0) return Unbekannt;
            var w = W(state);
            var ausbau = (JObject)w["ausbau"];
            var boxen = J.Int(ausbau["boxen"], 1);
            switch (id)
            {
                case "boxen2": if (boxen >= 2) return Schon; break;
                case "boxen3":
                    if (boxen >= 3) return Schon;
                    if (boxen < 2) return Vorstufe;
                    break;
                default: if (J.IsTrue(ausbau[id])) return Schon; break;
            }
            var preis = AusbauPreis(id);
            if (Coins(state) < preis || !Spend(state, preis, "ranch_ausbau")) return ZuTeuer;
            if (id == "boxen2") ausbau["boxen"] = 2;
            else if (id == "boxen3") ausbau["boxen"] = 3;
            else ausbau[id] = true;
            return Ok;
        }

        public static bool GearBesitzt(JObject state, string id) =>
            J.Path(state, RanchHof.SliceId, "wirtschaft", "gear", "owned") is JArray owned && owned.Any(o => J.Str(o) == id);

        public static bool IstSonder(string slot, string farbe) =>
            farbe != null && GearSonder.TryGetValue(slot ?? "", out var s) && Array.IndexOf(s, farbe) >= 0;

        /// <summary>A slot's chips: the five shop colours, then the special pieces the player owns.</summary>
        public static List<string> GearFarbenFuer(JObject state, string slot)
        {
            var o = new List<string>(GearFarben);
            if (GearSonder.TryGetValue(slot ?? "", out var sonder)) o.AddRange(sonder.Where(f => GearBesitzt(state, slot + "_" + f)));
            return o;
        }

        /// <summary>A piece of gear as a present (a quest reward) — free; false when it is already in the cupboard.</summary>
        public static bool GearSchenken(JObject state, string id)
        {
            var owned = (JArray)W(state)["gear"]["owned"];
            if (owned.Any(o => J.Str(o) == id)) return false;
            owned.Add(id);
            return true;
        }

        // ── the saddle room's outfit sets ──

        /// <summary>A saved set (slot → colour or null) or null when the place is empty.</summary>
        public static JObject OutfitSet(JObject state, int platz) =>
            J.Path(state, RanchHof.SliceId, "wirtschaft", "gear", "sets") is JArray sets && platz >= 0 && platz < sets.Count ? sets[platz] as JObject : null;

        /// <summary>Saves what a horse wears into set place <paramref name="platz"/> (the saddle room's places only).</summary>
        public static string OutfitSpeichern(JObject state, int platz, string pferdId)
        {
            if (platz < 0 || platz >= RanchBauEffekte.OutfitSets(state)) return Gesperrt;
            if (RanchHof.Pferd(state, pferdId) == null) return Unbekannt;
            var gear = (JObject)W(state)["gear"];
            var sets = gear["sets"] as JArray ?? new JArray();
            while (sets.Count <= platz) sets.Add(JValue.CreateNull());
            var set = new JObject();
            foreach (var slot in RanchHof.GearSlots)
            {
                var an = GearAn(state, pferdId, slot);
                set[slot] = an != null ? (JToken)an : JValue.CreateNull();
            }
            sets[platz] = set;
            gear["sets"] = sets;
            return Ok;
        }

        /// <summary>Puts a saved set on a horse in one go: every slot as saved (empty = off), pieces no longer owned stay off.</summary>
        public static string OutfitAnlegen(JObject state, int platz, string pferdId)
        {
            if (platz < 0 || platz >= RanchBauEffekte.OutfitSets(state)) return Gesperrt;
            var set = OutfitSet(state, platz);
            if (set == null) return Leer;
            if (RanchHof.Pferd(state, pferdId) == null) return Unbekannt;
            foreach (var slot in RanchHof.GearSlots)
            {
                var farbe = J.Str(set[slot], null);
                GearAnlegen(state, pferdId, slot, farbe != null && GearBesitzt(state, slot + "_" + farbe) ? farbe : null);
            }
            return Ok;
        }

        public static string GearKaufen(JObject state, string id, int rabattProzent = 0)
        {
            var teile = (id ?? "").Split('_');
            if (teile.Length != 2 || Array.IndexOf(RanchHof.GearSlots, teile[0]) < 0 || Array.IndexOf(GearFarben, teile[1]) < 0) return Unbekannt;
            var w = W(state);
            var owned = (JArray)w["gear"]["owned"];
            if (owned.Any(o => J.Str(o) == id)) return Schon;
            var preis = GearPreisFuer(state, teile[0], teile[1], rabattProzent);
            if (Coins(state) < preis || !Spend(state, preis, "ranch_gear")) return ZuTeuer;
            owned.Add(id);
            return Ok;
        }

        /// <summary>Puts bought gear on a horse (<paramref name="farbe"/> null takes it off).</summary>
        public static string GearAnlegen(JObject state, string pferdId, string slot, string farbe)
        {
            if (Array.IndexOf(RanchHof.GearSlots, slot) < 0) return Unbekannt;
            var w = W(state);
            if (farbe != null && !((JArray)w["gear"]["owned"]).Any(o => J.Str(o) == slot + "_" + farbe)) return NichtGekauft;
            var equipped = (JObject)w["gear"]["equippedByHorse"];
            var eintrag = equipped[pferdId] as JObject ?? new JObject();
            eintrag[slot] = farbe != null ? (JToken)farbe : JValue.CreateNull();
            equipped[pferdId] = eintrag;
            var pferd = RanchHof.Pferd(state, pferdId);
            if (pferd?["ausruestung"] is JObject ausr) ausr[slot] = farbe != null ? (JToken)farbe : JValue.CreateNull();
            return Ok;
        }

        public static string GearAn(JObject state, string pferdId, string slot) =>
            J.Str(J.Path(state, RanchHof.SliceId, "wirtschaft", "gear", "equippedByHorse", pferdId ?? "", slot), null);
    }

    /// <summary>
    /// Caring for one horse (port of pflege_screen.gd's bookings): hay and apples come from the ranch store, carrots
    /// from the player's own fridge (inventory.food.carrot); water, brushing and mucking out the stable are free. Each
    /// action writes the new values, books the bond under the daily cap and stamps the care time.
    /// </summary>
    public static class RanchPflege
    {
        public const string Heu = "heu", Apfel = "apfel", Karotte = "karotte", Hafer = "hafer", Leckerli = "leckerli", Traenken = "traenken",
            Striegeln = "striegeln", Ausmisten = "ausmisten";
        public static readonly string[] Aktionen = { Heu, Apfel, Karotte, Hafer, Leckerli, Traenken, Striegeln, Ausmisten };
        public static readonly string[] FutterIds = { Heu, Apfel, Karotte, Hafer, Leckerli };

        /// <summary>How much of a feed the player has: hay and apples in the farm store, carrots in the fridge, oats and treats from Hufingen.</summary>
        public static int Vorrat(JObject state, string futter)
        {
            switch (futter)
            {
                case Heu:
                case Apfel: return J.Int(J.Path(state, RanchHof.SliceId, "wirtschaft", "lager", futter));
                case Karotte: return Karotten(state);
                case Hafer:
                case Leckerli: return RanchDorf.Futter(state, futter);
                default: return 0;
            }
        }

        public sealed class Ergebnis
        {
            public string Code = RanchWirtschaft.Unbekannt;
            public double Gewinn, Bindung, Laune;
            /// <summary>The wash place's warm-water bond bonus this brushing earned (0 = none).</summary>
            public int Warmwasser;
            public JObject Werte;
            public bool Ok => Code == RanchWirtschaft.Ok;
        }

        public static int Karotten(JObject state) => J.Int(J.Path(state, "inventory", "food", "carrot"));

        public static Ergebnis Pflegen(JObject state, string pferdId, string aktion, long nowMs, string tag)
        {
            var res = new Ergebnis();
            if (Array.IndexOf(Aktionen, aktion) < 0) return res;
            RanchHof.Slice(state);
            var pferd = RanchHof.Pferd(state, pferdId);
            if (pferd == null) return res;
            var werte = J.Dict(pferd["werte"]);
            var lager = RanchHof.Lager(state);
            switch (aktion)
            {
                case Heu:
                case Apfel:
                    if (J.Int(lager[aktion]) <= 0) { res.Code = RanchWirtschaft.LagerLeer; return res; }
                    lager[aktion] = J.Int(lager[aktion]) - 1;
                    werte = RanchCare.Fuettern(werte, aktion);
                    break;
                case Karotte:
                    if (Karotten(state) <= 0) { res.Code = RanchWirtschaft.LagerLeer; return res; }
                    var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                    food["carrot"] = Karotten(state) - 1;
                    werte = RanchCare.Fuettern(werte, aktion);
                    break;
                case Hafer:
                case Leckerli:
                    if (RanchDorf.Futter(state, aktion) <= 0) { res.Code = RanchWirtschaft.LagerLeer; return res; }
                    var futter = (JObject)RanchDorf.Im(state)["futter"];
                    futter[aktion] = J.Int(futter[aktion]) - 1;
                    werte = RanchCare.Fuettern(werte, aktion);
                    break;
                case Traenken: werte = RanchCare.Traenken(werte); break;
                case Striegeln: werte = RanchCare.Striegeln(werte, RanchBauEffekte.StriegelWert(state)); break;
                case Ausmisten:
                    // the slice is already healed: healing again here would swap the horse objects under our feet
                    J.EnsureDict(J.EnsureDict(J.EnsureDict(state, RanchHof.SliceId), "tiere"), "stall")["sauberkeit"] = RanchCare.Ausmisten();
                    werte = RanchCare.ClampWerte(werte);
                    break;
            }
            var bond = RanchCare.BondNachAktion(J.NumOr(pferd["bindung"], 0.0), J.NumOr(pferd["bondHeute"], 0.0), aktion, J.Str(pferd["bondTag"]) != (tag ?? ""));
            var warm = aktion == Striegeln ? RanchBauEffekte.WarmwasserBonus(state) : 0;
            if (warm > 0 && J.Str(pferd["warmwasserTag"]) != (tag ?? ""))
            {
                // the wash place's warm water: a bond bonus once a day per horse, past the daily cap
                var neu = Math.Min(100.0, bond.Bindung + warm);
                bond = new RanchCare.Bond(neu, bond.BondHeute, bond.Gewinn + (neu - bond.Bindung));
                pferd["warmwasserTag"] = tag ?? "";
                res.Warmwasser = warm;
            }
            pferd["werte"] = werte;
            pferd["bindung"] = bond.Bindung;
            pferd["bondHeute"] = bond.BondHeute;
            pferd["bondTag"] = tag ?? "";
            pferd["letztePflegeAt"] = nowMs;
            res.Code = RanchWirtschaft.Ok;
            res.Gewinn = bond.Gewinn;
            res.Bindung = bond.Bindung;
            res.Werte = werte;
            res.Laune = RanchCare.Laune(werte, bond.Bindung);
            return res;
        }

        /// <summary>Catches the horses up to now (the offline rules; the pasture fence helps); returns the warnings.</summary>
        public static List<string> Aufholen(JObject state, long nowMs, int offsetMin)
        {
            var ranch = RanchHof.Slice(state);
            var sim = RanchOffline.Simulate((JObject)ranch["tiere"], nowMs, offsetMin, RanchWirtschaft.WeideSauberkeitMult(state), RanchBauEffekte.WeideBoni(state));
            ranch["tiere"] = sim.Tiere;
            return sim.Events;
        }
    }
}
