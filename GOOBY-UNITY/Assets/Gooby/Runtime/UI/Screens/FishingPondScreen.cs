using System;
using System.Collections.Generic;
using Gooby.Core.Meta;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Angelteich host on the shared minigame frame (port of fishing_pond.gd + its pond stage): the pond in cross
    /// section, painted with the Painter2D — sky and hills over the surface, the water darkening with depth down to a
    /// pebbled bed, lily pads and ducks on top, Gooby on the jetty at the right with the rod held out over the middle.
    /// HOLD sinks the hook (a faint ring shows its reach), RELEASE hooks the nearest swimmer and raises it; small
    /// and medium fish fly into the bucket (+2/+3), a big one starts a reel fight (five taps in two seconds, meter
    /// under the HUD) or the line snaps, a boot costs 3. Rare fish glow gold; the full rare set pays a bonus.
    /// </summary>
    public sealed partial class FishingPondScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;
        private const double ViewHalfW = 1.95;

        private FishingPondGame _game;
        private PondCanvas _canvas;
        private VisualElement _gooby, _infoPill, _reelCard, _reelFill, _reelWindow;
        private Label _infoLabel, _reelLabel;
        private float _show, _shake, _splash, _goobyHop, _glow;
        private readonly List<Ripple> _ripples = new List<Ripple>();
        private readonly List<Flight> _flights = new List<Flight>();
        private readonly List<Spark> _sparks = new List<Spark>();
        private float _rippleTimer;

        private struct Ripple { public float X, Age, Power; }
        private struct Flight { public Vector2 From; public float T; public Color C; public float Len; }
        private struct Spark { public Vector2 P, V; public float Life, Max, Size; public Color C; }

        private static readonly Color Sky = new Color(0.62f, 0.83f, 0.98f);
        private static readonly Color Hills = MgScenery.Hex(0xA6DB98);
        private static readonly Color HillsFar = MgScenery.Hex(0xC3E8CF);
        private static readonly Color Bank = MgScenery.Hex(0x8CCB86);
        private static readonly Color Water = MgScenery.Hex(0x5A9FC0);
        private static readonly Color WaterTop = MgScenery.Hex(0x9ED8E8);
        private static readonly Color Bed = MgScenery.Hex(0xC4B294);
        private static readonly Color Plank = MgScenery.Hex(0xE3BC90);
        private static readonly Color PlankDark = MgScenery.Hex(0xB98B5E);
        private static readonly Color Rim = new Color(1f, 0.99f, 0.94f, 0.85f);

        public FishingPondScreen(GameHost host) : base(host, "fishingPond", "fishingPond") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0x9BD6C8);
        protected override Color FieldColor => Sky;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public FishingPondGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new FishingPondGame(Difficulty, seed);
            _game.Cast += () => { _splash = 0.3f; AddRipple(0.6f); };
            _game.MissedCast += () => { _splash = 0.5f; AddRipple(1f); StageMood("sad"); };
            _game.HookedFish += f => { _splash = 0.6f; AddRipple(1f); if (FishingPond.NeedsReel(f.Kind)) _shake = 0.3f; StageMood("ecstatic"); };
            _game.Landed += (f, delta) => { StageLanded(f, delta); OnLanded(f, delta); };
            _game.LineBroke += () => { OnLineBroke(); StageMood("dizzy"); };
            _game.RareSet += bonus => { Banner(Host.Strings.T("mg.fishingPond.rare_set", ("n", bonus)), 2.0); _glow = 1.2f; };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e => { _game?.Press(); Field.CapturePointer(e.pointerId); });
            Field.RegisterCallback<PointerUpEvent>(e => { if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId); _game?.Release(); });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _game?.Release());

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new PondCanvas { Paint = Paint };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(120);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            // depth (or the failures in endless) under the pills
            _infoPill = Ux.El("gb-card", "gb-row");
            _infoPill.name = "info-pill";
            Ux.Abs(_infoPill, null, 62);
            _infoPill.style.left = Length.Percent(50);
            _infoPill.style.translate = new Translate(Length.Percent(-50), 0);
            _infoPill.style.alignItems = Align.Center;
            _infoPill.style.paddingTop = 6; _infoPill.style.paddingBottom = 6; _infoPill.style.paddingLeft = 12; _infoPill.style.paddingRight = 16;
            _infoPill.pickingMode = PickingMode.Ignore;
            var icon = Ux.Icon("pin");
            icon.style.width = 30; icon.style.height = 30; icon.style.marginRight = 8;
            _infoPill.Add(icon);
            _infoLabel = Ux.Text("", "gb-md", "gb-bold");
            _infoPill.Add(_infoLabel);
            Root.Add(_infoPill);

            // the reel meter: five crank pips filling up, the window draining beneath — shown during a fight
            _reelCard = Ux.El("gb-card");
            _reelCard.name = "reel-meter";
            Ux.Abs(_reelCard, null, null, null, 64);
            _reelCard.style.left = Length.Percent(50);
            _reelCard.style.translate = new Translate(Length.Percent(-50), 0);
            _reelCard.style.width = 300;
            _reelCard.style.alignItems = Align.Center;
            _reelCard.pickingMode = PickingMode.Ignore;
            _reelLabel = Ux.Text(t.T("mg.fishingPond.reel"), "gb-h3");
            _reelCard.Add(_reelLabel);
            var bar = Ux.Bar(0f, out _reelFill, "gb-bar-fill--sonne");
            bar.style.width = 260; bar.style.marginTop = 6;
            _reelCard.Add(bar);
            var win = Ux.Bar(1f, out _reelWindow, "gb-bar-fill--pfirsich");
            win.style.width = 260; win.style.marginTop = 4; win.style.height = 8;
            _reelCard.Add(win);
            _reelCard.style.display = DisplayStyle.None;
            Root.Add(_reelCard);
            ComboLabel.style.top = 128;
            Banner(t.T("mg.fishingPond.intro"), FishingPondGame.IntroSec + 0.7);
        }

        // ── the round ──────────────────────────────────────────────────────

        // ── scenery (board 06, "Teich am Morgen"): the soft morning sky with a sun, two drifting clouds and a few birds —
        //    a layer behind the pond canvas (which paints the hills, bank, reeds, water, bed, fish, pads and the jetty in
        //    the board's pastels). Built once, pointer-transparent; the clouds drift (two translates a frame). ──

        private readonly List<VisualElement> _clouds = new List<VisualElement>();
        private float _sceneT;
        private static readonly float[] CloudW = { 104f, 72f };
        private static readonly float[] CloudTop = { 4f, 11f };
        private static readonly float[] CloudSpeed = { 6f, 9f };
        private static readonly float[] CloudPhase = { 30f, 220f };

        private void BuildScenery()
        {
            _clouds.Clear();
            var scene = MgScenery.Layer(Field, "mgf-fishingpond");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 32f);
            var sun = MgScenery.Sun(scene, 44f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, 7f, 7.5f);
            for (var i = 0; i < CloudW.Length; i++)
            {
                var cloud = MgScenery.Cloud(scene, CloudW[i]);
                cloud.name = "mgf-cloud-" + i;
                cloud.style.left = 0;
                MgScenery.Pct(cloud, null, CloudTop[i]);
                cloud.style.translate = new Translate(CloudPhase[i], 0);
                _clouds.Add(cloud);
            }
            var birds = MgScenery.Painted(scene, (p, r) =>
            {
                p.strokeColor = MgScenery.Hex(0x8A7F9E); p.lineWidth = 2.5f;
                for (var i = 0; i < 3; i++)
                {
                    var at = new Vector2(r.width * (0.2f + i * 0.3f), r.height * (0.3f + (i % 2) * 0.4f));
                    var w = 9f - i;
                    p.BeginPath(); p.MoveTo(at + new Vector2(-w, -3f)); p.QuadraticCurveTo(at + new Vector2(-w * 0.4f, -5f), at);
                    p.QuadraticCurveTo(at + new Vector2(w * 0.4f, -5f), at + new Vector2(w, -3f)); p.Stroke();
                }
            }, "gb-mgf-birds");
            MgScenery.Pct(birds, 56f, 15f);
            MgScenery.Size(birds, 80f, 34f);
        }

        private void DriftClouds()
        {
            if (FieldW <= 0f) return;
            var t = ReducedMotion ? 0f : _sceneT;
            for (var i = 0; i < _clouds.Count; i++)
                _clouds[i].style.translate = new Translate(Mathf.Repeat(CloudPhase[i] + t * CloudSpeed[i], FieldW + CloudW[i]) - CloudW[i], 0);
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            var f = (float)dt;
            _show += f;
            _sceneT += f;
            _shake = Mathf.Max(0f, _shake - f);
            _splash = Mathf.Max(0f, _splash - f);
            _goobyHop = Mathf.Max(0f, _goobyHop - f * 2f);
            _glow = Mathf.Max(0f, _glow - f);
            for (var i = _ripples.Count - 1; i >= 0; i--)
            {
                var r = _ripples[i]; r.Age += f;
                if (r.Age > 1.2f) _ripples.RemoveAt(i); else _ripples[i] = r;
            }
            _rippleTimer -= f;
            if (_rippleTimer <= 0f && _game.Running)
            {
                _rippleTimer = _game.Phase == FishingPondGame.PhaseReel ? 0.45f : 1.4f;
                AddRipple(_game.Phase == FishingPondGame.PhaseReel ? 0.9f : 0.5f);
            }
            for (var i = _flights.Count - 1; i >= 0; i--)
            {
                var fl = _flights[i]; fl.T += f * 1.6f;
                if (fl.T >= 1f) _flights.RemoveAt(i); else _flights[i] = fl;
            }
            for (var i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i]; s.Life -= f;
                if (s.Life <= 0f) { _sparks.RemoveAt(i); continue; }
                s.V.y += 300f * f; s.P += s.V * f; _sparks[i] = s;
            }
        }

        private void AddRipple(float power) => _ripples.Add(new Ripple { X = (float)_game.Tune.HookX, Age = 0f, Power = power });

        protected override void RenderGame()
        {
            var t = Host.Strings;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.fishingPond.failures", ("n", _game.Failures), ("max", _game.Tune.EndlessFailureLimit))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            _infoLabel.text = _game.Tune.Endless
                ? t.T("mg.fishingPond.failures", ("n", _game.Failures), ("max", _game.Tune.EndlessFailureLimit))
                : t.T("mg.fishingPond.depth", ("m", _game.HookDepth.ToString("0.0")));
            ComboLabel.text = _game.Phase == FishingPondGame.PhaseReel ? t.T("mg.fishingPond.reel") : "";
            var reeling = _game.Phase == FishingPondGame.PhaseReel;
            _reelCard.style.display = reeling ? DisplayStyle.Flex : DisplayStyle.None;
            if (reeling)
            {
                Ux.SetBar(_reelFill, (double)_game.ReelTaps / _game.Tune.ReelTaps);
                Ux.SetBar(_reelWindow, _game.ReelWindowLeft / _game.Tune.ReelWindowSec);
                _reelCard.style.scale = new Scale(Vector3.one * (1f + Mathf.Sin(_show * 26f) * 0.03f));
            }
            // Gooby sits on the jetty at the right, leaning over the water while the hook is down
            var jetty = JettyTop();
            var size = FieldW * 0.27f;
            _gooby.style.width = size; _gooby.style.height = size;
            _gooby.style.left = FieldW - size - 10f; _gooby.style.top = jetty.y - size + 10f - _goobyHop * 30f;
            var lean = _game.Phase == FishingPondGame.PhaseLower ? -10f : reeling ? Mathf.Sin(_show * 30f) * 8f : -3f;
            _gooby.style.rotate = new Rotate(lean);
            if (_stage != null) RenderStage(_shake > 0f && !ReducedMotion ? Mathf.Sin(Time.unscaledTime * 90f) * _shake * 14f : 0f);
            else DriftClouds();
            _canvas.MarkDirtyRepaint();
        }

        // ── the view ───────────────────────────────────────────────────────

        private float PpuX => FieldW / (float)(2.0 * ViewHalfW);
        private float SurfaceY => FieldH * 0.31f;
        private float PpuY => FieldH * 0.58f / (float)_game.DepthSpan;
        private float X(double x) => FieldW * 0.5f + (float)x * PpuX;
        private float Y(double depth) => SurfaceY + (float)depth * PpuY;
        private Vector2 At(double x, double depth) => new Vector2(X(x), Y(depth));
        private Vector2 JettyTop() => new Vector2(FieldW, SurfaceY - 28f);
        private Vector2 RodTip() => new Vector2(X(0.05), SurfaceY - FieldH * 0.16f);

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            var surface = SurfaceY;
            var shakeX = _shake > 0f && !ReducedMotion ? Mathf.Sin(Time.unscaledTime * 90f) * _shake * 14f : 0f;
            if (_stage != null)
            {
                PaintOverlay3D(p, shakeX);
                return;
            }

            // sky, far hills, the far bank
            // (the sky, sun, clouds and birds are the scenery layer behind the canvas)
            for (var i = 0; i < 4; i++)
            {
                var cx = FieldW * (0.1f + i * 0.28f); var r = FieldW * (0.22f + (i % 2) * 0.08f);
                Circle(p, new Vector2(cx, surface - 22f + r * 0.35f), r, i % 2 == 0 ? HillsFar : Hills);
            }
            FillRect(p, 0f, surface - 30f, FieldW, 30f, Bank);
            // reeds along the far bank
            p.strokeColor = MgScenery.Hex(0x6FA866); p.lineWidth = 3f; p.lineCap = LineCap.Round;
            for (var i = 0; i < 9; i++)
            {
                var x = 10f + i * FieldW / 9f + Mathf.Sin(i * 1.7f) * 6f;
                var sway = Mathf.Sin(_show * 1.3f + i) * 4f;
                Line(p, new Vector2(x, surface - 4f), new Vector2(x + sway, surface - 44f - (i % 3) * 12f));
                Circle(p, new Vector2(x + sway, surface - 46f - (i % 3) * 12f), 4.5f, MgScenery.Hex(0xB08560));
            }

            // the water: bands darkening with depth, then the pebbled bed
            var bottom = Y(_game.DepthSpan);
            var bands = 8;
            for (var i = 0; i < bands; i++)
            {
                var y0 = surface + (bottom - surface) * i / bands; var y1 = surface + (bottom - surface) * (i + 1) / bands;
                FillRect(p, 0f, y0, FieldW, y1 - y0 + 1f, Color.Lerp(WaterTop, Water, (i + 0.5f) / bands));
            }
            FillRect(p, 0f, bottom, FieldW, FieldH - bottom, Bed);
            for (var i = 0; i < 14; i++)
            {
                var x = (i * 71 + 13) % (int)Mathf.Max(1f, FieldW);
                var r = 6f + (i * 5) % 9;
                Circle(p, new Vector2(x, bottom + 6f + (i * 13) % 26), r, i % 2 == 0 ? MgScenery.Hex(0xDCCDB2) : MgScenery.Hex(0xAE9E84));
            }
            // weeds swaying up from the bed
            p.strokeColor = MgScenery.Hex(0x6DBE8C); p.lineWidth = 4f;
            for (var i = 0; i < 6; i++)
            {
                var x = 24f + i * FieldW / 6f;
                var sway = Mathf.Sin(_show * 1.1f + i * 0.9f) * 8f;
                p.BeginPath(); p.MoveTo(new Vector2(x, bottom + 4f)); p.LineTo(new Vector2(x + sway * 0.5f, bottom - 30f)); p.LineTo(new Vector2(x + sway, bottom - 66f - (i % 2) * 20f)); p.Stroke();
            }
            // light shafts and bubbles
            for (var i = 0; i < 3; i++)
            {
                var x0 = FieldW * (0.2f + i * 0.3f);
                Poly(p, new[] { new Vector2(x0 - 10f, surface), new Vector2(x0 + 30f, surface), new Vector2(x0 + 80f, bottom), new Vector2(x0 + 20f, bottom) }, new Color(1f, 1f, 1f, 0.05f));
            }
            for (var i = 0; i < 7; i++)
            {
                var ph = (_show * (0.12f + i * 0.02f) + i * 0.37f) % 1f;
                var x = FieldW * ((i * 0.143f + 0.05f) % 1f) + Mathf.Sin(_show * 2f + i) * 5f;
                Circle(p, new Vector2(x, Mathf.Lerp(bottom, surface + 6f, ph)), 2f + (i % 3), new Color(1f, 1f, 1f, 0.35f * (1f - ph)));
            }

            // swimmers (deeper → tinted towards the water), the hooked one on the line
            foreach (var f in _game.Fish) PaintSwimmer(p, f, At(f.X + shakeX / PpuX, f.Depth + Math.Sin(f.Wiggle) * 0.03), f.Dir, 1f);
            var hook = At(_game.Tune.HookX, _game.HookDepth);
            hook.x += shakeX;
            if (_game.Hooked != null)
            {
                var wobble = Mathf.Sin(_show * 18f) * 0.35f;
                PaintSwimmer(p, _game.Hooked, hook + new Vector2(0f, 0.16f * PpuY), 1.0, 1f, wobble);
            }

            // the surface: the line, ripples, lily pads, ducks
            var bob = new Vector2(X(_game.Tune.HookX) + shakeX, surface + Mathf.Sin(_show * 2.4f) * 2f);
            if (_game.Phase == FishingPondGame.PhaseReel) { bob.y += 6f + Mathf.Sin(_show * 26f) * 3f; bob.x += Mathf.Sin(_show * 31f) * 2f; }
            p.strokeColor = new Color(1f, 1f, 1f, 0.7f); p.lineWidth = 2f;
            foreach (var r in _ripples)
            {
                var k = r.Age / 1.2f;
                var rx = 10f + k * 70f * r.Power; var ry = rx * 0.28f;
                p.strokeColor = new Color(1f, 1f, 1f, 0.6f * (1f - k));
                EllipseStroke(p, new Vector2(X(r.X), surface), rx, ry);
            }
            p.strokeColor = new Color(1f, 1f, 1f, 0.55f); p.lineWidth = 2f;
            Line(p, new Vector2(0f, surface), new Vector2(FieldW, surface));
            for (var i = 0; i < 3; i++)
            {
                var x = FieldW * (0.12f + i * 0.36f) + Mathf.Sin(_show * 0.7f + i) * 3f;
                Ellipse(p, new Vector2(x, surface + 2f), 22f, 8f, MgScenery.Hex(0x7CC486));
                Poly(p, new[] { new Vector2(x, surface + 2f), new Vector2(x + 22f, surface - 3f), new Vector2(x + 22f, surface + 4f) }, Color.Lerp(WaterTop, Water, 0.2f));
                if (i == 1) Circle(p, new Vector2(x - 6f, surface - 4f), 6f, new Color(1f, 0.8f, 0.86f));
            }
            for (var i = 0; i < 2; i++)
            {
                var x = FieldW * (0.14f + i * 0.62f) + Mathf.Sin(_show * 0.4f + i * 2f) * 14f;
                var y = surface - 6f + Mathf.Sin(_show * 2f + i) * 2f;
                Ellipse(p, new Vector2(x, y), 16f, 10f, new Color(1f, 0.97f, 0.88f));
                Circle(p, new Vector2(x + 12f, y - 12f), 8f, new Color(1f, 0.97f, 0.88f));
                Poly(p, new[] { new Vector2(x + 19f, y - 12f), new Vector2(x + 28f, y - 9f), new Vector2(x + 19f, y - 7f) }, new Color(1f, 0.6f, 0.3f));
                Circle(p, new Vector2(x + 14f, y - 14f), 1.6f, Color.black);
            }

            // the jetty with the bucket, the rod from Gooby's hands to the tip, the line down to the hook
            var jetty = JettyTop();
            var jettyW = FieldW * 0.42f;
            FillRect(p, FieldW - jettyW, jetty.y, jettyW, 16f, Plank);
            FillRect(p, FieldW - jettyW, jetty.y + 16f, jettyW, 8f, PlankDark);
            p.strokeColor = PlankDark; p.lineWidth = 6f;
            Line(p, new Vector2(FieldW - jettyW + 14f, jetty.y + 20f), new Vector2(FieldW - jettyW + 14f, bottom * 0.5f + surface * 0.5f));
            Line(p, new Vector2(FieldW - 24f, jetty.y + 20f), new Vector2(FieldW - 24f, bottom * 0.5f + surface * 0.5f));
            var bucket = new Vector2(FieldW - jettyW + 34f, jetty.y - 4f);
            Poly(p, new[] { bucket + new Vector2(-16f, -34f), bucket + new Vector2(16f, -34f), bucket + new Vector2(12f, 0f), bucket + new Vector2(-12f, 0f) }, new Color(0.55f, 0.6f, 0.68f));
            Ellipse(p, bucket + new Vector2(0f, -34f), 16f, 5f, new Color(0.3f, 0.5f, 0.6f));
            var tip = RodTip();
            var grip = new Vector2(FieldW - FieldW * 0.2f, jetty.y - FieldW * 0.1f);
            p.strokeColor = new Color(0.45f, 0.28f, 0.16f); p.lineWidth = 4f;
            Line(p, grip, tip);
            p.strokeColor = new Color(0.98f, 0.98f, 0.9f, 0.9f); p.lineWidth = 1.5f;
            Line(p, tip, bob);
            Line(p, bob, hook + new Vector2(0f, -4f));
            // bobber and hook, the catch ring while aiming
            Circle(p, bob, 7f, new Color(0.9f, 0.25f, 0.2f));
            Circle(p, bob + new Vector2(0f, 4f), 6f, Color.white);
            if (_game.Phase == FishingPondGame.PhaseIdle || _game.Phase == FishingPondGame.PhaseLower)
            {
                p.strokeColor = new Color(Rim.r, Rim.g, Rim.b, 0.45f); p.lineWidth = 2f;
                EllipseStroke(p, hook, (float)_game.Tune.CatchRadius * PpuX, (float)_game.Tune.CatchRadius * PpuY);
            }
            p.strokeColor = new Color(0.85f, 0.87f, 0.9f); p.lineWidth = 3f;
            p.BeginPath(); p.MoveTo(hook + new Vector2(0f, -4f)); p.LineTo(hook + new Vector2(0f, 6f)); p.Arc(hook + new Vector2(-5f, 6f), 5f, Angle.Degrees(0f), Angle.Degrees(180f)); p.Stroke();
            if (_splash > 0f)
            {
                for (var i = 0; i < 6; i++)
                {
                    var a = Mathf.PI * (0.15f + 0.7f * i / 5f);
                    var k = 1f - _splash / 0.6f;
                    Circle(p, new Vector2(bob.x + Mathf.Cos(a) * 30f * k, surface - Mathf.Sin(a) * 40f * k + 40f * k * k), 3f, new Color(1f, 1f, 1f, 0.8f * (1f - k)));
                }
            }
            // catches flying into the bucket, sparks
            foreach (var fl in _flights)
            {
                var k = fl.T;
                var pos = Vector2.Lerp(fl.From, bucket + new Vector2(0f, -40f), k) + new Vector2(0f, -Mathf.Sin(k * Mathf.PI) * 90f);
                PaintFishShape(p, pos, fl.Len * (1f - k * 0.4f), 1.0, fl.C, 0f);
            }
            foreach (var s in _sparks)
            {
                var c = s.C; c.a = Mathf.Clamp01(s.Life / s.Max);
                Circle(p, s.P, s.Size, c);
            }
            if (_glow > 0f) FillRect(p, 0f, 0f, FieldW, FieldH, new Color(1f, 0.9f, 0.5f, 0.25f * Mathf.Clamp01(_glow)));
        }

        private void PaintSwimmer(Painter2D p, FishingPondGame.Swimmer f, Vector2 at, double dir, float alpha, float tilt = 0f)
        {
            if (f.IsBoot)
            {
                var s = 0.42f * PpuX;
                var boot = new Color(0.31f, 0.25f, 0.22f, alpha);
                Poly(p, new[] { at + new Vector2(-s * 0.3f, -s * 0.9f), at + new Vector2(s * 0.15f, -s * 0.9f), at + new Vector2(s * 0.15f, -s * 0.1f), at + new Vector2(s * 0.7f, s * 0.1f), at + new Vector2(s * 0.7f, s * 0.45f), at + new Vector2(-s * 0.3f, s * 0.45f) }, boot);
                FillRect(p, at.x - s * 0.3f, at.y + s * 0.3f, s, s * 0.15f, new Color(0.2f, 0.17f, 0.16f, alpha));
                return;
            }
            var len = (float)(f.Scale + 0.25) * PpuX;
            var deep = Mathf.Clamp01((float)(f.Depth / _game.Tune.MaxDepth));
            var color = Color.Lerp(Hex(f.Species), Water, deep * 0.55f);
            color.a = alpha;
            if (f.Rare) Circle(p, at, len * 0.55f, new Color(1f, 0.85f, 0.4f, 0.3f + 0.1f * Mathf.Sin(_show * 6f)));
            PaintFishShape(p, at, len, dir, color, tilt + (float)Math.Sin(f.Wiggle) * 0.12f);
        }

        /// <summary>A fish: an oval body, a forked tail behind it, a stripe, an eye — facing the way it swims.</summary>
        private static void PaintFishShape(Painter2D p, Vector2 at, float len, double dir, Color color, float tilt)
        {
            var d = dir >= 0 ? 1f : -1f;
            var h = len * 0.42f;
            Vector2 R(Vector2 v) { var c = Mathf.Cos(tilt); var s = Mathf.Sin(tilt); return at + new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }
            // tail
            Poly(p, new[] { R(new Vector2(-d * len * 0.32f, 0f)), R(new Vector2(-d * len * 0.58f, -h * 0.55f)), R(new Vector2(-d * len * 0.58f, h * 0.55f)) }, color);
            // body
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i <= 20; i++)
            {
                var a = Mathf.PI * 2f * i / 20f;
                var pt = R(new Vector2(Mathf.Cos(a) * len * 0.36f, Mathf.Sin(a) * h * 0.5f));
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Fill();
            // fin and stripe
            Poly(p, new[] { R(new Vector2(0f, -h * 0.45f)), R(new Vector2(-d * len * 0.12f, -h * 0.85f)), R(new Vector2(-d * len * 0.22f, -h * 0.4f)) }, Color.Lerp(color, Color.black, 0.15f));
            Ellipse(p, R(new Vector2(0f, h * 0.05f)), len * 0.22f, h * 0.16f, new Color(1f, 1f, 1f, 0.28f * color.a));
            // eye
            Circle(p, R(new Vector2(d * len * 0.22f, -h * 0.1f)), Mathf.Max(1.5f, h * 0.12f), new Color(1f, 1f, 1f, color.a));
            Circle(p, R(new Vector2(d * len * 0.24f, -h * 0.1f)), Mathf.Max(1f, h * 0.06f), new Color(0.1f, 0.1f, 0.15f, color.a));
        }

        private static Color Hex(string species)
        {
            var hex = FishingPond.SpeciesColors.TryGetValue(species, out var h) ? h : "#9FB2C8";
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;
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

        private static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        private static void Circle(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath(); p.Arc(c, Mathf.Max(0.5f, r), Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
        }

        private static void EllipsePath(Painter2D p, Vector2 c, float rx, float ry)
        {
            p.BeginPath();
            for (var i = 0; i <= 28; i++)
            {
                var a = Mathf.PI * 2f * i / 28f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
        }

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color;
            EllipsePath(p, c, rx, ry); p.Fill();
        }

        private static void EllipseStroke(Painter2D p, Vector2 c, float rx, float ry)
        {
            EllipsePath(p, c, rx, ry); p.Stroke();
        }

        // ── juice ──────────────────────────────────────────────────────────

        private void OnLanded(FishingPondGame.Swimmer fish, int value)
        {
            var at = At(_game.Tune.HookX, _game.HookDepth);
            if (fish.IsBoot)
            {
                _shake = 0.3f;
                FloatText(at, "−3", new Color(0.85f, 0.35f, 0.35f));
                return;
            }
            _goobyHop = 1f;
            _flights.Add(new Flight { From = at, T = 0f, C = Hex(fish.Species), Len = (float)(fish.Scale + 0.25) * PpuX });
            for (var i = 0; i < 16; i += ParticleStep)
            {
                var a = i * Mathf.PI * 2f / 16f;
                var sp = 90f + (i * 29) % 70;
                _sparks.Add(new Spark { P = at, V = new Vector2(Mathf.Cos(a) * sp, Mathf.Sin(a) * sp - 80f), Life = 0.6f, Max = 0.6f, Size = 3f + (i % 3), C = fish.Rare ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.95f, 0.75f) });
            }
            if (fish.Rare) _glow = Mathf.Max(_glow, 0.8f);
            FloatText(at, "+" + value, new Color(1f, 0.78f, 0.3f));
        }

        private void OnLineBroke()
        {
            _shake = 0.34f;
            var at = At(_game.Tune.HookX, _game.HookDepth);
            FloatText(at, Host.Strings.T("mg.fishingPond.line_break"), new Color(0.85f, 0.35f, 0.35f));
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            // the album: every landed species counts for the fish set (rares under their common cousin) — not for a
            // round dropped with pause → Neustart (the frame books nothing for it)
            var ids = _game.CollectionIds();
            if (ids.Count > 0 && !DroppingRound)
            {
                var payload = new JObject { ["collections"] = new JObject { ["fish"] = new JArray(ids) } };
                Host.Store.Update(s => CollectionsLogic.AwardReport(s, payload), GameHost.TicksMs);
                Host.Store.NotifySliceChanged("collections");
            }
            _game = null;
            _flights.Clear(); _sparks.Clear(); _ripples.Clear();
            _infoPill?.RemoveFromHierarchy();
            _reelCard?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class PondCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public PondCanvas()
            {
                pickingMode = PickingMode.Ignore;
                name = "pond";
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
