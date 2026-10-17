using System;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Staging a strong feeling (port of moment_regie.gd + the "stark" flags and colours of feel_emotions.gd): fright,
    /// delight, pride and love get a short punch-in of the camera on Gooby, a breath of slow motion and a colour accent
    /// — sparingly, at most every 45 s, so it stays special. Reduced motion keeps only the colour accent. Pure timeline;
    /// the world applies it to camera, time scale and grade and restores all three exactly.
    /// </summary>
    public static class MomentRegie
    {
        public const double CooldownS = 45.0;
        public const double ZoomFaktor = 0.82;
        public const double ZoomInS = 0.22;
        public const double HaltenS = 0.55;
        public const double ZoomOutS = 0.45;
        /// <summary>Time runs at 55 % for the first 0.4 s (unscaled), then normal again.</summary>
        public const double Zeitlupe = 0.55;
        public const double ZeitlupeS = 0.4;
        /// <summary>How far the camera turns towards Gooby at the peak of the punch-in (0 = not at all, 1 = straight at him).</summary>
        public const double Blick = 0.35;

        public static double DauerS => ZoomInS + HaltenS + ZoomOutS;

        public static bool Stark(string emotion) => emotion == "schreck" || emotion == "begeisterung" || emotion == "stolz" || emotion == "verliebtheit";

        /// <summary>The accent colour of an emotion (feel_emotions.gd "farbe").</summary>
        public static string Farbe(string emotion)
        {
            switch (emotion)
            {
                case "schreck": return "#CFE0FF";
                case "begeisterung": return "#FFE98A";
                case "stolz": return "#FFD166";
                case "verliebtheit": return "#FF6FA0";
                default: return "#FFF3B0";
            }
        }

        public static bool Bereit(double letzteS, double jetztS) => jetztS - letzteS >= CooldownS;

        /// <summary>Weight of the punch-in at <paramref name="t"/> s: 0 → 1 (ease-out cubic), held, back to 0 (ease-in-out).</summary>
        public static double Gewicht(double t)
        {
            if (t <= 0.0 || t >= DauerS) return 0.0;
            if (t < ZoomInS)
            {
                var u = t / ZoomInS;
                return 1.0 - Math.Pow(1.0 - u, 3);
            }
            if (t < ZoomInS + HaltenS) return 1.0;
            var v = (t - ZoomInS - HaltenS) / ZoomOutS;
            return 1.0 - (v < 0.5 ? 4 * v * v * v : 1 - Math.Pow(-2 * v + 2, 3) / 2);
        }

        /// <summary>Field-of-view factor at <paramref name="t"/>: 1 at rest, <see cref="ZoomFaktor"/> at the peak.</summary>
        public static double Zoom(double t) => 1.0 + (ZoomFaktor - 1.0) * Gewicht(t);

        /// <summary>Time scale at <paramref name="t"/> unscaled seconds into the moment.</summary>
        public static double Zeitskala(double t) => t >= 0.0 && t < ZeitlupeS ? Zeitlupe : 1.0;
    }
}
