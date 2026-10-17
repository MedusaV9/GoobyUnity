using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_migration_transfer.gd core-field checks + the v2 chain fixture.</summary>
    public class MigrationTests
    {
        private const long NowMs = 1_768_478_400_000L;
        private static readonly string[] V4Fixtures = { "v4_fresh.json", "v4_maxed.json", "v4_extras.json", "v2_legacy.expected_v4.json" };

        [TestCaseSource(nameof(AllFixtures))]
        public void CoreFieldsSurviveMigration(string file)
        {
            var v4 = Fixtures.LoadObject("migration/" + file);
            var res = SaveMigration.MigrateAny(v4, NowMs);
            Assert.That(res.Ok, Is.True, file + ": " + res.Error);
            var s = res.State;
            var tag = file + ": ";
            var vac = Vacation.SliceOf(v4);
            var refund = 0;
            if (J.Str(vac["phase"]) != Vacation.PhaseNone && Vacation.TryGetDestination(J.Str(vac["destId"]), out var dest)) refund = dest.Price;
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(J.Int(v4["coins"]) + 250 + refund), tag + "coins incl. bonus/refund");
            Assert.That(J.Int(s["progression"]["level"]), Is.EqualTo(J.Int(v4["level"], 1)), tag + "level 1:1");
            var gotStickers = ((JObject)s["stickers"]["unlocked"]).Properties().Select(p => p.Name).OrderBy(x => x).ToList();
            var wantStickers = (J.Dict(J.Dict(v4["stickers"])["unlocked"])).Properties().Select(p => p.Name).OrderBy(x => x).ToList();
            Assert.That(gotStickers, Is.EqualTo(wantStickers), tag + "sticker set");
            Assert.That(JToken.DeepEquals(s["cosmetics"]["outfits"]["owned"], v4["outfits"]["owned"]), Is.True, tag + "outfits owned");
            Assert.That(JToken.DeepEquals(s["cosmetics"]["fur"]["owned"], v4["skins"]["owned"]), Is.True, tag + "fur owned");
            Assert.That(J.Str(s["cosmetics"]["fur"]["equipped"]), Is.EqualTo(J.Str(v4["skins"]["equipped"])), tag + "fur equipped");
            var boxed = 0;
            foreach (var row in (JArray)s["home"]["storage"]) boxed += J.Int(row["count"]);
            Assert.That(boxed, Is.EqualTo(((JArray)v4["furniture"]["owned"]).Count), tag + "furniture boxed");
            Assert.That(J.IsTrue(s["home"]["movingDay"]), Is.True, tag + "movingDay");
            foreach (var k in Stats.Keys)
                Assert.That(J.Num(s["gooby"]["stats"][k]), Is.EqualTo(J.Num(v4["stats"][k])).Within(1e-9), tag + "stat " + k);
            foreach (var p in ((JObject)v4["achievements"]["counters"]).Properties())
                Assert.That(J.Num(s["achievements"]["counters"][p.Name]), Is.EqualTo(J.Num(p.Value)).Within(1e-9), tag + "counter " + p.Name);
            Assert.That(JToken.DeepEquals(s["daily"], v4["daily"]), Is.True, tag + "daily");
            Assert.That(JToken.DeepEquals(s["collections"], v4["collections"]), Is.True, tag + "collections");
            Assert.That(J.Int(s["profile"]["playtimeMin"]), Is.EqualTo(J.Int(v4["profile"]["playtimeMin"])), tag + "playtime");
            Assert.That(J.Int(s["quests"]["completedTotal"]), Is.EqualTo(J.Int(v4["quests"]["completedTotal"])), tag + "quests total");
            Assert.That(J.Str(s["meta"]["importedFrom"]), Is.EqualTo("web-v4"));
            Assert.That(J.Num(s["progression"]["xp"]), Is.EqualTo(0.0));
            Assert.That(J.Int(s["v"]), Is.EqualTo(5));
            Assert.That(J.Str(s["vacation"]["phase"]), Is.EqualTo("none"), tag + "running trip cancelled");
            Assert.That(((JArray)s["migration"]["lost"]).Count, Is.GreaterThan(3));
            Assert.That(J.Int(res.Report["furnitureBoxed"]), Is.EqualTo(boxed));
            // the result must itself be a valid v5 save (idempotent normalize)
            var again = SaveSchema.Normalize(JObject.Parse(SaveManager.Serialize(s)), NowMs);
            Assert.That(again.Ok, Is.True, again.Error);
        }

        public static IEnumerable<string> AllFixtures() => V4Fixtures;

        [Test]
        public void V2LegacyChainMatchesExpectedV4ThenV5()
        {
            var v2 = Fixtures.LoadObject("migration/v2_legacy.json");
            var viaChain = SaveMigration.MigrateAny(v2, NowMs);
            Assert.That(viaChain.Ok, Is.True, viaChain.Error);
            var fromV4 = SaveMigration.MigrateAny(Fixtures.LoadObject("migration/v2_legacy.expected_v4.json"), NowMs);
            Assert.That(fromV4.Ok, Is.True, fromV4.Error);
            // both paths must produce the same v5 state (modulo import timestamps which are identical here)
            Assert.That(SaveManager.Serialize(viaChain.State), Is.EqualTo(SaveManager.Serialize(fromV4.State)));
        }

        [Test]
        public void FreshWebSaveGetsMovingBonusAndRadioGrandfathering()
        {
            var v4 = Fixtures.LoadObject("migration/v4_fresh.json");
            var res = SaveMigration.MigrateAny(v4, NowMs);
            Assert.That(res.Ok, Is.True, res.Error);
            Assert.That(J.Int(res.State["economy"]["coins"]), Is.EqualTo(J.Int(v4["coins"]) + 250));
            var ownsRadio = ((JArray)v4["furniture"]["owned"]).Any(t => t.Type == JTokenType.String && t.Value<string>() == "radio");
            Assert.That(J.IsTrue(res.State["radio"]["owned"]), Is.EqualTo(ownsRadio));
        }

        [Test]
        public void GarbageIsRejectedNotCrashed()
        {
            Assert.That(SaveMigration.MigrateAny(JToken.Parse("[1,2]"), NowMs).Ok, Is.False);
            Assert.That(SaveMigration.MigrateAny(JObject.Parse("{\"v\": 9}"), NowMs).Ok, Is.False);
            Assert.That(SaveMigration.MigrateAny(JObject.Parse("{\"v\": -1}"), NowMs).Ok, Is.False);
            Assert.That(SaveMigration.MigrateAny(JObject.Parse("{\"v\": 4, \"stats\": [1]}"), NowMs).Ok, Is.False);
            var minimal = SaveMigration.MigrateAny(JObject.Parse("{}"), NowMs);
            Assert.That(minimal.Ok, Is.True, minimal.Error);
            Assert.That(J.Int(minimal.State["economy"]["coins"]), Is.EqualTo(100 + 250));
        }

        [Test]
        public void NumericStringsAreCoercedLikeTheWeb()
        {
            var v4 = Fixtures.LoadObject("migration/v4_fresh.json");
            v4["coins"] = "62";
            v4["level"] = "7";
            v4["stats"]["hunger"] = "250";
            var res = SaveMigration.MigrateAny(v4, NowMs);
            Assert.That(res.Ok, Is.True, res.Error);
            Assert.That(J.Int(res.State["economy"]["coins"]), Is.EqualTo(62 + 250));
            Assert.That(J.Int(res.State["progression"]["level"]), Is.EqualTo(7));
            Assert.That(J.Num(res.State["gooby"]["stats"]["hunger"]), Is.EqualTo(100.0));
        }

        [Test]
        public void LegacySaveNowLoadsThroughTheSaveManager()
        {
            var io = new MemorySaveIo();
            io.Files["user/save_v5.json"] = Fixtures.LoadObject("migration/v4_maxed.json").ToString();
            var r = new SaveManager { SavePath = "user/save_v5.json", Io = io }.LoadState(NowMs);
            Assert.That(r.Recovered, Is.False, "a legacy save is migrated, not recovered");
            Assert.That(r.Fresh, Is.False);
            Assert.That(J.Int(r.State["v"]), Is.EqualTo(5));
            Assert.That(J.Str(r.State["meta"]["importedFrom"]), Is.EqualTo("web-v4"));
        }
    }
}
