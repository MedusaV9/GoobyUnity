using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Gobnom;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// GOB NOM — ports of test_gobnom_logic.gd, test_gobnom_elements.gd, test_gobnom_levels.gd and
    /// test_gobnom_progress.gd: determinism, one-sided ropes, cuts (id, swipe, budget), every element, hazards, coop
    /// ownership gates, the machine solvability proof of all 25 levels and the save slice.
    /// </summary>
    public class GobnomTests
    {
        private JObject _balance;

        [OneTimeSetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            _balance = GobnomData.LoadBalance();
        }

        private static JObject Mini(object extra = null)
        {
            var level = JObject.FromObject(new
            {
                id = 1,
                candy = new { x = 480, y = 200 },
                mouth = new { x = 480, y = 470 },
                ropes = new[] { new { x = 480, y = 120, rest = 80 } },
                jars = new[] { new { x = 480, y = 300 }, new { x = 480, y = 360 }, new { x = 480, y = 420 } },
            });
            if (extra != null) foreach (var p in JObject.FromObject(extra).Properties()) level[p.Name] = p.Value;
            return level;
        }

        private static JObject Coop(object extra = null)
        {
            var level = Mini(new
            {
                kind = "coop",
                split = new { axis = "x", at = 480 },
                ropes = new[] { new { x = 200, y = 120, rest = 80, owner = "a" }, new { x = 700, y = 120, rest = 80, owner = "b" } },
            });
            if (extra != null) foreach (var p in JObject.FromObject(extra).Properties()) level[p.Name] = p.Value;
            return level;
        }

        private static List<GobnomEvent> Ticks(GobnomRun run, int n)
        {
            var events = new List<GobnomEvent>();
            for (var i = 0; i < n; i++) events.AddRange(GobnomLogic.Step(run));
            return events;
        }

        // ── determinism ──

        [Test]
        public void SameSeedSameInputsGiveTheSameHashAndOtherSeedsDivergeWithTurbulence()
        {
            var level = Mini(new { fans = new[] { new { x = 60, y = 300, dx = 1, dy = 0, force = 120, turbulence = 0.4 } } });
            var hashes = new List<string>();
            for (var round = 0; round < 2; round++)
            {
                var run = GobnomLogic.NewRun(level, _balance, 42);
                Ticks(run, 45);
                GobnomLogic.CutRope(run, 0);
                Ticks(run, 30);
                hashes.Add(GobnomLogic.StateHash(run));
            }
            Assert.That(hashes[1], Is.EqualTo(hashes[0]));
            var stormy = Mini(new { fans = new[] { new { x = 60, y = 300, dx = 1, dy = 0, force = 200, turbulence = 0.5 } } });
            var a = GobnomLogic.NewRun(stormy, _balance, 1);
            var b = GobnomLogic.NewRun(stormy, _balance, 2);
            Ticks(a, 120);
            Ticks(b, 120);
            Assert.That(GobnomLogic.StateHash(b), Is.Not.EqualTo(GobnomLogic.StateHash(a)));
        }

        // ── constraints ──

        [Test]
        public void TheRopeHoldsWithoutStretchingAndSettles()
        {
            var run = GobnomLogic.NewRun(Mini(), _balance, 1);
            var anchor = new V2(480f, 120f);
            for (var i = 0; i < 600; i++)
            {
                GobnomLogic.Step(run);
                Assert.That(run.CandyPos.IsFinite, Is.True);
                Assert.That(anchor.DistanceTo(run.CandyPos), Is.LessThanOrEqualTo(80.0 + 1.5));
            }
            Assert.That(GobnomLogic.CandyVelocity(run).Length, Is.LessThan(100.0));
        }

        [Test]
        public void AOneSidedRopeNeverPushes()
        {
            var run = GobnomLogic.NewRun(Mini(new { candy = new { x = 480, y = 80 } }), _balance, 1);
            Ticks(run, 5);
            Assert.That(run.CandyPos.Y, Is.GreaterThan(80.0));
        }

        // ── cuts ──

        [Test]
        public void CutsByIdOnceAndTheStraightDropEatsAllJars()
        {
            var run = GobnomLogic.NewRun(Mini(), _balance, 1);
            Assert.That(GobnomLogic.CutRope(run, 9).Ok, Is.False);
            Assert.That(GobnomLogic.CutRope(run, 0).Ok, Is.True);
            Assert.That(GobnomLogic.CutRope(run, 0).Reason, Is.EqualTo("already_cut"));
            Ticks(run, 240);
            Assert.That(run.Outcome, Is.EqualTo("won"));
            Assert.That(run.JarsTaken, Is.EqualTo(3));
        }

        [Test]
        public void ASwipeCutsOnlyTheRopesItCrosses()
        {
            var level = Mini(new { ropes = new[] { new { x = 480, y = 120, rest = 80 }, new { x = 200, y = 120, rest = 200 } } });
            var run = GobnomLogic.NewRun(level, _balance, 1);
            Assert.That(GobnomLogic.CutSegment(run, new V2(700f, 300f), new V2(760f, 300f)), Is.Empty);
            Assert.That(GobnomLogic.CutSegment(run, new V2(300f, 160f), new V2(560f, 160f)).Count, Is.EqualTo(2));
        }

        [Test]
        public void TheCutBudgetIsEnforced()
        {
            var level = Mini(new { max_cuts = 1, ropes = new[] { new { x = 480, y = 120, rest = 80 }, new { x = 200, y = 120, rest = 200 } } });
            var run = GobnomLogic.NewRun(level, _balance, 1);
            Assert.That(GobnomLogic.CutsLeft(run), Is.EqualTo(1));
            Assert.That(GobnomLogic.CutRope(run, 0).Ok, Is.True);
            Assert.That(GobnomLogic.CutRope(run, 1).Reason, Is.EqualTo("cut_limit"));
            Assert.That(GobnomLogic.CutsLeft(run), Is.EqualTo(0));
        }

        // ── hazards + win ──

        [Test]
        public void SpikesAndTheWorldEdgeLoseAndActionsStopAfterTheOutcome()
        {
            var spiky = GobnomLogic.NewRun(Mini(new { mouth = new { x = 100, y = 470 }, spikes = new[] { new { x = 430, y = 320, w = 100, h = 30 } } }), _balance, 1);
            GobnomLogic.CutRope(spiky, 0);
            Assert.That(Ticks(spiky, 120).Select(e => e.Kind), Has.Member("spike"));
            Assert.That(spiky.Outcome, Is.EqualTo("lost"));

            var falling = GobnomLogic.NewRun(Mini(new { mouth = new { x = 100, y = 100 }, jars = new object[0] }), _balance, 1);
            GobnomLogic.CutRope(falling, 0);
            Ticks(falling, 300);
            Assert.That(falling.Outcome, Is.EqualTo("lost"));

            var done = GobnomLogic.NewRun(Mini(), _balance, 1);
            done.Outcome = "won";
            Assert.That(GobnomLogic.CutRope(done, 0).Reason, Is.EqualTo("outcome"));
            Assert.That(GobnomLogic.CutSegment(done, V2.Zero, new V2(960f, 540f)), Is.Empty);
            Assert.That(GobnomLogic.StarsFor(0), Is.EqualTo(0));
            Assert.That(GobnomLogic.StarsFor(2), Is.EqualTo(2));
            Assert.That(GobnomLogic.StarsFor(9), Is.EqualTo(3));
        }

        // ── coop ownership ──

        [Test]
        public void CoopGatesByOwnerTagOrScreenHalfAndSoloBypasses()
        {
            var run = GobnomLogic.NewRun(Coop(), _balance, 1);
            Assert.That(GobnomLogic.CutRope(run, 1, "a").Reason, Is.EqualTo("wrong_side"));
            Assert.That(run.Ropes[1].Cut, Is.False);
            Assert.That(GobnomLogic.CutRope(run, 1, "b").Ok, Is.True);
            Assert.That(GobnomLogic.CutRope(run, 0, "a").Ok, Is.True);

            var cushion = new { x = 200, y = 400, dx = 0, dy = -1, power = 400, range = 300, half_w = 60 };
            var cushRun = GobnomLogic.NewRun(Coop(new { cushions = new[] { cushion } }), _balance, 1);
            Assert.That(GobnomLogic.PuffCushion(cushRun, 0, "b").Reason, Is.EqualTo("wrong_side"));
            Assert.That(GobnomLogic.PuffCushion(cushRun, 0, "a").Ok, Is.True);

            var swipe = GobnomLogic.NewRun(Coop(), _balance, 1);
            Assert.That(GobnomLogic.CutSegment(swipe, new V2(100f, 160f), new V2(860f, 160f), "b"), Is.EqualTo(new List<int> { 1 }));
            Assert.That(swipe.Events.Count(e => e.Kind == "denied"), Is.GreaterThanOrEqualTo(1));

            Assert.That(GobnomLogic.CutRope(GobnomLogic.NewRun(Coop(), _balance, 1), 1, "solo").Ok, Is.True);
        }

        [Test]
        public void SideOfFollowsBothSplitAxes()
        {
            var x = GobnomLogic.NewRun(Coop(), _balance, 1);
            Assert.That(GobnomLogic.SideOf(x, new V2(100f, 100f)), Is.EqualTo("a"));
            Assert.That(GobnomLogic.SideOf(x, new V2(800f, 100f)), Is.EqualTo("b"));
            var y = GobnomLogic.NewRun(Coop(new { split = new { axis = "y", at = 270 } }), _balance, 1);
            Assert.That(GobnomLogic.SideOf(y, new V2(480f, 100f)), Is.EqualTo("a"));
            Assert.That(GobnomLogic.SideOf(y, new V2(480f, 400f)), Is.EqualTo("b"));
        }

        // ── elements ──

        [Test]
        public void ABubbleCatchesLiftsAndPops()
        {
            var run = GobnomLogic.NewRun(Mini(new { bubbles = new[] { new { x = 480, y = 330, r = 26 } } }), _balance, 1);
            GobnomLogic.CutRope(run, 0);
            var caught = false;
            for (var i = 0; i < 90 && !caught; i++) caught = GobnomLogic.Step(run).Any(e => e.Kind == "catch");
            Assert.That(caught, Is.True, "the bubble catches the falling candy");
            var rising = false;
            for (var i = 0; i < 300 && !rising; i++) { GobnomLogic.Step(run); rising = GobnomLogic.CandyVelocity(run).Y < -50.0; }
            Assert.That(rising, Is.True, "the bubble carries it UP");
            Assert.That(GobnomLogic.PopBubble(run, 0).Ok, Is.True);
            Assert.That(run.InBubble, Is.False);
            Assert.That(GobnomLogic.PopBubble(run, 0).Reason, Is.EqualTo("unknown"));
        }

        [Test]
        public void CushionsPuffInTheirBeamWithChargesAndCooldown()
        {
            object Cushion(double x, object extra)
            {
                var row = JObject.FromObject(new { x, y = 400.0, dx = 0, dy = -1, power = 500, range = 300, half_w = 60 });
                foreach (var p in JObject.FromObject(extra).Properties()) row[p.Name] = p.Value;
                return row;
            }
            var run = GobnomLogic.NewRun(Mini(new { cushions = new[] { Cushion(480.0, new { charges = 2 }) } }), _balance, 1);
            var puff = GobnomLogic.PuffCushion(run, 0);
            Assert.That(puff.Ok && puff.Hit, Is.True);
            Assert.That(GobnomLogic.CandyVelocity(run).Y, Is.LessThan(-300.0));
            Assert.That(GobnomLogic.PuffCushion(run, 0).Reason, Is.EqualTo("cooldown"));
            run.Tick += 100;
            Assert.That(GobnomLogic.PuffCushion(run, 0).Ok, Is.True);
            run.Tick += 100;
            Assert.That(GobnomLogic.PuffCushion(run, 0).Reason, Is.EqualTo("empty"));

            var aside = GobnomLogic.NewRun(Mini(new { cushions = new[] { Cushion(60.0, new { half_w = 40, charges = -1 }) } }), _balance, 1);
            var miss = GobnomLogic.PuffCushion(aside, 0);
            Assert.That(miss.Ok, Is.True);
            Assert.That(miss.Hit, Is.False);
        }

        [Test]
        public void FansBlowAndOnlyToggleableOnesSwitch()
        {
            var run = GobnomLogic.NewRun(Mini(new { fans = new[] { new { x = 60, y = 200, dx = 1, dy = 0, force = 300, toggleable = true } } }), _balance, 1);
            Ticks(run, 30);
            Assert.That(run.CandyPos.X, Is.GreaterThan(481.0));
            Assert.That(GobnomLogic.ToggleFan(run, 0).Ok, Is.True);
            Assert.That(run.Fans[0].On, Is.False);
            var fixedFan = GobnomLogic.NewRun(Mini(new { fans = new[] { new { x = 60, y = 200, dx = 1, dy = 0, force = 100 } } }), _balance, 1);
            Assert.That(GobnomLogic.ToggleFan(fixedFan, 0).Reason, Is.EqualTo("fixed"));
        }

        [Test]
        public void RailAnchorsClampAndShootersFireOnceWithACuttableRope()
        {
            var level = Mini(new
            {
                ropes = new[] { new { x = 200, y = 90, rest = 90, rail = new { x1 = 200, y1 = 90, x2 = 600, y2 = 90, t = 0.0 } } },
                candy = new { x = 200, y = 180 },
            });
            var run = GobnomLogic.NewRun(level, _balance, 1);
            Assert.That(GobnomLogic.MoveAnchor(run, 0, 2.0).Ok, Is.True);
            Assert.That(run.Ropes[0].Anchor, Is.EqualTo(new V2(600f, 90f)));
            Assert.That(GobnomLogic.MoveAnchor(run, 7, 0.5).Reason, Is.EqualTo("no_rail"));

            var shooter = GobnomLogic.NewRun(Mini(new { shooters = new[] { new { x = 560, y = 330, r = 110 } } }), _balance, 1);
            GobnomLogic.CutRope(shooter, 0);
            var shot = false;
            for (var i = 0; i < 120 && !shot; i++) shot = GobnomLogic.Step(shooter).Any(e => e.Kind == "shoot");
            Assert.That(shot, Is.True);
            Assert.That(shooter.Ropes.Count, Is.EqualTo(2));
            Assert.That(GobnomLogic.CutRope(shooter, 100).Ok, Is.True);
            Assert.That(shooter.Shooters[0].Fired, Is.True);
        }

        [Test]
        public void CottonCandyCloudsBrakeTheFall()
        {
            var free = GobnomLogic.NewRun(Mini(new { mouth = new { x = 100, y = 470 }, jars = new object[0] }), _balance, 1);
            var cloud = GobnomLogic.NewRun(Mini(new { mouth = new { x = 100, y = 470 }, jars = new object[0], clouds = new[] { new { x = 380, y = 250, w = 200, h = 200 } } }), _balance, 1);
            GobnomLogic.CutRope(free, 0);
            GobnomLogic.CutRope(cloud, 0);
            Ticks(free, 40);
            Ticks(cloud, 40);
            Assert.That(cloud.CandyPos.Y, Is.LessThan(free.CandyPos.Y - 20.0));
        }

        // ── levels: the machine solvability proof ──

        [Test]
        public void LevelsLoadValidateAndFollowTheIntroCurve()
        {
            var campaign = GobnomData.LoadCampaign();
            var coop = GobnomData.LoadCoop();
            Assert.That(campaign.Count, Is.EqualTo(15));
            Assert.That(coop.Count, Is.EqualTo(10));
            Assert.That(GobnomData.ValidateLevels(campaign, coop, _balance), Is.Empty);
            foreach (var level in campaign)
                Assert.That((string)level["intro"], Is.EqualTo(GobnomData.CampaignIntro[(int)level["id"]]));
        }

        [Test]
        public void EveryCampaignLevelIsWonByItsSolutionPlan()
        {
            foreach (var level in GobnomData.LoadCampaign())
            {
                var id = (int)level["id"];
                var r = GobnomSolver.RunSolution(level, _balance, 1);
                Assert.That(r.Won, Is.True, $"L{id:00} solvable (outcome={r.Outcome}, tick {r.Ticks})");
                if (level["solution"]?["full_clear"]?.Value<bool>() == true) Assert.That(r.Jars, Is.EqualTo(3), $"L{id:00} collects all jars");
            }
        }

        [Test]
        public void EveryCoopLevelIsWonWithoutDenialsAndNeedsBothPlayers()
        {
            foreach (var level in GobnomData.LoadCoop())
            {
                var id = (int)level["id"];
                var r = GobnomSolver.RunSolution(level, _balance, 1);
                Assert.That(r.Won, Is.True, $"CN{id} solvable (outcome={r.Outcome}, tick {r.Ticks})");
                Assert.That(r.Denied, Is.EqualTo(0), $"CN{id}: the plan respects ownership");
                if (level["solution"]?["full_clear"]?.Value<bool>() == true) Assert.That(r.Jars, Is.EqualTo(3), $"CN{id} collects all jars");
                var players = new HashSet<string>(GobnomSolver.SortedActions(level).Select(a => (string)a["player"]));
                Assert.That(players, Is.SupersetOf(new[] { "a", "b" }), $"CN{id}: both players act");
            }
        }

        [Test]
        public void SwappedCoopPlayersGetDeniedAndSolverRunsAreDeterministic()
        {
            var level = (JObject)GobnomData.LoadCoop()[0].DeepClone();
            foreach (var action in level["solution"]["actions"]) action["player"] = (string)action["player"] == "a" ? "b" : "a";
            var swapped = GobnomSolver.RunSolution(level, _balance, 1);
            Assert.That(swapped.Denied, Is.GreaterThan(0));
            Assert.That(swapped.Won, Is.False);

            var l11 = GobnomData.LoadCampaign()[10];
            var one = GobnomSolver.RunSolution(l11, _balance, 1);
            var two = GobnomSolver.RunSolution(l11, _balance, 1);
            Assert.That(two.Ticks, Is.EqualTo(one.Ticks));
            Assert.That(two.Jars, Is.EqualTo(one.Jars));
            Assert.That(two.Outcome, Is.EqualTo(one.Outcome));
        }

        // ── session (realtime wrapper) ──

        [Test]
        public void TheSessionBotClearsBothTracksAndTheSessionScoreAddsUpTheWins()
        {
            var g = new Gooby.Core.Minigames.Play.GobnomGame(4242);
            var wins = 0;
            g.PhaseChanged += p => { if (p == "won") wins++; };
            Assert.That(g.Phase, Is.EqualTo("select"));
            Assert.That(g.IsUnlocked("campaign", 2), Is.False, "L2 opens after L1");
            g.Open("campaign", 1);
            for (var i = 0; i < 30; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Run.Tick, Is.EqualTo(0), "the establish beat holds the sim");
            Assert.That(g.AcceptsInput, Is.False);
            var guard = 0;
            while (GobnomProgress.ClearedCount(g.Save, "campaign") < 15 && guard++ < 200000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(GobnomProgress.ClearedCount(g.Save, "campaign"), Is.EqualTo(15));
            g.BackToSelect();
            g.ShowTrack("coop");
            while (GobnomProgress.ClearedCount(g.Save, "coop") < 10 && guard++ < 400000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(GobnomProgress.ClearedCount(g.Save, "coop"), Is.EqualTo(10));
            Assert.That(wins, Is.GreaterThanOrEqualTo(25));
            var expected = 0;
            for (var id = 1; id <= 15; id++) expected += (int)g.Save["gobnom"]["best"]["c" + id];
            for (var id = 1; id <= 10; id++) expected += (int)g.Save["gobnom"]["best"]["n" + id];
            Assert.That(g.SessionScore, Is.GreaterThanOrEqualTo(expected), "every booked best came out of this session");
            g.Finish();
            Assert.That(g.Finished, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(g.SessionScore));
            Assert.That(g.Open("campaign", 1), Is.False, "a finished session opens nothing");
        }

        // ── progress slice ──

        [Test]
        public void TheProgressSliceHealsBooksMonotoneAndKeepsTracksApart()
        {
            var fresh = GobnomProgress.DefaultSlice();
            Assert.That(JToken.DeepEquals(GobnomProgress.NormalizeSlice(fresh.DeepClone()), fresh), Is.True);
            Assert.That((int)GobnomProgress.NormalizeSlice(new JValue("kaputt"))["v"], Is.EqualTo(1));
            var fixedSlice = GobnomProgress.NormalizeSlice(JObject.Parse("{\"v\":\"x\",\"stars\":[],\"best\":{\"c1\":120},\"cleared\":null}"));
            Assert.That((int)fixedSlice["v"], Is.EqualTo(1));
            Assert.That(fixedSlice["stars"], Is.TypeOf<JObject>());
            Assert.That((int)fixedSlice["best"]["c1"], Is.EqualTo(120));

            Assert.That(GobnomProgress.LevelKey(GobnomData.TrackCampaign, 3), Is.EqualTo("c3"));
            Assert.That(GobnomProgress.LevelKey(GobnomData.TrackCoop, 3), Is.EqualTo("n3"));

            var state = new JObject();
            var first = GobnomProgress.RecordWin(state, GobnomData.TrackCampaign, 1, 2, 100);
            Assert.That(first.FirstClear && first.NewBest, Is.True);
            Assert.That(GobnomProgress.LevelStars(state, GobnomData.TrackCampaign, 1), Is.EqualTo(2));
            Assert.That(GobnomProgress.IsCleared(state, GobnomData.TrackCampaign, 1), Is.True);
            var worse = GobnomProgress.RecordWin(state, GobnomData.TrackCampaign, 1, 1, 40);
            Assert.That(worse.FirstClear || worse.NewBest, Is.False);
            Assert.That(GobnomProgress.LevelStars(state, GobnomData.TrackCampaign, 1), Is.EqualTo(2));
            Assert.That(GobnomProgress.RecordWin(state, GobnomData.TrackCampaign, 1, 3, 200).NewBest, Is.True);
            Assert.That(GobnomProgress.LevelStars(state, GobnomData.TrackCampaign, 1), Is.EqualTo(3));
            Assert.That(GobnomProgress.IsCleared(state, GobnomData.TrackCoop, 1), Is.False);
            Assert.That(GobnomProgress.TotalStars(state, GobnomData.TrackCoop), Is.EqualTo(0));

            var coop = new JObject();
            Assert.That(GobnomProgress.MaxUnlocked(coop, GobnomData.TrackCoop), Is.EqualTo(1));
            for (var id = 1; id <= 10; id++) GobnomProgress.RecordWin(coop, GobnomData.TrackCoop, id, 1, 50);
            Assert.That(GobnomProgress.MaxUnlocked(coop, GobnomData.TrackCoop), Is.EqualTo(10));
            Assert.That(GobnomProgress.ClearedCount(coop, GobnomData.TrackCoop), Is.EqualTo(10));

            var score = (JObject)_balance["score"];
            var want = (int)score["win_base"] + 3 * (int)score["jar_bonus"] + 5 * (int)score["level_bonus"] + (int)score["first_clear_bonus"];
            Assert.That(GobnomProgress.FinalScore(5, 3, true, _balance), Is.EqualTo(want));
            Assert.That(GobnomProgress.FinalScore(5, 3, false, _balance), Is.EqualTo(want - (int)score["first_clear_bonus"]));
            Assert.That(GobnomProgress.LevelStars(null, "campaign", 1), Is.EqualTo(0));
            Assert.That(GobnomProgress.MaxUnlocked(null, "campaign"), Is.EqualTo(1));
            Assert.That(GobnomProgress.RecordWin((JObject)null, "campaign", 1, 3, 100).FirstClear, Is.True);
        }
    }
}
