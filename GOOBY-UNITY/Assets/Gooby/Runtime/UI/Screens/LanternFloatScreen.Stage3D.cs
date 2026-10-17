using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D night sky of the Sternenlaterne (port of lantern_float_stage3d.gd): a glowing paper lantern with a flickering
    /// candle carries the player's Gooby in its basket up through real hoops, fireflies and moonlit clouds. Three tinted
    /// star layers drift with the climb, the moon glows with a soft halo, festival lanterns rise in the distance and the
    /// village at the lake — hills, warm windows, the water with the moon's streak — falls away below. Engine units are
    /// metres on the stage (x across, y up); the camera rides up with the lantern and looks slightly up, so the hoops above
    /// read as ellipses and grow as they come. Steering stays the field's horizontal drag.
    /// </summary>
    public sealed partial class LanternFloatScreen
    {
        private sealed class StarLayer { public Transform Root; public float Parallax, Period; }

        private World.MgStage3D _stage;
        private readonly Dictionary<LanternFloatGame.Ring, Transform> _hoops = new Dictionary<LanternFloatGame.Ring, Transform>();
        private readonly Dictionary<LanternFloatGame.Ring, Transform> _clouds3d = new Dictionary<LanternFloatGame.Ring, Transform>();
        private readonly Dictionary<LanternFloatGame.Ring, Transform> _flies = new Dictionary<LanternFloatGame.Ring, Transform>();
        private readonly Stack<Transform> _hoopPool = new Stack<Transform>(), _goldPool = new Stack<Transform>(), _cloudPool = new Stack<Transform>(), _flyPool = new Stack<Transform>();
        private readonly HashSet<LanternFloatGame.Ring> _seen = new HashSet<LanternFloatGame.Ring>();
        private readonly List<LanternFloatGame.Ring> _gone = new List<LanternFloatGame.Ring>();
        private readonly List<StarLayer> _starLayers = new List<StarLayer>();
        private readonly List<Transform> _festival = new List<Transform>();
        private readonly List<Material> _ownedMats = new List<Material>();
        private Transform _rig, _balloon, _skyRoot, _flame;
        private Material _paper;
        private Light _candle;
        private float _windEmit;

        private const float CamDist = 15f, CamPitch = -6f, CamFov = 52f, RigScale = 1.5f;

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 P(double x, double y, float z = 0f) => World.MgStage3D.Origin + new Vector3((float)x, (float)y, z);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#2B2358"));
            _stage.Bind(Field);
            World.MgStudio.Night();
            _stage.Aim(new Vector3(0f, 0f, -CamDist), CamPitch, CamFov);
            var root = _stage.Root.transform;
            BuildVillage(root);
            // the sky that rides with the camera: three star layers wrapping vertically, the moon, far festival lanterns
            _skyRoot = new GameObject("Sky").transform;
            _skyRoot.SetParent(root, false);
            var tints = new[] { new Color(1.25f, 1.2f, 1.05f), new Color(0.95f, 1.05f, 1.35f), new Color(1.3f, 1f, 1.2f) };
            for (var l = 0; l < 3; l++)
            {
                var layer = new StarLayer { Root = new GameObject("Stars" + l).transform, Parallax = 0.12f + l * 0.12f, Period = 26f };
                layer.Root.SetParent(_skyRoot, false);
                var mat = World.ArtLibrary.Glow(tints[l]);
                for (var copy = -1; copy <= 1; copy++)
                {
                    // every copy draws the same stars, so the layer wraps without a seam
                    var rng = new System.Random(31 + l * 7);
                    for (var i = 0; i < 26; i++)
                    {
                        var x = (float)(rng.NextDouble() - 0.5) * (30f + l * 10f);
                        var y = (float)rng.NextDouble() * layer.Period;
                        var star = World.ArtLibrary.Sphere(layer.Root, "Star", Vector3.zero, 0.05f + (float)rng.NextDouble() * 0.08f + l * 0.02f, mat, false).transform;
                        star.localPosition = new Vector3(x, y + copy * layer.Period, 38f - l * 9f);
                    }
                }
                _starLayers.Add(layer);
            }
            // soft nebula glows and far dusk clouds give the night sky depth
            foreach (var (x, y, z, s, c) in new[] { (-5f, 6f, 46f, 9f, new Color(1f, 0.55f, 0.85f, 0.1f)), (6f, -2f, 48f, 8f, new Color(0.45f, 0.85f, 0.95f, 0.09f)), (-2f, -7f, 44f, 7f, new Color(1f, 0.82f, 0.5f, 0.08f)) })
            {
                var neb = World.ArtLibrary.Sphere(_skyRoot, "Nebula", Vector3.zero, 1f, World.ArtLibrary.Transparent(c), false).transform;
                neb.localPosition = new Vector3(x, y, z);
                neb.localScale = new Vector3(s * 2.2f, s * 1.2f, 2f);
            }
            var dusk = World.ArtLibrary.Solid(new Color(0.42f, 0.36f, 0.62f), 0.05f);
            for (var i = 0; i < 6; i++)
            {
                var cloud = new GameObject("DuskCloud").transform;
                cloud.SetParent(_skyRoot, false);
                cloud.localPosition = new Vector3(-10f + i * 4.2f, -6f + (i * 7 % 13), 34f + (i % 3) * 3f);
                foreach (var (dx, dy, rr) in new[] { (-1.2f, 0f, 0.9f), (0f, 0.3f, 1.3f), (1.3f, 0f, 1f) })
                {
                    var puff = World.ArtLibrary.Sphere(cloud, "Puff", Vector3.zero, rr, dusk, false).transform;
                    puff.localPosition = new Vector3(dx, dy, 0f);
                    puff.localScale = new Vector3(rr * 2.2f, rr * 1.1f, rr);
                }
            }
            var moon = World.ArtLibrary.Sphere(_skyRoot, "Moon", Vector3.zero, 1.6f, World.ArtLibrary.Glow(new Color(1.35f, 1.3f, 1.12f)), false).transform;
            moon.localPosition = new Vector3(4.6f, 8.5f, 42f);
            var halo = World.ArtLibrary.Sphere(_skyRoot, "MoonHalo", Vector3.zero, 3.2f, World.ArtLibrary.Transparent(new Color(1f, 0.96f, 0.84f, 0.12f)), false).transform;
            halo.localPosition = new Vector3(4.6f, 8.5f, 43f);
            for (var i = 0; i < 9; i++)
            {
                var f = World.ArtLibrary.Sphere(root, "Festival", Vector3.zero, 0.14f + (i % 3) * 0.04f, World.ArtLibrary.Glow(new Color(1.5f, 0.8f, 0.35f)), false).transform;
                f.position = P(-12f + i * 3f + (i % 2) * 1.3f, -3f + (i * 37 % 11), 22f + (i % 4) * 4f);
                _festival.Add(f);
            }
            BuildLantern(root);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnRider(outfit);
        }

        /// <summary>Down by the lake where the flight starts: hills, cottages with warm windows, the water and its moon streak.</summary>
        private void BuildVillage(Transform root)
        {
            var hillFar = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#5E4E98"), 0.05f);
            var hillNear = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#463A7C"), 0.05f);
            foreach (var (x, z, w, h, mat) in new[] { (-9f, 30f, 14f, 7f, hillFar), (6f, 32f, 16f, 8f, hillFar), (-3f, 24f, 12f, 4.5f, hillNear), (11f, 22f, 10f, 4f, hillNear) })
            {
                var hill = World.ArtLibrary.Sphere(root, "Hill", P(x, -5.5f, z), 1f, mat, false).transform;
                hill.localScale = new Vector3(w, h * 2f, w * 0.5f);
            }
            var window = World.ArtLibrary.Glow(new Color(1.6f, 1.15f, 0.5f));
            foreach (var (x, z, model) in new[] { (-4.5f, 19f, "cottage"), (-1.2f, 21f, "cottage_blue"), (3.5f, 18.5f, "cottage_pink") })
            {
                _stage.PlaceScenery(model, P(x, -4.4f, z), 180f + x * 4f, 1.2f);
                World.ArtLibrary.Box(root, "Window", P(x + 0.4f, -3.4f, z - 1.25f), new Vector3(0.36f, 0.36f, 0.04f), window, false);
            }
            _stage.PlaceScenery("church_tower", P(7f, -4.4f, 24f), 200f, 1.1f);
            var water = World.ArtLibrary.Box(root, "Lake", P(0f, -4.5f, 6f), new Vector3(60f, 0.1f, 24f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#4E4796"), 0.85f), false);
            water.name = "Lake";
            World.ArtLibrary.Box(root, "MoonStreak", P(3.4f, -4.43f, 10f), new Vector3(1.4f, 0.02f, 16f), World.ArtLibrary.Transparent(new Color(1f, 0.95f, 0.8f, 0.3f)), false);
            for (var i = 0; i < 8; i++)
                World.ArtLibrary.Box(root, "Glint", P(-8f + i * 2.3f, -4.42f, 3f + (i * 5 % 9)), new Vector3(0.9f, 0.01f, 0.06f), World.ArtLibrary.Glow(new Color(0.85f, 0.85f, 1.1f)), false);
            foreach (var x in new[] { -3.6f, 3.8f })
                for (var i = 0; i < 4; i++)
                    _stage.Cone(P(x + i * 0.18f, -4.45f, -1f + i * 0.2f), 0.05f, 1.1f + i * 0.2f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#3F3478"), 0.1f));
        }

        private void BuildLantern(Transform root)
        {
            _rig = new GameObject("LanternRig").transform;
            _rig.SetParent(root, false);
            _rig.localScale = Vector3.one * RigScale;
            _balloon = new GameObject("Balloon").transform;
            _balloon.SetParent(_rig, false);
            _paper = new Material(World.ArtLibrary.Glow(new Color(1.25f, 0.78f, 0.4f)));
            _ownedMats.Add(_paper);
            var body = World.ArtLibrary.Sphere(_balloon, "Paper", Vector3.zero, 0.5f, _paper, false).transform;
            body.localPosition = new Vector3(0f, 0.42f, 0f);
            body.localScale = new Vector3(0.66f, 0.82f, 0.66f);
            var rib = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#C8743A"), 0.3f);
            foreach (var y in new[] { 0.12f, 0.72f })
            {
                var band = _stage.Torus(_balloon, "Rib", Vector3.zero, 0.24f, 0.02f, rib);
                band.transform.localPosition = new Vector3(0f, y, 0f);
            }
            _flame = World.ArtLibrary.Sphere(_balloon, "Flame", Vector3.zero, 0.07f, World.ArtLibrary.Glow(new Color(2f, 1.5f, 0.6f)), false).transform;
            _flame.localPosition = new Vector3(0f, 0.12f, 0f);
            var lightGo = new GameObject("Candle");
            lightGo.transform.SetParent(_balloon, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.3f, -0.3f);
            _candle = lightGo.AddComponent<Light>();
            _candle.type = LightType.Point;
            _candle.color = new Color(1f, 0.7f, 0.4f);
            _candle.range = 3.2f;
            _candle.intensity = 1.6f;
            var cord = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8E623F"), 0.3f);
            foreach (var s in new[] { -1f, 1f })
            {
                var c = World.ArtLibrary.Cylinder(_rig, "Cord", Vector3.zero, 0.01f, 0.36f, cord, false).transform;
                c.localPosition = new Vector3(s * 0.17f, -0.1f, 0f);
                c.localRotation = Quaternion.Euler(0f, 0f, s * 12f);
            }
            var basket = World.ArtLibrary.Cylinder(_rig, "Basket", Vector3.zero, 0.22f, 0.18f, World.ArtLibrary.Pbr("wood_planks", 0.4f, World.ArtLibrary.Hex("#B07B45"), 0.2f)).transform;
            basket.localPosition = new Vector3(0f, -0.34f, 0f);
        }

        private async System.Threading.Tasks.Task SpawnRider(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(_rig.position, 0.5f * RigScale, outfit);
            if (g == null || _stage == null) return;
            g.transform.SetParent(_rig, true);
            g.transform.localPosition = new Vector3(0f, -0.4f, -0.02f);
            g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
        }

        private Transform NewHoop(bool gold)
        {
            var pool = gold ? _goldPool : _hoopPool;
            if (pool.Count > 0) { var t = pool.Pop(); t.gameObject.SetActive(true); return t; }
            var r = (float)_game.Tune.RingRadius * 1.25f + 0.3f;
            var hoop = _stage.Torus(null, gold ? "GoldHoop" : "Hoop", Vector3.zero, r, gold ? 0.075f : 0.055f, World.ArtLibrary.Glow(gold ? new Color(1.5f, 1.15f, 0.42f) : new Color(0.85f, 1.1f, 1.35f))).transform;
            var glow = _stage.Torus(hoop, "Glow", hoop.position, r * 1.06f, 0.14f, World.ArtLibrary.Transparent(gold ? new Color(1f, 0.82f, 0.35f, 0.18f) : new Color(0.72f, 0.9f, 1f, 0.16f)));
            glow.transform.localScale = Vector3.one * 1.06f;
            return hoop;
        }

        private Transform NewCloud()
        {
            if (_cloudPool.Count > 0) { var t = _cloudPool.Pop(); t.gameObject.SetActive(true); return t; }
            var c = new GameObject("Cloud").transform;
            c.SetParent(_stage.Root.transform, false);
            var mat = World.ArtLibrary.Solid(new Color(0.84f, 0.84f, 0.95f), 0.1f);
            foreach (var (dx, dy, r) in new[] { (-0.55f, 0f, 0.42f), (0f, 0.12f, 0.55f), (0.55f, 0.02f, 0.4f), (0.25f, -0.15f, 0.36f), (-0.25f, -0.12f, 0.34f) })
            {
                var puff = World.ArtLibrary.Sphere(c, "Puff", Vector3.zero, r, mat, false).transform;
                puff.localPosition = new Vector3(dx, dy, 0f);
                puff.localScale = new Vector3(r * 2.1f, r * 1.6f, r * 1.8f);
            }
            return c;
        }

        private Transform NewFly()
        {
            if (_flyPool.Count > 0) { var t = _flyPool.Pop(); t.gameObject.SetActive(true); return t; }
            var f = World.ArtLibrary.Sphere(_stage.Root.transform, "Firefly", Vector3.zero, 0.07f, World.ArtLibrary.Glow(new Color(1.2f, 1.6f, 0.6f)), false).transform;
            World.ArtLibrary.Sphere(f, "Halo", f.position, 0.22f, World.ArtLibrary.Transparent(new Color(0.75f, 1f, 0.45f, 0.18f)), false);
            return f;
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            var travel = (float)g.Travel;
            // the camera rides up with the lantern; the sky wraps around it
            _stage.Camera.transform.position = P(0f, travel, -CamDist);
            _skyRoot.position = P(0f, travel, 0f);
            foreach (var layer in _starLayers)
                layer.Root.localPosition = new Vector3(0f, -Mathf.Repeat(travel * layer.Parallax, layer.Period), 0f);
            for (var i = 0; i < _festival.Count; i++)
            {
                var f = _festival[i];
                var y = f.position.y - World.MgStage3D.Origin.y + dt * (0.35f + (i % 3) * 0.1f);
                if (y < travel - 9f) y += 22f;
                if (y > travel + 13f) y -= 22f;
                f.position = new Vector3(f.position.x, World.MgStage3D.Origin.y + y, f.position.z);
            }
            // the lantern: bobbing, flickering, flashing red after a cloud bump, trembling in the shake
            var shake = ReducedMotion ? 0f : (Mathf.PerlinNoise(_show * 18f, 0.3f) - 0.5f) * 0.3f * _shake;
            var bob = ReducedMotion ? 0f : Mathf.Sin(_show * 2.4f) * 0.05f;
            _rig.position = P(g.LanternX + shake, travel + bob, 0f);
            var steer = (float)(g.SteerTarget - g.LanternX);
            _rig.rotation = Quaternion.Euler(0f, 0f, Mathf.Clamp(-steer * 6f, -14f, 14f));
            var flicker = ReducedMotion ? 1f : 0.85f + 0.15f * Mathf.PerlinNoise(_show * 9f, 1.7f);
            var hot = _bumpFlash > 0f ? new Color(1.6f, 0.45f, 0.35f) : new Color(1.25f, 0.78f, 0.4f) * (flicker + _glow * 0.6f);
            hot.a = 1f;
            _paper.color = hot;
            if (_paper.HasProperty("_BaseColor")) _paper.SetColor("_BaseColor", hot);
            if (_paper.HasProperty("_EmissionColor")) _paper.SetColor("_EmissionColor", hot);
            _candle.intensity = 1.4f * flicker + _glow * 1.5f;
            _flame.localScale = Vector3.one * 0.14f * flicker;
            _balloon.localScale = Vector3.one * (_bumpFlash > 0f ? 1.08f : 1f);
            // hoops, clouds and fireflies of the rings ahead
            _seen.Clear();
            foreach (var ring in g.Rings)
            {
                _seen.Add(ring);
                var inView = ring.Y - travel < 13.0 && ring.Y - travel > -9.0;
                if (!ring.Done && inView)
                {
                    if (!_hoops.TryGetValue(ring, out var hoop)) _hoops[ring] = hoop = NewHoop(ring.Gold);
                    hoop.position = P(ring.X, ring.Y, 0f);
                    hoop.rotation = Quaternion.Euler(0f, _show * (ring.Gold ? 40f : 15f), 0f);
                }
                else if (_hoops.TryGetValue(ring, out var done)) { Retire(done, ring.Gold ? _goldPool : _hoopPool); _hoops.Remove(ring); }
                var cloudOn = ring.Cloud && !(ring.CloudDone && ring.Y < g.Travel) && inView;
                if (cloudOn)
                {
                    if (!_clouds3d.TryGetValue(ring, out var cloud)) _clouds3d[ring] = cloud = NewCloud();
                    cloud.position = P(ring.CloudX + (ReducedMotion ? 0f : Mathf.Sin(_show * 0.6f + ring.Index) * 0.08f), ring.Y - 0.8, 0.1f);
                }
                else if (_clouds3d.TryGetValue(ring, out var gone)) { Retire(gone, _cloudPool); _clouds3d.Remove(ring); }
                var flyOn = ring.Firefly && !ring.FireflyDone && inView;
                if (flyOn)
                {
                    if (!_flies.TryGetValue(ring, out var fly)) _flies[ring] = fly = NewFly();
                    var pulse = 0.55f + 0.45f * Mathf.Sin(_show * 8f + ring.Index);
                    fly.position = P(ring.FireflyX + Mathf.Sin(_show * 3f + ring.Index) * 0.06f, ring.Y - LanternFloatGame.FireflyLead + Mathf.Cos(_show * 2.4f) * 0.06f, -0.05f);
                    fly.localScale = Vector3.one * (0.11f + 0.05f * pulse);
                }
                else if (_flies.TryGetValue(ring, out var caught)) { Retire(caught, _flyPool); _flies.Remove(ring); }
            }
            Sweep(_hoops, null);
            Sweep(_clouds3d, _cloudPool);
            Sweep(_flies, _flyPool);
            // gust streaks across the frame
            var (gust, phase) = g.Gust;
            if (phase != "idle" && g.IntroLeft <= 0.0 && !ReducedMotion)
            {
                _windEmit -= dt;
                if (_windEmit <= 0f)
                {
                    _windEmit = phase == "telegraph" ? 0.12f : 0.05f;
                    var from = P(-gust.Dir * 4f, travel + (Random.value * 2f - 0.5f) * 5f, -1f);
                    World.Effects.Puff(from, phase == "telegraph" ? new Color(0.8f, 0.92f, 1f, 0.3f) : new Color(0.8f, 0.92f, 1f, 0.6f), 2, 0.1f, 6f, 1.2f, new Vector3(gust.Dir, 0f, 0f), 0.05f);
                }
            }
        }

        private void Sweep(Dictionary<LanternFloatGame.Ring, Transform> map, Stack<Transform> pool)
        {
            _gone.Clear();
            foreach (var kv in map) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var r in _gone)
            {
                Retire(map[r], pool ?? (r.Gold ? _goldPool : _hoopPool));
                map.Remove(r);
            }
        }

        private static void Retire(Transform t, Stack<Transform> pool)
        {
            if (t == null) return;
            t.gameObject.SetActive(false);
            pool.Push(t);
        }

        private void StageRing(bool gold)
        {
            if (_stage == null) return;
            var at = _rig.position + Vector3.up * 0.4f;
            if (!ReducedMotion) World.Effects.Sparkle(at, gold ? 0.8f : 0.5f, gold ? 26 : 14);
            var gb = _stage.Gooby;
            if (gb == null) return;
            gb.SetEmotion(gold ? "ecstatic" : "happy");
        }

        private void StageFirefly()
        {
            if (_stage == null || ReducedMotion) return;
            World.Effects.Sparkle(_rig.position + Vector3.up * 0.2f, 0.35f, 10);
        }

        private void StageBump()
        {
            _stage?.Gooby?.SetEmotion("dizzy");
            if (_stage != null && !ReducedMotion) World.Effects.Puff(_rig.position + Vector3.up * 0.6f, new Color(0.85f, 0.85f, 0.95f, 0.9f), 10, 0.2f, 1.2f, 0.6f, Vector3.up, 1f);
        }

        private void DropStage()
        {
            if (_stage != null) World.MgStudio.Day();
            _hoops.Clear(); _clouds3d.Clear(); _flies.Clear();
            _hoopPool.Clear(); _goldPool.Clear(); _cloudPool.Clear(); _flyPool.Clear();
            _seen.Clear(); _gone.Clear(); _starLayers.Clear(); _festival.Clear();
            _stage?.Destroy();
            _stage = null;
            foreach (var m in _ownedMats) if (m != null) Object.Destroy(m);
            _ownedMats.Clear();
        }
    }
}
