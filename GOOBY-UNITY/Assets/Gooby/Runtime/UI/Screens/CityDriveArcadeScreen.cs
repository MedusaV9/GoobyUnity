using System;
using Gooby.Core.City;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Einkaufsfahrt (cityDrive) host on the shared minigame frame — port of city_drive.gd as a sunny top-down town:
    /// the map turns with the car (nose up), the car rolls on auto-throttle, drag left/right steers. Gold coins (+10)
    /// respawn elsewhere, the pink checkpoint ring (+30) moves on, traffic loops the ring and the cross street. The
    /// car wears the paint of the active Autohaus car and drives with its stats. Game feel after city_drive.gd and its
    /// motion-comfort rule (no screen shake): coin pops with a climbing tone, a ring burst for checkpoints, sparks and
    /// a red flash on a crash, strike pips in the stats pill; the third strike tows Gooby home under a veil.
    /// </summary>
    public sealed partial class CityDriveArcadeScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private CityDriveGame _game;
        private DriveCanvas _canvas;
        private VisualElement _gooby, _statsPill;
        private Label _statsLabel, _strikeLabel;
        private bool _pressed;
        private double _ringPulse, _shownLabelFor = -1.0;
        private Color _paint = new Color(0.98f, 0.55f, 0.66f);

        private static readonly Color Road = MgScenery.Hex(0xBDB9D3);
        private static readonly Color Curb = MgScenery.Hex(0xFFF3DF);
        private static readonly Color RoadMark = new Color(1f, 0.98f, 0.9f, 0.85f);
        private static readonly Color[] HouseWalls = { MgScenery.Hex(0xFFE9D2), MgScenery.Hex(0xFFF6E6), MgScenery.Hex(0xE6F2FF), MgScenery.Hex(0xEAF7E4) };
        private static readonly Color[] Roofs = { MgScenery.Hex(0xF2928A), MgScenery.Hex(0xB9A3E0), MgScenery.Hex(0x86CDBF), MgScenery.Hex(0xF6B76E) };
        private static readonly Color Door = MgScenery.Hex(0xB07A55);
        private static readonly Color Window = MgScenery.Hex(0xBFE3FF);
        private static readonly Color TreeC = MgScenery.Hex(0x79C383);
        private static readonly Color TreeShine = MgScenery.Hex(0xA9DD9A);
        private static readonly Color Petal = MgScenery.Hex(0xFF9BC1);
        private static readonly Color Sun = MgScenery.Hex(0xFFE27A);
        private static readonly Color Coin = new Color(1f, 0.81f, 0.27f);
        private static readonly Color CoinRim = new Color(0.9f, 0.62f, 0.15f);
        private static readonly Color RingC = new Color(0.98f, 0.5f, 0.72f);
        private static readonly Color Shadow = new Color(0.25f, 0.35f, 0.25f, 0.22f);
        private static readonly Color Glass = new Color(0.7f, 0.86f, 0.98f);
        private static readonly Color[] TrafficC = { new Color(0.86f, 0.4f, 0.45f), new Color(0.45f, 0.64f, 0.92f) };

        public CityDriveArcadeScreen(GameHost host) : base(host, "cityDrive", "cityDrive") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xBFE6B4);
        protected override Color FieldColor => MgScenery.Hex(0xA9D89A);
        /// <summary>Gains only — a crash costs time and a strike, and the frame's loss answer would shake a continuous drive.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.GainsOnly;

        public CityDriveGame Game => _game;

        protected override void StartGame(int seed)
        {
            var (auto, farbe) = Host != null ? AutoKatalog.AktivesAuto(Host.Store) : (null, null);
            if (!string.IsNullOrEmpty(farbe) && ColorUtility.TryParseHtmlString(farbe.StartsWith("#") ? farbe : "#" + farbe, out var c)) _paint = c;
            _game = auto != null
                ? new CityDriveGame(Difficulty, seed, CarStats.SpeedMult(auto.Speed), CarStats.HandlingMult(auto.Handling), CarStats.BoostMult(auto.Boost))
                : new CityDriveGame(Difficulty, seed);
            var t = Host.Strings;
            _game.CoinPicked += (pts, x, z) => { OnCoin(pts, x, z); StageCoin(); };
            _game.CheckpointReached += (pts, x, z) => { Banner(t.T("mg.cityDrive.checkpoint", ("n", _game.Checkpoints)), 1.4); OnCheckpoint(pts, x, z); StageCheckpoint(); };
            _game.Crashed += n => { Banner(t.T("mg.cityDrive.crash", ("n", n), ("max", _game.Tune.StrikeLimit)), 1.6); OnCrash(); StageCrash(); };
            _game.Bumped += OnBump;
            _game.WentOffroad += () => { Banner(t.T("mg.cityDrive.offroad"), 2.2); PlaySfx("soft_tap", 2f, 0.8f); };
            _game.ZeroCrashBonused += n => { Banner(t.T("mg.cityDrive.zero_crash", ("n", n)), 1.8); PlaySfx("game_star"); };
            _game.TowedHome += OnTowed;
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_coin");
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
            Field.RegisterCallback<PointerMoveEvent>(e => { if (_pressed) _game?.SetSteer(SteerFrom(e.localPosition.x)); });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                _pressed = false;
                _game?.SetSteer(0.0);
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => { _pressed = false; _game?.SetSteer(0.0); });

            if (World.MgStage3D.Supported) BuildStage();
            else
            {
                var scene = MgScenery.Layer(Field, "mgf-citydrive");
                MgScenery.Gradient(scene, MgScenery.Hex(0xB3DEA2), MgScenery.Hex(0x9DD08F), 0f, 100f);
                var glow = MgScenery.Gradient(scene, new Color(1f, 0.95f, 0.75f, 0.5f), new Color(1f, 0.95f, 0.75f, 0f), 0f, 26f);
                glow.name = "mgf-sunglow";
            }

            _canvas = new DriveCanvas { Paint = Paint, name = "town" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(44);
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
            _statsLabel.name = "coin-count";
            _statsPill.Add(_statsLabel);
            _strikeLabel = Ux.Text("", "gb-md", "gb-bold");
            _strikeLabel.name = "strike-pips";
            _strikeLabel.style.marginLeft = 12;
            _strikeLabel.style.color = new Color(0.86f, 0.36f, 0.4f);
            _statsPill.Add(_strikeLabel);
            Root.Add(_statsPill);

            Banner(t.T("mg.cityDrive.intro"), CityDriveGame.IntroSec + 1.0);
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _ringPulse = Math.Max(0.0, _ringPulse - dt * 1.8);
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var car = WorldToView(g.X, g.Z);
            _gooby.style.left = car.x - 22f;
            _gooby.style.top = car.y - 60f;
            TimeLabel.text = g.Tune.Endless
                ? t.T("mg.cityDrive.endless_coins", ("n", g.Pickups))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = g.Score.ToString();
            if (_shownLabelFor != g.Pickups + g.Strikes * 1000)
            {
                _shownLabelFor = g.Pickups + g.Strikes * 1000;
                _statsLabel.text = "ᴳ " + g.Pickups;
                var pips = "";
                for (var i = 0; i < g.Tune.StrikeLimit; i++) pips += i < g.Strikes ? "●" : "○";
                _strikeLabel.text = pips;
            }
            ComboLabel.text = "";
            if (_stage != null) { RenderStage(); return; }
            _canvas.MarkDirtyRepaint();
        }

        private double SteerFrom(float px) => Mathf.Clamp((px - FieldW * 0.5f) / Mathf.Max(1f, FieldW * 0.4f), -1f, 1f);

        private float PxPerM() => Mathf.Max(4f, FieldH * 0.0125f);

        private Vector2 WorldToView(double x, double z)
        {
            if (_stage != null) return _stage.ToField(W(x, z, 0.5f), FieldW, FieldH);
            var g = _game;
            var dx = x - g.X;
            var dz = z - g.Z;
            var fx = Math.Sin(g.Heading);
            var fz = -Math.Cos(g.Heading);
            var rx = Math.Cos(g.Heading);
            var rz = Math.Sin(g.Heading);
            var right = dx * rx + dz * rz;
            var fwd = dx * fx + dz * fz;
            var s = PxPerM();
            return new Vector2(FieldW * 0.5f + (float)right * s, FieldH * 0.7f - (float)fwd * s);
        }

        /// <summary>A world direction (unit) turned into the view (nose-up map).</summary>
        private Vector2 DirToView(double dx, double dz)
        {
            var g = _game;
            var fx = Math.Sin(g.Heading);
            var fz = -Math.Cos(g.Heading);
            var rx = Math.Cos(g.Heading);
            var rz = Math.Sin(g.Heading);
            return new Vector2((float)(dx * rx + dz * rz), -(float)(dx * fx + dz * fz));
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            var g = _game;
            var s = PxPerM() * (float)CityDriveArcade.TileM * 0.5f;
            for (var r = -1; r <= CityDriveArcade.Grid; r++)
            for (var c = -1; c <= CityDriveArcade.Grid; c++)
            {
                var w = CityDriveArcade.TileToWorld(r, c);
                var at = WorldToView(w.x, w.z);
                if (at.x < -s * 2f || at.x > FieldW + s * 2f || at.y < -s * 2f || at.y > FieldH + s * 2f) continue;
                if (CityDriveArcade.IsRoad(r, c)) Quad(p, w, s * 1.08f, Curb);
                else if (r >= CityDriveArcade.RingMin && r <= CityDriveArcade.RingMax && c >= CityDriveArcade.RingMin && c <= CityDriveArcade.RingMax)
                    PaintHouse(p, w, (r * 7 + c * 3) % 4, s);
                else PaintPark(p, w, (r * 5 + c * 11) % 3, s);
            }

            for (var r = 0; r < CityDriveArcade.Grid; r++)
            for (var c = 0; c < CityDriveArcade.Grid; c++)
            {
                if (!CityDriveArcade.IsRoad(r, c)) continue;
                var w = CityDriveArcade.TileToWorld(r, c);
                var at = WorldToView(w.x, w.z);
                if (at.x < -s * 2f || at.x > FieldW + s * 2f || at.y < -s * 2f || at.y > FieldH + s * 2f) continue;
                PaintRoad(p, w, r, c, s);
            }

            // checkpoint: a pink ring on the asphalt with a soft halo, pulsing after each arrival
            var cp = WorldToView(g.Checkpoint.x, g.Checkpoint.z);
            var rr = (float)g.Tune.CheckpointRadiusM * PxPerM() * (1f + (float)_ringPulse * 0.35f);
            var wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
            Ellipse(p, cp, rr * 1.25f, rr * 1.25f, new Color(RingC.r, RingC.g, RingC.b, 0.18f + 0.1f * wave));
            p.strokeColor = RingC; p.lineWidth = 6f;
            EllipseStroke(p, cp, rr, rr);
            p.strokeColor = Color.white; p.lineWidth = 2f;
            EllipseStroke(p, cp, rr * 0.82f, rr * 0.82f);

            // coins: gold discs with a darker rim and a shine, gently bobbing
            var cr = Mathf.Max(5f, PxPerM() * 1.3f);
            for (var i = 0; i < g.Coins.Count; i++)
            {
                var at = WorldToView(g.Coins[i].x, g.Coins[i].z);
                if (at.x < -20f || at.x > FieldW + 20f || at.y < -20f || at.y > FieldH + 20f) continue;
                var bob = Mathf.Sin(Time.unscaledTime * 3f + i) * 1.5f;
                Ellipse(p, at + new Vector2(0f, cr * 0.7f), cr * 0.9f, cr * 0.3f, Shadow);
                Ellipse(p, at + new Vector2(0f, bob), cr, cr, CoinRim);
                Ellipse(p, at + new Vector2(0f, bob - 1f), cr * 0.8f, cr * 0.8f, Coin);
                Ellipse(p, at + new Vector2(-cr * 0.28f, bob - cr * 0.35f), cr * 0.22f, cr * 0.16f, new Color(1f, 1f, 0.9f, 0.9f));
            }

            foreach (var car in g.Traffic)
            {
                var tp = CityDriveGame.TrafficPos(car);
                var at = WorldToView(tp.x, tp.z);
                if (at.x < -40f || at.x > FieldW + 40f || at.y < -40f || at.y > FieldH + 40f) continue;
                var d = CityDriveGame.TrafficDir(car);
                PaintCar(p, at, DirToView(d.x, d.z), TrafficC[car.Tint % TrafficC.Length], PxPerM());
            }

            var me = WorldToView(g.X, g.Z);
            var blink = g.CrashCool > 0.0 && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.4f;
            PaintCar(p, me, new Vector2(0f, -1f), blink ? Color.white : _paint, PxPerM() * 1.12f);
        }

        private void PaintRoad(Painter2D p, (double x, double z) w, int r, int c, float s)
        {
            var at = WorldToView(w.x, w.z);
            Quad(p, w, s * 1.01f, Road);
            // dashed centre marks along the street's direction
            var horizontal = CityDriveArcade.IsRoad(r, c - 1) || CityDriveArcade.IsRoad(r, c + 1);
            var vertical = CityDriveArcade.IsRoad(r - 1, c) || CityDriveArcade.IsRoad(r + 1, c);
            if (horizontal && !vertical) Dash(p, w, 1.0, 0.0, s);
            else if (vertical && !horizontal) Dash(p, w, 0.0, 1.0, s);
            else Ellipse(p, at, s * 0.12f, s * 0.12f, RoadMark);
        }

        private void Dash(Painter2D p, (double x, double z) w, double ax, double az, float s)
        {
            var len = CityDriveArcade.TileM * 0.16;
            var wid = CityDriveArcade.TileM * 0.03;
            for (var k = -1; k <= 1; k += 2)
            {
                var cx = w.x + ax * k * CityDriveArcade.TileM * 0.25;
                var cz = w.z + az * k * CityDriveArcade.TileM * 0.25;
                var hx = ax != 0.0 ? len : wid;
                var hz = az != 0.0 ? len : wid;
                Poly(p, new[]
                {
                    WorldToView(cx - hx, cz - hz), WorldToView(cx + hx, cz - hz), WorldToView(cx + hx, cz + hz), WorldToView(cx - hx, cz + hz),
                }, RoadMark);
            }
        }

        private void PaintHouse(Painter2D p, (double x, double z) w, int v, float s)
        {
            var at = WorldToView(w.x, w.z);
            Ellipse(p, at + new Vector2(s * 0.1f, s * 0.12f), s * 0.86f, s * 0.86f, Shadow);
            Quad(p, w, s * 0.74f, HouseWalls[v]);
            Quad(p, w, s * 0.56f, Roofs[v]);
            Quad(p, w, s * 0.2f, Color.Lerp(Roofs[v], Color.white, 0.25f));
            // little windows on the roof edge and a tree in the yard corner
            var corner = CityDriveArcade.TileM * 0.36;
            Ellipse(p, WorldToView(w.x + corner, w.z + corner), s * 0.18f, s * 0.18f, TreeC);
            Ellipse(p, WorldToView(w.x + corner - 0.6, w.z + corner - 0.6), s * 0.07f, s * 0.07f, TreeShine);
            Ellipse(p, WorldToView(w.x - corner, w.z + corner), s * 0.06f, s * 0.06f, Petal);
            Ellipse(p, WorldToView(w.x - corner, w.z - corner), s * 0.09f, s * 0.09f, Window);
            Ellipse(p, WorldToView(w.x + corner, w.z - corner), s * 0.06f, s * 0.06f, Door);
        }

        private void PaintPark(Painter2D p, (double x, double z) w, int v, float s)
        {
            var at = WorldToView(w.x, w.z);
            Ellipse(p, at + new Vector2(-s * 0.35f, -s * 0.2f), s * 0.3f, s * 0.3f, TreeC);
            Ellipse(p, at + new Vector2(s * 0.38f, s * 0.32f), s * 0.24f, s * 0.24f, TreeC);
            Ellipse(p, at + new Vector2(-s * 0.42f, -s * 0.3f), s * 0.11f, s * 0.11f, TreeShine);
            if (v == 0) Ellipse(p, at + new Vector2(s * 0.25f, -s * 0.45f), s * 0.08f, s * 0.08f, Petal);
            else if (v == 1) Ellipse(p, at + new Vector2(-s * 0.1f, s * 0.42f), s * 0.07f, s * 0.07f, Sun);
        }

        /// <summary>A rounded top-down car: shadow, body in its paint, windscreen at the nose, roof highlight.</summary>
        private static void PaintCar(Painter2D p, Vector2 at, Vector2 nose, Color paint, float pxPerM)
        {
            nose = nose.sqrMagnitude > 1e-6f ? nose.normalized : new Vector2(0f, -1f);
            var side = new Vector2(-nose.y, nose.x);
            var hl = pxPerM * 2.1f;
            var hw = pxPerM * 1.0f;
            Vector2 P(float f, float sd) => at + nose * f + side * sd;
            Poly(p, new[] { P(hl, hw) + new Vector2(2f, 3f), P(hl, -hw) + new Vector2(2f, 3f), P(-hl, -hw) + new Vector2(2f, 3f), P(-hl, hw) + new Vector2(2f, 3f) }, Shadow);
            Poly(p, new[] { P(hl, hw * 0.8f), P(hl * 0.92f, hw), P(-hl * 0.92f, hw), P(-hl, hw * 0.8f), P(-hl, -hw * 0.8f), P(-hl * 0.92f, -hw), P(hl * 0.92f, -hw), P(hl, -hw * 0.8f) }, paint);
            Poly(p, new[] { P(hl * 0.72f, hw * 0.72f), P(hl * 0.72f, -hw * 0.72f), P(hl * 0.32f, -hw * 0.8f), P(hl * 0.32f, hw * 0.8f) }, Glass);
            Poly(p, new[] { P(hl * 0.18f, hw * 0.62f), P(hl * 0.18f, -hw * 0.62f), P(-hl * 0.5f, -hw * 0.62f), P(-hl * 0.5f, hw * 0.62f) }, Color.Lerp(paint, Color.white, 0.3f));
        }

        private void Quad(Painter2D p, (double x, double z) w, float halfPx, Color color)
        {
            var hm = halfPx / PxPerM();
            Poly(p, new[]
            {
                WorldToView(w.x - hm, w.z - hm), WorldToView(w.x + hm, w.z - hm), WorldToView(w.x + hm, w.z + hm), WorldToView(w.x - hm, w.z + hm),
            }, color);
        }

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
            for (var i = 0; i <= 20; i++)
            {
                var a = Mathf.PI * 2f * i / 20f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Stroke();
        }

        // ── juice (city_drive.gd _check_pickups / _check_checkpoint / _crash / _bump_feedback — never a shake) ──

        private static readonly Color Spark = new Color(1f, 0.85f, 0.4f);
        private static readonly Color CrashC = new Color(1f, 0.42f, 0.34f);

        private void OnCoin(int points, double x, double z)
        {
            PlaySfx("game_coin", Sfx.ComboSemitones(Math.Min(_game.Pickups, 8)));
            Haptics.Tap();
            if (FieldW <= 1f) return;
            var at = WorldToView(x, z);
            Burst(at, Coin, 10);
            Float(at + new Vector2(0f, -24f), "+" + points, new Color(1f, 0.82f, 0.35f), 26f);
        }

        private void OnCheckpoint(int points, double x, double z)
        {
            _ringPulse = 1.0;
            PlaySfx("game_star", Math.Min(_game.Checkpoints, 5) * 0.6f);
            Haptics.Tap();
            if (FieldW <= 1f) return;
            var at = WorldToView(x, z);
            Burst(at, RingC, 22);
            Burst(at, Color.white, 8);
            Float(at + new Vector2(0f, -36f), "+" + points, RingC, 32f);
            ScalePop(_gooby, 1.3f);
        }

        private void OnCrash()
        {
            PlaySfx("game_miss");
            Haptics.Warn();
            Flash(new Color(0.9f, 0.32f, 0.22f, 0.16f), 0.18f);
            ScalePop(_strikeLabel, 1.45f);
            if (FieldW <= 1f) return;
            var at = WorldToView(_game.X, _game.Z);
            Burst(at + new Vector2(0f, -PxPerM() * 2f), Spark, 14);
            Burst(at, CrashC, 8);
            ScalePop(_gooby, 0.8f);
        }

        private void OnBump()
        {
            PlaySfx("soft_tap", -4f, 0.9f);
            Haptics.Tap();
            if (FieldW <= 1f) return;
            Burst(WorldToView(_game.X, _game.Z) + new Vector2(0f, -PxPerM() * 2.2f), Spark, 6);
        }

        private void OnTowed()
        {
            Banner(Host.Strings.T("mg.host.strike_teleport"), 2.6);
            PlaySfx("game_lose");
            Flash(new Color(0.9f, 0.4f, 0.25f, 0.2f), 0.3f);
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

        private sealed class DriveCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public DriveCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
