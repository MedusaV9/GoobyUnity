using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Geisterjagd round: the certified GhostHunt state machine (CreateHunt/StepHunt/TapHunt is already the
    /// web's realtime simulation) behind the intro beat and typed events the hosts share — ghosts peek out of graves,
    /// pumpkins and the crypt for a shrinking window, a tap catches (+3, chain +1 per catch within 1.5 s, cap +5),
    /// a flickering pumpkin is a decoy (−2), every 25 s a boo wave of five (four caught = +10), lantern and net tokens.
    /// </summary>
    public sealed class GhostHuntGame
    {
        public const double IntroSec = 1.5;

        public GhostHunt.Tune Tune { get; }
        public GhostHunt.State State { get; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished => State.Ended;
        public int FinalScore => GhostHunt.HuntScore(State);
        public int Score => GhostHunt.HuntScore(State);
        public double Elapsed => State.T;
        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - State.T);

        public event Action<int, int, int> Caught;      // spot, points, chain
        public event Action<int> DecoyTapped;           // decoy
        public event Action<string> PowerUp;            // lantern | net
        public event Action<int> BooWave;               // ghosts in the wave
        public event Action<int> BooBonus;              // bonus
        public event Action<int> BooMissed;             // caught of five
        public event Action Finished_;

        private int _drained;

        public GhostHuntGame(string mode, int seed)
        {
            Tune = GhostHunt.ApplyDifficulty(GhostHunt.Hunt, mode);
            State = GhostHunt.CreateHunt(seed, Tune);
        }

        /// <summary>Visible part of a ghost's window: 0 → 1 while rising, 1 while up, 1 → 0 while sinking.</summary>
        public double Rise(GhostHunt.Ghost g)
        {
            var dur = Math.Max(1e-6, g.Dur);
            var f = (State.T - g.SpawnT) / dur;
            if (f < Tune.RiseFrac) return Math.Max(0.0, f / Tune.RiseFrac);
            if (f > 1.0 - Tune.SinkFrac) return Math.Max(0.0, (1.0 - f) / Tune.SinkFrac);
            return 1.0;
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            GhostHunt.StepHunt(State, dt);
            Drain();
        }

        public string TapGhost(int id) => Tap(new GhostHunt.Tap { Kind = "ghost", Id = id });
        public string TapDecoy(int decoy) => Tap(new GhostHunt.Tap { Kind = "decoy", Id = decoy });
        public string TapToken(int window) => Tap(new GhostHunt.Tap { Kind = "token", Id = window });
        /// <summary>A tap on nothing: the chain is gone.</summary>
        public string TapMiss() => Tap(new GhostHunt.Tap { Kind = "", Id = -1 });

        private string Tap(GhostHunt.Tap target)
        {
            if (Finished || IntroLeft > 0.0) return null;
            var (kind, _) = GhostHunt.TapHunt(State, target);
            Drain();
            return kind;
        }

        /// <summary>Turns the simulation's event log into typed events (each entry once).</summary>
        private void Drain()
        {
            var events = State.Events;
            for (; _drained < events.Count; _drained++)
            {
                var e = events[_drained];
                var d = e.Data;
                switch (e.Type)
                {
                    case "catch": Caught?.Invoke((int)d["spot"], (int)d["points"], (int)d["chain"]); break;
                    case "decoy": DecoyTapped?.Invoke((int)d["decoy"]); break;
                    case "powerup": PowerUp?.Invoke((string)d["kind"]); break;
                    case "booWave": BooWave?.Invoke((int)d["count"]); break;
                    case "booBonus": BooBonus?.Invoke((int)d["bonus"]); break;
                    case "booEnd": BooMissed?.Invoke((int)d["caught"]); break;
                    case "end": Finished_?.Invoke(); break;
                }
            }
            // the log only grows; the host never needs the old entries
            if (events.Count > 256) { events.RemoveRange(0, events.Count - 64); _drained = events.Count; }
        }
    }
}
