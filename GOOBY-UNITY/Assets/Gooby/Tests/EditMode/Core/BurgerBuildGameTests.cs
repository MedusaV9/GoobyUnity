using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Burger-Bau round (port of burger_build.gd) over the certified BurgerBuild rules.</summary>
    public class BurgerBuildGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(BurgerBuildGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        /// <summary>A cook who slides the plate under the needed layer and away from everything else.</summary>
        private static void Cook(BurgerBuildGame g, double seconds)
        {
            for (var s = 0.0; s < seconds && !g.Finished; s += Dt)
            {
                var needed = g.NextNeeded;
                var wanted = g.Items.Where(i => i.Id == needed).OrderBy(i => i.Y).FirstOrDefault();
                if (wanted != null) g.SetPlateX(wanted.X);
                else
                {
                    // dodge: stand in a column no falling item is in
                    var busy = new HashSet<int>(g.Items.Where(i => i.Y < 1.5).Select(i => i.Col));
                    var free = Enumerable.Range(0, 3).FirstOrDefault(c => !busy.Contains(c));
                    g.SetPlateX(g.Columns[free]);
                }
                g.Tick(Dt);
            }
        }

        [Test]
        public void TheFirstOrderIsATicketFromBunToBunAndTheIntroWaits()
        {
            var g = new BurgerBuildGame("normal", 7);
            Assert.That(g.Ticket.Count, Is.InRange(4, 7));
            Assert.That(g.Ticket.First(), Is.EqualTo("bun"));
            Assert.That(g.Ticket.Last(), Is.EqualTo("bun"));
            Assert.That(g.NextNeeded, Is.EqualTo("bun"));
            Assert.That(g.OrderNumber, Is.EqualTo(1));
            Assert.That(g.Rush, Is.False, "orders 2 and 4 are the gold ones");
            Assert.That(g.OrderLeft, Is.EqualTo(g.Tune.OrderTimerSec));
            Skip(g, 1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
            Assert.That(g.Items, Is.Empty, "nothing falls during the intro");
            Skip(g, 0.6);
            Assert.That(g.Items.Count, Is.EqualTo(1), "the first ingredient drops right after the intro");
            Assert.That(g.Items[0].Y, Is.LessThanOrEqualTo(BurgerBuildGame.HalfH + 0.6));
            Assert.That(g.Columns, Does.Contain(g.Items[0].X));
            g.SetPlateX(9.0);
            Assert.That(g.PlateX, Is.EqualTo(BurgerBuildGame.HalfW - 0.4), "the plate stays on the counter");
            g.SetPlateFromNormalizedX(0.5);
            Assert.That(g.PlateX, Is.EqualTo(0.0));
        }

        [Test]
        public void EveryCatchPaysByTheRulesAndAFinishedBurgerPaysAndBites()
        {
            var g = new BurgerBuildGame("normal", 3);
            Skip(g, BurgerBuildGame.IntroSec + 0.01);
            var caught = new List<(bool correct, bool rush, double delta, double scoreBefore)>();
            g.Caught += (item, correct, delta) => caught.Add((correct, g.Rush, delta, g.Score - delta));
            var completed = new List<(double points, bool rush)>();
            g.OrderCompleted += p => completed.Add((p, g.Rush));
            var bites = 0;
            g.NewOrder += () => bites++;
            Cook(g, g.Tune.DurationSec + 1.0);
            Assert.That(g.Finished, Is.True, "75 s round");
            Assert.That(caught.Count(c => c.correct), Is.GreaterThan(5), "the cook lands layers");
            foreach (var c in caught)
            {
                if (c.correct) Assert.That(c.delta, Is.EqualTo(c.rush ? g.Tune.CatchPts * BurgerBuild.Burger.RushScoreMult : g.Tune.CatchPts).Within(1e-9), "+5 per layer, ×1.5 on a gold order");
                else Assert.That(c.delta, Is.EqualTo(System.Math.Max(-c.scoreBefore, g.Tune.WrongPts)).Within(1e-9), "−2 for a wrong layer, never below zero");
            }
            foreach (var c in completed) Assert.That(c.points, Is.EqualTo(c.rush ? g.Tune.CompletePts * BurgerBuild.Burger.RushScoreMult : g.Tune.CompletePts).Within(1e-9));
            Assert.That(g.Completed, Is.EqualTo(completed.Count));
            Assert.That(g.Completed + g.Expired, Is.EqualTo(g.OrderNumber - 1), "every order was either finished or expired");
            var expectedScore = System.Math.Max(0.0, caught.Sum(c => c.delta) + completed.Sum(c => c.points));
            Assert.That(g.Score, Is.EqualTo(expectedScore).Within(1e-6));
            Assert.That(g.FinalScore, Is.EqualTo((int)System.Math.Floor(g.Score)));
            Assert.That(bites, Is.EqualTo(g.Completed + g.Expired), "a new ticket after every bite and every expiry");
        }

        [Test]
        public void TheSecondOrderIsGoldAndPaysOneAndAHalfTimes()
        {
            var g = new BurgerBuildGame("normal", 5);
            // nobody cooks the first order: it expires after 30 s and order 2 — a gold one — begins
            g.SetPlateX(BurgerBuildGame.HalfW - 0.4);
            Skip(g, BurgerBuildGame.IntroSec + g.Tune.OrderTimerSec + 0.05);
            Assert.That(g.Expired, Is.EqualTo(1));
            Assert.That(g.OrderNumber, Is.EqualTo(2));
            Assert.That(g.Rush, Is.True);
            Assert.That(g.OrderLeft, Is.EqualTo(g.Tune.OrderTimerSec * g.Tune.RushTimerMult).Within(0.1), "a gold order has 80 % of the time");
            Assert.That(g.Items.All(i => i.Y > BurgerBuildGame.HalfH - 0.5), Is.True, "the counter was cleared, only a fresh drop may be there");
            var deltas = new List<double>();
            g.Caught += (item, correct, delta) => { if (correct) deltas.Add(delta); };
            var guard = 0;
            while (deltas.Count == 0 && !g.Finished && guard++ < 60 * 60) Cook(g, 0.5);
            Assert.That(deltas, Is.Not.Empty, "the cook lands a layer of the gold order");
            Assert.That(deltas[0], Is.EqualTo(g.Tune.CatchPts * BurgerBuild.Burger.RushScoreMult).Within(1e-9), "7.5 per gold layer");
            Assert.That(BurgerBuild.OrderPoints(g.Tune.CompletePts, true), Is.EqualTo(22.5).Within(1e-9));
            Assert.That(BurgerBuild.FallSpeedAt(2, g.Tune), Is.GreaterThan(BurgerBuild.FallSpeedAt(0, g.Tune)), "+8 % per burger");
        }

        [Test]
        public void AnOrderNobodyCooksExpiresAndTheRoundsEndByTimeOrByThreeExpiries()
        {
            var g = new BurgerBuildGame("normal", 5);
            var expired = 0;
            g.OrderExpired += () => expired++;
            g.SetPlateX(BurgerBuildGame.HalfW - 0.4);   // out of the columns: nothing gets caught
            Skip(g, BurgerBuildGame.IntroSec + g.Tune.OrderTimerSec + 0.1);
            Assert.That(expired, Is.EqualTo(1));
            Assert.That(g.OrderNumber, Is.EqualTo(2));
            Assert.That(g.Finished, Is.False);
            Skip(g, g.Tune.DurationSec);
            Assert.That(g.Finished, Is.True, "75 s round");
            Assert.That(g.FinalScore, Is.EqualTo(0));
            var endless = new BurgerBuildGame("endless", 5);
            Assert.That(endless.Tune.Endless, Is.True);
            endless.SetPlateX(BurgerBuildGame.HalfW - 0.4);
            Skip(endless, BurgerBuildGame.IntroSec + endless.Tune.OrderTimerSec * 3.0 + 1.0);
            Assert.That(endless.Expired, Is.EqualTo(endless.Tune.EndlessExpires));
            Assert.That(endless.Finished, Is.True, "three expired orders end an endless round");
        }

        [Test]
        public void TheTicketsAreSeededAndTheModesScaleTheWindows()
        {
            var a = new BurgerBuildGame("normal", 42); var b = new BurgerBuildGame("normal", 42);
            Assert.That(a.Ticket, Is.EqualTo(b.Ticket));
            var hard = new BurgerBuildGame("hard", 42);
            Assert.That(hard.Tune.OrderTimerSec, Is.LessThan(BurgerBuild.Burger.OrderTimerSec));
            Assert.That(hard.Tune.PlateHalfWidth, Is.LessThan(BurgerBuild.Burger.PlateHalfWidth));
            var easy = new BurgerBuildGame("easy", 42);
            Assert.That(easy.Tune.DurationSec, Is.EqualTo(BurgerBuild.Burger.DurationSec * 1.2).Within(1e-9));
            Assert.That(a.Columns, Is.EqualTo(new[] { -2.1, 0.0, 2.1 }).Within(1e-9));
        }
    }
}
