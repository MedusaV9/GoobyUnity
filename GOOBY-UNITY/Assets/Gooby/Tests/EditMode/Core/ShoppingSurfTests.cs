using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>Einkaufs-Surf — parity with the web gold values (test_mg3_shopping_surf.gd) plus the realtime wrapper.</summary>
    public class ShoppingSurfTests
    {
        private static readonly Dictionary<string, int[]> GoldScore = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 975, 1035, 1057, 965, 977 }, ["normal"] = new[] { 706, 1126, 1176, 1083, 819 },
            ["hard"] = new[] { 1095, 708, 1383, 784, 632 }, ["endless"] = new[] { 1328, 716, 1206, 1405, 612 },
        };
        private static readonly Dictionary<string, int[]> GoldCrashes = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 1, 0, 0, 2, 1 }, ["normal"] = new[] { 3, 1, 1, 2, 3 }, ["hard"] = new[] { 3, 3, 2, 3, 3 }, ["endless"] = new[] { 2, 3, 3, 1, 3 },
        };

        [Test]
        public void ChunkStreamAndPowerupPlanMatchTheWeb()
        {
            var gold = new[] { 0, 1, 3, 2, 0, 1, 3, 4, 7, 6, 5, 0, 8, 3, 4, 0, 1, 0, 8, 0, 8, 3, 6, 2 };
            var rng = new GoobyRng(7);
            var last = -1;
            var at = 0.0;
            for (var i = 0; i < gold.Length; i++)
            {
                var idx = ShoppingSurf.PickNextChunk(rng, at, last, ShoppingSurf.Surf);
                Assert.That(idx, Is.EqualTo(gold[i]), "pick " + i);
                last = idx;
                at += 30.0;
            }
            var goldPu = new[] { "x2", "magnet", "shield", "turbo", "shield", "magnet", "shield", "turbo", "x2", "shield", "x2", "turbo" };
            var prng = new GoobyRng(11);
            string lastKind = null;
            var sinceTurbo = double.PositiveInfinity;
            var dist = 0.0;
            for (var i = 0; i < goldPu.Length; i++)
            {
                var k = ShoppingSurf.PlanPowerupKind(prng, lastKind, sinceTurbo, ShoppingSurf.Surf);
                Assert.That(k, Is.EqualTo(goldPu[i]), "power-up " + i);
                sinceTurbo = k == "turbo" ? 0.0 : sinceTurbo + 200.0;
                lastKind = k;
                dist += ShoppingSurf.PlanPowerupGap(prng, ShoppingSurf.Surf);
            }
            Assert.That(dist, Is.EqualTo(2584.926969).Within(1e-4));
        }

        [Test]
        public void ValidatorRowsAndSurvivability()
        {
            var hz = ShoppingSurf.ExpandChunk(ShoppingSurf.Chunks[6], 200.0).hazards;
            var rows = ShoppingSurf.HazardRows(hz, 12.0, ShoppingSurf.Surf);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].T, Is.EqualTo(16.916667).Within(1e-5));
            Assert.That(rows[0].Lanes, Is.EqualTo(new[] { "jump", "jump", null }));
            Assert.That(rows[1].T, Is.EqualTo(17.75).Within(1e-5));
            Assert.That(rows[1].Lanes, Is.EqualTo(new[] { null, null, "slide" }));
            foreach (var v in new[] { 8.0, 12.0, 16.0 }) Assert.That(ShoppingSurf.IsSequenceSurvivable(hz, v, ShoppingSurf.Surf), Is.True);
            var wall = new List<ShoppingSurf.Hazard>();
            for (var lane = 0; lane < 3; lane++) wall.Add(new ShoppingSurf.Hazard { AtM = 50.0, Kind = "crate", Lane = lane });
            Assert.That(ShoppingSurf.IsSequenceSurvivable(wall, 10.0, ShoppingSurf.Surf), Is.False, "a crate wall is impossible");
            foreach (var mode in new[] { "easy", "normal", "hard", "endless" })
            {
                var tune = ShoppingSurf.ApplyDifficulty(ShoppingSurf.Surf, mode);
                foreach (var def in ShoppingSurf.Chunks)
                {
                    var seq = ShoppingSurf.ExpandChunk(def, System.Math.Max(800.0, def.MinM)).hazards;
                    foreach (var v in ShoppingSurf.ValidatorProbeSpeeds(tune))
                        Assert.That(ShoppingSurf.IsSequenceSurvivable(seq, v, tune), Is.True, def.Name + " in " + mode + " at " + v);
                }
            }
            var endless = ShoppingSurf.ApplyDifficulty(ShoppingSurf.Surf, "endless");
            Assert.That(ShoppingSurf.DensityMultAt(750.0, endless), Is.EqualTo(1.325).Within(1e-9));
            Assert.That(ShoppingSurf.ApplyDifficulty(ShoppingSurf.Surf, "hard").MaxSpeed, Is.EqualTo(18.0).Within(1e-9));
            Assert.That(ShoppingSurf.ApplyDifficulty(ShoppingSurf.Surf, "easy").ArcadeMaxCrashes, Is.EqualTo(4));
        }

        [Test]
        public void ScriptedRunMatchesTheWebTrace()
        {
            var run = ShoppingSurfRun.CreateRun(new GoobyRng(99), "arcade", ShoppingSurf.Surf);
            var script = new Dictionary<int, ShoppingSurfRun.Input>
            {
                [20] = new ShoppingSurfRun.Input { Right = true }, [60] = new ShoppingSurfRun.Input { Jump = true },
                [95] = new ShoppingSurfRun.Input { Left = true }, [140] = new ShoppingSurfRun.Input { Slide = true },
            };
            var gold = new[]
            {
                (0, 2667, 1, 0, 0, 4, "spawn|spawn|spawn|spawn"), (50, 136000, 2, 0, 0, 5, ""), (100, 269333, 1, 4, 0, 4, ""),
                (150, 397333, 1, 4, 1, 6, ""), (200, 504000, 1, 4, 1, 5, ""), (250, 605333, 1, 4, 2, 5, ""),
                (300, 738667, 1, 6, 2, 6, ""), (350, 872000, 1, 8, 2, 6, ""),
            };
            var trace = new List<(int, int, int, int, int, int, string)>();
            for (var i = 0; i < 400; i++)
            {
                var evs = ShoppingSurfRun.StepRun(run, 1.0 / 30.0, script.TryGetValue(i, out var inp) ? inp : default);
                if (i % 50 == 0)
                {
                    var types = new List<string>();
                    foreach (var e in evs) types.Add(e.Type);
                    trace.Add((i, FrameworkLogic.JsRound(run.DistanceM * 1e4), run.Lane, run.Coins, run.Crashes, run.Obstacles.Count, string.Join("|", types)));
                }
                if (run.Ended) break;
            }
            Assert.That(trace.Count, Is.EqualTo(gold.Length));
            for (var k = 0; k < gold.Length; k++) Assert.That(trace[k], Is.EqualTo(gold[k]), "step " + gold[k].Item1);
            Assert.That(ShoppingSurfRun.RunScore(run), Is.EqualTo(115));
            Assert.That(run.Crashes, Is.EqualTo(3));
            Assert.That(FrameworkLogic.JsRound(run.DistanceM), Is.EqualTo(99));
        }

        [Test]
        public void TravelRunMatchesTheWeb()
        {
            var result = ShoppingSurfRun.SimulateRun(new GoobyRng(5), "travel", 300.0);
            Assert.That(result.run.Finished, Is.True);
            Assert.That(result.run.DistanceM, Is.EqualTo(700.040833).Within(1e-4));
            Assert.That(result.run.Coins, Is.EqualTo(145));
            Assert.That(result.run.Crashes, Is.EqualTo(1));
            Assert.That(ShoppingSurf.TravelReward(result.run.Coins, result.run.Crashes, ShoppingSurf.Surf), Is.EqualTo((30, false)));
        }

        [Test]
        public void AutoplayMatchesTheWebGold()
        {
            foreach (var mode in GoldScore.Keys)
                for (var i = 0; i < 5; i++)
                {
                    var out_ = ShoppingSurfRun.SimulateAutoplay(mode, i + 1, 90.0);
                    Assert.That(out_.score, Is.EqualTo(GoldScore[mode][i]), mode + " seed " + (i + 1) + " score");
                    Assert.That(out_.crashes, Is.EqualTo(GoldCrashes[mode][i]), mode + " seed " + (i + 1) + " crashes");
                }
        }

        [Test]
        public void ScoreSpeedAndRewardEdges()
        {
            var gold = new[] { 8.0, 8.0, 8.25, 8.5, 10.0, 16.0 };
            var at = new[] { 0.0, 4.9, 5.0, 12.0, 40.0, 200.0 };
            for (var i = 0; i < at.Length; i++) Assert.That(ShoppingSurf.SpeedRampAt(at[i], ShoppingSurf.Surf), Is.EqualTo(gold[i]).Within(1e-9));
            Assert.That(ShoppingSurf.SurfScore(123.9, 10, 3), Is.EqualTo(149));
            Assert.That(ShoppingSurf.SurfScore(-500.0, 0, 0), Is.EqualTo(0));
            Assert.That(ShoppingSurf.TravelReward(12, 0, ShoppingSurf.Surf), Is.EqualTo((17, true)));
            Assert.That(ShoppingSurf.TravelReward(60, 2, ShoppingSurf.Surf), Is.EqualTo((30, false)));
        }

        [Test]
        public void TheRealtimeRunWaitsForTheIntroThenSwipesWork()
        {
            var g = new ShoppingSurfGame("normal", 4);
            g.Swipe("right");
            for (var i = 0; i < 30; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Run.DistanceM, Is.EqualTo(0.0), "no running during the intro");
            for (var i = 0; i < 70; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Run.DistanceM, Is.GreaterThan(1.0));
            g.Swipe("left");
            g.Tick(1.0 / 60.0);
            Assert.That(g.Run.Lane, Is.EqualTo(0));
            g.Swipe("up");
            g.Tick(1.0 / 60.0);
            Assert.That(g.Run.JumpT, Is.GreaterThanOrEqualTo(0.0));
            var guard = 0;
            while (!g.Finished && guard++ < 200000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(g.Finished, Is.True, "three crashes end the arcade run");
            Assert.That(g.FinalScore, Is.EqualTo(ShoppingSurfRun.RunScore(g.Run)));
        }
    }
}
