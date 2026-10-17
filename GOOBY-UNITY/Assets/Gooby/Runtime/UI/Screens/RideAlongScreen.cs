using Gooby.Core.Net;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Riding along in a friend's car (port of beifahrer_ui.gd): the street with his car where his 5-Hz poses put it
    /// (smoothed in between), our chase camera behind it, a capsule "Unterwegs mit …" with the speed, the radio for both
    /// and "Aussteigen". When the driver goes home (or the connection drops) the passenger is back in the visited house.
    /// </summary>
    public sealed class RideAlongScreen : UiScreen
    {
        private readonly GameHost _host;
        private Label _title, _speed;
        private double _x, _z, _yaw, _kmh;
        private bool _has, _closing;

        public override string SoundScene => "city";

        public RideAlongScreen(GameHost host)
        {
            _host = host;
            Id = "ride-along";
            Opaque = true;
        }

        private CoopDriveClient Coop => _host.Online?.CoopDrive;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.pickingMode = PickingMode.Ignore;
            root.AddToClassList("gb-online");
            _host.World?.SetCityScene(true);

            var cap = Ux.El("gb-visit-cap");
            cap.name = "coop-cap";
            Ux.Abs(cap, 16, 12);
            cap.Add(Ux.Portrait(64));
            var col = Ux.Col("gb-visit-cap-text");
            col.Add(Ux.Text(t.T("nl.coop.kicker"), "gb-visit-kicker"));
            _title = Ux.Text(t.T("nl.coop.unterwegs", ("name", Coop?.PartnerName ?? "")), "gb-visit-title", "gb-nowrap");
            col.Add(_title);
            var sub = Ux.Row("gb-visit-sub");
            var dot = Ux.El("gb-status-dot", "gb-status-dot--on");
            sub.Add(dot);
            _speed = Ux.Text("0 km/h", "gb-nowrap");
            _speed.name = "coop-speed";
            sub.Add(_speed);
            col.Add(sub);
            cap.Add(col);
            root.Add(cap);

            var bar = Ux.Row("gb-coop-bar");
            Ux.Abs(bar, null, null, 28, 34);
            var radio = Ux.IconTextBtn("speaker", t.T("nl.coop.radio"), CycleRadio, "gb-btn-secondary");
            radio.name = "coop-radio";
            radio.style.marginRight = 12;
            bar.Add(radio);
            var getOut = Ux.IconTextBtn("home", t.T("nl.coop.aussteigen"), GetOut, "gb-btn-primary");
            getOut.name = "coop-aussteigen";
            bar.Add(getOut);
            root.Add(bar);
            Debug.Log("[coop] riding along with " + (Coop?.PartnerName ?? "?"));
        }

        public override void Update()
        {
            var coop = Coop;
            if (coop == null || _closing) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.5f);
            if (coop.PositionsReceived == 0) return;
            var target = coop.LastCar;
            if (!_has)
            {
                (_x, _z, _yaw, _kmh) = target;
                _has = true;
            }
            else
            {
                // the poses come five times a second: glide between them instead of hopping
                var k = 1.0 - System.Math.Exp(-8.0 * dt);
                _x += (target.x - _x) * k;
                _z += (target.z - _z) * k;
                _yaw = Mathf.LerpAngle((float)_yaw, (float)target.yawDeg, (float)k);
                _kmh = target.kmh;
            }
            _host.World?.PlaceCarRemote(_x, _z, _yaw, _kmh, dt);
            _speed.text = Mathf.RoundToInt((float)System.Math.Abs(_kmh)) + " km/h";
        }

        private void CycleRadio()
        {
            var coop = Coop;
            if (coop == null || _host.Audio == null) return;
            var station = _host.Audio.CycleRadio(99);
            if (station == null) coop.RadioOff();
            else coop.SetRadio(station);
            var t = _host.Strings;
            var name = station == null ? t.T("audio.radio.aus") : t.T(Gooby.Core.Audio.MusicRegistry.Stations().Find(s => s.id == station).nameKey);
            _host.Ui.Toast(name, "speaker", 2.4f);
        }

        private void GetOut()
        {
            Coop?.Leave();
            Close("selbst");
        }

        /// <summary>Back to the visited house (own choice, the driver went home, or the connection dropped).</summary>
        public void Close(string reason)
        {
            if (_closing) return;
            _closing = true;
            var t = _host.Strings;
            if (reason == "fahrer_weg") _host.Ui.Toast(t.T("nl.coop.fahrer_weg", ("name", Coop?.PartnerName ?? "")), "car", 3.6f);
            else if (reason == "offline") _host.Ui.Toast(t.T("nl.coop.offline"), "car", 3.6f);
            _host.Ui.FadeThrough(() => { if (_host.Ui.Current == this) _host.Ui.Pop(); });
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsOpen(this)) return;
            _host.Audio?.RadioOff();
            if (Coop != null && Coop.Active) Coop.Leave();
            _host.World?.SetCityScene(false);
        }
    }
}
