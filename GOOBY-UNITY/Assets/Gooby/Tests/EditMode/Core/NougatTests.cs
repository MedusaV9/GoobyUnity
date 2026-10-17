using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The Nougatschleuse (port of test_w13_food_nougat.gd): the catalog piece, install → ready → dollop → 30-minute
    /// pause, the refusal order (asleep, sick, pause) with the future-stamp clamp, the effects on stats, health, weight,
    /// XP and the Nutella jar, level-up coins through the one money path, and fail-closed on a refusal.
    /// </summary>
    public class NougatTests
    {
        private const long Now = 1_780_000_000_000;

        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        private static JObject State(double hunger = 40, double xp = 0) => new JObject
        {
            ["gooby"] = new JObject
            {
                ["stats"] = new JObject { ["hunger"] = hunger, ["fun"] = 50, ["energy"] = 60, ["hygiene"] = 70 },
                ["health"] = new JObject { ["state"] = "healthy", ["junkScore"] = 0 },
                ["weight"] = 50,
            },
            ["progression"] = new JObject { ["xp"] = xp, ["level"] = 1 },
            ["economy"] = new JObject { ["coins"] = 0 },
        };

        [Test]
        public void TheKitchenPieceIsAnInteractable()
        {
            var def = FurnitureCatalog.Def(Nougat.FurnitureId);
            Assert.That(def, Is.Not.Null);
            Assert.That(def.Kategorie, Is.EqualTo("kueche"));
            Assert.That(def.Preis, Is.EqualTo(Nougat.Price));
            Assert.That(Interactables.Kind(def), Is.EqualTo(Interactables.Nougat));
        }

        [Test]
        public void InstallReadyDollopPause()
        {
            var s = State();
            Assert.That(Nougat.IsInstalled(s), Is.False);
            Assert.That(Nougat.MarkInstalled(s), Is.True);
            Assert.That(Nougat.MarkInstalled(s), Is.False, "idempotent");
            Assert.That(Nougat.CooldownRemainingMs(s, Now), Is.EqualTo(0));
            Assert.That(Nougat.CanGlob(s, Now), Is.EqualTo(Nougat.Ok));
            Assert.That(Nougat.ApplyGlob(s, Now), Is.Not.Null);
            Assert.That(J.Long(s["easterEggs"]["nougat"]["lastGlobAt"]), Is.EqualTo(Now));
            Assert.That(Nougat.CooldownRemainingMs(s, Now + 60_000), Is.EqualTo(29 * 60_000));
            Assert.That(Nougat.CanGlob(s, Now + 60_000), Is.EqualTo(Nougat.Cooldown));
            Assert.That(Nougat.CooldownRemainingMs(s, Now + 30 * 60_000), Is.EqualTo(0));
            Assert.That(Nougat.CanGlob(s, Now + 30 * 60_000), Is.EqualTo(Nougat.Ok));
        }

        [Test]
        public void RefusalsAsleepSickAndAFutureStamp()
        {
            var schlaf = State();
            schlaf["gooby"]["sleep"] = new JObject { ["sleeping"] = true };
            Assert.That(Nougat.CanGlob(schlaf, Now), Is.EqualTo(Nougat.Sleeping));
            var krank = State();
            krank["gooby"]["health"]["state"] = "sick";
            Assert.That(Nougat.CanGlob(krank, Now), Is.EqualTo(Nougat.Sick));
            var zukunft = State();
            Nougat.Slice(zukunft)["lastGlobAt"] = Now + 999L * 24 * 3_600_000;
            Assert.That(Nougat.CooldownRemainingMs(zukunft, Now), Is.EqualTo(30 * 60_000), "a far-future stamp counts as just now");
        }

        [Test]
        public void ADollopFeedsGivesAJarAndCounts()
        {
            var s = State(hunger: 40);
            var r = Nougat.ApplyGlob(s, Now);
            Assert.That(r.HungerGain, Is.EqualTo(15));
            Assert.That(J.Num(s["gooby"]["stats"]["fun"]), Is.EqualTo(60));
            Assert.That(J.Num(s["gooby"]["stats"]["hygiene"]), Is.EqualTo(62));
            Assert.That(J.Num(s["gooby"]["health"]["junkScore"]), Is.EqualTo(2), "pure nougat counts double");
            Assert.That(J.Num(s["gooby"]["weight"]), Is.EqualTo(52), "one junk meal of weight");
            Assert.That(J.Num(s["progression"]["xp"]), Is.EqualTo(2));
            Assert.That(J.Int(s["inventory"]["food"]["nutella"]), Is.EqualTo(1), "the jar for the fridge");
            Assert.That(r.Jars, Is.EqualTo(1));
            Assert.That(J.Int(s["achievements"]["counters"]["nougatGlobs"]), Is.EqualTo(1));
            Assert.That(r.Globs, Is.EqualTo(1));
            Assert.That(Nougat.IsInstalled(s), Is.True);
        }

        [Test]
        public void LevelUpCoinsGoThroughTheMoneyPath()
        {
            var s = State(xp: 99);
            var r = Nougat.ApplyGlob(s, Now);
            Assert.That(r.Level, Is.EqualTo(2));
            Assert.That(r.LevelsGained, Is.EqualTo(1));
            Assert.That(r.CoinsAwarded, Is.EqualTo(50));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(50));
        }

        [Test]
        public void ARefusalChangesNothing()
        {
            var s = State();
            Nougat.ApplyGlob(s, Now);
            var before = s.ToString();
            Assert.That(Nougat.ApplyGlob(s, Now + 1000), Is.Null);
            Assert.That(s.ToString(), Is.EqualTo(before));
        }
    }
}
