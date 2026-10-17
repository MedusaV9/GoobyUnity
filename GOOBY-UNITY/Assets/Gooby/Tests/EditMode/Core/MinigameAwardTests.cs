using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Godot-golden (headless run of minigame_award.gd / arcade_spotlight.gd) parity for round booking + spotlight.</summary>
    public class MinigameAwardTests
    {
        private const long NowMs = 1_768_478_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            MinigameRegistry.ResetCache();
        }

        [Test]
        public void RegistryHasAll38GamesWithCoinTables()
        {
            var all = MinigameRegistry.AllGames();
            Assert.That(all.Count, Is.EqualTo(38));
            var cc = MinigameRegistry.GetGame("carrotCatch");
            Assert.That(cc.CoinTable.Divisor, Is.EqualTo(3));
            Assert.That(cc.Target, Is.EqualTo(70));
            Assert.That(cc.SupportsEndless, Is.True);
            Assert.That(MinigameRegistry.GetGame("gvz").Orientation, Is.EqualTo("landscape"));
            Assert.That(MinigameRegistry.GetGame("cityDrive").EnergyCost, Is.EqualTo(6));
            var golden = Fixtures.LoadObject("spotlight_golden.json");
            Assert.That(ArcadeSpotlight.PoolIds(all), Is.EqualTo(golden["registry_ids"].Select(t => t.ToString()).ToList()));
        }

        [Test]
        public void SpotlightMatchesGodotGolden()
        {
            var golden = Fixtures.LoadObject("spotlight_golden.json");
            var pool = new[] { "teaParty", "carrotCatch", "bunnyHop", "runner", "fishingPond", "miniGolf", "ghostHunt" }.Select(id => new MinigameMeta { Id = id }).ToList();
            foreach (var p in golden.Properties())
            {
                if (!p.Name.StartsWith("20")) continue;
                Assert.That(ArcadeSpotlight.SpotlightId(pool, p.Name), Is.EqualTo(p.Value.ToString()), p.Name);
            }
            Assert.That(ArcadeSpotlight.SpotlightId(MinigameRegistry.AllGames(), "2026-09-13"), Is.EqualTo(golden["registry_2026-09-13"].ToString()));
            Assert.That(ArcadeSpotlight.SpotlightId(new List<MinigameMeta>(), "2026-03-05"), Is.EqualTo(""));
            Assert.That(ArcadeSpotlight.SpotlightId(pool, "quatsch"), Is.EqualTo(""));
            Assert.That(ArcadeSpotlight.SpotlightId(new[] { new MinigameMeta { Id = "teaParty" } }, "2026-03-06"), Is.EqualTo("teaParty"));
        }

        [Test]
        public void AwardMatchesGodotGoldenStepByStep()
        {
            var golden = Fixtures.LoadObject("minigame_award_golden.json");
            var state = SaveSchema.DefaultState(NowMs);
            var cc = MinigameRegistry.GetGame("carrotCatch");
            var steps = (JArray)golden["steps"];
            Check(steps[0], MinigameAward.Award(state, cc, 70, "normal", "2026-01-15"), state);
            Assert.That(J.Num(state["gooby"]["stats"]["fun"]), Is.EqualTo(J.Num(steps[0]["fun"])));
            Check(steps[1], MinigameAward.Award(state, cc, 45, "normal", "2026-01-15"), state);
            Check(steps[2], MinigameAward.Award(state, cc, 120, "hard", "2026-01-15"), state);
            Check(steps[3], MinigameAward.Award(state, cc, 300, "endless", "2026-01-15"), state);
            Check(steps[4], MinigameAward.Award(state, MinigameRegistry.GetGame("teaParty"), 90, "easy", "2026-01-15", new[] { 30, 30, 30 }), state);
            for (var i = 0; i < 12; i++) MinigameAward.Award(state, cc, 200, "normal", "2026-01-15");
            CheckState(steps[5], state);
            var r6 = MinigameAward.Award(state, cc, 50, "normal", "2026-01-16");
            Check(steps[6], r6, state);
            Assert.That(r6.FirstToday, Is.True, "new day → first play again");
        }

        private static void Check(JToken step, AwardResult result, JObject state)
        {
            var label = step["label"].ToString();
            var want = (JObject)step["result"];
            var got = result.ToJson();
            foreach (var p in want.Properties())
                Assert.That(JToken.DeepEquals(Canon(got[p.Name]), Canon(p.Value)), Is.True, $"{label}: result.{p.Name} want {p.Value} got {got[p.Name]}");
            CheckState(step, state);
        }

        private static void CheckState(JToken step, JObject state)
        {
            var label = step["label"].ToString();
            foreach (var (key, path) in new[] { ("econ", "economy"), ("mg", "minigames"), ("prog", "progression") })
            {
                var want = Canon(step[key]);
                var got = Canon(state[path]);
                Assert.That(JToken.DeepEquals(got, want), Is.True, $"{label}: {path}\nwant {want}\ngot  {got}");
            }
        }

        /// <summary>Godot writes integral floats as 70.0 / ints as 70 depending on the path; compare on canonical numbers.</summary>
        private static JToken Canon(JToken t)
        {
            switch (t)
            {
                case null: return JValue.CreateNull();
                case JObject o:
                {
                    var c = new JObject();
                    foreach (var p in o.Properties().OrderBy(p => p.Name, System.StringComparer.Ordinal)) c[p.Name] = Canon(p.Value);
                    return c;
                }
                case JArray a: return new JArray(a.Select(Canon));
                default:
                    if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
                    {
                        var d = t.Value<double>();
                        return d == System.Math.Floor(d) && System.Math.Abs(d) < 1e15 ? new JValue((long)d) : new JValue(d);
                    }
                    return t;
            }
        }
    }
}
