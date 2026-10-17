using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Net;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Ranch friends leaderboards against the real GOOBY-SERVER (ranchmp.js REST): Alice + Bob are friends, Cara is a
    /// stranger. Grass-track times sort smallest first, a slower resubmit keeps the best, tournament points sort largest
    /// first and carry Bob's ghost, Alice downloads it as a valid G5 run, Cara is refused (NOT_FRIENDS) and stays off
    /// their boards; implausible values never reach the server.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class RanchBoardIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob, _cara;

        [OneTimeSetUp]
        public void Setup()
        {
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            _cara = NewClient("Cara", "Krümel");
            var ra = new Recorder(_alice);
            var rb = new Recorder(_bob);
            ConnectAll(_alice, _bob, _cara);
            MakeFriends(_alice, ra, _bob, rb);
        }

        private RanchScoreResult Score(SocialClient c, string kurs, int wert)
        {
            RanchScoreResult res = null;
            c.SubmitRanchScore(kurs, wert, r => res = r);
            PumpUntil(() => res != null, "POST /api/rmp/score " + c.PlayerName);
            return res;
        }

        private RanchBoard Board(SocialClient c, string kurs)
        {
            RanchBoard board = null;
            c.FetchRanchLeaderboard(kurs, b => board = b);
            PumpUntil(() => board != null, "GET /api/rmp/leaderboard " + c.PlayerName);
            return board;
        }

        private static string GhostB64()
        {
            var rec = RanchCompGhost.NeuerRecorder("springen", new V2(3f, -4f));
            for (var i = 0; i < 60; i++) RanchCompGhost.Tick(rec, 0.1, new V2(3f + i * 0.7f, -4f + i * 0.3f), 0.0, 0.9, "galopp");
            return RanchCompGhost.ToB64(rec);
        }

        [Test, Order(1)]
        public void A_GrassTrackTimesSortFastestFirstAndKeepTheBest()
        {
            Assert.That(Score(_alice, "grasbahn", 41_000).Ok, Is.True);
            var bob = Score(_bob, "grasbahn", 38_900);
            Assert.That((bob.Ok, bob.Ranked, bob.Improved, bob.Best), Is.EqualTo((true, true, true, 38_900)));
            var slower = Score(_alice, "grasbahn", 45_000);
            Assert.That((slower.Ok, slower.Improved, slower.Best), Is.EqualTo((true, false, 41_000)), "the server keeps the best time");

            var board = Board(_alice, "grasbahn");
            Assert.That(board.Ok, Is.True, board.Code);
            Assert.That(board.Richtung, Is.EqualTo(RanchBoards.Ab));
            Assert.That(board.Me, Is.EqualTo(_alice.FriendCode));
            Assert.That(board.Entries.Select(e => e.Code), Is.EqualTo(new[] { _bob.FriendCode, _alice.FriendCode }));
            Assert.That(board.Entries.Select(e => e.Wert), Is.EqualTo(new[] { 38_900, 41_000 }));
            Assert.That(board.Entries[0].Name, Is.EqualTo("Bob"));
            Assert.That(board.Entries[0].Gooby, Is.EqualTo("Bobby"));
            Assert.That(board.Rank, Is.EqualTo(2));
        }

        [Test, Order(2)]
        public void B_TournamentPointsCarryBobsGhostToAlice()
        {
            Assert.That(Score(_alice, "rw5_springen", 70).Ok, Is.True);
            Assert.That(Score(_bob, "rw5_springen", 92).Ok, Is.True);
            var b64 = GhostB64();
            RanchScoreResult put = null;
            _bob.UploadRanchGhost("rw5_springen", 92, b64, r => put = r);
            PumpUntil(() => put != null, "PUT /api/rmp/ghost");
            Assert.That((put.Ok, put.Improved), Is.EqualTo((true, true)), put.Code);

            var board = Board(_alice, "rw5_springen");
            Assert.That(board.Richtung, Is.EqualTo(RanchBoards.Auf));
            Assert.That(board.Entries.Select(e => e.Wert), Is.EqualTo(new[] { 92, 70 }), "most points first");
            Assert.That(board.Entries[0].HatGhost, Is.True);
            Assert.That(board.Entries[1].HatGhost, Is.False);

            RanchGhostReply ghost = null;
            _alice.FetchRanchGhost("rw5_springen", _bob.FriendCode, g => ghost = g);
            PumpUntil(() => ghost != null, "GET /api/rmp/ghost (friend)");
            Assert.That(ghost.Ok, Is.True, ghost.Code);
            Assert.That(ghost.Wert, Is.EqualTo(92));
            Assert.That(ghost.B64, Is.EqualTo(b64));
            var run = RanchCompGhost.FromB64(ghost.B64);
            Assert.That(run.Ok, Is.True);
            Assert.That(run.Disziplin, Is.EqualTo("springen"));
            Assert.That(run.Samples.Count, Is.EqualTo(60));
        }

        [Test, Order(3)]
        public void C_StrangersAreRefusedAndStayOffTheBoards()
        {
            Assert.That(Score(_cara, "grasbahn", 30_000).Ok, Is.True);
            RanchGhostReply ghost = null;
            _cara.FetchRanchGhost("rw5_springen", _bob.FriendCode, g => ghost = g);
            PumpUntil(() => ghost != null, "GET /api/rmp/ghost (stranger)");
            Assert.That(ghost.Ok, Is.False);
            Assert.That(ghost.Code, Is.EqualTo("NOT_FRIENDS"));
            var alice = Board(_alice, "grasbahn");
            Assert.That(alice.Entries.Select(e => e.Code), Has.No.Member(_cara.FriendCode));
            var cara = Board(_cara, "grasbahn");
            Assert.That(cara.Entries.Select(e => e.Code), Is.EqualTo(new[] { _cara.FriendCode }));
            Assert.That(cara.HasFriends, Is.False);
        }

        [Test, Order(4)]
        public void D_ImplausibleValuesNeverLeaveTheClient()
        {
            var tooFast = Score(_alice, "grasbahn", 900);
            Assert.That(tooFast.Code, Is.EqualTo("BAD_ZEIT"));
            var noBoard = Score(_alice, "weide", 40_000);
            Assert.That(noBoard.Code, Is.EqualTo("BAD_KURS"));
            Assert.That(Board(_alice, "grasbahn").Entries.First(e => e.Code == _alice.FriendCode).Wert, Is.EqualTo(41_000));
        }
    }
}
