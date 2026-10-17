using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>Tortenwerkstatt — parity with the web gold values (test_mg3_purble_place.gd) plus the realtime wrapper.</summary>
    public class PurblePlaceTests
    {
        private static readonly Dictionary<string, int[]> GoldScore = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 221, 178, 126, 198, 168 }, ["normal"] = new[] { 221, 148, 129, 191, 163 },
            ["hard"] = new[] { 171, 158, 123, 151, 165 }, ["endless"] = new[] { 166, 172, 62, 163, 236 },
        };
        private static readonly Dictionary<string, int[]> GoldServed = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 9, 9, 9, 9, 9 }, ["normal"] = new[] { 9, 9, 9, 9, 10 }, ["hard"] = new[] { 9, 9, 9, 9, 9 }, ["endless"] = new[] { 10, 11, 6, 11, 12 },
        };
        private static readonly Dictionary<string, int[]> GoldPerfect = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 5, 3, 3, 4, 5 }, ["normal"] = new[] { 5, 2, 3, 3, 5 }, ["hard"] = new[] { 3, 3, 3, 2, 4 }, ["endless"] = new[] { 3, 3, 2, 2, 5 },
        };
        private static readonly Dictionary<string, int[]> GoldRejected = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 1, 1, 3, 1, 2 }, ["normal"] = new[] { 1, 2, 3, 1, 3 }, ["hard"] = new[] { 2, 2, 3, 2, 2 }, ["endless"] = new[] { 3, 3, 3, 3, 2 },
        };
        private static readonly Dictionary<string, int[]> GoldExpired = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 0, 0, 0, 0, 0 }, ["normal"] = new[] { 0, 0, 0, 0, 0 }, ["hard"] = new[] { 0, 0, 0, 0, 0 }, ["endless"] = new[] { 0, 0, 0, 0, 1 },
        };
        private static readonly Dictionary<string, int[]> GoldPerfectBakes = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 6, 6, 6, 7, 6 }, ["normal"] = new[] { 6, 7, 7, 8, 6 }, ["hard"] = new[] { 7, 9, 7, 9, 6 }, ["endless"] = new[] { 7, 10, 4, 10, 8 },
        };
        private static readonly Dictionary<string, int[]> GoldSplats = new Dictionary<string, int[]>
        {
            ["easy"] = new[] { 0, 0, 0, 0, 0 }, ["normal"] = new[] { 0, 1, 1, 1, 0 }, ["hard"] = new[] { 2, 4, 2, 2, 4 }, ["endless"] = new[] { 2, 4, 0, 2, 4 },
        };
        private static readonly Dictionary<string, double[]> GoldTSec = new Dictionary<string, double[]>
        {
            ["easy"] = new[] { 210.033333, 210.033333, 210.033333, 210.033333, 210.033333 },
            ["normal"] = new[] { 210.033333, 210.033333, 210.033333, 210.033333, 210.033333 },
            ["hard"] = new[] { 210.033333, 210.033333, 210.033333, 210.033333, 210.033333 },
            ["endless"] = new[] { 214.333333, 223.8, 143.333333, 226.033333, 258.166667 },
        };

        [Test]
        public void BotRoundsMatchTheWebGold()
        {
            foreach (var mode in GoldScore.Keys)
                for (var i = 0; i < 5; i++)
                {
                    var r = PurblePlace.SimulateRound(i + 1, mode);
                    var tag = mode + " seed " + (i + 1);
                    Assert.That(r.Score, Is.EqualTo(GoldScore[mode][i]), "score " + tag);
                    Assert.That(r.CakesServed, Is.EqualTo(GoldServed[mode][i]), "served " + tag);
                    Assert.That(r.PerfectCakes, Is.EqualTo(GoldPerfect[mode][i]), "perfect " + tag);
                    Assert.That(r.Rejected, Is.EqualTo(GoldRejected[mode][i]), "rejected " + tag);
                    Assert.That(r.Expired, Is.EqualTo(GoldExpired[mode][i]), "expired " + tag);
                    Assert.That(r.PerfectBakes, Is.EqualTo(GoldPerfectBakes[mode][i]), "perfect bakes " + tag);
                    Assert.That(r.Splats, Is.EqualTo(GoldSplats[mode][i]), "splats " + tag);
                    Assert.That(r.Trashed, Is.EqualTo(0), "trashed " + tag);
                    Assert.That(r.TSec, Is.EqualTo(GoldTSec[mode][i]).Within(1e-4), "tSec " + tag);
                }
        }

        [Test]
        public void TicketsCurvesAndBeltMatchTheWeb()
        {
            var rng = new GoobyRng(42);
            var want = new[]
            {
                new PurblePlace.Spec { Shape = "square", Sponge = "vanilla", Icing = "pink", Topping = "berries", Candles = 2 },
                new PurblePlace.Spec { Shape = "square", Sponge = "chocolate", Icing = "chocolate", Topping = "cherry", Candles = 0 },
                new PurblePlace.Spec { Shape = "square", Sponge = "chocolate", Icing = "chocolate", Topping = "cherry", Candles = 1 },
                new PurblePlace.Spec { Shape = "square", Sponge = "vanilla", Icing = "chocolate", Topping = "berries", Candles = 0 },
                new PurblePlace.Spec { Shape = "square", Sponge = "strawberry", Icing = "none", Topping = "cherry", Candles = 3 },
                new PurblePlace.Spec { Shape = "heart", Sponge = "vanilla", Icing = "none", Topping = "berries", Candles = 2 },
            };
            for (var i = 0; i < want.Length; i++) Assert.That(PurblePlace.MakeTicket(rng, i), Is.EqualTo(want[i]), "order " + i);
            Assert.That(PurblePlace.PatienceFor(0), Is.EqualTo(45.0).Within(1e-9));
            Assert.That(PurblePlace.PatienceFor(5), Is.EqualTo(37.5).Within(1e-9));
            Assert.That(PurblePlace.PatienceFor(20), Is.EqualTo(30.0).Within(1e-9));
            Assert.That(PurblePlace.OrderIntervalAt(5, 14.0), Is.EqualTo(20.0).Within(1e-9));
            Assert.That(PurblePlace.OrderIntervalAt(20, 14.0), Is.EqualTo(14.0).Within(1e-9));
            var t = PurblePlace.Cake;
            Assert.That(PurblePlace.DropImpactS(1.0, new List<(double, double?)> { (0.9, null) }, t), Is.EqualTo(1.405).Within(1e-9));
            Assert.That(PurblePlace.DropImpactS(2.0, new List<(double, double?)> { (-0.7, null) }, t), Is.EqualTo(1.685).Within(1e-9));
            Assert.That(PurblePlace.DropImpactS(0.0, new List<(double, double?)> { (0.9, 0.2), (0.0, null) }, t), Is.EqualTo(0.18).Within(1e-9));
            var ramp = PurblePlace.BeltAdvance(0.0, 0.9, 6.0, 0.1);
            Assert.That(ramp.v1, Is.EqualTo(0.6).Within(1e-6));
            Assert.That(ramp.disp, Is.EqualTo(0.03).Within(1e-9));
            var full = PurblePlace.BeltAdvance(0.0, 0.9, 6.0, 1.0);
            Assert.That(full.disp, Is.EqualTo(0.0675 + 0.9 * 0.85).Within(1e-9));
            var brake = PurblePlace.BeltAdvance(0.9, 0.0, 6.0, 1.0);
            Assert.That(brake.v1, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(brake.disp, Is.EqualTo(0.0675).Within(1e-9));
            var s = PurblePlace.ScoreServe(0, 3, 0.6);
            Assert.That(s.Points, Is.EqualTo(20 + 6 + 4));
            Assert.That(PurblePlace.ScoreServe(2, 9, 0.9).Points, Is.EqualTo(-5));
        }

        [Test]
        public void TheNeededStationWalksTheOldestPanThroughTheFirstTicket()
        {
            var line = PurblePlace.CreateLine(new GoobyRng(1));
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo(""), "nothing ordered");
            var spec = new PurblePlace.Spec { Shape = "heart", Sponge = "chocolate", Icing = "pink", Topping = "cherry", Candles = 2 };
            line.Tickets.Add(new PurblePlace.Ticket { Id = 1, Spec = spec, Remain = 40.0, Patience = 45.0 });
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo("spawn"));
            var young = new PurblePlace.Pan { Id = 5, Shape = "round", S = 1.0, Sponge = "vanilla", Bake = "perfect" };
            var old = new PurblePlace.Pan { Id = 2, Shape = "heart", S = 0.5 };
            line.Pans.Add(young);
            line.Pans.Add(old);
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo("teig.chocolate"), "the oldest pan leads, not the first in the list");
            old.Sponge = "chocolate";
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo(""), "unbaked: the oven is next, no button");
            old.Bake = "perfect";
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo("guss.pink"));
            old.Icing = "pink";
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo("deko.cherry"));
            old.Topping = "cherry";
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo("kerzen"));
            old.Candles = 2;
            Assert.That(PurblePlace.NeededStation(line), Is.EqualTo("ship"));

            Assert.That(PurblePlace.ShipArmed(line), Is.False);
            old.S = line.Tune.ShipS - line.Tune.ShipHalfM;
            Assert.That(PurblePlace.ShipArmed(line), Is.True, "the hatch edge counts");
            old.Bake = null;
            Assert.That(PurblePlace.ShipArmed(line), Is.False, "only baked pans can ship");
        }

        [Test]
        public void TheRealtimeLineWaitsForTheIntroAndRunsTheBotToTheEnd()
        {
            var g = new PurblePlaceGame("easy", 3);
            g.SetBelt(1);
            for (var i = 0; i < 30; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Line.T, Is.EqualTo(0.0), "the line waits out the intro");
            var guard = 0;
            while (!g.Finished && guard++ < 100000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Line.CakesServed, Is.GreaterThan(0));
            Assert.That(g.FinalScore, Is.EqualTo(g.Line.Score));
            Assert.That(g.TimeLeft, Is.EqualTo(0.0));
        }
    }
}
