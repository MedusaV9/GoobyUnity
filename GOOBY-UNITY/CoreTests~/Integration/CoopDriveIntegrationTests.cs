using System;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Integration
{
    /// <summary>
    /// The ride along against the real GOOBY-SERVER (rooms.js drive:, coop_drive.gd): during Alice's visit at Bob's, Bob
    /// invites over the visit relay and takes his seat, Alice accepts and follows his car at 5 Hz, picks the radio for both,
    /// gets out while Bob drives on; a declined invitation reaches Bob; a passenger whose driver leaves gets out by herself.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class CoopDriveIntegrationTests : ServerFixtureBase
    {
        private SocialClient _alice, _bob;
        private Recorder _ra, _rb;
        private CoopDriveClient _ca, _cb;
        private int _nonce = 7;

        [OneTimeSetUp]
        public void Setup()
        {
            StartServer();
            _alice = NewClient("Alice", "Flausch");
            _bob = NewClient("Bob", "Bobby");
            _ra = new Recorder(_alice);
            _rb = new Recorder(_bob);
            ConnectAll(_alice, _bob);
            MakeFriends(_alice, _ra, _bob, _rb);
            var req = Await(cb => _alice.RequestVisit(_bob.FriendCode, cb), "VISIT_REQUEST");
            Assert.That(req.Ok, Is.True, req.Code);
            PumpUntil(() => _rb.VisitIncoming.Count == 1, "VisitIncoming");
            var snapshot = new JObject { ["v"] = 1, ["goobyName"] = "Bobby", ["rooms"] = new JObject { ["living"] = new JObject { ["items"] = new JArray() } } };
            var accept = Await(cb => _bob.AcceptVisit(_alice.FriendCode, snapshot, cb), "VISIT_ACCEPT");
            Assert.That(accept.Ok, Is.True, accept.Code);
            PumpUntil(() => _alice.VisitJoined && _bob.VisitJoined && _alice.PeerInRoom && _bob.PeerInRoom, "both in the visit room");
            _ca = new CoopDriveClient(_alice, nonce: () => 1);
            _cb = new CoopDriveClient(_bob, nonce: () => _nonce++);
        }

        private void StartDrive()
        {
            bool? started = null;
            _cb.StartAsDriver(true, "Bob", (ok, code) => started = ok);
            PumpUntil(() => started.HasValue && _ca.Pending != null, "Bob in his seat, Alice invited");
            Assert.That(started, Is.True);
        }

        [Test, Order(1)]
        public void BobDrivesAliceRidesAlongPicksTheRadioAndGetsOut()
        {
            StartDrive();
            Assert.That(_cb.Role, Is.EqualTo(CoopDriveClient.RoleDriver));
            Assert.That(_cb.RoomId, Is.EqualTo("drive:" + _bob.FriendCode + "-7"));
            Assert.That(_ca.Pending.Room, Is.EqualTo(_cb.RoomId));
            Assert.That(_ca.Pending.Von, Is.EqualTo("Bob"));
            Assert.That(_ca.Pending.Radio, Is.True);

            bool? seated = null;
            var joinedAtBob = "";
            _cb.PeerJoined += name => joinedAtBob = name;
            _ca.Accept((ok, code) => seated = ok);
            PumpUntil(() => seated.HasValue, "Alice takes the seat");
            Assert.That(seated, Is.True);
            Assert.That(_ca.Role, Is.EqualTo(CoopDriveClient.RolePassenger));
            PumpUntil(() => joinedAtBob == "Alice", "Bob sees his passenger");

            for (var i = 0; i < 4; i++)
            {
                Assert.That(_cb.ReportCar(10 + i, -20, 90, 30), Is.True);
                Assert.That(_cb.ReportCar(99, 99, 0, 0), Is.False, "5 Hz: the second pose in the same instant stays home");
                PumpFor(230);
            }
            PumpUntil(() => _ca.PositionsReceived >= 4, "Alice follows the car");
            Assert.That(_ca.LastCar, Is.EqualTo((13.0, -20.0, 90.0, 30.0)));

            var radioAtBob = "";
            _cb.RadioSet += (station, offset) => radioAtBob = station;
            Assert.That(_ca.SetRadio("gooby_fm"), Is.True);
            Assert.That(_ca.RadioStation, Is.EqualTo("gooby_fm"), "applied on the presser's side at once");
            PumpUntil(() => radioAtBob == "gooby_fm", "Bob's radio follows");

            var leftAtBob = 0;
            _cb.PeerLeft += () => leftAtBob++;
            _ca.Leave();
            Assert.That(_ca.Active, Is.False);
            PumpUntil(() => leftAtBob == 1, "Bob sees Alice get out");
            Assert.That(_cb.Role, Is.EqualTo(CoopDriveClient.RoleDriver), "the driver drives on");
            _cb.Leave();
            PumpFor(150);
        }

        [Test, Order(2)]
        public void ADeclineReachesBobAndALeavingDriverLetsAliceOut()
        {
            var declined = 0;
            _cb.InviteDeclined += () => declined++;
            StartDrive();
            _ca.Decline();
            Assert.That(_ca.Pending, Is.Null);
            PumpUntil(() => declined == 1, "Bob hears the no");
            _cb.Leave();
            PumpFor(150);

            StartDrive();
            bool? seated = null;
            _ca.Accept((ok, code) => seated = ok);
            PumpUntil(() => seated.HasValue, "Alice in again");
            var reason = "";
            _ca.Ended += r => reason = r;
            _cb.Leave();
            PumpUntil(() => !_ca.Active, "Alice gets out when Bob leaves");
            Assert.That(reason, Is.EqualTo("fahrer_weg"));
        }
    }
}
