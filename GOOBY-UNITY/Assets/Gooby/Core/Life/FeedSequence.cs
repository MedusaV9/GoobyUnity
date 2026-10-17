using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// The feeding sequence (port of fuetter_sequenz.gd, W14/FRIDGE): after choosing a food nothing is booked at once —
    /// the food floats over to Gooby, he takes three bites, swallows and shows a feeling; only the last event ("buchen")
    /// releases the booking (FoodCatalog.ApplyFeed stays with the caller). No timer of its own: the caller pumps
    /// <see cref="Tick"/> with the clock and gets every due event exactly once, in order. Starting while one runs is the
    /// double-tap guard. Reduced motion is the short version: a shorter flight and a single bite.
    /// </summary>
    public sealed class FeedSequence
    {
        public const string Bereit = "bereit", Schweben = "schweben", Mampf = "mampf", Schluck = "schluck", EmotionPhase = "emotion", Fertig = "fertig";
        public const int SchwebenMs = 700, BissMs = 350, SchluckMs = 300, EmotionMs = 450, BisseVoll = 3;
        public const int SchwebenReduziertMs = 400, BisseReduziert = 1;
        public const string Verliebt = "verliebt", Zucker = "zucker", Froh = "froh";

        public sealed class Event
        {
            public string Typ = "", Art = "";
            public int At, Index, Von, DauerMs;
        }

        private readonly List<Event> _events = new List<Event>();
        private bool _running;
        private long _startMs;
        private int _next;

        public string FoodId { get; private set; } = "";
        public bool Active => _running;

        /// <summary>The existing gates before the sequence (apply_feed still checks them itself): "" free, "satt" or "leer".</summary>
        public static string Refusal(JObject state, string foodId)
        {
            if (FoodCatalog.TooFull(state)) return "satt";
            return J.Int(J.Path(state, "inventory", "food", foodId ?? "")) <= 0 ? "leer" : "";
        }

        /// <summary>The feeling at the end: favourite food → in love, junk → the sugar shake, otherwise simply happy.</summary>
        public static string EmotionFor(string foodId) =>
            FoodCatalog.IsFavorite(foodId) ? Verliebt : FoodCatalog.IsJunk(foodId) ? Zucker : Froh;

        /// <summary>Starts the sequence; false while one runs (the double tap).</summary>
        public bool Start(string foodId, long nowMs, bool reduced = false)
        {
            if (_running) return false;
            FoodId = foodId ?? "";
            _running = true;
            _startMs = nowMs;
            _next = 0;
            _events.Clear();
            var bites = reduced ? BisseReduziert : BisseVoll;
            var fly = reduced ? SchwebenReduziertMs : SchwebenMs;
            _events.Add(new Event { At = 0, Typ = "schwebt", DauerMs = fly });
            var t = fly;
            for (var i = 0; i < bites; i++)
            {
                _events.Add(new Event { At = t, Typ = "biss", Index = i + 1, Von = bites });
                t += BissMs;
            }
            _events.Add(new Event { At = t, Typ = "schluck" });
            t += SchluckMs;
            _events.Add(new Event { At = t, Typ = "emotion", Art = EmotionFor(FoodId) });
            t += EmotionMs;
            _events.Add(new Event { At = t, Typ = "buchen" });
            return true;
        }

        /// <summary>Total length in ms (the moment of "buchen").</summary>
        public int DurationMs => _events.Count == 0 ? 0 : _events[_events.Count - 1].At;

        public int BiteCount
        {
            get
            {
                var n = 0;
                foreach (var e in _events) if (e.Typ == "biss") n++;
                return n;
            }
        }

        /// <summary>The events due since the last tick, each exactly once and in order; "buchen" ends the sequence.</summary>
        public List<Event> Tick(long nowMs)
        {
            var due = new List<Event>();
            if (!_running) return due;
            var offset = nowMs - _startMs;
            while (_next < _events.Count && _events[_next].At <= offset) due.Add(_events[_next++]);
            if (_next >= _events.Count) _running = false;
            return due;
        }

        /// <summary>The phase the clock says (informative, screenshots and tests).</summary>
        public string Phase(long nowMs)
        {
            if (_events.Count == 0) return Bereit;
            var offset = nowMs - _startMs;
            if (offset >= DurationMs && !_running) return Fertig;
            var current = Schweben;
            foreach (var e in _events)
            {
                if (e.At > offset) break;
                switch (e.Typ)
                {
                    case "biss": current = Mampf; break;
                    case "schluck": current = Schluck; break;
                    case "emotion": current = EmotionPhase; break;
                    case "buchen": current = Fertig; break;
                }
            }
            return current;
        }
    }

    /// <summary>
    /// Gooby's line after a meal (fuetter_sprueche.gd): eight per shelf category (fuettern.sprueche.&lt;kategorie&gt;),
    /// taken in turn rather than at random, so nothing repeats before all eight have come.
    /// </summary>
    public static class FeedLines
    {
        private static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();

        /// <summary>Index of the next line of this food's category in a list of <paramref name="count"/> (−1 without lines).</summary>
        public static int Next(string foodId, int count)
        {
            if (count <= 0) return -1;
            var kategorie = FoodCatalog.Kategorie(foodId);
            Counters.TryGetValue(kategorie, out var i);
            i %= count;
            Counters[kategorie] = i + 1;
            return i;
        }

        public static void ResetForTests() => Counters.Clear();
    }
}
