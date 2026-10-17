using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Obstacle course session — the run of parcours_game.gd around <see cref="RanchParcours"/>: course select (10) →
    /// ride (tempo ramps toward the course pace, ×1.3 while the gallop button is held) → a jump is rated at its takeoff
    /// against the first open obstacle inside the arc, the rating lands when the horse passes the obstacle centre (no
    /// jump = knock-down: +2.5 s, tempo ×0.6) → finish line → stars/score, booked in <c>ranch.spiele.parcours</c>.
    /// </summary>
    public sealed class RanchParcoursGame
    {
        public const double IntroSec = 1.5;
        public const string PhaseSelect = "select", PhasePlay = "play", PhaseWon = "won";

        public sealed class Hindernis
        {
            public double At;
            public string Typ;
            public RanchParcours.Wertung? Rating;
            public bool Gewertet, Knocked;
        }

        public List<JObject> Kurse { get; }
        public RanchParcours.Tune Tune { get; }
        public string Difficulty { get; }
        public string Phase { get; private set; } = PhaseSelect;
        public int KursId { get; private set; }
        public JObject Kurs { get; private set; }
        public readonly List<Hindernis> Hindernisse = new List<Hindernis>();
        public double X { get; private set; }
        public double Tempo { get; private set; }
        public bool Galopp { get; private set; }
        public double JumpY { get; private set; }
        public bool InLuft { get; private set; }
        public double Elapsed { get; private set; }
        public double Strafzeit { get; private set; }
        public int Punkte { get; private set; }
        public int Kombo { get; private set; }
        public int Abwuerfe { get; private set; }
        public double LevelIntroLeft { get; private set; }
        public double PhaseTime { get; private set; }
        public int SessionScore { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public int LastStars { get; private set; }
        public int LastScore { get; private set; }

        /// <summary>A jump landed over an obstacle (quality, points) or knocked it (quality "abwurf", 0).</summary>
        public event Action<Hindernis, string, int> Rated;
        public event Action Jumped;
        public event Action<string> PhaseChanged;
        public event Action Finished_;

        private readonly GameStateStore _store;
        private readonly JObject _localSave = new JObject();
        private double _vy;
        private bool _botOn;
        private readonly double _botSigma, _botGaloppAb;
        private double _botJitter;

        public RanchParcoursGame(string difficulty, long runSeed, GameStateStore store = null)
        {
            Difficulty = difficulty;
            _store = store;
            Tune = RanchParcours.ApplyDifficulty(RanchParcours.Base, difficulty);
            Kurse = RanchParcours.LoadKurse();
            _botSigma = difficulty == "easy" ? 0.45 : difficulty == "hard" ? 1.05 : 0.7;
            _botGaloppAb = difficulty == "easy" ? 13.0 : difficulty == "hard" ? 18.0 : 14.0;
            _rng = new Gooby.Core.Random.GoobyRng(runSeed);
        }

        private readonly Gooby.Core.Random.GoobyRng _rng;

        public JObject Save => _store?.State ?? _localSave;
        public bool IsUnlocked(int id) => id >= 1 && id <= RanchSpieleProgress.MaxUnlocked(Save, RanchSpieleProgress.Parcours);
        public bool AcceptsInput => Phase == PhasePlay && LevelIntroLeft <= 0.0;
        public double Laenge => RanchHerde.Num(Kurs, "laenge_m", 200.0);
        public double ParS => RanchHerde.Num(Kurs, "par_s", 60.0);
        public double Basis => RanchHerde.Num(Kurs, "tempo", 6.0);
        public double Zeit => Elapsed + Strafzeit;

        public bool Open(int id)
        {
            if (Finished) return false;
            var kurs = RanchParcours.KursById(Kurse, id);
            if (kurs == null) return false;
            KursId = id;
            Kurs = kurs;
            Hindernisse.Clear();
            foreach (var (at, typ) in RanchParcours.Hindernisse(kurs)) Hindernisse.Add(new Hindernis { At = at, Typ = typ });
            X = 0.0;
            Tempo = Basis;
            Galopp = false;
            JumpY = 0.0;
            _vy = 0.0;
            InLuft = false;
            Elapsed = 0.0;
            Strafzeit = 0.0;
            Punkte = 0;
            Kombo = 0;
            Abwuerfe = 0;
            LevelIntroLeft = IntroSec;
            _botJitter = (_rng.Next() * 2.0 - 1.0) * _botSigma;
            SetPhase(PhasePlay);
            return true;
        }

        public void Retry() { if (KursId > 0) Open(KursId); }

        public void Next()
        {
            if (KursId < RanchParcours.KursAnzahl && IsUnlocked(KursId + 1)) Open(KursId + 1);
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

        public void SetGalopp(bool on) { if (Phase == PhasePlay) Galopp = on; }

        /// <summary>Jump: rates the first open obstacle inside the arc right at the takeoff.</summary>
        public bool Jump()
        {
            if (!AcceptsInput || InLuft || !RideFeel.KannSpringen(Tempo, JumpY)) return false;
            JumpY = 0.001;
            _vy = RideFeel.SprungVy;
            InLuft = true;
            var weite = RideFeel.WeiteM(Tempo);
            foreach (var h in Hindernisse)
            {
                if (h.Rating.HasValue || h.Gewertet) continue;
                if (h.At <= X || h.At > X + weite) continue;
                h.Rating = RanchParcours.BewerteSprung(X, Tempo, h.At, h.Typ, Tune);
                break;
            }
            Jumped?.Invoke();
            return true;
        }

        /// <summary>The next obstacle not passed yet (null at the end).</summary>
        public Hindernis NextObstacle()
        {
            foreach (var h in Hindernisse) if (!h.Gewertet) return h;
            return null;
        }

        /// <summary>Ideal takeoff for an obstacle at the current tempo (arc middle over its centre).</summary>
        public double IdealTakeoff(Hindernis h) => h.At - RideFeel.WeiteM(Tempo) * 0.5;

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
            if (_botOn) BotStep();
            _botOn = false;
            Elapsed += dt;
            Tempo = RideFeel.StepTempo(Tempo, RanchParcours.LaufTempo(Basis, Galopp, Tune), dt);
            X += Tempo * dt;
            if (InLuft)
            {
                var s = RideFeel.StepSprung((JumpY, _vy), dt);
                JumpY = s.y;
                _vy = s.vy;
                if (JumpY <= 0.0) InLuft = false;
            }
            foreach (var h in Hindernisse)
            {
                if (h.At > X || h.Gewertet) continue;
                h.Gewertet = true;
                if (h.Rating.HasValue && h.Rating.Value.Qualitaet != "abwurf")
                {
                    var plus = RanchParcours.SprungPunkte(h.Rating.Value.Qualitaet, Kombo, Tune);
                    Kombo++;
                    Punkte += plus;
                    SessionScore += plus;
                    Rated?.Invoke(h, h.Rating.Value.Qualitaet, plus);
                }
                else
                {
                    h.Knocked = true;
                    Abwuerfe++;
                    Kombo = 0;
                    Strafzeit += Tune.AbwurfStrafeS;
                    Tempo *= Tune.AbwurfTempoMult;
                    Rated?.Invoke(h, "abwurf", 0);
                }
                _botJitter = (_rng.Next() * 2.0 - 1.0) * _botSigma;
            }
            if (X >= Laenge) Won();
        }

        public void ApplyBot()
        {
            if (Finished) return;
            switch (Phase)
            {
                case PhaseSelect: Open(RanchSpieleProgress.MaxUnlocked(Save, RanchSpieleProgress.Parcours)); break;
                case PhasePlay: _botOn = true; break;
                case PhaseWon: if (PhaseTime >= 1.5) Next(); break;
            }
        }

        /// <summary>The bot of simulate_lauf in real time: gallop on free stretches, jump at the ideal takeoff ± seeded error.</summary>
        private void BotStep()
        {
            var next = NextObstacle();
            Galopp = next == null || next.At - X > _botGaloppAb;
            if (next == null || InLuft || next.Rating.HasValue) return;
            if (X >= IdealTakeoff(next) + _botJitter) Jump();
        }

        private void Won()
        {
            var zeit = Zeit;
            LastStars = RanchParcours.Sterne(Abwuerfe, zeit, ParS);
            var first = !RanchSpieleProgress.IsCleared(Save, RanchSpieleProgress.Parcours, KursId);
            LastScore = RanchParcours.KursScore(Punkte, RanchParcours.Zeitbonus(ParS, zeit, Tune), first, KursId);
            if (_store != null) RanchSpieleProgress.RecordWin(_store, RanchSpieleProgress.Parcours, KursId, LastStars, LastScore);
            else RanchSpieleProgress.RecordWin(_localSave, RanchSpieleProgress.Parcours, KursId, LastStars, LastScore);
            SessionScore += LastScore - Punkte;
            Galopp = false;
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
