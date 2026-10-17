using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The height model of the ranch region (port of ranch_gelaende.gd) — a deterministic function of (x, z) and the map:
    /// large forms outside the old core, gentle base hills, the zone features (the ridge of the Hügelkamm, the lake basin,
    /// the Weidetal hollow, the mountain massif's three ridges with the foothill saddle, the ruin hill, the bay, the moor
    /// trough), fine ground waves smoothed on the paths, flat plateaus for the farm, the tournament ground and Hufingen and
    /// the round mountain plateau with its lake, the moor pans, a floor above the water and a soft world edge, and the
    /// notches of the brook (the ford stays shallow) and of the gorge (the suspension bridge carries the rider).
    /// </summary>
    public static class RanchGelaende
    {
        public const double WasserHoehe = -1.1, BasisHoehe = 3.0, PlateauRandM = 60.0, FurtRadiusM = 14.0;
        public const double BachWasserAbM = 0.9, FeinMaxM = 0.62, WegGlaettRandM = 5.0, WegRestAnteil = 0.15;
        public const double GrossformRampeM = 240.0, GrossformAmpM = 6.5, RandBlendeM = 80.0, RandZielM = 1.2, BodenMinM = -1.02;
        public const double MoorPfanneM = -0.4, MoorWanneM = 2.4, TuempelZielM = -0.85, TuempelRadiusM = 10.0, DeckHoeheM = 0.3;

        /// <summary>The world before the expansion: the large forms stay out of it.</summary>
        private static readonly RanchKarte.Rect KernRect = new RanchKarte.Rect(-700.0, -650.0, 1400.0, 1300.0);

        /// <summary>Mountain ridges: foot (x, z), head (x, z), amplitude, width.</summary>
        private static readonly double[][] BergGrate =
        {
            new[] { -280.0, -1050.0, 140.0, -1120.0, 88.0, 150.0 },
            new[] { 180.0, -1040.0, 340.0, -950.0, 48.0, 120.0 },
            new[] { -330.0, -950.0, -180.0, -850.0, 36.0, 110.0 },
        };

        private static readonly double[] Vorberg = { 100.0, -745.0, 14.0, 130.0 };

        private enum FeatureTyp { Kamm, See, Tal, Berg, Ruine, Bucht, Moor }

        private sealed class Feature
        {
            public FeatureTyp Typ;
            public V2d A, B;
            public double R;
            public RanchKarte.Rect Rect;
        }

        private sealed class Strecke
        {
            public V2d A, B;
            public double Halb, MinX, MaxX, MinZ, MaxZ;
        }

        private sealed class Geo
        {
            public List<Feature> Features;
            public List<(RanchKarte.Rect rect, double basis)> RectPlateaus;
            public (V2d mitte, double radius, double basis, V2d see, double seeRadius)? BergPlateau;
            public List<(V2d mitte, double radius)> Senken;
            public (RanchKarte.Rect rect, List<V2d> tuempel)? Moor;
            public (V2d mitte, double radius16, double wasser)? Bergsee;
            public RanchKarte.Rect Grenzen;
            public List<Strecke> Bach, Schlucht, Wege;
            public double BachHalb, BachTiefe, SchluchtHalb, SchluchtTiefe;
            public V2d BachFurt;
            public List<(V2d a, V2d b, double halb, double ya, double yb, double minX, double maxX, double minZ, double maxZ)> Decks;
        }

        private static Geo _geo;

        public static void ResetCache() => _geo = null;

        /// <summary>Ground height (m) at a region position. Deterministic.</summary>
        public static double Hoehe(double x, double z)
        {
            var geo = G();
            var h = BasisHoehe + Grundhuegel(x, z) + Grossformen(x, z);
            h += ZonenFeatures(x, z, geo);
            h += Feinstruktur(x, z);
            h = Plateaus(x, z, h, geo);
            h = MoorPfannen(x, z, h, geo);
            h = BodenUndRand(x, z, h, geo);
            h -= BachKerbe(x, z);
            h -= SchluchtKerbe(x, z);
            return h;
        }

        /// <summary>The riding height: the ground, or a bridge deck where one crosses the gorge (with a little sag).</summary>
        public static double ReitHoehe(double x, double z)
        {
            var h = Hoehe(x, z);
            foreach (var d in Decks())
            {
                if (x < d.minX || x > d.maxX || z < d.minZ || z > d.maxZ) continue;
                var abx = d.b.X - d.a.X;
                var abz = d.b.Z - d.a.Z;
                var t = Clamp01(((x - d.a.X) * abx + (z - d.a.Z) * abz) / Math.Max(abx * abx + abz * abz, 0.0001));
                var px = d.a.X + abx * t;
                var pz = d.a.Z + abz * t;
                if (Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz)) > d.halb) continue;
                var deck = Lerp(d.ya, d.yb, t) + DeckHoeheM - Math.Sin(t * Math.PI) * 0.55;
                h = Math.Max(h, deck);
            }
            return h;
        }

        /// <summary>Fine ground waves (~30 m) and small bumps (~10 m), damped on the map's paths.</summary>
        public static double Feinstruktur(double x, double z)
        {
            var wellen = Math.Sin(x * 0.19 + Math.Sin(z * 0.23) * 1.3) * Math.Sin(z * 0.17 - Math.Sin(x * 0.13) * 1.1) * 0.4;
            var rau = Math.Sin(x * 0.53 + 2.3) * Math.Cos(z * 0.47 - 0.8) * 0.14;
            rau += Math.Sin((x + z) * 0.71 + 1.1) * 0.08;
            return (wellen + rau) * WegGlaettung(x, z);
        }

        /// <summary>Damping of the fine structure: <see cref="WegRestAnteil"/> on a path, 1 in the open, a soft ramp at the edge.</summary>
        public static double WegGlaettung(double x, double z)
        {
            var faktor = 1.0;
            foreach (var s in G().Wege)
            {
                if (x < s.MinX || x > s.MaxX || z < s.MinZ || z > s.MaxZ) continue;
                var d = AbstandZuStrecke(x, z, s.A, s.B);
                if (d >= s.Halb + WegGlaettRandM) continue;
                var t = Clamp01((d - s.Halb) / WegGlaettRandM);
                faktor = Math.Min(faktor, Lerp(WegRestAnteil, 1.0, t * t * (3.0 - 2.0 * t)));
            }
            return faktor;
        }

        /// <summary>Distance to the nearest map path's centre line (∞ far from all).</summary>
        public static double WegAbstand(double x, double z, out double halb)
        {
            var best = double.PositiveInfinity;
            halb = 0.0;
            foreach (var s in G().Wege)
            {
                if (x < s.MinX || x > s.MaxX || z < s.MinZ || z > s.MaxZ) continue;
                var d = AbstandZuStrecke(x, z, s.A, s.B);
                if (d < best) { best = d; halb = s.Halb - 1.0; }
            }
            return best;
        }

        /// <summary>Surface normal (central differences) as (nx, ny, nz).</summary>
        public static (double x, double y, double z) Normale(double x, double z)
        {
            const double e = 1.5;
            var dx = Hoehe(x + e, z) - Hoehe(x - e, z);
            var dz = Hoehe(x, z + e) - Hoehe(x, z - e);
            var len = Math.Sqrt(dx * dx + 4.0 * e * e + dz * dz);
            return (-dx / len, 2.0 * e / len, -dz / len);
        }

        /// <summary>Water here? Lake and bay under the water line, the brook where its notch is deep, the mountain lake.</summary>
        public static bool IstWasser(double x, double z)
        {
            var h = Hoehe(x, z);
            if (h < WasserHoehe) return true;
            if (BachKerbe(x, z) >= BachWasserAbM) return true;
            var bergsee = G().Bergsee;
            if (bergsee.HasValue && bergsee.Value.mitte.Abstand(x, z) < bergsee.Value.radius16) return h < bergsee.Value.wasser;
            return false;
        }

        /// <summary>The brook's water level at (x, z): ground + the rest of the notch just under the bank.</summary>
        public static double BachWasserspiegel(double x, double z) => Hoehe(x, z) + Math.Max(0.0, BachKerbe(x, z) - 0.45);

        /// <summary>The mountain lake's water level (−∞ without one).</summary>
        public static double BergseeWasser => G().Bergsee?.wasser ?? double.NegativeInfinity;

        public static double GrossformMaske(double x, double z)
        {
            var dx = Math.Max(Math.Max(KernRect.X - x, x - KernRect.EndX), 0.0);
            var dz = Math.Max(Math.Max(KernRect.Z - z, z - KernRect.EndZ), 0.0);
            var d = Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0.0) return 0.0;
            var t = Clamp01(d / GrossformRampeM);
            return t * t * (3.0 - 2.0 * t);
        }

        public static double BachKerbe(double x, double z)
        {
            var geo = G();
            var d = StreckenAbstand(x, z, geo.Bach);
            if (d >= geo.BachHalb) return 0.0;
            var tiefe = geo.BachTiefe;
            var furtD = geo.BachFurt.Abstand(x, z);
            if (furtD < FurtRadiusM) tiefe = Lerp(0.55, tiefe, furtD / FurtRadiusM);
            var t = 1.0 - d / geo.BachHalb;
            return tiefe * t * t * (3.0 - 2.0 * t);
        }

        public static double SchluchtKerbe(double x, double z)
        {
            if (z > -560.0) return 0.0;
            var geo = G();
            if (geo.Schlucht.Count == 0) return 0.0;
            var d = StreckenAbstand(x, z, geo.Schlucht);
            if (d >= geo.SchluchtHalb) return 0.0;
            var wand = Clamp01((1.0 - d / geo.SchluchtHalb) * 1.55);
            return geo.SchluchtTiefe * wand * wand * (3.0 - 2.0 * wand);
        }

        // ── building blocks ──

        private static double Grundhuegel(double x, double z)
        {
            var a = Math.Sin(x * 0.011 + 1.7) * Math.Cos(z * 0.009 + 0.4) * 2.2;
            var b = Math.Sin(x * 0.027 - 0.8) * Math.Sin(z * 0.023 + 2.1) * 0.9;
            return a + b;
        }

        private static double Grossformen(double x, double z)
        {
            var maske = GrossformMaske(x, z);
            if (maske <= 0.0) return 0.0;
            var a = Math.Sin(x * 0.004 + 2.1) * Math.Cos(z * 0.0035 - 1.3);
            var b = 0.4 * Math.Sin((x + z) * 0.006 - 0.5);
            return (a + b) * GrossformAmpM * maske;
        }

        private static double ZonenFeatures(double x, double z, Geo geo)
        {
            var h = 0.0;
            foreach (var f in geo.Features)
            {
                switch (f.Typ)
                {
                    case FeatureTyp.Kamm:
                        h += 24.0 * Math.Exp(-Math.Pow(AbstandZuStrecke(x, z, f.A, f.B) / 130.0, 2.0));
                        break;
                    case FeatureTyp.See:
                        h += -7.0 * Math.Exp(-Math.Pow(f.A.Abstand(x, z) / f.R, 2.0));
                        break;
                    case FeatureTyp.Tal:
                        h += -3.2 * Math.Exp(-Math.Pow(f.A.Abstand(x, z) / 190.0, 2.0));
                        break;
                    case FeatureTyp.Berg:
                        h += Bergmassiv(x, z);
                        break;
                    case FeatureTyp.Ruine:
                        h += 14.0 * Math.Exp(-Math.Pow(f.A.Abstand(x, z) / 120.0, 2.0));
                        break;
                    case FeatureTyp.Bucht:
                        h += -8.0 * Math.Exp(-Math.Pow(f.A.Abstand(x, z) / f.R, 2.0));
                        break;
                    case FeatureTyp.Moor:
                        h += MoorWanne(x, z, f.Rect);
                        break;
                }
            }
            return h;
        }

        private static double Bergmassiv(double x, double z)
        {
            if (z > -560.0) return 0.0;
            var h = 0.0;
            foreach (var g in BergGrate)
                h += g[4] * Math.Exp(-Math.Pow(AbstandZuStrecke(x, z, new V2d(g[0], g[1]), new V2d(g[2], g[3])) / g[5], 2.0));
            var vd = new V2d(Vorberg[0], Vorberg[1]).Abstand(x, z);
            h += Vorberg[2] * Math.Exp(-Math.Pow(vd / Vorberg[3], 2.0));
            return h;
        }

        private static double MoorWanne(double x, double z, RanchKarte.Rect rect)
        {
            if (!rect.Enthaelt(x, z)) return 0.0;
            var tiefeX = Math.Min(x - rect.X, rect.EndX - x);
            var tiefeZ = Math.Min(z - rect.Z, rect.EndZ - z);
            var w = Clamp01(Math.Min(tiefeX, tiefeZ) / 45.0);
            return -MoorWanneM * w * w * (3.0 - 2.0 * w);
        }

        private static double Plateaus(double x, double z, double h, Geo geo)
        {
            var out_ = h;
            foreach (var (rect, basis) in geo.RectPlateaus)
            {
                var gewicht = Clamp01(1.0 - rect.Abstand(x, z) / PlateauRandM);
                if (gewicht > 0.0) out_ = Lerp(out_, basis, gewicht);
            }
            if (geo.BergPlateau.HasValue)
            {
                var p = geo.BergPlateau.Value;
                var d = p.mitte.Abstand(x, z);
                if (d < p.radius + PlateauRandM)
                {
                    var t = Clamp01((d - p.radius) / PlateauRandM);
                    out_ = Lerp(out_, p.basis, 1.0 - t * t * (3.0 - 2.0 * t));
                }
                var sd = p.see.Abstand(x, z);
                if (sd < p.seeRadius * 2.4) out_ -= 6.5 * Math.Exp(-Math.Pow(sd / 22.0, 2.0));
            }
            return out_;
        }

        private static double MoorPfannen(double x, double z, double h, Geo geo)
        {
            if (!geo.Moor.HasValue || !geo.Moor.Value.rect.Enthaelt(x, z)) return h;
            var out_ = h;
            if (out_ < MoorPfanneM) out_ = MoorPfanneM - (MoorPfanneM - out_) * 0.12;
            foreach (var m in geo.Moor.Value.tuempel)
            {
                var d = m.Abstand(x, z);
                if (d >= TuempelRadiusM * 3.0) continue;
                var g = Math.Exp(-Math.Pow(d / TuempelRadiusM, 2.0));
                out_ = Lerp(out_, TuempelZielM, Clamp01(g * 1.2));
            }
            return out_;
        }

        private static double BodenUndRand(double x, double z, double h, Geo geo)
        {
            var out_ = h;
            if (out_ < BodenMinM && !geo.Senken.Any(s => s.mitte.Abstand(x, z) < s.radius)) out_ = BodenMinM;
            var g = geo.Grenzen;
            var rand = Math.Min(Math.Min(x - g.X, g.EndX - x), Math.Min(z - g.Z, g.EndZ - z));
            if (rand < RandBlendeM)
            {
                var t = Clamp01(rand / RandBlendeM);
                out_ = Lerp(RandZielM, out_, t * t * (3.0 - 2.0 * t));
            }
            return out_;
        }

        // ── the map, once, as typed descriptors ──

        private static Geo G()
        {
            if (_geo != null) return _geo;
            var geo = new Geo
            {
                Features = new List<Feature>(),
                RectPlateaus = new List<(RanchKarte.Rect, double)>(),
                Senken = new List<(V2d, double)>(),
                Grenzen = RanchKarte.Grenzen,
                Bach = new List<Strecke>(),
                Schlucht = new List<Strecke>(),
                Wege = new List<Strecke>(),
            };
            foreach (var zone in RanchKarte.Zonen)
            {
                switch (zone.Id)
                {
                    case "huegelkamm":
                        var spitze = zone.Punkt("aussichtspunkt");
                        geo.Features.Add(new Feature { Typ = FeatureTyp.Kamm, A = new V2d(spitze.X - 140.0, spitze.Z + 220.0), B = new V2d(spitze.X + 120.0, spitze.Z - 60.0) });
                        break;
                    case "see":
                        geo.Features.Add(new Feature { Typ = FeatureTyp.See, A = zone.Punkt("see_mitte"), R = zone.Zahl("see_radius") * 0.9 });
                        geo.Senken.Add((zone.Punkt("see_mitte"), 220.0));
                        break;
                    case "weidetal":
                        geo.Features.Add(new Feature { Typ = FeatureTyp.Tal, A = zone.Rect.Mitte });
                        break;
                    case "bergmassiv":
                        geo.Features.Add(new Feature { Typ = FeatureTyp.Berg });
                        geo.BergPlateau = (zone.Punkt("plateau_mitte"), zone.Zahl("plateau_radius"), zone.Daten?["hoehe_basis"] != null ? zone.HoeheBasis : 62.0, zone.Punkt("bergsee_mitte"), zone.Zahl("bergsee_radius"));
                        geo.Bergsee = (zone.Punkt("bergsee_mitte"), zone.Zahl("bergsee_radius") * 1.6, zone.Zahl("bergsee_wasser"));
                        break;
                    case "ruine":
                        geo.Features.Add(new Feature { Typ = FeatureTyp.Ruine, A = zone.Punkt("turm") });
                        break;
                    case "strand":
                        geo.Features.Add(new Feature { Typ = FeatureTyp.Bucht, A = zone.Punkt("bucht_mitte"), R = zone.Zahl("bucht_radius") * 0.9 });
                        geo.Senken.Add((zone.Punkt("bucht_mitte"), 160.0));
                        break;
                    case "moor":
                        geo.Features.Add(new Feature { Typ = FeatureTyp.Moor, Rect = zone.Rect });
                        geo.Moor = (zone.Rect, RanchKarte.Punkte(zone.Daten?["tuempel"]));
                        break;
                }
                if (zone.Id == "hof" || zone.Id == "turnierplatz" || zone.Id == "hufingen") geo.RectPlateaus.Add((zone.Rect, zone.HoeheBasis));
            }
            var bach = RanchKarte.Bach;
            geo.BachHalb = J.NumOr(bach["breite"], 6.0) / 2.0 + 2.0;
            geo.BachTiefe = J.NumOr(bach["tiefe"], 1.0);
            geo.BachFurt = RanchKarte.Furt;
            geo.Bach = Strecken(RanchKarte.BachPunkte, 0.0, geo.BachHalb + 1.0);
            if (RanchKarte.Daten["schlucht"] is JObject schlucht)
            {
                geo.SchluchtHalb = J.NumOr(schlucht["breite"], 20.0) / 2.0 + 2.0;
                geo.SchluchtTiefe = J.NumOr(schlucht["tiefe"], 10.0);
                geo.Schlucht = Strecken(RanchKarte.Punkte(schlucht["punkte"]), 0.0, geo.SchluchtHalb + 1.0);
            }
            foreach (var w in RanchKarte.Wege)
            {
                var halb = w.Breite / 2.0 + 1.0;
                geo.Wege.AddRange(Strecken(w.Punkte, halb, halb + WegGlaettRandM));
            }
            _geo = geo;
            return geo;
        }

        private static List<(V2d a, V2d b, double halb, double ya, double yb, double minX, double maxX, double minZ, double maxZ)> Decks()
        {
            var geo = G();
            if (geo.Decks != null) return geo.Decks;
            geo.Decks = new List<(V2d, V2d, double, double, double, double, double, double, double)>();
            foreach (var e in (RanchKarte.Daten["bruecken"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var pts = RanchKarte.Punkte(new JArray(e["a"], e["b"]));
                if (pts.Count < 2) continue;
                var a = pts[0];
                var b = pts[1];
                var halb = J.NumOr(e["breite"], 4.0) / 2.0;
                geo.Decks.Add((a, b, halb, Hoehe(a.X, a.Z), Hoehe(b.X, b.Z),
                    Math.Min(a.X, b.X) - halb, Math.Max(a.X, b.X) + halb, Math.Min(a.Z, b.Z) - halb, Math.Max(a.Z, b.Z) + halb));
            }
            return geo.Decks;
        }

        /// <summary>The bridge decks (a, b, half width) — the scene builds the planks there.</summary>
        public static List<(V2d a, V2d b, double halb)> Bruecken() => Decks().Select(d => (d.a, d.b, d.halb)).ToList();

        private static List<Strecke> Strecken(List<V2d> punkte, double halb, double rand)
        {
            var out_ = new List<Strecke>();
            for (var i = 0; i + 1 < punkte.Count; i++)
            {
                var a = punkte[i];
                var b = punkte[i + 1];
                out_.Add(new Strecke
                {
                    A = a, B = b, Halb = halb,
                    MinX = Math.Min(a.X, b.X) - rand, MaxX = Math.Max(a.X, b.X) + rand,
                    MinZ = Math.Min(a.Z, b.Z) - rand, MaxZ = Math.Max(a.Z, b.Z) + rand,
                });
            }
            return out_;
        }

        private static double StreckenAbstand(double x, double z, List<Strecke> s)
        {
            var best = double.PositiveInfinity;
            foreach (var st in s)
            {
                if (x < st.MinX || x > st.MaxX || z < st.MinZ || z > st.MaxZ) continue;
                best = Math.Min(best, AbstandZuStrecke(x, z, st.A, st.B));
            }
            return best;
        }

        public static double AbstandZuStrecke(double x, double z, V2d a, V2d b)
        {
            var abx = b.X - a.X;
            var abz = b.Z - a.Z;
            var l2 = abx * abx + abz * abz;
            if (l2 <= 0.0001) return a.Abstand(x, z);
            var t = Clamp01(((x - a.X) * abx + (z - a.Z) * abz) / l2);
            var px = a.X + abx * t;
            var pz = a.Z + abz * t;
            return Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
