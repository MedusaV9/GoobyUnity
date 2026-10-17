using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_rest2_quest_engine.gd plus a web golden (GOOBY/src/systems/quests.js via Node) for roll/reroll.</summary>
    public class DailyQuestTests
    {
        private const string Day = "2026-07-27";
        private const string Day2 = "2026-07-28";
        private const long NowMs = 1_768_478_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            DailyQuestCatalog.ResetCache();
        }

        private static JObject State() => JObject.Parse(@"{
            ""achievements"": {""counters"": {""feeds"":0,""washes"":0,""tickles"":0,""petsToday"":0,""petsDay"":"""",""plantings"":0,""waterings"":0,""harvests"":0,""questsDone"":0}},
            ""minigames"": {""plays"": {}, ""legacy"": {""best"": {}, ""bestByDiff"": {}, ""endlessBest"": {}}},
            ""economy"": {""coins"": 100, ""coinsEarned"": 0, ""coinsSpent"": 0},
            ""progression"": {""level"": 1, ""xp"": 0},
            ""quests"": {""completedTotal"": 0}}");

        private static QuestCtx Ctx(IEnumerable<string> games = null, int level = 1, bool garden = true) =>
            new QuestCtx { Level = level, Minigames = new HashSet<string>(games ?? new string[0]), Garden = garden };

        private static List<QuestDef> PoolSimple() => new[]
        {
            "{\"id\":\"feed2\",\"kategorie\":\"care\",\"messung\":{\"typ\":\"counter\",\"key\":\"feeds\"},\"ziel\":2,\"muenzen\":20,\"xp\":10}",
            "{\"id\":\"wash1\",\"kategorie\":\"care\",\"messung\":{\"typ\":\"counter\",\"key\":\"washes\"},\"ziel\":1,\"muenzen\":15,\"xp\":8}",
            "{\"id\":\"spiel1\",\"kategorie\":\"games\",\"messung\":{\"typ\":\"spiele_gesamt\"},\"ziel\":1,\"muenzen\":25,\"xp\":12}",
            "{\"id\":\"garten1\",\"kategorie\":\"garden\",\"messung\":{\"typ\":\"counter\",\"key\":\"plantings\"},\"ziel\":1,\"muenzen\":20,\"xp\":10,\"braucht\":{\"garden\":true}}",
            "{\"id\":\"punkte30\",\"kategorie\":\"games\",\"messung\":{\"typ\":\"spiel_punkte\",\"spiel\":\"carrotCatch\"},\"ziel\":30,\"muenzen\":25,\"xp\":12,\"braucht\":{\"minigame\":\"carrotCatch\"}}",
        }.Select(s => QuestDef.From(JObject.Parse(s))).ToList();

        private static List<string> Ids(JObject slice) => ((JArray)slice["active"]).Select(e => J.Str(e["id"])).ToList();

        [Test]
        public void Hash32AndRngAreWebParity()
        {
            Assert.That(DailyQuestEngine.Hash32("2026-07-27"), Is.EqualTo(1187339125u));
            Assert.That(DailyQuestEngine.Hash32("2026-07-28"), Is.EqualTo(2735185966u));
            Assert.That(DailyQuestEngine.Hash32("2026-07-27:r"), Is.EqualTo(3622789181u));
            Assert.That(DailyQuestEngine.Hash32("gooby"), Is.EqualTo(3364636767u));
        }

        [Test]
        public void RollAndRerollMatchTheWebGolden()
        {
            var golden = Fixtures.LoadObject("quests_roll_golden.json");
            var pool = DailyQuestCatalog.Pool();
            Assert.That(pool.Count, Is.EqualTo(24));
            var gameSets = new Dictionary<string, (string[] games, int level)>
            {
                ["all4"] = (new[] { "carrotCatch", "bunnyHop", "runner", "teaParty" }, 1),
                ["none"] = (new string[0], 1),
                ["many"] = (new[] { "carrotCatch", "bunnyHop", "runner", "teaParty", "fishingPond", "danceParty", "miniGolf", "goobySays", "trampoline", "deliveryRush", "gvz", "gobnom" }, 12),
            };
            var cases = 0;
            foreach (var p in golden.Properties())
            {
                var parts = p.Name.Split('|');
                var day = parts[0];
                var set = gameSets[parts[1]];
                var ctx = Ctx(set.games, set.level);
                var slice = new JObject { ["completedTotal"] = 0 };
                Assert.That(DailyQuestEngine.RollToday(slice, day, pool, ctx, State()), Is.True);
                Assert.That(Ids(slice), Is.EqualTo(p.Value["roll"].Select(t => t.ToString()).ToList()), p.Name + " roll");
                var rerolled = (JObject)slice.DeepClone();
                Assert.That(DailyQuestEngine.RerollToday(rerolled, day, pool, ctx, State()), Is.True);
                Assert.That(Ids(rerolled), Is.EqualTo(p.Value["reroll"].Select(t => t.ToString()).ToList()), p.Name + " reroll");
                if (day == Day)
                {
                    // web golden touched the first quest (feed3 → progress 1): bump the feeds counter here
                    var state = State();
                    state["achievements"]["counters"]["feeds"] = 1;
                    var partial = (JObject)slice.DeepClone();
                    Assert.That(DailyQuestEngine.RerollToday(partial, day, pool, ctx, state), Is.True);
                    Assert.That(Ids(partial), Is.EqualTo(p.Value["rerollKeepFirst"].Select(t => t.ToString()).ToList()), p.Name + " partial reroll");
                }
                cases++;
            }
            Assert.That(cases, Is.EqualTo(12));
        }

        [Test]
        public void RollIsDeterministicAndCategoryDiverse()
        {
            var pool = DailyQuestCatalog.Pool();
            var games = new[] { "carrotCatch", "bunnyHop", "runner", "teaParty" };
            var a = new JObject { ["completedTotal"] = 0 };
            var b = new JObject { ["completedTotal"] = 0 };
            Assert.That(DailyQuestEngine.RollToday(a, Day, pool, Ctx(games), State()), Is.True);
            Assert.That(DailyQuestEngine.RollToday(b, Day, pool, Ctx(games), State()), Is.True);
            Assert.That(J.Str(a["day"]), Is.EqualTo(Day));
            Assert.That(Ids(a).Count, Is.EqualTo(3));
            Assert.That(Ids(a), Is.EqualTo(Ids(b)));
            var byId = DailyQuestEngine.PoolById(pool);
            Assert.That(Ids(a).Select(id => byId[id].Kategorie).Distinct().Count(), Is.GreaterThanOrEqualTo(2));
            Assert.That(DailyQuestEngine.RollToday(a, Day, pool, Ctx(games), State()), Is.False);
            Assert.That(DailyQuestEngine.RollNeeded(a, Day2), Is.True);
        }

        [Test]
        public void RequiresFilterAgainstProgress()
        {
            var pool = PoolSimple();
            Assert.That(DailyQuestEngine.EligibleDefs(pool, Ctx()).Select(d => d.Id), Does.Not.Contain("punkte30"));
            Assert.That(DailyQuestEngine.EligibleDefs(pool, Ctx(new[] { "carrotCatch" })).Select(d => d.Id), Does.Contain("punkte30"));
            Assert.That(DailyQuestEngine.EligibleDefs(pool, Ctx(garden: false)).Select(d => d.Id), Does.Not.Contain("garten1"));
            var levelPool = new List<QuestDef> { QuestDef.From(JObject.Parse("{\"id\":\"lvl\",\"kategorie\":\"care\",\"messung\":{\"typ\":\"counter\",\"key\":\"feeds\"},\"ziel\":1,\"braucht\":{\"level\":5}}")) };
            Assert.That(DailyQuestEngine.EligibleDefs(levelPool, Ctx(level: 4)).Count, Is.EqualTo(0));
            Assert.That(DailyQuestEngine.EligibleDefs(levelPool, Ctx(level: 5)).Count, Is.EqualTo(1));
        }

        [Test]
        public void CounterProgressClaimAndDoubleClaim()
        {
            var pool = PoolSimple();
            var state = State();
            var def = pool[0];
            var slice = new JObject { ["completedTotal"] = 0, ["day"] = Day, ["active"] = new JArray(DailyQuestEngine.MakeEntry(def, state)) };
            var entry = (JObject)slice["active"][0];
            state["achievements"]["counters"]["feeds"] = 0;
            Assert.That(DailyQuestEngine.ProgressOf(entry, def, state), Is.EqualTo(0));
            state["achievements"]["counters"]["feeds"] = 1;
            Assert.That(DailyQuestEngine.ProgressOf(entry, def, state), Is.EqualTo(1));
            state["achievements"]["counters"]["feeds"] = 9;
            Assert.That(DailyQuestEngine.ProgressOf(entry, def, state), Is.EqualTo(2), "capped at target");
            var res = DailyQuestEngine.Claim(slice, "feed2", def, state);
            Assert.That(res.Ok, Is.True);
            Assert.That(res.Muenzen, Is.EqualTo(20));
            Assert.That(res.Xp, Is.EqualTo(10));
            Assert.That(J.Int(slice["completedTotal"]), Is.EqualTo(1));
            Assert.That(DailyQuestEngine.Claim(slice, "feed2", def, state).Ok, Is.False);
        }

        [Test]
        public void ScoreQuestCountsOnlyAfterARoundToday()
        {
            var pool = PoolSimple();
            var def = pool[4];
            var state = State();
            state["minigames"]["plays"]["carrotCatch"] = 2;
            state["minigames"]["legacy"]["best"]["carrotCatch"] = 80;
            var entry = DailyQuestEngine.MakeEntry(def, state);
            Assert.That(DailyQuestEngine.ProgressOf(entry, def, state), Is.EqualTo(0), "old best does not count before a new round");
            state["minigames"]["plays"]["carrotCatch"] = 3;
            Assert.That(DailyQuestEngine.ProgressOf(entry, def, state), Is.EqualTo(30), "after a round: best capped at target");
        }

        [Test]
        public void RerollReplacesOnlyUntouchedAndOnlyOnce()
        {
            var pool = PoolSimple();
            var state = State();
            var slice = new JObject { ["completedTotal"] = 0 };
            DailyQuestEngine.RollToday(slice, Day, pool, Ctx(), state);
            var before = Ids(slice);
            var byId = DailyQuestEngine.PoolById(pool);
            var touched = before[0];
            var m = byId[touched].Messung;
            if (J.Str(m["typ"]) == "counter") state["achievements"]["counters"][J.Str(m["key"])] = 1;
            else state["minigames"]["plays"]["teaParty"] = 1;
            Assert.That(DailyQuestEngine.RerollToday(slice, Day, pool, Ctx(), state), Is.True);
            var after = Ids(slice);
            Assert.That(after, Does.Contain(touched));
            Assert.That(after.Count, Is.EqualTo(3));
            Assert.That(J.Str(slice["rerolledDay"]), Is.EqualTo(Day));
            Assert.That(DailyQuestEngine.RerollToday(slice, Day, pool, Ctx(), state), Is.False);
        }

        [Test]
        public void BonusIsDueOnceWhenAllThreeClaimed()
        {
            var slice = JObject.Parse("{\"completedTotal\":3,\"day\":\"" + Day + "\",\"bonusDay\":\"\",\"active\":[{\"id\":\"a\",\"claimed\":true,\"base\":{}},{\"id\":\"b\",\"claimed\":true,\"base\":{}},{\"id\":\"c\",\"claimed\":true,\"base\":{}}]}");
            Assert.That(DailyQuestEngine.AllClaimed(slice), Is.True);
            Assert.That(DailyQuestEngine.BonusDue(slice, Day), Is.True);
            DailyQuestEngine.MarkBonusPaid(slice, Day);
            Assert.That(DailyQuestEngine.BonusDue(slice, Day), Is.False);
            slice["active"][2]["claimed"] = false;
            Assert.That(DailyQuestEngine.AllClaimed(slice), Is.False);
        }

        [Test]
        public void ServiceRollsClaimsPaysAndBonuses()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            var svc = new DailyQuestService(store, () => Ctx(new[] { "carrotCatch", "bunnyHop", "runner", "teaParty" }));
            var changed = 0;
            svc.QuestsChanged += () => changed++;
            svc.EnsureRoll();
            Assert.That(changed, Is.EqualTo(1));
            var board = svc.Board();
            Assert.That(board.Count, Is.EqualTo(3));
            Assert.That(svc.ClaimableCount(), Is.EqualTo(0));
            Assert.That(svc.RerollAvailable(), Is.True);
            svc.EnsureRoll();
            Assert.That(changed, Is.EqualTo(1), "second roll same day is a no-op");
            // complete every quest by satisfying its measurement directly
            var coinsBefore = J.Int(store.State["economy"]["coins"]);
            var paid = 0;
            foreach (var row in board)
            {
                store.Update(s => Satisfy(s, row.Def));
                Assert.That(svc.ClaimableCount(), Is.GreaterThanOrEqualTo(1), row.Def.Id + " claimable");
                var outcome = svc.Claim(row.Def.Id);
                Assert.That(outcome.Ok, Is.True, row.Def.Id);
                paid += outcome.Muenzen;
                Assert.That(svc.Claim(row.Def.Id).Ok, Is.False, "no double claim");
            }
            Assert.That(svc.AllClaimed(), Is.True);
            Assert.That(J.Int(store.State["quests"]["completedTotal"]), Is.EqualTo(3));
            Assert.That(J.Int(store.State["achievements"]["counters"]["questsDone"]), Is.EqualTo(3));
            Assert.That(J.Str(store.State["quests"]["bonusDay"]), Is.EqualTo(svc.Today), "bonus paid once all three are claimed");
            var levelUpCoins = J.Int(store.State["economy"]["coins"]) - coinsBefore - paid - DailyQuestEngine.BonusCoins;
            Assert.That(levelUpCoins, Is.GreaterThanOrEqualTo(0), "coins = quest rewards + bonus (+ level-up coins)");
        }

        [Test]
        public void GardenCardPrefersWateringRoundOverOtherDailies()
        {
            var care = new DailyQuestService.Row { Def = QuestDef.From(JObject.Parse("{\"id\":\"merken\",\"kategorie\":\"games\",\"ziel\":60}")), Target = 60 };
            var water = new DailyQuestService.Row { Def = QuestDef.From(JObject.Parse("{\"id\":\"water4\",\"kategorie\":\"garden\",\"ziel\":4}")), Target = 4 };
            var plant = new DailyQuestService.Row { Def = QuestDef.From(JObject.Parse("{\"id\":\"plant2\",\"kategorie\":\"garden\",\"ziel\":2}")), Target = 2 };
            Assert.That(DailyQuestService.PickGardenCard(new[] { care, water, plant }).Def.Id, Is.EqualTo("water4"));
            water.Claimed = true;
            Assert.That(DailyQuestService.PickGardenCard(new[] { care, water, plant }).Def.Id, Is.EqualTo("plant2"));
            plant.Claimed = true;
            Assert.That(DailyQuestService.PickGardenCard(new[] { care, water, plant }), Is.Null);
        }

        [Test]
        public void WateringRoundReadsTheGardenCounter()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            store.Update(s => J.EnsureDict(J.EnsureDict(s, "achievements"), "counters")["waterings"] = 2);
            var svc = new DailyQuestService(store, () => Ctx());
            var round = svc.WateringRound();
            Assert.That(round.target, Is.EqualTo(4), "water4.ziel");
            Assert.That(round.progress, Is.EqualTo(2));
        }

        private static void Satisfy(JObject s, QuestDef def)
        {
            var m = def.Messung;
            var counters = J.EnsureDict(J.EnsureDict(s, "achievements"), "counters");
            var plays = J.EnsureDict(J.EnsureDict(s, "minigames"), "plays");
            switch (J.Str(m["typ"]))
            {
                case "counter": counters[J.Str(m["key"])] = J.Int(counters[J.Str(m["key"])]) + def.Ziel; break;
                case "streicheln_heute": counters["petsToday"] = def.Ziel; break;
                case "spiele_gesamt": plays["teaParty"] = J.Int(plays["teaParty"]) + def.Ziel; break;
                case "spiele_verschieden": for (var i = 0; i < def.Ziel; i++) plays["g" + i] = J.Int(plays["g" + i]) + 1; break;
                case "spiel_runden": plays[J.Str(m["spiel"])] = J.Int(plays[J.Str(m["spiel"])]) + def.Ziel; break;
                case "spiel_punkte":
                    plays[J.Str(m["spiel"])] = J.Int(plays[J.Str(m["spiel"])]) + 1;
                    J.EnsureDict(J.EnsureDict((JObject)s["minigames"], "legacy"), "best")[J.Str(m["spiel"])] = def.Ziel;
                    break;
                case "muenzen_verdient": s["economy"]["coinsEarned"] = J.Int(s["economy"]["coinsEarned"]) + def.Ziel; break;
                case "muenzen_ausgegeben": s["economy"]["coinsSpent"] = J.Int(s["economy"]["coinsSpent"]) + def.Ziel; break;
            }
        }
    }
}
