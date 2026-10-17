using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Raketen-Rettung round — port of rocket_rescue.gd's _process/_unhandled_input over the certified
    /// RocketRescue engine, engine-free. Setup builds the 5-pad moon (RNG unmoved by the 1.5 s intro). Hold is
    /// thrust, screen thirds tilt (TiltCommandFor). Soft/ok landing on a bunny pad picks it up; the station
    /// delivers (+30 + fuel/2 + 5 per soft). Hard landing bounces −10 fuel, never death. Empty tank tows home.
    /// 120 s; endless re-arms bunnies. Score = RoundScore.
    /// </summary>
    public sealed class RocketRescueGame
    {
        public const double IntroSec = 1.5;
        public const double TouchSquashSec = 0.28;
        public const double BeaconPopSec = 0.55;
        public const double HintFadeSec = 5.0;
        public const double GroundFrac = 0.88;
        public const double WorldHFrac = 0.8;
        public const double CamSmooth = 6.0;
        public const double CamLookaheadS = 0.4;

        public RocketRescue.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public RocketRescue.Engine Engine { get; }
        public RocketRescue.Bot Bot { get; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public bool Touching { get; private set; }
        public double TouchNx { get; private set; }
        public bool Thrust { get; private set; }
        public int TiltDir { get; private set; }
        public double SquashLeft { get; private set; }
        public double BeaconLeft { get; private set; }
        public double CamX { get; private set; }

        public event Action LiftedOff;
        public event Action<string, bool> Landed;     // kind, bonusEligible
        public event Action HardLanded;
        public event Action BunnyAboard;
        public event Action<int> Rescued_;            // count
        public event Action<double> FuelPicked;
        public event Action FuelLow;
        public event Action OutOfFuel;
        public event Action<int> WindTelegraph;
        public event Action<int> WindGust;
        public event Action Finished_;

        public RocketRescueGame(string mode, int seed)
        {
            Tune = RocketRescue.ApplyDifficulty(RocketRescue.Rocket, mode);
            Rng = new GoobyRng(seed);
            Engine = new RocketRescue.Engine(() => Rng.Next(), Tune);
            Bot = new RocketRescue.Bot(Tune);
        }

        public int Score => Engine.Score();
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Engine.Elapsed);
        public double Squash01 => TouchSquashSec <= 0.0 ? 0.0 : SquashLeft / TouchSquashSec;

        /// <summary>Finger X in −1…1 of the field (Godot `_normalized_x`). Hold = thrust.</summary>
        public bool SetTouch(bool down, double nx)
        {
            if (Finished || IntroLeft > 0.0) return false;
            TouchNx = Math.Max(-1.0, Math.Min(1.0, nx));
            Touching = down;
            Thrust = down;
            TiltDir = RocketRescue.TiltCommandFor(TouchNx, down);
            return true;
        }

        /// <summary>Certified PD autopilot — tests and PlayMode fly with it.</summary>
        public void ApplyBot()
        {
            if (Finished || IntroLeft > 0.0) return;
            var cmd = Bot.Control(Engine);
            Thrust = cmd.Thrust;
            TiltDir = cmd.TiltDir;
            Touching = cmd.Thrust;
            TouchNx = cmd.TiltDir;
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            SquashLeft = Math.Max(0.0, SquashLeft - dt);
            BeaconLeft = Math.Max(0.0, BeaconLeft - dt);
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            var input = new RocketRescue.Input { Thrust = Thrust && !Engine.Towing, TiltDir = TiltDir };
            foreach (var ev in Engine.Step(input, dt)) Handle(ev);
            TrackCamera(dt);
            if (!Finished && Engine.Ended) Finish();
        }

        /// <summary>Godot `cam_target`: ship + look-ahead, clamped to the visible world half.</summary>
        public static double CamTarget(double x, double vx, double limit) =>
            Math.Max(-limit, Math.Min(limit, x + vx * CamLookaheadS));

        private double _visibleHalf;

        private void TrackCamera(double dt)
        {
            if (_visibleHalf <= 0.0 || _visibleHalf >= Tune.WorldHalfW) { CamX = 0.0; return; }
            var want = CamTarget(Engine.X, Engine.Vx, Tune.WorldHalfW - _visibleHalf);
            CamX += (want - CamX) * Math.Min(1.0, CamSmooth * dt);
        }

        /// <summary>Half-width of the field in world metres (portrait: the whole moon fits, CamX stays 0).</summary>
        public void SetVisibleHalf(double metres) => _visibleHalf = Math.Max(0.0, metres);

        private void Handle(RocketRescue.Event ev)
        {
            switch (ev.Type)
            {
                case "liftoff": LiftedOff?.Invoke(); break;
                case "landing":
                    SquashLeft = TouchSquashSec;
                    Landed?.Invoke(ev.Kind, ev.BonusEligible);
                    break;
                case "hardLanding":
                    SquashLeft = TouchSquashSec;
                    HardLanded?.Invoke();
                    break;
                case "bunnyPickup":
                    BunnyAboard?.Invoke();
                    break;
                case "rescue":
                    BeaconLeft = BeaconPopSec;
                    Rescued_?.Invoke(Engine.Rescued);
                    break;
                case "fuelPickup": FuelPicked?.Invoke(ev.Amount); break;
                case "fuelLow": FuelLow?.Invoke(); break;
                case "outOfFuel": OutOfFuel?.Invoke(); break;
                case "windTelegraph": WindTelegraph?.Invoke(ev.Dir); break;
                case "windGust": WindGust?.Invoke(ev.Dir); break;
                case "ended": Finish(); break;
            }
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = Engine.Score();
            Thrust = false;
            Touching = false;
            Finished_?.Invoke();
        }
    }
}
