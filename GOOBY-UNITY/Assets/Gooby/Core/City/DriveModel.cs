using System;
using System.Collections.Generic;

namespace Gooby.Core.City
{
    /// <summary>
    /// Free driving through Sonnental as numbers (port of the web/Godot carController + carFeel, Doc E §C7.2): the car
    /// cruises by itself, the joystick turns it (low-pass filtered, yaw rate capped), a gentle lane assist eases the
    /// heading back to the nearest cardinal and the position to the right-hand lane while the thumb rests, and the
    /// asphalt of the street lattice (<see cref="CityGrid"/>) keeps the car on the road with a soft slide. The places
    /// of the town plan each own a parking spot at their kerb; the HUD offers to pull over there.
    /// Engine-free so the drive screen, the 3D scene and the tests agree on where the car is and what is nearby.
    /// </summary>
    public static class DriveModel
    {
        public const double CruiseKmh = 42.0;          // board 03: 42 km/h on the readout
        public const double ReverseKmh = 8.0;
        public const double BrakeRate = 60.0;          // km/h per second (≈ the web's 12 m/s² brake, forgiving)
        public const double ThrottleRate = 18.0;       // km/h per second up
        public const double CoastRate = 30.0;          // km/h per second down when throttle is released
        public const double SteerRate = 1.9;           // rad/s at full deflection (carFeel STEER_RATE)
        public const double SteerCapDegPerSec = 90.0;
        public const double SteerTau = 0.12;           // steer input low-pass (s)
        public const double CrawlSteerSpeed = 4.0;     // m/s below which the yaw rate scales down with speed …
        public const double CrawlSteerFloor = 0.4;     // … to no less than this share (a stopped car still turns)
        public const double AssistMaxDegPerSec = 8.0;  // lane assist yaw towards the cardinal
        public const double AssistFadeDeg = 25.0;      // … fading out over this intent angle
        public const double AssistOffDeflection = 0.4; // hard off while the thumb is ≥ 40 % deflected
        public const double LaneLateralRate = 1.8;     // 1/s spring towards the lane centre
        public const double CarRadius = 1.1;           // half-width kept off the kerb
        public const double WallSpeedMult = 0.55;
        public const double StopRadius = 7.0;          // metres within which a place counts as reached
        public const double ParkTolerance = 20.0;      // a late tap still parks within this (braking from cruise needs a few metres)
        public const double JustPassedHold = 12.0;     // the pill keeps a just-passed door this far — shorter than ParkTolerance so the MART row names MART
        public const double OfferRadius = 45.0;        // metres ahead within which the pill offers a place
        public const double FuelPerMetre = 0.00002 * 3.6;

        public sealed class State
        {
            public double X, Z;           // world position (CityGrid coordinates)
            public double Heading;        // radians, 0 = +z (south), π/2 = +x (east); Unity yaw = Heading in degrees
            public double Kmh;            // signed: negative = reverse
            public double Fuel = 0.72;
            public double Driven;         // metres, absolute
            public double SteerSmoothed;
            public bool Hit;              // a kerb/wall contact this step
            public double RingEntryHeading = double.NaN;   // the cardinal we entered the current roundabout on (NaN: not on one)
            public bool RingLeaving;                       // past the exit point: the lane assist takes the car off the plaza

            public State() { var p = HomeStart(); X = p.x; Z = p.z; Heading = p.heading; }

            public double HeadingDeg => Heading * 180.0 / Math.PI;
            public double ForwardX => Math.Sin(Heading);
            public double ForwardZ => Math.Cos(Heading);
            public double RightX => Math.Cos(Heading);
            public double RightZ => -Math.Sin(Heading);
        }

        public struct Input
        {
            public double Throttle;   // 1 forward, -1 reverse, 0 coast
            public bool Brake;
            public double Steer;      // -1 left … 1 right (screen sense under the chase camera)
            public double? Cruise;    // forward target speed override in km/h (approaching a place), else CruiseKmh
            public double? FollowGap; // metres of clear road to the ambient car ahead in our lane (null = free road)
            public double FollowKmh;  // that car's speed
            public bool RedLight;     // a red light within Traffic.LookAheadM — auto-brake unless reversing
        }

        /// <summary>The auto-throttle eases off behind a slower car in the same lane from this gap …</summary>
        public const double FollowM = 16.0;
        /// <summary>… and holds this gap behind it (the player overtakes by steering into the other lane).</summary>
        public const double FollowMinM = 6.0;

