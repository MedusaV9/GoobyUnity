using System;
using System.Collections.Generic;

namespace Gooby.Core.Audio
{
    /// <summary>
    /// What a scene sounds like — port of music_director.gd ROUTE_CONTEXTS and klangbett.gd ORT_BETTEN on the Unity
    /// scene keys: "home/&lt;room&gt;" (living, kitchen, bathroom, bedroom, garden), "city", "city/ort/&lt;place&gt;",
    /// "arcade", "mg/&lt;gameId&gt;", "park", "vacation", "space", "ranch/&lt;part&gt;", "boot". Pure; the runtime director
    /// asks this once per scene change.
    /// </summary>
    public static class SoundScape
    {
        /// <summary>Bed layers → clip ids under gooby-audio/ambient (the ranch layers are driven live, see <see cref="RanchKlang"/>).</summary>
        public static readonly IReadOnlyDictionary<string, string> BedClips = new Dictionary<string, string>
        {
            ["kamin"] = "bett_kamin", ["uhr"] = "bett_uhr", ["voegel"] = "bett_voegel",
            ["wind"] = "bett_wind", ["stadt"] = "bett_stadt", ["laden"] = "bett_laden",
            ["ranch_wind"] = "ranch_wind", ["ranch_regen"] = "ranch_regen", ["ranch_gewitter"] = "ranch_gewitter",
            ["ranch_voegel"] = "ranch_voegel", ["ranch_grillen"] = "ranch_grillen", ["ranch_bach"] = "ranch_bach",
            ["ranch_menge"] = "ranch_menge",
        };

        private static readonly Dictionary<string, Dictionary<string, double>> Beds = new Dictionary<string, Dictionary<string, double>>
        {
            ["home/living"] = new Dictionary<string, double> { ["kamin"] = 0.85, ["uhr"] = 0.3 },
            ["home/kitchen"] = new Dictionary<string, double> { ["uhr"] = 0.7 },
            ["home/bathroom"] = new Dictionary<string, double> { ["uhr"] = 0.35 },
            ["home/bedroom"] = new Dictionary<string, double> { ["uhr"] = 0.55 },
            ["home/garden"] = new Dictionary<string, double> { ["voegel"] = 0.8, ["wind"] = 0.5 },
            ["city"] = new Dictionary<string, double> { ["stadt"] = 0.85, ["voegel"] = 0.3 },
            ["city/ort/wochenmarkt"] = new Dictionary<string, double> { ["stadt"] = 0.5, ["voegel"] = 0.35 },
            ["city/ort/funkelpark"] = new Dictionary<string, double> { ["voegel"] = 0.6, ["stadt"] = 0.35 },
            ["park"] = new Dictionary<string, double> { ["voegel"] = 0.6, ["stadt"] = 0.35 },
            ["city/ort/flughafen"] = new Dictionary<string, double> { ["laden"] = 0.6, ["stadt"] = 0.3 },
            ["arcade"] = new Dictionary<string, double> { ["laden"] = 0.45 },
        };

        /// <summary>Interior fallback for places without an own bed (klangbett.gd LADEN_STANDARD).</summary>
        private static readonly Dictionary<string, double> ShopRoomTone = new Dictionary<string, double> { ["laden"] = 0.7 };

        private static readonly HashSet<string> ShopPlaces = new HashSet<string> { "mart", "goobyman", "pow", "baumarkt", "autohaus", "gouhbus", "rehwei", "ikea", "goobye" };
        private static readonly HashSet<string> CarePlaces = new HashSet<string> { "tierarzt", "goobytheke" };

        /// <summary>
        /// Music lift per context in dB (Unity, from tools/playtest/audio_sweep.sh): the music bed sits near −38 dBFS RMS in
        /// every game and most rounds reach the −30 median with their effects; a round with few effects stays far under it.
        /// </summary>
        private static readonly Dictionary<string, double> MusicLift = new Dictionary<string, double>
        {
            ["game:rocketRescue"] = 5.0,
        };

        public static double MusicLiftDb(string context) => context != null && MusicLift.TryGetValue(context, out var db) ? db : 0.0;

        /// <summary>The music context of a scene key ("" = silence, null = unknown key — keep what plays).</summary>
        public static string MusicContext(string scene, bool sleeping)
        {
            if (string.IsNullOrEmpty(scene)) return null;
            if (scene.StartsWith("home/", StringComparison.Ordinal))
            {
                var room = scene.Substring(5);
                switch (room)
                {
                    case "garden": return "garden";
                    case "kitchen": return "room:kitchen";
                    case "bathroom": return "room:bathroom";
                    case "bedroom": return sleeping ? "home_night" : "room:bedroom";
                    default: return sleeping ? "home_night" : "home";
                }
            }
            if (scene.StartsWith("city/ort/", StringComparison.Ordinal))
            {
                var place = scene.Substring(9);
                if (place == "flughafen") return "vacation";
                if (place == "funkelpark") return "park";
                if (CarePlaces.Contains(place)) return "vet";
                if (ShopPlaces.Contains(place)) return "shop";
                return "city";
            }
            if (scene.StartsWith("mg/", StringComparison.Ordinal)) return "game:" + scene.Substring(3);
            if (scene.StartsWith("ranch/", StringComparison.Ordinal))
            {
                // ranch_audio.gd musik_kontext_fuer: the tournament beats the night ("…/nacht"), the night beats riding;
                // the farm, the village and building play the ranch day
                if (scene.Contains("turnier")) return "ranch_turnier";
                if (scene.EndsWith("/nacht", StringComparison.Ordinal) || scene == "ranch/nacht") return "ranch_nacht";
                var part = scene.Substring(6);
                switch (part)
                {
                    case "hof": case "dorf": case "bau": return "ranch";
                    case "welt": case "fahrt": case "reiten": return "ranch_reiten";
                    default: return "ranch_" + part;
                }
            }
            switch (scene)
            {
                case "boot": return "home";
                case "recap": return "recap";
                case "city":
                case "arcade":
                case "park":
                case "vacation":
                case "space":
                    return scene;
                default: return null;
            }
        }

        /// <summary>
        /// The ambience layers of a scene (layer → gain 0..1): empty = beds off (minigames, ranch), null = unknown key
        /// (keep the running bed, like the music).
        /// </summary>
        public static IReadOnlyDictionary<string, double> BedPlan(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return null;
            if (Beds.TryGetValue(scene, out var plan)) return plan;
            if (scene.StartsWith("mg/", StringComparison.Ordinal) || scene.StartsWith("ranch/", StringComparison.Ordinal)
                || scene == "space" || scene == "boot" || scene == "vacation" || scene == "recap")
                return new Dictionary<string, double>();
            if (scene.StartsWith("city/ort/", StringComparison.Ordinal)) return ShopRoomTone;
            return null;
        }

        // ── ducking (klangbett.gd duck_ziel/duck_schritt): beds and music dip while Gooby talks or a jingle plays ──

        public const double DuckFactor = 0.25;
        public const double DuckDownSec = 0.25;
        public const double DuckUpSec = 0.9;

        public static double DuckTarget(bool babbling, double stingerLeftSec) => babbling || stingerLeftSec > 0 ? DuckFactor : 1.0;

        public static double DuckStep(double current, double target, double dt)
        {
            var span = 1.0 - DuckFactor;
            var rate = target < current ? span / DuckDownSec : span / DuckUpSec;
            var step = rate * Math.Max(dt, 0.0);
            return Math.Abs(target - current) <= step ? target : current + Math.Sign(target - current) * step;
        }
    }
}
