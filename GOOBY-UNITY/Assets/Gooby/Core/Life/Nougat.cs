using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// The Nougatschleuse — the chocolate dispenser on the kitchen wall (port of nougat_logic.gd, web §B7/§C6.4). A
    /// crank turns one dollop out: refusals first (asleep, sick, the 30-minute pause — in that order), then +15 hunger,
    /// +10 fun, −8 hygiene, double junk through the health pipe, one junk meal of weight, +2 XP (level-up coins through
    /// the one money path), a jar of Nutella for the fridge and the nougatGlobs counter for the stickers. Godot's twist
    /// on the web: the machine gives a jar instead of using one up. Time always comes in from outside.
    /// </summary>
    public static class Nougat
    {
        public const int CooldownMin = 30;
        public const double Hunger = 15, Fun = 10, Hygiene = -8;
        public const int JunkEats = 2, WeightEats = 1, Xp = 2;
        public const string JarFoodId = "nutella";
        public const int Price = 400;
        public const string FurnitureId = "nougatschleuse";
        public const string Ok = "ok", Sleeping = "sleeping", Sick = "sick", Cooldown = "cooldown";

        public sealed class Result
        {
            public double HungerGain;
            public int Jars, Globs, Level, LevelsGained, CoinsAwarded;
        }

        /// <summary>easterEggs.nougat with its defaults, healed in place.</summary>
        public static JObject Slice(JObject state)
        {
            var eggs = J.EnsureDict(state, "easterEggs");
            if (!(eggs["nougat"] is JObject n))
            {
                n = new JObject { ["lastGlobAt"] = 0, ["installed"] = false };
                eggs["nougat"] = n;
            }
            return n;
        }

        public static bool IsInstalled(JObject state) => J.IsTrue(J.Path(state, "easterEggs", "nougat", "installed"));

        /// <summary>Placed on the wall: true only the first time (the room says so once).</summary>
        public static bool MarkInstalled(JObject state)
        {
            var n = Slice(state);
            if (J.IsTrue(n["installed"])) return false;
            n["installed"] = true;
            return true;
        }

        /// <summary>The pause left (0 = ready). A stamp more than one whole pause in the future counts as "just now".</summary>
        public static long CooldownRemainingMs(JObject state, long nowMs)
        {
            var last = (long)J.NumOr(J.Path(state, "easterEggs", "nougat", "lastGlobAt"), 0);
            if (last <= 0) return 0;
            var cooldown = CooldownMin * 60_000L;
            if (last > nowMs + cooldown) last = nowMs;
            return Math.Max(0, last + cooldown - nowMs);
        }

        public static string CanGlob(JObject state, long nowMs)
        {
            var gooby = state?["gooby"] as JObject;
            if (J.IsTrue(J.Path(gooby, "sleep", "sleeping"))) return Sleeping;
            if (J.Str(J.Path(gooby, "health", "state")) == "sick") return Sick;
            if (CooldownRemainingMs(state, nowMs) > 0) return Cooldown;
            return Ok;
        }

        /// <summary>One dollop with all its effects, in place; null when a refusal holds (checked again here).</summary>
        public static Result ApplyGlob(JObject state, long nowMs)
        {
            if (CanGlob(state, nowMs) != Ok || !(state["gooby"] is JObject gooby)) return null;
            var before = gooby["stats"] as JObject ?? new JObject();
            var after = Stats.ApplyDeltas(before, new Dictionary<string, double> { ["hunger"] = Hunger, ["fun"] = Fun, ["hygiene"] = Hygiene });
            gooby["stats"] = after;
            JToken health = gooby["health"];
            for (var i = 0; i < JunkEats; i++) health = Health.OnEat(health, true);
            gooby["health"] = health;
            var weight = J.NumOr(gooby["weight"], Weight.Default);
            for (var i = 0; i < WeightEats; i++) weight = Weight.OnEat(weight, true);
            gooby["weight"] = weight;
            var prog = J.EnsureDict(state, "progression");
            var res = Leveling.ApplyXp(prog, Xp);
            prog["xp"] = res.Xp;
            prog["level"] = res.Level;
            if (res.CoinsAwarded > 0) Economy.Award(J.EnsureDict(state, "economy"), res.CoinsAwarded, "levelUp");
            var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
            food[JarFoodId] = J.Int(food[JarFoodId]) + 1;
            var n = Slice(state);
            n["lastGlobAt"] = nowMs;
            n["installed"] = true;
            var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
            counters["nougatGlobs"] = J.Int(counters["nougatGlobs"]) + 1;
            return new Result
            {
                HungerGain = J.Num(after["hunger"]) - J.Num(before["hunger"]),
                Jars = J.Int(food[JarFoodId]),
                Globs = J.Int(counters["nougatGlobs"]),
                Level = res.Level,
                LevelsGained = res.LevelsGained,
                CoinsAwarded = res.CoinsAwarded,
            };
        }
    }
}
