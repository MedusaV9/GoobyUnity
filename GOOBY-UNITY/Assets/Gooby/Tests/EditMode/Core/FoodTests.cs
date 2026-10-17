using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of the EF-1 feeding tests (test_ef1_fuettern.gd) against the catalog + pure feed transform.</summary>
    public class FoodTests
    {
        private const long Now = 1_768_478_400_000L;

        [Test]
        public void CatalogHasTheWebParitySetAndCategories()
        {
            Assert.That(FoodCatalog.Foods.Count, Is.EqualTo(44));
            Assert.That(FoodCatalog.Def("pizza").Hunger, Is.EqualTo(45));
            Assert.That(FoodCatalog.Def("pizza").Junk, Is.True);
            Assert.That(FoodCatalog.Def("nutella").Hygiene, Is.EqualTo(-4));
            Assert.That(FoodCatalog.IsFavorite("carrot"), Is.True);
            Assert.That(FoodCatalog.IsFavorite("weltraumMoehre"), Is.True);
            Assert.That(FoodCatalog.Kategorie("burger"), Is.EqualTo("warm"));
            Assert.That(FoodCatalog.Kategorie("lollypop"), Is.EqualTo("suesses"));
            Assert.That(FoodCatalog.Kategorie("weltraumMoehre"), Is.EqualTo("gemuese"));
            Assert.That(FoodCatalog.Kategorie("does-not-exist"), Is.EqualTo("gemuese"), "unknown → fallback snack → vegetables");
            Assert.That(FoodCatalog.Def("does-not-exist").Hunger, Is.EqualTo(10));
            foreach (var id in FoodCatalog.Foods.Keys)
                Assert.That(FoodCatalog.FoodKategorie.ContainsKey(id), Is.True, id + " has a shelf category");
        }

        [Test]
        public void FeedingConsumesStockAppliesDeltasAndCounters()
        {
            var s = SaveSchema.DefaultState(Now);
            var gooby = (JObject)s["gooby"];
            gooby["stats"]["hunger"] = 50;
            gooby["stats"]["fun"] = 50;
            var r = FoodCatalog.ApplyFeed(s, "carrot");
            Assert.That(r, Is.Not.Null);
            Assert.That(r.Id, Is.EqualTo("carrot"));
            Assert.That(r.Favorit, Is.True);
            Assert.That(r.Junk, Is.False);
            Assert.That(r.HungerGain, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(J.Num(gooby["stats"]["hunger"]), Is.EqualTo(60.0));
            Assert.That(J.Num(gooby["stats"]["fun"]), Is.EqualTo(52.0));
            Assert.That(J.Int(s["inventory"]["food"]["carrot"]), Is.EqualTo(2));
            Assert.That(J.Int(s["achievements"]["counters"]["feeds"]), Is.EqualTo(1));
            Assert.That(J.Num(gooby["weight"]), Is.EqualTo(50.5).Within(1e-9), "healthy food +0.5 weight");
            Assert.That(J.Num(gooby["health"]["junkScore"]), Is.EqualTo(0.0), "healthy food floors junkScore at 0");
        }

        [Test]
        public void JunkFeedsWeightAndJunkScoreAndLastPortionIsRemoved()
        {
            var s = SaveSchema.DefaultState(Now);
            var gooby = (JObject)s["gooby"];
            gooby["stats"]["hunger"] = 20;
            var r = FoodCatalog.ApplyFeed(s, "cupcake");
            Assert.That(r.Junk, Is.True);
            Assert.That(s["inventory"]["food"]["cupcake"], Is.Null, "last portion removed from the fridge");
            Assert.That(J.Num(gooby["weight"]), Is.EqualTo(52.0).Within(1e-9));
            Assert.That(J.Num(gooby["health"]["junkScore"]), Is.EqualTo(1.0));
            Assert.That(J.Num(gooby["stats"]["hygiene"]), Is.EqualTo(84.0));
            Assert.That(FoodCatalog.ApplyFeed(s, "cupcake"), Is.Null, "no stock left");
        }

        [Test]
        public void FullGoobyRefusesAndUnknownIdsNeverCrash()
        {
            var s = SaveSchema.DefaultState(Now);
            ((JObject)s["gooby"]["stats"])["hunger"] = 99.6;
            Assert.That(FoodCatalog.TooFull(s), Is.True);
            Assert.That(FoodCatalog.ApplyFeed(s, "carrot"), Is.Null);
            Assert.That(J.Int(s["inventory"]["food"]["carrot"]), Is.EqualTo(3), "nothing consumed when refusing");
            ((JObject)s["gooby"]["stats"])["hunger"] = 10;
            ((JObject)s["inventory"]["food"])["mystery-meat"] = 1;
            var r = FoodCatalog.ApplyFeed(s, "mystery-meat");
            Assert.That(r, Is.Not.Null);
            Assert.That(r.HungerGain, Is.EqualTo(10.0).Within(1e-9), "fallback snack");
        }

        [Test]
        public void InventoryEntriesAreSortedMostFirstThenAlphabetical()
        {
            var s = SaveSchema.DefaultState(Now);
            var food = (JObject)s["inventory"]["food"];
            food["banana"] = 3;
            food["zucchini"] = 0;
            var entries = FoodCatalog.InventoryEntries(s);
            Assert.That(entries.Count, Is.EqualTo(4));
            Assert.That(entries[0].Id, Is.EqualTo("banana"));
            Assert.That(entries[1].Id, Is.EqualTo("carrot"));
            Assert.That(entries[2].Id, Is.EqualTo("apple"));
            Assert.That(entries[3].Id, Is.EqualTo("cupcake"));
        }
    }
}
