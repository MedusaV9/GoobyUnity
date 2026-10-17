using System;
using Gooby.Core.City;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The GPS wedge and the pin tap of the street drive — port of test_w13b_stadtleben.gd (same golden values).</summary>
    public class CityGpsTests
    {
        private const double W = 1000.0, H = 600.0, Rand = 36.0;

        [Test]
        public void WedgeSitsOnTheRightEdge()
        {
            var lage = CityGps.ChevronPlatzierung(W, H, 2000.0, 300.0, false, Rand);
            Assert.That(lage.Sichtbar, Is.True);
            Assert.That(lage.X, Is.EqualTo(1000.0 - Rand).Within(1e-4), "on the right edge");
            Assert.That(lage.Y, Is.EqualTo(300.0).Within(1e-4));
            Assert.That(lage.Winkel, Is.EqualTo(0.0).Within(1e-6), "pointing right");
        }

        [Test]
        public void WedgeSitsOnTheTopEdge()
        {
            var lage = CityGps.ChevronPlatzierung(W, H, 500.0, -100.0, false, Rand);
            Assert.That(lage.Sichtbar, Is.True);
            Assert.That(lage.X, Is.EqualTo(500.0).Within(1e-4));
            Assert.That(lage.Y, Is.EqualTo(Rand).Within(1e-4), "on the top edge");
            Assert.That(lage.Winkel, Is.EqualTo(-Math.PI / 2.0).Within(1e-6), "pointing up");
        }

        [Test]
        public void WedgeOnTheDiagonalHitsTheNearerEdge()
        {
            var lage = CityGps.ChevronPlatzierung(W, H, 1100.0, 900.0, false, Rand);
            Assert.That(lage.Sichtbar, Is.True);
            Assert.That(lage.X, Is.EqualTo(764.0).Within(1e-4));
            Assert.That(lage.Y, Is.EqualTo(564.0).Within(1e-4));
            Assert.That(lage.Winkel, Is.EqualTo(Math.PI / 4.0).Within(1e-6));
        }

        [Test]
        public void APlaceInViewNeedsNoWedge()
        {
            Assert.That(CityGps.ChevronPlatzierung(W, H, 500.0, 300.0, false, Rand).Sichtbar, Is.False);
        }

        [Test]
        public void BehindTheCameraIsMirroredAndAlwaysShown()
        {
            var lage = CityGps.ChevronPlatzierung(W, H, 700.0, 300.0, true, Rand);
            Assert.That(lage.Sichtbar, Is.True, "behind the camera a wedge is always needed");
            Assert.That(lage.X, Is.EqualTo(Rand).Within(1e-4), "on the left edge");
            Assert.That(lage.Y, Is.EqualTo(300.0).Within(1e-4));
            Assert.That(Math.Abs(lage.Winkel), Is.EqualTo(Math.PI).Within(1e-6), "pointing left");
            var mitte = CityGps.ChevronPlatzierung(W, H, W * 0.5, H * 0.5, true, Rand);
            Assert.That(mitte.Sichtbar, Is.True);
            Assert.That(mitte.Winkel, Is.EqualTo(Math.PI / 2.0).Within(1e-6), "fallback direction = down");
        }

        [Test]
        public void TapPicksTheNearestPin()
        {
            var pins = new[] { ("rehwei", 10.0, 10.0), ("post", 100.0, 10.0) };
            Assert.That(CityGps.NaechsterPin(pins, 12.0, 11.0, 18.0), Is.EqualTo("rehwei"));
            Assert.That(CityGps.NaechsterPin(pins, 96.0, 14.0, 18.0), Is.EqualTo("post"));
            Assert.That(CityGps.NaechsterPin(pins, 55.0, 10.0, 18.0), Is.EqualTo(""), "too far");
            var gleich = new[] { ("a", 10.0, 10.0), ("b", 10.0, 10.0) };
            Assert.That(CityGps.NaechsterPin(gleich, 10.0, 10.0, 18.0), Is.EqualTo("a"), "a tie goes to the first");
        }
    }
}
