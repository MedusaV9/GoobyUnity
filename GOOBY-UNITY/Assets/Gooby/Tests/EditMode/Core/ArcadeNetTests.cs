using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Arcade friends leaderboard (arcade.js): parsing of leaderboard / best / submit replies and the REST calls of
    /// SocialClient (POST /api/arcade/score, GET /api/arcade/leaderboard/:game, GET /api/arcade/best) against the fake
    /// transport + fake HTTP — offline nothing is sent or queued.
    /// </summary>
    public class ArcadeNetTests
    {
        private string _dir;
        private long _now;
        private List<FakeNetTransport> _fakes;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gooby-arcade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _now = 1_000_000;
            _fakes = new List<FakeNetTransport>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { /* best effort */ }
        }

        private FakeNetTransport Fake => _fakes.Last();

        private SocialClient NewSocial(out FakeNetHttp http)
        {
            http = new FakeNetHttp();
            var client = new SocialClient(Path.Combine(_dir, "social"), () =>
            {
                var f = new FakeNetTransport();
                _fakes.Add(f);
                return f;
            }, () => _now, http);
            client.Configure(NetAddress.Parse("192.168.0.10"), "Anna", "Flausch", "");
            return client;
        }

        private void GoOnline(SocialClient c)
        {
            c.Connect();
            Fake.Open();
            c.Pump();
            var welcome = new JObject
            {
                ["friendCode"] = "GOOBY-AB23",
                ["serverTime"] = 123,
                ["heartbeatSec"] = 20,
                ["friendRequests"] = new JArray(),
                ["palPending"] = new JArray(),
                ["pendingEvents"] = new JArray(),
                ["mailUnread"] = 0,
                ["instantUnseen"] = 0,
            };
            Fake.Receive("WELCOME", welcome, (long)Fake.LastOfType("HELLO")["seq"]);
            c.Pump();
            Assert.That(c.Status, Is.EqualTo(NetStatus.Online));
        }

        private static JObject Entry(string code, string name, int score, int stars = 0, long at = 5000) => new JObject
        {
            ["code"] = code, ["name"] = name, ["gooby"] = name + "i", ["score"] = score, ["stars"] = stars, ["at"] = at,
        };

        private static JObject BoardJson(string me, params JObject[] entries) => new JObject
        {
            ["ok"] = true, ["game"] = "carrotCatch", ["me"] = me, ["rank"] = 0, ["entries"] = new JArray(entries),
        };

        // ------------------------------------------------------------------ parsing

        [Test]
        public void BoardParsesSortsAndRanks()
        {
            // the server sorts already; the client re-sorts defensively (score desc, earlier first) and drops junk
            var b = ArcadeBoard.FromJson(BoardJson("GOOBY-AB23",
                Entry("GOOBY-AB23", "Anna", 900, 2, 3000),
                Entry("GOOBY-BOB2", "Bob", 1240, 3),
                Entry("GOOBY-CARA", "Cara", 900, 1, 1000),
                new JObject { ["name"] = "no code" },
                Entry("GOOBY-BOB2", "Bob again", 1)));
            Assert.That(b.Ok, Is.True);
            Assert.That(b.Game, Is.EqualTo("carrotCatch"));
            Assert.That(b.Entries.Select(e => e.Code), Is.EqualTo(new[] { "GOOBY-BOB2", "GOOBY-CARA", "GOOBY-AB23" }));
            Assert.That(b.Rank, Is.EqualTo(3));
            Assert.That(b.HasFriends, Is.True);
            Assert.That(b.Mine.Score, Is.EqualTo(900));
            Assert.That(b.Mine.Stars, Is.EqualTo(2));
            Assert.That(b.Entries[0].Gooby, Is.EqualTo("Bobi"));
            Assert.That(b.Top(2).Select(e => e.Name), Is.EqualTo(new[] { "Bob", "Cara" }));
            Assert.That(b.Top(10).Count, Is.EqualTo(3));
        }

        [Test]
        public void BoardAloneHasNoFriendsAndFailsCarryTheCode()
        {
            var alone = ArcadeBoard.FromJson(BoardJson("GOOBY-AB23", Entry("GOOBY-AB23", "Anna", 5)));
            Assert.That(alone.HasFriends, Is.False);
            Assert.That(alone.Rank, Is.EqualTo(1));
            var empty = ArcadeBoard.FromJson(BoardJson("GOOBY-AB23"));
            Assert.That(empty.Rank, Is.EqualTo(0));
            Assert.That(empty.Mine, Is.Null);
            var fail = ArcadeBoard.FromJson(JObject.Parse("{\"ok\":false,\"code\":\"BAD_GAME\"}"));
            Assert.That(fail.Ok, Is.False);
            Assert.That(fail.Code, Is.EqualTo("BAD_GAME"));
            Assert.That(ArcadeBoard.FromJson(null).Code, Is.EqualTo("BAD_REPLY"));
        }

        [Test]
        public void BestParsesTopMineAndFriendLead()
        {
            var set = ArcadeBestSet.FromJson(JObject.Parse(@"{""ok"":true,""me"":""GOOBY-AB23"",""games"":{
                ""carrotCatch"":{""top"":{""code"":""GOOBY-BOB2"",""name"":""Bob"",""score"":1240},""mine"":900},
                ""bubblePop"":{""top"":null,""mine"":50},
                ""runner"":{""top"":{""code"":""GOOBY-BOB2"",""name"":""Bob"",""score"":7},""mine"":null},
                ""miniGolf"":{""top"":{""code"":""GOOBY-BOB2"",""name"":""Bob"",""score"":30},""mine"":30},
                ""bad-id"":{""top"":{""code"":""GOOBY-BOB2"",""name"":""Bob"",""score"":1},""mine"":null}}}"));
            Assert.That(set.Ok, Is.True);
            Assert.That(set.Me, Is.EqualTo("GOOBY-AB23"));
            Assert.That(set.Games.Keys, Is.EquivalentTo(new[] { "carrotCatch", "bubblePop", "runner", "miniGolf" }));
            Assert.That(set.FriendTops, Is.EqualTo(3));
            var cc = set.Find("carrotCatch");
            Assert.That(cc.TopName, Is.EqualTo("Bob"));
            Assert.That(cc.TopScore, Is.EqualTo(1240));
            Assert.That(cc.Mine, Is.EqualTo(900));
            Assert.That(cc.FriendLeads, Is.True);
            Assert.That(set.Find("bubblePop").HasTop, Is.False);
            Assert.That(set.Find("bubblePop").FriendLeads, Is.False);
            Assert.That(set.Find("runner").Mine, Is.EqualTo(-1));
            Assert.That(set.Find("runner").FriendLeads, Is.True, "no own score: the friend leads");
            Assert.That(set.Find("miniGolf").FriendLeads, Is.False, "a tie is no friend lead");
            Assert.That(set.Find("nope"), Is.Null);
        }

        [Test]
        public void GameBestFromBoardTakesTheFirstFriend()
        {
            var b = ArcadeBoard.FromJson(BoardJson("GOOBY-AB23",
                Entry("GOOBY-AB23", "Anna", 2000), Entry("GOOBY-BOB2", "Bob", 1240), Entry("GOOBY-CARA", "Cara", 800)));
            var g = ArcadeGameBest.FromBoard(b);
            Assert.That(g.Game, Is.EqualTo("carrotCatch"));
            Assert.That(g.TopCode, Is.EqualTo("GOOBY-BOB2"));
            Assert.That(g.Mine, Is.EqualTo(2000));
            Assert.That(g.FriendLeads, Is.False);
            Assert.That(ArcadeGameBest.FromBoard(ArcadeBoard.Fail("OFFLINE")), Is.Null);
        }

        [Test]
        public void SubmitResultParses()
        {
            var r = ArcadeSubmitResult.FromJson(JObject.Parse("{\"ok\":true,\"game\":\"carrotCatch\",\"best\":800,\"stars\":2,\"improved\":true}"));
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Best, Is.EqualTo(800));
            Assert.That(r.Stars, Is.EqualTo(2));
            Assert.That(r.Improved, Is.True);
            Assert.That(ArcadeSubmitResult.FromJson(JObject.Parse("{\"ok\":false,\"code\":\"RATE_LIMIT\"}")).Code, Is.EqualTo("RATE_LIMIT"));
            Assert.That(ArcadeScores.GameIdOk("carrotCatch"), Is.True);
            Assert.That(ArcadeScores.GameIdOk("x"), Is.False);
            Assert.That(ArcadeScores.GameIdOk("carrot-catch"), Is.False);
            Assert.That(ArcadeScores.GameIdOk(new string('a', 33)), Is.False);
            Assert.That(ArcadeScores.ClampScore(-5), Is.EqualTo(0));
            Assert.That(ArcadeScores.ClampScore(int.MaxValue), Is.EqualTo(ArcadeScores.ScoreMax));
            Assert.That(ArcadeScores.ClampStars(7), Is.EqualTo(3));
        }

        // ------------------------------------------------------------------ REST calls

        [Test]
        public void OfflineNothingIsSentOrQueued()
        {
            var c = NewSocial(out var http);
            ArcadeSubmitResult sub = null;
            ArcadeBoard board = null;
            ArcadeBestSet best = null;
            c.SubmitArcadeScore("carrotCatch", 500, 2, r => sub = r);
            c.FetchArcadeLeaderboard("carrotCatch", b => board = b);
            c.FetchArcadeBest(s => best = s);
            c.Pump();
            Assert.That(http.Calls, Is.Empty);
            Assert.That(sub.Code, Is.EqualTo(NetErrorCodes.Offline));
            Assert.That(board.Code, Is.EqualTo(NetErrorCodes.Offline));
            Assert.That(best.Code, Is.EqualTo(NetErrorCodes.Offline));
            Assert.That(c.OutboxCount, Is.EqualTo(0));
            // coming online later does not replay anything
            GoOnline(c);
            c.Pump();
            Assert.That(http.Calls, Is.Empty);
        }

        [Test]
        public void SubmitPostsClampedScoreAndPatchesTheCache()
        {
            var c = NewSocial(out var http);
            GoOnline(c);
            ArcadeSubmitResult bad = null;
            c.SubmitArcadeScore("carrot-catch", 5, 1, r => bad = r);
            Assert.That(bad.Code, Is.EqualTo("BAD_GAME"));
            Assert.That(http.Calls, Is.Empty);

            ArcadeSubmitResult res = null;
            c.SubmitArcadeScore("carrotCatch", 20_000_000, 9, r => res = r);
            Assert.That(http.Calls.Count, Is.EqualTo(1));
            var call = http.Calls[0];
            Assert.That(call.Method, Is.EqualTo("POST"));
            Assert.That(call.Url, Does.EndWith("/api/arcade/score"));
            Assert.That(call.Bearer, Is.EqualTo(c.Identity.Bearer));
            var body = JObject.Parse(call.Body);
            Assert.That((string)body["game"], Is.EqualTo("carrotCatch"));
            Assert.That((int)body["score"], Is.EqualTo(ArcadeScores.ScoreMax));
            Assert.That((int)body["stars"], Is.EqualTo(3));
            Assert.That(res, Is.Null, "the reply waits for Pump");
            http.Respond(call, 200, "{\"ok\":true,\"game\":\"carrotCatch\",\"best\":1500,\"stars\":3,\"improved\":true}");
            c.Pump();
            Assert.That(res.Ok, Is.True);
            Assert.That(res.Improved, Is.True);
            Assert.That(c.ArcadeBest.Find("carrotCatch").Mine, Is.EqualTo(1500));
            Assert.That(c.ArcadeBest.FriendTops, Is.EqualTo(0));

            ArcadeSubmitResult limited = null;
            c.SubmitArcadeScore("carrotCatch", 5, 0, r => limited = r);
            http.Respond(http.Calls[1], 429, "{\"ok\":false,\"code\":\"RATE_LIMIT\"}");
            c.Pump();
            Assert.That(limited.Ok, Is.False);
            Assert.That(limited.Code, Is.EqualTo("RATE_LIMIT"));

            ArcadeSubmitResult down = null;
            c.SubmitArcadeScore("carrotCatch", 5, 0, r => down = r);
            http.Respond(http.Calls[2], 0, "timeout");
            c.Pump();
            Assert.That(down.Code, Is.EqualTo(NetErrorCodes.NoConnection));
        }

        [Test]
        public void LeaderboardAndBestFillTheCache()
        {
            var c = NewSocial(out var http);
            GoOnline(c);
            ArcadeBestSet best = null;
            c.FetchArcadeBest(s => best = s);
            Assert.That(http.Calls[0].Method, Is.EqualTo("GET"));
            Assert.That(http.Calls[0].Url, Does.EndWith("/api/arcade/best"));
            http.Respond(http.Calls[0], 200, "{\"ok\":true,\"me\":\"GOOBY-AB23\",\"games\":{\"runner\":{\"top\":{\"code\":\"GOOBY-BOB2\",\"name\":\"Bob\",\"score\":7},\"mine\":null}}}");
            c.Pump();
            Assert.That(best.Ok, Is.True);
            Assert.That(c.ArcadeBestLoaded, Is.True);
            Assert.That(c.ArcadeBest.FriendTops, Is.EqualTo(1));

            ArcadeBoard board = null;
            c.FetchArcadeLeaderboard("carrotCatch", b => board = b);
            Assert.That(http.Calls[1].Url, Does.EndWith("/api/arcade/leaderboard/carrotCatch"));
            http.Respond(http.Calls[1], 200, BoardJson("GOOBY-AB23", Entry("GOOBY-BOB2", "Bob", 1240), Entry("GOOBY-AB23", "Anna", 900)).ToString());
            c.Pump();
            Assert.That(board.Ok, Is.True);
            Assert.That(board.Rank, Is.EqualTo(2));
            Assert.That(c.ArcadeBest.FriendTops, Is.EqualTo(2), "the leaderboard patched the card cache");
            Assert.That(c.ArcadeBest.Find("carrotCatch").FriendLeads, Is.True);

            ArcadeBoard missing = null;
            c.FetchArcadeLeaderboard("nopeGame", b => missing = b);
            http.Respond(http.Calls[2], 404, "{\"ok\":false,\"code\":\"BAD_GAME\"}");
            c.Pump();
            Assert.That(missing.Code, Is.EqualTo("BAD_GAME"));
            Assert.That(c.ArcadeBest.FriendTops, Is.EqualTo(2));
        }
    }
}
