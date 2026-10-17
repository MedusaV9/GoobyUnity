using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Hasenhüpfer round (port of bunny_hop.gd) over the certified BunnyHop rules.</summary>
    public class BunnyHopGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(BunnyHopGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        /// <summary>An autopilot that hops whenever Gooby sinks under the next gap's centre — flies until the given time.</summary>
        private static void Autopilot(BunnyHopGame g, double seconds)
        {
            if (!g.Started) g.Hop();
            for (var s = 0.0; s < seconds && !g.Finished; s += Dt)
            {
                // the gap to hold: the nearest pillar Gooby has not fully cleared yet (a passed one still overlaps for ~0.6 units)
                var next = g.Pillars.FirstOrDefault(p => p.X - g.Scroll > g.GoobyX - 0.7);
                var target = next?.GapCenterY ?? BunnyHopGame.HoverY;
                if (g.GoobyY < target - 0.15 && g.GoobyVy < 0.6) g.Hop();
                g.Tick(Dt);
            }
        }

        [Test]
        public void BeforeTheFirstTapGoobyHoversAndNothingScrolls()
        {
            var g = new BunnyHopGame("normal", 7);
            Skip(g, BunnyHopGame.IntroSec + 2.0);
            Assert.That(g.Started, Is.False);
            Assert.That(g.Scroll, Is.EqualTo(0.0));
            Assert.That(g.Pillars, Is.Empty, "no gates before the start");
            Assert.That(g.GoobyY, Is.EqualTo(BunnyHopGame.HoverY).Within(BunnyHopGame.HoverAmp + 1e-9), "hovering");
            Assert.That(g.Hop(), Is.True);
            Assert.That(g.Started, Is.True);
            Assert.That(g.GoobyVy, Is.EqualTo(g.Tune.HopVy));
            g.Tick(Dt);
            Assert.That(g.Scroll, Is.GreaterThan(0.0), "the world scrolls from the first hop");
            Assert.That(g.Pillars.Count, Is.GreaterThan(0), "the first pillars are born at the right margin");
            Assert.That(g.Pillars[0].X, Is.EqualTo(g.ViewWidthWorld + BunnyHopGame.SpawnMargin).Within(1e-9));
            var second = g.Pillars.Count > 1 ? g.Pillars[1].X : g.Pillars[0].X + g.Tune.PillarSpacingX;
            Assert.That(second - g.Pillars[0].X, Is.EqualTo(g.Tune.PillarSpacingX).Within(1e-9));
        }

        [Test]
        public void WithoutTapsGoobyFallsToTheFloorAndCrashes()
        {
            var g = new BunnyHopGame("normal", 3);
            Skip(g, BunnyHopGame.IntroSec + 0.1);
            g.Hop();
            var crashed = 0;
            g.Crashed_ += () => crashed++;
            Skip(g, 4.0);
            Assert.That(g.Crashed, Is.True, "gravity wins");
            Assert.That(g.Finished, Is.True);
            Assert.That(crashed, Is.EqualTo(1));
            Assert.That(g.FinalScore, Is.EqualTo(0));
            Assert.That(g.Hop(), Is.False, "over");
            var before = g.Scroll;
            g.Tick(Dt);
            Assert.That(g.Scroll, Is.EqualTo(before), "the world stands after the crash");
        }

        [Test]
        public void TheAutopilotPassesGatesForPointsAndTheGapNarrowsAtTheTenth()
        {
            var g = new BunnyHopGame("easy", 11);
            Skip(g, BunnyHopGame.IntroSec + 0.1);
            var points = new List<int>();
            g.GatePassed += (p, gusting) => points.Add(p);
            var narrowed = 0;
            g.Narrowed += () => narrowed++;
            Autopilot(g, 60.0);
            Assert.That(g.Gates, Is.GreaterThanOrEqualTo(10), "the autopilot flies through at least ten gates on easy");
            Assert.That(points.Count, Is.EqualTo(g.Gates));
            Assert.That(points.All(p => p == 1 || p == 2), Is.True, "one point per gate, two in the wind");
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(points.Sum()), "gates plus any coins");
            Assert.That(narrowed, Is.GreaterThanOrEqualTo(1), "the gap narrows every ten gates");
            Assert.That(BunnyHop.GapAtGate(10, g.Tune), Is.LessThan(BunnyHop.GapAtGate(9, g.Tune)));
            Assert.That(BunnyHop.SpeedAtGate(g.Gates, g.Tune), Is.GreaterThan(g.Tune.BaseSpeed), "faster with every gate");
            Assert.That(g.Pillars.All(p => p.X - g.Scroll > -2.0), Is.True, "passed pillars are dropped behind Gooby");
        }

        [Test]
        public void TheWindShovesGoobyOnceAGustAndGatesCountDoubleMeanwhile()
        {
            var g = new BunnyHopGame("easy", 5);
            var gusts = new List<int>();
            g.Gust += d => gusts.Add(d);
            Skip(g, BunnyHopGame.IntroSec + 0.1);
            // the autopilot flies the gaps through the first gust at second 6
            Autopilot(g, 8.0);
            Assert.That(g.Elapsed, Is.GreaterThan(g.Tune.GustFirstSec));
            Assert.That(gusts.Count, Is.EqualTo(1), "the first gust fired once");
            Assert.That(gusts[0], Is.EqualTo(1), "the first gust blows upwards");
            Assert.That(BunnyHop.GustPhaseAt(g.Tune.GustFirstSec - 0.5, g.Tune).phase, Is.EqualTo("telegraph"));
            Assert.That(BunnyHop.GustPhaseAt(g.Tune.GustFirstSec + 0.5, g.Tune).phase, Is.EqualTo("gust"));
            Assert.That(BunnyHop.GatePoints(true), Is.EqualTo(2));
        }

        [Test]
        public void TheRunIsSeededAndTheHardModeNarrowsTheGaps()
        {
            var a = new BunnyHopGame("normal", 21); var b = new BunnyHopGame("normal", 21);
            Skip(a, BunnyHopGame.IntroSec + 0.1); Skip(b, BunnyHopGame.IntroSec + 0.1);
            a.Hop(); b.Hop(); a.Tick(Dt); b.Tick(Dt);
            Assert.That(a.Pillars.Select(p => p.GapCenterY), Is.EqualTo(b.Pillars.Select(p => p.GapCenterY)));
            var hard = new BunnyHopGame("hard", 21);
            Assert.That(hard.Tune.GapBase, Is.LessThan(BunnyHop.Hop.GapBase));
            var easy = new BunnyHopGame("easy", 21);
            Assert.That(easy.Tune.GapBase, Is.GreaterThan(BunnyHop.Hop.GapBase));
            Assert.That(a.GoobyX, Is.EqualTo(a.ViewWidthWorld * BunnyHopGame.GoobyXFrac).Within(1e-9));
            Assert.That(a.ViewWidthWorld, Is.EqualTo((BunnyHopGame.WorldHalfH * 2.0 + BunnyHopGame.ViewMarginH) * 390.0 / 844.0).Within(1e-9));
        }
    }
}
