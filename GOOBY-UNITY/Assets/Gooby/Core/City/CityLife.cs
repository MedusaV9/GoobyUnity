using System;
using System.Collections.Generic;

namespace Gooby.Core.City
{
    /// <summary>
    /// How lively Sonnental is at a given hour (port of CityRhythmus, W18/J4): piecewise-linear density curves for the
    /// ambient cars and the strolling Goobys — quiet in the morning, full at noon, golden in the evening, a few night
    /// owls after ten. The scene builds the day's maximum once and shows the hour's share of it.
    /// </summary>
    public static class CityRhythm
    {
        // the Godot numbers (9 cars / 11 walkers at noon) scaled up: our loops run in both directions, so the fleet is spread thinner
        private static readonly (double hour, double count)[] TrafficAnchors = { (0, 6), (5, 6), (8, 12), (11, 22), (16, 22), (19, 16), (22, 6) };
        private static readonly (double hour, double count)[] WalkerAnchors = { (0, 5), (5, 5), (8, 9), (11, 18), (16, 18), (19, 12), (22, 5) };

        public static int TrafficCount(double hour) => (int)Math.Round(Curve(TrafficAnchors, hour));
        public static int WalkerCount(double hour) => (int)Math.Round(Curve(WalkerAnchors, hour));
        public static int TrafficMax => Max(TrafficAnchors);
        public static int WalkerMax => Max(WalkerAnchors);

        /// <summary>After nightfall the lights blink amber and stop regulating (small-town mode).</summary>
        public static bool LightsBlink(double hour) => Time.DayPhase.LampsOn(hour);

        /// <summary>Godot CityRhythmus.zeitungs_gooby_aktiv: a pavement Gooby reads the paper during the morning window.</summary>
        public static bool NewspaperGooby(double hour) => Time.DayPhase.Of(hour) == Time.DayPhase.Morgen;

        private static int Max((double hour, double count)[] anchors)
        {
            var top = 0.0;
            foreach (var a in anchors) top = Math.Max(top, a.count);
            return (int)Math.Round(top);
        }

        private static double Curve((double hour, double count)[] anchors, double hour)
        {
            var h = hour - Math.Floor(hour / 24.0) * 24.0;
            var last = anchors[anchors.Length - 1];
            if (h >= last.hour) return last.count;
            if (h <= anchors[0].hour) return anchors[0].count;
            for (var i = 1; i < anchors.Length; i++)
            {
                if (h > anchors[i].hour) continue;
                var a = anchors[i - 1]; var b = anchors[i];
                var t = (h - a.hour) / Math.Max(0.0001, b.hour - a.hour);
                return a.count + (b.count - a.count) * t;
            }
            return last.count;
        }
    }

    /// <summary>
    /// Near-miss horn/spark rules (port of Godot CityAmbiente + NearMissFunken): a traffic Gooby honks when the
    /// player passes within NearMissM while moving at least NearMissSpeed, then stays quiet for NearMissPauseS.
    /// </summary>
    public static class CityAmbiente
    {
        public const double NearMissM = 7.0;
        public const double NearMissSpeed = 4.0;     // m/s
        public const double NearMissPauseS = 4.0;
        public const double SparkHeightM = 0.35;

        public static bool IsNearMiss(double distanceM, double speedMs) =>
            distanceM <= NearMissM && Math.Abs(speedMs) >= NearMissSpeed;

        public static bool ShouldSpark(double distanceM, double speedMs, bool reducedMotion) =>
            !reducedMotion && IsNearMiss(distanceM, speedMs);

        public static (double x, double y, double z) SparkPoint(double px, double pz, double ox, double oz) =>
            ((px + ox) * 0.5, SparkHeightM, (pz + oz) * 0.5);
    }

    /// <summary>
    /// Cashier cadence (port of Godot KassenNpc): a tap/babble every TapEveryS (twice as slow with reduced
    /// motion) and a friendly wave every WaveEveryS. Pure clocks so the interior scene and the tests agree.
    /// </summary>
    public static class CashierNpc
    {
        public const double TapEveryS = 2.4;
        public const double WaveEveryS = 11.0;

