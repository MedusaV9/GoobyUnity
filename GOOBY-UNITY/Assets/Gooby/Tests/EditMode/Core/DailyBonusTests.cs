using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_rest1_daily.gd.</summary>
    public class DailyBonusTests
    {
        private const long NowMs = 1_768_478_400_000L;

        [Test]
        public void PrevDayCrossesMonthAndYearBoundaries()
        {
            Assert.That(DailyBonus.PrevDay("2026-07-27"), Is.EqualTo("2026-07-26"));
            Assert.That(DailyBonus.PrevDay("2026-03-01"), Is.EqualTo("2026-02-28"));
            Assert.That(DailyBonus.PrevDay("2024-03-01"), Is.EqualTo("2024-02-29"));
            Assert.That(DailyBonus.PrevDay("2026-01-01"), Is.EqualTo("2025-12-31"));
            Assert.That(DailyBonus.PrevDay("kaputt"), Is.EqualTo(""));
        }

        [Test]
        public void ClaimableOncePerDay()
        {
            Assert.That(DailyBonus.IsClaimable(new JObject(), "2026-07-27"), Is.True);
            Assert.That(DailyBonus.IsClaimable(new JObject { ["lastClaimDay"] = "2026-07-26" }, "2026-07-27"), Is.True);
            Assert.That(DailyBonus.IsClaimable(new JObject { ["lastClaimDay"] = "2026-07-27" }, "2026-07-27"), Is.False);
        }

        [Test]
        public void NextStreakCountsGraceAndBreaks()
        {
            const string today = "2026-07-27";
            Assert.That(DailyBonus.NextStreak(new JObject(), today), Is.EqualTo((1, false)));
            Assert.That(DailyBonus.NextStreak(new JObject { ["lastClaimDay"] = "2026-07-26", ["streak"] = 3 }, today), Is.EqualTo((4, false)));
            Assert.That(DailyBonus.NextStreak(new JObject { ["lastClaimDay"] = "2026-07-25", ["streak"] = 3 }, today), Is.EqualTo((4, true)));
            Assert.That(DailyBonus.NextStreak(new JObject { ["lastClaimDay"] = "2026-07-24", ["streak"] = 9 }, today), Is.EqualTo((1, false)));
            Assert.That(DailyBonus.NextStreak(new JObject { ["lastClaimDay"] = "2026-07-26", ["streak"] = 0 }, today), Is.EqualTo((1, false)));
        }

        [Test]
        public void RewardTableIsWebVerbatim()
        {
            var expected = new[] { 20, 30, 40, 50, 60, 80, 100 };
            for (var i = 0; i < expected.Length; i++)
            {
                var r = DailyBonus.RewardForStreak(i + 1);
                Assert.That(r.coins, Is.EqualTo(expected[i]));
                Assert.That(r.includesFood, Is.EqualTo(i + 1 >= 7));
            }
            Assert.That(DailyBonus.RewardForStreak(12), Is.EqualTo((100, true)));
        }

        [Test]
        public void ClaimIsOncePerDayAndPaysThroughEconomy()
        {
            var state = SaveSchema.DefaultState(NowMs);
            var start = J.Int(state["economy"]["coins"]);
            var res = DailyBonus.Claim(state, "2026-07-27");
            Assert.That(res.Ok, Is.True);
            Assert.That(res.StreakDay, Is.EqualTo(1));
            Assert.That(res.Coins, Is.EqualTo(20));
            Assert.That(J.Int(state["economy"]["coins"]), Is.EqualTo(start + 20));
            Assert.That(J.Str(state["daily"]["lastClaimDay"]), Is.EqualTo("2026-07-27"));
            Assert.That(DailyBonus.Claim(state, "2026-07-27").Ok, Is.False);
            Assert.That(J.Int(state["economy"]["coins"]), Is.EqualTo(start + 20));
        }

        [Test]
        public void ClaimAcrossDateJumpsWithGraceAndBreak()
        {
            var state = SaveSchema.DefaultState(NowMs);
            Assert.That(DailyBonus.Claim(state, "2026-07-20").Ok, Is.True);
            var day2 = DailyBonus.Claim(state, "2026-07-21");
            Assert.That(day2.StreakDay, Is.EqualTo(2));
            Assert.That(day2.Coins, Is.EqualTo(30));
            Assert.That(day2.Grace, Is.False);
            var day3 = DailyBonus.Claim(state, "2026-07-23");
            Assert.That(day3.StreakDay, Is.EqualTo(3));
            Assert.That(day3.Grace, Is.True);
            var reset = DailyBonus.Claim(state, "2026-07-26");
            Assert.That(reset.StreakDay, Is.EqualTo(1));
            Assert.That(reset.Grace, Is.False);
        }

        [Test]
        public void FromDay7ASnackLandsInTheInventory()
        {
            var state = SaveSchema.DefaultState(NowMs);
            state["daily"] = new JObject { ["lastClaimDay"] = "2026-07-26", ["streak"] = 6 };
            var expectedFood = FoodCatalog.Foods.Keys.OrderBy(k => k, System.StringComparer.Ordinal).First();
            var before = J.Int(state["inventory"]["food"][expectedFood]);
            var res = DailyBonus.Claim(state, "2026-07-27", 0.0);
            Assert.That(res.StreakDay, Is.EqualTo(7));
            Assert.That(res.Coins, Is.EqualTo(100));
            Assert.That(res.FoodId, Is.EqualTo(expectedFood));
            Assert.That(J.Int(state["inventory"]["food"][expectedFood]), Is.EqualTo(before + 1));
        }

        [Test]
        public void PickBonusFoodClampsTheRoll()
        {
            var ids = FoodCatalog.Foods.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();
            Assert.That(DailyBonus.PickBonusFood(0.0), Is.EqualTo(ids[0]));
            Assert.That(DailyBonus.PickBonusFood(0.999), Is.EqualTo(ids[ids.Count - 1]));
            Assert.That(DailyBonus.PickBonusFood(1.5), Is.EqualTo(ids[ids.Count - 1]));
        }
    }
}
