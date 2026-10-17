using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// GOOBERANDO app (gooberando.gd): pick a kitchen, order a dish, watch the countdown, open the door, tip.
    /// The van on the street is a pure function of the same timestamps.
    /// </summary>
    public sealed class GooberandoScreen : UiScreen
    {
        private readonly GameHost _host;
        private CounterPanel.Parts _ui;
        private string _restaurant = "";

        public GooberandoScreen(GameHost host)
        {
            _host = host;
            Id = "gooberando";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "food_burger", new Color(1f, 0.78f, 0.55f), t.T("phone.app.gooberando"), t.T("travel.gooberando.slogan"), () => _host.Ui.Pop());
            Refresh();
        }

        public override void Update()
        {
            var now = _host.Store.Clock.NowMs();
            var caught = GooberandoLogic.CatchUp(_host.Store, now);
            if (caught.Events.Count > 0) Refresh();
            else if (J.Str(GooberandoLogic.SliceOf(_host.Store.State)["state"]) == GooberandoLogic.StateBestellt)
                RefreshCountdown();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            _ui.Body.Clear();
            _ui.Coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));
            var slice = GooberandoLogic.SliceOf(_host.Store.State);
            var state = J.Str(slice["state"]);
            if (state == GooberandoLogic.StateBestellt)
            {
                _ui.Hint.text = t.T("travel.gooberando.unterwegs");
                // on its way: Gooby waiting by the door, the live countdown runs in the title band
                _ui.Body.Add(CounterPanel.Empty(t.T("travel.gooberando.bestellt"), RestLabel(slice)));
                return;
            }
            if (state == GooberandoLogic.StateVorDerTuer)
            {
                _ui.Hint.text = t.T("travel.gooberando.klingel");
                var card = CounterPanel.Card("home", new Color(1f, 0.86f, 0.7f), t.T("travel.gooberando.uebergeben"), null, null, t.T("travel.gooberando.annehmen"), true, Accept, "gb-btn-confirm");
                card.name = "doorbell";
                _ui.Body.Add(card);
                return;
            }
            if (state == GooberandoLogic.StateTrinkgeld)
            {
                _ui.Hint.text = t.T("travel.gooberando.uebergeben");
                _ui.Body.Add(CounterPanel.Card("heart", new Color(1f, 0.88f, 0.72f), t.T("travel.gooberando.trinkgeld_geben"), null, null, t.T("travel.gooberando.trinkgeld_geben"), ShopLogic.Coins(_host.Store) >= GooberandoLogic.TrinkgeldBetrag, () => Tip(true)));
                _ui.Body.Add(CounterPanel.Card("sparkle", new Color(0.93f, 0.91f, 0.86f), t.T("travel.gooberando.nur_winken"), null, null, t.T("travel.gooberando.nur_winken"), true, () => Tip(false), "gb-btn-secondary"));
                return;
            }
            if (_restaurant.Length == 0)
            {
                _ui.Hint.text = t.T("travel.gooberando.gebuehr", ("gebuehr", GooberandoLogic.Liefergebuehr));
                foreach (var r in GooberandoRestaurants.All())
                {
                    var local = r;
                    var gag = t.HasKey(r.GagKey) ? t.T(r.GagKey) : r.Sterne + " ★";
                    var card = CounterPanel.Card("shop", new Color(1f, 0.86f, 0.72f), r.NameDe, gag, null, t.T("nl.ort.weiter"), true, () => { _restaurant = local.Id; Refresh(); });
                    card.name = "kitchen-" + r.Id;
                    _ui.Body.Add(card);
                }
                return;
            }
            var kitchen = GooberandoRestaurants.ById(_restaurant);
            if (kitchen == null) { _restaurant = ""; Refresh(); return; }
            foreach (var g in kitchen.Gerichte)
            {
                var dish = g;
                var card = CounterPanel.Card(CounterPanel.FoodIcon(g.Id, "food_burger"), new Color(0.87f, 0.95f, 0.8f), g.NameDe,
                    t.T("nl.reise.lieferung_inkl", ("preis", g.Preis), ("gebuehr", GooberandoLogic.Liefergebuehr)), null,
                    t.T("nl.reise.buchen", ("preis", g.Preis + GooberandoLogic.Liefergebuehr)),
                    ShopLogic.Coins(_host.Store) >= g.Preis + GooberandoLogic.Liefergebuehr, () => Order(kitchen, dish));
                card.name = "dish-" + g.Id;
                _ui.Body.Add(card);
            }
        }

        private void RefreshCountdown()
        {
            if (_ui?.Hint == null) return;
            var slice = GooberandoLogic.SliceOf(_host.Store.State);
            _ui.Hint.text = RestLabel(slice);
        }

        private string RestLabel(Newtonsoft.Json.Linq.JObject slice)
        {
            var rest = GooberandoLogic.LieferRestS(slice, _host.Store.Clock.NowMs());
            var min = rest / 60; var s = rest % 60;
            return _host.Strings.T("travel.gooberando.countdown", ("min", min), ("s", s.ToString("00")));
        }

        private void Order(GooberandoRestaurants.Restaurant kitchen, GooberandoRestaurants.Gericht dish)
        {
            var now = _host.Store.Clock.NowMs();
            var start = GooberandoRestaurants.StreetTile(kitchen.Id);
            var home = (CityGrid.HomeStreet[0], CityGrid.HomeStreet[1]);
            var drive = (int)System.Math.Ceiling(GooberandoFahrerSim.FahrzeitS(GooberandoFahrerSim.RouteWorld(start, home)));
            var prep = PrepOverride() ?? (GooberandoRestaurants.PrepS(kitchen) + drive);
            if (GooberandoLogic.Buchen(_host.Store, now, prep, dish.Id, dish.Preis, kitchen.Id))
            {
                _ui.Hint.text = _host.Strings.T("travel.gooberando.bestellt");
                Debug.Log("[gooberando] ordered " + dish.Id + " from " + kitchen.Id + " prep=" + prep + "s");
            }
            else _ui.Hint.text = _host.Strings.T("nl.reise.zu_teuer");
            Refresh();
        }

        private void Accept()
        {
            var r = GooberandoLogic.Annehmen(_host.Store, _host.Store.Clock.NowMs());
            if (r.Ok) _ui.Hint.text = _host.Strings.T("travel.gooberando.uebergeben");
            Refresh();
        }

        private void Tip(bool geben)
        {
            var r = GooberandoLogic.TrinkgeldGeben(_host.Store, _host.Store.Clock.NowMs(), geben, 0.5);
            if (r.Buff) _ui.Hint.text = _host.Strings.T("travel.gooberando.buff");
            else if (r.Hinweis) _ui.Hint.text = _host.Strings.T("travel.gooberando.trinkgeld_hinweis");
            Refresh();
        }

        /// <summary>QA: <c>-gooby-gooberando-prep N</c> or env GOOBY_GOOBERANDO_PREP shortens the kitchen wait.</summary>
        public static int? PrepOverride()
        {
            var env = System.Environment.GetEnvironmentVariable("GOOBY_GOOBERANDO_PREP");
            if (int.TryParse(env, out var n) && n > 0) return n;
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-gooby-gooberando-prep" && int.TryParse(args[i + 1], out n) && n > 0) return n;
            return null;
        }
    }
}
