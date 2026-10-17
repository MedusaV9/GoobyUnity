using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Goobay (port of goobay_panel.gd, Doc D §5.4): sell furniture from storage to neighbours over a little chat. Pick a
    /// piece on the left, a buyer writes with an opening offer; "Höher!" gets more but their mood (the dots) sours, and
    /// past their budget or patience comes the very last offer — or they are gone (that piece rests until tomorrow).
    /// "Deal!" asks pickup or the post (+10 %); the coins fly into the pill. The last bed, couch and fridge are never
    /// listed, and pieces the day's demand likes carry a tag.
    /// </summary>
    public sealed class GoobayScreen : UiScreen
    {
        private static readonly string[] Kaeufer = { "Gerd", "Bibi", "Olaf", "Tante Roswita", "Mo", "Klaus-Dieter" };

        private readonly GameHost _host;
        private readonly GoobyRng _rng = new GoobyRng(System.Environment.TickCount);
        private CounterPanel.Parts _ui;
        private VisualElement _list, _chat, _log, _buttons, _mood;
        private Label _buyerName;
        private GoobayLogic.Session _session;
        private string _buyer = "";
        private readonly Juice _juice = new Juice();

        public GoobayScreen(GameHost host)
        {
            _host = host;
            Id = "goobay";
        }

        private string Heute => _host.Store.Clock.LocalDay();

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "gift", new Color(0.82f, 0.95f, 0.88f), t.T("goobay.titel"), t.T("nl.goobay.hinweis"), Close, fit: false);
            _ui.Scroll.RemoveFromHierarchy();
            var split = Ux.Row("gb-goobay");
            var listScroll = new ScrollView(ScrollViewMode.Vertical);
            listScroll.AddToClassList("gb-goobay-list");
            _list = Ux.Col();
            listScroll.Add(_list);
            split.Add(listScroll);
            _chat = Ux.Col("gb-goobay-chat");
            var head = Ux.Row("gb-goobay-buyer");
            var avatar = Ux.El("gb-goobay-avatar");
            avatar.Add(Ux.Icon("user"));
            head.Add(avatar);
            _buyerName = Ux.Text(t.T("nl.goobay.waehle"), "gb-goobay-buyer-name");
            head.Add(_buyerName);
            _mood = Ux.Row("gb-goobay-mood");
            head.Add(_mood);
            _chat.Add(head);
            var logScroll = new ScrollView(ScrollViewMode.Vertical);
            logScroll.AddToClassList("gb-goobay-log");
            _log = Ux.Col();
            logScroll.Add(_log);
            _chat.Add(logScroll);
            _buttons = Ux.Row("gb-goobay-buttons");
            _chat.Add(_buttons);
            split.Add(_chat);
            _ui.Content.Add(split);
            root.Add(_juice.Layer);
            _host.Store.CoinsChanged += OnCoins;
            RefreshList();
        }

        public override void OnHidden()
        {
            if (!_host.Ui.IsMounted(this)) _host.Store.CoinsChanged -= OnCoins;
        }

        private void OnCoins(int coins) => _ui.Coins.text = Ux.FormatNumber(coins);

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            _host.Ui.Pop();
        }

        private void RefreshList()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            _list.Clear();
            var angebote = GoobayState.Angebote(_host.Store, Heute);
            if (angebote.Count == 0)
            {
                _list.Add(CounterPanel.Empty(t.T("goobay.kein_angebot"), null));
                return;
            }
            foreach (var a in angebote)
            {
                var local = a;
                var def = FurnitureCatalog.Def(a.Item);
                var row = Ux.Row("gb-goobay-item");
                row.name = "goobay-item-" + a.Item;
                if (_session != null && _session.Item == a.Item && !GoobayLogic.IstBeendet(_session)) row.AddToClassList("gb-goobay-item--active");
                var thumb = Ux.El("gb-craft-thumb");
                thumb.Add(Ux.ItemThumb(a.Item, "gift"));
                row.Add(thumb);
                var col = Ux.Col();
                col.style.flexGrow = 1; col.style.flexShrink = 1; col.style.marginLeft = 10;
                col.Add(Ux.Text(def?.DisplayName(locale) ?? a.Item, "gb-bold"));
                col.Add(Ux.Text(t.T("nl.goobay.wert", ("wert", a.Wert)) + (a.Count > 1 ? "  ·  ×" + a.Count : ""), "gb-sm", "gb-mild"));
                row.Add(col);
                row.RegisterCallback<ClickEvent>(_ => StartHaggle(local));
                _list.Add(row);
            }
        }

        private void StartHaggle(GoobayState.Angebot a)
        {
            if (_session != null && !GoobayLogic.IstBeendet(_session) && _session.Item == a.Item) return;
            var t = _host.Strings;
            var demand = GoobayState.Nachfrage(_host.Store, a.Kategorie, Heute, _rng);
            _session = GoobayLogic.Start(a.Item, a.Wert, demand, _rng);
            _buyer = Kaeufer[_rng.RangeI(0, Kaeufer.Length - 1)];
            _buyerName.text = t.T("goobay.kaeufer", ("name", _buyer));
            _log.Clear();
            if (demand >= 1.15) Bubble(null, t.T("nl.goobay.gefragt"), info: true);
            Bubble(_buyer, t.T("goobay.angebot", ("preis", _session.Angebot)));
            Sfx.PlayUi("soft_toast");
            RefreshList();
            RefreshButtons();
            Debug.Log("[goobay] start " + a.Item + " offer=" + _session.Angebot + " demand=" + demand.ToString("0.00"));
        }

        private void Bubble(string who, string text, bool mine = false, bool info = false)
        {
            var b = Ux.Col("gb-goobay-bubble");
            if (mine) b.AddToClassList("gb-goobay-bubble--mine");
            if (info) b.AddToClassList("gb-goobay-bubble--info");
            var label = Ux.Text(text, "gb-goobay-bubble-text");
            label.style.whiteSpace = WhiteSpace.Normal;
            b.Add(label);
            _log.Add(b);
            _juice.ScalePop(b, 1.06f, 0.18f);
            _log.schedule.Execute(() => (_log.parent as ScrollView ?? _log.parent?.parent as ScrollView)?.ScrollTo(b)).StartingIn(30);
        }

        private void RefreshButtons()
        {
            var t = _host.Strings;
            _buttons.Clear();
            _mood.Clear();
            if (_session == null) return;
            for (var i = 0; i <= GoobayLogic.StimmungMax; i++)
            {
                var dot = Ux.El("gb-goobay-mood-dot");
                if (i < GoobayLogic.StimmungMax + 1 - _session.Stimmung)
                    dot.AddToClassList(_session.Stimmung <= 1 ? "gb-goobay-mood-dot--gut" : _session.Stimmung <= 2 ? "gb-goobay-mood-dot--mittel" : "gb-goobay-mood-dot--sauer");
                _mood.Add(dot);
            }
            switch (_session.Status)
            {
                case GoobayLogic.StatusOffen:
                case GoobayLogic.StatusFinal:
                    if (_session.Status == GoobayLogic.StatusOffen) AddButton("goobay-hoeher", t.T("goobay.hoeher"), "arrow_up", Hoeher, "gb-btn-secondary");
                    AddButton("goobay-deal", t.T("goobay.deal"), "check", Deal, "gb-btn-confirm");
                    AddButton("goobay-abbrechen", t.T("goobay.abbrechen"), "close", Abbrechen, "gb-btn-cancel");
                    break;
                case GoobayLogic.StatusDeal when _session.Erloes > 0 && !_paid:
                    AddButton("goobay-abholung", t.T("goobay.abholung"), "home", () => Finish(false), "gb-btn-confirm");
                    AddButton("goobay-versand", t.T("goobay.versand", ("bonus", GoobayLogic.PostBonus(_session.Erloes) - _session.Erloes)), "mail", () => Finish(true), "gb-btn-primary");
                    break;
            }
        }

        private bool _paid;

        private void AddButton(string name, string label, string icon, System.Action run, string style)
        {
            var b = Ux.IconTextBtn(icon, label, run, style, "gb-btn-sm");
            b.name = name;
            _buttons.Add(b);
        }

        private void Hoeher()
        {
            var t = _host.Strings;
            Bubble(null, t.T("goobay.antwort_hoeher"), mine: true);
            GoobayLogic.Hoeher(_session, _rng);
            switch (_session.Status)
            {
                case GoobayLogic.StatusOffen: Bubble(_buyer, t.T("goobay.nachbessern", ("preis", _session.Angebot))); Sfx.PlayUi("soft_chip"); break;
                case GoobayLogic.StatusFinal: Bubble(_buyer, t.T("goobay.letztes", ("preis", _session.Angebot))); Sfx.PlayUi("soft_toast"); break;
                default:
                    Bubble(_buyer, t.T("goobay.geplatzt"));
                    Bubble(null, t.T("goobay.geplatzt_info", ("name", _buyer)), info: true);
                    GoobayState.AbbruchMerken(_host.Store, _session.Item, FurnitureCatalog.Def(_session.Item)?.Kategorie ?? "deko", Heute);
                    Sfx.PlayUi("soft_error");
                    RefreshList();
                    break;
            }
            RefreshButtons();
        }

        private void Deal()
        {
            GoobayLogic.Annehmen(_session);
            _paid = false;
            Bubble(null, _host.Strings.T("goobay.deal"), mine: true);
            Sfx.PlayUi("soft_confirm");
            RefreshButtons();
        }

        private void Abbrechen()
        {
            GoobayLogic.Abbrechen(_session);
            Bubble(null, _host.Strings.T("goobay.abbrechen"), mine: true);
            _session = null;
            _buyerName.text = _host.Strings.T("nl.goobay.waehle");
            RefreshList();
            RefreshButtons();
        }

        private void Finish(bool versand)
        {
            var t = _host.Strings;
            var paid = GoobayState.DealAbschliessen(_host.Store, _session.Item, _session.Erloes, versand);
            _paid = true;
            if (paid <= 0) { Sfx.PlayUi("soft_error"); RefreshList(); RefreshButtons(); return; }
            Bubble(null, t.T("goobay.verkauft", ("preis", paid)), info: true);
            Bubble(null, versand ? t.T("nl.goobay.versand_ok") : t.T("goobay.abholung_laeuft", ("name", _buyer)), info: true);
            Sfx.Play("soft_buy");
            var from = _buttons.worldBound.center;
            _host.Ui.FlyIcons(from, _ui.Coins.parent, "coin", Mathf.Clamp(paid / 10, 3, 10));
            _host.Ui.Confetti(_host.Settings.ReducedMotion ? 10 : 36);
            Debug.Log("[goobay] sold " + _session.Item + " for " + paid + (versand ? " (post)" : ""));
            _session = null;
            RefreshList();
            RefreshButtons();
        }

        public override void Update() => _juice.Tick(Time.unscaledDeltaTime);
    }
}
