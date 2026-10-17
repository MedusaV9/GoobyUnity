using Gooby.Core.Minigames.Play;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Tanzparty latency calibration (port of test_w15_techkit.gd's dance cases).</summary>
    public class DanceTimingTests
    {
        private const double Eps = 1e-4;

        [Test]
        public void BaseLatency()
        {
            Assert.That(DanceTiming.BaseLatencyFrom(0.05, 0.0), Is.EqualTo(50.0).Within(Eps));
            Assert.That(DanceTiming.BaseLatencyFrom(0.05, 0.02), Is.EqualTo(30.0).Within(Eps), "time since the mix comes off");
            Assert.That(DanceTiming.BaseLatencyFrom(2.0, 0.0), Is.EqualTo(500.0).Within(Eps), "capped at 500 ms");
            Assert.That(DanceTiming.BaseLatencyFrom(0.05, -1.0), Is.EqualTo(50.0).Within(Eps));
            Assert.That(DanceTiming.BaseLatencyFrom(0.05, 9.0), Is.EqualTo(0.0).Within(Eps));
        }

        [Test]
        public void ClampAndTotalOffset()
        {
            Assert.That(DanceTiming.ClampManual(200.0), Is.EqualTo(150.0));
            Assert.That(DanceTiming.ClampManual(-999.0), Is.EqualTo(-150.0));
            Assert.That(DanceTiming.ClampManual(double.NaN), Is.EqualTo(0.0));
            Assert.That(DanceTiming.TotalOffsetMs(40.0, 30.0), Is.EqualTo(70.0).Within(Eps));
            Assert.That(DanceTiming.PlayTime(10.0, 40.0, 30.0), Is.EqualTo(10.0 - 0.07).Within(1e-6));
            Assert.That(DanceTiming.PlayTime(3.25, 0.0, 0.0), Is.EqualTo(3.25), "fresh: the identity (the bot plays with 0)");
        }

        [Test]
        public void MedianAndManualFromTaps()
        {
            Assert.That(DanceTiming.MedianMs(new[] { 10.0, 30.0, 20.0 }), Is.EqualTo(20.0));
            Assert.That(DanceTiming.MedianMs(new[] { 10.0, 20.0, 30.0, 40.0 }), Is.EqualTo(25.0));
            Assert.That(DanceTiming.MedianMs(new[] { double.NaN, 10.0 }), Is.EqualTo(10.0), "junk is ignored");
            Assert.That(DanceTiming.MedianMs(new double[0]), Is.EqualTo(0.0));
            Assert.That(DanceTiming.ManualFromTaps(new[] { 80.0, 90.0, 100.0 }, 40.0), Is.EqualTo(50.0).Within(Eps), "median 90 minus base 40");
            Assert.That(DanceTiming.ManualFromTaps(new[] { 400.0, 400.0, 400.0 }, 0.0), Is.EqualTo(150.0), "clamped");
            Assert.That(DanceTiming.ManualFromTaps(new double[0], 40.0), Is.EqualTo(0.0));
        }

        [Test]
        public void TapsMatchTheNearestBeat()
        {
            var lead = DanceTiming.CalibrationLeadInSec;
            var beat = DanceTiming.CalibrationBeatSec;
            var hit = DanceTiming.TapDeltaMs(lead + 2.0 * beat + 0.03);
            Assert.That(hit.beat, Is.EqualTo(2));
            Assert.That(hit.deltaMs, Is.EqualTo(30.0).Within(1e-3));
            Assert.That(DanceTiming.TapDeltaMs(0.0).beat, Is.EqualTo(0), "before the lead-in: beat 0");
            Assert.That(DanceTiming.TapDeltaMs(lead + 20.0 * beat).beat, Is.EqualTo(DanceTiming.CalibrationBeats - 1));
        }

        [Test]
        public void SaveRoundTrip()
        {
            var state = new JObject();
            DanceTiming.StoreManualOffset(state, 87.4);
            Assert.That((int)state["minigames"][DanceTiming.SaveKey], Is.EqualTo(87));
            Assert.That(DanceTiming.ManualOffsetFromState(state), Is.EqualTo(87.0));
            DanceTiming.StoreManualOffset(state, 400.0);
            Assert.That((int)state["minigames"][DanceTiming.SaveKey], Is.EqualTo(150), "clamped on write");
            Assert.That(DanceTiming.ManualOffsetFromState(JObject.Parse(@"{""minigames"":""kaputt""}")), Is.EqualTo(0.0));
            Assert.That(DanceTiming.ManualOffsetFromState(new JObject()), Is.EqualTo(0.0));
        }

        [Test]
        public void ACalibrationRunWithTapsFiftyMsLate()
        {
            var run = new DanceCalibrationRun(20.0);
            run.Start();
            Assert.That(run.Running, Is.True);
            var ticks = 0;
            for (var i = 0; i < DanceTiming.CalibrationBeats; i++)
            {
                ticks += run.Advance(DanceTiming.BeatTime(i) + 0.05 - run.Clock).Count;
                Assert.That(run.RegisterTap(), Is.True);
                Assert.That(run.RegisterTap(), Is.False, "a double tap on the same beat does not count");
            }
            Assert.That(ticks, Is.EqualTo(DanceTiming.CalibrationBeats), "every beat ticked once");
            Assert.That(run.TapDeltas.Count, Is.EqualTo(DanceTiming.CalibrationBeats));
            run.Advance(2.0);
            Assert.That(run.Running, Is.False);
            Assert.That(run.Done, Is.True);
            Assert.That(run.ResultMs, Is.EqualTo(30), "median 50 minus base 20");
        }
    }
}
