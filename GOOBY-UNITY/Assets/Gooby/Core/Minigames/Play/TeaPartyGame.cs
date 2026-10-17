using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Teestube round — port of tea_party.gd's _process/_unhandled_input/_release over the certified TeaParty
    /// rules, engine-free. A cup slides in (serve beat), the player holds to pour (the level rises at FillRate) and
    /// releases inside the band: perfect +6, good +3, outside or overflowing = a spill; every third perfect in a row
    /// +2. Then the next cup with a new band, the serve cadence tightening from 1.7 s to 1.1 s over the 60 s round;
    /// endless until three spills.
    /// </summary>
    public sealed class TeaPartyGame
    {
        public const double IntroSec = 1.5;
        public const double FirstServeSec = 0.5, SlideSec = 0.4;

        public TeaParty.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Cups { get; private set; }
        public int Spills { get; private set; }
        public int Streak { get; private set; }
        public double Elapsed { get; private set; }
        public double Level { get; private set; }
        public TeaParty.Band Band { get; private set; }
        public bool Holding { get; private set; }
        public bool Serving { get; private set; } = true;
        public double ServeLeft { get; private set; } = FirstServeSec;
        public double CupSlide { get; private set; } = 1.0;   // 1 = the cup is still sliding in, 0 = in place
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<string, int, int> Poured;   // "perfect" | "good" | "miss", points incl. streak bonus, streak after
        public event Action<int> StreakBonus;            // the streak that paid the bonus
        public event Action Finished_;

        public TeaPartyGame(string mode, int seed)
        {
            Tune = TeaParty.ApplyDifficulty(TeaParty.Tea, mode);
            Rng = new GoobyRng(seed);
            Band = TeaParty.RollBand(Rng, Tune);
        }

        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        /// <summary>The cup is in place and waits for the pour.</summary>
        public bool Ready => !Finished && IntroLeft <= 0.0 && !Serving;

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
            if (Serving)
            {
                ServeLeft -= dt;
                CupSlide = Math.Min(1.0, Math.Max(0.0, ServeLeft / SlideSec));
                if (ServeLeft <= 0.0) { Serving = false; CupSlide = 0.0; }
            }
            else if (Holding)
            {
                Level = TeaParty.FillAfter(Level, dt, Tune);
                if (Level >= Tune.OverflowLevel) Release();
            }
        }

        /// <summary>Finger down: the tea pours (only once the cup stands).</summary>
        public bool Press()
        {
            if (!Ready || Holding) return false;
            Holding = true;
            return true;
        }

        /// <summary>Finger up: the cup is served as it is. Returns the result or null when nothing was pouring.</summary>
        public string Release()
        {
            if (!Holding || Finished) return null;
            Holding = false;
            var res = TeaParty.Pour(Level, Band, Tune);
            var points = res.Points;
            Score = TeaParty.ApplyScore(Score, points);
            var bonus = 0;
            if (res.Result == "perfect")
            {
                Streak++;
                bonus = TeaParty.StreakBonusAt(Streak, Tune);
                Score = TeaParty.ApplyScore(Score, bonus);
            }
            else
            {
                Streak = 0;
                if (res.Result == "miss") Spills++;
            }
            Poured?.Invoke(res.Result, points + bonus, Streak);
            if (bonus > 0) StreakBonus?.Invoke(Streak);
            if (TeaParty.EndlessShouldEnd(Spills, Tune)) { Finish(); return res.Result; }
            // the next cup: a new band, the cadence from the rules
            Level = 0.0;
            Band = TeaParty.RollBand(Rng, Tune);
            Cups++;
            Serving = true;
            ServeLeft = TeaParty.ServeIntervalAt(Elapsed, Tune.DurationSec, Tune);
            return res.Result;
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            Holding = false;
            FinalScore = Score;
            Finished_?.Invoke();
        }
    }
}
