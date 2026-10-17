using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// Sonnental's street lattice from the authored town plan (city/city_map.json, Doc E §1.2): 15×12 tiles of 20 m,
    /// street rows and columns, the airport spur, the two roundabouts, the districts, home and the decorative
    /// landmarks. Pure numbers — the drive model steers on it, the 3D scene and the minimap draw it, the tests check it.
    ///
    /// Coordinates: tile (r, c) → world x = −(c − (Cols−1)/2)·TileM, z = −(r − (Rows−1)/2)·TileM: row 0 is the north
    /// edge (+z), columns grow towards −x. A heading of 0 rad drives north (+z), π/2 east (+x); facing north, east is on
    /// the right — and the plan's column 2 (home, the GOOBY MART) lies on the LEFT of the home street, as on board 03.
    /// The minimap (tools/art/build_minimap.py) is drawn in the same world frame, north up.
    /// </summary>
    public static class CityGrid
    {
        public const double TileM = 20.0;
        /// <summary>Half width of the asphalt of a street tile (the drivable band around the tile's centre line).</summary>
        public const double RoadHalfWidth = 4.0;
        public const double WalkWidth = 2.2;
        /// <summary>Right-hand traffic: the lane centre lies this far right of the tile's centre line.</summary>
        public const double LaneOffset = 2.0;
        /// <summary>A roundabout tile is a round plaza: asphalt out to this radius around the tile centre …</summary>
        public const double PlazaRadius = 5.6;
        /// <summary>… with the flower island (kerb included) in the middle that nothing drives over.</summary>
        public const double IslandRadius = 1.8;
        /// <summary>The ring the ambient traffic circulates on, counter-clockwise (right-hand traffic keeps the island on its left).</summary>
        public const double RingRadius = 3.6;

        public sealed class Landmark
        {
            public string Id, Glb, NameKey, Tint, SchildTint;
            public double R, C, Rot, Scale;
            public int[] SchildStrasse;
        }

        public sealed class District
        {
            public string Id;
            public int Energie;
            /// <summary>Zones as [r0, c0, r1, c1] inclusive tile rectangles.</summary>
            public List<int[]> Zonen = new List<int[]>();

            public bool Contains(int r, int c)
            {
                foreach (var z in Zonen)
                    if (r >= z[0] && r <= z[2] && c >= z[1] && c <= z[3]) return true;
                return false;
            }
        }

        private static bool _loaded;
        private static int _rows = 12, _cols = 15;
        private static readonly HashSet<(int r, int c)> Streets = new HashSet<(int, int)>();
        private static readonly HashSet<(int r, int c)> Roundabouts = new HashSet<(int, int)>();
        private static readonly List<Landmark> LandmarkList = new List<Landmark>();
        private static readonly List<District> DistrictList = new List<District>();
        private static int[] _homeTile = { 9, 2 }, _homeStreet = { 9, 1 };
        private static double _homeDriveway = 11.5;
        private static int[] _kitchenTile = { 5, 5 }, _kitchenStreet = { 5, 6 };
        private static int _seed = 20260725;
        private static readonly List<int[][]> LoopList = new List<int[][]>();

        /// <summary>The plan's ambient-traffic loops as corner tiles (the authored numbers, used when the plan is not readable).</summary>
        private static readonly int[][][] FallbackLoops =
        {
            new[] { new[] { 1, 1 }, new[] { 1, 6 }, new[] { 4, 6 }, new[] { 4, 1 } },
            new[] { new[] { 4, 6 }, new[] { 4, 13 }, new[] { 7, 13 }, new[] { 7, 6 } },
            new[] { new[] { 1, 1 }, new[] { 1, 13 }, new[] { 10, 13 }, new[] { 10, 1 } },
            new[] { new[] { 4, 1 }, new[] { 4, 6 }, new[] { 7, 6 }, new[] { 7, 1 } },
            new[] { new[] { 7, 10 }, new[] { 7, 13 }, new[] { 10, 13 }, new[] { 10, 10 } },
            new[] { new[] { 1, 6 }, new[] { 1, 10 }, new[] { 7, 10 }, new[] { 7, 6 } },
        };

        private static bool _fromPlan;

        private static void Ensure()
        {
            // the fallback lattice is replaced by the authored plan as soon as the content source is wired up
            if (_loaded && (_fromPlan || !ContentSource.Installed)) return;
            Streets.Clear(); Roundabouts.Clear(); LandmarkList.Clear(); DistrictList.Clear(); LoopList.Clear();
            _loaded = true;
            var map = ContentSource.Installed ? ContentSource.TryLoadObject(CityMap.Path) : null;
            if (map == null)
            {
                // the authored plan as a fallback so the street exists before the content source is wired up
                _rows = 12; _cols = 15;
                _fromPlan = false;
                AddLattice(new[] { 1, 4, 7, 10 }, new[] { 1, 13 }, new[] { 1, 6, 10, 13 }, new[] { 1, 10 }, new[] { new[] { 0, 4 } }, new[] { new[] { 1, 6 }, new[] { 7, 6 } });
                LoopList.AddRange(FallbackLoops);
                return;
            }
            _fromPlan = true;
            _rows = J.Int(map["grid_reihen"], 12);
            _cols = J.Int(map["grid_spalten"], 15);
            _seed = J.Int(map["deko_seed"], 20260725);
            var s = map["strassen"] as JObject ?? new JObject();
            AddLattice(Ints(s["reihen"]), Ints(s["reihen_spalten"]), Ints(s["spalten"]), Ints(s["spalten_reihen"]), Pairs(s["extra"]), Pairs(s["kreisel"]));
            foreach (var loop in map["traffic_loops"] as JArray ?? new JArray())
            {
                var corners = Pairs(loop);
                if (corners.Count >= 3) LoopList.Add(corners.ToArray());
            }
            if (LoopList.Count == 0) LoopList.AddRange(FallbackLoops);
            if (map["zuhause"] is JObject home)
            {
                _homeTile = Ints(home["tile"]);
                _homeStreet = Ints(home["strasse"]);
                _homeDriveway = J.NumOr(home["einfahrt_m"], 11.5);
            }
            if (map["gooberando_kueche"] is JObject k)
            {
                _kitchenTile = Ints(k["tile"]);
                _kitchenStreet = Ints(k["strasse"]);
            }
            foreach (var d in map["distrikte"] as JObject ?? new JObject())
            {
                var dist = new District { Id = d.Key, Energie = J.Int(d.Value?["energie"], 4) };
                foreach (var z in d.Value?["zonen"] as JArray ?? new JArray()) dist.Zonen.Add(Ints(z));
                DistrictList.Add(dist);
            }
            foreach (var t in map["deko"] as JArray ?? new JArray())
            {
                if (!(t is JObject o)) continue;
                var tile = o["tile"] as JArray;
                LandmarkList.Add(new Landmark
                {
                    Id = J.Str(o["id"]), Glb = J.Str(o["glb"]), NameKey = J.Str(o["name_key"]), Tint = J.Str(o["tint"]),
                    SchildTint = J.Str(o["schild_tint"]), SchildStrasse = Ints(o["schild_strasse"]),
                    R = tile != null && tile.Count > 0 ? J.Num(tile[0]) : 0.0, C = tile != null && tile.Count > 1 ? J.Num(tile[1]) : 0.0,
                    Rot = J.Num(o["rot"]), Scale = J.NumOr(o["scale"], 1.0),
                });
            }
        }

        private static void AddLattice(int[] rows, int[] rowSpan, int[] cols, int[] colSpan, IEnumerable<int[]> extra, IEnumerable<int[]> roundabouts)
        {
            if (rowSpan.Length >= 2)
                foreach (var r in rows)
                    for (var c = rowSpan[0]; c <= rowSpan[1]; c++) Streets.Add((r, c));
            if (colSpan.Length >= 2)
                foreach (var c in cols)
                    for (var r = colSpan[0]; r <= colSpan[1]; r++) Streets.Add((r, c));
            foreach (var e in extra) if (e.Length >= 2) Streets.Add((e[0], e[1]));
            foreach (var k in roundabouts) if (k.Length >= 2) { Streets.Add((k[0], k[1])); Roundabouts.Add((k[0], k[1])); }
            Streets.Add(RanchExit);
        }

        /// <summary>
        /// Row 7 runs on past column 13 to the town's east edge: the road out to the Gooby ranch (ranch_exit.gd's
        /// AUSFAHRT_TILE). Godot laid a slab beyond the lattice; here it is a dead-end street tile like the airport spur.
        /// </summary>
        public static readonly (int r, int c) RanchExit = (7, 14);

        /// <summary>Forget the cached plan (tests swap the content source).</summary>
        public static void Reset() { _loaded = false; _fromPlan = false; }

        public static int Rows { get { Ensure(); return _rows; } }
        public static int Cols { get { Ensure(); return _cols; } }
        public static int Seed { get { Ensure(); return _seed; } }
        public static double WorldHalfX => (Cols - 1) / 2.0 * TileM + TileM / 2.0;
        public static double WorldHalfZ => (Rows - 1) / 2.0 * TileM + TileM / 2.0;

        public static bool IsStreet(int r, int c) { Ensure(); return Streets.Contains((r, c)); }
        public static bool IsRoundabout(int r, int c) { Ensure(); return Roundabouts.Contains((r, c)); }
        public static bool InBounds(int r, int c) => r >= 0 && c >= 0 && r < Rows && c < Cols;

        public static IEnumerable<(int r, int c)> StreetTiles { get { Ensure(); return Streets; } }
        /// <summary>Ambient-traffic loops of the plan: corner tiles (r, c) of closed rectangles on the street lattice.</summary>
        public static IReadOnlyList<int[][]> TrafficLoops { get { Ensure(); return LoopList; } }
        public static IReadOnlyList<Landmark> Landmarks { get { Ensure(); return LandmarkList; } }
        public static IReadOnlyList<District> Districts { get { Ensure(); return DistrictList; } }

        public static int[] HomeTile { get { Ensure(); return _homeTile; } }
        public static int[] HomeStreet { get { Ensure(); return _homeStreet; } }
        public static double HomeDriveway { get { Ensure(); return _homeDriveway; } }
        public static int[] KitchenTile { get { Ensure(); return _kitchenTile; } }
        public static int[] KitchenStreet { get { Ensure(); return _kitchenStreet; } }

        /// <summary>District id of a tile (null for streets and the town's edge).</summary>
        public static string DistrictOf(int r, int c)
        {
            foreach (var d in Districts) if (d.Contains(r, c)) return d.Id;
            return null;
        }

        /// <summary>Open street sides of a tile as (dr, dc) steps: which neighbours are streets too.</summary>
        public static List<(int dr, int dc)> StreetNeighbours(int r, int c)
        {
            var outp = new List<(int, int)>(4);
            if (IsStreet(r - 1, c)) outp.Add((-1, 0));
            if (IsStreet(r, c + 1)) outp.Add((0, 1));
            if (IsStreet(r + 1, c)) outp.Add((1, 0));
            if (IsStreet(r, c - 1)) outp.Add((0, -1));
            return outp;
        }

        /// <summary>The nearest street tile next to a block tile (4-neighbourhood, then diagonals), or null.</summary>
        public static (int r, int c)? StreetNextTo(int r, int c)
        {
            foreach (var (dr, dc) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1) })
                if (IsStreet(r + dr, c + dc)) return (r + dr, c + dc);
            return null;
        }

        public static (double x, double z) World(double r, double c)
        {
            var x = -(c - (Cols - 1) / 2.0) * TileM;
            var z = -(r - (Rows - 1) / 2.0) * TileM;
            return (x, z);
        }

        public static (int r, int c) Tile(double x, double z)
        {
            var c = (int)Math.Round(-x / TileM + (Cols - 1) / 2.0);
            var r = (int)Math.Round(-z / TileM + (Rows - 1) / 2.0);
            return (r, c);
        }

        /// <summary>
        /// Keeps a point on the asphalt: the drivable area is the union of the street bands (centre line ± RoadHalfWidth
        /// minus <paramref name="margin"/>) of every street tile, including the junction squares. Returns the nearest
        /// drivable point and whether the input had to move.
        /// </summary>
        public static (double x, double z, bool hit) ClampToStreets(double x, double z, double margin)
        {
            var half = RoadHalfWidth - margin;
            var (r0, c0) = Tile(x, z);
            var bestD2 = double.MaxValue;
            double bx = x, bz = z;
            // the candidate tiles: the one under the point and its ring of neighbours (a moving car never jumps two
            // tiles in a step); a point far off the lattice falls back to every street tile
            for (var r = r0 - 1; r <= r0 + 1; r++)
                for (var c = c0 - 1; c <= c0 + 1; c++)
                    if (IsStreet(r, c)) ConsiderTile(r, c);
            if (bestD2 == double.MaxValue)
                foreach (var (r, c) in StreetTiles) ConsiderTile(r, c);
            if (bestD2 == double.MaxValue) return (x, z, false);   // no streets at all
            return (bx, bz, bestD2 > 1e-9);

            void ConsiderTile(int r, int c)
            {
                var (cx, cz) = World(r, c);
                // junction square around the tile centre — a roundabout is a round plaza with the island cut out
                var round = IsRoundabout(r, c);
                var plaza = PlazaRadius - margin;
                if (round) ConsiderAnnulus(cx, cz, IslandRadius + margin, plaza);
                else Consider(cx - half, cx + half, cz - half, cz + half);
                // arms towards the street neighbours reach the tile edge (and meet the neighbour's arm there); on a
                // roundabout they start where the plaza already covers their full width, so the island stays a wall
                var from = round ? Math.Sqrt(Math.Max(0.0, plaza * plaza - half * half)) : 0.0;
                foreach (var (dr, dc) in StreetNeighbours(r, c))
                {
                    var (nx, nz) = World(r + dr, c + dc);
                    var ex = (cx + nx) / 2.0; var ez = (cz + nz) / 2.0;   // the tile edge in world coordinates
                    var sx = cx + Math.Sign(ex - cx) * from; var sz = cz + Math.Sign(ez - cz) * from;   // the arm's start
                    if (dr != 0) Consider(cx - half, cx + half, Math.Min(sz, ez), Math.Max(sz, ez));
                    else Consider(Math.Min(sx, ex), Math.Max(sx, ex), cz - half, cz + half);
                }
            }

            void Consider(double minX, double maxX, double minZ, double maxZ)
            {
                var px = x < minX ? minX : x > maxX ? maxX : x;
                var pz = z < minZ ? minZ : z > maxZ ? maxZ : z;
                var d2 = (px - x) * (px - x) + (pz - z) * (pz - z);
                if (d2 < bestD2) { bestD2 = d2; bx = px; bz = pz; }
            }

            void ConsiderAnnulus(double cx, double cz, double rIn, double rOut)
            {
                var dx = x - cx; var dz = z - cz;
                var rho = Math.Sqrt(dx * dx + dz * dz);
                if (rho < 1e-9) { dx = 1.0; dz = 0.0; rho = 1.0; }   // dead centre: any way out is as good as another
                var target = rho < rIn ? rIn : rho > rOut ? rOut : rho;
                var px = cx + dx / rho * target; var pz = cz + dz / rho * target;
                var d2 = (px - x) * (px - x) + (pz - z) * (pz - z);
                if (d2 < bestD2) { bestD2 = d2; bx = px; bz = pz; }
            }
        }

        /// <summary>The number of street arms leaving a street tile (2 = straight/bend, 3 = T, 4 = crossroads).</summary>
        public static int Arms(int r, int c) => StreetNeighbours(r, c).Count;

        /// <summary>
        /// Shortest street-tile path (BFS, 4-neighbour). Empty when either end is not a street. Used by the
        /// GOOBERANDO driver (Godot CityRoadGraph.pfad) so kitchen → home is a pure function of the lattice.
        /// </summary>
        public static List<(int r, int c)> StreetPath((int r, int c) from, (int r, int c) to)
        {
            Ensure();
            var path = new List<(int, int)>();
            if (!IsStreet(from.r, from.c) || !IsStreet(to.r, to.c)) return path;
            if (from.Equals(to)) { path.Add(from); return path; }
            var q = new Queue<(int r, int c)>();
            var came = new Dictionary<(int, int), (int, int)>();
            q.Enqueue(from);
            came[from] = from;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur.Equals(to)) break;
                foreach (var (dr, dc) in StreetNeighbours(cur.r, cur.c))
                {
                    var n = (cur.r + dr, cur.c + dc);
                    if (came.ContainsKey(n)) continue;
                    came[n] = cur;
                    q.Enqueue(n);
                }
            }
            if (!came.ContainsKey(to)) return path;
            var p = to;
            while (!p.Equals(from)) { path.Add(p); p = came[p]; }
            path.Add(from);
            path.Reverse();
            return path;
        }

        private static int[] Ints(JToken t)
        {
            if (!(t is JArray a)) return new int[0];
            var outp = new int[a.Count];
            for (var i = 0; i < a.Count; i++) outp[i] = J.Int(a[i]);
            return outp;
        }

        private static List<int[]> Pairs(JToken t)
        {
            var outp = new List<int[]>();
            foreach (var p in t as JArray ?? new JArray()) outp.Add(Ints(p));
            return outp;
        }
    }
}
