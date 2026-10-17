using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The Funkelpark ticket desk (funkelpark.gd): four rides — coaster, wheel, carousel to ride, the bumper cars to
    /// watch — with price, ride counter and Gooby's reaction, plus the snack alley's three stalls into the tray.
    /// </summary>
    public sealed class ParkScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private CounterPanel.Parts _ui;

        public ParkScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "counter-park";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "star", new Color(1f, 0.85f, 0.92f), t.T("park.gate.titel"), t.T("nl.park.hinweis"), () => _host.Ui.Pop());
            _host.Store.CoinsChanged += OnCoins;
            Refresh();
        }

        public override void OnHidden() => _host.Store.CoinsChanged -= OnCoins;

        private void OnCoins(int coins) { _ui.Coins.text = Ux.FormatNumber(coins); Refresh(); }

        private void Refresh()
        {
            var t = _host.Strings;
            _ui.Body.Clear();
            foreach (var ride in ParkLogic.Rides)
            {
                var local = ride;
                var count = ride.Zaehlt ? ParkLogic.Fahrten(_host.Store, ride.Id) : 0;
                var can = ParkLogic.KannFahren(_host.Store, ride);
                var card = CounterPanel.Card(ride.Id == "wheel" ? "sun" : ride.Id == "scooter" ? "car" : "star",
                    ride.Id == "coaster" ? new Color(1f, 0.85f, 0.85f) : ride.Id == "wheel" ? new Color(0.8f, 0.9f, 1f) : ride.Id == "karussell" ? new Color(1f, 0.93f, 0.7f) : new Color(0.85f, 0.95f, 0.87f),
                    t.T(ride.NameKey), t.T(ride.TextKey), null,
                    ride.Preis > 0 ? t.T("nl.park.fahren", ("preis", ride.Preis)) : t.T("nl.park.zuschauen"), ride.Preis == 0 || can, () => Ride(local),
                    ride.Preis > 0 ? (can ? "gb-btn-primary" : "gb-btn-secondary") : "gb-btn-teal");
                if (count > 0) card.Add(CounterPanel.Tag(t.T("nl.park.gefahren", ("n", count)), "gb-ware-tag--rosa"));
                card.name = "ride-" + ride.Id;
                _ui.Body.Add(card);
            }
            foreach (var stall in ParkLogic.Stalls)
            {
                var local = stall;
                var name = t.HasKey("park.food." + stall.Id) ? t.T("park.food." + stall.Id) : stall.Id;
                var have = J.Int(J.Path(_host.Store.State, "inventory", "food", stall.Id));
                // snack alley: the stall's colour as a soft thumb, stock at home as the corner tag
                var snack = CounterPanel.Card(CounterPanel.FoodIcon(stall.Id, "gift"), CounterPanel.Pastel(ArtLibrary.Hex(stall.Tint), 0.6f), t.T("park.stall." + stall.Id + ".name"), t.T("park.stall." + stall.Id + ".pitch"),
                    null, t.T("nl.park.naschen", ("preis", stall.Preis)), ShopLogic.Coins(_host.Store) >= stall.Preis,
                    () => { if (ParkLogic.Naschen(_host.Store, local)) _ui.Hint.text = t.T("nl.park.genascht", ("name", name)); Refresh(); });
                if (have > 0) snack.Add(CounterPanel.Tag(t.T("fuettern.vorrat_badge", ("anzahl", have)), "gb-ware-tag--sonne"));
                snack.name = "stall-" + stall.Id;
                _ui.Body.Add(snack);
            }
        }

        private void Ride(ParkLogic.Ride ride)
        {
            var t = _host.Strings;
            var (ok, reason) = ParkLogic.Fahren(_host.Store, ride);
            _ui.Hint.text = ok ? (ride.Id == "scooter" ? t.T("park.scooter.hint") : t.T("nl.park.fahrt_ok")) : reason == "tired" ? t.T("nl.park.zu_muede") : t.T("nl.park.zu_teuer");
            if (ok && ride.Preis > 0) _host.World?.Bounce();
            Refresh();
        }
    }
}
