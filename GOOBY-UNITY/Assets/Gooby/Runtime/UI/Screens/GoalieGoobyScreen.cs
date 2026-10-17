using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Torwart-Gooby host on the shared minigame frame (port of goalie_gooby.gd + its stadium stage), landscape:
    /// the goal seen from behind the striker, painted in perspective with the Painter2D — stands with a pastel
    /// crowd, the striped pitch with its boxes, posts, crossbar and net. Gooby keeps goal in the middle; the target
    /// lane glows and a ring shrinks on it while the striker runs up, then the ball flies (lobs arc high, rollers
    /// skid along the grass). A swipe dives into a lane — up for lobs, down for rollers — and a dive within the
    /// hold window before the ball arrives is a save (+4, +2 super), otherwise the net ripples red. Every tenth save
    /// the crowd cheers and the kicks speed up; the last ten seconds are the shoot-out.
    /// </summary>
    public sealed partial class GoalieGoobyScreen : MinigameHostScreen
    {
        public const double ViewW = 844.0, ViewH = 390.0;
        private const double GoalHalfW = 3.1, GoalHRatio = 0.8, SpotZ = 7.4, BallR = 0.16, StrikerRun = 1.9;
        private const double CamY = 3.0, CamZ = 14.8;
        private const float CamPitchDeg = 9f;

        private GoalieGoobyGame _game;
        private PitchCanvas _canvas, _front;
        private VisualElement _gooby, _savesPill;
        private Label _savesLabel;
        private float _show, _shake, _ring, _ringScale, _crowd, _netRipple, _goalFlash, _pipPop;
        private Vector3 _ringAt;
        private float _keeperX, _keeperY, _holdDive;
        private GoalieGoobyGame.DiveInfo _heldDive;
        private readonly List<Spark> _sparks = new List<Spark>();
        private bool _pressed;
        private Vector2 _from;

        private struct Spark { public Vector2 P, V; public float Life, Max, Size; public Color C; }

        private static readonly Color Grass = MgScenery.Hex(0x8CCB84);
        private static readonly Color GrassLight = MgScenery.Hex(0xA2D896);
        private static readonly Color Post = new Color(0.99f, 0.99f, 0.97f);
        private static readonly Color Sky = new Color(0.6f, 0.8f, 0.98f);
        private static readonly Color Stand = MgScenery.Hex(0xD3CCEC);
        private static readonly Color StandDark = MgScenery.Hex(0xB7AEDD);
        private static readonly Color Ball = new Color(0.99f, 0.99f, 0.96f);
        private static readonly Color Gold = new Color(1f, 0.78f, 0.3f);

        public GoalieGoobyScreen(GameHost host) : base(host, "goalieGooby", "goalieGooby") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xA6D99C);
        protected override Color FieldColor => Sky;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public GoalieGoobyGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new GoalieGoobyGame(Difficulty, seed);
            _game.Saved += OnSaved;
            _game.Conceded += OnConceded;
            _game.Cheer += _ => { Banner(Host.Strings.T("mg.goalieGooby.cheer"), 1.6); _crowd = 1.6f; if (_stage != null) StageCheer(); };
            _game.Dived += _ => _shake = Mathf.Max(_shake, 0.06f);
            _game.Kicked += _ => { if (_stage != null) StageKick(); };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e => { if (_game == null || !_game.Running) return; _pressed = true; _from = e.localPosition; Field.CapturePointer(e.pointerId); });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                if (_game == null || !_pressed) return;
                _pressed = false;
                var d = ((Vector2)e.localPosition - _from) * (float)(ViewW / Mathf.Max(1f, FieldW));
                _game.DiveBy(d.x, d.y);
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _pressed = false);

            if (World.MgStage3D.Supported) BuildStage();
            _canvas = new PitchCanvas { Paint = Paint };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(90);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;
            else FollowGooby(_gooby, 1.8f, grounded: true);

            // the ball, the telegraph ring and the sparks fly in front of the keeper
            _front = new PitchCanvas { Paint = PaintFront, name = "pitch-front" };
            Ux.Abs(_front, 0, 0, 0, 0);
            Field.Add(_front);

            _savesPill = Ux.El("gb-card", "gb-row");
            _savesPill.name = "saves-pill";
            Ux.Abs(_savesPill, null, 12);
            _savesPill.style.left = Length.Percent(50);
            _savesPill.style.translate = new Translate(Length.Percent(-50), 0);
            _savesPill.style.alignItems = Align.Center;
            _savesPill.style.paddingTop = 6; _savesPill.style.paddingBottom = 6; _savesPill.style.paddingLeft = 12; _savesPill.style.paddingRight = 16;
            _savesPill.pickingMode = PickingMode.Ignore;
            var icon = Ux.Icon("hand");
            icon.style.width = 30; icon.style.height = 30; icon.style.marginRight = 8;
            _savesPill.Add(icon);
            _savesLabel = Ux.Text("", "gb-md", "gb-bold");
            _savesPill.Add(_savesLabel);
            Root.Add(_savesPill);
            ComboLabel.style.top = 70;
            Banner(t.T("mg.goalieGooby.hint"), GoalieGoobyGame.IntroSec + 0.8);
        }

        // ── the round ──────────────────────────────────────────────────────

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            var f = (float)dt;
            _show += f;
            _shake = Mathf.Max(0f, _shake - f);
            _ring = Mathf.Max(0f, _ring - f);
            _crowd = Mathf.Max(0f, _crowd - f * 0.9f);
            _netRipple = Mathf.Max(0f, _netRipple - f * 1.6f);
            _goalFlash = Mathf.Max(0f, _goalFlash - f * 3f);
            _pipPop = Mathf.Max(0f, _pipPop - f);
            _holdDive = Mathf.Max(0f, _holdDive - f);
            // the keeper slides to the dive lane and height, back to the middle between kicks
            var (tx, ty) = KeeperTarget();
            var k = Mathf.Min(1f, f * 14f);
            _keeperX += (tx - _keeperX) * k; _keeperY += (ty - _keeperY) * k;
            for (var i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i]; s.Life -= f;
                if (s.Life <= 0f) { _sparks.RemoveAt(i); continue; }
                s.V.y += 300f * f; s.P += s.V * f; _sparks[i] = s;
            }
        }

        /// <summary>The dive on show: the live one, then the last one held a moment after its kick resolved.</summary>
        private GoalieGoobyGame.DiveInfo ShownDive =>
            _game.Dive != null && _game.Current != null ? _game.Dive : _holdDive > 0f ? _heldDive : null;

        private (float x, float y) KeeperTarget()
        {
            var d = ShownDive;
            if (d == null) return (0f, 0.9f);
            var x = (float)LaneX(d.Lane);
            var y = d.V == "up" ? 1.9f : d.V == "down" ? 0.35f : 0.9f;
            return (x, y);
        }

        private static double LaneX(int lane) => ((lane - 2) / 2.0) * (GoalHalfW - 0.42);
        private static double GoalH => GoalHalfW * GoalHRatio + 0.4;
        private static double KickHeight(string kind) => kind == "lob" ? GoalH * 0.82 : kind == "roller" ? BallR + 0.04 : GoalH * 0.42;

        /// <summary>The ball in the world: on the spot, then along its flight to the target lane.</summary>
        private Vector3 BallWorld()
        {
            var kick = _game.Current;
            var spot = new Vector3(0f, (float)BallR, (float)SpotZ);
            if (kick == null || !_game.BallInFlight) return spot;
            var p = (float)_game.KickProgress;
            var target = new Vector3((float)LaneX(kick.Lane), (float)KickHeight(kick.Kind), 0f);
            var pos = Vector3.Lerp(spot, target, p);
            var arc = kick.Kind == "lob" ? 1.6f : kick.Kind == "roller" ? 0f : 0.5f;
            pos.y += Mathf.Sin(p * Mathf.PI) * arc;
            return pos;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.goalieGooby.conceded", ("n", _game.Goals), ("max", _game.Tune.EndlessGoals))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            _savesLabel.text = t.T("mg.goalieGooby.saves", ("n", _game.Saves));
            ComboLabel.text = _game.SaveStreak >= 2 ? t.T("mg.game.streak", ("n", _game.SaveStreak)) : "";
            if (_stage != null)
            {
                RenderStage();
                _front.MarkDirtyRepaint();
                return;
            }
            // Gooby the keeper in the goal, between the posts on the line
            var feet = Project(_keeperX, 0.0, 0.15);
            var head = Project(_keeperX, 1.35, 0.15);
            var size = Mathf.Max(50f, feet.y - head.y);
            var centre = Project(_keeperX, _keeperY + 0.45, 0.15);
            _gooby.style.width = size; _gooby.style.height = size;
            _gooby.style.left = centre.x - size * 0.5f; _gooby.style.top = centre.y - size * 0.5f;
            var d = ShownDive;
            var lean = d != null ? (d.Lane - 2) * 9f : 0f;
            _gooby.style.rotate = new Rotate(lean);
            _canvas.MarkDirtyRepaint();
            _front.MarkDirtyRepaint();
        }

        // ── dressing (board 06, "Fußballwiese"): a striped awning over the stands, two strings of bunting in front of
        //    the crowd and pastel ad boards along the pitch edge — painted with the stands (pure
        //    decor, no gameplay object moves; the crowd keeps its cheer bob). ──

        private static readonly Color[] FlagColors = { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFD166), MgScenery.Hex(0x9BE7E1), MgScenery.Hex(0xB7D8FF), MgScenery.Hex(0xC6F39A) };

        private void PaintAwning(Painter2D p)
        {
            // the awning: stripes with a scalloped hem across the top of the view (the top crowd row sits in front of it)
            var aw = FieldH * 0.05f; var stripes = 8; var sw = FieldW / stripes;
            for (var i = 0; i < stripes; i++)
            {
                var c = i % 2 == 0 ? MgScenery.Hex(0xFF9BC1) : MgScenery.Hex(0xFFF6EA);
                p.fillColor = c;
                p.BeginPath(); p.MoveTo(new Vector2(i * sw, 0f)); p.LineTo(new Vector2((i + 1) * sw, 0f)); p.LineTo(new Vector2((i + 1) * sw, aw));
                p.QuadraticCurveTo(new Vector2((i + 0.5f) * sw, aw + sw * 0.6f), new Vector2(i * sw, aw)); p.ClosePath(); p.Fill();
                p.strokeColor = MgScenery.Hex(0xE97FA6); p.lineWidth = 2f; p.Stroke();
            }
        }

        private void PaintStandDressing(Painter2D p, float standBottom)
        {
            // bunting in front of the lowest rows
            PaintBunting(p, standBottom - FieldH * 0.075f, 14f, 12, 0);
            PaintBunting(p, standBottom - FieldH * 0.2f, 10f, 9, 2);
            // the ad boards along the pitch edge
            var bh = Mathf.Max(12f, FieldH * 0.02f); var boards = 5; var bw = FieldW / boards;
            for (var i = 0; i < boards; i++)
            {
                var c = FlagColors[(i + 1) % FlagColors.Length];
                FillRect(p, i * bw + 2f, standBottom - bh, bw - 4f, bh, c);
                FillRect(p, i * bw + bw * 0.25f, standBottom - bh * 0.62f, bw * 0.5f, bh * 0.24f, new Color(1f, 1f, 1f, 0.8f));
            }
            FillRect(p, 0f, standBottom - 3f, FieldW, 3f, MgScenery.Hex(0xE3CBA8));
        }

        private void PaintBunting(Painter2D p, float y, float sag, int flags, int shift)
        {
            var a = new Vector2(-4f, y); var b = new Vector2(FieldW + 4f, y); var c = new Vector2(FieldW * 0.5f, y + sag * 2f);
            for (var i = 0; i < flags; i++)
            {
                var t0 = (i + 0.14f) / flags; var t1 = (i + 0.86f) / flags;
                var p0 = Bez(a, c, b, t0); var p1 = Bez(a, c, b, t1);
                var col = FlagColors[(i + shift) % FlagColors.Length];
                p.fillColor = col;
                p.BeginPath(); p.MoveTo(p0); p.LineTo(p1); p.LineTo((p0 + p1) * 0.5f + new Vector2(0f, 20f)); p.ClosePath(); p.Fill();
            }
            p.strokeColor = MgScenery.Hex(0xFFF6EA); p.lineWidth = 2f;
            p.BeginPath(); p.MoveTo(a); p.QuadraticCurveTo(c, b); p.Stroke();
        }

        private static Vector2 Bez(Vector2 a, Vector2 c, Vector2 b, float t) { var u = 1f - t; return u * u * a + 2f * u * t * c + t * t * b; }

        // ── camera ─────────────────────────────────────────────────────────

        private float Focal => FieldH * 1.7f;

        private Vector2 Project(double x, double y, double z)
        {
            var th = CamPitchDeg * Mathf.Deg2Rad;
            var dy = y - CamY; var dz = z - CamZ;
            var depth = -dy * Math.Sin(th) - dz * Math.Cos(th);
            if (depth < 0.4) depth = 0.4;
            var yv = dy * Math.Cos(th) - dz * Math.Sin(th);
            var sx = FieldW * 0.5f + (float)(x / depth) * Focal;
            var sy = FieldH * 0.5f - (float)(yv / depth) * Focal;
            if (_shake > 0f && !ReducedMotion) { sx += Mathf.Sin(Time.unscaledTime * 90f) * _shake * 50f; sy += Mathf.Cos(Time.unscaledTime * 75f) * _shake * 30f; }
            return new Vector2(sx, sy);
        }

        private float Depth(double y, double z)
        {
            var th = CamPitchDeg * Mathf.Deg2Rad;
            return Mathf.Max(0.4f, (float)(-(y - CamY) * Math.Sin(th) - (z - CamZ) * Math.Cos(th)));
        }

        // ── painting ───────────────────────────────────────────────────────

        private void Paint(Painter2D p)
        {
            if (_game == null || _stage != null || FieldW <= 0f || FieldH <= 0f) return;
            // the stands behind the goal with their crowd (they fill the view above the pitch, no sky shows), the pitch
            var standTop = Project(0.0, 6.5, -6.0).y; var standBottom = Project(0.0, 0.0, -6.0).y;
            FillRect(p, 0f, standTop, FieldW, standBottom - standTop, Stand);
            PaintAwning(p);
            for (var row = 0; row < 4; row++)
            {
                var y = Mathf.Lerp(standTop, standBottom, (row + 1f) / 5f);
                FillRect(p, 0f, y, FieldW, 6f, StandDark);
                for (var i = 0; i < 16; i++)
                {
                    var x = FieldW * (i + 0.5f) / 16f + (row % 2) * 14f;
                    var bob = Mathf.Sin(_show * 8f + i * 0.9f + row) * _crowd * 6f;
                    var c = (i + row) % 4 == 0 ? new Color(1f, 0.62f, 0.64f) : (i + row) % 4 == 1 ? new Color(0.98f, 0.86f, 0.5f) : (i + row) % 4 == 2 ? new Color(0.62f, 0.78f, 1f) : new Color(0.72f, 0.9f, 0.7f);
                    Circle(p, new Vector2(x, y - 10f - bob), 8f, c);
                }
            }
            PaintStandDressing(p, standBottom);
            FillRect(p, 0f, standBottom, FieldW, FieldH - standBottom, Grass);
            for (var i = 0; i < 6; i++)
            {
                var z0 = -6.0 + i * 3.2; var z1 = z0 + 1.6;
                Poly(p, new[] { Project(-30.0, 0.0, z0), Project(30.0, 0.0, z0), Project(30.0, 0.0, z1), Project(-30.0, 0.0, z1) }, GrassLight);
            }
            // lines: goal line, six-yard and penalty boxes, the spot
            p.strokeColor = Post; p.lineWidth = 3f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            Line(p, Project(-14.0, 0.01, 0.0), Project(14.0, 0.01, 0.0));
            StrokePoly(p, new[] { Project(-GoalHalfW - 1.6, 0.01, 0.0), Project(GoalHalfW + 1.6, 0.01, 0.0), Project(GoalHalfW + 1.6, 0.01, 3.0), Project(-GoalHalfW - 1.6, 0.01, 3.0) });
            StrokePoly(p, new[] { Project(-GoalHalfW - 5.0, 0.01, 0.0), Project(GoalHalfW + 5.0, 0.01, 0.0), Project(GoalHalfW + 5.0, 0.01, 8.6), Project(-GoalHalfW - 5.0, 0.01, 8.6) });
            Circle(p, Project(0.0, 0.01, SpotZ), 4f, Post);
            // the lanes: faint strips, the telegraphed one glowing
            var kick = _game.Current;
            for (var lane = 0; lane < 5; lane++)
            {
                var x = LaneX(lane); var hw = (GoalHalfW - 0.42) / 4.0 * 0.45;
                var glow = kick != null && kick.Lane == lane && !_game.BallInFlight ? 0.25f + 0.25f * Mathf.Sin(_show * 14f) : 0.06f;
                Poly(p, new[] { Project(x - hw, 0.02, 0.2), Project(x + hw, 0.02, 0.2), Project(x + hw, 0.02, SpotZ - 0.6), Project(x - hw, 0.02, SpotZ - 0.6) }, new Color(1f, 1f, 0.8f, glow));
            }
            // the goal: net, posts, crossbar (the net ripples after a goal)
            var goalH = GoalH; var netZ = -1.4;
            p.strokeColor = new Color(1f, 1f, 1f, 0.55f); p.lineWidth = 1.5f;
            for (var i = 0; i <= 12; i++)
            {
                var x = -GoalHalfW + GoalHalfW * 2.0 * i / 12.0;
                var ripple = Math.Sin(_show * 30f + i) * _netRipple * 0.12;
                Line(p, Project(x, goalH, 0.0), Project(x + ripple, goalH * 0.98, netZ));
                Line(p, Project(x + ripple, goalH * 0.98, netZ), Project(x + ripple, 0.0, netZ));
            }
            for (var i = 0; i <= 6; i++)
            {
                var y = goalH * i / 6.0;
                Line(p, Project(-GoalHalfW, y, netZ), Project(GoalHalfW, y, netZ));
            }
            for (var side = -1; side <= 1; side += 2)
            {
                Line(p, Project(side * GoalHalfW, 0.0, netZ), Project(side * GoalHalfW, 0.0, 0.0));
                Line(p, Project(side * GoalHalfW, goalH, netZ), Project(side * GoalHalfW, goalH, 0.0));
                Line(p, Project(side * GoalHalfW, goalH * 0.5, netZ), Project(side * GoalHalfW, goalH * 0.5, 0.0));
            }
            p.strokeColor = Post; p.lineWidth = Mathf.Max(4f, 0.12f * Focal / Depth(1.5, 0.0));
            Line(p, Project(-GoalHalfW, 0.0, 0.0), Project(-GoalHalfW, goalH, 0.0));
            Line(p, Project(GoalHalfW, 0.0, 0.0), Project(GoalHalfW, goalH, 0.0));
            Line(p, Project(-GoalHalfW, goalH, 0.0), Project(GoalHalfW, goalH, 0.0));
            // the striker: runs up during the telegraph, plants the kick — beside the spot so he never hides the ball or the goal
            var run = kick != null && !_game.BallInFlight ? (float)_game.TelegraphProgress : kick != null ? 1f : 0f;
            PaintStriker(p, 1.25, SpotZ + StrikerRun * (1.0 - run) + 0.35, run);
        }

        private void PaintFront(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            var kick = _game.Current;
            // the telegraph ring shrinking onto the target
            if (kick != null && !_game.BallInFlight)
            {
                var at = Project(LaneX(kick.Lane), KickHeight(kick.Kind), 0.0);
                var r = Mathf.Lerp(60f, 14f, (float)_game.TelegraphProgress);
                p.strokeColor = new Color(1f, 0.55f, 0.15f, 0.95f); p.lineWidth = 5f;   // warm orange: unlike anything in the stands
                p.BeginPath(); p.Arc(at, r, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
                // a wedge above (lob) or below (roller) the ring tells the height the dive needs
                if (kick.Kind != "straight")
                {
                    var up = kick.Kind == "lob";
                    Poly(p, new[] { at + new Vector2(0f, up ? -r - 18f : r + 18f), at + new Vector2(-10f, up ? -r - 4f : r + 4f), at + new Vector2(10f, up ? -r - 4f : r + 4f) }, new Color(1f, 0.55f, 0.15f, 0.95f));
                }
            }
            // the ball with its shadow (the stadium has a real one), then the gloves' save ring and sparks
            if (_stage == null) PaintBall(p);
            if (_ring > 0f)
            {
                var at = Project(_ringAt.x, _ringAt.y, _ringAt.z);
                var k = 1f - _ring / 0.38f;
                p.strokeColor = new Color(1f, 0.85f, 0.4f, 1f - k); p.lineWidth = 5f;
                p.BeginPath(); p.Arc(at, 16f + k * 40f * _ringScale, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
            }
            foreach (var s in _sparks)
            {
                var c = s.C; c.a = Mathf.Clamp01(s.Life / s.Max);
                Circle(p, s.P, s.Size, c);
            }
            // the three goal pips (conceded ones filled red) under the saves pill
            for (var i = 0; i < _game.MaxGoals; i++)
            {
                var c = new Vector2(FieldW * 0.5f + (i - (_game.MaxGoals - 1) * 0.5f) * 26f, 66f);
                var filled = i < _game.Goals;
                var pop = filled && i == _game.Goals - 1 ? 1f + _pipPop * 0.7f : 1f;
                Circle(p, c, 8f * pop, filled ? new Color(0.9f, 0.32f, 0.22f) : new Color(1f, 1f, 1f, 0.6f));
            }
            if (_goalFlash > 0f) FillRect(p, 0f, 0f, FieldW, FieldH, new Color(0.9f, 0.32f, 0.22f, 0.16f * _goalFlash));
        }

        private void PaintBall(Painter2D p)
        {
            var ball = BallWorld();
            var sh = Project(ball.x, 0.02, ball.z);
            Ellipse(p, sh, 0.22f * Focal / Depth(0.0, ball.z), 0.09f * Focal / Depth(0.0, ball.z), new Color(0f, 0f, 0f, 0.25f));
            var b = Project(ball.x, ball.y, ball.z);
            var br = (float)BallR * Focal / Depth(ball.y, ball.z);
            Circle(p, b, br, Ball);
            p.strokeColor = new Color(0.2f, 0.2f, 0.25f); p.lineWidth = Mathf.Max(1f, br * 0.12f);
            var spin = (float)_game.KickProgress * 12f + _show * 2f;
            for (var i = 0; i < 3; i++)
            {
                var a = spin + i * Mathf.PI * 2f / 3f;
                Circle(p, b + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * br * 0.5f, br * 0.22f, new Color(0.2f, 0.2f, 0.25f));
            }
        }

        /// <summary>The striker: a round head, a striped jersey, shorts and two legs — one swings on the kick.</summary>
        private void PaintStriker(Painter2D p, double x, double z, float run)
        {
            var scale = Focal / Depth(1.0, z);
            var feet = Project(x, 0.0, z);
            var kickSwing = run >= 1f ? 0.6f : 0f;
            var legL = new Vector2(-0.12f, 0f) * scale; var legR = new Vector2(0.12f + kickSwing * 0.3f, -kickSwing * 0.25f) * scale;
            var stride = run < 1f ? Mathf.Sin(run * 18f) * 0.08f * scale : 0f;
            p.strokeColor = new Color(0.96f, 0.8f, 0.7f); p.lineWidth = Mathf.Max(3f, 0.09f * scale); p.lineCap = LineCap.Round;
            Line(p, feet + new Vector2(0f, -0.45f * scale), feet + legL + new Vector2(stride, 0f));
            Line(p, feet + new Vector2(0f, -0.45f * scale), feet + legR - new Vector2(stride, 0f));
            FillRect(p, feet.x - 0.2f * scale, feet.y - 0.62f * scale, 0.4f * scale, 0.22f * scale, new Color(0.2f, 0.25f, 0.5f));
            FillRect(p, feet.x - 0.24f * scale, feet.y - 1.1f * scale, 0.48f * scale, 0.5f * scale, new Color(0.95f, 0.35f, 0.3f));
            FillRect(p, feet.x - 0.24f * scale, feet.y - 0.95f * scale, 0.48f * scale, 0.1f * scale, new Color(1f, 0.95f, 0.9f));
            Circle(p, feet + new Vector2(0f, -1.28f * scale), 0.19f * scale, new Color(0.96f, 0.8f, 0.7f));
            Circle(p, feet + new Vector2(0f, -1.36f * scale), 0.19f * scale * 0.85f, new Color(0.35f, 0.22f, 0.15f));
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

        private void HoldDive()
        {
            _heldDive = _game.Dive;
            _holdDive = _heldDive != null ? 0.45f : 0f;
        }

        private void OnSaved(GoalieGoobyGame.Kick kick, int points, bool super)
        {
            HoldDive();
            var world = new Vector3((float)LaneX(kick.Lane), (float)KickHeight(kick.Kind), 0f);
            _ringAt = world; _ring = 0.38f; _ringScale = super ? 5.2f : 3.2f;
            _crowd = Mathf.Max(_crowd, super ? 1.25f : 1f);
            _shake = super ? 0.06f : 0.03f;
            var at = Project(world.x, world.y, world.z);
            for (var i = 0; i < 18; i += ParticleStep)
            {
                var a = i * Mathf.PI * 2f / 18f;
                var sp = 100f + (i * 31) % 80;
                _sparks.Add(new Spark { P = at, V = new Vector2(Mathf.Cos(a) * sp, Mathf.Sin(a) * sp - 90f), Life = 0.6f, Max = 0.6f, Size = 3f + (i % 3), C = Gold });
            }
            if (super) Banner(Host.Strings.T("mg.goalieGooby.super"), 1.0);
            else FloatText(at, "+" + points, Gold);
            if (_stage != null) StageSaved(super);
        }

        private void OnConceded(GoalieGoobyGame.Kick kick)
        {
            HoldDive();
            _netRipple = 1f; _goalFlash = 1f; _pipPop = 0.4f; _shake = 0.1f; _crowd = Mathf.Max(_crowd, 0.45f);
            var at = Project(LaneX(kick.Lane), KickHeight(kick.Kind), 0.0);
            FloatText(at, Host.Strings.T("mg.goalieGooby.goal"), new Color(0.9f, 0.32f, 0.22f));
            if (_stage != null) StageConceded();
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _sparks.Clear();
            _savesPill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class PitchCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public PitchCanvas()
            {
                pickingMode = PickingMode.Ignore;
                name = "pitch";   // the front layer renames itself
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
