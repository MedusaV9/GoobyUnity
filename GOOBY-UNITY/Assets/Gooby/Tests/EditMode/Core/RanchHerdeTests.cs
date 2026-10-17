using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Random;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// Sheep herding — port of test_ranchplay_herde.gd: level data, pen/gate geometry, deterministic spawn + pure step,
    /// rider clamp, score/stars, difficulty knobs and the bot certification (every level doable, same seed = same run),
    /// plus the session and the ranch.spiele progress.
    /// </summary>
    public class RanchHerdeTests
    {
        private List<JObject> _levels;

        [OneTimeSetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            _levels = RanchHerde.LoadLevels();
        }

        private static JObject Lv(object o) => JObject.FromObject(o);

        [Test]
        public void LevelsLoadValidateAndGrow()
        {
            Assert.That(_levels.Count, Is.EqualTo(10));
            Assert.That(RanchHerde.ValidateLevels(_levels), Is.Empty);
            var first = RanchHerde.LevelById(_levels, 1);
            var last = RanchHerde.LevelById(_levels, 10);
            Assert.That((int)last["schafe"], Is.GreaterThan((int)first["schafe"]));
            Assert.That((int)first["schafe"], Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void PenGeometryAndGate()
        {
            var level = Lv(new { feld = new[] { 12.0, 9.0 }, pferch = new { x = 2.0, z = -6.0, w = 6.0, t = 4.0, tor = 3.0 } });
            var tor = RanchHerde.TorPos(level);
            Assert.That(tor.X, Is.EqualTo(2.0).Within(1e-6));
            Assert.That(tor.Y, Is.EqualTo(-4.0).Within(1e-6));
            Assert.That(RanchHerde.IstImPferch(level, new V2(2f, -6f)), Is.True);
            Assert.That(RanchHerde.IstImPferch(level, new V2(2f, -3f)), Is.False);
            Assert.That(RanchHerde.IstImPferch(level, new V2(6f, -6f)), Is.False);
        }

        [Test]
        public void SpawnIsDeterministicInTheSouthAndTheStepIsPure()
        {
            var level = RanchHerde.LevelById(_levels, 3);
            var a = RanchHerde.SpawnSchafe(level, new GoobyRng(7));
            var b = RanchHerde.SpawnSchafe(level, new GoobyRng(7));
            Assert.That(string.Join(";", b), Is.EqualTo(string.Join(";", a)));
            Assert.That(a.Count, Is.EqualTo((int)level["schafe"]));
            var feld = RanchHerde.Feld(level);
            foreach (var s in a)
            {
                Assert.That(s.Pos.Y, Is.GreaterThan(0.0));
                Assert.That(System.Math.Abs(s.Pos.X), Is.LessThanOrEqualTo(feld.x));
                Assert.That(s.Drin, Is.False);
            }

            var lv2 = RanchHerde.LevelById(_levels, 2);
            var schafe = RanchHerde.SpawnSchafe(lv2, new GoobyRng(3));
            var before = string.Join(";", schafe);
            var reiter = new V2(0f, 8f);
            var x = RanchHerde.Step(schafe, reiter, 1.0, 1.0 / 30.0, RanchHerde.Base, lv2);
            var y = RanchHerde.Step(schafe, reiter, 1.0, 1.0 / 30.0, RanchHerde.Base, lv2);
            Assert.That(string.Join(";", schafe), Is.EqualTo(before), "input untouched");
            Assert.That(string.Join(";", y), Is.EqualTo(string.Join(";", x)));
        }

        [Test]
        public void SheepFleeTheRiderAndPennedSheepStay()
        {
            var level = RanchHerde.LevelById(_levels, 1);
            var tune = RanchHerde.Base.Clone();
            tune.WanderKraft = 0.0;
            var one = new List<RanchHerde.Schaf> { new RanchHerde.Schaf { Pos = new V2(0f, 4f), Vel = V2.Zero } };
            var after = RanchHerde.Step(one, new V2(0f, 6f), 0.0, 1.0 / 30.0, tune, level);
            Assert.That(after[0].Vel.Y, Is.LessThan(0f), "flees north, away from the rider in the south");

            var pen = Lv(new { feld = new[] { 12.0, 9.0 }, pferch = new { x = 0.0, z = -6.0, w = 6.0, t = 4.0, tor = 3.0 } });
            var flock = new List<RanchHerde.Schaf> { new RanchHerde.Schaf { Pos = new V2(0f, -6f), Vel = new V2(0f, 3f), Drin = true } };
            for (var i = 0; i < 120; i++) flock = RanchHerde.Step(flock, new V2(0f, 8f), i / 30.0, 1.0 / 30.0, RanchHerde.Base, pen);
            Assert.That(flock[0].Drin, Is.True);
            Assert.That(RanchHerde.IstImPferch(pen, flock[0].Pos, 0.0), Is.True, "the walls hold");
        }

        [Test]
        public void TheRiderIsClampedAndScoreStarsAndDifficultyFollowTheTables()
        {
            var level = Lv(new { feld = new[] { 10.0, 8.0 }, pferch = new { } });
            Assert.That(RanchHerde.ReiterStep(new V2(9.9f, 0f), new V2(50f, 0f), 10.0, RanchHerde.Base, level).X, Is.EqualTo(10.0).Within(1e-6));
            Assert.That(RanchHerde.ReiterStep(new V2(0f, -7.9f), new V2(0f, -50f), 10.0, RanchHerde.Base, level).Y, Is.EqualTo(-8.0).Within(1e-6));
            Assert.That(RanchHerde.LevelScore(3, 20.0, false, RanchHerde.Base), Is.EqualTo(40 + 40 + 12));
            Assert.That(RanchHerde.LevelScore(3, 20.0, true, RanchHerde.Base), Is.EqualTo(40 + 40 + 12 + 25));
            Assert.That(RanchHerde.LevelScore(1, -5.0, false, RanchHerde.Base), Is.EqualTo(40 + 4));
            Assert.That(RanchHerde.Sterne(30.0, 60.0), Is.EqualTo(3));
            Assert.That(RanchHerde.Sterne(12.0, 60.0), Is.EqualTo(2));
            Assert.That(RanchHerde.Sterne(3.0, 60.0), Is.EqualTo(1));
            Assert.That(RanchHerde.Sterne(10.0, 0.0), Is.EqualTo(1));
            var easy = RanchHerde.ApplyDifficulty(RanchHerde.Base, "easy");
            var hard = RanchHerde.ApplyDifficulty(RanchHerde.Base, "hard");
            Assert.That(easy.ReiterTempo, Is.GreaterThan(hard.ReiterTempo));
            Assert.That(easy.WanderKraft, Is.LessThan(hard.WanderKraft));
            var sixty = Lv(new { zeit_s = 60.0 });
            Assert.That(RanchHerde.Zeitlimit(sixty, easy), Is.EqualTo(75.0).Within(1e-6));
            Assert.That(RanchHerde.Zeitlimit(sixty, hard), Is.EqualTo(51.0).Within(1e-6));
            Assert.That(RanchHerde.Zeitlimit(sixty, RanchHerde.Base), Is.EqualTo(60.0).Within(1e-6));
        }

        [Test]
        public void TheBotIsDeterministicAndClearsEveryLevelOnNormalAndLevel1OnEasy()
        {
            var lv2 = RanchHerde.LevelById(_levels, 2);
            var a = RanchHerde.SimulateHueten(lv2, 11);
            var b = RanchHerde.SimulateHueten(lv2, 11);
            Assert.That(b.ZeitS, Is.EqualTo(a.ZeitS));
            Assert.That(b.Drin, Is.EqualTo(a.Drin));
            for (var id = 1; id <= 10; id++)
            {
                var r = RanchHerde.SimulateHueten(RanchHerde.LevelById(_levels, id), 1);
                Assert.That(r.Geschafft, Is.True, $"level {id} doable (in={r.Drin})");
                Assert.That(r.Score, Is.GreaterThan(0));
                Assert.That(r.Sterne, Is.GreaterThanOrEqualTo(1));
            }
            for (var seed = 1; seed <= 3; seed++)
                Assert.That(RanchHerde.SimulateHueten(RanchHerde.LevelById(_levels, 1), seed, "easy").Geschafft, Is.True, "easy seed " + seed);
        }

        [Test]
        public void TheSessionBookstheRanchSlice()
        {
            var g = new RanchHerdeGame("normal", 5);
            Assert.That(g.IsUnlocked(2), Is.False);
            var guard = 0;
            while (RanchSpieleProgress.MaxUnlocked(g.Save, RanchSpieleProgress.Herde) < 4 && guard++ < 100000) { g.ApplyBot(); g.Tick(1.0 / 30.0); }
            Assert.That(RanchSpieleProgress.IsCleared(g.Save, RanchSpieleProgress.Herde, 3), Is.True);
            Assert.That(g.Save["ranch"]["spiele"]["herde"]["cleared"]["k1"].Value<bool>(), Is.True);
            Assert.That(g.SessionScore, Is.GreaterThan(0));
            g.Finish();
            Assert.That(g.FinalScore, Is.EqualTo(g.SessionScore));
        }
    }
}
