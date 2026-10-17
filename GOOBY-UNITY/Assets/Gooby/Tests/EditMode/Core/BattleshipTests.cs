using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Schiffe versenken: the pure rules (battleship_logic.gd — golden fleets from an independent port of the GDScript
    /// draw order, so Godot and Unity place the same ships for the same BOARD_START seed) and <see cref="BoardClient"/>
    /// against a scripted server on <see cref="FakeNetTransport"/> (invites, turn relay with move numbers, results,
    /// game over, rematch, forfeit, peer down/up, resume after a reconnect).
    /// </summary>
    public class BattleshipTests
    {
        private const string Me = "GOOBY-AB23";
        private const string Opp = "GOOBY-XY45";
        private const long Seed = 123456789;

        private static BattleshipShip Ship(int x, int y, int len, bool horizontal) => new BattleshipShip(x, y, len, horizontal);

        private static string Describe(IEnumerable<BattleshipShip> fleet) =>
            string.Join(" ", fleet.Select(s => s.X + "," + s.Y + "," + s.Len + "," + (s.Horizontal ? "h" : "v")));

        // ------------------------------------------------------------------ logic

        [TestCase("A1", 0, 0)]
        [TestCase("B4", 1, 3)]
        [TestCase("J10", 9, 9)]
        [TestCase(" c7 ", 2, 6)]
        public void CellRefsRoundTrip(string text, int x, int y)
        {
            var c = BattleshipLogic.RefToCell(text);
            Assert.That(c, Is.EqualTo(new BattleshipCell(x, y)));
            Assert.That(c.Ref, Is.EqualTo(text.Trim().ToUpperInvariant()));
        }

        [TestCase("")]
        [TestCase("Z9")]
        [TestCase("K1")]
        [TestCase("A0")]
        [TestCase("A11")]
        [TestCase("B-1")]
        [TestCase("1A")]
        public void BadCellRefsAreInvalid(string text)
        {
            Assert.That(BattleshipLogic.RefToCell(text).InBounds, Is.False, text);
            Assert.That(BattleshipCell.Invalid.Ref, Is.EqualTo(""));
        }

        [Test]
        public void FleetValidationReasons()
        {
            var ok = new List<BattleshipShip> { Ship(0, 0, 5, true), Ship(0, 2, 4, true), Ship(0, 4, 3, true), Ship(0, 6, 3, true), Ship(0, 8, 2, true) };
            Assert.That(BattleshipLogic.ValidateFleet(ok), Is.EqualTo(BattleshipLogic.ReasonOk));
            Assert.That(BattleshipLogic.ValidateFleet(ok.Take(4).ToList()), Is.EqualTo(BattleshipLogic.ReasonWrongCount));
            var lengths = new List<BattleshipShip>(ok) { [4] = Ship(0, 8, 3, true) };
            Assert.That(BattleshipLogic.ValidateFleet(lengths), Is.EqualTo(BattleshipLogic.ReasonWrongLengths));
            var outside = new List<BattleshipShip>(ok) { [0] = Ship(6, 0, 5, true) };
            Assert.That(BattleshipLogic.ValidateFleet(outside), Is.EqualTo(BattleshipLogic.ReasonOutOfBounds));
            var overlap = new List<BattleshipShip>(ok) { [1] = Ship(2, 0, 4, false) };
            Assert.That(BattleshipLogic.ValidateFleet(overlap), Is.EqualTo(BattleshipLogic.ReasonOverlap));
            Assert.That(BattleshipLogic.ValidateFleet(null), Is.EqualTo(BattleshipLogic.ReasonWrongCount));
        }

        /// <summary>Golden values from a line-by-line Python port of battleship_logic.gd auto_fleet + gooby_rng.gd + String.hash().</summary>
        [Test]
        public void FleetsFromTheSeedMatchGodot()
        {
            Assert.That(BattleshipLogic.GodotStringHash(Me), Is.EqualTo(3670379674u));
            Assert.That(BattleshipLogic.GodotStringHash(Opp), Is.EqualTo(3671231340u));
            Assert.That(BattleshipLogic.FleetSeed(Seed, Me), Is.EqualTo(1646352815L));
            Assert.That(BattleshipLogic.FleetSeed(Seed, Opp), Is.EqualTo(1647204481L));
            Assert.That(BattleshipLogic.FleetSeed(4294967295L, Opp), Is.EqualTo(1523747691L), "u32 server seed + hash, masked to 31 bits");

            Assert.That(Describe(BattleshipLogic.AutoFleet(20260725)), Is.EqualTo("0,0,5,h 0,4,4,v 7,8,3,h 7,9,3,h 7,1,2,v"));
            Assert.That(Describe(BattleshipLogic.DefaultFleet(Seed, Me)), Is.EqualTo("1,8,5,h 5,7,4,h 3,1,3,v 6,4,3,v 8,2,2,h"));
            Assert.That(Describe(BattleshipLogic.AutoFleet(BattleshipLogic.RerollSeed(Seed, Me, 1))), Is.EqualTo("1,1,5,v 0,5,4,v 7,6,3,h 3,4,3,v 5,7,2,v"));
            Assert.That(Describe(BattleshipLogic.DefaultFleet(4294967295L, Opp)), Is.EqualTo("5,2,5,v 3,5,4,v 7,7,3,h 2,7,3,v 9,2,2,v"));
            Assert.That(BattleshipLogic.RerollSeed(Seed, Me, 0), Is.EqualTo(BattleshipLogic.FleetSeed(Seed, Me)));
            for (var s = 0; s < 200; s++)
                Assert.That(BattleshipLogic.ValidateFleet(BattleshipLogic.AutoFleet(s * 7919L + 3)), Is.EqualTo(""), "seed " + s);
        }

        [Test]
        public void BoardAnswersHitMissSunkAndAllSunk()
        {
            var board = new BattleshipBoard();
            var fleet = BattleshipLogic.DefaultFleet(Seed, Me);
            Assert.That(board.Setup(fleet), Is.True);
            Assert.That(board.IsReady, Is.True);
            Assert.That(board.Setup(fleet.Take(3).ToList()), Is.False, "invalid fleet is refused");
            Assert.That(board.Ships, Has.Count.EqualTo(5), "…and the board stays as it was");

            var water = AllCells().First(c => board.ShipAt(c) < 0);
            var miss = board.ReceiveShot(water);
            Assert.That(miss.Hit || miss.Sunk || miss.Repeat, Is.False);
            Assert.That(board.ReceiveShot(water).Repeat, Is.True);

            var small = fleet[4]; // 2 cells
            var first = board.ReceiveShot(small.Cells()[0]);
            Assert.That(first.Hit, Is.True);
            Assert.That(first.Sunk, Is.False);
            var second = board.ReceiveShot(small.Cells()[1]);
            Assert.That(second.Sunk, Is.True);
            Assert.That(second.SunkCells, Is.EquivalentTo(small.Cells()));
            Assert.That(second.AllSunk, Is.False);
            var again = board.ReceiveShot(small.Cells()[1]);
            Assert.That(again.Repeat && again.Hit && !again.Sunk, Is.True, "a repeat counts nothing twice");
            Assert.That(board.RemainingShips, Is.EqualTo(4));

            ShotOutcome last = null;
            foreach (var ship in fleet.Take(4)) foreach (var c in ship.Cells()) last = board.ReceiveShot(c);
            Assert.That(last.Sunk && last.AllSunk, Is.True);
            Assert.That(board.RemainingShips, Is.EqualTo(0));
            Assert.That(board.ReceiveShot(BattleshipCell.Invalid).Hit, Is.False);
        }

        [Test]
        public void TrackerMarksSunkShipsFromCellsOrInfersThemForGodotPeers()
        {
            var tr = new BattleshipTracker();
            Assert.That(tr.Record(new BattleshipCell(1, 0), true, false), Is.Empty);
            // Unity peer: sunkCells marks the whole ship
            var unity = tr.Record(new BattleshipCell(0, 0), true, true, new List<BattleshipCell> { new BattleshipCell(0, 0), new BattleshipCell(1, 0) });
            Assert.That(unity, Is.EquivalentTo(new[] { new BattleshipCell(0, 0), new BattleshipCell(1, 0) }));
            // Godot peer: only "sunk" — the straight run of unsunk hits through the last cell
            tr.Record(new BattleshipCell(2, 0), true, false); // touches the sunk ship: must not merge with it
            tr.Record(new BattleshipCell(3, 0), true, false);
            var godot = tr.Record(new BattleshipCell(4, 0), true, true);
            Assert.That(godot, Is.EquivalentTo(new[] { new BattleshipCell(2, 0), new BattleshipCell(3, 0), new BattleshipCell(4, 0) }));
            tr.Record(new BattleshipCell(5, 5), false, false);
            Assert.That(tr.SunkCount, Is.EqualTo(2));
            Assert.That(tr.HitCount, Is.EqualTo(5));
            Assert.That(tr.ShotCount, Is.EqualTo(6));
            Assert.That(tr.IsNewTarget(new BattleshipCell(5, 5)), Is.False);
            Assert.That(tr.IsNewTarget(new BattleshipCell(6, 6)), Is.True);
            Assert.That(tr.IsNewTarget(BattleshipCell.Invalid), Is.False);
            Assert.That(tr.TryGetShot(new BattleshipCell(5, 5), out var hit) && !hit, Is.True);
            Assert.That(tr.IsSunkCell(new BattleshipCell(3, 0)), Is.True);
            Assert.That(tr.HasWon, Is.False);
        }

        [Test]
        public void TurnMirrorFollowsTheServerRules()
        {
            var turn = new BattleshipTurn(Me, new[] { Me, Opp });
            Assert.That(turn.CanShoot(Me), Is.True);
            Assert.That(turn.CanShoot(Opp), Is.False);
            Assert.That(turn.CanAnswer(Opp), Is.False, "no open shot");
            turn.OnShot();
            Assert.That(turn.CanShoot(Me), Is.False);
            Assert.That(turn.CanAnswer(Opp), Is.True);
            Assert.That(turn.CanAnswer(Me), Is.False, "the shooter never answers");
            turn.OnResult();
            Assert.That(turn.N, Is.EqualTo(2));
            Assert.That(turn.TurnCode, Is.EqualTo(Opp));
            Assert.That(turn.RoundIndex, Is.EqualTo(0));
            turn.OnShot();
            turn.OnResult();
            Assert.That(turn.RoundIndex, Is.EqualTo(1), "a round = both players shot once");
            Assert.That(turn.TurnCode, Is.EqualTo(Me));

            var tomatoes = new TomatoTracker();
            Assert.That(tomatoes.CanThrow(0), Is.True);
            tomatoes.MarkThrown(0);
            Assert.That(tomatoes.CanThrow(0), Is.False);
            Assert.That(tomatoes.CanThrow(1), Is.True);
        }

        [Test]
        public void RotatingAShipKeepsTheFleetValid()
        {
            var fleet = new List<BattleshipShip> { Ship(0, 0, 5, true), Ship(0, 2, 4, true), Ship(0, 4, 3, true), Ship(0, 6, 3, true), Ship(8, 8, 2, true) };
            var turned = BattleshipLogic.RotateAt(fleet, new BattleshipCell(9, 8));
            Assert.That(turned, Is.Not.Null);
            Assert.That(turned[4].Horizontal, Is.False);
            Assert.That(turned[4].Bow, Is.EqualTo(new BattleshipCell(8, 8)), "the bow stays");
            Assert.That(BattleshipLogic.RotateAt(fleet, new BattleshipCell(2, 0)), Is.Null, "would hit the ship below");
            Assert.That(BattleshipLogic.RotateAt(fleet, new BattleshipCell(5, 5)), Is.Null, "no ship there");
        }

        [Test]
        public void ShipsRoundTripThroughJson()
        {
            var fleet = BattleshipLogic.DefaultFleet(Seed, Opp);
            var json = BattleshipLogic.FleetToJson(fleet);
            Assert.That((int)json[0]["len"], Is.EqualTo(5));
            Assert.That(json[0]["at"] is JArray, Is.True);
            var back = BattleshipLogic.FleetFromJson(JArray.Parse(json.ToString()));
            Assert.That(Describe(back), Is.EqualTo(Describe(fleet)));
            Assert.That(BattleshipLogic.FleetFromJson(JArray.Parse("[{\"at\":[1],\"len\":2}]")), Is.Null);
            var cells = BattleshipLogic.CellsFromJson(BattleshipLogic.CellsToJson(fleet[0].Cells()));
            Assert.That(cells, Is.EqualTo(fleet[0].Cells()));
        }

        private static IEnumerable<BattleshipCell> AllCells()
        {
            for (var y = 0; y < BattleshipLogic.Grid; y++)
                for (var x = 0; x < BattleshipLogic.Grid; x++)
                    yield return new BattleshipCell(x, y);
        }

        // ------------------------------------------------------------------ BoardClient on a fake transport

        private string _dir;
        private long _now;
        private List<FakeNetTransport> _fakes;
        private NetSession _s;
        private BoardClient _b;
        private FakeNetTransport Fake => _fakes.Last();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gooby-board-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _now = 5_000_000;
            _fakes = new List<FakeNetTransport>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { /* best effort */ }
        }

        private void Online()
        {
            var identity = NetIdentity.CreateNew(Path.Combine(_dir, "id.json"));
            _s = new NetSession(identity, () =>
            {
                var f = new FakeNetTransport();
                _fakes.Add(f);
                return f;
            }, () => _now, new System.Random(3));
            _s.Configure(NetAddress.Parse("localhost"), "Anna", "Flausch", "", "test");
            _b = new BoardClient(_s, () => Me, () => _now);
            Connect();
        }

        private void Connect()
        {
            _s.Connect();
            Fake.Open();
            _s.Pump();
            Fake.Receive("WELCOME", new JObject { ["friendCode"] = Me, ["heartbeatSec"] = 20 }, (long)Fake.LastOfType("HELLO")["seq"]);
            _s.Pump();
            Assert.That(_s.IsOnline, Is.True);
        }

        private void Push(string t, JObject d)
        {
            Fake.Receive(t, d);
            _s.Pump();
        }

        private void Reply(JObject request, string t, JObject d)
        {
            Assert.That(request, Is.Not.Null, "request to answer");
            Fake.Receive(t, d, (long)request["seq"]);
            _s.Pump();
        }

        private List<JObject> Relayed(string kind) =>
            Fake.SentOfType("ROOM_MSG").Where(m => (string)m["d"]["kind"] == kind).ToList();

        private JObject LastRelayed(string kind) => Relayed(kind).LastOrDefault();

        private static JObject StartPayload(string room, string first, long seed = Seed) => new JObject
        {
            ["room"] = room,
            ["game"] = "battleship",
            ["seed"] = seed,
            ["first"] = first,
            ["players"] = new JArray
            {
                new JObject { ["friendCode"] = first == Me ? Me : Opp, ["name"] = first == Me ? "Anna" : "Ben", ["goobyName"] = first == Me ? "Flausch" : "Bobby" },
                new JObject { ["friendCode"] = first == Me ? Opp : Me, ["name"] = first == Me ? "Ben" : "Anna", ["goobyName"] = first == Me ? "Bobby" : "Flausch" },
            },
        };

        private static JObject RoomMsg(string room, string kind, JObject body, string from = Opp) => new JObject
        {
            ["room"] = room,
            ["kind"] = kind,
            ["body"] = body,
            ["from"] = new JObject { ["friendCode"] = from, ["goobyName"] = "Bobby" },
        };

        /// <summary>BOARD_START push (we invited) + ROOM_JOIN OK with both members.</summary>
        private void StartedGame(string room = "board:r1", string first = Me)
        {
            Push("BOARD_START", StartPayload(room, first));
            Reply(Fake.LastOfType("ROOM_JOIN"), "OK", new JObject { ["room"] = room, ["members"] = new JArray(Me, Opp) });
            Assert.That(_b.PeerInRoom, Is.True);
        }

        [Test]
        public void InvitesGoOutAndComeInAndExpireAfterThirtySeconds()
        {
            Online();
            NetReply reply = null;
            _b.Invite("gooby-xy45", r => reply = r);
            var inv = Fake.LastOfType("BOARD_INVITE");
            Assert.That((string)inv["d"]["target"], Is.EqualTo(Opp));
            Assert.That((string)inv["d"]["game"], Is.EqualTo("battleship"));
            Reply(inv, "OK", new JObject());
            Assert.That(reply.Ok, Is.True);
            Assert.That(_b.IsInvitePending(Opp), Is.True);
            _now += BoardClient.InviteTtlMs;
            Assert.That(_b.IsInvitePending(Opp), Is.False, "the server forgets it after 30 s");

            var incoming = new List<BoardInvite>();
            _b.InviteIncoming += i => incoming.Add(i);
            Push("BOARD_INVITED", new JObject { ["from"] = Opp, ["name"] = "Ben", ["goobyName"] = "Bobby", ["game"] = "go", ["expiresInMs"] = 30000 });
            Assert.That(incoming, Is.Empty, "games this client cannot play are ignored");
            Push("BOARD_INVITED", new JObject { ["from"] = Opp, ["name"] = "Ben", ["goobyName"] = "Bobby", ["game"] = "chess", ["expiresInMs"] = 30000 });
            Assert.That(incoming, Has.Count.EqualTo(1), "chess invites come in too");
            Assert.That(incoming[0].Game, Is.EqualTo("chess"));
            Push("BOARD_INVITED", new JObject { ["from"] = Opp, ["name"] = "Ben", ["goobyName"] = "Bobby", ["game"] = "battleship", ["expiresInMs"] = 30000 });
            Assert.That(incoming, Has.Count.EqualTo(2));
            Assert.That(incoming[1].Game, Is.EqualTo("battleship"));
            Assert.That(_b.IncomingInvites, Has.Count.EqualTo(1), "the newer invite of the same friend replaces the older one");
            Assert.That(incoming[1].Name, Is.EqualTo("Ben"));
            Assert.That(_b.FindInvite(Opp), Is.Not.Null);
            _now += 29_000;
            Assert.That(_b.IncomingInvites, Has.Count.EqualTo(1));
            _now += 1_000;
            Assert.That(_b.IncomingInvites, Is.Empty);
        }

        [Test]
        public void AcceptingStartsTheGameJoinsTheRoomAndPlacesTheSeedFleet()
        {
            Online();
            Push("BOARD_INVITED", new JObject { ["from"] = Opp, ["name"] = "Ben", ["goobyName"] = "Bobby", ["game"] = "battleship" });
            var started = 0;
            _b.GameStarted += () => started++;
            NetReply reply = null;
            _b.Accept(Opp, r => reply = r);
            var accept = Fake.LastOfType("BOARD_ACCEPT");
            Assert.That((string)accept["d"]["from"], Is.EqualTo(Opp));
            Assert.That(_b.IncomingInvites, Is.Empty);
            Reply(accept, "BOARD_START", StartPayload("board:abc", Opp));
            Assert.That(reply.Ok, Is.True);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(_b.RoomId, Is.EqualTo("board:abc"));
            Assert.That(_b.Seed, Is.EqualTo(Seed));
            Assert.That(_b.FirstCode, Is.EqualTo(Opp));
            Assert.That(_b.OpponentCode, Is.EqualTo(Opp));
            Assert.That(_b.OpponentName, Is.EqualTo("Ben"));
            Assert.That(_b.OpponentGoobyName, Is.EqualTo("Bobby"));
            Assert.That(_b.MyTurn, Is.False, "the inviter begins");
            Assert.That(Describe(_b.Fleet), Is.EqualTo(Describe(BattleshipLogic.DefaultFleet(Seed, Me))), "same fleet as a Godot client would place");
            var join = Fake.LastOfType("ROOM_JOIN");
            Assert.That((string)join["d"]["room"], Is.EqualTo("board:abc"));
            var joined = 0;
            _b.PeerJoined += () => joined++;
            Reply(join, "OK", new JObject { ["room"] = "board:abc", ["members"] = new JArray(Me) });
            Assert.That(_b.Joined, Is.True);
            Assert.That(_b.PeerInRoom, Is.False);
            Push("ROOM_PEER_JOINED", new JObject { ["room"] = "board:abc", ["friendCode"] = Opp, ["name"] = "Ben" });
            Assert.That(_b.PeerInRoom, Is.True);
            Assert.That(joined, Is.EqualTo(1));
        }

        [Test]
        public void DeclineGoesOutAndTheInviterHearsIt()
        {
            Online();
            Push("BOARD_INVITED", new JObject { ["from"] = Opp, ["game"] = "battleship" });
            _b.Decline(Opp);
            Assert.That((string)Fake.LastOfType("BOARD_DECLINE")["d"]["from"], Is.EqualTo(Opp));
            Assert.That(_b.IncomingInvites, Is.Empty);

            _b.Invite(Opp, null);
            Reply(Fake.LastOfType("BOARD_INVITE"), "OK", new JObject());
            var declined = new List<string>();
            _b.InviteDeclined += by => declined.Add(by);
            Push("BOARD_DECLINED", new JObject { ["from"] = Opp });
            Assert.That(declined, Is.EqualTo(new[] { Opp }));
            Assert.That(_b.OutgoingInvite, Is.EqualTo(""));
        }

        [Test]
        public void ShotsFollowTheTurnWithExactMoveNumbers()
        {
            Online();
            StartedGame();
            Assert.That(_b.MyTurn, Is.True);
            var target = new BattleshipCell(1, 3);
            Assert.That(_b.Shoot(target), Is.EqualTo(""));
            Assert.That(_b.FleetLocked, Is.True, "the first shot locks the fleet");
            var shot = LastRelayed("SHOT");
            Assert.That((string)shot["d"]["room"], Is.EqualTo("board:r1"));
            Assert.That((int)shot["d"]["body"]["n"], Is.EqualTo(1));
            Assert.That((string)shot["d"]["body"]["cell"], Is.EqualTo("B4"));
            Assert.That(_b.AwaitingResult, Is.True);
            Assert.That(_b.Shoot(new BattleshipCell(2, 2)), Is.EqualTo(BoardClient.ErrNotYourTurn), "one shot per turn");

            var results = new List<string>();
            _b.ShotResult += (c, hit, sunk, cells) => results.Add(c.Ref + ":" + hit + ":" + sunk);
            Push("ROOM_MSG", RoomMsg("board:r1", "SHOT_RESULT", new JObject { ["n"] = 1, ["hit"] = true, ["sunk"] = false }));
            Assert.That(results, Is.EqualTo(new[] { "B4:True:False" }));
            Assert.That(_b.Tracker.TryGetShot(target, out var wasHit) && wasHit, Is.True);
            Assert.That(_b.MyTurn, Is.False);
            Assert.That(_b.Turn.N, Is.EqualTo(2));
            Assert.That(_b.Shoot(new BattleshipCell(5, 5)), Is.EqualTo(BoardClient.ErrNotYourTurn));

            // the opponent shoots at our fleet: we answer with the same n and it is our turn again
            var incoming = new List<string>();
            _b.OpponentShot += (c, o) => incoming.Add(c.Ref + ":" + o.Hit);
            var ownShip = _b.Fleet[0].Cells()[0];
            Push("ROOM_MSG", RoomMsg("board:r1", "SHOT", new JObject { ["n"] = 2, ["cell"] = ownShip.Ref }));
            var answer = LastRelayed("SHOT_RESULT");
            Assert.That((int)answer["d"]["body"]["n"], Is.EqualTo(2));
            Assert.That((bool)answer["d"]["body"]["hit"], Is.True);
            Assert.That((bool)answer["d"]["body"]["sunk"], Is.False);
            Assert.That((string)answer["d"]["body"]["cell"], Is.EqualTo(ownShip.Ref));
            Assert.That(incoming, Is.EqualTo(new[] { ownShip.Ref + ":True" }));
            Assert.That(_b.LastIncoming, Is.EqualTo(ownShip));
            Assert.That(_b.MyTurn, Is.True);
            Assert.That(_b.Turn.N, Is.EqualTo(3));
            Assert.That(_b.Shoot(target), Is.EqualTo(BoardClient.ErrAlreadyShot));
            Assert.That(_b.Shoot(new BattleshipCell(3, 3)), Is.EqualTo(""));
            Assert.That((int)LastRelayed("SHOT")["d"]["body"]["n"], Is.EqualTo(3));

            // messages of other rooms (a house visit) are not ours
            Push("ROOM_MSG", RoomMsg("visit:x", "SHOT_RESULT", new JObject { ["n"] = 3, ["hit"] = true }));
            Assert.That(_b.AwaitingResult, Is.True);
        }

        [Test]
        public void LocalGuardsRefuseShotsBeforeThePeerSitsAndWhileOffline()
        {
            Online();
            Push("BOARD_START", StartPayload("board:r1", Me));
            Reply(Fake.LastOfType("ROOM_JOIN"), "OK", new JObject { ["members"] = new JArray(Me) });
            Assert.That(_b.Shoot(new BattleshipCell(0, 0)), Is.EqualTo(BoardClient.ErrWaitingPeer), "a relay to an empty seat would be lost");
            Assert.That(_b.Shoot(BattleshipCell.Invalid), Is.EqualTo(BoardClient.ErrBadCell));
            Push("ROOM_PEER_JOINED", new JObject { ["room"] = "board:r1", ["friendCode"] = Opp });
            Fake.ServerClose(1006, "");
            _s.Pump();
            Assert.That(_s.IsOnline, Is.False);
            Assert.That(_b.PeerInRoom, Is.False);
            Assert.That(_b.Shoot(new BattleshipCell(0, 0)), Is.EqualTo(NetErrorCodes.Offline));
        }

        [Test]
        public void AServerRejectionTurnsTheShotBack()
        {
            Online();
            StartedGame();
            var rejected = new List<string>();
            _b.SendRejected += (kind, code) => rejected.Add(kind + ":" + code);
            Assert.That(_b.Shoot(new BattleshipCell(4, 4)), Is.EqualTo(""));
            Reply(LastRelayed("SHOT"), "ERROR", new JObject { ["code"] = "NOT_YOUR_TURN" });
            Assert.That(rejected, Is.EqualTo(new[] { "SHOT:NOT_YOUR_TURN" }));
            Assert.That(_b.PendingShot.InBounds, Is.False);
            Assert.That(_b.MyTurn, Is.True);
            Assert.That(_b.ShotsFired, Is.EqualTo(0));
            // a relayed message has no reply: silence past the timeout means delivered, nothing changes
            Assert.That(_b.Shoot(new BattleshipCell(4, 4)), Is.EqualTo(""));
            _now += BoardClient.RelayAckTimeoutMs + 10;
            _s.Pump();
            Assert.That(_b.AwaitingResult, Is.True);
            Assert.That(rejected, Has.Count.EqualTo(1));
        }

        /// <summary>A whole game against a Godot-style peer (SHOT_RESULT without sunkCells): we sink the seed fleet and win.</summary>
        [Test]
        public void AFullGameEndsWithOurGameOver()
        {
            Online();
            StartedGame();
            var enemy = new BattleshipBoard();
            enemy.Setup(BattleshipLogic.DefaultFleet(Seed, Opp));
            var targets = enemy.Ships.SelectMany(s => s.Cells()).ToList();
            var water = AllCells().Where(c => _b.Fleet.All(s => !s.Contains(c))).ToList();
            var overs = new List<string>();
            _b.GameOver += (w, won, reason) => overs.Add(w + ":" + won + ":" + reason);
            var n = 1;
            for (var i = 0; i < targets.Count; i++)
            {
                Assert.That(_b.Shoot(targets[i]), Is.EqualTo(""), "shot " + i);
                Assert.That((int)LastRelayed("SHOT")["d"]["body"]["n"], Is.EqualTo(n));
                var o = enemy.ReceiveShot(targets[i]);
                Push("ROOM_MSG", RoomMsg("board:r1", "SHOT_RESULT", new JObject { ["n"] = n, ["hit"] = o.Hit, ["sunk"] = o.Sunk }));
                n++;
                if (_b.Finished) break;
                Push("ROOM_MSG", RoomMsg("board:r1", "SHOT", new JObject { ["n"] = n, ["cell"] = water[i].Ref }));
                Assert.That((bool)LastRelayed("SHOT_RESULT")["d"]["body"]["hit"], Is.False);
                n++;
            }
            Assert.That(_b.Finished && _b.IWon, Is.True);
            Assert.That(_b.EndReason, Is.EqualTo(BoardEndReason.AllSunk));
            Assert.That(overs, Is.EqualTo(new[] { Me + ":True:AllSunk" }));
            Assert.That((string)LastRelayed("GAME_OVER")["d"]["body"]["winner"], Is.EqualTo(Me));
            Assert.That(_b.ShotsFired, Is.EqualTo(17));
            Assert.That(_b.HitsLanded, Is.EqualTo(17));
            Assert.That(_b.AccuracyPercent, Is.EqualTo(100));
            Assert.That(_b.ShipsSunk, Is.EqualTo(5));
            Assert.That(_b.Tracker.SunkCells, Is.EquivalentTo(targets), "sunk ships inferred from the hits of a Godot peer");
            Assert.That(_b.Shoot(water[30]), Is.EqualTo(BoardClient.ErrNoGame));
            Push("ROOM_MSG", RoomMsg("board:r1", "GAME_OVER", new JObject { ["winner"] = Me }));
            Assert.That(overs, Has.Count.EqualTo(1), "GAME_OVER is raised once");
        }

        [Test]
        public void LosingTheLastShipAnswersAllSunkAndEndsTheGame()
        {
            Online();
            StartedGame(first: Opp);
            Assert.That(_b.LockFleet(), Is.True);
            var mine = _b.Fleet.SelectMany(s => s.Cells()).ToList();
            var overs = new List<bool>();
            _b.GameOver += (w, won, reason) => overs.Add(won);
            var n = 1;
            for (var i = 0; i < mine.Count; i++)
            {
                Push("ROOM_MSG", RoomMsg("board:r1", "SHOT", new JObject { ["n"] = n, ["cell"] = mine[i].Ref }));
                n++;
                if (_b.Finished) break;
                Assert.That(_b.Shoot(new BattleshipCell(i % 10, i / 10)), Is.EqualTo(""));
                Push("ROOM_MSG", RoomMsg("board:r1", "SHOT_RESULT", new JObject { ["n"] = n, ["hit"] = false, ["sunk"] = false }));
                n++;
            }
            var last = LastRelayed("SHOT_RESULT")["d"]["body"];
            Assert.That((bool)last["sunk"] && (bool)last["allSunk"], Is.True);
            Assert.That(BattleshipLogic.CellsFromJson(last["sunkCells"]), Has.Count.EqualTo(_b.Fleet.Last().Len));
            Assert.That(_b.Finished && !_b.IWon, Is.True);
            Assert.That(_b.Winner, Is.EqualTo(Opp));
            Assert.That(_b.ShipsLeft, Is.EqualTo(0));
            Assert.That(Relayed("GAME_OVER"), Is.Empty, "the winner sends GAME_OVER");
            Push("ROOM_MSG", RoomMsg("board:r1", "GAME_OVER", new JObject { ["winner"] = Opp }));
            Assert.That(overs, Is.EqualTo(new[] { false }));
        }

        [Test]
        public void TomatoOncePerRoundAndEmotes()
        {
            Online();
            StartedGame();
            Assert.That(_b.ThrowTomato(), Is.True);
            Assert.That(LastRelayed("TOMATO"), Is.Not.Null);
            Assert.That(_b.ThrowTomato(), Is.False, "one per round");
            Assert.That(_b.SendEmote("dance"), Is.True);
            Assert.That((string)LastRelayed("EMOTE")["d"]["body"]["id"], Is.EqualTo("dance"));
            Assert.That(_b.SendEmote("poker"), Is.False);
            var emotes = new List<string>();
            var tomatoes = 0;
            _b.OpponentEmote += id => emotes.Add(id);
            _b.TomatoIncoming += () => tomatoes++;
            Push("ROOM_MSG", RoomMsg("board:r1", "EMOTE", new JObject { ["id"] = "laugh" }));
            Push("ROOM_MSG", RoomMsg("board:r1", "TOMATO", new JObject()));
            Assert.That(emotes, Is.EqualTo(new[] { "laugh" }));
            Assert.That(tomatoes, Is.EqualTo(1));
            // round 1 after one exchange each
            _b.Shoot(new BattleshipCell(0, 0));
            Push("ROOM_MSG", RoomMsg("board:r1", "SHOT_RESULT", new JObject { ["n"] = 1, ["hit"] = false, ["sunk"] = false }));
            Push("ROOM_MSG", RoomMsg("board:r1", "SHOT", new JObject { ["n"] = 2, ["cell"] = "J10" }));
            Assert.That(_b.Turn.RoundIndex, Is.EqualTo(1));
            Assert.That(_b.CanThrowTomato, Is.True);
            var rejected = new List<string>();
            _b.TomatoRejected += code => rejected.Add(code);
            Assert.That(_b.ThrowTomato(), Is.True);
            Reply(LastRelayed("TOMATO"), "ERROR", new JObject { ["code"] = "TOMATO_LIMIT" });
            Assert.That(rejected, Is.EqualTo(new[] { "TOMATO_LIMIT" }));
            Assert.That(_b.CanThrowTomato, Is.False, "the server said: already thrown");
        }

        [Test]
        public void SurrenderThenRematchStartsAFreshTable()
        {
            Online();
            StartedGame();
            Assert.That(_b.Surrender(), Is.True);
            Assert.That((string)LastRelayed("GAME_OVER")["d"]["body"]["winner"], Is.EqualTo(Opp));
            Assert.That(_b.Finished && !_b.IWon, Is.True);
            Assert.That(_b.EndReason, Is.EqualTo(BoardEndReason.Surrender));
            Assert.That(_b.Surrender(), Is.False);

            NetReply reply = null;
            _b.RequestRematch(r => reply = r);
            var rematch = Fake.LastOfType("BOARD_REMATCH");
            Assert.That((string)rematch["d"]["room"], Is.EqualTo("board:r1"));
            Reply(rematch, "OK", new JObject { ["waiting"] = true });
            Assert.That(reply.Ok, Is.True);
            Assert.That(_b.RematchMine, Is.True);
            var starts = 0;
            _b.GameStarted += () => starts++;
            Push("BOARD_START", StartPayload("board:r2", Opp));
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(_b.RoomId, Is.EqualTo("board:r2"));
            Assert.That(_b.Finished, Is.False);
            Assert.That(_b.RematchMine, Is.False);
            Assert.That(_b.FirstCode, Is.EqualTo(Opp), "roles swap");
            Assert.That(_b.Tracker.ShotCount, Is.EqualTo(0));
            Assert.That(Fake.SentOfType("ROOM_LEAVE").Any(m => (string)m["d"]["room"] == "board:r1"), Is.True, "the old table is left");
            Assert.That((string)Fake.LastOfType("ROOM_JOIN")["d"]["room"], Is.EqualTo("board:r2"));
        }

        [Test]
        public void TheOpponentsRematchWishAndLeavingAreReported()
        {
            Online();
            StartedGame();
            _b.Surrender();
            var asked = 0;
            var declined = 0;
            _b.RematchRequestedByOpponent += () => asked++;
            _b.RematchDeclined += () => declined++;
            Push("BOARD_REMATCH_WAIT", new JObject { ["room"] = "board:r1", ["friendCode"] = Opp });
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(_b.RematchTheirs, Is.True);
            Push("BOARD_REMATCH_DECLINED", new JObject { ["room"] = "board:r1", ["friendCode"] = Opp });
            Assert.That(declined, Is.EqualTo(1));
            Assert.That(_b.OpponentGone, Is.True);
            Push("BOARD_REMATCH_WAIT", new JObject { ["room"] = "board:other" });
            Assert.That(asked, Is.EqualTo(1), "other tables are not ours");
        }

        [Test]
        public void PeerDownUpAndForfeit()
        {
            Online();
            StartedGame();
            var changes = new List<string>();
            _b.PeerConnectionChanged += (down, wait) => changes.Add(down + ":" + wait);
            Push("ROOM_PEER_LEFT", new JObject { ["room"] = "board:r1", ["friendCode"] = Opp });
            Push("BOARD_PEER_DOWN", new JObject { ["room"] = "board:r1", ["friendCode"] = Opp, ["waitMs"] = 120000 });
            Assert.That(_b.PeerDown, Is.True);
            Assert.That(_b.PeerInRoom, Is.False);
            Assert.That(_b.OpponentGone, Is.False, "a dropped connection is not leaving");
            Push("ROOM_PEER_JOINED", new JObject { ["room"] = "board:r1", ["friendCode"] = Opp });
            Push("BOARD_PEER_UP", new JObject { ["room"] = "board:r1", ["friendCode"] = Opp });
            Assert.That(_b.PeerDown, Is.False);
            Assert.That(_b.PeerInRoom, Is.True);
            Assert.That(changes, Is.EqualTo(new[] { "True:120000", "False:0" }));

            var overs = new List<string>();
            _b.GameOver += (w, won, reason) => overs.Add(won + ":" + reason);
            Push("BOARD_FORFEIT", new JObject { ["room"] = "board:r1", ["winner"] = Me });
            Assert.That(overs, Is.EqualTo(new[] { "True:Forfeit" }));
            Assert.That(_b.OpponentGone, Is.True);
            NetReply rematch = null;
            _b.Leave(r => rematch = r);
            Assert.That((string)Fake.LastOfType("ROOM_LEAVE")["d"]["room"], Is.EqualTo("board:r1"));
            Assert.That(_b.HasGame, Is.False);
            Reply(Fake.LastOfType("ROOM_LEAVE"), "OK", new JObject());
            Assert.That(rematch.Ok, Is.True);
        }

        [Test]
        public void AfterAReconnectTheTableIsRejoinedAndTheHistoryReplayed()
        {
            Online();
            StartedGame();
            Assert.That(_b.LockFleet(), Is.True);
            var fleet = _b.Fleet.ToList();
            // exchange 1: we shoot B4 (hit)
            _b.Shoot(new BattleshipCell(1, 3));
            Push("ROOM_MSG", RoomMsg("board:r1", "SHOT_RESULT", new JObject { ["n"] = 1, ["hit"] = true, ["sunk"] = false }));
            // link lost; meanwhile the opponent fires at our first ship (the relay never reached us)
            Fake.ServerClose(1006, "");
            _s.Pump();
            Assert.That(_b.Joined, Is.False);
            _now += NetSession.ReconnectMaxMs + 1000;
            _s.Pump();
            Fake.Open();
            _s.Pump();
            Fake.Receive("WELCOME", new JObject { ["friendCode"] = Me, ["heartbeatSec"] = 20 }, (long)Fake.LastOfType("HELLO")["seq"]);
            _s.Pump();
            var join = Fake.LastOfType("ROOM_JOIN");
            Assert.That((string)join["d"]["room"], Is.EqualTo("board:r1"), "back to the table after WELCOME");

            var resumed = 0;
            _b.GameResumed += () => resumed++;
            var hitCell = fleet[0].Cells()[0];
            Push("BOARD_RESUME", new JObject
            {
                ["room"] = "board:r1",
                ["game"] = "battleship",
                ["turn"] = Opp,
                ["n"] = 2,
                ["history"] = new JArray
                {
                    new JObject { ["kind"] = "SHOT", ["from"] = Me, ["body"] = new JObject { ["n"] = 1, ["cell"] = "B4" } },
                    new JObject { ["kind"] = "SHOT_RESULT", ["from"] = Opp, ["body"] = new JObject { ["n"] = 1, ["hit"] = true, ["sunk"] = false } },
                    new JObject { ["kind"] = "EMOTE", ["from"] = Opp, ["body"] = new JObject { ["id"] = "angry" } },
                    new JObject { ["kind"] = "SHOT", ["from"] = Opp, ["body"] = new JObject { ["n"] = 2, ["cell"] = hitCell.Ref } },
                },
            });
            Assert.That(resumed, Is.EqualTo(1));
            var answer = LastRelayed("SHOT_RESULT");
            Assert.That(answer, Is.Not.Null, "the open shot is answered after the resume");
            Assert.That((int)answer["d"]["body"]["n"], Is.EqualTo(2));
            Assert.That((bool)answer["d"]["body"]["hit"], Is.True);
            Assert.That(Describe(_b.Fleet), Is.EqualTo(Describe(fleet)), "the locked fleet survives the reconnect");
            Assert.That(_b.Board.WasShot(hitCell), Is.True);
            Assert.That(_b.Tracker.TryGetShot(new BattleshipCell(1, 3), out var hit) && hit, Is.True);
            Assert.That(_b.MyTurn, Is.True);
            Assert.That(_b.Turn.N, Is.EqualTo(3));
            Assert.That(_b.ShotsFired, Is.EqualTo(1));
            Reply(join, "OK", new JObject { ["room"] = "board:r1", ["members"] = new JArray(Me, Opp) });
            Assert.That(_b.PeerInRoom, Is.True);
        }

        [Test]
        public void ATableTheServerForgotEndsTheGame()
        {
            Online();
            StartedGame();
            Fake.ServerClose(1006, "");
            _s.Pump();
            _now += NetSession.ReconnectMaxMs + 1000;
            _s.Pump();
            Fake.Open();
            _s.Pump();
            Fake.Receive("WELCOME", new JObject { ["friendCode"] = Me, ["heartbeatSec"] = 20 }, (long)Fake.LastOfType("HELLO")["seq"]);
            _s.Pump();
            Reply(Fake.LastOfType("ROOM_JOIN"), "ERROR", new JObject { ["code"] = "BAD_ROOM" });
            Assert.That(_b.Finished, Is.True);
            Assert.That(_b.EndReason, Is.EqualTo(BoardEndReason.Expired));
            Assert.That(_b.OpponentGone, Is.True);
        }

        [Test]
        public void SetupRerollsAndTurnsShipsUntilTheFleetIsLocked()
        {
            Online();
            StartedGame();
            var first = Describe(_b.Fleet);
            Assert.That(_b.RerollFleet(), Is.True);
            Assert.That(Describe(_b.Fleet), Is.EqualTo(Describe(BattleshipLogic.AutoFleet(BattleshipLogic.RerollSeed(Seed, Me, 1)))), "Godot's reroll seed");
            Assert.That(Describe(_b.Fleet), Is.Not.EqualTo(first));
            var custom = new List<BattleshipShip> { Ship(0, 0, 5, true), Ship(0, 2, 4, true), Ship(0, 4, 3, true), Ship(0, 6, 3, true), Ship(8, 8, 2, true) };
            Assert.That(_b.SetDraftFleet(custom), Is.True);
            Assert.That(_b.RotateShipAt(new BattleshipCell(9, 8)), Is.True);
            Assert.That(_b.Fleet[4].Horizontal, Is.False);
            Assert.That(_b.RotateShipAt(new BattleshipCell(1, 0)), Is.False);
            Assert.That(_b.LockFleet(), Is.True);
            Assert.That(_b.RerollFleet(), Is.False);
            Assert.That(_b.RotateShipAt(new BattleshipCell(8, 8)), Is.False);
            Assert.That(_b.Board.IsReady, Is.True);
            Assert.That(_b.ShipsLeft, Is.EqualTo(5));
        }
    }
}
