using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Core.Cosmetics;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The player's own market stall (port of markt_stand_sheet.gd): a panel on the right while the camera looks at the
    /// stall on the square, so every ware put out lands in a crate on the counter. On top the stall's state and the day's
    /// favourite ("Heute lieben alle Kürbisse!"), then the wares on the stall — each with its price, a −/+ stepper in 5 %
    /// steps (±50 % around the base value) and "Runter" — and the stores below (harvest, workshop pieces, souvenirs) with
    /// "+1" / "Alle". On market day (Saturday 8–14, or once it is over) "Zuschauen" plays the day with customers at the
    /// stall (<see cref="MarktTagScreen"/>) and "Abrechnung holen" settles it at once. Herb boxes deliver their bundles
    /// when the panel opens.
    /// </summary>
    public sealed class MarktStandScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _body;
        private CounterPanel.Header _head;
        private readonly Juice _juice = new Juice();
        private int _kraeuter;
        private bool _toMarketDay;

        public MarktStandScreen(GameHost host)
        {
            _host = host;
            Id = "markt-stand";
        }

        private long Now => _host.Store.Clock.NowMs();
        private int Bias => _host.Store.Clock.UtcOffsetMinutes(Now);
        private string Status => MarktStand.Status(_host.Store, Now, Bias);

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            root.pickingMode = PickingMode.Ignore;
            var sheet = Ux.El("gb-sheet", "gb-win-sheet", "gb-stand-sheet");
            sheet.name = "stand-sheet";
            sheet.style.left = Length.Percent(46); sheet.style.right = 16;
            sheet.style.top = 14; sheet.style.bottom = 0;
            sheet.style.borderBottomLeftRadius = 0; sheet.style.borderBottomRightRadius = 0;
            _head = CounterPanel.Head("shop", new Color(1f, 0.92f, 0.62f), t.T("markt.stand.titel"), "", Close, ShopLogic.Coins(_host.Store));
            sheet.Add(_head.Row);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _body = Ux.Col("gb-win-body", "gb-stand-body");
            scroll.Add(_body);
            sheet.Add(scroll);
            root.Add(sheet);
            root.Add(_juice.Layer);
            _kraeuter = KraeuterKasten.Schoepfe(_host.Store, Now / 1000);
            _host.Store.CoinsChanged += OnCoins;
            Refresh();
        }

        public override void OnShown()
        {
            var interior = _host.World?.Interior;
            if (interior == null || !interior.HasOwnStall) return;
            _host.World.GlideCamera(interior.StallCameraPosition, interior.StallCameraRotation, 0.9f);
            _ = interior.ShowStallGoobyAsync(CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"])));
            interior.SetStallWares(MarktStand.SliceVon(_host.Store).Slots);
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            _host.Store.CoinsChanged -= OnCoins;
            if (!_toMarketDay) _host.World?.GlideToDoorView(0.8f);
        }

        private void OnCoins(int coins) { if (_head.Coins != null) _head.Coins.text = Ux.FormatNumber(coins); }

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            _host.Ui.Pop();
        }

        private string WareName(string id)
        {
            var t = _host.Strings;
            var w = MarktWaren.Ware(id);
            if (w != null && w.Lager == "food" && t.HasKey("rewards.food." + id)) return t.T("rewards.food." + id);
            return MarktWaren.Anzeigename(id, _host.Settings.Locale);
        }

        private static VisualElement Thumb(string id) => WareThumb(id);

        /// <summary>A ware's picture: the food icon, the herb leaves, a workshop piece's prop, else a dot in the crop's colour.</summary>
        internal static VisualElement WareThumb(string id)
        {
            var w = MarktWaren.Ware(id);
            if (w != null && w.Lager == "storage") return Ux.ItemThumb(id, "toolbox");
            if (id == "kraeuter") return Ux.Icon("mat_blatt");
            var icon = CounterPanel.FoodIcon(id, null);
            if (icon != null) return Ux.Icon(icon);
            var dot = Ux.El("gb-ware-seed");
            var crop = Gooby.Core.Garden.GardenCrops.Crop(id);
            dot.style.backgroundColor = Gooby.Unity.World.ArtLibrary.Hex(crop?.Farbe ?? "#FFB46B");
            return dot;
        }

        private void Refresh()
        {
            var t = _host.Strings;
            var gs = _host.Store;
            var status = Status;
            var slice = MarktStand.SliceVon(gs);
            var day = MarktStand.AnzeigeTag(gs, Now, Bias);
            _head.Sub.text = t.T("markt.status." + status);
            _body.Clear();
            _host.World?.Interior?.SetStallWares(slice.Slots);

            // the day's favourite and Gooby's line of the day
            var favorite = MarktSim.Lieblingsware(MarktSim.TagesSeed(day));
            var fav = Ux.Row("gb-win-note", "gb-win-note--white");
            fav.Add(Ux.IconTile("heart", new Color(1f, 0.9f, 0.9f)));
            var favText = Ux.Col();
            favText.style.flexGrow = 1; favText.style.flexShrink = 1;
            favText.Add(Ux.Text(t.T("markt.stand.nachfrage", ("ware", WareName(favorite))), "gb-win-note-text"));
            var comment = Ux.Text(t.T("markt.kommentar." + (int)(MarktSim.TagesSeed(day) % 3 + 1)), "gb-sm", "gb-mild");
            comment.style.whiteSpace = WhiteSpace.Normal;
            favText.Add(comment);
            fav.Add(favText);
            fav.Add(Ux.Text(DayLabel(day), "gb-win-note-tag"));
            _body.Add(fav);
            if (_kraeuter > 0)
            {
                var herbs = Ux.Row("gb-win-note");
                herbs.Add(Ux.IconTile("mat_blatt"));
                herbs.Add(Ux.Text(t.T("markt.kraeuter.ertrag", ("n", _kraeuter)), "gb-win-note-text"));
                _body.Add(herbs);
            }
            if (MarktStand.MarktTage(gs) == 0 && (status == MarktStand.StatusLeer || status == MarktStand.StatusWartet))
            {
                var first = Ux.Row("gb-win-note", "gb-win-note--sand");
                first.name = "stand-erste-male";
                first.Add(Ux.IconTile("sparkle"));
                var col = Ux.Col();
                col.style.flexGrow = 1; col.style.flexShrink = 1;
                col.Add(Ux.Text(t.T("markt.erste_male.titel"), "gb-bold"));
                var text = Ux.Text(t.T("markt.erste_male.text"), "gb-sm");
                text.style.whiteSpace = WhiteSpace.Normal;
                col.Add(text);
                first.Add(col);
                _body.Add(first);
            }

            // market day: watch it or settle at once
            if (status == MarktStand.StatusLaeuft || status == MarktStand.StatusFertig)
            {
                var dayCard = Ux.Col("gb-stand-day");
                dayCard.Add(Ux.Text(t.T("markt.markttag." + status), "gb-stand-day-text"));
                var buttons = Ux.Row("gb-win-actions");
                var watch = Ux.IconTextBtn("play", t.T("markt.replay.knopf"), Watch, "gb-btn-primary");
                watch.name = "stand-watch";
                buttons.Add(watch);
                var collect = Ux.IconTextBtn("coin", t.T("markt.abholen.knopf"), Collect, "gb-btn-confirm");
                collect.name = "stand-collect";
                buttons.Add(collect);
                dayCard.Add(buttons);
                _body.Add(dayCard);
            }

            var canStock = MarktStand.KannBestuecken(status);
            _body.Add(CounterPanel.Section("shop", t.T("markt.stand.slots"), t.T("markt.stand.slots_frei", ("n", MarktStand.SlotMax - slice.Slots.Count))));
            var slots = Ux.El("gb-win-grid");
            if (slice.Slots.Count == 0)
                slots.Add(CounterPanel.Empty(t.T("markt.status.leer"), t.T("nl.stand.leer_hint"), row: true));
            foreach (var s in slice.Slots) slots.Add(SlotCard(s, canStock && status != MarktStand.StatusLeer));
            _body.Add(slots);

            _body.Add(CounterPanel.Section("inventory", t.T("markt.lager.titel")));
            var stock = Ux.El("gb-win-grid");
            var angebot = MarktWaren.AngebotDesSpielers(gs);
            if (angebot.Count == 0) stock.Add(CounterPanel.Empty(t.T("markt.lager.leer"), null, row: true));
            foreach (var a in angebot) stock.Add(StockCard(a, canStock));
            _body.Add(stock);
        }

        private string DayLabel(string day)
        {
            // "2026-10-10" → "Sa 10.10." / "Sat 10/10"
            var parts = day.Split('-');
            if (parts.Length != 3) return day;
            return _host.Settings.Locale == "en" ? "Sat " + parts[2] + "/" + parts[1] : "Sa " + parts[2] + "." + parts[1] + ".";
        }

        private VisualElement SlotCard(MarktSim.Slot s, bool editable)
        {
            var t = _host.Strings;
            var card = CounterPanel.Ware(Thumb(s.Ware), new Color(1f, 0.95f, 0.82f), WareName(s.Ware),
                t.T("markt.slot.preis", ("preis", MarktSim.Stueckpreis(s.Ware, s.Faktor)), ("basis", MarktWaren.Basis(s.Ware))), "×" + s.Menge, "gb-ware--wide");
            card.name = "stand-slot-" + s.Ware;
            var stepper = Ux.Row("gb-stand-stepper");
            var minus = Ux.IconBtn("arrow_down", () => Price(s.Ware, s.Faktor - 0.05), "gb-btn-icon-sm", "gb-btn-secondary");
            minus.name = "price-down-" + s.Ware;
            minus.SetEnabled(editable && s.Faktor > MarktStand.FaktorMin + 1e-6);
            stepper.Add(minus);
            var pct = Mathf.RoundToInt((float)(s.Faktor * 100.0)) - 100;
            var label = Ux.Text(pct == 0 ? t.T("nl.stand.basispreis") : (pct > 0 ? "+" : "") + pct + " %", "gb-stand-pct");
            if (pct < 0) label.AddToClassList("gb-stand-pct--billig");
            if (pct > 0) label.AddToClassList("gb-stand-pct--teuer");
            stepper.Add(label);
            var plus = Ux.IconBtn("arrow_up", () => Price(s.Ware, s.Faktor + 0.05), "gb-btn-icon-sm", "gb-btn-secondary");
            plus.name = "price-up-" + s.Ware;
            plus.SetEnabled(editable && s.Faktor < MarktStand.FaktorMax - 1e-6);
            stepper.Add(plus);
            card.Add(stepper);
            var down = CounterPanel.ActionBtn(t.T("markt.slot.runter"), () => TakeBack(s.Ware, s.Menge), editable, "gb-btn-secondary");
            down.name = "stand-down-" + s.Ware;
            card.Add(down);
            return card;
        }

        private VisualElement StockCard(MarktWaren.Angebot a, bool enabled)
        {
            var t = _host.Strings;
            var card = CounterPanel.Ware(Thumb(a.Id), new Color(0.87f, 0.96f, 0.9f), WareName(a.Id),
                t.T("nl.stand.basis", ("basis", a.Basis)), t.T("markt.lager.vorrat", ("n", a.Vorrat)), "gb-ware--sm");
            card.name = "stand-stock-" + a.Id;
            var one = CounterPanel.ActionBtn(t.T("markt.lager.plus_eins"), () => Stock(a.Id, 1), enabled, "gb-btn-primary");
            one.name = "stand-plus-" + a.Id;
            card.Add(one);
            if (a.Vorrat > 1)
            {
                var all = CounterPanel.ActionBtn(t.T("markt.lager.plus_alle", ("n", a.Vorrat)), () => Stock(a.Id, a.Vorrat), enabled, "gb-btn-secondary");
                all.name = "stand-all-" + a.Id;
                card.Add(all);
            }
            return card;
        }

        private void Stock(string id, int menge)
        {
            var slice = MarktStand.SliceVon(_host.Store);
            var slot = slice.Slots.Find(s => s.Ware == id);
            var n = MarktStand.Bestuecke(_host.Store, Now, Bias, id, menge, slot?.Faktor ?? 1.0);
            if (n <= 0)
            {
                Sfx.PlayUi("soft_error");
                if (slot == null && slice.Slots.Count >= MarktStand.SlotMax) _host.Ui.Toast(_host.Strings.T("nl.stand.voll"), "shop");
                return;
            }
            Sfx.PlayUi("soft_chip");
            Debug.Log("[stand] stock " + id + " +" + n + " status=" + Status);
            Refresh();
        }

        private void TakeBack(string id, int menge)
        {
            if (MarktStand.Entnehme(_host.Store, Now, Bias, id, menge) <= 0) { Sfx.PlayUi("soft_error"); return; }
            Sfx.PlayUi("soft_back");
            Refresh();
        }

        private void Price(string id, double faktor)
        {
            if (!MarktStand.SetFaktor(_host.Store, Now, Bias, id, faktor)) { Sfx.PlayUi("soft_error"); return; }
            Sfx.Play("soft_tick", (float)((faktor - 1.0) * 12.0));
            Refresh();
        }

        private void Watch()
        {
            if (MarktStand.Ergebnis(_host.Store) == null) return;
            Sfx.PlayUi("soft_confirm");
            _toMarketDay = true;
            _host.Ui.Pop();
            _host.Ui.Push(new MarktTagScreen(_host, watch: true));
        }

        private void Collect()
        {
            if (MarktStand.Ergebnis(_host.Store) == null) return;
            _toMarketDay = true;
            _host.Ui.Pop();
            _host.Ui.Push(new MarktTagScreen(_host, watch: false));
        }

        public override void Update() => _juice.Tick(Time.unscaledDeltaTime);
    }
}
