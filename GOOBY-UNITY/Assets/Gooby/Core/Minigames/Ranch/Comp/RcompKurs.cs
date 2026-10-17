using System;
using System.Collections.Generic;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Course layouts of the competitions (comp_kurs.gd, RW-5) — pure, seeded geometry so driver and tests see the same
    /// courses. Coordinates are arena metres around the origin as (x, z) in float32 like Godot's Vector3: the sand ring
    /// spans ±30 × ±18 m, the meadow around it ±95 × ±70 m.
    /// </summary>
    public static class RcompKurs
    {
        public const float ArenaX = -30f, ArenaZ = -18f, ArenaW = 60f, ArenaT = 36f;
        public const float FeldX = -95f, FeldZ = -70f, FeldW = 190f, FeldT = 140f;

        /// <summary>Grass oval: straights at z = ±RennenRz, half circles around (±RennenRx, 0).</summary>
        public const double RennenRx = 26.0, RennenRz = 16.0;

        /// <summary>Cloverleaf: start south, right → left → back barrel.</summary>
        public static readonly V2 TonnenStart = new V2(0f, 13f);
        public static readonly V2[] TonnenPositionen = { new V2(9f, -2f), new V2(-9f, -2f), new V2(0f, -13f) };

        public sealed class TrailStation
        {
            public string Id;
            public V2 Pos;
            public double Winkel, Breite;
        }

        /// <summary>The six trail corridors in the sand ring; Breite = half clear width.</summary>
        public static readonly TrailStation[] TrailStationen =
        {
            new TrailStation { Id = "tor", Pos = new V2(-20f, 8f), Winkel = 0.0, Breite = 1.6 },
            new TrailStation { Id = "rueckwaerts_l", Pos = new V2(-10f, -8f), Winkel = 0.9, Breite = 1.3 },
            new TrailStation { Id = "slalom", Pos = new V2(0f, 8f), Winkel = 0.0, Breite = 1.1 },
            new TrailStation { Id = "bruecke", Pos = new V2(10f, -8f), Winkel = -0.9, Breite = 1.4 },
            new TrailStation { Id = "plane", Pos = new V2(20f, 8f), Winkel = 0.0, Breite = 1.6 },
            new TrailStation { Id = "kreisel", Pos = new V2(0f, 0f), Winkel = 0.5, Breite = 1.2 },
        };

        public const double TrailLaengeM = 6.0;

        /// <summary>A gate or obstacle: centre, cross axis (the line riders cross) and the heading through it.</summary>
        public struct Tor
        {
            public V2 Pos, Quer;
            public double Heading;
        }

        public sealed class Figur
        {
            public string Id, Gangart;
            public List<V2> Punkte;
        }

        /// <summary>Show jumping: obstacles in serpentine rows through the sand ring.</summary>
        public static List<Tor> Springen(string klasse)
        {
            var anzahl = RcompWertung.Springen.HindernisAnzahl(klasse);
            const int reihen = 3;
            var jeReihe = (int)Math.Ceiling((double)anzahl / reihen);
            var kurs = new List<Tor>();
            for (var i = 0; i < anzahl; i++)
            {
                var reihe = i / jeReihe;
                var spalte = i % jeReihe;
                var linksNachRechts = reihe % 2 == 0;
                var t = (spalte + 0.5) / jeReihe;
                var x = Lerp(-22.0, 22.0, linksNachRechts ? t : 1.0 - t);
                var z = Lerp(11.0, -11.0, reihe / Math.Max(1.0, reihen - 1.0));
                kurs.Add(new Tor { Pos = new V2(x, z), Quer = new V2(0f, 1f), Heading = linksNachRechts ? -Math.PI * 0.5 : Math.PI * 0.5 });
            }
            return kurs;
        }

        /// <summary>Cross-country: flag gates on a seeded ellipse over the meadow.</summary>
        public static List<Tor> Gelaende(string klasse, int seed)
        {
            var anzahl = RcompWertung.Gelaende.TorAnzahl(klasse);
            var rng = new GoobyRng(seed);
            var tore = new List<Tor>();
            for (var i = 0; i < anzahl; i++)
            {
                var winkel = 2.0 * Math.PI * (i + 0.5) / anzahl + Math.PI * 0.5;
                var rx = 66.0 + rng.Next() * 18.0;
                var rz = 44.0 + rng.Next() * 12.0;
                var pos = new V2(Math.Cos(winkel) * rx, Math.Sin(winkel) * rz);
                var tangente = new V2(-Math.Sin(winkel) * rx, Math.Cos(winkel) * rz).Normalized();
                tore.Add(new Tor { Pos = pos, Quer = new V2(-tangente.Y, tangente.X) });
            }
            return tore;
        }

        /// <summary>Time trial (arcade): zigzag gates from the south to the north rim of the meadow.</summary>
        public static List<Tor> ZeitRoute(int level, int seed)
        {
            var anzahl = Math.Max(7, Math.Min(16, 6 + level));
            var rng = new GoobyRng(seed + level * 977);
            var tore = new List<Tor>();
            var vorher = new V2(0f, 58f);
            for (var i = 0; i < anzahl; i++)
            {
                var t = (i + 1.0) / anzahl;
                var x = (rng.Next() * 2.0 - 1.0) * (26.0 + 26.0 * t);
                var z = Lerp(44.0, -58.0, t);
                var pos = new V2(x, z);
                var tangente = (pos - vorher).Normalized();
                tore.Add(new Tor { Pos = pos, Quer = new V2(-tangente.Y, tangente.X) });
                vorher = pos;
            }
            return tore;
        }

        public static double RennenUmfang() => 4.0 * RennenRx + 2.0 * Math.PI * RennenRz;

        /// <summary>Point of the oval at arc length s (counter-clockwise from the middle of the south straight).</summary>
        public static V2 RennenPunkt(double s)
        {
            var u = Fposmod(s, RennenUmfang());
            var gerade = 2.0 * RennenRx;
            var bogen = Math.PI * RennenRz;
            if (u < gerade * 0.5) return new V2(u, RennenRz);
            u -= gerade * 0.5;
            if (u < bogen)
            {
                var w = u / RennenRz;
                return new V2(RennenRx + Math.Sin(w) * RennenRz, Math.Cos(w) * RennenRz);
            }
            u -= bogen;
            if (u < gerade) return new V2(RennenRx - u, -RennenRz);
            u -= gerade;
            if (u < bogen)
            {
                var w = u / RennenRz;
                return new V2(-RennenRx - Math.Sin(w) * RennenRz, -Math.Cos(w) * RennenRz);
            }
            u -= bogen;
            return new V2(-RennenRx + u, RennenRz);
        }

        /// <summary>Arc length closest to a position, sampled every <paramref name="schritt"/> metres.</summary>
        public static double RennenSBei(V2 pos, double schritt = 1.0)
        {
            var besteS = 0.0;
            var besteD = double.PositiveInfinity;
            var umfang = RennenUmfang();
            for (var s = 0.0; s < umfang; s += schritt)
            {
                double d = (RennenPunkt(s) - pos).LengthSquared;
                if (d < besteD)
                {
                    besteD = d;
                    besteS = s;
                }
            }
            return besteS;
        }

        /// <summary>The dressage test: five figures with target gait and waypoints to touch in order.</summary>
        public static List<Figur> DressurFiguren()
        {
            var figuren = new List<Figur>();
            var zirkel = new List<V2>();
            for (var i = 0; i < 10; i++)
            {
                var w = 2.0 * Math.PI * i / 10.0;
                zirkel.Add(new V2(Math.Cos(w) * 9.0, Math.Sin(w) * 9.0));
            }
            figuren.Add(new Figur { Id = "zirkel", Gangart = "trab", Punkte = zirkel });
            var acht = new List<V2>();
            for (var i = 0; i < 8; i++)
            {
                var w = 2.0 * Math.PI * i / 8.0;
                acht.Add(new V2(-8.0 + Math.Cos(w) * 6.0, Math.Sin(w) * 6.0));
            }
            for (var i = 0; i < 8; i++)
            {
                var w = Math.PI - 2.0 * Math.PI * i / 8.0;
                acht.Add(new V2(8.0 + Math.Cos(w) * 6.0, Math.Sin(w) * 6.0));
            }
            figuren.Add(new Figur { Id = "acht", Gangart = "trab", Punkte = acht });
            var schlange = new List<V2>();
            for (var i = 0; i < 9; i++)
            {
                var t = i / 8.0;
                schlange.Add(new V2(Lerp(-20.0, 20.0, t), Math.Sin(t * Math.PI * 3.0) * 8.0));
            }
            figuren.Add(new Figur { Id = "schlangenlinie", Gangart = "galopp", Punkte = schlange });
            figuren.Add(new Figur { Id = "halten", Gangart = "schritt", Punkte = new List<V2> { new V2(20f, 8f), new V2(8f, 12f) } });
            figuren.Add(new Figur { Id = "rueckwaerts", Gangart = "schritt", Punkte = new List<V2> { new V2(-4f, 12f), new V2(-16f, 12f) } });
            return figuren;
        }

        /// <summary>Distance of a point to the segment a→b (deviation from the ideal line).</summary>
        public static double AbstandZurStrecke(V2 p, V2 a, V2 b)
        {
            var ab = b - a;
            var ap = p - a;
            double laenge2 = ab.LengthSquared;
            if (laenge2 <= 0.0001) return ap.Length;
            var t = Math.Max(0.0, Math.Min(1.0, ap.Dot(ab) / laenge2));
            return (ap - ab * t).Length;
        }

        /// <summary>Did the step vorher→jetzt cross the gate line (pos, quer) within half its width?</summary>
        public static bool TorDurchritten(V2 vorher, V2 jetzt, V2 pos, V2 quer, double halbeBreite)
        {
            var normale = new V2(-quer.Y, quer.X);
            var a = vorher - pos;
            var b = jetzt - pos;
            double da = a.Dot(normale);
            double db = b.Dot(normale);
            if (da == 0.0 && db == 0.0) return false;
            if (da < 0.0 == db < 0.0) return false;
            var t = da / (da - db);
            var schnitt = a.Lerp(b, (float)t);
            return Math.Abs(schnitt.Dot(quer)) <= halbeBreite;
        }

        public static bool InArena(V2 p) => p.X >= ArenaX && p.X <= ArenaX + ArenaW && p.Y >= ArenaZ && p.Y <= ArenaZ + ArenaT;

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double Fposmod(double x, double y)
        {
            var v = x % y;
            if ((v < 0.0 && y > 0.0) || (v > 0.0 && y < 0.0)) v += y;
            return v;
        }
    }
}
