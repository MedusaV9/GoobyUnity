using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Möhrenwache host on the shared minigame frame (port of carrot_guard.gd + its stage): the carrot bed as a
    /// 3×3 field of mounds on dark soil, the row of carrots still in the bed along the top; moles pop out of the
    /// mounds (a tap bonks them), the mole king rises with a crown after twenty bonks and takes three taps. Combo
    /// pips show the run to the next +3.
    /// </summary>
    public sealed partial class CarrotGuardScreen : MinigameHostScreen
    {
        private CarrotGuardGame _game;
        private readonly List<VisualElement> _mounds = new List<VisualElement>();
        private readonly List<VisualElement> _moles = new List<VisualElement>();
        private readonly List<VisualElement> _carrots = new List<VisualElement>();
        private readonly List<VisualElement> _pips = new List<VisualElement>();
        private VisualElement _king, _crown;
        private Label _carrotLabel;
        private float _pulse;

        private const float MoundW = 92f, MoundH = 46f, CellX = 106f, CellY = 120f;
        private static readonly Color Soil = new Color(0.45f, 0.3f, 0.2f);
        private static readonly Color Mound = new Color(0.36f, 0.23f, 0.15f);
        private static readonly Color Fur = new Color(0.56f, 0.42f, 0.36f);   // lighter than the soil, so the head reads

        public CarrotGuardScreen(GameHost host) : base(host, "carrotGuard", "carrotGuard") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0x8CC985);
        protected override Color FieldColor => MgScenery.Hex(0xA3D89C);

        /// <summary>The running round (flow tests drive it directly).</summary>
        public CarrotGuardGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new CarrotGuardGame(Difficulty, seed);
            _game.Bonked += (hole, delta, combo) => { StageBonk(hole, false, false); FloatText(MoundAt(hole), "+" + delta, new Color(0.24f, 0.55f, 0.32f)); if (combo % _game.Tune.ComboBonusAt == 0) Banner(Host.Strings.T("mg.carrotGuard.combo"), 0.9); };
            _game.Stolen += (hole, left) => { StageSteal(hole); FloatText(MoundAt(hole), Host.Strings.T("mg.carrotGuard.steal"), new Color(0.85f, 0.3f, 0.3f)); };
            _game.KingRises += hole => Banner(Host.Strings.T("mg.carrotGuard.king"), 1.6);
            _game.KingHit += (hole, hp) => { StageBonk(hole, true, false); FloatText(MoundAt(hole), "×" + hp, new Color(0.95f, 0.75f, 0.2f)); };
            _game.KingDown += (hole, delta) => { StageBonk(hole, true, true); Banner(Host.Strings.T("mg.carrotGuard.king_defeated"), 1.6); };
            _game.Finished_ += () => { if (_game.Carrots <= 0) Banner(Host.Strings.T("mg.carrotGuard.empty"), 1.6); RoundOver(); };
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the bed: dark soil with a wooden edging, on the meadow
            var bed = new VisualElement { pickingMode = PickingMode.Ignore };
            bed.style.position = Position.Absolute;
            bed.style.left = Length.Percent(4); bed.style.right = Length.Percent(4);
            bed.style.top = Length.Percent(22); bed.style.bottom = Length.Percent(10);
            bed.style.backgroundColor = Soil;
            bed.style.borderTopLeftRadius = 18; bed.style.borderTopRightRadius = 18; bed.style.borderBottomLeftRadius = 18; bed.style.borderBottomRightRadius = 18;
            var edging = new Color(0.73f, 0.55f, 0.38f);
            bed.style.borderTopWidth = 8; bed.style.borderBottomWidth = 8; bed.style.borderLeftWidth = 8; bed.style.borderRightWidth = 8;
            bed.style.borderTopColor = edging; bed.style.borderBottomColor = edging; bed.style.borderLeftColor = edging; bed.style.borderRightColor = edging;
            bed.name = "mgf-bed";
            Field.Add(bed);
            DressBed(bed);
            if (_stage != null) bed.style.display = DisplayStyle.None;
            // the moles (and the king) first, the mounds after them: a mole rises from behind its mound's rim
            for (var i = 0; i < 9; i++)
            {
                var mole = MoleShape(false);
                mole.style.display = DisplayStyle.None;
                Field.Add(mole);
                _moles.Add(mole);
            }
            _king = MoleShape(true);
            _king.style.display = DisplayStyle.None;
            Field.Add(_king);
            for (var i = 0; i < 9; i++)
            {
                var mound = new VisualElement { pickingMode = PickingMode.Position, name = "mound-" + i };
                mound.style.position = Position.Absolute;
                mound.style.width = MoundW; mound.style.height = MoundH;
                mound.style.borderTopLeftRadius = MoundW; mound.style.borderTopRightRadius = MoundW; mound.style.borderBottomLeftRadius = MoundW; mound.style.borderBottomRightRadius = MoundW;
                mound.style.backgroundColor = Mound;
                var hole = i;
                mound.RegisterCallback<PointerDownEvent>(_ => _game?.Tap(hole));
                if (_stage != null) mound.style.opacity = 0f;
                Field.Add(mound);
                _mounds.Add(mound);
            }
            // the carrots still in the bed, along the top
            for (var i = 0; i < _game.Tune.Carrots; i++)
            {
                var c = Ux.Icon("food_carrot");
                c.style.position = Position.Absolute;
                c.style.width = 28; c.style.height = 28;
                c.style.left = Length.Percent(6 + i * 9.2f); c.style.top = Length.Percent(12);
                c.pickingMode = PickingMode.Ignore;
                if (_stage != null) c.style.visibility = Visibility.Hidden;
                Field.Add(c);
                _carrots.Add(c);
            }
            // combo pips: five dots filling up to the next bonus
            for (var i = 0; i < _game.Tune.ComboBonusAt; i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.style.position = Position.Absolute;
                pip.style.width = 14; pip.style.height = 14;
                pip.style.borderTopLeftRadius = 7; pip.style.borderTopRightRadius = 7; pip.style.borderBottomLeftRadius = 7; pip.style.borderBottomRightRadius = 7;
                pip.style.left = Length.Percent(30 + i * 8); pip.style.top = Length.Percent(17.5f);
                Field.Add(pip);
                _pips.Add(pip);
            }
            _carrotLabel = Ux.Text("", "gb-md", "gb-bold");
            _carrotLabel.name = "carrots";
            Ux.Abs(_carrotLabel, null, 58);
            _carrotLabel.style.left = Length.Percent(50);
            _carrotLabel.style.translate = new Translate(Length.Percent(-50), 0);
            Root.Add(_carrotLabel);
            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.carrotGuard.intro"), CarrotGuardGame.IntroSec + 0.7);
        }

        // ── scenery (board 06 vegetable garden): a strip of sky with clouds, hills and two round trees over the meadow,
        //    tufts and flowers around the bed, a picket fence with tulips along the bottom; in the bed furrows, pebbles
        //    and a plank grain on the edging. All static, pointer-transparent, behind the moles and mounds. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-carrotguard");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xE6F3FF), 0f, 11f);
            var c1 = MgScenery.Cloud(scene, 70f);
            MgScenery.Pct(c1, 6f, 1.5f);
            var c2 = MgScenery.Cloud(scene, 54f, MgScenery.Hex(0xF4F8FF));
            MgScenery.Pct(c2, 66f, 3f);
            var farC = MgScenery.Hex(0xBFE6CC); var farRim = MgScenery.Hex(0xD9F3E1);
            for (var i = 0; i < 3; i++)
            {
                var hill = MgScenery.Hill(scene, 170f, 34f + (i % 2) * 10f, farC, farRim);
                MgScenery.Pct(hill, -10f + i * 38f, null, null, 88.5f);
            }
            var treeL = MgScenery.Sprite(scene, "plant_tree_round", 40f, 52f);
            MgScenery.Pct(treeL, 1f, null, null, 88f);
            var treeR = MgScenery.Sprite(scene, "plant_tree_round", 34f, 44f);
            MgScenery.Pct(treeR, null, null, 3f, 88f);
            // the meadow's own edge under the hills
            var edge = MgScenery.Shape(scene, MgScenery.Hex(0x8FCC89));
            MgScenery.Pct(edge, 0, 11f, 0);
            edge.style.height = 4f;
            var tuftC = MgScenery.Hex(0x74BF76);
            // tufts beside the combo pips (they sit at 30–66 %), never under them
            var tuftAt = new[] { 3f, 15f, 76f, 89f };
            for (var i = 0; i < tuftAt.Length; i++)
            {
                var tuft = MgScenery.Tuft(scene, tuftC, 0.8f);
                MgScenery.Pct(tuft, tuftAt[i], 18.5f);
            }
            // the picket fence along the bottom with tulips and daisies in front of it
            var fence = MgScenery.Fence(scene, 8, 40f, MgScenery.Hex(0xF6E4C8), MgScenery.Hex(0xC8A27A));
            fence.name = "mgf-fence";
            MgScenery.Pct(fence, null, null, null, 2.5f);
            var plants = new[] { "plant_tulips_red", "plant_daisies", "plant_tulips_white", "plant_flowers_pink" };
            for (var i = 0; i < 4; i++)
            {
                var p = MgScenery.Sprite(scene, plants[i], 36f, 40f);
                MgScenery.Pct(p, 3f + i * 25f, null, null, 0.5f);
            }
        }

        /// <summary>Inside the bed (children of the soil box, so under every mole and mound): furrows, pebbles, a plank grain.</summary>
        private static void DressBed(VisualElement bed)
        {
            var furrow = MgScenery.Hex(0x533826);
            for (var i = 0; i < 4; i++)
            {
                var f = MgScenery.Shape(bed, furrow, 5f, "gb-mgf-furrow");
                MgScenery.Pct(f, 5f, 8f + i * 25f, 5f);
                f.style.height = 10f;
            }
            var pebble = MgScenery.Hex(0x7A5C46);
            for (var i = 0; i < 10; i++)
            {
                var p = MgScenery.Shape(bed, pebble, 4f, "gb-mgf-pebble");
                MgScenery.Pct(p, 7f + (i * 37) % 86, 4f + (i * 53) % 92);
                MgScenery.Size(p, 8f + (i % 3) * 3f, 6f + (i % 2) * 2f);
            }
            // grain lines on the wooden edging (top and bottom planks)
            for (var i = 0; i < 2; i++)
            {
                var grain = MgScenery.Shape(bed, MgScenery.Hex(0xD9B088), 2f);
                grain.style.left = 14f; grain.style.right = 14f; grain.style.height = 2f;
                if (i == 0) grain.style.top = -5f; else grain.style.bottom = -5f;
            }
        }

        /// <summary>A mole: round furry head with a pink snout, two bead eyes and little paws; the king wears a crown.</summary>
        private VisualElement MoleShape(bool king)
        {
            var size = king ? 100f : 78f;
            var m = new VisualElement { pickingMode = PickingMode.Ignore };
            m.style.position = Position.Absolute;
            m.style.width = size; m.style.height = size;
            m.style.borderTopLeftRadius = size * 0.5f; m.style.borderTopRightRadius = size * 0.5f; m.style.borderBottomLeftRadius = size * 0.3f; m.style.borderBottomRightRadius = size * 0.3f;
            m.style.backgroundColor = king ? new Color(0.35f, 0.24f, 0.22f) : Fur;
            foreach (var x in new[] { 0.32f, 0.68f })
            {
                var eye = new VisualElement { pickingMode = PickingMode.Ignore };
                eye.style.position = Position.Absolute;
                eye.style.left = size * x - 4; eye.style.top = size * 0.38f;
                eye.style.width = 8; eye.style.height = 8;
                eye.style.borderTopLeftRadius = 4; eye.style.borderTopRightRadius = 4; eye.style.borderBottomLeftRadius = 4; eye.style.borderBottomRightRadius = 4;
                eye.style.backgroundColor = new Color(0.12f, 0.1f, 0.1f);
                m.Add(eye);
            }
            var snout = new VisualElement { pickingMode = PickingMode.Ignore };
            snout.style.position = Position.Absolute;
            snout.style.left = size * 0.5f - 11; snout.style.top = size * 0.52f;
            snout.style.width = 22; snout.style.height = 16;
            snout.style.borderTopLeftRadius = 11; snout.style.borderTopRightRadius = 11; snout.style.borderBottomLeftRadius = 11; snout.style.borderBottomRightRadius = 11;
            snout.style.backgroundColor = new Color(1f, 0.6f, 0.62f);
            m.Add(snout);
            foreach (var x in new[] { 0.12f, 0.72f })
            {
                var paw = new VisualElement { pickingMode = PickingMode.Ignore };
                paw.style.position = Position.Absolute;
                paw.style.left = size * x; paw.style.top = size * 0.8f;
                paw.style.width = size * 0.16f; paw.style.height = size * 0.14f;
                paw.style.borderTopLeftRadius = 8; paw.style.borderTopRightRadius = 8; paw.style.borderBottomLeftRadius = 8; paw.style.borderBottomRightRadius = 8;
                paw.style.backgroundColor = new Color(0.9f, 0.75f, 0.7f);
                m.Add(paw);
            }
            if (king)
            {
                _crown = Ux.Icon("star");
                _crown.style.position = Position.Absolute;
                _crown.style.left = size * 0.5f - 22; _crown.style.top = -26;
                _crown.style.width = 44; _crown.style.height = 44;
                _crown.pickingMode = PickingMode.Ignore;
                m.Add(_crown);
            }
            return m;
        }

        private Vector2 MoundCentre(int hole)
        {
            var col = hole % 3; var row = hole / 3;
            return new Vector2(FieldW * 0.5f + (col - 1) * CellX, FieldH * 0.36f + row * CellY);
        }

        private Vector2 MoundAt(int hole) => MoundCentre(hole) + new Vector2(0f, -40f);

        protected override void TickGame(double dt) { _game.Tick(dt); _pulse += (float)dt; }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            for (var i = 0; i < 9; i++)
            {
                var c = MoundCentre(i);
                _mounds[i].style.left = c.x - MoundW * 0.5f; _mounds[i].style.top = c.y - MoundH * 0.5f;
                _moles[i].style.display = DisplayStyle.None;
            }
            foreach (var m in _game.Moles)
            {
                var v = _moles[m.Hole];
                var c = MoundCentre(m.Hole);
                var up = (float)m.Up;
                v.style.display = DisplayStyle.Flex;
                v.style.left = c.x - 39f; v.style.top = c.y - 10f - up * 58f;
                v.style.height = 78f * Mathf.Lerp(0.2f, 1f, up);
                v.style.opacity = m.Left < 0.25 ? 0.6f + 0.4f * Mathf.Sin(_pulse * 25f) : 1f;   // flickers just before it dives
            }
            var king = _game.TheKing;
            if (king != null)
            {
                var c = MoundCentre(king.Hole);
                var up = (float)king.Up;
                _king.style.display = DisplayStyle.Flex;
                _king.style.left = c.x - 50f; _king.style.top = c.y - 14f - up * 76f;
                _king.style.height = 100f * Mathf.Lerp(0.2f, 1f, up);
                _king.style.scale = new Scale(Vector2.one * (1f + 0.04f * Mathf.Sin(_pulse * 8f)));
            }
            else _king.style.display = DisplayStyle.None;
            for (var i = 0; i < _carrots.Count; i++) _carrots[i].style.opacity = i < _game.Carrots ? 1f : 0.22f;
            var run = _game.Combo % _game.Tune.ComboBonusAt;
            for (var i = 0; i < _pips.Count; i++)
                _pips[i].style.backgroundColor = i < run ? new Color(0.42f, 0.66f, 0.34f) : _game.Combo > 0 && run == 0 ? new Color(1f, 0.78f, 0.25f) : new Color(0.55f, 0.48f, 0.4f, 0.55f);
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.carrotGuard.stolen", ("n", _game.StolenCount), ("max", _game.Tune.EndlessStolen))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            _carrotLabel.text = t.T("mg.carrotGuard.carrots", ("n", _game.Carrots), ("max", _game.Tune.Carrots));
            if (_stage != null)
            {
                foreach (var m in _moles) m.style.display = DisplayStyle.None;
                _king.style.display = DisplayStyle.None;
                RenderStage();
            }
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _mounds.Clear(); _moles.Clear(); _carrots.Clear(); _pips.Clear();
            _carrotLabel?.RemoveFromHierarchy();
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
