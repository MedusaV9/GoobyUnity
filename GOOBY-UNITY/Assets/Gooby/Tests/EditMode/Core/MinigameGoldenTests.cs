using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Minigames;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Port of tests/unit/test_w13c_crosscheck.gd: every registered minigame simulation replays the
    /// web release bot for 4 modes × 50 seeds and must match tests/expected/&lt;id&gt;.json field by field
    /// (ints exact, floats ±1e-9). The fixtures were generated from the ORIGINAL web logic.
    /// </summary>
    public class MinigameGoldenTests
    {
        private static IEnumerable<string> GameIds()
        {
            foreach (var s in MinigameSims.All) yield return s.Id;
        }

        private static JToken Dig(JObject result, string path)
        {
            JToken v = result;
            foreach (var part in path.Split('.')) v = v[part];
            return v;
        }

        [TestCaseSource(nameof(GameIds))]
        public void SimulationMatchesWebFixture(string id)
        {
            var spec = MinigameSims.Find(id);
            var fixture = Fixtures.LoadObject($"expected/{id}.json");
            var modes = (JObject)fixture["modes"];
            Assert.That(modes.Count, Is.GreaterThanOrEqualTo(1), id);
            var runs = 0;
            var failures = new List<string>();
            foreach (var modeProp in modes.Properties())
            {
                var mode = modeProp.Name;
                foreach (var run in (JArray)modeProp.Value)
                {
                    var seed = run["seed"].Value<int>();
                    var got = spec.Autoplay(seed, mode);
                    var tag = $"{id} {mode} seed={seed}";
                    foreach (var f in spec.IntFields)
                    {
                        var g = (long)J.Num(Dig(got, f));
                        var w = run[f].Value<long>();
                        if (g != w) failures.Add($"{tag} {f}: got={g} want={w}");
                    }
                    foreach (var f in spec.FloatFields)
                    {
                        var g = J.Num(Dig(got, f));
                        var w = run[f].Value<double>();
                        if (System.Math.Abs(g - w) > 1e-9) failures.Add($"{tag} {f}: got={g:R} want={w:R}");
                    }
                    foreach (var f in spec.StringFields)
                    {
                        var g = Dig(got, f)?.ToString();
                        var w = run[f]?.ToString();
                        if (g != w) failures.Add($"{tag} {f}: got={g} want={w}");
                    }
                    runs++;
                    if (failures.Count >= 12) break;
                }
                if (failures.Count >= 12) break;
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            Assert.That(runs, Is.GreaterThanOrEqualTo(50), id + " runs");
        }

        [TestCaseSource(nameof(GameIds))]
        public void TuneMatchesWebFixtureWhenPresent(string id)
        {
            var spec = MinigameSims.Find(id);
            if (spec.TuneFor == null) Assert.Ignore("no tune exporter");
            var fixture = Fixtures.LoadObject($"expected/{id}.json");
            var tunes = fixture["tune"] as JObject;
            if (tunes == null) Assert.Ignore("fixture has no tune section");
            foreach (var modeProp in tunes.Properties())
            {
                var got = spec.TuneFor(modeProp.Name);
                foreach (var kv in (JObject)modeProp.Value)
                {
                    var g = got[kv.Key];
                    Assert.That(g, Is.Not.Null, $"{id} tune {modeProp.Name}.{kv.Key} missing");
                    if (kv.Value.Type == JTokenType.Boolean) Assert.That(g.Value<bool>(), Is.EqualTo(kv.Value.Value<bool>()), $"{id} {modeProp.Name}.{kv.Key}");
                    else Assert.That(J.Num(g), Is.EqualTo(kv.Value.Value<double>()).Within(1e-12), $"{id} {modeProp.Name}.{kv.Key}");
                }
            }
        }

        [Test]
        public void FrameworkCoinCasesMatchWebFixture()
        {
            var fixture = Fixtures.LoadObject("expected/framework.json");
            var rows = new Dictionary<string, FrameworkLogic.CoinTable>
            {
                { "teaParty", new FrameworkLogic.CoinTable(4, 4, 26) },
                { "carrotCatch", new FrameworkLogic.CoinTable(3, 4, 25) },
                { "hideSeek", new FrameworkLogic.CoinTable(5, 4, 20) },
                { "goobySays", new FrameworkLogic.CoinTable(5, 4, 24) },
                { "gardenRush", new FrameworkLogic.CoinTable(3, 4, 25) },
                { "burgerBuild", new FrameworkLogic.CoinTable(4, 4, 26) },
                { "bunnyHop", new FrameworkLogic.CoinTable(2, 3, 25) },
                { "memoryMatch", new FrameworkLogic.CoinTable(2, 5, 24) },
                { "bubblePop", new FrameworkLogic.CoinTable(4, 4, 24) },
            };
            var checkedCases = 0;
            foreach (var c in (JArray)fixture["coinCases"])
            {
                var game = c["game"].Value<string>();
                if (!rows.TryGetValue(game, out var table)) continue;
                var coins = FrameworkLogic.ApplyDifficultyCoinBase(table, c["score"].Value<double>(), c["mode"].Value<string>());
                Assert.That(coins, Is.EqualTo(c["coins"].Value<int>()), c.ToString());
                checkedCases++;
            }
            Assert.That(checkedCases, Is.GreaterThan(20));
        }
    }
}
