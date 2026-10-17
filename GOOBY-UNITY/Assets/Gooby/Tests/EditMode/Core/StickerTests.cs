using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_album_unlocks.gd + catalog integrity over the content pack.</summary>
    public class StickerTests
    {
        private const long NowMs = 1_768_478_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            StickerCatalog.ResetCache();
        }

        private static JObject State() => JObject.Parse(@"{
            ""achievements"": {""counters"": {""feeds"": 3}},
            ""progression"": {""level"": 7},
            ""daily"": {""streak"": 4, ""lastClaimDay"": ""2026-07-27""},
            ""stickers"": {""unlocked"": {}, ""hooks"": {""gvz_kampagne"": true}},
            ""codes"": {""redeemed"": {""herz"": 123}},
            ""vacation"": {""archive"": [{""destId"":""beach"",""dayIndex"":1,""variant"":2,""atMs"":5}]}}");

        [Test]
        public void CatalogIsValidAndHasImagesAndPages()
        {
            var all = StickerCatalog.All();
            var pages = StickerCatalog.Pages();
            Assert.That(all.Count, Is.EqualTo(144));
            Assert.That(pages.Count, Is.EqualTo(23));
            Assert.That(StickerCatalog.Validate(all, pages), Is.Empty);
            Assert.That(StickerCatalog.ByPage(all).Keys.Count, Is.EqualTo(pages.Count), "every page carries stickers");
            Assert.That(StickerCatalog.RegularCount(all), Is.LessThanOrEqualTo(all.Count));
            var assetDir = Path.Combine(Fixtures.ResourcesRoot, "content", "stickers", "assets");
            var shipped = new HashSet<string>(Directory.GetFiles(assetDir).Select(Path.GetFileName));
            Assert.That(shipped.Count, Is.GreaterThan(70));
            // every special key used by the pack is understood by the rules engine
            var probe = SaveSchema.DefaultState(NowMs);
            foreach (var def in all.Where(d => J.Str(d.Cond["type"]) == "special"))
                Assert.That(StickerRules.SpecialValue(probe, J.Str(def.Cond["key"]), def.Cond["sub"] as JObject), Is.GreaterThanOrEqualTo(0.0), def.Id + " special key handled");
        }

        [Test]
        public void CondMetCounterSpecialEventCode()
        {
            var state = State();
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"counter\",\"key\":\"feeds\",\"count\":3}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"counter\",\"key\":\"feeds\",\"count\":4}"), state), Is.False);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"counter\",\"key\":\"washes\",\"count\":1}"), state), Is.False);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"level\",\"count\":7}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"level\",\"count\":8}"), state), Is.False);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"streak\",\"count\":4}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"first_boot\"}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"unbekannt\",\"count\":1}"), state), Is.False);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"postcards\",\"count\":1,\"sub\":{\"dest\":\"beach\"}}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"postcards\",\"count\":1,\"sub\":{\"dest\":\"harbor\"}}"), state), Is.False);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"special\",\"key\":\"seasonPlay\",\"count\":1,\"sub\":{\"season\":\"sommer\"}}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"event\",\"key\":\"gvz_kampagne\"}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"event\",\"key\":\"nie_gefeuert\"}"), state), Is.False);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"code\",\"key\":\"herz\"}"), state), Is.True);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"code\",\"key\":\"anderer\"}"), state), Is.False);
            Assert.That(StickerRules.CondMet(JObject.Parse("{\"type\":\"quatsch\",\"key\":\"x\"}"), state), Is.False);
        }

        [Test]
        public void NewlyMetSkipsUnlockedAndSecretCounting()
        {
            var state = State();
            state["stickers"]["unlocked"] = new JObject { ["s3"] = 1 };
            var catalog = new List<StickerDef>
            {
                StickerDef.From(JObject.Parse("{\"id\":\"s1\",\"page\":\"p\",\"cond\":{\"type\":\"counter\",\"key\":\"feeds\",\"count\":1}}")),
                StickerDef.From(JObject.Parse("{\"id\":\"s2\",\"page\":\"p\",\"cond\":{\"type\":\"counter\",\"key\":\"feeds\",\"count\":99}}")),
                StickerDef.From(JObject.Parse("{\"id\":\"s3\",\"page\":\"p\",\"secret\":true,\"cond\":{\"type\":\"counter\",\"key\":\"feeds\",\"count\":1}}")),
            };
            var fresh = StickerRules.NewlyMet(catalog, state);
            Assert.That(fresh.Select(d => d.Id), Is.EqualTo(new[] { "s1" }));
            Assert.That(StickerRules.IsUnlocked(state, "s3"), Is.True);
            Assert.That(StickerRules.UnlockedCount(state, catalog), Is.EqualTo(0), "secret ones do not count");
            Assert.That(StickerRules.PageProgress(state, catalog, "p"), Is.EqualTo((1, 3)), "unlocked secrets count towards the page");
        }

        [Test]
        public void ServiceUnlocksOnSignalsAndPaysPageReward()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            var catalog = new List<StickerDef>
            {
                StickerDef.From(JObject.Parse("{\"id\":\"erster_happs\",\"page\":\"p\",\"cond\":{\"type\":\"counter\",\"key\":\"feeds\",\"count\":1}}")),
                StickerDef.From(JObject.Parse("{\"id\":\"gvz_sieg\",\"page\":\"p\",\"cond\":{\"type\":\"event\",\"key\":\"gvz_kampagne\"}}")),
            };
            var svc = new StickerService(store, catalog);
            var ids = new List<string>();
            var pages = new List<string>();
            svc.Unlocked += d => ids.Add(d.Id);
            svc.PageCompleted += (p, coins) => pages.Add(p.Id + ":" + coins);
            svc.Attach();
            Assert.That(ids, Is.Empty);
            var coins = J.Int(store.State["economy"]["coins"]);
            store.Update(s => s["achievements"]["counters"]["feeds"] = 1);
            store.NotifySliceChanged("achievements");
            Assert.That(ids, Is.EqualTo(new[] { "erster_happs" }));
            Assert.That(J.Long(store.State["stickers"]["unlocked"]["erster_happs"]), Is.EqualTo(NowMs));
            StickerRules.FireEventHook(store, "gvz_kampagne");
            Assert.That(ids, Is.EqualTo(new[] { "erster_happs", "gvz_sieg" }));
            Assert.That(pages, Is.EqualTo(new[] { "p:120" }), "page complete → set reward once");
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(coins + 120));
            store.NotifySliceChanged("achievements");
            Assert.That(ids.Count, Is.EqualTo(2), "no double unlock");
            Assert.That(pages.Count, Is.EqualTo(1), "no double reward");
        }

        [Test]
        public void RealCatalogFirstStickerUnlocksAfterFirstFeed()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            var svc = new StickerService(store);
            var ids = new List<string>();
            svc.Unlocked += d => ids.Add(d.Id);
            svc.Attach();
            store.Update(s => s["achievements"]["counters"]["feeds"] = 1);
            store.NotifySliceChanged("achievements");
            Assert.That(ids, Does.Contain("firstNom"));
        }

        [Test]
        public void CrateNapHookUnlocksKartonversteck()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            var svc = new StickerService(store);
            var ids = new List<string>();
            svc.Unlocked += d => ids.Add(d.Id);
            svc.Attach();
            StickerRules.FireEventHook(store, "karton_gooby");
            Assert.That(ids, Does.Contain("ev_karton_gooby"));
            Assert.That(StickerRules.IsUnlocked(store.State, "ev_karton_gooby"), Is.True);
        }
    }
}
