using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gooby.Core.Minigames.Gvz;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// GvZ online PvP against the real GOOBY-SERVER (gvzmp.js): invite → decline / late accept, invite → accept (the
    /// inviter defends as "gooby", the guest attacks as "zombie"), the start handshake (GVZ_START only after BOTH
    /// GVZ_START_REQ), a whole lockstep match played by two <see cref="GvzGame"/> bots (each only its side, GP_INPUT/GP_HASH
    /// through the server) to the personalised GVZ_RESULT with the coin reward booked exactly once, leaving the consumed
    /// session; ROOM_LEAVE mid-match aborting for the partner and a mismatching GP_HASH stopping both (GVZ_DESYNC).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class GvzIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private GvzNetClient _ga, _gb;
        private readonly List<GvzInvite> _invitesB = new List<GvzInvite>();
        private readonly List<string> _declinedA = new List<string>(), _abortedA = new List<string>();
        private readonly List<int> _desyncA = new List<int>(), _desyncB = new List<int>();
        private int _readyA, _startedA, _startedB;

        [OneTimeSetUp]
        public void Setup()
        {
            NetLog.OnError = m => TestContext.Progress.WriteLine(m);
            Gooby.Tests.Core.Fixtures.InstallContentSource();
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            var sa = new Recorder(_alice);
            var sb = new Recorder(_bob);
            _ga = new GvzNetClient(_alice);
            _gb = new GvzNetClient(_bob);
            _gb.InviteIncoming += i => _invitesB.Add(i);
            _ga.InviteDeclined += by => _declinedA.Add(by);
            _ga.SessionReady += () => _readyA++;
            _ga.GameStarted += () => _startedA++;
            _gb.GameStarted += () => _startedB++;
            _ga.Aborted += (reason, by) => _abortedA.Add(reason + ":" + by);
            _ga.DesyncReported += t => _desyncA.Add(t);
            _gb.DesyncReported += t => _desyncB.Add(t);
            ConnectAll(_alice, _bob);
            MakeFriends(_alice, sa, _bob, sb);
        }

        private sealed class Pair : IDisposable
        {
            public readonly GvzGame A, B;
            public readonly GvzPvpLink LinkA, LinkB;
            public readonly List<string> NoticesA = new List<string>(), NoticesB = new List<string>();
            public string EndHashA = "", EndHashB = "";
            public int EndTickA = -1, EndTickB = -1;
            public bool Bots;
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private double _last;

            public Pair(GvzNetClient ca, GvzNetClient cb, JObject rules)
            {
                A = new GvzGame("normal", 11) { PvpRules = rules };
                B = new GvzGame("normal", 22) { PvpRules = rules };
                LinkA = new GvzPvpLink(A, ca);
                LinkB = new GvzPvpLink(B, cb);
                LinkA.Notice += k => NoticesA.Add(k);
                LinkB.Notice += k => NoticesB.Add(k);
                A.PhaseChanged += p => { if (Ended(p)) { EndHashA = A.Net.StateHashText(); EndTickA = A.Run.Tick; } };
                B.PhaseChanged += p => { if (Ended(p)) { EndHashB = B.Net.StateHashText(); EndTickB = B.Run.Tick; } };
            }

            private static bool Ended(string p) => p == GvzGame.PhaseWon || p == GvzGame.PhaseLost;

            public void Tick()
            {
                var now = _clock.Elapsed.TotalSeconds;
                // a little faster than real time: the lockstep keeps both sims together however fast the wall clock runs
                var dt = Math.Min(0.1, (now - _last) * 3.0);
                _last = now;
                if (dt <= 0.0) return;
                if (Bots) { A.ApplyBot(); B.ApplyBot(); }
                A.Tick(dt);
                B.Tick(dt);
            }

            public void Dispose()
            {
                LinkA.Dispose();
                LinkB.Dispose();
            }
        }

        private static JObject Rules(int surviveSec, int mud)
        {
            var rules = GvzData.LoadPvp();
            rules["match"]["gooby_survive_sec"] = surviveSec;
            rules["matsch"]["start"] = mud;
            return rules;
        }

        private void InviteAndAccept()
        {
            var invites = _invitesB.Count;
            var invite = Await(cb => _ga.Invite(_bob.FriendCode, cb), "GVZ_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            PumpUntil(() => _invitesB.Count == invites + 1, "GVZ_INVITED at Bob");
            var ready = _readyA;
            var accept = Await(cb => _gb.Accept(_alice.FriendCode, cb), "GVZ_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            Assert.That(accept.Type, Is.EqualTo("GVZ_READY"));
            PumpUntil(() => _readyA == ready + 1 && _ga.Joined && _gb.Joined, "GVZ_READY + ROOM_JOIN on both");
        }

        [Test, Order(1)]
        public void A_DeclinedChallengeReachesTheInviterAndALateAcceptFails()
        {
            var invite = Await(cb => _ga.Invite(_bob.FriendCode, cb), "GVZ_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            PumpUntil(() => _invitesB.Count == 1, "GVZ_INVITED at Bob");
            Assert.That(_invitesB[0].From, Is.EqualTo(_alice.FriendCode));
            Assert.That(_invitesB[0].GoobyName, Is.EqualTo("Flausch"));
            _gb.Decline(_alice.FriendCode);
            PumpUntil(() => _declinedA.Count == 1, "GVZ_DECLINED at Alice");
            Assert.That(_ga.OutgoingInvite, Is.EqualTo(""));
            var late = Await(cb => _gb.Accept(_alice.FriendCode, cb), "late GVZ_ACCEPT");
            Assert.That(late.Code, Is.EqualTo("NOT_FOUND"));
            var start = Await(cb => _ga.RequestStart(cb), "GVZ_START_REQ unpaired");
            Assert.That(start.Code, Is.EqualTo("NOT_IN_ROOM"));
        }

        [Test, Order(2)]
        public void B_ChallengeAcceptStartHandshakeAndAWholeMatchWithTheReward()
        {
            InviteAndAccept();
            Assert.That(_ga.RoomId, Does.StartWith("gvz:"));
            Assert.That(_ga.MySide, Is.EqualTo(GvzNetClient.SideGooby), "the inviter defends");
            Assert.That(_gb.MySide, Is.EqualTo(GvzNetClient.SideZombie), "the guest attacks");
            Assert.That(_gb.PartnerGoobyName, Is.EqualTo("Flausch"));
            using (var pair = new Pair(_ga, _gb, Rules(15, 120)))
            {
                pair.LinkA.RequestStart();
                PumpUntil(() => _ga.StartRequested, "Alice's start request");
                PumpFor(200);
                Assert.That(_startedA + _startedB, Is.EqualTo(0), "one confirmation never starts");
                // Bob's select bot confirms for him
                pair.Bots = true;
                PumpUntil(() => { pair.Tick(); return pair.A.OnlineRun && pair.B.OnlineRun; }, "GVZ_START on both");
                Assert.That(_gb.Seed, Is.EqualTo(_ga.Seed));
                Assert.That(pair.NoticesB, Does.Contain("gvz.netz.start_banner"));
                Assert.That(pair.B.NetZombie, Is.True);

                PumpUntil(() => { pair.Tick(); return pair.LinkA.LastResult != null && pair.LinkB.LastResult != null && pair.EndTickA >= 0 && pair.EndTickB >= 0; },
                    "both matches end and GVZ_RESULT reaches both", 60000);
                Assert.That(pair.EndTickB, Is.EqualTo(pair.EndTickA), "both lockstep sims end on the same tick");
                Assert.That(pair.EndHashB, Is.EqualTo(pair.EndHashA), "identical final match state through the server");
                var ra = pair.LinkA.LastResult;
                var rb = pair.LinkB.LastResult;
                Assert.That(rb.MatchId, Is.EqualTo(ra.MatchId));
                Assert.That(ra.Winner, Is.EqualTo(rb.Winner));
                Assert.That(ra.Winner, Is.EqualTo(pair.A.Net == null ? ra.Winner : pair.A.Net.Winner));
                Assert.That(ra.Won, Is.Not.EqualTo(rb.Won), "the result is personalised: one winner");
                Assert.That(ra.Side, Is.EqualTo(GvzNetClient.SideGooby));
                Assert.That(rb.Side, Is.EqualTo(GvzNetClient.SideZombie));
                Assert.That(ra.Coins, Is.EqualTo(ra.Won ? 30 : 10));
                Assert.That(rb.Coins, Is.EqualTo(rb.Won ? 30 : 10));
                Assert.That(ra.RewardId, Is.EqualTo("gvz-" + ra.MatchId + "-" + _alice.FriendCode));
                Assert.That(pair.A.LastWon, Is.EqualTo(ra.Won));
                Assert.That((int)pair.A.Save["economy"]["coins"], Is.EqualTo(ra.Coins), "the reward booked (fresh session save)");
                Assert.That(pair.A.BookNetResult(ra.RewardId, ra.Won, ra.Coins), Is.False, "booked exactly once");
                Assert.That(GvzProgress.NetWins(pair.A.Save) + GvzProgress.NetLosses(pair.A.Save), Is.EqualTo(1));
                PumpUntil(() => !_ga.IsPaired && !_gb.IsPaired, "both left the consumed session");
                Assert.That(_desyncA.Count + _desyncB.Count, Is.EqualTo(0));
                Assert.That(_abortedA, Is.Empty, "leaving after the result aborts nothing");
            }
        }

        [Test, Order(3)]
        public void C_LeavingMidMatchEndsItForThePartner()
        {
            InviteAndAccept();
            using (var pair = new Pair(_ga, _gb, Rules(60, 0)))
            {
                pair.LinkA.RequestStart();
                pair.LinkB.RequestStart();
                PumpUntil(() => { pair.Tick(); return pair.A.OnlineRun && pair.B.OnlineRun; }, "GVZ_START on both");
                PumpUntil(() => { pair.Tick(); return pair.A.Run != null && pair.A.Run.Tick > 30; }, "the match runs");
                var aborted = _abortedA.Count;
                pair.LinkB.Leave();
                Assert.That(pair.B.Phase, Is.EqualTo(GvzGame.PhaseSelect));
                PumpUntil(() => { pair.Tick(); return _abortedA.Count == aborted + 1; }, "GVZ_ABORTED at Alice");
                Assert.That(_abortedA[aborted], Is.EqualTo("left:" + _bob.FriendCode));
                Assert.That(pair.A.Phase, Is.EqualTo(GvzGame.PhaseSelect), "the partner's match ends politely");
                Assert.That(pair.NoticesA, Does.Contain("gvz.netz.abbruch"));
                Assert.That(_ga.IsPaired || _gb.IsPaired, Is.False);
            }
        }

        [Test, Order(4)]
        public void D_AMismatchingHashMakesTheServerStopBothMatches()
        {
            InviteAndAccept();
            using (var pair = new Pair(_ga, _gb, Rules(60, 0)))
            {
                pair.LinkA.RequestStart();
                pair.LinkB.RequestStart();
                PumpUntil(() => { pair.Tick(); return pair.A.OnlineRun && pair.B.OnlineRun; }, "GVZ_START on both");
                Assert.That(_ga.SendHash(7, "aaaaaaaaaaaaaaaa"), Is.True);
                Assert.That(_gb.SendHash(7, "bbbbbbbbbbbbbbbb"), Is.True);
                PumpUntil(() => { pair.Tick(); return pair.NoticesA.Contains("gvz.netz.desync") && pair.NoticesB.Contains("gvz.netz.desync"); }, "GVZ_DESYNC on both");
                Assert.That(_desyncA[_desyncA.Count - 1], Is.EqualTo(7));
                Assert.That(pair.A.Phase, Is.EqualTo(GvzGame.PhaseSelect));
                Assert.That(pair.B.Phase, Is.EqualTo(GvzGame.PhaseSelect));
                PumpUntil(() => !_ga.IsPaired && !_gb.IsPaired, "both left the broken session");
            }
        }
    }
}
