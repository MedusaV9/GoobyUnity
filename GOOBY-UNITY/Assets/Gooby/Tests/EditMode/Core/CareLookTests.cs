using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    public class CareLookTests
    {
        private const double Eps = 1e-9;

        [Test]
        public void SpeedSlowsWithTirednessAndIsCappedWhenPoorly()
        {
            Assert.That(CareLook.SpeedMult(0, 0.0), Is.EqualTo(1.0).Within(Eps));
            Assert.That(CareLook.SpeedMult(0, 1.0), Is.EqualTo(0.65).Within(Eps), "fully tired: −35 %");
            Assert.That(CareLook.SpeedMult(1, 0.0), Is.EqualTo(0.85).Within(Eps), "queasy caps at 0.85");
            Assert.That(CareLook.SpeedMult(2, 0.0), Is.EqualTo(0.6).Within(Eps), "sick caps at 0.6");
            Assert.That(CareLook.SpeedMult(2, 1.0), Is.EqualTo(0.6).Within(Eps), "never slower than sick");
            Assert.That(CareLook.SpeedMult(1, 0.8), Is.EqualTo(0.72).Within(Eps), "tiredness below the queasy cap wins");
        }

        [Test]
        public void LookFollowsTheGrade()
        {
            Assert.That(CareLook.PaleAmount(0), Is.EqualTo(0.0));
            Assert.That(CareLook.PaleAmount(1), Is.EqualTo(0.5));
            Assert.That(CareLook.PaleAmount(2), Is.EqualTo(1.0));
            Assert.That(CareLook.SniffleNose(0), Is.False);
            Assert.That(CareLook.SniffleNose(1), Is.True);
            Assert.That(CareLook.IcePack(1), Is.False);
            Assert.That(CareLook.IcePack(2), Is.True);
        }

        [Test]
        public void EyeBagsShowFromATiredThirdOrWhenPoorly()
        {
            Assert.That(CareLook.EyeBags(0, 0.2, false), Is.EqualTo(0.0), "a little tired: nothing yet");
            Assert.That(CareLook.EyeBags(0, 0.6, false), Is.EqualTo(0.6).Within(Eps));
            Assert.That(CareLook.EyeBags(1, 0.0, false), Is.EqualTo(0.45).Within(Eps), "poorly: at least 0.45");
            Assert.That(CareLook.EyeBags(0, 0.9, true), Is.EqualTo(0.0), "asleep the eyes are shut");
        }

        [Test]
        public void YawnsAndSneezesComeWhenAwake()
        {
            Assert.That(CareLook.Yawns(new CareLook.Input(0, 0.5, 50, false)), Is.True);
            Assert.That(CareLook.Yawns(new CareLook.Input(0, 0.2, 50, false)), Is.False);
            Assert.That(CareLook.Yawns(new CareLook.Input(0, 0.9, 50, true)), Is.False);
            Assert.That(CareLook.Sneezes(new CareLook.Input(1, 0.0, 50, false)), Is.True);
            Assert.That(CareLook.Sneezes(new CareLook.Input(0, 0.0, 50, false)), Is.False);
            Assert.That(CareLook.NextYawn(0.0), Is.EqualTo(18.0));
            Assert.That(CareLook.NextYawn(1.0), Is.EqualTo(32.0));
            Assert.That(CareLook.NextSneeze(2, 0.0), Is.EqualTo(12.0), "properly sick sneezes more often");
            Assert.That(CareLook.NextSneeze(1, 1.0), Is.EqualTo(40.0));
        }

        [Test]
        public void ReadsGradeTirednessWeightAndSleepFromTheSave()
        {
            var state = JObject.Parse(@"{""gooby"":{""stats"":{""hunger"":80,""energy"":9,""fun"":60,""hygiene"":70},
                ""health"":{""state"":""queasy""},""weight"":88,""sleep"":{""sleeping"":false}}}");
            var c = CareLook.Of(state);
            Assert.That(c.Grade, Is.EqualTo(1));
            Assert.That(c.Tired, Is.EqualTo((45.0 - 9.0) / 45.0).Within(Eps));
            Assert.That(c.Weight, Is.EqualTo(88.0));
            Assert.That(c.Sleeping, Is.False);
            var empty = CareLook.Of(new JObject());
            Assert.That(empty.Grade, Is.EqualTo(0));
            Assert.That(empty.Weight, Is.EqualTo(Weight.Default));
        }
    }
}
