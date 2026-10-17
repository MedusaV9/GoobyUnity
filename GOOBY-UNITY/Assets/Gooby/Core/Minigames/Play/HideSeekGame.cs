using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Guck-guck-Garten round — port of hide_seek.gd's _process/_start_wave/_tick_peeks/_tap_spot/
    /// _clear_wave/_expire_wave over the certified HideSeek rules, engine-free. A 3×4 grid of hideouts; each wave
    /// hides 3→5 critters that peek on a seeded schedule. A tap on a occupied spot finds it (+2), clearing the wave
    /// pays +3 then a 1 s serve; a wave that times out expires (endless: three expiries end). Intro 1.5 s gates
    /// the clock, peeks and taps; the first wave is already rolled in setup so the seed matches Godot.
    /// </summary>
    public sealed class HideSeekGame
    {
        public const double IntroSec = 1.5;

        public sealed class Critter
        {
            public int Spot, Color;
            public bool Found;
            public double PeekT, NextPeek;
        }

        public HideSeek.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Wave { get; private set; }
        public int Expired { get; private set; }
        public int FoundTotal { get; private set; }
        public double Elapsed { get; private set; }
        public double WaveT { get; private set; }
        public double WaveSec { get; private set; }
        public double ServeT { get; private set; }
        public string Phase { get; private set; } = "play";   // play | serve
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly Dictionary<int, Critter> Critters = new Dictionary<int, Critter>();
        public readonly HashSet<int> Hidden = new HashSet<int>();
        public readonly double[] Shake;

        public event Action<int, int> Found;        // spot, points
        public event Action<int> Empty;             // spot
        public event Action<int> WaveCleared;       // bonus
        public event Action<int> WaveStarted;       // hiders
        public event Action WaveExpired;
        public event Action Finished_;

        public HideSeekGame(string mode, int seed)
        {
            Tune = HideSeek.ApplyDifficulty(HideSeek.Seek, mode);
            Rng = new GoobyRng(seed);
            Shake = new double[HideSeek.SpotCount(Tune)];
            StartWave(0);
        }

        public int Spots => HideSeek.SpotCount(Tune);
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public int HiddenCount => Hidden.Count;

        /// <summary>Godot `_rise_of`: 0 hidden, sine pop while peeking, 1 once found.</summary>
        public double RiseOf(int spot)
        {
            if (!Critters.TryGetValue(spot, out var c)) return 0.0;
            if (c.Found) return 1.0;
            if (c.PeekT <= 0.0) return 0.0;
            var k = Math.Sin(Math.PI * Math.Max(0.0, 1.0 - c.PeekT / Tune.PeekDurationSec));
            return 0.12 + k * 0.76;
        }

        public bool IsPeeking(int spot) => Critters.TryGetValue(spot, out var c) && !c.Found && c.PeekT > 0.0;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            for (var i = 0; i < Shake.Length; i++) Shake[i] = Math.Max(0.0, Shake[i] - dt);
            if (!Tune.Endless && Elapsed >= Tune.DurationSec) { Finish(); return; }
            if (Phase == "serve")
            {
                ServeT -= dt;
                if (ServeT <= 0.0)
                {
                    Phase = "play";
                    StartWave(Wave + 1);
                }
                return;
            }
            WaveT += dt;
            if (WaveT >= WaveSec) { ExpireWave(); return; }
            TickPeeks(dt);
        }

        /// <summary>A tap on a hideout. "found", "empty", or null when gated / already resolved.</summary>
        public string Tap(int spot)
        {
            if (Finished || IntroLeft > 0.0 || Phase != "play") return null;
            if (spot < 0 || spot >= Spots) return null;
            Shake[spot] = 0.35;
            if (!Hidden.Contains(spot))
            {
                Empty?.Invoke(spot);
                return "empty";
            }
            Hidden.Remove(spot);
            var c = Critters[spot];
            c.Found = true;
            c.PeekT = 0.0;
            FoundTotal += 1;
            Score = HideSeek.ApplyScore(Score, Tune.FindPts);
            Found?.Invoke(spot, Tune.FindPts);
            if (Hidden.Count == 0) ClearWave();
            return "found";
        }

        private int _botTaps;

        /// <summary>
        /// QA autoplay (<c>mgbot</c>): taps a critter a beat after it starts to peek; every sixth time it tries an empty
        /// hideout first, so a tour shows the miss as well. Draws nothing from the round's random stream.
        /// </summary>
        public void ApplyBot()
        {
            if (Finished || IntroLeft > 0.0 || Phase != "play") return;
            foreach (var c in Critters.Values)
            {
                if (c.Found || c.PeekT <= 0.0 || c.PeekT > Tune.PeekDurationSec - 0.3) continue;
                _botTaps++;
                if (_botTaps % 6 == 0)
                {
                    for (var s = 0; s < Spots; s++)
                        if (!Hidden.Contains(s)) { Tap(s); return; }
                }
                Tap(c.Spot);
                return;
            }
        }

        private void StartWave(int index)
        {
            Wave = index;
            WaveSec = HideSeek.WaveSecFor(Wave, Tune);
            WaveT = 0.0;
            Hidden.Clear();
            Critters.Clear();
            var spots = HideSeek.RollHiders(Rng, Wave, Tune);
            for (var i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                Hidden.Add(spot);
                Critters[spot] = new Critter
                {
                    Spot = spot,
                    Found = false,
                    PeekT = 0.0,
                    NextPeek = Elapsed + 0.6 + Rng.Next() * Tune.PeekEverySec,
                    Color = i % 5,
                };
            }
            if (Wave > 0) WaveStarted?.Invoke(spots.Count);
        }

        private void TickPeeks(double dt)
        {
            foreach (var c in Critters.Values)
            {
                if (c.Found || !Hidden.Contains(c.Spot)) continue;
                if (c.PeekT > 0.0)
                {
                    c.PeekT -= dt;
                    if (c.PeekT <= 0.0)
                    {
                        c.PeekT = 0.0;
                        c.NextPeek = Elapsed + Tune.PeekEverySec * (0.75 + Rng.Next() * 0.5);
                    }
                }
                else if (Elapsed >= c.NextPeek)
                {
                    c.PeekT = Tune.PeekDurationSec;
                }
            }
        }

        private void ClearWave()
        {
            Score = HideSeek.ApplyScore(Score, Tune.WaveBonus);
            WaveCleared?.Invoke(Tune.WaveBonus);
            Phase = "serve";
            ServeT = Tune.ServeSec;
        }

        private void ExpireWave()
        {
            Expired += 1;
            foreach (var c in Critters.Values) c.PeekT = 0.0;
            WaveExpired?.Invoke();
            if (HideSeek.EndlessShouldEnd(Expired, Tune)) { Finish(); return; }
            Phase = "serve";
            ServeT = Tune.ServeSec;
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
