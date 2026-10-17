using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Dlc;
using Gooby.Core.Json;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The DLC library (port of dlc_screen.gd, W14/DLCHUB): big cover cards with name, a ribbon (NEU / BALD / INSTALLIERT)
    /// and a spoiler-free teaser; a tap opens the detail with the features, the unlock line and the action — McGooby's
    /// daily trial shift, buying the restaurant (from level 14) and its own shifts; Goo und Bye sells Uncle Alwin's key
    /// (from level 12) and then unlocks the corner store; the ranch leads to its games.
    /// Reached from Settings → Spiel → DLC.
    /// </summary>
    public sealed class DlcScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _list, _detail;

        public DlcScreen(GameHost host)
        {
            _host = host;
            Id = "dlc";
        }

        private static JArray Items() => ContentSource.LoadItems("content/dlc/data/dlcs");

        private string Loc => _host.Strings.Locale;

        private static string Field(JObject d, string f, string loc) => J.Str(d[f + "_" + loc], J.Str(d[f + "_de"]));

        private static bool Playable(string id) => id == "mcgooby" || id == "ranch" || id == "goo_und_bye";

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 90; sheet.style.right = 90;
            sheet.style.maxHeight = Length.Percent(90);
            var head = CounterPanel.Head("gift", new Color(1f, 0.9f, 0.75f), t.T("dlc.titel"), t.T("dlc.intro"), Close);
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _list = Ux.Row("gb-dlc-list");
            scroll.Add(_list);
            body.Add(scroll);
            sheet.Add(body);
            root.Add(sheet);
            foreach (var d in Items().OfType<JObject>())
            {
                var item = d;
                var id = J.Str(d["id"]);
                var card = Ux.Col("gb-card", "gb-dlc-card");
                card.name = "dlc-" + id;
                var cover = Ux.El("gb-dlc-cover");
                var tex = Resources.Load<Texture2D>("gooby-ui/Covers/dlc_" + id);
                if (tex != null) cover.style.backgroundImage = new StyleBackground(tex);
                var ribbon = Ux.Text(Ribbon(id), "gb-dlc-ribbon");
                cover.Add(ribbon);
                card.Add(cover);
                card.Add(Ux.Text(J.Str(d["name"]), "gb-h3"));
                var teaser = Ux.Text(Field(d, "teaser", Loc), "gb-sm", "gb-mild");
                teaser.style.whiteSpace = WhiteSpace.Normal;
                card.Add(teaser);
                var look = Ux.IconTextBtn("chevron", t.T("dlc.knopf.ansehen"), () => ShowDetail(item), "gb-btn-secondary", "gb-btn-sm");
                look.name = "dlc-open-" + id;
                look.style.marginTop = 8;
                card.Add(look);
                card.RegisterCallback<ClickEvent>(e => { if (!(e.target is Button)) ShowDetail(item); });
                _list.Add(card);
            }
        }

        private string Ribbon(string id)
        {
            var t = _host.Strings;
            if (!Playable(id)) return t.T("dlc.ribbon.bald");
            if (id == "mcgooby" && McGoobyState.IstGekauft(_host.Store.State)) return t.T("dlc.ribbon.installiert");
            if (id == "goo_und_bye" && GoobyeState.IstGekauft(_host.Store.State)) return t.T("dlc.ribbon.installiert");
            return t.T("dlc.ribbon.neu");
        }

        private void ShowDetail(JObject d)
        {
            var t = _host.Strings;
            var id = J.Str(d["id"]);
            _detail?.RemoveFromHierarchy();
            Sfx.PlayUi("soft_open");
            _detail = Ux.El("gb-scrim", "gb-dlc-detail-veil");
            _detail.name = "dlc-detail";
            _detail.RegisterCallback<ClickEvent>(e => { if (e.target == _detail) CloseDetail(); });
            var card = Ux.Col("gb-card", "gb-dlc-detail");
            var cover = Ux.El("gb-dlc-cover", "gb-dlc-cover--big");
            var tex = Resources.Load<Texture2D>("gooby-ui/Covers/dlc_" + id);
            if (tex != null) cover.style.backgroundImage = new StyleBackground(tex);
            card.Add(cover);
            card.Add(Ux.Text(J.Str(d["name"]), "gb-h2"));
            card.Add(Ux.Text(t.T("dlc.features_titel"), "gb-md", "gb-bold"));
            foreach (var f in (d["features_" + Loc] as JArray ?? d["features_de"] as JArray ?? new JArray()))
                card.Add(Ux.Text("• " + J.Str(f), "gb-md"));
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var hint = Ux.Text("", "gb-sm", "gb-mild");
            hint.name = "dlc-hint";
            hint.style.whiteSpace = WhiteSpace.Normal;
            if (id == "mcgooby") McGoobyActions(d, actions, hint);
            else if (id == "goo_und_bye") GoobyeActions(d, actions, hint);
            else if (id == "ranch")
            {
                var go = Ux.IconTextBtn("horseshoe", t.T("dlc.knopf.zur_ranch"), () => { Close(); _host.Ui.Push(new RanchHofScreen(_host)); }, "gb-btn-primary");
                go.name = "dlc-action";
                actions.Add(go);
            }
            else hint.text = t.T("dlc.bald_hinweis");
            card.Add(hint);
            var back = Ux.Btn(t.T("dlc.zurueck"), CloseDetail, "gb-btn-secondary");
            back.style.marginRight = 10;
            actions.Insert(0, back);
            card.Add(actions);
            _detail.Add(card);
            Root.Add(_detail);
        }

        private void McGoobyActions(JObject d, VisualElement actions, Label hint)
        {
            var t = _host.Strings;
            var state = _host.Store.State;
            var today = System.DateTimeOffset.FromUnixTimeMilliseconds(_host.Store.Clock.NowMs()).ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var bought = McGoobyState.IstGekauft(state);
            var unlock = Field(d, "unlock", Loc).Replace("{level}", McGoobyKatalog.FreischaltLevel().ToString()).Replace("{preis}", McGoobyKatalog.Preis().ToString());
            if (!bought)
            {
                hint.text = t.T("dlc.unlock_titel") + ": " + unlock + "\n" + t.T("dlc_mcgooby.angebot.demo");
                var buy = Ux.IconTextBtn("coin", t.T("dlc_mcgooby.angebot.kaufen"), Buy, McGoobyState.Check(state) == McGoobyState.KaufOk ? "gb-btn-confirm" : "gb-btn-secondary");
                buy.name = "dlc-buy";
                buy.style.marginRight = 10;
                actions.Add(buy);
            }
            else hint.text = t.T("dlc_mcgooby.fortschritt.rang", ("sterne", new string('★', McGoobyState.Sterne(state)) + new string('☆', McGoobyState.MaxSterne - McGoobyState.Sterne(state))));
            var can = McGoobyState.SchichtErlaubt(state, today);
            var play = Ux.IconTextBtn("food_burger", t.T(bought ? "dlc_mcgooby.knopf.schicht" : "dlc_mcgooby.knopf.probeschicht"), () => Play(can), can ? "gb-btn-primary" : "gb-btn-secondary");
            play.name = "dlc-action";
            actions.Add(play);
            if (!can) hint.text = t.T("dlc_mcgooby.demo_verbraucht");
        }

        /// <summary>Goo und Bye: Uncle Alwin's key from level 12 (bought here), then "Laden aufschließen!" into the store.</summary>
        private void GoobyeActions(JObject d, VisualElement actions, Label hint)
        {
            var t = _host.Strings;
            var state = _host.Store.State;
            if (!GoobyeState.IstGekauft(state))
            {
                var unlock = Field(d, "unlock", Loc).Replace("{level}", GoobyeKatalog.FreischaltLevel().ToString()).Replace("{preis}", GoobyeKatalog.Preis().ToString());
                hint.text = t.T("dlc.unlock_titel") + ": " + unlock + "\n" + t.T("dlc_goobye.angebot.frage");
                var buy = Ux.IconTextBtn("coin", t.T("dlc_goobye.angebot.kaufen"), BuyGoobye, GoobyeKauf.Check(state) == GoobyeKauf.Ok ? "gb-btn-confirm" : "gb-btn-secondary");
                buy.name = "dlc-buy";
                actions.Add(buy);
                return;
            }
            var lvl = GoobyeLevel.LevelFuer(state);
            hint.text = t.T("dlc_goobye.laden.level", ("name", t.T(GoobyeLevel.NameKey(lvl))), ("level", lvl));
            var go = Ux.IconTextBtn("shop", t.T("dlc_goobye.knopf.zum_laden"), OpenGoobye, "gb-btn-primary");
            go.name = "dlc-action";
            actions.Add(go);
        }

        private void OpenGoobye()
        {
            Close();
            Sfx.PlayUi("soft_confirm");
            _host.Ui.FadeThrough(() => _host.Ui.Push(new GoobyeScreen(_host)));
        }

        private void BuyGoobye()
        {
            var t = _host.Strings;
            var result = GoobyeKauf.Pleite;
            var now = _host.Store.Clock.NowMs();
            _host.Store.Update(s => result = GoobyeKauf.Kaufe(s, now), GameHost.TicksMs);
            if (result == GoobyeKauf.Ok)
            {
                _host.Store.NotifySliceChanged(GoobyeState.SliceId);
                _host.Store.NotifySliceChanged("economy");
                Sfx.PlayUi("soft_buy");
                _host.Ui.Celebrate(t.T("dlc_goobye.titel"), t.T("dlc_goobye.angebot.titel"), "shop");
                Debug.Log("[goobye] gekauft (dlc)");
                OpenGoobye();
                return;
            }
            Sfx.PlayUi("soft_error");
            var hint = _detail?.Q<Label>("dlc-hint");
            if (hint != null)
                hint.text = result == GoobyeKauf.Gesperrt
                    ? t.T("dlc.gesperrt_hinweis", ("aktuell", J.Int(J.Path(_host.Store.State, "progression", "level"), 1)))
                    : t.T("dlc_goobye.angebot.zu_wenig", ("preis", GoobyeKatalog.Preis()));
        }

        private void Play(bool can)
        {
            if (!can) { Sfx.PlayUi("soft_error"); return; }
            Close();
            Sfx.PlayUi("soft_confirm");
            _host.Ui.FadeThrough(() => _host.Ui.Push(new McGoobyScreen(_host)));
        }

        private void Buy()
        {
            var t = _host.Strings;
            var result = McGoobyState.KaufPleite;
            _host.Store.Update(s => result = McGoobyState.Kaufe(s, _host.Store.Clock.NowMs()), GameHost.TicksMs);
            var hint = _detail?.Q<Label>("dlc-hint");
            if (result == McGoobyState.KaufOk)
            {
                _host.Store.NotifySliceChanged(McGoobyState.SliceId);
                Sfx.PlayUi("soft_buy");
                _host.Ui.Celebrate(t.T("dlc_mcgooby.titel"), t.T("dlc_mcgooby.angebot.titel"), "food_burger");
                Debug.Log("[mcgooby] gekauft");
                CloseDetail();
                Root.Clear();
                Build(Root);
                return;
            }
            Sfx.PlayUi("soft_error");
            if (hint != null)
                hint.text = result == McGoobyState.KaufGesperrt
                    ? t.T("dlc.gesperrt_hinweis", ("aktuell", J.Int(J.Path(_host.Store.State, "progression", "level"), 1)))
                    : t.T("dlc_mcgooby.angebot.zu_wenig", ("preis", McGoobyKatalog.Preis()));
        }

        private void CloseDetail()
        {
            _detail?.RemoveFromHierarchy();
            _detail = null;
        }

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }
    }
}