        /// <summary>
        /// Advances the tap/wave accumulators. Returns "tap", "wave" or null. When both fire in the same
        /// step the wave wins (Godot's letzte_aktion). Reduced motion skips the automatic wave.
        /// </summary>
        public static string Step(ref double tapAcc, ref double waveAcc, double dt, bool reducedMotion)
        {
            if (dt <= 0.0) return null;
            tapAcc += dt;
            waveAcc += dt;
            string action = null;
            var tapEvery = TapEveryS * (reducedMotion ? 2.0 : 1.0);
            if (tapAcc >= tapEvery) { tapAcc = 0.0; action = "tap"; }
            if (!reducedMotion && waveAcc >= WaveEveryS) { waveAcc = 0.0; action = "wave"; }
            return action;
        }
    }

    /// <summary>
    /// Ambient traffic (port of CityVerkehr + the polyline maths of CityRoadGraph): cars circle the plan's traffic loops
    /// in the right-hand lane — every loop in both directions — hold at red lights and behind the car in front, and
    /// ease off for the player's car. The lights at the three- and four-way junctions run a 12-second cycle with an
    /// all-red buffer; at night they blink amber. Pure numbers: the scene only moves the meshes.
    /// </summary>
    public static class Traffic
    {
        public const double Speed = 8.0;            // m/s — a touch above carFeel's TRAFFIC_SPEED (6.5): the player's 42 km/h still catches up, but not at every block
        public const double CycleS = 12.0, BufferS = 1.5;
        public const double LookAheadM = 8.0;       // how far ahead a car looks for a red light
        public const double Accel = 4.0, Decel = 10.0;
        public const double MinGapM = 8.0;          // arc distance kept to the car in front
        public const double PlayerGapM = 13.0;      // … and behind the player's car: the chase camera rides 8 m behind it
        public const double BlinkS = 0.9;

        public sealed class Loop
        {
            public readonly List<(double x, double z)> Points = new List<(double, double)>();
            public double Length;
            public bool Reversed;

            /// <summary>Point and unit direction at arc length s (wraps).</summary>
            public (double x, double z, double dx, double dz) At(double s)
            {
                var n = Points.Count;
                if (n == 0) return (0, 0, 0, 1);
                if (Length > 0) s = s - Math.Floor(s / Length) * Length;
                var acc = 0.0;
                for (var i = 0; i < n; i++)
                {
                    var a = Points[i]; var b = Points[(i + 1) % n];
                    var seg = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                    if (seg <= 1e-9) continue;
                    if (acc + seg >= s || i == n - 1)
                    {
                        var f = Math.Min(1.0, Math.Max(0.0, (s - acc) / seg));
                        return (a.x + (b.x - a.x) * f, a.z + (b.z - a.z) * f, (b.x - a.x) / seg, (b.z - a.z) / seg);
                    }
                    acc += seg;
                }
                var last = Points[n - 1];
                return (last.x, last.z, 0, 1);
            }
        }

        public sealed class Car
        {
            public Loop Loop;
            public double S;        // arc position on the loop (m)
            public double Speed;    // m/s
            public int Model;       // body variant index (scene decides what that means)
            public int Tint;        // paint index
            public bool Active;     // shown at this hour
        }

        /// <summary>
        /// The loops as world polylines in the right-hand lane. Each authored rectangle yields two loops: the authored
        /// direction and the reverse, each shifted LaneOffset to the right of its travel direction, so both lanes of a
        /// through road carry traffic.
        /// </summary>
        public static List<Loop> Loops()
        {
            var outp = new List<Loop>();
            foreach (var corners in CityGrid.TrafficLoops)
            {
                var centre = new List<(double x, double z)>();
                foreach (var c in corners) if (c.Length >= 2) centre.Add(CityGrid.World(c[0], c[1]));
                if (centre.Count < 3) continue;
                outp.Add(Offset(centre, false));
                var back = new List<(double x, double z)>(centre);
                back.Reverse();
                outp.Add(Offset(back, true));
            }
            return outp;
        }

