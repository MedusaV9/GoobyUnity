using System.Collections.Generic;
using System.IO;
using Gooby.Core.Content;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_rest1_achievements.gd against the JSON content pack.</summary>
    public class AchievementsTests
    {
        private const long NowMs = 1_768_478_400_000L;
        private const int WebCount = 44;
        private const int WebCoinsTotal = 3410;

        [SetUp]
        public void InstallContent()
        {
            Fixtures.InstallContentSource();
            AchievementsCatalog.ResetCache();
            RoomDefs.ResetCache();
        }

        private static JObject Fresh() => SaveSchema.DefaultState(NowMs);

        [Test]
        public void CatalogHas44ValidAchievementsWorth3410Coins()
        {
            var catalog = AchievementsCatalog.All();
            Assert.That(catalog.Count, Is.EqualTo(WebCount));
            Assert.That(AchievementsCatalog.TotalCoins(), Is.EqualTo(WebCoinsTotal));
            Assert.That(AchievementsCatalog.Validate(catalog), Is.Empty);
            var perCat = 0;
            foreach (var cat in AchievementsCatalog.Categories) perCat += AchievementsCatalog.ByCategory(cat).Count;
            Assert.That(perCat, Is.EqualTo(WebCount));
            for (var i = 1; i < catalog.Count; i++) Assert.That(catalog[i].Order, Is.GreaterThanOrEqualTo(catalog[i - 1].Order));
        }

        [Test]
        public void StringsExistForEveryAchievementInBothLocales()
        {
            foreach (var locale in new[] { "de", "en" })
            {
                var defs = JObject.Parse(File.ReadAllText(Path.Combine(Fixtures.ResourcesRoot, "strings", locale, "achievements.json")))["achievements"]?["defs"] as JObject;
                Assert.That(defs, Is.Not.Null, locale + ": defs block");
                foreach (var def in AchievementsCatalog.All())
                    Assert.That(defs[def.Id], Is.InstanceOf<JObject>(), locale + ": entry for " + def.Id);
            }
        }

        [Test]
        public void CounterConditions()
        {
            var state = Fresh();
            var firstFeed = AchievementsCatalog.ById("firstFeed");
            Assert.That(AchievementsEngine.ProgressOf(firstFeed, state).current, Is.EqualTo(0));
            state["achievements"]["counters"]["feeds"] = 1;
            Assert.That(AchievementsEngine.IsSatisfied(firstFeed, state), Is.True);
            var feed100 = AchievementsCatalog.ById("feed100");
            Assert.That(AchievementsEngine.ProgressOf(feed100, state).current, Is.EqualTo(1));
            state["achievements"]["counters"]["feeds"] = 250;
            var p = AchievementsEngine.ProgressOf(feed100, state);
            Assert.That(p.current, Is.EqualTo(100), "current clamps to target");
            Assert.That(AchievementsEngine.IsSatisfied(feed100, state), Is.True);
        }

        [Test]
        public void SpecialConditions()
        {
            var state = Fresh();
            var cases = new List<(string id, System.Action apply)>
            {
                ("coins1000", () => state["economy"]["coins"] = 1000),
                ("level10", () => state["progression"]["level"] = 10),
                ("streak7", () => state["daily"]["streak"] = 7),
                ("chonkZone", () => state["gooby"]["weight"] = 86.0),
                ("sleekMode", () => state["gooby"]["weight"] = 25.0),
                ("parkDay", () => state["park"]["visits"] = 1),
                ("coasterFan", () => state["park"]["rides"]["coaster"] = 5),
                ("wheelRide", () => state["park"]["rides"]["wheel"] = 1),
                ("funkelnacht", () => state["park"]["nightVisit"] = true),
                ("weltenbummler", () => { for (var i = 0; i < 9; i++) state["vacation"]["visited"]["ziel" + i] = NowMs; }),
            };
            foreach (var c in cases)
            {
                var def = AchievementsCatalog.ById(c.id);
                Assert.That(def, Is.Not.Null, c.id);
                Assert.That(AchievementsEngine.IsSatisfied(def, state), Is.False, c.id + ": fresh open");
                c.apply();
                Assert.That(AchievementsEngine.IsSatisfied(def, state), Is.True, c.id + ": satisfied");
            }
            var outfit = AchievementsCatalog.ById("fullOutfit");
            Assert.That(AchievementsEngine.ProgressOf(outfit, state).current, Is.EqualTo(0));
            state["cosmetics"]["outfits"]["equipped"] = new JObject { ["hat"] = "tophat", ["glasses"] = "round", ["neck"] = "scarf", ["back"] = JValue.CreateNull() };
            Assert.That(AchievementsEngine.IsSatisfied(outfit, state), Is.True);
            var never = AchievementsCatalog.ById("neverSick");
            Assert.That(AchievementsEngine.IsSatisfied(never, state), Is.True, "level 10, never sick");
            state["achievements"]["counters"]["sickEver"] = 1;
            Assert.That(AchievementsEngine.IsSatisfied(never, state), Is.False, "once sick → forfeited");
        }

        [Test]
        public void StickerAndCollectionConditions()
        {
            var state = Fresh();
            var first = AchievementsCatalog.ById("firstSticker");
            Assert.That(AchievementsEngine.IsSatisfied(first, state), Is.False);
            state["stickers"]["unlocked"]["st1"] = NowMs;
            Assert.That(AchievementsEngine.IsSatisfied(first, state), Is.True);
            var book10 = AchievementsCatalog.ById("stickerBook10");
            for (var i = 0; i < 10; i++) state["stickers"]["unlocked"]["st" + i] = NowMs;
            Assert.That(AchievementsEngine.IsSatisfied(book10, state), Is.True);
            var setComplete = AchievementsCatalog.ById("setComplete");
            Assert.That(AchievementsEngine.IsSatisfied(setComplete, state), Is.False);
            state["stickers"]["setRewards"] = new JObject { ["tiere"] = NowMs };
            Assert.That(AchievementsEngine.IsSatisfied(setComplete, state), Is.True);
        }

        [Test]
        public void DecorCountsOnlyNonDefaultItemsAndRoomDefsLoad()
        {
            Assert.That(RoomDefs.Ids(), Does.Contain("living"));
            Assert.That(RoomDefs.Get("living").GridW, Is.EqualTo(12));
            Assert.That(RoomDefs.DefaultStorage().Count, Is.GreaterThan(0));
            var defaults = RoomDefs.DefaultItemSet();
            Assert.That(defaults, Does.Contain("loungeSofa"));
            var state = Fresh();
            state["home"]["rooms"] = new JObject
            {
                ["living"] = new JObject { ["items"] = new JArray(new JObject { ["item"] = "loungeSofa" }, new JObject { ["item"] = "discoBall" }) },
            };
            state["decor"]["wallpaper"]["living"] = "stripes";
            Assert.That(AchievementsEngine.CountNonDefaultDecor(state), Is.EqualTo(2));
        }

        [Test]
        public void ServiceUnlocksOncePaysOnceAndChainsRewards()
        {
            var io = new MemorySaveIo();
            var store = new GameStateStore { Manager = { Io = io } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            var service = new AchievementsService(store);
            var events = new List<AchievementDef>();
            service.Unlocked += d => events.Add(d);
            service.Attach();
            Assert.That(events, Is.Empty, "fresh save unlocks nothing");
            var coinsBefore = J.Int(store.State["economy"]["coins"]);
            store.Update(s => s["achievements"]["counters"]["feeds"] = 1);
            store.NotifySliceChanged("achievements");
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Id, Is.EqualTo("firstFeed"));
            Assert.That(J.Long(store.State["achievements"]["unlocked"]["firstFeed"]), Is.EqualTo(NowMs));
            var coinsAfter = J.Int(store.State["economy"]["coins"]);
            Assert.That(coinsAfter, Is.EqualTo(coinsBefore + 10));
            store.NotifySliceChanged("achievements");
            Assert.That(events.Count, Is.EqualTo(1), "no double celebration");
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(coinsAfter), "no double reward");
            // reward chain: 995 coins + feed reward(already paid) → set coins to 990 and unlock a 10-coin achievement → coins1000 follows
            store.Update(s => { s["economy"]["coins"] = 990; s["achievements"]["counters"]["washes"] = 1; });
            store.NotifySliceChanged("achievements");
            Assert.That(events.Exists(d => d.Id == "coins1000"), Is.True, "reward can satisfy a follow-up achievement");
        }
    }
}
