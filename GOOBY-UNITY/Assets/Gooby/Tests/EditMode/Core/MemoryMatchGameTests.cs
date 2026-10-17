using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Memory round (port of memory_match.gd) over the certified MemoryMatch rules.</summary>
    public class MemoryMatchGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(MemoryMatchGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        private static void Ready(MemoryMatchGame g) => Skip(g, MemoryMatchGame.IntroSec + g.Tune.RevealSec + 0.05);

        private static int PartnerOf(MemoryMatchGame g, int index) =>
            Enumerable.Range(0, g.Cards.Count).First(i => i != index && g.Cards[i].Face == g.Cards[index].Face);

        [Test]
        public void ADealtBoardShowsItsFacesForTheRevealWindowAndThenTakesFlips()
        {
            var g = new MemoryMatchGame("normal", 7);
            Assert.That(g.Layout.Pairs, Is.EqualTo(8), "level 1: 4×4 with eight pairs");
            Assert.That(g.Cards.Count, Is.EqualTo(16));
            foreach (var face in Enumerable.Range(0, 8)) Assert.That(g.Cards.Count(c => c.Face == face), Is.EqualTo(2));
            Assert.That(g.Revealing, Is.True, "the memorise window is open from the deal");
            Assert.That(g.Flip(0), Is.False, "no flips in the intro or the reveal");
            Skip(g, MemoryMatchGame.IntroSec + 0.05);
            Assert.That(g.Elapsed, Is.GreaterThan(0.0).And.LessThan(0.1), "the clock waits for the intro");
            Assert.That(g.CountdownFrac, Is.EqualTo(1.0).Within(0.1));
            Skip(g, g.Tune.RevealSec);
            Assert.That(g.Revealing, Is.False);
            Assert.That(g.CountdownFrac, Is.EqualTo(0.0));
            Assert.That(g.Flip(0), Is.True);
            Assert.That(g.Cards[0].State, Is.EqualTo(MemoryMatchGame.Up));
            Assert.That(g.Flip(0), Is.False, "a face-up card cannot be flipped again");
            var big = new MemoryMatchGame("normal", 7, level: 6);
            Assert.That(big.Layout.Pairs, Is.EqualTo(12), "from level 6: 4×6 with twelve pairs");
            Assert.That(big.Cards.Count, Is.EqualTo(24));
        }

        [Test]
        public void APairStaysAMissTurnsBackAndTheBoardEndsTheTimedRoundWithTheRuleScore()
        {
            var g = new MemoryMatchGame("normal", 3);
            Ready(g);
            var results = new List<bool>();
            g.Resolved += (hit, second) => results.Add(hit);
            // a miss: two different faces
            var other = Enumerable.Range(1, 15).First(i => g.Cards[i].Face != g.Cards[0].Face);
            Assert.That(g.Flip(0), Is.True);
            Assert.That(g.Flip(other), Is.True);
            Assert.That(g.Flip(2), Is.False, "two picked: the third waits for the resolve");
            Assert.That(g.ResolveLeft, Is.EqualTo(g.Tune.FlipSec + MemoryMatchGame.ResolveExtraSec).Within(1e-9));
            Skip(g, g.Tune.FlipSec + MemoryMatchGame.ResolveExtraSec + 0.05);
            Assert.That(results, Is.EqualTo(new[] { false }));
            Assert.That(g.Misses, Is.EqualTo(1));
            Assert.That(g.Cards[0].State, Is.EqualTo(MemoryMatchGame.Down));
            Assert.That(g.Cards[other].State, Is.EqualTo(MemoryMatchGame.Down));
            // the perfect rest: every pair in order
            var cleared = 0;
            g.BoardCleared += () => cleared++;
            for (var face = 0; face < 8; face++)
            {
                var first = Enumerable.Range(0, 16).First(i => g.Cards[i].Face == face);
                Assert.That(g.Flip(first), Is.True);
                Assert.That(g.Flip(PartnerOf(g, first)), Is.True);
                Skip(g, g.Tune.FlipSec + MemoryMatchGame.ResolveExtraSec + 0.05);
                Assert.That(g.Cards[first].State, Is.EqualTo(MemoryMatchGame.Matched));
            }
            Assert.That(g.MatchedPairs, Is.EqualTo(8));
            Assert.That(cleared, Is.EqualTo(1));
            Assert.That(g.Finished, Is.True, "a timed round ends with the board");
            Assert.That(g.FinalScore, Is.EqualTo(MemoryMatch.Score(1, g.Elapsed, g.Layout, g.Tune)));
            Assert.That(g.FinalScore, Is.EqualTo(20 - 1 + 8 + 20), "well under par: the full time bonus");
            Assert.That(g.Flip(0), Is.False);
        }

        [Test]
        public void ThreeCleanMatchesEarnOnePeekThatShowsTheBoardForASecond()
        {
            var g = new MemoryMatchGame("normal", 5);
            Ready(g);
            var earned = 0;
            g.PeekEarned += () => earned++;
            Assert.That(g.UsePeek(), Is.False, "nothing earned yet");
            for (var face = 0; face < 3; face++)
            {
                var first = Enumerable.Range(0, 16).First(i => g.Cards[i].Face == face);
                g.Flip(first); g.Flip(PartnerOf(g, first));
                Skip(g, g.Tune.FlipSec + MemoryMatchGame.ResolveExtraSec + 0.05);
            }
            Assert.That(g.CleanMatches, Is.EqualTo(3));
            Assert.That(g.CanUsePeek, Is.True);
            Assert.That(earned, Is.EqualTo(1));
            Assert.That(g.UsePeek(), Is.True);
            Assert.That(g.Revealing, Is.True);
            Assert.That(g.PeekLeft, Is.EqualTo(g.Tune.PeekSec).Within(1e-9));
            Assert.That(g.Flip(15), Is.False, "no flips while peeking");
            Skip(g, g.Tune.PeekSec + 0.05);
            Assert.That(g.Revealing, Is.False);
            Assert.That(g.CanUsePeek, Is.False, "one peek per round");
            Assert.That(g.UsePeek(), Is.False);
            // a miss resets the clean streak, and a used peek is not earned again
            var a = Enumerable.Range(0, 16).First(i => g.Cards[i].State == MemoryMatchGame.Down);
            var b = Enumerable.Range(0, 16).First(i => g.Cards[i].State == MemoryMatchGame.Down && g.Cards[i].Face != g.Cards[a].Face);
            g.Flip(a); g.Flip(b);
            Skip(g, 1.0);
            Assert.That(g.CleanMatches, Is.EqualTo(0));
            Assert.That(g.MatchStreak, Is.EqualTo(0));
        }

        [Test]
        public void EndlessDealsTheNextBoardAndEndsWhenTheMissesRunOut()
        {
            var g = new MemoryMatchGame("endless", 11);
            Assert.That(g.Tune.Endless, Is.True);
            Ready(g);
            var boards = 0;
            g.BoardCleared += () => boards++;
            for (var face = 0; face < 8; face++)
            {
                var first = Enumerable.Range(0, 16).First(i => g.Cards[i].Face == face);
                g.Flip(first); g.Flip(PartnerOf(g, first));
                Skip(g, g.Tune.FlipSec + MemoryMatchGame.ResolveExtraSec + 0.05);
            }
            Assert.That(boards, Is.EqualTo(1));
            Assert.That(g.Finished, Is.False, "endless chains the next board");
            Assert.That(g.Revealing, Is.True, "the new board shows its faces");
            Assert.That(g.Cards.All(c => c.State == MemoryMatchGame.Down), Is.True);
            Skip(g, g.Tune.RevealSec + 0.05);
            // twelve misses end it
            while (!g.Finished)
            {
                var a = Enumerable.Range(0, 16).First(i => g.Cards[i].State == MemoryMatchGame.Down);
                var b = Enumerable.Range(0, 16).First(i => g.Cards[i].State == MemoryMatchGame.Down && g.Cards[i].Face != g.Cards[a].Face);
                Assert.That(g.Flip(a) && g.Flip(b), Is.True);
                Skip(g, 1.0);
            }
            Assert.That(g.Misses, Is.EqualTo(g.Tune.EndlessMissFlips));
            Assert.That(g.BoardsCleared, Is.EqualTo(1));
        }

        [Test]
        public void TheDealIsSeededAndTheHardModeShortensTheWindows()
        {
            var a = new MemoryMatchGame("normal", 42);
            var b = new MemoryMatchGame("normal", 42);
            Assert.That(a.Cards.Select(c => c.Face), Is.EqualTo(b.Cards.Select(c => c.Face)));
            var hard = new MemoryMatchGame("hard", 42);
            Assert.That(hard.Tune.RevealSec, Is.LessThan(MemoryMatch.Memory.RevealSec));
            Assert.That(hard.Tune.FlipSec, Is.LessThan(MemoryMatch.Memory.FlipSec));
            var easy = new MemoryMatchGame("easy", 42);
            Assert.That(easy.Tune.RevealSec, Is.GreaterThan(MemoryMatch.Memory.RevealSec));
            Assert.That(a.Cards[0].Key, Is.EqualTo(MemoryMatch.FaceKeys[a.Cards[0].Face]));
        }
    }
}
