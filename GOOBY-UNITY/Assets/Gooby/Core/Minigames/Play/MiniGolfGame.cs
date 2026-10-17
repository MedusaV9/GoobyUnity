using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Minigolf round — port of mini_golf.gd's _process/_putt/_step_roll/_end_hole/_next_hole over the
    /// certified MiniGolf rules, engine-free. Setup rolls the six-hole course (RNG unmoved by the 1.5 s intro).
    /// Drag-to-aim then release putts; friction 0.985/frame, rails, windmill, bump, ramp, capture when slow enough.
    /// Ace +30 / ≤par +20 / bogey +12 / else +6. Pause 1.1 s between holes; endless ends on three over-par.
    /// </summary>
    public sealed class MiniGolfGame
    {
        public const double IntroSec = 1.5;
        public const double HolePauseSec = 1.1;
        public const double SinkSec = 0.38;
        public const double MinPullPx = 8.0;

        public MiniGolf.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public readonly List<MiniGolf.Hole> Course;
        public MiniGolf.EndlessState Endless { get; }
        public MiniGolf.Ball Ball { get; private set; } = new MiniGolf.Ball();
        public int Score { get; private set; }
        public int HoleIndex { get; private set; }
        public int Strokes { get; private set; }
        public int HoleStreak { get; private set; }
        public double Theta { get; private set; }
        public string Phase { get; private set; } = "aim";   // aim | roll | pause
        public double PauseLeft { get; private set; }
        public double SinkT { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public double ViewW { get; set; } = 390.0;
        public double ViewH { get; set; } = 844.0;
        public bool Dragging { get; private set; }
        public double DragFromX { get; private set; }
        public double DragFromY { get; private set; }
        public double DragToX { get; private set; }
        public double DragToY { get; private set; }
        public readonly List<(int strokes, int par, bool holed)> Results = new List<(int, int, bool)>();

        public event Action<int, bool> Holed;     // points, ace
        public event Action GaveUp;
        public event Action<string> BallEvent;    // bank|windmill|bump|nougat
        public event Action HoleStarted;
        public event Action Finished_;

        public MiniGolfGame(string mode, int seed)
        {
            Tune = MiniGolf.ApplyDifficulty(MiniGolf.Golf, mode);
            Rng = new GoobyRng(seed);
            Course = MiniGolf.GenerateCourse(() => Rng.Next(), Tune);
            Endless = MiniGolf.CreateEndlessState();
            ResetBall();
        }

        public MiniGolf.Hole Current => Course[HoleIndex % Course.Count];
        public int CurrentPar => Current.Par;
        public int DisplayHole => Tune.Endless ? HoleIndex + 1 : Math.Min(HoleIndex + 1, Course.Count);
        public double PullLen => Hypot(DragFromX - DragToX, DragFromY - DragToY);
        public double AimPower => MiniGolf.PowerFromDrag(PullLen, ViewW, ViewH);

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            if (Phase == "pause")
            {
                PauseLeft -= dt;
                SinkT = Math.Max(0.0, SinkT - dt);
                if (PauseLeft <= 0.0) NextHole();
                return;
            }
            if (Phase == "roll") StepRoll(dt);
            Theta += Math.PI * 2.0 * MiniGolf.Golf.WindmillRps * dt;
        }

        public void BeginAim(double x, double y)
        {
            if (Finished || IntroLeft > 0.0 || Phase != "aim") return;
            Dragging = true;
            DragFromX = DragToX = x;
            DragFromY = DragToY = y;
        }

        public void AimTo(double x, double y)
        {
            if (!Dragging) return;
            DragToX = x;
            DragToY = y;
        }

        /// <summary>Release the drag; returns true if a putt started.</summary>
        public bool Release()
        {
            if (!Dragging) return false;
            Dragging = false;
            return PuttPull(DragFromX - DragToX, DragFromY - DragToY);
        }

        /// <summary>Screen-space pull (from − to), same as Godot's fallback `(pull.x, −pull.y)` → world x/z.</summary>
        public bool PuttPull(double pullX, double pullY)
        {
            if (Finished || IntroLeft > 0.0 || Phase != "aim") return false;
            var len = Hypot(pullX, pullY);
            if (len < MinPullPx) return false;
            var power = MiniGolf.PowerFromDrag(len, ViewW, ViewH);
            if (power <= 0.05) return false;
            var dx = pullX;
            var dz = -pullY;
            var d = Hypot(dx, dz);
            if (d < 1e-6) return false;
            return PuttWorld(dx / d * power, dz / d * power);
        }

        /// <summary>Fire the ball with a world-space velocity (tests and the auto-caddy).</summary>
        public bool PuttWorld(double vx, double vz)
        {
            if (Finished || IntroLeft > 0.0 || Phase != "aim") return false;
            var power = Hypot(vx, vz);
            if (power <= 0.05) return false;
            Ball.Vx = vx;
            Ball.Vz = vz;
            Strokes += 1;
            Phase = "roll";
            return true;
        }

        public void CancelAim() => Dragging = false;

        private void StepRoll(double dt)
        {
            var hole = Current;
            var events = MiniGolf.StepBall(hole, Ball, dt, Theta, Tune);
            for (var i = 0; i < events.Count; i++)
            {
                var tag = events[i];
                if (tag != "holed") BallEvent?.Invoke(tag);
            }
            if (Ball.Done) EndHole(true);
            else if (MiniGolf.IsStopped(hole, Ball))
            {
                Phase = "aim";
                if (Strokes >= Tune.MaxStrokes) EndHole(false);
            }
        }

        private void EndHole(bool holed)
        {
            var par = CurrentPar;
            var taken = holed ? Strokes : Tune.MaxStrokes + 1;
            var points = MiniGolf.HoleScore(taken, par, Tune);
            Score += points;
            Results.Add((taken, par, holed));
            if (holed)
            {
                if (taken == 1) HoleStreak += 1;
                else HoleStreak += 1;
                SinkT = SinkSec;
                Holed?.Invoke(points, taken == 1);
            }
            else
            {
                HoleStreak = 0;
                GaveUp?.Invoke();
            }
            if (Tune.Endless && MiniGolf.RecordHole(Endless, taken, par)) { Finish(); return; }
            Phase = "pause";
            PauseLeft = HolePauseSec;
        }

        private void NextHole()
        {
            HoleIndex += 1;
            if (!Tune.Endless && HoleIndex >= Course.Count) { Finish(); return; }
            ResetBall();
            Phase = "aim";
            HoleStarted?.Invoke();
        }

        private void ResetBall()
        {
            var start = Current.Start;
            Ball = new MiniGolf.Ball { X = start.x, Z = start.z, Vx = 0.0, Vz = 0.0, Done = false };
            Strokes = 0;
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Score;
            Dragging = false;
            Finished_?.Invoke();
        }

        private static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
    }
}
