using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The Geisterjagd round for the host: the certified GhostHunt sim behind the intro beat and typed events.</summary>
    public class GhostHuntGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(GhostHuntGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        [Test]
        public void GhostsPeekOutAfterTheIntroAndATapCatchesThemWithTheChainBonus()
        {
            var g = new GhostHuntGame("normal", 7);
            var caught = new List<(int spot, int points, int chain)>();
            g.Caught += (s, p, c) => caught.Add((s, p, c));
            Assert.That(g.TapMiss(), Is.Null, "nothing during the intro");
            Skip(g, GhostHuntGame.IntroSec + 0.01);
            Assert.That(g.State.Ghosts, Is.Empty, "the first ghost comes at 0.8 s");
            Skip(g, 0.9);
            Assert.That(g.State.Ghosts.Count, Is.EqualTo(1));
            var ghost = g.State.Ghosts[0];
            Assert.That(new[] { "grave", "pumpkin", "crypt" }, Does.Contain(GhostHunt.Spots[ghost.Spot].Kind));
            Assert.That(g.Rise(ghost), Is.GreaterThan(0.0).And.LessThanOrEqualTo(1.0), "rising out of its spot");
            Skip(g, ghost.Dur * g.Tune.RiseFrac);
            Assert.That(g.Rise(ghost), Is.EqualTo(1.0), "up");
            Assert.That(g.TapGhost(ghost.Id), Is.EqualTo("ghost"));
            var t1 = g.Elapsed;
            Assert.That(caught.Count, Is.EqualTo(1));
            Assert.That(caught[0].points, Is.EqualTo(g.Tune.CatchPoints), "+3, no chain yet");
            Assert.That(g.Score, Is.EqualTo(3));
            // the next ghost: a chain of two when it comes within the window, otherwise a fresh chain of one
            var guard = 0;
            while (g.State.Ghosts.Count == 0 && guard++ < 600) g.Tick(Dt);
            Assert.That(g.State.Ghosts.Count, Is.GreaterThan(0));
            Assert.That(g.TapGhost(g.State.Ghosts[0].Id), Is.EqualTo("ghost"));
            var expectedChain = g.Elapsed - t1 <= g.Tune.ChainWindowSec ? 2 : 1;
            Assert.That(caught[1].chain, Is.EqualTo(expectedChain));
            Assert.That(caught[1].points, Is.EqualTo(g.Tune.CatchPoints + GhostHunt.ChainBonus(caught[1].chain, g.Tune)));
            Assert.That(g.TapGhost(999), Is.EqualTo("miss"), "a ghost that is not there");
        }

        [Test]
        public void ADecoyCostsTwoAndBreaksTheChainAMissOnlyBreaksIt()
        {
            var g = new GhostHuntGame("normal", 3);
            var decoys = 0;
            g.DecoyTapped += _ => decoys++;
            Skip(g, GhostHuntGame.IntroSec + 0.01);
            // run until a pumpkin flickers
            var guard = 0;
            while (g.State.Flickers.Count == 0 && guard++ < 60 * 60) g.Tick(Dt);
            Assert.That(g.State.Flickers.Count, Is.GreaterThan(0), "a decoy flickers within the minute");
            var before = g.Score;
            Assert.That(g.TapDecoy(g.State.Flickers[0].Decoy), Is.EqualTo("decoy"));
            Assert.That(decoys, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(System.Math.Max(0, before + g.Tune.DecoyPenalty)));
            Assert.That(g.State.Chain, Is.EqualTo(0));
            Assert.That(g.TapMiss(), Is.EqualTo("miss"));
            Assert.That(g.State.Chain, Is.EqualTo(0));
        }

        [Test]
        public void TheBooWaveComesAt25SecondsAndTheRoundEndsAt90()
        {
            var g = new GhostHuntGame("normal", 5);
            var waves = new List<int>();
            g.BooWave += n => waves.Add(n);
            var ended = 0;
            g.Finished_ += () => ended++;
            Skip(g, GhostHuntGame.IntroSec + 26.0);
            Assert.That(waves.Count, Is.EqualTo(1), "the first boo wave at second 25");
            Assert.That(waves[0], Is.EqualTo(g.Tune.BooCount));
            Assert.That(g.State.BooActive, Is.Not.Null);
            Skip(g, 70.0);
            Assert.That(g.Finished, Is.True, "90 s round");
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(g.FinalScore, Is.EqualTo(0), "an idle hunter scores nothing");
            Assert.That(g.TapMiss(), Is.Null);
        }

        [Test]
        public void TokensAppearInTheirWindowsAndTheLanternRevealsForThreeSeconds()
        {
            var g = new GhostHuntGame("normal", 9);
            var powerUps = new List<string>();
            g.PowerUp += k => powerUps.Add(k);
            Skip(g, GhostHuntGame.IntroSec + 11.0);
            var guard = 0;
            while (g.State.Tokens.Count == 0 && guard++ < 60 * 8) g.Tick(Dt);
            Assert.That(g.State.Tokens.Count, Is.GreaterThan(0), "the first lantern token in its window (12–18 s)");
            Assert.That(g.Elapsed, Is.InRange(12.0, 18.1));
            Assert.That(g.State.Tokens[0].Kind, Is.EqualTo("lantern"));
            Assert.That(g.TapToken(g.State.Tokens[0].Window), Is.EqualTo("token"));
            Assert.That(powerUps, Is.EqualTo(new[] { "lantern" }));
            Assert.That(g.State.LanternT, Is.EqualTo(g.Tune.LanternSec).Within(1e-9));
            Skip(g, 1.0);
            Assert.That(g.State.LanternT, Is.EqualTo(g.Tune.LanternSec - 1.0).Within(0.05));
        }
    }
}
