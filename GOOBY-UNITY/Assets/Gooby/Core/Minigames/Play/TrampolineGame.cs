using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Trampolin-Tricks round — port of trampoline.gd's _process/_flight_tick/_butt_landing/_launch/
    /// _tap_landing/_resolve_swipe over the certified Trampoline rules, engine-free. Gooby bounces on the mat: a tap
    /// inside the landing window (0.3 s, shrinking with height, at least 0.1 s before the mat) arms a boost (×1.16
    /// +0.35 m/s, capped), a tap earlier in the judge zone (0.5 s) arms a butt landing (stagger 1.1 s, back to the
    /// base bounce), no tap decays the bounce (×0.94, floor 4.2 m/s). Swipes in the air are tricks — flip (left),
    /// spin (right), twist (vertical) — worth 2/2/3 × the height tier (1–3); all three kinds in one flight pay +12.
    /// 60 s; endless until three butt landings.
    /// </summary>
    public sealed class TrampolineGame
    {
        public const double IntroSec = 1.5;
        public const double TrickSec = 0.3;

        public Trampoline.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public double Elapsed { get; private set; }
        public double Height { get; private set; }
        public double Vy { get; private set; }
        public double Apex { get; private set; }
        public bool Airborne { get; private set; } = true;
        public int Failures { get; private set; }
        public double StaggerLeft { get; private set; }
        public string Armed { get; private set; } = "";
        public Trampoline.TrickChain Chain { get; private set; } = new Trampoline.TrickChain();
        public double Tricking { get; private set; }
        public double TrickSpin { get; private set; }
        public string LastTrick { get; private set; } = "";
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public int Tier { get; private set; }

        public event Action<double> Landed;                 // impact speed 0..1 of the max
        public event Action Boosted;
        public event Action ButtLanded;
        public event Action<int> TierUp;                    // new height multiplier
        public event Action<string, int> Tricked;           // kind, points
        public event Action<int> Combo;                     // the three-trick bonus
        public event Action Finished_;

        public TrampolineGame(string mode, int seed)
        {
            Tune = Trampoline.ApplyDifficulty(Trampoline.Tramp, mode);
            Rng = new GoobyRng(seed);
            Vy = Tune.BaseVy;
            Apex = Trampoline.ApexFor(Vy, Tune.Gravity);
            Tier = Trampoline.HeightMultiplier(Apex);
        }

        public bool Running => IntroLeft <= 0.0 && !Finished;
        public bool Staggering => StaggerLeft > 0.0;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public int Multiplier => Trampoline.HeightMultiplier(Apex);
        public double WindowSec => Trampoline.WindowSecFor(Apex, Tune);
        public double TimeToImpact => Airborne ? Trampoline.TimeToImpact(Height, Vy, Tune.Gravity) : double.PositiveInfinity;
        /// <summary>How far the falling Gooby is inside the judge zone (0 = outside, 1 = on the mat) — the window gauge.</summary>
        public double ZoneProgress => Airborne && Vy <= 0.0 && TimeToImpact <= Tune.JudgeZoneSec ? 1.0 - TimeToImpact / Tune.JudgeZoneSec : 0.0;
        public bool InWindow => Airborne && Vy <= 0.0 && TimeToImpact <= WindowSec;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            Tricking = Math.Max(0.0, Tricking - dt);
            if (Tricking > 0.0) TrickSpin += dt * 9.0;
            if (StaggerLeft > 0.0)
            {
                StaggerLeft = Math.Max(0.0, StaggerLeft - dt);
                if (StaggerLeft <= 0.0) Launch(Tune.BaseVy);
            }
            else FlightTick(dt);
            if (Trampoline.EndlessShouldEnd(Failures, Tune)) { Finish(); return; }
            if (!Tune.Endless && Elapsed >= Tune.DurationSec) Finish();
        }

        private void FlightTick(double dt)
        {
            var g = Tune.Gravity;
            var previous = Height;
            Vy -= g * dt;
            Height += Vy * dt;
            if (!Trampoline.CrossedMat(previous, Height, Vy)) return;
            var action = Armed != "" ? Armed : "none";
            Armed = "";
            Height = 0.0;
            if (action == "butt") { ButtLanding(); return; }
            var nextVy = Trampoline.NextBounceVy(Math.Abs(Vy), action, Tune);
            Landed?.Invoke(Math.Max(0.3, Math.Min(1.0, Math.Abs(Vy) / Tune.MaxVy)));
            if (action == "boost") Boosted?.Invoke();
            Launch(nextVy);
        }

        private void ButtLanding()
        {
            Failures++;
            StaggerLeft = Tune.ButtStaggerSec;
            Airborne = false;
            Vy = 0.0;
            ButtLanded?.Invoke();
        }

        private void Launch(double nextVy)
        {
            Vy = nextVy;
            Height = 0.0;
            Airborne = true;
            Apex = Trampoline.ApexFor(Vy, Tune.Gravity);
            Armed = "";
            Chain = new Trampoline.TrickChain();
            TrickSpin = 0.0;
            var mult = Trampoline.HeightMultiplier(Apex);
            if (mult > Tier) TierUp?.Invoke(mult);
            Tier = mult;
        }

        // ── input ──────────────────────────────────────────────────────────

        /// <summary>Finger down while falling: inside the window it arms the boost, in the judge zone before it the butt landing.</summary>
        public string Tap()
        {
            if (!Running || Staggering || Vy > 0.0) return "ignore";
            var verdict = Trampoline.ClassifyLandingTap(TimeToImpact, Apex, Tune);
            if (verdict == "ignore") return verdict;
            Armed = verdict;
            return verdict;
        }

        /// <summary>A swipe by (dx, dy) view pixels, resolved to flip (left) / spin (right) / twist (up or down).</summary>
        public static string TrickKindFor(double dx, double dy)
        {
            if (Math.Abs(dx) > Math.Abs(dy)) return dx < 0.0 ? "flip" : "spin";
            return "twist";
        }

        /// <summary>A swipe in the air: a trick worth its points × the height tier. Returns the kind or null when no trick was possible.</summary>
        public string Swipe(double dx, double dy)
        {
            if (!Running || Staggering) return null;
            if (!Trampoline.CanTrick(Airborne, TimeToImpact, Tricking > 0.0, Tune)) return null;
            var kind = TrickKindFor(dx, dy);
            var mult = Multiplier;
            var points = Trampoline.TrickPoints(kind, mult);
            Score += points;
            Tricking = TrickSec;
            LastTrick = kind;
            Tricked?.Invoke(kind, points);
            var (triggered, bonus) = Trampoline.RecordTrick(Chain, kind);
            if (triggered)
            {
                Score += bonus;
                Combo?.Invoke(bonus);
            }
            return kind;
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Math.Max(0, Score);
            Finished_?.Invoke();
        }
    }
}
