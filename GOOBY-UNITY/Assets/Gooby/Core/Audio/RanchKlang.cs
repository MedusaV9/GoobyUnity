using System;
using System.Collections.Generic;
using Gooby.Core.Life;

namespace Gooby.Core.Audio
{
    /// <summary>
    /// The sound of the ranch — the pure half of ranch_audio.gd (RW-8): the ambience mix per zone, weather and time of day
    /// (wind, rain, thunderstorm, birds, crickets, the brook), the music context of a ranch place (the tournament beats the
    /// night, the night beats riding), hoof beats by ground, horse calls by reaction and the distance fall-off.
    /// </summary>
    public static class RanchKlang
    {
        /// <summary>The ambience layers; their bed ids are "ranch_&lt;layer&gt;".</summary>
        public static readonly string[] Ebenen = { "wind", "regen", "gewitter", "voegel", "grillen", "bach" };
        public const double HoerweiteBach = 60.0, HoerweiteMenge = 40.0;
        /// <summary>Crickets instead of birds, and the night music, from 20 to 6 o'clock.</summary>
        public const double NachtAb = 20.0, NachtBis = 6.0;

        public sealed class Boden
        {
            public string Sound;
            public double VolDb, Pitch, TempoMult;
        }

        /// <summary>Ground → hoof sound, level and pitch (ride_feel.gd UNTERGRUND).</summary>
        public static readonly IReadOnlyDictionary<string, Boden> Untergruende = new Dictionary<string, Boden>
        {
            ["wiese"] = new Boden { Sound = "ranch_huf_gras", VolDb = -6.0, Pitch = 0.9, TempoMult = 1.0 },
            ["sand"] = new Boden { Sound = "ranch_huf_sand", VolDb = -4.0, Pitch = 1.0, TempoMult = 1.0 },
            ["holz"] = new Boden { Sound = "ranch_huf_holz", VolDb = 3.0, Pitch = 1.05, TempoMult = 1.0 },
            ["stein"] = new Boden { Sound = "ranch_huf_stein", VolDb = 0.0, Pitch = 1.1, TempoMult = 1.0 },
            ["wasser"] = new Boden { Sound = "ranch_huf_sand", VolDb = 0.0, Pitch = 1.0, TempoMult = 0.85 },
            ["matsch"] = new Boden { Sound = "ranch_huf_sand", VolDb = -2.0, Pitch = 0.8, TempoMult = 0.9 },
            ["schnee"] = new Boden { Sound = "ranch_huf_gras", VolDb = -9.0, Pitch = 0.85, TempoMult = 1.0 },
        };

        private static readonly Dictionary<string, string> Reaktionen = new Dictionary<string, string>
        {
            ["begruessung"] = "ranch_wiehern_a", ["freude"] = "ranch_wiehern_b", ["scheu"] = "ranch_wiehern_b",
            ["bindung"] = "ranch_schnauben_a", ["zweiter_wind"] = "ranch_schnauben_a", ["erschoepfung"] = "ranch_schnauben_b",
        };

        public static bool IstNacht(double stunde) => stunde >= NachtAb || stunde < NachtBis;

        /// <summary>Linear fall-off with distance (1 = right there, 0 = out of earshot).</summary>
        public static double EntfernungGain(double entfernung, double hoerweite) =>
            hoerweite <= 0.0 ? 0.0 : Clamp01(1.0 - Math.Max(entfernung, 0.0) / hoerweite);

