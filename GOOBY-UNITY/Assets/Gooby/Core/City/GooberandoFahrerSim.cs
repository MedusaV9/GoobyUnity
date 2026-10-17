using System;
using System.Collections.Generic;

namespace Gooby.Core.City
{
    /// <summary>
    /// Deterministic GOOBERANDO driver (port of fahrer_sim.gd): A* / BFS on the street lattice from the kitchen
    /// street to home, 8 m/s, phases kueche / unterwegs / da. Position is a pure function of the save timestamps.
    /// </summary>
    public static class GooberandoFahrerSim
    {
        public const double TempoMs = 8.0;
        public const string PhaseKueche = "kueche";
        public const string PhaseUnterwegs = "unterwegs";
        public const string PhaseDa = "da";

        public sealed class Point
        {
            public double X, Z;
        }

        public sealed class Status
        {
            public string Phase;
            public double X, Z, Dx, Dz, Fortschritt;
        }

        /// <summary>World polyline of tile centres along the street path restaurant → home. Empty when no lattice.</summary>
        public static List<Point> RouteWorld((int r, int c) start, (int r, int c) ziel)
        {
            var tiles = CityGrid.StreetPath(start, ziel);
            var pts = new List<Point>(tiles.Count);
            foreach (var t in tiles)
            {
                var (x, z) = CityGrid.World(t.r, t.c);
                pts.Add(new Point { X = x, Z = z });
            }
            return pts;
        }

        public static double PolylineLength(IReadOnlyList<Point> route)
        {
            var len = 0.0;
            for (var i = 1; i < route.Count; i++)
            {
                var dx = route[i].X - route[i - 1].X; var dz = route[i].Z - route[i - 1].Z;
                len += Math.Sqrt(dx * dx + dz * dz);
            }
            return len;
        }

        public static double FahrzeitS(IReadOnlyList<Point> route) => PolylineLength(route) / TempoMs;

        /// <summary>Departure ms: as late as possible so arrival hits fertigAt — never before the order.</summary>
        public static long AbfahrtMs(long bestelltMs, long fertigMs, IReadOnlyList<Point> route) =>
            Math.Max(bestelltMs, fertigMs - (long)(FahrzeitS(route) * 1000.0));

        public static Status At(IReadOnlyList<Point> route, long bestelltMs, long fertigMs, long nowMs)
        {
            if (route == null || route.Count == 0)
            {
                var da = nowMs >= fertigMs;
                return new Status { Phase = da ? PhaseDa : PhaseKueche, Fortschritt = da ? 1.0 : 0.0, Dx = 1, Dz = 0 };
            }
            var los = AbfahrtMs(bestelltMs, fertigMs, route);
            var first = route[0]; var last = route[route.Count - 1];
            if (nowMs <= los)
            {
                Dir(route, 0, out var dx, out var dz);
                return new Status { Phase = PhaseKueche, X = first.X, Z = first.Z, Dx = dx, Dz = dz, Fortschritt = 0 };
            }
            if (nowMs >= fertigMs || fertigMs <= los)
            {
                Dir(route, route.Count - 2, out var dx, out var dz);
                return new Status { Phase = PhaseDa, X = last.X, Z = last.Z, Dx = dx, Dz = dz, Fortschritt = 1 };
            }
            var fortschritt = (nowMs - los) / (double)(fertigMs - los);
            var bei = PunktBeiLaenge(route, fortschritt * PolylineLength(route));
            return new Status { Phase = PhaseUnterwegs, X = bei.x, Z = bei.z, Dx = bei.dx, Dz = bei.dz, Fortschritt = fortschritt };
        }

        private static (double x, double z, double dx, double dz) PunktBeiLaenge(IReadOnlyList<Point> route, double along)
        {
            var acc = 0.0;
            for (var i = 1; i < route.Count; i++)
            {
                var a = route[i - 1]; var b = route[i];
                var dx = b.X - a.X; var dz = b.Z - a.Z;
                var seg = Math.Sqrt(dx * dx + dz * dz);
                if (seg <= 1e-9) continue;
                if (acc + seg >= along || i == route.Count - 1)
                {
                    var f = Math.Min(1.0, Math.Max(0.0, (along - acc) / seg));
                    return (a.X + dx * f, a.Z + dz * f, dx / seg, dz / seg);
                }
                acc += seg;
            }
            var last = route[route.Count - 1];
            return (last.X, last.Z, 1, 0);
        }

        private static void Dir(IReadOnlyList<Point> route, int from, out double dx, out double dz)
        {
            dx = 1; dz = 0;
            if (from < 0 || from + 1 >= route.Count) return;
            dx = route[from + 1].X - route[from].X;
            dz = route[from + 1].Z - route[from].Z;
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dz) < 1e-9) { dx = 1; dz = 0; }
        }
    }
}
