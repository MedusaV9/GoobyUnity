using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Teestube round (port of tea_party.gd) over the certified TeaParty rules.</summary>
    public class TeaPartyGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(TeaPartyGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        private static void WaitForCup(TeaPartyGame g) { var guard = 0; while (!g.Ready && !g.Finished && guard++ < 60 * 10) g.Tick(Dt); Assert.That(g.Ready, Is.True, "a cup stands"); }

        /// <summary>Pours to the given level (within a step) and serves.</summary>
        private static string PourTo(TeaPartyGame g, double level)
        {
            WaitForCup(g);
            Assert.That(g.Press(), Is.True);
            var guard = 0;
            while (g.Level + g.Tune.FillRate * Dt <= level && g.Holding && guard++ < 60 * 10) g.Tick(Dt);
            return g.Release();
        }

        [Test]
        public void TheFirstCupSlidesInAfterTheIntroAndThePourOnlyStartsOnceItStands()
        {
            var g = new TeaPartyGame("normal", 7);
            Assert.That(g.Band.Center, Is.InRange(g.Tune.BandCenterMin, g.Tune.BandCenterMax));
            Assert.That(g.Press(), Is.False, "nothing during the intro");
            Skip(g, TeaPartyGame.IntroSec + 0.01);
            Assert.That(g.Serving, Is.True);
            Assert.That(g.Press(), Is.False, "the cup is still sliding in");
            Skip(g, TeaPartyGame.FirstServeSec + 0.05);
            Assert.That(g.Serving, Is.False);
            Assert.That(g.CupSlide, Is.EqualTo(0.0));
            Assert.That(g.Ready, Is.True);
            Assert.That(g.Press(), Is.True);
            Skip(g, 0.5);
            Assert.That(g.Level, Is.EqualTo(g.Tune.FillRate * 0.5).Within(0.02), "the tea rises at FillRate while holding");
            Assert.That(g.Release(), Is.Not.Null);
            Assert.That(g.Holding, Is.False);
            Assert.That(g.Cups, Is.EqualTo(1));
            Assert.That(g.Serving, Is.True, "the next cup is on its way");
            Assert.That(g.ServeLeft, Is.EqualTo(TeaParty.ServeIntervalAt(g.Elapsed, g.Tune.DurationSec, g.Tune)).Within(1e-9));
        }

        [Test]
        public void PerfectGoodAndSpillScoreByTheRulesAndTheThirdPerfectInARowPaysTheBonus()
        {
            var g = new TeaPartyGame("normal", 3);
            var results = new List<(string result, int points, int streak)>();
            g.Poured += (r, p, s) => results.Add((r, p, s));
            var bonuses = new List<int>();
            g.StreakBonus += n => bonuses.Add(n);
            Skip(g, TeaPartyGame.IntroSec + 0.01);
            // three perfects: the band centre each time
            for (var i = 0; i < 3; i++) Assert.That(PourTo(g, g.Band.Center), Is.EqualTo("perfect"));
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].points, Is.EqualTo(g.Tune.PerfectPts));
            Assert.That(results[2].points, Is.EqualTo(g.Tune.PerfectPts + g.Tune.StreakBonus), "the third in a row pays +2");
            Assert.That(results[2].streak, Is.EqualTo(3));
            Assert.That(bonuses, Is.EqualTo(new[] { 3 }));
            Assert.That(g.Score, Is.EqualTo(3 * g.Tune.PerfectPts + g.Tune.StreakBonus));
            // a good pour: just inside the band but outside the perfect core — the streak resets
            var scoreBefore = g.Score;
            Assert.That(PourTo(g, g.Band.Center + (g.Tune.BandHalfW + g.Tune.PerfectHalfW) * 0.5), Is.EqualTo("good"));
            Assert.That(g.Score, Is.EqualTo(scoreBefore + g.Tune.GoodPts));
            Assert.That(g.Streak, Is.EqualTo(0));
            // a spill: stopped far below the band
            scoreBefore = g.Score;
            Assert.That(PourTo(g, 0.2), Is.EqualTo("miss"));
            Assert.That(g.Score, Is.EqualTo(scoreBefore), "a spill pays nothing");
            Assert.That(g.Spills, Is.EqualTo(1));
            // holding on and on: the cup overflows and serves itself as a spill
            WaitForCup(g);
            g.Press();
            Skip(g, 1.0 / g.Tune.FillRate + 0.2);
            Assert.That(g.Holding, Is.False, "overflow released the pour");
            Assert.That(g.Spills, Is.EqualTo(2));
            Assert.That(results[results.Count - 1].result, Is.EqualTo("miss"));
        }

        [Test]
        public void TheTimedRoundEndsAfterSixtySecondsAndEndlessAfterThreeSpills()
        {
            var g = new TeaPartyGame("normal", 5);
            Skip(g, TeaPartyGame.IntroSec + g.Tune.DurationSec + 0.1);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(0));
            Assert.That(g.Press(), Is.False);
            var e = new TeaPartyGame("endless", 5);
            Assert.That(e.Tune.Endless, Is.True);
            Skip(e, TeaPartyGame.IntroSec + 0.01);
            for (var i = 0; i < 3; i++) PourTo(e, 0.1);
            Assert.That(e.Spills, Is.EqualTo(3));
            Assert.That(e.Finished, Is.True, "three spills end an endless round");
        }

        [Test]
        public void TheBandsAreSeededAndTheModesScaleFillAndBand()
        {
            var a = new TeaPartyGame("normal", 21); var b = new TeaPartyGame("normal", 21);
            Assert.That(a.Band.Center, Is.EqualTo(b.Band.Center));
            var hard = new TeaPartyGame("hard", 21);
            Assert.That(hard.Tune.FillRate, Is.GreaterThan(TeaParty.Tea.FillRate), "hard pours faster");
            Assert.That(hard.Tune.BandHalfW, Is.LessThan(TeaParty.Tea.BandHalfW), "into a narrower band");
            var easy = new TeaPartyGame("easy", 21);
            Assert.That(easy.Tune.DurationSec, Is.EqualTo(TeaParty.Tea.DurationSec * 1.2).Within(1e-9));
            Assert.That(TeaParty.ServeIntervalAt(0.0, 60.0, a.Tune), Is.EqualTo(a.Tune.ServeSecStart));
            Assert.That(TeaParty.ServeIntervalAt(60.0, 60.0, a.Tune), Is.EqualTo(a.Tune.ServeSecEnd));
        }
    }
}
