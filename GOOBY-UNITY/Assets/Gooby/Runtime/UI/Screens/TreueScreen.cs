using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The shop's loyalty card and till (the counter tabs "Sammeln" and "Kasse"): ten stamp circles, filled in the shop's
    /// colour; a full card hands over the present from the shelf (it flies home with confetti); below, the receipt of the
    /// last purchases with the day's deal marked and the total. A sheet over the interior like the counters.
    /// </summary>
    public sealed class TreueScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private CounterPanel.Parts _ui;

        public TreueScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "treue-" + (place?.Id ?? "mart");
        }

        private string Laden => _place?.Id ?? "mart";
        private string Pfad => _place?.Shop ?? ShopLogic.RehweiPath;

        private Color Farbe => Laden == "goobytheke" ? new Color(0.48f, 0.78f, 0.55f) : Laden == "pow" ? new Color(0.98f, 0.76f, 0.25f)
            : Laden == "baumarkt" ? new Color(0.95f, 0.62f, 0.38f) : Laden == "goobyman" ? new Color(0.93f, 0.55f, 0.75f) : new Color(0.95f, 0.45f, 0.45f);

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "gift", new Color(1f, 0.9f, 0.93f), t.T("nl.treue.titel"), t.T("nl.treue.hinweis"), () => _host.Ui.Pop());
            Refresh();
        }

        private void Refresh()
        {
            if (_ui == null) return;
            var t = _host.Strings;
            var state = _host.Store.State;
            _ui.Body.Clear();
            _ui.Coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));

            var karte = Ux.Col("gb-card", "gb-treue-karte");
            karte.name = "treue-karte";
            var stempel = ShopTreue.Stempel(state, Laden);
            var kopf = Ux.Row("gb-treue-kopf");
            kopf.Add(Ux.Text(t.T("nl.treue.stempel", ("n", stempel), ("max", ShopTreue.KarteStempel)), "gb-h2"));
            var geschenke = ShopTreue.Geschenke(state, Laden);
            if (geschenke > 0) kopf.Add(Ux.Text(t.T("nl.treue.geschenke", ("n", geschenke)), "gb-sm", "gb-mild", "gb-treue-bisher"));
            karte.Add(kopf);
            var reihen = Ux.Col("gb-treue-stempel");
            for (var r = 0; r < 2; r++)
            {
                var reihe = Ux.Row("gb-treue-reihe");
                for (var i = 0; i < 5; i++)
                {
                    var nr = r * 5 + i;
                    var kreis = Ux.El("gb-treue-kreis");
                    kreis.name = "treue-kreis-" + nr;
                    if (nr < stempel)
                    {
                        kreis.AddToClassList("gb-treue-kreis--voll");
                        kreis.style.backgroundColor = Farbe;
                        kreis.Add(Ux.Icon(nr == ShopTreue.KarteStempel - 1 ? "gift" : "star", "gb-treue-kreis-icon"));
                    }
                    else if (nr == ShopTreue.KarteStempel - 1) kreis.Add(Ux.Icon("gift", "gb-treue-kreis-icon", "gb-treue-kreis-icon--leer"));
                    reihe.Add(kreis);
                }
                reihen.Add(reihe);
            }
            karte.Add(reihen);
            if (ShopTreue.Voll(state, Laden))
            {
                var abholen = Ux.IconTextBtn("gift", t.T("nl.treue.abholen"), Abholen, "gb-btn-confirm", "gb-treue-abholen");
                abholen.name = "treue-abholen";
                karte.Add(abholen);
            }
            _ui.Body.Add(karte);

            var bon = Ux.Col("gb-treue-bon");
            bon.name = "treue-bon";
            bon.Add(Ux.Text(t.T("nl.treue.bon_titel"), "gb-treue-bon-titel"));
            bon.Add(Ux.Text(Ortsname(), "gb-treue-bon-laden"));
            var zeilen = ShopTreue.Bon(state, Laden);
            if (zeilen.Count == 0) bon.Add(Ux.Text(t.T("nl.treue.bon_leer"), "gb-sm", "gb-treue-bon-leer"));
            var summe = 0;
            var waren = AlleWaren();
            foreach (var z in zeilen)
            {
                var zeile = Ux.Row("gb-treue-bon-zeile");
                var name = waren.TryGetValue(z.Id, out var w) ? WareName(w) : z.Id;
                zeile.Add(Ux.Text((z.Angebot ? "* " : "") + name, "gb-treue-bon-name"));
                zeile.Add(Ux.Text(z.Preis.ToString(), "gb-treue-bon-preis"));
                bon.Add(zeile);
                summe += z.Preis;
            }
            if (zeilen.Count > 0)
            {
                var gesamt = Ux.Row("gb-treue-bon-zeile", "gb-treue-bon-summe");
                gesamt.Add(Ux.Text(t.T("nl.treue.bon_summe"), "gb-treue-bon-name"));
                gesamt.Add(Ux.Text(summe.ToString(), "gb-treue-bon-preis"));
                bon.Add(gesamt);
            }
            bon.Add(Ux.Text(t.T("nl.treue.bon_danke"), "gb-sm", "gb-treue-bon-danke"));
            _ui.Body.Add(bon);
        }

        private void Abholen()
        {
            Ware ware = null;
            _host.Store.Update(s => ware = ShopTreue.GeschenkAbholen(s, Laden, Pfad), GameHost.TicksMs);
            if (ware == null) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged("inventory");
            Sfx.PlayUi("soft_win");
            Haptics.Success();
            _host.Ui.Confetti(60);
            _host.Ui.Toast(_host.Strings.T("nl.treue.geschenk", ("name", WareName(ware))), "gift", 4f);
            Debug.Log("[treue] " + Laden + " geschenk " + ware.Id);
            Refresh();
        }

        private string WareName(Ware w)
        {
            var t = _host.Strings;
            return w.IsFood && t.HasKey("rewards.food." + w.Id) ? t.T("rewards.food." + w.Id) : w.Name(_host.Settings.Locale);
        }

        private Dictionary<string, Ware> AlleWaren()
        {
            var alle = new Dictionary<string, Ware>();
            foreach (var w in ShopLogic.Waren(Pfad).Concat(ShopLogic.Saatgut(Pfad)).Concat(ShopLogic.Buecher(Pfad)).Concat(ShopLogic.Materialien(Pfad)).Concat(ShopLogic.Bauplaene(Pfad)))
                alle[w.Id] = w;
            foreach (var w in PowLogic.Pool()) alle[w.Id] = w;
            var kamera = PowLogic.Kamera();
            if (kamera != null) alle[kamera.Id] = kamera;
            return alle;
        }

        private string Ortsname()
        {
            var t = _host.Strings;
            if (_place == null) return t.T("nl.stadt.ort_mart");
            return t.HasKey(_place.NameKey) ? t.T(_place.NameKey) : _place.Id;
        }
    }
}
