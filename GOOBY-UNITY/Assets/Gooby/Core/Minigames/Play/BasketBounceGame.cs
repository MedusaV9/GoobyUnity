using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Korbwurf round — port of basket_bounce.gd's _process/_flick_velocity/_launch/_step_flight/
    /// _resolve_shot over the certified BasketBounce rules, engine-free. A flick (finger samples of the last 0.13 s,
    /// in 390×844 view pixels per second) throws the ball from the spawn spot; the ballistic arc bounces off rim and
    /// board (StepBallSwept), a basket pays 3 (+2 off the board, +2 for the second swish in a row, ×2 while the hoop
    /// slides after ten baskets), the hoop backs off 0.35 m per basket up to 8 m. 60 s; endless until three misses
    /// in a row. Between shots the ball rests 0.55 s on the spawn spot.
    /// </summary>
    public sealed class BasketBounceGame
    {
        public const double IntroSec = 1.5;
        public const double FlickSampleSec = 0.13;
        public const int TrailMax = 26;
        public const string PhaseAim = "aim", PhaseFly = "fly", PhaseReset = "reset";

        public BasketBounce.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public int Score { get; private set; }
        public int Baskets { get; private set; }
        public int Shots { get; private set; }
        public int MissStreak { get; private set; }
        public int SwishStreak { get; private set; }
        public double Elapsed { get; private set; }
        public double SlideElapsed { get; private set; }
        public string Phase { get; private set; } = PhaseAim;
        public double ResetLeft { get; private set; }
        public BasketBounce.Ball Ball { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public bool Dragging { get; private set; }
        public (double x, double y) DragFrom { get; private set; }
        public (double x, double y) DragTo { get; private set; }
        public readonly List<(double x, double y, double z)> Trail = new List<(double, double, double)>();

        private readonly List<(double x, double y, double t)> _samples = new List<(double, double, double)>();

        public event Action<BasketBounce.Vel> Launched;
        public event Action RimHit, BoardHit;
        /// <summary>points, swish, bank</summary>
        public event Action<int, bool, bool> Scored;
        public event Action MissedShot;
        public event Action Finished_;

        public BasketBounceGame(string mode, int seed)
        {
            Tune = BasketBounce.ApplyDifficulty(BasketBounce.Basket, mode);
            Rng = new GoobyRng(seed);
        }

        public bool Aiming => Phase == PhaseAim && IntroLeft <= 0.0 && !Finished;
        public bool MovingHoop => BasketBounce.IsMovingHoop(Baskets, Tune);
        public bool OnFire => BasketBounce.IsOnFire(SwishStreak);
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Elapsed);
        public double HoopDistance => BasketBounce.HoopDistance(Baskets, Tune);

        /// <summary>Where the hoop is right now (it slides sideways once ten baskets are in).</summary>
        public BasketBounce.Hoop HoopNow() => new BasketBounce.Hoop
        {
            X = BasketBounce.HoopSlideX(SlideElapsed, Baskets, Tune),
            Z = Tune.SpawnZ - HoopDistance,
        };

        /// <summary>The ball's position: in flight its own, otherwise resting on the spawn spot.</summary>
        public (double x, double y, double z) BallWorld() =>
            Phase == PhaseFly && Ball != null ? (Ball.Px, Ball.Py, Ball.Pz) : (Tune.SpawnX, Tune.SpawnY, Tune.SpawnZ);

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            if (MovingHoop) SlideElapsed += dt;
            if (BasketBounce.IsRoundOver(Elapsed, MissStreak, Tune)) { Finish(); return; }
            if (Phase == PhaseFly) StepFlight(dt);
            else if (Phase == PhaseReset)
            {
                ResetLeft -= dt;
                if (ResetLeft <= 0.0) Phase = PhaseAim;
            }
        }

        // ── the flick ──────────────────────────────────────────────────────

        /// <summary>Finger down at a view position (390×844 pixels) — starts collecting the flick.</summary>
        public void BeginDrag(double x, double y)
        {
            if (!Aiming) return;
            Dragging = true;
            DragFrom = (x, y); DragTo = (x, y);
            _samples.Clear();
            _samples.Add((x, y, Elapsed));
        }

        public void Drag(double x, double y)
        {
            if (!Dragging) return;
            DragTo = (x, y);
            _samples.Add((x, y, Elapsed));
            if (_samples.Count > 24) _samples.RemoveAt(0);
        }

        /// <summary>The finger was lost (capture gone, app paused): no throw.</summary>
        public void CancelDrag()
        {
            Dragging = false;
            _samples.Clear();
        }

        /// <summary>Finger up: the last 0.13 s of the stroke become the throw. Returns false when it was too gentle.</summary>
        public bool EndDrag()
        {
            if (!Dragging) return false;
            Dragging = false;
            var (fx, fy) = FlickVelocity();
            _samples.Clear();
            var vel = BasketBounce.FlickToVelocity(fx, fy, Tune);
            if (!vel.Valid) return false;
            Launch(vel);
            return true;
        }

        /// <summary>
        /// basket_bounce.gd _flick_velocity: view pixels per second over the last FlickSampleSec. On a device whose
        /// frames are further apart than that window (the software-rendered QA player) the window holds only the
        /// final sample, so the stretch back to the previous distinct sample counts instead of no throw at all.
        /// </summary>
        public (double vx, double vy) FlickVelocity()
        {
            if (_samples.Count < 2) return (0.0, 0.0);
            var last = _samples[_samples.Count - 1];
            var first = _samples[0];
            foreach (var s in _samples)
            {
                if (last.t - s.t <= FlickSampleSec) { first = s; break; }
            }
            var dt = last.t - first.t;
            if (dt <= 0.0001)
            {
                for (var i = _samples.Count - 2; i >= 0; i--)
                {
                    if (last.t - _samples[i].t > 0.0001) { first = _samples[i]; break; }
                }
                dt = last.t - first.t;
                if (dt <= 0.0001 || dt > 0.5) return (0.0, 0.0);
            }
            return ((last.x - first.x) / dt, (last.y - first.y) / dt);
        }

        /// <summary>A throw with a given launch velocity (bots, tests).</summary>
        public bool Shoot(BasketBounce.Vel vel)
        {
            if (!Aiming || !vel.Valid) return false;
            Dragging = false;
            _samples.Clear();
            Launch(vel);
            return true;
        }

        /// <summary>A throw the rules solved to drop in (the QA tour's shot); false when none was found.</summary>
        public bool ShootAtHoop()
        {
            if (!Aiming) return false;
            var vel = BasketBounce.SolveBasketVelocity(HoopNow(), Rng, Tune);
            return vel.Valid && Shoot(vel);
        }

        private double _botAt = -1.0;

        /// <summary>
        /// QA autoplay (<c>mgbot</c>): a beat after the ball is back on the spot, the throw the rules solved to drop in;
        /// every fifth throw is short of the rim, so a tour shows misses too.
        /// </summary>
        public void ApplyBot()
        {
            if (!Aiming) { _botAt = -1.0; return; }
            if (_botAt < 0.0) _botAt = Elapsed + 0.45;
            if (Elapsed < _botAt) return;
            var vel = BasketBounce.SolveBasketVelocity(HoopNow(), Rng, Tune);
            if (!vel.Valid) { _botAt = Elapsed + 0.2; return; }
            _botAt = -1.0;
            if (Shots % 5 == 4) { vel.Y *= 0.8; vel.Z *= 0.8; }
            Shoot(vel);
        }

        private void Launch(BasketBounce.Vel vel)
        {
            Ball = BasketBounce.MakeBall(vel, Tune);
            Trail.Clear();
            Phase = PhaseFly;
            Launched?.Invoke(vel);
        }

        private void StepFlight(double dt)
        {
            var hoop = HoopNow();
            var ev = BasketBounce.StepBallSwept(Ball, dt, hoop, Tune);
            Trail.Add((Ball.Px, Ball.Py, Ball.Pz));
            if (Trail.Count > TrailMax) Trail.RemoveAt(0);
            if (ev.Rim) RimHit?.Invoke();
            if (ev.Board) BoardHit?.Invoke();
            if (ev.BasketMade) ResolveShot(true, !Ball.Rim && !Ball.Board, Ball.Board);
            else if (ev.Dead) ResolveShot(false, false, false);
        }

        private void ResolveShot(bool made, bool swish, bool bank)
        {
            Shots++;
            var moving = MovingHoop;
            var (points, streak) = BasketBounce.ScoreShot(made, swish, bank, SwishStreak, moving, Tune);
            SwishStreak = streak;
            Score += points;
            if (made)
            {
                Baskets++;
                MissStreak = 0;
                Scored?.Invoke(points, swish, bank);
            }
            else
            {
                MissStreak++;
                MissedShot?.Invoke();
            }
            Phase = PhaseReset;
            ResetLeft = Tune.ShotResetSec;
            if (BasketBounce.IsRoundOver(Elapsed, MissStreak, Tune)) Finish();
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            Dragging = false;
            FinalScore = Math.Max(0, Score);
            Finished_?.Invoke();
        }
    }
}
