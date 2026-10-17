using System;
using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    /// <summary>home.garden slice — port of garden_state.gd (plant / water / harvest / tick / expand / market stock).</summary>
    public static class GardenState
    {
        public static readonly (int w, int h)[] Stufen = { (6, 5), (8, 6), (10, 8), (12, 10) };
        public static readonly int[] StufenPreise = { 800, 2000, 5000 };

        public static JObject DefaultGarden() => new JObject
        {
            ["v"] = 1, ["stufe"] = 0, ["size"] = new JArray(Stufen[0].w, Stufen[0].h),
            ["cells"] = new JArray(), ["structures"] = new JArray(), ["edges"] = new JArray(), ["spots"] = new JArray(),
            ["ernte"] = new JObject(), ["baeume"] = new JObject(), ["lastTickAt"] = 0.0,
        };

        public static JObject Normalize(JToken raw)
        {
            var garden = raw as JObject ?? DefaultGarden();
            var defaults = DefaultGarden();
            foreach (var p in defaults.Properties()) if (garden[p.Name] == null) garden[p.Name] = p.Value.DeepClone();
            garden["stufe"] = J.ClampI(J.Int(garden["stufe"]), 0, Stufen.Length - 1);
            var size = Stufen[J.Int(garden["stufe"])];
            garden["size"] = new JArray(size.w, size.h);
            foreach (var k in new[] { "cells", "structures", "edges", "spots" }) if (!(garden[k] is JArray)) garden[k] = new JArray();
            foreach (var k in new[] { "ernte", "baeume" }) if (!(garden[k] is JObject)) garden[k] = new JObject();
            return garden;
        }

        public static JObject Slice(GameStateStore gs) => J.Path(gs.State, "home", "garden") as JObject ?? DefaultGarden();

        public static GardenGrid Grid(GameStateStore gs) => GardenGrid.FromSave(Slice(gs));

        private static JObject GardenOf(JObject state)
        {
            var home = J.EnsureDict(state, "home");
            if (!(home["garden"] is JObject)) home["garden"] = DefaultGarden();
            return (JObject)home["garden"];
        }

        public static void SaveGrid(GameStateStore gs, GardenGrid grid)
        {
            var payload = grid.ToSave();
            gs.Update(state =>
            {
                var garden = GardenOf(state);
                foreach (var p in payload.Properties()) garden[p.Name] = p.Value;
            });
            gs.NotifySliceChanged("home");
        }

        public static int NextStufePreis(GameStateStore gs)
        {
            var stufe = J.Int(Slice(gs)["stufe"]);
            return stufe >= StufenPreise.Length ? 0 : StufenPreise[stufe];
        }

        public static bool Erweitern(GameStateStore gs)
        {
            var stufe = J.Int(Slice(gs)["stufe"]);
            if (stufe + 1 >= Stufen.Length) return false;
            var next = Stufen[stufe + 1];
            gs.Update(state =>
            {
                var garden = GardenOf(state);
                garden["stufe"] = stufe + 1;
                garden["size"] = new JArray(next.w, next.h);
            });
            gs.NotifySliceChanged("home");
            return true;
        }

        public static bool Pflanzen(GameStateStore gs, Cell at, string cropId)
        {
            var grid = Grid(gs);
            var crop = GardenCrops.Crop(cropId);
            if (crop == null || !grid.InBounds(at)) return false;
            if (crop.Exot && !grid.GreenhouseCells().Contains(at)) return false;
            var occupied = grid.OccupiedCells();
            if (occupied.Count > 0 && occupied.TryGetValue(at, out var kind) && kind != "" && kind != "gewaechshaus") return false;
            var data = grid.Cell(at);
            if (J.Str(data["crop"]).Length > 0) return false;
            var samen = crop.Samen;
            if (samen.Length > 0 && SamenCount(gs, cropId) <= 0) return false;
            grid.SetCell(at, new JObject { ["kind"] = "plot", ["crop"] = cropId, ["stage"] = 0, ["progress_min"] = 0.0, ["watered_until"] = J.Num(data["watered_until"]) });
            SaveGrid(gs, grid);
            if (samen.Length > 0)
            {
                gs.Update(state =>
                {
                    var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
                    var rest = J.Int(items[samen]) - 1;
                    if (rest <= 0) items.Remove(samen);
                    else items[samen] = rest;
                });
            }
            BumpCounter(gs, "plantings");
            return true;
        }

        public static int SamenCount(GameStateStore gs, string cropId)
        {
            var samen = GardenCrops.SamenItem(cropId);
            if (samen.Length == 0) return -1;
            return J.Int(J.Path(gs.State, "inventory", "items", samen));
        }

        public static bool Giessen(GameStateStore gs, Cell at, double jetztS)
        {
            var grid = Grid(gs);
            var data = grid.Cell(at);
            if (!data.HasValues) return false;
            data["watered_until"] = jetztS + GardenGrowth.GiessStunden * 3600.0;
            grid.SetCell(at, data);
            SaveGrid(gs, grid);
            BumpCounter(gs, "waterings");
            return true;
        }

        public static int Ernten(GameStateStore gs, Cell at)
        {
            var grid = Grid(gs);
            var data = grid.Cell(at);
            if (!GardenGrowth.IstErntereif(data)) return 0;
            var cropId = J.Str(data["crop"]);
            var crop = GardenCrops.Crop(cropId);
            var menge = crop.Ernte;
            grid.SetCell(at, new JObject { ["kind"] = "plot", ["crop"] = "", ["stage"] = 0, ["progress_min"] = 0.0, ["watered_until"] = J.Num(data["watered_until"]) });
            SaveGrid(gs, grid);
            gs.Update(state =>
            {
                var ernte = J.EnsureDict(GardenOf(state), "ernte");
                ernte[cropId] = J.Int(ernte[cropId]) + menge;
                if (crop.Food.Length > 0)
                {
                    var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                    food[crop.Food] = J.Int(food[crop.Food]) + menge;
                }
                CollectionsLogic.AwardInState(state, "veggies", CollectionsLogic.VeggieEntryForCrop(cropId));
            });
            gs.NotifySliceChanged("home");
            BumpCounter(gs, "harvests");
            return menge;
        }

        /// <summary>Advances every planted plot by the elapsed minutes; returns how many plots reached a new stage.</summary>
        public static int Tick(GameStateStore gs, double jetztS, bool regen = false)
        {
            var grid = Grid(gs);
            var letzte = J.Num(Slice(gs)["lastTickAt"]);
            if (letzte <= 0.0 || jetztS <= letzte)
            {
                Stempel(gs, jetztS);
                return 0;
            }
            var minuten = (jetztS - letzte) / 60.0;
            var schatten = GardenGrowth.SchattenZellen(grid);
            var gewachsen = 0;
            foreach (var at in grid.PlotCells())
            {
                var data = grid.Cell(at);
                if (J.Str(data["crop"]).Length == 0) continue;
                var f = GardenGrowth.FaktorenOf(grid, at, letzte, regen, schatten);
                var vorher = J.Int(data["stage"]);
                var nachher = GardenGrowth.Advance(data, minuten, f.Rate);
                grid.SetCell(at, data);
                if (nachher > vorher) gewachsen++;
            }
            SaveGrid(gs, grid);
            Stempel(gs, jetztS);
            return gewachsen;
        }

        private static void Stempel(GameStateStore gs, double jetztS)
        {
            gs.Update(state => GardenOf(state)["lastTickAt"] = jetztS);
            gs.NotifySliceChanged("home");
        }

        public static JObject Ernte(GameStateStore gs) => (JObject)(Slice(gs)["ernte"] as JObject ?? new JObject()).DeepClone();

        public static bool TakeErnte(GameStateStore gs, string cropId, int menge = 1)
        {
            var have = J.Int(Ernte(gs)[cropId]);
            if (menge <= 0 || have < menge) return false;
            gs.Update(state =>
            {
                var lager = J.EnsureDict(GardenOf(state), "ernte");
                if (have == menge) lager.Remove(cropId);
                else lager[cropId] = have - menge;
            });
            gs.NotifySliceChanged("home");
            return true;
        }

        private static void BumpCounter(GameStateStore gs, string key)
        {
            gs.Update(state =>
            {
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                counters[key] = J.Int(counters[key]) + 1;
            });
            gs.NotifySliceChanged("achievements");
        }
    }
}
