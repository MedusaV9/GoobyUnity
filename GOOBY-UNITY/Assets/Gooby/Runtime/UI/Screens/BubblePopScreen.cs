using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Blasen-Platzer host on the shared minigame frame (port of bubble_pop.gd + its stage): pastel bubbles with a
    /// food inside rise through the water of the portrait field, the target food sits in a pill at the top and
    /// rotates every twelve seconds; a tap pops the bubble under the finger — match +2, wrong −2 and a short stun
    /// (the water greys), spiky bubbles never pop (−1). Three of a kind in two seconds burst their neighbours.
    /// </summary>
    public sealed class BubblePopScreen : MinigameHostScreen
    {
        private BubblePopGame _game;
        private readonly Dictionary<BubblePopGame.Bubble, VisualElement> _views = new Dictionary<BubblePopGame.Bubble, VisualElement>();
        private VisualElement _targetPill, _targetIcon;
        private Label _targetLabel;
        private float _wobble;
        private string _shownTarget;

        private static readonly Color Water = new Color(0.72f, 0.9f, 0.95f);
        private static readonly Color WaterStunned = new Color(0.8f, 0.8f, 0.86f);
        private static readonly Color WaterWrong = new Color(0.98f, 0.72f, 0.7f);
        private float _flashUntil;
        // the 3D lagoon (Godot bubble_pop_stage3d.gd): glossy bubbles with the real foods inside rise past seaweed
        private World.MgStage3D _stage;
        private readonly Dictionary<BubblePopGame.Bubble, Transform> _bubbles3d = new Dictionary<BubblePopGame.Bubble, Transform>();
        private readonly List<Transform> _weeds = new List<Transform>();

        public BubblePopScreen(GameHost host) : base(host, "bubblePop", "bubblePop") { }

        protected override (double w, double h) ViewSize => (BubblePopGame.ViewW, BubblePopGame.ViewH);
        protected override bool Running => _game != null;
        protected override bool StageHasGooby => _stage != null;
        protected override Color BackdropColor => MgScenery.Hex(0x8FD3E3);
        protected override Color FieldColor => Water;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public BubblePopGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new BubblePopGame(Difficulty, seed);
            _game.SetAspect(BubblePopGame.ViewW / BubblePopGame.ViewH);
            _game.Popped += OnPopped;
            _game.Chain += (b, n) => Banner(Host.Strings.T("mg.bubblePop.chain"), 1.0);
            _game.StreakMilestone += n => Banner(Host.Strings.T("mg.game.streak", ("n", n)), 1.0);
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e => Tap(e.localPosition));
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the target badge under the pills: "Zerplatze: Apfel" with the food's icon
            _targetPill = Ux.El("gb-card", "gb-row");
            _targetPill.name = "target-pill";
            Ux.Abs(_targetPill, null, 62);
            _targetPill.style.left = Length.Percent(50);
            _targetPill.style.translate = new Translate(Length.Percent(-50), 0);
            _targetPill.style.alignItems = Align.Center;
            _targetPill.style.paddingTop = 6; _targetPill.style.paddingBottom = 6; _targetPill.style.paddingLeft = 12; _targetPill.style.paddingRight = 16;
            _targetPill.pickingMode = PickingMode.Ignore;
            _targetIcon = Ux.Icon("food_carrot");
            _targetIcon.style.width = 40; _targetIcon.style.height = 40; _targetIcon.style.marginRight = 8;
            _targetPill.Add(_targetIcon);
            _targetLabel = Ux.Text("", "gb-md", "gb-bold");
            _targetPill.Add(_targetLabel);
            Root.Add(_targetPill);
            ComboLabel.style.top = 128;
            RefreshTarget();
            Banner(t.T("mg.bubblePop.intro"), BubblePopGame.IntroSec + 0.7);
        }

        // ── scenery (board 06, "Bucht"): a sunny sky sliver over soft waves, light rays in pastel sea water, a far reef,
        //    swaying seaweed and the sandy floor with ripples, shells, a starfish and pebbles. Built once, pointer-
        //    transparent, behind the bubbles; the surface waves and a sheet of faint background bubbles move (two
        //    translates a frame, still with reduced motion). The wrong/stunned tint is a veil on top of the scenery. ──

        private VisualElement _waves, _fizz, _veil;
        private int _veilState = -1;
        private float _sceneT;

        private void BuildScenery()
        {
            _veilState = -1;
            var scene = MgScenery.Layer(Field, "mgf-bubblepop");
            MgScenery.Gradient(scene, MgScenery.Hex(0xC9F1F6), MgScenery.Hex(0x86D2E4), 0f, 100f);
            // the sky sliver over the surface with a little sun, then the waves rolling on it
            MgScenery.Gradient(scene, MgScenery.Hex(0xBFE3FF), MgScenery.Hex(0xFFF3E2), 0f, 7.5f);
            var sun = MgScenery.Sun(scene, 30f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, 8f, 0.6f);
            _waves = MgScenery.Waves(scene, 9, 7f, MgScenery.Hex(0xB4EAF3), new Color(1f, 1f, 1f, 0.9f));
            _waves.name = "mgf-waves";
            MgScenery.Pct(_waves, -8f, 6f, -8f);
            _waves.style.height = Length.Percent(4f);
            // light rays from the surface
            var rayC = new Color(1f, 1f, 1f, 0.07f);
            float[] rayAt = { 10f, 42f, 70f }; float[] rayRot = { -10f, 4f, 14f };
            for (var i = 0; i < rayAt.Length; i++)
            {
                var ray = MgScenery.Beam(scene, rayC, 0.3f);
                MgScenery.Pct(ray, rayAt[i] - 12f, 8f);
                ray.style.width = Length.Percent(34f); ray.style.height = Length.Percent(58f);
                ray.style.transformOrigin = new TransformOrigin(Length.Percent(50), 0f, 0f);
                ray.style.rotate = new Rotate(rayRot[i]);
            }
            // faint bubbles far back, a sheet twice the field tall so it can rise and wrap
            _fizz = MgScenery.Painted(scene, (p, r) =>
            {
                for (var k = 0; k < 2; k++)
                    for (var i = 0; i < 14; i++)
                    {
                        var at = new Vector2(MgScenery.Hash(i, 5) * r.width, (MgScenery.Hash(i, 9) + k) * r.height * 0.5f);
                        var rr = 3f + MgScenery.Hash(i, 13) * 7f;
                        p.strokeColor = new Color(1f, 1f, 1f, 0.35f); p.lineWidth = 2f;
                        p.BeginPath(); p.Arc(at, rr, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
                        MgScenery.Disc(p, at + new Vector2(-rr * 0.35f, -rr * 0.35f), rr * 0.25f, new Color(1f, 1f, 1f, 0.45f));
                    }
            }, "gb-mgf-fizz");
            _fizz.name = "mgf-fizz";
            MgScenery.Pct(_fizz, 0, 0, 0);
            _fizz.style.height = Length.Percent(200f);
            // the far reef: hazy rounded rocks and coral puffs
            var reef = MgScenery.Puffs(scene, 6, MgScenery.Hex(0xA3DCE6), MgScenery.Hex(0xC2EBF1), 4);
            MgScenery.Pct(reef, -4f, null, -4f, 8f);
            reef.style.height = Length.Percent(12f);
            var coral = MgScenery.Puffs(scene, 3, MgScenery.Hex(0xFFC2CF), MgScenery.Hex(0xFFE0E6), 8);
            MgScenery.Pct(coral, 64f, null, 4f, 9.5f);
            coral.style.height = Length.Percent(6f);
            // seaweed ribbons swaying up from the floor
            Seaweed(scene, 4f, 16f, MgScenery.Hex(0x7CCB9E), 0);
            Seaweed(scene, 20f, 11f, MgScenery.Hex(0x9EDBA4), 1);
            Seaweed(scene, 78f, 18f, MgScenery.Hex(0x7CCB9E), 2);
            Seaweed(scene, 90f, 12f, MgScenery.Hex(0xA8E0B0), 3);
            // the sandy floor: a soft dune with ripples, pebbles, shells and a starfish
            var sand = MgScenery.Shape(scene, MgScenery.Hex(0xF6E3BE), 0f, "gb-mgf-sand");
            sand.name = "mgf-sand";
            MgScenery.Pct(sand, -6f, null, -6f, 0);
            sand.style.height = Length.Percent(10.5f);
            MgScenery.RoundTop(sand, 90f);
            sand.style.borderTopWidth = 5; sand.style.borderTopColor = MgScenery.Hex(0xFFF3DA);
            var ripples = MgScenery.Painted(sand, (p, r) =>
            {
                p.strokeColor = MgScenery.Hex(0xE6CC9C); p.lineWidth = 3f;
                for (var i = 0; i < 7; i++)
                {
                    var x = r.width * (0.1f + 0.8f * MgScenery.Hash(i, 21)); var y = r.height * (0.25f + 0.6f * MgScenery.Hash(i, 22));
                    var w = 18f + 16f * MgScenery.Hash(i, 23);
                    p.BeginPath(); p.MoveTo(new Vector2(x - w, y)); p.QuadraticCurveTo(new Vector2(x, y - 7f), new Vector2(x + w, y)); p.Stroke();
                }
                for (var i = 0; i < 10; i++)
                    MgScenery.Oval(p, new Vector2(r.width * MgScenery.Hash(i, 31), r.height * (0.3f + 0.65f * MgScenery.Hash(i, 32))), 3f + 2f * MgScenery.Hash(i, 33), 2.2f + 1.5f * MgScenery.Hash(i, 33), i % 2 == 0 ? MgScenery.Hex(0xE0CDB0) : MgScenery.Hex(0xD7E3E8));
            });
            Ux.Abs(ripples, 0, 0, 0, 0);
            Shell(sand, 18f, 30f, MgScenery.Hex(0xFFB8C8));
            Shell(sand, 60f, 55f, MgScenery.Hex(0xFFD4A8));
            var star = Starfish(sand, 30f, MgScenery.Hex(0xFF9B85));
            MgScenery.Pct(star, 80f, 22f);
            star.style.rotate = new Rotate(14f);
            // the wrong/stunned tint (was the field colour; the scenery now covers it)
            _veil = MgScenery.Shape(scene, Color.clear, 0f, "gb-mgf-veil");
            _veil.name = "mgf-veil";
            Ux.Abs(_veil, 0, 0, 0, 0);
        }

        private static void Seaweed(VisualElement scene, float leftPct, float tallPct, Color c, int seed)
        {
            var weed = MgScenery.Painted(scene, (p, r) =>
            {
                p.fillColor = c;
                for (var s = 0; s < 2; s++)
                {
                    var x0 = r.width * (0.3f + s * 0.35f); var top = r.height * (s == 0 ? 0f : 0.25f);
                    var bend = (MgScenery.Hash(seed, s) - 0.5f) * 10f;
                    p.BeginPath(); p.MoveTo(new Vector2(x0 - 5f, r.height));
                    p.BezierCurveTo(new Vector2(x0 - 16f + bend, r.height * 0.7f), new Vector2(x0 + 12f + bend, r.height * 0.4f), new Vector2(x0 + bend, top));
                    p.BezierCurveTo(new Vector2(x0 + 18f + bend, r.height * 0.4f), new Vector2(x0 - 6f + bend, r.height * 0.7f), new Vector2(x0 + 6f, r.height));
                    p.ClosePath(); p.Fill();
                    p.strokeColor = MgScenery.Darker(c, 0.14f); p.lineWidth = 2f; p.Stroke();
                }
            }, "gb-mgf-seaweed");
            MgScenery.Pct(weed, leftPct, null, null, 6f);
            weed.style.width = 44f; weed.style.height = Length.Percent(tallPct);
        }

        private static void Shell(VisualElement sand, float leftPct, float topPct, Color c)
        {
            var shell = MgScenery.Painted(sand, (p, r) =>
            {
                var foot = new Vector2(r.width * 0.5f, r.height - 2f); var rr = r.width * 0.5f - 2f;
                p.fillColor = c;
                p.BeginPath(); p.MoveTo(foot); p.Arc(foot, rr, Angle.Degrees(180f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
                p.strokeColor = MgScenery.Darker(c, 0.18f); p.lineWidth = 2f; p.Stroke();
                for (var i = 1; i < 5; i++)
                {
                    var a = Mathf.PI + Mathf.PI * i / 5f;
                    p.BeginPath(); p.MoveTo(foot); p.LineTo(foot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr * 0.9f); p.Stroke();
                }
            }, "gb-mgf-shell");
            MgScenery.Pct(shell, leftPct, topPct);
            MgScenery.Size(shell, 26f, 16f);
        }

        private static VisualElement Starfish(VisualElement parent, float size, Color c)
        {
            var e = MgScenery.Painted(parent, (p, r) =>
            {
                var m = new Vector2(r.width * 0.5f, r.height * 0.5f); var ro = r.width * 0.48f; var ri = ro * 0.45f;
                p.fillColor = c;
                p.BeginPath();
                for (var i = 0; i < 10; i++)
                {
                    var a = (-90f + i * 36f) * Mathf.Deg2Rad; var rr = i % 2 == 0 ? ro : ri;
                    var pt = m + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.ClosePath(); p.Fill();
                p.strokeColor = MgScenery.Darker(c, 0.2f); p.lineWidth = 2.5f; p.Stroke();
                MgScenery.Disc(p, m, 2f, MgScenery.Lighter(c, 0.5f));
            }, "gb-mgf-starfish");
            MgScenery.Size(e, size, size);
            return e;
        }

        /// <summary>Surface waves bob sideways, the faint bubble sheet rises and wraps (two translates a frame).</summary>
        private void MoveScenery()
        {
            if (FieldH <= 0f || _waves == null) return;
            var t = ReducedMotion ? 0f : _sceneT;
            _waves.style.translate = new Translate(Mathf.Sin(t * 0.9f) * 14f, 0);
            _fizz.style.translate = new Translate(0, -Mathf.Repeat(t * 16f, FieldH));
            var state = Time.unscaledTime < _flashUntil ? 2 : _game.Stunned ? 1 : 0;
            if (state == _veilState) return;
            _veilState = state;
            _veil.style.backgroundColor = state == 2 ? new Color(WaterWrong.r, WaterWrong.g, WaterWrong.b, 0.55f) : state == 1 ? new Color(WaterStunned.r, WaterStunned.g, WaterStunned.b, 0.5f) : Color.clear;
        }

        private static string FoodKey(string food) => food == "donut-sprinkles" ? "donut" : food;

        private void RefreshTarget()
        {
            var food = _game.Target;
            if (food == _shownTarget) return;
            _shownTarget = food;
            _targetLabel.text = Host.Strings.T("mg.bubblePop.target", ("food", Host.Strings.T("mg.bubblePop.food_" + FoodKey(food))));
            var tex = Ux.IconTexture("food_" + FoodKey(food));
            if (tex != null) _targetIcon.style.backgroundImage = new StyleBackground(tex);
        }

        private void Tap(Vector2 local)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            _game.TapNormalized(local.x / FieldW, local.y / FieldH);
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _wobble += (float)dt;
            _sceneT += (float)dt;
        }

        private float Ppu => FieldH / (float)(2.0 * _game.HalfH);

        protected override void RenderGame()
        {
            if (_stage == null) MoveScenery();
            var ppu = Ppu;
            var live = new HashSet<BubblePopGame.Bubble>(_game.Bubbles);
            if (_stage != null) RenderStage(live);
            else foreach (var b in _game.Bubbles)
            {
                if (!_views.TryGetValue(b, out var v))
                {
                    v = BubbleView(b);
                    _views[b] = v;
                    Field.Add(v);
                }
                var r = (float)(b.Kind == "spiky" ? BubblePopGame.SpikyR : BubblePopGame.BubbleR) * ppu;
                var wob = Mathf.Sin(_wobble * 2.1f + (float)b.Wobble) * 0.06f;
                v.style.left = FieldW * 0.5f + (float)(b.X + wob) * ppu - r;
                v.style.top = FieldH * 0.5f - (float)b.Y * ppu - r;
            }
            var dead = new List<BubblePopGame.Bubble>();
            foreach (var kv in _views) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _views[d].RemoveFromHierarchy(); _views.Remove(d); }

            RefreshTarget();
            TimeLabel.text = _game.Tune.Endless
                ? Host.Strings.T("mg.bubblePop.spikes", ("n", _game.SpikyPops), ("max", _game.Tune.EndlessSpikyLimit))
                : Host.Strings.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            ComboLabel.text = _game.Streak >= 2 ? Host.Strings.T("mg.game.streak", ("n", _game.Streak)) : "";
        }

        private static readonly Dictionary<string, Color> FoodTint = new Dictionary<string, Color>
        {
            ["carrot"] = new Color(1f, 0.72f, 0.5f), ["apple"] = new Color(1f, 0.62f, 0.62f), ["banana"] = new Color(1f, 0.9f, 0.55f),
            ["cheese"] = new Color(1f, 0.85f, 0.45f), ["donut-sprinkles"] = new Color(1f, 0.7f, 0.85f), ["cupcake"] = new Color(0.85f, 0.75f, 1f),
        };

        /// <summary>A bubble: translucent pastel disc with a white rim, a highlight and the food (or the spikes) inside.</summary>
        private VisualElement BubbleView(BubblePopGame.Bubble b)
        {
            var spiky = b.Kind == "spiky";
            var d = (float)(spiky ? BubblePopGame.SpikyR : BubblePopGame.BubbleR) * 2f * Ppu;
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.width = d; v.style.height = d;
            v.style.borderTopLeftRadius = d; v.style.borderTopRightRadius = d; v.style.borderBottomLeftRadius = d; v.style.borderBottomRightRadius = d;
            var tint = spiky ? new Color(0.55f, 0.58f, 0.66f) : FoodTint.TryGetValue(b.Food, out var c) ? c : Color.white;
            v.style.backgroundColor = new Color(tint.r, tint.g, tint.b, spiky ? 0.85f : 0.55f);
            var rim = spiky ? new Color(0.35f, 0.36f, 0.45f, 0.9f) : new Color(1f, 1f, 1f, 0.9f);
            v.style.borderTopColor = rim; v.style.borderBottomColor = rim; v.style.borderLeftColor = rim; v.style.borderRightColor = rim;
            v.style.borderTopWidth = 3; v.style.borderBottomWidth = 3; v.style.borderLeftWidth = 3; v.style.borderRightWidth = 3;
            v.style.justifyContent = Justify.Center; v.style.alignItems = Align.Center;
            var icon = Ux.Icon(spiky ? "spiky" : "food_" + FoodKey(b.Food));
            icon.style.width = d * 0.62f; icon.style.height = d * 0.62f;
            icon.pickingMode = PickingMode.Ignore;
            v.Add(icon);
            if (!spiky)
            {
                var shine = new VisualElement { pickingMode = PickingMode.Ignore };
                shine.style.position = Position.Absolute;
                shine.style.left = d * 0.16f; shine.style.top = d * 0.12f;
                shine.style.width = d * 0.2f; shine.style.height = d * 0.13f;
                shine.style.borderTopLeftRadius = d; shine.style.borderTopRightRadius = d; shine.style.borderBottomLeftRadius = d; shine.style.borderBottomRightRadius = d;
                shine.style.backgroundColor = new Color(1f, 1f, 1f, 0.8f);
                shine.style.rotate = new Rotate(-30f);
                v.Add(shine);
            }
            return v;
        }

        private void OnPopped(BubblePopGame.Bubble b, string result, int delta)
        {
            var t = Host.Strings;
            var ppu = Ppu;
            var at = new Vector2(FieldW * 0.5f + (float)b.X * ppu, FieldH * 0.5f - (float)b.Y * ppu);
            if (_stage != null)
            {
                var w = StageAt(b.X, b.Y);
                World.Effects.Puff(w, result == "match" ? new Color(0.9f, 1f, 1f, 0.95f) : new Color(1f, 0.6f, 0.6f, 0.9f), result == "match" ? 14 : 8, 0.12f, 0.55f, 1.4f, Vector3.up * 1.2f, 1.6f);
                _stage.Gooby?.SetEmotion(result == "match" ? "happy" : "dizzy");
            }
            switch (result)
            {
                case "match":
                    FloatText(at, "+" + delta, new Color(0.24f, 0.55f, 0.32f));
                    break;
                case "wrong":
                    _flashUntil = Time.unscaledTime + 0.18f;
                    FloatText(at, t.T("mg.bubblePop.wrong"), new Color(0.85f, 0.3f, 0.3f));
                    break;
                default:
                    FloatText(at, t.T("mg.bubblePop.spiky"), new Color(0.85f, 0.3f, 0.3f));
                    break;
            }
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _views.Clear();
            _shownTarget = null;
            _targetPill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        // ── 3D stage ─────────────────────────────────────────────────────────

        private float StageScale => (float)(BubblePopGame.ViewH * World.MgStage3D.MetresPerPx / (2.0 * _game.HalfH));

        private Vector3 StageAt(double x, double y, float depth = 0f) =>
            World.MgStage3D.Origin + new Vector3((float)x * StageScale, (float)BubblePopGame.ViewH * World.MgStage3D.MetresPerPx * 0.5f + (float)y * StageScale, depth);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(BubblePopGame.ViewW, BubblePopGame.ViewH, new Color(0.42f, 0.74f, 0.86f));
            _stage.Bind(Field);
            var o = World.MgStage3D.Origin;
            var root = _stage.Root.transform;
            // a shallow sand shelf closed by the lagoon's water wall: the level camera must not see the floor recede
            World.ArtLibrary.Box(root, "Sand", new Vector3(o.x, -0.45f, o.z + 2.2f), new Vector3(40f, 1.2f, 6.4f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F2DDB0"), 0.1f), false);
            World.ArtLibrary.Box(root, "Deep", new Vector3(o.x, 2.4f, o.z + 5.6f), new Vector3(60f, 6f, 0.2f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#5FA8C8"), 0.05f), false);
            World.ArtLibrary.Box(root, "Mid", new Vector3(o.x, 8.4f, o.z + 5.7f), new Vector3(60f, 6f, 0.2f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#79BFD8"), 0.05f), false);
            World.ArtLibrary.Box(root, "Top", new Vector3(o.x, 14.4f, o.z + 5.8f), new Vector3(60f, 6f, 0.2f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#9ED6E8"), 0.05f), false);
            var weed = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#5DBB7A"), 0.3f);
            var weedDark = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#3E9A6A"), 0.3f);
            for (var i = 0; i < 9; i++)
            {
                var x = -2.6f + i * 0.65f + (i % 2) * 0.2f;
                var z = 1.2f + (i % 3) * 1.4f;
                var stalk = new GameObject("Weed").transform;
                stalk.SetParent(root, false);
                stalk.position = new Vector3(o.x + x, 0.2f, o.z + z);
                var h = 1.2f + (i * 37 % 10) * 0.12f;
                for (var k = 0; k < 4; k++)
                {
                    var seg = World.ArtLibrary.Box(stalk, "Leaf", Vector3.zero, new Vector3(0.16f, h * 0.28f, 0.05f), i % 2 == 0 ? weed : weedDark, false);
                    seg.transform.localPosition = new Vector3(Mathf.Sin(k * 1.3f) * 0.06f, h * 0.14f + k * h * 0.25f, 0f);
                }
                _weeds.Add(stalk);
            }
            foreach (var (x, z, sc) in new[] { (-1.9f, 0.6f, 1.6f), (2.1f, 1.0f, 2.0f), (0.4f, 2.8f, 2.4f), (-3.2f, 3.4f, 2.2f) })
                _stage.HomeProp("rock", new Vector3(o.x + x, 0.15f, o.z + z), sc, x * 30f);
            var shell = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFC9D6"), 0.5f);
            foreach (var (x, z) in new[] { (-0.8f, 0.2f), (1.2f, -0.1f), (2.6f, 0.5f) })
                World.ArtLibrary.Sphere(root, "Shell", new Vector3(o.x + x, 0.22f, o.z + z), 0.14f, shell, false).transform.localScale = new Vector3(0.32f, 0.16f, 0.26f);
            var ray = World.ArtLibrary.Transparent(new Color(1f, 1f, 0.92f, 0.12f));
            for (var i = 0; i < 4; i++)
                World.ArtLibrary.Quad(root, "Ray", new Vector3(o.x - 2f + i * 1.4f, 5.2f, o.z + 5f), Quaternion.Euler(0f, 180f, 12f + i * 4f), new Vector2(0.6f, 14f), ray);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = _stage.SpawnGoobyAsync(new Vector3(o.x + 1.45f, 0.2f, o.z + 0.6f), 1.5f, outfit);
        }

        private void RenderStage(HashSet<BubblePopGame.Bubble> live)
        {
            var t = Time.time;
            for (var i = 0; i < _weeds.Count; i++)
                if (_weeds[i] != null) _weeds[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f + i) * 8f);
            var scale = StageScale;
            foreach (var b in _game.Bubbles)
            {
                var spiky = b.Kind == "spiky";
                if (!_bubbles3d.TryGetValue(b, out var tr))
                {
                    tr = new GameObject("Bubble").transform;
                    tr.SetParent(_stage.Root.transform, false);
                    var r = (float)(spiky ? BubblePopGame.SpikyR : BubblePopGame.BubbleR) * scale;
                    if (spiky)
                    {
                        var steel = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#6E7486"), 0.6f);
                        World.ArtLibrary.Sphere(tr, "Core", Vector3.zero, r * 0.8f, steel);
                        for (var k = 0; k < 10; k++)
                        {
                            var dir = Quaternion.Euler(k * 36f, k * 71f, 0f) * Vector3.up;
                            var spike = World.ArtLibrary.Cylinder(tr, "Spike", dir * r * 0.85f, r * 0.12f, r * 0.55f, steel, false);
                            spike.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                        }
                    }
                    else
                    {
                        var tint = FoodTint.TryGetValue(b.Food, out var c) ? c : Color.white;
                        var skin = Color.Lerp(tint, Color.white, 0.5f);
                        World.ArtLibrary.Sphere(tr, "Skin", Vector3.zero, r, World.ArtLibrary.Transparent(new Color(skin.r, skin.g, skin.b, 0.42f)), false);
                        World.ArtLibrary.Sphere(tr, "Shine", new Vector3(-r * 0.38f, r * 0.42f, -r * 0.6f), r * 0.2f, World.ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.75f)), false);
                        var food = _stage.Prop(b.Food, Vector3.zero, 0.95f);
                        food.SetParent(tr, false);
                        food.localPosition = Vector3.down * r * 0.45f;
                    }
                    foreach (Transform c in tr) if (c.name != "Mg:" + b.Food) c.localPosition = c.position;
                    _bubbles3d[b] = tr;
                }
                var wob = Mathf.Sin(_wobble * 2.1f + (float)b.Wobble) * 0.06f;
                tr.position = StageAt(b.X + wob, b.Y);
                tr.rotation = Quaternion.Euler(0f, t * 40f + (float)b.Wobble * 50f, Mathf.Sin(t * 1.6f + (float)b.Wobble) * 8f);
            }
            var dead = new List<BubblePopGame.Bubble>();
            foreach (var kv in _bubbles3d) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead)
            {
                if (_bubbles3d[d] != null) Object.Destroy(_bubbles3d[d].gameObject);
                _bubbles3d.Remove(d);
            }
        }

        private void DropStage()
        {
            _bubbles3d.Clear();
            _weeds.Clear();
            _stage?.Destroy();
            _stage = null;
        }
    }
}
