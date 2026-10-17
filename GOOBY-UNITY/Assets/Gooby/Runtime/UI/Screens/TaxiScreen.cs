using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The ride app of the phone (port of fahrdienst.gd / fahrdienst_app.gd): Taxi Goobsen &amp; Söhne (10 ᴳ, a short wait
    /// with its countdown) or GUBER (30 ᴳ, 45 at the evening rush, there at once), then a destination — home or any place of
    /// the town plan (closed ones say so). The car is paid when it arrives; the ride is a soft fade straight into the
    /// place (parked in front of the door, the interior open) or back into the living room. Out of energy, the taxi is the
    /// way home and costs nothing.
    /// </summary>
    public sealed class TaxiScreen : UiScreen
    {
        public const int TaxiCost = 10, GuberCost = 30, GuberSurgeCost = 45;
        public const float TaxiWaitSec = 4f, GuberWaitSec = 1.2f;

        private readonly GameHost _host;
        private string _service = "taxi";
        private VisualElement _list, _serviceRow, _ride;
        private Label _rideText, _pitch;
        private float _rideLeft = -1f;
        private DriveModel.Place _target;
        private bool _home;

        public TaxiScreen(GameHost host)
        {
            _host = host;
            Id = "taxi";
        }

        private double Hour
        {
            get
            {
                var now = _host.Store.Clock.NowMs();
                return CityMap.LokaleZeit(now, _host.Store.Clock.UtcOffsetMinutes(now)).stunde;
            }
        }

        private bool Exhausted => J.Num(J.Path(_host.Store.State, "gooby", "stats", "energy")) < 1.0;

        private int Cost(bool home) => home && Exhausted ? 0 : _service == "guber" ? (Hour >= 18 && Hour < 20 ? GuberSurgeCost : GuberCost) : TaxiCost;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => { if (_rideLeft < 0f) _host.Ui.Pop(); });
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 220; sheet.style.right = 220;
            sheet.style.maxHeight = Length.Percent(90);
            var head = CounterPanel.Head("car", new Color(1f, 0.9f, 0.78f), t.T("phone.taxi.slogan"), t.T("nl.taxi.untertitel"), () => { if (_rideLeft < 0f) _host.Ui.Pop(); });
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");
            _serviceRow = Ux.Row("gb-win-tabs");
            foreach (var (id, key) in new[] { ("taxi", "phone.app.taxi"), ("guber", "phone.app.guber") })
            {
                var local = id;
                var chip = new Button(() => { _service = local; Sfx.PlayUi("soft_chip"); Refresh(); }) { text = t.T(key), name = "taxi-service-" + id };
                chip.ClearClassList();
                chip.AddToClassList("gb-chip");
                chip.userData = id;
                chip.style.marginRight = 8;
                _serviceRow.Add(chip);
            }
            body.Add(_serviceRow);
            _pitch = Ux.Text("", "gb-md", "gb-mild");
            _pitch.style.whiteSpace = WhiteSpace.Normal;
            _pitch.style.marginTop = 6; _pitch.style.marginBottom = 10;
            body.Add(_pitch);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _list = Ux.El("gb-taxi-grid");
            scroll.Add(_list);
            body.Add(scroll);
            sheet.Add(body);
            root.Add(sheet);
            // the car is on its way: a card over the sheet with the countdown
            _ride = Ux.El("gb-card", "gb-taxi-ride");
            _ride.style.display = DisplayStyle.None;
            var car = Ux.IconTile("car", new Color(1f, 0.85f, 0.55f));
            _ride.Add(car);
            _rideText = Ux.Text("", "gb-lg", "gb-bold");
            _rideText.style.whiteSpace = WhiteSpace.Normal;
            _rideText.style.marginLeft = 14; _rideText.style.flexShrink = 1;
            _ride.Add(_rideText);
            root.Add(_ride);
            Refresh();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            foreach (var c in _serviceRow.Children())
                if (c is Button b && b.userData is string id) b.EnableInClassList("gb-chip--active", id == _service);
            var surge = _service == "guber" && Hour >= 18 && Hour < 20;
            _pitch.text = t.T(_service == "guber" ? "phone.guber.pitch" : "phone.taxi.pitch") + (surge ? "  " + t.T("phone.guber.surge") : "");
            _list.Clear();
            _list.Add(Destination("home", "home", t.T("nl.taxi.nach_hause"), null, true));
            var now = _host.Store.Clock.NowMs();
            var (wochentag, stunde) = CityMap.LokaleZeit(now, _host.Store.Clock.UtcOffsetMinutes(now));
            foreach (var place in DriveModel.Places)
            {
                if (place.Side > 0) continue;
                var open = CityMap.IstOffen(CityMap.Get(place.MapId), wochentag, stunde) || CityMap.ImmerBetretbar(place.MapId);
                _list.Add(Destination(place.Id, place.Icon, t.T(place.NameKey), place, open));
            }
        }

        private VisualElement Destination(string id, string icon, string name, DriveModel.Place place, bool open)
        {
            var t = _host.Strings;
            var home = place == null;
            var cost = Cost(home);
            var b = new Button(() => Order(place, open)) { text = "", name = "taxi-to-" + id };
            b.ClearClassList();
            b.AddToClassList("gb-taxi-dest");
            b.AddToClassList("gb-arcade-card");
            if (!open) b.AddToClassList("gb-taxi-dest--closed");
            b.Add(Ux.IconTile(icon, home ? new Color(1f, 0.86f, 0.86f) : new Color(1f, 0.95f, 0.85f)));
            var col = Ux.Col("gb-grow");
            col.style.marginLeft = 10; col.style.flexShrink = 1;
            col.Add(Ux.Text(name, "gb-md", "gb-bold"));
            col.Add(Ux.Text(open ? (cost == 0 ? t.T("nl.taxi.gratis") : cost + " ᴳ") : t.T("nl.taxi.zu"), "gb-sm", "gb-mild"));
            b.Add(col);
            return b;
        }

        private void Order(DriveModel.Place place, bool open)
        {
            var t = _host.Strings;
            if (_rideLeft >= 0f) return;
            if (!open) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(t.T("nl.taxi.zu")); return; }
            var home = place == null;
            var cost = Cost(home);
            var paid = true;
            if (cost > 0) _host.Store.Update(s => paid = Economy.Spend(J.EnsureDict(s, "economy"), cost, "fahrdienst"));
            if (!paid) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(t.T("nl.taxi.zu_teuer", ("preis", cost))); return; }
            Sfx.PlayUi("soft_buy");
            _target = place;
            _home = home;
            _rideLeft = _service == "guber" ? GuberWaitSec : TaxiWaitSec;
            _ride.style.display = DisplayStyle.Flex;
            _rideText.text = t.T(_service == "guber" ? "phone.guber.bestellt" : "phone.taxi.bestellt");
            Debug.Log("[taxi] " + _service + " to " + (home ? "home" : place.Id) + " for " + cost);
        }

        public override void Update()
        {
            if (_rideLeft < 0f) return;
            _rideLeft -= Time.unscaledDeltaTime;
            var t = _host.Strings;
            if (_rideLeft > 0f)
            {
                _rideText.text = t.T(_service == "guber" ? "phone.guber.unterwegs" : "phone.taxi.unterwegs") + " · " + Mathf.CeilToInt(_rideLeft) + " s";
                return;
            }
            _rideLeft = -1f;
            _rideText.text = t.T(_service == "guber" ? "phone.guber.fahrt" : "phone.taxi.fahrt");
            Sfx.Play("travel_whoosh_auf");
            var target = _target;
            var home = _home;
            _host.Ui.FadeThrough(() => Arrive(target, home), 0.5f);
        }

        private void Arrive(DriveModel.Place place, bool home)
        {
            _host.Ui.PopToRoot();
            if (home)
            {
                _host.World?.EnterRoom("living");
                return;
            }
            var drive = new CityDriveScreen(_host);
            _host.Ui.Push(drive);
            if (_host.World?.Drive != null) { _host.World.Drive.X = place.X; _host.World.Drive.Z = place.Z; _host.World.Drive.Kmh = 0.0; }
            drive.Braking = true;
            _host.Ui.Push(PlaceScreen.For(_host, place));
            Debug.Log("[taxi] arrived at " + place.Id);
        }
    }
}
