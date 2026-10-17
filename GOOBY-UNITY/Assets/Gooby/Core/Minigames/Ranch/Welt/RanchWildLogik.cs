using System;
using System.Collections.Generic;
using Gooby.Core.Life;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The region's wild animals (port of ranch_wild_logik.gd): deer, foxes, hares, ducks and wild horses graze, drink,
    /// rest and wander in small groups near their home spot, flee from the rider and — the fox — come a little closer out
    /// of curiosity. The hour and the weather decide who is out (deer and hares hide from rain); swarms (birds,
    /// butterflies, fireflies) only switch on and off. "Less life" halves the groups and drops the swarms. Pure: the
    /// caller rolls the next activity.
    /// </summary>
    public static class RanchWildLogik
    {
        public sealed class Art
        {
            /// <summary>Hours out and about (from > to wraps over midnight).</summary>
            public (double von, double bis)[] Aktiv;
            public double FluchtM, NeugierM, Tempo, HeimRadius;
            public int Gruppe;
            public bool RegenScheu;
        }

        public static readonly IReadOnlyDictionary<string, Art> Arten = new Dictionary<string, Art>
        {
            ["reh"] = new Art { Aktiv = new[] { (5.0, 11.0), (15.0, 23.0) }, FluchtM = 26.0, NeugierM = 0.0, Tempo = 3.2, Gruppe = 3, HeimRadius = 55.0, RegenScheu = true },
            ["fuchs"] = new Art { Aktiv = new[] { (20.0, 5.5) }, FluchtM = 11.0, NeugierM = 30.0, Tempo = 2.8, Gruppe = 2, HeimRadius = 60.0, RegenScheu = false },
            ["hase"] = new Art { Aktiv = new[] { (6.0, 21.5) }, FluchtM = 18.0, NeugierM = 0.0, Tempo = 4.2, Gruppe = 4, HeimRadius = 45.0, RegenScheu = true },
            ["ente"] = new Art { Aktiv = new[] { (0.0, 24.0) }, FluchtM = 9.0, NeugierM = 0.0, Tempo = 1.6, Gruppe = 5, HeimRadius = 40.0, RegenScheu = false },
            ["wildpferd"] = new Art { Aktiv = new[] { (6.0, 22.0) }, FluchtM = 40.0, NeugierM = 0.0, Tempo = 5.0, Gruppe = 4, HeimRadius = 70.0, RegenScheu = false },
        };

        /// <summary>The swarms: no behaviour of their own, only on/off and a budget.</summary>
        public static readonly IReadOnlyDictionary<string, int> SchwarmBudget = new Dictionary<string, int>
        {
            ["vogel"] = 22, ["schmetterling"] = 14, ["gluehwuermchen"] = 36,
        };

        /// <summary>Fleeing runs this much faster than the normal pace.</summary>
        public const double FluchtFaktor = 2.2;
        /// <summary>Curiosity stops this far outside the flight distance.</summary>
        public const double NeugierStopM = 5.0;

        /// <summary>Animals per kind in the scene; less life halves the groups (never below one) and drops the swarms.</summary>
        public static Dictionary<string, int> Budget(bool lebenReduziert)
        {
            var budget = new Dictionary<string, int>();
            foreach (var kv in Arten) budget[kv.Key] = lebenReduziert ? Math.Max(1, kv.Value.Gruppe / 2) : kv.Value.Gruppe;
            foreach (var kv in SchwarmBudget) budget[kv.Key] = lebenReduziert ? 0 : kv.Value;
            return budget;
        }

        /// <summary>Is this kind out at this hour in this weather?</summary>
        public static bool Aktiv(string art, double stunde, string wetterTyp)
        {
            var h = ((stunde % 24.0) + 24.0) % 24.0;
            var nass = Array.IndexOf(RanchWetter.RegenTypen, wetterTyp) >= 0;
            switch (art)
            {
                case "vogel": return h >= 6.0 && h < 20.0 && !nass;
                case "schmetterling": return h >= 9.0 && h < 18.5 && (wetterTyp == "sonne" || wetterTyp == "wolken");
                case "gluehwuermchen": return (h >= 21.0 || h < 4.5) && wetterTyp != "regen" && wetterTyp != "gewitter";
            }
            if (art == null || !Arten.TryGetValue(art, out var daten)) return false;
            if (daten.RegenScheu && (wetterTyp == "regen" || wetterTyp == "gewitter")) return false;
            foreach (var (von, bis) in daten.Aktiv)
            {
                if (von <= bis ? h >= von && h < bis : h >= von || h < bis) return true;
            }
            return false;
        }

        public sealed class Tier
        {
            public string Art;
            public V2d Pos, Heim, Ziel;
            /// <summary>ruhen | fressen | trinken | wandern | flucht | neugier</summary>
            public string Zustand = "ruhen";
            public double Timer, Phase;
        }

        public static Tier Neu(string art, V2d heim, double phase = 0.0) =>
            new Tier { Art = art, Pos = heim, Heim = heim, Ziel = heim, Zustand = "ruhen", Timer = 1.0 + phase * 3.0, Phase = phase };

        /// <summary>One behaviour step: state and movement. <paramref name="reiter"/> is the rider, <paramref name="roll"/> in [0, 1) picks the next activity.</summary>
        public static Tier Schritt(Tier tier, double dt, V2d reiter, double roll)
        {
            var daten = Arten[tier.Art];
            var abstand = tier.Pos.Abstand(reiter);
            if (abstand < daten.FluchtM)
            {
                tier.Zustand = "flucht";
                var weg = abstand > 0.01 ? Norm(tier.Pos - reiter) : new V2d(1.0, 0.0);
                tier.Ziel = HeimKlammer(tier.Pos + weg * 34.0, tier, daten);
                tier.Timer = 1.2;
            }
            else if (daten.NeugierM > 0.0 && abstand < daten.NeugierM && tier.Zustand != "flucht")
            {
                tier.Zustand = "neugier";
                var stop = daten.FluchtM + NeugierStopM;
                tier.Ziel = abstand > stop ? tier.Pos + Norm(reiter - tier.Pos) * (abstand - stop) : tier.Pos;
            }
            else
            {
                tier.Timer -= dt;
                if (tier.Timer <= 0.0) NaechsteBeschaeftigung(tier, daten, roll);
            }
            return Bewege(tier, daten, dt);
        }

        /// <summary>Is the animal walking right now (for the step animation)?</summary>
        public static bool Laeuft(Tier tier) => tier.Pos.Abstand(tier.Ziel) > 0.25;

        /// <summary>Grazing (often), drinking and wandering (sometimes), resting (rarely) — small goals round the home spot.</summary>
        private static void NaechsteBeschaeftigung(Tier tier, Art daten, double roll)
        {
            var radius = daten.HeimRadius;
            var winkel = roll * Math.PI * 2.0 * 7.31;
            var richtung = new V2d(Math.Cos(winkel), Math.Sin(winkel));
            if (roll < 0.5)
            {
                tier.Zustand = "fressen";
                tier.Ziel = tier.Heim + richtung * (radius * 0.35 * (0.3 + roll));
                tier.Timer = 4.0 + roll * 5.0;
            }
            else if (roll < 0.7)
            {
                tier.Zustand = "trinken";
                tier.Ziel = tier.Heim + richtung * (radius * 0.2);
                tier.Timer = 3.0 + roll * 3.0;
            }
            else if (roll < 0.9)
            {
                tier.Zustand = "wandern";
                tier.Ziel = tier.Heim + richtung * (radius * (0.4 + roll * 0.6));
                tier.Timer = 5.0 + roll * 4.0;
            }
            else
            {
                tier.Zustand = "ruhen";
                tier.Ziel = tier.Pos;
                tier.Timer = 6.0 + roll * 6.0;
            }
        }

        private static Tier Bewege(Tier tier, Art daten, double dt)
        {
            var abstand = tier.Pos.Abstand(tier.Ziel);
            if (abstand < 0.2)
            {
                if (tier.Zustand == "flucht")
                {
                    tier.Zustand = "ruhen";
                    tier.Timer = 1.0;
                }
                return tier;
            }
            var tempo = daten.Tempo;
            if (tier.Zustand == "flucht") tempo *= FluchtFaktor;
            else if (tier.Zustand == "fressen" || tier.Zustand == "trinken") tempo *= 0.45;
            tier.Pos = tier.Pos + Norm(tier.Ziel - tier.Pos) * Math.Min(tempo * dt, abstand);
            return tier;
        }

        private static V2d HeimKlammer(V2d ziel, Tier tier, Art daten)
        {
            var radius = daten.HeimRadius * 1.4;
            if (tier.Heim.Abstand(ziel) <= radius) return ziel;
            return tier.Heim + Norm(ziel - tier.Heim) * radius;
        }

        private static V2d Norm(V2d v)
        {
            var l = Math.Sqrt(v.X * v.X + v.Z * v.Z);
            return l > 1e-9 ? new V2d(v.X / l, v.Z / l) : new V2d(0.0, 0.0);
        }
    }
}
