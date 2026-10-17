using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D raised bed of the Gießkannen-Wirbel (port of garden_rush_stage3d.gd): terracotta pots with growing sprouts
    /// and spiky weeds stand in a plank-framed bed of furrowed soil; behind it the lawn, a picket fence, a garden shed, round
    /// trees and clouds. The camera looks down at the bed like the Godot set and every pot stands on the floor under its 2D
    /// tap square (MgStage3D.GroundPoint), so holding a pot still works where it did. Holding brings a real watering can over
    /// the pot that tilts and pours; sprouts droop and brown as their window runs down, a perfect pour opens a flower in the
    /// pot and the player's Gooby — the gardener at the edge of the bed — turns to every pot, cheers and winces. The fill
    /// ring and the wilt arcs stay on the 2D canvas.
    /// </summary>
    public sealed partial class GardenRushScreen
    {
        private sealed class Pot3D
        {
            public Transform Root, Sprout, Stem, Weed, Bloom;
            public Renderer Body;
            public Material Leaf;
            public float Radius, Pop, BloomLeft;
        }

        private World.MgStage3D _stage;
        private readonly List<Pot3D> _pots3d = new List<Pot3D>();
        private readonly List<Material> _ownedMats = new List<Material>();
        private Transform _can, _sprinkler3d;
        private Material _clay, _clayIdle;
        private float _goobyYaw = 140f, _streamEmit, _rainEmit;
        private Vector3 _bedCentre, _bedSize;
        private static Mesh _frustum;

        private static readonly Color LeafFresh = new Color(0.46f, 0.74f, 0.32f);
        private static readonly Color LeafWilt = new Color(0.7f, 0.58f, 0.34f);

        protected override bool StageHasGooby => _stage != null;

        private Vector2 ViewOf(Vector2 field) => new Vector2(field.x * (float)ViewW / Mathf.Max(1f, FieldW), field.y * (float)ViewH / Mathf.Max(1f, FieldH));
        private Vector3 Ground(Vector2 field, float y = 0f) { var v = ViewOf(field); return _stage.GroundPoint(v.x, v.y, y); }

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.66f, 0.83f, 0.98f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 10.5f, -8f), 40f, 52f);
            var root = _stage.Root.transform;
            _clay = World.ArtLibrary.Solid(new Color(0.78f, 0.47f, 0.31f), 0.25f);
            _clayIdle = World.ArtLibrary.Solid(new Color(0.55f, 0.42f, 0.34f), 0.2f);
            // the garden behind the bed: lawn, fence, shed, trees, sky dressing
            _stage.Ground(0f, World.ArtLibrary.Hex("#93C982"), "grass", 60f, 70f, 14f);
            BuildPotsAndBed(root);
            var backZ = _bedCentre.z - World.MgStage3D.Origin.z + _bedSize.z * 0.5f + 1.6f;
            _stage.Fence("picket_fence", 0f, backZ, -9f, 9f, 1.9f, 1f);
            _stage.PlaceScenery("garden_shed", World.MgStage3D.At(-3.6f, 0f, backZ + 2.2f), 200f, 1.2f);
            _stage.Row("hedge_long", 0f, backZ + 1.2f, 1f, 8f, 3, 1f, 180f, 0.2f, 7);
            foreach (var (x, z, s) in new[] { (4.6f, 3.6f, 1.6f), (-7f, 3f, 1.8f), (0.8f, 5.5f, 1.4f), (7.5f, 5f, 1.9f) })
                _stage.PlaceScenery("tree_round", World.MgStage3D.At(x, 0f, backZ + z), x * 17f, s);
            _stage.Row("bush_flowers", 0f, backZ - 0.7f, -6f, 6f, 5, 0.9f, 0f, 0.4f, 11);
            _stage.Clouds(5, 9f, backZ + 18f, 30f, 1.6f, 21);
            _stage.Sun(new Vector3(6f, 12f, backZ + 26f), 1.8f);
            // the watering can that hovers over the held pot and pours
            _can = _stage.PlaceScenery("watering_can", World.MgStage3D.At(0f, -50f, 0f), 0f, 1.3f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnGardener(outfit);
        }

        private void BuildPotsAndBed(Transform root)
        {
            var lo = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var hi = new Vector3(float.MinValue, 0f, float.MinValue);
            var maxR = 0.3f;
            for (var i = 0; i < 8; i++)
            {
                var r = PotRect(i);
                var c = Ground(r.center, 0.25f);
                var e = Ground(r.center + new Vector2(r.width * 0.5f, 0f), 0.25f);
                var radius = Mathf.Clamp(Vector3.Distance(c, e) * 0.95f, 0.3f, 3f);
                maxR = Mathf.Max(maxR, radius);
                var pot = SpawnPot(root, new Vector3(c.x, World.MgStage3D.Origin.y + 0.14f, c.z), radius);
                _pots3d.Add(pot);
                lo = Vector3.Min(lo, c);
                hi = Vector3.Max(hi, c);
            }
            // the raised bed: soil slab with furrows and a plank frame round the pot field
            var w = hi.x - lo.x + maxR * 3.2f;
            var d = hi.z - lo.z + maxR * 3f;
            _bedCentre = new Vector3((lo.x + hi.x) * 0.5f, World.MgStage3D.Origin.y, (lo.z + hi.z) * 0.5f);
            _bedSize = new Vector3(w, 0.3f, d);
            World.ArtLibrary.Box(root, "Soil", _bedCentre + Vector3.up * 0.06f, new Vector3(w, 0.12f, d), World.ArtLibrary.PbrSlab("concrete", new Vector2(w, d), 1.2f, World.ArtLibrary.Hex("#8A6048"), 0.05f, 1.8f), false);
            var furrow = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#74503B"), 0.05f);
            var rows = Mathf.Max(4, Mathf.RoundToInt(d / 0.7f));
            for (var i = 0; i < rows; i++)
            {
                var z = _bedCentre.z - d * 0.46f + d * 0.92f * (i + 0.5f) / rows;
                World.ArtLibrary.Box(root, "Furrow", new Vector3(_bedCentre.x, _bedCentre.y + 0.125f, z), new Vector3(w * 0.94f, 0.03f, 0.12f), furrow, false);
            }
            var plank = World.ArtLibrary.Pbr("wood_planks", 1.2f, World.ArtLibrary.Hex("#E3BC90"), 0.3f);
            foreach (var s in new[] { -1f, 1f })
            {
                World.ArtLibrary.Box(root, "PlankZ", _bedCentre + new Vector3(0f, 0.15f, s * (d * 0.5f + 0.08f)), new Vector3(w + 0.32f, 0.3f, 0.16f), plank);
                World.ArtLibrary.Box(root, "PlankX", _bedCentre + new Vector3(s * (w * 0.5f + 0.08f), 0.15f, 0f), new Vector3(0.16f, 0.3f, d + 0.32f), plank);
            }
            // flower borders beside the pot columns and a red toadstool at the front corner
            for (var i = 0; i < 6; i++)
            {
                var z = _bedCentre.z - d * 0.4f + d * 0.8f * i / 5f;
                _stage.Prop(i % 2 == 0 ? "flower_redA" : "flower_yellowA", new Vector3(_bedCentre.x - w * 0.5f - 0.45f, _bedCentre.y, z), 1.4f, i * 50f);
                _stage.Prop(i % 2 == 1 ? "flower_redA" : "flower_purpleA", new Vector3(_bedCentre.x + w * 0.5f + 0.45f, _bedCentre.y, z), 1.4f, i * 70f);
            }
            _stage.Prop("mushroom_red", new Vector3(_bedCentre.x + w * 0.5f - 0.2f, _bedCentre.y + 0.12f, _bedCentre.z - d * 0.5f + 0.3f), 2.4f, 30f);
            _sprinkler3d = World.ArtLibrary.Cylinder(root, "Sprinkler", new Vector3(_bedCentre.x + w * 0.5f - 0.35f, _bedCentre.y + 0.3f, _bedCentre.z + d * 0.5f - 0.35f), 0.12f, 0.36f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#7FB8FF"), 0.6f)).transform;
            World.ArtLibrary.Box(_sprinkler3d, "Arm", _sprinkler3d.position + Vector3.up * 0.2f, new Vector3(0.7f, 0.04f, 0.06f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFD166"), 0.5f), false);
            _sprinkler3d.gameObject.SetActive(false);
        }

        private Pot3D SpawnPot(Transform root, Vector3 at, float radius)
        {
            var p = new Pot3D { Radius = radius, Root = new GameObject("Pot").transform };
            p.Root.SetParent(root, false);
            p.Root.position = at;
            p.Root.localScale = Vector3.one * radius;
            var body = new GameObject("Body");
            body.transform.SetParent(p.Root, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(1f, 0.5f, 1f);
            body.AddComponent<MeshFilter>().sharedMesh = Frustum();
            p.Body = body.AddComponent<MeshRenderer>();
            p.Body.sharedMaterial = _clay;
            var rim = World.ArtLibrary.Cylinder(p.Root, "Rim", at + Vector3.up * 0.48f * radius, 0.66f * radius, 0.08f * radius, World.ArtLibrary.Solid(new Color(0.7f, 0.4f, 0.27f), 0.25f), false);
            rim.transform.SetParent(p.Root, true);
            var soil = World.ArtLibrary.Cylinder(p.Root, "PotSoil", at + Vector3.up * 0.47f * radius, 0.58f * radius, 0.04f * radius, World.ArtLibrary.Solid(new Color(0.32f, 0.22f, 0.15f), 0.05f), false);
            soil.transform.SetParent(p.Root, true);
            // the sprout: stem with two leaves and a bud, tinted live with the window
            p.Leaf = new Material(World.ArtLibrary.Solid(LeafFresh, 0.3f));
            _ownedMats.Add(p.Leaf);
            p.Sprout = new GameObject("Sprout").transform;
            p.Sprout.SetParent(p.Root, false);
            p.Sprout.localPosition = new Vector3(0f, 0.5f, 0f);
            p.Stem = new GameObject("Stem").transform;
            p.Stem.SetParent(p.Sprout, false);
            var stem = World.ArtLibrary.Cylinder(p.Stem, "StemMesh", Vector3.zero, 0.05f, 0.5f, p.Leaf, false).transform;
            stem.localPosition = new Vector3(0f, 0.25f, 0f);
            stem.localScale = new Vector3(0.09f, 0.25f, 0.09f);
            foreach (var side in new[] { -1f, 1f })
            {
                var leaf = World.ArtLibrary.Sphere(p.Stem, "Leaf", Vector3.zero, 0.5f, p.Leaf, false).transform;
                leaf.localPosition = new Vector3(side * 0.2f, 0.42f, 0f);
                leaf.localScale = new Vector3(0.36f, 0.1f, 0.2f);
                leaf.localRotation = Quaternion.Euler(0f, 0f, side * 22f);
            }
            var bud = World.ArtLibrary.Sphere(p.Stem, "Bud", Vector3.zero, 0.5f, World.ArtLibrary.Solid(new Color(1f, 0.8f, 0.86f), 0.3f), false).transform;
            bud.localPosition = new Vector3(0f, 0.55f, 0f);
            bud.localScale = Vector3.one * 0.12f;
            // the weed: dark spikes fanning out of the soil
            p.Weed = new GameObject("Weed").transform;
            p.Weed.SetParent(p.Root, false);
            p.Weed.localPosition = new Vector3(0f, 0.5f, 0f);
            var spike = World.ArtLibrary.Solid(new Color(0.19f, 0.36f, 0.17f), 0.2f);
            for (var k = 0; k < 7; k++)
            {
                var a = k * Mathf.PI * 2f / 7f;
                var cone = _stage.Cone(Vector3.zero, 0.07f, 0.55f + (k % 2) * 0.15f, spike).transform;
                cone.SetParent(p.Weed, false);
                cone.localPosition = new Vector3(Mathf.Cos(a) * 0.12f, 0f, Mathf.Sin(a) * 0.12f);
                cone.localRotation = Quaternion.Euler(Mathf.Sin(a) * 30f, 0f, -Mathf.Cos(a) * 30f);
            }
            // the reward bloom (hidden until a perfect pour)
            p.Bloom = _stage.Prop(radius > 0.45f ? "flower_redA" : "flower_yellowA", at, 1f);
            p.Bloom.SetParent(p.Root, true);
            p.Bloom.localPosition = new Vector3(0f, 0.55f, 0f);
            p.Bloom.localScale = Vector3.zero;
            return p;
        }

        /// <summary>A tapered pot body: unit height, top radius 0.62, bottom 0.45, closed bottom (flat shaded sides).</summary>
        private static Mesh Frustum()
        {
            if (_frustum != null) return _frustum;
            const int n = 16;
            const float top = 0.62f, bottom = 0.45f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (var i = 0; i < n; i++)
            {
                var a0 = i * Mathf.PI * 2f / n;
                var a1 = (i + 1) * Mathf.PI * 2f / n;
                var b0 = new Vector3(Mathf.Cos(a0) * bottom, 0f, Mathf.Sin(a0) * bottom);
                var b1 = new Vector3(Mathf.Cos(a1) * bottom, 0f, Mathf.Sin(a1) * bottom);
                var t0 = new Vector3(Mathf.Cos(a0) * top, 1f, Mathf.Sin(a0) * top);
                var t1 = new Vector3(Mathf.Cos(a1) * top, 1f, Mathf.Sin(a1) * top);
                var b = verts.Count;
                verts.Add(b0); verts.Add(t0); verts.Add(t1); verts.Add(b1);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                b = verts.Count;
                verts.Add(Vector3.zero); verts.Add(b1); verts.Add(b0);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            _frustum = new Mesh { name = "PotFrustum" };
            _frustum.SetVertices(verts);
            _frustum.SetTriangles(tris, 0);
            _frustum.RecalculateNormals();
            _frustum.RecalculateBounds();
            return _frustum;
        }

        private async System.Threading.Tasks.Task SpawnGardener(Dictionary<string, string> outfit)
        {
            // the gardener on the lawn at the front right corner of the bed, small enough to leave the front pots free
            var at = _stage.GroundPoint(ViewW * 0.86, ViewH * 0.985, 0f);
            var g = await _stage.SpawnGoobyAsync(at, 1.05f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            g.SetEmotion("happy");
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            for (var i = 0; i < _pots3d.Count; i++)
            {
                var p = _pots3d[i];
                var active = i < g.ActivePots && i < g.Pots.Count;
                p.Body.sharedMaterial = active ? _clay : _clayIdle;
                var pot = active ? g.Pots[i] : null;
                var state = pot?.State;
                var sprout = state == GardenRushGame.Sprout;
                var weed = state == GardenRushGame.Weed;
                p.Sprout.gameObject.SetActive(sprout);
                p.Weed.gameObject.SetActive(weed);
                if (sprout)
                {
                    var frac = (float)pot.Fraction;
                    var c = Color.Lerp(LeafWilt, LeafFresh, Mathf.Clamp01(frac * 1.4f));
                    p.Leaf.color = c;
                    if (p.Leaf.HasProperty("_BaseColor")) p.Leaf.SetColor("_BaseColor", c);
                    var droop = (1f - frac) * 55f;
                    var sway = ReducedMotion ? 0f : Mathf.Sin(_show * 2f + i) * 5f;
                    p.Stem.localRotation = Quaternion.Euler(0f, 0f, -droop + sway);
                    p.Sprout.localScale = Vector3.one * (g.Holding && g.HoldIndex == i ? 1.12f : 1f) * (0.75f + 0.25f * Mathf.Clamp01(frac + 0.4f));
                }
                else if (weed)
                {
                    p.Weed.localRotation = Quaternion.Euler(0f, _show * 45f, 0f);
                    p.Weed.localScale = Vector3.one * (pot.Grown ? 1.25f : 1f);
                }
                // springy pop on water/weed/wilt and the reward bloom opening and closing again
                p.Pop = Mathf.Max(0f, p.Pop - dt * 4.5f);
                var bounce = Mathf.Sin(p.Pop * Mathf.PI) * 0.16f;
                p.Root.localScale = Vector3.one * p.Radius * (1f + (ReducedMotion ? 0f : bounce));
                if (p.BloomLeft > 0f)
                {
                    p.BloomLeft = Mathf.Max(0f, p.BloomLeft - dt);
                    var open = Mathf.Clamp01((1.4f - p.BloomLeft) / 0.2f);
                    var fade = Mathf.Clamp01(p.BloomLeft / 0.3f);
                    p.Bloom.localScale = Vector3.one * 1.6f * open * fade;
                    p.Bloom.localRotation = Quaternion.Euler(0f, _show * 60f, 0f);
                }
            }
            // the can over the held pot, tilted and pouring
            if (g.Holding && g.HoldIndex >= 0 && g.HoldIndex < _pots3d.Count)
            {
                var p = _pots3d[g.HoldIndex];
                var top = p.Root.position + Vector3.up * (p.Radius * 0.5f);
                _can.position = top + new Vector3(-p.Radius * 1.1f, p.Radius * 1.5f + 0.3f, -p.Radius * 0.2f);
                _can.rotation = Quaternion.Euler(0f, 90f, -32f + (ReducedMotion ? 0f : Mathf.Sin(_show * 9f) * 3f));
                _streamEmit -= dt;
                if (_streamEmit <= 0f)
                {
                    _streamEmit = 0.06f;
                    World.Effects.Puff(_can.position + new Vector3(p.Radius * 0.8f, -0.1f, 0f), new Color(0.6f, 0.82f, 0.98f, 0.9f), 3, 0.08f, 2.4f, 0.35f, new Vector3(0.45f, -1f, 0f), 0.15f);
                }
            }
            else _can.position = World.MgStage3D.At(0f, -50f, 0f);
            // the sprinkler waits in its corner once it is ready; while it rains, drops fall over the whole bed
            _sprinkler3d.gameObject.SetActive(g.SprinklerAvailable || _sprinklerRain > 0f);
            if (_sprinkler3d.gameObject.activeSelf) _sprinkler3d.rotation = Quaternion.Euler(0f, _show * 220f, 0f);
            if (_sprinklerRain > 0f && !ReducedMotion)
            {
                _rainEmit -= dt;
                if (_rainEmit <= 0f)
                {
                    _rainEmit = 0.05f;
                    var at = _bedCentre + new Vector3((Random.value - 0.5f) * _bedSize.x, 2.6f, (Random.value - 0.5f) * _bedSize.z);
                    World.Effects.Puff(at, new Color(0.6f, 0.82f, 0.98f, 0.85f), 4, 0.07f, 4f, 0.6f, Vector3.down, 0.1f);
                }
            }
            // the gardener turns to the held pot
            var gb = _stage.Gooby;
            if (gb == null) return;
            var want = 140f;
            if (g.Holding && g.HoldIndex >= 0 && g.HoldIndex < _pots3d.Count)
            {
                var to = _pots3d[g.HoldIndex].Root.position - gb.transform.position;
                want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            }
            _goobyYaw = Mathf.LerpAngle(_goobyYaw, want, Mathf.Min(1f, dt * 8f));
            gb.transform.rotation = Quaternion.Euler(0f, _goobyYaw, ReducedMotion ? 0f : Mathf.Sin(_show * 2.2f) * 1.7f);
        }

        private void StageWatered(int index, bool perfect)
        {
            if (_stage == null || index < 0 || index >= _pots3d.Count) return;
            var p = _pots3d[index];
            p.Pop = 1f;
            var at = p.Root.position + Vector3.up * p.Radius * 0.8f;
            if (!ReducedMotion) World.Effects.Puff(at, new Color(0.6f, 0.84f, 0.95f, 0.9f), 10, 0.1f, 1.6f, 0.5f, Vector3.up, 0.9f);
            var gb = _stage.Gooby;
            if (perfect)
            {
                p.BloomLeft = 1.4f;
                if (!ReducedMotion) World.Effects.Sparkle(at + Vector3.up * 0.3f, p.Radius, 18);
                if (gb != null) { gb.SetEmotion("ecstatic"); gb.Play("hop", 0.1f); }
            }
            else gb?.SetEmotion("happy");
        }

        private void StageWeed(int index)
        {
            if (_stage == null || index < 0 || index >= _pots3d.Count) return;
            _pots3d[index].Pop = 1f;
            if (!ReducedMotion) World.Effects.Puff(_pots3d[index].Root.position + Vector3.up * 0.4f, new Color(0.3f, 0.5f, 0.25f, 0.9f), 10, 0.12f, 1.4f, 0.6f, Vector3.up, 1f);
            _stage.Gooby?.SetEmotion("dizzy");
        }

        private void StageWilted(int index)
        {
            if (_stage == null || index < 0 || index >= _pots3d.Count) return;
            if (!ReducedMotion) World.Effects.Puff(_pots3d[index].Root.position + Vector3.up * 0.4f, new Color(0.65f, 0.52f, 0.3f, 0.9f), 8, 0.12f, 1f, 0.7f, Vector3.up, 1f);
            _stage.Gooby?.SetEmotion("scared");
        }

        private void StageSprinkler()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("celebrate", 0.1f);
            gb.SetEmotion("ecstatic");
        }

        private void DropStage()
        {
            _pots3d.Clear();
            _stage?.Destroy();
            _stage = null;
            foreach (var m in _ownedMats) if (m != null) Object.Destroy(m);
            _ownedMats.Clear();
        }
    }
}
