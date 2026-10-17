using System;
using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    /// <summary>
    /// Collecting spots on the bed field — port of garden_world.gd refresh_spots / offene_spots / sammeln: up to four
    /// free cells carry a stick or a leaf bundle for the workbench; a picked spot grows back after an hour with a new
    /// draw. Save: home.garden.spots = [{at:[x,y], material, leer, ready_at}]. A spot whose cell got planted or built
    /// over moves on at the next refresh.
    /// </summary>
    public static class GardenSpots
    {
        public const int SpotsMax = 4;
        public const double RespawnS = 3600.0;
        public static readonly string[] SpotMaterialien = { "stock", "stock", "stock", "blatt" };

        public readonly struct Spot
        {
            public readonly Cell At;
            public readonly string Material;

            public Spot(Cell at, string material)
            {
                At = at;
                Material = material;
            }
        }

        private static Cell AtOf(JToken entry)
        {
            var at = entry?["at"] as JArray;
            return at != null && at.Count >= 2 ? new Cell(J.Int(at[0]), J.Int(at[1])) : new Cell(-1, -1);
        }

        private static string Draw(GoobyRng rng) => SpotMaterialien[rng.RangeI(0, SpotMaterialien.Length - 1)];

        /// <summary>Spawns missing spots on free cells and lets picked ones grow back. Idempotent; runs whenever the garden is staged.</summary>
        public static void Refresh(GameStateStore gs, double jetztS, GoobyRng rng)
        {
            if (gs == null) return;
            var frei = GardenState.Grid(gs).FreeCells();
            var freeSet = new HashSet<Cell>(frei);
            if (!NeedsWork(GardenState.Slice(gs)["spots"] as JArray, freeSet, jetztS)) return;
            var changed = false;
            gs.Update(state =>
            {
                var garden = J.EnsureDict(J.EnsureDict(state, "home"), "garden");
                if (!(garden["spots"] is JArray)) garden["spots"] = new JArray();
                var spots = (JArray)garden["spots"];
                for (var i = spots.Count - 1; i >= 0; i--)
                {
                    if (spots[i] is JObject e && freeSet.Contains(AtOf(e))) continue;
                    spots.RemoveAt(i);
                    changed = true;
                }
                var belegt = new HashSet<Cell>();
                foreach (var t in spots)
                {
                    var e = (JObject)t;
                    belegt.Add(AtOf(e));
                    if (!J.IsTrue(e["leer"]) || J.Num(e["ready_at"]) > jetztS) continue;
                    e["leer"] = false;
                    e["material"] = Draw(rng);
                    changed = true;
                }
                var kandidaten = new List<Cell>(frei);
                kandidaten.RemoveAll(belegt.Contains);
                while (spots.Count < SpotsMax && kandidaten.Count > 0)
                {
                    var idx = rng.RangeI(0, kandidaten.Count - 1);
                    var cell = kandidaten[idx];
                    kandidaten.RemoveAt(idx);
                    spots.Add(new JObject { ["at"] = new JArray(cell.X, cell.Y), ["material"] = Draw(rng), ["leer"] = false, ["ready_at"] = 0.0 });
                    changed = true;
                }
            });
            if (changed) gs.NotifySliceChanged("home");
        }

        /// <summary>Read-only look whether a refresh would change anything (keeps the minute tick from rewriting the save).</summary>
        private static bool NeedsWork(JArray spots, HashSet<Cell> free, double jetztS)
        {
            spots = spots ?? new JArray();
            var lying = 0;
            foreach (var t in spots)
            {
                if (!(t is JObject e) || !free.Contains(AtOf(e))) return true;
                if (J.IsTrue(e["leer"]) && J.Num(e["ready_at"]) <= jetztS) return true;
                lying++;
            }
            return lying < SpotsMax && free.Count > lying;
        }

        /// <summary>The spots lying right now (picked ones and built-over cells are left out).</summary>
        public static List<Spot> Offene(GameStateStore gs)
        {
            var outp = new List<Spot>();
            if (gs == null) return outp;
            var free = new HashSet<Cell>(GardenState.Grid(gs).FreeCells());
            foreach (var t in GardenState.Slice(gs)["spots"] as JArray ?? new JArray())
            {
                if (!(t is JObject e) || J.IsTrue(e["leer"])) continue;
                var at = AtOf(e);
                if (!free.Contains(at)) continue;
                outp.Add(new Spot(at, J.Str(e["material"], "stock")));
            }
            return outp;
        }

        /// <summary>Picks the spot on a cell: one unit into home.materials, the respawn timer starts. "" = nothing lay there.</summary>
        public static string Sammeln(GameStateStore gs, Cell at, double jetztS)
        {
            var gefunden = "";
            foreach (var s in Offene(gs))
                if (s.At.Equals(at)) { gefunden = s.Material; break; }
            if (gefunden.Length == 0) return "";
            gs.Update(state =>
            {
                var garden = J.EnsureDict(J.EnsureDict(state, "home"), "garden");
                foreach (var t in garden["spots"] as JArray ?? new JArray())
                {
                    if (!(t is JObject e) || !AtOf(e).Equals(at)) continue;
                    e["leer"] = true;
                    e["ready_at"] = jetztS + RespawnS;
                }
                CraftLogic.Add(J.EnsureDict(J.EnsureDict(state, "home"), "materials"), gefunden, 1);
            });
            gs.NotifySliceChanged("home");
            return gefunden;
        }
    }
}
