using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class GoalieGoobyGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static GoalieGoobyGame Started(string mode = "normal", int seed = 4)
        {
            var g = new GoalieGoobyGame(mode, seed);
            g.Tick(GoalieGoobyGame.IntroSec + 0.01);
            return g;
        }

        private static void Run(GoalieGoobyGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        /// <summary>Waits for the next kick to be telegraphed.</summary>
        private static GoalieGoobyGame.Kick NextKick(GoalieGoobyGame g)
        {
            for (var i = 0; i < 6000 && g.Current == null && !g.Finished; i++) g.Tick(Dt);
            return g.Current;
        }

        /// <summary>The dive a kick wants: its lane, up for a lob, down for a roller.</summary>
        private static (double dx, double dy) SwipeFor(GoalieGoobyGame.Kick k)
        {
            // the lane comes from the swipe's angle against the vertical (±18° middle, ±54° the outer lanes), the
            // height from its vertical part (24 px or more up/down; a shorter one stays "mid")
            var dy = k.Kind == "lob" ? -80.0 : k.Kind == "roller" ? 80.0 : 20.0;
            var ratio = k.Lane == 2 ? 0.0 : k.Lane == 1 || k.Lane == 3 ? Math.Tan(36.0 * Math.PI / 180.0) : Math.Tan(70.0 * Math.PI / 180.0);
            var dx = (k.Lane < 2 ? -1.0 : 1.0) * ratio * Math.Abs(dy);
            return (dx, dy);
        }

        /// <summary>Dives correctly at the given lead before the ball arrives, then lets the kick resolve.</summary>
        private static void SaveNext(GoalieGoobyGame g, double lead)
        {
            var k = NextKick(g);
            Assume.That(k, Is.Not.Null);
            while (g.ArriveT - g.Elapsed > lead && !g.Finished) g.Tick(Dt);
            var (dx, dy) = SwipeFor(k);
            g.DiveBy(dx, dy);
            while (g.Current == k && !g.Finished) g.Tick(Dt);
        }

        [Test]
        public void TheFirstKickIsTelegraphedShortlyAfterTheIntroAndFliesIntoOneOfFiveLanes()
        {
            var g = new GoalieGoobyGame("normal", 4);
            g.Tick(1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
            g.Tick(0.3);
            Assert.That(g.Running, Is.True);
            var kicked = new List<GoalieGoobyGame.Kick>();
            g.Kicked += k => kicked.Add(k);
            var k = NextKick(g);
            Assert.That(k, Is.Not.Null);
            Assert.That(kicked.Count, Is.EqualTo(1));
            Assert.That(g.Elapsed, Is.EqualTo(0.6).Within(Dt + 1e-9), "the first kick comes at 0.6 s");
            Assert.That(k.Lane, Is.InRange(0, 4));
            Assert.That(k.Kind, Is.EqualTo("straight"), "no lobs or rollers before 8 s");
            Assert.That(k.Telegraph, Is.EqualTo(GoalieGooby.TelegraphSecAt(g.Elapsed, g.Tune)).Within(1e-9));
            Assert.That(k.Flight, Is.EqualTo(0.55).Within(1e-9));
            Assert.That(k.Shootout, Is.False);
            Assert.That(g.KickProgress, Is.EqualTo(0.0), "the ball waits on the spot during the telegraph");
            Run(g, k.Telegraph + 0.01);
            Assert.That(g.BallInFlight, Is.True);
            Assert.That(g.KickProgress, Is.GreaterThan(0.0));
            Assert.That(g.TelegraphProgress, Is.EqualTo(1.0));
        }

        [Test]
        public void ADiveIntoTheRightLaneWithinTheHoldWindowSavesForFourAndInTheLastSplitSecondForSix()
        {
            var g = Started();
            var saved = new List<(int points, bool super)>();
            g.Saved += (k, p, s) => saved.Add((p, s));
            SaveNext(g, 0.3);
            Assert.That(saved, Is.EqualTo(new[] { (4, false) }));
            Assert.That(g.Saves, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(4));
            Assert.That(g.SaveStreak, Is.EqualTo(1));
            SaveNext(g, 0.1);
            Assert.That(saved[1], Is.EqualTo((6, true)), "inside the 0.15 s super window");
            Assert.That(g.Score, Is.EqualTo(10));
            Assert.That(g.Goals, Is.EqualTo(0));
        }

        [Test]
        public void ADiveTooEarlyOrIntoTheWrongLaneIsAGoalAndThreeGoalsEndTheRound()
        {
            var g = Started();
            var conceded = 0;
            g.Conceded += _ => conceded++;
            // too early: more than DiveHoldSec before the ball arrives
            var k = NextKick(g);
            var (dx, dy) = SwipeFor(k);
            g.DiveBy(dx, dy);
            Assert.That(g.ArriveT - g.Elapsed, Is.GreaterThan(g.Tune.DiveHoldSec));
            while (g.Current == k) g.Tick(Dt);
            Assert.That(conceded, Is.EqualTo(1));
            Assert.That(g.Goals, Is.EqualTo(1));
            // the wrong lane, in time
            k = NextKick(g);
            while (g.ArriveT - g.Elapsed > 0.2) g.Tick(Dt);
            g.DiveBy((k.Lane == 0 ? 4 : 0) * 60.0 - 120.0, 0.0);
            Assert.That(g.Dive.Lane, Is.Not.EqualTo(k.Lane));
            while (g.Current == k) g.Tick(Dt);
            Assert.That(conceded, Is.EqualTo(2));
            // no dive at all
            k = NextKick(g);
            while (g.Current == k && !g.Finished) g.Tick(Dt);
            Assert.That(conceded, Is.EqualTo(3));
            Assert.That(g.Finished, Is.True, "three goals end the round early");
            Assert.That(g.Score, Is.EqualTo(0));
            Assert.That(g.DiveBy(0, 0), Is.Null, "nothing after the whistle");
        }

        [Test]
        public void ABareTapDivesToTheMiddleAndALobNeedsAnUpwardSwipe()
        {
            var g = Started();
            NextKick(g);
            var d = g.DiveBy(3.0, -2.0);
            Assert.That(d.Lane, Is.EqualTo(2));
            Assert.That(d.V, Is.EqualTo("mid"));
            d = g.DiveBy(-90.0, -60.0);
            Assert.That(d.Lane, Is.EqualTo(0), "far left");
            Assert.That(d.V, Is.EqualTo("up"));
            Assert.That(GoalieGooby.SaveMatches(0, "lob", 0, "up"), Is.True);
            Assert.That(GoalieGooby.SaveMatches(0, "lob", 0, "mid"), Is.False, "a lob over a flat dive is a goal");
            Assert.That(GoalieGooby.SaveMatches(1, "roller", 1, "down"), Is.True);
            Assert.That(GoalieGooby.SaveMatches(1, "straight", 1, "up"), Is.True, "a straight shot is saved at any height");
        }

        [Test]
        public void LobsAndRollersMixInAfterEightSecondsAndTheTelegraphShrinksOverTheMinute()
        {
            var g = Started("normal", 11);
            var kinds = new HashSet<string>();
            var telegraphs = new List<double>();
            for (var n = 0; n < 40 && !g.Finished; n++)
            {
                var k = NextKick(g);
                if (k == null) break;
                if (g.Elapsed >= 8.0) kinds.Add(k.Kind);
                if (!k.Shootout) telegraphs.Add(k.Telegraph);
                // keep the round alive by saving everything
                while (g.ArriveT - g.Elapsed > 0.2 && !g.Finished) g.Tick(Dt);
                var (dx, dy) = SwipeFor(k);
                g.DiveBy(dx, dy);
                while (g.Current == k && !g.Finished) g.Tick(Dt);
            }
            Assert.That(kinds, Does.Contain("lob").Or.Contain("roller"));
            Assert.That(telegraphs[telegraphs.Count - 1], Is.LessThan(telegraphs[0]));
            Assert.That(g.Goals, Is.EqualTo(0), "every kick was saved");
        }

        [Test]
        public void TenSavesMakeTheCrowdCheerAndTheFlightsTenPercentFaster()
        {
            var g = Started("normal", 2);
            var cheers = 0;
            g.Cheer += c => cheers = c;
            for (var n = 0; n < 10 && !g.Finished; n++) SaveNext(g, 0.2);
            Assert.That(g.Saves, Is.EqualTo(10));
            Assert.That(cheers, Is.EqualTo(1));
            Assert.That(g.Cheers, Is.EqualTo(1));
            var k = NextKick(g);
            Assert.That(k.Flight, Is.EqualTo(0.55 / 1.1).Within(1e-9));
        }

        [Test]
        public void TheLastTenSecondsAreAShootOutWithDoublePointsAndTheRoundEndsAtSixty()
        {
            var g = Started("normal", 6);
            // survive to 50 s by saving every kick
            while (g.Elapsed < 50.0 && !g.Finished)
            {
                var k = NextKick(g);
                if (k == null) break;
                while (g.ArriveT - g.Elapsed > 0.2 && !g.Finished) g.Tick(Dt);
                var (dx, dy) = SwipeFor(k);
                g.DiveBy(dx, dy);
                while (g.Current == k && !g.Finished) g.Tick(Dt);
            }
            Assume.That(g.Finished, Is.False);
            Assert.That(g.InShootout, Is.True);
            var k2 = NextKick(g);
            Assert.That(k2.Shootout, Is.True);
            Assert.That(k2.Telegraph, Is.EqualTo(g.Tune.ShootoutTelegraphSec));
            Assert.That(k2.Flight, Is.EqualTo(g.Tune.ShootoutFlightSec));
            var before = g.Score;
            while (g.ArriveT - g.Elapsed > 0.1 && !g.Finished) g.Tick(Dt);
            var (sx, sy) = SwipeFor(k2);
            g.DiveBy(sx, sy);
            while (g.Current == k2 && !g.Finished) g.Tick(Dt);
            Assert.That(g.Score - before, Is.EqualTo(12), "a super save in the shoot-out pays double");
            var finished = false;
            g.Finished_ += () => finished = true;
            var goalsBefore = g.Goals;
            // save the rest of the shoot-out: the whistle, not a third goal, ends the round
            while (!g.Finished)
            {
                var k = NextKick(g);
                if (k == null) break;
                while (g.ArriveT - g.Elapsed > 0.1 && !g.Finished) g.Tick(Dt);
                var (dx, dy) = SwipeFor(k);
                g.DiveBy(dx, dy);
                while (g.Current == k && !g.Finished) g.Tick(Dt);
            }
            Assert.That(finished, Is.True);
            Assert.That(g.Goals, Is.EqualTo(goalsBefore));
            Assert.That(g.TimeLeft, Is.EqualTo(0.0));
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(60.0));
        }

        [Test]
        public void TheQaBotSavesSevenOfEveryEightKicksAtEveryHeightAndLeavesTheKickStreamAlone()
        {
            var g = Started("normal", 9);
            var plain = Started("normal", 9);
            var heights = new HashSet<string>();
            g.Saved += (k, p, s) => heights.Add(k.Kind);
            var lanes = new List<int>();
            var plainLanes = new List<int>();
            var kinds = new List<string>();
            g.Kicked += k => { lanes.Add(k.Lane); kinds.Add(k.Kind); };
            plain.Kicked += k => plainLanes.Add(k.Lane);
            for (var t = 0.0; t < 62.0 && !g.Finished; t += Dt)
            {
                g.ApplyBot();
                g.Tick(Dt);
            }
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Saves, Is.GreaterThanOrEqualTo(15));
            Assert.That(g.Goals, Is.EqualTo((g.Saves + g.Goals) / 8), "only the deliberate wrong guesses go in");
            Assert.That(heights, Is.SupersetOf(new[] { "straight", "lob", "roller" }), string.Join(",", kinds));
            for (var t = 0.0; t < 62.0 && !plain.Finished; t += Dt) plain.Tick(Dt);
            Assert.That(plainLanes.GetRange(0, Math.Min(3, plainLanes.Count)), Is.EqualTo(lanes.GetRange(0, Math.Min(3, plainLanes.Count))),
                "the bot rolls nothing: the kicks come as without it");
        }

        [Test]
        public void TheEndlessRoundHasNoShootOutAndEndsAfterThreeGoals()
        {
            var g = Started("endless", 3);
            Assert.That(g.Tune.Endless, Is.True);
            Assert.That(g.MaxGoals, Is.EqualTo(3));
            Run(g, 20.0);
            Assert.That(g.Finished, Is.True, "nobody dives — three goals in short order");
            Assert.That(g.Goals, Is.EqualTo(3));
            Assert.That(g.InShootout, Is.False);
        }
    }
}
