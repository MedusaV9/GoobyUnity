using System.Collections.Generic;
using System.Threading.Tasks;
using Gooby.Core.City;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Life on the streets of Sonnental (Doc E §1.4, Godot CityScene._baue_verkehr/_baue_fussgaenger): the ambient
    /// cars of the plan's traffic loops (Blender vehicle variants in their own paint, holding at the lights and behind
    /// each other, easing off for the player), the strolling Goobys on the pavements (fur and hats from the cosmetics
    /// catalog, walk / look / wave clips) and the traffic lights at the junctions (green/red per axis, amber blink at
    /// night). All the numbers come from <see cref="Traffic"/>, <see cref="Pedestrians"/> and <see cref="CityRhythm"/>;
    /// this class only moves the meshes.
    /// </summary>
    public sealed class CityLife
    {
        private static readonly string[] Models = { "car_small", "car_taxi", "van_delivery", "car_hero" };
        private static readonly Color[] Paints =
        {
            ArtLibrary.Hex("#4E79D6"), ArtLibrary.Hex("#F2C14E"), ArtLibrary.Hex("#FFFFFF"), ArtLibrary.Hex("#8FD06C"),
            ArtLibrary.Hex("#B58CE4"), ArtLibrary.Hex("#F2C14E"), ArtLibrary.Hex("#F7C9A6"), ArtLibrary.Hex("#59C9B9"),
            ArtLibrary.Hex("#E8524A"), ArtLibrary.Hex("#F2C14E"), ArtLibrary.Hex("#CFE9F5"), ArtLibrary.Hex("#FF7BA9"),
        };
        private static readonly Color TaxiYellow = ArtLibrary.Hex("#F5C542");

        private readonly Transform _root;
        private readonly List<Traffic.Car> _fleet;
        private readonly List<Transform> _carNodes = new List<Transform>();
        private readonly List<Pedestrians.Route> _routes;
        private readonly List<Transform> _walkerNodes = new List<Transform>();
        private readonly List<GoobyAvatar> _walkers = new List<GoobyAvatar>();
        private readonly List<int> _walkerState = new List<int>();   // 0 walk, 1 stand, 2 wave
        private readonly List<Transform> _papers = new List<Transform>();
        private readonly HashSet<(int r, int c)> _lightTiles = new HashSet<(int, int)>();
        private readonly List<(Renderer bulb, bool ew, int slot)> _bulbs = new List<(Renderer, bool, int)>();   // slot 0 red, 1 amber, 2 green
        private readonly Material _bulbRed, _bulbAmber, _bulbGreen, _bulbOff;
        private double _time, _walkTime, _hour;
        private int _lightSig = -1;
        private float _nearMissCool;

        public int CarCount => _fleet.Count;
        public int WalkerCount => _routes.Count;
        public int LightCount => _lightTiles.Count;
        public Transform DeliveryVan { get; private set; }

        private Transform _delivery;
        private string _deliveryPhase = "";

        public CityLife(Transform parent, double hour)
        {
            _root = new GameObject("Life").transform;
            _root.SetParent(parent, false);
            _hour = hour;
            _bulbRed = ArtLibrary.Glow(new Color(2.4f, 0.5f, 0.42f));
            _bulbAmber = ArtLibrary.Glow(new Color(2.4f, 1.8f, 0.45f));
            _bulbGreen = ArtLibrary.Glow(new Color(0.6f, 2.3f, 1.0f));
            _bulbOff = ArtLibrary.Solid(ArtLibrary.Hex("#2A2830"), 0.4f);

            _fleet = Traffic.Fleet(Traffic.Loops(), hour, CityGrid.Seed);
            for (var i = 0; i < _fleet.Count; i++) _carNodes.Add(BuildCar(_fleet[i], i));

            _routes = Pedestrians.Routes(hour, CityGrid.Seed + 7);
            for (var i = 0; i < _routes.Count; i++)
            {
                var anchor = new GameObject("Walker" + i).transform;
                anchor.SetParent(_root, false);
                anchor.gameObject.SetActive(_routes[i].Active);
                _walkerNodes.Add(anchor);
                _walkers.Add(null);
                _walkerState.Add(-1);
                _papers.Add(null);
                _ = SpawnWalkerAsync(i, anchor);
            }

            foreach (var tile in Traffic.LightTiles()) { _lightTiles.Add(tile); BuildLights(tile.r, tile.c); }
            Tick(0f, hour, new Vector3(1e6f, 0f, 1e6f), Vector3.zero);
        }

        // ── cars ─────────────────────────────────────────────────────────────

        private Transform BuildCar(Traffic.Car car, int index)
        {
            var anchor = new GameObject("Traffic" + index).transform;
            anchor.SetParent(_root, false);
            var model = Models[car.Model % Models.Length];
            var paint = model == "car_taxi" ? TaxiYellow : Paints[car.Tint % Paints.Length];
            CityScene.TintCar(anchor, paint);
            anchor.gameObject.AddComponent<VehicleRig>();
            _ = AssetLoader.InstanceAsync(AssetLoader.VehiclePath(model), anchor, model);
            anchor.gameObject.SetActive(car.Active);
            return anchor;
        }

        // ── walkers ──────────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task SpawnWalkerAsync(int index, Transform anchor)
        {
            var avatar = await GoobyAvatar.CreateAsync(anchor, Vector3.zero);
            if (avatar == null || anchor == null) return;
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            // grown-up Goobys of the town: a good third taller than the player's little one, so they read from the car
            avatar.transform.localScale = Vector3.one * (1.3f + (index % 3) * 0.08f);
            var route = _routes[index];
            var equipped = new Dictionary<string, string> { ["fell"] = route.Fur };
            if (route.Hat != null) equipped["hut"] = route.Hat;
            avatar.Wear(equipped);
            avatar.SetEmotion(index % 3 == 0 ? "ecstatic" : "happy");
            _walkers[index] = avatar;
            _walkerState[index] = -1;
            if (route.Newspaper) _papers[index] = BuildNewspaper(avatar.transform);
        }

        private static Transform BuildNewspaper(Transform gooby)
        {
            var paper = new GameObject("Zeitung").transform;
            paper.SetParent(gooby, false);
            paper.localPosition = new Vector3(0f, 0.42f, 0.24f);
            var mat = ArtLibrary.Solid(new Color(0.96f, 0.95f, 0.88f), 0.08f);
            foreach (var side in new[] { -1f, 1f })
            {
                var leaf = ArtLibrary.Box(paper, "Blatt", paper.position, new Vector3(0.16f, 0.22f, 0.012f), mat, false);
                leaf.transform.localPosition = new Vector3(side * 0.08f, 0f, 0f);
                leaf.transform.localRotation = Quaternion.Euler(-12f, side * 22f, 0f);
            }
            paper.gameObject.SetActive(false);
            return paper;
        }

        // ── lights ───────────────────────────────────────────────────────────

        /// <summary>
        /// One light per incoming arm of the junction, on the pavement corner to the right of the arriving cars, its
        /// head turned towards them; the three bulbs are runtime spheres so the phase can recolour them.
        /// </summary>
        private void BuildLights(int r, int c)
        {
            var (cx, cz) = CityGrid.World(r, c);
            var centre = new Vector3((float)cx, 0f, (float)cz);
            var edge = (float)(CityGrid.RoadHalfWidth + CityGrid.WalkWidth * 0.5);
            foreach (var (dr, dc) in CityGrid.StreetNeighbours(r, c))
            {
                var (nx, nz) = CityGrid.World(r + dr, c + dc);
                var dir = (new Vector3((float)nx, 0f, (float)nz) - centre).normalized;
                var side = new Vector3(dir.z, 0f, -dir.x);
                var at = centre + dir * edge - side * edge + Vector3.up * 0.12f;
                var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                var pole = Scenery.Place(_root, "traffic_light", at, yaw);
                var ew = Mathf.Abs(dir.x) > Mathf.Abs(dir.z);
                for (var slot = 0; slot < 3; slot++)
                {
                    var bulb = ArtLibrary.Sphere(pole.transform, "Bulb", Vector3.zero, 0.105f, _bulbOff, false);
                    bulb.transform.localPosition = new Vector3(0f, 3.28f - slot * 0.3f, 0.19f);
                    var rend = bulb.GetComponent<Renderer>();
                    if (rend != null) _bulbs.Add((rend, ew, slot));
                }
            }
        }

        private void ApplyLights()
        {
            var blink = CityRhythm.LightsBlink(_hour);
            var state = Traffic.LightState(_time);
            var blinkOn = Traffic.BlinkOn(_time);
            // one signature per visible configuration: bulbs are only touched when it changes
            var sig = blink ? (blinkOn ? 1 : 2) : 16 | (state.nsGreen ? 4 : 0) | (state.ewGreen ? 8 : 0);
            if (sig == _lightSig) return;
            _lightSig = sig;
            foreach (var (bulb, ew, slot) in _bulbs)
            {
                if (bulb == null) continue;
                Material m;
                if (blink) m = slot == 1 && blinkOn ? _bulbAmber : _bulbOff;
                else
                {
                    var green = ew ? state.ewGreen : state.nsGreen;
                    m = slot == 2 && green ? _bulbGreen : slot == 0 && !green ? _bulbRed : _bulbOff;
                }
                if (bulb.sharedMaterial != m) bulb.sharedMaterial = m;
            }
        }

        // ── tick ─────────────────────────────────────────────────────────────

        /// <summary>The traffic never integrates more than this per frame (the drive model caps at the same): a loading hitch must not throw a car twenty metres.</summary>
        public const float MaxTickSec = 0.5f;
        /// <summary>… and steps in slices of at most this, so the gaps to the car in front and to the player hold at any frame rate.</summary>
        public const float SubStepSec = 0.1f;

        /// <summary>Advances cars, walkers and lights by dt; <paramref name="player"/> is the hero car's world position, <paramref name="playerVelocity"/> its velocity in m/s.</summary>
        public void Tick(float dt, double hour, Vector3 player, Vector3 playerVelocity)
        {
            dt = Mathf.Min(dt, MaxTickSec);
            _time += dt;
            _walkTime += dt;
            if (System.Math.Abs(hour - _hour) > 1e-6)
            {
                _hour = hour;
                _lightSig = -1;
                if (Traffic.SetHour(_fleet, hour))
                    for (var i = 0; i < _fleet.Count; i++) _carNodes[i].gameObject.SetActive(_fleet[i].Active);
                if (Pedestrians.SetHour(_routes, hour))
                    for (var i = 0; i < _routes.Count; i++) _walkerNodes[i].gameObject.SetActive(_routes[i].Active);
            }
            var lights = CityRhythm.LightsBlink(hour) ? null : _lightTiles;
            var steps = Mathf.Max(1, Mathf.CeilToInt(dt / SubStepSec));
            var h = dt / steps;
            for (var k = 0; k < steps; k++)
                for (var i = 0; i < _fleet.Count; i++)
                {
                    var car = _fleet[i];
                    if (!car.Active) continue;
                    var gap = System.Math.Min(Traffic.GapAhead(car, _fleet), Traffic.GapToPlayer(car, player.x, player.z, playerVelocity.x, playerVelocity.z));
                    Traffic.Step(car, h, _time, lights, gap);
                }
            for (var i = 0; i < _fleet.Count; i++)
            {
                var car = _fleet[i];
                if (!car.Active) continue;
                var p = car.Loop.At(car.S);
                var node = _carNodes[i];
                node.position = new Vector3((float)p.x, 0f, (float)p.z);
                node.rotation = Quaternion.Euler(0f, Mathf.Atan2((float)p.dx, (float)p.dz) * Mathf.Rad2Deg, 0f);
            }
            for (var i = 0; i < _routes.Count; i++)
            {
                var route = _routes[i];
                if (!route.Active) continue;
                var pose = Pedestrians.At(route, _walkTime);
                var node = _walkerNodes[i];
                node.position = new Vector3((float)pose.X, 0.12f, (float)pose.Z);
                node.rotation = Quaternion.Euler(0f, (float)(pose.Heading * Mathf.Rad2Deg), 0f);
                var reading = Pedestrians.ReadingPaper(route, pose, _hour);
                if (i < _papers.Count && _papers[i] != null) _papers[i].gameObject.SetActive(reading);
                var avatar = _walkers[i];
                if (avatar == null) continue;
                var state = pose.Standing ? (reading ? 1 : pose.Waving ? 2 : 1) : 0;
                if (state == _walkerState[i]) continue;
                _walkerState[i] = state;
                if (state == 0) avatar.Play("walk-loop");
                else if (state == 2) avatar.Play("wave");
                else avatar.Play("idle-loop");
            }
            ApplyLights();
            PoseDelivery();
            _logTimer -= dt;
            if (_logTimer <= 0f)
            {
                // a heartbeat for the playtest logs: how much life is within sight of the car
                _logTimer = 10f;
                int carsNear = 0, walkersNear = 0; var nearest = float.MaxValue;
                for (var i = 0; i < _fleet.Count; i++)
                {
                    if (!_fleet[i].Active) continue;
                    var d = Vector3.Distance(_carNodes[i].position, player);
                    if (d < 80f) carsNear++;
                    nearest = Mathf.Min(nearest, d);
                }
                var nearestWalker = float.MaxValue; var loadedWalkers = 0; var nearestAt = Vector3.zero; var visible = 0;
                for (var i = 0; i < _routes.Count; i++)
                {
                    if (_walkers[i] != null) { loadedWalkers++; if (_walkers[i].GetComponentInChildren<Renderer>() is Renderer rr && rr.isVisible) visible++; }
                    if (!_routes[i].Active) continue;
                    var d = Vector3.Distance(_walkerNodes[i].position, player);
                    if (d < 80f) walkersNear++;
                    if (d < nearestWalker) { nearestWalker = d; nearestAt = _walkerNodes[i].position; }
                }
                Debug.Log($"[life] h={hour:F1} cars={_fleet.FindAll(c => c.Active).Count}/{_fleet.Count} near80={carsNear} nearest={nearest:F0}m walkers={_routes.FindAll(r => r.Active).Count}/{_routes.Count} loaded={loadedWalkers} visible={visible} near80={walkersNear} nearest={nearestWalker:F0}m at=({nearestAt.x:F0},{nearestAt.z:F0}) player=({player.x:F0},{player.z:F0}) lights={_lightTiles.Count} paper={(CityRhythm.NewspaperGooby(_hour) ? "on" : "off")} gooberando={(_deliveryPhase.Length > 0 ? _deliveryPhase : "off")}");
            }
        }

        private float _logTimer;

        /// <summary>A red light within Traffic.LookAheadM of the player's heading — never at night (amber blink).</summary>
        public bool RedAheadOfPlayer(double x, double z, double dx, double dz) =>
            !CityRhythm.LightsBlink(_hour) && Traffic.RedAhead(x, z, dx, dz, _time, _lightTiles);

        /// <summary>
        /// Horn + sparks when an ambient car passes within CityAmbiente.NearMissM while the player is moving
        /// at least NearMissSpeed. Quiet for NearMissPauseS afterwards. Returns true on a hit (the HUD toasts).
        /// </summary>
        public bool PollNearMiss(Vector3 player, double speedMs, float dt, bool reducedMotion)
        {
            _nearMissCool = Mathf.Max(0f, _nearMissCool - dt);
            if (_nearMissCool > 0f) return false;
            for (var i = 0; i < _fleet.Count; i++)
            {
                if (!_fleet[i].Active) continue;
                var other = _carNodes[i].position;
                var dx = other.x - player.x; var dz = other.z - player.z;
                var dist = System.Math.Sqrt(dx * dx + dz * dz);
                if (!CityAmbiente.IsNearMiss(dist, speedMs)) continue;
                _nearMissCool = (float)CityAmbiente.NearMissPauseS;
                if (CityAmbiente.ShouldSpark(dist, speedMs, reducedMotion))
                {
                    var spark = CityAmbiente.SparkPoint(player.x, player.z, other.x, other.z);
                    SpawnSparks(new Vector3((float)spark.x, (float)spark.y, (float)spark.z));
                }
                Debug.Log($"[drive] near-miss d={dist:F1}m v={speedMs:F1}m/s spark={!reducedMotion}");
                return true;
            }
            return false;
        }

        private void SpawnSparks(Vector3 at)
        {
            var go = new GameObject("NearMissSparks");
            go.transform.SetParent(_root, false);
            go.transform.position = at;
            var mat = ArtLibrary.Glow(new Color(2.55f, 1.9f, 0.55f));
            for (var i = 0; i < 8; i++)
            {
                var a = i * 45f * Mathf.Deg2Rad;
                var p = at + new Vector3(Mathf.Cos(a) * 0.28f, 0.12f, Mathf.Sin(a) * 0.28f);
                ArtLibrary.Sphere(go.transform, "spark", p, 0.07f, mat, false);
            }
            UnityEngine.Object.Destroy(go, 0.5f);
        }

        /// <summary>Orange GOOBERANDO van (Godot #FF7A00) on the kitchen→home polyline, or hidden when idle.</summary>
        public void SetDelivery(GooberandoFahrerSim.Status pose)
        {
            var phase = pose?.Phase ?? "";
            var changed = phase != _deliveryPhase;
            if (changed)
                Debug.Log("[gooberando] van=" + (phase.Length > 0 ? phase : "off")
                    + (pose != null ? $" at=({pose.X:F1},{pose.Z:F1}) dir=({pose.Dx:F1},{pose.Dz:F1})" : ""));
            _deliveryPose = pose;
            _deliveryPhase = phase;
            if (pose == null)
            {
                if (_delivery != null) _delivery.gameObject.SetActive(false);
                return;
            }
            EnsureDeliveryVan();
            _delivery.gameObject.SetActive(true);
            PoseDelivery();
            if (changed)
                Debug.Log($"[gooberando] posed ({_delivery.position.x:F1},{_delivery.position.z:F1}) children={_delivery.childCount}");
        }

        private GooberandoFahrerSim.Status _deliveryPose;

        private void EnsureDeliveryVan()
        {
            if (_delivery != null) return;
            _delivery = new GameObject("GooberandoVan").transform;
            _delivery.SetParent(_root, false);
            // van-sized hull so the delivery is visible before the GLB streams in (Godot #FF7A00)
            var hull = ArtLibrary.Box(_delivery, "VanHull", _delivery.position + Vector3.up * 0.75f, new Vector3(2.2f, 1.5f, 4.4f), ArtLibrary.Solid(ArtLibrary.Hex("FF7A00"), 0.35f));
            CityScene.TintCar(_delivery, ArtLibrary.Hex("FF7A00"));
            _delivery.gameObject.AddComponent<VehicleRig>();
            _ = FillVanAsync(hull);
            DeliveryVan = _delivery;
        }

        private async Task FillVanAsync(GameObject hull)
        {
            var clone = await AssetLoader.InstanceAsync(AssetLoader.VehiclePath("van_delivery"), _delivery, "van_delivery");
            if (clone == null || hull == null) return;
            hull.SetActive(false);
        }

        private void PoseDelivery()
        {
            if (_delivery == null || _deliveryPose == null) return;
            var dx = _deliveryPose.Dx; var dz = _deliveryPose.Dz;
            var len = Mathf.Sqrt((float)(dx * dx + dz * dz));
            if (len < 1e-9f) { dx = 1; dz = 0; len = 1; }
            dx /= len; dz /= len;
            var lane = (float)CityGrid.LaneOffset;
            _delivery.position = new Vector3((float)_deliveryPose.X + (float)dz * lane, 0f, (float)_deliveryPose.Z + (float)(-dx) * lane);
            var yaw = Mathf.Atan2((float)dx, (float)dz) * Mathf.Rad2Deg;
            _delivery.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>The ambient cars' current positions (for tests and the HUD's near-miss check).</summary>
        public IEnumerable<Vector3> CarPositions()
        {
            for (var i = 0; i < _fleet.Count; i++) if (_fleet[i].Active) yield return _carNodes[i].position;
        }

        /// <summary>
        /// Clear road ahead of the player in his own lane: bumper gap (m) and speed (km/h) of the nearest ambient car
        /// driving the same way within DriveModel.FollowM + a car length, or null when the lane is free.
        /// </summary>
        public (double gap, double kmh)? LeaderAhead(Vector3 pos, Vector3 forward)
        {
            var right = new Vector3(forward.z, 0f, -forward.x);
            (double gap, double kmh)? best = null;
            for (var i = 0; i < _fleet.Count; i++)
            {
                if (!_fleet[i].Active) continue;
                var node = _carNodes[i];
                var rel = node.position - pos;
                var ahead = Vector3.Dot(rel, forward);
                if (ahead < 0.5f || ahead > (float)DriveModel.FollowM + 4.5f) continue;
                var same = Vector3.Dot(node.forward, forward);
                if (same < -0.5f) continue;                                        // oncoming: the other lane
                // a car turning into our lane at a junction is crossing until it has swung round — it counts while
                // it is close and roughly on our line, so we brake for it instead of driving through
                var lateral = Mathf.Abs(Vector3.Dot(rel, right));
                if (same >= 0.5f ? lateral > 1.8f : lateral > 2.6f || ahead > 12f) continue;
                var gap = ahead - 4.2;                                              // centre to centre minus a car length
                if (best == null || gap < best.Value.gap) best = (gap, _fleet[i].Speed * 3.6);
            }
            return best;
        }
    }
}
