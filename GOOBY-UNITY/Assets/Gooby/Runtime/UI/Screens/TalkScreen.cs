using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Reden" (board 08, bottom right): a little chat with Gooby. He opens with a line for his mood; the
    /// answer chips ask how he is (stats in his words), what is on today (the quest board), for a story
    /// (his idle activities from the Godot soul lines) or say goodbye. Every line also lands in the HUD bubble.
    /// </summary>
    public sealed class TalkScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly Action<string> _say;
        private Label _line;
        private int _stories;

        private static readonly string[] StoryKeys =
        {
            "home.suche.akt.idle_aufraeumen", "home.suche.akt.idle_fernsehen", "home.suche.akt.idle_doesen",
            "home.suche.akt.idle_fenster", "home.suche.akt.idle_tanzen", "home.suche.akt.idle_lieblingsplatz",
        };

        public TalkScreen(GameHost host, Action<string> say)
        {
            _host = host;
            _say = say;
            Id = "talk";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 220; sheet.style.right = 220;
            sheet.Add(CounterPanel.Head("chat", new Color(1f, 0.85f, 0.85f), t.T("nl.reden.titel"), t.T("nl.reden.untertitel"), () => _host.Ui.Pop()).Row);
            var body = Ux.Col("gb-win-body");

            // Gooby's speech (board 06/08): Gooby on a sun disc, white bubble with a tail and his name tag
            var talk = Ux.Row("gb-talk");
            var art = Ux.El("gb-talk-art");
            art.Add(Ux.Image("gooby_sitting", 116, 116));
            talk.Add(art);
            var bubble = Ux.El("gb-talk-bubble");
            bubble.Add(Ux.El("gb-talk-tail"));
            _line = Ux.Text("", "gb-talk-line");
            bubble.Add(_line);
            bubble.Add(Ux.Text(t.T("nl.marke.name").ToUpperInvariant(), "gb-talk-name"));
            talk.Add(bubble);
            body.Add(talk);

            // answer pills: the first one pink like the board-10 dialog card, the others white
            var chips = Ux.Row("gb-talk-chips");
            var first = true;
            foreach (var (key, action) in new (string, Action)[]
                     {
                         ("nl.reden.frage_wie", AskHow), ("nl.reden.frage_plan", AskPlan),
                         ("nl.reden.frage_erzaehl", AskStory), ("nl.reden.tschuess", Bye),
                     })
            {
                var chip = Ux.Btn(t.T(key), action, "gb-chip", "gb-talk-chip");
                if (first) chip.AddToClassList("gb-talk-chip--first");
                first = false;
                chips.Add(chip);
            }
            body.Add(chips);
            sheet.Add(body);
            root.Add(sheet);

            Say(OpeningLine());
        }

        private void Say(string text)
        {
            _line.text = text;
            _host.Audio?.Say(text);
            _say?.Invoke(text);
        }

        private string OpeningLine()
        {
            var state = _host.Store.State;
            var flat = Sleep.FlatOf(state);
            if (Sleep.IsSleeping(flat)) return _host.Strings.T("home.suche.schlaeft");
            var band = Stats.MoodBand(Sleep.CurrentMood(GoobyTicker.FlatView(state), _host.Store.Clock.NowMs()));
            return _host.Strings.T(band == "ecstatic" || band == "happy" ? "home.suche.hier" : band == "neutral" ? "home.suche.unterwegs" : "home.suche.akt.absicht_langeweile");
        }

        /// <summary>The lowest stat decides what Gooby complains about; all fine → happy line.</summary>
        private void AskHow()
        {
            var t = _host.Strings;
            var stats = J.Dict(J.Dict(_host.Store.State["gooby"])["stats"]);
            var lowest = Stats.Keys.OrderBy(k => J.Num(stats[k])).First();
            var value = J.Num(stats[lowest]);
            string key;
            if (value >= 60) key = "nl.reden.gut";
            else switch (lowest)
            {
                case "hunger": key = "home.suche.akt.absicht_hunger"; break;
                case "energy": key = "home.suche.akt.absicht_muede"; break;
                case "hygiene": key = "home.suche.akt.absicht_dreckig"; break;
                default: key = "home.suche.akt.absicht_langeweile"; break;
            }
            Say(t.T(key) + " " + t.T("nl.reden.werte", ("hunger", Mathf.RoundToInt((float)J.Num(stats["hunger"]))), ("energie", Mathf.RoundToInt((float)J.Num(stats["energy"]))),
                ("sauber", Mathf.RoundToInt((float)J.Num(stats["hygiene"]))), ("spass", Mathf.RoundToInt((float)J.Num(stats["fun"])))));
        }

        /// <summary>Today's board in Gooby's words: open quests listed, or a cheer when everything is done.</summary>
        private void AskPlan()
        {
            var t = _host.Strings;
            var open = new List<string>();
            foreach (var row in _host.Quests?.Board() ?? new List<Gooby.Core.Meta.DailyQuestService.Row>())
            {
                if (row.Claimed) continue;
                var title = t.HasKey(row.Def.TitleKey) ? t.T(row.Def.TitleKey) : row.Def.Id;
                open.Add(row.Target > 1 ? title + " (" + row.Progress + "/" + row.Target + ")" : title);
            }
            Say(open.Count == 0 ? t.T("nl.reden.plan_fertig") : t.T("nl.reden.plan", ("liste", string.Join(", ", open))));
        }

        private void AskStory()
        {
            var key = StoryKeys[_stories % StoryKeys.Length];
            _stories++;
            Say(_host.Strings.T(key));
        }

        private void Bye()
        {
            Say(_host.Strings.T("nl.reden.bis_gleich"));
            _host.World?.Bounce();
            _host.Ui.Pop();
        }
    }
}
