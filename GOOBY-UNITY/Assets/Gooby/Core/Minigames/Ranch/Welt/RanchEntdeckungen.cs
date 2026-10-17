using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Life;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The discoveries of the ranch region (port of ranch_entdeckungen.gd): sixteen fixed places — the waterfall, the
    /// glitter cave, the ancient tree, the stone circle, two lookouts, three hidden chests, and from the expansion the summit
    /// cross, the mountain lake's pearl, the ruin's treasure, the will-o'-the-wisp, the giant shell, the lavender bees and the
    /// crop circle — each with a small coin reward when you really ride there (16 m); found ones land additively in
    /// <c>ranch.welt.funde</c>. The trails leading to them are drawn by the terrain.
    /// </summary>
    public static class RanchEntdeckungen
    {
        public const double FundRadiusM = 16.0;

        public sealed class Ort
        {
            public string Id, NameKey;
            public V2d Pos, Blick;
            public int Muenzen;
            public bool Neu;
        }

        public sealed class Pfad
        {
            public string Id;
            public double Breite;
            public V2d[] Punkte;
        }

        private static Ort O(string id, double x, double z, double bx, double bz, int muenzen, bool neu = false) =>
            new Ort { Id = id, NameKey = "rwelt.fund." + id, Pos = new V2d(x, z), Blick = new V2d(bx, bz), Muenzen = muenzen, Neu = neu };

        /// <summary>The nine places of the first region (the contract keeps them) and the seven of the expansion.</summary>
        public static readonly Ort[] AlleOrte =
        {
            O("wasserfall", 252, -470, 226, -414, 40),
            O("hoehle", 95, -335, 58, -296, 40),
            O("alter_baum", -500, -20, -455, 16, 30),
            O("steinkreis", 60, 250, 22, 214, 30),
            O("aussicht_kamm", 160, -510, 140, -462, 25),
            O("aussicht_see", 598, 88, 556, 120, 25),
            O("truhe_wald", -448, -428, -420, -400, 60),
            O("truhe_scheune", -520, 498, -492, 474, 60),
            O("truhe_ufer", 592, 352, 556, 322, 60),
            O("gipfelkreuz", -80, -1085, 30, -1030, 80, true),
            O("bergsee_perle", 100, -1064, 120, -1044, 50, true),
            O("ruine_schatz", 700, -500, 666, -474, 70, true),
            O("moor_irrlicht", 842, -172, 806, -142, 50, true),
            O("strand_muschel", 812, 322, 846, 292, 45, true),
            O("lavendel_bienen", -846, 46, -812, 74, 40, true),
            O("kornkreis", -470, 748, -446, 776, 55, true),
        };

        private static Pfad P(string id, double breite, params double[] xz)
        {
            var pts = new V2d[xz.Length / 2];
            for (var i = 0; i < pts.Length; i++) pts[i] = new V2d(xz[i * 2], xz[i * 2 + 1]);
            return new Pfad { Id = id, Breite = breite, Punkte = pts };
        }

        /// <summary>Narrow trails from a map path to each place (earth tracks in the grass).</summary>
        public static readonly Pfad[] AllePfade =
        {
            P("pfad_alter_baum", 2.0, -320, 60, -400, 30, -460, 4, -500, -20),
            P("pfad_steinkreis", 2.0, 30, 190, 44, 220, 60, 250),
            P("pfad_wasserfall", 2.0, 210, -380, 232, -424, 248, -456),
            P("pfad_aussicht_see", 2.0, 520, 60, 560, 74, 598, 88),
            P("pfad_truhe_ufer", 1.8, 520, 396, 556, 376, 592, 352),
            P("pfad_gipfelkreuz", 2.0, 60, -1004, 4, -1042, -80, -1085),
            P("pfad_moor_irrlicht", 1.8, 800, -130, 824, -152, 842, -172),
            P("pfad_strand_muschel", 1.8, 810, 260, 808, 292, 812, 322),
            P("pfad_lavendel_bienen", 1.8, -810, 90, -830, 68, -846, 46),
            P("pfad_kornkreis", 1.8, -440, 780, -456, 764, -470, 748),
        };

        public static Ort Fundort(string id) => string.IsNullOrEmpty(id) ? null : AlleOrte.FirstOrDefault(o => o.Id == id);

        /// <summary>The ids found so far (healed: unknown ids dropped).</summary>
        public static List<string> Gefunden(JObject state) => RanchWeltState.Liste(state, "funde");

        /// <summary>A place found: marked and its coins paid once. (neu, muenzen, nameKey).</summary>
        public static (bool neu, int muenzen, string nameKey) Entdecke(JObject state, string id)
        {
            var ort = Fundort(id);
            if (state == null || ort == null) return (false, 0, "");
            if (Gefunden(state).Contains(id)) return (false, 0, ort.NameKey);
            var welt = RanchWeltState.Welt(state);
            var funde = Gefunden(state);
            funde.Add(id);
            welt["funde"] = new JArray(funde);
            var muenzen = Math.Max(0, ort.Muenzen);
            if (muenzen > 0) Economy.Award(J.EnsureDict(state, "economy"), muenzen, "ranch_fund");
            return (true, muenzen, ort.NameKey);
        }

        /// <summary>The first place not yet found within <see cref="FundRadiusM"/> of (x, z) (null when none).</summary>
        public static Ort FundBei(JObject state, double x, double z)
        {
            var schon = Gefunden(state);
            return AlleOrte.FirstOrDefault(o => !schon.Contains(o.Id) && o.Pos.Abstand(x, z) <= FundRadiusM);
        }
    }

    /// <summary>
    /// The explorer map's model (port of ranch_entdecker_karte.gd): zones under fog until ridden through (mood pastels
    /// after), paths known once one end is, the places — found ones at their spot with a "NEU" badge until looked at on the
    /// map, hidden ones as a "?" pin on a coarse 180 m grid (a hint, never the spot), the progress line and the compass
    /// hint "Dahin!" from the farm.
    /// </summary>
    public static class RanchEntdeckerKarte
    {
        public const string GesehenKey = "karteGesehen";
        public const double GrobRasterM = 180.0;
        public static readonly string[] Richtungen = { "n", "no", "o", "so", "s", "sw", "w", "nw" };

        public static readonly IReadOnlyDictionary<string, string> StimmungFarben = new Dictionary<string, string>
        {
            ["heimat"] = "#F2D8A7", ["weite"] = "#CDE8B5", ["schatten"] = "#A8CBA0", ["ruhe"] = "#BFE3EF", ["panorama"] = "#DCEAC2",
            ["frisch"] = "#C6E7E2", ["geheimnis"] = "#D9C7E8", ["fest"] = "#F7CBD8", ["dorf"] = "#F3D9C0", ["gipfel"] = "#E3E7F2",
            ["duft"] = "#E5D3F0", ["nebel"] = "#D3DAD8", ["sage"] = "#E8DAC4", ["ferien"] = "#FBE6C2", ["ernte"] = "#F5CFA8", ["gold"] = "#F3E3A9",
        };

        public const string FogFarbe = "#E4DFD6";

        public static string ZonenFarbe(string stimmung) => stimmung != null && StimmungFarben.TryGetValue(stimmung, out var c) ? c : StimmungFarben["weite"];

        public sealed class Pin
        {
            public RanchEntdeckungen.Ort Ort;
            public V2d ZeigPos;
            public bool Entdeckt, Neu;
        }

        public static List<Pin> Fundorte(JObject state)
        {
            var gefunden = RanchEntdeckungen.Gefunden(state);
            var neu = NeueFunde(state);
            return RanchEntdeckungen.AlleOrte.Select(o => new Pin
            {
                Ort = o,
                Entdeckt = gefunden.Contains(o.Id),
                ZeigPos = gefunden.Contains(o.Id) ? o.Pos : GrobePosition(o.Pos),
                Neu = neu.Contains(o.Id),
            }).ToList();
        }

        public static (int zonen, int zonenGesamt, int funde, int fundeGesamt) Fortschritt(JObject state) =>
            (RanchWeltState.EntdeckteZonen(state).Count, RanchKarte.Zonen.Count, RanchEntdeckungen.Gefunden(state).Count, RanchEntdeckungen.AlleOrte.Length);

        /// <summary>The coarse grid cell centre of a position, inside the bounds.</summary>
        public static V2d GrobePosition(V2d pos)
        {
            var x = Math.Floor(pos.X / GrobRasterM) * GrobRasterM + GrobRasterM / 2.0;
            var z = Math.Floor(pos.Z / GrobRasterM) * GrobRasterM + GrobRasterM / 2.0;
            var g = RanchKarte.Grenzen;
            var m = GrobRasterM / 4.0;
            return new V2d(Math.Min(Math.Max(x, g.X + m), g.EndX - m), Math.Min(Math.Max(z, g.Z + m), g.EndZ - m));
        }

        /// <summary>Compass sector from → to ("" for the same spot): +x east, +z south, 0° north, clockwise.</summary>
        public static string RichtungKey(V2d von, V2d nach)
        {
            var dx = nach.X - von.X;
            var dz = nach.Z - von.Z;
            if (Math.Sqrt(dx * dx + dz * dz) < 1.0) return "";
            var winkel = Math.Atan2(dx, -dz) * 180.0 / Math.PI;
            winkel = (winkel % 360.0 + 360.0) % 360.0;
            return Richtungen[(int)Math.Round(winkel / 45.0) % Richtungen.Length];
        }

        /// <summary>The zone at (or nearest to) a position — the landmark of the "Dahin!" hint.</summary>
        public static string HinweisZone(V2d pos)
        {
            var direkt = RanchKarte.ZoneBei(pos.X, pos.Z);
            if (direkt.Length > 0) return direkt;
            return RanchKarte.Zonen.OrderBy(z => z.Rect.Abstand(pos.X, pos.Z)).Select(z => z.Id).FirstOrDefault() ?? "";
        }

        public static V2d HeimatPunkt => RanchKarte.ZoneVon("hof")?.Spawn ?? new V2d(0, 0);

        public static List<string> GeseheneFunde(JObject state) => RanchWeltState.Liste(state, GesehenKey);

        /// <summary>Found, but never looked at on the map → the "NEU" badge.</summary>
        public static List<string> NeueFunde(JObject state)
        {
            var gesehen = GeseheneFunde(state);
            return RanchEntdeckungen.Gefunden(state).Where(id => !gesehen.Contains(id)).ToList();
        }

        public static void MarkiereGesehen(JObject state, IEnumerable<string> ids)
        {
            var gesehen = GeseheneFunde(state);
            var neu = false;
            foreach (var id in ids)
            {
                if (RanchEntdeckungen.Fundort(id) == null || gesehen.Contains(id)) continue;
                gesehen.Add(id);
                neu = true;
            }
            if (neu) RanchWeltState.Welt(state)[GesehenKey] = new JArray(gesehen);
        }
    }
}
