using System;

namespace Gooby.Core.Net
{
    /// <summary>
    /// Presence "kind" strings understood by GOOBY-SERVER presence.js (the server builds the
    /// German label, e.g. "ist mit Flausch im Garten"). Kinds are capped at 32 chars.
    /// </summary>
    public static class PresenceKinds
    {
        public const int MaxLength = 32;

        public const string Online = "online";
        public const string Home = "home";
        public const string Garden = "garden";
        public const string City = "city";
        public const string Drive = "drive";
        public const string Park = "park";
        public const string Ikea = "ikea";
        public const string Visit = "visit";
        public const string Board = "board";
        public const string Sleep = "sleep";
        public const string Vacation = "vacation";
        public const string Ranch = "ranch";
        public const string RanchBesuch = "ranchbesuch";
        public const string Ausritt = "ausritt";
        public const string MinigamePrefix = "minigame:";

        /// <summary>
        /// The Ranch-Treffen (ranchmp.js modes): a guest on a visit "besucht eine Freundes-Ranch", the host stays "auf der
        /// Ranch", a trail ride is "ausritt", the matches report their mode (rennen | fangen | parcours); the menu is "ranch".
        /// </summary>
        public static string ForRanch(string mode, bool isHost)
        {
            switch (mode)
            {
                case "besuch": return isHost ? Ranch : RanchBesuch;
                case "ausritt": return Ausritt;
                case "rennen":
                case "fangen":
                case "parcours": return mode;
                default: return Ranch;
            }
        }

        /// <summary>"minigame:&lt;id&gt;" (id "arcade" when empty), capped to 32 chars.</summary>
        public static string Minigame(string gameId)
        {
            var id = string.IsNullOrWhiteSpace(gameId) ? "arcade" : gameId.Trim();
            return Clamp(MinigamePrefix + id);
        }

        /// <summary>Trims and caps a kind to the server limit ("" stays "").</summary>
        public static string Clamp(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return "";
            var k = kind.Trim();
            return k.Length > MaxLength ? k.Substring(0, MaxLength) : k;
        }

        /// <summary>
        /// Maps an app route / screen id to a presence kind (presence.gd kind_for_route, loosened):
        /// "home", "home/…" → home; "garden"; "city"; "city/ort/rehwei" → park; "city/ort/gouhbus" → ikea;
        /// "city/ort/&lt;x&gt;" → x; "drive"/"city/drive"/"citydrive" → drive; "park"; "visit"/"social/visit";
        /// "board"/"social/battleship"; "sleep"; "vacation"; "minigame"/"mg_host"/"minigame:&lt;id&gt;" → minigame:&lt;id&gt;.
        /// Anything else (full-screen menus) counts as home.
        /// </summary>
        public static string ForRoute(string route, string gameId = null)
        {
            var r = (route ?? "").Trim().ToLowerInvariant();
            if (r.Length == 0) return Home;
            if (r.StartsWith(MinigamePrefix, StringComparison.Ordinal))
            {
                var id = route.Trim().Substring(MinigamePrefix.Length);
                return Minigame(string.IsNullOrEmpty(id) ? gameId : id);
            }
            if (r == "minigame" || r == "minigames" || r == "mg_host" || r == "mg" || r == "arcade")
                return Minigame(gameId);
            if (r == "home" || r.StartsWith("home/", StringComparison.Ordinal)) return Home;
            if (r == "garden" || r.StartsWith("garden/", StringComparison.Ordinal)) return Garden;
            if (r == "drive" || r == "citydrive" || r == "city/drive" || r == "city_drive") return Drive;
            if (r.StartsWith("city/ort/", StringComparison.Ordinal))
            {
                var ort = r.Substring("city/ort/".Length);
                if (ort == "rehwei") return Park;
                if (ort == "gouhbus") return Ikea;
                return ort.Length > 0 ? Clamp(ort) : City;
            }
            if (r == "city" || r.StartsWith("city/", StringComparison.Ordinal)) return City;
            if (r == "park" || r.StartsWith("park/", StringComparison.Ordinal)) return Park;
            if (r == "visit" || r == "social/visit") return Visit;
            if (r == "board" || r == "social/battleship") return Board;
            if (r == "sleep" || r == "bed") return Sleep;
            if (r == "vacation" || r.StartsWith("vacation/", StringComparison.Ordinal)) return Vacation;
            return Home;
        }
    }
}
