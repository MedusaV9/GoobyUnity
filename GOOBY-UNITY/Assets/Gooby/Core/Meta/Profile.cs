using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Minigames;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>
    /// The game's completion as a visible long-term goal (port of abschluss_logic.gd, FERTIG-1): four bounded collections
    /// — level (cap 40), achievements, base stickers and "every arcade game played at least once" — average to a percent.
    /// 100 % is reachable offline; online and DLC stickers are shown as extras and never block it.
    /// </summary>
    public static class Completion
    {
        public readonly struct Part
        {
            public readonly string Id;
            public readonly int N, Total;
            public Part(string id, int n, int total) { Id = id; Total = Math.Max(1, total); N = Math.Max(0, Math.Min(n, Total)); }
        }

        /// <summary>The four parts in display order: level, achievements, base stickers, arcade games played.</summary>
        public static List<Part> Parts(JObject state)
        {
            var achievements = AchievementsCatalog.All();
            var stickers = StickerCatalog.All();
            var baseStickers = StickerCatalog.RegularForScope(stickers, "base");
            var games = MinigameRegistry.AllGames();
            return new List<Part>
            {
                new Part("level", LevelOf(state), Leveling.MaxLevel),
                new Part("erfolge", AchievementsEngine.UnlockedCount(state, achievements), achievements.Count),
                new Part("sticker", StickerRules.UnlockedCount(state, baseStickers), baseStickers.Count),
                new Part("arcade", GamesPlayed(state, games.Select(g => g.Id)), games.Count),
            };
        }

        /// <summary>Optional progress outside the base 100 %: online first, then DLC stickers.</summary>
        public static List<Part> Extras(JObject state)
        {
            var stickers = StickerCatalog.All();
            var online = StickerCatalog.RegularForScope(stickers, "online");
            var dlc = StickerCatalog.RegularForScope(stickers, "dlc");
            return new List<Part>
            {
                new Part("sticker_online", StickerRules.UnlockedCount(state, online), online.Count),
                new Part("sticker_dlc", StickerRules.UnlockedCount(state, dlc), dlc.Count),
            };
        }

        /// <summary>The mean of the part shares in percent, floored — "100 %" only once every base goal is full.</summary>
        public static int Percent(JObject state) => Percent(Parts(state));

        public static int Percent(IList<Part> parts)
        {
            if (parts == null || parts.Count == 0) return 0;
            var sum = parts.Sum(p => (double)p.N / p.Total);
            return (int)Math.Floor(sum / parts.Count * 100.0 + 1e-9);
        }

        private static int LevelOf(JObject state) => Math.Max(1, Math.Min(Leveling.MaxLevel, (int)J.NumOr(J.Dict(state?["progression"])["level"], 1.0)));

        /// <summary>How many of the given games were played at least once (minigames.plays).</summary>
        public static int GamesPlayed(JObject state, IEnumerable<string> gameIds)
        {
            var plays = J.Dict(J.Dict(state?["minigames"])["plays"]);
            return gameIds.Count(id => J.NumOr(plays[id ?? ""], 0.0) > 0.0);
        }
    }

    /// <summary>
    /// The GOOBY-PASS (port of passport_card.gd + mrz_gag.gd, W13B): the pure parts of the passport card — the two lines
    /// of "machine-readable" nonsense from player name and Gooby nickname (strictly A–Z and '&lt;', umlauts transliterated,
    /// deterministic), the stamps page (one stamp per visited holiday destination with the date of its latest postcard,
    /// the golden WELTENGOOBY stamp at 9/9, the 5.0 UMZUG stamp for imported saves; tilt and colour from the id) and the
    /// daily rotating "special features" gag line.
    /// </summary>
    public static class Passport
    {
        public const int MrzLineLength = 30;
        public const string UmzugId = "umzug", WeltengoobyId = "weltengooby";

        /// <summary>
        /// The passport photo picked from the gallery (passport_card.gd passfoto_von): the photo's file name in the album
        /// folder, kept as the additive key profile.passfoto ("" = Gooby's 3D portrait). Only the name is stored, never a
        /// path, so a moved data folder keeps the photo.
        /// </summary>
        public static string PhotoOf(JObject state) => J.Str(J.Path(state, "profile", "passfoto"));

        public static void SetPhoto(JObject state, string fileName)
        {
            if (state == null) return;
            var name = fileName ?? "";
            var slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            J.EnsureDict(state, "profile")["passfoto"] = slash >= 0 ? name.Substring(slash + 1) : name;
        }
        private static readonly string[] Quatsch = { "MAMPF", "NOM", "FLAUSCH", "MOEHRE", "KAROTTE", "HOPPEL", "SCHNUFF", "WACKELPO", "PLUESCH", "KNUFF", "BOING", "MJAM" };

        /// <summary>A stamp: id, its name key, the date (ms, 0 = none), tilt in degrees (−6..6) and a colour slot (0..3; −1 red, 4 gold).</summary>
        public readonly struct Stamp
        {
            public readonly string Id, NameKey;
            public readonly long AtMs;
            public readonly int TiltDeg, Colour;
            public Stamp(string id, string nameKey, long atMs, int tiltDeg, int colour) { Id = id; NameKey = nameKey; AtMs = atMs; TiltDeg = tiltDeg; Colour = colour; }
        }

        public static string[] MrzLines(string playerName, string nickname) => new[] { MrzLine1(playerName, nickname), MrzLine2(playerName, nickname) };

        /// <summary>The document line: P&lt;GOO + nickname &lt;&lt; player name.</summary>
        public static string MrzLine1(string playerName, string nickname)
        {
            var gooby = Sanitize(nickname);
            if (gooby.Length == 0) gooby = "GOOBY";
            var human = Sanitize(playerName);
            if (human.Length == 0) human = "FLAUSCHFREUND";
            return Pad("P<GOO" + gooby + "<<" + human);
        }

        /// <summary>Four nonsense words picked by the names' hash.</summary>
        public static string MrzLine2(string playerName, string nickname)
        {
            var spread = HashOf(Sanitize(playerName) + "|" + Sanitize(nickname));
            var parts = new List<string>();
            for (var i = 0; i < 4; i++) parts.Add(Quatsch[(spread + i * 7) % Quatsch.Length]);
            return Pad(string.Join("<", parts));
        }

        /// <summary>Name → MRZ characters: umlauts AE/OE/UE/SS, the rest A–Z, space and hyphen → '&lt;', everything else dropped.</summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var t = text.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue").Replace("ß", "ss").ToUpperInvariant();
            var sb = new StringBuilder();
            foreach (var c in t)
            {
                if (c >= 'A' && c <= 'Z') sb.Append(c);
                else if (c == ' ' || c == '-') sb.Append('<');
            }
            return sb.ToString();
        }

        /// <summary>Deterministic spread hash (not string.GetHashCode — the contract must not hang on runtime internals).</summary>
        public static int HashOf(string text)
        {
            var h = 7;
            foreach (var c in text ?? "") h = (int)((h * 31L + c) % 1000003);
            return h;
        }

        private static string Pad(string text) => (text.Length > MrzLineLength ? text.Substring(0, MrzLineLength) : text).PadRight(MrzLineLength, '<');

        public static int TiltOf(string id) => HashOf(id) % 13 - 6;
        public static int ColourOf(string id) => HashOf("stempel|" + id) % 4;

        /// <summary>The stamps page: visited destinations in catalogue order, then WELTENGOOBY, then the moving-day stamp.</summary>
        public static List<Stamp> Stamps(JObject state)
        {
            var v = J.Dict(state?["vacation"]);
            var visited = J.Dict(v["visited"]);
            var archive = Postcards.ArchiveOf(state ?? new JObject());
            var outp = new List<Stamp>();
            foreach (var id in Postcards.DestIds)
            {
                if (!J.IsTrue(visited[id])) continue;
                long latest = 0;
                foreach (var e in archive.OfType<JObject>())
                    if (J.Str(e["destId"]) == id) latest = Math.Max(latest, J.Long(e["atMs"]));
                outp.Add(new Stamp(id, "travel.ziel." + id, latest, TiltOf(id), ColourOf(id)));
            }
            var welt = (long)J.NumOr(v["weltengoobyAt"], 0.0);
            if (welt > 0) outp.Add(new Stamp(WeltengoobyId, "reisepass.stempel_weltengooby", welt, TiltOf(WeltengoobyId), 4));
            var meta = J.Dict(state?["meta"]);
            if (J.Str(meta["importedFrom"]).Length > 0)
                outp.Add(new Stamp(UmzugId, "reisepass.stempel_umzug", (long)J.NumOr(meta["importedAt"], 0.0), TiltOf(UmzugId), -1));
            return outp;
        }

        /// <summary>The "special features" gag: rotates once a day, offset by the nickname.</summary>
        public static int FeatureIndex(string nickname, long dayIndex, int count) =>
            count <= 0 ? 0 : (int)((HashOf(nickname ?? "") + dayIndex) % count);
    }
}
