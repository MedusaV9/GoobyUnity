using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>An incoming GOBNOM_INVITED (valid for <see cref="ExpiresInMs"/> after <see cref="ReceivedAtMs"/>).</summary>
    public sealed class GobnomInvite
    {
        public string From { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string GoobyName { get; internal set; } = "";
        public int ExpiresInMs { get; internal set; } = GobnomNetClient.InviteTtlMs;
        public long ReceivedAtMs { get; internal set; }
        public long ExpiresAtMs => ReceivedAtMs + ExpiresInMs;
    }

    /// <summary>A booked coop result (GOBNOM_RESULT push or WELCOME.gnomPending), acked once seen.</summary>
    public sealed class GobnomNetResult
    {
        public string MatchId = "", Outcome = "", RewardId = "", Room = "";
        public int Level, Jars, Stars, Tick;
    }

    /// <summary>
    /// Client side of GOB NOM's online coop (GOOBY-SERVER gobnommp.js; port of gobnom_net_session.gd): invites like the
    /// board games (GOBNOM_INVITE → GOBNOM_INVITED, GOBNOM_ACCEPT → GOBNOM_READY to both, GOBNOM_DECLINE), the gobnom: room
    /// (both sit down with ROOM_JOIN), the level handshake (both vote the same coop level → GOBNOM_START with the server
    /// seed), the lockstep channels as ROOM_MSG kinds (GN_INPUT frames, GN_HASH, GN_CURSOR), peer down/up, desync, abort,
    /// the rejoin snapshot after a reconnect and the idempotent result (GOBNOM_RESULT + ACK). Pure protocol: the sim
    /// lives in <c>GobnomLockstep</c>, the view in the GOB NOM host. Main-thread only like <see cref="SocialClient"/>.
    /// </summary>
    public sealed class GobnomNetClient
    {
        public const int InviteTtlMs = 30000;
        public const int LevelMin = 1, LevelMax = 10;

        private readonly NetSession _session;
        private readonly Func<string> _myCode;
        private readonly Func<long> _now;
        private readonly List<GobnomInvite> _incoming = new List<GobnomInvite>();
        private readonly List<(string kind, JObject body)> _held = new List<(string, JObject)>();
        private string _outgoing = "";
        private long _outgoingAtMs;

        public GobnomNetClient(SocialClient social, Func<long> nowMs = null)
            : this(social?.Session, () => social?.FriendCode ?? "", nowMs)
        {
        }

        public GobnomNetClient(NetSession session, Func<string> myCode, Func<long> nowMs = null)
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

        // ── state ──

        public string MyCode => _myCode() ?? "";
        public bool IsOnline => _session.IsOnline;
        /// <summary>"" = not paired.</summary>
        public string RoomId { get; private set; } = "";
        public string MySide { get; private set; } = "a";
        public string PartnerCode { get; private set; } = "";
        public string PartnerName { get; private set; } = "";
        public string PartnerGoobyName { get; private set; } = "Gooby";
        public int InputDelay { get; private set; } = 4;
        public int HashEveryTicks { get; private set; } = 60;
        public int Level { get; private set; }
        public long Seed { get; private set; }
        public bool Running { get; private set; }
        public bool PeerDown { get; private set; }
        public bool Joined { get; private set; }
        /// <summary>
        /// Our connection dropped mid-run: until the rejoin snapshot is in, nothing of the stale lockstep may leave (the
        /// server would take its frames after our ROOM_JOIN and the partner would trust their fences), and the partner's
        /// frames wait for the replayed sim.
        /// </summary>
        public bool Rejoining { get; private set; }
        /// <summary>Level votes by friend code (the lobby shows both).</summary>
        public Dictionary<string, int> Votes { get; } = new Dictionary<string, int>();
        public List<GobnomNetResult> PendingResults { get; } = new List<GobnomNetResult>();

        public bool IsPaired => RoomId.Length > 0;
        public int MyVote => Votes.TryGetValue(Upper(MyCode), out var v) ? v : 0;
        public int PartnerVote => PartnerCode.Length > 0 && Votes.TryGetValue(PartnerCode, out var v) ? v : 0;

        /// <summary>Target of our open invite ("" when none or after the 30 s the server keeps it).</summary>
        public string OutgoingInvite
        {
            get
            {
                if (_outgoing.Length > 0 && _now() - _outgoingAtMs >= InviteTtlMs) _outgoing = "";
                return _outgoing;
            }
        }

        public IReadOnlyList<GobnomInvite> IncomingInvites
        {
            get
            {
                var now = _now();
                _incoming.RemoveAll(i => now >= i.ExpiresAtMs);
                return _incoming;
            }
        }

        // ── events ──

        public event Action Changed;
        public event Action<GobnomInvite> InviteIncoming;
        public event Action<string> InviteDeclined;
        /// <summary>Paired (GOBNOM_READY): both are in the lobby, the level vote can begin.</summary>
        public event Action SessionReady;
        public event Action VotesChanged;
        /// <summary>GOBNOM_START: both devices build the identical sim from <see cref="Seed"/> and <see cref="Level"/>.</summary>
        public event Action GameStarted;
        public event Action<JObject> FrameReceived;
        public event Action<int, string> HashReceived;
        public event Action<double, double> CursorReceived;
        public event Action<GobnomNetResult> ResultConfirmed;
        public event Action<int> DesyncReported;
        public event Action<bool, int> PeerConnectionChanged;
        /// <summary>(reason, byCode): left | timeout | overflow.</summary>
        public event Action<string, string> Aborted;
        public event Action<JObject> SnapshotReceived;

        // ── invites ──

        public void Invite(string targetCode, Action<NetReply> cb)
        {
            var target = Upper(targetCode);
            _session.Request("GOBNOM_INVITE", new JObject { ["target"] = target }, r =>
            {
                if (r.Ok)
                {
                    _outgoing = target;
                    _outgoingAtMs = _now();
                }
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        /// <summary>GOBNOM_ACCEPT {from}; the reply IS the GOBNOM_READY (SessionReady fires before <paramref name="cb"/>).</summary>
        public void Accept(string fromCode, Action<NetReply> cb)
        {
            var from = Upper(fromCode);
            _incoming.RemoveAll(i => i.From == from);
            _session.Request("GOBNOM_ACCEPT", new JObject { ["from"] = from }, r =>
            {
                if (r.Ok && r.Type == "GOBNOM_READY") OnReady(r.Data);
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        public void Decline(string fromCode, Action<NetReply> cb = null)
        {
            var from = Upper(fromCode);
            _incoming.RemoveAll(i => i.From == from);
            _session.Request("GOBNOM_DECLINE", new JObject { ["from"] = from }, r => cb?.Invoke(r));
            Changed?.Invoke();
        }

        /// <summary>Votes a coop level (both the same → the server pushes GOBNOM_START).</summary>
        public void ChooseLevel(int level, Action<NetReply> cb = null)
        {
            if (!IsPaired)
            {
                cb?.Invoke(NetReply.Fail("NOT_IN_ROOM"));
                return;
            }
            var room = RoomId;
            _session.Request("GOBNOM_LEVEL", new JObject { ["room"] = room, ["level"] = level }, r =>
            {
                if (r.Ok && RoomId == room)
                {
                    Votes[Upper(MyCode)] = level;
                    if (r.Data["votes"] is JObject votes) ReadVotes(votes);
                    VotesChanged?.Invoke();
                }
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        // ── lockstep channels (fire-and-forget, like POS) ──

        public bool SendFrame(JObject frame) => SendRoomMessage("GN_INPUT", frame);
        public bool SendHash(int tick, string hash) => SendRoomMessage("GN_HASH", new JObject { ["t"] = tick, ["h"] = hash });
        public bool SendCursor(double x, double y) => SendRoomMessage("GN_CURSOR", new JObject { ["x"] = x, ["y"] = y });

        private bool SendRoomMessage(string kind, JObject body)
        {
            if (!IsPaired || body == null || !_session.IsOnline || Rejoining) return false;
            return _session.Send("ROOM_MSG", new JObject { ["room"] = RoomId, ["kind"] = kind, ["body"] = body }) >= 0;
        }

        // ── result + leaving ──

        public void ReportResult(string outcome, int jars, int tick, Action<NetReply> cb = null)
        {
            if (!IsPaired)
            {
                cb?.Invoke(NetReply.Fail("NOT_IN_ROOM"));
                return;
            }
            _session.Request("GOBNOM_RESULT", new JObject { ["room"] = RoomId, ["outcome"] = outcome, ["jars"] = jars, ["tick"] = tick }, r => cb?.Invoke(r));
        }

        public void AckResult(string rewardId)
        {
            if (!string.IsNullOrEmpty(rewardId)) _session.Send("GOBNOM_RESULT_ACK", new JObject { ["rewardId"] = rewardId });
        }

        /// <summary>Leaves cleanly: the server ends the session for the partner (coop has no forfeit winner); invites are free again.</summary>
        public void Leave()
        {
            if (!IsPaired) return;
            var room = RoomId;
            Reset();
            if (_session.IsOnline) _session.Send("ROOM_LEAVE", new JObject { ["room"] = room });
            Changed?.Invoke();
        }

        // ── server → client ──

        private void OnPush(string type, JObject d)
        {
            var room = NetJson.Str(d["room"]);
            switch (type)
            {
                case "GOBNOM_INVITED":
                {
                    var inv = new GobnomInvite
                    {
                        From = Upper(NetJson.Str(d["from"])),
                        Name = NetJson.Str(d["name"], "?"),
                        GoobyName = NetJson.Str(d["goobyName"], "Gooby"),
                        ExpiresInMs = NetJson.Int(d["expiresInMs"], InviteTtlMs),
                        ReceivedAtMs = _now(),
                    };
                    if (inv.From.Length == 0) return;
                    _incoming.RemoveAll(i => i.From == inv.From);
                    _incoming.Add(inv);
                    InviteIncoming?.Invoke(inv);
                    Changed?.Invoke();
                    break;
                }
                case "GOBNOM_DECLINED":
                {
                    var by = Upper(NetJson.Str(d["from"]));
                    if (_outgoing == by) _outgoing = "";
                    InviteDeclined?.Invoke(by);
                    Changed?.Invoke();
                    break;
                }
                case "GOBNOM_READY":
                    OnReady(d);
                    break;
                case "GOBNOM_LEVEL_STATE":
                    if (room != RoomId) return;
                    if (d["votes"] is JObject votes) ReadVotes(votes);
                    VotesChanged?.Invoke();
                    Changed?.Invoke();
                    break;
                case "GOBNOM_START":
                    if (room != RoomId) return;
                    Level = NetJson.Int(d["level"]);
                    Seed = NetJson.Long(d["seed"]);
                    InputDelay = NetJson.Int(d["inputDelay"], InputDelay);
                    HashEveryTicks = NetJson.Int(d["hashEveryTicks"], HashEveryTicks);
                    ApplyPlayers(d["players"] as JArray);
                    Running = true;
                    PeerDown = false;
                    Rejoining = false;
                    _held.Clear();
                    GameStarted?.Invoke();
                    Changed?.Invoke();
                    break;
                case "GOBNOM_PEER_DOWN":
                    if (room != RoomId) return;
                    PeerDown = true;
                    PeerConnectionChanged?.Invoke(true, NetJson.Int(d["waitMs"]));
                    Changed?.Invoke();
                    break;
                case "GOBNOM_PEER_UP":
                    if (room != RoomId) return;
                    PeerDown = false;
                    PeerConnectionChanged?.Invoke(false, 0);
                    Changed?.Invoke();
                    break;
                case "GOBNOM_DESYNC":
                    if (room != RoomId) return;
                    Running = false;
                    DesyncReported?.Invoke(NetJson.Int(d["tick"], -1));
                    Changed?.Invoke();
                    break;
                case "GOBNOM_ABORTED":
                {
                    if (room != RoomId) return;
                    var reason = NetJson.Str(d["reason"]);
                    var by = NetJson.Str(d["by"]);
                    Reset();
                    Aborted?.Invoke(reason, by);
                    Changed?.Invoke();
                    break;
                }
                case "GOBNOM_RESULT":
                {
                    if (room != RoomId) return;
                    Running = false;
                    var result = ReadResult(d);
                    ResultConfirmed?.Invoke(result);
                    AckResult(result.RewardId);
                    Changed?.Invoke();
                    break;
                }
                case "GOBNOM_SNAPSHOT":
                    if (room != RoomId) return;
                    OnSnapshot(d);
                    break;
                case "ROOM_MSG":
                {
                    if (room != RoomId || !(d["body"] is JObject body)) return;
                    var kind = NetJson.Str(d["kind"]);
                    if (Rejoining)
                    {
                        // a replay is on its way: the partner's frames must reach the replayed sim, not the stale one
                        if (kind != "GN_CURSOR") _held.Add((kind, body));
                        return;
                    }
                    DispatchRoomMessage(kind, body);
                    break;
                }
            }
        }

        private void DispatchRoomMessage(string kind, JObject body)
        {
            switch (kind)
            {
                case "GN_INPUT": FrameReceived?.Invoke(body); break;
                case "GN_HASH": HashReceived?.Invoke(NetJson.Int(body["t"], -1), NetJson.Str(body["h"])); break;
                case "GN_CURSOR": CursorReceived?.Invoke(NetJson.Num(body["x"]), NetJson.Num(body["y"])); break;
            }
        }

        /// <summary>GOBNOM_SNAPSHOT (pushed on our rejoin, or the reply to GOBNOM_RESUME): replay, then the held partner frames.</summary>
        private void OnSnapshot(JObject d)
        {
            Level = NetJson.Int(d["level"], Level);
            Seed = NetJson.Long(d["seed"], Seed);
            InputDelay = NetJson.Int(d["inputDelay"], InputDelay);
            HashEveryTicks = NetJson.Int(d["hashEveryTicks"], HashEveryTicks);
            ApplyPlayers(d["players"] as JArray);
            if (d["votes"] is JObject sv) ReadVotes(sv);
            Running = NetJson.Str(d["phase"]) == "run";
            PeerDown = false;
            Rejoining = false;
            SnapshotReceived?.Invoke(d);
            var held = _held.ToArray();
            _held.Clear();
            foreach (var (kind, body) in held)
            {
                if (!IsPaired) break;
                DispatchRoomMessage(kind, body);
            }
            Changed?.Invoke();
        }

        private void OnReady(JObject d)
        {
            RoomId = NetJson.Str(d["room"]);
            InputDelay = NetJson.Int(d["inputDelay"], 4);
            HashEveryTicks = NetJson.Int(d["hashEveryTicks"], 60);
            Running = false;
            PeerDown = false;
            Votes.Clear();
            _outgoing = "";
            ApplyPlayers(d["players"] as JArray);
            JoinRoom();
            SessionReady?.Invoke();
            Changed?.Invoke();
        }

        private void JoinRoom()
        {
            if (!IsPaired || !_session.IsOnline) return;
            var room = RoomId;
            _session.Request("ROOM_JOIN", new JObject { ["room"] = room }, r =>
            {
                if (RoomId != room) return;
                Joined = r.Ok;
                if (!r.Ok && r.Code == "BAD_ROOM")
                {
                    // the session is gone on the server (e.g. our rejoin window ran out)
                    Reset();
                    Aborted?.Invoke("expired", "");
                }
                // the join hook pushes the snapshot before this reply — still none: the server never saw our drop, ask for it
                else if (r.Ok && Rejoining) RequestSnapshot();
                Changed?.Invoke();
            });
        }

        private void RequestSnapshot()
        {
            var room = RoomId;
            _session.Request("GOBNOM_RESUME", new JObject { ["room"] = room }, r =>
            {
                if (RoomId != room || !Rejoining) return;
                if (r.Ok && r.Type == "GOBNOM_SNAPSHOT") OnSnapshot(r.Data);
                else if (r.Code == "NOT_FOUND")
                {
                    Reset();
                    Aborted?.Invoke("expired", "");
                    Changed?.Invoke();
                }
            });
        }

        private void ApplyPlayers(JArray rows)
        {
            if (rows == null) return;
            foreach (var t in rows)
            {
                if (!(t is JObject row)) continue;
                var code = Upper(NetJson.Str(row["friendCode"]));
                if (code == Upper(MyCode)) MySide = NetJson.Str(row["side"], "a");
                else
                {
                    PartnerCode = code;
                    PartnerName = NetJson.Str(row["name"], "?");
                    PartnerGoobyName = NetJson.Str(row["goobyName"], "Gooby");
                }
            }
        }

        private void ReadVotes(JObject votes)
        {
            Votes.Clear();
            foreach (var p in votes.Properties()) Votes[Upper(p.Name)] = NetJson.Int(p.Value);
        }

        private static GobnomNetResult ReadResult(JObject d) => new GobnomNetResult
        {
            MatchId = NetJson.Str(d["matchId"]),
            Level = NetJson.Int(d["level"]),
            Outcome = NetJson.Str(d["outcome"]),
            Jars = NetJson.Int(d["jars"]),
            Stars = NetJson.Int(d["stars"]),
            Tick = NetJson.Int(d["tick"]),
            RewardId = NetJson.Str(d["rewardId"]),
            Room = NetJson.Str(d["room"]),
        };

        private void OnWelcome(JObject d)
        {
            // after a reconnect the server dropped us from every room: sit down again (a snapshot follows while the rejoin window is open)
            if (IsPaired)
            {
                Joined = false;
                if (Running) Rejoining = true;
                JoinRoom();
            }
            if (d?["gnomPending"] is JArray pending)
                foreach (var t in pending)
                {
                    if (!(t is JObject row)) continue;
                    var result = ReadResult(row);
                    PendingResults.Add(result);
                    ResultConfirmed?.Invoke(result);
                    AckResult(result.RewardId);
                }
        }

        private void OnStatus(NetStatus s)
        {
            if (s == NetStatus.Online || !IsPaired) return;
            Joined = false;
            if (Running) Rejoining = true;
            Changed?.Invoke();
        }

        private void Reset()
        {
            RoomId = "";
            PartnerCode = "";
            PartnerName = "";
            PartnerGoobyName = "Gooby";
            Level = 0;
            Seed = 0;
            Running = false;
            PeerDown = false;
            Joined = false;
            Rejoining = false;
            _held.Clear();
            Votes.Clear();
        }

        private static string Upper(string s) => (s ?? "").Trim().ToUpperInvariant();
    }
}
