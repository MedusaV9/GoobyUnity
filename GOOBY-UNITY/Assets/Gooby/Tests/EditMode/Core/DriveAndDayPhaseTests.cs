using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Time;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Free driving through the street lattice (speed, steering, lane assist, places) and the time-of-day curve.</summary>
    public class DriveAndDayPhaseTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            CityMap.Reset();
            CityGrid.Reset();
            DriveModel.Reset();
        }

        private static DriveModel.State Cruise()
        {
            var s = new DriveModel.State();
            for (var i = 0; i < 200; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0 }, 0.05);
            return s;
        }

        private static double Dist(DriveModel.State s, double x, double z) => System.Math.Sqrt((s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z));

        [Test]
        public void ThrottleReachesCruiseAndBrakeStops()
        {
            var s = Cruise();
            Assert.That(s.Kmh, Is.EqualTo(DriveModel.CruiseKmh).Within(1e-9));
            Assert.That(s.Driven, Is.GreaterThan(50.0));
            for (var i = 0; i < 40; i++) DriveModel.Step(s, new DriveModel.Input { Brake = true }, 0.05);
            Assert.That(s.Kmh, Is.EqualTo(0.0));
            for (var i = 0; i < 40; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = -1.0 }, 0.05);
            Assert.That(s.Kmh, Is.EqualTo(-DriveModel.ReverseKmh).Within(1e-9), "reverse gear crawls backwards");
        }

        [Test]
        public void RedLightBrakesCruiseButReverseIgnoresIt()
        {
            var s = Cruise();
            Assert.That(s.Kmh, Is.EqualTo(DriveModel.CruiseKmh).Within(1e-9));
            for (var i = 0; i < 40; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, RedLight = true }, 0.05);
            Assert.That(s.Kmh, Is.EqualTo(0.0), "auto-throttle brakes for a red like the ambient cars");
            for (var i = 0; i < 40; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = -1.0, RedLight = true }, 0.05);
            Assert.That(s.Kmh, Is.EqualTo(-DriveModel.ReverseKmh).Within(1e-9), "reverse still works at a red");
        }

        [Test]
        public void BehindASlowerCarTheAutoThrottleHoldsItsSpeedAndCrawlsAtTheMinimumGap()
        {
            Assert.That(DriveModel.FollowCap(null, 29.0), Is.Null, "free road");
            Assert.That(DriveModel.FollowCap(DriveModel.FollowM + 1.0, 29.0), Is.Null, "still far enough");
            Assert.That(DriveModel.FollowCap(DriveModel.FollowM - 0.01, 29.0), Is.EqualTo(29.0).Within(0.1), "closing in: the leader's speed");
            Assert.That(DriveModel.FollowCap(DriveModel.FollowMinM, 29.0), Is.EqualTo(29.0 * 0.3).Within(1e-9), "at the minimum gap: a crawl");
            Assert.That(DriveModel.FollowCap(1.0, 29.0), Is.EqualTo(29.0 * 0.3).Within(1e-9), "never below the crawl, never negative");
            var s = Cruise();
            for (var i = 0; i < 60; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, FollowGap = 12.0, FollowKmh = 28.8 }, 0.05);
            Assert.That(s.Kmh, Is.EqualTo(DriveModel.FollowCap(12.0, 28.8).Value).Within(0.5), "eased off to the car in front");
            for (var i = 0; i < 60; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0 }, 0.05);
            Assert.That(s.Kmh, Is.EqualTo(DriveModel.CruiseKmh).Within(1e-9), "back to cruise once the lane is clear");
        }

        [Test]
        public void TheLatticeComesFromThePlanAndTheDriveStartsInTheDriveway()
        {
            Assert.That(CityGrid.Rows, Is.EqualTo(12));
            Assert.That(CityGrid.Cols, Is.EqualTo(15));
            Assert.That(CityGrid.StreetTiles.Count(), Is.EqualTo(4 * 13 + 4 * 10 - 16 + 1 + 1), "four rows × four columns of streets, junctions once, plus the airport spur and the road out to the ranch");
            Assert.That(CityGrid.Arms(7, 14), Is.EqualTo(1), "the ranch exit is a dead end at the town's edge");
            Assert.That(CityGrid.Arms(7, 13), Is.EqualTo(4), "row 7 runs on past column 13");
            var ranch = DriveModel.PlaceById(DriveModel.RanchId);
            Assert.That(ranch, Is.Not.Null);
            Assert.That(ranch.StreetTile, Is.EqualTo(new[] { 7, 14 }));
            Assert.That(ranch.Side, Is.GreaterThan(1), "not a place of the town plan");
            Assert.That(CityGrid.IsStreet(1, 6) && CityGrid.IsRoundabout(1, 6), Is.True);
            Assert.That(CityGrid.IsStreet(9, 2), Is.False, "home is a block tile");
            Assert.That(CityGrid.Arms(4, 6), Is.EqualTo(4), "a crossroads in the middle");
            Assert.That(CityGrid.Arms(1, 1), Is.EqualTo(2), "the north-west corner bends");
            Assert.That(CityGrid.Arms(0, 4), Is.EqualTo(1), "the airport spur is a dead end");
            Assert.That(CityGrid.DistrictOf(9, 2), Is.EqualTo("wohnen"));
            Assert.That(CityGrid.DistrictOf(5, 5), Is.EqualTo("zentrum"));
            Assert.That(CityGrid.Landmarks.Any(l => l.Id == "ikea"), Is.True);

            var s = new DriveModel.State();
            var (sx, sz) = CityGrid.World(9, 1);
            Assert.That(Dist(s, sx, sz), Is.LessThan(CityGrid.RoadHalfWidth), "the car starts on the home street tile");
            Assert.That(DriveModel.Nearby(s)?.Id, Is.EqualTo("home"), "…at the driveway");
            Assert.That(s.Heading, Is.EqualTo(0.0).Within(1e-9), "heading north (+z), the long way up column 1 (the GOOBY MART comes up on the left)");
            Assert.That(s.X, Is.EqualTo(sx + CityGrid.LaneOffset).Within(1e-9), "in the right-hand (east) lane — home is on the left");
            var (_, _, hit) = CityGrid.ClampToStreets(s.X, s.Z, DriveModel.CarRadius);
            Assert.That(hit, Is.False, "on the asphalt");
            var (cx, cz, moved) = CityGrid.ClampToStreets(sx + 30.0, sz, DriveModel.CarRadius);
            Assert.That(moved, Is.True, "a point in the block next door is pushed back onto the nearest street");
            Assert.That(CityGrid.ClampToStreets(cx, cz, DriveModel.CarRadius).hit, Is.False, "…and that point is on the asphalt");
            var (tr, tc) = CityGrid.Tile(cx, cz);
            Assert.That(CityGrid.IsStreet(tr, tc), Is.True);
            var far = CityGrid.ClampToStreets(400.0, 400.0, DriveModel.CarRadius);
            Assert.That(far.hit && CityGrid.IsStreet(CityGrid.Tile(far.x, far.z).r, CityGrid.Tile(far.x, far.z).c), Is.True, "even a point far outside the town comes back to a street");
        }

        [Test]
        public void SteeringTurnsTheCarAndTheLaneAssistStraightensItAgain()
        {
            var s = Cruise();
            var h0 = s.Heading;
            for (var i = 0; i < 10; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, Steer = -1.0 }, 0.05);
            Assert.That(DriveModel.Wrap(s.Heading - h0), Is.LessThan(0.0), "left stick → the heading turns left (counter-clockwise from above)");
            for (var i = 0; i < 14; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, Steer = 1.0 }, 0.05);
            Assert.That(DriveModel.Wrap(s.Heading - h0), Is.GreaterThan(0.0), "right stick → right");
            // stick released a little off the cardinal (well inside the 25° assist window): the assist eases the car straight again
            var cardinal = System.Math.Round(s.Heading / (System.Math.PI / 2)) * (System.Math.PI / 2);
            Assert.That(System.Math.Abs(DriveModel.Wrap(s.Heading - cardinal)), Is.InRange(0.05, 0.4), "a few degrees off");
            for (var i = 0; i < 200; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0 }, 0.05);
            Assert.That(System.Math.Abs(DriveModel.Wrap(s.Heading - cardinal)), Is.LessThan(0.02), "straightened onto the cardinal");
            // a hard turn (beyond the window) is left alone: the driver means it
            var turning = Cruise();
            for (var i = 0; i < 20; i++) DriveModel.Step(turning, new DriveModel.Input { Throttle = 1.0, Steer = 1.0 }, 0.05);
            var off = turning.Heading;
            DriveModel.Step(turning, new DriveModel.Input { Throttle = 1.0 }, 0.05);
            Assert.That(System.Math.Abs(DriveModel.Wrap(turning.Heading - off)), Is.LessThan(0.1), "no assist beyond the fade window (only the filtered stick decays)");

            var parked = new DriveModel.State();
            var x0 = parked.X; var hp = parked.Heading;
            for (var i = 0; i < 20; i++) DriveModel.Step(parked, new DriveModel.Input { Steer = 1.0 }, 0.05);
            Assert.That(parked.X, Is.EqualTo(x0), "a standing car does not move");
            Assert.That(parked.Heading, Is.EqualTo(hp), "…nor turn");
        }

        [Test]
        public void TheCarStaysOnTheAsphaltAndLosesSpeedAtTheKerb()
        {
            // head straight for the block east of the start: the kerb stops the car, it slides along and slows down
            var s = new DriveModel.State { Kmh = DriveModel.CruiseKmh };
            s.Heading = System.Math.PI / 2; // east, into the home block
            var slowest = s.Kmh;
            for (var i = 0; i < 60; i++)
            {
                DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, Steer = 0.0 }, 0.05);
                slowest = System.Math.Min(slowest, s.Kmh);
            }
            var (sx, _) = CityGrid.World(9, 1);
            Assert.That(s.X - sx, Is.LessThanOrEqualTo(CityGrid.RoadHalfWidth - DriveModel.CarRadius + 1e-6), "never leaves the asphalt band");
            Assert.That(s.Hit, Is.True, "leaning on the kerb");
            Assert.That(slowest, Is.LessThan(DriveModel.CruiseKmh * 0.6), "the first contact cost speed (one loss per contact episode)");
            Assert.That(s.Heading, Is.EqualTo(System.Math.PI / 2).Within(1e-9), "head-on, the car keeps its heading — the player picks the way round");
        }

        [Test]
        public void ARoundaboutIsTakenByItselfRoundTheIslandAndStraightOutAgain()
        {
            // northbound in the right-hand lane of column 6, two tiles south of the roundabout at (7,6)
            var (kx, kz) = CityGrid.World(7, 6);
            var (sx, sz) = CityGrid.World(9, 6);
            var s = new DriveModel.State { X = sx + CityGrid.LaneOffset, Z = sz, Heading = 0.0, Kmh = DriveModel.CruiseKmh };
            var minRho = double.MaxValue; var minKmhOnPlaza = double.MaxValue; var eastSide = false; var hits = 0;
            for (var i = 0; i < 60 * 10 && s.Z < kz + 14.0; i++)
            {
                DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0 }, 1.0 / 60.0);
                var dx = s.X - kx; var dz = s.Z - kz;
                var rho = System.Math.Sqrt(dx * dx + dz * dz);
                if (rho < CityGrid.PlazaRadius)
                {
                    minRho = System.Math.Min(minRho, rho);
                    minKmhOnPlaza = System.Math.Min(minKmhOnPlaza, s.Kmh);
                    if (System.Math.Abs(dz) < 1.0 && dx > CityGrid.IslandRadius) eastSide = true;
                    if (s.Hit) hits++;
                }
            }
            Assert.That(s.Z, Is.GreaterThan(kz + 14.0), "through the roundabout and on up the street within ten seconds");
            Assert.That(minRho, Is.GreaterThanOrEqualTo(CityGrid.IslandRadius + DriveModel.CarRadius - 0.05), "never on the island");
            Assert.That(hits, Is.EqualTo(0), "round the island without a scrape of the kerb");
            Assert.That(eastSide, Is.True, "passes the island on its east side — the island on the left, like the traffic");
            Assert.That(minKmhOnPlaza, Is.LessThanOrEqualTo(DriveModel.RingKmh + 1.0), "slows to ring speed on the plaza");
            Assert.That(System.Math.Abs(DriveModel.Wrap(s.Heading)), Is.LessThan(0.2), "heading north again");
            Assert.That(s.X, Is.EqualTo(kx + CityGrid.LaneOffset).Within(0.9), "back in the right-hand lane");
            Assert.That(s.Kmh, Is.GreaterThan(DriveModel.RingKmh), "and picking up speed again");
            Assert.That(double.IsNaN(s.RingEntryHeading), Is.True, "the roundabout is behind the car");
        }

        [Test]
        public void NosedIntoTheKerbAtTheBendASecondOnTheStickTurnsTheCornerInsteadOfFlippingTheCar()
        {
            // the north-east bend (1,1): the home street ends at its north kerb, the town continues west (column 2 lies at −x)
            var s = new DriveModel.State { Kmh = DriveModel.CruiseKmh };
            var (bx, bz) = CityGrid.World(1, 1);
            s.X = bx + CityGrid.LaneOffset; s.Z = bz - 12.0; s.Heading = 0.0;
            for (var i = 0; i < 60; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0 }, 0.05);
            Assert.That(s.Hit, Is.True, "nosed into the kerb");
            Assert.That(s.Kmh, Is.LessThan(4.0), "…and creeping");
            // a full second of left stick from the crawl
            for (var i = 0; i < 20; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, Steer = -1.0 }, 0.05);
            var turned = System.Math.Abs(DriveModel.Wrap(s.Heading)) * 180.0 / System.Math.PI;
            Assert.That(turned, Is.InRange(25.0, 70.0), $"a crawling car turns gently ({turned:F0}°), not the 100° of a cruising one");
            // thumb off: the kerb and lane assists finish the corner
            for (var i = 0; i < 120; i++) DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0 }, 0.05);
            Assert.That(System.Math.Abs(DriveModel.Wrap(s.Heading + System.Math.PI / 2)) * 180.0 / System.Math.PI, Is.LessThan(3.0), "heading west along the north row");
            Assert.That(s.X, Is.LessThan(bx - 15.0), "well into the row");
            Assert.That(s.Z, Is.EqualTo(bz + CityGrid.LaneOffset).Within(0.6), "in the right-hand (north) lane");
            Assert.That(s.Kmh, Is.GreaterThan(DriveModel.CruiseKmh * 0.9));
        }

        [Test]
        public void ScrapingAlongTheKerbAtASlantTheCarStraightensOutAndFindsItsLane()
        {
            // a U-turn taken 36° too far: heading south-south-east on the home street, the lane assist has given up (> 25°)
            var s = new DriveModel.State { Kmh = DriveModel.CruiseKmh };
            var (sx, sz) = CityGrid.World(5, 1);
            s.X = sx; s.Z = sz;
            s.Heading = System.Math.PI - 36.0 * System.Math.PI / 180.0;
            var touched = false;
            for (var i = 0; i < 120; i++)
            {
                DriveModel.Step(s, new DriveModel.Input { Throttle = 1.0, Steer = 0.0 }, 0.05);
                touched |= s.Hit;
            }
            Assert.That(touched, Is.True, "the slant runs the car into the east kerb first");
            Assert.That(System.Math.Abs(DriveModel.Wrap(s.Heading - System.Math.PI)), Is.LessThan(2.0 * System.Math.PI / 180.0), "…then it straightens out along the kerb and the lane assist takes over");
            Assert.That(s.Hit, Is.False, "off the kerb again");
            Assert.That(s.X, Is.EqualTo(sx - CityGrid.LaneOffset).Within(0.6), "back in the right-hand lane (west of the centre line heading south)");
            Assert.That(s.Kmh, Is.GreaterThan(DriveModel.CruiseKmh * 0.9), "and back up to speed");
        }

        [Test]
        public void PlacesSitOnTheirPlanTilesWithAParkingSpotAtTheKerb()
        {
            Assert.That(DriveModel.Places.Count(p => p.Id == "mart"), Is.EqualTo(1));
            var mart = DriveModel.PlaceById("mart");
            Assert.That(mart.Tile, Is.EqualTo(new[] { 5, 2 }), "REHWEI on its plan tile");
            Assert.That(mart.StreetTile, Is.EqualTo(new[] { 5, 1 }));
            var (sx, sz) = CityGrid.World(5, 1);
            Assert.That(Dist(new DriveModel.State { X = sx, Z = sz }, mart.X, mart.Z), Is.EqualTo(CityGrid.LaneOffset).Within(1e-9), "parking spot in the kerb-side lane of the street tile");
            Assert.That(mart.FaceX, Is.EqualTo(1.0).Within(1e-9), "the door looks east onto the street (column 1 lies east of column 2)");
            Assert.That(mart.Dialog, Is.EqualTo("rehwei"));
            Assert.That(mart.Shop, Is.EqualTo(ShopLogic.RehweiPath));
            Assert.That(mart.Counter, Is.EqualTo(DriveModel.CounterShop));
            var vet = DriveModel.PlaceById("tierarzt");
            Assert.That(vet.Tile, Is.EqualTo(new[] { 6, 4 }));
            Assert.That(vet.HasCounter, Is.False, "the practice has no counter");
            foreach (var p in DriveModel.Places)
            {
                var (_, _, hit) = CityGrid.ClampToStreets(p.X, p.Z, DriveModel.CarRadius);
                Assert.That(hit, Is.False, p.Id + ": the parking spot lies on the asphalt");
                Assert.That(CityGrid.IsStreet(p.StreetTile[0], p.StreetTile[1]), Is.True, p.Id + " fronts a street tile");
                Assert.That(CityGrid.IsStreet(p.Tile[0], p.Tile[1]), Is.False, p.Id + " stands on a block tile");
            }
            // parking spots of different places never share a stop zone
            var spots = DriveModel.Places.ToList();
            for (var i = 0; i < spots.Count; i++)
                for (var j = i + 1; j < spots.Count; j++)
                    Assert.That(Dist(new DriveModel.State { X = spots[i].X, Z = spots[i].Z }, spots[j].X, spots[j].Z), Is.GreaterThan(2 * DriveModel.StopRadius), spots[i].Id + " vs " + spots[j].Id);

            var car = new DriveModel.State { X = mart.X, Z = mart.Z + 3.0, Kmh = 30.0 };
            Assert.That(DriveModel.Reached(car), Is.Null, "driving past does not count");
            Assert.That(DriveModel.Nearby(car)?.Id, Is.EqualTo("mart"));
            car.Kmh = 0.0;
            Assert.That(DriveModel.Reached(car)?.Id, Is.EqualTo("mart"));
            car.Z = mart.Z + DriveModel.StopRadius + 1.0;
            Assert.That(DriveModel.Reached(car), Is.Null);
        }

        [Test]
        public void AheadNamesTheNextPlaceInFrontOfTheCar()
        {
            // up the home street (column 1) heading north: the pharmacy [6,2] comes first, the GOOBY MART [5,2] a tile later
            var mart = DriveModel.PlaceById("mart");
            var pharmacy = DriveModel.PlaceById("goobytheke");
            var s = new DriveModel.State { X = mart.X, Z = mart.Z - 40.0, Heading = 0.0 };
            var (ahead, metres) = DriveModel.Ahead(s);
            Assert.That(ahead.Id, Is.EqualTo("goobytheke"));
            Assert.That(metres, Is.EqualTo(20.0).Within(1e-9));
            Assert.That(metres, Is.LessThan(DriveModel.OfferRadius), "already offered");
            s.Z = mart.Z - 15.0;   // past the pharmacy door: the mart is next
            (ahead, metres) = DriveModel.Ahead(s);
            Assert.That(ahead.Id, Is.EqualTo("mart"));
            Assert.That(metres, Is.EqualTo(15.0).Within(1e-9));
            s.Heading = System.Math.PI; // turned around: the mart is behind and not 'ahead' any more
            var (behind, _) = DriveModel.Ahead(s);
            Assert.That(behind.Id, Is.Not.EqualTo("mart"), "a place behind the car is not 'ahead'");
            Assert.That(pharmacy.Z, Is.LessThan(mart.Z), "the pharmacy lies south of the mart");
        }

        [Test]
        public void PassedOrAheadHandsThePillToTheCloserShopAhead()
        {
            var mart = DriveModel.PlaceById("mart");
            var pharmacy = DriveModel.PlaceById("goobytheke");
            // eight metres past the pharmacy: still the nearer stop, so Anhalten stays on GOOBYTHEKE
            var past = new DriveModel.State { X = pharmacy.X, Z = pharmacy.Z + 8.0, Heading = 0.0 };
            Assert.That(DriveModel.PassedOrAhead(past, out _).Id, Is.EqualTo("goobytheke"));
            // twelve metres past: MART is closer ahead, so the pill names MART (board 03 row)
            var onward = new DriveModel.State { X = pharmacy.X, Z = pharmacy.Z + 12.0, Heading = 0.0 };
            Assert.That(DriveModel.Distance(onward, mart), Is.LessThan(DriveModel.Distance(onward, pharmacy)));
            Assert.That(DriveModel.PassedOrAhead(onward, out _).Id, Is.EqualTo("mart"));
            // 12.5 m past THEKE: the hold has ended and MART is still ahead of StopRadius
            var between = new DriveModel.State { X = pharmacy.X, Z = pharmacy.Z + 12.5, Heading = 0.0 };
            Assert.That(DriveModel.JustPassed(between), Is.Null);
            Assert.That(DriveModel.PassedOrAhead(between, out _).Id, Is.EqualTo("mart"));
            // MART street (20 m past THEKE): the car is at MART's door, Nearby names it (board 03 row)
            var martRow = new DriveModel.State { X = pharmacy.X, Z = pharmacy.Z + 20.0, Heading = 0.0 };
            Assert.That(DriveModel.JustPassed(martRow), Is.Null);
            Assert.That(DriveModel.Nearby(martRow)?.Id, Is.EqualTo("mart"));
        }

        [Test]
        public void ApproachRollsUpToThePlaceAndStopsThere()
        {
            var mart = DriveModel.PlaceById("mart");
            var s = new DriveModel.State { X = mart.X, Z = mart.Z - 40.0, Heading = 0.0, Kmh = DriveModel.CruiseKmh };
            var steps = 0;
            while (steps++ < 2000)
            {
                var input = DriveModel.Approach(s, mart);
                Assert.That(input, Is.Not.Null, "never overshoots the place");
                DriveModel.Step(s, input.Value, 0.05);
                if (DriveModel.Reached(s) != null && s.Kmh == 0.0) break;
            }
            Assert.That(DriveModel.Reached(s)?.Id, Is.EqualTo("mart"));
            Assert.That(s.Kmh, Is.EqualTo(0.0));
            Assert.That(DriveModel.Distance(s, mart), Is.LessThan(2.5), "parked right at the kerb of the shop");
            Assert.That(steps, Is.LessThan(400), "arrives within 20 s");
            var gone = new DriveModel.State { X = mart.X, Z = mart.Z + DriveModel.ParkTolerance * 1.5 + 1.0, Heading = 0.0 };
            Assert.That(DriveModel.Approach(gone, mart), Is.Null, "well behind the place: give up");
            var late = new DriveModel.State { X = mart.X, Z = mart.Z + DriveModel.ParkTolerance - 1.0, Heading = 0.0, Kmh = 20.0 };
            Assert.That(DriveModel.Approach(late, mart)?.Brake, Is.True, "just past the door: brake and park here");
        }

        [Test]
        public void ADoorJustPassedStaysOfferedUntilTheParkTolerance()
        {
            var vet = DriveModel.PlaceById("tierarzt");
            // the vet fronts the street row 7 (tile [7,4]); driving west along it (away from the GOOBYMAN next door),
            // the door slips behind the car
            var west = -System.Math.PI / 2;
            Assert.That(DriveModel.JustPassed(new DriveModel.State { X = vet.X - 3.0, Z = vet.Z, Heading = west }), Is.Null, "within StopRadius Nearby already offers it");
            Assert.That(DriveModel.JustPassed(new DriveModel.State { X = vet.X - 9.0, Z = vet.Z, Heading = west })?.Id, Is.EqualTo("tierarzt"), "nine metres past the door: still a stop");
            Assert.That(DriveModel.JustPassed(new DriveModel.State { X = vet.X - DriveModel.JustPassedHold - 0.5, Z = vet.Z, Heading = west }), Is.Null, "beyond the hold the practice is gone from the pill");
            Assert.That(DriveModel.JustPassed(new DriveModel.State { X = vet.X + 9.0, Z = vet.Z, Heading = west }), Is.Null, "ahead of the car is not passed");
            // pulling over from there still parks within tolerance
            var s = new DriveModel.State { X = vet.X - 9.0, Z = vet.Z, Heading = west, Kmh = DriveModel.CruiseKmh };
            var steps = 0;
            while (steps++ < 200 && !DriveModel.ParkedAt(s, vet))
            {
                var input = DriveModel.Approach(s, vet);
                Assert.That(input, Is.Not.Null);
                DriveModel.Step(s, input.Value, 0.1);
            }
            Assert.That(DriveModel.ParkedAt(s, vet), Is.True);
        }

        [Test]
        public void ALateTapAtTheEndOfTheNearZoneStillParksAtThePlace()
        {
            // "Hier kannst du anhalten" is offered while d ≤ StopRadius; braking from cruise speed needs a few
            // metres, so a tap at the far end must not be dropped — the car stops a little past the door and counts as parked
            var vet = DriveModel.PlaceById("tierarzt");
            var west = -System.Math.PI / 2;
            var s = new DriveModel.State { X = vet.X - (DriveModel.StopRadius - 0.5), Z = vet.Z, Heading = west, Kmh = DriveModel.CruiseKmh };
            Assert.That(DriveModel.Nearby(s)?.Id, Is.EqualTo("tierarzt"), "the pill offers the stop here");
            var steps = 0;
            while (steps++ < 200)
            {
                var input = DriveModel.Approach(s, vet);
                Assert.That(input, Is.Not.Null, "the approach is not abandoned while braking");
                DriveModel.Step(s, input.Value, 0.45); // a slow frame, like the low-end player
                if (DriveModel.ParkedAt(s, vet)) break;
            }
            Assert.That(DriveModel.ParkedAt(s, vet), Is.True);
            Assert.That(s.Kmh, Is.EqualTo(0.0));
            Assert.That(DriveModel.Along(s, vet), Is.LessThan(0.0).And.GreaterThan(-DriveModel.ParkTolerance), "came to rest just past the door");
            Assert.That(DriveModel.ParkedAt(new DriveModel.State { X = vet.X - DriveModel.ParkTolerance - 0.1, Z = vet.Z }, vet), Is.False);
            Assert.That(DriveModel.ParkedAt(new DriveModel.State { X = vet.X, Z = vet.Z, Kmh = 20.0 }, vet), Is.False, "still rolling");
        }

        [Test]
        public void DayPhaseNamesAndDaylightCurve()
        {
            Assert.That(DayPhase.Of(7.5), Is.EqualTo(DayPhase.Morgen));
            Assert.That(DayPhase.Of(13.0), Is.EqualTo(DayPhase.Tag));
            Assert.That(DayPhase.Of(19.0), Is.EqualTo(DayPhase.Abend));
            Assert.That(DayPhase.Of(23.0), Is.EqualTo(DayPhase.Nacht));
            Assert.That(DayPhase.Of(3.0), Is.EqualTo(DayPhase.Nacht));
            Assert.That(DayPhase.Of(27.0), Is.EqualTo(DayPhase.Nacht), "hours wrap");

            Assert.That(DayPhase.Daylight(3.0), Is.EqualTo(0.0));
            Assert.That(DayPhase.Daylight(6.5), Is.InRange(0.3, 0.7));
            Assert.That(DayPhase.Daylight(12.0), Is.EqualTo(1.0));
            Assert.That(DayPhase.Daylight(19.75), Is.InRange(0.3, 0.7));
            Assert.That(DayPhase.Daylight(22.0), Is.EqualTo(0.0));
            for (var h = 0.0; h < 24.0; h += 0.25)
                Assert.That(System.Math.Abs(DayPhase.Daylight(h + 0.25) - DayPhase.Daylight(h)), Is.LessThan(0.2), "no jumps at " + h);

            Assert.That(DayPhase.Warmth(7.0), Is.GreaterThan(0.3), "golden hour at dawn");
            Assert.That(DayPhase.Warmth(12.0), Is.LessThan(0.05));
            Assert.That(DayPhase.Warmth(16.4), Is.GreaterThan(0.2), "board 03 afternoon");
            Assert.That(DayPhase.Warmth(19.0), Is.GreaterThan(0.3), "golden hour at dusk");
            Assert.That(DayPhase.Warmth(2.0), Is.EqualTo(0.0));
            Assert.That(DayPhase.LampsOn(22.0), Is.True);
            Assert.That(DayPhase.LampsOn(12.0), Is.False);
            Assert.That(DayPhase.LocalHour(0L, 120), Is.EqualTo(2.0).Within(1e-9), "UTC midnight + 2 h bias");
            Assert.That(DayPhase.LocalHour(1_768_478_400_000L, 0), Is.EqualTo(12.0).Within(1e-6));
        }
    }
}
