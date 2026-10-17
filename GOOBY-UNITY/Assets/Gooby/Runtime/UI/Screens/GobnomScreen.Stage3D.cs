using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Gobnom;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D kitchen of GOB NOM (port of gobnom_stage3d.gd + gobnom_backdrop.gd): the puzzle plays on an upright plane in
    /// front of Gooby's kitchen wall — a tiled splashback, pantry shelves full of jars, a window onto the garden and a
    /// counter with cakes. The candy (with its twisted wrapper), the ropes sagging from wooden pegs, sliding rail pegs,
    /// soap bubbles, the puffing cushions, switchable fans with their wind, spike boards, sugar clouds, the dangling
    /// nutella jars and the rope shooters are real meshes; the player's Gooby is the catcher, standing with his mouth wide
    /// open, chewing on a NOM and sad when the candy is lost. The default front camera maps the 960×540 world exactly onto
    /// the field (1 world px = 1 cm on the plane), so swipes, taps and the canvas's trails stay under the finger.
    /// </summary>
    public sealed partial class GobnomScreen
    {
        private sealed class Rope3D { public Transform Root; public Transform[] Segs; public Transform Peg; }

        private World.MgStage3D _stage;
        private GobnomRun _shownRun;
        private Transform _level, _candy, _candySpin, _mouthRing;
        private readonly List<Rope3D> _ropes3d = new List<Rope3D>();
        private readonly List<Transform> _bubbles3d = new List<Transform>();
        private readonly List<Transform> _jars3d = new List<Transform>();
        private readonly List<(Transform node, Transform blades, List<Transform> wind)> _fans3d = new List<(Transform, Transform, List<Transform>)>();
        private readonly List<Transform> _cushions3d = new List<Transform>();
        private readonly List<Transform> _shooters3d = new List<Transform>();
        private string _shownMood = "";
        private float _stageT;

        private const int RopeSegs = 12;
        private const float U = World.MgStage3D.MetresPerPx;
        private static readonly Color RopeTan = MgScenery.Hex(0xA9744B);
        private static readonly Color WoodPeg = MgScenery.Hex(0xA9744B);
        private static readonly Color RailGold = MgScenery.Hex(0xF3C969);

        protected override bool StageHasGooby => _stage != null;

        private Vector3 W(V2 p, float depth = 0f) => _stage.ViewToWorld(p.X, p.Y, depth);
        private Vector3 W(double x, double y, float depth = 0f) => _stage.ViewToWorld(x, y, depth);

        /// <summary>A world-px direction (y down) as a stage direction (y up).</summary>
        private static Vector3 Dir3(V2 d) => new Vector3(d.X, -d.Y, 0f).normalized;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, MgScenery.Hex(0xF6E7D2));
            _stage.Bind(Field);
            BuildKitchen(_stage.Root.transform);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnCatcher(outfit);
        }

        // ── the kitchen behind the plane ──

        private void BuildKitchen(Transform root)
        {
            const float z = 7.2f;
            World.ArtLibrary.Box(root, "Wall", World.MgStage3D.At(0f, 3f, z + 0.2f), new Vector3(24f, 14f, 0.4f),
                World.ArtLibrary.PbrSlab("plaster", new Vector2(24f, 14f), 3f, World.ArtLibrary.Hex("#EED9B6"), 0.1f), false);
            var tiles = new GameObject("Tiles").transform;
            tiles.SetParent(root, false);
            var sage = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#C4DECE"), 0.55f);
            var cream = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#EFE7CF"), 0.55f);
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 16; col++)
                    World.ArtLibrary.Box(tiles, "Tile", World.MgStage3D.At(-9f + col * 1.2f, 0.62f + row * 1.2f, z - 0.02f), new Vector3(1.16f, 1.16f, 0.06f), (col + row) % 2 == 0 ? sage : cream, false);
            _stage.MergeChildren(tiles, "Tiles");
            // the counter along the wall with cakes on it
            World.ArtLibrary.Box(root, "Counter", World.MgStage3D.At(0f, -0.15f, z - 1.2f), new Vector3(24f, 0.3f, 2.4f),
                World.ArtLibrary.PbrSlab("wood_planks", new Vector2(24f, 2.4f), 1.2f, World.ArtLibrary.Hex("#E2BE8E"), 0.4f), false);
            World.ArtLibrary.Box(root, "CounterFront", World.MgStage3D.At(0f, -1.4f, z - 2.35f), new Vector3(24f, 2.2f, 0.1f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#D9A87A"), 0.2f), false);
            foreach (var (model, x, size) in new[] { ("cupcake", -6.6f, 1.1f), ("cake", 1.4f, 1.5f), ("donut-sprinkles", 6.4f, 1.1f) })
                _stage.PropFit(model, World.MgStage3D.At(x, 0f, z - 1.2f), size, x * 9f);
            // pantry shelves on the left: two boards, jars in pantry colours
            var wood = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#A9744B"), 0.25f);
            var shelves = new GameObject("Shelves").transform;
            shelves.SetParent(root, false);
            string[] pantry = { "#5C3A21", "#C94F4F", "#E8B84C", "#7FA95A", "#B87333", "#5C3A21" };
            foreach (var by in new[] { 4.3f, 6.3f })
            {
                World.ArtLibrary.Box(shelves, "Board", World.MgStage3D.At(-5.2f, by, z - 0.5f), new Vector3(6.6f, 0.22f, 1f), wood, false);
                for (var i = 0; i < 5; i++)
                {
                    var s = 0.85f + ((i * 5 + (int)by) % 3) * 0.15f;
                    var x = -7.9f + i * 1.3f + (by > 5f ? 0.4f : 0f);
                    World.ArtLibrary.Cylinder(shelves, "Jar", World.MgStage3D.At(x, by + 0.11f + 0.45f * s, z - 0.5f), 0.32f * s, 0.9f * s, World.ArtLibrary.Solid(World.ArtLibrary.Hex(pantry[(i * 7 + (int)by) % pantry.Length]), 0.4f), false);
                    World.ArtLibrary.Cylinder(shelves, "Lid", World.MgStage3D.At(x, by + 0.11f + 0.92f * s, z - 0.5f), 0.34f * s, 0.1f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#E8E2D8"), 0.4f), false);
                }
            }
            _stage.MergeChildren(shelves, "Shelves");
            // the window on the right with a garden view
            World.ArtLibrary.Box(root, "WindowFrame", World.MgStage3D.At(5.6f, 4.6f, z - 0.05f), new Vector3(4.4f, 4.6f, 0.2f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#7C5433"), 0.3f), false);
            World.ArtLibrary.Box(root, "Sky", World.MgStage3D.At(5.6f, 5.2f, z - 0.17f), new Vector3(3.8f, 2.6f, 0.02f), World.ArtLibrary.Glow(new Color(0.75f, 0.89f, 1f)), false);
            World.ArtLibrary.Box(root, "Garden", World.MgStage3D.At(5.6f, 3.25f, z - 0.17f), new Vector3(3.8f, 1.3f, 0.02f), World.ArtLibrary.Glow(new Color(0.62f, 0.82f, 0.55f)), false);
            foreach (var (x, y, r) in new[] { (4.6f, 3.9f, 0.55f), (5.7f, 4.05f, 0.7f), (6.7f, 3.85f, 0.5f) })
            {
                var crown = World.ArtLibrary.Sphere(root, "Tree", World.MgStage3D.At(x, y, z - 0.18f), r, World.ArtLibrary.Glow(new Color(0.47f, 0.7f, 0.43f)), false).transform;
                crown.localScale = new Vector3(r * 2f, r * 1.7f, 0.02f);
            }
            var sun = World.ArtLibrary.Sphere(root, "Sun", World.MgStage3D.At(4.5f, 6.0f, z - 0.18f), 0.32f, World.ArtLibrary.Glow(new Color(1.1f, 1.0f, 0.7f)), false).transform;
            sun.localScale = new Vector3(0.64f, 0.64f, 0.02f);
            World.ArtLibrary.Box(root, "Mullion", World.MgStage3D.At(5.6f, 4.6f, z - 0.22f), new Vector3(0.14f, 4.0f, 0.08f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#7C5433"), 0.3f), false);
            World.ArtLibrary.Box(root, "Sill", World.MgStage3D.At(5.6f, 2.25f, z - 0.4f), new Vector3(4.8f, 0.16f, 0.6f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#A9744B"), 0.3f), false);
            _stage.PropFit("pot_small", World.MgStage3D.At(4.3f, 2.33f, z - 0.4f), 0.55f, 20f);
            _stage.PropFit("muffin", World.MgStage3D.At(6.8f, 2.33f, z - 0.4f), 0.5f, -30f);
        }

        // ── the catcher ──

        private async System.Threading.Tasks.Task SpawnCatcher(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(0f, -5f, 0.4f), 1f, outfit);
            if (g == null || _stage == null) return;
            var height = 1f;
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                height = Mathf.Max(0.3f, b.size.y);
            }
            g.transform.localScale = Vector3.one * (1.5f / height);
            g.Play("idle-loop");
            _shownMood = "";
        }

        // ── one level's pieces, rebuilt when a new run starts ──

        private void BuildLevel(GobnomRun run)
        {
            if (_level != null) Object.Destroy(_level.gameObject);
            _ropes3d.Clear(); _bubbles3d.Clear(); _jars3d.Clear(); _fans3d.Clear(); _cushions3d.Clear(); _shooters3d.Clear();
            _level = new GameObject("Level").transform;
            _level.SetParent(_stage.Root.transform, false);
            _shownRun = run;
            if (run == null) return;
            var outline = World.ArtLibrary.Solid(MgScenery.Hex(0x4A3B36), 0.3f);
            // sugar clouds (they slow the candy) and spike boards
            var cloudMat = World.ArtLibrary.Solid(new Color(1f, 0.86f, 0.92f), 0.2f);
            foreach (var c in run.Clouds)
                for (var i = 0; i < 5; i++)
                {
                    var t = i / 4f - 0.5f;
                    var puff = World.ArtLibrary.Sphere(_level, "Cloud", W(c.X + c.W * (0.5 + t * 0.72), c.Y + c.H * (0.5 + Mathf.Sin(t * 4f) * 0.16f), 0.2f), 0.5f, cloudMat, false).transform;
                    puff.localScale = new Vector3((float)c.W * 0.48f * U, (float)c.H * 0.76f * U, 0.5f);
                }
            var spikeWood = World.ArtLibrary.Solid(MgScenery.Hex(0x7C5433), 0.3f);
            var steel = World.ArtLibrary.Solid(MgScenery.Hex(0xB9BEC6), 0.85f);
            foreach (var b in run.Spikes)
            {
                World.ArtLibrary.Box(_level, "SpikeBoard", W(b.X + b.W * 0.5, b.Y + b.H * 0.5), new Vector3((float)b.W * U, (float)b.H * U, 0.25f), spikeWood);
                var horizontal = b.W >= b.H;
                var count = (int)Mathf.Max(2f, (float)(horizontal ? b.W : b.H) / 20f);
                for (var i = 0; i < count; i++)
                {
                    var t = (i + 0.5f) / count;
                    var basePos = horizontal ? W(b.X + b.W * t, b.Y) : W(b.X, b.Y + b.H * t);
                    var cone = _stage.Cone(basePos, 0.08f, 0.16f, steel, true);
                    cone.transform.SetParent(_level, true);
                    if (!horizontal) cone.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }
            // rails: a steel bar with knobs at both ends
            var railMat = World.ArtLibrary.Solid(MgScenery.Hex(0xC9BCA9), 0.6f);
            foreach (var r in run.Ropes)
            {
                if (r.Rail == null) continue;
                var a = W(r.Rail.From, 0.05f); var e = W(r.Rail.To, 0.05f);
                var bar = World.ArtLibrary.Cylinder(_level, "Rail", (a + e) * 0.5f, 0.045f, Vector3.Distance(a, e), railMat).transform;
                bar.rotation = Quaternion.FromToRotation(Vector3.up, (e - a).normalized);
                World.ArtLibrary.Sphere(_level, "RailEnd", a, 0.07f, outline);
                World.ArtLibrary.Sphere(_level, "RailEnd", e, 0.07f, outline);
            }
            // ropes: twelve segments each, laid along the sagging curve every frame; their pegs
            var ropeMat = World.ArtLibrary.Solid(RopeTan, 0.25f);
            var pegWood = World.ArtLibrary.Solid(WoodPeg, 0.3f);
            var pegGold = World.ArtLibrary.Solid(RailGold, 0.55f);
            foreach (var r in run.Ropes)
            {
                var rope = new Rope3D { Root = new GameObject("Rope").transform, Segs = new Transform[RopeSegs] };
                rope.Root.SetParent(_level, false);
                for (var i = 0; i < RopeSegs; i++)
                    rope.Segs[i] = World.ArtLibrary.Cylinder(rope.Root, "Seg", Vector3.zero, 0.03f, 1f, ropeMat).transform;
                var peg = new GameObject("Peg").transform;
                peg.SetParent(_level, false);
                var disc = World.ArtLibrary.Cylinder(peg, "Disc", Vector3.zero, (r.Rail != null ? 13f : 11f) * U, 0.12f, r.Rail != null ? pegGold : pegWood).transform;
                disc.localPosition = Vector3.zero;
                disc.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var bolt = World.ArtLibrary.Sphere(peg, "Bolt", Vector3.zero, 3.6f * U, outline).transform;
                bolt.localPosition = new Vector3(0f, 0f, -0.07f);
                rope.Peg = peg;
                _ropes3d.Add(rope);
            }
            // fans: a steel drum facing the camera, three blades, wind streaks along their blow
            var fanMetal = World.ArtLibrary.Solid(MgScenery.Hex(0x9DA6AD), 0.75f);
            var blade = World.ArtLibrary.Solid(MgScenery.Hex(0xF9EDD6), 0.3f);
            var wind = World.ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.55f));
            foreach (var f in run.Fans)
            {
                var node = new GameObject("Fan").transform;
                node.SetParent(_level, false);
                node.position = W(f.Pos);
                var drum = World.ArtLibrary.Cylinder(node, "Drum", node.position, 21f * U, 0.18f, fanMetal).transform;
                drum.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var blades = new GameObject("Blades").transform;
                blades.SetParent(node, false);
                blades.localPosition = new Vector3(0f, 0f, -0.12f);
                for (var i = 0; i < 3; i++)
                {
                    var bl = World.ArtLibrary.Sphere(blades, "Blade", node.position, 0.5f, blade, false).transform;
                    var a = i * 120f * Mathf.Deg2Rad;
                    bl.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.1f;
                    bl.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                    bl.localScale = new Vector3(0.16f, 0.08f, 0.02f);
                }
                World.ArtLibrary.Sphere(node, "Hub", node.position + new Vector3(0f, 0f, -0.14f), 0.045f, outline, false);
                var streaks = new List<Transform>();
                for (var i = 0; i < 3; i++)
                {
                    var s = World.ArtLibrary.Box(node, "Wind", node.position, new Vector3(0.025f, 0.12f + i * 0.06f, 0.025f), wind, false).transform;
                    streaks.Add(s);
                }
                _fans3d.Add((node, blades, streaks));
            }
            // cushions: a teal pillow with a wooden back, a dark tip that shows where it puffs
            var teal = World.ArtLibrary.Solid(MgScenery.Hex(0x9FD8CF), 0.25f);
            foreach (var cu in run.Cushions)
            {
                var node = new GameObject("Cushion").transform;
                node.SetParent(_level, false);
                node.position = W(cu.Pos);
                var d = Dir3(cu.Dir);
                node.rotation = Quaternion.FromToRotation(Vector3.right, d);
                var pillow = World.ArtLibrary.Sphere(node, "Pillow", node.position, 0.5f, teal).transform;
                pillow.localScale = new Vector3(0.52f, 0.32f, 0.3f);
                var back = World.ArtLibrary.Sphere(node, "Back", node.position, 0.12f, pegWood).transform;
                back.localPosition = new Vector3(-0.16f, 0f, 0f);
                var tip = _stage.Cone(node.position, 0.07f, 0.13f, outline).transform;
                tip.SetParent(node, true);
                tip.localPosition = new Vector3(0.34f, 0f, 0f);
                tip.localRotation = Quaternion.Euler(0f, 0f, -90f);
                _cushions3d.Add(node);
            }
            // shooters: a wooden crate with a cream muzzle facing the camera
            foreach (var s in run.Shooters)
            {
                var node = new GameObject("Shooter").transform;
                node.SetParent(_level, false);
                node.position = W(s.Pos);
                World.ArtLibrary.Box(node, "Crate", node.position, new Vector3(0.36f, 0.3f, 0.3f), pegWood);
                var muzzle = World.ArtLibrary.Cylinder(node, "Muzzle", node.position + new Vector3(0f, 0f, -0.16f), 0.055f, 0.04f, World.ArtLibrary.Solid(MgScenery.Hex(0xF9EDD6), 0.3f)).transform;
                muzzle.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _shooters3d.Add(node);
            }
            // nutella jars: brown glass, a cream lid, a white label with a red dot and a gold star
            var nut = World.ArtLibrary.Solid(MgScenery.Hex(0x5C3A21), 0.55f);
            var lid = World.ArtLibrary.Solid(MgScenery.Hex(0xE8E2D8), 0.4f);
            var label = World.ArtLibrary.Solid(Color.white, 0.3f);
            var gold = World.ArtLibrary.Glow(new Color(1.2f, 0.95f, 0.35f));
            var jr = (float)run.Physics.JarR * U;
            foreach (var j in run.Jars)
            {
                var node = new GameObject("Jar").transform;
                node.SetParent(_level, false);
                node.position = W(j.Pos);
                World.ArtLibrary.Cylinder(node, "Glass", node.position + new Vector3(0f, -jr * 0.12f, 0f), jr * 0.72f, jr * 1.35f, nut);
                World.ArtLibrary.Cylinder(node, "Lid", node.position + new Vector3(0f, jr * 0.7f, 0f), jr * 0.82f, jr * 0.4f, lid);
                var lab = World.ArtLibrary.Sphere(node, "Label", node.position + new Vector3(0f, -jr * 0.05f, -jr * 0.62f), 0.5f, label, false).transform;
                lab.localScale = new Vector3(jr, jr * 0.62f, jr * 0.12f);
                World.ArtLibrary.Sphere(node, "Dot", node.position + new Vector3(0f, -jr * 0.05f, -jr * 0.7f), jr * 0.14f, World.ArtLibrary.Solid(new Color(0.9f, 0.3f, 0.25f), 0.3f), false);
                var star = World.ArtLibrary.Sphere(node, "Star", node.position + new Vector3(jr * 0.85f, jr * 1.1f, -jr * 0.2f), jr * 0.26f, gold, false).transform;
                star.localScale = new Vector3(jr * 0.52f, jr * 0.52f, jr * 0.14f);
                _jars3d.Add(node);
            }
            // soap bubbles
            foreach (var b in run.Bubbles)
            {
                var bub = World.ArtLibrary.Sphere(_level, "Bubble", W(b.Pos, -0.05f), (float)b.R * U, World.ArtLibrary.Transparent(new Color(0.72f, 0.88f, 1f, 0.38f)), false).transform;
                var shine = World.ArtLibrary.Sphere(bub, "Shine", bub.position, 0.5f, World.ArtLibrary.Glow(new Color(1f, 1f, 1f)), false).transform;
                shine.localPosition = new Vector3(-0.17f, 0.17f, -0.38f);
                shine.localScale = new Vector3(0.18f, 0.18f, 0.06f);
                _bubbles3d.Add(bub);
            }
            // the candy: a pink sweet with a twisted cream wrapper on both sides and a shine
            _candy = new GameObject("Candy").transform;
            _candy.SetParent(_level, false);
            _candySpin = new GameObject("Spin").transform;
            _candySpin.SetParent(_candy, false);
            _candySpin.localScale = Vector3.one * 1.3f;
            var cr = (float)run.Physics.CandyR * U;
            World.ArtLibrary.Sphere(_candySpin, "Sweet", _candy.position, cr, World.ArtLibrary.Solid(MgScenery.Hex(0xF2A0B5), 0.6f));
            var wrap = World.ArtLibrary.Solid(MgScenery.Hex(0xFFE9F0), 0.35f);
            foreach (var side in new[] { -1f, 1f })
            {
                var cone = _stage.Cone(_candy.position, cr * 0.62f, cr * 0.95f, wrap, true).transform;
                cone.SetParent(_candySpin, true);
                cone.localPosition = new Vector3(side * cr * 1.55f, 0f, 0f);
                cone.localRotation = Quaternion.Euler(0f, 0f, side * 90f);
            }
            var stripe = _stage.Torus(_candySpin, "Stripe", _candy.position, cr * 0.98f, cr * 0.12f, wrap);
            stripe.transform.localRotation = Quaternion.Euler(62f, 0f, 30f);
            World.ArtLibrary.Sphere(_candySpin, "Shine", _candy.position + new Vector3(-cr * 0.35f, cr * 0.35f, -cr * 0.8f), cr * 0.2f, World.ArtLibrary.Glow(Color.white), false);
            // the mouth zone: a soft pink ring around the catcher's mouth
            _mouthRing = _stage.Torus(_level, "MouthRing", W(run.Mouth, -0.1f), (float)run.Physics.MouthR * U, 0.04f, World.ArtLibrary.Transparent(new Color(1f, 0.5f, 0.68f, 0.7f))).transform;
            _mouthRing.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // ── every frame ──

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            var run = _game?.Run;
            if (run != _shownRun) BuildLevel(run);
            var gb = _stage.Gooby;
            if (run == null)
            {
                if (gb != null) gb.gameObject.SetActive(false);
                return;
            }
            var tick = run.Tick;
            var candyAt = W(run.CandyPos, -0.02f);
            _candy.gameObject.SetActive(run.Outcome != "won");
            _candy.position = candyAt;
            _candySpin.localRotation = Quaternion.Euler(0f, 0f, tick * 0.018f * Mathf.Rad2Deg);
            for (var i = 0; i < _ropes3d.Count && i < run.Ropes.Count; i++)
            {
                var r = run.Ropes[i];
                var rope = _ropes3d[i];
                rope.Root.gameObject.SetActive(!r.Cut);
                rope.Peg.gameObject.SetActive(!r.Cut);
                if (r.Cut) continue;
                var a = W(r.Anchor, 0.02f);
                rope.Peg.position = W(r.Anchor, 0.08f);
                var dist = Vector3.Distance(a, candyAt);
                var rest = (float)r.Rest * U;
                var slack = Mathf.Clamp01(1f - dist / Mathf.Max(rest, 0.01f));
                var mid = (a + candyAt) * 0.5f + Vector3.down * (slack * rest * 0.35f);
                var prev = a;
                for (var s = 0; s < RopeSegs; s++)
                {
                    var t = (s + 1f) / RopeSegs;
                    var u = 1f - t;
                    var p = u * u * a + 2f * u * t * mid + t * t * candyAt;
                    var seg = rope.Segs[s];
                    seg.position = (prev + p) * 0.5f;
                    var len = Vector3.Distance(prev, p);
                    seg.localScale = new Vector3(0.075f, Mathf.Max(0.001f, len * 0.5f + 0.015f), 0.075f);
                    if (len > 1e-5f) seg.rotation = Quaternion.FromToRotation(Vector3.up, (p - prev) / len);
                    prev = p;
                }
            }
            for (var i = 0; i < _bubbles3d.Count && i < run.Bubbles.Count; i++)
            {
                var b = run.Bubbles[i];
                var node = _bubbles3d[i];
                node.gameObject.SetActive(!b.Popped);
                if (b.Popped) continue;
                node.position = b.Holds ? candyAt + new Vector3(0f, 0f, -0.03f) : W(b.Pos, -0.05f);
                node.localScale = Vector3.one * ((float)b.R * U * 2f * (1f + Mathf.Sin(tick * 0.16f) * 0.04f));
            }
            for (var i = 0; i < _jars3d.Count && i < run.Jars.Count; i++)
            {
                var j = run.Jars[i];
                _jars3d[i].gameObject.SetActive(!j.Taken);
                if (j.Taken) continue;
                var bob = Mathf.Sin(tick * 0.15f + j.Pos.X * 0.05f) * (float)run.Physics.JarR * U * 0.08f;
                _jars3d[i].position = W(j.Pos) + Vector3.up * bob;
                _jars3d[i].rotation = Quaternion.Euler(0f, Mathf.Sin(tick * 0.05f + i) * 14f, 0f);
            }
            for (var i = 0; i < _fans3d.Count && i < run.Fans.Count; i++)
            {
                var f = run.Fans[i];
                var (node, blades, streaks) = _fans3d[i];
                blades.localRotation = Quaternion.Euler(0f, 0f, -tick * (f.On ? 0.45f : 0f) * Mathf.Rad2Deg);
                var d = Dir3(f.Dir);
                var perp = new Vector3(-d.y, d.x, 0f);
                for (var k = 0; k < streaks.Count; k++)
                {
                    streaks[k].gameObject.SetActive(f.On);
                    if (!f.On) continue;
                    var off = (k * 16f + (tick * 2.2f) % 16f) * U;
                    streaks[k].position = node.position + d * (26f * U + off) + new Vector3(0f, 0f, -0.05f);
                    streaks[k].rotation = Quaternion.LookRotation(Vector3.forward, perp);
                }
            }
            for (var i = 0; i < _cushions3d.Count && i < run.Cushions.Count; i++)
            {
                var cu = run.Cushions[i];
                var ready = cu.Charges != 0 && run.Tick >= cu.ReadyTick;
                var squish = 1f + (ready ? Mathf.Sin(tick * 0.2f) * 0.06f : 0f);
                _cushions3d[i].localScale = new Vector3(squish, 1f / squish, 1f);
            }
            for (var i = 0; i < _shooters3d.Count && i < run.Shooters.Count; i++)
                _shooters3d[i].localScale = Vector3.one * (run.Shooters[i].Fired ? 0.85f : 1f);
            // the catcher: his mouth on the goal, wide open; chewing on a NOM, sad when the candy is lost
            _mouthRing.position = W(run.Mouth, -0.1f);
            var near = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(run.CandyPos.X, run.CandyPos.Y), new Vector2(run.Mouth.X, run.Mouth.Y)) / 260f);
            _mouthRing.localScale = Vector3.one * (float)run.Physics.MouthR * U * (1f + near * 0.18f + Mathf.Sin(_stageT * 4f) * 0.03f);
            _mouthRing.gameObject.SetActive(_mood == "open");
            if (gb == null) return;
            if (!gb.gameObject.activeSelf) gb.gameObject.SetActive(true);
            var mouthAt = _stage.PlanePoint(Px2View(run.Mouth).x, Px2View(run.Mouth).y, World.MgStage3D.Origin.z + 0.35f);
            gb.transform.position = mouthAt + Vector3.down * (1.5f * 0.535f);
            gb.transform.rotation = Quaternion.Euler(0f, 180f + Mathf.Sin(_stageT * 1.3f) * 6f * (1f - near), 0f);
            if (_mood != _shownMood)
            {
                _shownMood = _mood;
                gb.SetEmotion(_mood == "nom" ? "ecstatic" : _mood == "sad" ? "sad" : near > 0.5f ? "ecstatic" : "happy");
                if (_mood == "nom") gb.Play("celebrate", 0.1f);
                else if (_mood == "sad") gb.Play("refuse", 0.15f);
            }
            var open = _mood == "open" ? 0.75f + 0.25f * near + Mathf.Sin(_stageT * 3f) * 0.05f : _mood == "nom" ? 0.35f + 0.3f * Mathf.Abs(Mathf.Sin(_stageT * 12f)) : 0f;
            gb.SetMouthOpen(open);
        }

        /// <summary>World px → the stage's view px (the stage frames the 960×540 world one to one).</summary>
        private static Vector2 Px2View(V2 p) => new Vector2(p.X, p.Y);

        // ── moments ──

        private void StageEvent(GobnomEvent ev)
        {
            if (ReducedMotion || _stage == null) return;
            var at = W(ev.At, -0.1f);
            switch (ev.Kind)
            {
                case "cut": World.Effects.Puff(at, RopeTan, 8, 0.05f, 1.2f, 0.45f, Vector3.up, 1.2f); break;
                case "jar": World.Effects.Sparkle(at, 0.35f, 16); break;
                case "pop": World.Effects.Puff(at, new Color(0.75f, 0.9f, 1f, 0.9f), 12, 0.08f, 1.6f, 0.45f, Vector3.up, 1.4f); break;
                case "nom":
                    if (_game?.Run != null) World.Effects.Sparkle(W(_game.Run.Mouth, -0.2f), 0.5f, 26);
                    break;
                case "spike":
                case "fell":
                    if (_game?.Run != null) World.Effects.Puff(W(_game.Run.CandyPos, -0.1f), new Color(0.85f, 0.8f, 0.78f, 0.9f), 12, 0.1f, 1.2f, 0.6f, Vector3.up, 1f);
                    break;
            }
        }

        private void DropStage()
        {
            _ropes3d.Clear(); _bubbles3d.Clear(); _jars3d.Clear(); _fans3d.Clear(); _cushions3d.Clear(); _shooters3d.Clear();
            _level = _candy = _candySpin = _mouthRing = null;
            _shownRun = null;
            _shownMood = "";
            _stage?.Destroy();
            _stage = null;
        }
    }
}
