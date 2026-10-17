using System.Linq;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The feeding sequence (port of test_w14_fridge.gd): flight, three bites, swallow, feeling and only then the booking —
    /// 2.5 s, each event once; the double-tap guard; the reduced-motion short version; the refusal gates; the feeling per
    /// food; the eight rotating lines per category in German and English.
    /// </summary>
    public class FeedSequenceTests
    {
        private static JObject State(JObject food, double hunger = 30.0) => new JObject
        {
            ["gooby"] = new JObject { ["stats"] = new JObject { ["hunger"] = hunger, ["fun"] = 50.0, ["energy"] = 50.0, ["hygiene"] = 50.0 } },
            ["inventory"] = new JObject { ["food"] = food },
        };

        [Test]
        public void EventsComeInOrderAndTheBookingOnlyAtTheEnd()
        {
            var seq = new FeedSequence();
            Assert.That(seq.Phase(0), Is.EqualTo(FeedSequence.Bereit));
            Assert.That(seq.Start("bread", 10_000), Is.True);
            Assert.That(seq.DurationMs, Is.EqualTo(2500), "the full sequence is 2.5 s");
            Assert.That(seq.BiteCount, Is.EqualTo(3));
            var types = seq.Tick(10_000 + 2499).Select(e => e.Typ).ToList();
            Assert.That(types, Has.No.Member("buchen"), "no booking before the end");
            Assert.That(seq.Active, Is.True);
            types.AddRange(seq.Tick(10_000 + 2500).Select(e => e.Typ));
            Assert.That(types, Is.EqualTo(new[] { "schwebt", "biss", "biss", "biss", "schluck", "emotion", "buchen" }));
            Assert.That(seq.Active, Is.False);
            Assert.That(seq.Phase(10_000 + 2500), Is.EqualTo(FeedSequence.Fertig));
            Assert.That(seq.Tick(10_000 + 9999), Is.Empty, "every event only once");
        }

        [Test]
        public void ThePhaseFollowsTheClock()
        {
            var seq = new FeedSequence();
            seq.Start("bread", 0);
            seq.Tick(100);
            Assert.That(seq.Phase(100), Is.EqualTo(FeedSequence.Schweben));
            seq.Tick(800);
            Assert.That(seq.Phase(800), Is.EqualTo(FeedSequence.Mampf));
            seq.Tick(1800);
            Assert.That(seq.Phase(1800), Is.EqualTo(FeedSequence.Schluck));
            seq.Tick(2100);
            Assert.That(seq.Phase(2100), Is.EqualTo(FeedSequence.EmotionPhase));
        }

        [Test]
        public void ADoubleTapIsRefusedUntilTheSequenceEnds()
        {
            var seq = new FeedSequence();
            Assert.That(seq.Start("apple", 0), Is.True);
            Assert.That(seq.Start("apple", 10), Is.False);
            Assert.That(seq.Start("cookie", 500), Is.False);
            Assert.That(seq.FoodId, Is.EqualTo("apple"));
            seq.Tick(2500);
            Assert.That(seq.Active, Is.False);
            Assert.That(seq.Start("cookie", 3000), Is.True, "free again after the end");
        }

        [Test]
        public void ReducedMotionIsTheShortVersionWithOneBite()
        {
            var seq = new FeedSequence();
            Assert.That(seq.Start("apple", 0, true), Is.True);
            Assert.That(seq.BiteCount, Is.EqualTo(1));
            Assert.That(seq.DurationMs, Is.EqualTo(1500), "400 + 350 + 300 + 450 ms");
            Assert.That(seq.Tick(1500).Select(e => e.Typ), Is.EqualTo(new[] { "schwebt", "biss", "schluck", "emotion", "buchen" }));
        }

        [Test]
        public void TheGatesAreTheExistingOnes()
        {
            Assert.That(FeedSequence.Refusal(State(new JObject { ["apple"] = 1 }, 99.9), "apple"), Is.EqualTo("satt"));
            Assert.That(FeedSequence.Refusal(State(new JObject()), "apple"), Is.EqualTo("leer"));
            Assert.That(FeedSequence.Refusal(State(new JObject { ["apple"] = 0 }), "apple"), Is.EqualTo("leer"));
            Assert.That(FeedSequence.Refusal(State(new JObject { ["apple"] = 1 }), "apple"), Is.EqualTo(""));
        }

        [Test]
        public void TheFeelingFollowsTheFood()
        {
            Assert.That(FeedSequence.EmotionFor("carrot"), Is.EqualTo(FeedSequence.Verliebt), "favourite");
            Assert.That(FeedSequence.EmotionFor("pizza"), Is.EqualTo(FeedSequence.Zucker), "junk");
            Assert.That(FeedSequence.EmotionFor("bread"), Is.EqualTo(FeedSequence.Froh));
            var seq = new FeedSequence();
            seq.Start("carrot", 0);
            Assert.That(seq.Tick(2500).Single(e => e.Typ == "emotion").Art, Is.EqualTo(FeedSequence.Verliebt));
        }

        [Test]
        public void EightLinesPerCategoryTakenInTurn()
        {
            var root = System.IO.Path.Combine(Fixtures.ResourcesRoot, "strings");
            var i18n = new Gooby.Core.I18n.I18n(new Gooby.Core.I18n.IndexedStringSource(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "index.json")),
                rel => System.IO.File.Exists(System.IO.Path.Combine(root, rel)) ? System.IO.File.ReadAllText(System.IO.Path.Combine(root, rel)) : null));
            foreach (var locale in new[] { "de", "en" })
            {
                var table = i18n.Table(locale);
                foreach (var kategorie in new[] { "gemuese", "suesses", "warm", "getraenke" })
                {
                    table.TryGetValue("fuettern.sprueche." + kategorie, out var lines);
                    Assert.That((lines as JArray)?.Count, Is.EqualTo(8), locale + "/" + kategorie);
                }
            }
            FeedLines.ResetForTests();
            var first = FeedLines.Next("bread", 8);
            Assert.That(FeedLines.Next("bread", 8), Is.Not.EqualTo(first), "the next meal, the next line");
            for (var i = 0; i < 6; i++) FeedLines.Next("bread", 8);
            Assert.That(FeedLines.Next("bread", 8), Is.EqualTo(first), "after eight from the top");
            FeedLines.ResetForTests();
        }
    }
}
