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
    /// Tortenwerkstatt (purblePlace) host on the shared minigame frame — port of purble_place.gd as a pastel bakery:
    /// the order board on top, a conveyor running down the left past the sponge nozzles, the oven tunnel with its bake
    /// clock, the icing and topping nozzles, the candle dispenser and the shipping hatch. Hold ▲/▼ to drive the belt,
    /// park a pan under a nozzle and tap its button — the drop falls for 0.45 s, so the pan must stand still.
    /// Readable state after purble_place.gd: the station the oldest pan needs next wears the gold frame (its catch
    /// window glows on the belt), locked nozzles, a full belt and an empty hatch grey their buttons out.
    /// Game feel: drops fall visibly, a pop and sparkle for a catch, a splat on a miss, a golden glow for a perfect
    /// bake, a stamp and confetti for a perfect cake, a buzz for illegal moves.
    /// </summary>
    public sealed partial class PurblePlaceScreen : MinigameHostScreen
    {
        public const double ViewW = 390.0, ViewH = 844.0;

        private PurblePlaceGame _game;
        private BakeryCanvas _canvas;
        private VisualElement _board, _controls;
        private Label _bakeLabel;
        private readonly List<VisualElement> _ticketCards = new List<VisualElement>();
        private readonly Dictionary<string, VisualElement> _buttons = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, float> _columnOf = new Dictionary<string, float>();
        private readonly HashSet<string> _gold = new HashSet<string>();
        private readonly HashSet<string> _goldWanted = new HashSet<string>();
        private int _ticketKey = -1;
        private double _travel;
        private string _need = "", _bakeState = "";

        private const float BeltTopPct = 0.2f, BeltBottomPct = 0.86f, BeltXPct = 0.27f;
        // neighbouring nozzles sit only 0.3 m apart: their buttons alternate between two columns, level with the nozzle
        private const float ColAPct = 0.43f, ColBPct = 0.715f, ColWPct = 0.265f;

        private static readonly Color BeltC = MgScenery.Hex(0xB8B0C4);
        private static readonly Color BeltDark = MgScenery.Hex(0xA49BB3);
        private static readonly Color RollerC = MgScenery.Hex(0x8E86A0);
        private static readonly Color OvenC = MgScenery.Hex(0xC98B63);
        private static readonly Color OvenDark = MgScenery.Hex(0xA86E4C);
        private static readonly Color GlowC = new Color(1f, 0.62f, 0.25f, 0.55f);
        private static readonly Color PanC = MgScenery.Hex(0xC8D2DC);
        private static readonly Color PanDark = MgScenery.Hex(0x9EA9B5);
        private static readonly Color Shadow = new Color(0.3f, 0.22f, 0.25f, 0.18f);
        private static readonly Color CandleC = MgScenery.Hex(0xFFF3C4);
        private static readonly Color FlameC = MgScenery.Hex(0xFFB347);
        private static readonly Color Gold = MgScenery.Hex(0xF2B630);
        private static readonly Color PlateC = MgScenery.Hex(0xE6E0EE);
        private static readonly Color PaleC = MgScenery.Hex(0xF3E3C3);
        private static readonly Color GreenC = MgScenery.Hex(0x7CCB86);
        private static readonly Color AmberC = MgScenery.Hex(0xF6C343);
        private static readonly Color RedC = MgScenery.Hex(0xF26D6D);

        public PurblePlaceScreen(GameHost host) : base(host, "purblePlace", "purblePlace") { }

        protected override (double w, double h) ViewSize => (ViewW, ViewH);
        protected override bool Running => _game != null;
        protected override Color BackdropColor => MgScenery.Hex(0xF6DCE6);
        protected override Color FieldColor => MgScenery.Hex(0xFFF1E8);
        protected override ScoreFeel ScoreFeedback => ScoreFeel.Off;

        public PurblePlaceGame Game => _game;

        /// <summary>Button id that wears the "needed next" gold frame ("" while the oven is next or nothing is ordered).</summary>
        public string NeededButton => _need;

        protected override void StartGame(int seed)
        {
            _game = new PurblePlaceGame(Difficulty, seed);
            _game.Event += OnEvent;
            _game.Finished_ += RoundOver;
            _ticketKey = -1;
            _need = "";
            _bakeState = "";
            _gold.Clear();
            Sfx.Preload("game_pop");
        }

        protected override void BuildPlayField()
        {
            var t = Host.Strings;
            if (World.MgStage3D.Supported) BuildStage();
            else
            {
                var scene = MgScenery.Layer(Field, "mgf-purbleplace");
                MgScenery.Gradient(scene, MgScenery.Hex(0xFFF4EA), MgScenery.Hex(0xFBE3E9), 0f, 100f);
            }
            _canvas = new BakeryCanvas { Paint = Paint, name = "bakery" };
            Ux.Abs(_canvas, 0, 0, 0, 0);
            Field.Add(_canvas);

            // order board along the top
            _board = Ux.Row();
            _board.name = "order-board";
            Ux.Abs(_board, 8, 8, 8, null);
            _board.style.height = Length.Percent(13f);
            _board.pickingMode = PickingMode.Ignore;
            Field.Add(_board);

            _buttons.Clear();
            _columnOf.Clear();
            _gold.Clear();
            var columnB = false;
            foreach (var st in PurblePlace.Stations)
            {
                if (!st.Drop) continue;
                var b = StationButton(t.T("mg.purblePlace.btn." + st.Id), () => { _game?.Press(st.Id); Sfx.PlayUi("soft_tap"); });
                b.Insert(0, Dot(StationColor(st)));
                b.name = "press-" + st.Id;
                var col = columnB ? ColBPct : ColAPct;
                columnB = !columnB;
                PlaceAt(b, st.S, col, ColWPct);
                Field.Add(b);
                _buttons[st.Id] = b;
                _columnOf[st.Id] = col;
            }
            var ship = StationButton(t.T("mg.purblePlace.btn.versand"), () => { _game?.Ship(); Sfx.PlayUi("soft_confirm"); });
            ship.Insert(0, Dot(MgScenery.Hex(0x9FE0B9)));
            ship.name = "ship";
            PlaceAt(ship, PurblePlace.Cake.ShipS, columnB ? ColBPct : ColAPct, ColWPct);
            Field.Add(ship);
            _buttons["ship"] = ship;

            // the three pan shapes share one strip level with the spawn point at the head of the belt
            var spawnRow = Ux.Row();
            spawnRow.name = "spawn-row";
            spawnRow.style.justifyContent = Justify.SpaceBetween;
            for (var i = 0; i < PurblePlace.Shapes.Length; i++)
            {
                var shape = PurblePlace.Shapes[i];
                var sb = StationButton(t.T("mg.purblePlace.btn." + shape), () => { _game?.Spawn(shape); Sfx.PlayUi("soft_tap"); });
                var icon = new BakeryCanvas { Paint = p => ShapeFill(p, shape, new Vector2(8f, 8.5f), 7f, MgScenery.Hex(0xE7799B)) };
                icon.style.width = 16; icon.style.height = 16; icon.style.marginRight = 4; icon.style.flexShrink = 0;
                sb.Insert(0, icon);
                sb.name = "spawn-" + shape;
                sb.style.flexGrow = 1;
                sb.style.justifyContent = Justify.Center;
                sb.style.paddingLeft = 4; sb.style.paddingRight = 4;
                if (i < PurblePlace.Shapes.Length - 1) sb.style.marginRight = 4;
                spawnRow.Add(sb);
                _buttons["spawn-" + shape] = sb;
            }
            PlaceAt(spawnRow, PurblePlace.Cake.SpawnS, ColAPct, ColBPct + ColWPct - ColAPct);
            Field.Add(spawnRow);

            // bake clock: the label sits above the bar the canvas draws beside the oven tunnel
            _bakeLabel = Ux.Text(t.T("mg.purblePlace.oven.idle"), "gb-sm", "gb-bold", "gb-nowrap");
            _bakeLabel.name = "bake-label";
            _bakeLabel.pickingMode = PickingMode.Ignore;
            _bakeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            PlaceAt(_bakeLabel, OvenMidS - 0.24, ColAPct, ColBPct + ColWPct - ColAPct);
            Field.Add(_bakeLabel);

            // hold-to-drive belt buttons at the bottom
            _controls = Ux.Row();
            _controls.name = "belt-controls";
            Ux.Abs(_controls, 10, null, 10, 8);
            _controls.style.justifyContent = Justify.SpaceBetween;
            _controls.Add(HoldButton("▲", -1, "belt-back"));
            _controls.Add(HoldButton("▼", 1, "belt-fwd"));
            Field.Add(_controls);

            Banner(t.T("mg.purblePlace.intro"), PurblePlaceGame.IntroSec + 1.2);
        }

        private static double OvenMidS => (PurblePlace.Cake.OvenStartS + PurblePlace.Cake.OvenEndS) * 0.5;

        private Color StationColor(PurblePlace.Station st)
        {
            if (st.Kind == "teig" && PurblePlace.SpongeHex.TryGetValue(st.Value, out var hx)) return Hex(hx);
            if (st.Kind == "guss" && PurblePlace.IcingHex.TryGetValue(st.Value, out var gx)) return Hex(gx);
            if (st.Kind == "deko") return st.Value == "cherry" ? RedC : st.Value == "berries" ? MgScenery.Hex(0x7C6FD8) : MgScenery.Hex(0xFFD166);
            return CandleC;
        }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        private static Button StationButton(string label, Action onClick)
        {
            var b = new Button(onClick) { text = "" };
            b.ClearClassList();
            b.AddToClassList("gb-card");
            b.style.flexDirection = FlexDirection.Row;
            b.style.alignItems = Align.Center;
            // no vertical padding: the 3-px gold frame of the needed station must not squeeze the label
            b.style.paddingLeft = 6; b.style.paddingRight = 6; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.height = 32;
            b.style.overflow = Overflow.Hidden;
            b.Add(Ux.Text(label, "gb-sm", "gb-bold", "gb-nowrap"));
            return b;
        }

        private static VisualElement Dot(Color color)
        {
            var dot = Ux.El();
            dot.name = "station-dot";
            dot.style.width = 14; dot.style.height = 14; dot.style.marginRight = 5; dot.style.flexShrink = 0;
            dot.style.borderTopLeftRadius = 7; dot.style.borderTopRightRadius = 7; dot.style.borderBottomLeftRadius = 7; dot.style.borderBottomRightRadius = 7;
            dot.style.backgroundColor = color;
            dot.style.borderLeftWidth = 1; dot.style.borderRightWidth = 1; dot.style.borderTopWidth = 1; dot.style.borderBottomWidth = 1;
            var edge = new Color(0.45f, 0.35f, 0.3f, 0.35f);
            dot.style.borderLeftColor = edge; dot.style.borderRightColor = edge; dot.style.borderTopColor = edge; dot.style.borderBottomColor = edge;
            return dot;
        }

        private VisualElement HoldButton(string glyph, int dir, string name)
        {
            var b = Ux.El("gb-card");
            b.name = name;
            b.style.width = 120; b.style.height = 58;
            b.style.alignItems = Align.Center; b.style.justifyContent = Justify.Center;
            var l = Ux.Text(glyph, "gb-h2", "gb-bold");
            b.Add(l);
            b.RegisterCallback<PointerDownEvent>(e => { b.CapturePointer(e.pointerId); _game?.SetBelt(dir); b.AddToClassList("gb-arcade-card--pressed"); });
            b.RegisterCallback<PointerUpEvent>(e => { if (b.HasPointerCapture(e.pointerId)) b.ReleasePointer(e.pointerId); _game?.SetBelt(0); b.RemoveFromClassList("gb-arcade-card--pressed"); });
            b.RegisterCallback<PointerCaptureOutEvent>(_ => { _game?.SetBelt(0); b.RemoveFromClassList("gb-arcade-card--pressed"); });
            return b;
        }

        /// <summary>Absolute placement in % of the field at a belt position (the element's vertical centre sits on s).</summary>
        private static void PlaceAt(VisualElement e, double s, float leftPct, float widthPct)
        {
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(leftPct * 100f);
            e.style.width = Length.Percent(widthPct * 100f);
            e.style.top = Length.Percent((BeltTopPct + (BeltBottomPct - BeltTopPct) * (float)(s / PurblePlace.Cake.BeltLengthM)) * 100f);
            e.style.translate = new Translate(0, Length.Percent(-50));
        }

        protected override void TickGame(double dt)
        {
            _game.Tick(dt);
            _travel += _game.Line.BeltV * dt;
        }

        protected override void RenderGame()
        {
            var t = Host.Strings;
            var g = _game;
            var line = g.Line;
            TimeLabel.text = g.Tune.Endless
                ? t.T("mg.purblePlace.fails", ("n", line.Rejected + line.Expired), ("max", g.Tune.EndlessFailCount))
                : t.T("mg.game.time", ("sec", Mathf.CeilToInt((float)g.TimeLeft)));
            ScoreLabel.text = line.Score.ToString();
            ComboLabel.text = line.Combo >= 2 ? "×" + line.Combo : "";
            var key = 0;
            foreach (var tk in line.Tickets) key = key * 31 + tk.Id;
            if (key != _ticketKey) { _ticketKey = key; RebuildTickets(); }
            for (var i = 0; i < _ticketCards.Count && i < line.Tickets.Count; i++)
            {
                var bar = _ticketCards[i].Q("patience-fill");
                var frac = (float)(line.Tickets[i].Remain / line.Tickets[i].Patience);
                if (bar != null)
                {
                    bar.style.width = Length.Percent(Mathf.Clamp01(frac) * 100f);
                    bar.style.backgroundColor = frac > 0.5f ? GreenC : frac > 0.25f ? AmberC : RedC;
                }
            }
            SyncButtons(line);
            SyncBakeLabel(line);
            if (_stage != null) RenderStage();
            _canvas.MarkDirtyRepaint();
        }

        /// <summary>Gold frame on the needed station (plus the hatch once a baked pan stands in it), greyed-out buttons.</summary>
        private void SyncButtons(PurblePlace.Line line)
        {
            var need = PurblePlace.NeededStation(line);
            if (need == "spawn") need = line.Tickets.Count > 0 ? "spawn-" + line.Tickets[0].Spec.Shape : "";
            _need = need;
            var armed = PurblePlace.ShipArmed(line);
            _goldWanted.Clear();
            if (need.Length > 0) _goldWanted.Add(need);
            if (armed) _goldWanted.Add("ship");
            foreach (var id in _gold) if (!_goldWanted.Contains(id)) SetGold(id, false);
            foreach (var id in _goldWanted) if (!_gold.Contains(id)) SetGold(id, true);
            _gold.Clear();
            _gold.UnionWith(_goldWanted);
            if (_buttons.TryGetValue(need, out var nb)) nb.style.scale = new Scale(Vector3.one * (1f + 0.035f * Mathf.Sin((float)line.T * 6f)));

            foreach (var st in PurblePlace.Stations)
                if (st.Drop && _buttons.TryGetValue(st.Id, out var b))
                    SetLive(b, !(line.Lockouts.TryGetValue(st.Id, out var lo) && lo > 0.0));
            var canSpawn = PurblePlace.CanSpawn(line).ok;
            foreach (var shape in PurblePlace.Shapes) SetLive(_buttons["spawn-" + shape], canSpawn);
            SetLive(_buttons["ship"], armed);
        }

        private void SetGold(string id, bool on)
        {
            if (!_buttons.TryGetValue(id, out var b)) return;
            if (on)
            {
                b.style.borderTopWidth = 3; b.style.borderBottomWidth = 3; b.style.borderLeftWidth = 3; b.style.borderRightWidth = 3;
                b.style.borderTopColor = Gold; b.style.borderBottomColor = Gold; b.style.borderLeftColor = Gold; b.style.borderRightColor = Gold;
                b.style.backgroundColor = MgScenery.Hex(0xFFF6D8);
                return;
            }
            b.style.borderTopWidth = StyleKeyword.Null; b.style.borderBottomWidth = StyleKeyword.Null;
            b.style.borderLeftWidth = StyleKeyword.Null; b.style.borderRightWidth = StyleKeyword.Null;
            b.style.borderTopColor = StyleKeyword.Null; b.style.borderBottomColor = StyleKeyword.Null;
            b.style.borderLeftColor = StyleKeyword.Null; b.style.borderRightColor = StyleKeyword.Null;
            b.style.backgroundColor = StyleKeyword.Null;
            b.style.scale = StyleKeyword.Null;
        }

        private static void SetLive(VisualElement b, bool live)
        {
            if (b.enabledSelf == live) return;
            b.SetEnabled(live);
            b.style.opacity = live ? 1f : 0.45f;
        }

        /// <summary>The pan longest in the oven, or null.</summary>
        private static PurblePlace.Pan OvenPan(PurblePlace.Line line)
        {
            PurblePlace.Pan best = null;
            foreach (var pan in line.Pans)
                if (pan.InOven && (best == null || pan.BakeT > best.BakeT)) best = pan;
            return best;
        }

        private void SyncBakeLabel(PurblePlace.Line line)
        {
            var pan = OvenPan(line);
            var tune = line.Tune;
            var state = pan == null ? "idle"
                : pan.BakeT < tune.BakeGreenStartSec ? "pale"
                : pan.BakeT <= tune.BakeGreenEndSec ? "perfect"
                : pan.BakeT < tune.SingeSec ? "over" : "singed";
            if (state == _bakeState) return;
            _bakeState = state;
            _bakeLabel.text = Host.Strings.T("mg.purblePlace.oven." + state);
            _bakeLabel.style.color = state == "perfect" ? MgScenery.Hex(0x2F8F4E) : state == "over" ? MgScenery.Hex(0xC2780E)
                : state == "singed" ? MgScenery.Hex(0xC23B3B) : MgScenery.Hex(0x6B4F3A);
            if (state == "perfect") ScalePop(_bakeLabel, 1.15f);
        }

        private void RebuildTickets()
        {
            _board.Clear();
            _ticketCards.Clear();
            foreach (var tk in _game.Line.Tickets)
            {
                var card = Ux.El("gb-card");
                card.name = "ticket-" + tk.Id;
                card.style.width = Length.Percent(31.5f);
                card.style.height = Length.Percent(100);
                card.style.marginRight = Length.Percent(1.5f);
                card.style.paddingLeft = 4; card.style.paddingRight = 4; card.style.paddingTop = 4; card.style.paddingBottom = 6;
                card.pickingMode = PickingMode.Ignore;
                var spec = tk.Spec;
                BakeryCanvas art = null;
                art = new BakeryCanvas { Paint = p => PaintSpec(p, spec, art.contentRect.width, art.contentRect.height) };
                art.style.flexGrow = 1;
                card.Add(art);
                var barBg = Ux.El();
                barBg.style.height = 6; barBg.style.marginTop = 2; barBg.style.flexShrink = 0;
                barBg.style.backgroundColor = new Color(0.9f, 0.86f, 0.84f);
                barBg.style.borderTopLeftRadius = 3; barBg.style.borderTopRightRadius = 3; barBg.style.borderBottomLeftRadius = 3; barBg.style.borderBottomRightRadius = 3;
                var fill = Ux.El();
                fill.name = "patience-fill";
                fill.style.height = Length.Percent(100);
                fill.style.borderTopLeftRadius = 3; fill.style.borderTopRightRadius = 3; fill.style.borderBottomLeftRadius = 3; fill.style.borderBottomRightRadius = 3;
                barBg.Add(fill);
                card.Add(barBg);
                _board.Add(card);
                _ticketCards.Add(card);
                if (FieldW > 1f) ScalePop(card, 1.12f);
            }
        }

        // ── canvas: belt, stations, oven + bake clock, pans, drops, splats ──

        private float BeltX => FieldW * BeltXPct;
        private float BeltHalfW => FieldW * 0.11f;
        private float YOf(double s) => FieldH * (BeltTopPct + (BeltBottomPct - BeltTopPct) * (float)(s / PurblePlace.Cake.BeltLengthM));

        private void Paint(Painter2D p)
        {
            if (_game == null || FieldW <= 1f) return;
            var line = _game.Line;
            var tune = _game.Tune;
            var top = YOf(0.0) - 14f;
            var bottom = YOf(tune.BeltLengthM) + 14f;
            var needSt = PurblePlace.StationById(_need);
            // leader lines from the belt edge to each nozzle button (the needed one in gold)
            foreach (var st in PurblePlace.Stations)
            {
                if (!st.Drop || !_columnOf.TryGetValue(st.Id, out var col)) continue;
                var y = YOf(st.S);
                var needed = needSt != null && st.Id == needSt.Id;
                var c = needed ? Gold : new Color(0.55f, 0.45f, 0.5f, 0.28f);
                var h = needed ? 1.5f : 1f;
                Rect(p, BeltX + BeltHalfW + 2f, y - h, FieldW * col - 2f, y + h, c);
            }
            if (_stage != null)
            {
                // the workshop is 3D: the canvas keeps the catch window on the belt and the bake clock
                if (needSt != null && needSt.Drop)
                {
                    var c0 = YOf(needSt.S - tune.CatchHalfM); var c1 = YOf(needSt.S + tune.CatchHalfM);
                    Rect(p, BeltX - BeltHalfW, c0, BeltX + BeltHalfW, c1, new Color(1f, 0.82f, 0.3f, 0.22f));
                    Rect(p, BeltX - BeltHalfW, c0, BeltX + BeltHalfW, c0 + 2f, new Color(0.95f, 0.71f, 0.19f, 0.85f));
                    Rect(p, BeltX - BeltHalfW, c1 - 2f, BeltX + BeltHalfW, c1, new Color(0.95f, 0.71f, 0.19f, 0.85f));
                }
                PaintBakeClock(p, tune, OvenPan(line), YOf(OvenMidS) + 8f);
                return;
            }
            // belt: frame, moving slats, rollers
            Rect(p, BeltX - BeltHalfW - 6f, top, BeltX + BeltHalfW + 6f, bottom, RollerC);
            Rect(p, BeltX - BeltHalfW, top, BeltX + BeltHalfW, bottom, BeltC);
            for (var y = top + (float)Mod(_travel * (YOf(1.0) - YOf(0.0)), 22.0); y < bottom; y += 22f)
                Rect(p, BeltX - BeltHalfW, y, BeltX + BeltHalfW, y + 3f, BeltDark);
            // catch window of the needed nozzle: the pan has to stand inside this band when the drop lands
            if (needSt != null && needSt.Drop)
            {
                var y0 = YOf(needSt.S - tune.CatchHalfM); var y1 = YOf(needSt.S + tune.CatchHalfM);
                Rect(p, BeltX - BeltHalfW, y0, BeltX + BeltHalfW, y1, new Color(1f, 0.82f, 0.3f, 0.26f));
                Rect(p, BeltX - BeltHalfW, y0, BeltX + BeltHalfW, y0 + 2f, new Color(0.95f, 0.71f, 0.19f, 0.85f));
                Rect(p, BeltX - BeltHalfW, y1 - 2f, BeltX + BeltHalfW, y1, new Color(0.95f, 0.71f, 0.19f, 0.85f));
            }
            // oven tunnel
            var oy0 = YOf(tune.OvenStartS); var oy1 = YOf(tune.OvenEndS);
            Rect(p, BeltX - BeltHalfW - 14f, oy0, BeltX + BeltHalfW + 14f, oy1, OvenC);
            Rect(p, BeltX - BeltHalfW - 14f, oy0, BeltX + BeltHalfW + 14f, oy0 + 6f, OvenDark);
            Rect(p, BeltX - BeltHalfW - 14f, oy1 - 6f, BeltX + BeltHalfW + 14f, oy1, OvenDark);
            Rect(p, BeltX - BeltHalfW + 4f, oy0 + 10f, BeltX + BeltHalfW - 4f, oy1 - 10f, new Color(0.25f, 0.15f, 0.12f, 0.55f));
            var ovenPan = OvenPan(line);
            if (ovenPan != null) Rect(p, BeltX - BeltHalfW + 4f, oy0 + 10f, BeltX + BeltHalfW - 4f, oy1 - 10f, GlowC);
            PaintBakeClock(p, tune, ovenPan, YOf(OvenMidS) + 8f);
            // nozzles to the left of the belt
            foreach (var st in PurblePlace.Stations)
            {
                if (!st.Drop) continue;
                var y = YOf(st.S);
                var c = StationColor(st);
                var mouth = new Vector2(BeltX - BeltHalfW - 30f, y);
                Rect(p, BeltX - BeltHalfW - 34f, y - 5f, BeltX - BeltHalfW + 8f, y + 5f, MgScenery.Hex(0xE9D7C8));
                if (needSt != null && st.Id == needSt.Id) Ellipse(p, mouth, 13f, 13f, Gold);
                Ellipse(p, mouth, 9f, 9f, c);
                var locked = line.Lockouts.TryGetValue(st.Id, out var lo) && lo > 0.0;
                if (locked) Ellipse(p, mouth, 4f, 4f, Color.white);
            }
            // shipping hatch
            var hy = YOf(tune.ShipS);
            var hh = (float)(tune.ShipHalfM * (YOf(1) - YOf(0)));
            Rect(p, BeltX - BeltHalfW - 4f, hy - hh, BeltX + BeltHalfW + 4f, hy + hh, new Color(0.55f, 0.85f, 0.65f, 0.35f));
            // splats
            foreach (var sp in line.Splats)
            {
                var a = Mathf.Clamp01((float)(sp.Ttl / tune.SplatTtlSec));
                Ellipse(p, new Vector2(BeltX + 6f, YOf(sp.S)), 16f, 7f, new Color(0.85f, 0.6f, 0.45f, 0.55f * a));
            }
            // pans
            foreach (var pan in line.Pans) PaintPan(p, pan, new Vector2(BeltX, YOf(pan.S)), BeltHalfW * 0.82f);
            // falling drops: from the nozzle mouth towards the belt centre
            foreach (var d in line.Drops)
            {
                var u = Mathf.Clamp01((float)((line.T - d.FiredAt) / tune.FallSec));
                var y = YOf(d.NozzleS);
                var from = new Vector2(BeltX - BeltHalfW - 22f, y);
                var to = new Vector2(BeltX, y);
                var at = Vector2.Lerp(from, to, u) + new Vector2(0f, -14f * Mathf.Sin(u * Mathf.PI));
                var st = PurblePlace.StationById(d.Station);
                Ellipse(p, at, 7f, 7f, st != null ? StationColor(st) : Color.white);
            }
        }

        /// <summary>Bar beside the oven: pale → golden window → over → singed, with a marker at the bake time.</summary>
        private void PaintBakeClock(Painter2D p, PurblePlace.Tune tune, PurblePlace.Pan pan, float y)
        {
            var x0 = FieldW * ColAPct; var x1 = FieldW * (ColBPct + ColWPct);
            var max = tune.SingeSec + 0.6;
            float X(double sec) => x0 + (x1 - x0) * Mathf.Clamp01((float)(sec / max));
            var a = pan != null ? 1f : 0.45f;
            Rect(p, x0 - 2f, y - 8f, x1 + 2f, y + 8f, new Color(0.42f, 0.31f, 0.23f, 0.35f * a));
            Rect(p, x0, y - 6f, X(tune.BakeGreenStartSec), y + 6f, Fade(PaleC, a));
            Rect(p, X(tune.BakeGreenStartSec), y - 6f, X(tune.BakeGreenEndSec), y + 6f, Fade(GreenC, a));
            Rect(p, X(tune.BakeGreenEndSec), y - 6f, X(tune.SingeSec), y + 6f, Fade(AmberC, a));
            Rect(p, X(tune.SingeSec), y - 6f, x1, y + 6f, Fade(RedC, a));
            if (pan == null) return;
            var mx = X(pan.BakeT);
            Rect(p, mx - 3.5f, y - 12f, mx + 3.5f, y + 12f, new Color(0.3f, 0.2f, 0.16f, 0.9f));
            Rect(p, mx - 2f, y - 10.5f, mx + 2f, y + 10.5f, Color.white);
        }

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        private void PaintPan(Painter2D p, PurblePlace.Pan pan, Vector2 c, float r)
        {
            Ellipse(p, c + new Vector2(3f, 4f), r * 1.05f, r * 0.95f, Shadow);
            ShapeFill(p, pan.Shape, c, r, PanDark);
            ShapeFill(p, pan.Shape, c, r * 0.9f, PanC);
            if (pan.Sponge != null)
            {
                var sponge = Hex(PurblePlace.SpongeHex[pan.Sponge]);
                if (pan.Bake == "perfect") sponge = Color.Lerp(sponge, MgScenery.Hex(0xE0A050), 0.25f);
                else if (pan.Bake == "over") sponge = Color.Lerp(sponge, MgScenery.Hex(0x9A6030), 0.35f);
                else if (pan.Bake == "singed") sponge = Color.Lerp(sponge, MgScenery.Hex(0x3A2A20), 0.7f);
                else if (pan.InOven) sponge = Color.Lerp(sponge, MgScenery.Hex(0xE0A050), Mathf.Clamp01((float)pan.BakeT / 3f) * 0.3f);
                ShapeFill(p, pan.Shape, c, r * 0.78f, sponge);
            }
            if (pan.Icing != null) ShapeFill(p, pan.Shape, c + new Vector2(0f, -2f), r * 0.62f, Hex(PurblePlace.IcingHex[pan.Icing]));
            if (pan.Topping != null) PaintTopping(p, pan.Topping, c + new Vector2(0f, -3f), r);
            for (var i = 0; i < pan.Candles; i++)
            {
                var x = c.x - r * 0.45f + i * r * 0.3f;
                Rect(p, x - 2f, c.y - r * 0.9f, x + 2f, c.y - r * 0.35f, CandleC);
                Ellipse(p, new Vector2(x, c.y - r * 0.98f), 2.6f, 4f, FlameC);
            }
            if (pan.Bake == "perfect") { p.strokeColor = new Color(1f, 0.85f, 0.3f, 0.8f); p.lineWidth = 2f; EllipseStroke(p, c, r * 1.15f, r * 1.15f); }
        }

        private static void PaintTopping(Painter2D p, string topping, Vector2 c, float r)
        {
            var tc = topping == "cherry" ? RedC : topping == "berries" ? MgScenery.Hex(0x7C6FD8) : MgScenery.Hex(0xFFD166);
            var n = topping == "cherry" ? 1 : 5;
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2f / n + 0.4f;
                var off = n == 1 ? Vector2.zero : new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.32f;
                var rr = n == 1 ? r * 0.18f : r * 0.08f;
                Ellipse(p, c + off, rr, rr, tc);
            }
        }

        /// <summary>Order card art: the wanted cake on a plate, scaled to the card.</summary>
        private void PaintSpec(Painter2D p, PurblePlace.Spec spec, float w, float h)
        {
            if (w <= 1f || h <= 1f) return;
            var r = Mathf.Min(w * 0.3f, h * 0.3f);
            var c = new Vector2(w * 0.5f, h * 0.58f);
            Ellipse(p, c + new Vector2(0f, r * 0.9f), r * 1.35f, r * 0.34f, PlateC);
            var sponge = Hex(PurblePlace.SpongeHex[spec.Sponge]);
            ShapeFill(p, spec.Shape, c, r * 1.08f, Color.Lerp(sponge, MgScenery.Hex(0x6B4F3A), 0.35f));
            ShapeFill(p, spec.Shape, c, r, sponge);
            if (spec.Icing != "none") ShapeFill(p, spec.Shape, c + new Vector2(0f, -1.5f), r * 0.78f, Hex(PurblePlace.IcingHex[spec.Icing]));
            if (spec.Topping != "none") PaintTopping(p, spec.Topping, c + new Vector2(0f, -2f), r);
            for (var i = 0; i < spec.Candles; i++)
            {
                var x = c.x - r * 0.5f + i * r * 0.34f;
                Rect(p, x - 1.6f, c.y - r * 1.2f, x + 1.6f, c.y - r * 0.55f, CandleC);
                Rect(p, x - 1.6f, c.y - r * 1.2f, x - 0.4f, c.y - r * 0.55f, new Color(0.85f, 0.7f, 0.45f, 0.6f));
                Ellipse(p, new Vector2(x, c.y - r * 1.3f), 2.2f, 3.2f, FlameC);
            }
        }

        private static void ShapeFill(Painter2D p, string shape, Vector2 c, float r, Color color)
        {
            if (shape == "square") { Rect(p, c.x - r * 0.85f, c.y - r * 0.85f, c.x + r * 0.85f, c.y + r * 0.85f, color); return; }
            if (shape == "heart")
            {
                Ellipse(p, c + new Vector2(-r * 0.42f, -r * 0.25f), r * 0.52f, r * 0.52f, color);
                Ellipse(p, c + new Vector2(r * 0.42f, -r * 0.25f), r * 0.52f, r * 0.52f, color);
                p.fillColor = color;
                p.BeginPath();
                p.MoveTo(c + new Vector2(-r * 0.92f, -r * 0.1f));
                p.LineTo(c + new Vector2(r * 0.92f, -r * 0.1f));
                p.LineTo(c + new Vector2(0f, r * 0.95f));
                p.ClosePath(); p.Fill();
                return;
            }
            Ellipse(p, c, r, r, color);
        }

        private static void Rect(Painter2D p, float x0, float y0, float x1, float y1, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x0, y1));
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
            for (var i = 0; i <= 16; i++)
            {
                var a = Mathf.PI * 2f * i / 16f;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath(); p.Stroke();
        }

        private static double Mod(double v, double m)
        {
            var r = v % m;
            return r < 0.0 ? r + m : r;
        }

        // ── juice (purble_place.gd _handle_event — no shake) ──

        private Vector2 PanAt(int panId)
        {
            foreach (var pan in _game.Line.Pans) if (pan.Id == panId) return new Vector2(BeltX, YOf(pan.S));
            return new Vector2(BeltX, YOf(_game.Tune.ShipS));
        }

        private void OnEvent(PurblePlace.Ev ev)
        {
            var t = Host.Strings;
            switch (ev.Type)
            {
                case "catch":
                    PlaySfx("game_pop", 2f);
                    Haptics.Tap();
                    if (FieldW > 1f) Burst(PanAt(ev.PanId), Color.white, 8);
                    break;
                case "splat":
                    PlaySfx("game_miss", -2f, 0.8f);
                    if (FieldW > 1f) { Burst(new Vector2(BeltX, YOf(ev.S)), new Color(0.85f, 0.6f, 0.45f), 10); Float(new Vector2(BeltX + 40f, YOf(ev.S)), ev.Points.ToString(), new Color(1f, 0.42f, 0.42f), 22f); }
                    break;
                case "bakeCommit":
                    if (ev.Result == "perfect") { Banner(t.T("mg.purblePlace.baked"), 1.2); PlaySfx("game_star"); if (FieldW > 1f) Burst(PanAt(ev.PanId), new Color(1f, 0.8f, 0.35f), 16); }
                    else if (ev.Result == "singed") { Banner(t.T("mg.purblePlace.singed"), 1.2); PlaySfx("game_lose", 0f, 0.7f); }
                    break;
                case "serve":
                    if (ev.Outcome == "perfect") { Banner(t.T("mg.purblePlace.perfect", ("n", ev.Points)), 1.6); PlaySfx("game_win"); ConfettiRain(50); }
                    else { Banner(t.T("mg.purblePlace.served", ("n", ev.Points)), 1.4); PlaySfx("game_hit"); }
                    Haptics.Tap();
                    if (FieldW > 1f) Float(new Vector2(BeltX + 50f, YOf(_game.Tune.ShipS)), "+" + ev.Points, new Color(1f, 0.82f, 0.35f), 30f);
                    StageServed(ev.Outcome == "perfect");
                    break;
                case "reject":
                    Banner(t.T("mg.purblePlace.rejected"), 1.4);
                    PlaySfx("game_miss");
                    Haptics.Warn();
                    Flash(new Color(0.9f, 0.32f, 0.22f, 0.14f), 0.16f);
                    break;
                case "expire":
                    Banner(t.T("mg.purblePlace.expired"), 1.4);
                    PlaySfx("game_lose", -3f, 0.7f);
                    break;
                case "buzz":
                    PlaySfx("soft_error", 0f, 0.7f);
                    break;
                case "ticketNew":
                    PlaySfx("soft_toast", 3f, 0.8f);
                    break;
                case "panSpawn":
                    PlaySfx("soft_chip", 0f, 0.8f);
                    break;
            }
        }

        protected override int TakeFinalScore()
        {
            var score = _game?.FinalScore ?? 0;
            _game = null;
            DropStage();
            return score;
        }

        protected override void DropGame()
        {
            _game = null;
            DropStage();
        }

        private sealed class BakeryCanvas : VisualElement
        {
            public Action<Painter2D> Paint;
            public BakeryCanvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
