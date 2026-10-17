using System;

namespace Gooby.Core.Time
{
    /// <summary>
    /// Time of day as the world sees it: a named phase for strings and rules, and a continuous 0..1 "daylight"
    /// curve (0 at night, 1 at noon) the renderer blends sun, sky and lamps with. Local hours come from
    /// GameClock.LocalDateTime, so a pinned or offset clock moves the sun along with it.
    /// </summary>
    public static class DayPhase
    {
        public const string Morgen = "morgen", Tag = "tag", Abend = "abend", Nacht = "nacht";

        /// <summary>Phase by local hour: 6–10 morning, 10–17 day, 17–21 evening, otherwise night.</summary>
        public static string Of(double hour)
        {
            var h = Wrap(hour);
            if (h >= 6.0 && h < 10.0) return Morgen;
            if (h >= 10.0 && h < 17.0) return Tag;
            if (h >= 17.0 && h < 21.0) return Abend;
            return Nacht;
        }

        /// <summary>
        /// Daylight 0..1: full night until 5:00, dawn ramps to 1 by 8:00, dusk fades from 18:00 to 0 at 21:30.
        /// Smoothstepped so the sun never jumps between frames.
        /// </summary>
        public static double Daylight(double hour)
        {
            var h = Wrap(hour);
            if (h < 5.0 || h >= 21.5) return 0.0;
            if (h < 8.0) return Smooth((h - 5.0) / 3.0);
            if (h < 18.0) return 1.0;
            return 1.0 - Smooth((h - 18.0) / 3.5);
        }

        /// <summary>
        /// How "golden" the light is: peaks at dawn (7:00), Board 03's afternoon (16:24) and dusk (19:00).
        /// Noon stays near-white so midday screenshots do not go amber; SceneDressing maps the curve onto
        /// the ColorAdjustments filter.
        /// </summary>
        public static double Warmth(double hour)
        {
            var h = Wrap(hour);
            var d = Daylight(h);
            if (d <= 0.0) return 0.0;
            var dawn = Math.Exp(-Math.Pow((h - 7.0) / 1.6, 2));
            var afternoon = Math.Exp(-Math.Pow((h - 16.4) / 1.5, 2));
            var dusk = Math.Exp(-Math.Pow((h - 19.0) / 1.8, 2));
            return Math.Max(Math.Max(dawn, dusk), afternoon * 0.55) * d;
        }

        /// <summary>Street lamps and the garden's string lights are on from dusk to dawn.</summary>
        public static bool LampsOn(double hour) => Daylight(hour) < 0.6;

        /// <summary>Local hour with fraction for an epoch-ms and UTC bias in minutes.</summary>
        public static double LocalHour(long nowMs, int biasMinutes)
        {
            var secs = nowMs / 1000.0 + biasMinutes * 60.0;
            var dayStart = Math.Floor(secs / 86400.0) * 86400.0;
            return (secs - dayStart) / 3600.0;
        }

        private static double Wrap(double hour)
        {
            var h = hour % 24.0;
            return h < 0 ? h + 24.0 : h;
        }

        private static double Smooth(double t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
