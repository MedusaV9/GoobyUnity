using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gooby.Core.Minigames.Gobnom;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>Records every <see cref="GobnomNetClient"/> event for assertions.</summary>
    public sealed class GobnomRecorder
    {
        public readonly List<GobnomInvite> Invites = new List<GobnomInvite>();
        public readonly List<string> Declined = new List<string>();
        public readonly List<string> Aborted = new List<string>();
        public readonly List<int> Desyncs = new List<int>();
        public readonly List<GobnomNetResult> Results = new List<GobnomNetResult>();
        public readonly List<bool> PeerDown = new List<bool>();
        public int Ready, Started, VoteUpdates, Snapshots;

        public GobnomRecorder(GobnomNetClient c)
        {
            c.InviteIncoming += i => Invites.Add(i);
            c.InviteDeclined += by => Declined.Add(by);
            c.SessionReady += () => Ready++;
            c.VotesChanged += () => VoteUpdates++;
            c.GameStarted += () => Started++;
            c.Aborted += (reason, by) => Aborted.Add(reason + ":" + by);
            c.DesyncReported += t => Desyncs.Add(t);
            c.ResultConfirmed += r => Results.Add(r);
            c.PeerConnectionChanged += (down, ms) => PeerDown.Add(down);
            c.SnapshotReceived += d => Snapshots++;
        }
    }

    /// <summary>
    /// GOB NOM online coop against the real GOOBY-SERVER (gobnommp.js): invite → decline/late accept, invite → accept
    /// (both sit down in the gobnom: room as sides a/b), the level vote (GOBNOM_LEVEL_STATE, GOBNOM_START only when both
    /// chose the same coop level), a whole lockstep round played by two <see cref="GobnomGame"/>s whose bots only plan
    /// their own half — GN_INPUT/GN_HASH through the server — to the idempotent GOBNOM_RESULT on both, leaving the
    /// consumed session; ROOM_LEAVE mid-run aborting for the partner, a mismatching GN_HASH stopping both rounds
    /// (GOBNOM_DESYNC), and a connection drop mid-run healed by the rejoin snapshot (PEER_DOWN/UP + replay).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class GobnomIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private GobnomNetClient _ga, _gb;
        private GobnomRecorder _ra, _rb;

        [OneTimeSetUp]
        public void Setup()
        {
            // a handler that throws inside a push is swallowed by NetLog.Guard: make it visible in the test output
            NetLog.OnError = m => TestContext.Progress.WriteLine(m);
            Gooby.Tests.Core.Fixtures.InstallContentSource();
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            var sa = new Recorder(_alice);
            var sb = new Recorder(_bob);
            _ga = new GobnomNetClient(_alice);
            _gb = new GobnomNetClient(_bob);
            _ra = new GobnomRecorder(_ga);
            _rb = new GobnomRecorder(_gb);
            ConnectAll(_alice, _bob);
            MakeFriends(_alice, sa, _bob, sb);
        }

        private sealed class Pair : IDisposable
        {
            public GobnomGame A, B;
            public GobnomCoopLink LinkA, LinkB;
            public readonly List<string> NoticesA = new List<string>(), NoticesB = new List<string>();
            public string EndHashA = "", EndHashB = "";
            public int EndTickA = -1, EndTickB = -1;
            public bool Bots;
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private double _last;

            public Pair(GobnomNetClient ca, GobnomNetClient cb, long seedA, long seedB)
            {
                A = new GobnomGame(seedA);
                B = new GobnomGame(seedB);
                LinkA = new GobnomCoopLink(A, ca);
                LinkB = new GobnomCoopLink(B, cb);
                LinkA.Notice += k => NoticesA.Add(k);
                LinkB.Notice += k => NoticesB.Add(k);
                A.PhaseChanged += p => { if (Ended(p)) { EndHashA = GobnomLogic.StateHash(A.Run); EndTickA = A.Run.Tick; } };
                B.PhaseChanged += p => { if (Ended(p)) { EndHashB = GobnomLogic.StateHash(B.Run); EndTickB = B.Run.Tick; } };
            }

            private static bool Ended(string p) => p == GobnomGame.PhaseWon || p == GobnomGame.PhaseLost;

            /// <summary>Real time drives both games (the condition lambda of PumpUntil ticks them between client pumps).</summary>
            public void Tick()
            {
                var now = _clock.Elapsed.TotalSeconds;
                var dt = Math.Min(0.1, now - _last);
                _last = now;
                if (dt <= 0.0) return;
                if (Bots) { A.ApplyBot(); B.ApplyBot(); }
                A.Tick(dt); LinkA.Tick(dt);
                B.Tick(dt); LinkB.Tick(dt);
            }

            public void Dispose()
            {
                LinkA.Dispose();
                LinkB.Dispose();
            }
        }

        private void Pair_InviteAndAccept()
        {
            var invites = _rb.Invites.Count;
            var invite = Await(cb => _ga.Invite(_bob.FriendCode, cb), "GOBNOM_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            PumpUntil(() => _rb.Invites.Count == invites + 1, "GOBNOM_INVITED at Bob");
            var readyA = _ra.Ready;
            var accept = Await(cb => _gb.Accept(_alice.FriendCode, cb), "GOBNOM_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            Assert.That(accept.Type, Is.EqualTo("GOBNOM_READY"));
            PumpUntil(() => _ra.Ready == readyA + 1 && _ga.Joined && _gb.Joined, "GOBNOM_READY + ROOM_JOIN on both");
        }

        [Test, Order(1)]
        public void A_DeclinedInviteReachesTheInviterAndALateAcceptFails()
        {
            var invite = Await(cb => _ga.Invite(_bob.FriendCode, cb), "GOBNOM_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            Assert.That(_ga.OutgoingInvite, Is.EqualTo(_bob.FriendCode));
            PumpUntil(() => _rb.Invites.Count == 1, "GOBNOM_INVITED at Bob");
            Assert.That(_rb.Invites[0].From, Is.EqualTo(_alice.FriendCode));
            Assert.That(_rb.Invites[0].Name, Is.EqualTo("Alice"));
            Assert.That(_rb.Invites[0].GoobyName, Is.EqualTo("Flausch"));
            Assert.That(_rb.Invites[0].ExpiresInMs, Is.EqualTo(GobnomNetClient.InviteTtlMs));
            Assert.That(_gb.IncomingInvites.Count, Is.EqualTo(1));

            _gb.Decline(_alice.FriendCode);
            PumpUntil(() => _ra.Declined.Count == 1, "GOBNOM_DECLINED at Alice");
            Assert.That(_ra.Declined[0], Is.EqualTo(_bob.FriendCode));
            Assert.That(_ga.OutgoingInvite, Is.EqualTo(""));
            Assert.That(_gb.IncomingInvites, Is.Empty);
            var late = Await(cb => _gb.Accept(_alice.FriendCode, cb), "late GOBNOM_ACCEPT");
            Assert.That(late.Ok, Is.False);
            Assert.That(late.Code, Is.EqualTo("NOT_FOUND"));
            Assert.That(_gb.IsPaired, Is.False);

            var self = Await(cb => _ga.Invite(_alice.FriendCode, cb), "GOBNOM_INVITE self");
            Assert.That(self.Code, Is.EqualTo("SELF"));
            var vote = Await(cb => _ga.ChooseLevel(1, cb), "GOBNOM_LEVEL unpaired");
            Assert.That(vote.Code, Is.EqualTo("NOT_IN_ROOM"));
        }

        [Test, Order(2)]
        public void B_InviteAcceptVoteAndWinACoopLevelTogether()
        {
            Pair_InviteAndAccept();
            Assert.That(_ga.RoomId, Does.StartWith("gobnom:"));
            Assert.That(_gb.RoomId, Is.EqualTo(_ga.RoomId));
            Assert.That(_ga.MySide, Is.EqualTo("a"), "the inviter plays a");
            Assert.That(_gb.MySide, Is.EqualTo("b"));
            Assert.That(_ga.PartnerCode, Is.EqualTo(_bob.FriendCode));
            Assert.That(_gb.PartnerGoobyName, Is.EqualTo("Flausch"));
            Assert.That(_ga.InputDelay, Is.EqualTo(GobnomLockstep.InputDelayDefault));
            Assert.That(_ga.HashEveryTicks, Is.EqualTo(GobnomLockstep.HashTicksDefault));

            using (var pair = new Pair(_ga, _gb, 11, 22))
            {
                // different votes: no start
                pair.LinkA.ChooseLevel(2);
                PumpUntil(() => _gb.PartnerVote == 2, "GOBNOM_LEVEL_STATE at Bob");
                pair.LinkB.ChooseLevel(1);
                PumpUntil(() => _ga.PartnerVote == 1 && _gb.MyVote == 1, "Bob's vote at Alice");
                PumpFor(150);
                Assert.That(_ra.Started + _rb.Started, Is.EqualTo(0), "two different levels never start");
                // the guest's select bot follows the inviter's vote → both on CN2 → GOBNOM_START with the server seed
                pair.Bots = true;
                PumpUntil(() => { pair.Tick(); return pair.A.OnlineRun && pair.B.OnlineRun; }, "GOBNOM_START on both");
                Assert.That(_ga.Level, Is.EqualTo(2));
                Assert.That(_gb.Level, Is.EqualTo(2));
                Assert.That(_gb.Seed, Is.EqualTo(_ga.Seed));
                Assert.That(pair.NoticesA, Does.Contain("gobnom.netz.start_banner"));
                Assert.That(pair.A.Net.Side, Is.EqualTo("a"));
                Assert.That(pair.B.Net.Side, Is.EqualTo("b"));

                PumpUntil(() => { pair.Tick(); return pair.LinkA.LastResult != null && pair.LinkB.LastResult != null && pair.EndTickA >= 0 && pair.EndTickB >= 0; },
                    "both sims end and GOBNOM_RESULT reaches both", 40000);
                Assert.That(pair.A.LastWon && pair.B.LastWon, Is.True, "won on both devices");
                Assert.That(pair.EndTickB, Is.EqualTo(pair.EndTickA), "both lockstep sims end on the same tick");
                Assert.That(pair.EndHashB, Is.EqualTo(pair.EndHashA), "identical final state on both devices");
                var level = GobnomData.LevelById(GobnomData.LoadCoop(), 2);
                var solver = GobnomSolver.RunSolution(level, GobnomData.LoadBalance(), _ga.Seed);
                Assert.That(pair.EndTickA, Is.EqualTo(solver.Ticks), "every planned action ran on its plan tick through the server");
                var ra = pair.LinkA.LastResult;
                var rb = pair.LinkB.LastResult;
                Assert.That(ra.Outcome, Is.EqualTo("won"));
                Assert.That(ra.Level, Is.EqualTo(2));
                Assert.That(ra.Jars, Is.EqualTo(solver.Jars));
                Assert.That(ra.Stars, Is.EqualTo(solver.Jars));
                Assert.That(rb.MatchId, Is.EqualTo(ra.MatchId), "one match");
                Assert.That(ra.RewardId, Is.EqualTo("gnom-" + ra.MatchId + "-" + _alice.FriendCode));
                Assert.That(rb.RewardId, Is.EqualTo("gnom-" + ra.MatchId + "-" + _bob.FriendCode));
                Assert.That(GobnomProgress.IsCleared(pair.A.Save, "coop", 2) && GobnomProgress.IsCleared(pair.B.Save, "coop", 2), Is.True, "booked on both devices");
                PumpUntil(() => !_ga.IsPaired && !_gb.IsPaired, "both left the consumed session");
                Assert.That(_ra.Desyncs.Count + _rb.Desyncs.Count, Is.EqualTo(0));
                Assert.That(_ra.Aborted.Count + _rb.Aborted.Count, Is.EqualTo(0), "leaving after the result aborts nothing");
            }
        }

        [Test, Order(3)]
        public void C_LeavingMidRunEndsTheRoundForThePartner()
        {
            Pair_InviteAndAccept();
            using (var pair = new Pair(_ga, _gb, 33, 44))
            {
                pair.LinkA.ChooseLevel(2);
                pair.LinkB.ChooseLevel(2);
                PumpUntil(() => { pair.Tick(); return pair.A.OnlineRun && pair.B.OnlineRun; }, "GOBNOM_START on both");
                PumpUntil(() => { pair.Tick(); return pair.A.Run != null && pair.A.Run.Tick > 20; }, "the round runs");
                var aborted = _ra.Aborted.Count;
                pair.LinkB.Leave();
                Assert.That(pair.B.Phase, Is.EqualTo(GobnomGame.PhaseSelect), "the leaver is back in the select");
                PumpUntil(() => { pair.Tick(); return _ra.Aborted.Count == aborted + 1; }, "GOBNOM_ABORTED at Alice");
                Assert.That(_ra.Aborted[aborted], Is.EqualTo("left:" + _bob.FriendCode));
                Assert.That(pair.A.Phase, Is.EqualTo(GobnomGame.PhaseSelect), "the partner's round ends politely");
                Assert.That(pair.A.OnlineRun, Is.False);
                Assert.That(pair.NoticesA, Does.Contain("gobnom.netz.abbruch"));
                Assert.That(_ga.IsPaired || _gb.IsPaired, Is.False);
            }
        }

        [Test, Order(4)]
        public void D_AMismatchingHashMakesTheServerStopBothRounds()
        {
            Pair_InviteAndAccept();
            using (var pair = new Pair(_ga, _gb, 55, 66))
            {
                pair.LinkA.ChooseLevel(3);
                pair.LinkB.ChooseLevel(3);
                PumpUntil(() => { pair.Tick(); return pair.A.OnlineRun && pair.B.OnlineRun; }, "GOBNOM_START on both");
                var desyncs = _ra.Desyncs.Count;
                Assert.That(_ga.SendHash(5, "aaaaaaaaaaaaaaaa"), Is.True);
                Assert.That(_gb.SendHash(5, "bbbbbbbbbbbbbbbb"), Is.True);
                PumpUntil(() => { pair.Tick(); return pair.NoticesA.Contains("gobnom.netz.desync") && pair.NoticesB.Contains("gobnom.netz.desync"); },
                    "GOBNOM_DESYNC ends the round on both");
                Assert.That(_ra.Desyncs.Count, Is.EqualTo(desyncs + 1));
                Assert.That(_ra.Desyncs[desyncs], Is.EqualTo(5));
                Assert.That(pair.A.Phase, Is.EqualTo(GobnomGame.PhaseSelect));
                Assert.That(pair.B.Phase, Is.EqualTo(GobnomGame.PhaseSelect));
                PumpUntil(() => !_ga.IsPaired && !_gb.IsPaired, "both left the broken session");
            }
        }

        [Test, Order(5)]
        public void E_AConnectionDropMidRunHealsWithTheRejoinSnapshot()
        {
            Pair_InviteAndAccept();
            using (var pair = new Pair(_ga, _gb, 77, 88))
            {
                pair.LinkA.ChooseLevel(5);
                pair.LinkB.ChooseLevel(5);
                PumpUntil(() => { pair.Tick(); return pair.A.OnlineRun && pair.B.OnlineRun; }, "GOBNOM_START on both");
                Assert.That(_ga.Level, Is.EqualTo(5));
                pair.Bots = true; // during the establish beat — CN5's first action is at 0.5 s
                PumpUntil(() => { pair.Tick(); return pair.B.Run != null && pair.B.Run.Tick > 20; }, "the round runs");
                var downs = _ra.PeerDown.Count;
                var snapshots = _rb.Snapshots;
                var desyncs = _ra.Desyncs.Count + _rb.Desyncs.Count;
                _bob.Disconnect();
                Assert.That(_gb.Rejoining, Is.True, "a drop mid-run waits for the replay");
                Assert.That(_gb.SendHash(60, "stale"), Is.False, "nothing of the stale lockstep leaves");
                PumpUntil(() => { pair.Tick(); return _ra.PeerDown.Count == downs + 1; }, "GOBNOM_PEER_DOWN at Alice");
                Assert.That(_ra.PeerDown[downs], Is.True);
                Assert.That(pair.NoticesA, Does.Contain("gobnom.netz.partner_weg"));
                PumpFor(300);
                pair.Tick();
                Assert.That(pair.A.NetWaiting, Is.True, "Alice's sim waits for Bob's fence");
                _bob.Connect();
                PumpUntil(() => { pair.Tick(); return _rb.Snapshots == snapshots + 1 && _ra.PeerDown.Count == downs + 2; }, "WELCOME → ROOM_JOIN → GOBNOM_SNAPSHOT + PEER_UP");
                Assert.That(_ra.PeerDown[downs + 1], Is.False);
                Assert.That(pair.NoticesA, Does.Contain("gobnom.netz.partner_da"));
                Assert.That(_gb.Rejoining, Is.False, "the snapshot ends the rejoin");
                Assert.That(pair.B.OnlineRun && pair.B.LevelId == 5, Is.True, "Bob's round resumed from the replay");

                PumpUntil(() => { pair.Tick(); return pair.LinkA.LastResult != null && pair.LinkB.LastResult != null && pair.EndTickA >= 0 && pair.EndTickB >= 0; },
                    "both finish CN5 after the rejoin", 40000);
                Assert.That(pair.EndTickB, Is.EqualTo(pair.EndTickA));
                Assert.That(pair.EndHashB, Is.EqualTo(pair.EndHashA), "the replayed sim ends exactly like the live one");
                Assert.That(pair.LinkA.LastResult.Outcome, Is.EqualTo("won"));
                Assert.That(_ra.Desyncs.Count + _rb.Desyncs.Count, Is.EqualTo(desyncs), "no desync after the replay");
                PumpUntil(() => !_ga.IsPaired && !_gb.IsPaired, "both left");
            }
        }
    }
}
