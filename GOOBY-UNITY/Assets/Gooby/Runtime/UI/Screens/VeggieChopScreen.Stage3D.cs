using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D kitchen of the Gemüse-Schnippler (port of veggie_chop_kitchen3d.gd): the camera looks straight at the flight
    /// plane, framed exactly like the engine's view mapping, so real food models fly where the knife's hit test expects
    /// them and a cut drops the matching half models in a splash of juice and gold sparks. Behind the plane: a mint tiled
    /// splashback with a garden window, a shelf with jars, a pot rail with pans, a clock, the ceiling beams; the counter
    /// with cupboard doors, the cutting board, a steaming stew pot and a frying pan. The player's Gooby is the chef behind
    /// the counter — in a chef's hat, chopping, with a knife that swings on every cut — dizzy after junk, beaming in a
    /// frenzy, when the kitchen lamp turns gold.
    /// </summary>
    public sealed partial class VeggieChopScreen
    {
        private sealed class Half { public Transform Node; public string Key; public Vector3 Vel, Spin; public float Age; }

        private World.MgStage3D _stage;
        private readonly Dictionary<string, Stack<Transform>> _pool = new Dictionary<string, Stack<Transform>>();
        private readonly Dictionary<VeggieChopGame.Flying, (string key, Transform node)> _live3d = new Dictionary<VeggieChopGame.Flying, (string, Transform)>();
        private readonly List<VeggieChopGame.Flying> _gone3d = new List<VeggieChopGame.Flying>();
        private readonly List<Half> _halves = new List<Half>();
        private Transform _items3d, _knife, _knifeSwing;
        private Light _lamp;
        private string _chefMood = "";
        private float _chopT = -1f, _steamT, _stageT;

        private const float CamDist = 9f, CounterY = -3.35f, WallZ = 4.8f, ItemSize = 0.95f;
        private static readonly Color LampWarm = new Color(1f, 0.86f, 0.66f);
        private static readonly Color LampGold = new Color(1f, 0.72f, 0.32f);

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 P3(float x, float y, float z) => World.MgStage3D.At(x, y, z);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#F6E2CF"));
            _stage.Bind(Field);
            var fov = 2f * Mathf.Atan((float)VeggieChopGame.HalfH / CamDist) * Mathf.Rad2Deg;
            _stage.Aim(new Vector3(0f, 0f, -CamDist), 0f, fov);
            var root = _stage.Root.transform;
            BuildWall(root);
            BuildCounter(root);
            BuildUpperWall(root);
            _items3d = new GameObject("Items").transform;
            _items3d.SetParent(root, false);
            _lamp = new GameObject("KitchenLamp").AddComponent<Light>();
            _lamp.transform.SetParent(root, false);
            _lamp.transform.position = P3(0.2f, CounterY + 3.2f, 1f);
            _lamp.type = LightType.Point;
            _lamp.range = 9f;
            _lamp.intensity = 2.2f;
            _lamp.color = LampWarm;
            _lamp.shadows = LightShadows.None;
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            outfit["hut"] = "chefHat";
            _ = SpawnChef(outfit);
            // warm the pool: every veggie, its half and the junk load before the first throw
            foreach (var key in new[] { "apple", "pear", "lemon", "onion", "mushroom", "paprika", "tomato", "coconut", "soda", "boot" })
            {
                Release(key, MakePiece(key));
                Release(key, MakePiece(key));
            }
        }

        // ── the room ──

        private void BuildWall(Transform root)
        {
            World.ArtLibrary.Box(root, "Wall", P3(0f, 2f, WallZ + 0.2f), new Vector3(30f, 18f, 0.4f),
                World.ArtLibrary.PbrSlab("plaster", new Vector2(30f, 18f), 3f, World.ArtLibrary.Hex("#F0CDB0"), 0.1f), false);
            // the splashback: mint tiles over the counter, the garden window left out
            var tiles = new GameObject("Tiles").transform;
            tiles.SetParent(root, false);
            var mint = World.ArtLibrary.Solid(new Color(0.6f, 0.83f, 0.8f), 0.55f);
            var cream = World.ArtLibrary.Solid(new Color(0.96f, 0.93f, 0.86f), 0.55f);
            for (var row = 0; row < 11; row++)
                for (var col = 0; col < 14; col++)
                {
                    var p = new Vector3(-4.55f + col * 0.7f, CounterY + 0.5f + row * 0.7f, WallZ - 0.02f);
                    if (Mathf.Abs(p.x - WindowX) < 1.75f && Mathf.Abs(p.y - WindowY) < 1.55f) continue;
                    World.ArtLibrary.Box(tiles, "Tile", P3(p.x, p.y, p.z), new Vector3(0.62f, 0.62f, 0.06f), (row + col) % 5 == 0 ? cream : mint, false);
                }
            _stage.MergeChildren(tiles, "Tiles");
            BuildWindow(root);
            World.ArtLibrary.Box(root, "Floor", P3(0f, -5.75f, 1.5f), new Vector3(30f, 0.1f, 14f), World.ArtLibrary.PbrSlab("wood_floor", new Vector2(30f, 14f), 2f, World.ArtLibrary.Hex("#C9A385"), 0.3f), false);
        }

        private const float WindowX = 0.95f, WindowY = 1.05f;

        private void BuildWindow(Transform root)
        {
            var z = WallZ - 0.04f;
            var sky = World.ArtLibrary.Glow(new Color(0.74f, 0.88f, 1f));
            World.ArtLibrary.Box(root, "Sky", P3(WindowX, WindowY + 0.45f, z + 0.03f), new Vector3(2.9f, 1.75f, 0.02f), sky, false);
            World.ArtLibrary.Box(root, "Hedge", P3(WindowX, WindowY - 0.85f, z + 0.02f), new Vector3(2.9f, 0.95f, 0.02f), World.ArtLibrary.Glow(new Color(0.6f, 0.8f, 0.52f)), false);
            foreach (var (x, y, r, c) in new[] { (WindowX - 0.9f, WindowY + 0.9f, 0.3f, new Color(1.1f, 1.02f, 0.75f)), (WindowX + 0.25f, WindowY + 0.75f, 0.26f, Color.white), (WindowX + 0.55f, WindowY + 0.8f, 0.2f, Color.white), (WindowX + 0.85f, WindowY + 0.72f, 0.16f, Color.white),
                                                (WindowX - 0.95f, WindowY - 0.3f, 0.5f, new Color(0.44f, 0.68f, 0.42f)), (WindowX, WindowY - 0.22f, 0.62f, new Color(0.5f, 0.73f, 0.44f)), (WindowX + 1f, WindowY - 0.34f, 0.46f, new Color(0.42f, 0.65f, 0.4f)) })
            {
                var blob = World.ArtLibrary.Sphere(root, "View", P3(x, y, z + 0.01f), r, World.ArtLibrary.Glow(c), false).transform;
                blob.localScale = new Vector3(r * 2f, r * 1.7f, 0.02f);
            }
            var frame = World.ArtLibrary.Solid(new Color(0.99f, 0.97f, 0.94f), 0.4f);
            foreach (var s in new[] { -1f, 1f })
            {
                World.ArtLibrary.Box(root, "Frame", P3(WindowX, WindowY + s * 1.4f, z - 0.05f), new Vector3(3.4f, 0.16f, 0.12f), frame, false);
                World.ArtLibrary.Box(root, "Frame", P3(WindowX + s * 1.55f, WindowY, z - 0.05f), new Vector3(0.16f, 2.95f, 0.12f), frame, false);
            }
            World.ArtLibrary.Box(root, "Sill", P3(WindowX, WindowY - 1.5f, z - 0.16f), new Vector3(3.5f, 0.1f, 0.34f), frame, false);
            World.ArtLibrary.Box(root, "Bar", P3(WindowX, WindowY - 0.3f, z - 0.06f), new Vector3(3.0f, 0.1f, 0.1f), frame, false);
            _stage.PropFit("pot_small", P3(WindowX - 0.9f, WindowY - 1.45f, z - 0.16f), 0.42f, 10f);
        }

        private void BuildCounter(Transform root)
        {
            World.ArtLibrary.Box(root, "Top", P3(0f, CounterY - 0.15f, 2.2f), new Vector3(32f, 0.3f, 2.6f),
                World.ArtLibrary.PbrSlab("wood_planks", new Vector2(32f, 2.6f), 1.2f, World.ArtLibrary.Hex("#E9CDA6"), 0.4f), false);
            World.ArtLibrary.Box(root, "Body", P3(0f, CounterY - 1.35f, 2.25f), new Vector3(31.6f, 2.1f, 2.3f), World.ArtLibrary.Solid(new Color(0.79f, 0.56f, 0.4f), 0.2f), false);
            var doors = new GameObject("Doors").transform;
            doors.SetParent(root, false);
            var door = World.ArtLibrary.Solid(new Color(0.94f, 0.72f, 0.56f), 0.25f);
            var knob = World.ArtLibrary.Solid(new Color(0.95f, 0.93f, 0.9f), 0.7f);
            for (var i = 0; i < 9; i++)
            {
                var x = -6.4f + i * 1.6f;
                World.ArtLibrary.Box(doors, "Door", P3(x, CounterY - 1.25f, 1.06f), new Vector3(1.5f, 1.5f, 0.08f), door, false);
                World.ArtLibrary.Box(doors, "Knob", P3(x + 0.6f, CounterY - 1.25f, 1.0f), new Vector3(0.1f, 0.26f, 0.08f), knob, false);
            }
            _stage.MergeChildren(doors, "Doors");
            _stage.PropFit("cutting-board", P3(0.55f, CounterY, 1.85f), 2.0f, 0f);
            _stage.PropFit("pot-stew", P3(1.55f, CounterY, 2.55f), 1.15f, 0f);
            _stage.PropFit("frying-pan", P3(-1.7f, CounterY, 2.45f), 1.4f, 25f);
        }

        /// <summary>The upper half would be bare wall: a shelf of jars, a pot rail with pans, a clock and the ceiling beams.</summary>
        private void BuildUpperWall(Transform root)
        {
            var wood = World.ArtLibrary.Solid(new Color(0.76f, 0.53f, 0.37f), 0.25f);
            World.ArtLibrary.Box(root, "Shelf", P3(-0.6f, 3.0f, WallZ - 0.35f), new Vector3(4.2f, 0.16f, 0.7f), wood, false);
            foreach (var (key, x) in new[] { ("tomato", -2.2f), ("mushroom", -1.3f), ("lemon", -0.3f), ("onion", 0.6f) })
                _stage.PropFit(key, P3(x, 3.08f, WallZ - 0.35f), 0.5f, x * 40f);
            var steel = World.ArtLibrary.Solid(new Color(0.82f, 0.85f, 0.88f), 0.8f);
            var rail = World.ArtLibrary.Cylinder(root, "PotRail", P3(0f, 4.55f, WallZ - 0.8f), 0.06f, 20f, steel, false).transform;
            rail.rotation = Quaternion.Euler(0f, 0f, 90f);
            foreach (var (key, x, size) in new[] { ("frying-pan", -2.1f, 1.0f), ("pot-stew", -0.9f, 0.85f), ("frying-pan", 2.1f, 0.9f) })
            {
                var pan = _stage.PropFit(key, P3(x, 4.5f, WallZ - 0.8f), size, 0f);
                pan.rotation = Quaternion.Euler(-88f, 0f, 0f);
                pan.position = P3(x, 4.45f - size * 0.5f, WallZ - 0.8f);
            }
            // the clock: a cream face with its hands at a few minutes to twelve
            var face = World.ArtLibrary.Cylinder(root, "Clock", P3(-2.3f, 1.55f, WallZ - 0.1f), 0.42f, 0.12f, World.ArtLibrary.Solid(new Color(0.99f, 0.97f, 0.92f), 0.4f), false).transform;
            face.rotation = Quaternion.Euler(90f, 0f, 0f);
            World.RanchDressing.Ring(root, "ClockRim", 0.4f, 0.47f, World.ArtLibrary.Solid(new Color(0.95f, 0.55f, 0.65f), 0.4f)).SetPositionAndRotation(P3(-2.3f, 1.55f, WallZ - 0.17f), Quaternion.Euler(-90f, 0f, 0f));
            var hand = World.ArtLibrary.Solid(new Color(0.25f, 0.28f, 0.34f), 0.3f);
            foreach (var (len, deg) in new[] { (0.3f, -24f), (0.2f, 150f) })
            {
                var a = deg * Mathf.Deg2Rad;
                var h = World.ArtLibrary.Box(root, "Hand", P3(-2.3f - Mathf.Sin(a) * len * 0.5f, 1.55f + Mathf.Cos(a) * len * 0.5f, WallZ - 0.19f), new Vector3(0.05f, len, 0.03f), hand, false).transform;
                h.rotation = Quaternion.Euler(0f, 0f, deg);
            }
            // the ceiling with two beams closes the tall view
            World.ArtLibrary.Box(root, "Ceiling", P3(0f, 5.25f, 1.5f), new Vector3(30f, 0.2f, 12f), World.ArtLibrary.Solid(new Color(0.99f, 0.94f, 0.88f), 0.1f), false);
            foreach (var z in new[] { 3.4f, 0.6f })
                World.ArtLibrary.Box(root, "Beam", P3(0f, 4.95f, z), new Vector3(30f, 0.42f, 0.5f), World.ArtLibrary.PbrSlab("wood_planks", new Vector2(30f, 0.5f), 1f, World.ArtLibrary.Hex("#B7865F"), 0.2f), false);
        }

        // ── the chef ──

        private async System.Threading.Tasks.Task SpawnChef(Dictionary<string, string> outfit)
        {
            var feet = P3(-1.05f, -3.95f, 2.9f);
            var g = await _stage.SpawnGoobyAsync(feet, 1f, outfit);
            if (g == null || _stage == null) return;
            var height = 1f;
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                height = Mathf.Max(0.3f, b.size.y);
            }
            g.transform.localScale = Vector3.one * (3.6f / height);
            g.transform.rotation = Quaternion.Euler(0f, 194f, 0f);
            g.Play("build_hammer-loop");
            _chefMood = "";
            // the knife hovers over the board at his right, its swing joint turns on every cut
            _knife = new GameObject("Knife").transform;
            _knife.SetParent(_stage.Root.transform, false);
            _knife.position = P3(0.3f, -2.85f, 1.8f);
            _knife.rotation = Quaternion.LookRotation(P3(1.25f, -3.3f, 1.7f) - _knife.position, Vector3.up);
            _knifeSwing = new GameObject("Swing").transform;
            _knifeSwing.SetParent(_knife, false);
            World.ArtLibrary.Box(_knifeSwing, "Blade", _knife.position, new Vector3(0.08f, 0.24f, 1.0f), World.ArtLibrary.Solid(new Color(0.86f, 0.9f, 0.94f), 0.85f), false).transform.localPosition = new Vector3(0f, 0.06f, 0.55f);
            World.ArtLibrary.Box(_knifeSwing, "Grip", _knife.position, new Vector3(0.12f, 0.16f, 0.44f), World.ArtLibrary.Solid(new Color(0.44f, 0.29f, 0.2f), 0.3f), false).transform.localPosition = new Vector3(0f, 0f, -0.2f);
        }

        // ── the pieces ──

        private static string ModelOf(string key) => key == "soda" ? "soda-can-crushed" : key == "boot" ? "fish-bones" : key;

        private Transform MakePiece(string key)
        {
            var half = key.StartsWith("half:");
            var model = half ? key.Substring(5) : ModelOf(key);
            var node = new GameObject("Piece:" + key).transform;
            node.SetParent(_items3d, false);
            _stage.PropFit(model, P3(0f, -20f, 0f), half ? ItemSize * 0.8f : ItemSize, 0f, false, node, false).localPosition = Vector3.zero;
            return node;
        }

        private Transform Take(string key)
        {
            if (_pool.TryGetValue(key, out var free) && free.Count > 0)
            {
                var n = free.Pop();
                n.gameObject.SetActive(true);
                return n;
            }
            return MakePiece(key);
        }

        private void Release(string key, Transform node)
        {
            node.gameObject.SetActive(false);
            if (!_pool.TryGetValue(key, out var free)) _pool[key] = free = new Stack<Transform>();
            free.Push(node);
        }

        // ── every frame ──

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            // the flying pieces: one pooled model each, spinning on their own axes
            foreach (var e in _game.Items)
            {
                if (!_live3d.TryGetValue(e, out var entry))
                {
                    entry = (e.Item.Key, Take(e.Item.Key));
                    _live3d[e] = entry;
                }
                entry.node.position = P3((float)e.X, (float)e.Y, 0f);
                var spin = (float)(e.Spin + e.T * 3.0) * Mathf.Rad2Deg;
                entry.node.rotation = Quaternion.Euler(spin * 0.6f, spin, spin * 0.35f);
            }
            _gone3d.Clear();
            foreach (var kv in _live3d) if (!_game.Items.Contains(kv.Key)) _gone3d.Add(kv.Key);
            foreach (var e in _gone3d)
            {
                var (key, node) = _live3d[e];
                _live3d.Remove(e);
                Release(key, node);
            }
            // the cut halves tumble apart under gravity and leave after a second
            for (var i = _halves.Count - 1; i >= 0; i--)
            {
                var h = _halves[i];
                h.Age += dt;
                h.Vel.y -= 11f * dt;
                h.Node.position += h.Vel * dt;
                h.Node.Rotate(h.Spin * (dt * Mathf.Rad2Deg), Space.Self);
                if (h.Age < 1.1f) continue;
                Release(h.Key, h.Node);
                _halves.RemoveAt(i);
            }
            // the knife's chop: up, a fast strike with overshoot, back
            if (_knifeSwing != null)
            {
                var a = 0f;
                if (_chopT >= 0f)
                {
                    _chopT += dt;
                    a = _chopT < 0.07f ? Mathf.Lerp(-55f, 38f, _chopT / 0.07f) : _chopT < 0.31f ? Mathf.Lerp(38f, 0f, EaseOutBack((_chopT - 0.07f) / 0.24f)) : 0f;
                    if (_chopT >= 0.31f) _chopT = -1f;
                }
                _knifeSwing.localRotation = Quaternion.Euler(a, 0f, 0f);
            }
            var gb = _stage.Gooby;
            if (gb != null)
            {
                var mood = _game.Stunned ? "dizzy" : _game.InFrenzy || _game.SwipeCombo >= 2 ? "ecstatic" : _game.Misses >= _game.Tune.MaxMisses - 1 ? "scared" : "happy";
                if (mood != _chefMood) { _chefMood = mood; gb.SetEmotion(mood); }
                gb.transform.rotation = Quaternion.Euler(0f, 194f, _game.Stunned && !ReducedMotion ? Mathf.Sin(_stageT * 18f) * 6f : 0f);
            }
            // the lamp turns gold in a frenzy; steam curls off the stew
            var gold = _game.InFrenzy ? 0.5f + 0.5f * Mathf.Sin(_stageT * 9f) : 0f;
            _lamp.color = Color.Lerp(LampWarm, LampGold, _game.InFrenzy ? 1f : 0f);
            _lamp.intensity = 2.2f + gold * 1.6f;
            _steamT -= dt;
            if (_steamT <= 0f && !ReducedMotion)
            {
                _steamT = 0.35f;
                World.Effects.Puff(P3(1.55f, CounterY + 0.95f, 2.55f), new Color(1f, 1f, 1f, 0.35f), 1, 0.3f, 0.5f, 1.8f, Vector3.up, 0.15f);
            }
        }

        private static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        // ── moments ──

        private void StageChopped(VeggieChopGame.Flying e, Color juice)
        {
            var at = P3((float)e.X, (float)e.Y, 0f);
            _chopT = 0f;
            _stage.Gooby?.Play("build_hammer-loop");
            if (!string.IsNullOrEmpty(e.Item.Half))
                foreach (var side in new[] { -1f, 1f })
                {
                    var key = "half:" + e.Item.Half;
                    var node = Take(key);
                    node.position = at;
                    node.rotation = Quaternion.Euler(0f, side * 90f, 0f);
                    _halves.Add(new Half { Node = node, Key = key, Vel = new Vector3(side * 2.6f, 2.4f, -side * 0.6f), Spin = new Vector3(side * 5f, 3f, side * 4f), Age = 0f });
                }
            if (ReducedMotion) return;
            World.Effects.Puff(at, juice, 16, 0.12f, 3.2f, 0.6f, Vector3.up, 1.4f);
            World.Effects.Sparkle(at + new Vector3(0f, 0f, -0.2f), 0.35f, 10);
        }

        private void StageJunk(VeggieChopGame.Flying e)
        {
            if (ReducedMotion) return;
            World.Effects.Puff(P3((float)e.X, (float)e.Y, 0f), new Color(0.3f, 0.28f, 0.3f, 0.9f), 14, 0.2f, 1.6f, 0.7f, Vector3.up, 0.9f);
        }

        private void DropStage()
        {
            _pool.Clear();
            _live3d.Clear();
            _halves.Clear();
            _chefMood = "";
            _chopT = -1f;
            _knife = _knifeSwing = null;
            _lamp = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
