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
    /// Spielzeug-Rennen (toyRacer) host on the shared minigame frame — port of toy_racer.gd as a toy race in the
    /// children's room: an orange plastic track with red/white rails lies on a pastel rug (loops lift off it), seen
    /// from above and turned with Gooby's kart (nose up). Hold to drift and steer towards the finger, let go for the
    /// boost, tap to fire the item. Rainbow "?" boxes, wooden building blocks, three rubber-band AI karts. Game feel
    /// after toy_racer.gd: drift sparks change colour with the charge, boost flames, a pop for pickups, a bonk for
    /// blocks, place/lap banners — no screen shake on a continuous drive.
    /// </summary>
    public sealed partial class ToyRacerScreen : MinigameHostScreen
    {
        public const double ViewW = 844.0, ViewH = 390.0;

        private ToyRacerGame _game;
        private RacerCanvas _canvas;
        private VisualElement _gooby, _statsPill, _chargeBar, _chargeFill;
        private Label _statsLabel, _lapLabel;
        private bool _pressed;
        private int _shownKey = -1;

        private static readonly Color RugA = MgScenery.Hex(0xF6D9C8);
        private static readonly Color RugB = MgScenery.Hex(0xF1CDB8);
        private static readonly Color RugBorder = MgScenery.Hex(0xE7A8A0);
        private static readonly Color Floor = MgScenery.Hex(0xE9C9A3);
        private static readonly Color TrackC = MgScenery.Hex(0xFF9F43);
        private static readonly Color TrackDark = MgScenery.Hex(0xF08A2E);
        private static readonly Color RailRed = MgScenery.Hex(0xF26D6D);
        private static readonly Color RailWhite = MgScenery.Hex(0xFFF6EC);
        private static readonly Color Shadow = new Color(0.35f, 0.2f, 0.15f, 0.18f);
        private static readonly Color[] KartC = { MgScenery.Hex(0xFF7FA8), MgScenery.Hex(0x6FA8F0), MgScenery.Hex(0x7CCB86), MgScenery.Hex(0xF6C343) };
        private static readonly Color[] BoxC = { MgScenery.Hex(0xFF8A8A), MgScenery.Hex(0xFFD166), MgScenery.Hex(0x7FD1AE), MgScenery.Hex(0x8FB7F2), MgScenery.Hex(0xC9A0E8) };
        private static readonly Color BlockC = MgScenery.Hex(0xE3A35C);

        public ToyRacerScreen(GameHost host) : base(host, "toyRacer", "toyRacer") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xF3D3BE);
        protected override Color FieldColor => Floor;
        /// <summary>The race has no round clock (it ends at the finish line).</summary>
        protected override bool RoundEndedByClock => false;
        protected override ScoreFeel ScoreFeedback => ScoreFeel.GainsOnly;

        public ToyRacerGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new ToyRacerGame(Difficulty, seed);
            _game.Event += OnEvent;
            _game.Finished_ += RoundOver;
            Sfx.Preload("game_whoosh");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_game == null) return;
                _pressed = true;
                Field.CapturePointer(e.pointerId);
                _game.Press(SteerFrom(e.localPosition.x));
            });
            Field.RegisterCallback<PointerMoveEvent>(e => { if (_pressed) _game?.SetSteer(SteerFrom(e.localPosition.x)); });
            Field.RegisterCallback<PointerUpEvent>(e =>
            {
                if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
                if (_pressed) _game?.Release();
                _pressed = false;
            });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => { if (_pressed) _game?.Release(); _pressed = false; });

            if (World.MgStage3D.Supported) BuildStage();
            else
            {
                var scene = MgScenery.Layer(Field, "mgf-toyracer");
                MgScenery.Gradient(scene, MgScenery.Hex(0xEFD0AE), MgScenery.Hex(0xE4BE96), 0f, 100f);
            }

            _canvas = new RacerCanvas { Paint = Paint, name = "track" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(34);
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
            _lapLabel = Ux.Text("", "gb-md", "gb-bold");
            _lapLabel.name = "lap-pill";
            _lapLabel.style.marginRight = 12;
            _statsPill.Add(_lapLabel);
            _statsLabel = Ux.Text("", "gb-md", "gb-bold");
            _statsLabel.name = "race-stats";
            _statsPill.Add(_statsLabel);
            _chargeBar = Ux.El();
            _chargeBar.name = "drift-charge";
            _chargeBar.style.width = 70; _chargeBar.style.height = 10; _chargeBar.style.marginLeft = 12;
            _chargeBar.style.backgroundColor = new Color(0.9f, 0.85f, 0.8f);
            _chargeBar.style.borderTopLeftRadius = 5; _chargeBar.style.borderTopRightRadius = 5; _chargeBar.style.borderBottomLeftRadius = 5; _chargeBar.style.borderBottomRightRadius = 5;
            _chargeFill = Ux.El();
            _chargeFill.style.height = Length.Percent(100);
            _chargeFill.style.width = Length.Percent(0);
            _chargeFill.style.borderTopLeftRadius = 5; _chargeFill.style.borderBottomLeftRadius = 5; _chargeFill.style.borderTopRightRadius = 5; _chargeFill.style.borderBottomRightRadius = 5;
            _chargeBar.Add(_chargeFill);
            _statsPill.Add(_chargeBar);
            Root.Add(_statsPill);

            Banner(t.T("mg.toyRacer.intro"), ToyRacerGame.IntroSec + 1.0);
        }

        protected override void TickGame(double dt) => _game.Tick(dt);

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var me = KartPos(g.Player);
            _gooby.style.left = me.x - 17f;
            _gooby.style.top = me.y - 46f;
            var race = g.Race;
            var key = g.Rank * 1000 + g.Lap * 100 + race.ChainRaces * 10 + ItemCode(g.Player.Item);
            if (key != _shownKey)
            {
                _shownKey = key;
                _lapLabel.text = g.Tune.Endless
                    ? t.T("mg.toyRacer.chain_pill", ("n", race.ChainRaces + 1), ("lap", g.Lap))
                    : t.T("mg.toyRacer.lap_pill", ("n", g.Lap), ("total", g.Tune.Laps));
                var item = string.IsNullOrEmpty(g.Player.Item) ? t.T("mg.toyRacer.item_none") : t.T("mg.toyRacer.item_" + g.Player.Item + "_short");
                _statsLabel.text = t.T("mg.toyRacer.stats", ("p", g.Rank), ("item", item));
            }
            var charge = (float)g.Player.DriftCharge;
            _chargeFill.style.width = Length.Percent(charge * 100f);
            _chargeFill.style.backgroundColor = ChargeColor(charge);
            TimeLabel.text = t.T("mg.toyRacer.pos_pill", ("p", g.Rank));
            ScoreLabel.text = g.Score.ToString();
            ComboLabel.text = "";
            if (_stage != null) { RenderStage(); return; }
            _canvas.MarkDirtyRepaint();
        }

        private static int ItemCode(string item) => item == "turbo" ? 1 : item == "shield" ? 2 : item == "block" ? 3 : 0;

        private static Color ChargeColor(float c) =>
            c < ToyRacer.Racer.DriftMinCharge ? new Color(0.55f, 0.75f, 1f) : c < 0.8f ? new Color(1f, 0.67f, 0.3f) : new Color(1f, 0.45f, 0.7f);

        private double SteerFrom(float px) => Mathf.Clamp(px / Mathf.Max(1f, FieldW) * 2f - 1f, -1f, 1f);

        // ── projection: world units around Gooby's kart, turned so the kart drives up the screen ──

        private float Px => Mathf.Max(20f, FieldH * 0.105f);

        private Vector2 WorldToView(double x, double y, double z)
        {
            var me = _game.Player;
            var f = ToyRacer.PointAt(_game.Race.Track, me.S);
            var cx = f.P.X + f.Right.X * me.Lateral;
            var cz = f.P.Z + f.Right.Z * me.Lateral;
            var hx = f.T.X; var hz = f.T.Z;
            var hl = Math.Sqrt(hx * hx + hz * hz);
            if (hl < 1e-6) { hx = 0.0; hz = 1.0; hl = 1.0; }
            hx /= hl; hz /= hl;
            var dx = x - cx;
            var dz = z - cz;
            var fwd = dx * hx + dz * hz;
            // screen right is cross(travel, up) = (−hz, hx): the logic's positive lateral, where the finger steers
            var side = dz * hx - dx * hz;
            return new Vector2(FieldW * 0.5f + (float)side * Px, FieldH * 0.68f - (float)fwd * Px - (float)y * Px * 0.55f);
        }

        private Vector2 KartPos(ToyRacer.Kart k)
        {
            var f = ToyRacer.PointAt(_game.Race.Track, k.S);
            return WorldToView(f.P.X + f.Right.X * k.Lateral, f.P.Y + f.Right.Y * k.Lateral, f.P.Z + f.Right.Z * k.Lateral);
        }

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f || _stage != null) return;
            var race = _game.Race;
            var track = race.Track;
            var tune = _game.Tune;

            // the rug under the track: a pastel checker with a rose border (the track's bounding box + margin)
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var s in track.Samples) { minX = Math.Min(minX, s.P.X); maxX = Math.Max(maxX, s.P.X); minZ = Math.Min(minZ, s.P.Z); maxZ = Math.Max(maxZ, s.P.Z); }
            minX -= 3.0; maxX += 3.0; minZ -= 3.0; maxZ += 3.0;
            Poly(p, new[] { WorldToView(minX - 0.6, 0, minZ - 0.6), WorldToView(maxX + 0.6, 0, minZ - 0.6), WorldToView(maxX + 0.6, 0, maxZ + 0.6), WorldToView(minX - 0.6, 0, maxZ + 0.6) }, RugBorder);
            Poly(p, new[] { WorldToView(minX, 0, minZ), WorldToView(maxX, 0, minZ), WorldToView(maxX, 0, maxZ), WorldToView(minX, 0, maxZ) }, RugA);
            const double cell = 2.0;
            for (var x = minX; x < maxX - 1e-6; x += cell)
            for (var z = minZ; z < maxZ - 1e-6; z += cell)
            {
                if (((int)Math.Floor((x - minX) / cell) + (int)Math.Floor((z - minZ) / cell)) % 2 == 0) continue;
                Poly(p, new[] { WorldToView(x, 0, z), WorldToView(Math.Min(maxX, x + cell), 0, z), WorldToView(Math.Min(maxX, x + cell), 0, Math.Min(maxZ, z + cell)), WorldToView(x, 0, Math.Min(maxZ, z + cell)) }, RugB);
            }

            // the track ribbon, sample to sample: shadow, rails in red/white segments, orange deck with a centre dash
            var n = track.Samples.Count;
            var half = tune.TrackHalfW;
            for (var pass = 0; pass < 3; pass++)
            {
                for (var i = 0; i < n; i++)
                {
                    var a = track.Samples[i];
                    var b = track.Samples[(i + 1) % n];
                    var ra = Right(a);
                    var rb = Right(b);
                    if (pass == 0)
                    {
                        var w = half + 0.12;
                        Poly(p, new[] { WorldToView(a.P.X - ra.x * w, 0, a.P.Z - ra.z * w) + new Vector2(3f, 4f), WorldToView(a.P.X + ra.x * w, 0, a.P.Z + ra.z * w) + new Vector2(3f, 4f),
                            WorldToView(b.P.X + rb.x * w, 0, b.P.Z + rb.z * w) + new Vector2(3f, 4f), WorldToView(b.P.X - rb.x * w, 0, b.P.Z - rb.z * w) + new Vector2(3f, 4f) }, Shadow);
                    }
                    else if (pass == 1)
                    {
                        var w = half + 0.1;
                        var rail = i / 3 % 2 == 0 ? RailRed : RailWhite;
                        Poly(p, Ribbon(a, b, ra, rb, w), rail);
                    }
                    else
                    {
                        Poly(p, Ribbon(a, b, ra, rb, half), i % 8 < 4 ? TrackC : TrackDark);
                        if (i % 6 == 0) Poly(p, Ribbon(a, b, ra, rb, 0.03), new Color(1f, 1f, 1f, 0.7f));
                    }
                }
            }
            // start/finish line
            var s0 = track.Samples[0];
            var r0 = Right(s0);
            for (var k = -2; k < 2; k++)
            {
                var l0 = half * k / 2.0;
                var l1 = half * (k + 1) / 2.0;
                Poly(p, new[] { WorldToView(s0.P.X + r0.x * l0, s0.P.Y, s0.P.Z + r0.z * l0), WorldToView(s0.P.X + r0.x * l1, s0.P.Y, s0.P.Z + r0.z * l1),
                    WorldToView(s0.P.X + r0.x * l1 + s0.T.X * 0.25, s0.P.Y, s0.P.Z + r0.z * l1 + s0.T.Z * 0.25), WorldToView(s0.P.X + r0.x * l0 + s0.T.X * 0.25, s0.P.Y, s0.P.Z + r0.z * l0 + s0.T.Z * 0.25) },
                    (k & 1) == 0 ? Color.white : new Color(0.25f, 0.2f, 0.2f));
            }

            // item boxes (rainbow cubes with a "?" gloss) and dropped building blocks
            var bob = Mathf.Sin(Time.unscaledTime * 4f) * 2f;
            foreach (var row in track.ItemRows)
            {
                var f = ToyRacer.PointAt(track, row.S);
                for (var bi = 0; bi < row.Boxes.Count; bi++)
                {
                    var box = row.Boxes[bi];
                    if (box.RespawnT > 0.0) continue;
                    var at = WorldToView(f.P.X + f.Right.X * box.Lat, f.P.Y, f.P.Z + f.Right.Z * box.Lat) + new Vector2(0f, -6f + bob);
                    var r = Px * 0.17f;
                    Poly(p, Rect(at + new Vector2(2f, 8f), r, r * 0.4f), Shadow);
                    Poly(p, Rect(at, r, r), BoxC[(bi + (int)(Time.unscaledTime * 3f)) % BoxC.Length]);
                    Poly(p, Rect(at + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.3f, r * 0.3f), new Color(1f, 1f, 1f, 0.55f));
                }
            }
            foreach (var block in race.Blocks)
            {
                var f = ToyRacer.PointAt(track, block.S);
                var at = WorldToView(f.P.X + f.Right.X * block.Lat, f.P.Y, f.P.Z + f.Right.Z * block.Lat);
                var r = Px * 0.16f;
                Poly(p, Rect(at + new Vector2(2f, 6f), r, r * 0.45f), Shadow);
                Poly(p, Rect(at + new Vector2(0f, -r * 0.6f), r, r), BlockC);
                Poly(p, Rect(at + new Vector2(0f, -r * 1.25f), r * 0.55f, r * 0.25f), MgScenery.Hex(0xF26D6D));
            }

            // karts far → near, Gooby's last so it sits on top
            var order = new List<ToyRacer.Kart>(race.Karts);
            order.Sort((x, y) => KartPos(x).y.CompareTo(KartPos(y).y));
            foreach (var k in order) PaintKart(p, k);
        }

        /// <summary>The track's lateral axis cross(T, Up) on the ground — stays horizontal through the loops.</summary>
        private static (double x, double z) Right(ToyRacer.Sample s)
        {
            var t = s.T; var up = s.Up;
            var rx = t.Y * up.Z - t.Z * up.Y;
            var rz = t.X * up.Y - t.Y * up.X;
            var len = Math.Sqrt(rx * rx + rz * rz);
            return len > 1e-6 ? (rx / len, rz / len) : (1.0, 0.0);
        }

        private Vector2[] Ribbon(ToyRacer.Sample a, ToyRacer.Sample b, (double x, double z) ra, (double x, double z) rb, double w) => new[]
        {
            WorldToView(a.P.X - ra.x * w, a.P.Y, a.P.Z - ra.z * w), WorldToView(a.P.X + ra.x * w, a.P.Y, a.P.Z + ra.z * w),
            WorldToView(b.P.X + rb.x * w, b.P.Y, b.P.Z + rb.z * w), WorldToView(b.P.X - rb.x * w, b.P.Y, b.P.Z - rb.z * w),
        };

        private void PaintKart(Painter2D p, ToyRacer.Kart k)
        {
            var track = _game.Race.Track;
            var f = ToyRacer.PointAt(track, k.S);
            var at = KartPos(k);
            var ahead = ToyRacer.PointAt(track, k.S + 0.3);
            var nosePt = WorldToView(ahead.P.X + ahead.Right.X * k.Lateral, ahead.P.Y, ahead.P.Z + ahead.Right.Z * k.Lateral);
            var nose = nosePt - at;
            nose = nose.sqrMagnitude > 1e-4f ? nose.normalized : new Vector2(0f, -1f);
            var side = new Vector2(-nose.y, nose.x);
            var hl = Px * 0.24f;
            var hw = Px * 0.14f;
            Vector2 P(float fw, float sd) => at + nose * fw + side * sd;
            var paint = KartC[k.Id % KartC.Length];
            if (k.StunT > 0.0 && Mathf.Repeat(Time.unscaledTime * 10f, 1f) < 0.5f) paint = Color.white;
            // drift sparks behind the rear wheels, coloured by the charge; boost flames
            if (k.Drifting && k.DriftCharge > 0.05)
            {
                var c = ChargeColor((float)k.DriftCharge);
                for (var j = 0; j < 4; j++)
                {
                    var jitter = new Vector2(Mathf.Sin(Time.unscaledTime * 31f + j * 2f), Mathf.Cos(Time.unscaledTime * 27f + j)) * 3f;
                    Ellipse(p, P(-hl * 1.1f, (j < 2 ? -1 : 1) * hw) + jitter, 2.6f, 2.6f, c);
                }
            }
            if (k.BoostT > 0.0)
                Poly(p, new[] { P(-hl, -hw * 0.6f), P(-hl, hw * 0.6f), P(-hl * 2.1f + Mathf.Sin(Time.unscaledTime * 40f) * 3f, 0f) }, new Color(1f, 0.6f, 0.25f, 0.9f));
            Ellipse(p, at + new Vector2(2f, 4f), hl * 1.05f, hw * 1.1f, Shadow);
            Poly(p, new[] { P(hl, hw * 0.75f), P(hl * 0.85f, hw), P(-hl * 0.9f, hw), P(-hl, hw * 0.75f), P(-hl, -hw * 0.75f), P(-hl * 0.9f, -hw), P(hl * 0.85f, -hw), P(hl, -hw * 0.75f) }, paint);
            Poly(p, new[] { P(hl * 0.55f, hw * 0.7f), P(hl * 0.55f, -hw * 0.7f), P(hl * 0.15f, -hw * 0.75f), P(hl * 0.15f, hw * 0.75f) }, new Color(0.75f, 0.88f, 1f));
            foreach (var (fw, sd) in new[] { (hl * 0.6f, hw * 1.05f), (hl * 0.6f, -hw * 1.05f), (-hl * 0.6f, hw * 1.05f), (-hl * 0.6f, -hw * 1.05f) })
                Ellipse(p, P(fw, sd), hw * 0.32f, hw * 0.32f, new Color(0.25f, 0.22f, 0.24f));
            if (k.Shield) { p.strokeColor = new Color(0.55f, 0.85f, 1f, 0.8f); p.lineWidth = 3f; EllipseStroke(p, at, hl * 1.5f, hl * 1.2f); }
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

        // ── juice (toy_racer.gd _handle_event — no shake) ──

        private void OnEvent(ToyRacer.Ev ev)
        {
            StageEvent(ev);
            var t = Host.Strings;
            var g = _game;
            var mine = ev.Kart == 0;
            switch (ev.Type)
            {
                case "boost":
                    Banner(t.T("mg.toyRacer.boost"), 1.0);
                    PlaySfx("game_whoosh", (float)(ev.Charge * 4.0));
                    Haptics.Tap();
                    if (FieldW > 1f) Burst(KartPos(g.Player), ChargeColor((float)ev.Charge), 14);
                    break;
                case "turbo":
                    if (!mine) break;
                    Banner(t.T("mg.toyRacer.turbo"), 1.0);
                    PlaySfx("game_combo", 4f);
                    break;
                case "shield":
                    if (mine) { Banner(t.T("mg.toyRacer.shield"), 1.2); PlaySfx("soft_sticker"); }
                    break;
                case "shieldPop":
                    if (mine) { Banner(t.T("mg.toyRacer.shield_pop"), 1.2); PlaySfx("game_pop"); }
                    break;
                case "blockDrop":
                    if (mine) { Banner(t.T("mg.toyRacer.block_drop"), 1.0); PlaySfx("soft_tap"); }
                    break;
                case "blockHit":
                    if (!mine) break;
                    Banner(t.T("mg.toyRacer.block_hit"), 1.2);
                    PlaySfx("game_miss");
                    Haptics.Warn();
                    Flash(new Color(0.9f, 0.4f, 0.25f, 0.16f), 0.16f);
                    if (FieldW > 1f) Burst(KartPos(g.Player), BlockC, 12);
                    break;
                case "pickup":
                    if (!mine) break;
                    Banner(t.T("mg.toyRacer.item_" + ev.Item), 1.2);
                    PlaySfx("game_pop", 3f);
                    Haptics.Tap();
                    if (FieldW > 1f) Burst(KartPos(g.Player) + new Vector2(0f, -10f), BoxC[1], 10);
                    ScalePop(_statsPill, 1.08f);
                    break;
                case "offtrack":
                    Banner(t.T("mg.toyRacer.offtrack"), 1.0);
                    PlaySfx("soft_tap", -3f);
                    break;
                case "overtake":
                    PlaySfx("game_hit", Sfx.ComboSemitones(Math.Min(ev.Total, 6)));
                    if (FieldW > 1f) Float(KartPos(g.Player) + new Vector2(0f, -50f), "+" + g.Tune.OvertakePoints, new Color(1f, 0.82f, 0.35f), 24f);
                    break;
                case "lap":
                    Banner(ev.Final ? t.T("mg.toyRacer.final_lap") : t.T("mg.toyRacer.lap", ("n", ev.Lap)), 1.4);
                    PlaySfx("game_star");
                    break;
                case "chainRace":
                    Banner(t.T("mg.toyRacer.chain", ("n", ev.Races), ("p", ev.Rank)), 1.8);
                    PlaySfx("game_win");
                    break;
                case "finish":
                    Banner(ev.Rank == 1 ? t.T("mg.toyRacer.finish_first") : t.T("mg.toyRacer.finish_place", ("p", ev.Rank)), 2.0);
                    if (ev.Rank == 1) ConfettiRain(60);
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

        private sealed class RacerCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public RacerCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
