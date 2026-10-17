using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Obstacle course (ranchParcours) — port of parcours_logic.gd: Gooby rides a course (x grows), jumps by tap and may
    /// gallop by holding. Scored are time + clean jumps: the jump arc (RideFeel physics) has to cover the obstacle —
    /// "perfekt" near the arc middle, "gut" in the safe window, else a knock-down (time penalty + tempo loss). 10 courses
    /// from <c>minigames/ranch/parcours_kurse.json</c>.
    /// </summary>
    public static class RanchParcours
    {
        public const string KursePath = "minigames/ranch/parcours_kurse";
        public const int KursAnzahl = 10;

        public sealed class Tune
        {
            public double GaloppMult = 1.3, SicherVon = 0.18, SicherBis = 0.82, PerfektM = 0.55, AbwurfStrafeS = 2.5, AbwurfTempoMult = 0.6,
                ZeitbonusProS = 4.0, Toleranz = 1.0;
            public int PunktePerfekt = 15, PunkteGut = 10, KomboBonus = 2, KomboDeckel = 10;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Base = new Tune();

        /// <summary>Span (m) the arc has to cover safely, per obstacle type.</summary>
        public static readonly IReadOnlyDictionary<string, double> HindernisBreite = new Dictionary<string, double>
        {
            ["zaun"] = 0.4, ["hecke"] = 0.9, ["oxer"] = 1.2, ["wasser"] = 1.6,
        };

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double tol;
            switch (mode)
            {
                case "easy": tol = 1.25; break;
                case "hard":
                case "endless": tol = 0.8; break;
                default: return tune;
            }
            var t = tune.Clone();
            t.Toleranz = tol;
            t.Mode = mode;
            return t;
        }

        public static List<JObject> LoadKurse()
        {
            var list = new List<JObject>();
            if (ContentSource.LoadObject(KursePath)["kurse"] is JArray arr)
                foreach (var t in arr) if (t is JObject o) list.Add((JObject)o.DeepClone());
            return list;
        }

        public static JObject KursById(IEnumerable<JObject> kurse, int id)
        {
            foreach (var k in kurse) if ((int)RanchHerde.Num(k, "id", 0) == id) return k;
            return null;
        }

        public static List<string> ValidateKurse(IList<JObject> kurse)
        {
            var errors = new List<string>();
            if (kurse.Count != KursAnzahl) errors.Add($"erwartet {KursAnzahl} Kurse, gefunden {kurse.Count}");
            foreach (var k in kurse)
            {
                var id = (int)RanchHerde.Num(k, "id", 0);
                var weite = RideFeel.WeiteM(RanchHerde.Num(k, "tempo", 0.0));
                var vorher = double.NegativeInfinity;
                foreach (var h in Hindernisse(k))
                {
                    if (!HindernisBreite.ContainsKey(h.typ)) errors.Add($"Kurs {id}: unbekannter Typ {h.typ}");
                    if (h.at - vorher < weite * 1.2) errors.Add($"Kurs {id}: Hindernis bei {h.at:0.0} zu dicht");
                    if ((HindernisBreite.TryGetValue(h.typ, out var b) ? b : 0.0) > weite * 0.64) errors.Add($"Kurs {id}: {h.typ} passt nicht in den Bogen");
                    vorher = h.at;
                    if (h.at <= 0.0 || h.at >= RanchHerde.Num(k, "laenge_m", 0.0) - 4.0) errors.Add($"Kurs {id}: Hindernis bei {h.at:0.0} außerhalb");
                }
            }
            return errors;
        }

        public static List<(double at, string typ)> Hindernisse(JObject kurs)
        {
            var list = new List<(double, string)>();
            foreach (var t in kurs?["hindernisse"] as JArray ?? new JArray())
                if (t is JObject h) list.Add((RanchHerde.Num(h, "at", 0.0), h["typ"]?.ToString() ?? "zaun"));
            return list;
        }

        public readonly struct Wertung
        {
            public readonly string Qualitaet;
            public readonly double FehlerM;
            public Wertung(string q, double f) { Qualitaet = q; FehlerM = f; }
        }

        /// <summary>Rate a jump: takeoff at x0 with tempo v against an obstacle (centre, type).</summary>
        public static Wertung BewerteSprung(double x0, double tempo, double mitte, string typ, Tune tune)
        {
            var weite = RideFeel.WeiteM(tempo);
            var toleranz = tune.Toleranz;
            var breite = HindernisBreite.TryGetValue(typ, out var b) ? b : 0.4;
            var von = x0 + weite * tune.SicherVon / toleranz;
            var bis = x0 + weite * (1.0 - (1.0 - tune.SicherBis) / toleranz);
            var fehlerM = Math.Abs(mitte - (x0 + weite * 0.5));
            if (mitte - breite * 0.5 < von || mitte + breite * 0.5 > bis) return new Wertung("abwurf", fehlerM);
            if (fehlerM <= tune.PerfektM * toleranz) return new Wertung("perfekt", fehlerM);
            return new Wertung("gut", fehlerM);
        }

        public static int SprungPunkte(string qualitaet, int kombo, Tune tune)
        {
            switch (qualitaet)
            {
                case "perfekt": return tune.PunktePerfekt + Math.Min(tune.KomboDeckel, kombo * tune.KomboBonus);
                case "gut": return tune.PunkteGut + Math.Min(tune.KomboDeckel, kombo * tune.KomboBonus);
                default: return 0;
            }
        }

        public static int Zeitbonus(double parS, double zeitS, Tune tune) =>
            Math.Max(0, (int)Math.Round(Math.Max(0.0, parS - zeitS) * tune.ZeitbonusProS, MidpointRounding.AwayFromZero));

        public static int KursScore(int sprungPunkteSumme, int bonus, bool firstClear, int kursId) =>
            sprungPunkteSumme + bonus + kursId * 3 + (firstClear ? 25 : 0);

        /// <summary>Stars: 3 = clean AND under par, 2 = at most one knock-down, else 1.</summary>
        public static int Sterne(int abwuerfe, double zeitS, double parS)
        {
            if (abwuerfe == 0 && zeitS <= parS) return 3;
            return abwuerfe <= 1 ? 2 : 1;
        }

        public static double LaufTempo(double basis, bool galopp, Tune tune) => basis * (galopp ? tune.GaloppMult : 1.0);

        public sealed class SimResult
        {
            public int Score, Abwuerfe, Sterne;
            public double ZeitS;
            public override string ToString() => Score + "|" + Abwuerfe + "|" + Sterne + "|" + ZeitS.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Deterministic bot certification: jumps at the ideal takeoff with seeded error (σ per mode), gallops on free stretches.</summary>
        public static SimResult SimulateLauf(JObject kurs, long seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Base, mode);
            var rng = new GoobyRng(seed + (long)RanchHerde.Num(kurs, "id", 0) * 101);
            var sigma = 0.7;
            var galoppAb = 14.0;
            if (mode == "easy") { sigma = 0.45; galoppAb = 13.0; }
            else if (mode == "hard" || mode == "endless") { sigma = 1.05; galoppAb = 18.0; }
            var basis = RanchHerde.Num(kurs, "tempo", 6.0);
            var laenge = RanchHerde.Num(kurs, "laenge_m", 200.0);
            int punkte = 0, abwuerfe = 0, kombo = 0;
            double zeit = 0.0, cursor = 0.0;
            foreach (var (mitte, typ) in Hindernisse(kurs))
            {
                var frei = mitte - cursor;
                var galopp = frei > galoppAb;
                var tempo = LaufTempo(basis, galopp, tune);
                var weite = RideFeel.WeiteM(tempo);
                var ideal = mitte - weite * 0.5;
                var streuung = (rng.Next() * 2.0 - 1.0) * sigma;
                var wertung = BewerteSprung(ideal + streuung, tempo, mitte, typ, tune);
                zeit += frei / tempo;
                if (wertung.Qualitaet == "abwurf")
                {
                    abwuerfe++;
                    kombo = 0;
                    zeit += tune.AbwurfStrafeS;
                }
                else
                {
                    punkte += SprungPunkte(wertung.Qualitaet, kombo, tune);
                    kombo++;
                }
                cursor = mitte;
            }
            zeit += Math.Max(0.0, laenge - cursor) / LaufTempo(basis, true, tune);
            var par = RanchHerde.Num(kurs, "par_s", 60.0);
            var bonus = Zeitbonus(par, zeit, tune);
            return new SimResult
            {
                Score = KursScore(punkte, bonus, false, (int)RanchHerde.Num(kurs, "id", 0)),
                Abwuerfe = abwuerfe,
                ZeitS = zeit,
                Sterne = Sterne(abwuerfe, zeit, par),
            };
        }
    }
}
