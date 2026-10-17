using System.Collections.Generic;
using Gooby.Core.City;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Sonnental as a whole town (board 03 look on the town plan of city_map.json): the 15×12 street lattice with
    /// junctions, zebra crossings and lane dashes; the twelve places on their plan tiles with the Blender fronts,
    /// lettered signs and doorstep dressing, flanked by town houses so every shop is one house of a closed street
    /// front; district blocks in between (closed three-house terraces along every street side in the centre and the
    /// trade district with poplars and sheds in the back yards, cottages behind picket fences and hedges in the
    /// residential district, the big blue GOOBEA store, the park with its church tower, the Funkelpark with the
    /// ferris wheel, the airport apron); the clock tower closing the vista of the home street; lamp posts with
    /// banners, trees, hedges and planters along the pavements; home as the cottage with its driveway; the hero car;
    /// and — turning with the camera — the painted mountain horizon. All buildings and street furniture are Blender
    /// scenery models (tools/blender/build_scenery.py), streets and pavements are generated here.
    /// </summary>
    public sealed class CityScene
    {
        public GameObject Root { get; }
        public Transform Car { get; }

        private const float Tile = (float)CityGrid.TileM;
        private const float RoadHalf = (float)CityGrid.RoadHalfWidth;
        private const float Walk = (float)CityGrid.WalkWidth;
        private const float WalkY = 0.12f;   // pavement top above the asphalt
        private const float PromenadeWidth = 4.8f;   // paving from the pavement's outer edge up to the facade line of the house rows
        // the painting stands 330 m out; `bg_town_sky` is the horizon with half a painting of sky added on top
        // (build_art.extend_sky). At 0.18 m per source row the painted tree line sits ≈9° and the ridge ≈12° above
        // the chase camera, the top edge at ≈28° — board 03's frame (ridge low, a fifth of the frame blue) never
        // runs off the painting, and the town's far blocks still stand in front of it.
        private const float HorizonDistance = 330f, HorizonHeight = 185f, HorizonGround = -12f;
        private const string HorizonTexture = "bg_town_sky";

        private static readonly string[] TownHouses = { "house_pink", "house_blue", "house_salmon", "house_cream", "house_yellow", "house_green" };
        private static readonly string[] Cottages = { "cottage_green", "cottage_blue", "cottage_pink", "house_yellow", "cottage_green", "house_pink" };

        /// <summary>
        /// Front per place of the town plan: scenery model, lettering position in the model's frame (+Z = street)
        /// and lettering size. The GOOBY MART is the coral bay-window shop of board 03; vet and pharmacy keep their houses.
        /// </summary>
        private static readonly Dictionary<string, (string model, Vector3 sign, Vector2 size)> PlaceFronts = new Dictionary<string, (string, Vector3, Vector2)>
        {
            { "mart", ("shop_mart", new Vector3(-0.20f, 2.38f, 3.86f), new Vector2(4.6f, 0.95f)) },
            { "goobyman", ("shop_goobyman", new Vector3(0f, 3.62f, 3.06f), new Vector2(2.4f, 0.6f)) },
            { "tierarzt", ("house_yellow", new Vector3(0f, 3.62f, 3.06f), new Vector2(2.4f, 0.6f)) },
            { "gouhbus", ("shop_gouhbus", new Vector3(0f, 3.62f, 3.06f), new Vector2(2.5f, 0.64f)) },
            { "goobytheke", ("house_green", new Vector3(0f, 3.62f, 3.06f), new Vector2(2.4f, 0.6f)) },
            { "post", ("shop_post", new Vector3(0f, 3.62f, 3.06f), new Vector2(2.2f, 0.6f)) },
            { "pow", ("shop_pow", new Vector3(0f, 3.62f, 3.06f), new Vector2(2.4f, 0.6f)) },
            { "baumarkt", ("shop_baumarkt", new Vector3(0f, 3.62f, 3.26f), new Vector2(2.7f, 0.64f)) },
            { "autohaus", ("shop_autohaus", new Vector3(0f, 3.62f, 3.26f), new Vector2(3.9f, 0.62f)) },
            { "wochenmarkt", ("market_sign", new Vector3(0f, 1.55f, 0.13f), new Vector2(1.6f, 0.66f)) },
            { "funkelpark", ("park_gate", new Vector3(0f, 5.2f, 0.7f), new Vector2(5.6f, 0.68f)) },
            { "flughafen", ("airport_terminal", new Vector3(0f, 3.62f, 3.76f), new Vector2(4.0f, 0.62f)) },
        };

        /// <summary>Sign lettering texture per place (tools/art/build_art.py).</summary>
        public static string SignOf(string placeId) => placeId == "mart" ? "sign_gooby_mart" : "sign_" + placeId;

        private readonly Transform _horizon;
        private readonly List<Vector3> _lampHeads = new List<Vector3>();
        private readonly List<Light> _lampLights = new List<Light>();
        private readonly List<Light> _headlights = new List<Light>();
        private GameObject _nightGlow;
        private bool _night;
        private readonly System.Random _rng;
        private readonly HashSet<(int, int)> _taken = new HashSet<(int, int)>();

        // shared materials (asphalt and pavement carry the CC0 grain/relief, tiled per slab size in Asphalt()/Walk())
        private static readonly Color AsphaltTint = ArtLibrary.Hex("#626270");
        private static readonly Color WalkTint = ArtLibrary.Hex("#D8CDD0");
        private readonly Material _kerb = ArtLibrary.Solid(ArtLibrary.Hex("#E4DADB"), 0.12f);
        private readonly Material _paint = ArtLibrary.Solid(ArtLibrary.Hex("#FFF4E0"), 0f);

        private static Material Asphalt(Vector3 size) => ArtLibrary.PbrSlab("asphalt", new Vector2(size.x, size.z), 3.2f, AsphaltTint, 0.1f, 1.0f);
        private static Material Pavement(Vector3 size) => ArtLibrary.PbrSlab("paving", new Vector2(size.x, size.z), 1.5f, WalkTint, 0.14f, 1.1f);

        public CityScene(Transform parent, double hour = 12.0)
        {
            Root = new GameObject("City");
            Root.transform.SetParent(parent, false);
            var t = Root.transform;
            _rng = new System.Random(CityGrid.Seed);

            BuildGround(t);
            MarkPlaceSides();
            foreach (var (r, c) in CityGrid.StreetTiles) BuildStreetTile(t, r, c);
            // only the tile a place's front stands on is its own; the further tiles of its footprint get the
            // ordinary block dressing (house rows towards their streets), so no gap opens in the street front
            foreach (var p in DriveModel.Places) _taken.Add((p.Tile[0], p.Tile[1]));
            _taken.Add((CityGrid.KitchenTile[0], CityGrid.KitchenTile[1]));
            foreach (var l in CityGrid.Landmarks) if (l.Id == "ikea") { BuildStore(t, l); }
            BuildHome(t, DriveModel.PlaceById("home"));
            foreach (var p in DriveModel.Places)
            {
                if (p.Id == DriveModel.RanchId) BuildRanchExit(t, p);
                else if (p.Id != "home") BuildPlace(t, p);
            }
            BuildKitchen(t);
            BuildVistaTower(t);
            for (var r = 0; r < CityGrid.Rows; r++)
                for (var c = 0; c < CityGrid.Cols; c++)
                    if (!CityGrid.IsStreet(r, c) && !_taken.Contains((r, c))) BuildBlock(t, r, c);
            BuildWelcomeSign(t);

            Car = BuildCar(t);
            Life = new CityLife(t, hour);

            // the painted mountains ride along with the camera (FollowBackdrop turns them to the driving direction);
            // far beyond the town's edge, so they never cut through the far blocks
            _horizon = ArtLibrary.Horizon(t, HorizonTexture, new Vector3(0f, HorizonGround, 0f), HorizonHeight);
            _clouds = BuildClouds(t);
        }

        // ── clouds ──────────────────────────────────────────────────────────

        private readonly Transform _clouds;
        private Material _cloudMat, _cloudShade;
        private float _cloudDrift;
        private const float CloudDriftDegPerSec = 0.35f;
        private const int CloudCount = 18;
        private static readonly Color CloudDay = ArtLibrary.Hex("FFFDF7"), CloudDayShade = ArtLibrary.Hex("D4DFF1");
        private static readonly Color CloudNight = ArtLibrary.Hex("6C7396"), CloudNightShade = ArtLibrary.Hex("4C547A");

        /// <summary>
        /// Board 03's sky: a ring of painterly cumulus over the town — each cloud a row of domed puffs with a flat,
        /// cooler underside, 42–80 m up and 180–290 m out (8–24° above the chase camera: in the band between the
        /// painted ridge and the top of the frame). Both tones are unlit: the sun's angle must never grey a cloud into
        /// a boulder. The ring rides with the camera and turns slowly about it (FollowBackdrop); at night it goes slate.
        /// </summary>
        private Transform BuildClouds(Transform parent)
        {
            var root = new GameObject("Clouds");
            root.transform.SetParent(parent, false);
            // own instances: the cached Glow materials are shared, and night recolours these two
            _cloudMat = new Material(ArtLibrary.Glow(CloudDay)) { name = "cloud:top" };
            _cloudShade = new Material(ArtLibrary.Glow(CloudDayShade)) { name = "cloud:under" };
            var rng = new System.Random(_seedOf("clouds"));
            for (var i = 0; i < CloudCount; i++)
            {
                var angle = (i + 0.5f) * (360f / CloudCount) + (float)rng.NextDouble() * 14f - 7f;
                var dist = 180f + (float)rng.NextDouble() * 110f;
                var height = 42f + (float)rng.NextDouble() * 38f;
                var centre = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * dist, height, Mathf.Cos(angle * Mathf.Deg2Rad) * dist);
                var cloud = new GameObject("Cloud").transform;
                cloud.SetParent(root.transform, false);
                cloud.position = centre;
                // the row runs along `right`; yaw = ring angle makes `right` the ring's tangent, so the row lies
                // across the line of sight (yaw + 90° laid it along the sight line and every cloud read as one disc)
                cloud.rotation = Quaternion.Euler(0f, angle, 0f);
                var scale = 1.0f + (float)rng.NextDouble() * 1.0f;
                var puffs = 4 + rng.Next(4);
                var step = 8.5f * scale;
                var x = -(puffs - 1) * 0.5f * step;
                // a cumulus pile, not a plate: every dome is nearly as tall as it is wide and stands on the same
                // flat base plane (cloud.position), so the big middle domes rise highest and the silhouette gets the
                // board's cauliflower bumps; the two middle domes carry a smaller dome on top. Flattened, sunken
                // ellipsoids here showed the chase camera (10–20° below) nothing but their undersides — one lens.
                for (var k = 0; k < puffs; k++)
                {
                    var mid = 1f - Mathf.Abs(k - (puffs - 1) * 0.5f) / ((puffs - 1) * 0.5f + 0.5f);   // the middle puffs are the big ones
                    var r = (6.0f + mid * 6.5f + (float)rng.NextDouble() * 2f) * scale;
                    var foot = cloud.position + cloud.right * x + cloud.forward * ((float)rng.NextDouble() - 0.5f) * 3f * scale;
                    var puff = ArtLibrary.Sphere(cloud, "Puff", foot + Vector3.up * (r * 0.72f), r, _cloudMat, false);
                    puff.transform.localScale = new Vector3(r * 2.0f, r * 1.9f, r * 1.8f);
                    puff.GetComponent<Renderer>().receiveShadows = false;
                    if (mid > 0.6f)
                    {
                        var r2 = r * (0.5f + (float)rng.NextDouble() * 0.15f);
                        var crown = ArtLibrary.Sphere(cloud, "Crown", foot + cloud.right * (((float)rng.NextDouble() - 0.5f) * 0.6f * r) + Vector3.up * (r * 1.35f), r2, _cloudMat, false);
                        crown.transform.localScale = new Vector3(r2 * 2.0f, r2 * 1.8f, r2 * 1.8f);
                        crown.GetComponent<Renderer>().receiveShadows = false;
                    }
                    x += step;
                }
                // the shaded belly: one thin lens in the shade tone along the base plane, inset from the domes in
                // width and depth, so from the street it is the flat, cooler underside every cumulus on the board
                // has — a line, never a boulder. One mesh per cloud, not one per puff: sphere count is what the
                // software-rendered playtest player and a phone both pay for.
                var baseR = puffs * 4.0f * scale;
                var flat = ArtLibrary.Sphere(cloud, "Base", cloud.position - Vector3.up * (1.6f * scale), baseR, _cloudShade, false);
                flat.transform.localScale = new Vector3(baseR * 1.8f, baseR * 0.24f, baseR * 0.9f);
                flat.GetComponent<Renderer>().receiveShadows = false;
            }
            return root.transform;
        }

        private static int _seedOf(string salt) { unchecked { var h = 23; foreach (var ch in salt) h = h * 31 + ch; return h ^ CityGrid.Seed; } }

        /// <summary>Ambient cars, walkers and traffic lights; advance it with <see cref="TickLife"/>.</summary>
        public CityLife Life { get; }

        public void TickLife(float dt, double hour, Vector3 playerVelocity)
        {
            Life?.Tick(dt, hour, Car != null ? Car.position : Vector3.zero, playerVelocity);
            TickNight(dt);
        }

        private Vector3 W(double r, double c, float y = 0f)
        {
            var (x, z) = CityGrid.World(r, c);
            return new Vector3((float)x, y, (float)z);
        }

        private static float YawTo(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

        // ── ground and streets ─────────────────────────────────────────────

        private void BuildGround(Transform t)
        {
            var halfX = (float)CityGrid.WorldHalfX + 260f;
            var halfZ = (float)CityGrid.WorldHalfZ + 260f;
            var grass = ArtLibrary.PbrSlab("grass", new Vector2(halfX * 2f, halfZ * 2f), 2.6f, ArtLibrary.Hex("#74A882"), 0.04f, 0.6f);
            ArtLibrary.Box(t, "Meadow", new Vector3(0f, -0.11f, 0f), new Vector3(halfX * 2f, 0.2f, halfZ * 2f), grass, false);
        }

        /// <summary>
        /// One street tile: asphalt as a junction square plus an arm towards every street neighbour, pavement strips
        /// beside the arms with corner squares, a kerb-side strip where no arm leaves, lane dashes along through
        /// roads, zebra crossings on junction arms, a lamp post every third tile.
        /// </summary>
        private void BuildStreetTile(Transform t, int r, int c)
        {
            var centre = W(r, c);
            var arms = CityGrid.StreetNeighbours(r, c);
            var isJunction = arms.Count >= 3;
            var straight = arms.Count == 2 && arms[0].dr == -arms[1].dr && arms[0].dc == -arms[1].dc;
            var square = new Vector3(RoadHalf * 2f, 0.2f, RoadHalf * 2f);
            ArtLibrary.Box(t, "Asphalt", centre + new Vector3(0f, -0.1f, 0f), square, Asphalt(square), false);
            var round = CityGrid.IsRoundabout(r, c);
            var mouth = round ? (float)CityGrid.PlazaRadius + 1.3f : RoadHalf + 1.5f;   // where the zebra crossings lie
            foreach (var (dr, dc) in arms)
            {
                var dir = (W(r + dr, c + dc) - centre).normalized;   // world direction of the neighbour
                var side = new Vector3(dir.z, 0f, -dir.x);           // perpendicular
                var armLen = Tile * 0.5f - RoadHalf;
                var armCentre = centre + dir * (RoadHalf + armLen * 0.5f);
                var armSize = Abs(dir * armLen + side * RoadHalf * 2f) + new Vector3(0f, 0.2f, 0f);
                ArtLibrary.Box(t, "Arm", armCentre + new Vector3(0f, -0.1f, 0f), armSize, Asphalt(armSize), false);
                // pavement strips along the arm, from the corner square to the tile edge
                var stripLen = Tile * 0.5f - (RoadHalf + Walk);
                var stripCentre = centre + dir * (RoadHalf + Walk + stripLen * 0.5f);
                foreach (var s in new[] { -1f, 1f })
                {
                    var stripSize = Abs(dir * stripLen + side * Walk) + new Vector3(0f, WalkY + 0.1f, 0f);
                    ArtLibrary.Box(t, "Pavement", stripCentre + side * s * (RoadHalf + Walk * 0.5f) + new Vector3(0f, WalkY * 0.5f - 0.05f, 0f),
                        stripSize, Pavement(stripSize), false);
                    ArtLibrary.Box(t, "Kerb", stripCentre + side * s * (RoadHalf + 0.09f) + new Vector3(0f, WalkY * 0.5f - 0.05f, 0f),
                        Abs(dir * stripLen + side * 0.18f) + new Vector3(0f, WalkY + 0.11f, 0f), _kerb, false);
                }
                if (isJunction)
                {
                    // zebra crossing at the mouth of the arm
                    for (var k = -4; k <= 4; k++)
                        ArtLibrary.Box(t, "Zebra", centre + dir * mouth + side * (k * 0.8f) + new Vector3(0f, 0.006f, 0f),
                            Abs(dir * 2.2f + side * 0.42f) + new Vector3(0f, 0.01f, 0f), _paint, false);
                    for (var d = mouth + 3f; d < Tile * 0.5f; d += 6f)
                        ArtLibrary.Box(t, "Dash", centre + dir * d + new Vector3(0f, 0.006f, 0f), Abs(dir * 3f + side * 0.18f) + new Vector3(0f, 0.01f, 0f), _paint, false);
                }
            }
            // pavement along a closed side of the junction square and the four corner squares
            foreach (var (dr, dc) in new[] { (-1, 0), (0, 1), (1, 0), (0, -1) })
            {
                if (arms.Contains((dr, dc))) continue;
                var dir = (W(r + dr, c + dc) - centre).normalized;
                var side = new Vector3(dir.z, 0f, -dir.x);
                var edgeSize = Abs(dir * Walk + side * RoadHalf * 2f) + new Vector3(0f, WalkY + 0.1f, 0f);
                ArtLibrary.Box(t, "PavementEdge", centre + dir * (RoadHalf + Walk * 0.5f) + new Vector3(0f, WalkY * 0.5f - 0.05f, 0f),
                    edgeSize, Pavement(edgeSize), false);
                ArtLibrary.Box(t, "Kerb", centre + dir * (RoadHalf + 0.09f) + new Vector3(0f, WalkY * 0.5f - 0.05f, 0f),
                    Abs(dir * 0.18f + side * RoadHalf * 2f) + new Vector3(0f, WalkY + 0.11f, 0f), _kerb, false);
            }
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    ArtLibrary.Box(t, "PavementCorner", centre + new Vector3(sx * (RoadHalf + Walk * 0.5f), WalkY * 0.5f - 0.05f, sz * (RoadHalf + Walk * 0.5f)),
                        new Vector3(Walk, WalkY + 0.1f, Walk), Pavement(new Vector3(Walk, 0f, Walk)), false);
            if (straight)
            {
                // a through road: dashes over the whole tile length along the arm axis
                var dir = (W(r + arms[0].dr, c + arms[0].dc) - centre).normalized;
                var side = new Vector3(dir.z, 0f, -dir.x);
                for (var d = -Tile * 0.5f + 1.5f; d < Tile * 0.5f; d += 6f)
                    ArtLibrary.Box(t, "Dash", centre + dir * d + new Vector3(0f, 0.006f, 0f), Abs(dir * 3f + side * 0.18f) + new Vector3(0f, 0.01f, 0f), _paint, false);
            }
            else if (arms.Count == 2)
            {
                // a bend: a short dash into each arm
                foreach (var (dr, dc) in arms)
                {
                    var dir = (W(r + dr, c + dc) - centre).normalized;
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    ArtLibrary.Box(t, "Dash", centre + dir * (RoadHalf + 2.5f) + new Vector3(0f, 0.006f, 0f), Abs(dir * 3f + side * 0.18f) + new Vector3(0f, 0.01f, 0f), _paint, false);
                }
            }
            if (round)
            {
                // a real roundabout: a round asphalt plaza with a kerb rim (CityGrid.PlazaRadius — the drive model and
                // the traffic ring use the same numbers) and the flower island in the middle that nothing drives over
                var plazaR = (float)CityGrid.PlazaRadius; var islandR = (float)CityGrid.IslandRadius;
                ArtLibrary.Cylinder(t, "PlazaKerb", centre + new Vector3(0f, WalkY * 0.5f - 0.05f, 0f), plazaR + 0.5f, WalkY + 0.1f, _kerb, false);
                ArtLibrary.Cylinder(t, "Plaza", centre + new Vector3(0f, -0.099f, 0f), plazaR, 0.2f, ArtLibrary.Pbr("asphalt", 3.2f, AsphaltTint, 0.1f, 1.0f), false);
                // the ring the traffic follows, painted as a dashed circle
                for (var k = 0; k < 16; k += 2)
                {
                    var a = k * Mathf.PI * 2f / 16f;
                    var at = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ((float)CityGrid.RingRadius + 1.1f);
                    var dashRot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                    var dash = ArtLibrary.Box(t, "RingDash", at + new Vector3(0f, 0.006f, 0f), new Vector3(0.16f, 0.01f, 1.4f), _paint, false);
                    dash.transform.rotation = dashRot;
                }
                ArtLibrary.Cylinder(t, "IslandKerb", centre + new Vector3(0f, 0.1f, 0f), islandR, 0.2f, _kerb, false);
                var island = ArtLibrary.Solid(ArtLibrary.Hex("#9CCB8A"), 0.1f);
                ArtLibrary.Cylinder(t, "Island", centre + new Vector3(0f, 0.16f, 0f), islandR - 0.3f, 0.16f, island, false);
                Scenery.Place(t, "tree_small", centre + new Vector3(0f, 0.24f, 0f), r * 37f, 0.9f);
                ArtLibrary.Plant(t, "plant_tulips_red", centre + new Vector3(0.9f, 0.24f, 0.3f), 0.5f, 20f);
                ArtLibrary.Plant(t, "plant_daisies", centre + new Vector3(-0.8f, 0.24f, -0.5f), 0.45f, 70f);
                ArtLibrary.Plant(t, "plant_flowers_pink", centre + new Vector3(-0.3f, 0.24f, 0.9f), 0.45f, 130f);
            }
            StreetFurniture(t, r, c, centre, arms, straight);
        }

        /// <summary>
        /// Board 03's kerb line: lamp posts with the Gooby banner on both pavements, staggered so one stands every ten
        /// metres; round trees on the verge behind the pavement (not in front of a place's door), flowering bushes,
        /// benches and planters here and there, a bus stop now and then; flower beds on the free corners of the
        /// junctions (the others carry the traffic lights) and a lamp on the outer corner of a bend.
        /// </summary>
        private void StreetFurniture(Transform t, int r, int c, Vector3 centre, List<(int dr, int dc)> arms, bool straight)
        {
            var kerbLine = RoadHalf + Walk * 0.5f;      // the pavement's middle
            var verge = RoadHalf + Walk + 1.7f;          // just behind the pavement
            var roll = _rng.Next(1000);
            if (straight)
            {
                var dir = (W(r + arms[0].dr, c + arms[0].dc) - centre).normalized;
                var side = new Vector3(dir.z, 0f, -dir.x);
                foreach (var s in new[] { -1f, 1f })
                {
                    var lateral = side * s;
                    var facing = Mathf.Atan2(-lateral.x, -lateral.z) * Mathf.Rad2Deg;   // towards the street
                    LampPost(t, centre + dir * (5f * s) + lateral * kerbLine + Vector3.up * WalkY, facing);
                    var open = !_placeSides.Contains((r, c, (int)s));
                    // what stands behind this pavement decides the planting: in front of the closed house rows of the
                    // centre one tree per tile and planters (the shop fronts stay visible), elsewhere the full avenue
                    var beside = CityGrid.Tile(centre.x + lateral.x * Tile, centre.z + lateral.z * Tile);
                    var district = CityGrid.DistrictOf(beside.r, beside.c);
                    var rowSide = district == "zentrum" || district == "gewerbe";
                    if (rowSide)
                    {
                        // board 03's shopping street: the paving runs from the kerb right up to the house fronts
                        var promSize = Abs(dir * Tile + lateral * PromenadeWidth) + new Vector3(0f, WalkY + 0.1f, 0f);
                        ArtLibrary.Box(t, "Promenade", centre + lateral * (RoadHalf + Walk + PromenadeWidth * 0.5f) + new Vector3(0f, WalkY * 0.5f - 0.05f, 0f),
                            promSize, Pavement(promSize), false);
                    }
                    if (open && rowSide)
                    {
                        var up = Vector3.up * WalkY;
                        // board 03's shopping street: a lush round crown above the awnings on most tiles (the tree at
                        // the MART corner), a poplar column on the rest, so the shop fronts stay in view
                        var pick = (roll + (s > 0 ? 1 : 0)) % 4;
                        var kind = pick == 3 ? "tree_poplar" : pick == 2 ? "tree_tall" : "tree_round_big";
                        var size = pick == 3 ? 1.0f + (roll % 3) * 0.08f : pick == 2 ? 1.12f + (roll % 3) * 0.06f : 1.55f + (roll % 3) * 0.1f;
                        Scenery.Place(t, kind, centre - dir * (4f * s) + lateral * (verge - 0.2f) + up, roll * 0.7f + s * 40f, size);
                        Scenery.Place(t, (roll % 4) < 2 ? "bush_flowers" : "bush_big", centre + dir * (7.2f * s) + lateral * (verge - 0.4f) + up, roll * 0.3f, 0.6f);
                        Scenery.Place(t, "street_planter", centre + dir * (2.2f * s) + lateral * (verge - 0.6f) + up, roll * 0.19f);
                        if ((roll + (int)s) % 2 == 0) Scenery.Place(t, "street_planter", centre + dir * (4.6f * s) + lateral * (RoadHalf + Walk - 0.45f) + up, roll * 1.1f);
                    }
                    else if (open)
                    {
                        // two trees per side and tile (about every ten metres, like the avenue of board 03): round leafy
                        // crowns and slim poplar columns with a fir now and then — a flowering bush between them,
                        // flowers at the kerb and every third tile another poplar rising behind
                        var pick = (roll + (s > 0 ? 1 : 0)) % 4;
                        var first = pick == 1 ? "tree_big" : "tree_round_big";
                        var firstSize = pick == 1 ? 1.15f : 1.45f + (roll % 3) * 0.12f;
                        var second = pick == 2 ? "tree_round_big" : pick == 1 ? "tree_tall" : "tree_poplar";
                        var secondSize = pick == 2 ? 1.35f : pick == 1 ? 0.9f + (roll % 2) * 0.1f : 0.95f + (roll % 3) * 0.1f;
                        Scenery.Place(t, first, centre - dir * (3f * s) + lateral * verge, roll * 0.7f + s * 40f, firstSize);
                        Scenery.Place(t, second, centre + dir * (6.8f * s) + lateral * (verge + 0.4f), roll * 1.3f - s * 70f, secondSize);
                        Scenery.Place(t, (roll % 4) < 2 ? "bush_flowers" : "bush_big", centre - dir * (8f * s) + lateral * (verge - 0.3f), roll * 0.3f, 0.65f);
                        ArtLibrary.Plant(t, roll % 2 == 0 ? "plant_tulips_red" : "plant_daisies", centre - dir * (0.6f * s) + lateral * (verge - 0.5f), 0.5f, roll * 0.19f);
                        if ((roll + (int)s) % 2 == 0) ArtLibrary.Plant(t, "plant_flowers_pink", centre - dir * (5.5f * s) + lateral * (verge - 0.6f), 0.45f, roll * 0.41f);
                        if ((roll + r + (int)s) % 3 == 0 && district != "flughafen") Scenery.Place(t, "tree_poplar", centre + dir * (2f * s) + lateral * (verge + 1.6f), roll * 0.5f, 0.9f + (roll % 3) * 0.1f);
                    }
                    if (roll % 4 == (s > 0 ? 1 : 2))
                        Scenery.Place(t, "park_bench", centre - dir * (6.5f * s) + lateral * (RoadHalf + Walk - 0.5f) + Vector3.up * WalkY, facing);
                    else if (roll % 5 == (s > 0 ? 2 : 3))
                        Scenery.Place(t, "street_planter", centre + dir * (1.5f * s) + lateral * (RoadHalf + Walk - 0.45f) + Vector3.up * WalkY, roll * 1.7f);
                    if (open && (r * 31 + c * 17 + (s > 0 ? 5 : 0)) % 13 == 4)
                        Scenery.Place(t, "bus_stop", centre + dir * (1.5f * s) + lateral * (RoadHalf + Walk + 0.95f), facing);
                }
                return;
            }
            // corners of the tile as (sx, sz); the traffic lights of a junction stand at dir·edge − side·edge for each arm
            var lit = new HashSet<(int, int)>();
            if (arms.Count >= 3 && !CityGrid.IsRoundabout(r, c))
                foreach (var (dr, dc) in arms)
                {
                    var dir = (W(r + dr, c + dc) - centre).normalized;
                    var side = new Vector3(dir.z, 0f, -dir.x);
                    var p = dir - side;
                    lit.Add((p.x > 0 ? 1 : -1, p.z > 0 ? 1 : -1));
                }
            foreach (var sx in new[] { -1, 1 })
                foreach (var sz in new[] { -1, 1 })
                {
                    // the block diagonally behind this corner: in the centre the promenade paving fills the quadrant
                    var diagonal = CityGrid.Tile(centre.x + sx * Tile, centre.z + sz * Tile);
                    var district = CityGrid.DistrictOf(diagonal.r, diagonal.c);
                    var paved = district == "zentrum" || district == "gewerbe";
                    if (paved)
                    {
                        var inner = RoadHalf + Walk; var outer = inner + PromenadeWidth;
                        var quad = new Vector3(PromenadeWidth, WalkY + 0.1f, PromenadeWidth);
                        ArtLibrary.Box(t, "PromenadeCorner", centre + new Vector3(sx * (inner + outer) * 0.5f, WalkY * 0.5f - 0.05f, sz * (inner + outer) * 0.5f), quad, Pavement(quad), false);
                    }
                    var lift = paved ? Vector3.up * WalkY : Vector3.zero;
                    if (lit.Contains((sx, sz))) continue;
                    var corner = centre + new Vector3(sx * kerbLine, WalkY, sz * kerbLine);
                    var facing = Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg;
                    if (arms.Count == 2 && (roll + sx + sz) % 2 == 0) LampPost(t, corner, facing);
                    else Scenery.Place(t, "flower_bed", corner, facing + 45f, 0.8f);
                    // a tree diagonally behind the corner unless a street arm leaves that way
                    var outward = new Vector3(sx, 0f, sz);
                    var blocked = false;
                    foreach (var (dr, dc) in arms) { var dir = (W(r + dr, c + dc) - centre).normalized; if (Vector3.Dot(dir, outward.normalized) > 0.6f) blocked = true; }
                    if (!blocked && (roll + sx * 3 + sz) % 3 != 0) Scenery.Place(t, paved ? "tree_small" : "tree_big", centre + outward * verge + lift, roll * 0.9f, paved ? 1.1f : 0.95f);
                    if (!blocked && paved) Scenery.Place(t, "street_planter", centre + outward * (verge + 1.4f) + lift, roll * 0.7f);
                }
        }

        /// <summary>(street tile r, c, pavement side ±1 along the tile's arm axis) in front of a place's door — no trees there.</summary>
        private readonly HashSet<(int, int, int)> _placeSides = new HashSet<(int, int, int)>();

        private void MarkPlaceSides()
        {
            foreach (var p in DriveModel.Places)
            {
                var (r, c) = (p.StreetTile[0], p.StreetTile[1]);
                var arms = CityGrid.StreetNeighbours(r, c);
                if (arms.Count == 0) continue;
                var centre = W(r, c);
                var dir = (W(r + arms[0].dr, c + arms[0].dc) - centre).normalized;
                var side = new Vector3(dir.z, 0f, -dir.x);
                // the place lies against the door→street direction
                var s = Vector3.Dot(-new Vector3((float)p.FaceX, 0f, (float)p.FaceZ), side) >= 0f ? 1 : -1;
                _placeSides.Add((r, c, s));
                // welcome sign: right-hand verge of the home street and the tile north of it (no avenue tree in front)
                if (p.Id == "home")
                {
                    _placeSides.Add((r, c, -s));
                    _placeSides.Add((r - 1, c, -s));
                }
            }
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ── places, home, landmarks ─────────────────────────────────────────

        /// <summary>
        /// A place on its plan tile: the front model set forward towards the street, the lettered sign, chalkboard and
        /// produce crate at the door of the shops, the square/dealer/park/airport with their own dressing.
        /// </summary>
        /// <summary>
        /// The road out to the ranch (ranch_exit.gd): a white ranch gate across the end of row 7 with a horseshoe on its
        /// beam, the wooden sign "Zur Gooby-Ranch" on the verge, hay bales and a paddock rail on the block beside it.
        /// </summary>
        private void BuildRanchExit(Transform t, DriveModel.Place place)
        {
            var road = W(place.StreetTile[0], place.StreetTile[1]);
            var block = W(place.Tile[0], place.Tile[1]);
            var onward = (road - W(place.StreetTile[0], place.StreetTile[1] - 1)).normalized;
            var across = new Vector3(onward.z, 0f, -onward.x);
            var white = ArtLibrary.Solid(ArtLibrary.Hex("#FFF8EE"), 0.3f);
            var wood = ArtLibrary.Solid(ArtLibrary.Hex("#B58A5F"), 0.2f);
            var gateAt = road + onward * (Tile * 0.5f - 1.2f);
            foreach (var s in new[] { -1f, 1f })
                ArtLibrary.Box(t, "RanchGatePost", gateAt + across * s * (RoadHalf + 0.6f) + Vector3.up * 2.1f, new Vector3(0.45f, 4.2f, 0.45f), white);
            var beam = ArtLibrary.Box(t, "RanchGateBeam", gateAt + Vector3.up * 4.1f, Abs(across * (RoadHalf * 2f + 2.2f)) + new Vector3(0.35f, 0.5f, 0.35f), white);
            beam.name = "RanchGateBeam";
            var board = ArtLibrary.Box(t, "RanchGateBoard", gateAt + Vector3.up * 4.9f - onward * 0.05f, Abs(across * 3.4f) + new Vector3(0.12f, 1.1f, 0.12f), ArtLibrary.Solid(ArtLibrary.Hex("#F2C14E"), 0.3f));
            board.name = "RanchGateBoard";
            var shoe = Resources.Load<Texture2D>("gooby-ui/Icons/icon_horseshoe");
            if (shoe != null)
                ArtLibrary.Quad(t, "RanchGateShoe", gateAt + Vector3.up * 4.9f - onward * 0.13f, Quaternion.LookRotation(onward), new Vector2(1.0f, 1.0f), ArtLibrary.Cutout(shoe, "ranch_shoe", Color.white));
            // the wooden road sign on the verge, facing the cars coming east
            var signAt = road - onward * 3.5f + across * (RoadHalf + 2.4f);
            ArtLibrary.Box(t, "RanchSignPost", signAt + Vector3.up * 1.3f, new Vector3(0.22f, 2.6f, 0.22f), wood);
            var sign = ArtLibrary.Box(t, "RanchSign", signAt + Vector3.up * 2.75f, Abs(across * 2.8f) + new Vector3(0.14f, 1.0f, 0.14f), ArtLibrary.Solid(ArtLibrary.Hex("#F2E9DC"), 0.2f));
            sign.name = "RanchSign";
            if (shoe != null)
                ArtLibrary.Quad(t, "RanchSignShoe", signAt + Vector3.up * 2.75f - onward * 0.09f, Quaternion.LookRotation(onward), new Vector2(0.8f, 0.8f), ArtLibrary.Cutout(shoe, "ranch_shoe", Color.white));
            // the ranch's verge: a paddock rail, hay bales and a meadow on the block
            var hay = ArtLibrary.Solid(ArtLibrary.Hex("#E9C35A"), 0.1f);
            ArtLibrary.Box(t, "RanchMeadow", block + Vector3.up * 0.03f, new Vector3(Tile - 1f, 0.06f, Tile - 1f), ArtLibrary.Solid(ArtLibrary.Hex("#9CCB7A"), 0.05f), false);
            var toRoad = (road - block).normalized;
            var railAt = block + toRoad * (Tile * 0.5f - 2.2f);
            var railAcross = new Vector3(toRoad.z, 0f, -toRoad.x);
            for (var i = -3; i <= 3; i++) ArtLibrary.Box(t, "RanchRailPost", railAt + railAcross * (i * 2.6f) + Vector3.up * 0.6f, new Vector3(0.18f, 1.2f, 0.18f), white);
            foreach (var y in new[] { 0.55f, 1.0f })
                ArtLibrary.Box(t, "RanchRail", railAt + Vector3.up * y, Abs(railAcross * 15.8f) + new Vector3(0.1f, 0.12f, 0.1f), white);
            foreach (var (a, b, h) in new[] { (-3.5f, -1.5f, 0f), (-2.2f, -1.2f, 0f), (-2.9f, -1.4f, 0.9f), (3.8f, -4.5f, 0f) })
                ArtLibrary.Box(t, "RanchHay", block + railAcross * a + toRoad * (b + Tile * 0.2f) + Vector3.up * (0.45f + h), new Vector3(1.6f, 0.9f, 1.0f), hay);
            Scenery.Place(t, "tree_round_big", block - toRoad * 5.5f + railAcross * 5f, 30f, 1.2f);
            Scenery.Place(t, "tree_round_big", block - toRoad * 6.5f - railAcross * 4.5f, 120f, 1.0f);
        }

        private void BuildPlace(Transform t, DriveModel.Place place)
        {
            if (!PlaceFronts.TryGetValue(place.Id, out var front)) return;
            var centre = W(place.Tile[0], place.Tile[1]);
            var face = new Vector3((float)place.FaceX, 0f, (float)place.FaceZ);
            var side = new Vector3(face.z, 0f, -face.x);   // to the right when looking at the street
            var yaw = (float)place.Yaw;
            GameObject anchor;
            // the house fronts stand in line with the terraces of the blocks: facade FrontDepth from the tile centre.
            // MART stays on its tile so the coral shop with "GOOBY MART" is the left facade at the MART pill
            // (a south shift put the cream cafe in that shot). THEKE on tile 6 remains the first shop south.
            var along = Vector3.zero;
            var frontAt = centre + face * (FrontDepth - 2.8f) + along;
            var flanked = false;
            switch (place.Id)
            {
                case "wochenmarkt":
                    // an open square: three stalls under striped awnings, Greta's board at the kerb, crates and trees behind
                    Scenery.Place(t, "market_stall_red", centre + face * 2.5f - side * 4.2f, yaw);
                    Scenery.Place(t, "market_stall_green", centre + face * 2.2f, yaw);
                    Scenery.Place(t, "market_stall_yellow", centre + face * 2.5f + side * 4.2f, yaw);
                    Scenery.Place(t, "garden_crate", centre + face * 5.2f + side * 6.2f, yaw + 20f, 0.8f);
                    Scenery.Place(t, "garden_crate", centre + face * 5.6f - side * 6.4f, yaw - 15f, 0.8f);
                    Scenery.Place(t, "tree_small", centre - face * 4.5f - side * 5f, 40f, 1.1f);
                    Scenery.Place(t, "tree_small", centre - face * 4.5f + side * 5f, 120f, 1.0f);
                    Scenery.Place(t, "hedge_long", centre - face * 7.5f, yaw, 1.0f);
                    Scenery.Place(t, "tree_poplar", centre - face * 7.5f - side * 7f, 10f, 0.9f);
                    Scenery.Place(t, "tree_poplar", centre - face * 7.5f + side * 7f, 80f, 1.0f);
                    anchor = Scenery.Place(t, front.model, centre + face * 6.0f - side * 1.2f, yaw);
                    Gravel(t, centre + face * 1.5f, new Vector2(16f, 12f), yaw);
                    break;
                case "funkelpark":
                    // the gate at the pavement, the ferris wheel turning behind it, hedges along the fence line
                    anchor = Scenery.Place(t, front.model, centre + face * 5.0f, yaw);
                    Scenery.Place(t, "ferris_wheel", centre - face * 8f + side * 2f, yaw, 1.05f);
                    Scenery.Place(t, "hedge_long", centre + face * 5.4f - side * 6.4f, yaw, 1.0f);
                    Scenery.Place(t, "hedge_long", centre + face * 5.4f + side * 6.4f, yaw, 1.0f);
                    Scenery.Place(t, "tree_big", centre - face * 3f - side * 7f, 30f, 0.9f);
                    break;
                case "flughafen":
                    anchor = Scenery.Place(t, front.model, frontAt, yaw);
                    Scenery.Place(t, "plane_small", centre - face * 6f - side * 8f, yaw + 60f);
                    Gravel(t, centre - face * 4f - side * 9f, new Vector2(22f, 14f), yaw, ArtLibrary.Hex("#B8B4BC"));
                    break;
                case "autohaus":
                    anchor = Scenery.Place(t, front.model, frontAt, yaw);
                    ShowroomCar(anchor.transform);
                    Gravel(t, centre + face * (FrontDepth + 1.8f), new Vector2(14f, 3.2f), yaw, ArtLibrary.Hex("#B8B4BC"));
                    break;
                case "baumarkt":
                    anchor = Scenery.Place(t, front.model, frontAt, yaw);
                    Scenery.Place(t, "garden_crate", centre + face * (FrontDepth + 1.4f) + side * 5.5f, yaw + 30f, 0.9f);
                    break;
                case "mart":
                    // board 03: MART is the first coral shop of the northbound row. GOOBYTHEKE (green) fills the
                    // tile south of it; a cream cafe with outdoor seating stands north (the board's third house).
                    anchor = Scenery.Place(t, front.model, frontAt, yaw);
                    Scenery.Place(t, "house_cream", frontAt - side * 6.8f, yaw);
                    flanked = true;
                    break;
                default:
                    anchor = Scenery.Place(t, front.model, frontAt, yaw);
                    // neighbours shoulder to shoulder on both sides: the shop is one house of a closed street front
                    FlankHouses(t, centre, face, side, yaw);
                    flanked = true;
                    break;
            }
            Lettering(anchor, SignOf(place.Id), front.sign, front.size);
            // in the centre and the trade district the paving of the promenade runs up to the facade: dressing stands on it
            var district = CityGrid.DistrictOf(place.Tile[0], place.Tile[1]);
            var lift = (district == "zentrum" || district == "gewerbe") ? Vector3.up * WalkY : Vector3.zero;
            var door = centre + face * (FrontDepth + 0.9f) + lift + along;   // the doorstep on the promenade in front of the house
            if (place.Typ == "laden" && place.Id != "autohaus")
            {
                // board 03: chalkboard on one side of the steps, a produce crate on the other
                Scenery.Place(t, "sandwich_board", door - side * 1.6f, yaw + 12f, place.Id == "mart" ? 1.22f : 1.08f);
                Scenery.Place(t, "shop_crate", door + side * 1.5f, yaw - 8f);
                if (place.Id == "mart")
                {
                    Scenery.Place(t, "shop_crate", door + side * 1.5f + Vector3.up * 0.36f, yaw + 20f, 0.9f);
                    Scenery.Place(t, "shop_crate", door - side * 2.35f, yaw + 18f, 0.88f);
                    Scenery.Place(t, "shop_crate", door - side * 2.55f + face * 0.4f, yaw - 22f, 0.78f);
                    // cream cafe north of MART (board 03 outdoor seating under the orange awning)
                    Scenery.Place(t, "cafe_set", door + face * 0.9f - side * 6.4f, yaw - 10f);
                    Scenery.Place(t, "cafe_set", door + face * 1.1f - side * 8.6f, yaw + 32f, 0.92f);
                }
                if (place.Id == "pow")
                {
                    Scenery.Place(t, "cafe_set", door + face * 0.85f - side * 2.15f, yaw - 12f);
                    Scenery.Place(t, "cafe_set", door + face * 1.05f + side * 2.35f, yaw + 28f, 0.92f);
                }
            }
            if (place.Id != "wochenmarkt" && place.Id != "flughafen")
            {
                Scenery.Place(t, "bush_big", door - side * 4.6f, (float)_rng.Next(360), 0.75f);
                if (lift == Vector3.zero) ArtLibrary.Plant(t, place.Tile[0] % 2 == 0 ? "plant_tulips_red" : "plant_daisies", door + side * 4.2f, 0.5f, place.Tile[1] * 19f);
                else Scenery.Place(t, "street_planter", door + side * 4.2f, place.Tile[1] * 19f);
                if (place.Id != "funkelpark" && !flanked) Scenery.Place(t, "tree_big", centre - side * 7.6f + face * 1f, (float)_rng.Next(360), 0.85f);
            }
            if (flanked)
            {
                _rows.Clear();
                _rows.Add((centre, face, side, 9.9f, 3.3f, 9.1f));
                BackYard(t, centre, face, side, place.Tile[0] * 7 + place.Tile[1]);
            }
        }

        /// <summary>Facade line of the house rows: this far from the block tile's centre towards its street (1 m inside the tile edge).</summary>
        private const float FrontDepth = 9.0f;

        /// <summary>A plain town house left and right of a place's front (widths 5.6–6.4 m fill the 20 m tile edge with it).</summary>
        private void FlankHouses(Transform t, Vector3 centre, Vector3 face, Vector3 side, float yaw)
        {
            var at = centre + face * (FrontDepth - 2.8f);
            var a = _rng.Next(TownHouses.Length);
            var b = (a + 1 + _rng.Next(TownHouses.Length - 1)) % TownHouses.Length;
            Scenery.Place(t, TownHouses[a], at - side * 6.7f, yaw);
            Scenery.Place(t, TownHouses[b], at + side * 6.7f, yaw);
            var door = centre + face * (FrontDepth + 0.9f) + Vector3.up * WalkY;
            Scenery.Place(t, "street_planter", door - side * 7.4f, yaw * 0.3f);
            Scenery.Place(t, "street_planter", door + side * 7.6f, yaw + 40f);
        }

        /// <summary>Home: the cottage of the garden on its plan tile, picket fence with a gap for the gravel driveway to the street.</summary>
        private void BuildHome(Transform t, DriveModel.Place home)
        {
            if (home == null) return;
            var centre = W(home.Tile[0], home.Tile[1]);
            var face = new Vector3((float)home.FaceX, 0f, (float)home.FaceZ);
            var side = new Vector3(face.z, 0f, -face.x);
            var yaw = (float)home.Yaw;
            Scenery.Place(t, "cottage", centre - face * 1.5f - side * 1.5f, yaw);
            Gravel(t, centre + face * 5.5f + side * 3.6f, new Vector2(3.6f, 10f), yaw);
            for (var k = -4; k <= 4; k++)
            {
                if (k == 2 || k == 3) continue;   // the driveway gap
                Scenery.Place(t, "picket_fence", centre + face * 9.6f + side * (k * 2f), yaw);
                if (k % 2 == 0) ArtLibrary.Plant(t, k % 4 == 0 ? "plant_flowers_pink" : "plant_tulips_white", centre + face * 9.0f + side * (k * 2f + 0.6f), 0.42f, k * 41f);
            }
            Scenery.Place(t, "mailbox_red", centre + face * 9.0f + side * 1.6f, yaw);
            Scenery.Place(t, "tree_big", centre - face * 5f + side * 6.5f, 25f, 1.0f);
            Scenery.Place(t, "bush_flowers", centre + face * 4f - side * 6f, 80f, 0.9f);
            Scenery.Place(t, "light_post", centre + face * 4.5f + side * 6.5f, yaw, 0.9f);
        }

        /// <summary>
        /// The GOOBEA store of the plan (deko "ikea"): the big blue hall with the yellow band (Blender store_goobea)
        /// over four tiles of the trade district, its entrance and lettering towards the street north of it, planters
        /// and a concrete forecourt in front, poplars at the corners.
        /// </summary>
        private void BuildStore(Transform t, CityGrid.Landmark l)
        {
            var centre = W(l.R, l.C);
            var anchor = Scenery.Place(t, "store_goobea", centre, 0f);
            Lettering(anchor, "sign_goobea", new Vector3(0f, 7.7f, 12.26f), new Vector2(9f, 1.2f));
            foreach (var k in new[] { -1f, 1f })
            {
                _taken.Add(CityGrid.Tile(centre.x + k * 10f, centre.z + 10f));
                _taken.Add(CityGrid.Tile(centre.x + k * 10f, centre.z - 10f));
            }
            for (var i = 0; i < 6; i++) Scenery.Place(t, "street_planter", centre + new Vector3(-12.5f + i * 5f, 0f, 13.4f), i * 40f);
            Gravel(t, centre + new Vector3(0f, 0f, 16.5f), new Vector2(30f, 6f), 0f, ArtLibrary.Hex("#B8B4BC"));
            foreach (var k in new[] { -1f, 1f })
            {
                Scenery.Place(t, "tree_poplar", centre + new Vector3(k * 16.5f, 0f, 13.5f), k * 30f, 1.0f);
                Scenery.Place(t, "tree_poplar", centre + new Vector3(k * 16.5f, 0f, -13.5f), k * 70f, 0.95f);
                Scenery.Place(t, "hedge_long", centre + new Vector3(k * 16.3f, 0f, 0f), 90f, 1.0f);
            }
        }

        /// <summary>The GOOBERANDO kitchen of the plan: a salmon town house with a delivery scooter's crate at the door.</summary>
        private void BuildKitchen(Transform t)
        {
            var centre = W(CityGrid.KitchenTile[0], CityGrid.KitchenTile[1]);
            var street = W(CityGrid.KitchenStreet[0], CityGrid.KitchenStreet[1]);
            var yaw = YawTo(centre, street);
            var face = (street - centre).normalized;
            var anchor = Scenery.Place(t, "house_salmon", centre + face * 3.6f, yaw);
            Lettering(anchor, "sign_gooberando", new Vector3(0f, 3.62f, 3.06f), new Vector2(2.6f, 0.6f));
            Scenery.Place(t, "shop_crate", centre + face * 7.2f + new Vector3(face.z, 0f, -face.x) * 1.6f, yaw + 15f, 0.9f);
            Scenery.Place(t, "street_planter", centre + face * 7.0f - new Vector3(face.z, 0f, -face.x) * 1.8f, yaw);
        }

        /// <summary>
        /// A block tile without a place: what stands there follows the district — town houses in the centre and the
        /// trade district, cottages behind picket fences in the residential district, the park with trees, hedges and
        /// the church tower, the fairground meadow, the airport apron, woodland at the town's edge.
        /// </summary>
        private void BuildBlock(Transform t, int r, int c)
        {
            var centre = W(r, c);
            var district = CityGrid.DistrictOf(r, c);
            var streets = CityGrid.StreetNeighbours(r, c);
            // the town's edge along the outer streets: cottages behind picket fences (board 03's right-hand side)
            if (district == null && streets.Count > 0) district = "wohnen";
            var streetTile = CityGrid.StreetNextTo(r, c);
            var face = streetTile.HasValue ? (W(streetTile.Value.r, streetTile.Value.c) - centre).normalized : Vector3.forward;
            var side = new Vector3(face.z, 0f, -face.x);
            var yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            var roll = _rng.Next(1000);
            switch (district)
            {
                case "zentrum":
                case "gewerbe":
                    BuildTerraceBlock(t, r, c, centre, streets, roll);
                    break;
                case "wohnen":
                    BuildCottageBlock(t, r, c, centre, streets, face, side, yaw, roll);
                    break;
                case "park":
                    BuildParkTile(t, r, c, centre, roll);
                    break;
                case "funkelpark":
                    Scenery.Place(t, "hedge", centre + face * 8.6f - side * 4f, yaw, 1.0f);
                    Scenery.Place(t, "hedge", centre + face * 8.6f + side * 4f, yaw, 1.0f);
                    Scenery.Place(t, "tree_big", centre - face * 2f + side * 5f, roll * 0.41f, 0.9f);
                    Scenery.Place(t, "tree_small", centre - face * 5f - side * 5f, roll * 0.29f, 1.1f);
                    ArtLibrary.Plant(t, "plant_flowers_pink", centre + face * 6.5f, 0.6f, roll * 0.1f);
                    break;
                case "flughafen":
                    Gravel(t, centre, new Vector2(Tile, 14f), 0f, ArtLibrary.Hex("#B8B4BC"));
                    if (roll % 2 == 0) Scenery.Place(t, "light_post", centre + new Vector3(0f, 0f, 8f), 0f, 1.1f);
                    break;
                default:
                    // the town's edge: woodland and meadow flowers
                    Scenery.Place(t, "tree_big", centre + new Vector3(-4f + roll % 5, 0f, 3f - roll % 7), roll * 0.61f, 1.0f + (roll % 4) * 0.12f);
                    if (roll % 2 == 0) Scenery.Place(t, "tree_small", centre + new Vector3(5f - roll % 9, 0f, -5f + roll % 6), roll * 0.31f, 1.2f);
                    if (roll % 3 == 0) Scenery.Place(t, "bush_big", centre + new Vector3(2f, 0f, 6f), roll * 0.13f, 0.9f);
                    ArtLibrary.Plant(t, roll % 2 == 0 ? "plant_daisies" : "plant_flowers_pink", centre + new Vector3(-6f + roll % 12, 0f, 6f - roll % 11), 0.5f, roll * 0.7f);
                    break;
            }
        }

        private static readonly string[] Terraces = { "terrace_a", "terrace_b", "terrace_c", "terrace_d" };
        private static readonly string[] ShortTerraces = { "terrace_short_a", "terrace_short_b" };

        /// <summary>Footprints of the house rows placed so far on the current block (centre, facing, half width, depth range) — back-yard dressing keeps out.</summary>
        private readonly List<(Vector3 centre, Vector3 face, Vector3 side, float halfW, float from, float to)> _rows = new List<(Vector3, Vector3, Vector3, float, float, float)>();

        private bool Free(Vector3 p, float margin = 0.8f)
        {
            foreach (var row in _rows)
            {
                var d = p - row.centre;
                var along = Vector3.Dot(d, row.side);
                var depth = Vector3.Dot(d, row.face);
                if (Mathf.Abs(along) < row.halfW + margin && depth > row.from - margin && depth < row.to + margin) return false;
            }
            return true;
        }

        /// <summary>
        /// Board 03's street front: a closed row of three houses (one Blender mesh) along every street side of the
        /// block, the facade a metre inside the tile edge; on a corner block the second side gets the two-house row,
        /// shifted off the corner the first row already fills. Behind the rows: back yards with poplars, trees, hedges.
        /// </summary>
        private void BuildTerraceBlock(Transform t, int r, int c, Vector3 centre, List<(int dr, int dc)> streets, int roll)
        {
            _rows.Clear();
            Vector3 face1 = Vector3.forward, side1 = Vector3.right;
            for (var i = 0; i < streets.Count && i < 2; i++)
            {
                var (dr, dc) = streets[i];
                var face = (W(r + dr, c + dc) - centre).normalized;
                var side = new Vector3(face.z, 0f, -face.x);
                var yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
                var at = centre + face * (FrontDepth - 2.8f);
                if (i == 0)
                {
                    face1 = face; side1 = side;
                    Scenery.Place(t, Terraces[(roll + r * 3 + c) % Terraces.Length], at, yaw);
                    _rows.Add((centre, face, side, 9.4f, 3.3f, 9.1f));
                }
                else
                {
                    // the first row occupies |along side| ∈ [3.3, 9.1] on the side where face1 points: shift the short row away
                    var k = Vector3.Dot(face1, side) > 0f ? 1f : -1f;
                    var shift = -k * 3.0f;
                    Scenery.Place(t, ShortTerraces[(roll + r + c) % ShortTerraces.Length], at + side * shift, yaw);
                    _rows.Add((centre + side * shift, face, side, 6.3f, 3.3f, 9.1f));
                }
                // planters, a flowering bush and now and then a chalkboard or a bench on the promenade at the facade
                var verge = centre + face * (FrontDepth + 0.9f) + Vector3.up * WalkY;
                Scenery.Place(t, "street_planter", verge + side * (roll % 5 - 2) * 1.5f, roll * 1.3f);
                Scenery.Place(t, "street_planter", verge - side * (4.5f + roll % 3), roll * 0.19f);
                Scenery.Place(t, "bush_flowers", verge + side * (6.5f + roll % 2), roll * 0.7f, 0.6f);
                if ((roll + i) % 3 == 0) Scenery.Place(t, "sandwich_board", verge - side * 7.8f, yaw + 15f, 1.08f);
                else if ((roll + i) % 3 == 1) Scenery.Place(t, "park_bench", verge + face * 0.2f - side * 7.6f, yaw);
                else Scenery.Place(t, "cafe_set", verge + side * (roll % 2 == 0 ? 2.2f : -2.4f) + face * 0.85f, yaw + (roll % 40 - 20));
            }
            if (streets.Count == 0) Scenery.Place(t, Terraces[roll % Terraces.Length], centre + face1 * (FrontDepth - 2.8f), 0f);
            BackYard(t, centre, face1, side1, roll);
        }

        /// <summary>Behind a house row: two poplars rising over the roofs (board 03), a round tree, a hedge run along the back boundary and a shed.</summary>
        private void BackYard(Transform t, Vector3 centre, Vector3 face, Vector3 side, int roll)
        {
            var yardYaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            var spots = new (Vector3 p, string what, float yaw, float scale)[]
            {
                (centre - face * 4.9f + side * 7.2f, "tree_poplar", roll * 0.3f, 0.9f + (roll % 3) * 0.08f),
                (centre - face * 4.9f - side * 7.6f, "tree_poplar", roll * 0.7f, 1.0f + (roll % 2) * 0.1f),
                (centre - face * 1.0f + side * (roll % 2 == 0 ? 2.5f : -2.5f), "tree_big", roll * 0.37f, 0.85f + (roll % 3) * 0.1f),
                (centre - face * 6.5f + side * (roll % 2 == 0 ? -1.5f : 3.5f), "tree_small", roll * 0.91f, 1.1f),
                (centre - face * 8.4f, "hedge_long", yardYaw, 1.0f),
                (centre - face * 5.6f + side * (roll % 2 == 0 ? 6.0f : -6.0f), "garden_shed", yardYaw + 180f + (roll % 30 - 15), 1.0f),
                (centre - face * 3.2f - side * (roll % 2 == 0 ? 5.5f : -5.5f), "bush_big", roll * 0.23f, 0.8f),
            };
            foreach (var s in spots) if (Free(s.p)) Scenery.Place(t, s.what, s.p, s.yaw, s.scale);
        }

        /// <summary>
        /// Board 03's right-hand side: a cottage behind a white picket fence at the pavement, hedge runs behind the
        /// fence, a flower bed and stepping stones to the door, a shed and trees in the back garden, a poplar at the
        /// boundary; on a corner block the fence turns the corner along the second street.
        /// </summary>
        private void BuildCottageBlock(Transform t, int r, int c, Vector3 centre, List<(int dr, int dc)> streets, Vector3 face, Vector3 side, float yaw, int roll)
        {
            _rows.Clear();
            Scenery.Place(t, Cottages[roll % Cottages.Length], centre + face * 2.6f + side * 0.5f, yaw);
            _rows.Add((centre, face, side, 4.8f, -0.4f, 5.6f));
            for (var k = -4; k <= 4; k++)
            {
                if (k == 0) continue;   // the gate gap in line with the front door
                Scenery.Place(t, "picket_fence", centre + face * 9.3f + side * (k * 2f), yaw);
                if (k % 2 != 0) ArtLibrary.Plant(t, (roll + k) % 3 == 0 ? "plant_flowers_pink" : (roll + k) % 3 == 1 ? "plant_tulips_white" : "plant_daisies", centre + face * 8.7f + side * (k * 2f + 0.6f), 0.42f, k * 41f);
            }
            Scenery.Place(t, "hedge_long", centre + face * 8.4f - side * 5.8f, yaw, 0.9f);
            if (roll % 2 == 0) Scenery.Place(t, "hedge_long", centre + face * 8.4f + side * 5.8f, yaw, 0.9f);
            else Scenery.Place(t, "flower_bed", centre + face * 7.6f + side * 4.5f, yaw, 1.0f);
            for (var k = 0; k < 3; k++) Scenery.Place(t, "stepping_stone", centre + face * (6.2f + k * 1.0f), k * 60f);
            Scenery.Place(t, "tree_big", centre - face * 4.5f - side * 6.5f, roll * 0.53f, 0.9f + (roll % 2) * 0.15f);
            Scenery.Place(t, "tree_poplar", centre - face * 7.5f + side * 6.8f, roll * 0.61f, 0.95f + (roll % 3) * 0.08f);
            Scenery.Place(t, "bush_big", centre + face * 6.5f + side * 7.0f, roll * 0.17f, 0.85f);
            Scenery.Place(t, "bush_flowers", centre + face * 6.8f - side * 3.2f, roll * 0.41f, 0.7f);
            Scenery.Place(t, "garden_shed", centre - face * 6.0f - side * (roll % 2 == 0 ? 4.5f : -6.5f), yaw + 180f + (roll % 40 - 20));
            if (roll % 3 == 0) Scenery.Place(t, "mailbox_red", centre + face * 9.1f + side * 1.2f, yaw);
            if (roll % 4 == 1) Scenery.Place(t, "log_fence", centre - face * 8.8f + side * 2f, yaw);
            // the second street side of a corner block: fence and hedge along it too
            if (streets.Count >= 2)
            {
                var (dr, dc) = streets[1];
                var face2 = (W(r + dr, c + dc) - centre).normalized;
                var side2 = new Vector3(face2.z, 0f, -face2.x);
                var yaw2 = Mathf.Atan2(face2.x, face2.z) * Mathf.Rad2Deg;
                for (var k = -4; k <= 4; k++) Scenery.Place(t, "picket_fence", centre + face2 * 9.3f + side2 * (k * 2f), yaw2);
                Scenery.Place(t, "hedge_long", centre + face2 * 8.4f + side2 * 3f, yaw2, 0.9f);
                Scenery.Place(t, "tree_small", centre + face2 * 6.5f - side2 * 4f, roll * 0.29f, 1.1f);
            }
        }

        /// <summary>
        /// The tower closing the vista of the home street (board 03: the clock tower at the end of the road): the
        /// first block tile straight beyond the last street tile north of home, a gravel square in front of it.
        /// </summary>
        private void BuildVistaTower(Transform t)
        {
            var home = DriveModel.PlaceById("home");
            if (home == null) return;
            var (r, c) = (home.StreetTile[0], home.StreetTile[1]);
            var heading = DriveModel.HomeStart().heading;
            // tile step of the start heading: forward (Δx, Δz) = (sin h, cos h) and World() maps Δc = −Δx, Δr = −Δz
            var (dr, dc) = ((int)System.Math.Round(-System.Math.Cos(heading)), (int)System.Math.Round(-System.Math.Sin(heading)));
            var guard = 0;
            while (CityGrid.IsStreet(r + dr, c + dc) && guard++ < 32) { r += dr; c += dc; }
            var (vr, vc) = (r + dr, c + dc);
            if (vr < 0 || vc < 0 || vr >= CityGrid.Rows || vc >= CityGrid.Cols || CityGrid.IsStreet(vr, vc) || _taken.Contains((vr, vc))) return;
            _taken.Add((vr, vc));
            var centre = W(vr, vc);
            var face = (W(r, c) - centre).normalized;
            var side = new Vector3(face.z, 0f, -face.x);
            var yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            Scenery.Place(t, "church_tower", centre - face * 1.5f, yaw, 1.15f);
            Gravel(t, centre + face * 5.5f, new Vector2(14f, 9f), yaw);
            for (var k = -2; k <= 2; k++) if (k != 0) ArtLibrary.Plant(t, k % 2 == 0 ? "plant_tulips_red" : "plant_flowers_pink", centre + face * 8.6f + side * (k * 2.8f), 0.5f, k * 50f);
            Scenery.Place(t, "flower_bed", centre + face * 8.8f, yaw, 1.1f);
            Scenery.Place(t, "park_bench", centre + face * 4.5f - side * 5.5f, yaw + 90f);
            Scenery.Place(t, "park_bench", centre + face * 4.5f + side * 5.5f, yaw - 90f);
            Scenery.Place(t, "tree_poplar", centre - face * 4f - side * 6f, 20f, 1.0f);
            Scenery.Place(t, "tree_poplar", centre - face * 4f + side * 6f, 75f, 1.05f);
            Scenery.Place(t, "tree_big", centre + face * 1f - side * 8f, 40f, 0.9f);
            Scenery.Place(t, "tree_big", centre + face * 1f + side * 8f, 130f, 0.95f);
            Scenery.Place(t, "hedge_long", centre - face * 7f, yaw, 1.0f);
        }

        /// <summary>The park: lawn paths, trees, benches (crates), flower beds — and on its central tile the church tower of board 03.</summary>
        private void BuildParkTile(Transform t, int r, int c, Vector3 centre, int roll)
        {
            var zone = FindZone("park");
            var midR = zone != null ? (zone[0] + zone[2]) * 0.5f : r;
            var midC = zone != null ? (zone[1] + zone[3]) * 0.5f : c;
            if (Mathf.Abs(r - midR) <= 0.5f && Mathf.Abs(c - midC) <= 0.5f && !_churchPlaced)
            {
                _churchPlaced = true;
                Scenery.Place(t, "church_tower", centre, 180f, 1.15f);
                Gravel(t, centre + new Vector3(0f, 0f, 8f), new Vector2(6f, 16f), 0f);
                for (var i = 0; i < 4; i++) ArtLibrary.Plant(t, i % 2 == 0 ? "plant_tulips_red" : "plant_flowers_pink", centre + new Vector3(-4f + i * 2.6f, 0f, 6.5f), 0.5f, i * 50f);
                return;
            }
            Scenery.Place(t, "tree_big", centre + new Vector3(-5f, 0f, -4f), roll * 0.37f, 1.1f);
            Scenery.Place(t, "tree_big", centre + new Vector3(5.5f, 0f, 4.5f), roll * 0.71f, 0.95f);
            Scenery.Place(t, "tree_small", centre + new Vector3(4f, 0f, -6f), roll * 0.11f, 1.2f);
            Scenery.Place(t, "hedge", centre + new Vector3(-6f, 0f, 6f), 90f, 1.0f);
            Scenery.Place(t, "garden_crate", centre + new Vector3(0f, 0f, 2f), 90f, 0.8f);
            Gravel(t, centre, new Vector2(2.4f, Tile), 0f);
            ArtLibrary.Plant(t, "plant_daisies", centre + new Vector3(-2.5f, 0f, -1f), 0.55f, 30f);
            ArtLibrary.Plant(t, "plant_flowers_pink", centre + new Vector3(2.6f, 0f, 0.5f), 0.55f, 80f);
        }

        private bool _churchPlaced;

        private static int[] FindZone(string district)
        {
            foreach (var d in CityGrid.Districts) if (d.Id == district && d.Zonen.Count > 0) return d.Zonen[0];
            return null;
        }

        /// <summary>A flat gravel / concrete pad (driveways, forecourts, the market square, the apron).</summary>
        private void Gravel(Transform t, Vector3 centre, Vector2 size, float yaw, Color? tint = null)
        {
            var mat = ArtLibrary.PbrSlab("concrete", size, 2.2f, tint ?? ArtLibrary.Hex("#DDCFB0"), 0.08f, 0.6f);
            var pad = ArtLibrary.Box(t, "Pad", centre + new Vector3(0f, 0.01f, 0f), new Vector3(size.x, 0.04f, size.y), mat, false);
            pad.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>The dealer's showroom piece: the hero car in sky blue on the podium behind the glass front.</summary>
        private static void ShowroomCar(Transform showroom)
        {
            var car = new GameObject("ShowroomCar").transform;
            car.SetParent(showroom, false);
            car.localPosition = new Vector3(0f, 0.2f, -0.6f);
            car.localRotation = Quaternion.Euler(0f, 90f, 0f);
            car.localScale = Vector3.one * 0.8f;
            TintCar(car, ArtLibrary.Hex("#4E79D6"));
            _ = AssetLoader.InstanceAsync(AssetLoader.VehiclePath("car_hero"), car, "car_hero");
        }

        /// <summary>The player's car as bought at the dealer: paint plus a body shape per catalog model.</summary>
        public void DressCar(string modelId, Color body)
        {
            Car.localScale = CarShape(modelId);
            TintCar(Car, body);
        }

        /// <summary>Body proportions of a catalog model on the hero car (street and garage alike).</summary>
        public static Vector3 CarShape(string modelId)
        {
            switch (modelId)
            {
                case "hatchback-sports": return new Vector3(1.02f, 0.88f, 1.06f);
                case "suv": return new Vector3(1.06f, 1.16f, 1.02f);
                case "van": return new Vector3(1.04f, 1.22f, 1.14f);
                default: return Vector3.one;
            }
        }

        /// <summary>Body paint of a hero car instance (Autohaus): the two "lack" materials of the Blender model.</summary>
        public static void TintCar(Transform car, Color body)
        {
            if (!car.TryGetComponent<CarPaint>(out var marker)) marker = car.gameObject.AddComponent<CarPaint>();
            marker.Body = body;
            marker.Apply();
        }

        /// <summary>Street lamp (1.08 scale → lantern head at 4.75 m); the head position is kept for the night lights.</summary>
        private void LampPost(Transform t, Vector3 at, float yaw)
        {
            // the banners alternate between board 03's orange and the pink of the brand
            const float lampScale = 1.08f;
            Scenery.Place(t, (_lampHeads.Count % 3 == 2) ? "lamp_post_pink" : "lamp_post", at, yaw, lampScale);
            _lampHeads.Add(at + Vector3.up * (4.4f * lampScale));
        }

        /// <summary>
        /// Night: every lantern gets a warm point light and a glowing head, the car switches its headlights on and
        /// the painted horizon dims to a blue dusk. Day undoes all of it. Lights are created on first use.
        /// </summary>
        public void SetNight(bool on)
        {
            if (_night == on && (_nightGlow != null || !on)) return;
            _night = on;
            if (_nightGlow == null && on)
            {
                _nightGlow = new GameObject("NightLights");
                _nightGlow.transform.SetParent(Root.transform, false);
                var glow = ArtLibrary.Glow(new Color(2.6f, 2.1f, 1.2f));
                foreach (var head in _lampHeads) ArtLibrary.Sphere(_nightGlow.transform, "LampGlow", head, 0.22f, glow, false);
                // every lantern glows, but only the pool nearest the car carries a real point light (URP's visible-light budget)
                for (var i = 0; i < Mathf.Min(LampLightPool, _lampHeads.Count); i++)
                {
                    var l = new GameObject("LampLight").AddComponent<Light>();
                    l.transform.SetParent(_nightGlow.transform, false);
                    l.type = LightType.Point;
                    l.color = ArtLibrary.Hex("FFD9A0");
                    l.intensity = 2.6f;
                    l.range = 11f;
                    l.shadows = LightShadows.None;
                    _lampLights.Add(l);
                }
                _lampTimer = 0f;
                PlaceLampLights(Car != null ? Car.position : Vector3.zero);
                foreach (var side in new[] { -1f, 1f })
                {
                    var h = new GameObject("Headlight").AddComponent<Light>();
                    h.transform.SetParent(Car, false);
                    h.transform.localPosition = new Vector3(side * 0.55f, 0.7f, 1.9f);
                    h.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
                    h.type = LightType.Spot;
                    h.spotAngle = 55f;
                    h.color = ArtLibrary.Hex("FFF4D6");
                    h.intensity = 4f;
                    h.range = 22f;
                    h.shadows = LightShadows.None;
                    _headlights.Add(h);
                }
            }
            if (_nightGlow != null) _nightGlow.SetActive(on);
            foreach (var h in _headlights) if (h != null) h.enabled = on;
            VehicleRig.Night = on;   // every car's lamps glow, the ambient ones included
            ApplySkyTint();
            MaterialDresser.SetWindowsLit(on);   // warm windows all over town (the concept's "Nacht – ruhig & entspannt")
        }

        public bool Night => _night;

        private float _overcast;
        private static readonly Color CloudRain = ArtLibrary.Hex("B9C0CC"), CloudRainShade = ArtLibrary.Hex("8F98AA");

        /// <summary>Weather (0 sunny … 1 thunderstorm): the painted horizon and the cumulus turn toward rain-cloud grey.</summary>
        public void SetOvercast(float overcast)
        {
            overcast = Mathf.Clamp01(overcast);
            if (Mathf.Abs(overcast - _overcast) < 0.01f) return;
            _overcast = overcast;
            ApplySkyTint();
        }

        private void ApplySkyTint()
        {
            var horizon = _night ? ArtLibrary.Hex("5A648C") : Color.white;
            var grey = _night ? ArtLibrary.Hex("454B66") : ArtLibrary.Hex("A7AFBD");
            ArtLibrary.TintBackdrop(HorizonTexture, Color.Lerp(horizon, grey, _overcast * 0.85f));
            var top = _night ? CloudNight : CloudDay;
            var under = _night ? CloudNightShade : CloudDayShade;
            if (_cloudMat != null) _cloudMat.color = Color.Lerp(top, _night ? CloudNightShade : CloudRain, _overcast);
            if (_cloudShade != null) _cloudShade.color = Color.Lerp(under, _night ? CloudNightShade * 0.8f : CloudRainShade, _overcast);
        }

        private const int LampLightPool = 20;
        private float _lampTimer;

        /// <summary>Moves the pooled point lights to the lanterns nearest <paramref name="near"/> (twice a second).</summary>
        private void PlaceLampLights(Vector3 near)
        {
            if (_lampLights.Count == 0) return;
            var order = new List<Vector3>(_lampHeads);
            order.Sort((a, b) => (a - near).sqrMagnitude.CompareTo((b - near).sqrMagnitude));
            for (var i = 0; i < _lampLights.Count; i++)
            {
                var l = _lampLights[i];
                if (l == null) continue;
                if (i < order.Count) { l.transform.position = order[i] + Vector3.down * 0.3f; l.enabled = _night; }
                else l.enabled = false;
            }
        }

        private void TickNight(float dt)
        {
            if (!_night || _nightGlow == null) return;
            _lampTimer -= dt;
            if (_lampTimer > 0f) return;
            _lampTimer = 0.5f;
            PlaceLampLights(Car != null ? Car.position : Vector3.zero);
        }

        /// <summary>"Willkommen in Sonnental": wooden board on the right-hand verge of the home street (board 03).</summary>
        private void BuildWelcomeSign(Transform t)
        {
            var home = DriveModel.PlaceById("home");
            if (home == null) return;
            var street = W(home.StreetTile[0], home.StreetTile[1]);
            var forward = new Vector3((float)System.Math.Sin(DriveModel.HomeStart().heading), 0f, (float)System.Math.Cos(DriveModel.HomeStart().heading));
            var right = new Vector3(forward.z, 0f, -forward.x);
            // mid-foreground of the driveway shot: +Z of the model faces the street so Lettering's 180° hits the road
            var at = street + forward * 7.2f + right * (RoadHalf + Walk + 1.55f);
            var yaw = Mathf.Atan2(-right.x, -right.z) * Mathf.Rad2Deg - 28f;
            var anchor = Scenery.Place(t, "welcome_sign", at, yaw, 1.28f);
            Lettering(anchor, "sign_welcome", new Vector3(0f, 1.32f, 0.1f), new Vector2(2.15f, 0.82f));
            ArtLibrary.Plant(t, "plant_flowers_pink", at + right * 1.0f, 0.48f, 20f);
            ArtLibrary.Plant(t, "plant_daisies", at - right * 0.85f + forward * 0.4f, 0.42f, 65f);
        }

        /// <summary>Crisp lettering quad in the model's local space, just in front of its board (local +Z).</summary>
        private static void Lettering(GameObject anchor, string texture, Vector3 localPos, Vector2 size)
        {
            var q = ArtLibrary.Quad(anchor.transform, "Lettering", Vector3.zero, Quaternion.identity, size, ArtLibrary.Decal(texture));
            q.transform.localPosition = localPos;
            q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the shop's +Z (street), not the wall
        }

        private static Transform BuildCar(Transform parent)
        {
            var car = new GameObject("Car");
            car.transform.SetParent(parent, false);
            car.AddComponent<VehicleRig>().Exhaust = true;
            _ = AssetLoader.InstanceAsync(AssetLoader.VehiclePath("car_hero"), car.transform, "car_hero");
            return car.transform;
        }

        /// <summary>Keeps the painted mountains a fixed distance ahead of the camera and turned towards it, whatever way we drive.</summary>
        public void FollowBackdrop(Vector3 camPos, float camYawDeg)
        {
            if (_horizon == null) return;
            var forward = new Vector3(Mathf.Sin(camYawDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(camYawDeg * Mathf.Deg2Rad));
            _horizon.position = new Vector3(camPos.x, HorizonGround, camPos.z) + forward * HorizonDistance;
            _horizon.rotation = Quaternion.Euler(0f, camYawDeg, 0f);
            if (_clouds != null)
            {
                // the cloud ring is centred on the camera and turns slowly about it: a seamless drift, no gap ever opens
                _cloudDrift = (_cloudDrift + CloudDriftDegPerSec * Time.deltaTime) % 360f;
                _clouds.position = new Vector3(camPos.x, 0f, camPos.z);
                _clouds.rotation = Quaternion.Euler(0f, _cloudDrift, 0f);
            }
        }
    }
}
