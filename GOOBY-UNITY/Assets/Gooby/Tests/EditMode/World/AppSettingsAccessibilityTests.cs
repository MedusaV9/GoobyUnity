using System.IO;
using Gooby.Unity.App;
using NUnit.Framework;

namespace Gooby.Tests.World
{
    /// <summary>The accessibility settings of settings_screen.gd: colour vision, high contrast, hint duration — and the old switch.</summary>
    public class AppSettingsAccessibilityTests
    {
        private string _dir;

        [SetUp]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gooby-settings-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private AppSettings LoadWith(string json)
        {
            File.WriteAllText(Path.Combine(_dir, AppSettings.FileName), json);
            var s = new AppSettings();
            s.Load(_dir);
            return s;
        }

        [Test]
        public void TheOldColourBlindSwitchBecomesTheDeutanAid()
        {
            var s = LoadWith("{\"colorBlind\": true}");
            Assert.That(s.ColorVisionMode, Is.EqualTo("deutan"));
            Assert.That(LoadWith("{\"colorBlind\": false}").ColorVisionMode, Is.EqualTo("aus"));
            Assert.That(LoadWith("{\"colorBlind\": true, \"colorVision\": \"aus\"}").ColorVisionMode, Is.EqualTo("aus"), "a saved choice wins");
        }

        [Test]
        public void ChoicesSurviveARestartAndUnknownValuesFallBack()
        {
            var s = LoadWith("{}");
            Assert.That(s.ColorVisionMode, Is.EqualTo("aus"));
            Assert.That(s.HighContrast, Is.False);
            Assert.That(s.HintScale, Is.EqualTo(1f));
            s.SetColorVision("tritan");
            s.SetHighContrast(true);
            s.SetHintDuration("lang");
            var again = new AppSettings();
            again.Load(_dir);
            Assert.That(again.ColorVisionMode, Is.EqualTo("tritan"));
            Assert.That(again.HighContrast, Is.True);
            Assert.That(again.HintDuration, Is.EqualTo("lang"));
            Assert.That(again.HintScale, Is.EqualTo(2f), "notification_service.gd: 8 s instead of 4 s");
            var odd = LoadWith("{\"colorVision\": \"lila\", \"hintDuration\": \"ewig\"}");
            Assert.That(odd.ColorVisionMode, Is.EqualTo("aus"));
            Assert.That(odd.HintDuration, Is.EqualTo("normal"));
        }

        [Test]
        public void TheNewsAreSeenOncePerVersionAndTheToolboxStaysUnlocked()
        {
            var s = LoadWith("{}");
            Assert.That(s.WhatsNewSeen, Is.EqualTo(""));
            Assert.That(s.DevUnlocked, Is.False);
            s.SetWhatsNewSeen(Gooby.Unity.UI.Screens.WhatsNewScreen.Version);
            s.SetDevUnlocked(true);
            var again = new AppSettings();
            again.Load(_dir);
            Assert.That(again.WhatsNewSeen, Is.EqualTo(Gooby.Unity.UI.Screens.WhatsNewScreen.Version));
            Assert.That(again.DevUnlocked, Is.True);
        }
    }
}
