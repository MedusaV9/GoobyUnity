using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Life on the streets: traffic loops in the right-hand lane, lights at the junctions, walkers on the pavements, density by hour.</summary>
    public class CityLifeTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            CityMap.Reset();
            CityGrid.Reset();
            DriveModel.Reset();
        }

        [Test]
        public void TheRhythmIsQuietAtNightAndFullAtNoon()
        {
            Assert.That(CityRhythm.TrafficCount(3.0), Is.EqualTo(6));
            Assert.That(CityRhythm.TrafficCount(13.0), Is.EqualTo(22));
            Assert.That(CityRhythm.TrafficCount(23.5), Is.EqualTo(6), "after the last anchor the night level holds");
            Assert.That(CityRhythm.WalkerCount(2.0), Is.EqualTo(5));
            Assert.That(CityRhythm.WalkerCount(12.0), Is.EqualTo(18));
            Assert.That(CityRhythm.WalkerCount(9.8), Is.EqualTo(14), "on the morning ramp (9 → 18 between 8 and 11)");
            Assert.That(CityRhythm.TrafficMax, Is.EqualTo(22));
            Assert.That(CityRhythm.WalkerMax, Is.EqualTo(18));
            Assert.That(CityRhythm.LightsBlink(23.0), Is.True);
            Assert.That(CityRhythm.LightsBlink(12.0), Is.False);
        }

        [Test]
        public void TheLoopsComeFromThePlanAndRunInTheRightHandLaneBothWays()
        {
            Assert.That(CityGrid.TrafficLoops.Count, Is.EqualTo(6));
            var loops = Traffic.Loops();
            Assert.That(loops.Count, Is.EqualTo(12), "every authored rectangle in both directions");
            foreach (var loop in loops)
            {
                Assert.That(loop.Points.Count, Is.GreaterThanOrEqualTo(4), "four corners, plus the ring points of any roundabout on the way");
                Assert.That(loop.Length, Is.GreaterThan(100.0));
                // every point along the loop lies on the asphalt (the clamp never has to move it) …
                for (var s = 0.0; s < loop.Length; s += 3.0)
                {
                    var p = loop.At(s);
                    var (cx, cz, hit) = CityGrid.ClampToStreets(p.x, p.z, 0.5);
                    Assert.That(hit, Is.False, $"loop point ({p.x:F1}, {p.z:F1}) at s={s:F0} leaves the road");
                    // … and to the right of the tile's centre line in the direction of travel
                    var tile = CityGrid.Tile(p.x, p.z);
                    var (tx, tz) = CityGrid.World(tile.r, tile.c);
                    var rightX = p.dz; var rightZ = -p.dx;
                    var lateral = (p.x - tx) * rightX + (p.z - tz) * rightZ;
                    if ((System.Math.Abs(p.dx) > 0.99 || System.Math.Abs(p.dz) > 0.99) && !CityGrid.IsRoundabout(tile.r, tile.c))
                        Assert.That(lateral, Is.EqualTo(CityGrid.LaneOffset).Within(0.01), "right-hand lane");
                }
            }
            // the first authored loop is a counter-clockwise rectangle (west along row 1 first); its twin runs clockwise
            var first = loops[0].At(1.0);
            Assert.That(first.dx, Is.LessThan(-0.99));
            Assert.That(loops[1].Reversed, Is.True);
            Assert.That(SignedArea(loops[0]) * SignedArea(loops[1]), Is.LessThan(0.0), "opposite sense of rotation");
            Assert.That(System.Math.Abs(SignedArea(loops[0])), Is.GreaterThan(System.Math.Abs(SignedArea(loops[1]))), "the outer lane is the longer lap");
        }

        private static double SignedArea(Traffic.Loop loop)
        {
            var a = 0.0;
            for (var i = 0; i < loop.Points.Count; i++)
            {
                var p = loop.Points[i]; var q = loop.Points[(i + 1) % loop.Points.Count];
                a += p.x * q.z - q.x * p.z;
            }
            return a / 2.0;
        }

        [Test]
        public void LightsStandAtEveryThreeAndFourWayJunctionAndCycle()
        {
            var tiles = Traffic.LightTiles();
            Assert.That(tiles, Has.Member((4, 6)), "the central crossroads");
            Assert.That(tiles, Has.Member((1, 10)), "a T on the north row");
            Assert.That(tiles, Has.No.Member((1, 6)), "roundabouts regulate themselves");
            Assert.That(tiles, Has.No.Member((1, 1)), "corners have no lights");
            Assert.That(tiles.Count, Is.EqualTo(CityGrid.StreetTiles.Count(t => CityGrid.Arms(t.r, t.c) >= 3 && !CityGrid.IsRoundabout(t.r, t.c))));

            Assert.That(Traffic.LightState(0.0), Is.EqualTo((true, false)));
            Assert.That(Traffic.LightState(5.0), Is.EqualTo((false, false)), "all-red buffer before the switch");
            Assert.That(Traffic.LightState(7.0), Is.EqualTo((false, true)));
            Assert.That(Traffic.LightState(11.0), Is.EqualTo((false, false)));
            Assert.That(Traffic.LightState(12.5), Is.EqualTo((true, false)), "the cycle wraps");
            Assert.That(Traffic.BlinkOn(0.2), Is.True);
            Assert.That(Traffic.BlinkOn(1.0), Is.False);
        }

        [Test]
        public void CarsHoldAtRedKeepTheirGapAndEaseOffForThePlayer()
        {
            var loops = Traffic.Loops();
            var lights = new HashSet<(int r, int c)>(Traffic.LightTiles());
            var loop = loops[4];   // the big outer ring: (1,1) → (1,13) → (10,13) → (10,1), westwards along the north row first
            // a car 14 m short of the T at (1,10) — still on the tile before it — at time 7 (east/west green) rolls through …
            var (jx, jz) = CityGrid.World(1, 10);
            var car = new Traffic.Car { Loop = loop, Speed = Traffic.Speed, Active = true };
            car.S = Nearest(loop, jx + 14.0, jz + CityGrid.LaneOffset);
            var before = car.S;
            Assert.That(loop.At(car.S).dx, Is.LessThan(-0.99), "driving west along the north row (x decreases)");
            Assert.That(CityGrid.Tile(loop.At(car.S).x, loop.At(car.S).z), Is.EqualTo((1, 9)));
            Assert.That(Traffic.RedAhead(car, 7.0, lights), Is.False);
            // … and at time 0 (north/south green) it brakes to a halt short of the junction
            Assert.That(Traffic.RedAhead(car, 0.0, lights), Is.True);
            for (var i = 0; i < 40; i++) Traffic.Step(car, 0.05, 0.0, lights, double.PositiveInfinity);
            Assert.That(car.Speed, Is.EqualTo(0.0));
            var stopped = loop.At(car.S);
            Assert.That(CityGrid.Tile(stopped.x, stopped.z), Is.EqualTo((1, 9)), "it stops before the junction tile");
            Assert.That(car.S - before, Is.LessThan(4.5), "braking from 8 m/s at 10 m/s² takes a good three metres");
            // a car already on the junction clears it even at red
            var onJunction = new Traffic.Car { Loop = loop, Speed = Traffic.Speed, Active = true, S = Nearest(loop, jx, jz + CityGrid.LaneOffset) };
            Assert.That(Traffic.RedAhead(onJunction, 0.0, lights), Is.False);

            // the gap to the car in front keeps them apart
            var leader = new Traffic.Car { Loop = loop, S = car.S + 5.0, Speed = 0.0, Active = true };
            var gap = Traffic.GapAhead(car, new[] { car, leader });
            Assert.That(gap, Is.EqualTo(5.0).Within(1e-9));
            var farBehind = new Traffic.Car { Loop = loop, S = car.S - 5.0, Speed = 0.0, Active = true };
            Assert.That(Traffic.GapAhead(car, new[] { car, farBehind }), Is.EqualTo(loop.Length - 5.0).Within(1e-9), "a car behind is a whole lap ahead");
            Assert.That(Traffic.GapAhead(car, new[] { car, new Traffic.Car { Loop = loops[0], S = car.S + 3.0, Active = true } }), Is.EqualTo(double.PositiveInfinity), "other loops do not count");
            var free = new Traffic.Car { Loop = loop, Speed = Traffic.Speed, Active = true, S = 30.0 };
            for (var i = 0; i < 40; i++) Traffic.Step(free, 0.05, 7.0, lights, 4.0);
            Assert.That(free.Speed, Is.EqualTo(0.0), "held behind the car in front");
            for (var i = 0; i < 40; i++) Traffic.Step(free, 0.05, 7.0, null, double.PositiveInfinity);
            Assert.That(free.Speed, Is.EqualTo(Traffic.Speed).Within(1e-9), "back up to speed once the road is clear");

            // the player's car uses the same RedAhead numbers: westbound of the T at time 0 is red, at time 7 green
            var player = loop.At(Nearest(loop, jx + 14.0, jz + CityGrid.LaneOffset));
            Assert.That(Traffic.RedAhead(player.x, player.z, player.dx, player.dz, 0.0, lights), Is.True, "player sees the same red");
            Assert.That(Traffic.RedAhead(player.x, player.z, player.dx, player.dz, 7.0, lights), Is.False, "player rolls through on green");
            Assert.That(Traffic.RedAhead(player.x, player.z, player.dx, player.dz, 0.0, null), Is.False, "night blink: nobody stops");

            // the player's car on the lane 6 m ahead is an obstacle too — and counts as closer than it is, so the
            // ambient car settles PlayerGapM behind it (out of the chase camera) instead of MinGapM
            var ahead = loop.At(free.S + 6.0);
            Assert.That(Traffic.GapToPlayer(free, ahead.x, ahead.z), Is.LessThanOrEqualTo(7.0 - (Traffic.PlayerGapM - Traffic.MinGapM)));
            Assert.That(Traffic.GapToPlayer(free, ahead.x + 40.0, ahead.z), Is.EqualTo(double.PositiveInfinity));
            var farther = loop.At(free.S + Traffic.PlayerGapM + 1.0);
            Assert.That(Traffic.GapToPlayer(free, farther.x, farther.z), Is.GreaterThanOrEqualTo(Traffic.MinGapM), "PlayerGapM behind the player the road counts as clear");
        }

        [Test]
        public void TheLoopsCircleTheRoundaboutIslandsCounterClockwiseAndThePlayerCannotDriveOverThem()
        {
            var loops = Traffic.Loops();
            var (kx, kz) = CityGrid.World(1, 6);   // the roundabout on the north row (a corner of loops A/B, a through pass of loop C)
            Assert.That(CityGrid.IsRoundabout(1, 6), Is.True);
            var passing = 0;
            foreach (var loop in loops)
            {
                var near = false;
                for (var s = 0.0; s < loop.Length; s += 0.5)
                {
                    var p = loop.At(s);
                    var d = System.Math.Sqrt((p.x - kx) * (p.x - kx) + (p.z - kz) * (p.z - kz));
                    if (d > CityGrid.PlazaRadius) continue;
                    near = true;
                    Assert.That(d, Is.GreaterThanOrEqualTo(CityGrid.IslandRadius + 1.0), $"no loop crosses the island (s={s:F1}, d={d:F2})");
                    // counter-clockwise round the island: the direction of travel is the positive tangent (island on the left)
                    var tangential = (-(p.z - kz) * p.dx + (p.x - kx) * p.dz) / d;
                    if (d < CityGrid.RingRadius + 0.2 && d > CityGrid.RingRadius - 0.2) Assert.That(tangential, Is.GreaterThan(0.3), "right-hand traffic keeps the island on its left");
                }
                if (near) passing++;
            }
            Assert.That(passing, Is.GreaterThanOrEqualTo(4), "several loops pass this roundabout");
            // a through pass: the lane comes in straight, follows the ring and leaves straight — its ring points lie on the ring
            var straightThrough = loops.First(l => l.Points.Count(p => System.Math.Abs(System.Math.Sqrt((p.x - kx) * (p.x - kx) + (p.z - kz) * (p.z - kz)) - CityGrid.RingRadius) < 0.01) >= 6);
            Assert.That(straightThrough, Is.Not.Null);
            // the player: the island is a wall, the plaza is asphalt out to its rim — even on the diagonal, where a plain
            // junction square would already have kerb
            var (cx, cz, hit) = CityGrid.ClampToStreets(kx + 0.3, kz + 0.2, 1.2);
            Assert.That(hit, Is.True, "the island pushes the car out");
            Assert.That(System.Math.Sqrt((cx - kx) * (cx - kx) + (cz - kz) * (cz - kz)), Is.EqualTo(CityGrid.IslandRadius + 1.2).Within(1e-6));
            var diag = 3.4;   // (3.4, 3.4) is 4.8 m from the centre: inside the plaza (5.6 − 1.2 = 4.4 … just inside), outside a ±2.8 square
            var (dx2, dz2, hit2) = CityGrid.ClampToStreets(kx + 3.0, kz + 3.0, 1.2);
            Assert.That(hit2, Is.False, "the plaza is round: the diagonal is drivable");
            var (jx, jz) = CityGrid.World(4, 1);   // a plain crossroads for comparison
            var (_, _, hit3) = CityGrid.ClampToStreets(jx + 3.0, jz + 3.0, 1.2);
            Assert.That(hit3, Is.True, "… where a junction square already has kerb");
            _ = diag;
        }

        [Test]
        public void ACarRollingUpToAJunctionGivesWayToThePlayerAboutToCrossItButNotToOncomingTraffic()
        {
            var loops = Traffic.Loops();
            var loop = loops[4];   // westwards along the north row
            var (jx, jz) = CityGrid.World(1, 10);
            // 14 m east of the T at (1,10), driving west at full speed: the junction centre is reached in under two seconds
            var car = new Traffic.Car { Loop = loop, Speed = Traffic.Speed, Active = true, S = Nearest(loop, jx + 14.0, jz + CityGrid.LaneOffset) };
            // the player comes up the spur from the south, 20 m short of the junction at 42 km/h — nowhere near the loop
            // yet, but on the junction at the same moment as the car
            var v = DriveModel.CruiseKmh / 3.6;
            var (px, pz) = (jx + CityGrid.LaneOffset, jz - 20.0);
            Assert.That(Traffic.GapToPlayer(car, px, pz), Is.EqualTo(double.PositiveInfinity), "standing still 20 m off the loop the player is no obstacle");
            var yielding = Traffic.GapToPlayer(car, px, pz, 0.0, v);
            Assert.That(yielding, Is.LessThan(Traffic.MinGapM), "moving north it is: the car brakes for the crossing");
            // the same player driving away south is ignored, and so is one oncoming in the other lane of the row
            Assert.That(Traffic.GapToPlayer(car, px, pz, 0.0, -v), Is.EqualTo(double.PositiveInfinity));
            var here = loop.At(car.S);
            var (ox, oz) = (here.x - 12.0, here.z - 2.0 * CityGrid.LaneOffset);   // 4 m to the left of our lane, 12 m ahead, driving east
            Assert.That(Traffic.GapToPlayer(car, ox, oz, v, 0.0), Is.EqualTo(double.PositiveInfinity), "oncoming traffic in the other lane");
            // a slower player ahead in our lane is a leader (held by position), not a crossing
            var lead = loop.At(car.S + 10.0);
            Assert.That(Traffic.GapToPlayer(car, lead.x, lead.z, lead.dx * 3.0, lead.dz * 3.0), Is.LessThan(Traffic.MinGapM));
            // … and a faster player coming up behind us must not make us brake in front of them
            var behind = loop.At(car.S - 5.0);
            Assert.That(Traffic.GapToPlayer(car, behind.x, behind.z, behind.dx * 11.0, behind.dz * 11.0), Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void NearMissNeedsProximityAndSpeedAndSparksStayOffWhenReduced()
        {
            // Godot test_city_leben + test_w13b_stadtleben: ≤ 7 m AND |tempo| ≥ 4 m/s
            var close = CityAmbiente.NearMissM - 1.0;
            var fast = CityAmbiente.NearMissSpeed + 1.0;
            Assert.That(CityAmbiente.IsNearMiss(close, fast), Is.True, "close + fast = horn");
            Assert.That(CityAmbiente.IsNearMiss(close, 0.5), Is.False, "walking pace is no drama");
            Assert.That(CityAmbiente.IsNearMiss(CityAmbiente.NearMissM + 1.0, fast), Is.False, "too far");
            Assert.That(CityAmbiente.IsNearMiss(close, -fast), Is.True, "reverse counts");
            Assert.That(CityAmbiente.NearMissPauseS, Is.GreaterThan(0.0));
            Assert.That(CityAmbiente.ShouldSpark(6.9, 5.0, false), Is.True);
            Assert.That(CityAmbiente.ShouldSpark(7.1, 5.0, false), Is.False);
            Assert.That(CityAmbiente.ShouldSpark(6.9, 3.9, false), Is.False);
            Assert.That(CityAmbiente.ShouldSpark(6.9, 5.0, true), Is.False, "reduced motion: horn yes, sparks no");
            var spark = CityAmbiente.SparkPoint(0.0, 0.0, 2.0, 4.0);
            Assert.That(spark.x, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(spark.y, Is.EqualTo(CityAmbiente.SparkHeightM).Within(1e-9));
            Assert.That(spark.z, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void NewspaperGoobyReadsInTheMorningAndTheFirstWalkerOwnsThePaper()
        {
            Assert.That(CityRhythm.NewspaperGooby(8.0), Is.True, "morning paper");
            Assert.That(CityRhythm.NewspaperGooby(6.0), Is.True);
            Assert.That(CityRhythm.NewspaperGooby(9.9), Is.True);
            Assert.That(CityRhythm.NewspaperGooby(12.0), Is.False, "noon: paper down");
            Assert.That(CityRhythm.NewspaperGooby(5.9), Is.False);
            Assert.That(CityRhythm.NewspaperGooby(10.0), Is.False);
            var routes = Pedestrians.Routes(8.0, 5);
            Assert.That(routes[0].Newspaper, Is.True, "first pavement Gooby is the reader");
            Assert.That(routes.Skip(1).Count(r => r.Newspaper), Is.EqualTo(0));
            var standing = new Pedestrians.Pose { Standing = true, Waving = true };
            Assert.That(Pedestrians.ReadingPaper(routes[0], standing, 8.0), Is.True, "morning pause: idle into the page");
            Assert.That(Pedestrians.ReadingPaper(routes[0], standing, 12.0), Is.False);
            Assert.That(Pedestrians.ReadingPaper(routes[0], new Pedestrians.Pose { Standing = false }, 8.0), Is.False);
            Assert.That(Pedestrians.ReadingPaper(routes[1], standing, 8.0), Is.False, "only the paper Gooby");
        }

        [Test]
        public void CashierNpcTapsThenWavesAndReducedMotionSkipsTheWave()
        {
            double tap = 0, wave = 0;
            Assert.That(CashierNpc.Step(ref tap, ref wave, 2.3, false), Is.Null);
            Assert.That(CashierNpc.Step(ref tap, ref wave, 0.2, false), Is.EqualTo("tap"));
            Assert.That(tap, Is.EqualTo(0.0));
            // remaining to first wave: 11 − 2.5 = 8.5 s
            Assert.That(CashierNpc.Step(ref tap, ref wave, 8.4, false), Is.EqualTo("tap")); // 2.4 of those 8.4 is another tap
            Assert.That(CashierNpc.Step(ref tap, ref wave, 0.2, false), Is.EqualTo("wave"));
            tap = 0; wave = 0;
            Assert.That(CashierNpc.Step(ref tap, ref wave, CashierNpc.TapEveryS, true), Is.Null, "reduced: tap is twice as slow");
            Assert.That(CashierNpc.Step(ref tap, ref wave, CashierNpc.TapEveryS, true), Is.EqualTo("tap"));
            tap = 0; wave = 0;
            Assert.That(CashierNpc.Step(ref tap, ref wave, CashierNpc.WaveEveryS, true), Is.EqualTo("tap"));
            Assert.That(wave, Is.EqualTo(CashierNpc.WaveEveryS), "reduced: the wave clock runs but never fires");
        }

        [Test]
        public void ClosingInOnTheCarInFrontTheAutoThrottleBrakesInsteadOfCoasting()
        {
            var s = new DriveModel.State { Kmh = DriveModel.CruiseKmh };
            var (x0, z0) = (s.X, s.Z);
            // a car standing 12 m ahead: within a single step of 0.5 s the speed must come down at the brake rate
            DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, FollowGap = 12.0 - 4.2, FollowKmh = 0.0 }, 0.5);
            Assert.That(s.Kmh, Is.EqualTo(DriveModel.CruiseKmh - DriveModel.BrakeRate * 0.5).Within(1e-6));
            var travelled = System.Math.Sqrt((s.X - x0) * (s.X - x0) + (s.Z - z0) * (s.Z - z0));
            Assert.That(travelled, Is.LessThan(4.0), "hardly a car length while braking down");
        }

        [Test]
        public void TheFleetIsTheDaysMaximumAndTheHourShowsItsShare()
        {
            var fleet = Traffic.Fleet(Traffic.Loops(), 13.0, 1);
            Assert.That(fleet.Count, Is.EqualTo(CityRhythm.TrafficMax));
            Assert.That(fleet.Count(c => c.Active), Is.EqualTo(22));
            Assert.That(fleet.Select(c => c.Loop).Distinct().Count(), Is.GreaterThanOrEqualTo(6), "spread over the loops");
            Assert.That(Traffic.SetHour(fleet, 2.0), Is.True);
            Assert.That(fleet.Count(c => c.Active), Is.EqualTo(6));
            Assert.That(Traffic.SetHour(fleet, 2.5), Is.False, "nothing changes within the night level");
        }

        [Test]
        public void WalkersStrollThePavementsPauseAtTheShopWindowsAndKeepOutOfTheJunctions()
        {
            var routes = Pedestrians.Routes(12.0, 5);
            Assert.That(routes.Count, Is.EqualTo(CityRhythm.WalkerMax));
            Assert.That(routes.Count(r => r.Active), Is.EqualTo(18));
            Assert.That(routes.Count(r => r.ShopWindow), Is.GreaterThanOrEqualTo(7), "the shops look visited");
            foreach (var r in routes)
            {
                Assert.That(r.Length, Is.GreaterThan(8.0));
                Assert.That(r.Speed, Is.InRange(Pedestrians.SpeedMin, Pedestrians.SpeedMax));
                Assert.That(Pedestrians.Furs, Has.Member(r.Fur));
                Assert.That(r.Fur, Is.Not.EqualTo("cream"), "never the player's own fur");
                foreach (var (x, z) in new[] { (r.FromX, r.FromZ), (r.ToX, r.ToZ) })
                {
                    // both ends lie on a pavement: off the asphalt by the pavement's half width …
                    var (cx, cz, hit) = CityGrid.ClampToStreets(x, z, 0.0);
                    var off = System.Math.Sqrt((cx - x) * (cx - x) + (cz - z) * (cz - z));
                    Assert.That(hit, Is.True, "a route end must not stand on the asphalt");
                    Assert.That(off, Is.EqualTo(CityGrid.WalkWidth * 0.5).Within(0.05));
                    // … and clear of the junction squares
                    var tile = CityGrid.Tile(x, z);
                    if (CityGrid.IsStreet(tile.r, tile.c) && (CityGrid.Arms(tile.r, tile.c) >= 3 || CityGrid.IsRoundabout(tile.r, tile.c)))
                    {
                        var (tx, tz) = CityGrid.World(tile.r, tile.c);
                        Assert.That(System.Math.Max(System.Math.Abs(x - tx), System.Math.Abs(z - tz)), Is.GreaterThanOrEqualTo(Pedestrians.JunctionClearM - 0.01));
                    }
                }
            }
            // the pose runs walk → pause (looking at the houses, maybe waving) → back → pause
            var route = routes[0];
            var walk = route.Length / route.Speed;
            var start = -route.Phase * 2.0 * (walk + route.PauseS);   // cycle origin
            var mid = Pedestrians.At(route, start + walk * 0.5);
            Assert.That(mid.Standing, Is.False);
            Assert.That(mid.X, Is.EqualTo((route.FromX + route.ToX) / 2.0).Within(0.01));
            var pause = Pedestrians.At(route, start + walk + route.PauseS * 0.5);
            Assert.That(pause.Standing, Is.True);
            Assert.That(pause.X, Is.EqualTo(route.ToX).Within(1e-6));
            Assert.That(pause.Heading, Is.EqualTo(route.Look).Within(1e-9));
            Assert.That(pause.Waving, Is.EqualTo(route.Waves));
            var back = Pedestrians.At(route, start + walk + route.PauseS + walk * 0.25);
            Assert.That(back.Standing, Is.False);
            Assert.That(back.X, Is.EqualTo(route.ToX + (route.FromX - route.ToX) * 0.25).Within(0.01));
            Assert.That(Pedestrians.SetHour(routes, 3.0), Is.True);
            Assert.That(routes.Count(r => r.Active), Is.EqualTo(5));
        }

        [Test]
        public void ShopWindowRoutesRunAlongTheDoorSideOfThePlacesStreet()
        {
            var routes = Pedestrians.Routes(12.0, 5).Where(r => r.ShopWindow).ToList();
            var places = DriveModel.Places.Where(p => p.Id != "home").ToList();
            foreach (var r in routes)
            {
                var midX = (r.FromX + r.ToX) / 2.0; var midZ = (r.FromZ + r.ToZ) / 2.0;
                var place = places.OrderBy(p => Dist(p, midX, midZ)).First();
                var (sx, sz) = CityGrid.World(place.StreetTile[0], place.StreetTile[1]);
                // the route's middle is the door-side pavement of the place's street tile
                var expectedX = sx - place.FaceX * Pedestrians.PavementOffset;
                var expectedZ = sz - place.FaceZ * Pedestrians.PavementOffset;
                Assert.That(System.Math.Abs(midX - expectedX) < 1.5 || System.Math.Abs(midZ - expectedZ) < 1.5, Is.True, $"route near {place.Id}");
                Assert.That(System.Math.Abs((midX - sx) * place.FaceX + (midZ - sz) * place.FaceZ + Pedestrians.PavementOffset), Is.LessThan(0.05), "on the door's side");
                // pausing, the walker looks at the shop (against the door→street direction)
                var lookX = System.Math.Sin(r.Look); var lookZ = System.Math.Cos(r.Look);
                Assert.That(lookX * place.FaceX + lookZ * place.FaceZ, Is.LessThan(-0.99));
            }
        }

        private static double Dist(DriveModel.Place p, double x, double z)
        {
            var (sx, sz) = CityGrid.World(p.StreetTile[0], p.StreetTile[1]);
            return System.Math.Sqrt((sx - x) * (sx - x) + (sz - z) * (sz - z));
        }

        private static double Nearest(Traffic.Loop loop, double x, double z)
        {
            var best = 0.0; var bestD = double.MaxValue;
            for (var s = 0.0; s < loop.Length; s += 0.25)
            {
                var p = loop.At(s);
                var d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }
    }
}