        /// <summary>
        /// Cruise cap behind the car in front: its speed at FollowM, a crawl at FollowMinM, nothing on a free road —
        /// so the ambient traffic is something to drive behind and around instead of through.
        /// </summary>
        public static double? FollowCap(double? gapM, double leadKmh)
        {
            if (gapM == null || gapM.Value >= FollowM) return null;
            var t = Clamp((gapM.Value - FollowMinM) / (FollowM - FollowMinM), 0.0, 1.0);
            return Math.Max(0.0, leadKmh) * (0.3 + 0.7 * t);
        }

        public sealed class Place
        {
            public string Id, NameKey, Icon;
            public int Side;          // -1 = a place of the town plan, 1 = home (legacy flag kept for the HUD/tests), 2 = the ranch exit
            public string Dialog;     // city/dialoge tree id, null → no shopkeeper talk (home, park, airport)
            public string Shop;       // ShopLogic assortment path for the generic counter, null → none
            public string Counter;    // what "laden" / "Zum Tresen" opens: shop | pow | baumarkt | markt | autohaus | post | park | travel
            public string MapId;      // id in city/city_map.json ("rehwei" is the GOOBY MART of the boards)
            public string Typ;        // town-plan kind: laden / praxis / amt / markt / freizeitpark / flughafen / zuhause
            public string Tint;       // facade tint (#rrggbb) from the town plan
            public bool Awning;       // striped awning over the door (town plan)
            public string Distrikt;
            public int[] Tile = { 0, 0 };        // the place's (first) tile (r, c)
            public int[] StreetTile = { 0, 0 };  // the street tile in front of the door
            public double X, Z;                   // parking spot at the kerb in front of the door (world)
            public double FaceX, FaceZ;           // unit direction from the door towards the street
            public double Yaw;                    // degrees: the front of the building looks this way (0 = +z)

            public bool HasCounter => Counter != null;
        }

        public const string CounterShop = "shop", CounterPow = "pow", CounterBaumarkt = "baumarkt", CounterMarkt = "markt",
            CounterAutohaus = "autohaus", CounterPost = "post", CounterPark = "park", CounterTravel = "travel",
            CounterStation = "station";

        /// <summary>
        /// The places of the town plan (city_map.json) with their shopkeeper tree, counter and assortment. The GOOBY
        /// MART of board 03 is REHWEI of the plan (Frau Rehwald's grocery); the Funkelpark and the airport have no
        /// shopkeeper tree, their counter opens straight away. Order = the drive order of the guided tour.
        /// </summary>
        public static readonly (string id, string mapId, string nameKey, string icon, string dialog, string counter, string shop)[] Street =
        {
            ("mart", "rehwei", "nl.stadt.ort_mart", "shop", "rehwei", CounterShop, ShopLogic.RehweiPath),
            ("goobyman", "goobyman", "city.ort.goobyman", "sparkle", "goobyman", CounterShop, ShopLogic.GoobymanPath),
            ("tierarzt", "tierarzt", "nl.stadt.ort_tierarzt", "heart", "tierarzt", null, null),
            ("gouhbus", "gouhbus", "city.ort.gouhbus", "heart", "gouhbus", null, null),
            ("goobytheke", "goobytheke", "nl.stadt.ort_goobytheke", "leaf", "goobytheke", CounterShop, ShopLogic.GoobythekePath),
            ("post", "post", "city.ort.post", "mail", "post", CounterPost, null),
            ("pow", "pow", "city.ort.pow", "star", "pow", CounterPow, ShopLogic.PowPath),
            ("baumarkt", "baumarkt", "city.ort.baumarkt", "hammer", "baumarkt", CounterBaumarkt, ShopLogic.BaumarktPath),
            ("autohaus", "autohaus", "city.ort.autohaus", "car", "autohaus", CounterAutohaus, ShopLogic.CarsPath),
            ("wochenmarkt", "wochenmarkt", "city.ort.wochenmarkt", "leaf", "wochenmarkt", CounterMarkt, ShopLogic.MarktWarenPath),
            ("funkelpark", "funkelpark", "park.ort.funkelpark", "star", null, CounterPark, null),
            ("flughafen", "flughafen", "city.ort.flughafen", "compass", null, CounterTravel, null),
        };

