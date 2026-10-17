using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Numeric parity of the life-sim ports against golden_values.json (generated from the real web code).</summary>
    public class LifeGoldenTests
    {
        private const double Eps = 1e-9;

        private static void AssertStats(JObject got, JToken want, string label)
        {
            foreach (var k in Stats.Keys)
                Assert.That(J.Num(got[k]), Is.EqualTo(want[k].Value<double>()).Within(Eps), $"{label}.{k}");
        }

        [Test]
        public void LevelingMatchesGolden()
        {
            var g = (JObject)Fixtures.Golden()["leveling"];
            Assert.That(Leveling.MaxLevel, Is.EqualTo(g["maxLevel"].Value<int>()));
            foreach (var p in ((JObject)g["xpToNext"]).Properties())
                Assert.That(Leveling.XpToNext(int.Parse(p.Name)), Is.EqualTo(p.Value.Value<int>()), "xpToNext " + p.Name);
            Assert.That(Leveling.CumulativeXpToLevel(40), Is.EqualTo(g["cumulativeXpTo40"].Value<int>()));
            Assert.That(Leveling.CumulativeXpToLevel(10), Is.EqualTo(g["cumulativeXpTo10"].Value<int>()));
            foreach (var c in (JArray)g["applyXpCases"])
            {
                var r = Leveling.ApplyXp((JObject)c["progress"], c["amount"].Value<double>());
                var want = c["result"];
                Assert.That(r.Xp, Is.EqualTo(want["xp"].Value<double>()).Within(Eps), c.ToString());
                Assert.That(r.Level, Is.EqualTo(want["level"].Value<int>()), c.ToString());
                Assert.That(r.LevelsGained, Is.EqualTo(want["levelsGained"].Value<int>()), c.ToString());
                Assert.That(r.CoinsAwarded, Is.EqualTo(want["coinsAwarded"].Value<int>()), c.ToString());
            }
            foreach (var p in ((JObject)g["minigameXp"]).Properties())
                Assert.That(Leveling.MinigameXp(double.Parse(p.Name)), Is.EqualTo(p.Value.Value<int>()), "minigameXp " + p.Name);
        }

        [Test]
        public void StatsMatchGolden()
        {
            var g = (JObject)Fixtures.Golden()["stats"];
            foreach (var k in Stats.Keys)
            {
                Assert.That(Stats.RatesAwake[k], Is.EqualTo(g["ratesAwake"][k].Value<double>()));
                Assert.That(Stats.RatesAsleep[k], Is.EqualTo(g["ratesAsleep"][k].Value<double>()));
            }
            foreach (var c in (JArray)g["applyTickCases"])
            {
                var opts = (JObject)c["opts"];
                var got = Stats.ApplyTick((JObject)c["stats"], c["dtMin"].Value<double>(),
                    asleep: J.IsTrue(opts["asleep"]), rateMult: J.NumOr(opts["rateMult"], 1.0));
                AssertStats(got, c["result"], c.ToString());
            }
            foreach (var c in (JArray)g["moodCases"])
            {
                var opts = (JObject)c["opts"];
                var mood = Stats.Mood((JObject)c["stats"], J.NumOr(opts["debuff"], 0.0));
                Assert.That(mood, Is.EqualTo(c["result"].Value<double>()).Within(Eps), c.ToString());
                Assert.That(Stats.MoodBand(mood), Is.EqualTo(c["band"].Value<string>()), c.ToString());
            }
            var bands = (JArray)g["bands"];
            for (var i = 0; i < bands.Count; i++)
            {
                Assert.That(Stats.MoodBands[i].id, Is.EqualTo(bands[i]["id"].Value<string>()));
                Assert.That(Stats.MoodBands[i].min, Is.EqualTo(bands[i]["min"].Value<double>()));
            }
        }

        [Test]
        public void EconomyMatchesGolden()
        {
            var g = (JObject)Fixtures.Golden()["economy"];
            Assert.That(Economy.StartingCoins, Is.EqualTo(g["startingCoins"].Value<int>()));
            Assert.That(Economy.DayCoinCap, Is.EqualTo(g["dayCoinCap"].Value<int>()));
            foreach (var p in ((JObject)g["quickPrices"]).Properties())
                Assert.That(Economy.QuickPrice(double.Parse(p.Name)), Is.EqualTo(p.Value.Value<int>()), "quickPrice " + p.Name);
            var econ = Economy.DefaultSlice();
            const string day = "2026-01-15";
            foreach (var step in (JArray)g["steps"])
            {
                var kind = step["kind"].Value<string>();
                if (kind == "award")
                {
                    var args = (JArray)step["args"];
                    var ret = Economy.Award(econ, args[0].Value<double>(), args[1].Value<string>(), day);
                    Assert.That(ret, Is.EqualTo(step["ret"].Value<int>()), step.ToString());
                }
                else if (kind == "spend")
                {
                    var args = (JArray)step["args"];
                    var ret = Economy.Spend(econ, args[0].Value<double>(), args[1].Value<string>());
                    Assert.That(ret, Is.EqualTo(step["ret"].Value<bool>()), step.ToString());
                }
                foreach (var key in new[] { "coins", "coinsEarned", "coinsSpent", "dayCoins", "endlessCoins" })
                    Assert.That(J.Int(econ[key]), Is.EqualTo(step[key].Value<int>()), $"{kind} {key}: {step}");
            }
        }

        [Test]
        public void SleepMatchesGolden()
        {
            var g = (JObject)Fixtures.Golden()["sleep"];
            foreach (var p in ((JObject)g["durations"]).Properties())
                Assert.That(Sleep.SleepDurationMin(double.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture)),
                    Is.EqualTo(p.Value.Value<int>()), "duration " + p.Name);
            foreach (var c in (JArray)g["canSleepCases"])
            {
                var state = new JObject
                {
                    ["stats"] = new JObject { ["energy"] = c["energy"].Value<double>(), ["hunger"] = 50, ["hygiene"] = 50, ["fun"] = 50 },
                    ["sleep"] = new JObject { ["sleeping"] = c["sleeping"].Value<bool>(), ["startedAt"] = 0, ["wakeAt"] = 0 },
                };
                Assert.That(Sleep.CanSleep(state), Is.EqualTo(c["result"].Value<bool>()), c.ToString());
            }
            var grants = (JObject)g["completedGrants"];
            var input = (JObject)grants["input"];
            var flat = new JObject
            {
                ["xp"] = input["xp"], ["level"] = input["level"], ["coins"] = input["coins"],
                ["achievements"] = new JObject { ["counters"] = new JObject { ["sleeps"] = input["sleeps"] } },
            };
            var outp = Sleep.ApplyCompletedSleepGrants(flat);
            var want = (JObject)grants["output"];
            Assert.That(J.Num(outp["xp"]), Is.EqualTo(want["xp"].Value<double>()).Within(Eps));
            Assert.That(J.Int(outp["level"]), Is.EqualTo(want["level"].Value<int>()));
            Assert.That(J.Int(outp["coins"]), Is.EqualTo(want["coins"].Value<int>()));
            Assert.That(J.Int(outp["achievements"]["counters"]["sleeps"]), Is.EqualTo(want["achievements"]["counters"]["sleeps"].Value<int>()));

            var tick = (JObject)g["tickSleepCase"];
            var wantState = (JObject)tick["result"]["state"];
            var started = new JObject
            {
                ["stats"] = new JObject { ["hunger"] = 60, ["energy"] = 40, ["hygiene"] = 60, ["fun"] = 60 },
                ["sleep"] = tick["started"].DeepClone(),
                ["grumpyUntil"] = 0,
                ["lastTickAt"] = tick["started"]["startedAt"],
                ["xp"] = 0, ["level"] = 1, ["coins"] = 0,
                ["achievements"] = new JObject { ["counters"] = new JObject { ["sleeps"] = 0 } },
            };
            var (res, events) = Sleep.TickSleep(started, wantState["lastTickAt"].Value<long>());
            AssertStats((JObject)res["stats"], wantState["stats"], "tickSleep");
            Assert.That(J.IsTrue(res["sleep"]["sleeping"]), Is.False);
            Assert.That(J.Num(res["xp"]), Is.EqualTo(wantState["xp"].Value<double>()).Within(Eps));
            Assert.That(J.Int(res["achievements"]["counters"]["sleeps"]), Is.EqualTo(1));
            Assert.That(events, Is.EqualTo(new List<string> { "wokeUp" }));
        }

        [Test]
        public void OfflineCasesMatchGolden()
        {
            var g = (JObject)Fixtures.Golden()["offline"];
            var nowMs = Fixtures.Golden()["vacation"]["nowMs"].Value<long>();
            Assert.That(Offline.AwakeRateMult, Is.EqualTo(g["awakeRateMult"].Value<double>()));
            Assert.That(Offline.AwakeCapMin, Is.EqualTo(g["awakeCapMin"].Value<double>()));
            foreach (var c in (JArray)g["cases"])
            {
                var label = c["label"].Value<string>();
                var input = (JObject)c["input"].DeepClone();
                if (input["vacation"] == null || input["vacation"].Type == JTokenType.Null) input.Remove("vacation");
                var (state, events) = Offline.SimulateOffline(input, nowMs);
                var want = (JObject)c["result"];
                AssertStats((JObject)state["stats"], want["stats"], label);
                Assert.That(J.Long(state["lastTickAt"]), Is.EqualTo(want["lastTickAt"].Value<long>()), label);
                Assert.That(J.IsTrue(state["sleep"]["sleeping"]), Is.EqualTo(want["sleep"]["sleeping"].Value<bool>()), label);
                Assert.That(J.Num(state["xp"]), Is.EqualTo(want["xp"].Value<double>()).Within(Eps), label);
                Assert.That(J.Int(state["level"]), Is.EqualTo(want["level"].Value<int>()), label);
                Assert.That(J.Int(state["coins"]), Is.EqualTo(want["coins"].Value<int>()), label);
                var vac = Vacation.SliceOf(state);
                Assert.That(J.Str(vac["phase"]), Is.EqualTo(want["vacationPhase"].Value<string>()), label);
                Assert.That(J.Int(vac["postcards"]), Is.EqualTo(want["vacationPostcards"].Value<int>()), label);
                var wantEvents = new List<string>();
                foreach (var e in (JArray)want["events"]) wantEvents.Add(e.Value<string>());
                Assert.That(events, Is.EqualTo(wantEvents), label);
            }
        }

        [Test]
        public void VacationCatalogAndPhasesMatchGolden()
        {
            var g = (JObject)Fixtures.Golden()["vacation"];
            var ids = new List<string>();
            foreach (var d in Vacation.Catalog) ids.Add(d.Id);
            var wantIds = new List<string>();
            foreach (var t in (JArray)g["ids"]) wantIds.Add(t.Value<string>());
            Assert.That(ids, Is.EqualTo(wantIds));
            Assert.That(Vacation.MsPerDay, Is.EqualTo(g["msPerDay"].Value<long>()));
            Assert.That(Vacation.PickupWindowMs, Is.EqualTo(g["pickupWindowMs"].Value<long>()));
            var nowMs = g["nowMs"].Value<long>();
            var state = new JObject
            {
                ["vacation"] = new JObject
                {
                    ["phase"] = "away", ["destId"] = "beach", ["bookedAt"] = nowMs - Vacation.MsPerDay,
                    ["returnAt"] = nowMs + g["midgameRemainingMs"].Value<long>(),
                    ["pickupBy"] = nowMs + g["midgameRemainingMs"].Value<long>() + Vacation.PickupWindowMs,
                },
            };
            Assert.That(Vacation.RemainingMs(state, nowMs), Is.EqualTo(g["midgameRemainingMs"].Value<long>()));
            Assert.That(Vacation.PhaseAt(Vacation.SliceOf(state), nowMs), Is.EqualTo(g["midgamePhase"].Value<string>()));
        }

        [Test]
        public void VacationTickWalksSkippedStagesInOrder()
        {
            var booked = 1_000_000_000_000L;
            var state = new JObject
            {
                ["vacation"] = new JObject
                {
                    ["phase"] = "away", ["destId"] = "harbor", ["bookedAt"] = booked,
                    ["returnAt"] = booked + 3 * Vacation.MsPerDay,
                    ["pickupBy"] = booked + 4 * Vacation.MsPerDay,
                },
            };
            var (changes, events) = Vacation.Tick(state, booked + 5 * Vacation.MsPerDay);
            Assert.That(changes, Is.Not.Null);
            Assert.That(J.Str(changes["phase"]), Is.EqualTo(Vacation.PhaseOverdue));
            Assert.That(J.Int(changes["postcards"]), Is.EqualTo(2));
            var types = events.ConvertAll(e => e.Type);
            Assert.That(types, Is.EqualTo(new List<string> { "postcard", "postcard", "returnReady", "overdue" }));
        }

        [Test]
        public void WeightModelIsCosmeticAndMonotone()
        {
            Assert.That(Weight.TierOf(25.0), Is.EqualTo("sleek"));
            Assert.That(Weight.TierOf(25.01), Is.EqualTo("chubby"));
            Assert.That(Weight.TierOf(60.0), Is.EqualTo("chubby"));
            Assert.That(Weight.TierOf(85.0), Is.EqualTo("chonky"));
            Assert.That(Weight.TierOf(85.01), Is.EqualTo("floof"));
            Assert.That(Weight.BodyScale(15.0), Is.EqualTo(0.93).Within(Eps));
            Assert.That(Weight.BodyScale(42.5), Is.EqualTo(1.0).Within(Eps));
            Assert.That(Weight.BodyScale(90.0), Is.EqualTo(1.14).Within(Eps));
            var prev = 0.0;
            for (var w = 5.0; w <= 95.0; w += 0.5)
            {
                var s = Weight.BodyScale(w);
                Assert.That(s, Is.GreaterThanOrEqualTo(prev));
                prev = s;
            }
            Assert.That(Weight.OnEat(50.0, junk: true), Is.EqualTo(52.0).Within(Eps));
            Assert.That(Weight.OnMinigameEnd(50.0, "runner"), Is.EqualTo(49.0).Within(Eps));
            Assert.That(Weight.OnMinigameEnd(50.0, "memoryMatch"), Is.EqualTo(49.75).Within(Eps));
            Assert.That(Weight.Tick(60.0, 1440.0), Is.EqualTo(58.0).Within(Eps));
            Assert.That(Weight.Tick(50.5, 1440.0), Is.EqualTo(50.0).Within(Eps));
        }

        [Test]
        public void HealthEscalatesOneStepPerTickAndRecovers()
        {
            var h = Health.Normalize(null);
            for (var i = 0; i < 5; i++) h = Health.OnEat(h, junk: true);
            Assert.That(J.Num(h["junkScore"]), Is.EqualTo(5.0).Within(Eps));
            var (h1, ev1) = Health.Tick(h, 0.0, 0, Health.TickOptions.Default);
            Assert.That(J.Str(h1["state"]), Is.EqualTo("queasy"));
            Assert.That(ev1, Does.Contain("becameQueasy"));
            Assert.That(ev1, Does.Not.Contain("becameSick"));
            for (var i = 0; i < 3; i++) h1 = Health.OnEat(h1, junk: true);
            var (h2, ev2) = Health.Tick(h1, 0.0, 0, Health.TickOptions.Default);
            Assert.That(J.Str(h2["state"]), Is.EqualTo("sick"));
            Assert.That(ev2, Is.EqualTo(new List<string> { "becameSick" }));
            var (h3, ok) = Health.UseMedicine(h2, 123);
            Assert.That(ok, Is.True);
            Assert.That(J.Str(h3["state"]), Is.EqualTo("queasy"));
            var cured = Health.VetCure(h3, 456);
            Assert.That(J.Str(cured["state"]), Is.EqualTo("healthy"));
            Assert.That(J.Num(cured["junkScore"]), Is.EqualTo(0.0));
            var (_, okHealthy) = Health.UseMedicine(cured);
            Assert.That(okHealthy, Is.False);
        }

        [Test]
        public void HealthTummyWarningFiresOnceAtJunkFour()
        {
            var h = Health.Normalize(null);
            for (var i = 0; i < 4; i++) h = Health.OnEat(h, junk: true);
            Assert.That(J.IsTrue(h["tummyWarnPending"]), Is.True);
            var (h1, ev1) = Health.Tick(h, 1.0, 0, Health.TickOptions.Default);
            Assert.That(ev1, Is.EqualTo(new List<string> { "tummyWarning" }));
            var (_, ev2) = Health.Tick(h1, 1.0, 0, Health.TickOptions.Default);
            Assert.That(ev2, Is.Empty);
        }

        [Test]
        public void PayVetSpendsCoinsAndCures()
        {
            var state = Gooby.Core.Save.SaveSchema.DefaultState(1000);
            var gooby = (JObject)state["gooby"];
            gooby["health"] = new JObject { ["state"] = "sick", ["junkScore"] = 9 };
            var broke = Health.PayVet(state, "cure", 2000);
            Assert.That(broke.Ok, Is.False, "100 starting coins cannot pay the 120 c cure");
            Assert.That(broke.Reason, Is.EqualTo("coins"));
            state["economy"]["coins"] = 500;
            var res = Health.PayVet(state, "cure", 2000);
            Assert.That(res.Ok, Is.True);
            Assert.That(res.Total, Is.EqualTo(Health.VetCurePrice));
            Assert.That(J.Int(state["economy"]["coins"]), Is.EqualTo(500 - Health.VetCurePrice));
            Assert.That(J.Str(gooby["health"]["state"]), Is.EqualTo("healthy"));
            Assert.That(J.Num(gooby["stats"]["hunger"]), Is.EqualTo(90.0).Within(Eps));
            Assert.That(J.Int(state["achievements"]["counters"]["cures"]), Is.EqualTo(1));
            var again = Health.PayVet(state, "cure", 3000);
            Assert.That(again.Ok, Is.False);
            Assert.That(again.Reason, Is.EqualTo("healthy"));
        }
    }
}
