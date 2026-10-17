using System;

namespace Gooby.Core.Home
{
    /// <summary>
    /// A chain hung between two points (port of catenary_logic.gd): it sags as a catenary (cosh), normalised so both
    /// ends are hit exactly and the sag is given in metres. The garden's clothesline and the garlands under a ceiling.
    /// </summary>
    public static class Catenary
    {
        /// <summary>Shape factor of the curve (larger = a rounder belly).</summary>
        public const double Form = 2.4;
        /// <summary>Automatic sag: a base plus a share of the span, capped (more than ~0.9 m reads as a washing line).</summary>
        public const double SagBase = 0.1, SagShare = 0.12, SagMax = 0.9;

        /// <summary>Sag in metres for a span in metres; grows with the distance.</summary>
        public static double Sag(double distance) => Math.Min(SagBase + SagShare * Math.Max(distance, 0.0), SagMax);

        /// <summary>Normalised hanging shape: 0 at both ends (t = 0 and 1), 1 at the lowest point (t = 0.5).</summary>
        public static double Shape(double t)
        {
            var tc = Math.Max(0.0, Math.Min(1.0, t));
            var rim = Math.Cosh(Form * 0.5);
            return (rim - Math.Cosh(Form * (tc - 0.5))) / (rim - 1.0);
        }

        /// <summary>
        /// The points from <paramref name="a"/> to <paramref name="b"/>, both ends included: <paramref name="segments"/>
        /// pieces (at least one) give segments + 1 points. A negative <paramref name="sag"/> takes it from the distance.
        /// </summary>
        public static (double x, double y, double z)[] Points((double x, double y, double z) a, (double x, double y, double z) b, int segments, double sag = -1.0)
        {
            var n = Math.Max(1, segments);
            var dx = b.x - a.x; var dy = b.y - a.y; var dz = b.z - a.z;
            var s = sag >= 0.0 ? sag : Sag(Math.Sqrt(dx * dx + dy * dy + dz * dz));
            var points = new (double x, double y, double z)[n + 1];
            for (var i = 0; i <= n; i++)
            {
                var t = (double)i / n;
                points[i] = (a.x + dx * t, a.y + dy * t - s * Shape(t), a.z + dz * t);
            }
            return points;
        }
    }
}
