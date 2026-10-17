using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Ranch visit and trail ride against the real GOOBY-SERVER (ranchmp.js besuch/ausritt): Alice is friends with Bob and
    /// Cara. A trail ride needs no lobby — both ride from RANCH_READY on, poses and reactions (HERZ, ENTDECKUNG) reach the
    /// other side, Alice brings Cara into the running ride, Bob leaves and the ride goes on. A visit: Alice uploads her ranch
    /// metadata, invites Bob, RANCH_READY carries its revision, Bob reads her horses, a gesture reaches Alice, and when Bob
    /// goes the visit ends for Alice too.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class RanchFreiIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob, _cara;
        private RanchMpClient _ra, _rb, _rc;
        private readonly List<RmpInvite> _invitesB = new List<RmpInvite>(), _invitesC = new List<RmpInvite>();
        private readonly List<(string kind, string from, JObject body)> _reactB = new List<(string, string, JObject)>(), _reactA = new List<(string, string, JObject)>();
        private readonly List<string> _joinedA = new List<string>(), _leftA = new List<string>(), _endedA = new List<string>();

        [OneTimeSetUp]
        public void Setup()
        {
            NetLog.OnError = m => TestContext.Progress.WriteLine(m);
            Gooby.Tests.Core.Fixtures.InstallContentSource();
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            _cara = NewClient("Cara", "Krümel");
            var sa = new Recorder(_alice);
            var sb = new Recorder(_bob);
            var sc = new Recorder(_cara);
            _ra = new RanchMpClient(_alice);
            _rb = new RanchMpClient(_bob);
            _rc = new RanchMpClient(_cara);
            _rb.InviteIncoming += i => _invitesB.Add(i);
            _rc.InviteIncoming += i => _invitesC.Add(i);
            _rb.ReactionReceived += (k, f, b) => _reactB.Add((k, f, b));
            _ra.ReactionReceived += (k, f, b) => _reactA.Add((k, f, b));
            _ra.PeerJoined += (c, n) => _joinedA.Add(c);
            _ra.PeerLeft += (c, n) => _leftA.Add(c);
            _ra.Ended += by => _endedA.Add(by);
            ConnectAll(_alice, _bob, _cara);
            MakeFriends(_alice, sa, _bob, sb);
            MakeFriends(_alice, sa, _cara, sc);
        }

        private void Accept(RanchMpClient who, string from, string label)
        {
            var accept = Await(cb => who.Accept(from, cb), label);
            Assert.That(accept.Ok, Is.True, accept.Code);
            Assert.That(accept.Type, Is.EqualTo("RANCH_READY"));
        }

        [Test, Order(1)]
        public void A_ATrailRideRidesWithoutLobbyAndTakesNewRiders()
        {
            var invite = Await(cb => _ra.Invite(_bob.FriendCode, RmpKurse.Ausritt, "", cb), "RANCH_INVITE ausritt");
            Assert.That(invite.Ok, Is.True, invite.Code);
            PumpUntil(() => _invitesB.Count == 1, "RANCH_INVITED at Bob");
            Assert.That((_invitesB[0].Mode, _invitesB[0].Kurs), Is.EqualTo((RmpKurse.Ausritt, "")), "free rides have no course");
            Accept(_rb, _alice.FriendCode, "RANCH_ACCEPT Bob");
            PumpUntil(() => _ra.Joined && _rb.Joined, "both in the mg: room");
            Assert.That((_ra.Phase, _rb.Phase), Is.EqualTo((RanchMpClient.PhaseFrei, RanchMpClient.PhaseFrei)), "no lobby");
            Assert.That(_ra.IsFree && _ra.Running, Is.True, "riding from the start");

            var clock = Stopwatch.StartNew();
            using (var a = new RmpLink(_ra, () => clock.ElapsedMilliseconds) { Bot = true })
            using (var b = new RmpLink(_rb, () => clock.ElapsedMilliseconds))
            {
                var found = new List<string>();
                a.Entdeckt += found.Add;
                Assert.That(a.Lauf, Is.Not.Null, "the free ride is built at once");
                Assert.That(a.Lauf.IsFree, Is.True);
                double last = 0;
                void Tick()
                {
                    var now = clock.Elapsed.TotalSeconds;
                    var dt = Math.Min(0.1, now - last);
                    last = now;
                    if (dt <= 0) return;
                    a.Tick(dt);
                    b.Tick(dt);
                }
                PumpUntil(() => { Tick(); return b.Lauf.Remotes.ContainsKey(_alice.FriendCode) && a.Lauf.Remotes.ContainsKey(_bob.FriendCode); }, "poses both ways", 5000);
                PumpUntil(() => { Tick(); return found.Count >= 1; }, "Alice's bot discovers a landmark", 60000);
                PumpUntil(() => { Tick(); return _reactB.Any(r => r.kind == "ENTDECKUNG"); }, "ENTDECKUNG reaches Bob", 5000);
                var discovery = _reactB.First(r => r.kind == "ENTDECKUNG");
                Assert.That(discovery.from, Is.EqualTo(_alice.FriendCode));
                Assert.That((string)discovery.body["zone"], Is.EqualTo(found[0]));
                var pose = b.Lauf.PeerAt(_alice.FriendCode, clock.ElapsedMilliseconds);
                Assert.That(pose, Is.Not.Null);
                Assert.That((pose.Value.Pos - RmpFrei.StartPosition(0, 2)).Length, Is.GreaterThan(10.0), "Bob sees Alice ride away from the yard");

                Assert.That(_ra.SendReaction("HERZ"), Is.True);
                Assert.That(_ra.SendReaction("SCHUBS"), Is.False, "only the known reactions");
                PumpUntil(() => { Tick(); return _reactB.Any(r => r.kind == "HERZ"); }, "HERZ reaches Bob", 5000);

                // Alice brings Cara into the running ride (a trail ride takes riders at any time, up to four); the server
                // tells Alice and Bob with a second RANCH_READY for the same room — their rides must go on unchanged
                var laufA = a.Lauf;
                var laufB = b.Lauf;
                var readyAgain = 0;
                void CountReady() => readyAgain++;
                _ra.SessionReady += CountReady;
                _rb.SessionReady += CountReady;
                var inviteC = Await(cb => _ra.Invite(_cara.FriendCode, RmpKurse.Ausritt, "", cb), "RANCH_INVITE Cara");
                Assert.That(inviteC.Ok, Is.True, inviteC.Code);
                PumpUntil(() => _invitesC.Count == 1, "RANCH_INVITED at Cara");
                Accept(_rc, _alice.FriendCode, "RANCH_ACCEPT Cara");
                PumpUntil(() => { Tick(); return _rc.Joined && _joinedA.Contains(_cara.FriendCode) && _rb.Players.Count == 3; }, "Cara joins the same ride", 5000);
                _ra.SessionReady -= CountReady;
                _rb.SessionReady -= CountReady;
                Assert.That(_rc.RoomId, Is.EqualTo(_ra.RoomId));
                Assert.That(_ra.Players.Select(p => p.Code), Has.Member(_cara.FriendCode));
                Assert.That(readyAgain, Is.EqualTo(0), "no second SessionReady for the riders already out");
                Assert.That(a.Lauf, Is.SameAs(laufA), "Alice rides on — no restart at the yard");
                Assert.That(b.Lauf, Is.SameAs(laufB), "Bob rides on");
                Assert.That((_ra.Joined, _rb.Joined, _ra.Phase), Is.EqualTo((true, true, RanchMpClient.PhaseFrei)));
                Assert.That((a.Lauf.Controller.Pos - RmpFrei.StartPosition(0, 2)).Length, Is.GreaterThan(10.0), "Alice is still out on the meadow");

                using (var c = new RmpLink(_rc, () => clock.ElapsedMilliseconds))
                {
                    Assert.That(c.Lauf, Is.Not.Null, "Cara rides from her RANCH_READY on");
                    PumpUntil(() => { Tick(); c.Tick(0.05); return a.Lauf.Remotes.ContainsKey(_cara.FriendCode) && c.Lauf.Remotes.ContainsKey(_alice.FriendCode) && c.Lauf.Remotes.ContainsKey(_bob.FriendCode); }, "Cara's ghost at Alice, Alice and Bob at Cara", 5000);

                    // Bob leaves; the ride goes on for Alice and Cara, his ghost is gone
                    b.Leave();
                    PumpUntil(() => { Tick(); c.Tick(0.05); return _leftA.Contains(_bob.FriendCode); }, "Alice sees Bob leave", 5000);
                    Assert.That(_ra.IsActive, Is.True, "the ride goes on");
                    Assert.That(_ra.Players.Select(p => p.Code), Has.No.Member(_bob.FriendCode));
                    Assert.That(a.Lauf.Remotes.Keys, Has.No.Member(_bob.FriendCode), "Bob's horse leaves Alice's meadow");
                    Assert.That(a.Lauf.Remotes.Keys, Has.Member(_cara.FriendCode));
                    a.Leave();
                    c.Leave();
                }
                PumpFor(200);
            }
        }

        [Test, Order(2)]
        public void B_AVisitShowsTheHostsRanchAndEndsWhenOneSideGoes()
        {
            var state = JObject.Parse(@"{""ranch"":{""wirtschaft"":{""ausbau"":{""boxen"":2,""weidezaun"":true}},""tiere"":{""pferde"":{""p1"":{""name"":""Sternchen"",""rasse"":""fjordi"",""level"":7,""farbe"":""palomino""}}},""comp"":{""trophaeen"":[""gold_springen""],""schleifen"":{""springen"":1}}}}");
            bool? uploaded = null;
            var rev = 0;
            _alice.UploadRanchMeta(RmpRanchMeta.Build(state, "Alice", "Flausch"), (ok, r, code) => { uploaded = ok; rev = r; });
            PumpUntil(() => uploaded != null, "PUT /api/ranch");
            Assert.That(uploaded, Is.True);
            Assert.That(rev, Is.GreaterThanOrEqualTo(1));

            var invites = _invitesB.Count;
            var invite = Await(cb => _ra.Invite(_bob.FriendCode, RmpKurse.Besuch, "", cb), "RANCH_INVITE besuch");
            Assert.That(invite.Ok, Is.True, invite.Code);
            PumpUntil(() => _invitesB.Count == invites + 1, "the visit invite at Bob");
            Accept(_rb, _alice.FriendCode, "RANCH_ACCEPT visit");
            PumpUntil(() => _ra.Joined && _rb.Joined, "both on Alice's ranch");
            Assert.That((_rb.Mode, _rb.Host, _rb.Phase), Is.EqualTo((RmpKurse.Besuch, _alice.FriendCode, RanchMpClient.PhaseFrei)));
            Assert.That(_rb.HostMetaRev, Is.EqualTo(rev), "RANCH_READY names the metadata revision");

            JObject meta = null;
            var code = "?";
            _bob.FetchRanchMeta(_rb.Host, (m, c) => { meta = m; code = c; });
            PumpUntil(() => code != "?", "GET /api/ranch/:host");
            Assert.That(meta, Is.Not.Null, code);
            Assert.That((string)meta["name"], Is.EqualTo("Alice"));
            Assert.That(((int)meta["ausbau"]["boxen"], (bool)meta["ausbau"]["weidezaun"]), Is.EqualTo((2, true)));
            Assert.That((string)meta["pferde"][0]["name"], Is.EqualTo("Sternchen"));
            Assert.That((int)meta["schleifen"], Is.EqualTo(1));

            JObject strangerMeta = null;
            var strangerCode = "?";
            _cara.FetchRanchMeta(_bob.FriendCode, (m, c) => { strangerMeta = m; strangerCode = c; });
            PumpUntil(() => strangerCode != "?", "GET /api/ranch of a stranger");
            Assert.That((strangerMeta, strangerCode), Is.EqualTo(((JObject)null, "NOT_FRIENDS")));

            Assert.That(_rb.SendReaction("GESTE", new JObject { ["id"] = "streicheln", ["pferd"] = "Sternchen" }), Is.True);
            PumpUntil(() => _reactA.Any(r => r.kind == "GESTE"), "the gesture reaches the host");
            Assert.That((string)_reactA.Last(r => r.kind == "GESTE").body["id"], Is.EqualTo("streicheln"));

            var ended = _endedA.Count;
            _rb.Leave();
            PumpUntil(() => _endedA.Count == ended + 1, "RANCH_ENDED at the host when the guest goes");
            Assert.That(_endedA.Last(), Is.EqualTo(_bob.FriendCode));
            Assert.That(_ra.IsActive, Is.False);
        }

        [Test, Order(3)]
        public void C_RidersSeeEachOthersOwnHorses()
        {
            var lookA = JObject.Parse(@"{""name"":""Karamell"",""farbe"":""palomino"",""look"":{""flausch"":true},""gear"":{""sattel"":""rot"",""decke"":null,""halfter"":""gold""}}");
            var lookB = JObject.Parse(@"{""name"":""Wolke"",""farbe"":""weiss"",""farbeHex"":""#E8E0D4"",""maehneHex"":""#B9A88E"",""look"":{""fell"":""nebel""},""gear"":{""decke"":""schleife""}}");
            var lookC = JObject.Parse(@"{""name"":""Beere"",""farbe"":""fuchs"",""gear"":{""sattel"":""blau""}}");
            _ra.OwnHorse = () => lookA;
            _rb.OwnHorse = () => lookB;
            _rc.OwnHorse = () => lookC;
            var changedA = new List<string>();
            void OnHorse(string code) => changedA.Add(code);
            _ra.PeerHorseChanged += OnHorse;
            try
            {
                var invites = _invitesB.Count;
                var invite = Await(cb => _ra.Invite(_bob.FriendCode, RmpKurse.Ausritt, "", cb), "RANCH_INVITE ausritt (horses)");
                Assert.That(invite.Ok, Is.True, invite.Code);
                PumpUntil(() => _invitesB.Count == invites + 1, "the invite at Bob");
                Accept(_rb, _alice.FriendCode, "RANCH_ACCEPT Bob (horses)");
                PumpUntil(() => _ra.PeerHorses.ContainsKey(_bob.FriendCode) && _rb.PeerHorses.ContainsKey(_alice.FriendCode), "both horses cross", 5000);
                var karamell = _rb.PeerHorses[_alice.FriendCode];
                Assert.That((string)karamell["name"], Is.EqualTo("Karamell"));
                Assert.That((RmpPferdLook.Gear(karamell, "sattel"), RmpPferdLook.Gear(karamell, "halfter")), Is.EqualTo(("rot", "gold")));
                Assert.That(RanchBelohnungen.TraegtLook(karamell, RanchBelohnungen.Flausch), Is.True);
                var wolke = _ra.PeerHorses[_bob.FriendCode];
                Assert.That(((string)wolke["farbeHex"], RanchBelohnungen.TraegtLook(wolke, RanchBelohnungen.Nebel), RmpPferdLook.Gear(wolke, "decke")), Is.EqualTo(("#E8E0D4", true, "schleife")));
                Assert.That(changedA, Has.Member(_bob.FriendCode));

                // Cara comes later: the riders already out send their horses again for her
                var invitesC = _invitesC.Count;
                var inviteC = Await(cb => _ra.Invite(_cara.FriendCode, RmpKurse.Ausritt, "", cb), "RANCH_INVITE Cara (horses)");
                Assert.That(inviteC.Ok, Is.True, inviteC.Code);
                PumpUntil(() => _invitesC.Count == invitesC + 1, "the invite at Cara");
                Accept(_rc, _alice.FriendCode, "RANCH_ACCEPT Cara (horses)");
                PumpUntil(() => _rc.PeerHorses.ContainsKey(_alice.FriendCode) && _rc.PeerHorses.ContainsKey(_bob.FriendCode)
                    && _ra.PeerHorses.ContainsKey(_cara.FriendCode) && _rb.PeerHorses.ContainsKey(_cara.FriendCode), "Cara sees both horses, both see hers", 5000);
                Assert.That((string)_rc.PeerHorses[_bob.FriendCode]["name"], Is.EqualTo("Wolke"));
                Assert.That(RmpPferdLook.Gear(_ra.PeerHorses[_cara.FriendCode], "sattel"), Is.EqualTo("blau"));

                // a forged body keeps only what a horse can be
                _bob.Session.Send("ROOM_MSG", new JObject
                {
                    ["room"] = _rb.RoomId, ["kind"] = RmpPferdLook.Kind,
                    ["body"] = JObject.Parse(@"{""name"":""Wolke"",""farbe"":""<script>"",""gear"":{""sattel"":""diamant""},""coins"":500}"),
                });
                PumpUntil(() => (string)_ra.PeerHorses[_bob.FriendCode]["farbe"] == "braun", "the forged look arrives cleaned", 5000);
                Assert.That(RmpPferdLook.Gear(_ra.PeerHorses[_bob.FriendCode], "sattel"), Is.Null);
                Assert.That(_ra.PeerHorses[_bob.FriendCode]["coins"], Is.Null);

                // Bob rides home: his horse leaves the table
                _rb.Leave();
                PumpUntil(() => !_ra.PeerHorses.ContainsKey(_bob.FriendCode), "Bob's horse is gone at Alice", 5000);
                Assert.That(_ra.PeerHorses.Keys, Has.Member(_cara.FriendCode));
                _ra.Leave();
                _rc.Leave();
                Assert.That(_ra.PeerHorses, Is.Empty, "leaving clears the table");
                PumpFor(200);
            }
            finally
            {
                _ra.PeerHorseChanged -= OnHorse;
                _ra.OwnHorse = _rb.OwnHorse = _rc.OwnHorse = null;
            }
        }
    }
}
