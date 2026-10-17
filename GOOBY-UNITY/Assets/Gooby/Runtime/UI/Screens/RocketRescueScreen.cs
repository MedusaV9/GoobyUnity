using System;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Raketen-Rettung host on the shared minigame frame (port of rocket_rescue.gd + its moon stage): a lander with
    /// Gooby as pilot over a cratered plane, five floating pads each with a stranded bunny, the station on the ground,
    /// fuel cans in the sky. Hold is thrust, the left/right thirds tilt. A zone bar ◀ ▲ ▶ lights under the finger.
    /// Game feel after rocket_rescue.gd: moon dust on liftoff and touchdown, "Perfekt! +5" and the perfect tone for a
    /// soft landing, sparks + knock for a hard one, a green burst and the tank level for a fuel can, a pop when a bunny
    /// climbs aboard, and the big star fountain + "+30" over the station for every rescue.
    /// </summary>
    public sealed partial class RocketRescueScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private RocketRescueGame _game;
        private MoonCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel;
        private float _show, _scale, _groundY;
        private bool _pressed;

        private static readonly Color SkyTop = MgScenery.Hex(0x221B4C);
        private static readonly Color SkyMid = MgScenery.Hex(0x463884);
        private static readonly Color Horizon = MgScenery.Hex(0x9377C2);
        private static readonly Color Ground = MgScenery.Hex(0xC4BCE2);
        private static readonly Color GroundDark = MgScenery.Hex(0xA69DCC);
        private static readonly Color PadC = new Color(0.55f, 0.78f, 0.95f);
        private static readonly Color PlatC = new Color(0.78f, 0.62f, 0.48f);
        private static readonly Color BunnyC = new Color(0.96f, 0.82f, 0.62f);
        private static readonly Color CanC = new Color(0.45f, 0.86f, 0.6f);
        private static readonly Color CraftC = new Color(0.92f, 0.93f, 0.98f);
        private static readonly Color FlameC = new Color(1f, 0.62f, 0.28f);
        private static readonly Color Planet = MgScenery.Hex(0xFFB38A);

        // ── scenery (board 06 "Nacht – ruhig", pastel space, never black): violet space graded to a lilac glow over the
        //    moon plain, soft nebulae, stars and breathing twinkles, Gooby's little home planet (blue with a green
        //    island and a cloud), a pink moon with craters, a shooting star, and the pale lilac moon ground (88 % down)
        //    with craters and rocks. A layer behind the moon canvas (the ringed planet, pads, platforms, bunnies,
        //    fuel, station and lander stay painted); built once, pointer-transparent; only the twinkles breathe. ──

        private VisualElement _twinkles;

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-rocketrescue");
            var ground = (float)RocketRescueGame.GroundFrac * 100f;
            MgScenery.Gradient(scene, SkyTop, SkyMid, 0f, 55f);
            MgScenery.Gradient(scene, SkyMid, Horizon, 55f, ground + 1f);
            foreach (var (c, d, l, t) in new[] { (new Color(1f, 0.6f, 0.85f, 0.1f), 260f, -30f, 30f), (new Color(0.5f, 0.9f, 0.95f, 0.08f), 220f, 58f, 8f), (new Color(0.72f, 0.6f, 1f, 0.1f), 280f, 20f, 58f) })
            {
                var n = MgScenery.Shape(scene, c, d * 0.5f, "gb-mgf-nebula");
                MgScenery.Pct(n, l, t);
                MgScenery.Size(n, d, d);
            }
            var stars = MgScenery.Dots(scene, 46, new Color(1f, 0.97f, 0.9f, 0.7f), 1f, 2.2f, 37);
            MgScenery.Pct(stars, 0, 0, 0);
            stars.style.height = Length.Percent(80f);
            _twinkles = MgScenery.Group(scene, "gb-mgf-twinkles");
            Ux.Abs(_twinkles, 0, 0, 0, 0);
            float[] twAt = { 12f, 46f, 84f, 30f, 64f }; float[] twTop = { 30f, 12f, 44f, 58f, 66f };
            for (var i = 0; i < twAt.Length; i++) MgScenery.Pct(MgScenery.Twinkle(_twinkles, i % 2 == 0 ? 14f : 10f, new Color(1f, 0.92f, 0.7f, 0.85f)), twAt[i], twTop[i]);
            // Gooby's home planet: blue with a green island and a cloud band
            var home = MgScenery.Shape(scene, MgScenery.Hex(0x8CC8F5), 34f, "gb-mgf-planet");
            home.name = "mgf-home-planet";
            MgScenery.Pct(home, 7f, 9f);
            MgScenery.Size(home, 68f, 68f);
            MgScenery.Outline(home, 3f, MgScenery.Hex(0xC4E4FF));
            home.style.overflow = Overflow.Hidden;
            var isle = MgScenery.Shape(home, MgScenery.Hex(0x9ED499), 14f); isle.style.left = 10f; isle.style.top = 16f; MgScenery.Size(isle, 30f, 22f);
            var isle2 = MgScenery.Shape(home, MgScenery.Hex(0xA6DB98), 9f); isle2.style.left = 36f; isle2.style.top = 38f; MgScenery.Size(isle2, 20f, 16f);
            var band = MgScenery.Shape(home, new Color(1f, 1f, 1f, 0.7f), 5f); band.style.left = -4f; band.style.right = 18f; band.style.top = 30f; band.style.height = 8f;
            var shine = MgScenery.Shape(home, new Color(1f, 1f, 1f, 0.45f), 6f); shine.style.left = 12f; shine.style.top = 8f; MgScenery.Size(shine, 14f, 9f);
            // a pink moon with craters
            var moon = MgScenery.Shape(scene, MgScenery.Hex(0xFFC2D6), 17f, "gb-mgf-moon");
            MgScenery.Pct(moon, 40f, 40f);
            MgScenery.Size(moon, 34f, 34f);
            MgScenery.Outline(moon, 2f, MgScenery.Hex(0xFFE0EA));
            var c1 = MgScenery.Shape(moon, MgScenery.Hex(0xF5A8C2), 4f); c1.style.left = 7f; c1.style.top = 8f; MgScenery.Size(c1, 9f, 9f);
            var c2 = MgScenery.Shape(moon, MgScenery.Hex(0xF5A8C2), 3f); c2.style.left = 18f; c2.style.top = 18f; MgScenery.Size(c2, 6f, 6f);
            // a shooting star
            var streak = MgScenery.Painted(scene, (p, r) =>
            {
                for (var i = 0; i < 4; i++)
                {
                    p.strokeColor = new Color(1f, 0.95f, 0.8f, 0.12f + i * 0.12f); p.lineWidth = 2f + i;
                    var k = i / 4f;
                    p.BeginPath(); p.MoveTo(new Vector2(r.width * k, r.height * k)); p.LineTo(new Vector2(r.width, r.height)); p.Stroke();
                }
                MgScenery.Disc(p, new Vector2(r.width, r.height), 3.5f, MgScenery.Hex(0xFFF4D6));
            }, "gb-mgf-comet");
            MgScenery.Pct(streak, 58f, 26f);
            MgScenery.Size(streak, 70f, 34f);
            // the moon ground with craters and rocks
            var plain = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, Ground);
                MgScenery.FillRect(p, 0f, 0f, r.width, 5f, MgScenery.Hex(0xE0DAF4));
                for (var i = 0; i < 7; i++)
                {
                    var at = new Vector2(r.width * (0.06f + i * 0.14f + (MgScenery.Hash(i, 3) - 0.5f) * 0.06f), r.height * (0.35f + 0.4f * MgScenery.Hash(i, 4)));
                    var rx = 12f + 12f * MgScenery.Hash(i, 5);
                    MgScenery.Oval(p, at, rx, rx * 0.34f, GroundDark, MgScenery.Hex(0xDCD5F0), 2f);
                }
            }, "gb-mgf-moonground");
            plain.name = "mgf-ground";
            MgScenery.Pct(plain, 0, ground, 0, 0);
            float[] rockAt = { 3f, 88f }; float[] rockW = { 34f, 26f };
            for (var i = 0; i < rockAt.Length; i++)
            {
                var rock = MgScenery.Shape(scene, MgScenery.Hex(0xAFA6D4), rockW[i] * 0.4f, "gb-mgf-rock");
                MgScenery.Pct(rock, rockAt[i], null, null, 100f - ground - 1.2f);
                MgScenery.Size(rock, rockW[i], rockW[i] * 0.62f);
                MgScenery.RoundBottom(rock, 3f);
                MgScenery.Outline(rock, 2.5f, MgScenery.Hex(0x8F86BA));
            }
        }

        public RocketRescueScreen(GameHost host) : base(host, "rocketRescue", "rocketRescue") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        /// <summary>The score counts the fuel left, so it drains while thrusting — only rescues get the score pop.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.GainsOnly;
        protected override Color BackdropColor => MgScenery.Hex(0x33295F);
        protected override Color FieldColor => SkyTop;

        public RocketRescueGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new RocketRescueGame(Difficulty, seed);
            _game.BunnyAboard += () => { Banner(Host.Strings.T("mg.rocketRescue.aboard"), 1.2); OnBunnyAboard(); StageMood("ecstatic"); };
            _game.Rescued_ += n => { Banner(Host.Strings.T("mg.rocketRescue.saved"), 1.4); OnRescued(n); StageRescue(); };
            _game.HardLanded += () => { Banner(Host.Strings.T("mg.rocketRescue.hard"), 1.2); OnHardLanded(); StageMood("dizzy"); StageDust(); };
            _game.FuelLow += () => { Banner(Host.Strings.T("mg.rocketRescue.low_fuel"), 1.3); OnFuelLow(); };
            _game.OutOfFuel += () => { Banner(Host.Strings.T("mg.rocketRescue.towed"), 1.6); OnOutOfFuel(); };
            _game.LiftedOff += () => { OnLiftedOff(); StageDust(); };
            _game.Landed += OnLanded;
            _game.FuelPicked += OnFuelPicked;
            _game.WindTelegraph += _ => PlaySfx("game_whoosh", -5f, 0.6f);
            _game.WindGust += _ => { PlaySfx("game_whoosh", 0f, 0.9f); if (!ReducedMotion) Shake(0.08f); };
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
                Aim(true, e.localPosition.x);
            });
            Field.RegisterCallback<PointerMoveEvent>(e => { if (_pressed) Aim(true, e.localPosition.x); });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                _pressed = false;
                Aim(false, e.localPosition.x);
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => { _pressed = false; Aim(false, FieldW * 0.5f); });

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new MoonCanvas { Paint = Paint, name = "moon" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(44);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            _statsPill = Ux.El("gb-card", "gb-row");
            _statsPill.name = "fuel-pill";
            Ux.Abs(_statsPill, null, 62);
            _statsPill.style.left = Length.Percent(50);
            _statsPill.style.translate = new Translate(Length.Percent(-50), 0);
            _statsPill.style.alignItems = Align.Center;
            _statsPill.style.paddingTop = 6; _statsPill.style.paddingBottom = 6; _statsPill.style.paddingLeft = 14; _statsPill.style.paddingRight = 16;
            _statsPill.pickingMode = PickingMode.Ignore;
            _statsLabel = Ux.Text("", "gb-md", "gb-bold");
            _statsPill.Add(_statsLabel);
            Root.Add(_statsPill);

            ComboLabel.style.color = new Color(0.85f, 0.95f, 0.88f);
            Banner(t.T("mg.rocketRescue.intro"), RocketRescueGame.IntroSec + 0.7);
        }

        private void Aim(bool down, float viewX)
        {
            if (_game == null || FieldW <= 1f) return;
            _game.SetTouch(down, (viewX / FieldW) * 2.0 - 1.0);
        }

        protected override void TickGame(double dt)
        {
            if (FieldW > 1f && _scale > 1f)
                _game.SetVisibleHalf(FieldW * 0.5f / _scale);
            _game.Tick(dt);
            _show += (float)dt;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var e = g.Engine;
            _scale = (FieldH * (float)RocketRescueGame.WorldHFrac) / ((float)g.Tune.CeilingY + 1.4f);
            _groundY = FieldH * (float)RocketRescueGame.GroundFrac;
            var craft = WorldToView(e.X, e.Y);
            var squash = 1f - 0.18f * (float)g.Squash01;
            _gooby.style.left = craft.x - 22f;
            _gooby.style.top = craft.y - 52f * squash;
            _gooby.style.scale = new Scale(new Vector3(1f / squash, squash, 1f));
            _gooby.style.rotate = new Rotate(-(float)(e.Tilt * 180.0 / Math.PI));

            TimeLabel.text = g.Tune.Endless
                ? t.T("mg.rocketRescue.rescued", ("n", e.Rescued), ("max", g.Tune.PlatformCount))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            _statsLabel.text = t.T("mg.rocketRescue.fuel", ("n", Mathf.RoundToInt((float)e.Fuel)))
                + " · " + t.T("mg.rocketRescue.rescued", ("n", e.Rescued), ("max", g.Tune.PlatformCount));
            ComboLabel.text = e.Carrying ? t.T("mg.rocketRescue.aboard") : "";
            if (_twinkles != null) _twinkles.style.opacity = ReducedMotion ? 0.8f : 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(_show * 1.2f));
            if (_stage != null) RenderStage();
            _canvas.MarkDirtyRepaint();
        }

        private Vector2 WorldToView(double wx, double wy) =>
            new Vector2(FieldW * 0.5f + (float)(wx - _game.CamX) * _scale, _groundY - (float)wy * _scale);

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f) return;
            var g = _game;
            var e = g.Engine;
            if (_stage != null)
            {
                // over the 3D moon only the zone bar under the finger and the fuel gauge stay painted
                DrawZones(p, g);
                DrawFuelBar(p, e.Fuel / g.Tune.FuelMax);
                return;
            }
            var introK = g.IntroLeft > 0.0 ? 1f - (float)(g.IntroLeft / RocketRescueGame.IntroSec) : 1f;
            // the space gradient, nebulae, stars, the far planets and the moon ground are the scenery layer behind the
            // canvas; the ringed planet stays painted (it rises during the intro)

            // ring planet in the intro total / upper sky
            var planet = new Vector2(FieldW * 0.78f, FieldH * (0.18f - 0.06f * (1f - introK)));
            Ellipse(p, planet, 48f, 38f, Planet);
            p.strokeColor = new Color(1f, 0.89f, 0.64f, 0.85f); p.lineWidth = 6f;
            EllipseStroke(p, planet, 70f, 14f);


            var pad = WorldToView(e.Layout.PadX, e.Layout.PadY);
            var padW = (float)e.Layout.PadHalfW * _scale;
            Poly(p, Rect(pad + new Vector2(0f, 6f), padW, 8f), new Color(0.35f, 0.42f, 0.55f));
            Poly(p, Rect(pad, padW, 5f), PadC);
            if (g.BeaconLeft > 0.0)
                Circle(p, pad + new Vector2(0f, -18f), 16f + (float)g.BeaconLeft * 40f, new Color(1f, 0.86f, 0.45f, 0.35f));
            Circle(p, pad + new Vector2(0f, -10f), 7f, new Color(1f, 0.9f, 0.55f, 0.8f));

            for (var i = 0; i < e.Layout.Platforms.Count; i++)
            {
                var plat = e.Layout.Platforms[i];
                var at = WorldToView(plat.X, plat.Y);
                var hw = (float)plat.HalfW * _scale;
                Poly(p, Rect(at + new Vector2(0f, 4f), hw, 6f), new Color(0.45f, 0.35f, 0.28f));
                Poly(p, Rect(at, hw, 4f), PlatC);
                if (plat.Bunny && !(e.Carrying && e.LastLandedOn == i && e.LandedOn == i))
                    DrawBunny(p, at + new Vector2(0f, -14f));
            }

            for (var i = 0; i < e.Layout.FuelPickups.Count; i++)
            {
                var f = e.Layout.FuelPickups[i];
                if (f.Taken) continue;
                var at = WorldToView(f.X, f.Y);
                var bob = Mathf.Sin(_show * 3f + i) * 3f;
                Ellipse(p, at + new Vector2(0f, bob), 7f, 10f, CanC);
                Ellipse(p, at + new Vector2(0f, bob - 8f), 4f, 3f, new Color(0.35f, 0.7f, 0.5f));
            }

            if (e.WindPhase != "idle")
            {
                var a = e.WindPhase == "telegraph" ? 0.28f : 0.7f;
                p.strokeColor = new Color(0.8f, 0.9f, 1f, a); p.lineWidth = 3f;
                for (var i = 0; i < 6; i++)
                {
                    var y = FieldH * (0.2f + 0.1f * i);
                    var x0 = FieldW * (e.WindDir > 0 ? 0.12f : 0.88f);
                    var x1 = x0 + e.WindDir * FieldW * 0.2f;
                    Line(p, new Vector2(x0, y), new Vector2(x1, y));
                }
            }

            var craft = WorldToView(e.X, e.Y);
            var tilt = -(float)e.Tilt;
            var up = new Vector2(Mathf.Sin(tilt), -Mathf.Cos(tilt));
            var right = new Vector2(Mathf.Cos(tilt), Mathf.Sin(tilt));
            var squash = 1f - 0.18f * (float)g.Squash01;
            var body = craft + up * 10f;
            Ellipse(p, body, 14f * squash, 22f / squash, CraftC);
            Ellipse(p, body + right * 10f, 5f, 10f, new Color(0.75f, 0.45f, 0.5f));
            Ellipse(p, body - right * 10f, 5f, 10f, new Color(0.75f, 0.45f, 0.5f));
            if (g.Thrust && e.Fuel > 0.0 && !e.Towing)
            {
                var flicker = 0.7f + 0.3f * Mathf.Sin(_show * 28f);
                Ellipse(p, craft - up * (18f * flicker), 6f, 14f * flicker, FlameC);
                Ellipse(p, craft - up * (26f * flicker), 3.5f, 8f * flicker, new Color(1f, 0.92f, 0.55f));
            }
            if (e.Carrying) DrawBunny(p, craft + up * 28f + right * 16f);

            DrawZones(p, g);
            DrawFuelBar(p, e.Fuel / g.Tune.FuelMax);
        }

        private void DrawBunny(Painter2D p, Vector2 at)
        {
            Ellipse(p, at, 8f, 9f, BunnyC);
            Ellipse(p, at + new Vector2(-4f, -10f), 2.4f, 7f, BunnyC);
            Ellipse(p, at + new Vector2(4f, -10f), 2.4f, 7f, BunnyC);
            Circle(p, at + new Vector2(-3f, -1f), 1.4f, new Color(0.2f, 0.15f, 0.12f));
            Circle(p, at + new Vector2(3f, -1f), 1.4f, new Color(0.2f, 0.15f, 0.12f));
        }

        private void DrawZones(Painter2D p, RocketRescueGame g)
        {
            var h = FieldH * 0.07f;
            var y = FieldH - h - 10f;
            var third = FieldW / 3f;
            for (var i = 0; i < 3; i++)
            {
                var zone = i - 1;
                var active = g.Touching && g.TiltDir == zone;
                var c = new Vector2(third * i + third * 0.5f, y + h * 0.5f);
                Poly(p, Rect(c, third * 0.42f, h * 0.38f), active ? new Color(0.5f, 0.6f, 1f, 0.35f) : new Color(0.2f, 0.2f, 0.4f, 0.18f));
                // the arrows themselves sit in ComboLabel / the painted bar — a pip is enough at this scale
                Circle(p, c, active ? 7f : 4f, active ? new Color(1f, 0.95f, 0.75f) : new Color(0.8f, 0.84f, 1f, 0.45f));
            }
        }

        private void DrawFuelBar(Painter2D p, double pct)
        {
            var w = FieldW * 0.42f;
            var c = new Vector2(FieldW * 0.5f, 28f);
            Poly(p, Rect(c, w * 0.5f, 6f), new Color(0.15f, 0.16f, 0.28f, 0.7f));
            var fill = (float)Math.Max(0.0, Math.Min(1.0, pct));
            var col = fill < 0.2f ? new Color(1f, 0.5f, 0.4f) : new Color(0.45f, 0.86f, 0.6f);
            Poly(p, new[]
            {
                c + new Vector2(-w * 0.5f, -5f), c + new Vector2(-w * 0.5f + w * fill, -5f),
                c + new Vector2(-w * 0.5f + w * fill, 5f), c + new Vector2(-w * 0.5f, 5f),
            }, col);
        }

        private void Band(Painter2D p, float y0, float y1, Color a, Color b)
        {
            const int steps = 6;
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

        private static void Circle(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath(); p.Arc(c, Mathf.Max(0.5f, r), Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
        }

        private static void EllipsePath(Painter2D p, Vector2 c, float rx, float ry)
        {
            p.BeginPath();
            for (var i = 0; i <= 18; i++)
            {
                var a = Mathf.PI * 2f * i / 18f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
        }

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color; EllipsePath(p, c, rx, ry); p.Fill();
        }

        private static void EllipseStroke(Painter2D p, Vector2 c, float rx, float ry)
        {
            EllipsePath(p, c, rx, ry); p.Stroke();
        }

        // ── juice (rocket_rescue.gd _handle_event / _on_landing / _on_hard_landing / _on_rescue) ──

        private static readonly Color Dust = new Color(0.62f, 0.6f, 0.68f);
        private static readonly Color SoftC = new Color(0.6f, 0.95f, 0.8f);
        private static readonly Color HardC = new Color(1f, 0.55f, 0.35f);
        private static readonly Color RescueC = new Color(1f, 0.86f, 0.45f);

        /// <summary>False before the first render has sized the moon (events only fire after the intro, so rarely).</summary>
        private bool Sized => _game != null && FieldW > 1f && _scale > 1f;

        private Vector2 CraftAt() => WorldToView(_game.Engine.X, _game.Engine.Y);

        private void OnLiftedOff()
        {
            if (!Sized) return;
            Burst(CraftAt() + new Vector2(0f, 6f), Dust, 8);
            PlaySfx("game_whoosh", -2f, 0.7f);
        }

        private void OnLanded(string kind, bool bonus)
        {
            if (!Sized) return;
            var at = CraftAt();
            Burst(at + new Vector2(0f, 6f), Dust, 8);
            if (kind != "soft")
            {
                PlaySfx("game_hit", -2f, 0.8f);
                return;
            }
            PlaySfx("game_perfect", 0f, 0.8f);
            if (!bonus) return;
            Burst(at, SoftC, 12);
            Float(at + new Vector2(0f, -56f), Host.Strings.T("mg.hudkit.beat_perfekt", ("n", _game.Tune.SoftLandingBonus)), SoftC, 26f);
            Haptics.Tap();
        }

        private void OnHardLanded()
        {
            if (!Sized) return;
            var at = CraftAt();
            Burst(at, HardC, 14);
            Burst(at + new Vector2(0f, 6f), Dust, 8);
            Float(at + new Vector2(0f, -56f), "−" + Mathf.RoundToInt((float)_game.Tune.HardFuelPenalty), HardC, 26f);
            Flash(new Color(1f, 0.45f, 0.35f, 0.14f), 0.16f);
            if (!ReducedMotion) Shake(0.45f);
            PlaySfx("game_miss");
            Haptics.Warn();
        }

        private void OnBunnyAboard()
        {
            if (!Sized) return;
            Burst(CraftAt() + new Vector2(0f, -20f), BunnyC, 12);
            PlaySfx("game_pop", 4f);
            Haptics.Tap();
        }

        /// <summary>DIE Belohnung des Spiels: the star fountain over the station, "+30", a gold flash, a success buzz.</summary>
        private void OnRescued(int count)
        {
            if (!Sized) return;
            var e = _game.Engine;
            var pad = WorldToView(e.Layout.PadX, e.Layout.PadY) + new Vector2(0f, -18f);
            Burst(pad, RescueC, 26);
            Burst(pad, Color.white, 12);
            Burst(pad, BunnyC, 10);
            Float(pad + new Vector2(0f, -30f), "+" + _game.Tune.RescuePoints, RescueC, 32f);
            Flash(new Color(1f, 0.9f, 0.55f, 0.2f), 0.24f);
            PlaySfx("game_star", Sfx.ComboSemitones(count));
            Haptics.Success();
            if (count % 3 == 0) ConfettiRain(40);
        }

        private void OnFuelPicked(double amount)
        {
            if (!Sized) return;
            var at = CraftAt();
            Burst(at, CanC, 12);
            Float(at + new Vector2(0f, -56f), Host.Strings.T("mg.rocketRescue.fuel", ("n", Mathf.RoundToInt((float)_game.Engine.Fuel))), CanC, 24f);
            ScalePop(_statsPill, 1.2f);
        }

        private void OnFuelLow()
        {
            PlaySfx("game_miss", -3f, 0.7f);
            ScalePop(_statsPill, 1.2f);
            Haptics.Warn();
        }

        private void OnOutOfFuel()
        {
            PlaySfx("game_lose", 0f, 0.8f);
            if (Sized) Burst(CraftAt(), Dust, 10);
            Haptics.Warn();
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

        private sealed class MoonCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public MoonCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
