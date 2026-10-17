using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    public enum GridLayer { Rug = 0, Floor = 1, Surface = 2, Wall = 3, Ceiling = 4 }

    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public static Cell operator +(Cell a, Cell b) => new Cell(a.X + b.X, a.Y + b.Y);
        public bool Equals(Cell o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public override string ToString() => $"({X},{Y})";
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
    }

    /// <summary>Placed instance inside a GridData.</summary>
    public sealed class GridItem
    {
        public string Uid;
        public FurnitureDef Def;
        public Cell At;
        public int Rot;
        public string Wall;   // null for floor-layer items
        /// <summary>The colour it was bought in (FurnitureVariants id); null = the original.</summary>
        public string Variant;
    }

    public struct PlaceCheck
    {
        public bool Ok;
        public string Reason;
        public static PlaceCheck Good => new PlaceCheck { Ok = true, Reason = GridData.ReasonOk };
        public static PlaceCheck Bad(string reason) => new PlaceCheck { Ok = false, Reason = reason };
    }

    /// <summary>
    /// Room grid occupancy model — port of scripts/home/grid_data.gd. Layers: rug/floor/surface/ceiling on cells,
    /// wall items on wall slots; door zones block building; surface items need a carrier below.
    /// </summary>
    public sealed class GridData
    {
        public const double CellSize = 0.5;
        public const double DeckenHoehe = 2.45;
        public static readonly string[] Walls = { "N", "E", "S", "W" };
        public const string ReasonOk = "";
        public const string ReasonOob = "out_of_bounds";
        public const string ReasonBlocked = "blocked_zone";
        public const string ReasonOccupied = "occupied";
        public const string ReasonNeedsSurface = "needs_surface";
        public const string ReasonUnknownItem = "unknown_item";
        public const string ReasonNeedsExterior = "needs_exterior";
        public const string ReasonWindow = "window";

        public int Width { get; }
        public int Height { get; }
        public readonly HashSet<Cell> Blocked = new HashSet<Cell>();
        public readonly Dictionary<string, List<(int from, int toExcl)>> WallBlocked = new Dictionary<string, List<(int, int)>>();
        /// <summary>The room's own windows (rooms.json): only a window piece may hang there (it replaces the window).</summary>
        public readonly Dictionary<string, List<(int from, int toExcl)>> WallWindows = new Dictionary<string, List<(int, int)>>();
        public readonly Dictionary<string, string> WallExterior = new Dictionary<string, string>();

        private readonly Dictionary<string, GridItem> _items = new Dictionary<string, GridItem>();
        private readonly Dictionary<Cell, string> _rug = new Dictionary<Cell, string>();
        private readonly Dictionary<Cell, string> _floor = new Dictionary<Cell, string>();
        private readonly Dictionary<Cell, string> _surface = new Dictionary<Cell, string>();
        private readonly Dictionary<Cell, string> _ceiling = new Dictionary<Cell, string>();
        private readonly Dictionary<string, string> _wallSlots = new Dictionary<string, string>();

        public GridData(int width = 12, int height = 10, IEnumerable<Cell> blocked = null,
            Dictionary<string, List<(int, int)>> wallDoors = null, Dictionary<string, string> exterior = null,
            Dictionary<string, List<(int, int)>> wallWindows = null)
        {
            Width = width;
            Height = height;
            if (blocked != null) foreach (var c in blocked) Blocked.Add(c);
            if (wallDoors != null) foreach (var kv in wallDoors) WallBlocked[kv.Key] = new List<(int, int)>(kv.Value);
            if (exterior != null) foreach (var kv in exterior) WallExterior[kv.Key] = kv.Value;
            if (wallWindows != null) foreach (var kv in wallWindows) WallWindows[kv.Key] = new List<(int, int)>(kv.Value);
        }

        public IEnumerable<GridItem> Items => _items.Values;

        public static (int w, int h) RotatedFootprint(int w, int h, int rot) => PosMod(rot, 2) == 1 ? (h, w) : (w, h);

        public static List<Cell> CellsFor(Cell at, int fw, int fh, int rot)
        {
            var cells = new List<Cell>();
            var (w, h) = RotatedFootprint(fw, fh, rot);
            for (var dy = 0; dy < h; dy++)
            for (var dx = 0; dx < w; dx++)
                cells.Add(at + new Cell(dx, dy));
            return cells;
        }

        public bool InBounds(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public PlaceCheck CanPlace(FurnitureDef def, Cell at, int rot, string ignoreUid = "")
        {
            if (def == null) return PlaceCheck.Bad(ReasonUnknownItem);
            if (def.Layer == GridLayer.Wall) return PlaceCheck.Bad(ReasonOob);
            foreach (var cell in CellsFor(at, def.FootprintW, def.FootprintH, rot))
            {
                var reason = CellReason(def.Layer, cell, ignoreUid);
                if (reason != ReasonOk) return PlaceCheck.Bad(reason);
            }
            return PlaceCheck.Good;
        }

        private string CellReason(GridLayer layer, Cell cell, string ignoreUid)
        {
            if (!InBounds(cell)) return ReasonOob;
            if (layer == GridLayer.Ceiling) return Taken(_ceiling, cell, ignoreUid) ? ReasonOccupied : ReasonOk;
            if (layer != GridLayer.Surface && Blocked.Contains(cell)) return ReasonBlocked;
            var map = layer == GridLayer.Rug ? _rug : layer == GridLayer.Floor ? _floor : _surface;
            if (Taken(map, cell, ignoreUid)) return ReasonOccupied;
            if (layer == GridLayer.Surface && !HasSurfaceBelow(cell)) return ReasonNeedsSurface;
            return ReasonOk;
        }

        public PlaceCheck Place(FurnitureDef def, Cell at, int rot, string uid)
        {
            var check = CanPlace(def, at, rot);
            if (!check.Ok) return check;
            var item = new GridItem { Uid = uid, Def = def, At = at, Rot = PosMod(rot, 4) };
            _items[uid] = item;
            WriteCells(item);
            return check;
        }

        public PlaceCheck CanPlaceWall(FurnitureDef def, string wall, int offset, string ignoreUid = "")
        {
            if (def == null || def.Layer != GridLayer.Wall) return PlaceCheck.Bad(ReasonUnknownItem);
            if (Array.IndexOf(Walls, wall) < 0) return PlaceCheck.Bad(ReasonOob);
            var span = def.WallSize;
            if (offset < 0 || offset + span > WallWidth(wall)) return PlaceCheck.Bad(ReasonOob);
            if (def.Exterior && !WallExterior.ContainsKey(wall)) return PlaceCheck.Bad(ReasonNeedsExterior);
            var conflict = WallSpanConflict(wall, offset, span, ignoreUid);
            if (conflict == ReasonOk && !def.Exterior && WallWindows.TryGetValue(wall, out var windows))
                foreach (var w in windows)
                    if (offset < w.toExcl && offset + span > w.from) conflict = ReasonWindow;
            return conflict != ReasonOk ? PlaceCheck.Bad(conflict) : PlaceCheck.Good;
        }

        /// <summary>Is a window of the room covered by a window piece hanging over it (then the room does not draw its own)?</summary>
        public bool WindowReplaced(string wall, int from, int toExcl)
        {
            for (var slot = from; slot < toExcl; slot++)
            {
                var uid = WallItemAt(wall, slot);
                if (uid != "" && GetItem(uid)?.Def.Exterior == true) return true;
            }
            return false;
        }

        private string WallSpanConflict(string wall, int offset, int span, string ignoreUid)
        {
            if (WallBlocked.TryGetValue(wall, out var doors))
                foreach (var d in doors)
                    if (offset < d.toExcl && offset + span > d.from) return ReasonBlocked;
            for (var slot = 0; slot < span; slot++)
            {
                var key = wall + ":" + (offset + slot);
                if (_wallSlots.TryGetValue(key, out var uid) && uid != ignoreUid) return ReasonOccupied;
            }
            return ReasonOk;
        }

        public PlaceCheck PlaceWall(FurnitureDef def, string wall, int offset, string uid)
        {
            var check = CanPlaceWall(def, wall, offset);
            if (!check.Ok) return check;
            var item = new GridItem { Uid = uid, Def = def, Wall = wall, At = new Cell(offset, 0), Rot = 0 };
            _items[uid] = item;
            WriteCells(item);
            return check;
        }

        public GridItem RemoveItem(string uid)
        {
            if (!_items.TryGetValue(uid, out var item)) return null;
            EraseCells(uid);
            _items.Remove(uid);
            return item;
        }

        public PlaceCheck MoveItem(string uid, Cell at, int rot)
        {
            if (!_items.TryGetValue(uid, out var item)) return PlaceCheck.Bad(ReasonUnknownItem);
            var check = CanPlace(item.Def, at, rot, uid);
            if (!check.Ok) return check;
            EraseCells(uid);
            item.At = at;
            item.Rot = PosMod(rot, 4);
            WriteCells(item);
            return check;
        }

        public GridItem GetItem(string uid) => _items.TryGetValue(uid, out var i) ? i : null;

        public string ItemAt(Cell cell, GridLayer layer)
        {
            Dictionary<Cell, string> map;
            switch (layer)
            {
                case GridLayer.Rug: map = _rug; break;
                case GridLayer.Floor: map = _floor; break;
                case GridLayer.Surface: map = _surface; break;
                case GridLayer.Ceiling: map = _ceiling; break;
                default: return "";
            }
            return map.TryGetValue(cell, out var uid) ? uid : "";
        }

        public string WallItemAt(string wall, int offset) => _wallSlots.TryGetValue(wall + ":" + offset, out var uid) ? uid : "";

        public int WallWidth(string wall) => wall == "N" || wall == "S" ? Width : Height;

        public bool Walkable(Cell cell)
        {
            if (!InBounds(cell)) return false;
            if (!_floor.TryGetValue(cell, out var uid) || uid == "") return true;
            return !_items[uid].Def.BlocksMovement;
        }

        public bool IsReachable(Cell from, Cell to)
        {
            if (!Walkable(from) || !Walkable(to)) return false;
            if (from == to) return true;
            var frontier = new Stack<Cell>();
            frontier.Push(from);
            var seen = new HashSet<Cell> { from };
            var steps = new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, -1), new Cell(0, 1) };
            while (frontier.Count > 0)
            {
                var cell = frontier.Pop();
                foreach (var step in steps)
                {
                    var next = cell + step;
                    if (seen.Contains(next) || !Walkable(next)) continue;
                    if (next == to) return true;
                    seen.Add(next);
                    frontier.Push(next);
                }
            }
            return false;
        }

        public bool IsZoneReachable(Cell from, IEnumerable<Cell> zone) => zone.Any(c => IsReachable(from, c));

        public List<Cell> FreeCells()
        {
            var cells = new List<Cell>();
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var c = new Cell(x, y);
                if (Blocked.Contains(c) || _floor.ContainsKey(c)) continue;
                cells.Add(c);
            }
            return cells;
        }

        /// <summary>Save-format rows sorted by uid: {uid, item, at:[x,y], rot, wall?}.</summary>
        public JArray ToItemsArray()
        {
            var outp = new JArray();
            foreach (var uid in _items.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var item = _items[uid];
                var entry = new JObject { ["uid"] = uid, ["item"] = item.Def.Id, ["at"] = new JArray(item.At.X, item.At.Y), ["rot"] = item.Rot };
                if (item.Wall != null) entry["wall"] = item.Wall;
                if (!FurnitureVariants.IsNeutral(item.Variant)) entry["variant"] = item.Variant;
                outp.Add(entry);
            }
            return outp;
        }

        public struct FromSaveResult
        {
            public GridData Grid;
            public List<JToken> Leftovers;
        }

        /// <summary>Rebuilds a grid from save rows; carriers are placed before surface items; failures become leftovers.</summary>
        public static FromSaveResult FromSave(JArray entries, IReadOnlyDictionary<string, FurnitureDef> defs, int w, int h,
            IEnumerable<Cell> blocked = null, Dictionary<string, List<(int, int)>> doors = null, Dictionary<string, string> exterior = null,
            Dictionary<string, List<(int, int)>> windows = null)
        {
            var grid = new GridData(w, h, blocked, doors, exterior, windows);
            var leftovers = new List<JToken>();
            var surface = new List<JObject>();
            foreach (var entry in entries)
            {
                if (!(entry is JObject e) || !defs.TryGetValue(J.Str(e["item"]), out var def)) { leftovers.Add(entry); continue; }
                if (def.Layer == GridLayer.Surface) { surface.Add(e); continue; }
                if (!grid.PlaceSavedEntry(def, e).Ok) leftovers.Add(entry);
            }
            foreach (var e in surface)
                if (!grid.PlaceSavedEntry(defs[J.Str(e["item"])], e).Ok) leftovers.Add(e);
            return new FromSaveResult { Grid = grid, Leftovers = leftovers };
        }

        private PlaceCheck PlaceSavedEntry(FurnitureDef def, JObject entry)
        {
            var uid = J.Str(entry["uid"]);
            var at = entry["at"] as JArray;
            var ax = at != null && at.Count > 0 ? J.Int(at[0]) : 0;
            var ay = at != null && at.Count > 1 ? J.Int(at[1]) : 0;
            var placed = entry["wall"] != null && entry["wall"].Type == JTokenType.String
                ? PlaceWall(def, J.Str(entry["wall"]), ax, uid)
                : Place(def, new Cell(ax, ay), J.Int(entry["rot"]), uid);
            var variant = J.Str(entry["variant"]);
            if (placed.Ok && !FurnitureVariants.IsNeutral(variant) && _items.TryGetValue(uid, out var item)) item.Variant = variant;
            return placed;
        }

        public static (double x, double y, double z) WorldCenter(Cell at, int fw, int fh, int rot)
        {
            var (w, h) = RotatedFootprint(fw, fh, rot);
            return ((at.X + w * 0.5) * CellSize, 0.0, (at.Y + h * 0.5) * CellSize);
        }

        public static Cell CellOf(double x, double z) => new Cell((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize));

        private void WriteCells(GridItem item)
        {
            if (item.Wall != null)
            {
                for (var slot = 0; slot < item.Def.WallSize; slot++) _wallSlots[item.Wall + ":" + (item.At.X + slot)] = item.Uid;
                return;
            }
            foreach (var cell in CellsFor(item.At, item.Def.FootprintW, item.Def.FootprintH, item.Rot))
            {
                switch (item.Def.Layer)
                {
                    case GridLayer.Rug: _rug[cell] = item.Uid; break;
                    case GridLayer.Floor: _floor[cell] = item.Uid; break;
                    case GridLayer.Surface: _surface[cell] = item.Uid; break;
                    case GridLayer.Ceiling: _ceiling[cell] = item.Uid; break;
                }
            }
        }

        private void EraseCells(string uid)
        {
            foreach (var map in new[] { _rug, _floor, _surface, _ceiling })
                foreach (var key in map.Where(kv => kv.Value == uid).Select(kv => kv.Key).ToList()) map.Remove(key);
            foreach (var key in _wallSlots.Where(kv => kv.Value == uid).Select(kv => kv.Key).ToList()) _wallSlots.Remove(key);
        }

        private static bool Taken(Dictionary<Cell, string> map, Cell cell, string ignoreUid) =>
            map.TryGetValue(cell, out var uid) && uid != "" && uid != ignoreUid;

        private bool HasSurfaceBelow(Cell cell) => _floor.TryGetValue(cell, out var uid) && uid != "" && _items[uid].Def.Surface;

        public static int PosMod(int a, int m) => ((a % m) + m) % m;
    }
}
