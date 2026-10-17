using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class FishingPondGameTests
    {
        private static FishingPondGame Started(string mode = "normal", int seed = 5)
        {
            var g = new FishingPondGame(mode, seed);
            g.Tick(FishingPondGame.IntroSec + 0.01);
            return g;
        }

        private static void Run(FishingPondGame g, double seconds, double dt = 1.0 / 60.0)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += dt) g.Tick(dt);
        }

        /// <summary>
        /// Fishes for a swimmer of the wanted kind: casts when one will be over the hook by the time it has sunk to
        /// its depth, lets go the moment it is the nearest in reach. False when none came by within the patience.
        /// </summary>
        private static bool CastFor(FishingPondGame g, Func<FishingPondGame.Swimmer, bool> want, double patienceSec = 60.0)
        {
            for (var t = 0.0; t < patienceSec && !g.Finished; t += 1.0 / 60.0)
            {
                g.Tick(1.0 / 60.0);
                if (g.Phase == FishingPondGame.PhaseIdle)
                {
                    foreach (var f in g.Fish)
                    {
                        if (!want(f)) continue;
                        var eta = f.Depth / g.Tune.LowerSpeed;
                        var xThen = f.X + f.Dir * f.Speed * eta;
                        if (Math.Abs(xThen - g.Tune.HookX) < 0.3) { g.Press(); break; }
                    }
                }
                else if (g.Phase == FishingPondGame.PhaseLower)
                {
                    var idx = g.NearestCatch();
                    if (idx >= 0 && want(g.Fish[idx])) { g.Release(); return true; }
                    // missed it: once nothing else is in reach at the bottom, come up empty and wait for the next pass
                    if (g.HookDepth >= g.Tune.MaxDepth - 1e-9 && idx < 0) g.Release();
                }
            }
            return false;
        }

        private static void WaitIdle(FishingPondGame g)
        {
            for (var i = 0; i < 600 && g.Phase != FishingPondGame.PhaseIdle && !g.Finished; i++) g.Tick(1.0 / 60.0);
        }

        /// <summary>Cranks a hooked big one in (five quick taps).</summary>
        private static void ReelIn(FishingPondGame g)
        {
            for (var i = 0; i < 10 && g.Phase == FishingPondGame.PhaseReel && !g.Finished; i++) { g.Press(); g.Tick(0.1); }
        }

        /// <summary>One complete catch of a wanted swimmer, reeled if it is big, hook back on the surface afterwards.</summary>
        private static bool FishOne(FishingPondGame g, Func<FishingPondGame.Swimmer, bool> want, double patienceSec = 60.0)
        {
            var ok = CastFor(g, want, patienceSec);
            if (ok) ReelIn(g);
            WaitIdle(g);
            return ok;
        }

        /// <summary>Fishes the pond (everything but boots) until a swimmer of the wanted kind is in it — only catches respawn.</summary>
        private static bool EnsureSwimmer(FishingPondGame g, Func<FishingPondGame.Swimmer, bool> want)
        {
            for (var n = 0; n < 60 && !g.Fish.Any(want) && !g.Finished; n++)
            {
                if (!FishOne(g, f => !f.IsBoot)) return false;
            }
            return g.Fish.Any(want);
        }

        [Test]
        public void SevenSwimmersPatrolThePondFromTheStartAndTurnAtTheBanks()
        {
            var g = new FishingPondGame("normal", 5);
            Assert.That(g.Fish.Count, Is.EqualTo(7));
            foreach (var f in g.Fish)
            {
                Assert.That(Math.Abs(f.X), Is.LessThanOrEqualTo(g.Tune.PondHalfW));
                if (f.IsBoot) continue;
                Assert.That(f.Depth, Is.InRange(g.Tune.FishDepthMin, g.Tune.FishDepthMax));
                Assert.That(new[] { "S", "M", "L" }, Has.Member(f.Kind));
                Assert.That(FishingPond.SpeciesColors.ContainsKey(f.Species), Is.True, f.Species + " has a colour");
            }
            Assert.That(g.Fish.Any(f => f.IsBoot), Is.False, "no boot in the opening school (14 s minimum gap)");
            g.Tick(1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0), "the intro holds the clock");
            var xs = g.Fish.Select(f => f.X).ToList();
            g.Tick(0.6);
            Assert.That(g.Fish.Select(f => f.X).Zip(xs, (a, b) => Math.Abs(a - b)).Any(d => d > 0.1), Is.True, "but the fish already swim");
            Run(g, 20.0);
            foreach (var f in g.Fish) Assert.That(Math.Abs(f.X), Is.LessThanOrEqualTo(g.Tune.PondHalfW + 1e-9), "they turn at the banks");
        }

        [Test]
        public void HoldingLowersTheHookAtTwoPointOneMetresPerSecondToTheBottomAndLettingGoRaisesItFaster()
        {
            var g = Started();
            var cast = 0;
            g.Cast += () => cast++;
            g.Press();
            Assert.That(cast, Is.EqualTo(1));
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseLower));
            g.Tick(1.0);
            Assert.That(g.HookDepth, Is.EqualTo(2.1).Within(1e-9));
            g.Tick(2.0);
            Assert.That(g.HookDepth, Is.EqualTo(g.Tune.MaxDepth).Within(1e-9), "the bottom stops it");
            // clear the water around the hook so the release comes up empty
            g.Fish.Clear();
            var missed = 0;
            g.MissedCast += () => missed++;
            g.Release();
            Assert.That(missed, Is.EqualTo(1));
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseRaise));
            g.Tick(0.5);
            Assert.That(g.HookDepth, Is.EqualTo(g.Tune.MaxDepth - 1.7).Within(1e-9));
            g.Tick(1.0);
            Assert.That(g.HookDepth, Is.EqualTo(0.0));
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseIdle));
        }

        [Test]
        public void ASmallOrMediumFishInReachLandsAtOnceForTwoOrThreeAndIsReplacedAfterASecond()
        {
            var g = Started();
            var landed = new List<(FishingPondGame.Swimmer fish, int value)>();
            g.Landed += (f, v) => landed.Add((f, v));
            Assume.That(EnsureSwimmer(g, f => f.Kind == "S" || f.Kind == "M"), Is.True);
            landed.Clear();
            var before = g.Score;
            Assert.That(CastFor(g, f => f.Kind == "S" || f.Kind == "M"), Is.True);
            Assert.That(landed.Count, Is.EqualTo(1));
            var (fish, value) = landed[0];
            Assert.That(value, Is.EqualTo(fish.Kind == "S" ? 2 : 3));
            Assert.That(g.Score, Is.EqualTo(before + value));
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseRaise), "the hook comes back up");
            Assert.That(g.Hooked, Is.Null, "the fish is in the bucket already");
            Assert.That(g.Fish.Count, Is.EqualTo(6));
            Assert.That(g.CaughtSpecies.Last(), Is.EqualTo(fish.Species));
            Run(g, g.Tune.RespawnSec + 0.05);
            Assert.That(g.Fish.Count, Is.EqualTo(7), "a new swimmer took its place");
        }

        [Test]
        public void ABigFishNeedsFiveCranksWithinTwoSecondsOrTheLineBreaks()
        {
            var g = Started("normal", 9);
            FishingPondGame.Swimmer hooked = null;
            g.HookedFish += f => hooked = f;
            var broke = 0;
            g.LineBroke += () => broke++;
            Assume.That(EnsureSwimmer(g, f => f.Kind == "L"), Is.True, "a big one swims in the pond");
            var scoreBefore = g.Score;
            Assert.That(CastFor(g, f => f.Kind == "L", 90.0), Is.True, "a big one comes within reach");
            Assert.That(hooked.Kind, Is.EqualTo("L"));
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseReel));
            Assert.That(g.HasCatch, Is.True);
            Assert.That(g.ReelWindowLeft, Is.EqualTo(2.0).Within(1e-9));
            // too slow: the window runs out
            Run(g, 2.1);
            Assert.That(broke, Is.EqualTo(1));
            Assert.That(g.Hooked, Is.Null);
            Assert.That(g.Score, Is.EqualTo(scoreBefore), "nothing for a fish that got away");
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseRaise));
            WaitIdle(g);

            // the next big one, cranked in time: +5
            var landed = new List<(FishingPondGame.Swimmer fish, int value)>();
            g.Landed += (f, v) => landed.Add((f, v));
            Assume.That(EnsureSwimmer(g, f => f.Kind == "L"), Is.True, "another big one swims in");
            landed.Clear();
            scoreBefore = g.Score;
            Assert.That(CastFor(g, f => f.Kind == "L", 90.0), Is.True, "and comes within reach");
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseReel));
            for (var tap = 0; tap < 5; tap++) { g.Press(); g.Tick(0.1); }
            Assert.That(landed.Count, Is.EqualTo(1));
            Assert.That(landed[0].value, Is.EqualTo(5));
            Assert.That(g.Score, Is.EqualTo(scoreBefore + 5));
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseRaise));
        }

        [Test]
        public void ABootCostsThreeAndInEndlessThreeBootsOrBreaksEndTheRound()
        {
            var g = Started("endless", 4);
            Assert.That(g.Tune.Endless, Is.True);
            var boots = 0;
            g.Landed += (f, v) => { if (f.IsBoot) { boots++; Assert.That(v, Is.EqualTo(-3)); } };
            var failuresSeen = new List<int>();
            for (var round = 0; round < 3 && !g.Finished; round++)
            {
                // boots only drift in as replacements: fish until one shows (60 % per respawn once 14 s have passed)
                if (!EnsureSwimmer(g, f => f.IsBoot)) break;
                if (!CastFor(g, f => f.IsBoot, 120.0)) break;
                failuresSeen.Add(g.Failures);
                WaitIdle(g);
            }
            Assert.That(boots, Is.EqualTo(3), "three boots drift by in endless");
            Assert.That(failuresSeen, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(g.Finished, Is.True, "three failures end the endless round");
            Assert.That(g.Failures, Is.EqualTo(3));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(0), "the boot's −3 never takes the score below zero");
        }

        [Test]
        public void TheTimedRoundEndsAfterNinetySecondsAndThenIgnoresTheLine()
        {
            var g = Started();
            var finished = false;
            g.Finished_ += () => finished = true;
            Run(g, 89.0);
            Assert.That(g.Finished, Is.False);
            Run(g, 1.2);
            Assert.That(finished, Is.True);
            Assert.That(g.TimeLeft, Is.EqualTo(0.0));
            g.Press();
            Assert.That(g.Phase, Is.EqualTo(FishingPondGame.PhaseIdle), "no cast after the whistle");
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
        }

        [Test]
        public void TheCollectionIdsMapRaresOntoTheirCommonCousins()
        {
            var g = Started();
            g.CaughtSpecies.AddRange(new[] { "pearlMinnow", "sunsetKoi", "bigWhopper" });
            Assert.That(g.CollectionIds(), Is.EqualTo(new[] { "tinyMinnow", "pinkKoi", "bigWhopper" }));
            Assert.That(FishingPond.RareSetBonusFor(g.CaughtSpecies), Is.EqualTo(0), "the gilded whopper is still missing");
            g.CaughtSpecies.Add("gildedWhopper");
            Assert.That(FishingPond.RareSetBonusFor(g.CaughtSpecies), Is.EqualTo(15));
        }

        [Test]
        public void TheEasyPondIsMoreForgivingTheHardOneTighter()
        {
            var easy = new FishingPondGame("easy", 1);
            var hard = new FishingPondGame("hard", 1);
            Assert.That(easy.Tune.DurationSec, Is.EqualTo(108.0).Within(1e-9));
            Assert.That(hard.Tune.DurationSec, Is.EqualTo(90.0).Within(1e-9));
            Assert.That(easy.Tune.CatchRadius, Is.GreaterThan(hard.Tune.CatchRadius));
            Assert.That(easy.Tune.ReelWindowSec, Is.GreaterThan(hard.Tune.ReelWindowSec));
            Assert.That(easy.Tune.Endless, Is.False);
        }
    }
}
