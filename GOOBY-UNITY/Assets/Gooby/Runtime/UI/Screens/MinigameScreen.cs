using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// CarrotCatch host on the shared minigame frame: the basket follows the pointer/finger along the bottom of the
    /// portrait field, carrots, treats, golden carrots and junk fall on the certified realtime engine. Game feel after
    /// carrot_catch.gd: sparks + "+N" over the basket on a catch, a gold shower for the golden carrot, "Igitt!" rattle
    /// for junk, a dust puff where good food hits the ground, a pink burst on every fifth catch in a row.
    /// </summary>
    public sealed class MinigameScreen : MinigameHostScreen
    {
        private VisualElement _basket;
        private readonly Dictionary<CarrotCatchGame.FallingItem, VisualElement> _views = new Dictionary<CarrotCatchGame.FallingItem, VisualElement>();
        private CarrotCatchGame _game;
        // the 3D garden diorama (Godot carrot_catch_stage3d.gd): Gooby carries a basket, the real foods fall and spin
        private World.MgStage3D _stage;
        private Transform _basket3d;
        private readonly Dictionary<CarrotCatchGame.FallingItem, Transform> _props = new Dictionary<CarrotCatchGame.FallingItem, Transform>();
        private float _groundY, _goobyYaw = 180f, _goobyBounce, _lastBasketX;
        private bool _goobyWalking;

        private static readonly Color GoodC = new Color(0.42f, 0.6f, 0.36f);
        private static readonly Color GoldC = new Color(1f, 0.8f, 0.3f);
        private static readonly Color JunkC = new Color(0.8f, 0.3f, 0.25f);
        private static readonly Color JunkDust = new Color(0.6f, 0.56f, 0.52f);
        private static readonly Color GroundDust = new Color(0.72f, 0.64f, 0.46f);
        private static readonly Color StreakC = new Color(0.95f, 0.45f, 0.66f);

        public MinigameScreen(GameHost host, string gameId = "carrotCatch") : base(host, gameId, "carrotCatch") { }

        protected override (double w, double h) ViewSize => (CarrotCatchGame.ViewW, CarrotCatchGame.ViewH);
        protected override bool Running => _game != null;
        protected override bool StageHasGooby => _stage != null;

        protected override void StartGame(int seed)
        {
            _game = new CarrotCatchGame(Difficulty, seed);
            _game.Caught += OnCaught;
            _game.Missed += OnMissed;
            _game.ComboMilestone += OnComboMilestone;
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_perfect");
        }

        protected override void BuildPlayField()
        {
            Field.RegisterCallback<PointerMoveEvent>(e => Steer(e.localPosition.x));
            Field.RegisterCallback<PointerDownEvent>(e => Steer(e.localPosition.x));
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();

            _basket = Ux.El("gb-card", "gb-row");
            _basket.style.position = Position.Absolute;
            _basket.style.width = 96; _basket.style.height = 44;
            _basket.style.paddingTop = 0; _basket.style.paddingBottom = 0; _basket.style.paddingLeft = 0; _basket.style.paddingRight = 0;
            _basket.style.justifyContent = Justify.Center; _basket.style.alignItems = Align.Center;
            _basket.style.backgroundColor = new Color(0.85f, 0.71f, 0.54f);
            _basket.Add(Ux.Icon("inventory"));
            Field.Add(_basket);
            if (_stage != null) _basket.style.opacity = 0f;
            Banner(Host.Strings.T("mg.carrotCatch.intro"), CarrotCatchGame.IntroSec + 0.7);
        }

        // ── scenery (board 06): a soft morning sky with drifting clouds, hills, a picket fence behind the basket and
        //    the meadow strip the basket runs on — all behind the game, pointer-transparent, built once ──

        private readonly List<VisualElement> _clouds = new List<VisualElement>();
        private float _sceneT;
        private static readonly float[] CloudW = { 112f, 78f, 132f };
        private static readonly float[] CloudTop = { 9f, 22f, 35f };
        private static readonly float[] CloudSpeed = { 7f, 4.5f, 10f };
        private static readonly float[] CloudPhase = { 40f, 230f, 340f };

        protected override Color FieldColor => MgScenery.Hex(0xB5DAFF);
        protected override Color BackdropColor => MgScenery.Hex(0x93CF8E);

        private void BuildScenery()
        {
            _clouds.Clear();
            var scene = MgScenery.Layer(Field, "mgf-carrotcatch");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 89f);
            var sun = MgScenery.Sun(scene, 50f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, null, 6f, 7f);
            for (var i = 0; i < CloudW.Length; i++)
            {
                var cloud = MgScenery.Cloud(scene, CloudW[i], i == 1 ? MgScenery.Hex(0xF4F8FF) : (Color?)null);
                cloud.name = "mgf-cloud-" + i;
                cloud.style.left = 0;
                MgScenery.Pct(cloud, null, CloudTop[i]);
                cloud.style.translate = new Translate(CloudPhase[i], 0);
                _clouds.Add(cloud);
            }
            // far hills (hazy mint) with a round tree on the middle crest, the near hills and two bushes in front
            var farC = MgScenery.Hex(0xBFE6CC); var farRim = MgScenery.Hex(0xD9F3E1);
            AddHill(scene, -12f, 200f, 112f, farC, farRim);
            var crest = AddHill(scene, 40f, 230f, 150f, farC, farRim);
            var tree = MgScenery.Sprite(crest, "plant_tree_round", 58f, 76f);
            tree.style.left = 96f; tree.style.top = -62f;
            AddHill(scene, 76f, 170f, 96f, farC, farRim);
            var nearC = MgScenery.Hex(0xA6DB98); var nearRim = MgScenery.Hex(0xC6F39A);
            AddHill(scene, -20f, 250f, 70f, nearC, nearRim);
            AddHill(scene, 55f, 260f, 58f, nearC, nearRim);
            var bushL = MgScenery.Sprite(scene, "plant_bush", 70f, 64f);
            MgScenery.Pct(bushL, 2f, null, null, 10.5f);
            var bushR = MgScenery.Sprite(scene, "plant_bush", 56f, 52f);
            MgScenery.Pct(bushR, null, null, 3f, 10.5f);
            // the picket fence the basket runs in front of (the basket's rim sits at ~82 %, its floor at ~87 %)
            var fence = MgScenery.Fence(scene, 9, 46f, MgScenery.Hex(0xF6E4C8), MgScenery.Hex(0xC8A27A));
            fence.name = "mgf-fence";
            MgScenery.Pct(fence, null, null, null, 11f);
            // the meadow strip: grass edge, tufts poking over it, dot flowers and painted daisies/tulips
            var meadow = MgScenery.Shape(scene, MgScenery.Hex(0x9ED499), 0f, "gb-mgf-meadow");
            meadow.name = "mgf-meadow";
            MgScenery.Pct(meadow, 0, 88f, 0, 0);
            meadow.style.borderTopColor = MgScenery.Hex(0x7CC486);
            var tuftC = MgScenery.Hex(0x6DBE78);
            for (var i = 0; i < 7; i++)
            {
                var tuft = MgScenery.Tuft(meadow, tuftC, i % 2 == 0 ? 1f : 0.8f);
                MgScenery.Pct(tuft, 3f + i * 14.5f, null);
                tuft.style.top = -16f + (i % 2) * 3f;
            }
            var petals = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xB7D8FF), MgScenery.Hex(0xFF8B8B) };
            for (var i = 0; i < 6; i++)
            {
                var f = MgScenery.Flower(meadow, petals[i % petals.Length], 0.9f);
                MgScenery.Pct(f, 9f + i * 16f, 30f + (i % 3) * 18f);
            }
            var daisies = MgScenery.Sprite(meadow, "plant_daisies", 40f, 42f);
            MgScenery.Pct(daisies, 1f, null, null, 2f);
            var tulips = MgScenery.Sprite(meadow, "plant_tulips_red", 36f, 42f);
            MgScenery.Pct(tulips, null, null, 2f, 2f);
        }

        private static VisualElement AddHill(VisualElement scene, float leftPct, float w, float h, Color c, Color rim)
        {
            var hill = MgScenery.Hill(scene, w, h, c, rim);
            MgScenery.Pct(hill, leftPct, null, null, 11f);
            return hill;
        }

        /// <summary>The clouds drift slowly to the right and wrap (three translates a frame; still with reduced motion).</summary>
        private void DriftClouds()
        {
            if (FieldW <= 0f) return;
            var t = ReducedMotion ? 0f : _sceneT;
            for (var i = 0; i < _clouds.Count; i++)
            {
                var x = Mathf.Repeat(CloudPhase[i] + t * CloudSpeed[i], FieldW + CloudW[i]) - CloudW[i];
                _clouds[i].style.translate = new Translate(x, 0);
            }
        }

        private void Steer(float localX)
        {
            if (_game == null || FieldW <= 0f) return;
            _game.SetTargetFromNormalizedX(localX / FieldW);
        }

        protected override void TickGame(double dt) { _game.Tick(dt); _sceneT += (float)dt; }

        protected override void RenderGame()
        {
            DriftClouds();
            var sx = FieldW / (float)CarrotCatchGame.ViewW;
            var sy = FieldH / (float)CarrotCatchGame.ViewH;
            var ppu = (float)_game.PxPerUnit * sx;
            var bx = FieldW * 0.5f + (float)_game.BasketX * ppu;
            _basket.style.left = bx - 48;
            _basket.style.top = (float)_game.BasketY * sy - 22;
            _basket.style.rotate = new Rotate(_game.Dizzy ? 12f : 0f);
            var live = new HashSet<CarrotCatchGame.FallingItem>(_game.Items);
            if (_stage != null) RenderStage(live);
            else foreach (var item in _game.Items)
            {
                if (!_views.TryGetValue(item, out var v))
                {
                    v = Ux.IconTile(IconFor(item), TintFor(item));
                    v.style.position = Position.Absolute;
                    v.pickingMode = PickingMode.Ignore;
                    _views[item] = v;
                    Field.Add(v);
                }
                v.style.left = FieldW * 0.5f + (float)item.X * ppu - 24;
                v.style.top = (float)item.Y * sy - 24;
            }
            var dead = new List<CarrotCatchGame.FallingItem>();
            foreach (var kv in _views) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _views[d].RemoveFromHierarchy(); _views.Remove(d); }
            TimeLabel.text = _game.Tune.Endless ? Host.Strings.T("mg.carrotCatch.missed", ("n", _game.MissedCarrots), ("max", _game.Tune.EndlessMissedCarrots)) : Host.Strings.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            ComboLabel.text = _game.Combo >= 2 ? Host.Strings.T("mg.game.streak", ("n", _game.Combo)) : "";
        }

        private static string IconFor(CarrotCatchGame.FallingItem item)
        {
            if (item.Kind == "golden") return "star";
            if (item.Kind == "junk" || item.Kind == "rotten") return "close";
            return item.Key == "carrot" ? "food_carrot" : "heart";
        }

        private static Color TintFor(CarrotCatchGame.FallingItem item)
        {
            if (item.Kind == "golden") return new Color(1f, 0.82f, 0.4f);
            if (item.Kind == "junk" || item.Kind == "rotten") return new Color(0.78f, 0.78f, 0.78f);
            return item.Key == "carrot" ? new Color(1f, 0.6f, 0.35f) : new Color(1f, 0.7f, 0.7f);
        }

        // ── juice (carrot_catch.gd _catch / _on_item_missed; the frame already pops the pill, the hit tone and the streak line) ──

        /// <summary>Field position just above the basket rim (where carrot_catch.gd anchors its float texts).</summary>
        private Vector2 BasketTop()
        {
            var ppu = (float)_game.PxPerUnit * FieldW / (float)CarrotCatchGame.ViewW;
            var sy = FieldH / (float)CarrotCatchGame.ViewH;
            return new Vector2(FieldW * 0.5f + (float)_game.BasketX * ppu, (float)_game.BasketY * sy - 30f);
        }

        private void OnCaught(CarrotCatchGame.FallingItem item, int delta)
        {
            StageCatch(item);
            if (FieldW <= 0f) return;
            var at = BasketTop();
            if (item.Kind == "golden")
            {
                // THE beat of the round: gold pill (banner), a gold shower, confetti, a bright flash
                Banner(Host.Strings.T("mg.hudkit.beat_gold", ("n", item.Value)), 1.2);
                Burst(at, GoldC, 24);
                Burst(at, Color.white, 10);
                Float(at, "+" + item.Value, GoldC, 34f);
                ConfettiRain(36);
                Flash(new Color(1f, 0.9f, 0.5f, 0.22f), 0.22f);
                ScalePop(_basket, 1.3f);
                PlaySfx("game_perfect");
                Haptics.Success();
            }
            else if (item.Kind == "junk" || item.Kind == "rotten")
            {
                // "Igitt!" rises from the basket (carrot_catch.gd float_text) instead of a banner card
                Burst(at, JunkDust, 12);
                Float(at, Host.Strings.T("mg.carrotCatch.yuck"), JunkC, 28f);
                Flash(new Color(0.9f, 0.35f, 0.3f, 0.16f), 0.16f);
                ScalePop(_basket, 0.82f);
                // the frame rattles on the lost points already; junk at zero points still needs its knock
                if (delta == 0 && !ReducedMotion) Shake(0.3f);
                if (delta == 0) PlaySfx("game_miss");
                Haptics.Warn();
            }
            else
            {
                Burst(at, TintFor(item), 8);
                Float(at, "+" + item.Value, GoodC, 26f);
                ScalePop(_basket, 1.14f);
            }
        }

        /// <summary>Good food hit the ground: a dust puff + soft plop there; in endless a lost carrot is a lost life.</summary>
        private void OnMissed(CarrotCatchGame.FallingItem item)
        {
            if (FieldW <= 0f) return;
            var ppu = (float)_game.PxPerUnit * FieldW / (float)CarrotCatchGame.ViewW;
            var at = new Vector2(FieldW * 0.5f + (float)item.X * ppu, FieldH - 26f);
            if (_game.Tune.Endless && item.Kind == "good" && item.Key == "carrot")
            {
                Burst(at, JunkC, 10);
                Float(at + new Vector2(0f, -30f), Host.Strings.T("mg.carrotCatch.missed", ("n", _game.MissedCarrots), ("max", _game.Tune.EndlessMissedCarrots)), JunkC, 24f);
                if (!ReducedMotion) Shake(0.25f);
                PlaySfx("game_miss");
                Haptics.Warn();
                return;
            }
            Burst(at, GroundDust, 6);
            PlaySfx("game_pop", -5f, 0.7f);
        }

        /// <summary>Every fifth catch in a row: the streak banner, a pink burst over the basket, a star tone climbing the ladder.</summary>
        private void OnComboMilestone(int n)
        {
            Banner(Host.Strings.T("mg.game.streak", ("n", n)), 1.0);
            if (FieldW <= 0f) return;
            var at = BasketTop() + new Vector2(0f, -30f);
            Burst(at, StreakC, 18);
            PlaySfx("game_star", Sfx.ComboSemitones(n / 5));
            Haptics.Tap();
            if (n % 10 == 0) ConfettiRain(24);
        }

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _views.Clear();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        // ── 3D stage ─────────────────────────────────────────────────────────

        private void BuildStage()
        {
            _stage = new World.MgStage3D(CarrotCatchGame.ViewW, CarrotCatchGame.ViewH, new Color(0.66f, 0.83f, 0.98f));
            _stage.Bind(Field);
            var o = World.MgStage3D.Origin;
            _groundY = _stage.ViewToWorld(0, _game.BasketY).y - 0.42f;
            var grass = World.ArtLibrary.PbrSlab("grass", new Vector2(40f, 40f), 2.4f, World.ArtLibrary.Hex("#9CC49E"), 0.04f, 0.7f);
            World.ArtLibrary.Box(_stage.Root.transform, "Ground", new Vector3(o.x, _groundY - 1f, o.z + 14f), new Vector3(40f, 2f, 32f), grass, false);
            var soil = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#A8835E"), 0.1f);
            for (var row = 0; row < 3; row++)
            {
                var z = 1.6f + row * 1.25f;
                World.ArtLibrary.Box(_stage.Root.transform, "Bed", new Vector3(o.x, _groundY + 0.06f, o.z + z), new Vector3(5.6f, 0.14f, 0.8f), soil, false);
                for (var i = 0; i < 6; i++)
                    _stage.Prop("crop_carrot", new Vector3(o.x - 2.4f + i * 0.95f + (row % 2) * 0.4f, _groundY + 0.12f, o.z + z), 0.9f, i * 37f);
            }
            for (var i = -2; i <= 2; i++) _stage.PlaceScenery("picket_fence", new Vector3(o.x + i * 1.9f, _groundY, o.z + 0.95f), 0f, 0.9f);
            foreach (var (x, z, sc) in new[] { (-3.8f, 7f, 1.5f), (3.6f, 8f, 1.8f), (-1.2f, 12f, 1.6f), (5.5f, 13f, 1.4f), (-6f, 11f, 1.7f) })
                _stage.PlaceScenery("tree_round", new Vector3(o.x + x, _groundY, o.z + z), x * 20f, sc);
            foreach (var (x, z, m) in new[] { (-1.7f, -0.6f, "flower_redA"), (1.9f, -0.5f, "flower_yellowA"), (-2.6f, 0.2f, "flower_yellowA"), (2.7f, 0.3f, "flower_redA") })
                _stage.Prop(m, new Vector3(o.x + x, _groundY, o.z + z), 0.8f);
            World.ArtLibrary.Horizon(_stage.Root.transform, "bg_town_horizon", new Vector3(o.x, _groundY - 3f, o.z + 34f), 24f);
            // the basket: woven tub with a rim, carried right at the engine's catch line
            var basket = new GameObject("Basket").transform;
            basket.SetParent(_stage.Root.transform, false);
            var wicker = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#D9A066"), 0.15f);
            var rim = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#B07B45"), 0.2f);
            World.ArtLibrary.Cylinder(basket, "Tub", Vector3.zero, 0.44f, 0.34f, wicker);
            World.ArtLibrary.Cylinder(basket, "Rim", Vector3.up * 0.17f, 0.47f, 0.05f, rim);
            foreach (Transform c in basket) c.localPosition = c.position;
            _basket3d = basket;
            _lastBasketX = (float)_game.BasketX;
            PlaceBasket();
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = _stage.SpawnGoobyAsync(new Vector3(o.x, _groundY, o.z + 0.35f), 1.75f, outfit);
        }

        private Vector3 BasketWorld() => _stage.ViewToWorld(CarrotCatchGame.ViewW * 0.5 + _game.BasketX * _game.PxPerUnit, _game.BasketY);

        private void PlaceBasket()
        {
            if (_basket3d == null) return;
            var p = BasketWorld();
            var squash = 1f + Mathf.Sin(_goobyBounce * Mathf.PI) * 0.18f;
            _basket3d.position = p + new Vector3(0f, -0.02f, -0.12f);
            _basket3d.localScale = new Vector3(squash, 2f - squash, squash);
            _basket3d.rotation = Quaternion.Euler(0f, 0f, _game.Dizzy ? Mathf.Sin(Time.time * 18f) * 10f : 0f);
        }

        private void RenderStage(HashSet<CarrotCatchGame.FallingItem> live)
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _goobyBounce = Mathf.Max(0f, _goobyBounce - dt * 3f);
            PlaceBasket();
            var g = _stage.Gooby;
            if (g != null)
            {
                var bp = BasketWorld();
                var moved = (float)_game.BasketX - _lastBasketX;
                _lastBasketX = (float)_game.BasketX;
                var walking = Mathf.Abs(moved) > 0.0008f;
                var targetYaw = walking ? (moved > 0f ? 125f : 235f) : 180f;
                _goobyYaw = Mathf.LerpAngle(_goobyYaw, targetYaw, 1f - Mathf.Exp(-10f * dt));
                g.transform.position = new Vector3(bp.x, _groundY + Mathf.Sin(_goobyBounce * Mathf.PI) * 0.25f, bp.z + 0.38f);
                g.transform.rotation = Quaternion.Euler(0f, _goobyYaw, 0f);
                if (walking != _goobyWalking)
                {
                    _goobyWalking = walking;
                    g.Play(walking ? "walk-loop" : "idle-loop");
                }
            }
            var t = Time.time;
            foreach (var item in _game.Items)
            {
                if (!_props.TryGetValue(item, out var p))
                {
                    var model = item.Kind == "rotten" ? "fish-bones" : item.Kind == "junk" ? (item.Key == "fish-bones" ? "fish-bones" : "soda-can-crushed") : item.Key;
                    p = _stage.Prop(model, _stage.ViewToWorld(0, 0), item.Kind == "golden" ? 1.3f : 1.05f);
                    if (item.Kind == "golden")
                        World.ArtLibrary.Sphere(p, "Glow", p.position + Vector3.up * 0.3f, 0.34f, World.ArtLibrary.Glow(new Color(2.2f, 1.7f, 0.5f)), false);
                    _props[item] = p;
                }
                var phase = (item.GetHashCode() & 255) * 0.1f;
                p.position = _stage.ViewToWorld(CarrotCatchGame.ViewW * 0.5 + item.X * _game.PxPerUnit, item.Y) + Vector3.down * 0.25f;
                p.rotation = Quaternion.Euler(Mathf.Sin(t * 2f + phase) * 25f, t * 110f + phase * 40f, Mathf.Cos(t * 1.7f + phase) * 20f);
            }
            var dead = new List<CarrotCatchGame.FallingItem>();
            foreach (var kv in _props) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead)
            {
                if (_props[d] != null) Object.Destroy(_props[d].gameObject);
                _props.Remove(d);
            }
        }

        /// <summary>Catch feedback on the stage: Gooby hops, the basket squashes, sparkles fly from the rim.</summary>
        private void StageCatch(CarrotCatchGame.FallingItem item)
        {
            if (_stage == null) return;
            var at = BasketWorld() + Vector3.up * 0.3f;
            var bad = item.Kind == "junk" || item.Kind == "rotten";
            _goobyBounce = bad ? 0f : 1f;
            World.Effects.Puff(at, bad ? new Color(0.55f, 0.5f, 0.45f, 0.9f) : item.Kind == "golden" ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.95f, 0.7f), item.Kind == "golden" ? 18 : 8, 0.16f, 0.6f, 1.6f, Vector3.up * 2.2f, 1.2f);
            _stage.Gooby?.SetEmotion(bad ? "dizzy" : item.Kind == "golden" ? "ecstatic" : "happy");
        }

        private void DropStage()
        {
            _props.Clear();
            _basket3d = null;
            _stage?.Destroy();
            _stage = null;
            _goobyWalking = false;
        }
    }
}
