using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Counter sheet in the concept look: cream panel, segment tabs, item cards with price pills, coin balance
    /// top-right. One sheet for every assortment along the street — REHWEI / GOOBY MART (grocery, seeds, story
    /// books), GOOBYTHEKE (remedies, prescription gate), GOOBYMAN (drugstore: brushes, the once-only sleep mask,
    /// the daily plaster), Baumarkt Bodo Balken (material packs, blueprints once) and POW! (the camera once plus
    /// today's three discounted offers). Purchases go through ShopLogic / PowLogic.
    /// </summary>
    public sealed class ShopScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private readonly string _path;
        private readonly string _counter;
        private CounterPanel.Parts _ui;
        private VisualElement _cards;
        private Label _coins, _hint;
        private string _tab = "waren";
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();

        private bool Pharmacy => _path == ShopLogic.GoobythekePath;
        private bool Drugstore => _path == ShopLogic.GoobymanPath;

        /// <summary>The shop whose loyalty card a purchase here stamps.</summary>
        private string Laden => _place?.Id ?? "mart";

        public ShopScreen(GameHost host, DriveModel.Place place = null)
        {
            _host = host;
            _place = place;
            _path = place?.Shop ?? ShopLogic.RehweiPath;
            _counter = place?.Counter ?? DriveModel.CounterShop;
            Id = Pharmacy ? "shop-goobytheke" : _counter == DriveModel.CounterPow ? "shop-pow" : _counter == DriveModel.CounterBaumarkt ? "shop-baumarkt" : Drugstore ? "shop-goobyman" : "shop";
            _tab = _counter == DriveModel.CounterBaumarkt ? "material" : _counter == DriveModel.CounterPow ? "angebote" : "waren";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            // the shared counter window (CounterPanel): title band with tile, hint, coin pill and close; tabs above the grid
            _ui = CounterPanel.Build(root, _host, Icon(), Tint(), Title(), HintText(), () => _host.Ui.Pop());
            _hint = _ui.Hint;
            _coins = _ui.Coins;
            _cards = _ui.Body;

            var tabs = Ux.Row("gb-win-tabs");
            foreach (var (id, key) in TabDefs())
            {
                var local = id;
                var b = new Button(() => { _tab = local; RefreshTabs(); RefreshCards(); }) { text = t.T(key) };
                b.ClearClassList();
                b.AddToClassList("gb-chip");
                b.name = "tab-" + id;
                _tabs[id] = b;
                tabs.Add(b);
            }
            _ui.Content.Insert(0, tabs);

            _host.Store.CoinsChanged += OnCoins;
            RefreshTabs();
            RefreshCards();
        }

        public override void OnHidden() => _host.Store.CoinsChanged -= OnCoins;

        private string Icon() => Pharmacy ? "leaf" : _counter == DriveModel.CounterPow ? "star" : _counter == DriveModel.CounterBaumarkt ? "hammer" : Drugstore ? "sparkle" : "shop";

        private Color Tint() => Pharmacy ? new Color(0.82f, 0.94f, 0.86f) : _counter == DriveModel.CounterPow ? new Color(1f, 0.93f, 0.7f)
            : _counter == DriveModel.CounterBaumarkt ? new Color(1f, 0.88f, 0.75f) : Drugstore ? new Color(1f, 0.85f, 0.92f) : new Color(1f, 0.85f, 0.85f);

        private string Title()
        {
            var t = _host.Strings;
            if (Pharmacy) return t.T("city.ort.goobytheke");
            if (_counter == DriveModel.CounterPow) return t.T("city.pow.sheet_titel");
            if (_counter == DriveModel.CounterBaumarkt) return t.T("city.baumarkt.sheet_titel");
            if (Drugstore) return t.T("city.ort.goobyman");
            return t.T("city.laden.titel");
        }

        private string HintText()
        {
            var t = _host.Strings;
            if (Pharmacy) return t.T("nl.shop.hinweis_apotheke");
            if (_counter == DriveModel.CounterPow) return t.T("nl.shop.hinweis_pow");
            if (_counter == DriveModel.CounterBaumarkt) return t.T("nl.shop.hinweis_baumarkt");
            if (Drugstore) return t.T("nl.shop.hinweis_drogerie");
            return t.T("nl.shop.hinweis");
        }

        private (string id, string key)[] TabDefs()
        {
            if (Pharmacy) return new[] { ("waren", "nl.shop.medizin") };
            if (Drugstore) return new[] { ("waren", "nl.shop.drogerie") };
            if (_counter == DriveModel.CounterBaumarkt) return new[] { ("material", "city.baumarkt.material"), ("bauplaene", "city.baumarkt.bauplaene") };
            if (_counter == DriveModel.CounterPow) return new[] { ("angebote", "city.pow.angebote_titel"), ("kamera", "nl.shop.kamera") };
            return new[] { ("waren", "nl.shop.lebensmittel"), ("saatgut", "nl.shop.saatgut"), ("buecher", "nl.shop.buecher") };
        }

        private void OnCoins(int coins)
        {
            _coins.text = Ux.FormatNumber(coins);
            RefreshCards();
        }

        private void RefreshTabs()
        {
            foreach (var kv in _tabs) kv.Value.EnableInClassList("gb-chip--active", kv.Key == _tab);
        }

        /// <summary>One card on the counter: what it shows and what the button does.</summary>
        private sealed class Row
        {
            public Ware Ware;
            public string Icon, Name, Sub, Badge, ButtonLabel;
            public Color Tint;
            public int Preis;
            public bool Enabled;
            /// <summary>True while the ware is for sale: the pill shows coin + price instead of a status label.</summary>
            public bool Kauf;
            public Action Buy;
            public string OldPrice;
            /// <summary>The day's deal: star tag, the price 30 % down.</summary>
            public bool Angebot;
        }

        private List<Row> Rows()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            var rows = new List<Row>();
            switch (_tab)
            {
                case "angebote":
                {
                    var clock = _host.Store.Clock;
                    var now = clock.NowMs();
                    foreach (var a in PowLogic.Angebote(PowLogic.TagSeed(now, clock.UtcOffsetMinutes(now))))
                    {
                        var local = a;
                        // deco pieces count what stands in the storage, toys what is in Gooby's box
                        var moebel = PowLogic.MoebelVon(a.Ware.Id);
                        var have = moebel != null ? Gelagert(moebel) : ShopLogic.Vorrat(_host.Store, a.Ware);
                        rows.Add(new Row
                        {
                            Ware = a.Ware, Icon = CounterPanel.WareIcon(a.Ware.Id, "star"), Tint = new Color(1f, 0.93f, 0.7f), Name = a.Ware.Name(locale),
                            Sub = t.T("city.pow.rabatt", ("rabatt", a.Rabatt), ("alt", a.Ware.Preis)) + " · " + t.T(moebel != null ? "nl.pow.deko" : "nl.pow.spielzeug"),
                            Badge = have > 0 ? t.T("fuettern.vorrat_badge", ("anzahl", have)) : null,
                            Preis = a.PreisNeu, Enabled = a.PreisNeu <= ShopLogic.Coins(_host.Store), Kauf = true, ButtonLabel = t.T("city.laden.kaufen", ("preis", a.PreisNeu)),
                            Buy = () => PowKauf(local),
                        });
                    }
                    return rows;
                }
                case "kamera":
                {
                    var kamera = PowLogic.Kamera();
                    if (kamera == null) return rows;
                    var owned = PowLogic.HatKamera(_host.Store);
                    rows.Add(new Row
                    {
                        Ware = kamera, Icon = "camera", Tint = new Color(0.8f, 0.9f, 1f), Name = t.T("city.pow.kamera_titel"), Sub = t.T("city.pow.kamera_gate"),
                        Preis = kamera.Preis, Enabled = !owned && kamera.Preis <= ShopLogic.Coins(_host.Store), Kauf = !owned,
                        ButtonLabel = owned ? t.T("city.pow.kamera_hast_du") : t.T("city.laden.kaufen", ("preis", kamera.Preis)),
                        Buy = () => { if (PowLogic.KaufeKamera(_host.Store)) { _host.Store.NotifySliceChanged("economy"); _hint.text = t.T("city.pow.kamera_gekauft"); RefreshCards(); } else _hint.text = t.T("nl.shop.zu_teuer"); },
                    });
                    return rows;
                }
            }
            List<Ware> waren;
            switch (_tab)
            {
                case "saatgut": waren = ShopLogic.Saatgut(_path); break;
                case "buecher": waren = ShopLogic.Buecher(_path); break;
                case "material": waren = ShopLogic.Materialien(_path); break;
                case "bauplaene": waren = ShopLogic.Bauplaene(_path); break;
                default: waren = ShopLogic.Waren(_path); break;
            }
            var tag = _host.Store.Clock.LocalDay();
            var hauptListe = _tab == "waren" || _tab == "material";
            var deal = hauptListe && ShopTreue.HatKarte(Laden) ? ShopTreue.Angebot(_path, Laden, tag) : null;
            if (deal != null) waren = waren.OrderBy(w => w.Id == deal.Id ? 0 : 1).ToList();
            foreach (var ware in waren)
            {
                var local = ware;
                var kat = ware.IsFood ? FoodCatalog.Kategorie(ware.Id) : ware.Kategorie;
                var medicine = ware.Inventar == "medicine";
                var row = new Row
                {
                    Ware = ware, Preis = ware.Preis,
                    Icon = ware.IsFood ? CounterPanel.FoodIcon(ware.Id, kat == "suesses" ? "star" : kat == "warm" ? "sun" : "leaf")
                        : CounterPanel.WareIcon(ware.Id, ware.Kategorie == "buch" ? "book" : medicine ? "heart" : ware.Kategorie == "material" ? "hammer"
                            : ware.Kategorie == "bauplan" ? "map" : ware.Kategorie == "zahnbuerste" ? "toothbrush" : ware.IsEffekt ? "heart" : "leaf"),
                    Tint = kat == "suesses" ? new Color(1f, 0.93f, 0.7f) : kat == "warm" ? new Color(1f, 0.85f, 0.75f) : kat == "buch" ? new Color(0.8f, 0.9f, 1f)
                        : medicine || ware.IsEffekt ? new Color(1f, 0.88f, 0.9f) : ware.Kategorie == "material" ? new Color(1f, 0.88f, 0.75f) : ware.Kategorie == "bauplan" ? new Color(0.9f, 0.9f, 1f) : new Color(0.85f, 0.95f, 0.87f),
                    Name = ware.IsFood && t.HasKey("rewards.food." + ware.Id) ? t.T("rewards.food." + ware.Id) : ware.Name(locale),
                };
                if (ware.IsFood)
                {
                    var d = FoodCatalog.Def(ware.Id);
                    row.Sub = t.T("fuettern.pill_hunger", ("wert", (int)d.Hunger)) + " · " + t.T("fuettern.pill_spass", ("wert", (int)d.Fun));
                }
                else
                {
                    // shelf hint from the assortment (DE-authored, EN falls back), material packs name their count
                    var hint = J.Str(ware.Raw[locale == "en" ? "hinweis_en" : "hinweis_de"], J.Str(ware.Raw["hinweis_de"], J.Str(ware.Raw["spruch_de"])));
                    if (ware.Kategorie == "bauplan") hint = J.Str(ware.Raw["material_de"], hint);
                    row.Sub = hint.Length > 0 ? hint : ware.Kategorie == "material" ? t.T("nl.shop.paket", ("n", ware.Menge)) : ware.IsEffekt ? t.T("nl.shop.sofort") : null;
                }
                var have = ShopLogic.Vorrat(_host.Store, ware);
                if (have > 0 && !ware.NurEinmal) row.Badge = t.T("fuettern.vorrat_badge", ("anzahl", have));
                var owned = ShopLogic.Ausverkauft(_host.Store, ware);
                var needsPrescription = ware.BrauchtRezept && !ShopLogic.HatRezept(_host.Store);
                var usedToday = ware.IsEffekt && ShopLogic.PflasterHeuteVerbraucht(_host.Store);
                row.Enabled = ShopLogic.KannKaufen(_host.Store, ware) && !owned && !usedToday;
                row.Kauf = !owned && !needsPrescription && !usedToday;
                row.ButtonLabel = owned ? (ware.Kategorie == "buch" ? t.T("nl.shop.im_regal") : t.T("nl.shop.hast_du"))
                    : needsPrescription ? t.T("nl.shop.rezept_noetig") : usedToday ? t.T("nl.shop.heute_schon") : t.T("city.laden.kaufen", ("preis", ware.Preis));
                row.Buy = () => Done(ShopLogic.Kaufe(_host.Store, local, Laden), row.Name);
                if (deal != null && ware.Id == deal.Id && row.Kauf)
                {
                    var dealPreis = ShopTreue.AngebotPreis(ware);
                    row.Angebot = true;
                    row.Preis = dealPreis;
                    row.Enabled = dealPreis <= ShopLogic.Coins(_host.Store) && !owned;
                    row.Sub = t.T("nl.treue.angebot_statt", ("preis", ware.Preis)) + (row.Sub != null ? " · " + row.Sub : "");
                    row.Buy = () => Done(ShopLogic.Kaufe(_host.Store, local, Laden, dealPreis), row.Name);
                }
                rows.Add(row);
            }
            return rows;
        }

        private void RefreshCards()
        {
            var t = _host.Strings;
            _cards.Clear();
            var rows = Rows();
            if (rows.Count == 0)
            {
                _cards.Add(CounterPanel.Empty(t.T("nl.shop.leer"), t.T("nl.shop.leer_hint")));
                return;
            }
            foreach (var row in rows)
            {
                // white tile, pastel thumb, name, shelf hint, stock tag; the pill is coin + price while for sale, else the status
                var card = CounterPanel.Ware(row.Icon, row.Tint, row.Name, row.Sub, null);
                if (row.Angebot)
                {
                    card.AddToClassList("gb-ware--angebot");
                    var stern = Ux.Row("gb-ware-tag", "gb-ware-tag--left", "gb-ware-tag--angebot");
                    stern.name = "angebot-" + row.Ware.Id;
                    stern.pickingMode = PickingMode.Ignore;
                    stern.Add(Ux.Icon("star", "gb-ware-tag-icon"));
                    stern.Add(Ux.Text(t.T("nl.treue.angebot")));
                    card.Add(stern);
                }
                if (!string.IsNullOrEmpty(row.Badge)) card.Add(CounterPanel.Tag(row.Badge, "gb-ware-tag--sonne"));
                var buy = row.Kauf ? CounterPanel.Buy(row.Preis, row.Buy, row.Enabled) : CounterPanel.ActionBtn(row.ButtonLabel, row.Buy, row.Enabled);
                buy.name = "buy-" + row.Ware.Id;
                card.Add(buy);
                _cards.Add(card);
            }
        }

        private int Gelagert(string moebel)
        {
            var n = 0;
            foreach (var r in Gooby.Core.Home.HomeState.Storage(_host.Store))
                if (J.Str(r["item"]) == moebel) n += J.Int(r["count"]);
            return n;
        }

        /// <summary>A POW! offer: a full storage refuses the deco before paying; the line says where the piece went.</summary>
        private void PowKauf(PowLogic.Angebot a)
        {
            var t = _host.Strings;
            var name = a.Ware.Name(_host.Settings.Locale);
            var r = PowLogic.KaufeAngebotGrund(_host.Store, a);
            if (r == PowLogic.LagerVoll)
            {
                _hint.text = t.T("nl.pow.lager_voll");
                Sfx.PlayUi("soft_error");
                return;
            }
            Done(Gestempelt(r == PowLogic.Ok, a.Ware.Id, a.PreisNeu, true), name);
            if (r == PowLogic.Ok) _hint.text = t.T(PowLogic.MoebelVon(a.Ware.Id) != null ? "nl.pow.ins_lager" : "nl.pow.in_die_kiste", ("name", name));
        }

        /// <summary>POW!'s own purchases stamp the card here (ShopLogic stamps its own).</summary>
        private bool Gestempelt(bool ok, string wareId, int preis, bool angebot)
        {
            if (ok) _host.Store.Update(s => ShopTreue.Buche(s, Laden, wareId, preis, _host.Store.Clock.NowMs(), angebot));
            return ok;
        }

        private void Done(bool ok, string name)
        {
            var t = _host.Strings;
            if (!ok) { _hint.text = t.T("nl.shop.zu_teuer"); return; }
            _host.Store.NotifySliceChanged("economy");
            _hint.text = t.T("nl.shop.gekauft", ("name", name));
            if (ShopTreue.HatKarte(Laden))
            {
                var stempel = ShopTreue.Stempel(_host.Store.State, Laden);
                if (ShopTreue.Voll(_host.Store.State, Laden))
                {
                    Sfx.PlayUi("soft_sticker");
                    _host.Ui.Toast(t.T("nl.treue.voll_toast"), "gift", 4f);
                }
                else _host.Ui.Toast(t.T("nl.treue.stempel_toast", ("n", stempel), ("max", ShopTreue.KarteStempel)), "star", 2.2f);
                Debug.Log("[treue] " + Laden + " stempel " + stempel);
            }
            RefreshCards();
        }
    }
}
