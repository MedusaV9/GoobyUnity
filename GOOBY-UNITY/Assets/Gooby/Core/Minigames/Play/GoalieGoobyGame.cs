using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Torwart-Gooby round — port of goalie_gooby.gd's _process/_spawn_kick/_dive/_resolve_kick/_on_save/
    /// _on_goal over the certified GoalieGooby rules, engine-free. The striker telegraphs a kick into one of five
    /// lanes (0.9 s shrinking to 0.45 s over the minute), the ball flies 0.55 s; from 8 s in a fifth of the kicks
    /// are lobs (dive up) or rollers (dive down). A swipe picks lane and height; the dive counts when it comes within
    /// 0.45 s before the ball arrives (+4, +2 more inside the last 0.15 s), three goals end the round early. Every ten
    /// saves the crowd cheers and the flights get 10 % faster; the last ten seconds are a shoot-out (double points).
    /// </summary>
    public sealed class GoalieGoobyGame
    {
        public const double IntroSec = 1.2;
        public const double SwipeMinPx = 10.0;

        public sealed class Kick
        {
            public int Lane;
            public string Kind;
            public double Telegraph, Flight;
            public bool Shootout;
        }

        public sealed class DiveInfo
        {
            public int Lane;
            public string V;
            public double T;
        }

        public GoalieGooby.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Saves { get; private set; }
        public int Goals { get; private set; }
        public int SaveStreak { get; private set; }
        public double Elapsed { get; private set; }
        public Kick Current { get; private set; }
        public double KickStart { get; private set; }
        public double ArriveT { get; private set; }
        public double NextKickAt { get; private set; } = 0.6;
        public DiveInfo Dive { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        private readonly Func<double> _stream;

        public event Action<Kick> Kicked;                       // the telegraph starts
        public event Action<DiveInfo> Dived;
        public event Action<Kick, int, bool> Saved;             // kick, points, super save
        public event Action<Kick> Conceded;
        public event Action<int> Cheer;                         // cheers so far
        public event Action Finished_;

        public GoalieGoobyGame(string mode, int seed)
        {
            Tune = GoalieGooby.ApplyDifficulty(GoalieGooby.Goalie, mode);
            Rng = new GoobyRng(seed);
            _stream = () => Rng.Next();
        }

        public bool Running => IntroLeft <= 0.0 && !Finished;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public int Cheers => GoalieGooby.CheersAt(Saves);
        public bool InShootout => GoalieGooby.IsShootoutAt(Elapsed, Tune);
        public int MaxGoals => Tune.Endless ? Tune.EndlessGoals : Tune.MaxGoals;

        /// <summary>0 during the telegraph, then the ball's way from the spot (0) to the line (1).</summary>
        public double KickProgress
        {
            get
            {
                if (Current == null) return 0.0;
                var t = Elapsed - KickStart;
                if (t < Current.Telegraph) return 0.0;
                return Math.Max(0.0, Math.Min(1.0, (t - Current.Telegraph) / Math.Max(0.001, Current.Flight)));
            }
        }

        /// <summary>How far the telegraph has run (0..1) — the shrinking ring on the target lane.</summary>
        public double TelegraphProgress => Current == null ? 0.0 : Math.Max(0.0, Math.Min(1.0, (Elapsed - KickStart) / Math.Max(0.001, Current.Telegraph)));
        public bool BallInFlight => Current != null && Elapsed - KickStart >= Current.Telegraph;

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
            if (Current == null)
            {
                if (Elapsed >= NextKickAt) SpawnKick();
            }
            else if (Elapsed >= ArriveT) ResolveKick();
        }

        private void SpawnKick()
        {
            var shootout = GoalieGooby.IsShootoutAt(Elapsed, Tune);
            var (lane, kind) = GoalieGooby.RollKick(_stream, Elapsed);
            Current = new Kick
            {
                Lane = lane, Kind = kind, Shootout = shootout,
                Telegraph = shootout ? Tune.ShootoutTelegraphSec : GoalieGooby.TelegraphSecAt(Elapsed, Tune),
                Flight = shootout ? Tune.ShootoutFlightSec : GoalieGooby.FlightSecAt(Cheers, Tune),
            };
            KickStart = Elapsed;
            ArriveT = Elapsed + Current.Telegraph + Current.Flight;
            Dive = null;
            Kicked?.Invoke(Current);
        }

        /// <summary>A swipe (view pixels): lane from its angle, height from its vertical part; a bare tap is a dive to the middle.</summary>
        public DiveInfo DiveBy(double dx, double dy)
        {
            if (!Running) return null;
            var lane = GoalieGooby.LaneFromSwipe(dx, dy);
            var v = GoalieGooby.VKindFromSwipe(dy);
            if (Math.Sqrt(dx * dx + dy * dy) < SwipeMinPx) { lane = 2; v = "mid"; }
            Dive = new DiveInfo { Lane = lane, V = v, T = Elapsed };
            Dived?.Invoke(Dive);
            return Dive;
        }

        private static readonly double[] BotLaneDeg = { -70.0, -36.0, 0.0, 36.0, 70.0 };

        /// <summary>
        /// QA autoplay (<c>mgbot</c>): swipes into the kick's lane and height <see cref="GoalieGooby.Tune.AutoplayLeadSec"/>
        /// before the ball arrives. Every eighth kick it guesses the wrong side, so a tour shows goals as well as saves; it
        /// draws nothing from the kick stream.
        /// </summary>
        public void ApplyBot()
        {
            if (!Running || Current == null || !BallInFlight || Dive != null) return;
            if (ArriveT - Elapsed > Tune.AutoplayLeadSec) return;
            var lane = Current.Lane;
            if ((Saves + Goals) % 8 == 7) lane = lane >= 2 ? lane - 2 : lane + 2;
            var v = Current.Kind == "lob" ? "up" : Current.Kind == "roller" ? "down" : "mid";
            var mag = v == "mid" ? 20.0 : 60.0;
            DiveBy(Math.Tan(BotLaneDeg[lane] * Math.PI / 180.0) * mag, v == "up" ? -mag : mag);
        }

        private void ResolveKick()
        {
            var kick = Current;
            var saved = Dive != null && GoalieGooby.SaveMatches(kick.Lane, kick.Kind, Dive.Lane, Dive.V) && GoalieGooby.DiveCovers(Dive.T, ArriveT, Tune);
            if (saved) OnSave(kick); else OnGoal(kick);
            Current = null;
            NextKickAt = Elapsed + (kick.Shootout ? Tune.ShootoutGapSec : Tune.GapSec);
        }

        private void OnSave(Kick kick)
        {
            var super = GoalieGooby.IsSuperSave(Dive.T, ArriveT, Tune);
            var points = GoalieGooby.SavePoints(super, kick.Shootout, Tune);
            var before = Cheers;
            Saves++;
            SaveStreak++;
            Score += points;
            Saved?.Invoke(kick, points, super);
            if (Cheers > before) Cheer?.Invoke(Cheers);
        }

        private void OnGoal(Kick kick)
        {
            Goals++;
            SaveStreak = 0;
            Conceded?.Invoke(kick);
            if (Goals >= MaxGoals) Finish();
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
