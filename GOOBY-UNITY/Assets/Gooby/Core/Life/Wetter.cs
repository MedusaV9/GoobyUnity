using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Random;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Weather of a region — port of scripts/ranch/wetter/ranch_wetter.gd. Offline-first: the whole day plan comes
    /// deterministically from <b>date + seed</b> (no server, no real-time chance), the same inputs give the same day on every
    /// device. <see cref="ZustandBei"/> is the moment with soft transitions (blend), ground wetness (puddles after rain), the
    /// evening rainbow and light/fog factors. The day plan draws from mulberry32 (<see cref="GoobyRng"/>) seeded with a
    /// stable FNV-1a hash of "date|seed" — Godot used its PCG32 and String.hash, so the Unity days differ from Godot's
    /// (weather is computed, never saved) while keeping every rule of the plan.
    /// </summary>
    public static class RanchWetter
    {
        /// <summary>All weather types (display order).</summary>
        public static readonly string[] Typen = { "sonne", "wolken", "niesel", "regen", "gewitter", "nebel" };
        /// <summary>Raining types (wetness, puddles, ambience).</summary>
        public static readonly string[] RegenTypen = { "niesel", "regen", "gewitter" };
        /// <summary>Transition between two segments in hours (10 min).</summary>
        public const double UebergangH = 10.0 / 60.0;
        public const double NassAufbauH = 0.33, NassTrocknenH = 2.0;

        private static readonly Dictionary<string, double> Gewichte = new Dictionary<string, double>
        {
            ["sonne"] = 32.0, ["wolken"] = 24.0, ["niesel"] = 10.0, ["regen"] = 13.0, ["gewitter"] = 7.0, ["nebel"] = 14.0,
        };

        private static readonly Dictionary<string, double> BewoelkungJeTyp = new Dictionary<string, double>
        {
            ["sonne"] = 0.12, ["wolken"] = 0.62, ["niesel"] = 0.78, ["regen"] = 0.9, ["gewitter"] = 1.0, ["nebel"] = 0.7,
        };

        private static readonly Dictionary<string, double> LichtJeTyp = new Dictionary<string, double>
        {
            ["sonne"] = 1.0, ["wolken"] = 0.78, ["niesel"] = 0.58, ["regen"] = 0.45, ["gewitter"] = 0.3, ["nebel"] = 0.55,
        };

        public sealed class Segment
        {
            public string Typ;
            public double Von, Bis, Intensitaet, Wind;
            public override string ToString() => Typ + "@" + Von.ToString("0.00") + "-" + Bis.ToString("0.00");
        }

        public sealed class Zustand
        {
            public string Typ, Vorher, NameKey;
            public double Blend, Intensitaet, Wind, Bewoelkung, Naesse, LichtFaktor, NebelDichte;
            public bool Regenbogen;
            public bool Regnet => Array.IndexOf(RegenTypen, Typ) >= 0;
        }

        /// <summary>Deterministic day plan: segments over 24 h (2,5–6 h each, never the same type twice in a row).</summary>
        public static List<Segment> Tagesplan(string datum, long seed)
        {
            var rng = new GoobyRng(TagesSeed(datum, seed));
            double Range(double a, double b) => a + rng.Next() * (b - a);
            var plan = new List<Segment>();
            var stunde = 0.0;
            var vorher = "";
            while (stunde < 24.0)
            {
                var dauer = Range(2.5, 6.0);
                var typ = WuerfleTyp(rng, stunde, vorher);
                var windMin = typ == "gewitter" ? 0.55 : 0.05;
                plan.Add(new Segment
                {
                    Typ = typ,
                    Von = stunde,
                    Bis = Math.Min(stunde + dauer, 24.0),
                    Intensitaet = Range(0.5, 1.0),
                    Wind = Range(windMin, typ != "sonne" ? 1.0 : 0.5),
                });
                vorher = typ;
                stunde += dauer;
            }
            return plan;
        }

        /// <summary>The moment at date + hour (0..24, fractions ok).</summary>
        public static Zustand ZustandBei(string datum, double stunde, long seed)
        {
            var plan = Tagesplan(datum, seed);
            var h = ((stunde % 24.0) + 24.0) % 24.0;
            var index = SegmentIndex(plan, h);
            var segment = plan[index];
            var vorher = plan[Math.Max(0, index - 1)];
            var blend = index == 0 ? 1.0 : Clamp01((h - segment.Von) / UebergangH);
            var naesse = Naesse(plan, h);
            return new Zustand
            {
                Typ = segment.Typ,
                Vorher = vorher.Typ,
                Blend = blend,
                Intensitaet = Lerp(vorher.Intensitaet, segment.Intensitaet, blend),
                Wind = Lerp(vorher.Wind, segment.Wind, blend),
                Bewoelkung = Lerp(BewoelkungJeTyp[vorher.Typ], BewoelkungJeTyp[segment.Typ], blend),
                Naesse = naesse,
                Regenbogen = IstRegenbogen(segment.Typ, naesse, h),
                LichtFaktor = Lerp(LichtJeTyp[vorher.Typ], LichtJeTyp[segment.Typ], blend),
                NebelDichte = Lerp(Nebel(vorher), Nebel(segment), blend),
                NameKey = "rwelt.wetter." + segment.Typ,
            };
        }

        /// <summary>Ground wetness 0..1: builds up in the rain and dries in ~2 h afterwards.</summary>
        public static double Naesse(IList<Segment> plan, double stunde)
        {
            var wert = 0.0;
            foreach (var segment in plan)
            {
                if (Array.IndexOf(RegenTypen, segment.Typ) < 0 || stunde < segment.Von) continue;
                var staerke = segment.Intensitaet * (segment.Typ == "niesel" ? 0.5 : 1.0);
                if (stunde <= segment.Bis) wert = Math.Max(wert, staerke * Clamp01((stunde - segment.Von) / NassAufbauH));
                else wert = Math.Max(wert, staerke * Math.Max(0.0, 1.0 - (stunde - segment.Bis) / NassTrocknenH));
            }
            return Clamp01(wert);
        }

        /// <summary>Evening rainbow: a clear sky, still wet ground, 16:00–20:30.</summary>
        public static bool IstRegenbogen(string typ, double naesse, double stunde) =>
            (typ == "sonne" || typ == "wolken") && naesse > 0.15 && stunde >= 16.0 && stunde <= 20.5;

        /// <summary>Continuous gust 0..~1,3 for shaders/particles (time in s).</summary>
        public static double Boe(double zeitS, double wind)
        {
            var puls = 0.72 + 0.2 * Math.Sin(zeitS * 0.9) + 0.08 * Math.Sin(zeitS * 2.7 + 1.3);
            return Math.Max(0.0, wind * puls);
        }

        /// <summary>Stable seed of a calendar day ("YYYY-MM-DD") + world seed (FNV-1a over "date|seed", 31 bits).</summary>
        public static long TagesSeed(string datum, long seed)
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var c in (datum ?? "") + "|" + seed)
                {
                    h ^= c;
                    h *= 16777619u;
                }
                return h & 0x7FFFFFFF;
            }
        }

        /// <summary>Light factor of a weather type (1 = clear sun).</summary>
        public static double LichtVon(string typ) => typ != null && LichtJeTyp.TryGetValue(typ, out var v) ? v : 1.0;

        /// <summary>Cloud cover of a weather type (0..1).</summary>
        public static double BewoelkungVon(string typ) => typ != null && BewoelkungJeTyp.TryGetValue(typ, out var v) ? v : 0.0;

        private static string WuerfleTyp(GoobyRng rng, double stunde, string vorher)
        {
            var summe = Typen.Sum(t => Gewicht(t, stunde, vorher));
            var wurf = rng.Next() * summe;
            foreach (var typ in Typen)
            {
                wurf -= Gewicht(typ, stunde, vorher);
                if (wurf <= 0.0) return typ;
            }
            return "sonne";
        }

        /// <summary>Weights by time of day: fog in the morning/late evening, thunder only 13–22 h; never the same type twice.</summary>
        private static double Gewicht(string typ, double stunde, string vorher)
        {
            if (typ == vorher) return 0.0;
            var g = Gewichte[typ];
            if (typ == "nebel") g *= stunde < 9.0 || stunde >= 21.0 ? 2.2 : 0.35;
            if (typ == "gewitter") g *= stunde >= 13.0 && stunde < 22.0 ? 1.8 : 0.0;
            return g;
        }

        private static int SegmentIndex(IList<Segment> plan, double stunde)
        {
            for (var i = 0; i < plan.Count; i++) if (stunde < plan[i].Bis) return i;
            return plan.Count - 1;
        }

        private static double Nebel(Segment s)
        {
            switch (s.Typ)
            {
                case "nebel": return 0.5 + 0.5 * s.Intensitaet;
                case "regen": return 0.18;
                case "gewitter": return 0.26;
                case "niesel": return 0.12;
                default: return 0.0;
            }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }

    /// <summary>
    /// Home weather (port of scripts/soul/soul_wetter.gd): a deterministic adapter onto the region plan with its own seed —
    /// independent of the ranch, stable per day. In December, January and February the rain becomes SNOW.
    /// </summary>
    public static class SoulWetter
    {
        public const long HomeSeed = 47_110_815;
        public static readonly int[] WinterMonate = { 12, 1, 2 };

        public sealed class Lage
        {
            /// <summary>sonne | wolken | niesel | regen | gewitter | nebel | schnee</summary>
            public string Typ;
            public bool Regen, Schnee;
            public RanchWetter.Zustand Basis;
        }

        /// <summary>The home weather at a local date ("YYYY-MM-DD") and hour.</summary>
        public static Lage Zustand(string datum, double stunde)
        {
            var basis = RanchWetter.ZustandBei(datum, stunde, HomeSeed);
            var typ = basis.Typ;
            var regen = Array.IndexOf(RanchWetter.RegenTypen, typ) >= 0;
            var schnee = regen && IstWinter(datum);
            if (schnee)
            {
                typ = "schnee";
                regen = false;
            }
            return new Lage { Typ = typ, Regen = regen, Schnee = schnee, Basis = basis };
        }

        public static bool IstWinter(string datum) => Array.IndexOf(WinterMonate, MonatVon(datum)) >= 0;

        /// <summary>Month of "YYYY-MM-DD" (0 for a broken format — never crash).</summary>
        public static int MonatVon(string datum)
        {
            var parts = (datum ?? "").Split('-');
            return parts.Length == 3 && int.TryParse(parts[1], out var m) ? m : 0;
        }

        /// <summary>GoobyTicker's weather hook: rain or snow outside makes a clammy Gooby freeze.</summary>
        public sealed class Oracle : IWeatherOracle
        {
            public bool IsWetOutside(string localDate, double localHour)
            {
                var lage = Zustand(localDate, localHour);
                return lage.Regen || lage.Schnee;
            }
        }
    }

    /// <summary>
    /// What a scene shows for the weather — port of wetter_fx.gd <c>fx_plan</c> and city_ambiente.gd
    /// <c>wetter_licht_profil</c> (pure): rain streaks (with splash rings outdoors), snow, lightning, a strength from the
    /// segment's intensity (drizzle a quarter of it), the fog density, and how the light dims under the clouds — sun
    /// energy toward the light factor at 85 %, ambient at 75 %, the sun colour greyed by the cloud cover × 0.55. Snow reads
    /// as an overcast day, not as a rain day. Reduced motion keeps only the static fog.
    /// </summary>
    public static class WetterFx
    {
        public sealed class Plan
        {
            public string Typ = "sonne";
            public bool Regen, Spritzer, Schnee, Blitz;
            public double Staerke, Nebel, Grau;
            public double SonnenFaktor = 1.0, AmbientFaktor = 1.0;
            public bool Nass => Regen || Schnee;
        }

        public static Plan Fuer(SoulWetter.Lage lage, bool indoor, bool reduced)
        {
            if (lage == null) return new Plan();
            var basis = lage.Basis;
            var typ = lage.Typ ?? "sonne";
            var intensitaet = Clamp(basis?.Intensitaet ?? 0.8, 0.0, 1.0);
            var licht = lage.Schnee ? RanchWetter.LichtVon("wolken") : basis?.LichtFaktor ?? RanchWetter.LichtVon(typ);
            var wolken = lage.Schnee ? RanchWetter.BewoelkungVon("wolken") : basis?.Bewoelkung ?? RanchWetter.BewoelkungVon(typ);
            return new Plan
            {
                Typ = typ,
                Regen = lage.Regen && !reduced,
                Spritzer = lage.Regen && !reduced && !indoor,
                Schnee = lage.Schnee && !reduced,
                Blitz = typ == "gewitter" && !reduced,
                Staerke = Clamp((typ == "niesel" ? 0.25 : 1.0) * intensitaet, 0.05, 1.0),
                Nebel = Clamp(basis?.NebelDichte ?? (typ == "nebel" ? 0.8 : 0.0), 0.0, 1.0),
                SonnenFaktor = 1.0 + (licht - 1.0) * 0.85,
                AmbientFaktor = 1.0 + (licht - 1.0) * 0.75,
                Grau = wolken * 0.55,
            };
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
