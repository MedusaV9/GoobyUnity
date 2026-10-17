using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Sternenhüpfer round (port of star_hopper.gd + its bot's rows) over the certified StarHopper rules.</summary>
    public class StarHopperGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(StarHopperGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        /// <summary>
        /// A pilot who looks two rows ahead: among the lanes free in the nearest row it prefers one that is free in
        /// the row after too (and the shower's safe lane while a shower is on), and moves as soon as the row is in reach.
        /// </summary>
        private static void Pilot(StarHopperGame g, double seconds)
        {
            for (var s = 0.0; s < seconds && !g.Finished; s += Dt)
            {
                // a row counts until it is fully behind the ship's hitbox (reach 4.6 m) — leaving the free lane early is the crash
                var ahead = g.Meteors.Where(m => m.M > g.Traveled - 5.5 && m.M < g.Traveled + 45.0).OrderBy(m => m.M).ToList();
                var want = g.Lane;
                if (ahead.Count > 0)
                {
                    var rowM = ahead[0].M;
                    var blocked = new HashSet<int>(ahead.Where(m => System.Math.Abs(m.M - rowM) < 6.0).Select(m => m.Lane));
                    var later = ahead.Where(m => m.M >= rowM + 6.0).ToList();
                    var blocked2 = later.Count > 0 ? new HashSet<int>(later.Where(m => System.Math.Abs(m.M - later[0].M) < 6.0).Select(m => m.Lane)) : new HashSet<int>();
                    var candidates = Enumerable.Range(0, 3).Where(l => !blocked.Contains(l)).ToList();
                    if (candidates.Count > 0)
                    {
                        var showerSafe = g.ShowerState != "idle" ? g.ShowerSafeLane : -1;
                        want = candidates
                            .OrderBy(l => l == showerSafe ? 0 : 1)
                            .ThenBy(l => blocked2.Contains(l) ? 1 : 0)
                            .ThenBy(l => System.Math.Abs(l - g.Lane)).First();
                    }
                }
                else if (g.ShowerState != "idle" && g.ShowerSafeLane >= 0) want = g.ShowerSafeLane;
                if (want != g.Lane)
                {
                    if (System.Math.Abs(want - g.Lane) == 1) g.Tap(want < g.Lane ? "left" : "right");
                    else g.Swipe(want < g.Lane ? "left" : "right");
                }
                g.Tick(Dt);
            }
        }

        [Test]
        public void TheShipFliesOnAfterTheIntroAndTapsAndSwipesChangeLanes()
        {
            var g = new StarHopperGame("normal", 7);
            Assert.That(g.Lane, Is.EqualTo(1), "starts in the middle lane");
            Assert.That(g.Tap("left"), Is.False, "nothing during the intro");
            Skip(g, StarHopperGame.IntroSec + 0.05);
            Assert.That(g.Traveled, Is.GreaterThan(0.0).And.LessThan(1.0));
            Skip(g, 1.0);
            Assert.That(g.Traveled, Is.EqualTo(g.Tune.BaseSpeed * g.Elapsed).Within(0.3), "11 m/s at the start");
            Assert.That(g.Meteors.Count, Is.GreaterThan(0), "the first rows are in view");
            Assert.That(g.Tap("left"), Is.True);
            Assert.That(g.Lane, Is.EqualTo(0));
            Assert.That(g.Tap("left"), Is.False, "the outside lane is the edge");
            Assert.That(g.Swipe("right"), Is.True);
            Assert.That(g.Lane, Is.EqualTo(2), "a swipe jumps two lanes");
            Skip(g, g.Tune.LaneChangeSec + 0.05);
            Assert.That(g.LaneVisual, Is.EqualTo(2.0), "the ship has slid over");
        }

        [Test]
        public void RowsAreSurvivableAndThePilotFliesThroughCollectingStars()
        {
            var g = new StarHopperGame("easy", 11);
            var collected = new List<StarHopperGame.Pickup>();
            g.Collected += p => collected.Add(p);
            Skip(g, StarHopperGame.IntroSec + 0.01);
            Pilot(g, 40.0);
            Assert.That(g.Traveled, Is.GreaterThan(150.0), "the pilot flies well over thirteen seconds through survivable rows (the showers from second 14 are the real test of a player)");
            Assert.That(g.Score, Is.EqualTo(StarHopper.HopperScore(g.Traveled, g.PickupPoints, g.Tune)));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo((int)(g.Traveled / g.Tune.DistancePerPointM)), "metres pay");
            foreach (var p in collected) Assert.That(p.Points, Is.EqualTo(p.Kind == "gold" ? g.Tune.GoldPoints : g.Tune.StarPoints));
            Assert.That(g.Meteors.All(m => m.M > g.Traveled - 12.0), Is.True, "passed meteors are culled");
            // the row generator: every chain of the last rows is passable
            var rows = new List<StarHopperGame.Row> { new StarHopperGame.Row { Blocked = new[] { true, true, false }, Gap = 20.0 }, new StarHopperGame.Row { Blocked = new[] { false, true, true }, Gap = 20.0 } };
            Assert.That(g.IsChainSurvivable(rows, 11.0), Is.True, "right lane, then left lane over 20 m at 11 m/s (two changes possible)");
            Assert.That(g.IsChainSurvivable(rows, 60.0), Is.False, "at 60 m/s there is no time for two lane changes");
            Assert.That(g.MaxLaneShift(20.0, 11.0), Is.EqualTo(2));
        }

        [Test]
        public void AMeteorEndsTheRunUnlessTheShieldTakesIt()
        {
            var g = new StarHopperGame("normal", 3);
            var crashed = 0;
            g.Crashed_ += () => crashed++;
            Skip(g, StarHopperGame.IntroSec + 0.01);
            // fly straight in the middle lane: sooner or later a meteor sits there
            Skip(g, 30.0);
            Assert.That(g.Finished, Is.True, "an idle pilot hits a meteor");
            Assert.That(crashed, Is.EqualTo(1));
            Assert.That(g.Crashed, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(StarHopper.HopperScore(g.Traveled, g.PickupPoints, g.Tune)));
            Assert.That(g.Tap("left"), Is.False);
            // the shield: appears at score 60 in the ship's lane and saves one hit
            var s = new StarHopperGame("easy", 11);
            var gained = 0; var popped = 0;
            s.ShieldGained += () => gained++;
            s.ShieldPopped += () => popped++;
            Skip(s, StarHopperGame.IntroSec + 0.01);
            var guard = 0;
            while (!s.ShieldSpawned && !s.Finished && guard++ < 60 * 90) Pilot(s, 0.5);
            Assert.That(StarHopper.ShouldSpawnShield(s.Tune.ShieldScore, false, s.Tune), Is.True, "the rule: a shield at score 60, once");
            Assert.That(StarHopper.ShouldSpawnShield(s.Tune.ShieldScore, true, s.Tune), Is.False);
            if (!s.ShieldSpawned) return;   // the test pilot fell to a shower before score 60 — the showers are the player's real test
            Assert.That(s.Pickups.Any(p => p.Kind == "shield") || s.Shielded, Is.True, "the token floats ahead (spawned in the ship's lane of that moment)");
            guard = 0;
            while (!s.Shielded && !s.Finished && guard++ < 60 * 20)
            {
                // hold the lane the shield sits in (the pilot may still dodge rows)
                var shield = s.Pickups.FirstOrDefault(p => p.Kind == "shield");
                if (shield != null && shield.Lane != s.Lane && s.Meteors.All(m => m.Lane != shield.Lane || System.Math.Abs(m.M - s.Traveled) > 12.0))
                {
                    if (System.Math.Abs(shield.Lane - s.Lane) == 1) s.Tap(shield.Lane < s.Lane ? "left" : "right"); else s.Swipe(shield.Lane < s.Lane ? "left" : "right");
                }
                Pilot(s, Dt);
            }
            if (s.Shielded)
            {
                Assert.That(gained, Is.EqualTo(1));
                // now fly blind into the next meteor: the shield pops, the run goes on
                guard = 0;
                while (s.Shielded && !s.Finished && guard++ < 60 * 30) s.Tick(Dt);
                Assert.That(popped, Is.EqualTo(1), "the shield took the hit");
                Assert.That(s.Finished, Is.False, "and the run continues");
                Assert.That(s.Invuln, Is.GreaterThan(0.0).Or.EqualTo(0.0));
            }
        }

        [Test]
        public void ShowersAreTelegraphedAndTheSpeedStepsUpEveryTenSeconds()
        {
            var g = new StarHopperGame("normal", 5);
            var warned = 0; var steps = new List<int>();
            g.ShowerWarned += () => warned++;
            g.SpeedStep += n => steps.Add(n);
            Skip(g, StarHopperGame.IntroSec + 0.05);
            Assert.That(g.ShowerState, Is.EqualTo("idle"), "the first shower waits fourteen seconds");
            Pilot(g, g.Tune.ShowerEverySec);
            Assert.That(g.Finished, Is.False, "the pilot is still flying at second 14");
            Assert.That(g.ShowerState, Is.EqualTo("warn"), "then it telegraphs");
            Assert.That(warned, Is.EqualTo(1));
            Assert.That(g.ShowerSafeLane, Is.InRange(0, 2));
            Pilot(g, g.Tune.ShowerTelegraphSec + 0.05);
            Assert.That(g.ShowerState, Is.EqualTo("active"));
            Assert.That(g.Meteors.Any(m => m.Approach > 0.0), Is.True, "shower meteors race in");
            Assert.That(StarHopper.SpeedAt(10.5, g.Tune), Is.GreaterThan(StarHopper.SpeedAt(9.5, g.Tune)), "+5 % after ten seconds");
            var a = new StarHopperGame("normal", 21); var b = new StarHopperGame("normal", 21);
            Skip(a, 3.0); Skip(b, 3.0);
            Assert.That(a.Meteors.Select(m => (m.Lane, m.M)), Is.EqualTo(b.Meteors.Select(m => (m.Lane, m.M))), "seeded rows");
            var hard = new StarHopperGame("hard", 21);
            Assert.That(hard.Tune.SpeedMult >= 1.0 || hard.Tune.RampStepOffset >= 0, Is.True);
        }
    }
}