        private static Loop Offset(List<(double x, double z)> centre, bool reversed)
        {
            var loop = new Loop { Reversed = reversed };
            var n = centre.Count;
            for (var i = 0; i < n; i++)
            {
                var prev = centre[(i - 1 + n) % n]; var here = centre[i]; var next = centre[(i + 1) % n];
                var (ix, iz) = Unit(here.x - prev.x, here.z - prev.z);
                var (ox, oz) = Unit(next.x - here.x, next.z - here.z);
                var tile = CityGrid.Tile(here.x, here.z);
                if (CityGrid.IsRoundabout(tile.r, tile.c))
                {
                    // the corner is a roundabout: in on the right lane, round the island, out on the right lane
                    RoundaboutArc(loop, here, (ix, iz), (ox, oz));
                }
                else
                {
                    // right-hand normals of the incoming and outgoing legs; their sum shifts a right-angle corner onto the lane
                    var rx = iz + oz; var rz = -ix - ox;
                    loop.Points.Add((here.x + rx * CityGrid.LaneOffset, here.z + rz * CityGrid.LaneOffset));
                }
                // roundabouts on the leg to the next corner: the through road bends round their islands too
                var (lx, lz) = (ox, oz);
                var legTiles = (int)Math.Round(Math.Max(Math.Abs(next.x - here.x), Math.Abs(next.z - here.z)) / CityGrid.TileM);
                for (var k = 1; k < legTiles; k++)
                {
                    var (px, pz) = (here.x + lx * CityGrid.TileM * k, here.z + lz * CityGrid.TileM * k);
                    var t = CityGrid.Tile(px, pz);
                    if (CityGrid.IsRoundabout(t.r, t.c)) RoundaboutArc(loop, CityGrid.World(t.r, t.c), (lx, lz), (lx, lz));
                }
            }
            loop.Length = 0;
            var m = loop.Points.Count;
            for (var i = 0; i < m; i++)
            {
                var a = loop.Points[i]; var b = loop.Points[(i + 1) % m];
                loop.Length += Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
            }
            return loop;
        }

