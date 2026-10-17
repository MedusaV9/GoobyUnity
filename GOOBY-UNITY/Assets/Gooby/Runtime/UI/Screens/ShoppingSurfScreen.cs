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
    /// Shopping-Surf (shoppingSurf) host on the shared minigame frame — port of shopping_surf.gd as a pastel shopping
    /// street running to the horizon: three lanes on warm asphalt between cream sidewalks and shop fronts with striped
    /// awnings and lanterns, Gooby running towards the viewer's back. Swipe sideways to change lanes, up to jump, down
    /// to slide (in the air: fast drop). Shopping carts roll towards Gooby (a "!" warns), crates block a lane, shoppers
    /// cross, awnings must be slid under, puddles slow, curb gaps need a jump. Coins (+1, ×2), near misses (+2), power-ups
    /// (magnet, ×2, shield, turbo carrot). Game feel after shopping_surf.gd: coin pops with a climbing tone, a "Knapp!"
    /// pop, a red flash and wobble on a crash, power-up banners — no screen shake.
    /// </summary>
    public sealed partial class ShoppingSurfScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private ShoppingSurfGame _game;
        private SurfCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel;
        private Vector2 _from;
        private bool _swipeLive;
        private double _scroll, _crashWobble;
        private int _shownKey = -1;

        private const float HorizonPct = 0.3f, PlayerPct = 0.8f, DepthK = 6f;

        private static readonly Color Asphalt = MgScenery.Hex(0xC9C1C8);
        private static readonly Color AsphaltFar = MgScenery.Hex(0xDCD6DB);
        private static readonly Color Sidewalk = MgScenery.Hex(0xF6EBDD);
        private static readonly Color Curb = MgScenery.Hex(0xE2D2C0);
        private static readonly Color LaneMark = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color[] Facades = { MgScenery.Hex(0xFFD8C2), MgScenery.Hex(0xD3E9FF), MgScenery.Hex(0xFFF0B8), MgScenery.Hex(0xD9F0D2), MgScenery.Hex(0xF6D5EC) };
        private static readonly Color[] AwningC = { MgScenery.Hex(0xF26D6D), MgScenery.Hex(0x6FA8F0), MgScenery.Hex(0x7CCB86), MgScenery.Hex(0xF6B13F), MgScenery.Hex(0xC98BE0) };
        private static readonly Color Coin = new Color(1f, 0.81f, 0.27f);
        private static readonly Color CoinRim = new Color(0.9f, 0.62f, 0.15f);
        private static readonly Color Shadow = new Color(0.25f, 0.2f, 0.25f, 0.2f);
        private static readonly Color CrateC = MgScenery.Hex(0xD9A066);
        private static readonly Color CrateDark = MgScenery.Hex(0xB57E45);
        private static readonly Color Metal = MgScenery.Hex(0x9AA6B8);
        private static readonly Color Puddle = new Color(0.55f, 0.75f, 0.95f, 0.75f);

        public ShoppingSurfScreen(GameHost host) : base(host, "shoppingSurf", "shoppingSurf") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE1F6);
        protected override Color FieldColor => MgScenery.Hex(0xCDE8F8);
        /// <summary>Points tick with every metre: fanfares per milestone, big jumps (power-up coins) as hits.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Milestones;
        protected override int ScoreMilestone => 250;
        protected override bool RoundEndedByClock => false;

        public ShoppingSurfGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new ShoppingSurfGame(Difficulty, seed);
            _game.Event += OnEvent;
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_coin");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _from = e.localPosition;
                _swipeLive = true;
                Field.CapturePointer(e.pointerId);
            });
            Field.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_swipeLive) return;
                var delta = (Vector2)e.localPosition - _from;
                if (delta.magnitude >= 42f) { _swipeLive = false; Resolve(delta); }
            });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                if (_swipeLive) { _swipeLive = false; Resolve((Vector2)e.localPosition - _from); }
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _swipeLive = false);

            if (World.MgStage3D.Supported) BuildStage();
            else
            {
                var scene = MgScenery.Layer(Field, "mgf-shoppingsurf");
                MgScenery.Gradient(scene, MgScenery.Hex(0x9FD3F5), MgScenery.Hex(0xFFF1E3), 0f, HorizonPct * 100f + 1f);
                var sun = MgScenery.Sun(scene, 36f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
                MgScenery.Pct(sun, 70f, 5f);
                foreach (var (l, top, w) in new[] { (8f, 6f, 90f), (52f, 12f, 70f) })
                    MgScenery.Pct(MgScenery.Cloud(scene, w), l, top);
            }

            _canvas = new SurfCanvas { Paint = Paint, name = "street" };
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
            _statsLabel.name = "surf-stats";
            _statsPill.Add(_statsLabel);
            Root.Add(_statsPill);

            Banner(t.T("mg.shoppingSurf.intro"), ShoppingSurfGame.IntroSec + 1.0);
        }

        private void Resolve(Vector2 delta)
        {
            if (delta.magnitude < 28f || _game == null) return;
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) _game.Swipe(delta.x > 0f ? "right" : "left");
            else _game.Swipe(delta.y < 0f ? "up" : "down");
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _scroll += _game.Run.Speed * dt;
            _crashWobble = Math.Max(0.0, _crashWobble - dt * 2.5);
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var run = g.Run;
            var px = ShoppingSurfRun.PlayerX(run);
            var py = ShoppingSurfRun.PlayerY(run);
            var at = Project(px, py + 1.0, 0.0);
            var sliding = run.SlideT >= 0.0;
            var size = sliding ? 44f : 56f;
            _gooby.style.width = size; _gooby.style.height = size;
            _gooby.style.left = at.x - size * 0.5f + Mathf.Sin((float)_crashWobble * 30f) * 8f * (float)_crashWobble;
            _gooby.style.top = at.y - size * (sliding ? 0.2f : 0.6f);
            var left = Math.Max(0, g.Tune.ArcadeMaxCrashes - run.Crashes);
            var key = run.Coins * 10000 + left * 1000 + run.NearMisses;
            if (key != _shownKey)
            {
                _shownKey = key;
                _statsLabel.text = t.T("mg.shoppingSurf.stats", ("coins", run.Coins), ("left", left), ("near", run.NearMisses));
            }
            TimeLabel.text = t.T("mg.shoppingSurf.distance", ("m", Mathf.FloorToInt((float)run.DistanceM)));
            ScoreLabel.text = g.Score.ToString();
            ComboLabel.text = run.NearStreak >= 2 ? "×" + run.NearStreak : "";
            if (_stage != null) { RenderStage(); return; }
            _canvas.MarkDirtyRepaint();
        }

        // ── projection: distance ahead recedes to the horizon; x and heights shrink with the same factor ──

        private float Factor(double z) => DepthK / (DepthK + Mathf.Max(-2.5f, (float)-z));
        private float Horizon => FieldH * HorizonPct;
        private float PlayerY => FieldH * PlayerPct;
        private float PxPerM => FieldW / 6.4f;

        private Vector2 Project(double x, double y, double z)
        {
            if (_stage != null) return _stage.ToField(S(x, y, z), FieldW, FieldH);
            var f = Factor(z);
            return new Vector2(FieldW * 0.5f + (float)x * PxPerM * f, Horizon + (PlayerY - Horizon) * f - (float)y * PxPerM * f);
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            var run = _game.Run;
            var tune = _game.Tune;
            const double far = -72.0, near = 3.0;
            const double half = 2.6, walk = 4.4;

            // street bands far → near, sidewalks on both sides, curbs
            const int bands = 12;
            for (var i = 0; i < bands; i++)
            {
                var z0 = far + (near - far) * i / bands;
                var z1 = far + (near - far) * (i + 1) / bands;
                var c = Color.Lerp(AsphaltFar, Asphalt, (i + 0.5f) / bands);
                Poly(p, new[] { Project(-half, 0, z0), Project(half, 0, z0), Project(half, 0, z1), Project(-half, 0, z1) }, c);
                Poly(p, new[] { Project(-walk, 0, z0), Project(-half, 0, z0), Project(-half, 0, z1), Project(-walk, 0, z1) }, Sidewalk);
                Poly(p, new[] { Project(half, 0, z0), Project(walk, 0, z0), Project(walk, 0, z1), Project(half, 0, z1) }, Sidewalk);
            }
            Poly(p, new[] { Project(-half - 0.12, 0, far), Project(-half, 0, far), Project(-half, 0, near), Project(-half - 0.12, 0, near) }, Curb);
            Poly(p, new[] { Project(half, 0, far), Project(half + 0.12, 0, far), Project(half + 0.12, 0, near), Project(half, 0, near) }, Curb);
            // dashed lane markings streaming towards the viewer
            foreach (var lx in new[] { -0.8, 0.8 })
                for (var k = 0; k < 18; k++)
                {
                    var z = -Mod(k * 4.2 - _scroll, 75.6) + 3.0;
                    if (z < far || z > near) continue;
                    Poly(p, new[] { Project(lx - 0.05, 0, z - 0.9), Project(lx + 0.05, 0, z - 0.9), Project(lx + 0.05, 0, z), Project(lx - 0.05, 0, z) }, LaneMark);
                }

            // shop fronts with striped awnings and lanterns on both sides, far first
            for (var side = -1; side <= 1; side += 2)
                for (var k = 9; k >= 0; k--)
                {
                    var z = -Mod(k * 7.5 - _scroll + (side > 0 ? 3.7 : 0.0), 75.0) + 3.0;
                    if (z < far || z > near + 1.0) continue;
                    var idx = (int)Math.Floor((_scroll + k * 7.5) / 7.5) + (side > 0 ? 2 : 0);
                    var v = ((idx % Facades.Length) + Facades.Length) % Facades.Length;
                    var xIn = side * walk;
                    var xOut = side * (walk + 0.1);
                    var a = Project(xIn, 0, z); var b = Project(xIn, 0, z - 6.8);
                    var h = 4.2 + (idx & 1) * 0.8;
                    var aT = Project(xIn, h, z); var bT = Project(xIn, h, z - 6.8);
                    Poly(p, new[] { a, b, bT, aT }, Facades[v]);
                    // shop window and door
                    Poly(p, new[] { Project(xIn, 0.6, z - 1.0), Project(xIn, 0.6, z - 4.2), Project(xIn, 2.2, z - 4.2), Project(xIn, 2.2, z - 1.0) }, new Color(0.78f, 0.9f, 1f));
                    Poly(p, new[] { Project(xIn, 0.0, z - 5.0), Project(xIn, 0.0, z - 6.0), Project(xIn, 2.1, z - 6.0), Project(xIn, 2.1, z - 5.0) }, MgScenery.Hex(0xB07A55));
                    // striped awning sticking out over the sidewalk
                    for (var s = 0; s < 6; s++)
                    {
                        var za = z - 0.4 - s * 1.0; var zb = za - 1.0;
                        Poly(p, new[] { Project(xIn, 2.6, za), Project(xIn, 2.6, zb), Project(xIn - side * 1.1, 2.2, zb), Project(xIn - side * 1.1, 2.2, za) }, s % 2 == 0 ? AwningC[v] : Color.white);
                    }
                    _ = xOut;
                }

            // obstacles, coins and power-ups sorted far → near
            var draw = new List<(double z, Action act)>();
            // things that already passed Gooby leave the picture quickly (the perspective would blow them up behind him)
            foreach (var ob in run.Obstacles) { var o = ob; if (o.Z > far && o.Z < 1.2) draw.Add((o.Z, () => PaintObstacle(p, o))); }
            foreach (var c in run.CoinItems) { var ci = c; if (ci.Z > far && ci.Z < 0.5) draw.Add((ci.Z, () => PaintCoin(p, ci))); }
            foreach (var pu in run.PowerupItems) { var pi = pu; if (pi.Z > far && pi.Z < 0.5) draw.Add((pi.Z, () => PaintPowerup(p, pi))); }
            draw.Sort((x, y) => x.z.CompareTo(y.z));
            foreach (var d in draw) d.act();

            PaintRunner(p, run);
        }

        private void PaintObstacle(Painter2D p, ShoppingSurfRun.Obstacle ob)
        {
            var f = Factor(ob.Z);
            var basePt = Project(ob.X, 0, ob.Z);
            switch (ob.Kind)
            {
                case "cart":
                {
                    var w = 0.55; var h = 0.95;
                    Ellipse(p, basePt + new Vector2(0f, 2f), (float)(w * PxPerM * f), 5f * f, Shadow);
                    Poly(p, new[] { Project(ob.X - w, 0.35, ob.Z), Project(ob.X + w, 0.35, ob.Z), Project(ob.X + w * 1.1, h, ob.Z), Project(ob.X - w * 1.1, h, ob.Z) }, Metal);
                    for (var i = 1; i < 4; i++) Poly(p, new[] { Project(ob.X - w + i * w * 0.5 - 0.03, 0.4, ob.Z), Project(ob.X - w + i * w * 0.5 + 0.03, 0.4, ob.Z), Project(ob.X - w + i * w * 0.5 + 0.03, h - 0.05, ob.Z), Project(ob.X - w + i * w * 0.5 - 0.03, h - 0.05, ob.Z) }, Color.white);
                    Poly(p, new[] { Project(ob.X - w * 1.15, h, ob.Z), Project(ob.X + w * 1.15, h, ob.Z), Project(ob.X + w * 1.15, h + 0.08, ob.Z), Project(ob.X - w * 1.15, h + 0.08, ob.Z) }, MgScenery.Hex(0xF26D6D));
                    foreach (var wx in new[] { -w * 0.8, w * 0.8 }) Ellipse(p, Project(ob.X + wx, 0.1, ob.Z), 4f * f * 1.4f, 4f * f * 1.4f, MgScenery.Hex(0x4A4048));
                    if (ob.Telegraphed && ob.Z < -1.0)
                    {
                        var warn = Project(ob.X, h + 0.7, ob.Z);
                        Ellipse(p, warn, 12f * Mathf.Max(0.6f, f), 12f * Mathf.Max(0.6f, f), MgScenery.Hex(0xFFD166));
                        Poly(p, Rect(warn + new Vector2(0f, -2f), 2f, 6f), MgScenery.Hex(0x7A4A2A));
                        Poly(p, Rect(warn + new Vector2(0f, 6f), 2f, 2f), MgScenery.Hex(0x7A4A2A));
                    }
                    break;
                }
                case "crate":
                {
                    var w = 0.6; var h = 1.0;
                    Ellipse(p, basePt + new Vector2(0f, 2f), (float)(w * PxPerM * f), 5f * f, Shadow);
                    Poly(p, new[] { Project(ob.X - w, 0, ob.Z), Project(ob.X + w, 0, ob.Z), Project(ob.X + w, h, ob.Z), Project(ob.X - w, h, ob.Z) }, CrateC);
                    Poly(p, new[] { Project(ob.X - w, h * 0.45, ob.Z), Project(ob.X + w, h * 0.45, ob.Z), Project(ob.X + w, h * 0.55, ob.Z), Project(ob.X - w, h * 0.55, ob.Z) }, CrateDark);
                    Poly(p, new[] { Project(ob.X - w, h - 0.08, ob.Z), Project(ob.X + w, h - 0.08, ob.Z), Project(ob.X + w, h, ob.Z), Project(ob.X - w, h, ob.Z) }, CrateDark);
                    break;
                }
                case "npc":
                {
                    var bob = Mathf.Sin((float)(_scroll * 2.0 + ob.Id)) * 0.04f;
                    Ellipse(p, basePt + new Vector2(0f, 2f), 14f * f * 1.4f, 4f * f, Shadow);
                    Poly(p, new[] { Project(ob.X - 0.3, 0.1, ob.Z), Project(ob.X + 0.3, 0.1, ob.Z), Project(ob.X + 0.22, 1.05 + bob, ob.Z), Project(ob.X - 0.22, 1.05 + bob, ob.Z) }, AwningC[ob.Id % AwningC.Length]);
                    var head = Project(ob.X, 1.3 + bob, ob.Z);
                    var r = (float)(0.22 * PxPerM * f);
                    Ellipse(p, head, r, r, MgScenery.Hex(0xFFE0C2));
                    Ellipse(p, head + new Vector2(-r * 0.2f, -r * 0.5f), r * 0.9f, r * 0.45f, MgScenery.Hex(0x8A5A3C));
                    Poly(p, new[] { Project(ob.X + 0.32, 0.5, ob.Z), Project(ob.X + 0.55, 0.5, ob.Z), Project(ob.X + 0.55, 0.85, ob.Z), Project(ob.X + 0.32, 0.85, ob.Z) }, MgScenery.Hex(0xF6E2C0));
                    break;
                }
                case "awning":
                {
                    var hw = ob.HalfW;
                    var y0 = ob.Def.GapY + 0.05; var y1 = y0 + 0.45;
                    foreach (var px in new[] { ob.X - hw, ob.X + hw })
                        Poly(p, new[] { Project(px - 0.05, 0, ob.Z), Project(px + 0.05, 0, ob.Z), Project(px + 0.05, y0, ob.Z), Project(px - 0.05, y0, ob.Z) }, Metal);
                    const int stripes = 8;
                    for (var s = 0; s < stripes; s++)
                    {
                        var xa = ob.X - hw + 2 * hw * s / stripes; var xb = ob.X - hw + 2 * hw * (s + 1) / stripes;
                        Poly(p, new[] { Project(xa, y0, ob.Z), Project(xb, y0, ob.Z), Project(xb, y1, ob.Z), Project(xa, y1, ob.Z) }, s % 2 == 0 ? AwningC[0] : Color.white);
                    }
                    for (var s = 0; s < stripes; s++)
                    {
                        var xm = ob.X - hw + 2 * hw * (s + 0.5) / stripes;
                        Ellipse(p, Project(xm, y0, ob.Z), (float)(hw / stripes * PxPerM * f), 4f * f, s % 2 == 0 ? AwningC[0] : Color.white);
                    }
                    break;
                }
                case "puddle":
                    Ellipse(p, basePt, (float)(0.65 * PxPerM * f), (float)(0.22 * PxPerM * f), Puddle);
                    Ellipse(p, basePt + new Vector2(-6f * f, -2f * f), (float)(0.25 * PxPerM * f), (float)(0.06 * PxPerM * f), new Color(1f, 1f, 1f, 0.6f));
                    break;
                case "gap":
                {
                    var d = ob.Def.HalfDepth;
                    Poly(p, new[] { Project(-2.6, 0, ob.Z + d), Project(2.6, 0, ob.Z + d), Project(2.6, 0, ob.Z - d), Project(-2.6, 0, ob.Z - d) }, MgScenery.Hex(0x5A4A55));
                    Poly(p, new[] { Project(-2.6, 0, ob.Z - d), Project(2.6, 0, ob.Z - d), Project(2.6, -0.4, ob.Z - d), Project(-2.6, -0.4, ob.Z - d) }, MgScenery.Hex(0x3E3239));
                    break;
                }
            }
        }

        private void PaintCoin(Painter2D p, ShoppingSurfRun.CoinItem c)
        {
            var f = Factor(c.Z);
            var at = Project(c.X, c.Y, c.Z);
            var r = Mathf.Max(3f, 0.28f * PxPerM * f);
            var spin = Mathf.Abs(Mathf.Cos((float)(_scroll * 0.8 + c.Id)));
            Ellipse(p, at, r * (0.35f + 0.65f * spin), r, CoinRim);
            Ellipse(p, at + new Vector2(0f, -1f), r * (0.25f + 0.55f * spin), r * 0.8f, Coin);
        }

        private void PaintPowerup(Painter2D p, ShoppingSurfRun.PowerupItem pu)
        {
            var f = Factor(pu.Z);
            var bob = Mathf.Sin((float)_scroll * 1.5f + pu.Id) * 0.1f;
            var at = Project(pu.X, 0.9 + bob, pu.Z);
            var r = Mathf.Max(5f, 0.38f * PxPerM * f);
            var c = pu.Kind == "magnet" ? MgScenery.Hex(0xF26D6D) : pu.Kind == "x2" ? MgScenery.Hex(0xFFD166) : pu.Kind == "shield" ? MgScenery.Hex(0x6FB7F0) : MgScenery.Hex(0xFF9F43);
            Ellipse(p, at, r * 1.25f, r * 1.25f, new Color(c.r, c.g, c.b, 0.28f));
            Ellipse(p, at, r, r, c);
            Ellipse(p, at + new Vector2(-r * 0.3f, -r * 0.35f), r * 0.32f, r * 0.24f, new Color(1f, 1f, 1f, 0.75f));
            if (pu.Kind == "turbo") Poly(p, new[] { at + new Vector2(-r * 0.25f, -r * 0.6f), at + new Vector2(r * 0.25f, -r * 0.6f), at + new Vector2(0f, r * 0.7f) }, MgScenery.Hex(0xFF7A1A));
            else if (pu.Kind == "magnet") { Poly(p, Rect(at + new Vector2(-r * 0.3f, 0f), r * 0.12f, r * 0.45f), Color.white); Poly(p, Rect(at + new Vector2(r * 0.3f, 0f), r * 0.12f, r * 0.45f), Color.white); }
        }

        private void PaintRunner(Painter2D p, ShoppingSurfRun.Run run)
        {
            var px = ShoppingSurfRun.PlayerX(run);
            var py = ShoppingSurfRun.PlayerY(run);
            var ground = Project(px, 0, 0.0);
            var sliding = run.SlideT >= 0.0;
            var shadowR = 0.42f * PxPerM * (1f - Mathf.Clamp01((float)py / 2f) * 0.4f);
            Ellipse(p, ground + new Vector2(0f, 3f), shadowR, shadowR * 0.28f, Shadow);
            var body = Project(px, py + (sliding ? 0.3 : 0.55), 0.0);
            var bw = 0.36f * PxPerM;
            var bh = (sliding ? 0.28f : 0.55f) * PxPerM;
            var stride = sliding || py > 0.05 ? 0f : Mathf.Sin((float)_scroll * 2.4f) * bw * 0.25f;
            Ellipse(p, body + new Vector2(-bw * 0.45f, bh * 0.75f + stride), bw * 0.28f, bw * 0.18f, MgScenery.Hex(0xF2D6B8));
            Ellipse(p, body + new Vector2(bw * 0.45f, bh * 0.75f - stride), bw * 0.28f, bw * 0.18f, MgScenery.Hex(0xF2D6B8));
            Ellipse(p, body, bw, bh, MgScenery.Hex(0xFFE9D2));
            if (run.Shield) { p.strokeColor = new Color(0.45f, 0.75f, 1f, 0.85f); p.lineWidth = 3f; EllipseStroke(p, body + new Vector2(0f, -bh * 0.4f), bw * 1.6f, bh * 1.7f); }
            if (run.MagnetT > 0.0) { p.strokeColor = new Color(0.95f, 0.42f, 0.42f, 0.5f); p.lineWidth = 2f; EllipseStroke(p, body + new Vector2(0f, -bh * 0.4f), bw * 2.2f, bh * 2.0f); }
            if (run.TurboT > 0.0)
                for (var i = 0; i < 6; i++)
                {
                    var x = FieldW * (0.1f + 0.16f * i);
                    var y = Mathf.Repeat((float)_scroll * 40f + i * 97f, FieldH * 0.6f) + FieldH * 0.35f;
                    Poly(p, Rect(new Vector2(x, y), 1.5f, 18f), new Color(1f, 1f, 1f, 0.55f));
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
            for (var i = 0; i <= 12; i++)
            {
                var a = Mathf.PI * 2f * i / 12f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Fill();
        }

        private static void EllipseStroke(Painter2D p, Vector2 c, float rx, float ry)
        {
            p.BeginPath();
            for (var i = 0; i <= 16; i++)
            {
                var a = Mathf.PI * 2f * i / 16f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Stroke();
        }

        private static double Mod(double v, double m)
        {
            var r = v % m;
            return r < 0.0 ? r + m : r;
        }

        // ── juice (shopping_surf.gd _handle_event — no shake) ──

        private Vector2 RunnerAt() => Project(ShoppingSurfRun.PlayerX(_game.Run), ShoppingSurfRun.PlayerY(_game.Run) + 1.0, 0.0);

        private void OnEvent(ShoppingSurfRun.Ev ev)
        {
            var t = Host.Strings;
            var run = _game.Run;
            StageEvent(ev);
            switch (ev.Type)
            {
                case "coin":
                    PlaySfx("game_coin", Sfx.ComboSemitones(Math.Min(run.Coins % 12, 8)), 0.8f);
                    if (FieldW > 1f) Burst(Project(ev.X, ev.Y, Math.Min(0.0, ev.Z)), Coin, 5);
                    break;
                case "nearMiss":
                    Banner(t.T("mg.shoppingSurf.near", ("streak", ev.Streak)), 0.9);
                    PlaySfx("game_hit", Sfx.ComboSemitones(Math.Min(ev.Streak, 6)));
                    Haptics.Tap();
                    if (FieldW > 1f) Float(RunnerAt() + new Vector2(0f, -40f), "+2", new Color(0.55f, 0.85f, 1f), 26f);
                    break;
                case "crash":
                    Banner(t.T("mg.shoppingSurf.crash", ("left", Math.Max(0, _game.Tune.ArcadeMaxCrashes - ev.Crashes))), 1.4);
                    PlaySfx("game_miss");
                    Haptics.Warn();
                    Flash(new Color(0.9f, 0.32f, 0.22f, 0.18f), 0.2f);
                    _crashWobble = 1.0;
                    if (FieldW > 1f) Burst(RunnerAt(), new Color(1f, 0.85f, 0.4f), 14);
                    break;
                case "wipeout":
                    PlaySfx("game_lose");
                    break;
                case "shieldPop":
                    Banner(t.T("mg.shoppingSurf.shield_pop"), 1.2);
                    PlaySfx("game_pop");
                    if (FieldW > 1f) Burst(RunnerAt(), new Color(0.55f, 0.8f, 1f), 16);
                    break;
                case "puddle":
                    Banner(t.T("mg.shoppingSurf.puddle"), 1.2);
                    PlaySfx("game_whoosh", -5f, 0.7f);
                    if (FieldW > 1f) Burst(Project(ShoppingSurfRun.PlayerX(run), 0.1, 0.0), Puddle, 12);
                    break;
                case "powerup":
                    Banner(t.T("mg.shoppingSurf." + ev.Kind), 1.5);
                    PlaySfx("game_star", 3f);
                    Haptics.Tap();
                    if (FieldW > 1f) Burst(RunnerAt(), Coin, 18);
                    break;
                case "powerupEnd":
                    Banner(t.T("mg.shoppingSurf." + ev.Kind + "_end"), 1.0);
                    break;
                case "jump":
                    PlaySfx("game_whoosh", 4f, 0.6f);
                    break;
                case "slide":
                    PlaySfx("soft_tap", -6f, 0.7f);
                    break;
                case "lane":
                    PlaySfx("soft_tap", ev.Dir > 0 ? 2f : 0f, 0.5f);
                    break;
                case "telegraph":
                    PlaySfx("soft_tick", 5f, 0.6f);
                    break;
            }
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

        private sealed class SurfCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public SurfCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
