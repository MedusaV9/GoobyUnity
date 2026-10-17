using Gooby.Core.Cosmetics;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_cosmetics_state.gd + content-pack integrity.</summary>
    public class CosmeticsTests
    {
        private const long NowMs = 1_768_478_400_000L;

        private static readonly JArray Katalog = JArray.Parse(@"[
            {""id"":""hut_gratis"",""kategorie"":""hut"",""name_de"":""Gratishut"",""desc_de"":""Kostet nix."",""build"":""kegel"",""standard"":true},
            {""id"":""hut_teuer"",""kategorie"":""hut"",""name_de"":""Teuer"",""preis"":500,""build"":""zylinder""},
            {""id"":""hut_billig"",""kategorie"":""hut"",""name_de"":""Billig"",""preis"":50,""build"":""kappe""},
            {""id"":""hut_spaet"",""kategorie"":""hut"",""name_de"":""Spät"",""preis"":10,""min_level"":7,""build"":""krone""},
            {""id"":""brille_a"",""kategorie"":""brille"",""name_de"":""Brille A"",""preis"":20,""build"":""brille""},
            {""id"":""hals_a"",""kategorie"":""hals"",""name_de"":""Hals A"",""preis"":20,""build"":""schal""},
            {""id"":""ruecken_a"",""kategorie"":""ruecken"",""name_de"":""Rücken A"",""preis"":20,""build"":""rucksack""},
            {""id"":""cream"",""kategorie"":""fell"",""name_de"":""Creme"",""farben"":[""#a"",""#b"",""#c""],""build"":""fell"",""standard"":true},
            {""id"":""fell_pink"",""kategorie"":""fell"",""name_de"":""Pink"",""preis"":120,""farben"":[""#a"",""#b"",""#c""],""build"":""fell""}]");

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            CosmeticsCatalog.SetItems(Katalog);
        }

        [TearDown]
        public void Teardown() => CosmeticsCatalog.ResetCache();

        [Test]
        public void ShippedPackIsValid()
        {
            CosmeticsCatalog.ResetCache();
            var all = CosmeticsCatalog.All();
            Assert.That(all.Count, Is.EqualTo(93));
            Assert.That(CosmeticsCatalog.Validate(Gooby.Core.Content.ContentSource.LoadItems(CosmeticsCatalog.DataPath)), Is.Empty);
            Assert.That(CosmeticsCatalog.ByKategorie("fell").Count, Is.EqualTo(14));
            Assert.That(CosmeticsCatalog.StandardIds("fell"), Does.Contain("cream"));
        }

        [Test]
        public void OneItemPerSlotDisplacesTheOld()
        {
            var slice = CosmeticsState.DefaultSlice();
            CosmeticsState.Grant(slice, "hut_teuer");
            CosmeticsState.Grant(slice, "hut_billig");
            CosmeticsState.Equip(slice, "hut_teuer");
            Assert.That(CosmeticsState.Equipped(slice, "hut"), Is.EqualTo("hut_teuer"));
            var zweiter = CosmeticsState.Equip(slice, "hut_billig");
            Assert.That(zweiter.Ok, Is.True);
            Assert.That(zweiter.Vorher, Is.EqualTo("hut_teuer"));
            Assert.That(CosmeticsState.Equipped(slice, "hut"), Is.EqualTo("hut_billig"));
            Assert.That(CosmeticsState.IsOwned(slice, "hut_teuer"), Is.True);
        }

        [Test]
        public void SlotsDoNotInterfere()
        {
            var slice = CosmeticsState.DefaultSlice();
            foreach (var id in new[] { "hut_billig", "brille_a", "hals_a", "ruecken_a" }) { CosmeticsState.Grant(slice, id); CosmeticsState.Equip(slice, id); }
            Assert.That(CosmeticsState.SlotsFilled(slice), Is.EqualTo(4));
            var karte = CosmeticsState.EquippedMap(slice);
            Assert.That(karte["hut"], Is.EqualTo("hut_billig"));
            Assert.That(karte["brille"], Is.EqualTo("brille_a"));
            Assert.That(karte["fell"], Is.EqualTo("cream"));
            CosmeticsState.Unequip(slice, "brille");
            Assert.That(CosmeticsState.Equipped(slice, "brille"), Is.EqualTo(""));
            Assert.That(CosmeticsState.SlotsFilled(slice), Is.EqualTo(3));
            Assert.That(CosmeticsState.Toggle(slice, "hut_billig").Ok, Is.True, "toggle off");
            Assert.That(CosmeticsState.Equipped(slice, "hut"), Is.EqualTo(""));
            Assert.That(CosmeticsState.Toggle(slice, "hut_billig").Ok, Is.True, "toggle on");
            Assert.That(CosmeticsState.Equipped(slice, "hut"), Is.EqualTo("hut_billig"));
        }

        [Test]
        public void FurIsNeverEmptyAndShopOnly()
        {
            var slice = CosmeticsState.DefaultSlice();
            var belohnung = CosmeticsState.Grant(slice, "fell_pink", "quest");
            Assert.That(belohnung.Ok, Is.False);
            Assert.That(belohnung.Grund, Is.EqualTo("nur_im_shop"));
            Assert.That(CosmeticsState.Grant(slice, "hut_teuer", "quest").Ok, Is.True);
            Assert.That(CosmeticsState.Grant(slice, "fell_pink", "shop").Ok, Is.True);
            CosmeticsState.Equip(slice, "fell_pink");
            Assert.That(CosmeticsState.Equipped(slice, "fell"), Is.EqualTo("fell_pink"));
            Assert.That(CosmeticsState.SlotsFilled(slice), Is.EqualTo(0));
            Assert.That(CosmeticsState.Unequip(slice, "fell").Ok, Is.True);
            Assert.That(CosmeticsState.Equipped(slice, "fell"), Is.EqualTo("cream"));
            Assert.That(CosmeticsState.Unequip(slice, "fell").Ok, Is.False);
        }

        [Test]
        public void BuyBooksCoinsAndOwnershipTogetherAndFailsCleanly()
        {
            var slice = CosmeticsState.DefaultSlice();
            var econ = Economy.DefaultSlice();
            econ["coins"] = 200;
            var kauf = CosmeticsState.Buy(slice, econ, "hut_billig");
            Assert.That(kauf.Gekauft, Is.True);
            Assert.That(J.Int(econ["coins"]), Is.EqualTo(150));
            Assert.That(J.Int(econ["coinsSpent"]), Is.EqualTo(50));
            Assert.That(CosmeticsState.IsOwned(slice, "hut_billig"), Is.True);
            econ["coins"] = 100;
            var zuTeuer = CosmeticsState.Buy(slice, econ, "hut_teuer");
            Assert.That(zuTeuer.Gekauft, Is.False);
            Assert.That(zuTeuer.Grund, Is.EqualTo("zu_teuer"));
            Assert.That(J.Int(econ["coins"]), Is.EqualTo(100));
            var zuFrueh = CosmeticsState.Buy(slice, econ, "hut_spaet", 3);
            Assert.That(zuFrueh.Grund, Is.EqualTo("level_zu_niedrig"));
            Assert.That(CosmeticsState.Buy(slice, econ, "hut_spaet", 7).Gekauft, Is.True);
            Assert.That(CosmeticsState.Buy(slice, econ, "hut_spaet", 7).Grund, Is.EqualTo("schon_besessen"));
            Assert.That(CosmeticsState.CanBuy(slice, econ, "hut_gratis").Grund, Is.EqualTo("schon_besessen"));
        }

        [Test]
        public void UnknownIdsAreIgnoredSoftlyAndPruneCleansUp()
        {
            var slice = CosmeticsState.DefaultSlice();
            CosmeticsState.Grant(slice, "hut_billig");
            CosmeticsState.Equip(slice, "hut_billig");
            var vorher = slice.ToString(Newtonsoft.Json.Formatting.None);
            foreach (var id in new[] { "gibt_es_nicht", "", "hut_billig/../hack" })
            {
                Assert.That(CosmeticsState.Equip(slice, id).Ok, Is.False);
                Assert.That(CosmeticsState.Grant(slice, id).Ok, Is.False);
                Assert.That(CosmeticsState.IsOwned(slice, id), Is.False);
            }
            Assert.That(CosmeticsState.Equip(slice, "hut_teuer").Grund, Is.EqualTo("nicht_besessen"));
            Assert.That(CosmeticsState.Unequip(slice, "hosentraeger").Ok, Is.False);
            Assert.That(slice.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(vorher));
            slice["outfits"]["equipped"]["hat"] = "vom_alten_pack";
            slice["fur"]["equipped"] = "fell_weg";
            var geraeumt = CosmeticsState.PruneEquipped(slice);
            Assert.That(geraeumt, Is.EquivalentTo(new[] { "hut", "fell" }));
            Assert.That(CosmeticsState.Equipped(slice, "fell"), Is.EqualTo("cream"));
        }

        [Test]
        public void ApplyToStateWritesIntoTheRealStore()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            var changed = 0;
            store.SliceChanged += (id, _) => { if (id == "cosmetics") changed++; };
            var kauf = CosmeticsState.ApplyToState(store, (c, e) => CosmeticsState.Buy(c, e, "hut_billig"));
            Assert.That(kauf.Gekauft, Is.True);
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(50));
            Assert.That(CosmeticsState.IsOwned((JObject)store.State["cosmetics"], "hut_billig"), Is.True);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(CosmeticsState.ApplyToState(store, (c, e) => CosmeticsState.Equip(c, "hut_billig")).Ok, Is.True);
            Assert.That(J.Str(store.State["cosmetics"]["outfits"]["equipped"]["hat"]), Is.EqualTo("hut_billig"));
        }
    }
}
