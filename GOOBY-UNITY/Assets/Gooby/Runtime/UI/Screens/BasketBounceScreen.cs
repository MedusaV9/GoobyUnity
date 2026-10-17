using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Korbwurf host on the shared minigame frame (port of basket_bounce.gd + its park stage): a sunlit park court
    /// seen from behind Gooby's shoulder, drawn in perspective with the Painter2D — the hoop with board, pole and
    /// net stands 5–8 m out, the ball rests on the spot at the bottom. A flick up the screen throws it; the ball
    /// arcs in 3D, clangs off the rim, banks off the board and drops through the net (+3, +2 for a bank, +2 for the
    /// second swish in a row, doubled once the hoop slides after ten baskets). Misses puff dust on the court.
    /// </summary>
    public sealed partial class BasketBounceScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private BasketBounceGame _game;
        private CourtCanvas _canvas;
        private VisualElement _gooby, _missPill;
        private Label _missLabel;
        private float _rimPulse, _netPulse, _crowdPulse, _goobyHop;
        private readonly List<Spark> _sparks = new List<Spark>();
        private float _shake;

        private struct Spark { public Vector2 P, V; public float Life, Max, Size; public Color C; }

        private static readonly Color SkyTop = new Color(0.4f, 0.66f, 0.96f);
        private static readonly Color Grass = MgScenery.Hex(0x93CD8A);
        private static readonly Color GrassFar = MgScenery.Hex(0xB8E2B2);
        private static readonly Color Court = MgScenery.Hex(0xE8A67C);
        private static readonly Color CourtLine = new Color(0.99f, 0.97f, 0.93f);
        private static readonly Color BallColor = new Color(0.94f, 0.52f, 0.19f);
        private static readonly Color Seam = new Color(0.45f, 0.22f, 0.09f);
        private static readonly Color Rim = new Color(0.94f, 0.35f, 0.22f);
        private static readonly Color Pole = MgScenery.Hex(0xA3AFC4);
        private static readonly Color TreeDark = MgScenery.Hex(0x6DBE78);
        private static readonly Color TreeLight = MgScenery.Hex(0x9ED499);
        private static readonly Color Hedge = MgScenery.Hex(0x7CC486);
        private static readonly Color FenceWood = MgScenery.Hex(0xF6E4C8);
        private static readonly Color FenceRim = MgScenery.Hex(0xC8A27A);

        private const float CamPitchDeg = 14f;
        private const double CamY = 3.2, CamBackM = 4.6;

        public BasketBounceScreen(GameHost host) : base(host, "basketBounce", "basketBounce") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0x9FD3F2);
        protected override Color FieldColor => SkyTop;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public BasketBounceGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new BasketBounceGame(Difficulty, seed);
            _game.RimHit += () => { _rimPulse = 0.28f; _shake = 0.05f; };
            _game.BoardHit += () => { _rimPulse = Mathf.Max(_rimPulse, 0.2f); _shake = 0.04f; };
            _game.Scored += OnScored;
            _game.MissedShot += OnMissed;
            _game.Launched += _ => { if (_stage != null) StageLaunched(); };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(OnDown);
            Field.RegisterCallback<PointerMoveEvent>(OnMove);
            Field.RegisterCallback<PointerUpEvent>(OnUp);
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _game?.CancelDrag());

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new CourtCanvas { Paint = Paint };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            // Gooby stands beside the spot, a little to the left, watching the shot
            _gooby = Ux.Portrait(150);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;
            else FollowGooby(_gooby, 1.5f, grounded: true, fromBehind: true);

            _missPill = Ux.El("gb-card", "gb-row");
            _missPill.name = "miss-pill";
            Ux.Abs(_missPill, null, 62);
            _missPill.style.left = Length.Percent(50);
            _missPill.style.translate = new Translate(Length.Percent(-50), 0);
            _missPill.style.alignItems = Align.Center;
            _missPill.style.paddingTop = 6; _missPill.style.paddingBottom = 6; _missPill.style.paddingLeft = 12; _missPill.style.paddingRight = 16;
            _missPill.pickingMode = PickingMode.Ignore;
            var icon = Ux.Icon("star");
            icon.style.width = 34; icon.style.height = 34; icon.style.marginRight = 8;
            _missPill.Add(icon);
            _missLabel = Ux.Text("", "gb-md", "gb-bold");
            _missPill.Add(_missLabel);
            _missPill.style.display = _game.Tune.Endless ? DisplayStyle.Flex : DisplayStyle.None;   // the miss streak only counts in endless
            Root.Add(_missPill);
            ComboLabel.style.top = 128;
            Banner(t.T("mg.basketBounce.hint"), BasketBounceGame.IntroSec + 0.7);
        }

        // ── the flick ──────────────────────────────────────────────────────

        private Vector2 ToView(Vector2 local) => new Vector2(local.x * (float)(ViewH / FieldH), local.y * (float)(ViewH / FieldH));

        private void OnDown(PointerDownEvent e)
        {
            if (_game == null || !_game.Aiming || FieldH <= 0f) return;
            var v = ToView(e.localPosition);
            _game.BeginDrag(v.x, v.y);
            Field.CapturePointer(e.pointerId);
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_game == null || !_game.Dragging || FieldH <= 0f) return;
            var v = ToView(e.localPosition);
            _game.Drag(v.x, v.y);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
            _game?.EndDrag();
        }

        // ── the round ──────────────────────────────────────────────────────

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            var f = (float)dt;
            _sceneT += f;
            _rimPulse = Mathf.Max(0f, _rimPulse - f);
            _netPulse = Mathf.Max(0f, _netPulse - f);
            _crowdPulse = Mathf.Max(0f, _crowdPulse - f * 1.2f);
            _goobyHop = Mathf.Max(0f, _goobyHop - f * 2f);
            _shake = Mathf.Max(0f, _shake - f);
            for (var i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.Life -= f;
                if (s.Life <= 0f) { _sparks.RemoveAt(i); continue; }
                s.V.y += 420f * f;
                s.P += s.V * f;
                _sparks[i] = s;
            }
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.basketBounce.misses", ("n", _game.MissStreak), ("max", _game.Tune.EndlessConsecutiveMisses))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            ComboLabel.text = _game.OnFire ? t.T("mg.basketBounce.on_fire") : _game.SwishStreak > 1 ? t.T("mg.game.streak", ("n", _game.SwishStreak)) : "";
            if (_game.Tune.Endless) _missLabel.text = t.T("mg.basketBounce.misses", ("n", _game.MissStreak), ("max", _game.Tune.EndlessConsecutiveMisses));
            if (_stage != null)
            {
                RenderStage();
                _canvas.MarkDirtyRepaint();
                return;
            }

            // Gooby beside the spot, a step back and to the left: his feet on the court, hopping on a basket
            var feet = Project(_game.Tune.SpawnX - 1.35, 0.0, _game.Tune.SpawnZ - 0.9);
            var head = Project(_game.Tune.SpawnX - 1.35, 1.3, _game.Tune.SpawnZ - 0.9);
            var h = Mathf.Clamp(feet.y - head.y, 60f, FieldW * 0.3f);
            _gooby.style.width = h; _gooby.style.height = h;
            // the narrow portrait field puts that spot on its left edge: keep the whole Gooby on the court
            _gooby.style.left = Mathf.Clamp(feet.x - h * 0.5f, 6f, Mathf.Max(6f, FieldW - h - 6f));
            _gooby.style.top = feet.y - h - _goobyHop * 40f;
            _gooby.style.rotate = new Rotate(_game.Phase == BasketBounceGame.PhaseFly ? -8f : _game.Dragging ? 6f : 0f);
            DriftClouds();
            _canvas.MarkDirtyRepaint();
        }

        // ── the camera ─────────────────────────────────────────────────────

        private double CamZ => _game.Tune.SpawnZ + CamBackM + (_game.HoopDistance - _game.Tune.DistStart) * 0.45;
        private float Focal => FieldH * 0.95f;

        /// <summary>World (metres, x right, y up, z towards the player) → field pixels through a camera behind the spot, pitched down.</summary>
        private Vector2 Project(double x, double y, double z)
        {
            var th = CamPitchDeg * Mathf.Deg2Rad;
            var dx = x; var dy = y - CamY; var dz = z - CamZ;
            var depth = -dy * Math.Sin(th) - dz * Math.Cos(th);
            if (depth < 0.4) depth = 0.4;
            var yv = dy * Math.Cos(th) - dz * Math.Sin(th);
            var f = Focal;
            var sx = FieldW * 0.5f + (float)(dx / depth) * f;
            var sy = FieldH * 0.5f - (float)(yv / depth) * f;
            if (_shake > 0f && !ReducedMotion) { sx += Mathf.Sin(Time.unscaledTime * 80f) * _shake * 60f; sy += Mathf.Cos(Time.unscaledTime * 70f) * _shake * 40f; }
            return new Vector2(sx, sy);
        }

        private float Depth(double y, double z)
        {
            var th = CamPitchDeg * Mathf.Deg2Rad;
            return Mathf.Max(0.4f, (float)(-(y - CamY) * Math.Sin(th) - (z - CamZ) * Math.Cos(th)));
        }

        private float Horizon()
        {
            var th = CamPitchDeg * Mathf.Deg2Rad;
            return FieldH * 0.5f - Mathf.Tan(th) * Focal;
        }

        // ── painting ───────────────────────────────────────────────────────

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            if (_stage != null)
            {
                // the stage shows court, hoop and ball: the canvas keeps the trail, the aim line and the sparks
                if (_game.Phase == BasketBounceGame.PhaseFly) PaintTrail(p);
                PaintAim(p);
                PaintSparks(p);
                return;
            }
            var tune = _game.Tune;
            var hoop = _game.HoopNow();
            var horizon = Horizon();

            // the sky, sun, clouds and far hills are the scenery layer behind the canvas; the park lawn from the horizon down
            FillRect(p, 0f, horizon, FieldW, FieldH - horizon, Grass);
            FillRect(p, 0f, horizon, FieldW, 40f, GrassFar);
            // a treeline behind the court, then a hedge
            for (var i = 0; i < 9; i++)
            {
                var x = -12.0 + i * 3.0 + (i % 2) * 0.7;
                var z = hoop.Z - 9.0 - (i % 3) * 1.5;
                var baseAt = Project(x, 0.0, z);
                var top = Project(x, 3.2 + (i % 2) * 0.8, z);
                var r = Mathf.Max(6f, (baseAt.y - top.y) * 0.5f);
                Circle(p, new Vector2(top.x, top.y + r), r, i % 2 == 0 ? TreeDark : TreeLight);
            }
            var hedgeL = Project(-8.0, 0.0, hoop.Z - 4.0); var hedgeR = Project(8.0, 0.0, hoop.Z - 4.0);
            var hedgeTopL = Project(-8.0, 0.9, hoop.Z - 4.0); var hedgeTopR = Project(8.0, 0.9, hoop.Z - 4.0);
            PaintFence(p, hoop.Z - 6.0);
            Poly(p, new[] { hedgeL, hedgeR, hedgeTopR, hedgeTopL }, Hedge);
            p.strokeColor = MgScenery.Hex(0xA6DB98); p.lineWidth = 3f; p.lineCap = LineCap.Round;
            Line(p, hedgeTopL, hedgeTopR);
            // the crowd on a bench behind the hedge: three pastel blobs bobbing on a basket
            for (var i = 0; i < 3; i++)
            {
                var bob = Mathf.Sin(Time.unscaledTime * 6f + i) * _crowdPulse * 0.25;
                var at = Project(-2.2 + i * 2.2, 1.15 + bob, hoop.Z - 3.6);
                var r = 0.32f * Focal / Depth(1.15, hoop.Z - 3.6);
                Circle(p, at, r, i == 0 ? new Color(1f, 0.6f, 0.62f) : i == 1 ? new Color(0.98f, 0.85f, 0.45f) : new Color(0.6f, 0.75f, 1f));
            }

            // the court: a rust-red slab with cream lines, its near edge just under the camera
            var nearZ = tune.SpawnZ + 1.6; var farZ = hoop.Z - 2.2;
            Poly(p, new[] { Project(-4.2, 0.0, nearZ), Project(4.2, 0.0, nearZ), Project(4.2, 0.0, farZ), Project(-4.2, 0.0, farZ) }, Court);
            p.strokeColor = CourtLine; p.lineWidth = 3f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            Line(p, Project(-4.0, 0.01, farZ + 0.2), Project(4.0, 0.01, farZ + 0.2));           // baseline
            Line(p, Project(-4.0, 0.01, farZ + 0.2), Project(-4.0, 0.01, nearZ - 0.2));       // sidelines
            Line(p, Project(4.0, 0.01, farZ + 0.2), Project(4.0, 0.01, nearZ - 0.2));
            var keyNear = hoop.Z + 2.6;
            Line(p, Project(-1.25, 0.01, farZ + 0.2), Project(-1.25, 0.01, keyNear));          // the key
            Line(p, Project(1.25, 0.01, farZ + 0.2), Project(1.25, 0.01, keyNear));
            Line(p, Project(-1.25, 0.01, keyNear), Project(1.25, 0.01, keyNear));
            p.BeginPath();                                                                     // free-throw arc
            for (var i = 0; i <= 16; i++)
            {
                var a = Math.PI * i / 16.0;
                var at = Project(Math.Cos(a) * 1.25, 0.01, keyNear + Math.Sin(a) * 1.25);
                if (i == 0) p.MoveTo(at); else p.LineTo(at);
            }
            p.Stroke();
            // the shooting spot
            var spot = Project(tune.SpawnX, 0.01, tune.SpawnZ);
            Ellipse(p, spot, 0.5f * Focal / Depth(0.0, tune.SpawnZ), 0.16f * Focal / Depth(0.0, tune.SpawnZ), new Color(1f, 1f, 1f, 0.35f));

            var ball = _game.BallWorld();
            var ballBehindHoop = ball.z < hoop.Z;
            if (ballBehindHoop) PaintBall(p, ball);
            PaintHoop(p, hoop, tune);
            if (!ballBehindHoop) PaintBall(p, ball);

            PaintAim(p);
            PaintSparks(p);
        }

        private void PaintAim(Painter2D p)
        {
            // the aim line while the finger is down
            if (_game.Dragging)
            {
                var k = (float)(FieldH / ViewH);
                var a = new Vector2((float)_game.DragFrom.x * k, (float)_game.DragFrom.y * k);
                var b = new Vector2((float)_game.DragTo.x * k, (float)_game.DragTo.y * k);
                p.strokeColor = new Color(1f, 1f, 1f, 0.85f); p.lineWidth = 6f;
                Line(p, a, b);
                var d = b - a;
                if (d.sqrMagnitude > 100f)
                {
                    var n = d.normalized; var side = new Vector2(-n.y, n.x);
                    Poly(p, new[] { b + n * 16f, b - n * 6f + side * 12f, b - n * 6f - side * 12f }, new Color(1f, 1f, 1f, 0.9f));
                }
            }
        }

        private void PaintSparks(Painter2D p)
        {
            foreach (var s in _sparks)
            {
                var c = s.C; c.a = Mathf.Clamp01(s.Life / s.Max);
                Circle(p, s.P, s.Size, c);
            }
        }

        /// <summary>The playground's wooden picket fence across the park between the treeline and the hedge (decor only).</summary>
        private void PaintFence(Painter2D p, double fz)
        {
            var fd = Focal / Depth(0.5, fz);
            p.lineCap = LineCap.Round;
            p.strokeColor = FenceRim; p.lineWidth = Mathf.Max(3f, 0.1f * fd) + 2f;
            Line(p, Project(-10.0, 0.42, fz), Project(10.0, 0.42, fz));
            Line(p, Project(-10.0, 0.82, fz), Project(10.0, 0.82, fz));
            p.strokeColor = FenceWood; p.lineWidth = Mathf.Max(3f, 0.1f * fd);
            Line(p, Project(-10.0, 0.42, fz), Project(10.0, 0.42, fz));
            Line(p, Project(-10.0, 0.82, fz), Project(10.0, 0.82, fz));
            for (var i = -10; i <= 10; i++)
            {
                var a = Project(i, 0.0, fz); var b = Project(i, 1.05, fz);
                p.strokeColor = FenceRim; p.lineWidth = Mathf.Max(4f, 0.17f * fd) + 2f; Line(p, a, b);
                p.strokeColor = FenceWood; p.lineWidth = Mathf.Max(4f, 0.17f * fd); Line(p, a, b);
            }
        }

        // ── scenery (board 06, "Spielplatz im Park"): the soft sky with a sun and two drifting clouds and the hazy far
        //    hills sitting on the horizon — a layer behind the court canvas (which paints the lawn, trees, fence, hedge,
        //    crowd, court and hoop). Built once, pointer-transparent; the clouds drift (two translates a frame). ──

        private readonly List<VisualElement> _clouds = new List<VisualElement>();
        private float _sceneT;
        private static readonly float[] CloudW = { 110f, 76f };
        private static readonly float[] CloudTop = { 5f, 12f };
        private static readonly float[] CloudSpeed = { 6f, 9f };
        private static readonly float[] CloudPhase = { 60f, 250f };

        private void BuildScenery()
        {
            _clouds.Clear();
            var scene = MgScenery.Layer(Field, "mgf-basketbounce");
            // the canvas horizon sits at 0.5 - tan(14°)·0.95 ≈ 26.3 % of the field height
            const float horizonPct = 26.3f;
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, horizonPct + 2f);
            var sun = MgScenery.Sun(scene, 40f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, null, 4f, 8f);
            for (var i = 0; i < CloudW.Length; i++)
            {
                var cloud = MgScenery.Cloud(scene, CloudW[i]);
                cloud.name = "mgf-cloud-" + i;
                cloud.style.left = 0;
                MgScenery.Pct(cloud, null, CloudTop[i]);
                cloud.style.translate = new Translate(CloudPhase[i], 0);
                _clouds.Add(cloud);
            }
            var farC = MgScenery.Hex(0xBFE6CC); var farRim = MgScenery.Hex(0xD9F3E1);
            foreach (var (l, w, h) in new[] { (-14f, 200f, 70f), (36f, 240f, 90f), (72f, 170f, 60f) })
            {
                var hill = MgScenery.Hill(scene, w, h, farC, farRim);
                MgScenery.Pct(hill, l, null, null, 100f - horizonPct - 0.5f);
            }
        }

        private void DriftClouds()
        {
            if (FieldW <= 0f) return;
            var t = ReducedMotion ? 0f : _sceneT;
            for (var i = 0; i < _clouds.Count; i++)
                _clouds[i].style.translate = new Translate(Mathf.Repeat(CloudPhase[i] + t * CloudSpeed[i], FieldW + CloudW[i]) - CloudW[i], 0);
        }

        private void PaintHoop(Painter2D p, BasketBounce.Hoop hoop, BasketBounce.Tune tune)
        {
            var boardZ = hoop.Z - tune.BoardGap;
            var boardTop = tune.BoardBottomY + tune.BoardH;
            // pole behind the board, its foot on the court
            p.strokeColor = Pole; p.lineWidth = Mathf.Max(3f, 0.12f * Focal / Depth(1.5, boardZ - 0.3));
            Line(p, Project(hoop.X, 0.0, boardZ - 0.3), Project(hoop.X, boardTop - 0.3, boardZ - 0.3));
            Line(p, Project(hoop.X, boardTop - 0.3, boardZ - 0.3), Project(hoop.X, boardTop - 0.3, boardZ));
            // the board with its white target square
            var hw = tune.BoardW * 0.5;
            Poly(p, new[] { Project(hoop.X - hw, tune.BoardBottomY, boardZ), Project(hoop.X + hw, tune.BoardBottomY, boardZ), Project(hoop.X + hw, boardTop, boardZ), Project(hoop.X - hw, boardTop, boardZ) }, new Color(0.97f, 0.96f, 0.92f));
            p.strokeColor = new Color(0.86f, 0.36f, 0.3f); p.lineWidth = 3f;
            StrokePoly(p, new[] { Project(hoop.X - hw, tune.BoardBottomY, boardZ), Project(hoop.X + hw, tune.BoardBottomY, boardZ), Project(hoop.X + hw, boardTop, boardZ), Project(hoop.X - hw, boardTop, boardZ) });
            StrokePoly(p, new[] { Project(hoop.X - 0.3, tune.RimY, boardZ), Project(hoop.X + 0.3, tune.RimY, boardZ), Project(hoop.X + 0.3, tune.RimY + 0.45, boardZ), Project(hoop.X - 0.3, tune.RimY + 0.45, boardZ) });
            // the net: cords from the ring down to a narrower ring, pulsing wider after a basket
            var lowR = tune.RimR * 0.62 * (1.0 + _netPulse * 0.5);
            var lowY = tune.RimY - 0.48 - _netPulse * 0.1;
            p.strokeColor = new Color(1f, 1f, 1f, 0.9f); p.lineWidth = 2f;
            for (var i = 0; i < 12; i++)
            {
                var a = Math.PI * 2.0 * i / 12.0;
                var top = Project(hoop.X + Math.Cos(a) * tune.RimR, tune.RimY, hoop.Z + Math.Sin(a) * tune.RimR);
                var bottom = Project(hoop.X + Math.Cos(a + 0.26) * lowR, lowY, hoop.Z + Math.Sin(a + 0.26) * lowR);
                Line(p, top, bottom);
            }
            RingPath(p, hoop.X, lowY, hoop.Z, lowR); p.Stroke();
            RingPath(p, hoop.X, (tune.RimY + lowY) * 0.5, hoop.Z, (tune.RimR + lowR) * 0.5); p.Stroke();
            // the rim, flashing pale when the ball clangs off it
            p.strokeColor = Color.Lerp(Rim, Color.white, Mathf.Clamp01(_rimPulse / 0.28f) * 0.7f);
            p.lineWidth = Mathf.Max(3f, (float)(tune.RimTube * 2.4) * Focal / Depth(tune.RimY, hoop.Z));
            RingPath(p, hoop.X, tune.RimY, hoop.Z, tune.RimR); p.Stroke();
            // the bracket to the board
            Line(p, Project(hoop.X, tune.RimY, hoop.Z - tune.RimR), Project(hoop.X, tune.RimY, boardZ));
        }

        private void PaintBall(Painter2D p, (double x, double y, double z) ball)
        {
            var tune = _game.Tune;
            // blob shadow on the court, fainter and smaller the higher the ball
            var sh = Project(ball.x, 0.02, ball.z);
            var k = Mathf.Clamp((float)(1.4 - ball.y * 0.12), 0.5f, 1.4f);
            var rs = (float)tune.BallR * k * Focal / Depth(0.0, ball.z);
            Ellipse(p, sh, rs, rs * 0.4f, new Color(0f, 0f, 0f, Mathf.Clamp((float)(1.0 - ball.y / 5.0), 0.12f, 0.34f)));
            if (_game.Phase == BasketBounceGame.PhaseFly) PaintTrail(p);
            var at2 = Project(ball.x, ball.y, ball.z);
            var r = (float)tune.BallR * Focal / Depth(ball.y, ball.z);
            Circle(p, at2, r, BallColor);
            Circle(p, at2 + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.28f, new Color(1f, 0.75f, 0.45f, 0.7f));
            p.strokeColor = Seam; p.lineWidth = Mathf.Max(1.5f, r * 0.1f);
            var spin = _game.Phase == BasketBounceGame.PhaseFly ? (float)_game.Elapsed * 6f : 0f;
            p.BeginPath(); p.Arc(at2, r * 0.98f, Angle.Radians(spin + 0.3f), Angle.Radians(spin + 2.8f)); p.Stroke();
            p.BeginPath(); p.Arc(at2, r * 0.98f, Angle.Radians(spin + 3.4f), Angle.Radians(spin + 5.9f)); p.Stroke();
            p.BeginPath();
            for (var i = 0; i <= 10; i++)
            {
                var a = spin + Mathf.PI * i / 10f;
                var pt = at2 + new Vector2(Mathf.Cos(a) * r * 0.98f, Mathf.Sin(a) * r * 0.35f);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
        }

        /// <summary>The trail of the flight: fading dots along the last positions.</summary>
        private void PaintTrail(Painter2D p)
        {
            var n = _game.Trail.Count;
            for (var i = 0; i < n; i += 2)
            {
                var tp = _game.Trail[i];
                var at = Project(tp.x, tp.y, tp.z);
                var f = (i + 1f) / n;
                Circle(p, at, 3f + 4f * f, new Color(1f, 0.9f, 0.6f, 0.35f * f));
            }
        }

        private void RingPath(Painter2D p, double cx, double y, double cz, double r)
        {
            p.BeginPath();
            for (var i = 0; i <= 28; i++)
            {
                var a = Math.PI * 2.0 * i / 28.0;
                var at = Project(cx + Math.Cos(a) * r, y, cz + Math.Sin(a) * r);
                if (i == 0) p.MoveTo(at); else p.LineTo(at);
            }
            p.ClosePath();
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

        private void OnScored(int points, bool swish, bool bank)
        {
            var hoop = _game.HoopNow();
            var at = Project(hoop.X, _game.Tune.RimY - 0.3, hoop.Z);
            _netPulse = 0.45f; _crowdPulse = 1f; _goobyHop = 1f;
            for (var i = 0; i < 22; i += ParticleStep)
            {
                var a = i * Mathf.PI * 2f / 22f;
                var sp = 120f + (i * 37) % 90;
                _sparks.Add(new Spark { P = at, V = new Vector2(Mathf.Cos(a) * sp, Mathf.Sin(a) * sp - 120f), Life = 0.7f, Max = 0.7f, Size = 3f + (i % 3) * 2f, C = new Color(1f, 0.86f, 0.42f) });
            }
            var t = Host.Strings;
            FloatText(at, "+" + points, new Color(0.24f, 0.55f, 0.32f));
            if (swish && _game.SwishStreak >= 2) FloatText(at - new Vector2(0f, 44f), t.T("mg.game.streak", ("n", _game.SwishStreak)), new Color(0.95f, 0.45f, 0.7f));
            if (_game.OnFire) Banner(t.T("mg.basketBounce.on_fire"), 1.2);
            if (_stage != null) StageResolved(true, swish);
        }

        private void OnMissed()
        {
            var b = _game.Ball;
            var at = b != null ? Project(b.Px, Math.Max(0.05, b.Py), b.Pz) : Project(0.0, 0.0, 0.0);
            for (var i = 0; i < 10; i += ParticleStep)
            {
                var a = Mathf.PI + i * Mathf.PI / 10f;
                _sparks.Add(new Spark { P = at, V = new Vector2(Mathf.Cos(a) * 80f, Mathf.Sin(a) * 60f - 40f), Life = 0.55f, Max = 0.55f, Size = 5f + (i % 3) * 2f, C = new Color(0.85f, 0.76f, 0.62f, 0.85f) });
            }
            _shake = 0.08f;
            FloatText(at, Host.Strings.T("mg.basketBounce.miss"), new Color(0.8f, 0.35f, 0.3f));
            if (_stage != null) StageResolved(false, false);
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _sparks.Clear();
            _missPill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        /// <summary>The court is painted every frame with the Painter2D (perspective from a fixed camera behind the spot).</summary>
        private sealed class CourtCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public CourtCanvas()
            {
                pickingMode = PickingMode.Ignore;
                name = "court";
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
