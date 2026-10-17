using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Random;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Sheep herding session — the flow of herde_game.gd around <see cref="RanchHerde"/>: level select (10 levels) →
    /// herding → all in (won) or time up (lost) → next/retry/select until "Fertig". The finger sets the rider's target
    /// (a tap near a free sheep jumps behind it, a drag steers freely); the rider trots there, the flock flees. A level
    /// opens with a 1.5-s beat (sim and input wait). Wins book <c>ranch.spiele.herde</c>.
    /// </summary>
    public sealed class RanchHerdeGame
    {
        public const double IntroSec = 1.5;
        public const string PhaseSelect = "select", PhasePlay = "play", PhaseWon = "won", PhaseLost = "lost";

        public List<JObject> Levels { get; }
        public RanchHerde.Tune Tune { get; }
        public string Phase { get; private set; } = PhaseSelect;
        public int LevelId { get; private set; }
        public int Attempt { get; private set; }
        public JObject Level { get; private set; }
        public List<RanchHerde.Schaf> Schafe { get; private set; } = new List<RanchHerde.Schaf>();
        public V2 Reiter { get; private set; }
        public V2 Ziel { get; private set; }
        public double TAbs { get; private set; }
        public double Limit { get; private set; }
        public double LevelIntroLeft { get; private set; }
        public double PhaseTime { get; private set; }
        public int SessionScore { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public int LastStars { get; private set; }
        public int LastScore { get; private set; }
        public bool LastFirstClear { get; private set; }

        public event Action<string> PhaseChanged;
        /// <summary>A sheep reached the pen (count in, total).</summary>
        public event Action<int, int> SheepIn;
        public event Action Finished_;

        private readonly long _runSeed;
        private readonly GameStateStore _store;
        private readonly JObject _localSave = new JObject();
        private bool _botOn;
        private int _drinVorher;

        public RanchHerdeGame(string difficulty, long runSeed, GameStateStore store = null)
        {
            _runSeed = runSeed;
            _store = store;
            Tune = RanchHerde.ApplyDifficulty(RanchHerde.Base, difficulty);
            Levels = RanchHerde.LoadLevels();
        }

        public JObject Save => _store?.State ?? _localSave;
        public bool IsUnlocked(int id) => id >= 1 && id <= RanchSpieleProgress.MaxUnlocked(Save, RanchSpieleProgress.Herde);
        public bool AcceptsInput => Phase == PhasePlay && LevelIntroLeft <= 0.0;
        public double TimeLeft => Math.Max(0.0, Limit - TAbs);
        public int DrinAnzahl => RanchHerde.DrinAnzahl(Schafe);

        public bool Open(int id)
        {
            if (Finished) return false;
            var level = RanchHerde.LevelById(Levels, id);
            if (level == null) return false;
            LevelId = id;
            Attempt += 1;
            Level = level;
            Schafe = RanchHerde.SpawnSchafe(level, new GoobyRng(_runSeed + id * 211L + Attempt * 131L));
            var feld = RanchHerde.Feld(level);
            Reiter = new V2(0.0, feld.z * 0.9);
            Ziel = Reiter;
            TAbs = 0.0;
            Limit = RanchHerde.Zeitlimit(level, Tune);
            _drinVorher = 0;
            LevelIntroLeft = IntroSec;
            SetPhase(PhasePlay);
            return true;
        }

        public void Retry() { if (LevelId > 0) Open(LevelId); }

        public void Next()
        {
            if (LevelId < RanchHerde.LevelCount && IsUnlocked(LevelId + 1)) Open(LevelId + 1);
            else BackToSelect();
        }

        public void BackToSelect() => SetPhase(PhaseSelect);

        public void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = SessionScore;
            Finished_?.Invoke();
        }

        /// <summary>The finger: a tap near a free sheep targets the drive point behind it, a drag steers freely.</summary>
        public void SetZiel(V2 world, bool tap)
        {
            if (!AcceptsInput) return;
            Ziel = tap ? RanchHerde.TreibZiel(world, Schafe, Level) : world;
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            PhaseTime += dt;
            if (Phase != PhasePlay) return;
            if (LevelIntroLeft > 0.0)
            {
                LevelIntroLeft = Math.Max(0.0, LevelIntroLeft - Math.Min(dt, 0.25));
                _botOn = false;
                return;
            }
            if (_botOn) Ziel = RanchHerde.BotZiel(Schafe, Level);
            _botOn = false;
            TAbs += dt;
            Reiter = RanchHerde.ReiterStep(Reiter, Ziel, dt, Tune, Level);
            Schafe = RanchHerde.Step(Schafe, Reiter, TAbs, dt, Tune, Level);
            var drin = DrinAnzahl;
            if (drin > _drinVorher) SheepIn?.Invoke(drin, Schafe.Count);
            _drinVorher = drin;
            if (drin == Schafe.Count) { Won(); return; }
            if (TAbs >= Limit) { LastStars = 0; LastScore = 0; LastFirstClear = false; SetPhase(PhaseLost); }
        }

        public void ApplyBot()
        {
            if (Finished) return;
            switch (Phase)
            {
                case PhaseSelect: Open(RanchSpieleProgress.MaxUnlocked(Save, RanchSpieleProgress.Herde)); break;
                case PhasePlay: _botOn = true; break;
                case PhaseWon: if (PhaseTime >= 1.5) Next(); break;
                case PhaseLost: if (PhaseTime >= 1.5) Retry(); break;
            }
        }

        private void Won()
        {
            var rest = TimeLeft;
            LastStars = RanchHerde.Sterne(rest, Limit);
            LastFirstClear = !RanchSpieleProgress.IsCleared(Save, RanchSpieleProgress.Herde, LevelId);
            LastScore = RanchHerde.LevelScore(LevelId, rest, LastFirstClear, Tune);
            if (_store != null) RanchSpieleProgress.RecordWin(_store, RanchSpieleProgress.Herde, LevelId, LastStars, LastScore);
            else RanchSpieleProgress.RecordWin(_localSave, RanchSpieleProgress.Herde, LevelId, LastStars, LastScore);
            SessionScore += LastScore;
            SetPhase(PhaseWon);
        }

        private void SetPhase(string phase)
        {
            Phase = phase;
            PhaseTime = 0.0;
            PhaseChanged?.Invoke(phase);
        }
    }
}
