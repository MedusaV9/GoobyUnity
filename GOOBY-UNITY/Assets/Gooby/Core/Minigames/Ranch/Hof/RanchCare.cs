using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Horse care and bond (port of horse_care.gd): every horse has three decaying values (hunger, thirst and
    /// cleanliness, 0..100), a mood derived from them and a lasting bond that grows with care under a daily cap and only
    /// fades after two days without any. Pure: values in, new values out (JObject {hunger, durst, sauberkeit}).
    /// </summary>
    public static class RanchCare
    {
        public const double Min = 0.0, Max = 100.0;
        public static readonly string[] Keys = { "hunger", "durst", "sauberkeit" };

        /// <summary>Decay per real minute on the pasture (day, awake) and in the stable (night, asleep: less hunger, more dirt).</summary>
        private static readonly double[] RatesWach = { -0.25, -0.4, -0.15 };
        private static readonly double[] RatesSchlaf = { -0.08, -0.15, -0.2 };

        public const double StallDreckigUnter = 40.0, StallSauberkeitFaktor = 2.0;
        public const double Niedrig = 25.0, Kritisch = 10.0;
        public const double DurstigAb = 15.0, DurstigLauneDeckel = 39.0;
        public const double LauneMinGewicht = 0.35, LauneAvgGewicht = 0.65, LauneBindungSpanne = 5.0;
        public const double BondTagesDeckel = 12.0, BondVerfallKarenzMin = 2880.0, BondVerfallProMin = 1.0 / 1440.0;
        public const double StallMax = 100.0, StallVerfallProMin = 0.05;

        private static readonly (string id, double min)[] LauneBaender =
            { ("strahlend", 80.0), ("froh", 60.0), ("zufrieden", 40.0), ("muffig", 25.0), ("elend", 0.0) };

        private static readonly (string id, double min)[] BindungStufen =
            { ("seelenpferd", 90.0), ("vertraut", 70.0), ("freund", 45.0), ("bekannt", 20.0), ("fremd", 0.0) };

        /// <summary>
        /// Hay fills up, fruit and veg bring joy (and bond); deltas in <see cref="Keys"/> order. Oats and treats from the
        /// Hufingen feed yard are a Unity addition (Godot sold them without a use): oats fill more than hay, a treat is
        /// small but bonds like brushing.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, double[]> Futter = new Dictionary<string, double[]>
        {
            ["heu"] = new[] { 30.0, 0.0, 0.0 },
            ["apfel"] = new[] { 12.0, 4.0, 0.0 },
            ["karotte"] = new[] { 8.0, 2.0, 0.0 },
            ["hafer"] = new[] { 40.0, 0.0, 0.0 },
            ["leckerli"] = new[] { 6.0, 0.0, 0.0 },
        };

        private static readonly double[] TraenkenDelta = { 0.0, 45.0, 0.0 };
        /// <summary>Cleanliness one brushing gives (the wash place raises it, RanchBauEffekte.StriegelWert).</summary>
        public const double StriegelBasis = 35.0;

        public static readonly IReadOnlyDictionary<string, double> BondBasis = new Dictionary<string, double>
        {
            ["heu"] = 1.0, ["apfel"] = 2.0, ["karotte"] = 2.0, ["hafer"] = 2.0, ["leckerli"] = 3.0, ["traenken"] = 1.0, ["striegeln"] = 3.0,
            ["ausmisten"] = 2.0, ["reiten"] = 4.0,
        };

        /// <summary>One value into [0, 100]; NaN, infinities and non-numbers fall to 0.</summary>
        public static double ClampWert(JToken value) => ClampWert(J.NumStats(value));

        public static double ClampWert(double n) => double.IsNaN(n) || double.IsInfinity(n) ? Min : Math.Min(Max, Math.Max(Min, n));

        /// <summary>All three values clamped (missing ones → 0) into a new object.</summary>
        public static JObject ClampWerte(JObject werte)
        {
            var o = new JObject();
            foreach (var k in Keys)
            {
                var n = J.NumStats(werte?[k]);
                o[k] = ClampWert(double.IsNaN(n) ? 0.0 : n);
            }
            return o;
        }

        /// <summary>
        /// One decay step over <paramref name="dtMin"/> minutes. <paramref name="rateMult"/> 0.3 is the offline pasture;
        /// a dirty stable (&lt; 40) doubles the dirt only while asleep in it, the pasture fence only helps while awake, and
        /// so do the pasture's lush grass (hunger) and the water place (thirst).
        /// </summary>
        public static JObject Tick(JObject werte, double dtMin, bool schlaeft = false, double rateMult = 1.0, double stallSauberkeit = StallMax, double sauberkeitMult = 1.0,
            double hungerMult = 1.0, double durstMult = 1.0)
        {
            var rates = schlaeft ? RatesSchlaf : RatesWach;
            var o = new JObject();
            for (var i = 0; i < Keys.Length; i++)
            {
                var rate = rates[i];
                if (Keys[i] == "sauberkeit")
                {
                    if (schlaeft && stallSauberkeit < StallDreckigUnter) rate *= StallSauberkeitFaktor;
                    else if (!schlaeft) rate *= sauberkeitMult;
                }
                else if (!schlaeft) rate *= Keys[i] == "hunger" ? hungerMult : durstMult;
                o[Keys[i]] = ClampWert(J.NumStats(werte?[Keys[i]]) + rate * dtMin * rateMult);
            }
            return o;
        }

        /// <summary>Mood 0..100: 0.35·lowest + 0.65·average, the bond pushes ±5 around 50, very thirsty (≤ 15) caps it at 39.</summary>
        public static double Laune(JObject werte, double bindung = 50.0)
        {
            var lowest = double.PositiveInfinity;
            var sum = 0.0;
            foreach (var k in Keys)
            {
                var v = J.NumStats(werte?[k]);
                lowest = Math.Min(lowest, v);
                sum += v;
            }
            var m = LauneMinGewicht * lowest + LauneAvgGewicht * (sum / Keys.Length);
            m += (ClampWert(bindung) - 50.0) / 50.0 * LauneBindungSpanne;
            if (J.NumStats(werte?["durst"]) <= DurstigAb) m = Math.Min(m, DurstigLauneDeckel);
            return Math.Min(Max, Math.Max(Min, m));
        }

        public static string LauneBand(double laune)
        {
            foreach (var (id, min) in LauneBaender)
                if (laune >= min) return id;
            return "elend";
        }

        public static string BindungStufe(double bindung)
        {
            foreach (var (id, min) in BindungStufen)
                if (bindung >= min) return id;
            return "fremd";
        }

        public static bool IstNiedrig(double value) => value < Niedrig;

        public static bool IstKritisch(double value) => value < Kritisch;

        /// <summary>Feeding hay, an apple or a carrot; unknown food leaves the values as they are (clamped copy).</summary>
        public static JObject Fuettern(JObject werte, string futterId) =>
            futterId != null && Futter.TryGetValue(futterId, out var d) ? ApplyDeltas(werte, d) : ClampWerte(werte);

        public static JObject Traenken(JObject werte) => ApplyDeltas(werte, TraenkenDelta);

        public static JObject Striegeln(JObject werte, double wert = StriegelBasis) => ApplyDeltas(werte, new[] { 0.0, 0.0, wert });

        public static JObject ApplyDeltas(JObject werte, double[] deltas)
        {
            var o = new JObject();
            for (var i = 0; i < Keys.Length; i++) o[Keys[i]] = ClampWert(J.NumStats(werte?[Keys[i]]) + deltas[i]);
            return o;
        }

        /// <summary>The bond an action earns today: its base, but never past the daily cap of 12.</summary>
        public static double BondGewinn(string aktion, double bondHeute)
        {
            var basis = aktion != null && BondBasis.TryGetValue(aktion, out var b) ? b : 0.0;
            var rest = Math.Max(0.0, BondTagesDeckel - Math.Max(0.0, bondHeute));
            return Math.Min(basis, rest);
        }

        public readonly struct Bond
        {
            public readonly double Bindung, BondHeute, Gewinn;
            public Bond(double bindung, double bondHeute, double gewinn) { Bindung = bindung; BondHeute = bondHeute; Gewinn = gewinn; }
        }

        /// <summary>The bond after a care action; a new day resets today's tally before booking.</summary>
        public static Bond BondNachAktion(double bindung, double bondHeute, string aktion, bool tagGewechselt = false)
        {
            var heute = tagGewechselt ? 0.0 : Math.Max(0.0, bondHeute);
            var gewinn = BondGewinn(aktion, heute);
            return new Bond(ClampWert(bindung + gewinn), heute + gewinn, gewinn);
        }

        /// <summary>Bond fading over <paramref name="dtMin"/> minutes without care: 48 h grace, then one point a day.</summary>
        public static double BondVerfall(double bindung, double seitPflegeMin, double dtMin)
        {
            var start = Math.Max(0.0, seitPflegeMin);
            var ende = start + Math.Max(0.0, dtMin);
            var strafMin = Math.Max(0.0, ende - BondVerfallKarenzMin) - Math.Max(0.0, start - BondVerfallKarenzMin);
            return ClampWert(bindung - strafMin * BondVerfallProMin);
        }

        /// <summary>The stable gets dirty while horses sleep in it.</summary>
        public static double StallTick(double stallSauberkeit, double dtMin, int pferdeImStall) =>
            pferdeImStall <= 0 ? ClampWert(stallSauberkeit) : ClampWert(stallSauberkeit - StallVerfallProMin * dtMin);

        public static double Ausmisten() => StallMax;

        /// <summary>Riding perks of a strong bond: up to +6 % gallop and +25 % stamina regeneration, linear from 45.</summary>
        public static (double tempoMult, double ausdauerRegenMult) ReitPerks(double bindung)
        {
            var f = Math.Max(0.0, (ClampWert(bindung) - 45.0) / 55.0);
            return (1.0 + 0.06 * f, 1.0 + 0.25 * f);
        }
    }
}
