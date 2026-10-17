using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D dusk town of the Liefer-Hetze (port of delivery_rush_world.gd): the 9×9 ring-and-cross town from the engine's
    /// grid in a warm peach evening — asphalt, kerbs and marks, town houses on the blocks, parks on the rim, and the six
    /// landmarks the parcels go to (GOOBY MART, the vet, the fountain, the sky tower, the park gazebo, the windmill café).
    /// The GOOBERANDO van carries the player's Gooby (peeking out of the roof hatch); the route to the drop glows as a pink
    /// ribbon on the road, the drop is a gold ring with a beam and a parcel spinning over it, traffic loops the ring and
    /// the cross street. A camera above and behind keeps the van's nose up. Engine metres map with z mirrored.
    /// </summary>
    public sealed partial class DeliveryRushScreen
    {
        private World.MgStage3D _stage;
        private Transform _van3d, _drop3d, _dropBeam, _parcel;
        private readonly Dictionary<DeliveryRushGame.Car, Transform> _cars3d = new Dictionary<DeliveryRushGame.Car, Transform>();
        private MeshFilter _routeFilter;
        private Mesh _routeMesh;
        private int _routeKey = -1;
        private readonly List<Vector3> _routeVerts = new List<Vector3>();
        private readonly List<int> _routeTris = new List<int>();
        private float _camYaw, _clock;
        private bool _camSet;

        private static readonly string[] Houses = { "house_pink", "house_cream", "house_blue", "house_yellow", "cottage", "house_salmon", "cottage_green" };

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 W(double x, double z, float y = 0f) => World.MgStage3D.At((float)x, y, (float)-z);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.96f, 0.76f, 0.66f));
            _stage.Bind(Field);
            _stage.Camera.fieldOfView = 62f;
            _stage.Camera.farClipPlane = 280f;
            World.MgStudio.Weather(0.9f, 0.95f, 0f, 0f);
            var root = _stage.Root.transform;
            _stage.Ground(-0.05f, World.ArtLibrary.Hex("#9FD08F"), "grass", 420f, 420f, 0f);
            var asphalt = World.ArtLibrary.PbrSlab("asphalt", new Vector2(20f, 20f), 4f, World.ArtLibrary.Hex("#B1AEC8"), 0.25f);
            var kerb = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F3E6D0"), 0.3f);
            var mark = World.ArtLibrary.Solid(new Color(1f, 0.97f, 0.88f), 0.3f);
            const float t = (float)DeliveryRush.TileM;
            for (var r = -1; r <= DeliveryRush.Grid; r++)
                for (var c = -1; c <= DeliveryRush.Grid; c++)
                {
                    var w = DeliveryRush.TileToWorld(r, c);
                    var at = W(w.x, w.z);
                    var inside = r >= 0 && r < DeliveryRush.Grid && c >= 0 && c < DeliveryRush.Grid;
                    var kind = inside ? _game.Grid[r, c] : "rim";
                    if (kind == "road")
                    {
                        World.ArtLibrary.Box(root, "Road", at + Vector3.down * 0.03f, new Vector3(t, 0.06f, t), asphalt, false);
                        if (r % 2 == 1 && c % 2 == 0) World.ArtLibrary.Box(root, "Mark", at + Vector3.up * 0.005f, new Vector3(0.4f, 0.01f, t * 0.32f), mark, false);
                        else if (c % 2 == 1 && r % 2 == 0) World.ArtLibrary.Box(root, "Mark", at + Vector3.up * 0.005f, new Vector3(t * 0.32f, 0.01f, 0.4f), mark, false);
                        foreach (var (dr, dc, ox, oz, sx, sz) in new[] { (0, -1, -0.5f, 0f, 0.5f, t), (0, 1, 0.5f, 0f, 0.5f, t), (-1, 0, 0f, 0.5f, t, 0.5f), (1, 0, 0f, -0.5f, t, 0.5f) })
                        {
                            var nr = r + dr; var nc = c + dc;
                            var road = nr >= 0 && nr < DeliveryRush.Grid && nc >= 0 && nc < DeliveryRush.Grid && _game.Grid[nr, nc] == "road";
                            if (!road) World.ArtLibrary.Box(root, "Kerb", at + new Vector3(ox * t, 0.08f, oz * t), new Vector3(sx, 0.16f, sz), kerb, false);
                        }
                    }
                    else if (kind == "block")
                    {
                        if (IsLandmarkTile(r, c)) continue;
                        var yaw = RoadSideYaw(r, c);
                        _stage.PlaceScenery(Houses[((r * 7 + c * 3) % Houses.Length + Houses.Length) % Houses.Length], at, yaw, 1.3f);
                        _stage.PlaceScenery("tree_round", at + new Vector3(6.5f, 0f, -6.5f), r * 25f, 1.5f);
                        _stage.PlaceScenery("lamp_post", at + new Vector3(-7.5f, 0f, 7.5f), 0f, 1f);
                    }
                    else
                    {
                        _stage.PlaceScenery("tree_round", at + new Vector3(-4f, 0f, 3f), r * 20f, 1.7f);
                        _stage.PlaceScenery("tree_round", at + new Vector3(5f, 0f, -4f), c * 30f, 1.4f);
                        _stage.PlaceScenery("bush_flowers", at + new Vector3(1f, 0f, 5f), 0f, 1.1f);
                    }
                }
            Landmarks();
            _stage.Hills(-0.05f, 180f, 12, new Color(0.62f, 0.74f, 0.66f), 26f, 360f, 0f, 31);
            _stage.Hills(-0.05f, -180f, 12, new Color(0.62f, 0.74f, 0.66f), 26f, 360f, 0f, 32);
            _stage.Sun(new Vector3(-60f, 40f, 220f), 12f, new Color(1.35f, 0.82f, 0.55f));
            _stage.Clouds(8, 70f, 190f, 300f, 12f, 33);
            // the drop ring, its beam and the parcel over it; the route ribbon
            _drop3d = _stage.Torus(null, "Drop", Vector3.zero, (float)_game.Tune.DropRadiusM, 0.35f, World.ArtLibrary.Glow(new Color(1.45f, 1.15f, 0.42f))).transform;
            _dropBeam = World.ArtLibrary.Cylinder(root, "Beam", Vector3.zero, (float)_game.Tune.DropRadiusM * 0.75f, 20f, World.ArtLibrary.Transparent(new Color(1f, 0.85f, 0.4f, 0.14f)), false).transform;
            _parcel = World.ArtLibrary.Box(root, "Parcel", Vector3.zero, new Vector3(1.4f, 1.1f, 1.4f), World.ArtLibrary.Pbr("fabric", 0.6f, World.ArtLibrary.Hex("#D9A66B"), 0.1f)).transform;
            World.ArtLibrary.Box(_parcel, "Tape", Vector3.zero, new Vector3(1.42f, 0.2f, 1.42f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF8B8B"), 0.4f), false).transform.localPosition = Vector3.zero;
            var routeGo = new GameObject("Route");
            routeGo.transform.SetParent(root, false);
            _routeFilter = routeGo.AddComponent<MeshFilter>();
            _routeMesh = new Mesh { name = "Route" };
            _routeMesh.MarkDynamic();
            _routeFilter.sharedMesh = _routeMesh;
            var rr = routeGo.AddComponent<MeshRenderer>();
            rr.sharedMaterial = World.ArtLibrary.Glow(new Color(1.2f, 0.45f, 0.75f));
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // the van
            _van3d = new GameObject("Van").transform;
            _van3d.SetParent(root, false);
            _ = World.AssetLoader.InstanceAsync(World.AssetLoader.VehiclePath("van_delivery"), _van3d, "van_delivery");
            World.CityScene.TintCar(_van3d, VanC);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnCourier(outfit);
        }

        private static bool IsLandmarkTile(int r, int c)
        {
            foreach (var lm in DeliveryRush.Landmarks)
            {
                var tile = DeliveryRush.WorldToTile(lm.X, lm.Z);
                if (tile.r == r && tile.c == c) return true;
            }
            return false;
        }

        private float RoadSideYaw(int r, int c)
        {
            bool Road(int rr, int cc) => rr >= 0 && rr < DeliveryRush.Grid && cc >= 0 && cc < DeliveryRush.Grid && _game.Grid[rr, cc] == "road";
            return Road(r + 1, c) ? 180f : Road(r - 1, c) ? 0f : Road(r, c - 1) ? -90f : 90f;
        }

        private void Landmarks()
        {
            foreach (var lm in DeliveryRush.Landmarks)
            {
                var at = W(lm.X, lm.Z);
                var tile = DeliveryRush.WorldToTile(lm.X, lm.Z);
                var yaw = RoadSideYaw(tile.r, tile.c);
                switch (lm.Id)
                {
                    case "shop": _stage.PlaceScenery("shop_mart", at, yaw, 1.2f); break;
                    case "vetClinic": _stage.PlaceScenery("shop_goobyman", at, yaw, 1.2f); break;
                    case "fountain": _stage.Prop("fountain", at, 5f, 0f); _stage.PlaceScenery("park_bench", at + new Vector3(4f, 0f, 0f), -90f, 1.1f); break;
                    case "skyTower": _stage.PlaceScenery("church_tower", at, yaw, 1.4f); break;
                    case "parkGazebo": Gazebo(at); break;
                    default: _stage.Prop("windmill", at, 7f, yaw); _stage.PlaceScenery("cafe_set", at + new Vector3(5f, 0f, 3f), 0f, 1.1f); break;
                }
            }
        }

        /// <summary>A white park gazebo: a round floor, six posts and a pink cone roof.</summary>
        private void Gazebo(Vector3 at)
        {
            var root = _stage.Root.transform;
            World.ArtLibrary.Cylinder(root, "GazeboFloor", at + Vector3.up * 0.15f, 2.6f, 0.3f, World.ArtLibrary.Pbr("wood_planks", 1f, World.ArtLibrary.Hex("#F3E6D0"), 0.3f));
            for (var i = 0; i < 6; i++)
            {
                var a = i * Mathf.PI * 2f / 6f;
                World.ArtLibrary.Cylinder(root, "Post", at + new Vector3(Mathf.Cos(a) * 2.2f, 1.4f, Mathf.Sin(a) * 2.2f), 0.1f, 2.5f, World.ArtLibrary.Solid(Color.white, 0.4f));
            }
            _stage.Cone(at + Vector3.up * 2.65f, 3f, 1.6f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F08A80"), 0.35f), true);
        }

        private async System.Threading.Tasks.Task SpawnCourier(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(_van3d.position, 0.95f, outfit);
            if (g == null || _stage == null) return;
            g.transform.SetParent(_van3d, true);
            g.transform.localPosition = new Vector3(0f, 1.25f, 0.6f);
            g.transform.localRotation = Quaternion.identity;
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
        }

        private Transform Car(DeliveryRushGame.Car car)
        {
            var t = new GameObject("Traffic").transform;
            t.SetParent(_stage.Root.transform, false);
            _ = World.AssetLoader.InstanceAsync(World.AssetLoader.VehiclePath(car.Cross ? "car_taxi" : "car_small"), t, "traffic");
            World.CityScene.TintCar(t, car.Cross ? new Color(0.42f, 0.62f, 0.9f) : new Color(0.85f, 0.35f, 0.4f));
            return t;
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _clock += dt;
            var g = _game;
            var heading = (float)g.Heading * Mathf.Rad2Deg;
            _van3d.position = W(g.VanX, g.VanZ, 0.05f);
            _van3d.rotation = Quaternion.Euler(0f, heading, -(float)g.Steer * 3f);
            var drop = g.CurrentDrop;
            _drop3d.position = W(drop.x, drop.z, 0.4f);
            _drop3d.localScale = Vector3.one * (float)g.Tune.DropRadiusM * (1f + Mathf.Sin(_clock * 4f) * 0.04f);
            _dropBeam.position = W(drop.x, drop.z, 10f);
            _parcel.position = W(drop.x, drop.z, 3.2f + Mathf.Sin(_clock * 2.5f) * 0.4f);
            _parcel.rotation = Quaternion.Euler(0f, _clock * 70f, 0f);
            foreach (var car in g.Traffic)
            {
                if (!_cars3d.TryGetValue(car, out var tr)) _cars3d[car] = tr = Car(car);
                var p = DeliveryRushGame.TrafficPos(car);
                var ahead = DeliveryRushGame.TrafficPos(new DeliveryRushGame.Car { S = car.S + 0.5 * System.Math.Sign(car.Speed == 0 ? 1 : car.Speed), Lane = car.Lane, Cross = car.Cross });
                tr.position = W(p.x, p.z, 0.05f);
                var dir = W(ahead.x, ahead.z) - W(p.x, p.z);
                if (dir.sqrMagnitude > 1e-6f) tr.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            UpdateRoute();
            var k = _camSet ? 1f - Mathf.Exp(-4f * dt) : 1f;
            _camYaw = Mathf.LerpAngle(_camYaw, heading, k);
            _camSet = true;
            var fwd = Quaternion.Euler(0f, _camYaw, 0f) * Vector3.forward;
            var cam = _van3d.position - fwd * 12f + Vector3.up * 18f;
            _stage.Camera.transform.position = cam;
            _stage.Camera.transform.rotation = Quaternion.LookRotation(_van3d.position + fwd * 6f - cam, Vector3.up);
        }

        /// <summary>The route as a glowing pink ribbon on the road (rebuilt when it changes).</summary>
        private void UpdateRoute()
        {
            var g = _game;
            var key = g.Route.Count * 7919 + g.Parcel;
            if (g.Route.Count > 0) key += (int)(g.Route[0].x * 13 + g.Route[g.Route.Count - 1].z * 7);
            if (key == _routeKey) return;
            _routeKey = key;
            _routeVerts.Clear();
            _routeTris.Clear();
            _routeMesh.Clear();
            if (g.Route.Count < 2) return;
            var o = _stage.Root.transform;
            for (var i = 0; i < g.Route.Count; i++)
            {
                var p = W(g.Route[i].x, g.Route[i].z, 0.06f);
                var next = i + 1 < g.Route.Count ? W(g.Route[i + 1].x, g.Route[i + 1].z, 0.06f) : p + (p - W(g.Route[i - 1].x, g.Route[i - 1].z, 0.06f));
                var back = i > 0 ? W(g.Route[i - 1].x, g.Route[i - 1].z, 0.06f) : p - (next - p);
                var tan = next - back;
                tan.y = 0f;
                var side = tan.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, tan.normalized) : Vector3.right;
                _routeVerts.Add(o.InverseTransformPoint(p - side * 0.45f));
                _routeVerts.Add(o.InverseTransformPoint(p + side * 0.45f));
                if (i == 0) continue;
                var k = i * 2;
                _routeTris.AddRange(new[] { k - 2, k, k - 1, k - 1, k, k + 1 });
            }
            _routeMesh.SetVertices(_routeVerts);
            _routeMesh.SetTriangles(_routeTris, 0);
            _routeMesh.RecalculateNormals();
            _routeMesh.RecalculateBounds();
        }

        private void StageDropped()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("ecstatic");
            gb.Play("wave", 0.1f);
            gb.Play("sit-loop", 0.6f);
            if (!ReducedMotion) World.Effects.Sparkle(_van3d.position + Vector3.up * 2.4f, 1.2f, 26);
        }

        private void StageCrashed()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("dizzy");
            if (!ReducedMotion) World.Effects.Puff(_van3d.position + Vector3.up, new Color(0.9f, 0.85f, 0.75f, 0.9f), 14, 0.5f, 3f, 0.6f, Vector3.up, 1f);
        }

        private void DropStage()
        {
            if (_stage != null) World.MgStudio.Weather(1f, 1f);
            _cars3d.Clear();
            _routeKey = -1;
            if (_routeMesh != null) Object.Destroy(_routeMesh);
            _routeMesh = null;
            _camSet = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
