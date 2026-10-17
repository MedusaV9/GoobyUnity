using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>Records every <see cref="BoardClient"/> event for assertions.</summary>
    public sealed class BoardRecorder
    {
        public readonly List<BoardInvite> Invites = new List<BoardInvite>();
        public readonly List<string> Declined = new List<string>();
        public readonly List<string> Results = new List<string>();
        public readonly List<string> Incoming = new List<string>();
        public readonly List<string> Rejected = new List<string>();
        public readonly List<string> Overs = new List<string>();
        public int Started, RematchAsks, PeerJoined;

        public BoardRecorder(BoardClient b)
        {
            b.InviteIncoming += i => Invites.Add(i);
            b.InviteDeclined += by => Declined.Add(by);
            b.GameStarted += () => Started++;
            b.PeerJoined += () => PeerJoined++;
            b.ShotResult += (c, hit, sunk, cells) => Results.Add(c.Ref + ":" + (sunk ? "sunk" : hit ? "hit" : "miss") + ":" + cells.Count);
            b.OpponentShot += (c, o) => Incoming.Add(c.Ref + ":" + (o.Sunk ? "sunk" : o.Hit ? "hit" : "miss"));
            b.SendRejected += (kind, code) => Rejected.Add(kind + ":" + code);
            b.GameOver += (w, won, reason) => Overs.Add(w + ":" + won + ":" + reason);
            b.RematchRequestedByOpponent += () => RematchAsks++;
        }
    }

    /// <summary>
    /// Schiffe versenken against the real GOOBY-SERVER (boardgames.js): befriend, decline, invite + accept, both sit
    /// down in the board: room, the server's turn ownership rejects wrong turns / move numbers, a whole game with
    /// alternating turns to GAME_OVER (the inviter knows the seed fleet of the other side, exactly like Godot's
    /// fleet_seed), rematch with swapped roles and a mid-game ROOM_LEAVE scored as forfeit. Own server per fixture
    /// (HELLO limit 5/min per IP); two clients = two HELLOs.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class BoardIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private BoardClient _ba, _bb;
        private BoardRecorder _ra, _rb;

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
            ConnectAll(_alice, _bob);
            MakeFriends(_alice, sa, _bob, sb);
        }

        [Test, Order(1)]
        public void A_DeclinedInviteReachesTheInviter()
        {
            var invite = Await(cb => _ba.Invite(_bob.FriendCode, cb), "BOARD_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            Assert.That(_ba.IsInvitePending(_bob.FriendCode), Is.True);
            PumpUntil(() => _rb.Invites.Count == 1, "BOARD_INVITED at Bob");
            Assert.That(_rb.Invites[0].From, Is.EqualTo(_alice.FriendCode));
            Assert.That(_rb.Invites[0].Name, Is.EqualTo("Alice"));
            Assert.That(_rb.Invites[0].GoobyName, Is.EqualTo("Flausch"));
            Assert.That(_rb.Invites[0].ExpiresInMs, Is.EqualTo(BoardClient.InviteTtlMs));

            var decline = Await(cb => _bb.Decline(_alice.FriendCode, cb), "BOARD_DECLINE");
            Assert.That(decline.Ok, Is.True);
            PumpUntil(() => _ra.Declined.Count == 1, "BOARD_DECLINED at Alice");
            Assert.That(_ra.Declined[0], Is.EqualTo(_bob.FriendCode));
            Assert.That(_ba.OutgoingInvite, Is.EqualTo(""));
            // the invite is gone on the server: accepting it now fails
            var late = Await(cb => _bb.Accept(_alice.FriendCode, cb), "late BOARD_ACCEPT");
            Assert.That(late.Ok, Is.False);
            Assert.That(late.Code, Is.EqualTo("NOT_FOUND"));
            Assert.That(_bb.HasGame, Is.False);

            var self = Await(cb => _ba.Invite(_alice.FriendCode, cb), "BOARD_INVITE self");
            Assert.That(self.Code, Is.EqualTo("SELF"));
        }

        [Test, Order(2)]
        public void B_InviteAcceptSitDownAndWrongTurnsAreRejected()
        {
            var invite = Await(cb => _ba.Invite(_bob.FriendCode, cb), "BOARD_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            PumpUntil(() => _rb.Invites.Count == 2, "second BOARD_INVITED");
            var accept = Await(cb => _bb.Accept(_alice.FriendCode, cb), "BOARD_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            Assert.That(accept.Type, Is.EqualTo("BOARD_START"));
            PumpUntil(() => _ba.HasGame && _bb.HasGame, "BOARD_START on both");
            Assert.That(_ra.Started, Is.EqualTo(1));
            Assert.That(_rb.Started, Is.EqualTo(1));
            Assert.That(_ba.RoomId, Does.StartWith("board:"));
            Assert.That(_bb.RoomId, Is.EqualTo(_ba.RoomId));
            Assert.That(_ba.Seed, Is.EqualTo(_bb.Seed));
            Assert.That(_ba.FirstCode, Is.EqualTo(_alice.FriendCode), "the inviter begins");
            Assert.That(_ba.OpponentCode, Is.EqualTo(_bob.FriendCode));
            Assert.That(_bb.OpponentName, Is.EqualTo("Alice"));
            PumpUntil(() => _ba.Joined && _bb.Joined && _ba.PeerInRoom && _bb.PeerInRoom, "both at the table");
            Assert.That(_ra.PeerJoined + _rb.PeerJoined, Is.EqualTo(2));
            Assert.That(_ba.MyTurn, Is.True);
            Assert.That(_bb.MyTurn, Is.False);

            // local guard and the server's turn ownership (boardgames.js kind hooks)
            Assert.That(_bb.Shoot(new BattleshipCell(0, 0)), Is.EqualTo(BoardClient.ErrNotYourTurn));
            Assert.That(_bb.SendRoomMessage("SHOT", new JObject { ["n"] = 1, ["cell"] = "A1" }), Is.True);
            PumpUntil(() => _rb.Rejected.Count == 1, "NOT_YOUR_TURN for Bob");
            Assert.That(_rb.Rejected[0], Is.EqualTo("SHOT:NOT_YOUR_TURN"));
            Assert.That(_ba.SendRoomMessage("SHOT", new JObject { ["n"] = 7, ["cell"] = "A1" }), Is.True);
            PumpUntil(() => _ra.Rejected.Count == 1, "BAD_TURN_N for Alice");
            Assert.That(_ra.Rejected[0], Is.EqualTo("SHOT:BAD_TURN_N"));
            Assert.That(_bb.SendRoomMessage("SHOT_RESULT", new JObject { ["n"] = 1, ["hit"] = true }), Is.True);
            PumpUntil(() => _rb.Rejected.Count == 2, "early SHOT_RESULT");
            Assert.That(_rb.Rejected[1], Is.EqualTo("SHOT_RESULT:NOT_YOUR_TURN"));
            Assert.That(_rb.Incoming, Is.Empty, "nothing rejected was relayed");
            Assert.That(_ba.MyTurn, Is.True, "the rejected messages changed nothing");
        }

        [Test, Order(3)]
        public void C_AFullGameWithAlternatingTurnsEndsInGameOver()
        {
            Assume.That(_ba.IsActive && _bb.IsActive, "needs the table of B_");
            var a = _alice.FriendCode;
            var b = _bob.FriendCode;
            // both clients place the seed fleet — the same one a Godot client computes (fleet_seed + auto_fleet)
            var fleetA = BattleshipLogic.DefaultFleet(_ba.Seed, a);
            var fleetB = BattleshipLogic.DefaultFleet(_bb.Seed, b);
            Assert.That(_ba.Fleet.Select(s => s.ToString()), Is.EqualTo(fleetA.Select(s => s.ToString())));
            Assert.That(_bb.Fleet.Select(s => s.ToString()), Is.EqualTo(fleetB.Select(s => s.ToString())));
            Assert.That(_bb.LockFleet(), Is.True, "Bob presses Bereit!");

            var targets = fleetB.SelectMany(s => s.Cells()).ToList();
            var water = new List<BattleshipCell>();
            for (var y = 0; y < BattleshipLogic.Grid; y++)
                for (var x = 0; x < BattleshipLogic.Grid; x++)
                    if (fleetA.All(s => !s.Contains(new BattleshipCell(x, y)))) water.Add(new BattleshipCell(x, y));

            for (var i = 0; i < targets.Count; i++)
            {
                PumpUntil(() => _ba.MyTurn, "Alice's turn " + i);
                Assert.That(_bb.MyTurn, Is.False);
                Assert.That(_ba.Turn.N, Is.EqualTo(1 + 2 * i), "move numbers count both players");
                Assert.That(_ba.Shoot(targets[i]), Is.EqualTo(""), "Alice shot " + i);
                PumpUntil(() => _ra.Results.Count == i + 1, "result of Alice's shot " + i);
                Assert.That(_ra.Results[i], Does.StartWith(targets[i].Ref + ":").And.Not.Contains(":miss"));
                Assert.That(_rb.Incoming.Last(), Does.StartWith(targets[i].Ref + ":"));
                if (_ba.Finished) break;
                PumpUntil(() => _bb.MyTurn, "Bob's turn " + i);
                Assert.That(_ba.MyTurn, Is.False);
                Assert.That(_bb.Shoot(water[i]), Is.EqualTo(""), "Bob shot " + i);
                PumpUntil(() => _rb.Results.Count == i + 1, "result of Bob's shot " + i);
                Assert.That(_rb.Results[i], Is.EqualTo(water[i].Ref + ":miss:0"));
            }
            PumpUntil(() => _ba.Finished && _bb.Finished, "GAME_OVER on both");
            PumpFor(150); // the relayed GAME_OVER must not count twice
            Assert.That(_ba.IWon, Is.True);
            Assert.That(_bb.IWon, Is.False);
            Assert.That(_ba.Winner, Is.EqualTo(a));
            Assert.That(_bb.Winner, Is.EqualTo(a));
            Assert.That(_ra.Overs, Is.EqualTo(new[] { a + ":True:AllSunk" }));
            Assert.That(_rb.Overs, Is.EqualTo(new[] { a + ":False:AllSunk" }));
            Assert.That(_ra.Results.Count(r => r.Contains(":sunk:")), Is.EqualTo(5));
            Assert.That(_ra.Results.Where(r => r.Contains(":sunk:")).Select(r => int.Parse(r.Split(':')[2])).Sum(), Is.EqualTo(17), "sunkCells from the Unity peer");
            Assert.That(_ba.ShotsFired, Is.EqualTo(17));
            Assert.That(_bb.ShotsFired, Is.EqualTo(16));
            Assert.That(_ba.ShipsSunk, Is.EqualTo(5));
            Assert.That(_bb.ShipsLeft, Is.EqualTo(0));
            Assert.That(_ba.ShipsLeft, Is.EqualTo(5));
            Assert.That(_ra.Rejected, Has.Count.EqualTo(1), "only the deliberate BAD_TURN_N of B_");
            Assert.That(_rb.Rejected, Has.Count.EqualTo(2), "only the deliberate probes of B_");
            Assert.That(_ba.Shoot(water[0]), Is.EqualTo(BoardClient.ErrNoGame));
        }

        [Test, Order(4)]
        public void D_RematchSwapsRolesAndLeavingMidGameIsAForfeit()
        {
            Assume.That(_ba.Finished && _bb.Finished, "needs the finished game of C_");
            var oldRoom = _ba.RoomId;
            var wait = Await(cb => _ba.RequestRematch(cb), "BOARD_REMATCH Alice");
            Assert.That(wait.Ok, Is.True, wait.Code);
            Assert.That(NetJson.Bool(wait.Data["waiting"]), Is.True);
            PumpUntil(() => _bb.RematchTheirs, "BOARD_REMATCH_WAIT at Bob");
            Assert.That(_rb.RematchAsks, Is.EqualTo(1));
            var start = Await(cb => _bb.RequestRematch(cb), "BOARD_REMATCH Bob");
            Assert.That(start.Type, Is.EqualTo("BOARD_START"));
            PumpUntil(() => _ba.RoomId == _bb.RoomId && _ba.RoomId != oldRoom && _ba.IsActive && _bb.IsActive, "fresh table on both");
            Assert.That(_ba.FirstCode, Is.EqualTo(_bob.FriendCode), "roles swap: Bob begins");
            Assert.That(_ba.Finished || _ba.RematchMine, Is.False, "state reset");
            PumpUntil(() => _ba.PeerInRoom && _bb.PeerInRoom, "both at the new table");

            // one exchange on the new table: Bob fires move 1, Alice answers
            Assert.That(_bb.MyTurn, Is.True);
            var shotsBefore = _rb.Results.Count;
            Assert.That(_bb.Shoot(new BattleshipCell(4, 4)), Is.EqualTo(""));
            PumpUntil(() => _rb.Results.Count == shotsBefore + 1, "result of the rematch shot");
            PumpUntil(() => _ba.MyTurn, "Alice's turn in the rematch");
            Assert.That(_ba.Turn.N, Is.EqualTo(2));

            // Bob walks away mid-game: the server pushes BOARD_FORFEIT to Alice
            var left = Await(cb => _bb.Leave(cb), "ROOM_LEAVE Bob");
            Assert.That(left.Ok, Is.True);
            Assert.That(_bb.HasGame, Is.False);
            PumpUntil(() => _ba.Finished, "BOARD_FORFEIT at Alice");
            Assert.That(_ba.IWon, Is.True);
            Assert.That(_ba.EndReason, Is.EqualTo(BoardEndReason.Forfeit));
            Assert.That(_ba.OpponentGone, Is.True);
            var rematch = Await(cb => _ba.RequestRematch(cb), "BOARD_REMATCH on a closed table");
            Assert.That(rematch.Ok, Is.False, "the table is gone");
            Await(cb => _ba.Leave(cb), "ROOM_LEAVE Alice");
            Assert.That(_ba.HasGame, Is.False);
        }
    }
}
