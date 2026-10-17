using System.Linq;
using Gooby.Core.City;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The split-flap departure board (flap_board.gd): letters clatter forward, rows sit on a fixed grid.</summary>
    public class FlapBoardTests
    {
        [Test]
        public void ALetterClattersForwardThroughTheAlphabet()
        {
            Assert.That(FlapBoard.ZeichenSchritt('A', 'C'), Is.EqualTo('B'));
            Assert.That(FlapBoard.ZeichenSchritt('C', 'C'), Is.EqualTo('C'));
            Assert.That(FlapBoard.ZeichenSchritt('-', 'A'), Is.EqualTo(' '), "past the last flap it wraps to the blank");
            Assert.That(FlapBoard.ZeichenSchritt(' ', 'Ü'), Is.EqualTo('Ü'), "letters off the alphabet snap");
            var frames = FlapBoard.Sequenz("", "AB");
            Assert.That(frames.Last(), Is.EqualTo("AB"));
            Assert.That(frames.Count, Is.EqualTo(2), "blank → A takes one flap, blank → B two");
            Assert.That(frames[0], Is.EqualTo("AA"));
            Assert.That(FlapBoard.Sequenz("Z", "A").Count, Is.EqualTo(16), "Z goes on through the digits, the blank and round to A");
            Assert.That(FlapBoard.Sequenz("ABC", "ABC"), Is.Empty);
        }

        [Test]
        public void RowsSitOnTheGridInCapitals()
        {
            var row = FlapBoard.Zeile("Glitzermeer", "3 Tage · 180 ᴳ", "Pünktlich");
            Assert.That(row.Length, Is.EqualTo(47));
            Assert.That(row.Substring(0, 17), Is.EqualTo("GLITZERMEER      "));
            Assert.That(row.Substring(17, 15), Is.EqualTo("3 TAGE · 180   "), "13 letters fit, the coin mark falls off the edge as in Godot");
            Assert.That(row.Substring(32), Is.EqualTo("PÜNKTLICH      "));
            Assert.That(FlapBoard.Norm("Spielzeugzimmerchen", 17), Is.EqualTo("SPIELZEUGZIMMER  "), "cut with two letters of air");
            Assert.That(FlapBoard.SpaltenFuerBudget(60), Is.EqualTo(new[] { 17, 15, 15 }));
            Assert.That(FlapBoard.SpaltenFuerBudget(30), Is.EqualTo(new[] { 18, 0, 12 }), "narrow: Ziel | Status");
            Assert.That(FlapBoard.SpaltenFuerBudget(5), Is.EqualTo(new[] { 12, 0, 8 }));
        }

        [Test]
        public void StatusIsStablePerDestinationAndTooExpensiveWins()
        {
            Assert.That(FlapBoard.StatusKey("beach", true), Is.EqualTo(FlapBoard.StatusKey("beach", true)));
            Assert.That(FlapBoard.StatusKey("beach", false), Is.EqualTo("reisepass.tafel.status_zu_teuer"));
            var keys = new[] { "beach", "harbor", "meadowTrip", "spookGarden", "bakery", "bigCity", "nightSky", "toyRoom", "space" }
                .Select(z => FlapBoard.StatusKey(z, true)).Distinct().Count();
            Assert.That(keys, Is.GreaterThan(1), "the gag statuses vary across the board");
        }
    }
}
