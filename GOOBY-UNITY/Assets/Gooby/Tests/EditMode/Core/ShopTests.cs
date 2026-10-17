using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    public class ShopTests
    {
        private const long NowMs = 1_768_478_400_000L;

        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            return store;
        }

        [Test]
        public void RehweiAssortmentMatchesTheFoodCatalog()
        {
            var waren = ShopLogic.Waren();
            Assert.That(waren.Count, Is.EqualTo(31));
            foreach (var w in waren)
            {
                Assert.That(FoodCatalog.Foods.ContainsKey(w.Id), Is.True, w.Id + " is a catalog food");
                Assert.That(w.Preis, Is.GreaterThan(0), w.Id);
                Assert.That(w.IsFood, Is.True, w.Id);
            }
            Assert.That(ShopLogic.Saatgut().Count, Is.EqualTo(4));
            Assert.That(ShopLogic.Buecher().Count, Is.EqualTo(5));
            Assert.That(ShopLogic.GooberandoGerichte().Count, Is.GreaterThan(0));
        }

        [Test]
        public void BuyingSpendsCoinsAndFillsTheFridgeOrItems()
        {
            var gs = Store();
            var carrot = ShopLogic.Find(ShopLogic.Waren(), "carrot");
            var coins = ShopLogic.Coins(gs);
            var before = J.Int(gs.State["inventory"]["food"]["carrot"]);
            Assert.That(ShopLogic.Kaufe(gs, carrot), Is.True);
            Assert.That(ShopLogic.Coins(gs), Is.EqualTo(coins - carrot.Preis));
            Assert.That(J.Int(gs.State["inventory"]["food"]["carrot"]), Is.EqualTo(before + 1));
            Assert.That(J.Int(gs.State["economy"]["coinsSpent"]), Is.EqualTo(carrot.Preis));
            var samen = ShopLogic.Saatgut()[0];
            Assert.That(ShopLogic.Kaufe(gs, samen), Is.True);
            Assert.That(J.Int(gs.State["inventory"]["items"][samen.Inventar]), Is.EqualTo(1));
        }

        [Test]
        public void SeedsFromTheShopUnlockTheirCropOnABed()
        {
            var gs = Store();
            var bed = new Gooby.Core.Home.Cell(0, 0);
            Assert.That(Gooby.Core.Garden.GardenState.SamenCount(gs, "radish"), Is.EqualTo(0), "no radish seeds yet");
            Assert.That(Gooby.Core.Garden.GardenState.Pflanzen(gs, bed, "radish"), Is.False, "seed crops need seeds");
            Assert.That(Gooby.Core.Garden.GardenState.Pflanzen(gs, bed, "salat"), Is.True, "free crops plant without seeds");
            var radish = ShopLogic.Find(ShopLogic.Saatgut(), "samen_radish");
            Assert.That(radish, Is.Not.Null);
            Assert.That(ShopLogic.Kaufe(gs, radish), Is.True);
            Assert.That(Gooby.Core.Garden.GardenState.SamenCount(gs, "radish"), Is.EqualTo(1), "shop seeds count for the bed sheet");
            var other = new Gooby.Core.Home.Cell(1, 0);
            Assert.That(Gooby.Core.Garden.GardenState.Pflanzen(gs, other, "radish"), Is.True);
            Assert.That(Gooby.Core.Garden.GardenState.SamenCount(gs, "radish"), Is.EqualTo(0), "planting consumes the seed");
            Assert.That(J.Str(Gooby.Core.Garden.GardenState.Grid(gs).Cell(other)["crop"]), Is.EqualTo("radish"));
        }

        [Test]
        public void TooExpensiveRecipeGatedAndBooksOnlyOnce()
        {
            var gs = Store();
            var pizza = ShopLogic.Find(ShopLogic.Waren(), "pizza");
            gs.Update(s => s["economy"]["coins"] = pizza.Preis - 1);
            Assert.That(ShopLogic.KannKaufen(gs, pizza), Is.False);
            Assert.That(ShopLogic.Kaufe(gs, pizza), Is.False);
            gs.Update(s => s["economy"]["coins"] = 1000);
            var rezept = Ware.From(JObject.Parse("{\"id\":\"tropfen\",\"preis\":10,\"inventar\":\"tropfen\",\"braucht_rezept\":true}"));
            Assert.That(ShopLogic.KannKaufen(gs, rezept), Is.False, "needs the prescription flag");
            gs.Update(s => J.EnsureDict(J.EnsureDict(s, "city"), "flags")[ShopLogic.FlagRezept] = true);
            Assert.That(ShopLogic.Kaufe(gs, rezept), Is.True);
            Assert.That(ShopLogic.HatRezept(gs), Is.False, "prescription consumed");
            var buch = ShopLogic.Buecher()[0];
            Assert.That(ShopLogic.Kaufe(gs, buch), Is.True);
            Assert.That(ShopLogic.ImRegal(gs, buch), Is.True);
            Assert.That(ShopLogic.Kaufe(gs, buch), Is.False, "books are bought once");
        }
    }
}
