using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Gestalten" for the room Gooby is in (port of customize_screen.gd, inside part): a panel on the right with
    /// "Tapeten & Wände" / "Böden"; every option shows its real pattern in the chosen colour, the colour dots below try
    /// the look on the room at once (nothing is saved until "Übernehmen"), paid options are bought once and then work in
    /// every room in all their colours, and "Zurücksetzen" brings back the room's built look.
    /// </summary>
    public sealed class CustomizeScreen : UiScreen
    {
        private readonly GameHost _host;
        private string _art = HouseStyle.Wand;
        private HouseStyle.RoomStyle _draft;
        private VisualElement _cards, _swatches, _actions;
        private Label _swatchLabel;
        private CounterPanel.Header _head;
        private readonly Juice _juice = new Juice();

        public CustomizeScreen(GameHost host)
        {
            _host = host;
            Id = "gestalten";
        }

        private string RoomId => _host.World?.RoomId ?? "living";
        private RoomView Room => _host.World?.Room;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            root.pickingMode = PickingMode.Ignore;
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.name = "gestalten-sheet";
            sheet.style.left = Length.Percent(50); sheet.style.right = 16;
            sheet.style.top = 14; sheet.style.bottom = 0;
            sheet.style.borderBottomLeftRadius = 0; sheet.style.borderBottomRightRadius = 0;
            if (_host.World != null && _host.World.InGarden) _art = Haus;
            _head = CounterPanel.Head("palette", new Color(1f, 0.9f, 0.93f), t.T("customize.title"), t.T(_art == Haus ? "customize.bereich.haus" : "customize.bereich.innen"), Close, J.Int(J.Path(_host.Store.State, "economy", "coins")));
            sheet.Add(_head.Row);
            var body = Ux.Col("gb-win-body");
            var tabs = Ux.Row("gb-win-tabs");
            foreach (var (art, key) in _art == Haus ? new[] { (Haus, "customize.bereich.haus") } : new[] { (HouseStyle.Wand, "customize.kategorie.wand"), (HouseStyle.Boden, "customize.kategorie.boden") })
            {
                var a = art;
                var chip = Ux.Btn(t.T(key), () => { _art = a; Refresh(); }, "gb-chip");
                chip.name = "gestalten-tab-" + a;
                tabs.Add(chip);
            }
            body.Add(tabs);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("gb-win-scroll");
            _cards = Ux.El("gb-win-grid");
            scroll.Add(_cards);
            body.Add(scroll);
            _swatchLabel = Ux.Text(t.T("customize.farbe_titel"), "gb-bold", "gb-gestalten-label");
            body.Add(_swatchLabel);
            _swatches = Ux.Row("gb-gestalten-swatches");
            body.Add(_swatches);
            _actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            body.Add(_actions);
            sheet.Add(body);
            root.Add(sheet);
            root.Add(_juice.Layer);
            _draft = HouseStyle.Room(_host.Store, RoomId) ?? new HouseStyle.RoomStyle { Wand = "uni", WandFarbe = "creme", Boden = "dielen_hell", BodenFarbe = "eiche" };
            _host.Store.CoinsChanged += OnCoins;
            Refresh();
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            _host.Store.CoinsChanged -= OnCoins;
            // unsaved try-ons go back to what is saved
            Room?.ApplyStyle(_host.Store);
        }

        private void OnCoins(int coins) => _head.Coins.text = Ux.FormatNumber(coins);

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            _host.Ui.Pop();
        }

        private const string Haus = "haus";

        private string Current => _art == HouseStyle.Wand ? _draft.Wand : _draft.Boden;
        private string CurrentColor => _art == HouseStyle.Wand ? _draft.WandFarbe : _draft.BodenFarbe;

        private void Refresh()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            foreach (var chip in _head.Row.parent.Query<Button>(className: "gb-chip").ToList())
                chip.EnableInClassList("gb-chip--active", chip.name == "gestalten-tab-" + _art);
            _cards.Clear();
            if (_art == Haus) { RefreshHaus(); return; }
            _swatchLabel.style.display = DisplayStyle.Flex;
            _swatches.style.display = DisplayStyle.Flex;
            foreach (var opt in HouseStyle.OfArt(_art))
            {
                var o = opt;
                var owned = HouseStyle.Owns(_host.Store, _art, opt.Id);
                var color = opt.Id == Current ? CurrentColor : opt.Farben[0];
                var card = Ux.Col("gb-gestalten-card");
                card.name = "gestalten-" + opt.Id;
                if (opt.Id == Current) card.AddToClassList("gb-gestalten-card--on");
                var tile = Ux.El("gb-gestalten-tile");
                tile.style.backgroundImage = new StyleBackground(SurfaceTextures.Get(opt.Muster, color));
                card.Add(tile);
                card.Add(Ux.Text(opt.Name(locale), "gb-gestalten-name"));
                card.Add(Ux.Text(owned ? t.T("customize.im_besitz") : t.T("customize.preis", ("n", opt.Preis)), owned ? "gb-gestalten-owned" : "gb-gestalten-price"));
                card.RegisterCallback<ClickEvent>(_ => Pick(o.Id, o.Id == Current ? CurrentColor : o.Farben[0]));
                _cards.Add(card);
            }
            _swatches.Clear();
            var sel = HouseStyle.Get(_art, Current);
            if (sel != null)
                foreach (var farbe in sel.Farben)
                {
                    var f = farbe;
                    var dot = Ux.El("gb-gestalten-swatch");
                    dot.name = "gestalten-farbe-" + farbe;
                    dot.style.backgroundColor = ArtLibrary.Hex(HouseStyle.Palette[farbe]);
                    if (farbe == CurrentColor) dot.AddToClassList("gb-gestalten-swatch--on");
                    dot.tooltip = t.T("customize.farbe." + farbe);
                    dot.RegisterCallback<ClickEvent>(_ => Pick(sel.Id, f));
                    _swatches.Add(dot);
                }
            _actions.Clear();
            var reset = Ux.IconTextBtn("undo", t.T("customize.reset"), ResetRoom, "gb-btn-secondary", "gb-btn-sm");
            reset.name = "gestalten-reset";
            _actions.Add(reset);
            if (sel == null) return;
            if (HouseStyle.Owns(_host.Store, _art, sel.Id))
            {
                var apply = Ux.IconTextBtn("check", t.T("nl.gestalten.uebernehmen"), Save, "gb-btn-confirm", "gb-btn-sm");
                apply.name = "gestalten-save";
                _actions.Add(apply);
            }
            else
            {
                var buy = Ux.IconTextBtn("coin", t.T("customize.kaufen") + " · " + Ux.FormatNumber(sel.Preis) + " ᴳ", Buy, "gb-btn-primary", "gb-btn-sm");
                buy.name = "gestalten-buy";
                buy.SetEnabled(J.Int(J.Path(_host.Store.State, "economy", "coins")) >= sel.Preis);
                _actions.Add(buy);
            }
        }

        /// <summary>The cottage: one row of colour dots per part, painted at once (free picks).</summary>
        /// <summary>
        /// One outside part: its options as chips (a price until bought — the tap buys and puts it up), the colour dots of
        /// the chosen one, and for the house number its −/+ stepper. Parts never chosen keep the garden's built look, so
        /// no chip lights up for them yet (the plaque and the canopy are always there).
        /// </summary>
        private VisualElement TeilRow(string art)
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            var row = Ux.Col("gb-gestalten-hausrow");
            row.name = "teil-" + art;
            row.Add(Ux.Text(t.T("customize.kategorie." + (art == HouseStyle.Grund ? "grund_boden" : art)), "gb-bold"));
            var (cur, curFarbe) = HouseStyle.TeilWahl(_host.Store, art);
            var sichtbar = art == HouseStyle.Hausnummer || art == HouseStyle.Vordach || HouseStyle.TeilGewaehlt(_host.Store, art);
            var chips = Ux.Row("gb-gestalten-teilchips");
            foreach (var opt in HouseStyle.TeilOptionen(art))
            {
                var o = opt;
                var owned = HouseStyle.OwnsTeil(_host.Store, art, o.Id);
                var chip = Ux.Btn(o.Name(locale) + (owned ? "" : " · " + t.T("customize.preis", ("n", o.Preis))), () => PickTeil(art, o), "gb-chip");
                chip.name = "teil-" + art + "-" + o.Id;
                chip.EnableInClassList("gb-chip--active", sichtbar && o.Id == cur);
                chips.Add(chip);
            }
            row.Add(chips);
            var current = HouseStyle.TeilOption(art, cur);
            if (sichtbar && current != null && current.Farben.Count > 0)
            {
                var dots = Ux.Row("gb-gestalten-swatches");
                foreach (var farbe in current.Farben)
                {
                    var f = farbe;
                    var dot = Ux.El("gb-gestalten-swatch");
                    dot.name = "teil-" + art + "-farbe-" + f;
                    dot.style.backgroundColor = ArtLibrary.Hex(HouseStyle.Palette[f]);
                    if (f == curFarbe) dot.AddToClassList("gb-gestalten-swatch--on");
                    dot.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (!HouseStyle.SetTeil(_host.Store, art, cur, f)) return;
                        _host.World?.ApplyHouseStyle();
                        Sfx.PlayUi("soft_chip");
                        Refresh();
                    });
                    dots.Add(dot);
                }
                row.Add(dots);
            }
            if (art == HouseStyle.Hausnummer)
            {
                var stepper = Ux.Row("gb-gestalten-nummer");
                var zahl = HouseStyle.HausnummerZahl(_host.Store);
                void Set(int n)
                {
                    HouseStyle.SetHausnummer(_host.Store, n);
                    _host.World?.ApplyHouseStyle();
                    Sfx.PlayUi("soft_tick");
                    Refresh();
                }
                var minus = Ux.Btn("−", () => Set(zahl - 1), "gb-btn-secondary", "gb-btn-sm");
                minus.name = "hausnummer-minus";
                stepper.Add(minus);
                stepper.Add(Ux.Text(t.T("customize.hausnummer_zahl", ("n", zahl)), "gb-md", "gb-bold", "gb-gestalten-nummerzahl"));
                var plus = Ux.Btn("+", () => Set(zahl + 1), "gb-btn-secondary", "gb-btn-sm");
                plus.name = "hausnummer-plus";
                stepper.Add(plus);
                row.Add(stepper);
            }
            return row;
        }

        private void PickTeil(string art, HouseStyle.Option opt)
        {
            var t = _host.Strings;
            if (!HouseStyle.OwnsTeil(_host.Store, art, opt.Id))
            {
                var result = HouseStyle.BuyTeil(_host.Store, art, opt.Id);
                if (result != HouseStyle.ResultOk)
                {
                    Sfx.PlayUi("soft_error");
                    _host.Ui.Toast(t.T(result == HouseStyle.ResultBroke ? "customize.zu_teuer" : "customize.fehler"), "close");
                    return;
                }
                Sfx.PlayUi("soft_buy");
                _host.Ui.Toast(t.T("customize.gekauft_toast", ("name", opt.Name(_host.Settings.Locale))), "palette");
            }
            var (cur, curFarbe) = HouseStyle.TeilWahl(_host.Store, art);
            var farbe = opt.Farben.Count == 0 ? "" : opt.Id == cur && opt.Farben.Contains(curFarbe) ? curFarbe : opt.Farben[0];
            if (!HouseStyle.SetTeil(_host.Store, art, opt.Id, farbe)) return;
            _host.World?.ApplyHouseStyle();
            Sfx.PlayUi("soft_confirm");
            Debug.Log("[gestalten] " + art + "=" + opt.Id + (farbe.Length > 0 ? "/" + farbe : ""));
            Refresh();
        }

        /// <summary>QA: "gestalten teil zaun metall [farbe]" buys/puts up a part; "gestalten nummer 12".</summary>
        public void QaTeil(string art, string id, string farbe)
        {
            var opt = HouseStyle.TeilOption(art, id);
            if (opt == null) { Debug.LogWarning("[qa] gestalten: no " + art + "/" + id); return; }
            PickTeil(art, opt);
            if (!string.IsNullOrEmpty(farbe) && HouseStyle.SetTeil(_host.Store, art, id, farbe)) { _host.World?.ApplyHouseStyle(); Refresh(); }
        }

        public void QaNummer(int n)
        {
            HouseStyle.SetHausnummer(_host.Store, n);
            _host.World?.ApplyHouseStyle();
            Refresh();
        }

        private void RefreshHaus()
        {
            var t = _host.Strings;
            _swatchLabel.style.display = DisplayStyle.None;
            _swatches.style.display = DisplayStyle.None;
            var haus = HouseStyle.Haus(_host.Store);
            foreach (var (key, section) in HouseStyle.HausFarben)
            {
                var row = Ux.Col("gb-gestalten-hausrow");
                row.Add(Ux.Text(t.T("customize.kategorie." + section), "gb-bold"));
                var dots = Ux.Row("gb-gestalten-swatches");
                foreach (var farbe in HouseStyle.FarbenFuer(section))
                {
                    var k = key;
                    var f = farbe;
                    var dot = Ux.El("gb-gestalten-swatch");
                    dot.name = "haus-" + key + "-" + farbe;
                    dot.style.backgroundColor = ArtLibrary.Hex(HouseStyle.Palette[farbe]);
                    if (haus != null && haus.TryGetValue(key, out var cur) && cur == farbe) dot.AddToClassList("gb-gestalten-swatch--on");
                    dot.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (!HouseStyle.SetHaus(_host.Store, k, f)) return;
                        _host.World?.ApplyHouseStyle();
                        Sfx.PlayUi("soft_chip");
                        Debug.Log("[gestalten] haus " + k + "=" + f);
                        Refresh();
                    });
                    dots.Add(dot);
                }
                row.Add(dots);
                _cards.Add(row);
            }
            foreach (var (art, _, _, _) in HouseStyle.Teile) _cards.Add(TeilRow(art));
            _actions.Clear();
            var reset = Ux.IconTextBtn("undo", t.T("customize.reset"), () =>
            {
                HouseStyle.ResetHaus(_host.Store);
                _host.World?.ApplyHouseStyle();
                Sfx.PlayUi("soft_back");
                Refresh();
            }, "gb-btn-secondary", "gb-btn-sm");
            reset.name = "gestalten-reset";
            _actions.Add(reset);
        }

        private void Pick(string id, string farbe)
        {
            if (_art == HouseStyle.Wand) { _draft.Wand = id; _draft.WandFarbe = farbe; }
            else { _draft.Boden = id; _draft.BodenFarbe = farbe; }
            Room?.ShowStyle(_draft);
            Sfx.PlayUi("soft_chip");
            Refresh();
        }

        private void Buy()
        {
            var t = _host.Strings;
            var sel = HouseStyle.Get(_art, Current);
            if (sel == null) return;
            var result = HouseStyle.Buy(_host.Store, _art, sel.Id);
            if (result != HouseStyle.ResultOk)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(t.T(result == HouseStyle.ResultBroke ? "customize.zu_teuer" : "customize.fehler"), "coin");
                return;
            }
            Sfx.Play("soft_buy");
            _host.Ui.Toast(t.T("customize.gekauft_toast", ("name", sel.Name(_host.Settings.Locale))), "palette");
            Save();
        }

        private void Save()
        {
            var a = HouseStyle.Apply(_host.Store, RoomId, HouseStyle.Wand, _draft.Wand, _draft.WandFarbe);
            var b = HouseStyle.Apply(_host.Store, RoomId, HouseStyle.Boden, _draft.Boden, _draft.BodenFarbe);
            if (!a || !b)
            {
                // one side is still an unbought try-on: save what is owned, the rest goes back
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(_host.Strings.T("nl.gestalten.noch_kaufen"), "coin");
            }
            else
            {
                Sfx.Play("soft_sticker");
                _host.Ui.Confetti(_host.Settings.ReducedMotion ? 8 : 30);
                _host.Ui.Toast(_host.Strings.T("nl.gestalten.gespeichert"), "palette");
            }
            Room?.ApplyStyle(_host.Store);
            Debug.Log("[gestalten] " + RoomId + " wand=" + _draft.Wand + "/" + _draft.WandFarbe + " boden=" + _draft.Boden + "/" + _draft.BodenFarbe + " ok=" + (a && b));
            Refresh();
        }

        private void ResetRoom()
        {
            HouseStyle.Reset(_host.Store, RoomId);
            _draft = new HouseStyle.RoomStyle { Wand = "uni", WandFarbe = "creme", Boden = "dielen_hell", BodenFarbe = "eiche" };
            Room?.ApplyStyle(_host.Store);
            Sfx.PlayUi("soft_back");
            Refresh();
        }

        public override void Update() => _juice.Tick(Time.unscaledDeltaTime);
    }
}
