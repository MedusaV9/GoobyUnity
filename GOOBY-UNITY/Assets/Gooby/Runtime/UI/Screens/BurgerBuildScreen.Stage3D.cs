using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D diner of Burger-Bau (port of burger_build_stage3d.gd): Gooby stands as the chef (with a white toque) behind
    /// the counter, the ingredients rain down three chutes as chunky 3D layers — domed sesame bun, patty, a cheese square,
    /// tomato, frilly lettuce, an onion ring — onto the plate that rides along the counter, where the burger grows layer by
    /// layer. Tiled wall, chalk menu, shelf with fries and soda, fridge, pendant lamps and bar stools around it. The engine
    /// mapping is the 2D one (field px → stage), so the plate follows the finger exactly as before.
    /// </summary>
    public sealed partial class BurgerBuildScreen
    {
        private World.MgStage3D _stage;
        private Transform _plate3d;
        private readonly List<Transform> _stack3d = new List<Transform>();
        private readonly Dictionary<BurgerBuildGame.Item, Transform> _items3d = new Dictionary<BurgerBuildGame.Item, Transform>();
        private readonly Dictionary<string, Material> _layerMats = new Dictionary<string, Material>();
        private Material _seedMat;
        private int _stackOrder = -1;
        private float _emoteLeft, _chefHop;

        protected override bool StageHasGooby => _stage != null;

        /// <summary>Engine world (units) → stage world through the 2D projection.</summary>
        private Vector3 StagePoint(double wx, double wy, float depth = 0f)
        {
            var p = Project(wx, wy);
            return _stage.ViewToWorld(p.x * ViewSize.w / FieldW, p.y * ViewSize.h / FieldH, depth);
        }

        /// <summary>Metres per engine unit on the stage plane.</summary>
        private float StageUnit => Ppu * (float)(ViewSize.w / FieldW) * World.MgStage3D.MetresPerPx;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewSize.w, ViewSize.h, new Color(0.98f, 0.9f, 0.84f));
            _stage.Bind(Field);
            foreach (var kv in LayerColor) _layerMats[kv.Key] = World.ArtLibrary.Solid(kv.Value, kv.Key == "tomato" || kv.Key == "cheese" ? 0.5f : 0.25f);
            _seedMat = World.ArtLibrary.Solid(new Color(1f, 0.96f, 0.84f), 0.3f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnChef(outfit);
        }

        /// <summary>The diner set — built once the field knows its size (the counter sits on the plate line).</summary>
        private void BuildDiner()
        {
            var root = _stage.Root.transform;
            var top = StagePoint(0.0, BurgerBuildGame.PlateY).y - World.MgStage3D.Origin.y - 0.06f;
            _counterY = top;
            var wallMat = World.ArtLibrary.Solid(new Color(1f, 0.93f, 0.88f), 0.05f);
            World.ArtLibrary.Box(root, "Wall", World.MgStage3D.At(0f, 6f, 3.2f), new Vector3(12f, 14f, 0.1f), wallMat, false);
            // pink checker tiles under a wooden rail, stripes above
            var tileA = World.ArtLibrary.Solid(new Color(1f, 0.84f, 0.88f), 0.4f);
            var tileB = World.ArtLibrary.Solid(new Color(1f, 0.97f, 0.93f), 0.4f);
            for (var row = 0; row < 6; row++)
                for (var col = -6; col <= 6; col++)
                    World.ArtLibrary.Box(root, "Tile", World.MgStage3D.At(col * 0.42f, top + 0.21f + row * 0.42f, 3.14f), new Vector3(0.41f, 0.41f, 0.02f), (row + col) % 2 == 0 ? tileA : tileB, false);
            World.ArtLibrary.Box(root, "Rail", World.MgStage3D.At(0f, top + 2.6f, 3.08f), new Vector3(12f, 0.12f, 0.1f), World.ArtLibrary.Solid(new Color(0.85f, 0.66f, 0.48f), 0.2f), false);
            var stripe = World.ArtLibrary.Solid(new Color(0.98f, 0.86f, 0.78f), 0.05f);
            for (var i = 0; i < 10; i++) World.ArtLibrary.Box(root, "Stripe", World.MgStage3D.At(-4.5f + i, top + 5.5f, 3.13f), new Vector3(0.3f, 5.6f, 0.02f), stripe, false);
            // chalk menu board with a burger
            World.ArtLibrary.Box(root, "Menu", World.MgStage3D.At(-0.95f, top + 3.9f, 3.08f), new Vector3(1.4f, 1.0f, 0.06f), World.ArtLibrary.Solid(new Color(0.3f, 0.45f, 0.38f), 0.1f), false);
            World.ArtLibrary.Box(root, "MenuFrame", World.MgStage3D.At(-0.95f, top + 3.9f, 3.11f), new Vector3(1.56f, 1.16f, 0.04f), World.ArtLibrary.Solid(new Color(0.83f, 0.66f, 0.49f), 0.2f), false);
            _stage.Prop("burger", World.MgStage3D.At(-1.32f, top + 3.95f, 3.0f), 0.7f, 20f);
            var chalk = World.ArtLibrary.Solid(new Color(0.97f, 0.96f, 0.9f), 0.05f);
            for (var i = 0; i < 3; i++) World.ArtLibrary.Box(root, "Chalk", World.MgStage3D.At(-0.7f, top + 4.12f - i * 0.2f, 3.04f), new Vector3(0.6f - i * 0.1f, 0.04f, 0.01f), chalk, false);
            // shelf with fries and soda, the fridge, a coffee machine
            World.ArtLibrary.Box(root, "Shelf", World.MgStage3D.At(1.05f, top + 3.3f, 2.95f), new Vector3(1.5f, 0.07f, 0.36f), World.ArtLibrary.Solid(new Color(0.72f, 0.52f, 0.36f), 0.2f), false);
            _stage.Prop("fries", World.MgStage3D.At(0.65f, top + 3.34f, 2.95f), 1.3f, 10f);
            _stage.Prop("soda", World.MgStage3D.At(1.05f, top + 3.34f, 2.95f), 1.1f, 0f);
            _stage.Prop("fries", World.MgStage3D.At(1.45f, top + 3.34f, 2.95f), 1.3f, -15f);
            _stage.Prop("kitchenFridgeLarge", World.MgStage3D.At(1.75f, top - 1.0f, 2.6f), 4.4f, 180f);
            // pendant lamps
            foreach (var x in new[] { -1.1f, 1.2f })
            {
                World.ArtLibrary.Cylinder(root, "Cord", World.MgStage3D.At(x, top + 6.4f, 1.2f), 0.012f, 2.4f, World.ArtLibrary.Solid(new Color(0.3f, 0.26f, 0.24f)), false);
                var shade = World.ArtLibrary.Sphere(root, "Shade", World.MgStage3D.At(x, top + 5.2f, 1.2f), 0.3f, World.ArtLibrary.Solid(new Color(1f, 0.82f, 0.4f), 0.4f), false);
                shade.transform.localScale = new Vector3(0.6f, 0.36f, 0.6f);
                World.ArtLibrary.Sphere(root, "Bulb", World.MgStage3D.At(x, top + 5.08f, 1.2f), 0.08f, World.ArtLibrary.Glow(new Color(1.5f, 1.3f, 0.9f)), false);
            }
            // the counter: worktop + panelled front reaching below the view
            World.ArtLibrary.Box(root, "Worktop", World.MgStage3D.At(0f, top - 0.06f, 0.1f), new Vector3(12f, 0.12f, 1.4f), World.ArtLibrary.Solid(new Color(0.92f, 0.79f, 0.62f), 0.35f));
            World.ArtLibrary.Box(root, "CounterBody", World.MgStage3D.At(0f, top - 2.2f, 0.15f), new Vector3(12f, 4.2f, 1.2f), World.ArtLibrary.Solid(new Color(0.79f, 0.6f, 0.43f), 0.2f), false);
            var panel = World.ArtLibrary.Solid(new Color(0.85f, 0.67f, 0.49f), 0.2f);
            for (var i = -3; i <= 3; i++) World.ArtLibrary.Box(root, "Panel", World.MgStage3D.At(i * 0.95f, top - 1.0f, -0.47f), new Vector3(0.8f, 1.5f, 0.04f), panel, false);
            // bar stools in front
            foreach (var x in new[] { -1.3f, 1.3f }) _stage.Prop("stoolBar", World.MgStage3D.At(x, top - 2.4f, -1.2f), 4.2f, 0f);
            _plate3d = new GameObject("Plate").transform;
            _plate3d.SetParent(root, false);
            var dish = _stage.Prop("plate-dinner", World.MgStage3D.At(0f, top, 0f), 1f);
            dish.SetParent(_plate3d, true);
            dish.localPosition = Vector3.zero;
        }

        private float _counterY = float.NaN;

        private async System.Threading.Tasks.Task SpawnChef(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(-1.55f, 0f, 1.0f), 2.1f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            // the white toque on the head bone (bind-pose crown, then parented like the ice pack of Care:ice)
            var head = g.Bone("head");
            if (head == null) return;
            var gs = g.transform.lossyScale.x;
            var hat = new GameObject("Toque").transform;
            hat.position = g.transform.TransformPoint(new Vector3(0f, 0.93f, 0.06f));
            hat.rotation = g.transform.rotation;
            var white = World.ArtLibrary.Solid(new Color(1f, 1f, 0.99f), 0.2f);
            World.ArtLibrary.Cylinder(hat, "Band", hat.position + hat.up * (0.05f * gs), 0.11f * gs, 0.1f * gs, white);
            foreach (var (dx, dy, r) in new[] { (0f, 0.16f, 0.11f), (-0.07f, 0.13f, 0.08f), (0.07f, 0.13f, 0.08f) })
                World.ArtLibrary.Sphere(hat, "Puff", hat.position + hat.right * (dx * gs) + hat.up * (dy * gs), r * gs, white);
            hat.SetParent(head, true);
        }

        /// <summary>A layer of the burger as a chunky 3D piece (unit width; scaled per use).</summary>
        private Transform SpawnLayer(string id, bool topBun)
        {
            var t = new GameObject("Layer:" + id).transform;
            t.SetParent(_stage.Root.transform, false);
            var mat = _layerMats.TryGetValue(id, out var m) ? m : World.ArtLibrary.Solid(Color.gray);
            GameObject Part(GameObject go, Vector3 local, Vector3 scale) { go.transform.SetParent(t, false); go.transform.localPosition = local; go.transform.localScale = scale; return go; }
            switch (id)
            {
                case "bun":
                    if (topBun)
                    {
                        Part(World.ArtLibrary.Sphere(t, "Dome", Vector3.zero, 0.5f, mat), new Vector3(0f, 0.02f, 0f), new Vector3(1f, 0.62f, 1f));
                        for (var i = 0; i < 7; i++)
                        {
                            var a = i * 2.4f;
                            var r = 0.14f + (i % 3) * 0.1f;
                            Part(World.ArtLibrary.Sphere(t, "Seed", Vector3.zero, 0.5f, _seedMat, false), new Vector3(Mathf.Cos(a) * r, 0.24f + (0.33f - r) * 0.18f, Mathf.Sin(a) * r - 0.05f), new Vector3(0.07f, 0.03f, 0.045f));
                        }
                    }
                    else Part(World.ArtLibrary.Cylinder(t, "Heel", Vector3.zero, 0.5f, 1f, mat), new Vector3(0f, 0.09f, 0f), new Vector3(0.98f, 0.09f, 0.98f));
                    break;
                case "patty":
                    Part(World.ArtLibrary.Cylinder(t, "Patty", Vector3.zero, 0.5f, 1f, mat), new Vector3(0f, 0.12f, 0f), new Vector3(0.92f, 0.11f, 0.92f));
                    break;
                case "cheese":
                    var slice = Part(World.ArtLibrary.Box(t, "Cheese", Vector3.zero, Vector3.one, mat), new Vector3(0f, 0.04f, 0f), new Vector3(0.82f, 0.05f, 0.82f));
                    slice.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    break;
                case "tomato":
                    Part(World.ArtLibrary.Cylinder(t, "Tomato", Vector3.zero, 0.5f, 1f, mat), new Vector3(0f, 0.05f, 0f), new Vector3(0.84f, 0.045f, 0.84f));
                    break;
                case "salad":
                    for (var i = 0; i < 6; i++)
                    {
                        var a = i * Mathf.PI / 3f;
                        Part(World.ArtLibrary.Sphere(t, "Leaf", Vector3.zero, 0.5f, mat), new Vector3(Mathf.Cos(a) * 0.3f, 0.05f + (i % 2) * 0.02f, Mathf.Sin(a) * 0.3f), new Vector3(0.48f, 0.08f, 0.48f));
                    }
                    break;
                default:
                    Part(World.ArtLibrary.Cylinder(t, "Ring", Vector3.zero, 0.5f, 1f, mat), new Vector3(0f, 0.04f, 0f), new Vector3(0.78f, 0.035f, 0.78f));
                    Part(World.ArtLibrary.Cylinder(t, "Hole", Vector3.zero, 0.5f, 1f, World.ArtLibrary.Solid(new Color(0.75f, 0.6f, 0.78f), 0.4f), false), new Vector3(0f, 0.045f, 0f), new Vector3(0.36f, 0.036f, 0.36f));
                    break;
            }
            return t;
        }

        private void RenderStage()
        {
            if (float.IsNaN(_counterY)) BuildDiner();
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var unit = StageUnit;
            var plateAt = StagePoint(_game.PlateX, BurgerBuildGame.PlateY);
            var plateW = (float)_game.Tune.PlateHalfWidth * 2f * unit;
            _plate3d.position = new Vector3(plateAt.x, World.MgStage3D.Origin.y + _counterY, plateAt.z);
            _plate3d.localScale = Vector3.one * (plateW / 0.892f);
            // the burger on the plate: one piece per placed layer, rebuilt when the order or the count changes
            if (_stackOrder != _game.OrderNumber || _stack3d.Count != _game.Placed)
            {
                if (_stackOrder != _game.OrderNumber || _stack3d.Count > _game.Placed)
                {
                    foreach (var s in _stack3d) if (s != null) Object.Destroy(s.gameObject);
                    _stack3d.Clear();
                }
                _stackOrder = _game.OrderNumber;
                while (_stack3d.Count < _game.Placed)
                {
                    var i = _stack3d.Count;
                    _stack3d.Add(SpawnLayer(_game.Ticket[i], i == _game.Ticket.Count - 1 && i > 0));
                }
            }
            var layerW = 0.9f * plateW;
            for (var i = 0; i < _stack3d.Count; i++)
            {
                var y = BurgerBuildGame.PlateY + BurgerBuildGame.StackBase + i * BurgerBuildGame.LayerHeight;
                var p = StagePoint(_game.PlateX, y - BurgerBuildGame.LayerHeight * 0.5);
                _stack3d[i].position = p;
                _stack3d[i].localScale = new Vector3(layerW, layerW * 1.2f, layerW);
                _stack3d[i].rotation = Quaternion.Euler(0f, i * 23f, 0f);
            }
            // the rain: each falling ingredient as its 3D piece, tumbling a little
            var live = new HashSet<BurgerBuildGame.Item>(_game.Items);
            foreach (var it in _game.Items)
            {
                if (!_items3d.TryGetValue(it, out var tr)) _items3d[it] = tr = SpawnLayer(it.Id, it.Id == "bun");
                tr.position = StagePoint(it.X, it.Y);
                tr.localScale = new Vector3(unit, unit * 1.2f, unit);
                var ph = (it.GetHashCode() & 63) * 0.3f;
                tr.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 2f + ph) * 18f, Time.time * 60f + ph * 30f, Mathf.Cos(Time.time * 1.6f + ph) * 14f);
            }
            List<BurgerBuildGame.Item> dead = null;
            foreach (var kv in _items3d) if (!live.Contains(kv.Key)) (dead ??= new List<BurgerBuildGame.Item>()).Add(kv.Key);
            if (dead != null) foreach (var d in dead) { if (_items3d[d] != null) Object.Destroy(_items3d[d].gameObject); _items3d.Remove(d); }
            // the chef turns towards the plate and bobs; a hop when an order is done
            var g = _stage.Gooby;
            if (g == null) return;
            _chefHop = Mathf.Max(0f, _chefHop - dt * 2.2f);
            var look = Mathf.Clamp((plateAt.x - g.transform.position.x) * 30f, -30f, 40f);
            g.transform.position = new Vector3(World.MgStage3D.Origin.x - 1.55f, World.MgStage3D.Origin.y + _counterY - 0.95f + Mathf.Sin(_chefHop * Mathf.PI) * 0.4f, World.MgStage3D.Origin.z + 1.0f);
            g.transform.rotation = Quaternion.Euler(0f, 180f - look, 0f);
            if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) g.SetEmotion("happy");
        }

        private void StageCaught(bool correct)
        {
            if (_stage == null || _plate3d == null) return;
            var at = StagePoint(_game.PlateX, BurgerBuildGame.PlateY + _game.StackTop) + Vector3.back * 0.3f;
            if (!ReducedMotion) World.Effects.Puff(at, correct ? new Color(1f, 0.92f, 0.6f) : new Color(0.75f, 0.7f, 0.66f, 0.9f), correct ? 10 : 8, 0.08f, 1.4f, 0.45f, Vector3.up, 1f);
            _stage.Gooby?.SetEmotion(correct ? "happy" : "dizzy");
            _emoteLeft = 1f;
        }

        private void StageOrder(bool done)
        {
            var g = _stage?.Gooby;
            if (g == null) return;
            g.SetEmotion(done ? "ecstatic" : "sad");
            if (done) { g.Play("celebrate"); _chefHop = 1f; if (!ReducedMotion) World.Effects.Sparkle(_plate3d.position + Vector3.up * 0.8f, 0.6f, 26); }
            _emoteLeft = 1.6f;
        }

        private void DropStage()
        {
            _stack3d.Clear();
            _items3d.Clear();
            _stackOrder = -1;
            _counterY = float.NaN;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
