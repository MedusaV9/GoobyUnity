using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Runs the real GOOBY-SERVER (`node server.js`) on a free port with a temp DATA_DIR.
    /// dotnet-only (CoreTests~/Integration is not part of the Unity project).
    /// </summary>
    public sealed class GoobyServerProcess : IDisposable
    {
        private Process _proc;
        private readonly StringBuilder _log = new StringBuilder();

        public int Port { get; private set; }
        public string DataDir { get; private set; }
        public string Address => "127.0.0.1:" + Port;
        public string Log
        {
            get { lock (_log) return _log.ToString(); }
        }

        public static string FindServerDir()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var probe = Path.Combine(dir.FullName, "GOOBY-SERVER");
                    if (File.Exists(Path.Combine(probe, "server.js"))) return probe;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        public static string FindNode()
        {
            var overridePath = Environment.GetEnvironmentVariable("GOOBY_NODE");
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) return overridePath;
            var exe = RuntimeInformationIsWindows() ? "node.exe" : "node";
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static bool RuntimeInformationIsWindows() =>
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        /// <summary>Starts the server or calls Assert.Ignore when node / GOOBY-SERVER are unavailable.</summary>
        public static GoobyServerProcess StartOrIgnore(Dictionary<string, string> extraEnv = null)
        {
            var serverDir = FindServerDir();
            if (serverDir == null) Assert.Ignore("GOOBY-SERVER/server.js not found above " + AppContext.BaseDirectory);
            if (!Directory.Exists(Path.Combine(serverDir, "node_modules", "ws")))
                Assert.Ignore("GOOBY-SERVER/node_modules missing (npm install)");
            var node = FindNode();
            if (node == null) Assert.Ignore("node not found on PATH");

            var srv = new GoobyServerProcess();
            srv.Start(node, serverDir, extraEnv);
            return srv;
        }

        private void Start(string node, string serverDir, Dictionary<string, string> extraEnv)
        {
            Port = FreePort();
            DataDir = Path.Combine(Path.GetTempPath(), "gooby-it-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDir);
            var psi = new ProcessStartInfo(node, "server.js")
            {
                WorkingDirectory = serverDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.Environment["PORT"] = Port.ToString();
            psi.Environment["DATA_DIR"] = DataDir;
            psi.Environment["GOOBY_ADMIN_PASSWORD"] = "test";
            if (extraEnv != null)
                foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (s, e) => { if (e.Data != null) lock (_log) _log.AppendLine(e.Data); };
            _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (_log) _log.AppendLine("ERR " + e.Data); };
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 20000)
                {
                    if (_proc.HasExited) break;
                    try
                    {
                        var resp = http.GetAsync("http://127.0.0.1:" + Port + "/health").GetAwaiter().GetResult();
                        if (resp.IsSuccessStatusCode) return;
                    }
                    catch (Exception)
                    {
                        // not up yet
                    }
                    Thread.Sleep(100);
                }
            }
            var log = Log;
            Dispose();
            Assert.Fail("GOOBY-SERVER did not become healthy on port " + Port + ":\n" + log);
        }

        public void Dispose()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    _proc.Kill(true);
                    _proc.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
                // already gone
            }
            _proc?.Dispose();
            _proc = null;
            try { if (DataDir != null && Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch (Exception) { /* best effort */ }
        }
    }

    /// <summary>Records every SocialClient event for assertions.</summary>
    public sealed class Recorder
    {
        public readonly List<FriendRequestInfo> RequestsIncoming = new List<FriendRequestInfo>();
        public readonly List<FriendInfo> FriendsAdded = new List<FriendInfo>();
        public readonly List<MailInfo> MailArrived = new List<MailInfo>();
        public readonly List<string> MailBounced = new List<string>();
        public readonly List<string> Pal = new List<string>();
        public readonly List<string> VisitIncoming = new List<string>();
        public readonly List<string> VisitReady = new List<string>();
        public readonly List<string> VisitDenied = new List<string>();
        public readonly List<string> VisitEnded = new List<string>();
        public readonly List<string> PeerJoined = new List<string>();
        public readonly List<string> PeerLeft = new List<string>();
        public readonly List<float[]> PeerPos = new List<float[]>();
        public readonly List<string> PeerPosMeta = new List<string>();
        public readonly List<string> PeerEmote = new List<string>();
        public readonly List<KeyValuePair<string, JObject>> RawPushes = new List<KeyValuePair<string, JObject>>();
        public Action<string, string, int> OnPal;

        public Recorder(SocialClient c)
        {
            c.FriendRequestIncoming += r => RequestsIncoming.Add(r);
            c.FriendAdded += f => FriendsAdded.Add(f);
            c.MailArrived += m => MailArrived.Add(m);
            c.MailBounced += (to, item, code) => MailBounced.Add(to + ":" + code);
            c.PalReceived += (id, from, amount) =>
            {
                Pal.Add(id + ":" + from + ":" + amount);
                OnPal?.Invoke(id, from, amount);
            };
            c.VisitIncoming += (from, name, gooby) => VisitIncoming.Add(from + ":" + name + ":" + gooby);
            c.VisitReady += (room, host, guest, rev) => VisitReady.Add(room + "|" + host + "|" + guest + "|" + rev);
            c.VisitDenied += from => VisitDenied.Add(from);
            c.VisitEnded += (room, by) => VisitEnded.Add(room + "|" + by);
            c.PeerJoined += (code, name, gooby) => PeerJoined.Add(code + ":" + name + ":" + gooby);
            c.PeerLeft += code => PeerLeft.Add(code);
            c.PeerPos += (x, z, anim, roomId) =>
            {
                PeerPos.Add(new[] { x, z });
                PeerPosMeta.Add(anim + ":" + roomId);
            };
            c.PeerEmote += id => PeerEmote.Add(id);
            c.Session.Push += (t, d) => RawPushes.Add(new KeyValuePair<string, JObject>(t, (JObject)d.DeepClone()));
        }

        public JObject LastRaw(string type) => RawPushes.LastOrDefault(p => p.Key == type).Value;
    }

    /// <summary>Shared plumbing: one server per fixture (HELLO limit: 5/min per IP), real transports, pump loop.</summary>
    public abstract class ServerFixtureBase
    {
        protected GoobyServerProcess Server;
        protected readonly Stopwatch Clock = Stopwatch.StartNew();
        private readonly List<SocialClient> _clients = new List<SocialClient>();
        private readonly List<HttpClientNetHttp> _https = new List<HttpClientNetHttp>();
        private string _root;

        protected void StartServer(Dictionary<string, string> env = null)
        {
            Server = GoobyServerProcess.StartOrIgnore(env);
            _root = Path.Combine(Path.GetTempPath(), "gooby-itc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        protected SocialClient NewClient(string name, string gooby, string secret = "")
        {
            var http = new HttpClientNetHttp();
            _https.Add(http);
            var c = new SocialClient(Path.Combine(_root, name), () => new WebSocketTransport(), () => Clock.ElapsedMilliseconds, http);
            c.AppVersion = "it-test";
            c.Configure(NetAddress.Parse(Server.Address), name, gooby, secret);
            _clients.Add(c);
            return c;
        }

        /// <summary>Pumps all clients until <paramref name="cond"/> holds (fails with the server log otherwise).</summary>
        protected void PumpUntil(Func<bool> cond, string what, int timeoutMs = 8000)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                foreach (var c in _clients) c.Pump();
                if (cond()) return;
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    var states = string.Join(", ", _clients.Select(c => c.PlayerName + "=" + c.Status + "/" + c.LastErrorCode));
                    Assert.Fail("timeout waiting for: " + what + " [" + states + "]\n--- server log ---\n" + Server.Log);
                }
                Thread.Sleep(10);
            }
        }

        /// <summary>Pumps for a fixed time (to prove that something does NOT happen).</summary>
        protected void PumpFor(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                foreach (var c in _clients) c.Pump();
                Thread.Sleep(10);
            }
        }

        protected NetReply Await(Action<Action<NetReply>> start, string what, int timeoutMs = 8000)
        {
            NetReply reply = null;
            start(r => reply = r);
            PumpUntil(() => reply != null, what, timeoutMs);
            return reply;
        }

        protected MailSendResult AwaitMail(SocialClient c, string to, string text, string typ = null, string id = null, int menge = 0)
        {
            MailSendResult res = null;
            c.SendMail(to, text, typ, id, menge, r => res = r);
            PumpUntil(() => res != null, "MAIL_SEND result");
            return res;
        }

        protected void ConnectAll(params SocialClient[] clients)
        {
            foreach (var c in clients) c.Connect();
            PumpUntil(() => clients.All(c => c.IsOnline), "all online");
            // WELCOME triggers FRIENDS_LIST/MAIL_LIST — let them settle.
            PumpUntil(() => clients.All(c => c.Session.PendingRequestCount == 0), "welcome refreshes");
        }

        protected void MakeFriends(SocialClient a, Recorder ra, SocialClient b, Recorder rb)
        {
            var add = Await(cb => a.AddFriend(b.FriendCode, cb), "FRIEND_REQUEST");
            Assert.That(add.Ok, Is.True, add.Code);
            PumpUntil(() => rb.RequestsIncoming.Any(r => r.FromCode == a.FriendCode), "FRIEND_REQUEST_INCOMING");
            var accept = Await(cb => b.AcceptFriend(a.FriendCode, cb), "FRIEND_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            PumpUntil(() => a.FindFriend(b.FriendCode) != null && b.FindFriend(a.FriendCode) != null, "FRIEND_ADDED both");
        }

        [OneTimeTearDown]
        public void StopAll()
        {
            foreach (var c in _clients)
            {
                try { c.Disconnect(); } catch (Exception) { /* ignore */ }
            }
            Thread.Sleep(50);
            foreach (var h in _https) h.Dispose();
            Server?.Dispose();
            try { if (_root != null) Directory.Delete(_root, true); } catch (Exception) { /* best effort */ }
        }
    }

    /// <summary>Scenarios a–f: codes, friends, presence, letters (live, offline recipient, gift claim, outbox).</summary>
    [TestFixture]
    [NonParallelizable]
    public class FriendsAndMailIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob, _cara;
        private Recorder _ra, _rb, _rc;

        [OneTimeSetUp]
        public void Setup()
        {
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            _cara = NewClient("Cara", "Krümel");
            _ra = new Recorder(_alice);
            _rb = new Recorder(_bob);
            _rc = new Recorder(_cara);
        }

        [Test, Order(1)]
        public void A_ThreeClientsGetDistinctFriendCodes()
        {
            ConnectAll(_alice, _bob, _cara); // HELLO 1-3
            var codes = new[] { _alice.FriendCode, _bob.FriendCode, _cara.FriendCode };
            foreach (var code in codes) Assert.That(code, Does.Match("^GOOBY-[A-HJ-NP-Z2-9]{4}$"));
            Assert.That(codes.Distinct().Count(), Is.EqualTo(3));
            Assert.That(_alice.Identity.FriendCode, Is.EqualTo(_alice.FriendCode), "stored in identity");
            Assert.That(_alice.LastErrorCode, Is.EqualTo(""));
            Assert.That(_alice.Friends, Is.Empty);
        }

        [Test, Order(2)]
        public void B_FriendRequestByCodeAndAccept()
        {
            var add = Await(cb => _alice.AddFriend(_bob.FriendCode.ToLowerInvariant(), cb), "FRIEND_REQUEST");
            Assert.That(add.Ok, Is.True, add.Code);
            PumpUntil(() => _rb.RequestsIncoming.Count == 1, "FriendRequestIncoming");
            var req = _rb.RequestsIncoming[0];
            Assert.That(req.FromCode, Is.EqualTo(_alice.FriendCode));
            Assert.That(req.Name, Is.EqualTo("Alice"));
            Assert.That(req.GoobyName, Is.EqualTo("Flausch"));
            Assert.That(_bob.Requests.Select(r => r.FromCode), Has.Member(_alice.FriendCode));

            var dup = Await(cb => _alice.AddFriend(_bob.FriendCode, cb), "duplicate FRIEND_REQUEST");
            Assert.That(dup.Code, Is.EqualTo("DUPLICATE"));

            var accept = Await(cb => _bob.AcceptFriend(_alice.FriendCode, cb), "FRIEND_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            PumpUntil(() => _alice.FindFriend(_bob.FriendCode) != null && _bob.FindFriend(_alice.FriendCode) != null, "both friends");
            Assert.That(_bob.Requests, Is.Empty);
            var bobAtAlice = _alice.FindFriend(_bob.FriendCode);
            var aliceAtBob = _bob.FindFriend(_alice.FriendCode);
            Assert.That(bobAtAlice.Online, Is.True);
            Assert.That(aliceAtBob.Online, Is.True);
            Assert.That(bobAtAlice.Name, Is.EqualTo("Bob"));
            Assert.That(bobAtAlice.GoobyName, Is.EqualTo("Bobby"));
            Assert.That(aliceAtBob.GoobyName, Is.EqualTo("Flausch"));
            Assert.That(_ra.FriendsAdded.Select(f => f.Code), Has.Member(_bob.FriendCode));
            Assert.That(_rb.FriendsAdded.Select(f => f.Code), Has.Member(_alice.FriendCode));

            // RefreshFriends returns the same picture from the server.
            var refresh = Await(cb => _alice.RefreshFriends(cb), "FRIENDS_LIST");
            Assert.That(refresh.Ok, Is.True);
            Assert.That(_alice.Friends.Select(f => f.Code), Is.EqualTo(new[] { _bob.FriendCode }));
            Assert.That(_alice.FindFriend(_bob.FriendCode).Online, Is.True);
        }

        [Test, Order(3)]
        public void C_PresenceAndOfflineAndLetterForOfflineFriend()
        {
            _bob.SetPresence(PresenceKinds.Garden);
            PumpUntil(() => _alice.FindFriend(_bob.FriendCode)?.ActivityKind == "garden", "presence garden");
            Assert.That(_alice.FindFriend(_bob.FriendCode).ActivityLabel, Is.EqualTo("ist mit Bobby im Garten"));

            _bob.Disconnect();
            PumpUntil(() => _alice.FindFriend(_bob.FriendCode)?.Online == false, "Bob offline at Alice");
            Assert.That(_alice.FindFriend(_bob.FriendCode).ActivityKind, Is.EqualTo(""));

            // Letter while Bob is offline → delivered on his next boot (WELCOME.mailUnread + MAIL_LIST).
            var res = AwaitMail(_alice, _bob.FriendCode, "Brief, während du weg warst");
            Assert.That(res.Ok, Is.True, res.Code);
            Assert.That(res.Queued, Is.False);

            _bob.Connect(); // HELLO 4
            PumpUntil(() => _bob.IsOnline, "Bob reconnect");
            Assert.That(NetJson.Int(_bob.Session.LastWelcome["mailUnread"]), Is.EqualTo(1));
            PumpUntil(() => _bob.Mail.Count == 1, "RefreshMail after WELCOME");
            Assert.That(_bob.MailUnread, Is.EqualTo(1));
            Assert.That(_bob.Mail[0].Text, Is.EqualTo("Brief, während du weg warst"));
            Assert.That(_bob.Mail[0].From, Is.EqualTo(_alice.FriendCode));
            Assert.That(_bob.Mail[0].FromName, Is.EqualTo("Alice"));
            Assert.That(_bob.Mail[0].Read, Is.False);
            PumpUntil(() => _alice.FindFriend(_bob.FriendCode)?.Online == true, "Bob online again at Alice");
            // Presence is re-sent after the reconnect WELCOME.
            PumpUntil(() => _alice.FindFriend(_bob.FriendCode)?.ActivityKind == "garden", "presence re-sent");

            var ack = Await(cb => _bob.AckMail(_bob.Mail[0].Id, cb), "MAIL_ACK");
            Assert.That(ack.Ok, Is.True);
            Assert.That(_bob.MailUnread, Is.EqualTo(0));
        }

        [Test, Order(4)]
        public void D_LetterWithGiftClaimAckDelete()
        {
            var before = _rb.MailArrived.Count;
            var res = AwaitMail(_alice, _bob.FriendCode, "Hallo Bob! Medizin für Bobby 💊", "items", "medicine", 1);
            Assert.That(res.Ok, Is.True, res.Code);
            Assert.That(res.Id, Does.StartWith("mail-"));
            Assert.That(res.DailyLimit, Is.EqualTo(20));
            PumpUntil(() => _rb.MailArrived.Count == before + 1, "MailArrived live");
            var mail = _rb.MailArrived.Last();
            Assert.That(mail.Id, Is.EqualTo(res.Id));
            Assert.That(mail.Text, Is.EqualTo("Hallo Bob! Medizin für Bobby 💊"));
            Assert.That(mail.FromGooby, Is.EqualTo("Flausch"));
            Assert.That(mail.ItemTyp, Is.EqualTo("items"));
            Assert.That(mail.ItemId, Is.EqualTo("medicine"));
            Assert.That(mail.ItemMenge, Is.EqualTo(1));
            Assert.That(mail.Claimed, Is.False);
            Assert.That(_bob.MailUnread, Is.EqualTo(1));
            Assert.That(_bob.Mail[0].Id, Is.EqualTo(mail.Id), "live mail on top");

            var claim = Await(cb => _bob.ClaimMail(mail.Id, cb), "MAIL_CLAIM");
            Assert.That(claim.Ok, Is.True, claim.Code);
            Assert.That((string)claim.Data["item"]["id"], Is.EqualTo("medicine"));
            Assert.That(_bob.FindMail(mail.Id).Claimed, Is.True);
            var again = Await(cb => _bob.ClaimMail(mail.Id, cb), "second MAIL_CLAIM");
            Assert.That(again.Ok, Is.False);
            Assert.That(again.Code, Is.EqualTo("ALREADY_CLAIMED"));

            var ack = Await(cb => _bob.AckMail(mail.Id, cb), "MAIL_ACK");
            Assert.That(ack.Ok, Is.True);
            Assert.That(_bob.MailUnread, Is.EqualTo(0));
            Assert.That(_bob.FindMail(mail.Id).Read, Is.True);

            var del = Await(cb => _bob.DeleteMail(mail.Id, cb), "MAIL_DELETE");
            Assert.That(del.Ok, Is.True);
            Assert.That(NetJson.Bool(del.Data["removed"]), Is.True);
            Assert.That(_bob.FindMail(mail.Id), Is.Null);
            var refresh = Await(cb => _bob.RefreshMail(cb), "MAIL_LIST");
            Assert.That(refresh.Ok, Is.True);
            Assert.That(_bob.Mail.Select(m => m.Id), Has.No.Member(mail.Id));
        }

        [Test, Order(5)]
        public void E_LetterToNonFriendFails()
        {
            var res = AwaitMail(_alice, _cara.FriendCode, "Hallo Cara");
            Assert.That(res.Ok, Is.False);
            Assert.That(res.Queued, Is.False);
            Assert.That(res.Code, Is.EqualTo("NOT_FRIENDS"));
            var pal = Await(cb => _alice.SendPal(_cara.FriendCode, 5, cb), "PAL_SEND to non-friend");
            Assert.That(pal.Code, Is.EqualTo("NOT_FRIENDS"));
            var visit = Await(cb => _alice.RequestVisit(_cara.FriendCode, cb), "VISIT_REQUEST to non-friend");
            Assert.That(visit.Code, Is.EqualTo("NOT_FRIENDS"));
            Assert.That(_alice.OutboxCount, Is.EqualTo(0));
        }

        [Test, Order(6)]
        public void F_OfflineLetterIsQueuedAndDeliveredAfterReconnect()
        {
            _alice.Disconnect();
            PumpUntil(() => _bob.FindFriend(_alice.FriendCode)?.Online == false, "Alice offline at Bob");
            var res = AwaitMail(_alice, _bob.FriendCode, "Offline geschrieben ✉", "food", "apple", 2);
            Assert.That(res.Queued, Is.True);
            Assert.That(res.Code, Is.EqualTo("QUEUED"));
            Assert.That(_alice.OutboxCount, Is.EqualTo(1));
            Assert.That(File.Exists(_alice.Outbox.Path), Is.True, "persisted");
            Assert.That(new NetOutbox(_alice.Outbox.Path).Count, Is.EqualTo(1));

            var before = _rb.MailArrived.Count;
            _alice.Connect(); // HELLO 5
            PumpUntil(() => _rb.MailArrived.Count == before + 1, "queued letter delivered");
            PumpUntil(() => _alice.OutboxCount == 0, "outbox drained");
            var mail = _rb.MailArrived.Last();
            Assert.That(mail.Text, Is.EqualTo("Offline geschrieben ✉"));
            Assert.That(mail.ItemTyp, Is.EqualTo("food"));
            Assert.That(mail.ItemMenge, Is.EqualTo(2));
            Assert.That(_ra.MailBounced, Is.Empty);
            Assert.That(new NetOutbox(_alice.Outbox.Path).Count, Is.EqualTo(0));
        }
    }

    /// <summary>Scenarios g–h: GoobyPal with lost ACK + dedupe, full house visit with relay.</summary>
    [TestFixture]
    [NonParallelizable]
    public class PalAndVisitIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private Recorder _ra, _rb;

        [OneTimeSetUp]
        public void Setup()
        {
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            _ra = new Recorder(_alice);
            _rb = new Recorder(_bob);
            ConnectAll(_alice, _bob); // HELLO 1-2
            MakeFriends(_alice, _ra, _bob, _rb);
        }

        [Test, Order(1)]
        public void G_PalCreditedOnceEvenWhenTheAckIsLost()
        {
            // Bob credits the gift and drops the connection inside the handler → the PAL_ACK never
            // reaches the server, which re-delivers the gift in the next WELCOME.palPending.
            var dropOnce = true;
            _rb.OnPal = (id, from, amount) =>
            {
                if (!dropOnce) return;
                dropOnce = false;
                _bob.Disconnect();
            };
            var res = Await(cb => _alice.SendPal(_bob.FriendCode, 25, cb), "PAL_SEND");
            Assert.That(res.Ok, Is.True, res.Code);
            Assert.That(res.Type, Is.EqualTo("PAL_RESULT"));
            Assert.That(NetJson.Int(res.Data["sentToday"]), Is.EqualTo(25));
            PumpUntil(() => _rb.Pal.Count == 1, "PalReceived");
            var parts = _rb.Pal[0].Split(':');
            Assert.That(parts[0], Does.StartWith("pal-"));
            Assert.That(parts[1], Is.EqualTo(_alice.FriendCode));
            Assert.That(parts[2], Is.EqualTo("25"));
            Assert.That(_bob.Identity.IsPalCredited(parts[0]), Is.True);
            Assert.That(_bob.Status, Is.EqualTo(NetStatus.Offline));

            _bob.Connect(); // HELLO 3
            PumpUntil(() => _bob.IsOnline, "Bob reconnect");
            var pending = _bob.Session.LastWelcome["palPending"] as JArray;
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending.Select(p => (string)p["id"]), Has.Member(parts[0]), "server re-delivered (ACK was lost)");
            var sync = Await(cb => _bob.RefreshFriends(cb), "sync after ACK");
            Assert.That(sync.Ok, Is.True);
            Assert.That(_rb.Pal.Count, Is.EqualTo(1), "not raised again");

            // The dedupe ACK cleared the pending entry: the next boot has nothing pending.
            _bob.Disconnect();
            PumpFor(200);
            _bob.Connect(); // HELLO 4
            PumpUntil(() => _bob.IsOnline, "Bob reconnect 2");
            Assert.That(((JArray)_bob.Session.LastWelcome["palPending"]).Count, Is.EqualTo(0));
            PumpUntil(() => _alice.FindFriend(_bob.FriendCode)?.Online == true, "Bob online at Alice");
            PumpUntil(() => _bob.Session.PendingRequestCount == 0, "settle");
            Assert.That(_rb.Pal.Count, Is.EqualTo(1));
        }

        [Test, Order(2)]
        public void H_HouseVisitWithRelayAndEnd()
        {
            var req = Await(cb => _alice.RequestVisit(_bob.FriendCode, cb), "VISIT_REQUEST");
            Assert.That(req.Ok, Is.True, req.Code);
            PumpUntil(() => _rb.VisitIncoming.Count == 1, "VisitIncoming");
            Assert.That(_rb.VisitIncoming[0], Is.EqualTo(_alice.FriendCode + ":Alice:Flausch"));

            var snapshot = new JObject
            {
                ["v"] = 1,
                ["goobyName"] = "Bobby",
                ["rooms"] = new JObject { ["living"] = new JObject { ["items"] = new JArray() } },
            };
            var accept = Await(cb => _bob.AcceptVisit(_alice.FriendCode, snapshot, cb), "VISIT_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            Assert.That(accept.Type, Is.EqualTo("VISIT_READY"));
            var room = "visit:" + _bob.FriendCode;
            PumpUntil(() => _ra.VisitReady.Count == 1 && _rb.VisitReady.Count == 1, "VisitReady both");
            var expectedReady = room + "|" + _bob.FriendCode + "|" + _alice.FriendCode + "|1";
            Assert.That(_rb.VisitReady[0], Is.EqualTo(expectedReady));
            Assert.That(_ra.VisitReady[0], Is.EqualTo(expectedReady));
            Assert.That(_bob.VisitRole, Is.EqualTo(VisitRole.Host));
            Assert.That(_alice.VisitRole, Is.EqualTo(VisitRole.Guest));
            Assert.That(_alice.VisitRoom, Is.EqualTo(room));
            Assert.That(_alice.PeerCode, Is.EqualTo(_bob.FriendCode));
            Assert.That(_bob.PeerCode, Is.EqualTo(_alice.FriendCode));
            PumpUntil(() => _alice.VisitJoined && _bob.VisitJoined && _alice.PeerInRoom && _bob.PeerInRoom, "both in room");
            Assert.That(_ra.PeerJoined.Count + _rb.PeerJoined.Count, Is.EqualTo(2));
            // Exactly one side got the ROOM_PEER_JOINED push (the other saw the peer in ROOM_JOIN.members).
            var joinedPush = _ra.LastRaw("ROOM_PEER_JOINED") ?? _rb.LastRaw("ROOM_PEER_JOINED");
            Assert.That(joinedPush, Is.Not.Null);
            Assert.That(joinedPush.Properties().Select(p => p.Name).OrderBy(n => n), Is.EqualTo(new[] { "friendCode", "goobyName", "name", "room" }));
            Assert.That((string)joinedPush["room"], Is.EqualTo(room));

            bool? fetched = null;
            JObject layout = null;
            string fetchCode = null;
            _alice.FetchHouse(_bob.FriendCode, (ok, l, code) => { fetched = ok; layout = l; fetchCode = code; });
            PumpUntil(() => fetched.HasValue, "FetchHouse");
            Assert.That(fetched, Is.True, fetchCode);
            Assert.That((string)layout["goobyName"], Is.EqualTo("Bobby"));
            Assert.That(layout["rooms"]["living"]["items"], Is.InstanceOf<JArray>());

            Assert.That(_alice.SendVisitPos(1.5f, -2.25f, "walk", "living", true), Is.True);
            PumpUntil(() => _rb.PeerPos.Count >= 1, "PeerPos at Bob");
            Assert.That(_rb.PeerPos[0][0], Is.EqualTo(1.5f));
            Assert.That(_rb.PeerPos[0][1], Is.EqualTo(-2.25f));
            Assert.That(_rb.PeerPosMeta[0], Is.EqualTo("walk:living"));
            Assert.That(_bob.PeerRoomId, Is.EqualTo("living"));
            var rawPos = _rb.LastRaw("ROOM_MSG");
            Assert.That((string)rawPos["from"]["friendCode"], Is.EqualTo(_alice.FriendCode));

            Assert.That(_bob.SendVisitPos(3f, 4f, "idle", "kitchen", true), Is.True);
            PumpUntil(() => _ra.PeerPos.Count >= 1, "PeerPos at Alice");
            Assert.That(_ra.PeerPosMeta[0], Is.EqualTo("idle:kitchen"));

            Assert.That(_alice.SendVisitEmote("heart"), Is.True);
            PumpUntil(() => _rb.PeerEmote.Count == 1, "PeerEmote");
            Assert.That(_rb.PeerEmote[0], Is.EqualTo("heart"));

            var end = Await(cb => _alice.EndVisit(cb), "VISIT_END");
            Assert.That(end.Ok, Is.True, end.Code);
            PumpUntil(() => _ra.VisitEnded.Count == 1 && _rb.VisitEnded.Count == 1, "VisitEnded both");
            Assert.That(_ra.VisitEnded[0], Is.EqualTo(room + "|" + _alice.FriendCode));
            Assert.That(_rb.VisitEnded[0], Is.EqualTo(room + "|" + _alice.FriendCode));
            Assert.That(_alice.VisitActive, Is.False);
            Assert.That(_bob.VisitActive, Is.False);
            Assert.That(_alice.SendVisitPos(0, 0, "idle", "living", true), Is.False);
            PumpFor(SocialClient.HostGoneGraceMs + 200);
            Assert.That(_ra.VisitEnded.Count, Is.EqualTo(1), "no second VisitEnded from the host-left grace timer");
            var left = _ra.LastRaw("ROOM_PEER_LEFT");
            Assert.That(left, Is.Not.Null);
            Assert.That(left.Properties().Select(p => p.Name).OrderBy(n => n), Is.EqualTo(new[] { "friendCode", "room" }));
        }

        [Test, Order(3)]
        public void I_DeclinedVisit()
        {
            var req = Await(cb => _bob.RequestVisit(_alice.FriendCode, cb), "VISIT_REQUEST");
            Assert.That(req.Ok, Is.True, req.Code);
            PumpUntil(() => _ra.VisitIncoming.Count == 1, "VisitIncoming at Alice");
            var decline = Await(cb => _alice.DeclineVisit(_bob.FriendCode, cb), "VISIT_DECLINE");
            Assert.That(decline.Ok, Is.True);
            PumpUntil(() => _rb.VisitDenied.Count == 1, "VisitDenied");
            Assert.That(_rb.VisitDenied[0], Is.EqualTo(_alice.FriendCode));
            Assert.That(_bob.VisitActive, Is.False);
        }
    }

    /// <summary>Real WebSocketTransport against a port nobody listens on (no node needed).</summary>
    [TestFixture]
    [NonParallelizable]
    public class TransportIntegrationTests
    {
        [Test]
        public void UnreachableServerReportsAndSchedulesBackoff()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            var dir = Path.Combine(Path.GetTempPath(), "gooby-itu-" + Guid.NewGuid().ToString("N"));
            var clock = Stopwatch.StartNew();
            using (var http = new HttpClientNetHttp())
            {
                var c = new SocialClient(dir, () => new WebSocketTransport(), () => clock.ElapsedMilliseconds, http);
                c.Configure(NetAddress.Parse("127.0.0.1:" + port), "Nobody", "Gooby", "");
                c.Connect();
                var sw = Stopwatch.StartNew();
                while (c.LastErrorCode != "UNREACHABLE" && sw.ElapsedMilliseconds < 10000)
                {
                    c.Pump();
                    Thread.Sleep(10);
                }
                Assert.That(c.LastErrorCode, Is.EqualTo("UNREACHABLE"), c.Session.LastTransportError);
                Assert.That(c.Status, Is.EqualTo(NetStatus.Offline));
                Assert.That(c.Session.WantsConnection, Is.True);
                Assert.That(c.Session.LastReconnectDelayMs, Is.InRange(1000L, 1300L));

                // REST without a server → status 0 → NO_CONNECTION.
                bool? ok = null;
                string code = null;
                c.FetchHouse("GOOBY-2222", (o, layout, cd) => { ok = o; code = cd; });
                sw.Restart();
                while (!ok.HasValue && sw.ElapsedMilliseconds < 12000)
                {
                    c.Pump();
                    Thread.Sleep(10);
                }
                Assert.That(ok, Is.False);
                Assert.That(code, Is.EqualTo("NO_CONNECTION"));
                c.Disconnect();
            }
            try { Directory.Delete(dir, true); } catch (Exception) { /* best effort */ }
        }
    }

    /// <summary>Scenario i: server with GOOBY_JOIN_SECRET — missing/wrong secret is fatal (no reconnect loop).</summary>
    [TestFixture]
    [NonParallelizable]
    public class JoinSecretIntegrationTests : ServerFixtureBase
    {
        [OneTimeSetUp]
        public void Setup()
        {
            StartServer(new Dictionary<string, string> { ["GOOBY_JOIN_SECRET"] = "geheim" });
        }

        [Test, Order(1)]
        public void MissingSecretIsFatal()
        {
            var c = NewClient("Nosecret", "Gooby");
            var statuses = new List<NetStatus>();
            c.StatusChanged += s => statuses.Add(s);
            c.Connect(); // HELLO 1
            PumpUntil(() => c.LastErrorCode == "SECRET_REQUIRED" && c.Status == NetStatus.Offline, "SECRET_REQUIRED");
            Assert.That(c.Session.WantsConnection, Is.False);
            Assert.That(c.Session.NextReconnectAtMs, Is.EqualTo(-1));
            PumpFor(2500);
            Assert.That(c.Status, Is.EqualTo(NetStatus.Offline));
            Assert.That(c.LastErrorCode, Is.EqualTo("SECRET_REQUIRED"));
            Assert.That(statuses.Count(s => s == NetStatus.Connecting), Is.EqualTo(1), "no reconnect loop");
            Assert.That(c.FriendCode, Is.EqualTo(""));
        }

        [Test, Order(2)]
        public void WrongSecretIsFatalAndRightSecretConnects()
        {
            var wrong = NewClient("Wrong", "Gooby", "falsch");
            wrong.Connect(); // HELLO 2
            PumpUntil(() => wrong.LastErrorCode == "SECRET_WRONG" && wrong.Status == NetStatus.Offline, "SECRET_WRONG");
            Assert.That(wrong.Session.WantsConnection, Is.False);

            var right = NewClient("Right", "Gooby", "geheim");
            right.Connect(); // HELLO 3
            PumpUntil(() => right.IsOnline, "online with the right secret");
            Assert.That(right.FriendCode, Does.StartWith("GOOBY-"));
        }
    }
}
