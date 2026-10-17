using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Tanzparty — port of GOOBY/src/minigames/games/danceParty.logic.js + DANCE constants (§C6.1 #9): 100 BPM,
    /// seeded 75-s chart from PATTERN_SEED, 3 lanes, window ≤ 70 ms perfect (+4) / ≤ 140 ms good (+2), a miss
    /// breaks the combo and costs 2, encore doubles points for 5 s.
    /// </summary>
    public static class DanceParty
    {
        public const double Bpm = 100.0;
        public const int PatternSeed = 1002026;
        public const int Lanes = 3;
        public const int PerfectPts = 4, GoodPts = 2, MissPenalty = 2;

        public sealed class Tune
        {
            public double DurationSec = 75.0, PerfectMs = 70.0, GoodMs = 140.0;
            public bool Endless = false;
            public int EndlessBreakLimit = 3, RampFloorStep = 0;
            public double LeadInSec = 2.4, NoteTravelSec = 1.6;
            public int SlotsPerBeat = 2;
            public double MinGapSec = 0.3, LaneGapSec = 0.6, DensityStart = 0.3, DensityEnd = 0.55, OffbeatMult = 0.5;
            public int StartBeat = 4;
            public double TailSec = 2.0, LaneRepeatChance = 0.3;
            public int[] TierCombos = { 4, 8, 16 };
            public double EndDelaySec = 1.6;
            public int EncorePerfects = 5;
            public double EncoreSec = 5.0, DriftMaxFrameGapSec = 2.0;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Tuning = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var speedMult = hard ? 1.15 : 0.85;
            var windowMult = hard ? 0.8 : 1.25;
            var o = tune.Clone();
            o.DensityStart = tune.DensityStart * speedMult;
            o.DensityEnd = tune.DensityEnd * speedMult;
            o.NoteTravelSec = tune.NoteTravelSec / speedMult;
            o.PerfectMs = tune.PerfectMs * windowMult;
            o.GoodMs = tune.GoodMs * windowMult;
            o.RampFloorStep = hard ? -1 : 0;
            o.Endless = mode == "endless";
            return o;
        }

        public static Tune WithHitbox(Tune tune, double hitboxMult = 1.0)
        {
            var mult = !double.IsNaN(hitboxMult) && !double.IsInfinity(hitboxMult) && hitboxMult > 0.0 ? hitboxMult : 1.0;
            if (mult == 1.0) return tune;
            var o = tune.Clone();
            o.PerfectMs = tune.PerfectMs * mult;
            o.GoodMs = tune.GoodMs * mult;
            return o;
        }

        public struct Note
        {
            public double Time;
            public int Lane, Slot;
            public bool Hit, Missed;
        }

        /// <summary>Seeded chart (§C6.1 #9): eighth grid at bpm, density ramp, weighted lane walk, global + per-lane gaps.</summary>
        public static List<Note> GeneratePattern(int seed, Tune tune, double bpm = Bpm)
        {
            var duration = tune.DurationSec;
            var rng = new GoobyRng(seed);
            var beatSec = 60.0 / bpm;
            var slotSec = beatSec / tune.SlotsPerBeat;
            var startSlot = tune.StartBeat * tune.SlotsPerBeat;
            var endTime = duration - tune.TailSec;
            var totalSlots = (int)Math.Floor(endTime / slotSec);
            var notes = new List<Note>();
            var lastTime = double.NegativeInfinity;
            var lastLaneTime = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            var lane = (int)Math.Floor(rng.Next() * Lanes);
            for (var slot = startSlot; slot <= totalSlots; slot++)
            {
                var time = slot * slotSec;
                if (time > endTime) break;
                var density = DensityAt(time / duration, slot, tune);
                if (rng.Next() >= density) continue;
                if (time - lastTime < tune.MinGapSec - 1e-9) continue;
                if (rng.Next() >= tune.LaneRepeatChance) lane = WalkLane(lane, rng, Lanes);
                var chosen = FirstFreeLane(lane, time, lastLaneTime, tune, Lanes);
                if (chosen == -1) continue;
                lane = chosen;
                notes.Add(new Note { Time = time, Lane = lane, Slot = slot });
                lastTime = time;
                lastLaneTime[lane] = time;
            }
            return notes;
        }

        private static double DensityAt(double t, int slot, Tune tune)
        {
            var density = tune.DensityStart + (tune.DensityEnd - tune.DensityStart) * t;
            if (slot % tune.SlotsPerBeat != 0) density *= tune.OffbeatMult;
            return density;
        }

        private static int WalkLane(int lane, GoobyRng rng, int lanes)
        {
            if (lane == 0) return 1;
            if (lane == lanes - 1) return lanes - 2;
            return lane + (rng.Next() < 0.5 ? -1 : 1);
        }

        private static int FirstFreeLane(int lane, double time, double[] lastLaneTime, Tune tune, int lanes)
        {
            for (var k = 0; k < lanes; k++)
            {
                var cand = (lane + k) % lanes;
                if (time - lastLaneTime[cand] >= tune.LaneGapSec - 1e-9) return cand;
            }
            return -1;
        }

        public static long SegmentSeed(int segment)
        {
            if (segment == 0) return PatternSeed;
            return unchecked((uint)(PatternSeed + GoobyRng.Imul(unchecked((uint)segment), 0x9e3779b1u)));
        }

        public static int SectionIndex(double songTime) => (int)Math.Floor(songTime / 12.0);

        public static string ClassifyHit(double deltaSec, Tune tune)
        {
            var ms = Math.Abs(deltaSec) * 1000.0;
            if (ms <= tune.PerfectMs) return "perfect";
            if (ms <= tune.GoodMs) return "good";
            return "";
        }

        public static int JudgeTap(List<Note> notes, int lane, double songTime, Tune tune)
        {
            var best = -1;
            var bestAbs = double.PositiveInfinity;
            var window = tune.GoodMs / 1000.0;
            for (var i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.Lane != lane || n.Hit || n.Missed) continue;
                if (n.Time - songTime > window) break;
                var d = Math.Abs(n.Time - songTime);
                if (d <= window && d < bestAbs) { best = i; bestAbs = d; }
            }
            return best;
        }

        public sealed class Tally
        {
            public int Perfect, Good, Miss, Combo, MaxCombo, Bonus;
            public JObject ToJson() => new JObject { ["perfect"] = Perfect, ["good"] = Good, ["miss"] = Miss, ["combo"] = Combo, ["maxCombo"] = MaxCombo, ["bonus"] = Bonus };
        }

        public static Tally ApplyJudgment(Tally tally, string kind)
        {
            if (kind == "miss")
            {
                tally.Miss += 1;
                tally.Combo = 0;
                return tally;
            }
            if (kind == "perfect") tally.Perfect += 1; else tally.Good += 1;
            tally.Combo += 1;
            if (tally.Combo > tally.MaxCombo) tally.MaxCombo = tally.Combo;
            return tally;
        }

        public static int Score(Tally t) => Math.Max(0, t.Perfect * PerfectPts + t.Good * GoodPts - t.Miss * MissPenalty + t.Bonus);

        public static int ComboTier(int combo)
        {
            var tiers = Tuning.TierCombos;
            if (combo >= tiers[2]) return 3;
            if (combo >= tiers[1]) return 2;
            if (combo >= tiers[0]) return 1;
            return 0;
        }

        public sealed class FeverChain
        {
            public int Perfects, Encores;
            public double EncoreUntil = double.NegativeInfinity;
        }

        public static bool EncoreActive(FeverChain chain, double songTime) => songTime < chain.EncoreUntil;

        public static (bool active, bool started) AdvanceFeverChain(FeverChain chain, string kind, int combo, double songTime)
        {
            if (EncoreActive(chain, songTime)) return (true, false);
            if (kind != "perfect" || ComboTier(combo) < 3)
            {
                chain.Perfects = 0;
                return (false, false);
            }
            chain.Perfects += 1;
            if (chain.Perfects < Tuning.EncorePerfects) return (false, false);
            chain.Perfects = 0;
            chain.EncoreUntil = songTime + Tuning.EncoreSec;
            chain.Encores += 1;
            return (true, true);
        }

        public static int EncoreBonus(string kind, bool active)
        {
            if (!active || kind == "miss") return 0;
            return kind == "perfect" ? PerfectPts : GoodPts;
        }

        public static string NoteLifecycle(double noteTime, double songTime, Tune tune)
        {
            if (songTime - noteTime > tune.GoodMs / 1000.0) return "expired";
            if (noteTime - songTime <= tune.NoteTravelSec) return "visible";
            return "future";
        }

        public static JObject SimulateAutoplay(int seed, string mode = "normal")
        {
            var tune = ApplyDifficulty(Tuning, mode);
            var notes = GeneratePattern(PatternSeed, tune);
            var rng = new GoobyRng(seed);
            var tally = new Tally();
            var missChance = mode == "easy" ? 0.04 : (mode == "hard" || mode == "endless" ? 0.1 : 0.08);
            foreach (var _ in notes)
            {
                if (rng.Next() < missChance)
                {
                    ApplyJudgment(tally, "miss");
                    continue;
                }
                var error = (rng.Next() + rng.Next() + rng.Next() - 1.5) * 0.16;
                var kind = ClassifyHit(error, tune);
                ApplyJudgment(tally, kind.Length > 0 ? kind : "miss");
            }
            return new JObject { ["score"] = Score(tally), ["tally"] = tally.ToJson() };
        }
    }
}
