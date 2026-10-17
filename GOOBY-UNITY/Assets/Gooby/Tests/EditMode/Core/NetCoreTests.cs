using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>In-memory transport for NetSession tests: records sent frames, tests inject events.</summary>
    public sealed class FakeNetTransport : INetTransport
    {
        private readonly Queue<NetTransportEvent> _events = new Queue<NetTransportEvent>();

        public string Url;
        public bool Connected;
        public bool Closed;
        public readonly List<string> Sent = new List<string>();

        public void Connect(string url)
        {
            Url = url;
            Connected = true;
        }

        public void Send(string text) => Sent.Add(text);

        public void Close() => Closed = true;

        public bool TryDequeue(out NetTransportEvent ev)
        {
            if (_events.Count > 0)
            {
                ev = _events.Dequeue();
                return true;
            }
            ev = null;
            return false;
        }

        public void Open() => _events.Enqueue(NetTransportEvent.Opened());

        public void Receive(string json) => _events.Enqueue(NetTransportEvent.Message(json));

        public void Receive(string t, JObject d, long? re = null)
        {
            var o = new JObject { ["v"] = 1, ["t"] = t, ["ts"] = 1 };
            if (re.HasValue) o["re"] = re.Value;
            o["d"] = d ?? new JObject();
            Receive(o.ToString());
        }

        public void ServerClose(int code, string reason) => _events.Enqueue(NetTransportEvent.Closed(code, reason));

        public List<JObject> SentMessages() => Sent.Select(JObject.Parse).ToList();

        public List<JObject> SentOfType(string t) => SentMessages().Where(m => (string)m["t"] == t).ToList();

        public JObject LastOfType(string t) => SentOfType(t).LastOrDefault();
    }

    /// <summary>INetHttp fake: records calls; tests complete them via Respond.</summary>
    public sealed class FakeNetHttp : INetHttp
    {
        public sealed class Call
        {
            public string Method, Url, Bearer, Body;
            public Action<int, string> Done;
        }

        public readonly List<Call> Calls = new List<Call>();
        private readonly Queue<Action> _ready = new Queue<Action>();

        public void Send(string method, string url, string bearer, string jsonBody, Action<int, string> done) =>
            Calls.Add(new Call { Method = method, Url = url, Bearer = bearer, Body = jsonBody, Done = done });

        public void Respond(Call call, int status, string body) => _ready.Enqueue(() => call.Done(status, body));

        public void Pump()
        {
            while (_ready.Count > 0) _ready.Dequeue()();
        }
    }

    public class NetCoreTests
    {
        private string _dir;
        private long _now;
        private List<FakeNetTransport> _fakes;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gooby-netcore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _now = 1_000_000;
            _fakes = new List<FakeNetTransport>();
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (Exception) { /* best effort */ }
        }

        private FakeNetTransport Fake => _fakes.Last();

        private NetSession NewSession(string secret = "")
        {
            var identity = NetIdentity.CreateNew(Path.Combine(_dir, "id.json"));
            var s = new NetSession(identity, () =>
            {
                var f = new FakeNetTransport();
                _fakes.Add(f);
                return f;
            }, () => _now, new System.Random(7));
            s.Configure(NetAddress.Parse("localhost"), "Anna", "Flausch", secret, "test-1");
            return s;
        }

        private static long SeqOf(JObject msg) => (long)msg["seq"];

        private static JObject Welcome(string code = "GOOBY-AB23", int heartbeat = 20) => new JObject
        {
            ["friendCode"] = code,
            ["serverTime"] = 123,
            ["heartbeatSec"] = heartbeat,
            ["friendRequests"] = new JArray(),
            ["palPending"] = new JArray(),
            ["pendingEvents"] = new JArray(),
            ["mailUnread"] = 0,
        };

        private NetSession OnlineSession(int heartbeat = 20)
        {
            var s = NewSession();
            s.Connect();
            Fake.Open();
            s.Pump();
            var hello = Fake.LastOfType("HELLO");
            Fake.Receive("WELCOME", Welcome(heartbeat: heartbeat), SeqOf(hello));
            s.Pump();
            Assert.That(s.Status, Is.EqualTo(NetStatus.Online));
            return s;
        }

        // ------------------------------------------------------------------ NetAddress

        [TestCase("192.168.0.10", "192.168.0.10", 8080, false, "ws://192.168.0.10:8080/ws")]
        [TestCase("192.168.0.10:8080", "192.168.0.10", 8080, false, "ws://192.168.0.10:8080/ws")]
        [TestCase("192.168.0.10:9001", "192.168.0.10", 9001, false, "ws://192.168.0.10:9001/ws")]
        [TestCase("localhost", "localhost", 8080, false, "ws://localhost:8080/ws")]
        [TestCase("gooby.example.com", "gooby.example.com", 8080, false, "ws://gooby.example.com:8080/ws")]
        [TestCase("ws://host.lan:1234/whatever", "host.lan", 1234, false, "ws://host.lan:1234/ws")]
        [TestCase("ws://10.0.0.5", "10.0.0.5", 8080, false, "ws://10.0.0.5:8080/ws")]
        [TestCase("wss://gooby.example.com", "gooby.example.com", 443, true, "wss://gooby.example.com:443/ws")]
        [TestCase("WSS://Gooby.Example.com:8443/ws", "gooby.example.com", 8443, true, "wss://gooby.example.com:8443/ws")]
        [TestCase("https://gooby.example.com/panel/", "gooby.example.com", 443, true, "wss://gooby.example.com:443/ws")]
        [TestCase("http://192.168.0.10:8080", "192.168.0.10", 8080, false, "ws://192.168.0.10:8080/ws")]
        [TestCase("[::1]:8080", "::1", 8080, false, "ws://[::1]:8080/ws")]
        [TestCase("[::1]", "::1", 8080, false, "ws://[::1]:8080/ws")]
        [TestCase("wss://[2001:db8::1]:9443/x", "2001:db8::1", 9443, true, "wss://[2001:db8::1]:9443/ws")]
        [TestCase("  192.168.0.10:8080  ", "192.168.0.10", 8080, false, "ws://192.168.0.10:8080/ws")]
        [TestCase("\t gooby.example.com \n", "gooby.example.com", 8080, false, "ws://gooby.example.com:8080/ws")]
        public void NetAddressParsesValidInputs(string text, string host, int port, bool tls, string wsUrl)
        {
            var ep = NetAddress.Parse(text);
            Assert.That(ep.Valid, Is.True, text);
            Assert.That(ep.Host, Is.EqualTo(host));
            Assert.That(ep.Port, Is.EqualTo(port));
            Assert.That(ep.Tls, Is.EqualTo(tls));
            Assert.That(ep.WsUrl, Is.EqualTo(wsUrl));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("host:abc")]
        [TestCase("host:0")]
        [TestCase("host:70000")]
        [TestCase("ftp://host")]
        [TestCase("ws://")]
        [TestCase("wss:///ws")]
        [TestCase("two words")]
        [TestCase("[::1")]
        [TestCase("[zz::1]:80")]
        [TestCase("user@host")]
        [TestCase("host..example")]
        public void NetAddressRejectsInvalidInputs(string text)
        {
            var ep = NetAddress.Parse(text);
            Assert.That(ep.Valid, Is.False, text ?? "(null)");
            Assert.That(ep.WsUrl, Is.EqualTo(""));
        }

        [Test]
        public void CloseCode4008TellsTheAccountMoveFromTheSecretChecks()
        {
            Assert.That(NetErrorCodes.FromClose(4008, "MOVED", true), Is.EqualTo(NetErrorCodes.Moved), "move.js closes both ends of a move");
            Assert.That(NetErrorCodes.FromClose(4008, "SECRET_WRONG", true), Is.EqualTo(NetErrorCodes.SecretWrong));
            Assert.That(NetErrorCodes.FromClose(4008, "", true), Is.EqualTo(NetErrorCodes.SecretRequired));
            Assert.That(NetErrorCodes.IsFatal(NetErrorCodes.Moved), Is.True, "the moved-away device must not retry with its void secret");
        }

        [Test]
        public void NetAddressHttpBaseDisplayAndPortOverride()
        {
            var ep = NetAddress.Parse("wss://gooby.example.com");
            Assert.That(ep.HttpBase, Is.EqualTo("https://gooby.example.com:443"));
            Assert.That(ep.Display, Is.EqualTo("wss://gooby.example.com:443"));
            var lan = NetAddress.Parse("192.168.0.10");
            Assert.That(lan.HttpBase, Is.EqualTo("http://192.168.0.10:8080"));
            Assert.That(lan.Display, Is.EqualTo("192.168.0.10:8080"));
            Assert.That(lan.IsPrivate, Is.True);

            Assert.That(NetAddress.Parse("192.168.0.10:9000", 7000).Port, Is.EqualTo(7000));
            Assert.That(NetAddress.Parse("192.168.0.10:9000", 0).Port, Is.EqualTo(9000), "invalid override ignored");
            Assert.That(NetAddress.Parse("192.168.0.10", null).Port, Is.EqualTo(8080));
            Assert.That(NetAddress.Parse("[::1]:8080").HttpBase, Is.EqualTo("http://[::1]:8080"));
            Assert.That(NetAddress.Parse("localhost:8080"), Is.EqualTo(NetAddress.Parse("ws://LOCALHOST:8080/ws")));
        }

        [TestCase("localhost", true)]
        [TestCase("127.0.0.1", true)]
        [TestCase("10.1.2.3", true)]
        [TestCase("172.16.0.1", true)]
        [TestCase("172.31.255.254", true)]
        [TestCase("192.168.0.10", true)]
        [TestCase("192.168.0.10:8080", true)]
        [TestCase("::1", true)]
        [TestCase("[::1]:8080", true)]
        [TestCase("fd12::1", true)]
        [TestCase("fe80::1%eth0", true)]
        [TestCase("gooby.local", true)]
        [TestCase("8.8.8.8", false)]
        [TestCase("172.32.0.1", false)]
        [TestCase("11.0.0.1", false)]
        [TestCase("gooby.example.com", false)]
        [TestCase("2001:db8::1", false)]
        [TestCase("", false)]
        public void IsPrivateHostClassifiesLanTargets(string host, bool expected)
        {
            Assert.That(NetAddress.IsPrivateHost(host), Is.EqualTo(expected), host);
        }

        // ------------------------------------------------------------------ envelope

        [Test]
        public void EnvelopeBuildAndParseRoundtrip()
        {
            var text = NetEnvelope.Build("MAIL_SEND", 42, 1700000000000, new JObject { ["to"] = "GOOBY-AB23", ["text"] = "Hallo Ümlaut 🥕" });
            var raw = JObject.Parse(text);
            Assert.That((int)raw["v"], Is.EqualTo(1));
            Assert.That((string)raw["t"], Is.EqualTo("MAIL_SEND"));
            Assert.That((long)raw["seq"], Is.EqualTo(42));
            Assert.That((long)raw["ts"], Is.EqualTo(1700000000000));

            var env = NetEnvelope.Parse(text);
            Assert.That(env, Is.Not.Null);
            Assert.That(env.Type, Is.EqualTo("MAIL_SEND"));
            Assert.That(env.Seq, Is.EqualTo(42));
            Assert.That(env.Re, Is.Null);
            Assert.That((string)env.Data["text"], Is.EqualTo("Hallo Ümlaut 🥕"));

            var reply = NetEnvelope.Parse("{\"v\":1,\"t\":\"ERROR\",\"ts\":5,\"re\":7,\"d\":{\"code\":\"NOT_FRIENDS\",\"when\":\"2024-01-01T10:00:00Z\"}}");
            Assert.That(reply.Re, Is.EqualTo(7));
            Assert.That(reply.IsError, Is.True);
            Assert.That(reply.Data["when"].Type, Is.EqualTo(JTokenType.String), "no date coercion");
            Assert.That((string)reply.Data["when"], Is.EqualTo("2024-01-01T10:00:00Z"));

            var noData = NetEnvelope.Parse("{\"v\":1,\"t\":\"PONG\",\"d\":[1,2]}");
            Assert.That(noData.Data, Is.Not.Null);
            Assert.That(noData.Data.Count, Is.EqualTo(0));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("nope")]
        [TestCase("[1,2]")]
        [TestCase("{}")]
        [TestCase("{\"t\":5}")]
        [TestCase("{\"t\":\"\"}")]
        [TestCase("{\"t\":\"OK\"} trailing")]
        [TestCase("{\"t\":\"OK\"")]
        public void EnvelopeParseRejectsGarbage(string text)
        {
            Assert.That(NetEnvelope.Parse(text), Is.Null);
        }

        [Test]
        public void NetReplyNormalizesResultPayloads()
        {
            var ok = NetReply.FromEnvelope(NetEnvelope.Parse("{\"t\":\"MAIL_RESULT\",\"re\":1,\"d\":{\"ok\":true,\"id\":\"mail-1\"}}"));
            Assert.That(ok.Ok, Is.True);
            Assert.That(ok.Type, Is.EqualTo("MAIL_RESULT"));
            var fail = NetReply.FromEnvelope(NetEnvelope.Parse("{\"t\":\"PAL_RESULT\",\"re\":1,\"d\":{\"ok\":false,\"code\":\"NOT_FRIENDS\"}}"));
            Assert.That(fail.Ok, Is.False);
            Assert.That(fail.Code, Is.EqualTo("NOT_FRIENDS"));
            var err = NetReply.FromEnvelope(NetEnvelope.Parse("{\"t\":\"ERROR\",\"re\":1,\"d\":{}}"));
            Assert.That(err.Ok, Is.False);
            Assert.That(err.Code, Is.EqualTo("ERROR"));
        }

        // ------------------------------------------------------------------ identity

        [Test]
        public void IdentityCreateLoadAndCorrupt()
        {
            var path = Path.Combine(_dir, "sub", "identity.json");
            var a = NetIdentity.LoadOrCreate(path);
            Assert.That(File.Exists(path), Is.True);
            Assert.That(a.DeviceId, Does.Match("^un-[0-9a-f]{32}$"));
            Assert.That(a.DeviceSecret, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(a.FriendCode, Is.EqualTo(""));
            Assert.That(a.Bearer, Is.EqualTo("Bearer " + a.DeviceId + ":" + a.DeviceSecret));

            a.FriendCode = "GOOBY-AB23";
            a.MarkPalCredited("pal-1");
            Assert.That(a.Save(), Is.True);
            Assert.That(File.Exists(path + ".tmp"), Is.False, "tmp file replaced");

            var b = NetIdentity.LoadOrCreate(path);
            Assert.That(b.DeviceId, Is.EqualTo(a.DeviceId));
            Assert.That(b.DeviceSecret, Is.EqualTo(a.DeviceSecret));
            Assert.That(b.FriendCode, Is.EqualTo("GOOBY-AB23"));
            Assert.That(b.CreditedPal, Has.Member("pal-1"));

            File.WriteAllText(path, "{ kaputt");
            var c = NetIdentity.LoadOrCreate(path);
            Assert.That(c.DeviceId, Is.Not.EqualTo(a.DeviceId));
            Assert.That(NetIdentity.LoadOrCreate(path).DeviceId, Is.EqualTo(c.DeviceId), "corrupt file was replaced");

            File.WriteAllText(path, "{\"deviceId\":\"x\",\"deviceSecret\":\"nothex\"}");
            Assert.That(NetIdentity.LoadOrCreate(path).DeviceId, Does.StartWith("un-"));
        }

        [Test]
        public void IdentityCreditedPalKeepsNewest200()
        {
            var path = Path.Combine(_dir, "identity.json");
            var id = NetIdentity.LoadOrCreate(path);
            for (var i = 0; i < 250; i++) id.MarkPalCredited("pal-" + i);
            Assert.That(id.MarkPalCredited("pal-249"), Is.False, "duplicate");
            Assert.That(id.CreditedPal.Count, Is.EqualTo(NetIdentity.CreditedPalCap));
            Assert.That(id.CreditedPal, Has.No.Member("pal-0"));
            Assert.That(id.CreditedPal, Has.Member("pal-50"));
            id.Save();
            var loaded = NetIdentity.LoadOrCreate(path);
            Assert.That(loaded.CreditedPal.Count, Is.EqualTo(NetIdentity.CreditedPalCap));
            Assert.That(loaded.IsPalCredited("pal-249"), Is.True);
            Assert.That(loaded.IsPalCredited("pal-49"), Is.False);
        }

        // ------------------------------------------------------------------ session

        [Test]
        public void SessionSendsHelloOnOpenAndGoesOnlineOnWelcome()
        {
            var s = NewSession();
            var statuses = new List<NetStatus>();
            JObject welcome = null;
            s.StatusChanged += st => statuses.Add(st);
            s.Welcome += d => welcome = d;

            s.Connect();
            Assert.That(_fakes.Count, Is.EqualTo(1));
            Assert.That(Fake.Url, Is.EqualTo("ws://localhost:8080/ws"));
            Assert.That(s.Status, Is.EqualTo(NetStatus.Connecting));
            s.Pump();
            Assert.That(Fake.Sent, Is.Empty, "nothing before open");

            Fake.Open();
            s.Pump();
            var hello = Fake.LastOfType("HELLO");
            Assert.That(hello, Is.Not.Null);
            Assert.That((int)hello["v"], Is.EqualTo(1));
            var d = (JObject)hello["d"];
            Assert.That((string)d["deviceId"], Is.EqualTo(s.Identity.DeviceId));
            Assert.That((string)d["deviceSecret"], Is.EqualTo(s.Identity.DeviceSecret));
            Assert.That((string)d["name"], Is.EqualTo("Anna"));
            Assert.That((string)d["goobyName"], Is.EqualTo("Flausch"));
            Assert.That((string)d["appVersion"], Is.EqualTo("test-1"));
            Assert.That(d["secret"], Is.Null);
            Assert.That(s.Status, Is.EqualTo(NetStatus.Connecting));

            // Requests before WELCOME are refused (server would close with HELLO_REQUIRED).
            NetReply early = null;
            s.Request("FRIENDS_LIST", new JObject(), r => early = r);
            Assert.That(early.Code, Is.EqualTo("OFFLINE"));

            Fake.Receive("WELCOME", Welcome("GOOBY-XY45", 7), SeqOf(hello));
            s.Pump();
            Assert.That(s.Status, Is.EqualTo(NetStatus.Online));
            Assert.That(welcome, Is.Not.Null);
            Assert.That(s.FriendCode, Is.EqualTo("GOOBY-XY45"));
            Assert.That(s.Identity.FriendCode, Is.EqualTo("GOOBY-XY45"));
            Assert.That(s.HeartbeatSec, Is.EqualTo(7));
            Assert.That(statuses, Is.EqualTo(new[] { NetStatus.Connecting, NetStatus.Online }));
            Assert.That(NetIdentity.LoadOrCreate(s.Identity.Path).FriendCode, Is.EqualTo("GOOBY-XY45"), "friend code persisted");
        }

        /// <summary>A transport that can send on open gets HELLO with the handshake (slow clients beat the 5 s HELLO deadline).</summary>
        private sealed class EarlyFrameTransport : INetTransport, INetTransportOpenFrame
        {
            public readonly Queue<NetTransportEvent> Events = new Queue<NetTransportEvent>();
            public readonly List<string> Sent = new List<string>();
            public string SendOnOpen { get; set; }
            public void Connect(string url) { }
            public void Send(string text) => Sent.Add(text);
            public void Close() { }
            public bool TryDequeue(out NetTransportEvent ev)
            {
                if (Events.Count > 0) { ev = Events.Dequeue(); return true; }
                ev = null;
                return false;
            }
        }

        [Test]
        public void SessionHandsHelloToTheTransportBeforeTheSocketOpens()
        {
            var t = new EarlyFrameTransport();
            var dir = Path.Combine(Path.GetTempPath(), "gooby-net-early-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var s = new NetSession(NetIdentity.LoadOrCreate(Path.Combine(dir, "id.json")), () => t, () => 0);
            s.Configure(NetAddress.Parse("localhost"), "Anna", "Flausch", "", "test-1");
            s.Connect();
            Assert.That(t.SendOnOpen, Is.Not.Null.And.Contains("\"HELLO\""), "HELLO rides on the handshake");
            var hello = JObject.Parse(t.SendOnOpen);
            Assert.That((string)hello["d"]["name"], Is.EqualTo("Anna"));
            t.Events.Enqueue(NetTransportEvent.Opened());
            s.Pump();
            Assert.That(t.Sent, Is.Empty, "no second HELLO after Opened");
            var welcome = new JObject { ["v"] = 1, ["t"] = "WELCOME", ["ts"] = 0, ["re"] = (long)hello["seq"], ["d"] = new JObject { ["friendCode"] = "GOOBY-AB23", ["heartbeatSec"] = 20 } };
            t.Events.Enqueue(NetTransportEvent.Message(welcome.ToString(Newtonsoft.Json.Formatting.None)));
            s.Pump();
            Assert.That(s.Status, Is.EqualTo(NetStatus.Online));
            Assert.That(s.FriendCode, Is.EqualTo("GOOBY-AB23"));
            try { Directory.Delete(dir, true); } catch (Exception) { /* best effort */ }
        }

        [Test]
        public void SessionHelloCarriesJoinSecretWhenConfigured()
        {
            var s = NewSession("geheim");
            s.Connect();
            Fake.Open();
            s.Pump();
            Assert.That((string)Fake.LastOfType("HELLO")["d"]["secret"], Is.EqualTo("geheim"));
        }

        [Test]
        public void SessionCorrelatesRepliesByRe()
        {
            var s = OnlineSession();
            NetReply a = null, b = null, c = null;
            var calls = 0;
            s.Request("FRIENDS_LIST", new JObject(), r => { a = r; calls++; });
            s.Request("MAIL_SEND", new JObject { ["to"] = "GOOBY-AB23" }, r => { b = r; calls++; });
            s.Request("FRIEND_REQUEST", new JObject { ["target"] = "GOOBY-ZZZZ" }, r => { c = r; calls++; });
            var seqA = SeqOf(Fake.LastOfType("FRIENDS_LIST"));
            var seqB = SeqOf(Fake.LastOfType("MAIL_SEND"));
            var seqC = SeqOf(Fake.LastOfType("FRIEND_REQUEST"));
            Assert.That(s.PendingRequestCount, Is.EqualTo(3));

            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = false, ["code"] = "NOT_FRIENDS" }, seqB);
            Fake.Receive("ERROR", new JObject { ["code"] = "NOT_FOUND" }, seqC);
            Fake.Receive("FRIENDS_STATE", new JObject { ["friends"] = new JArray() }, seqA);
            Fake.Receive("OK", new JObject(), 9999); // unknown re → ignored
            s.Pump();

            Assert.That(calls, Is.EqualTo(3));
            Assert.That(a.Ok, Is.True);
            Assert.That(a.Type, Is.EqualTo("FRIENDS_STATE"));
            Assert.That(b.Ok, Is.False);
            Assert.That(b.Code, Is.EqualTo("NOT_FRIENDS"));
            Assert.That(c.Ok, Is.False);
            Assert.That(c.Type, Is.EqualTo("ERROR"));
            Assert.That(c.Code, Is.EqualTo("NOT_FOUND"));
            Assert.That(s.PendingRequestCount, Is.EqualTo(0));
        }

        [Test]
        public void SessionRequestTimesOut()
        {
            var s = OnlineSession();
            var replies = new List<NetReply>();
            s.Request("MAIL_LIST", new JObject(), replies.Add, 5000);
            var seq = SeqOf(Fake.LastOfType("MAIL_LIST"));
            _now += 4999;
            s.Pump();
            Assert.That(replies, Is.Empty);
            _now += 2;
            s.Pump();
            Assert.That(replies.Count, Is.EqualTo(1));
            Assert.That(replies[0].Code, Is.EqualTo("TIMEOUT"));
            Fake.Receive("MAIL_STATE", new JObject(), seq);
            s.Pump();
            Assert.That(replies.Count, Is.EqualTo(1), "late reply dropped");

            s.Request("MAIL_LIST", new JObject(), replies.Add);
            _now += NetSession.DefaultRequestTimeoutMs + 1;
            Fake.Receive("PONG", new JObject()); // keep the link alive
            s.Pump();
            Assert.That(replies.Count, Is.EqualTo(2));
            Assert.That(replies[1].Code, Is.EqualTo("TIMEOUT"));
        }

        [Test]
        public void SessionOfflineRequestRepliesImmediately()
        {
            var s = NewSession();
            NetReply reply = null;
            s.Request("FRIENDS_LIST", new JObject(), r => reply = r);
            Assert.That(reply, Is.Not.Null, "synchronous");
            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Code, Is.EqualTo("OFFLINE"));
            Assert.That(s.Send("PING", new JObject()), Is.EqualTo(-1));
            Assert.That(_fakes, Is.Empty);
        }

        [Test]
        public void SessionLinkLossFailsPendingAndSchedulesReconnect()
        {
            var s = OnlineSession();
            NetReply reply = null;
            s.Request("MAIL_LIST", new JObject(), r => reply = r);
            Fake.ServerClose(1006, "");
            s.Pump();
            Assert.That(reply.Code, Is.EqualTo("OFFLINE"));
            Assert.That(s.Status, Is.EqualTo(NetStatus.Offline));
            Assert.That(s.LastErrorCode, Is.EqualTo("CONNECTION_LOST"));
            Assert.That(s.WantsConnection, Is.True);
            Assert.That(s.NextReconnectAtMs, Is.GreaterThan(_now));
            _now = s.NextReconnectAtMs;
            s.Pump();
            Assert.That(_fakes.Count, Is.EqualTo(2), "reconnected");
            Assert.That(s.Status, Is.EqualTo(NetStatus.Connecting));
        }

        [Test]
        public void SessionFatalAuthFailStopsReconnect()
        {
            var s = NewSession();
            s.Connect();
            Fake.Open();
            s.Pump();
            var hello = Fake.LastOfType("HELLO");
            Fake.Receive("ERROR", new JObject { ["code"] = "AUTH_FAIL" }, SeqOf(hello));
            Fake.ServerClose(4004, "AUTH_FAIL");
            s.Pump();
            Assert.That(s.Status, Is.EqualTo(NetStatus.Offline));
            Assert.That(s.LastErrorCode, Is.EqualTo("AUTH_FAIL"));
            Assert.That(s.WantsConnection, Is.False);
            Assert.That(s.NextReconnectAtMs, Is.EqualTo(-1));
            for (var i = 0; i < 10; i++)
            {
                _now += 60_000;
                s.Pump();
            }
            Assert.That(_fakes.Count, Is.EqualTo(1), "no reconnect loop");
        }

        [TestCase(4005, "BANNED", "BANNED")]
        [TestCase(4007, "REPLACED", "REPLACED")]
        [TestCase(4008, "SECRET_WRONG", "SECRET_WRONG")]
        [TestCase(4008, "SECRET_REQUIRED", "SECRET_REQUIRED")]
        [TestCase(4002, "PROTO_VERSION", "PROTO_VERSION")]
        public void SessionFatalCloseCodesStopReconnect(int code, string reason, string expected)
        {
            var s = NewSession();
            s.Connect();
            Fake.Open();
            s.Pump();
            Fake.ServerClose(code, reason);
            s.Pump();
            Assert.That(s.LastErrorCode, Is.EqualTo(expected));
            Assert.That(s.WantsConnection, Is.False);
            _now += 120_000;
            s.Pump();
            Assert.That(_fakes.Count, Is.EqualTo(1));
        }

        [Test]
        public void SessionReplacedViaGoingDownStopsReconnect()
        {
            var s = OnlineSession();
            var pushes = new List<string>();
            s.Push += (t, d) => pushes.Add(t + ":" + (string)d["reason"]);
            Fake.Receive("GOING_DOWN", new JObject { ["reason"] = "REPLACED" });
            Fake.ServerClose(1006, ""); // close code lost — GOING_DOWN reason still wins
            s.Pump();
            Assert.That(pushes, Has.Member("GOING_DOWN:REPLACED"));
            Assert.That(s.LastErrorCode, Is.EqualTo("REPLACED"));
            Assert.That(s.WantsConnection, Is.False);
        }

        [Test]
        public void SessionBackoffGrowsCapsAndResetsOnWelcome()
        {
            var s = NewSession();
            s.Connect();
            var delays = new List<long>();
            for (var i = 0; i < 8; i++)
            {
                Fake.ServerClose(NetTransportEvent.CodeConnectFailed, "CONNECT_FAILED");
                s.Pump();
                Assert.That(s.LastErrorCode, Is.EqualTo("UNREACHABLE"));
                delays.Add(s.LastReconnectDelayMs);
                Assert.That(s.NextReconnectAtMs, Is.EqualTo(_now + s.LastReconnectDelayMs));
                _now = s.NextReconnectAtMs - 1;
                s.Pump();
                Assert.That(_fakes.Count, Is.EqualTo(i + 1), "not before the backoff");
                _now++;
                s.Pump();
                Assert.That(_fakes.Count, Is.EqualTo(i + 2));
            }
            var expected = new long[] { 1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000 };
            for (var i = 0; i < expected.Length; i++)
                Assert.That(delays[i], Is.InRange(expected[i], (long)(expected[i] * 1.3)), "attempt " + i);

            Fake.Open();
            s.Pump();
            Fake.Receive("WELCOME", Welcome(), SeqOf(Fake.LastOfType("HELLO")));
            s.Pump();
            Assert.That(s.ReconnectAttempts, Is.EqualTo(0));
            Fake.ServerClose(1001, "SHUTDOWN");
            s.Pump();
            Assert.That(s.LastErrorCode, Is.EqualTo("SHUTDOWN"));
            Assert.That(s.LastReconnectDelayMs, Is.InRange(1000L, 1300L), "reset after WELCOME");
        }

        [Test]
        public void SessionRateLimitWaitsForTheHelloBucket()
        {
            var s = NewSession();
            s.Connect();
            Fake.Open();
            s.Pump();
            Fake.Receive("ERROR", new JObject { ["code"] = "RATE_LIMIT" }, SeqOf(Fake.LastOfType("HELLO")));
            Fake.ServerClose(4003, "RATE_LIMIT");
            s.Pump();
            Assert.That(s.LastErrorCode, Is.EqualTo("RATE_LIMIT"));
            Assert.That(s.WantsConnection, Is.True);
            Assert.That(s.LastReconnectDelayMs, Is.GreaterThanOrEqualTo(NetSession.RateLimitMinDelayMs));
        }

        [Test]
        public void SessionHeartbeatPingsAndDropsDeadLink()
        {
            var s = OnlineSession(heartbeat: 5);
            Assert.That(Fake.SentOfType("PING"), Is.Empty);
            _now += 5000;
            s.Pump();
            Assert.That(Fake.SentOfType("PING").Count, Is.EqualTo(1));
            Fake.Receive("PONG", new JObject(), SeqOf(Fake.LastOfType("PING")));
            s.Pump();
            _now += 5000;
            s.Pump();
            Assert.That(Fake.SentOfType("PING").Count, Is.EqualTo(2));
            // No answers any more: after 3 × heartbeat of silence the link counts as dead.
            for (var i = 0; i < 6 && s.Status == NetStatus.Online; i++)
            {
                _now += 4000;
                s.Pump();
            }
            Assert.That(s.Status, Is.EqualTo(NetStatus.Offline));
            Assert.That(s.LastErrorCode, Is.EqualTo("TIMEOUT"));
            Assert.That(_fakes[0].Closed, Is.True);
            Assert.That(s.NextReconnectAtMs, Is.GreaterThan(0));
        }

        [Test]
        public void SessionHandshakeTimeout()
        {
            var s = NewSession();
            s.Connect();
            Fake.Open();
            s.Pump();
            _now += s.HandshakeTimeoutMs + 1;
            s.Pump();
            Assert.That(s.Status, Is.EqualTo(NetStatus.Offline));
            Assert.That(s.LastErrorCode, Is.EqualTo("TIMEOUT"));
            Assert.That(_fakes[0].Closed, Is.True);
            Assert.That(s.WantsConnection, Is.True);
        }

        [Test]
        public void SessionConnectTimeoutFromTransportMapsToTimeout()
        {
            var s = NewSession();
            s.Connect();
            Fake.ServerClose(NetTransportEvent.CodeConnectTimeout, "CONNECT_TIMEOUT");
            s.Pump();
            Assert.That(s.LastErrorCode, Is.EqualTo("TIMEOUT"));
        }

        [Test]
        public void SessionPushesAndDisconnect()
        {
            var s = OnlineSession();
            var pushes = new List<string>();
            s.Push += (t, d) => pushes.Add(t);
            Fake.Receive("FRIEND_ADDED", new JObject { ["friendCode"] = "GOOBY-BBBB" });
            Fake.Receive("garbage");
            s.Pump();
            Assert.That(pushes, Is.EqualTo(new[] { "FRIEND_ADDED" }));

            NetReply pending = null;
            s.Request("MAIL_LIST", new JObject(), r => pending = r);
            s.Disconnect();
            Assert.That(pending.Code, Is.EqualTo("OFFLINE"));
            Assert.That(Fake.Closed, Is.True);
            Assert.That(s.Status, Is.EqualTo(NetStatus.Offline));
            _now += 120_000;
            s.Pump();
            Assert.That(_fakes.Count, Is.EqualTo(1), "Disconnect() stops reconnects");
        }

        [Test]
        public void SessionInvalidEndpointDoesNotConnect()
        {
            var s = NewSession();
            s.Configure(NetAddress.Parse("not a host"), "Anna", "Flausch", "", "t");
            s.Connect();
            Assert.That(_fakes, Is.Empty);
            Assert.That(s.LastErrorCode, Is.EqualTo("INVALID_ENDPOINT"));
            Assert.That(s.Status, Is.EqualTo(NetStatus.Offline));
        }

        // ------------------------------------------------------------------ outbox

        [Test]
        public void OutboxPersistsAndReloads()
        {
            var path = Path.Combine(_dir, "outbox.json");
            var box = new NetOutbox(path);
            var id1 = box.Enqueue(new JObject { ["to"] = "GOOBY-AB23", ["text"] = "Hallo", ["clientId"] = "m-11111111" });
            var id2 = box.Enqueue(new JObject { ["to"] = "GOOBY-AB23", ["text"] = "Geschenk", ["item"] = new JObject { ["typ"] = "items", ["id"] = "medicine", ["menge"] = 1 } });
            Assert.That(id1, Is.EqualTo("m-11111111"));
            Assert.That(id2, Does.Match("^[A-Za-z0-9._-]{8,64}$"));
            box.Enqueue(new JObject { ["to"] = "GOOBY-AB23", ["text"] = "Hallo 2", ["clientId"] = "m-11111111" });
            Assert.That(box.Count, Is.EqualTo(2), "same clientId upserts");

            var reloaded = new NetOutbox(path);
            Assert.That(reloaded.Count, Is.EqualTo(2));
            Assert.That(reloaded.Entries[0].Id, Is.EqualTo(id1));
            Assert.That((string)reloaded.Entries[0].Payload["text"], Is.EqualTo("Hallo 2"));
            Assert.That((string)reloaded.Entries[1].Payload["clientId"], Is.EqualTo(id2));
            Assert.That((string)reloaded.Entries[1].Item["id"], Is.EqualTo("medicine"));
            Assert.That(reloaded.Remove(id1), Is.True);
            Assert.That(new NetOutbox(path).Count, Is.EqualTo(1));

            File.WriteAllText(path, "][");
            Assert.That(new NetOutbox(path).Count, Is.EqualTo(0), "corrupt file → empty");
        }

        [Test]
        public void OutboxFlushRemovesOnSuccessAndBounceKeepsOnTransientErrors()
        {
            var box = new NetOutbox(Path.Combine(_dir, "outbox.json"));
            box.Enqueue(new JObject { ["to"] = "GOOBY-AAAA", ["text"] = "1", ["clientId"] = "m-00000001" });
            box.Enqueue(new JObject { ["to"] = "GOOBY-BBBB", ["text"] = "2", ["clientId"] = "m-00000002", ["item"] = new JObject { ["typ"] = "food", ["id"] = "apple" } });
            box.Enqueue(new JObject { ["to"] = "GOOBY-CCCC", ["text"] = "3", ["clientId"] = "m-00000003" });
            var s = OnlineSession();
            var delivered = new List<string>();
            var bounced = new List<string>();
            var done = 0;

            box.Flush(s, (e, r) => delivered.Add(e.Id), (e, code) => bounced.Add(e.To + ":" + code + ":" + (string)e.Item?["id"]), () => done++);
            Assert.That(box.Flushing, Is.True);
            var first = Fake.LastOfType("MAIL_SEND");
            Assert.That((string)first["d"]["clientId"], Is.EqualTo("m-00000001"));
            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = true, ["id"] = "", ["dupe"] = true }, SeqOf(first));
            s.Pump();
            Assert.That(delivered, Is.EqualTo(new[] { "m-00000001" }));

            var second = Fake.LastOfType("MAIL_SEND");
            Assert.That((string)second["d"]["clientId"], Is.EqualTo("m-00000002"));
            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = false, ["code"] = "NOT_FRIENDS" }, SeqOf(second));
            s.Pump();
            Assert.That(bounced, Is.EqualTo(new[] { "GOOBY-BBBB:NOT_FRIENDS:apple" }));

            var third = Fake.LastOfType("MAIL_SEND");
            Assert.That((string)third["d"]["clientId"], Is.EqualTo("m-00000003"));
            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = false, ["code"] = "RATE_LIMIT" }, SeqOf(third));
            s.Pump();
            Assert.That(box.Count, Is.EqualTo(1), "transient failure keeps the letter");
            Assert.That(box.Flushing, Is.False);
            Assert.That(done, Is.EqualTo(1));

            s.Disconnect();
            box.Flush(s, null, null, () => done++);
            Assert.That(done, Is.EqualTo(2));
            Assert.That(box.Count, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ presence kinds

        [TestCase("home", "home")]
        [TestCase("home/kitchen", "home")]
        [TestCase("garden", "garden")]
        [TestCase("city", "city")]
        [TestCase("city/ort/rehwei", "park")]
        [TestCase("city/ort/gouhbus", "ikea")]
        [TestCase("city/ort/baecker", "baecker")]
        [TestCase("drive", "drive")]
        [TestCase("park", "park")]
        [TestCase("social/visit", "visit")]
        [TestCase("sleep", "sleep")]
        [TestCase("vacation", "vacation")]
        [TestCase("minigame:gardenRush", "minigame:gardenRush")]
        [TestCase("settings", "home")]
        [TestCase("", "home")]
        public void PresenceKindsForRoute(string route, string expected)
        {
            Assert.That(PresenceKinds.ForRoute(route), Is.EqualTo(expected));
        }

        [Test]
        public void PresenceKindsMinigame()
        {
            Assert.That(PresenceKinds.ForRoute("mg_host", "bubblePop"), Is.EqualTo("minigame:bubblePop"));
            Assert.That(PresenceKinds.Minigame(""), Is.EqualTo("minigame:arcade"));
            Assert.That(PresenceKinds.Minigame(new string('x', 50)).Length, Is.EqualTo(PresenceKinds.MaxLength));
        }

        [TestCase("", false, "ranch")]
        [TestCase("besuch", false, "ranchbesuch")]
        [TestCase("besuch", true, "ranch")]
        [TestCase("ausritt", true, "ausritt")]
        [TestCase("rennen", false, "rennen")]
        [TestCase("fangen", false, "fangen")]
        [TestCase("parcours", true, "parcours")]
        [TestCase("quatsch", false, "ranch")]
        public void PresenceKindsForRanch(string mode, bool isHost, string expected)
        {
            Assert.That(PresenceKinds.ForRanch(mode, isHost), Is.EqualTo(expected));
        }

        // ------------------------------------------------------------------ social client (fake transport)

        private SocialClient NewSocial(out FakeNetHttp http)
        {
            http = new FakeNetHttp();
            var client = new SocialClient(Path.Combine(_dir, "social"), () =>
            {
                var f = new FakeNetTransport();
                _fakes.Add(f);
                return f;
            }, () => _now, http);
            client.Configure(NetAddress.Parse("192.168.0.10"), "Anna", "Flausch", "");
            return client;
        }

        private void Welcome(SocialClient c, JObject welcome)
        {
            c.Connect();
            Fake.Open();
            c.Pump();
            Fake.Receive("WELCOME", welcome, SeqOf(Fake.LastOfType("HELLO")));
            c.Pump();
            Assert.That(c.Status, Is.EqualTo(NetStatus.Online));
        }

        [Test]
        public void SocialPalReceivedOnceAndAckedAcrossRedeliveries()
        {
            var c = NewSocial(out _);
            var credited = new List<string>();
            c.PalReceived += (id, from, amount) => credited.Add(id + ":" + from + ":" + amount);
            var w = Welcome("GOOBY-AB23");
            w["palPending"] = new JArray(new JObject { ["id"] = "pal-1", ["from"] = "GOOBY-CD45", ["amount"] = 25, ["at"] = 1 });
            Welcome(c, w);
            Assert.That(credited, Is.EqualTo(new[] { "pal-1:GOOBY-CD45:25" }));
            Assert.That((string)Fake.LastOfType("PAL_ACK")["d"]["id"], Is.EqualTo("pal-1"));
            Assert.That(c.Identity.IsPalCredited("pal-1"), Is.True);

            // Live push of a new gift, then the same id again (lost ACK) → raised once.
            Fake.Receive("PAL_RECEIVED", new JObject { ["id"] = "pal-2", ["from"] = "GOOBY-CD45", ["amount"] = 5 });
            Fake.Receive("PAL_RECEIVED", new JObject { ["id"] = "pal-2", ["from"] = "GOOBY-CD45", ["amount"] = 5 });
            c.Pump();
            Assert.That(credited.Count, Is.EqualTo(2));
            Assert.That(Fake.SentOfType("PAL_ACK").Count, Is.EqualTo(3));

            // Reconnect: WELCOME re-delivers both (ACK lost) → ACK only, no second credit.
            Fake.ServerClose(1006, "");
            c.Pump();
            _now = c.Session.NextReconnectAtMs;
            c.Pump();
            Fake.Open();
            c.Pump();
            var w2 = Welcome("GOOBY-AB23");
            w2["palPending"] = new JArray(
                new JObject { ["id"] = "pal-1", ["from"] = "GOOBY-CD45", ["amount"] = 25 },
                new JObject { ["id"] = "pal-2", ["from"] = "GOOBY-CD45", ["amount"] = 5 });
            Fake.Receive("WELCOME", w2, SeqOf(Fake.LastOfType("HELLO")));
            c.Pump();
            Assert.That(credited.Count, Is.EqualTo(2));
            Assert.That(Fake.SentOfType("PAL_ACK").Select(m => (string)m["d"]["id"]), Is.EqualTo(new[] { "pal-1", "pal-2" }));
        }

        [Test]
        public void SocialPalWithoutSubscriberIsNotAcked()
        {
            var c = NewSocial(out _);
            var w = Welcome("GOOBY-AB23");
            w["palPending"] = new JArray(new JObject { ["id"] = "pal-9", ["from"] = "GOOBY-CD45", ["amount"] = 3 });
            Welcome(c, w);
            Assert.That(Fake.SentOfType("PAL_ACK"), Is.Empty);
            Assert.That(c.Identity.IsPalCredited("pal-9"), Is.False);
        }

        [Test]
        public void SocialPresenceIsDedupedAndResentAfterWelcome()
        {
            var c = NewSocial(out _);
            c.SetPresence("garden"); // offline → remembered
            Welcome(c, Welcome("GOOBY-AB23"));
            Assert.That(Fake.SentOfType("PRESENCE_SET").Select(m => (string)m["d"]["kind"]), Is.EqualTo(new[] { "garden" }));
            c.SetPresence("garden");
            c.Pump();
            Assert.That(Fake.SentOfType("PRESENCE_SET").Count, Is.EqualTo(1), "deduped");
            for (var i = 0; i < 20; i++) c.SetPresence(i % 2 == 0 ? "home" : "city");
            Assert.That(Fake.SentOfType("PRESENCE_SET").Count, Is.LessThanOrEqualTo(SocialClient.PresenceBurst), "client bucket");
            _now += 60_000;
            Fake.Receive("PONG", new JObject());
            c.Pump();
            Assert.That((string)Fake.LastOfType("PRESENCE_SET")["d"]["kind"], Is.EqualTo("city"), "latest wanted kind wins");
        }

        [Test]
        public void SocialSyncCoinsIsThrottled()
        {
            var c = NewSocial(out _);
            c.SyncCoins(500); // offline → remembered, sent after WELCOME
            Welcome(c, Welcome("GOOBY-AB23"));
            Func<int[]> synced = () => Fake.SentOfType("SYNC").Select(m => (int)m["d"]["coins"]).ToArray();
            Assert.That(synced(), Is.EqualTo(new[] { 500 }));
            c.SyncCoins(510);
            _now += 5000;
            Fake.Receive("PONG", new JObject());
            c.Pump();
            Assert.That(synced(), Is.EqualTo(new[] { 500 }), "small change waits for the 30 s window");
            c.SyncCoins(700); // big change → soon
            Assert.That(synced(), Is.EqualTo(new[] { 500, 700 }));
            c.SyncCoins(705);
            _now += SocialClient.CoinsSyncIntervalMs;
            Fake.Receive("PONG", new JObject());
            c.Pump();
            Assert.That(synced(), Is.EqualTo(new[] { 500, 700, 705 }), "pending value flushed after the window");
            _now += SocialClient.CoinsSyncIntervalMs;
            Fake.Receive("PONG", new JObject());
            c.Pump();
            Assert.That(synced().Length, Is.EqualTo(3), "unchanged → nothing");
        }

        [Test]
        public void SocialSendMailOfflineQueuesAndFlushesOnWelcome()
        {
            var c = NewSocial(out _);
            MailSendResult result = null;
            c.SendMail("gooby-cd45", "Hallo!", "items", "medicine", 1, r => result = r);
            Assert.That(result.Queued, Is.True);
            Assert.That(result.Code, Is.EqualTo("QUEUED"));
            Assert.That(c.OutboxCount, Is.EqualTo(1));

            MailSendResult bad = null;
            c.SendMail("GOOBY-CD45", "   ", null, null, 0, r => bad = r);
            Assert.That(bad.Code, Is.EqualTo("BAD_MAIL"));
            c.SendMail("GOOBY-CD45", new string('x', 501), null, null, 0, r => bad = r);
            Assert.That(bad.Code, Is.EqualTo("TEXT_TOO_LONG"));

            Welcome(c, Welcome("GOOBY-AB23"));
            var sent = Fake.LastOfType("MAIL_SEND");
            Assert.That(sent, Is.Not.Null);
            Assert.That((string)sent["d"]["to"], Is.EqualTo("GOOBY-CD45"));
            Assert.That((string)sent["d"]["clientId"], Is.EqualTo(result.ClientId));
            Assert.That((string)sent["d"]["item"]["typ"], Is.EqualTo("items"));
            Assert.That((int)sent["d"]["item"]["menge"], Is.EqualTo(1));
            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = true, ["id"] = "mail-1" }, SeqOf(sent));
            c.Pump();
            Assert.That(c.OutboxCount, Is.EqualTo(0));
        }

        [Test]
        public void SocialAcceptVisitUploadsSnapshotThenAccepts()
        {
            var c = NewSocial(out var http);
            Welcome(c, Welcome("GOOBY-AB23"));
            var ready = new List<string>();
            c.VisitReady += (room, host, guest, rev) => ready.Add(room + "|" + host + "|" + guest + "|" + rev);
            NetReply acceptReply = null;
            var snapshot = new JObject { ["v"] = 1, ["goobyName"] = "Flausch", ["rooms"] = new JObject { ["living"] = new JObject { ["items"] = new JArray() } } };
            c.AcceptVisit("GOOBY-CD45", snapshot, r => acceptReply = r);
            Assert.That(http.Calls.Count, Is.EqualTo(1));
            Assert.That(http.Calls[0].Method, Is.EqualTo("PUT"));
            Assert.That(http.Calls[0].Url, Is.EqualTo("http://192.168.0.10:8080/api/house"));
            Assert.That(http.Calls[0].Bearer, Is.EqualTo(c.Identity.Bearer));
            Assert.That(JObject.Parse(http.Calls[0].Body)["rooms"]["living"], Is.Not.Null);
            Assert.That(Fake.SentOfType("VISIT_ACCEPT"), Is.Empty, "upload first");

            http.Respond(http.Calls[0], 200, "{\"ok\":true,\"rev\":3}");
            c.Pump();
            var accept = Fake.LastOfType("VISIT_ACCEPT");
            Assert.That((string)accept["d"]["guest"], Is.EqualTo("GOOBY-CD45"));
            Fake.Receive("VISIT_READY", new JObject { ["room"] = "visit:GOOBY-AB23", ["host"] = "GOOBY-AB23", ["guest"] = "GOOBY-CD45", ["rev"] = 3 }, SeqOf(accept));
            c.Pump();
            Assert.That(c.VisitRole, Is.EqualTo(VisitRole.Host));
            Assert.That(c.PeerCode, Is.EqualTo("GOOBY-CD45"));
            var join = Fake.LastOfType("ROOM_JOIN");
            Assert.That((string)join["d"]["room"], Is.EqualTo("visit:GOOBY-AB23"));
            Assert.That(ready, Is.Empty, "VisitReady after the room join");
            Fake.Receive("OK", new JObject { ["room"] = "visit:GOOBY-AB23", ["members"] = new JArray("GOOBY-AB23") }, SeqOf(join));
            c.Pump();
            Assert.That(ready, Is.EqualTo(new[] { "visit:GOOBY-AB23|GOOBY-AB23|GOOBY-CD45|3" }));
            Assert.That(acceptReply.Ok, Is.True);
            Assert.That(c.VisitJoined, Is.True);

            Assert.That(c.SendVisitPos(1.23456f, -2f, "walk", "living"), Is.True);
            Assert.That(c.SendVisitPos(1f, 1f, "walk", "living"), Is.False, "5 Hz throttle");
            _now += SocialClient.PosIntervalMs;
            Assert.That(c.SendVisitPos(1f, 1f, "walk", "living"), Is.True);
            var pos = Fake.SentOfType("ROOM_MSG").First();
            Assert.That((string)pos["d"]["kind"], Is.EqualTo("POS"));
            Assert.That((double)pos["d"]["body"]["pos"][0], Is.EqualTo(1.235).Within(1e-9));
            Assert.That((string)pos["d"]["body"]["roomId"], Is.EqualTo("living"));

            // Peer (guest) joins and sends POS + EMOTE.
            var peer = new List<string>();
            c.PeerJoined += (code, name, gooby) => peer.Add("join:" + code + ":" + name);
            c.PeerPos += (x, z, anim, roomId) => peer.Add("pos:" + x.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture) + ":" + anim + ":" + roomId);
            c.PeerEmote += id => peer.Add("emote:" + id);
            c.PeerLeft += code => peer.Add("left:" + code);
            Fake.Receive("ROOM_PEER_JOINED", new JObject { ["room"] = "visit:GOOBY-AB23", ["friendCode"] = "GOOBY-CD45", ["name"] = "Ben", ["goobyName"] = "Knopf" });
            Fake.Receive("ROOM_MSG", new JObject { ["room"] = "visit:GOOBY-AB23", ["kind"] = "POS", ["body"] = new JObject { ["pos"] = new JArray(2.5, 3), ["anim"] = "walk", ["roomId"] = "kitchen" }, ["from"] = new JObject { ["friendCode"] = "GOOBY-CD45" } });
            Fake.Receive("ROOM_MSG", new JObject { ["room"] = "visit:GOOBY-AB23", ["kind"] = "EMOTE", ["body"] = new JObject { ["id"] = "heart" } });
            Fake.Receive("ROOM_MSG", new JObject { ["room"] = "visit:OTHER", ["kind"] = "EMOTE", ["body"] = new JObject { ["id"] = "x" } });
            Fake.Receive("ROOM_PEER_LEFT", new JObject { ["room"] = "visit:GOOBY-AB23", ["friendCode"] = "GOOBY-CD45" });
            c.Pump();
            Assert.That(peer, Is.EqualTo(new[] { "join:GOOBY-CD45:Ben", "pos:2.5:3:walk:kitchen", "emote:heart", "left:GOOBY-CD45" }));
            Assert.That(c.PeerRoomId, Is.EqualTo("kitchen"));
            Assert.That(c.VisitActive, Is.True, "a guest leaving does not end the host's visit");

            var ended = new List<string>();
            c.VisitEnded += (room, by) => ended.Add(room + "|" + by);
            Fake.Receive("VISIT_ENDED", new JObject { ["room"] = "visit:GOOBY-AB23", ["by"] = "GOOBY-CD45" });
            c.Pump();
            Assert.That(ended, Is.EqualTo(new[] { "visit:GOOBY-AB23|GOOBY-CD45" }));
            Assert.That(c.VisitActive, Is.False);
            Assert.That(c.VisitRole, Is.EqualTo(VisitRole.None));
            Assert.That(c.SendVisitPos(0, 0, "idle", "living", true), Is.False);
        }

        [Test]
        public void SocialAcceptVisitUploadFailureIsReported()
        {
            var c = NewSocial(out var http);
            Welcome(c, Welcome("GOOBY-AB23"));
            NetReply reply = null;
            c.AcceptVisit("GOOBY-CD45", new JObject { ["rooms"] = new JObject() }, r => reply = r);
            http.Respond(http.Calls[0], 413, "{\"ok\":false,\"code\":\"PAYLOAD_TOO_LARGE\"}");
            c.Pump();
            Assert.That(reply.Code, Is.EqualTo("PAYLOAD_TOO_LARGE"));
            Assert.That(Fake.SentOfType("VISIT_ACCEPT"), Is.Empty);

            bool? ok = null;
            string code = null;
            c.FetchHouse("GOOBY-CD45", (o, layout, cd) => { ok = o; code = cd; });
            Assert.That(http.Calls[1].Url, Is.EqualTo("http://192.168.0.10:8080/api/house/GOOBY-CD45"));
            http.Respond(http.Calls[1], 0, "timeout");
            c.Pump();
            Assert.That(ok, Is.False);
            Assert.That(code, Is.EqualTo("NO_CONNECTION"));
        }

        [Test]
        public void SocialGuestEndsVisitWhenHostDropsWithoutVisitEnded()
        {
            var c = NewSocial(out _);
            Welcome(c, Welcome("GOOBY-CD45"));
            Fake.Receive("VISIT_READY", new JObject { ["room"] = "visit:GOOBY-AB23", ["host"] = "GOOBY-AB23", ["guest"] = "GOOBY-CD45", ["rev"] = 1 });
            c.Pump();
            Assert.That(c.VisitRole, Is.EqualTo(VisitRole.Guest));
            Fake.Receive("OK", new JObject { ["room"] = "visit:GOOBY-AB23", ["members"] = new JArray("GOOBY-AB23", "GOOBY-CD45") }, SeqOf(Fake.LastOfType("ROOM_JOIN")));
            c.Pump();
            Assert.That(c.PeerInRoom, Is.True, "peer known from the members list");
            var ended = new List<string>();
            c.VisitEnded += (room, by) => ended.Add(by);
            Fake.Receive("ROOM_PEER_LEFT", new JObject { ["room"] = "visit:GOOBY-AB23", ["friendCode"] = "GOOBY-AB23" });
            c.Pump();
            Assert.That(ended, Is.Empty, "grace period for a following VISIT_ENDED");
            _now += SocialClient.HostGoneGraceMs;
            Fake.Receive("PONG", new JObject());
            c.Pump();
            Assert.That(ended, Is.EqualTo(new[] { "GOOBY-AB23" }));
            Assert.That(c.VisitActive, Is.False);
        }

        [Test]
        public void SocialServerEventsBouncesAndGoingDown()
        {
            var c = NewSocial(out _);
            var events = new List<string>();
            var bounced = new List<string>();
            var goingDown = new List<string>();
            c.ServerEvent += e => events.Add((string)e["id"] + ":" + (string)e["type"]);
            c.MailBounced += (to, item, code) => bounced.Add(to + ":" + code + ":" + (string)item?["id"]);
            c.GoingDown += reason => goingDown.Add(reason);

            c.SendMail("GOOBY-CD45", "", "food", "apple", 3, null); // queued offline (gift only is fine)
            var w = Welcome("GOOBY-AB23");
            w["pendingEvents"] = new JArray(new JObject { ["id"] = "evt-1", ["type"] = "DOUBLE_COINS", ["params"] = new JObject() });
            Welcome(c, w);
            Assert.That(events, Is.EqualTo(new[] { "evt-1:DOUBLE_COINS" }));
            Assert.That((string)Fake.LastOfType("EVENT_ACK")["d"]["id"], Is.EqualTo("evt-1"));

            Fake.Receive("SERVER_EVENT", new JObject { ["id"] = "evt-1", ["type"] = "DOUBLE_COINS" }); // duplicate
            Fake.Receive("SERVER_EVENT", new JObject { ["id"] = "evt-2", ["type"] = "SNOW" });
            var send = Fake.LastOfType("MAIL_SEND");
            Assert.That((int)send["d"]["item"]["menge"], Is.EqualTo(3));
            Fake.Receive("MAIL_RESULT", new JObject { ["ok"] = false, ["code"] = "NOT_FRIENDS" }, SeqOf(send));
            Fake.Receive("GOING_DOWN", new JObject { ["reason"] = "SHUTDOWN" });
            c.Pump();
            Assert.That(events, Is.EqualTo(new[] { "evt-1:DOUBLE_COINS", "evt-2:SNOW" }));
            Assert.That(Fake.SentOfType("EVENT_ACK").Select(m => (string)m["d"]["id"]), Is.EqualTo(new[] { "evt-1", "evt-1", "evt-2" }));
            Assert.That(bounced, Is.EqualTo(new[] { "GOOBY-CD45:NOT_FRIENDS:apple" }));
            Assert.That(c.OutboxCount, Is.EqualTo(0));
            Assert.That(goingDown, Is.EqualTo(new[] { "SHUTDOWN" }));
        }

        [Test]
        public void SocialConfigureNewServerReconnectsAndClearsServerState()
        {
            var c = NewSocial(out _);
            var w = Welcome("GOOBY-AB23");
            w["friendRequests"] = new JArray(new JObject { ["from"] = "GOOBY-CD45", ["name"] = "Ben" });
            Welcome(c, w);
            Assert.That(c.Requests.Count, Is.EqualTo(1));

            c.Configure(NetAddress.Parse("192.168.0.10"), "Anna2", "Flausch", "");
            Assert.That((string)Fake.LastOfType("PROFILE_UPDATE")["d"]["name"], Is.EqualTo("Anna2"), "same server → profile update");
            Assert.That(_fakes.Count, Is.EqualTo(1));

            c.Configure(NetAddress.Parse("wss://other.example.com"), "Anna2", "Flausch", "");
            Assert.That(_fakes.Count, Is.EqualTo(2), "new server → reconnect");
            Assert.That(_fakes[0].Closed, Is.True);
            Assert.That(Fake.Url, Is.EqualTo("wss://other.example.com:443/ws"));
            Assert.That(c.Requests, Is.Empty);
            Assert.That(c.FriendCode, Is.EqualTo(""));
        }

        [Test]
        public void SocialFriendPushesUpdateLists()
        {
            var c = NewSocial(out _);
            var w = Welcome("GOOBY-AB23");
            w["friendRequests"] = new JArray(new JObject { ["from"] = "GOOBY-CD45", ["name"] = "Ben", ["goobyName"] = "Knopf", ["at"] = 5 });
            Welcome(c, w);
            Assert.That(c.Requests.Count, Is.EqualTo(1));
            var changed = 0;
            c.Changed += () => changed++;
            var online = new List<bool>();
            c.FriendOnlineChanged += f => online.Add(f.Online);
            Fake.Receive("FRIEND_ADDED", new JObject { ["friendCode"] = "GOOBY-CD45", ["name"] = "Ben", ["goobyName"] = "Knopf", ["online"] = true, ["activity"] = new JObject { ["kind"] = "online", ["label"] = "ist online" }, ["coins"] = 10 });
            c.Pump();
            Assert.That(c.Requests, Is.Empty, "accepted request disappears");
            Assert.That(c.Friends.Count, Is.EqualTo(1));
            Assert.That(c.Friends[0].ActivityKind, Is.EqualTo("online"));
            Assert.That(changed, Is.EqualTo(1), "coalesced per Pump");
            Fake.Receive("FRIEND_PRESENCE", new JObject { ["friendCode"] = "GOOBY-CD45", ["online"] = true, ["activity"] = new JObject { ["kind"] = "garden", ["label"] = "ist mit Knopf im Garten" } });
            Fake.Receive("FRIEND_PRESENCE", new JObject { ["friendCode"] = "GOOBY-CD45", ["online"] = false, ["activity"] = null });
            c.Pump();
            Assert.That(online, Is.EqualTo(new[] { false }));
            Assert.That(c.Friends[0].Online, Is.False);
            Assert.That(c.Friends[0].ActivityKind, Is.EqualTo(""));
            Fake.Receive("FRIEND_REMOVED", new JObject { ["friendCode"] = "GOOBY-CD45" });
            c.Pump();
            Assert.That(c.Friends, Is.Empty);

            NetReply r = null;
            c.AddFriend("gooby-ef67", x => r = x);
            Assert.That((string)Fake.LastOfType("FRIEND_REQUEST")["d"]["target"], Is.EqualTo("GOOBY-EF67"));
            c.AddFriend("Lena", x => r = x);
            Assert.That((string)Fake.LastOfType("FRIEND_REQUEST")["d"]["targetName"], Is.EqualTo("Lena"));
            c.AddFriend("Gooby-Fan", x => r = x);
            Assert.That((string)Fake.LastOfType("FRIEND_REQUEST")["d"]["targetName"], Is.EqualTo("Gooby-Fan"));
        }
    }
}
