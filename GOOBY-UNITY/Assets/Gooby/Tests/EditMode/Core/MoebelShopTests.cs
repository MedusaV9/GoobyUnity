using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The GOOBEA furniture store (ports of test_shop_catalog.gd / test_shop_purchase.gd / test_furniture_variants.gd):
    /// what is for sale, filters and order, buying into the storage in a colour (coins and storage in one step), the
    /// palette's brightness-keeping recolour, storage handing out any colour, and placed pieces keeping their colour
    /// through the save.
    /// </summary>
    public class MoebelShopTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            FurnitureCatalog.ResetCache();
        }

        private static JObject State(int coins) => new JObject
        {
            ["economy"] = new JObject { ["coins"] = coins },
            ["home"] = new JObject { ["storage"] = new JArray(), ["storageCapacity"] = 100 },
        };

        [Test]
        public void OnlyPricedPiecesAreForSaleCheapestFirst()
        {
            var alle = MoebelShop.Filter("");
            Assert.That(alle.Count, Is.GreaterThan(150));
            Assert.That(alle.All(d => d.Preis > 0), Is.True, "price 0 = workshop only");
            for (var i = 1; i < alle.Count; i++) Assert.That(alle[i].Preis, Is.GreaterThanOrEqualTo(alle[i - 1].Preis));
            var cats = MoebelShop.Categories();
            Assert.That(cats, Does.Contain("kueche").And.Contain("sitzen"));
            Assert.That(cats, Is.Ordered);
            Assert.That(MoebelShop.Filter("", "bad").All(d => d.Kategorie == "bad"), Is.True);
            Assert.That(MoebelShop.Filter("nougat").Select(d => d.Id), Does.Contain("nougatschleuse"), "search by name");
            Assert.That(MoebelShop.Filter("Nougat Chute").Count, Is.EqualTo(1), "search by the English name");
            Assert.That(MoebelShop.Filter("gibtsnichtxyz"), Is.Empty);
        }

        [Test]
        public void BuyingPaysAndStoresTheColour()
        {
            var s = State(1000);
            Assert.That(MoebelShop.Kaufe(s, "nougatschleuse", "rose"), Is.EqualTo(MoebelShop.Ok));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(600));
            Assert.That(((JArray)s["home"]["storage"]).Count, Is.EqualTo(0), "the van brings it (delivery_cutscene.gd)");
            Assert.That(Lieferung.Offene(s), Is.EqualTo(new[] { ("nougatschleuse", "rose", 1) }));
            Assert.That(MoebelShop.Kaufe(s, "nougatschleuse", "gibtsnicht"), Is.EqualTo(MoebelShop.Ok), "an unknown colour falls back");
            Assert.That(Lieferung.Stueck(s), Is.EqualTo(2));
            Assert.That(Lieferung.Einlagern(s), Is.EqualTo(2));
            Assert.That(Lieferung.Offene(s), Is.Empty);
            var row = (JObject)((JArray)s["home"]["storage"])[0];
            Assert.That(J.Str(row["item"]), Is.EqualTo("nougatschleuse"));
            Assert.That(J.Str(row["variant"]), Is.EqualTo("rose"));
            Assert.That(((JArray)s["home"]["storage"]).Any(r => J.Str(r["variant"]) == "default"), Is.True, "natur is stored as the plain piece");
            Assert.That(Lieferung.Einlagern(s), Is.EqualTo(0), "nothing twice");
            Assert.That(MoebelShop.Kaufe(State(10), "nougatschleuse"), Is.EqualTo(MoebelShop.Pleite));
            Assert.That(MoebelShop.Kaufe(s, "gibtsnicht"), Is.EqualTo(MoebelShop.Unbekannt));
            var voll = State(100000);
            voll["home"]["storageCapacity"] = 1;
            Assert.That(MoebelShop.Kaufe(voll, "nougatschleuse"), Is.EqualTo(MoebelShop.Voll));
            Assert.That(J.Int(voll["economy"]["coins"]), Is.EqualTo(100000), "a full storage books nothing");
            var knapp = State(100000);
            knapp["home"]["storageCapacity"] = StorageLogic.ItemWeight("nougatschleuse", HomeState.DefsAsJson());
            Assert.That(MoebelShop.Kaufe(knapp, "nougatschleuse"), Is.EqualTo(MoebelShop.Ok));
            Assert.That(MoebelShop.Kaufe(knapp, "nougatschleuse"), Is.EqualTo(MoebelShop.Voll), "what the van brings counts against the room");
        }

        [Test]
        public void VariantsKeepTheBrightness()
        {
            var def = FurnitureCatalog.Def("nougatschleuse");
            var ids = FurnitureVariants.IdsFor(def);
            Assert.That(ids[0], Is.EqualTo("natur"));
            Assert.That(ids.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(FurnitureVariants.Normalize(def, "gibtsnicht"), Is.EqualTo("natur"));
            Assert.That(FurnitureVariants.Blend(0.2, 0.4, 0.6, "natur"), Is.EqualTo((0.2, 0.4, 0.6)), "natur is the original");
            var (r, g, b) = FurnitureVariants.Blend(0.62, 0.62, 0.62, "rose");
            Assert.That(r, Is.GreaterThan(g), "rosé leans red");
            var dark = FurnitureVariants.Blend(0.1, 0.1, 0.1, "mint");
            var light = FurnitureVariants.Blend(0.9, 0.9, 0.9, "mint");
            Assert.That(light.g, Is.GreaterThan(dark.g), "a dark sofa stays darker than a light one");
            Assert.That(FurnitureVariants.IsNeutral("default"), Is.True);
            Assert.That(FurnitureVariants.IsNeutral("koralle"), Is.False);
        }

        [Test]
        public void StorageHandsOutAnyColourPlainFirst()
        {
            var storage = new JArray();
            StorageLogic.Add(storage, "chair", "mint");
            StorageLogic.Add(storage, "chair");
            Assert.That(StorageLogic.TakeAny(storage, "chair"), Is.EqualTo("default"), "the plain one first");
            Assert.That(StorageLogic.TakeAny(storage, "chair"), Is.EqualTo("mint"));
            Assert.That(StorageLogic.TakeAny(storage, "chair"), Is.Null);
            Assert.That(storage.Count, Is.EqualTo(0));
            var legacy = new JArray { new JObject { ["item"] = "chair", ["count"] = 1 } };
            Assert.That(StorageLogic.TakeAny(legacy, "chair"), Is.EqualTo("default"), "a row without a variant is the plain piece");
        }

        [Test]
        public void PlacedPiecesKeepTheirColourThroughTheSave()
        {
            var defs = FurnitureCatalog.Defs();
            var rows = new JArray
            {
                new JObject { ["uid"] = "a", ["item"] = "chair", ["at"] = new JArray(1, 1), ["rot"] = 0, ["variant"] = "lavendel" },
                new JObject { ["uid"] = "b", ["item"] = "chair", ["at"] = new JArray(3, 1), ["rot"] = 0 },
            };
            var grid = GridData.FromSave(rows, defs, 10, 10).Grid;
            Assert.That(grid.GetItem("a").Variant, Is.EqualTo("lavendel"));
            Assert.That(grid.GetItem("b").Variant, Is.Null);
            var back = grid.ToItemsArray();
            Assert.That(J.Str(back.First(r => J.Str(r["uid"]) == "a")["variant"]), Is.EqualTo("lavendel"));
            Assert.That(back.First(r => J.Str(r["uid"]) == "b")["variant"], Is.Null, "plain pieces save as before");
        }
    }
}
