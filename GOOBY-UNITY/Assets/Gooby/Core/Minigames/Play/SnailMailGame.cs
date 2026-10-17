using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Schneckenpost round — port of snail_mail.gd's _process/_unhandled_input/_follow/_deliver over the
    /// certified SnailMail rules, engine-free. Setup deals level 0 before the 1.5 s intro (RNG like Godot). The
    /// intro gates the clock and the stroke. Draw from the mailbox to the glowing door; the snail then crawls the
    /// Catmull-Rom. A puddle retreats 2 s and drops the dry bonus; flowers +1; delivery +4 (+2 dry). 60 s; endless
    /// ends on three splashes. Score is the running total.
    /// </summary>
    public sealed class SnailMailGame
    {
        public const double IntroSec = 1.5;
        public const string PhaseDraw = "draw";
        public const string PhaseFollow = "follow";
        public const string PhaseRetreat = "retreat";
        public const string PhaseBeat = "beat";

        public SnailMail.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public SnailMail.Level Level { get; private set; }
        public SnailMail.SmoothPath Path { get; private set; }
        public string Phase { get; private set; } = PhaseDraw;
        public double Elapsed { get; private set; }
        public int Score { get; private set; }
        public int Deliveries { get; private set; }
        public int Splashes { get; private set; }
        public int FlowersTotal { get; private set; }
        public int DryStreak { get; private set; }
        public bool Wet { get; private set; }
        public double Arc { get; private set; }
        public double RetreatLeft { get; private set; }
        public double BeatLeft { get; private set; }
        public double SnailX { get; private set; }
        public double SnailY { get; private set; }
        public double SnailAngle { get; private set; } = Math.PI * 0.5;
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public readonly List<SnailMail.Pt> Raw = new List<SnailMail.Pt>();
        public readonly List<int> Picked = new List<int>();

        public event Action StrokeBegan;
        public event Action<string> StrokeRejected; // "post" | "door"
        public event Action PathCommitted;
        public event Action<int> FlowerTaken;     // points
        public event Action Splashed;
        public event Action<int, bool> Delivered; // points, dry
        public event Action Finished_;

        public SnailMailGame(string mode, int seed)
        {
            Tune = SnailMail.ApplyDifficulty(SnailMail.Snail, mode);
            Rng = new GoobyRng(seed);
            NextLevel();
        }

        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public bool Drawing { get; private set; }

        public bool BeginStroke(double x, double y)
        {
            if (Finished || IntroLeft > 0.0 || Phase != PhaseDraw) return false;
            var pt = new SnailMail.Pt(x, y);
            if (!SnailMail.StartsAtPost(pt, Tune))
            {
                StrokeRejected?.Invoke("post");
                return false;
            }
            Drawing = true;
            Raw.Clear();
            Raw.Add(pt);
            StrokeBegan?.Invoke();
            return true;
        }

        public bool ExtendStroke(double x, double y)
        {
            if (!Drawing || Finished || Phase != PhaseDraw) return false;
            if (Raw.Count >= Tune.MaxInputPoints) return false;
            var last = Raw[Raw.Count - 1];
            var dx = x - last.X;
            var dy = y - last.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < Tune.MinInputSpacing) return false;
            Raw.Add(new SnailMail.Pt(x, y));
            return true;
        }

        public bool CommitStroke()
        {
            if (!Drawing) return false;
            Drawing = false;
            var path = SnailMail.Smooth(Raw, Tune);
            if (path == null)
            {
                Raw.Clear();
                return false;
            }
            if (SnailMail.EndHouse(path, Level, Tune) != Level.TargetIdx)
            {
                Raw.Clear();
                StrokeRejected?.Invoke("door");
                return false;
            }
            Path = path;
            Arc = 0.0;
            Phase = PhaseFollow;
            PathCommitted?.Invoke();
            return true;
        }

        /// <summary>Certified auto-router — does not touch the seeded RNG.</summary>
        public bool ApplyBot()
        {
            if (Finished || IntroLeft > 0.0 || Phase != PhaseDraw) return false;
            var route = SnailMail.AutoRoute(Level, Tune);
            if (!route.ok || route.smooth == null) return false;
            Drawing = true;
            Raw.Clear();
            Raw.AddRange(route.pts);
            return CommitStroke();
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            dt = Math.Min(dt, 0.1);
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            if (!Tune.Endless && Elapsed >= Tune.DurationSec) { Finish(); return; }
            if (Phase == PhaseRetreat)
            {
                RetreatLeft = Math.Max(0.0, RetreatLeft - dt);
                if (RetreatLeft <= 0.0) Phase = PhaseFollow;
            }
            if (Phase == PhaseFollow) Follow(dt);
            if (Phase == PhaseBeat)
            {
                BeatLeft = Math.Max(0.0, BeatLeft - dt);
                if (BeatLeft <= 0.0) NextLevel();
            }
        }

        private void Follow(double dt)
        {
            if (Path == null) return;
            Arc = SnailMail.AdvanceArc(Arc, dt, Path.Length, Tune);
            var at = SnailMail.FollowAt(Path, Arc);
            SnailX = at.x; SnailY = at.y; SnailAngle = at.angle;
            if (!Wet && SnailMail.PuddleHitAt(SnailX, SnailY, Level.Puddles, Tune) >= 0)
            {
                Splash();
                return;
            }
            PickFlowers();
            if (Arc >= Path.Length) Deliver();
        }

        private void PickFlowers()
        {
            var flowers = Level.Flowers;
            for (var i = 0; i < flowers.Count; i++)
            {
                if (Picked.Contains(i)) continue;
                var f = flowers[i];
                var dx = SnailX - f.X;
                var dy = SnailY - f.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > Tune.FlowerPickRadius) continue;
                Picked.Add(i);
                FlowerTaken?.Invoke(Tune.FlowerPts);
            }
        }

        private void Splash()
        {
            Wet = true;
            DryStreak = 0;
            Phase = PhaseRetreat;
            RetreatLeft = Tune.RetreatSec;
            Splashed?.Invoke();
        }

        private void Deliver()
        {
            var points = SnailMail.DeliveryPoints(Wet, Picked.Count, Tune);
            Score = SnailMail.ApplyScore(Score, points);
            Deliveries += 1;
            FlowersTotal += Picked.Count;
            if (Wet) Splashes += 1;
            if (Wet) DryStreak = 0; else DryStreak += 1;
            Delivered?.Invoke(points, !Wet);
            if (SnailMail.EndlessShouldEnd(Splashes, Tune)) { Finish(); return; }
            Phase = PhaseBeat;
            BeatLeft = Tune.RoundBeatSec;
        }

        private void NextLevel()
        {
            Level = SnailMail.GenerateLevel(Rng, Deliveries, Tune);
            Path = null;
            Raw.Clear();
            Picked.Clear();
            Arc = 0.0;
            Wet = false;
            Drawing = false;
            Phase = PhaseDraw;
            SnailX = Level.Post.X;
            SnailY = Level.Post.Y;
            SnailAngle = Math.PI * 0.5;
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
