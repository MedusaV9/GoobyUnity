using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>Spielzeug-Rennen — parity with the web gold values (test_mg3_toy_racer.gd) plus the realtime wrapper.</summary>
    public class ToyRacerTests
    {
        private static readonly Dictionary<string, int[]> GoldScore = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 170, 170, 158, 170, 158 }, ["normal"] = new[] { 170, 170, 158, 170, 158 },
            ["hard"] = new[] { 167, 166, 154, 166, 155 }, ["endless"] = new[] { 970, 1220, 826, 890, 1089 },
        };
        private static readonly Dictionary<string, int[]> GoldRank = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 1, 1, 1, 1, 1 }, ["normal"] = new[] { 1, 1, 1, 1, 1 }, ["hard"] = new[] { 1, 1, 1, 1, 1 }, ["endless"] = new[] { 3, 3, 3, 4, 3 },
        };
        private static readonly Dictionary<string, int[]> GoldRaces = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 1, 1, 1, 1, 1 }, ["normal"] = new[] { 1, 1, 1, 1, 1 }, ["hard"] = new[] { 1, 1, 1, 1, 1 }, ["endless"] = new[] { 6, 8, 6, 6, 8 },
        };
        private static readonly Dictionary<string, int[]> GoldWins = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 1, 1, 1, 1, 1 }, ["normal"] = new[] { 1, 1, 1, 1, 1 }, ["hard"] = new[] { 1, 1, 1, 1, 1 }, ["endless"] = new[] { 5, 5, 4, 4, 4 },
        };
        private static readonly Dictionary<string, int[]> GoldOvertakes = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 3, 3, 3, 3, 3 }, ["normal"] = new[] { 3, 3, 3, 3, 3 }, ["hard"] = new[] { 3, 3, 3, 3, 3 }, ["endless"] = new[] { 16, 4, 5, 8, 3 },
        };
        private static readonly Dictionary<string, double[]> GoldDrift = new Dictionary<string, double[]>
        {
            ["easy"] = new[] { 442.804140383, 444.117779934, 323.141811378, 444.07513131, 323.143889265 },
            ["normal"] = new[] { 444.10189904, 441.635563618, 323.145896288, 444.044139076, 323.005729769 },
            ["hard"] = new[] { 412.383298185, 401.220271341, 285.767746849, 408.92019576, 292.469674329 },
            ["endless"] = new[] { 412.913496583, 407.551573422, 286.534122294, 397.701111687, 284.934614599 },
        };
        private static readonly Dictionary<string, double[]> GoldTime = new Dictionary<string, double[]>
        {
            ["easy"] = new[] { 152.066666667, 149.733333333, 146.8, 149.5, 144.833333333 },
            ["normal"] = new[] { 126.866666667, 129.0, 122.3, 126.5, 122.633333333 },
            ["hard"] = new[] { 106.0, 106.766666667, 103.966666667, 105.866666667, 103.033333333 },
            ["endless"] = new[] { 642.2, 857.033333333, 628.033333333, 643.4, 843.2 },
        };

        [Test]
        public void TrackGenerationMatchesTheWeb()
        {
            var gold = new Dictionary<int, (string tpl, bool bumps, double lap, int samples, int corners, int loops)>
            {
                [1] = ("loopBoulevard", false, 56.851674873, 228, 4, 2), [2] = ("loopBoulevard", false, 56.851674873, 228, 4, 2),
                [3] = ("rugRing", true, 41.206998146, 165, 4, 0), [4] = ("loopBoulevard", false, 56.851674873, 228, 4, 2),
                [5] = ("rugRing", true, 41.206998146, 165, 4, 0), [7] = ("rugRing", true, 41.206998146, 165, 4, 0),
            };
            foreach (var kv in gold)
            {
                var track = ToyRacer.BuildTrack(kv.Key, ToyRacer.Racer);
                var tag = "seed " + kv.Key;
                Assert.That(track.TemplateId, Is.EqualTo(kv.Value.tpl), tag);
                Assert.That(track.HasBumps, Is.EqualTo(kv.Value.bumps), tag);
                Assert.That(track.LapLen, Is.EqualTo(kv.Value.lap).Within(1e-8), tag);
                Assert.That(track.Samples.Count, Is.EqualTo(kv.Value.samples), tag);
                Assert.That(track.CornerZones.Count, Is.EqualTo(kv.Value.corners), tag);
                Assert.That(track.LoopZones.Count, Is.EqualTo(kv.Value.loops), tag);
            }
            var loopRows = ToyRacer.BuildTrack(1, ToyRacer.Racer).ItemRows;
            Assert.That(loopRows[0].S, Is.EqualTo(30.25515441).Within(1e-8));
            Assert.That(loopRows[1].S, Is.EqualTo(30.25515441).Within(1e-8));
            Assert.That(loopRows[2].S, Is.EqualTo(46.618373396).Within(1e-8));
            var ringRows = ToyRacer.BuildTrack(3, ToyRacer.Racer).ItemRows;
            Assert.That(ringRows[0].S, Is.EqualTo(7.417259666).Within(1e-8));
            Assert.That(ringRows[1].S, Is.EqualTo(20.603499073).Within(1e-8));
            Assert.That(ringRows[2].S, Is.EqualTo(33.78973848).Within(1e-8));
        }

        [Test]
        public void PointAtMatchesTheWeb()
        {
            var pt = ToyRacer.PointAt(ToyRacer.BuildTrack(1, ToyRacer.Racer), 5.5);
            Assert.That(pt.P.X, Is.EqualTo(-0.326261926).Within(1e-8));
            Assert.That(pt.P.Y, Is.EqualTo(2.483652995).Within(1e-8));
            Assert.That(pt.P.Z, Is.EqualTo(3.5083212).Within(1e-8));
            Assert.That(pt.T.X, Is.EqualTo(-0.0935557).Within(1e-8));
            Assert.That(pt.T.Y, Is.EqualTo(0.883421799).Within(1e-8));
            Assert.That(pt.T.Z, Is.EqualTo(-0.459144048).Within(1e-8));
            Assert.That(pt.Kappa, Is.EqualTo(0.0).Within(1e-9));
            var track = ToyRacer.BuildTrack(3, ToyRacer.Racer);
            Assert.That(ToyRacer.PointAt(track, 1.0 + track.LapLen).P.X, Is.EqualTo(ToyRacer.PointAt(track, 1.0).P.X).Within(1e-9));
        }

        [Test]
        public void AutoplayMatchesTheWebGold()
        {
            foreach (var mode in GoldScore.Keys)
                for (var i = 0; i < 5; i++)
                {
                    var run = ToyRacer.SimulateAutoplay(mode, i + 1);
                    var tag = mode + " seed " + (i + 1);
                    Assert.That((int)run["score"], Is.EqualTo(GoldScore[mode][i]), tag + " score");
                    Assert.That((int)run["rank"], Is.EqualTo(GoldRank[mode][i]), tag + " rank");
                    Assert.That((int)run["races"], Is.EqualTo(GoldRaces[mode][i]), tag + " races");
                    Assert.That((int)run["wins"], Is.EqualTo(GoldWins[mode][i]), tag + " wins");
                    Assert.That((int)run["overtakes"], Is.EqualTo(GoldOvertakes[mode][i]), tag + " overtakes");
                    Assert.That((double)run["driftMeters"], Is.EqualTo(GoldDrift[mode][i]).Within(1e-6), tag + " drift");
                    Assert.That((double)run["time"], Is.EqualTo(GoldTime[mode][i]).Within(1e-6), tag + " time");
                }
        }

        [Test]
        public void SmallRulesMatch()
        {
            var gold = new[] { 0.88, 0.9, 1.0, 1.1, 1.12 };
            var gaps = new[] { -20.0, -6.0, 0.0, 6.0, 20.0 };
            for (var i = 0; i < gaps.Length; i++) Assert.That(ToyRacer.ComputeRubber(gaps[i], ToyRacer.Racer), Is.EqualTo(gold[i]).Within(1e-9));
            Assert.That(ToyRacer.RaceScore(1, 0, 0.0, ToyRacer.Racer), Is.EqualTo(120));
            Assert.That(ToyRacer.RaceScore(2, 3, 125.0, ToyRacer.Racer), Is.EqualTo(98));
            Assert.That(ToyRacer.RaceScore(4, 10, 999.0, ToyRacer.Racer), Is.EqualTo(149));
            Assert.That(ToyRacer.RaceScore(0, 0, 0.0, ToyRacer.Racer), Is.EqualTo(120));
            Assert.That(ToyRacer.RaceScore(9, 0, 0.0, ToyRacer.Racer), Is.EqualTo(30));
            Assert.That(ToyRacer.RaceScore(1, 0, 19.9, ToyRacer.Racer), Is.EqualTo(121));
            var race = ToyRacer.CreateRace(3, ToyRacer.Racer);
            Assert.That(race.Karts.Count, Is.EqualTo(4));
            Assert.That(ToyRacer.PlayerRank(race), Is.EqualTo(4), "the player starts at the back");
            var lap = race.Track.LapLen;
            Assert.That(ToyRacer.SDelta(1.0, lap - 1.0, lap), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(ToyRacer.SDelta(lap - 1.0, 1.0, lap), Is.EqualTo(-2.0).Within(1e-9));
        }

        [Test]
        public void BlocksStunAndShieldsPop()
        {
            var race = ToyRacer.CreateRace(3, ToyRacer.Racer);
            var player = race.Karts[0];
            player.Item = "block";
            ToyRacer.StepRace(race, 1.0 / 30.0, new ToyRacer.Input { Steer = 0.0, UseItem = true });
            Assert.That(race.Blocks.Count, Is.EqualTo(1));
            Assert.That(player.Item, Is.Empty);
            race.Blocks[0].S = player.S;
            race.Blocks[0].Lat = player.Lateral;
            race.Blocks[0].By = 99;
            ToyRacer.StepRace(race, 1.0 / 30.0, new ToyRacer.Input { Steer = 0.0 });
            Assert.That(player.StunT, Is.GreaterThan(0.0), "a block stuns");
            player.StunT = 0.0;
            player.Shield = true;
            race.Blocks.Add(new ToyRacer.Block { S = player.S, Lat = player.Lateral, By = 99 });
            ToyRacer.StepRace(race, 1.0 / 30.0, new ToyRacer.Input { Steer = 0.0 });
            Assert.That(player.Shield, Is.False, "the shield is used up instead");
            Assert.That(player.StunT, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void TheRealtimeRaceWaitsForTheIntroAndFinishesWithTheBot()
        {
            var g = new ToyRacerGame("normal", 3);
            g.SetSteer(0.3);
            for (var i = 0; i < 60; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Race.Time, Is.EqualTo(0.0), "the grid waits out the intro");
            var guard = 0;
            while (!g.Finished && guard++ < 100000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(g.Finished, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(ToyRacer.RunScore(g.Race)));
            Assert.That(g.Race.FinishRank, Is.InRange(1, 4));
        }
    }
}
