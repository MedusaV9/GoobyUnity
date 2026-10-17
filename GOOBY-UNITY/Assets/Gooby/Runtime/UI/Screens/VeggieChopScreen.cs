using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Gemüse-Schnippler host on the shared minigame frame (port of veggie_chop.gd + its kitchen stage): produce is
    /// tossed up out of the pan on the counter and flies in arcs across a tiled kitchen wall; the finger is the knife —
    /// a swipe across a piece splits it in two halves that fall apart in a splash of juice (+2, +1 more for every
    /// further piece in the same swipe), a can or a boot smashes (−3) and dizzies the cook for half a second (the
    /// kitchen greys out), a veggie that drops back uncut is a miss and three misses end the round. Every 25 s a
    /// frenzy throws eight veggies in a row.
    /// </summary>
    public sealed partial class VeggieChopScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private VeggieChopGame _game;
        private readonly Dictionary<VeggieChopGame.Flying, VisualElement> _views = new Dictionary<VeggieChopGame.Flying, VisualElement>();
        private readonly List<(VisualElement dot, float born)> _trail = new List<(VisualElement, float)>();
        private VisualElement _veil, _missPill, _tiles, _gooby;
        private Label _missLabel;
        private bool _swiping;
        private Vector2 _last;
        private float _frenzyGlow;

        private static readonly Color Wall = new Color(0.98f, 0.93f, 0.84f);
        private static readonly Color WallFrenzy = new Color(1f, 0.9f, 0.68f);
        private static readonly Color Wood = new Color(0.78f, 0.58f, 0.4f);
        private static readonly Color WoodDark = new Color(0.62f, 0.44f, 0.3f);
        private const float TrailLife = 0.24f;

        public VeggieChopScreen(GameHost host) : base(host, "veggieChop", "veggieChop") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xEDBE98);
        protected override Color FieldColor => Wall;

        /// <summary>The running round (flow tests drive it directly).</summary>
        public VeggieChopGame Game => _game;

        protected override void StartGame(int seed)
        {
            _game = new VeggieChopGame(Difficulty, seed);
            _game.SetAspect(ViewW / ViewH);
            _game.Chopped += OnChopped;
            _game.JunkHit += OnJunk;
            _game.Missed += OnMissed;
            _game.Frenzy += () => { Banner(Host.Strings.T("mg.veggieChop.frenzy"), 2.0); _frenzyGlow = 1f; };
            _game.Finished_ += RoundOver;
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            Field.RegisterCallback<PointerDownEvent>(OnDown);
            Field.RegisterCallback<PointerMoveEvent>(OnMove);
            Field.RegisterCallback<PointerUpEvent>(OnUp);
            Field.RegisterCallback<PointerCaptureOutEvent>(_ => _swiping = false);

            if (World.MgStage3D.Supported) BuildStage();
            else BuildKitchen2D();

            // the dizzy veil over everything, hidden until junk is hit
            _veil = new VisualElement { pickingMode = PickingMode.Ignore };
            Ux.Abs(_veil, 0, 0, 0, 0);
            _veil.style.backgroundColor = new Color(0.45f, 0.45f, 0.52f, 0f);
            Field.Add(_veil);

            // misses (or junk hits in endless) under the pills
            _missPill = Ux.El("gb-card", "gb-row");
            _missPill.name = "miss-pill";
            Ux.Abs(_missPill, null, 62);
            _missPill.style.left = Length.Percent(50);
            _missPill.style.translate = new Translate(Length.Percent(-50), 0);
            _missPill.style.alignItems = Align.Center;
            _missPill.style.paddingTop = 6; _missPill.style.paddingBottom = 6; _missPill.style.paddingLeft = 12; _missPill.style.paddingRight = 16;
            _missPill.pickingMode = PickingMode.Ignore;
            var knife = Ux.Icon("food_carrot");
            knife.style.width = 34; knife.style.height = 34; knife.style.marginRight = 8;
            _missPill.Add(knife);
            _missLabel = Ux.Text("", "gb-md", "gb-bold");
            _missPill.Add(_missLabel);
            Root.Add(_missPill);
            ComboLabel.style.top = 128;
            Banner(t.T("mg.veggieChop.intro"), VeggieChopGame.IntroSec + 0.7);
        }

        /// <summary>The painted kitchen (no 3D stage): tiles, shelf and jars, the round window, counter, board, pan and Gooby.</summary>
        private void BuildKitchen2D()
        {
            BuildScenery();
            // the splashback: rows of pastel tiles across the middle of the wall
            _tiles = new VisualElement { pickingMode = PickingMode.Ignore };
            Ux.Abs(_tiles, 0, null, 0, null);
            _tiles.style.top = Length.Percent(34); _tiles.style.height = Length.Percent(40);
            _tiles.style.flexDirection = FlexDirection.Row; _tiles.style.flexWrap = Wrap.Wrap;
            _tiles.style.paddingLeft = 2;
            Field.Add(_tiles);
            var tints = new[] { new Color(0.86f, 0.93f, 0.9f), new Color(0.93f, 0.9f, 0.84f), new Color(0.82f, 0.9f, 0.94f) };
            for (var i = 0; i < 9 * 7; i++)
            {
                var tile = new VisualElement { pickingMode = PickingMode.Ignore };
                tile.style.width = Length.Percent(10.6f); tile.style.height = Length.Percent(13.6f);
                tile.style.marginLeft = 2; tile.style.marginTop = 2;
                tile.style.borderTopLeftRadius = 6; tile.style.borderTopRightRadius = 6; tile.style.borderBottomLeftRadius = 6; tile.style.borderBottomRightRadius = 6;
                tile.style.backgroundColor = tints[(i * 7 + i / 9) % 3];
                _tiles.Add(tile);
            }
            // a shelf with jars and a herb pot above the tiles
            var shelf = new VisualElement { pickingMode = PickingMode.Ignore };
            Ux.Abs(shelf, 24, null, 24, null);
            shelf.style.top = Length.Percent(27); shelf.style.height = 10;
            shelf.style.backgroundColor = WoodDark;
            shelf.style.borderTopLeftRadius = 4; shelf.style.borderTopRightRadius = 4;
            Field.Add(shelf);
            var jarTints = new[] { new Color(0.95f, 0.75f, 0.45f), new Color(0.75f, 0.85f, 0.55f), new Color(0.9f, 0.6f, 0.55f) };
            for (var i = 0; i < 3; i++)
            {
                var jar = new VisualElement { pickingMode = PickingMode.Ignore };
                jar.style.position = Position.Absolute;
                jar.style.left = Length.Percent(36 + i * 15); jar.style.top = Length.Percent(27);
                jar.style.width = 34; jar.style.height = 44;
                jar.style.translate = new Translate(0, -44);
                jar.style.borderTopLeftRadius = 8; jar.style.borderTopRightRadius = 8; jar.style.borderBottomLeftRadius = 10; jar.style.borderBottomRightRadius = 10;
                jar.style.backgroundColor = jarTints[i];
                var lid = new VisualElement { pickingMode = PickingMode.Ignore };
                lid.style.position = Position.Absolute; lid.style.left = 4; lid.style.right = 4; lid.style.top = -6; lid.style.height = 10;
                lid.style.backgroundColor = WoodDark;
                lid.style.borderTopLeftRadius = 4; lid.style.borderTopRightRadius = 4;
                jar.Add(lid);
                Field.Add(jar);
            }
            var herb = Ux.Icon("sprout");
            herb.style.position = Position.Absolute;
            herb.style.left = 30; herb.style.top = Length.Percent(27);
            herb.style.width = 56; herb.style.height = 56;
            herb.style.translate = new Translate(0, -54);
            herb.pickingMode = PickingMode.Ignore;
            Field.Add(herb);
            // a round window up on the wall, right of the pills, with a slice of Sonnental sky
            var window = new VisualElement { pickingMode = PickingMode.Ignore };
            window.style.position = Position.Absolute;
            window.style.right = 14; window.style.top = Length.Percent(7);
            window.style.width = 100; window.style.height = 100;
            window.style.borderTopLeftRadius = 50; window.style.borderTopRightRadius = 50; window.style.borderBottomLeftRadius = 50; window.style.borderBottomRightRadius = 50;
            window.style.backgroundColor = new Color(0.62f, 0.83f, 0.98f);
            window.style.borderTopWidth = 8; window.style.borderBottomWidth = 8; window.style.borderLeftWidth = 8; window.style.borderRightWidth = 8;
            window.style.borderTopColor = Color.white; window.style.borderBottomColor = Color.white; window.style.borderLeftColor = Color.white; window.style.borderRightColor = Color.white;
            var cloud = Ux.Icon("cloud");
            cloud.style.position = Position.Absolute; cloud.style.left = 14; cloud.style.top = 28; cloud.style.width = 54; cloud.style.height = 54;
            cloud.pickingMode = PickingMode.Ignore;
            window.Add(cloud);
            Field.Add(window);

            // the counter with its cutting board, and the pan the produce is tossed out of
            var counter = new VisualElement { pickingMode = PickingMode.Ignore };
            Ux.Abs(counter, 0, null, 0, 0);
            counter.style.height = Length.Percent(15);
            counter.style.backgroundColor = Wood;
            counter.style.borderTopWidth = 10; counter.style.borderTopColor = new Color(0.9f, 0.76f, 0.58f);
            Field.Add(counter);
            var board = new VisualElement { pickingMode = PickingMode.Ignore };
            board.style.position = Position.Absolute;
            board.style.left = 22; board.style.width = 130; board.style.height = 54; board.style.bottom = Length.Percent(12);
            board.style.backgroundColor = new Color(0.94f, 0.82f, 0.62f);
            board.style.borderTopLeftRadius = 14; board.style.borderTopRightRadius = 14; board.style.borderBottomLeftRadius = 14; board.style.borderBottomRightRadius = 14;
            board.style.borderBottomWidth = 6; board.style.borderBottomColor = new Color(0.8f, 0.64f, 0.44f);
            Field.Add(board);
            var pan = new VisualElement { pickingMode = PickingMode.Ignore };
            pan.style.position = Position.Absolute;
            pan.style.left = Length.Percent(50); pan.style.width = 210; pan.style.height = 70; pan.style.bottom = -26;
            pan.style.translate = new Translate(-105, 0);
            pan.style.backgroundColor = new Color(0.3f, 0.32f, 0.38f);
            pan.style.borderTopLeftRadius = 105; pan.style.borderTopRightRadius = 105; pan.style.borderBottomLeftRadius = 40; pan.style.borderBottomRightRadius = 40;
            pan.style.borderTopWidth = 8; pan.style.borderTopColor = new Color(0.55f, 0.58f, 0.66f);
            Field.Add(pan);
            var handle = new VisualElement { pickingMode = PickingMode.Ignore };
            handle.style.position = Position.Absolute;
            handle.style.left = Length.Percent(50); handle.style.width = 110; handle.style.height = 18; handle.style.bottom = 20;
            handle.style.translate = new Translate(90, 0);
            handle.style.rotate = new Rotate(-12f);
            handle.style.backgroundColor = new Color(0.22f, 0.24f, 0.3f);
            handle.style.borderTopLeftRadius = 9; handle.style.borderTopRightRadius = 9; handle.style.borderBottomLeftRadius = 9; handle.style.borderBottomRightRadius = 9;
            Field.Add(handle);
            // Gooby the cook watches from the corner of the counter
            _gooby = Ux.Portrait(84);
            _gooby.style.position = Position.Absolute;
            _gooby.style.right = 16; _gooby.style.bottom = Length.Percent(9);
            _gooby.pickingMode = PickingMode.Ignore;
            Field.Add(_gooby);
            FollowGooby(_gooby, 1.6f, grounded: true);
        }

        // ── scenery (board 06): the kitchen already has its tiles, shelf, jars, window, counter, board and pan; this
        //    layer only adds what sits behind them without covering the wall (its colour carries the frenzy glow):
        //    a faint dotted wallpaper, a mint pelmet over the round window, a rail with ladle, spatula and whisk, and a
        //    beadboard wainscot between the tiles and the counter. Built once, pointer-transparent, nothing moves. ──

        private void BuildScenery()
        {
            var scene = MgScenery.Layer(Field, "mgf-veggiechop");
            var paper = MgScenery.Dots(scene, 40, new Color(0.9f, 0.72f, 0.55f, 0.22f), 2f, 3.5f, 41);
            MgScenery.Pct(paper, 0, 0, 0);
            paper.style.height = Length.Percent(33f);
            var wains = MgScenery.Painted(scene, (p, r) =>
            {
                MgScenery.FillRect(p, 0f, 0f, r.width, r.height, new Color(0.99f, 0.84f, 0.78f, 0.75f));
                p.strokeColor = new Color(0.9f, 0.66f, 0.6f, 0.6f); p.lineWidth = 2f;
                for (var x = 14f; x < r.width; x += 22f) { p.BeginPath(); p.MoveTo(new Vector2(x, 10f)); p.LineTo(new Vector2(x, r.height)); p.Stroke(); }
                MgScenery.FillRect(p, 0f, 0f, r.width, 8f, MgScenery.Hex(0xF6E4C8));
            }, "gb-mgf-wainscot");
            MgScenery.Pct(wains, 0, 74f, 0, 12f);
            // a mint pelmet over the round window (right, 7 %)
            var pelmet = MgScenery.Scallop(scene, 4, 6f, MgScenery.Hex(0x9BE7C8), MgScenery.Hex(0x6FCBA8));
            pelmet.style.right = 4f; pelmet.style.width = 120f; pelmet.style.height = 26f;
            MgScenery.Pct(pelmet, null, 5.2f);
            // the utensil rail on the left wall
            var rail = MgScenery.Group(scene, "gb-mgf-rail-set");
            MgScenery.Pct(rail, 5f, 9f);
            MgScenery.Size(rail, 120f, 96f);
            var bar = MgScenery.Shape(rail, MgScenery.Hex(0xC8A27A), 4f); bar.style.left = 0; bar.style.right = 0; bar.style.top = 0; bar.style.height = 8f;
            MgScenery.Outline(bar, 2f, MgScenery.Hex(0xA9825B));
            var steel = MgScenery.Hex(0xB9C4D6); var steelRim = MgScenery.Hex(0x8C99AE);
            // ladle
            var ls = MgScenery.Shape(rail, steel, 2f); ls.style.left = 20f; ls.style.top = 6f; MgScenery.Size(ls, 5f, 56f);
            var lb = MgScenery.Shape(rail, steel, 11f); lb.style.left = 12f; lb.style.top = 58f; MgScenery.Size(lb, 22f, 20f); MgScenery.Outline(lb, 2f, steelRim);
            // spatula (wooden)
            var ss = MgScenery.Shape(rail, MgScenery.Hex(0xE8C9A0), 2f); ss.style.left = 56f; ss.style.top = 6f; MgScenery.Size(ss, 6f, 50f);
            var sb = MgScenery.Shape(rail, MgScenery.Hex(0xE8C9A0), 6f); sb.style.left = 49f; sb.style.top = 52f; MgScenery.Size(sb, 20f, 28f); MgScenery.Outline(sb, 2f, MgScenery.Hex(0xC8A27A));
            // whisk
            var ws = MgScenery.Shape(rail, MgScenery.Hex(0xFF9BC1), 3f); ws.style.left = 92f; ws.style.top = 6f; MgScenery.Size(ws, 6f, 30f);
            var wb = MgScenery.Shape(rail, Color.clear, 0f); wb.style.left = 84f; wb.style.top = 34f; MgScenery.Size(wb, 22f, 40f);
            MgScenery.RoundBottom(wb, 11f); MgScenery.RoundTop(wb, 4f); MgScenery.Outline(wb, 3f, steelRim);
            var wm = MgScenery.Shape(rail, Color.clear, 0f); wm.style.left = 92f; wm.style.top = 34f; MgScenery.Size(wm, 6f, 38f);
            MgScenery.RoundBottom(wm, 3f); MgScenery.Outline(wm, 2f, steelRim);
            foreach (var hx in new[] { 21f, 57f, 93f })
            {
                var hook = MgScenery.Shape(rail, MgScenery.Hex(0xA9825B), 2f); hook.style.left = hx - 1f; hook.style.top = 2f; MgScenery.Size(hook, 5f, 8f);
            }
        }

        // ── the knife ──────────────────────────────────────────────────────

        private void OnDown(PointerDownEvent e)
        {
            if (_game == null) return;
            _swiping = true;
            _last = e.localPosition;
            Field.CapturePointer(e.pointerId);
            _game.BeginSwipe();
            AddTrailDot(_last);
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_game == null || !_swiping || FieldW <= 0f || FieldH <= 0f) return;
            var now = (Vector2)e.localPosition;
            if ((now - _last).sqrMagnitude < 1f) return;
            AddTrailDot(now);
            _game.CutNormalized(_last.x / FieldW, _last.y / FieldH, now.x / FieldW, now.y / FieldH);
            _last = now;
        }

        private void OnUp(PointerUpEvent e)
        {
            _swiping = false;
            if (Field.HasPointerCapture(e.pointerId)) Field.ReleasePointer(e.pointerId);
        }

        /// <summary>Public for the flow tests: one straight stroke across the field in view space (0..1).</summary>
        public int Swipe(float nx0, float ny0, float nx1, float ny1)
        {
            if (_game == null) return 0;
            _game.BeginSwipe();
            return _game.CutNormalized(nx0, ny0, nx1, ny1);
        }

        private void AddTrailDot(Vector2 at)
        {
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.position = Position.Absolute;
            dot.style.width = 14; dot.style.height = 14;
            dot.style.left = at.x - 7; dot.style.top = at.y - 7;
            dot.style.borderTopLeftRadius = 7; dot.style.borderTopRightRadius = 7; dot.style.borderBottomLeftRadius = 7; dot.style.borderBottomRightRadius = 7;
            dot.style.backgroundColor = new Color(1f, 1f, 1f, 0.95f);
            Field.Add(dot);
            _trail.Add((dot, Time.unscaledTime));
        }

        // ── the round ──────────────────────────────────────────────────────

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _frenzyGlow = Mathf.Max(0f, _frenzyGlow - (float)dt * 0.35f);
            TickDebris((float)dt);
        }

        /// <summary>
        /// Cut halves and juice drops: ballistic, on the round's clock (TickGame) — they stand still in the pause and
        /// vanish with the round, instead of element schedules that ran on through pause and results.
        /// </summary>
        private struct Debris { public VisualElement E; public float T, Life, X0, Y0, Vx, Vy, G, Spin, Fade, FadeRate, Half; }
        private readonly List<Debris> _debris = new List<Debris>();

        private void TickDebris(float dt)
        {
            for (var i = _debris.Count - 1; i >= 0; i--)
            {
                var d = _debris[i];
                d.T += dt;
                if (d.T >= d.Life || d.E.parent == null) { d.E.RemoveFromHierarchy(); _debris.RemoveAt(i); continue; }
                d.E.style.left = d.X0 + d.Vx * d.T - d.Half;
                d.E.style.top = d.Y0 + d.Vy * d.T + d.G * d.T * d.T - d.Half;
                if (d.Spin != 0f) d.E.style.rotate = new Rotate(d.Spin * d.T);
                d.E.style.opacity = Mathf.Clamp01(d.Fade - d.T * d.FadeRate);
                _debris[i] = d;
            }
        }

        private float Ppu => FieldH / (float)(2.0 * VeggieChopGame.HalfH);
        private float PieceD => 1.05f * Ppu;

        private Vector2 ToField(double x, double y)
        {
            var (nx, ny) = _game.ToView(x, y);
            return new Vector2((float)nx * FieldW, (float)ny * FieldH);
        }

        protected override void RenderGame()
        {
            var live = new HashSet<VeggieChopGame.Flying>(_game.Items);
            if (_stage != null) RenderStage();
            foreach (var e in _game.Items)
            {
                if (_stage != null) break;
                if (!_views.TryGetValue(e, out var v))
                {
                    v = PieceView(e);
                    _views[e] = v;
                    Field.Add(v);
                }
                var at = ToField(e.X, e.Y);
                var d = PieceD;
                v.style.left = at.x - d * 0.5f; v.style.top = at.y - d * 0.5f;
                v.style.rotate = new Rotate((float)(e.Spin * Mathf.Rad2Deg + e.T * 140.0 * (e.Arc.Vx >= 0 ? 1.0 : -1.0)));
            }
            var dead = new List<VeggieChopGame.Flying>();
            foreach (var kv in _views) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var d in dead) { _views[d].RemoveFromHierarchy(); _views.Remove(d); }

            // the trail fades within a quarter second
            for (var i = _trail.Count - 1; i >= 0; i--)
            {
                var age = Time.unscaledTime - _trail[i].born;
                if (age >= TrailLife) { _trail[i].dot.RemoveFromHierarchy(); _trail.RemoveAt(i); continue; }
                var k = 1f - age / TrailLife;
                _trail[i].dot.style.opacity = k;
                _trail[i].dot.style.scale = new Scale(new Vector3(0.4f + 0.6f * k, 0.4f + 0.6f * k, 1f));
            }

            _veil.style.backgroundColor = new Color(0.45f, 0.45f, 0.52f, _game.Stunned ? 0.42f : 0f);
            if (_stage == null)
            {
                Field.style.backgroundColor = Color.Lerp(Wall, WallFrenzy, _frenzyGlow * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 9f)));
                _gooby.style.rotate = new Rotate(_game.Stunned ? Mathf.Sin(Time.unscaledTime * 18f) * 10f : _game.SwipeCombo > 0 ? -6f : 0f);
            }

            var t = Host.Strings;
            TimeLabel.text = _game.Tune.Endless
                ? t.T("mg.veggieChop.junk_hits", ("n", _game.JunkHits), ("max", _game.Tune.EndlessJunkHits))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)_game.TimeLeft)));
            ScoreLabel.text = _game.Score.ToString();
            _missLabel.text = t.T("mg.veggieChop.missed", ("n", _game.Misses), ("max", _game.Tune.MaxMisses));
            _missLabel.style.color = _game.Misses >= _game.Tune.MaxMisses - 1 ? new StyleColor(new Color(0.85f, 0.3f, 0.3f)) : new StyleColor(StyleKeyword.Null);
            ComboLabel.text = _game.InFrenzy ? t.T("mg.veggieChop.frenzy") : _game.SwipeCombo >= 2 ? t.T("mg.veggieChop.combo") : "";
        }

        private static string IconFor(VeggieChopGame.Flying e) => (e.Item.Kind == "junk" ? "junk_" : "food_") + e.Item.Key;

        /// <summary>A flying piece: its icon on a soft round shadow (junk sits on a grey one).</summary>
        private VisualElement PieceView(VeggieChopGame.Flying e)
        {
            var d = PieceD;
            var v = new VisualElement { pickingMode = PickingMode.Ignore };
            v.style.position = Position.Absolute;
            v.style.width = d; v.style.height = d;
            v.style.justifyContent = Justify.Center; v.style.alignItems = Align.Center;
            var icon = Ux.Icon(IconFor(e));
            icon.style.width = d * 0.92f; icon.style.height = d * 0.92f;
            icon.pickingMode = PickingMode.Ignore;
            v.Add(icon);
            return v;
        }

        // ── juice ──────────────────────────────────────────────────────────

        private void OnChopped(VeggieChopGame.Flying e, int points, int combo)
        {
            if (FieldW <= 0f) return;
            var at = ToField(e.X, e.Y);
            if (_stage != null) StageChopped(e, Hex(e.Item.Juice));
            else
            {
                SplitPiece(e, at);
                Splash(at, Hex(e.Item.Juice));
            }
            FloatText(at, "+" + points, new Color(0.24f, 0.55f, 0.32f));
            if (combo > 1) FloatText(at - new Vector2(0f, 44f), Host.Strings.T("mg.veggieChop.combo"), new Color(0.95f, 0.45f, 0.7f));
        }

        private void OnJunk(VeggieChopGame.Flying e)
        {
            if (FieldW <= 0f) return;
            var at = ToField(e.X, e.Y);
            if (_stage != null) StageJunk(e);
            else Splash(at, new Color(0.6f, 0.62f, 0.68f));
            FloatText(at, Host.Strings.T("mg.veggieChop.junk"), new Color(0.85f, 0.3f, 0.3f));
        }

        private void OnMissed(int left)
        {
            if (FieldW <= 0f) return;
            FloatText(new Vector2(FieldW * 0.5f, FieldH - 150f), Host.Strings.T("mg.veggieChop.miss", ("n", left)), new Color(0.85f, 0.3f, 0.3f));
        }

        private static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString("#" + hex, out var c)) return c;
            return Color.white;
        }

        /// <summary>The two halves of a cut piece: each shows one half of the icon and tumbles away from the cut.</summary>
        private void SplitPiece(VeggieChopGame.Flying e, Vector2 at)
        {
            var d = PieceD;
            var icon = IconFor(e);
            for (var side = 0; side < 2; side++)
            {
                var half = new VisualElement { pickingMode = PickingMode.Ignore };
                half.style.position = Position.Absolute;
                half.style.width = d * 0.5f; half.style.height = d;
                half.style.overflow = Overflow.Hidden;
                var img = Ux.Icon(icon);
                img.style.position = Position.Absolute;
                img.style.width = d; img.style.height = d;
                img.style.left = side == 0 ? 0f : -d * 0.5f; img.style.top = 0f;
                img.pickingMode = PickingMode.Ignore;
                half.Add(img);
                Field.Add(half);
                var dir = side == 0 ? -1f : 1f;
                var x0 = at.x - d * 0.5f + side * d * 0.5f; var y0 = at.y - d * 0.5f;
                half.style.left = x0; half.style.top = y0;
                _debris.Add(new Debris
                {
                    E = half, Life = 0.85f, X0 = x0, Y0 = y0, Vx = dir * (70f + 30f * side), Vy = -140f, G = 520f,
                    Spin = dir * 260f, Fade = 1.4f, FadeRate = 1.6f, Half = 0f,
                });
            }
        }

        /// <summary>Juice drops flying out of the cut in the piece's colour.</summary>
        private void Splash(Vector2 at, Color juice)
        {
            for (var i = 0; i < 7; i++)
            {
                var drop = new VisualElement { pickingMode = PickingMode.Ignore };
                drop.style.position = Position.Absolute;
                var s = 8f + (i * 5) % 9;
                drop.style.width = s; drop.style.height = s;
                drop.style.borderTopLeftRadius = s; drop.style.borderTopRightRadius = s; drop.style.borderBottomLeftRadius = s; drop.style.borderBottomRightRadius = s;
                drop.style.backgroundColor = juice;
                Field.Add(drop);
                var a = i * Mathf.PI * 2f / 7f + 0.4f;
                drop.style.left = at.x - s * 0.5f; drop.style.top = at.y - s * 0.5f;
                _debris.Add(new Debris
                {
                    E = drop, Life = 0.55f, X0 = at.x, Y0 = at.y, Vx = Mathf.Cos(a) * (90f + (i * 17) % 50), Vy = Mathf.Sin(a) * (90f + (i * 23) % 50) - 60f,
                    G = 480f, Spin = 0f, Fade = 1.2f, FadeRate = 2.2f, Half = s * 0.5f,
                });
            }
        }

        /// <summary>A float text at a field position — the frame's shared, frame-driven one (Juice).</summary>
        private void FloatText(Vector2 at, string text, Color color) => Float(at, text, color, 26f);

        protected override int TakeFinalScore()
        {
            var score = _game.FinalScore;
            _game = null;
            _views.Clear();
            _trail.Clear();
            foreach (var d in _debris) d.E.RemoveFromHierarchy();
            _debris.Clear();
            _missPill?.RemoveFromHierarchy();
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
