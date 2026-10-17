using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// The RW-5 arcade rides as sessions: run select → intro → ride → booking, plus the bot certification — the QA rider
    /// clears all 10 barrel runs and all 10 time-trial routes with the base horse (stats 10) through the real ride physics.
    /// </summary>
    public class RanchCompGameTests
    {
        [OneTimeSetUp]
        public void Content() => Fixtures.InstallContentSource();

        private static (int stars, double wertung, double ziel, double zeit) RideBot(RanchCompArcadeGame game, int level)
        {
            game.Open(level);
            var guard = 0;
            while (game.Phase != RanchCompArcadeGame.PhaseEnd && guard++ < 60 * 400)
            {
                game.ApplyBot();
                game.Tick(1.0 / 60.0);
            }
            Assert.That(game.Phase, Is.EqualTo(RanchCompArcadeGame.PhaseEnd), game.Spiel + " run " + level + " finishes");
            return (game.LastStars, game.LastWertung, game.Ziel(level), game.LastErgebnis.ZeitS);
        }

        [Test]
        public void SitzungBuchtSterneGeistUndFreischaltung()
        {
            var game = new RanchTonnenGame("normal", 99);
            Assert.That(game.IsUnlocked(1), Is.True);
            Assert.That(game.IsUnlocked(2), Is.False);
            var r = RideBot(game, 1);
            Assert.That(r.stars, Is.EqualTo(3));
            Assert.That(RanchCompState.ArcadeCleared(game.Save, "tonnen", 1), Is.True);
            Assert.That(RanchCompState.ArcadeBest(game.Save, "tonnen", 1), Is.EqualTo((int)Math.Round(r.wertung * 10.0, MidpointRounding.AwayFromZero)));
            Assert.That(game.IsUnlocked(2), Is.True, "a win opens the next run");
            Assert.That(game.LastGeistNeu, Is.True);
            Assert.That((string)RanchCompState.Geist(game.Save, "tonnen")["b64"], Is.Not.Empty, "the best run rides along as a ghost");
            Assert.That(game.LastScore, Is.EqualTo(RanchCompArcade.Score(3, r.wertung, 26.0, true)));
            Assert.That(game.SessionScore, Is.EqualTo(game.LastScore));

            game.Open(2);
            Assert.That(game.Lauf.Geist, Is.Not.Null, "the next run brings the ghost");
            for (var i = 0; i < 60 * 30; i++) game.Tick(1.0 / 60.0);
            var vorher = game.SessionScore;
            var guard = 0;
            while (game.Phase != RanchCompArcadeGame.PhaseEnd && guard++ < 60 * 200)
            {
                game.ApplyBot();
                game.Tick(1.0 / 60.0);
            }
            Assert.That(game.LastStars, Is.EqualTo(0), "30 s of dawdling is too slow");
            Assert.That(game.SessionScore, Is.EqualTo(vorher), "too slow books nothing");
            Assert.That(RanchCompState.ArcadeCleared(game.Save, "tonnen", 2), Is.False);
            game.Finish();
            Assert.That(game.Finished, Is.True);
            Assert.That(game.FinalScore, Is.EqualTo(vorher));

            var zeit = new RanchZeitGame("normal", 1);
            RideBot(zeit, 2);
            Assert.That((string)RanchCompState.Geist(zeit.Save, "zeit_k2")["b64"], Is.Not.Empty, "time-trial ghosts are kept per route");
            Assert.That(RanchCompState.Geist(zeit.Save, "zeit_k1").Count, Is.EqualTo(0));
        }

        [Test]
        public void LeichtLenktDreissigProzentZumZiel()
        {
            var easy = new RanchTonnenGame("easy", 5);
            easy.Open(1);
            for (var i = 0; i < 100 && easy.Phase != RanchCompArcadeGame.PhasePlay; i++) easy.Tick(1.0 / 60.0);
            easy.GaitUp();
            easy.GaitUp();
            for (var i = 0; i < 30; i++) easy.Tick(1.0 / 60.0);
            var c = easy.Lauf.Controller;
            easy.Steer(0.0);
            easy.Tick(1.0 / 60.0);
            Assert.That(c.SteerTarget, Is.EqualTo(0.3 * RcompBot.SteerZu(c, easy.Lauf.MarkerZiel().Value)).Within(0.05), "no input: 30 % of the steer toward the next barrel");
            var normal = new RanchTonnenGame("normal", 5);
            Assert.That(normal.AssistStaerke, Is.EqualTo(0.0), "normal stays Godot-exact");
        }

        [Test]
        public void BotZertifiziertAlleTonnenLaeufe()
        {
            var game = new RanchTonnenGame("normal", 4242);
            var report = new List<string>();
            for (var level = 1; level <= 10; level++)
            {
                var r = RideBot(game, level);
                report.Add($"T{level}: {r.wertung:0.0}s/{r.ziel:0.0}s ★{r.stars}");
                Assert.That(r.stars, Is.GreaterThanOrEqualTo(1), $"barrel run {level}: {r.wertung:0.00} s vs ideal {r.ziel} s");
            }
            TestContext.WriteLine(string.Join("  ", report));
        }

        [Test]
        public void BotZertifiziertAlleZeitStrecken()
        {
            var game = new RanchZeitGame("normal", 4242);
            var report = new List<string>();
            for (var level = 1; level <= 10; level++)
            {
                var r = RideBot(game, level);
                report.Add($"Z{level}: {r.wertung:0.0}s/{r.ziel:0.0}s ★{r.stars}");
                Assert.That(r.stars, Is.GreaterThanOrEqualTo(1), $"route {level}: {r.wertung:0.00} s vs target {r.ziel} s");
                Assert.That(((JObject)game.LastErgebnis.Detail)["verpasst"].Value<int>(), Is.EqualTo(0), "the bot rides every gate");
            }
            TestContext.WriteLine(string.Join("  ", report));
        }
    }
}
