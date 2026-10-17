using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D cake workshop of the Tortenwerkstatt (after purble_place_stage3d.gd, kept on the Unity layout's upright
    /// belt): a steep camera over a bakery table; the conveyor's frame, rubber and moving slats are the 2D belt rectangle
    /// cast onto the table, so every pan stands where the engine has it and the canvas's catch window lies on it. Beside
    /// the belt the dispensers (sponge, icing, toppings, candles) with their coloured caps — the needed one ringed in gold,
    /// a locked one blinking white — shoot real drops onto the pans; the open oven tunnel glows while a pan bakes; the mint
    /// shipping hatch waits at the end. Every pan is a real layered cake in its round, square or heart tin: sponge (tinted by
    /// its bake), icing, cherry, sprinkles or berries and flickering candles.
    /// </summary>
    public sealed partial class PurblePlaceScreen
    {
        private World.MgStage3D _stage;
        private readonly Dictionary<int, (string sig, Transform node)> _pans3d = new Dictionary<int, (string, Transform)>();
        private readonly Dictionary<string, (Transform cap, Transform ring, Transform lockLight, Vector3 spout)> _nozzles3d =
            new Dictionary<string, (Transform, Transform, Transform, Vector3)>();
        private readonly List<Transform> _slats = new List<Transform>();
        private readonly List<Transform> _drops3d = new List<Transform>();
        private readonly List<Transform> _splats3d = new List<Transform>();
        private readonly HashSet<int> _livePans = new HashSet<int>();
        private readonly List<int> _deadPans = new List<int>();
        private Material _ovenGlow;
        private Transform _ovenHeat;
        private float _stageT;

        private const float CamPitch = 76f, CamFov = 44f, SlatGapPx = 22f;
        private static readonly Vector3 CamPos = new Vector3(0f, 9f, -2.24f);

        private static float VBeltX => (float)ViewW * BeltXPct;
        private static float VBeltHalfW => (float)ViewW * 0.11f;
        private static float VYOf(double s) => (float)ViewH * (BeltTopPct + (BeltBottomPct - BeltTopPct) * (float)(s / PurblePlace.Cake.BeltLengthM));

        private Vector3 G(float vx, float vy, float lift = 0f) => _stage.GroundPoint(vx, vy) + Vector3.up * lift;

        /// <summary>The belt's width in metres at a belt position (it widens a little with distance, constant on screen).</summary>
        private float BeltMetres(double s) => Vector3.Distance(G(VBeltX - VBeltHalfW, VYOf(s)), G(VBeltX + VBeltHalfW, VYOf(s)));

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, MgScenery.Hex(0xF7E6DA));
            _stage.Bind(Field);
            _stage.Aim(CamPos, CamPitch, CamFov);
            var root = _stage.Root.transform;
            var tune = PurblePlace.Cake;
            World.ArtLibrary.Box(root, "Table", World.MgStage3D.At(0f, -0.05f, 3f), new Vector3(16f, 0.1f, 22f),
                World.ArtLibrary.PbrSlab("wood_floor", new Vector2(16f, 22f), 1.6f, MgScenery.Hex(0xF2D3B3), 0.35f), false);
            var top = VYOf(0.0) - 14f; var bottom = VYOf(tune.BeltLengthM) + 14f;
            Prism(root, "BeltFrame", VBeltX - VBeltHalfW - 6f, top, VBeltX + VBeltHalfW + 6f, bottom, 0f, 0.1f, World.ArtLibrary.Solid(RollerC, 0.55f));
            Prism(root, "Belt", VBeltX - VBeltHalfW, top, VBeltX + VBeltHalfW, bottom, 0.1f, 0.12f, World.ArtLibrary.Solid(BeltC, 0.25f));
            var slatMat = World.ArtLibrary.Solid(BeltDark, 0.25f);
            for (var i = 0; i < 40; i++)
            {
                var slat = World.ArtLibrary.Box(root, "Slat", Vector3.zero, Vector3.one, slatMat, false).transform;
                _slats.Add(slat);
            }
            foreach (var y in new[] { top, bottom })
            {
                var a = G(VBeltX - VBeltHalfW - 6f, y, 0.06f); var b = G(VBeltX + VBeltHalfW + 6f, y, 0.06f);
                var roller = World.ArtLibrary.Cylinder(root, "Roller", (a + b) * 0.5f, 0.07f, Vector3.Distance(a, b) + 0.1f, World.ArtLibrary.Solid(new Color(0.75f, 0.76f, 0.8f), 0.8f)).transform;
                roller.rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            }
            BuildOven(root, tune);
            BuildHatch(root, tune);
            BuildNozzles(root);
        }

        /// <summary>A closed slab whose top is the screen rectangle cast onto the table, between heights y0 and y1.</summary>
        private void Prism(Transform parent, string name, float x0, float vy0, float x1, float vy1, float y0, float y1, Material mat)
        {
            var o = _stage.Root.transform.position;
            Vector3 P(float vx, float vy, float y) => G(vx, vy) - o + Vector3.up * y;
            var v = new[] { P(x0, vy0, y1), P(x1, vy0, y1), P(x1, vy1, y1), P(x0, vy1, y1), P(x0, vy0, y0), P(x1, vy0, y0), P(x1, vy1, y0), P(x0, vy1, y0) };
            var faces = new[] { (0, 1, 2, 3), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0) };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var (a, b, c, d) in faces)
            {
                var i = verts.Count;
                verts.Add(v[a]); verts.Add(v[b]); verts.Add(v[c]); verts.Add(v[d]);
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = o;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void BuildOven(Transform root, PurblePlace.Tune tune)
        {
            var oy0 = VYOf(tune.OvenStartS); var oy1 = VYOf(tune.OvenEndS);
            var brick = World.ArtLibrary.Solid(OvenC, 0.25f);
            Prism(root, "OvenWallL", VBeltX - VBeltHalfW - 16f, oy0, VBeltX - VBeltHalfW - 4f, oy1, 0f, 0.42f, brick);
            Prism(root, "OvenWallR", VBeltX + VBeltHalfW + 4f, oy0, VBeltX + VBeltHalfW + 16f, oy1, 0f, 0.42f, brick);
            var rim = World.ArtLibrary.Solid(OvenDark, 0.3f);
            Prism(root, "OvenArchTop", VBeltX - VBeltHalfW - 16f, oy0 - 7f, VBeltX + VBeltHalfW + 16f, oy0, 0.32f, 0.46f, rim);
            Prism(root, "OvenArchBottom", VBeltX - VBeltHalfW - 16f, oy1, VBeltX + VBeltHalfW + 16f, oy1 + 7f, 0.32f, 0.46f, rim);
            // heating bars along the inner walls, glowing while a pan bakes
            _ovenGlow = new Material(World.ArtLibrary.Glow(new Color(0.6f, 0.25f, 0.12f)));
            _ovenHeat = new GameObject("OvenHeat").transform;
            _ovenHeat.SetParent(root, false);
            foreach (var x in new[] { VBeltX - VBeltHalfW - 3f, VBeltX + VBeltHalfW + 3f })
                for (var k = 0; k < 4; k++)
                {
                    var y = Mathf.Lerp(oy0 + 8f, oy1 - 8f, k / 3f);
                    var bar = World.ArtLibrary.Box(_ovenHeat, "HeatBar", G(x, y, 0.24f), new Vector3(0.05f, 0.05f, 0.05f), _ovenGlow, false).transform;
                    bar.localScale = new Vector3(0.06f, 0.06f, Vector3.Distance(G(x, y - 6f), G(x, y + 6f)));
                }
            var chimney = World.ArtLibrary.Cylinder(root, "Chimney", G(VBeltX + VBeltHalfW + 10f, oy0 + 6f, 0.62f), 0.07f, 0.4f, World.ArtLibrary.Solid(new Color(0.6f, 0.62f, 0.66f), 0.7f));
            chimney.name = "Chimney";
        }

        private void BuildHatch(Transform root, PurblePlace.Tune tune)
        {
            var hy = VYOf(tune.ShipS);
            var hh = (VYOf(1.0) - VYOf(0.0)) * (float)tune.ShipHalfM;
            var mint = World.ArtLibrary.Solid(MgScenery.Hex(0x9FE0B9), 0.4f);
            Prism(root, "HatchL", VBeltX - VBeltHalfW - 10f, hy - hh, VBeltX - VBeltHalfW - 4f, hy + hh, 0f, 0.3f, mint);
            Prism(root, "HatchR", VBeltX + VBeltHalfW + 4f, hy - hh, VBeltX + VBeltHalfW + 10f, hy + hh, 0f, 0.3f, mint);
            Prism(root, "HatchTop", VBeltX - VBeltHalfW - 10f, hy - hh - 5f, VBeltX + VBeltHalfW + 10f, hy - hh, 0.26f, 0.34f, mint);
            var box = World.ArtLibrary.Box(root, "ParcelBox", G(VBeltX + VBeltHalfW + 34f, hy + 4f, 0.18f), new Vector3(0.4f, 0.36f, 0.34f), World.ArtLibrary.Solid(MgScenery.Hex(0xD7A86E), 0.2f)).transform;
            box.rotation = Quaternion.Euler(0f, 12f, 0f);
            World.ArtLibrary.Box(box, "Tape", box.position + Vector3.up * 0.181f, new Vector3(0.08f, 0.005f, 0.34f), World.ArtLibrary.Solid(MgScenery.Hex(0xF2E3C4), 0.3f), false).transform.localScale = new Vector3(0.2f, 0.02f, 1.01f);
        }

        private void BuildNozzles(Transform root)
        {
            var body = World.ArtLibrary.Solid(MgScenery.Hex(0xE9D7C8), 0.4f);
            var steel = World.ArtLibrary.Solid(new Color(0.78f, 0.8f, 0.84f), 0.8f);
            foreach (var st in PurblePlace.Stations)
            {
                if (!st.Drop) continue;
                var y = VYOf(st.S);
                var at = G(VBeltX - VBeltHalfW - 34f, y, 0f);
                World.ArtLibrary.Cylinder(root, "Dispenser", at + Vector3.up * 0.28f, 0.13f, 0.56f, body);
                var cap = World.ArtLibrary.Sphere(root, "Cap", at + Vector3.up * 0.6f, 0.14f, World.ArtLibrary.Solid(StationColor(st), 0.45f)).transform;
                var spoutEnd = G(VBeltX - VBeltHalfW + 4f, y, 0.42f);
                var mid = ((at + Vector3.up * 0.42f) + spoutEnd) * 0.5f;
                var spout = World.ArtLibrary.Cylinder(root, "Spout", mid, 0.04f, Vector3.Distance(at + Vector3.up * 0.42f, spoutEnd), steel).transform;
                spout.rotation = Quaternion.FromToRotation(Vector3.up, (spoutEnd - (at + Vector3.up * 0.42f)).normalized);
                var ring = _stage.Torus(root, "NeedRing", at + Vector3.up * 0.62f, 0.2f, 0.03f, World.ArtLibrary.Glow(new Color(1.25f, 0.95f, 0.35f))).transform;
                ring.gameObject.SetActive(false);
                var lockLight = World.ArtLibrary.Sphere(root, "Lock", at + Vector3.up * 0.76f, 0.05f, World.ArtLibrary.Glow(Color.white), false).transform;
                lockLight.gameObject.SetActive(false);
                _nozzles3d[st.Id] = (cap, ring, lockLight, spoutEnd);
            }
        }

        // ── the cakes ──

        private static string Signature(PurblePlace.Pan pan) =>
            pan.Shape + "|" + pan.Sponge + "|" + pan.Bake + "|" + pan.Icing + "|" + pan.Topping + "|" + pan.Candles;

        private static Color SpongeColour(PurblePlace.Pan pan)
        {
            var sponge = Hex(PurblePlace.SpongeHex[pan.Sponge]);
            if (pan.Bake == "perfect") return Color.Lerp(sponge, MgScenery.Hex(0xE0A050), 0.25f);
            if (pan.Bake == "over") return Color.Lerp(sponge, MgScenery.Hex(0x9A6030), 0.35f);
            if (pan.Bake == "singed") return Color.Lerp(sponge, MgScenery.Hex(0x3A2A20), 0.7f);
            return sponge;
        }

        /// <summary>One layer of the cake in the pan's shape (a round disc, a square slab or a heart of two discs and a turned block).</summary>
        private void Layer(Transform parent, string shape, float size, float y, float height, Material mat, bool shadows)
        {
            switch (shape)
            {
                case "square":
                    World.ArtLibrary.Box(parent, "Layer", parent.position, new Vector3(size * 1.6f, height, size * 1.6f), mat, shadows).transform.localPosition = new Vector3(0f, y, 0f);
                    break;
                case "heart":
                    foreach (var s in new[] { -1f, 1f })
                    {
                        var lobe = World.ArtLibrary.Cylinder(parent, "Lobe", parent.position, size * 0.52f, height, mat, shadows).transform;
                        lobe.localPosition = new Vector3(s * size * 0.36f, y, size * 0.28f);
                    }
                    var tip = World.ArtLibrary.Box(parent, "Tip", parent.position, new Vector3(size * 1.04f, height, size * 1.04f), mat, shadows).transform;
                    tip.localPosition = new Vector3(0f, y, -size * 0.12f);
                    tip.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    break;
                default:
                    World.ArtLibrary.Cylinder(parent, "Layer", parent.position, size, height, mat, shadows).transform.localPosition = new Vector3(0f, y, 0f);
                    break;
            }
        }

        private Transform BuildCake(PurblePlace.Pan pan)
        {
            var node = new GameObject("Pan" + pan.Id).transform;
            node.SetParent(_stage.Root.transform, false);
            Layer(node, pan.Shape, 0.45f, 0.04f, 0.08f, World.ArtLibrary.Solid(PanC, 0.75f), true);
            Layer(node, pan.Shape, 0.42f, 0.085f, 0.02f, World.ArtLibrary.Solid(PanDark, 0.6f), false);
            if (pan.Sponge != null) Layer(node, pan.Shape, 0.4f, 0.17f, 0.18f, World.ArtLibrary.Solid(SpongeColour(pan), 0.15f), true);
            if (pan.Icing != null) Layer(node, pan.Shape, 0.36f, 0.27f, 0.04f, World.ArtLibrary.Solid(Hex(PurblePlace.IcingHex[pan.Icing]), 0.55f), false);
            var topY = pan.Icing != null ? 0.3f : pan.Sponge != null ? 0.27f : 0.1f;
            if (pan.Topping != null)
            {
                var colour = pan.Topping == "cherry" ? RedC : pan.Topping == "berries" ? MgScenery.Hex(0x7C6FD8) : MgScenery.Hex(0xFFD166);
                var mat = World.ArtLibrary.Solid(colour, 0.6f);
                if (pan.Topping == "cherry")
                {
                    World.ArtLibrary.Sphere(node, "Cherry", node.position, 0.08f, mat).transform.localPosition = new Vector3(0f, topY + 0.07f, 0f);
                }
                else
                {
                    var n = pan.Topping == "sprinkles" ? 9 : 5;
                    for (var i = 0; i < n; i++)
                    {
                        var a = i * Mathf.PI * 2f / n + 0.4f;
                        var r = pan.Topping == "sprinkles" ? 0.2f + (i % 3) * 0.04f : 0.18f;
                        var bit = World.ArtLibrary.Sphere(node, "Bit", node.position, pan.Topping == "sprinkles" ? 0.025f : 0.05f, pan.Topping == "sprinkles" ? World.ArtLibrary.Solid(new[] { RedC, MgScenery.Hex(0x7CCB86), MgScenery.Hex(0x7C6FD8), MgScenery.Hex(0xFFD166) }[i % 4], 0.5f) : mat, false).transform;
                        bit.localPosition = new Vector3(Mathf.Cos(a) * r, topY + 0.03f, Mathf.Sin(a) * r);
                        if (pan.Topping == "sprinkles") bit.localScale = new Vector3(0.09f, 0.025f, 0.025f);
                    }
                }
            }
            for (var i = 0; i < pan.Candles; i++)
            {
                var x = -0.24f + i * 0.16f;
                World.ArtLibrary.Cylinder(node, "Candle", node.position, 0.022f, 0.2f, World.ArtLibrary.Solid(CandleC, 0.3f), false).transform.localPosition = new Vector3(x, topY + 0.1f, 0.12f);
                var flame = World.ArtLibrary.Sphere(node, "Flame", node.position, 0.035f, World.ArtLibrary.Glow(new Color(1.3f, 0.8f, 0.3f)), false).transform;
                flame.localPosition = new Vector3(x, topY + 0.24f, 0.12f);
                flame.localScale = new Vector3(0.05f, 0.08f, 0.05f);
                flame.name = "Flame";
            }
            if (pan.Bake == "perfect")
            {
                var halo = _stage.Torus(node, "Perfect", node.position, 0.52f, 0.02f, World.ArtLibrary.Glow(new Color(1.2f, 0.95f, 0.4f))).transform;
                halo.localPosition = new Vector3(0f, 0.1f, 0f);
            }
            return node;
        }

        // ── every frame ──

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            var line = _game.Line;
            var tune = _game.Tune;
            // the slats run with the belt
            var top = VYOf(0.0) - 14f; var bottom = VYOf(tune.BeltLengthM) + 14f;
            var pxPerM = VYOf(1.0) - VYOf(0.0);
            var y0 = top + (float)Mod(_travel * pxPerM, SlatGapPx);
            for (var i = 0; i < _slats.Count; i++)
            {
                var y = y0 + i * SlatGapPx;
                var slat = _slats[i];
                var on = y < bottom;
                if (slat.gameObject.activeSelf != on) slat.gameObject.SetActive(on);
                if (!on) continue;
                var a = G(VBeltX - VBeltHalfW, y, 0.125f); var b = G(VBeltX + VBeltHalfW, y, 0.125f);
                slat.position = (a + b) * 0.5f;
                slat.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
                slat.localScale = new Vector3(Vector3.Distance(G(VBeltX, y), G(VBeltX, y + 3f)), 0.008f, Vector3.Distance(a, b));
            }
            // the needed dispenser wears the gold ring, locked ones blink white
            var needSt = PurblePlace.StationById(_need);
            foreach (var kv in _nozzles3d)
            {
                var needed = needSt != null && needSt.Id == kv.Key;
                kv.Value.ring.gameObject.SetActive(needed);
                if (needed) kv.Value.ring.localScale = Vector3.one * 0.2f * (1f + 0.08f * Mathf.Sin(_stageT * 6f));
                var locked = line.Lockouts.TryGetValue(kv.Key, out var lo) && lo > 0.0;
                kv.Value.lockLight.gameObject.SetActive(locked && Mathf.Sin(_stageT * 20f) > 0f);
            }
            // the oven glows while it holds a pan
            var baking = OvenPan(line) != null;
            var glow = baking ? new Color(1.6f, 0.65f, 0.25f) * (0.85f + 0.15f * Mathf.Sin(_stageT * 9f)) : new Color(0.5f, 0.22f, 0.12f);
            _ovenGlow.color = glow;
            if (_ovenGlow.HasProperty("_BaseColor")) _ovenGlow.SetColor("_BaseColor", glow);
            if (_ovenGlow.HasProperty("_EmissionColor")) _ovenGlow.SetColor("_EmissionColor", glow);
            // the cakes: rebuilt when their recipe changed, moved with the belt
            _livePans.Clear();
            foreach (var pan in line.Pans)
            {
                _livePans.Add(pan.Id);
                var sig = Signature(pan);
                if (!_pans3d.TryGetValue(pan.Id, out var entry) || entry.sig != sig)
                {
                    if (entry.node != null) Object.Destroy(entry.node.gameObject);
                    entry = (sig, BuildCake(pan));
                    _pans3d[pan.Id] = entry;
                }
                var s = BeltMetres(pan.S) * 0.82f / 0.9f;
                entry.node.position = G(VBeltX, VYOf(pan.S), 0.12f);
                entry.node.localScale = Vector3.one * s;
                if (pan.Candles > 0 && !ReducedMotion)
                    foreach (Transform c in entry.node)
                        if (c.name == "Flame") c.localScale = new Vector3(0.05f, 0.08f * (0.85f + 0.2f * Mathf.Sin(_stageT * 17f + c.localPosition.x * 40f)), 0.05f);
            }
            _deadPans.Clear();
            foreach (var kv in _pans3d) if (!_livePans.Contains(kv.Key)) _deadPans.Add(kv.Key);
            foreach (var id in _deadPans) { Object.Destroy(_pans3d[id].node.gameObject); _pans3d.Remove(id); }
            // drops arc from the spout onto the belt
            var used = 0;
            foreach (var d in line.Drops)
            {
                var st = PurblePlace.StationById(d.Station);
                if (st == null || !_nozzles3d.TryGetValue(st.Id, out var nz)) continue;
                var u = Mathf.Clamp01((float)((line.T - d.FiredAt) / tune.FallSec));
                var to = G(VBeltX, VYOf(d.NozzleS), 0.32f);
                var at = Vector3.Lerp(nz.spout, to, u) + Vector3.up * (0.25f * Mathf.Sin(u * Mathf.PI));
                Transform drop;
                if (used < _drops3d.Count) drop = _drops3d[used];
                else
                {
                    drop = World.ArtLibrary.Sphere(_stage.Root.transform, "Drop", at, 0.07f, World.ArtLibrary.Solid(Color.white, 0.5f), false).transform;
                    _drops3d.Add(drop);
                }
                drop.gameObject.SetActive(true);
                drop.position = at;
                var r = drop.GetComponent<Renderer>();
                var mat = World.ArtLibrary.Solid(StationColor(st), 0.5f);
                if (r.sharedMaterial != mat) r.sharedMaterial = mat;
                used++;
            }
            for (var i = used; i < _drops3d.Count; i++) if (_drops3d[i].gameObject.activeSelf) _drops3d[i].gameObject.SetActive(false);
            // splats on the belt fade out
            var splatsUsed = 0;
            foreach (var sp in line.Splats)
            {
                Transform splat;
                if (splatsUsed < _splats3d.Count) splat = _splats3d[splatsUsed];
                else
                {
                    splat = World.ArtLibrary.Sphere(_stage.Root.transform, "Splat", Vector3.zero, 0.5f, World.ArtLibrary.Solid(new Color(0.85f, 0.6f, 0.45f), 0.3f), false).transform;
                    _splats3d.Add(splat);
                }
                var k = Mathf.Clamp01((float)(sp.Ttl / tune.SplatTtlSec));
                splat.gameObject.SetActive(true);
                splat.position = G(VBeltX + 6f, VYOf(sp.S), 0.13f);
                splat.localScale = new Vector3(0.36f, 0.02f, 0.2f) * (0.4f + 0.6f * k);
                splatsUsed++;
            }
            for (var i = splatsUsed; i < _splats3d.Count; i++) if (_splats3d[i].gameObject.activeSelf) _splats3d[i].gameObject.SetActive(false);
        }

        private void StageServed(bool perfect)
        {
            if (_stage == null || ReducedMotion) return;
            var at = G(VBeltX, VYOf(PurblePlace.Cake.ShipS), 0.4f);
            if (perfect) World.Effects.Sparkle(at, 0.4f, 22);
            else World.Effects.Puff(at, new Color(0.95f, 0.9f, 0.8f, 0.9f), 10, 0.1f, 1.2f, 0.5f, Vector3.up, 1f);
        }

        private void DropStage()
        {
            _pans3d.Clear();
            _nozzles3d.Clear();
            _slats.Clear();
            _drops3d.Clear();
            _splats3d.Clear();
            if (_ovenGlow != null) Object.Destroy(_ovenGlow);
            _ovenGlow = null;
            _ovenHeat = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
