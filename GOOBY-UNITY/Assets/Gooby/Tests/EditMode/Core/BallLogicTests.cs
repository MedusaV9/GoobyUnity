using Gooby.Core.Home;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The living-room ball (port of test_w13_ball.gd): flicks map to a capped arc that always goes up, the ball bounces to
    /// rest inside its box, a throw in reach becomes a fetch with a head-nudge home and a cooldown, and the reward is
    /// +3 fun, −0.2 weight and one more ball.
    /// </summary>
    public class BallLogicTests
    {
        [Test]
        public void FlicksBecomeACappedArcThatAlwaysRises()
        {
            var (x, y, z) = BallLogic.FlickToVelocity(0, 0);
            Assert.That(y, Is.EqualTo(0.8).Within(1e-9), "even a flat flick flies");
            Assert.That(x, Is.EqualTo(0.0));
            var hard = BallLogic.FlickToVelocity(5000, -9000);
            Assert.That(System.Math.Sqrt(hard.x * hard.x + hard.y * hard.y + hard.z * hard.z), Is.EqualTo(BallLogic.MaxSpeed).Within(1e-6));
            Assert.That(hard.z, Is.GreaterThan(0), "upward flicks throw away from the camera");
        }

        [Test]
        public void AThrowInReachTurnsIntoAFetchAndACooldown()
        {
            var ball = new BallLogic();
            Assert.That(ball.Werfen(200, -700), Is.True);
            Assert.That(ball.Werfen(200, -700), Is.False, "no second throw mid-air");
            var rested = false;
            for (var i = 0; i < 2000 && !rested; i++) rested = ball.Step(1.0 / 60.0).resting;
            Assert.That(rested, Is.True);
            Assert.That(System.Math.Abs(ball.X), Is.LessThanOrEqualTo(BallLogic.BoundX + 1e-9));
            Assert.That(ball.Z, Is.InRange(BallLogic.BoundZMin, BallLogic.BoundZMax));
            Assert.That(ball.LandungVerarbeiten(1.2, 1000), Is.EqualTo(BallLogic.LandungApport));
            Assert.That(ball.Kopfstoss(1000), Is.True);
            Assert.That(ball.Zustand, Is.EqualTo(BallLogic.BringtZurueck));
            rested = false;
            for (var i = 0; i < 2000 && !rested; i++) rested = ball.Step(1.0 / 60.0).resting;
            Assert.That(ball.LandungVerarbeiten(0.5, 2000), Is.EqualTo(BallLogic.LandungHeim));
            Assert.That(ball.Zustand, Is.EqualTo(BallLogic.Ruht));
            ball.Werfen(0, -500);
            for (var i = 0; i < 2000 && ball.Zustand == BallLogic.Fliegt; i++) if (ball.Step(1.0 / 60.0).resting) break;
            Assert.That(ball.LandungVerarbeiten(1.0, 5000), Is.EqualTo(BallLogic.LandungLiegen), "cooldown: 15 s between fetches");
            ball.Werfen(0, -500);
            for (var i = 0; i < 2000; i++) if (ball.Step(1.0 / 60.0).resting) break;
            Assert.That(ball.LandungVerarbeiten(9.0, 99_000), Is.EqualTo(BallLogic.LandungLiegen), "out of reach");
        }

        [Test]
        public void TheFetchRewardsFunWeightAndCount()
        {
            var state = new JObject { ["gooby"] = new JObject { ["stats"] = new JObject { ["fun"] = 50.0 }, ["weight"] = 60.0 } };
            var gained = BallLogic.ApplyApportReward(state);
            Assert.That(gained, Is.EqualTo(BallLogic.BallFun).Within(1e-9));
            Assert.That(J.Num(J.Path(state, "gooby", "stats", "fun")), Is.EqualTo(53.0).Within(1e-9));
            Assert.That(J.Num(J.Path(state, "gooby", "weight")), Is.EqualTo(59.8).Within(1e-9));
            Assert.That(J.Int(J.Path(state, "achievements", "counters", "balls")), Is.EqualTo(1));
        }
    }
}
