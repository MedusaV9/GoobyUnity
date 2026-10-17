using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Burger-Bau — port of GOOBY/src/minigames/games/burgerBuild.logic.js (§C1.2 #3 / §C10.2 / §G5.3):
    /// seeded 4–7-layer tickets, ingredients rain in 3 columns, right layer +5, wrong −2, finished burger +15,
    /// fall speed +8 % per burger. 75 s round, coin row /4, 4..26.
    /// </summary>
    public static class BurgerBuild
    {
        public sealed class Tune
        {
            public double DurationSec = 75.0;
            public int Columns = 3, MinLayers = 4, MaxLayers = 7;
            public double CatchPts = 5.0, WrongPts = -2.0, CompletePts = 15.0;
            public double FallRampPct = 0.08, FallBaseSpeed = 2.1, SpawnSec = 1.5, NextWeight = 0.24, ForceNextSec = 6.0, BiteSec = 2.0;
            public double AutoplayTickSec = 0.3, AutoplayDistract = 0.42;
            public int[] RushOrderNumbers = { 2, 4 };
            public double RushScoreMult = 1.5, OrderTimerSec = 30.0, RushTimerMult = 0.8;
            public int MaxRushOrders = 2;
            public double PlateHalfWidth = 0.78;
            public bool Endless = false;
            public int EndlessExpires = 3;
            public double BotSkill = 0.95;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Burger = new Tune();
        public static readonly string[] Ingredients = { "patty", "cheese", "tomato", "salad", "onion" };
        public static readonly string[] FallingIds = { "bun", "patty", "cheese", "tomato", "salad", "onion" };

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double spawnMult, windowMult, durationMult, botSkill, distract;
            switch (mode)
            {
                case "easy": spawnMult = 1.2; windowMult = 1.25; durationMult = 1.2; botSkill = 0.99; distract = 0.25; break;
                case "hard":
                case "endless": spawnMult = 0.85; windowMult = 0.8; durationMult = 1.0; botSkill = 0.55; distract = 0.34; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * durationMult;
            o.SpawnSec = tune.SpawnSec * spawnMult;
            o.OrderTimerSec = Math.Max(0.35, tune.OrderTimerSec * windowMult);
            o.PlateHalfWidth = Math.Max(tune.PlateHalfWidth * 0.55, tune.PlateHalfWidth * windowMult);
            o.Endless = mode == "endless";
            o.BotSkill = botSkill;
            o.AutoplayDistract = distract;
            o.Mode = mode;
            return o;
        }

        public static List<string> MakeTicket(GoobyRng rng)
        {
            var span = Burger.MaxLayers - Burger.MinLayers + 1;
            var total = Burger.MinLayers + (int)Math.Floor(rng.Next() * span);
            var layers = new List<string> { "bun" };
            for (var i = 0; i < total - 2; i++)
            {
                var idx = Math.Min(Ingredients.Length - 1, (int)Math.Floor(rng.Next() * Ingredients.Length));
                layers.Add(Ingredients[idx]);
            }
            layers.Add("bun");
            return layers;
        }

        public static string NextNeeded(List<string> ticket, int placed) => placed >= 0 && placed < ticket.Count ? ticket[placed] : "";
        public static bool IsComplete(List<string> ticket, int placed) => placed >= ticket.Count;

        public static double FallSpeedAt(int completedBurgers, Tune tune) =>
            tune.FallBaseSpeed * Math.Pow(1.0 + tune.FallRampPct, Math.Max(0, completedBurgers));

        public static double[] ColumnCenters(double halfW)
        {
            var spacing = Math.Max(0.0, Math.Min(2.1, halfW - 0.95));
            return new[] { -spacing, 0.0, spacing };
        }

        public static bool IsRushOrder(int orderNumber) => Array.IndexOf(Burger.RushOrderNumbers, orderNumber) >= 0;

        public static double OrderTimerSec(bool rush, Tune tune) => tune.OrderTimerSec * (rush ? tune.RushTimerMult : 1.0);

        public static double OrderPoints(double points, bool rush) => points > 0.0 && rush ? points * Burger.RushScoreMult : points;

        public static string RollSpawn(GoobyRng rng, string needed, double sinceNeededSec, Tune tune)
        {
            if (needed.Length > 0 && (sinceNeededSec >= tune.ForceNextSec || rng.Next() < tune.NextWeight)) return needed;
            var idx = Math.Min(FallingIds.Length - 1, (int)Math.Floor(rng.Next() * FallingIds.Length));
            return FallingIds[idx];
        }

        public static double ApplyCatch(double score, bool correct, bool rush, Tune tune)
        {
            var points = correct ? OrderPoints(tune.CatchPts, rush) : tune.WrongPts;
            return Math.Max(0.0, score + points);
        }

        public static bool EndlessShouldEnd(int expired, Tune tune) => tune.Endless && expired >= tune.EndlessExpires;

        public static JObject SimulateAutoplay(int seed, string mode = "normal")
        {
            var tune = ApplyDifficulty(Burger, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            var score = 0.0;
            int completed = 0, expired = 0, orderNumber = 1;
            var limit = tune.Endless ? 600.0 : tune.DurationSec;
            while (elapsed < limit && !EndlessShouldEnd(expired, tune))
            {
                var ticket = MakeTicket(rng);
                var rush = IsRushOrder(orderNumber);
                var skill = tune.BotSkill;
                var buildSec = ticket.Count * tune.SpawnSec * (1.45 + rng.Next() * 0.25) / skill;
                var deadline = OrderTimerSec(rush, tune);
                if (buildSec <= deadline)
                {
                    elapsed += buildSec + tune.BiteSec;
                    score += OrderPoints(ticket.Count * tune.CatchPts + tune.CompletePts, rush);
                    completed += 1;
                }
                else
                {
                    elapsed += deadline;
                    expired += 1;
                }
                orderNumber += 1;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["completed"] = completed, ["expired"] = expired };
        }
    }
}