        /// <summary>
        /// THE ambience mix: target gains 0..1 per layer for a zone, the weather and the hour. A water distance ≥ 0 sets the
        /// brook by metres, otherwise the zone does (the brook, the lake).
        /// </summary>
        public static Dictionary<string, double> AmbienceZiel(string zone, RanchWetter.Zustand wetter, double stunde, double wasserEntfernung = -1.0)
        {
            var typ = wetter?.Typ ?? "sonne";
            var intensitaet = Clamp01(wetter?.Intensitaet ?? 0.5);
            var windStaerke = Clamp01(wetter?.Wind ?? 0.3);
            var nacht = IstNacht(stunde);
            var regen = typ == "niesel" ? 0.35 + 0.3 * intensitaet
                : typ == "regen" ? 0.6 + 0.3 * intensitaet
                : typ == "gewitter" ? 0.8 + 0.2 * intensitaet
                : 0.0;
            var wind = 0.2 + 0.5 * windStaerke + (zone == "huegelkamm" ? 0.2 : 0.0);
            var voegel = 0.0;
            if (!nacht && regen <= 0.0 && typ != "nebel") voegel = zone == "waeldchen" || zone == "weidetal" ? 0.75 : 0.55;
            else if (!nacht && typ == "nebel") voegel = 0.2;
            var grillen = nacht && regen <= 0.0 ? 0.65 : 0.0;
            var bach = wasserEntfernung >= 0.0 ? 0.85 * EntfernungGain(wasserEntfernung, HoerweiteBach)
                : zone == "bachlauf" ? 0.85
                : zone == "see" ? 0.7
                : 0.0;
            return new Dictionary<string, double>
            {
                ["wind"] = Clamp01(wind),
                ["regen"] = Clamp01(regen),
                ["gewitter"] = typ == "gewitter" ? 0.9 : 0.0,
                ["voegel"] = Clamp01(voegel),
                ["grillen"] = Clamp01(grillen),
                ["bach"] = Clamp01(bach),
            };
        }

        /// <summary>
        /// The mix as live bed layers ("ranch_wind" …) with the layers' trims from sfx_map.gd (rain +2 dB, thunderstorm +4 dB
        /// over the −12 dB of the others).
        /// </summary>
        public static Dictionary<string, double> Betten(string zone, RanchWetter.Zustand wetter, double stunde, double wasserEntfernung = -1.0)
        {
            var plan = new Dictionary<string, double>();
            foreach (var kv in AmbienceZiel(zone, wetter, stunde, wasserEntfernung))
            {
                var trimDb = kv.Key == "regen" ? 2.0 : kv.Key == "gewitter" ? 4.0 : 0.0;
                plan["ranch_" + kv.Key] = kv.Value * Math.Pow(10.0, trimDb / 20.0);
            }
            return plan;
        }

        /// <summary>
        /// A ranch place + the hour → the music context ("" = not a ranch place: the music stays). Tournament beats night
        /// beats riding; the farm and the village play the ranch day.
        /// </summary>
        public static string MusikKontextFuer(string ziel, double stunde)
        {
            if (ziel == null || !ziel.StartsWith("ranch/", StringComparison.Ordinal)) return "";
            if (ziel.Contains("turnier")) return "ranch_turnier";
            if (IstNacht(stunde)) return "ranch_nacht";
            if (ziel == "ranch/welt" || ziel == "ranch/fahrt") return "ranch_reiten";
            return "ranch";
        }

        /// <summary>Ground info; an unknown ground falls back softly to the meadow.</summary>
        public static Boden UntergrundInfo(string untergrund) =>
            untergrund != null && Untergruende.TryGetValue(untergrund, out var b) ? b : Untergruende["wiese"];

        /// <summary>The single hoof beat of a ground (gras/wiese/matsch → grass, sand, wood, stone; unknown → grass).</summary>
        public static string HufIdFuer(string untergrund)
        {
            switch (untergrund)
            {
                case "sand": return "ranch_huf_sand";
                case "holz": return "ranch_huf_holz";
                case "stein": return "ranch_huf_stein";
                default: return "ranch_huf_gras";
            }
        }

        /// <summary>A horse call by reaction ("" = unknown, no sound).</summary>
        public static string ReaktionId(string art) => art != null && Reaktionen.TryGetValue(art, out var id) ? id : "";

        /// <summary>Hoof beats between two phases of the gait cycle (0..1): two per stride (ride_feel.gd hufschlaege).</summary>
        public static int Hufschlaege(double phaseVorher, double phaseNachher) =>
            phaseNachher <= phaseVorher ? 0 : (int)Math.Floor(phaseNachher * 2.0) - (int)Math.Floor(phaseVorher * 2.0);

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
