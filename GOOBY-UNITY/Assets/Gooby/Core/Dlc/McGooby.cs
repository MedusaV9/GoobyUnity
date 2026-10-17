using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Dlc
{
    /// <summary>
    /// McGooby's menu pack (port of mcgooby_katalog.gd): recipes with their stations (grill, assembly, fryer, shake bar)
    /// and steps, the timing per station (a recipe's fenster_mult narrows the golden window), the balance block (every
    /// number of a shift) and the five-star rank ladder. Read from content/dlc/data/mcgooby_menu.
    /// </summary>
    public static class McGoobyKatalog
    {
        public const string PackPath = "content/dlc/data/mcgooby_menu";
        public static readonly string[] StationIds = { "grill", "belegen", "fritteuse", "shake" };
        public const int DefaultPreis = 3000;
        public const int DefaultFreischaltLevel = 14;

        public static JObject Daten() => ContentSource.LoadObject(PackPath);

        public static List<JObject> Rezepte() => (Daten()["rezepte"] as JArray ?? new JArray()).OfType<JObject>().ToList();

        public static JObject Rezept(string id) => Rezepte().FirstOrDefault(r => J.Str(r["id"]) == id);

        public static List<JObject> RezepteFuer(string station) =>
            Rezepte().Where(r => (r["stationen"] as JArray)?.Any(s => J.Str(s) == station) == true).ToList();

        public static JObject Balance() => Daten()["balance"] as JObject ?? new JObject();

        public static int Preis() => Math.Max(0, J.Int(Balance()["preis"], DefaultPreis));

        public static int FreischaltLevel() => Math.Max(1, J.Int(Balance()["freischalt_level"], DefaultFreischaltLevel));

        public static List<JObject> Raenge()
        {
            var raw = (Daten()["raenge"] as JArray)?.OfType<JObject>().ToList();
            if (raw != null && raw.Count > 0) return raw;
            return new List<JObject>
            {
                new JObject { ["stern"] = 1, ["schichten"] = 1, ["bestwert"] = 0, ["perfekt"] = 0 },
                new JObject { ["stern"] = 2, ["schichten"] = 5, ["bestwert"] = 80, ["perfekt"] = 0 },
                new JObject { ["stern"] = 3, ["schichten"] = 12, ["bestwert"] = 130, ["perfekt"] = 0 },
                new JObject { ["stern"] = 4, ["schichten"] = 25, ["bestwert"] = 170, ["perfekt"] = 1 },
                new JObject { ["stern"] = 5, ["schichten"] = 40, ["bestwert"] = 200, ["perfekt"] = 3 },
            };
        }

        public static JObject Station(string id) => (Daten()["stationen"] as JArray)?.OfType<JObject>().FirstOrDefault(s => J.Str(s["id"]) == id);

        public sealed class Timing
        {
            public double GarSec, FensterSec, NachlaufSec;
            public double Ende => GarSec + FensterSec;
        }

        /// <summary>A station's timing (gar / golden window / grace); the recipe's fenster_mult (0.25..2) scales the window.</summary>
        public static Timing TimingFuer(string station, JObject rezept = null)
        {
            var zeile = (Daten()["timing"] as JObject)?[station] as JObject ?? new JObject();
            var mult = J.Clamp(J.NumOr(rezept?["fenster_mult"], 1.0), 0.25, 2.0);
            return new Timing
            {
                GarSec = Math.Max(0.0, J.NumOr(zeile["gar_sec"], 4.0)),
                FensterSec = Math.Max(0.1, J.NumOr(zeile["fenster_sec"], 1.4) * mult),
                NachlaufSec = Math.Max(0.1, J.NumOr(zeile["nachlauf_sec"], 2.0)),
            };
        }

        /// <summary>A localized field ("name" → name_de / name_en), falling back to German.</summary>
        public static string TextVon(JObject eintrag, string feld, string locale)
        {
            var wert = J.Str(eintrag?[feld + "_" + locale]);
            return wert != "" ? wert : J.Str(eintrag?[feld + "_de"]);
        }
    }

    /// <summary>
    /// One McGooby shift (ports of schicht_logic.gd, station_belegen/fritteuse/shake_logic.gd and abrechnung_logic.gd):
    /// a seeded order stream (the first customer always wants a burger, the second something from the new counter),
    /// each order worked through its stations — grill taps in the golden window (too late = "Röstaroma", half points,
    /// never lost), stacking the ticket's ingredients in order (a wrong one costs 2), pulling the fryer basket while
    /// golden, stopping the shake at its crown — then the till: points, tips that grow with flawless orders in a row
    /// (×1.1 … ×2.0) and coins from the points (floor(p/4) between 6 and 30). The bot certifies the goldens.
    /// </summary>
    public static class McGoobySchicht
    {
        public const string Roh = "roh", Goldbraun = "goldbraun", Kohle = "kohle";
        public const string Perfekt = "perfekt", Roestaroma = "roestaroma";
        public const string Blass = "blass", Goldgelb = "goldgelb", Dunkel = "dunkel";
        public const string Fluessig = "fluessig", Krone = "krone", Schaum = "schaum";
        public const string Richtig = "richtig", Falsch = "falsch";

        public sealed class Bestellung
        {
            public int Nr;
            public string RezeptId;
            public int Patties;
        }

        public sealed class Wertung
        {
            public string Art;
            public int Punkte;
        }

        private static int Bal(JObject bal, string key, int fallback) => J.Int(bal?[key], fallback);

        /// <summary>The seeded order stream: order 1 a burger, order 2 from the counter (fryer/shake), then free.</summary>
        public static List<Bestellung> BestellFolge(long seed, IList<JObject> menu, JObject bal)
        {
            var outp = new List<Bestellung>();
            if (menu == null || menu.Count == 0) return outp;
            var rng = new GoobyRng(seed);
            var lo = Math.Max(1, Bal(bal, "bestellungen_min", 2));
            var hi = Math.Max(lo, Bal(bal, "bestellungen_max", 4));
            var anzahl = lo + (int)Math.Floor(rng.Next() * (hi - lo + 1));
            var burger = menu.Where(r => HasStation(r, "grill")).ToList();
            var tresen = menu.Where(r => !HasStation(r, "grill")).ToList();
            for (var i = 0; i < anzahl; i++)
            {
                var topf = (IList<JObject>)menu;
                if (i == 0 && burger.Count > 0) topf = burger;
                else if (i == 1 && tresen.Count > 0) topf = tresen;
                var idx = Math.Min(topf.Count - 1, (int)Math.Floor(rng.Next() * topf.Count));
                var rezept = topf[idx];
                outp.Add(new Bestellung { Nr = i + 1, RezeptId = J.Str(rezept["id"]), Patties = GrillSchritte(rezept) });
            }
            return outp;
        }

        private static bool HasStation(JObject rezept, string station) => (rezept?["stationen"] as JArray)?.Any(s => J.Str(s) == station) == true;

        /// <summary>The recipe's stations in working order (its "stationen" field).</summary>
        public static List<string> PhasenVon(JObject rezept) => (rezept?["stationen"] as JArray ?? new JArray()).Select(s => J.Str(s)).ToList();

        /// <summary>Grill actions of a recipe (sum of the grill steps' anzahl, at least 1 with a grill; 0 without).</summary>
        public static int GrillSchritte(JObject rezept)
        {
            if (!HasStation(rezept, "grill")) return 0;
            var summe = 0;
            foreach (var s in (rezept["schritte"] as JArray ?? new JArray()).OfType<JObject>())
                if (J.Str(s["station"]) == "grill") summe += Math.Max(1, J.Int(s["anzahl"], 1));
            return Math.Max(1, summe);
        }

        /// <summary>The fryer/shake actions of a recipe at a station (one entry per anzahl).</summary>
        public static List<string> AktionenFuer(JObject rezept, string station)
        {
            var outp = new List<string>();
            foreach (var s in (rezept?["schritte"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (J.Str(s["station"]) != station) continue;
                for (var i = 0; i < Math.Max(1, J.Int(s["anzahl"], 1)); i++) outp.Add(J.Str(s["aktion"]));
            }
            return outp;
        }

        // ── timing stations (grill / fryer / shake) ──

        /// <summary>The state of a timing round at t: before the window, golden, after.</summary>
        public static string Zustand(string station, double t, McGoobyKatalog.Timing timing)
        {
            var golden = t >= timing.GarSec;
            var over = t >= timing.GarSec + timing.FensterSec;
            switch (station)
            {
                case "fritteuse": return over ? Dunkel : golden ? Goldgelb : Blass;
                case "shake": return over ? Schaum : golden ? Krone : Fluessig;
                default: return over ? Kohle : golden ? Goldbraun : Roh;
            }
        }

        public static double Fortschritt(double t, McGoobyKatalog.Timing timing) => timing.Ende <= 0 ? 1.0 : J.Clamp(t / timing.Ende, 0.0, 1.0);

        /// <summary>A tap / release / stop at t: before the window nothing happens (0, the round goes on), golden = full, after = half.</summary>
        public static Wertung Bewerte(string station, double t, McGoobyKatalog.Timing timing, JObject bal)
        {
            var z = Zustand(station, t, timing);
            if (z == Roh || z == Blass || z == Fluessig) return new Wertung { Art = z, Punkte = 0 };
            if (z == Goldbraun || z == Goldgelb || z == Krone) return new Wertung { Art = Perfekt, Punkte = PunktePerfekt(station, bal) };
            return Vergessen(station, bal);
        }

        /// <summary>A round left past its grace counts like a late one (half points — never lost).</summary>
        public static Wertung Vergessen(string station, JObject bal)
        {
            switch (station)
            {
                case "fritteuse": return new Wertung { Art = Dunkel, Punkte = Math.Max(0, Bal(bal, "fritteuse_punkte_dunkel", 5)) };
                case "shake": return new Wertung { Art = Schaum, Punkte = Math.Max(0, Bal(bal, "shake_punkte_schaum", 5)) };
                default: return new Wertung { Art = Roestaroma, Punkte = Bal(bal, "punkte_roestaroma", 5) };
            }
        }

        public static int PunktePerfekt(string station, JObject bal)
        {
            switch (station)
            {
                case "fritteuse": return Math.Max(0, Bal(bal, "fritteuse_punkte_perfekt", 10));
                case "shake": return Math.Max(0, Bal(bal, "shake_punkte_perfekt", 10));
                default: return Bal(bal, "punkte_perfekt", 10);
            }
        }

        // ── assembly station ──

        /// <summary>The assembly ticket: the ingredients of the first belegen step, bottom to top.</summary>
        public static List<string> TicketVon(JObject rezept)
        {
            foreach (var s in (rezept?["schritte"] as JArray ?? new JArray()).OfType<JObject>())
                if (J.Str(s["station"]) == "belegen") return (s["zutaten"] as JArray ?? new JArray()).Select(z => J.Str(z)).ToList();
            return new List<string>();
        }

        /// <summary>The ingredient bar: each ingredient of the ticket once, in first-use order.</summary>
        public static List<string> Leiste(IList<string> ticket) => ticket.Distinct().ToList();

        public static string NaechsteZutat(IList<string> ticket, int platziert) => platziert >= 0 && platziert < ticket.Count ? ticket[platziert] : "";

        /// <summary>A swipe of <paramref name="zutat"/>: the next one = +5 and placed, any other = −2 and nothing placed.</summary>
        public static (Wertung wertung, int platziert) BewerteWisch(IList<string> ticket, int platziert, string zutat, JObject bal)
        {
            if (!string.IsNullOrEmpty(zutat) && zutat == NaechsteZutat(ticket, platziert))
                return (new Wertung { Art = Richtig, Punkte = Math.Max(0, Bal(bal, "belegen_punkte_richtig", 5)) }, platziert + 1);
            return (new Wertung { Art = Falsch, Punkte = -Math.Max(0, Bal(bal, "belegen_malus_falsch", 2)) }, platziert);
        }

        // ── the till ──

        public static double ComboMult(int fehlerfreieInFolge, JObject bal) =>
            Math.Min(J.NumOr(bal?["combo_max"], 2.0), J.NumOr(bal?["combo_start"], 1.0) + J.NumOr(bal?["combo_schritt"], 0.1) * Math.Max(0, fehlerfreieInFolge));

        public static int MuenzenFuer(int punkte, JObject bal)
        {
            var divisor = Math.Max(1, Bal(bal, "coin_divisor", 4));
            var lo = Math.Max(0, Bal(bal, "coin_min", 6));
            var hi = Math.Max(lo, Bal(bal, "coin_max", 30));
            return J.ClampI((int)Math.Floor(Math.Max(0, punkte) / (double)divisor), lo, hi);
        }

        public sealed class Kasse
        {
            public int Punkte, MuenzenBasis, Trinkgeld, Muenzen, Fehlerfreie;
            public double ComboMaxMult;
        }

        /// <summary>The settlement of a shift's orders (points, combo tips, coins).</summary>
        public static Kasse Abrechnung(IEnumerable<(int punkte, bool fehlerfrei)> ergebnisse, JObject bal)
        {
            var k = new Kasse { ComboMaxMult = ComboMult(0, bal) };
            var streak = 0;
            var basis = Math.Max(0, Bal(bal, "trinkgeld_basis", 2));
            foreach (var (punkte, fehlerfrei) in ergebnisse)
            {
                k.Punkte += Math.Max(0, punkte);
                if (!fehlerfrei) { streak = 0; continue; }
                streak++;
                k.Fehlerfreie++;
                var mult = ComboMult(streak, bal);
                k.ComboMaxMult = Math.Max(k.ComboMaxMult, mult);
                k.Trinkgeld += (int)Math.Round(basis * mult, MidpointRounding.AwayFromZero);
            }
            k.MuenzenBasis = MuenzenFuer(k.Punkte, bal);
            k.Muenzen = k.MuenzenBasis + k.Trinkgeld;
            return k;
        }

        // ── the bot ──

        public sealed class Autoplay
        {
            public int Bestellungen, Perfekt, Roestaroma, Fehlgriffe, Dunkel, Schaum, Punkte, Trinkgeld, Muenzen;
        }

        /// <summary>Deterministic bot certification: the same seed plays the same orders and the same actions (exact goldens).</summary>
        public static Autoplay SimulateAutoplay(long seed, IList<JObject> menu, JObject bal)
        {
            var folge = BestellFolge(seed, menu, bal);
            var rng = new GoobyRng(seed + 1);
            var skill = J.Clamp(J.NumOr(bal?["bot_skill"], 0.9), 0.0, 1.0);
            var a = new Autoplay { Bestellungen = folge.Count };
            var ergebnisse = new List<(int, bool)>();
            foreach (var b in folge)
            {
                var rezept = menu.FirstOrDefault(r => J.Str(r["id"]) == b.RezeptId);
                var punkte = 0;
                var fehlerfrei = true;
                foreach (var phase in PhasenVon(rezept))
                {
                    switch (phase)
                    {
                        case "grill":
                            for (var i = 0; i < b.Patties; i++)
                            {
                                if (rng.Next() < skill) { punkte += Bal(bal, "punkte_perfekt", 10); a.Perfekt++; }
                                else { punkte += Bal(bal, "punkte_roestaroma", 5); a.Roestaroma++; fehlerfrei = false; }
                            }
                            break;
                        case "belegen":
                        {
                            var ticket = TicketVon(rezept);
                            if (ticket.Count == 0) break;
                            var p = 0;
                            var fehl = 0;
                            foreach (var _ in ticket)
                            {
                                if (rng.Next() >= skill) { fehl++; p = Math.Max(0, p - Math.Max(0, Bal(bal, "belegen_malus_falsch", 2))); }
                                p += Math.Max(0, Bal(bal, "belegen_punkte_richtig", 5));
                            }
                            punkte += p;
                            a.Fehlgriffe += fehl;
                            fehlerfrei = fehlerfrei && fehl == 0;
                            break;
                        }
                        case "fritteuse":
                        case "shake":
                        {
                            var aktionen = AktionenFuer(rezept, phase);
                            var schlecht = 0;
                            foreach (var _ in aktionen)
                            {
                                if (rng.Next() < skill) punkte += PunktePerfekt(phase, bal);
                                else { punkte += Vergessen(phase, bal).Punkte; schlecht++; }
                            }
                            a.Perfekt += aktionen.Count - schlecht;
                            if (phase == "fritteuse") a.Dunkel += schlecht; else a.Schaum += schlecht;
                            fehlerfrei = fehlerfrei && schlecht == 0;
                            break;
                        }
                    }
                }
                punkte += Bal(bal, "bestellung_fertig_bonus", 15);
                ergebnisse.Add((punkte, fehlerfrei));
            }
            var kasse = Abrechnung(ergebnisse, bal);
            a.Punkte = kasse.Punkte;
            a.Trinkgeld = kasse.Trinkgeld;
            a.Muenzen = kasse.Muenzen;
            return a;
        }
    }

    /// <summary>
    /// The McGooby save slice and progress (ports of mcgooby_state.gd, mcgooby_fortschritt.gd, mcgooby_kauf.gd): one free
    /// demo shift a day until the restaurant is bought (3,000 coins, from level 14), shifts played / best / flawless, the
    /// five-star rank ladder (a new star is celebrated once).
    /// </summary>
    public static class McGoobyState
    {
        public const string SliceId = "mcgooby";
        public const int MaxSterne = 5;
        public const string KaufOk = "ok", KaufGesperrt = "level_zu_niedrig", KaufSchon = "schon_gekauft", KaufPleite = "not_enough_coins";

        /// <summary>The slice with every field in place, healed in place (assigning an attached JObject would store a copy).</summary>
        public static JObject Slice(JObject state)
        {
            var s = state?[SliceId] as JObject;
            if (s == null)
            {
                s = new JObject();
                if (state != null) state[SliceId] = s;
            }
            s["v"] = Math.Max(1, J.Int(s["v"], 1));
            foreach (var key in new[] { "introGesehen", "gekauft", "angebotGesehen", "angebotVerschoben" })
                s[key] = J.IsTrue(s[key]);
            s["gekauftAm"] = Math.Max(0L, J.Long(s["gekauftAm"]));
            s["demoTag"] = J.Str(s["demoTag"]);
            var sch = s["schichten"] as JObject;
            if (sch == null)
            {
                sch = new JObject();
                s["schichten"] = sch;
            }
            sch["gespielt"] = Math.Max(0, J.Int(sch["gespielt"]));
            sch["bestwert"] = Math.Max(0, J.Int(sch["bestwert"]));
            sch["perfekt"] = Math.Max(0, J.Int(sch["perfekt"]));
            sch["rangGefeiert"] = J.ClampI(J.Int(sch["rangGefeiert"]), 0, MaxSterne);
            return s;
        }

        private static JObject Read(JObject state) => Slice((JObject)(state?.DeepClone() ?? new JObject()));

        public static bool IstGekauft(JObject state) => J.IsTrue(J.Path(state, SliceId, "gekauft"));

        public static bool IstFreigeschaltet(JObject state) => J.Int(J.Path(state, "progression", "level"), 1) >= McGoobyKatalog.FreischaltLevel();

        /// <summary>Without the restaurant one demo shift a day; bought, any number.</summary>
        public static bool SchichtErlaubt(JObject state, string tag) => IstGekauft(state) || J.Str(J.Path(state, SliceId, "demoTag")) != tag;

        public static void DemoVerbuchen(JObject state, string tag)
        {
            if (IstGekauft(state)) return;
            Slice(state)["demoTag"] = tag;
        }

        public static void SchichtVerbuchen(JObject state, int punkte, bool fehlerfrei)
        {
            var sch = (JObject)Slice(state)["schichten"];
            sch["gespielt"] = J.Int(sch["gespielt"]) + 1;
            sch["bestwert"] = Math.Max(J.Int(sch["bestwert"]), Math.Max(0, punkte));
            if (fehlerfrei) sch["perfekt"] = J.Int(sch["perfekt"]) + 1;
        }

        public static (int gespielt, int bestwert, int perfekt) Stand(JObject state)
        {
            var sch = (JObject)Read(state)["schichten"];
            return (J.Int(sch["gespielt"]), J.Int(sch["bestwert"]), J.Int(sch["perfekt"]));
        }

        private static bool Erfuellt((int gespielt, int bestwert, int perfekt) ist, JObject stufe) =>
            ist.gespielt >= J.Int(stufe["schichten"]) && ist.bestwert >= J.Int(stufe["bestwert"]) && ist.perfekt >= J.Int(stufe["perfekt"]);

        public static int Sterne(JObject state)
        {
            var ist = Stand(state);
            var erreicht = 0;
            foreach (var stufe in McGoobyKatalog.Raenge())
                if (Erfuellt(ist, stufe)) erreicht = Math.Max(erreicht, J.Int(stufe["stern"]));
            return J.ClampI(erreicht, 0, MaxSterne);
        }

        public sealed class NaechsterRang
        {
            public int Stern, FehltSchichten, FehltBestwert, FehltPerfekt, BestwertZiel;
        }

        /// <summary>What the next star still needs (null at five stars).</summary>
        public static NaechsterRang Naechster(JObject state)
        {
            var ist = Stand(state);
            var aktuell = Sterne(state);
            var stufe = McGoobyKatalog.Raenge().FirstOrDefault(s => J.Int(s["stern"]) == aktuell + 1);
            if (stufe == null) return null;
            return new NaechsterRang
            {
                Stern = aktuell + 1,
                FehltSchichten = Math.Max(0, J.Int(stufe["schichten"]) - ist.gespielt),
                FehltBestwert = Math.Max(0, J.Int(stufe["bestwert"]) - ist.bestwert),
                FehltPerfekt = Math.Max(0, J.Int(stufe["perfekt"]) - ist.perfekt),
                BestwertZiel = J.Int(stufe["bestwert"]),
            };
        }

        /// <summary>After booking a shift: a new star above the last celebrated one is celebrated (once).</summary>
        public static (bool feiern, int stern) AufstiegPruefen(JObject state, int vorher)
        {
            var neu = Sterne(state);
            var sch = (JObject)Slice(state)["schichten"];
            var gefeiert = J.Int(sch["rangGefeiert"]);
            var feiern = neu > vorher && neu > gefeiert;
            if (neu > gefeiert) sch["rangGefeiert"] = neu;
            return (feiern, neu);
        }

        public static string Check(JObject state)
        {
            if (state == null) return KaufGesperrt;
            if (IstGekauft(state)) return KaufSchon;
            if (!IstFreigeschaltet(state)) return KaufGesperrt;
            if (J.Int(J.Path(state, "economy", "coins")) < McGoobyKatalog.Preis()) return KaufPleite;
            return KaufOk;
        }

        /// <summary>Buys the restaurant: coins out and the key in, or nothing.</summary>
        public static string Kaufe(JObject state, long nowMs)
        {
            var grund = Check(state);
            if (grund != KaufOk) return grund;
            if (!Economy.Spend(J.EnsureDict(state, "economy"), McGoobyKatalog.Preis(), "mcgooby")) return KaufPleite;
            var s = Slice(state);
            s["gekauft"] = true;
            s["gekauftAm"] = nowMs;
            s["angebotGesehen"] = true;
            s["angebotVerschoben"] = false;
            return KaufOk;
        }
    }
}
