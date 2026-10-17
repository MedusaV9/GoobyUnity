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
    /// Hafen-Hüpfer (harborHopper) host on the shared minigame frame — port of harbor_hopper.gd as a pastel harbour
    /// seen from behind the cutter: the canal recedes to a skyline under a morning sky, quays with bollards and
    /// harbour houses on both banks, piers reaching in, foam wave bands rolling towards the boat with a glittering
    /// sweet spot. Drag to steer (the finger's position is the target lane), tap to sound the horn. Game feel after
    /// harbor_hopper.gd (no screen shake, motion comfort): crate/ring pops with a climbing tone and a ring at the bow,
    /// a spray + red flash on a bump, the boost chain climbs in pitch, the horn cone flashes ahead, the gull circles
    /// the cutter while it warns and flaps off with a crate.
    /// </summary>
    public sealed partial class HarborHopperScreen : MinigameHostScreen
    {
        public const double ViewW = 844.0, ViewH = 390.0;

        private HarborHopperGame _game;
        private HarborCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel;
        private Vector2 _from;
        private bool _pressed, _moved;
        private double _hornFlash, _wake, _gullT;
        private string _gullMode = "";
        private int _shownCrates = -1, _shownHorn = -1;

        private const float HorizonPct = 0.36f, NearPct = 0.86f, DepthK = 9f;

        private static readonly Color WaterNear = MgScenery.Hex(0x7FC9D9);
        private static readonly Color WaterFar = MgScenery.Hex(0xB7E3EC);
        private static readonly Color Foam = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color QuayTop = MgScenery.Hex(0xF4E9D8);
        private static readonly Color QuaySide = MgScenery.Hex(0xD8C8B2);
        private static readonly Color Bollard = MgScenery.Hex(0x6B5A4E);
        private static readonly Color Plank = MgScenery.Hex(0xC9935F);
        private static readonly Color PlankDark = MgScenery.Hex(0xA8764A);
        private static readonly Color CrateC = MgScenery.Hex(0xE0A867);
        private static readonly Color CrateDark = MgScenery.Hex(0xB97D45);
        private static readonly Color RingRed = MgScenery.Hex(0xF26D6D);
        private static readonly Color BuoyRed = MgScenery.Hex(0xEF5F68);
        private static readonly Color Hull = MgScenery.Hex(0x4F86C6);
        private static readonly Color HullTrim = MgScenery.Hex(0xFFF6EC);
        private static readonly Color Cabin = MgScenery.Hex(0xFFE7C2);
        private static readonly Color Shadow = new Color(0.1f, 0.3f, 0.4f, 0.2f);
        private static readonly Color Meadow = MgScenery.Hex(0xB5DDA0);
        private static readonly Color MeadowFar = MgScenery.Hex(0xCDE9BE);
        private static readonly Color Door = MgScenery.Hex(0xB07A55);
        private static readonly Color Tree = MgScenery.Hex(0x79C383);
        private static readonly Color TreeShine = MgScenery.Hex(0xA9DD9A);
        private static readonly Color[] Facades = { MgScenery.Hex(0xFFD8C2), MgScenery.Hex(0xCFE8FF), MgScenery.Hex(0xFFF0B8), MgScenery.Hex(0xD8F0D2), MgScenery.Hex(0xF4D2EC) };
        private static readonly Color[] RoofsC = { MgScenery.Hex(0xE98C7E), MgScenery.Hex(0x7FA8D8), MgScenery.Hex(0xE6B15A), MgScenery.Hex(0x7CC39A), MgScenery.Hex(0xC69BD8) };

        public HarborHopperScreen(GameHost host) : base(host, "harborHopper", "harborHopper") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE3F0);
        protected override Color FieldColor => MgScenery.Hex(0xCDEBF7);

        public HarborHopperGame Game => _game;

        protected override void StartGame(int seed)
        {
            var t = Host.Strings;
            _game = new HarborHopperGame(Difficulty, seed);
            _game.Crate += item => { OnPickup(item, true); StageMood("ecstatic", true); };
            _game.Ring += item => { OnPickup(item, false); StageMood("happy", true); };
            _game.Bumped += what => { Banner(t.T("mg.harborHopper.bump"), 1.2); OnBump(); StageBump(); };
            _game.Boosted += chain =>
            {
                Banner(chain > 1 ? t.T("mg.harborHopper.boost_chain", ("n", chain)) : t.T("mg.harborHopper.boost"), 1.3);
                PlaySfx("game_combo", Sfx.ComboSemitones(chain));
                Haptics.Tap();
                if (FieldW > 1f) Burst(BoatAt() + new Vector2(0f, -10f), Foam, 16);
            };
            _game.BuoysCleared += n => { _hornFlash = 0.5; Banner(t.T("mg.harborHopper.horn", ("n", n)), 1.4); PlaySfx("game_whoosh", -2f); Haptics.Tap(); };
            _game.HornEmpty += () => { Banner(t.T("mg.harborHopper.horn_empty"), 1.2); PlaySfx("soft_error"); };
            _game.GullWarned += () => { _gullMode = "circle"; _gullT = 0.0; Banner(t.T("mg.harborHopper.gull_warn"), 1.6); PlaySfx("game_hit", 6f, 0.8f); };
            _game.GullStole += () =>
            {
                _gullMode = "leave"; _gullT = 0.0;
                Banner(t.T("mg.harborHopper.gull_steal"), 1.6);
                PlaySfx("game_lose", 0f, 0.8f);
                Flash(new Color(0.9f, 0.32f, 0.22f, 0.14f), 0.16f);
                if (FieldW > 1f) Float(BoatAt() + new Vector2(0f, -70f), "−" + _game.Tune.CratePoints, new Color(1f, 0.42f, 0.42f), 28f);
            };
            _game.GullLeft += () => { if (_gullMode == "circle") { _gullMode = "leave"; _gullT = 0.0; } };
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_combo");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _pressed = true;
                _moved = false;
                _from = e.localPosition;
                Field.CapturePointer(e.pointerId);
                _game.SetTarget(SteerFrom(e.localPosition.x));
            });
            Field.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_pressed || _game == null) return;
                if (((Vector2)e.localPosition - _from).magnitude > 10f) _moved = true;
                _game.SetTarget(SteerFrom(e.localPosition.x));
            });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                if (_pressed && !_moved) _game?.QueueHorn();
                _pressed = false;
                _game?.ReleaseTarget();
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => { _pressed = false; _game?.ReleaseTarget(); });

            if (World.MgStage3D.Supported) BuildStage();
            else
            {
                var scene = MgScenery.Layer(Field, "mgf-harborhopper");
                MgScenery.Gradient(scene, MgScenery.Hex(0x9FD3F5), MgScenery.Hex(0xFFF0DF), 0f, HorizonPct * 100f + 1f);
                var sun = MgScenery.Sun(scene, 40f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
                MgScenery.Pct(sun, 78f, 5f);
                foreach (var (l, top, w) in new[] { (12f, 6f, 110f), (46f, 12f, 80f), (64f, 4f, 70f) })
                {
                    var cloud = MgScenery.Cloud(scene, w);
                    MgScenery.Pct(cloud, l, top);
                }
            }

            _canvas = new HarborCanvas { Paint = Paint, name = "harbor" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(40);
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
            _statsLabel.name = "harbor-stats";
            _statsPill.Add(_statsLabel);
            Root.Add(_statsPill);

            Banner(t.T("mg.harborHopper.hint"), HarborHopperGame.IntroSec + 1.2);
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _hornFlash = Math.Max(0.0, _hornFlash - dt);
            _wake += HarborHopper.SpeedOf(_game.Engine.S, _game.Tune) * dt;
            if (_gullMode != "") _gullT += dt;
            if (_gullMode == "leave" && _gullT > 1.6) _gullMode = "";
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var s = g.Engine.S;
            var boat = BoatAt();
            _gooby.style.left = boat.x - 20f;
            _gooby.style.top = boat.y - 22f - 50f * Mathf.Max(1f, FieldH / 300f);
            TimeLabel.text = g.Tune.Endless
                ? t.T("mg.harborHopper.bump_count", ("n", s.Bumps), ("max", g.Tune.EndlessBumpLimit))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            if (_shownCrates != s.Crates || _shownHorn != s.HornCharges)
            {
                _shownCrates = s.Crates;
                _shownHorn = s.HornCharges;
                _statsLabel.text = t.T("mg.harborHopper.stats", ("crates", s.Crates), ("horn", s.HornCharges));
            }
            ComboLabel.text = g.Streak >= 3 ? "×" + g.Streak : "";
            if (_stage != null) { RenderStage(); return; }
            _canvas.MarkDirtyRepaint();
        }

        private double SteerFrom(float px) => Mathf.Clamp(px / Mathf.Max(1f, FieldW) * 2f - 1f, -1f, 1f);

        // ── projection: z ahead of the boat recedes to the horizon; the canal narrows with the same factor ──

        private float Factor(double relZ) => DepthK / (DepthK + Mathf.Max(-3f, (float)relZ));

        private float Horizon => FieldH * HorizonPct;
        private float Near => FieldH * NearPct;
        private float HalfNearPx => FieldW * 0.34f;

        private Vector2 Project(double x, double relZ, double lift = 0.0)
        {
            if (_stage != null) return _stage.ToField(H(x, relZ, (float)lift), FieldW, FieldH);
            var f = Factor(relZ);
            var y = Horizon + (Near - Horizon) * f - (float)lift * f * FieldH * 0.08f;
            var half = HalfNearPx * f;
            return new Vector2(FieldW * 0.5f + (float)(x / _game.Tune.ChannelHalfW) * half, y);
        }

        private Vector2 BoatAt() => Project(_game.Engine.S.X, 0.0, 0.6);

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            var g = _game;
            var s = g.Engine.S;
            var half = g.Tune.ChannelHalfW;
            const double far = 70.0;

            // skyline on the horizon: pastel harbour houses across the whole width
            for (var i = 0; i < 14; i++)
            {
                var w = FieldW / 13f;
                var x0 = i * w - w * 0.3f;
                var h = 18f + (i * 37 % 5) * 5f;
                Poly(p, new[] { new Vector2(x0, Horizon), new Vector2(x0 + w * 0.9f, Horizon), new Vector2(x0 + w * 0.9f, Horizon - h), new Vector2(x0, Horizon - h) }, Color.Lerp(Facades[i % Facades.Length], Color.white, 0.35f));
                Poly(p, new[] { new Vector2(x0 - 2f, Horizon - h), new Vector2(x0 + w * 0.92f, Horizon - h), new Vector2(x0 + w * 0.45f, Horizon - h - 10f) }, Color.Lerp(RoofsC[i % RoofsC.Length], Color.white, 0.3f));
            }

            // the land on both banks runs to the horizon: a meadow from light far to deeper near
            Poly(p, new[] { new Vector2(0f, Horizon), new Vector2(FieldW, Horizon), new Vector2(FieldW, FieldH), new Vector2(0f, FieldH) }, Meadow);
            Poly(p, new[] { new Vector2(0f, Horizon), new Vector2(FieldW, Horizon), new Vector2(FieldW, Horizon + 10f), new Vector2(0f, Horizon + 10f) }, MeadowFar);

            // water: depth bands from the far light teal to the near deeper one
            const int bands = 10;
            for (var i = 0; i < bands; i++)
            {
                var z0 = far * (1.0 - i / (double)bands);
                var z1 = far * (1.0 - (i + 1) / (double)bands);
                var a = Project(-half - 0.6, z0); var b = Project(half + 0.6, z0);
                var c = Project(half + 0.6, z1 - (i == bands - 1 ? 4.0 : 0.0)); var d = Project(-half - 0.6, z1 - (i == bands - 1 ? 4.0 : 0.0));
                Poly(p, new[] { a, b, c, d }, Color.Lerp(WaterFar, WaterNear, (i + 0.5f) / bands));
            }
            // drifting sparkle dashes on the water (wake-relative, so they stream past)
            for (var i = 0; i < 18; i++)
            {
                var z = Mod(i * 4.1 - _wake, far);
                var x = ((i * 53) % 11 / 10.0 * 2.0 - 1.0) * (half - 0.4);
                var at = Project(x, z);
                var f = Factor(z);
                Poly(p, Rect(at, 9f * f, 1.4f * f), new Color(1f, 1f, 1f, 0.45f));
            }

            PaintQuay(p, -1, half, far);
            PaintQuay(p, 1, half, far);

            // piers, waves, items — far to near so nearer ones paint over
            foreach (var pier in g.Engine.Piers)
            {
                var rel = pier.Z - s.Z;
                if (rel < -3.0 || rel > far) continue;
                var inner = half - g.Tune.PierReachM;
                var x0 = pier.Side * (half + 0.6);
                var x1 = pier.Side * inner;
                var d = g.Tune.PierDepthM * 0.5;
                Poly(p, new[] { Project(x0, rel + d, 0.25), Project(x1, rel + d, 0.25), Project(x1, rel - d, 0.25), Project(x0, rel - d, 0.25) }, pier.Hit ? PlankDark : Plank);
                Poly(p, new[] { Project(x0, rel - d, 0.25), Project(x1, rel - d, 0.25), Project(x1, rel - d, 0.0), Project(x0, rel - d, 0.0) }, PlankDark);
                var post = Project(x1, rel - d, 0.0);
                var f = Factor(rel);
                Poly(p, Rect(post + new Vector2(0f, -6f * f), 3f * f, 9f * f), Bollard);
            }

            foreach (var wave in g.Engine.Waves)
            {
                var rel = wave.Z - s.Z;
                if (rel < -2.0 || rel > far) continue;
                var f = Factor(rel);
                var a = Project(-half, rel); var b = Project(half, rel);
                var crest = 4f * f;
                Poly(p, new[] { a + new Vector2(0f, -crest), b + new Vector2(0f, -crest), b + new Vector2(0f, crest), a + new Vector2(0f, crest) }, wave.Ridden ? new Color(1f, 1f, 1f, 0.35f) : Foam);
                var sw = Project(wave.SweetX, rel);
                var swHalf = (float)(g.Tune.SweetHalfW / half) * HalfNearPx * f;
                var glint = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 8f);
                Poly(p, new[] { sw + new Vector2(-swHalf, -crest * 1.6f), sw + new Vector2(swHalf, -crest * 1.6f), sw + new Vector2(swHalf, crest), sw + new Vector2(-swHalf, crest) },
                    wave.Ridden ? new Color(1f, 0.95f, 0.6f, 0.25f) : new Color(1f, 0.93f, 0.55f, 0.55f + 0.3f * glint));
            }

            var drawn = new List<HarborHopper.Item>();
            foreach (var item in g.Engine.Items)
                if (!item.Gone && item.Z - s.Z > -3.0 && item.Z - s.Z <= far) drawn.Add(item);
            drawn.Sort((x, y) => y.Z.CompareTo(x.Z));
            foreach (var item in drawn) PaintItem(p, item, item.Z - s.Z);

            // horn cone flash ahead of the bow
            if (_hornFlash > 0.0)
            {
                var a = (float)(_hornFlash / 0.5) * 0.45f;
                var bx = s.X;
                Poly(p, new[]
                {
                    Project(bx - g.Tune.HornConeBase, 0.0), Project(bx + g.Tune.HornConeBase, 0.0),
                    Project(bx + g.Tune.HornConeBase + g.Tune.HornConeM * g.Tune.HornConeSpread, g.Tune.HornConeM),
                    Project(bx - g.Tune.HornConeBase - g.Tune.HornConeM * g.Tune.HornConeSpread, g.Tune.HornConeM),
                }, new Color(1f, 0.92f, 0.5f, a));
            }

            PaintBoat(p, s);
            if (_gullMode != "") PaintGull(p);
        }

        private void PaintQuay(Painter2D p, int side, double half, double far)
        {
            var edge = side * (half + 0.6);
            var outer = side * (half + 2.0);
            Poly(p, new[] { Project(edge, far, 0.3), Project(outer, far, 0.3), Project(outer, -4.0, 0.3), Project(edge, -4.0, 0.3) }, QuayTop);
            Poly(p, new[] { Project(edge, far, 0.3), Project(edge, -4.0, 0.3), Project(edge, -4.0, 0.0), Project(edge, far, 0.0) }, QuaySide);
            var s = _game.Engine.S;
            // harbour houses every 9 m (far first, so nearer ones overlap), with a round tree between every other pair
            for (var k = 9; k >= 0; k--)
            {
                var zi = Mod(k * 9.0 + (side > 0 ? 4.5 : 0.0) - s.Z, 90.0) - 4.0;
                if (zi > far || zi < -3.5) continue;
                var f = Factor(zi);
                var idx = (int)Math.Floor((s.Z + zi + 4.0) / 9.0) + (side > 0 ? 3 : 0);
                var v = ((idx % Facades.Length) + Facades.Length) % Facades.Length;
                var baseL = Project(side * (half + 2.3), zi, 0.3);
                var w = 92f * f;
                var h = (96f + (idx & 3) * 16f) * f;
                var x0 = side > 0 ? baseL.x : baseL.x - w;
                Poly(p, new[] { new Vector2(x0, baseL.y), new Vector2(x0 + w, baseL.y), new Vector2(x0 + w, baseL.y - h), new Vector2(x0, baseL.y - h) }, Facades[v]);
                Poly(p, new[] { new Vector2(x0 - 6f * f, baseL.y - h), new Vector2(x0 + w + 6f * f, baseL.y - h), new Vector2(x0 + w * 0.5f, baseL.y - h - 34f * f) }, RoofsC[v]);
                for (var row = 0; row < 2; row++)
                for (var col = 0; col < 2; col++)
                    Poly(p, Rect(new Vector2(x0 + w * (0.3f + col * 0.4f), baseL.y - h * (0.42f + row * 0.32f)), 9f * f, 10f * f), new Color(0.78f, 0.9f, 1f));
                Poly(p, Rect(new Vector2(x0 + w * 0.5f, baseL.y - 13f * f), 8f * f, 13f * f), Door);
                if ((idx & 1) == 0)
                {
                    var tree = new Vector2(side > 0 ? x0 - 10f * f : x0 + w + 10f * f, baseL.y - 26f * f);
                    Poly(p, Rect(tree + new Vector2(0f, 16f * f), 3f * f, 10f * f), Door);
                    Ellipse(p, tree, 22f * f, 24f * f, Tree);
                    Ellipse(p, tree + new Vector2(-6f * f, -8f * f), 8f * f, 7f * f, TreeShine);
                }
            }
            // bollards every 8 m along the quay edge
            for (var i = 0; i < 10; i++)
            {
                var z = Mod(i * 8.0 - s.Z, 80.0) - 4.0;
                if (z > far) continue;
                var f = Factor(z);
                var at = Project(edge + side * 0.3, z, 0.3);
                Poly(p, Rect(at + new Vector2(0f, -5f * f), 4.5f * f, 6.5f * f), Bollard);
                Ellipse(p, at + new Vector2(0f, -11f * f), 5.5f * f, 2.2f * f, Bollard);
            }
        }

        private void PaintItem(Painter2D p, HarborHopper.Item item, double rel)
        {
            var f = Factor(rel);
            var at = Project(item.X, rel, 0.15);
            var bob = Mathf.Sin(Time.unscaledTime * 3f + (float)item.Z) * 1.5f * f;
            Ellipse(p, Project(item.X, rel) + new Vector2(0f, 2f * f), 15f * f, 4f * f, Shadow);
            switch (item.Type)
            {
                case "crate":
                {
                    var c = at + new Vector2(0f, -10f * f + bob);
                    var r = 11f * f;
                    Poly(p, Rect(c, r, r), CrateC);
                    Poly(p, Rect(c + new Vector2(0f, -r * 0.55f), r, r * 0.12f), CrateDark);
                    Poly(p, Rect(c + new Vector2(0f, r * 0.55f), r, r * 0.12f), CrateDark);
                    Poly(p, Rect(c, r * 0.12f, r), CrateDark);
                    break;
                }
                case "ring":
                {
                    var c = at + new Vector2(0f, -4f * f + bob);
                    var r = 12f * f;
                    Ellipse(p, c, r, r * 0.55f, Color.white);
                    for (var k = 0; k < 4; k++)
                    {
                        var a0 = k * Mathf.PI * 0.5f + 0.4f;
                        Ellipse(p, c + new Vector2(Mathf.Cos(a0) * r * 0.8f, Mathf.Sin(a0) * r * 0.44f), r * 0.24f, r * 0.2f, RingRed);
                    }
                    Ellipse(p, c, r * 0.5f, r * 0.26f, WaterNear);
                    break;
                }
                default:
                {
                    var c = at + new Vector2(0f, bob);
                    var r = 10f * f;
                    Poly(p, new[] { c + new Vector2(-r, 0f), c + new Vector2(r, 0f), c + new Vector2(r * 0.35f, -r * 2.2f), c + new Vector2(-r * 0.35f, -r * 2.2f) }, BuoyRed);
                    Poly(p, new[] { c + new Vector2(-r * 0.75f, -r * 0.8f), c + new Vector2(r * 0.75f, -r * 0.8f), c + new Vector2(r * 0.6f, -r * 1.3f), c + new Vector2(-r * 0.6f, -r * 1.3f) }, Color.white);
                    Ellipse(p, c + new Vector2(0f, -r * 2.5f), r * 0.4f, r * 0.4f, MgScenery.Hex(0xFFD166));
                    break;
                }
            }
        }

        private void PaintBoat(Painter2D p, HarborHopper.State s)
        {
            var at = Project(s.X, 0.0);
            var tilt = Mathf.Clamp((float)s.Vx * 0.08f, -0.25f, 0.25f);
            var blink = s.IframesT > 0.0 && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.4f;
            // wake: two foam trails fanning back towards the viewer
            var wk = Mathf.Max(1f, FieldH / 300f);
            for (var side = -1; side <= 1; side += 2)
                Poly(p, new[] { at + new Vector2(side * 20f, -4f) * wk, at + new Vector2(side * 26f, -2f) * wk, at + new Vector2(side * 60f, 30f) * wk, at + new Vector2(side * 48f, 30f) * wk }, new Color(1f, 1f, 1f, 0.6f));
            var k = Mathf.Max(1f, FieldH / 300f);
            Vector2 R(float x, float y) => at + new Vector2(x + y * tilt, y - x * tilt * 0.3f) * k;
            Ellipse(p, at + new Vector2(0f, 6f * k), 44f * k, 10f * k, Shadow);
            Poly(p, new[] { R(-42f, -8f), R(42f, -8f), R(34f, 10f), R(-34f, 10f) }, blink ? Color.white : Hull);
            Poly(p, new[] { R(-42f, -8f), R(42f, -8f), R(40f, -14f), R(-40f, -14f) }, HullTrim);
            Poly(p, new[] { R(-18f, -14f), R(18f, -14f), R(15f, -44f), R(-15f, -44f) }, Cabin);
            Poly(p, new[] { R(-20f, -44f), R(20f, -44f), R(17f, -50f), R(-17f, -50f) }, RingRed);
            Poly(p, new[] { R(-10f, -36f), R(10f, -36f), R(10f, -26f), R(-10f, -26f) }, new Color(0.7f, 0.86f, 0.98f));
            // crates stacked on deck
            var n = Math.Min(4, s.Crates);
            for (var i = 0; i < n; i++)
                Poly(p, Rect(R(28f - (i % 2) * 9f, -18f - (i / 2) * 8f), 4.5f * k, 4f * k), CrateC);
            if (s.BoostT > 0.0)
                for (var j = 0; j < 3; j++) Poly(p, Rect(at + new Vector2(-30f + j * 30f, 18f + j % 2 * 4f) * k, 8f * k, 1.5f * k), new Color(1f, 0.93f, 0.55f, 0.85f));
        }

        private void PaintGull(Painter2D p)
        {
            var boat = BoatAt();
            Vector2 at;
            if (_gullMode == "circle")
            {
                var a = (float)_gullT * 3f;
                at = boat + new Vector2(Mathf.Cos(a) * 50f, -110f + Mathf.Sin(a) * 14f);
                Poly(p, Rect(at + new Vector2(0f, -26f), 2.5f, 7f), new Color(0.95f, 0.36f, 0.4f));
                Poly(p, Rect(at + new Vector2(0f, -15f), 2.5f, 2.5f), new Color(0.95f, 0.36f, 0.4f));
            }
            else
            {
                var u = Mathf.Clamp01((float)_gullT / 1.6f);
                at = boat + new Vector2(u * FieldW * 0.6f, -110f - u * 120f);
                Poly(p, Rect(at + new Vector2(0f, 10f), 4.5f, 4f), CrateC);
            }
            var flap = Mathf.Sin(Time.unscaledTime * 16f) * 7f;
            Poly(p, new[] { at + new Vector2(-22f, -flap), at + new Vector2(0f, 2f), at + new Vector2(-4f, 6f) }, Color.white);
            Poly(p, new[] { at + new Vector2(22f, -flap), at + new Vector2(0f, 2f), at + new Vector2(4f, 6f) }, Color.white);
            Ellipse(p, at + new Vector2(0f, 3f), 6f, 4f, Color.white);
            Poly(p, new[] { at + new Vector2(5f, 2f), at + new Vector2(11f, 4f), at + new Vector2(5f, 5f) }, MgScenery.Hex(0xFFB347));
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

        private static double Mod(double v, double m)
        {
            var r = v % m;
            return r < 0.0 ? r + m : r;
        }

        // ── juice (harbor_hopper.gd _handle_event — no shake) ──

        private void OnPickup(HarborHopper.Item item, bool crate)
        {
            var g = _game;
            PlaySfx(crate ? "game_coin" : "game_pop", Sfx.ComboSemitones(g.Streak));
            Haptics.Tap();
            if (FieldW <= 1f) return;
            var at = BoatAt() + new Vector2(0f, -30f);
            var color = crate ? new Color(1f, 0.82f, 0.4f) : new Color(0.54f, 0.88f, 0.82f);
            Burst(at, color, 12);
            Float(at + new Vector2(0f, -20f), "+" + (crate ? g.Tune.CratePoints : g.Tune.RingPoints), color, 26f);
            ScalePop(_statsPill, 1.08f);
        }

        private void OnBump()
        {
            PlaySfx("game_miss");
            Haptics.Warn();
            Flash(new Color(0.9f, 0.32f, 0.22f, 0.16f), 0.18f);
            if (FieldW <= 1f) return;
            var at = BoatAt();
            Burst(at + new Vector2(0f, -10f), Color.white, 16);
            Float(at + new Vector2(0f, -70f), _game.Tune.BumpPenalty.ToString(), new Color(1f, 0.42f, 0.42f), 28f);
            ScalePop(_gooby, 0.8f);
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

        private sealed class HarborCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public HarborCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
