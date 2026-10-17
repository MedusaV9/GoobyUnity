using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Training by doing (port of horse_levels.gd stat_xp_buchen / ritt_training) and the riding hall's rain rule.</summary>
    public class RanchTrainingTests
    {
        private const string Tag = "2026-10-12";

        private static JObject Pferd(int level = 1, double xp = 0.0) => new JObject
        {
            ["level"] = level, ["xp"] = xp, ["stats"] = new JObject { ["tempo"] = 10, ["ausdauer"] = 10, ["sprungkraft"] = 10, ["wendigkeit"] = 10, ["gelassenheit"] = 10 },
        };

        [Test]
        public void TheGateAndThePriceOfAPointFollowGodot()
        {
            Assert.That(RanchPferd.StatGate(1), Is.EqualTo(1));
            Assert.That(RanchPferd.StatGate(4), Is.EqualTo(2));
            Assert.That(RanchPferd.StatGate(30), Is.EqualTo(10));
            Assert.That(RanchPferd.PunktKosten(0), Is.EqualTo(100.0));
            Assert.That(RanchPferd.PunktKosten(2), Is.EqualTo(196.0).Within(1e-9));
            Assert.That(RanchPferd.FrischeFaktor(10.0), Is.EqualTo(0.15), "a tired value still learns a little");
        }

        [Test]
        public void GallopingEarnsSpeedPointsUpToTheLevelGate()
        {
            var t = new RanchPferd.Telemetrie { GaloppM = 5000.0, GaloppS = 60.0 };
            var b = RanchPferd.RittTraining(Pferd(), t, Tag);
            Assert.That(b.NeuePunkte["tempo"], Is.EqualTo(1));
            Assert.That(J.Int(b.Pferd["trainiert"]["tempo"]), Is.EqualTo(1));
            Assert.That(RanchPferd.StatWert(b.Pferd, "tempo"), Is.EqualTo(11));
            var zweit = RanchPferd.RittTraining(b.Pferd, new RanchPferd.Telemetrie { GaloppM = 10000.0, GaloppS = 60.0 }, Tag);
            Assert.That(zweit.NeuePunkte.ContainsKey("tempo"), Is.False, "level 1 holds one point");
            Assert.That(J.NumOr(zweit.Pferd["statXp"]["tempo"], 0.0), Is.EqualTo(RanchPferd.PunktKosten(1)).Within(1e-9), "one point's worth banked");
            Assert.That(RanchPferd.StatFortschritt(zweit.Pferd, "tempo"), Is.EqualTo(1.0));
        }

        [Test]
        public void FreshnessWearsOffAndANewDayRefillsIt()
        {
            var e = RanchPferd.StatXpBuchen(Pferd(), "tempo", 50.0, 180.0, Tag);
            Assert.That(J.NumOr(e.Pferd["frische"]["tempo"], 0.0), Is.EqualTo(65.0).Within(1e-9), "one 3-minute unit costs 35");
            var zweit = RanchPferd.StatXpBuchen(e.Pferd, "tempo", 50.0, 0.0, Tag);
            Assert.That(zweit.XpEffektiv, Is.EqualTo(32.5).Within(1e-9), "×0.65 when a third tired");
            var morgen = RanchPferd.StatXpBuchen(e.Pferd, "tempo", 50.0, 0.0, "2026-10-13");
            Assert.That(morgen.XpEffektiv, Is.EqualTo(50.0).Within(1e-9), "fresh again the next day");
            var fleissig = Pferd();
            fleissig["charakter"] = new JArray("fleissig");
            Assert.That(J.NumOr(RanchPferd.StatXpBuchen(fleissig, "tempo", 50.0, 180.0, Tag).Pferd["frische"]["tempo"], 0.0), Is.EqualTo(70.0).Within(1e-9), "a diligent horse tires slower");
        }

        [Test]
        public void RainWithoutARidingHallTeachesHalf()
        {
            var t = new RanchPferd.Telemetrie { UnterwegsS = 600.0, GaloppM = 2000.0, GaloppS = 140.0 };
            var trocken = RanchPferd.RittTraining(Pferd(), t, Tag);
            var nass = RanchPferd.RittTraining(Pferd(), t, Tag, RanchPferd.RegenStatMult);
            Assert.That(J.NumOr(nass.Pferd["statXp"]["ausdauer"], 0.0), Is.EqualTo(J.NumOr(trocken.Pferd["statXp"]["ausdauer"], 0.0) * 0.5).Within(1e-9));
            Assert.That(nass.Xp, Is.EqualTo(trocken.Xp), "the horse's own XP is not the hall's business");
            var bau = RanchBauState.DefaultBau();
            Assert.That(RanchBauEffekte.TrainingBeiRegen(bau, null), Is.False);
            ((JObject)bau["anlagen"])["reithalle"] = new JObject { ["stufe"] = 1 };
            Assert.That(RanchBauEffekte.TrainingBeiRegen(bau, null), Is.True);
        }

        [Test]
        public void RideXpIsCappedPerDayAndLevelsUp()
        {
            var t = new RanchPferd.Telemetrie { UnterwegsS = 30 * 60.0 };
            var b = RanchPferd.RittTraining(Pferd(), t, Tag);
            Assert.That(b.Xp, Is.EqualTo(RanchPferd.XpRittTagesDeckel), "120 minute-XP capped at 60");
            Assert.That(b.LevelUps, Is.EqualTo(new[] { 2 }), "31 XP make level 2");
            Assert.That(RanchPferd.RittTraining(b.Pferd, t, Tag).Xp, Is.EqualTo(0.0), "the day's ride XP is used up");
        }

        [Test]
        public void TheTelemetryCountsLikeTheGodotRide()
        {
            var t = new RanchPferd.Telemetrie();
            t.Sammle(1.0, "galopp", 14.0, 80.0);
            t.Sammle(1.0, "schritt", 3.6, 80.0);
            t.Sammle(1.0, "schritt", 3.6, 40.0);
            t.Sammle(1.0, "stand", 0.0, 80.0);
            Assert.That(t.UnterwegsS, Is.EqualTo(3.0));
            Assert.That(t.GaloppM, Is.EqualTo(14.0));
            Assert.That(t.GaloppS, Is.EqualTo(1.0));
            Assert.That(t.SchrittLauneS, Is.EqualTo(1.0), "only a happy walk calms");
            Assert.That(new RanchPferd.Telemetrie().Leer, Is.True);
            Assert.That(t.Leer, Is.False);
        }
    }
}
