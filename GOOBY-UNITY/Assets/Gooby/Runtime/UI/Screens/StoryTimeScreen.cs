using System;
using System.Collections.Generic;
using System.Text;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The bedtime story hour (port of story_time.gd + story_books.gd, W13B GESCHICHTEN): Gooby's bookshelf (owned books
    /// with how worn they are, the others with their REHWEI price), then a page of the chosen book — three sentences with
    /// gaps and six word chips; a tap puts the word into the next gap (wrong words are allowed and make it funnier). A
    /// row of moons fills toward the words this book needs tonight (worn books need more, see
    /// <see cref="StoryBooks.NeededWords"/>); a full page turns to the next story. Once the moons are full the book is
    /// worn a little more and Gooby falls asleep — giggling if a word was silly.
    /// </summary>
    public sealed class StoryTimeScreen : UiScreen
    {
        private static readonly Color[] Covers =
        {
            new Color(1f, 0.83f, 0.6f), new Color(0.72f, 0.85f, 1f), new Color(0.8f, 0.93f, 0.72f),
            new Color(1f, 0.78f, 0.86f), new Color(0.88f, 0.8f, 1f), new Color(1f, 0.93f, 0.62f),
        };

        private readonly GameHost _host;
        private readonly Action<string> _say;
        private readonly Action _fallAsleep;
        private VisualElement _sheet, _body;
        private CounterPanel.Header _head;
        private List<JObject> _books;
        private JObject _book, _story;
        private List<JObject> _pages;
        private int _page, _needed, _placed, _wrong;
        private string[] _fills;
        private Label[] _sentences;
        private VisualElement _moons, _chips;
        private bool _closing;

        /// <param name="say">Gooby's speech bubble (the HUD's).</param>
        /// <param name="fallAsleep">Runs once the story put him to sleep (the care sheet starts the night).</param>
        public StoryTimeScreen(GameHost host, Action<string> say, Action fallAsleep)
        {
            _host = host;
            _say = say;
            _fallAsleep = fallAsleep;
            Id = "geschichte";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);
            _sheet = Ux.El("gb-sheet", "gb-win-sheet", "gb-story-sheet");
            _sheet.name = "story-sheet";
            _sheet.style.left = 160; _sheet.style.right = 160;
            _sheet.style.maxHeight = Length.Percent(92);
            _head = CounterPanel.Head("books", new Color(0.88f, 0.8f, 1f), t.T("sleep.story.bibliothek_titel"), t.T("sleep.story.waehlen"), Close);
            _sheet.Add(_head.Row);
            _body = Ux.Col("gb-win-body");
            _sheet.Add(_body);
            root.Add(_sheet);
            _books = StoryBooks.Books();
            ShowLibrary();
        }

        private void Close()
        {
            if (_closing) return;
            _host.Ui.Pop();
        }

        // ── the bookshelf ──

        private void ShowLibrary()
        {
            var t = _host.Strings;
            _head.Title.text = t.T("sleep.story.bibliothek_titel");
            _head.Sub.text = t.T("sleep.story.waehlen");
            _body.Clear();
            var state = _host.Store.State;
            var owned = StoryBooks.OwnedBookIds(_books, state);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            for (var i = 0; i < _books.Count; i++)
            {
                var book = _books[i];
                var id = J.Str(book["id"]);
                var has = owned.Contains(id);
                var reads = StoryBooks.ReadsOf(state, id);
                var row = Ux.Row("gb-card", "gb-story-book");
                row.name = "story-book-" + id;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 10;
                row.style.paddingLeft = 14; row.style.paddingRight = 14; row.style.paddingTop = 10; row.style.paddingBottom = 10;
                if (!has) row.style.opacity = 0.62f;
                var cover = Ux.IconTile(has ? "books" : "lock_white", Covers[i % Covers.Length]);
                cover.style.marginRight = 14;
                row.Add(cover);
                var col = Ux.Col();
                col.style.flexGrow = 1; col.style.flexShrink = 1;
                col.Add(Ux.Text(J.Str(book["titel_de"]), "gb-lg", "gb-bold"));
                var caption = !has ? t.T("sleep.story.gesperrt", ("preis", J.Int(book["preis"])))
                    : StoryBooks.IsWorn(reads) ? t.T("sleep.story.abgenutzt")
                    : reads > 0 ? t.T("sleep.story.gelesen", ("mal", reads)) : t.T("sleep.story.frisch");
                var cap = Ux.Text(caption, "gb-sm", "gb-mild");
                cap.style.whiteSpace = WhiteSpace.Normal;
                col.Add(cap);
                row.Add(col);
                if (has) row.Add(Ux.Icon("chevron", "gb-story-chevron"));
                var local = book;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!has) { Sfx.PlayUi("soft_error"); _host.Ui.Toast(caption, "shop"); return; }
                    Sfx.PlayUi("soft_open");
                    StartBook(local);
                });
                scroll.Add(row);
            }
            _body.Add(scroll);
            var more = Ux.Text(t.T("sleep.story.nachschub"), "gb-sm", "gb-mild");
            more.style.whiteSpace = WhiteSpace.Normal;
            more.style.marginTop = 6;
            _body.Add(more);
        }

        // ── reading ──

        private void StartBook(JObject book)
        {
            var state = _host.Store.State;
            var id = J.Str(book["id"]);
            var reads = StoryBooks.ReadsOf(state, id);
            _book = book;
            _pages = StoryBooks.StoriesOf(book);
            if (_pages.Count == 0) return;
            for (var i = _pages.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (_pages[i], _pages[j]) = (_pages[j], _pages[i]);
            }
            _page = 0;
            _needed = StoryBooks.NeededWords(J.Int(book["entertainment"], 1), reads);
            _placed = 0;
            _wrong = 0;
            if (StoryBooks.IsWorn(reads)) _say?.Invoke(_host.Strings.T("sleep.story.abgenutzt"));
            OpenPage(_pages[0]);
        }

        private void OpenPage(JObject story)
        {
            var t = _host.Strings;
            _story = story;
            _fills = StoryBooks.EmptyFills(story);
            _head.Title.text = J.Str(story["titel_de"]);
            _head.Sub.text = J.Str(_book["titel_de"]);
            _body.Clear();
            var page = Ux.Col("gb-card", "gb-story-page");
            page.name = "story-page";
            page.style.paddingLeft = 22; page.style.paddingRight = 22; page.style.paddingTop = 16; page.style.paddingBottom = 16;
            page.style.backgroundColor = new Color(1f, 0.98f, 0.93f);
            var count = (story["saetze_de"] as JArray)?.Count ?? 0;
            _sentences = new Label[count];
            for (var i = 0; i < count; i++)
            {
                var line = Ux.Text("", "gb-lg");
                line.enableRichText = true;
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.marginBottom = 8;
                _sentences[i] = line;
                page.Add(line);
            }
            _body.Add(page);
            var hint = Ux.Text(t.T("events.story.hinweis"), "gb-sm", "gb-mild");
            hint.style.marginTop = 10; hint.style.marginBottom = 6;
            _body.Add(hint);
            _chips = Ux.Row("gb-story-chips");
            _chips.name = "story-chips";
            _chips.style.flexWrap = Wrap.Wrap;
            var words = StoryBooks.Words(story);
            for (var i = words.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (words[i], words[j]) = (words[j], words[i]);
            }
            foreach (var (id, text) in words)
            {
                var wordId = id;
                Button chip = null;
                chip = new Button(() => Place(wordId, chip)) { text = text, name = "story-word-" + id };
                chip.ClearClassList();
                chip.AddToClassList("gb-chip");
                chip.AddToClassList("gb-story-word");
                chip.style.marginRight = 10; chip.style.marginBottom = 10;
                _chips.Add(chip);
            }
            _body.Add(_chips);
            _moons = Ux.Row("gb-story-moons");
            _moons.name = "story-moons";
            _moons.style.alignItems = Align.Center;
            _moons.style.marginTop = 4;
            _body.Add(_moons);
            Refresh();
        }

        private void Place(string wordId, Button chip)
        {
            if (_closing || _fills == null) return;
            var gap = StoryBooks.PlaceWord(_fills, wordId);
            if (gap < 0) return;
            chip.SetEnabled(false);
            chip.AddToClassList("gb-story-word--used");
            _placed++;
            var right = StoryBooks.IsCorrect(_story, gap, wordId);
            if (!right) _wrong++;
            Sfx.PlayUi(right ? "soft_chip" : "soft_sticker");
            if (!right && _wrong == 1) _host.Audio?.Say("hihi", "happy");
            Refresh();
            if (_placed >= _needed) { Finish(); return; }
            if (StoryBooks.IsComplete(_fills))
            {
                _say?.Invoke(_host.Strings.T("sleep.story.naechste_seite"));
                _page = (_page + 1) % _pages.Count;
                var next = _pages[_page];
                _sheet.schedule.Execute(() => { if (!_closing) OpenPage(next); }).StartingIn(900);
            }
        }

        /// <summary>The sentences with placed words in teal (silly ones in rose), open gaps as soft lines; the moons fill up.</summary>
        private void Refresh()
        {
            var words = new Dictionary<string, string>();
            foreach (var (id, text) in StoryBooks.Words(_story)) words[id] = text;
            var saetze = _story["saetze_de"] as JArray;
            for (var s = 0; s < _sentences.Length; s++)
            {
                var sb = new StringBuilder(J.Str(saetze?[s]));
                for (var i = 0; i < _fills.Length; i++)
                {
                    var token = "{" + i + "}";
                    var w = _fills[i];
                    var shown = w == null ? "<color=#C9B79C>" + StoryBooks.GapPlaceholder + "</color>"
                        : "<b><color=" + (StoryBooks.IsCorrect(_story, i, w) ? "#3E9C8F" : "#E0657F") + ">" + (words.TryGetValue(w, out var tx) ? tx : w) + "</color></b>";
                    sb.Replace(token, shown);
                }
                _sentences[s].text = sb.ToString();
            }
            _moons.Clear();
            var label = Ux.Text(_host.Strings.T("nl.story.muede"), "gb-sm", "gb-bold");
            label.style.marginRight = 8;
            _moons.Add(label);
            for (var i = 0; i < _needed; i++)
            {
                var moon = Ux.Icon("moon", "gb-story-moon");
                moon.style.width = 22; moon.style.height = 22; moon.style.marginRight = 4;
                moon.style.opacity = i < _placed ? 1f : 0.25f;
                _moons.Add(moon);
            }
        }

        private void Finish()
        {
            _closing = true;
            var bookId = J.Str(_book["id"]);
            _host.Store.Update(s => StoryBooks.BumpRead(s, bookId), GameHost.TicksMs);
            _host.Store.NotifySliceChanged(StoryBooks.SliceId);
            _say?.Invoke(_host.Strings.T(_wrong > 0 ? "events.story.kichern" : "events.story.einschlafen"));
            Sfx.PlayUi("soft_confirm");
            var avatar = _host.World?.Avatar;
            if (avatar != null) avatar.SetEmotion(_wrong > 0 ? "happy" : "sleepy");
            Debug.Log("[story] " + bookId + " read: " + _placed + " words, " + _wrong + " silly");
            _sheet.schedule.Execute(() =>
            {
                if (_host.Ui.Current == this) _host.Ui.Pop();
                _fallAsleep?.Invoke();
            }).StartingIn(1600);
        }
    }
}
