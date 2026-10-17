using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Sternenlaterne host on the shared minigame frame (port of lantern_float.gd + its night-sky stage): a paper
    /// lantern with Gooby in the basket rises through the night. Rings, fireflies and clouds are painted in
    /// perspective above the lake; a sideways drag steers, telegraphed gusts shove, a cloud bump flashes the lantern.
    /// With a GPU the sky, hoops, clouds, fireflies and the lantern are the 3D stage of <c>LanternFloatScreen.Stage3D.cs</c>.
    /// </summary>
    public sealed partial class LanternFloatScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private LanternFloatGame _game;
        private SkyCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel;
        private float _show, _shake, _glow, _bumpFlash;
        private string _gustPhase = "idle";
        private readonly List<Spark> _sparks = new List<Spark>();
        private bool _pressed;

        private struct Spark { public Vector2 P, V; public float Life, Max, Size; public Color C; }

        private static readonly Color SkyTop = MgScenery.Hex(0x2B2358);
        private static readonly Color SkyMid = MgScenery.Hex(0x4F4290);
        private static readonly Color Horizon = MgScenery.Hex(0xE7A2B6);
        private static readonly Color Paper = new Color(1f, 0.72f, 0.38f);
        private static readonly Color PaperHot = new Color(1f, 0.88f, 0.55f);
        private static readonly Color RingC = new Color(0.72f, 0.9f, 1f);
        private static readonly Color GoldC = new Color(1f, 0.82f, 0.35f);

        // ── scenery (board 06, "Abend am See" — violet dusk, never black): the sky graded from deep violet through lilac
        //    to a rosy horizon glow, stars and a few twinkles (their opacity breathes), the moon with its halo, two rows
        //    of round hills with a little cottage whose window glows, the lake with the moon's shimmering reflection,
        //    soft glints and reeds at the shore. A layer behind the sky canvas (clouds, rings, fireflies and the lantern
        //    stay painted); built once, pointer-transparent; the glints bob and the twinkles breathe (two changes a
        //    frame, still with reduced motion). ──

        private VisualElement _glints, _twinkles;

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-lanternfloat");
            MgScenery.Gradient(scene, SkyTop, SkyMid, 0f, 42f);
            MgScenery.Gradient(scene, SkyMid, Horizon, 42f, 73f);
            var stars = MgScenery.Dots(scene, 40, new Color(1f, 0.97f, 0.88f, 0.7f), 1f, 2.2f, 17);
            MgScenery.Pct(stars, 0, 0, 0);
            stars.style.height = Length.Percent(50f);
            _twinkles = MgScenery.Group(scene, "gb-mgf-twinkles");
            Ux.Abs(_twinkles, 0, 0, 0, 0);
            float[] twAt = { 8f, 30f, 52f, 18f, 64f, 40f }; float[] twTop = { 8f, 4f, 11f, 30f, 36f, 22f };
            for (var i = 0; i < twAt.Length; i++) MgScenery.Pct(MgScenery.Twinkle(_twinkles, i % 2 == 0 ? 13f : 9f, new Color(1f, 0.94f, 0.75f, 0.9f)), twAt[i], twTop[i]);
            var moon = MgScenery.Sun(scene, 46f, MgScenery.Hex(0xFFF4D6), new Color(1f, 0.96f, 0.84f, 0.16f));
            moon.name = "mgf-moon";
            MgScenery.Pct(moon, 69f, 12.5f);
            var core = moon.Q(className: "gb-mgf-sun-core");
            if (core != null)
            {
                var cr = MgScenery.Shape(core, MgScenery.Hex(0xF1E3BD), 6f); cr.style.left = 22f; cr.style.top = 8f; MgScenery.Size(cr, 12f, 12f);
            }
            // far hills, the cottage, the near hills
            foreach (var (l, w, h) in new[] { (-10f, 190f, 70f), (30f, 220f, 88f), (70f, 170f, 64f) })
            {
                var hill = MgScenery.Hill(scene, w, h, MgScenery.Hex(0x7B67B0), MgScenery.Hex(0x9784C6));
                MgScenery.Pct(hill, l, null, null, 27f);
            }
            var house = MgScenery.House(scene, 46f, 48f, MgScenery.Hex(0xB9A8E0), MgScenery.Hex(0x8A6FC0), MgScenery.Hex(0x6A58A0));
            MgScenery.Pct(house, 20f, null, null, 30.5f);
            var glow = MgScenery.Shape(house, new Color(1f, 0.85f, 0.45f, 0.35f), 12f);
            glow.style.left = 2f; glow.style.top = 18f; MgScenery.Size(glow, 24f, 24f);
            var win = MgScenery.Shape(house, MgScenery.Hex(0xFFE08A), 2f);
            win.style.left = 8f; win.style.top = 25f; MgScenery.Size(win, 9f, 9f);
            foreach (var (l, w, h) in new[] { (-16f, 240f, 56f), (52f, 260f, 46f) })
            {
                var hill = MgScenery.Hill(scene, w, h, MgScenery.Hex(0x5E4E98), MgScenery.Hex(0x7563B0));
                MgScenery.Pct(hill, l, null, null, 27f);
            }
            // the lake
            var lake = MgScenery.Gradient(scene, MgScenery.Hex(0x9C8FD6), MgScenery.Hex(0x4E4796), 73f, 100f);
            lake.name = "mgf-lake";
            var shore = MgScenery.Shape(scene, MgScenery.Hex(0xB9AEE6), 0f);
            MgScenery.Pct(shore, 0, 72.6f, 0); shore.style.height = 3f;
            var reflect = MgScenery.Painted(scene, (p, r) =>
            {
                for (var i = 0; i < 7; i++)
                {
                    var y = r.height * i / 7f + 4f; var w = r.width * (0.5f - i * 0.05f);
                    p.strokeColor = new Color(1f, 0.95f, 0.8f, 0.5f - i * 0.05f); p.lineWidth = 3f;
                    p.BeginPath(); p.MoveTo(new Vector2(r.width * 0.5f - w * 0.5f, y)); p.LineTo(new Vector2(r.width * 0.5f + w * 0.5f, y)); p.Stroke();
                }
            }, "gb-mgf-reflection");
            MgScenery.Pct(reflect, 66f, 74.5f);
            MgScenery.Size(reflect, 80f, 110f);
            _glints = MgScenery.Painted(scene, (p, r) =>
            {
                p.strokeColor = new Color(0.9f, 0.9f, 1f, 0.35f); p.lineWidth = 2.5f;
                for (var i = 0; i < 9; i++)
                {
                    var x = r.width * MgScenery.Hash(i, 51); var y = r.height * (0.1f + 0.85f * MgScenery.Hash(i, 52));
                    var w = 14f + 18f * MgScenery.Hash(i, 53);
                    p.BeginPath(); p.MoveTo(new Vector2(x - w * 0.5f, y)); p.LineTo(new Vector2(x + w * 0.5f, y)); p.Stroke();
                }
            }, "gb-mgf-glints");
            _glints.name = "mgf-glints";
            MgScenery.Pct(_glints, -4f, 76f, -4f, 2f);
            var reedsL = MgScenery.Reeds(scene, 4, MgScenery.Hex(0x3F3478), MgScenery.Hex(0x6B4F7A), 5);
            MgScenery.Pct(reedsL, -2f, null, null, 0);
            MgScenery.Size(reedsL, 70f, 110f);
            var reedsR = MgScenery.Reeds(scene, 3, MgScenery.Hex(0x3F3478), MgScenery.Hex(0x6B4F7A), 8);
            MgScenery.Pct(reedsR, null, null, -2f, 0);
            MgScenery.Size(reedsR, 56f, 90f);
        }

        private void MoveScenery()
        {
            if (_glints == null) return;
            var t = ReducedMotion ? 0f : _show;
            _glints.style.translate = new Translate(Mathf.Sin(t * 0.8f) * 8f, Mathf.Sin(t * 1.3f) * 2f);
            _twinkles.style.opacity = ReducedMotion ? 0.8f : 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 1.1f));
        }

        public LanternFloatScreen(GameHost host) : base(host, "lanternFloat", "lanternFloat") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0x453A7A);
        protected override Color FieldColor => SkyTop;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public LanternFloatGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new LanternFloatGame(Difficulty, seed);
            _game.RingHit += (pts, gold) =>
            {
                FloatSpark(LanternAt(), gold ? GoldC : RingC, gold ? 18 : 10);
                _glow = gold ? 0.6f : 0.28f;
                StageRing(gold);
                if (_game.Streak >= 3) ComboLabel.text = "×" + _game.Streak;
            };
            _game.RingMissed += () => ComboLabel.text = "";
            _game.FireflyCaught += () => { FloatSpark(LanternAt() + new Vector2(0f, -40f), new Color(0.7f, 1f, 0.45f), 8); _glow = 0.35f; StageFirefly(); };
            _game.Bumped += () =>
            {
                _shake = 0.35f; _bumpFlash = 0.35f; ComboLabel.text = "";
                StageBump();
                Banner(Host.Strings.T("mg.lanternFloat.bump", ("n", _game.Tune.BumpPenalty)), 1.2);
            };
            _game.Finished_ += () =>
            {
                if (!_game.Tune.Endless && _game.Score > 0)
                    Banner(Host.Strings.T("mg.lanternFloat.win", ("hits", _game.Hits)), 2.2);
                RoundOver();
            };
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _pressed = true;
                Field.CapturePointer(e.pointerId);
                Aim(e.localPosition.x);
            });
            Field.RegisterCallback<PointerMoveEvent>(e => { if (_pressed) Aim(e.localPosition.x); });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                _pressed = false;
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _pressed = false);

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new SkyCanvas { Paint = Paint, name = "night-sky" };
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
            Ux.Abs(_statsPill, null, 62);
            _statsPill.style.left = Length.Percent(50);
            _statsPill.style.translate = new Translate(Length.Percent(-50), 0);
            _statsPill.style.alignItems = Align.Center;
            _statsPill.style.paddingTop = 6; _statsPill.style.paddingBottom = 6; _statsPill.style.paddingLeft = 14; _statsPill.style.paddingRight = 16;
            _statsPill.pickingMode = PickingMode.Ignore;
            _statsLabel = Ux.Text("", "gb-md", "gb-bold");
            _statsPill.Add(_statsLabel);
            Root.Add(_statsPill);

            ComboLabel.style.color = new Color(0.96f, 0.96f, 1f);
            Banner(t.T("mg.lanternFloat.intro"), LanternFloatGame.IntroSec + 0.7);
        }

        private void Aim(float viewX)
        {
            if (FieldW <= 1f) return;
            _game?.SteerNormalized((viewX - FieldW * 0.5f) / (FieldW * 0.5f));
        }

        /// <summary>Steer toward a world X (flow tests aim at a ring without sending a pointer).</summary>
        public void AimAtWorldX(double x)
        {
            if (_game == null || _game.Tune.HalfW <= 0.0) return;
            _game.SteerNormalized(x / (_game.Tune.HalfW * _game.Tune.SteerOverreach));
        }

        private float PpuX => FieldW / (float)(_game.Tune.HalfW * 2.2);
        private float PpuY => FieldH / (float)(LanternFloatGame.HalfH * 1.6);
        private float LanternY => FieldH * 0.62f;
        private Vector2 WorldToView(double x, double y) =>
            new Vector2(FieldW * 0.5f + (float)x * PpuX, LanternY - (float)(y - _game.Travel) * PpuY);

        private Vector2 LanternAt() => WorldToView(_game.LanternX, _game.Travel);

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _show += (float)dt;
            _shake = Mathf.Max(0f, _shake - (float)dt);
            _glow = Mathf.Max(0f, _glow - (float)dt);
            _bumpFlash = Mathf.Max(0f, _bumpFlash - (float)dt);
            for (var i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.P += s.V * (float)dt;
                s.V.y += 40f * (float)dt;
                s.Life -= (float)dt;
                if (s.Life <= 0f) _sparks.RemoveAt(i); else _sparks[i] = s;
            }
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var (_, phase) = g.Gust;
            if (phase == "telegraph" && _gustPhase != "telegraph" && g.IntroLeft <= 0.0)
                Banner(t.T("mg.lanternFloat.gust"), 1.1);
            _gustPhase = phase;

            var at = LanternAt();
            var shake = ReducedMotion ? 0f : (Mathf.PerlinNoise(_show * 18f, 0.3f) - 0.5f) * 18f * _shake;
            _gooby.style.left = at.x - 28f + shake;
            _gooby.style.top = at.y - 70f;
            _gooby.style.scale = new Scale(Vector2.one * (_bumpFlash > 0f ? 1.08f : 1f));

            TimeLabel.text = g.Tune.Endless
                ? t.T("mg.lanternFloat.bump_count", ("n", g.Bumps), ("max", g.Tune.EndlessMaxBumps))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            _statsLabel.text = t.T("mg.lanternFloat.stats", ("hits", g.Hits), ("gold", g.Golds));
            if (_stage != null) RenderStage();
            else MoveScenery();
            _canvas.MarkDirtyRepaint();
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            var g = _game;
            if (_stage != null)
            {
                PaintSparks(p);
                return;
            }
            // the dusk sky, moon, stars, hills and the lake with its reflections are the scenery layer behind the canvas

            // clouds then rings then fireflies — back to front, lantern last
            foreach (var ring in g.Rings)
            {
                if (!ring.Cloud || ring.CloudDone && ring.Y < g.Travel) continue;
                var c = WorldToView(ring.CloudX, ring.Y - 0.8);
                if (c.y < -80f || c.y > FieldH + 40f) continue;
                var depth = Mathf.Clamp01((float)((ring.Y - g.Travel) / LanternFloatGame.SpawnLead));
                var s = Mathf.Lerp(1f, 0.55f, depth);
                Ellipse(p, c, 46f * s, 22f * s, new Color(0.82f, 0.84f, 0.92f, 0.82f));
                Ellipse(p, c + new Vector2(-22f * s, 4f * s), 28f * s, 16f * s, new Color(0.74f, 0.76f, 0.86f, 0.8f));
                Ellipse(p, c + new Vector2(24f * s, 2f * s), 26f * s, 15f * s, new Color(0.88f, 0.9f, 0.96f, 0.75f));
            }
            foreach (var ring in g.Rings)
            {
                if (ring.Done) continue;
                var at = WorldToView(ring.X, ring.Y);
                if (at.y < -90f || at.y > FieldH + 40f) continue;
                var depth = Mathf.Clamp01((float)((ring.Y - g.Travel) / LanternFloatGame.SpawnLead));
                var rx = Mathf.Lerp(52f, 22f, depth);
                var ry = Mathf.Lerp(18f, 8f, depth);
                var col = ring.Gold ? GoldC : RingC;
                p.strokeColor = new Color(col.r, col.g, col.b, 0.95f); p.lineWidth = ring.Gold ? 6f : 4f;
                EllipseStroke(p, at, rx, ry);
                p.strokeColor = new Color(col.r, col.g, col.b, 0.35f); p.lineWidth = 10f;
                EllipseStroke(p, at, rx + 4f, ry + 3f);
                if (ring.Gold) Circle(p, at, 5f, GoldC);
            }
            foreach (var ring in g.Rings)
            {
                if (!ring.Firefly || ring.FireflyDone) continue;
                var at = WorldToView(ring.FireflyX, ring.Y - LanternFloatGame.FireflyLead);
                if (at.y < -40f || at.y > FieldH + 20f) continue;
                var pulse = 0.55f + 0.45f * Mathf.Sin(_show * 8f + ring.Index);
                Circle(p, at, 10f, new Color(0.7f, 1f, 0.4f, 0.18f * pulse));
                Circle(p, at, 3.5f, new Color(0.85f, 1f, 0.55f, 0.9f * pulse));
            }

            // gust streaks
            var (gust, phase) = g.Gust;
            if (phase != "idle" && g.IntroLeft <= 0.0)
            {
                var a = phase == "telegraph" ? 0.28f : 0.7f;
                p.strokeColor = new Color(0.8f, 0.92f, 1f, a); p.lineWidth = 3f;
                for (var i = 0; i < 7; i++)
                {
                    var y = FieldH * (0.12f + 0.11f * i);
                    var x0 = FieldW * (gust.Dir > 0 ? 0.1f : 0.9f);
                    var x1 = x0 + gust.Dir * FieldW * 0.22f * (0.6f + 0.4f * Mathf.Sin(_show * 6f + i));
                    Line(p, new Vector2(x0, y), new Vector2(x1, y));
                }
            }

            // the lantern: glow, paper body, rim, basket — Gooby sits in the portrait above
            var L = LanternAt();
            var bounce = Mathf.Sin(_show * 2.4f) * 3f;
            L.y += bounce;
            var glow = 28f + _glow * 22f;
            Circle(p, L, glow, new Color(1f, 0.7f, 0.3f, 0.22f + _glow * 0.35f));
            Ellipse(p, L, 22f, 28f, _bumpFlash > 0f ? new Color(1f, 0.45f, 0.35f) : Paper);
            Ellipse(p, L + new Vector2(-6f, -8f), 8f, 10f, PaperHot);
            Ellipse(p, L + new Vector2(0f, 22f), 14f, 8f, new Color(0.45f, 0.32f, 0.22f));
            p.strokeColor = new Color(0.55f, 0.38f, 0.22f); p.lineWidth = 2f;
            Line(p, L + new Vector2(-10f, 16f), L + new Vector2(-12f, 28f));
            Line(p, L + new Vector2(10f, 16f), L + new Vector2(12f, 28f));
            PaintSparks(p);
        }

        private void PaintSparks(Painter2D p)
        {
            foreach (var s in _sparks)
            {
                var c = s.C; c.a = Mathf.Clamp01(s.Life / s.Max);
                Circle(p, s.P, s.Size, c);
            }
        }

        private void FloatSpark(Vector2 at, Color c, int n)
        {
            for (var i = 0; i < n; i += ParticleStep)
            {
                var a = _show * 5f + i * 0.7f;
                _sparks.Add(new Spark
                {
                    P = at, V = new Vector2(Mathf.Cos(a) * 80f, Mathf.Sin(a) * 80f - 40f),
                    Life = 0.45f, Max = 0.45f, Size = 3f + i % 3, C = c,
                });
            }
        }

        private void Band(Painter2D p, float y0, float y1, Color a, Color b)
        {
            const int steps = 8;
            for (var i = 0; i < steps; i++)
            {
                var t0 = i / (float)steps; var t1 = (i + 1) / (float)steps;
                Poly(p, new[] {
                    new Vector2(0f, Mathf.Lerp(y0, y1, t0)), new Vector2(FieldW, Mathf.Lerp(y0, y1, t0)),
                    new Vector2(FieldW, Mathf.Lerp(y0, y1, t1)), new Vector2(0f, Mathf.Lerp(y0, y1, t1)),
                }, Color.Lerp(a, b, (t0 + t1) * 0.5f));
            }
        }

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
            p.fillColor = color; EllipsePath(p, c, rx, ry); p.Fill();
        }

        private static void EllipseStroke(Painter2D p, Vector2 c, float rx, float ry)
        {
            EllipsePath(p, c, rx, ry); p.Stroke();
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            _game = null;
            _statsPill?.RemoveFromHierarchy();
            _sparks.Clear();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class SkyCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public SkyCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
