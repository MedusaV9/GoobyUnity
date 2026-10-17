using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>An incoming GVZ_INVITED (valid for <see cref="ExpiresInMs"/> after <see cref="ReceivedAtMs"/>).</summary>
    public sealed class GvzInvite
    {
        public string From { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string GoobyName { get; internal set; } = "";
        public int ExpiresInMs { get; internal set; } = GvzNetClient.InviteTtlMs;
        public long ReceivedAtMs { get; internal set; }
        public long ExpiresAtMs => ReceivedAtMs + ExpiresInMs;
    }

    /// <summary>A booked PvP result (GVZ_RESULT push or WELCOME.gvzPending), personalised for the receiver.</summary>
    public sealed class GvzNetResult
    {
        public string MatchId = "", Winner = "", Side = "", RewardId = "", Room = "";
        public bool Won;
        public int Coins, Tick;
    }

    /// <summary>
    /// Client side of GvZ's online PvP (GOOBY-SERVER gvzmp.js; port of gvz_net_session.gd, built like
    /// <see cref="GobnomNetClient"/>): invites (GVZ_INVITE → GVZ_INVITED, GVZ_ACCEPT → GVZ_READY to both; the inviter
    /// defends as "gooby", the guest attacks as "zombie"), the gvz: room, the start handshake (BOTH send GVZ_START_REQ →
    /// GVZ_START with the server's seed), the lockstep channels as ROOM_MSG kinds (GP_INPUT, GP_HASH), peer down/up, desync,
    /// abort and the idempotent result with the small coin reward (GVZ_RESULT + ACK, WELCOME.gvzPending). Deliberately
    /// without a rejoin snapshot (short matches): a returning player re-joins, the hash guard decides whether the match can
    /// go on. Main-thread only.
    /// </summary>
    public sealed class GvzNetClient
    {
        public const int InviteTtlMs = 30000;
        public const string SideGooby = "gooby", SideZombie = "zombie";

        private readonly NetSession _session;
        private readonly Func<string> _myCode;
        private readonly Func<long> _now;
        private readonly List<GvzInvite> _incoming = new List<GvzInvite>();
        private string _outgoing = "";
        private long _outgoingAtMs;

        public GvzNetClient(SocialClient social, Func<long> nowMs = null)
            : this(social?.Session, () => social?.FriendCode ?? "", nowMs)
        {
        }

        public GvzNetClient(NetSession session, Func<string> myCode, Func<long> nowMs = null)
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
        public string RoomId { get; private set; } = "";
        public string MySide { get; private set; } = SideGooby;
        public string PartnerCode { get; private set; } = "";
        public string PartnerName { get; private set; } = "";
        public string PartnerGoobyName { get; private set; } = "Gooby";
        public int InputDelay { get; private set; } = 4;
        public int HashEveryTicks { get; private set; } = 60;
        public long Seed { get; private set; }
        public bool Running { get; private set; }
        public bool PeerDown { get; private set; }
        public bool Joined { get; private set; }
        /// <summary>Our GVZ_START_REQ is out (the lobby says "Warte auf …").</summary>
        public bool StartRequested { get; private set; }
        public List<GvzNetResult> PendingResults { get; } = new List<GvzNetResult>();

        public bool IsPaired => RoomId.Length > 0;

        public string OutgoingInvite
        {
            get
            {
                if (_outgoing.Length > 0 && _now() - _outgoingAtMs >= InviteTtlMs) _outgoing = "";
                return _outgoing;
            }
        }

        public IReadOnlyList<GvzInvite> IncomingInvites
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
        public event Action<GvzInvite> InviteIncoming;
        public event Action<string> InviteDeclined;
        /// <summary>Paired (GVZ_READY): sides are fixed, both may confirm the start.</summary>
        public event Action SessionReady;
        /// <summary>GVZ_START: both devices build the identical match from <see cref="Seed"/>.</summary>
        public event Action GameStarted;
        public event Action<JObject> FrameReceived;
        public event Action<int, string> HashReceived;
        public event Action<GvzNetResult> ResultConfirmed;
        public event Action<int> DesyncReported;
        public event Action<bool, int> PeerConnectionChanged;
        /// <summary>(reason, byCode): left | timeout.</summary>
        public event Action<string, string> Aborted;

        // ── invites ──

        public void Invite(string targetCode, Action<NetReply> cb)
        {
            var target = Upper(targetCode);
            _session.Request("GVZ_INVITE", new JObject { ["target"] = target }, r =>
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

        /// <summary>GVZ_ACCEPT {from}; the reply IS the GVZ_READY (SessionReady fires before <paramref name="cb"/>).</summary>
        public void Accept(string fromCode, Action<NetReply> cb)
        {
            var from = Upper(fromCode);
            _incoming.RemoveAll(i => i.From == from);
            _session.Request("GVZ_ACCEPT", new JObject { ["from"] = from }, r =>
            {
                if (r.Ok && r.Type == "GVZ_READY") OnReady(r.Data);
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        public void Decline(string fromCode, Action<NetReply> cb = null)
        {
            var from = Upper(fromCode);
            _incoming.RemoveAll(i => i.From == from);
            _session.Request("GVZ_DECLINE", new JObject { ["from"] = from }, r => cb?.Invoke(r));
            Changed?.Invoke();
        }

        /// <summary>Confirms the start (both confirmed → the server pushes GVZ_START with its seed).</summary>
        public void RequestStart(Action<NetReply> cb = null)
        {
            if (!IsPaired)
            {
                cb?.Invoke(NetReply.Fail("NOT_IN_ROOM"));
                return;
            }
            var room = RoomId;
            StartRequested = true;
            Changed?.Invoke();
            _session.Request("GVZ_START_REQ", new JObject { ["room"] = room }, r =>
            {
                if (!r.Ok && RoomId == room && !Running) StartRequested = false;
                cb?.Invoke(r);
                Changed?.Invoke();
            });
        }

        // ── lockstep channels (fire-and-forget) ──

        public bool SendFrame(JObject frame) => SendRoomMessage("GP_INPUT", frame);
        public bool SendHash(int tick, string hash) => SendRoomMessage("GP_HASH", new JObject { ["t"] = tick, ["h"] = hash });

        private bool SendRoomMessage(string kind, JObject body)
        {
            if (!IsPaired || body == null || !_session.IsOnline) return false;
            return _session.Send("ROOM_MSG", new JObject { ["room"] = RoomId, ["kind"] = kind, ["body"] = body }) >= 0;
        }

        // ── result + leaving ──

        public void ReportResult(string winner, int tick, Action<NetReply> cb = null)
        {
            if (!IsPaired)
            {
                cb?.Invoke(NetReply.Fail("NOT_IN_ROOM"));
                return;
            }
            _session.Request("GVZ_RESULT", new JObject { ["room"] = RoomId, ["winner"] = winner, ["tick"] = tick }, r => cb?.Invoke(r));
        }

        public void AckResult(string rewardId)
        {
            if (!string.IsNullOrEmpty(rewardId)) _session.Send("GVZ_RESULT_ACK", new JObject { ["rewardId"] = rewardId });
        }

        /// <summary>Leaves cleanly: the server ends the session for the partner (no forfeit winner); challenges are free again.</summary>
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
                case "GVZ_INVITED":
                {
                    var inv = new GvzInvite
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
                case "GVZ_DECLINED":
                {
                    var by = Upper(NetJson.Str(d["from"]));
                    if (_outgoing == by) _outgoing = "";
                    InviteDeclined?.Invoke(by);
                    Changed?.Invoke();
                    break;
                }
                case "GVZ_READY":
                    OnReady(d);
                    break;
                case "GVZ_START":
                    if (room != RoomId) return;
                    Seed = NetJson.Long(d["seed"]);
                    InputDelay = NetJson.Int(d["inputDelay"], InputDelay);
                    HashEveryTicks = NetJson.Int(d["hashEveryTicks"], HashEveryTicks);
                    ApplyPlayers(d["players"] as JArray);
                    Running = true;
                    PeerDown = false;
                    StartRequested = false;
                    GameStarted?.Invoke();
                    Changed?.Invoke();
                    break;
                case "GVZ_PEER_DOWN":
                    if (room != RoomId) return;
                    PeerDown = true;
                    PeerConnectionChanged?.Invoke(true, NetJson.Int(d["waitMs"]));
                    Changed?.Invoke();
                    break;
                case "GVZ_PEER_UP":
                    if (room != RoomId) return;
                    PeerDown = false;
                    PeerConnectionChanged?.Invoke(false, 0);
                    Changed?.Invoke();
                    break;
                case "GVZ_DESYNC":
                    if (room != RoomId) return;
                    Running = false;
                    DesyncReported?.Invoke(NetJson.Int(d["tick"], -1));
                    Changed?.Invoke();
                    break;
                case "GVZ_ABORTED":
                {
                    if (room != RoomId) return;
                    var reason = NetJson.Str(d["reason"]);
                    var by = NetJson.Str(d["by"]);
                    Reset();
                    Aborted?.Invoke(reason, by);
                    Changed?.Invoke();
                    break;
                }
                case "GVZ_RESULT":
                {
                    if (room != RoomId) return;
                    Running = false;
                    var result = ReadResult(d);
                    ResultConfirmed?.Invoke(result);
                    AckResult(result.RewardId);
                    Changed?.Invoke();
                    break;
                }
                case "ROOM_MSG":
                    if (room != RoomId || !(d["body"] is JObject body)) return;
                    switch (NetJson.Str(d["kind"]))
                    {
                        case "GP_INPUT": FrameReceived?.Invoke(body); break;
                        case "GP_HASH": HashReceived?.Invoke(NetJson.Int(body["t"], -1), NetJson.Str(body["h"])); break;
                    }
                    break;
            }
        }

        private void OnReady(JObject d)
        {
            RoomId = NetJson.Str(d["room"]);
            InputDelay = NetJson.Int(d["inputDelay"], 4);
            HashEveryTicks = NetJson.Int(d["hashEveryTicks"], 60);
            Running = false;
            PeerDown = false;
            StartRequested = false;
            Seed = 0;
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
                    Reset();
                    Aborted?.Invoke("expired", "");
                }
                Changed?.Invoke();
            });
        }

        private void ApplyPlayers(JArray rows)
        {
            if (rows == null) return;
            foreach (var t in rows)
            {
                if (!(t is JObject row)) continue;
                var code = Upper(NetJson.Str(row["friendCode"]));
                if (code == Upper(MyCode)) MySide = NetJson.Str(row["side"], SideGooby) == SideZombie ? SideZombie : SideGooby;
                else
                {
                    PartnerCode = code;
                    PartnerName = NetJson.Str(row["name"], "?");
                    PartnerGoobyName = NetJson.Str(row["goobyName"], "Gooby");
                }
            }
        }

        private static GvzNetResult ReadResult(JObject d) => new GvzNetResult
        {
            MatchId = NetJson.Str(d["matchId"]),
            Winner = NetJson.Str(d["winner"]),
            Side = NetJson.Str(d["side"]),
            Won = NetJson.Bool(d["won"]),
            Coins = NetJson.Int(d["coins"]),
            Tick = NetJson.Int(d["tick"]),
            RewardId = NetJson.Str(d["rewardId"]),
            Room = NetJson.Str(d["room"]),
        };

        private void OnWelcome(JObject d)
        {
            // after a reconnect the server dropped us from every room: sit down again (the partner gets GVZ_PEER_UP)
            if (IsPaired)
            {
                Joined = false;
                JoinRoom();
            }
            if (d?["gvzPending"] is JArray pending)
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
            Changed?.Invoke();
        }

        private void Reset()
        {
            RoomId = "";
            PartnerCode = "";
            PartnerName = "";
            PartnerGoobyName = "Gooby";
            Seed = 0;
            Running = false;
            PeerDown = false;
            Joined = false;
            StartRequested = false;
        }

        private static string Upper(string s) => (s ?? "").Trim().ToUpperInvariant();
    }
}
