using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Minigames;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>The profile's completion (abschluss_logic.gd) and the GOOBY-PASS derivations (passport_card.gd, mrz_gag.gd).</summary>
    public class ProfileTests
    {
        [SetUp]
        public void Content()
        {
            Gooby.Tests.Core.Fixtures.InstallContentSource();
            MinigameRegistry.ResetCache();
        }

        private static JObject Fresh() => SaveSchema.DefaultState(1_760_000_000_000L);

        [Test]
        public void AFreshSaveStartsLowAndEveryPartCountsWithinItsTotal()
        {
            var s = Fresh();
            var parts = Completion.Parts(s);
            Assert.That(parts.Select(p => p.Id), Is.EqualTo(new[] { "level", "erfolge", "sticker", "arcade" }));
            Assert.That(parts[0].N, Is.EqualTo(1));
            Assert.That(parts[0].Total, Is.EqualTo(40));
            Assert.That(Completion.Percent(s), Is.InRange(0, 10));
            Assert.That(Completion.Extras(s).Select(p => p.Id), Is.EqualTo(new[] { "sticker_online", "sticker_dlc" }));
        }

        [Test]
        public void PlayingEveryArcadeGameAndMaxLevelRaiseTheShareAndHundredNeedsEveryPart()
        {
            var s = Fresh();
            var plays = (JObject)s["minigames"]["plays"];
            foreach (var g in MinigameRegistry.AllGames()) plays[g.Id] = 1;
            s["progression"]["level"] = 40;
            var parts = Completion.Parts(s);
            Assert.That(parts[3].N, Is.EqualTo(parts[3].Total), "all arcade games played");
            Assert.That(parts[0].N, Is.EqualTo(40));
            Assert.That(Completion.Percent(s), Is.GreaterThanOrEqualTo(50));
            Assert.That(Completion.Percent(s), Is.LessThan(100), "achievements and stickers still open");
            var full = new[] { new Completion.Part("a", 3, 3), new Completion.Part("b", 9, 10) };
            Assert.That(Completion.Percent(full), Is.EqualTo(95), "floored mean");
            Assert.That(new Completion.Part("x", 12, 10).N, Is.EqualTo(10), "capped at the total");
        }

        [Test]
        public void TheMrzIsDeterministicAToZAndPaddedAndTransliteratesUmlauts()
        {
            var a = Passport.MrzLines("Jörg Müller", "Flauschi");
            var b = Passport.MrzLines("Jörg Müller", "Flauschi");
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a[0], Does.StartWith("P<GOOFLAUSCHI<<JOERG<MUELLER"));
            foreach (var line in a)
            {
                Assert.That(line.Length, Is.EqualTo(Passport.MrzLineLength));
                Assert.That(line.All(c => (c >= 'A' && c <= 'Z') || c == '<'), Is.True, line);
            }
            Assert.That(Passport.MrzLine1("", ""), Does.StartWith("P<GOOGOOBY<<FLAUSCHFREUND"));
            Assert.That(Passport.Sanitize("Ähm, ß-Test!"), Is.EqualTo("AEHM<SS<TEST"));
        }

        [Test]
        public void StampsFollowTheVisitedDestinationsWithTheirLatestPostcardThenTheSpecialStamps()
        {
            var s = Fresh();
            var v = (JObject)s["vacation"];
            v["visited"] = new JObject { ["beach"] = true, ["space"] = true, ["harbor"] = false };
            v["archive"] = new JArray(
                new JObject { ["destId"] = "beach", ["dayIndex"] = 1, ["variant"] = 1, ["atMs"] = 1000L },
                new JObject { ["destId"] = "beach", ["dayIndex"] = 2, ["variant"] = 2, ["atMs"] = 5000L });
            var stamps = Passport.Stamps(s);
            Assert.That(stamps.Select(x => x.Id), Is.EqualTo(new[] { "beach", "space" }), "catalogue order, only visited");
            Assert.That(stamps[0].AtMs, Is.EqualTo(5000L), "the latest postcard dates the stamp");
            Assert.That(stamps[1].AtMs, Is.EqualTo(0L), "no postcard, no date");
            Assert.That(stamps.All(x => x.TiltDeg >= -6 && x.TiltDeg <= 6), Is.True);
            v["weltengoobyAt"] = 7000L;
            s["meta"]["importedFrom"] = "godot-4";
            s["meta"]["importedAt"] = 9000L;
            stamps = Passport.Stamps(s);
            Assert.That(stamps.Select(x => x.Id).Skip(2), Is.EqualTo(new[] { "weltengooby", "umzug" }));
            Assert.That(stamps[2].Colour, Is.EqualTo(4), "gold");
            Assert.That(stamps[3].Colour, Is.EqualTo(-1), "red");
        }

        [Test]
        public void TheFeatureLineRotatesDailyAndStaysInRange()
        {
            var seen = Enumerable.Range(0, 10).Select(d => Passport.FeatureIndex("Gooby", 20000 + d, 10)).ToList();
            Assert.That(seen.Distinct().Count(), Is.EqualTo(10), "a different line every day of ten");
            Assert.That(Passport.FeatureIndex("Gooby", 5, 0), Is.EqualTo(0));
        }

        [Test]
        public void ThePassportPhotoIsAFileNameThatSurvivesASaveRoundTrip()
        {
            var s = SaveSchema.DefaultState(1_760_000_000_000L);
            Assert.That(Passport.PhotoOf(s), Is.Empty, "a fresh save shows Gooby's 3D portrait");
            Passport.SetPhoto(s, "/data/user/photos/20261012_031100_123.jpg");
            Assert.That(Passport.PhotoOf(s), Is.EqualTo("20261012_031100_123.jpg"), "only the name, never the folder");
            var loaded = JObject.Parse(s.ToString(Newtonsoft.Json.Formatting.None));
            Assert.That(Passport.PhotoOf(loaded), Is.EqualTo("20261012_031100_123.jpg"));
            Assert.That(J.Num(J.Path(loaded, "profile", "playtimeMin")), Is.EqualTo(0.0), "the other profile keys stay");
            Passport.SetPhoto(loaded, "");
            Assert.That(Passport.PhotoOf(loaded), Is.Empty, "back to the portrait");
            Passport.SetPhoto(loaded, @"C:\Gooby\photos\a.jpg");
            Assert.That(Passport.PhotoOf(loaded), Is.EqualTo("a.jpg"));
        }
    }
}
