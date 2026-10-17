using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Net;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Schach against the real GOOBY-SERVER (boardgames.js, game "chess"): invite + accept with game:"chess", both sit
    /// down, White = the inviter, moves travel as SHOT {n, move} with auto-acked SHOT_RESULT {n}, a whole fool's mate to
    /// the end (the receiver of the mating move writes GAME_OVER {winner, reason:"checkmate"} — the server accepting the
    /// rematch afterwards proves it arrived), rematch with swapped colours, surrender and leaving. Own server per fixture
    /// (HELLO limit 5/min per IP).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class ChessIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private BoardClient _ba, _bb;
        private BoardRecorder _ra, _rb;
        private readonly List<string> _movedA = new List<string>(), _movedB = new List<string>();

        [OneTimeSetUp]
        public void Setup()
        {
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            var sa = new Recorder(_alice);
            var sb = new Recorder(_bob);
            _ba = new BoardClient(_alice);
            _bb = new BoardClient(_bob);
            _ra = new BoardRecorder(_ba);
            _rb = new BoardRecorder(_bb);
            _ba.OpponentMoved += m => _movedA.Add(m);
            _bb.OpponentMoved += m => _movedB.Add(m);
            ConnectAll(_alice, _bob);
            MakeFriends(_alice, sa, _bob, sb);
        }

        [Test, Order(1)]
        public void A_AChessInviteStartsAChessTableWithTheInviterAsWhite()
        {
            var invite = Await(cb => _ba.Invite(_bob.FriendCode, BoardClient.GameChess, cb), "BOARD_INVITE chess");
            Assert.That(invite.Ok, Is.True, invite.Code);
            Assert.That(_ba.IsInvitePending(_bob.FriendCode, BoardClient.GameChess), Is.True);
            Assert.That(_ba.OutgoingInviteGame, Is.EqualTo(BoardClient.GameChess));
            PumpUntil(() => _rb.Invites.Count == 1, "BOARD_INVITED at Bob");
            Assert.That(_rb.Invites[0].Game, Is.EqualTo(BoardClient.GameChess));
            Assert.That(_rb.Invites[0].From, Is.EqualTo(_alice.FriendCode));

            var accept = Await(cb => _bb.Accept(_alice.FriendCode, cb), "BOARD_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            Assert.That(accept.Type, Is.EqualTo("BOARD_START"));
            PumpUntil(() => _ba.HasGame && _bb.HasGame, "BOARD_START on both");
            Assert.That(_ra.Started, Is.EqualTo(1));
            Assert.That(_rb.Started, Is.EqualTo(1));
            Assert.That(_ba.IsChess && _bb.IsChess, Is.True);
            Assert.That(_ba.Game, Is.EqualTo("chess"));
            Assert.That(_bb.RoomId, Is.EqualTo(_ba.RoomId));
            Assert.That(_ba.MyColor, Is.EqualTo(ChessLogic.White), "the inviter plays White");
            Assert.That(_bb.MyColor, Is.EqualTo(ChessLogic.Black));
            Assert.That(_ba.Chess.ToFen(), Is.EqualTo(_bb.Chess.ToFen()));
            Assert.That(_ba.Chess.ToFen(), Does.StartWith("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -"));
            PumpUntil(() => _ba.PeerInRoom && _bb.PeerInRoom, "both at the table");
            Assert.That(_ba.MyTurn, Is.True);
            Assert.That(_bb.MyTurn, Is.False);
        }

        [Test, Order(2)]
        public void B_FoolsMateIsPlayedToTheEnd()
        {
            Assume.That(_ba.IsActive && _bb.IsActive && _ba.IsChess, "needs the table of A_");
            var a = _alice.FriendCode;
            var b = _bob.FriendCode;
            // local guards: wrong side, wrong colour, nonsense
            Assert.That(_bb.SendMove("e7e5"), Is.EqualTo(BoardClient.ErrNotYourTurn));
            Assert.That(_ba.SendMove("e2e5"), Is.EqualTo(BoardClient.ErrIllegalMove));
            Assert.That(_ba.SendMove("e7e5"), Is.EqualTo(BoardClient.ErrIllegalMove));

            var moves = new[] { "f2f3", "e7e5", "g2g4", "d8h4" };
            for (var i = 0; i < moves.Length; i++)
            {
                var mover = i % 2 == 0 ? _ba : _bb;
                var other = i % 2 == 0 ? _bb : _ba;
                var seen = i % 2 == 0 ? _movedB : _movedA;
                PumpUntil(() => mover.MyTurn, "turn for " + moves[i]);
                Assert.That(other.MyTurn, Is.False);
                Assert.That(mover.Turn.N, Is.EqualTo(i + 1), "move numbers count both players");
                Assert.That(mover.SendMove(moves[i]), Is.EqualTo(""), moves[i]);
                Assert.That(mover.MyTurn, Is.False, "our move waits for its ack");
                PumpUntil(() => seen.Count == i / 2 + 1, "OpponentMoved " + moves[i]);
                Assert.That(seen[i / 2], Is.EqualTo(moves[i]));
                PumpUntil(() => !mover.AwaitingAck, "SHOT_RESULT for " + moves[i]);
                Assert.That(mover.Chess.ToFen(), Is.EqualTo(other.Chess.ToFen()), "same position after " + moves[i]);
            }

            PumpUntil(() => _ba.Finished && _bb.Finished, "GAME_OVER on both");
            PumpFor(200); // the relayed GAME_OVER must not count twice
            Assert.That(_ba.Chess.Result(), Is.EqualTo(ChessLogic.ResultCheckmate));
            Assert.That(_bb.Chess.Result(), Is.EqualTo(ChessLogic.ResultCheckmate));
            Assert.That(_ba.Chess.InCheck(), Is.True);
            Assert.That(_bb.IWon, Is.True, "Black mates");
            Assert.That(_ba.IWon, Is.False);
            Assert.That(_ba.Winner, Is.EqualTo(b));
            Assert.That(_bb.Winner, Is.EqualTo(b));
            Assert.That(_ba.EndReason, Is.EqualTo(BoardEndReason.Checkmate));
            Assert.That(_bb.ResultId, Is.EqualTo("checkmate"));
            Assert.That(_ba.IsDraw, Is.False);
            Assert.That(_ra.Overs, Is.EqualTo(new[] { b + ":False:Checkmate" }));
            Assert.That(_rb.Overs, Is.EqualTo(new[] { b + ":True:Checkmate" }));
            Assert.That(_ra.Rejected, Is.Empty);
            Assert.That(_rb.Rejected, Is.Empty);
            Assert.That(_ba.MovesSent, Is.EqualTo(2));
            Assert.That(_bb.MovesSent, Is.EqualTo(2));
            Assert.That(_ba.Chess.Moves.Count, Is.EqualTo(4));
            Assert.That(_ba.SendMove("e2e4"), Is.EqualTo(BoardClient.ErrNoGame));
            Assert.That(_ba.OpponentCode, Is.EqualTo(b));
            Assert.That(_bb.OpponentCode, Is.EqualTo(a));
        }

        [Test, Order(3)]
        public void C_RematchSwapsColoursAndSurrenderEndsIt()
        {
            Assume.That(_ba.Finished && _bb.Finished, "needs the finished game of B_");
            var oldRoom = _ba.RoomId;
            // the server only allows a rematch after GAME_OVER reached its history (else GAME_RUNNING)
            var wait = Await(cb => _ba.RequestRematch(cb), "BOARD_REMATCH Alice");
            Assert.That(wait.Ok, Is.True, wait.Code);
            PumpUntil(() => _bb.RematchTheirs, "BOARD_REMATCH_WAIT at Bob");
            var start = Await(cb => _bb.RequestRematch(cb), "BOARD_REMATCH Bob");
            Assert.That(start.Type, Is.EqualTo("BOARD_START"));
            PumpUntil(() => _ba.RoomId == _bb.RoomId && _ba.RoomId != oldRoom && _ba.IsActive && _bb.IsActive, "fresh table on both");
            Assert.That(_ba.IsChess && _bb.IsChess, Is.True, "a rematch stays chess");
            Assert.That(_bb.MyColor, Is.EqualTo(ChessLogic.White), "colours swap: Bob plays White");
            Assert.That(_ba.MyColor, Is.EqualTo(ChessLogic.Black));
            Assert.That(_ba.Chess.Moves.Count, Is.EqualTo(0), "fresh board");
            PumpUntil(() => _ba.PeerInRoom && _bb.PeerInRoom, "both at the new table");

            PumpUntil(() => _bb.MyTurn, "Bob begins");
            Assert.That(_bb.SendMove("e2e4"), Is.EqualTo(""));
            PumpUntil(() => _ba.MyTurn, "Alice's answer");
            Assert.That(_ba.SendMove("e7e5"), Is.EqualTo(""));
            PumpUntil(() => _bb.MyTurn, "Bob's second move");
            Assert.That(_bb.Chess.ToFen(), Is.EqualTo(_ba.Chess.ToFen()));

            Assert.That(_ba.Surrender(), Is.True, "Alice gives up");
            Assert.That(_ba.Finished, Is.True);
            PumpUntil(() => _bb.Finished, "GAME_OVER (surrender) at Bob");
            Assert.That(_bb.IWon, Is.True);
            Assert.That(_bb.EndReason, Is.EqualTo(BoardEndReason.Surrender));
            Assert.That(_ba.Winner, Is.EqualTo(_bob.FriendCode));
            Assert.That(_bb.ResultId, Is.EqualTo("surrender"));

            Await(cb => _bb.Leave(cb), "ROOM_LEAVE Bob");
            Await(cb => _ba.Leave(cb), "ROOM_LEAVE Alice");
            Assert.That(_ba.HasGame || _bb.HasGame, Is.False);
            Assert.That(_ba.IsChess, Is.False, "a left table forgets the game");
        }
    }
}