        /// <summary>Tiles of the plan's places when the plan itself is not readable yet (same numbers as city_map.json).</summary>
        private static readonly Dictionary<string, (int r, int c, int sr, int sc)> FallbackTiles = new Dictionary<string, (int, int, int, int)>
        {
            { "rehwei", (5, 2, 5, 1) }, { "goobytheke", (6, 2, 6, 1) }, { "gouhbus", (6, 11, 6, 10) }, { "tierarzt", (6, 4, 7, 4) },
            { "flughafen", (0, 2, 0, 4) }, { "baumarkt", (2, 7, 1, 7) }, { "post", (5, 11, 4, 11) }, { "pow", (5, 9, 5, 10) },
            { "autohaus", (2, 11, 2, 10) }, { "wochenmarkt", (6, 7, 6, 6) }, { "goobyman", (6, 5, 7, 5) }, { "funkelpark", (11, 7, 10, 7) },
        };

        /// <summary>
        /// The road out of town to the Gooby ranch (ranch_exit.gd): the dead end of row 7 at the map's east edge, the gate
        /// and the sign "Zur Gooby-Ranch" on the block beside it. Not part of the town plan, no taxi goes there — you drive out.
        /// </summary>
        public const string RanchId = "ranch";
        public static readonly int[] RanchTile = { 6, 14 }, RanchStreet = { CityGrid.RanchExit.r, CityGrid.RanchExit.c };

        private static List<Place> _places;
        private static bool _placesFromPlan;

        /// <summary>Home (the driveway) followed by the twelve places of <see cref="Street"/>; rebuilt once the plan is readable.</summary>
        public static IReadOnlyList<Place> Places
        {
            get
            {
                if (_places == null || (!_placesFromPlan && Content.ContentSource.Installed))
                {
                    _places = BuildPlaces();
                    _placesFromPlan = CityMap.Orte.Count > 0;
                }
                return _places;
            }
        }

        /// <summary>Forget the built places (tests swap the content source).</summary>
        public static void Reset() { _places = null; _placesFromPlan = false; }

        private static List<Place> BuildPlaces()
        {
            var list = new List<Place>();
            var home = new Place { Id = "home", NameKey = "nl.stadt.ort_zuhause", Icon = "home", Side = 1, MapId = "zuhause", Typ = "zuhause", Distrikt = "wohnen" };
            Locate(home, CityGrid.HomeTile, CityGrid.HomeStreet);
            list.Add(home);
            foreach (var (id, mapId, nameKey, icon, dialog, counter, shop) in Street)
            {
                var ort = CityMap.Get(mapId);
                var place = new Place
                {
                    Id = id, NameKey = nameKey, Icon = icon, Side = -1,
                    Dialog = dialog, Counter = counter, Shop = shop, MapId = mapId,
                    Typ = ort?.Typ ?? "laden", Tint = ort?.Tint ?? "", Awning = ort?.Awning ?? false, Distrikt = ort?.Distrikt ?? "",
                };
                int[] tile = ort?.Tile, street = ort?.Strasse;
                if ((tile == null || tile.Length < 2 || street == null || street.Length < 2) && FallbackTiles.TryGetValue(mapId, out var fb))
                {
                    tile = new[] { fb.r, fb.c };
                    street = new[] { fb.sr, fb.sc };
                }
                Locate(place, tile ?? new[] { 0, 0 }, street ?? new[] { 0, 0 });
                list.Add(place);
            }
            var ranch = new Place
            {
                Id = RanchId, NameKey = "ranch.exit.schild_zeile1", Icon = "horseshoe", Side = 2, MapId = RanchId,
                Typ = "ausfahrt", Distrikt = "rand",
            };
            Locate(ranch, RanchTile, RanchStreet);
            list.Add(ranch);
            return list;
        }

