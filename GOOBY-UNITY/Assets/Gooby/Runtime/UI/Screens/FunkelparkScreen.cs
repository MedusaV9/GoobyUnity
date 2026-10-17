using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The walkable Funkelpark (funkelpark.gd): the world stages <see cref="World.FunkelparkScene"/>; this HUD carries the
    /// board-10 place card, coins, the stick, the ride bar (Riesenrad, Karussell, Achterbahn, Autoscooter, Naschgasse with
    /// their prices — the ride Gooby stands at glows), ride captions and "Aussteigen". Rides and snacks are booked by
    /// Core <see cref="ParkLogic"/> (coins, a little energy, fun, counters); leaving walks back out to the street.
    /// </summary>
    public sealed class FunkelparkScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly DriveModel.Place _place;
        private Label _coins, _caption;
        private VisualElement _captionCard, _rideBar, _stopBtn, _snackSheet;
        private readonly Dictionary<string, VisualElement> _rideButtons = new Dictionary<string, VisualElement>();
        private float _captionLeft;
        private bool _leaving;
        private string _near = "";
        private World.FunkelparkScene _scene;

        public override string SoundScene => "park";
        public DriveModel.Place Place => _place;

        public FunkelparkScreen(GameHost host, DriveModel.Place place)
        {
            _host = host;
            _place = place;
            Id = "place-" + place.Id;
            Opaque = true;
        }

        private static readonly (string id, string icon)[] RideOrder =
        {
            ("wheel", "ride_wheel"), ("karussell", "ride_carousel"), ("coaster", "ride_coaster"), ("scooter", "ride_scooter"), ("snacks", "food_icecream"),
        };

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.pickingMode = PickingMode.Ignore;
            root.AddToClassList("gb-place-chrome");
            EnterScene();

            var back = Ux.IconBtn("back_white", Leave, "gb-place-back");
            back.name = "leave-place";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.El("gb-card", "gb-row");
            Ux.Abs(head, 84, 12);
            head.style.paddingLeft = 8; head.style.paddingTop = 6; head.style.paddingBottom = 6; head.style.paddingRight = 18;
            var tile = Ux.IconTile(_place.Icon, ArtColor("#FF8B8B"));
            tile.style.borderTopLeftRadius = 22; tile.style.borderTopRightRadius = 22; tile.style.borderBottomLeftRadius = 22; tile.style.borderBottomRightRadius = 22;
            head.Add(tile);
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(t.T(_place.NameKey), "gb-place-title"));
            hcol.Add(Ux.Text(t.T("nl.ort.slogan.funkelpark"), "gb-sm", "gb-mild"));
            head.Add(hcol);
            root.Add(head);

            var tr = Ux.Row("gb-place-top");
            Ux.Abs(tr, null, 12, 16);
            var coinPill = Ux.Pill("coin", Ux.FormatNumber(ShopLogic.Coins(_host.Store)), out _coins);
            coinPill.style.marginRight = 10;
            tr.Add(coinPill);
            tr.Add(Ux.IconBtn("settings_white", () => _host.Ui.Push(new SettingsScreen(_host)), "gb-btn-icon-sm"));
            root.Add(tr);
            _host.Store.CoinsChanged += OnCoins;

            _captionCard = Ux.El("gb-card", "gb-banner");
            _captionCard.name = "park-caption";
            _captionCard.pickingMode = PickingMode.Ignore;
            Ux.Abs(_captionCard, null, 92);
            _captionCard.style.left = Length.Percent(50);
            _captionCard.style.translate = new Translate(Length.Percent(-50), 0);
            _caption = Ux.Text("", "gb-h3");
            _caption.pickingMode = PickingMode.Ignore;
            _captionCard.Add(_caption);
            root.Add(_captionCard);
            ShowCaption(t.T("nl.park.willkommen"), 4.5f);

            var joy = new Joystick();
            joy.name = "park-joystick";
            Ux.Abs(joy, 40, null, null, 28);
            joy.Changed += v => _host.World?.SetMove(v);
            root.Add(joy);

            _rideBar = Ux.Row();
            _rideBar.name = "park-rides";
            Ux.Abs(_rideBar, null, null, null, 22);
            _rideBar.style.left = Length.Percent(50);
            _rideBar.style.translate = new Translate(Length.Percent(-50), 0);
            foreach (var (id, icon) in RideOrder)
            {
                var rideId = id;
                var b = RideButton(rideId, icon);
                b.RegisterCallback<ClickEvent>(_ => Tapped(rideId));
                _rideButtons[rideId] = b;
                _rideBar.Add(b);
            }
            root.Add(_rideBar);

            _stopBtn = Ux.IconTextBtn("close", t.T("nl.park.aussteigen"), () => _host.World?.StopParkRide(), "gb-btn-primary");
            _stopBtn.name = "park-stop";
            Ux.Abs(_stopBtn, null, null, 28, 34);
            _stopBtn.style.display = DisplayStyle.None;
            root.Add(_stopBtn);
        }

        private static Color ArtColor(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        private VisualElement RideButton(string id, string icon)
        {
            var t = _host.Strings;
            var a = Ux.El("gb-action");
            a.name = "ride-" + id;
            a.style.marginLeft = 6; a.style.marginRight = 6;
            a.Add(Ux.Icon(icon));
            var ride = ParkLogic.RideById(id);
            a.Add(Ux.Text(t.T("nl.park.kurz." + id)));
            var price = Ux.Text(id == "snacks" ? "" : ride.Preis > 0 ? t.T("nl.park.preis", ("preis", ride.Preis)) : t.T("nl.park.frei"), "gb-park-price");
            price.pickingMode = PickingMode.Ignore;
            price.style.fontSize = 13;
            price.style.color = new Color(1f, 0.85f, 0.4f);
            price.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (id != "snacks") a.Add(price);
            return a;
        }

        private void EnterScene()
        {
            if (_host.World == null) return;
            if (_host.World.Park == null) _host.World.EnterPark();
            _scene = _host.World.Park;
            if (_scene == null) return;
            _scene.Caption += OnCaption;
            _scene.RideEnded += OnRideEnded;
            _scene.Bumped += OnBumped;
            ParkLogic.BesuchBuchen(_host.Store, ParkLogic.IstNacht(_host.World.LocalHour));
        }

        /// <summary>QA remote: the same as tapping a ride button.</summary>
        public void QaTap(string id) => Tapped(id);

        private void Tapped(string id)
        {
            if (_scene == null || _scene.Riding) return;
            if (id == "snacks")
            {
                OpenSnacks();
                return;
            }
            var t = _host.Strings;
            var ride = ParkLogic.RideById(id);
            var (ok, reason) = ParkLogic.Fahren(_host.Store, ride);
            if (!ok)
            {
                Sfx.PlayUi("soft_error");
                ShowCaption(t.T(reason == "coins" ? "nl.park.zu_teuer" : "nl.park.zu_muede"), 3f);
                return;
            }
            Sfx.PlayUi(ride.Preis > 0 ? "soft_buy" : "soft_confirm");
            _host.Ui.FadeThrough(() =>
            {
                if (_host.World == null || !_host.World.StartParkRide(id)) return;
                _rideBar.style.display = DisplayStyle.None;
                _stopBtn.style.display = DisplayStyle.Flex;
                Haptics.Success();
                Debug.Log("[park] ride " + id);
            }, 0.28f);
        }

        private void OnCaption(string key)
        {
            var t = _host.Strings;
            var text = key == "nl.park.scooter.done" ? t.T(key, ("n", _scene?.Bumps ?? 0)) : t.T(key);
            ShowCaption(text, 3.2f);
            if (key.EndsWith(".apex") || key.EndsWith(".drop") || key.EndsWith(".loop")) _host.Audio?.Say(text, "ecstatic");
        }

        private void OnRideEnded(string id)
        {
            _host.World?.ParkRideFinished();
            _rideBar.style.display = DisplayStyle.Flex;
            _stopBtn.style.display = DisplayStyle.None;
            var ride = ParkLogic.RideById(id);
            if (ride != null && ride.Preis > 0)
            {
                _host.Ui.Toast(_host.Strings.T("nl.park.spass", ("spass", (int)ParkLogic.RideSpass)), "heart", 2.6f);
                _host.Audio?.PlayStinger("stinger-levelup");
            }
            Debug.Log("[park] ride end " + id);
        }

        private void OnBumped()
        {
            Sfx.Play("game_hit", Random.Range(-2f, 2f));
            Haptics.Tap();
            if ((_scene?.Bumps ?? 0) % 3 == 1) ShowCaption(_host.Strings.T("nl.park.scooter.bumm"), 0.9f);
        }

        private void ShowCaption(string text, float seconds)
        {
            if (_caption == null) return;
            _caption.text = text;
            _captionLeft = seconds;
            _captionCard.style.display = DisplayStyle.Flex;
            _captionCard.style.opacity = 1f;
        }

        // ── Naschgasse ───────────────────────────────────────────────────────

        private void OpenSnacks()
        {
            if (_snackSheet != null) return;
            var t = _host.Strings;
            Sfx.PlayUi("soft_open");
            _snackSheet = Ux.El("gb-scrim");
            _snackSheet.name = "park-snacks";
            _snackSheet.RegisterCallback<ClickEvent>(e => { if (e.target == _snackSheet) CloseSnacks(); });
            var card = Ux.El("gb-card");
            card.style.position = Position.Absolute;
            card.style.left = Length.Percent(50); card.style.top = Length.Percent(50);
            card.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            card.style.width = 760;
            card.style.paddingLeft = 24; card.style.paddingRight = 24; card.style.paddingTop = 18; card.style.paddingBottom = 20;
            var title = Ux.Row();
            title.Add(Ux.IconTile("food_icecream", ArtColor("#FFC9D6")));
            var tcol = Ux.Col();
            tcol.style.marginLeft = 12;
            tcol.style.flexGrow = 1;
            tcol.Add(Ux.Text(t.T("park.alley.title"), "gb-h2"));
            tcol.Add(Ux.Text(t.T("park.alley.hint"), "gb-sm", "gb-mild"));
            title.Add(tcol);
            title.Add(Ux.IconBtn("close", CloseSnacks, "gb-btn-icon-sm"));
            card.Add(title);
            var row = Ux.Row();
            row.style.marginTop = 16;
            row.style.justifyContent = Justify.SpaceBetween;
            var icons = new Dictionary<string, string> { ["cottonCandy"] = "food_cake", ["softServe"] = "food_icecream", ["waffle"] = "food_donut" };
            foreach (var stall in ParkLogic.Stalls)
            {
                var s = stall;
                var col = Ux.El("gb-card");
                col.style.width = 220;
                col.style.alignItems = Align.Center;
                col.style.paddingTop = 12; col.style.paddingBottom = 12;
                col.style.backgroundColor = Color.Lerp(ArtColor(s.Tint), Color.white, 0.72f);
                col.Add(Ux.IconTile(icons.TryGetValue(s.Id, out var ic) ? ic : "food_cake", ArtColor(s.Tint)));
                col.Add(Ux.Text(t.T("park.stall." + s.Id + ".name"), "gb-h3"));
                var pitch = Ux.Text(t.T("park.stall." + s.Id + ".pitch"), "gb-sm", "gb-mild");
                pitch.style.whiteSpace = WhiteSpace.Normal;
                pitch.style.unityTextAlign = TextAnchor.MiddleCenter;
                pitch.style.minHeight = 40;
                col.Add(pitch);
                var buy = Ux.Btn(t.T("nl.park.naschen", ("preis", s.Preis)), () => Buy(s), "gb-btn-primary");
                buy.name = "snack-" + s.Id;
                buy.style.marginTop = 8;
                col.Add(buy);
                row.Add(col);
            }
            card.Add(row);
            _snackSheet.Add(card);
            Root.Add(_snackSheet);
        }

        private void Buy(ParkLogic.Stall stall)
        {
            var t = _host.Strings;
            if (!ParkLogic.Naschen(_host.Store, stall))
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(t.T("nl.park.zu_teuer"), "coin", 2.6f);
                return;
            }
            Sfx.PlayUi("soft_buy");
            Haptics.Success();
            var name = t.T("park.food." + stall.Id);
            _host.Ui.Toast(t.T("park.stall.gekauft", ("name", name)), "heart", 2.8f);
            _host.Audio?.Say(name + "!", "happy");
        }

        private void CloseSnacks()
        {
            if (_snackSheet == null) return;
            Sfx.PlayUi("soft_close");
            _snackSheet.RemoveFromHierarchy();
            _snackSheet = null;
        }

        // ── frame ────────────────────────────────────────────────────────────

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            if (_captionLeft > 0f)
            {
                _captionLeft -= dt;
                if (_captionLeft <= 0.4f) _captionCard.style.opacity = Mathf.Clamp01(_captionLeft / 0.4f);
                if (_captionLeft <= 0f) _captionCard.style.display = DisplayStyle.None;
            }
            if (_scene == null || _host.World?.Gooby == null || _scene.Riding) return;
            var near = _scene.RideNear(_host.World.Gooby.position);
            if (near == _near) return;
            _near = near;
            foreach (var kv in _rideButtons) kv.Value.EnableInClassList("gb-action--main", kv.Key == near);
        }

        private void OnCoins(int coins)
        {
            if (_coins != null) _coins.text = Ux.FormatNumber(coins);
        }

        public override void OnShown()
        {
            _leaving = false;
            if (_coins != null) _coins.text = Ux.FormatNumber(ShopLogic.Coins(_host.Store));
        }

        /// <summary>Out through the gate: fade, the world puts the street back, this screen pops.</summary>
        public void Leave()
        {
            if (_leaving || _host.Ui.Current != this) return;
            _leaving = true;
            _host.Ui.FadeThrough(() =>
            {
                Unhook();
                _host.World?.LeavePark();
                if (_host.Ui.Current == this) _host.Ui.Pop();
            });
        }

        private void Unhook()
        {
            if (_scene == null) return;
            _scene.Caption -= OnCaption;
            _scene.RideEnded -= OnRideEnded;
            _scene.Bumped -= OnBumped;
            _scene = null;
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsOpen(this)) return;
            _host.Store.CoinsChanged -= OnCoins;
            _host.World?.SetMove(Vector2.zero);
            Unhook();
            if (_host.World?.Park != null) _host.World.LeavePark();
        }
    }
}
