using System;
using System.Collections.Generic;
using System.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The seven scoring formulas of the competitions (wertung_*.gd, RANCH-DLC-IDEAS-3 ch. 5.2) — pure. Points
    /// disciplines grow with quality, time disciplines (cross-country, barrels, race) shrink.
    /// </summary>
    public static class RcompWertung
    {
        /// <summary>No. 1 show jumping: 1000 − 40·knock-downs − 20·refusals − 5·s over the set time + 15·perfect takeoffs.</summary>
        public static class Springen
        {
            public const double Basis = 1000.0, AbwurfMalus = 40.0, VerweigerungMalus = 20.0, ZeitMalusProS = 5.0, PerfektBonus = 15.0;
            public static readonly int[] SterneAb = { 900, 750, 550 };

            public static readonly IReadOnlyDictionary<string, int> HindernisseJeKlasse = new Dictionary<string, int>
            {
                ["holz"] = 8, ["bronze"] = 9, ["silber"] = 11, ["gold"] = 12, ["sternenklasse"] = 14,
            };

            public static int Score(int abwuerfe, int verweigerungen, double zeitS, double richtzeitS, int perfekte)
            {
                var wert = Basis - AbwurfMalus * Math.Max(0, abwuerfe) - VerweigerungMalus * Math.Max(0, verweigerungen)
                           - ZeitMalusProS * Math.Max(0.0, zeitS - richtzeitS) + PerfektBonus * Math.Max(0, perfekte);
                return Math.Max(0, (int)Math.Round(wert, MidpointRounding.AwayFromZero));
            }

            public static int Sterne(int punkte)
            {
                for (var i = 0; i < SterneAb.Length; i++) if (punkte >= SterneAb[i]) return 3 - i;
                return 0;
            }

            public static int HindernisAnzahl(string klasse) => klasse != null && HindernisseJeKlasse.TryGetValue(klasse, out var n) ? n : 8;
        }

        /// <summary>No. 2 dressage: per figure 100 − 50·(d̄/0.75) − 25·gait errors; total = mean + 10 rhythm bonus.</summary>
        public static class Dressur
        {
            public const double FigurMax = 100.0, AbweichungReferenzM = 0.75, AbweichungMalus = 50.0, GangartfehlerMalus = 25.0;
            public const double TaktFensterMs = 250.0, TaktBonus = 10.0;
            public static readonly double[] SterneAb = { 85.0, 70.0, 50.0 };
            public static readonly string[] Figuren = { "zirkel", "acht", "schlangenlinie", "halten", "rueckwaerts" };

            public static double FigurPunkte(double dMittelM, int gangartfehler) =>
                Math.Max(0.0, FigurMax - AbweichungMalus * (Math.Max(0.0, dMittelM) / AbweichungReferenzM) - GangartfehlerMalus * Math.Max(0, gangartfehler));

            public static bool TaktOk(IEnumerable<double> wechselAbweichungenMs) => wechselAbweichungenMs.All(w => Math.Abs(w) <= TaktFensterMs);

            public static double Gesamt(IList<double> figurPunkte, bool mitTaktbonus)
            {
                if (figurPunkte == null || figurPunkte.Count == 0) return 0.0;
                var summe = 0.0;
                foreach (var w in figurPunkte) summe += Math.Max(0.0, Math.Min(FigurMax, w));
                return summe / figurPunkte.Count + (mitTaktbonus ? TaktBonus : 0.0);
            }

            public static int Sterne(double punkte)
            {
                for (var i = 0; i < SterneAb.Length; i++) if (punkte >= SterneAb[i]) return 3 - i;
                return 0;
            }
        }

        /// <summary>No. 3 cross-country: time + 8 s per missed gate (smaller wins).</summary>
        public static class Gelaende
        {
            public const double TorStrafeS = 8.0;
            public static readonly double[] SterneMult = { 1.0, 1.1, 1.25 };

            public static readonly IReadOnlyDictionary<string, int> ToreJeKlasse = new Dictionary<string, int>
            {
                ["holz"] = 8, ["bronze"] = 10, ["silber"] = 12, ["gold"] = 13, ["sternenklasse"] = 15,
            };

            public static double WertungS(double zeitS, int toreVerpasst) => Math.Max(0.0, zeitS) + TorStrafeS * Math.Max(0, toreVerpasst);

            public static int Sterne(double wertung, double richtzeitS)
            {
                if (richtzeitS <= 0.0) return 0;
                for (var i = 0; i < SterneMult.Length; i++) if (wertung <= richtzeitS * SterneMult[i]) return 3 - i;
                return 0;
            }

            public static int TorAnzahl(string klasse) => klasse != null && ToreJeKlasse.TryGetValue(klasse, out var n) ? n : 8;
        }

        /// <summary>No. 4 grass race: finishing order over 3 laps; slipstream (+3 % for 3 s after 1 s within 2 m).</summary>
        public static class Rennen
        {
            public const int Runden = 3, TeilnehmerMin = 4, TeilnehmerMax = 8;
            public const double WindschattenFensterM = 2.0, WindschattenSeiteM = 1.2, WindschattenAufbauS = 1.0, WindschattenBoost = 0.03, WindschattenDauerS = 3.0;

            public struct Zustand
            {
                public double AufbauS, BoostRestS;
            }

            public static Zustand StepWindschatten(Zustand z, bool dahinter, double dt)
            {
                var aufbau = z.AufbauS;
                var rest = Math.Max(0.0, z.BoostRestS - dt);
                if (dahinter)
                {
                    aufbau += dt;
                    if (aufbau >= WindschattenAufbauS && rest <= 0.0)
                    {
                        rest = WindschattenDauerS;
                        aufbau = 0.0;
                    }
                }
                else aufbau = 0.0;
                return new Zustand { AufbauS = aufbau, BoostRestS = rest };
            }

            public static bool ImFenster(double streckeVorneM, double seitlichM) =>
                streckeVorneM > 0.0 && streckeVorneM < WindschattenFensterM && Math.Abs(seitlichM) <= WindschattenSeiteM;

            public static double TempoMult(Zustand z) => z.BoostRestS > 0.0 ? 1.0 + WindschattenBoost : 1.0;

            /// <summary>Finishing order from finishing times; DNF (≤ 0) goes last, stable in input order.</summary>
            public static List<(string id, double zeitS)> Reihenfolge(IEnumerable<(string id, double zeitS)> zielzeiten)
            {
                var list = zielzeiten.ToList();
                var fertig = list.Where(e => e.zeitS > 0.0).OrderBy(e => e.zeitS).ToList();
                fertig.AddRange(list.Where(e => !(e.zeitS > 0.0)));
                return fertig;
            }

            public static int PlatzVon(IList<(string id, double zeitS)> sortiert, string id)
            {
                for (var i = 0; i < sortiert.Count; i++) if (sortiert[i].id == id) return i + 1;
                return 0;
            }

            public static int Sterne(int platz, int teilnehmer)
            {
                if (platz == 1) return 3;
                if (platz > 1 && platz <= 3) return 2;
                if (platz > 0 && platz <= Math.Max(1, teilnehmer / 2)) return 1;
                return 0;
            }
        }

        /// <summary>No. 5 western trail: 6 tasks at 0–10 (touch −2) + time bonus max(0; 20 − ⌈time−90 s⌉), max 80.</summary>
        public static class Trail
        {
            public const int AufgabenAnzahl = 6, AufgabeMax = 10, BeruehrungMalus = 2, ZeitbonusMax = 20, Maximum = 80;
            public const double ZeitbonusAbS = 90.0;
            public static readonly int[] SterneAb = { 70, 55, 40 };
            public static readonly string[] Aufgaben = { "tor", "rueckwaerts_l", "slalom", "bruecke", "plane", "kreisel" };

            public static int AufgabePunkte(int beruehrungen, bool ausgelassen) =>
                ausgelassen ? 0 : Math.Max(0, Math.Min(AufgabeMax, AufgabeMax - BeruehrungMalus * Math.Max(0, beruehrungen)));

            public static int Zeitbonus(double zeitS) => Math.Max(0, ZeitbonusMax - (int)Math.Ceiling(Math.Max(0.0, zeitS - ZeitbonusAbS)));

            public static int Gesamt(IEnumerable<int> aufgabenPunkte, double zeitS)
            {
                var summe = 0;
                foreach (var w in aufgabenPunkte) summe += Math.Max(0, Math.Min(AufgabeMax, w));
                return Math.Min(Maximum, summe + Zeitbonus(zeitS));
            }

            public static int Sterne(int punkte)
            {
                for (var i = 0; i < SterneAb.Length; i++) if (punkte >= SterneAb[i]) return 3 - i;
                return 0;
            }
        }

        /// <summary>No. 6 show: 0.4·care + 0.3·style + 0.3·routine (0–100 each); the routine is a 5-command Simon-says.</summary>
        public static class Schau
        {
            public const double GewichtPflege = 0.4, GewichtStil = 0.3, GewichtKuer = 0.3;
            public const int SetBonus = 10, ThemaBonus = 15, KuerKommandos = 5;
            public const double KuerPunkteJeTreffer = 20.0, KuerFensterMs = 300.0;
            public static readonly double[] SterneAb = { 85.0, 70.0, 50.0 };
            public static readonly string[] Kommandos = { "verbeugen", "drehen", "steigen", "kompliment", "kuss" };

            public static readonly IReadOnlyDictionary<string, int> RaritaetPunkte = new Dictionary<string, int>
            {
                ["gewoehnlich"] = 4, ["selten"] = 7, ["episch"] = 11, ["legendaer"] = 16,
            };

            public static double Pflege(double sauberkeit, double laune) =>
                Clamp((Clamp(sauberkeit, 0.0, 100.0) + Clamp(laune, 0.0, 100.0)) / 2.0, 0.0, 100.0);

            public static double Stil(IEnumerable<string> raritaeten, bool setKomplett, bool themaGetroffen, double stilMult = 1.0)
            {
                var punkte = 0.0;
                foreach (var r in raritaeten) punkte += r != null && RaritaetPunkte.TryGetValue(r, out var p) ? p : 0;
                if (setKomplett) punkte += SetBonus;
                if (themaGetroffen) punkte += ThemaBonus;
                return Clamp(punkte * Math.Max(0.0, stilMult), 0.0, 100.0);
            }

            public static bool KuerTreffer(double abweichungMs) => Math.Abs(abweichungMs) <= KuerFensterMs;

            public static double Kuer(int treffer) => Clamp(KuerPunkteJeTreffer * Math.Max(0, Math.Min(KuerKommandos, treffer)), 0.0, 100.0);

            public static double Gesamt(double pflege, double stil, double kuer) =>
                GewichtPflege * Clamp(pflege, 0.0, 100.0) + GewichtStil * Clamp(stil, 0.0, 100.0) + GewichtKuer * Clamp(kuer, 0.0, 100.0);

            public static int Sterne(double punkte)
            {
                for (var i = 0; i < SterneAb.Length; i++) if (punkte >= SterneAb[i]) return 3 - i;
                return 0;
            }
        }

        /// <summary>No. 7 barrel race: time + 5 s per barrel knocked over; ideal 24 s (wood) to 17 s (star class).</summary>
        public static class Tonnen
        {
            public const int TonnenAnzahl = 3;
            public const double TonneStrafeS = 5.0;
            public static readonly double[] SterneMult = { 1.0, 1.15, 1.35 };

            public static readonly IReadOnlyDictionary<string, double> IdealzeitS = new Dictionary<string, double>
            {
                ["holz"] = 24.0, ["bronze"] = 22.0, ["silber"] = 20.0, ["gold"] = 18.5, ["sternenklasse"] = 17.0,
            };

            public static double WertungS(double zeitS, int umgeworfen) => Math.Max(0.0, zeitS) + TonneStrafeS * Math.Max(0, umgeworfen);

            public static double Idealzeit(string klasse) => klasse != null && IdealzeitS.TryGetValue(klasse, out var s) ? s : 24.0;

            /// <summary>3 stars ≤ ideal, 2 ≤ 115 %, 1 ≤ 135 %, else 0.</summary>
            public static int Sterne(double wertung, double idealS)
            {
                if (idealS <= 0.0) return 0;
                for (var i = 0; i < SterneMult.Length; i++) if (wertung <= idealS * SterneMult[i]) return 3 - i;
                return 0;
            }
        }

        internal static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
