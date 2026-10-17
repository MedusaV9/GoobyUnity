using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Taming a wild horse (port of test_horse_taming.gd's rules) and the day's wild horse at the forest edge.</summary>
    public class RanchZaehmenTests
    {
        [Test]
        public void SneakingCrouchedAndCalmingInTheRhythm()
        {
            var z = new RanchZaehmen(7);
            z.StepAnschleichen(1.0, 5.0, true, false);
            var aufrecht = z.Aufmerksamkeit;
            Assert.That(aufrecht, Is.EqualTo(11.0).Within(1e-9), "22/s × nearness 0.5");
            var g = new RanchZaehmen(7);
            g.StepAnschleichen(1.0, 5.0, true, true);
            Assert.That(g.Aufmerksamkeit, Is.EqualTo(aufrecht * 0.35).Within(1e-9), "crouched is far quieter");
            var weit = new RanchZaehmen(7);
            weit.StepAnschleichen(1.0, 20.0, true, false);
            Assert.That(weit.Aufmerksamkeit, Is.EqualTo(0.0));
            var hastig = new RanchZaehmen(7);
            for (var i = 0; i < 20 && hastig.Phase == RanchZaehmen.Anschleichen; i++) hastig.StepAnschleichen(1.0, 1.6, true, false);
            Assert.That(hastig.Phase, Is.EqualTo(RanchZaehmen.Davongetrabt), "rushing in startles it");
            hastig.StepCooldown(25.0);
            Assert.That(hastig.Event, Is.EqualTo("wieder_da"));
            Assert.That(hastig.Phase, Is.EqualTo(RanchZaehmen.Anschleichen));

            z.StepAnschleichen(0.1, 1.4, true, true);
            Assert.That(z.Phase, Is.EqualTo(RanchZaehmen.Beruhigen));
            Assert.That(z.Event, Is.EqualTo("bereit"));
            z.StepBeruhigen(0.7);
            z.BeruhigenTap();
            Assert.That(z.Event, Is.EqualTo("daneben"), "mid-beat is a miss");
            Assert.That(z.Ruhe, Is.EqualTo(22.0).Within(1e-9));
            for (var i = 0; i < 12 && z.Phase == RanchZaehmen.Beruhigen; i++)
            {
                z.StepBeruhigen(1.4 - z.SeitSchnaubenS);
                z.BeruhigenTap();
            }
            Assert.That(z.Phase, Is.EqualTo(RanchZaehmen.Gezaehmt));
        }

        [Test]
        public void OneWildHorseADayJoinsTheRanch()
        {
            var a = RanchZaehmen.Wildpferd(RanchZaehmen.TagesSeed("2026-10-11"));
            Assert.That(RanchZaehmen.Wildpferd(RanchZaehmen.TagesSeed("2026-10-11")).ToString(), Is.EqualTo(a.ToString()), "same day, same horse");
            Assert.That(RanchZaehmen.WildRassen, Does.Contain(J.Str(a["rasse"])));
            Assert.That(J.IsTrue(a["wild"]), Is.True);
            var s = new JObject { ["progression"] = new JObject { ["level"] = 20 }, ["economy"] = new JObject { ["coins"] = RanchHof.Preis + 400 } };
            RanchKauf.Kaufe(s, 1);
            Assert.That(RanchZaehmen.Aufnehmen(s, "2026-10-11"), Is.Null, "three horses fill the first two boxes");
            RanchWirtschaft.AusbauKaufen(s, "boxen2");
            var id = RanchZaehmen.Aufnehmen(s, "2026-10-11");
            Assert.That(id, Is.Not.Null);
            Assert.That(J.IsTrue(RanchHof.Pferd(s, id)["wild"]), Is.True);
            Assert.That(RanchZaehmen.HeuteDa(s, "2026-10-11"), Is.False);
            Assert.That(RanchZaehmen.Aufnehmen(s, "2026-10-11"), Is.Null, "one a day");
            Assert.That(RanchZaehmen.HeuteDa(s, "2026-10-12"), Is.True);
        }
    }
}
