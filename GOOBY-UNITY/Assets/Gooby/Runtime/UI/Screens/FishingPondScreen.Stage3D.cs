using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D pond of the Angelteich (port of fishing_pond_scenery.gd + fishing_pond.gd): a cut-away pond under a summer
    /// sky — hills and round trees on the far shore, reeds on the banks, lily pads and two ducks on the surface, the water
    /// deepening to a pebble bed with swaying weed and stones. Real fish swim where the engine has them (rare ones in a
    /// golden glow), a boot lurks, the player's Gooby sits on the jetty at the right with a real rod: the line runs to the red
    /// bobber and on down to the hook, everything he lands arcs up into his bucket. The default camera frames the field, so
    /// the 2D aim ring and the reel card still sit on the hook.
    /// </summary>
    public sealed partial class FishingPondScreen
    {
        private sealed class Flight3D { public Transform Fish; public Vector3 From; public float T; }

        private World.MgStage3D _stage;
        private readonly Dictionary<FishingPondGame.Swimmer, Transform> _fish3d = new Dictionary<FishingPondGame.Swimmer, Transform>();
        private readonly HashSet<FishingPondGame.Swimmer> _liveFish = new HashSet<FishingPondGame.Swimmer>();
        private readonly List<FishingPondGame.Swimmer> _goneFish = new List<FishingPondGame.Swimmer>();
        private readonly List<Flight3D> _flights3d = new List<Flight3D>();
        private readonly List<Transform> _weeds = new List<Transform>();
        private readonly List<Transform> _ducks = new List<Transform>();
        private Transform _rodTip, _bobber, _hook, _lineTop, _lineDown, _bucket3d, _hooked3d;
        private FishingPondGame.Swimmer _hookedFor;

        protected override bool StageHasGooby => _stage != null;

        private Vector3 F(float fx, float fy, float depth = 0f) =>
            _stage.ViewToWorld(fx * ViewW / Mathf.Max(1f, FieldW), fy * ViewH / Mathf.Max(1f, FieldH), depth);

        private float MetresPerFieldPx => (float)(ViewW / Mathf.Max(1f, FieldW)) * World.MgStage3D.MetresPerPx;

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.68f, 0.85f, 0.98f));
            _stage.Bind(Field);
            var root = _stage.Root.transform;
            var surface = F(0f, SurfaceY).y;
            var bottom = F(0f, FieldH).y;
            var o = World.MgStage3D.Origin;
            // the far shore: meadow, hills, trees; reeds and stones on the near banks
            World.ArtLibrary.Box(root, "Shore", new Vector3(o.x, surface - 0.05f, o.z + 14f), new Vector3(40f, 0.1f, 20f), World.ArtLibrary.PbrSlab("grass", new Vector2(40f, 20f), 2.4f, World.ArtLibrary.Hex("#A6DB98"), 0.04f, 0.6f), false);
            _stage.Hills(surface, 22f, 7, new Color(0.62f, 0.82f, 0.62f), 4f, 40f, 0f, 51);
            _stage.Row("tree_round", surface, 9f, -6f, 6f, 5, 1.4f, 0f, 0.8f, 52);
            _stage.Clouds(4, surface + 5f, 26f, 20f, 1.4f, 53);
            _stage.Sun(new Vector3(-3.2f, surface + 6f, 30f), 1.2f);
            // the water body: a translucent front pane over the depth, the bed of pebbles
            // the fish swim between a deep back wall and a light front pane (an opaque body would hide them)
            World.ArtLibrary.Box(root, "WaterBack", new Vector3(o.x, (surface + bottom) * 0.5f, o.z + 3.2f), new Vector3(30f, surface - bottom, 0.1f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#3F8DB4"), 0.3f), false);
            World.ArtLibrary.Box(root, "WaterFront", new Vector3(o.x, (surface + bottom) * 0.5f, o.z - 0.4f), new Vector3(30f, surface - bottom, 0.02f), World.ArtLibrary.Transparent(new Color(0.45f, 0.78f, 0.92f, 0.16f)), false);
            World.ArtLibrary.Box(root, "WaterTop", new Vector3(o.x, surface - 0.35f, o.z - 0.42f), new Vector3(30f, 0.7f, 0.02f), World.ArtLibrary.Transparent(new Color(0.75f, 0.92f, 1f, 0.16f)), false);
            for (var i = 0; i < 4; i++)
            {
                var ray = World.ArtLibrary.Box(root, "SunRay", new Vector3(o.x - 1.6f + i * 1.1f, (surface + bottom) * 0.5f + 0.4f, o.z + 2f), new Vector3(0.35f, surface - bottom, 0.02f), World.ArtLibrary.Transparent(new Color(0.85f, 0.97f, 1f, 0.07f)), false).transform;
                ray.rotation = Quaternion.Euler(0f, 0f, -12f + i * 3f);
            }
            World.ArtLibrary.Box(root, "Surface", new Vector3(o.x, surface + 0.01f, o.z + 1.5f), new Vector3(30f, 0.02f, 4.2f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8BD0E6"), 0.92f), false);
            var bed = F(0f, Y(_game.DepthSpan)).y;
            World.ArtLibrary.Box(root, "Bed", new Vector3(o.x, bed - 0.3f, o.z + 1.5f), new Vector3(30f, 0.6f, 4.2f), World.ArtLibrary.PbrSlab("concrete", new Vector2(30f, 4.2f), 0.8f, World.ArtLibrary.Hex("#C9B79A"), 0.1f, 2f), false);
            var rng = new System.Random(7);
            for (var i = 0; i < 14; i++)
            {
                var x = (float)(rng.NextDouble() - 0.5) * 4.4f;
                _stage.Prop(i % 2 == 0 ? "rock_smallA" : "rock_smallFlatA", new Vector3(o.x + x, bed, o.z + 0.6f + (float)rng.NextDouble() * 1.4f), 1.2f + (float)rng.NextDouble(), i * 50f);
            }
            var weedMat = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#5FA866"), 0.2f);
            for (var i = 0; i < 9; i++)
            {
                var x = -2.1f + i * 0.5f + (float)(rng.NextDouble() - 0.5) * 0.2f;
                var weed = _stage.Cone(new Vector3(o.x + x, bed, o.z + 0.4f + (i % 3) * 0.5f), 0.07f, 0.8f + (float)rng.NextDouble() * 0.9f, weedMat).transform;
                _weeds.Add(weed);
            }
            var reed = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#6FA866"), 0.2f);
            var cattail = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8E623F"), 0.3f);
            foreach (var x in new[] { -1.85f, -1.6f, -1.45f })
            {
                var stem = World.ArtLibrary.Cylinder(root, "Reed", new Vector3(o.x + x, surface + 0.45f, o.z + 0.1f), 0.015f, 0.9f, reed, false).transform;
                stem.rotation = Quaternion.Euler(0f, 0f, x * 4f);
                World.ArtLibrary.Cylinder(root, "Cattail", new Vector3(o.x + x, surface + 0.85f, o.z + 0.1f), 0.04f, 0.18f, cattail, false);
            }
            // lily pads with a bloom, two ducks
            var pad = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#7CC486"), 0.3f);
            for (var i = 0; i < 3; i++)
            {
                var at = F(FieldW * (0.12f + i * 0.36f), SurfaceY, 0.3f + i * 0.4f);
                var lily = World.RanchDressing.Ring(root, "Lily", 0f, 0.28f, pad);
                lily.position = at + Vector3.up * 0.02f;
                if (i == 1) World.ArtLibrary.Sphere(root, "Bloom", at + Vector3.up * 0.08f, 0.07f, World.ArtLibrary.Solid(new Color(1f, 0.8f, 0.86f), 0.4f), false);
            }
            for (var i = 0; i < 2; i++) _ducks.Add(Duck(root, F(FieldW * (0.14f + i * 0.62f), SurfaceY, 0.6f + i * 0.5f)));
            BuildJetty(root, surface, bottom);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnAngler(outfit);
        }

        private Transform Duck(Transform root, Vector3 at)
        {
            var duck = new GameObject("Duck").transform;
            duck.SetParent(root, false);
            duck.position = at;
            var white = World.ArtLibrary.Solid(new Color(1f, 0.97f, 0.88f), 0.4f);
            var body = World.ArtLibrary.Sphere(duck, "Body", Vector3.zero, 0.5f, white).transform;
            body.localPosition = new Vector3(0f, 0.08f, 0f);
            body.localScale = new Vector3(0.32f, 0.2f, 0.22f);
            var head = World.ArtLibrary.Sphere(duck, "Head", Vector3.zero, 0.5f, white).transform;
            head.localPosition = new Vector3(0.13f, 0.24f, 0f);
            head.localScale = Vector3.one * 0.14f;
            var beak = World.ArtLibrary.Box(duck, "Beak", Vector3.zero, new Vector3(0.08f, 0.03f, 0.05f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFA14A"), 0.4f), false).transform;
            beak.localPosition = new Vector3(0.22f, 0.23f, 0f);
            return duck;
        }

        /// <summary>The jetty at the right with its posts in the water and the bucket; the rod, line, bobber and hook.</summary>
        private void BuildJetty(Transform root, float surface, float bottom)
        {
            var plank = World.ArtLibrary.Pbr("wood_planks", 0.5f, World.ArtLibrary.Hex("#D9A77A"), 0.25f);
            var jettyTop = F(FieldW, SurfaceY - 28f).y;
            var left = F(FieldW * 0.58f, 0f).x;
            var right = F(FieldW, 0f).x + 0.3f;
            World.ArtLibrary.Box(root, "Jetty", new Vector3((left + right) * 0.5f, jettyTop - 0.08f, World.MgStage3D.Origin.z + 0.5f), new Vector3(right - left, 0.16f, 1.6f), plank);
            foreach (var x in new[] { left + 0.18f, right - 0.4f })
                World.ArtLibrary.Cylinder(root, "Post", new Vector3(x, (jettyTop + bottom) * 0.5f, World.MgStage3D.Origin.z), 0.08f, jettyTop - bottom, World.ArtLibrary.Pbr("wood_planks", 0.5f, World.ArtLibrary.Hex("#A87A52"), 0.2f));
            _bucket3d = World.ArtLibrary.Cylinder(root, "Bucket", new Vector3(left + 0.35f, jettyTop + 0.17f, World.MgStage3D.Origin.z + 0.2f), 0.16f, 0.34f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8DA2B8"), 0.6f)).transform;
            var line = World.ArtLibrary.Solid(new Color(0.98f, 0.98f, 0.92f), 0.3f);
            _rodTip = new GameObject("RodTip").transform;
            _rodTip.SetParent(root, false);
            var rod = World.ArtLibrary.Cylinder(root, "Rod", Vector3.zero, 0.02f, 1f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8E5432"), 0.4f), false).transform;
            rod.name = "Rod";
            rod.SetParent(_rodTip, true);
            _lineTop = World.ArtLibrary.Cylinder(root, "Line", Vector3.zero, 0.006f, 1f, line, false).transform;
            _lineDown = World.ArtLibrary.Cylinder(root, "Line", Vector3.zero, 0.006f, 1f, line, false).transform;
            _bobber = new GameObject("Bobber").transform;
            _bobber.SetParent(root, false);
            World.ArtLibrary.Sphere(_bobber, "Red", Vector3.zero, 0.06f, World.ArtLibrary.Solid(new Color(0.9f, 0.25f, 0.2f), 0.5f), false).transform.localPosition = new Vector3(0f, 0.03f, 0f);
            World.ArtLibrary.Sphere(_bobber, "White", Vector3.zero, 0.055f, World.ArtLibrary.Solid(Color.white, 0.5f), false).transform.localPosition = new Vector3(0f, -0.03f, 0f);
            _hook = _stage.Torus(null, "Hook", Vector3.zero, 0.05f, 0.012f, World.ArtLibrary.Solid(new Color(0.85f, 0.87f, 0.9f), 0.8f)).transform;
        }

        private async System.Threading.Tasks.Task SpawnAngler(Dictionary<string, string> outfit)
        {
            var jetty = JettyTop();
            var at = F(FieldW - FieldW * 0.16f, jetty.y, 0.2f);
            var g = await _stage.SpawnGoobyAsync(at, 0.62f, outfit);
            if (g == null || _stage == null) return;
            g.transform.rotation = Quaternion.Euler(0f, 220f, 0f);
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
        }

        private Transform NewFish(FishingPondGame.Swimmer f)
        {
            if (f.IsBoot)
            {
                var boot = new GameObject("Boot").transform;
                boot.SetParent(_stage.Root.transform, false);
                var leather = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#4F3F38"), 0.3f);
                World.ArtLibrary.Box(boot, "Shaft", Vector3.zero, new Vector3(0.14f, 0.28f, 0.14f), leather).transform.localPosition = new Vector3(-0.04f, 0.14f, 0f);
                World.ArtLibrary.Box(boot, "Foot", Vector3.zero, new Vector3(0.26f, 0.1f, 0.14f), leather).transform.localPosition = new Vector3(0.04f, 0.02f, 0f);
                World.ArtLibrary.Box(boot, "Sole", Vector3.zero, new Vector3(0.27f, 0.03f, 0.15f), World.ArtLibrary.Solid(new Color(0.2f, 0.17f, 0.16f), 0.2f), false).transform.localPosition = new Vector3(0.04f, -0.035f, 0f);
                return boot;
            }
            var len = (float)(f.Scale + 0.25) * PpuX * MetresPerFieldPx;
            var t = new GameObject("Fish").transform;
            t.SetParent(_stage.Root.transform, false);
            var model = _stage.Prop("fish", Vector3.zero, len / 0.62f);
            model.SetParent(t, false);
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            if (f.Rare) World.ArtLibrary.Sphere(t, "Glow", Vector3.zero, 0.5f * len, World.ArtLibrary.Transparent(new Color(1f, 0.85f, 0.4f, 0.28f)), false).transform.localPosition = Vector3.zero;
            return t;
        }

        private void PoseFish(Transform t, FishingPondGame.Swimmer f, Vector3 at, double dir, float tilt)
        {
            t.position = at;
            var yaw = dir >= 0 ? 90f : -90f;
            t.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f) * Quaternion.Euler(0f, Mathf.Sin((float)f.Wiggle) * 14f, 0f);
        }

        private void RenderStage(float shakeX)
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            _liveFish.Clear();
            foreach (var f in g.Fish)
            {
                if (f == g.Hooked) continue;
                _liveFish.Add(f);
                if (!_fish3d.TryGetValue(f, out var t)) _fish3d[f] = t = NewFish(f);
                var p = At(f.X + shakeX / PpuX, f.Depth + System.Math.Sin(f.Wiggle) * 0.03);
                PoseFish(t, f, F(p.x, p.y, 0.15f), f.Dir, 0f);
            }
            _goneFish.Clear();
            foreach (var kv in _fish3d) if (!_liveFish.Contains(kv.Key)) _goneFish.Add(kv.Key);
            foreach (var d in _goneFish)
            {
                var t = _fish3d[d];
                _fish3d.Remove(d);
                if (d == g.Hooked) { _hooked3d = t; _hookedFor = d; continue; }
                if (t != null) Object.Destroy(t.gameObject);
            }
            // the hooked catch dangles under the hook
            var hook = At(g.Tune.HookX, g.HookDepth);
            hook.x += shakeX;
            var hookW = F(hook.x, hook.y, 0f);
            if (g.Hooked != null)
            {
                if (_hooked3d == null || _hookedFor != g.Hooked) { _hooked3d = NewFish(g.Hooked); _hookedFor = g.Hooked; }
                PoseFish(_hooked3d, g.Hooked, hookW + Vector3.down * 0.16f * PpuY * MetresPerFieldPx, 1.0, -70f + Mathf.Sin(_show * 18f) * 20f);
            }
            else if (_hooked3d != null && _hookedFor != null && g.Hooked == null)
            {
                // landed or lost: the flight (if any) owns it now
                Object.Destroy(_hooked3d.gameObject);
                _hooked3d = null;
                _hookedFor = null;
            }
            // rod, line, bobber and hook
            var tip = RodTip();
            var tipW = F(tip.x, tip.y, 0f);
            var grip = F(FieldW - FieldW * 0.2f, JettyTop().y - FieldW * 0.1f, 0.1f);
            var rod = _rodTip.GetChild(0);
            Stretch(rod, grip, tipW, 0.02f);
            var bob = new Vector2(X(g.Tune.HookX) + shakeX, SurfaceY + Mathf.Sin(_show * 2.4f) * 2f);
            if (g.Phase == FishingPondGame.PhaseReel) { bob.y += 6f + Mathf.Sin(_show * 26f) * 3f; bob.x += Mathf.Sin(_show * 31f) * 2f; }
            var bobW = F(bob.x, bob.y, 0f);
            _bobber.position = bobW;
            Stretch(_lineTop, tipW, bobW, 0.006f);
            Stretch(_lineDown, bobW, hookW + Vector3.up * 0.04f, 0.006f);
            _hook.position = hookW;
            _hook.rotation = Quaternion.Euler(90f, 0f, 0f);
            // weed and ducks sway; landed catches arc into the bucket
            for (var i = 0; i < _weeds.Count; i++) _weeds[i].rotation = Quaternion.Euler(0f, 0f, ReducedMotion ? 0f : Mathf.Sin(_show * 1.3f + i) * 9f);
            for (var i = 0; i < _ducks.Count; i++)
            {
                var d = _ducks[i];
                var x = FieldW * (0.14f + i * 0.62f) + Mathf.Sin(_show * 0.4f + i * 2f) * 14f;
                d.position = F(x, SurfaceY + Mathf.Sin(_show * 2f + i) * 2f, 0.6f + i * 0.5f);
                d.rotation = Quaternion.Euler(0f, Mathf.Cos(_show * 0.4f + i * 2f) >= 0f ? 0f : 180f, 0f);
            }
            for (var i = _flights3d.Count - 1; i >= 0; i--)
            {
                var fl = _flights3d[i];
                fl.T += dt / 0.8f;
                if (fl.T >= 1f)
                {
                    if (fl.Fish != null) Object.Destroy(fl.Fish.gameObject);
                    _flights3d.RemoveAt(i);
                    if (!ReducedMotion) World.Effects.Sparkle(_bucket3d.position + Vector3.up * 0.2f, 0.15f, 8);
                    continue;
                }
                var to = _bucket3d.position + Vector3.up * 0.2f;
                fl.Fish.position = Vector3.Lerp(fl.From, to, fl.T) + Vector3.up * Mathf.Sin(fl.T * Mathf.PI) * 0.9f;
                fl.Fish.rotation = Quaternion.Euler(fl.T * 540f, 90f, 0f);
                fl.Fish.localScale = Vector3.one * (1f - fl.T * 0.4f);
            }
            var gb = _stage.Gooby;
            if (gb != null)
            {
                var lean = g.Phase == FishingPondGame.PhaseLower ? -10f : g.Phase == FishingPondGame.PhaseReel ? Mathf.Sin(_show * 30f) * 8f : -3f;
                gb.transform.rotation = Quaternion.Euler(0f, 220f, lean);
            }
        }

        /// <summary>A unit cylinder stretched from a to b with the given radius (rod and line pieces).</summary>
        private static void Stretch(Transform t, Vector3 a, Vector3 b, float radius)
        {
            var d = b - a;
            t.position = (a + b) * 0.5f;
            t.rotation = d.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.up, d.normalized) : Quaternion.identity;
            t.localScale = new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f);
        }

        private void StageLanded(FishingPondGame.Swimmer f, int delta)
        {
            if (_stage == null) return;
            var gb = _stage.Gooby;
            if (gb != null)
            {
                gb.SetEmotion(f.IsBoot ? "sad" : f.Rare ? "ecstatic" : "happy");
                if (!f.IsBoot) { gb.Play("celebrate", 0.1f); gb.Play("sit-loop", 0.6f); }
            }
            var fish = NewFish(f);
            fish.position = _hooked3d != null ? _hooked3d.position : F(X(_game.Tune.HookX), SurfaceY, 0f);
            _flights3d.Add(new Flight3D { Fish = fish, From = fish.position, T = 0f });
        }

        private void StageMood(string emotion) => _stage?.Gooby?.SetEmotion(emotion);

        /// <summary>Over the 3D pond: the catch ring round the hook while aiming, the sparks and the gold glow of a rare set.</summary>
        private void PaintOverlay3D(UnityEngine.UIElements.Painter2D p, float shakeX)
        {
            var hook = At(_game.Tune.HookX, _game.HookDepth);
            hook.x += shakeX;
            if (_game.Phase == FishingPondGame.PhaseIdle || _game.Phase == FishingPondGame.PhaseLower)
            {
                p.strokeColor = new Color(Rim.r, Rim.g, Rim.b, 0.55f); p.lineWidth = 2.5f;
                EllipseStroke(p, hook, (float)_game.Tune.CatchRadius * PpuX, (float)_game.Tune.CatchRadius * PpuY);
            }
            foreach (var sp in _sparks)
            {
                var c = sp.C; c.a = Mathf.Clamp01(sp.Life / sp.Max);
                Circle(p, sp.P, sp.Size, c);
            }
            if (_glow > 0f) FillRect(p, 0f, 0f, FieldW, FieldH, new Color(1f, 0.9f, 0.5f, 0.2f * Mathf.Clamp01(_glow)));
        }

        private void DropStage()
        {
            foreach (var fl in _flights3d) if (fl.Fish != null) Object.Destroy(fl.Fish.gameObject);
            _flights3d.Clear(); _fish3d.Clear(); _liveFish.Clear(); _goneFish.Clear(); _weeds.Clear(); _ducks.Clear();
            _hooked3d = null;
            _hookedFor = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
