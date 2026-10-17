using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// What a horse of the save brings to a ride — port of <c>RanchHorseLevels.stat_wert/stats_effektiv</c> and the breed
    /// lookup of <c>ranch_rassen.gd</c> (table <c>minigames/ranch/rassen.json</c>, 1:1 from Godot). The five training
    /// values are the breed base in <c>stats</c> (10 if missing) plus the trained points, clamped to 1–20 (a young horse
    /// tops out at <see cref="JungpferdDeckel"/>). Of the breed peculiarities only the tölt changes the riding itself in
    /// Unity (the extra gait); the other effects stay with the open ranch world that Unity does not have.
    /// </summary>
    public static class RanchPferd
    {
        public const string RassenPath = "minigames/ranch/rassen";
        public const int JungpferdDeckel = 15;
        public const string FallbackRasse = "puschelhufer";

        public static readonly string[] StatKeys = { "tempo", "ausdauer", "sprungkraft", "wendigkeit", "gelassenheit" };

        public static int StatWert(JObject pferd, string stat)
        {
            var basis = (int)Num((pferd?["stats"] as JObject)?[stat], 10.0);
            var punkte = (int)Num((pferd?["trainiert"] as JObject)?[stat], 0.0);
            var deckel = (string)pferd?["alter"] == "jungpferd" ? JungpferdDeckel : 20;
            return Math.Max(1, Math.Min(deckel, basis + punkte));
        }

        /// <summary>All five effective values (empty without a horse — the rides then fall back to the base horse).</summary>
        public static Dictionary<string, double> StatsEffektiv(JObject pferd)
        {
            var stats = new Dictionary<string, double>();
            if (pferd == null) return stats;
            foreach (var k in StatKeys) stats[k] = StatWert(pferd, k);
            return stats;
        }

        /// <summary>The breed entry of an id; an unknown id is a Puschelhufer (like Godot).</summary>
        public static JObject Rasse(string rasseId)
        {
            JObject fallback = null;
            foreach (var t in ContentSource.LoadObject(RassenPath)?["rassen"] as JArray ?? new JArray())
            {
                if (!(t is JObject e)) continue;
                var id = (string)e["id"] ?? "";
                if (id == rasseId) return e;
                if (id == FallbackRasse) fallback = e;
            }
            return fallback;
        }

        public static string Eigenheit(JObject pferd) => (string)Rasse((string)pferd?["rasse"] ?? "")?["eigenheit"] ?? "";

        /// <summary>Tölterle and every breed with the "toelt" peculiarity know the fourth gait.</summary>
        public static bool KannToelt(JObject pferd) => pferd != null && Eigenheit(pferd) == "toelt";

        // ── levels (RanchHorseLevels: xp_fuer_level … xp_buchen) ───────────────────────────────────────────────────────

        public const int LevelMax = 30;
        public const double XpRittTagesDeckel = 60.0;
        public const double FrischeMax = 100.0;

        /// <summary>Gifts for reaching a level (rpferd.meilenstein.*).</summary>
        public static readonly IReadOnlyDictionary<int, string> Meilensteine = new Dictionary<int, string>
        {
            [5] = "schleife_jungstar", [10] = "maehnenfrisur", [15] = "sattel_gold",
            [20] = "fanpost_besuch", [25] = "namensschild_gold", [30] = "statue_legende",
        };

        /// <summary>Trait effects that matter outside the ride (ranch_rassen.gd CHARAKTER_EFFEKTE, first hit wins).</summary>
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> CharakterEffekte =
            new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["verspielt"] = new Dictionary<string, double> { ["stern_xp_mult"] = 1.15, ["spielball_laune"] = 5.0 },
                ["fleissig"] = new Dictionary<string, double> { ["frische_verbrauch"] = 30.0 },
                ["stur"] = new Dictionary<string, double> { ["erster_wechsel_verzoegerung_s"] = 0.4, ["gelassenheit_statxp_mult"] = 1.2 },
            };

        /// <summary>XP from level l to l+1: 30·l + l².</summary>
        public static double XpFuerLevel(int level)
        {
            var l = Math.Max(1, level);
            return 30.0 * l + (double)(l * l);
        }

        public static double XpSummeBis(int level)
        {
            var summe = 0.0;
            for (var l = 1; l < Math.Max(1, Math.Min(level, LevelMax)); l++) summe += XpFuerLevel(l);
            return summe;
        }

        public static int LevelFuerXp(double xp)
        {
            var rest = Math.Max(0.0, xp);
            var level = 1;
            while (level < LevelMax && rest >= XpFuerLevel(level))
            {
                rest -= XpFuerLevel(level);
                level++;
            }
            return level;
        }

        public sealed class XpBuchung
        {
            public JObject Pferd;
            public double Gewinn;
            public List<int> LevelUps = new List<int>();
            public List<string> Meilensteine = new List<string>();
        }

        /// <summary>
        /// Books XP onto a copy of the horse: "ritt" is capped at <see cref="XpRittTagesDeckel"/> a day, "stern" grows
        /// with a playful horse, every other source (e.g. "wettbewerb") counts in full; the level follows the XP, every
        /// level passed is reported, milestone levels with their gift. A new day refills the freshness first.
        /// </summary>
        public static XpBuchung XpBuchen(JObject pferd, double menge, string quelle, string tag)
        {
            var p = (JObject)(pferd ?? new JObject()).DeepClone();
            TagReset(p, tag);
            var gewinn = Math.Max(0.0, menge);
            if (quelle == "ritt")
            {
                var heute = Num(p["rittXpHeute"], 0.0);
                gewinn = Math.Min(gewinn, Math.Max(0.0, XpRittTagesDeckel - heute));
                p["rittXpHeute"] = heute + gewinn;
            }
            if (quelle == "stern" && HatZug(p, "verspielt")) gewinn *= Effekt(p, "stern_xp_mult", 1.0);
            var vorher = LevelFuerXp(Num(p["xp"], 0.0));
            p["xp"] = Math.Min(XpSummeBis(LevelMax), Num(p["xp"], 0.0) + gewinn);
            var nachher = LevelFuerXp(Num(p["xp"], 0.0));
            p["level"] = nachher;
            var buchung = new XpBuchung { Pferd = p, Gewinn = gewinn };
            for (var l = vorher + 1; l <= nachher; l++)
            {
                buchung.LevelUps.Add(l);
                if (Meilensteine.TryGetValue(l, out var geschenk)) buchung.Meilensteine.Add(geschenk);
            }
            return buchung;
        }

        // ── training by doing (RanchHorseLevels.stat_xp_buchen / ritt_training — defined in Godot, never called there) ──

        public const double PunktKostenBasis = 100.0, PunktKostenFaktor = 1.4, GateLevelJePunkt = 3.0;
        public const int PunkteMax = 10;
        public const double FrischeEinheitS = 180.0, FrischeVerbrauch = 35.0, FrischeFaktorMin = 0.15;
        public const double RateGaloppXpJe50M = 1.0, RateAusdauerXpJeMin = 2.0, RateSprungGut = 4.0, RateSprungPerfekt = 8.0, RateSlalomTor = 2.0,
            RateSchrittLauneJeMin = 3.0, SchrittLauneAb = 60.0;
        public const double AktivSJeSprung = 20.0, AktivSJeSlalomTor = 12.0, XpRittProMin = 4.0, JungpferdStatXpMult = 1.2;

        /// <summary>Unity: training in the rain without a riding hall teaches half as much (Godot's "Reithalle: Training auch bei Regen").</summary>
        public const double RegenStatMult = 0.5;

        /// <summary>The most trained points a value can have at a level: ⌈level / 3⌉, at most 10.</summary>
        public static int StatGate(int level) => Math.Min(PunkteMax, (int)Math.Ceiling(Math.Max(1, Math.Min(LevelMax, level)) / GateLevelJePunkt));

        /// <summary>Stat XP for the point p → p+1: 100 · 1.4^p.</summary>
        public static double PunktKosten(int p) => PunktKostenBasis * Math.Pow(PunktKostenFaktor, Math.Max(0, p));

        /// <summary>A tired value learns less: max(0.15, freshness / 100).</summary>
        public static double FrischeFaktor(double frische) => Math.Max(FrischeFaktorMin, frische / FrischeMax);

        public sealed class StatBuchung
        {
            public JObject Pferd;
            public double XpEffektiv;
            public int NeuePunkte;
        }

        /// <summary>
        /// Books stat XP onto a copy of the horse: freshness and traits scale it (a stubborn horse ×1.2 on calm, a young
        /// horse ×1.2), the active seconds use up freshness (35 a 3-minute unit, 30 for a diligent horse) and ripe points are
        /// handed out at once within the level gate (the rest is banked, at most one point's worth).
        /// </summary>
        public static StatBuchung StatXpBuchen(JObject pferd, string stat, double xpMenge, double aktivS, string tag, double mult = 1.0)
        {
            var p = (JObject)(pferd ?? new JObject()).DeepClone();
            TagReset(p, tag);
            if (Array.IndexOf(StatKeys, stat) < 0) return new StatBuchung { Pferd = p };
            var frische = p["frische"] as JObject ?? new JObject();
            var faktor = FrischeFaktor(Num(frische[stat], FrischeMax));
            if (stat == "gelassenheit" && HatZug(p, "stur")) mult *= Effekt(p, "gelassenheit_statxp_mult", 1.0);
            if ((string)p["alter"] == "jungpferd") mult *= JungpferdStatXpMult;
            var effektiv = Math.Max(0.0, xpMenge) * faktor * Math.Max(0.0, mult);
            var statxp = p["statXp"] as JObject ?? new JObject();
            statxp[stat] = Num(statxp[stat], 0.0) + effektiv;
            p["statXp"] = statxp;
            var verbrauch = HatZug(p, "fleissig") ? Effekt(p, "frische_verbrauch", FrischeVerbrauch) : FrischeVerbrauch;
            frische[stat] = Math.Max(0.0, Math.Min(FrischeMax, Num(frische[stat], FrischeMax) - verbrauch * Math.Max(0.0, aktivS) / FrischeEinheitS));
            p["frische"] = frische;
            return new StatBuchung { Pferd = p, XpEffektiv = effektiv, NeuePunkte = PunkteVergeben(p, stat) };
        }

        /// <summary>What a ride did: moving time, gallop metres and seconds, jumps, slalom gates, calm walking in a good mood.</summary>
        public sealed class Telemetrie
        {
            public double UnterwegsS, GaloppM, GaloppS, SchrittLauneS;
            public int SprungGut, SprungPerfekt, SlalomTore;

            public bool Leer => UnterwegsS <= 0.0 && GaloppM <= 0.0 && SprungGut + SprungPerfekt + SlalomTore == 0 && SchrittLauneS <= 0.0;

            /// <summary>ride_controller._sammle_telemetrie: moving above 0.2 m/s counts, gallop metres, walking with a mood of 60+.</summary>
            public void Sammle(double dt, string gangart, double tempo, double laune)
            {
                if (dt <= 0.0) return;
                if (tempo > 0.2) UnterwegsS += dt;
                if (gangart == "galopp")
                {
                    GaloppM += tempo * dt;
                    GaloppS += dt;
                }
                if (gangart == "schritt" && laune >= SchrittLauneAb) SchrittLauneS += dt;
            }
        }

        public sealed class RittBuchung
        {
            public JObject Pferd;
            public double Xp;
            public List<int> LevelUps = new List<int>();
            public List<string> Meilensteine = new List<string>();
            public Dictionary<string, int> NeuePunkte = new Dictionary<string, int>();
        }

        /// <summary>
        /// A ride into training and horse XP: gallop metres → speed, minutes on the way → stamina, jumps → jumping power,
        /// slalom gates → agility, calm walking → composure; then 4 ride XP a minute under the daily cap.
        /// <paramref name="statMult"/> scales the stat XP (rain without a riding hall: <see cref="RegenStatMult"/>).
        /// </summary>
        public static RittBuchung RittTraining(JObject pferd, Telemetrie t, string tag, double statMult = 1.0)
        {
            var minuten = Math.Max(0.0, t.UnterwegsS) / 60.0;
            var galoppM = Math.Max(0.0, t.GaloppM);
            var galoppS = t.GaloppS > 0.0 ? t.GaloppS : galoppM / 8.5;
            var spruenge = t.SprungGut + t.SprungPerfekt;
            var schrittMin = Math.Max(0.0, t.SchrittLauneS) / 60.0;
            var buchungen = new (string stat, double xp, double aktivS)[]
            {
                ("tempo", galoppM / 50.0 * RateGaloppXpJe50M, galoppS),
                ("ausdauer", minuten * RateAusdauerXpJeMin, minuten * 60.0),
                ("sprungkraft", t.SprungGut * RateSprungGut + t.SprungPerfekt * RateSprungPerfekt, spruenge * AktivSJeSprung),
                ("wendigkeit", t.SlalomTore * RateSlalomTor, t.SlalomTore * AktivSJeSlalomTor),
                ("gelassenheit", schrittMin * RateSchrittLauneJeMin, schrittMin * 60.0),
            };
            var p = pferd;
            var o = new RittBuchung();
            foreach (var (stat, xp, aktivS) in buchungen)
            {
                if (xp <= 0.0) continue;
                var e = StatXpBuchen(p, stat, xp, aktivS, tag, statMult);
                p = e.Pferd;
                if (e.NeuePunkte > 0) o.NeuePunkte[stat] = e.NeuePunkte;
            }
            var x = XpBuchen(p, minuten * XpRittProMin, "ritt", tag);
            o.Pferd = x.Pferd;
            o.Xp = x.Gewinn;
            o.LevelUps = x.LevelUps;
            o.Meilensteine = x.Meilensteine;
            return o;
        }

        /// <summary>A ride booked onto the horse in the save (null without that horse).</summary>
        public static RittBuchung RittAnPferd(JObject state, string pferdId, Telemetrie t, string tag, double statMult = 1.0)
        {
            if (state == null || string.IsNullOrEmpty(pferdId) || t == null) return null;
            if (!(state["ranch"]?["tiere"]?["pferde"]?[pferdId] is JObject pferd)) return null;
            var buchung = RittTraining(pferd, t, tag, statMult);
            ((JObject)state["ranch"]["tiere"]["pferde"])[pferdId] = buchung.Pferd;
            return buchung;
        }

        /// <summary>How far a value is to its next point (0..1; 1 when the level gate or the cap holds it).</summary>
        public static double StatFortschritt(JObject pferd, string stat)
        {
            var punkte = (int)Num((pferd?["trainiert"] as JObject)?[stat], 0.0);
            var gate = StatGate((int)Num(pferd?["level"], 1.0));
            if (punkte >= gate || punkte >= PunkteMax) return 1.0;
            return Math.Max(0.0, Math.Min(1.0, Num((pferd?["statXp"] as JObject)?[stat], 0.0) / PunktKosten(punkte)));
        }

        private static int PunkteVergeben(JObject p, string stat)
        {
            var trainiert = p["trainiert"] as JObject ?? new JObject();
            var statxp = p["statXp"] as JObject ?? new JObject();
            var gate = StatGate((int)Num(p["level"], 1.0));
            var punkte = (int)Num(trainiert[stat], 0.0);
            var xp = Num(statxp[stat], 0.0);
            var neu = 0;
            while (punkte < gate && punkte < PunkteMax && xp >= PunktKosten(punkte))
            {
                xp -= PunktKosten(punkte);
                punkte++;
                neu++;
            }
            statxp[stat] = Math.Min(xp, PunktKosten(punkte));
            trainiert[stat] = punkte;
            p["trainiert"] = trainiert;
            p["statXp"] = statxp;
            return neu;
        }

        /// <summary>RanchCompTurnier.xp_an_pferd: tournament XP onto the horse in the save (source "wettbewerb", no cap).</summary>
        public static XpBuchung XpAnPferd(JObject state, string pferdId, double menge, string tag)
        {
            if (state == null || string.IsNullOrEmpty(pferdId)) return null;
            if (!(state["ranch"]?["tiere"]?["pferde"]?[pferdId] is JObject pferd)) return null;
            var buchung = XpBuchen(pferd, menge, "wettbewerb", tag);
            ((JObject)state["ranch"]["tiere"]["pferde"])[pferdId] = buchung.Pferd;
            return buchung;
        }

        private static void TagReset(JObject p, string tag)
        {
            var t = p["frischeTag"];
            var bisher = t == null || t.Type == JTokenType.Null ? "" : t.Type == JTokenType.String ? (string)t : t.ToString();
            if (bisher == (tag ?? "")) return;
            p["frischeTag"] = tag;
            var frische = new JObject();
            foreach (var k in StatKeys) frische[k] = FrischeMax;
            p["frische"] = frische;
            p["rittXpHeute"] = 0.0;
        }

        private static bool HatZug(JObject p, string zug) =>
            p["charakter"] is JArray charakter && charakter.Any(t => t.Type == JTokenType.String && (string)t == zug);

        private static double Effekt(JObject p, string key, double fallback)
        {
            if (!(p["charakter"] is JArray charakter)) return fallback;
            foreach (var t in charakter)
                if (CharakterEffekte.TryGetValue(t.Type == JTokenType.String ? (string)t : t.ToString(), out var effekte) && effekte.TryGetValue(key, out var v))
                    return v;
            return fallback;
        }

        /// <summary>Godot's _num: a number or the fallback (text counts as missing).</summary>
        private static double Num(JToken t, double fallback) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : fallback;
    }
}
