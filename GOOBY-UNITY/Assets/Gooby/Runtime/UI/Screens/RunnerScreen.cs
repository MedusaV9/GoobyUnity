using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Gooby-Renner host on the shared minigame frame (port of runner.gd + its city corridor): landscape three-lane
    /// street, swipe sideways to change lane, up to jump, down to slide. Cones/boxes/barriers jump, scaffolds slide,
    /// cars dodge. Coins sit in the free lanes; mystery boxes grant magnet / ×2 / shield. First hit stumbles, second
    /// wipes out. Game feel after runner.gd — deliberately WITHOUT screen shake (motion comfort of the endless runs): chip
    /// / whoosh on lane, jump and slide, dust on touchdown, gold sparks + "+N" with a coin tone climbing the streak, a
    /// gold burst on each multiplier step, a blue burst + perfect tone for a mystery box, a red flash + dust + warn buzz
    /// on a stumble, a shield pop, a mint burst every 100 m. With a GPU the street is the 3D chase-camera stage of
    /// <c>RunnerScreen.Stage3D.cs</c>.
    /// </summary>
    public sealed partial class RunnerScreen : MinigameHostScreen
    {
        public const double ViewW = 844.0, ViewH = 390.0;

        private RunnerGame _game;
        private RoadCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel;
        private float _show;
        private bool _pressed;
        private Vector2 _from;
        private bool _wasJumping;

        private static readonly Color SkyTop = new Color(0.55f, 0.79f, 1f);
        private static readonly Color Asphalt = MgScenery.Hex(0xA9A6C2);
        private static readonly Color AsphaltDark = MgScenery.Hex(0x918DAF);
        private static readonly Color Line = new Color(0.95f, 0.9f, 0.55f, 0.85f);
        private static readonly Color House = new Color(0.93f, 0.82f, 0.7f);
        private static readonly Color Roof = MgScenery.Hex(0xF08A80);
        private static readonly Color CoinC = new Color(1f, 0.82f, 0.32f);
        private static readonly Color MysteryC = new Color(0.55f, 0.72f, 1f);

        public RunnerScreen(GameHost host) : base(host, "runner", "runner") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        /// <summary>The score ticks with every metre: a fanfare per 50, coins and pickups as hits.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Milestones;
        protected override Color BackdropColor => MgScenery.Hex(0xA9DCA0);
        protected override Color FieldColor => SkyTop;

        public RunnerGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new RunnerGame(Difficulty, seed);
            _game.CoinTaken += pts => { OnCoin(pts); StageCoin(); };
            _game.ComboUp += m => { Banner(Host.Strings.T("mg.runner.combo", ("mult", m)), 1.1); OnComboUp(m); StageMood("ecstatic"); };
            _game.MysteryTaken += kind => { Banner(Host.Strings.T("mg.runner." + kind), 1.3); OnMystery(); StageMood("ecstatic"); };
            _game.Stumbled += () => { Banner(Host.Strings.T("mg.runner.stumble", ("left", _game.LivesLeft)), 1.2); OnStumbled(); StageStumble(); };
            _game.ShieldPopped += () => { Banner(Host.Strings.T("mg.runner.shield_pop"), 1.2); OnShieldPopped(); };
            _game.Milestone += m => { Banner(Host.Strings.T("mg.runner.milestone", ("m", m)), 1.0); OnMilestone(); };
            _game.LaneChanged += () => PlaySfx("soft_chip", 3f, 0.6f);
            _game.Jumped += () => { PlaySfx("game_whoosh", 4f, 0.7f); StageJump(); };
            _game.Slid += OnSlid;
            _game.Finished_ += RoundOver;
            _wasJumping = false;
            Sfx.Preload("game_perfect", "game_whoosh", "soft_chip");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _pressed = true;
                _from = e.localPosition;
                Field.CapturePointer(e.pointerId);
            });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                if (!_pressed || _game == null) return;
                _pressed = false;
                var d = (Vector2)e.localPosition - _from;
                var scale = (float)(ViewW / Mathf.Max(1f, FieldW));
                _game.Swipe(d.x * scale, d.y * scale);
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _pressed = false);

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new RoadCanvas { Paint = Paint, name = "road" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(56);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            _statsPill = Ux.El("gb-card", "gb-row");
            _statsPill.name = "stats-pill";
            Ux.Abs(_statsPill, null, 58);
            _statsPill.style.left = Length.Percent(50);
            _statsPill.style.translate = new Translate(Length.Percent(-50), 0);
            _statsPill.style.alignItems = Align.Center;
            _statsPill.style.paddingTop = 6; _statsPill.style.paddingBottom = 6; _statsPill.style.paddingLeft = 14; _statsPill.style.paddingRight = 16;
            _statsPill.pickingMode = PickingMode.Ignore;
            _statsLabel = Ux.Text("", "gb-md", "gb-bold");
            _statsPill.Add(_statsLabel);
            Root.Add(_statsPill);

            ComboLabel.style.color = new Color(1f, 0.92f, 0.45f);
            Banner(t.T("mg.runner.intro"), RunnerGame.IntroSec + 0.7);
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _show += (float)dt;
            // touchdown after a jump: a dust puff at the feet (runner.gd _move_player, Reduced Motion skips it)
            var jumping = _game.JumpT >= 0.0;
            if (_wasJumping && !jumping && !_game.Finished && !ReducedMotion && FieldW > 1f) Burst(FeetAt(), Dust, 6);
            _wasJumping = jumping;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            if (_stage != null)
            {
                RenderStage();
                TimeLabel.text = t.T("mg.runner.distance", ("m", (int)g.Meters));
                ScoreLabel.text = g.Score.ToString();
                _statsLabel.text = t.T("mg.runner.stats", ("coins", g.Coins), ("left", g.LivesLeft));
                ComboLabel.text = g.ComboMult > 1 ? "×" + g.ComboMult : "";
                return;
            }
            var at = WorldToView(g.LaneX, g.PlayerY, 0.0);
            var squash = g.Sliding ? 0.55f : 1f;
            var blink = g.Invuln > 0.0 && (int)(g.Invuln * 12.0) % 2 == 1;
            _gooby.style.display = blink ? DisplayStyle.None : DisplayStyle.Flex;
            _gooby.style.left = at.x - 28f;
            _gooby.style.top = at.y - 52f * squash;
            _gooby.style.scale = new Scale(new Vector3(1f + (1f - squash) * 0.3f, squash, 1f));
            TimeLabel.text = t.T("mg.runner.distance", ("m", (int)g.Meters));
            ScoreLabel.text = g.Score.ToString();
            _statsLabel.text = t.T("mg.runner.stats", ("coins", g.Coins), ("left", g.LivesLeft));
            ComboLabel.text = g.ComboMult > 1 ? "×" + g.ComboMult : "";
            MoveScenery();
            _canvas.MarkDirtyRepaint();
        }

        /// <summary>z = 0 at the player, negative is ahead (SPAWN_Z = −88).</summary>
        private Vector2 WorldToView(double x, double y, double z)
        {
            var depth = Mathf.InverseLerp((float)RunnerGame.SpawnZ, (float)RunnerGame.DespawnZ, (float)z);
            var roadY0 = FieldH * 0.38f;
            var roadY1 = FieldH * 0.92f;
            var screenY = Mathf.Lerp(roadY0, roadY1, depth) - (float)y * FieldH * 0.12f;
            var half = Mathf.Lerp(FieldW * 0.12f, FieldW * 0.42f, depth);
            var screenX = FieldW * 0.5f + (float)(x / 1.1) * half * 0.55f;
            return new Vector2(screenX, screenY);
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            var g = _game;
            // the sky, clouds, hills and the meadow are the scenery layer behind the canvas; the verge flowers stream past

            var far = new Vector2(FieldW * 0.5f, FieldH * 0.38f);
            var nearL = new Vector2(FieldW * 0.08f, FieldH);
            var nearR = new Vector2(FieldW * 0.92f, FieldH);
            Poly(p, new[] { far + new Vector2(-18f, 0f), far + new Vector2(18f, 0f), nearR, nearL }, Asphalt);
            p.strokeColor = Line; p.lineWidth = 3f;
            Dash(p, far, new Vector2(FieldW * 0.5f, FieldH), 18f);
            for (var i = 0; i < 8; i++)
            {
                var t = (i / 8f + (_show * 0.15f + (float)g.Meters * 0.004f)) % 1f;
                var y = Mathf.Lerp(FieldH * 0.4f, FieldH * 0.95f, t);
                var w = Mathf.Lerp(10f, 90f, t);
                Poly(p, Rect(new Vector2(FieldW * 0.5f, y), w, 3f), AsphaltDark);
            }

            DrawVerge(p, -1);
            DrawVerge(p, 1);
            DrawHouses(p, -1);
            DrawHouses(p, 1);

            foreach (var ob in g.Obstacles)
            {
                if (ob.Z < RunnerGame.SpawnZ + 4.0 || ob.Z > RunnerGame.DespawnZ) continue;
                DrawObstacle(p, ob);
            }
            foreach (var c in g.CoinsOnRoad)
            {
                if (c.Z < RunnerGame.SpawnZ + 4.0 || c.Z > RunnerGame.DespawnZ) continue;
                var at = WorldToView(g.Tune.LaneX[c.Lane], c.Y, c.Z);
                var d = Depth(c.Z);
                Ellipse(p, at, 6f * d, 7f * d, CoinC);
            }
            foreach (var m in g.Mysteries)
            {
                if (m.Z < RunnerGame.SpawnZ + 4.0 || m.Z > RunnerGame.DespawnZ) continue;
                var at = WorldToView(g.Tune.LaneX[m.Lane], 0.7, m.Z);
                var d = Depth(m.Z);
                Poly(p, Rect(at, 8f * d, 8f * d), MysteryC);
                Ellipse(p, at + new Vector2(0f, -8f * d), 5f * d, 3f * d, new Color(1f, 0.85f, 0.4f));
            }

            if (g.MagnetT > 0.0)
            {
                var at = WorldToView(g.LaneX, g.PlayerY, 0.0);
                p.strokeColor = new Color(0.42f, 0.8f, 1f, 0.5f); p.lineWidth = 3f;
                EllipseStroke(p, at, 28f, 16f);
            }
        }

        private float Depth(double z) => Mathf.Lerp(0.35f, 1.15f, Mathf.InverseLerp((float)RunnerGame.SpawnZ, (float)RunnerGame.DespawnZ, (float)z));

        // ── scenery (board 06, the bunnyHop meadow idea in depth): a soft sky with a sun and two drifting clouds, two rows
        //    of round hills with a tree on the horizon (38 %), the meadow under the road (a layer behind the road
        //    canvas, built once, pointer-transparent) — the far hills slide a little against the near ones as the run
        //    goes on (parallax, two translates) and tufts + dot flowers stream past on both verges (painted with the
        //    houses). Still with reduced motion. ──

        private readonly List<VisualElement> _clouds = new List<VisualElement>();
        private VisualElement _farHills, _nearHills;
        private static readonly float[] CloudW = { 100f, 70f };
        private static readonly float[] CloudTop = { 6f, 14f };
        private static readonly float[] CloudSpeed = { 5f, 8f };
        private static readonly float[] CloudPhase = { 30f, 230f };
        private static readonly Color[] VergePetals = { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFFD166), MgScenery.Hex(0xB7D8FF) };

        private void BuildScenery()
        {
            _clouds.Clear();
            var scene = MgScenery.Layer(Field, "mgf-runner");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 39f);
            var sun = MgScenery.Sun(scene, 42f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, 8f, 6f);
            for (var i = 0; i < CloudW.Length; i++)
            {
                var cloud = MgScenery.Cloud(scene, CloudW[i]);
                cloud.name = "mgf-cloud-" + i;
                cloud.style.left = 0;
                MgScenery.Pct(cloud, null, CloudTop[i]);
                cloud.style.translate = new Translate(CloudPhase[i], 0);
                _clouds.Add(cloud);
            }
            _farHills = MgScenery.Group(scene, "gb-mgf-strip");
            MgScenery.Pct(_farHills, -20f, 0, -20f, 62f);
            foreach (var (l, w, h) in new[] { (-4f, 200f, 80f), (30f, 230f, 100f), (62f, 190f, 76f), (88f, 200f, 90f) })
            {
                var hill = MgScenery.Hill(_farHills, w, h, MgScenery.Hex(0xBFE6CC), MgScenery.Hex(0xD9F3E1));
                MgScenery.Pct(hill, l, null, null, 0);
            }
            _nearHills = MgScenery.Group(scene, "gb-mgf-strip");
            MgScenery.Pct(_nearHills, -20f, 0, -20f, 62f);
            foreach (var (l, w, h) in new[] { (0f, 220f, 52f), (58f, 240f, 60f) })
            {
                var hill = MgScenery.Hill(_nearHills, w, h, MgScenery.Hex(0xA6DB98), MgScenery.Hex(0xC6F39A));
                MgScenery.Pct(hill, l, null, null, 0);
            }
            var tree = MgScenery.Sprite(_nearHills, "plant_tree_round", 50f, 66f);
            MgScenery.Pct(tree, 20f, null, null, 1.5f);
            var meadow = MgScenery.Gradient(scene, MgScenery.Hex(0xB2DFA2), MgScenery.Hex(0x8FCB86), 38f, 100f);
            meadow.name = "mgf-meadow";
            var rim = MgScenery.Shape(scene, MgScenery.Hex(0x9ED499), 0f);
            MgScenery.Pct(rim, 0, 37.6f, 0); rim.style.height = 4f;
        }

        private void MoveScenery()
        {
            if (FieldW <= 0f || _farHills == null) return;
            var t = ReducedMotion ? 0f : _show;
            for (var i = 0; i < _clouds.Count; i++)
                _clouds[i].style.translate = new Translate(Mathf.Repeat(CloudPhase[i] + t * CloudSpeed[i], FieldW + CloudW[i]) - CloudW[i], 0);
            var run = ReducedMotion ? 0f : (float)_game.Meters;
            _farHills.style.translate = new Translate(Mathf.Sin(run * 0.004f) * FieldW * 0.06f, 0);
            _nearHills.style.translate = new Translate(Mathf.Sin(run * 0.004f) * FieldW * 0.12f, 0);
        }

        /// <summary>Tufts and dot flowers on the verges, streaming towards the camera just outside the road edge.</summary>
        private void DrawVerge(Painter2D p, int side)
        {
            for (var i = 0; i < 8; i++)
            {
                var t = (i / 8f + 0.06f + (float)(_game.Meters * 0.008 + _show * 0.02)) % 1f;
                var y = Mathf.Lerp(FieldH * 0.4f, FieldH * 0.99f, t);
                var s = Mathf.Lerp(0.3f, 1.2f, t);
                var roadHalf = 18f + (FieldW * 0.42f - 18f) * ((y - FieldH * 0.38f) / (FieldH * 0.62f));
                var x = FieldW * 0.5f + side * (roadHalf + 10f * s + (i % 3) * 7f * s);
                if (i % 2 == 0)
                {
                    p.strokeColor = MgScenery.Hex(0x6DBE78); p.lineWidth = 3f * s; p.lineCap = LineCap.Round;
                    for (var b = -1; b <= 1; b++) { p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + b * 5f * s, y - (b == 0 ? 14f : 10f) * s)); p.Stroke(); }
                }
                else
                {
                    Ellipse(p, new Vector2(x, y - 6f * s), 5f * s, 5f * s, VergePetals[(i / 2 + (side > 0 ? 1 : 0)) % VergePetals.Length]);
                    Ellipse(p, new Vector2(x, y - 6f * s), 1.8f * s, 1.8f * s, MgScenery.Hex(0xFFD166));
                }
            }
        }

        private void DrawHouses(Painter2D p, int side)
        {
            for (var i = 0; i < 6; i++)
            {
                var t = (i / 6f + (float)(_game.Meters * 0.008 + _show * 0.02)) % 1f;
                var y = Mathf.Lerp(FieldH * 0.36f, FieldH * 0.78f, t);
                var s = Mathf.Lerp(0.25f, 1.1f, t);
                var x = FieldW * 0.5f + side * Mathf.Lerp(FieldW * 0.16f, FieldW * 0.44f, t);
                var w = 28f * s; var h = 36f * s;
                Poly(p, Rect(new Vector2(x, y - h * 0.3f), w * 0.5f, h * 0.5f), Color.Lerp(House, new Color(0.85f, 0.72f, 0.62f), (i % 3) / 3f));
                Poly(p, new[] {
                    new Vector2(x - w * 0.55f, y - h * 0.8f), new Vector2(x + w * 0.55f, y - h * 0.8f),
                    new Vector2(x, y - h * 1.15f),
                }, Roof);
            }
        }

        private void DrawObstacle(Painter2D p, RunnerGame.Obstacle ob)
        {
            var at = WorldToView(_game.Tune.LaneX[ob.Lane], 0.0, ob.Z);
            var d = Depth(ob.Z);
            Color col;
            float hx, hy;
            switch (ob.Kind)
            {
                case "cone": col = new Color(1f, 0.55f, 0.2f); hx = 7f * d; hy = 12f * d; break;
                case "box": col = new Color(0.72f, 0.55f, 0.32f); hx = 11f * d; hy = 10f * d; break;
                case "barrier": col = new Color(0.95f, 0.85f, 0.35f); hx = 16f * d; hy = 8f * d; break;
                case "overhead": col = new Color(0.55f, 0.58f, 0.62f); hx = 18f * d; hy = 6f * d;
                    Poly(p, Rect(at + new Vector2(0f, -22f * d), hx, hy), col);
                    Poly(p, Rect(at + new Vector2(-14f * d, -8f * d), 3f * d, 16f * d), col);
                    Poly(p, Rect(at + new Vector2(14f * d, -8f * d), 3f * d, 16f * d), col);
                    return;
                default: col = new Color(0.85f, 0.25f, 0.28f); hx = 16f * d; hy = 10f * d; break; // car
            }
            if (ob.Kind == "cone")
            {
                Poly(p, new[] { at + new Vector2(-hx, hy * 0.4f), at + new Vector2(hx, hy * 0.4f), at + new Vector2(0f, -hy) }, col);
                return;
            }
            Poly(p, Rect(at, hx, hy), col);
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

        private void Dash(Painter2D p, Vector2 a, Vector2 b, float period)
        {
            var d = b - a; var len = d.magnitude;
            if (len < 1f) return;
            var n = d / len;
            var off = ((float)_game.Meters * 8f) % (period * 2f);
            for (var s = -off; s < len; s += period * 2f)
            {
                var p0 = a + n * Mathf.Max(0f, s);
                var p1 = a + n * Mathf.Min(len, s + period);
                if (p1.y <= p0.y) continue;
                p.BeginPath(); p.MoveTo(p0); p.LineTo(p1); p.Stroke();
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

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i <= 14; i++)
            {
                var a = Mathf.PI * 2f * i / 14f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Fill();
        }

        private static void EllipseStroke(Painter2D p, Vector2 c, float rx, float ry)
        {
            p.BeginPath();
            for (var i = 0; i <= 14; i++)
            {
                var a = Mathf.PI * 2f * i / 14f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Stroke();
        }

        // ── juice (runner.gd _take_coin / _collect_mystery / _on_hit / _milestone — no shake, see the class summary) ──

        private static readonly Color Dust = new Color(0.72f, 0.68f, 0.6f);
        private static readonly Color HitC = new Color(1f, 0.36f, 0.28f);
        private static readonly Color ShieldC = new Color(0.55f, 0.85f, 1f);
        private static readonly Color MileC = new Color(0.7f, 1f, 0.8f);

        private Vector2 FeetAt() => WorldToView(_game.LaneX, 0.0, 0.0);
        private Vector2 BodyAt() => WorldToView(_game.LaneX, _game.PlayerY, 0.0) + new Vector2(0f, -30f);

        private void OnSlid()
        {
            PlaySfx("game_whoosh", -4f, 0.7f);
            if (FieldW > 1f) Burst(FeetAt(), Dust, 5);
        }

        private void OnCoin(int points)
        {
            PlaySfx("game_coin", Sfx.ComboSemitones(_game.CoinStreak), 0.8f);
            if (FieldW <= 1f) return;
            var at = BodyAt();
            Burst(at, CoinC, 6);
            Float(at + new Vector2(0f, -24f), "+" + points, CoinC, 22f);
        }

        /// <summary>Multiplier step: gold burst + tap (the frame's streak line already pops and plays the combo ladder).</summary>
        private void OnComboUp(int mult)
        {
            Haptics.Tap();
            if (FieldW <= 1f) return;
            Burst(BodyAt(), new Color(1f, 0.85f, 0.4f), 16);
        }

        private void OnMystery()
        {
            PlaySfx("game_perfect");
            Haptics.Tap();
            if (FieldW <= 1f) return;
            var at = BodyAt();
            Burst(at, MysteryC, 18);
            Burst(at, CoinC, 8);
            Flash(new Color(0.55f, 0.72f, 1f, 0.12f), 0.16f);
            ScalePop(_statsPill, 1.2f);
        }

        /// <summary>Stumble or wipe-out: red flash + dust + miss tone + warn buzz (never a shake in this host).</summary>
        private void OnStumbled()
        {
            PlaySfx("game_miss");
            Haptics.Warn();
            Flash(new Color(0.9f, 0.32f, 0.22f, 0.16f), 0.18f);
            if (FieldW <= 1f) return;
            Burst(FeetAt() + new Vector2(0f, -10f), Dust, 10);
            Burst(BodyAt(), HitC, 6);
            ScalePop(_statsPill, 0.85f);
        }

        private void OnShieldPopped()
        {
            PlaySfx("game_pop", 2f);
            Haptics.Tap();
            if (FieldW > 1f) Burst(BodyAt(), ShieldC, 14);
        }

        private void OnMilestone()
        {
            PlaySfx("game_perfect", 2f, 0.85f);
            PlaySfx("game_whoosh", 2f, 0.7f);
            if (FieldW > 1f) Burst(BodyAt() + new Vector2(0f, -20f), MileC, 12);
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            _game = null;
            _statsPill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class RoadCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public RoadCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
