using Gooby.Core.Home;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The visitor's couch rule (port of couch_logic.gd's cases).</summary>
    public class CouchLogicTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            FurnitureCatalog.ResetCache();
        }

        [Test]
        public void OnlyAnEveningWithBothGoobysWornOut()
        {
            Assert.That(CouchLogic.SollSchlafen(21, 8, 10), Is.True);
            Assert.That(CouchLogic.SollSchlafen(23, 0, 0), Is.True);
            Assert.That(CouchLogic.SollSchlafen(20, 5, 5), Is.False, "not evening yet at the host's");
            Assert.That(CouchLogic.SollSchlafen(22, 11, 5), Is.False, "the guest still has energy");
            Assert.That(CouchLogic.SollSchlafen(22, 5, 10.5), Is.False, "the host still has energy");
            Assert.That(CouchLogic.SollSchlafen(-1, 5, 5), Is.False, "no hour from the host: off");
            Assert.That(CouchLogic.SollSchlafen(22, -1, 5), Is.False, "unknown energy: off");
        }

        [Test]
        public void FindsTheCouchAmongTheLivingRoomPieces()
        {
            var items = JArray.Parse(@"[{""uid"":""i-1"",""item"":""chair"",""at"":[1,1]},{""uid"":""i-2"",""item"":""pictureFrameWall"",""wall"":""N"",""at"":[2,0]},
                {""uid"":""i-3"",""item"":""loungeSofa"",""at"":[4,5],""rot"":0}]");
            var (ok, cell, id) = CouchLogic.CouchSuchen(items);
            Assert.That(ok, Is.True);
            Assert.That(cell, Is.EqualTo(new Cell(4, 5)));
            Assert.That(id, Is.EqualTo("loungeSofa"));
            Assert.That(CouchLogic.CouchSuchen(JArray.Parse(@"[{""item"":""chair"",""at"":[1,1]}]")).ok, Is.False, "no couch: a floor nap");
            Assert.That(CouchLogic.CouchSuchen(null).ok, Is.False);
        }

        [Test]
        public void NapPayloadRoundTripsAndBadBodiesAreRefused()
        {
            var (ok, on, cell, boden) = CouchLogic.ParseNap(CouchLogic.NapPayload(true, new Cell(4, 5), false));
            Assert.That(ok && on && !boden, Is.True);
            Assert.That(cell, Is.EqualTo(new Cell(4, 5)));
            Assert.That(CouchLogic.ParseNap(JObject.Parse(@"{""on"":""ja""}")).ok, Is.False, "on must be a bool");
            Assert.That(CouchLogic.ParseNap(new JArray()).ok, Is.False);
            var floor = CouchLogic.ParseNap(JObject.Parse(@"{""on"":true,""boden"":true,""extra"":1}"));
            Assert.That(floor.ok && floor.boden, Is.True, "unknown fields are ignored");
        }
    }
}
