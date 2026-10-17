using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Audio latency for the Tanzparty (port of dance_timing.gd): the heard song lags the game clock by the device's output
    /// latency; on top a manual fine offset from the metronome calibration (median of eight taps, clamped to ±150 ms)
    /// lives additively in <c>minigames.danceOffsetMs</c>. It never touches the judging or the score formula — the scene
    /// shifts its time line (note positions and hit window) by the total offset, the certification bot runs with 0.
    /// </summary>
    public static class DanceTiming
    {
        public const double ManualClampMs = 150.0, BaseClampMs = 500.0;
        public const int CalibrationBeats = 8;
        public const double CalibrationBeatSec = 0.6, CalibrationLeadInSec = 1.2;
        public const string SaveKey = "danceOffsetMs";

        /// <summary>Base latency (ms) from the output latency and the time since the last mix (s), capped at 500 ms.</summary>
        public static double BaseLatencyFrom(double outputLatencySec, double sinceMixSec)
        {
            var latency = outputLatencySec - Math.Min(Math.Max(sinceMixSec, 0.0), outputLatencySec);
            return Math.Max(0.0, Math.Min(BaseClampMs, latency * 1000.0));
        }

        public static double ClampManual(double ms) =>
            double.IsNaN(ms) || double.IsInfinity(ms) ? 0.0 : Math.Max(-ManualClampMs, Math.Min(ManualClampMs, ms));

        /// <summary>Total offset (ms): the capped base plus the clamped manual part.</summary>
        public static double TotalOffsetMs(double baseMs, double manualMs) => Math.Max(0.0, Math.Min(BaseClampMs, baseMs)) + ClampManual(manualMs);

        /// <summary>The scene's compensated time line: the song time minus the total offset.</summary>
        public static double PlayTime(double songTime, double baseMs, double manualMs) => songTime - TotalOffsetMs(baseMs, manualMs) / 1000.0;

        /// <summary>Median of the calibration taps; non-numbers are ignored (not counted as 0).</summary>
        public static double MedianMs(IEnumerable<double> values)
        {
            var clean = (values ?? Enumerable.Empty<double>()).Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (clean.Count == 0) return 0.0;
            var mid = clean.Count / 2;
            return clean.Count % 2 == 1 ? clean[mid] : (clean[mid - 1] + clean[mid]) * 0.5;
        }

        /// <summary>The calibration result: median of the taps minus the base latency already compensated, clamped ±150 ms.</summary>
        public static double ManualFromTaps(IReadOnlyCollection<double> tapDeltasMs, double baseMs)
        {
            if (tapDeltasMs == null || tapDeltasMs.Count == 0) return 0.0;
            return ClampManual(MedianMs(tapDeltasMs) - Math.Max(0.0, Math.Min(BaseClampMs, baseMs)));
        }

        /// <summary>A calibration tap at <paramref name="tapSec"/> matched to the nearest metronome beat: (beat 0…7, tap − beat in ms).</summary>
        public static (int beat, double deltaMs) TapDeltaMs(double tapSec)
        {
            var raw = (tapSec - CalibrationLeadInSec) / CalibrationBeatSec;
            var beat = Math.Max(0, Math.Min(CalibrationBeats - 1, (int)Math.Round(raw, MidpointRounding.AwayFromZero)));
            return (beat, (tapSec - (CalibrationLeadInSec + beat * CalibrationBeatSec)) * 1000.0);
        }

        public static double BeatTime(int beat) => CalibrationLeadInSec + beat * CalibrationBeatSec;

        public static double ManualOffsetFromState(JObject state) =>
            state?["minigames"] is JObject mg && J.IsNum(mg[SaveKey]) ? ClampManual(J.Num(mg[SaveKey])) : 0.0;

        public static void StoreManualOffset(JObject state, double ms) =>
            J.EnsureDict(state, "minigames")[SaveKey] = (int)Math.Round(ClampManual(ms), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The metronome calibration run (port of dance_calibration.gd's core): eight beats at the game's tempo after a short
    /// lead-in, one counted tap per beat, a grace after the last beat, then the result. Time-injected: <see cref="Advance"/>
    /// returns the beats that sounded in this step.
    /// </summary>
    public sealed class DanceCalibrationRun
    {
        public const double FinishGraceSec = 0.9;

        public bool Running { get; private set; }
        public bool Done { get; private set; }
        public double Clock { get; private set; }
        public int NextBeat { get; private set; }
        public List<double> TapDeltas { get; } = new List<double>();
        public int ResultMs { get; private set; }
        public double BaseLatencyMs { get; }

        private readonly HashSet<int> _tapped = new HashSet<int>();

        public DanceCalibrationRun(double baseLatencyMs) => BaseLatencyMs = baseLatencyMs;

        public void Start()
        {
            Running = true;
            Done = false;
            Clock = 0.0;
            NextBeat = 0;
            TapDeltas.Clear();
            _tapped.Clear();
        }

        /// <summary>Drives the clock; returns the beats (0…7) that ticked in this step.</summary>
        public List<int> Advance(double dt)
        {
            var ticks = new List<int>();
            if (!Running) return ticks;
            Clock += Math.Max(0.0, dt);
            while (NextBeat < DanceTiming.CalibrationBeats && Clock >= DanceTiming.BeatTime(NextBeat)) ticks.Add(NextBeat++);
            if (NextBeat >= DanceTiming.CalibrationBeats && Clock >= DanceTiming.BeatTime(DanceTiming.CalibrationBeats - 1) + FinishGraceSec)
            {
                Running = false;
                Done = true;
                ResultMs = (int)Math.Round(DanceTiming.ManualFromTaps(TapDeltas, BaseLatencyMs), MidpointRounding.AwayFromZero);
            }
            return ticks;
        }

        /// <summary>A tap now; one per beat counts.</summary>
        public bool RegisterTap()
        {
            if (!Running) return false;
            var (beat, delta) = DanceTiming.TapDeltaMs(Clock);
            if (!_tapped.Add(beat)) return false;
            TapDeltas.Add(delta);
            return true;
        }
    }
}
