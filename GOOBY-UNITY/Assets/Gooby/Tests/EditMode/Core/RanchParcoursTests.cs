using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// Obstacle course — port of test_ranchplay_parcours.gd: course data, the jump window, difficulty tolerance, points /
    /// combo / time bonus, stars, the bot certification (every course doable; easy ≤ normal &lt; hard knock-downs) and
    /// the realtime session.
    /// </summary>
    public class RanchParcoursTests
    {
        private List<JObject> _kurse;

        [OneTimeSetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            _kurse = RanchParcours.LoadKurse();
        }

        [Test]
        public void CoursesLoadValidateAndGrow()
        {
            Assert.That(_kurse.Count, Is.EqualTo(10));
            Assert.That(RanchParcours.ValidateKurse(_kurse), Is.Empty);
            var before = 0;
            for (var id = 1; id <= 10; id++)
            {
                var kurs = RanchParcours.KursById(_kurse, id);
                Assert.That(kurs, Is.Not.Null);
                var n = RanchParcours.Hindernisse(kurs).Count;
                Assert.That(n, Is.GreaterThanOrEqualTo(before));
                before = n;
            }
        }

        [Test]
        public void TheJumpWindowAndDifficultyTolerance()
        {
            var tune = RanchParcours.Base;
            const double tempo = 6.0;
            var weite = RideFeel.WeiteM(tempo);
            Assert.That(RanchParcours.BewerteSprung(10.0, tempo, 10.0 + weite * 0.5, "zaun", tune).Qualitaet, Is.EqualTo("perfekt"));
            Assert.That(RanchParcours.BewerteSprung(10.0, tempo, 10.0 + weite * 0.5, "zaun", tune).FehlerM, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(RanchParcours.BewerteSprung(10.0, tempo, 10.0 + weite * 0.5 + 0.7, "zaun", tune).Qualitaet, Is.EqualTo("gut"));
            Assert.That(RanchParcours.BewerteSprung(10.0, tempo, 10.0 + weite * 0.95, "zaun", tune).Qualitaet, Is.EqualTo("abwurf"));
            Assert.That(RanchParcours.BewerteSprung(10.0, tempo, 10.0 + weite * 0.5, "wasser", tune).Qualitaet, Is.EqualTo("perfekt"));
            var easy = RanchParcours.ApplyDifficulty(tune, "easy");
            var hard = RanchParcours.ApplyDifficulty(tune, "hard");
            Assert.That(easy.Toleranz, Is.EqualTo(1.25));
            Assert.That(hard.Toleranz, Is.EqualTo(0.8));
            Assert.That(RanchParcours.ApplyDifficulty(tune, "quatsch").Toleranz, Is.EqualTo(tune.Toleranz));
            var knapp = 10.0 + weite * 0.24;
            Assert.That(RanchParcours.BewerteSprung(10.0, tempo, knapp, "zaun", easy).Qualitaet, Is.EqualTo("gut"));
            Assert.That(RanchParcours.BewerteSprung(10.0, tempo, knapp, "zaun", hard).Qualitaet, Is.EqualTo("abwurf"));
        }

        [Test]
        public void PointsComboTimeBonusAndStars()
        {
            var t = RanchParcours.Base;
            Assert.That(RanchParcours.SprungPunkte("perfekt", 0, t), Is.EqualTo(15));
            Assert.That(RanchParcours.SprungPunkte("perfekt", 3, t), Is.EqualTo(21));
            Assert.That(RanchParcours.SprungPunkte("perfekt", 99, t), Is.EqualTo(25));
            Assert.That(RanchParcours.SprungPunkte("gut", 2, t), Is.EqualTo(14));
            Assert.That(RanchParcours.SprungPunkte("abwurf", 5, t), Is.EqualTo(0));
            Assert.That(RanchParcours.Zeitbonus(60.0, 50.0, t), Is.EqualTo(40));
            Assert.That(RanchParcours.Zeitbonus(60.0, 70.0, t), Is.EqualTo(0));
            Assert.That(RanchParcours.KursScore(100, 40, false, 5), Is.EqualTo(155));
            Assert.That(RanchParcours.KursScore(100, 40, true, 5), Is.EqualTo(180));
            Assert.That(RanchParcours.Sterne(0, 50.0, 60.0), Is.EqualTo(3));
            Assert.That(RanchParcours.Sterne(0, 65.0, 60.0), Is.EqualTo(2));
            Assert.That(RanchParcours.Sterne(1, 50.0, 60.0), Is.EqualTo(2));
            Assert.That(RanchParcours.Sterne(2, 50.0, 60.0), Is.EqualTo(1));
        }

        [Test]
        public void TheBotIsDeterministicClearsEveryCourseAndDifficultyOrdersKnockDowns()
        {
            var k5 = RanchParcours.KursById(_kurse, 5);
            Assert.That(RanchParcours.SimulateLauf(k5, 42).ToString(), Is.EqualTo(RanchParcours.SimulateLauf(k5, 42).ToString()));
            Assert.That(RanchParcours.SimulateLauf(k5, 43).ToString(), Is.Not.EqualTo(RanchParcours.SimulateLauf(k5, 42).ToString()));
            for (var id = 1; id <= 10; id++)
            {
                var r = RanchParcours.SimulateLauf(RanchParcours.KursById(_kurse, id), 1);
                Assert.That(r.Score, Is.GreaterThan(0), $"course {id}");
                Assert.That(r.Sterne, Is.GreaterThanOrEqualTo(1));
                Assert.That(r.ZeitS, Is.GreaterThan(0.0));
            }
            var knocks = new Dictionary<string, int> { ["easy"] = 0, ["normal"] = 0, ["hard"] = 0 };
            foreach (var mode in new[] { "easy", "normal", "hard" })
                for (var id = 1; id <= 10; id++)
                    for (var seed = 1; seed <= 5; seed++)
                        knocks[mode] += RanchParcours.SimulateLauf(RanchParcours.KursById(_kurse, id), seed, mode).Abwuerfe;
            Assert.That(knocks["easy"], Is.LessThanOrEqualTo(knocks["normal"]));
            Assert.That(knocks["normal"], Is.LessThan(knocks["hard"]));
            Assert.That(knocks["easy"], Is.LessThan(15));
        }

        [Test]
        public void TheRealtimeRunRatesJumpsAndBooksTheCourse()
        {
            var g = new RanchParcoursGame("normal", 9);
            var ratings = new List<string>();
            g.Rated += (h, q, p) => ratings.Add(q);
            g.Open(1);
            for (var i = 0; i < 30; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.X, Is.EqualTo(0.0), "the intro holds the horse");
            Assert.That(g.Jump(), Is.False, "no jump during the intro");
            var guard = 0;
            while (g.Phase == RanchParcoursGame.PhasePlay && guard++ < 100000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(g.Phase, Is.EqualTo(RanchParcoursGame.PhaseWon));
            Assert.That(ratings.Count, Is.EqualTo(RanchParcours.Hindernisse(g.Kurs).Count));
            Assert.That(ratings, Has.Some.Not.EqualTo("abwurf"));
            Assert.That(RanchSpieleProgress.IsCleared(g.Save, RanchSpieleProgress.Parcours, 1), Is.True);
            Assert.That(g.LastScore, Is.GreaterThan(0));

            var lazy = new RanchParcoursGame("normal", 9);
            var knocked = 0;
            lazy.Rated += (h, q, p) => { if (q == "abwurf") knocked++; };
            lazy.Open(1);
            while (lazy.Phase == RanchParcoursGame.PhasePlay && guard++ < 200000) lazy.Tick(1.0 / 30.0);
            Assert.That(knocked, Is.EqualTo(RanchParcours.Hindernisse(lazy.Kurs).Count), "no jump = every obstacle knocked");
            Assert.That(lazy.Strafzeit, Is.EqualTo(knocked * RanchParcours.Base.AbwurfStrafeS).Within(1e-9));
        }
    }
}
