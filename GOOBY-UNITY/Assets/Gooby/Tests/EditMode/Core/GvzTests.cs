using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Gvz;
using Gooby.Core.Minigames.Play;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// Goobys vs Zombies — ports of test_gvz_logic.gd, test_gvz_levels.gd and test_gvz_bot.gd: placement rules, the
    /// economy, determinism, panic Goobys, balloons, armour, special towers, boss Knurps, the conveyor, the save
    /// slice, and the bot certification (L1–L3 cruise without a panic Gooby, the mid-game holds, the boss ≥ 60 %).
    /// </summary>
    public class GvzTests
    {
        private static readonly string[] AllTowers =
        {
            "moehrenschuetze", "nutella_sammler", "dicker_bert", "schnarch_knolle", "boom_beere", "eis_gooby", "doppelmoehre",
            "magnet_gooby", "trampolin_gooby", "pust_gooby", "sternchen_gooby", "melonen_meier",
        };

        private JObject _balance;
        private List<JObject> _levels;

        [OneTimeSetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            _balance = GvzData.LoadBalance();
            _levels = GvzData.LoadLevels();
        }

        private static JObject Mini(object spawns = null, object extra = null)
        {
            var level = new JObject
            {
                ["id"] = 1,
                ["lanes"] = new JArray(0, 1, 2, 3, 4),
                ["start_nutella"] = 500,
                ["unlock_towers"] = new JArray(AllTowers),
                ["spawns"] = spawns != null ? JArray.FromObject(spawns) : new JArray(),
                ["waves"] = new JArray(),
            };
            if (extra != null) foreach (var p in JObject.FromObject(extra).Properties()) level[p.Name] = p.Value;
            return level;
        }

        private JObject Level(int id) => GvzData.LevelById(_levels, id);

        [Test]
        public void PlacementRulesAndTheGoldiCodeGate()
        {
            var run = GvzLogic.NewRun(Mini(), _balance);
            Assert.That(run.Nutella, Is.EqualTo(500));
            Assert.That(GvzLogic.PlaceTower(run, "moehrenschuetze", 0, 1).Ok, Is.True);
            Assert.That(run.Nutella, Is.EqualTo(400));
            Assert.That(GvzLogic.PlaceTower(run, "dicker_bert", 0, 1).Reason, Is.EqualTo("cell_occupied"));
            Assert.That(GvzLogic.PlaceTower(run, "moehrenschuetze", 1, 1).Reason, Is.EqualTo("cooldown"));
            Assert.That(GvzLogic.PlaceTower(run, "quatschturm", 0, 2).Reason, Is.EqualTo("unknown_tower"));
            Assert.That(GvzLogic.PlaceTower(run, "dicker_bert", 7, 0).Reason, Is.EqualTo("lane"));
            Assert.That(GvzLogic.PlaceTower(run, "dicker_bert", 0, 9).Reason, Is.EqualTo("col"));
            run.Nutella = 10;
            Assert.That(GvzLogic.PlaceTower(run, "dicker_bert", 1, 0).Reason, Is.EqualTo("nutella"));
            var locked = GvzLogic.NewRun(Mini(null, new { unlock_towers = new[] { "nutella_sammler" } }), _balance);
            Assert.That(GvzLogic.PlaceTower(locked, "moehrenschuetze", 0, 1).Reason, Is.EqualTo("locked"));

            Assert.That(GvzLogic.PlaceTower(GvzLogic.NewRun(Mini(), _balance), "goldi", 0, 0).Reason, Is.EqualTo("code_gate"));
            var gilded = GvzLogic.NewRun(Mini(), _balance, "normal", 1, goldi: true);
            Assert.That(GvzLogic.PlaceTower(gilded, "goldi", 0, 0).Ok, Is.True);
            Assert.That(GvzLogic.AvailableTowers(gilded), Has.Member("goldi"));
        }

        [Test]
        public void TheEconomyNeverGoesNegativeOrOverTheCapInABotRunAndDropsCap()
        {
            var run = GvzLogic.NewRun(Level(1), _balance, "normal", 1);
            var maxN = (int)_balance["economy"]["max_nutella"];
            while (!GvzLogic.IsOver(run) && run.Tick < GvzBot.MaxTicks)
            {
                GvzLogic.Tick(run);
                GvzBot.Act(run);
                Assert.That(run.Nutella, Is.InRange(0, maxN), "t=" + run.Tick);
            }
            Assert.That(run.Outcome, Is.EqualTo("won"));

            var capped = GvzLogic.NewRun(Mini(), _balance);
            capped.Nutella = maxN - 10;
            GvzLogic.SpawnDrop(capped, 0, 0, 25, "sky");
            var dropId = capped.Drops[0].Id;
            Assert.That(GvzLogic.CollectDrop(capped, dropId), Is.EqualTo(25));
            Assert.That(capped.Nutella, Is.EqualTo(maxN));
            Assert.That(GvzLogic.CollectDrop(capped, dropId), Is.EqualTo(-1));
        }

        [Test]
        public void TheSameSeedGivesTheSameHashAndEventStreamOtherSeedsDiverge()
        {
            var one = GvzLogic.NewRun(Level(3), _balance, "normal", 7);
            var two = GvzLogic.NewRun(Level(3), _balance, "normal", 7);
            var other = GvzLogic.NewRun(Level(3), _balance, "normal", 8);
            var trailOne = new List<string>();
            var trailTwo = new List<string>();
            var trailOther = new List<string>();
            for (var i = 0; i < 900; i++)
            {
                trailOne.Add(string.Join(";", GvzLogic.Tick(one)));
                GvzBot.Act(one);
                trailTwo.Add(string.Join(";", GvzLogic.Tick(two)));
                GvzBot.Act(two);
                trailOther.Add(string.Join(";", GvzLogic.Tick(other)));
                GvzBot.Act(other);
            }
            Assert.That(GvzLogic.StateHash(two), Is.EqualTo(GvzLogic.StateHash(one)));
            Assert.That(trailTwo, Is.EqualTo(trailOne));
            Assert.That(trailOther, Is.Not.EqualTo(trailOne));
            Assert.That(one.Tick == 900 && other.Tick == 900, Is.True);
        }

        [Test]
        public void APanicGoobySavesOnceThenTheHouseFallsAndAClearedLawnWins()
        {
            var run = GvzLogic.NewRun(Mini(new[] { new { t = 999.0, lane = 4, type = "schlurfi" } }), _balance);
            GvzZombies.Spawn(run, "schlurfi", 0, 100);
            var mowed = false;
            for (var i = 0; i < 40; i++) mowed |= GvzLogic.Tick(run).Any(e => e.Kind == "mower");
            Assert.That(mowed, Is.True);
            Assert.That(run.Mowers[0].Used, Is.True);
            Assert.That(run.Zombies, Is.Empty);
            Assert.That(run.Outcome, Is.EqualTo(""));
            for (var i = 0; i < 80; i++) GvzLogic.Tick(run);
            GvzZombies.Spawn(run, "schlurfi", 0, 100);
            for (var i = 0; i < 120 && !GvzLogic.IsOver(run); i++) GvzLogic.Tick(run);
            Assert.That(run.Outcome, Is.EqualTo("lost"));

            var lawn = GvzLogic.NewRun(Mini(new[] { new { t = 0.1, lane = 2, type = "schlurfi" } }), _balance);
            for (var i = 0; i < 1400 && !GvzLogic.IsOver(lawn); i++) GvzLogic.Tick(lawn);
            Assert.That(lawn.Outcome, Is.EqualTo("won"));
            Assert.That(lawn.Kills, Is.EqualTo(1));
            Assert.That(lawn.Score, Is.EqualTo((int)_balance["score"]["kill"]));
        }

        [Test]
        public void BalloonsFlyOverThePanicGoobyAndAGustPopsThem()
        {
            var run = GvzLogic.NewRun(Mini(new[] { new { t = 999.0, lane = 4, type = "schlurfi" } }), _balance);
            GvzZombies.Spawn(run, "ballon", 0, 300);
            for (var i = 0; i < 100 && !GvzLogic.IsOver(run); i++) GvzLogic.Tick(run);
            Assert.That(run.Outcome, Is.EqualTo("lost"));
            Assert.That(run.Mowers[0].Used, Is.False);

            var popper = GvzLogic.NewRun(Mini(), _balance);
            Assert.That(GvzLogic.PlaceTower(popper, "pust_gooby", 1, 0).Ok, Is.True);
            var balloon = GvzZombies.Spawn(popper, "ballon", 1, 6000);
            var popped = false;
            for (var i = 0; i < 70; i++) popped |= GvzLogic.Tick(popper).Any(e => e.Kind == "pop");
            Assert.That(popped, Is.True);
            Assert.That(balloon.Flying, Is.False);
        }

        [Test]
        public void TheShieldBlocksStraightShotsOnlyAndTheMagnetStealsArmour()
        {
            var run = GvzLogic.NewRun(Mini(), _balance);
            var bouncer = GvzZombies.Spawn(run, "tuersteher", 0, 5000);
            var hp = bouncer.Hp;
            GvzZombies.Damage(run, bouncer, 50, "carrot");
            Assert.That(bouncer.Hp, Is.EqualTo(hp));
            GvzZombies.Damage(run, bouncer, 50, "star");
            Assert.That(bouncer.Hp, Is.EqualTo(hp - 50));
            GvzZombies.Damage(run, bouncer, 50, "blast");
            Assert.That(bouncer.Hp, Is.EqualTo(hp - 100));

            var magnetRun = GvzLogic.NewRun(Mini(), _balance);
            Assert.That(GvzLogic.PlaceTower(magnetRun, "magnet_gooby", 0, 2).Ok, Is.True);
            var eimer = GvzZombies.Spawn(magnetRun, "eimer", 0, 5000);
            Assert.That(GvzZombies.MagnetSteal(magnetRun, magnetRun.Towers[GvzLogic.CellKey(0, 2)], 4), Is.True);
            Assert.That(eimer.ArmorHp, Is.EqualTo(0));
            Assert.That(eimer.Armor, Is.EqualTo(""));
        }

        [Test]
        public void KnolleTrampolineAndBoomBerryDoTheirJobs()
        {
            var run = GvzLogic.NewRun(Mini(), _balance);
            Assert.That(GvzLogic.PlaceTower(run, "schnarch_knolle", 0, 4).Ok, Is.True);
            run.Towers[GvzLogic.CellKey(0, 4)].ArmedAt = 0;
            var biter = GvzZombies.Spawn(run, "schlurfi", 0, 4700);
            GvzZombies.Step(run);
            Assert.That(biter.Dead, Is.True);
            Assert.That(run.Towers.ContainsKey(GvzLogic.CellKey(0, 4)), Is.False);
            Assert.That(GvzLogic.PlaceTower(run, "trampolin_gooby", 1, 4).Ok, Is.True);
            var jumper = GvzZombies.Spawn(run, "schlurfi", 1, 4700);
            GvzZombies.Step(run);
            Assert.That(jumper.X, Is.EqualTo(8600));

            var boom = GvzLogic.NewRun(Mini(), _balance);
            Assert.That(GvzLogic.PlaceTower(boom, "boom_beere", 2, 4).Ok, Is.True);
            var near = GvzZombies.Spawn(boom, "eimer", 2, 4400);
            var fuse = (int)_balance["towers"]["boom_beere"]["fuse_ticks"];
            for (var i = 0; i < fuse + 2; i++) GvzLogic.Tick(boom);
            Assert.That(boom.Towers.ContainsKey(GvzLogic.CellKey(2, 4)), Is.False);
            Assert.That(near.Dead, Is.True);
        }

        [Test]
        public void BossKnurpsEntersRagesSummonsAndFallsForTheWin()
        {
            var level = Mini(new[] { new { t = 999.0, lane = 4, type = "schlurfi" } }, new { boss = new { type = "boss_knurps", enter_at = 0 } });
            var run = GvzLogic.NewRun(level, _balance, "normal", 3);
            GvzLogic.Tick(run);
            Assert.That(run.Boss, Is.Not.Null);
            Assert.That(run.Boss.Phase, Is.EqualTo(1));
            var third = run.Boss.MaxHp / 3;
            GvzZombies.DamageBoss(run, run.Boss.Hp - third);
            bool summoned = false, phase3 = false;
            for (var i = 0; i < 400 && !(summoned && phase3); i++)
                foreach (var ev in GvzLogic.Tick(run))
                {
                    if (ev.Kind == "boss_summon") summoned = true;
                    if (ev.Kind == "boss_phase") phase3 = ev.Phase == 3;
                }
            Assert.That(phase3, Is.True);
            Assert.That(summoned, Is.True);
            Assert.That(run.Zombies, Is.Not.Empty);
            GvzZombies.DamageBoss(run, 999999);
            GvzLogic.Tick(run);
            Assert.That(run.Outcome, Is.EqualTo("won"));
        }

        [Test]
        public void ConveyorItemsAreFreeAndCooldownFree()
        {
            var level = Mini(new[] { new { t = 999.0, lane = 4, type = "schlurfi" } }, new
            {
                mods = new { conveyor = true, conveyor_hybrid = true },
                conveyor = new { start_delay_ticks = 2, interval_ticks = 10, max_queue = 3, pool = new[] { "eis_gooby" } },
            });
            var run = GvzLogic.NewRun(level, _balance);
            run.Nutella = 0;
            for (var i = 0; i < 5; i++) GvzLogic.Tick(run);
            Assert.That(run.Conveyor.Queue, Has.Member("eis_gooby"));
            Assert.That(GvzLogic.PlaceTower(run, "eis_gooby", 0, 3).Ok, Is.True);
            Assert.That(run.Nutella, Is.EqualTo(0));
            Assert.That(GvzLogic.CooldownLeft(run, "eis_gooby"), Is.EqualTo(0));
            Assert.That(run.Conveyor.Queue, Is.Empty);
        }

        [Test]
        public void TheProgressSliceScoresStarsAndStaysMonotone()
        {
            Assert.That(GvzProgress.StarsFor(0), Is.EqualTo(3));
            Assert.That(GvzProgress.StarsFor(1), Is.EqualTo(2));
            Assert.That(GvzProgress.StarsFor(4), Is.EqualTo(1));
            var score = (JObject)_balance["score"];
            var want = 20 + 3 * (int)score["star_bonus"] + 5 * (int)score["level_bonus"] + (int)score["first_clear_bonus"];
            Assert.That(GvzProgress.FinalScore(20, 5, 3, true, _balance), Is.EqualTo(want));
            var state = new JObject();
            var first = GvzProgress.RecordWin(state, 3, 2, 120);
            Assert.That(first.FirstClear, Is.True);
            Assert.That(GvzProgress.LevelStars(state, 3), Is.EqualTo(2));
            Assert.That(GvzProgress.IsCleared(state, 3), Is.True);
            Assert.That(GvzProgress.MaxUnlocked(state), Is.EqualTo(1), "L1 is not skipped");
            var again = GvzProgress.RecordWin(state, 3, 1, 90);
            Assert.That(again.FirstClear || again.NewBest, Is.False);
            Assert.That(GvzProgress.LevelStars(state, 3), Is.EqualTo(2));
            Assert.That((int)state["gvz"]["best"]["3"], Is.EqualTo(120));
        }

        [Test]
        public void LevelsLoadAndValidate()
        {
            Assert.That(_levels.Count, Is.EqualTo(15));
            Assert.That(GvzData.ValidateLevels(_levels, _balance), Is.Empty);
        }

        // ── bot certification (test_gvz_bot.gd) ──

        [Test]
        public void TheBotCruisesLevels1To3WithoutAPanicGooby()
        {
            foreach (var id in new[] { 1, 2, 3 })
                foreach (var seed in new[] { 1, 2, 3 })
                {
                    var r = GvzBot.Simulate(Level(id), _balance, seed);
                    Assert.That(r.Won, Is.True, $"L{id} seed={seed} lost ({r.Outcome} @{r.Ticks})");
                    Assert.That(r.MowersUsed, Is.EqualTo(0), $"L{id} seed={seed} not cruising");
                    Assert.That(r.Kills, Is.GreaterThan(0));
                    Assert.That(r.Stars, Is.EqualTo(GvzProgress.StarsFor(r.MowersUsed)));
                }
        }

        [Test]
        public void TheBotSurvivesTheMidgameAndCertifiesEasyAndHard()
        {
            foreach (var id in new[] { 8, 12, 13 })
            {
                var r = GvzBot.Simulate(Level(id), _balance, 1);
                Assert.That(r.Won, Is.True, $"L{id} lost ({r.Outcome} @{r.Ticks})");
            }
            foreach (var id in new[] { 2, 6, 12, 13, 14 })
                foreach (var diff in new[] { "easy", "hard" })
                {
                    var r = GvzBot.Simulate(Level(id), _balance, 1, diff);
                    Assert.That(r.Won, Is.True, $"L{id} {diff} lost ({r.Outcome} @{r.Ticks})");
                }
        }

        [Test]
        public void TheBotBeatsBossKnurpsAtLeast60PercentOnNormalAndHard()
        {
            foreach (var diff in new[] { "normal", "hard" })
            {
                var wins = 0;
                var withStars = false;
                for (var seed = 1; seed <= 5; seed++)
                {
                    var r = GvzBot.Simulate(Level(15), _balance, seed, diff);
                    if (!r.Won) continue;
                    wins++;
                    withStars |= r.Stars >= 1;
                }
                Assert.That(wins, Is.GreaterThanOrEqualTo(3), diff + " boss win rate");
                Assert.That(withStars, Is.True);
            }
            Assert.That(GvzBot.Simulate(Level(15), _balance, 1, "easy").Won, Is.True, "L15 easy");
        }

        [Test]
        public void BotRunsAreDeterministicAndHardBites()
        {
            var one = GvzBot.Simulate(Level(2), _balance, 42);
            var two = GvzBot.Simulate(Level(2), _balance, 42);
            Assert.That(two.Ticks, Is.EqualTo(one.Ticks));
            Assert.That(two.Kills, Is.EqualTo(one.Kills));
            Assert.That(two.Score, Is.EqualTo(one.Score));
            Assert.That(two.Won, Is.EqualTo(one.Won));
            var hard = GvzBot.Simulate(Level(2), _balance, 42, "hard");
            Assert.That(hard.Ticks != one.Ticks || hard.Won != one.Won, Is.True);
        }

        [Test]
        public void TheSessionBookstheCampaignAndTheStickerCounters()
        {
            var g = new GvzGame("normal", 77);
            Assert.That(g.IsUnlocked(2), Is.False);
            var wins = 0;
            g.PhaseChanged += p => { if (p == "won") wins++; };
            var guard = 0;
            while (GvzProgress.ClearedCount(g.Save) < 3 && guard++ < 200000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(GvzProgress.ClearedCount(g.Save), Is.EqualTo(3));
            Assert.That(wins, Is.GreaterThanOrEqualTo(3));
            Assert.That(g.SessionScore, Is.GreaterThan(0));
            Assert.That((int)g.Save["achievements"]["counters"]["gvzZombiesGestoppt"], Is.GreaterThan(0));
            Assert.That((int)g.Save["achievements"]["counters"]["gvzMoehrenSchuesse"], Is.GreaterThan(0));
            g.Finish();
            Assert.That(g.FinalScore, Is.EqualTo(g.SessionScore));
        }
    }
}
