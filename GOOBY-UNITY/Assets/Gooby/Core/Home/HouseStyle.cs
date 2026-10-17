using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// "Gestalten" for the rooms (port of house_style_state.gd + customize_catalog.gd + customize_materials.gd, room part):
    /// every room gets a wall (pattern × palette colour) and a floor from home/wall_floor_catalog.json. Free options are
    /// always owned, the others are bought once with coins (reason "gestalten") and then usable in every room in all of
    /// their colours. Saved additively in home.style = {gekauft{wand|boden: [ids]}, raeume{roomId: {wand, wandFarbe,
    /// boden, bodenFarbe}}}; a room without an entry keeps its built look. The pattern pixels are pure (128 px greyscale,
    /// tinted with the palette) so textures and the preview tiles share one formula.
    /// </summary>
    public static class HouseStyle
    {
        public const string CatalogPath = "home/wall_floor_catalog";
        public const string Wand = "wand";
        public const string Boden = "boden";
        public const string Reason = "gestalten";
        public const int TexPx = 128;
        public const double ReferenceLuminance = 0.78;

        public static readonly IReadOnlyDictionary<string, string> Palette = new Dictionary<string, string>
        {
            ["weiss"] = "#FFFFFF", ["creme"] = "#FFF6EC", ["rose"] = "#FFD5E5", ["pink"] = "#FF7BA9", ["koralle"] = "#FFC2B4",
            ["butter"] = "#FFEFC2", ["sonnengelb"] = "#FFD166", ["ocker"] = "#D9A441", ["mint"] = "#C9F0E0", ["salbei"] = "#D6E5C6",
            ["blattgruen"] = "#8FD06C", ["tannengruen"] = "#4E7D5B", ["himmel"] = "#CFE9F5", ["teal"] = "#59C9B9", ["marine"] = "#4A6FA5",
            ["lavendel"] = "#E2D6F5", ["flieder"] = "#C9B6E8", ["schiefer"] = "#AEBBC7", ["grau_hell"] = "#CFCBC4", ["grau"] = "#9C968E",
            ["anthrazit"] = "#55514B", ["sandstein"] = "#E3D3B4", ["terracotta"] = "#C97B4A", ["ziegelrot"] = "#B5533C",
            ["eiche_hell"] = "#D9B98C", ["eiche"] = "#C9A36B", ["nussbaum"] = "#8A6642", ["walnuss"] = "#5F4630",
        };

        /// <summary>World metres per pattern tile (customize_materials MUSTER_META, room patterns).</summary>
        public static readonly IReadOnlyDictionary<string, double> MeterProKachel = new Dictionary<string, double>
        {
            ["punkte"] = 0.8, ["streifen"] = 0.72, ["streifen_fein"] = 0.4, ["blumen"] = 0.9, ["ranken"] = 0.8, ["karo"] = 0.6,
            ["kacheln"] = 0.8, ["rauten"] = 0.7, ["dielen"] = 1.4, ["fischgraet"] = 0.9, ["wuerfel"] = 0.96, ["fliesen"] = 1.2,
            ["schach"] = 1.0, ["stein"] = 1.5,
        };

        public sealed class Option
        {
            public string Id, Art, Muster, NameDe, NameEn;
            public int Preis;
            public List<string> Farben = new List<string>();
            public string Name(string locale) => locale == "en" ? NameEn : NameDe;
        }

        public sealed class RoomStyle
        {
            public string Wand, WandFarbe, Boden, BodenFarbe;
        }

        private static List<Option> _options;

        public static IReadOnlyList<Option> Options()
        {
            if (_options != null) return _options;
            _options = new List<Option>();
            var root = ContentSource.LoadObject(CatalogPath);
            foreach (var (key, art) in new[] { ("waende", Wand), ("boeden", Boden) })
                foreach (var t in root[key] as JArray ?? new JArray())
                {
                    if (!(t is JObject o)) continue;
                    var opt = new Option
                    {
                        Id = J.Str(o["id"]), Art = art, Muster = J.Str(o["muster"], "uni"), NameDe = J.Str(o["name_de"]),
                        NameEn = J.Str(o["name_en"], J.Str(o["name_de"])), Preis = Math.Max(0, J.Int(o["preis"])),
                    };
                    foreach (var f in o["farben"] as JArray ?? new JArray()) if (Palette.ContainsKey(J.Str(f))) opt.Farben.Add(J.Str(f));
                    if (opt.Id.Length > 0 && opt.Farben.Count > 0) _options.Add(opt);
                }
            return _options;
        }

        public static void ResetCache() => _options = null;

        public static Option Get(string art, string id) => Options().FirstOrDefault(o => o.Art == art && o.Id == id);

        public static IEnumerable<Option> OfArt(string art) => Options().Where(o => o.Art == art);

        public static bool Owns(GameStateStore gs, string art, string id)
        {
            var opt = Get(art, id);
            if (opt == null) return false;
            if (opt.Preis <= 0) return true;
            return J.Path(gs?.State, "home", "style", "gekauft", art) is JArray owned && owned.Any(x => J.Str(x) == id);
        }

        /// <summary>The room's saved style, or null while it keeps its built look.</summary>
        public static RoomStyle Room(GameStateStore gs, string roomId)
        {
            if (!(J.Path(gs?.State, "home", "style", "raeume", roomId ?? "") is JObject r)) return null;
            var wand = Get(Wand, J.Str(r["wand"])) ?? Get(Wand, "uni");
            var boden = Get(Boden, J.Str(r["boden"])) ?? Get(Boden, "dielen_hell");
            if (wand == null || boden == null) return null;
            var wf = J.Str(r["wandFarbe"]);
            var bf = J.Str(r["bodenFarbe"]);
            return new RoomStyle
            {
                Wand = wand.Id, WandFarbe = wand.Farben.Contains(wf) ? wf : wand.Farben[0],
                Boden = boden.Id, BodenFarbe = boden.Farben.Contains(bf) ? bf : boden.Farben[0],
            };
        }

        public const string ResultOk = "ok";
        public const string ResultUnknown = "unknown_option";
        public const string ResultBroke = "not_enough_coins";
        public const string ResultOwned = "already_owned";

        /// <summary>Buys an option once (coins via Economy, reason "gestalten").</summary>
        public static string Buy(GameStateStore gs, string art, string id)
        {
            var opt = Get(art, id);
            if (opt == null || gs == null) return ResultUnknown;
            if (Owns(gs, art, id)) return ResultOwned;
            var result = ResultBroke;
            gs.Update(state =>
            {
                var economy = J.EnsureDict(state, "economy");
                if (!Economy.Spend(economy, opt.Preis, Reason)) return;
                var gekauft = J.EnsureDict(J.EnsureDict(J.EnsureDict(state, "home"), "style"), "gekauft");
                if (!(gekauft[art] is JArray list)) gekauft[art] = list = new JArray();
                list.Add(id);
                result = ResultOk;
            });
            if (result == ResultOk) gs.NotifySliceChanged("home");
            return result;
        }

        /// <summary>Puts an owned option (in one of its colours) on the room's wall or floor. False when not allowed.</summary>
        public static bool Apply(GameStateStore gs, string roomId, string art, string id, string farbe)
        {
            var opt = Get(art, id);
            if (gs == null || opt == null || string.IsNullOrEmpty(roomId) || !Owns(gs, art, id) || !opt.Farben.Contains(farbe)) return false;
            var current = Room(gs, roomId) ?? new RoomStyle { Wand = "uni", WandFarbe = "creme", Boden = "dielen_hell", BodenFarbe = "eiche" };
            if (art == Wand) { current.Wand = id; current.WandFarbe = farbe; }
            else { current.Boden = id; current.BodenFarbe = farbe; }
            gs.Update(state =>
            {
                var raeume = J.EnsureDict(J.EnsureDict(J.EnsureDict(state, "home"), "style"), "raeume");
                raeume[roomId] = new JObject { ["wand"] = current.Wand, ["wandFarbe"] = current.WandFarbe, ["boden"] = current.Boden, ["bodenFarbe"] = current.BodenFarbe };
            });
            gs.NotifySliceChanged("home");
            return true;
        }

        /// <summary>Back to the room's built look.</summary>
        public static void Reset(GameStateStore gs, string roomId)
        {
            if (!(J.Path(gs?.State, "home", "style", "raeume") is JObject)) return;
            gs.Update(state => (J.Path(state, "home", "style", "raeume") as JObject)?.Remove(roomId));
            gs.NotifySliceChanged("home");
        }

        // ── outside: the cottage's colours (house_styles.json fassade/dach/tuer/fenster — free palette picks) ──

        public const string HausStylesPath = "home/house_styles";
        /// <summary>home.style.haus key → house_styles.json section.</summary>
        public static readonly (string key, string section)[] HausFarben =
        {
            ("fassade", "fassade"), ("dachFarbe", "dach"), ("tuerFarbe", "tuer"), ("fensterFarbe", "fenster"),
        };

        public static IReadOnlyList<string> FarbenFuer(string section)
        {
            var root = ContentSource.LoadObject(HausStylesPath);
            return (J.Path(root, section, "farben") as JArray ?? new JArray()).Select(f => J.Str(f)).Where(Palette.ContainsKey).ToList();
        }

        /// <summary>The cottage's chosen colours (key → palette id), or null while it keeps its built look.</summary>
        public static Dictionary<string, string> Haus(GameStateStore gs)
        {
            if (!(J.Path(gs?.State, "home", "style", "haus") is JObject h)) return null;
            var outp = new Dictionary<string, string>();
            foreach (var (key, section) in HausFarben)
            {
                var farbe = J.Str(h[key]);
                if (FarbenFuer(section).Contains(farbe)) outp[key] = farbe;
            }
            return outp.Count > 0 ? outp : null;
        }

        /// <summary>Paints one part of the cottage (free); false for an unknown part or a colour that part does not offer.</summary>
        public static bool SetHaus(GameStateStore gs, string key, string farbe)
        {
            var section = HausFarben.FirstOrDefault(p => p.key == key).section;
            if (gs == null || section == null || !FarbenFuer(section).Contains(farbe)) return false;
            gs.Update(state => J.EnsureDict(J.EnsureDict(J.EnsureDict(state, "home"), "style"), "haus")[key] = farbe);
            gs.NotifySliceChanged("home");
            return true;
        }

        public static void ResetHaus(GameStateStore gs)
        {
            if (!(J.Path(gs?.State, "home", "style") is JObject)) return;
            gs.Update(state =>
            {
                var style = J.Path(state, "home", "style") as JObject;
                style?.Remove("haus");
                style?.Remove("grundstueck");
            });
            gs.NotifySliceChanged("home");
        }

        // ── outside: the cottage's parts and the plot (house_styles.json hausnummer/briefkasten/vordach + grundstueck) ──

        public const string Hausnummer = "hausnummer", Briefkasten = "briefkasten", Vordach = "vordach", Zaun = "zaun", Weg = "weg", Grund = "boden_aussen";

        /// <summary>The outside parts in panel order: (art, where they sit in the save, the id key, the colour key).</summary>
        public static readonly (string art, string bereich, string idKey, string farbKey)[] Teile =
        {
            (Hausnummer, "haus", "hausnummer", null), (Briefkasten, "haus", "briefkasten", "briefkastenFarbe"),
            (Vordach, "haus", "vordach", "vordachFarbe"), (Zaun, "grundstueck", "zaun", "zaunFarbe"),
            (Weg, "grundstueck", "weg", "wegFarbe"), (Grund, "grundstueck", "boden", "bodenFarbe"),
        };

        public static IReadOnlyList<Option> TeilOptionen(string art)
        {
            var root = ContentSource.LoadObject(HausStylesPath);
            JArray raw;
            switch (art)
            {
                case Zaun: raw = J.Path(root, "grundstueck", "zaeune") as JArray; break;
                case Weg: raw = J.Path(root, "grundstueck", "wege") as JArray; break;
                case Grund: raw = J.Path(root, "grundstueck", "boeden") as JArray; break;
                default: raw = root[art] as JArray; break;
            }
            var outp = new List<Option>();
            foreach (var t in raw ?? new JArray())
            {
                if (!(t is JObject o) || J.Str(o["id"]).Length == 0) continue;
                var opt = new Option
                {
                    Id = J.Str(o["id"]), Art = art, Muster = J.Str(o["muster"]), NameDe = J.Str(o["name_de"]),
                    NameEn = J.Str(o["name_en"], J.Str(o["name_de"])), Preis = Math.Max(0, J.Int(o["preis"])),
                };
                foreach (var f in o["farben"] as JArray ?? new JArray()) if (Palette.ContainsKey(J.Str(f))) opt.Farben.Add(J.Str(f));
                outp.Add(opt);
            }
            return outp;
        }

        public static Option TeilOption(string art, string id) => TeilOptionen(art).FirstOrDefault(o => o.Id == id);

        private static JObject TeilDefaults(string bereich) =>
            J.Path(ContentSource.LoadObject(HausStylesPath), "defaults", bereich) as JObject ?? new JObject();

        /// <summary>The chosen option and colour of a part — the save's pick when valid, else house_styles.json's default.</summary>
        public static (string id, string farbe) TeilWahl(GameStateStore gs, string art)
        {
            var (_, bereich, idKey, farbKey) = Teile.First(t => t.art == art);
            var saved = J.Path(gs?.State, "home", "style", bereich) as JObject;
            var defaults = TeilDefaults(bereich);
            var opt = TeilOption(art, J.Str(saved?[idKey])) ?? TeilOption(art, J.Str(defaults[idKey])) ?? TeilOptionen(art).FirstOrDefault();
            if (opt == null) return ("", "");
            if (farbKey == null || opt.Farben.Count == 0) return (opt.Id, "");
            var farbe = J.Str(saved?[farbKey]);
            if (!opt.Farben.Contains(farbe)) farbe = opt.Farben.Contains(J.Str(defaults[farbKey])) ? J.Str(defaults[farbKey]) : opt.Farben[0];
            return (opt.Id, farbe);
        }

        /// <summary>Has the player picked this part? Until then the garden keeps its built look (log fence, stones, red mailbox).</summary>
        public static bool TeilGewaehlt(GameStateStore gs, string art)
        {
            var (_, bereich, idKey, _) = Teile.First(t => t.art == art);
            return J.Str(J.Path(gs?.State, "home", "style", bereich, idKey)).Length > 0;
        }

        public static int HausnummerZahl(GameStateStore gs) =>
            J.ClampI(J.Int(J.Path(gs?.State, "home", "style", "haus", "hausnummerZahl"), J.Int(TeilDefaults("haus")["hausnummerZahl"], 5)), 1, 99);

        public static bool OwnsTeil(GameStateStore gs, string art, string id)
        {
            var opt = TeilOption(art, id);
            if (opt == null) return false;
            return opt.Preis <= 0 || (J.Path(gs?.State, "home", "style", "gekauft", art) is JArray owned && owned.Any(x => J.Str(x) == id));
        }

        /// <summary>Buys an outside part once (coins via Economy, reason "gestalten").</summary>
        public static string BuyTeil(GameStateStore gs, string art, string id)
        {
            var opt = TeilOption(art, id);
            if (opt == null || gs == null) return ResultUnknown;
            if (OwnsTeil(gs, art, id)) return ResultOwned;
            var result = ResultBroke;
            gs.Update(state =>
            {
                if (!Economy.Spend(J.EnsureDict(state, "economy"), opt.Preis, Reason)) return;
                var gekauft = J.EnsureDict(J.EnsureDict(J.EnsureDict(state, "home"), "style"), "gekauft");
                if (!(gekauft[art] is JArray list)) gekauft[art] = list = new JArray();
                list.Add(id);
                result = ResultOk;
            });
            if (result == ResultOk) gs.NotifySliceChanged("home");
            return result;
        }

        /// <summary>Puts an owned part (in one of its colours, or none) on the cottage or the plot. False when not allowed.</summary>
        public static bool SetTeil(GameStateStore gs, string art, string id, string farbe)
        {
            var opt = TeilOption(art, id);
            if (gs == null || opt == null || !OwnsTeil(gs, art, id)) return false;
            if (opt.Farben.Count > 0 && !opt.Farben.Contains(farbe ?? "")) return false;
            var (_, bereich, idKey, farbKey) = Teile.First(t => t.art == art);
            gs.Update(state =>
            {
                var part = J.EnsureDict(J.EnsureDict(J.EnsureDict(state, "home"), "style"), bereich);
                part[idKey] = id;
                if (farbKey != null) part[farbKey] = opt.Farben.Count > 0 ? farbe : "";
            });
            gs.NotifySliceChanged("home");
            return true;
        }

        public static void SetHausnummer(GameStateStore gs, int zahl)
        {
            if (gs == null) return;
            gs.Update(state => J.EnsureDict(J.EnsureDict(J.EnsureDict(state, "home"), "style"), "haus")["hausnummerZahl"] = J.ClampI(zahl, 1, 99));
            gs.NotifySliceChanged("home");
        }

        // ── the pattern pixels (customize_materials.gd _wert, room patterns) ──

        public static double Hash(int x, int y)
        {
            long h = ((long)x * 374761393L + (long)y * 668265263L) % 2147483647L;
            h = ((h ^ (h >> 13)) * 1274126177L) % 2147483647L;
            return Math.Abs(h) % 10000 / 10000.0;
        }

        private static double Raster(int x, int y, int feld, int fuge, double hell) =>
            x % feld < fuge || y % feld < fuge ? 0.55 : hell + (Hash(x / feld, y / feld) - 0.5) * 0.05;

        /// <summary>Greyscale brightness 0..1 of a pattern at a texel (128 px tiles).</summary>
        public static double Wert(string muster, int x, int y)
        {
            double v;
            switch (muster)
            {
                case "punkte":
                {
                    var row = y / 32;
                    var cx = (x + row % 2 * 16) % 32 - 16;
                    var cy = y % 32 - 16;
                    v = cx * cx + cy * cy < 36 ? 0.62 : 0.86;
                    break;
                }
                case "streifen": v = x / 16 % 2 == 0 ? 0.87 : 0.7; break;
                case "streifen_fein": v = x % 16 < 3 ? 0.7 : 0.88; break;
                case "blumen":
                {
                    var row = y / 64;
                    var dx = (x + row % 2 * 32) % 64 - 32;
                    var dy = y % 64 - 32;
                    v = 0.88 + (Hash(x / 8, y / 8) - 0.5) * 0.02;
                    if (dx * dx + dy * dy < 20) { v = 0.96; break; }
                    for (var k = 0; k < 5; k++)
                    {
                        var w = Math.PI * 2.0 * k / 5.0;
                        var px = dx - (int)Math.Round(Math.Cos(w) * 9.0);
                        var py = dy - (int)Math.Round(Math.Sin(w) * 9.0);
                        if (px * px + py * py < 30) { v = 0.63; break; }
                    }
                    break;
                }
                case "ranken":
                {
                    var col = x / 32;
                    var welle = 16.0 + Math.Sin(y * 0.2 + col * 2.1) * 4.0;
                    var abstand = Math.Abs(x % 32 - welle);
                    v = abstand < 1.6 ? 0.62 : y % 16 < 3 && abstand < 5.0 ? 0.68 : 0.87;
                    break;
                }
                case "paneel": v = x % 32 < 2 ? 0.55 : 0.82 + Hash(x / 32, 0) * 0.06 + (Hash(x / 32, y / 8) - 0.5) * 0.04; break;
                case "karo":
                {
                    var sx = x / 16 % 2 == 0;
                    var sy = y / 16 % 2 == 0;
                    v = sx && sy ? 0.66 : sx || sy ? 0.78 : 0.9;
                    break;
                }
                case "kacheln": v = Raster(x, y, 32, 2, 0.88); break;
                case "rauten": v = ((x + y) / 32 + (x - y + 256) / 32) % 2 == 0 ? 0.85 : 0.68; break;
                case "dielen":
                {
                    var row = y / 21;
                    if (y % 21 < 2 || (x + row * 43) % 128 < 2) { v = 0.5; break; }
                    var brett = (x + row * 43) / 128;
                    v = 0.8 + (Hash(row, brett) - 0.5) * 0.12 + (Hash(x, row) - 0.5) * 0.03;
                    break;
                }
                case "fischgraet":
                {
                    var col = x / 16;
                    var diagonale = col % 2 == 0 ? x + y : x - y + 256;
                    v = diagonale % 16 < 2 ? 0.55 : 0.82 + (Hash(diagonale / 16, col) - 0.5) * 0.1;
                    break;
                }
                case "wuerfel":
                {
                    var bx = x / 32;
                    var by = y / 32;
                    var quer = (bx + by) % 2 == 0;
                    v = (quer ? y % 8 < 1 : x % 8 < 1) ? 0.6 : 0.82 + (Hash(bx, by) - 0.5) * 0.06;
                    break;
                }
                case "fliesen": v = Raster(x, y, 64, 3, 0.9); break;
                case "schach": v = (x / 32 + y / 32) % 2 == 0 ? 0.9 : 0.62; break;
                case "teppich": v = 0.8 + (Hash(x / 2, y / 2) - 0.5) * 0.1; break;
                case "stein":
                {
                    var row = y / 42;
                    var ox = (int)(Hash(row, 7) * 40.0);
                    v = (x + ox) % 56 < 3 || y % 42 < 3 ? 0.55 : 0.8 + (Hash((x + ox) / 56, row) - 0.5) * 0.1;
                    break;
                }
                default: v = 0.86 + (Hash(x / 4, y / 4) - 0.5) * 0.03; break;
            }
            return Math.Max(0.0, Math.Min(1.0, v));
        }

        /// <summary>Tints a greyscale texel with a palette colour (customize_materials.blend): rgb 0..1.</summary>
        public static (double r, double g, double b) Blend(double grey, string farbe)
        {
            var hex = Palette.TryGetValue(farbe ?? "", out var h) ? h : Palette["creme"];
            var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
            var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
            var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
            var level = Math.Max(0.35, Math.Min(1.15, grey / ReferenceLuminance));
            return (Math.Min(1.0, r * level), Math.Min(1.0, g * level), Math.Min(1.0, b * level));
        }
    }
}
