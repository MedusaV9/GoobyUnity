using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of test_rwelt_tiere.gd.</summary>
    public class RanchWildLogikTests
    {
        [Test]
        public void BudgetHonoursLessLife()
        {
            var voll = RanchWildLogik.Budget(false);
            var reduziert = RanchWildLogik.Budget(true);
            foreach (var art in RanchWildLogik.Arten.Keys)
            {
                Assert.That(voll[art], Is.GreaterThanOrEqualTo(2), art + ": a group at full life");
                Assert.That(reduziert[art], Is.LessThan(voll[art]), art + ": fewer with less life");
                Assert.That(reduziert[art], Is.GreaterThanOrEqualTo(1), art + ": never empty");
            }
            Assert.That(reduziert["schmetterling"], Is.EqualTo(0), "swarms off with less life");
            Assert.That(reduziert["vogel"], Is.EqualTo(0));
            Assert.That(voll["gluehwuermchen"], Is.GreaterThan(0));
        }

        [Test]
        public void HourDecidesWhoIsOut()
        {
            Assert.That(RanchWildLogik.Aktiv("fuchs", 23.0, "sonne"), Is.True, "fox at night");
            Assert.That(RanchWildLogik.Aktiv("fuchs", 12.0, "sonne"), Is.False, "not at noon");
            Assert.That(RanchWildLogik.Aktiv("reh", 6.5, "sonne"), Is.True, "deer at dawn");
            Assert.That(RanchWildLogik.Aktiv("reh", 13.0, "sonne"), Is.False, "deer doze at noon");
            Assert.That(RanchWildLogik.Aktiv("gluehwuermchen", 22.0, "sonne"), Is.True);
            Assert.That(RanchWildLogik.Aktiv("gluehwuermchen", 12.0, "sonne"), Is.False);
            Assert.That(RanchWildLogik.Aktiv("ente", 3.0, "regen"), Is.True, "ducks always");
            Assert.That(RanchWildLogik.Aktiv("unbekannt", 12.0, "sonne"), Is.False);
        }

        [Test]
        public void WeatherSendsTheShyOnesHome()
        {
            Assert.That(RanchWildLogik.Aktiv("reh", 6.5, "regen"), Is.False, "deer hide in the rain");
            Assert.That(RanchWildLogik.Aktiv("hase", 10.0, "gewitter"), Is.False);
            Assert.That(RanchWildLogik.Aktiv("schmetterling", 12.0, "sonne"), Is.True);
            Assert.That(RanchWildLogik.Aktiv("schmetterling", 12.0, "regen"), Is.False);
            Assert.That(RanchWildLogik.Aktiv("vogel", 12.0, "gewitter"), Is.False);
            Assert.That(RanchWildLogik.Aktiv("vogel", 12.0, "niesel"), Is.False, "drizzle counts as rain for birds");
            Assert.That(RanchWildLogik.Aktiv("gluehwuermchen", 22.0, "regen"), Is.False);
        }

        [Test]
        public void DeerFleeFromTheRider()
        {
            var reiter = new V2d(110.0, 100.0);
            var tier = RanchWildLogik.Schritt(RanchWildLogik.Neu("reh", new V2d(100.0, 100.0)), 0.1, reiter, 0.3);
            Assert.That(tier.Zustand, Is.EqualTo("flucht"), "rider at 10 m");
            var start = tier.Pos.Abstand(reiter);
            for (var i = 0; i < 30; i++) tier = RanchWildLogik.Schritt(tier, 0.1, reiter, 0.3);
            Assert.That(tier.Pos.Abstand(reiter), Is.GreaterThan(start), "the deer gains distance");
        }

        [Test]
        public void TheFoxIsCuriousButFleesUpClose()
        {
            var reiter = new V2d(25.0, 0.0);
            var tier = RanchWildLogik.Schritt(RanchWildLogik.Neu("fuchs", new V2d(0.0, 0.0)), 0.1, reiter, 0.3);
            Assert.That(tier.Zustand, Is.EqualTo("neugier"), "25 m: curious");
            var vorher = tier.Pos.Abstand(reiter);
            for (var i = 0; i < 20; i++) tier = RanchWildLogik.Schritt(tier, 0.1, reiter, 0.3);
            Assert.That(tier.Pos.Abstand(reiter), Is.LessThan(vorher), "the fox comes closer");
            tier = RanchWildLogik.Schritt(tier, 0.1, tier.Pos + new V2d(5.0, 0.0), 0.3);
            Assert.That(tier.Zustand, Is.EqualTo("flucht"), "too close: off it goes");
        }

        [Test]
        public void AloneTheyGrazeWanderRestAndDrink()
        {
            var weit = new V2d(9999.0, 9999.0);
            var tier = RanchWildLogik.Neu("hase", new V2d(50.0, -20.0));
            var zustaende = new HashSet<string>();
            var roll = 0.05;
            for (var i = 0; i < 400; i++)
            {
                tier.Timer = System.Math.Min(tier.Timer, 0.05);
                tier = RanchWildLogik.Schritt(tier, 0.1, weit, roll);
                roll = (roll + 0.173) % 1.0;
                zustaende.Add(tier.Zustand);
            }
            foreach (var erwartet in new[] { "fressen", "wandern", "ruhen", "trinken" })
                Assert.That(zustaende, Has.Member(erwartet), erwartet);
        }

        [Test]
        public void AChasedHorseStaysNearItsPasture()
        {
            var heim = new V2d(-590.0, 200.0);
            var tier = RanchWildLogik.Neu("wildpferd", heim);
            var reiter = heim + new V2d(20.0, 0.0);
            for (var i = 0; i < 600; i++)
            {
                tier = RanchWildLogik.Schritt(tier, 0.1, reiter, (i * 0.31) % 1.0);
                reiter = tier.Pos + new V2d(15.0, 5.0);
            }
            Assert.That(tier.Pos.Abstand(heim), Is.LessThanOrEqualTo(RanchWildLogik.Arten["wildpferd"].HeimRadius * 1.4 + 1.0));
        }

        [Test]
        public void WalkingIsReported()
        {
            var tier = RanchWildLogik.Neu("reh", new V2d(0.0, 0.0));
            Assert.That(RanchWildLogik.Laeuft(tier), Is.False, "just spawned: standing");
            tier.Ziel = new V2d(10.0, 0.0);
            Assert.That(RanchWildLogik.Laeuft(tier), Is.True);
        }
    }
}
