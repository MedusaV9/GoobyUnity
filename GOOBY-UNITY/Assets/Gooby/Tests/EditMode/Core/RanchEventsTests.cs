using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The ranch's random events (ranch_event_host.gd + ranch_event_rewards.gd): context, rewards, the farrier's buff.</summary>
    public class RanchEventsTests
    {
        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        private const long T0 = 1_800_000_000_000L;

        [Test]
        public void RanchEventsOnlyRollOnTheRanch()
        {
            var defs = RandomEvents.Defs();
            var ranch = defs.Where(d => J.Str(d["context"]) == RanchEvents.Context).Select(d => J.Str(d["id"])).ToList();
            Assert.That(ranch, Is.EquivalentTo(new[] { "ausgebuext", "heudieb", "hufschmied", "karottenregen" }));
            var slice = new JObject();
            foreach (var d in defs.Where(d => J.Str(d["context"]) == RanchEvents.Context))
            {
                Assert.That(RandomEvents.IsAvailable(d, slice, T0, 12 * 60, "home"), Is.False, J.Str(d["id"]) + " never at home");
                Assert.That(RandomEvents.IsAvailable(d, slice, T0, 12 * 60, RanchEvents.Context), Is.True, J.Str(d["id"]) + " at noon on the ranch");
            }
        }

        [Test]
        public void RewardsLandWhereTheyBelong()
        {
            var s = new JObject { ["progression"] = new JObject { ["level"] = 20 }, ["economy"] = new JObject { ["coins"] = RanchHof.Preis } };
            RanchKauf.Kaufe(s, T0);
            var defs = RandomEvents.Defs();
            JObject Def(string id) => defs.First(d => J.Str(d["id"]) == id);
            var vorher = J.Int(s["economy"]["coins"]);
            var bond = J.NumOr(RanchHof.Pferd(s, "tier_pferd_wolke")["bindung"], 0.0);
            RanchEvents.Belohnen(s, Def("ausgebuext"), "tier_pferd_wolke", T0);
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(vorher + 25));
            Assert.That(J.NumOr(RanchHof.Pferd(s, "tier_pferd_wolke")["bindung"], 0.0), Is.EqualTo(bond + 6).Within(1e-9));
            var heu = J.Int(RanchHof.Lager(s)["heu"]);
            RanchEvents.Belohnen(s, Def("heudieb"), null, T0);
            Assert.That(J.Int(RanchHof.Lager(s)["heu"]), Is.EqualTo(heu + 2));
            Assert.That(RanchEvents.HufCheckAktiv(s, T0), Is.False);
            RanchEvents.Belohnen(s, Def("hufschmied"), null, T0);
            Assert.That(RanchEvents.HufCheckAktiv(s, T0 + 23 * 3_600_000L), Is.True);
            Assert.That(RanchEvents.HufCheckAktiv(s, T0 + 25 * 3_600_000L), Is.False);
            var res = RanchEvents.Belohnen(s, Def("karottenregen"), null, T0, gesammelt: 14);
            Assert.That(J.Int(res["karotten"]), Is.EqualTo(10), "at most ten carrots");
            Assert.That(J.Int(s["inventory"]["food"]["carrot"]), Is.EqualTo(10));
        }
    }
}
