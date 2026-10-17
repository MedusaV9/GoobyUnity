using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// Ranch multiplayer against the real GOOBY-SERVER (ranchmp.js): invite → decline / late accept, a whole grass-track
    /// race ridden by two <see cref="RmpBot"/> riders in real time (RANCH_READY → MG_READY with the course hash → MG_START →
    /// 10-Hz poses, every checkpoint and the finish server-checked) to MG_RESULT with server times, ranks and ranked runs,
    /// booked once; the rematch into a fresh room; a short tag round with the catcher's tags; leaving mid-race as DNF.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class RanchMpIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private RanchMpClient _ra, _rb;
        private readonly List<RmpInvite> _invitesB = new List<RmpInvite>();
        private readonly List<string> _declinedA = new List<string>();
        private int _readyA, _readyB, _startedA, _startedB;

        [OneTimeSetUp]
        public void Setup()
        {
            NetLog.OnError = m => TestContext.Progress.WriteLine(m);
            Gooby.Tests.Core.Fixtures.InstallContentSource();
            StartServer(new Dictionary<string, string> { ["GOOBY_RMP_COUNTDOWN_MS"] = "1500", ["GOOBY_RMP_FANGEN_MS"] = "9000", ["GOOBY_RMP_REJOIN_MS"] = "3000" });
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            var sa = new Recorder(_alice);
            var sb = new Recorder(_bob);
            _ra = new RanchMpClient(_alice);
            _rb = new RanchMpClient(_bob);
            _rb.InviteIncoming += i => _invitesB.Add(i);
            _ra.InviteDeclined += by => _declinedA.Add(by);
            _ra.SessionReady += () => _readyA++;
            _rb.SessionReady += () => _readyB++;
            _ra.MatchStarted += () => _startedA++;
            _rb.MatchStarted += () => _startedB++;
            ConnectAll(_alice, _bob);
            MakeFriends(_alice, sa, _bob, sb);
        }

        private sealed class Riders : IDisposable
        {
            public readonly RmpLink A, B;
            public readonly List<string> NoticesA = new List<string>(), NoticesB = new List<string>();
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private double _last;

            public Riders(RanchMpClient a, RanchMpClient b)
            {
                A = new RmpLink(a, () => _clock.ElapsedMilliseconds) { Bot = true };
                B = new RmpLink(b, () => _clock.ElapsedMilliseconds) { Bot = true };
                A.Notice += (k, n) => NoticesA.Add(k);
                B.Notice += (k, n) => NoticesB.Add(k);
            }

            /// <summary>Real time: the server times the finish and checks the pose speed with its own clock.</summary>
            public void Tick(bool tag = false)
            {
                var now = _clock.Elapsed.TotalSeconds;
                var dt = Math.Min(0.1, now - _last);
                _last = now;
                if (dt <= 0.0) return;
                A.Tick(dt);
                B.Tick(dt);
                if (!tag) return;
                if (A.Client.IAmIt) A.TryTag();
                if (B.Client.IAmIt) B.TryTag();
            }

            public void Dispose()
            {
                A.Dispose();
                B.Dispose();
            }
        }

        private void InviteAndAccept(string mode)
        {
            var invites = _invitesB.Count;
            var invite = Await(cb => _ra.Invite(_bob.FriendCode, mode, "", cb), "RANCH_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            PumpUntil(() => _invitesB.Count == invites + 1, "RANCH_INVITED at Bob");
            Assert.That(_invitesB.Last().Mode, Is.EqualTo(mode));
            Assert.That(_invitesB.Last().Kurs, Is.EqualTo(RmpKurse.StandardKurs(mode)), "the server fills in the standard course");
            var readyA = _readyA;
            var accept = Await(cb => _rb.Accept(_alice.FriendCode, cb), "RANCH_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            Assert.That(accept.Type, Is.EqualTo("RANCH_READY"));
            PumpUntil(() => _readyA == readyA + 1 && _ra.Joined && _rb.Joined, "RANCH_READY + ROOM_JOIN on both");
            Assert.That(_rb.RoomId, Is.EqualTo(_ra.RoomId));
            Assert.That(_ra.RoomId, Does.StartWith("mg:"));
            Assert.That(_ra.Mode, Is.EqualTo(mode));
            Assert.That(_ra.Players.Select(p => p.Code), Is.EquivalentTo(new[] { _alice.FriendCode, _bob.FriendCode }));
        }

        private void BothReady()
        {
            var startedA = _startedA;
            Assert.That(Await(cb => _ra.SetReady(cb), "MG_READY Alice").Ok, Is.True);
            PumpFor(200);
            Assert.That(_startedA, Is.EqualTo(startedA), "one ready rider never starts the round");
            Assert.That(Await(cb => _rb.SetReady(cb), "MG_READY Bob").Ok, Is.True);
            PumpUntil(() => _startedA == startedA + 1 && _startedB == _startedA, "MG_START on both");
            Assert.That(_rb.Seed, Is.EqualTo(_ra.Seed));
            Assert.That(_ra.StartAtMs, Is.GreaterThan(0));
            Assert.That(_ra.CountdownMs, Is.InRange(0, 1600), "the server's countdown, on our clock");
        }

        [Test, Order(1)]
        public void A_DeclinedInviteReachesTheInviterAndBadModesAreRefused()
        {
            var bad = Await(cb => _ra.Invite(_bob.FriendCode, "polo", "", cb), "RANCH_INVITE polo");
            Assert.That(bad.Code, Is.EqualTo("BAD_MODE"));
            var badKurs = Await(cb => _ra.Invite(_bob.FriendCode, RmpKurse.Rennen, "hof_parcours", cb), "RANCH_INVITE wrong course");
            Assert.That(badKurs.Code, Is.EqualTo("BAD_KURS"));
            var invite = Await(cb => _ra.Invite(_bob.FriendCode, RmpKurse.Parcours, "", cb), "RANCH_INVITE");
            Assert.That(invite.Ok, Is.True, invite.Code);
            Assert.That(_ra.OutgoingInvite, Is.EqualTo(_bob.FriendCode));
            PumpUntil(() => _invitesB.Count == 1, "RANCH_INVITED at Bob");
            Assert.That(_invitesB[0].From, Is.EqualTo(_alice.FriendCode));
            Assert.That(_invitesB[0].GoobyName, Is.EqualTo("Flausch"));
            _rb.Decline(_alice.FriendCode);
            PumpUntil(() => _declinedA.Count == 1, "RANCH_DECLINED at Alice");
            Assert.That(_ra.OutgoingInvite, Is.EqualTo(""));
            var late = Await(cb => _rb.Accept(_alice.FriendCode, cb), "late RANCH_ACCEPT");
            Assert.That(late.Code, Is.EqualTo("NOT_FOUND"));
            var ready = Await(cb => _ra.SetReady(cb), "MG_READY without a session");
            Assert.That(ready.Code, Is.EqualTo("NOT_IN_ROOM"));
        }

        [Test, Order(2)]
        public void B_AWholeRaceOnTheGrassTrackWithServerTimesAndRanks()
        {
            InviteAndAccept(RmpKurse.Rennen);
            using (var riders = new Riders(_ra, _rb))
            {
                BothReady();
                PumpUntil(() => { riders.Tick(); return riders.A.Running && riders.B.Running; }, "the start shot", 4000);
                var startA = riders.A.Lauf.Controller.Pos;
                PumpUntil(() => { riders.Tick(); return _ra.Progress.Count == 2; }, "MG_STATE progress of both riders", 6000);
                PumpUntil(() => { riders.Tick(); return riders.A.LastResult != null && riders.B.LastResult != null; }, "both riders finish and MG_RESULT reaches both", 120000);
                Assert.That((riders.A.Lauf.Controller.Pos - startA).Length, Is.GreaterThan(30f), "Alice really rode");
                var ra = riders.A.LastResult;
                var rb = riders.B.LastResult;
                Assert.That(ra.All.Count, Is.EqualTo(2));
                Assert.That(ra.All.Select(r => r.Rank), Is.EquivalentTo(new[] { 1, 2 }));
                Assert.That(ra.All.Select(r => r.FriendCode + ":" + r.Rank + ":" + r.ZeitMs), Is.EqualTo(rb.All.Select(r => r.FriendCode + ":" + r.Rank + ":" + r.ZeitMs)), "the same table on both");
                Assert.That(ra.FriendCode, Is.EqualTo(_alice.FriendCode), "each gets its own line");
                Assert.That(rb.FriendCode, Is.EqualTo(_bob.FriendCode));
                foreach (var line in ra.All)
                {
                    Assert.That(line.Dnf, Is.False);
                    Assert.That(line.ZeitMs, Is.InRange(15000, 90000), "server time of a gallop lap");
                    Assert.That(line.Ranked, Is.True, "fair pace, right course hash, no segment faster than 900 ms");
                    Assert.That(line.Mode, Is.EqualTo(RmpKurse.Rennen));
                    Assert.That(line.Kurs, Is.EqualTo("grasbahn"));
                }
                Assert.That(ra.RewardId, Does.StartWith("rmp-").And.EndWith("-" + _alice.FriendCode));
                Assert.That(_ra.Phase, Is.EqualTo(RanchMpClient.PhaseDone));
                Assert.That(riders.NoticesA, Does.Contain("ranch_mp.lauf.ziel"));
                var save = new JObject();
                Assert.That(RmpState.ErgebnisVerbuchen(save, ra, "2026-10-01"), Is.True);
                Assert.That(RmpState.ErgebnisVerbuchen(save, ra, "2026-10-01"), Is.False, "booked once");
                Assert.That(RmpState.Statistik(save, RmpKurse.Rennen).Item1, Is.EqualTo(1));
                _ra.AckResult(ra.RewardId);
                _rb.AckResult(rb.RewardId);
                Assert.That(_ra.PendingResults, Is.Empty);

                // rematch: both agree → a fresh room, back in the lobby
                var readyA = _readyA;
                var waitB = new List<string>();
                _rb.RematchWaiting += c => waitB.Add(c);
                var first = Await(cb => _ra.RequestRematch(cb), "RMP_REMATCH Alice");
                Assert.That(first.Ok, Is.True, first.Code);
                PumpUntil(() => waitB.Contains(_alice.FriendCode), "RMP_REMATCH_WAIT at Bob");
                var oldRoom = _ra.RoomId;
                var second = Await(cb => _rb.RequestRematch(cb), "RMP_REMATCH Bob");
                Assert.That(second.Type, Is.EqualTo("RANCH_READY"));
                PumpUntil(() => _readyA == readyA + 1 && _ra.Joined && _rb.Joined, "the rematch room on both");
                Assert.That(_ra.RoomId, Is.Not.EqualTo(oldRoom));
                Assert.That(_ra.Rematch, Is.True);
                Assert.That(_ra.Phase, Is.EqualTo(RanchMpClient.PhaseLobby));
                riders.A.Leave();
                riders.B.Leave();
            }
        }

        [Test, Order(3)]
        public void C_AShortTagRoundRanksByTheTimeAsCatcher()
        {
            InviteAndAccept(RmpKurse.Fangen);
            using (var riders = new Riders(_ra, _rb))
            {
                BothReady();
                Assert.That(new[] { _alice.FriendCode, _bob.FriendCode }, Does.Contain(_ra.ItCode), "MG_START names the catcher");
                Assert.That(_rb.ItCode, Is.EqualTo(_ra.ItCode));
                Assert.That(_ra.EndsAtMs - _ra.StartAtMs, Is.EqualTo(9000));
                var firstIt = _ra.ItCode;
                var catchers = new HashSet<string> { firstIt };
                PumpUntil(() =>
                {
                    riders.Tick(tag: true);
                    if (_ra.ItCode.Length > 0) catchers.Add(_ra.ItCode);
                    return riders.A.LastResult != null && riders.B.LastResult != null;
                }, "the 9-s tag round ends with MG_RESULT on both", 30000);
                var r = riders.A.LastResult;
                Assert.That(r.All.Count, Is.EqualTo(2));
                Assert.That(r.All.Select(x => x.Rank), Is.EquivalentTo(new[] { 1, 2 }));
                Assert.That(r.All.Sum(x => x.ZeitMs), Is.InRange(8000, 10500), "the catcher's time adds up to the round");
                Assert.That(r.All[0].ZeitMs, Is.LessThanOrEqualTo(r.All[1].ZeitMs), "least time as catcher wins");
                Assert.That(r.Mode, Is.EqualTo(RmpKurse.Fangen));
                TestContext.Progress.WriteLine("tag round: catchers " + string.Join(",", catchers) + " · " + string.Join(" ", r.All.Select(x => x.FriendCode + "=" + x.ZeitMs)));
                Assert.That(riders.NoticesA.Count(n => n.StartsWith("ranch_mp.lauf.")), Is.GreaterThan(0), "the catcher was announced");
                riders.A.Leave();
                riders.B.Leave();
                PumpFor(200);
            }
        }

        [Test, Order(4)]
        public void D_LeavingMidRaceCountsAsGivenUp()
        {
            InviteAndAccept(RmpKurse.Rennen);
            using (var riders = new Riders(_ra, _rb))
            {
                BothReady();
                PumpUntil(() => { riders.Tick(); return riders.A.Running && riders.A.Lauf.NextCheckpoint >= 1; }, "the race runs", 8000);
                riders.B.Leave();
                Assert.That(_rb.IsActive, Is.False);
                PumpUntil(() => { riders.Tick(); return _ra.Dnf.Contains(_bob.FriendCode); }, "MG_STATE dnf for Bob at Alice", 6000);
                PumpUntil(() => { riders.Tick(); return riders.A.LastResult != null; }, "Alice finishes alone", 120000);
                var r = riders.A.LastResult;
                var bob = r.All.Single(x => x.FriendCode == _bob.FriendCode);
                var alice = r.All.Single(x => x.FriendCode == _alice.FriendCode);
                Assert.That(bob.Dnf, Is.True);
                Assert.That(bob.Ranked, Is.False);
                Assert.That(alice.Rank, Is.EqualTo(1));
                Assert.That(alice.Ranked, Is.True);
                riders.A.Leave();
            }
        }
    }
}
