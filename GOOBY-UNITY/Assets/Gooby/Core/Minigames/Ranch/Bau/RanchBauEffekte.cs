using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// What the build levels do (port of ranch_bau_effekte.gd) — pure readings of <c>ranch.bau</c> and the balance's
    /// "effekte": more stable boxes = more horses, the hay barn stores more bales, the pasture keeps hunger down (and the
    /// herb pasture lifts the mood), the water place slows thirst (later the horses drink by themselves), the wash place
    /// makes brushing count more (with a warm-water bond bonus), the walker trains horses passively, the riding hall and
    /// the jump course add tournament XP, the breeding stable makes room for more pregnant mares, the foal pasture lets
    /// foals grow faster, the stand brings spectators and gold, the pasture fence keeps the paddock clean, five pieces of
    /// decoration make a style point. The state helpers read defensively: a save without <c>ranch.bau</c> keeps the old
    /// values, so nothing changes until the farm has been opened once (and migrated).
    /// </summary>
    public static class RanchBauEffekte
    {
        /// <summary>The self-drinker keeps thirst on the pasture at least here; the herb pasture's bonus stops here.</summary>
        public const double SelbsttraenkeBoden = 50.0, KraeuterDeckel = 70.0;

        private static JObject Eff(JObject balance) => RanchBauKatalog.Effekte(balance ?? RanchBauKatalog.Balance);

        /// <summary>A level's value of an effect row: row[level-1] clamped; level 0 (not built) gives the fallback.</summary>
        public static JToken StufenWert(JObject balance, string anlageId, string effekt, int stufe, JToken fallback)
        {
            if (stufe <= 0) return fallback;
            if (!(J.Dict(Eff(balance)[anlageId])[effekt] is JArray reihe) || reihe.Count == 0) return fallback;
            return reihe[J.ClampI(stufe, 1, reihe.Count) - 1];
        }

        private static double Zahl(JObject bau, JObject balance, string id, string effekt, double fallback) =>
            J.NumOr(StufenWert(balance, id, effekt, RanchBauState.AnlageStufe(bau, id), fallback), fallback);

        private static bool Wahr(JObject bau, JObject balance, string id, string effekt) =>
            J.IsTrue(StufenWert(balance, id, effekt, RanchBauState.AnlageStufe(bau, id), false));

        private static double Basis(JObject balance, string key, double fallback) => J.NumOr(Eff(balance)[key], fallback);

        /// <summary>Most horses: stable boxes 2/4/6/8 (2 without).</summary>
        public static int BoxenKapazitaet(JObject bau, JObject balance) =>
            (int)Zahl(bau, balance, "stallboxen", "kapazitaet", Basis(balance, "boxen_kapazitaet_basis", 2.0));

        /// <summary>Hunger decay multiplier (pasture level 2: lush grass ×0.85).</summary>
        public static double HungerMult(JObject bau, JObject balance) => Zahl(bau, balance, "weide", "hunger_mult", 1.0);

        /// <summary>Mood lift per hour on the pasture up to <see cref="KraeuterDeckel"/> (pasture level 3: herbs).</summary>
        public static int LauneBonusProH(JObject bau, JObject balance) => (int)Zahl(bau, balance, "weide", "laune_bonus_h", 0.0);

        /// <summary>Dirt decay multiplier of the pasture fence (0.8/0.7/0.6; 1.0 without).</summary>
        public static double SauberkeitMult(JObject bau, JObject balance) => Zahl(bau, balance, "weidezaun", "sauberkeit_mult", 1.0);

        /// <summary>Thirst decay multiplier (water place ×0.7).</summary>
        public static double DurstMult(JObject bau, JObject balance) => Zahl(bau, balance, "wasserstelle", "durst_mult", 1.0);

        /// <summary>The self-drinker (water place level 2): pasture horses drink by themselves.</summary>
        public static bool SelbsttraenkeAktiv(JObject bau, JObject balance) => Wahr(bau, balance, "wasserstelle", "selbsttraenke");

        /// <summary>Hay bales the store holds (12/24/48 with the hay barn; 10 without).</summary>
        public static int HeuKapazitaet(JObject bau, JObject balance) =>
            (int)Zahl(bau, balance, "heulager", "heu_kapazitaet", Basis(balance, "heu_kapazitaet_basis", 8.0));

        /// <summary>Cleanliness one brushing gives (wash place: 50 instead of 35).</summary>
        public static int StriegelWert(JObject bau, JObject balance) =>
            (int)Zahl(bau, balance, "waschplatz", "striegel_wert", Basis(balance, "striegel_wert_basis", 35.0));

        /// <summary>The warm-water bond bonus once a day per horse (wash place level 2).</summary>
        public static int WarmwasserBonus(JObject bau, JObject balance) => (int)Zahl(bau, balance, "waschplatz", "warmwasser_bonus", 0.0);

        /// <summary>Passive walker training: horse XP a day (0/40/60/80) for as many horses as it has places.</summary>
        public static int FuehranlageXpProTag(JObject bau, JObject balance) => (int)Zahl(bau, balance, "fuehranlage", "xp_pro_tag", 0.0);

        public static int FuehranlagePlaetze(JObject bau, JObject balance) => (int)Zahl(bau, balance, "fuehranlage", "plaetze", 0.0);

        /// <summary>Training in the rain (the riding hall from level 1).</summary>
        public static bool TrainingBeiRegen(JObject bau, JObject balance) => Wahr(bau, balance, "reithalle", "regen_training");

        /// <summary>The riding hall's dressage XP (+20 %).</summary>
        public static double DressurXpMult(JObject bau, JObject balance) => Zahl(bau, balance, "reithalle", "dressur_xp_mult", 1.0);

        /// <summary>The jump course's coin bonus in the parcours (×1.1 — the old riding arena).</summary>
        public static double ParcoursCoinMult(JObject bau, JObject balance) => Zahl(bau, balance, "parcours", "coin_mult", 1.0);

        /// <summary>The jump course's show-jumping XP (level 2+: +20 %).</summary>
        public static double SpringXpMult(JObject bau, JObject balance) => Zahl(bau, balance, "parcours", "spring_xp_mult", 1.0);

        /// <summary>The saddle room's gear discount in percent (10/15/20 — a Unity effect).</summary>
        public static int GearRabatt(JObject bau, JObject balance) => (int)Zahl(bau, balance, "sattelkammer", "gear_rabatt", 0.0);

        /// <summary>
        /// Outfit sets for quick re-saddling (Godot's <c>outfit_sets</c>/<c>extra_slots</c>, never wired there): one set
        /// with the saddle room, three from level 2 — 0 without it.
        /// </summary>
        public static int OutfitSets(JObject bau, JObject balance)
        {
            if (!Wahr(bau, balance, "sattelkammer", "outfit_sets")) return 0;
            return Wahr(bau, balance, "sattelkammer", "extra_slots") ? 3 : 1;
        }

        /// <summary>Extra pregnancies at once (breeding stable 1/2).</summary>
        public static int Traechtigkeiten(JObject bau, JObject balance) => (int)Zahl(bau, balance, "zuchtstall", "traechtigkeiten", 0.0);

        /// <summary>How much faster foals grow up (foal pasture ×1.15).</summary>
        public static double AufzuchtXpMult(JObject bau, JObject balance) => Zahl(bau, balance, "fohlenweide", "aufzucht_xp_mult", 1.0);

        /// <summary>Spectators at home shows (stand 6/12; 0 without).</summary>
        public static int TribueneZuschauer(JObject bau, JObject balance) => (int)Zahl(bau, balance, "tribuene", "zuschauer", 0.0);

        /// <summary>The tournament gold multiplier (stand level 2: +15 %).</summary>
        public static double HeimGoldMult(JObject bau, JObject balance) => Zahl(bau, balance, "tribuene", "heim_gold_mult", 1.0);

        /// <summary>
        /// Style points of the decoration: every <c>deko_stil_pro</c> pieces +1, capped at <c>deko_stil_max</c>; a quest
        /// reward counts as five pieces.
        /// </summary>
        public static int DekoStilpunkte(JObject bau, JObject balance)
        {
            var pro = Math.Max(1, (int)Basis(balance, "deko_stil_pro", 5.0));
            var deckel = Math.Max(0, (int)Basis(balance, "deko_stil_max", 10.0));
            var defs = RanchBauKatalog.Defs(balance ?? RanchBauKatalog.Balance);
            var anzahl = (bau?["items"] as JArray ?? new JArray()).OfType<JObject>()
                .Sum(e => defs.TryGetValue(J.Str(e["item"]), out var d) && d.Kategorie == "deko" ? d.Stil : 0);
            return Math.Min(deckel, anzahl / pro);
        }

        public static int DekoStilMax(JObject balance) => Math.Max(0, (int)Basis(balance, "deko_stil_max", 10.0));

        /// <summary>Every effect at once (the build mode's summary).</summary>
        public static JObject Zusammenfassung(JObject bau, JObject balance) => new JObject
        {
            ["boxen_kapazitaet"] = BoxenKapazitaet(bau, balance),
            ["hunger_mult"] = HungerMult(bau, balance),
            ["laune_bonus_pro_h"] = LauneBonusProH(bau, balance),
            ["sauberkeit_mult"] = SauberkeitMult(bau, balance),
            ["durst_mult"] = DurstMult(bau, balance),
            ["selbsttraenke"] = SelbsttraenkeAktiv(bau, balance),
            ["heu_kapazitaet"] = HeuKapazitaet(bau, balance),
            ["striegel_wert"] = StriegelWert(bau, balance),
            ["warmwasser_bonus"] = WarmwasserBonus(bau, balance),
            ["fuehranlage_xp_pro_tag"] = FuehranlageXpProTag(bau, balance),
            ["training_bei_regen"] = TrainingBeiRegen(bau, balance),
            ["dressur_xp_mult"] = DressurXpMult(bau, balance),
            ["parcours_coin_mult"] = ParcoursCoinMult(bau, balance),
            ["tribuene_zuschauer"] = TribueneZuschauer(bau, balance),
            ["heim_gold_mult"] = HeimGoldMult(bau, balance),
            ["deko_stilpunkte"] = DekoStilpunkte(bau, balance),
        };

        // ── on the save (defensive: no ranch.bau → the values before the build mode) ──

        private static JObject BauOderNull(JObject state) => RanchBauState.Vorhanden(state) ? RanchBauState.Lese(state) : null;

        /// <summary>Horses the farm holds: the old boxes or the grid's stable boxes, whichever is more (nobody loses room).</summary>
        public static int BoxenKapazitaet(JObject state, int alt)
        {
            var bau = BauOderNull(state);
            return bau == null ? alt : Math.Max(alt, BoxenKapazitaet(bau, null));
        }

        /// <summary>Hay bales the store takes; int.MaxValue before the build mode existed.</summary>
        public static int HeuKapazitaet(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? int.MaxValue : HeuKapazitaet(bau, null);
        }

        public static double SauberkeitMult(JObject state, double alt)
        {
            var bau = BauOderNull(state);
            return bau == null ? alt : Math.Min(alt, SauberkeitMult(bau, null));
        }

        public static int StriegelWert(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? (int)RanchCare.StriegelBasis : StriegelWert(bau, null);
        }

        public static int WarmwasserBonus(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? 0 : WarmwasserBonus(bau, null);
        }

        public static int GearRabatt(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? 0 : GearRabatt(bau, null);
        }

        public static int OutfitSets(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? 0 : OutfitSets(bau, null);
        }

        /// <summary>Can the horses train dry in the rain (the riding hall)?</summary>
        public static bool TrainingBeiRegen(JObject state)
        {
            var bau = BauOderNull(state);
            return bau != null && TrainingBeiRegen(bau, null);
        }

        public static int Traechtigkeiten(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? 0 : Traechtigkeiten(bau, null);
        }

        /// <summary>The age clock of foals (1.0, or the foal pasture's 1.15).</summary>
        public static double AufzuchtTempo(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? 1.0 : Math.Max(1.0, AufzuchtXpMult(bau, null));
        }

        /// <summary>The spectators the farm's stand brings to a tournament (0 without one).</summary>
        public static int TribueneZuschauer(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? 0 : TribueneZuschauer(bau, null);
        }

        public static double HeimGoldMult(JObject state)
        {
            var bau = BauOderNull(state);
            return bau == null ? 1.0 : Math.Max(1.0, HeimGoldMult(bau, null));
        }

        /// <summary>Tournament XP of a discipline: dressage with the riding hall, show jumping with the jump course.</summary>
        public static double TurnierXpMult(JObject state, string disziplin)
        {
            var bau = BauOderNull(state);
            if (bau == null) return 1.0;
            if (disziplin == "dressur") return Math.Max(1.0, DressurXpMult(bau, null));
            if (disziplin == "springen") return Math.Max(1.0, SpringXpMult(bau, null));
            return 1.0;
        }

        /// <summary>The parcours's coin multiplier: the old riding arena or the grid's jump course.</summary>
        public static double ParcoursCoinMult(JObject state, double alt)
        {
            var bau = BauOderNull(state);
            return bau == null ? alt : Math.Max(alt, ParcoursCoinMult(bau, null));
        }

        public sealed class Weide
        {
            public double Hunger = 1.0, Durst = 1.0, DurstBoden, KraeuterProH;
        }

        /// <summary>What the pasture facilities do to the horses while they graze (offline catch-up).</summary>
        public static Weide WeideBoni(JObject state)
        {
            var bau = BauOderNull(state);
            if (bau == null) return new Weide();
            return new Weide
            {
                Hunger = HungerMult(bau, null), Durst = DurstMult(bau, null),
                DurstBoden = SelbsttraenkeAktiv(bau, null) ? SelbsttraenkeBoden : 0.0, KraeuterProH = LauneBonusProH(bau, null),
            };
        }

        public sealed class Training
        {
            public string PferdId;
            public RanchPferd.XpBuchung Buchung;
        }

        /// <summary>
        /// The walker's day: once a day the rideable horses that need it most (lowest level first) get its XP, as many as
        /// it has places. Returns who trained ([] when there is no walker or today is already done).
        /// </summary>
        public static List<Training> FuehranlageTag(JObject state, string tag, long nowMs)
        {
            var o = new List<Training>();
            var bau = BauOderNull(state);
            if (bau == null || string.IsNullOrEmpty(tag)) return o;
            var xp = FuehranlageXpProTag(bau, null);
            var plaetze = FuehranlagePlaetze(bau, null);
            if (xp <= 0 || plaetze <= 0 || J.Str(bau["fuehranlageTag"]) == tag) return o;
            var pferde = J.Path(state, RanchHof.SliceId, "tiere", "pferde") as JObject ?? new JObject();
            var tempo = AufzuchtTempo(state);
            var wahl = pferde.Properties()
                .Where(p => p.Value is JObject pf && RanchZucht.Reitbar(RanchZucht.Alter(pf, nowMs, tempo)) && J.Int(pf["level"], 1) < RanchPferd.LevelMax)
                .OrderBy(p => J.Int(p.Value["level"], 1)).ThenBy(p => p.Name, StringComparer.Ordinal)
                .Take(plaetze).Select(p => p.Name).ToList();
            foreach (var id in wahl)
            {
                var buchung = RanchPferd.XpBuchen((JObject)pferde[id], xp, "fuehranlage", tag);
                pferde[id] = buchung.Pferd;
                o.Add(new Training { PferdId = id, Buchung = buchung });
            }
            RanchBauState.Im(state)["fuehranlageTag"] = tag;
            return o;
        }
    }
}
