using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Memory host on the shared minigame frame (port of memory_match.gd + its stage): the cards lie on a picnic
    /// blanket in the meadow, face up for the memorise window (a countdown bar runs down), then two flips at a
    /// time — a pair stays with a mint rim, a miss turns back after the flip beat with "Kein Paar!". Three clean
    /// pairs in a row earn the one peek of the round (a button under the blanket). 4×4 up to level 5, 4×6 from
    /// level 6; endless deals board after board until twelve misses.
    /// </summary>
    public sealed partial class MemoryMatchScreen : MinigameHostScreen
    {
        private MemoryMatchGame _game;
        private readonly List<VisualElement> _cards = new List<VisualElement>();
        private readonly List<VisualElement> _faces = new List<VisualElement>();
        private readonly List<string> _shown = new List<string>();
        private VisualElement _blanket, _countdown, _countdownFill;
        private Button _peek;
        private int _dealt = -1;

        private const float CardW = 68f, CardH = 82f, GapX = 8f, GapY = 10f;
        private static readonly Color Meadow = new Color(0.62f, 0.83f, 0.6f);
        private static readonly Color Back = new Color(1f, 0.82f, 0.4f);
        private static readonly Color Face = new Color(1f, 0.97f, 0.93f);
        private static readonly Color MatchedRim = new Color(0.6f, 0.9f, 0.88f);

        public MemoryMatchScreen(GameHost host) : base(host, "memoryMatch", "memoryMatch") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        /// <summary>The live score only falls (misses, the shrinking time bonus) — pairs speak through the pairs line instead.</summary>
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;
        protected override Color BackdropColor => new Color(0.45f, 0.7f, 0.45f);
        protected override Color FieldColor => Meadow;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public MemoryMatchGame Game => _game;

        protected override void StartGame(int seed)
        {
            var level = J.Int(J.Path(Host.Store.State, "progression", "level"), 1);
            _game = new MemoryMatchGame(Difficulty, seed, level);
            _game.Resolved += OnResolved;
            _game.PeekEarned += () => Banner(Host.Strings.T("mg.memoryMatch.peek_ready"), 1.6);
            _game.BoardCleared += () => { Banner(Host.Strings.T("mg.memoryMatch.cleared"), 2.2); StageCleared(); };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the blanket: a rose cloth with a lighter hem, sized to the biggest board so a new endless deal fits
            _blanket = new VisualElement { pickingMode = PickingMode.Ignore, name = "blanket" };
            _blanket.style.position = Position.Absolute;
            _blanket.style.backgroundColor = new Color(0.97f, 0.85f, 0.85f);
            var hem = new Color(0.91f, 0.71f, 0.72f);
            _blanket.style.borderTopColor = hem; _blanket.style.borderBottomColor = hem; _blanket.style.borderLeftColor = hem; _blanket.style.borderRightColor = hem;
            _blanket.style.borderTopWidth = 6; _blanket.style.borderBottomWidth = 6; _blanket.style.borderLeftWidth = 6; _blanket.style.borderRightWidth = 6;
            _blanket.style.borderTopLeftRadius = 22; _blanket.style.borderTopRightRadius = 22; _blanket.style.borderBottomLeftRadius = 22; _blanket.style.borderBottomRightRadius = 22;
            Field.Add(_blanket);
            Gingham(_blanket);
            if (_stage != null) _blanket.style.display = DisplayStyle.None;
            // the memorise/peek countdown under the pills
            _countdown = new VisualElement { pickingMode = PickingMode.Ignore, name = "countdown" };
            Ux.Abs(_countdown, null, 66);
            _countdown.style.left = Length.Percent(50);
            _countdown.style.translate = new Translate(Length.Percent(-50), 0);
            _countdown.style.width = 220; _countdown.style.height = 10;
            _countdown.style.backgroundColor = new Color(1f, 1f, 1f, 0.55f);
            _countdown.style.borderTopLeftRadius = 5; _countdown.style.borderTopRightRadius = 5; _countdown.style.borderBottomLeftRadius = 5; _countdown.style.borderBottomRightRadius = 5;
            _countdownFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _countdownFill.style.height = Length.Percent(100);
            _countdownFill.style.width = Length.Percent(100);
            _countdownFill.style.backgroundColor = new Color(1f, 0.55f, 0.55f);
            _countdownFill.style.borderTopLeftRadius = 5; _countdownFill.style.borderTopRightRadius = 5; _countdownFill.style.borderBottomLeftRadius = 5; _countdownFill.style.borderBottomRightRadius = 5;
            _countdown.Add(_countdownFill);
            Root.Add(_countdown);
            // the earned peek
            _peek = Ux.Btn(t.T("mg.memoryMatch.peek_button"), () => { if (_game != null && _game.UsePeek()) Banner(t.T("mg.memoryMatch.peek"), 1.0); }, "gb-btn-secondary", "gb-btn-sm");
            _peek.name = "peek";
            Ux.Abs(_peek, null, null, null, 28);
            _peek.style.left = Length.Percent(50);
            _peek.style.translate = new Translate(Length.Percent(-50), 0);
            _peek.style.display = DisplayStyle.None;
            Root.Add(_peek);
            ComboLabel.style.top = 84;
            Banner(t.T("mg.memoryMatch.intro"), MemoryMatchGame.IntroSec + 0.7);
        }

        // ── scenery (board 06 picnic on the meadow): a strip of sky with clouds, hills and trees, tufts and flowers above
        //    and below the blanket, a picnic basket with an apple; gingham checks woven into the blanket. The blanket
        //    covers most of the field on the big board — everything here sits behind it and the cards. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-memory");
            MgScenery.Gradient(scene, MgScenery.Hex(0xA9D3FF), MgScenery.Hex(0xE6F3FF), 0f, 13f);
            var c1 = MgScenery.Cloud(scene, 76f);
            MgScenery.Pct(c1, 8f, 2f);
            var c2 = MgScenery.Cloud(scene, 58f, MgScenery.Hex(0xF4F8FF));
            MgScenery.Pct(c2, 64f, 4f);
            var farC = MgScenery.Hex(0xBFE6CC); var farRim = MgScenery.Hex(0xD9F3E1);
            for (var i = 0; i < 3; i++)
            {
                var hill = MgScenery.Hill(scene, 180f, 38f + (i % 2) * 12f, farC, farRim);
                MgScenery.Pct(hill, -12f + i * 40f, null, null, 86.5f);
            }
            var treeL = MgScenery.Sprite(scene, "plant_tree_round", 42f, 56f);
            MgScenery.Pct(treeL, 2f, null, null, 86f);
            var treeR = MgScenery.Sprite(scene, "plant_tree_round", 36f, 48f);
            MgScenery.Pct(treeR, null, null, 4f, 86f);
            var edge = MgScenery.Shape(scene, MgScenery.Hex(0x8FCC89));
            MgScenery.Pct(edge, 0, 13f, 0);
            edge.style.height = 4f;
            var tuftC = MgScenery.Hex(0x74BF76);
            var petals = new[] { MgScenery.Hex(0xFFFFFF), MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0xFFD166), MgScenery.Hex(0xB7D8FF) };
            for (var i = 0; i < 4; i++)
            {
                var tuft = MgScenery.Tuft(scene, tuftC, 0.85f);
                MgScenery.Pct(tuft, 6f + i * 25f, 17f + (i % 2) * 2f);
                var f = MgScenery.Flower(scene, petals[i], 0.8f);
                MgScenery.Pct(f, 17f + i * 24f, 15.5f + ((i + 1) % 2) * 2.5f);
                var low = MgScenery.Tuft(scene, tuftC, 0.9f);
                MgScenery.Pct(low, 36f + i * 17f, 81f + (i % 2) * 7f);
            }
            for (var i = 0; i < 3; i++)
            {
                var f = MgScenery.Flower(scene, petals[(i + 2) % petals.Length], 0.9f);
                MgScenery.Pct(f, 40f + i * 18f, 90f - (i % 2) * 6f);
            }
            var daisies = MgScenery.Sprite(scene, "plant_daisies", 40f, 42f);
            MgScenery.Pct(daisies, null, null, 3f, 7f);
            var tulips = MgScenery.Sprite(scene, "plant_tulips_red", 34f, 40f);
            MgScenery.Pct(tulips, null, null, 16f, 2f);
            // the picnic basket with a pink cloth and an apple beside it
            var basket = MgScenery.Group(scene, "gb-mgf-basket");
            MgScenery.Pct(basket, 5f, null, null, 5f);
            MgScenery.Size(basket, 72f, 72f);
            var handle = MgScenery.Shape(basket, Color.clear, 26f, "gb-mgf-handle");
            handle.style.left = 12f; handle.style.top = 0; MgScenery.Size(handle, 48f, 46f);
            handle.style.borderTopWidth = 6; handle.style.borderLeftWidth = 6; handle.style.borderRightWidth = 6;
            var wicker = MgScenery.Hex(0xB47A45);
            handle.style.borderTopColor = wicker; handle.style.borderLeftColor = wicker; handle.style.borderRightColor = wicker;
            var cloth = MgScenery.Shape(basket, MgScenery.Hex(0xFF9BC1), 8f);
            cloth.style.left = 10f; cloth.style.right = 10f; cloth.style.top = 24f; cloth.style.height = 16f;
            MgScenery.Outline(cloth, 2f, Color.white);
            var body = MgScenery.Shape(basket, MgScenery.Hex(0xE0AD72), 0f, "gb-mgf-wicker");
            body.style.left = 0; body.style.right = 0; body.style.bottom = 0; body.style.height = 40f;
            MgScenery.RoundBottom(body, 16f); MgScenery.RoundTop(body, 6f);
            MgScenery.Outline(body, 3f, wicker);
            for (var i = 0; i < 2; i++)
            {
                var weave = MgScenery.Shape(body, MgScenery.Hex(0xC98E52), 2f);
                weave.style.left = 6f; weave.style.right = 6f; weave.style.top = 10f + i * 12f; weave.style.height = 3f;
            }
            var apple = Ux.Icon("food_apple");
            apple.pickingMode = PickingMode.Ignore;
            apple.style.position = Position.Absolute;
            MgScenery.Pct(apple, 27f, null, null, 5f);
            MgScenery.Size(apple, 30f, 30f);
            scene.Add(apple);
        }

        /// <summary>Gingham checks on the blanket: pale bands both ways (their crossings read as the lighter squares).</summary>
        private static void Gingham(VisualElement blanket)
        {
            var band = new Color(1f, 1f, 1f, 0.32f);
            for (var i = 0; i < 5; i++)
            {
                var v = MgScenery.Shape(blanket, band, 0f, "gb-mgf-gingham");
                MgScenery.Pct(v, 6f + i * 20f, 0, null, 0);
                v.style.width = Length.Percent(8f);
                var h = MgScenery.Shape(blanket, band, 0f, "gb-mgf-gingham");
                MgScenery.Pct(h, 0, 6f + i * 20f, 0);
                h.style.height = Length.Percent(8f);
            }
        }

        /// <summary>Lays out the card elements of the current board (once per deal — endless deals anew).</summary>
        private void Deal()
        {
            foreach (var c in _cards) c.RemoveFromHierarchy();
            _cards.Clear(); _faces.Clear(); _shown.Clear();
            var cols = _game.Layout.Cols; var rows = _game.Layout.Rows;
            var gridW = cols * CardW + (cols - 1) * GapX;
            var gridH = rows * CardH + (rows - 1) * GapY;
            var x0 = (FieldW - gridW) * 0.5f; var y0 = (FieldH - gridH) * 0.5f;
            _blanket.style.left = x0 - 22; _blanket.style.top = y0 - 22;
            _blanket.style.width = gridW + 44; _blanket.style.height = gridH + 44;
            for (var i = 0; i < _game.Cards.Count; i++)
            {
                var card = new VisualElement { pickingMode = PickingMode.Position, name = "card-" + i };
                card.style.position = Position.Absolute;
                card.style.left = x0 + (i % cols) * (CardW + GapX);
                card.style.top = y0 + (i / cols) * (CardH + GapY);
                card.style.width = CardW; card.style.height = CardH;
                card.style.borderTopLeftRadius = 12; card.style.borderTopRightRadius = 12; card.style.borderBottomLeftRadius = 12; card.style.borderBottomRightRadius = 12;
                card.style.borderTopWidth = 3; card.style.borderBottomWidth = 3; card.style.borderLeftWidth = 3; card.style.borderRightWidth = 3;
                card.style.justifyContent = Justify.Center; card.style.alignItems = Align.Center;
                var face = Ux.Icon("food_" + FaceKey(_game.Cards[i].Key));
                face.style.width = 46; face.style.height = 46;
                face.pickingMode = PickingMode.Ignore;
                card.Add(face);
                var index = i;
                card.RegisterCallback<PointerDownEvent>(_ => { if (_game != null) _game.Flip(index); });
                // with the 3D picnic the element is only the tap target over its tile
                if (_stage != null) card.style.opacity = 0f;
                Field.Add(card);
                _cards.Add(card); _faces.Add(face); _shown.Add("");
            }
            LayoutStage();
        }

        private static string FaceKey(string key) => key == "donut-sprinkles" ? "donut" : key == "ice-cream" ? "icecream" : key;

        protected override void TickGame(double dt) => _game.Tick(dt);

        protected override void RenderGame()
        {
            if (_dealt != _game.BoardsCleared || _cards.Count != _game.Cards.Count) { _dealt = _game.BoardsCleared; Deal(); }
            var revealing = _game.Revealing;
            for (var i = 0; i < _cards.Count; i++)
            {
                var state = _game.Cards[i].State;
                var look = revealing && state == MemoryMatchGame.Down ? "peek" : state;
                if (_shown[i] == look) continue;
                _shown[i] = look;
                var card = _cards[i];
                var faceUp = look != MemoryMatchGame.Down;
                card.style.backgroundColor = faceUp ? Face : Back;
                var rim = look == MemoryMatchGame.Matched ? MatchedRim : look == MemoryMatchGame.Up ? new Color(1f, 0.55f, 0.55f) : new Color(1f, 1f, 1f, 0.9f);
                card.style.borderTopColor = rim; card.style.borderBottomColor = rim; card.style.borderLeftColor = rim; card.style.borderRightColor = rim;
                card.style.opacity = _stage != null ? 0f : look == MemoryMatchGame.Matched ? 0.72f : 1f;
                _faces[i].style.display = faceUp ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_stage != null) RenderStage();
            var frac = (float)_game.CountdownFrac;
            _countdown.style.display = frac > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _countdownFill.style.width = Length.Percent(frac * 100f);
            _peek.style.display = _game.CanUsePeek ? DisplayStyle.Flex : DisplayStyle.None;
            TimeLabel.text = _game.Tune.Endless
                ? Host.Strings.T("mg.memoryMatch.misses", ("n", _game.Misses), ("max", _game.Tune.EndlessMissFlips))
                : Host.Strings.T("mg.game.time", ("sec", (int)_game.Elapsed));
            ScoreLabel.text = _game.LiveScore.ToString();
            ComboLabel.text = Host.Strings.T("mg.memoryMatch.pairs", ("n", _game.MatchedPairs), ("max", _game.Layout.Pairs));
        }

        private void OnResolved(bool hit, int second)
        {
            if (second < 0 || second >= _cards.Count) return;
            StageResolved(hit, second);
            var card = _cards[second];
            var at = new Vector2(card.resolvedStyle.left + CardW * 0.5f, card.resolvedStyle.top);
            FloatText(at, hit ? Host.Strings.T("mg.hudkit.beat_paar") : Host.Strings.T("mg.memoryMatch.oops"), hit ? new Color(0.24f, 0.55f, 0.32f) : new Color(0.85f, 0.3f, 0.3f));
            if (hit && _game.MatchStreak >= 2) Banner(Host.Strings.T("mg.game.streak", ("n", _game.MatchStreak)), 0.9);
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _cards.Clear(); _faces.Clear(); _shown.Clear();
            _dealt = -1;
            _countdown?.RemoveFromHierarchy();
            _peek?.RemoveFromHierarchy();
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
