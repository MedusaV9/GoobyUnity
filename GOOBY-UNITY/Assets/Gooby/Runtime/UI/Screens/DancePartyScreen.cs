using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Tanzparty host on the shared minigame frame (port of dance_party.gd + its club stage): a dark club painted
    /// with the Painter2D — a checkered light floor pulsing to the beat, spotlights sweeping, speakers, the disco
    /// ball spinning faster the higher the combo tier. Three coloured lanes carry the seeded chart's notes down to
    /// the hit line; a tap on a lane judges the nearest note (perfect gold ring, good blue ring, a miss dims the
    /// floor). Gooby dances at the bottom, bobbing on the beat; the encore floods the club with gold and confetti. With a
    /// GPU the club, lanes and notes are the 3D stage of <c>DancePartyScreen.Stage3D.cs</c>; the canvas keeps the pops,
    /// confetti and veils.
    /// </summary>
    public sealed partial class DancePartyScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;
        private const float HitLineFrac = 0.74f, TravelFrac = 0.66f;
        private const float BeatsPerSec = (float)(Gooby.Core.Minigames.Games.DanceParty.Bpm / 60.0);

        private DancePartyGame _game;
        private ClubCanvas _canvas;
        private VisualElement _gooby, _comboPill;
        private Label _comboPillLabel;
        private float _show, _ballSpin, _ballPop, _missDim, _encoreGlow;
        private readonly float[] _laneFlash = new float[3];
        private readonly List<Pop> _pops = new List<Pop>();
        private readonly List<Confetti> _confetti = new List<Confetti>();

        private struct Pop { public Vector2 P; public float Age; public bool Perfect; }
        private struct Confetti { public Vector2 P, V; public float Life, Spin; public Color C; }

        private static readonly Color FloorDark = MgScenery.Hex(0x6A57A8);
        private static readonly Color WallTop = MgScenery.Hex(0x3C2E70);
        private static readonly Color FloorLight = MgScenery.Hex(0xC9B8F2);
        private static readonly Color Ink = new Color(0.12f, 0.07f, 0.2f, 0.92f);
        private static readonly Color PopPerfect = new Color(1f, 0.85f, 0.4f);
        private static readonly Color PopGood = new Color(0.6f, 0.85f, 1f);
        private static readonly Color[] LaneColors = { new Color(1f, 0.48f, 0.66f), new Color(0.35f, 0.79f, 0.73f), new Color(1f, 0.82f, 0.4f) };

        public DancePartyScreen(GameHost host) : base(host, "danceParty", "danceParty") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0x5A4290);
        protected override Color FieldColor => WallTop;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public DancePartyGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new DancePartyGame(Difficulty);
            // the device's output latency plus the player's calibrated fine offset (dance_timing.gd play_time)
            var offsetMs = DanceTiming.TotalOffsetMs(BaseLatencyMs(), ManualOffsetMs);
            _game.ShiftTimeline(offsetMs / 1000.0);
            Debug.Log("[dance] timeline offset " + offsetMs.ToString("F0") + " ms");
            _game.Judged += OnJudged;
            _game.EncoreStarted += () =>
            {
                Banner(Host.Strings.T("mg.danceParty.encore"), 1.6);
                _encoreGlow = 1f;
                StageEncore();
                for (var i = 0; i < 40; i += ParticleStep)
                    _confetti.Add(new Confetti { P = new Vector2(FieldW * ((i * 37) % 100) / 100f, -10f - (i * 13) % 60), V = new Vector2(((i * 29) % 60) - 30f, 120f + (i * 17) % 90), Life = 2.4f, Spin = i * 0.7f, C = LaneColors[i % 3] });
            };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e => Tap(e.localPosition.x));

            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            _canvas = new ClubCanvas { Paint = Paint };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            _gooby = Ux.Portrait(96);
            _gooby.name = "gooby";
            _gooby.style.position = Position.Absolute;
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) _gooby.style.display = DisplayStyle.None;

            _comboPill = Ux.El("gb-card", "gb-row");
            _comboPill.name = "combo-pill";
            Ux.Abs(_comboPill, null, 62);
            _comboPill.style.left = Length.Percent(50);
            _comboPill.style.translate = new Translate(Length.Percent(-50), 0);
            _comboPill.style.alignItems = Align.Center;
            _comboPill.style.paddingTop = 6; _comboPill.style.paddingBottom = 6; _comboPill.style.paddingLeft = 12; _comboPill.style.paddingRight = 16;
            _comboPill.pickingMode = PickingMode.Ignore;
            var icon = Ux.Icon("sparkle");
            icon.style.width = 30; icon.style.height = 30; icon.style.marginRight = 8;
            _comboPill.Add(icon);
            _comboPillLabel = Ux.Text("", "gb-md", "gb-bold");
            _comboPill.Add(_comboPillLabel);
            Root.Add(_comboPill);
            ComboLabel.style.top = 128;
            Banner(t.T("mg.danceParty.hint"), _game.Tune.LeadInSec + 0.6);
        }

        // ── lanes ──────────────────────────────────────────────────────────

        private float LaneSpan => Mathf.Min(FieldW / 3.4f, 150f * FieldW / (float)ViewW);
        private float LaneX(int lane) => FieldW * 0.5f + (lane - 1f) * LaneSpan;
        private int LaneAt(float px) => Mathf.Clamp(Mathf.FloorToInt((px - (FieldW * 0.5f - LaneSpan * 1.5f)) / LaneSpan), 0, 2);
        private float HitLineY => FieldH * HitLineFrac;

        // ── scenery (board 06 "Abend", a pastel disco — violet, never black): the wall graded from deep violet to lilac
        //    with faint sparkles and soft glow blobs, a pink scalloped valance, three strings of fairy lights across the
        //    top and a lilac wainscot behind the floor's open corners. A layer behind the club canvas (floor tiles,
        //    spots, speakers, disco ball, lanes and notes stay painted per beat); built once, pointer-transparent. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-danceparty");
            MgScenery.Gradient(scene, WallTop, MgScenery.Hex(0x7560B4), 0f, 76f);
            foreach (var (c, d, l, t) in new[] { (new Color(1f, 0.6f, 0.85f, 0.12f), 240f, -20f, 28f), (new Color(0.5f, 0.9f, 0.95f, 0.1f), 200f, 62f, 40f), (new Color(1f, 0.85f, 0.5f, 0.08f), 180f, 20f, 52f) })
            {
                var blob = MgScenery.Shape(scene, c, d * 0.5f, "gb-mgf-nebula");
                MgScenery.Pct(blob, l, t);
                MgScenery.Size(blob, d, d);
            }
            var sparkles = MgScenery.Dots(scene, 30, new Color(1f, 0.95f, 1f, 0.35f), 1.2f, 2.5f, 27);
            MgScenery.Pct(sparkles, 0, 14f, 0);
            sparkles.style.height = Length.Percent(50f);
            float[] twAt = { 10f, 84f, 26f, 70f }; float[] twTop = { 34f, 30f, 58f, 56f };
            for (var i = 0; i < twAt.Length; i++) MgScenery.Pct(MgScenery.Twinkle(scene, 12f, new Color(1f, 0.92f, 0.7f, 0.6f)), twAt[i], twTop[i]);
            var wains = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, MgScenery.Hex(0x8B76C6));
                p.strokeColor = MgScenery.Hex(0x9E8AD6); p.lineWidth = 2f;
                for (var x = 12f; x < r.width; x += 22f) { p.BeginPath(); p.MoveTo(new Vector2(x, 8f)); p.LineTo(new Vector2(x, r.height)); p.Stroke(); }
                MgScenery.FillRect(p, 0f, 0f, r.width, 6f, MgScenery.Hex(0xFF9BC1));
            }, "gb-mgf-wainscot");
            MgScenery.Pct(wains, 0, 64f, 0, 0);
            var bulbs = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFD166), MgScenery.Hex(0x9BE7E1), MgScenery.Hex(0xB7D8FF), MgScenery.Hex(0xC6F39A) };
            var wire = MgScenery.Hex(0x2C2258);
            var l1 = MgScenery.StringLights(scene, 11, 16f, bulbs, wire);
            MgScenery.Pct(l1, -3f, 4.5f, -3f); l1.style.height = 56f;
            var l2 = MgScenery.StringLights(scene, 7, 12f, new[] { bulbs[2], bulbs[0], bulbs[1] }, wire);
            MgScenery.Pct(l2, -3f, 9.5f, 45f); l2.style.height = 44f;
            var l3 = MgScenery.StringLights(scene, 7, 12f, new[] { bulbs[3], bulbs[1], bulbs[4] }, wire);
            MgScenery.Pct(l3, 45f, 9.5f, -3f); l3.style.height = 44f;
            var valance = MgScenery.Scallop(scene, 8, 7f, MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xE97FA6));
            MgScenery.Pct(valance, 0, 0, 0);
            valance.style.height = Length.Percent(4.5f);
        }
        private float NoteY(double progress) => HitLineY - (1f - (float)progress) * FieldH * TravelFrac;

        private void Tap(float px)
        {
            if (_game == null || FieldW <= 0f) return;
            var lane = LaneAt(px);
            _laneFlash[lane] = 0.18f;
            _game.Tap(lane);
        }

        /// <summary>Public for the flow tests: a tap on a lane by index.</summary>
        public string TapLane(int lane)
        {
            if (_game == null) return null;
            _laneFlash[Mathf.Clamp(lane, 0, 2)] = 0.18f;
            return _game.Tap(lane);
        }

        // ── the round ──────────────────────────────────────────────────────

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            var f = (float)dt;
            _show += f;
            _ballSpin += f * (0.9f + 0.35f * _game.Tier);
            _ballPop = Mathf.Max(0f, _ballPop - f);
            _missDim = Mathf.Max(0f, _missDim - f * 2f);
            _encoreGlow = Mathf.Max(0f, _encoreGlow - f * 0.25f);
            for (var i = 0; i < 3; i++) _laneFlash[i] = Mathf.Max(0f, _laneFlash[i] - f);
            for (var i = _pops.Count - 1; i >= 0; i--)
            {
                var p = _pops[i]; p.Age += f;
                if (p.Age > 0.32f) _pops.RemoveAt(i); else _pops[i] = p;
            }
            for (var i = _confetti.Count - 1; i >= 0; i--)
            {
                var c = _confetti[i]; c.Life -= f; c.P += c.V * f; c.V.x += Mathf.Sin(_show * 5f + c.Spin) * 40f * f; c.Spin += f * 6f;
                if (c.Life <= 0f || c.P.y > FieldH + 20f) _confetti.RemoveAt(i); else _confetti[i] = c;
            }
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.danceParty.breaks", ("n", _game.Breaks), ("max", _game.Tune.EndlessBreakLimit))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            _comboPillLabel.text = t.T("mg.game.streak", ("n", _game.Combo));
            ComboLabel.text = _game.Encore ? t.T("mg.danceParty.encore") : "";
            if (_stage != null)
            {
                RenderStage();
                _canvas.MarkDirtyRepaint();
                return;
            }
            // Gooby dances under the hit line, bobbing on the beat — bigger the higher the tier
            var beat = (float)(_game.SongTime * BeatsPerSec);
            var bob = Mathf.Abs(Mathf.Sin(beat * Mathf.PI)) * (10f + 6f * _game.Tier);
            var size = FieldW * 0.28f;
            _gooby.style.width = size; _gooby.style.height = size;
            _gooby.style.left = FieldW * 0.5f - size * 0.5f; _gooby.style.top = FieldH * 0.86f - size * 0.5f - bob;
            _gooby.style.rotate = new Rotate(Mathf.Sin(beat * Mathf.PI * 0.5f) * (6f + 3f * _game.Tier));
            _gooby.style.scale = new Scale(new Vector3(1f, 1f - bob / 140f, 1f));
            _canvas.MarkDirtyRepaint();
        }


        // ── painting ───────────────────────────────────────────────────────

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 0f || FieldH <= 0f) return;
            var beat = (float)(_game.SongTime * BeatsPerSec);
            var pulse = Mathf.Max(0f, 1f - Mathf.Repeat(beat, 1f) * 2.5f);
            var hitY = HitLineY;
            // the club: the violet wall with its lights is the scenery layer behind the canvas (a miss dims it with a veil
            // here), a checkered pastel floor in perspective under the hit line, pulsing on the beat
            if (_missDim > 0f) FillRect(p, 0f, 0f, FieldW, FieldH, new Color(0f, 0f, 0f, _missDim * (_stage != null ? 0.2f : 0.4f)));
            if (_stage == null) PaintClub(p, beat, pulse, hitY);
            PaintOverlay(p);
        }

        /// <summary>The painted club of the 2D field (without a GPU): floor, spots, speakers, mirror ball, lanes and notes.</summary>
        private void PaintClub(Painter2D p, float beat, float pulse, float hitY)
        {
            var floorTop = hitY - 10f;
            for (var row = 0; row < 5; row++)
            {
                var y0 = Mathf.Lerp(floorTop, FieldH, row / 5f); var y1 = Mathf.Lerp(floorTop, FieldH, (row + 1) / 5f);
                var w0 = Mathf.Lerp(FieldW * 0.6f, FieldW * 1.4f, row / 5f); var w1 = Mathf.Lerp(FieldW * 0.6f, FieldW * 1.4f, (row + 1) / 5f);
                for (var col = 0; col < 8; col++)
                {
                    var lit = (row + col) % 2 == 0;
                    var c = lit ? Color.Lerp(FloorLight, LaneColors[(row + col / 2) % 3], 0.25f + 0.35f * pulse) : FloorDark;
                    c = Color.Lerp(c, Color.black, _missDim * 0.4f);
                    var x00 = FieldW * 0.5f - w0 * 0.5f + w0 * col / 8f; var x01 = x00 + w0 / 8f;
                    var x10 = FieldW * 0.5f - w1 * 0.5f + w1 * col / 8f; var x11 = x10 + w1 / 8f;
                    Poly(p, new[] { new Vector2(x00, y0), new Vector2(x01, y0), new Vector2(x11, y1), new Vector2(x10, y1) }, c);
                }
            }
            // spotlights sweeping down from the ceiling
            for (var i = 0; i < 3; i++)
            {
                var sway = Mathf.Sin(_show * (0.7f + i * 0.2f) + i * 2f) * FieldW * 0.25f;
                var c = Color.Lerp(LaneColors[i], Color.white, 0.5f); c.a = 0.05f + 0.04f * pulse;
                Poly(p, new[] { new Vector2(FieldW * (0.2f + i * 0.3f), 0f), new Vector2(FieldW * (0.2f + i * 0.3f) + 26f, 0f), new Vector2(FieldW * 0.5f + sway + 90f, floorTop), new Vector2(FieldW * 0.5f + sway - 90f, floorTop) }, c);
            }
            // speakers left and right, thumping on the beat
            for (var side = 0; side < 2; side++)
            {
                var x = side == 0 ? 8f : FieldW - 8f - 54f;
                var y = FieldH * 0.47f;
                FillRect(p, x, y, 54f, 120f, MgScenery.Hex(0x4A3C80));
                Circle(p, new Vector2(x + 27f, y + 36f), 16f + pulse * 3f, MgScenery.Hex(0x8A78C8));
                Circle(p, new Vector2(x + 27f, y + 36f), 8f, Ink);
                Circle(p, new Vector2(x + 27f, y + 88f), 22f + pulse * 4f, MgScenery.Hex(0x8A78C8));
                Circle(p, new Vector2(x + 27f, y + 88f), 11f, Ink);
            }
            // the disco ball, spinning with the tier and popping on hits
            var ballC = new Vector2(FieldW * 0.5f, FieldH * 0.21f);
            var ballR = 34f * (1f + _ballPop * 0.4f);
            p.strokeColor = new Color(0.6f, 0.6f, 0.7f); p.lineWidth = 3f;
            Line(p, new Vector2(ballC.x, 0f), new Vector2(ballC.x, ballC.y - ballR));
            Circle(p, ballC, ballR, new Color(0.75f, 0.78f, 0.9f));
            for (var i = 0; i < 24; i++)
            {
                var a = _ballSpin * 2f + i * Mathf.PI * 2f / 24f;
                var row = i % 3 - 1;
                var pt = ballC + new Vector2(Mathf.Cos(a) * ballR * (0.85f - Mathf.Abs(row) * 0.25f), row * ballR * 0.45f + Mathf.Sin(a) * 4f);
                if (Mathf.Cos(a) > -0.2f) Circle(p, pt, 4f, Color.Lerp(Color.white, LaneColors[i % 3], 0.5f + 0.5f * Mathf.Sin(a * 3f)));
            }
            // lanes: soft columns in their colours, brighter when tapped, the hit line with its pads
            for (var lane = 0; lane < 3; lane++)
            {
                var x = LaneX(lane); var w = LaneSpan * 0.86f;
                var c = LaneColors[lane]; c.a = 0.05f + _laneFlash[lane] * 1.2f;
                FillRect(p, x - w * 0.5f, hitY - FieldH * TravelFrac, w, FieldH * TravelFrac + 20f, c);
                var pad = LaneColors[lane]; pad.a = 0.9f;
                Ellipse(p, new Vector2(x, hitY), w * 0.5f, 12f, new Color(0f, 0f, 0f, 0.35f));
                p.strokeColor = pad; p.lineWidth = 4f;
                p.BeginPath();
                for (var i = 0; i <= 24; i++) { var a = Mathf.PI * 2f * i / 24f; var pt = new Vector2(x + Mathf.Cos(a) * w * 0.5f, hitY + Mathf.Sin(a) * 12f); if (i == 0) p.MoveTo(pt); else p.LineTo(pt); }
                p.ClosePath(); p.Stroke();
            }
            p.strokeColor = new Color(1f, 1f, 1f, 0.7f); p.lineWidth = 3f;
            Line(p, new Vector2(FieldW * 0.5f - LaneSpan * 1.5f, hitY), new Vector2(FieldW * 0.5f + LaneSpan * 1.5f, hitY));
            // the notes on their way down
            foreach (var (note, progress) in _game.Visible())
            {
                var x = LaneX(note.Lane); var y = NoteY(progress);
                if (y < -30f) continue;
                var r = 20f + 8f * Mathf.Clamp01((float)progress);
                Circle(p, new Vector2(x, y + 4f), r, new Color(0f, 0f, 0f, 0.3f));
                Circle(p, new Vector2(x, y), r, Ink);
                Circle(p, new Vector2(x, y), r - 3f, LaneColors[note.Lane]);
                Circle(p, new Vector2(x - r * 0.3f, y - r * 0.3f), r * 0.22f, new Color(1f, 1f, 1f, 0.7f));
            }
        }

        /// <summary>What stays over either field: hit pops, confetti, the encore glow and the lead-in veil.</summary>
        private void PaintOverlay(Painter2D p)
        {
            // hit pops: rings bursting on the line
            foreach (var pop in _pops)
            {
                var k = pop.Age / 0.32f;
                var c = pop.Perfect ? PopPerfect : PopGood; c.a = 1f - k;
                p.strokeColor = c; p.lineWidth = 5f * (1f - k) + 1f;
                p.BeginPath(); p.Arc(pop.P, 14f + k * 54f * (pop.Perfect ? 2.7f : 1.9f) * 0.5f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
            }
            foreach (var c in _confetti)
            {
                var col = c.C; col.a = Mathf.Clamp01(c.Life);
                var s = 6f;
                var d = new Vector2(Mathf.Cos(c.Spin), Mathf.Sin(c.Spin)) * s;
                Poly(p, new[] { c.P + d, c.P + new Vector2(-d.y, d.x) * 0.5f, c.P - d, c.P - new Vector2(-d.y, d.x) * 0.5f }, col);
            }
            if (_encoreGlow > 0f)
            {
                var g = new Color(1f, 0.75f, 0.3f, 0.35f * _encoreGlow);
                FillRect(p, 0f, 0f, FieldW, 18f, g); FillRect(p, 0f, FieldH - 18f, FieldW, 18f, g);
                FillRect(p, 0f, 0f, 18f, FieldH, g); FillRect(p, FieldW - 18f, 0f, 18f, FieldH, g);
            }
            if (_game.InLeadIn)
            {
                // the intro: a spotlight opening on Gooby
                var k = (float)_game.IntroProgress;
                FillRect(p, 0f, 0f, FieldW, FieldH, new Color(0f, 0f, 0f, 0.6f * (1f - k)));
            }
        }

        private void OnJudged(string kind, int lane, double noteTime)
        {
            StageJudged(kind);
            if (kind == "miss") { _missDim = 1f; return; }
            _pops.Add(new Pop { P = new Vector2(LaneX(lane), HitLineY), Age = 0f, Perfect = kind == "perfect" });
            _ballPop = 0.5f;
            if (_game.Combo >= 4 && _game.Combo % 4 == 0) FloatText(new Vector2(LaneX(lane), HitLineY - 60f), Host.Strings.T("mg.game.streak", ("n", _game.Combo)), kind == "perfect" ? PopPerfect : PopGood);
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

        private static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i <= 24; i++)
            {
                var a = Mathf.PI * 2f * i / 24f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Fill();
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _pops.Clear(); _confetti.Clear();
            _comboPill?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class ClubCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public ClubCanvas()
            {
                pickingMode = PickingMode.Ignore;
                name = "club";
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