        /// <summary>Parking spot and facing of a place: on its street tile, pulled towards the door onto the near kerb.</summary>
        private static void Locate(Place p, int[] tile, int[] street)
        {
            p.Tile = tile; p.StreetTile = street;
            var (tx, tz) = CityGrid.World(tile[0], tile[1]);
            var (sx, sz) = CityGrid.World(street[0], street[1]);
            var dx = sx - tx; var dz = sz - tz;
            var len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) { dx = 0; dz = 1; len = 1; }
            p.FaceX = dx / len; p.FaceZ = dz / len;
            // the kerb-side lane: 2 m off the centre line towards the door
            p.X = sx - p.FaceX * CityGrid.LaneOffset;
            p.Z = sz - p.FaceZ * CityGrid.LaneOffset;
            p.Yaw = Math.Atan2(p.FaceX, p.FaceZ) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Where a fresh drive begins: pulled out of the driveway onto the home street, in the right-hand lane, heading
        /// the way the street runs longest (from the plan's home that is north along column 1 — home and the GOOBY MART
        /// lie on the left of that street, the cottages of the town's edge on the right, as on board 03).
        /// </summary>
        public static (double x, double z, double heading) HomeStart()
        {
            var tile = CityGrid.HomeTile; var street = CityGrid.HomeStreet;
            var (sx, sz) = CityGrid.World(street[0], street[1]);
            var (tx, tz) = CityGrid.World(tile[0], tile[1]);
            var fx = sx - tx; var fz = sz - tz;
            var len = Math.Sqrt(fx * fx + fz * fz); if (len < 1e-6) { fx = 0; fz = 1; len = 1; }
            fx /= len; fz /= len;
            // the street runs perpendicular to the door direction; pick the way with more street tiles ahead
            // (tile steps: Δc = −Δx/TileM, Δr = −Δz/TileM — see CityGrid.World)
            var (r, c) = (street[0], street[1]);
            var dirA = (dr: (int)Math.Round(-fx), dc: (int)Math.Round(fz));   // the perpendicular (−fz, fx) as a tile step
            var dirB = (dr: -dirA.dr, dc: -dirA.dc);
            var pick = Run(r, c, dirA) >= Run(r, c, dirB) ? dirA : dirB;
            var heading = Math.Atan2(-pick.dc, -pick.dr);                    // forward = (sin h, cos h) = (Δx, Δz) = (−dc, −dr)
            var rx = Math.Cos(heading); var rz = -Math.Sin(heading);         // right-hand lane
            return (sx + rx * CityGrid.LaneOffset, sz + rz * CityGrid.LaneOffset, heading);

            static int Run(int r0, int c0, (int dr, int dc) d)
            {
                var n = 0;
                while (CityGrid.IsStreet(r0 + d.dr * (n + 1), c0 + d.dc * (n + 1))) n++;
                return n;
            }
        }

        public static Place PlaceById(string id)
        {
            foreach (var p in Places) if (p.Id == id) return p;
            // GOOB-1 is not on the town plan — the shuttle place is synthesised after space is visited
            if (id == Raumstation.Id) return Raumstation.Place();
            return null;
        }

        public static double Distance(State s, Place p) { var dx = p.X - s.X; var dz = p.Z - s.Z; return Math.Sqrt(dx * dx + dz * dz); }

        /// <summary>Signed forward distance of a place along the car's heading (negative = behind).</summary>
        public static double Along(State s, Place p) => (p.X - s.X) * s.ForwardX + (p.Z - s.Z) * s.ForwardZ;

