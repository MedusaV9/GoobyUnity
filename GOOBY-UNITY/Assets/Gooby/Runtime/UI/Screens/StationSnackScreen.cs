using Gooby.Core.City;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>Astro-Snack automaton on GOOB-1: one row, the Weltraum-Möhre at the authored surcharge of 14.</summary>
    public sealed class StationSnackScreen : UiScreen
    {
        private readonly GameHost _host;
        private CounterPanel.Parts _ui;

        public StationSnackScreen(GameHost host)
        {
            _host = host;
            Id = "counter-station";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "leaf", new Color(0.85f, 0.45f, 0.7f), t.T("raumstation.automat.titel"), t.T("raumstation.automat.schild"), () => _host.Ui.Pop());
            var card = CounterPanel.Card("carrot", new Color(1f, 0.55f, 0.35f), t.T("raumstation.automat.moehre"),
                "+" + Raumstation.MoehrePreis + " ᴳ", null,
                t.T("nl.reise.buchen", ("preis", Raumstation.MoehrePreis)), ShopLogic.Coins(_host.Store) >= Raumstation.MoehrePreis, Buy);
            card.name = "moehre";
            _ui.Body.Add(card);
        }

        private void Buy()
        {
            var t = _host.Strings;
            if (Raumstation.KaufeMoehre(_host.Store))
            {
                _ui.Hint.text = t.T("nl.ort.item_erhalten");
                _ui.Coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));
            }
            else _ui.Hint.text = t.T("nl.reise.zu_teuer");
        }
    }
}
