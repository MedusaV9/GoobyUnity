using System;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The boarding pass after a booking at the travel desk — Unity's stand-in for the taxi wait before a Godot departure
    /// (flughafen.gd): GOOBY AIR from Sonnental to the destination, Gooby as the passenger, flight, gate, seat, boarding
    /// time and a barcode, all from the booking (<see cref="GoobyFree.Karte"/>). While boarding is open the GOOBY FREE
    /// duty-free is one tap away; "Einsteigen" — or the last call when boarding closes — starts the departure reel.
    /// </summary>
    public sealed class BoardingScreen : UiScreen
    {
        private readonly GameHost _host;
        private GoobyFree.Bordkarte _pass;
        private Label _rest;
        private Button _gfree;
        private bool _boarded;
        private float _tick;

        public BoardingScreen(GameHost host)
        {
            _host = host;
            Id = "boarding";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _pass = GoobyFree.Karte(_host.Store.State) ?? new GoobyFree.Bordkarte();
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var panel = Ux.Col("gb-board");
            panel.name = "boarding-pass";
            panel.pickingMode = PickingMode.Ignore;
            Ux.Abs(panel, 120, 30, 120, 30);

            var ticket = Ux.Row("gb-pass");
            var main = Ux.Col("gb-pass-main");
            var band = Ux.Row("gb-pass-band");
            band.Add(Ux.Icon("plane", "gb-pass-band-icon"));
            band.Add(Ux.Text(t.T("nl.bordkarte.airline"), "gb-pass-airline"));
            band.Add(Ux.El("gb-pass-spacer"));
            band.Add(Ux.Text(t.T("nl.bordkarte.kopf"), "gb-pass-kind"));
            var close = Ux.IconBtn("close", () => _host.Ui.Pop(), "gb-win-close", "gb-pass-close");
            close.name = "sheet-close";
            band.Add(close);
            main.Add(band);

            var route = Ux.Row("gb-pass-route");
            route.Add(Airport(_pass.Von, t.T("nl.bordkarte.von"), "boarding-from"));
            var flight = Ux.Col("gb-pass-flight");
            flight.Add(Ux.Icon("plane", "gb-pass-plane"));
            var dots = Ux.Row("gb-pass-dots");
            for (var i = 0; i < 9; i++) dots.Add(Ux.El("gb-pass-dot"));
            flight.Add(dots);
            route.Add(flight);
            route.Add(Airport(_pass.Nach, _pass.ZielId.Length > 0 ? t.T("travel.ziel." + _pass.ZielId) : "", "boarding-to"));
            main.Add(route);

            var fields = Ux.Row("gb-pass-fields");
            fields.Add(Field(t.T("nl.bordkarte.passagier"), J.Str(J.Path(_host.Store.State, "meta", "goobyNickname"), "Gooby").ToUpperInvariant()));
            fields.Add(Field(t.T("nl.bordkarte.flug"), _pass.Flug));
            fields.Add(Field(t.T("nl.bordkarte.gate"), _pass.Gate));
            fields.Add(Field(t.T("nl.bordkarte.sitz"), _pass.Sitz));
            fields.Add(Field(t.T("nl.bordkarte.boarding"), ClockText(_pass.BoardingBis)));
            main.Add(fields);
            ticket.Add(main);

            var perf = Ux.Col("gb-pass-perf");
            for (var i = 0; i < 14; i++) perf.Add(Ux.El("gb-pass-perf-dot"));
            ticket.Add(perf);
            var stub = Ux.Col("gb-pass-stub");
            stub.Add(Ux.Text(t.T("nl.bordkarte.airline"), "gb-pass-stub-air"));
            stub.Add(Ux.Text(_pass.Von + " → " + _pass.Nach, "gb-pass-stub-route"));
            stub.Add(Ux.Text(_pass.Sitz, "gb-pass-stub-seat"));
            stub.Add(Barcode(_pass.Flug + "|" + _pass.Gate + "|" + _pass.Sitz + "|" + _pass.GebuchtAt));
            stub.Add(Ux.Text(_pass.Flug + " · " + _pass.Gate, "gb-pass-stub-flight"));
            ticket.Add(stub);
            panel.Add(ticket);

            _rest = Ux.Text("", "gb-pass-rest");
            _rest.name = "boarding-rest";
            panel.Add(_rest);
            var actions = Ux.Row("gb-pass-actions");
            _gfree = Ux.IconTextBtn("gift", t.T("gfree.knopf"), () => _host.Ui.Push(new GoobyFreeScreen(_host)), "gb-btn-secondary");
            _gfree.name = "boarding-gfree";
            actions.Add(_gfree);
            var board = Ux.IconTextBtn("plane", t.T("nl.bordkarte.einsteigen"), Board, "gb-btn-primary");
            board.name = "boarding-board";
            actions.Add(board);
            panel.Add(actions);
            root.Add(panel);
            Refresh();
            Debug.Log("[boarding] pass " + _pass.Von + "→" + _pass.Nach + " " + _pass.Flug + " gate " + _pass.Gate + " seat " + _pass.Sitz);
        }

        private static VisualElement Airport(string code, string city, string name)
        {
            var col = Ux.Col("gb-pass-airport");
            col.name = name;
            col.Add(Ux.Text(code, "gb-pass-code"));
            col.Add(Ux.Text(city, "gb-pass-city"));
            return col;
        }

        private static VisualElement Field(string label, string value)
        {
            var col = Ux.Col("gb-pass-field");
            col.Add(Ux.Text(label, "gb-pass-label"));
            col.Add(Ux.Text(value, "gb-pass-value"));
            return col;
        }

        private static string ClockText(long ms) => ms <= 0 ? "--:--" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm");

        /// <summary>Bars of two widths with gaps, read off a hash of the pass (the same pass always prints the same code).</summary>
        private static VisualElement Barcode(string text)
        {
            var row = Ux.Row("gb-pass-barcode");
            var h = 2166136261u;
            foreach (var c in text) { h ^= c; h *= 16777619u; }
            for (var i = 0; i < 30; i++)
            {
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                var bar = Ux.El("gb-pass-bar");
                bar.style.width = new Length((h & 3) == 0 ? 5f : (h & 1) == 0 ? 3f : 2f, LengthUnit.Pixel);
                bar.style.marginRight = new Length((h & 8) == 0 ? 2f : 3f, LengthUnit.Pixel);
                bar.pickingMode = PickingMode.Ignore;
                row.Add(bar);
            }
            return row;
        }

        public override void Update()
        {
            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            _tick = 1f;
            Refresh();
            if (!GoobyFree.AmGate(_host.Store.State, _host.Store.Clock.NowMs())) Board();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            var state = _host.Store.State;
            var now = _host.Store.Clock.NowMs();
            var open = GoobyFree.AmGate(state, now);
            _rest.text = open ? t.T("nl.bordkarte.rest", ("min", GoobyFree.BoardingRestMin(state, now))) : t.T("nl.bordkarte.letzter_aufruf", ("flug", _pass.Flug));
            _gfree.SetEnabled(open);
        }

        /// <summary>On board: the pass goes, the departure reel plays over the travel desk (only once, and only on top).</summary>
        private void Board()
        {
            if (_boarded || _host.Ui.Current != this) return;
            _boarded = true;
            Sfx.PlayUi("soft_confirm");
            Debug.Log("[boarding] board " + _pass.Flug);
            _host.Ui.Pop();
            _host.Ui.Push(new TravelCutsceneScreen(_host, TravelCutscene.ModusAbflug));
        }
    }
}