        /// <summary>One simulation step; speeds are in km/h, positions in metres.</summary>
        public static void Step(State s, Input input, double dt)
        {
            if (dt <= 0.0) return;
            var target = input.Brake ? 0.0 : input.Throttle > 0.0 ? (input.Cruise ?? CruiseKmh) : input.Throttle < 0.0 ? -ReverseKmh : 0.0;
            var cap = FollowCap(input.FollowGap, input.FollowKmh);
            var capped = cap.HasValue && target > cap.Value;
            if (capped) target = cap.Value;
            // a red light ahead: the auto-throttle brakes like a car in traffic (the player can still reverse or hold the pedal)
            if (input.RedLight && !input.Brake && input.Throttle >= 0.0 && target > 0.0) { target = 0.0; capped = true; }
            // a roundabout ahead: the auto-throttle slows to ring speed for the plaza
            if (target > RingKmh && RoundaboutNear(s.X, s.Z) != null) { target = RingKmh; capped = true; }
            // closing in on the car in front (which may be braking for a light) the auto-throttle brakes, it does not coast
            var rate = input.Brake || (capped && target < s.Kmh) ? BrakeRate : Math.Abs(target) > Math.Abs(s.Kmh) ? ThrottleRate : CoastRate;
            s.Kmh = MoveTowards(s.Kmh, target, rate * dt);
            var v = s.Kmh / 3.6;

            var steer = Clamp(input.Steer, -1.0, 1.0);
            s.SteerSmoothed += (steer - s.SteerSmoothed) * (1.0 - Math.Exp(-dt / SteerTau));
            if (Math.Abs(v) > 0.05)
            {
                var damp = 1.0 - 0.25 * Math.Min(1.0, Math.Abs(v) / (CruiseKmh / 3.6));
                // creeping (nosed into a kerb at a bend) the wheel turns the car slowly, like a real car's yaw rate
                // grows with speed — a second on the stick must not flip a stopped car into a U-turn
                var crawl = Clamp(Math.Abs(v) / CrawlSteerSpeed, CrawlSteerFloor, 1.0);
                var yaw = Clamp(s.SteerSmoothed * SteerRate * damp * crawl, -SteerCapDegPerSec * Math.PI / 180.0, SteerCapDegPerSec * Math.PI / 180.0);
                // reversing: the tail swings to the thumb side
                s.Heading = Wrap(s.Heading + yaw * dt * (v < 0 ? -1.0 : 1.0));
                if (v > 0 && !RoundaboutAssist(s, steer, dt)) LaneAssist(s, steer, dt);
            }
            var mx = s.ForwardX * v * dt;
            var mz = s.ForwardZ * v * dt;
            var (cx, cz, hit) = CityGrid.ClampToStreets(s.X + mx, s.Z + mz, CarRadius);
            if (hit && !s.Hit) s.Kmh *= WallSpeedMult;   // one speed loss per contact episode
            if (hit)
            {
                // leaning on the kerb: the car only goes as fast as it actually moves (head-on it stands and creeps)
                var moved = Math.Sqrt((cx - s.X) * (cx - s.X) + (cz - s.Z) * (cz - s.Z)) / dt;
                var actualKmh = moved * 3.6;
                if (Math.Abs(s.Kmh) > actualKmh + 3.0) s.Kmh = Math.Sign(s.Kmh) * (actualKmh + 3.0);
                if (v > 0.5 && Math.Abs(steer) < AssistOffDeflection) KerbAssist(s, cx - (s.X + mx), cz - (s.Z + mz), dt);
            }
            s.Hit = hit;
            var travelled = Math.Sqrt((cx - s.X) * (cx - s.X) + (cz - s.Z) * (cz - s.Z));
            s.X = cx; s.Z = cz;
            s.Driven += travelled;
            s.Fuel = Math.Max(0.0, s.Fuel - travelled * FuelPerMetre);
        }

        public const double KerbAssistDegPerSec = 40.0;

        /// <summary>
        /// Scraping along a kerb with the thumb off the stick (a turn taken too wide, a bend hit at an angle), the car
        /// straightens out along the kerb instead of grinding on forever at a slant — the lane assist alone gives up
        /// beyond 25° of intent. Head-on into a kerb it stays put: the player picks the way round.
        /// </summary>
        private static void KerbAssist(State s, double pushX, double pushZ, double dt)
        {
            var len = Math.Sqrt(pushX * pushX + pushZ * pushZ);
            if (len < 1e-6) return;
            var nx = pushX / len; var nz = pushZ / len;      // the kerb's normal (into the road)
            var tx = -nz; var tz = nx;                        // its tangent …
            if (tx * s.ForwardX + tz * s.ForwardZ < 0) { tx = -tx; tz = -tz; }   // … the way we are going
            var along = tx * s.ForwardX + tz * s.ForwardZ;
            if (along < 0.35) return;                         // near head-on: no opinion
            var want = Math.Atan2(tx, tz);
            var diff = Wrap(want - s.Heading);
            var maxTurn = KerbAssistDegPerSec * Math.PI / 180.0 * dt;
            s.Heading = Wrap(s.Heading + Math.Sign(diff) * Math.Min(Math.Abs(diff), maxTurn));
        }

        public const double RingKmh = 22.0;               // the auto-throttle's speed on a roundabout plaza
        public const double RingAssistDegPerSec = 150.0;  // how fast the assist bends the car onto the ring
        public const double RingPullRate = 6.0;           // position spring onto the ring (1/s)
        public const double RingExitDeg = 12.0;           // the tangent this close to the entry cardinal on the far side: leave

        /// <summary>The centre of the roundabout whose plaza (plus a metre) the point is on, or null.</summary>
        public static (double x, double z)? RoundaboutNear(double x, double z)
        {
            var (r0, c0) = CityGrid.Tile(x, z);
            for (var r = r0 - 1; r <= r0 + 1; r++)
                for (var c = c0 - 1; c <= c0 + 1; c++)
                {
                    if (!CityGrid.IsRoundabout(r, c)) continue;
                    var (cx, cz) = CityGrid.World(r, c);
                    var d = Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                    if (d <= CityGrid.PlazaRadius + 1.0) return (cx, cz);
                }
            return null;
        }

