using System.Collections.Generic;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Random events at home (port of the random_events.gd rules): windows incl. past midnight, weighted pick and
    /// probability gate, one active at a time, cooldown per event, timeout ranges, resolve pays the reward on the stat,
    /// a missed event leaves its fail line once, the ranch defs stay out of the house.
    /// </summary>
    public class RandomEventsTests
    {
        private static List<JObject> Defs() => new List<JObject>
        {
            JObject.Parse(@"{""id"":""a"",""weight"":3,""cooldown_days"":1,""trigger_window"":[""08:00"",""22:00""],""wahrscheinlichkeit"":0.5,""timeout_min"":8,""fail_text_de"":""zu spät"",""reward"":{""stat"":""fun"",""wert"":10}}"),
            JObject.Parse(@"{""id"":""nacht"",""weight"":1,""cooldown_days"":3,""trigger_window"":[""22:30"",""03:00""],""wahrscheinlichkeit"":1.0,""timeout_min"":[10,20],""reward"":null}"),
            JObject.Parse(@"{""id"":""ranch"",""context"":""ranch"",""weight"":5,""trigger_window"":[""00:00"",""23:59""],""wahrscheinlichkeit"":1.0}"),
        };

        [Test]
        public void WindowsWrapPastMidnight()
        {
            var w = new JArray("22:30", "03:00");
            Assert.That(RandomEvents.WindowContains(w, 23 * 60), Is.True);
            Assert.That(RandomEvents.WindowContains(w, 2 * 60), Is.True);
            Assert.That(RandomEvents.WindowContains(w, 12 * 60), Is.False);
            Assert.That(RandomEvents.WindowContains(new JArray("08:00", "22:00"), 7 * 60 + 59), Is.False);
            Assert.That(RandomEvents.ParseMinutes("24:00"), Is.EqualTo(-1));
            Assert.That(RandomEvents.WindowContains(null, 0), Is.True, "no window = always");
        }

        [Test]
        public void PickIsWeightedGatedAndKeepsTheRanchOut()
        {
            var slice = RandomEvents.Slice(new JObject());
            var noon = 12 * 60;
            Assert.That(RandomEvents.PickEvent(Defs(), slice, 0, noon, 0.1, 0.2)?["id"]?.ToString(), Is.EqualTo("a"), "only a is in its window at noon");
            Assert.That(RandomEvents.PickEvent(Defs(), slice, 0, noon, 0.1, 0.6), Is.Null, "probability gate");
            Assert.That(RandomEvents.PickEvent(Defs(), slice, 0, 23 * 60, 0.9, 0.0)?["id"]?.ToString(), Is.EqualTo("nacht"), "weights: the late roll lands on the second");
            Assert.That(RandomEvents.PickEvent(Defs(), slice, 0, noon, 0.1, 0.0, "ranch")?["id"]?.ToString(), Is.EqualTo("ranch"));
        }

        [Test]
        public void OneActiveAtATimeWithCooldownAndTimeout()
        {
            var state = new JObject();
            var now = 1_000_000_000L;
            var started = RandomEvents.RollOnStart(state, Defs(), now, 12 * 60, 0.1, 0.1, 0.0);
            Assert.That(started?["id"]?.ToString(), Is.EqualTo("a"));
            Assert.That(RandomEvents.HasActive(state), Is.True);
            Assert.That(RandomEvents.RollOnStart(state, Defs(), now + 1000, 12 * 60, 0.1, 0.1, 0.0), Is.Null, "one at a time");
            Assert.That((long)state["events"]["active"]["timeout_ms"], Is.EqualTo(now + 8 * RandomEvents.MsPerMin));
            // missed: the next start fails it and leaves the line once
            RandomEvents.RollOnStart(state, Defs(), now + 9 * RandomEvents.MsPerMin, 12 * 60, 0.1, 0.1, 0.0);
            Assert.That(RandomEvents.HasActive(state), Is.False);
            Assert.That(RandomEvents.TakeFailNotice(state), Is.EqualTo("zu spät"));
            Assert.That(RandomEvents.TakeFailNotice(state), Is.EqualTo(""), "only once");
            // its cooldown keeps it away until tomorrow
            Assert.That(RandomEvents.RollOnStart(state, Defs(), now + 2 * 3_600_000L, 12 * 60, 0.1, 0.1, 0.0), Is.Null);
            Assert.That(RandomEvents.RollOnStart(state, Defs(), now + RandomEvents.MsPerDay, 12 * 60, 0.1, 0.1, 0.0)?["id"]?.ToString(), Is.EqualTo("a"));
        }

        [Test]
        public void TimeoutRangesFollowTheRoll()
        {
            var d = Defs()[1];
            Assert.That(RandomEvents.TimeoutDeadline(d, 0, 0.0), Is.EqualTo(10 * RandomEvents.MsPerMin));
            Assert.That(RandomEvents.TimeoutDeadline(d, 0, 1.0), Is.EqualTo(20 * RandomEvents.MsPerMin));
        }

        [Test]
        public void ResolvePaysTheRewardOnTheStat()
        {
            var state = new JObject { ["gooby"] = new JObject { ["stats"] = new JObject { ["fun"] = 95.0 } } };
            RandomEvents.Activate(state, Defs()[0], 0);
            var def = RandomEvents.Resolve(state, Defs());
            Assert.That(def?["id"]?.ToString(), Is.EqualTo("a"));
            Assert.That((double)state["gooby"]["stats"]["fun"], Is.EqualTo(100.0), "clamped");
            Assert.That((int)state["events"]["resolvedTotal"], Is.EqualTo(1));
            Assert.That(RandomEvents.HasActive(state), Is.False);
        }

        [Test]
        public void ThePackEventsAreValid()
        {
            Fixtures.InstallContentSource();
            var defs = RandomEvents.Defs();
            Assert.That(defs.Count, Is.GreaterThanOrEqualTo(14));
            foreach (var d in defs)
            {
                Assert.That((string)d["notification_text_de"], Is.Not.Empty, (string)d["id"]);
                var w = d["trigger_window"] as JArray;
                Assert.That(RandomEvents.ParseMinutes((string)w[0]), Is.GreaterThanOrEqualTo(0), (string)d["id"]);
                Assert.That(RandomEvents.ParseMinutes((string)w[1]), Is.GreaterThanOrEqualTo(0), (string)d["id"]);
            }
        }
    }
}
