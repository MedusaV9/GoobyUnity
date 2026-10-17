using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Gooby sagt — port of GOOBY/src/minigames/games/goobySays.logic.js (PLAN2 §C1.2 #1): sequence starts
    /// at 3 and grows by 1 per round, replay speeds up 5 %/round (floor 320 ms), a mistake ends the round,
    /// two-pad chords from round 6. Score = 10·rounds + speed bonus (0–8). Coin row /5, 4..24.
    /// </summary>
    public static class GoobySays
    {
        public sealed class Tune
        {
            public int Pads = 4, StartLen = 3, GrowPerRound = 1, RoundPoints = 10;
            public double StepDecayPct = 0.05, StepFloorMs = 320.0, StepBaseMs = 600.0;
            public int SpeedBonusMax = 8;
            public double ReactionFullMs = 500.0, ReactionZeroMs = 1500.0, InputTimeoutMs = 5000.0;
            public double AutoplayTapMs = 250.0, AutoplayErrRamp = 0.0025, AutoplayErrCap = 0.08;
            public int ChordFromRound = 6;
            public double ChordWindowMs = 250.0;
            public double AutoplayErrMult = 1.0;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Says = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double replaySpeed, windowMult, botErrorMult;
            int floorSteps;
            switch (mode)
            {
                case "easy": replaySpeed = 0.85; windowMult = 1.25; floorSteps = 0; botErrorMult = 0.75; break;
                case "hard":
                case "endless": replaySpeed = 1.15; windowMult = 0.8; floorSteps = 1; botErrorMult = 1.15; break;
                default: return tune;
            }
            var hardFloor = tune.StepFloorMs * Math.Pow(1.0 - tune.StepDecayPct, floorSteps);
            var o = tune.Clone();
            o.StepBaseMs = tune.StepBaseMs / replaySpeed;
            if (mode == "endless") o.StepFloorMs = 0.0;
            else if (mode == "easy") o.StepFloorMs = hardFloor / replaySpeed;
            else o.StepFloorMs = hardFloor;
            o.InputTimeoutMs = Math.Max(350.0, tune.InputTimeoutMs * windowMult);
            o.ChordWindowMs = mode == "hard" || mode == "endless"
                ? Math.Max(350.0, tune.ChordWindowMs * windowMult)
                : tune.ChordWindowMs * windowMult;
            o.ReactionFullMs = tune.ReactionFullMs * windowMult;
            o.ReactionZeroMs = tune.ReactionZeroMs * windowMult;
            o.AutoplayErrMult = botErrorMult;
            return o;
        }

        public static int SeqLengthAt(int roundNo) => Says.StartLen + Says.GrowPerRound * (Math.Max(1, roundNo) - 1);

        public static double StepMsAt(int roundNo, Tune tune)
        {
            var mult = Math.Pow(1.0 - tune.StepDecayPct, Math.Max(1, roundNo) - 1);
            return Math.Max(tune.StepFloorMs, tune.StepBaseMs * mult);
        }

        /// <summary>A sequence step: single pad (Second = -1) or a two-pad chord.</summary>
        public struct Step
        {
            public int First, Second;
            public bool IsChord => Second >= 0 && Second != First;
        }

        public static List<Step> ExtendSequence(List<Step> seq, GoobyRng rng, int roundNo = 1)
        {
            var pads = Says.Pads;
            var first = Math.Min(pads - 1, (int)Math.Floor(rng.Next() * pads));
            var outp = new List<Step>(seq);
            if (roundNo < Says.ChordFromRound)
            {
                outp.Add(new Step { First = first, Second = -1 });
                return outp;
            }
            var rolled = Math.Min(pads - 2, (int)Math.Floor(rng.Next() * (pads - 1)));
            var second = rolled >= first ? rolled + 1 : rolled;
            outp.Add(new Step { First = first, Second = second });
            return outp;
        }

        public static string ChordTapResult(Step step, int firstPad, int secondPad, double gapMs, Tune tune)
        {
            if (!step.IsChord || (step.First != firstPad && step.Second != firstPad)) return "wrong";
            if (secondPad < 0) return "waiting";
            if (secondPad == firstPad || (step.First != secondPad && step.Second != secondPad)) return "wrong";
            return gapMs <= tune.ChordWindowMs ? "complete" : "late";
        }

        public static double AutoplayErrAt(int roundNo, Tune tune) =>
            Math.Min(tune.AutoplayErrCap, tune.AutoplayErrRamp * (Math.Max(1, roundNo) - 1) * tune.AutoplayErrMult);

        public static int SpeedBonus(double avgReactionMs, Tune tune)
        {
            if (double.IsNaN(avgReactionMs) || double.IsInfinity(avgReactionMs)) return 0;
            if (avgReactionMs <= tune.ReactionFullMs) return tune.SpeedBonusMax;
            if (avgReactionMs >= tune.ReactionZeroMs) return 0;
            var span = tune.ReactionZeroMs - tune.ReactionFullMs;
            var t = (avgReactionMs - tune.ReactionFullMs) / span;
            return (int)Math.Round(tune.SpeedBonusMax * (1.0 - t), MidpointRounding.AwayFromZero);
        }

        public static int RoundScore(int roundsCompleted, double avgReactionMs, Tune tune) =>
            roundsCompleted <= 0 ? 0 : tune.RoundPoints * roundsCompleted + SpeedBonus(avgReactionMs, tune);

        public static bool EndlessShouldEnd(string mode, int mistakes) => mode == "endless" && mistakes >= 1;

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Says, mode);
            var rng = new GoobyRng(seed);
            var completed = 0;
            for (var roundNo = 1; roundNo <= 40; roundNo++)
            {
                var failed = false;
                var len = SeqLengthAt(roundNo);
                for (var step = 0; step < len; step++)
                {
                    if (rng.Next() < AutoplayErrAt(roundNo, tune)) { failed = true; break; }
                }
                if (failed) break;
                completed = roundNo;
            }
            var reactionMs = Math.Min(tune.ReactionFullMs, tune.AutoplayTapMs);
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["rounds"] = completed, ["score"] = RoundScore(completed, reactionMs, tune) };
        }
    }
}
