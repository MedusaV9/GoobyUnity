using Gooby.Core.City;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The airport's travel desk (flughafen.gd + reise_logic.gd): nine destinations with price, days and souvenir
    /// coins to book; a booking prints the boarding pass (<see cref="BoardingScreen"/>, GOOBY FREE until boarding
    /// closes), while Gooby is away the desk shows his trip, once he is back it hands him over with the souvenirs and
    /// postcards (Travel); the day after a pickup it points to the souvenir window in GOOBY FREE.
    /// </summary>
    public sealed class TravelScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private CounterPanel.Parts _ui;
        private string _shownKey = "";
        private float _tick;
        private FlapBoardView _board;
        private int[] _cols;
        private readonly System.Collections.Generic.Dictionary<string, (int row, Button book, VisualElement stamp)> _boardRows =
            new System.Collections.Generic.Dictionary<string, (int, Button, VisualElement)>();

        public TravelScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "counter-travel";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _ui = CounterPanel.Build(root, _host, "compass", new Color(0.8f, 0.9f, 1f), t.T("travel.titel"), t.T("nl.reise.hinweis"), () => _host.Ui.Pop());
            _host.Store.CoinsChanged += OnCoins;
            Refresh();
        }

        public override void OnHidden() => _host.Store.CoinsChanged -= OnCoins;

        /// <summary>Back from the pass or GOOBY FREE: listen again and show what the purchases changed.</summary>
        public override void OnShown()
        {
            _host.Store.CoinsChanged -= OnCoins;
            _host.Store.CoinsChanged += OnCoins;
            _ui.Coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));
            Refresh();
        }

        private void OnCoins(int coins) { _ui.Coins.text = Ux.FormatNumber(coins); Refresh(); }

        /// <summary>What the desk shows depends on the clock too (boarding minutes, the end of the souvenir window).</summary>
        private string ShownKey(long now)
        {
            var state = _host.Store.State;
            return Travel.Phase(_host.Store, now) + "|" + GoobyFree.BoardingRestMin(state, now) + "|" + Urlaub.FensterRestH(state, now);
        }

        public override void Update()
        {
            _board?.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            _tick = 2f;
            if (ShownKey(_host.Store.Clock.NowMs()) != _shownKey) Refresh();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            _ui.Body.Clear();
            var now = _host.Store.Clock.NowMs();
            _shownKey = ShownKey(now);
            var phase = Travel.Phase(_host.Store, now);
            var slice = Vacation.SliceOf(_host.Store.State);
            var pass = GoobyFree.AmGate(_host.Store.State, now) ? GoobyFree.Karte(_host.Store.State) : null;
            if (pass != null)
            {
                // booked, not yet on board: Gooby waits at the gate with his pass (and GOOBY FREE is open)
                var gate = CounterPanel.Card("plane", new Color(0.8f, 0.9f, 1f), t.T("nl.reise.am_gate", ("gate", pass.Gate)),
                    t.T("nl.reise.am_gate_rest", ("min", GoobyFree.BoardingRestMin(_host.Store.State, now)), ("flug", pass.Flug)), null,
                    t.T("nl.reise.zur_karte"), true, () => _host.Ui.Push(new BoardingScreen(_host)), "gb-btn-primary");
                gate.name = "trip-gate";
                gate.Q<Button>().name = "trip-gate-btn";
                _ui.Body.Add(gate);
                return;
            }
            if (phase != Vacation.PhaseNone)
            {
                // away or back: one card for the trip in progress / the pickup
                var ziel = t.T("travel.ziel." + Gooby.Core.Json.J.Str(slice["destId"]));
                var back = phase != Vacation.PhaseAway;
                var card = CounterPanel.Card("compass", new Color(1f, 0.93f, 0.7f), back ? t.T("travel.abholen.titel") : t.T("nl.reise.weg", ("ziel", ziel)),
                    back ? (phase == Vacation.PhaseOverdue ? t.T("travel.abholen.overdue") : t.T("travel.confirm.nutzen")) : t.T("nl.reise.weg_rest", ("tage", Travel.RestTage(_host.Store, now))),
                    null, back ? t.T("nl.reise.abholen") : null, back, PickUp, "gb-btn-confirm");
                card.name = "trip-card";
                _ui.Body.Add(card);
                if (Urlaub.Verfuegbar(_host.Store.State, now))
                {
                    var visit = CounterPanel.Card("heart", new Color(1f, 0.85f, 0.9f), t.T("nl.reise.besuchen"),
                        t.T("urlaub.titel." + Urlaub.ArchetypFuer(Gooby.Core.Json.J.Str(slice["destId"]))), null,
                        t.T("urlaub.knopf.besuchen"), true, Visit, "gb-btn-primary");
                    visit.name = "trip-visit";
                    _ui.Body.Add(visit);
                }
                return;
            }
            // 24 hours after a pickup the other two souvenirs of the trip wait in GOOBY FREE (heimkehr_logik.gd window)
            if (Urlaub.FensterSortiment(_host.Store.State, now).Count > 0)
            {
                var ziel = t.T("travel.ziel." + Gooby.Core.Json.J.Str(slice["heimkehrZiel"]));
                var teaser = CounterPanel.Card("gift", GoobyFreeScreen.Tint,
                    t.T("nl.reise.gfree_teaser", ("ziel", ziel), ("stunden", Urlaub.FensterRestH(_host.Store.State, now))), t.T("gfree.hinweis"), null,
                    t.T("nl.reise.gfree_hin"), true, () => _host.Ui.Push(new GoobyFreeScreen(_host)), "gb-btn-primary");
                teaser.Add(CounterPanel.Tag(t.T("heimkehr.gfree.exklusiv"), "gb-ware-tag--rosa"));
                teaser.name = "trip-gfree";
                teaser.Q<Button>().name = "trip-gfree-btn";
                _ui.Body.Add(teaser);
            }
            BuildDepartures(Vacation.NormalizeVisited(slice["visited"]));
        }

        /// <summary>
        /// The departure board (flap_board.gd in reise_app.gd): the Weltengooby progress over a split-flap board with one
        /// row per destination — Ziel | Tage · Preis | a gag status (ZU TEUER beats it) — a visited stamp and a booking
        /// pill at its end. The board outlives the refreshes, so only rows whose text changed clatter again.
        /// </summary>
        private void BuildDepartures(Newtonsoft.Json.Linq.JObject visited)
        {
            var t = _host.Strings;
            var coins = ShopLogic.Coins(_host.Store);
            var seen = 0;
            foreach (var zielId in Travel.Ziele) if (Gooby.Core.Json.J.IsTrue(visited[zielId])) seen++;
            if (_board == null)
            {
                var width = _ui.Body.layout.width;
                var budget = float.IsNaN(width) || width < 1f ? 49 : Mathf.FloorToInt((width - BoardTrailPx) / FlapBoardView.CellPitch);
                _cols = budget >= 49 ? new[] { 17, 17, 15 } : FlapBoard.SpaltenFuerBudget(budget);
                _board = new FlapBoardView(t.T("reisepass.tafel.titel"), _cols, t.T("reisepass.tafel.ziel"), t.T("reisepass.tafel.abflug"), t.T("reisepass.tafel.status"));
                _boardRows.Clear();
                foreach (var zielId in Travel.Ziele)
                {
                    var local = zielId;
                    var trail = Ux.Row();
                    var stamp = Ux.El("gb-flap-stamp");
                    stamp.Add(Ux.Icon("check"));
                    trail.Add(stamp);
                    var book = Ux.Btn(t.T("nl.reise.buchen_kurz"), () => Book(local), "gb-btn-primary", "gb-btn-sm");
                    book.name = "trip-book-" + zielId;
                    trail.Add(book);
                    _boardRows[zielId] = (_board.AddRow("trip-" + zielId, trail), book, stamp);
                }
                _board.Root.RegisterCallback<GeometryChangedEvent>(_ => FitBoard());
            }
            var holder = Ux.Row("gb-flap-holder");
            holder.Add(_board.Root);
            _ui.Body.Add(holder);
            _board.SetNote(NoEmoji(t.T("g4travel.ziele.fortschritt", ("n", seen), ("max", Travel.Ziele.Length))));
            foreach (var zielId in Travel.Ziele)
            {
                var info = Travel.Info(zielId, coins);
                if (info == null || !_boardRows.TryGetValue(zielId, out var row)) continue;
                // the tiny superscript ᴳ reads as a degree sign on a flap: the board spells the coin as a plain G
                var abflug = t.T("reisepass.tafel.abflug_wert", ("tage", info.Tage), ("preis", info.Preis)).Replace("ᴳ", "G");
                var text = FlapBoard.Zeile(t.T(info.NameKey), abflug, t.T(FlapBoard.StatusKey(zielId, info.KannZahlen)), _cols);
                _board.SetText(row.row, text, animate: !_host.Settings.ReducedMotion);
                row.book.SetEnabled(info.KannZahlen);
                row.stamp.style.visibility = Gooby.Core.Json.J.IsTrue(visited[zielId]) ? Visibility.Visible : Visibility.Hidden;
            }
        }

        /// <summary>Room the booking pill and the stamp need beside the flaps (board padding included).</summary>
        private const float BoardTrailPx = 210f;

        /// <summary>A board built before the sheet knew its width is rebuilt narrower once it would overflow.</summary>
        private void FitBoard()
        {
            if (_board == null || _cols == null) return;
            var width = _ui.Body.layout.width;
            if (float.IsNaN(width) || width < 1f) return;
            var need = (_cols[0] + _cols[1] + _cols[2]) * FlapBoardView.CellPitch + BoardTrailPx;
            if (need <= width + 1f || _cols[1] == 0) return;
            _board = null;
            Refresh();
        }

        /// <summary>Nunito has no emoji glyphs: a leading globe would print as a box.</summary>
        private static string NoEmoji(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsSurrogate(text[i])) continue;
                sb.Append(text[i]);
            }
            return sb.ToString().Trim();
        }

        private void Book(string zielId)
        {
            var t = _host.Strings;
            if (Travel.Buchen(_host.Store, zielId, _host.Store.Clock.NowMs()))
            {
                _ui.Hint.text = t.T("nl.reise.gebucht");
                Sfx.PlayUi("soft_buy");
                _host.Ui.Push(new BoardingScreen(_host));
            }
            else _ui.Hint.text = t.T("nl.reise.zu_teuer");
            Refresh();
        }

        private void Visit()
        {
            var dest = Gooby.Core.Json.J.Str(Vacation.SliceOf(_host.Store.State)["destId"]);
            Sfx.PlayUi("soft_open");
            _host.Ui.FadeThrough(() => _host.Ui.Push(new VacationScreen(_host, dest)));
        }

        private void PickUp()
        {
            var t = _host.Strings;
            var r = Travel.Abholen(_host.Store, _host.Store.Clock.NowMs());
            if (r.Ok)
            {
                _ui.Hint.text = t.T("nl.reise.abgeholt", ("coins", r.SouvenirCoins), ("karten", r.Postkarten));
                _host.Ui.Push(new TravelCutsceneScreen(_host, TravelCutscene.ModusRueckkehr));
            }
            Refresh();
        }
    }
}
