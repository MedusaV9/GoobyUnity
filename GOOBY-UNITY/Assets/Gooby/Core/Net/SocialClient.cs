using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>Role of the local player in the current house visit.</summary>
    public enum VisitRole
    {
        None,
        Host,
        Guest,
    }

    /// <summary>A friend row (FRIENDS_STATE / FRIEND_ADDED / FRIEND_PRESENCE).</summary>
    public sealed class FriendInfo
    {
        public string Code { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string GoobyName { get; internal set; } = "";
        public bool Online { get; internal set; }
        /// <summary>Presence kind ("garden", "minigame:x", "online"…); "" when offline.</summary>
        public string ActivityKind { get; internal set; } = "";
        /// <summary>German label built by the server ("ist mit Flausch im Garten").</summary>
        public string ActivityLabel { get; internal set; } = "";
        /// <summary>Server-side coin DISPLAY cache (last SYNC of that friend).</summary>
        public int Coins { get; internal set; }
        public long CoinsUpdatedAt { get; internal set; }
        /// <summary>Unix ms (server clock) of the last connect/disconnect.</summary>
        public long LastSeenAt { get; internal set; }

        internal static FriendInfo FromJson(JObject o)
        {
            if (o == null) return null;
            var code = NetJson.Str(o["friendCode"]);
            if (code.Length == 0) return null;
            var f = new FriendInfo
            {
                Code = code,
                Name = NetJson.Str(o["name"]),
                GoobyName = NetJson.Str(o["goobyName"], "Gooby"),
                Online = NetJson.Bool(o["online"]),
                Coins = NetJson.Int(o["coins"]),
                CoinsUpdatedAt = NetJson.Long(o["coinsUpdatedAt"]),
                LastSeenAt = NetJson.Long(o["lastSeenAt"]),
            };
            f.ApplyActivity(o["activity"] as JObject);
            return f;
        }

        internal void ApplyActivity(JObject activity)
        {
            ActivityKind = NetJson.Str(activity?["kind"]);
            ActivityLabel = NetJson.Str(activity?["label"]);
        }

        public override string ToString() => Code + " " + Name + (Online ? " (online: " + ActivityKind + ")" : " (offline)");
    }

    /// <summary>An incoming friend request.</summary>
    public sealed class FriendRequestInfo
    {
        public string FromCode { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public string GoobyName { get; internal set; } = "";
        /// <summary>Unix ms (server clock).</summary>
        public long At { get; internal set; }

        internal static FriendRequestInfo FromJson(JObject o)
        {
            if (o == null) return null;
            var from = NetJson.Str(o["from"]);
            if (from.Length == 0) return null;
            return new FriendRequestInfo
            {
                FromCode = from,
                Name = NetJson.Str(o["name"]),
                GoobyName = NetJson.Str(o["goobyName"], "Gooby"),
                At = NetJson.Long(o["at"]),
            };
        }
    }

    /// <summary>A letter in the mailbox (server client view of mail.js).</summary>
    public sealed class MailInfo
    {
        public string Id { get; internal set; } = "";
        public string From { get; internal set; } = "";
        public string FromName { get; internal set; } = "";
        public string FromGooby { get; internal set; } = "";
        /// <summary>Unix ms (server clock).</summary>
        public long At { get; internal set; }
        public string Text { get; internal set; } = "";
        /// <summary>"" = no photo (photos load lazily via REST /api/mail/blob/:id).</summary>
        public string PhotoId { get; internal set; } = "";
        /// <summary>Gift type "food" | "items"; "" = no gift.</summary>
        public string ItemTyp { get; internal set; } = "";
        public string ItemId { get; internal set; } = "";
        public int ItemMenge { get; internal set; }
        public bool Read { get; internal set; }
        public bool Claimed { get; internal set; }

        public bool HasItem => ItemTyp.Length > 0 && ItemId.Length > 0;

        internal static MailInfo FromJson(JObject o)
        {
            if (o == null) return null;
            var id = NetJson.Str(o["id"]);
            if (id.Length == 0) return null;
            var m = new MailInfo
            {
                Id = id,
                From = NetJson.Str(o["from"]),
                FromName = NetJson.Str(o["fromName"]),
                FromGooby = NetJson.Str(o["fromGooby"]),
                At = NetJson.Long(o["at"]),
                Text = NetJson.Str(o["text"]),
                PhotoId = NetJson.Str(o["photoId"]),
                Read = NetJson.Bool(o["read"]),
                Claimed = NetJson.Bool(o["claimed"]),
            };
            if (o["item"] is JObject item)
            {
                m.ItemTyp = NetJson.Str(item["typ"]);
                m.ItemId = NetJson.Str(item["id"]);
                m.ItemMenge = NetJson.Int(item["menge"], 1);
            }
            return m;
        }
    }

    /// <summary>Outcome of <see cref="SocialClient.SendMail"/>.</summary>
    public sealed class MailSendResult
    {
        /// <summary>Delivered (or recognised as an already delivered retry, see <see cref="Dupe"/>).</summary>
        public bool Ok { get; internal set; }
        /// <summary>Not sent yet — stored in the outbox, sent automatically when online.</summary>
        public bool Queued { get; internal set; }
        /// <summary>"" on success, "QUEUED" when queued, else the failure code (NOT_FRIENDS, DAILY_LIMIT…).</summary>
        public string Code { get; internal set; } = "";
        public string Id { get; internal set; } = "";
        public string ClientId { get; internal set; } = "";
        public bool Dupe { get; internal set; }
        public int SentToday { get; internal set; }
        public int DailyLimit { get; internal set; }
        /// <summary>A photo letter that could not go over REST (offline/no connection) travelled as text only.</summary>
        public bool PhotoDropped { get; internal set; }

        public override string ToString() => Ok ? "OK(" + Id + ")" : Queued ? "QUEUED" : "FAIL(" + Code + ")";
    }

    /// <summary>
    /// High-level GOOBY social client for the Unity UI: connection (server address typed by the
    /// player), friends + presence, letters with offline outbox, GoobyPal coin gifts, server events
    /// and house visits. Main-thread only — call <see cref="Pump"/> every frame; ALL events and
    /// callbacks are raised from inside Pump (immediate local failures such as OFFLINE invoke the
    /// callback synchronously). Payload shapes match the Godot client, so both can meet.
    /// </summary>
    public sealed partial class SocialClient
    {
        public const string IdentityFileName = "net_identity.json";
        public const string OutboxFileName = "net_outbox.json";
        public const int TextMax = 500;
        public const int GiftQtyMax = 99;
        public const int MailPageSize = 50;
        /// <summary>POS relay rate (5 Hz — the server bucket is 5/s, burst 10).</summary>
        public const int PosIntervalMs = 200;
        public const int CoinsSyncIntervalMs = 30000;
        public const int CoinsBigDelta = 100;
        public const int CoinsBigDeltaMinIntervalMs = 2000;
        /// <summary>Client presence bucket (server: 12/min, burst 12) — stays below it.</summary>
        public const int PresenceBurst = 10;
        public const int PresenceRefillMs = 5000;
        public const int OutboxRetryMs = 60000;

        private static readonly Regex FriendCodeRe = new Regex("^GOOBY-[A-HJ-NP-Z2-9]{4}$");

        private readonly Func<long> _now;
        private readonly INetHttp _http;
        private readonly List<FriendInfo> _friends = new List<FriendInfo>();
        private readonly List<FriendRequestInfo> _requests = new List<FriendRequestInfo>();
        private readonly List<MailInfo> _mail = new List<MailInfo>();
        private readonly HashSet<string> _seenEvents = new HashSet<string>();
        private bool _dirty;

        // presence
        private string _presenceWanted = "";
        private string _presenceSent;
        private double _presenceTokens = PresenceBurst;
        private long _presenceTokensAt = -1;

        // coins
        private int _coinsWanted = -1;
        private int _coinsSent = -1;
        private long _coinsSentAt = -1;

        // outbox
        private long _outboxRetryAt = -1;

        // visit
        private long _lastPosMs = -1;
        private long _hostGoneAtMs = -1;

        /// <summary>Guest side: how long after the host left the room without VISIT_ENDED the visit ends locally.</summary>
        public const int HostGoneGraceMs = 3000;

        public SocialClient(string dataDir, Func<INetTransport> transportFactory, Func<long> nowMs, INetHttp http)
        {
            if (string.IsNullOrEmpty(dataDir)) throw new ArgumentException("dataDir required", nameof(dataDir));
            _now = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            try { Directory.CreateDirectory(dataDir); } catch (Exception e) { NetLog.Warn("[social] cannot create " + dataDir + ": " + e.Message); }
            DataDir = dataDir;
            Identity = NetIdentity.LoadOrCreate(Path.Combine(dataDir, IdentityFileName));
            Outbox = new NetOutbox(Path.Combine(dataDir, OutboxFileName));
            Session = new NetSession(Identity, transportFactory, nowMs);
            Session.StatusChanged += OnStatusChanged;
            Session.Welcome += OnWelcome;
            Session.Push += OnPush;
        }

        /// <summary>Production wiring: WebSocketTransport + HttpClientNetHttp + monotonic Stopwatch clock.</summary>
        public static SocialClient CreateDefault(string dataDir)
        {
            var sw = Stopwatch.StartNew();
            return new SocialClient(dataDir, () => new WebSocketTransport(), () => sw.ElapsedMilliseconds, new HttpClientNetHttp());
        }

        // ------------------------------------------------------------------ state

        public string DataDir { get; }
        public NetSession Session { get; }
        public NetIdentity Identity { get; }
        public NetOutbox Outbox { get; }
        /// <summary>Sent in HELLO (appVersion). Applies on the next connect.</summary>
        public string AppVersion { get; set; } = "unity";

        public NetStatus Status => Session.Status;
        public bool IsOnline => Session.IsOnline;
        public string LastErrorCode => Session.LastErrorCode;
        public NetEndpoint Endpoint => Session.Endpoint;
        public string FriendCode => Identity.FriendCode ?? "";
        public string PlayerName => Session.PlayerName;
        public string GoobyName => Session.GoobyName;

        public IReadOnlyList<FriendInfo> Friends => _friends;
        public IReadOnlyList<FriendRequestInfo> Requests => _requests;
        /// <summary>Mailbox as last listed (unread first, then newest) plus live arrivals on top.</summary>
        public IReadOnlyList<MailInfo> Mail => _mail;
        public int MailUnread { get; private set; }
        public int MailTotal { get; private set; }
        public int InstantUnseen { get; private set; }
        public int OutboxCount => Outbox.Count;
        /// <summary>Desired presence kind (re-sent after every WELCOME).</summary>
        public string PresenceKind => _presenceWanted;

        public string VisitRoom { get; private set; } = "";
        public VisitRole VisitRole { get; private set; } = VisitRole.None;
        public string VisitHost { get; private set; } = "";
        public string VisitGuest { get; private set; } = "";
        public int VisitRev { get; private set; }
        /// <summary>Friend code of the other visit participant.</summary>
        public string PeerCode { get; private set; } = "";
        public string PeerName { get; private set; } = "";
        public string PeerGoobyName { get; private set; } = "";
        /// <summary>True while the peer is in the visit room.</summary>
        public bool PeerInRoom { get; private set; }
        /// <summary>Last room id ("living", "kitchen"…) reported by the peer's POS.</summary>
        public string PeerRoomId { get; private set; } = "";
        /// <summary>True once our ROOM_JOIN succeeded (POS/EMOTE can be sent).</summary>
        public bool VisitJoined { get; private set; }
        public bool VisitActive => VisitRoom.Length > 0;

        // ------------------------------------------------------------------ events

        /// <summary>Any list/state change (coalesced, raised at the end of Pump) — refresh the UI.</summary>
        public event Action Changed;
        public event Action<NetStatus> StatusChanged;
        public event Action<FriendRequestInfo> FriendRequestIncoming;
        public event Action<FriendInfo> FriendAdded;
        /// <summary>Friend code of the removed friend.</summary>
        public event Action<string> FriendRemoved;
        /// <summary>A friend went online/offline.</summary>
        public event Action<FriendInfo> FriendOnlineChanged;
        /// <summary>Any FRIEND_PRESENCE update (activity change included).</summary>
        public event Action<FriendInfo> FriendPresenceChanged;
        public event Action<MailInfo> MailArrived;
        /// <summary>(to, item or null, code): a queued letter was definitively rejected — refund postage/gift.</summary>
        public event Action<string, JObject, string> MailBounced;
        /// <summary>
        /// (deliveryId, fromCode, amount): credit the coins in the handler. The client ACKs right after
        /// and remembers the id, so re-deliveries are never raised twice. Without a subscriber the
        /// delivery stays pending on the server (not ACKed).
        /// </summary>
        public event Action<string, string, int> PalReceived;
        /// <summary>(fromCode, name, goobyName): a friend wants to visit — AcceptVisit/DeclineVisit within 30 s.</summary>
        public event Action<string, string, string> VisitIncoming;
        /// <summary>(room, hostCode, guestCode, rev): visit started and the room was joined.</summary>
        public event Action<string, string, string, int> VisitReady;
        /// <summary>(hostCode): the host declined our request.</summary>
        public event Action<string> VisitDenied;
        /// <summary>(room, byCode): the visit is over (state already reset).</summary>
        public event Action<string, string> VisitEnded;
        /// <summary>(friendCode, name, goobyName) of the peer entering the visit room.</summary>
        public event Action<string, string, string> PeerJoined;
        /// <summary>(friendCode) of the peer leaving the visit room.</summary>
        public event Action<string> PeerLeft;
        /// <summary>(x, z, anim, roomId) — world XZ of the peer's Gooby.</summary>
        public event Action<float, float, string, string> PeerPos;
        public event Action<string> PeerEmote;
        /// <summary>Every ROOM_MSG of the visit room (kind, body) — incl. POS/EMOTE and BUILD_* relays.</summary>
        public event Action<string, JObject> RoomMessage;
        /// <summary>Admin event {id, type, params, at, expiresAt}; ACKed after the handler ran.</summary>
        public event Action<JObject> ServerEvent;
        /// <summary>GOING_DOWN reason (SHUTDOWN, REPLACED, BANNED).</summary>
        public event Action<string> GoingDown;

        // ------------------------------------------------------------------ connection

        /// <summary>
        /// Sets server + names (+ optional join secret). A changed endpoint/secret reconnects when a
        /// connection is wanted and clears the per-server lists; changed names are pushed via PROFILE_UPDATE.
        /// </summary>
        public void Configure(NetEndpoint endpoint, string playerName, string goobyName, string joinSecret)
        {
            var ep = endpoint ?? NetEndpoint.Invalid;
            var oldEp = Session.Endpoint;
            var oldSecret = Session.JoinSecret;
            var oldName = Session.PlayerName;
            var oldGooby = Session.GoobyName;
            Session.Configure(ep, playerName, goobyName, joinSecret, AppVersion);
            var serverChanged = !ep.Equals(oldEp);
            var secretChanged = (joinSecret ?? "") != (oldSecret ?? "");
            if (serverChanged && oldEp.Valid)
            {
                ClearServerState(); // friend codes, lists and mail belong to the previous server
            }
            if ((serverChanged || secretChanged) && Session.WantsConnection)
            {
                Session.Reconnect();
            }
            else if (Session.IsOnline && (oldName != Session.PlayerName || oldGooby != Session.GoobyName))
            {
                Session.Send("PROFILE_UPDATE", new JObject { ["name"] = Session.PlayerName, ["goobyName"] = Session.GoobyName });
            }
            MarkDirty();
        }

        public void Connect() => Session.Connect();

        public void Disconnect() => Session.Disconnect();

        /// <summary>Main-thread tick — call every frame.</summary>
        public void Pump()
        {
            try { _http.Pump(); } catch (Exception e) { NetLog.Error("[social] http.Pump threw: " + e); }
            Session.Pump();
            if (_moveReconnect)
            {
                // outside the session's own pump: the old link's MOVED close must not cancel the new connect
                _moveReconnect = false;
                Session.Disconnect();
                Session.Connect();
            }
            var now = _now();
            TickPresence(now);
            TickCoins(now);
            TickOutbox(now);
            if (_hostGoneAtMs >= 0 && now - _hostGoneAtMs >= HostGoneGraceMs)
            {
                _hostGoneAtMs = -1;
                if (VisitActive && VisitRole == VisitRole.Guest && !PeerInRoom) EndVisitLocally(VisitRoom, VisitHost);
            }
            if (_dirty)
            {
                _dirty = false;
                Raise(() => Changed?.Invoke(), "Changed");
            }
        }

        // ------------------------------------------------------------------ friends

        public void RefreshFriends(Action<NetReply> cb = null)
        {
            Session.Request("FRIENDS_LIST", new JObject(), r =>
            {
                if (r.Ok && r.Type == "FRIENDS_STATE") ApplyFriendsState(r.Data);
                Callback(cb, r);
            });
        }

        /// <summary>Friend request by code ("GOOBY-XXXX" → target) or unique player name (→ targetName).</summary>
        public void AddFriend(string codeOrName, Action<NetReply> cb)
        {
            var trimmed = (codeOrName ?? "").Trim();
            if (trimmed.Length == 0)
            {
                Callback(cb, NetReply.Fail(NetErrorCodes.BadMessage));
                return;
            }
            var upper = trimmed.ToUpperInvariant();
            var d = FriendCodeRe.IsMatch(upper) ? new JObject { ["target"] = upper } : new JObject { ["targetName"] = trimmed };
            Session.Request("FRIEND_REQUEST", d, r => Callback(cb, r));
        }

        public void AcceptFriend(string code, Action<NetReply> cb)
        {
            var c = Upper(code);
            Session.Request("FRIEND_ACCEPT", new JObject { ["target"] = c }, r =>
            {
                if (r.Ok) RemoveRequest(c);
                Callback(cb, r);
            });
        }

        public void DeclineFriend(string code, Action<NetReply> cb)
        {
            var c = Upper(code);
            Session.Request("FRIEND_DECLINE", new JObject { ["target"] = c }, r =>
            {
                if (r.Ok) RemoveRequest(c);
                Callback(cb, r);
            });
        }

        public void RemoveFriend(string code, Action<NetReply> cb)
        {
            var c = Upper(code);
            Session.Request("FRIEND_REMOVE", new JObject { ["target"] = c }, r =>
            {
                if (r.Ok) RemoveFriendRow(c, false);
                Callback(cb, r);
            });
        }

        public FriendInfo FindFriend(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            foreach (var f in _friends)
                if (string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        // ------------------------------------------------------------------ presence / coins

        /// <summary>Reports the current activity (see <see cref="PresenceKinds"/>). Deduped, rate-limited, re-sent after reconnects.</summary>
        public void SetPresence(string kind)
        {
            _presenceWanted = PresenceKinds.Clamp(kind);
            TickPresence(_now());
        }

        /// <summary>Coins display cache for friends (SYNC): at most every 30 s, sooner for changes ≥ 100.</summary>
        public void SyncCoins(int coins)
        {
            _coinsWanted = Math.Max(0, Math.Min(100_000_000, coins)); // server ignores values outside 0..1e8
            TickCoins(_now());
        }

        private void TickPresence(long now)
        {
            if (!Session.IsOnline || _presenceWanted.Length == 0 || _presenceWanted == _presenceSent) return;
            if (!TakePresenceToken(now)) return;
            var kind = _presenceWanted;
            _presenceSent = kind;
            Session.Request("PRESENCE_SET", new JObject { ["kind"] = kind }, r =>
            {
                if (!r.Ok && _presenceSent == kind) _presenceSent = null; // retry later (next token)
            });
        }

        private bool TakePresenceToken(long now)
        {
            if (_presenceTokensAt < 0) _presenceTokensAt = now;
            _presenceTokens = Math.Min(PresenceBurst, _presenceTokens + (now - _presenceTokensAt) / (double)PresenceRefillMs);
            _presenceTokensAt = now;
            if (_presenceTokens < 1) return false;
            _presenceTokens -= 1;
            return true;
        }

        private void TickCoins(long now)
        {
            if (_coinsWanted < 0 || !Session.IsOnline) return;
            if (_coinsSentAt >= 0 && _coinsWanted == _coinsSent) return;
            var first = _coinsSentAt < 0;
            var intervalOk = first || now - _coinsSentAt >= CoinsSyncIntervalMs;
            var big = !first && Math.Abs(_coinsWanted - _coinsSent) >= CoinsBigDelta && now - _coinsSentAt >= CoinsBigDeltaMinIntervalMs;
            if (!intervalOk && !big) return;
            Session.Send("SYNC", new JObject { ["coins"] = _coinsWanted });
            _coinsSent = _coinsWanted;
            _coinsSentAt = now;
        }

        // ------------------------------------------------------------------ mail

        /// <summary>
        /// Sends a letter (text ≤ 500, optional gift typ "food"|"items" + id + menge 1..99) to a friend.
        /// Offline or transport failure → queued in the outbox (Queued=true), sent after the next WELCOME.
        /// </summary>
        public void SendMail(string to, string text, string itemTyp, string itemId, int menge, Action<MailSendResult> cb)
        {
            var target = Upper(to);
            var body = text ?? "";
            var hasItem = !string.IsNullOrEmpty(itemTyp) && !string.IsNullOrEmpty(itemId);
            string localError = null;
            if (target.Length == 0) localError = "NOT_FOUND";
            else if (target == FriendCode) localError = "SELF";
            else if (body.Length > TextMax) localError = "TEXT_TOO_LONG";
            else if (body.Trim().Length == 0 && !hasItem) localError = "BAD_MAIL";
            if (localError != null)
            {
                Callback(cb, new MailSendResult { Code = localError });
                return;
            }
            var clientId = NetOutbox.NewClientId();
            var payload = new JObject { ["to"] = target, ["text"] = body, ["clientId"] = clientId };
            if (hasItem)
            {
                payload["item"] = new JObject
                {
                    ["typ"] = itemTyp,
                    ["id"] = itemId,
                    ["menge"] = Math.Max(1, Math.Min(GiftQtyMax, menge)),
                };
            }
            SendMailPayload(payload, clientId, false, cb);
        }

        /// <summary>WS path of a letter (MAIL_SEND); offline / timeout → outbox. Shared by text and photo letters.</summary>
        private void SendMailPayload(JObject payload, string clientId, bool photoDropped, Action<MailSendResult> cb)
        {
            if (!Session.IsOnline)
            {
                Queue(payload, clientId, cb, photoDropped);
                return;
            }
            Session.Request("MAIL_SEND", payload, r =>
            {
                if (r.Ok)
                {
                    Callback(cb, new MailSendResult
                    {
                        Ok = true,
                        Id = NetJson.Str(r.Data["id"]),
                        ClientId = clientId,
                        Dupe = NetJson.Bool(r.Data["dupe"]),
                        SentToday = NetJson.Int(r.Data["sentToday"]),
                        DailyLimit = NetJson.Int(r.Data["dailyLimit"]),
                        PhotoDropped = photoDropped,
                    });
                }
                else if (r.Code == NetErrorCodes.Offline || r.Code == NetErrorCodes.Timeout)
                {
                    Queue(payload, clientId, cb, photoDropped); // clientId makes the retry idempotent server-side
                }
                else
                {
                    Callback(cb, new MailSendResult
                    {
                        Code = r.Code,
                        ClientId = clientId,
                        SentToday = NetJson.Int(r.Data["sentToday"]),
                        DailyLimit = NetJson.Int(r.Data["dailyLimit"]),
                        PhotoDropped = photoDropped,
                    });
                }
            });
        }

        private void Queue(JObject payload, string clientId, Action<MailSendResult> cb, bool photoDropped = false)
        {
            Outbox.Enqueue(payload);
            MarkDirty();
            Callback(cb, new MailSendResult { Queued = true, Code = "QUEUED", ClientId = clientId, PhotoDropped = photoDropped });
        }

        /// <summary>Pulls the mailbox (first page, unread first).</summary>
        public void RefreshMail(Action<NetReply> cb = null)
        {
            Session.Request("MAIL_LIST", new JObject { ["offset"] = 0, ["limit"] = MailPageSize }, r =>
            {
                if (r.Ok && r.Type == "MAIL_STATE")
                {
                    _mail.Clear();
                    if (r.Data["mails"] is JArray arr)
                    {
                        foreach (var t in arr)
                        {
                            var m = MailInfo.FromJson(t as JObject);
                            if (m != null) _mail.Add(m);
                        }
                    }
                    MailTotal = NetJson.Int(r.Data["total"], _mail.Count);
                    MailUnread = NetJson.Int(r.Data["unread"], MailUnread);
                    MarkDirty();
                }
                Callback(cb, r);
            });
        }

        /// <summary>Marks a letter as read (server-idempotent).</summary>
        public void AckMail(string id, Action<NetReply> cb = null)
        {
            Session.Request("MAIL_ACK", new JObject { ["id"] = id ?? "" }, r =>
            {
                if (r.Ok)
                {
                    var m = FindMail(id);
                    if (m != null) m.Read = true;
                    MailUnread = NetJson.Int(r.Data["unread"], MailUnread);
                    MarkDirty();
                }
                Callback(cb, r);
            });
        }

        /// <summary>Claims the gift of a letter — credit the item ONLY when the reply is Ok (Data.item). Second claim → ALREADY_CLAIMED.</summary>
        public void ClaimMail(string id, Action<NetReply> cb)
        {
            Session.Request("MAIL_CLAIM", new JObject { ["id"] = id ?? "" }, r =>
            {
                if (r.Ok || r.Code == "ALREADY_CLAIMED")
                {
                    var m = FindMail(id);
                    if (m != null) m.Claimed = true;
                    MarkDirty();
                }
                Callback(cb, r);
            });
        }

        public void DeleteMail(string id, Action<NetReply> cb = null)
        {
            Session.Request("MAIL_DELETE", new JObject { ["id"] = id ?? "" }, r =>
            {
                if (r.Ok)
                {
                    var m = FindMail(id);
                    if (m != null)
                    {
                        _mail.Remove(m);
                        if (MailTotal > 0) MailTotal--;
                    }
                    MailUnread = NetJson.Int(r.Data["unread"], MailUnread);
                    MarkDirty();
                }
                Callback(cb, r);
            });
        }

        public MailInfo FindMail(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var m in _mail) if (m.Id == id) return m;
            return null;
        }

        /// <summary>Tries to send queued letters now (also runs automatically after WELCOME and every 60 s).</summary>
        public void FlushOutbox()
        {
            _outboxRetryAt = _now() + OutboxRetryMs;
            Outbox.Flush(Session,
                (entry, reply) => MarkDirty(),
                (entry, code) =>
                {
                    MarkDirty();
                    var to = entry.To;
                    var item = entry.Item != null ? (JObject)entry.Item.DeepClone() : null;
                    Raise(() => MailBounced?.Invoke(to, item, code), "MailBounced");
                });
        }

        private void TickOutbox(long now)
        {
            if (!Session.IsOnline || Outbox.Count == 0 || Outbox.Flushing) return;
            if (_outboxRetryAt >= 0 && now < _outboxRetryAt) return;
            FlushOutbox();
        }

        // ------------------------------------------------------------------ GoobyPal

        /// <summary>Coin gift to a friend (online only). Reply Data: {ok, sentToday, dailyLimit, code?}.</summary>
        public void SendPal(string to, int amount, Action<NetReply> cb)
        {
            if (amount < 1)
            {
                Callback(cb, NetReply.Fail("BAD_AMOUNT"));
                return;
            }
            Session.Request("PAL_SEND", new JObject { ["to"] = Upper(to), ["amount"] = amount }, r => Callback(cb, r));
        }

        private void HandlePalDelivery(JObject d)
        {
            if (d == null) return;
            var id = NetJson.Str(d["id"]);
            if (id.Length == 0) return;
            if (Identity.IsPalCredited(id))
            {
                AckPal(id); // re-delivery of an already credited gift → ACK only
                return;
            }
            var handler = PalReceived;
            if (handler == null) return; // nobody can credit it — leave it pending on the server
            var from = NetJson.Str(d["from"]);
            var amount = NetJson.Int(d["amount"]);
            Raise(() => handler(id, from, amount), "PalReceived");
            Identity.MarkPalCredited(id);
            Identity.Save();
            AckPal(id);
        }

        private void AckPal(string id) => Session.Send("PAL_ACK", new JObject { ["id"] = id });

        // ------------------------------------------------------------------ server events

        private void HandleServerEvent(JObject d)
        {
            if (d == null) return;
            var id = NetJson.Str(d["id"]);
            if (id.Length == 0) return;
            if (_seenEvents.Contains(id))
            {
                Session.Send("EVENT_ACK", new JObject { ["id"] = id });
                return;
            }
            var handler = ServerEvent;
            if (handler == null) return; // not handled → stays pending for the next session
            Raise(() => handler(d), "ServerEvent");
            if (_seenEvents.Count >= 500) _seenEvents.Clear(); // server stops re-sending after EVENT_ACK anyway
            _seenEvents.Add(id);
            Session.Send("EVENT_ACK", new JObject { ["id"] = id });
        }

        // ------------------------------------------------------------------ visits

        /// <summary>Guest: asks a friend (must be online) for a visit. Ok = request delivered; the start comes as VisitReady.</summary>
        public void RequestVisit(string hostCode, Action<NetReply> cb)
        {
            Session.Request("VISIT_REQUEST", new JObject { ["target"] = Upper(hostCode) }, r => Callback(cb, r));
        }

        /// <summary>
        /// Host: uploads <paramref name="houseSnapshot"/> (PUT /api/house; null = keep the last upload),
        /// then VISIT_ACCEPT. On VISIT_READY the room is joined, VisitReady fires, then <paramref name="cb"/>.
        /// </summary>
        public void AcceptVisit(string guestCode, JObject houseSnapshot, Action<NetReply> cb)
        {
            var guest = Upper(guestCode);
            if (!Session.IsOnline)
            {
                Callback(cb, NetReply.Fail(NetErrorCodes.Offline));
                return;
            }
            Action accept = () => Session.Request("VISIT_ACCEPT", new JObject { ["guest"] = guest }, r =>
            {
                if (r.Ok && r.Type == "VISIT_READY") EnterVisit(r.Data, () => Callback(cb, r));
                else Callback(cb, r);
            });
            if (houseSnapshot == null)
            {
                accept();
                return;
            }
            UploadHouse(houseSnapshot, (ok, rev, code) =>
            {
                if (!ok) Callback(cb, NetReply.Fail(code));
                else accept();
            });
        }

        public void DeclineVisit(string guestCode, Action<NetReply> cb)
        {
            Session.Request("VISIT_DECLINE", new JObject { ["guest"] = Upper(guestCode) }, r => Callback(cb, r));
        }

        /// <summary>Ends the current visit (either side). VisitEnded fires for both players.</summary>
        public void EndVisit(Action<NetReply> cb)
        {
            if (!VisitActive)
            {
                Callback(cb, NetReply.Fail("NOT_IN_ROOM"));
                return;
            }
            var room = VisitRoom;
            if (!Session.IsOnline)
            {
                EndVisitLocally(room, FriendCode);
                Callback(cb, NetReply.Fail(NetErrorCodes.Offline));
                return;
            }
            Session.Request("VISIT_END", new JObject { ["room"] = room }, r =>
            {
                // Server forgot the visit (e.g. host dropped) → end locally, the UI must leave anyway.
                if (!r.Ok && VisitRoom == room) EndVisitLocally(room, FriendCode);
                Callback(cb, r);
            });
        }

        /// <summary>Uploads the own house snapshot (PUT /api/house, ≤ 256 KB). cb(ok, rev, code).</summary>
        public void UploadHouse(JObject snapshot, Action<bool, int, string> cb)
        {
            if (!Endpoint.Valid)
            {
                Raise(() => cb?.Invoke(false, 0, NetErrorCodes.InvalidEndpoint), "UploadHouse cb");
                return;
            }
            var json = (snapshot ?? new JObject()).ToString(Formatting.None);
            _http.Send("PUT", Endpoint.HttpBase + "/api/house", Identity.Bearer, json, (status, body) =>
            {
                var o = NetJson.ParseObject(body);
                if (status == 200 && o != null && NetJson.Bool(o["ok"]))
                    Raise(() => cb?.Invoke(true, NetJson.Int(o["rev"]), ""), "UploadHouse cb");
                else
                    Raise(() => cb?.Invoke(false, 0, HttpCode(status, o)), "UploadHouse cb");
            });
        }

        /// <summary>GET /api/house/:code (friends or self only). cb(ok, layout, code).</summary>
        public void FetchHouse(string hostCode, Action<bool, JObject, string> cb)
        {
            if (!Endpoint.Valid)
            {
                Raise(() => cb?.Invoke(false, null, NetErrorCodes.InvalidEndpoint), "FetchHouse cb");
                return;
            }
            var url = Endpoint.HttpBase + "/api/house/" + Uri.EscapeDataString(Upper(hostCode));
            _http.Send("GET", url, Identity.Bearer, null, (status, body) =>
            {
                var o = NetJson.ParseObject(body);
                if (status == 200 && o != null && NetJson.Bool(o["ok"]) && o["layout"] is JObject layout)
                    Raise(() => cb?.Invoke(true, layout, ""), "FetchHouse cb");
                else
                    Raise(() => cb?.Invoke(false, null, HttpCode(status, o)), "FetchHouse cb");
            });
        }

        /// <summary>Own Gooby position into the visit relay; throttled to 5 Hz (force bypasses, e.g. room change). False = not sent.</summary>
        /// <param name="energy">Our Gooby's energy (≥ 0) for the couch rule; −1 leaves the field out.</param>
        /// <param name="hour">The local hour — only the host sends it (it is the host's evening that counts); −1 leaves it out.</param>
        public bool SendVisitPos(float x, float z, string anim, string roomId, bool force = false, double energy = -1.0, int hour = -1)
        {
            if (!VisitActive || !VisitJoined || !Session.IsOnline) return false;
            var now = _now();
            if (!force && _lastPosMs >= 0 && now - _lastPosMs < PosIntervalMs) return false;
            _lastPosMs = now;
            var body = new JObject
            {
                ["pos"] = new JArray(Math.Round((double)x, 3), Math.Round((double)z, 3)),
                ["anim"] = string.IsNullOrEmpty(anim) ? "idle" : anim,
                ["roomId"] = roomId ?? "",
            };
            if (energy >= 0.0) body["energy"] = Math.Round(energy, 1);
            if (hour >= 0) body["hour"] = hour;
            return SendRoomMessage("POS", body);
        }

        /// <summary>The peer's Gooby's energy from its POS (−1 while unknown) — the couch rule.</summary>
        public double PeerEnergy { get; private set; } = -1.0;

        /// <summary>The host's local hour from its POS (−1 while unknown or when the peer is the guest).</summary>
        public int PeerHour { get; private set; } = -1;

        /// <summary>(on, boden): the peer's Gooby fell asleep on our couch (or the floor) or woke up.</summary>
        public event Action<bool, bool> PeerNap;

        /// <summary>Guest: our Gooby naps on the host's couch (couch_logic.gd NAP relay).</summary>
        public bool SendVisitNap(bool on, Gooby.Core.Home.Cell cell, bool boden) => SendRoomMessage("NAP", CouchLogic.NapPayload(on, cell, boden));

        public bool SendVisitEmote(string id) => SendRoomMessage("EMOTE", new JObject { ["id"] = id ?? "" });

        /// <summary>Generic visit-room relay (kind ^[A-Z][A-Z0-9_]{0,23}$, body ≤ 8 KB), e.g. BUILD_START/BUILD_DELTA.</summary>
        public bool SendRoomMessage(string kind, JObject body)
        {
            if (!VisitActive || !VisitJoined || !Session.IsOnline) return false;
            return Session.Send("ROOM_MSG", new JObject { ["room"] = VisitRoom, ["kind"] = kind, ["body"] = body ?? new JObject() }) >= 0;
        }

        private void EnterVisit(JObject d, Action after)
        {
            var room = NetJson.Str(d["room"]);
            if (room.Length == 0)
            {
                after?.Invoke();
                return;
            }
            VisitRoom = room;
            VisitHost = NetJson.Str(d["host"]);
            VisitGuest = NetJson.Str(d["guest"]);
            VisitRev = NetJson.Int(d["rev"]);
            VisitRole = VisitHost == FriendCode ? VisitRole.Host : VisitRole.Guest;
            PeerCode = VisitRole == VisitRole.Host ? VisitGuest : VisitHost;
            var peer = FindFriend(PeerCode);
            PeerName = peer?.Name ?? "";
            PeerGoobyName = peer?.GoobyName ?? "";
            PeerInRoom = false;
            PeerRoomId = "";
            PeerEnergy = -1.0;
            PeerHour = -1;
            VisitJoined = false;
            _lastPosMs = -1;
            _hostGoneAtMs = -1;
            MarkDirty();
            var host = VisitHost;
            var guest = VisitGuest;
            var rev = VisitRev;
            JoinVisitRoom(room, ok =>
            {
                if (VisitRoom == room) Raise(() => VisitReady?.Invoke(room, host, guest, rev), "VisitReady");
                after?.Invoke();
            });
        }

        private void JoinVisitRoom(string room, Action<bool> done)
        {
            Session.Request("ROOM_JOIN", new JObject { ["room"] = room }, r =>
            {
                if (VisitRoom != room)
                {
                    done?.Invoke(false);
                    return;
                }
                if (r.Ok)
                {
                    VisitJoined = true;
                    if (r.Data["members"] is JArray members)
                    {
                        foreach (var m in members)
                        {
                            if (NetJson.Str(m) == PeerCode && PeerCode.Length > 0) MarkPeerJoined(PeerCode, PeerName, PeerGoobyName);
                        }
                    }
                }
                else
                {
                    NetLog.Warn("[social] ROOM_JOIN " + room + " failed: " + r.Code);
                }
                MarkDirty();
                done?.Invoke(r.Ok);
            });
        }

        private void MarkPeerJoined(string code, string name, string gooby)
        {
            PeerCode = code;
            if (!string.IsNullOrEmpty(name)) PeerName = name;
            if (!string.IsNullOrEmpty(gooby)) PeerGoobyName = gooby;
            MarkDirty();
            _hostGoneAtMs = -1;
            if (PeerInRoom) return;
            PeerInRoom = true;
            var n = PeerName;
            var g = PeerGoobyName;
            Raise(() => PeerJoined?.Invoke(code, n, g), "PeerJoined");
        }

        private void EndVisitLocally(string room, string by)
        {
            ResetVisit();
            Raise(() => VisitEnded?.Invoke(room, by), "VisitEnded");
        }

        private void ResetVisit()
        {
            VisitRoom = "";
            VisitRole = VisitRole.None;
            VisitHost = "";
            VisitGuest = "";
            VisitRev = 0;
            PeerCode = "";
            PeerName = "";
            PeerGoobyName = "";
            PeerInRoom = false;
            PeerRoomId = "";
            PeerEnergy = -1.0;
            PeerHour = -1;
            VisitJoined = false;
            _lastPosMs = -1;
            _hostGoneAtMs = -1;
            MarkDirty();
        }

        // After a reconnect the server has dropped us from all rooms: a host's visit is over
        // (visits.js ends it on host disconnect); a guest tries to rejoin the still-allowed room.
        private void ResumeVisitAfterReconnect()
        {
            if (!VisitActive) return;
            var room = VisitRoom;
            if (VisitRole == VisitRole.Host)
            {
                EndVisitLocally(room, FriendCode);
                return;
            }
            VisitJoined = false;
            PeerInRoom = false;
            var host = VisitHost;
            JoinVisitRoom(room, ok =>
            {
                if (!ok && VisitRoom == room) EndVisitLocally(room, host);
            });
        }

        // ------------------------------------------------------------------ session hooks

        private void OnStatusChanged(NetStatus status)
        {
            if (status != NetStatus.Online) _presenceSent = null;
            MarkDirty();
            Raise(() => StatusChanged?.Invoke(status), "StatusChanged");
        }

        private void OnWelcome(JObject d)
        {
            InstantUnseen = NetJson.Int(d["instantUnseen"], InstantUnseen);
            MailUnread = NetJson.Int(d["mailUnread"], MailUnread);
            if (d["friendRequests"] is JArray fr)
            {
                _requests.Clear();
                foreach (var t in fr)
                {
                    var req = FriendRequestInfo.FromJson(t as JObject);
                    if (req != null) _requests.Add(req);
                }
            }
            _presenceSent = null;
            _coinsSentAt = -1;
            var now = _now();
            TickPresence(now);
            TickCoins(now);
            if (d["palPending"] is JArray pal)
                foreach (var t in pal) HandlePalDelivery(t as JObject);
            if (d["pendingEvents"] is JArray evts)
                foreach (var t in evts) HandleServerEvent(t as JObject);
            ResumeVisitAfterReconnect();
            MarkDirty();
            RefreshFriends();
            RefreshMail();
            FlushOutbox();
        }

        private void OnPush(string type, JObject d)
        {
            switch (type)
            {
                case "FRIEND_REQUEST_INCOMING":
                {
                    var req = FriendRequestInfo.FromJson(d);
                    if (req == null) return;
                    RemoveRequest(req.FromCode);
                    _requests.Add(req);
                    MarkDirty();
                    Raise(() => FriendRequestIncoming?.Invoke(req), "FriendRequestIncoming");
                    break;
                }
                case "FRIEND_ADDED":
                {
                    var f = FriendInfo.FromJson(d);
                    if (f == null) return;
                    var idx = _friends.FindIndex(x => x.Code == f.Code);
                    if (idx >= 0) _friends[idx] = f;
                    else _friends.Add(f);
                    RemoveRequest(f.Code);
                    MarkDirty();
                    Raise(() => FriendAdded?.Invoke(f), "FriendAdded");
                    break;
                }
                case "FRIEND_REMOVED":
                    RemoveFriendRow(NetJson.Str(d["friendCode"]), true);
                    break;
                case "FRIEND_PRESENCE":
                {
                    var f = FindFriend(NetJson.Str(d["friendCode"]));
                    if (f == null) return;
                    var wasOnline = f.Online;
                    f.Online = NetJson.Bool(d["online"]);
                    f.ApplyActivity(f.Online ? d["activity"] as JObject : null);
                    MarkDirty();
                    Raise(() => FriendPresenceChanged?.Invoke(f), "FriendPresenceChanged");
                    if (wasOnline != f.Online) Raise(() => FriendOnlineChanged?.Invoke(f), "FriendOnlineChanged");
                    break;
                }
                case "MAIL_NEW":
                {
                    var m = MailInfo.FromJson(d["mail"] as JObject);
                    MailUnread = NetJson.Int(d["unread"], MailUnread + 1);
                    MarkDirty();
                    if (m == null) return;
                    var old = FindMail(m.Id);
                    if (old != null) _mail.Remove(old);
                    else MailTotal++;
                    _mail.Insert(0, m);
                    Raise(() => MailArrived?.Invoke(m), "MailArrived");
                    break;
                }
                case "INSTANT_NEW":
                    OnInstantNew(d);
                    break;
                case "INSTANT_LIKE":
                    OnInstantLikedPush(d);
                    break;
                case "PAL_RECEIVED":
                    HandlePalDelivery(d);
                    break;
                case "SERVER_EVENT":
                    HandleServerEvent(d);
                    break;
                case "GOING_DOWN":
                {
                    var reason = NetJson.Str(d["reason"]);
                    // the account moved to another device: this device's secret is void — it starts over as a fresh
                    // guest on the next connect (the device that redeemed the move just adopted the identity: not it)
                    if (reason == NetErrorCodes.Moved && !_moveReconnect) RenewAfterMove();
                    Raise(() => GoingDown?.Invoke(reason), "GoingDown");
                    break;
                }
                case "VISIT_INCOMING":
                {
                    var from = NetJson.Str(d["from"]);
                    var name = NetJson.Str(d["name"]);
                    var gooby = NetJson.Str(d["goobyName"], "Gooby");
                    Raise(() => VisitIncoming?.Invoke(from, name, gooby), "VisitIncoming");
                    break;
                }
                case "VISIT_READY":
                    EnterVisit(d, null);
                    break;
                case "VISIT_DENIED":
                {
                    var from = NetJson.Str(d["from"]);
                    Raise(() => VisitDenied?.Invoke(from), "VisitDenied");
                    break;
                }
                case "VISIT_ENDED":
                {
                    var room = NetJson.Str(d["room"]);
                    if (!VisitActive || room != VisitRoom) return;
                    EndVisitLocally(room, NetJson.Str(d["by"]));
                    break;
                }
                case "ROOM_PEER_JOINED":
                {
                    if (!VisitActive || NetJson.Str(d["room"]) != VisitRoom) return;
                    var code = NetJson.Str(d["friendCode"]);
                    if (code.Length == 0 || code == FriendCode) return;
                    MarkPeerJoined(code, NetJson.Str(d["name"]), NetJson.Str(d["goobyName"]));
                    break;
                }
                case "ROOM_PEER_LEFT":
                {
                    if (!VisitActive || NetJson.Str(d["room"]) != VisitRoom) return;
                    var code = NetJson.Str(d["friendCode"]);
                    PeerInRoom = false;
                    MarkDirty();
                    // Host gone: either VISIT_END (VISIT_ENDED follows right away) or a host disconnect,
                    // which ends the visit server-side without VISIT_ENDED → end locally after a grace period.
                    if (VisitRole == VisitRole.Guest && code == VisitHost) _hostGoneAtMs = _now();
                    Raise(() => PeerLeft?.Invoke(code), "PeerLeft");
                    break;
                }
                case "ROOM_MSG":
                    OnRoomMessage(d);
                    break;
            }
        }

        private void OnRoomMessage(JObject d)
        {
            if (!VisitActive || NetJson.Str(d["room"]) != VisitRoom) return;
            var kind = NetJson.Str(d["kind"]);
            var body = d["body"] as JObject ?? new JObject();
            if (kind == "POS")
            {
                if (body["pos"] is JArray pos && pos.Count >= 2 && IsNumber(pos[0]) && IsNumber(pos[1]))
                {
                    var x = (float)NetJson.Num(pos[0]);
                    var z = (float)NetJson.Num(pos[1]);
                    var anim = NetJson.Str(body["anim"], "idle");
                    var roomId = NetJson.Str(body["roomId"]);
                    PeerRoomId = roomId;
                    PeerEnergy = IsNumber(body["energy"]) ? NetJson.Num(body["energy"]) : -1.0;
                    PeerHour = IsNumber(body["hour"]) ? (int)NetJson.Num(body["hour"]) : -1;
                    Raise(() => PeerPos?.Invoke(x, z, anim, roomId), "PeerPos");
                }
            }
            else if (kind == "NAP")
            {
                var nap = CouchLogic.ParseNap(body);
                if (nap.ok) Raise(() => PeerNap?.Invoke(nap.on, nap.boden), "PeerNap");
            }
            else if (kind == "EMOTE")
            {
                var id = NetJson.Str(body["id"]);
                Raise(() => PeerEmote?.Invoke(id), "PeerEmote");
            }
            Raise(() => RoomMessage?.Invoke(kind, body), "RoomMessage");
        }

        // ------------------------------------------------------------------ helpers

        private void ApplyFriendsState(JObject d)
        {
            _friends.Clear();
            if (d["friends"] is JArray rows)
            {
                foreach (var t in rows)
                {
                    var f = FriendInfo.FromJson(t as JObject);
                    if (f != null) _friends.Add(f);
                }
            }
            if (d["requests"] is JArray reqs)
            {
                _requests.Clear();
                foreach (var t in reqs)
                {
                    var r = FriendRequestInfo.FromJson(t as JObject);
                    if (r != null) _requests.Add(r);
                }
            }
            MarkDirty();
        }

        private void RemoveRequest(string code)
        {
            if (_requests.RemoveAll(r => string.Equals(r.FromCode, code, StringComparison.OrdinalIgnoreCase)) > 0) MarkDirty();
        }

        private void RemoveFriendRow(string code, bool raise)
        {
            if (string.IsNullOrEmpty(code)) return;
            var removed = _friends.RemoveAll(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed) return;
            MarkDirty();
            if (raise) Raise(() => FriendRemoved?.Invoke(code), "FriendRemoved");
        }

        private void ClearServerState()
        {
            _friends.Clear();
            _requests.Clear();
            _mail.Clear();
            _seenEvents.Clear();
            MailUnread = 0;
            MailTotal = 0;
            InstantUnseen = 0;
            ClearInstantState();
            ClearArcadeState();
            ResetVisit();
            Identity.FriendCode = "";
            Identity.Save();
            _presenceSent = null;
            _coinsSentAt = -1;
        }

        private static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);

        private static string Upper(string code) => (code ?? "").Trim().ToUpperInvariant();

        private static string HttpCode(int status, JObject body)
        {
            if (status == 0) return NetErrorCodes.NoConnection;
            var code = NetJson.Str(body?["code"]);
            return code.Length > 0 ? code : "HTTP_" + status;
        }

        private void MarkDirty() => _dirty = true;

        private static void Raise(Action a, string what) => NetLog.Guard(a, what);

        private static void Callback<T>(Action<T> cb, T value)
        {
            if (cb == null) return;
            NetLog.Guard(() => cb(value), "callback");
        }
    }
}
