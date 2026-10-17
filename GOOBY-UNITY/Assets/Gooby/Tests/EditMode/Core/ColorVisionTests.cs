using Gooby.Core.Meta;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The colour-vision aid: each mode's folded matrix equals the Godot shader step by step, greys stay grey.</summary>
    public class ColorVisionTests
    {
        /// <summary>color_filter.gd's fragment shader, one step after the other (no clamping).</summary>
        private static double[] Shader(int mode, double r, double g, double b)
        {
            var l = 17.8824 * r + 43.5161 * g + 4.11935 * b;
            var m = 3.45565 * r + 27.1554 * g + 3.86714 * b;
            var s = 0.0299566 * r + 0.184309 * g + 1.46709 * b;
            double sl = l, sm = m, ss = s;
            if (mode == 1) sl = 2.02344 * m - 2.52581 * s;
            else if (mode == 2) sm = 0.494207 * l + 1.24827 * s;
            else ss = -0.395913 * l + 0.801109 * m;
            var simR = 0.0809444479 * sl - 0.130504409 * sm + 0.116721066 * ss;
            var simG = -0.0102485335 * sl + 0.0540193266 * sm - 0.113614708 * ss;
            var simB = -0.000365296938 * sl - 0.00412161469 * sm + 0.693511405 * ss;
            double er = r - simR, eg = g - simG, eb = b - simB;
            return new[] { r, g + 0.7 * er + eg, b + 0.7 * er + eb };
        }

        [TestCase(1, ColorVision.Protan)]
        [TestCase(2, ColorVision.Deutan)]
        [TestCase(3, ColorVision.Tritan)]
        public void TheFoldedMatrixIsTheShader(int index, string mode)
        {
            var mat = ColorVision.Matrix(mode);
            foreach (var (r, g, b) in new[] { (0.9, 0.2, 0.15), (0.2, 0.7, 0.3), (0.1, 0.3, 0.8), (0.55, 0.5, 0.2) })
            {
                var want = Shader(index, r, g, b);
                Assert.That(mat[0] * r + mat[1] * g + mat[2] * b, Is.EqualTo(want[0]).Within(1e-9));
                Assert.That(mat[3] * r + mat[4] * g + mat[5] * b, Is.EqualTo(want[1]).Within(1e-9));
                Assert.That(mat[6] * r + mat[7] * g + mat[8] * b, Is.EqualTo(want[2]).Within(1e-9));
            }
        }

        [Test]
        public void OffIsTheIdentityAndUnknownModesAreOff()
        {
            Assert.That(ColorVision.Matrix(ColorVision.Aus), Is.EqualTo(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }));
            Assert.That(ColorVision.Normalize("rot"), Is.EqualTo(ColorVision.Aus));
            Assert.That(ColorVision.Active(ColorVision.Aus, false), Is.False);
            Assert.That(ColorVision.Active(ColorVision.Aus, true), Is.True, "high contrast alone switches the filter on");
            Assert.That(ColorVision.Active(ColorVision.Deutan, false), Is.True);
            Assert.That(ColorVision.Apply(ColorVision.Aus, false, 0.3, 0.6, 0.9), Is.EqualTo((0.3, 0.6, 0.9)));
        }

        [TestCase(ColorVision.Protan)]
        [TestCase(ColorVision.Deutan)]
        [TestCase(ColorVision.Tritan)]
        public void GreysStayGreyAndRedTurnsDistinct(string mode)
        {
            foreach (var v in new[] { 0.2, 0.5, 0.8 })
            {
                var (r, g, b) = ColorVision.Apply(mode, false, v, v, v);
                Assert.That(r, Is.EqualTo(v).Within(0.01));
                Assert.That(g, Is.EqualTo(v).Within(0.01));
                Assert.That(b, Is.EqualTo(v).Within(0.01));
            }
            var red = ColorVision.Apply(mode, false, 0.85, 0.2, 0.2);
            var green = ColorVision.Apply(mode, false, 0.2, 0.75, 0.2);
            if (mode != ColorVision.Tritan)
                Assert.That(red.b - green.b, Is.GreaterThan(0.1), "the red/green difference a protan or deutan eye loses comes back as blue");
        }

        [Test]
        public void HighContrastPushesAwayFromMidGrey()
        {
            var (r, g, b) = ColorVision.Apply(ColorVision.Aus, true, 0.25, 0.5, 0.9);
            Assert.That(r, Is.EqualTo(0.195).Within(1e-9));
            Assert.That(g, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(b, Is.EqualTo(0.988).Within(1e-9));
        }
    }
}
