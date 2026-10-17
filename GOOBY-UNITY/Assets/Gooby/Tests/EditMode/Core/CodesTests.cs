using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Gooby.Core.State;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_rest4_codes.gd (engine + apply path).</summary>
    public class CodesTests
    {
        private const long Now = 1_768_478_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            CodesEngine.IncludeDevCodes = false;
            CodesEngine.ResetCache();
        }

        [Test]
        public void NormalizeStripsWhitespaceAndCase()
        {
            Assert.That(CodesEngine.Normalize("  Ich LIE3B  Dich \n"), Is.EqualTo("ichlie3bdich"));
            Assert.That(CodesEngine.Normalize("update\tLiebe"), Is.EqualTo("updateliebe"));
            Assert.That(CodesEngine.Normalize(new JValue(42)), Is.EqualTo(""));
            Assert.That(CodesEngine.Normalize("   "), Is.EqualTo(""));
        }

        [Test]
        public void PackCodesResolveViaSha256AndDevCodesAreFiltered()
        {
            Assert.That(CodesEngine.Sha256Hex("goldigold"), Is.EqualTo("21c75081907547c670a3f0da8e857774aa893c5dcdc17c98333cfe48a0dddc11"), "pack hash of the built-in goldiGold secret");
            Assert.That(CodesEngine.CodeById("willkommen2026"), Is.Not.Null);
            Assert.That(CodesEngine.CodeById("dev_gooby"), Is.Null, "dev_only filtered by default");
            CodesEngine.IncludeDevCodes = true;
            CodesEngine.ResetCache();
            Assert.That(CodesEngine.CodeBySecret("gooby")?.Id, Is.EqualTo("dev_gooby"));
        }

        [Test]
        public void RedeemSucceedsOnceAndDoesNotCountAsAttempt()
        {
            var state = SaveSchema.DefaultState(Now);
            var attempts = new List<long>();
            var r = CodesEngine.Redeem(state, "Ich LIE3B Dich", Now, attempts);
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Code.Id, Is.EqualTo("herzGooby"));
            Assert.That(((JObject)state["codes"]["redeemed"]).ContainsKey("herzGooby"), Is.True);
            var again = CodesEngine.Redeem(state, "ichlie3bdich", Now + 1, attempts);
            Assert.That(again.Ok, Is.False);
            Assert.That(again.Reason, Is.EqualTo("already"));
            Assert.That(attempts.Count, Is.EqualTo(0));
        }

        [Test]
        public void UnknownLocksAfterFiveAndWindowRolls()
        {
            var state = SaveSchema.DefaultState(Now);
            var attempts = new List<long>();
            for (var i = 0; i < 4; i++) Assert.That(CodesEngine.Redeem(state, "quatsch" + i, Now + i, attempts).Reason, Is.EqualTo("unknown"));
            Assert.That(J.Long(state["codes"]["lockUntil"]), Is.EqualTo(0));
            Assert.That(CodesEngine.Redeem(state, "quatsch5", Now + 5, attempts).Reason, Is.EqualTo("unknown"));
            Assert.That(J.Long(state["codes"]["lockUntil"]), Is.GreaterThan(Now));
            Assert.That(CodesEngine.Redeem(state, "ichlie3bdich", Now + 6, attempts).Reason, Is.EqualTo("locked"));
            Assert.That(CodesEngine.LockRemainingMs(state, Now + 6), Is.GreaterThan(0));
            Assert.That(CodesEngine.Redeem(state, "ichlie3bdich", Now + 5 + CodesEngine.LockSec * 1000L + 1, attempts).Ok, Is.True);
            var s2 = SaveSchema.DefaultState(Now);
            var a2 = new List<long>();
            for (var i = 0; i < 4; i++) CodesEngine.Redeem(s2, "x" + i, Now + i, a2);
            CodesEngine.Redeem(s2, "x9", Now + CodesEngine.LockWindowSec * 1000L + 10, a2);
            Assert.That(J.Long(s2["codes"]["lockUntil"]), Is.EqualTo(0), "window rolls");
            Assert.That(a2.Count, Is.EqualTo(1));
        }

        [Test]
        public void BuffAndLockClocks()
        {
            var state = SaveSchema.DefaultState(Now);
            Assert.That(CodesEngine.IsDoubleCoinsActive(state, Now), Is.False);
            state["codes"]["buffs"]["doubleCoinsUntil"] = Now + 60000;
            Assert.That(CodesEngine.IsDoubleCoinsActive(state, Now), Is.True);
            Assert.That(CodesEngine.RemainingMs(state, Now), Is.EqualTo(60000));
            Assert.That(CodesEngine.IsDoubleCoinsActive(state, Now + 60001), Is.False);
            state["codes"]["lockUntil"] = Now + 30000;
            Assert.That(CodesEngine.LockRemainingMs(state, Now), Is.EqualTo(30000));
            Assert.That(CodesEngine.LockRemainingMs(state, Now + 30001), Is.EqualTo(0));
        }

        [Test]
        public void ApplyPathBooksCoinsBuffFlagAndCounter()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(Now);
            store.Initialize("user/save_v5.json", 0);
            var attempts = new List<long>();
            Assert.That(CodesEngine.RedeemAndApply(store, "   ", attempts).Reason, Is.EqualTo("empty"));
            var r = CodesEngine.RedeemAndApply(store, "Ich LIE3B Dich", attempts);
            Assert.That(r.Ok, Is.True);
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(150));
            Assert.That(J.Int(store.State["achievements"]["counters"]["codesRedeemed"]), Is.EqualTo(1));
            Assert.That(CodesEngine.RedeemAndApply(store, "updateliebe", attempts).Ok, Is.True);
            Assert.That(CodesEngine.IsDoubleCoinsActive(store.State, Now), Is.True);
            Assert.That(CodesEngine.RemainingMs(store.State, Now), Is.EqualTo(10 * 60_000L));
            Assert.That(CodesEngine.RedeemAndApply(store, "goldigold", attempts).Ok, Is.True);
            Assert.That(CodesEngine.IsFlagSet(store.State, "gvz.goldi"), Is.True);
            Assert.That(CodesEngine.RedeemedEntries(store.State).Count, Is.EqualTo(3));
            Assert.That(CodesEngine.RedeemAndApply(store, "goldigold", attempts).Reason, Is.EqualTo("already"));
        }

        [Test]
        public void AServerRewardRunsThroughTheSameEffectPathAndLandsInTheHistory()
        {
            Fixtures.InstallContentSource();
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(Now);
            store.Initialize("user/save_v5.json", 0);
            var coins = J.Int(store.State["economy"]["coins"]);
            var reward = new JObject { ["coins"] = 500, ["sticker"] = "ballBuddy", ["buff"] = "doubleCoins", ["minutes"] = 5 };
            var def = CodesEngine.ApplyServerReward(store, " panel-500 ", reward);
            Assert.That(def.Id, Is.EqualTo("srv:PANEL-500"));
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(coins + 500));
            Assert.That(StickerRules.IsUnlocked(store.State, "ballBuddy"), Is.True, "a sticker the catalog knows is granted");
            Assert.That(CodesEngine.RemainingMs(store.State, Now), Is.EqualTo(5 * 60_000L));
            Assert.That(J.Int(store.State["achievements"]["counters"]["codesRedeemed"]), Is.EqualTo(1));
            Assert.That(CodesEngine.RedeemedEntries(store.State)[0].id, Is.EqualTo("srv:PANEL-500"));
            CodesEngine.ApplyServerReward(store, "x", new JObject { ["sticker"] = "gibtsNicht" });
            Assert.That(StickerRules.IsUnlocked(store.State, "gibtsNicht"), Is.False, "unknown stickers are not invented");
        }
    }
}
