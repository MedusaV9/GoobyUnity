using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace Gooby.Tests.World
{
    /// <summary>Surface pieces stand on their support's model (RoomView, BuildSession), not at a flat 45 cm.</summary>
    public class SurfaceSeatingTests
    {
        [SetUp]
        public void Setup()
        {
            Core.Fixtures.InstallContentSource();
            FurnitureCatalog.ResetCache();
        }

        private static GridItem Item(string uid, string id, int x, int y, int rot = 0) =>
            new GridItem { Uid = uid, Def = FurnitureCatalog.Def(id), At = new Cell(x, y), Rot = rot };

        [Test]
        public void TheSupportIsTheSurfaceFurnitureUnderThePiece()
        {
            var dresser = Item("a", "cabinetTelevision", 4, 0);
            var tv = Item("b", "televisionModern", 4, 0);
            var bowl = Item("c", "proc:goldfishBowl", 6, 0);
            var stray = Item("d", "books", 9, 9);
            var sofa = Item("e", "loungeSofa", 8, 8);
            var all = new List<GridItem> { dresser, tv, bowl, stray, sofa };
            Assert.That(PropPlaceholder.SupportOf(tv, all), Is.SameAs(dresser));
            Assert.That(PropPlaceholder.SupportOf(bowl, all), Is.SameAs(dresser), "the dresser's third cell carries the bowl");
            Assert.That(PropPlaceholder.SupportOf(stray, all), Is.Null, "nothing under it");
            Assert.That(PropPlaceholder.SupportOf(dresser, all), Is.Null, "floor pieces stand on the floor");
        }

        [Test]
        public void APieceStandsOnItsSupportsTop()
        {
            var cabinet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var piece = new GameObject("Piece");
            try
            {
                cabinet.transform.position = new Vector3(0f, 0.345f, 0f);
                cabinet.transform.localScale = new Vector3(1.5f, 0.69f, 0.5f);
                piece.transform.position = new Vector3(0.5f, 0.45f, 0f);
                PropPlaceholder.SeatOnSupport(piece, Item("a", "cabinetTelevision", 4, 0), cabinet);
                Assert.That(piece.transform.position.y, Is.EqualTo(0.69f).Within(1e-4f), "on the dresser's top, not at 45 cm");
                Assert.That(piece.transform.position.x, Is.EqualTo(0.5f), "only the height changes");
            }
            finally
            {
                Object.DestroyImmediate(cabinet);
                Object.DestroyImmediate(piece);
            }
        }

        [Test]
        public void ACoffeeTableWithBakedBooksCarriesPiecesOnItsBoard()
        {
            var table = new GameObject("TableLow");
            var piece = new GameObject("Piece");
            try
            {
                table.transform.localScale = new Vector3(1f, 0.75f, 0.5f);
                PropPlaceholder.SeatOnSupport(piece, Item("a", "tableCoffee", 2, 5), table);
                Assert.That(piece.transform.position.y, Is.EqualTo(0.43f * 0.75f).Within(1e-4f), "the board, not the baked books on it");
            }
            finally
            {
                Object.DestroyImmediate(table);
                Object.DestroyImmediate(piece);
            }
        }
    }
}
