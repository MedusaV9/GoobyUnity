using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// IGohbie — Gooby's phone (port of phone_shell.gd + phone_apps.gd). A landscape device pops up over the room: a
    /// status bar (clock, coins, battery = Gooby's energy), a grid of colourful app tiles with white glyphs and a home bar
    /// that closes it. The apps gather what used to crowd the HUD: GOOBERANDO, the radio, the post, friends, InstantGooby,
    /// the camera (after buying it at POW!), the taxi, the sticker album and the achievements. A tile opens its screen
    /// over the phone; back from it, the phone is still there.
    /// </summary>
    public sealed class PhoneScreen : UiScreen
    {
        private readonly GameHost _host;
        private Label _clock, _battery, _coins;
        private float _tick;

        private sealed class App
        {
            public string Id, Icon, NameKey;
            public Color Tile;
            public Func<bool> Open;
            public Func<UiScreen> Make;
            public Func<int> Badge;
            public string LockedKey;
        }

        public PhoneScreen(GameHost host)
        {
            _host = host;
            Id = "phone";
        }

        /// <summary>The app list in grid order (ids are the element names "phone-app-&lt;id&gt;").</summary>
        private List<App> Apps()
        {
            var online = _host.Online?.Client;
            return new List<App>
            {
                new App { Id = "gooberando", Icon = "app_gooberando", NameKey = "phone.app.gooberando", Tile = Hex("#FF8B8B"), Make = () => new GooberandoScreen(_host) },
                new App { Id = "post", Icon = "mail_white", NameKey = "nl.phone.post", Tile = Hex("#FFC24D"), Make = () => new MailboxScreen(_host), Badge = () => online?.MailUnread ?? 0 },
                new App { Id = "freunde", Icon = "app_freunde", NameKey = "phone.app.freunde", Tile = Hex("#6CC58E"), Make = () => new FriendsScreen(_host), Badge = () => online?.Requests.Count ?? 0 },
                new App { Id = "instant", Icon = "app_instant", NameKey = "phone.app.instant", Tile = Hex("#FF8FBF"), Make = () => new InstantScreen(_host) },
                new App { Id = "kamera", Icon = "app_kamera", NameKey = "phone.app.kamera", Tile = Hex("#9C8CF0"), Make = () => new PhotoModeScreen(_host), Open = CameraOwned, LockedKey = "phone.app.kamera_gesperrt" },
                new App { Id = "galerie", Icon = "photos_white", NameKey = "nl.phone.galerie", Tile = Hex("#7FB8FF"), Make = () => new GalleryScreen(_host) },
                new App { Id = "taxi", Icon = "app_taxi", NameKey = "phone.app.taxi", Tile = Hex("#F2A65A"), Make = () => new TaxiScreen(_host) },
                new App { Id = "radio", Icon = "radio_white", NameKey = "nl.phone.radio", Tile = Hex("#5FB6C9"), Make = () => new RadioScreen(_host) },
                new App { Id = "goobay", Icon = "gift", NameKey = "goobay.titel", Tile = Hex("#BFEBD0"), Make = () => new GoobayScreen(_host) },
                new App
                {
                    Id = "goobye", Icon = "shop", NameKey = "dlc_goobye.titel", Tile = Hex("#A7DCD6"),
                    Make = () => Gooby.Core.Dlc.GoobyeState.IstGekauft(_host.Store.State) ? new GoobyeScreen(_host) : (UiScreen)new DlcScreen(_host),
                },
                new App { Id = "mcgooby", Icon = "food_burger", NameKey = "dlc_mcgooby.titel", Tile = Hex("#FFD166"), Make = () => new McGoobyScreen(_host) },
                new App { Id = "ranch", Icon = "horseshoe", NameKey = "ranch.name", Tile = Hex("#F7C59F"), Make = () => new RanchHofScreen(_host) },
                new App { Id = "goobea", Icon = "sofa", NameKey = "shop.ikea.app", Tile = Hex("#8FB0F0"), Make = () => new GoobeaScreen(_host) },
                new App { Id = "album", Icon = "star", NameKey = "album.titel", Tile = Hex("#FFE7A3"), Make = () => new AlbumScreen(_host) },
                new App { Id = "erfolge", Icon = "trophy", NameKey = "achievements.titel", Tile = Hex("#FFD9C2"), Make = () => new AchievementsScreen(_host) },
            };
        }

        private bool CameraOwned() => J.IsTrue(J.Path(_host.Store.State, "city", "flags", "kamera_gekauft"));

        private static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim", "gb-phone-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);
            var device = Ux.El("gb-phone");
            device.name = "phone";
            device.AddToClassList("gb-pop-in");
            device.AddToClassList("gb-pop-in--pre");
            device.schedule.Execute(() => device.RemoveFromClassList("gb-pop-in--pre")).StartingIn(16);
            var screen = Ux.El("gb-phone-screen");
            device.Add(screen);
            // status bar: brand + clock left, coins + battery (Gooby's energy) right
            var bar = Ux.Row("gb-phone-status");
            bar.Add(Ux.Text("IGohbie", "gb-phone-brand"));
            _clock = Ux.Text("", "gb-phone-clock");
            bar.Add(_clock);
            var spacer = Ux.El(); spacer.style.flexGrow = 1;
            bar.Add(spacer);
            var coin = Ux.Icon("coin"); coin.AddToClassList("gb-phone-status-icon");
            bar.Add(coin);
            _coins = Ux.Text("", "gb-phone-status-text");
            bar.Add(_coins);
            _battery = Ux.Text("", "gb-phone-status-text");
            _battery.style.marginLeft = 14;
            bar.Add(_battery);
            screen.Add(bar);
            // the app grid
            var grid = Ux.El("gb-phone-grid");
            var index = 0;
            foreach (var app in Apps())
            {
                var a = app;
                var open = a.Open == null || a.Open();
                var cell = new Button(() => Launch(a, open)) { text = "", name = "phone-app-" + a.Id };
                cell.ClearClassList();
                cell.AddToClassList("gb-phone-app");
                cell.AddToClassList("gb-arcade-card");
                if (!open) cell.AddToClassList("gb-phone-app--locked");
                var tile = Ux.El("gb-phone-tile");
                tile.style.backgroundColor = a.Tile;
                var glyph = Ux.Icon(a.Icon, "gb-phone-glyph");
                tile.Add(glyph);
                if (!open)
                {
                    var lockIcon = Ux.Icon("lock_white", "gb-phone-lock");
                    tile.Add(lockIcon);
                }
                var badge = a.Badge?.Invoke() ?? 0;
                if (badge > 0) tile.Add(Ux.Badge(badge > 9 ? "9+" : badge.ToString()));
                cell.Add(tile);
                cell.Add(Ux.Text(t.HasKey(a.NameKey) ? t.T(a.NameKey) : a.Id, "gb-phone-label"));
                cell.style.transitionDelay = new List<TimeValue> { new TimeValue(0.03f * index, TimeUnit.Second) };
                grid.Add(cell);
                index++;
            }
            screen.Add(grid);
            var home = Ux.El("gb-phone-home");
            home.name = "phone-home";
            home.RegisterCallback<ClickEvent>(_ => Close());
            screen.Add(home);
            root.Add(device);
            Sfx.PlayUi("soft_open");
            Refresh();
        }

        private void Launch(App app, bool open)
        {
            if (!open)
            {
                Sfx.PlayUi("soft_error");
                Toast(_host.Strings.T(app.LockedKey));
                return;
            }
            Sfx.PlayUi("soft_tap");
            var screen = app.Make?.Invoke();
            if (screen != null) _host.Ui.Push(screen);
        }

        private void Toast(string text) => _host.Ui.Toast(text);

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        public override void OnShown() => Refresh();

        public override void Update()
        {
            if ((_tick += Time.unscaledDeltaTime) < 1f) return;
            _tick = 0f;
            Refresh();
        }

        private void Refresh()
        {
            if (_clock == null) return;
            var nowMs = _host.Store.Clock.NowMs();
            var local = System.DateTimeOffset.FromUnixTimeMilliseconds(nowMs + _host.Store.Clock.UtcOffsetMinutes(nowMs) * 60000L).UtcDateTime;
            _clock.text = local.ToString("HH:mm");
            _coins.text = Ux.FormatNumber(J.Long(J.Path(_host.Store.State, "economy", "coins")));
            var energy = Mathf.RoundToInt((float)J.Num(J.Path(_host.Store.State, "gooby", "stats", "energy")));
            _battery.text = "🔋 " + Mathf.Clamp(energy, 0, 100) + " %";
        }
    }
}
