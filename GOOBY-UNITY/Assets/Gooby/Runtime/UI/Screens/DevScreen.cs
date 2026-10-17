using System;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The developer toolbox (port of dev_menu.gd and its tabs): unlocked by tapping the version seven times in
    /// Settings → Sonstiges. Save (coins, XP, full needs, ill/healthy, guide again, the news again, a daily quest done),
    /// time (the clock forward by hours or a day, back to real time — crops, weather and the day's parcel follow),
    /// world (a fright moment, a random home event, the garage, the colour-vision modes), a command line for every
    /// QA command of the tours, and network + frame-rate info. Everything goes through the same paths as the game.
    /// </summary>
    public sealed class DevScreen : UiScreen
    {
        private readonly GameHost _host;
        private QaDriver _qa;
        private Label _clock, _result, _net, _coins;
        private TextField _cmd;
        private float _tick;

        public DevScreen(GameHost host)
        {
            _host = host;
            Id = "dev";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            _qa = QaDriver.ForDevMenu(_host);
            var ui = CounterPanel.Build(root, _host, "toolbox", new Color(0.86f, 0.9f, 1f), t.T("nl.dev.titel"), t.T("nl.dev.untertitel"), () => _host.Ui.Pop());
            _coins = ui.Coins;
            var col = Ux.Col("gb-dev-col");

            Section(col, "coin", t.T("nl.dev.spielstand"),
                (t.T("nl.dev.muenzen"), () => Run("coins " + (J.Int(J.Path(_host.Store.State, "economy", "coins")) + 500))),
                (t.T("nl.dev.xp"), AddXp),
                (t.T("nl.dev.werte_voll"), FillNeeds),
                (t.T("nl.dev.krank"), () => Run("krank sick")),
                (t.T("nl.dev.gesund"), () => Run("krank healthy")),
                (t.T("nl.dev.einfuehrung"), () => { _host.Ui.PopToRoot(); Run("guide start"); }),
                (t.T("nl.dev.neu"), () => _host.Ui.Push(new WhatsNewScreen(_host))),
                (t.T("nl.dev.quest"), () => Run("quest done")));

            var time = Section(col, "sun", t.T("nl.dev.zeit"),
                (t.T("nl.dev.plus_stunde"), () => Shift(DevTools.HourMs)),
                (t.T("nl.dev.plus_6"), () => Shift(6 * DevTools.HourMs)),
                (t.T("nl.dev.plus_tag"), () => Shift(24 * DevTools.HourMs)),
                (t.T("nl.dev.echtzeit"), () => { DevTools.ResetOffset(); Note(t.T("nl.dev.echtzeit")); }));
            _clock = Ux.Text("", "gb-dev-line");
            time.Add(_clock);

            Section(col, "sparkle", t.T("nl.dev.welt"),
                (t.T("nl.dev.schreck"), () => { _host.Ui.PopToRoot(); Run("seele gefuehl donner"); }),
                (t.T("nl.dev.ereignis"), RandomEvent),
                (t.T("nl.dev.garage"), () => Run("garage kaufen")),
                (t.T("nl.dev.farbsehen"), CycleColourVision));

            var cmd = Section(col, "pencil", t.T("nl.dev.befehl"));
            var line = Ux.Row("gb-dev-cmd");
            _cmd = new TextField { name = "dev-cmd", value = "" };
            _cmd.AddToClassList("gb-dev-field");
            line.Add(_cmd);
            var go = Ux.IconTextBtn("play", t.T("nl.dev.ausfuehren"), () => Run(_cmd.value), "gb-btn-primary", "gb-btn-sm");
            go.name = "dev-run";
            line.Add(go);
            cmd.Add(line);
            cmd.Add(Ux.Text(t.T("nl.dev.befehl_hint"), "gb-dev-hint"));

            var net = Section(col, "globe", t.T("nl.dev.netz"),
                (t.T(DevTools.FpsShown ? "nl.dev.fps_aus" : "nl.dev.fps_an"), ToggleFps));
            _net = Ux.Text("", "gb-dev-line");
            _net.style.whiteSpace = WhiteSpace.Normal;
            net.Add(_net);

            _result = Ux.Text("", "gb-dev-result");
            _result.name = "dev-result";
            col.Add(_result);
            ui.Body.Add(col);
            Refresh();
        }

        private VisualElement Section(VisualElement parent, string icon, string title, params (string label, Action run)[] buttons)
        {
            var box = Ux.Col("gb-dev-section");
            var head = Ux.Row("gb-dev-head");
            head.Add(Ux.IconTile(icon));
            head.Add(Ux.Text(title, "gb-dev-title"));
            box.Add(head);
            if (buttons.Length > 0)
            {
                var chips = Ux.Row("gb-dev-chips");
                foreach (var (label, run) in buttons)
                {
                    var b = Ux.Btn(label, () => { run(); Refresh(); }, "gb-btn-secondary", "gb-btn-sm");
                    b.AddToClassList("gb-dev-chip");
                    chips.Add(b);
                }
                box.Add(chips);
            }
            parent.Add(box);
            return box;
        }

        private void Run(string line)
        {
            var ok = _qa.RunCommand(line);
            Note((ok ? "✓ " : "✗ ") + line);
        }

        private void Note(string text)
        {
            if (_result != null) _result.text = text;
            Sfx.PlayUi("soft_tick");
        }

        private void AddXp()
        {
            _host.Store.Update(s =>
            {
                var prog = J.EnsureDict(s, "progression");
                var res = Leveling.ApplyXp(J.NumOr(prog["xp"], 0.0), (int)J.NumOr(prog["level"], 1.0), 250);
                prog["xp"] = res.Xp;
                prog["level"] = res.Level;
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged("progression");
            Note("+250 XP");
        }

        private void FillNeeds()
        {
            _host.Store.Update(s =>
            {
                var stats = J.EnsureDict(J.EnsureDict(s, "gooby"), "stats");
                foreach (var k in new[] { "hunger", "energy", "hygiene", "fun" }) stats[k] = 100.0;
            }, GameHost.TicksMs);
            _host.Store.NotifySliceChanged("gooby");
            Note(_host.Strings.T("nl.dev.werte_voll"));
        }

        private void Shift(long ms)
        {
            DevTools.AddOffset(ms);
            Note("+" + ms / DevTools.HourMs + " h");
        }

        private void RandomEvent()
        {
            var defs = RandomEvents.Defs();
            if (defs.Count == 0) return;
            var id = J.Str(defs[UnityEngine.Random.Range(0, defs.Count)]["id"]);
            _host.Ui.PopToRoot();
            Run("ereignis " + id);
        }

        private void CycleColourVision()
        {
            var modes = Gooby.Core.Meta.ColorVision.Modi;
            var i = Array.IndexOf(modes, _host.Settings.ColorVisionMode);
            var next = modes[(i + 1) % modes.Length];
            _host.Settings.SetColorVision(next);
            Note(_host.Strings.T("settings.farbmodus") + ": " + next);
        }

        private void ToggleFps()
        {
            DevTools.ShowFps(_host.Ui.Document, !DevTools.FpsShown);
            Note(DevTools.FpsShown ? "FPS an" : "FPS aus");
        }

        public override void Update()
        {
            _tick -= Time.unscaledDeltaTime;
            if (_tick > 0f) return;
            _tick = 1f;
            Refresh();
        }

        private void Refresh()
        {
            if (_clock == null) return;
            var t = _host.Strings;
            var now = _host.Store.Clock.NowMs();
            var local = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
            var culture = System.Globalization.CultureInfo.GetCultureInfo(_host.Settings.Locale == "en" ? "en-GB" : "de-DE");
            _clock.text = t.T("nl.dev.uhr", ("zeit", local.ToString("ddd dd.MM. HH:mm", culture)), ("plus", DevTools.OffsetMs / DevTools.HourMs));
            if (_coins != null) _coins.text = Ux.FormatNumber(J.Int(J.Path(_host.Store.State, "economy", "coins")));
            var s = _host.Settings;
            var online = _host.Online?.Client != null && _host.Online.Client.IsOnline;
            _net.text = t.T("nl.dev.netz_zeile", ("server", string.IsNullOrEmpty(s.ServerAddress) ? "—" : s.ServerAddress), ("status", online ? "online" : "offline"))
                + "  ·  " + Application.version + "  ·  " + SystemInfo.graphicsDeviceType;
        }
    }
}
