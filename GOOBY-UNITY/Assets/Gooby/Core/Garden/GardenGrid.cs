using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    public sealed class GardenStructure
    {
        public string Kind;
        public Cell At;
        public int Rot, Level = 1;
        public Cell? Door;
    }

    public sealed class GardenEdge
    {
        public Cell From;
        public string Dir = "E", Fence = "fence_wood";
        public int Len = 1;
    }

    /// <summary>Garden plot grid (plots, structures, fence edges) — port of garden_grid.gd. Cell data stays JObject (save format).</summary>
    public sealed class GardenGrid
    {
        public const string ReasonOk = "";
        public const string ReasonOob = "out_of_bounds";
        public const string ReasonOccupied = "occupied";
        public const string ReasonNeedsDoor = "needs_door";
        public const string ReasonUnknown = "unknown_structure";
        public const double CellSize = 1.0;
        public const int SprinklerRadius = 1;

        public static readonly IReadOnlyDictionary<string, (int w, int h)> StructureSizes = new Dictionary<string, (int, int)>
        {
            { "shed", (2, 2) }, { "werkstatt", (3, 2) }, { "gewaechshaus", (2, 3) }, { "baum", (1, 1) }, { "sprinkler", (1, 1) }, { "garage", (2, 3) },
        };

        public int Width { get; }
        public int Height { get; }
        public readonly Dictionary<Cell, JObject> Cells = new Dictionary<Cell, JObject>();
        public readonly List<GardenStructure> Structures = new List<GardenStructure>();
        public readonly List<GardenEdge> Edges = new List<GardenEdge>();

        public GardenGrid(int w = 8, int h = 6)
        {
            Width = w;
            Height = h;
        }

        public bool InBounds(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public static (int w, int h) StructureSize(string kind) => StructureSizes.TryGetValue(kind ?? "", out var s) ? s : (0, 0);

        public static List<Cell> StructureCells(string kind, Cell at, int rot = 0)
        {
            var outp = new List<Cell>();
            var (w, h) = StructureSize(kind);
            if (w == 0) return outp;
            if (GridData.PosMod(rot, 2) == 1) (w, h) = (h, w);
            for (var dy = 0; dy < h; dy++)
            for (var dx = 0; dx < w; dx++)
                outp.Add(at + new Cell(dx, dy));
            return outp;
        }

        public Dictionary<Cell, string> OccupiedCells()
        {
            var taken = new Dictionary<Cell, string>();
            foreach (var s in Structures)
                foreach (var c in StructureCells(s.Kind, s.At, s.Rot)) taken[c] = s.Kind;
            return taken;
        }

        public PlaceCheck CanPlaceStructure(string kind, Cell at, int rot = 0, Cell? door = null)
        {
            if (StructureSize(kind).w == 0) return PlaceCheck.Bad(ReasonUnknown);
            var taken = OccupiedCells();
            var footprint = StructureCells(kind, at, rot);
            foreach (var c in footprint)
            {
                if (!InBounds(c)) return PlaceCheck.Bad(ReasonOob);
                if (taken.ContainsKey(c) || CellKind(c) == "plot") return PlaceCheck.Bad(ReasonOccupied);
            }
            if (kind == "gewaechshaus" && !DoorValid(footprint, door, taken)) return PlaceCheck.Bad(ReasonNeedsDoor);
            return PlaceCheck.Good;
        }

        public PlaceCheck PlaceStructure(string kind, Cell at, int rot = 0, Cell? door = null)
        {
            var check = CanPlaceStructure(kind, at, rot, door);
            if (!check.Ok) return check;
            var s = new GardenStructure { Kind = kind, At = at, Rot = GridData.PosMod(rot, 4), Level = 1 };
            if (kind == "gewaechshaus") s.Door = door;
            Structures.Add(s);
            return check;
        }

        public GardenStructure StructureAt(Cell cell) => Structures.FirstOrDefault(s => StructureCells(s.Kind, s.At, s.Rot).Contains(cell));

        public List<GardenStructure> StructuresOfKind(string kind) => Structures.Where(s => s.Kind == kind).ToList();

        public bool RemoveStructure(Cell cell)
        {
            var idx = Structures.FindIndex(s => StructureCells(s.Kind, s.At, s.Rot).Contains(cell));
            if (idx < 0) return false;
            Structures.RemoveAt(idx);
            return true;
        }

        public HashSet<Cell> GreenhouseCells()
        {
            var outp = new HashSet<Cell>();
            foreach (var s in StructuresOfKind("gewaechshaus"))
                foreach (var c in StructureCells("gewaechshaus", s.At, s.Rot))
                    if (!(s.Door.HasValue && s.Door.Value == c)) outp.Add(c);
            return outp;
        }

        public HashSet<Cell> SprinklerCells()
        {
            var outp = new HashSet<Cell>();
            foreach (var s in StructuresOfKind("sprinkler"))
                for (var dy = -SprinklerRadius; dy <= SprinklerRadius; dy++)
                for (var dx = -SprinklerRadius; dx <= SprinklerRadius; dx++)
                {
                    var c = s.At + new Cell(dx, dy);
                    if (InBounds(c)) outp.Add(c);
                }
            return outp;
        }

        public HashSet<Cell> FenceShieldedCells(int depth = 3)
        {
            var outp = new HashSet<Cell>();
            foreach (var e in Edges)
            {
                var horizontal = e.Dir == "E";
                for (var step = 0; step < Math.Max(1, e.Len); step++)
                for (var d = 0; d < depth; d++)
                {
                    var c = horizontal ? e.From + new Cell(step, d) : e.From + new Cell(d, step);
                    if (InBounds(c)) outp.Add(c);
                }
            }
            return outp;
        }

        public List<Cell> PlotCells() => Cells.Keys.Where(c => CellKind(c) == "plot").OrderBy(c => c.Y * Width + c.X).ToList();

        public List<Cell> FreeCells()
        {
            var taken = OccupiedCells();
            var outp = new List<Cell>();
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var c = new Cell(x, y);
                if (taken.ContainsKey(c) || Cells.ContainsKey(c)) continue;
                outp.Add(c);
            }
            return outp;
        }

        public JObject Cell(Cell at) => Cells.TryGetValue(at, out var d) ? d : new JObject();

        public void SetCell(Cell at, JObject data) => Cells[at] = data;

        public static GardenGrid FromSave(JObject raw)
        {
            var sizeRaw = raw["size"] as JArray;
            var grid = new GardenGrid(sizeRaw != null && sizeRaw.Count > 0 ? J.Int(sizeRaw[0], 8) : 8, sizeRaw != null && sizeRaw.Count > 1 ? J.Int(sizeRaw[1], 6) : 6);
            foreach (var t in raw["cells"] as JArray ?? new JArray())
            {
                if (!(t is JObject e)) continue;
                var at = AsCell(e["at"]);
                if (!grid.InBounds(at)) continue;
                grid.Cells[at] = new JObject
                {
                    ["kind"] = J.Str(e["kind"], "plot"), ["crop"] = J.Str(e["crop"]),
                    ["stage"] = Math.Max(0, J.Int(e["stage"])), ["progress_min"] = Math.Max(0.0, J.Num(e["progress_min"])),
                    ["watered_until"] = Math.Max(0.0, J.Num(e["watered_until"])),
                };
            }
            foreach (var t in raw["structures"] as JArray ?? new JArray())
            {
                if (!(t is JObject e) || StructureSize(J.Str(e["kind"])).w == 0) continue;
                var s = new GardenStructure { Kind = J.Str(e["kind"]), At = AsCell(e["at"]), Rot = GridData.PosMod(J.Int(e["rot"]), 4), Level = Math.Max(1, J.Int(e["level"], 1)) };
                if (e["door"] != null) s.Door = AsCell(e["door"]);
                grid.Structures.Add(s);
            }
            foreach (var t in raw["edges"] as JArray ?? new JArray())
            {
                if (!(t is JObject e)) continue;
                grid.Edges.Add(new GardenEdge { From = AsCell(e["from"]), Dir = J.Str(e["dir"], "E") == "S" ? "S" : "E", Len = Math.Max(1, J.Int(e["len"], 1)), Fence = J.Str(e["fence"], "fence_wood") });
            }
            return grid;
        }

        public JObject ToSave()
        {
            var cellList = new JArray();
            foreach (var at in Cells.Keys.OrderBy(c => c.Y * Width + c.X))
            {
                var d = Cells[at];
                cellList.Add(new JObject
                {
                    ["at"] = new JArray(at.X, at.Y), ["kind"] = J.Str(d["kind"], "plot"), ["crop"] = J.Str(d["crop"]),
                    ["stage"] = J.Int(d["stage"]), ["progress_min"] = J.Num(d["progress_min"]), ["watered_until"] = J.Num(d["watered_until"]),
                });
            }
            var structList = new JArray();
            foreach (var s in Structures)
            {
                var o = new JObject { ["kind"] = s.Kind, ["at"] = new JArray(s.At.X, s.At.Y), ["rot"] = s.Rot, ["level"] = s.Level };
                if (s.Door.HasValue) o["door"] = new JArray(s.Door.Value.X, s.Door.Value.Y);
                structList.Add(o);
            }
            var edgeList = new JArray();
            foreach (var e in Edges) edgeList.Add(new JObject { ["from"] = new JArray(e.From.X, e.From.Y), ["dir"] = e.Dir, ["len"] = e.Len, ["fence"] = e.Fence });
            return new JObject { ["size"] = new JArray(Width, Height), ["cells"] = cellList, ["structures"] = structList, ["edges"] = edgeList };
        }

        private static Cell AsCell(JToken v)
        {
            if (v is JArray a && a.Count >= 2) return new Cell(J.Int(a[0]), J.Int(a[1]));
            return new Cell(-1, -1);
        }

        private string CellKind(Cell at) => Cells.TryGetValue(at, out var d) ? J.Str(d["kind"]) : "";

        private bool DoorValid(List<Cell> footprint, Cell? door, Dictionary<Cell, string> taken)
        {
            if (!door.HasValue || !footprint.Contains(door.Value)) return false;
            foreach (var step in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, -1), new Cell(0, 1) })
            {
                var outside = door.Value + step;
                if (footprint.Contains(outside)) continue;
                if (InBounds(outside) && !taken.ContainsKey(outside)) return true;
            }
            return false;
        }
    }
}
