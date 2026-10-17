using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>A judge event (richter_*.gd): typ plus the optional index/number/points/gait/command it carries.</summary>
    public sealed class RcompEvent
    {
        public string Typ;
        public int Index = -1, Nummer;
        public double Punkte, AbweichungMs;
        public string Gangart, Id;

        public static RcompEvent Of(string typ, int index = -1) => new RcompEvent { Typ = typ, Index = index };
    }

    /// <summary>The result of a run: the scored value (points: higher wins, time: lower wins), the ride time, details.</summary>
    public sealed class RcompErgebnis
    {
        public double Wert, ZeitS;
        public JObject Detail = new JObject();
        public string Disziplin, GeistB64 = "";
    }

    /// <summary>The judge's HUD line: a string key with its parameters.</summary>
    public struct RcompHudInfo
    {
        public string Key, Name, Gangart;
        public int N, Max;
    }

    /// <summary>A pure judge: fed with the rider's positions every frame, it emits events and finally the result.</summary>
    public interface IRcompRichter
    {
        bool Fertig { get; }
        List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt);
        RcompErgebnis Ergebnis(double zeitS);
        RcompHudInfo Hud();
    }

    /// <summary>Show jumping (richter_springen.gd): obstacles in order; airborne = cleared, walk/stand = refusal, else a knock-down.</summary>
    public sealed class RcompRichterSpringen : IRcompRichter
    {
        public const double TorHalbbreite = 2.4, PerfektFensterS = 1.4;
        public List<RcompKurs.Tor> Kurs = new List<RcompKurs.Tor>();
        public int Idx, Abwuerfe, Verweigerungen, Perfekte;
        public double Richtzeit = 60.0;
        private double _perfektRest;

        public void Setup(JObject balance, string klasse)
        {
            Kurs = RcompKurs.Springen(klasse);
            Richtzeit = RanchCompKatalog.RichtzeitS(balance, "springen", klasse);
        }

        /// <summary>The ride controller rated a takeoff ("perfekt" | "gut" | "daneben").</summary>
        public void NotiereSprung(string wertung)
        {
            if (wertung == "perfekt") _perfektRest = PerfektFensterS;
        }

        public List<V2> HindernisPunkte()
        {
            var punkte = new List<V2>();
            foreach (var h in Kurs) punkte.Add(h.Pos);
            return punkte;
        }

        public bool Fertig => Idx >= Kurs.Count;

        public List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt)
        {
            _perfektRest = Math.Max(0.0, _perfektRest - dt);
            var events = new List<RcompEvent>();
            if (Fertig) return events;
            var h = Kurs[Idx];
            if (!RcompKurs.TorDurchritten(vorher, jetzt, h.Pos, h.Quer, TorHalbbreite)) return events;
            if (inLuft)
            {
                if (_perfektRest > 0.0)
                {
                    Perfekte++;
                    events.Add(RcompEvent.Of("perfekt", Idx));
                }
                else events.Add(RcompEvent.Of("hindernis_ok", Idx));
            }
            else if (gait == "stand" || gait == "schritt")
            {
                Verweigerungen++;
                events.Add(RcompEvent.Of("verweigert", Idx));
            }
            else
            {
                Abwuerfe++;
                events.Add(RcompEvent.Of("abwurf", Idx));
            }
            Idx++;
            return events;
        }

        public RcompErgebnis Ergebnis(double zeitS) => new RcompErgebnis
        {
            Wert = RcompWertung.Springen.Score(Abwuerfe, Verweigerungen, zeitS, Richtzeit, Perfekte),
            ZeitS = zeitS,
            Detail = new JObject { ["abwuerfe"] = Abwuerfe, ["verweigerungen"] = Verweigerungen, ["perfekte"] = Perfekte },
        };

        public RcompHudInfo Hud() => new RcompHudInfo { Key = "rcomp.hud.hindernis", N = Math.Min(Idx + 1, Kurs.Count), Max = Kurs.Count };
    }

    /// <summary>Dressage (richter_dressur.gd): five figures as waypoint chains with a target gait; line deviation is sampled.</summary>
    public sealed class RcompRichterDressur : IRcompRichter
    {
        public const double PunktToleranzM = 3.0, MessIntervallS = 0.4, FehlerJeS = 2.5, TaktGnadeMs = 400.0, TaktMaxS = 4.0;
        public const int MaxGangartfehler = 2;
        public List<RcompKurs.Figur> Figuren = new List<RcompKurs.Figur>();
        public int FigurIdx, PunktIdx;
        public readonly List<double> FigurenPunkte = new List<double>();
        public readonly List<double> TaktAbweichungen = new List<double>();
        private readonly List<double> _abweichungen = new List<double>();
        private double _falschS, _messRest, _wechselOffenS = -1.0;

        public void Setup()
        {
            Figuren = RcompKurs.DressurFiguren();
            _wechselOffenS = 0.0;
        }

        public bool Fertig => FigurIdx >= Figuren.Count;
        public RcompKurs.Figur AktuelleFigur => FigurIdx < Figuren.Count ? Figuren[FigurIdx] : null;

        public V2 ZielPunkt()
        {
            var figur = AktuelleFigur;
            if (figur == null) return V2.Zero;
            return figur.Punkte[Math.Min(PunktIdx, figur.Punkte.Count - 1)];
        }

        public string SollGangart => AktuelleFigur?.Gangart ?? "schritt";

        public List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt)
        {
            var events = new List<RcompEvent>();
            if (Fertig) return events;
            if (_wechselOffenS >= 0.0)
            {
                _wechselOffenS += dt;
                if (gait == SollGangart)
                {
                    TaktAbweichungen.Add(Math.Max(0.0, _wechselOffenS * 1000.0 - TaktGnadeMs));
                    _wechselOffenS = -1.0;
                    events.Add(RcompEvent.Of("gangart_ok"));
                }
                else if (_wechselOffenS > TaktMaxS)
                {
                    TaktAbweichungen.Add(9999.0);
                    _wechselOffenS = -1.0;
                }
            }
            else if (gait != SollGangart && gait != "stand") _falschS += dt;
            _messRest -= dt;
            if (_messRest <= 0.0)
            {
                _messRest = MessIntervallS;
                if (PunktIdx > 0) _abweichungen.Add(AbstandZurLinie(jetzt));
            }
            var ziel = ZielPunkt();
            if ((jetzt - ziel).Length <= PunktToleranzM)
            {
                PunktIdx++;
                if (PunktIdx >= AktuelleFigur.Punkte.Count) SchliesseFigur(events);
            }
            return events;
        }

        public RcompErgebnis Ergebnis(double zeitS)
        {
            var takt = TaktAbweichungen.Count >= Figuren.Count && RcompWertung.Dressur.TaktOk(TaktAbweichungen);
            return new RcompErgebnis
            {
                Wert = RcompWertung.Dressur.Gesamt(FigurenPunkte, takt),
                ZeitS = zeitS,
                Detail = new JObject { ["figuren"] = new JArray(FigurenPunkte), ["takt"] = takt },
            };
        }

        public RcompHudInfo Hud()
        {
            var figur = AktuelleFigur;
            return new RcompHudInfo { Key = "rcomp.hud.figur", N = Math.Min(FigurIdx + 1, Figuren.Count), Max = Figuren.Count, Name = figur?.Id ?? "", Gangart = SollGangart };
        }

        private void SchliesseFigur(List<RcompEvent> events)
        {
            var dMittel = 0.0;
            foreach (var w in _abweichungen) dMittel += w;
            dMittel /= Math.Max(1.0, _abweichungen.Count);
            var fehler = Math.Min((int)(_falschS / FehlerJeS), MaxGangartfehler);
            var wert = RcompWertung.Dressur.FigurPunkte(dMittel, fehler);
            FigurenPunkte.Add(wert);
            events.Add(new RcompEvent { Typ = "figur_ok", Index = FigurIdx, Punkte = wert });
            FigurIdx++;
            PunktIdx = 0;
            _abweichungen.Clear();
            _falschS = 0.0;
            if (!Fertig)
            {
                _wechselOffenS = 0.0;
                events.Add(new RcompEvent { Typ = "gangart_wechsel", Gangart = SollGangart });
            }
        }

        /// <summary>
        /// Distance to the figure's line — the nearer of the last two segments, so the 3 m waypoint switch does not count
        /// the corner still ahead as a deviation. Godot measured only the newest segment and already the ride to the
        /// first waypoint, which left every figure near 0 points.
        /// </summary>
        public double AbstandZurLinie(V2 pos)
        {
            var figur = AktuelleFigur;
            if (figur == null || PunktIdx <= 0) return 0.0;
            var b = figur.Punkte[Math.Min(PunktIdx, figur.Punkte.Count - 1)];
            var a = figur.Punkte[PunktIdx - 1];
            var d = RcompKurs.AbstandZurStrecke(pos, a, b);
            if (PunktIdx >= 2) d = Math.Min(d, RcompKurs.AbstandZurStrecke(pos, figur.Punkte[PunktIdx - 2], a));
            return d;
        }
    }

    /// <summary>Cross-country and the arcade time trial (richter_gelaende.gd): gates in order, skipped ones cost 8 s.</summary>
    public sealed class RcompRichterGelaende : IRcompRichter
    {
        public const double TorHalbbreite = 3.2;
        public List<RcompKurs.Tor> Tore = new List<RcompKurs.Tor>();
        public int Idx, Verpasst;
        public double Richtzeit = 150.0;

        public void Setup(JObject balance, string klasse, int seed)
        {
            Tore = RcompKurs.Gelaende(klasse, seed);
            Richtzeit = RanchCompKatalog.RichtzeitS(balance, "gelaende", klasse);
        }

        public void SetupRoute(List<RcompKurs.Tor> route, double zielS)
        {
            Tore = route;
            Richtzeit = zielS;
        }

        public bool Fertig => Idx >= Tore.Count;

        public List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt)
        {
            var events = new List<RcompEvent>();
            if (Fertig) return events;
            for (var j = Idx; j < Tore.Count; j++)
            {
                var tor = Tore[j];
                if (!RcompKurs.TorDurchritten(vorher, jetzt, tor.Pos, tor.Quer, TorHalbbreite)) continue;
                for (var uebersprungen = Idx; uebersprungen < j; uebersprungen++)
                {
                    Verpasst++;
                    events.Add(RcompEvent.Of("tor_verpasst", uebersprungen));
                }
                events.Add(new RcompEvent { Typ = "tor_ok", Index = j, Nummer = j + 1 });
                Idx = j + 1;
                break;
            }
            return events;
        }

        public RcompErgebnis Ergebnis(double zeitS) => new RcompErgebnis
        {
            Wert = RcompWertung.Gelaende.WertungS(zeitS, Verpasst),
            ZeitS = zeitS,
            Detail = new JObject { ["verpasst"] = Verpasst, ["tore"] = Tore.Count },
        };

        public RcompHudInfo Hud() => new RcompHudInfo { Key = "rcomp.hud.tor", N = Math.Min(Idx + 1, Tore.Count), Max = Tore.Count };
    }

    /// <summary>A pre-simulated race bot on the oval: its total time, and where the run driver currently put it.</summary>
    public sealed class RcompRennBot
    {
        public string Id, Name, Pferd, Farbe;
        public double ZeitS = 90.0, FortschrittM, Heading;
        public V2 Pos;
        public bool HatPos;
    }

    /// <summary>Grass race (richter_rennen.gd): 3 laps through quarter checkpoints in order; slipstream credits +3 % as track.</summary>
    public sealed class RcompRichterRennen : IRcompRichter
    {
        public const int Runden = 3;
        public const double BotNahM = 4.0;
        public List<RcompRennBot> Bots = new List<RcompRennBot>();
        public int ViertelTreffer;
        public double ExtraS;
        public bool ImZiel, BoostAktiv;
        private RcompWertung.Rennen.Zustand _zustand;
        private double _sJetzt, _boostSSumme;

        public void SetupBots(List<RcompRennBot> bots) => Bots = bots ?? new List<RcompRennBot>();

        public bool Fertig => ImZiel;

        public double BotFortschrittM(RcompRennBot bot, double t)
        {
            var zeit = Math.Max(1.0, bot.ZeitS);
            return Math.Max(0.0, Math.Min(1.0, t / zeit)) * Runden * RcompKurs.RennenUmfang();
        }

        public double SpielerFortschrittM()
        {
            var viertel = RcompKurs.RennenUmfang() * 0.25;
            var rest = RcompKurs.Fposmod(_sJetzt, viertel);
            return ViertelTreffer * viertel + Math.Min(rest, viertel) + ExtraS;
        }

        public int RundeJetzt => Math.Min(ViertelTreffer / 4 + 1, Runden);

        public List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt)
        {
            var events = new List<RcompEvent>();
            if (ImZiel) return events;
            var umfang = RcompKurs.RennenUmfang();
            _sJetzt = RcompKurs.RennenSBei(jetzt);
            var naechstes = RcompKurs.Fposmod((ViertelTreffer + 1) * umfang * 0.25, umfang);
            if (Math.Abs(_sJetzt - naechstes) < 6.0)
            {
                ViertelTreffer++;
                if (ViertelTreffer % 4 == 0 && ViertelTreffer < Runden * 4) events.Add(new RcompEvent { Typ = "runde", Nummer = ViertelTreffer / 4 + 1 });
            }
            if (ViertelTreffer >= Runden * 4)
            {
                ImZiel = true;
                events.Add(RcompEvent.Of("ziel"));
                return events;
            }
            Windschatten(jetzt, dt, events);
            return events;
        }

        public RcompErgebnis Ergebnis(double zeitS) => new RcompErgebnis
        {
            Wert = zeitS,
            ZeitS = zeitS,
            Detail = new JObject { ["boost_s"] = _boostSSumme, ["extra_m"] = ExtraS },
        };

        public RcompHudInfo Hud() => new RcompHudInfo { Key = "rcomp.hud.runde", N = RundeJetzt, Max = Runden };

        private void Windschatten(V2 jetzt, double dt, List<RcompEvent> events)
        {
            var eigenes = SpielerFortschrittM() - ExtraS;
            var dahinter = false;
            foreach (var bot in Bots)
            {
                var vorsprung = bot.FortschrittM - eigenes;
                var nah = !bot.HatPos || (jetzt - bot.Pos).Length < BotNahM;
                if (nah && RcompWertung.Rennen.ImFenster(vorsprung, 1.0))
                {
                    dahinter = true;
                    break;
                }
            }
            _zustand = RcompWertung.Rennen.StepWindschatten(_zustand, dahinter, dt);
            var boost = RcompWertung.Rennen.TempoMult(_zustand) > 1.0;
            if (boost && !BoostAktiv) events.Add(RcompEvent.Of("windschatten"));
            BoostAktiv = boost;
            if (boost)
            {
                _boostSSumme += dt;
                ExtraS += 6.0 * RcompWertung.Rennen.WindschattenBoost * dt;
            }
        }
    }

    /// <summary>Western trail (richter_trail.gd): six corridors in order, slow and centred; grazing the rails costs 2 points.</summary>
    public sealed class RcompRichterTrail : IRcompRichter
    {
        public const double BeruehrCooldownS = 0.8;
        public const int MaxBeruehrungen = 3;
        public RcompKurs.TrailStation[] Stationen = RcompKurs.TrailStationen;
        public int Idx;
        public readonly List<int> Punkte = new List<int>();
        private int _beruehrungen;
        private double _cooldown;

        public bool Fertig => Idx >= Stationen.Length;
        public RcompKurs.TrailStation AktuelleStation => Idx < Stationen.Length ? Stationen[Idx] : null;
        public int Beruehrungen => _beruehrungen;

        public List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt)
        {
            _cooldown = Math.Max(0.0, _cooldown - dt);
            var events = new List<RcompEvent>();
            if (Fertig) return events;
            var station = Stationen[Idx];
            var lokal = Lokal(jetzt, station);
            if (Math.Abs(lokal.X) <= RcompKurs.TrailLaengeM * 0.5 && _cooldown <= 0.0 && Math.Abs(lokal.Y) > station.Breite && Math.Abs(lokal.Y) <= station.Breite + 0.8
                && _beruehrungen < MaxBeruehrungen)
            {
                _beruehrungen++;
                events.Add(RcompEvent.Of("beruehrung", Idx));
                _cooldown = BeruehrCooldownS;
            }
            for (var j = Idx; j < Stationen.Length; j++)
            {
                var s = Stationen[j];
                if (!RcompKurs.TorDurchritten(vorher, jetzt, Ausgang(s), Quer(s), s.Breite + 0.8)) continue;
                for (var uebersprungen = Idx; uebersprungen < j; uebersprungen++)
                {
                    Punkte.Add(0);
                    events.Add(RcompEvent.Of("aufgabe_ausgelassen", uebersprungen));
                }
                var wert = RcompWertung.Trail.AufgabePunkte(_beruehrungen, false);
                Punkte.Add(wert);
                events.Add(new RcompEvent { Typ = "aufgabe_ok", Index = j, Punkte = wert });
                Idx = j + 1;
                _beruehrungen = 0;
                break;
            }
            return events;
        }

        public RcompErgebnis Ergebnis(double zeitS) => new RcompErgebnis
        {
            Wert = RcompWertung.Trail.Gesamt(Punkte, zeitS),
            ZeitS = zeitS,
            Detail = new JObject { ["aufgaben"] = new JArray(Punkte), ["zeitbonus"] = RcompWertung.Trail.Zeitbonus(zeitS) },
        };

        public RcompHudInfo Hud() => new RcompHudInfo
        {
            Key = "rcomp.hud.aufgabe", N = Math.Min(Idx + 1, Stationen.Length), Max = Stationen.Length, Name = AktuelleStation?.Id ?? "",
        };

        /// <summary>World position → corridor coordinates (x along, y across).</summary>
        public static V2 Lokal(V2 pos, RcompKurs.TrailStation station)
        {
            var d = pos - station.Pos;
            var entlang = new V2(Math.Cos(station.Winkel), Math.Sin(station.Winkel));
            return new V2(d.Dot(entlang), d.Dot(new V2(-entlang.Y, entlang.X)));
        }

        public static V2 Ausgang(RcompKurs.TrailStation s) => s.Pos + new V2(Math.Cos(s.Winkel), Math.Sin(s.Winkel)) * (RcompKurs.TrailLaengeM * 0.5);
        public static V2 Eingang(RcompKurs.TrailStation s) => s.Pos - new V2(Math.Cos(s.Winkel), Math.Sin(s.Winkel)) * (RcompKurs.TrailLaengeM * 0.5);
        public static V2 Quer(RcompKurs.TrailStation s) => new V2(-Math.Sin(s.Winkel), Math.Cos(s.Winkel));
    }

    /// <summary>
    /// Show (richter_schau.gd): the horse stands in the spotlight, care + style are fixed before, the routine is a
    /// Simon-says of five commands — a tap within ±300 ms of the ideal moment hits.
    /// </summary>
    public sealed class RcompRichterSchau : IRcompRichter
    {
        public const double StartS = 3.0, AbstandS = 4.0, AnsageVorlaufS = 1.5, FensterS = 1.2;

        public sealed class Kommando
        {
            public string Id;
            public double ZeitS;
        }

        public readonly List<Kommando> Kommandos = new List<Kommando>();
        public int Idx, Treffer;
        public readonly List<double> Abweichungen = new List<double>();
        public double PflegeWert = 70.0, StilWert = 40.0;
        private double _zeit;
        private bool _angesagt;

        public void Setup(int seed)
        {
            var rng = new GoobyRng(seed);
            var pool = new List<string>(RcompWertung.Schau.Kommandos);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = (int)(rng.Next() * (i + 1));
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            Kommandos.Clear();
            for (var i = 0; i < RcompWertung.Schau.KuerKommandos; i++) Kommandos.Add(new Kommando { Id = pool[i % pool.Count], ZeitS = StartS + i * AbstandS });
        }

        public void SetzeBasis(double pflege, double stil)
        {
            PflegeWert = Math.Max(0.0, Math.Min(100.0, pflege));
            StilWert = Math.Max(0.0, Math.Min(100.0, stil));
        }

        public bool Fertig => Idx >= Kommandos.Count;
        public Kommando AktuellesKommando => Idx < Kommandos.Count ? Kommandos[Idx] : null;
        public bool Angesagt => _angesagt;
        public double Countdown => Fertig ? 0.0 : Kommandos[Idx].ZeitS - _zeit;

        public List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt)
        {
            var events = new List<RcompEvent>();
            if (Fertig) return events;
            _zeit += dt;
            var kommando = Kommandos[Idx];
            if (!_angesagt && _zeit >= kommando.ZeitS - AnsageVorlaufS)
            {
                _angesagt = true;
                events.Add(new RcompEvent { Typ = "kommando", Id = kommando.Id, Index = Idx });
            }
            if (_zeit > kommando.ZeitS + FensterS)
            {
                Abweichungen.Add((FensterS + 1.0) * 1000.0);
                events.Add(RcompEvent.Of("daneben", Idx));
                Idx++;
                _angesagt = false;
            }
            return events;
        }

        /// <summary>The player's tap, timed against the ideal moment; far too early (before the call) fizzles without penalty.</summary>
        public RcompEvent Tippe()
        {
            if (Fertig) return null;
            var kommando = Kommandos[Idx];
            var abweichungMs = (_zeit - kommando.ZeitS) * 1000.0;
            if (abweichungMs < -AnsageVorlaufS * 1000.0) return RcompEvent.Of("zu_frueh");
            Abweichungen.Add(Math.Abs(abweichungMs));
            var getroffen = RcompWertung.Schau.KuerTreffer(abweichungMs);
            if (getroffen) Treffer++;
            Idx++;
            _angesagt = false;
            return new RcompEvent { Typ = getroffen ? "treffer" : "daneben", Index = Idx - 1, AbweichungMs = abweichungMs, Id = kommando.Id };
        }

        public RcompErgebnis Ergebnis(double zeitS)
        {
            var kuer = RcompWertung.Schau.Kuer(Treffer);
            return new RcompErgebnis
            {
                Wert = RcompWertung.Schau.Gesamt(PflegeWert, StilWert, kuer),
                ZeitS = zeitS,
                Detail = new JObject { ["pflege"] = PflegeWert, ["stil"] = StilWert, ["kuer"] = kuer, ["treffer"] = Treffer },
            };
        }

        public RcompHudInfo Hud() => new RcompHudInfo { Key = "rcomp.hud.kommando", N = Math.Min(Idx + 1, Kommandos.Count), Max = Kommandos.Count };

        /// <summary>Care/style from the horse save: care = (cleanliness + mood) / 2, style counts the equipped gear.</summary>
        public static (double pflege, double stil) BasisAusPferd(JObject pferd, JToken gear)
        {
            var werte = pferd?["werte"] as JObject;
            var sauberkeit = Num(werte?["sauberkeit"], 75.0);
            var laune = werte == null || werte.Count == 0 ? 70.0 : Laune(werte, Num(pferd["bindung"], 50.0));
            var raritaeten = new List<string>();
            if (gear is JArray arr) foreach (var _ in arr) raritaeten.Add("selten");
            else if (gear is JObject o)
                foreach (var p in o.Properties())
                    if (p.Value.Type != JTokenType.Null && (string)p.Value != "")
                        raritaeten.Add(RanchWirtschaft.IstSonder(p.Name, (string)p.Value) ? "episch" : "selten");
            var teile = raritaeten.Count;
            return (RcompWertung.Schau.Pflege(sauberkeit, laune), RcompWertung.Schau.Stil(raritaeten, teile >= 3, false));
        }

        /// <summary>RanchHorseCare.laune: 0.35·lowest + 0.65·mean of hunger/thirst/cleanliness, ±5 around bond 50, thirsty caps at 39.</summary>
        public static double Laune(JObject werte, double bindung)
        {
            var lowest = double.PositiveInfinity;
            var sum = 0.0;
            foreach (var k in new[] { "hunger", "durst", "sauberkeit" })
            {
                var v = Math.Max(0.0, Math.Min(100.0, Num(werte?[k], 0.0)));
                lowest = Math.Min(lowest, v);
                sum += v;
            }
            var m = 0.35 * lowest + 0.65 * (sum / 3.0) + (Math.Max(0.0, Math.Min(100.0, bindung)) - 50.0) / 50.0 * 5.0;
            if (Num(werte?["durst"], 0.0) <= 15.0) m = Math.Min(m, 39.0);
            return Math.Min(100.0, Math.Max(0.0, m));
        }

        private static double Num(JToken t, double fallback) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<double>() : fallback;
    }

    /// <summary>Barrel race (richter_tonnen.gd): circle each barrel in turn (angle sweep ≥ 300°), bumping tips it (+5 s), then home.</summary>
    public sealed class RcompRichterTonnen : IRcompRichter
    {
        public const double SweepZiel = 5.2, SweepRadius = 8.0, RammAbstand = 1.15, ZielHalbbreite = 7.0;
        public V2[] Tonnen = (V2[])RcompKurs.TonnenPositionen.Clone();
        public int Idx, Umgeworfen;
        public double Ideal = 24.0;
        public bool Heim;
        private double _sweep, _winkelVorher;
        private bool _winkelInit;
        private readonly HashSet<int> _gerammt = new HashSet<int>();

        public void Setup(string klasse) => Ideal = RcompWertung.Tonnen.Idealzeit(klasse);
        public void SetzeIdeal(double zielS) => Ideal = zielS;
        public bool Fertig => Heim;
        public double Sweep => _sweep;
        public bool IstUmgeworfen(int i) => _gerammt.Contains(i);

        public List<RcompEvent> Tick(V2 vorher, V2 jetzt, string gait, bool inLuft, double dt)
        {
            var events = new List<RcompEvent>();
            for (var i = 0; i < Tonnen.Length; i++)
            {
                if (_gerammt.Contains(i)) continue;
                if ((jetzt - Tonnen[i]).Length < RammAbstand)
                {
                    _gerammt.Add(i);
                    Umgeworfen++;
                    events.Add(RcompEvent.Of("tonne_um", i));
                }
            }
            if (Idx < Tonnen.Length) SweepUmTonne(jetzt, events);
            else if (RcompKurs.TorDurchritten(vorher, jetzt, RcompKurs.TonnenStart, new V2(1f, 0f), ZielHalbbreite))
            {
                Heim = true;
                events.Add(RcompEvent.Of("ziel"));
            }
            return events;
        }

        public RcompErgebnis Ergebnis(double zeitS) => new RcompErgebnis
        {
            Wert = RcompWertung.Tonnen.WertungS(zeitS, Umgeworfen),
            ZeitS = zeitS,
            Detail = new JObject { ["umgeworfen"] = Umgeworfen, ["ideal"] = Ideal },
        };

        public RcompHudInfo Hud() => new RcompHudInfo { Key = "rcomp.hud.tonnen", N = Math.Min(Idx, 3), Max = 3 };

        private void SweepUmTonne(V2 jetzt, List<RcompEvent> events)
        {
            var d = jetzt - Tonnen[Idx];
            if (d.Length > SweepRadius)
            {
                _winkelInit = false;
                return;
            }
            var winkel = Math.Atan2(d.Y, d.X);
            if (!_winkelInit)
            {
                _winkelInit = true;
                _winkelVorher = winkel;
                return;
            }
            var delta = RcompKurs.Fposmod(winkel - _winkelVorher + Math.PI, 2.0 * Math.PI) - Math.PI;
            _winkelVorher = winkel;
            _sweep += delta;
            if (Math.Abs(_sweep) >= SweepZiel)
            {
                events.Add(new RcompEvent { Typ = "tonne_ok", Index = Idx, Nummer = Idx + 1 });
                Idx++;
                _sweep = 0.0;
                _winkelInit = false;
            }
        }
    }
}
