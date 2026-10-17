using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>Connection state of a <see cref="NetSession"/>.</summary>
    public enum NetStatus
    {
        Offline,
        Connecting,
        Online,
    }

    /// <summary>
    /// Result of <see cref="NetSession.Request"/>. <see cref="Ok"/> is false for ERROR replies, for
    /// result payloads with <c>ok:false</c> (MAIL_RESULT/PAL_RESULT), and for local failures
    /// (OFFLINE, TIMEOUT). <see cref="Data"/> is never null.
    /// </summary>
    public sealed class NetReply
    {
        public bool Ok { get; }
        /// <summary>Error code ("" when <see cref="Ok"/>), e.g. OFFLINE, TIMEOUT, NOT_FRIENDS.</summary>
        public string Code { get; }
        /// <summary>Server reply type (OK, FRIENDS_STATE, MAIL_RESULT, ERROR…); "" for local failures.</summary>
        public string Type { get; }
        public JObject Data { get; }

        public NetReply(bool ok, string code, string type, JObject data)
        {
            Ok = ok;
            Code = code ?? "";
            Type = type ?? "";
            Data = data ?? new JObject();
        }

        public static NetReply Fail(string code, JObject data = null) => new NetReply(false, code, "", data);

        public static NetReply Success(string type, JObject data = null) => new NetReply(true, "", type, data);

        public static NetReply FromEnvelope(NetEnvelope env)
        {
            var d = env.Data ?? new JObject();
            var okField = d["ok"];
            var ok = !env.IsError && !(okField != null && okField.Type == JTokenType.Boolean && !okField.Value<bool>());
            var code = NetJson.Str(d["code"]);
            if (!ok && code.Length == 0) code = env.IsError ? "ERROR" : "FAILED";
            return new NetReply(ok, ok ? "" : code, env.Type, d);
        }

        public override string ToString() => Ok ? "OK(" + Type + ")" : "FAIL(" + Code + ")";
    }

    /// <summary>Error codes surfaced by the net layer (server codes pass through unchanged).</summary>
    public static class NetErrorCodes
    {
        public const string Offline = "OFFLINE";
        public const string Timeout = "TIMEOUT";
        public const string Unreachable = "UNREACHABLE";
        public const string ConnectionLost = "CONNECTION_LOST";
        public const string InvalidEndpoint = "INVALID_ENDPOINT";
        public const string HeartbeatTimeout = "HEARTBEAT_TIMEOUT";
        public const string Shutdown = "SHUTDOWN";
        public const string HelloRequired = "HELLO_REQUIRED";
        public const string RateLimit = "RATE_LIMIT";
        public const string AuthFail = "AUTH_FAIL";
        public const string Banned = "BANNED";
        public const string BadMessage = "BAD_MESSAGE";
        public const string Replaced = "REPLACED";
        /// <summary>The account moved to another device (MOVE_REDEEM there): this device's secret is void.</summary>
        public const string Moved = "MOVED";
        public const string SecretRequired = "SECRET_REQUIRED";
        public const string SecretWrong = "SECRET_WRONG";
        public const string ProtoVersion = "PROTO_VERSION";
        public const string NoConnection = "NO_CONNECTION";

        /// <summary>Codes after which auto-reconnect stops (retrying cannot succeed without user action).</summary>
        public static bool IsFatal(string code)
        {
            switch (code)
            {
                case AuthFail:
                case Banned:
                case SecretRequired:
                case SecretWrong:
                case Replaced:
                case Moved:
                case ProtoVersion:
                case BadMessage:
                case InvalidEndpoint:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Maps a WebSocket close (code, reason) to a net error code.</summary>
        public static string FromClose(int code, string reason, bool wasOpened)
        {
            switch (code)
            {
                case 4000: return HeartbeatTimeout;
                case 4001: return HelloRequired;
                case 4002: return ProtoVersion;
                case 4003: return RateLimit;
                case 4004: return AuthFail;
                case 4005: return Banned;
                case 4006: return BadMessage;
                case 4007: return Replaced;
                // move.js closes both ends of a move with 4008 "MOVED" (auth.js uses 4008 for the secret checks)
                case 4008: return reason == Moved ? Moved : reason == SecretWrong ? SecretWrong : SecretRequired;
                case NetTransportEvent.CodeConnectTimeout: return Timeout;
                case NetTransportEvent.CodeConnectFailed: return Unreachable;
            }
            if (!wasOpened) return Unreachable;
            if (code == 1001 || reason == Shutdown) return Shutdown;
            return ConnectionLost;
        }
    }

    /// <summary>
    /// Optional log sink for the net layer (hook Debug.LogWarning/LogError from Unity).
    /// Exceptions thrown by event handlers/callbacks are caught and reported here.
    /// </summary>
    public static class NetLog
    {
        public static Action<string> OnWarning { get; set; }
        public static Action<string> OnError { get; set; }

        public static void Warn(string message)
        {
            try { OnWarning?.Invoke(message); } catch (Exception) { /* never throw from logging */ }
        }

        public static void Error(string message)
        {
            try { OnError?.Invoke(message); } catch (Exception) { /* never throw from logging */ }
        }

        /// <summary>Runs a user callback; exceptions are logged, not propagated.</summary>
        public static void Guard(Action action, string what)
        {
            if (action == null) return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Error("[net] " + what + " threw: " + e);
            }
        }
    }

    /// <summary>
    /// GOOBY lobby protocol state machine (port of net_client.gd), transport-agnostic and
    /// main-thread only: HELLO/WELCOME handshake, request/reply correlation via <c>re</c>,
    /// request timeouts, PING heartbeat, reconnect backoff (1 s doubling to 30 s + jitter, reset on
    /// WELCOME; fatal codes stop auto-reconnect). Call <see cref="Pump"/> every frame — all events
    /// and callbacks are raised from inside Pump (or synchronously for immediate OFFLINE replies).
    /// </summary>
    public sealed class NetSession
    {
        public const int DefaultRequestTimeoutMs = 10000;
        public const int ReconnectBaseMs = 1000;
        public const int ReconnectMaxMs = 30000;
        /// <summary>Server HELLO bucket refills one token per 12 s — never retry faster after RATE_LIMIT.</summary>
        public const int RateLimitMinDelayMs = 15000;
        public const string DefaultPlayerName = "Gooby-Fan";
        public const string DefaultGoobyName = "Gooby";

        private sealed class PendingRequest
        {
            public Action<NetReply> Callback;
            public long Deadline;
            public string Type;
        }

        private readonly Func<INetTransport> _factory;
        private readonly Func<long> _now;
        private readonly System.Random _random;
        private readonly Dictionary<long, PendingRequest> _pending = new Dictionary<long, PendingRequest>();

        private INetTransport _transport;
        private bool _opened;
        private long _seq;
        private long _helloSeq = -1;
        private string _helloErrorCode;
        private string _goingDownReason;
        private long _connectStartedMs;
        private long _lastRxMs;
        private long _lastPingMs;
        private long _lastPumpMs = -1;

        public NetSession(NetIdentity identity, Func<INetTransport> transportFactory, Func<long> nowMs, System.Random random = null)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _factory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _now = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            _random = random ?? new System.Random();
        }

        public NetIdentity Identity { get; }
        public NetStatus Status { get; private set; } = NetStatus.Offline;
        public bool IsOnline => Status == NetStatus.Online;
        /// <summary>Reason of the last failure ("" after a successful WELCOME / explicit Connect()).</summary>
        public string LastErrorCode { get; private set; } = "";
        /// <summary>Last transport error text (diagnostics only).</summary>
        public string LastTransportError { get; private set; } = "";
        public NetEndpoint Endpoint { get; private set; } = NetEndpoint.Invalid;
        public string PlayerName { get; private set; } = DefaultPlayerName;
        public string GoobyName { get; private set; } = DefaultGoobyName;
        public string JoinSecret { get; private set; } = "";
        public string AppVersion { get; private set; } = "unity";
        public string FriendCode => Identity.FriendCode ?? "";
        public JObject LastWelcome { get; private set; }
        public int HeartbeatSec { get; private set; } = 20;
        /// <summary>True while the session wants to be connected (Connect() called, no fatal error, no Disconnect()).</summary>
        public bool WantsConnection { get; private set; }
        public int ReconnectAttempts { get; private set; }
        /// <summary>When the next automatic reconnect fires (nowMs clock), -1 = none scheduled.</summary>
        public long NextReconnectAtMs { get; private set; } = -1;
        public long LastReconnectDelayMs { get; private set; }
        /// <summary>Connect → WELCOME deadline (safety net above the transport's own connect timeout).</summary>
        public int HandshakeTimeoutMs { get; set; } = 15000;
        public int PendingRequestCount => _pending.Count;

        public event Action<NetStatus> StatusChanged;
        /// <summary>WELCOME payload (after Status became Online).</summary>
        public event Action<JObject> Welcome;
        /// <summary>Server push (message without <c>re</c>): type + payload.</summary>
        public event Action<string, JObject> Push;

        /// <summary>Stores connection parameters; they apply to the next connect (use <see cref="Reconnect"/> to apply now).</summary>
        public void Configure(NetEndpoint endpoint, string playerName, string goobyName, string joinSecret, string appVersion)
        {
            Endpoint = endpoint ?? NetEndpoint.Invalid;
            PlayerName = Clean(playerName, DefaultPlayerName);
            GoobyName = Clean(goobyName, DefaultGoobyName);
            JoinSecret = joinSecret ?? "";
            AppVersion = string.IsNullOrWhiteSpace(appVersion) ? "unity" : appVersion.Trim();
        }

        private static string Clean(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var v = value.Trim();
            return v.Length > 24 ? v.Substring(0, 24) : v;
        }

        /// <summary>Starts connecting now (resets backoff). No-op while already connecting/online.</summary>
        public void Connect()
        {
            WantsConnection = true;
            if (_transport != null) return;
            LastErrorCode = "";
            ReconnectAttempts = 0;
            NextReconnectAtMs = -1;
            StartConnect();
        }

        /// <summary>Closes the connection and stops auto-reconnect. Pending requests fail with OFFLINE.</summary>
        public void Disconnect()
        {
            WantsConnection = false;
            NextReconnectAtMs = -1;
            var t = _transport;
            ResetLink();
            if (t != null)
            {
                try { t.Close(); } catch (Exception) { /* transports must not throw */ }
            }
            SetStatus(NetStatus.Offline);
            FailAllPending(NetErrorCodes.Offline);
        }

        /// <summary>Disconnect + Connect (applies a new Configure()).</summary>
        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        /// <summary>Fire-and-forget message; returns the seq, or -1 when not online.</summary>
        public long Send(string t, JObject d)
        {
            if (Status != NetStatus.Online || _transport == null) return -1;
            return SendRaw(t, d);
        }

        /// <summary>
        /// Request/reply via <c>re</c>. Not online → <paramref name="onReply"/> is invoked immediately with
        /// Code=OFFLINE. No reply within <paramref name="timeoutMs"/> → TIMEOUT; link loss → OFFLINE.
        /// </summary>
        public void Request(string t, JObject d, Action<NetReply> onReply, int timeoutMs = DefaultRequestTimeoutMs)
        {
            if (Status != NetStatus.Online || _transport == null)
            {
                Deliver(onReply, NetReply.Fail(NetErrorCodes.Offline), t);
                return;
            }
            var seq = SendRaw(t, d);
            _pending[seq] = new PendingRequest
            {
                Callback = onReply,
                Deadline = _now() + (timeoutMs > 0 ? timeoutMs : DefaultRequestTimeoutMs),
                Type = t,
            };
        }

        /// <summary>Main-thread tick: transport events, handshake/request timeouts, heartbeat, reconnect.</summary>
        public void Pump()
        {
            var now = _now();
            var resumed = _lastPumpMs >= 0 && now - _lastPumpMs > HeartbeatSec * 1000L;
            _lastPumpMs = now;

            var t = _transport;
            if (t != null)
            {
                // Bounded drain: a message handler may disconnect/replace the transport.
                var budget = 10000;
                while (_transport == t && budget-- > 0 && t.TryDequeue(out var ev))
                {
                    switch (ev.Kind)
                    {
                        case NetTransportEventKind.Opened:
                            OnOpened();
                            break;
                        case NetTransportEventKind.Message:
                            OnMessage(ev.Text);
                            break;
                        case NetTransportEventKind.Error:
                            LastTransportError = ev.Text;
                            NetLog.Warn("[net] transport: " + ev.Text);
                            break;
                        case NetTransportEventKind.Closed:
                            OnClosed(ev.Code, ev.Reason);
                            break;
                    }
                }
            }

            now = _now();
            if (_transport != null && Status == NetStatus.Connecting && now - _connectStartedMs > HandshakeTimeoutMs)
            {
                HandleLinkDown(string.IsNullOrEmpty(_helloErrorCode) ? NetErrorCodes.Timeout : _helloErrorCode);
            }

            if (_transport != null && Status == NetStatus.Online)
            {
                var hbMs = Math.Max(1, HeartbeatSec) * 1000L;
                if (resumed)
                {
                    // App was paused (no Pump) — give the link a fresh grace period and probe it now.
                    _lastRxMs = now;
                    _lastPingMs = now - hbMs;
                }
                if (now - _lastPingMs >= hbMs)
                {
                    _lastPingMs = now;
                    SendRaw("PING", new JObject());
                }
                if (now - _lastRxMs > hbMs * 3)
                {
                    HandleLinkDown(NetErrorCodes.Timeout);
                }
            }

            if (_pending.Count > 0) ExpireRequests(now);

            if (_transport == null && WantsConnection && NextReconnectAtMs >= 0 && now >= NextReconnectAtMs)
            {
                NextReconnectAtMs = -1;
                StartConnect();
            }
        }

        // ------------------------------------------------------------------ internals

        private void StartConnect()
        {
            if (!Endpoint.Valid)
            {
                LastErrorCode = NetErrorCodes.InvalidEndpoint;
                WantsConnection = false;
                NextReconnectAtMs = -1;
                SetStatus(NetStatus.Offline);
                return;
            }
            INetTransport t = null;
            try
            {
                t = _factory();
            }
            catch (Exception e)
            {
                NetLog.Error("[net] transport factory threw: " + e);
            }
            if (t == null)
            {
                LastErrorCode = NetErrorCodes.Unreachable;
                SetStatus(NetStatus.Offline);
                ScheduleReconnect(NetErrorCodes.Unreachable);
                return;
            }
            ResetLink();
            _transport = t;
            _connectStartedMs = _now();
            if (t is INetTransportOpenFrame early)
            {
                // HELLO leaves with the handshake itself (see INetTransportOpenFrame) — OnOpened then only marks the link
                _seq++;
                _helloSeq = _seq;
                early.SendOnOpen = NetEnvelope.Build("HELLO", _helloSeq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), HelloPayload());
                _helloPreSent = true;
            }
            try
            {
                t.Connect(Endpoint.WsUrl);
            }
            catch (Exception e)
            {
                NetLog.Error("[net] transport.Connect threw: " + e);
            }
            SetStatus(NetStatus.Connecting);
        }

        private bool _helloPreSent;

        private void ResetLink()
        {
            _transport = null;
            _opened = false;
            _helloPreSent = false;
            _helloSeq = -1;
            _helloErrorCode = null;
            _goingDownReason = null;
        }

        private long SendRaw(string t, JObject d)
        {
            _seq++;
            var text = NetEnvelope.Build(t, _seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), d ?? new JObject());
            try
            {
                _transport?.Send(text);
            }
            catch (Exception e)
            {
                NetLog.Error("[net] transport.Send threw: " + e);
            }
            return _seq;
        }

        private void OnOpened()
        {
            _opened = true;
            _lastRxMs = _now();
            if (_helloPreSent) return;
            _helloSeq = SendRaw("HELLO", HelloPayload());
        }

        private JObject HelloPayload()
        {
            var d = new JObject
            {
                ["deviceId"] = Identity.DeviceId,
                ["deviceSecret"] = Identity.DeviceSecret,
                ["name"] = PlayerName,
                ["goobyName"] = GoobyName,
                ["appVersion"] = AppVersion,
            };
            if (!string.IsNullOrEmpty(Identity.FriendCode)) d["friendCode"] = Identity.FriendCode;
            if (!string.IsNullOrEmpty(JoinSecret)) d["secret"] = JoinSecret;
            return d;
        }

        private void OnMessage(string text)
        {
            _lastRxMs = _now();
            var env = NetEnvelope.Parse(text);
            if (env == null) return;
            if (env.Re.HasValue)
            {
                var re = env.Re.Value;
                if (_helloSeq >= 0 && re == _helloSeq)
                {
                    OnHelloReply(env);
                    return;
                }
                if (_pending.TryGetValue(re, out var p))
                {
                    _pending.Remove(re);
                    Deliver(p.Callback, NetReply.FromEnvelope(env), p.Type);
                }
                return; // PONG and late replies are dropped
            }
            if (Status != NetStatus.Online)
            {
                // Pre-WELCOME pushes: only errors matter (e.g. HELLO_REQUIRED before close 4001).
                if (env.IsError) _helloErrorCode = NetJson.Str(env.Data["code"], _helloErrorCode ?? "");
                return;
            }
            if (env.Type == "GOING_DOWN") _goingDownReason = NetJson.Str(env.Data["reason"]);
            var type = env.Type;
            var data = env.Data;
            NetLog.Guard(() => Push?.Invoke(type, data), "Push(" + type + ")");
        }

        private void OnHelloReply(NetEnvelope env)
        {
            _helloSeq = -1;
            var d = env.Data;
            if (env.Type == "WELCOME")
            {
                var code = NetJson.Str(d["friendCode"]);
                if (code.Length > 0 && code != Identity.FriendCode)
                {
                    Identity.FriendCode = code;
                    Identity.Save();
                }
                HeartbeatSec = Math.Max(5, NetJson.Int(d["heartbeatSec"], 20));
                ReconnectAttempts = 0;
                LastReconnectDelayMs = 0;
                NextReconnectAtMs = -1;
                LastErrorCode = "";
                LastWelcome = d;
                var now = _now();
                _lastPingMs = now;
                _lastRxMs = now;
                SetStatus(NetStatus.Online);
                if (Status == NetStatus.Online) NetLog.Guard(() => Welcome?.Invoke(d), "Welcome");
                return;
            }
            if (env.IsError)
            {
                _helloErrorCode = NetJson.Str(d["code"], "ERROR");
                LastErrorCode = _helloErrorCode;
                // The server closes right after; the handshake timeout is the safety net.
            }
        }

        private void OnClosed(int code, string reason)
        {
            string err;
            if (!string.IsNullOrEmpty(_helloErrorCode)) err = _helloErrorCode;
            else if (_goingDownReason == NetErrorCodes.Replaced || _goingDownReason == NetErrorCodes.Banned || _goingDownReason == NetErrorCodes.Moved) err = _goingDownReason;
            else err = NetErrorCodes.FromClose(code, reason, _opened);
            HandleLinkDown(err);
        }

        private void HandleLinkDown(string errorCode)
        {
            var t = _transport;
            ResetLink();
            if (t != null)
            {
                try { t.Close(); } catch (Exception) { /* ignore */ }
            }
            LastErrorCode = errorCode ?? "";
            if (NetErrorCodes.IsFatal(errorCode))
            {
                WantsConnection = false;
                NextReconnectAtMs = -1;
            }
            else if (WantsConnection)
            {
                ScheduleReconnect(errorCode);
            }
            SetStatus(NetStatus.Offline);
            FailAllPending(NetErrorCodes.Offline);
        }

        private void ScheduleReconnect(string errorCode)
        {
            var exp = Math.Min(ReconnectAttempts, 16);
            long delay = Math.Min(ReconnectMaxMs, (long)ReconnectBaseMs << exp);
            ReconnectAttempts++;
            delay += (long)(_random.NextDouble() * 0.3 * delay);
            if (errorCode == NetErrorCodes.RateLimit) delay = Math.Max(delay, RateLimitMinDelayMs);
            LastReconnectDelayMs = delay;
            NextReconnectAtMs = _now() + delay;
        }

        private void ExpireRequests(long now)
        {
            List<KeyValuePair<long, PendingRequest>> expired = null;
            foreach (var kv in _pending)
            {
                if (now >= kv.Value.Deadline)
                {
                    if (expired == null) expired = new List<KeyValuePair<long, PendingRequest>>();
                    expired.Add(kv);
                }
            }
            if (expired == null) return;
            foreach (var kv in expired) _pending.Remove(kv.Key);
            foreach (var kv in expired) Deliver(kv.Value.Callback, NetReply.Fail(NetErrorCodes.Timeout), kv.Value.Type);
        }

        private void FailAllPending(string code)
        {
            if (_pending.Count == 0) return;
            var list = new List<PendingRequest>(_pending.Values);
            _pending.Clear();
            foreach (var p in list) Deliver(p.Callback, NetReply.Fail(code), p.Type);
        }

        private static void Deliver(Action<NetReply> cb, NetReply reply, string type)
        {
            if (cb == null) return;
            NetLog.Guard(() => cb(reply), "reply callback (" + type + ")");
        }

        private void SetStatus(NetStatus next)
        {
            if (Status == next) return;
            Status = next;
            NetLog.Guard(() => StatusChanged?.Invoke(next), "StatusChanged");
        }
    }
}