        /// <summary>
        /// The roundabout taken by itself (thumb off the stick): on the plaza the assist bends the car onto the ring and
        /// round the island counter-clockwise — the island on the left, as the ambient traffic drives — and, once the
        /// far side is reached (the ring's tangent points the way the car came in and the car is past the centre), lets
        /// the lane assist take it out along the arm: straight through without a touch. Steering right leaves early,
        /// steering left keeps the car on the ring past the exit; the thumb on the stick switches the assist off.
        /// Returns true while it steers.
        /// </summary>
        private static bool RoundaboutAssist(State s, double steer, double dt)
        {
            var near = RoundaboutNear(s.X, s.Z);
            if (near == null) { s.RingEntryHeading = double.NaN; s.RingLeaving = false; return false; }
            if (Math.Abs(steer) >= AssistOffDeflection) return false;   // the thumb decides
            if (s.RingLeaving) return false;                             // on the way out along the arm
            var (cx, cz) = near.Value;
            var dx = s.X - cx; var dz = s.Z - cz;
            var rho = Math.Sqrt(dx * dx + dz * dz);
            if (rho < 1e-6) return false;
            if (double.IsNaN(s.RingEntryHeading)) s.RingEntryHeading = Math.Round(s.Heading / (Math.PI / 2.0)) * (Math.PI / 2.0);
            var phi = Math.Atan2(dz, dx);
            var tx = -Math.Sin(phi); var tz = Math.Cos(phi);      // the counter-clockwise tangent of the ring here
            var tangentHeading = Math.Atan2(tx, tz);
            // the exit: level with the centre on the far side, the ring's tangent points the way the car came in —
            // from here the lane assist steers it out along the arm (and the ring lets go until the plaza is behind it)
            var atCentreLine = dx * Math.Sin(s.RingEntryHeading) + dz * Math.Cos(s.RingEntryHeading) > -0.4;
            if (atCentreLine && Math.Abs(Wrap(tangentHeading - s.RingEntryHeading)) < RingExitDeg * Math.PI / 180.0) { s.RingLeaving = true; return false; }
            var diff = Wrap(tangentHeading - s.Heading);
            var maxTurn = RingAssistDegPerSec * Math.PI / 180.0 * dt;
            s.Heading = Wrap(s.Heading + Math.Sign(diff) * Math.Min(Math.Abs(diff), maxTurn));
            var k = Math.Min(1.0, RingPullRate * dt);
            s.X += (cx + dx / rho * CityGrid.RingRadius - s.X) * k;
            s.Z += (cz + dz / rho * CityGrid.RingRadius - s.Z) * k;
            return true;
        }

        /// <summary>
        /// Lane assist spring (§C7.2): eases the heading towards the nearest cardinal and the position towards the
        /// right-hand lane of the tile's centre line; fades over 25° of intent and is off at ≥ 40 % deflection.
        /// </summary>
        private static void LaneAssist(State s, double steer, double dt)
        {
            if (Math.Abs(steer) >= AssistOffDeflection) return;
            var cardinal = Math.Round(s.Heading / (Math.PI / 2.0)) * (Math.PI / 2.0);
            var diff = Wrap(cardinal - s.Heading);
            var fade = Math.Max(0.0, 1.0 - Math.Abs(diff) / (AssistFadeDeg * Math.PI / 180.0));
            if (fade <= 0.0) return;
            var maxTurn = AssistMaxDegPerSec * Math.PI / 180.0 * fade * dt;
            s.Heading = Wrap(s.Heading + Math.Sign(diff) * Math.Min(Math.Abs(diff), maxTurn));
            // lane centre = tile centre line + LaneOffset to the right of the cardinal heading
            var (r, c) = CityGrid.Tile(s.X, s.Z);
            var (cx, cz) = CityGrid.World(r, c);
            var k = Math.Min(1.0, LaneLateralRate * fade * dt);
            var rx = Math.Cos(cardinal); var rz = -Math.Sin(cardinal);
            if (Math.Abs(rx) > 0.5) s.X += (cx + Math.Sign(rx) * CityGrid.LaneOffset - s.X) * k;   // driving along z: lane is offset in x
            else s.Z += (cz + Math.Sign(rz) * CityGrid.LaneOffset - s.Z) * k;                       // driving along x: lane is offset in z
        }

