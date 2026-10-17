using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Tournament league session (turnier_game.gd, RW-5): menu (league panel, start class, ghost toggle, 7 disciplines
    /// with today's bonus ones) → briefing with rules, set time and the pre-simulated field → 1.5 s intro → the run
    /// (<see cref="RcompLauf"/>) → standings, reward booked into ranch.comp + economy.coins (gold, league points,
    /// ribbon, trophy, promotion) → ceremony for places 1–3 → back to the menu; every discipline once per session.
    /// Session points: 20 + 10 per league point. "Fertig" ends the session.
    /// </summary>
    public sealed class RanchTurnierGame : IRcompRide
    {
        public const double IntroSec = 1.5;
        public const int TurniertagSeed = 777, ScoreBasis = 20, ScoreJeLigaPunkt = 10;
        public const string PhaseMenu = "menu", PhaseEinweisung = "einweisung", PhaseIntro = "intro", PhaseLauf = "lauf", PhaseErgebnis = "ergebnis", PhaseZeremonie = "zeremonie";

        public string Difficulty { get; }
        public string Phase { get; private set; } = PhaseMenu;
        public JObject Balance { get; }
        public List<string> Plan { get; }
        public string Datum { get; }
        public string Disziplin { get; private set; } = "springen";
        public string Klasse { get; private set; } = "holz";
        public bool GeistAn { get; private set; } = true;
        public Dictionary<string, int> Gespielt { get; } = new Dictionary<string, int>();
        public List<RcompStarter> Bots { get; private set; } = new List<RcompStarter>();
        public List<RcompStarter> Stand { get; private set; } = new List<RcompStarter>();
        public RcompLauf Lauf { get; private set; }
        public RcompErgebnis LastErgebnis { get; private set; }
        public RanchCompTurnier.Bericht LastBericht { get; private set; }

        /// <summary>The XP booking of the last run onto the ridden horse (null without a horse): level-ups and gifts.</summary>
        public RanchPferd.XpBuchung LastPferdXp { get; private set; }

        /// <summary>QA: the bot rides and briefs, but leaves the result card to the player.</summary>
        public bool BotBleibtBeimErgebnis { get; set; }
        public int LastPlatz { get; private set; }
        public bool LastGeistNeu { get; private set; }
        public int LastPunkte { get; private set; }
        public string PferdId { get; private set; } = "";
        public JObject Pferd { get; private set; } = new JObject();
        public double LevelIntroLeft { get; private set; }
        public double PhaseTime { get; private set; }
        public int SessionScore { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public double AssistStaerke { get; }
        /// <summary>A friend's ghost (G5 Base64) for a discipline, loaded from the ranch leaderboard; it replaces the own best run.</summary>
        public Func<string, string> FreundGeist { get; set; }
        /// <summary>The run on the stage races a friend's ghost (not the own best one).</summary>
        public bool LaufMitFreundGeist { get; private set; }

        public event Action<string> PhaseChanged;
        public event Action Finished_;

        private readonly GameStateStore _store;
        private readonly JObject _localSave = JObject.Parse(@"{""economy"":{""coins"":0}}");
        private readonly long _runSeed;
        private bool _botOn;
        private double _steer;

        public RanchTurnierGame(string difficulty, long runSeed, GameStateStore store = null, string datum = null)
        {
            Difficulty = difficulty;
            _runSeed = runSeed;
            _store = store;
            Balance = RanchCompKatalog.LoadBalance();
            Datum = datum ?? store?.Clock.LocalDay() ?? "2026-10-01";
            Plan = RanchCompLiga.TurniertagPlan(Balance, Datum, TurniertagSeed);
            AssistStaerke = difficulty == "easy" ? 0.3 : 0.0;
            LadePferd();
            Klasse = BesteErlaubteKlasse(Klasse);
        }

        public JObject Save => _store?.State ?? _localSave;
        public JObject Comp => RanchCompState.Lese(Save);
        public string LigaKlasse => (string)Comp["klasse"] ?? "holz";
        public int PferdLevel => Math.Max(1, (int)RanchCompKatalog.Num(Pferd["level"], 1.0));
        public string PferdName => Pferd["name"]?.Type == JTokenType.String && (string)Pferd["name"] != "" ? (string)Pferd["name"] : null;
        public bool AcceptsInput => Phase == PhaseLauf && Lauf != null && Lauf.Laeuft;
        public bool ZeitGewinnt => RanchCompKatalog.ZeitGewinnt(Balance, Disziplin);
        public double Richtzeit => RanchCompKatalog.RichtzeitS(Balance, Disziplin, Klasse);
        public bool IstTurniertag(string disziplin) => Plan.Contains(disziplin);

        public bool StartErlaubt(string klasse) => RanchCompLiga.StartErlaubt(Balance, LigaKlasse, klasse, PferdLevel);

        public void SetKlasse(string klasse)
        {
            if (Phase == PhaseMenu && StartErlaubt(klasse)) Klasse = klasse;
        }

        public void SetGeistAn(bool an) => GeistAn = an;

        /// <summary>Briefing for a discipline: the bots ride first (deterministic from the run seed).</summary>
        public bool OeffneEinweisung(string disziplin)
        {
            if (Finished || Phase != PhaseMenu || Gespielt.ContainsKey(disziplin) || Array.IndexOf(RanchCompKatalog.Disziplinen, disziplin) < 0) return false;
            Disziplin = disziplin;
            Klasse = BesteErlaubteKlasse(Klasse);
            Bots = RanchCompTurnier.BotsSimulieren(Balance, disziplin, Klasse, LaufSeed());
            Lauf = BaueLauf();
            SetPhase(PhaseEinweisung);
            return true;
        }

        public void ZurueckZumMenu()
        {
            if (Phase == PhaseLauf || Phase == PhaseIntro) return;
            Lauf = null;
            LadePferd();
            SetPhase(PhaseMenu);
        }

        /// <summary>The run is built with the briefing (the stage shows its course behind the card) and started on "Los".</summary>
        public bool StarteLauf()
        {
            if (Finished || Phase != PhaseEinweisung) return false;
            Lauf = BaueLauf();
            Lauf.LaufFertig += OnLaufFertig;
            LevelIntroLeft = IntroSec;
            _steer = 0.0;
            SetPhase(PhaseIntro);
            return true;
        }

        private RcompLauf BaueLauf()
        {
            var cfg = new RcompLauf.Config { Disziplin = Disziplin, Klasse = Klasse, Seed = LaufSeed(), Balance = Balance };
            var freund = GeistAn ? FreundGeist?.Invoke(Disziplin) : null;
            LaufMitFreundGeist = !string.IsNullOrEmpty(freund);
            if (GeistAn) cfg.GeistB64 = LaufMitFreundGeist ? freund : (string)RanchCompState.Geist(Save, Disziplin)["b64"] ?? "";
            if (Pferd["bindung"] != null) cfg.Bindung = RanchCompKatalog.Num(Pferd["bindung"], 50.0);
            if (PferdId != "")
            {
                cfg.Stats = RanchPferd.StatsEffektiv(Pferd);
                cfg.KannToelt = RanchPferd.KannToelt(Pferd);
                cfg.Farbe = RanchBelohnungen.FellId(Pferd);
            }
            if (Disziplin == "rennen")
                cfg.Bots = Bots.Select(b => new RcompRennBot { Id = b.Id, Name = b.Name, Pferd = b.Pferd, Farbe = b.Farbe, ZeitS = b.ZeitS }).ToList();
            if (Disziplin == "schau")
            {
                var gear = PferdId != "" ? Save["ranch"]?["wirtschaft"]?["gear"]?["equippedByHorse"]?[PferdId] : null;
                var (pflege, stil) = RcompRichterSchau.BasisAusPferd(Pferd, gear);
                cfg.Pflege = pflege;
                cfg.Stil = stil;
            }
            return new RcompLauf(cfg);
        }

        public void NachErgebnis()
        {
            if (Phase != PhaseErgebnis) return;
            if (LastPlatz <= 3) SetPhase(PhaseZeremonie);
            else ZurueckZumMenu();
        }

        public void ZeremonieWeiter()
        {
            if (Phase == PhaseZeremonie) ZurueckZumMenu();
        }

        public void Finish()
        {
            if (Finished || Phase == PhaseLauf || Phase == PhaseIntro) return;
            Finished = true;
            FinalScore = SessionScore;
            Finished_?.Invoke();
        }

        public void Steer(double value) => _steer = Math.Max(-1.0, Math.Min(1.0, value));

        public void GaitUp()
        {
            if (Lauf?.Controller != null && (Phase == PhaseIntro || Phase == PhaseLauf)) Lauf.Controller.GaitUp();
        }

        public void GaitDown()
        {
            if (Lauf?.Controller != null && (Phase == PhaseIntro || Phase == PhaseLauf)) Lauf.Controller.GaitDown();
        }

        public bool Jump() => AcceptsInput && Lauf.Controller != null && Lauf.Controller.Jump();
        public RcompEvent Tippe() => AcceptsInput ? Lauf.Tippe() : null;
        public bool ZweiterWind() => AcceptsInput && Lauf.Controller != null && Lauf.Controller.ZweiterWind();

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            PhaseTime += dt;
            switch (Phase)
            {
                case PhaseIntro:
                    LevelIntroLeft = Math.Max(0.0, LevelIntroLeft - Math.Min(dt, 0.25));
                    if (LevelIntroLeft <= 0.0)
                    {
                        Lauf.Starte();
                        SetPhase(PhaseLauf);
                    }
                    break;
                case PhaseLauf:
                    if (_botOn) RcompBot.Step(Lauf);
                    else if (Lauf.Controller != null)
                    {
                        var steer = _steer;
                        if (AssistStaerke > 0.0 && Lauf.MarkerZiel() is V2 ziel) steer = RideStats.Lenkassistent(steer, RcompBot.SteerZu(Lauf.Controller, ziel), AssistStaerke);
                        Lauf.Controller.SteerInput(steer);
                    }
                    _botOn = false;
                    Lauf.Tick(dt);
                    break;
            }
        }

        /// <summary>QA rider: every discipline once in menu order, the run by <see cref="RcompBot"/>, then "Fertig".</summary>
        public void ApplyBot()
        {
            if (Finished) return;
            switch (Phase)
            {
                case PhaseMenu:
                    var offen = RanchCompKatalog.Disziplinen.FirstOrDefault(d => !Gespielt.ContainsKey(d));
                    if (offen == null) Finish();
                    else OeffneEinweisung(offen);
                    break;
                case PhaseEinweisung:
                    if (PhaseTime >= 0.6) StarteLauf();
                    break;
                case PhaseLauf: _botOn = true; break;
                case PhaseErgebnis:
                    if (PhaseTime >= 1.2 && !BotBleibtBeimErgebnis) NachErgebnis();
                    break;
                case PhaseZeremonie:
                    if (PhaseTime >= 1.5) ZeremonieWeiter();
                    break;
            }
        }

        /// <summary>Run seed: session seed + discipline + entries so far (every attempt rolls anew, reproducibly).</summary>
        public int LaufSeed()
        {
            var idx = Math.Max(0, Array.IndexOf(RanchCompKatalog.Disziplinen, Disziplin));
            return (int)(_runSeed + idx * 1009 + (int)RanchCompKatalog.Num(Comp["teilnahmen"], 0.0) * 8117);
        }

        private void OnLaufFertig(RcompErgebnis ergebnis)
        {
            LastErgebnis = ergebnis;
            Stand = RanchCompTurnier.Endstand(Balance, Disziplin, Bots, ergebnis.Wert, ergebnis.ZeitS, PferdName ?? "");
            LastPlatz = RanchCompTurnier.SpielerPlatz(Stand);
            // the farm's build: the stand pays more gold, the riding hall and the jump course more dressage/jumping XP
            var lohn = RanchCompTurnier.Belohnung(Balance, Disziplin, Klasse, LastPlatz, IstTurniertag(Disziplin), RanchBauEffekte.HeimGoldMult(Save));
            var xpMult = RanchBauEffekte.TurnierXpMult(Save, Disziplin);
            if (xpMult > 1.0) lohn.Xp = (int)Math.Round(lohn.Xp * xpMult, MidpointRounding.AwayFromZero);
            var zeitGewinnt = ZeitGewinnt;
            void Book(JObject s)
            {
                LastBericht = RanchCompTurnier.Verbuche(s, Balance, Disziplin, Klasse, LastPlatz, lohn);
                // turnier_game.gd: the class XP of the result card goes onto the ridden horse
                LastPferdXp = RanchPferd.XpAnPferd(s, PferdId, lohn.Xp, Datum);
                LastGeistNeu = RanchCompState.GeistSpeichern(s, Disziplin, ergebnis.GeistB64, ergebnis.Wert, ergebnis.ZeitS, Datum, PferdId, zeitGewinnt);
            }
            if (_store != null)
            {
                _store.Update(Book);
                _store.NotifySliceChanged("ranch");
                _store.NotifySliceChanged("economy");
            }
            else Book(_localSave);
            Gespielt[Disziplin] = LastPlatz;
            LastPunkte = ScoreBasis + LastBericht.LigaPunkte * ScoreJeLigaPunkt;
            SessionScore += LastPunkte;
            SetPhase(PhaseErgebnis);
        }

        private void LadePferd()
        {
            var bestes = RanchCompTurnier.BestesPferd(Save);
            PferdId = bestes?.id ?? "";
            Pferd = bestes?.pferd ?? new JObject();
        }

        private string BesteErlaubteKlasse(string wunsch)
        {
            if (StartErlaubt(wunsch)) return wunsch;
            var beste = "holz";
            foreach (var k in RanchCompKatalog.Klassen) if (StartErlaubt(k)) beste = k;
            return beste;
        }

        private void SetPhase(string phase)
        {
            Phase = phase;
            PhaseTime = 0.0;
            PhaseChanged?.Invoke(phase);
        }
    }
}
