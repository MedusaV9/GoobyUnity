using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace Gooby.Tests.World
{
    /// <summary>Wall items (framed art, postcard cord, souvenir shelf) hang flat on their wall and face into the room.</summary>
    public class WallPropsTests
    {
        private static readonly Vector3 Origin = new Vector3(-3f, 0f, -2.5f);
        private const float W = 6f, D = 5f;

        [SetUp]
        public void Setup()
        {
            Core.Fixtures.InstallContentSource();
            FurnitureCatalog.ResetCache();
        }

        private static GridItem Item(string id, string wall, int offset) =>
            new GridItem { Uid = "t-" + wall, Def = FurnitureCatalog.Def(id), Wall = wall, At = new Cell(offset, 0), Rot = 0 };

        /// <summary>Direction from the wall surface into the room for each wall id.</summary>
        private static Vector3 Inward(string wall)
        {
            switch (wall)
            {
                case "N": return Vector3.back;
                case "S": return Vector3.forward;
                case "W": return Vector3.right;
                default: return Vector3.left;
            }
        }

        private static IEnumerable<Transform> Named(GameObject go, string name) =>
            go.GetComponentsInChildren<Transform>().Where(t => t.name == name);

        [TestCase("pictureFrameWall", "N", 1)]
        [TestCase("pictureFrameWall", "E", 3)]
        [TestCase("pictureFrameWall", "W", 2)]
        [TestCase("pictureFrameWall", "S", 4)]
        public void ArtQuadsFaceIntoTheRoomAndSitInFrontOfTheirFrames(string id, string wall, int offset)
        {
            var parent = new GameObject("WallTest");
            try
            {
                var item = Item(id, wall, offset);
                Assert.That(item.Def, Is.Not.Null, id + " is in the catalog");
                var go = WallProps.Make(parent.transform, item, Origin, W, D);
                Assert.That(go, Is.Not.Null);
                var arts = Named(go, "Art").ToList();
                var frames = Named(go, "Frame").ToList();
                Assert.That(arts.Count, Is.GreaterThan(0), "at least one painting");
                Assert.That(frames.Count, Is.EqualTo(arts.Count), "one frame per painting");
                var inward = Inward(wall);
                foreach (var art in arts)
                {
                    // ArtLibrary.Quad is visible from its local -Z, so the visible face points along -forward
                    var visibleFrom = -art.forward;
                    Assert.That(Vector3.Dot(visibleFrom, inward), Is.GreaterThan(0.99f), id + " on " + wall + ": painting faces the room, not the wall");
                    var frame = frames.OrderBy(f => Vector3.Distance(f.position, art.position)).First();
                    Assert.That(Vector3.Dot(art.position - frame.position, inward), Is.GreaterThan(0.005f), id + " on " + wall + ": painting hangs in front of its frame");
                    var fb = frame.GetComponent<Renderer>().bounds.size;
                    Assert.That(Mathf.Abs(Vector3.Dot(fb, inward)), Is.LessThan(0.1f), id + " on " + wall + ": the frame lies flat on the wall, not across it");
                    // flat against the wall: no thickness along the inward axis beyond the frame depth
                    var span = Mathf.Abs(Vector3.Dot(art.lossyScale, inward));
                    Assert.That(span, Is.LessThan(1.01f), "quad depth is unit scale");
                }
                // everything stays within a hand's width of the wall surface
                var wallPlane = wall == "N" ? Origin.z + D : wall == "S" ? Origin.z : wall == "W" ? Origin.x : Origin.x + W;
                foreach (var t in go.GetComponentsInChildren<Transform>().Where(t => t != go.transform))
                {
                    var along = wall == "N" || wall == "S" ? t.position.z : t.position.x;
                    Assert.That(Mathf.Abs(along - wallPlane), Is.LessThan(0.3f), t.name + " hugs the " + wall + " wall");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        private static float WallPlane(string wall) => wall == "N" ? Origin.z + D : wall == "S" ? Origin.z : wall == "W" ? Origin.x : Origin.x + W;

        private static float Along(string wall, Vector3 p) => wall == "N" || wall == "S" ? p.z : p.x;

        [TestCase("W", 1)]
        [TestCase("S", 4)]
        public void ThePostcardWallPinsTheNewestCardsOfTheArchiveOnCork(string wall, int offset)
        {
            var archive = new Newtonsoft.Json.Linq.JArray();
            for (var k = 1; k <= 8; k++)
                archive.Add(new Newtonsoft.Json.Linq.JObject { ["destId"] = k % 2 == 0 ? "beach" : "space", ["dayIndex"] = k, ["variant"] = 1, ["atMs"] = 1_760_000_000_000L + k * 86_400_000L });
            var state = new Newtonsoft.Json.Linq.JObject { ["vacation"] = new Newtonsoft.Json.Linq.JObject { ["archive"] = archive } };
            var parent = new GameObject("WallTest");
            try
            {
                var go = WallProps.Make(parent.transform, Item("postkartenWand", wall, offset), Origin, W, D, state);
                var cork = Named(go, "Cork").Single();
                var cards = Named(go, "Card").ToList();
                Assert.That(cards.Count, Is.EqualTo(WallProps.WallCards), "the six newest of eight");
                var inward = Inward(wall);
                Assert.That(Mathf.Abs(Vector3.Dot(cork.GetComponent<Renderer>().bounds.size, inward)), Is.LessThan(0.1f), "the cork lies flat on the " + wall + " wall");
                foreach (var card in cards)
                {
                    Assert.That(Vector3.Dot(card.position - cork.position, inward), Is.GreaterThan(0.01f), "cards hang in front of the cork");
                    Assert.That(Mathf.Abs(Vector3.Dot(card.GetComponent<Renderer>().bounds.size, inward)), Is.LessThan(0.05f), "cards lie flat on the cork");
                }
                foreach (var tr in go.GetComponentsInChildren<Transform>().Where(x => x != go.transform))
                    Assert.That(Mathf.Abs(Along(wall, tr.position) - WallPlane(wall)), Is.LessThan(0.3f), tr.name + " hugs the " + wall + " wall");
                var empty = WallProps.Make(parent.transform, Item("postkartenWand", wall, offset), Origin, W, D, new Newtonsoft.Json.Linq.JObject());
                Assert.That(Named(empty, "Card").Count(), Is.EqualTo(2), "before the first trip: two greeting cards from Sonnental");
                Assert.That(Named(empty, "Cork").Count(), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void TheSouvenirShelfShowsOneMiniPerVisitedDestination()
        {
            var state = new Newtonsoft.Json.Linq.JObject
            {
                ["vacation"] = new Newtonsoft.Json.Linq.JObject { ["visited"] = new Newtonsoft.Json.Linq.JObject { ["beach"] = true, ["space"] = true, ["harbor"] = true, ["bakery"] = false } },
            };
            var parent = new GameObject("WallTest");
            try
            {
                var go = WallProps.Make(parent.transform, Item("souvenirRegal", "E", 0), Origin, W, D, state);
                var minis = go.GetComponentsInChildren<Transform>().Where(x => x.name.StartsWith("Souvenir:")).Select(x => x.name).Distinct().OrderBy(n => n).ToList();
                Assert.That(minis, Is.EqualTo(new[] { "Souvenir:beach", "Souvenir:harbor", "Souvenir:space" }));
                var board = Named(go, "Board").Single();
                foreach (var tr in go.GetComponentsInChildren<Transform>().Where(x => x.name.StartsWith("Souvenir:")))
                {
                    Assert.That(tr.position.y, Is.GreaterThan(board.position.y), tr.name + " stands on the plank");
                    Assert.That(Mathf.Abs(Along("E", tr.position) - WallPlane("E")), Is.LessThan(0.3f), tr.name + " stays on the shelf");
                }
                var bare = WallProps.Make(parent.transform, Item("souvenirRegal", "E", 0), Origin, W, D, new Newtonsoft.Json.Linq.JObject());
                Assert.That(Named(bare, "Book").Count(), Is.EqualTo(3), "before the first trip the shelf keeps its books");
                Assert.That(bare.GetComponentsInChildren<Transform>().Any(x => x.name.StartsWith("Souvenir:")), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void MotifsDifferAcrossNeighbouringSlots()
        {
            var parent = new GameObject("WallTest");
            try
            {
                var textures = new HashSet<string>();
                for (var off = 0; off < 4; off++)
                {
                    var go = WallProps.Make(parent.transform, Item("pictureFrameWall", "N", off), Origin, W, D);
                    var art = Named(go, "Art").First().GetComponent<Renderer>();
                    textures.Add(art.sharedMaterial.name);
                }
                Assert.That(textures.Count, Is.EqualTo(4), "four neighbouring frames show four different paintings");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
