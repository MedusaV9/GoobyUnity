using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_home_garden.gd.</summary>
    public class GardenTests
    {
        private const double JetztS = 1_768_478_400.0;
        private const long NowMs = 1_768_478_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            GardenCrops.ResetCache();
        }

        private static GardenGrid G() => new GardenGrid(8, 8);

        private static JObject Plot(string crop) => new JObject { ["kind"] = "plot", ["crop"] = crop, ["stage"] = 0, ["progress_min"] = 0.0, ["watered_until"] = 0.0 };

        [Test]
        public void CropsAreNormalised()
        {
            Assert.That(GardenCrops.Ids().Count, Is.EqualTo(11));
            foreach (var id in GardenCrops.Ids())
            {
                var c = GardenCrops.Crop(id);
                Assert.That(GardenCrops.LichtArten, Does.Contain(c.Licht), id);
                Assert.That(c.Stufen >= 1 && c.Ernte >= 1, Is.True, id);
                Assert.That(GardenCrops.BasePrice(id), Is.GreaterThan(0), id);
            }
            var exoten = GardenCrops.Plantable(false).Count;
            Assert.That(GardenCrops.Plantable(true).Count, Is.GreaterThan(exoten));
        }

        [Test]
        public void StructuresOccupyTheirFootprint()
        {
            Assert.That(GardenGrid.StructureSize("gewaechshaus"), Is.EqualTo((2, 3)));
            Assert.That(GardenGrid.StructureCells("shed", new Cell(1, 1)).Count, Is.EqualTo(4));
            Assert.That(GardenGrid.StructureCells("werkstatt", new Cell(0, 0), 1).Count, Is.EqualTo(6));
            var grid = G();
            Assert.That(grid.PlaceStructure("shed", new Cell(0, 0)).Ok, Is.True);
            var kollision = grid.PlaceStructure("baum", new Cell(1, 1));
            Assert.That(kollision.Ok, Is.False);
            Assert.That(kollision.Reason, Is.EqualTo(GardenGrid.ReasonOccupied));
            Assert.That(grid.CanPlaceStructure("shed", new Cell(7, 7)).Reason, Is.EqualTo(GardenGrid.ReasonOob));
            Assert.That(grid.CanPlaceStructure("ufo", new Cell(4, 4)).Reason, Is.EqualTo(GardenGrid.ReasonUnknown));
            Assert.That(grid.StructureAt(new Cell(1, 1)).Kind, Is.EqualTo("shed"));
            Assert.That(grid.RemoveStructure(new Cell(1, 1)), Is.True);
            Assert.That(grid.Structures, Is.Empty);
        }

        [Test]
        public void GreenhouseNeedsADoorCell()
        {
            var grid = G();
            Assert.That(grid.CanPlaceStructure("gewaechshaus", new Cell(2, 2)).Reason, Is.EqualTo(GardenGrid.ReasonNeedsDoor));
            Assert.That(grid.PlaceStructure("gewaechshaus", new Cell(2, 2), 0, new Cell(2, 2)).Ok, Is.True);
            var innen = grid.GreenhouseCells();
            Assert.That(innen.Count, Is.EqualTo(5));
            Assert.That(innen.Contains(new Cell(2, 2)), Is.False);
            Assert.That(innen.Contains(new Cell(3, 4)), Is.True);
        }

        [Test]
        public void SprinklerAndFenceCoverage()
        {
            var grid = G();
            grid.PlaceStructure("sprinkler", new Cell(4, 4));
            var nass = grid.SprinklerCells();
            Assert.That(nass.Count, Is.EqualTo(9));
            Assert.That(nass.Contains(new Cell(3, 3)) && nass.Contains(new Cell(5, 5)), Is.True);
            Assert.That(nass.Contains(new Cell(6, 4)), Is.False);
            grid.Edges.Add(new GardenEdge { From = new Cell(0, 0), Dir = "E", Len = 4 });
            var geschuetzt = grid.FenceShieldedCells();
            Assert.That(geschuetzt.Contains(new Cell(0, 0)) && geschuetzt.Contains(new Cell(3, 2)), Is.True);
            Assert.That(geschuetzt.Contains(new Cell(4, 0)), Is.False);
        }

        [Test]
        public void SaveRoundTripAndHealing()
        {
            var grid = G();
            grid.SetCell(new Cell(5, 5), Plot("carrot"));
            Assert.That(grid.PlaceStructure("gewaechshaus", new Cell(1, 1), 0, new Cell(1, 1)).Ok, Is.True);
            grid.Edges.Add(new GardenEdge { From = new Cell(0, 5), Dir = "S", Len = 2 });
            var kopie = GardenGrid.FromSave(grid.ToSave());
            Assert.That((kopie.Width, kopie.Height), Is.EqualTo((grid.Width, grid.Height)));
            Assert.That(kopie.PlotCells(), Is.EqualTo(grid.PlotCells()));
            Assert.That(J.Str(kopie.Cell(new Cell(5, 5))["crop"]), Is.EqualTo("carrot"));
            Assert.That(kopie.GreenhouseCells().Count, Is.EqualTo(5));
            Assert.That(kopie.Edges.Count, Is.EqualTo(1));
            Assert.That(kopie.Edges[0].Len, Is.EqualTo(2));
            var kaputt = GardenGrid.FromSave(JObject.Parse("{\"size\":[8,8],\"cells\":[{\"at\":[99,99],\"crop\":\"carrot\"}],\"structures\":[{\"kind\":\"ufo\",\"at\":[1,1]}]}"));
            Assert.That(kaputt.Structures, Is.Empty);
            Assert.That(kaputt.Cells, Is.Empty);
        }

        [Test]
        public void WaterWindAndShadeFactors()
        {
            Assert.That(GardenGrowth.WasserFaktor(0.0, 100.0, false, false, false), Is.EqualTo(0.0));
            Assert.That(GardenGrowth.WasserFaktor(200.0, 100.0, false, false, false), Is.EqualTo(1.0));
            Assert.That(GardenGrowth.WasserFaktor(0.0, 100.0, false, true, false), Is.EqualTo(1.0));
            Assert.That(GardenGrowth.WasserFaktor(0.0, 100.0, true, false, false), Is.EqualTo(1.0));
            Assert.That(GardenGrowth.WasserFaktor(0.0, 100.0, true, false, true), Is.EqualTo(0.0), "rain stays outside the greenhouse");
            var none = new HashSet<Cell>();
            Assert.That(GardenGrowth.WindFaktor(new Cell(0, 0), 8, 8, none, false), Is.EqualTo(0.85).Within(1e-9));
            Assert.That(GardenGrowth.WindFaktor(new Cell(1, 3), 8, 8, none, false), Is.EqualTo(0.85).Within(1e-9));
            Assert.That(GardenGrowth.WindFaktor(new Cell(4, 4), 8, 8, none, false), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(GardenGrowth.WindFaktor(new Cell(0, 0), 8, 8, new HashSet<Cell> { new Cell(0, 0) }, false), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(GardenGrowth.WindFaktor(new Cell(0, 0), 8, 8, none, true), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(GardenGrowth.WindFaktor(new Cell(0, 0), 8, 8, none, false, true), Is.EqualTo(0.7).Within(1e-9));
            Assert.That(GardenGrowth.SchattenFaktor(false, "sonne"), Is.EqualTo(1.0));
            Assert.That(GardenGrowth.SchattenFaktor(true, "sonne"), Is.EqualTo(0.75));
            Assert.That(GardenGrowth.SchattenFaktor(true, "schatten"), Is.EqualTo(1.1));
            Assert.That(GardenGrowth.SchattenFaktor(true, "neutral"), Is.EqualTo(1.0));
        }

        [Test]
        public void ShadeFallsNorthAndSprinklersCastNone()
        {
            var grid = G();
            grid.PlaceStructure("baum", new Cell(4, 4));
            var schatten = GardenGrowth.SchattenZellen(grid);
            Assert.That(schatten.Contains(new Cell(4, 3)) && schatten.Contains(new Cell(4, 2)), Is.True);
            Assert.That(schatten.Contains(new Cell(4, 5)), Is.False);
            grid.Structures.Clear();
            grid.PlaceStructure("sprinkler", new Cell(4, 4));
            Assert.That(GardenGrowth.SchattenZellen(grid), Is.Empty);
        }

        [Test]
        public void CellRateAndGreenhouseBonus()
        {
            var grid = G();
            grid.SetCell(new Cell(4, 4), Plot("tomate"));
            Assert.That(GardenGrowth.FaktorenOf(grid, new Cell(4, 4), 100.0, false, new HashSet<Cell>()).Rate, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(GardenGrowth.FaktorenOf(grid, new Cell(4, 4), 100.0, true, new HashSet<Cell>()).Rate, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(GardenGrowth.FaktorenOf(grid, new Cell(4, 4), 100.0, true, new HashSet<Cell> { new Cell(4, 4) }).Rate, Is.EqualTo(0.75).Within(1e-9));
            Assert.That(GardenGrowth.Rate(1.0, 0.85, 1.1, 1.25), Is.EqualTo(1.0 * 0.85 * 1.1 * 1.25).Within(1e-12));
            var gh = G();
            gh.PlaceStructure("gewaechshaus", new Cell(3, 3), 0, new Cell(3, 3));
            var innen = new Cell(4, 5);
            gh.SetCell(innen, Plot("ananas"));
            var regen = GardenGrowth.FaktorenOf(gh, innen, 100.0, true, new HashSet<Cell> { innen });
            Assert.That(regen.Wasser, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(regen.Wind, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(regen.Schatten, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(regen.Gewaechshaus, Is.EqualTo(1.25).Within(1e-9));
            Assert.That(gh.PlaceStructure("sprinkler", new Cell(5, 5)).Ok, Is.True);
            Assert.That(GardenGrowth.FaktorenOf(gh, innen, 100.0, false, new HashSet<Cell>()).Rate, Is.EqualTo(1.25).Within(1e-9));
        }

        [Test]
        public void AdvanceCountsStagesAndNeverDies()
        {
            var crop = GardenCrops.Crop("carrot");
            double proStufe = crop.MinutenProStufe;
            var data = new JObject { ["crop"] = "carrot", ["stage"] = 0, ["progress_min"] = 0.0 };
            Assert.That(GardenGrowth.Advance(data, proStufe, 1.0), Is.EqualTo(1));
            Assert.That(GardenGrowth.Advance(data, proStufe, 0.0), Is.EqualTo(1));
            Assert.That(GardenGrowth.Advance(data, proStufe * 10.0, 1.0), Is.EqualTo(crop.Stufen));
            Assert.That(GardenGrowth.IstErntereif(data), Is.True);
            Assert.That(GardenGrowth.IstErntereif(new JObject { ["crop"] = "", ["stage"] = 9 }), Is.False);
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            return store;
        }

        [Test]
        public void PlantWaterHarvestLoop()
        {
            var gs = Store();
            var beet = new Cell(2, 2);
            Assert.That(GardenState.Pflanzen(gs, beet, "gibtsnicht"), Is.False);
            Assert.That(GardenState.Pflanzen(gs, beet, "ananas"), Is.False, "exotic needs a greenhouse");
            Assert.That(GardenState.Pflanzen(gs, beet, "carrot"), Is.True);
            Assert.That(GardenState.Pflanzen(gs, beet, "salat"), Is.False, "plot taken");
            Assert.That(GardenState.Giessen(gs, beet, JetztS), Is.True);
            Assert.That(J.Num(GardenState.Grid(gs).Cell(beet)["watered_until"]), Is.GreaterThan(JetztS));
            Assert.That(GardenState.Ernten(gs, beet), Is.EqualTo(0));
            var minuten = GardenCrops.TotalMinutes("carrot");
            Assert.That(GardenState.Tick(gs, JetztS), Is.EqualTo(0), "first tick only sets the clock");
            Assert.That(GardenState.Tick(gs, JetztS + minuten * 60.0), Is.GreaterThanOrEqualTo(1));
            Assert.That(GardenGrowth.IstErntereif(GardenState.Grid(gs).Cell(beet)), Is.True);
            var foodBefore = J.Int(gs.State["inventory"]["food"]["carrot"]);
            var menge = GardenState.Ernten(gs, beet);
            Assert.That(menge, Is.EqualTo(GardenCrops.Crop("carrot").Ernte));
            Assert.That(J.Int(GardenState.Ernte(gs)["carrot"]), Is.EqualTo(menge));
            Assert.That(J.Int(gs.State["inventory"]["food"]["carrot"]), Is.EqualTo(foodBefore + menge), "harvest lands in the fridge");
            Assert.That(J.Str(GardenState.Grid(gs).Cell(beet)["crop"]), Is.EqualTo(""));
            Assert.That(J.Int(gs.State["achievements"]["counters"]["harvests"]), Is.EqualTo(1));
            Assert.That(J.Int(gs.State["achievements"]["counters"]["plantings"]), Is.EqualTo(1));
            Assert.That(J.Int(gs.State["achievements"]["counters"]["waterings"]), Is.EqualTo(1));
            Assert.That(J.Int(gs.State["collections"]["entries"]["veggies.carrot"]), Is.EqualTo(1), "album entry");
        }

        [Test]
        public void MarketStockContractAndExpansion()
        {
            var gs = Store();
            var beet = new Cell(2, 2);
            GardenState.Pflanzen(gs, beet, "salat");
            GardenState.Tick(gs, JetztS);
            GardenState.Giessen(gs, beet, JetztS);
            GardenState.Tick(gs, JetztS + GardenCrops.TotalMinutes("salat") * 60.0);
            var menge = GardenState.Ernten(gs, beet);
            Assert.That(menge, Is.GreaterThan(0));
            Assert.That(GardenState.TakeErnte(gs, "salat", menge + 1), Is.False);
            Assert.That(GardenState.TakeErnte(gs, "salat", menge), Is.True);
            Assert.That(GardenState.Ernte(gs)["salat"], Is.Null);
            Assert.That(GardenState.NextStufePreis(gs), Is.EqualTo(800));
            Assert.That(GardenState.Erweitern(gs), Is.True);
            Assert.That(GardenState.Grid(gs).Width, Is.EqualTo(8));
            Assert.That(GardenState.Normalize(JToken.Parse("{\"stufe\": 99}"))["size"].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[12,10]"));
        }
    }
}
