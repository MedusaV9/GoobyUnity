using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Settings (concept board 07): back pill top-left, DEV badge, left tab column (Spiel/Grafik/Audio/
    /// Steuerung/Sprache/Konto/Sonstiges), right panel with grouped rows. Every row writes AppSettings.
    /// </summary>
    public sealed class SettingsScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _panel;
        private string _tab = "spiel";
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();

        public SettingsScreen(GameHost host, string tab = "spiel")
        {
            _host = host;
            _tab = string.IsNullOrEmpty(tab) ? "spiel" : tab;
            Id = "settings";
            Opaque = true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-set");
            root.AddToClassList("gb-sheets");
            root.style.backgroundColor = new Color(0.98f, 0.90f, 0.80f);
            root.Add(Ux.Backdrop("bg_settings")); // board 07: the softly blurred living room behind the panels

            // back capsule (board 07: charcoal stadium, solid white round back button, title + leaf, subtitle)
            var back = Ux.Row("gb-set-back");
            var backBtn = Ux.IconBtn("back", () => _host.Ui.Pop(), "gb-set-backbtn");
            backBtn.name = "settings-back";
            back.Add(backBtn);
            var bcol = Ux.Col("gb-set-back-texts");
            var titleRow = Ux.Row("gb-set-back-title-row");
            titleRow.Add(Ux.Text(t.T("nl.settings.titel"), "gb-set-back-title"));
            titleRow.Add(Ux.Icon("leaf"));
            bcol.Add(titleRow);
            bcol.Add(Ux.Text(t.T("nl.settings.untertitel"), "gb-set-back-sub"));
            back.Add(bcol);
            root.Add(back);

            if (Debug.isDebugBuild || Application.isEditor)
            {
                var dev = Ux.Text("DEV", "gb-dev");
                Ux.Abs(dev, null, 16, 16);
                root.Add(dev);
            }

            // tabs: one cream column card, the selected tab solid strawberry
            var tabs = Ux.Col("gb-set-tabs");
            foreach (var (id, icon, key) in new[]
                     {
                         ("spiel", "settings", "nl.settings.tab_spiel"), ("grafik", "monitor", "nl.settings.tab_grafik"),
                         ("audio", "speaker", "nl.settings.tab_audio"), ("steuerung", "gamepad", "nl.settings.tab_steuerung"),
                         ("sprache", "globe", "nl.settings.tab_sprache"), ("konto", "user", "nl.settings.tab_konto"),
                         ("sonstiges", "dots", "nl.settings.tab_sonstiges"),
                     })
            {
                var localId = id;
                var b = new Button(() => SelectTab(localId)) { text = "" };
                b.ClearClassList();
                b.AddToClassList("gb-tab");
                b.AddToClassList("gb-tab--hot");
                b.name = "tab-" + id;
                b.Add(Ux.IconTile(icon));
                b.Add(Ux.Text(t.T(key)));
                _tabs[id] = b;
                tabs.Add(b);
            }
            root.Add(tabs);

            // panel
            _panel = Ux.El("gb-set-panel");
            root.Add(_panel);

            // mascot bottom-left: Gooby dozing under the tab column with his thank-you bubble (board 07)
            var mascot = Ux.Row("gb-set-mascot");
            mascot.pickingMode = PickingMode.Ignore;
            mascot.Add(Ux.Image("gooby_sitting", 110, 110));
            var thanks = Ux.El("gb-set-thanks");
            thanks.pickingMode = PickingMode.Ignore;
            thanks.Add(Ux.Text(t.T("nl.settings.danke")));
            mascot.Add(thanks);
            root.Add(mascot);

            SelectTab(_tab);
        }

        private void SelectTab(string id)
        {
            _tab = id;
            foreach (var kv in _tabs) kv.Value.EnableInClassList("gb-tab--active", kv.Key == id);
            _panel.Clear();
            var t = _host.Strings;
            var s = _host.Settings;

            // header (board 07): plain glyph, big title, one-line description, the motto with leaves on the right
            var head = Ux.Row("gb-set-head");
            head.Add(Ux.Icon(IconFor(id), "gb-set-head-icon"));
            var hcol = Ux.Col("gb-set-head-texts");
            hcol.Add(Ux.Text(t.T("nl.settings.tab_" + id), "gb-set-title"));
            hcol.Add(Ux.Text(id == "spiel" ? t.T("nl.settings.spiel_beschreibung") : t.T("nl.settings.beschreibung_" + id), "gb-set-sub"));
            head.Add(hcol);
            var motto = Ux.Row("gb-set-motto");
            motto.Add(Ux.Icon("leaf"));
            motto.Add(Ux.Text(t.T("nl.settings.motto")));
            motto.Add(Ux.Icon("leaf"));
            head.Add(motto);
            _panel.Add(head);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-set-scroll");
            _panel.Add(scroll);

            switch (id)
            {
                case "spiel":
                case "sprache":
                {
                    var g = Ux.Group("book", t.T("nl.settings.gruppe_allgemein"));
                    var lang = Ux.Segment(new List<(string, string)> { ("de", t.T("nl.settings.deutsch")), ("en", t.T("nl.settings.english")) },
                            s.Locale, loc =>
                            {
                                s.SetLocale(loc);
                                _host.Strings.SetLocale(loc);
                                Root.Clear();
                                Build(Root);
                            }, out _);
                    LanguageChips(lang);
                    g.Add(Ux.SettingRow(t.T("nl.settings.sprache"), t.T("nl.settings.sprache_hint"), lang));
                    g.Add(Ux.SettingRow(t.T("nl.settings.mg_ausrichtung"), t.T("nl.settings.mg_ausrichtung_hint"),
                        Ux.Dropdown(new List<(string, string)>
                        {
                            ("auto", t.T("nl.settings.ausrichtung_auto")), ("landscape", t.T("nl.settings.ausrichtung_quer")), ("portrait", t.T("nl.settings.ausrichtung_hoch")),
                        }, s.MinigameOrientation, s.SetMinigameOrientation)));
                    scroll.Add(Polish(g));
                    if (id == "spiel")
                    {
                        // dlc_sektion.gd: the DLC library lives under the game settings
                        var dg = Ux.Group("gift", t.T("dlc.settings_titel"));
                        var dlcs = Gooby.Core.Content.ContentSource.LoadItems("content/dlc/data/dlcs").Count;
                        var open = Ux.IconTextBtn("gift", t.T("dlc.settings_knopf"), () => _host.Ui.Push(new DlcScreen(_host)), "gb-btn-primary", "gb-btn-sm");
                        open.name = "settings-dlc";
                        dg.Add(Ux.SettingRow(t.T("dlc.settings_titel"), t.T("dlc.settings_info", ("spielbar", dlcs - 1), ("bald", 1)), open));
                        scroll.Add(Polish(dg));
                        scroll.Add(ControlsGroup());
                        scroll.Add(DisplayGroup());
                    }
                    break;
                }
                case "steuerung":
                    scroll.Add(ControlsGroup());
                    break;
                case "grafik":
                {
                    var g = Ux.Group("monitor", t.T("nl.settings.gruppe_qualitaet"));
                    g.Add(Ux.SettingRow(t.T("nl.settings.grafik_preset"), t.T("nl.settings.grafik_preset_hint"),
                        Ux.Dropdown(new List<(string, string)>
                        {
                            ("auto", t.T("nl.settings.preset_auto")), ("sparsam", t.T("nl.settings.preset_sparsam")), ("schoen", t.T("nl.settings.preset_schoen")),
                        }, s.GraphicsPreset, s.SetGraphicsPreset)));
                    scroll.Add(Polish(g));
                    scroll.Add(DisplayGroup());
                    break;
                }
                case "audio":
                {
                    var g = Ux.Group("speaker", t.T("nl.settings.gruppe_lautstaerke"));
                    var ml = Ux.Text("", "gb-set-value");
                    var mrow = Ux.Row("gb-set-slider");
                    mrow.Add(Ux.Slider(s.MusicVolume, s.SetMusicVolume, ml));
                    mrow.Add(ml);
                    g.Add(Ux.SettingRow(t.T("nl.settings.musik"), "", mrow));
                    var sl = Ux.Text("", "gb-set-value");
                    var srow = Ux.Row("gb-set-slider");
                    srow.Add(Ux.Slider(s.SfxVolume, s.SetSfxVolume, sl));
                    srow.Add(sl);
                    g.Add(Ux.SettingRow(t.T("nl.settings.effekte"), "", srow));
                    g.Add(Ux.SettingRow(t.T("nl.settings.haptik"), t.T("nl.settings.haptik_hint"),
                        Ux.Segment(new List<(string, string)>
                        {
                            ("dezent", t.T("nl.settings.haptik_dezent")), ("normal", t.T("nl.settings.haptik_normal")), ("stark", t.T("nl.settings.haptik_stark")),
                        }, s.HapticStrength, v => { s.SetHapticStrength(v); }, out var setHaptic)));
                    scroll.Add(Polish(g));
                    break;
                }
                case "konto":
                {
                    scroll.Add(OnlineGroup());
                    // SAVE-TRANSFER ways A + B live here (Einstellungen → Konto → Spielstand übertragen)
                    var tg = Ux.Group("books", t.T("nl.transfer.titel"));
                    tg.Add(Ux.SettingRow(t.T("nl.transfer.titel"), t.T("nl.transfer.untertitel"),
                        Ux.IconTextBtn("books", t.T("nl.transfer.titel"), () => _host.Ui.Push(new TransferScreen(_host, _host.LegacyCandidate)), "gb-btn-primary", "gb-btn-sm")));
                    tg.Add(Ux.SettingRow(t.T("nl.transfer.exportieren"), t.T("nl.transfer.kopiert"),
                        Ux.IconTextBtn("copy", t.T("nl.transfer.exportieren"), () => { GUIUtility.systemCopyBuffer = _host.Legacy.ExportCode(); }, "gb-btn-teal", "gb-btn-sm")));
                    scroll.Add(Polish(tg));
                    // codes_screen.gd + the account move (Doc C §4 / §7)
                    var cg = Ux.Group("gift", t.T("nl.settings.codes_umzug"));
                    var codes = Ux.IconTextBtn("gift", t.T("codes.einloesen"), () => _host.Ui.Push(new CodesScreen(_host)), "gb-btn-primary", "gb-btn-sm");
                    codes.name = "settings-codes";
                    cg.Add(Ux.SettingRow(t.T("codes.settings_eintrag"), t.T("codes.hinweis"), codes));
                    var move = Ux.IconTextBtn("user", t.T("umzug.titel"), () => _host.Ui.Push(new UmzugScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
                    move.name = "settings-umzug";
                    cg.Add(Ux.SettingRow(t.T("umzug.settings_eintrag"), t.T("umzug.intro"), move));
                    scroll.Add(Polish(cg));
                    break;
                }
                default:
                {
                    var g = Ux.Group("dots", t.T("nl.settings.tab_sonstiges"));
                    var version = Ux.Text(t.T("nl.settings.version", ("version", Application.version)), "gb-lg", "gb-bold");
                    version.name = "set-version";
                    g.Add(Ux.SettingRow(t.T("nl.settings.version"), "", version));
                    // dev_trigger.gd: seven taps on the version unlock the developer toolbox (kept in the settings)
                    var taps = 0;
                    var lastTap = 0f;
                    version.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (_host.Settings.DevUnlocked) { _host.Ui.Push(new DevScreen(_host)); return; }
                        taps = Time.unscaledTime - lastTap < 2f ? taps + 1 : 1;
                        lastTap = Time.unscaledTime;
                        if (taps < 7) return;
                        _host.Settings.SetDevUnlocked(true);
                        _host.Ui.Toast(t.T("nl.dev.freigeschaltet"), "sparkle");
                        _host.Ui.Push(new DevScreen(_host));
                    });
                    if (_host.Settings.DevUnlocked)
                    {
                        var dev = Ux.IconTextBtn("toolbox", t.T("nl.dev.oeffnen"), () => _host.Ui.Push(new DevScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
                        dev.name = "set-dev";
                        g.Add(Ux.SettingRow(t.T("nl.dev.titel"), t.T("nl.dev.untertitel"), dev));
                    }
                    var neu = Ux.IconTextBtn("sparkle", t.T("nl.neu.knopf"), () => _host.Ui.Push(new WhatsNewScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
                    neu.name = "set-neu";
                    g.Add(Ux.SettingRow(t.T("nl.neu.titel"), t.T("nl.neu.einstellungen_hint"), neu));
                    var btn = Ux.IconTextBtn("books", t.T("nl.transfer.titel"), () => _host.Ui.Push(new TransferScreen(_host, _host.LegacyCandidate)), "gb-btn-secondary", "gb-btn-sm");
                    g.Add(Ux.SettingRow(t.T("nl.transfer.titel"), t.T("nl.transfer.untertitel"), btn));
                    scroll.Add(Polish(g));
                    break;
                }
            }
        }

        /// <summary>
        /// Board 07 row rhythm for the groups built here: the first row sits right under the header band (no divider),
        /// the title/hint column may shrink so long hints wrap instead of pushing the control out of the panel.
        /// </summary>
        private static VisualElement Polish(VisualElement group)
        {
            var first = true;
            foreach (var child in group.Children())
            {
                if (!child.ClassListContains("gb-setting-row")) continue;
                if (first) child.AddToClassList("gb-set-row--first");
                first = false;
                if (child.childCount > 0) child[0].AddToClassList("gb-set-texts");
            }
            return group;
        }

        /// <summary>Language segment (board 07): the label moves into a child so the active chip can carry its check disc.</summary>
        private static void LanguageChips(VisualElement segment)
        {
            foreach (var child in segment.Children())
            {
                if (!(child is Button chip)) continue;
                var label = chip.text;
                chip.text = "";
                chip.AddToClassList("gb-chip--teal");
                chip.AddToClassList("gb-set-lang");
                var l = Ux.Text(label);
                l.pickingMode = PickingMode.Ignore;
                chip.Add(l);
                if (chip.ClassListContains("gb-chip--active"))
                {
                    var disc = Ux.El("gb-set-check");
                    disc.pickingMode = PickingMode.Ignore;
                    disc.Add(Ux.Icon("check"));
                    chip.Add(disc);
                }
            }
        }

        // ── Mehrspieler (mehrspieler_sektion.gd): the server the player types in ─────────────

        private Label _onlineStatus, _onlineCode;
        private TextField _serverField, _secretField, _nameField;
        private Label _serverWarn;
        private Button _connectBtn;
        private VisualElement _onlineCard, _onlineDot;

        /// <summary>
        /// Board 07 group: help line, a status card that takes the colour of the connection (green online, sun while
        /// connecting, strawberry for errors) with Verbinden / Trennen, the three fields as setting rows, auto-connect
        /// and the own friend code as a ticket with copy and "Freunde".
        /// </summary>
        private VisualElement OnlineGroup()
        {
            var t = _host.Strings;
            var online = _host.Online;
            var g = Ux.Group("friends", t.T("online.settings.gruppe"));
            g.name = "online-group";
            g.AddToClassList("gb-online");
            g.Add(Ux.Text(t.T("online.settings.hilfe"), "gb-mp-help"));

            _onlineCard = Ux.Row("gb-mp-status");
            _onlineCard.Add(Ux.IconTile("globe"));
            var scol = Ux.Col("gb-mp-status-col");
            var kicker = Ux.Row("gb-mp-status-kicker");
            _onlineDot = Ux.El("gb-status-dot");
            kicker.Add(_onlineDot);
            kicker.Add(Ux.Text(t.T("online.settings.verbindung")));
            scol.Add(kicker);
            _onlineStatus = Ux.Text("", "gb-mp-status-text");
            _onlineStatus.name = "online-status";
            scol.Add(_onlineStatus);
            _onlineCard.Add(scol);
            _connectBtn = Ux.IconTextBtn("globe", t.T("online.settings.verbinden"), () =>
            {
                online.SetPlayerName(_nameField.value, apply: false);
                online.ConnectTo(_serverField.value, _secretField.value);
                Sfx.PlayUi("soft_confirm");
                RefreshOnline();
            }, "gb-btn-primary", "gb-btn-sm");
            _connectBtn.name = "online-connect";
            _onlineCard.Add(_connectBtn);
            var off = Ux.IconTextBtn("close", t.T("online.settings.trennen"), () => { online.GoOffline(); RefreshOnline(); }, "gb-btn-secondary", "gb-btn-sm");
            off.name = "online-disconnect";
            _onlineCard.Add(off);
            g.Add(_onlineCard);

            _serverField = Field("server-address", online.ServerAddress, t.T("netset.mp.server_hinweis"), 64, false);
            g.Add(Ux.SettingRow(t.T("netset.mp.server"), t.T("online.settings.server_hint"), _serverField));
            _serverWarn = Ux.Text("", "gb-mp-warn");
            _serverWarn.name = "server-warning";
            g.Add(_serverWarn);
            _serverField.RegisterValueChangedCallback(e => RefreshServerWarning(e.newValue));
            RefreshServerWarning(_serverField.value);
            _secretField = Field("server-secret", _host.Settings.ServerSecret, t.T("netset.mp.secret_hinweis"), 64, true);
            g.Add(Ux.SettingRow(t.T("netset.mp.secret"), t.T("online.settings.secret_hint"), _secretField));
            _nameField = Field("player-name", J.Str(J.Path(_host.Store.State, "meta", "playerName")), online.PlayerName, 24, false);
            _nameField.RegisterCallback<FocusOutEvent>(_ => online.SetPlayerName(_nameField.value));
            g.Add(Ux.SettingRow(t.T("online.settings.name"), t.T("online.settings.name_hint"), _nameField));

            g.Add(Ux.SettingRow(t.T("online.settings.auto"), t.T("online.settings.auto_hint"), Ux.Toggle(_host.Settings.OnlineAuto, _host.Settings.SetOnlineAuto)));

            var codeRow = Ux.Row("gb-mp-code");
            var ticket = Ux.Row("gb-code-ticket", "gb-code-ticket--sm");
            _onlineCode = Ux.Text("—", "gb-code-value", "gb-nowrap");
            _onlineCode.name = "online-code";
            ticket.Add(_onlineCode);
            ticket.Add(Ux.IconBtn("copy", () =>
            {
                if (online.Client.FriendCode.Length == 0) return;
                GUIUtility.systemCopyBuffer = online.Client.FriendCode;
                _host.Ui.Toast(t.T("net.friends.copied"), "copy", 1.6f);
            }, "gb-btn-icon-sm"));
            codeRow.Add(ticket);
            codeRow.Add(Ux.IconTextBtn("friends", t.T("online.settings.freunde"), () => _host.Ui.Push(new FriendsScreen(_host)), "gb-btn-teal", "gb-btn-sm"));
            g.Add(Ux.SettingRow(t.T("net.friends.my_code"), t.T("online.settings.code_hint"), codeRow));
            RefreshOnline();
            return g;
        }

        /// <summary>
        /// Plain ws:// to an internet host stays allowed (a home server is usually reached by bare IP), but the player
        /// learns that the device secret then travels unencrypted.
        /// </summary>
        private void RefreshServerWarning(string text)
        {
            if (_serverWarn == null) return;
            var raw = (text ?? "").Trim();
            var ep = raw.Length > 0 ? Gooby.Core.Net.NetAddress.Parse(raw) : Gooby.Core.Net.NetEndpoint.Invalid;
            var key = raw.Length == 0 ? null
                : !ep.Valid ? "online.settings.ungueltig"
                : !ep.Tls && !ep.IsPrivate ? "online.settings.unverschluesselt"
                : null;
            _serverWarn.text = key != null ? _host.Strings.T(key) : "";
            _serverWarn.style.display = key != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static TextField Field(string name, string value, string placeholder, int max, bool password)
        {
            var f = new TextField { maxLength = max, isPasswordField = password };
            f.name = name;
            f.AddToClassList("gb-textfield");
            f.AddToClassList("gb-mp-field");
            f.value = value ?? "";
            f.textEdition.placeholder = placeholder;
            return f;
        }

        private void RefreshOnline()
        {
            if (_onlineStatus == null || _onlineStatus.panel == null) return;
            var online = _host.Online;
            var c = online.Client;
            _onlineStatus.text = online.StatusText();
            _onlineCode.text = c.FriendCode.Length > 0 ? c.FriendCode : "—";
            var on = c.Status == Gooby.Core.Net.NetStatus.Online;
            var wait = c.Status == Gooby.Core.Net.NetStatus.Connecting;
            var err = !on && !wait && c.LastErrorCode.Length > 0;
            _onlineCard.EnableInClassList("gb-mp-status--on", on);
            _onlineCard.EnableInClassList("gb-mp-status--wait", wait);
            _onlineCard.EnableInClassList("gb-mp-status--off", err);
            _onlineDot.EnableInClassList("gb-status-dot--on", on);
            _onlineDot.EnableInClassList("gb-status-dot--wait", wait);
            _onlineDot.EnableInClassList("gb-status-dot--off", err);
        }

        public override void OnShown()
        {
            _host.Online.Changed -= RefreshOnline;
            _host.Online.Changed += RefreshOnline;
            RefreshOnline();
        }

        public override void OnHidden() => _host.Online.Changed -= RefreshOnline;

        private VisualElement ControlsGroup()
        {
            var t = _host.Strings;
            var s = _host.Settings;
            var g = Ux.Group("gamepad", t.T("nl.settings.gruppe_steuerung"));
            g.Add(Ux.SettingRow(t.T("nl.settings.haendigkeit"), t.T("nl.settings.haendigkeit_hint"),
                Ux.Dropdown(new List<(string, string)> { ("right", t.T("nl.settings.rechts")), ("left", t.T("nl.settings.links")) }, s.Handedness, s.SetHandedness)));
            g.Add(Ux.SettingRow(t.T("nl.settings.steuerung_anzeigen"), t.T("nl.settings.steuerung_anzeigen_hint"),
                Ux.Toggle(s.ShowControls, s.SetShowControls)));
            return Polish(g);
        }

        private VisualElement DisplayGroup()
        {
            var t = _host.Strings;
            var s = _host.Settings;
            var g = Ux.Group("monitor", t.T("nl.settings.gruppe_anzeige"));
            var valueLabel = Ux.Text("", "gb-set-value");
            var row = Ux.Row("gb-set-slider");
            // slider 0..1 ↔ scale 0.8..1.3
            row.Add(Ux.Slider((s.UiScale - 0.8) / 0.5, v => s.SetUiScale(0.8 + v * 0.5), valueLabel, v => Mathf.RoundToInt((float)((0.8 + v * 0.5) * 100)) + "%"));
            row.Add(valueLabel);
            g.Add(Ux.SettingRow(t.T("nl.settings.ui_groesse"), t.T("nl.settings.ui_groesse_hint"), row));
            g.Add(Ux.SettingRow(t.T("nl.settings.weniger_bewegung"), t.T("nl.settings.weniger_bewegung_hint"), Ux.Toggle(s.ReducedMotion, s.SetReducedMotion)));
            // the accessibility block of settings_screen.gd: colour-vision aid, high contrast, how long hints stay
            var farben = Ux.Dropdown(new List<(string, string)>
            {
                (Gooby.Core.Meta.ColorVision.Aus, t.T("settings.farbmodus_aus")), (Gooby.Core.Meta.ColorVision.Protan, t.T("settings.farbmodus_protan")),
                (Gooby.Core.Meta.ColorVision.Deutan, t.T("settings.farbmodus_deutan")), (Gooby.Core.Meta.ColorVision.Tritan, t.T("settings.farbmodus_tritan")),
            }, s.ColorVisionMode, s.SetColorVision);
            farben.name = "set-farbsehen";
            g.Add(Ux.SettingRow(t.T("settings.farbmodus"), t.T("settings.farbmodus_hilfe"), farben));
            var kontrast = Ux.Toggle(s.HighContrast, s.SetHighContrast);
            kontrast.name = "set-kontrast";
            g.Add(Ux.SettingRow(t.T("settings.hoher_kontrast"), t.T("nl.settings.kontrast_hint"), kontrast));
            g.Add(Ux.SettingRow(t.T("settings.hinweisdauer"), t.T("nl.settings.hinweisdauer_hint"),
                Ux.Dropdown(new List<(string, string)> { ("normal", t.T("settings.hinweisdauer_normal")), ("lang", t.T("settings.hinweisdauer_lang")) }, s.HintDuration, s.SetHintDuration)));
            return Polish(g);
        }

        private static string IconFor(string id)
        {
            switch (id)
            {
                case "grafik": return "monitor";
                case "audio": return "speaker";
                case "steuerung": return "gamepad";
                case "sprache": return "globe";
                case "konto": return "user";
                case "sonstiges": return "dots";
                default: return "gamepad"; // board 07: the "Spiel" panel header carries the gamepad, the tab the gear
            }
        }
    }
}
