using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Hafen-Hopser round — the input side of harbor_hopper.gd (_take_input/_steer_to/_handle_event/_finish)
    /// around the certified <see cref="HarborHopper.Engine"/>. A finger position across the screen becomes the target
    /// lane (×1.25 of the canal half-width, like Godot), a tap without dragging queues the horn. A 1.5 s intro lock
    /// lets the frame count 3-2-1 before the cutter casts off (the engine itself has none).
    /// </summary>
    public sealed class HarborHopperGame
    {
        public const double IntroSec = 1.5;

        public HarborHopper.Tune Tune { get; }
        public HarborHopper.Engine Engine { get; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public int Boosts { get; private set; }
        public int Streak { get; private set; }

        public event Action<HarborHopper.Item> Crate;
        public event Action<HarborHopper.Item> Ring;
        public event Action<string> Bumped;
        public event Action<int> Boosted;
        public event Action<int> BuoysCleared;
        public event Action HornEmpty;
        public event Action GullWarned, GullStole, GullLeft;
        public event Action Finished_;

        private double? _target;
        private bool _horn;
        private readonly HarborHopper.Bot _bot;

        public HarborHopperGame(string mode, int seed)
        {
            Tune = HarborHopper.ApplyDifficulty(HarborHopper.Harbor, mode);
            Engine = new HarborHopper.Engine(new GoobyRng(seed), Tune);
            _bot = new HarborHopper.Bot(Tune);
        }

        public int Score => HarborHopper.HopperScore(Engine.S, Tune);
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Engine.S.Elapsed);

        /// <summary>Finger at <paramref name="normalized"/> (−1 left edge … +1 right edge) steers towards that spot.</summary>
        public void SetTarget(double normalized)
        {
            var n = Math.Max(-1.0, Math.Min(1.0, normalized));
            _target = n * Tune.ChannelHalfW * 1.25;
        }

        public void ReleaseTarget() => _target = null;

        public void QueueHorn()
        {
            if (!Finished && IntroLeft <= 0.0) _horn = true;
        }

        private bool _botOn;

        /// <summary>QA/tests: the certified greedy skipper steers this frame.</summary>
        public void ApplyBot() => _botOn = true;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                _horn = false;
                return;
            }
            var input = _botOn
                ? _bot.Control(Engine.S, Engine.Items, Engine.Piers, Engine.Waves)
                : new HarborHopper.Input { TargetX = _target, Horn = _horn };
            _botOn = false;
            _horn = false;
            foreach (var ev in Engine.Step(input, dt))
            {
                switch (ev.Type)
                {
                    case "crate": Streak++; Crate?.Invoke(ev.Item); break;
                    case "ring": Streak++; Ring?.Invoke(ev.Item); break;
                    case "bump": Streak = 0; Bumped?.Invoke(ev.What); break;
                    case "boost": Boosts++; Boosted?.Invoke(ev.Chain); break;
                    case "buoyCleared": BuoysCleared?.Invoke(ev.Count); break;
                    case "hornEmpty": HornEmpty?.Invoke(); break;
                    case "gullWarn": GullWarned?.Invoke(); break;
                    case "gullSteal": Streak = 0; GullStole?.Invoke(); break;
                    case "gullLeave": GullLeft?.Invoke(); break;
                }
            }
            if (Engine.S.Ended) Finish();
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
