using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Guck-guck-Garten host on the shared minigame frame (port of hide_seek.gd + its garden stage): a hillside
    /// garden painted in perspective, twelve hideouts (bushes, pots, a stump, a well) in a 3×4 grid, Gooby seeking
    /// from the lower third. Critters peek on a seeded schedule; a tap on the right bush finds them.
    /// </summary>
    public sealed partial class HideSeekScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private HideSeekGame _game;
        private GardenCanvas _canvas;
        private readonly List<VisualElement> _spots = new List<VisualElement>();
        private readonly List<VisualElement> _critters = new List<VisualElement>();
        private VisualElement _gooby, _wavePill;
        private Label _waveLabel;
        private float _show;

        private static readonly Color[] CritterColors =
        {
            new Color(1f, 0.66f, 0.78f),
            new Color(0.5f, 0.83f, 0.76f),
            new Color(1f, 0.85f, 0.48f),
            new Color(0.73f, 0.66f, 1f),
            new Color(1f, 0.69f, 0.54f),
        };

        public HideSeekScreen(GameHost host) : base(host, "hideSeek", "hideSeek") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xA8DDB5);
        protected override Color FieldColor => new Color(0.55f, 0.82f, 0.95f);

        /// <summary>The running round (flow tests drive it directly).</summary>
        public HideSeekGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new HideSeekGame(Difficulty, seed);
            _game.Found += (spot, pts) =>
            {
                FloatText(SpotAt(spot), "+" + pts + " " + Host.Strings.T("mg.hideSeek.found"), new Color(0.18f, 0.55f, 0.34f));
                if (_stage != null) StageFound(spot);
            };
            _game.Empty += spot =>
            {
                FloatText(SpotAt(spot), Host.Strings.T("mg.hideSeek.empty"), new Color(0.54f, 0.5f, 0.66f));
                if (_stage != null) StageEmpty(spot);
            };
            _game.WaveCleared += n =>
            {
                Banner(Host.Strings.T("mg.hideSeek.wave_clear", ("n", n)), 1.6);
                if (_stage != null) StageWaveCleared();
            };
            _game.WaveStarted += n => Banner(Host.Strings.T("mg.hideSeek.wave_new", ("n", n)), 1.3);
            _game.WaveExpired += () => Banner(Host.Strings.T("mg.hideSeek.expired"), 1.4);
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new GardenCanvas { Paint = Paint, name = "garden" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            for (var i = 0; i < 12; i++)
            {
                var critter = CritterShape();
                critter.name = "critter-" + i;
                critter.style.display = DisplayStyle.None;
                Field.Add(critter);
                _critters.Add(critter);
            }
            for (var i = 0; i < 12; i++)
            {
                var hit = new VisualElement { pickingMode = PickingMode.Position, name = "spot-" + i };
                hit.style.position = Position.Absolute;
                var spot = i;
                hit.RegisterCallback<PointerDownEvent>(_ => _game?.Tap(spot));
                Field.Add(hit);
                _spots.Add(hit);
            }

            _gooby = Ux.Portrait(96);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;
            else FollowGooby(_gooby, 1.6f, grounded: true);

            _wavePill = Ux.El("gb-card", "gb-row");
            _wavePill.name = "wave-pill";
            Ux.Abs(_wavePill, null, 62);
            _wavePill.style.left = Length.Percent(50);
            _wavePill.style.translate = new Translate(Length.Percent(-50), 0);
            _wavePill.style.alignItems = Align.Center;
            _wavePill.style.paddingTop = 6; _wavePill.style.paddingBottom = 6; _wavePill.style.paddingLeft = 14; _wavePill.style.paddingRight = 16;
            _wavePill.pickingMode = PickingMode.Ignore;
            _waveLabel = Ux.Text("", "gb-md", "gb-bold");
            _wavePill.Add(_waveLabel);
            Root.Add(_wavePill);

            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.hideSeek.intro"), HideSeekGame.IntroSec + 0.7);
        }

        private VisualElement CritterShape()
        {
            var m = new VisualElement { pickingMode = PickingMode.Ignore };
            m.style.position = Position.Absolute;
            m.style.width = 44; m.style.height = 44;
            m.style.borderTopLeftRadius = 22; m.style.borderTopRightRadius = 22;
            m.style.borderBottomLeftRadius = 16; m.style.borderBottomRightRadius = 16;
            foreach (var x in new[] { 12f, 26f })
            {
                var eye = new VisualElement { pickingMode = PickingMode.Ignore };
                eye.style.position = Position.Absolute;
                eye.style.left = x; eye.style.top = 14; eye.style.width = 6; eye.style.height = 6;
                eye.style.borderTopLeftRadius = 3; eye.style.borderTopRightRadius = 3;
                eye.style.borderBottomLeftRadius = 3; eye.style.borderBottomRightRadius = 3;
                eye.style.backgroundColor = new Color(0.15f, 0.12f, 0.12f);
                m.Add(eye);
            }
            return m;
        }

        private Vector2 SpotCentre(int spot)
        {
            var col = spot % 3; var row = spot / 3;
            var cellW = FieldW * 0.9f / 3f;
            var cellH = FieldH * 0.48f / 4f;
            var originX = (FieldW - cellW * 3f) * 0.5f;
            var originY = FieldH * 0.14f;
            return new Vector2(originX + (col + 0.5f) * cellW, originY + (row + 0.55f) * cellH);
        }

        private Vector2 SpotAt(int spot) => SpotCentre(spot) + new Vector2(0f, -24f);

        protected override void TickGame(double dt) { _game.Tick(dt); _show += (float)dt; }

        // ── scenery (board 06, "Garten – Verstecken"): a short sky strip with a sun and two drifting clouds, round
        //    trees over a flowering hedge and a picket fence, then the whole garden lawn (so every hideout of the 3×4
        //    grid, 20–57 % down, sits on grass) with a stepping-stone path, tufts, dot flowers, a tulip bed along the
        //    bottom and bushes in the corners. A layer behind the garden canvas (hideouts, critters, timer stay
        //    painted/added by the host); built once, pointer-transparent; the clouds drift (two translates). ──

        private readonly List<VisualElement> _clouds = new List<VisualElement>();
        private static readonly float[] CloudW = { 90f, 64f };
        private static readonly float[] CloudTop = { 2f, 7f };
        private static readonly float[] CloudSpeed = { 6f, 9f };
        private static readonly float[] CloudPhase = { 40f, 210f };

        private void BuildScenery()
        {
            _clouds.Clear();
            var scene = MgScenery.Layer(Field, "mgf-hideseek");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xFFF1E0), 0f, 18f);
            var sun = MgScenery.Sun(scene, 36f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, null, 2f, 6f);
            for (var i = 0; i < CloudW.Length; i++)
            {
                var cloud = MgScenery.Cloud(scene, CloudW[i]);
                cloud.name = "mgf-cloud-" + i;
                cloud.style.left = 0;
                MgScenery.Pct(cloud, null, CloudTop[i]);
                cloud.style.translate = new Translate(CloudPhase[i], 0);
                _clouds.Add(cloud);
            }
            float[] treeAt = { -4f, 30f, 70f }; float[] treeH = { 96f, 80f, 104f };
            for (var i = 0; i < treeAt.Length; i++)
            {
                var tree = MgScenery.Sprite(scene, "plant_tree_round", treeH[i] * 0.74f, treeH[i]);
                MgScenery.Pct(tree, treeAt[i], null, null, 83f);
            }
            var hedge = MgScenery.Puffs(scene, 7, MgScenery.Hex(0x92CF8F), MgScenery.Hex(0xB8E6A8), 31);
            MgScenery.Pct(hedge, -3f, 12.5f, -3f);
            hedge.style.height = Length.Percent(6.5f);
            var fence = MgScenery.Fence(scene, 9, 34f, MgScenery.Hex(0xF6E4C8), MgScenery.Hex(0xC8A27A));
            MgScenery.Pct(fence, null, 14.2f);
            var lawn = MgScenery.Gradient(scene, MgScenery.Hex(0xC9E9A2), MgScenery.Hex(0xA3D996), 18f, 100f);
            lawn.name = "mgf-lawn";
            var rim = MgScenery.Shape(scene, MgScenery.Hex(0x8CCF8A), 0f);
            MgScenery.Pct(rim, 0, 18f, 0); rim.style.height = 4f;
            var path = MgScenery.Painted(scene, (p, r) =>
            {
                for (var i = 0; i < 11; i++)
                {
                    var k = i / 10f;
                    var at = new Vector2(r.width * (0.5f + Mathf.Sin(k * 5.2f) * 0.16f), r.height * k);
                    MgScenery.Oval(p, at + new Vector2(0f, 2f), 17f, 8f, MgScenery.Hex(0xB9C9A0));
                    MgScenery.Oval(p, at, 16f, 8f, MgScenery.Hex(0xEDE3D0), MgScenery.Hex(0xD6C8AE), 2f);
                }
            }, "gb-mgf-stones");
            MgScenery.Pct(path, 0, 20f, 0, 4f);
            var tuftC = MgScenery.Hex(0x7CC486);
            for (var i = 0; i < 10; i++)
            {
                var tuft = MgScenery.Tuft(scene, tuftC, 0.7f + (i % 3) * 0.12f);
                MgScenery.Pct(tuft, 3f + MgScenery.Hash(i, 61) * 90f, 22f + MgScenery.Hash(i, 62) * 66f);
            }
            var petals = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFFD166), MgScenery.Hex(0xB7D8FF) };
            for (var i = 0; i < 8; i++)
            {
                var f = MgScenery.Flower(scene, petals[i % petals.Length], 0.75f);
                MgScenery.Pct(f, 4f + MgScenery.Hash(i, 71) * 90f, 24f + MgScenery.Hash(i, 72) * 62f);
            }
            var bed = MgScenery.Shape(scene, MgScenery.Hex(0xB98B6A), 0f, "gb-mgf-bed");
            MgScenery.Pct(bed, -4f, null, -4f, 0);
            bed.style.height = Length.Percent(5f);
            MgScenery.RoundTop(bed, 22f);
            bed.style.borderTopWidth = 4; bed.style.borderTopColor = MgScenery.Hex(0xD6A987);
            string[] bloom = { "plant_tulips_red", "plant_daisies", "plant_tulips_white", "plant_flowers_pink", "plant_tulips_red", "plant_daisies" };
            for (var i = 0; i < bloom.Length; i++)
            {
                var b = MgScenery.Sprite(scene, bloom[i], 40f, 42f);
                MgScenery.Pct(b, 2f + i * 16.5f, null, null, 2.2f);
            }
            var bushL = MgScenery.Sprite(scene, "plant_bush", 64f, 58f);
            MgScenery.Pct(bushL, -5f, null, null, 7f);
            var bushR = MgScenery.Sprite(scene, "plant_bush", 58f, 54f);
            MgScenery.Pct(bushR, null, null, -5f, 7f);
        }

        private void DriftClouds()
        {
            if (FieldW <= 0f) return;
            var t = ReducedMotion ? 0f : _show;
            for (var i = 0; i < _clouds.Count; i++)
                _clouds[i].style.translate = new Translate(Mathf.Repeat(CloudPhase[i] + t * CloudSpeed[i], FieldW + CloudW[i]) - CloudW[i], 0);
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var cellW = FieldW * 0.9f / 3f;
            var cellH = FieldH * 0.48f / 4f;
            for (var i = 0; i < 12; i++)
            {
                var at = SpotCentre(i);
                var shake = (float)g.Shake[i] * 8f * Mathf.Sin(_show * 40f);
                _spots[i].style.left = at.x - cellW * 0.45f + shake;
                _spots[i].style.top = at.y - cellH * 0.55f;
                _spots[i].style.width = cellW * 0.9f;
                _spots[i].style.height = cellH * 0.95f;

                var rise = (float)g.RiseOf(i);
                var shown = rise > 0.04f && _stage == null;
                _critters[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (shown)
                {
                    var color = g.Critters.TryGetValue(i, out var c) ? CritterColors[c.Color] : CritterColors[0];
                    _critters[i].style.backgroundColor = color;
                    _critters[i].style.left = at.x - 22f + shake;
                    _critters[i].style.top = at.y - 18f - rise * 36f;
                    _critters[i].style.opacity = 0.35f + rise * 0.65f;
                }
            }
            _gooby.style.left = FieldW * 0.5f - 48f;
            _gooby.style.top = FieldH * 0.78f;
            TimeLabel.text = g.Tune.Endless
                ? t.T("mg.hideSeek.expired_count", ("n", g.Expired), ("max", g.Tune.EndlessMaxExpired))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            _waveLabel.text = t.T("mg.hideSeek.wave", ("n", g.Wave + 1), ("left", g.HiddenCount));
            if (_stage != null) RenderStage();
            else DriftClouds();
            _canvas.MarkDirtyRepaint();
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            // the sky, trees, hedge, fence and the garden lawn are the scenery layer behind the canvas (or the 3D garden,
            // which brings its own hideouts); hideouts: bushes, a pot, a stump — sit on the hill in the 3×4 grid
            for (var i = 0; i < 12 && _stage == null; i++)
            {
                var at = SpotCentre(i);
                var kind = i % 3;
                if (kind == 0)
                {
                    Ellipse(p, at + new Vector2(0f, 8f), 36f, 22f, MgScenery.Hex(0x6DB478));
                    Ellipse(p, at + new Vector2(-16f, 4f), 22f, 16f, MgScenery.Hex(0x9ED499));
                    Ellipse(p, at + new Vector2(16f, 6f), 20f, 15f, MgScenery.Hex(0x7CC486));
                }
                else if (kind == 1)
                {
                    Poly(p, new[] { at + new Vector2(-16f, 18f), at + new Vector2(16f, 18f), at + new Vector2(13f, -4f), at + new Vector2(-13f, -4f) }, MgScenery.Hex(0xF4A38C));
                    Ellipse(p, at + new Vector2(0f, -10f), 20f, 10f, MgScenery.Hex(0x8CCB86));
                }
                else
                {
                    Ellipse(p, at + new Vector2(0f, 10f), 22f, 16f, MgScenery.Hex(0xC9A07A));
                    Ellipse(p, at + new Vector2(0f, 4f), 18f, 10f, MgScenery.Hex(0xE8CBA4));
                    Ellipse(p, at + new Vector2(0f, 4f), 9f, 5f, MgScenery.Hex(0xD2AE84));
                }
            }
            // wave timer under the HUD
            var frac = Mathf.Max(0f, 1f - (float)(_game.WaveT / Math.Max(0.001, _game.WaveSec)));
            if (_game.Phase != "play") frac = 0f;
            var urgent = frac < 0.3f && _game.Phase == "play";
            var barW = FieldW * 0.72f;
            var barH = 10f + (urgent ? 2f + 2f * Mathf.Sin(_show * 9f) : 0f);
            var bx = (FieldW - barW) * 0.5f;
            var by = FieldH * 0.115f;
            Poly(p, new[] { new Vector2(bx - 2f, by - 2f), new Vector2(bx + barW + 2f, by - 2f), new Vector2(bx + barW + 2f, by + barH + 2f), new Vector2(bx - 2f, by + barH + 2f) }, new Color(0.24f, 0.36f, 0.22f, 0.45f));
            Poly(p, new[] { new Vector2(bx, by), new Vector2(bx + barW, by), new Vector2(bx + barW, by + barH), new Vector2(bx, by + barH) }, new Color(1f, 1f, 1f, 0.55f));
            Poly(p, new[] { new Vector2(bx, by), new Vector2(bx + barW * frac, by), new Vector2(bx + barW * frac, by + barH), new Vector2(bx, by + barH) },
                urgent ? new Color(0.96f, 0.55f, 0.43f) : new Color(0.35f, 0.79f, 0.73f));
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

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

        private static void Poly(Painter2D p, Vector2[] pts, Color color)
        {
            p.fillColor = color;
            p.BeginPath(); p.MoveTo(pts[0]);
            for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath(); p.Fill();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b) { p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }

        private static void EllipsePath(Painter2D p, Vector2 c, float rx, float ry)
        {
            p.BeginPath();
            for (var i = 0; i <= 24; i++)
            {
                var a = Mathf.PI * 2f * i / 24f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
        }

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color; EllipsePath(p, c, rx, ry); p.Fill();
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            _game = null;
            _wavePill?.RemoveFromHierarchy();
            _spots.Clear(); _critters.Clear();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class GardenCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public GardenCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
