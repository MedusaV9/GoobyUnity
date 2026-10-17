using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// The bedtime story hour (port of story_books.gd + the gap logic of story_time.gd, W13B GESCHICHTEN): books from the
    /// pack content/books (title, price, entertainment, 2–3 gap-fill stories with three gaps and six word chips each).
    /// The start book belongs to everyone, the rest are bought at REHWEI (inventory.items). Reading wears a book out: the
    /// "story" slice counts reads per book, and a worn book needs more words before Gooby falls asleep
    /// (<see cref="NeededWords"/>). Wrong words are allowed — a funnier story, Gooby giggles in his sleep. Pure statics.
    /// </summary>
    public static class StoryBooks
    {
        public const string BooksPath = "content/books/data/books";
        public const string SliceId = "story";
        public const int WordsMin = 3, WordsMax = 9, ReadsPerExtraWord = 2, WornAfterReads = 5;
        public const string GapPlaceholder = "____";

        public static List<JObject> Books()
        {
            var list = new List<JObject>();
            foreach (var t in ContentSource.LoadItems(BooksPath)) if (t is JObject o) list.Add(o);
            return list;
        }

        public static JObject BookById(IEnumerable<JObject> books, string id)
        {
            foreach (var b in books) if (J.Str(b["id"]) == id) return b;
            return null;
        }

        public static List<JObject> StoriesOf(JObject book)
        {
            var list = new List<JObject>();
            if (book?["geschichten"] is JArray arr) foreach (var t in arr) if (t is JObject o) list.Add(o);
            return list;
        }

        /// <summary>Books the player owns: start books (start or price 0) always, the rest once bought (inventory.items).</summary>
        public static List<string> OwnedBookIds(IEnumerable<JObject> books, JObject state)
        {
            var items = J.Path(state, "inventory", "items") as JObject;
            var owned = new List<string>();
            foreach (var b in books)
            {
                var id = J.Str(b["id"]);
                if (id == "") continue;
                var start = J.IsTrue(b["start"]) || J.Int(b["preis"]) <= 0;
                if (start || J.Int(items?[id]) > 0) owned.Add(id);
            }
            return owned;
        }

        /// <summary>Words until Gooby sleeps: exciting books forgive more reads, worn ones need more pages (3…9).</summary>
        public static int NeededWords(int entertainment, int reads)
        {
            var baseWords = 6 - Math.Max(1, entertainment);
            var wear = Math.Max(0, reads) / ReadsPerExtraWord;
            return Math.Max(WordsMin, Math.Min(WordsMax, baseWords + wear));
        }

        public static bool IsWorn(int reads) => reads >= WornAfterReads;

        public static int ReadsOf(JObject state, string bookId) => Math.Max(0, J.Int(J.Path(state, SliceId, "reads", bookId)));

        /// <summary>A reading session wears the book (reads +1).</summary>
        public static void BumpRead(JObject state, string bookId)
        {
            if (string.IsNullOrEmpty(bookId)) return;
            var reads = J.EnsureDict(J.EnsureDict(state, SliceId), "reads");
            reads[bookId] = Math.Max(0, J.Int(reads[bookId])) + 1;
        }

        // ── the gaps (story_time.gd, pure) ──

        public static string[] EmptyFills(JObject story) => new string[(story?["luecken"] as JArray)?.Count ?? 0];

        /// <summary>Puts a word into the first free gap; returns its index (−1 when all are full).</summary>
        public static int PlaceWord(string[] fills, string wordId)
        {
            for (var i = 0; i < fills.Length; i++)
                if (fills[i] == null) { fills[i] = wordId; return i; }
            return -1;
        }

        public static bool IsComplete(string[] fills)
        {
            if (fills.Length == 0) return false;
            foreach (var f in fills) if (f == null) return false;
            return true;
        }

        public static bool IsCorrect(JObject story, int gapIndex, string wordId) =>
            story?["luecken"] is JArray l && gapIndex >= 0 && gapIndex < l.Count && J.Str(l[gapIndex]) == wordId;

        /// <summary>How many "wrong" (= funnier) words a filling holds.</summary>
        public static int WrongCount(JObject story, string[] fills)
        {
            var wrong = 0;
            for (var i = 0; i < fills.Length; i++) if (fills[i] != null && !IsCorrect(story, i, fills[i])) wrong++;
            return wrong;
        }

        /// <summary>The story's word chips (id, text) in pack order.</summary>
        public static List<(string id, string text)> Words(JObject story)
        {
            var list = new List<(string, string)>();
            if (story?["woerter_de"] is JArray arr)
                foreach (var t in arr) if (t is JObject w) list.Add((J.Str(w["id"]), J.Str(w["text"])));
            return list;
        }

        /// <summary>The sentences with the words put in ({0}/{1}/{2} → the word's text or ____).</summary>
        public static List<string> RenderedSentences(JObject story, string[] fills)
        {
            var words = new Dictionary<string, string>();
            foreach (var (id, text) in Words(story)) words[id] = text;
            var result = new List<string>();
            if (!(story?["saetze_de"] is JArray saetze)) return result;
            foreach (var s in saetze)
            {
                var text = J.Str(s);
                for (var i = 0; i < fills.Length; i++)
                    text = text.Replace("{" + i + "}", fills[i] == null ? GapPlaceholder : words.TryGetValue(fills[i], out var w) ? w : fills[i]);
                result.Add(text);
            }
            return result;
        }
    }
}
