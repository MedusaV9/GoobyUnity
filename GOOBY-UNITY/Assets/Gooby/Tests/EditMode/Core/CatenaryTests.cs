using Gooby.Core.Home;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    public class CatenaryTests
    {
        private const double Eps = 1e-9;

        [Test]
        public void ShapeIsZeroAtTheEndsAndOneInTheMiddle()
        {
            Assert.That(Catenary.Shape(0.0), Is.EqualTo(0.0).Within(Eps));
            Assert.That(Catenary.Shape(1.0), Is.EqualTo(0.0).Within(Eps));
            Assert.That(Catenary.Shape(0.5), Is.EqualTo(1.0).Within(Eps));
            Assert.That(Catenary.Shape(0.25), Is.EqualTo(Catenary.Shape(0.75)).Within(Eps), "symmetric");
            Assert.That(Catenary.Shape(-1.0), Is.EqualTo(0.0).Within(Eps), "clamped");
        }

        [Test]
        public void SagGrowsWithTheSpanAndIsCapped()
        {
            Assert.That(Catenary.Sag(0.0), Is.EqualTo(0.1).Within(Eps));
            Assert.That(Catenary.Sag(2.0), Is.EqualTo(0.34).Within(Eps));
            Assert.That(Catenary.Sag(4.0), Is.GreaterThan(Catenary.Sag(2.0)));
            Assert.That(Catenary.Sag(50.0), Is.EqualTo(Catenary.SagMax).Within(Eps));
        }

        [Test]
        public void PointsHitBothEndsAndHangLowestInTheMiddle()
        {
            var p = Catenary.Points((0.0, 1.6, 2.0), (3.0, 1.6, 2.0), 10, 0.2);
            Assert.That(p.Length, Is.EqualTo(11));
            Assert.That(p[0], Is.EqualTo((0.0, 1.6, 2.0)));
            Assert.That(p[10].x, Is.EqualTo(3.0).Within(Eps));
            Assert.That(p[10].y, Is.EqualTo(1.6).Within(Eps));
            Assert.That(p[5].y, Is.EqualTo(1.4).Within(Eps), "the full sag at the middle");
            Assert.That(p[3].y, Is.LessThan(1.6));
            Assert.That(p[3].y, Is.GreaterThan(p[5].y));
            var auto = Catenary.Points((0.0, 2.45, 0.0), (0.0, 2.45, 2.0), 4);
            Assert.That(auto[2].y, Is.EqualTo(2.45 - Catenary.Sag(2.0)).Within(Eps), "negative sag: from the distance");
            Assert.That(Catenary.Points((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), 0).Length, Is.EqualTo(2), "at least one segment");
        }
    }
}
