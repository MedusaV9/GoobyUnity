using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D shopping street of Shopping-Surf (port of shopping_surf_world.gd): a chase camera rides behind the player's
    /// Gooby on his little board down a pedestrian street lined with the town's shop fronts (GOOBY MART, POW!, the post,
    /// the café …), awnings and lanterns. Shopping carts roll at him, crates block lanes, shoppers cross, striped awnings
    /// hang low, puddles shine and the kerb breaks open; coins spin, power-ups glow (magnet ring, ×2 sparkle, shield bubble,
    /// turbo trail). Engine metres are stage metres (x across, z ahead = −engine z); the camera reproduces the old 2D
    /// framing (horizon at 30 %, Gooby at 80 %), so the frame's floats land on him via <see cref="Project"/>.
    /// </summary>
    public sealed partial class ShoppingSurfScreen
    {
        private World.MgStage3D _stage;
        private readonly Dictionary<ShoppingSurfRun.Obstacle, Transform> _obs3d = new Dictionary<ShoppingSurfRun.Obstacle, Transform>();
        private readonly Dictionary<ShoppingSurfRun.CoinItem, Transform> _coins3d = new Dictionary<ShoppingSurfRun.CoinItem, Transform>();
        private readonly Dictionary<ShoppingSurfRun.PowerupItem, Transform> _pows3d = new Dictionary<ShoppingSurfRun.PowerupItem, Transform>();
        private readonly Dictionary<string, Stack<Transform>> _pool = new Dictionary<string, Stack<Transform>>();
        private readonly HashSet<object> _alive = new HashSet<object>();
        private readonly List<object> _gone = new List<object>();
        private readonly List<Transform> _fronts = new List<Transform>();
        private Transform _dashes3d, _board, _magnetRing, _bubble;
        private bool _slideShown;
        private float _trailEmit, _clock;

        private const float FrontStep = 9f, FrontRows = 10, DashStep3 = 4.2f, GoobyScale = 1.45f;
        private static readonly string[] Fronts = { "shop_mart", "house_pink", "shop_pow", "house_cream", "shop_post", "house_yellow", "shop_goobyman", "house_blue", "shop_gouhbus", "house_green" };

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 S(double x, double y, double z) => World.MgStage3D.At((float)x, (float)y, (float)-z);

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.66f, 0.84f, 0.97f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 5.5f, -9.5f), 11.5f, 64f);
            var root = _stage.Root.transform;
            _stage.Ground(-0.03f, World.ArtLibrary.Hex("#9CCF8C"), "grass", 80f, 150f, 60f);
            World.ArtLibrary.Box(root, "Street", World.MgStage3D.At(0f, -0.05f, 55f), new Vector3(5.2f, 0.1f, 140f), World.ArtLibrary.PbrSlab("paving", new Vector2(5.2f, 140f), 1.6f, World.ArtLibrary.Hex("#C9C2D8"), 0.2f), false);
            foreach (var s in new[] { -1f, 1f })
            {
                World.ArtLibrary.Box(root, "Sidewalk", World.MgStage3D.At(s * 3.5f, 0.03f, 55f), new Vector3(1.8f, 0.12f, 140f), World.ArtLibrary.PbrSlab("tiles", new Vector2(1.8f, 140f), 0.9f, World.ArtLibrary.Hex("#F3E6D2"), 0.25f), false);
                World.ArtLibrary.Box(root, "Curb", World.MgStage3D.At(s * 2.66f, 0.05f, 55f), new Vector3(0.12f, 0.16f, 140f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#DCD2C4"), 0.3f), false);
            }
            _dashes3d = new GameObject("Dashes").transform;
            _dashes3d.SetParent(root, false);
            var mark = World.ArtLibrary.Solid(new Color(1f, 0.97f, 0.88f), 0.3f);
            for (var k = 0; k < 30; k++)
                foreach (var lx in new[] { -0.8f, 0.8f })
                    World.ArtLibrary.Box(_dashes3d, "Dash", World.MgStage3D.At(lx, 0.012f, k * DashStep3), new Vector3(0.1f, 0.01f, 0.9f), mark, false);
            _stage.MergeChildren(_dashes3d, "Dashes");
            for (var k = 0; k < FrontRows; k++)
                foreach (var s in new[] { -1f, 1f })
                {
                    var row = new GameObject("Front").transform;
                    row.SetParent(root, false);
                    var model = Fronts[(k * 2 + (s > 0f ? 1 : 0)) % Fronts.Length];
                    _stage.PlaceScenery(model, World.MgStage3D.At(s * 8.2f, 0f, 0f), s > 0f ? -90f : 90f, 0.82f).SetParent(row, true);
                    _stage.PlaceScenery(k % 2 == 0 ? "lamp_post_pink" : "street_planter", World.MgStage3D.At(s * 4.1f, 0.09f, FrontStep * 0.5f), 0f, 1f).SetParent(row, true);
                    row.localPosition = new Vector3(0f, 0f, k * FrontStep);
                    _fronts.Add(row);
                }
            _stage.Hills(-0.03f, 130f, 8, new Color(0.7f, 0.84f, 0.66f), 12f, 160f, 0f, 9);
            _stage.Clouds(6, 18f, 120f, 90f, 3.6f, 12);
            _stage.Sun(new Vector3(24f, 34f, 150f), 4.5f);
            // the surf board under Gooby, the magnet ring and the shield bubble
            _board = new GameObject("Board").transform;
            _board.SetParent(root, false);
            World.ArtLibrary.Box(_board, "Deck", Vector3.zero, new Vector3(0.62f, 0.06f, 1.25f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF8B8B"), 0.5f)).transform.localPosition = new Vector3(0f, 0.1f, 0f);
            World.ArtLibrary.Box(_board, "Stripe", Vector3.zero, new Vector3(0.64f, 0.065f, 0.18f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFF8EE"), 0.5f), false).transform.localPosition = new Vector3(0f, 0.1f, 0.25f);
            foreach (var (x, z) in new[] { (-0.24f, -0.42f), (0.24f, -0.42f), (-0.24f, 0.42f), (0.24f, 0.42f) })
            {
                var wheel = World.ArtLibrary.Cylinder(_board, "Wheel", Vector3.zero, 0.07f, 0.06f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#7FB8FF"), 0.5f), false).transform;
                wheel.localPosition = new Vector3(x, 0.045f, z);
                wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            _magnetRing = _stage.Torus(null, "Magnet", Vector3.zero, 1.1f, 0.06f, World.ArtLibrary.Glow(new Color(0.55f, 1f, 1.3f))).transform;
            _magnetRing.gameObject.SetActive(false);
            _bubble = World.ArtLibrary.Sphere(root, "Shield", Vector3.zero, 1.05f, World.ArtLibrary.Transparent(new Color(0.55f, 0.85f, 1f, 0.22f)), false).transform;
            _bubble.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnSurfer(outfit);
        }

        private async System.Threading.Tasks.Task SpawnSurfer(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(0f, 0.13f, 0f), GoobyScale, outfit);
            if (g == null || _stage == null) return;
            g.transform.rotation = Quaternion.identity;
            g.Play("idle-loop", 0f);
            g.SetEmotion("happy");
        }

        private Transform Take(string kind, int variant)
        {
            if (_pool.TryGetValue(kind, out var stack) && stack.Count > 0) { var t = stack.Pop(); t.gameObject.SetActive(true); return t; }
            Transform made;
            switch (kind)
            {
                case "cart": made = Cart(); break;
                case "crate": made = _stage.PlaceScenery("shop_crate", Vector3.zero, 0f, 1.25f); break;
                case "npc": made = Shopper(variant); break;
                case "awning": made = new GameObject("awning").transform; made.SetParent(_stage.Root.transform, false); break;
                case "puddle":
                    made = World.RanchDressing.Ring(_stage.Root.transform, "puddle", 0f, 0.8f, World.ArtLibrary.Solid(new Color(0.48f, 0.7f, 0.95f), 0.95f));
                    break;
                case "gap":
                {
                    made = new GameObject("gap").transform;
                    made.SetParent(_stage.Root.transform, false);
                    World.ArtLibrary.Box(made, "Hole", Vector3.zero, new Vector3(5.2f, 0.02f, 1.1f), World.ArtLibrary.Solid(new Color(0.18f, 0.15f, 0.2f), 0.1f), false).transform.localPosition = new Vector3(0f, 0.01f, 0f);
                    foreach (var z in new[] { -0.6f, 0.6f })
                        World.ArtLibrary.Box(made, "Edge", Vector3.zero, new Vector3(5.2f, 0.08f, 0.12f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFB347"), 0.4f), false).transform.localPosition = new Vector3(0f, 0.04f, z);
                    break;
                }
                case "coin": made = _stage.Prop("item-coin-gold", Vector3.zero, 2.2f); break;
                default:
                {
                    // a power-up: a glowing gift on a pedestal of light
                    made = _stage.Prop("item-box", Vector3.zero, 2.4f);
                    var col = kind == "magnet" ? new Color(0.55f, 0.95f, 1.2f) : kind == "x2" ? new Color(1.3f, 1.05f, 0.4f) : kind == "shield" ? new Color(0.6f, 0.85f, 1.3f) : new Color(1.3f, 0.6f, 0.8f);
                    World.ArtLibrary.Sphere(made, "Glow", made.position + Vector3.up * 0.3f, 0.48f, World.ArtLibrary.Transparent(new Color(col.r * 0.7f, col.g * 0.7f, col.b * 0.7f, 0.28f)), false);
                    break;
                }
            }
            made.name = kind;
            return made;
        }

        /// <summary>A shopping cart from primitives: wire basket, child seat, handle, four wheels.</summary>
        private Transform Cart()
        {
            var cart = new GameObject("cart").transform;
            cart.SetParent(_stage.Root.transform, false);
            var wire = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#B8C4D2"), 0.85f);
            var basket = World.ArtLibrary.Transparent(new Color(0.75f, 0.82f, 0.9f, 0.45f));
            World.ArtLibrary.Box(cart, "Basket", Vector3.zero, new Vector3(1.0f, 0.55f, 0.8f), basket, false).transform.localPosition = new Vector3(0f, 0.68f, 0f);
            foreach (var (x, y, z, sx, sy, sz) in new[] { (0f, 0.95f, 0.4f, 1.02f, 0.04f, 0.04f), (0f, 0.95f, -0.4f, 1.02f, 0.04f, 0.04f), (0.5f, 0.95f, 0f, 0.04f, 0.04f, 0.82f), (-0.5f, 0.95f, 0f, 0.04f, 0.04f, 0.82f), (0f, 0.4f, 0f, 1.0f, 0.04f, 0.8f) })
                World.ArtLibrary.Box(cart, "Wire", Vector3.zero, new Vector3(sx, sy, sz), wire, false).transform.localPosition = new Vector3(x, y, z);
            World.ArtLibrary.Box(cart, "Handle", Vector3.zero, new Vector3(0.9f, 0.06f, 0.06f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF8B8B"), 0.4f), false).transform.localPosition = new Vector3(0f, 1.08f, -0.52f);
            foreach (var (x, z) in new[] { (-0.42f, -0.32f), (0.42f, -0.32f), (-0.42f, 0.32f), (0.42f, 0.32f) })
            {
                var w = World.ArtLibrary.Cylinder(cart, "Wheel", Vector3.zero, 0.07f, 0.05f, World.ArtLibrary.Solid(new Color(0.25f, 0.25f, 0.3f), 0.3f), false).transform;
                w.localPosition = new Vector3(x, 0.07f, z);
                w.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            // a few groceries peeking out
            var groceries = new[] { "banana", "bread", "apple" };
            for (var i = 0; i < groceries.Length; i++)
            {
                var item = _stage.Prop(groceries[i], Vector3.zero, 1.6f);
                item.SetParent(cart, false);
                item.localPosition = new Vector3(-0.25f + i * 0.25f, 0.85f, 0.05f);
                item.localRotation = Quaternion.Euler(0f, i * 70f, 0f);
                item.localScale = Vector3.one * 1.6f;
            }
            return cart;
        }

        /// <summary>A shopper Gooby with a paper bag (the crossing NPC), dressed from the fur palette.</summary>
        private Transform Shopper(int variant)
        {
            var anchor = new GameObject("npc").transform;
            anchor.SetParent(_stage.Root.transform, false);
            _ = DressShopper(anchor, variant);
            World.ArtLibrary.Box(anchor, "Bag", Vector3.zero, new Vector3(0.32f, 0.38f, 0.2f), World.ArtLibrary.Pbr("fabric", 0.4f, World.ArtLibrary.Hex("#D9B48A"), 0.1f)).transform.localPosition = new Vector3(0.42f, 0.45f, 0f);
            return anchor;
        }

        private async System.Threading.Tasks.Task DressShopper(Transform anchor, int variant)
        {
            var g = await World.GoobyAvatar.CreateAsync(anchor, anchor.position);
            if (g == null) return;
            if (anchor == null || _stage == null) { Object.Destroy(g.gameObject); return; }
            g.transform.SetParent(anchor, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = Vector3.one * 1.2f;
            string[] furs = { "fell_lavendel", "fell_minze", "fell_himmel", "fell_kakao" };
            g.Wear(new Dictionary<string, string> { ["fell"] = furs[variant % furs.Length], ["hut"] = variant % 2 == 0 ? "strawHat" : "cap" });
            g.Play("walk-loop", 0f);
            World.RanchDressing.QuietShadows(g.transform);
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _clock += dt;
            var run = _game.Run;
            var scroll = (float)_scroll;
            _dashes3d.localPosition = new Vector3(0f, 0f, -Mathf.Repeat(scroll, DashStep3));
            for (var i = 0; i < _fronts.Count; i++)
            {
                var row = i / 2;
                _fronts[i].localPosition = new Vector3(0f, 0f, Mathf.Repeat(row * FrontStep - scroll + (i % 2) * 3.7f, FrontStep * FrontRows) - FrontStep);
            }
            _alive.Clear();
            foreach (var ob in run.Obstacles)
            {
                if (ob.Z < -80.0 || ob.Z > 6.0) continue;
                _alive.Add(ob);
                if (!_obs3d.TryGetValue(ob, out var tr))
                {
                    tr = Take(ob.Kind, ob.Id);
                    if (ob.Kind == "awning") DressAwning(tr, ob);
                    _obs3d[ob] = tr;
                }
                switch (ob.Kind)
                {
                    case "npc":
                        tr.position = S(ob.X, 0.0, ob.Z);
                        tr.rotation = Quaternion.Euler(0f, 90f, 0f);
                        break;
                    case "puddle":
                        tr.position = S(ob.X, 0.012, ob.Z);
                        tr.localScale = new Vector3((float)System.Math.Max(0.6, ob.HalfW) / 0.8f, 1f, 0.8f);
                        break;
                    case "gap":
                    case "awning":
                        tr.position = S(0.0, 0.0, ob.Z);
                        break;
                    default:
                        tr.position = S(ob.X, 0.0, ob.Z);
                        tr.rotation = ob.Kind == "cart" ? Quaternion.Euler(0f, 180f + Mathf.Sin((float)ob.Z) * 10f, 0f) : Quaternion.identity;
                        break;
                }
            }
            foreach (var c in run.CoinItems)
            {
                if (c.Z < -80.0 || c.Z > 1.0) continue;
                _alive.Add(c);
                if (!_coins3d.TryGetValue(c, out var tr)) _coins3d[c] = tr = Take("coin", 0);
                tr.position = S(c.X, c.Y + 0.1, c.Z);
                tr.rotation = Quaternion.Euler(0f, _clock * 220f + c.Id * 30f, 0f);
            }
            foreach (var pu in run.PowerupItems)
            {
                if (pu.Z < -80.0 || pu.Z > 1.0) continue;
                _alive.Add(pu);
                if (!_pows3d.TryGetValue(pu, out var tr)) _pows3d[pu] = tr = Take(pu.Kind, 0);
                tr.position = S(pu.X, 0.4 + Mathf.Sin(_clock * 3f) * 0.1f, pu.Z);
                tr.rotation = Quaternion.Euler(0f, _clock * 100f, 0f);
            }
            Sweep(_obs3d);
            Sweep(_coins3d);
            Sweep(_pows3d);
            PlaceSurfer(dt);
        }

        private void DressAwning(Transform t, ShoppingSurfRun.Obstacle ob)
        {
            for (var i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
            var hw = (float)System.Math.Max(0.8, ob.HalfW);
            var x0 = (float)ob.X - hw;
            const int stripes = 7;
            for (var s = 0; s < stripes; s++)
            {
                var w = hw * 2f / stripes;
                World.ArtLibrary.Box(t, "Stripe", Vector3.zero, new Vector3(w, 0.08f, 1.3f), World.ArtLibrary.Solid(s % 2 == 0 ? World.ArtLibrary.Hex("#F26D6D") : Color.white, 0.3f)).transform.localPosition = new Vector3(x0 + w * (s + 0.5f), 1.15f, 0f);
                var scallop = World.ArtLibrary.Sphere(t, "Scallop", Vector3.zero, 0.5f, World.ArtLibrary.Solid(s % 2 == 0 ? World.ArtLibrary.Hex("#F26D6D") : Color.white, 0.3f), false).transform;
                scallop.localPosition = new Vector3(x0 + w * (s + 0.5f), 1.08f, -0.65f);
                scallop.localScale = new Vector3(w, 0.22f, 0.12f);
            }
            foreach (var x in new[] { x0, x0 + hw * 2f })
                World.ArtLibrary.Cylinder(t, "Pole", Vector3.zero, 0.04f, 1.2f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8C96A6"), 0.6f), false).transform.localPosition = new Vector3(x, 0.6f, -0.6f);
        }

        private void Sweep<TKey>(Dictionary<TKey, Transform> map)
        {
            _gone.Clear();
            foreach (var kv in map) if (!_alive.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (TKey k in _gone)
            {
                var t = map[k];
                map.Remove(k);
                if (t == null) continue;
                t.gameObject.SetActive(false);
                if (!_pool.TryGetValue(t.name, out var stack)) _pool[t.name] = stack = new Stack<Transform>();
                stack.Push(t);
            }
        }

        private void PlaceSurfer(float dt)
        {
            var run = _game.Run;
            var x = ShoppingSurfRun.PlayerX(run);
            var y = ShoppingSurfRun.PlayerY(run);
            var wobble = Mathf.Sin((float)_crashWobble * 30f) * 0.25f * (float)_crashWobble;
            var at = S(x + wobble, y, 0.0);
            var sliding = run.SlideT >= 0.0;
            _board.position = at;
            _board.rotation = Quaternion.Euler(sliding ? -6f : 0f, 0f, (float)(run.Tune.LaneX[run.Lane] - x) * -10f);
            _magnetRing.gameObject.SetActive(run.MagnetT > 0.0);
            if (run.MagnetT > 0.0) { _magnetRing.position = at + Vector3.up * 0.8f; _magnetRing.rotation = Quaternion.Euler(0f, _clock * 140f, 0f); }
            _bubble.gameObject.SetActive(run.Shield);
            if (run.Shield) _bubble.position = at + Vector3.up * 0.9f;
            if (run.TurboT > 0.0 && !ReducedMotion)
            {
                _trailEmit -= dt;
                if (_trailEmit <= 0f)
                {
                    _trailEmit = 0.05f;
                    World.Effects.Puff(at + new Vector3(0f, 0.15f, -0.6f), new Color(1f, 0.6f, 0.8f, 0.8f), 3, 0.14f, 2f, 0.4f, Vector3.back, 0.4f);
                }
            }
            var gb = _stage.Gooby;
            if (gb == null) return;
            var blink = run.InvulnT > 0.0 && (int)(run.InvulnT * 12.0) % 2 == 1;
            gb.gameObject.SetActive(!blink);
            gb.transform.position = at + Vector3.up * 0.13f;
            gb.transform.rotation = Quaternion.Euler(0f, (float)(run.Tune.LaneX[run.Lane] - x) * 14f, 0f);
            if (sliding != _slideShown)
            {
                _slideShown = sliding;
                gb.Play(sliding ? "sit-loop" : "idle-loop", 0.08f);
            }
            gb.transform.localScale = sliding ? new Vector3(1.12f, 0.72f, 1.12f) * GoobyScale : Vector3.one * GoobyScale;
        }

        private void StageEvent(ShoppingSurfRun.Ev ev)
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            switch (ev.Type)
            {
                case "coin": if (!ReducedMotion) World.Effects.Sparkle(gb.transform.position + Vector3.up * 1.1f, 0.35f, 6); break;
                case "nearMiss": gb.SetEmotion("ecstatic"); break;
                case "crash": gb.SetEmotion("dizzy"); if (!ReducedMotion) World.Effects.Puff(gb.transform.position + Vector3.up * 0.5f, new Color(1f, 0.85f, 0.4f, 0.9f), 12, 0.2f, 1.6f, 0.5f, Vector3.up, 1f); break;
                case "powerup": gb.SetEmotion("ecstatic"); gb.Play("celebrate", 0.1f); gb.Play("idle-loop", 0.6f); break;
                case "puddle": gb.SetEmotion("scared"); break;
                case "jump": gb.Play("hop", 0.05f); break;
            }
        }

        private void DropStage()
        {
            _obs3d.Clear(); _coins3d.Clear(); _pows3d.Clear(); _pool.Clear(); _alive.Clear(); _gone.Clear(); _fronts.Clear();
            _slideShown = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
