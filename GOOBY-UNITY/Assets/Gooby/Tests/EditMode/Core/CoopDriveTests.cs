using Gooby.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The ride along's wire format (coop_drive_logic.gd): room id, invitation, car pose, radio, the 5 Hz gate.</summary>
    public class CoopDriveTests
    {
        private static JObject Wire(JObject o) => NetJson.ParseObject(o.ToString(Formatting.None));

        [Test]
        public void TheRoomAndTheInvitationMatchTheServer()
        {
            Assert.That(CoopDriveLogic.DriveRoomId("ABCD-1234", -42), Is.EqualTo("drive:ABCD-1234-42"));
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(CoopDriveLogic.DriveRoomId("ABCD-1234", 999999), @"^drive:[A-Za-z0-9._-]{1,64}$"), Is.True, "rooms.js ROOM_RE");
            var invite = CoopDriveLogic.ParseDrive(Wire(CoopDriveLogic.InvitePayload("drive:X-1", true, "Bob")));
            Assert.That(invite, Is.EqualTo((true, "invite", "drive:X-1", true, "Bob")));
            Assert.That(CoopDriveLogic.ParseDrive(Wire(CoopDriveLogic.InvitePayload("visit:X", false, "Bob"))).ok, Is.False, "an invitation must name a drive: room");
            Assert.That(CoopDriveLogic.ParseDrive(Wire(CoopDriveLogic.DeclinePayload())).op, Is.EqualTo("decline"));
            Assert.That(CoopDriveLogic.ParseDrive(new JObject { ["op"] = "hello" }).ok, Is.False);
            Assert.That(CoopDriveLogic.ParseDrive(new JObject { ["op"] = "invite", ["room"] = "drive:X-1", ["radio"] = "ja" }).radio, Is.False, "only a real boolean grants the radio");
        }

        [Test]
        public void TheCarPoseAndTheRadioSurviveTheWire()
        {
            var car = CoopDriveLogic.ParseCar(Wire(CoopDriveLogic.CarPayload(12.34567, -8.9, 271.04, 31.456)));
            Assert.That(car.ok, Is.True);
            Assert.That(car.x, Is.EqualTo(12.346).Within(1e-9));
            Assert.That(car.z, Is.EqualTo(-8.9).Within(1e-9));
            Assert.That(car.yawDeg, Is.EqualTo(271.0).Within(1e-9));
            Assert.That(car.kmh, Is.EqualTo(31.46).Within(1e-9));
            Assert.That(CoopDriveLogic.ParseCar(new JObject { ["pos"] = new JArray(1) }).ok, Is.False);
            Assert.That(CoopDriveLogic.ParseCar(new JObject { ["pos"] = new JArray(1, 2) }), Is.EqualTo((true, 1.0, 2.0, 0.0, 0.0)), "the Godot payload without yaw still reads");
            var radio = CoopDriveLogic.ParseRadio(Wire(CoopDriveLogic.RadioPayload("gooby_fm", "", 5000)));
            Assert.That(radio, Is.EqualTo((true, "set", "gooby_fm", "", 5000L)));
            Assert.That(CoopDriveLogic.ParseRadio(new JObject { ["op"] = "set" }).ok, Is.False, "a set needs a station");
            Assert.That(CoopDriveLogic.ParseRadio(Wire(CoopDriveLogic.RadioStopPayload(1))).ok, Is.True);
            Assert.That(CoopDriveLogic.OffsetSec(5000, 6500), Is.EqualTo(1.5));
            Assert.That(CoopDriveLogic.OffsetSec(5000, 4000), Is.EqualTo(0.0), "clocks apart: never negative");
        }

        [Test]
        public void ThePoseGoesOutAtFiveHertz()
        {
            Assert.That(CoopDriveLogic.ShouldSendPos(1000, 1199), Is.False);
            Assert.That(CoopDriveLogic.ShouldSendPos(1000, 1200), Is.True);
        }
    }
}