        /// <summary>
        /// Input that rolls the car up to a place and stops at its kerb: steers towards the parking spot, the target
        /// speed shrinks with the distance (never below a crawl) and the brake goes on within the last two metres.
        /// Null once the spot is behind the car and out of <see cref="ParkTolerance"/>.
        /// </summary>
        public static Input? Approach(State s, Place place, double steer = 0.0)
        {
            var d = Distance(s, place);
            var along = Along(s, place);
            if (along < 0 && d > ParkTolerance * 1.5) return null;
            // at the spot, or already past its door (late tap): brake and park right here — a slow frame must not
            // turn a tap into a missed stop
            if (d <= 2.0 || along < 0) return new Input { Brake = true, Steer = steer };
            // pure pursuit towards the spot, straightening out over the last metres so the car parks parallel to the kerb
            var want = Math.Atan2(place.X - s.X, place.Z - s.Z);
            var turn = Wrap(want - s.Heading);
            var aim = Clamp(turn * 1.6 * Math.Min(1.0, d / 6.0), -1.0, 1.0);
            if (Math.Abs(steer) > 0.2) aim = steer;   // the thumb overrides
            var cruise = Math.Max(6.0, Math.Min(CruiseKmh, d * 1.6));
            return new Input { Throttle = 1.0, Cruise = cruise, Steer = aim };
        }

        /// <summary>The place the car is stopped next to (slow and within StopRadius), or null while driving past.</summary>
        public static Place Reached(State s)
        {
            if (Math.Abs(s.Kmh) > 4.0) return null;
            return Nearby(s);
        }

        /// <summary>True once the car has come to rest at the place it was told to stop at (within ParkTolerance).</summary>
        public static bool ParkedAt(State s, Place target)
        {
            if (target == null || Math.Abs(s.Kmh) > 4.0) return false;
            return Distance(s, target) <= ParkTolerance;
        }

        /// <summary>The nearest place within StopRadius regardless of speed (the HUD pill shows it while approaching).</summary>
        public static Place Nearby(State s)
        {
            Place best = null;
            var bestD = double.MaxValue;
            foreach (var p in Places)
            {
                var d = Distance(s, p);
                if (d <= StopRadius && d < bestD) { best = p; bestD = d; }
            }
            return best;
        }

        /// <summary>
        /// After Nearby: the pill keeps a door just passed, unless the next shop ahead is closer (home street: MART
        /// takes over from GOOBYTHEKE once it is the nearer stop).
        /// </summary>
        public static Place PassedOrAhead(State s, out double metres)
        {
            var (ahead, m) = Ahead(s);
            var passed = JustPassed(s);
            if (passed != null && (ahead == null || Distance(s, passed) <= m))
            {
                metres = 0.0;
                return passed;
            }
            metres = m;
            return m <= OfferRadius ? ahead : null;
        }

        /// <summary>
        /// A place the car has just driven past (behind it, within <see cref="JustPassedHold"/>, beyond StopRadius) —
        /// still offered by the pill. Parking itself still uses <see cref="ParkTolerance"/> so a late tap can stop.
        /// </summary>
        public static Place JustPassed(State s)
        {
            Place best = null;
            var bestD = double.MaxValue;
            foreach (var p in Places)
            {
                var d = Distance(s, p);
                if (d <= StopRadius || d > JustPassedHold || Along(s, p) >= 0) continue;
                if (d < bestD) { best = p; bestD = d; }
            }
            return best;
        }

        /// <summary>
        /// The next place for the pill hint: the nearest one in front of the car (forward half-plane, beyond
        /// StopRadius); if nothing lies ahead, the nearest one anywhere. Metres = straight-line distance.
        /// </summary>
        public static (Place place, double metres) Ahead(State s)
        {
            Place best = null;
            var bestD = double.MaxValue;
            foreach (var p in Places)
            {
                var d = Distance(s, p);
                if (d <= StopRadius || Along(s, p) < 0) continue;
                if (d < bestD) { best = p; bestD = d; }
            }
            if (best != null) return (best, bestD);
            foreach (var p in Places)
            {
                var d = Distance(s, p);
                if (d <= StopRadius) continue;
                if (d < bestD) { best = p; bestD = d; }
            }
            return (best, bestD);
        }

        public static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private static double MoveTowards(double current, double target, double maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
