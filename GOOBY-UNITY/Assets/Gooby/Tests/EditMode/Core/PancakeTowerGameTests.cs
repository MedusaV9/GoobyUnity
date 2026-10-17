using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Pfannkuchenturm round (port of pancake_tower.gd) over the certified PancakeTower rules.</summary>
    public class PancakeTowerGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(PancakeTowerGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        /// <summary>Waits until the swing passes within eps of the stack centre (tower coordinates), then drops and lets it land.</summary>
        private static void DropNear(PancakeTowerGame g, double targetLocalX, double eps)
        {
            var guard = 0;
            while (guard++ < 60 * 20)
            {
                var localX = PancakeTower.WobbleLocalX(g.SwingX, g.StackHeight, g.Wobble.Angle);
                if (System.Math.Abs(localX - targetLocalX) <= eps) break;
                g.Tick(Dt);
            }
            Assert.That(g.Drop(), Is.True);
            guard = 0;
            while (g.Falling && guard++ < 600) g.Tick(Dt);
        }

        [Test]
        public void ThePancakeSwingsAboveTheStackAfterTheIntroAndATapDropsIt()
        {
            var g = new PancakeTowerGame("normal", 7);
            Assert.That(g.Stack.Width, Is.EqualTo(g.Tune.BaseWidth));
            Assert.That(g.CurrentIndex, Is.EqualTo(1));
            Assert.That(g.Drop(), Is.False, "nothing during the intro");
            Skip(g, PancakeTowerGame.IntroSec + 0.01);
            var x0 = g.SwingX;
            Skip(g, 0.4);
            Assert.That(g.SwingX, Is.Not.EqualTo(x0), "the pancake swings");
            Assert.That(System.Math.Abs(g.SwingX), Is.LessThanOrEqualTo(g.Tune.SlideAmplitude + 1e-9));
            Assert.That(g.Drop(), Is.True);
            Assert.That(g.Falling, Is.True);
            Assert.That(g.FallY, Is.EqualTo(PancakeTowerGame.DropHeight).Within(1e-9), "1.5 above the empty plate");
            Assert.That(g.Drop(), Is.False, "one pancake in the air at a time");
            Skip(g, PancakeTowerGame.DropHeight / g.Tune.FallSpeed + 0.1);
            Assert.That(g.Falling, Is.False, "landed");
            Assert.That(g.Layers.Count, Is.EqualTo(1));
            Assert.That(g.CurrentIndex, Is.EqualTo(2));
            Assert.That(g.StackHeight, Is.EqualTo(g.Tune.LayerHeight).Within(1e-9));
        }

        [Test]
        public void PerfectDropsPayAndRestoreWidthOffCentreDropsCutTheOverhang()
        {
            var g = new PancakeTowerGame("normal", 3);
            Skip(g, PancakeTowerGame.IntroSec + 0.01);
            var drops = new List<PancakeTower.Drop>();
            g.Landed += (layer, drop) => drops.Add(drop);
            // a perfect: dropped when the swing passes the stack centre
            DropNear(g, g.Stack.Center, g.Tune.PerfectEps * 0.6);
            Assert.That(drops.Count, Is.EqualTo(1));
            Assert.That(drops[0].Perfect, Is.True);
            Assert.That(drops[0].Points, Is.EqualTo(g.Tune.PerfectPoints));
            Assert.That(g.Stack.Width, Is.EqualTo(g.Tune.BaseWidth), "a perfect on a full plate stays at full width");
            Assert.That(g.Perfects, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(1 * g.Tune.PointsPerLayer + g.Tune.PerfectPoints));
            // off-centre: the overhang is cut, the stack narrows and re-centres
            DropNear(g, g.Stack.Center + 0.5, 0.06);
            Assert.That(drops.Count, Is.EqualTo(2));
            Assert.That(drops[1].Perfect, Is.False);
            Assert.That(drops[1].Cut.Present, Is.True, "the overhang is cut off");
            Assert.That(g.Stack.Width, Is.LessThan(g.Tune.BaseWidth));
            Assert.That(g.Stack.Width, Is.EqualTo(g.Tune.BaseWidth - drops[1].Cut.Size).Within(1e-6));
            Assert.That(g.Crumbs.Count, Is.EqualTo(1), "the cut piece tumbles down as a crumb");
            Assert.That(g.PerfectStreak, Is.EqualTo(0));
            // a perfect on the narrowed stack restores ten percent
            var narrow = g.Stack.Width;
            DropNear(g, g.Stack.Center, g.Tune.PerfectEps * 0.6);
            Assert.That(g.Stack.Width, Is.EqualTo(System.Math.Min(g.Tune.BaseWidth, narrow + g.Tune.BaseWidth * g.Tune.PerfectRestorePct)).Within(1e-6));
            Assert.That(g.Layers.Count, Is.EqualTo(3));
            Assert.That(g.Score, Is.EqualTo(3 * g.Tune.PointsPerLayer + 2 * g.Tune.PerfectPoints));
        }

        [Test]
        public void EveryFifthLayerIsAToppingThatNeverShrinksTheStack()
        {
            var g = new PancakeTowerGame("easy", 11);
            Skip(g, PancakeTowerGame.IntroSec + 0.01);
            for (var i = 0; i < 4; i++) DropNear(g, g.Stack.Center, g.Tune.PerfectEps * 0.6);
            Assert.That(g.CurrentIndex, Is.EqualTo(5));
            Assert.That(g.CurrentIsTopping, Is.True, "the fifth pancake is a topping");
            var width = g.Stack.Width;
            var bonusBefore = g.BonusPoints;
            DropNear(g, g.Stack.Center + 0.4, 0.06);   // off-centre on purpose
            Assert.That(g.Layers.Last().Topping, Is.True);
            Assert.That(g.Stack.Width, Is.EqualTo(width), "a topping never shrinks the stack");
            Assert.That(g.BonusPoints, Is.EqualTo(bonusBefore + g.Tune.ToppingPoints), "+4 for the topping");
        }

        [Test]
        public void AMissedStackTopplesTheTowerAndEndsTheRoundWithTheScoreSoFar()
        {
            var g = new PancakeTowerGame("normal", 5);
            Skip(g, PancakeTowerGame.IntroSec + 0.01);
            DropNear(g, g.Stack.Center, g.Tune.PerfectEps * 0.6);
            var toppled = 0;
            g.Toppled_ += x => toppled++;
            // drops at the far end of the swing: the first cuts the stack down to a sliver, the next misses it
            var rounds = 0;
            while (!g.Finished && rounds++ < 6)
            {
                var guard = 0;
                while (System.Math.Abs(g.SwingX) < g.Tune.SlideAmplitude * 0.97 && guard++ < 600) g.Tick(Dt);
                g.Drop();
                guard = 0;
                while (g.Falling && guard++ < 600) g.Tick(Dt);
            }
            Assert.That(toppled, Is.EqualTo(1), "the pancake beyond the edge tumbles");
            Assert.That(g.Toppled, Is.True);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Layers.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.FinalScore, Is.EqualTo(g.Layers.Count * g.Tune.PointsPerLayer + g.BonusPoints), "the layers before the fall still count");
            Assert.That(g.Drop(), Is.False);
        }

        [Test]
        public void TheTowerWobblesFromLayerEightAndTheSwingSpeedsUpPerLayer()
        {
            var g = new PancakeTowerGame("normal", 9);
            Skip(g, PancakeTowerGame.IntroSec + 0.01);
            Assert.That(PancakeTower.SlidePeriod(2, g.Tune), Is.LessThan(PancakeTower.SlidePeriod(1, g.Tune)), "faster with every layer");
            for (var i = 0; i < 9 && !g.Finished; i++) DropNear(g, g.Stack.Center, g.Tune.PerfectEps * 0.6);
            Assert.That(g.Layers.Count, Is.EqualTo(9));
            Skip(g, 1.0);
            Assert.That(System.Math.Abs(g.Wobble.Angle), Is.GreaterThan(0.0), "the tower sways from layer eight");
            Assert.That(System.Math.Abs(g.Wobble.Angle), Is.LessThanOrEqualTo(g.Tune.WobbleMaxRad));
            var a = new PancakeTowerGame("normal", 21); var b = new PancakeTowerGame("normal", 21);
            Assert.That(a.SlidePhase, Is.EqualTo(b.SlidePhase), "seeded swing");
            var hard = new PancakeTowerGame("hard", 21);
            Assert.That(hard.Tune.PerfectEps, Is.LessThan(PancakeTower.Pancake.PerfectEps));
        }
    }
}
