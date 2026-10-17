using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Pfannkuchenturm — port of GOOBY/src/minigames/games/pancakeTower.logic.js (§C6.1 #8 / §C10.2): a pancake
    /// swings above the stack, tap drops it; overhang is cut, every 5th layer is a topping (+4, no shrink),
    /// a perfect drop +2 and restores 10 % width. Ends at width &lt; 20 % or 40 layers. Wobble from layer 8.
    /// </summary>
    public static class PancakeTower
    {
        public sealed class Tune
        {
            public double BaseWidth = 1.5, LayerHeight = 0.16, PerfectEps = 0.045;
            public int PerfectPoints = 2;
            public double PerfectRestorePct = 0.1;
            public int ToppingEvery = 5, ToppingPoints = 4, PointsPerLayer = 2;
            public double EndWidthFrac = 0.2, MaxLayers = 40.0;
            public double SlideAmplitude = 1.05, SlidePeriodStart = 2.6, SlidePeriodStep = 0.055, SlidePeriodMin = 1.15, FallSpeed = 7.0;
            public int WobbleStartLayer = 8;
            public double WobbleForce = 0.7, WobbleSpring = 10.0, WobbleDamping = 3.2, WobbleMaxRad = 0.16, PerfectWobbleDamp = 0.4, FallenDespawnSec = 1.4;
            public double ToleranceMult = 1.0, OverhangToleranceMult = 1.0;
            public bool Endless = false;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Pancake = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double toleranceRow;
            bool endless;
            switch (mode)
            {
                case "easy": toleranceRow = 1.25; endless = false; break;
                case "hard": toleranceRow = 0.8; endless = false; break;
                case "endless": toleranceRow = 0.8; endless = true; break;
                default: return tune;
            }
            var tolerance = Math.Max(0.55, toleranceRow);
            var o = tune.Clone();
            o.PerfectEps = tune.PerfectEps * tolerance;
            o.ToleranceMult = tolerance;
            o.OverhangToleranceMult = tolerance;
            o.MaxLayers = endless ? double.PositiveInfinity : tune.MaxLayers;
            o.Endless = endless;
            o.Mode = mode;
            return o;
        }

        public static bool IsToppingLayer(int index, Tune tune) => index > 0 && index % tune.ToppingEvery == 0;

        public static double SlidePeriod(int layerIndex, Tune tune) =>
            Math.Max(tune.SlidePeriodMin, tune.SlidePeriodStart - (layerIndex - 1) * tune.SlidePeriodStep);

        public static double SlideX(double t, int layerIndex, double phase, Tune tune) =>
            tune.SlideAmplitude * Math.Sin((t / SlidePeriod(layerIndex, tune) + phase) * Math.PI * 2.0);

        public struct Stack
        {
            public double Center, Width;
        }

        public struct Cut
        {
            public bool Present;
            public double Size, Side, Center;
        }

        public struct Drop
        {
            public bool Landed, Perfect;
            public double Center, Width;
            public Cut Cut;
            public int Points;
        }

        /// <summary>The core: resolve a drop against the stack (cut math §C6.1 #8).</summary>
        public static Drop ResolveDrop(Stack stack, double dropCenter, bool topping, Tune tune)
        {
            var center = stack.Center;
            var width = stack.Width;
            var offset = dropCenter - center;
            var absOff = Math.Abs(offset);

            if (absOff <= tune.PerfectEps)
            {
                var restored = width;
                if (!topping) restored = Math.Min(tune.BaseWidth, width + tune.BaseWidth * tune.PerfectRestorePct);
                var pts = tune.PerfectPoints;
                if (topping) pts += tune.ToppingPoints;
                return new Drop { Landed = true, Perfect = true, Center = center, Width = restored, Points = pts };
            }

            var effectiveOff = absOff / tune.OverhangToleranceMult;
            var overlap = width - effectiveOff;
            if (overlap <= 0.0) return new Drop { Landed = false, Perfect = false, Center = center, Width = width, Points = 0 };

            var side = offset > 0.0 ? 1.0 : -1.0;
            if (topping)
            {
                var maxOff = Math.Max(0.0, (width - overlap) / 2.0);
                return new Drop { Landed = true, Perfect = false, Center = center + side * Math.Min(absOff, maxOff), Width = width, Points = tune.ToppingPoints };
            }

            return new Drop
            {
                Landed = true, Perfect = false, Center = center + offset / 2.0, Width = overlap,
                Cut = new Cut { Present = true, Size = Math.Min(width, effectiveOff), Side = side, Center = dropCenter + side * (overlap / 2.0) },
                Points = 0,
            };
        }

        public static bool IsTowerDone(double width, int layers, Tune tune)
        {
            if (width < tune.BaseWidth * tune.EndWidthFrac) return true;
            return layers >= tune.MaxLayers;
        }

        public static int TowerScore(int layers, int bonusPoints, Tune tune) => Math.Max(0, layers * tune.PointsPerLayer + bonusPoints);

        public struct Wobble
        {
            public double Angle, Velocity, Phase;
        }

        public static Wobble StepWobble(Wobble state, double dt, int layers, Tune tune)
        {
            var h = Math.Max(0.0, dt);
            var phase = state.Phase + h;
            var start = tune.WobbleStartLayer;
            var active = layers >= start;
            var ramp = active ? Math.Min(1.0, (layers - start + 1) / 8.0) : 0.0;
            var drive = active ? Math.Sin(phase * 2.3) * tune.WobbleForce * ramp : 0.0;
            var accel = drive - state.Angle * tune.WobbleSpring - state.Velocity * tune.WobbleDamping;
            var velocity = state.Velocity + accel * h;
            var limit = tune.WobbleMaxRad;
            var angle = Math.Max(-limit, Math.Min(limit, state.Angle + velocity * h));
            return new Wobble { Angle = angle, Velocity = velocity, Phase = phase };
        }

        public static Wobble DampWobble(Wobble state, Tune tune, int layers = 0)
        {
            if (tune.Endless && layers >= tune.WobbleStartLayer) return state;
            return new Wobble { Angle = state.Angle * tune.PerfectWobbleDamp, Velocity = state.Velocity * tune.PerfectWobbleDamp, Phase = state.Phase };
        }

        public static double WobbleTopX(double localCenter, double heightAboveBase, double angle) =>
            localCenter * Math.Cos(angle) - heightAboveBase * Math.Sin(angle);

        public static double WobbleLocalX(double worldX, double heightAboveBase, double angle)
        {
            var c = Math.Cos(angle);
            return (worldX + heightAboveBase * Math.Sin(angle)) / (Math.Abs(c) < 1e-6 ? 1e-6 : c);
        }

        public static bool IsFallenExpired(double age, Tune tune) => age >= tune.FallenDespawnSec;

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1)
        {
            var tune = ApplyDifficulty(Pancake, mode);
            var rng = new GoobyRng(seed);
            var stack = new Stack { Center = 0.0, Width = tune.BaseWidth };
            int layers = 0, bonusPoints = 0;
            var limit = tune.Endless ? 120.0 : tune.MaxLayers;
            var spread = mode == "easy" ? 0.055 : (mode == "hard" || mode == "endless" ? 0.075 : 0.065);
            while (!IsTowerDone(stack.Width, layers, tune) && layers < limit)
            {
                var index = layers + 1;
                var topping = IsToppingLayer(index, tune);
                var offset = (rng.Next() - 0.5) * spread * 2.0;
                var drop = ResolveDrop(stack, stack.Center + offset, topping, tune);
                if (!drop.Landed) break;
                layers += 1;
                bonusPoints += drop.Points;
                stack = new Stack { Center = drop.Center, Width = drop.Width };
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = TowerScore(layers, bonusPoints, tune), ["layers"] = layers, ["width"] = stack.Width };
        }
    }
}
