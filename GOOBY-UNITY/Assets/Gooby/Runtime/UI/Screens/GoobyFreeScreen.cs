using Gooby.Core.City;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// GOOBY FREE, the duty-free at the gate (flughafen.gd _gfree_inhalt): the souvenir window's two pieces first, then
    /// the five exclusive space pieces (their 3D thumbnails, "Nur hier!") and the travel snacks at airport prices. The
    /// note row under the title says how long boarding stays open (or the souvenir window) and answers every purchase;
    /// when the shop closes while it is open (boarding over) the sheet closes too.
    /// </summary>
    public sealed class GoobyFreeScreen : UiScreen
    {
        public static readonly Color Tint = new Color(1f, 0.89f, 0.55f);
        private static readonly Color SnackTint = new Color(1f, 0.9f, 0.82f);
        private static readonly Color SouvenirTint = new Color(1f, 0.85f, 0.92f);

        private readonly GameHost _host;
        private VisualElement _grid;
        private Label _coins, _note;
        private string _answer;
        private float _tick;

        public GoobyFreeScreen(GameHost host)
        {
            _host = host;
            Id = "gooby-free";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var panel = Ux.El("gb-win", "gb-gfree");
            Ux.Abs(panel, 120, 40, 120, 40);
            var head = CounterPanel.Head("gift", Tint, t.T("gfree.titel"), t.T("gfree.hinweis"), () => _host.Ui.Pop(), ShopLogic.Coins(_host.Store));
            _coins = head.Coins;
            panel.Add(head.Row);

            var content = Ux.Col("gb-win-body");
            var note = Ux.Row("gb-win-note");
            note.Add(Ux.IconTile("plane", new Color(0.8f, 0.9f, 1f)));
            _note = Ux.Text("", "gb-win-note-text");
            _note.name = "gfree-note";
            note.Add(_note);
            content.Add(note);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _grid = Ux.El("gb-win-grid");
            scroll.Add(_grid);
            content.Add(scroll);
            panel.Add(content);
            root.Add(panel);
            _host.Store.CoinsChanged += OnCoins;
            Rebuild();
        }

        public override void OnHidden() => _host.Store.CoinsChanged -= OnCoins;

        private void OnCoins(int coins)
        {
            if (_coins != null) _coins.text = Ux.FormatNumber(coins);
            Rebuild();
        }

        public override void Update()
        {
            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            _tick = 1f;
            var now = _host.Store.Clock.NowMs();
            if (!GoobyFree.Offen(_host.Store.State, now) && _host.Ui.Current == this)
            {
                Debug.Log("[gfree] closed while open → pop");
                _host.Ui.Pop();
                return;
            }
            _note.text = _answer ?? Status(now);
        }

        /// <summary>Boarding countdown at the gate; otherwise the souvenir window's destination and hours.</summary>
        private string Status(long now)
        {
            var t = _host.Strings;
            var state = _host.Store.State;
            var pass = GoobyFree.Karte(state);
            if (GoobyFree.AmGate(state, now) && pass != null)
                return t.T("nl.gfree.boarding", ("min", GoobyFree.BoardingRestMin(state, now)), ("flug", pass.Flug), ("gate", pass.Gate));
            var ziel = Gooby.Core.Json.J.Str(Gooby.Core.Life.Vacation.SliceOf(state)["heimkehrZiel"]);
            return t.T("heimkehr.gfree.hinweis", ("ziel", t.T("travel.ziel." + ziel)), ("stunden", Gooby.Core.Life.Urlaub.FensterRestH(state, now)));
        }

        private string NameOf(GoobyFree.Ware w)
        {
            var t = _host.Strings;
            switch (w.Typ)
            {
                case GoobyFree.TypMoebel: return FurnitureCatalog.Def(w.Id)?.DisplayName(_host.Settings.Locale) ?? w.Id;
                case GoobyFree.TypMitbringsel: return HomecomingScreen.SouvenirName(t, w.MitbringselTyp, w.ZielId);
                default: return t.HasKey("rewards.food." + w.Id) ? t.T("rewards.food." + w.Id) : w.Id;
            }
        }

        private void Rebuild()
        {
            if (_grid == null) return;
            var t = _host.Strings;
            var now = _host.Store.Clock.NowMs();
            var coins = ShopLogic.Coins(_host.Store);
            _note.text = _answer ?? Status(now);
            _grid.Clear();
            foreach (var ware in GoobyFree.Angebot(_host.Store.State, now))
            {
                var w = ware;
                VisualElement card;
                switch (w.Typ)
                {
                    case GoobyFree.TypMoebel:
                        card = CounterPanel.Ware(Ux.ItemThumb(w.Id, "star"), Tint, NameOf(w), t.T("nl.gfree.deko_sub"), t.T("gfree.exklusiv"));
                        break;
                    case GoobyFree.TypMitbringsel:
                        card = CounterPanel.Ware(HomecomingScreen.SouvenirIcon(w.MitbringselTyp), SouvenirTint, NameOf(w),
                            t.T("nl.gfree.mitbringsel_sub", ("ziel", t.T("travel.ziel." + w.ZielId))), t.T("heimkehr.gfree.exklusiv"));
                        break;
                    default:
                        card = CounterPanel.Ware(CounterPanel.FoodIcon(w.Id, "leaf"), SnackTint, NameOf(w), t.T("nl.gfree.snack_sub"), null);
                        break;
                }
                card.name = "gfree-" + w.Id;
                var buy = CounterPanel.Buy(w.Preis, () => Buy(w), coins >= w.Preis);
                buy.name = "gfree-buy-" + w.Id;
                card.Add(buy);
                _grid.Add(card);
            }
        }

        private void Buy(GoobyFree.Ware ware)
        {
            var t = _host.Strings;
            var name = NameOf(ware);
            var result = GoobyFree.Zu;
            _host.Store.Update(s => result = GoobyFree.Kaufe(s, ware.Id, _host.Store.Clock.NowMs()), GameHost.TicksMs);
            switch (result)
            {
                case GoobyFree.Ok:
                    _host.Store.NotifySliceChanged("economy");
                    _host.Store.NotifySliceChanged(ware.Typ == GoobyFree.TypMoebel ? HomeState.SliceId : "inventory");
                    Sfx.PlayUi("soft_buy");
                    _answer = ware.Typ == GoobyFree.TypMitbringsel ? t.T("nl.reise.gfree_gekauft", ("name", name)) : t.T("gfree.gekauft", ("name", name));
                    break;
                case GoobyFree.Voll:
                    Sfx.PlayUi("soft_error");
                    _answer = t.T("gfree.lager_voll");
                    break;
                case GoobyFree.Pleite:
                    Sfx.PlayUi("soft_error");
                    _answer = t.T("nl.reise.gfree_pleite");
                    break;
                default:
                    Sfx.PlayUi("soft_error");
                    _answer = t.T("gfree.zu");
                    break;
            }
            Debug.Log("[gfree] buy " + ware.Id + " → " + result + ", coins " + ShopLogic.Coins(_host.Store));
            Rebuild();
        }
    }
}
