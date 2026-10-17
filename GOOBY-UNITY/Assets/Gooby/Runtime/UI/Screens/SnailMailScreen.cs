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
    /// Schneckenpost host on the shared minigame frame (port of snail_mail.gd + its garden diorama): draw a path
    /// from the mailbox to the glowing house, then the post-snail crawls it. Puddles splash; flowers bonus.
    /// Painter2D lawn with three cottages, a red mailbox, puddles and blooms. Gooby waits at the post. Game feel after
    /// snail_mail.gd: chip / confirm / error tones for the stroke, a pink bloom burst + "+1" climbing the scale per
    /// flower, a blue splash with knock + flash + warn buzz in a puddle, sparks + "+N" at the door on delivery (the
    /// perfect tone climbs with the dry streak, confetti from three dry tours in a row). With a GPU the garden is the 3D
    /// stage of <c>SnailMailScreen.Stage3D.cs</c>; the canvas keeps only the stroke being drawn.
    /// </summary>
    public sealed partial class SnailMailScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private SnailMailGame _game;
        private LawnCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel;
        private float _show;
        private bool _pressed;

        private static readonly Color SkyTop = new Color(0.62f, 0.84f, 0.98f);
        private static readonly Color PathC = new Color(0.96f, 0.86f, 0.62f);
        private static readonly Color Puddle = new Color(0.42f, 0.68f, 0.92f, 0.85f);
        private static readonly Color HouseC = new Color(0.96f, 0.82f, 0.68f);
        private static readonly Color Roof = MgScenery.Hex(0xF08A80);
        private static readonly Color Glow = new Color(1f, 0.92f, 0.45f, 0.85f);
        private static readonly Color Flower = new Color(1f, 0.62f, 0.78f);
        private static readonly Color SnailC = new Color(0.62f, 0.42f, 0.28f);
        private static readonly Color Mail = new Color(0.86f, 0.22f, 0.24f);

        public SnailMailScreen(GameHost host) : base(host, "snailMail", "snailMail") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xB8E0A8);
        protected override Color FieldColor => SkyTop;

        public SnailMailGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new SnailMailGame(Difficulty, seed);
            _game.StrokeRejected += why =>
            {
                Banner(Host.Strings.T(why == "post" ? "mg.snailMail.start_at_post" : "mg.snailMail.miss_door"), 1.3);
                PlaySfx("soft_error");
            };
            _game.StrokeBegan += () => PlaySfx("soft_chip", 4f, 0.8f);
            _game.PathCommitted += OnPathCommitted;
            // the flower's "+N" rises from the bloom itself (snail_mail.gd float_text) instead of a banner card
            _game.FlowerTaken += pts => { OnFlower(pts); StageFlower(); };
            _game.Splashed += () => { Banner(Host.Strings.T("mg.snailMail.splash"), 1.4); OnSplashed(); StageSplash(); };
            _game.Delivered += (pts, dry) => { Banner(Host.Strings.T(dry ? "mg.snailMail.delivered" : "mg.snailMail.delivered_wet"), 1.4); OnDelivered(pts, dry); StageDelivered(dry); };
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_perfect", "soft_chip", "soft_error");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _pressed = true;
                Field.CapturePointer(e.pointerId);
                var w = ViewFrom(e.localPosition);
                _game.BeginStroke(w.x, w.y);
            });
            Field.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_pressed || _game == null) return;
                var w = ViewFrom(e.localPosition);
                _game.ExtendStroke(w.x, w.y);
            });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                if (!_pressed || _game == null) return;
                _pressed = false;
                _game.CommitStroke();
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _pressed = false);

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new LawnCanvas { Paint = Paint, name = "lawn" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(52);
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

            Banner(t.T("mg.snailMail.hint"), SnailMailGame.IntroSec + 0.8);
        }

        protected override void TickGame(double dt) { _game.Tick(dt); _show += (float)dt; }

        // ── scenery (board 06, "Garten mit Briefkasten"): a sky strip with a sun and a cloud, round trees over a hedge and
        //    a picket fence, then the garden lawn under the whole diorama (it spans ~13–95 % of the height, so the
        //    cottages stand on grass) with faint clover dots and tufts, a flower border along the bottom and bushes in
        //    the corners — no paths on the lawn (the player draws the path). A layer behind the lawn canvas; built
        //    once, pointer-transparent, nothing moves. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-snailmail");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 12f);
            var sun = MgScenery.Sun(scene, 30f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, 6f, 1.2f);
            var cloud = MgScenery.Cloud(scene, 76f);
            MgScenery.Pct(cloud, 58f, 1f);
            float[] treeAt = { 20f, 70f }; float[] treeH = { 78f, 90f };
            for (var i = 0; i < treeAt.Length; i++)
            {
                var tree = MgScenery.Sprite(scene, "plant_tree_round", treeH[i] * 0.74f, treeH[i]);
                MgScenery.Pct(tree, treeAt[i] - 8f, null, null, 89f);
            }
            var hedge = MgScenery.Puffs(scene, 7, MgScenery.Hex(0x92CF8F), MgScenery.Hex(0xB8E6A8), 53);
            MgScenery.Pct(hedge, -3f, 7f, -3f);
            hedge.style.height = Length.Percent(5.5f);
            var fence = MgScenery.Fence(scene, 9, 30f, MgScenery.Hex(0xF6E4C8), MgScenery.Hex(0xC8A27A));
            MgScenery.Pct(fence, null, 8.6f);
            var lawn = MgScenery.Gradient(scene, MgScenery.Hex(0xB6E2A4), MgScenery.Hex(0x9AD18E), 12f, 100f);
            lawn.name = "mgf-lawn";
            var rim = MgScenery.Shape(scene, MgScenery.Hex(0x8CCF8A), 0f);
            MgScenery.Pct(rim, 0, 12f, 0); rim.style.height = 4f;
            var clover = MgScenery.Dots(scene, 40, new Color(1f, 1f, 1f, 0.22f), 2f, 3.2f, 57);
            MgScenery.Pct(clover, 2f, 15f, 2f, 8f);
            var tuftC = MgScenery.Hex(0x86C98A);
            for (var i = 0; i < 9; i++)
            {
                var tuft = MgScenery.Tuft(scene, tuftC, 0.65f);
                MgScenery.Pct(tuft, 2f + MgScenery.Hash(i, 58) * 94f, 16f + MgScenery.Hash(i, 59) * 72f);
            }
            string[] bloom = { "plant_tulips_red", "plant_daisies", "plant_flowers_pink", "plant_tulips_white", "plant_daisies", "plant_tulips_red" };
            for (var i = 0; i < bloom.Length; i++)
            {
                var b = MgScenery.Sprite(scene, bloom[i], 38f, 36f);
                MgScenery.Pct(b, 1f + i * 16.8f, null, null, -0.5f);
            }
            var bushL = MgScenery.Sprite(scene, "plant_bush", 54f, 48f);
            MgScenery.Pct(bushL, -5f, 12f);
            var bushR = MgScenery.Sprite(scene, "plant_bush", 50f, 46f);
            MgScenery.Pct(bushR, null, 12.5f, -5f);
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var at = WorldToView(g.Tune.PostX, g.Tune.PostY);
            _gooby.style.left = at.x - 26f;
            _gooby.style.top = at.y - 18f;
            if (g.Tune.Endless)
                TimeLabel.text = t.T("mg.snailMail.splash_count", ("n", g.Splashes), ("max", g.Tune.EndlessMaxSplashes));
            else
                TimeLabel.text = t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            _statsLabel.text = t.T("mg.snailMail.stats", ("n", g.Deliveries), ("flowers", g.FlowersTotal));
            ComboLabel.text = g.DryStreak >= 2 ? "×" + g.DryStreak : "";
            if (_stage != null) RenderStage();
            _canvas.MarkDirtyRepaint();
        }

        private float WorldScale()
        {
            var half = (float)_game.Tune.FieldHalfW;
            var spanY = (float)(_game.Tune.FieldYMax - _game.Tune.FieldYMin);
            return Mathf.Min(FieldW * 0.94f / (half * 2f), FieldH * 0.82f / spanY);
        }

        private float FieldCenterY() => FieldH * 0.54f;

        private Vector2 WorldToView(double x, double y)
        {
            var s = WorldScale();
            return new Vector2(FieldW * 0.5f + (float)x * s, FieldCenterY() - (float)y * s);
        }

        private (double x, double y) ViewFrom(Vector2 px)
        {
            var s = Mathf.Max(1f, WorldScale());
            return ((px.x - FieldW * 0.5f) / s, (FieldCenterY() - px.y) / s);
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f) return;
            var g = _game;
            if (_stage != null)
            {
                // over the 3D garden only the stroke under the finger is drawn (the committed path is the sand ribbon)
                if (g.Drawing && g.Raw.Count >= 2)
                {
                    p.strokeColor = new Color(1f, 0.97f, 0.88f, 0.85f); p.lineWidth = 5f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                    p.BeginPath();
                    p.MoveTo(WorldToView(g.Raw[0].X, g.Raw[0].Y));
                    for (var i = 1; i < g.Raw.Count; i++) p.LineTo(WorldToView(g.Raw[i].X, g.Raw[i].Y));
                    p.Stroke();
                }
                return;
            }
            // the sky strip, hedge, fence and the garden lawn are the scenery layer behind the canvas

            var lvl = g.Level;
            for (var i = 0; i < lvl.Houses.Count; i++)
                DrawHouse(p, lvl.Houses[i], i == lvl.TargetIdx);
            foreach (var puddle in lvl.Puddles)
            {
                var at = WorldToView(puddle.X, puddle.Y);
                var r = (float)puddle.R * WorldScale();
                Ellipse(p, at, r, r * 0.72f, Puddle);
                Ellipse(p, at + new Vector2(-r * 0.2f, -r * 0.15f), r * 0.35f, r * 0.22f, new Color(0.72f, 0.88f, 1f, 0.45f));
            }
            for (var i = 0; i < lvl.Flowers.Count; i++)
            {
                if (g.Picked.Contains(i)) continue;
                var f = lvl.Flowers[i];
                var at = WorldToView(f.X, f.Y);
                Ellipse(p, at, 7f, 7f, Flower);
                Ellipse(p, at, 3f, 3f, new Color(1f, 0.92f, 0.45f));
            }

            var mail = WorldToView(g.Tune.PostX, g.Tune.PostY);
            var ms = Mathf.Max(8f, WorldScale() * 0.14f);
            Poly(p, Rect(mail + new Vector2(0f, ms * 0.7f), ms * 0.7f, ms), Mail);
            Poly(p, Rect(mail + new Vector2(0f, -ms * 0.35f), ms * 0.18f, ms * 0.55f), new Color(0.35f, 0.28f, 0.22f));
            Ellipse(p, mail + new Vector2(0f, -ms * 0.15f), ms * 0.35f, ms * 0.28f, new Color(0.96f, 0.9f, 0.55f));

            var pts = PreviewPts();
            if (pts.Count >= 2)
            {
                p.strokeColor = PathC; p.lineWidth = 6f;
                p.BeginPath();
                var a = WorldToView(pts[0].X, pts[0].Y);
                p.MoveTo(a);
                for (var i = 1; i < pts.Count; i++)
                {
                    var b = WorldToView(pts[i].X, pts[i].Y);
                    p.LineTo(b);
                }
                p.Stroke();
            }

            var snail = WorldToView(g.SnailX, g.SnailY);
            var hide = g.Phase == SnailMailGame.PhaseRetreat && (int)(g.RetreatLeft * 8.0) % 2 == 1;
            if (!hide)
            {
                Ellipse(p, snail, 11f, 8f, SnailC);
                Ellipse(p, snail + new Vector2(Mathf.Cos((float)g.SnailAngle) * 8f, -Mathf.Sin((float)g.SnailAngle) * 8f), 5f, 4f, new Color(0.78f, 0.58f, 0.4f));
                Ellipse(p, snail, 6f, 6f, new Color(0.78f, 0.48f, 0.28f));
            }
        }

        private List<SnailMail.Pt> PreviewPts()
        {
            var g = _game;
            if (g.Path != null) return g.Path.Pts;
            if (g.Drawing && g.Raw.Count >= 2)
            {
                var preview = SnailMail.Smooth(g.Raw, g.Tune);
                if (preview != null) return preview.Pts;
            }
            return g.Raw;
        }

        private void DrawHouse(Painter2D p, SnailMail.House h, bool target)
        {
            var at = WorldToView(h.X, h.Y);
            var px = WorldScale();
            var hx = 0.28f * px;
            var hy = 0.32f * px;
            if (target) Ellipse(p, at + new Vector2(0f, -hy * 0.2f), hx * 1.6f, hy * 1.4f, Glow);
            var col = h.Kind == "burrow" ? new Color(0.72f, 0.58f, 0.42f) : HouseC;
            Poly(p, Rect(at, hx, hy), col);
            Poly(p, new[] {
                at + new Vector2(-hx * 1.15f, -hy), at + new Vector2(hx * 1.15f, -hy), at + new Vector2(0f, -hy - 0.28f * px),
            }, target ? new Color(0.95f, 0.72f, 0.28f) : Roof);
            Poly(p, Rect(at + new Vector2(0f, hy * 0.45f), hx * 0.22f, hy * 0.4f), MgScenery.Hex(0xB07A55));
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

        // ── juice (snail_mail.gd _begin_stroke / _commit_stroke / _pick_flowers / _splash / _deliver) ──

        private static readonly Color DryC = new Color(0.55f, 1f, 0.7f);
        private static readonly Color WetC = new Color(0.75f, 0.86f, 1f);
        private static readonly Color FlowerText = new Color(1f, 0.72f, 0.85f);

        private void OnPathCommitted()
        {
            PlaySfx("soft_confirm");
            if (FieldW > 1f) Burst(WorldToView(_game.Tune.PostX, _game.Tune.PostY), PathC, 8);
        }

        private void OnFlower(int points)
        {
            var g = _game;
            PlaySfx("game_pop", 3f + Sfx.ComboSemitones(g.Picked.Count));
            if (FieldW <= 1f || g.Picked.Count == 0) return;
            var f = g.Level.Flowers[g.Picked[g.Picked.Count - 1]];
            var at = WorldToView(f.X, f.Y);
            Burst(at, Flower, 10);
            Float(at + new Vector2(0f, -20f), "+" + points, FlowerText, 24f);
            Haptics.Tap();
        }

        private void OnSplashed()
        {
            PlaySfx("game_miss");
            Haptics.Warn();
            Flash(new Color(0.5f, 0.65f, 0.95f, 0.18f), 0.18f);
            if (!ReducedMotion) Shake(0.3f);
            if (FieldW > 1f) Burst(WorldToView(_game.SnailX, _game.SnailY), Puddle, 16);
        }

        private void OnDelivered(int points, bool dry)
        {
            var g = _game;
            if (dry) PlaySfx("game_perfect", Sfx.ComboSemitones(g.DryStreak));
            if (FieldW <= 1f) return;
            var lvl = g.Level;
            var door = lvl.TargetIdx >= 0 && lvl.TargetIdx < lvl.Houses.Count
                ? SnailMail.DoorOf(lvl.Houses[lvl.TargetIdx], g.Tune)
                : new SnailMail.Pt(g.SnailX, g.SnailY);
            var at = WorldToView(door.X, door.Y);
            Burst(at, dry ? DryC : WetC, dry ? 18 : 10);
            if (dry) Burst(at, Glow, 10);
            Float(at + new Vector2(0f, -30f), "+" + points, dry ? DryC : WetC, 30f);
            if (dry) Flash(new Color(1f, 0.95f, 0.6f, 0.14f), 0.18f);
            if (g.DryStreak >= 3)
            {
                ConfettiRain(50);
                Haptics.Success();
            }
            else Haptics.Tap();
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

        private sealed class LawnCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public LawnCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