        /// <summary>
        /// Round the island of a roundabout at <paramref name="centre"/>: the right-hand lane of the incoming leg meets
        /// the ring at the entry point, the loop follows the ring counter-clockwise (the island on the left, as in
        /// right-hand traffic) to where the right-hand lane of the outgoing leg leaves it — a short arc for a right
        /// turn, a half circle straight through, three quarters for a left turn.
        /// </summary>
        private static void RoundaboutArc(Loop loop, (double x, double z) centre, (double x, double z) din, (double x, double z) dout)
        {
            var ring = CityGrid.RingRadius;
            var lane = CityGrid.LaneOffset;
            var reach = Math.Sqrt(Math.Max(0.0, ring * ring - lane * lane));
            // right-hand normals of the legs
            var (rinX, rinZ) = (din.z, -din.x);
            var (routX, routZ) = (dout.z, -dout.x);
            var entry = (x: centre.x + rinX * lane - din.x * reach, z: centre.z + rinZ * lane - din.z * reach);
            var exit = (x: centre.x + routX * lane + dout.x * reach, z: centre.z + routZ * lane + dout.z * reach);
            var a0 = Math.Atan2(entry.z - centre.z, entry.x - centre.x);
            var a1 = Math.Atan2(exit.z - centre.z, exit.x - centre.x);
            var sweep = a1 - a0;
            while (sweep <= 1e-9) sweep += 2.0 * Math.PI;     // counter-clockwise from the entry to the exit
            var steps = Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 12.0)));
            for (var i = 0; i <= steps; i++)
            {
                var a = a0 + sweep * i / steps;
                loop.Points.Add((centre.x + Math.Cos(a) * ring, centre.z + Math.Sin(a) * ring));
            }
        }

        private static (double x, double z) Unit(double x, double z)
        {
            var l = Math.Sqrt(x * x + z * z);
            return l < 1e-9 ? (0, 1) : (x / l, z / l);
        }

        /// <summary>Every junction with three or more arms carries lights; the roundabouts regulate themselves.</summary>
        public static List<(int r, int c)> LightTiles()
        {
            var outp = new List<(int, int)>();
            foreach (var (r, c) in CityGrid.StreetTiles)
                if (!CityGrid.IsRoundabout(r, c) && CityGrid.Arms(r, c) >= 3) outp.Add((r, c));
            outp.Sort();
            return outp;
        }

        /// <summary>Which axis has green at scene time t: first half north/south, second half east/west, all-red buffer at each end.</summary>
        public static (bool nsGreen, bool ewGreen) LightState(double time)
        {
            var t = time - Math.Floor(time / CycleS) * CycleS;
            var half = CycleS / 2.0;
            return (t < half - BufferS, t >= half && t < CycleS - BufferS);
        }

        /// <summary>Amber blink phase at night: on for BlinkS, off for BlinkS.</summary>
        public static bool BlinkOn(double time) => time - Math.Floor(time / (BlinkS * 2.0)) * (BlinkS * 2.0) < BlinkS;

        /// <summary>A red light for this heading within LookAheadM — never while already standing on the junction.</summary>
        public static bool RedAhead(double x, double z, double dx, double dz, double time, HashSet<(int r, int c)> lights)
        {
            if (lights == null || lights.Count == 0) return false;
            var len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) return false;
            dx /= len; dz /= len;
            var target = CityGrid.Tile(x + dx * LookAheadM, z + dz * LookAheadM);
            if (!lights.Contains(target)) return false;
            if (CityGrid.Tile(x, z) == target) return false;
            var ew = Math.Abs(dx) > Math.Abs(dz);
            var (ns, ewGreen) = LightState(time);
            return !(ew ? ewGreen : ns);
        }

        /// <summary>A red light for this car's direction within LookAheadM — never while it already stands on the junction.</summary>
        public static bool RedAhead(Car car, double time, HashSet<(int r, int c)> lights)
        {
            if (car.Loop == null) return false;
            var here = car.Loop.At(car.S);
            return RedAhead(here.x, here.z, here.dx, here.dz, time, lights);
        }

        /// <summary>One step: target speed from the light and the gap ahead, integrated with gentle accel / firm braking.</summary>
        public static void Step(Car car, double dt, double time, HashSet<(int r, int c)> lights, double gapAheadM)
        {
            if (car.Loop == null || car.Loop.Length <= 0) return;
            var target = Speed;
            if (gapAheadM < MinGapM || RedAhead(car, time, lights)) target = 0.0;
            if (car.Speed < target) car.Speed = Math.Min(target, car.Speed + Accel * dt);
            else car.Speed = Math.Max(target, car.Speed - Decel * dt);
            car.S += car.Speed * dt;
            car.S -= Math.Floor(car.S / car.Loop.Length) * car.Loop.Length;
        }

        /// <summary>Arc distance to the next car ahead on the same loop (∞ when the road is clear).</summary>
        public static double GapAhead(Car car, IEnumerable<Car> others)
        {
            var best = double.PositiveInfinity;
            if (car.Loop == null) return best;
            var len = car.Loop.Length;
            foreach (var o in others)
            {
                if (ReferenceEquals(o, car) || !o.Active || !ReferenceEquals(o.Loop, car.Loop)) continue;
                var d = o.S - car.S;
                d -= Math.Floor(d / len) * len;
                if (d > 0.01 && d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// The player's car as an obstacle: the arc distance at which the loop passes within <paramref name="radius"/>
        /// of the player (sampled every 2 m up to PlayerGapM + LookAheadM), ∞ when the way is clear. Measured against
        /// MinGapM like any other gap, so the extra PlayerGapM − MinGapM is taken off: ambient cars hold PlayerGapM
        /// behind the player and stay out of the chase camera.
        /// </summary>
        public static double GapToPlayer(Car car, double px, double pz, double radius = 2.4) => GapToPlayer(car, px, pz, 0.0, 0.0, radius);

        /// <summary>Radius around the player's predicted path for the yielding look-ahead — under the 4 m between the two lanes.</summary>
        public const double YieldRadiusM = 3.0;
        /// <summary>Half the time window (s) around the meeting moment in which the player's car still counts as being there.</summary>
        public const double YieldWindowS = 0.8;

        /// <summary>
        /// As above, but the player is moving at (<paramref name="vx"/>, <paramref name="vz"/>) m/s: each sample of the
        /// loop is also checked against where the player will be when this car gets there (at its own speed, at least
        /// walking pace) — the stretch of the player's path from YieldWindowS before to after that moment, within
        /// YieldRadiusM. A car rolling towards a junction the player is about to cross gives way instead of meeting the
        /// player's car in the middle of it. Only a crossing player is anticipated this way: oncoming traffic is in the
        /// other lane, and a faster player behind us must not make us brake in front of them.
        /// </summary>
        public static double GapToPlayer(Car car, double px, double pz, double vx, double vz, double radius = 2.4)
        {
            if (car.Loop == null) return double.PositiveInfinity;
            var speed = Math.Sqrt(vx * vx + vz * vz);
            var moving = speed > 0.5;
            var v = Math.Max(car.Speed, 2.0);
            for (var d = 1.0; d <= PlayerGapM + LookAheadM; d += 2.0)
            {
                var p = car.Loop.At(car.S + d);
                var dx = p.x - px; var dz = p.z - pz;
                if (dx * dx + dz * dz <= radius * radius) return d - (PlayerGapM - MinGapM);
                // only a player crossing our path is anticipated: one driving against us is in the other lane, one
                // driving our way is behind us (a follower) or ahead (a leader we hold the gap to by position alone)
                var along = (vx * p.dx + vz * p.dz) / speed;
                if (!moving || along < -0.5 || along > 0.7) continue;
                var t = d / v;
                var t0 = Math.Max(0.0, t - YieldWindowS); var t1 = t + YieldWindowS;
                var ax = px + vx * t0; var az = pz + vz * t0;
                var bx = px + vx * t1; var bz = pz + vz * t1;
                if (DistanceToSegment(p.x, p.z, ax, az, bx, bz) <= YieldRadiusM) return d - (PlayerGapM - MinGapM);
            }
            return double.PositiveInfinity;
        }

        private static double DistanceToSegment(double x, double z, double ax, double az, double bx, double bz)
        {
            var ex = bx - ax; var ez = bz - az;
            var len2 = ex * ex + ez * ez;
            var f = len2 <= 1e-9 ? 0.0 : Clamp01(((x - ax) * ex + (z - az) * ez) / len2);
            var cx = ax + ex * f - x; var cz = az + ez * f - z;
            return Math.Sqrt(cx * cx + cz * cz);
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

        /// <summary>The fleet: the day's maximum, spread over the loops with staggered starts; the hour decides who is out.</summary>
        public static List<Car> Fleet(List<Loop> loops, double hour, int seed)
        {
            var outp = new List<Car>();
            if (loops == null || loops.Count == 0) return outp;
            var rng = new System.Random(seed);
            var max = CityRhythm.TrafficMax;
            var active = CityRhythm.TrafficCount(hour);
            var count = new int[loops.Count];
            for (var i = 0; i < max; i++)
            {
                // the loop with the fewest cars per metre of lane: an even density over the whole town
                var pick = 0; var best = double.MaxValue;
                for (var k = 0; k < loops.Count; k++)
                {
                    var density = (count[k] + 1) / Math.Max(1.0, loops[k].Length);
                    if (density < best) { best = density; pick = k; }
                }
                count[pick]++;
                var loop = loops[pick];
                outp.Add(new Car
                {
                    Loop = loop,
                    S = (count[pick] * loop.Length / 3.0 + rng.NextDouble() * 40.0) % Math.Max(1.0, loop.Length),
                    Speed = Speed,
                    Model = i % 4,
                    Tint = i,
                    Active = i < active,
                });
            }
            return outp;
        }

        /// <summary>Show the hour's share of the fleet (the first n cars); returns true when anything changed.</summary>
        public static bool SetHour(List<Car> fleet, double hour)
        {
            var n = Math.Min(fleet.Count, CityRhythm.TrafficCount(hour));
            var changed = false;
            for (var i = 0; i < fleet.Count; i++)
            {
                var want = i < n;
                if (fleet[i].Active != want) { fleet[i].Active = want; changed = true; }
            }
            return changed;
        }
    }

    /// <summary>
    /// The strolling Goobys of the pavements (port of CityFussgaenger): each walker owns a stretch of pavement along a
    /// straight street run — the stretches in front of the places first, so the shops look visited — and walks it back
    /// and forth, pausing at both ends to look at the shop windows (some wave). Ends keep clear of the junction squares
    /// so nobody stands in the road. Pure numbers; the scene animates the avatars.
    /// </summary>
    public static class Pedestrians
    {
        /// <summary>Lateral offset of the pavement's middle from the street's centre line.</summary>
        public const double PavementOffset = CityGrid.RoadHalfWidth + CityGrid.WalkWidth * 0.5;
        /// <summary>Route ends stay this far from a junction tile's centre (past the perpendicular arm and its zebra crossing).</summary>
        public const double JunctionClearM = CityGrid.RoadHalfWidth + CityGrid.WalkWidth + 0.6;
        public const double SpeedMin = 0.9, SpeedMax = 1.6;   // m/s — Gooby strolls
        public const double PauseMinS = 2.5, PauseMaxS = 7.0;
        public const double WaveShare = 0.4;
        public const double RouteMinM = 14.0, RouteMaxM = 38.0;
        public const int MaxWalkers = 18;

        /// <summary>Fur ids of the passers-by (cosmetics catalog) — never the player's own cream.</summary>
        public static readonly string[] Furs = { "golden", "fell_minze", "fell_himmel", "rose", "caramel", "fell_lavendel", "fell_pfirsich", "ash", "fell_zuckerwatte" };
        /// <summary>Hats some of them wear (index −1 = bare).</summary>
        public static readonly string[] Hats = { "beanie", "cap", "strawHat", "beret", "flowerCrown", "hut_melone" };

        public sealed class Route
        {
            public double FromX, FromZ, ToX, ToZ;
            public double Length, Speed, Phase, PauseS;
            public double Look;        // heading (rad) towards the houses while pausing
            public bool Waves, ShopWindow, Newspaper, Active;
            public string Fur, Hat;
        }

        public struct Pose
        {
            public double X, Z, Heading;
            public bool Standing, Waving;
        }

        private sealed class Run
        {
            public double X0, Z0, DirX, DirZ, Length;
            public readonly List<double> Junctions = new List<double>();   // arc positions of junction tiles along the run
        }

        /// <summary>Straight runs of consecutive street tiles along the rows and the columns (two tiles or longer).</summary>
        private static List<Run> Runs()
        {
            var outp = new List<Run>();
            for (var r = 0; r < CityGrid.Rows; r++) Collect(outp, r, true);
            for (var c = 0; c < CityGrid.Cols; c++) Collect(outp, c, false);
            return outp;
        }

        private static void Collect(List<Run> outp, int fixedIndex, bool row)
        {
            var n = row ? CityGrid.Cols : CityGrid.Rows;
            var start = -1;
            for (var i = 0; i <= n; i++)
            {
                var street = i < n && (row ? CityGrid.IsStreet(fixedIndex, i) : CityGrid.IsStreet(i, fixedIndex));
                if (street && start < 0) start = i;
                if (street) continue;
                if (start >= 0 && i - start >= 2)
                {
                    var (x0, z0) = row ? CityGrid.World(fixedIndex, start) : CityGrid.World(start, fixedIndex);
                    var (x1, z1) = row ? CityGrid.World(fixedIndex, i - 1) : CityGrid.World(i - 1, fixedIndex);
                    var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                    var run = new Run { X0 = x0, Z0 = z0, DirX = (x1 - x0) / len, DirZ = (z1 - z0) / len, Length = len };
                    for (var k = start; k < i; k++)
                    {
                        // any tile with an arm leaving the run sideways (crossroads, T, bend, spur) has asphalt where the pavement would be
                        var (r, c) = row ? (fixedIndex, k) : (k, fixedIndex);
                        var sideways = false;
                        foreach (var (dr, dc) in CityGrid.StreetNeighbours(r, c)) if (row ? dr != 0 : dc != 0) sideways = true;
                        if (sideways || CityGrid.IsRoundabout(r, c)) run.Junctions.Add((k - start) * CityGrid.TileM);
                    }
                    outp.Add(run);
                }
                start = -1;
            }
        }

        private static bool Clear(Run run, double s)
        {
            if (s < 0 || s > run.Length) return false;
            foreach (var j in run.Junctions) if (Math.Abs(s - j) < JunctionClearM) return false;
            return true;
        }

        /// <summary>The day's walkers (WalkerMax routes, deterministic for the seed); the hour decides who is out.</summary>
        public static List<Route> Routes(double hour, int seed)
        {
            var outp = new List<Route>();
            var runs = Runs();
            if (runs.Count == 0) return outp;
            var rng = new System.Random(seed);
            var want = Math.Min(MaxWalkers, CityRhythm.WalkerMax);
            var active = CityRhythm.WalkerCount(hour);

            // shop-window stretches first: the pavement in front of every place's door, in a shuffled order
            var places = new List<DriveModel.Place>();
            foreach (var p in DriveModel.Places) if (p.Id != "home") places.Add(p);
            for (var i = places.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); var tmp = places[i]; places[i] = places[j]; places[j] = tmp; }
            var shopRoutes = Math.Min(places.Count, (want + 1) / 2);
            for (var i = 0; i < shopRoutes && outp.Count < want; i++)
            {
                var p = places[i];
                var (sx, sz) = CityGrid.World(p.StreetTile[0], p.StreetTile[1]);
                var run = RunThrough(runs, sx, sz, p.FaceX, p.FaceZ);
                if (run == null) continue;
                // the pavement on the door's side of the street: shift against the door→street direction
                var side = Math.Sign(-(p.FaceX * (run.DirZ) - p.FaceZ * (run.DirX)));
                if (side == 0) side = 1;
                var s0 = (sx - run.X0) * run.DirX + (sz - run.Z0) * run.DirZ;
                var half = 7.0 + rng.NextDouble() * 4.0;
                var a = s0 - half; var b = s0 + half;
                for (var tries = 0; tries < 10 && !(Clear(run, a) && Clear(run, b)); tries++) { a += 1.5; b -= 1.5; }
                if (!(Clear(run, a) && Clear(run, b))) continue;
                outp.Add(Make(rng, run, a, b, side, true));
            }
            // the rest of the town: random stretches of random runs
            var guard = 0;
            while (outp.Count < want && guard++ < 400)
            {
                var run = runs[rng.Next(runs.Count)];
                var len = RouteMinM + rng.NextDouble() * (RouteMaxM - RouteMinM);
                if (len > run.Length) len = run.Length;
                var a = rng.NextDouble() * (run.Length - len);
                var b = a + len;
                if (!Clear(run, a) || !Clear(run, b)) continue;
                outp.Add(Make(rng, run, a, b, rng.Next(2) == 0 ? -1 : 1, false));
            }
            for (var i = 0; i < outp.Count; i++) outp[i].Active = i < active;
            // Godot: the first pavement Gooby is the morning newspaper reader (prop only in the morning pause)
            if (outp.Count > 0) outp[0].Newspaper = true;
            return outp;
        }

        /// <summary>Morning + this route owns the paper + standing at a pause: idle into the page, no wave.</summary>
        public static bool ReadingPaper(Route r, Pose pose, double hour) =>
            r != null && r.Newspaper && pose.Standing && CityRhythm.NewspaperGooby(hour);

        private static Run RunThrough(List<Run> runs, double x, double z, double faceX, double faceZ)
        {
            // the run that passes through the street tile in front of the door and runs ALONG the door's street
            // (perpendicular to the door→street direction)
            Run best = null; var bestD = double.MaxValue;
            foreach (var run in runs)
            {
                var s = (x - run.X0) * run.DirX + (z - run.Z0) * run.DirZ;
                if (s < -1 || s > run.Length + 1) continue;
                var px = run.X0 + run.DirX * s; var pz = run.Z0 + run.DirZ * s;
                var d = Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                var along = Math.Abs(run.DirX * faceX + run.DirZ * faceZ);   // 0 = the run is perpendicular to the door direction
                if (d < 1.0 && along < 0.5 && d < bestD) { best = run; bestD = d; }
            }
            return best;
        }

        private static Route Make(System.Random rng, Run run, double a, double b, int side, bool shop)
        {
            // right-hand normal of the run direction; `side` picks the pavement
            var nx = run.DirZ * side; var nz = -run.DirX * side;
            var route = new Route
            {
                FromX = run.X0 + run.DirX * a + nx * PavementOffset, FromZ = run.Z0 + run.DirZ * a + nz * PavementOffset,
                ToX = run.X0 + run.DirX * b + nx * PavementOffset, ToZ = run.Z0 + run.DirZ * b + nz * PavementOffset,
                Speed = SpeedMin + rng.NextDouble() * (SpeedMax - SpeedMin),
                Phase = rng.NextDouble(),
                PauseS = PauseMinS + rng.NextDouble() * (PauseMaxS - PauseMinS),
                Waves = rng.NextDouble() < WaveShare,
                Look = Math.Atan2(nx, nz),
                ShopWindow = shop,
                Fur = Furs[rng.Next(Furs.Length)],
                Hat = rng.NextDouble() < 0.45 ? Hats[rng.Next(Hats.Length)] : null,
            };
            route.Length = Math.Max(0.001, Math.Abs(b - a));
            return route;
        }

        /// <summary>Show the hour's share of the walkers (the first n routes); returns true when anything changed.</summary>
        public static bool SetHour(List<Route> routes, double hour)
        {
            var n = Math.Min(routes.Count, CityRhythm.WalkerCount(hour));
            var changed = false;
            for (var i = 0; i < routes.Count; i++)
            {
                var want = i < n;
                if (routes[i].Active != want) { routes[i].Active = want; changed = true; }
            }
            return changed;
        }

        /// <summary>Where a walker is after <paramref name="seconds"/>: walk → pause (look at the houses, maybe wave) → walk back → pause → …</summary>
        public static Pose At(Route r, double seconds)
        {
            var walk = r.Length / Math.Max(0.001, r.Speed);
            var cycle = 2.0 * (walk + r.PauseS);
            var t = seconds + r.Phase * cycle;
            t -= Math.Floor(t / cycle) * cycle;
            var fwdHeading = Math.Atan2(r.ToX - r.FromX, r.ToZ - r.FromZ);
            var backHeading = Math.Atan2(r.FromX - r.ToX, r.FromZ - r.ToZ);
            if (t < walk)
            {
                var f = t / walk;
                return new Pose { X = r.FromX + (r.ToX - r.FromX) * f, Z = r.FromZ + (r.ToZ - r.FromZ) * f, Heading = fwdHeading };
            }
            if (t < walk + r.PauseS) return new Pose { X = r.ToX, Z = r.ToZ, Heading = r.Look, Standing = true, Waving = r.Waves };
            if (t < 2.0 * walk + r.PauseS)
            {
                var f = (t - walk - r.PauseS) / walk;
                return new Pose { X = r.ToX + (r.FromX - r.ToX) * f, Z = r.ToZ + (r.FromZ - r.ToZ) * f, Heading = backHeading };
            }
            return new Pose { X = r.FromX, Z = r.FromZ, Heading = r.Look, Standing = true, Waving = r.Waves };
        }
    }
}
