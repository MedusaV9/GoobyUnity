using System.Linq;
using Gooby.Core.Net;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Arcade friends leaderboard against the real GOOBY-SERVER (arcade.js): Alice + Bob are friends, Cara is a stranger.
    /// Both friends submit carrotCatch scores, the leaderboard lists both best first, a lower resubmit keeps the best,
    /// Cara's score stays invisible to them (and theirs to her), /api/arcade/best names Bob as the top friend.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class ArcadeIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob, _cara;
        private Recorder _ra, _rb;

        [OneTimeSetUp]
        public void Setup()
        {
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            _cara = NewClient("Cara", "Krümel");
            _ra = new Recorder(_alice);
            _rb = new Recorder(_bob);
            ConnectAll(_alice, _bob, _cara);
            MakeFriends(_alice, _ra, _bob, _rb);
        }

        private ArcadeSubmitResult Submit(SocialClient c, string game, int score, int stars)
        {
            ArcadeSubmitResult res = null;
            c.SubmitArcadeScore(game, score, stars, r => res = r);
            PumpUntil(() => res != null, "POST /api/arcade/score " + c.PlayerName);
            return res;
        }

        private ArcadeBoard Board(SocialClient c, string game)
        {
            ArcadeBoard board = null;
            c.FetchArcadeLeaderboard(game, b => board = b);
            PumpUntil(() => board != null, "GET /api/arcade/leaderboard " + c.PlayerName);
            return board;
        }

        [Test, Order(1)]
        public void A_FriendsSeeEachOtherSortedBestFirst()
        {
            var a = Submit(_alice, "carrotCatch", 900, 2);
            Assert.That(a.Ok, Is.True, a.Code);
            Assert.That(a.Improved, Is.True);
            Assert.That(a.Best, Is.EqualTo(900));
            var b = Submit(_bob, "carrotCatch", 1240, 3);
            Assert.That(b.Ok, Is.True, b.Code);

            var board = Board(_alice, "carrotCatch");
            Assert.That(board.Ok, Is.True, board.Code);
            Assert.That(board.Me, Is.EqualTo(_alice.FriendCode));
            Assert.That(board.Entries.Select(e => e.Code), Is.EqualTo(new[] { _bob.FriendCode, _alice.FriendCode }));
            Assert.That(board.Entries.Select(e => e.Score), Is.EqualTo(new[] { 1240, 900 }));
            Assert.That(board.Entries[0].Name, Is.EqualTo("Bob"));
            Assert.That(board.Entries[0].Gooby, Is.EqualTo("Bobby"));
            Assert.That(board.Entries[0].Stars, Is.EqualTo(3));
            Assert.That(board.Rank, Is.EqualTo(2));
            Assert.That(board.HasFriends, Is.True);
        }

        [Test, Order(2)]
        public void B_LowerResubmitKeepsTheBest()
        {
            var low = Submit(_alice, "carrotCatch", 300, 3);
            Assert.That(low.Ok, Is.True, low.Code);
            Assert.That(low.Improved, Is.False);
            Assert.That(low.Best, Is.EqualTo(900));
            var board = Board(_bob, "carrotCatch");
            Assert.That(board.Entries.Select(e => e.Score), Is.EqualTo(new[] { 1240, 900 }));
            Assert.That(board.Rank, Is.EqualTo(1));
        }

        [Test, Order(3)]
        public void C_StrangersStayInvisible()
        {
            var c = Submit(_cara, "carrotCatch", 99_999, 3);
            Assert.That(c.Ok, Is.True, c.Code);
            var alice = Board(_alice, "carrotCatch");
            Assert.That(alice.Entries.Select(e => e.Code), Does.Not.Contain(_cara.FriendCode));
            Assert.That(alice.Entries.Count, Is.EqualTo(2));
            var cara = Board(_cara, "carrotCatch");
            Assert.That(cara.Entries.Select(e => e.Code), Is.EqualTo(new[] { _cara.FriendCode }));
            Assert.That(cara.HasFriends, Is.False);
        }

        [Test, Order(4)]
        public void D_BestNamesTheTopFriendForTheArcadeCards()
        {
            ArcadeBestSet set = null;
            _alice.FetchArcadeBest(s => set = s);
            PumpUntil(() => set != null, "GET /api/arcade/best");
            Assert.That(set.Ok, Is.True, set.Code);
            var cc = set.Find("carrotCatch");
            Assert.That(cc, Is.Not.Null);
            Assert.That(cc.TopCode, Is.EqualTo(_bob.FriendCode));
            Assert.That(cc.TopName, Is.EqualTo("Bob"));
            Assert.That(cc.TopScore, Is.EqualTo(1240));
            Assert.That(cc.Mine, Is.EqualTo(900));
            Assert.That(cc.FriendLeads, Is.True);
            Assert.That(_alice.ArcadeBest.FriendTops, Is.EqualTo(1));

            var bad = Submit(_alice, "nopeGame", 5, 0);
            Assert.That(bad.Ok, Is.False);
            Assert.That(bad.Code, Is.EqualTo("BAD_GAME"));
        }
    }
}
