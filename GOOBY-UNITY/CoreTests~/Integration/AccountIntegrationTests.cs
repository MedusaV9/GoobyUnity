using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Net;
using Gooby.Core.Save;
using Gooby.Core.State;
using Gooby.Tests.Core;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Online codes and the account move against the real GOOBY-SERVER: a code made in the panel (codes.js) is redeemed
    /// once per device over REST and its reward runs through the game's effect path; a move code made in the panel
    /// (move.js) carries the old device's identity — friend code, friends — to a new device (MOVE_REDEEM → reconnect with
    /// the adopted identity), while the old device is signed off for good (4008 "MOVED", no retry with the void secret).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class AccountIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private HttpClient _panel;

        [OneTimeSetUp]
        public void Setup()
        {
            NetLog.OnError = m => TestContext.Progress.WriteLine(m);
            Gooby.Tests.Core.Fixtures.InstallContentSource();
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            var sa = new Recorder(_alice);
            var sb = new Recorder(_bob);
            ConnectAll(_alice, _bob);
            MakeFriends(_alice, sa, _bob, sb);
            _panel = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri("http://" + Server.Address + "/") };
            var login = _panel.PostAsync("panel/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["password"] = "test" })).GetAwaiter().GetResult();
            Assert.That((int)login.StatusCode, Is.EqualTo(303), "panel login");
        }

        [OneTimeTearDown]
        public void Panel() => _panel?.Dispose();

        private string PanelPost(string path, Dictionary<string, string> form)
        {
            var resp = _panel.PostAsync(path, new FormUrlEncodedContent(form)).GetAwaiter().GetResult();
            return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private CodeRedeemResult Redeem(SocialClient c, string code)
        {
            CodeRedeemResult result = null;
            c.RedeemOnlineCode(code, r => result = r);
            PumpUntil(() => result != null, "POST /api/codes/redeem " + code);
            return result;
        }

        [Test, Order(1)]
        public void A_APanelCodeIsRedeemedOncePerDeviceAndItsRewardApplies()
        {
            var html = PanelPost("panel/api/codes", new Dictionary<string, string> { ["code"] = "PANEL-500", ["reward"] = "{\"coins\":500,\"sticker\":\"ballBuddy\"}", ["maxUses"] = "5" });
            Assert.That(html, Does.Contain("PANEL-500"), "the panel made the code");
            var ok = Redeem(_alice, " panel-500 ");
            Assert.That(ok.Ok, Is.True, ok.Code);
            Assert.That(J.Int(ok.Reward["coins"]), Is.EqualTo(500));

            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Initialize("user/save_v5.json", 0);
            var coins = J.Int(store.State["economy"]["coins"]);
            var def = CodesEngine.ApplyServerReward(store, "panel-500", ok.Reward);
            Assert.That(J.Int(store.State["economy"]["coins"]), Is.EqualTo(coins + 500), "the reward books through the one money path");
            Assert.That(StickerRules.IsUnlocked(store.State, "ballBuddy"), Is.True);
            Assert.That(def.Id, Is.EqualTo("srv:PANEL-500"));

            Assert.That(Redeem(_alice, "PANEL-500").Code, Is.EqualTo("ALREADY_REDEEMED"), "once per device");
            Assert.That(Redeem(_bob, "PANEL-500").Ok, Is.True, "another device may");
            Assert.That(Redeem(_alice, "NOPE-123").Code, Is.EqualTo("UNKNOWN"));
        }

        [Test, Order(2)]
        public void B_AMoveCodeCarriesTheIdentityToANewDeviceAndSignsTheOldOneOff()
        {
            var neu = NewClient("Neu", "Neuling");
            ConnectAll(neu);
            var guestCode = neu.FriendCode;
            var aliceCode = _alice.FriendCode;
            Assert.That(guestCode, Is.Not.EqualTo(aliceCode));

            MoveRedeemResult bad = null;
            neu.RedeemMoveCode("kurz", r => bad = r);
            PumpUntil(() => bad != null, "local format check");
            Assert.That(bad.Code, Is.EqualTo("BAD_CODE"));
            MoveRedeemResult unknown = null;
            neu.RedeemMoveCode("ABCDEFGH", r => unknown = r);
            PumpUntil(() => unknown != null, "MOVE_REDEEM unknown");
            Assert.That(unknown.Code, Is.EqualTo("INVALID_CODE"));

            var oldDeviceId = _alice.Identity.DeviceId;
            var html = PanelPost("panel/api/movecode", new Dictionary<string, string> { ["deviceId"] = oldDeviceId });
            var m = Regex.Match(html, "<code>([A-HJ-NP-Z2-9]{8})</code>");
            Assert.That(m.Success, Is.True, "the panel made a move code:\n" + html.Substring(0, Math.Min(600, html.Length)));
            MoveRedeemResult moved = null;
            neu.RedeemMoveCode(m.Groups[1].Value.ToLowerInvariant(), r => moved = r);
            PumpUntil(() => moved != null, "MOVE_REDEEM");
            Assert.That(moved.Ok, Is.True, moved.Code);
            Assert.That(moved.FriendCode, Is.EqualTo(aliceCode));
            Assert.That(neu.Identity.FriendCode, Is.EqualTo(aliceCode));

            PumpUntil(() => neu.IsOnline && neu.FriendCode == aliceCode && neu.FindFriend(_bob.FriendCode) != null, "the new device reconnects as Alice with her friends", 15000);
            PumpUntil(() => !_alice.IsOnline && _alice.LastErrorCode == NetErrorCodes.Moved, "the old device is signed off (MOVED)");
            PumpFor(1500);
            Assert.That(_alice.IsOnline, Is.False, "no retry with the void secret");
            Assert.That(_bob.FindFriend(aliceCode)?.Online ?? false, Is.True, "Bob still sees Alice online — on the new device");
            Assert.That(_alice.Identity.DeviceId, Is.Not.EqualTo(oldDeviceId), "the old device starts over as a fresh guest");
            Assert.That(_alice.Identity.FriendCode, Is.EqualTo(""));
            Assert.That(_alice.Friends, Is.Empty, "the moved account's friends left with it");
            _alice.Connect();
            PumpUntil(() => _alice.IsOnline && _alice.FriendCode.StartsWith("GOOBY-") && _alice.FriendCode != aliceCode, "a manual connect makes the old device a new guest");

            var again = Redeem(neu, "PANEL-500");
            Assert.That(again.Code, Is.EqualTo("ALREADY_REDEEMED"), "the redemption belongs to the moved identity (deviceId)");
        }
    }
}
