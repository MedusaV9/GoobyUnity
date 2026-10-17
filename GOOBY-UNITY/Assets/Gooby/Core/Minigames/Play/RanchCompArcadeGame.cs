using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>What a ride HUD drives: steering, gaits, the jump, the show tap and the second wind of the current run.</summary>
    public interface IRcompRide
    {
        RcompLauf Lauf { get; }
        bool AcceptsInput { get; }
        void Steer(double value);
        void GaitUp();
        void GaitDown();
        bool Jump();
        RcompEvent Tippe();
        bool ZweiterWind();
    }

    /// <summary>
    /// Session of the two RW-5 arcade rides (tonnen_game.gd / zeit_game.gd): run select (10, unlocked in order) → 1.5 s
    /// intro beat → the run (<see cref="RcompLauf"/> with the ride controller) → stars against the run's target
    /// (barrel scale 1.0/1.15/1.35), score, progress in <c>ranch.comp.arcade.&lt;spiel&gt;</c> and the best run as a ghost.
    /// Too slow books nothing. "Fertig" ends the session. On easy a 30 % steering assist pulls toward the next target.
    /// </summary>
    public abstract class RanchCompArcadeGame : IRcompRide
    {
        public const double IntroSec = 1.5;
        public const string PhaseSelect = "select", PhaseIntro = "intro", PhasePlay = "play", PhaseEnd = "end";

        public string Spiel { get; }
        public string Difficulty { get; }
        public string Phase { get; private set; } = PhaseSelect;
        public int LevelId { get; private set; }
        public RcompLauf Lauf { get; private set; }
        public double LevelIntroLeft { get; private set; }
        public double PhaseTime { get; private set; }
        public int SessionScore { get; private set; }
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public int LastStars { get; private set; }
        public int LastScore { get; private set; }
        public double LastWertung { get; private set; }
        public bool LastGeistNeu { get; private set; }
        public bool LastFirstClear { get; private set; }
        public RcompErgebnis LastErgebnis { get; private set; }
        public double AssistStaerke { get; }
        public JObject Balance { get; }

        public event Action<string> PhaseChanged;
        public event Action<RcompEvent> Ereignis;
        public event Action Finished_;

        protected readonly GameStateStore Store;
        private readonly JObject _localSave = new JObject();
        private readonly long _runSeed;
        private bool _botOn;
        private double _steer;

        protected RanchCompArcadeGame(string spiel, string difficulty, long runSeed, GameStateStore store)
        {
            Spiel = spiel;
            Difficulty = difficulty;
            _runSeed = runSeed;
            Store = store;
            Balance = RanchCompKatalog.LoadBalance();
            AssistStaerke = difficulty == "easy" ? 0.3 : 0.0;
        }

        public JObject Save => Store?.State ?? _localSave;
        public long RunSeed => _runSeed;
        public bool IsUnlocked(int id) => id >= 1 && id <= RanchCompState.ArcadeMaxUnlocked(Save, Spiel);
        public bool AcceptsInput => Phase == PhasePlay && Lauf != null && Lauf.Laeuft;
        public bool BotOn => _botOn;

        /// <summary>The run's target time (barrel ideal or route target).</summary>
        public abstract double Ziel(int level);

        /// <summary>The ghost key of a run (tonnen shares the tournament's; the time trial keeps one per route).</summary>
        public abstract string GeistKey(int level);

        protected abstract RcompLauf.Config BuildConfig(int level, JObject geist);

        public bool Open(int id)
        {
            if (Finished || id < 1 || id > RanchCompArcade.LevelAnzahl) return false;
            LevelId = id;
            var pferd = RanchCompTurnier.BestesPferd(Save);
            var cfg = BuildConfig(id, RanchCompState.Geist(Save, GeistKey(id)));
            cfg.Balance = Balance;
            if (pferd.HasValue && pferd.Value.pferd["bindung"] != null) cfg.Bindung = RanchCompKatalog.Num(pferd.Value.pferd["bindung"], 50.0);
            if (pferd.HasValue)
            {
                cfg.Stats = RanchPferd.StatsEffektiv(pferd.Value.pferd);
                cfg.KannToelt = RanchPferd.KannToelt(pferd.Value.pferd);
                cfg.Farbe = (string)pferd.Value.pferd["farbe"] ?? "braun";
            }
            Lauf = new RcompLauf(cfg);
            Lauf.Ereignis += e => Ereignis?.Invoke(e);
            Lauf.LaufFertig += OnLaufFertig;
            LevelIntroLeft = IntroSec;
            _steer = 0.0;
            SetPhase(PhaseIntro);
            return true;
        }

        public void Retry()
        {
            if (LevelId > 0) Open(LevelId);
        }

        public void Next()
        {
            if (LevelId < RanchCompArcade.LevelAnzahl && IsUnlocked(LevelId + 1)) Open(LevelId + 1);
            else BackToSelect();
        }

        public void BackToSelect()
        {
            Lauf = null;
            SetPhase(PhaseSelect);
        }

        public void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = SessionScore;
            Finished_?.Invoke();
        }

        /// <summary>Steering input −1..1 from the HUD (the easy assist blends toward the next target).</summary>
        public void Steer(double value) => _steer = Math.Max(-1.0, Math.Min(1.0, value));

        public void GaitUp()
        {
            if (Lauf?.Controller != null && Phase != PhaseSelect && Phase != PhaseEnd) Lauf.Controller.GaitUp();
        }

        public void GaitDown()
        {
            if (Lauf?.Controller != null && Phase != PhaseSelect && Phase != PhaseEnd) Lauf.Controller.GaitDown();
        }

        public bool ZweiterWind() => AcceptsInput && Lauf.Controller.ZweiterWind();
        public bool Jump() => AcceptsInput && Lauf.Controller.Jump();
        public RcompEvent Tippe() => null;

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
                        SetPhase(PhasePlay);
                    }
                    break;
                case PhasePlay:
                    if (_botOn) RcompBot.Step(Lauf);
                    else ApplySteer();
                    _botOn = false;
                    Lauf.Tick(dt);
                    break;
            }
        }

        public void ApplyBot()
        {
            if (Finished) return;
            switch (Phase)
            {
                case PhaseSelect: Open(RanchCompState.ArcadeMaxUnlocked(Save, Spiel)); break;
                case PhasePlay: _botOn = true; break;
                case PhaseEnd:
                    if (PhaseTime < 1.5) break;
                    if (LastStars > 0) Next();
                    else Retry();
                    break;
            }
        }

        private void ApplySteer()
        {
            var c = Lauf.Controller;
            if (c == null) return;
            var steer = _steer;
            if (AssistStaerke > 0.0 && Lauf.MarkerZiel() is V2 ziel) steer = RideStats.Lenkassistent(steer, RcompBot.SteerZu(c, ziel), AssistStaerke);
            c.SteerInput(steer);
        }

        private void OnLaufFertig(RcompErgebnis ergebnis)
        {
            LastErgebnis = ergebnis;
            LastWertung = ergebnis.Wert;
            var ziel = Ziel(LevelId);
            LastStars = RanchCompArcade.Sterne(LastWertung, ziel);
            LastGeistNeu = false;
            LastFirstClear = false;
            LastScore = 0;
            if (LastStars > 0)
            {
                LastFirstClear = !RanchCompState.ArcadeCleared(Save, Spiel, LevelId);
                LastScore = RanchCompArcade.Score(LastStars, LastWertung, ziel, LastFirstClear);
                var datum = Store?.Clock.LocalDay() ?? "";
                var pferd = RanchCompTurnier.BestesPferd(Save)?.id ?? "";
                if (Store != null)
                {
                    Store.Update(s => Book(s, ergebnis, datum, pferd));
                    Store.NotifySliceChanged("ranch");
                }
                else Book(_localSave, ergebnis, datum, pferd);
                SessionScore += LastScore;
            }
            SetPhase(PhaseEnd);
        }

        private void Book(JObject state, RcompErgebnis ergebnis, string datum, string pferd)
        {
            RanchCompState.ArcadeWin(state, Spiel, LevelId, LastStars, (int)Math.Round(LastWertung * 10.0, MidpointRounding.AwayFromZero), true);
            LastGeistNeu = RanchCompState.GeistSpeichern(state, GeistKey(LevelId), ergebnis.GeistB64, LastWertung, ergebnis.ZeitS, datum, pferd, true);
        }

        private void SetPhase(string phase)
        {
            Phase = phase;
            PhaseTime = 0.0;
            PhaseChanged?.Invoke(phase);
        }
    }

    /// <summary>Barrel race (ranchTonnen): cloverleaf around 3 barrels, the ideal time drops 26 → 17 s over 10 runs.</summary>
    public sealed class RanchTonnenGame : RanchCompArcadeGame
    {
        public RanchTonnenGame(string difficulty, long runSeed, GameStateStore store = null) : base("tonnen", difficulty, runSeed, store) { }

        public override double Ziel(int level) => RanchCompArcade.TonnenIdealS(level);
        public override string GeistKey(int level) => "tonnen";

        protected override RcompLauf.Config BuildConfig(int level, JObject geist) => new RcompLauf.Config
        {
            Disziplin = "tonnen",
            Klasse = "holz",
            Seed = (int)(RunSeed + level * 7919),
            IdealS = RanchCompArcade.TonnenIdealS(level),
            GeistB64 = (string)geist["b64"] ?? "",
        };
    }

    /// <summary>Time trial (ranchZeit): through the flag gates across the meadow against the clock and the own ghost.</summary>
    public sealed class RanchZeitGame : RanchCompArcadeGame
    {
        public RanchZeitGame(string difficulty, long runSeed, GameStateStore store = null) : base("zeit", difficulty, runSeed, store) { }

        public override double Ziel(int level) => RanchCompArcade.ZeitZielS(level);
        public override string GeistKey(int level) => RanchCompArcade.ZeitGeistKey(level);

        protected override RcompLauf.Config BuildConfig(int level, JObject geist) => new RcompLauf.Config
        {
            Disziplin = "zeit",
            Klasse = "holz",
            Seed = RanchCompArcade.ZeitKursSeed,
            Route = RanchCompArcade.ZeitRoute(level),
            ZielS = RanchCompArcade.ZeitZielS(level),
            GeistB64 = (string)geist["b64"] ?? "",
        };
    }

    /// <summary>
    /// The QA/certification rider: pursuit steering toward the judge's target, barrels circled on a tangent orbit (trot,
    /// 3.4 m), gallop on the open stretches while the tank holds, jumps at the perfect takeoff, the race line on the oval.
    /// </summary>
    public static class RcompBot
    {
        public const double OrbitRadius = 3.0;
        private static readonly int[] TonnenRichtung = { 1, -1, -1 };

        /// <summary>Steering (−1..1) that turns the horse toward a point (steering right lowers the heading).</summary>
        public static double SteerZu(RideController c, V2 ziel, double gain = 2.4)
        {
            var e = RideFeel.WrapAngle(RcompLauf.HeadingZu(c.Pos, ziel) - c.Heading);
            return Math.Max(-1.0, Math.Min(1.0, -e * gain));
        }

        private static double SteerRichtung(RideController c, V2 dir, double gain = 2.4)
        {
            var soll = Math.Atan2(-dir.X, -dir.Y);
            var e = RideFeel.WrapAngle(soll - c.Heading);
            return Math.Max(-1.0, Math.Min(1.0, -e * gain));
        }

        public static void Step(RcompLauf lauf)
        {
            var c = lauf.Controller;
            if (c == null)
            {
                if (lauf.Richter is RcompRichterSchau schau && schau.AktuellesKommando != null && Math.Abs(schau.Countdown) < 0.06) lauf.Tippe();
                return;
            }
            switch (lauf.Richter)
            {
                case RcompRichterTonnen t: Tonnen(c, t); break;
                case RcompRichterRennen r: Rennen(c, r); break;
                case RcompRichterSpringen s: Springen(c, s); break;
                case RcompRichterDressur d: Dressur(c, d); break;
                case RcompRichterTrail tr: Trail(c, tr); break;
                default:
                    if (lauf.MarkerZiel() is V2 ziel) Fahre(c, ziel, VorausWinkel(lauf, c));
                    break;
            }
        }

        private static void Wunsch(RideController c, string gait)
        {
            if (gait == "galopp" && c.Ausdauer < 6.0) gait = "trab";
            if (c.Gait == gait) return;
            var order = Array.IndexOf(RideFeel.Gangarten, gait);
            var jetzt = Array.IndexOf(RideFeel.Gangarten, c.Gait);
            if (order > jetzt) c.GaitUp();
            else if (order < jetzt) c.GaitDown();
        }

        private static void Fahre(RideController c, V2 ziel, double kurve)
        {
            c.SteerInput(SteerZu(c, ziel));
            var e = Math.Abs(RideFeel.WrapAngle(RcompLauf.HeadingZu(c.Pos, ziel) - c.Heading));
            Wunsch(c, e < 0.5 && kurve < 1.4 ? "galopp" : "trab");
        }

        /// <summary>How sharply the route bends after the next gate (rad) — the time-trial bot trots into hairpins.</summary>
        private static double VorausWinkel(RcompLauf lauf, RideController c)
        {
            if (!(lauf.Richter is RcompRichterGelaende g) || g.Idx >= g.Tore.Count) return 0.0;
            var a = g.Tore[g.Idx].Pos;
            if ((a - c.Pos).Length > 14f || g.Idx + 1 >= g.Tore.Count) return 0.0;
            var b = g.Tore[g.Idx + 1].Pos;
            var h1 = RcompLauf.HeadingZu(c.Pos, a);
            var h2 = RcompLauf.HeadingZu(a, b);
            return Math.Abs(RideFeel.WrapAngle(h2 - h1));
        }

        private static void Tonnen(RideController c, RcompRichterTonnen t)
        {
            if (t.Idx >= t.Tonnen.Length)
            {
                var heim = new V2(0f, 15f);
                c.SteerInput(SteerZu(c, heim));
                Wunsch(c, "galopp");
                return;
            }
            var b = t.Tonnen[t.Idx];
            var s = TonnenRichtung[t.Idx];
            var d = c.Pos - b;
            var dist = (double)d.Length;
            var theta = Math.Atan2(d.Y, d.X);
            if (dist > OrbitRadius * 1.25)
            {
                // ride the tangent line onto the orbit: the judge already counts the angle swept on the way in
                var alpha = Math.Acos(Math.Min(1.0, OrbitRadius / dist));
                var thetaT = theta + s * alpha;
                var tPunkt = b + new V2(Math.Cos(thetaT), Math.Sin(thetaT)) * OrbitRadius;
                c.SteerInput(SteerZu(c, tPunkt));
                var weg = (tPunkt - c.Pos).Length;
                var e = Math.Abs(RideFeel.WrapAngle(RcompLauf.HeadingZu(c.Pos, tPunkt) - c.Heading));
                Wunsch(c, weg > 5.5f && e < 0.45 ? "galopp" : "trab");
                return;
            }
            // on the orbit: feed-forward the yaw rate v/R (steering right grows the angle), correct radius and heading
            var tangente = new V2(-Math.Sin(theta) * s, Math.Cos(theta) * s);
            var damp = 1.0 - RideFeel.SteerSpeedDamp * Math.Min(1.0, c.Tempo / RideFeel.Tempo["galopp"]);
            var vorsteuerung = c.Tempo / OrbitRadius / (RideFeel.SteerRate * damp);
            var kursFehler = RideFeel.WrapAngle(Math.Atan2(-tangente.X, -tangente.Y) - c.Heading);
            c.SteerInput(Math.Max(-1.0, Math.Min(1.0, s * vorsteuerung + s * (dist - OrbitRadius) * 0.9 - kursFehler * 1.2)));
            Wunsch(c, "trab");
        }

        /// <summary>The race line 7 m ahead on the oval; gallop until the tank runs dry, walk it back up (trot neither drains
        /// nor refills), and sprint home as soon as the tank carries the rest of the race.</summary>
        private static void Rennen(RideController c, RcompRichterRennen r)
        {
            var s = RcompKurs.RennenSBei(c.Pos);
            c.SteerInput(SteerZu(c, RcompKurs.RennenPunkt(s + 7.0), 2.0));
            var m = Merk.GetOrCreateValue(c);
            var restM = (RcompRichterRennen.Runden * 4 - r.ViertelTreffer) * RcompKurs.RennenUmfang() * 0.25;
            var heimSprint = restM <= c.Ausdauer / RideStats.GaloppVerbrauch(RideStats.Leer) * RideFeel.Tempo["galopp"] - 6.0;
            if (m.Phase == 0 && c.Ausdauer < 6.0 && !heimSprint) m.Phase = 1;
            if (m.Phase == 1 && (c.Ausdauer > 80.0 || heimSprint)) m.Phase = 0;
            Wunsch(c, m.Phase == 0 ? "galopp" : "schritt");
        }

        private sealed class Merker
        {
            public int Idx = -1, Phase;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RideController, Merker> Merk =
            new System.Runtime.CompilerServices.ConditionalWeakTable<RideController, Merker>();

        /// <summary>Per obstacle three waypoints — far back to line up, the approach 4 m before, the landing 3 m behind — so
        /// the serpentine's U-turns never cut across an obstacle line; the takeoff lands in the perfect window.</summary>
        private static void Springen(RideController c, RcompRichterSpringen s)
        {
            if (s.Fertig) return;
            var h = s.Kurs[s.Idx];
            var richtung = new V2(Math.Sin(h.Heading), Math.Cos(h.Heading)) * -1.0;
            var rel = c.Pos - h.Pos;
            double along = rel.Dot(richtung);
            double lateral = rel.Dot(h.Quer);
            var ausgerichtet = Math.Abs(RideFeel.WrapAngle(c.Heading - h.Heading)) < 0.6;
            var m = Merk.GetOrCreateValue(c);
            if (m.Idx != s.Idx)
            {
                m.Idx = s.Idx;
                m.Phase = along < -6.0 && Math.Abs(lateral) < 2.0 && ausgerichtet ? 1 : 0;
            }
            var a = h.Pos - richtung * 10.0;
            var b = h.Pos - richtung * 4.0;
            var e = h.Pos + richtung * 3.0;
            if (m.Phase == 0 && (c.Pos - a).Length < 2.5f) m.Phase = 1;
            if (m.Phase == 1 && ((c.Pos - b).Length < 1.8f || (along > -4.5 && along < -1.5 && Math.Abs(lateral) < 1.2 && ausgerichtet))) m.Phase = 2;
            if (m.Phase == 2 && along > 2.5) m.Phase = 0;
            c.SteerInput(SteerZu(c, m.Phase == 0 ? a : m.Phase == 1 ? b : e));
            Wunsch(c, "trab");
            var abstand = c.NaechstesHindernisM();
            if (m.Phase == 2 && abstand > 0.95 && abstand < 1.25 && !c.InLuft) c.Jump();
        }

        /// <summary>Dressage: on the figure follow its line 2.5 m ahead (pure pursuit along the waypoints) in the asked gait.</summary>
        private static void Dressur(RideController c, RcompRichterDressur d)
        {
            if (d.Fertig) return;
            var pts = d.AktuelleFigur.Punkte;
            var ziel = pts[0];
            if (d.PunktIdx > 0)
            {
                var k = Math.Min(d.PunktIdx, pts.Count - 1);
                var a = pts[k - 1];
                var ab = pts[k] - a;
                var t = ab.LengthSquared > 1e-4f ? Math.Max(0.0, Math.Min(1.0, (c.Pos - a).Dot(ab) / ab.LengthSquared)) : 1.0;
                var punkt = a + ab * t;
                var rest = 2.5;
                ziel = pts[pts.Count - 1];
                for (var i = k; i < pts.Count; i++)
                {
                    var seg = (pts[i] - punkt).Length;
                    if (rest <= seg)
                    {
                        ziel = punkt + (pts[i] - punkt).Normalized() * rest;
                        break;
                    }
                    rest -= seg;
                    punkt = pts[i];
                }
            }
            c.SteerInput(SteerZu(c, ziel));
            Wunsch(c, d.SollGangart);
        }

        private static void Trail(RideController c, RcompRichterTrail t)
        {
            if (t.Fertig) return;
            var station = t.AktuelleStation;
            var eingang = RcompRichterTrail.Eingang(station);
            var lokal = RcompRichterTrail.Lokal(c.Pos, station);
            var imKorridor = Math.Abs(lokal.X) <= RcompKurs.TrailLaengeM * 0.5 + 1.0 && Math.Abs(lokal.Y) < station.Breite;
            var ziel = imKorridor || (c.Pos - eingang).Length < 1.5f ? RcompRichterTrail.Ausgang(station) + (RcompRichterTrail.Ausgang(station) - eingang).Normalized() * 2.0 : eingang - (RcompRichterTrail.Ausgang(station) - eingang).Normalized() * 3.0;
            if ((c.Pos - eingang).Length > 6f && !imKorridor) ziel = eingang - (RcompRichterTrail.Ausgang(station) - eingang).Normalized() * 3.0;
            c.SteerInput(SteerZu(c, ziel));
            Wunsch(c, imKorridor || (c.Pos - eingang).Length < 5f ? "schritt" : "trab");
        }
    }
}
