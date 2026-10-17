using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Burger-Bau host on the shared minigame frame (port of burger_build.gd + its stage): a diner kitchen — tiled
    /// wall, the order ticket pinned at the top, ingredients raining down three columns onto a plate that follows
    /// the finger along the counter. The stack grows layer by layer in the ticket's order; the finished burger is
    /// bitten (a beat), then the next ticket. Gold orders (2 and 4) pay ×1.5 in 80 % of the time.
    /// </summary>
    public sealed partial class BurgerBuildScreen : MinigameHostScreen
    {
        private BurgerBuildGame _game;
        private readonly Dictionary<BurgerBuildGame.Item, VisualElement> _items = new Dictionary<BurgerBuildGame.Item, VisualElement>();
        private readonly List<VisualElement> _stack = new List<VisualElement>();
        private readonly List<VisualElement> _ticketRows = new List<VisualElement>();
        private VisualElement _plate, _ticket, _counter, _worktop;
        private float _counterTop = -1f;
        private Label _orderLabel, _ticketTitle;
        private int _shownOrder = -1, _shownPlaced = -1;
        private float _pulse;

        // burger_build.gd LAYER_COLORS
        private static readonly Dictionary<string, Color> LayerColor = new Dictionary<string, Color>
        {
            ["bun"] = new Color(0.91f, 0.68f, 0.36f), ["patty"] = new Color(0.55f, 0.33f, 0.2f), ["cheese"] = new Color(1f, 0.79f, 0.28f),
            ["tomato"] = new Color(0.9f, 0.31f, 0.28f), ["salad"] = new Color(0.5f, 0.79f, 0.38f), ["onion"] = new Color(0.93f, 0.85f, 0.93f),
        };

        public BurgerBuildScreen(GameHost host) : base(host, "burgerBuild", "burgerBuild") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xE7B79C);
        protected override Color FieldColor => MgScenery.Hex(0xFBEFE2);

        /// <summary>The running round (flow tests drive it directly).</summary>
        public BurgerBuildGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new BurgerBuildGame(Difficulty, seed);
            _game.Caught += OnCaught;
            _game.OrderCompleted += p => { Banner(Host.Strings.T("mg.burgerBuild.done", ("n", Fmt(p))), 1.2); StageOrder(true); };
            _game.OrderExpired += () => { Banner(Host.Strings.T("mg.burgerBuild.expired"), 1.2); StageOrder(false); };
            _game.Finished_ += RoundOver;
        }

        private static string Fmt(double v) => System.Math.Abs(v - System.Math.Round(v)) < 1e-6 ? ((int)System.Math.Round(v)).ToString() : v.ToString("0.#");

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(e => Slide(e.localPosition.x));
            Field.RegisterCallback<PointerMoveEvent>(e => { if (e.pressedButtons != 0) Slide(e.localPosition.x); });
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the plate and its stack
            _plate = new VisualElement { pickingMode = PickingMode.Ignore, name = "plate" };
            _plate.style.position = Position.Absolute;
            _plate.style.height = 16;
            _plate.style.backgroundColor = new Color(0.98f, 0.98f, 1f);
            _plate.style.borderTopLeftRadius = 8; _plate.style.borderTopRightRadius = 8; _plate.style.borderBottomLeftRadius = 8; _plate.style.borderBottomRightRadius = 8;
            _plate.style.borderBottomWidth = 4; _plate.style.borderBottomColor = new Color(0.8f, 0.82f, 0.9f);
            Field.Add(_plate);
            if (_stage != null) _plate.style.visibility = Visibility.Hidden;
            // the ticket pinned to the wall
            _ticket = Ux.El("gb-card");
            _ticket.name = "ticket";
            Ux.Abs(_ticket, null, 60);
            _ticket.style.right = 10;
            _ticket.style.width = 150;
            _ticket.style.paddingTop = 8; _ticket.style.paddingBottom = 8; _ticket.style.paddingLeft = 10; _ticket.style.paddingRight = 10;
            _ticket.pickingMode = PickingMode.Ignore;
            _ticketTitle = Ux.Text("", "gb-sm", "gb-bold");
            _ticket.Add(_ticketTitle);
            Root.Add(_ticket);
            _orderLabel = Ux.Text("", "gb-md", "gb-bold");
            _orderLabel.name = "order";
            Ux.Abs(_orderLabel, null, 84);
            _orderLabel.style.left = Length.Percent(50);
            _orderLabel.style.translate = new Translate(Length.Percent(-50), 0);
            Root.Add(_orderLabel);
            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.burgerBuild.intro"), BurgerBuildGame.IntroSec + 0.7);
        }

        // ── scenery (board 06 diner): striped upper wall, a chalk menu board with a burger, two pendant lamps, a window,
        //    a pink checker wainscot under a wooden rail, and the counter (panelled front, worktop) whose top follows the
        //    plate's line. Static, pointer-transparent, behind the plate, the falling layers and the stack. ──

        private void BuildScenery()
        {
            _counterTop = -1f;
            var scene = MgScenery.Layer(Field, "mgf-burger");
            for (var i = 0; i < 6; i++)
            {
                var stripe = MgScenery.Shape(scene, MgScenery.Hex(0xF8E3D3));
                MgScenery.Pct(stripe, i * 16.67f + 4f, 0, null, 0);
                stripe.style.width = Length.Percent(8.3f);
            }
            var wainscot = MgScenery.Tiles(scene, 5, 4, MgScenery.Hex(0xFFD9E2), MgScenery.Hex(0xFFF7EE), MgScenery.Hex(0xEAD7C6), MgScenery.Hex(0xF2B9C8));
            MgScenery.Pct(wainscot, 0, 52f, 0, 0);
            var rail = MgScenery.Shape(scene, MgScenery.Hex(0xE8C9A0), 5f, "gb-mgf-rail");
            MgScenery.Pct(rail, 0, 52f, 0);
            rail.style.left = -6; rail.style.right = -6; rail.style.height = 12; rail.style.translate = new Translate(0, -6);
            MgScenery.Outline(rail, 2f, MgScenery.Hex(0xC8A27A));
            // the chalk menu board
            var board = MgScenery.Shape(scene, MgScenery.Hex(0x5E7F6A), 12f, "gb-mgf-menu");
            MgScenery.Pct(board, 7f, 19f);
            MgScenery.Size(board, 118f, 92f);
            MgScenery.Outline(board, 6f, MgScenery.Hex(0xD4A87C));
            var burger = Ux.Icon("food_burger");
            burger.pickingMode = PickingMode.Ignore;
            burger.style.position = Position.Absolute; burger.style.left = 8f; burger.style.top = 10f;
            MgScenery.Size(burger, 32f, 32f);
            board.Add(burger);
            for (var i = 0; i < 4; i++)
            {
                var line = MgScenery.Shape(board, new Color(1f, 0.98f, 0.92f, 0.8f), 2f);
                line.style.left = i < 2 ? 48f : 10f; line.style.top = 14f + i * 14f; line.style.height = 4f;
                line.style.width = i < 2 ? 44f - i * 10f : 76f - i * 12f;
            }
            // two pendant lamps (clear of the order line in the middle)
            foreach (var x in new[] { 16f, 84f })
            {
                var cord = MgScenery.Shape(scene, MgScenery.Hex(0xA8A29E), 1f);
                MgScenery.Pct(cord, x, 0);
                cord.style.width = 2f; cord.style.height = Length.Percent(6f);
                var shade = MgScenery.Shape(scene, MgScenery.Hex(0xFFD166), 0f, "gb-mgf-lamp");
                MgScenery.Pct(shade, x, 6f);
                MgScenery.Size(shade, 46f, 24f);
                MgScenery.RoundTop(shade, 23f);
                shade.style.translate = new Translate(-22f, 0);
                MgScenery.Outline(shade, 3f, MgScenery.Hex(0xE9A23B));
                var glow = MgScenery.Shape(scene, new Color(1f, 0.93f, 0.6f, 0.35f), 16f);
                MgScenery.Pct(glow, x, 6f);
                MgScenery.Size(glow, 32f, 32f);
                glow.style.translate = new Translate(-15f, 14f);
            }
            var window = MgScenery.Window(scene, 104f, 96f);
            MgScenery.Pct(window, null, 22f, 10f);
            // the counter: panelled body + worktop; RenderGame seats its top on the plate's line
            _counter = MgScenery.Shape(scene, MgScenery.Hex(0xC99A6E), 0f, "gb-mgf-counter");
            _counter.name = "mgf-counter";
            MgScenery.Pct(_counter, 0, 76f, 0, 0);
            for (var i = 0; i < 4; i++)
            {
                var panel = MgScenery.Shape(_counter, MgScenery.Hex(0xD4A87C), 10f, "gb-mgf-door");
                MgScenery.Pct(panel, 4f + i * 24f, 20f, null, 10f);
                panel.style.width = Length.Percent(20f);
                MgScenery.Outline(panel, 3f, MgScenery.Hex(0xB5845A));
            }
            _worktop = MgScenery.Shape(scene, MgScenery.Hex(0xE8C9A0), 7f, "gb-mgf-worktop");
            MgScenery.Pct(_worktop, 0, 76f, 0);
            _worktop.style.left = -8; _worktop.style.right = -8; _worktop.style.height = 14; _worktop.style.translate = new Translate(0, -6);
            MgScenery.Outline(_worktop, 2f, MgScenery.Hex(0xC8A27A));
        }

        /// <summary>The counter's top follows the plate's bottom (the plate line is world-fixed, the field height is not).</summary>
        private void SeatCounter(float top)
        {
            if (Mathf.Abs(top - _counterTop) < 0.5f) return;
            _counterTop = top;
            _counter.style.top = top; _worktop.style.top = top;
        }

        private void Slide(float localX)
        {
            if (_game == null || FieldW <= 0f) return;
            _game.SetPlateFromNormalizedX(localX / FieldW);
        }

        private float Ppu => Mathf.Min(FieldW / (float)(BurgerBuildGame.HalfW * 2.0), FieldH / (float)(BurgerBuildGame.HalfH * 2.0));
        private Vector2 Project(double wx, double wy) => new Vector2(FieldW * 0.5f + (float)wx * Ppu, FieldH * 0.5f - (float)wy * Ppu);

        protected override void TickGame(double dt) { _game.Tick(dt); _pulse += (float)dt; }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var ppu = Ppu;
            // the plate
            var plateW = (float)_game.Tune.PlateHalfWidth * 2f * ppu;
            var plateAt = Project(_game.PlateX, BurgerBuildGame.PlateY);
            _plate.style.left = plateAt.x - plateW * 0.5f; _plate.style.top = plateAt.y - 8; _plate.style.width = plateW;
            if (_counter != null) SeatCounter(plateAt.y + 8f);
            // the stack: one slab per placed layer, in the ticket's colours
            if (_shownOrder != _game.OrderNumber || _shownPlaced != _game.Placed) RebuildStack();
            var layerH = (float)BurgerBuildGame.LayerHeight * ppu;
            for (var i = 0; i < _stack.Count; i++)
            {
                var slab = _stack[i];
                var w = (i == 0 || i == _game.Ticket.Count - 1 ? 0.9f : 0.8f) * plateW;
                slab.style.left = plateAt.x - w * 0.5f; slab.style.width = w;
                slab.style.top = plateAt.y - 8 - (float)(BurgerBuildGame.StackBase + i * BurgerBuildGame.LayerHeight) * ppu - layerH * 0.5f;
                slab.style.height = layerH * 1.15f;
            }
            // the rain of ingredients
            var live = new HashSet<BurgerBuildGame.Item>(_game.Items);
            foreach (var it in _game.Items)
            {
                if (!_items.TryGetValue(it, out var v))
                {
                    v = Slab(it.Id, 1.0f * ppu, 0.34f * ppu, true);
                    if (_stage != null) v.style.visibility = Visibility.Hidden;
                    _items[it] = v;
                    Field.Add(v);
                }
                var at = Project(it.X, it.Y);
                v.style.left = at.x - 1.0f * ppu * 0.5f; v.style.top = at.y - 0.34f * ppu * 0.5f;
            }
            var dead = new List<BurgerBuildGame.Item>();
            foreach (var kv in _items) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _items[d].RemoveFromHierarchy(); _items.Remove(d); }
            // the ticket and the order line
            var urgent = !_game.Biting && _game.OrderLeft < 5.0;
            _orderLabel.text = _game.Biting ? t.T("mg.burgerBuild.done", ("n", Fmt(_game.Tune.CompletePts * (_game.Rush ? 1.5 : 1.0))))
                : t.T(_game.Rush ? "mg.burgerBuild.order_rush" : "mg.burgerBuild.order", ("n", _game.OrderNumber), ("sec", Mathf.CeilToInt((float)_game.OrderLeft)));
            _orderLabel.style.color = urgent ? Color.Lerp(new Color(0.3f, 0.25f, 0.3f), new Color(0.9f, 0.25f, 0.25f), 0.5f + 0.5f * Mathf.Sin(_pulse * 8f)) : _game.Rush ? new Color(0.85f, 0.6f, 0.1f) : new Color(0.3f, 0.25f, 0.3f);
            _ticket.style.backgroundColor = _game.Rush ? new Color(1f, 0.93f, 0.72f) : Color.white;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.burgerBuild.expired_count", ("n", _game.Expired), ("max", _game.Tune.EndlessExpires))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.LiveScore.ToString();
            if (_stage != null) RenderStage();
        }

        /// <summary>Ticket rows and the slabs on the plate for the current order and its placed layers.</summary>
        private void RebuildStack()
        {
            var t = Host.Strings;
            if (_shownOrder != _game.OrderNumber)
            {
                foreach (var r in _ticketRows) r.RemoveFromHierarchy();
                _ticketRows.Clear();
                _ticketTitle.text = t.T(_game.Rush ? "mg.burgerBuild.order_rush" : "mg.burgerBuild.order", ("n", _game.OrderNumber), ("sec", Mathf.CeilToInt((float)_game.OrderLeft))).Split('·')[0].Trim();
                // the ticket reads top-down like the burger: the last layer on top
                for (var i = _game.Ticket.Count - 1; i >= 0; i--)
                {
                    var row = Ux.Row();
                    row.style.alignItems = Align.Center;
                    row.style.marginTop = 3;
                    var swatch = Slab(_game.Ticket[i], 34f, 12f, false);
                    swatch.style.position = Position.Relative;
                    swatch.style.marginRight = 8;
                    row.Add(swatch);
                    var label = Ux.Text(t.T("mg.burgerBuild.layer." + _game.Ticket[i]), "gb-sm");
                    label.name = "ticket-" + i;
                    row.Add(label);
                    row.userData = i;
                    _ticket.Add(row);
                    _ticketRows.Add(row);
                }
                foreach (var s in _stack) s.RemoveFromHierarchy();
                _stack.Clear();
            }
            while (_stack.Count < _game.Placed)
            {
                var slab = Slab(_game.Ticket[_stack.Count], 40f, 12f, false);
                slab.name = "layer-" + _stack.Count;
                if (_stage != null) slab.style.visibility = Visibility.Hidden;
                Field.Add(slab);
                _stack.Add(slab);
            }
            foreach (var row in _ticketRows)
            {
                var i = (int)row.userData;
                row.style.opacity = i < _game.Placed ? 0.4f : 1f;
                row.style.unityFontStyleAndWeight = i == _game.Placed ? FontStyle.Bold : FontStyle.Normal;
            }
            _shownOrder = _game.OrderNumber; _shownPlaced = _game.Placed;
        }

        /// <summary>A layer as a rounded slab in its colour (buns domed, salad frilly, onion a ring).</summary>
        private static VisualElement Slab(string id, float w, float h, bool falling)
        {
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.width = w; v.style.height = h;
            var c = LayerColor.TryGetValue(id, out var col) ? col : Color.gray;
            v.style.backgroundColor = c;
            var r = id == "bun" ? h : id == "salad" ? h * 0.3f : h * 0.5f;
            v.style.borderTopLeftRadius = r; v.style.borderTopRightRadius = r; v.style.borderBottomLeftRadius = id == "bun" ? h * 0.3f : r; v.style.borderBottomRightRadius = id == "bun" ? h * 0.3f : r;
            var rim = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f);
            v.style.borderTopWidth = 2; v.style.borderBottomWidth = 2; v.style.borderLeftWidth = 2; v.style.borderRightWidth = 2;
            v.style.borderTopColor = rim; v.style.borderBottomColor = rim; v.style.borderLeftColor = rim; v.style.borderRightColor = rim;
            if (id == "onion")
            {
                var hole = new VisualElement { pickingMode = PickingMode.Ignore };
                hole.style.position = Position.Absolute;
                hole.style.left = w * 0.4f; hole.style.top = h * 0.3f; hole.style.width = w * 0.2f; hole.style.height = h * 0.4f;
                hole.style.borderTopLeftRadius = h; hole.style.borderTopRightRadius = h; hole.style.borderBottomLeftRadius = h; hole.style.borderBottomRightRadius = h;
                hole.style.backgroundColor = new Color(0.75f, 0.6f, 0.78f);
                v.Add(hole);
            }
            if (id == "bun" && falling)
            {
                foreach (var x in new[] { 0.3f, 0.5f, 0.7f })
                {
                    var seed = new VisualElement { pickingMode = PickingMode.Ignore };
                    seed.style.position = Position.Absolute;
                    seed.style.left = w * x - 2; seed.style.top = h * 0.25f; seed.style.width = 4; seed.style.height = 3;
                    seed.style.borderTopLeftRadius = 2; seed.style.borderTopRightRadius = 2; seed.style.borderBottomLeftRadius = 2; seed.style.borderBottomRightRadius = 2;
                    seed.style.backgroundColor = new Color(1f, 0.95f, 0.8f);
                    v.Add(seed);
                }
            }
            return v;
        }

        private void OnCaught(BurgerBuildGame.Item item, bool correct, double delta)
        {
            StageCaught(correct);
            var at = Project(_game.PlateX, BurgerBuildGame.PlateY + _game.StackTop);
            FloatText(at, correct ? "+" + Fmt(delta) : Host.Strings.T("mg.burgerBuild.wrong"), correct ? new Color(0.2f, 0.6f, 0.34f) : new Color(0.82f, 0.32f, 0.3f));
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _items.Clear(); _stack.Clear(); _ticketRows.Clear();
            _shownOrder = -1; _shownPlaced = -1;
            _ticket?.RemoveFromHierarchy();
            _orderLabel?.RemoveFromHierarchy();
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
