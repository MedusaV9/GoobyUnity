using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D breakfast kitchen of the pancake tower (port of pancake_tower_stage3d.gd): golden pancakes are thick discs
    /// stacking on a plate on the wooden worktop in front of a tiled backsplash, a window and shelves of baked treats; the
    /// tower sways as one around its base, cut-off crumbs tumble, and the player's Gooby rides the swinging pancake —
    /// down with it when it drops, and with a hop back up onto the next one. The kitchen slides down as the camera rides
    /// up with the tower (the 2D mapping, so taps and engine stay as they are).
    /// </summary>
    public sealed partial class PancakeTowerScreen
    {
        private World.MgStage3D _stage;
        private Transform _room3d, _plate3d, _active3d, _activeTop;
        private readonly List<Transform> _layers3d = new List<Transform>();
        private readonly Dictionary<PancakeTowerGame.Crumb, Transform> _crumbs3d = new Dictionary<PancakeTowerGame.Crumb, Transform>();
        private Material _cakeMat, _cakeTopMat, _goldMat, _berryMat;
        private Vector3 _goobyAt;
        private bool _goobyPlaced;
        private float _emoteLeft;

        protected override bool StageHasGooby => _stage != null;

        /// <summary>Metres per engine unit on the stage plane (the view is 2.9 units wide).</summary>
        private float StageUnit => (float)(ViewSize.w * World.MgStage3D.MetresPerPx / ViewUnitsX);

        /// <summary>Engine point (units, camera ride included) → stage world, through the 2D mapping.</summary>
        private Vector3 StagePoint(double x, double y, float depth = 0f) =>
            _stage.ViewToWorld(ScreenX(x) * ViewSize.w / FieldW, ScreenY(y) * ViewSize.h / FieldH, depth);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewSize.w, ViewSize.h, new Color(0.96f, 0.86f, 0.74f));
            _stage.Bind(Field);
            _cakeMat = World.ArtLibrary.Solid(new Color(0.88f, 0.6f, 0.25f), 0.25f);
            _cakeTopMat = World.ArtLibrary.Solid(new Color(0.96f, 0.74f, 0.4f), 0.3f);
            _goldMat = World.ArtLibrary.Solid(new Color(1f, 0.85f, 0.36f), 0.55f);
            _berryMat = World.ArtLibrary.Solid(new Color(0.93f, 0.36f, 0.48f), 0.45f);
            _room3d = new GameObject("Kitchen").transform;
            _room3d.SetParent(_stage.Root.transform, false);
            _room3d.position = World.MgStage3D.Origin;
            BuildKitchen();
            _plate3d = new GameObject("Plate").transform;
            _plate3d.SetParent(_stage.Root.transform, false);
            var plateMat = World.ArtLibrary.Solid(new Color(0.93f, 0.95f, 0.99f), 0.6f);
            Child(World.ArtLibrary.Cylinder(_plate3d, "Plate", Vector3.zero, 0.92f, 0.08f, plateMat), _plate3d, new Vector3(0f, -0.04f, 0f));
            Child(World.ArtLibrary.Cylinder(_plate3d, "Rim", Vector3.zero, 0.98f, 0.03f, World.ArtLibrary.Solid(new Color(0.7f, 0.8f, 0.95f), 0.6f), false), _plate3d, new Vector3(0f, -0.01f, 0f));
            _active3d = SpawnPancake("Active");
            _activeTop = new GameObject("Top").transform;
            _activeTop.SetParent(_active3d, false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnRider(outfit);
        }

        private async System.Threading.Tasks.Task SpawnRider(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(0f, 4f, 0f), 0.78f, outfit);
            if (g == null || _stage == null) return;
            g.Play("sit-loop");
        }

        private static void Child(GameObject go, Transform parent, Vector3 local)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
        }

        /// <summary>Wall with wallpaper stripes, tiled backsplash, two windows, shelves with treats, bunting, clock, worktop.</summary>
        private void BuildKitchen()
        {
            var r = _room3d;
            var o = World.MgStage3D.Origin;
            var wall = World.ArtLibrary.Solid(new Color(0.98f, 0.86f, 0.74f), 0.05f);
            var stripe = World.ArtLibrary.Solid(new Color(0.95f, 0.78f, 0.66f), 0.05f);
            Child(World.ArtLibrary.Box(r, "Wall", o, new Vector3(30f, 40f, 0.1f), wall, false), r, new Vector3(0f, 16f, 2.4f));
            for (var i = 0; i < 14; i++) Child(World.ArtLibrary.Box(r, "Stripe", o, new Vector3(0.45f, 40f, 0.02f), stripe, false), r, new Vector3(-9.75f + i * 1.5f, 16f, 2.33f));
            // worktop + cabinet front under it
            var wood = World.ArtLibrary.Solid(new Color(0.78f, 0.58f, 0.4f), 0.2f);
            var door = World.ArtLibrary.Solid(new Color(0.84f, 0.66f, 0.48f), 0.2f);
            Child(World.ArtLibrary.Box(r, "Worktop", o, new Vector3(30f, 0.18f, 3.6f), World.ArtLibrary.Solid(new Color(0.9f, 0.76f, 0.58f), 0.3f)), r, new Vector3(0f, -0.09f, 0.6f));
            Child(World.ArtLibrary.Box(r, "Cabinet", o, new Vector3(30f, 3.2f, 3.4f), wood, false), r, new Vector3(0f, -1.8f, 0.7f));
            for (var i = -3; i <= 3; i++)
            {
                Child(World.ArtLibrary.Box(r, "Door", o, new Vector3(1.05f, 2.2f, 0.06f), door, false), r, new Vector3(i * 1.25f, -1.55f, -1.03f));
                Child(World.ArtLibrary.Sphere(r, "Knob", o, 0.06f, _goldMat, false), r, new Vector3(i * 1.25f + (i < 0 ? 0.38f : -0.38f), -1.1f, -1.08f));
            }
            // backsplash tiles in two staggered rows
            var tileMat = World.ArtLibrary.Solid(new Color(0.85f, 0.95f, 0.93f), 0.5f);
            for (var row = 0; row < 3; row++)
                for (var col = -6; col <= 6; col++)
                    Child(World.ArtLibrary.Box(r, "Tile", o, new Vector3(0.6f, 0.6f, 0.04f), tileMat, false), r, new Vector3(col * 0.66f + (row % 2) * 0.33f, 0.36f + row * 0.66f, 2.3f));
            Window(new Vector3(-1.2f, 3.3f, 2.3f));
            Window(new Vector3(1.4f, 8.8f, 2.3f));
            // shelves with baked treats (rewards for a tall tower)
            Shelf(new Vector3(1.3f, 5.3f, 2.1f), new[] { ("cake", 1.6f), ("cupcake", 1.5f) });
            Shelf(new Vector3(-1.4f, 10.6f, 2.1f), new[] { ("muffin", 2.6f), ("macaron_pink", 2.1f), ("pie", 0.9f) });
            Shelf(new Vector3(1.2f, 14.6f, 2.1f), new[] { ("cookie", 2.3f), ("croissant", 1.25f), ("donut-sprinkles", 1.35f) });
            // bunting across the wall higher up
            var flagMats = new[] { World.ArtLibrary.Solid(new Color(0.95f, 0.62f, 0.7f), 0.1f), World.ArtLibrary.Solid(new Color(1f, 0.85f, 0.45f), 0.1f), World.ArtLibrary.Solid(new Color(0.6f, 0.85f, 0.75f), 0.1f) };
            for (var i = 0; i < 9; i++)
            {
                var f = i / 8f;
                var flag = World.ArtLibrary.Box(r, "Flag", o, new Vector3(0.3f, 0.3f, 0.03f), flagMats[i % 3], false);
                Child(flag, r, new Vector3(-2.2f + f * 4.4f, 6.9f - Mathf.Sin(f * Mathf.PI) * 0.45f, 2.25f));
                flag.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            // the kitchen clock
            var dial = World.ArtLibrary.Cylinder(r, "Clock", o, 0.5f, 0.06f, World.ArtLibrary.Solid(new Color(0.99f, 0.96f, 0.9f), 0.3f), false);
            Child(dial, r, new Vector3(-0.6f, 12.6f, 2.3f));
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ring = World.ArtLibrary.Cylinder(r, "ClockRim", o, 0.56f, 0.04f, World.ArtLibrary.Solid(new Color(1f, 0.55f, 0.55f), 0.3f), false);
            Child(ring, r, new Vector3(-0.6f, 12.6f, 2.33f));
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // worktop props left and right of the plate (visible width is only ±1.95 m — the edges may crop them)
            Prop("stand_mixer", new Vector3(-1.75f, 0f, 1.2f), 0.8f, 20f);
            Prop("pan", new Vector3(-1.45f, 0f, 0.1f), 0.74f, 40f);
            Prop("kettle", new Vector3(1.7f, 0f, 1.1f), 0.82f, -30f);
            Prop("waffle", new Vector3(1.45f, 0f, -0.2f), 2.1f, 20f);
            Prop("strawberry", new Vector3(-1.25f, 0f, -0.55f), 1.0f, 0f);
        }

        private void Prop(string model, Vector3 local, float scale, float yaw)
        {
            var p = _stage.Prop(model, _room3d.position + local, scale, yaw);
            p.SetParent(_room3d, true);
        }

        private void Window(Vector3 local)
        {
            var r = _room3d;
            var o = World.MgStage3D.Origin;
            Child(World.ArtLibrary.Box(r, "Glass", o, new Vector3(1.4f, 1.1f, 0.02f), World.ArtLibrary.Glow(new Color(0.82f, 0.93f, 1f)), false), r, local);
            var frame = World.ArtLibrary.Solid(new Color(1f, 0.97f, 0.92f), 0.2f);
            foreach (var (p, s) in new[] { (new Vector3(0f, 0.58f, -0.02f), new Vector3(1.56f, 0.1f, 0.06f)), (new Vector3(0f, -0.58f, -0.02f), new Vector3(1.56f, 0.1f, 0.06f)),
                         (new Vector3(-0.75f, 0f, -0.02f), new Vector3(0.1f, 1.26f, 0.06f)), (new Vector3(0.75f, 0f, -0.02f), new Vector3(0.1f, 1.26f, 0.06f)),
                         (new Vector3(0f, 0f, -0.02f), new Vector3(0.06f, 1.2f, 0.04f)), (new Vector3(0f, 0f, -0.02f), new Vector3(1.5f, 0.06f, 0.04f)),
                         (new Vector3(0f, -0.7f, -0.12f), new Vector3(1.8f, 0.08f, 0.26f)) })
                Child(World.ArtLibrary.Box(r, "Frame", o, s, frame, false), r, local + p);
            // curtains
            var curtain = World.ArtLibrary.Solid(new Color(1f, 0.7f, 0.78f), 0.1f);
            Child(World.ArtLibrary.Box(r, "CurtainL", o, new Vector3(0.36f, 1.35f, 0.05f), curtain, false), r, local + new Vector3(-0.92f, 0f, -0.08f));
            Child(World.ArtLibrary.Box(r, "CurtainR", o, new Vector3(0.36f, 1.35f, 0.05f), curtain, false), r, local + new Vector3(0.92f, 0f, -0.08f));
        }

        private void Shelf(Vector3 local, (string model, float scale)[] treats)
        {
            var r = _room3d;
            var plank = World.ArtLibrary.Solid(new Color(0.72f, 0.52f, 0.36f), 0.2f);
            Child(World.ArtLibrary.Box(r, "Shelf", World.MgStage3D.Origin, new Vector3(2.2f, 0.08f, 0.5f), plank, false), r, local);
            for (var i = 0; i < treats.Length; i++)
                Prop(treats[i].model, local + new Vector3(-0.7f + i * (1.4f / Mathf.Max(1, treats.Length - 1)), 0.04f, 0f), treats[i].scale, i * 25f);
        }

        /// <summary>A pancake: a squat golden disc with a lighter top face (unit radius; scaled per layer).</summary>
        private Transform SpawnPancake(string name)
        {
            var p = new GameObject(name).transform;
            p.SetParent(_stage.Root.transform, false);
            Child(World.ArtLibrary.Cylinder(p, "Cake", Vector3.zero, 0.5f, 1f, _cakeMat), p, new Vector3(0f, 0.5f, 0f));
            Child(World.ArtLibrary.Cylinder(p, "Top", Vector3.zero, 0.46f, 0.1f, _cakeTopMat, false), p, new Vector3(0f, 0.97f, 0f));
            return p;
        }

        private void ShapePancake(Transform p, Vector3 bottomCentre, float widthM, float heightM, bool topping, bool perfect, float rollDeg)
        {
            p.position = bottomCentre;
            p.rotation = Quaternion.Euler(0f, 0f, rollDeg);
            p.localScale = new Vector3(widthM, heightM, Mathf.Min(widthM, 1.4f));
            var top = p.childCount > 1 ? p.GetChild(1).GetComponent<Renderer>() : null;
            if (top != null) top.sharedMaterial = topping ? _berryMat : perfect ? _goldMat : _cakeTopMat;
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var unit = StageUnit;
            var layerM = (float)_game.Tune.LayerHeight * unit;
            // the kitchen rides down with the camera like the 2D room
            _room3d.position = World.MgStage3D.Origin + Vector3.down * (_camBottom * unit);
            var basePt = StagePoint(0.0, 0.0);
            _plate3d.position = basePt;
            _plate3d.localScale = Vector3.one * ((float)_game.Tune.BaseWidth * unit / 1.6f);
            var angle = (float)_game.Wobble.Angle;
            while (_layers3d.Count < _game.Layers.Count) _layers3d.Add(SpawnPancake("Layer"));
            for (var i = 0; i < _layers3d.Count; i++)
            {
                var on = i < _game.Layers.Count;
                _layers3d[i].gameObject.SetActive(on);
                if (!on) continue;
                var l = _game.Layers[i];
                var h = i * _game.Tune.LayerHeight;
                var cx = PancakeTower.WobbleTopX(l.Center, h, angle);
                ShapePancake(_layers3d[i], StagePoint(cx, h), (float)l.Width * unit, layerM, l.Topping, l.Perfect, -angle * Mathf.Rad2Deg);
            }
            var sx = _game.Falling ? _game.FallX : _game.SwingX;
            var sy = _game.Falling ? _game.FallY : _game.StackHeight + PancakeTowerGame.DropHeight;
            var showActive = !(_game.Finished && _game.Toppled);
            _active3d.gameObject.SetActive(showActive);
            var activeBottom = StagePoint(sx, sy - _game.Tune.LayerHeight);
            ShapePancake(_active3d, activeBottom, (float)_game.Stack.Width * unit, layerM, _game.CurrentIsTopping, false, _game.Falling ? 0f : Mathf.Sin(Time.time * 3f) * 4f);
            // crumbs tumble off the edge
            var live = new HashSet<PancakeTowerGame.Crumb>(_game.Crumbs);
            foreach (var c in _game.Crumbs)
            {
                if (!_crumbs3d.TryGetValue(c, out var tr)) _crumbs3d[c] = tr = SpawnPancake("Crumb");
                ShapePancake(tr, StagePoint(c.Center, c.Y - _game.Tune.LayerHeight), (float)c.Width * unit, layerM, false, false, (float)c.Age * 160f);
            }
            List<PancakeTowerGame.Crumb> dead = null;
            foreach (var kv in _crumbs3d) if (!live.Contains(kv.Key)) (dead ??= new List<PancakeTowerGame.Crumb>()).Add(kv.Key);
            if (dead != null) foreach (var d in dead) { if (_crumbs3d[d] != null) Object.Destroy(_crumbs3d[d].gameObject); _crumbs3d.Remove(d); }
            // Gooby rides the active pancake; after a landing he hops up onto the next one
            var g = _stage.Gooby;
            if (g == null) return;
            var seat = activeBottom + Vector3.up * layerM;
            if (!_goobyPlaced) { _goobyAt = seat; _goobyPlaced = true; }
            var k = 1f - Mathf.Exp(-(_game.Falling ? 30f : 9f) * dt);
            var before = _goobyAt;
            _goobyAt = Vector3.Lerp(_goobyAt, seat, k);
            var rise = Mathf.Max(0f, (_goobyAt.y - before.y) / Mathf.Max(dt, 1e-3f));
            g.transform.position = _goobyAt + Vector3.up * Mathf.Min(0.4f, rise * 0.03f);
            g.transform.rotation = Quaternion.Euler(0f, 180f + Mathf.Sin(Time.time * 1.3f) * 18f, _game.Falling ? 0f : Mathf.Sin(Time.time * 3f) * 4f);
            g.gameObject.SetActive(showActive);
            if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) g.SetEmotion("happy");
        }

        private void StageLanded(PancakeTowerGame.Layer layer, PancakeTower.Drop drop)
        {
            if (_stage == null) return;
            var at = StagePoint(layer.Center, _game.StackHeight) + Vector3.back * 0.3f;
            if (!ReducedMotion)
            {
                if (drop.Perfect) World.Effects.Puff(at, new Color(1f, 0.88f, 0.45f), 18, 0.13f, 2.4f, 0.6f, Vector3.up * 2f, 1.3f);
                else World.Effects.Puff(at, new Color(0.95f, 0.8f, 0.6f), 8, 0.1f, 1.4f, 0.45f, Vector3.up, 1.2f);
            }
            _stage.Gooby?.SetEmotion(drop.Perfect ? "ecstatic" : drop.Points > 0 ? "happy" : "scared");
            _emoteLeft = 1.2f;
        }

        private void StageToppled()
        {
            if (_stage?.Gooby == null) return;
            _stage.Gooby.SetEmotion("sad");
            _emoteLeft = 0f;
        }

        private void DropStage()
        {
            _layers3d.Clear();
            _crumbs3d.Clear();
            _goobyPlaced = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
