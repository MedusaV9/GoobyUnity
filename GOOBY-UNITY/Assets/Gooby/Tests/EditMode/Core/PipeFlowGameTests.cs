using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class PipeFlowGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(PipeFlowGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        [Test]
        public void SetupDealsPuzzleOneBeforeTheIntroAndGatesTaps()
        {
            var expected = PipeFlow.GenerateBoard(7 * 1009 + 1, PipeFlow.Pipe);
            var g = new PipeFlowGame("normal", 7);
            Assert.That(g.PuzzleNo, Is.EqualTo(1));
            Assert.That(g.Size, Is.EqualTo(5));
            Assert.That(g.Board.SrcCol, Is.EqualTo(expected.SrcCol), "deal formula is runSeed·1009 + n");
            Assert.That(g.Board.GoalCol, Is.EqualTo(expected.GoalCol));
            Assert.That(g.Tap(0), Is.False, "intro gates taps");
            Assert.That(g.TotalTaps, Is.EqualTo(0));
            Run(g, 1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0), "the clock waits out the intro");
            Assert.That(g.PuzzleElapsed, Is.EqualTo(0.0));
        }

        [Test]
        public void TheSolverConnectsTheOpenerAndTheFillWaveServesTheNextPuzzle()
        {
            var g = new PipeFlowGame("normal", 3);
            var solved = 0;
            g.Solved_ += () => solved++;
            Run(g, PipeFlowGame.IntroSec + 0.02);
            Assert.That(g.ApplySolution(), Is.GreaterThan(0));
            Assert.That(solved, Is.EqualTo(1));
            Assert.That(g.Filling, Is.True);
            Assert.That(g.Tap(0), Is.False, "fill gates taps");
            Assert.That(g.Score, Is.EqualTo(PipeFlow.PipeScore(1, g.TotalTaps, g.OptimalTaps, g.Tune, 0)));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(25));
            Run(g, 4.0);
            Assert.That(g.Filling, Is.False);
            Assert.That(g.PuzzleNo, Is.EqualTo(2));
            Assert.That(g.Solved, Is.EqualTo(1));
        }

        [Test]
        public void ATapRotatesTheTileAndGrowsTheReachFromTheTap()
        {
            var g = new PipeFlowGame("normal", 11);
            Run(g, PipeFlowGame.IntroSec + 0.02);
            var before = g.Board.Tiles[0].Rot;
            Assert.That(g.Tap(0), Is.True);
            Assert.That(g.Board.Tiles[0].Rot, Is.EqualTo((before + 1) % 4));
            Assert.That(g.TotalTaps, Is.EqualTo(1));
            Assert.That(g.Spin[0], Is.GreaterThan(0.0));
        }

        [Test]
        public void PuzzleThreeLeaksAfterTwentyFiveSeconds()
        {
            var g = new PipeFlowGame("normal", 5);
            var leaks = 0;
            g.Leaked += () => leaks++;
            Run(g, PipeFlowGame.IntroSec + 0.02);
            g.ApplySolution();
            Run(g, 4.0); // fill → puzzle 2
            g.ApplySolution();
            Run(g, 4.0); // fill → puzzle 3
            Assert.That(g.PuzzleNo, Is.EqualTo(3));
            Assert.That(g.LeakIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(g.LeakPending, Is.True);
            var scoreBefore = g.Score;
            Run(g, g.Tune.LeakSec + 0.1);
            Assert.That(leaks, Is.EqualTo(1));
            Assert.That(g.Failures, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(scoreBefore - 5));
            Assert.That(g.Finished, Is.False);
        }

        [Test, Timeout(8000)]
        public void TheTimedRoundEndsAtNinetySecondsWithTheScoreBanked()
        {
            var g = new PipeFlowGame("normal", 9);
            Run(g, PipeFlowGame.IntroSec + 0.02);
            g.ApplySolution();
            Run(g, 95.0);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(90.0 - 0.05));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
            Assert.That(g.Tap(0), Is.False);
        }

        [Test, Timeout(8000)]
        public void EndlessEndsOnTheThirdLeakAndEasyLeaksLater()
        {
            var easy = new PipeFlowGame("easy", 1);
            Assert.That(easy.Tune.LeakFromPuzzle, Is.EqualTo(4));
            Assert.That(easy.Tune.LeakSec, Is.GreaterThan(PipeFlow.Pipe.LeakSec));

            var endless = new PipeFlowGame("endless", 4);
            Assert.That(endless.Tune.Endless, Is.True);
            Assert.That(endless.Tune.LeakFromPuzzle, Is.EqualTo(2));
            Run(endless, PipeFlowGame.IntroSec + 0.02);
            while (endless.PuzzleNo < endless.Tune.LeakFromPuzzle && !endless.Finished)
            {
                endless.ApplySolution();
                Run(endless, 4.0);
            }
            for (var n = 0; n < 3 && !endless.Finished; n++)
            {
                if (!endless.LeakPending)
                {
                    endless.ApplySolution();
                    Run(endless, 4.0);
                }
                Run(endless, endless.Tune.LeakSec + 0.2);
                if (endless.Finished) break;
                endless.ApplySolution();
                Run(endless, 4.0);
            }
            Assert.That(endless.Finished, Is.True);
            Assert.That(endless.Failures, Is.EqualTo(3));
        }

        [Test]
        public void WateredTilesFollowTheFillDepth()
        {
            var g = new PipeFlowGame("normal", 2);
            Run(g, PipeFlowGame.IntroSec + 0.02);
            g.ApplySolution();
            Assert.That(g.Filling, Is.True);
            Assert.That(g.TileWatered(g.Board.SrcCol), Is.False, "fill_depth starts at 0, src is depth 0 so not yet < 0");
            g.Tick(Dt); // consume fill_left=0 → fill_depth 1
            Assert.That(g.FillDepth, Is.EqualTo(1));
            Assert.That(g.TileWatered(g.Board.SrcCol), Is.True);
        }
    }
}
