using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Burger-Bau round — port of burger_build.gd's _process/_new_order/_spawn_item/_step_items/_catch_item/
    /// _complete_order/_expire_order over the certified BurgerBuild rules, engine-free. World: half width 3.4, half
    /// height 5.2, the plate slides along y = −3.4. A seeded ticket (bun … bun) per order, ingredients rain down
    /// three columns, the right layer +5 (rush orders ×1.5), a wrong one −2, the finished burger +15; a bite beat,
    /// then the next order; an order that runs out of time expires. 75 s round, endless until three expire.
    /// </summary>
    public sealed class BurgerBuildGame
    {
        public const double IntroSec = 1.5;
        public const double HalfW = 3.4, HalfH = 5.2, PlateY = -3.4;
        public const double LayerHeight = 0.22, StackBase = 0.18;

        public sealed class Item
        {
            public string Id;
            public double X, Y;
            public int Col;
        }

        public BurgerBuild.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public double Score { get; private set; }
        public readonly List<string> Ticket = new List<string>();
        public int Placed { get; private set; }
        public int Completed { get; private set; }
        public int Expired { get; private set; }
        public int OrderNumber { get; private set; } = 1;
        public bool Rush { get; private set; }
        public double OrderLeft { get; private set; }
        public double BiteLeft { get; private set; }
        public double Elapsed { get; private set; }
        public double PlateX { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Item> Items = new List<Item>();

        private double _sinceNeeded, _spawnLeft;

        public event Action<Item, bool, double> Caught;     // item, correct, score delta
        public event Action<double> OrderCompleted;         // points for the burger
        public event Action OrderExpired;
        public event Action NewOrder;
        public event Action Finished_;

        public BurgerBuildGame(string mode, int seed)
        {
            Tune = BurgerBuild.ApplyDifficulty(BurgerBuild.Burger, mode);
            Rng = new GoobyRng(seed);
            StartOrder();
        }

        public int LiveScore => (int)Math.Floor(Score);
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public string NextNeeded => BurgerBuild.NextNeeded(Ticket, Placed);
        /// <summary>Top of the stack above the plate (the next layer lands here).</summary>
        public double StackTop => StackBase + Placed * LayerHeight;
        public bool Biting => BiteLeft > 0.0;
        public double[] Columns => BurgerBuild.ColumnCenters(HalfW);

        private void StartOrder()
        {
            Ticket.Clear();
            Ticket.AddRange(BurgerBuild.MakeTicket(Rng));
            Placed = 0;
            Rush = BurgerBuild.IsRushOrder(OrderNumber);
            OrderLeft = BurgerBuild.OrderTimerSec(Rush, Tune);
            _sinceNeeded = 0.0;
            _spawnLeft = 0.0;
            Items.Clear();
            NewOrder?.Invoke();
        }

        /// <summary>The plate follows the finger: a world x, clamped to the counter.</summary>
        public void SetPlateX(double worldX) => PlateX = Math.Max(-HalfW + 0.4, Math.Min(HalfW - 0.4, worldX));

        /// <summary>Plate from a view x (0..1 across the play field).</summary>
        public void SetPlateFromNormalizedX(double nx) => SetPlateX((nx - 0.5) * 2.0 * HalfW);

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            if (!Tune.Endless && Elapsed >= Tune.DurationSec) { Finish(); return; }
            if (BiteLeft > 0.0)
            {
                BiteLeft -= dt;
                if (BiteLeft <= 0.0) StartOrder();
                return;
            }
            OrderLeft -= dt;
            if (OrderLeft <= 0.0) { ExpireOrder(); return; }
            _sinceNeeded += dt;
            _spawnLeft -= dt;
            if (_spawnLeft <= 0.0)
            {
                SpawnItem();
                _spawnLeft = Tune.SpawnSec;
            }
            StepItems(dt);
        }

        private void SpawnItem()
        {
            var needed = NextNeeded;
            var id = BurgerBuild.RollSpawn(Rng, needed, _sinceNeeded, Tune);
            if (id == needed) _sinceNeeded = 0.0;
            var cols = Columns;
            var col = Math.Min(cols.Length - 1, (int)Math.Floor(Rng.Next() * cols.Length));
            Items.Add(new Item { Id = id, X = cols[col], Y = HalfH + 0.6, Col = col });
        }

        private void StepItems(double dt)
        {
            var speed = BurgerBuild.FallSpeedAt(Completed, Tune);
            var catchY = PlateY + StackTop + 0.12;
            var half = Tune.PlateHalfWidth;
            var keep = new List<Item>(Items.Count);
            foreach (var item in Items)
            {
                var prevY = item.Y;
                item.Y = prevY - speed * dt;
                if (prevY > catchY && item.Y <= catchY && Math.Abs(item.X - PlateX) <= half)
                {
                    CatchItem(item);
                    if (Biting) { Items.Clear(); return; }   // the finished burger clears the counter
                    continue;
                }
                if (item.Y < -HalfH - 0.8) continue;
                keep.Add(item);
            }
            Items.Clear();
            Items.AddRange(keep);
        }

        private void CatchItem(Item item)
        {
            var needed = NextNeeded;
            var correct = needed.Length > 0 && item.Id == needed;
            var prev = Score;
            Score = BurgerBuild.ApplyCatch(Score, correct, Rush, Tune);
            if (correct) Placed++;
            Caught?.Invoke(item, correct, Score - prev);
            if (BurgerBuild.IsComplete(Ticket, Placed)) CompleteOrder();
        }

        private void CompleteOrder()
        {
            var points = BurgerBuild.OrderPoints(Tune.CompletePts, Rush);
            Score += points;
            Completed++;
            OrderCompleted?.Invoke(points);
            OrderNumber++;
            BiteLeft = Tune.BiteSec;
        }

        private void ExpireOrder()
        {
            Expired++;
            OrderExpired?.Invoke();
            OrderNumber++;
            if (BurgerBuild.EndlessShouldEnd(Expired, Tune)) { Finish(); return; }
            StartOrder();
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = LiveScore;
            Finished_?.Invoke();
        }
    }
}
