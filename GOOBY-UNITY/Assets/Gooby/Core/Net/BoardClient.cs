using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gooby.Core.Minigames;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>A player of a board game (BOARD_START.players).</summary>
    public sealed class BoardPlayer
    {
        public string Code { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string GoobyName { get; internal set; } = "";
    }

    /// <summary>An incoming BOARD_INVITED (valid for <see cref="ExpiresInMs"/> after <see cref="ReceivedAtMs"/>).</summary>
    public sealed class BoardInvite
    {
        public string From { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string GoobyName { get; internal set; } = "";
        public string Game { get; internal set; } = "";
        public int ExpiresInMs { get; internal set; } = BoardClient.InviteTtlMs;
        public long ReceivedAtMs { get; internal set; }
        public long ExpiresAtMs => ReceivedAtMs + ExpiresInMs;
    }

    /// <summary>Why a game ended.</summary>
    public enum BoardEndReason
    {
        None,
        /// <summary>A whole fleet went down.</summary>
        AllSunk,
        /// <summary>Somebody gave up with GAME_OVER (see <see cref="BoardClient.Winner"/> for who won).</summary>
        Surrender,
        /// <summary>BOARD_FORFEIT: the opponent left the room or did not come back within the rejoin window.</summary>
        Forfeit,
        /// <summary>The server no longer knows the room (e.g. our own rejoin window ran out).</summary>
        Expired,
        /// <summary>Schach: checkmate (see <see cref="BoardClient.Winner"/>).</summary>
        Checkmate,
        /// <summary>Schach: stalemate — a draw, no winner.</summary>
        Stalemate,
        /// <summary>Schach: 50-move rule — a draw.</summary>
        DrawFiftyMoves,
        /// <summary>Schach: threefold repetition — a draw.</summary>
        DrawRepetition,
        /// <summary>Schach: not enough material to mate — a draw.</summary>
        DrawMaterial,
    }

    /// <summary>
    /// Client side of the server's board-game relay (GOOBY-SERVER boardgames.js; port of board_session.gd and
    /// chess_session.gd) for Schiffe versenken AND Schach (<see cref="Game"/>): invites (BOARD_INVITE → BOARD_INVITED, BOARD_ACCEPT → BOARD_START to both, BOARD_DECLINE),
    /// the board: room, turn relay with exact move numbers (SHOT {n, cell} by the player whose turn it is, SHOT_RESULT
    /// {n, hit, sunk} by the other side switches the turn), tomato (once per round), emotes, GAME_OVER, surrender,
    /// rematch, forfeit, peer down/up and resume after a reconnect (history replay).
    /// <para>
    /// Main-thread only, like <see cref="SocialClient"/>: it rides on <see cref="NetSession.Request"/>/<see cref="NetSession.Push"/>,
    /// so every event is raised from inside the session's Pump (or synchronously for immediate local failures).
    /// </para>
    /// <para>
    /// ROOM_MSG is fire-and-forget on the server (a relay has no OK) but rejections come back as ERROR with re — game
    /// messages therefore go out as requests with a short timeout: ERROR = rejected (<see cref="SendRejected"/>),
    /// TIMEOUT = relayed. Payloads stay Godot-compatible; SHOT_RESULT additionally carries cell/sunkCells/allSunk,
    /// which the Godot client ignores.
    /// </para>
    /// <para>
    /// Schach rides the same state machine (chess_session.gd): a move travels as SHOT {n, move:"e2e4"} ("e7e8q" for a
    /// promotion), the receiver auto-acks with SHOT_RESULT {n}, so the server's turn ownership, move numbers, history and
    /// resume replay work unchanged. White = <see cref="FirstCode"/> (the inviter; a rematch swaps colours). The rules
    /// (<see cref="ChessLogic"/>) are client-side: the RECEIVER of the last move writes GAME_OVER {winner, reason} into the
    /// server history after its ack (draw = winner ""), a surrender sends GAME_OVER {winner: opponent, reason:"surrender"}.
    /// </para>
    /// </summary>
    public sealed class BoardClient
    {
        public const string GameBattleship = "battleship";
        public const string GameChess = "chess";
        public const int InviteTtlMs = 30000;
        /// <summary>A relayed ROOM_MSG has no reply: silence for this long counts as delivered.</summary>
        public const int RelayAckTimeoutMs = 5000;

        // local reasons returned by Shoot() (server codes pass through SendRejected unchanged)
        public const string ErrNoGame = "NO_GAME";
        public const string ErrWaitingPeer = "WAITING_PEER";
        public const string ErrNotYourTurn = "NOT_YOUR_TURN";
        public const string ErrAlreadyShot = "ALREADY_SHOT";
        public const string ErrBadCell = "BAD_CELL";
        public const string ErrIllegalMove = "ILLEGAL_MOVE";

        /// <summary>Game ids the server accepts (boardgames.js GAMES).</summary>
        public static bool IsKnownGame(string game) => game == GameBattleship || game == GameChess;

        private readonly NetSession _session;
        private readonly Func<string> _myCode;
        private readonly Func<long> _now;
        private readonly List<BoardInvite> _incoming = new List<BoardInvite>();
        private readonly List<BoardPlayer> _players = new List<BoardPlayer>();
        private List<BattleshipShip> _draft = new List<BattleshipShip>();
        private List<BattleshipShip> _locked;
        private BattleshipCell _pendingShot = BattleshipCell.Invalid;
        private int _rerolls;
        private string _outgoing = "";
        private string _outgoingGame = "";
        private long _outgoingAtMs;
        private bool _chessPending, _chessOwnEnd;
        private int _chessPendingPly;

        public BoardClient(SocialClient social, Func<long> nowMs = null)
            : this(social?.Session, () => social?.FriendCode ?? "", nowMs)
        {
        }

        public BoardClient(NetSession session, Func<string> myCode, Func<long> nowMs = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _myCode = myCode ?? throw new ArgumentNullException(nameof(myCode));
            if (nowMs == null)
            {
                var sw = Stopwatch.StartNew();
                nowMs = () => sw.ElapsedMilliseconds;
            }
            _now = nowMs;
            _session.Push += OnPush;
            _session.Welcome += OnWelcome;
            _session.StatusChanged += OnStatus;
        }

        // ------------------------------------------------------------------ state

        public string MyCode => _myCode() ?? "";
        /// <summary>"" = no game (after Leave or before the first BOARD_START).</summary>
        public string RoomId { get; private set; } = "";
        public string Game { get; private set; } = "";
        public long Seed { get; private set; }
        public string FirstCode { get; private set; } = "";
        public IReadOnlyList<BoardPlayer> Players => _players;
        public string OpponentCode { get; private set; } = "";
        public string OpponentName { get; private set; } = "";
        public string OpponentGoobyName { get; private set; } = "";
        /// <summary>The running game is Schach (<see cref="Chess"/> holds the position).</summary>
        public bool IsChess => Game == GameChess;

        /// <summary>Schach: the shared position (null for Schiffe versenken / no game).</summary>
        public ChessLogic Chess { get; private set; }
        /// <summary>Schach: our colour — <see cref="ChessLogic.White"/> when we begin (<see cref="FirstCode"/>).</summary>
        public int MyColor { get; private set; } = ChessLogic.White;
        /// <summary>Schach: our moves sent in this game.</summary>
        public int MovesSent { get; private set; }
        /// <summary>Schach: our move is out, the opponent's ack (SHOT_RESULT) is pending.</summary>
        public bool AwaitingAck => IsActive && IsChess && _chessPending;
        /// <summary>Why the game ended as the Godot/strings id: checkmate, stalemate, draw_50, draw_repetition, draw_material, surrender, forfeit, expired, all_sunk ("" while running).</summary>
        public string ResultId => ReasonId(EndReason);
        /// <summary>Finished without a winner (stalemate / draw rules).</summary>
        public bool IsDraw => Finished && Winner.Length == 0 && IsDrawReason(EndReason);

        public BattleshipBoard Board { get; private set; }
        public BattleshipTracker Tracker { get; private set; }
        public BattleshipTurn Turn { get; private set; }
        public TomatoTracker Tomatoes { get; } = new TomatoTracker();

        /// <summary>The fleet being placed (setup); after <see cref="LockFleet"/> the locked one.</summary>
        public IReadOnlyList<BattleshipShip> Fleet => FleetLocked ? (IReadOnlyList<BattleshipShip>)Board.Ships : _draft;
        public bool FleetLocked { get; private set; }
        public int Rerolls => _rerolls;

        /// <summary>True once our ROOM_JOIN of the board room succeeded (again after reconnects).</summary>
        public bool Joined { get; private set; }
        /// <summary>The opponent is in the room — shots are only sent then (a relay to an empty seat would be lost).</summary>
        public bool PeerInRoom { get; private set; }
        /// <summary>BOARD_PEER_DOWN received (until PEER_UP / PEER_JOINED).</summary>
        public bool PeerDown { get; private set; }
        /// <summary>The opponent left for good (rematch impossible).</summary>
        public bool OpponentGone { get; private set; }

        public bool Finished { get; private set; }
        public string Winner { get; private set; } = "";
        public bool IWon => Finished && Winner.Length > 0 && Winner == MyCode;
        public BoardEndReason EndReason { get; private set; } = BoardEndReason.None;
        public bool RematchMine { get; private set; }
        public bool RematchTheirs { get; private set; }
        /// <summary>Target of our last successful BOARD_INVITE ("" when none or after the 30 s the server keeps it).</summary>
        public string OutgoingInvite
        {
            get
            {
                if (_outgoing.Length > 0 && _now() - _outgoingAtMs >= InviteTtlMs) _outgoing = "";
                return _outgoing;
            }
            private set
            {
                _outgoing = value ?? "";
                _outgoingAtMs = _now();
            }
        }

        /// <summary>Game of <see cref="OutgoingInvite"/> ("" when none).</summary>
        public string OutgoingInviteGame => OutgoingInvite.Length > 0 ? _outgoingGame : "";
        /// <summary>Game of the invite that was declined last (for the toast of <see cref="InviteDeclined"/>).</summary>
        public string LastDeclinedGame { get; private set; } = "";

        /// <summary>Our invite to <paramref name="code"/> is still open (sent &lt; 30 s ago, no answer yet).</summary>
        public bool IsInvitePending(string code) => OutgoingInvite.Length > 0 && OutgoingInvite == Upper(code);

        /// <summary>Our invite to <paramref name="code"/> for <paramref name="game"/> is still open.</summary>
        public bool IsInvitePending(string code, string game) => IsInvitePending(code) && _outgoingGame == game;
        /// <summary>Shots we fired in this game (for the results card).</summary>
        public int ShotsFired { get; private set; }
        /// <summary>Our answered shots that hit (results card).</summary>
        public int HitsLanded => Tracker?.HitCount ?? 0;
        /// <summary>Enemy ships we sank.</summary>
        public int ShipsSunk => Tracker?.SunkCount ?? 0;
        /// <summary>Own ships still afloat (the whole fleet before it is locked).</summary>
        public int ShipsLeft => Board != null && Board.IsReady ? Board.RemainingShips : BattleshipLogic.Fleet.Length;
        /// <summary>Hit rate of our answered shots in percent (0 before the first answer).</summary>
        public int AccuracyPercent => Tracker == null || Tracker.ShotCount == 0 ? 0 : (int)Math.Round(100.0 * Tracker.HitCount / Tracker.ShotCount);
        /// <summary>The cell we shot and wait for the answer of (Invalid when none).</summary>
        public BattleshipCell PendingShot => _pendingShot;
        /// <summary>Last cell the opponent shot at (own board), Invalid before the first shot.</summary>
        public BattleshipCell LastIncoming { get; private set; } = BattleshipCell.Invalid;
        /// <summary>Last cell we shot at that got its answer (enemy board).</summary>
        public BattleshipCell LastOutgoing { get; private set; } = BattleshipCell.Invalid;

        public bool HasGame => RoomId.Length > 0;
        public bool IsActive => HasGame && !Finished;
        public bool MyTurn => IsActive && Turn != null && Turn.CanShoot(MyCode) && (!IsChess || (Chess != null && Chess.ToMove == MyColor && !_chessPending));
        /// <summary>Our shot (or chess move) is out, the answer is pending.</summary>
        public bool AwaitingResult => IsActive && (_pendingShot.InBounds || (IsChess && _chessPending));

        /// <summary>Open incoming invites (expired ones are dropped on read).</summary>
        public IReadOnlyList<BoardInvite> IncomingInvites
        {
            get
            {
                var now = _now();
                _incoming.RemoveAll(i => now >= i.ExpiresAtMs);
                return _incoming;
            }
        }

        // ------------------------------------------------------------------ events

        /// <summary>Any state change — refresh the UI.</summary>
        public event Action Changed;
        public event Action<BoardInvite> InviteIncoming;
        /// <summary>(byCode): our invite was declined.</summary>
        public event Action<string> InviteDeclined;
        /// <summary>A new game started (first game or rematch); state is reset, the room join is on its way.</summary>
        public event Action GameStarted;
        /// <summary>Boards were rebuilt from the server history after a reconnect.</summary>
        public event Action GameResumed;
        /// <summary>The opponent sat down at the table (ROOM_PEER_JOINED or already in the room on our join).</summary>
        public event Action PeerJoined;
        /// <summary>The opponent left the room (after the game this means: no rematch).</summary>
        public event Action PeerLeft;
        /// <summary>The fleet got locked without "Bereit!" (first incoming shot, or our first shot).</summary>
        public event Action FleetAutoLocked;
        /// <summary>(cell, outcome): the opponent shot at our board; the answer is already sent.</summary>
        public event Action<BattleshipCell, ShotOutcome> OpponentShot;
        /// <summary>(cell, hit, sunk, sunkCells): answer to our shot.</summary>
        public event Action<BattleshipCell, bool, bool, IReadOnlyList<BattleshipCell>> ShotResult;
        /// <summary>Schach: (uci) the opponent's move is applied to <see cref="Chess"/> and acked.</summary>
        public event Action<string> OpponentMoved;
        public event Action<string> OpponentEmote;
        public event Action TomatoIncoming;
        public event Action<string> TomatoRejected;
        /// <summary>(kind, code): the server rejected one of our room messages (NOT_YOUR_TURN, BAD_TURN_N, TOMATO_LIMIT…).</summary>
        public event Action<string, string> SendRejected;
        /// <summary>(winnerCode, iWon, reason).</summary>
        public event Action<string, bool, BoardEndReason> GameOver;
        public event Action RematchRequestedByOpponent;
        public event Action RematchDeclined;
        /// <summary>(down, waitMs): the opponent lost / regained the connection.</summary>
        public event Action<bool, int> PeerConnectionChanged;

        // ------------------------------------------------------------------ invites

        /// <summary>BOARD_INVITE {target, game:"battleship"}; Ok = delivered, the start comes as BOARD_START (GameStarted).</summary>
        public void Invite(string targetCode, Action<NetReply> cb) => Invite(targetCode, GameBattleship, cb);

        /// <summary>BOARD_INVITE {target, game} ("battleship" | "chess"); Ok = delivered, the start comes as BOARD_START.</summary>
        public void Invite(string targetCode, string game, Action<NetReply> cb)
        {
            var target = Upper(targetCode);
            var g = IsKnownGame(game) ? game : GameBattleship;
            _session.Request("BOARD_INVITE", new JObject { ["target"] = target, ["game"] = g }, r =>
            {
                if (r.Ok)
                {
                    OutgoingInvite = target;
                    _outgoingGame = g;
                }
                Raise(() => cb?.Invoke(r), "Invite cb");
                MarkChanged();
            });
        }

        /// <summary>BOARD_ACCEPT {from}; the reply IS the BOARD_START (GameStarted fires before <paramref name="cb"/>).</summary>
        public void Accept(string fromCode, Action<NetReply> cb)
        {
            var from = Upper(fromCode);
            RemoveInvite(from);
            _session.Request("BOARD_ACCEPT", new JObject { ["from"] = from }, r =>
            {
                if (r.Ok && r.Type == "BOARD_START") StartGame(r.Data);
                Raise(() => cb?.Invoke(r), "Accept cb");
                MarkChanged();
            });
        }

        public void Decline(string fromCode, Action<NetReply> cb = null)
        {
            var from = Upper(fromCode);
            RemoveInvite(from);
            _session.Request("BOARD_DECLINE", new JObject { ["from"] = from }, r => Raise(() => cb?.Invoke(r), "Decline cb"));
            MarkChanged();
        }

        public BoardInvite FindInvite(string fromCode)
        {
            var from = Upper(fromCode);
            foreach (var i in IncomingInvites) if (i.From == from) return i;
            return null;
        }

        private void RemoveInvite(string from) => _incoming.RemoveAll(i => i.From == from);

        // ------------------------------------------------------------------ setup

        /// <summary>"Neu würfeln": the next deterministic fleet (seed + n·7919, like Godot). False once locked.</summary>
        public bool RerollFleet()
        {
            if (!HasGame || IsChess || FleetLocked) return false;
            _rerolls++;
            _draft = BattleshipLogic.AutoFleet(BattleshipLogic.RerollSeed(Seed, MyCode, _rerolls));
            MarkChanged();
            return true;
        }

        /// <summary>Setup: turns the ship at <paramref name="cell"/> by 90° when it still fits. False = nothing changed.</summary>
        public bool RotateShipAt(BattleshipCell cell)
        {
            if (!HasGame || IsChess || FleetLocked) return false;
            var turned = BattleshipLogic.RotateAt(_draft, cell);
            if (turned == null) return false;
            _draft = turned;
            MarkChanged();
            return true;
        }

        /// <summary>Replaces the draft with an own placement (validated). False when invalid or already locked.</summary>
        public bool SetDraftFleet(IList<BattleshipShip> fleet)
        {
            if (!HasGame || IsChess || FleetLocked || BattleshipLogic.ValidateFleet(fleet).Length > 0) return false;
            _draft = new List<BattleshipShip>(fleet);
            MarkChanged();
            return true;
        }

        /// <summary>"Bereit!": the draft (or <paramref name="fleet"/>) becomes the own board. Shots arriving earlier lock it too.</summary>
        public bool LockFleet(IList<BattleshipShip> fleet = null)
        {
            if (!HasGame || IsChess || FleetLocked) return false;
            var f = fleet != null ? new List<BattleshipShip>(fleet) : _draft;
            if (Board == null) Board = new BattleshipBoard();
            if (!Board.Setup(f)) return false;
            _locked = new List<BattleshipShip>(f);
            FleetLocked = true;
            MarkChanged();
            return true;
        }

        // ------------------------------------------------------------------ play

        /// <summary>
        /// Shoots at the enemy board. "" = sent (the answer arrives as <see cref="ShotResult"/>), else a local reason:
        /// NO_GAME, OFFLINE, WAITING_PEER, NOT_YOUR_TURN (also while our shot is pending), ALREADY_SHOT, BAD_CELL.
        /// An unlocked fleet is locked first.
        /// </summary>
        public string Shoot(BattleshipCell cell)
        {
            if (!IsActive || Turn == null || Tracker == null) return ErrNoGame;
            if (!_session.IsOnline) return NetErrorCodes.Offline;
            if (!cell.InBounds) return ErrBadCell;
            if (!PeerInRoom) return ErrWaitingPeer;
            if (!Turn.CanShoot(MyCode)) return ErrNotYourTurn;
            if (!Tracker.IsNewTarget(cell)) return ErrAlreadyShot;
            if (!FleetLocked && LockFleet()) Raise(() => FleetAutoLocked?.Invoke(), "FleetAutoLocked");
            var n = Turn.N;
            var room = RoomId;
            SendRelay("SHOT", new JObject { ["n"] = n, ["cell"] = cell.Ref }, code =>
            {
                // the server kept its state → turn our mirror back
                if (RoomId != room || _pendingShot != cell) return;
                _pendingShot = BattleshipCell.Invalid;
                if (Turn.Phase == BattleshipTurn.PhaseResult) Turn.Phase = BattleshipTurn.PhaseShot;
                ShotsFired = Math.Max(0, ShotsFired - 1);
            });
            _pendingShot = cell;
            Turn.OnShot();
            ShotsFired++;
            MarkChanged();
            return "";
        }

        /// <summary>
        /// Schach: plays our move (UCI "e2e4", promotion "e7e8q") on <see cref="Chess"/> and relays it as SHOT {n, move}.
        /// "" = sent (the ack arrives as SHOT_RESULT), else a local reason: NO_GAME, OFFLINE, WAITING_PEER, NOT_YOUR_TURN
        /// (also while our move waits for its ack), ILLEGAL_MOVE. A server rejection takes the move back
        /// (<see cref="SendRejected"/> "SHOT"). Mate/draw by our move ends the game locally at once — the opponent writes
        /// the GAME_OVER into the server history after its ack.
        /// </summary>
        public string SendMove(string uci)
        {
            if (!IsActive || !IsChess || Chess == null || Turn == null) return ErrNoGame;
            if (!_session.IsOnline) return NetErrorCodes.Offline;
            if (!PeerInRoom) return ErrWaitingPeer;
            if (!MyTurn) return ErrNotYourTurn;
            var m = Chess.UciToMove(uci);
            if (m == 0) return ErrIllegalMove;
            var move = ChessLogic.MoveToUci(m);
            var n = Turn.N;
            var room = RoomId;
            var chess = Chess;
            if (!SendRelay("SHOT", new JObject { ["n"] = n, ["move"] = move }, code =>
            {
                // the server kept its state → take our move back
                if (RoomId != room || Chess != chess || !_chessPending || chess.Moves.Count != _chessPendingPly) return;
                chess.UndoPlay();
                _chessPending = false;
                if (Turn.Phase == BattleshipTurn.PhaseResult) Turn.Phase = BattleshipTurn.PhaseShot;
                MovesSent = Math.Max(0, MovesSent - 1);
                if (Finished && _chessOwnEnd)
                {
                    Finished = false;
                    Winner = "";
                    EndReason = BoardEndReason.None;
                    _chessOwnEnd = false;
                }
            })) return NetErrorCodes.Offline;
            chess.PlayMove(m);
            _chessPending = true;
            _chessPendingPly = chess.Moves.Count;
            Turn.OnShot();
            MovesSent++;
            if (!CheckChessEnd(true)) MarkChanged();
            return "";
        }

        /// <summary>Emote to the opponent (dance/angry/laugh/sleep/selfie) — also after the game.</summary>
        public bool SendEmote(string id)
        {
            if (!HasGame || !BoardEmotes.IsValid(id)) return false;
            return SendRelay("EMOTE", new JObject { ["id"] = id });
        }

        /// <summary>Tomato (once per round — the client check only mirrors the server's TOMATO_LIMIT).</summary>
        public bool ThrowTomato()
        {
            if (!IsActive || Turn == null) return false;
            var round = Turn.RoundIndex;
            if (!Tomatoes.CanThrow(round)) return false;
            if (!SendRelay("TOMATO", new JObject(), code => { if (code != "TOMATO_LIMIT") Tomatoes.Reset(); })) return false;
            Tomatoes.MarkThrown(round);
            MarkChanged();
            return true;
        }

        public bool CanThrowTomato => IsActive && Turn != null && Tomatoes.CanThrow(Turn.RoundIndex);

        /// <summary>
        /// Gives up mid-game: GAME_OVER {winner: opponent} goes through the server history, the opponent wins at once;
        /// we stay in the room so a rematch is possible. False when no game is running.
        /// </summary>
        public bool Surrender()
        {
            if (!IsActive || OpponentCode.Length == 0) return false;
            var winner = OpponentCode;
            var body = new JObject { ["winner"] = winner };
            if (IsChess) body["reason"] = ReasonId(BoardEndReason.Surrender); // chess_session.gd surrender()
            SendRelay("GAME_OVER", body);
            Finish(winner, BoardEndReason.Surrender);
            return true;
        }

        /// <summary>
        /// Rematch (after GAME_OVER, both still in the room): Ok + Data.waiting=true = the opponent must agree;
        /// when both want it the reply is BOARD_START (GameStarted fires, the one who did not start last time begins).
        /// </summary>
        public void RequestRematch(Action<NetReply> cb)
        {
            if (!HasGame || !Finished)
            {
                Raise(() => cb?.Invoke(NetReply.Fail(ErrNoGame)), "RequestRematch cb");
                return;
            }
            RematchMine = true;
            MarkChanged();
            var room = RoomId;
            _session.Request("BOARD_REMATCH", new JObject { ["room"] = room }, r =>
            {
                if (r.Ok && r.Type == "BOARD_START") StartGame(r.Data);
                else if (!r.Ok && RoomId == room) RematchMine = false;
                Raise(() => cb?.Invoke(r), "RequestRematch cb");
                MarkChanged();
            });
        }

        /// <summary>Leaves the table. Mid-game the server scores it as a forfeit for the opponent (intended).</summary>
        public void Leave(Action<NetReply> cb = null)
        {
            if (!HasGame)
            {
                Raise(() => cb?.Invoke(NetReply.Success("OK")), "Leave cb");
                return;
            }
            var room = RoomId;
            ResetGame();
            MarkChanged();
            _session.Request("ROOM_LEAVE", new JObject { ["room"] = room }, r => Raise(() => cb?.Invoke(r), "Leave cb"));
        }

        /// <summary>
        /// Generic relay into the board room with error correlation (EMOTE, TOMATO … or raw SHOT for tests):
        /// false when there is no room or we are offline; a server rejection raises <see cref="SendRejected"/>.
        /// </summary>
        public bool SendRoomMessage(string kind, JObject body) => SendRelay(kind, body);

        private bool SendRelay(string kind, JObject body, Action<string> onRejected = null)
        {
            if (!HasGame || !_session.IsOnline) return false;
            var room = RoomId;
            _session.Request("ROOM_MSG", new JObject { ["room"] = room, ["kind"] = kind, ["body"] = body ?? new JObject() }, r =>
            {
                // no ERROR = relayed; OFFLINE = link lost (the resume replays the server history)
                if (r.Ok || r.Code == NetErrorCodes.Timeout || r.Code == NetErrorCodes.Offline) return;
                if (RoomId != room) return;
                onRejected?.Invoke(r.Code);
                if (kind == "TOMATO") Raise(() => TomatoRejected?.Invoke(r.Code), "TomatoRejected");
                Raise(() => SendRejected?.Invoke(kind, r.Code), "SendRejected");
                MarkChanged();
            }, RelayAckTimeoutMs);
            return true;
        }

        // ------------------------------------------------------------------ server → client

        private void OnPush(string type, JObject d)
        {
            switch (type)
            {
                case "BOARD_INVITED":
                {
                    var game = NetJson.Str(d["game"], GameBattleship);
                    if (!IsKnownGame(game)) return; // a game this client cannot play
                    var inv = new BoardInvite
                    {
                        From = Upper(NetJson.Str(d["from"])),
                        Name = NetJson.Str(d["name"]),
                        GoobyName = NetJson.Str(d["goobyName"], "Gooby"),
                        Game = game,
                        ExpiresInMs = Math.Max(1000, NetJson.Int(d["expiresInMs"], InviteTtlMs)),
                        ReceivedAtMs = _now(),
                    };
                    if (inv.From.Length == 0) return;
                    RemoveInvite(inv.From);
                    _incoming.Add(inv);
                    Raise(() => InviteIncoming?.Invoke(inv), "InviteIncoming");
                    MarkChanged();
                    break;
                }
                case "BOARD_DECLINED":
                {
                    var by = Upper(NetJson.Str(d["from"]));
                    LastDeclinedGame = OutgoingInvite == by && _outgoingGame.Length > 0 ? _outgoingGame : GameBattleship;
                    if (OutgoingInvite == by) OutgoingInvite = "";
                    Raise(() => InviteDeclined?.Invoke(by), "InviteDeclined");
                    MarkChanged();
                    break;
                }
                case "BOARD_START":
                    if (IsKnownGame(NetJson.Str(d["game"], GameBattleship))) StartGame(d);
                    break;
                case "BOARD_RESUME":
                    if (HasGame && NetJson.Str(d["room"]) == RoomId) Resume(d);
                    break;
                case "BOARD_FORFEIT":
                    if (!HasGame || NetJson.Str(d["room"]) != RoomId) return;
                    OpponentGone = true;
                    PeerInRoom = false;
                    PeerDown = false;
                    if (!Finished) Finish(NetJson.Str(d["winner"]), BoardEndReason.Forfeit);
                    else MarkChanged();
                    break;
                case "BOARD_REMATCH_WAIT":
                    if (!HasGame || NetJson.Str(d["room"]) != RoomId) return;
                    RematchTheirs = true;
                    Raise(() => RematchRequestedByOpponent?.Invoke(), "RematchRequestedByOpponent");
                    MarkChanged();
                    break;
                case "BOARD_REMATCH_DECLINED":
                    if (!HasGame || NetJson.Str(d["room"]) != RoomId) return;
                    RematchMine = false;
                    RematchTheirs = false;
                    OpponentGone = true;
                    Raise(() => RematchDeclined?.Invoke(), "RematchDeclined");
                    MarkChanged();
                    break;
                case "BOARD_PEER_DOWN":
                    if (!HasGame || NetJson.Str(d["room"]) != RoomId) return;
                    PeerDown = true;
                    PeerInRoom = false;
                    var wait = NetJson.Int(d["waitMs"]);
                    Raise(() => PeerConnectionChanged?.Invoke(true, wait), "PeerConnectionChanged");
                    MarkChanged();
                    break;
                case "BOARD_PEER_UP":
                    if (!HasGame || NetJson.Str(d["room"]) != RoomId) return;
                    PeerDown = false;
                    Raise(() => PeerConnectionChanged?.Invoke(false, 0), "PeerConnectionChanged");
                    MarkChanged();
                    break;
                case "ROOM_PEER_JOINED":
                {
                    if (!HasGame || NetJson.Str(d["room"]) != RoomId) return;
                    var code = NetJson.Str(d["friendCode"]);
                    if (code.Length == 0 || code == MyCode) return;
                    SetPeerInRoom(true);
                    break;
                }
                case "ROOM_PEER_LEFT":
                {
                    if (!HasGame || NetJson.Str(d["room"]) != RoomId) return;
                    if (NetJson.Str(d["friendCode"]) == MyCode) return;
                    PeerInRoom = false;
                    if (Finished) OpponentGone = true; // after the game: no rematch any more
                    Raise(() => PeerLeft?.Invoke(), "PeerLeft");
                    MarkChanged();
                    break;
                }
                case "ROOM_MSG":
                    if (HasGame && NetJson.Str(d["room"]) == RoomId)
                        OnRoomMessage(NetJson.Str(d["kind"]), d["body"] as JObject ?? new JObject());
                    break;
            }
        }

        private void OnRoomMessage(string kind, JObject body)
        {
            if (IsChess && OnChessMessage(kind, body)) return;
            switch (kind)
            {
                case "SHOT":
                    AnswerShot(NetJson.Int(body["n"]), BattleshipLogic.RefToCell(NetJson.Str(body["cell"])));
                    break;
                case "SHOT_RESULT":
                    ApplyShotResult(body);
                    break;
                case "EMOTE":
                {
                    var id = NetJson.Str(body["id"]);
                    if (id.Length > 0) Raise(() => OpponentEmote?.Invoke(id), "OpponentEmote");
                    break;
                }
                case "TOMATO":
                    Raise(() => TomatoIncoming?.Invoke(), "TomatoIncoming");
                    break;
                case "GAME_OVER":
                {
                    if (Finished) return;
                    var winner = NetJson.Str(body["winner"]);
                    // the winner of a sunk-out game already ended it locally; a GAME_OVER naming US is a surrender
                    Finish(winner, winner == MyCode ? BoardEndReason.Surrender : BoardEndReason.AllSunk);
                    break;
                }
            }
        }

        /// <summary>The opponent shot → evaluate the OWN board and answer SHOT_RESULT with the same n.</summary>
        private void AnswerShot(int n, BattleshipCell cell)
        {
            if (Turn == null || Finished) return;
            if (!FleetLocked && LockFleet()) Raise(() => FleetAutoLocked?.Invoke(), "FleetAutoLocked");
            if (Board == null || !Board.IsReady) return;
            // the server only relays a SHOT of the player whose turn it is: take its n and owner as the truth
            Turn.TurnCode = OpponentCode.Length > 0 ? OpponentCode : Turn.TurnCode;
            Turn.N = n;
            Turn.OnShot();
            var outcome = Board.ReceiveShot(cell);
            var result = new JObject { ["n"] = n, ["hit"] = outcome.Hit, ["sunk"] = outcome.Sunk, ["cell"] = cell.Ref };
            if (outcome.Sunk) result["sunkCells"] = BattleshipLogic.CellsToJson(outcome.SunkCells);
            if (outcome.AllSunk) result["allSunk"] = true;
            SendRelay("SHOT_RESULT", result);
            Turn.OnResult();
            LastIncoming = cell;
            Raise(() => OpponentShot?.Invoke(cell, outcome), "OpponentShot");
            if (outcome.AllSunk) Finish(OpponentCode, BoardEndReason.AllSunk); // the winner sends GAME_OVER
            else MarkChanged();
        }

        /// <summary>Answer to OUR shot.</summary>
        private void ApplyShotResult(JObject body)
        {
            if (Tracker == null || Turn == null || !_pendingShot.InBounds) return;
            var cell = _pendingShot;
            _pendingShot = BattleshipCell.Invalid;
            var hit = NetJson.Bool(body["hit"]);
            var sunk = NetJson.Bool(body["sunk"]);
            var sent = sunk ? BattleshipLogic.CellsFromJson(body["sunkCells"]) : new List<BattleshipCell>();
            // Record marks the ship (Unity peers send sunkCells, for Godot peers the straight run of hits is inferred)
            var sunkCells = Tracker.Record(cell, hit, sunk, sent);
            // our shot was the one of this exchange: the turn passes to the opponent whatever our mirror thought
            Turn.TurnCode = MyCode;
            Turn.OnResult();
            LastOutgoing = cell;
            IReadOnlyList<BattleshipCell> shown = sunkCells;
            Raise(() => ShotResult?.Invoke(cell, hit, sunk, shown), "ShotResult");
            if (!Finished && (Tracker.HasWon || NetJson.Bool(body["allSunk"])))
            {
                var me = MyCode;
                SendRelay("GAME_OVER", new JObject { ["winner"] = me });
                Finish(me, BoardEndReason.AllSunk);
            }
            else MarkChanged();
        }

        // ------------------------------------------------------------------ chess

        /// <summary>SHOT / SHOT_RESULT / GAME_OVER of a chess table (true = handled; emotes + tomatoes stay shared).</summary>
        private bool OnChessMessage(string kind, JObject body)
        {
            switch (kind)
            {
                case "SHOT":
                    ApplyOpponentMove(NetJson.Int(body["n"]), NetJson.Str(body["move"]));
                    return true;
                case "SHOT_RESULT":
                    if (!_chessPending || Turn == null) return true;
                    _chessPending = false;
                    Turn.TurnCode = MyCode;
                    Turn.N = NetJson.Int(body["n"], Turn.N);
                    Turn.OnResult();
                    MarkChanged();
                    return true;
                case "GAME_OVER":
                {
                    if (Finished) return true;
                    var winner = NetJson.Str(body["winner"]);
                    Finish(winner, ChessReason(NetJson.Str(body["reason"]), winner));
                    return true;
                }
            }
            return false;
        }

        /// <summary>The opponent's move: apply it, auto-ack SHOT_RESULT {n} and check for mate/draw (then WE send GAME_OVER).</summary>
        private void ApplyOpponentMove(int n, string uci)
        {
            if (Chess == null || Turn == null || Finished) return;
            if (Chess.ToMove == MyColor) return; // the server only relays the mover's SHOT — ignore anything else
            var m = Chess.UciToMove(uci);
            if (m == 0 || !Chess.PlayMove(m))
            {
                NetLog.Warn("[board] illegal chess move from the opponent: " + uci);
                return;
            }
            Turn.TurnCode = OpponentCode.Length > 0 ? OpponentCode : Turn.TurnCode;
            Turn.N = n;
            Turn.OnShot();
            SendRelay("SHOT_RESULT", new JObject { ["n"] = n });
            Turn.OnResult();
            var played = ChessLogic.MoveToUci(m);
            Raise(() => OpponentMoved?.Invoke(played), "OpponentMoved");
            if (!CheckChessEnd(false)) MarkChanged();
        }

        /// <summary>
        /// After every applied move: mate / stalemate / draw? The receiver of the last move (<paramref name="iMoved"/> false)
        /// writes GAME_OVER {winner, reason} into the server history (its ack is already out), exactly like chess_session.gd.
        /// </summary>
        private bool CheckChessEnd(bool iMoved)
        {
            if (Chess == null || Finished) return false;
            var res = Chess.Result();
            if (res.Length == 0) return false;
            var winner = "";
            if (res == ChessLogic.ResultCheckmate) winner = -Chess.ToMove == MyColor ? MyCode : OpponentCode;
            if (!iMoved) SendRelay("GAME_OVER", new JObject { ["winner"] = winner, ["reason"] = res });
            _chessOwnEnd = iMoved;
            Finish(winner, ChessReason(res, winner));
            return true;
        }

        /// <summary>Godot reason id (GAME_OVER.reason) → <see cref="BoardEndReason"/>; unknown ids fall back on the winner.</summary>
        public BoardEndReason ChessReason(string id, string winner)
        {
            switch (id ?? "")
            {
                case ChessLogic.ResultCheckmate: return BoardEndReason.Checkmate;
                case ChessLogic.ResultStalemate: return BoardEndReason.Stalemate;
                case ChessLogic.ResultDraw50: return BoardEndReason.DrawFiftyMoves;
                case ChessLogic.ResultDrawRepetition: return BoardEndReason.DrawRepetition;
                case ChessLogic.ResultDrawMaterial: return BoardEndReason.DrawMaterial;
                case "surrender": return BoardEndReason.Surrender;
                case "forfeit": return BoardEndReason.Forfeit;
            }
            if (string.IsNullOrEmpty(winner)) return BoardEndReason.Stalemate;
            return winner == MyCode ? BoardEndReason.Surrender : BoardEndReason.Checkmate;
        }

        /// <summary>Reason id for UI keys / GAME_OVER bodies (chess.reason.*).</summary>
        public static string ReasonId(BoardEndReason r)
        {
            switch (r)
            {
                case BoardEndReason.AllSunk: return "all_sunk";
                case BoardEndReason.Surrender: return "surrender";
                case BoardEndReason.Forfeit: return "forfeit";
                case BoardEndReason.Expired: return "expired";
                case BoardEndReason.Checkmate: return ChessLogic.ResultCheckmate;
                case BoardEndReason.Stalemate: return ChessLogic.ResultStalemate;
                case BoardEndReason.DrawFiftyMoves: return ChessLogic.ResultDraw50;
                case BoardEndReason.DrawRepetition: return ChessLogic.ResultDrawRepetition;
                case BoardEndReason.DrawMaterial: return ChessLogic.ResultDrawMaterial;
                default: return "";
            }
        }

        public static bool IsDrawReason(BoardEndReason r) =>
            r == BoardEndReason.Stalemate || r == BoardEndReason.DrawFiftyMoves || r == BoardEndReason.DrawRepetition || r == BoardEndReason.DrawMaterial;

        /// <summary>BOARD_RESUME of a chess table: replay every move of the history on a fresh board, take turn/n from the server, ack an open opponent move.</summary>
        private void ResumeChess(JObject d)
        {
            var me = MyCode;
            var codes = new List<string>();
            foreach (var p in _players) codes.Add(p.Code);
            Chess = new ChessLogic();
            Turn = new BattleshipTurn(FirstCode, codes);
            Tomatoes.Reset();
            MovesSent = 0;
            var pendingFrom = "";
            var pendingN = 0;
            if (d["history"] is JArray history)
            {
                foreach (var t in history)
                {
                    if (!(t is JObject item)) continue;
                    var kind = NetJson.Str(item["kind"]);
                    var body = item["body"] as JObject ?? new JObject();
                    var from = NetJson.Str(item["from"]);
                    switch (kind)
                    {
                        case "SHOT":
                            if (!Chess.PlayUci(NetJson.Str(body["move"]))) NetLog.Warn("[board] resume: bad chess move " + NetJson.Str(body["move"]));
                            pendingFrom = from;
                            pendingN = NetJson.Int(body["n"], Turn.N);
                            Turn.TurnCode = from;
                            Turn.OnShot();
                            if (from == me) MovesSent++;
                            break;
                        case "SHOT_RESULT":
                            Turn.OnResult();
                            pendingFrom = "";
                            break;
                        case "TOMATO":
                            if (from == me) Tomatoes.MarkThrown(Turn.RoundIndex);
                            break;
                        case "GAME_OVER":
                            if (!Finished)
                            {
                                Finished = true;
                                Winner = NetJson.Str(body["winner"]);
                                EndReason = ChessReason(NetJson.Str(body["reason"]), Winner);
                            }
                            break;
                    }
                }
            }
            Turn.TurnCode = NetJson.Str(d["turn"], Turn.TurnCode);
            Turn.N = NetJson.Int(d["n"], Turn.N);
            _chessPending = false;
            _chessOwnEnd = false;
            PeerDown = false;
            Joined = true;
            var answer = false;
            if (pendingFrom.Length > 0 && !Finished)
            {
                Turn.Phase = BattleshipTurn.PhaseResult;
                if (pendingFrom == me)
                {
                    _chessPending = true; // the opponent acks at its own resume
                    _chessPendingPly = Chess.Moves.Count;
                }
                else answer = true;
            }
            else Turn.Phase = BattleshipTurn.PhaseShot;
            if (!answer && !Finished)
            {
                // a mate that is in the history but whose GAME_OVER is not (the receiver dropped before sending it)
                var res = Chess.Result();
                if (res.Length > 0)
                {
                    Finished = true;
                    Winner = res == ChessLogic.ResultCheckmate ? (-Chess.ToMove == MyColor ? me : OpponentCode) : "";
                    EndReason = ChessReason(res, Winner);
                    if (Chess.ToMove == MyColor) SendRelay("GAME_OVER", new JObject { ["winner"] = Winner, ["reason"] = res });
                }
            }
            Raise(() => GameResumed?.Invoke(), "GameResumed");
            MarkChanged();
            if (answer)
            {
                SendRelay("SHOT_RESULT", new JObject { ["n"] = pendingN });
                Turn.OnResult();
                if (!CheckChessEnd(false)) MarkChanged();
            }
        }

        private void Finish(string winner, BoardEndReason reason)
        {
            Finished = true;
            Winner = winner ?? "";
            EndReason = reason;
            _pendingShot = BattleshipCell.Invalid;
            var iWon = IWon;
            var w = Winner;
            Raise(() => GameOver?.Invoke(w, iWon, reason), "GameOver");
            MarkChanged();
        }

        private void StartGame(JObject d)
        {
            var room = NetJson.Str(d["room"]);
            if (room.Length == 0) return;
            var old = RoomId;
            if (old.Length > 0 && old != room && _session.IsOnline)
            {
                // another game replaces this one: leave the old table (mid-game the server scores a forfeit)
                _session.Request("ROOM_LEAVE", new JObject { ["room"] = old }, null);
            }
            ResetGame();
            RoomId = room;
            Game = NetJson.Str(d["game"], GameBattleship);
            if (!IsKnownGame(Game)) Game = GameBattleship;
            Seed = NetJson.Long(d["seed"]);
            FirstCode = NetJson.Str(d["first"]);
            var me = MyCode;
            var codes = new List<string>();
            if (d["players"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (!(t is JObject p)) continue;
                    var player = new BoardPlayer
                    {
                        Code = NetJson.Str(p["friendCode"]),
                        Name = NetJson.Str(p["name"]),
                        GoobyName = NetJson.Str(p["goobyName"], "Gooby"),
                    };
                    _players.Add(player);
                    codes.Add(player.Code);
                    if (player.Code != me && player.Code.Length > 0)
                    {
                        OpponentCode = player.Code;
                        OpponentName = player.Name;
                        OpponentGoobyName = player.GoobyName;
                    }
                }
            }
            Turn = new BattleshipTurn(FirstCode, codes);
            if (IsChess)
            {
                Chess = new ChessLogic();
                MyColor = FirstCode == me ? ChessLogic.White : ChessLogic.Black;
            }
            else
            {
                Board = new BattleshipBoard();
                Tracker = new BattleshipTracker();
                _draft = BattleshipLogic.DefaultFleet(Seed, me);
            }
            RemoveInvite(OpponentCode);
            if (OutgoingInvite == OpponentCode) OutgoingInvite = "";
            Raise(() => GameStarted?.Invoke(), "GameStarted");
            MarkChanged();
            JoinRoom();
        }

        private void JoinRoom()
        {
            var room = RoomId;
            if (room.Length == 0) return;
            _session.Request("ROOM_JOIN", new JObject { ["room"] = room }, r =>
            {
                if (RoomId != room) return;
                if (r.Ok)
                {
                    Joined = true;
                    var peerThere = false;
                    if (r.Data["members"] is JArray members)
                        foreach (var m in members) if (NetJson.Str(m) == OpponentCode && OpponentCode.Length > 0) peerThere = true;
                    if (peerThere) SetPeerInRoom(true);
                    else MarkChanged();
                    return;
                }
                Joined = false;
                if (r.Code == "BAD_ROOM")
                {
                    // the server forgot the table (rejoin window over / rematch elsewhere)
                    OpponentGone = true;
                    if (!Finished) Finish("", BoardEndReason.Expired);
                    else MarkChanged();
                    return;
                }
                NetLog.Warn("[board] ROOM_JOIN " + room + " failed: " + r.Code);
                MarkChanged();
            });
        }

        private void SetPeerInRoom(bool inRoom)
        {
            var was = PeerInRoom;
            PeerInRoom = inRoom;
            if (inRoom) PeerDown = false;
            if (inRoom && !was) Raise(() => PeerJoined?.Invoke(), "PeerJoined");
            MarkChanged();
        }

        /// <summary>
        /// BOARD_RESUME after our reconnect: rebuild both boards from the full server history (own fleet = the locked
        /// one, else the seed default like Godot), take turn/n from the server, and answer an opponent shot that is
        /// still open (it was relayed while we were gone).
        /// </summary>
        private void Resume(JObject d)
        {
            if (IsChess)
            {
                ResumeChess(d);
                return;
            }
            var me = MyCode;
            var fleet = _locked ?? (IList<BattleshipShip>)_draft;
            if (BattleshipLogic.ValidateFleet(fleet).Length > 0) fleet = BattleshipLogic.DefaultFleet(Seed, me);
            Board = new BattleshipBoard();
            Board.Setup(fleet);
            _locked = new List<BattleshipShip>(fleet);
            FleetLocked = true;
            Tracker = new BattleshipTracker();
            var codes = new List<string>();
            foreach (var p in _players) codes.Add(p.Code);
            Turn = new BattleshipTurn(FirstCode, codes);
            Tomatoes.Reset();
            ShotsFired = 0;
            var pending = BattleshipCell.Invalid;
            var pendingFrom = "";
            var pendingN = 0;
            if (d["history"] is JArray history)
            {
                foreach (var t in history)
                {
                    if (!(t is JObject item)) continue;
                    var kind = NetJson.Str(item["kind"]);
                    var body = item["body"] as JObject ?? new JObject();
                    var from = NetJson.Str(item["from"]);
                    switch (kind)
                    {
                        case "SHOT":
                            pending = BattleshipLogic.RefToCell(NetJson.Str(body["cell"]));
                            pendingFrom = from;
                            pendingN = NetJson.Int(body["n"], Turn.N);
                            Turn.TurnCode = from;
                            Turn.OnShot();
                            if (from == me) ShotsFired++;
                            break;
                        case "SHOT_RESULT":
                            if (pendingFrom == me)
                            {
                                var sunk = NetJson.Bool(body["sunk"]);
                                Tracker.Record(pending, NetJson.Bool(body["hit"]), sunk, sunk ? BattleshipLogic.CellsFromJson(body["sunkCells"]) : null);
                                LastOutgoing = pending;
                            }
                            else if (pending.InBounds)
                            {
                                Board.ReceiveShot(pending);
                                LastIncoming = pending;
                            }
                            Turn.OnResult();
                            pending = BattleshipCell.Invalid;
                            pendingFrom = "";
                            break;
                        case "TOMATO":
                            if (from == me) Tomatoes.MarkThrown(Turn.RoundIndex);
                            break;
                        case "GAME_OVER":
                            if (!Finished)
                            {
                                Finished = true;
                                Winner = NetJson.Str(body["winner"]);
                                EndReason = from == Winner ? BoardEndReason.AllSunk : BoardEndReason.Surrender;
                            }
                            break;
                    }
                }
            }
            // the server's state is the authority for turn/n
            Turn.TurnCode = NetJson.Str(d["turn"], Turn.TurnCode);
            Turn.N = NetJson.Int(d["n"], Turn.N);
            _pendingShot = BattleshipCell.Invalid;
            PeerDown = false;
            Joined = true;
            var answer = false;
            if (pendingFrom.Length > 0 && !Finished)
            {
                Turn.Phase = BattleshipTurn.PhaseResult;
                if (pendingFrom == me) _pendingShot = pending; // the opponent still owes the answer
                else answer = true;
            }
            else Turn.Phase = BattleshipTurn.PhaseShot;
            if (!Finished && Board.RemainingShips == 0) { Finished = true; Winner = OpponentCode; EndReason = BoardEndReason.AllSunk; }
            if (!Finished && Tracker.HasWon) { Finished = true; Winner = me; EndReason = BoardEndReason.AllSunk; }
            Raise(() => GameResumed?.Invoke(), "GameResumed");
            MarkChanged();
            if (answer)
            {
                Turn.Phase = BattleshipTurn.PhaseShot;
                AnswerShot(pendingN, pending);
            }
        }

        private void OnWelcome(JObject d)
        {
            // after a reconnect the server dropped us from every room: sit down again (BOARD_RESUME follows when the
            // rejoin window is still open, BAD_ROOM when the table is gone)
            if (!HasGame || OpponentGone) return;
            Joined = false;
            JoinRoom();
        }

        private void OnStatus(NetStatus s)
        {
            if (s == NetStatus.Online || !HasGame) return;
            Joined = false;
            PeerInRoom = false;
            MarkChanged();
        }

        private void ResetGame()
        {
            RoomId = "";
            Game = "";
            Seed = 0;
            FirstCode = "";
            _players.Clear();
            OpponentCode = "";
            OpponentName = "";
            OpponentGoobyName = "";
            Board = null;
            Tracker = null;
            Turn = null;
            Tomatoes.Reset();
            _draft = new List<BattleshipShip>();
            _locked = null;
            _rerolls = 0;
            FleetLocked = false;
            Joined = false;
            PeerInRoom = false;
            PeerDown = false;
            OpponentGone = false;
            Finished = false;
            Winner = "";
            EndReason = BoardEndReason.None;
            RematchMine = false;
            RematchTheirs = false;
            ShotsFired = 0;
            _pendingShot = BattleshipCell.Invalid;
            LastIncoming = BattleshipCell.Invalid;
            LastOutgoing = BattleshipCell.Invalid;
            Chess = null;
            MyColor = ChessLogic.White;
            MovesSent = 0;
            _chessPending = false;
            _chessOwnEnd = false;
            _chessPendingPly = 0;
        }

        // ------------------------------------------------------------------ helpers

        private void MarkChanged() => Raise(() => Changed?.Invoke(), "Changed");

        private static void Raise(Action a, string what) => NetLog.Guard(a, "[board] " + what);

        private static string Upper(string code) => (code ?? "").Trim().ToUpperInvariant();
    }
}
