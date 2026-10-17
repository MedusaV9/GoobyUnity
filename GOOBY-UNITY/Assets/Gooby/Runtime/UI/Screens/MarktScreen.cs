using Gooby.Core.City;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Greta's stall: the player's harvest with today's next-unit price, "sell one" / "sell all" — every unit sold
    /// pushes the price down until the floor (MarktLogic). Empty basket → the sign.
    /// </summary>
    public sealed class MarktScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private CounterPanel.Parts _ui;

        public MarktScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "counter-markt";
        }

        private string Heute
        {
            get
            {
                var clock = _host.Store.Clock;
                var now = clock.NowMs();
                return MarktLogic.TagesKey(now, clock.UtcOffsetMinutes(now));
            }
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "leaf", new Color(0.85f, 0.95f, 0.87f), t.T("city.markt.sheet_titel"), t.T("nl.markt.hinweis"), () => _host.Ui.Pop());
            _host.Store.CoinsChanged += OnCoins;
            Refresh();
        }

        public override void OnHidden() => _host.Store.CoinsChanged -= OnCoins;

        private void OnCoins(int coins) => _ui.Coins.text = Ux.FormatNumber(coins);

        private void Refresh()
        {
            var t = _host.Strings;
            _ui.Body.Clear();
            if (!Offen())
            {
                // Greta only buys on Saturday mornings; the own stall can be stocked any day
                _ui.Body.Add(CounterPanel.Empty(t.T("city.ort.nur_samstag"), t.T("nl.markt.zu_stand"), row: true));
                var stand = Ux.IconTextBtn("shop", t.T("nl.ort.tab.meinstand"), () => { _host.Ui.Pop(); _host.Ui.Push(new MarktStandScreen(_host)); }, "gb-btn-primary");
                stand.name = "markt-zum-stand";
                _ui.Body.Add(stand);
                return;
            }
            var heute = Heute;
            var rows = MarktLogic.AngebotDesSpielers(_host.Store, heute);
            if (rows.Count == 0)
            {
                // empty basket: Gooby with the stall's sign text instead of a lone card
                _ui.Body.Add(CounterPanel.Empty(t.T("nl.markt.korb_leer"), t.T("city.markt.schild_text")));
                return;
            }
            foreach (var a in rows)
            {
                var local = a;
                var name = t.HasKey("rewards.food." + a.Sorte.Id) ? t.T("rewards.food." + a.Sorte.Id) : a.Sorte.NameDe;
                var sub = a.Preis < a.VollerPreis ? t.T("city.markt.gedrueckt", ("voll", a.VollerPreis)) : t.T("nl.markt.voll", ("preis", a.VollerPreis));
                var card = CounterPanel.Card(CounterPanel.FoodIcon(a.Sorte.Id, "sprout"), new Color(0.85f, 0.95f, 0.87f), name, sub, null,
                    t.T("nl.markt.eins", ("preis", a.Preis)), true, () => Sell(local.Sorte.Id, 1), "gb-btn-confirm");
                card.Add(CounterPanel.Tag(t.T("city.markt.vorrat", ("n", a.Vorrat)), "gb-ware-tag--mint"));
                var alle = CounterPanel.ActionBtn(t.T("nl.markt.alle", ("n", a.Vorrat), ("preis", MarktLogic.Erloes(a.Sorte.Id, a.Vorrat, a.HeuteVerkauft))),
                    () => Sell(local.Sorte.Id, local.Vorrat), a.Vorrat > 1, "gb-btn-secondary", "gb-ware-btn--second");
                card.Add(alle);
                _ui.Body.Add(card);
            }
        }

        private bool Offen()
        {
            var clock = _host.Store.Clock;
            var now = clock.NowMs();
            var (wochentag, stunde) = CityMap.LokaleZeit(now, clock.UtcOffsetMinutes(now));
            return CityMap.IstOffen(CityMap.Get(_place.MapId), wochentag, stunde);
        }

        private void Sell(string id, int menge)
        {
            var t = _host.Strings;
            var (ok, n, erloes) = MarktLogic.Verkaufen(_host.Store, Heute, id, menge);
            if (ok) _ui.Hint.text = t.T("nl.markt.verkauft", ("n", n), ("coins", erloes));
            Refresh();
        }
    }
}
