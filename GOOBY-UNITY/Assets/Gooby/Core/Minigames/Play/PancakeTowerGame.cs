using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Pfannkuchenturm round — port of pancake_tower.gd's _process/_drop/_land over the certified
    /// PancakeTower rules, engine-free. A pancake swings above the stack (faster with every layer), a tap drops it;
    /// the overhang is cut off, a perfect drop (within PerfectEps) pays +2 and restores 10 % width, every fifth layer
    /// is a topping (+4, never shrinks). From layer 8 the tower wobbles and the drop is read in tower coordinates.
    /// The round ends when the width falls under 20 % or at 40 layers, or when a pancake misses the stack.
    /// </summary>
    public sealed class PancakeTowerGame
    {
        public const double IntroSec = 1.5;
        public const double DropHeight = 1.5;

        public sealed class Layer
        {
            public double Center, Width;
            public bool Topping, Perfect;
            public int Index;
        }

        public sealed class Crumb
        {
            public double Center, Y, Width, Age;
        }

        public PancakeTower.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public readonly List<Layer> Layers = new List<Layer>();
        public readonly List<Crumb> Crumbs = new List<Crumb>();
        public PancakeTower.Stack Stack { get; private set; }
        public PancakeTower.Wobble Wobble { get; private set; }
        public int BonusPoints { get; private set; }
        public int Perfects { get; private set; }
        public int PerfectStreak { get; private set; }
        public double SlideT { get; private set; }
        public double SlidePhase { get; private set; }
        public bool Falling { get; private set; }
        public double FallX { get; private set; }
        public double FallY { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public bool Toppled { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<Layer, PancakeTower.Drop> Landed;   // the new layer and how it landed
        public event Action<double> Toppled_;                    // x where the pancake fell past the stack
        public event Action Finished_;

        public PancakeTowerGame(string mode, int seed)
        {
            Tune = PancakeTower.ApplyDifficulty(PancakeTower.Pancake, mode);
            Rng = new GoobyRng(seed);
            Stack = new PancakeTower.Stack { Center = 0.0, Width = Tune.BaseWidth };
            Wobble = new PancakeTower.Wobble();
            SlidePhase = Rng.Next();
        }

        /// <summary>1-based number of the pancake swinging right now.</summary>
        public int CurrentIndex => Layers.Count + 1;
        public double StackHeight => Layers.Count * Tune.LayerHeight;
        /// <summary>The swinging pancake's x (tower coordinates) at this moment.</summary>
        public double SwingX => PancakeTower.SlideX(SlideT, CurrentIndex, SlidePhase, Tune);
        public bool CurrentIsTopping => PancakeTower.IsToppingLayer(CurrentIndex, Tune);
        public int Score => PancakeTower.TowerScore(Layers.Count, BonusPoints, Tune);
        public double WidthFrac => Stack.Width / Tune.BaseWidth;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Wobble = PancakeTower.StepWobble(Wobble, dt, Layers.Count, Tune);
            for (var i = Crumbs.Count - 1; i >= 0; i--)
            {
                Crumbs[i].Age += dt;
                Crumbs[i].Y -= Tune.FallSpeed * 0.6 * dt;
                if (PancakeTower.IsFallenExpired(Crumbs[i].Age, Tune)) Crumbs.RemoveAt(i);
            }
            if (Falling)
            {
                FallY -= Tune.FallSpeed * dt;
                if (FallY <= StackHeight) Land();
            }
            else SlideT += dt;
        }

        /// <summary>A tap: the swinging pancake drops from where it is.</summary>
        public bool Drop()
        {
            if (Finished || Falling || IntroLeft > 0.0) return false;
            Falling = true;
            FallX = SwingX;
            FallY = StackHeight + DropHeight;
            return true;
        }

        private void Land()
        {
            Falling = false;
            var index = CurrentIndex;
            var topping = PancakeTower.IsToppingLayer(index, Tune);
            // the tower sways: the world-space drop is read back in tower coordinates
            var localX = PancakeTower.WobbleLocalX(FallX, StackHeight, Wobble.Angle);
            var drop = PancakeTower.ResolveDrop(Stack, localX, topping, Tune);
            if (!drop.Landed)
            {
                Crumbs.Add(new Crumb { Center = localX, Y = StackHeight, Width = Stack.Width, Age = 0.0 });
                Toppled = true;
                Toppled_?.Invoke(FallX);
                Finish();
                return;
            }
            if (drop.Cut.Present) Crumbs.Add(new Crumb { Center = drop.Cut.Center, Y = StackHeight, Width = drop.Cut.Size, Age = 0.0 });
            BonusPoints += drop.Points;
            var layer = new Layer { Center = drop.Center, Width = drop.Width, Topping = topping, Perfect = drop.Perfect, Index = index };
            Layers.Add(layer);
            Stack = new PancakeTower.Stack { Center = drop.Center, Width = drop.Width };
            if (drop.Perfect)
            {
                Perfects++;
                PerfectStreak++;
                Wobble = PancakeTower.DampWobble(Wobble, Tune, Layers.Count);
            }
            else PerfectStreak = 0;
            SlideT = 0.0;
            SlidePhase = Rng.Next();
            Landed?.Invoke(layer, drop);
            if (PancakeTower.IsTowerDone(Stack.Width, Layers.Count, Tune)) Finish();
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Finished_?.Invoke();
        }
    }
}
