using System;

namespace Gooby.Core.Meta
{
    /// <summary>
    /// The colour-vision aid of the settings (port of color_filter.gd): daltonisation for protan, deutan and tritan
    /// colour blindness after Fidaner/Viénot — the deficit is simulated in LMS cone space and the lost difference is
    /// shifted onto the channels that are still seen — plus a high-contrast boost around mid grey. Every step is
    /// linear, so each mode folds into one 3×3 matrix the renderer applies to the 3D image and the UI alike.
    /// An aid only: shapes, icons and text stay the game's primary code.
    /// </summary>
    public static class ColorVision
    {
        public const string Aus = "aus";
        public const string Protan = "protan";
        public const string Deutan = "deutan";
        public const string Tritan = "tritan";
        public static readonly string[] Modi = { Aus, Protan, Deutan, Tritan };

        /// <summary>The contrast factor of "Hoher Kontrast" (color_filter.gd: 1.22 around 0.5).</summary>
        public const double HighContrast = 1.22;

        // RGB → LMS and back (color_filter.gd, the matrices of the GLSL shader written row by row)
        private static readonly double[] RgbToLms =
        {
            17.8824, 43.5161, 4.11935,
            3.45565, 27.1554, 3.86714,
            0.0299566, 0.184309, 1.46709,
        };

        private static readonly double[] LmsToRgb =
        {
            0.0809444479, -0.130504409, 0.116721066,
            -0.0102485335, 0.0540193266, -0.113614708,
            -0.000365296938, -0.00412161469, 0.693511405,
        };

        public static string Normalize(string mode) => Array.IndexOf(Modi, mode) >= 0 ? mode : Aus;

        public static bool Active(string mode, bool highContrast) => Normalize(mode) != Aus || highContrast;

        /// <summary>The daltonising matrix of a mode, row-major (out = M · rgb); the identity for "aus".</summary>
        public static double[] Matrix(string mode)
        {
            mode = Normalize(mode);
            var id = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            if (mode == Aus) return id;
            // simulated cone response: the missing cone is rebuilt from the other two
            var sim = (double[])id.Clone();
            if (mode == Protan) { sim[0] = 0; sim[1] = 2.02344; sim[2] = -2.52581; }
            else if (mode == Deutan) { sim[3] = 0.494207; sim[4] = 0; sim[5] = 1.24827; }
            else { sim[6] = -0.395913; sim[7] = 0.801109; sim[8] = 0; }
            var seen = Mul(LmsToRgb, Mul(sim, RgbToLms));
            // the error the eye misses, shifted onto green and blue (shifted = (0, 0.7·e.r + e.g, 0.7·e.r + e.b))
            var shift = new double[] { 0, 0, 0, 0.7, 1, 0, 0.7, 0, 1 };
            var error = new double[9];
            for (var i = 0; i < 9; i++) error[i] = id[i] - seen[i];
            var shiftErr = Mul(shift, error);
            var outp = new double[9];
            for (var i = 0; i < 9; i++) outp[i] = id[i] + shiftErr[i];
            return outp;
        }

        /// <summary>One colour through the aid: the mode's matrix, clamped, then the contrast boost (as the Godot shader).</summary>
        public static (double r, double g, double b) Apply(string mode, bool highContrast, double r, double g, double b)
        {
            var m = Matrix(mode);
            var o = new[]
            {
                Clamp01(m[0] * r + m[1] * g + m[2] * b),
                Clamp01(m[3] * r + m[4] * g + m[5] * b),
                Clamp01(m[6] * r + m[7] * g + m[8] * b),
            };
            var k = highContrast ? HighContrast : 1.0;
            return (Clamp01((o[0] - 0.5) * k + 0.5), Clamp01((o[1] - 0.5) * k + 0.5), Clamp01((o[2] - 0.5) * k + 0.5));
        }

        private static double[] Mul(double[] a, double[] b)
        {
            var c = new double[9];
            for (var r = 0; r < 3; r++)
            for (var col = 0; col < 3; col++)
                c[r * 3 + col] = a[r * 3] * b[col] + a[r * 3 + 1] * b[3 + col] + a[r * 3 + 2] * b[6 + col];
            return c;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
