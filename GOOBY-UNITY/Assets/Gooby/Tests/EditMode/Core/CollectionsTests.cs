using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_w13_collections_ui.gd (pure album logic + claim booking).</summary>
    public class CollectionsTests
    {
        private const long NowMs = 1_768_478_400_000L;

        private static JObject FullSetSlice(string setId)
        {
            var c = new JObject { ["entries"] = new JObject(), ["claimedSets"] = new JObject() };
            foreach (var id in CollectionsLogic.GetSet(setId).Entries) ((JObject)c["entries"])[CollectionsLogic.EntryKey(setId, id)] = 1;
            return c;
        }

        [Test]
        public void SetDefinitionsAreWebVerbatim()
        {
            Assert.That(CollectionsLogic.Sets.Length, Is.EqualTo(4));
            Assert.That(CollectionsLogic.GetSet("fish").Entries.Length, Is.EqualTo(8));
            Assert.That(CollectionsLogic.GetSet("treats").Entries.Length, Is.EqualTo(10));
            Assert.That(CollectionsLogic.GetSet("landmarks").Coins, Is.EqualTo(150));
            Assert.That(CollectionsLogic.TotalProgress(new JObject()).total, Is.EqualTo(32));
        }

        [Test]
        public void NormalizeHealsBrokenSlices()
        {
            var c = CollectionsLogic.NormalizeSlice(JObject.Parse("{\"entries\":{\"fish.pinkKoi\":\"2\",\"\":3,\"veggies.carrot\":0,\"treats.cake\":2.7},\"claimedSets\":{\"fish\":\"x\"}}"));
            Assert.That(((JObject)c["entries"]).Count, Is.EqualTo(1));
            Assert.That(J.Int(c["entries"]["treats.cake"]), Is.EqualTo(2));
            Assert.That(J.Long(c["claimedSets"]["fish"]), Is.EqualTo(0));
            Assert.That(CollectionsLogic.NormalizeSlice(JToken.Parse("[1]"))["entries"], Is.InstanceOf<JObject>());
        }

        [Test]
        public void AwardIsPureAndReportsFirst()
        {
            var c = new JObject { ["entries"] = new JObject(), ["claimedSets"] = new JObject { ["fish"] = 123 } };
            var first = CollectionsLogic.Award(c, "fish", "pinkKoi");
            Assert.That(first.First, Is.True);
            Assert.That(CollectionsLogic.CountOf(first.C, "fish", "pinkKoi"), Is.EqualTo(1));
            Assert.That(J.Int(first.C["claimedSets"]["fish"]), Is.EqualTo(123));
            Assert.That(((JObject)c["entries"]).Count, Is.EqualTo(0), "pure: input untouched");
            var again = CollectionsLogic.Award(first.C, "fish", "pinkKoi", 2);
            Assert.That(again.First, Is.False);
            Assert.That(CollectionsLogic.CountOf(again.C, "fish", "pinkKoi"), Is.EqualTo(3));
            var noop = CollectionsLogic.Award(c, "fish", "pinkKoi", 0);
            Assert.That(noop.First, Is.False);
            Assert.That(ReferenceEquals(noop.C, c), Is.True);
            Assert.That(CollectionsLogic.Award(c, "", "pinkKoi").First, Is.False);
        }

        [Test]
        public void ClaimIsOnceOnlyAndRequiresCompleteSet()
        {
            var denied = CollectionsLogic.ClaimSet(new JObject(), "fish", NowMs);
            Assert.That(denied.Ok, Is.False);
            var c = FullSetSlice("fish");
            var res = CollectionsLogic.ClaimSet(c, "fish", NowMs);
            Assert.That(res.Ok, Is.True);
            Assert.That(J.Long(res.C["claimedSets"]["fish"]), Is.EqualTo(NowMs));
            Assert.That(res.Reward.Coins, Is.EqualTo(200));
            Assert.That(((JObject)c["claimedSets"]).Count, Is.EqualTo(0), "pure");
            Assert.That(CollectionsLogic.ClaimSet(res.C, "fish", NowMs + 5).Ok, Is.False);
        }

        [Test]
        public void ApplyClaimBooksCoinsXpAndFurnitureExactlyOnce()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            store.Update(s => s["collections"] = FullSetSlice("fish"));
            var coinsBefore = J.Int(store.State["economy"]["coins"]);
            var reward = CollectionsLogic.ApplyClaim(store, "fish", NowMs, "2026-07-31");
            Assert.That(reward.Coins, Is.EqualTo(200));
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(coinsBefore + 200));
            Assert.That(J.Num(store.State["progression"]["xp"]), Is.EqualTo(50.0));
            Assert.That(J.Long(store.State["collections"]["claimedSets"]["fish"]), Is.EqualTo(NowMs));
            Assert.That(StorageLogic.CountOf((JArray)store.State["home"]["storage"], "proc:goldfishBowl"), Is.EqualTo(1));
            Assert.That(CollectionsLogic.ApplyClaim(store, "fish", NowMs + 99, "2026-07-31"), Is.Null);
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(coinsBefore + 200));
            Assert.That(StorageLogic.CountOf((JArray)store.State["home"]["storage"], "proc:goldfishBowl"), Is.EqualTo(1));
        }

        [Test]
        public void CropAndFoodIdMappingAndReports()
        {
            Assert.That(CollectionsLogic.VeggieEntryForCrop("tomate"), Is.EqualTo("tomato"));
            Assert.That(CollectionsLogic.VeggieEntryForCrop("carrot"), Is.EqualTo("carrot"));
            Assert.That(CollectionsLogic.VeggieEntryForCrop("chili"), Is.EqualTo(""));
            Assert.That(CollectionsLogic.TreatEntryForFood("cupcakePink"), Is.EqualTo("cupcake"));
            Assert.That(CollectionsLogic.TreatEntryForFood("carrot"), Is.EqualTo(""));
            var state = SaveSchema.DefaultState(NowMs);
            CollectionsLogic.AwardReport(state, JObject.Parse("{\"collections\":{\"fish\":[\"pinkKoi\",\"pinkKoi\"],\"landmarks\":[\"shop\",\"shop\"]}}"));
            Assert.That(CollectionsLogic.CountOf((JObject)state["collections"], "fish", "pinkKoi"), Is.EqualTo(2));
            Assert.That(CollectionsLogic.CountOf((JObject)state["collections"], "landmarks", "shop"), Is.EqualTo(1), "landmarks first-only");
        }

        [Test]
        public void FeedingTreatsFillsTheAlbumThroughTheCatalogHook()
        {
            FoodCatalog.TreatAwarder = (s, id) => CollectionsLogic.AwardInState(s, "treats", CollectionsLogic.TreatEntryForFood(id));
            try
            {
                var state = SaveSchema.DefaultState(NowMs);
                ((JObject)state["gooby"]["stats"])["hunger"] = 10;
                Assert.That(FoodCatalog.ApplyFeed(state, "cupcake"), Is.Not.Null);
                Assert.That(CollectionsLogic.CountOf((JObject)state["collections"], "treats", "cupcake"), Is.EqualTo(1));
            }
            finally { FoodCatalog.TreatAwarder = null; }
        }

        [Test]
        public void EverySetRewardIsAPieceOfDecoOnlyTheAlbumGives()
        {
            Fixtures.InstallContentSource();
            FurnitureCatalog.ResetCache();
            foreach (var def in CollectionsLogic.Sets)
            {
                var piece = FurnitureCatalog.Def(def.Furniture);
                Assert.That(piece, Is.Not.Null, def.Furniture + " is in the furniture catalog (placeable from the storage)");
                Assert.That(piece.Preis, Is.EqualTo(0), def.Furniture + " is not sold in the shop");
                Assert.That(piece.Kategorie, Is.EqualTo("deko"));
                Assert.That(piece.Verkaufswert, Is.GreaterThan(0), "it can still be sold back at the market");
            }
        }
    }
}
