using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Run driver of the competitions (comp_lauf.gd, RW-5) without the 3D stage: builds the discipline's judge, the ride
    /// controller at the start pose inside its bounds and the best old run as a ghost; every tick moves the rider, feeds
    /// the judge, records the new ghost and reports events — until the judge is done. Consumers: ranchTonnen, ranchZeit,
    /// ranchTurnier. Two Godot gaps are closed here: the judge gets the rider's real airborne state (Godot passed a
    /// never-set flag, so every show jump counted as a knock-down), and a show tap reports its hit/miss as an event.
    /// </summary>
    public sealed class RcompLauf
    {
        public sealed class Config
        {
            public string Disziplin = "springen", Klasse = "holz";
            public int Seed = 1;
            public JObject Balance;
            public IReadOnlyDictionary<string, double> Stats;
            public bool KannToelt;
            public double? Bindung;
            /// <summary>Coat of the ridden horse (save field "farbe"), only for the stage.</summary>
            public string Farbe = "braun";
            public string GeistB64 = "";
            public List<RcompRennBot> Bots;
            public List<RcompKurs.Tor> Route;
            public double ZielS = 60.0;
            public double? IdealS;
            public double Pflege = 70.0, Stil = 40.0;
        }

        public string Disziplin { get; }
        public string Klasse { get; }
        public string Farbe { get; }
        public int Seed { get; }
        public bool Laeuft { get; private set; }
        public bool Gestartet { get; private set; }
        public double Zeit { get; private set; }
        public IRcompRichter Richter { get; }
        public RideController Controller { get; }
        public RanchCompGhost.Geist Geist { get; }
        public double GeistZeit { get; private set; }
        public bool GeistSichtbar { get; private set; }
        public List<RcompRennBot> Bots { get; } = new List<RcompRennBot>();
        public RcompErgebnis Ergebnis { get; private set; }
        public bool FertigGemeldet { get; private set; }
        public V2 SchauPferdPos { get; } = V2.Zero;

        public event Action<RcompEvent> Ereignis;
        public event Action<RcompErgebnis> LaufFertig;

        private RanchCompGhost.Recorder _rec;
        private V2 _posVorher;

        public RcompLauf(Config cfg)
        {
            Disziplin = cfg.Disziplin ?? "springen";
            Klasse = cfg.Klasse ?? "holz";
            Farbe = string.IsNullOrEmpty(cfg.Farbe) ? "braun" : cfg.Farbe;
            Seed = cfg.Seed;
            var balance = cfg.Balance ?? new JObject();
            switch (Disziplin)
            {
                case "springen":
                {
                    var r = new RcompRichterSpringen();
                    r.Setup(balance, Klasse);
                    Richter = r;
                    break;
                }
                case "dressur":
                {
                    var r = new RcompRichterDressur();
                    r.Setup();
                    Richter = r;
                    break;
                }
                case "gelaende":
                {
                    var r = new RcompRichterGelaende();
                    r.Setup(balance, Klasse, Seed);
                    Richter = r;
                    break;
                }
                case "zeit":
                {
                    var r = new RcompRichterGelaende();
                    r.SetupRoute(cfg.Route ?? new List<RcompKurs.Tor>(), cfg.ZielS);
                    Richter = r;
                    break;
                }
                case "rennen":
                {
                    var r = new RcompRichterRennen();
                    Bots.AddRange(cfg.Bots ?? new List<RcompRennBot>());
                    r.SetupBots(Bots);
                    Richter = r;
                    break;
                }
                case "trail":
                    Richter = new RcompRichterTrail();
                    break;
                case "schau":
                {
                    var r = new RcompRichterSchau();
                    r.Setup(Seed);
                    r.SetzeBasis(cfg.Pflege, cfg.Stil);
                    Richter = r;
                    break;
                }
                default:
                {
                    Disziplin = "tonnen";
                    var r = new RcompRichterTonnen();
                    r.Setup(Klasse);
                    if (cfg.IdealS.HasValue) r.SetzeIdeal(cfg.IdealS.Value);
                    Richter = r;
                    break;
                }
            }
            if (Disziplin != "schau")
            {
                Controller = new RideController { Active = false };
                if (cfg.Stats != null) Controller.SetStats(cfg.Stats, cfg.KannToelt);
                if (cfg.Bindung.HasValue) Controller.SetBindung(cfg.Bindung.Value);
                var (pos, heading) = StartPose();
                Controller.Pos = pos;
                Controller.Heading = heading;
                Controller.SetBounds(V2.Zero, ReitGrenzen(Disziplin));
                if (Richter is RcompRichterSpringen springen)
                {
                    Controller.SetHindernisse(springen.HindernisPunkte());
                    Controller.SprungGewertet += (wertung, _) => springen.NotiereSprung(wertung);
                }
            }
            if (Disziplin == "rennen") BotsBewegen();
            if (!string.IsNullOrEmpty(cfg.GeistB64) && Disziplin != "schau" && Disziplin != "rennen")
            {
                var geist = RanchCompGhost.FromB64(cfg.GeistB64);
                if (geist.Ok) Geist = geist;
            }
            _posVorher = SpielerPos;
        }

        public V2 SpielerPos => Controller?.Pos ?? SchauPferdPos;

        public void Starte()
        {
            Laeuft = true;
            Gestartet = true;
            Zeit = 0.0;
            FertigGemeldet = false;
            if (Controller != null) Controller.Active = true;
            _posVorher = SpielerPos;
            if (Disziplin != "schau") _rec = RanchCompGhost.NeuerRecorder(Disziplin, _posVorher);
            if (Geist != null)
            {
                GeistZeit = 0.0;
                GeistSichtbar = true;
            }
        }

        public void Tick(double dt)
        {
            if (!Laeuft || dt <= 0.0) return;
            Zeit += dt;
            Controller?.Step(dt);
            var jetzt = SpielerPos;
            if (Disziplin == "rennen") BotsBewegen();
            var gait = Controller?.Gait ?? "stand";
            var inLuft = Controller?.InLuft ?? false;
            foreach (var e in Richter.Tick(_posVorher, jetzt, gait, inLuft, dt)) Ereignis?.Invoke(e);
            if (_rec != null && Controller != null) RanchCompGhost.Tick(_rec, dt, jetzt, Controller.Y, Controller.Heading, gait, inLuft);
            _posVorher = jetzt;
            if (GeistSichtbar)
            {
                GeistZeit += dt;
                if (GeistZeit > Geist.DauerS + 1.5) GeistSichtbar = false;
            }
            if (Richter.Fertig && !FertigGemeldet) Beende();
        }

        /// <summary>Show: the routine tap; the hit or miss is reported like any judge event.</summary>
        public RcompEvent Tippe()
        {
            if (!(Richter is RcompRichterSchau schau) || !Laeuft) return null;
            var e = schau.Tippe();
            if (e != null && e.Typ != "zu_frueh") Ereignis?.Invoke(e);
            if (schau.Fertig && !FertigGemeldet) Beende();
            return e;
        }

        public RanchCompGhost.Zustand? GeistZustand() => GeistSichtbar && Geist != null ? RanchCompGhost.ZustandBei(Geist, GeistZeit) : (RanchCompGhost.Zustand?)null;

        public RcompHudInfo HudInfo() => Richter.Hud();

        /// <summary>Where the floating marker points: the next obstacle/gate/waypoint/barrel (null = none).</summary>
        public V2? MarkerZiel()
        {
            switch (Richter)
            {
                case RcompRichterSpringen s: return s.Idx < s.Kurs.Count ? s.Kurs[s.Idx].Pos : (V2?)null;
                case RcompRichterGelaende g: return g.Idx < g.Tore.Count ? g.Tore[g.Idx].Pos : (V2?)null;
                case RcompRichterTrail t: return t.Fertig ? (V2?)null : t.AktuelleStation.Pos;
                case RcompRichterDressur d: return d.Fertig ? (V2?)null : d.ZielPunkt();
                case RcompRichterTonnen tn: return tn.Idx < tn.Tonnen.Length ? tn.Tonnen[tn.Idx] : RcompKurs.TonnenStart;
            }
            return null;
        }

        public static V2 ReitGrenzen(string disziplin)
        {
            switch (disziplin)
            {
                case "gelaende":
                case "zeit": return new V2(94f, 69f);
                case "rennen": return new V2(46f, 26f);
            }
            return new V2(29f, 17f);
        }

        public (V2 pos, double heading) StartPose()
        {
            switch (Disziplin)
            {
                case "springen": return (new V2(-27f, 11f), -Math.PI * 0.5);
                case "dressur": return (new V2(9f, 6f), 0.0);
                case "gelaende":
                {
                    var start = new V2(0f, 30f);
                    var tore = ((RcompRichterGelaende)Richter).Tore;
                    return (start, tore.Count == 0 ? Math.PI : HeadingZu(start, tore[0].Pos));
                }
                case "zeit":
                {
                    var start = new V2(0f, 58f);
                    var tore = ((RcompRichterGelaende)Richter).Tore;
                    return (start, tore.Count == 0 ? 0.0 : HeadingZu(start, tore[0].Pos));
                }
                case "rennen": return (RcompKurs.RennenPunkt(0.0), -Math.PI * 0.5);
                case "trail": return (new V2(-25f, 8f), -Math.PI * 0.5);
                case "tonnen": return (RcompKurs.TonnenStart, 0.0);
            }
            return (V2.Zero, Math.PI);
        }

        /// <summary>Heading that rides from one point toward another (forward = −(sin h, cos h)).</summary>
        public static double HeadingZu(V2 von, V2 nach)
        {
            var d = nach - von;
            return Math.Atan2(-d.X, -d.Y);
        }

        private void BotsBewegen()
        {
            var rennen = (RcompRichterRennen)Richter;
            for (var i = 0; i < Bots.Count; i++)
            {
                var bot = Bots[i];
                bot.FortschrittM = rennen.BotFortschrittM(bot, Zeit);
                var punkt = RcompKurs.RennenPunkt(bot.FortschrittM);
                var voraus = RcompKurs.RennenPunkt(bot.FortschrittM + 0.8);
                var tangente = (voraus - punkt).Normalized();
                var quer = new V2(-tangente.Y, tangente.X);
                var lane = (i - (Bots.Count - 1) * 0.5) * 1.15;
                bot.Pos = punkt + quer * lane;
                bot.HatPos = true;
                bot.Heading = Math.Atan2(-tangente.X, -tangente.Y);
            }
        }

        private void Beende()
        {
            FertigGemeldet = true;
            Laeuft = false;
            if (Controller != null) Controller.Active = false;
            var ergebnis = Richter.Ergebnis(Zeit);
            ergebnis.Disziplin = Disziplin;
            ergebnis.GeistB64 = _rec != null && _rec.Samples.Count > 0 ? RanchCompGhost.ToB64(_rec) : "";
            Ergebnis = ergebnis;
            LaufFertig?.Invoke(ergebnis);
        }
    }
}
