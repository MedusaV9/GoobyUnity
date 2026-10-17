using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Teestube host on the shared minigame frame (port of tea_party.gd + its stage): a cosy tea room — striped
    /// wallpaper, a shelf with pots, the counter — the pot tilts over the cup while the finger holds, the tea rises in
    /// the cup and on the meter beside it, where the green band and its golden core show where to stop. Release
    /// inside the band serves the cup (it slides off to the left, the next one slides in); outside or over the rim
    /// it spills. Every third perfect in a row pays the streak bonus.
    /// </summary>
    public sealed partial class TeaPartyScreen : MinigameHostScreen
    {
        private TeaPartyGame _game;
        private VisualElement _pot, _cup, _cupTea, _meter, _meterTea, _band, _core, _stream, _ghost;
        private Label _cupsLabel;
        private float _pulse, _ghostT;
        private string _shownBand = "";

        private static readonly Color Tea = new Color(0.72f, 0.45f, 0.18f);
        private static readonly Color BandGreen = new Color(0.36f, 0.74f, 0.42f, 0.78f);
        private static readonly Color CoreGold = new Color(1f, 0.68f, 0.16f, 0.92f);
        private const float MeterH = 300f, MeterW = 26f, CupW = 150f, CupH = 130f;

        public TeaPartyScreen(GameHost host) : base(host, "teaParty", "teaParty") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xF2B9C4);
        protected override Color FieldColor => MgScenery.Hex(0xFFF3E2);

        /// <summary>The running round (flow tests drive it directly).</summary>
        public TeaPartyGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new TeaPartyGame(Difficulty, seed);
            _game.Poured += OnPoured;
            _game.StreakBonus += n => Banner(Host.Strings.T("mg.teaParty.streak_banner", ("n", n)), 1.4);
            _game.Finished_ += () =>
            {
                Banner(_game.Tune.Endless ? Host.Strings.T("mg.teaParty.ende_spills") : Host.Strings.T("mg.teaParty.ende_zeit", ("cups", _game.Cups)), 2.0);
                RoundOver();
            };
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e => { if (_game != null && _game.Press()) Field.CapturePointer(e.pointerId); });
            Field.RegisterCallback<PointerUpEvent>(e => { if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId); _game?.Release(); });
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _game?.Release());
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the served cup sliding off to the left (a ghost), the cup in play, its tea, the pot above it
            _ghost = CupShape();
            _ghost.style.display = DisplayStyle.None;
            Field.Add(_ghost);
            _cup = CupShape();
            _cup.name = "cup";
            Field.Add(_cup);
            _cupTea = new VisualElement { pickingMode = PickingMode.Ignore, name = "cup-tea" };
            _cupTea.style.position = Position.Absolute;
            _cupTea.style.backgroundColor = Tea;
            _cupTea.style.borderBottomLeftRadius = 26; _cupTea.style.borderBottomRightRadius = 26;
            Field.Add(_cupTea);
            _stream = new VisualElement { pickingMode = PickingMode.Ignore, name = "stream" };
            _stream.style.position = Position.Absolute;
            _stream.style.width = 10;
            _stream.style.backgroundColor = new Color(0.78f, 0.52f, 0.24f, 0.9f);
            _stream.style.borderBottomLeftRadius = 5; _stream.style.borderBottomRightRadius = 5;
            _stream.style.display = DisplayStyle.None;
            Field.Add(_stream);
            _pot = Ux.Icon("teapot");
            _pot.name = "pot";
            _pot.style.position = Position.Absolute;
            _pot.style.width = 150; _pot.style.height = 150;
            _pot.pickingMode = PickingMode.Ignore;
            Field.Add(_pot);
            // the meter: empty glass with the band and the golden core, the tea level rising in it
            _meter = new VisualElement { pickingMode = PickingMode.Ignore, name = "meter" };
            _meter.style.position = Position.Absolute;
            _meter.style.width = MeterW; _meter.style.height = MeterH;
            _meter.style.backgroundColor = new Color(1f, 0.99f, 0.94f, 0.55f);
            _meter.style.borderTopLeftRadius = 13; _meter.style.borderTopRightRadius = 13; _meter.style.borderBottomLeftRadius = 13; _meter.style.borderBottomRightRadius = 13;
            _meter.style.borderTopWidth = 3; _meter.style.borderBottomWidth = 3; _meter.style.borderLeftWidth = 3; _meter.style.borderRightWidth = 3;
            var rim = new Color(0.85f, 0.32f, 0.25f, 0.9f);
            _meter.style.borderTopColor = rim; _meter.style.borderBottomColor = rim; _meter.style.borderLeftColor = rim; _meter.style.borderRightColor = rim;
            _meterTea = new VisualElement { pickingMode = PickingMode.Ignore };
            _meterTea.style.position = Position.Absolute; _meterTea.style.left = 0; _meterTea.style.right = 0; _meterTea.style.bottom = 0;
            _meterTea.style.backgroundColor = Tea;
            _meterTea.style.borderBottomLeftRadius = 10; _meterTea.style.borderBottomRightRadius = 10;
            _meter.Add(_meterTea);
            _band = new VisualElement { pickingMode = PickingMode.Ignore };
            _band.style.position = Position.Absolute; _band.style.left = 0; _band.style.right = 0;
            _band.style.backgroundColor = BandGreen;
            _meter.Add(_band);
            _core = new VisualElement { pickingMode = PickingMode.Ignore };
            _core.style.position = Position.Absolute; _core.style.left = 0; _core.style.right = 0;
            _core.style.backgroundColor = CoreGold;
            _meter.Add(_core);
            Field.Add(_meter);
            if (_stage != null) foreach (var flat in new[] { _ghost, _cup, _cupTea, _stream, _pot }) flat.style.visibility = Visibility.Hidden;
            _cupsLabel = Ux.Text("", "gb-md", "gb-bold");
            _cupsLabel.name = "cups";
            Ux.Abs(_cupsLabel, null, 58);
            _cupsLabel.style.left = Length.Percent(50);
            _cupsLabel.style.translate = new Translate(Length.Percent(-50), 0);
            Root.Add(_cupsLabel);
            ComboLabel.style.top = 84;
            Banner(t.T("mg.teaParty.intro"), TeaPartyGame.IntroSec + 0.7);
        }

        // ── scenery (board 06 "Tag – hell & freundlich"): a garden tea party — soft sky with a sun and two drifting
        //    clouds, two strings of bunting, round trees and a flowering hedge behind the table, the table edge the cup
        //    stands on (62 %), a gingham cloth with a lace hem, a vase of flowers and a cookie plate on the left, lawn
        //    below. Built once, pointer-transparent, behind the cup, pot and meter; the clouds drift (two translates). ──

        private readonly List<VisualElement> _clouds = new List<VisualElement>();
        private float _sceneT;
        private static readonly float[] CloudW = { 96f, 70f };
        private static readonly float[] CloudTop = { 18f, 27f };
        private static readonly float[] CloudSpeed = { 6f, 9f };
        private static readonly float[] CloudPhase = { 150f, 20f };

        private void BuildScenery()
        {
            _clouds.Clear();
            var scene = MgScenery.Layer(Field, "mgf-teaparty");
            MgScenery.Gradient(scene, MgScenery.Hex(0xB9DEFF), MgScenery.Hex(0xFFF3E2), 0f, 52f);
            var sun = MgScenery.Sun(scene, 44f, MgScenery.Hex(0xFFD166), new Color(1f, 0.95f, 0.7f, 0.5f));
            MgScenery.Pct(sun, 5f, 20f);
            for (var i = 0; i < CloudW.Length; i++)
            {
                var cloud = MgScenery.Cloud(scene, CloudW[i]);
                cloud.name = "mgf-cloud-" + i;
                cloud.style.left = 0;
                MgScenery.Pct(cloud, null, CloudTop[i]);
                cloud.style.translate = new Translate(CloudPhase[i], 0);
                _clouds.Add(cloud);
            }
            // round trees and the flowering hedge behind the table
            var treeL = MgScenery.Sprite(scene, "plant_tree_round", 110f, 150f);
            MgScenery.Pct(treeL, -6f, null, null, 46f);
            var treeR = MgScenery.Sprite(scene, "plant_tree_round", 92f, 126f);
            MgScenery.Pct(treeR, null, null, -4f, 46f);
            var hedge = MgScenery.Puffs(scene, 6, MgScenery.Hex(0x92CF8F), MgScenery.Hex(0xB8E6A8), 6);
            hedge.name = "mgf-hedge";
            MgScenery.Pct(hedge, -3f, null, -3f, 43f);
            hedge.style.height = Length.Percent(13f);
            var blossoms = MgScenery.Dots(hedge, 18, MgScenery.Hex(0xFF9BC1), 3f, 5f, 7);
            MgScenery.Pct(blossoms, 4f, 30f, 4f, 10f);
            var blossomsW = MgScenery.Dots(hedge, 10, MgScenery.Hex(0xFFFFFF), 2.5f, 4f, 19);
            MgScenery.Pct(blossomsW, 4f, 34f, 4f, 12f);
            var lawn = MgScenery.Shape(scene, MgScenery.Hex(0xA6DB98), 0f, "gb-mgf-meadow");
            MgScenery.Pct(lawn, 0, 54f, 0, 0);
            lawn.style.borderTopColor = MgScenery.Hex(0x8CCF8A);
            for (var i = 0; i < 5; i++)
            {
                var tuft = MgScenery.Tuft(lawn, MgScenery.Hex(0x7CC486), 0.8f);
                MgScenery.Pct(tuft, 4f + i * 21f, null);
                tuft.style.top = -12f;
            }
            // two strings of bunting across the garden
            var flags = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFD166), MgScenery.Hex(0x9BE7E1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xB7D8FF) };
            var b1 = MgScenery.Bunting(scene, 8, 16f, flags, MgScenery.Hex(0xC8A27A));
            MgScenery.Pct(b1, -2f, 10.5f, -2f); b1.style.height = 52f;
            var b2 = MgScenery.Bunting(scene, 6, 10f, new[] { flags[2], flags[0], flags[4], flags[1] }, MgScenery.Hex(0xC8A27A));
            MgScenery.Pct(b2, 18f, 15.5f, -4f); b2.style.height = 40f;
            // the table: its edge (the cup's floor at 62 %), the gingham cloth with a lace hem
            var cloth = MgScenery.Gingham(scene, 18f, MgScenery.Hex(0xFFFBF5), new Color(1f, 0.6f, 0.72f, 0.32f));
            cloth.name = "mgf-cloth";
            MgScenery.Pct(cloth, 0, 62f, 0, 8f);
            var hem = MgScenery.Scallop(scene, 9, 7f, MgScenery.Hex(0xFFFBF5), MgScenery.Hex(0xFF9BC1));
            MgScenery.Pct(hem, 0, null, 0, 5.5f);
            hem.style.height = Length.Percent(3f);
            var edge = MgScenery.Shape(scene, MgScenery.Hex(0xFFF1F4), 7f, "gb-mgf-table-edge");
            MgScenery.Pct(edge, -2f, 61f, -2f);
            edge.style.height = 16f;
            MgScenery.Outline(edge, 3f, MgScenery.Hex(0xF2A7BA));
            // a vase of flowers and a cookie plate at the left end of the table
            var vase = MgScenery.Shape(scene, MgScenery.Hex(0x9BE7E1), 0f, "gb-mgf-vase");
            MgScenery.Pct(vase, 6f, null, null, 38f);
            MgScenery.Size(vase, 34f, 40f);
            MgScenery.RoundBottom(vase, 16f); MgScenery.RoundTop(vase, 8f);
            MgScenery.Outline(vase, 3f, MgScenery.Hex(0x6CC7C0));
            var bouquet = MgScenery.Sprite(vase, "plant_flowers_pink", 58f, 52f);
            bouquet.style.left = -12f; bouquet.style.top = -46f;
            var plate = MgScenery.Painted(scene, (p, r) =>
            {
                var c = new Vector2(r.width * 0.5f, r.height - 7f);
                MgScenery.Oval(p, c, r.width * 0.5f - 2f, 6f, MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xE3CFC0), 2f);
                for (var i = 0; i < 3; i++)
                {
                    var at = c + new Vector2((i - 1) * 13f, -6f - (i == 1 ? 7f : 0f));
                    MgScenery.Disc(p, at, 8f, MgScenery.Hex(0xE8B77E));
                    p.strokeColor = MgScenery.Hex(0xC98F57); p.lineWidth = 2f;
                    p.BeginPath(); p.Arc(at, 8f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
                    MgScenery.Disc(p, at + new Vector2(-2.5f, -1.5f), 1.6f, MgScenery.Hex(0x7A4E2F));
                    MgScenery.Disc(p, at + new Vector2(2.5f, 2f), 1.6f, MgScenery.Hex(0x7A4E2F));
                }
            }, "gb-mgf-plate");
            MgScenery.Pct(plate, 18f, null, null, 38f);
            MgScenery.Size(plate, 52f, 32f);
            // flowers in the lawn under the table
            var petals = new[] { MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFFD166) };
            for (var i = 0; i < 5; i++)
            {
                var f = MgScenery.Flower(scene, petals[i % petals.Length], 0.8f);
                MgScenery.Pct(f, 6f + i * 20f, null, null, 1f + (i % 2) * 1.5f);
            }
        }

        /// <summary>The clouds drift slowly to the right and wrap (two translates a frame; still with reduced motion).</summary>
        private void DriftClouds()
        {
            if (FieldW <= 0f) return;
            var t = ReducedMotion ? 0f : _sceneT;
            for (var i = 0; i < _clouds.Count; i++)
                _clouds[i].style.translate = new Translate(Mathf.Repeat(CloudPhase[i] + t * CloudSpeed[i], FieldW + CloudW[i]) - CloudW[i], 0);
        }

        private static VisualElement CupShape()
        {
            var cup = new VisualElement { pickingMode = PickingMode.Ignore };
            cup.style.position = Position.Absolute;
            cup.style.width = CupW; cup.style.height = CupH;
            cup.style.backgroundColor = new Color(1f, 0.97f, 0.93f);
            cup.style.borderBottomLeftRadius = 30; cup.style.borderBottomRightRadius = 30;
            cup.style.borderTopLeftRadius = 6; cup.style.borderTopRightRadius = 6;
            cup.style.borderTopWidth = 6; cup.style.borderBottomWidth = 4; cup.style.borderLeftWidth = 4; cup.style.borderRightWidth = 4;
            var rim = new Color(0.71f, 0.46f, 0.23f);
            cup.style.borderTopColor = rim; cup.style.borderBottomColor = rim; cup.style.borderLeftColor = rim; cup.style.borderRightColor = rim;
            var handle = new VisualElement { pickingMode = PickingMode.Ignore };
            handle.style.position = Position.Absolute; handle.style.right = -34; handle.style.top = 30;
            handle.style.width = 44; handle.style.height = 56;
            handle.style.borderTopLeftRadius = 22; handle.style.borderTopRightRadius = 22; handle.style.borderBottomLeftRadius = 22; handle.style.borderBottomRightRadius = 22;
            handle.style.borderTopWidth = 8; handle.style.borderBottomWidth = 8; handle.style.borderLeftWidth = 8; handle.style.borderRightWidth = 8;
            handle.style.borderTopColor = rim; handle.style.borderBottomColor = rim; handle.style.borderLeftColor = rim; handle.style.borderRightColor = rim;
            cup.Add(handle);
            return cup;
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _pulse += (float)dt;
            _sceneT += (float)dt;
            if (_ghostT > 0f) _ghostT = Mathf.Max(0f, _ghostT - (float)dt);
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            DriftClouds();
            var counterTop = FieldH * 0.62f;
            var cupX = FieldW * 0.5f - CupW * 0.5f;
            // the cup slides in from the right while serving
            var slide = (float)_game.CupSlide * (FieldW * 0.5f + CupW);
            var cupLeft = cupX + slide;
            var cupTop = counterTop - CupH - 4f;
            _cup.style.left = cupLeft; _cup.style.top = cupTop;
            var level = (float)_game.Level;
            var teaH = Mathf.Clamp01(level) * (CupH - 12f);
            _cupTea.style.left = cupLeft + 4; _cupTea.style.width = CupW - 8;
            _cupTea.style.top = cupTop + CupH - 4 - teaH; _cupTea.style.height = teaH;
            _cupTea.style.display = teaH > 1f ? DisplayStyle.Flex : DisplayStyle.None;
            // the pot over the cup: tilts and pours while holding
            var pouring = _game.Holding;
            _pot.style.left = FieldW * 0.5f - 60f; _pot.style.top = cupTop - 190f + (pouring ? 12f : 0f) + Mathf.Sin(_pulse * 3f) * 3f;
            _pot.style.rotate = new Rotate(pouring ? -32f : -6f);
            _stream.style.display = pouring ? DisplayStyle.Flex : DisplayStyle.None;
            if (pouring)
            {
                _stream.style.left = FieldW * 0.5f + 46f; _stream.style.top = cupTop - 70f;
                _stream.style.height = 70f + CupH - 4f - teaH;
            }
            // the meter beside the cup
            var meterLeft = FieldW * 0.5f + CupW * 0.5f + 60f;
            _meter.style.left = meterLeft; _meter.style.top = counterTop - MeterH - 20f;
            _meterTea.style.height = Mathf.Clamp01(level) * (MeterH - 6f);
            var band = _game.Band;
            var key = band.Center.ToString("F4");
            if (_shownBand != key)
            {
                _shownBand = key;
                _band.style.bottom = (float)(band.Center - band.Half) * (MeterH - 6f); _band.style.height = (float)(band.Half * 2.0) * (MeterH - 6f);
                _core.style.bottom = (float)(band.Center - band.PerfectHalf) * (MeterH - 6f); _core.style.height = (float)(band.PerfectHalf * 2.0) * (MeterH - 6f);
            }
            // the served cup gliding off to the left
            if (_ghostT > 0f)
            {
                _ghost.style.display = DisplayStyle.Flex;
                var k = 1f - _ghostT / 0.6f;
                _ghost.style.left = cupX - k * (FieldW * 0.5f + CupW); _ghost.style.top = cupTop;
                _ghost.style.opacity = 1f - k * 0.6f;
            }
            else _ghost.style.display = DisplayStyle.None;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.teaParty.spills", ("spills", _game.Spills), ("max", _game.Tune.EndlessMaxSpills))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            ComboLabel.text = _game.Streak > 0 ? t.T("mg.game.streak", ("n", _game.Streak)) : "";
            _cupsLabel.text = t.T("mg.teaParty.cups", ("n", _game.Cups));
            if (_stage != null) RenderStage();
        }

        private void OnPoured(string result, int points, int streak)
        {
            StagePoured(result);
            var at = new Vector2(FieldW * 0.5f, FieldH * 0.62f - CupH - 30f);
            switch (result)
            {
                case "perfect": FloatText(at, "+" + points, new Color(1f, 0.72f, 0.2f)); _ghostT = 0.6f; break;
                case "good": FloatText(at, "+" + points, new Color(0.42f, 0.6f, 0.36f)); _ghostT = 0.6f; break;
                default: Banner(Host.Strings.T("mg.teaParty.spill"), 1.2); break;
            }
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _shownBand = "";
            _cupsLabel?.RemoveFromHierarchy();
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
