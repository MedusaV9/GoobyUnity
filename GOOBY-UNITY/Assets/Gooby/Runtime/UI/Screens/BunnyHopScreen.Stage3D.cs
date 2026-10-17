using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D hedge meadow of the Hasenhüpfer (port of bunny_hop_stage3d.gd): the player's Gooby flutters through leafy
    /// hedge gates in front of parallax hills, a poplar row and drifting clouds. The next gate wears golden crowns and a
    /// pulsing gold ring in its gap (the timing window), coins spin in the gaps, a gust blows streaks across the frame and
    /// a crash sends him tumbling into the grass. The default front camera frames the engine's plane exactly, so every gate
    /// stands where its hitbox is; the engine's world unit is ≈ 1 m on the stage.
    /// </summary>
    public sealed partial class BunnyHopScreen
    {
        private sealed class Gate3D
        {
            public Transform Root, Top, Bottom, CapTop, CapBottom;
            public Material Hedge, Cap;
            public float TopH = -1f, BottomH = -1f;
        }

        private World.MgStage3D _stage;
        private readonly Dictionary<BunnyHopGame.Pillar, Gate3D> _gates3d = new Dictionary<BunnyHopGame.Pillar, Gate3D>();
        private readonly Stack<Gate3D> _gatePool = new Stack<Gate3D>();
        private readonly Dictionary<BunnyHopGame.Coin, Transform> _coins3d = new Dictionary<BunnyHopGame.Coin, Transform>();
        private readonly Stack<Transform> _coinPool = new Stack<Transform>();
        private readonly List<Material> _ownedMats = new List<Material>();
        private readonly List<BunnyHopGame.Pillar> _deadGates = new List<BunnyHopGame.Pillar>();
        private readonly List<BunnyHopGame.Coin> _deadCoins = new List<BunnyHopGame.Coin>();
        private readonly HashSet<BunnyHopGame.Pillar> _liveGates = new HashSet<BunnyHopGame.Pillar>();
        private readonly HashSet<BunnyHopGame.Coin> _liveCoins = new HashSet<BunnyHopGame.Coin>();
        private Transform _ring, _shadow, _meadowNear, _meadowFar, _hillsNear3d, _hillsFar3d, _trees3d, _clouds3d;
        private Material _ringMat, _capGold;
        private float _floorM, _stretch, _crashVy, _crashSpin, _windEmit, _goobyYaw = 125f;
        private bool _crashFalling, _crashLanded;

        private static readonly Color HedgeGreen = new Color(0.42f, 0.66f, 0.3f);
        private static readonly Color HedgePale = new Color(0.62f, 0.76f, 0.56f);
        private static readonly Color CapGreen = new Color(0.36f, 0.62f, 0.27f);
        private static readonly Color CapPale = new Color(0.58f, 0.72f, 0.52f);
        private static readonly Color GateGold = new Color(1f, 0.8f, 0.35f);
        private const float GoobyScale = 1.0f, ColumnDepth = 0.9f;

        protected override bool StageHasGooby => _stage != null;

        /// <summary>View pixels per engine unit (the engine shows <see cref="BunnyHopGame.WorldH"/> units over the view height).</summary>
        private float Pv => (float)(BunnyHopGame.ViewH / _game.WorldH);

        /// <summary>Engine point (x from the view's left edge, y up from the view's middle) → stage world.</summary>
        private Vector3 W(double wx, double wy, float depth = 0f) =>
            _stage.ViewToWorld(wx * Pv, BunnyHopGame.ViewH * 0.5 - wy * Pv, depth);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(BunnyHopGame.ViewW, BunnyHopGame.ViewH, new Color(0.62f, 0.81f, 0.95f));
            _stage.Bind(Field);
            var root = _stage.Root.transform;
            _floorM = W(0, _game.Tune.FloorY).y - World.MgStage3D.Origin.y;
            // the meadow: a wide grass slab, a darker lip along the play line, a band of tufts and flowers in front
            _stage.Ground(_floorM, World.ArtLibrary.Hex("#86BE6A"), "grass", 60f, 52f, 14f);
            World.ArtLibrary.Box(root, "Lip", World.MgStage3D.At(0f, _floorM + 0.03f, -0.75f), new Vector3(60f, 0.06f, 0.5f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#6FAF55"), 0.05f), false);
            _meadowNear = ScrollRow("MeadowNear", 1.4f, 22, (row, i, x) =>
            {
                var tuft = World.ArtLibrary.Solid(new Color(0.33f, 0.6f, 0.25f), 0.05f);
                var h = 0.18f + (i * 7 % 5) * 0.04f;
                _stage.Cone(World.MgStage3D.At(x, _floorM, -1.3f + (i % 3) * 0.35f), 0.07f, h, tuft).transform.SetParent(row, true);
                if (i % 2 == 0)
                    _stage.Prop(i % 4 == 0 ? "flower_redA" : "flower_yellowA", World.MgStage3D.At(x + 0.55f, _floorM, -1.05f + (i % 3) * 0.2f), 0.75f, i * 47f).SetParent(row, true);
            });
            _meadowFar = ScrollRow("MeadowFar", 2.1f, 18, (row, i, x) =>
            {
                var model = i % 3 == 0 ? "plant_bushLarge" : i % 3 == 1 ? "flower_purpleA" : "grass_large";
                _stage.Prop(model, World.MgStage3D.At(x, _floorM, 1.6f + (i % 2) * 0.9f), i % 3 == 0 ? 2.2f : 1.5f, i * 61f).SetParent(row, true);
            });
            // parallax hills, a poplar row and clouds; the far rows move slower (see RenderStage)
            _hillsNear3d = ScrollRow("HillsNear", 2.8f, 14, (row, i, x) =>
            {
                var hill = World.ArtLibrary.Sphere(row, "Hill", World.MgStage3D.At(x, _floorM + 0.2f, 9f), 1f, World.ArtLibrary.Solid(new Color(0.45f, 0.67f, 0.4f), 0.04f), false);
                hill.transform.localScale = new Vector3(3.6f, 1.4f * (0.85f + (i % 3) * 0.15f), 2f);
            });
            _hillsFar3d = ScrollRow("HillsFar", 4.6f, 12, (row, i, x) =>
            {
                var hill = World.ArtLibrary.Sphere(row, "Hill", World.MgStage3D.At(x, _floorM + 0.3f, 16f), 1f, World.ArtLibrary.Solid(new Color(0.64f, 0.81f, 0.63f), 0.04f), false);
                hill.transform.localScale = new Vector3(6.4f, 3.2f * (0.85f + (i % 3) * 0.15f), 3f);
            });
            _trees3d = ScrollRow("Poplars", 5.2f, 9, (row, i, x) =>
            {
                var t = _stage.PlaceScenery(i % 3 == 1 ? "tree_round" : "tree_poplar", World.MgStage3D.At(x, _floorM, 12.5f), i * 33f, 1.1f + (i % 2) * 0.25f);
                t.SetParent(row, true);
            });
            _clouds3d = new GameObject("Clouds").transform;
            _clouds3d.SetParent(root, false);
            foreach (var c in _stage.Clouds(9, _floorM + 6.8f, 20f, 45f, 1.2f, 5)) c.SetParent(_clouds3d, true);
            _stage.Sun(new Vector3(4.6f, _floorM + 8.6f, 26f), 1.5f);
            // the next gate's gold ring and Gooby's blob shadow on the grass
            _ringMat = new Material(World.ArtLibrary.Transparent(new Color(GateGold.r, GateGold.g, GateGold.b, 0.8f)));
            _ownedMats.Add(_ringMat);
            _ring = World.RanchDressing.Ring(root, "GateRing", 0.92f, 1f, _ringMat);
            _ring.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _ring.gameObject.SetActive(false);
            _shadow = World.RanchDressing.Ring(root, "Shadow", 0f, 0.42f, World.ArtLibrary.Transparent(new Color(0.15f, 0.25f, 0.1f, 0.28f)));
            _capGold = World.ArtLibrary.Glow(new Color(1.25f, 1f, 0.42f));
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnFlyer(outfit);
        }

        private async System.Threading.Tasks.Task SpawnFlyer(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(W(_game.GoobyX, _game.GoobyY), GoobyScale, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            g.SetEmotion("happy");
            PlaceFlyer(0f);
        }

        /// <summary>A row of repeating set pieces under one parent, <paramref name="count"/> of them every <paramref name="period"/> m centred on the frame.</summary>
        private Transform ScrollRow(string name, float period, int count, System.Action<Transform, int, float> build)
        {
            var row = new GameObject(name).transform;
            row.SetParent(_stage.Root.transform, false);
            for (var i = 0; i < count; i++) build(row, i, (i - count * 0.5f) * period);
            return row;
        }

        private static void Scroll(Transform row, float travelled, float period) =>
            row.localPosition = new Vector3(-Mathf.Repeat(travelled, period), 0f, 0f);

        private Gate3D NewGate()
        {
            if (_gatePool.Count > 0)
            {
                var pooled = _gatePool.Pop();
                pooled.Root.gameObject.SetActive(true);
                return pooled;
            }
            var g = new Gate3D { Root = new GameObject("Gate").transform };
            g.Root.SetParent(_stage.Root.transform, false);
            g.Hedge = new Material(World.ArtLibrary.Pbr("leaves", Vector2.one, HedgeGreen, 0.08f, 1.4f));
            g.Cap = new Material(World.ArtLibrary.Pbr("leaves", new Vector2(2f, 1.5f), CapGreen, 0.08f, 1.6f));
            _ownedMats.Add(g.Hedge);
            _ownedMats.Add(g.Cap);
            g.Top = World.ArtLibrary.Box(g.Root, "HedgeTop", Vector3.zero, Vector3.one, g.Hedge).transform;
            g.Bottom = World.ArtLibrary.Box(g.Root, "HedgeBottom", Vector3.zero, Vector3.one, g.Hedge).transform;
            g.CapTop = Crown(g.Root, g.Cap, true);
            g.CapBottom = Crown(g.Root, g.Cap, false);
            return g;
        }

        /// <summary>The leafy bulge at a gap end: three overlapping leaf balls and a blossom, hanging down (top) or sitting up (bottom).</summary>
        private Transform Crown(Transform parent, Material leaf, bool top)
        {
            var crown = new GameObject(top ? "CrownTop" : "CrownBottom").transform;
            crown.SetParent(parent, false);
            foreach (var (x, y, s) in new[] { (-0.22f, 0f, 0.62f), (0.2f, 0.02f, 0.58f), (0f, 0.1f, 0.7f) })
            {
                var ball = World.ArtLibrary.Sphere(crown, "Leaf", Vector3.zero, 0.5f, leaf);
                ball.transform.localPosition = new Vector3(x, top ? -y : y, -0.05f);
                ball.transform.localScale = new Vector3(s * 1.15f, s * 0.75f, s);
            }
            var blossom = World.ArtLibrary.Sphere(crown, "Blossom", Vector3.zero, 0.5f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF9BC1"), 0.3f), false);
            blossom.transform.localPosition = new Vector3(0.16f, top ? -0.12f : 0.12f, -0.4f);
            blossom.transform.localScale = Vector3.one * 0.13f;
            return crown;
        }

        private Transform NewCoin()
        {
            if (_coinPool.Count > 0)
            {
                var c = _coinPool.Pop();
                c.gameObject.SetActive(true);
                return c;
            }
            return _stage.Prop("item-coin-gold", Vector3.zero, 1.6f);
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            var travel = (float)g.Scroll * Pv * World.MgStage3D.MetresPerPx;
            Scroll(_meadowNear, travel, 1.4f);
            Scroll(_meadowFar, travel * 0.9f, 2.1f);
            Scroll(_hillsNear3d, travel * 0.34f, 2.8f);
            Scroll(_hillsFar3d, travel * 0.14f, 4.6f);
            Scroll(_trees3d, travel * 0.2f, 5.2f);
            _clouds3d.localPosition = new Vector3(-Mathf.Repeat(travel * 0.05f + _pulse * 0.15f, 5f), 0f, 0f);
            // gates: hedge columns from the gap ends to the ceiling and the meadow; the next one glows gold
            BunnyHopGame.Pillar next = null;
            foreach (var p in g.Pillars) if (!p.Passed) { next = p; break; }
            _liveGates.Clear();
            foreach (var p in g.Pillars)
            {
                _liveGates.Add(p);
                if (!_gates3d.TryGetValue(p, out var v)) _gates3d[p] = v = NewGate();
                var x = W(p.X - g.Scroll, 0).x;
                var gapTop = W(0, p.GapCenterY + p.GapHeight * 0.5).y;
                var gapBottom = W(0, p.GapCenterY - p.GapHeight * 0.5).y;
                var ceiling = W(0, BunnyHopGame.WorldHalfH + 1.6).y;
                var floor = World.MgStage3D.Origin.y + _floorM;
                var w = (float)(g.Tune.PillarHalfW * 2.0) * Pv * World.MgStage3D.MetresPerPx;
                PoseColumn(v.Top, x, gapTop, ceiling, w);
                PoseColumn(v.Bottom, x, floor, gapBottom, w);
                if (!Mathf.Approximately(v.TopH, ceiling - gapTop) || !Mathf.Approximately(v.BottomH, gapBottom - floor))
                {
                    v.TopH = ceiling - gapTop;
                    v.BottomH = gapBottom - floor;
                    v.Hedge.mainTextureScale = new Vector2(1.3f, Mathf.Max(1f, Mathf.Max(v.TopH, v.BottomH) * 1.6f));
                    if (v.Hedge.HasProperty("_BaseMap")) v.Hedge.SetTextureScale("_BaseMap", v.Hedge.mainTextureScale);
                    if (v.Hedge.HasProperty("_BumpMap")) v.Hedge.SetTextureScale("_BumpMap", v.Hedge.mainTextureScale);
                }
                var tint = p.Passed ? HedgePale : HedgeGreen;
                v.Hedge.SetColor("_BaseColor", tint);
                v.Cap.SetColor("_BaseColor", p.Passed ? CapPale : CapGreen);
                var capMat = p == next ? _capGold : v.Cap;
                SetCrownMat(v.CapTop, capMat);
                SetCrownMat(v.CapBottom, capMat);
                v.CapTop.position = new Vector3(x, gapTop + 0.08f, World.MgStage3D.Origin.z);
                v.CapBottom.position = new Vector3(x, gapBottom - 0.08f, World.MgStage3D.Origin.z);
                v.CapTop.localScale = v.CapBottom.localScale = new Vector3(w * 1.25f, w * 1.25f, 1.1f);
            }
            _deadGates.Clear();
            foreach (var kv in _gates3d) if (!_liveGates.Contains(kv.Key)) _deadGates.Add(kv.Key);
            foreach (var d in _deadGates)
            {
                var v = _gates3d[d];
                v.Root.gameObject.SetActive(false);
                v.TopH = v.BottomH = -1f;
                _gatePool.Push(v);
                _gates3d.Remove(d);
            }
            if (next != null)
            {
                _ring.gameObject.SetActive(true);
                var r = Mathf.Max(0.3f, (float)(next.GapHeight * 0.5) - 0.2f) * Pv * World.MgStage3D.MetresPerPx;
                _ring.position = W(next.X - g.Scroll, next.GapCenterY, -0.14f);
                _ring.localScale = Vector3.one * r * (1f + 0.04f * Mathf.Sin(_pulse * 6f));
                var c = _ringMat.color;
                c.a = 0.55f + 0.3f * Mathf.Sin(_pulse * 6f);
                _ringMat.color = c;
                if (_ringMat.HasProperty("_BaseColor")) _ringMat.SetColor("_BaseColor", c);
            }
            else _ring.gameObject.SetActive(false);
            // coins spin in the gaps
            _liveCoins.Clear();
            foreach (var c in g.Coins)
            {
                _liveCoins.Add(c);
                if (!_coins3d.TryGetValue(c, out var tr)) _coins3d[c] = tr = NewCoin();
                tr.position = W(c.X - g.Scroll, c.Y, -0.1f) + Vector3.down * 0.2f + Vector3.up * Mathf.Sin(_pulse * 4f + (float)c.X) * 0.05f;
                tr.rotation = Quaternion.Euler(0f, _pulse * 180f, Mathf.Sin(_pulse * 2.2f) * 11f);
            }
            _deadCoins.Clear();
            foreach (var kv in _coins3d) if (!_liveCoins.Contains(kv.Key)) _deadCoins.Add(kv.Key);
            foreach (var d in _deadCoins)
            {
                var tr = _coins3d[d];
                if (tr != null) { tr.gameObject.SetActive(false); _coinPool.Push(tr); }
                _coins3d.Remove(d);
            }
            Wind(dt);
            PlaceFlyer(dt);
        }

        private static void PoseColumn(Transform column, float x, float from, float to, float width)
        {
            var h = to - from;
            column.gameObject.SetActive(h > 0.02f);
            if (h <= 0.02f) return;
            column.position = new Vector3(x, (from + to) * 0.5f, World.MgStage3D.Origin.z);
            column.localScale = new Vector3(width, h, ColumnDepth);
        }

        private static void SetCrownMat(Transform crown, Material mat)
        {
            for (var i = 0; i < crown.childCount; i++)
            {
                var child = crown.GetChild(i);
                if (child.name == "Leaf" && child.TryGetComponent<MeshRenderer>(out var r) && r.sharedMaterial != mat) r.sharedMaterial = mat;
            }
        }

        /// <summary>Gooby flies at the engine's spot: tilts with the climb, stretches on a hop; after the crash he tumbles into the grass.</summary>
        private void PlaceFlyer(float dt)
        {
            var gb = _stage?.Gooby;
            if (gb == null || _game == null) return;
            var floor = World.MgStage3D.Origin.y + _floorM;
            var feetOffset = (float)_game.Tune.BodyHalfH * Pv * World.MgStage3D.MetresPerPx;
            var target = W(_game.GoobyX, _game.GoobyY) + Vector3.down * feetOffset;
            if (_game.Crashed)
            {
                if (!_crashFalling)
                {
                    _crashFalling = true;
                    _crashVy = 2.2f;
                    gb.Play("ragdoll_flail-loop", 0.1f);
                    gb.SetEmotion("dizzy");
                }
                var p = gb.transform.position;
                if (!_crashLanded)
                {
                    _crashVy -= 16f * dt;
                    p.y = Mathf.Max(floor, p.y + _crashVy * dt);
                    gb.transform.position = p;
                    _crashSpin += dt * 380f;
                    gb.transform.rotation = Quaternion.Euler(0f, 0f, _crashSpin) * Quaternion.Euler(0f, _goobyYaw, 0f);
                    if (p.y <= floor + 0.001f)
                    {
                        _crashLanded = true;
                        gb.transform.rotation = Quaternion.Euler(0f, 0f, 80f) * Quaternion.Euler(0f, _goobyYaw, 0f);
                        gb.Play("idle-loop");
                        if (!ReducedMotion) World.Effects.Puff(p + Vector3.up * 0.15f, new Color(0.85f, 0.8f, 0.7f, 0.9f), 12, 0.22f, 1.2f, 0.6f, Vector3.up, 0.9f);
                    }
                }
                PlaceShadow(gb.transform.position, floor);
                return;
            }
            _stretch = Mathf.Max(0f, _stretch - dt * 4f);
            var s = Mathf.Clamp((float)_game.GoobyVy * 0.04f, -0.12f, 0.16f) + _stretch * 0.14f;
            gb.transform.localScale = new Vector3(1f - s * 0.55f, 1f + s, 1f - s * 0.55f) * GoobyScale;
            var tilt = Mathf.Clamp((float)_game.GoobyVy * 4.6f, -23f, 23f);
            gb.transform.position = target;
            gb.transform.rotation = Quaternion.Euler(0f, 0f, tilt) * Quaternion.Euler(0f, _goobyYaw, 0f);
            PlaceShadow(target, floor);
        }

        private void PlaceShadow(Vector3 at, float floor)
        {
            var drop = Mathf.Clamp(1f - (at.y - floor) / 6f, 0.3f, 1f);
            _shadow.position = new Vector3(at.x, floor + 0.015f, at.z + 0.05f);
            _shadow.localScale = Vector3.one * drop;
        }

        /// <summary>Gust streaks: warm and soft while the wind telegraphs, teal and strong while it blows.</summary>
        private void Wind(float dt)
        {
            var phase = _game.GustPhase;
            if (ReducedMotion || (phase != "telegraph" && phase != "gust")) return;
            var gust = phase == "gust";
            _windEmit -= dt;
            if (_windEmit > 0f) return;
            _windEmit = gust ? 0.05f : 0.12f;
            var dir = new Vector3(1f, 0.35f * _game.GustDirection, 0f);
            var from = W(-0.3, (Random.value * 2f - 1f) * 3.4, -0.6f);
            World.Effects.Puff(from, gust ? new Color(0.5f, 0.92f, 0.86f, 0.6f) : new Color(1f, 0.9f, 0.5f, 0.4f), gust ? 3 : 2, 0.12f, gust ? 5.5f : 3.2f, 1.3f, dir, 0.08f);
        }

        /// <summary>A tap that hopped: Gooby stretches and a little cloud puffs out under him.</summary>
        private void StageHop()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            _stretch = 1f;
            gb.Play("hop", 0.08f);
            if (!ReducedMotion) World.Effects.Puff(gb.transform.position + new Vector3(-0.1f, 0.05f, -0.15f), new Color(1f, 1f, 1f, 0.75f), 6, 0.14f, 1f, 0.35f, Vector3.down, 0.6f);
        }

        private void StageCoin()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("ecstatic");
            if (!ReducedMotion) World.Effects.Sparkle(gb.transform.position + Vector3.up * 0.6f, 0.35f, 14);
        }

        private void StageGate(bool gusting)
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion(gusting ? "ecstatic" : "happy");
        }

        private void DropStage()
        {
            _gates3d.Clear(); _gatePool.Clear(); _coins3d.Clear(); _coinPool.Clear();
            _liveGates.Clear(); _liveCoins.Clear(); _deadGates.Clear(); _deadCoins.Clear();
            _stage?.Destroy();
            _stage = null;
            foreach (var m in _ownedMats) if (m != null) Object.Destroy(m);
            _ownedMats.Clear();
            _crashFalling = _crashLanded = false;
            _stretch = _crashSpin = 0f;
        }
    }
}
