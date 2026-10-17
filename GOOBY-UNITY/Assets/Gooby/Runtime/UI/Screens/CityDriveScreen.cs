using System;
using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Unity.App;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// City drive chrome (concept board 03): minimap card (the town plan, north up, car-centred, heading arrow),
    /// place pill, settings + clock, teal "Nach Hause", current task card, joystick, speed/fuel readout with
    /// steer nudges, reverse + pink brake. The car drives freely through Sonnental's street lattice (Core DriveModel).
    /// </summary>
    public sealed class CityDriveScreen : UiScreen
    {
        private readonly GameHost _host;

        public override string SoundScene => "city";
        private Label _speed;
        private VisualElement _fuelFill;
        private VisualElement _map;
        private bool _braking;
        private float _throttle;
        private float _steer;
        private float _nudge;   // the arrow buttons beside the speed: a gentle steer while held
        private DriveModel.Place _stopAt;
        private DriveModel.Place _parkedAt;   // the place whose sheet is open — the pill keeps naming it even a few metres past its door
        private Label _placeTitle, _placeSub, _enterLabel;
        private Button _enter;
        private VisualElement _skyIcon;
        private string _skyIconName = "";
        private VisualElement _toastLane;

        private DriveModel.State Drive => _host.World?.Drive;

        /// <summary>The brake pedal (held while the pink disc is pressed); tests park the car with it.</summary>
        public bool Braking { get => _braking; set => _braking = value; }

        // the minimap picture (tools/art/build_minimap.py) covers x ∈ ±MapHalfX, z ∈ ±MapHalfZ metres of the town
        private const float MapHalfX = 170f, MapHalfZ = 140f;
        private const float MapPxPerMetre = 0.9f;         // HUD pixels per metre: the 188 px window spans ~10 tiles
        // board 03: a near-square plan in a thick white frame (.gb-minimap — keep these in step with the stylesheet)
        private const float MiniW = 196f, MiniH = 184f, MiniBorder = 4f;
        private const float WindowW = MiniW - 2f * MiniBorder, WindowH = MiniH - 2f * MiniBorder;
        private VisualElement _you;

        public CityDriveScreen(GameHost host)
        {
            _host = host;
            Id = "city-drive";
            Opaque = true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.pickingMode = PickingMode.Ignore;
            _host.World?.SetCityScene(true);
            RefreshTempo();
            _host.Store.SliceChanged += OnSlice;
            // fresh out of the driveway: home is not offered until we have driven past it — the pill shows the first shop
            if (Drive != null && DriveModel.Nearby(Drive)?.Id == "home") _left = DriveModel.PlaceById("home");

            root.AddToClassList("gb-drive");

            // minimap: the village illustration scrolls under a fixed white nav arrow; the "N" badge rides the top edge
            var miniWrap = new VisualElement { name = "minimap", pickingMode = PickingMode.Ignore };
            Ux.Abs(miniWrap, 18, 18);
            var mini = Ux.El("gb-card", "gb-minimap");
            _map = new VisualElement { name = "minimap-town", pickingMode = PickingMode.Ignore };
            _map.style.position = Position.Absolute;
            _map.style.width = MapHalfX * 2f * MapPxPerMetre; _map.style.height = MapHalfZ * 2f * MapPxPerMetre;
            _map.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            var mapTex = Ux.ImageTexture("minimap_town");
            if (mapTex != null) _map.style.backgroundImage = new StyleBackground(mapTex);
            mini.Add(_map);
            // a tap near a pin sets the GPS destination, the same pin again switches it off (drive_hud.gd W13B)
            _pinLayer = new VisualElement { name = "minimap-pins", pickingMode = PickingMode.Position };
            _pinLayer.style.position = Position.Absolute;
            _pinLayer.style.left = 0; _pinLayer.style.top = 0; _pinLayer.style.right = 0; _pinLayer.style.bottom = 0;
            _pinLayer.RegisterCallback<PointerDownEvent>(e => { e.StopPropagation(); TapMinimap(e.localPosition); });
            mini.Add(_pinLayer);
            _you = Ux.Icon("nav_arrow");
            _you.AddToClassList("gb-minimap-you");
            _you.pickingMode = PickingMode.Ignore; // a pin under the car stays tappable for the GPS
            mini.Add(_you);
            miniWrap.Add(mini);
            var compass = Ux.El("gb-minimap-compass");
            compass.Add(Ux.Text("N", "gb-sm", "gb-extrabold"));
            miniWrap.Add(compass);
            root.Add(miniWrap);
            ScrollMap();

            // place pill (dark glass): where we are / what comes next; grows an "Anhalten" button at a place
            var place = Ux.El("gb-row", "gb-drive-place");
            Ux.Abs(place, 18 + MiniW + 18, 24);
            var pin = Ux.Icon("pin_white");
            pin.style.width = 24; pin.style.height = 24;
            pin.style.marginRight = 10;
            place.Add(pin);
            // fixed text column: the distance in the subtitle ticks every frame, the button must not jitter under the thumb
            var pcol = Ux.Col();
            pcol.style.width = 200;
            _placeTitle = Ux.Text(t.T("nl.stadt.ort_sonnental"), "gb-drive-place-title");
            _placeTitle.name = "place-title";
            _placeSub = Ux.Text(t.T("nl.stadt.ort_untertitel"), "gb-drive-place-sub");
            _placeSub.name = "place-sub";
            _placeSub.style.overflow = Overflow.Hidden;
            _placeSub.style.textOverflow = TextOverflow.Ellipsis;
            pcol.Add(_placeTitle);
            pcol.Add(_placeSub);
            place.Add(pcol);
            _enter = Ux.IconTextBtn("shop", t.T("nl.stadt.anhalten"), StopHere, "gb-btn-confirm", "gb-btn-sm");
            _enter.name = "enter-place";
            _enterLabel = _enter.Q<Label>();
            _enter.style.marginLeft = 14;
            _enter.style.display = DisplayStyle.None;
            place.Add(_enter);
            root.Add(place);

            // below the place pill (top 24, ~56 high): a toast must not sit on the "Anhalten" button
            _toastLane = new VisualElement { pickingMode = PickingMode.Ignore, name = "near-miss-toast" };
            Ux.Abs(_toastLane, 0, 92, 0);
            _toastLane.style.alignItems = Align.Center;
            _toastLane.style.justifyContent = Justify.Center;
            _toastLane.style.flexDirection = FlexDirection.Row;
            root.Add(_toastLane);

            _chevron = new MgPaint.Canvas { name = "gps-chevron", Paint = PaintChevron };
            _chevron.style.position = Position.Absolute;
            _chevron.style.width = ChevronSize; _chevron.style.height = ChevronSize;
            _chevron.style.display = DisplayStyle.None;
            root.Add(_chevron);

            // top right (dark glass like the home HUD): settings disc, clock with the sky icon in front, teal home, task card
            var tr = Ux.Row();
            Ux.Abs(tr, null, 18, 16);
            var radio = Ux.IconBtn("radio_white", ToggleRadio, "gb-btn-icon-sm", "gb-drive-disc");
            radio.name = "drive-radio";
            radio.style.marginRight = 10;
            tr.Add(radio);
            var gear = Ux.IconBtn("settings_white", () => _host.Ui.Push(new SettingsScreen(_host)), "gb-btn-icon-sm", "gb-drive-disc");
            gear.style.marginRight = 10;
            tr.Add(gear);
            var clockCard = Ux.El("gb-row", "gb-drive-clock");
            _skyIcon = Ux.Icon("sun", "gb-icon-lg");
            _skyIcon.style.marginRight = 8;
            clockCard.Add(_skyIcon);
            UpdateSkyIcon();
            var now = DateTimeOffset.FromUnixTimeMilliseconds(_host.Store.Clock.NowMs()).ToLocalTime();
            var ccol = Ux.Col();
            ccol.style.alignItems = Align.FlexEnd;
            ccol.Add(Ux.Text(now.ToString("HH:mm"), "gb-drive-clock-time"));
            ccol.Add(Ux.Text(now.ToString("ddd, d. MMM", new System.Globalization.CultureInfo("de-DE")).Replace(".,", ","), "gb-drive-clock-date"));
            clockCard.Add(ccol);
            tr.Add(clockCard);
            root.Add(tr);

            var home = Ux.IconTextBtn("home_white", t.T("nl.hud.nach_hause"), Close, "gb-btn-teal");
            home.style.minWidth = 190;
            Ux.Abs(home, null, 86, 16);
            root.Add(home);

            // task card: leaf + title, a hairline, the task, its box underneath (board 03)
            var task = Ux.El("gb-card", "gb-drive-task");
            Ux.Abs(task, null, 150, 16);
            task.style.width = 236;
            var th = Ux.Row();
            th.Add(Ux.Icon("leaf"));
            var tl = Ux.Text(t.T("nl.hud.aufgabe"), "gb-md", "gb-bold");
            tl.style.marginLeft = 6;
            th.Add(tl);
            task.Add(th);
            task.Add(Ux.El("gb-drive-task-rule"));
            var open = _host.Quests?.Board().FirstOrDefault(r => !r.Claimed);
            var questTitle = open != null ? (t.HasKey(open.Def.TitleKey) ? t.T(open.Def.TitleKey) : open.Def.Id) : t.T("nl.stadt.quest_mart");
            var questText = Ux.Text(open != null && open.Target > 1 ? $"{questTitle} ({open.Progress}/{open.Target})" : questTitle, "gb-md");
            questText.style.whiteSpace = WhiteSpace.Normal;
            task.Add(questText);
            var box = Ux.El("gb-checkbox");
            box.style.marginTop = 6;
            if (open != null && open.Complete) { box.AddToClassList("gb-checkbox--done"); box.Add(Ux.Icon("check")); }
            task.Add(box);
            root.Add(task);

            // joystick: x turns the car (low-pass filtered in the model, lane assist while the thumb rests)
            var joy = new Joystick();
            Ux.Abs(joy, 40, null, null, 28);
            joy.Changed += v => _steer = v.x;
            joy.style.display = _host.Settings.ShowControls ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(joy);

            // speed capsule (dark glass): light nudge discs either side, km/h and the fuel bar in the middle (board 03)
            var speedCard = Ux.El("gb-row", "gb-drive-speed");
            Ux.Abs(speedCard, null, null, null, 22);
            speedCard.style.left = Length.Percent(50);
            speedCard.style.translate = new Translate(Length.Percent(-50), 0);
            var left = Ux.IconBtn("arrow_left", () => { }, "gb-btn-icon-sm", "gb-drive-nudge");
            left.RegisterCallback<PointerDownEvent>(_ => _nudge = -0.75f, TrickleDown.TrickleDown);
            left.RegisterCallback<PointerUpEvent>(_ => _nudge = 0f, TrickleDown.TrickleDown);
            left.RegisterCallback<PointerLeaveEvent>(_ => _nudge = 0f);
            speedCard.Add(left);
            var scol = Ux.Col();
            scol.style.alignItems = Align.Center;
            scol.style.marginLeft = 22; scol.style.marginRight = 22;
            _speed = Ux.Text("0", "gb-drive-speed-value");
            scol.Add(_speed);
            scol.Add(Ux.Text(t.T("nl.hud.kmh"), "gb-drive-speed-unit"));
            var frow = Ux.Row();
            frow.style.marginTop = 4;
            var fuelIcon = Ux.Icon("fuel_white");
            fuelIcon.style.width = 18; fuelIcon.style.height = 18; fuelIcon.style.marginRight = 8;
            frow.Add(fuelIcon);
            var fuel = Ux.Bar((float)(Drive?.Fuel ?? 0.72), out _fuelFill);
            fuel.AddToClassList("gb-drive-fuel");
            frow.Add(fuel);
            scol.Add(frow);
            speedCard.Add(scol);
            var right = Ux.IconBtn("arrow_right", () => { }, "gb-btn-icon-sm", "gb-drive-nudge");
            right.RegisterCallback<PointerDownEvent>(_ => _nudge = 0.75f, TrickleDown.TrickleDown);
            right.RegisterCallback<PointerUpEvent>(_ => _nudge = 0f, TrickleDown.TrickleDown);
            right.RegisterCallback<PointerLeaveEvent>(_ => _nudge = 0f);
            speedCard.Add(right);
            root.Add(speedCard);

            // reverse + brake
            var rev = Ux.El("gb-action", "gb-drive-reverse");
            Ux.Abs(rev, null, null, 56, 198);
            rev.Add(Ux.Text(t.T("nl.hud.rueckwaerts")));
            rev.RegisterCallback<PointerDownEvent>(_ => _throttle = -1f);
            rev.RegisterCallback<PointerUpEvent>(_ => _throttle = 0f);
            root.Add(rev);
            var brake = Ux.El("gb-action", "gb-drive-brake");
            Ux.Abs(brake, null, null, 28, 34);
            brake.Add(Ux.Icon("brake"));
            brake.Add(Ux.Text(t.T("nl.hud.bremse")));
            brake.RegisterCallback<PointerDownEvent>(_ => _braking = true);
            brake.RegisterCallback<PointerUpEvent>(_ => _braking = false);
            root.Add(brake);

            // the car cruises by itself; the player steers, brakes, reverses and stops at places
            _throttle = 1f;
            UpdatePlace();
        }

        public override void Update()
        {
            var drive = Drive;
            if (drive == null) return;
            // a friend riding along follows the car (the client sends at most 5 times a second)
            _host.Online?.CoopDrive?.ReportCar(drive.X, drive.Z, drive.HeadingDeg, drive.Kmh);
            TickTestDrive(Mathf.Min(Time.unscaledDeltaTime, 0.5f));
            // unscaledDeltaTime is not clamped by Time.maximumDeltaTime: a loading hitch must not teleport the car past a place
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.5f);
            // a sheet over the street (shop, place dialog, settings): the car stays parked until we are back
            if (_host.Ui.Current != this)
            {
                if (drive.Kmh != 0.0) _host.World.DriveStep(new DriveModel.Input { Brake = true }, dt);
                UpdatePlace();
                return;
            }
            // the arrow buttons nudge the wheel while the stick rests
            var steer = Mathf.Abs(_steer) >= 0.2f ? _steer : _nudge;
            // the car from the dealer sets the cruising speed (stats.speed: 3 = the start sedan's 42 km/h)
            var input = new DriveModel.Input { Throttle = _throttle, Brake = _braking, Steer = steer, Cruise = DriveModel.CruiseKmh * Tempo };
            if (_entering) input = new DriveModel.Input { Brake = true, Steer = steer }; // engine off while we walk in
            if (_stopAt != null)
            {
                // pulling over: the model rolls the car up to the place and parks it; a passed place is dropped
                var approach = DriveModel.Approach(drive, _stopAt, steer);
                if (approach == null) { Debug.Log($"[drive] approach abandoned d={DriveModel.Distance(drive, _stopAt):F1} along={DriveModel.Along(drive, _stopAt):F1}"); _stopAt = null; }
                else input = _braking ? new DriveModel.Input { Brake = true, Steer = steer } : approach.Value;
            }
            _host.World.DriveStep(input, dt);
            if (_host.World.LastNearMiss) ShowNearMissToast();
            _speed.text = Mathf.RoundToInt(Mathf.Abs((float)drive.Kmh)).ToString();
            Ux.SetBar(_fuelFill, drive.Fuel);
            ScrollMap();
            UpdatePlace();
            UpdateChevron();
            if (_stopAt != null && DriveModel.ParkedAt(drive, _stopAt)) EnterPlace(_stopAt);
        }

        private void ShowNearMissToast() => ShowToast("sparkle", _host.Strings.T("city.fahren.beinahe_hupe"));

        /// <summary>The car radio: off → the stations the level unlocks → off, with the station and track as a toast.</summary>
        private void ToggleRadio()
        {
            if (_host.Audio == null) return;
            var station = _host.Audio.CycleRadio(_host.Store.GetValue("progression.level", 1));
            var coop = _host.Online?.CoopDrive;
            if (coop != null && coop.Active)
            {
                if (station == null) coop.RadioOff();
                else coop.SetRadio(station);
            }
            var t = _host.Strings;
            if (station == null)
            {
                ShowToast("speaker", t.T("audio.radio.aus"));
                return;
            }
            var def = Gooby.Core.Audio.MusicRegistry.Stations().Find(s => s.id == station);
            var title = Gooby.Core.Audio.MusicRegistry.Get(_host.Audio.Track)?.Title ?? "";
            ShowToast("speaker", t.T(def.nameKey) + (title.Length > 0 ? " · " + title : ""));
        }

        private void ShowToast(string icon, string text)
        {
            if (_toastLane == null) return;
            _toastLane.Clear();
            var toast = Ux.El("gb-toast");
            toast.pickingMode = PickingMode.Ignore;
            toast.Add(Ux.Icon(icon));
            toast.Add(Ux.Text(text));
            _toastLane.Add(toast);
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(2800);
        }

        // ── GPS (drive_hud.gd W13B Ziel-Chevron) ────────────────────────────

        private const float ChevronSize = 54f;
        private DriveModel.Place _ziel;
        private MgPaint.Canvas _chevron;

        /// <summary>The GPS destination ("" = none) and the wedge as last placed (Root-local px) — for QA.</summary>
        public string GpsZiel => _ziel?.Id ?? "";
        public bool GpsPfeilSichtbar => _chevron != null && _chevron.style.display == DisplayStyle.Flex;
        public Vector2 GpsPfeilPos { get; private set; }
        public float GpsPfeilWinkel { get; private set; }

        /// <summary>QA: the visible minimap pins and the wedge in window pixels (top-left origin), for real taps and checks.</summary>
        public List<(string id, Vector2 px)> PinScreenPoints()
        {
            var list = new List<(string id, Vector2 px)>();
            var tree = Root?.panel?.visualTree;
            if (tree == null || tree.layout.width <= 1f) return list;
            var sx = Screen.width / tree.layout.width;
            var sy = Screen.height / tree.layout.height;
            foreach (var kv in _pins)
                if (kv.Value.resolvedStyle.display == DisplayStyle.Flex && kv.Value.worldBound.width > 0f)
                    list.Add((kv.Key, new Vector2(kv.Value.worldBound.center.x * sx, kv.Value.worldBound.center.y * sy)));
            if (GpsPfeilSichtbar) list.Add(("@pfeil", new Vector2(_chevron.worldBound.center.x * sx, _chevron.worldBound.center.y * sy)));
            return list;
        }

        /// <summary>A tap on the minimap (pin layer px): the nearest pin within reach becomes the destination, or GPS goes off.</summary>
        private void TapMinimap(Vector2 at)
        {
            var drive = Drive;
            if (drive == null) return;
            var id = CityGps.NaechsterPin(PinPoints(drive), at.x, at.y);
            if (id != "") SetZiel(DriveModel.PlaceById(id));
        }

        /// <summary>The visible pins in pin-layer px (their centres, as UpdateMapPins places them).</summary>
        private List<(string id, double x, double y)> PinPoints(DriveModel.State drive)
        {
            var pins = new List<(string id, double x, double y)>();
            foreach (var p in DriveModel.Places)
            {
                var dx = (float)(p.X - drive.X) * MapPxPerMetre;
                var dz = -(float)(p.Z - drive.Z) * MapPxPerMetre;
                if (Mathf.Abs(dx) <= WindowW * 0.5f - 9f && Mathf.Abs(dz) <= WindowH * 0.5f - 9f)
                    pins.Add((p.Id, WindowW * 0.5f + dx, WindowH * 0.5f + dz));
            }
            return pins;
        }

        /// <summary>Sets the destination; null or the current one switches GPS off (the latter with a toast, like Godot).</summary>
        /// <summary>QA: the car stands at a place's kerb a few metres before the spot, nose along the street (into a dead end: towards its end).</summary>
        public void QaWarp(DriveModel.Place place)
        {
            var drive = Drive;
            if (drive == null || place == null) return;
            var (r, c) = (place.StreetTile[0], place.StreetTile[1]);
            var arms = CityGrid.StreetNeighbours(r, c);
            var (sx, sz) = CityGrid.World(r, c);
            var (ax, az) = arms.Count > 0 ? CityGrid.World(r + arms[0].dr, c + arms[0].dc) : (sx, sz - 1.0);
            var heading = Math.Atan2(sx - ax, sz - az);
            drive.X = place.X - Math.Sin(heading) * 6.0;
            drive.Z = place.Z - Math.Cos(heading) * 6.0;
            drive.Heading = heading;
            drive.Kmh = 0.0;
            Debug.Log($"[qa] warp {place.Id} → ({drive.X:F1}, {drive.Z:F1})");
        }

        public void SetZiel(DriveModel.Place place)
        {
            var t = _host.Strings;
            if (place == null || place == _ziel)
            {
                var war = _ziel != null;
                _ziel = null;
                if (war && place != null) ShowToast("pin", t.T("city_leben.ziel_weg"));
                Debug.Log("[drive] gps off");
                UpdateChevron();
                return;
            }
            _ziel = place;
            ShowToast("pin", t.T("city_leben.ziel_gesetzt", ("ort", t.T(place.NameKey))));
            Debug.Log("[drive] gps " + place.Id);
            UpdateChevron();
        }

        /// <summary>The wedge at the screen edge towards the destination's parking spot, hidden while the spot is in view.</summary>
        private void UpdateChevron()
        {
            if (_chevron == null) return;
            var cam = _host.World?.Camera;
            if (_ziel == null || cam == null || Root?.panel == null || Root.layout.width <= 1f)
            {
                _chevron.style.display = DisplayStyle.None;
                return;
            }
            var ziel = new Vector3((float)_ziel.X, 1f, (float)_ziel.Z);
            var behind = cam.WorldToScreenPoint(ziel).z < 0f;
            var p = Root.WorldToLocal(RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, ziel, cam));
            var lage = CityGps.ChevronPlatzierung(Root.layout.width, Root.layout.height, p.x, p.y, behind);
            if (!lage.Sichtbar)
            {
                _chevron.style.display = DisplayStyle.None;
                return;
            }
            GpsPfeilPos = new Vector2((float)lage.X, (float)lage.Y);
            GpsPfeilWinkel = (float)(lage.Winkel * Mathf.Rad2Deg);
            _chevron.style.display = DisplayStyle.Flex;
            _chevron.style.left = GpsPfeilPos.x - ChevronSize * 0.5f;
            _chevron.style.top = GpsPfeilPos.y - ChevronSize * 0.5f;
            _chevron.style.rotate = new Rotate(new Angle(GpsPfeilWinkel, AngleUnit.Degree));
        }

        /// <summary>Dark glass disc with a sun-yellow wedge pointing right (the element turns it towards the destination).</summary>
        private static void PaintChevron(Painter2D p)
        {
            var c = new Vector2(ChevronSize * 0.5f, ChevronSize * 0.5f);
            MgPaint.Circle(p, c, ChevronSize * 0.5f - 2f, new Color(0.23f, 0.21f, 0.31f, 0.82f));
            MgPaint.Ring(p, c, ChevronSize * 0.5f - 2f, 2.5f, new Color(1f, 1f, 1f, 0.9f));
            MgPaint.Poly(p, new[] { c + new Vector2(15f, 0f), c + new Vector2(-9f, -12f), c + new Vector2(-4f, 0f), c + new Vector2(-9f, 12f) },
                new Color(1f, 0.82f, 0.4f));
        }

        /// <summary>Pill: the place within reach (offer to pull over / parked) or the next one ahead with its distance.</summary>
        private void UpdatePlace()
        {
            var drive = Drive;
            if (drive == null || _placeTitle == null) return;
            var t = _host.Strings;
            var offer = Offer(drive, out var near, out var metres);
            // the destination's parking prompt is the arrival: GPS off with a toast
            if (_ziel != null && near == _ziel)
            {
                Debug.Log("[drive] gps reached " + _ziel.Id);
                ShowToast("pin", t.T("city_leben.ziel_erreicht", ("ort", t.T(_ziel.NameKey))));
                _ziel = null;
            }
            UpdateMapPins(drive);
            if (offer != null)
            {
                _placeTitle.text = t.T(offer.NameKey);
                var open = IsOpen(offer);
                if (!open && !CityMap.ImmerBetretbar(offer.MapId))
                {
                    // closed today: the sign says why, no stop is offered
                    _placeSub.text = t.T(CityMap.GeschlossenKey(CityMap.Get(offer.MapId)));
                    _enter.style.display = DisplayStyle.None;
                    return;
                }
                _placeSub.text = !open ? t.T("nl.markt.zu_stand")
                    : near != null && (DriveModel.Reached(drive) != null || near == _parkedAt) ? t.T("nl.stadt.angekommen")
                    : near != null ? t.T("nl.stadt.hier_anhalten")
                    : t.T("nl.stadt.naechster_ort", ("ort", t.T(offer.NameKey)), ("m", Mathf.RoundToInt((float)metres)));
                _enter.style.display = DisplayStyle.Flex;
                _enterLabel.text = t.T(_stopAt != null ? "nl.stadt.haelt" : offer.Id == "home" ? "nl.hud.nach_hause" : "nl.stadt.anhalten");
                return;
            }
            var (ahead, m) = DriveModel.Ahead(drive);
            _placeTitle.text = t.T("nl.stadt.ort_sonnental");
            _placeSub.text = ahead != null ? t.T("nl.stadt.naechster_ort", ("ort", t.T(ahead.NameKey)), ("m", Mathf.RoundToInt((float)m))) : t.T("nl.stadt.ort_untertitel");
            _enter.style.display = DisplayStyle.None;
        }

        // ── minimap pins ────────────────────────────────────────────────────

        private readonly Dictionary<string, VisualElement> _pins = new Dictionary<string, VisualElement>();
        private VisualElement _pinLayer;

        /// <summary>The places as small round pins on the minimap at their plan positions around the car.</summary>
        private void UpdateMapPins(DriveModel.State drive)
        {
            if (_pinLayer == null) return;
            foreach (var p in DriveModel.Places)
            {
                var dx = (float)(p.X - drive.X) * MapPxPerMetre;
                var dz = -(float)(p.Z - drive.Z) * MapPxPerMetre;   // north (+z) is up
                var visible = Mathf.Abs(dx) <= WindowW * 0.5f - 9f && Mathf.Abs(dz) <= WindowH * 0.5f - 9f;
                if (!_pins.TryGetValue(p.Id, out var pin))
                {
                    if (!visible) continue;
                    pin = Ux.El("gb-minimap-pin");
                    pin.style.position = Position.Absolute;
                    pin.style.width = 20; pin.style.height = 20;
                    pin.style.borderTopLeftRadius = 10; pin.style.borderTopRightRadius = 10; pin.style.borderBottomLeftRadius = 10; pin.style.borderBottomRightRadius = 10;
                    pin.style.backgroundColor = p.Id == "home" ? new Color(1f, 0.85f, 0.85f) : new Color(1f, 0.98f, 0.94f);
                    pin.style.borderTopWidth = 2; pin.style.borderBottomWidth = 2; pin.style.borderLeftWidth = 2; pin.style.borderRightWidth = 2;
                    var border = new Color(0.42f, 0.31f, 0.23f, 0.35f);
                    pin.style.borderTopColor = border; pin.style.borderBottomColor = border; pin.style.borderLeftColor = border; pin.style.borderRightColor = border;
                    pin.style.alignItems = Align.Center; pin.style.justifyContent = Justify.Center;
                    pin.pickingMode = PickingMode.Ignore;
                    var icon = Ux.Icon(p.Icon);
                    icon.style.width = 13; icon.style.height = 13;
                    pin.Add(icon);
                    _pins[p.Id] = pin;
                    _pinLayer.Add(pin);
                }
                pin.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                // the GPS destination wears a sun-yellow ring
                var ziel = p == _ziel;
                var rand = ziel ? new Color(1f, 0.75f, 0.2f) : new Color(0.42f, 0.31f, 0.23f, 0.35f);
                var breite = ziel ? 3f : 2f;
                pin.style.borderTopColor = rand; pin.style.borderBottomColor = rand; pin.style.borderLeftColor = rand; pin.style.borderRightColor = rand;
                pin.style.borderTopWidth = breite; pin.style.borderBottomWidth = breite; pin.style.borderLeftWidth = breite; pin.style.borderRightWidth = breite;
                // north up: +x (east) is right, +z (north) is up — the car sits in the middle of the window
                pin.style.left = WindowW * 0.5f + dx - 10f;
                pin.style.top = WindowH * 0.5f + dz - 10f;
            }
        }

        private DriveModel.Place _left;   // the place we just walked out of: not offered again while pulling away

        /// <summary>
        /// The place the pill offers: the one within reach (parked / arriving), or the next one ahead within the offer
        /// radius. Pulling away from a place we just left skips it — the next stop is what a tap means then.
        /// </summary>
        private DriveModel.Place Offer(DriveModel.State drive, out DriveModel.Place near, out double metres)
        {
            if (_left != null && DriveModel.Distance(drive, _left) > DriveModel.ParkTolerance) _left = null;
            near = DriveModel.Nearby(drive);
            if (near != null && near == _left) near = null;   // just walked out of it: the next stop is what a tap means
            near = near ?? _parkedAt;
            var offerBeyond = DriveModel.PassedOrAhead(drive, out var m);
            metres = m;
            if (near != null) return near;
            if (offerBeyond != null && offerBeyond != _left) { metres = offerBeyond == DriveModel.JustPassed(drive) ? 0.0 : m; return offerBeyond; }
            var (ahead, aheadM) = DriveModel.Ahead(drive);
            metres = aheadM;
            return aheadM <= DriveModel.OfferRadius && ahead != _left ? ahead : null;
        }

        /// <summary>"Anhalten": roll up to the offered place, park, then walk in.</summary>
        public void StopHere()
        {
            var drive = Drive;
            if (drive == null || _entering) return;
            var target = Offer(drive, out var near, out _);
            Debug.Log($"[drive] StopHere target={target?.Id} near={near?.Id} kmh={drive.Kmh:F1} d={(target != null ? DriveModel.Distance(drive, target) : -1):F1} along={(target != null ? DriveModel.Along(drive, target) : 0):F1}");
            if (target == null) return;
            if (near == target && DriveModel.Reached(drive) == target) { EnterPlace(target); return; }
            _stopAt = target;
        }

        private void EnterPlace(DriveModel.Place place)
        {
            Debug.Log($"[drive] EnterPlace {place.Id}");
            _stopAt = null;
            _parkedAt = place;
            if (_ziel == place) _ziel = null;
            if (place.Id == "home") { Close(); return; }
            if (place.Id == DriveModel.RanchId)
            {
                // out of town: the ranch farm (its gate card offers the ranch before it is bought)
                _entering = true;
                Sfx.Play("ranch_wiehern_a", 0f, 0.6f);
                _host.Ui.FadeThrough(() =>
                {
                    _entering = false;
                    if (_host.Ui.Current != this) return;
                    _host.Ui.Push(new RanchHofScreen(_host));
                });
                return;
            }
            if (!IsOpen(place) && !CityMap.ImmerBetretbar(place.MapId)) return; // the pill already says why
            // parked at a place: the tank is filled while we are there; through the door (fade) into the interior
            // with the shopkeeper's dialog or — park, airport — straight to the counter
            if (Drive != null) Drive.Fuel = 1.0;
            Ux.SetBar(_fuelFill, 1.0);
            _entering = true;
            _host.Ui.FadeThrough(() =>
            {
                _entering = false;
                if (_host.Ui.Current != this) return;
                _host.Ui.Push(PlaceScreen.For(_host, place));
            });
        }

        private bool _entering;

        /// <summary>Cruise multiplier of the active car (AutoKatalog.TempoFaktor), re-read when the city slice changes.</summary>
        private double Tempo { get; set; } = 1.0;

        private void RefreshTempo() => Tempo = AutoKatalog.TempoFaktor(AutoKatalog.AktivesAuto(_host.Store).auto?.Speed ?? 3);

        private void OnSlice(string id, Newtonsoft.Json.Linq.JToken _) { if (id == "city" && !_trialOn) RefreshTempo(); }

        /// <summary>Opening rule of the town plan (CityMap) against the game clock — the market only on Saturday mornings.</summary>
        private bool IsOpen(DriveModel.Place place)
        {
            var ort = CityMap.Get(place.MapId);
            if (ort?.Oeffnung == null) return true;
            var now = _host.Store.Clock.NowMs();
            var (wochentag, stunde) = CityMap.LokaleZeit(now, _host.Store.Clock.UtcOffsetMinutes(now));
            return CityMap.IstOffen(ort, wochentag, stunde);
        }

        /// <summary>This hour's home weather (rain, snow, clouds); clear skies show the sun by day and the moon at night (DayPhase).</summary>
        private void UpdateSkyIcon()
        {
            if (_skyIcon == null || _host.World == null) return;
            var local = DateTimeOffset.FromUnixTimeMilliseconds(_host.Store.Clock.NowMs()).ToLocalTime();
            var icon = HomeHudScreen.WeatherIcon(local);
            if (icon == "sun" || icon == "moon")
                icon = Gooby.Core.Time.DayPhase.Of(_host.World.LocalHour) == Gooby.Core.Time.DayPhase.Nacht ? "moon" : "sun";
            if (icon == _skyIconName) return;
            _skyIconName = icon;
            _skyIcon.style.backgroundImage = new StyleBackground(Ux.IconTexture(icon));
        }

        public override void OnShown()
        {
            // back from a place: the street is still the stage, the car stands at its kerb — and that place is not
            // offered again until we have driven past it
            _stopAt = null;
            if (_parkedAt != null) { _left = _parkedAt; _parkedAt = null; }
            UpdateSkyIcon();
        }

        /// <summary>The town plan stays north-up; it slides so the car is always under the arrow, which turns with the heading.</summary>
        private void ScrollMap()
        {
            if (_map == null || Drive == null) return;
            _map.style.left = WindowW * 0.5f - ((float)Drive.X + MapHalfX) * MapPxPerMetre;
            _map.style.top = WindowH * 0.5f - (MapHalfZ - (float)Drive.Z) * MapPxPerMetre;
            // heading 0 drives north (up on the map), positive headings turn clockwise — like the arrow icon
            if (_you != null) _you.style.rotate = new Rotate(new Angle((float)Drive.HeadingDeg, AngleUnit.Degree));
        }

        // ── the dealer's test drive (Autohaus Blechbert) ──

        /// <summary>How long a test drive lasts before Benzino wants his car back.</summary>
        public const float TestDriveSeconds = 60f;

        private float _trialLeft;
        private bool _trialOn;
        private string _trialName = "";
        private DriveModel.Place _trialDealer;
        private VisualElement _trialPill;
        private Label _trialLabel;

        public bool TestDriving => _trialOn;

        /// <summary>From the dealer's door: the street car in the chosen model and paint, its own cruise speed, a minute on the clock.</summary>
        public void BeginTestDrive(string carId, string farbe, DriveModel.Place dealer)
        {
            var t = _host.Strings;
            var auto = AutoKatalog.Get(carId);
            _trialName = auto?.NameDe ?? carId;
            _trialDealer = dealer;
            _trialLeft = TestDriveSeconds;
            _trialOn = true;
            _left = dealer;
            // off the dealer's forecourt: whatever held the car at the kerb lets go, the engine pulls away
            _braking = false;
            _throttle = 1f;
            _host.World?.DressStreetCar(carId, farbe);
            Tempo = AutoKatalog.TempoFaktor(auto?.Speed ?? 3);
            if (_trialPill == null)
            {
                _trialPill = Ux.Row("gb-drive-trial");
                _trialPill.name = "drive-trial";
                _trialPill.pickingMode = PickingMode.Ignore;
                var icon = Ux.Icon("compass");
                icon.pickingMode = PickingMode.Ignore;
                _trialPill.Add(icon);
                _trialLabel = Ux.Text("", "gb-drive-trial-text");
                _trialLabel.pickingMode = PickingMode.Ignore;
                _trialPill.Add(_trialLabel);
                Root.Add(_trialPill);
            }
            _trialPill.style.display = DisplayStyle.Flex;
            UpdateTrialPill();
            ShowToast("compass", t.T("nl.probefahrt.start", ("name", _trialName), ("s", Mathf.RoundToInt(TestDriveSeconds))));
            Sfx.Play("car_start", 0f, 0.8f);
            Debug.Log("[drive] test drive " + carId + " " + farbe);
        }

        private void UpdateTrialPill()
        {
            if (_trialLabel == null) return;
            var s = Mathf.Max(0, Mathf.CeilToInt(_trialLeft));
            _trialLabel.text = _host.Strings.T("nl.probefahrt.pill", ("name", _trialName), ("zeit", (s / 60) + ":" + (s % 60).ToString("00")));
            _trialPill.EnableInClassList("gb-drive-trial--last", _trialLeft <= 10f);
        }

        private void TickTestDrive(float dt)
        {
            if (!_trialOn || _entering || _host.Ui.Current != this) return;
            var before = Mathf.CeilToInt(_trialLeft);
            _trialLeft -= dt;
            if (Mathf.CeilToInt(_trialLeft) != before) UpdateTrialPill();
            if (_trialLeft <= 0f) EndTestDrive();
        }

        /// <summary>Time is up (or the player heads home): fade, our own car back at the dealer's kerb, in through his door.</summary>
        private void EndTestDrive()
        {
            if (!_trialOn) return;
            var t = _host.Strings;
            var dealer = _trialDealer;
            var name = _trialName;
            _trialOn = false;
            _trialDealer = null;
            if (_trialPill != null) _trialPill.style.display = DisplayStyle.None;
            _entering = true;
            Sfx.Play("car_hupe", 0f, 0.8f);
            _host.Ui.FadeThrough(() =>
            {
                _entering = false;
                _host.World?.RestoreStreetCar();
                RefreshTempo();
                _host.World?.ParkAt(dealer);
                Debug.Log("[drive] test drive over");
                if (dealer == null || _host.Ui.Current != this) return;
                _parkedAt = dealer;
                var screen = PlaceScreen.For(_host, dealer);
                _host.Ui.Push(screen);
                if (screen is PlaceScreen ps) ps.Aside(t.T("nl.probefahrt.zurueck", ("name", name)));
            });
        }

        /// <summary>
        /// Popped without "Nach Hause" (popping to the root for a visit or the taxi): the street leaves the stage as well,
        /// otherwise the town stays under the home HUD. Covered by a place or a sheet, it stays.
        /// </summary>
        public override void OnHidden()
        {
            if (_host.Ui.IsOpen(this)) return;
            _host.Store.SliceChanged -= OnSlice;
            var coop = _host.Online?.CoopDrive;
            if (coop != null && coop.Role == Gooby.Core.Net.CoopDriveClient.RoleDriver) coop.Leave();
            _host.World?.SetCityScene(false);
        }

        private void Close()
        {
            // during a test drive "Nach Hause" first brings the car back to the dealer
            if (_trialOn) { EndTestDrive(); return; }
            _host.Store.SliceChanged -= OnSlice;
            // a ride along ends with the driver's way home: the passenger is back in the visited house
            var coop = _host.Online?.CoopDrive;
            if (coop != null && coop.Role == Gooby.Core.Net.CoopDriveClient.RoleDriver) coop.Leave();
            _host.World?.SetCityScene(false);
            _host.Ui.Pop();
        }
    }
}
