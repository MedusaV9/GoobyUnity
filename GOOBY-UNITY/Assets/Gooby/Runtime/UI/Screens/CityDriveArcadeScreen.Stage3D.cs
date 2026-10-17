using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D town of the Einkaufsfahrt (port of city_drive_world.gd): the 7×7 tile town built from the engine's grid —
    /// asphalt with dashed marks and kerbs, pastel town houses with trees on the house tiles inside the ring, parks with
    /// round trees, flowers and benches outside it, hills round the edge. The player's car is the town's hero hatchback in
    /// the paint of the active Autohaus car, Gooby peeking out of its sunroof; coins spin over the road, the pink checkpoint
    /// ring glows with a beam of light, traffic cars loop the ring and the cross street. A camera follows above and behind
    /// the car, nose up like the old map. Engine metres map with z mirrored (forward at heading 0 is +z).
    /// </summary>
    public sealed partial class CityDriveArcadeScreen
    {
        private World.MgStage3D _stage;
        private Transform _car3d, _ring3d, _beam;
        private readonly List<Transform> _coins3d = new List<Transform>();
        private readonly Dictionary<CityDriveGame.Car, Transform> _traffic3d = new Dictionary<CityDriveGame.Car, Transform>();
        private float _camYaw, _clock;
        private bool _camSet;

        private static readonly string[] HouseModels = { "house_pink", "house_yellow", "house_blue", "house_cream", "house_green", "house_salmon", "cottage", "cottage_pink" };

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 W(double x, double z, float y = 0f) => World.MgStage3D.At((float)x, y, (float)-z);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.66f, 0.84f, 0.97f));
            _stage.Bind(Field);
            _stage.Camera.fieldOfView = 62f;
            _stage.Camera.farClipPlane = 260f;
            var root = _stage.Root.transform;
            _stage.Ground(-0.05f, World.ArtLibrary.Hex("#A9D89A"), "grass", 400f, 400f, 0f);
            var asphalt = World.ArtLibrary.PbrSlab("asphalt", new Vector2(20f, 20f), 4f, World.ArtLibrary.Hex("#BDB9D3"), 0.25f);
            var kerb = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFF3DF"), 0.3f);
            var mark = World.ArtLibrary.Solid(new Color(1f, 0.98f, 0.9f), 0.3f);
            for (var r = -2; r <= CityDriveArcade.Grid + 1; r++)
                for (var c = -2; c <= CityDriveArcade.Grid + 1; c++)
                {
                    var w = CityDriveArcade.TileToWorld(r, c);
                    var at = W(w.x, w.z);
                    if (CityDriveArcade.IsRoad(r, c)) RoadTile(root, r, c, at, asphalt, kerb, mark);
                    else if (r >= CityDriveArcade.RingMin && r <= CityDriveArcade.RingMax && c >= CityDriveArcade.RingMin && c <= CityDriveArcade.RingMax) HouseTile(r, c, at);
                    else ParkTile(r, c, at);
                }
            _stage.Hills(-0.05f, 140f, 11, new Color(0.62f, 0.8f, 0.6f), 22f, 320f, 0f, 21);
            _stage.Hills(-0.05f, -140f, 11, new Color(0.62f, 0.8f, 0.6f), 22f, 320f, 0f, 22);
            _stage.Clouds(8, 60f, 150f, 260f, 9f, 23);
            // the checkpoint ring and its beam
            _ring3d = _stage.Torus(null, "Checkpoint", Vector3.zero, (float)_game.Tune.CheckpointRadiusM, 0.35f, World.ArtLibrary.Glow(new Color(1.4f, 0.6f, 0.95f))).transform;
            _beam = World.ArtLibrary.Cylinder(root, "Beam", Vector3.zero, (float)_game.Tune.CheckpointRadiusM * 0.8f, 18f, World.ArtLibrary.Transparent(new Color(1f, 0.6f, 0.85f, 0.13f)), false).transform;
            for (var i = 0; i < _game.Coins.Count; i++) _coins3d.Add(_stage.Prop("item-coin-gold", Vector3.zero, 5.5f));
            // the hero car in the active paint, Gooby looking out of its sunroof
            _car3d = new GameObject("HeroCar").transform;
            _car3d.SetParent(root, false);
            _ = World.AssetLoader.InstanceAsync(World.AssetLoader.VehiclePath("car_hero"), _car3d, "car_hero");
            World.CityScene.TintCar(_car3d, _paint);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnDriver(outfit);
        }

        private async System.Threading.Tasks.Task SpawnDriver(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(_car3d.position, 0.95f, outfit);
            if (g == null || _stage == null) return;
            g.transform.SetParent(_car3d, true);
            g.transform.localPosition = new Vector3(0f, 0.72f, 0.1f);
            g.transform.localRotation = Quaternion.identity;
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
        }

        private void RoadTile(Transform root, int r, int c, Vector3 at, Material asphalt, Material kerb, Material mark)
        {
            const float t = (float)CityDriveArcade.TileM;
            World.ArtLibrary.Box(root, "Road", at + Vector3.down * 0.03f, new Vector3(t, 0.06f, t), asphalt, false);
            var horizontal = CityDriveArcade.IsRoad(r, c - 1) || CityDriveArcade.IsRoad(r, c + 1);
            var vertical = CityDriveArcade.IsRoad(r - 1, c) || CityDriveArcade.IsRoad(r + 1, c);
            if (horizontal && !vertical)
                foreach (var k in new[] { -0.25f, 0.25f }) World.ArtLibrary.Box(root, "Dash", at + new Vector3(k * t, 0.005f, 0f), new Vector3(t * 0.32f, 0.01f, 0.5f), mark, false);
            else if (vertical && !horizontal)
                foreach (var k in new[] { -0.25f, 0.25f }) World.ArtLibrary.Box(root, "Dash", at + new Vector3(0f, 0.005f, k * t), new Vector3(0.5f, 0.01f, t * 0.32f), mark, false);
            // kerbs where the road meets a block (grid rows grow towards −z on the stage, columns towards +x)
            if (!CityDriveArcade.IsRoad(r, c - 1)) World.ArtLibrary.Box(root, "Kerb", at + new Vector3(-t * 0.5f, 0.08f, 0f), new Vector3(0.5f, 0.16f, t), kerb, false);
            if (!CityDriveArcade.IsRoad(r, c + 1)) World.ArtLibrary.Box(root, "Kerb", at + new Vector3(t * 0.5f, 0.08f, 0f), new Vector3(0.5f, 0.16f, t), kerb, false);
            if (!CityDriveArcade.IsRoad(r - 1, c)) World.ArtLibrary.Box(root, "Kerb", at + new Vector3(0f, 0.08f, t * 0.5f), new Vector3(t, 0.16f, 0.5f), kerb, false);
            if (!CityDriveArcade.IsRoad(r + 1, c)) World.ArtLibrary.Box(root, "Kerb", at + new Vector3(0f, 0.08f, -t * 0.5f), new Vector3(t, 0.16f, 0.5f), kerb, false);
        }

        /// <summary>A town house facing the nearest road, two trees in the yard corners and a short fence.</summary>
        private void HouseTile(int r, int c, Vector3 at)
        {
            var yaw = CityDriveArcade.IsRoad(r + 1, c) ? 180f : CityDriveArcade.IsRoad(r - 1, c) ? 0f : CityDriveArcade.IsRoad(r, c - 1) ? -90f : 90f;
            var model = HouseModels[((r * 7 + c * 3) % HouseModels.Length + HouseModels.Length) % HouseModels.Length];
            _stage.PlaceScenery(model, at, yaw, 1.35f);
            _stage.PlaceScenery("tree_round", at + new Vector3(6.8f, 0f, 6.8f), r * 30f, 1.6f);
            _stage.PlaceScenery("tree_round", at + new Vector3(-6.8f, 0f, -6.8f), c * 40f, 1.4f);
            _stage.PlaceScenery("bush_flowers", at + new Vector3(-6.5f, 0f, 6.5f), 0f, 1.2f);
        }

        private void ParkTile(int r, int c, Vector3 at)
        {
            var v = ((r * 5 + c * 11) % 3 + 3) % 3;
            _stage.PlaceScenery(v == 0 ? "tree_round_big" : "tree_round", at + new Vector3(-4.5f, 0f, 3f), r * 20f, 1.8f);
            _stage.PlaceScenery("tree_round", at + new Vector3(5f, 0f, -4f), c * 35f, 1.5f);
            if (v == 1) _stage.PlaceScenery("park_bench", at + new Vector3(2f, 0f, 4f), 180f, 1.2f);
            else _stage.PlaceScenery("flower_bed", at + new Vector3(2f, 0f, 3.5f), 0f, 1.3f);
            _stage.PlaceScenery("bush_flowers", at + new Vector3(-2f, 0f, -5f), 0f, 1.1f);
        }

        private Transform TrafficCar(CityDriveGame.Car car)
        {
            var t = new GameObject("Traffic").transform;
            t.SetParent(_stage.Root.transform, false);
            _ = World.AssetLoader.InstanceAsync(World.AssetLoader.VehiclePath(car.Tint % 2 == 0 ? "car_small" : "car_taxi"), t, "traffic");
            World.CityScene.TintCar(t, TrafficC[car.Tint % TrafficC.Length]);
            return t;
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _clock += dt;
            var g = _game;
            var heading = (float)g.Heading * Mathf.Rad2Deg;
            _car3d.position = W(g.X, g.Z, 0.05f);
            var lean = (float)g.Steer * 4f;
            _car3d.rotation = Quaternion.Euler(0f, heading, -lean);
            var blink = g.CrashCool > 0.0 && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.4f;
            _car3d.gameObject.SetActive(!blink);
            for (var i = 0; i < _coins3d.Count; i++)
            {
                var on = i < g.Coins.Count;
                _coins3d[i].gameObject.SetActive(on);
                if (!on) continue;
                _coins3d[i].position = W(g.Coins[i].x, g.Coins[i].z, 1.4f + Mathf.Sin(_clock * 3f + i) * 0.25f);
                _coins3d[i].rotation = Quaternion.Euler(0f, _clock * 160f + i * 40f, 0f);
            }
            var pulse = 1f + (float)_ringPulse * 0.35f;
            _ring3d.position = W(g.Checkpoint.x, g.Checkpoint.z, 0.4f);
            _ring3d.localScale = Vector3.one * (float)g.Tune.CheckpointRadiusM * pulse * (1f + Mathf.Sin(_clock * 4f) * 0.03f);
            _beam.position = W(g.Checkpoint.x, g.Checkpoint.z, 9f);
            foreach (var car in g.Traffic)
            {
                if (!_traffic3d.TryGetValue(car, out var tr)) _traffic3d[car] = tr = TrafficCar(car);
                var p = CityDriveGame.TrafficPos(car);
                var d = CityDriveGame.TrafficDir(car);
                tr.position = W(p.x, p.z, 0.05f);
                var dir = new Vector3((float)d.x, 0f, (float)-d.z);
                if (dir.sqrMagnitude > 1e-6f) tr.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            // the camera: above and behind, turning with the car (eased) so its nose points up the screen
            var k = _camSet ? 1f - Mathf.Exp(-4f * dt) : 1f;
            _camYaw = Mathf.LerpAngle(_camYaw, heading, k);
            _camSet = true;
            var rot = Quaternion.Euler(0f, _camYaw, 0f);
            var fwd = rot * Vector3.forward;
            var target = _car3d.position + fwd * 6f;
            var cam = _car3d.position - fwd * 11f + Vector3.up * 17f;
            _stage.Camera.transform.position = cam;
            _stage.Camera.transform.rotation = Quaternion.LookRotation(target - cam, Vector3.up);
        }

        private void StageCoin()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("ecstatic");
            if (!ReducedMotion) World.Effects.Sparkle(_car3d.position + Vector3.up * 2.2f, 0.8f, 12);
        }

        private void StageCheckpoint()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("ecstatic");
            gb.Play("wave", 0.1f);
            gb.Play("sit-loop", 0.6f);
            if (!ReducedMotion) World.Effects.Sparkle(_ring3d.position + Vector3.up * 1.5f, 3f, 30);
        }

        private void StageCrash()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("dizzy");
            if (!ReducedMotion) World.Effects.Puff(_car3d.position + Vector3.up * 1f, new Color(0.9f, 0.85f, 0.75f, 0.9f), 14, 0.5f, 3f, 0.6f, Vector3.up, 1f);
        }

        private void DropStage()
        {
            _coins3d.Clear();
            _traffic3d.Clear();
            _camSet = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
