using Gooby.Core.Audio;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The home radio (port of radio_sheet.gd): the stations of the music registry — the ones the level unlocks to tap,
    /// the others with their unlock level — the song playing now, "next song" and "radio off". The radio keeps playing
    /// while Gooby is at home (and on the street); a place or a minigame brings its own music back.
    /// </summary>
    public sealed class RadioScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _list;
        private Label _now;

        public RadioScreen(GameHost host)
        {
            _host = host;
            Id = "radio";
        }

        private int Level => J.Int(J.Path(_host.Store.State, "progression", "level"), 1);

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 260; sheet.style.right = 260;
            sheet.style.maxHeight = Length.Percent(86);
            var head = CounterPanel.Head("speaker", new Color(0.86f, 0.92f, 1f), t.T("nl.radio.titel"), t.T("nl.radio.untertitel"), () => _host.Ui.Pop());
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");
            var nowCard = Ux.Row("gb-card");
            nowCard.style.alignItems = Align.Center;
            nowCard.style.marginBottom = 12;
            nowCard.Add(Ux.IconTile("speaker", new Color(0.86f, 0.92f, 1f)));
            _now = Ux.Text("", "gb-lg", "gb-bold");
            _now.style.marginLeft = 12; _now.style.flexGrow = 1; _now.style.flexShrink = 1;
            _now.style.whiteSpace = WhiteSpace.Normal;
            nowCard.Add(_now);
            var skip = Ux.IconTextBtn("play", t.T("nl.radio.weiter"), () => { _host.Audio?.SkipRadio(); Sfx.PlayUi("soft_tap"); Refresh(); }, "gb-btn-secondary");
            skip.name = "radio-skip";
            skip.style.marginRight = 8;
            nowCard.Add(skip);
            var off = Ux.IconTextBtn("close", t.T("nl.radio.aus"), () => { _host.Audio?.RadioOff(); Sfx.PlayUi("soft_close"); Refresh(); }, "gb-btn-secondary");
            off.name = "radio-off";
            nowCard.Add(off);
            body.Add(nowCard);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _list = Ux.Col();
            scroll.Add(_list);
            body.Add(scroll);
            body.Add(Ux.Text(t.T("nl.radio.hinweis"), "gb-sm", "gb-mild"));
            sheet.Add(body);
            root.Add(sheet);
            Refresh();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            var playing = _host.Audio?.RadioStation;
            var title = _host.Audio?.NowPlayingTitle;
            _now.text = playing == null
                ? t.T("nl.radio.leise")
                : t.T("nl.radio.laeuft", ("titel", title ?? "…"), ("sender", StationName(playing)));
            _list.Clear();
            foreach (var (id, nameKey, unlock) in MusicRegistry.Stations())
            {
                var station = id;
                var open = unlock <= Mathf.Max(1, Level);
                var row = new Button(() =>
                {
                    if (!open) { Sfx.PlayUi("soft_error"); return; }
                    if (_host.Audio != null && _host.Audio.PlayRadio(station, Level)) Sfx.PlayUi("soft_confirm");
                    Refresh();
                }) { text = "", name = "radio-" + id };
                row.ClearClassList();
                row.AddToClassList("gb-btn");
                row.AddToClassList(station == playing ? "gb-btn-primary" : "gb-btn-secondary");
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.height = 56;
                row.style.marginBottom = 8;
                row.style.paddingLeft = 18; row.style.paddingRight = 18;
                row.style.borderTopLeftRadius = 18; row.style.borderTopRightRadius = 18;
                row.style.borderBottomLeftRadius = 18; row.style.borderBottomRightRadius = 18;
                row.Add(Ux.Text(StationName(id), "gb-lg", "gb-bold"));
                row.Add(Ux.Text(open ? (station == playing ? t.T("nl.radio.an") : "") : t.T("nl.radio.ab_level", ("level", unlock)), "gb-md"));
                if (!open) row.style.opacity = 0.55f;
                _list.Add(row);
            }
        }

        private string StationName(string id)
        {
            foreach (var (sid, nameKey, _) in MusicRegistry.Stations())
                if (sid == id && _host.Strings.HasKey(nameKey)) return _host.Strings.T(nameKey);
            return id;
        }
    }
}
