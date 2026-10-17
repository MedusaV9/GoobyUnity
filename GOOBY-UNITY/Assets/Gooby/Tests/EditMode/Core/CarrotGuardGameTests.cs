using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Möhrenwache round (port of carrot_guard.gd) over the certified CarrotGuard rules.</summary>
    public class CarrotGuardGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(CarrotGuardGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        /// <summary>A watchful gardener: bonks every mole (and the king) the moment it is up.</summary>
        private static void Guard(CarrotGuardGame g, double seconds)
        {
            for (var s = 0.0; s < seconds && !g.Finished; s += Dt)
            {
                g.Tick(Dt);
                if (g.TheKing != null && g.TheKing.Up >= 1.0) g.Tap(g.TheKing.Hole);
                else if (g.Moles.Count > 0 && g.Moles[0].Up >= 1.0) g.Tap(g.Moles[0].Hole);
            }
        }

        [Test]
        public void MolesPopUpAfterTheIntroStayTheirWindowAndStealACarrotWhenTheyGetAway()
        {
            var g = new CarrotGuardGame("normal", 7);
            Assert.That(g.Holes, Is.EqualTo(9));
            Assert.That(g.Carrots, Is.EqualTo(10));
            Skip(g, 1.0);
            Assert.That(g.Moles, Is.Empty, "the intro waits");
            Skip(g, 0.6);
            Assert.That(g.Moles.Count, Is.EqualTo(1), "the first mole right after the intro");
            var mole = g.Moles[0];
            Assert.That(mole.Hole, Is.InRange(0, 8));
            Assert.That(mole.Left, Is.LessThanOrEqualTo(g.Tune.UpTimeStart).And.GreaterThan(g.Tune.UpTimeStart - 0.15), "its window is the start window, already ticking");
            Skip(g, g.Tune.PopSec + 0.02);
            Assert.That(mole.Up, Is.EqualTo(1.0), "popped up within PopSec");
            var stolen = new List<int>();
            g.Stolen += (hole, left) => stolen.Add(hole);
            Skip(g, 1.0);
            Assert.That(stolen, Does.Contain(mole.Hole), "the mole got away with a carrot");
            Assert.That(g.Carrots, Is.LessThanOrEqualTo(9));
            Assert.That(g.Combo, Is.EqualTo(0));
        }

        [Test]
        public void BonksScoreOneEveryFifthInARowThreeMoreAndAWhiffBreaksTheCombo()
        {
            var g = new CarrotGuardGame("normal", 3);
            var bonks = new List<(int hole, int delta, int combo)>();
            g.Bonked += (h, d, c) => bonks.Add((h, d, c));
            Skip(g, CarrotGuardGame.IntroSec + 0.01);
            Guard(g, 12.0);
            Assert.That(bonks.Count, Is.GreaterThanOrEqualTo(5), "a watchful gardener bonks several moles in twelve seconds");
            foreach (var b in bonks) Assert.That(b.delta, Is.EqualTo(b.combo % g.Tune.ComboBonusAt == 0 ? g.Tune.HitPoints + g.Tune.ComboBonus : g.Tune.HitPoints), "+1, every fifth +3 more");
            Assert.That(g.Score, Is.EqualTo(bonks.Sum(b => b.delta)));
            Assert.That(g.Carrots, Is.EqualTo(10), "nothing stolen from a watchful gardener");
            var whiffs = 0;
            g.Whiffed += _ => whiffs++;
            var combo = g.Combo;
            Skip(g, g.Tune.TapDebounceSec + 0.01);
            var empty = g.FreeHoles()[0];
            Assert.That(g.Tap(empty), Is.EqualTo("whiff"));
            Assert.That(whiffs, Is.EqualTo(1));
            Assert.That(g.Combo, Is.EqualTo(0), "a swing at nothing breaks the combo");
            Assert.That(g.Score, Is.EqualTo(bonks.Sum(b => b.delta)), "but costs no points");
            Assert.That(g.Tap(empty), Is.Null, "debounced: two taps within 75 ms count once");
            _ = combo;
        }

        [Test]
        public void EveryTwentyBonksTheKingRisesAndFallsAfterThreeTaps()
        {
            var g = new CarrotGuardGame("easy", 11);
            var kings = new List<int>();
            g.KingRises += h => kings.Add(h);
            var downs = new List<int>();
            g.KingDown += (h, d) => downs.Add(d);
            var hits = new List<int>();
            g.KingHit += (h, hp) => hits.Add(hp);
            Skip(g, CarrotGuardGame.IntroSec + 0.01);
            // bonk the ordinary moles until the king rises (and leave him standing)
            for (var i = 0; i < 60 * 120 && g.TheKing == null && !g.Finished; i++)
            {
                g.Tick(Dt);
                if (g.Moles.Count > 0 && g.Moles[0].Up >= 1.0) g.Tap(g.Moles[0].Hole);
            }
            Assert.That(kings.Count, Is.EqualTo(1), "after twenty bonks the mole king rises");
            Assert.That(g.Bonks, Is.GreaterThanOrEqualTo(20));
            Assert.That(g.TheKing, Is.Not.Null);
            var scoreBefore = g.Score;
            Skip(g, g.Tune.PopSec + 0.02);
            Assert.That(g.Tap(g.TheKing.Hole), Is.EqualTo("king-hit"));
            Skip(g, 0.1);
            Assert.That(g.Tap(g.TheKing.Hole), Is.EqualTo("king-hit"));
            Assert.That(hits, Is.EqualTo(new[] { 2, 1 }));
            Skip(g, 0.1);
            Assert.That(g.Tap(g.TheKing.Hole), Is.EqualTo("king-down"));
            Assert.That(g.TheKing, Is.Null);
            Assert.That(downs.Count, Is.EqualTo(1));
            Assert.That(downs[0], Is.GreaterThanOrEqualTo(CarrotGuard.Guard.KingPoints + CarrotGuard.Guard.KingCoinDrop * CarrotGuard.Guard.KingScorePerCoin), "+8 and two coins worth three each");
            Assert.That(g.Score, Is.EqualTo(scoreBefore + downs[0]));
            Assert.That(g.Moles, Is.Empty, "no ordinary moles while the king was up");
        }

        [Test]
        public void TheRoundEndsAfter45SecondsOrWhenTheBedIsEmptyAndEndlessAfterThreeThefts()
        {
            var g = new CarrotGuardGame("normal", 5);
            Skip(g, CarrotGuardGame.IntroSec + g.Tune.DurationSec + 0.1);
            Assert.That(g.Finished, Is.True, "45 s round");
            Assert.That(g.Carrots, Is.LessThan(10), "an idle gardener loses carrots");
            var e = new CarrotGuardGame("endless", 5);
            Assert.That(e.Tune.Endless, Is.True);
            Skip(e, CarrotGuardGame.IntroSec + 30.0);
            Assert.That(e.Finished, Is.True, "three stolen carrots end an endless round");
            Assert.That(e.StolenCount, Is.EqualTo(e.Tune.EndlessStolen));
            Assert.That(e.Tap(0), Is.Null);
        }

        [Test]
        public void TheSpawnsAreSeededAndTheModesScaleWindowAndCadence()
        {
            var a = new CarrotGuardGame("normal", 42); var b = new CarrotGuardGame("normal", 42);
            Skip(a, 5.0); Skip(b, 5.0);
            Assert.That(a.Moles.Select(m => m.Hole), Is.EqualTo(b.Moles.Select(m => m.Hole)));
            var hard = new CarrotGuardGame("hard", 42);
            Assert.That(hard.Tune.UpTimeStart, Is.LessThan(CarrotGuard.Guard.UpTimeStart), "hard moles dive sooner");
            Assert.That(hard.Tune.SpawnStartSec, Is.LessThan(CarrotGuard.Guard.SpawnStartSec), "and come faster");
            var easy = new CarrotGuardGame("easy", 42);
            Assert.That(easy.Tune.DurationSec, Is.EqualTo(CarrotGuard.Guard.DurationSec * 1.2).Within(1e-9));
            Assert.That(CarrotGuard.UpTimeAt(45.0, 45.0, a.Tune), Is.EqualTo(a.Tune.UpTimeEnd));
        }
    }
}
