using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Benzino's showroom: the four cars of the catalog with their stats and paint swatches. Owned cars can be
    /// driven or repainted, the others bought in the chosen colour (AutoKatalog); the street's hero car takes the
    /// paint and shape right away.
    /// </summary>
    public sealed class AutohausScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private CounterPanel.Parts _ui;
        private readonly Dictionary<string, string> _chosen = new Dictionary<string, string>();

        public AutohausScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "counter-autohaus";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "car", new Color(0.8f, 0.9f, 1f), t.T("city.autohaus.sheet_titel"), t.T("nl.autohaus.hinweis"), () => _host.Ui.Pop());
            _host.Store.CoinsChanged += OnCoins;
            Refresh();
        }

        public override void OnHidden() => _host.Store.CoinsChanged -= OnCoins;

        private void OnCoins(int coins) { _ui.Coins.text = Ux.FormatNumber(coins); Refresh(); }

        /// <summary>The sheet steps aside and the dealer's door opens onto the street with the chosen car.</summary>
        private void TestDrive(string carId, string farbe)
        {
            Sfx.PlayUi("soft_confirm");
            _host.Ui.Pop();
            if (_host.Ui.Current is PlaceScreen dealer) dealer.LeaveForTestDrive(carId, farbe);
        }

        private void Refresh()
        {
            var t = _host.Strings;
            _ui.Body.Clear();
            var besitz = AutoKatalog.Besitz(_host.Store);
            var (aktiv, aktivFarbe) = AutoKatalog.AktivesAuto(_host.Store);
            foreach (var auto in AutoKatalog.Autos())
            {
                var local = auto;
                var owned = besitz.TryGetValue(auto.Id, out var ownedColor);
                if (!_chosen.TryGetValue(auto.Id, out var farbe)) farbe = owned ? ownedColor : (auto.Farben.Count > 0 ? auto.Farben[0] : "#E8524A");
                var isActive = aktiv != null && aktiv.Id == auto.Id;
                // white tile with the car on a pastel of the chosen paint, stats + slogan, paint swatches, colour name
                var card = CounterPanel.Ware("car", CounterPanel.Pastel(ArtLibrary.Hex(farbe), 0.6f), auto.NameDe,
                    t.T("nl.autohaus.stats", ("tempo", auto.Speed), ("lenkung", auto.Handling), ("antritt", auto.Boost)), null, "gb-ware--wide");
                card.name = "car-" + auto.Id;
                var spruch = Ux.Text(auto.Spruch, "gb-ware-sub");
                spruch.pickingMode = PickingMode.Ignore;
                card.Insert(card.IndexOf(card.Q(className: "gb-ware-spacer")), spruch);
                // paint swatches
                var swatches = Ux.El("gb-paint-row");
                foreach (var hex in auto.Farben)
                {
                    var localHex = hex;
                    var sw = new Button(() => { _chosen[local.Id] = localHex; if (owned) AutoKatalog.Lackieren(_host.Store, local.Id, localHex); Refresh(); }) { text = "" };
                    sw.ClearClassList();
                    sw.AddToClassList("gb-paint");
                    if (hex == farbe) sw.AddToClassList("gb-paint--on");
                    sw.style.backgroundColor = ArtLibrary.Hex(hex);
                    sw.tooltip = AutoKatalog.FarbName(hex);
                    swatches.Add(sw);
                }
                card.Add(swatches);
                card.Add(Ux.Text(AutoKatalog.FarbName(farbe), "gb-ware-meta"));
                if (isActive) card.Add(CounterPanel.Tag(t.T("nl.autohaus.faehrst_du"), "gb-ware-tag--mint"));
                else if (owned) card.Add(CounterPanel.Tag(t.T("city.autohaus.im_besitz"), "gb-ware-tag--blau"));
                Button action;
                if (owned)
                {
                    action = CounterPanel.ActionBtn(t.T("nl.autohaus.fahren"), () => { AutoKatalog.Waehle(_host.Store, local.Id); Refresh(); }, !isActive, isActive ? "gb-btn-secondary" : "gb-btn-confirm");
                }
                else
                {
                    var can = AutoKatalog.KannKaufen(_host.Store, auto.Id, farbe);
                    var farbeNow = farbe;
                    action = CounterPanel.ActionBtn(t.T("nl.autohaus.kaufen", ("preis", auto.Preis)), () =>
                    {
                        if (AutoKatalog.Kaufe(_host.Store, local.Id, farbeNow)) { _ui.Hint.text = t.T("nl.autohaus.gekauft"); _host.Store.NotifySliceChanged("economy"); }
                        else _ui.Hint.text = t.T("nl.autohaus.zu_teuer");
                        Refresh();
                    }, can);
                }
                action.name = "car-action-" + auto.Id;
                card.Add(action);
                if (!owned)
                {
                    // a minute through town in this car and this paint, then back to Benzino
                    var farbeTrial = farbe;
                    var trial = Ux.IconTextBtn("compass", t.T("nl.autohaus.probefahrt"), () => TestDrive(local.Id, farbeTrial), "gb-btn-secondary", "gb-btn-sm");
                    trial.name = "car-trial-" + auto.Id;
                    trial.style.marginTop = 6;
                    card.Add(trial);
                }
                _ui.Body.Add(card);
            }
        }
    }
}
