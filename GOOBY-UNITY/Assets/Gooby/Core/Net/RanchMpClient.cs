using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Ranch.Mp;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>An incoming RANCH_INVITED (valid for <see cref="ExpiresInMs"/> after <see cref="ReceivedAtMs"/>).</summary>
    public sealed class RmpInvite
    {
        public string From { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string GoobyName { get; internal set; } = "";
        public string Mode { get; internal set; } = "";
        public string Kurs { get; internal set; } = "";
        public int ExpiresInMs { get; internal set; } = RanchMpClient.InviteTtlMs;
        public long ReceivedAtMs { get; internal set; }
        public long ExpiresAtMs => ReceivedAtMs + ExpiresInMs;
    }

    public sealed class RmpPlayer
    {
        public string Code = "", Name = "?", GoobyName = "Gooby";
    }

    /// <summary>A peer pose from the 10-Hz relay (MG_PEER_POSE).</summary>
    public struct RmpPeerPose
    {
        public V2 Pos;
        public double Y, Yaw, Speed;
        public int Gait, Seq;
        public bool Jump;
    }

    /// <summary>One line of MG_RESULT (or WELCOME.rmpPending) — the own line carries all lines in <see cref="All"/>.</summary>
    public sealed class RmpResult
    {
        public string FriendCode = "", RewardId = "", Mode = "", Kurs = "", Room = "";
        public int Rank, ZeitMs;
        public bool Dnf, Ranked;
        public List<RmpResult> All = new List<RmpResult>();
    }

    /// <summary>
    /// Client side of the ranch multiplayer (GOOBY-SERVER ranchmp.js; port of rmp_service.gd) on the shared session: invites
    /// (RANCH_INVITE → RANCH_INVITED, RANCH_ACCEPT → RANCH_READY to everyone, RANCH_DECLINE), the mg: room (ROOM_JOIN),
    /// the lobby (MG_READY with the course hash → MG_LOBBY, MG_START with seed and server start time), the 10-Hz pose relay
    /// (MG_POSE → MG_PEER_POSE), server-checked events (MG_EVENT checkpoint | finish | tag), state pushes (MG_STATE),
    /// the idempotent result (MG_RESULT + MG_RESULT_ACK, WELCOME.rmpPending), the rejoin snapshot (MG_SNAPSHOT,
    /// MG_RESUME), peer down/up and the rematch (RMP_REMATCH → a fresh RANCH_READY). Pure protocol: the ride lives in
    /// <see cref="RmpLauf"/>. Main-thread only like <see cref="SocialClient"/>.
    /// </summary>
    public sealed class RanchMpClient
    {
        public const int InviteTtlMs = 30000, PoseIntervalMs = 100;
        public const string PhaseLobby = "lobby", PhaseCountdown = "countdown", PhaseRun = "run", PhaseDone = "done";
        /// <summary>Besuch / Ausritt: no lobby, no match — everyone rides from the moment they are in the room.</summary>
        public const string PhaseFrei = "frei";

        /// <summary>The cosmetic reactions of a visit or trail ride (ROOM_MSG kinds, rmp_service.gd REAKTIONEN).</summary>
        public static readonly string[] Reaktionen = { "HERZ", "GESTE", "FOLGE_MIR", "ENTDECKUNG" };

        private readonly NetSession _session;
        private readonly Func<string> _myCode;
        private readonly Func<long> _now;
        private readonly List<RmpInvite> _incoming = new List<RmpInvite>();
        private string _outgoing = "";
        private long _outgoingAtMs, _lastPoseMs = -1;
        private int _poseSeq;

        public RanchMpClient(SocialClient social, Func<long> nowMs = null)
            : this(social?.Session, () => social?.FriendCode ?? "", nowMs)
        {
        }

        public RanchMpClient(NetSession session, Func<string> myCode, Func<long> nowMs = null)
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

        public string MyCode => Upper(_myCode());
        public bool IsOnline => _session.IsOnline;
        /// <summary>"" = no session.</summary>
        public string RoomId { get; private set; } = "";
        public string Mode { get; private set; } = "";
        public string Kurs { get; private set; } = "";
        public string Host { get; private set; } = "";
        public string Phase { get; private set; } = "";
        public long Seed { get; private set; }
        /// <summary>Server clock (ms) of the start shot / of the end of a tag round (0 = none).</summary>
        public long StartAtMs { get; private set; }
        public long EndsAtMs { get; private set; }
        public long ServerOffsetMs { get; private set; }
        public string ItCode { get; private set; } = "";
        public long ImmunityUntilMs { get; private set; }
        public bool Joined { get; private set; }
        public bool Rematch { get; private set; }
        public List<RmpPlayer> Players { get; } = new List<RmpPlayer>();
        public HashSet<string> Ready { get; } = new HashSet<string>();
        /// <summary>Next checkpoint per friend code (MG_STATE progress).</summary>
        public Dictionary<string, int> Progress { get; } = new Dictionary<string, int>();
        public Dictionary<string, (int timeMs, int rank)> Finished { get; } = new Dictionary<string, (int, int)>();
        public HashSet<string> Dnf { get; } = new HashSet<string>();
        public HashSet<string> PeersDown { get; } = new HashSet<string>();
        public List<RmpResult> PendingResults { get; } = new List<RmpResult>();
        /// <summary>The result of the current room (MG_RESULT), null until the round is booked.</summary>
        public RmpResult LastResult { get; private set; }
        public HashSet<string> RematchWanted { get; } = new HashSet<string>();

        /// <summary>The horse this player rides into a round (<see cref="RmpPferdLook"/>), asked for each time it is sent.</summary>
        public Func<JObject> OwnHorse { get; set; }
        /// <summary>The friends' horses in the room (friend code → look) from their PFERD messages.</summary>
        public Dictionary<string, JObject> PeerHorses { get; } = new Dictionary<string, JObject>();

        public bool IsActive => RoomId.Length > 0;
        public bool IsMatch => Array.IndexOf(RmpKurse.MatchModi, Mode) >= 0;
        public bool IsFree => RmpKurse.IstFrei(Mode);
        /// <summary>Besuch: the revision of the host's ranch metadata at RANCH_READY (-1 = none uploaded).</summary>
        public int HostMetaRev { get; private set; } = -1;
        public bool IAmReady => Ready.Contains(MyCode);
        public bool IAmIt => ItCode.Length > 0 && ItCode == MyCode;
        public long ServerNowMs => _now() + ServerOffsetMs;
        /// <summary>Time to the start shot (≤ 0: running).</summary>
        public long CountdownMs => StartAtMs - ServerNowMs;
        public bool Running => Phase == PhaseRun || Phase == PhaseFrei || Phase == PhaseCountdown && StartAtMs > 0 && CountdownMs <= 0;

        public string OutgoingInvite
        {
            get
            {
                if (_outgoing.Length > 0 && _now() - _outgoingAtMs >= InviteTtlMs) _outgoing = "";
                return _outgoing;
            }
        }

        public IReadOnlyList<RmpInvite> IncomingInvites
        {
            get
            {
                var now = _now();
                _incoming.RemoveAll(i => now >= i.ExpiresAtMs);
                return _incoming;
            }
        }

        public string NameOf(string code)
        {
            code = Upper(code);
            foreach (var p in Players) if (p.Code == code) return p.Name;
            return code;
        }

        // ── events ──

        public event Action Changed;
        public event Action<RmpInvite> InviteIncoming;
        public event Action<string> InviteDeclined;
        /// <summary>RANCH_READY (an accepted invite, also a rematch): the lobby opens.</summary>
        public event Action SessionReady;
        public event Action LobbyChanged;
        /// <summary>MG_START: everyone builds the run from the course, the players and <see cref="StartAtMs"/>.</summary>
        public event Action MatchStarted;
        public event Action<JObject> StateUpdated;
        public event Action<string, RmpPeerPose> PeerPose;
        public event Action<RmpResult> ResultReceived;
        public event Action<JObject> SnapshotReceived;
        public event Action<string, bool> PeerConnectionChanged;
        /// <summary>(room, byCode): a ranch visit ended or the session is gone.</summary>
        public event Action<string> Ended;
        public event Action<string> RematchWaiting;
        public event Action<string> RematchDeclined;
        public event Action<string, string> PeerLeft;
        /// <summary>(code, name) — a friend came into the room (a trail ride takes new riders at any time).</summary>
        public event Action<string, string> PeerJoined;
        /// <summary>(kind, fromCode, body) — HERZ, GESTE {id}, FOLGE_MIR {an}, ENTDECKUNG {zone} from a friend in the room.</summary>
        public event Action<string, string, JObject> ReactionReceived;
        /// <summary>A friend's horse look came in (friend code; the look is in <see cref="PeerHorses"/>).</summary>
        public event Action<string> PeerHorseChanged;

        // ── invites ──

        /// <summary>RANCH_INVITE {target, mode, kurs} ("" = the server's standard course of the mode).</summary>
        public void Invite(string targetCode, string mode, string kurs, Action<NetReply> cb)
        {
            var target = Upper(targetCode);
            _session.Request("RANCH_INVITE", new JObject { ["target"] = target, ["mode"] = mode ?? "", ["kurs"] = kurs ?? "" }, r =>
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

        /// <summary>RANCH_ACCEPT {from}; the reply IS the RANCH_READY (SessionReady fires before <paramref name="cb"/>).</summary>
        public void Accept(string fromCode, Action<NetReply> cb)
        {
            var from = Upper(fromCode);
            _incoming.RemoveAll(i => i.From == from);
            _session.Request("RANCH_ACCEPT", new JObject { ["from"] = from }, r =>
            {
                if (r.Ok && r.Type == "RANCH_READY") OnReady(r.Data);
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        public void Decline(string fromCode, Action<NetReply> cb = null)
        {
            var from = Upper(fromCode);
            _incoming.RemoveAll(i => i.From == from);
            _session.Request("RANCH_DECLINE", new JObject { ["from"] = from }, r => cb?.Invoke(r));
            Changed?.Invoke();
        }

        // ── lobby / match ──

        /// <summary>MG_READY with the course hash (a mismatch only makes the run unranked); MG_START follows once everyone is ready.</summary>
        public void SetReady(Action<NetReply> cb = null)
        {
            if (!IsActive)
            {
                cb?.Invoke(NetReply.Fail("NOT_IN_ROOM"));
                return;
            }
            var room = RoomId;
            _session.Request("MG_READY", new JObject { ["room"] = room, ["kursHash"] = RmpKurse.KursHash(Kurs) }, r =>
            {
                if (r.Ok && RoomId == room)
                {
                    Ready.Add(MyCode);
                    if (r.Data["ready"] is JArray ready) ReadReady(ready);
                    LobbyChanged?.Invoke();
                }
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        /// <summary>Own pose into the relay (fire-and-forget, throttled to 10 Hz; <paramref name="force"/> for the start and jumps).</summary>
        public bool SendPose(V2 pos, double y, double yaw, double speed, int gait, string anim = "", bool jump = false, bool force = false)
        {
            if (!IsActive || !Joined || !_session.IsOnline) return false;
            var now = _now();
            if (!force && _lastPoseMs >= 0 && now - _lastPoseMs < PoseIntervalMs) return false;
            _lastPoseMs = now;
            _poseSeq++;
            var d = new JObject
            {
                ["room"] = RoomId,
                ["p"] = new JArray(Round(pos.X, 100.0), Round(y, 100.0), Round(pos.Y, 100.0)),
                ["yaw"] = Round(yaw, 100.0),
                ["speed"] = Round(speed, 10.0),
                ["gait"] = gait,
                ["poseSeq"] = _poseSeq,
            };
            if (!string.IsNullOrEmpty(anim)) d["anim"] = anim;
            if (jump) d["jump"] = true;
            return _session.Send("MG_POSE", d) >= 0;
        }

        /// <summary>A cosmetic reaction to everyone in the room (ROOM_MSG; rate-limited by the server, never gameplay).</summary>
        public bool SendReaction(string kind, JObject body = null)
        {
            if (!IsActive || !Joined || !_session.IsOnline || Array.IndexOf(Reaktionen, kind) < 0) return false;
            return _session.Send("ROOM_MSG", new JObject { ["room"] = RoomId, ["kind"] = kind, ["body"] = body ?? new JObject() }) >= 0;
        }

        /// <summary>
        /// The own horse to everyone in the room (PFERD, cosmetic) — sent on joining and again whenever a friend joins, so
        /// riders who come later see it too. False without a horse or outside a room.
        /// </summary>
        public bool SendHorse()
        {
            if (!IsActive || !Joined || !_session.IsOnline) return false;
            var look = RmpPferdLook.Sauber(OwnHorse?.Invoke());
            if (look == null) return false;
            return _session.Send("ROOM_MSG", new JObject { ["room"] = RoomId, ["kind"] = RmpPferdLook.Kind, ["body"] = look }) >= 0;
        }

        /// <summary>MG_EVENT {kind:checkpoint, idx} — the server checks order and distance; the reply carries {next}.</summary>
        public void SendCheckpoint(int idx, Action<NetReply> cb) => SendEvent(new JObject { ["kind"] = "checkpoint", ["idx"] = idx }, cb);

        /// <summary>MG_EVENT {kind:finish} — the finish time is SERVER time; the reply carries {timeMs, rank}.</summary>
        public void SendFinish(Action<NetReply> cb) => SendEvent(new JObject { ["kind"] = "finish" }, cb);

        /// <summary>MG_EVENT {kind:tag, target} — only the catcher, within reach, after the immunity.</summary>
        public void SendTag(string target, Action<NetReply> cb) => SendEvent(new JObject { ["kind"] = "tag", ["target"] = Upper(target) }, cb);

        private void SendEvent(JObject d, Action<NetReply> cb)
        {
            if (!IsActive || !_session.IsOnline)
            {
                cb?.Invoke(NetReply.Fail(IsActive ? NetErrorCodes.Offline : "NOT_IN_ROOM"));
                return;
            }
            d["room"] = RoomId;
            _session.Request("MG_EVENT", d, r => cb?.Invoke(r));
        }

        /// <summary>RMP_REMATCH: once everybody still in the room agrees, the server opens a fresh room (RANCH_READY with rematch).</summary>
        public void RequestRematch(Action<NetReply> cb = null)
        {
            if (!IsActive)
            {
                cb?.Invoke(NetReply.Fail("NOT_IN_ROOM"));
                return;
            }
            RematchWanted.Add(MyCode);
            _session.Request("RMP_REMATCH", new JObject { ["room"] = RoomId }, r =>
            {
                if (r.Ok && r.Type == "RANCH_READY") OnReady(r.Data);
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        public void AckResult(string rewardId)
        {
            if (string.IsNullOrEmpty(rewardId)) return;
            PendingResults.RemoveAll(p => p.RewardId == rewardId);
            if (_session.IsOnline) _session.Send("MG_RESULT_ACK", new JObject { ["rewardId"] = rewardId });
        }

        /// <summary>Leaves the session (during a run the server books it as given up).</summary>
        public void Leave()
        {
            if (!IsActive) return;
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
                case "RANCH_INVITED":
                {
                    var inv = new RmpInvite
                    {
                        From = Upper(NetJson.Str(d["from"])),
                        Name = NetJson.Str(d["name"], "?"),
                        GoobyName = NetJson.Str(d["goobyName"], "Gooby"),
                        Mode = NetJson.Str(d["mode"]),
                        Kurs = NetJson.Str(d["kurs"]),
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
                case "RANCH_DECLINED":
                {
                    var by = Upper(NetJson.Str(d["from"]));
                    if (_outgoing == by) _outgoing = "";
                    InviteDeclined?.Invoke(by);
                    Changed?.Invoke();
                    break;
                }
                case "RANCH_READY":
                    OnReady(d);
                    break;
                case "RANCH_ENDED":
                {
                    if (room != RoomId) return;
                    var by = NetJson.Str(d["by"]);
                    Reset();
                    Ended?.Invoke(by);
                    Changed?.Invoke();
                    break;
                }
                case "MG_LOBBY":
                    if (room != RoomId) return;
                    ApplyPlayers(d["players"] as JArray);
                    if (d["ready"] is JArray ready) ReadReady(ready);
                    LobbyChanged?.Invoke();
                    Changed?.Invoke();
                    break;
                case "MG_START":
                    if (room != RoomId) return;
                    Phase = PhaseCountdown;
                    Seed = NetJson.Long(d["seed"]);
                    StartAtMs = NetJson.Long(d["startAt"]);
                    EndsAtMs = NetJson.Long(d["endsAt"]);
                    ItCode = Upper(NetJson.Str(d["it"]));
                    ImmunityUntilMs = 0;
                    SyncClock(d["serverNow"]);
                    ApplyPlayers(d["players"] as JArray);
                    Progress.Clear();
                    Finished.Clear();
                    Dnf.Clear();
                    LastResult = null;
                    MatchStarted?.Invoke();
                    Changed?.Invoke();
                    break;
                case "MG_PEER_POSE":
                {
                    if (room != RoomId || !(d["p"] is JArray p) || p.Count < 3) return;
                    var from = Upper(NetJson.Str(d["from"]));
                    if (from.Length == 0 || from == MyCode) return;
                    var pose = new RmpPeerPose
                    {
                        Pos = new V2(NetJson.Num(p[0]), NetJson.Num(p[2])),
                        Y = NetJson.Num(p[1]),
                        Yaw = NetJson.Num(d["yaw"]),
                        Speed = NetJson.Num(d["speed"]),
                        Gait = NetJson.Int(d["gait"]),
                        Seq = NetJson.Int(d["poseSeq"]),
                        Jump = NetJson.Bool(d["jump"]),
                    };
                    PeerPose?.Invoke(from, pose);
                    break;
                }
                case "MG_STATE":
                    if (room != RoomId) return;
                    ApplyState(d);
                    if (Phase == PhaseCountdown && CountdownMs <= 0) Phase = PhaseRun;
                    StateUpdated?.Invoke(d);
                    Changed?.Invoke();
                    break;
                case "MG_RESULT":
                {
                    var result = ReadResult(d);
                    if (room == RoomId)
                    {
                        Phase = PhaseDone;
                        LastResult = result;
                        RematchWanted.Clear();
                    }
                    PendingResults.RemoveAll(x => x.RewardId == result.RewardId);
                    PendingResults.Add(result);
                    ResultReceived?.Invoke(result);
                    Changed?.Invoke();
                    break;
                }
                case "MG_SNAPSHOT":
                    if (room.Length > 0 && room != RoomId) return;
                    OnSnapshot(d);
                    break;
                case "MG_PEER_DOWN":
                case "MG_PEER_UP":
                {
                    if (room != RoomId) return;
                    var code = Upper(NetJson.Str(d["friendCode"]));
                    var down = type == "MG_PEER_DOWN";
                    if (down) PeersDown.Add(code);
                    else PeersDown.Remove(code);
                    PeerConnectionChanged?.Invoke(code, down);
                    Changed?.Invoke();
                    break;
                }
                case "RMP_REMATCH_WAIT":
                {
                    if (room != RoomId) return;
                    var code = Upper(NetJson.Str(d["friendCode"]));
                    RematchWanted.Add(code);
                    RematchWaiting?.Invoke(code);
                    Changed?.Invoke();
                    break;
                }
                case "RMP_REMATCH_DECLINED":
                {
                    if (room != RoomId) return;
                    var code = Upper(NetJson.Str(d["friendCode"]));
                    RematchDeclined?.Invoke(code);
                    Changed?.Invoke();
                    break;
                }
                case "ROOM_PEER_LEFT":
                {
                    if (room != RoomId) return;
                    var code = Upper(NetJson.Str(d["friendCode"]));
                    var name = NetJson.Str(d["name"]);
                    if (name.Length == 0) name = NameOf(code);
                    // a trail rider who left is gone for good (the server dropped the code); a match keeps its table
                    if (IsFree)
                    {
                        Players.RemoveAll(p => p.Code == code);
                        PeerHorses.Remove(code);
                    }
                    PeerLeft?.Invoke(code, name);
                    Changed?.Invoke();
                    break;
                }
                case "ROOM_PEER_JOINED":
                {
                    if (room != RoomId) return;
                    var code = Upper(NetJson.Str(d["friendCode"]));
                    if (code.Length == 0 || code == MyCode) return;
                    var known = Players.Find(p => p.Code == code);
                    if (known == null) Players.Add(new RmpPlayer { Code = code, Name = NetJson.Str(d["name"], "?"), GoobyName = NetJson.Str(d["goobyName"], "Gooby") });
                    SendHorse();
                    PeerJoined?.Invoke(code, NameOf(code));
                    Changed?.Invoke();
                    break;
                }
                case "ROOM_MSG":
                {
                    if (room != RoomId) return;
                    var kind = NetJson.Str(d["kind"]);
                    var from = d["from"] is JObject f ? Upper(NetJson.Str(f["friendCode"])) : Upper(NetJson.Str(d["from"]));
                    if (from.Length == 0 || from == MyCode) return;
                    if (kind == RmpPferdLook.Kind)
                    {
                        var look = RmpPferdLook.Sauber(d["body"]);
                        if (look == null) return;
                        PeerHorses[from] = look;
                        PeerHorseChanged?.Invoke(from);
                        return;
                    }
                    if (Array.IndexOf(Reaktionen, kind) < 0) return;
                    ReactionReceived?.Invoke(kind, from, d["body"] as JObject ?? new JObject());
                    break;
                }
            }
        }

        private void OnReady(JObject d)
        {
            // a trail ride takes riders at any time: everyone already riding hears RANCH_READY again for the same room —
            // only the table changes, nobody goes back to the start
            if (IsActive && IsFree && NetJson.Str(d["room"]) == RoomId)
            {
                ApplyPlayers(d["players"] as JArray);
                _outgoing = "";
                Changed?.Invoke();
                return;
            }
            RoomId = NetJson.Str(d["room"]);
            Mode = NetJson.Str(d["mode"]);
            Kurs = NetJson.Str(d["kurs"]);
            Host = Upper(NetJson.Str(d["host"]));
            Rematch = NetJson.Bool(d["rematch"]);
            HostMetaRev = d["rev"] == null ? -1 : NetJson.Int(d["rev"], -1);
            Phase = IsFree ? PhaseFrei : PhaseLobby;
            Seed = 0;
            StartAtMs = 0;
            EndsAtMs = 0;
            ItCode = "";
            ImmunityUntilMs = 0;
            Ready.Clear();
            Progress.Clear();
            Finished.Clear();
            Dnf.Clear();
            PeersDown.Clear();
            RematchWanted.Clear();
            LastResult = null;
            Players.Clear();
            PeerHorses.Clear();
            ApplyPlayers(d["players"] as JArray);
            _outgoing = "";
            _lastPoseMs = -1;
            _poseSeq = 0;
            Joined = false;
            JoinRoom();
            SessionReady?.Invoke();
            Changed?.Invoke();
        }

        private void JoinRoom()
        {
            if (!IsActive || !_session.IsOnline) return;
            var room = RoomId;
            _session.Request("ROOM_JOIN", new JObject { ["room"] = room }, r =>
            {
                if (RoomId != room) return;
                Joined = r.Ok;
                if (r.Ok) SendHorse();
                if (!r.Ok && r.Code == "BAD_ROOM")
                {
                    // the session is gone on the server (rejoin window over, round finished without us)
                    Reset();
                    Ended?.Invoke("");
                }
                else if (r.Ok && IsMatch && Phase != PhaseLobby) RequestSnapshot();
                Changed?.Invoke();
            });
        }

        private void RequestSnapshot()
        {
            var room = RoomId;
            _session.Request("MG_RESUME", new JObject { ["room"] = room }, r =>
            {
                if (RoomId != room) return;
                if (r.Ok && r.Type == "MG_SNAPSHOT") OnSnapshot(r.Data);
            });
        }

        private void OnSnapshot(JObject d)
        {
            if (!IsActive) RoomId = NetJson.Str(d["room"]);
            Mode = NetJson.Str(d["mode"], Mode);
            Kurs = NetJson.Str(d["kurs"], Kurs);
            Host = Upper(NetJson.Str(d["host"], Host));
            ApplyPlayers(d["players"] as JArray);
            Phase = NetJson.Str(d["phase"], Phase);
            Seed = NetJson.Long(d["seed"], Seed);
            StartAtMs = NetJson.Long(d["startAt"], StartAtMs);
            EndsAtMs = NetJson.Long(d["endsAt"], EndsAtMs);
            SyncClock(d["serverNow"]);
            if (d["ready"] is JArray ready) ReadReady(ready);
            Progress.Clear();
            if (d["progress"] is JObject progress) foreach (var p in progress.Properties()) Progress[Upper(p.Name)] = NetJson.Int(p.Value);
            Finished.Clear();
            if (d["finished"] is JObject fin)
                foreach (var p in fin.Properties())
                    if (p.Value is JObject f) Finished[Upper(p.Name)] = (NetJson.Int(f["timeMs"]), NetJson.Int(f["rank"]));
            Dnf.Clear();
            if (d["dnf"] is JArray dnf) foreach (var t in dnf) Dnf.Add(Upper(t.ToString()));
            if (d["tag"] is JObject tag)
            {
                ItCode = Upper(NetJson.Str(tag["it"]));
                ImmunityUntilMs = NetJson.Long(tag["immunityUntil"]);
            }
            PeersDown.Clear();
            SnapshotReceived?.Invoke(d);
            Changed?.Invoke();
        }

        private void ApplyState(JObject d)
        {
            if (d["progress"] is JObject progress) foreach (var p in progress.Properties()) Progress[Upper(p.Name)] = NetJson.Int(p.Value);
            if (d["finished"] is JObject fin)
            {
                var code = Upper(NetJson.Str(fin["friendCode"]));
                if (code.Length > 0) Finished[code] = (NetJson.Int(fin["timeMs"]), NetJson.Int(fin["rank"]));
            }
            if (d["dnf"] is JObject dnf)
            {
                var code = Upper(NetJson.Str(dnf["friendCode"]));
                if (code.Length > 0) Dnf.Add(code);
            }
            if (d["tag"] is JObject tag)
            {
                ItCode = Upper(NetJson.Str(tag["it"], ItCode));
                ImmunityUntilMs = NetJson.Long(tag["immunityUntil"], ImmunityUntilMs);
            }
        }

        private void ApplyPlayers(JArray rows)
        {
            if (rows == null) return;
            Players.Clear();
            foreach (var t in rows)
            {
                if (!(t is JObject row)) continue;
                var code = Upper(NetJson.Str(row["friendCode"]));
                if (code.Length == 0) continue;
                Players.Add(new RmpPlayer { Code = code, Name = NetJson.Str(row["name"], "?"), GoobyName = NetJson.Str(row["goobyName"], "Gooby") });
            }
        }

        private void ReadReady(JArray ready)
        {
            Ready.Clear();
            foreach (var t in ready) Ready.Add(Upper(t.ToString()));
        }

        private void SyncClock(JToken serverNow)
        {
            var s = NetJson.Long(serverNow);
            if (s > 0) ServerOffsetMs = s - _now();
        }

        private static RmpResult ReadResult(JObject d)
        {
            var r = ReadLine(d);
            if (d["results"] is JArray rows)
                foreach (var t in rows)
                    if (t is JObject row) r.All.Add(ReadLine(row));
            r.All.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            return r;
        }

        private static RmpResult ReadLine(JObject d) => new RmpResult
        {
            FriendCode = Upper(NetJson.Str(d["friendCode"])),
            Rank = NetJson.Int(d["rank"]),
            ZeitMs = NetJson.Int(d["zeitMs"]),
            Dnf = NetJson.Bool(d["dnf"]),
            Ranked = NetJson.Bool(d["ranked"]),
            RewardId = NetJson.Str(d["rewardId"]),
            Mode = NetJson.Str(d["mode"]),
            Kurs = NetJson.Str(d["kurs"]),
            Room = NetJson.Str(d["room"]),
        };

        private void OnWelcome(JObject d)
        {
            // after a reconnect the server dropped us from every room: sit down again (the snapshot follows in the rejoin window)
            if (IsActive)
            {
                Joined = false;
                JoinRoom();
            }
            if (d?["rmpPending"] is JArray pending)
                foreach (var t in pending)
                {
                    if (!(t is JObject row)) continue;
                    var result = ReadResult(row);
                    if (PendingResults.Exists(p => p.RewardId == result.RewardId)) continue;
                    PendingResults.Add(result);
                    ResultReceived?.Invoke(result);
                }
        }

        private void OnStatus(NetStatus s)
        {
            if (s == NetStatus.Online || !IsActive) return;
            Joined = false;
            Changed?.Invoke();
        }

        private void Reset()
        {
            RoomId = "";
            Mode = "";
            Kurs = "";
            Host = "";
            Phase = "";
            Seed = 0;
            StartAtMs = 0;
            EndsAtMs = 0;
            ItCode = "";
            ImmunityUntilMs = 0;
            Joined = false;
            Rematch = false;
            HostMetaRev = -1;
            Players.Clear();
            PeerHorses.Clear();
            Ready.Clear();
            Progress.Clear();
            Finished.Clear();
            Dnf.Clear();
            PeersDown.Clear();
            RematchWanted.Clear();
            LastResult = null;
            _lastPoseMs = -1;
            _poseSeq = 0;
        }

        private static double Round(double v, double scale) => Math.Round(v * scale) / scale;

        private static string Upper(string s) => (s ?? "").Trim().ToUpperInvariant();
    }
}
