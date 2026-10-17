using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>
    /// The guided first quarter hour (port of onboarding_guide_logic.gd, web onboarding.js §D2): after moving in, nine
    /// steps that explain the basics by doing them — pet, feed, wash, first coins, first round, first purchase, first
    /// sticker — between a manual welcome and a manual outlook. A step counts only for what happened since it began:
    /// its baseline freezes the existing save counters (achievements.counters, economy, minigames.plays, stickers), no
    /// event bus of its own. Progress lives additively in onboarding.guide {done, skipped, step, base} and resumes after a
    /// restart; saves that are no longer fresh are marked done quietly — nobody gets tutored after the fact.
    /// </summary>
    public static class OnboardingGuide
    {
        public const string Manuell = "manuell", Auto = "auto";

        public sealed class Step
        {
            public readonly string Id, Art;
            public Step(string id, string art) { Id = id; Art = art; }
            public bool Manual => Art == Manuell;
        }

        public static readonly IReadOnlyList<Step> Steps = new[]
        {
            new Step("ankunft", Manuell), new Step("streicheln", Auto), new Step("fuettern", Auto), new Step("waschen", Auto),
            new Step("muenzen", Auto), new Step("minispiel", Auto), new Step("moebel", Auto), new Step("sticker", Auto),
            new Step("ausblick", Manuell),
        };

        /// <summary>Fresh enough for the tour: level, coins ever earned and rounds played still at the very start.</summary>
        public const int FrischMaxLevel = 2, FrischMaxEarned = 150, FrischMaxPlays = 3;

        public static JObject SliceOf(JObject state)
        {
            if (J.Path(state, "onboarding", "guide") is JObject g) return g;
            return new JObject { ["done"] = false, ["skipped"] = false, ["step"] = 0, ["base"] = new JObject() };
        }

        private static JObject EnsureSlice(JObject state)
        {
            var onboarding = J.EnsureDict(state, "onboarding");
            if (!(onboarding["guide"] is JObject g))
            {
                g = new JObject { ["done"] = false, ["skipped"] = false, ["step"] = 0, ["base"] = new JObject() };
                onboarding["guide"] = g;
            }
            return g;
        }

        public static bool Finished(JObject state)
        {
            var g = SliceOf(state);
            return J.IsTrue(g["done"]) || J.IsTrue(g["skipped"]);
        }

        /// <summary>Does the tour run for this save? Moving in is done, the tour neither finished nor skipped, the save fresh.</summary>
        public static bool ShouldStart(JObject state) =>
            J.IsTrue(J.Path(state, "onboarding", "done")) && !Finished(state) && IsFresh(state);

        /// <summary>The tour is on: moved in, not finished — the coach and other hints keep quiet meanwhile.</summary>
        public static bool Active(JObject state) =>
            J.IsTrue(J.Path(state, "onboarding", "done")) && !Finished(state) && J.Path(state, "onboarding", "guide") is JObject;

        public static bool IsFresh(JObject state) =>
            J.Int(J.Path(state, "progression", "level"), 1) <= FrischMaxLevel
            && Econ(state, "coinsEarned") <= FrischMaxEarned
            && PlaysSum(state) <= FrischMaxPlays;

        /// <summary>The step the tour stands on (null once it is over).</summary>
        public static Step Current(JObject state)
        {
            if (Finished(state)) return null;
            var i = J.Int(SliceOf(state)["step"]);
            return i >= 0 && i < Steps.Count ? Steps[i] : null;
        }

        public static int StepIndex(JObject state) => J.Int(SliceOf(state)["step"]);

        /// <summary>Counters frozen at a step's start.</summary>
        public static JObject Snapshot(JObject state) => new JObject
        {
            ["pets"] = Counter(state, "petsToday"),
            ["feeds"] = Counter(state, "feeds"),
            ["washes"] = Counter(state, "washes"),
            ["earned"] = Econ(state, "coinsEarned"),
            ["spent"] = Econ(state, "coinsSpent"),
            ["plays"] = PlaysSum(state),
            ["sticker"] = StickerCount(state),
        };

        /// <summary>Is the step done against its baseline? Manual steps never (their button confirms them).</summary>
        public static bool Satisfied(string stepId, JObject baseline, JObject state)
        {
            switch (stepId)
            {
                case "streicheln":
                    // petsToday resets with the day: any change to at least one is a real pet since the step began
                    var pets = Counter(state, "petsToday");
                    return pets >= 1 && pets != J.Int(baseline?["pets"]);
                case "fuettern": return Counter(state, "feeds") > J.Int(baseline?["feeds"]);
                case "waschen": return Counter(state, "washes") > J.Int(baseline?["washes"]);
                case "muenzen": return Econ(state, "coinsEarned") > J.Int(baseline?["earned"]);
                case "minispiel": return PlaysSum(state) > J.Int(baseline?["plays"]);
                case "moebel": return Econ(state, "coinsSpent") > J.Int(baseline?["spent"]);
                case "sticker": return StickerCount(state) > 0;
                default: return false;
            }
        }

        /// <summary>The tour starts (or resumes): the current step's baseline is taken if it has none yet.</summary>
        public static void Begin(JObject state)
        {
            var g = EnsureSlice(state);
            if (!(g["base"] is JObject b) || !b.HasValues) g["base"] = Snapshot(state);
        }

        /// <summary>On to the next step with a fresh baseline; past the last one the tour is done. Returns the new step or null.</summary>
        public static Step Advance(JObject state)
        {
            var g = EnsureSlice(state);
            var next = J.Int(g["step"]) + 1;
            if (next >= Steps.Count)
            {
                g["done"] = true;
                g["step"] = Steps.Count;
                return null;
            }
            g["step"] = next;
            g["base"] = Snapshot(state);
            return Steps[next];
        }

        /// <summary>"Tour beenden": the tour stops for good (skipped and done, it never comes back).</summary>
        public static void Skip(JObject state)
        {
            var g = EnsureSlice(state);
            g["skipped"] = true;
            g["done"] = true;
        }

        public const string DecideNone = "none", DecideStart = "start", DecideResume = "resume", DecideQuiet = "quiet";

        /// <summary>
        /// What the home screen does with the tour (onboarding_guide.gd attach_to): nothing before moving in or once it is
        /// over, "resume" for a tour already under way (even if the save is no longer fresh), "start" for a fresh save,
        /// "quiet" for an older save (the caller marks it done quietly).
        /// </summary>
        public static string Decide(JObject state)
        {
            if (!J.IsTrue(J.Path(state, "onboarding", "done")) || Finished(state)) return DecideNone;
            if (J.Path(state, "onboarding", "guide") is JObject g && (J.Int(g["step"]) > 0 || (g["base"] is JObject b && b.HasValues))) return DecideResume;
            return IsFresh(state) ? DecideStart : DecideQuiet;
        }

        /// <summary>A save that is no longer fresh never sees the tour (and is not asked again).</summary>
        public static void MarkDoneQuietly(JObject state) => EnsureSlice(state)["done"] = true;

        public static int StickerCount(JObject state) => J.Path(state, "stickers", "unlocked") is JObject u ? u.Count : 0;

        private static int Counter(JObject state, string key) => J.Int(J.Path(state, "achievements", "counters", key));

        private static int Econ(JObject state, string key) => (int)J.Num(J.Path(state, "economy", key));

        private static int PlaysSum(JObject state)
        {
            if (!(J.Path(state, "minigames", "plays") is JObject plays)) return 0;
            var total = 0;
            foreach (var p in plays.Properties()) total += (int)J.Num(p.Value);
            return total;
        }
    }
}
