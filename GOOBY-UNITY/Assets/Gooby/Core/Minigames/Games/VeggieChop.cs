using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Gemüse-Schnippler — port of GOOBY/src/minigames/games/veggieChop.logic.js (PLAN2 §C1.2 #4): veggies fly up
    /// in arcs (1–3 at once, ramping), swipe-cut +2 (+1 per further piece in the same swipe), junk (can/boot)
    /// −3 + 0.5 s stun, three missed veggies end early, ≤ 60 s. Every 25 s a frenzy with exactly 8 veggies, no junk.
    /// </summary>
    public static class VeggieChop
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0;
            public int ChopPts = 2, ComboBonus = 1, JunkPts = -3;
            public double StunSec = 0.5;
            public int MaxMisses = 3;
            public double Wave2FromSec = 20.0, Wave3FromSec = 40.0, SpawnStartSec = 2.3, SpawnEndSec = 1.7;
            public double JunkChanceStart = 0.1, JunkChanceEnd = 0.22, Gravity = 9.5, ApexMinY = -0.4, ApexMaxY = 2.3, HitRadius = 0.42;
            public double AutoplayChopRate = 0.965, AutoplayAimErr = 0.14;
            public double FrenzyEverySec = 25.0, FrenzyDurationSec = 3.0;
            public int FrenzyItems = 8;
            public bool Endless = false;
            public int EndlessJunkHits = 3;
            public double SpeedMult = 1.0, ScoreMult = 1.0, EndlessBotJunkRate = 0.55;
            public double? EndlessSpawnFloorSec;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Chop = new Tune();

        public struct Veggie
        {
            public string Key, Half, Juice;
        }

        public static readonly Veggie[] Veggies =
        {
            new Veggie { Key = "apple", Half = "apple-half", Juice = "E85D4A" }, new Veggie { Key = "pear", Half = "pear-half", Juice = "B3D06B" },
            new Veggie { Key = "lemon", Half = "lemon-half", Juice = "FFE066" }, new Veggie { Key = "onion", Half = "onion-half", Juice = "F2E8DA" },
            new Veggie { Key = "mushroom", Half = "mushroom-half", Juice = "E8D9C5" }, new Veggie { Key = "paprika", Half = "paprika-slice", Juice = "FF8552" },
            new Veggie { Key = "tomato", Half = "tomato-slice", Juice = "E8523F" }, new Veggie { Key = "coconut", Half = "coconut-half", Juice = "FFFFFF" },
        };

        public static readonly string[] JunkItems = { "soda", "boot" };

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double spawnMult, windowMult, durationMult, botRate;
            switch (mode)
            {
                case "easy": spawnMult = 1.2; windowMult = 1.25; durationMult = 1.2; botRate = 0.99; break;
                case "hard":
                case "endless": spawnMult = 0.85; windowMult = 0.8; durationMult = 1.0; botRate = 0.81; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * durationMult;
            o.SpawnStartSec = tune.SpawnStartSec * spawnMult;
            o.SpawnEndSec = tune.SpawnEndSec * spawnMult;
            o.HitRadius = Math.Max(tune.HitRadius * 0.55, tune.HitRadius * windowMult);
            o.AutoplayChopRate = botRate;
            o.Endless = mode == "endless";
            o.EndlessSpawnFloorSec = 0.8;
            return o;
        }

        public static Tune ApplyTurbo(Tune tune, double speedMult = 1.0, double scoreMult = 1.0)
        {
            var o = tune.Clone();
            o.SpeedMult = Math.Max(1.0, speedMult);
            o.ScoreMult = Math.Max(1.0, scoreMult);
            return o;
        }

        public static int MaxWaveSizeAt(double elapsed)
        {
            if (elapsed >= Chop.Wave3FromSec) return 3;
            if (elapsed >= Chop.Wave2FromSec) return 2;
            return 1;
        }

        public static int WaveSizeAt(GoobyRng rng, double elapsed) => 1 + (int)Math.Floor(rng.Next() * MaxWaveSizeAt(elapsed));

        public static double SpawnIntervalAt(double elapsed, double duration, Tune tune)
        {
            var t = tune.Endless ? Math.Max(0.0, elapsed / duration) : Math.Min(1.0, Math.Max(0.0, elapsed / duration));
            var value = tune.SpawnStartSec + (tune.SpawnEndSec - tune.SpawnStartSec) * t;
            return Math.Max(tune.EndlessSpawnFloorSec ?? tune.SpawnEndSec, value);
        }

        public static double JunkChanceAt(double elapsed, double duration, Tune tune)
        {
            var t = Math.Min(1.0, Math.Max(0.0, elapsed / duration));
            return tune.JunkChanceStart + (tune.JunkChanceEnd - tune.JunkChanceStart) * t;
        }

        public struct Item
        {
            public string Kind, Key, Half, Juice;
        }

        public static Item RollItem(GoobyRng rng, double elapsed, Tune tune)
        {
            if (rng.Next() < JunkChanceAt(elapsed, tune.DurationSec, tune))
            {
                var idx = Math.Min(JunkItems.Length - 1, (int)Math.Floor(rng.Next() * JunkItems.Length));
                return new Item { Kind = "junk", Key = JunkItems[idx], Half = "", Juice = "BFC4CC" };
            }
            return RollVeggie(rng);
        }

        public static Item RollVeggie(GoobyRng rng)
        {
            var idx = Math.Min(Veggies.Length - 1, (int)Math.Floor(rng.Next() * Veggies.Length));
            var v = Veggies[idx];
            return new Item { Kind = "veggie", Key = v.Key, Half = v.Half, Juice = v.Juice };
        }

        public static double FrenzySpawnInterval() => Chop.FrenzyDurationSec / Chop.FrenzyItems;

        public static int FrenzyCountAt(double elapsed) => (int)Math.Floor(Math.Max(0.0, elapsed) / Chop.FrenzyEverySec);

        public static double VyForApex(double h, double g = 9.5) => Math.Sqrt(2.0 * g * Math.Max(0.0, h));

        public struct Arc
        {
            public double X0, Y0, Vx, Vy;
        }

        public static Arc MakeArc(GoobyRng rng, double halfW, double y0, double g = 9.5)
        {
            var apexY = Chop.ApexMinY + rng.Next() * (Chop.ApexMaxY - Chop.ApexMinY);
            var vy = VyForApex(apexY - y0, g);
            var tApex = vy / g;
            var x0 = (rng.Next() * 2.0 - 1.0) * Math.Max(0.0, halfW - 0.4);
            var apexX = (rng.Next() * 2.0 - 1.0) * Math.Max(0.0, halfW - 0.55);
            var vx = tApex > 0.0 ? (apexX - x0) / tApex : 0.0;
            return new Arc { X0 = x0, Y0 = y0, Vx = vx, Vy = vy };
        }

        public static (double x, double y) ArcPos(Arc arc, double t, double g = 9.5) =>
            (arc.X0 + arc.Vx * t, arc.Y0 + arc.Vy * t - 0.5 * g * t * t);

        public static (double t, double x, double y) ArcApex(Arc arc, double g = 9.5)
        {
            var t = arc.Vy / g;
            var (x, y) = ArcPos(arc, t, g);
            return (t, x, y);
        }

        public static int ChopPoints(int k) => Chop.ChopPts + (k > 1 ? Chop.ComboBonus : 0);

        public static int ComboAfterHit(int current, string kind) => kind == "junk" ? 0 : Math.Max(0, current) + 1;

        public static int SwipeScore(int n) => n <= 0 ? 0 : Chop.ChopPts * n + Chop.ComboBonus * (n - 1);

        public static int ApplyPoints(int score, int delta) => Math.Max(0, score + delta);

        public static int FinalScore(int score, Tune tune) => (int)Math.Round(Math.Max(0, score) * tune.ScoreMult, MidpointRounding.AwayFromZero);

        public static bool EndlessShouldEnd(int junkHits, Tune tune) => tune.Endless && junkHits >= tune.EndlessJunkHits;

        public static bool SegmentHitsCircle(double ax, double ay, double bx, double by, double cx, double cy, double r)
        {
            var dx = bx - ax; var dy = by - ay;
            var len2 = dx * dx + dy * dy;
            var t = 0.0;
            if (len2 > 0.0) t = Math.Max(0.0, Math.Min(1.0, ((cx - ax) * dx + (cy - ay) * dy) / len2));
            var px = ax + t * dx - cx; var py = ay + t * dy - cy;
            return px * px + py * py <= r * r;
        }

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Chop, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            int score = 0, misses = 0, junkHits = 0;
            var limit = tune.Endless ? 600.0 : tune.DurationSec;
            while (elapsed < limit && !EndlessShouldEnd(junkHits, tune))
            {
                var size = WaveSizeAt(rng, elapsed);
                for (var i = 0; i < size; i++)
                {
                    var item = RollItem(rng, elapsed, tune);
                    if (item.Kind == "veggie")
                    {
                        if (rng.Next() < tune.AutoplayChopRate) score += tune.ChopPts;
                        else misses += 1;
                    }
                    else if (tune.Endless && rng.Next() < tune.EndlessBotJunkRate)
                    {
                        junkHits += 1;
                        if (EndlessShouldEnd(junkHits, tune)) break;
                    }
                }
                elapsed += SpawnIntervalAt(elapsed, tune.DurationSec, tune);
            }
            var frenzyVeggies = tune.Endless ? 0 : FrenzyCountAt(tune.DurationSec) * tune.FrenzyItems;
            score += (int)Math.Round(frenzyVeggies * tune.AutoplayChopRate, MidpointRounding.AwayFromZero) * tune.ChopPts;
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["misses"] = misses, ["junkHits"] = junkHits, ["elapsed"] = elapsed };
        }
    }
}
