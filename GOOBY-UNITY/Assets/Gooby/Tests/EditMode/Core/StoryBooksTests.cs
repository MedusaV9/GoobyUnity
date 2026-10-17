using System.Collections.Generic;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The bedtime story hour (port of tests/unit/test_w13b_geschichten.gd): the book catalog is complete and consistent
    /// (one free start book, unique ids, three gaps per story with their tokens in the text, the right words in the
    /// pool), the gap logic fills every story correctly, the wear formula rises with reads and falls with excitement,
    /// reads land in the "story" slice, and only the start book is owned before buying.
    /// </summary>
    public class StoryBooksTests
    {
        [SetUp]
        public void Content() => Fixtures.InstallContentSource();

        [Test]
        public void TheCatalogIsCompleteAndConsistent()
        {
            var books = StoryBooks.Books();
            Assert.That(books.Count, Is.GreaterThanOrEqualTo(6));
            var ids = new HashSet<string>();
            var storyIds = new HashSet<string>();
            var starts = 0;
            var stories = 0;
            foreach (var b in books)
            {
                var id = (string)b["id"];
                Assert.That(ids.Add(id), Is.True, "unique book id " + id);
                Assert.That((string)b["titel_de"], Is.Not.Empty, id);
                Assert.That((int)b["entertainment"], Is.GreaterThanOrEqualTo(1), id);
                if (b["start"] != null && (bool)b["start"]) { starts++; Assert.That((int)b["preis"], Is.EqualTo(0), "the start book is free"); }
                foreach (var s in StoryBooks.StoriesOf(b))
                {
                    stories++;
                    var sid = (string)s["id"];
                    Assert.That(storyIds.Add(sid), Is.True, "unique story id " + sid);
                    var gaps = (JArray)s["luecken"];
                    Assert.That(gaps.Count, Is.EqualTo(3), sid);
                    var words = new HashSet<string>();
                    foreach (var (wid, text) in StoryBooks.Words(s)) { Assert.That(words.Add(wid), Is.True, sid + " word " + wid); Assert.That(text, Is.Not.Empty); }
                    var all = string.Join(" ", ((JArray)s["saetze_de"]).ToObject<string[]>());
                    for (var i = 0; i < gaps.Count; i++)
                    {
                        Assert.That(words.Contains((string)gaps[i]), Is.True, sid + ": gap word in the pool");
                        Assert.That(all.Contains("{" + i + "}"), Is.True, sid + ": token {" + i + "}");
                    }
                }
            }
            Assert.That(starts, Is.EqualTo(1), "exactly one start book");
            Assert.That(stories, Is.GreaterThanOrEqualTo(12));
        }

        [Test]
        public void TheGapLogicFillsEveryStoryCorrectly()
        {
            foreach (var b in StoryBooks.Books())
                foreach (var s in StoryBooks.StoriesOf(b))
                {
                    var fills = StoryBooks.EmptyFills(s);
                    Assert.That(fills.Length, Is.EqualTo(3));
                    foreach (var gap in (JArray)s["luecken"]) Assert.That(StoryBooks.PlaceWord(fills, (string)gap), Is.GreaterThanOrEqualTo(0));
                    Assert.That(StoryBooks.IsComplete(fills), Is.True);
                    Assert.That(StoryBooks.WrongCount(s, fills), Is.EqualTo(0), (string)s["id"]);
                    Assert.That(StoryBooks.PlaceWord(fills, "x"), Is.EqualTo(-1), "full");
                    foreach (var sentence in StoryBooks.RenderedSentences(s, fills)) Assert.That(sentence.Contains(StoryBooks.GapPlaceholder), Is.False);
                }
        }

        [Test]
        public void AWrongWordMakesAFunnierStory()
        {
            var s = StoryBooks.StoriesOf(StoryBooks.Books()[0])[0];
            var fills = StoryBooks.EmptyFills(s);
            var gaps = (JArray)s["luecken"];
            var wrong = StoryBooks.Words(s).Find(w => w.id != (string)gaps[0]).id;
            StoryBooks.PlaceWord(fills, wrong);
            Assert.That(StoryBooks.IsComplete(fills), Is.False);
            Assert.That(StoryBooks.RenderedSentences(s, fills)[1].Contains(StoryBooks.GapPlaceholder), Is.True, "the rest still has gaps");
            StoryBooks.PlaceWord(fills, (string)gaps[1]);
            StoryBooks.PlaceWord(fills, (string)gaps[2]);
            Assert.That(StoryBooks.WrongCount(s, fills), Is.EqualTo(1));
        }

        [Test]
        public void TheWearFormula()
        {
            Assert.That(StoryBooks.NeededWords(3, 0), Is.EqualTo(3), "fresh = 3 words");
            Assert.That(StoryBooks.NeededWords(6, 0), Is.EqualTo(3), "floor at 3");
            for (var ent = 1; ent <= 6; ent++)
            {
                var prev = 0;
                for (var reads = 0; reads <= 20; reads++)
                {
                    var n = StoryBooks.NeededWords(ent, reads);
                    Assert.That(n, Is.GreaterThanOrEqualTo(prev), "monotone in reads");
                    Assert.That(n, Is.InRange(StoryBooks.WordsMin, StoryBooks.WordsMax));
                    prev = n;
                }
            }
            Assert.That(StoryBooks.NeededWords(1, 100), Is.EqualTo(StoryBooks.WordsMax), "cap");
            Assert.That(StoryBooks.IsWorn(StoryBooks.WornAfterReads - 1), Is.False);
            Assert.That(StoryBooks.IsWorn(StoryBooks.WornAfterReads), Is.True);
        }

        [Test]
        public void ReadsLandInTheStorySliceAndOnlyTheStartBookIsOwned()
        {
            var books = StoryBooks.Books();
            var state = new JObject();
            var start = books.Find(b => b["start"] != null && (bool)b["start"]);
            var bought = books.Find(b => (int)b["preis"] > 0);
            Assert.That(StoryBooks.OwnedBookIds(books, state), Is.EqualTo(new List<string> { (string)start["id"] }));
            for (var i = 0; i < 5; i++) StoryBooks.BumpRead(state, (string)start["id"]);
            Assert.That(StoryBooks.ReadsOf(state, (string)start["id"]), Is.EqualTo(5));
            state["inventory"] = new JObject { ["items"] = new JObject { [(string)bought["id"]] = 1 } };
            Assert.That(StoryBooks.OwnedBookIds(books, state), Does.Contain((string)bought["id"]));
        }
    }
}
