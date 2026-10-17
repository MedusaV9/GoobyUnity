using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Gooby sagt" host on the shared minigame frame (port of gooby_says.gd + its stage): a little theatre — red
    /// curtain, spotlit boards, Gooby in the middle — with four coloured pads in front. Gooby plays the sequence
    /// (the pads light up one after the other), then the player taps it back; the state line says whose turn it is
    /// and how far the round is, the pad rims pulse red when the player hesitates. From round 6 chords light two pads.
    /// With a GPU the theatre, Gooby and the pads are the 3D stage of <c>GoobySaysScreen.Stage3D.cs</c>; the 2D pads stay
    /// as invisible tap squares over their 3D twins.
    /// </summary>
    public sealed partial class GoobySaysScreen : MinigameHostScreen
    {
        private GoobySaysGame _game;
        private readonly List<VisualElement> _pads = new List<VisualElement>();
        private VisualElement _gooby, _spot;
        private Label _state;
        private int _chordPartner = -1;
        private float _pulse;

        // the stage's pad colours (gooby_says_stage3d.gd PAD_COLORS): teal, pink, sun, leaf
        private static readonly Color[] PadColors = { new Color(0.35f, 0.79f, 0.73f), new Color(1f, 0.48f, 0.66f), new Color(1f, 0.82f, 0.4f), new Color(0.56f, 0.82f, 0.42f) };
        private static readonly Color Plum = MgScenery.Hex(0x5A4A8C);
        private const float PadSize = 128f, PadGap = 22f;

        public GoobySaysScreen(GameHost host) : base(host, "goobySays", "goobySays") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        /// <summary>The speed bonus re-averages after a round — only the round points get the score pop.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.GainsOnly;
        protected override Color BackdropColor => MgScenery.Hex(0x4B3D72);
        protected override Color FieldColor => Plum;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public GoobySaysGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new GoobySaysGame(Difficulty, seed);
            _game.PadLit += (pad, partner) => _chordPartner = partner;
            _game.InputOpen += () => _chordPartner = -1;
            _game.RoundWon += n => { FloatText(GoobyAt(), "+" + _game.Tune.RoundPoints, new Color(0.6f, 0.95f, 0.7f)); if (n % 3 == 0) Banner(Host.Strings.T("mg.goobySays.round", ("n", n + 1)), 1.0); StageRoundWon(); };
            _game.Failed += () => { FloatText(PadsAt(), Host.Strings.T("mg.goobySays.oops"), new Color(1f, 0.97f, 0.93f)); StageFailed(); };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the spotlight cone and Gooby, the star of the show
            _spot = new VisualElement { pickingMode = PickingMode.Ignore };
            _spot.style.position = Position.Absolute;
            _spot.style.left = Length.Percent(50); _spot.style.top = Length.Percent(24);
            _spot.style.translate = new Translate(Length.Percent(-50), 0);
            _spot.style.width = 190; _spot.style.height = 190;
            _spot.style.borderTopLeftRadius = 95; _spot.style.borderTopRightRadius = 95; _spot.style.borderBottomLeftRadius = 95; _spot.style.borderBottomRightRadius = 95;
            _spot.style.backgroundColor = new Color(1f, 0.93f, 0.78f, 0.22f);
            Field.Add(_spot);
            _gooby = Ux.Portrait(120);
            _gooby.style.position = Position.Absolute;
            _gooby.style.left = Length.Percent(50); _gooby.style.top = Length.Percent(28);
            _gooby.style.translate = new Translate(Length.Percent(-50), 0);
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            if (_stage != null) { _spot.style.display = DisplayStyle.None; _gooby.style.display = DisplayStyle.None; }
            // the four pads in a square in front of the stage
            for (var i = 0; i < 4; i++)
            {
                var pad = new VisualElement { pickingMode = PickingMode.Position, name = "pad-" + i };
                pad.style.position = Position.Absolute;
                pad.style.width = PadSize; pad.style.height = PadSize;
                pad.style.left = Length.Percent(50); pad.style.top = Length.Percent(56);
                var dx = (i % 2 == 0 ? -1f : 1f) * (PadSize + PadGap) * 0.5f - PadSize * 0.5f;
                var dy = (i / 2) * (PadSize + PadGap);
                pad.style.translate = new Translate(dx, dy);
                pad.style.borderTopLeftRadius = 30; pad.style.borderTopRightRadius = 30; pad.style.borderBottomLeftRadius = 30; pad.style.borderBottomRightRadius = 30;
                pad.style.borderTopWidth = 5; pad.style.borderBottomWidth = 5; pad.style.borderLeftWidth = 5; pad.style.borderRightWidth = 5;
                pad.style.justifyContent = Justify.Center; pad.style.alignItems = Align.Center;
                var glyph = Ux.Icon(new[] { "star", "heart", "sun", "leaf" }[i]);
                glyph.style.width = 54; glyph.style.height = 54;
                glyph.style.opacity = 0.9f;
                glyph.pickingMode = PickingMode.Ignore;
                pad.Add(glyph);
                var index = i;
                pad.RegisterCallback<PointerDownEvent>(_ => { if (_game != null) { _game.Tap(index); StageTapped(index); } });
                if (_stage != null)
                {
                    pad.style.backgroundColor = Color.clear;
                    pad.style.borderTopWidth = 0; pad.style.borderBottomWidth = 0; pad.style.borderLeftWidth = 0; pad.style.borderRightWidth = 0;
                    glyph.style.display = DisplayStyle.None;
                }
                Field.Add(pad);
                _pads.Add(pad);
            }
            // whose turn, and how far the round is
            _state = Ux.Text("", "gb-md", "gb-bold");
            _state.name = "state";
            Ux.Abs(_state, null, 84);
            _state.style.left = Length.Percent(50);
            _state.style.translate = new Translate(Length.Percent(-50), 0);
            _state.style.color = new Color(1f, 0.97f, 0.93f);
            Root.Add(_state);
            ComboLabel.style.top = 112;
            ComboLabel.style.color = new Color(1f, 0.85f, 0.5f);
            Banner(t.T("mg.goobySays.intro"), GoobySaysGame.IntroSec + 0.7);
        }

        // ── scenery (board 06 "Abend – warm & gemütlich"): a little theatre — violet back wall with faint twinkles, a
        //    gold scalloped valance with bunting, raspberry curtains tied back, two spotlights aimed at Gooby's round
        //    podium, footlights along the lip and a warm plank floor under the pads. Built once, pointer-transparent,
        //    behind the spot, Gooby and the pads; nothing moves (the reactive pool stays the host's _spot). ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-goobysays");
            MgScenery.Gradient(scene, MgScenery.Hex(0x4E3F80), MgScenery.Hex(0x7A67AD), 0f, 45f);
            var twinkles = MgScenery.Dots(scene, 26, new Color(1f, 0.92f, 0.75f, 0.35f), 1.5f, 3f, 11);
            MgScenery.Pct(twinkles, 20f, 8f, 20f);
            twinkles.style.height = Length.Percent(30f);
            float[] starAt = { 30f, 66f, 44f, 58f }; float[] starTop = { 12f, 15f, 21f, 9f };
            for (var i = 0; i < starAt.Length; i++)
            {
                var tw = MgScenery.Twinkle(scene, i % 2 == 0 ? 14f : 10f, new Color(1f, 0.9f, 0.6f, 0.7f));
                MgScenery.Pct(tw, starAt[i], starTop[i]);
            }
            // the plank floor with the stage lip and footlights
            var floor = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, MgScenery.Hex(0xD19C6E));
                p.strokeColor = MgScenery.Hex(0xBB8559); p.lineWidth = 2.5f;
                for (var i = 1; i < 9; i++)
                {
                    var y = r.height * Mathf.Pow(i / 9f, 1.35f);
                    p.BeginPath(); p.MoveTo(new Vector2(0f, y)); p.LineTo(new Vector2(r.width, y)); p.Stroke();
                    for (var j = 0; j < 3; j++)
                    {
                        var x = r.width * ((j + (i % 2) * 0.5f + 0.25f) / 3f);
                        var y0 = r.height * Mathf.Pow((i - 1) / 9f, 1.35f);
                        p.BeginPath(); p.MoveTo(new Vector2(x, y0)); p.LineTo(new Vector2(x, y)); p.Stroke();
                    }
                }
            }, "gb-mgf-planks");
            MgScenery.Pct(floor, 0, 45f, 0, 0);
            var lip = MgScenery.Shape(scene, MgScenery.Hex(0xF6E4C8), 0f, "gb-mgf-stage-lip");
            MgScenery.Pct(lip, 0, 44f, 0);
            lip.style.height = 14f;
            lip.style.borderBottomWidth = 4; lip.style.borderBottomColor = MgScenery.Hex(0xC8A27A);
            var foot = MgScenery.Painted(lip, (p, r) =>
            {
                for (var i = 0; i < 7; i++)
                {
                    var at = new Vector2(r.width * (i + 0.5f) / 7f, r.height * 0.45f);
                    MgScenery.Disc(p, at, 9f, new Color(1f, 0.86f, 0.45f, 0.3f));
                    MgScenery.Disc(p, at, 4f, MgScenery.Hex(0xFFD166));
                }
            });
            Ux.Abs(foot, 0, 0, 0, 0);
            // Gooby's round podium at the lip
            var podium = MgScenery.Shape(scene, MgScenery.Hex(0xF2C79A), 0f, "gb-mgf-podium");
            podium.style.left = Length.Percent(50); podium.style.translate = new Translate(Length.Percent(-50), 0);
            MgScenery.Pct(podium, null, 40f);
            MgScenery.Size(podium, 190f, 46f);
            MgScenery.Round(podium, 23f);
            MgScenery.Outline(podium, 4f, MgScenery.Hex(0xFFF1DC));
            podium.style.borderBottomColor = MgScenery.Hex(0xC8A27A);
            // two spotlights from the top corners aimed at the podium
            for (var s = 0; s < 2; s++)
            {
                var beam = MgScenery.Beam(scene, new Color(1f, 0.95f, 0.78f, 0.07f), 0.12f);
                beam.name = "mgf-spot-" + s;
                MgScenery.Pct(beam, s == 0 ? -6f : 66f, 4f);
                beam.style.width = Length.Percent(40f); beam.style.height = Length.Percent(42f);
                beam.style.transformOrigin = new TransformOrigin(Length.Percent(50), 0f, 0f);
                beam.style.rotate = new Rotate(s == 0 ? -34f : 34f);
                var can = MgScenery.Shape(scene, MgScenery.Hex(0x3A2F63), 10f, "gb-mgf-spotlamp");
                MgScenery.Pct(can, s == 0 ? 8f : null, 5f, s == 0 ? (float?)null : 8f);
                MgScenery.Size(can, 26f, 22f);
                MgScenery.Outline(can, 2f, MgScenery.Hex(0x9C8BD0));
                var lens = MgScenery.Shape(can, MgScenery.Hex(0xFFE9A8), 5f);
                lens.style.left = 6f; lens.style.bottom = 2f; MgScenery.Size(lens, 10f, 8f);
            }
            // the curtains, tied back with gold ropes
            for (var s = 0; s < 2; s++)
            {
                var curtain = MgScenery.Shape(scene, MgScenery.Hex(0xE8718F), 0f, "gb-mgf-curtain");
                curtain.name = "mgf-curtain-" + s;
                MgScenery.Pct(curtain, s == 0 ? -2f : null, 3f, s == 0 ? (float?)null : -2f);
                curtain.style.width = Length.Percent(24f); curtain.style.height = Length.Percent(44f);
                if (s == 0) curtain.style.borderBottomRightRadius = 70f; else curtain.style.borderBottomLeftRadius = 70f;
                MgScenery.Outline(curtain, 3f, MgScenery.Hex(0xCC5876));
                for (var f = 0; f < 3; f++)
                {
                    var fold = MgScenery.Shape(curtain, MgScenery.Hex(0xF590A8), 6f);
                    MgScenery.Pct(fold, 12f + f * 28f, 0f, null, 18f + f * 6f);
                    fold.style.width = Length.Percent(10f);
                }
                var rope = MgScenery.Shape(curtain, MgScenery.Hex(0xFFD166), 5f, "gb-mgf-tieback");
                MgScenery.Pct(rope, -4f, 62f, -4f);
                rope.style.height = 10f;
                MgScenery.Outline(rope, 2f, MgScenery.Hex(0xE0A93E));
                var tassel = MgScenery.Shape(curtain, MgScenery.Hex(0xFFD166), 7f);
                MgScenery.Pct(tassel, s == 0 ? null : 8f, 62f, s == 0 ? 8f : (float?)null);
                MgScenery.Size(tassel, 14f, 22f);
                MgScenery.Outline(tassel, 2f, MgScenery.Hex(0xE0A93E));
            }
            // the valance with its bunting
            var bunting = MgScenery.Bunting(scene, 9, 12f, new[] { MgScenery.Hex(0x7CCF9B), MgScenery.Hex(0xFFD166), MgScenery.Hex(0xB7D8FF), MgScenery.Hex(0xFF9BC1) }, MgScenery.Hex(0xFFF1DC));
            MgScenery.Pct(bunting, 6f, 6.5f, 6f);
            bunting.style.height = 44f;
            var valance = MgScenery.Scallop(scene, 7, 9f, MgScenery.Hex(0xF6C453), MgScenery.Hex(0xD99A2E));
            valance.name = "mgf-valance";
            MgScenery.Pct(valance, 0, 0, 0);
            valance.style.height = Length.Percent(7f);
        }

        private Vector2 GoobyAt() => new Vector2(FieldW * 0.5f, FieldH * 0.28f);
        private Vector2 PadsAt() => new Vector2(FieldW * 0.5f, FieldH * 0.56f + PadSize + PadGap * 0.5f);

        protected override void TickGame(double dt) { _game.Tick(dt); _pulse += (float)dt; }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var urgency = (float)_game.TimeoutUrgency;
            var warn = urgency > 0f ? 0.5f + 0.5f * Mathf.Sin(_pulse * (6f + urgency * 8f)) : 0f;
            for (var i = 0; i < _pads.Count && _stage == null; i++)
            {
                var pad = _pads[i];
                var lit = _game.LitPad == i || (_game.Phase == GoobySaysGame.Watch && _game.LitLeft > 0.0 && _chordPartner == i) || _game.ChordFirst == i;
                var baseCol = PadColors[i];
                pad.style.backgroundColor = lit ? Color.Lerp(baseCol, Color.white, 0.45f) : baseCol;
                var rim = lit ? Color.white : urgency > 0f ? Color.Lerp(new Color(0f, 0f, 0f, 0.35f), new Color(1f, 0.3f, 0.3f), warn * urgency) : new Color(0f, 0f, 0f, 0.35f);
                pad.style.borderTopColor = rim; pad.style.borderBottomColor = rim; pad.style.borderLeftColor = rim; pad.style.borderRightColor = rim;
                pad.style.scale = new Scale(Vector2.one * (lit ? 1.06f : 1f));
            }
            var watching = _game.Phase == GoobySaysGame.Watch;
            if (_stage != null) RenderStage(watching, urgency);
            _spot.style.backgroundColor = new Color(1f, 0.93f, 0.78f, watching ? 0.32f : 0.18f);
            _gooby.style.scale = new Scale(Vector2.one * (watching ? 1f + 0.03f * Mathf.Sin(_pulse * 6f) : 1f));
            var steps = "  " + Mathf.Min(_game.StepIndex, _game.Sequence.Count) + "/" + _game.Sequence.Count;
            var current = _game.CurrentStep;
            _state.text = watching ? t.T("mg.goobySays.watch") : current.HasValue && current.Value.IsChord ? t.T("mg.goobySays.chord") + steps : t.T("mg.goobySays.go") + steps;
            TimeLabel.text = t.T("mg.game.time", ("sec", (int)_game.Elapsed));
            ScoreLabel.text = _game.LiveScore.ToString();
            ComboLabel.text = t.T("mg.goobySays.round", ("n", Mathf.Max(1, _game.RoundNo)));
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _pads.Clear();
            _state?.RemoveFromHierarchy();
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }
    }
}
