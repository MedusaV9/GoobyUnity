using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Einkaufs-Surf (arcade) — the input side of shopping_surf.gd around the certified
    /// <see cref="ShoppingSurfRun"/>: swipes left/right/up/down become the edge-triggered actions of the next step (the
    /// run buffers one action for 250 ms itself). A 1.5 s intro lock keeps Gooby on the start line while the frame
    /// counts down; three crashes end the round (four on easy).
    /// </summary>
    public sealed class ShoppingSurfGame
    {
        public const double IntroSec = 1.5;

        public ShoppingSurf.Tune Tune { get; }
        public ShoppingSurfRun.Run Run { get; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<ShoppingSurfRun.Ev> Event;
        public event Action Finished_;

        private ShoppingSurfRun.Input _pending;
        private bool _botOn;

        public ShoppingSurfGame(string mode, int seed)
        {
            Tune = ShoppingSurf.ApplyDifficulty(ShoppingSurf.Surf, mode);
            Run = ShoppingSurfRun.CreateRun(new GoobyRng(seed), "arcade", Tune);
        }

        public int Score => ShoppingSurfRun.RunScore(Run);

        /// <summary>"left" | "right" | "up" (jump) | "down" (slide / fast drop in the air).</summary>
        public void Swipe(string dir)
        {
            if (Finished || IntroLeft > 0.0) return;
            switch (dir)
            {
                case "left": _pending.Left = true; break;
                case "right": _pending.Right = true; break;
                case "up": _pending.Jump = true; break;
                case "down": _pending.Slide = true; break;
            }
        }

        /// <summary>QA/tests: the certified pilot decides this step.</summary>
        public void ApplyBot() => _botOn = true;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                _pending = default;
                return;
            }
            var input = _botOn ? ShoppingSurfRun.BotInput(Run) : _pending;
            _botOn = false;
            _pending = default;
            foreach (var ev in ShoppingSurfRun.StepRun(Run, Math.Min(dt, 0.1), input)) Event?.Invoke(ev);
            if (Run.Ended && !Finished)
            {
                Finished = true;
                FinalScore = ShoppingSurfRun.RunScore(Run);
                Finished_?.Invoke();
            }
        }
    }
}
