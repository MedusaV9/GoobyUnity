using Gooby.Core.Minigames;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>minigame_host.gd effective_orientation(): the settings choice wins over the registry default.</summary>
    public class MinigameOrientationTests
    {
        [TestCase("auto", "portrait", "portrait")]
        [TestCase("auto", "landscape", "landscape")]
        [TestCase(null, "landscape", "landscape")]
        [TestCase("", "weird", "portrait")]
        [TestCase("landscape", "portrait", "landscape")]
        [TestCase("portrait", "landscape", "portrait")]
        public void ChoiceWinsOverTheRegistry(string choice, string registry, string expected)
        {
            Assert.That(FrameworkLogic.EffectiveOrientation(choice, registry), Is.EqualTo(expected));
        }

        [Test]
        public void EveryRegisteredGameSitsInOneArcadeRow()
        {
            foreach (var game in MinigameRegistry.AllGames())
            {
                Assert.That(ArcadeRows.Assigned(game.Id), Is.True, game.Id + " needs an arcade row");
                Assert.That(ArcadeRows.Order, Has.Member(ArcadeRows.RowOf(game.Id)));
            }
            Assert.That(ArcadeRows.RowOf("someFutureGame"), Is.EqualTo(ArcadeRows.Fallback));
        }

        [Test]
        public void EveryRegisteredGameHasANormalizedOrientation()
        {
            foreach (var game in MinigameRegistry.AllGames())
                Assert.That(game.Orientation, Is.EqualTo("portrait").Or.EqualTo("landscape"), game.Id);
        }
    }
}
