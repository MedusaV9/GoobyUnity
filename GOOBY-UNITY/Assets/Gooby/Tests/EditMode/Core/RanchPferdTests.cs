using System.Linq;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// A horse of the save rides with its own values (port of RanchHorseLevels.stat_wert/stats_effektiv): breed base +
    /// trained points, clamped 1–20, a young horse to 15; the breed table comes from rassen.json (unknown = Puschelhufer),
    /// a Tölterle knows the tölt; the tournament hands the best horse's values and the tölt to the ride.
    /// </summary>
    public class RanchPferdTests
    {
        [OneTimeSetUp]
        public void Content() => Fixtures.InstallContentSource();

        private static JObject Sternchen() => new JObject
        {
            ["name"] = "Sternchen", ["rasse"] = "toelterle", ["level"] = 3, ["bindung"] = 60, ["farbe"] = "palomino",
            ["stats"] = new JObject { ["tempo"] = 11, ["ausdauer"] = 13, ["sprungkraft"] = 7, ["wendigkeit"] = 10, ["gelassenheit"] = 12 },
            ["trainiert"] = new JObject { ["ausdauer"] = 4, ["tempo"] = 2 },
        };

        [Test]
        public void EffectiveValuesAreBasePlusTrainingClamped()
        {
            var p = Sternchen();
            Assert.That(RanchPferd.StatWert(p, "ausdauer"), Is.EqualTo(17));
            Assert.That(RanchPferd.StatWert(p, "tempo"), Is.EqualTo(13));
            Assert.That(RanchPferd.StatWert(p, "sprungkraft"), Is.EqualTo(7));
            Assert.That(RanchPferd.StatWert(new JObject(), "tempo"), Is.EqualTo(10), "no stats = the base 10");
            p["trainiert"]["ausdauer"] = 9;
            Assert.That(RanchPferd.StatWert(p, "ausdauer"), Is.EqualTo(20), "never over 20");
            p["alter"] = "jungpferd";
            Assert.That(RanchPferd.StatWert(p, "ausdauer"), Is.EqualTo(RanchPferd.JungpferdDeckel), "a young horse tops out at 15");
            Assert.That(RanchPferd.StatWert(new JObject { ["stats"] = new JObject { ["tempo"] = -4 } }, "tempo"), Is.EqualTo(1));
            Assert.That(RanchPferd.StatWert(new JObject { ["stats"] = new JObject { ["tempo"] = "14" } }, "tempo"), Is.EqualTo(10), "text counts as missing (Godot _num)");
            var alle = RanchPferd.StatsEffektiv(Sternchen());
            Assert.That(alle.Keys, Is.EquivalentTo(RanchPferd.StatKeys));
            Assert.That(RanchPferd.StatsEffektiv(null), Is.Empty, "no horse: the ride keeps the base horse");
        }

        [Test]
        public void BreedTableKnowsTheToelt()
        {
            Assert.That((string)RanchPferd.Rasse("toelterle")["eigenheit"], Is.EqualTo("toelt"));
            Assert.That((string)RanchPferd.Rasse("gibtsnicht")["id"], Is.EqualTo("puschelhufer"), "unknown = Puschelhufer");
            Assert.That(RanchPferd.KannToelt(Sternchen()), Is.True);
            Assert.That(RanchPferd.KannToelt(new JObject { ["rasse"] = "flitzewind" }), Is.False);
            Assert.That(RanchPferd.KannToelt(null), Is.False);
        }

        [Test]
        public void XpCurveFollowsTheDoc()
        {
            Assert.That(RanchPferd.XpFuerLevel(1), Is.EqualTo(31.0), "L1→2 = 31");
            Assert.That(RanchPferd.XpFuerLevel(10), Is.EqualTo(400.0));
            Assert.That(RanchPferd.XpFuerLevel(29), Is.EqualTo(1711.0), "L29→30 = 1711");
            Assert.That(RanchPferd.XpSummeBis(30), Is.EqualTo(21605.0), "Σ 1→30 ≈ 21 600");
            Assert.That(RanchPferd.LevelFuerXp(0.0), Is.EqualTo(1));
            Assert.That(RanchPferd.LevelFuerXp(30.9), Is.EqualTo(1));
            Assert.That(RanchPferd.LevelFuerXp(31.0), Is.EqualTo(2));
            Assert.That(RanchPferd.LevelFuerXp(999999.0), Is.EqualTo(30), "capped at 30");
        }

        [Test]
        public void RideXpHasADailyCapOtherSourcesDoNot()
        {
            const string tag = "2026-07-26";
            var pferd = new JObject { ["name"] = "Testpferd", ["xp"] = 0.0, ["level"] = 1 };
            var erst = RanchPferd.XpBuchen(pferd, 50.0, "ritt", tag);
            Assert.That(erst.Gewinn, Is.EqualTo(50.0));
            var zweit = RanchPferd.XpBuchen(erst.Pferd, 50.0, "ritt", tag);
            Assert.That(zweit.Gewinn, Is.EqualTo(10.0), "cap 60 a day");
            Assert.That(RanchPferd.XpBuchen(zweit.Pferd, 50.0, "ritt", "2026-07-27").Gewinn, Is.EqualTo(50.0), "a new day, a new cap");
            Assert.That(RanchPferd.XpBuchen(zweit.Pferd, 20.0, "stern", tag).Gewinn, Is.EqualTo(20.0), "star XP outside the ride cap");
            var verspielt = (JObject)zweit.Pferd.DeepClone();
            verspielt["charakter"] = new JArray("verspielt");
            Assert.That(RanchPferd.XpBuchen(verspielt, 20.0, "stern", tag).Gewinn, Is.EqualTo(23.0).Within(1e-9), "a playful horse ×1.15");
            Assert.That(RanchPferd.XpBuchen(zweit.Pferd, 500.0, "wettbewerb", tag).Gewinn, Is.EqualTo(500.0), "tournaments count in full");
            Assert.That((double)pferd["xp"], Is.EqualTo(0.0), "the input stays untouched");
        }

        [Test]
        public void LevelUpsAndMilestones()
        {
            const string tag = "2026-07-26";
            var p = new JObject { ["xp"] = RanchPferd.XpSummeBis(5) - 1.0 };
            p["level"] = RanchPferd.LevelFuerXp((double)p["xp"]);
            var ergebnis = RanchPferd.XpBuchen(p, 2.0, "direkt", tag);
            Assert.That(ergebnis.LevelUps, Is.EqualTo(new[] { 5 }), "exactly one level-up to 5");
            Assert.That(ergebnis.Meilensteine, Is.EqualTo(new[] { "schleife_jungstar" }));
            Assert.That((int)ergebnis.Pferd["level"], Is.EqualTo(5));
            var gross = RanchPferd.XpBuchen(new JObject(), RanchPferd.XpSummeBis(11), "direkt", tag);
            Assert.That(gross.Meilensteine, Has.Member("maehnenfrisur"), "milestone 10 in one jump");
            Assert.That((double)RanchPferd.XpBuchen(new JObject(), 1e9, "direkt", tag).Pferd["xp"], Is.EqualTo(RanchPferd.XpSummeBis(30)), "XP capped at level 30");
        }

        [Test]
        public void TournamentXpLandsOnTheRiddenHorse()
        {
            var game = new RanchTurnierGame("normal", 11, null, "2026-10-01");
            var sternchen = Sternchen();
            sternchen["xp"] = RanchPferd.XpSummeBis(4) - 10.0; // ten XP short of level 4
            var ranch = game.Save["ranch"] as JObject ?? (JObject)(game.Save["ranch"] = new JObject());
            ranch["tiere"] = new JObject { ["pferde"] = new JObject { ["p1"] = sternchen } };
            game.ZurueckZumMenu();
            var guard = 0;
            while (game.Phase != RanchTurnierGame.PhaseErgebnis && guard++ < 60 * 400)
            {
                game.ApplyBot();
                game.Tick(1.0 / 60.0);
            }
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseErgebnis));
            var xp = game.LastBericht.Xp;
            Assert.That(xp, Is.GreaterThan(0), "the class XP of the result card");
            var gebucht = (JObject)game.Save["ranch"]["tiere"]["pferde"]["p1"];
            Assert.That((double)gebucht["xp"], Is.EqualTo(RanchPferd.XpSummeBis(4) - 10.0 + xp).Within(1e-9), "the card's XP landed on the horse");
            Assert.That((int)gebucht["level"], Is.EqualTo(4));
            Assert.That(game.LastPferdXp.LevelUps, Is.EqualTo(new[] { 4 }), "the result card can announce the level-up");
        }

        [Test]
        public void TheTournamentRidesTheBestHorseWithItsValues()
        {
            var game = new RanchTurnierGame("normal", 7, null, "2026-10-01");
            var ranch = game.Save["ranch"] as JObject ?? (JObject)(game.Save["ranch"] = new JObject());
            ranch["tiere"] = new JObject
            {
                ["pferde"] = new JObject
                {
                    ["p1"] = Sternchen(),
                    ["p2"] = new JObject { ["name"] = "Moos", ["rasse"] = "moosmaehne", ["level"] = 1 },
                },
            };
            game.ZurueckZumMenu();
            Assert.That(game.PferdName, Is.EqualTo("Sternchen"), "the higher level rides");
            Assert.That(game.OeffneEinweisung("tonnen"), Is.True);
            Assert.That(game.Lauf.Farbe, Is.EqualTo("palomino"), "the stage paints the horse in its own coat");
            var c = game.Lauf.Controller;
            Assert.That(c.AusdauerMaxWert, Is.EqualTo(RideStats.AusdauerMax(RanchPferd.StatsEffektiv(Sternchen()))).Within(1e-9));
            Assert.That(c.AusdauerMaxWert, Is.GreaterThan(RideStats.AusdauerMax(RideStats.Leer)), "13 + 4 trained beats the base horse");
            var gangarten = Enumerable.Range(0, 4).Select(_ => { c.GaitUp(); return c.Gait; }).ToList();
            Assert.That(gangarten, Is.EqualTo(new[] { "schritt", "trab", "toelt", "galopp" }), "a Tölterle has the fourth gait");
        }
    }
}
