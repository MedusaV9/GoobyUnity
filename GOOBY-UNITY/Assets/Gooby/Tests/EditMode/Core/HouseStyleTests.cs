using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// "Gestalten" for rooms (port of test_haus_custom.gd, room part): the catalogue, buying an option once, putting it on
    /// a room's wall or floor in one of its colours, going back to the built look, and the pattern pixels.
    /// </summary>
    public class HouseStyleTests
    {
        private const long NowMs = 1_791_158_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            HouseStyle.ResetCache();
        }

        private static GameStateStore Store(int coins)
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            store.Update(s => J.EnsureDict(s, "economy")["coins"] = coins);
            return store;
        }

        [Test]
        public void TheCatalogHasWallsAndFloorsAndFreeOnesAreOwned()
        {
            Assert.That(HouseStyle.OfArt(HouseStyle.Wand).Count(), Is.EqualTo(10));
            Assert.That(HouseStyle.OfArt(HouseStyle.Boden).Count(), Is.EqualTo(8));
            var gs = Store(0);
            Assert.That(HouseStyle.Owns(gs, HouseStyle.Wand, "uni"), Is.True);
            Assert.That(HouseStyle.Owns(gs, HouseStyle.Boden, "dielen_hell"), Is.True);
            Assert.That(HouseStyle.Owns(gs, HouseStyle.Wand, "gebluemt"), Is.False);
            Assert.That(HouseStyle.Room(gs, "living"), Is.Null, "a room keeps its built look until styled");
        }

        [Test]
        public void BuyingOnceThenStylingAnyRoom()
        {
            var gs = Store(150);
            Assert.That(HouseStyle.Buy(gs, HouseStyle.Wand, "gebluemt"), Is.EqualTo(HouseStyle.ResultBroke), "180 coins");
            Assert.That(HouseStyle.Buy(gs, HouseStyle.Wand, "punkte"), Is.EqualTo(HouseStyle.ResultOk));
            Assert.That(J.Int(J.Path(gs.State, "economy", "coins")), Is.EqualTo(70));
            Assert.That(HouseStyle.Buy(gs, HouseStyle.Wand, "punkte"), Is.EqualTo(HouseStyle.ResultOwned));
            Assert.That(HouseStyle.Buy(gs, HouseStyle.Wand, "nope"), Is.EqualTo(HouseStyle.ResultUnknown));

            Assert.That(HouseStyle.Apply(gs, "living", HouseStyle.Wand, "gebluemt", "rose"), Is.False, "not owned");
            Assert.That(HouseStyle.Apply(gs, "living", HouseStyle.Wand, "punkte", "walnuss"), Is.False, "not one of its colours");
            Assert.That(HouseStyle.Apply(gs, "living", HouseStyle.Wand, "punkte", "mint"), Is.True);
            Assert.That(HouseStyle.Apply(gs, "living", HouseStyle.Boden, "dielen_hell", "eiche_hell"), Is.True);
            var style = HouseStyle.Room(gs, "living");
            Assert.That(style.Wand + "/" + style.WandFarbe + "/" + style.Boden + "/" + style.BodenFarbe, Is.EqualTo("punkte/mint/dielen_hell/eiche_hell"));
            Assert.That(HouseStyle.Apply(gs, "kitchen", HouseStyle.Wand, "punkte", "rose"), Is.True, "bought once, usable everywhere");
            HouseStyle.Reset(gs, "living");
            Assert.That(HouseStyle.Room(gs, "living"), Is.Null);
            Assert.That(HouseStyle.Room(gs, "kitchen"), Is.Not.Null);
        }

        [Test]
        public void TheCottageTakesItsColoursFromTheStyleCatalog()
        {
            var gs = Store(0);
            Assert.That(HouseStyle.Haus(gs), Is.Null, "built look until painted");
            Assert.That(HouseStyle.FarbenFuer("fassade"), Does.Contain("himmel"));
            Assert.That(HouseStyle.SetHaus(gs, "fassade", "himmel"), Is.True);
            Assert.That(HouseStyle.SetHaus(gs, "dachFarbe", "himmel"), Is.False, "roofs do not come in sky blue");
            Assert.That(HouseStyle.SetHaus(gs, "kamin", "rose"), Is.False);
            Assert.That(HouseStyle.SetHaus(gs, "tuerFarbe", "teal"), Is.True);
            var haus = HouseStyle.Haus(gs);
            Assert.That(haus["fassade"] + "/" + haus["tuerFarbe"], Is.EqualTo("himmel/teal"));
            HouseStyle.ResetHaus(gs);
            Assert.That(HouseStyle.Haus(gs), Is.Null);
        }

        [Test]
        public void PatternsAreDeterministicAndTinted()
        {
            foreach (var o in HouseStyle.Options())
                for (var i = 0; i < 64; i++)
                {
                    var v = HouseStyle.Wert(o.Muster, (i * 37) % 128, (i * 53) % 128);
                    Assert.That(v, Is.InRange(0.0, 1.0), o.Muster);
                    Assert.That(HouseStyle.Wert(o.Muster, (i * 37) % 128, (i * 53) % 128), Is.EqualTo(v), "deterministic");
                }
            Assert.That(HouseStyle.Wert("streifen", 0, 5), Is.Not.EqualTo(HouseStyle.Wert("streifen", 16, 5)), "stripes alternate");
            var bright = HouseStyle.Blend(HouseStyle.ReferenceLuminance, "himmel");
            Assert.That(bright.r, Is.EqualTo(0xCF / 255.0).Within(1e-6), "the reference grey shows the palette colour itself");
            var dark = HouseStyle.Blend(0.5, "himmel");
            Assert.That(dark.b, Is.LessThan(bright.b));
        }
    
        [Test]
        public void OutsidePartsComeFromTheHouseStyles()
        {
            Assert.That(HouseStyle.TeilOptionen(HouseStyle.Hausnummer).Select(o => o.Id), Is.EqualTo(new[] { "holz", "emaille", "modern" }));
            Assert.That(HouseStyle.TeilOptionen(HouseStyle.Briefkasten).Count, Is.EqualTo(4));
            Assert.That(HouseStyle.TeilOptionen(HouseStyle.Vordach).First().Id, Is.EqualTo("keins"));
            Assert.That(HouseStyle.TeilOptionen(HouseStyle.Zaun).Select(o => o.Id), Does.Contain("hecke").And.Contain("metall"));
            Assert.That(HouseStyle.TeilOptionen(HouseStyle.Weg).Count, Is.EqualTo(5));
            Assert.That(HouseStyle.TeilOptionen(HouseStyle.Grund).Count, Is.EqualTo(7));
            var gs = Store(0);
            Assert.That(HouseStyle.TeilWahl(gs, HouseStyle.Hausnummer), Is.EqualTo(("holz", "")), "the default wooden plaque");
            Assert.That(HouseStyle.TeilWahl(gs, HouseStyle.Briefkasten), Is.EqualTo(("standard", "ziegelrot")));
            Assert.That(HouseStyle.TeilWahl(gs, HouseStyle.Vordach).Item1, Is.EqualTo("keins"));
            Assert.That(HouseStyle.HausnummerZahl(gs), Is.EqualTo(5));
            Assert.That(HouseStyle.TeilGewaehlt(gs, HouseStyle.Zaun), Is.False, "the built fence until chosen");
        }

        [Test]
        public void BuyingAndPuttingUpOutsideParts()
        {
            var gs = Store(500);
            Assert.That(HouseStyle.OwnsTeil(gs, HouseStyle.Zaun, "latten"), Is.True, "free");
            Assert.That(HouseStyle.SetTeil(gs, HouseStyle.Zaun, "latten", "teal"), Is.True);
            Assert.That(HouseStyle.TeilGewaehlt(gs, HouseStyle.Zaun), Is.True);
            Assert.That(HouseStyle.TeilWahl(gs, HouseStyle.Zaun), Is.EqualTo(("latten", "teal")));
            Assert.That(HouseStyle.SetTeil(gs, HouseStyle.Zaun, "metall", "anthrazit"), Is.False, "not bought yet");
            Assert.That(HouseStyle.BuyTeil(gs, HouseStyle.Zaun, "metall"), Is.EqualTo(HouseStyle.ResultOk));
            Assert.That(J.Int(gs.State["economy"]["coins"]), Is.EqualTo(220));
            Assert.That(HouseStyle.BuyTeil(gs, HouseStyle.Zaun, "metall"), Is.EqualTo(HouseStyle.ResultOwned));
            Assert.That(HouseStyle.SetTeil(gs, HouseStyle.Zaun, "metall", "rose"), Is.False, "a colour the fence does not come in");
            Assert.That(HouseStyle.SetTeil(gs, HouseStyle.Zaun, "metall", "anthrazit"), Is.True);
            Assert.That(HouseStyle.BuyTeil(gs, HouseStyle.Vordach, "markise_gestreift"), Is.EqualTo(HouseStyle.ResultBroke));
            Assert.That(HouseStyle.SetTeil(gs, HouseStyle.Hausnummer, "holz", null), Is.True, "a part without colours");
            HouseStyle.SetHausnummer(gs, 123);
            Assert.That(HouseStyle.HausnummerZahl(gs), Is.EqualTo(99));
            HouseStyle.SetHausnummer(gs, 12);
            Assert.That(HouseStyle.HausnummerZahl(gs), Is.EqualTo(12));
            HouseStyle.ResetHaus(gs);
            Assert.That(HouseStyle.TeilGewaehlt(gs, HouseStyle.Zaun), Is.False, "reset brings the built look back");
            Assert.That(HouseStyle.OwnsTeil(gs, HouseStyle.Zaun, "metall"), Is.True, "bought stays bought");
        }
    }
}
