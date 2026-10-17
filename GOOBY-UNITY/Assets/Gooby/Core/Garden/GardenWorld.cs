using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    /// <summary>
    /// Garden structures on the bed field — port of garden_world.gd (bauen / baum / erweitern) and the shed part of
    /// garden_host.gd: buying greenhouse, sprinkler, tree and workshop for coins (prices from home/materials.json
    /// "baumarkt"), the shed through ShedLogic, harvesting wood from a tree and enlarging the field.
    /// </summary>
    public static class GardenWorld
    {
        public const string ReasonZuTeuer = "zu_teuer";
        public const string ReasonSchonGebaut = "schon_gebaut";

        /// <summary>What the Baumarkt sells as a structure (garden_world.gd KAUFBAR); the shed goes through ShedLogic.</summary>
        public static readonly string[] Kaufbar = { "werkstatt", "gewaechshaus", "sprinkler", "baum" };

        /// <summary>A bought tree gives wood every two days (garden_state.gd BAUM_REIFE_S / BAUM_HOLZ).</summary>
        public const double BaumReifeS = 172800.0;
        public const int BaumHolz = 3;

        public const string MaterialsPath = "home/materials";

        private static Dictionary<string, int> _preise;

        /// <summary>Coin price of a structure from the Baumarkt list (0 when it is not for sale).</summary>
        public static int Preis(string kind)
        {
            if (_preise == null)
            {
                _preise = new Dictionary<string, int>();
                foreach (var t in ContentSource.LoadObject(MaterialsPath)["baumarkt"] as JArray ?? new JArray())
                    if (t is JObject o && J.Str(o["art"]) == "struktur") _preise[J.Str(o["id"])] = J.Int(o["preis"]);
            }
            return kind != null && _preise.TryGetValue(kind, out var p) ? p : 0;
        }

        public static void ResetCache() => _preise = null;

        public static bool IstKaufbar(string kind) => Array.IndexOf(Kaufbar, kind) >= 0;

        /// <summary>First footprint cell that works as the greenhouse door (garden_host.gd _tuer_zelle); null for other kinds.</summary>
        public static Cell? TuerZelle(GardenGrid grid, string kind, Cell at, int rot = 0)
        {
            if (kind != "gewaechshaus") return null;
            foreach (var cell in GardenGrid.StructureCells(kind, at, rot))
                if (grid.CanPlaceStructure(kind, at, rot, cell).Ok) return cell;
            return null;
        }

        /// <summary>Can the structure stand here and is it affordable? Reason as in GardenGrid plus zu_teuer.</summary>
        public static PlaceCheck Pruefen(GameStateStore gs, string kind, Cell at, int rot = 0, Cell? door = null)
        {
            if (!IstKaufbar(kind)) return PlaceCheck.Bad(GardenGrid.ReasonUnknown);
            var grid = GardenState.Grid(gs);
            var check = grid.CanPlaceStructure(kind, at, rot, door ?? TuerZelle(grid, kind, at, rot));
            if (!check.Ok) return check;
            return Preis(kind) > Coins(gs) ? PlaceCheck.Bad(ReasonZuTeuer) : PlaceCheck.Good;
        }

        /// <summary>Buys and places a structure; the door of a greenhouse is picked automatically when none is given.</summary>
        public static PlaceCheck Bauen(GameStateStore gs, string kind, Cell at, int rot = 0, Cell? door = null)
        {
            if (!IstKaufbar(kind)) return PlaceCheck.Bad(GardenGrid.ReasonUnknown);
            var grid = GardenState.Grid(gs);
            var tuer = door ?? TuerZelle(grid, kind, at, rot);
            var check = grid.CanPlaceStructure(kind, at, rot, tuer);
            if (!check.Ok) return check;
            var preis = Preis(kind);
            if (preis > Coins(gs)) return PlaceCheck.Bad(ReasonZuTeuer);
            grid.PlaceStructure(kind, at, rot, tuer);
            GardenState.SaveGrid(gs, grid);
            gs.Update(state =>
            {
                Economy.Spend(J.EnsureDict(state, "economy"), preis, "garten_bau");
                if (kind == "baum") J.EnsureDict(GardenOf(state), "baeume")[Key(at)] = 0.0;
            });
            gs.NotifySliceChanged("home");
            gs.NotifySliceChanged("economy");
            return PlaceCheck.Good;
        }

        /// <summary>The shed: first purchase places it (2×2) and raises the storage cap; later calls upgrade it in place.</summary>
        public static PlaceCheck ShedBauen(GameStateStore gs, Cell at)
        {
            var grid = GardenState.Grid(gs);
            var frisch = grid.StructuresOfKind("shed").Count == 0;
            if (frisch)
            {
                var check = grid.CanPlaceStructure("shed", at);
                if (!check.Ok) return check;
            }
            var vorher = HomeState.ShedStufe(gs);
            if (ShedLogic.UpgradePreis(vorher) == 0) return PlaceCheck.Bad(ReasonSchonGebaut);
            if (HomeState.UpgradeShed(gs) == vorher) return PlaceCheck.Bad(ReasonZuTeuer);
            if (frisch)
            {
                grid = GardenState.Grid(gs);
                grid.PlaceStructure("shed", at);
                GardenState.SaveGrid(gs, grid);
            }
            return PlaceCheck.Good;
        }

        public const string ReasonKeineZaunteile = "no_fence_parts";
        public const string ReasonRichtung = "bad_direction";
        public const string ZaunItem = "fence_wood";

        /// <summary>
        /// Port of garden_world.gd zaun_setzen: a fence edge of <paramref name="laenge"/> cells from <paramref name="from"/>
        /// along "E" (the north side of the row) or "S" (the west side of the column), paid with fence parts from the
        /// storage (one `fence_wood` per cell — crafted at the workbench or bought at the market).
        /// </summary>
        public static PlaceCheck ZaunSetzen(GameStateStore gs, Cell from, string dir, int laenge, string fence = ZaunItem)
        {
            if (laenge <= 0 || (dir != "E" && dir != "S")) return PlaceCheck.Bad(ReasonRichtung);
            if (StorageLogic.CountOf(HomeState.Storage(gs), fence) < laenge) return PlaceCheck.Bad(ReasonKeineZaunteile);
            var grid = GardenState.Grid(gs);
            if (!grid.InBounds(from)) return PlaceCheck.Bad(GardenGrid.ReasonOob);
            grid.Edges.Add(new GardenEdge { From = from, Dir = dir, Len = laenge, Fence = fence });
            GardenState.SaveGrid(gs, grid);
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, "home");
                if (!(home["storage"] is JArray)) home["storage"] = new JArray();
                for (var i = 0; i < laenge; i++) StorageLogic.Take((JArray)home["storage"], fence);
            });
            gs.NotifySliceChanged("home");
            return PlaceCheck.Good;
        }

        /// <summary>Fence parts in the storage (how many cells of fence the player can set).</summary>
        public static int Zaunteile(GameStateStore gs, string fence = ZaunItem) => StorageLogic.CountOf(HomeState.Storage(gs), fence);

        /// <summary>Next field size for coins (GardenState.StufenPreise); false when maxed out or too poor.</summary>
        public static PlaceCheck Erweitern(GameStateStore gs)
        {
            var preis = GardenState.NextStufePreis(gs);
            if (preis <= 0) return PlaceCheck.Bad(ReasonSchonGebaut);
            if (preis > Coins(gs)) return PlaceCheck.Bad(ReasonZuTeuer);
            gs.Update(state => Economy.Spend(J.EnsureDict(state, "economy"), preis, "garten_ausbau"));
            GardenState.Erweitern(gs);
            gs.NotifySliceChanged("economy");
            return PlaceCheck.Good;
        }

        public static bool BaumReif(GameStateStore gs, Cell at, double jetztS)
        {
            var baeume = GardenState.Slice(gs)["baeume"] as JObject;
            if (baeume == null || baeume[Key(at)] == null) return false;
            return jetztS - J.Num(baeume[Key(at)]) >= BaumReifeS;
        }

        /// <summary>Seconds until the tree gives wood again (0 when ripe, -1 for no tree).</summary>
        public static double BaumRestS(GameStateStore gs, Cell at, double jetztS)
        {
            var baeume = GardenState.Slice(gs)["baeume"] as JObject;
            if (baeume == null || baeume[Key(at)] == null) return -1.0;
            return Math.Max(0.0, BaumReifeS - (jetztS - J.Num(baeume[Key(at)])));
        }

        /// <summary>Wood into home.materials; the tree stays and regrows from now. 0 = not ripe yet.</summary>
        public static int BaumErnten(GameStateStore gs, Cell at, double jetztS)
        {
            if (!BaumReif(gs, at, jetztS)) return 0;
            gs.Update(state =>
            {
                var materials = J.EnsureDict(J.EnsureDict(state, "home"), "materials");
                materials["holz"] = J.Int(materials["holz"]) + BaumHolz;
                J.EnsureDict(GardenOf(state), "baeume")[Key(at)] = jetztS;
            });
            gs.NotifySliceChanged("home");
            return BaumHolz;
        }

        public static int Holz(GameStateStore gs) => J.Int(J.Path(gs.State, "home", "materials", "holz"));

        private static int Coins(GameStateStore gs) => J.Int(J.Path(gs.State, "economy", "coins"));

        private static JObject GardenOf(JObject state)
        {
            var home = J.EnsureDict(state, "home");
            if (!(home["garden"] is JObject)) home["garden"] = GardenState.DefaultGarden();
            return (JObject)home["garden"];
        }

        private static string Key(Cell at) => at.X + "," + at.Y;
    }
}
