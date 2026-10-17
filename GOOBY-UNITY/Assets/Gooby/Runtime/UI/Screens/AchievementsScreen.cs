using System.Linq;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The achievements (port of achievements_screen.gd): category chips (Alle, Pflege, Spielen, Garten, Sammeln, Reisen,
    /// Fortschritt), then one row per achievement — a medal (gold when earned), name and description, a progress bar with
    /// current/target while it is open, and its coin reward (paid by the AchievementsService the moment it unlocks).
    /// Earned ones sort to the end of their category, nearly-there ones to the top.
    /// </summary>
    public sealed class AchievementsScreen : UiScreen
    {
        private readonly GameHost _host;
        private string _cat = "alle";
        private VisualElement _list, _chips;
        private Label _title;

        public AchievementsScreen(GameHost host)
        {
            _host = host;
            Id = "achievements";
        }

        private JObject State => _host.Store.State;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var sheetStyle = Resources.Load<StyleSheet>("gooby-ui/Theme/gooby-album");
            if (sheetStyle != null) root.styleSheets.Add(sheetStyle);
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 140; sheet.style.right = 140;
            sheet.style.maxHeight = Length.Percent(86);
            var head = CounterPanel.Head("trophy", new Color(1f, 0.9f, 0.7f), t.T("achievements.titel"), "", () => _host.Ui.Pop());
            _title = head.Title;
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");
            _chips = Ux.Row("gb-win-tabs");
            foreach (var cat in new[] { "alle" }.Concat(AchievementsCatalog.Categories))
            {
                var id = cat;
                var chip = Ux.Btn(t.T("achievements.kategorie." + cat), () => { _cat = id; Sfx.PlayUi("soft_chip"); Refresh(); }, "gb-chip");
                chip.name = "ach-cat-" + cat;
                _chips.Add(chip);
            }
            body.Add(_chips);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _list = Ux.Col("gb-ach-list");
            scroll.Add(_list);
            body.Add(scroll);
            sheet.Add(body);
            root.Add(sheet);
            Refresh();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            var all = AchievementsCatalog.All();
            _title.text = t.T("achievements.titel") + "  ·  " + t.T("achievements.zaehler", ("n", AchievementsEngine.UnlockedCount(State, all)), ("total", all.Count));
            foreach (var cat in new[] { "alle" }.Concat(AchievementsCatalog.Categories))
                _chips.Q("ach-cat-" + cat)?.EnableInClassList("gb-chip--active", cat == _cat);
            _list.Clear();
            var rows = all.Where(d => _cat == "alle" || d.Cat == _cat)
                .Select(d => (def: d, done: AchievementsEngine.IsUnlocked(State, d.Id), progress: AchievementsEngine.ProgressOf(d, State)))
                .OrderBy(r => r.done)
                .ThenByDescending(r => r.done ? 0.0 : r.progress.target > 0 ? (double)r.progress.current / r.progress.target : 0.0)
                .ThenBy(r => r.def.Order);
            foreach (var (def, done, progress) in rows) _list.Add(Row(def, done, progress.current, progress.target));
        }

        private static readonly System.Collections.Generic.Dictionary<string, (string icon, string pastel)> CategoryLook =
            new System.Collections.Generic.Dictionary<string, (string, string)>
            {
                ["pflege"] = ("heart", "#FFE1EA"), ["spiel"] = ("gamepad", "#E3EEFF"), ["garten"] = ("sprout", "#E2F5E8"),
                ["sammeln"] = ("star", "#FFF1C9"), ["reisen"] = ("plane", "#DDF4F1"), ["fortschritt"] = ("sparkle", "#EFE6FA"),
            };

        private VisualElement Row(AchievementDef def, bool done, int current, int target)
        {
            var t = _host.Strings;
            var row = Ux.Row("gb-ach-row", done ? "gb-ach-row--done" : "gb-ach-row--locked");
            row.name = "ach-" + def.Id;
            var medal = Ux.El("gb-ach-medal");
            (string icon, string pastel) look = CategoryLook.TryGetValue(def.Cat ?? "", out var l) ? l : ("star", "#EFE6DA");
            var icon = Ux.Icon(done ? "trophy" : look.icon);
            if (!done)
            {
                // still to earn: the category's picture, a little faded, on its pastel — the list reads at a glance
                medal.style.backgroundColor = Gooby.Unity.World.ArtLibrary.Hex(look.pastel);
                icon.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, 0.72f);
            }
            medal.Add(icon);
            row.Add(medal);
            var texts = Ux.Col("gb-ach-texts");
            var key = "achievements.defs." + def.Id;
            texts.Add(Ux.Text(t.HasKey(key + ".name") ? t.T(key + ".name") : def.Id, "gb-ach-name"));
            if (t.HasKey(key + ".desc")) texts.Add(Ux.Text(t.T(key + ".desc"), "gb-ach-desc"));
            if (!done && target > 1)
            {
                var prog = Ux.Row("gb-ach-progress");
                var bar = Ux.Bar(Mathf.Clamp01((float)current / target), out _, "gb-bar-fill--sonne");
                bar.AddToClassList("gb-ach-bar");
                prog.Add(bar);
                prog.Add(Ux.Text(t.T("achievements.fortschritt", ("current", Mathf.Min(current, target)), ("target", target)), "gb-ach-count"));
                texts.Add(prog);
            }
            row.Add(texts);
            row.Add(Ux.Text(done ? t.T("achievements.freigeschaltet") : t.T("achievements.belohnung", ("coins", def.Coins)), "gb-ach-coins"));
            return row;
        }
    }
}
