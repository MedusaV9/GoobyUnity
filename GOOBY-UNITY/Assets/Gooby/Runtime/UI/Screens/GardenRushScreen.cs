using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Gießkannen-Wirbel host on the shared minigame frame (port of garden_rush.gd + its bed stage): a raised
    /// garden bed with eight terracotta pots in two columns under a picket fence and a strip of sky. Sprouts pop
    /// up (the seventh and eighth pot open later), droop and brown as their window runs down; holding a pot brings
    /// the watering can over it and fills the ring — let go once it turns green for a perfect (+3), earlier is okay
    /// (+1); weeds are spiky and cost when watered, wilted sprouts cost two. The one-off sprinkler waits in the
    /// corner from 30 s and refills every sprout's window by half. With a GPU the bed, pots, plants, can and gardener are
    /// the 3D stage of <c>GardenRushScreen.Stage3D.cs</c>; the canvas keeps the fill ring, wilt arcs and splashes.
    /// </summary>
    public sealed partial class GardenRushScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;
        private const float PotFill = 0.74f;

        private GardenRushGame _game;
        private BedCanvas _canvas;
        private VisualElement _gooby, _sprinklerBtn, _witheredPill;
        private Label _witheredLabel;
        private readonly List<VisualElement> _hits = new List<VisualElement>();
        private readonly float[] _splash = new float[8];
        private readonly float[] _wiltFlash = new float[8];
        private float _show, _sprinklerRain, _bloom;
        private readonly List<Drop> _drops = new List<Drop>();

        private struct Drop { public Vector2 P, V; public float Life; }

        private static readonly Color Grass = MgScenery.Hex(0xA6DB98);
        private static readonly Color Soil = MgScenery.Hex(0x9C7358);
        private static readonly Color BedWood = MgScenery.Hex(0xE3BC90);
        private static readonly Color BedWoodDark = MgScenery.Hex(0xC39468);
        private static readonly Color Terracotta = new Color(0.86f, 0.52f, 0.36f);
        private static readonly Color TerracottaDark = new Color(0.7f, 0.4f, 0.28f);
        private static readonly Color Leaf = new Color(0.45f, 0.78f, 0.42f);
        private static readonly Color LeafDark = new Color(0.24f, 0.55f, 0.32f);
        private static readonly Color Wilt = new Color(0.72f, 0.6f, 0.36f);
        private static readonly Color WeedColor = new Color(0.36f, 0.45f, 0.24f);
        private static readonly Color Can = new Color(0.5f, 0.72f, 0.85f);
        private static readonly Color Water = new Color(0.53f, 0.78f, 0.92f, 0.9f);
        private static readonly Color Ink = new Color(0.24f, 0.19f, 0.17f);

        public GardenRushScreen(GameHost host) : base(host, "gardenRush", "gardenRush") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xC2E3A0);
        protected override Color FieldColor => Grass;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public GardenRushGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new GardenRushGame(Difficulty, seed);
            _game.Watered += OnWatered;
            _game.WeedWatered += i => { FloatText(PotCentre(i) - new Vector2(0f, 40f), Host.Strings.T("mg.gardenRush.weed"), new Color(0.85f, 0.35f, 0.35f)); _wiltFlash[i] = 0.5f; StageWeed(i); };
            _game.Wilted += i => { FloatText(PotCentre(i) - new Vector2(0f, 40f), Host.Strings.T("mg.gardenRush.wilted"), new Color(0.85f, 0.35f, 0.35f)); _wiltFlash[i] = 1f; StageWilted(i); };
            _game.MorePots += () => { Banner(Host.Strings.T("mg.gardenRush.more_pots"), 2.0); _bloom = 0.5f; };
            _game.SprinklerReady += () => Banner(Host.Strings.T("mg.gardenRush.sprinkler_ready"), 2.5);
            _game.SprinklerFired += n => { Banner(Host.Strings.T("mg.gardenRush.sprinkler_used", ("n", n)), 2.5); _sprinklerRain = 1.4f; _bloom = 1f; StageSprinkler(); };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerUpEvent>(_ => _game?.Release());
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _game?.Release());

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new BedCanvas { Paint = Paint };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            // one transparent hit area per pot: the finger lands on it, the release is the field's
            for (var i = 0; i < 8; i++)
            {
                var idx = i;
                var hit = new VisualElement { name = "pot-" + i, pickingMode = PickingMode.Position };
                hit.style.position = Position.Absolute;
                hit.RegisterCallback<PointerDownEvent>(e => { if (_game != null && _game.Press(idx)) Field.CapturePointer(e.pointerId); });
                Field.Add(hit);
                _hits.Add(hit);
            }

            _sprinklerBtn = Ux.IconBtn("rain", () => _game?.FireSprinkler(), "gb-btn-icon-sm");
            _sprinklerBtn.name = "sprinkler";
            _sprinklerBtn.style.position = Position.Absolute;
            _sprinklerBtn.style.right = 14; _sprinklerBtn.style.top = 68;
            _sprinklerBtn.style.display = DisplayStyle.None;
            Field.Add(_sprinklerBtn);

            _gooby = Ux.Portrait(84);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            _witheredPill = Ux.El("gb-card", "gb-row");
            _witheredPill.name = "withered-pill";
            Ux.Abs(_witheredPill, null, 62);
            _witheredPill.style.left = Length.Percent(50);
            _witheredPill.style.translate = new Translate(Length.Percent(-50), 0);
            _witheredPill.style.alignItems = Align.Center;
            _witheredPill.style.paddingTop = 6; _witheredPill.style.paddingBottom = 6; _witheredPill.style.paddingLeft = 12; _witheredPill.style.paddingRight = 16;
            _witheredPill.pickingMode = PickingMode.Ignore;
            var icon = Ux.Icon("sprout");
            icon.style.width = 30; icon.style.height = 30; icon.style.marginRight = 8;
            _witheredPill.Add(icon);
            _witheredLabel = Ux.Text("", "gb-md", "gb-bold");
            _witheredPill.Add(_witheredLabel);
            Root.Add(_witheredPill);
            ComboLabel.style.top = 128;
            Banner(t.T("mg.gardenRush.hint"), 2.6);
        }

        // ── layout ─────────────────────────────────────────────────────────

        // ── scenery (board 06, "Gemüsegarten"): the sky strip over the bed with a sun and a cloud, a flowering hedge and
        //    the white picket fence, the lawn round the raised bed with tufts, flowers and daisies along its edges. A
        //    layer behind the bed canvas (the bed, its furrowed soil, the pots and plants stay painted); built once,
        //    pointer-transparent, nothing moves. The bed occupies 130 px (of 844) from the top to 64 px from the bottom. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-gardenrush");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 10f);
            var sun = MgScenery.Sun(scene, 30f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, null, 0.6f, 6f);
            var cloud = MgScenery.Cloud(scene, 70f);
            MgScenery.Pct(cloud, 12f, 0.5f);
            var hedge = MgScenery.Puffs(scene, 7, MgScenery.Hex(0x92CF8F), MgScenery.Hex(0xB8E6A8), 12);
            MgScenery.Pct(hedge, -3f, 3.5f, -3f);
            hedge.style.height = Length.Percent(6.5f);
            var blossoms = MgScenery.Dots(hedge, 14, MgScenery.Hex(0xFF9BC1), 2.5f, 4f, 3);
            MgScenery.Pct(blossoms, 3f, 40f, 3f, 12f);
            var fence = MgScenery.Fence(scene, 9, 40f, MgScenery.Hex(0xFFFBF2), MgScenery.Hex(0xD9C8AE));
            fence.name = "mgf-fence";
            MgScenery.Pct(fence, null, 5.2f);
            var lawn = MgScenery.Shape(scene, Grass, 0f, "gb-mgf-meadow");
            lawn.name = "mgf-lawn";
            MgScenery.Pct(lawn, 0, 9.8f, 0, 0);
            lawn.style.borderTopColor = MgScenery.Hex(0x8CCF8A);
            var tuftC = MgScenery.Hex(0x7CC486);
            for (var i = 0; i < 6; i++)
            {
                var tuft = MgScenery.Tuft(lawn, tuftC, 0.75f);
                MgScenery.Pct(tuft, 3f + i * 17f, null);
                tuft.style.top = -12f;
            }
            var petals = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFFD166), MgScenery.Hex(0xB7D8FF) };
            for (var i = 0; i < 6; i++)
            {
                var f = MgScenery.Flower(scene, petals[i % petals.Length], 0.75f);
                MgScenery.Pct(f, 4f + i * 17f, null, null, 1.2f + (i % 2) * 1.8f);
            }
            var daisyL = MgScenery.Sprite(scene, "plant_daisies", 40f, 40f);
            MgScenery.Pct(daisyL, -2f, null, null, 0.5f);
            var tulipR = MgScenery.Sprite(scene, "plant_tulips_white", 36f, 40f);
            MgScenery.Pct(tulipR, null, null, -1f, 0.5f);
            for (var i = 0; i < 5; i++)
            {
                var edge = MgScenery.Tuft(scene, tuftC, 0.7f);
                MgScenery.Pct(edge, i % 2 == 0 ? 0.3f : null, 24f + i * 14f, i % 2 == 0 ? (float?)null : 0.3f);
            }
        }

        private Rect FieldRect()
        {
            var k = FieldH / (float)ViewH;
            var top = 130f * k; var bottom = 64f * k; var inset = FieldW * 0.06f;
            return new Rect(inset, top, FieldW - inset * 2f, FieldH - top - bottom);
        }

        private Rect PotRect(int index)
        {
            var field = FieldRect();
            var cellW = field.width / 2f; var cellH = field.height / 4f;
            var col = index % 2; var row = index / 2;
            var side = Mathf.Min(cellW, cellH) * PotFill;
            var centre = new Vector2(field.x + (col + 0.5f) * cellW, field.y + (row + 0.5f) * cellH);
            return new Rect(centre.x - side * 0.5f, centre.y - side * 0.5f, side, side);
        }

        private Vector2 PotCentre(int index) => PotRect(index).center;

        // ── the round ──────────────────────────────────────────────────────

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            var f = (float)dt;
            _show += f;
            _sprinklerRain = Mathf.Max(0f, _sprinklerRain - f);
            _bloom = Mathf.Max(0f, _bloom - f);
            for (var i = 0; i < 8; i++) { _splash[i] = Mathf.Max(0f, _splash[i] - f * 2.5f); _wiltFlash[i] = Mathf.Max(0f, _wiltFlash[i] - f * 1.5f); }
            if (_game.Holding && FieldW > 0f && _stage == null)
            {
                var c = PotCentre(_game.HoldIndex); var half = PotRect(_game.HoldIndex).width * 0.5f;
                _drops.Add(new Drop { P = c + new Vector2(-half * 0.6f, -half * 1.2f), V = new Vector2(60f + (_drops.Count % 3) * 20f, 80f), Life = 0.5f });
            }
            for (var i = _drops.Count - 1; i >= 0; i--)
            {
                var d = _drops[i]; d.Life -= f; d.V.y += 500f * f; d.P += d.V * f;
                if (d.Life <= 0f) _drops.RemoveAt(i); else _drops[i] = d;
            }
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.gardenRush.withered", ("n", _game.Withered), ("max", _game.Tune.EndlessWilts))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            _witheredLabel.text = t.T("mg.gardenRush.withered", ("n", _game.Withered), ("max", _game.Tune.EndlessWilts));
            ComboLabel.text = _game.PerfectStreak >= 2 ? t.T("mg.game.streak", ("n", _game.PerfectStreak)) : "";
            for (var i = 0; i < _hits.Count; i++)
            {
                var r = PotRect(i);
                _hits[i].style.left = r.x - 10f; _hits[i].style.top = r.y - 10f;
                _hits[i].style.width = r.width + 20f; _hits[i].style.height = r.height + 20f;
                _hits[i].style.display = i < _game.ActivePots ? DisplayStyle.Flex : DisplayStyle.None;
            }
            _sprinklerBtn.style.display = _game.SprinklerAvailable ? DisplayStyle.Flex : DisplayStyle.None;
            _sprinklerBtn.style.scale = new Scale(Vector3.one * (1f + Mathf.Sin(_show * 8f) * 0.08f));
            if (_stage != null)
            {
                RenderStage();
                _canvas.MarkDirtyRepaint();
                return;
            }
            // Gooby at the foot of the bed, leaning in while watering
            var size = FieldW * 0.24f;
            _gooby.style.width = size; _gooby.style.height = size;
            _gooby.style.left = FieldW - size - 10f; _gooby.style.top = FieldH - size - 6f;
            _gooby.style.rotate = new Rotate(_game.Holding ? -10f : Mathf.Sin(_show * 2f) * 3f);
            _canvas.MarkDirtyRepaint();
        }

        // ── painting ───────────────────────────────────────────────────────

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            var field = FieldRect();
            if (_stage != null)
            {
                for (var i = 0; i < _game.Pots.Count; i++) PaintPotOverlay(p, i);
                if (_game.Holding) PaintRing(p, _game.HoldIndex);
                if (_bloom > 0f) FillRect(p, 0f, 0f, FieldW, FieldH, new Color(1f, 1f, 0.85f, 0.12f * _bloom));
                return;
            }
            // the sky strip, fence, hedge and lawn are the scenery layer behind the canvas; here the raised bed with its
            // furrowed soil
            var bedPad = 14f;
            FillRect(p, field.x - bedPad, field.y - bedPad, field.width + bedPad * 2f, field.height + bedPad * 2f, BedWood);
            FillRect(p, field.x - bedPad, field.y + field.height + bedPad - 8f, field.width + bedPad * 2f, 8f, BedWoodDark);
            FillRect(p, field.x - 4f, field.y - 4f, field.width + 8f, field.height + 8f, Soil);
            var furrow = Mathf.Max(18f, field.height / 22f);
            for (var y = field.y + furrow * 0.6f; y < field.y + field.height - 4f; y += furrow)
            {
                FillRect(p, field.x - 4f, y, field.width + 8f, furrow * 0.3f, MgScenery.Hex(0x86604A));
                FillRect(p, field.x - 4f, y - 3f, field.width + 8f, 2.5f, MgScenery.Hex(0xB48A6C));
            }
            for (var i = 0; i < 18; i++) Circle(p, new Vector2(field.x + (i * 47) % (int)field.width, field.y + (i * 89) % (int)field.height), 2.5f, MgScenery.Hex(0x7A5541));

            for (var i = 0; i < _game.Pots.Count; i++) PaintPot(p, i);
            if (_game.Holding) PaintRing(p, _game.HoldIndex);
            foreach (var d in _drops) Circle(p, d.P, 3.5f, new Color(Water.r, Water.g, Water.b, Mathf.Clamp01(d.Life * 2f)));
            if (_sprinklerRain > 0f)
            {
                for (var i = 0; i < 40; i++)
                {
                    var x = field.x + (i * 53) % (int)field.width;
                    var y = field.y + ((i * 97) + (int)(_show * 900f)) % (int)field.height;
                    p.strokeColor = new Color(Water.r, Water.g, Water.b, 0.7f * Mathf.Clamp01(_sprinklerRain)); p.lineWidth = 2f;
                    Line(p, new Vector2(x, y), new Vector2(x - 3f, y + 14f));
                }
            }
            if (_bloom > 0f) FillRect(p, 0f, 0f, FieldW, FieldH, new Color(1f, 1f, 0.85f, 0.2f * _bloom));
        }

        /// <summary>Over the 3D bed: only the wilt arc of a live sprout or weed, the splash and the wilt flash of pot i.</summary>
        private void PaintPotOverlay(Painter2D p, int i)
        {
            if (i >= _game.ActivePots) return;
            var r = PotRect(i);
            var c = r.center;
            var w = r.width;
            var pot = _game.Pots[i];
            if (pot.State == GardenRushGame.Sprout)
            {
                var frac = (float)pot.Fraction;
                p.lineCap = LineCap.Round;
                p.strokeColor = new Color(1f, 1f, 1f, 0.55f); p.lineWidth = 5f;
                p.BeginPath(); p.Arc(new Vector2(c.x, c.y + w * 0.5f), w * 0.34f, Angle.Degrees(180f), Angle.Degrees(360f)); p.Stroke();
                p.strokeColor = frac > 0.4f ? Leaf : frac > 0.2f ? new Color(1f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.35f);
                p.BeginPath(); p.Arc(new Vector2(c.x, c.y + w * 0.5f), w * 0.34f, Angle.Degrees(180f), Angle.Degrees(180f + 180f * frac)); p.Stroke();
            }
            if (_wiltFlash[i] > 0f) Ellipse(p, c, w * 0.5f, w * 0.5f, new Color(0.9f, 0.4f, 0.3f, 0.2f * _wiltFlash[i]));
        }

        private void PaintPot(Painter2D p, int i)
        {
            var r = PotRect(i);
            var c = r.center;
            var active = i < _game.ActivePots;
            var pot = _game.Pots[i];
            var w = r.width;
            // the pot: a tapered terracotta body with a rim, greyed out while not yet in play
            var body = active ? Terracotta : Color.Lerp(Terracotta, Soil, 0.6f);
            var rim = active ? TerracottaDark : Color.Lerp(TerracottaDark, Soil, 0.6f);
            Poly(p, new[] { new Vector2(c.x - w * 0.36f, c.y - w * 0.05f), new Vector2(c.x + w * 0.36f, c.y - w * 0.05f), new Vector2(c.x + w * 0.28f, c.y + w * 0.42f), new Vector2(c.x - w * 0.28f, c.y + w * 0.42f) }, body);
            FillRect(p, c.x - w * 0.42f, c.y - w * 0.16f, w * 0.84f, w * 0.13f, rim);
            Ellipse(p, new Vector2(c.x, c.y - w * 0.09f), w * 0.34f, w * 0.06f, new Color(0.3f, 0.2f, 0.14f));
            if (!active) return;
            var state = pot.State;
            if (state == GardenRushGame.Sprout || state == GardenRushGame.Weed)
            {
                var frac = (float)pot.Fraction;                         // 1 fresh → 0 wilted
                var weed = state == GardenRushGame.Weed;
                var droop = weed ? 0f : (1f - frac) * 0.9f;
                var leaf = weed ? WeedColor : Color.Lerp(Wilt, Leaf, Mathf.Clamp01(frac * 1.4f));
                var stemTop = new Vector2(c.x + Mathf.Sin(droop) * w * 0.25f, c.y - w * 0.12f - Mathf.Cos(droop) * w * 0.42f);
                p.strokeColor = Color.Lerp(leaf, LeafDark, 0.4f); p.lineWidth = Mathf.Max(3f, w * 0.06f); p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(new Vector2(c.x, c.y - w * 0.1f)); p.LineTo(stemTop); p.Stroke();
                if (weed)
                {
                    for (var k = 0; k < 5; k++)
                    {
                        var a = -Mathf.PI * 0.5f + (k - 2) * 0.55f;
                        var tip = stemTop + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w * 0.3f * (pot.Grown ? 1.3f : 1f);
                        Poly(p, new[] { stemTop + new Vector2(-4f, 0f), tip, stemTop + new Vector2(4f, 0f) }, leaf);
                    }
                }
                else
                {
                    var sway = Mathf.Sin(_show * 3f + i) * 0.1f;
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var dir = new Vector2(side, -0.35f + droop * 0.8f).normalized;
                        var tip = stemTop + dir * w * 0.3f + new Vector2(sway * w * 0.1f, 0f);
                        var mid = (stemTop + tip) * 0.5f + new Vector2(0f, -w * 0.08f);
                        Poly(p, new[] { stemTop, mid + new Vector2(-side * w * 0.06f, -w * 0.06f), tip, mid + new Vector2(side * w * 0.04f, w * 0.06f) }, leaf);
                    }
                    Circle(p, stemTop, w * 0.06f, leaf);
                    if (frac > 0.85f) Circle(p, stemTop + new Vector2(0f, -w * 0.08f), w * 0.05f, new Color(1f, 0.8f, 0.86f));
                }
                // the wilt ring under the pot: what is left of the window
                p.strokeColor = new Color(1f, 1f, 1f, 0.4f); p.lineWidth = 4f;
                p.BeginPath(); p.Arc(new Vector2(c.x, c.y + w * 0.5f), w * 0.34f, Angle.Degrees(180f), Angle.Degrees(360f)); p.Stroke();
                p.strokeColor = frac > 0.4f ? Leaf : frac > 0.2f ? new Color(1f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.35f);
                p.BeginPath(); p.Arc(new Vector2(c.x, c.y + w * 0.5f), w * 0.34f, Angle.Degrees(180f), Angle.Degrees(180f + 180f * frac)); p.Stroke();
            }
            if (_splash[i] > 0f)
            {
                var k = 1f - _splash[i];
                for (var d = 0; d < 6; d++)
                {
                    var a = d * Mathf.PI * 2f / 6f;
                    Circle(p, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f) * (w * 0.2f + k * w * 0.4f) - new Vector2(0f, w * 0.1f), 4f * (1f - k), Water);
                }
            }
            if (_wiltFlash[i] > 0f) Ellipse(p, c, w * 0.5f, w * 0.5f, new Color(0.9f, 0.4f, 0.3f, 0.25f * _wiltFlash[i]));
        }

        /// <summary>The fill ring around the held pot: white track, green perfect zone, the fill in teal turning green in the zone; the can pours from the top left.</summary>
        private void PaintRing(Painter2D p, int index)
        {
            var r = PotRect(index);
            var c = r.center; var half = r.width * 0.5f;
            var frac = (float)_game.HoldFraction;
            var perfect = _game.HoldInPerfectZone;
            var zone = (float)_game.Tune.PerfectZone;
            var radius = half * 1.42f;
            p.lineCap = LineCap.Butt;
            p.strokeColor = new Color(1f, 1f, 1f, 0.65f); p.lineWidth = 9f;
            p.BeginPath(); p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
            p.strokeColor = new Color(0.45f, 0.78f, 0.42f, 0.55f);
            p.BeginPath(); p.Arc(c, radius, Angle.Degrees(-90f + 360f * (1f - zone)), Angle.Degrees(270f)); p.Stroke();
            p.strokeColor = perfect ? LeafDark : new Color(0.3f, 0.72f, 0.7f); p.lineWidth = 7f;
            if (frac > 0.002f) { p.BeginPath(); p.Arc(c, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * frac)); p.Stroke(); }
            if (_stage != null) return;
            // the can
            var at = c - new Vector2(half * 1.5f, half * 1.1f);
            p.fillColor = Can;
            Poly(p, new[] { at + new Vector2(-16f, -14f), at + new Vector2(16f, -14f), at + new Vector2(16f, 12f), at + new Vector2(-16f, 12f) }, Can);
            p.strokeColor = Ink; p.lineWidth = 3f; p.lineJoin = LineJoin.Round;
            StrokePoly(p, new[] { at + new Vector2(-16f, -14f), at + new Vector2(16f, -14f), at + new Vector2(16f, 12f), at + new Vector2(-16f, 12f) });
            p.strokeColor = Can; p.lineWidth = 6f; p.lineCap = LineCap.Round;
            Line(p, at + new Vector2(16f, -8f), at + new Vector2(34f, -18f));
            p.strokeColor = Ink; p.lineWidth = 3f;
            p.BeginPath(); p.Arc(at + new Vector2(-6f, -14f), 10f, Angle.Degrees(180f), Angle.Degrees(360f)); p.Stroke();
        }

        private static void FillRect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }

        private static void Poly(Painter2D p, Vector2[] pts, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            for (var i = 0; i < pts.Length; i++) { if (i == 0) p.MoveTo(pts[i]); else p.LineTo(pts[i]); }
            p.ClosePath(); p.Fill();
        }

        private static void StrokePoly(Painter2D p, Vector2[] pts)
        {
            p.BeginPath();
            for (var i = 0; i < pts.Length; i++) { if (i == 0) p.MoveTo(pts[i]); else p.LineTo(pts[i]); }
            p.ClosePath(); p.Stroke();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        private static void Circle(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath(); p.Arc(c, Mathf.Max(0.5f, r), Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
        }

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i <= 24; i++)
            {
                var a = Mathf.PI * 2f * i / 24f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Fill();
        }

        // ── juice ──────────────────────────────────────────────────────────

        private void OnWatered(int index, int points, bool perfect)
        {
            if (FieldW <= 0f) return;
            _splash[index] = 1f;
            if (perfect) _bloom = Mathf.Max(_bloom, 0.4f);
            StageWatered(index, perfect);
            FloatText(PotCentre(index) - new Vector2(0f, 40f), Host.Strings.T(perfect ? "mg.gardenRush.perfect" : "mg.gardenRush.early"), perfect ? LeafDark : new Color(0.45f, 0.4f, 0.5f));
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _drops.Clear();
            _witheredPill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class BedCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public BedCanvas()
            {
                pickingMode = PickingMode.Ignore;
                name = "bed";
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
