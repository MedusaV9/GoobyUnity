using Gooby.Core.Life;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The shake secret's detector (port of test_w13b_shake.gd's pure cases).</summary>
    public class ShakeLogicTests
    {
        private const long NowMs = 1_768_478_400_000L;
        private const double Dt = 1.0 / 60.0;
        private const double G = -9.8;

        /// <summary>Alternating leftover acceleration ±strength on X above gravity — deterministic.</summary>
        private static int Shake(ShakeLogic logic, double strength, double seconds)
        {
            var stage = logic.Stage;
            var n = (int)System.Math.Round(seconds / Dt);
            for (var i = 0; i < n; i++) stage = logic.Feed(strength * (i % 2 == 0 ? 1.0 : -1.0), G, 0.0, Dt);
            return stage;
        }

        private static int Rest(ShakeLogic logic, double seconds)
        {
            var stage = logic.Stage;
            for (var i = 0; i < (int)System.Math.Round(seconds / Dt); i++) stage = logic.Feed(0.0, G, 0.0, Dt);
            return stage;
        }

        [Test]
        public void LyingStillAndTiltingDoNothing()
        {
            var logic = new ShakeLogic();
            Assert.That(Rest(logic, 3.0), Is.EqualTo(0));
            Assert.That(logic.Energy, Is.EqualTo(0.0).Within(0.001));
            for (var i = 0; i < (int)System.Math.Round(3.0 / Dt); i++) logic.Feed(6.93, -6.93, 0.0, Dt);
            Assert.That(logic.Stage, Is.EqualTo(0), "tilting is no shaking");
            Assert.That(logic.Energy, Is.LessThan(ShakeLogic.Stage1Energy));
        }

        [Test]
        public void StagesEscalateDeterministically()
        {
            var logic = new ShakeLogic();
            logic.Feed(0.0, G, 0.0, Dt);
            Assert.That(Shake(logic, 6.5, 1.2), Is.EqualTo(1), "gentle for about a second");
            Assert.That(Shake(logic, 9.0, 1.3), Is.EqualTo(2), "hearty to about 2.5 s");
            Assert.That(Shake(logic, 14.0, 1.5), Is.EqualTo(3), "wild to about 4 s");
        }

        [Test]
        public void RestLetsItFallBack()
        {
            var logic = new ShakeLogic();
            logic.Feed(0.0, G, 0.0, Dt);
            Assert.That(Shake(logic, 6.5, 1.2), Is.EqualTo(1));
            var before = logic.Energy;
            Rest(logic, 1.0);
            Assert.That(logic.Energy, Is.LessThan(before));
            Assert.That(Rest(logic, 2.0), Is.EqualTo(0));
            Assert.That(logic.Energy, Is.EqualTo(0.0).Within(0.001));
        }

        [Test]
        public void ResetAndStageForEnergy()
        {
            Assert.That(ShakeLogic.StageForEnergy(0.0), Is.EqualTo(0));
            Assert.That(ShakeLogic.StageForEnergy(4.1), Is.EqualTo(1));
            Assert.That(ShakeLogic.StageForEnergy(9.1), Is.EqualTo(2));
            Assert.That(ShakeLogic.StageForEnergy(15.1), Is.EqualTo(3));
            var logic = new ShakeLogic();
            logic.Feed(0.0, G, 0.0, Dt);
            Shake(logic, 14.0, 3.0);
            logic.Reset();
            Assert.That(logic.Stage, Is.EqualTo(0));
            Assert.That(logic.Energy, Is.EqualTo(0.0));
        }

        [Test]
        public void TheCooldownIsTimeInjected()
        {
            Assert.That(ShakeLogic.CooldownReady(0, NowMs), Is.True, "never reached: ready");
            Assert.That(ShakeLogic.CooldownReady(NowMs, NowMs), Is.False);
            Assert.That(ShakeLogic.CooldownReady(NowMs, NowMs + ShakeLogic.CooldownMs - 1), Is.False, "9:59 still paused");
            Assert.That(ShakeLogic.CooldownReady(NowMs, NowMs + ShakeLogic.CooldownMs), Is.True, "10 min: free again");
        }
    }
}
