using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Gießkannen-Wirbel round — port of garden_rush.gd's _process/_pot_growth_tick/_pot_tick/_spawn_tick/
    /// _sprinkler_tick/_press/_release/_water_sprout/_water_weed/_wilt_out/_fire_sprinkler over the certified
    /// GardenRush rules, engine-free. Sprouts pop up in six pots (seven from 20 s, eight from 35 s) and wilt after a
    /// window that shrinks from 6 to 3 s; holding a pot fills its 0.8-s ring, letting go in the last quarter waters
    /// perfectly (+3), earlier is okay (+1), a wilted sprout costs 2. From 12 s some spawns are weeds — watering one
    /// costs 1. The one-off sprinkler at 30 s refills every sprout's window by half. 60 s; endless until three wilts.
    /// </summary>
    public sealed class GardenRushGame
    {
        public const double WateredCooldown = 0.55;
        public const string Empty = "empty", Sprout = "sprout", Weed = "weed", Cooldown = "cooldown";

        public sealed class Pot
        {
            public string State = Empty;
            public double Remaining, Window, CooldownLeft;
            public bool Grown;
            public double Fraction => Window > 0.0 ? Math.Max(0.0, Math.Min(1.0, Remaining / Window)) : 0.0;
        }

        public GardenRush.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public double Elapsed { get; private set; }
        public int Withered { get; private set; }
        public int ActivePots { get; private set; }
        public int HoldIndex { get; private set; } = -1;
        public double HoldSec { get; private set; }
        public bool SprinklerSpawned { get; private set; }
        public bool SprinklerUsed { get; private set; }
        public int PerfectStreak { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<Pot> Pots = new List<Pot>();
        private double _nextSpawn = 0.35;

        public event Action<int> Sprouted;                 // pot index (sprout or weed)
        public event Action<int, int, bool> Watered;       // pot, points, perfect
        public event Action<int> WeedWatered;
        public event Action<int> Wilted;
        public event Action MorePots;
        public event Action SprinklerReady;
        public event Action<int> SprinklerFired;           // sprouts refilled
        public event Action Finished_;

        public GardenRushGame(string mode, int seed)
        {
            Tune = GardenRush.ApplyDifficulty(GardenRush.Rush, mode);
            Rng = new GoobyRng(seed);
            for (var i = 0; i < Tune.Pots; i++) Pots.Add(new Pot());
            ActivePots = GardenRush.ActivePotsAt(0.0);
        }

        public bool Holding => HoldIndex >= 0;
        public double HoldFraction => Holding ? GardenRush.HoldFillFraction(HoldSec, Tune) : 0.0;
        public bool HoldInPerfectZone => Holding && GardenRush.InPerfectZone(HoldFraction, Tune);
        public bool SprinklerAvailable => SprinklerSpawned && !SprinklerUsed;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            Elapsed += dt;
            GrowthTick();
            PotTick(dt);
            SpawnTick(dt);
            SprinklerTick();
            if (HoldIndex >= 0) HoldSec += dt;
            if (RoundOver()) Finish();
        }

        private bool RoundOver()
        {
            if (GardenRush.EndlessShouldEnd(Withered, Tune)) return true;
            return !Tune.Endless && Elapsed >= Tune.DurationSec;
        }

        private void GrowthTick()
        {
            var active = GardenRush.ActivePotsAt(Elapsed);
            if (active <= ActivePots) return;
            ActivePots = active;
            MorePots?.Invoke();
        }

        private void PotTick(double dt)
        {
            for (var i = 0; i < Pots.Count; i++)
            {
                var pot = Pots[i];
                if (pot.State == Cooldown)
                {
                    pot.CooldownLeft -= dt;
                    if (pot.CooldownLeft <= 0.0) pot.State = Empty;
                    continue;
                }
                if (pot.State != Sprout && pot.State != Weed) continue;
                pot.Remaining -= dt;
                if (pot.Remaining > 0.0) continue;
                if (pot.State == Sprout) WiltOut(i); else ClearPot(i, Tune.RespawnSec);
            }
        }

        private void SpawnTick(double dt)
        {
            _nextSpawn -= dt;
            if (_nextSpawn > 0.0) return;
            _nextSpawn = GardenRush.SpawnIntervalAt(Elapsed, Tune.DurationSec, Tune);
            var free = new List<int>();
            for (var i = 0; i < Math.Min(ActivePots, Pots.Count); i++) if (Pots[i].State == Empty) free.Add(i);
            if (free.Count == 0) return;
            var index = free[Math.Min(free.Count - 1, (int)Math.Floor(Rng.Next() * free.Count))];
            var weed = GardenRush.RollWeed(Rng, Elapsed);
            var pot = Pots[index];
            var window = GardenRush.WiltWindowAt(Elapsed, Tune.DurationSec, Tune);
            pot.State = weed ? Weed : Sprout;
            pot.Window = weed ? Tune.WeedLifeSec : window;
            pot.Remaining = pot.Window;
            pot.Grown = false;
            Sprouted?.Invoke(index);
        }

        private void SprinklerTick()
        {
            if (!GardenRush.ShouldSpawnSprinkler(Elapsed, SprinklerSpawned)) return;
            SprinklerSpawned = true;
            SprinklerReady?.Invoke();
        }

        // ── input ──────────────────────────────────────────────────────────

        /// <summary>Finger down on a pot: starts the watering hold (only on a sprout or a weed, one at a time).</summary>
        public bool Press(int index)
        {
            if (Finished || HoldIndex >= 0 || index < 0 || index >= Math.Min(ActivePots, Pots.Count)) return false;
            var state = Pots[index].State;
            if (state != Sprout && state != Weed) return false;
            HoldIndex = index;
            HoldSec = 0.0;
            return true;
        }

        /// <summary>Finger up: the held pot is watered with the ring's fill (a weed costs, whatever the fill).</summary>
        public void Release()
        {
            if (HoldIndex < 0) return;
            var index = HoldIndex;
            var frac = GardenRush.HoldFillFraction(HoldSec, Tune);
            HoldIndex = -1;
            HoldSec = 0.0;
            var pot = Pots[index];
            if (pot.State == Weed) WaterWeed(index);
            else if (pot.State == Sprout) WaterSprout(index, frac);
        }

        /// <summary>The sprinkler button: every sprout's window gets half of it back — once per round.</summary>
        public bool FireSprinkler()
        {
            if (Finished || !SprinklerAvailable) return false;
            SprinklerUsed = true;
            var refilled = 0;
            foreach (var pot in Pots)
            {
                if (pot.State != Sprout) continue;
                pot.Remaining = GardenRush.SprinklerRefill(pot.Remaining, pot.Window);
                refilled++;
            }
            SprinklerFired?.Invoke(refilled);
            return true;
        }

        private void WaterSprout(int index, double frac)
        {
            var points = GardenRush.ReleasePoints(frac, Tune);
            var perfect = GardenRush.InPerfectZone(frac, Tune);
            Score = GardenRush.ApplyPoints(Score, points);
            PerfectStreak = perfect ? PerfectStreak + 1 : 0;
            var pot = Pots[index];
            pot.State = Cooldown;
            pot.CooldownLeft = WateredCooldown;
            pot.Remaining = 0.0;
            Watered?.Invoke(index, points, perfect);
        }

        private void WaterWeed(int index)
        {
            Score = GardenRush.ApplyPoints(Score, Tune.WeedPts);
            var pot = Pots[index];
            pot.Remaining = Math.Min(pot.Remaining, 1.2);
            pot.Grown = true;
            PerfectStreak = 0;
            WeedWatered?.Invoke(index);
        }

        private void WiltOut(int index)
        {
            Score = GardenRush.ApplyPoints(Score, Tune.WiltPts);
            Withered++;
            PerfectStreak = 0;
            Wilted?.Invoke(index);
            ClearPot(index, Tune.RespawnSec);
        }

        private void ClearPot(int index, double cooldown)
        {
            var pot = Pots[index];
            pot.State = Cooldown;
            pot.CooldownLeft = cooldown;
            pot.Remaining = 0.0;
            pot.Grown = false;
            if (HoldIndex == index) { HoldIndex = -1; HoldSec = 0.0; }
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            HoldIndex = -1;
            FinalScore = Math.Max(0, Score);
            Finished_?.Invoke();
        }
    }
}
