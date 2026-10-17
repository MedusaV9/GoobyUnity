using System.Collections.Generic;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Gooby's look back at a level milestone (port of recap_scene.gd / recap_director.gd, as a storybook instead of the
    /// 3D vignettes): "Level 10!" pops over Gooby on his sun disc, then every line of <see cref="Recap.LinesFor"/> comes in
    /// as a big card — the station it belongs to (home, kitchen, garden, bath, arcade, city …), the number counting up with
    /// soft ticks, a star when it lands, confetti every few cards — over the recap music. The end card shows all lines
    /// at once; "Weiter" (or skipping) finishes the milestone: history, fresh baseline, recapsSeen.
    /// </summary>
    public sealed class RecapScreen : UiScreen
    {
        private const float IntroS = 2.6f;
        private const float LineS = 2.7f;
        private const float CountS = 1.1f;

        private static readonly Dictionary<string, (string station, string icon, string tint)> Stations = new Dictionary<string, (string, string, string)>
        {
            ["days"] = ("home", "sun", "#FFE7A3"), ["games"] = ("arcade", "gamepad", "#DCEBFF"), ["coinsEarned"] = ("shop", "coin", "#FFF1C2"),
            ["tickles"] = ("home", "hand", "#FFE3E3"), ["feeds"] = ("kitchen", "carrot", "#FFE9D2"), ["harvests"] = ("garden", "sprout", "#DDF3E4"),
            ["stickers"] = ("home", "star", "#FFF1C2"), ["quests"] = ("city", "trophy", "#FFE9D2"), ["washes"] = ("bath", "rain", "#DCEBFF"),
            ["sleeps"] = ("night", "moon", "#E4DEFF"), ["trips"] = ("city", "car", "#DCEBFF"), ["distance"] = ("city", "compass", "#E4F6EA"),
            ["photos"] = ("vacation", "camera", "#FFE3EE"), ["deliveries"] = ("city", "gift", "#FFE3E3"), ["cures"] = ("home", "heart", "#FFE3E3"),
            ["cakes"] = ("kitchen", "food_cake", "#FFE9D2"), ["nougat"] = ("kitchen", "food_donut", "#FFE9D2"), ["coinsSpent"] = ("shop", "shop", "#FFE3E3"),
        };

        private readonly GameHost _host;
        private readonly int _level;
        private readonly Juice _juice = new Juice();
        private List<Recap.Line> _lines;
        private VisualElement _stage, _card, _dots, _skip;
        private Label _headline;
        private float _t, _lineT, _tickIn;
        private int _index = -1, _shown;
        private bool _ended, _done;

        public RecapScreen(GameHost host, int level)
        {
            _host = host;
            _level = level;
            Id = "recap";
            Opaque = true;
        }

        public override string SoundScene => "recap";

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-recap");
            _lines = Recap.LinesFor(_host.Store, _host.Store.Clock.NowMs());
            var title = Ux.Text(t.T("recap.title"), "gb-recap-title");
            root.Add(title);
            _headline = Ux.Text(t.T("recap.level_headline", ("level", _level)), "gb-recap-level");
            root.Add(_headline);
            _stage = Ux.Row("gb-recap-stage");
            var gooby = Ux.Col("gb-recap-gooby");
            var disc = Ux.El("gb-recap-disc");
            disc.Add(Ux.Image("gooby_sitting", 220, 220));
            gooby.Add(disc);
            _stage.Add(gooby);
            _card = Ux.Col("gb-recap-card");
            _stage.Add(_card);
            root.Add(_stage);
            _dots = Ux.Row("gb-recap-dots");
            for (var i = 0; i < _lines.Count; i++) _dots.Add(Ux.El("gb-recap-dot"));
            root.Add(_dots);
            _skip = Ux.IconTextBtn("chevron", t.T("recap.ueberspringen"), ShowEnd, "gb-btn-secondary", "gb-btn-sm", "gb-recap-skip");
            _skip.name = "recap-skip";
            _skip.style.display = DisplayStyle.None;
            root.Add(_skip);
            root.Add(_juice.Layer);
            _juice.ScalePop(_headline, 1.35f, 0.5f);
            Sfx.Play("soft_levelup");
            Debug.Log("[recap] level " + _level + " lines=" + _lines.Count);
        }

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            if (_ended) return;
            _t += dt;
            if (_t > 3f) _skip.style.display = DisplayStyle.Flex;
            var reduced = _host.Settings.ReducedMotion;
            if (_index < 0)
            {
                if (_t < (reduced ? 0.8f : IntroS)) return;
                NextLine();
                return;
            }
            _lineT += dt;
            var line = _lines[_index];
            // the number counts up with soft ticks, then lands with a star
            var k = Mathf.Clamp01(_lineT / (reduced ? 0.2f : CountS));
            var shown = (int)Mathf.Round(line.N * (1f - Mathf.Pow(1f - k, 3f)));
            if (shown != _shown)
            {
                _shown = shown;
                if (_card.Q<Label>("recap-number") is Label n) n.text = Ux.FormatNumber(shown);
                _tickIn -= dt;
                if (_tickIn <= 0f && k < 1f) { _tickIn = 0.08f; Sfx.Play("soft_tick", k * 8f, 0.5f); }
            }
            if (k >= 1f && _lineT - dt < (reduced ? 0.2f : CountS))
            {
                Sfx.Play("game_star", Mathf.Min(_index, 8), 0.8f);
                if (_index % 4 == 3) _host.Ui.Confetti(reduced ? 10 : 40);
            }
            if (_lineT >= (reduced ? 1.0f : LineS)) NextLine();
        }

        private void NextLine()
        {
            _index++;
            _lineT = 0f;
            _shown = -1;
            if (_index >= _lines.Count) { ShowEnd(); return; }
            var t = _host.Strings;
            var line = _lines[_index];
            var st = Station(line.Id);
            _card.Clear();
            var chip = Ux.Row("gb-recap-station");
            chip.Add(Ux.Icon(st.icon));
            chip.Add(Ux.Text(t.T("recap.station." + st.station)));
            _card.Add(chip);
            var tile = Ux.El("gb-recap-tile");
            tile.style.backgroundColor = Gooby.Unity.World.ArtLibrary.Hex(st.tint);
            tile.Add(Ux.Icon(st.icon, "gb-recap-tile-icon"));
            _card.Add(tile);
            var number = Ux.Text("0", "gb-recap-number");
            number.name = "recap-number";
            _card.Add(number);
            var text = Ux.Text(LineText(line), "gb-recap-line");
            text.style.whiteSpace = WhiteSpace.Normal;
            _card.Add(text);
            _juice.ScalePop(_card, 1.12f, 0.32f);
            var i = 0;
            foreach (var d in _dots.Children()) d.EnableInClassList("gb-recap-dot--on", i++ <= _index);
            Sfx.PlayUi("soft_toast");
        }

        private static (string station, string icon, string tint) Station(string id) =>
            Stations.TryGetValue(id, out var s) ? s : (station: "home", icon: "star", tint: "#FFF1C2");

        private string LineText(Recap.Line line)
        {
            var t = _host.Strings;
            if (line.Id == "days" && line.N == 1) return t.T("recap.stat.days_one");
            return t.T("recap.stat." + line.Id, ("n", Ux.FormatNumber(line.N)));
        }

        private void ShowEnd()
        {
            if (_ended) return;
            _ended = true;
            var t = _host.Strings;
            _skip.style.display = DisplayStyle.None;
            _dots.style.display = DisplayStyle.None;
            _card.Clear();
            _card.AddToClassList("gb-recap-card--end");
            var grid = Ux.El("gb-recap-grid");
            foreach (var line in _lines)
            {
                var st = Station(line.Id);
                var cell = Ux.Row("gb-recap-chip");
                cell.style.backgroundColor = Gooby.Unity.World.ArtLibrary.Hex(st.tint);
                cell.Add(Ux.Icon(st.icon));
                var label = Ux.Text(LineText(line));
                label.style.whiteSpace = WhiteSpace.Normal;
                cell.Add(label);
                grid.Add(cell);
            }
            _card.Add(grid);
            var weiter = Ux.IconTextBtn("check", t.T("recap.weiter"), Finish, "gb-btn-primary");
            weiter.name = "recap-weiter";
            _card.Add(weiter);
            _juice.ScalePop(_card, 1.08f, 0.3f);
            _host.Ui.Confetti(_host.Settings.ReducedMotion ? 16 : 80);
            Sfx.Play("soft_win");
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            Recap.Complete(_host.Store, _level, _host.Store.Clock.NowMs());
            Sfx.PlayUi("soft_confirm");
            _host.Ui.Pop();
        }
    }
}
