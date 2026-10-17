using System;
using System.Collections.Generic;
using Gooby.Core.Json;
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
    /// Liefer-Hetze host on the shared minigame frame (port of delivery_rush.gd + its dusk city): a 9×9 ring-and-cross
    /// town at peach hour, the van always rolling, drag left/right to steer into the gold drop ring. Traffic on the
    /// ring; a crash cools 1.5 s. Three parcels, then the time bonus. Game feel after delivery_rush.gd — WITHOUT screen
    /// shake (motion comfort of the drive): a gold pop + "+N" + small confetti at the drop ring with the perfect tone
    /// climbing the delivery streak; a crash puffs dust at the van, flashes red, floats "−N", jolts the van and buzzes;
    /// an expired parcel gets the miss tone and a grey flash.
    /// </summary>
    public sealed partial class DeliveryRushScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private DeliveryRushGame _game;
        private CityCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel;
        private bool _pressed, _firstLeg;

        private static readonly Color SkyTop = new Color(0.86f, 0.63f, 0.55f);
        private static readonly Color Road = MgScenery.Hex(0xB1AEC8);
        private static readonly Color RoadDark = MgScenery.Hex(0xA5A2BE);
        private static readonly Color Curb = MgScenery.Hex(0xF3E6D0);
        private static readonly Color RoadMark = new Color(1f, 0.97f, 0.88f, 0.8f);
        private static readonly Color[] HouseWalls = { MgScenery.Hex(0xFFE9D2), MgScenery.Hex(0xFFF3E2), MgScenery.Hex(0xE8F1FF) };
        private static readonly Color[] Roofs = { MgScenery.Hex(0xF08A80), MgScenery.Hex(0xB39DDB), MgScenery.Hex(0x7FC8B8) };
        private static readonly Color Door = MgScenery.Hex(0xB07A55);
        private static readonly Color Window = MgScenery.Hex(0xFFE08A);
        private static readonly Color TreeC = MgScenery.Hex(0x7CC486);
        private static readonly Color TreeShine = MgScenery.Hex(0xA6DB98);
        private static readonly Color Petal = MgScenery.Hex(0xFF9BC1);
        private static readonly Color RingC = new Color(1f, 0.82f, 0.32f);
        private static readonly Color RouteC = new Color(0.95f, 0.36f, 0.62f, 0.7f);
        private static readonly Color VanC = new Color(0.92f, 0.55f, 0.28f);

        public DeliveryRushScreen(GameHost host) : base(host, "deliveryRush", "deliveryRush") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xF4C3A6);
        protected override Color FieldColor => SkyTop;
        /// <summary>
        /// Gains only: a crash costs points, and the frame answers losses with a screen shake — this is a continuous drive
        /// (delivery_rush.gd motion-comfort rule: no screen shake), so the crash carries its own shake-free feedback.
        /// </summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.GainsOnly;
        /// <summary>TimeLeft is the time-bonus window, not a round clock — the round ends with the last parcel.</summary>
        protected override bool RoundEndedByClock => false;

        public DeliveryRushGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new DeliveryRushGame(Difficulty, seed);
            _game.Dropped += pts => { Banner(Host.Strings.T("mg.deliveryRush.delivered", ("n", _game.Drops), ("max", _game.Tune.Parcels)), 1.3); OnDropped(pts); StageDropped(); };
            _game.Crashed += n => { Banner(Host.Strings.T("mg.deliveryRush.crash", ("n", n)), 1.2); OnCrashed(n, false); StageCrashed(); };
            _game.FragileBroken += n => { Banner(Host.Strings.T("mg.deliveryRush.fragile_broken", ("n", n)), 1.4); OnCrashed(_game.Tune.CrashPenalty + n, true); };
            _game.TimeBonused += n => { Banner(Host.Strings.T("mg.deliveryRush.time_bonus", ("n", n)), 1.6); OnTimeBonus(n); };
            _game.Expired += () => { Banner(Host.Strings.T("mg.deliveryRush.expired", ("n", _game.Endless.Expired), ("max", _game.Endless.Limit)), 1.3); OnExpired(); };
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_perfect");
            _firstLeg = false;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _pressed = true;
                Field.CapturePointer(e.pointerId);
                _game.SetSteer(SteerFrom(e.localPosition.x));
            });
            Field.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_pressed) _game?.SetSteer(SteerFrom(e.localPosition.x));
            });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                _pressed = false;
                _game?.SetSteer(0.0);
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => { _pressed = false; _game?.SetSteer(0.0); });

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new CityCanvas { Paint = Paint, name = "city" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(48);
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

            Banner(t.T("mg.deliveryRush.intro"), DeliveryRushGame.IntroSec + 0.7);
        }

        protected override void TickGame(double dt)
        {
            var intro = _game.IntroLeft > 0.0;
            _game.Tick(dt);
            if (intro && _game.IntroLeft <= 0.0 && !_firstLeg)
            {
                _firstLeg = true;
                var name = Host.Strings.T("mg.deliveryRush.spot." + _game.CurrentTarget);
                Banner(Host.Strings.T("mg.deliveryRush.first_leg", ("name", name)), 2.4);
            }
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var van = WorldToView(g.VanX, g.VanZ);
            _gooby.style.left = van.x - 24f;
            _gooby.style.top = van.y - 52f;
            if (g.Tune.Endless)
                TimeLabel.text = t.T("mg.deliveryRush.expired_count", ("n", g.Endless.Expired), ("max", g.Endless.Limit));
            else
                TimeLabel.text = t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            var name = t.T("mg.deliveryRush.spot." + g.CurrentTarget);
            _statsLabel.text = t.T("mg.deliveryRush.target", ("n", Math.Min(g.Tune.Parcels, g.Parcel + 1)), ("max", g.Tune.Parcels), ("name", name));
            ComboLabel.text = g.Streak >= 2 ? "×" + g.Streak : "";
            if (_stage != null) { RenderStage(); return; }
            _canvas.MarkDirtyRepaint();
        }

        private double SteerFrom(float px) =>
            Mathf.Clamp((px - FieldW * 0.5f) / Mathf.Max(1f, FieldW * 0.4f), -1f, 1f);

        private float PxPerM() => Mathf.Max(4f, FieldH * 0.013f);

        // ── scenery (board 06 "Abend – warm"): the town seen from above sits on an evening lawn — a soft green graded
        //    ground with a warm peach dusk glow washing in from the top (a layer behind the city canvas, built once,
        //    pointer-transparent, nothing moves: the rotating map, its cream curbs, pastel houses with lit windows,
        //    trees and park lots are painted world-relative with the van). ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-deliveryrush");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D89A), MgScenery.Hex(0x93CB88), 0f, 100f);
            var dusk = MgScenery.Gradient(scene, new Color(1f, 0.78f, 0.62f, 0.55f), new Color(1f, 0.78f, 0.62f, 0f), 0f, 32f);
            dusk.name = "mgf-dusk";
        }

        private Vector2 WorldToView(double x, double z)
        {
            if (_stage != null) return _stage.ToField(W(x, z, 0.5f), FieldW, FieldH);
            var g = _game;
            var dx = x - g.VanX;
            var dz = z - g.VanZ;
            var fx = Math.Sin(g.Heading);
            var fz = -Math.Cos(g.Heading);
            var rx = Math.Cos(g.Heading);
            var rz = Math.Sin(g.Heading);
            var right = dx * rx + dz * rz;
            var fwd = dx * fx + dz * fz;
            var s = PxPerM();
            return new Vector2(FieldW * 0.5f + (float)right * s, FieldH * 0.72f - (float)fwd * s);
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            var g = _game;
            // the evening lawn under the town is the scenery layer behind the canvas; here the streets and houses
            var s = PxPerM() * (float)DeliveryRush.TileM * 0.46f;
            for (var r = 0; r < DeliveryRush.Grid; r++)
            for (var c = 0; c < DeliveryRush.Grid; c++)
            {
                var w = DeliveryRush.TileToWorld(r, c);
                var at = WorldToView(w.x, w.z);
                // only the tiles that can reach the view (pure paint culling, the grid itself is untouched)
                if (at.x < -s * 1.6f || at.x > FieldW + s * 1.6f || at.y < -s * 1.6f || at.y > FieldH + s * 1.6f) continue;
                var kind = g.Grid[r, c];
                if (kind == "road")
                {
                    Poly(p, Rect(at, s * 1.087f, s * 1.087f), Curb);
                    Poly(p, Rect(at, s, s), (r + c) % 2 == 0 ? Road : RoadDark);
                    if (r % 2 == 1 && c % 2 == 0) Poly(p, Rect(at, s * 0.06f, s * 0.3f), RoadMark);
                    else if (c % 2 == 1 && r % 2 == 0) Poly(p, Rect(at, s * 0.3f, s * 0.06f), RoadMark);
                }
                else if (kind == "block")
                {
                    var v = (r * 7 + c * 3) % 3;
                    Ellipse(p, at + new Vector2(s * 0.08f, s * 0.78f), s * 0.8f, s * 0.14f, new Color(0.35f, 0.5f, 0.3f, 0.18f));
                    Poly(p, Rect(at, s * 0.72f, s * 0.72f), HouseWalls[v]);
                    Poly(p, new[] {
                        at + new Vector2(-s * 0.78f, -s * 0.5f), at + new Vector2(s * 0.78f, -s * 0.5f), at + new Vector2(0f, -s * 1.05f),
                    }, Roofs[v]);
                    Poly(p, Rect(at + new Vector2(0f, s * 0.46f), s * 0.14f, s * 0.26f), Door);
                    Poly(p, Rect(at + new Vector2(-s * 0.4f, s * 0.02f), s * 0.15f, s * 0.15f), Window);
                    Poly(p, Rect(at + new Vector2(s * 0.4f, s * 0.02f), s * 0.15f, s * 0.15f), Window);
                    Ellipse(p, at + new Vector2(s * 0.86f, s * 0.6f), s * 0.2f, s * 0.2f, TreeC);
                    Ellipse(p, at + new Vector2(s * 0.82f, s * 0.54f), s * 0.08f, s * 0.08f, TreeShine);
                }
                else
                {
                    // the rim: a little park lot with two round trees and a flower bed
                    Ellipse(p, at + new Vector2(-s * 0.4f, -s * 0.2f), s * 0.28f, s * 0.28f, TreeC);
                    Ellipse(p, at + new Vector2(s * 0.35f, s * 0.3f), s * 0.22f, s * 0.22f, TreeC);
                    Ellipse(p, at + new Vector2(-s * 0.46f, -s * 0.28f), s * 0.1f, s * 0.1f, TreeShine);
                    Ellipse(p, at + new Vector2(s * 0.2f, -s * 0.45f), s * 0.07f, s * 0.07f, Petal);
                }
            }

            if (g.Route.Count >= 2)
            {
                p.strokeColor = RouteC; p.lineWidth = 6f;
                p.BeginPath();
                var a = WorldToView(g.Route[0].x, g.Route[0].z);
                p.MoveTo(a);
                for (var i = 1; i < g.Route.Count; i++)
                {
                    var b = WorldToView(g.Route[i].x, g.Route[i].z);
                    p.LineTo(b);
                }
                p.Stroke();
            }

            var drop = g.CurrentDrop;
            var ring = WorldToView(drop.x, drop.z);
            var rr = (float)g.Tune.DropRadiusM * PxPerM();
            p.strokeColor = RingC; p.lineWidth = 5f;
            EllipseStroke(p, ring, rr, rr * 0.72f);
            Ellipse(p, ring, 6f, 6f, RingC);

            foreach (var car in g.Traffic)
            {
                var tp = DeliveryRushGame.TrafficPos(car);
                var at = WorldToView(tp.x, tp.z);
                Poly(p, Rect(at, 8f, 5f), car.Cross ? new Color(0.42f, 0.62f, 0.9f) : new Color(0.85f, 0.35f, 0.4f));
            }

            var van = WorldToView(g.VanX, g.VanZ);
            Poly(p, Rect(van, 11f, 16f), VanC);
            Poly(p, Rect(van + new Vector2(0f, 8f), 7f, 5f), new Color(0.55f, 0.78f, 0.95f));
        }

        private void Band(Painter2D p, float y0, float y1, Color a, Color b)
        {
            const int steps = 4;
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

        // ── juice (delivery_rush.gd _check_drop / _crash / _expire_parcel / _finish_with_bonus — no shake) ──

        private static readonly Color Dust = new Color(0.72f, 0.66f, 0.6f);
        private static readonly Color CrashC = new Color(1f, 0.4f, 0.32f);
        private static readonly Color Cardboard = new Color(0.78f, 0.6f, 0.4f);
        private static readonly Color DropText = new Color(1f, 0.82f, 0.35f);

        private Vector2 VanAt() => WorldToView(_game.VanX, _game.VanZ);

        private void OnDropped(int points)
        {
            var g = _game;
            PlaySfx("game_perfect", Sfx.ComboSemitones(g.Streak));
            Haptics.Tap();
            if (FieldW <= 1f) return;
            // the engine already advanced to the next parcel: the ring just hit is the previous drop point
            var i = g.Parcel - 1;
            var at = i >= 0 && i < g.DropPoints.Count ? WorldToView(g.DropPoints[i].x, g.DropPoints[i].z) : VanAt();
            Burst(at, RingC, 20);
            Burst(at, Color.white, 8);
            Float(at + new Vector2(0f, -30f), "+" + points, DropText, 30f);
            ConfettiRain(30);
        }

        /// <summary>Traffic crash (fragile: the extra penalty too): dust + red flash + "−N" + a van jolt — never a shake.</summary>
        private void OnCrashed(int penalty, bool fragile)
        {
            PlaySfx("game_miss");
            Haptics.Warn();
            Flash(new Color(0.9f, 0.32f, 0.22f, 0.16f), 0.18f);
            if (FieldW <= 1f) return;
            var at = VanAt();
            Burst(at, Dust, 12);
            if (fragile) Burst(at, Cardboard, 14);
            Float(at + new Vector2(0f, -60f), "−" + penalty, CrashC, 28f);
            ScalePop(_gooby, 1.25f);
        }

        private void OnTimeBonus(int bonus)
        {
            PlaySfx("game_star");
            if (FieldW > 1f && bonus > 0) Float(VanAt() + new Vector2(0f, -90f), "+" + bonus, DropText, 32f);
        }

        private void OnExpired()
        {
            PlaySfx("game_miss", -3f, 0.8f);
            Flash(new Color(0.55f, 0.6f, 0.75f, 0.14f), 0.2f);
            ScalePop(_statsPill, 0.85f);
            Haptics.Warn();
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            // landmarks for the album — not for a round dropped with pause → Neustart (the frame books nothing for it)
            if (_game != null && _game.Visited.Count > 0 && !DroppingRound)
            {
                var payload = new JObject { ["collections"] = new JObject { ["landmarks"] = new JArray(_game.Visited) } };
                Host.Store.Update(s => CollectionsLogic.AwardReport(s, payload), GameHost.TicksMs);
                Host.Store.NotifySliceChanged("collections");
            }
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

        private sealed class CityCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public CityCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
