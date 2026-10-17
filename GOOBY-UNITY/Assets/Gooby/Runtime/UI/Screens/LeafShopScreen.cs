using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The leaf exchange behind the "+" of the leaf pill (boards 05/08): garden things for leaves
    /// (<see cref="GardenLeaves.Offers"/>) straight into the storage. The counters' window kit with the leaf balance in
    /// the title band instead of coins; the note above the cards says how many leaves still lie in the garden today and
    /// answers every trade.
    /// </summary>
    public sealed class LeafShopScreen : UiScreen
    {
        public static readonly Color LeafTint = new Color(0.84f, 0.95f, 0.86f);

        private readonly GameHost _host;
        private VisualElement _grid;
        private Label _balance, _note;

        public LeafShopScreen(GameHost host)
        {
            _host = host;
            Id = "blaetter";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var panel = Ux.El("gb-win");
            Ux.Abs(panel, 120, 40, 120, 40);
            var head = CounterPanel.Head("leaf", LeafTint, t.T("nl.blaetter.titel"), t.T("nl.blaetter.untertitel"), () => _host.Ui.Pop());
            var pill = Ux.Pill("leaf", "0", out _balance);
            pill.AddToClassList("gb-win-coins");
            pill.name = "sheet-leaves";
            head.Row.Insert(head.Row.childCount - 1, pill);
            panel.Add(head.Row);

            var content = Ux.Col("gb-win-body");
            var note = Ux.Row("gb-win-note");
            note.Add(Ux.IconTile("leaf", LeafTint));
            _note = Ux.Text(TodayNote(), "gb-win-note-text");
            _note.name = "leaf-note";
            note.Add(_note);
            content.Add(note);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _grid = Ux.El("gb-win-grid");
            scroll.Add(_grid);
            content.Add(scroll);
            panel.Add(content);
            root.Add(panel);
            Rebuild();
        }

        /// <summary>How many of today's leaves are still to be found (the lawn may hold fewer when it is crowded).</summary>
        private string TodayNote()
        {
            var day = _host.Store.Clock.LocalDay();
            var left = Mathf.Max(0, GardenLeaves.CountFor(day) - GardenLeaves.Picked(_host.Store, day).Count);
            return left > 0 ? _host.Strings.T("nl.blaetter.noch", ("n", left)) : _host.Strings.T("nl.blaetter.keine_mehr");
        }

        private void Rebuild()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            var leaves = GardenLeaves.Count(_host.Store);
            _balance.text = Ux.FormatNumber(leaves);
            _grid.Clear();
            foreach (var offer in GardenLeaves.Offers())
            {
                var def = FurnitureCatalog.Def(offer.ItemId);
                var stored = StorageLogic.CountOf(HomeState.Storage(_host.Store), offer.ItemId);
                var card = CounterPanel.Ware(Ux.ItemThumb(offer.ItemId, "leaf"), LeafTint, def?.DisplayName(locale) ?? offer.ItemId,
                    stored > 0 ? t.T("nl.blaetter.im_lager", ("n", stored)) : null, null);
                card.name = "leaf-offer-" + offer.ItemId;
                var id = offer.ItemId;
                var affordable = leaves >= offer.Price;
                var buy = Ux.IconTextBtn("leaf", Ux.FormatNumber(offer.Price), () => Trade(id),
                    affordable ? "gb-btn-primary" : "gb-btn-secondary", "gb-btn-sm", "gb-ware-btn");
                buy.name = "leaf-buy-" + id;
                buy.SetEnabled(affordable);
                card.Add(buy);
                _grid.Add(card);
            }
        }

        private void Trade(string itemId)
        {
            var t = _host.Strings;
            var def = FurnitureCatalog.Def(itemId);
            var price = GardenLeaves.PriceOf(def);
            var result = GardenLeaves.Buy(_host.Store, itemId);
            switch (result)
            {
                case GardenLeaves.ResultOk:
                    Sfx.Play("soft_confirm");
                    _note.text = t.T("nl.blaetter.gekauft", ("name", def?.DisplayName(_host.Settings.Locale) ?? itemId));
                    break;
                case GardenLeaves.ResultStorageFull:
                    Sfx.Play("soft_error");
                    _note.text = t.T("nl.blaetter.lager_voll");
                    break;
                default:
                    Sfx.Play("soft_error");
                    _note.text = t.T("nl.blaetter.zu_wenig", ("n", Mathf.Max(1, price - (int)GardenLeaves.Count(_host.Store))));
                    break;
            }
            Debug.Log("[leaves] trade " + itemId + " → " + (result == GardenLeaves.ResultOk ? "ok" : result) + ", left " + GardenLeaves.Count(_host.Store));
            Rebuild();
        }
    }
}
