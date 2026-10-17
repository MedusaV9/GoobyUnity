using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>Hafen-Hopser — parity with the web gold values (test_mg3_harbor_hopper.gd) plus the realtime wrapper.</summary>
    public class HarborHopperTests
    {
        private static readonly Dictionary<string, int[]> GoldScore = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 97, 110, 102, 94, 91 },
            ["normal"] = new[] { 66, 108, 82, 83, 70 },
            ["hard"] = new[] { 107, 98, 124, 103, 71 },
            ["endless"] = new[] { 65, 109, 71, 89, 47 },
        };
        private static readonly Dictionary<string, int[]> GoldCrates = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 21, 24, 18, 22, 20 },
            ["normal"] = new[] { 13, 24, 15, 18, 13 },
            ["hard"] = new[] { 24, 20, 26, 24, 17 },
            ["endless"] = new[] { 16, 24, 16, 20, 12 },
        };
        private static readonly Dictionary<string, int[]> GoldBumps = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 1, 0, 0, 2, 1 },
            ["normal"] = new[] { 2, 0, 0, 1, 0 },
            ["hard"] = new[] { 1, 2, 2, 1, 5 },
            ["endless"] = new[] { 3, 3, 3, 3, 3 },
        };
        private static readonly Dictionary<string, int[]> GoldDist = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 798, 804, 798, 791, 787 },
            ["normal"] = new[] { 755, 788, 777, 770, 781 },
            ["hard"] = new[] { 932, 917, 913, 932, 909 },
            ["endless"] = new[] { 666, 994, 689, 839, 404 },
        };
        private static readonly Dictionary<string, double> GoldMean = new Dictionary<string, double> { ["easy"] = 102.325, ["normal"] = 84.175, ["hard"] = 96.6 };
        private static readonly Dictionary<string, double> GoldReach = new Dictionary<string, double>
        {
            ["easy"] = 2.2700304390445236, ["normal"] = 1.9295258731878449, ["hard"] = 1.6079382276565377, ["endless"] = 1.2059536707424032,
        };

        [Test]
        public void AutoplayMatchesTheWebGoldPerModeAndSeed()
        {
            foreach (var mode in GoldScore.Keys)
                for (var i = 0; i < 5; i++)
                {
                    var run = HarborHopper.SimulateAutoplay(mode, i + 1);
                    var tag = mode + " seed " + (i + 1);
                    Assert.That((int)run["score"], Is.EqualTo(GoldScore[mode][i]), tag + " score");
                    Assert.That((int)run["crates"], Is.EqualTo(GoldCrates[mode][i]), tag + " crates");
                    Assert.That((int)run["bumps"], Is.EqualTo(GoldBumps[mode][i]), tag + " bumps");
                    Assert.That((int)run["distanceM"], Is.EqualTo(GoldDist[mode][i]), tag + " meters");
                }
        }

        [Test]
        public void SimulateRoundMatchesTheWebGold()
        {
            var run = HarborHopper.SimulateRound(7, HarborHopper.Harbor);
            Assert.That((int)run["score"], Is.EqualTo(78));
            Assert.That((int)run["crates"], Is.EqualTo(14));
            Assert.That((int)run["rings"], Is.EqualTo(11));
            Assert.That((int)run["bumps"], Is.EqualTo(0));
            Assert.That((int)run["boosts"], Is.EqualTo(19));
            Assert.That((int)run["distanceM"], Is.EqualTo(788));
            Assert.That((int)run["hornsUsed"], Is.EqualTo(2));
        }

        [Test]
        public void MeansAndReachabilityMatchTheWeb()
        {
            foreach (var kv in GoldMean)
            {
                var sum = 0;
                for (var seed = 1; seed <= 40; seed++) sum += (int)HarborHopper.SimulateAutoplay(kv.Key, seed)["score"];
                Assert.That(sum / 40.0, Is.EqualTo(kv.Value).Within(1e-9), kv.Key);
            }
            foreach (var kv in GoldReach)
                Assert.That(HarborHopper.RowReachability(HarborHopper.ApplyDifficulty(HarborHopper.Harbor, kv.Key)), Is.EqualTo(kv.Value).Within(1e-12), kv.Key);
        }

        [Test]
        public void SmallRulesMatch()
        {
            Assert.That(HarborHopper.ApplyScore(0, -3), Is.EqualTo(0));
            Assert.That(HarborHopper.ApplyScore(10, 4), Is.EqualTo(14));
            Assert.That(HarborHopper.LaneOf(-3.2, HarborHopper.Harbor), Is.EqualTo(0));
            Assert.That(HarborHopper.LaneOf(0.0, HarborHopper.Harbor), Is.EqualTo(1));
            Assert.That(HarborHopper.LaneOf(1.2, HarborHopper.Harbor), Is.EqualTo(2));
            Assert.That(HarborHopper.LaneOf(99.0, HarborHopper.Harbor), Is.EqualTo(2));
            Assert.That(HarborHopper.ApplyDifficulty(HarborHopper.Harbor, "quatsch"), Is.SameAs(HarborHopper.Harbor));
        }

        [Test]
        public void TheHornClearsOnlyBuoysInItsConeAndRunsDry()
        {
            var engine = new HarborHopper.Engine(new GoobyRng(3), HarborHopper.Harbor);
            engine.Items.Clear();
            engine.Items.Add(new HarborHopper.Item { Type = "buoy", X = 0.2, Z = 3.0 });
            engine.Items.Add(new HarborHopper.Item { Type = "buoy", X = 3.0, Z = 3.0 });
            var cleared = -1;
            foreach (var ev in engine.Step(new HarborHopper.Input { Horn = true }, 1.0 / 60.0))
                if (ev.Type == "buoyCleared") cleared = ev.Count;
            Assert.That(cleared, Is.EqualTo(1), "only the buoy in the cone");
            Assert.That(engine.S.HornCharges, Is.EqualTo(1));
            engine.Step(new HarborHopper.Input { Horn = true }, 1.0 / 60.0);
            Assert.That(engine.S.HornCharges, Is.EqualTo(0));
            var empty = false;
            foreach (var ev in engine.Step(new HarborHopper.Input { Horn = true }, 1.0 / 60.0))
                if (ev.Type == "hornEmpty") empty = true;
            Assert.That(empty, Is.True);
        }

        [Test]
        public void TheRealtimeRoundWaitsForTheIntroThenSailsAndEnds()
        {
            var g = new HarborHopperGame("normal", 5);
            Assert.That(g.IntroLeft, Is.GreaterThan(0.0));
            g.SetTarget(0.5);
            for (var i = 0; i < 60; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Engine.S.Z, Is.EqualTo(0.0), "no sailing during the intro");
            for (var i = 0; i < 60; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Engine.S.Z, Is.GreaterThan(1.0));
            var guard = 0;
            while (!g.Finished && guard++ < 200000) { g.ApplyBot(); g.Tick(1.0 / 60.0); }
            Assert.That(g.Finished, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(HarborHopper.HopperScore(g.Engine.S, g.Tune)));
            Assert.That(g.TimeLeft, Is.EqualTo(0.0));
        }
    }
}
