using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Liefer-Hetze — port of GOOBY/src/minigames/games/deliveryRush.logic.js (§C1.2 #5 / §C10.2 / §G5): the van
    /// starts at the shop with 3 parcels; a seeded sequence of 3 DISTINCT targets among the 6 city landmarks;
    /// 4-m drop rings give +50 each; crashes −5 (floor 0); time bonus +max(0, 120 − s) after the 3rd drop.
    /// Also carries the minimal city grid (9×9, ring + cross roads) the path-finding and scene need.
    /// </summary>
    public static class DeliveryRush
    {
        public sealed class Tune
        {
            public int Parcels = 3, LandmarkPool = 6;
            public double DropRadiusM = 4.0;
            public int DropPoints = 50, CrashPenalty = 5;
            public double TimeBonusFromSec = 120.0;
            public int FragileCrashPenalty = 20, FragileCleanBonus = 15;
            public double SpeedMult = 1.0, TrafficDensityMult = 1.0;
            public int CrashAllowance = 0;
            public double CoinRate = 1.0, CoinIntervalSec = 8.0;
            public int CoinPoints = 3;
            public bool Endless = false;
            public double ParcelExpireSec = 45.0;
            public int EndlessExpiredLimit = 3;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Delivery = new Tune();

        // ── city grid excerpt (cityBuilder.js DRIVE_TUNING GRID/TILE_M) ─────
        public const int Grid = 9;
        public const double TileM = 20.0;
        public const int Center = 4;
        public const int RingMin = 1;
        public const int RingMax = Grid - 2;
        public static readonly (int r, int c) HomeTile = (7, 2), ShopTile = (3, 6), VetTile = (2, 2), ParkTile = (6, 6);

        public struct Landmark
        {
            public string Id;
            public double X, Z;
        }

        public static readonly Landmark[] Landmarks =
        {
            new Landmark { Id = "shop", X = 46.5, Z = -20.0 }, new Landmark { Id = "vetClinic", X = -46.5, Z = -40.0 },
            new Landmark { Id = "fountain", X = 12.0, Z = 12.0 }, new Landmark { Id = "skyTower", X = 20.0, Z = -46.0 },
            new Landmark { Id = "parkGazebo", X = -46.0, Z = 20.0 }, new Landmark { Id = "windmillCafe", X = 20.0, Z = 48.0 },
        };

        public static readonly (int r, int c)[] LandmarkTiles = { (2, 2), (5, 5), (2, 5), (5, 2), (6, 5) };

        public static (double x, double z) TileToWorld(int r, int c) => ((c - Center) * TileM, (r - Center) * TileM);

        public static (int r, int c) WorldToTile(double x, double z) =>
            (FrameworkLogic.JsRound(z / TileM + Center), FrameworkLogic.JsRound(x / TileM + Center));

        public static bool IsRoad(int r, int c)
        {
            if (r < RingMin || r > RingMax || c < RingMin || c > RingMax) return false;
            var onRing = r == RingMin || r == RingMax || c == RingMin || c == RingMax;
            var onCross = r == Center || c == Center;
            return onRing || onCross;
        }

        /// <summary>Tile kinds: "road" | "block" | "rim".</summary>
        public static string[,] BuildGrid()
        {
            var grid = new string[Grid, Grid];
            for (var r = 0; r < Grid; r++)
            for (var c = 0; c < Grid; c++)
            {
                var kind = "rim";
                if (IsRoad(r, c)) kind = "road";
                else if (r >= 1 && r <= RingMax && c >= 1 && c <= RingMax) kind = "block";
                grid[r, c] = kind;
            }
            return grid;
        }

        public struct Box
        {
            public double MinX, MaxX, MinZ, MaxZ;
        }

        public static List<Box> LayoutColliders()
        {
            var outp = new List<Box>();
            var half = TileM * 0.34;
            for (var r = 0; r < Grid; r++)
            for (var c = 0; c < Grid; c++)
            {
                if (IsRoad(r, c) || r < 1 || r > RingMax || c < 1 || c > RingMax) continue;
                if ((r, c) == ShopTile || (r, c) == ParkTile || Array.IndexOf(LandmarkTiles, (r, c)) >= 0) continue;
                var (x, z) = TileToWorld(r, c);
                outp.Add(new Box { MinX = x - half, MaxX = x + half, MinZ = z - half, MaxZ = z + half });
            }
            return outp;
        }

        // ── pure rules ──────────────────────────────────────────────────────

        public static (double x, double y, double z) ParcelArcPos((double x, double y, double z) from, (double x, double y, double z) to, double t, double lift = 2.6)
        {
            var k = Math.Min(1.0, Math.Max(0.0, t));
            if (k == 0.0) return from;
            if (k == 1.0) return to;
            return (from.x + (to.x - from.x) * k, from.y + (to.y - from.y) * k + Math.Sin(Math.PI * k) * lift, from.z + (to.z - from.z) * k);
        }

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var o = tune.Clone();
            o.SpeedMult = hard ? 1.2 : 0.85;
            o.TrafficDensityMult = hard ? 1.15 : 0.85;
            o.CrashAllowance = tune.CrashAllowance + (hard ? 0 : 1);
            o.Endless = mode == "endless";
            o.Mode = mode;
            return o;
        }

        public static Tune WithCoinRate(Tune tune, double coinRate = 1.0)
        {
            var rate = !double.IsNaN(coinRate) && !double.IsInfinity(coinRate) && coinRate > 0.0 ? coinRate : 1.0;
            if (rate == 1.0) return tune;
            var o = tune.Clone();
            o.CoinRate = rate;
            o.CoinIntervalSec = tune.CoinIntervalSec / rate;
            return o;
        }

        public static bool ParcelExpired(double legElapsed, Tune tune) => tune.Endless && legElapsed >= tune.ParcelExpireSec;

        /// <summary>Seeded target pick: PARCELS distinct landmarks (Fisher-Yates); a leading "shop" is rotated to the end.</summary>
        public static List<string> PickDeliveries(GoobyRng rng, IList<string> ids, int count = 3)
        {
            var pool = new List<string>(ids);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = (int)Math.Floor(rng.Next() * (i + 1));
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var picks = pool.GetRange(0, count);
            if (picks.Count > 1 && picks[0] == "shop")
            {
                var first = picks[0];
                picks.RemoveAt(0);
                picks.Add(first);
            }
            return picks;
        }

        public static int PickFragileParcel(GoobyRng rng, int count = 3) => Math.Min(count - 1, (int)Math.Floor(rng.Next() * count));

        public static int FragileCrashPenalty(int fragileIndex, int currentParcel, bool alreadyDamaged) =>
            fragileIndex == currentParcel && !alreadyDamaged ? Delivery.FragileCrashPenalty : 0;

        public static int FragileDeliveryBonus(int fragileIndex, int deliveredParcel, bool damaged) =>
            fragileIndex == deliveredParcel && !damaged ? Delivery.FragileCleanBonus : 0;

        public static int ApplyDrop(int score) => score + Delivery.DropPoints;
        public static int ApplyCrash(int score) => Math.Max(0, score - Delivery.CrashPenalty);
        public static int TimeBonus(double elapsedSec, Tune tune) => Math.Max(0, (int)Math.Floor(tune.TimeBonusFromSec - elapsedSec));

        public sealed class EndlessState
        {
            public int Expired, Limit;
            public bool Ended;
        }

        public static EndlessState CreateEndlessState(int? limit = null) =>
            new EndlessState { Expired = 0, Limit = limit ?? Delivery.EndlessExpiredLimit, Ended = false };

        public static bool RecordExpiry(EndlessState state)
        {
            if (!state.Ended) state.Expired += 1;
            state.Ended = state.Expired >= state.Limit;
            return state.Ended;
        }

        public static int RoundScore(int drops, int crashes, double elapsedSec, Tune tune)
        {
            var score = 0;
            for (var i = 0; i < drops; i++) score = ApplyDrop(score);
            for (var i = 0; i < crashes; i++) score = ApplyCrash(score);
            if (drops >= tune.Parcels) score += TimeBonus(elapsedSec, tune);
            return score;
        }

        public static (double x, double z) DropPoint(double ax, double az, IList<Box> colliders, double clearance = 1.6)
        {
            var x = ax;
            var z = az;
            for (var guard = 0; guard < 4; guard++)
            {
                Box? hit = null;
                foreach (var b in colliders)
                {
                    if (x > b.MinX - clearance && x < b.MaxX + clearance && z > b.MinZ - clearance && z < b.MaxZ + clearance) { hit = b; break; }
                }
                if (!hit.HasValue) break;
                var h = hit.Value;
                var pushes = new[] { (dx: h.MinX - clearance - x, dz: 0.0), (dx: h.MaxX + clearance - x, dz: 0.0), (dx: 0.0, dz: h.MinZ - clearance - z), (dx: 0.0, dz: h.MaxZ + clearance - z) };
                var best = pushes[0];
                foreach (var p in pushes)
                    if (Math.Abs(p.dx) + Math.Abs(p.dz) < Math.Abs(best.dx) + Math.Abs(best.dz)) best = p;
                x += best.dx;
                z += best.dz;
            }
            return (x, z);
        }

        public static bool SegmentHitsDrop(double fx, double fz, double tx, double tz, double cx, double cz, double radius = 4.0)
        {
            var dx = tx - fx;
            var dz = tz - fz;
            var len2 = dx * dx + dz * dz;
            var t = 0.0;
            if (len2 > 0.0) t = Math.Max(0.0, Math.Min(1.0, ((cx - fx) * dx + (cz - fz) * dz) / len2));
            var x = fx + dx * t;
            var z = fz + dz * t;
            var ox = cx - x;
            var oz = cz - z;
            return Math.Sqrt(ox * ox + oz * oz) <= radius;
        }

        public static (int r, int c) NearestRoadTile(string[,] grid, int r, int c)
        {
            var best = (-1, -1);
            var bestD = double.PositiveInfinity;
            for (var rr = 0; rr < grid.GetLength(0); rr++)
            for (var cc = 0; cc < grid.GetLength(1); cc++)
            {
                if (grid[rr, cc] != "road") continue;
                var d = (double)((rr - r) * (rr - r) + (cc - c) * (cc - c));
                if (d < bestD) { bestD = d; best = (rr, cc); }
            }
            return best;
        }

        /// <summary>Shortest road path between two road tiles (4-neighbour BFS); empty = no path.</summary>
        public static List<(int r, int c)> RoadPathBetween(string[,] grid, (int r, int c) from, (int r, int c) to)
        {
            var empty = new List<(int, int)>();
            var rows = grid.GetLength(0);
            if (rows == 0) return empty;
            var cols = grid.GetLength(1);
            if (!IsRoadCell(grid, from) || !IsRoadCell(grid, to)) return empty;
            var prev = new Dictionary<int, int> { { from.r * cols + from.c, -1 } };
            var queue = new List<(int r, int c)> { from };
            var head = 0;
            while (head < queue.Count)
            {
                var cur = queue[head++];
                if (cur == to) break;
                foreach (var step in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    var nxt = (r: cur.r + step.Item1, c: cur.c + step.Item2);
                    if (nxt.r < 0 || nxt.r >= rows || nxt.c < 0 || nxt.c >= cols) continue;
                    var nkey = nxt.r * cols + nxt.c;
                    if (!IsRoadCell(grid, nxt) || prev.ContainsKey(nkey)) continue;
                    prev[nkey] = cur.r * cols + cur.c;
                    queue.Add(nxt);
                }
            }
            var toKey = to.r * cols + to.c;
            if (!prev.ContainsKey(toKey)) return empty;
            var path = new List<(int, int)>();
            var walk = toKey;
            while (walk != -1)
            {
                path.Insert(0, (walk / cols, walk % cols));
                walk = prev[walk];
            }
            return path;
        }

        private static bool IsRoadCell(string[,] grid, (int r, int c) tile) =>
            tile.r >= 0 && tile.r < grid.GetLength(0) && tile.c >= 0 && tile.c < grid.GetLength(1) && grid[tile.r, tile.c] == "road";

        /// <summary>Deterministic arcade certification model (closed form like the web).</summary>
        public static JObject SimulateAutoplay(int seed, string mode = "normal", double coinRate = 1.0)
        {
            var tune = WithCoinRate(ApplyDifficulty(Delivery, mode), coinRate);
            var jitter = (seed % 7) - 3;
            var elapsed = 41.0 + jitter;
            if (mode == "easy") elapsed = 32.0 + jitter;
            else if (mode == "hard" || mode == "endless") elapsed = 49.0 + jitter;
            var crashes = mode == "easy" ? 0 : (mode == "hard" || mode == "endless" ? 2 : 1);
            var coinPoints = 0;
            if (tune.CoinRate > 1.0) coinPoints = (int)Math.Floor(elapsed / tune.CoinIntervalSec) * tune.CoinPoints;
            var score = RoundScore(tune.Parcels, crashes, elapsed, tune) + coinPoints;
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["elapsed"] = elapsed, ["crashes"] = crashes, ["coinPoints"] = coinPoints };
        }
    }
}
