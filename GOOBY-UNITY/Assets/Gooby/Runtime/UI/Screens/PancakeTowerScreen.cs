using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Pfannkuchenturm host on the shared minigame frame (port of pancake_tower.gd + its stage): a breakfast
    /// kitchen — the plate on the counter, the stack of golden pancakes growing on it, the next one swinging above
    /// on the spatula; a tap anywhere drops it. Overhang crumbs tumble off, toppings (every fifth) are strawberry
    /// red, perfect layers gleam, and the camera slides up with the tower. The tower sways from layer eight.
    /// </summary>
    public sealed partial class PancakeTowerScreen : MinigameHostScreen
    {
        private PancakeTowerGame _game;
        private readonly List<VisualElement> _layers = new List<VisualElement>();
        private readonly Dictionary<PancakeTowerGame.Crumb, VisualElement> _crumbs = new Dictionary<PancakeTowerGame.Crumb, VisualElement>();
        private VisualElement _plate, _swing, _spatula, _tower;
        private Label _layersLabel, _widthLabel;
        private float _pulse, _camBottom;

        private const double ViewUnitsX = 2.9;   // world units across the view (pancake_tower.gd VIEW_UNITS_X)
        private const float GroundFrac = 0.82f;
        private static readonly Color Pancake = new Color(0.93f, 0.68f, 0.32f);
        private static readonly Color PancakeRim = new Color(0.72f, 0.46f, 0.18f);
        private static readonly Color Topping = new Color(0.93f, 0.33f, 0.36f);

        public PancakeTowerScreen(GameHost host) : base(host, "pancakeTower", "pancakeTower") { }

        protected override (double w, double h) ViewSize => (390.0, 844.0);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xE7BFA0);
        protected override Color FieldColor => MgScenery.Hex(0xFCF0DE);

        /// <summary>The running round (flow tests drive it directly).</summary>
        public PancakeTowerGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new PancakeTowerGame(Difficulty, seed);
            _game.Landed += OnLanded;
            _game.Toppled_ += x => { Banner(Host.Strings.T("mg.pancakeTower.topple"), 1.4); StageToppled(); };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(_ => _game?.Drop());
            if (World.MgStage3D.Supported) BuildStage();
            else BuildScenery();
            // the tower group: plate and layers move together when the camera rides up
            _tower = new VisualElement { pickingMode = PickingMode.Ignore };
            _tower.style.position = Position.Absolute; _tower.style.left = 0; _tower.style.top = 0; _tower.style.right = 0; _tower.style.bottom = 0;
            Field.Add(_tower);
            _plate = new VisualElement { pickingMode = PickingMode.Ignore, name = "plate" };
            _plate.style.position = Position.Absolute;
            _plate.style.height = 14;
            _plate.style.backgroundColor = new Color(0.98f, 0.98f, 1f);
            _plate.style.borderTopLeftRadius = 7; _plate.style.borderTopRightRadius = 7; _plate.style.borderBottomLeftRadius = 7; _plate.style.borderBottomRightRadius = 7;
            _plate.style.borderBottomWidth = 4; _plate.style.borderBottomColor = new Color(0.8f, 0.82f, 0.9f);
            _tower.Add(_plate);
            _spatula = new VisualElement { pickingMode = PickingMode.Ignore };
            _spatula.style.position = Position.Absolute;
            _spatula.style.width = 8; _spatula.style.backgroundColor = new Color(0.55f, 0.55f, 0.6f);
            Field.Add(_spatula);
            _swing = PancakeShape(false);
            _swing.name = "swing";
            Field.Add(_swing);
            if (_stage != null) { _tower.style.display = DisplayStyle.None; _swing.style.visibility = Visibility.Hidden; _spatula.style.visibility = Visibility.Hidden; }
            _layersLabel = Ux.Text("", "gb-md", "gb-bold");
            _layersLabel.name = "layers";
            Ux.Abs(_layersLabel, null, 58);
            _layersLabel.style.left = Length.Percent(50);
            _layersLabel.style.translate = new Translate(Length.Percent(-50), 0);
            Root.Add(_layersLabel);
            _widthLabel = Ux.Text("", "gb-sm", "gb-mild");
            _widthLabel.name = "width";
            Ux.Abs(_widthLabel, null, 84);
            _widthLabel.style.left = Length.Percent(50);
            _widthLabel.style.translate = new Translate(Length.Percent(-50), 0);
            Root.Add(_widthLabel);
            ComboLabel.style.display = DisplayStyle.None;
            Banner(t.T("mg.pancakeTower.intro"), PancakeTowerGame.IntroSec + 0.7);
        }

        // ── scenery (board 06 breakfast kitchen): a static striped wallpaper under a scalloped awning, and the room that
        //    rides down with the camera — mint pillow-tile backsplash, a window with curtains, a shelf with jars and a
        //    pot plant, a wall clock, the counter with its cabinet doors, a syrup bottle and a strawberry bowl. ──

        private VisualElement _room;

        private void BuildScenery()
        {
            var wall = MgScenery.Layer(Field, "mgf-pancake-wall");
            for (var i = 0; i < 8; i++)
            {
                var stripe = MgScenery.Shape(wall, MgScenery.Hex(0xFBE4D2));
                MgScenery.Pct(stripe, i * 12.5f + 3f, 0, null, 0);
                stripe.style.width = Length.Percent(6.25f);
            }
            _room = MgScenery.Layer(Field, "mgf-pancake-room");
            var g = GroundFrac * 100f;
            var tiles = MgScenery.Tiles(_room, 6, 3, MgScenery.Hex(0xD9F2EE), MgScenery.Hex(0xF4FBF9), MgScenery.Hex(0xE8D9C6), MgScenery.Hex(0xBFE3DC));
            MgScenery.Pct(tiles, 0, g - 17f, 0);
            tiles.style.height = Length.Percent(17f);
            var window = MgScenery.Window(_room, 138f, 124f);
            MgScenery.Pct(window, 9f, 22f);
            // the shelf: a plank with three jars and a pot plant standing on it
            var shelf = MgScenery.Shape(_room, MgScenery.Hex(0xE8C9A0), 5f, "gb-mgf-shelf");
            MgScenery.Pct(shelf, null, 45f, 5f);
            MgScenery.Size(shelf, 118f, 12f);
            MgScenery.Outline(shelf, 2f, MgScenery.Hex(0xC8A27A));
            var jarC = new[] { MgScenery.Hex(0xFFD166), MgScenery.Hex(0xFF9BC1), MgScenery.Hex(0x9BE7E1) };
            for (var i = 0; i < 3; i++)
            {
                var jar = MgScenery.Shape(shelf, jarC[i], 8f, "gb-mgf-jar");
                jar.style.left = 6f + i * 27f; jar.style.bottom = Length.Percent(100);
                MgScenery.Size(jar, 22f, 26f + (i % 2) * 6f);
                MgScenery.Outline(jar, 2f, new Color(1f, 1f, 1f, 0.9f));
                var lid = MgScenery.Shape(jar, MgScenery.Hex(0xC8A27A), 3f);
                lid.style.left = 2f; lid.style.right = 2f; lid.style.top = -6f; lid.style.height = 7f;
            }
            var pot = MgScenery.Sprite(shelf, "plant_flowers_pink", 38f, 42f);
            pot.style.right = 2f; pot.style.bottom = Length.Percent(100);
            // the wall clock
            var clock = MgScenery.Shape(_room, MgScenery.Hex(0xFFFDF8), 26f, "gb-mgf-clock");
            MgScenery.Pct(clock, null, 22f, 12f);
            MgScenery.Size(clock, 52f, 52f);
            MgScenery.Outline(clock, 4f, MgScenery.Hex(0xFF8B8B));
            var hand = MgScenery.Shape(clock, MgScenery.Hex(0x6B645C), 2f);
            hand.style.left = 20f; hand.style.top = 8f; MgScenery.Size(hand, 4f, 16f);
            var hand2 = MgScenery.Shape(clock, MgScenery.Hex(0x6B645C), 2f);
            hand2.style.left = 20f; hand2.style.top = 20f; MgScenery.Size(hand2, 14f, 4f);
            // the counter: worktop edge, cabinet body with two doors and knobs
            var body = MgScenery.Shape(_room, MgScenery.Hex(0xC99A6E), 0f, "gb-mgf-counter");
            MgScenery.Pct(body, 0, g, 0, 0);
            for (var i = 0; i < 2; i++)
            {
                var door = MgScenery.Shape(body, MgScenery.Hex(0xD4A87C), 10f, "gb-mgf-door");
                MgScenery.Pct(door, 6f + i * 46f, 22f, null, 12f);
                door.style.width = Length.Percent(42f);
                MgScenery.Outline(door, 3f, MgScenery.Hex(0xB5845A));
                var knob = MgScenery.Shape(door, MgScenery.Hex(0xFFD166), 5f);
                MgScenery.Pct(knob, i == 0 ? 82f : 8f, 40f);
                MgScenery.Size(knob, 10f, 10f);
            }
            var top = MgScenery.Shape(_room, MgScenery.Hex(0xE8C9A0), 7f, "gb-mgf-worktop");
            MgScenery.Pct(top, 0, g, 0);
            top.style.left = -8; top.style.right = -8; top.style.height = 14; top.style.translate = new Translate(0, -6);
            MgScenery.Outline(top, 2f, MgScenery.Hex(0xC8A27A));
            // breakfast bits on the worktop, well clear of the plate
            var syrup = MgScenery.Shape(_room, MgScenery.Hex(0xE9A23B), 9f, "gb-mgf-syrup");
            MgScenery.Pct(syrup, null, null, 6f, 100f - g);
            MgScenery.Size(syrup, 26f, 46f);
            syrup.style.translate = new Translate(0, -4);
            MgScenery.Outline(syrup, 2f, MgScenery.Hex(0xB9761F));
            var cap = MgScenery.Shape(syrup, MgScenery.Hex(0xFF8B8B), 3f);
            cap.style.left = 5f; cap.style.right = 5f; cap.style.top = -9f; cap.style.height = 9f;
            var label = MgScenery.Shape(syrup, MgScenery.Hex(0xFFF4E4), 3f);
            label.style.left = 2f; label.style.right = 2f; label.style.top = 16f; label.style.height = 12f;
            var bowl = MgScenery.Shape(_room, MgScenery.Hex(0xFFFFFF), 0f, "gb-mgf-bowl");
            MgScenery.Pct(bowl, 5f, null, null, 100f - g);
            MgScenery.Size(bowl, 48f, 20f);
            MgScenery.RoundBottom(bowl, 20f);
            bowl.style.translate = new Translate(0, -4);
            MgScenery.Outline(bowl, 2f, MgScenery.Hex(0xB7D8FF));
            for (var i = 0; i < 2; i++)
            {
                var berry = Ux.Icon("food_strawberry");
                berry.pickingMode = PickingMode.Ignore;
                berry.style.position = Position.Absolute;
                berry.style.left = 4f + i * 18f; berry.style.bottom = 10f; MgScenery.Size(berry, 22f, 22f);
                bowl.Add(berry);
            }
            // the scalloped awning along the top of the wall (static, like the wallpaper)
            var awning = MgScenery.Layer(Field, "mgf-pancake-awning");
            for (var i = 0; i < 7; i++)
            {
                var sc = MgScenery.Shape(awning, i % 2 == 0 ? MgScenery.Hex(0xFFB3CB) : MgScenery.Hex(0xFFF4E4), 0f, "gb-mgf-scallop");
                MgScenery.Pct(sc, i * 100f / 7f, 0);
                sc.style.width = Length.Percent(100f / 7f); sc.style.height = 40f;
                MgScenery.RoundBottom(sc, 22f);
                sc.style.borderBottomWidth = 3; sc.style.borderBottomColor = MgScenery.Hex(0xF29BB8);
            }
        }

        private static VisualElement PancakeShape(bool topping)
        {
            var p = new VisualElement { pickingMode = PickingMode.Ignore };
            p.style.position = Position.Absolute;
            p.style.backgroundColor = topping ? Topping : Pancake;
            var rim = topping ? new Color(0.7f, 0.2f, 0.25f) : PancakeRim;
            p.style.borderTopWidth = 2; p.style.borderBottomWidth = 3; p.style.borderLeftWidth = 2; p.style.borderRightWidth = 2;
            p.style.borderTopColor = rim; p.style.borderBottomColor = rim; p.style.borderLeftColor = rim; p.style.borderRightColor = rim;
            return p;
        }

        private float Ppu => FieldW / (float)ViewUnitsX;
        private float GroundY => FieldH * GroundFrac;
        private float ScreenX(double x) => FieldW * 0.5f + (float)x * Ppu;
        private float ScreenY(double y) => GroundY - (float)(y - _camBottom) * Ppu;

        protected override void TickGame(double dt) { _game.Tick(dt); _pulse += (float)dt; }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var ppu = Ppu;
            var layerH = (float)_game.Tune.LayerHeight * ppu;
            // the camera rides up so the top of the tower stays at ~60 % of the field
            var wantBottom = System.Math.Max(0.0, _game.StackHeight - 0.6 * (GroundFrac * FieldH / ppu));
            _camBottom = Mathf.Lerp(_camBottom, (float)wantBottom, 0.15f);
            // the kitchen rides down with the camera, so the counter stays under the plate
            if (_room != null) _room.style.translate = new Translate(0, _camBottom * ppu);
            if (_stage != null) RenderStage();
            // the plate (in tower coordinates the base is x = 0, y = 0)
            var plateW = (float)_game.Tune.BaseWidth * ppu + 40f;
            _plate.style.left = FieldW * 0.5f - plateW * 0.5f; _plate.style.top = ScreenY(0.0) - 6f; _plate.style.width = plateW;
            // the layers, swaying with the wobble angle around the base
            while (_layers.Count < _game.Layers.Count)
            {
                var l = _game.Layers[_layers.Count];
                var v = PancakeShape(l.Topping);
                v.name = "layer-" + l.Index;
                if (l.Perfect && !l.Topping) v.style.backgroundColor = new Color(0.98f, 0.8f, 0.42f);
                _tower.Add(v);
                _layers.Add(v);
            }
            var angle = _game.Wobble.Angle;
            for (var i = 0; i < _layers.Count; i++)
            {
                var l = _game.Layers[i];
                var h = i * _game.Tune.LayerHeight;
                var cx = PancakeTower.WobbleTopX(l.Center, h, angle);
                var w = (float)l.Width * ppu;
                var v = _layers[i];
                v.style.left = ScreenX(cx) - w * 0.5f; v.style.width = w;
                v.style.top = ScreenY(h + _game.Tune.LayerHeight) ; v.style.height = layerH + 2f;
                v.style.borderTopLeftRadius = layerH * 0.6f; v.style.borderTopRightRadius = layerH * 0.6f; v.style.borderBottomLeftRadius = layerH * 0.6f; v.style.borderBottomRightRadius = layerH * 0.6f;
                v.style.rotate = new Rotate(-(float)angle * Mathf.Rad2Deg);
            }
            // the swinging (or falling) pancake and the spatula above it
            var topping = _game.CurrentIsTopping;
            _swing.style.backgroundColor = topping ? Topping : Pancake;
            var sw = (float)_game.Stack.Width * ppu;
            var sx = _game.Falling ? _game.FallX : _game.SwingX;
            var sy = _game.Falling ? _game.FallY : _game.StackHeight + PancakeTowerGame.DropHeight;
            _swing.style.left = ScreenX(sx) - sw * 0.5f; _swing.style.width = sw;
            _swing.style.top = ScreenY(sy) - layerH; _swing.style.height = layerH + 2f;
            _swing.style.borderTopLeftRadius = layerH * 0.6f; _swing.style.borderTopRightRadius = layerH * 0.6f; _swing.style.borderBottomLeftRadius = layerH * 0.6f; _swing.style.borderBottomRightRadius = layerH * 0.6f;
            _swing.style.display = _game.Finished && _game.Toppled ? DisplayStyle.None : DisplayStyle.Flex;
            _spatula.style.left = ScreenX(sx) - 4f; _spatula.style.top = 0; _spatula.style.height = System.Math.Max(0f, ScreenY(sy) - layerH);
            _spatula.style.display = _game.Falling ? DisplayStyle.None : DisplayStyle.Flex;
            // crumbs: cut-off pieces tumbling down
            var live = new HashSet<PancakeTowerGame.Crumb>(_game.Crumbs);
            foreach (var c in _game.Crumbs)
            {
                if (!_crumbs.TryGetValue(c, out var cv))
                {
                    cv = PancakeShape(false);
                    cv.style.opacity = 0.9f;
                    if (_stage != null) cv.style.visibility = Visibility.Hidden;
                    _crumbs[c] = cv;
                    Field.Add(cv);
                }
                var cw = (float)c.Width * ppu;
                cv.style.left = ScreenX(c.Center) - cw * 0.5f; cv.style.width = cw;
                cv.style.top = ScreenY(c.Y) - layerH; cv.style.height = layerH;
                cv.style.rotate = new Rotate((float)c.Age * 120f);
                cv.style.opacity = 1f - (float)(c.Age / _game.Tune.FallenDespawnSec);
            }
            var dead = new List<PancakeTowerGame.Crumb>();
            foreach (var kv in _crumbs) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _crumbs[d].RemoveFromHierarchy(); _crumbs.Remove(d); }
            TimeLabel.text = t.T("mg.pancakeTower.layers", ("n", _game.Layers.Count));
            ScoreLabel.text = _game.Score.ToString();
            _layersLabel.text = t.T("mg.pancakeTower.layers", ("n", _game.Layers.Count));
            _widthLabel.text = t.T("mg.pancakeTower.width", ("pct", Mathf.RoundToInt((float)_game.WidthFrac * 100f)));
            _widthLabel.style.color = _game.WidthFrac < 0.4 ? new Color(0.85f, 0.3f, 0.3f) : new Color(0.45f, 0.4f, 0.45f);
        }

        private void OnLanded(PancakeTowerGame.Layer layer, PancakeTower.Drop drop)
        {
            StageLanded(layer, drop);
            var at = new Vector2(ScreenX(layer.Center), ScreenY(_game.StackHeight) - 30f);
            if (drop.Perfect) FloatText(at, Host.Strings.T("mg.pancakeTower.perfect") + " +" + drop.Points, new Color(0.95f, 0.72f, 0.2f));
            else if (drop.Points > 0) FloatText(at, "+" + drop.Points, new Color(0.24f, 0.55f, 0.32f));
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _layers.Clear(); _crumbs.Clear();
            _camBottom = 0f;
            _layersLabel?.RemoveFromHierarchy();
            _widthLabel?.RemoveFromHierarchy();
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
