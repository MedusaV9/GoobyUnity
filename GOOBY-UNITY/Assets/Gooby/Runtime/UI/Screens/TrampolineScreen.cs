using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Trampolin-Tricks host on the shared minigame frame (port of trampoline.gd + its hall stage): a bright gym hall
    /// painted with the Painter2D — bleachers with a pastel crowd, banners, tall windows, the sprung floor and the
    /// trampoline at the bottom whose mat dips on every landing. Gooby bounces above it; a gauge beside the mat shows
    /// the judge zone with the green landing window at its foot — a tap inside boosts the next bounce, a tap too
    /// early is a butt landing. Swipes in the air are tricks (flip left, spin right, twist up/down) and the three
    /// chips at the top light up as the kinds are collected in one flight; all three pay the Dreierpack. With a GPU the
    /// hall is the 3D gym of <c>TrampolineScreen.Stage3D.cs</c> and the canvas keeps only the gauge, chips and sparks.
    /// </summary>
    public sealed partial class TrampolineScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;
        private const float SwipeMinPx = 44f;
        private const double ViewHeightM = 4.8;

        private TrampolineGame _game;
        private HallCanvas _canvas;
        private VisualElement _gooby, _multPill;
        private Label _multLabel;
        private float _show, _shock, _chipFlash, _cheer, _tierFlash, _boostFlash;
        private readonly Dictionary<string, float> _chipPop = new Dictionary<string, float> { ["flip"] = 0f, ["spin"] = 0f, ["twist"] = 0f };
        private readonly List<Spark> _sparks = new List<Spark>();
        private bool _swiping, _swipeDone;
        private Vector2 _swipeFrom;

        private struct Spark { public Vector2 P, V; public float Life, Max, Size; public Color C; }

        private static readonly Color Wall = new Color(0.95f, 0.88f, 0.78f);
        private static readonly Color Floor = MgScenery.Hex(0xE8C49A);
        private static readonly Color FloorLine = MgScenery.Hex(0xCFA476);
        private static readonly Color Mat = MgScenery.Hex(0x6FA8F0);
        private static readonly Color MatEdge = MgScenery.Hex(0x4A7FC4);
        private static readonly Color Frame = MgScenery.Hex(0x9AA6BC);
        private static readonly Color Bleacher = MgScenery.Hex(0xD9A77A);
        private static readonly Color Green = new Color(0.45f, 0.82f, 0.5f);
        private static readonly Color Amber = new Color(1f, 0.8f, 0.4f);

        public TrampolineScreen(GameHost host) : base(host, "trampoline", "trampoline") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xF3CDA0);
        protected override Color FieldColor => Wall;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public TrampolineGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new TrampolineGame(Difficulty, seed);
            _game.Landed += v => { _shock = Mathf.Max(0.3f, (float)v); StageLanded((float)v); };
            _game.Boosted += () => { _boostFlash = 1f; Burst(MatCentre() + new Vector2(0f, -30f), new Color(0.4f, 0.9f, 0.7f), 14, 160f); StageBoost(); };
            _game.ButtLanded += () => { _shock = 1f; Burst(MatCentre(), new Color(0.75f, 0.6f, 0.45f), 10, 90f); FloatText(GoobyAt(), Host.Strings.T("mg.trampoline.butt"), new Color(0.85f, 0.35f, 0.35f)); StageButt(); };
            _game.TierUp += m => { _tierFlash = 1f; if (m >= 3) { _cheer = 1f; StageCheer(true); } FloatText(GoobyAt() - new Vector2(0f, 64f), Host.Strings.T("mg.trampoline.tier", ("n", m)), m >= 3 ? Amber : new Color(0.2f, 0.6f, 0.55f)); };
            _game.Tricked += (kind, pts) =>
            {
                _chipPop[kind] = 0.3f;
                FloatText(GoobyAt() - new Vector2(0f, 46f), Host.Strings.T("mg.trampoline." + kind) + " +" + pts, _game.Multiplier < 3 ? new Color(0.95f, 0.45f, 0.7f) : Amber);
            };
            _game.Combo += b => { _chipFlash = 1f; _cheer = 1f; Banner(Host.Strings.T("mg.trampoline.combo"), 1.4); Burst(GoobyAt(), Amber, 24, 220f); StageCheer(true); };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(OnDown);
            Field.RegisterCallback<PointerMoveEvent>(OnMove);
            Field.RegisterCallback<PointerUpEvent>(OnUp);
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _swiping = false);

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new HallCanvas { Paint = Paint };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(110);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            _multPill = Ux.El("gb-card", "gb-row");
            _multPill.name = "mult-pill";
            Ux.Abs(_multPill, null, 62);
            _multPill.style.left = Length.Percent(50);
            _multPill.style.translate = new Translate(Length.Percent(-50), 0);
            _multPill.style.alignItems = Align.Center;
            _multPill.style.paddingTop = 6; _multPill.style.paddingBottom = 6; _multPill.style.paddingLeft = 12; _multPill.style.paddingRight = 16;
            _multPill.pickingMode = PickingMode.Ignore;
            var icon = Ux.Icon("arrow_up");
            icon.style.width = 30; icon.style.height = 30; icon.style.marginRight = 8;
            _multPill.Add(icon);
            _multLabel = Ux.Text("", "gb-md", "gb-bold");
            _multPill.Add(_multLabel);
            Root.Add(_multPill);
            ComboLabel.style.top = 128;
            Banner(t.T("mg.trampoline.intro"), TrampolineGame.IntroSec + 0.8);
        }

        // ── input ──────────────────────────────────────────────────────────

        private void OnDown(PointerDownEvent e)
        {
            if (_game == null || !_game.Running || _game.Staggering) return;
            _swiping = true; _swipeDone = false;
            _swipeFrom = e.localPosition;
            Field.CapturePointer(e.pointerId);
            _game.Tap();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_game == null || !_swiping || _swipeDone) return;
            var d = (Vector2)e.localPosition - _swipeFrom;
            if (d.magnitude * (float)(ViewH / FieldH) >= SwipeMinPx) { _swipeDone = true; _game.Swipe(d.x, d.y); }
        }

        private void OnUp(PointerUpEvent e)
        {
            if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
            if (_game == null || !_swiping) return;
            _swiping = false;
            if (_swipeDone) return;
            var d = (Vector2)e.localPosition - _swipeFrom;
            if (d.magnitude * (float)(ViewH / FieldH) >= SwipeMinPx) _game.Swipe(d.x, d.y);
        }

        // ── the round ──────────────────────────────────────────────────────

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            var f = (float)dt;
            _show += f;
            _shock = Mathf.Max(0f, _shock - f * 2.2f);
            _chipFlash = Mathf.Max(0f, _chipFlash - f * 1.4f);
            _cheer = Mathf.Max(0f, _cheer - f * 0.8f);
            _tierFlash = Mathf.Max(0f, _tierFlash - f * 1.5f);
            _boostFlash = Mathf.Max(0f, _boostFlash - f * 2f);
            foreach (var k in new[] { "flip", "spin", "twist" }) _chipPop[k] = Mathf.Max(0f, _chipPop[k] - f);
            for (var i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i]; s.Life -= f;
                if (s.Life <= 0f) { _sparks.RemoveAt(i); continue; }
                s.V.y += 380f * f; s.P += s.V * f; _sparks[i] = s;
            }
        }

        private float Ppu => FieldH * 0.72f / (float)ViewHeightM;
        private float MatY => FieldH * 0.8f;
        private Vector2 MatCentre() => new Vector2(FieldW * 0.5f, MatY);

        // ── scenery (board 06): the bright hall — a cream wall with soft stripes, two tall windows (at the old painted
        //    spots, 7 % / 73 %, 5 % down, 20 % × 17 %) looking out on a garden hedge under a sky with a cloud, pink
        //    curtains and sills, a mint beadboard wainscot under the bleachers and two potted bushes at the floor line.
        //    A layer behind the hall canvas (banners, bleachers, crowd, floor and trampoline stay painted); built once,
        //    pointer-transparent, nothing moves. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-trampoline");
            MgScenery.Gradient(scene, MgScenery.Hex(0xFFF6EA), MgScenery.Hex(0xF7E2CC), 0f, 86f);
            var stripes = MgScenery.Painted(scene, (p, r) =>
            {
                for (var x = 0f; x < r.width; x += 36f) MgScenery.FillRect(p, x, 0f, 16f, r.height, new Color(1f, 0.85f, 0.78f, 0.28f));
            }, "gb-mgf-stripes");
            MgScenery.Pct(stripes, 0, 0, 0);
            stripes.style.height = Length.Percent(50f);
            for (var i = 0; i < 2; i++) HallWindow(scene, 7f + i * 66f);
            var wains = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, MgScenery.Hex(0xCDEFE0));
                p.strokeColor = MgScenery.Hex(0xAEDFCB); p.lineWidth = 2f;
                for (var x = 12f; x < r.width; x += 20f) { p.BeginPath(); p.MoveTo(new Vector2(x, 10f)); p.LineTo(new Vector2(x, r.height)); p.Stroke(); }
                MgScenery.FillRect(p, 0f, 0f, r.width, 9f, MgScenery.Hex(0xF6E4C8));
                MgScenery.FillRect(p, 0f, 9f, r.width, 3f, MgScenery.Hex(0xE3CBA8));
            }, "gb-mgf-wainscot");
            MgScenery.Pct(wains, 0, 64f, 0, 14f);
            for (var s = 0; s < 2; s++)
            {
                var pot = MgScenery.Shape(scene, MgScenery.Hex(0xF4A38C), 0f, "gb-mgf-pot");
                MgScenery.Pct(pot, s == 0 ? 1.5f : null, null, s == 0 ? (float?)null : 1.5f, 14f);
                MgScenery.Size(pot, 34f, 30f);
                MgScenery.RoundBottom(pot, 10f); MgScenery.RoundTop(pot, 4f);
                MgScenery.Outline(pot, 3f, MgScenery.Hex(0xD9826C));
                var bush = MgScenery.Sprite(pot, "plant_bush", 52f, 48f);
                bush.style.left = -9f; bush.style.top = -42f;
            }
        }

        private static void HallWindow(VisualElement scene, float leftPct)
        {
            var win = MgScenery.Group(scene, "gb-mgf-window");
            MgScenery.Pct(win, leftPct, 5f);
            win.style.width = Length.Percent(20f); win.style.height = Length.Percent(17f);
            var pane = MgScenery.Shape(win, MgScenery.Hex(0xCFE6FF), 10f, "gb-mgf-pane");
            Ux.Abs(pane, 0, 0, 0, 0);
            MgScenery.Gradient(pane, MgScenery.Hex(0xB7D8FF), MgScenery.Hex(0xEAF4FF));
            var cloud = MgScenery.Cloud(pane, 40f);
            MgScenery.Pct(cloud, 38f, 12f);
            var hedge = MgScenery.Puffs(pane, 3, MgScenery.Hex(0x92CF8F), MgScenery.Hex(0xB8E6A8), 2);
            MgScenery.Pct(hedge, -10f, null, -10f, 0);
            hedge.style.height = Length.Percent(38f);
            var dots = MgScenery.Dots(hedge, 5, MgScenery.Hex(0xFF9BC1), 2f, 3f, 9);
            MgScenery.Pct(dots, 6f, 40f, 6f, 10f);
            var wood = Color.white; var rim = MgScenery.Hex(0xE3CBA8);
            var barV = MgScenery.Shape(win, wood, 2f); MgScenery.Pct(barV, 48f, 0, null, 0); barV.style.width = 5f;
            var barH = MgScenery.Shape(win, wood, 2f); MgScenery.Pct(barH, 0, 48f, 0); barH.style.height = 5f;
            var frame = MgScenery.Shape(win, Color.clear, 10f, "gb-mgf-frame");
            Ux.Abs(frame, 0, 0, 0, 0);
            MgScenery.Outline(frame, 5f, wood);
            var inner = MgScenery.Shape(win, Color.clear, 7f);
            Ux.Abs(inner, 4, 4, 4, 4);
            MgScenery.Outline(inner, 1.5f, rim);
            for (var s = 0; s < 2; s++)
            {
                var curtain = MgScenery.Shape(win, MgScenery.Hex(0xFF9BC1), 0f, "gb-mgf-curtain");
                MgScenery.Pct(curtain, s == 0 ? -12f : null, -3f, s == 0 ? (float?)null : -12f);
                curtain.style.width = Length.Percent(24f); curtain.style.height = Length.Percent(86f);
                MgScenery.RoundBottom(curtain, 12f); MgScenery.RoundTop(curtain, 4f);
                MgScenery.Outline(curtain, 2f, MgScenery.Hex(0xE97FA6));
            }
            var rod = MgScenery.Shape(win, MgScenery.Hex(0xC8A27A), 3f); MgScenery.Pct(rod, -16f, null, -16f); rod.style.top = -8f; rod.style.height = 6f;
            var sill = MgScenery.Shape(win, MgScenery.Hex(0xF6E4C8), 4f, "gb-mgf-sill");
            MgScenery.Pct(sill, -10f, null, -10f); sill.style.bottom = -9f; sill.style.height = 11f;
            MgScenery.Outline(sill, 2f, rim);
        }
        private Vector2 GoobyAt() => new Vector2(FieldW * 0.5f, MatY - (float)_game.Height * Ppu - 50f);

        protected override void RenderGame()
        {
            var t = Host.Strings;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.trampoline.fails", ("n", _game.Failures), ("max", _game.Tune.EndlessFailureLimit))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            _multLabel.text = t.T("mg.trampoline.mult", ("n", _game.Multiplier));
            ComboLabel.text = _game.InWindow ? t.T("mg.trampoline.boost") : "";
            if (_stage != null)
            {
                RenderStage();
                _canvas.MarkDirtyRepaint();
                return;
            }

            // Gooby above the mat: squashed on impact, spinning through a trick, slumped on a butt landing
            var size = FieldW * 0.28f;
            var at = GoobyAt();
            var squash = _game.Staggering ? 0.8f : 1f - _shock * 0.18f;
            _gooby.style.width = size; _gooby.style.height = size;
            _gooby.style.left = at.x - size * 0.5f; _gooby.style.top = at.y - size * 0.5f + (1f - squash) * size * 0.5f;
            var spin = (float)_game.TrickSpin;
            var trick = _game.Tricking > 0.0 ? _game.LastTrick : "";
            var rot = trick == "flip" ? -spin * Mathf.Rad2Deg * 0.4f : trick == "twist" ? Mathf.Sin(spin) * 30f : 0f;
            var sx = trick == "spin" ? Mathf.Abs(Mathf.Cos(spin * 0.6f)) * 0.7f + 0.3f : 1f;
            if (_game.Staggering) rot = 14f;
            _gooby.style.rotate = new Rotate(rot);
            _gooby.style.scale = new Scale(new Vector3(sx, squash, 1f));
            _canvas.MarkDirtyRepaint();
        }

        // ── painting ───────────────────────────────────────────────────────

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            if (_stage == null) PaintHall(p);
            PaintOverlay(p);
        }

        /// <summary>The painted hall of the 2D field (without a GPU): pennants, bleachers with the crowd, floor and trampoline.</summary>
        private void PaintHall(Painter2D p)
        {
            var floorY = FieldH * 0.86f;
            // the wall, its windows onto the garden and the wainscot are the scenery layer behind the canvas; banners
            // a string of pennants across the hall
            for (var i = 0; i < 9; i++)
            {
                var x0 = FieldW * i / 9f; var x1 = FieldW * (i + 1) / 9f;
                var y = FieldH * 0.245f + Mathf.Sin(i * 0.7f) * 6f;
                Poly(p, new[] { new Vector2(x0, y), new Vector2(x1, y), new Vector2((x0 + x1) * 0.5f, y + 26f) }, i % 3 == 0 ? new Color(1f, 0.6f, 0.62f) : i % 3 == 1 ? new Color(0.98f, 0.85f, 0.45f) : new Color(0.6f, 0.78f, 1f));
            }
            // bleachers with the crowd
            var bleachY = FieldH * 0.5f;
            for (var row = 0; row < 3; row++)
            {
                var y = bleachY + row * 30f;
                FillRect(p, 0f, y, FieldW, 14f, Color.Lerp(Bleacher, Color.black, row * 0.08f));
                for (var i = 0; i < 7; i++)
                {
                    var x = FieldW * (0.08f + i * 0.14f) + (row % 2) * 20f;
                    var bob = Mathf.Sin(_show * 7f + i + row) * _cheer * 8f;
                    var c = (i + row) % 3 == 0 ? new Color(1f, 0.62f, 0.64f) : (i + row) % 3 == 1 ? new Color(0.98f, 0.86f, 0.5f) : new Color(0.62f, 0.78f, 1f);
                    Circle(p, new Vector2(x, y - 12f - bob), 11f, c);
                    if (_cheer > 0.2f) { Circle(p, new Vector2(x - 12f, y - 20f - bob), 4f, c); Circle(p, new Vector2(x + 12f, y - 20f - bob), 4f, c); }
                }
            }
            // the sprung floor with lane lines
            FillRect(p, 0f, floorY, FieldW, FieldH - floorY, Floor);
            p.strokeColor = FloorLine; p.lineWidth = 3f;
            for (var i = 1; i < 6; i++) Line(p, new Vector2(FieldW * i / 6f, floorY), new Vector2(FieldW * i / 6f, FieldH));
            // the trampoline: frame legs, springs, the mat dipping with the shock
            var mat = MatCentre();
            var hw = FieldW * 0.36f;
            var dip = _shock * 22f;
            p.strokeColor = Frame; p.lineWidth = 8f;
            Line(p, new Vector2(mat.x - hw + 10f, mat.y), new Vector2(mat.x - hw - 10f, floorY + 30f));
            Line(p, new Vector2(mat.x + hw - 10f, mat.y), new Vector2(mat.x + hw + 10f, floorY + 30f));
            Line(p, new Vector2(mat.x - hw - 12f, floorY + 30f), new Vector2(mat.x + hw + 12f, floorY + 30f));
            FillRect(p, mat.x - hw - 6f, mat.y - 8f, hw * 2f + 12f, 16f, MatEdge);
            p.fillColor = Mat;
            p.BeginPath();
            p.MoveTo(new Vector2(mat.x - hw, mat.y - 4f));
            p.LineTo(new Vector2(mat.x + hw, mat.y - 4f));
            p.LineTo(new Vector2(mat.x + hw, mat.y + 4f));
            p.LineTo(new Vector2(mat.x, mat.y + 4f + dip));
            p.LineTo(new Vector2(mat.x - hw, mat.y + 4f));
            p.ClosePath(); p.Fill();
            p.strokeColor = new Color(1f, 1f, 1f, 0.35f); p.lineWidth = 2f;
            for (var i = 1; i < 6; i++)
            {
                var x = mat.x - hw + hw * 2f * i / 6f;
                var k = 1f - Mathf.Abs(i - 3f) / 3f;
                Line(p, new Vector2(x, mat.y - 4f), new Vector2(x, mat.y + 4f + dip * k));
            }
            // Gooby's shadow on the mat
            var gh = (float)_game.Height;
            Ellipse(p, new Vector2(mat.x, mat.y + dip * 0.5f), Mathf.Max(14f, 40f - gh * 5f), 8f, new Color(0f, 0f, 0f, Mathf.Clamp(0.3f - gh * 0.05f, 0.08f, 0.3f)));
            if (_boostFlash > 0f) Ellipse(p, new Vector2(mat.x, mat.y), hw * (1.2f - _boostFlash * 0.4f), 18f, new Color(0.4f, 0.9f, 0.7f, 0.5f * _boostFlash));
        }

        /// <summary>What stays over either field: the landing-window gauge, the trick chips, the tier flash and the sparks.</summary>
        private void PaintOverlay(Painter2D p)
        {
            // the window gauge beside the mat: judge zone in amber, the landing window in green at its foot, the marker falling into it
            var gx = FieldW - 30f; var gTop = MatY - FieldH * 0.36f; var gBot = MatY - 6f; var gH = gBot - gTop;
            FillRect(p, gx - 9f, gTop, 18f, gH, new Color(0f, 0f, 0f, 0.12f));
            var windowFrac = (float)(_game.WindowSec / _game.Tune.JudgeZoneSec);
            FillRect(p, gx - 9f, gTop, 18f, gH * (1f - windowFrac), new Color(Amber.r, Amber.g, Amber.b, 0.55f));
            FillRect(p, gx - 9f, gBot - gH * windowFrac, 18f, gH * windowFrac, Green);
            var zone = (float)_game.ZoneProgress;
            if (zone > 0f)
            {
                var my = gTop + gH * zone;
                Poly(p, new[] { new Vector2(gx - 22f, my - 8f), new Vector2(gx - 22f, my + 8f), new Vector2(gx - 10f, my) }, Color.white);
                Poly(p, new[] { new Vector2(gx + 22f, my - 8f), new Vector2(gx + 22f, my + 8f), new Vector2(gx + 10f, my) }, Color.white);
            }
            if (_game.Armed == "boost") FillRect(p, gx - 12f, gBot - gH * windowFrac - 3f, 24f, gH * windowFrac + 6f, new Color(1f, 1f, 1f, 0.5f + 0.3f * Mathf.Sin(_show * 30f)));

            // the trick chips: flip / spin / twist, lit once seen in this flight, flashing gold on the Dreierpack
            var kinds = new[] { "flip", "spin", "twist" };
            for (var i = 0; i < 3; i++)
            {
                var c = new Vector2(FieldW * 0.5f + (i - 1) * 62f, FieldH * 0.3f);
                var seen = _game.Chain.Seen.Contains(kinds[i]);
                var r = 20f + _chipPop[kinds[i]] * 20f;
                var col = _chipFlash > 0f ? Color.Lerp(seen ? Green : Color.white, Amber, _chipFlash) : seen ? Green : new Color(1f, 1f, 1f, 0.75f);
                Circle(p, c, r, col);
                p.strokeColor = new Color(0.24f, 0.19f, 0.17f); p.lineWidth = 3f; p.lineCap = LineCap.Round;
                if (kinds[i] == "flip") { p.BeginPath(); p.Arc(c, r * 0.5f, Angle.Degrees(200f), Angle.Degrees(500f)); p.Stroke(); Poly(p, new[] { c + new Vector2(-r * 0.5f, -r * 0.1f), c + new Vector2(-r * 0.15f, -r * 0.25f), c + new Vector2(-r * 0.55f, -r * 0.55f) }, new Color(0.24f, 0.19f, 0.17f)); }
                else if (kinds[i] == "spin") { p.BeginPath(); for (var k = 0; k <= 16; k++) { var a = Mathf.PI * 2f * k / 16f; var pt = c + new Vector2(Mathf.Cos(a) * r * 0.55f, Mathf.Sin(a) * r * 0.22f); if (k == 0) p.MoveTo(pt); else p.LineTo(pt); } p.Stroke(); Line(p, c + new Vector2(0f, -r * 0.5f), c + new Vector2(0f, r * 0.5f)); }
                else { Line(p, c + new Vector2(-r * 0.45f, r * 0.4f), c + new Vector2(r * 0.45f, -r * 0.4f)); Line(p, c + new Vector2(-r * 0.45f, -r * 0.4f), c + new Vector2(r * 0.45f, r * 0.4f)); }
            }
            if (_tierFlash > 0f) FillRect(p, 0f, 0f, FieldW, FieldH, new Color(1f, 0.95f, 0.7f, 0.25f * _tierFlash));
            foreach (var s in _sparks)
            {
                var c = s.C; c.a = Mathf.Clamp01(s.Life / s.Max);
                Circle(p, s.P, s.Size, c);
            }
        }

        private void Burst(Vector2 at, Color c, int n, float speed)
        {
            for (var i = 0; i < n; i += ParticleStep)
            {
                var a = i * Mathf.PI * 2f / n + 0.2f;
                var sp = speed * (0.6f + ((i * 37) % 10) / 20f);
                _sparks.Add(new Spark { P = at, V = new Vector2(Mathf.Cos(a) * sp, Mathf.Sin(a) * sp - speed * 0.5f), Life = 0.6f, Max = 0.6f, Size = 3f + (i % 3) * 2f, C = c });
            }
        }

        private static void FillRect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }

        private static void StrokeRect(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Stroke();
        }

        private static void Poly(Painter2D p, Vector2[] pts, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            for (var i = 0; i < pts.Length; i++) { if (i == 0) p.MoveTo(pts[i]); else p.LineTo(pts[i]); }
            p.ClosePath(); p.Fill();
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

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _sparks.Clear();
            _multPill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class HallCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public HallCanvas()
            {
                pickingMode = PickingMode.Ignore;
                name = "hall";
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
