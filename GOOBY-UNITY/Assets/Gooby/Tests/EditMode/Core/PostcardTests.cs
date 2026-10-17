using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_rest4_postkarten.gd (pure parts) + web golden (GOOBY/src/systems/postcards.js).</summary>
    public class PostcardTests
    {
        private const long Now = 1_700_000_000_000L;
        private const long Tag = Postcards.MsPerDay;

        [SetUp]
        public void Setup() => Postcards.Install();

        [Test]
        public void SeedsAndVariantsMatchTheWebGolden()
        {
            var golden = Fixtures.LoadObject("postcards_golden.json");
            foreach (var p in ((JObject)golden["seeds"]).Properties())
                Assert.That(Postcards.TripSeed(p.Name, Now), Is.EqualTo(p.Value.Value<uint>()), "seed " + p.Name);
            foreach (var dest in Postcards.DestIds)
            {
                var want = golden["variants"][dest].Select(t => t.Value<int>()).ToList();
                var got = Enumerable.Range(1, 11).Select(k => Postcards.VariantOf(dest, Now, k)).ToList();
                Assert.That(got, Is.EqualTo(want), "variants " + dest);
            }
            var other = golden["variants"]["beach@other"].Select(t => t.Value<int>()).ToList();
            Assert.That(Enumerable.Range(1, 6).Select(k => Postcards.VariantOf("beach", Now + 12345678, k)).ToList(), Is.EqualTo(other));
            Assert.That(Postcards.TextKey(new JObject { ["destId"] = "beach", ["variant"] = 3 }), Is.EqualTo("postkarten.text.beach.3"));
        }

        [Test]
        public void NormalizeArchiveDropsJunkAndCaps()
        {
            var raw = JArray.Parse("[{\"destId\":\"beach\",\"dayIndex\":1,\"variant\":2,\"atMs\":200},\"junk\",{\"destId\":\"nirgendwo\",\"dayIndex\":1,\"variant\":1,\"atMs\":1},{\"destId\":\"space\",\"dayIndex\":1,\"variant\":1,\"atMs\":100},{\"destId\":\"beach\",\"dayIndex\":1,\"variant\":2,\"atMs\":200},{\"destId\":\"beach\",\"dayIndex\":0,\"variant\":2,\"atMs\":300}]");
            var sauber = Postcards.NormalizeArchive(raw);
            Assert.That(sauber.Count, Is.EqualTo(2));
            Assert.That(J.Str(sauber[0]["destId"]), Is.EqualTo("space"), "chronological");
            var many = new JArray();
            for (var i = 1; i <= 46; i++) many.Add(new JObject { ["destId"] = "beach", ["dayIndex"] = i, ["variant"] = 1, ["atMs"] = i * 1000 });
            var capped = Postcards.NormalizeArchive(many);
            Assert.That(capped.Count, Is.EqualTo(Postcards.MaxArchive));
            Assert.That(J.Int(capped[0]["dayIndex"]), Is.EqualTo(11), "oldest dropped first");
        }

        [Test]
        public void ProcessorGeneratesAndStaysIdempotent()
        {
            var golden = Fixtures.LoadObject("postcards_golden.json")["process"];
            var v = new JObject { ["destId"] = "beach", ["bookedAt"] = Now, ["returnAt"] = Now + 5 * Tag, ["archive"] = new JArray(), ["lastPostcardDayProcessed"] = 0 };
            var (archive, last, added) = Postcards.ProcessPostcardsUpTo(v, Now + 2 * Tag + 5000);
            Assert.That(added, Is.EqualTo(2));
            Assert.That(J.Long(archive[0]["atMs"]), Is.EqualTo(Now + Tag));
            Assert.That(J.Int(archive[1]["dayIndex"]), Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(archive, golden["archive"]), Is.True, "web-identical archive");
            Assert.That(last, Is.EqualTo(golden["lastPostcardDayProcessed"].Value<int>()));
            v["archive"] = archive;
            v["lastPostcardDayProcessed"] = last;
            Assert.That(Postcards.ProcessPostcardsUpTo(v, Now + 2 * Tag + 5000).added, Is.EqualTo(0));
            Assert.That(Postcards.ProcessPostcardsUpTo(v, Now + 99 * Tag).archive.Count, Is.EqualTo(4), "5-day trip = max 4 cards");
            Assert.That(Postcards.ProcessPostcardsUpTo(new JObject { ["destId"] = "nirgendwo" }, Now).added, Is.EqualTo(0));
        }

        [Test]
        public void VacationTickFillsTheArchive()
        {
            var state = SaveSchema.DefaultState(Now);
            var v = (JObject)state["vacation"];
            v["phase"] = "away";
            v["destId"] = "harbor";
            v["bookedAt"] = Now;
            v["returnAt"] = Now + 4 * Tag;
            var result = Vacation.Tick(state, Now + 3 * Tag + 60000);
            Assert.That(result.changes, Is.Not.Null);
            Assert.That(((JArray)result.changes["archive"]).Count, Is.EqualTo(3));
            Assert.That(J.Int(result.changes["lastPostcardDayProcessed"]), Is.EqualTo(3));
            state["vacation"] = result.changes;
            Assert.That(Vacation.Tick(state, Now + 3 * Tag + 60000).changes, Is.Null, "same clock, no new changes");
        }

        [Test]
        public void SetBonusTiersAndClaim()
        {
            var state = SaveSchema.DefaultState(Now);
            var visited = (JObject)state["vacation"]["visited"];
            visited["beach"] = true; visited["space"] = true; visited["bakery"] = true;
            var stufen = Postcards.SetStufenOf(state);
            Assert.That(stufen.Count, Is.EqualTo(3));
            Assert.That(stufen[0].Erreicht, Is.True);
            Assert.That(stufen[1].Erreicht, Is.False);
            Assert.That(Postcards.ClaimSetBonus(state, 6, Now), Is.EqualTo(0));
            Assert.That(Postcards.ClaimSetBonus(state, 3, Now), Is.EqualTo(150));
            Assert.That(Postcards.ClaimSetBonus(state, 3, Now), Is.EqualTo(0));
            Assert.That(Postcards.BesuchtAnzahl(state), Is.EqualTo(3));
            Assert.That(Postcards.SouvenirsVon(state), Is.EqualTo(new[] { "beach", "space", "bakery" }));
        }
    }
}
