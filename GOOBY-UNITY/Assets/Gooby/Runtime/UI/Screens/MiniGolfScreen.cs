using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Minigolf host on the shared minigame frame (port of mini_golf.gd + its garden course): a top-down felt
    /// fairway on the lawn, rails, cup and flag, a windmill gate, the bump dome. Drag back and release to putt;
    /// Gooby watches from the tee. The physics live in MiniGolfGame over the certified MiniGolf rules. Game feel after
    /// mini_golf.gd: a puff + whoosh on the putt, sparks off rails/windmill/bump, gold sparks and "+N" at the cup, a
    /// perfect tone at or under par climbing with the hole streak, confetti and a gold flash for a hole-in-one.
    /// </summary>
    public sealed partial class MiniGolfScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private MiniGolfGame _game;
        private CourseCanvas _canvas;
        private VisualElement _gooby, _holePill;
        private Label _holeLabel;
        private float _show, _flash;
        private string _flashText = "";
        private bool _pressed;
        private float _scale = 80f;
        private Vector2 _origin;
        private double _midX, _maxZ;
        private string _lastBallTag;
        private float _lastBallFxAt = -10f;

        private static readonly Color Sky = new Color(0.55f, 0.78f, 0.96f);
        private static readonly Color Lawn = MgScenery.Hex(0x8CC985);
        private static readonly Color LawnStripe = MgScenery.Hex(0x98D290);
        private static readonly Color Felt = new Color(0.62f, 0.92f, 0.6f);
        private static readonly Color FeltDark = new Color(0.54f, 0.85f, 0.54f);
        private static readonly Color Rail = MgScenery.Hex(0xF0CD9A);
        private static readonly Color BallC = new Color(1f, 0.99f, 0.96f);
        private static readonly Color Cup = new Color(0.12f, 0.16f, 0.14f);
        private static readonly Color Flag = new Color(0.92f, 0.28f, 0.32f);

        public MiniGolfScreen(GameHost host) : base(host, "miniGolf", "miniGolf") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xB2DE9E);
        protected override Color FieldColor => Sky;

        public MiniGolfGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new MiniGolfGame(Difficulty, seed);
            _game.Holed += (pts, ace) =>
            {
                _flash = 1.2f;
                _flashText = ace ? Host.Strings.T("mg.miniGolf.ace") : "+" + pts;
                Banner(_flashText, ace ? 1.8 : 1.2);
                OnHoled(pts, ace);
            };
            _game.GaveUp += () =>
            {
                _flash = 1.0f;
                _flashText = Host.Strings.T("mg.miniGolf.gave_up");
                Banner(_flashText, 1.2);
                OnGaveUp();
            };
            _game.BallEvent += OnBallEvent;
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_perfect", "game_whoosh");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _pressed = true;
                Field.CapturePointer(e.pointerId);
                _game.ViewW = FieldW; _game.ViewH = FieldH;
                _game.BeginAim(e.localPosition.x, e.localPosition.y);
            });
            Field.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_pressed) _game?.AimTo(e.localPosition.x, e.localPosition.y);
            });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                _pressed = false;
                if (_game == null) return;
                var power = _game.Dragging ? _game.AimPower : 0.0;   // read before the release clears the drag
                if (_game.Release()) OnPutt(power);
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => { _pressed = false; _game?.CancelAim(); });

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new CourseCanvas { Paint = Paint, name = "course" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);
            if (_stage != null) _canvas.style.display = DisplayStyle.None;

            _gooby = Ux.Portrait(72);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            _holePill = Ux.El("gb-card", "gb-row");
            _holePill.name = "hole-pill";
            Ux.Abs(_holePill, null, 62);
            _holePill.style.left = Length.Percent(50);
            _holePill.style.translate = new Translate(Length.Percent(-50), 0);
            _holePill.style.alignItems = Align.Center;
            _holePill.style.paddingTop = 6; _holePill.style.paddingBottom = 6; _holePill.style.paddingLeft = 14; _holePill.style.paddingRight = 16;
            _holePill.pickingMode = PickingMode.Ignore;
            _holeLabel = Ux.Text("", "gb-md", "gb-bold");
            _holePill.Add(_holeLabel);
            Root.Add(_holePill);

            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.miniGolf.intro"), MiniGolfGame.IntroSec + 0.7);
        }

        protected override void TickGame(double dt)
        {
            _game.ViewW = FieldW > 1f ? FieldW : ViewW;
            _game.ViewH = FieldH > 1f ? FieldH : ViewH;
            _game.Tick(dt);
            _show += (float)dt;
            _flash = Mathf.Max(0f, _flash - (float)dt);
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            FitCourse(g.Current);
            var tee = WorldToView(g.Current.Start.x + 0.55, g.Current.Start.z - 0.15);
            _gooby.style.left = tee.x - 36f;
            _gooby.style.top = tee.y - 24f;
            TimeLabel.text = t.T("mg.miniGolf.strokes", ("n", g.Strokes), ("par", g.CurrentPar));
            ScoreLabel.text = g.Score.ToString();
            _holeLabel.text = g.Tune.Endless
                ? t.T("mg.miniGolf.over_par", ("n", g.Endless.OverPar), ("max", g.Endless.Limit))
                : t.T("mg.miniGolf.hole", ("n", g.DisplayHole), ("max", g.Course.Count));
            if (_stage != null) RenderStage();
            else _canvas.MarkDirtyRepaint();
        }

        private void FitCourse(MiniGolf.Hole hole)
        {
            double minX = 1e9, maxX = -1e9, minZ = 1e9, maxZ = -1e9;
            foreach (var c in hole.Cells)
            {
                minX = Math.Min(minX, c.x); maxX = Math.Max(maxX, c.x);
                minZ = Math.Min(minZ, c.z); maxZ = Math.Max(maxZ, c.z);
            }
            _midX = (minX + maxX) * 0.5;
            _maxZ = maxZ;
            var spanX = maxX - minX + 1.8;
            var spanZ = maxZ - minZ + 2.4;
            _scale = Mathf.Min(FieldW * 0.8f / (float)spanX, FieldH * 0.58f / (float)spanZ);
            _origin = new Vector2(FieldW * 0.5f, FieldH * 0.14f);
        }

        // ── scenery (board 06, "Minigolf im Park"): a sky strip with a sun and a cloud, round trees over the hedge, then a
        //    mown lawn in soft stripes under the whole course (it starts 14 % down), a sand bunker and a little pond
        //    with a lily pad in the free bottom corners, pennant flags on white poles at the edges, bushes, tufts and
        //    flowers. A layer behind the course canvas; built once, pointer-transparent, nothing moves. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-minigolf");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 14f);
            var sun = MgScenery.Sun(scene, 34f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, 5f, 1.5f);
            var cloud = MgScenery.Cloud(scene, 84f);
            MgScenery.Pct(cloud, 60f, 1.5f);
            float[] treeAt = { 14f, 46f, 80f }; float[] treeH = { 84f, 96f, 76f };
            for (var i = 0; i < treeAt.Length; i++)
            {
                var tree = MgScenery.Sprite(scene, "plant_tree_round", treeH[i] * 0.74f, treeH[i]);
                MgScenery.Pct(tree, treeAt[i] - 8f, null, null, 88f);
            }
            var hedge = MgScenery.Puffs(scene, 7, MgScenery.Hex(0x92CF8F), MgScenery.Hex(0xB8E6A8), 44);
            MgScenery.Pct(hedge, -3f, 9f, -3f);
            hedge.style.height = Length.Percent(6.5f);
            var lawn = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, Lawn);
                var band = r.height / 12f;
                for (var i = 1; i < 12; i += 2) MgScenery.FillRect(p, 0f, i * band, r.width, band, LawnStripe);
                MgScenery.FillRect(p, 0f, 0f, r.width, 5f, MgScenery.Hex(0xA6DB98));
            }, "gb-mgf-lawn");
            lawn.name = "mgf-lawn";
            MgScenery.Pct(lawn, 0, 14.5f, 0, 0);
            // the bunker (bottom left) and the pond (bottom right)
            var bunker = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.Oval(p, new Vector2(r.width * 0.5f, r.height * 0.5f), r.width * 0.5f - 2f, r.height * 0.5f - 2f, MgScenery.Hex(0xF6E3BE), MgScenery.Hex(0xE3C99A), 3f);
                MgScenery.Oval(p, new Vector2(r.width * 0.42f, r.height * 0.42f), r.width * 0.22f, r.height * 0.14f, MgScenery.Hex(0xFFF1D6));
            }, "gb-mgf-bunker");
            MgScenery.Pct(bunker, 4f, null, null, 5f);
            MgScenery.Size(bunker, 110f, 54f);
            var pond = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.Oval(p, new Vector2(r.width * 0.5f, r.height * 0.5f), r.width * 0.5f - 2f, r.height * 0.5f - 2f, MgScenery.Hex(0x9ED8E8), MgScenery.Hex(0xE8F7FA), 4f);
                p.strokeColor = new Color(1f, 1f, 1f, 0.6f); p.lineWidth = 2f;
                p.BeginPath(); p.MoveTo(new Vector2(r.width * 0.25f, r.height * 0.4f)); p.LineTo(new Vector2(r.width * 0.45f, r.height * 0.4f)); p.Stroke();
            }, "gb-mgf-pond");
            MgScenery.Pct(pond, null, null, 3f, 4f);
            MgScenery.Size(pond, 120f, 60f);
            var pad = MgScenery.LilyPad(pond, 30f, MgScenery.Hex(0x7CC486), MgScenery.Hex(0x5FA866), MgScenery.Hex(0xFF9BC1));
            pad.style.left = 62f; pad.style.top = 24f;
            // flags on white poles along the edges
            var flagCols = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFD166), MgScenery.Hex(0x9BE7E1), MgScenery.Hex(0xB7D8FF) };
            float[] flagTop = { 24f, 44f, 34f, 56f };
            for (var i = 0; i < 4; i++)
            {
                var left = i % 2 == 0;
                var pole = MgScenery.Group(scene, "gb-mgf-flagpole");
                MgScenery.Pct(pole, left ? 1.5f : null, flagTop[i], left ? (float?)null : 1.5f);
                MgScenery.Size(pole, 30f, 70f);
                var stick = MgScenery.Shape(pole, Color.white, 2f); stick.style.left = left ? 4f : 22f; stick.style.top = 0; MgScenery.Size(stick, 4f, 66f);
                var flag = MgScenery.Roof(pole, flagCols[i], MgScenery.Darker(flagCols[i], 0.18f));
                flag.style.left = left ? 7f : 0f; flag.style.top = 1f; MgScenery.Size(flag, 22f, 22f);
                flag.style.rotate = new Rotate(left ? 90f : -90f);
                var hole = MgScenery.Shape(pole, MgScenery.Hex(0x5E8C5A), 5f); hole.style.left = left ? 0f : 18f; hole.style.bottom = 0; MgScenery.Size(hole, 12f, 6f);
            }
            var tuftC = MgScenery.Hex(0x6DBE78);
            for (var i = 0; i < 8; i++)
            {
                var tuft = MgScenery.Tuft(scene, tuftC, 0.75f);
                MgScenery.Pct(tuft, MgScenery.Hash(i, 81) * 92f, 18f + MgScenery.Hash(i, 82) * 70f);
            }
            var petals = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFFD166) };
            for (var i = 0; i < 6; i++)
            {
                var f = MgScenery.Flower(scene, petals[i % petals.Length], 0.75f);
                MgScenery.Pct(f, 30f + i * 7f, null, null, 1.5f + (i % 2) * 2f);
            }
            var bushL = MgScenery.Sprite(scene, "plant_bush", 56f, 50f);
            MgScenery.Pct(bushL, -4f, null, null, 14f);
            var bushR = MgScenery.Sprite(scene, "plant_bush", 50f, 46f);
            MgScenery.Pct(bushR, null, null, -4f, 16f);
        }

        private Vector2 WorldToView(double x, double z) =>
            new Vector2(_origin.x + (float)(x - _midX) * _scale, _origin.y + (float)(_maxZ + 1.05 - z) * _scale);

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f) return;
            var g = _game;
            var hole = g.Current;
            // the sky, hedge, striped lawn, bunker, pond and flags are the scenery layer behind the course canvas

            var half = 0.5f * _scale;
            foreach (var c in hole.Cells)
            {
                var at = WorldToView(c.x, c.z);
                var tint = ((c.x + c.z) & 1) == 0 ? Felt : FeltDark;
                Poly(p, Rect(at, half * 0.98f, half * 0.98f), tint);
            }
            // rails on missing neighbours
            p.strokeColor = Rail; p.lineWidth = Mathf.Max(5f, _scale * 0.12f);
            foreach (var c in hole.Cells)
            {
                var at = WorldToView(c.x, c.z);
                if (!hole.CellSet.Contains((c.x - 1, c.z))) Line(p, at + new Vector2(-half, -half), at + new Vector2(-half, half));
                if (!hole.CellSet.Contains((c.x + 1, c.z))) Line(p, at + new Vector2(half, -half), at + new Vector2(half, half));
                if (!hole.CellSet.Contains((c.x, c.z - 1))) Line(p, at + new Vector2(-half, half), at + new Vector2(half, half));
                if (!hole.CellSet.Contains((c.x, c.z + 1))) Line(p, at + new Vector2(-half, -half), at + new Vector2(half, -half));
            }
            if (hole.HasRamp)
            {
                var at = WorldToView(hole.RampCell.x, hole.RampCell.z);
                Poly(p, Rect(at, half * 0.7f, half * 0.7f), new Color(0.48f, 0.78f, 0.5f));
            }
            if (hole.HasBump)
                Ellipse(p, WorldToView(hole.BumpX, hole.BumpZ), _scale * 0.19f, _scale * 0.16f, new Color(0.45f, 0.72f, 0.42f));
            if (hole.HasWindmill)
            {
                var mill = WorldToView(hole.MillCellX, hole.MillGateZ);
                var ang = (float)(g.Theta + hole.MillPhase);
                p.strokeColor = new Color(0.85f, 0.86f, 0.88f); p.lineWidth = 7f;
                var r = _scale * 0.42f;
                Line(p, mill + new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r), mill + new Vector2(-Mathf.Cos(ang) * r, -Mathf.Sin(ang) * r));
                Line(p, mill + new Vector2(Mathf.Cos(ang + 1.57f) * r, Mathf.Sin(ang + 1.57f) * r), mill + new Vector2(-Mathf.Cos(ang + 1.57f) * r, -Mathf.Sin(ang + 1.57f) * r));
                Ellipse(p, mill, 8f, 8f, new Color(0.55f, 0.4f, 0.28f));
            }
            if (hole.HasTunnel)
            {
                var at = WorldToView(hole.TunnelCell.x, hole.TunnelCell.z);
                p.strokeColor = new Color(0.55f, 0.48f, 0.4f); p.lineWidth = 10f;
                p.BeginPath(); p.MoveTo(at + new Vector2(-half * 0.7f, 0f));
                p.LineTo(at + new Vector2(-half * 0.35f, -half * 0.7f));
                p.LineTo(at + new Vector2(half * 0.35f, -half * 0.7f));
                p.LineTo(at + new Vector2(half * 0.7f, 0f));
                p.Stroke();
            }

            var cup = WorldToView(hole.HoleAt.x, hole.HoleAt.z);
            Ellipse(p, cup, _scale * 0.13f, _scale * 0.11f, Cup);
            p.strokeColor = Flag; p.lineWidth = 3f;
            Line(p, cup, cup + new Vector2(0f, -_scale * 0.55f));
            Poly(p, new[] { cup + new Vector2(0f, -_scale * 0.55f), cup + new Vector2(_scale * 0.28f, -_scale * 0.42f), cup + new Vector2(0f, -_scale * 0.32f) }, Flag);

            if (g.Dragging && g.Phase == "aim")
            {
                var ball = WorldToView(g.Ball.X, g.Ball.Z);
                var pullX = g.DragFromX - g.DragToX;
                var pullY = g.DragFromY - g.DragToY;
                var dx = pullX; var dz = -pullY;
                var d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 4)
                {
                    p.strokeColor = new Color(1f, 1f, 1f, 0.7f); p.lineWidth = 3f;
                    var power = (float)g.AimPower;
                    var reach = power * 0.22f * _scale;
                    var aim = ball + new Vector2((float)(dx / d) * reach, -(float)(dz / d) * reach);
                    for (var i = 1; i <= 10; i++)
                    {
                        var t = i / 10f;
                        Ellipse(p, Vector2.Lerp(ball, aim, t), 3.2f - t * 1.4f, 3.2f - t * 1.4f, new Color(1f, 1f, 1f, 0.85f - t * 0.5f));
                    }
                }
            }

            var by = (float)MiniGolf.HeightAt(hole, g.Ball.X, g.Ball.Z);
            var ballAt = WorldToView(g.Ball.X, g.Ball.Z) + new Vector2(0f, -by * _scale);
            var sink = g.Phase == "pause" && g.SinkT > 0.0 ? 1f - (float)(g.SinkT / MiniGolfGame.SinkSec) : 0f;
            if (sink < 0.95f)
            {
                Ellipse(p, ballAt + new Vector2(3f, 4f), 8f, 5f, new Color(0f, 0f, 0f, 0.18f));
                Ellipse(p, ballAt, 9f * (1f - 0.5f * sink), 9f * (1f - 0.5f * sink), BallC);
            }
        }

        private void Band(Painter2D p, float y0, float y1, Color a, Color b)
        {
            const int steps = 5;
            for (var i = 0; i < steps; i++)
            {
                var t0 = i / (float)steps; var t1 = (i + 1) / (float)steps;
                Poly(p, new[] {
                    new Vector2(0f, Mathf.Lerp(y0, y1, t0)), new Vector2(FieldW, Mathf.Lerp(y0, y1, t0)),
                    new Vector2(FieldW, Mathf.Lerp(y0, y1, t1)), new Vector2(0f, Mathf.Lerp(y0, y1, t1)),
                }, Color.Lerp(a, b, (t0 + t1) * 0.5f));
            }
        }

        private static Vector2[] Rect(Vector2 c, float hx, float hy) => new[]
        {
            c + new Vector2(-hx, -hy), c + new Vector2(hx, -hy), c + new Vector2(hx, hy), c + new Vector2(-hx, hy),
        };

        private static void Poly(Painter2D p, Vector2[] pts, Color color)
        {
            p.fillColor = color;
            p.BeginPath(); p.MoveTo(pts[0]);
            for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath(); p.Fill();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b) { p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i <= 20; i++)
            {
                var a = Mathf.PI * 2f * i / 20f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Fill();
        }

        // ── juice (mini_golf.gd _putt / _on_event / _end_hole; the frame pops the pill and plays the score tone) ──

        private Vector2 BallAt()
        {
            var g = _game;
            var by = (float)MiniGolf.HeightAt(g.Current, g.Ball.X, g.Ball.Z);
            if (_stage != null) return _stage.ToField(Cell3(g.Ball.X, g.Ball.Z, 0.2f + by), FieldW, FieldH);
            return WorldToView(g.Ball.X, g.Ball.Z) + new Vector2(0f, -by * _scale);
        }

        private Vector2 CupAt() => _stage != null
            ? _stage.ToField(Cell3(_game.Current.HoleAt.x, _game.Current.HoleAt.z, 0.2f), FieldW, FieldH)
            : WorldToView(_game.Current.HoleAt.x, _game.Current.HoleAt.z);

        /// <summary>The putt: a dust puff off the tee, a whoosh pitched by the swing, a knock that grows with the power.</summary>
        private void OnPutt(double power)
        {
            if (_game == null || FieldW <= 1f) return;
            var k = Mathf.Clamp01((float)(power / Math.Max(0.1, _game.Tune.MaxPower)));
            StageEvent("putt");
            Burst(BallAt(), FeltDark, 6);
            PlaySfx("game_whoosh", -3f + 6f * k, 0.6f + 0.4f * k);
            if (!ReducedMotion) Shake(0.05f + 0.07f * k);
            Haptics.Tap();
        }

        /// <summary>Rails, windmill, bump and nougat each answer at the ball (one burst per tag and breath).</summary>
        private void OnBallEvent(string tag)
        {
            if (_game == null || FieldW <= 1f) return;
            var now = Time.unscaledTime;
            if (tag == _lastBallTag && now - _lastBallFxAt < 0.1f && now >= _lastBallFxAt) return;
            _lastBallTag = tag;
            _lastBallFxAt = now;
            var at = BallAt();
            switch (tag)
            {
                case "bank":
                    Burst(at, Rail, 6);
                    PlaySfx("game_pop", 3f, 0.7f);
                    break;
                case "windmill":
                    StageEvent("bonk");
                    Burst(at, new Color(0.85f, 0.86f, 0.88f), 10);
                    PlaySfx("game_hit", -5f);
                    if (!ReducedMotion) Shake(0.2f);
                    Haptics.Tap();
                    break;
                case "bump":
                    Burst(at, new Color(0.45f, 0.72f, 0.42f), 6);
                    PlaySfx("game_pop", -2f, 0.6f);
                    break;
                case "nougat":
                    Burst(at, new Color(0.62f, 0.45f, 0.3f), 6);
                    PlaySfx("game_pop", -5f, 0.6f);
                    break;
            }
        }

        /// <summary>
        /// In the cup: gold sparks + "+N" at the flag; at or under par the perfect tone climbs with the hole streak; a
        /// hole-in-one is the moment of the game (gold flash, confetti, success buzz); a streak shows its "Serie ×n" there
        /// (the streak line is hidden in this host).
        /// </summary>
        private void OnHoled(int points, bool ace)
        {
            if (_game == null || FieldW <= 1f) return;
            var g = _game;
            var cup = CupAt();
            var underPar = g.Strokes <= g.CurrentPar;
            StageEvent(ace ? "ace" : "holed");
            Burst(cup, Flag, 10);
            Burst(cup, new Color(1f, 0.85f, 0.35f), ace ? 26 : 16);
            Float(cup + new Vector2(0f, -_scale * 0.7f), "+" + points, new Color(1f, 0.72f, 0.2f), ace ? 34f : 28f);
            if (ace)
            {
                Burst(cup, Color.white, 14);
                ConfettiRain(60);
                Flash(new Color(1f, 0.9f, 0.5f, 0.26f), 0.26f);
                PlaySfx("game_perfect", Sfx.ComboSemitones(g.HoleStreak));
                Haptics.Success();
            }
            else if (underPar) PlaySfx("game_perfect", Sfx.ComboSemitones(g.HoleStreak), 0.85f);
            if (g.HoleStreak >= 2)
                Float(cup + new Vector2(0f, -_scale * 0.7f - 40f), Host.Strings.T("mg.game.streak", ("n", g.HoleStreak)), new Color(0.95f, 0.45f, 0.66f), 24f);
            if (g.HoleStreak >= 3 && !ace) ConfettiRain(24);
        }

        /// <summary>Out of strokes: a grey puff at the ball, the miss tone, a small knock.</summary>
        private void OnGaveUp()
        {
            if (_game == null || FieldW <= 1f) return;
            StageEvent("giveup");
            Burst(BallAt(), new Color(0.62f, 0.62f, 0.6f), 8);
            PlaySfx("game_miss");
            if (!ReducedMotion) Shake(0.12f);
            Haptics.Warn();
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            _game = null;
            _holePill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class CourseCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public CourseCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
