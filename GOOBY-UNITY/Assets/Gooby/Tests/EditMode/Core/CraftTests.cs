using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Workshop (port of test_craft_logic.gd / test_w15_markt.gd craft parts): the recipe catalogue, the order of the
    /// refusal reasons, all-or-nothing consumption, Bodo's packs and blueprints reaching the workbench, crafting into
    /// storage, the herb box's weekly bundles and the stick/leaf spots on the bed field.
    /// </summary>
    public class CraftTests
    {
        private const long NowMs = 1_791_158_400_000L; // 2026-10-05 00:00 UTC, a Monday
        private const double NowS = 1_791_158_400.0;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
            GardenCrops.ResetCache();
            GardenWorld.ResetCache();
            CraftCatalog.ResetCache();
        }

        private static GameStateStore Store(int coins = 5000)
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            store.Update(s => J.EnsureDict(s, "economy")["coins"] = coins);
            return store;
        }

        private static void BuildWorkshop(GameStateStore gs) => Assert.That(GardenWorld.Bauen(gs, "werkstatt", new Cell(0, 0)).Ok, Is.True);

        [Test]
        public void TheCatalogueReadsRecipesAndMaterialsInFileOrder()
        {
            var recipes = CraftCatalog.ForStation();
            Assert.That(recipes.Select(r => r.Id), Is.EqualTo(new[]
            {
                "r_zaun_holz", "r_hocker_rustikal", "r_blumenkasten", "r_gartentisch", "r_regal_rustikal", "r_vogelhaus", "r_kraeuterkasten", "r_windrad_deko",
            }));
            var zaun = CraftCatalog.Recipe("r_zaun_holz");
            Assert.That(zaun.Item, Is.EqualTo("fence_wood"));
            Assert.That(zaun.Count, Is.EqualTo(4));
            Assert.That(zaun.Bauplan, Is.Empty, "a null blueprint means free");
            Assert.That(zaun.Materialien.Select(kv => kv.Key + ":" + kv.Value), Is.EqualTo(new[] { "stock:2", "holz:1", "naegel:4" }));
            Assert.That(CraftCatalog.Recipe("r_vogelhaus").Bauplan, Is.EqualTo("bp_vogelhaus"));
            Assert.That(CraftCatalog.Materials().Select(m => m.Id), Is.EqualTo(new[] { "stock", "blatt", "holz", "eisen", "naegel", "saatgut" }));
            Assert.That(CraftCatalog.Material("holz").Quelle, Is.EqualTo("baum-ernte"));
            Assert.That(CraftCatalog.Material("naegel").Quelle, Is.EqualTo("baumarkt"));
            foreach (var r in recipes) Assert.That(FurnitureCatalog.Def(r.Item), Is.Not.Null, r.Item + " is a furniture piece");
        }

        [Test]
        public void TheCheckRefusesInDocOrderAndConsumesAllOrNothing()
        {
            var recipe = CraftCatalog.Recipe("r_vogelhaus"); // holz 3, naegel 6, blueprint bp_vogelhaus
            var inv = new JObject { ["holz"] = 3, ["naegel"] = 2 };
            Assert.That(CraftLogic.Pruefen(recipe, inv, new string[0], false).Reason, Is.EqualTo(CraftLogic.ReasonStation));
            Assert.That(CraftLogic.Pruefen(recipe, inv, new string[0], true).Reason, Is.EqualTo(CraftLogic.ReasonBlueprint));
            var missing = CraftLogic.Pruefen(recipe, inv, new[] { "bp_vogelhaus" }, true);
            Assert.That(missing.Reason, Is.EqualTo(CraftLogic.ReasonMaterial));
            Assert.That(missing.Missing, Is.EquivalentTo(new[] { new System.Collections.Generic.KeyValuePair<string, int>("naegel", 4) }));
            Assert.That(CraftLogic.Consume(inv, recipe), Is.False);
            Assert.That(J.Int(inv["holz"]), Is.EqualTo(3), "a refused craft eats nothing");
            inv["naegel"] = 6;
            Assert.That(CraftLogic.Pruefen(recipe, inv, new[] { "bp_vogelhaus" }, true, 0).Reason, Is.EqualTo(CraftLogic.ReasonStorage));
            Assert.That(CraftLogic.Pruefen(recipe, inv, new[] { "bp_vogelhaus" }, true).Ok, Is.True);
            Assert.That(CraftLogic.Consume(inv, recipe), Is.True);
            Assert.That(inv.Properties().Count(), Is.EqualTo(0), "empty rows leave the inventory");
        }

        [Test]
        public void BodosPacksAndBlueprintsReachTheWorkbench()
        {
            var gs = Store();
            foreach (var id in new[] { "bretter", "naegel", "stoecke" }) Assert.That(ShopLogic.Kaufe(gs, ShopLogic.Find(ShopLogic.Materialien(), id)), Is.True, id);
            Assert.That(ShopLogic.Kaufe(gs, ShopLogic.Find(ShopLogic.Bauplaene(), "bauplan_vogelhaus")), Is.True);
            Assert.That(CraftState.HasBlueprint(gs, "bp_vogelhaus"), Is.True, "a bought blueprint unlocks bp_<werkstatt_id>");
            Assert.That(CraftState.HasBlueprint(gs, "bp_windrad_deko"), Is.False);
            Assert.That(CraftState.UebernehmeBaumarktEinkaeufe(gs), Is.EqualTo(5 + 20 + 10));
            Assert.That(CraftState.MaterialCount(gs, "holz"), Is.EqualTo(5), "boards are wood");
            Assert.That(CraftState.MaterialCount(gs, "stock"), Is.EqualTo(10));
            Assert.That(CraftState.MaterialCount(gs, "naegel"), Is.EqualTo(20));
            Assert.That(J.Path(gs.State, "inventory", "items", "bretter"), Is.Null, "the packs left inventory.items");
            Assert.That(J.Int(J.Path(gs.State, "inventory", "items", "bauplan_vogelhaus")), Is.EqualTo(1), "the blueprint flag stays (sold once)");
            Assert.That(CraftState.UebernehmeBaumarktEinkaeufe(gs), Is.EqualTo(0), "idempotent");
            Assert.That(CraftState.AddBlueprint(gs, "bp_gartentisch"), Is.True);
            Assert.That(CraftState.AddBlueprint(gs, "bp_gartentisch"), Is.False, "already known");
            Assert.That(CraftState.Blueprints(gs), Is.EquivalentTo(new[] { "bp_gartentisch", "bp_vogelhaus" }));
        }

        [Test]
        public void CraftingNeedsTheWorkshopAndPutsThePiecesIntoStorage()
        {
            var gs = Store();
            CraftState.AddMaterial(gs, "stock", 3);
            CraftState.AddMaterial(gs, "holz", 1);
            CraftState.AddMaterial(gs, "naegel", 4);
            Assert.That(CraftState.Craft(gs, "r_zaun_holz").reason, Is.EqualTo(CraftLogic.ReasonStation), "no workshop in the garden yet");
            BuildWorkshop(gs);
            Assert.That(CraftState.WerkstattGebaut(gs), Is.True);
            var before = StorageLogic.CountOf(HomeState.Storage(gs), "fence_wood");
            var res = CraftState.Craft(gs, "r_zaun_holz");
            Assert.That(res.ok, Is.True);
            Assert.That(res.item, Is.EqualTo("fence_wood"));
            Assert.That(res.count, Is.EqualTo(4));
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), "fence_wood"), Is.EqualTo(before + 4));
            Assert.That(CraftState.MaterialCount(gs, "stock"), Is.EqualTo(1));
            Assert.That(CraftState.MaterialCount(gs, "holz"), Is.EqualTo(0));
            Assert.That(J.Int(J.Path(gs.State, "achievements", "counters", "crafts")), Is.EqualTo(1));
            Assert.That(CraftState.Craft(gs, "r_zaun_holz").reason, Is.EqualTo(CraftLogic.ReasonMaterial), "the stock is used up");
            var states = CraftState.RecipeStates(gs);
            Assert.That(states.Count, Is.EqualTo(8));
            Assert.That(states.First(s => s.recipe.Id == "r_vogelhaus").check.Reason, Is.EqualTo(CraftLogic.ReasonBlueprint));
        }

        [Test]
        public void BaumarktCostsCountWholePacks()
        {
            var recipe = CraftCatalog.Recipe("r_zaun_holz"); // stock 2, holz 1, naegel 4
            // missing: 2 sticks (one 10-pack, 12), 1 wood (one 5-board pack, 30), 4 nails (one 20-pack, 18)
            Assert.That(CraftState.BaumarktKosten(recipe, new JObject()), Is.EqualTo(12 + 30 + 18));
            Assert.That(CraftState.BaumarktKosten(recipe, new JObject { ["stock"] = 2, ["holz"] = 1, ["naegel"] = 4 }), Is.EqualTo(0));
            Assert.That(CraftState.BaumarktKosten(CraftCatalog.Recipe("r_blumenkasten"), new JObject { ["holz"] = 2, ["naegel"] = 2 }), Is.EqualTo(0),
                "leaves are not sold at the Baumarkt");
        }

        [Test]
        public void TheHerbBoxGrowsOneBundleAWeekCappedAtTwo()
        {
            var gs = Store();
            var now = (long)NowS;
            Assert.That(KraeuterKasten.Schoepfe(gs, now), Is.EqualTo(0), "no box, no herbs");
            Assert.That(HomeState.StoreItem(gs, KraeuterKasten.ItemId), Is.True);
            Assert.That(KraeuterKasten.Anzahl(gs), Is.EqualTo(1));
            Assert.That(KraeuterKasten.Schoepfe(gs, now), Is.EqualTo(0), "the first call only anchors the week");
            Assert.That(KraeuterKasten.LetzteErnteUnix(gs), Is.EqualTo(now));
            Assert.That(KraeuterKasten.Schoepfe(gs, now + 6 * 86400), Is.EqualTo(0));
            Assert.That(KraeuterKasten.Schoepfe(gs, now + 7 * 86400 + 5), Is.EqualTo(1));
            Assert.That(J.Int(J.Path(gs.State, "inventory", "items", KraeuterKasten.WareId)), Is.EqualTo(1));
            Assert.That(KraeuterKasten.LetzteErnteUnix(gs), Is.EqualTo(now + 7 * 86400), "the anchor moves by whole weeks");
            Assert.That(HomeState.StoreItem(gs, KraeuterKasten.ItemId), Is.True);
            Assert.That(KraeuterKasten.Faellig(gs, now + 7 * 86400 + 30 * 86400), Is.EqualTo(2 * 2), "two boxes, at most two weeks");
            Assert.That(KraeuterKasten.Schoepfe(gs, now + 7 * 86400 + 30 * 86400), Is.EqualTo(4));
            Assert.That(J.Int(J.Path(gs.State, "inventory", "items", KraeuterKasten.WareId)), Is.EqualTo(5));
        }

        [Test]
        public void GardenSpotsLieOnFreeCellsAndGrowBackAfterAnHour()
        {
            var gs = Store();
            GardenSpots.Refresh(gs, NowS, new GoobyRng(7));
            var spots = GardenSpots.Offene(gs);
            Assert.That(spots.Count, Is.EqualTo(GardenSpots.SpotsMax));
            Assert.That(spots.Select(s => s.At).Distinct().Count(), Is.EqualTo(spots.Count), "one spot per cell");
            Assert.That(spots.All(s => s.Material == "stock" || s.Material == "blatt"), Is.True);
            var free = GardenState.Grid(gs).FreeCells();
            Assert.That(spots.All(s => free.Contains(s.At)), Is.True);

            var first = spots[0];
            Assert.That(GardenSpots.Sammeln(gs, first.At, NowS), Is.EqualTo(first.Material));
            Assert.That(CraftState.MaterialCount(gs, first.Material), Is.EqualTo(1));
            Assert.That(GardenSpots.Sammeln(gs, first.At, NowS), Is.Empty, "picked once");
            GardenSpots.Refresh(gs, NowS + GardenSpots.RespawnS - 1, new GoobyRng(8));
            Assert.That(GardenSpots.Offene(gs).Count, Is.EqualTo(GardenSpots.SpotsMax - 1), "not back before the hour");
            GardenSpots.Refresh(gs, NowS + GardenSpots.RespawnS, new GoobyRng(9));
            Assert.That(GardenSpots.Offene(gs).Count, Is.EqualTo(GardenSpots.SpotsMax));

            // planting on a spot's cell hides it, and the next refresh moves it to a free cell
            var victim = GardenSpots.Offene(gs)[1];
            Assert.That(GardenState.Pflanzen(gs, victim.At, "carrot") || GardenWorld.Bauen(gs, "sprinkler", victim.At).Ok, Is.True);
            Assert.That(GardenSpots.Offene(gs).Any(s => s.At.Equals(victim.At)), Is.False);
            GardenSpots.Refresh(gs, NowS + GardenSpots.RespawnS + 1, new GoobyRng(10));
            Assert.That(GardenSpots.Offene(gs).Count, Is.EqualTo(GardenSpots.SpotsMax));
            Assert.That(GardenSpots.Offene(gs).Any(s => s.At.Equals(victim.At)), Is.False);
        }
    }
}
