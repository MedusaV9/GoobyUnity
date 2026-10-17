using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>A cell of the farm's build grid (x east, y south).</summary>
    public readonly struct Zelle : IEquatable<Zelle>
    {
        public readonly int X, Y;
        public Zelle(int x, int y) { X = x; Y = y; }
        public static Zelle operator +(Zelle a, Zelle b) => new Zelle(a.X + b.X, a.Y + b.Y);
        public static bool operator ==(Zelle a, Zelle b) => a.Equals(b);
        public static bool operator !=(Zelle a, Zelle b) => !a.Equals(b);
        public bool Equals(Zelle o) => X == o.X && Y == o.Y;
        public override bool Equals(object o) => o is Zelle z && Equals(z);
        public override int GetHashCode() => X * 7919 + Y;
        public override string ToString() => "(" + X + "," + Y + ")";
    }

    /// <summary>A rectangle of cells (min corner, width, depth).</summary>
    public readonly struct ZellRect
    {
        public readonly int X, Y, W, H;
        public ZellRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public int EndX => X + W;
        public int EndY => Y + H;
        public bool Enthaelt(Zelle c) => c.X >= X && c.Y >= Y && c.X < EndX && c.Y < EndY;
    }

    /// <summary>One entry of the build catalogue (RanchBauKatalog.defs).</summary>
    public sealed class RanchBauDef
    {
        public string Id = "", Kategorie = "", NameKey = "";
        public RanchGridData.Layer Layer;
        public Zelle Footprint = new Zelle(1, 1);
        public bool Kante, Upgrade;
        public int Kosten;
        public int[] Stufen = new int[0];

        /// <summary>A quest reward: never for sale, only placed from the build store, back into it when pulled down.</summary>
        public bool Belohnung;

        /// <summary>How many decoration pieces it counts as for the style points (a reward counts as five).</summary>
        public int Stil = 1;
    }

    /// <summary>
    /// The farm's outdoor build grid (port of ranch_grid_data.gd, the pure model of the ranch build mode): 16×16 cells of
    /// 3 m; a ground layer (paths and surfaces — things may stand on them) and an object layer (facilities and decoration
    /// collide with each other); fences live on cell EDGES, each edge normalised to its north/west form
    /// (S(x,y) = N(x,y+1), E(x,y) = W(x+1,y)) — one edge, one truth; only cells in an unlocked zone can be built on.
    /// Rebuilt from the save items on load; unknown items and conflicts come back as leftovers (no data is ever lost).
    /// </summary>
    public sealed class RanchGridData
    {
        public enum Layer { Boden, Objekt }

        public const double CellSize = 3.0;
        public static readonly string[] Kanten = { "N", "W" };
        public const string ReasonOk = "", ReasonOob = "out_of_bounds", ReasonGesperrt = "locked_zone", ReasonOccupied = "occupied",
            ReasonUnknownItem = "unknown_item";

        public sealed class Item
        {
            public string Uid;
            public RanchBauDef Def;
            public Zelle At;
            public int Rot;
            /// <summary>"N" or "W" for a fence on an edge, null for a cell item.</summary>
            public string Kante;
        }

        public Zelle Size { get; }
        public List<ZellRect> Zonen { get; } = new List<ZellRect>();

        private readonly Dictionary<string, Item> _items = new Dictionary<string, Item>();
        private readonly Dictionary<Zelle, string> _bodenCells = new Dictionary<Zelle, string>();
        private readonly Dictionary<Zelle, string> _objektCells = new Dictionary<Zelle, string>();
        private readonly Dictionary<string, string> _kantenSlots = new Dictionary<string, string>();

        public RanchGridData(Zelle size, IEnumerable<ZellRect> freiZonen = null)
        {
            Size = size;
            if (freiZonen != null) Zonen.AddRange(freiZonen);
        }

        public static int PosMod(int v, int m) => ((v % m) + m) % m;

        /// <summary>Footprint after rotation (1 and 3 swap width and depth).</summary>
        public static Zelle RotatedFootprint(Zelle footprint, int rot) => PosMod(rot, 2) == 1 ? new Zelle(footprint.Y, footprint.X) : footprint;

        /// <summary>Every cell of an item anchored at <paramref name="at"/> (min corner) with rotation <paramref name="rot"/>.</summary>
        public static List<Zelle> CellsFor(Zelle at, Zelle footprint, int rot)
        {
            var cells = new List<Zelle>();
            var fp = RotatedFootprint(footprint, rot);
            for (var dy = 0; dy < fp.Y; dy++)
                for (var dx = 0; dx < fp.X; dx++)
                    cells.Add(new Zelle(at.X + dx, at.Y + dy));
            return cells;
        }

        /// <summary>An edge into its N/W form: S(x,y) → N(x,y+1), E(x,y) → W(x+1,y).</summary>
        public static (Zelle cell, string seite) NormalizeKante(Zelle cell, string seite)
        {
            switch (seite)
            {
                case "S": return (new Zelle(cell.X, cell.Y + 1), "N");
                case "E": return (new Zelle(cell.X + 1, cell.Y), "W");
                default: return (cell, seite);
            }
        }

        /// <summary>The ring of edges round a cell rect ("stake out a pasture"), optionally with a gate gap at an index along the ring.</summary>
        public static List<(Zelle cell, string seite)> WeideRing(ZellRect rect, int torIndex = -1)
        {
            var o = new List<(Zelle, string)>();
            for (var dx = 0; dx < rect.W; dx++) o.Add((new Zelle(rect.X + dx, rect.Y), "N"));
            for (var dy = 0; dy < rect.H; dy++) o.Add(NormalizeKante(new Zelle(rect.EndX - 1, rect.Y + dy), "E"));
            for (var dx = 0; dx < rect.W; dx++) o.Add(NormalizeKante(new Zelle(rect.EndX - 1 - dx, rect.EndY - 1), "S"));
            for (var dy = 0; dy < rect.H; dy++) o.Add((new Zelle(rect.X, rect.EndY - 1 - dy), "W"));
            if (torIndex >= 0 && torIndex < o.Count) o.RemoveAt(torIndex);
            return o;
        }

        public bool InBounds(Zelle cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Size.X && cell.Y < Size.Y;

        /// <summary>Is the cell inside an unlocked zone?</summary>
        public bool IstFrei(Zelle cell)
        {
            foreach (var zone in Zonen)
                if (zone.Enthaelt(cell)) return true;
            return false;
        }

        /// <summary>
        /// May a fence stand on this (normalised) edge? An N edge (x,y) borders (x,y-1)/(x,y), a W edge (x,y) borders
        /// (x-1,y)/(x,y) — at least one neighbour must be an unlocked cell (the zones' rim edges count).
        /// </summary>
        public string KanteErlaubt(Zelle cell, string seite)
        {
            if (Array.IndexOf(Kanten, seite) < 0) return ReasonOob;
            Zelle[] nachbarn;
            if (seite == "N")
            {
                if (cell.X < 0 || cell.X >= Size.X || cell.Y < 0 || cell.Y > Size.Y) return ReasonOob;
                nachbarn = new[] { new Zelle(cell.X, cell.Y - 1), cell };
            }
            else
            {
                if (cell.X < 0 || cell.X > Size.X || cell.Y < 0 || cell.Y >= Size.Y) return ReasonOob;
                nachbarn = new[] { new Zelle(cell.X - 1, cell.Y), cell };
            }
            foreach (var n in nachbarn)
                if (InBounds(n) && IstFrei(n)) return ReasonOk;
            return ReasonGesperrt;
        }

        /// <summary>Collision check on the cell layers; <paramref name="ignoreUid"/> is the item being moved.</summary>
        public (bool ok, string reason) CanPlace(RanchBauDef def, Zelle at, int rot, string ignoreUid = "")
        {
            if (def == null || def.Kante) return (false, ReasonUnknownItem);
            foreach (var cell in CellsFor(at, def.Footprint, rot))
            {
                var reason = CellReason(def.Layer, cell, ignoreUid);
                if (reason != ReasonOk) return (false, reason);
            }
            return (true, ReasonOk);
        }

        private string CellReason(Layer layer, Zelle cell, string ignoreUid)
        {
            if (!InBounds(cell)) return ReasonOob;
            if (!IstFrei(cell)) return ReasonGesperrt;
            var cells = layer == Layer.Boden ? _bodenCells : _objektCells;
            if (cells.TryGetValue(cell, out var uid) && uid != "" && uid != (ignoreUid ?? "")) return ReasonOccupied;
            return ReasonOk;
        }

        public (bool ok, string reason) Place(RanchBauDef def, Zelle at, int rot, string uid)
        {
            var check = CanPlace(def, at, rot);
            if (!check.ok) return check;
            var item = new Item { Uid = uid, Def = def, At = at, Rot = PosMod(rot, 4) };
            _items[uid] = item;
            WriteCells(item, uid);
            return check;
        }

        /// <summary>Collision check on the edge layer (fences); takes any side (N/E/S/W) and normalises it.</summary>
        public (bool ok, string reason) CanPlaceKante(RanchBauDef def, Zelle cell, string seite, string ignoreUid = "")
        {
            if (def == null || !def.Kante) return (false, ReasonUnknownItem);
            var norm = NormalizeKante(cell, seite);
            var erlaubt = KanteErlaubt(norm.cell, norm.seite);
            if (erlaubt != ReasonOk) return (false, erlaubt);
            if (_kantenSlots.TryGetValue(KantenKey(norm.cell, norm.seite), out var uid) && uid != "" && uid != (ignoreUid ?? "")) return (false, ReasonOccupied);
            return (true, ReasonOk);
        }

        public (bool ok, string reason) PlaceKante(RanchBauDef def, Zelle cell, string seite, string uid)
        {
            var check = CanPlaceKante(def, cell, seite);
            if (!check.ok) return check;
            var norm = NormalizeKante(cell, seite);
            _items[uid] = new Item { Uid = uid, Def = def, At = norm.cell, Rot = 0, Kante = norm.seite };
            _kantenSlots[KantenKey(norm.cell, norm.seite)] = uid;
            return check;
        }

        /// <summary>Removes an item (returns it, or null).</summary>
        public Item RemoveItem(string uid)
        {
            if (!_items.TryGetValue(uid ?? "", out var item)) return null;
            EraseCells(uid);
            _items.Remove(uid);
            return item;
        }

        /// <summary>Move and turn in one step (cell layers).</summary>
        public (bool ok, string reason) MoveItem(string uid, Zelle at, int rot)
        {
            if (!_items.TryGetValue(uid ?? "", out var item) || item.Kante != null) return (false, ReasonUnknownItem);
            var check = CanPlace(item.Def, at, rot, uid);
            if (!check.ok) return check;
            EraseCells(uid);
            item.At = at;
            item.Rot = PosMod(rot, 4);
            WriteCells(item, uid);
            return check;
        }

        public Item GetItem(string uid) => _items.TryGetValue(uid ?? "", out var item) ? item : null;

        public IReadOnlyDictionary<string, Item> Items => _items;

        /// <summary>The item on a cell of a layer ("" = free).</summary>
        public string ItemAt(Zelle cell, Layer layer) =>
            (layer == Layer.Boden ? _bodenCells : _objektCells).TryGetValue(cell, out var uid) ? uid : "";

        /// <summary>The fence on an edge named any way ("" = free).</summary>
        public string KanteItemAt(Zelle cell, string seite)
        {
            var norm = NormalizeKante(cell, seite);
            return _kantenSlots.TryGetValue(KantenKey(norm.cell, norm.seite), out var uid) ? uid : "";
        }

        /// <summary>Into the save format — deterministic, sorted by uid.</summary>
        public JArray ToItemsArray()
        {
            var o = new JArray();
            foreach (var uid in _items.Keys.OrderBy(u => u, StringComparer.Ordinal))
            {
                var item = _items[uid];
                var entry = new JObject { ["uid"] = uid, ["item"] = item.Def.Id, ["at"] = new JArray(item.At.X, item.At.Y), ["rot"] = item.Rot };
                if (item.Kante != null) entry["kante"] = item.Kante;
                o.Add(entry);
            }
            return o;
        }

        /// <summary>Rebuilds the grid from save items and the catalogue; unknown items and conflicts come back as leftovers.</summary>
        public static (RanchGridData grid, JArray leftovers) FromSave(JArray entries, IReadOnlyDictionary<string, RanchBauDef> defs, Zelle size, IEnumerable<ZellRect> freiZonen = null)
        {
            var grid = new RanchGridData(size, freiZonen);
            var leftovers = new JArray();
            foreach (var token in entries ?? new JArray())
            {
                if (!(token is JObject entry) || !defs.TryGetValue(J.Str(entry["item"]), out var def))
                {
                    leftovers.Add(token.DeepClone());
                    continue;
                }
                var uid = J.Str(entry["uid"], entry["uid"]?.ToString() ?? "");
                var at = AtVon(entry["at"]);
                var ok = entry.ContainsKey("kante")
                    ? grid.PlaceKante(def, at, J.Str(entry["kante"]), uid).ok
                    : grid.Place(def, at, J.Int(entry["rot"]), uid).ok;
                if (!ok) leftovers.Add(entry.DeepClone());
            }
            return (grid, leftovers);
        }

        public static Zelle AtVon(JToken raw) =>
            raw is JArray a && a.Count >= 2 ? new Zelle(J.Int(a[0]), J.Int(a[1])) : new Zelle(0, 0);

        /// <summary>Grid metres (x east, z south) of a footprint's centre.</summary>
        public static (double x, double z) WorldCenter(Zelle at, Zelle footprint, int rot)
        {
            var fp = RotatedFootprint(footprint, rot);
            return ((at.X + fp.X * 0.5) * CellSize, (at.Y + fp.Y * 0.5) * CellSize);
        }

        /// <summary>The cell under a grid position (metres).</summary>
        public static Zelle CellOf(double x, double z) => new Zelle((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize));

        /// <summary>Centre and heading of a (normalised) edge: N edges run along x (0), W edges along z (π/2).</summary>
        public static (double x, double z, double rot) KanteWorld(Zelle cell, string seite)
        {
            var norm = NormalizeKante(cell, seite);
            var c = norm.cell;
            return norm.seite == "N" ? ((c.X + 0.5) * CellSize, c.Y * CellSize, 0.0) : (c.X * CellSize, (c.Y + 0.5) * CellSize, Math.PI / 2.0);
        }

        /// <summary>The nearest edge to a grid position (a fence's ghost snaps to it).</summary>
        public static (Zelle cell, string seite) NearestKante(double x, double z)
        {
            var zx = x / CellSize;
            var zz = z / CellSize;
            var basis = new Zelle((int)Math.Floor(zx), (int)Math.Floor(zz));
            var fx = zx - basis.X;
            var fz = zz - basis.Y;
            var beste = "N";
            var best = fz;
            foreach (var (seite, d) in new[] { ("S", 1.0 - fz), ("W", fx), ("E", 1.0 - fx) })
                if (d < best) { best = d; beste = seite; }
            return NormalizeKante(basis, beste);
        }

        private static string KantenKey(Zelle cell, string seite) => seite + ":" + cell.X + ":" + cell.Y;

        private void WriteCells(Item item, string uid)
        {
            var cells = item.Def.Layer == Layer.Boden ? _bodenCells : _objektCells;
            foreach (var cell in CellsFor(item.At, item.Def.Footprint, item.Rot)) cells[cell] = uid;
        }

        private void EraseCells(string uid)
        {
            foreach (var k in _bodenCells.Where(p => p.Value == uid).Select(p => p.Key).ToList()) _bodenCells.Remove(k);
            foreach (var k in _objektCells.Where(p => p.Value == uid).Select(p => p.Key).ToList()) _objektCells.Remove(k);
            foreach (var k in _kantenSlots.Where(p => p.Value == uid).Select(p => p.Key).ToList()) _kantenSlots.Remove(k);
        }
    }
}
