using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Net;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Schach: the pure rules (port of chess_logic.gd — perft of the standard positions, castling, en passant, promotion,
    /// mate/stalemate and the draw rules, FEN + SAN), the practice AI (chess_ai.gd — mate in 1, deterministic per seed,
    /// fast enough) and <see cref="BoardClient"/> for chess against a scripted server on <see cref="FakeNetTransport"/>
    /// (SHOT {n, move} + auto-ack SHOT_RESULT {n}, GAME_OVER by the receiver of the mating move, surrender, rejection
    /// rollback, resume replay).
    /// </summary>
    public class ChessTests
    {
        private const string Me = "GOOBY-AB23";
        private const string Opp = "GOOBY-XY45";
        private const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";

        private static ChessLogic Play(params string[] moves)
        {
            var c = new ChessLogic();
            foreach (var m in moves) Assert.That(c.PlayUci(m), Is.True, "move " + m);
            return c;
        }

        // ------------------------------------------------------------------ rules

        [TestCase(1, 20)]
        [TestCase(2, 400)]
        [TestCase(3, 8902)]
        public void PerftFromTheStartPosition(int depth, long nodes)
        {
            var c = new ChessLogic();
            Assert.That(c.Perft(depth), Is.EqualTo(nodes));
            Assert.That(c.ToFen(), Is.EqualTo(ChessLogic.StartFen), "make/unmake leaves the board untouched");
        }

        [TestCase(1, 48)]
        [TestCase(2, 2039)]
        public void PerftOfKiwipeteCoversCastlingEnPassantAndPromotions(int depth, long nodes)
        {
            var c = new ChessLogic(Kiwipete);
            Assert.That(c.Perft(depth), Is.EqualTo(nodes));
            Assert.That(c.ToFen(), Is.EqualTo(Kiwipete));
        }

        [Test]
        public void FenRoundTripsAndBadFensAreRejected()
        {
            Assert.That(new ChessLogic().ToFen(), Is.EqualTo(ChessLogic.StartFen));
            Assert.That(new ChessLogic(Kiwipete).ToFen(), Is.EqualTo(Kiwipete));
            var c = new ChessLogic();
            Assert.That(c.FromFen("8/8/8 w - -"), Is.False);
            Assert.That(c.FromFen("8/8/8/8/8/8/8/8 w - - 0 1"), Is.False, "no kings");
            Assert.That(Play("e2e4").ToFen(), Is.EqualTo("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"));
        }

        [Test]
        public void CastlingBothSidesMovesTheRookAndClearsTheRights()
        {
            var c = new ChessLogic("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            Assert.That(c.UciToMove("e1g1"), Is.Not.EqualTo(0));
            Assert.That(c.UciToMove("e1c1"), Is.Not.EqualTo(0));
            Assert.That(c.San(c.UciToMove("e1g1")), Is.EqualTo("O-O"));
            Assert.That(c.PlayUci("e1g1"), Is.True);
            Assert.That(c.PieceAt(6, 0), Is.EqualTo(ChessLogic.King));
            Assert.That(c.PieceAt(5, 0), Is.EqualTo(ChessLogic.Rook));
            Assert.That(c.PieceAt(7, 0), Is.EqualTo(0));
            Assert.That(c.PlayUci("e8c8"), Is.True);
            Assert.That(c.PieceAt(2, 7), Is.EqualTo(-ChessLogic.King));
            Assert.That(c.PieceAt(3, 7), Is.EqualTo(-ChessLogic.Rook));
            Assert.That(c.ToFen(), Is.EqualTo("2kr3r/8/8/8/8/8/8/R4RK1 w - - 2 2"));
            Assert.That(c.SanMoves, Is.EqualTo(new[] { "O-O", "O-O-O" }));

            // not out of, through or into check; not after the king or rook moved
            Assert.That(new ChessLogic("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1").UciToMove("e1g1"), Is.Not.EqualTo(0));
            Assert.That(new ChessLogic("r3k2r/8/8/8/8/8/5r2/R3K2R w KQkq - 0 1").UciToMove("e1g1"), Is.EqualTo(0), "f1 attacked");
            Assert.That(new ChessLogic("4k3/8/8/8/8/8/8/R3K2r w Q - 0 1").UciToMove("e1c1"), Is.EqualTo(0), "king in check");
            Assert.That(new ChessLogic("4k3/8/8/8/8/8/8/R3K1r1 w Q - 0 1").UciToMove("e1c1"), Is.EqualTo(0), "in check along the rank");
            var moved = new ChessLogic("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            Assert.That(moved.PlayUci("h1h2") && moved.PlayUci("a8a7"), Is.True);
            Assert.That(moved.UciToMove("e1g1"), Is.EqualTo(0), "the h-rook moved");
            Assert.That(moved.UciToMove("e1c1"), Is.Not.EqualTo(0));
            Assert.That(moved.ToFen().Split(' ')[2], Is.EqualTo("Qk"));
        }

        [Test]
        public void EnPassantCapturesThePassedPawnOnlyRightAway()
        {
            var c = Play("e2e4", "a7a6", "e4e5", "d7d5");
            Assert.That(c.EpSquare, Is.EqualTo(ChessLogic.ParseSquare("d6")));
            var ep = c.UciToMove("e5d6");
            Assert.That(ChessLogic.MoveFlags(ep) & ChessLogic.FlagEp, Is.Not.EqualTo(0));
            Assert.That(c.San(ep), Is.EqualTo("exd6"));
            Assert.That(c.PlayMove(ep), Is.True);
            Assert.That(c.PieceAt(3, 4), Is.EqualTo(0), "the d5 pawn is gone");
            Assert.That(c.PieceAt(3, 5), Is.EqualTo(ChessLogic.Pawn));
            Assert.That(c.UndoPlay(), Is.True);
            Assert.That(c.PieceAt(3, 4), Is.EqualTo(-ChessLogic.Pawn), "undo puts it back");

            var late = Play("e2e4", "a7a6", "e4e5", "d7d5", "h2h3", "h7h6");
            Assert.That(late.UciToMove("e5d6"), Is.EqualTo(0), "only on the very next move");
        }

        [Test]
        public void PromotionNeedsThePieceLetter()
        {
            var c = new ChessLogic("8/P7/8/8/8/8/8/k6K w - - 0 1");
            Assert.That(c.UciToMove("a7a8"), Is.EqualTo(0), "no promotion piece = no move");
            Assert.That(c.LegalMovesFrom(ChessLogic.ParseSquare("a7")), Has.Count.EqualTo(4), "Q R B N");
            var n = new ChessLogic("8/P7/8/8/8/8/8/k6K w - - 0 1");
            Assert.That(n.PlayUci("a7a8n"), Is.True);
            Assert.That(n.PieceAt(0, 7), Is.EqualTo(ChessLogic.Knight));
            Assert.That(c.PlayUci("a7a8q"), Is.True);
            Assert.That(c.PieceAt(0, 7), Is.EqualTo(ChessLogic.Queen));
            Assert.That(c.Moves.Last(), Is.EqualTo("a7a8q"));
            Assert.That(c.SanMoves.Last(), Is.EqualTo("a8=Q+"));
            Assert.That(c.InCheck(), Is.True);
            Assert.That(c.UndoPlay(), Is.True);
            Assert.That(c.PieceAt(0, 6), Is.EqualTo(ChessLogic.Pawn), "undo turns it back into a pawn");
        }

        [Test]
        public void FoolsMateIsCheckmateForWhite()
        {
            var c = Play("f2f3", "e7e5", "g2g4");
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultRunning));
            Assert.That(c.PlayUci("d8h4"), Is.True);
            Assert.That(c.InCheck(), Is.True);
            Assert.That(c.LegalMoves(), Is.Empty);
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultCheckmate));
            Assert.That(c.ToMove, Is.EqualTo(ChessLogic.White), "the side to move lost");
            Assert.That(c.SanMoves, Is.EqualTo(new[] { "f3", "e5", "g4", "Qh4#" }));
            Assert.That(c.KingSquare(ChessLogic.White), Is.EqualTo(ChessLogic.ParseSquare("e1")));
        }

        [Test]
        public void StalemateIsADrawNotAMate()
        {
            var c = new ChessLogic("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
            Assert.That(c.InCheck(), Is.False);
            Assert.That(c.LegalMoves(), Is.Empty);
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultStalemate));
        }

        [Test]
        public void ThreefoldRepetitionIsADraw()
        {
            var c = new ChessLogic();
            var dance = new[] { "g1f3", "g8f6", "f3g1", "f6g8" };
            foreach (var m in dance) Assert.That(c.PlayUci(m), Is.True);
            Assert.That(c.RepetitionCount, Is.EqualTo(2));
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultRunning));
            foreach (var m in dance) Assert.That(c.PlayUci(m), Is.True);
            Assert.That(c.RepetitionCount, Is.EqualTo(3));
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultDrawRepetition));
            Assert.That(c.UndoPlay(), Is.True);
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultRunning), "undo forgets the count");
        }

        [Test]
        public void FiftyMovesAndDeadMaterialAreDraws()
        {
            var c = new ChessLogic("4k3/8/8/8/8/8/R7/4K3 w - - 99 80");
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultRunning));
            Assert.That(c.PlayUci("a2a3"), Is.True);
            Assert.That(c.Result(), Is.EqualTo(ChessLogic.ResultDraw50));

            Assert.That(new ChessLogic("8/8/8/4k3/8/8/8/4K3 w - - 0 1").Result(), Is.EqualTo(ChessLogic.ResultDrawMaterial), "K vs K");
            Assert.That(new ChessLogic("8/8/8/4k3/8/8/8/4K2N w - - 0 1").Result(), Is.EqualTo(ChessLogic.ResultDrawMaterial), "K+N vs K");
            Assert.That(new ChessLogic("8/8/8/4k1b1/8/8/8/2B1K3 w - - 0 1").Result(), Is.EqualTo(ChessLogic.ResultDrawMaterial), "bishops on the same colour");
            Assert.That(new ChessLogic("8/8/8/4kb2/8/8/8/2B1K3 w - - 0 1").Result(), Is.EqualTo(ChessLogic.ResultRunning), "bishops on different colours");
            Assert.That(new ChessLogic("8/8/8/4k3/8/8/P7/4K3 w - - 0 1").Result(), Is.EqualTo(ChessLogic.ResultRunning), "a pawn can still promote");
        }

        [Test]
        public void IllegalMovesAreRefusedAndUciIsCanonical()
        {
            var c = new ChessLogic();
            Assert.That(c.PlayUci("e2e5"), Is.False);
            Assert.That(c.PlayUci("e7e5"), Is.False, "not black's turn");
            Assert.That(c.PlayUci("zz"), Is.False);
            Assert.That(c.Moves, Is.Empty);
            Assert.That(ChessLogic.MoveToUci(c.UciToMove(" E2E4 ")), Is.EqualTo("e2e4"));
            // a pinned piece may not move
            var pin = new ChessLogic("4k3/4r3/8/8/8/8/4B3/4K3 w - - 0 1");
            Assert.That(pin.LegalMovesFrom(ChessLogic.ParseSquare("e2")), Is.Empty);
            // SAN disambiguation
            var two = new ChessLogic("4k3/8/8/8/8/8/4K3/R6R w - - 0 1");
            Assert.That(two.San(two.UciToMove("a1d1")), Is.EqualTo("Rad1"));
        }

        // ------------------------------------------------------------------ AI

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void AiFindsMateInOne(int strength)
        {
            var backRank = new ChessLogic("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
            Assert.That(new ChessAi(7).PickUci(backRank, strength), Is.EqualTo("a1a8"));
            var fool = Play("f2f3", "e7e5", "g2g4");
            var before = fool.ToFen();
            Assert.That(new ChessAi(7).PickUci(fool, strength), Is.EqualTo("d8h4"));
            Assert.That(fool.ToFen(), Is.EqualTo(before), "the search leaves the logic untouched");
        }

        [Test]
        public void AiIsDeterministicPerSeedAndHasNoMoveWhenMated()
        {
            var a = new ChessAi(new GoobyRng(42));
            var b = new ChessAi(new GoobyRng(42));
            var c = new ChessLogic();
            for (var i = 0; i < 6; i++)
            {
                var ma = a.PickUci(c, 1);
                Assert.That(b.PickUci(c, 1), Is.EqualTo(ma), "same seed ⇒ same move (ply " + i + ")");
                Assert.That(c.PlayUci(ma), Is.True);
            }
            var mated = Play("f2f3", "e7e5", "g2g4", "d8h4");
            Assert.That(new ChessAi().PickMove(mated, 3), Is.EqualTo(0));
        }

        [Test]
        public void AiTakesAHangingQueenAndAnswersQuickly()
        {
            var hanging = new ChessLogic("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1");
            Assert.That(new ChessAi(1).PickUci(hanging, 2), Is.EqualTo("d1d5"));
            foreach (var fen in new[] { ChessLogic.StartFen, Kiwipete, "r1bq1rk1/pp2bppp/2n1pn2/2pp4/3P4/2PBPN2/PP1N1PPP/R1BQ1RK1 w - - 0 8" })
            {
                var c = new ChessLogic(fen);
                var sw = Stopwatch.StartNew();
                var move = new ChessAi(3).PickMove(c, 3);
                sw.Stop();
                TestContext.Progress.WriteLine($"[chess] strength 3 on {fen.Split(' ')[0]}: {ChessLogic.MoveToUci(move)} in {sw.ElapsedMilliseconds} ms");
                Assert.That(move, Is.Not.EqualTo(0));
                // target < 0.5 s on a phone; generous here so a busy CI box does not flake
                Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2500));
            }
        }

        // ------------------------------------------------------------------ BoardClient (chess) on a fake transport

        private string _dir;
        private long _now;
        private List<FakeNetTransport> _fakes;
        private NetSession _s;
        private BoardClient _b;
        private FakeNetTransport Fake => _fakes.Last();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gooby-chess-" + Guid.NewGuid().ToString("N"));
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

        private static JObject StartPayload(string room, string first) => new JObject
        {
            ["room"] = room,
            ["game"] = "chess",
            ["seed"] = 99,
            ["first"] = first,
            ["players"] = new JArray
            {
                new JObject { ["friendCode"] = first, ["name"] = first == Me ? "Anna" : "Ben", ["goobyName"] = first == Me ? "Flausch" : "Bobby" },
                new JObject { ["friendCode"] = first == Me ? Opp : Me, ["name"] = first == Me ? "Ben" : "Anna", ["goobyName"] = first == Me ? "Bobby" : "Flausch" },
            },
        };

        private static JObject RoomMsg(string room, string kind, JObject body) => new JObject
        {
            ["room"] = room,
            ["kind"] = kind,
            ["body"] = body,
            ["from"] = new JObject { ["friendCode"] = Opp, ["goobyName"] = "Bobby" },
        };

        private void StartedChess(string first, string room = "board:c1")
        {
            Push("BOARD_START", StartPayload(room, first));
            Reply(Fake.LastOfType("ROOM_JOIN"), "OK", new JObject { ["room"] = room, ["members"] = new JArray(Me, Opp) });
            Assert.That(_b.PeerInRoom, Is.True);
        }

        /// <summary>We (white) move, the opponent acks.</summary>
        private void MyMove(string uci, int n)
        {
            Assert.That(_b.SendMove(uci), Is.EqualTo(""), "our move " + uci);
            var shot = LastRelayed("SHOT");
            Assert.That((int)shot["d"]["body"]["n"], Is.EqualTo(n));
            Assert.That((string)shot["d"]["body"]["move"], Is.EqualTo(uci));
            Push("ROOM_MSG", RoomMsg("board:c1", "SHOT_RESULT", new JObject { ["n"] = n }));
        }

        [Test]
        public void ChessInvitesCarryTheGameAndStartAChessTable()
        {
            Online();
            _b.Invite(Opp, BoardClient.GameChess, null);
            var inv = Fake.LastOfType("BOARD_INVITE");
            Assert.That((string)inv["d"]["game"], Is.EqualTo("chess"));
            Reply(inv, "OK", new JObject());
            Assert.That(_b.IsInvitePending(Opp, BoardClient.GameChess), Is.True);
            Assert.That(_b.IsInvitePending(Opp, BoardClient.GameBattleship), Is.False);
            Assert.That(_b.OutgoingInviteGame, Is.EqualTo("chess"));
            Push("BOARD_DECLINED", new JObject { ["from"] = Opp });
            Assert.That(_b.LastDeclinedGame, Is.EqualTo("chess"));
            Assert.That(_b.OutgoingInvite, Is.EqualTo(""));

            var started = 0;
            _b.GameStarted += () => started++;
            StartedChess(Opp);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(_b.IsChess, Is.True);
            Assert.That(_b.Game, Is.EqualTo("chess"));
            Assert.That(_b.Chess, Is.Not.Null);
            Assert.That(_b.MyColor, Is.EqualTo(ChessLogic.Black), "the inviter plays white");
            Assert.That(_b.MyTurn, Is.False);
            Assert.That(_b.Board, Is.Null, "no battleship board on a chess table");
            Assert.That(_b.LockFleet(), Is.False);
            Assert.That(_b.Shoot(new BattleshipCell(0, 0)), Is.EqualTo(BoardClient.ErrNoGame));
            Assert.That(_b.SendMove("e7e5"), Is.EqualTo(BoardClient.ErrNotYourTurn));
        }

        [Test]
        public void MovesTravelAsShotsAndTheOpponentsAreAutoAcked()
        {
            Online();
            StartedChess(Me);
            Assert.That(_b.MyColor, Is.EqualTo(ChessLogic.White));
            Assert.That(_b.MyTurn, Is.True);
            Assert.That(_b.SendMove("e2e5"), Is.EqualTo(BoardClient.ErrIllegalMove));
            Assert.That(_b.SendMove("e2e4"), Is.EqualTo(""));
            Assert.That(_b.AwaitingResult, Is.True);
            Assert.That(_b.MyTurn, Is.False);
            Assert.That(_b.SendMove("d2d4"), Is.EqualTo(BoardClient.ErrNotYourTurn));
            var shot = LastRelayed("SHOT");
            Assert.That((string)shot["d"]["body"]["move"], Is.EqualTo("e2e4"));
            Assert.That((int)shot["d"]["body"]["n"], Is.EqualTo(1));
            Push("ROOM_MSG", RoomMsg("board:c1", "SHOT_RESULT", new JObject { ["n"] = 1 }));
            Assert.That(_b.AwaitingResult, Is.False);
            Assert.That(_b.Turn.N, Is.EqualTo(2));
            Assert.That(_b.MyTurn, Is.False, "black to move");

            var moved = new List<string>();
            _b.OpponentMoved += u => moved.Add(u);
            Push("ROOM_MSG", RoomMsg("board:c1", "SHOT", new JObject { ["n"] = 2, ["move"] = "e7e5" }));
            Assert.That(moved, Is.EqualTo(new[] { "e7e5" }));
            var ack = LastRelayed("SHOT_RESULT");
            Assert.That((int)ack["d"]["body"]["n"], Is.EqualTo(2));
            Assert.That(_b.Turn.N, Is.EqualTo(3));
            Assert.That(_b.MyTurn, Is.True);
            Assert.That(_b.Chess.Moves, Is.EqualTo(new[] { "e2e4", "e7e5" }));

            // an illegal relayed move is ignored (no ack)
            var acks = Relayed("SHOT_RESULT").Count;
            Push("ROOM_MSG", RoomMsg("board:c1", "SHOT", new JObject { ["n"] = 4, ["move"] = "a7a5" }));
            Assert.That(Relayed("SHOT_RESULT").Count, Is.EqualTo(acks));

            // a server rejection takes our move back
            Assert.That(_b.SendMove("g1f3"), Is.EqualTo(""));
            Reply(LastRelayed("SHOT"), "ERROR", new JObject { ["code"] = "BAD_TURN_N" });
            Assert.That(_b.Chess.Moves.Count, Is.EqualTo(2));
            Assert.That(_b.MyTurn, Is.True);
            Assert.That(_b.MovesSent, Is.EqualTo(1));
        }

        [Test]
        public void TheReceiverOfTheMatingMoveWritesGameOver()
        {
            Online();
            StartedChess(Opp); // Ben is white and walks into the fool's mate
            var overs = new List<string>();
            _b.GameOver += (w, won, r) => overs.Add(w + ":" + won + ":" + r);
            Push("ROOM_MSG", RoomMsg("board:c1", "SHOT", new JObject { ["n"] = 1, ["move"] = "f2f3" }));
            MyMoveAs("e7e5", 2);
            Push("ROOM_MSG", RoomMsg("board:c1", "SHOT", new JObject { ["n"] = 3, ["move"] = "g2g4" }));
            Assert.That(_b.SendMove("d8h4"), Is.EqualTo(""));
            Assert.That(_b.Finished, Is.True, "the mover ends at once");
            Assert.That(_b.IWon, Is.True);
            Assert.That(_b.EndReason, Is.EqualTo(BoardEndReason.Checkmate));
            Assert.That(_b.ResultId, Is.EqualTo("checkmate"));
            Assert.That(Relayed("GAME_OVER"), Is.Empty, "the receiver writes GAME_OVER");
            Push("ROOM_MSG", RoomMsg("board:c1", "SHOT_RESULT", new JObject { ["n"] = 4 }));
            Push("ROOM_MSG", RoomMsg("board:c1", "GAME_OVER", new JObject { ["winner"] = Me, ["reason"] = "checkmate" }));
            Assert.That(overs, Is.EqualTo(new[] { Me + ":True:Checkmate" }), "only once");

            // the other way round: we receive the mate and report it
            Online();
            StartedChess(Me, "board:c2");
            overs.Clear();
            _b.GameOver += (w, won, r) => overs.Add(w + ":" + won + ":" + r);
            MyMoveIn("board:c2", "f2f3", 1);
            Push("ROOM_MSG", RoomMsg("board:c2", "SHOT", new JObject { ["n"] = 2, ["move"] = "e7e5" }));
            MyMoveIn("board:c2", "g2g4", 3);
            Push("ROOM_MSG", RoomMsg("board:c2", "SHOT", new JObject { ["n"] = 4, ["move"] = "d8h4" }));
            var over = LastRelayed("GAME_OVER");
            Assert.That(over, Is.Not.Null);
            Assert.That((string)over["d"]["body"]["winner"], Is.EqualTo(Opp));
            Assert.That((string)over["d"]["body"]["reason"], Is.EqualTo("checkmate"));
            var ackIdx = Fake.SentMessages().FindLastIndex(m => (string)m["t"] == "ROOM_MSG" && (string)m["d"]["kind"] == "SHOT_RESULT");
            var overIdx = Fake.SentMessages().FindLastIndex(m => (string)m["t"] == "ROOM_MSG" && (string)m["d"]["kind"] == "GAME_OVER");
            Assert.That(ackIdx, Is.LessThan(overIdx), "ack first, then GAME_OVER");
            Assert.That(overs, Is.EqualTo(new[] { Opp + ":False:Checkmate" }));
        }

        private void MyMoveAs(string uci, int n) => MyMoveIn("board:c1", uci, n);

        private void MyMoveIn(string room, string uci, int n)
        {
            Assert.That(_b.SendMove(uci), Is.EqualTo(""), "our move " + uci);
            Assert.That((int)LastRelayed("SHOT")["d"]["body"]["n"], Is.EqualTo(n));
            Push("ROOM_MSG", RoomMsg(room, "SHOT_RESULT", new JObject { ["n"] = n }));
        }

        [Test]
        public void SurrenderSendsTheReasonAndDrawsHaveNoWinner()
        {
            Online();
            StartedChess(Me);
            MyMove("e2e4", 1);
            Assert.That(_b.Surrender(), Is.True);
            var over = LastRelayed("GAME_OVER");
            Assert.That((string)over["d"]["body"]["winner"], Is.EqualTo(Opp));
            Assert.That((string)over["d"]["body"]["reason"], Is.EqualTo("surrender"));
            Assert.That(_b.EndReason, Is.EqualTo(BoardEndReason.Surrender));
            Assert.That(_b.IWon, Is.False);

            Online();
            StartedChess(Me, "board:c3");
            Push("ROOM_MSG", RoomMsg("board:c3", "GAME_OVER", new JObject { ["winner"] = Me, ["reason"] = "surrender" }));
            Assert.That(_b.IWon, Is.True);
            Assert.That(_b.ResultId, Is.EqualTo("surrender"));

            Online();
            StartedChess(Me, "board:c4");
            Push("ROOM_MSG", RoomMsg("board:c4", "GAME_OVER", new JObject { ["winner"] = "", ["reason"] = "draw_repetition" }));
            Assert.That(_b.Finished, Is.True);
            Assert.That(_b.IsDraw, Is.True);
            Assert.That(_b.EndReason, Is.EqualTo(BoardEndReason.DrawRepetition));
        }

        [Test]
        public void ResumeReplaysTheMovesAndAcksAnOpenOpponentMove()
        {
            Online();
            StartedChess(Me);
            MyMove("e2e4", 1);
            // link lost; on the reconnect the history is replayed
            Fake.ServerClose(1006, "");
            _s.Pump();
            _now += NetSession.ReconnectMaxMs + 1000;
            _s.Pump();
            Fake.Open();
            _s.Pump();
            Fake.Receive("WELCOME", new JObject { ["friendCode"] = Me, ["heartbeatSec"] = 20 }, (long)Fake.LastOfType("HELLO")["seq"]);
            _s.Pump();
            var resumed = 0;
            _b.GameResumed += () => resumed++;
            Push("BOARD_RESUME", new JObject
            {
                ["room"] = "board:c1",
                ["game"] = "chess",
                ["turn"] = Opp,
                ["n"] = 2,
                ["history"] = new JArray
                {
                    new JObject { ["kind"] = "SHOT", ["from"] = Me, ["body"] = new JObject { ["n"] = 1, ["move"] = "e2e4" } },
                    new JObject { ["kind"] = "SHOT_RESULT", ["from"] = Opp, ["body"] = new JObject { ["n"] = 1 } },
                    new JObject { ["kind"] = "SHOT", ["from"] = Opp, ["body"] = new JObject { ["n"] = 2, ["move"] = "c7c5" } },
                },
            });
            Assert.That(resumed, Is.EqualTo(1));
            Assert.That(_b.Chess.Moves, Is.EqualTo(new[] { "e2e4", "c7c5" }));
            var ack = LastRelayed("SHOT_RESULT");
            Assert.That((int)ack["d"]["body"]["n"], Is.EqualTo(2));
            Assert.That(_b.MyTurn, Is.True);
            Assert.That(_b.Turn.N, Is.EqualTo(3));
            Assert.That(_b.MovesSent, Is.EqualTo(1));
        }
    }
}
