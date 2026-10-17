using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Random;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Competition catalogue (comp_katalog.gd, RW-5) — the one truth about disciplines, classes, gold/XP numbers and set
    /// times. Balance comes from <c>minigames/ranch/comp_balance.json</c>; the code only knows fallbacks. Class ladder
    /// wood → bronze → silver → gold → star class, no relegation.
    /// </summary>
    public static class RanchCompKatalog
    {
        public const string BalancePath = "minigames/ranch/comp_balance";
        public static readonly string[] Klassen = { "holz", "bronze", "silber", "gold", "sternenklasse" };
        public static readonly string[] Disziplinen = { "springen", "dressur", "gelaende", "rennen", "trail", "schau", "tonnen" };

        public static readonly IReadOnlyDictionary<string, int> KlasseAbLevel = new Dictionary<string, int>
        {
            ["holz"] = 1, ["bronze"] = 5, ["silber"] = 10, ["gold"] = 18, ["sternenklasse"] = 25,
        };

        public static readonly IReadOnlyDictionary<string, double> GoldFaktor = new Dictionary<string, double>
        {
            ["holz"] = 1.0, ["bronze"] = 1.5, ["silber"] = 2.0, ["gold"] = 3.0, ["sternenklasse"] = 4.0,
        };

        public static readonly IReadOnlyDictionary<string, int> XpJeTeilnahme = new Dictionary<string, int>
        {
            ["holz"] = 60, ["bronze"] = 90, ["silber"] = 130, ["gold"] = 180, ["sternenklasse"] = 240,
        };

        public const int BasisGold = 40, SchleifeAbPlatz = 3;
        public static readonly double[] PlatzAnteil = { 1.0, 0.6, 0.35 };

        public static JObject LoadBalance() => ContentSource.Installed && ContentSource.TryLoadObject(BalancePath) is JObject o ? (JObject)o.DeepClone() : new JObject();

        public static JObject Klasse(JObject balance, string id)
        {
            if (balance?["klassen"] is JArray arr)
                foreach (var e in arr)
                    if (e is JObject o && (string)o["id"] == id)
                        return o;
            return new JObject
            {
                ["id"] = id,
                ["ab_level"] = KlasseAbLevel.TryGetValue(id ?? "", out var ab) ? ab : 1,
                ["gold_faktor"] = GoldFaktor.TryGetValue(id ?? "", out var g) ? g : 1.0,
                ["xp"] = XpJeTeilnahme.TryGetValue(id ?? "", out var xp) ? xp : 60,
                ["aufstieg_punkte"] = 0,
            };
        }

        public static int KlasseIndex(string id) => Math.Max(0, Array.IndexOf(Klassen, id));

        public static string KlasseDanach(string id)
        {
            var i = Array.IndexOf(Klassen, id);
            return i < 0 || i >= Klassen.Length - 1 ? "" : Klassen[i + 1];
        }

        public static bool KlasseErlaubt(JObject balance, string id, int pferdeLevel) => pferdeLevel >= (int)Num(Klasse(balance, id)["ab_level"], 1.0);

        public static JObject Disziplin(JObject balance, string id)
        {
            if (balance?["disziplinen"] is JArray arr)
                foreach (var e in arr)
                    if (e is JObject o && (string)o["id"] == id)
                        return o;
            return new JObject { ["id"] = id, ["wertung"] = "punkte", ["stats"] = new JArray() };
        }

        /// <summary>True when the smaller number wins (time and placing disciplines).</summary>
        public static bool ZeitGewinnt(JObject balance, string disziplin)
        {
            var art = (string)Disziplin(balance, disziplin)["wertung"] ?? "punkte";
            return art == "zeit" || art == "platz";
        }

        public static double RichtzeitS(JObject balance, string disziplin, string klasse) =>
            balance?["richtzeiten"]?[disziplin] is JObject zeiten ? Num(zeiten[klasse], 0.0) : 0.0;

        /// <summary>Gold for a placing: base 40 × share (100/60/35 %, from 4th 10 %) × class factor.</summary>
        public static int GoldFuerPlatz(JObject balance, string klasse, int platz)
        {
            var basis = Num(balance?["basis_gold"], BasisGold);
            var anteil = 0.1;
            if (balance?["platz_anteil"] is JArray arr)
            {
                if (platz >= 1 && platz <= arr.Count) anteil = Num(arr[platz - 1], 0.1);
            }
            else if (platz >= 1 && platz <= PlatzAnteil.Length) anteil = PlatzAnteil[platz - 1];
            var faktor = Num(Klasse(balance, klasse)["gold_faktor"], 1.0);
            return (int)Math.Round(basis * anteil * faktor, MidpointRounding.AwayFromZero);
        }

        public static int XpFuerTeilnahme(JObject balance, string klasse) => (int)Num(Klasse(balance, klasse)["xp"], 60.0);

        public static bool SchleifeVerdient(JObject balance, int platz) => platz >= 1 && platz <= (int)Num(balance?["schleife_ab_platz"], SchleifeAbPlatz);

        public static (double von, double bis) BotBand(JObject balance, string klasse)
        {
            if (balance?["bot_koennen"]?[klasse] is JArray band && band.Count == 2) return (Num(band[0], 0.3), Num(band[1], 0.5));
            return (0.3, 0.5);
        }

        public static double Num(JToken t, double fallback) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? t.Value<double>() : fallback;
    }

    /// <summary>
    /// Save slice of the competitions (comp_state.gd) — additive under <c>ranch.comp</c>, no version bump; it heals its data
    /// on READ and keeps foreign keys: class (only rises), league points per class, ribbons, trophies, counters, the best
    /// run per discipline as a ghost, and the arcade progress of ranchTonnen/ranchZeit.
    /// </summary>
    public static class RanchCompState
    {
        public static readonly string[] ArcadeSpiele = { "tonnen", "zeit" };
        public const int ArcadeLevel = 10;

        public static JObject DefaultComp()
        {
            var punkte = new JObject();
            foreach (var k in RanchCompKatalog.Klassen) punkte[k] = 0;
            return new JObject
            {
                ["v"] = 1, ["klasse"] = "holz", ["punkte"] = punkte, ["schleifen"] = new JObject(), ["trophaeen"] = new JArray(),
                ["teilnahmen"] = 0, ["siege"] = 0, ["geister"] = new JObject(), ["arcade"] = DefaultArcade(),
            };
        }

        /// <summary>The healed competition data of a save root (null → defaults).</summary>
        public static JObject Lese(JObject state) => state == null ? DefaultComp() : Normalize(state["ranch"]?["comp"]);

        public static void Schreibe(JObject state, JObject comp)
        {
            if (state == null) return;
            var ranch = state["ranch"] as JObject;
            if (ranch == null) state["ranch"] = ranch = new JObject();
            ranch["comp"] = comp;
        }

        public static JObject Normalize(JToken raw)
        {
            var comp = raw is JObject o ? (JObject)o.DeepClone() : DefaultComp();
            comp["v"] = Math.Max(1, (int)RanchCompKatalog.Num(comp["v"], 1.0));
            if (!RanchCompKatalog.Klassen.Contains(comp["klasse"]?.Type == JTokenType.String ? (string)comp["klasse"] : "")) comp["klasse"] = "holz";
            var punkteRoh = comp["punkte"] as JObject ?? new JObject();
            var punkte = new JObject();
            foreach (var k in RanchCompKatalog.Klassen) punkte[k] = Math.Max(0, (int)RanchCompKatalog.Num(punkteRoh[k], 0.0));
            comp["punkte"] = punkte;
            var schleifen = new JObject();
            if (comp["schleifen"] is JObject schleifenRoh)
                foreach (var p in schleifenRoh.Properties())
                {
                    var platz = (int)RanchCompKatalog.Num(p.Value, 0.0);
                    if (platz >= 1 && platz <= 3) schleifen[p.Name] = platz;
                }
            comp["schleifen"] = schleifen;
            var sauber = new JArray();
            if (comp["trophaeen"] is JArray trophaeen)
                foreach (var t in trophaeen)
                    if (t.Type == JTokenType.String && !sauber.Any(x => (string)x == (string)t))
                        sauber.Add((string)t);
            comp["trophaeen"] = sauber;
            comp["teilnahmen"] = Math.Max(0, (int)RanchCompKatalog.Num(comp["teilnahmen"], 0.0));
            comp["siege"] = Math.Max(0, (int)RanchCompKatalog.Num(comp["siege"], 0.0));
            comp["geister"] = NormalizeGeister(comp["geister"]);
            comp["arcade"] = NormalizeArcade(comp["arcade"]);
            return comp;
        }

        // ── ghost runs ──

        public static JObject Geist(JObject state, string disziplin) => Lese(state)["geister"]?[disziplin] as JObject ?? new JObject();

        /// <summary>Stores the run only when it is better (time: smaller, points: larger). True = new best run saved.</summary>
        public static bool GeistSpeichern(JObject state, string disziplin, string b64, double wert, double zeitS, string datum, string pferd, bool zeitGewinnt)
        {
            if (state == null || string.IsNullOrEmpty(b64)) return false;
            var alt = Geist(state, disziplin);
            if (alt.Count > 0)
            {
                var altWert = RanchCompKatalog.Num(alt["wert"], 0.0);
                var besser = zeitGewinnt ? wert < altWert : wert > altWert;
                if (!besser) return false;
            }
            var comp = Lese(state);
            ((JObject)comp["geister"])[disziplin] = new JObject { ["b64"] = b64, ["wert"] = wert, ["zeit_s"] = zeitS, ["datum"] = datum ?? "", ["pferd"] = pferd ?? "" };
            Schreibe(state, comp);
            return true;
        }

        // ── arcade progress (ranchTonnen / ranchZeit) ──

        public static string LevelKey(int levelId) => "k" + levelId;

        public static int ArcadeStars(JObject state, string spiel, int levelId) => (int)RanchCompKatalog.Num(ArcadeFeld(state, spiel, "stars")[LevelKey(levelId)], 0.0);

        public static int ArcadeBest(JObject state, string spiel, int levelId) => (int)RanchCompKatalog.Num(ArcadeFeld(state, spiel, "best")[LevelKey(levelId)], 0.0);

        public static bool ArcadeCleared(JObject state, string spiel, int levelId) =>
            ArcadeFeld(state, spiel, "cleared")[LevelKey(levelId)] is JValue v && v.Type == JTokenType.Boolean && (bool)v;

        public static int ArcadeMaxUnlocked(JObject state, string spiel)
        {
            for (var id = 1; id <= ArcadeLevel; id++) if (!ArcadeCleared(state, spiel, id)) return id;
            return ArcadeLevel;
        }

        public static int ArcadeTotalStars(JObject state, string spiel)
        {
            var total = 0;
            for (var id = 1; id <= ArcadeLevel; id++) total += ArcadeStars(state, spiel, id);
            return total;
        }

        /// <summary>Books a win (stars/best only improve; bestKleiner = time games). → (first clear, new best).</summary>
        public static (bool firstClear, bool newBest) ArcadeWin(JObject state, string spiel, int levelId, int stars, int score, bool bestKleiner = false)
        {
            var first = !ArcadeCleared(state, spiel, levelId);
            var bestAlt = ArcadeBest(state, spiel, levelId);
            var newBest = bestKleiner && bestAlt > 0 ? score < bestAlt : score > bestAlt;
            if (state == null) return (false, false);
            var comp = Lese(state);
            var eintrag = (JObject)comp["arcade"][spiel];
            var key = LevelKey(levelId);
            ((JObject)eintrag["cleared"])[key] = true;
            ((JObject)eintrag["stars"])[key] = Math.Max(ArcadeStars(state, spiel, levelId), stars);
            if (newBest || bestAlt == 0) ((JObject)eintrag["best"])[key] = score;
            Schreibe(state, comp);
            return (first, newBest);
        }

        private static JObject DefaultArcade()
        {
            var arcade = new JObject();
            foreach (var spiel in ArcadeSpiele) arcade[spiel] = new JObject { ["stars"] = new JObject(), ["best"] = new JObject(), ["cleared"] = new JObject() };
            return arcade;
        }

        private static JObject NormalizeArcade(JToken raw)
        {
            var arcade = raw as JObject ?? new JObject();
            foreach (var spiel in ArcadeSpiele)
            {
                var eintrag = arcade[spiel] as JObject ?? new JObject();
                foreach (var feld in new[] { "stars", "best", "cleared" })
                    if (!(eintrag[feld] is JObject)) eintrag[feld] = new JObject();
                arcade[spiel] = eintrag;
            }
            return arcade;
        }

        private static JObject NormalizeGeister(JToken raw)
        {
            var sauber = new JObject();
            if (!(raw is JObject geister)) return sauber;
            foreach (var p in geister.Properties())
            {
                if (!(p.Value is JObject eintrag)) continue;
                if (!RanchCompKatalog.Disziplinen.Contains(p.Name) && !p.Name.StartsWith("zeit", StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(eintrag["b64"]?.Type == JTokenType.String ? (string)eintrag["b64"] : "")) continue;
                sauber[p.Name] = eintrag;
            }
            return sauber;
        }

        private static JObject ArcadeFeld(JObject state, string spiel, string feld) => Lese(state)["arcade"]?[spiel]?[feld] as JObject ?? new JObject();
    }

    /// <summary>
    /// Arcade ladders of ranchTonnen/ranchZeit (comp_arcade.gd): 10 runs each — the barrel ideal drops 26 → 17 s, the time
    /// trial takes its target from the real route length (fixed course seed, so ghosts stay comparable across sessions).
    /// </summary>
    public static class RanchCompArcade
    {
        public const int LevelAnzahl = 10;
        public const double TonnenIdealStartS = 26.0, TonnenIdealSchrittS = 1.0;
        public const int ZeitKursSeed = 4242;
        public const double ZeitTempoMs = 5.2, ZeitPolsterS = 6.0;
        public const int ScoreBasis = 40, ScoreJeStern = 25, ScoreZeitbonusMax = 60, FirstClearBonus = 30;

        public static int ClampLevel(int level) => Math.Max(1, Math.Min(LevelAnzahl, level));

        public static double TonnenIdealS(int level) => TonnenIdealStartS - TonnenIdealSchrittS * (ClampLevel(level) - 1);

        public static List<RcompKurs.Tor> ZeitRoute(int level) => RcompKurs.ZeitRoute(ClampLevel(level), ZeitKursSeed);

        public static readonly V2 ZeitStart = new V2(0f, 58f);

        public static double ZeitZielS(int level)
        {
            var vorher = ZeitStart;
            var laenge = 0.0;
            foreach (var tor in ZeitRoute(level))
            {
                laenge += (tor.Pos - vorher).Length;
                vorher = tor.Pos;
            }
            return Math.Floor((laenge / ZeitTempoMs + ZeitPolsterS) / 0.5 + 0.5) * 0.5;
        }

        /// <summary>Both games rate stars on the barrel scale (1.0 / 1.15 / 1.35).</summary>
        public static int Sterne(double wertungS, double zielS) => RcompWertung.Tonnen.Sterne(wertungS, zielS);

        public static int Score(int stars, double wertungS, double zielS, bool firstClear)
        {
            var zeitbonus = Math.Max(0, Math.Min(ScoreZeitbonusMax, (int)Math.Round(zielS * 1.35 - wertungS, MidpointRounding.AwayFromZero)));
            var summe = ScoreBasis + ScoreJeStern * Math.Max(0, Math.Min(3, stars)) + zeitbonus;
            return summe + (firstClear ? FirstClearBonus : 0);
        }

        public static string ZeitGeistKey(int level) => "zeit_k" + ClampLevel(level);
    }

    /// <summary>
    /// The tournament league rules (comp_liga.gd): promotion by league points, no relegation; starting needs the league
    /// class AND the horse level gate; the weekly tournament day draws 3 bonus disciplines from date + seed, offline-first.
    /// </summary>
    public static class RanchCompLiga
    {
        public static readonly int[] PunkteJePlatz = { 10, 7, 5, 3, 1 };

        public static int PunkteFuerPlatz(JObject balance, int platz)
        {
            var liste = balance?["liga_punkte_je_platz"] is JArray arr && arr.Count > 0 ? arr.Select(t => RanchCompKatalog.Num(t, 1.0)).ToArray() : PunkteJePlatz.Select(p => (double)p).ToArray();
            if (platz < 1) return 0;
            return (int)liste[Math.Min(platz, liste.Length) - 1];
        }

        public static int AufstiegAb(JObject balance, string klasse) => (int)RanchCompKatalog.Num(RanchCompKatalog.Klasse(balance, klasse)["aufstieg_punkte"], 0.0);

        public static (bool aufgestiegen, string klasse) PruefeAufstieg(JObject balance, string klasse, int punkteInKlasse)
        {
            var schwelle = AufstiegAb(balance, klasse);
            var danach = RanchCompKatalog.KlasseDanach(klasse);
            return schwelle > 0 && danach != "" && punkteInKlasse >= schwelle ? (true, danach) : (false, klasse);
        }

        public static bool StartErlaubt(JObject balance, string ligaKlasse, string startKlasse, int pferdeLevel)
        {
            if (RanchCompKatalog.KlasseIndex(startKlasse) > RanchCompKatalog.KlasseIndex(ligaKlasse)) return false;
            return RanchCompKatalog.KlasseErlaubt(balance, startKlasse, pferdeLevel);
        }

        public static double AufstiegFortschritt(JObject balance, string klasse, int punkte)
        {
            var schwelle = AufstiegAb(balance, klasse);
            return schwelle <= 0 ? 1.0 : Math.Max(0.0, Math.Min(1.0, (double)punkte / schwelle));
        }

        /// <summary>This week's tournament day: 3 disciplines from date ("YYYY-MM-DD") + seed, the same on every device.</summary>
        public static List<string> TurniertagPlan(JObject balance, string datum, int seed)
        {
            var anzahl = (int)RanchCompKatalog.Num(balance?["turniertag"]?["disziplinen_je_woche"], 3.0);
            var rng = new GoobyRng(seed + WochenHash(datum));
            var pool = new List<string>(RanchCompKatalog.Disziplinen);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = (int)(rng.Next() * (i + 1));
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool.Take(Math.Max(1, Math.Min(pool.Count, anzahl))).ToList();
        }

        public static double BonusGoldMult(JObject balance) => RanchCompKatalog.Num(balance?["turniertag"]?["bonus_gold_mult"], 1.25);

        public static string SchleifenKey(string disziplin, string klasse) => disziplin + "_" + klasse;

        /// <summary>The first podium per class earns the cup, a star-class win per discipline the star ("" = nothing new).</summary>
        public static string TrophaeeFuer(string disziplin, string klasse, int platz)
        {
            if (platz == 1 && klasse == "sternenklasse") return "stern_" + disziplin;
            if (platz >= 1 && platz <= 3) return "pokal_" + klasse;
            return "";
        }

        public static int WochenHash(string datum)
        {
            var teile = (datum ?? "").Split('-');
            if (teile.Length < 3) return 0;
            int.TryParse(teile[0], out var jahr);
            int.TryParse(teile[1], out var monat);
            int.TryParse(teile[2], out var tag);
            var tagImJahr = (monat - 1) * 31 + (tag - 1);
            return jahr * 54 + tagImJahr / 7;
        }
    }

    /// <summary>A starter of the tournament field: roster entry, ability, and the run it rode (simulated or the player's).</summary>
    public sealed class RcompStarter
    {
        public string Id, Name, Pferd, Rasse, Farbe, Zug, Liebling;
        public double Talent, Koennen, Wert, ZeitS;
        public JObject Detail = new JObject();
        public bool IstSpieler;

        public RcompStarter Copy() => (RcompStarter)MemberwiseClone();
    }

    /// <summary>
    /// The bot riders of the league (comp_bots.gd) — lovingly named riders with their own horses ("Rosalinde auf
    /// Donnerkeks"), deterministic from the seed. Ability comes from the class band (no rubber band: bots roll their run
    /// before the player rides); the simulation scores through the SAME formulas as the player.
    /// </summary>
    public static class RanchCompBots
    {
        public const double LieblingBonus = 0.07, FremdMalus = 0.02, SpielerFahrGewicht = 0.55, SpielerStatGewicht = 0.45, RennenRundeBasisS = 34.0;

        public static readonly RcompStarter[] Roster =
        {
            new RcompStarter { Id = "rosalinde", Name = "Rosalinde", Pferd = "Donnerkeks", Rasse = "donnerbommel", Farbe = "braun", Zug = "gemuetlich", Talent = 0.5, Liebling = "springen" },
            new RcompStarter { Id = "fjodor", Name = "Fjodor Streusel", Pferd = "Zimtwölkchen", Rasse = "wolkentraber", Farbe = "palomino", Zug = "vertraeumt", Talent = 0.72, Liebling = "dressur" },
            new RcompStarter { Id = "waltraud", Name = "Oma Waltraud", Pferd = "Blitzgurke", Rasse = "flitzewind", Farbe = "fuchs", Zug = "rasant", Talent = 0.9, Liebling = "rennen" },
            new RcompStarter { Id = "bodo", Name = "Bäckermeister Bodo", Pferd = "Brezelchen", Rasse = "puschelhufer", Farbe = "braun", Zug = "verfressen", Talent = 0.28, Liebling = "schau" },
            new RcompStarter { Id = "frieda", Name = "Frieda Fussel", Pferd = "Kartoffelmus", Rasse = "westernwuschel", Farbe = "schecke", Zug = "ordentlich", Talent = 0.45, Liebling = "trail" },
            new RcompStarter { Id = "kunibert", Name = "Ritter Kunibert", Pferd = "Turboschnecke", Rasse = "moosmaehne", Farbe = "schwarz", Zug = "heldenhaft", Talent = 0.55, Liebling = "gelaende" },
            new RcompStarter { Id = "paula", Name = "Postbotin Paula", Pferd = "Paketchen", Rasse = "knuffpony", Farbe = "fuchs", Zug = "flink", Talent = 0.62, Liebling = "tonnen" },
            new RcompStarter { Id = "knuff", Name = "Professor Knuff", Pferd = "Fußnote", Rasse = "toelterle", Farbe = "rauchgrau", Zug = "gruebelnd", Talent = 0.66, Liebling = "dressur" },
            new RcompStarter { Id = "miapia", Name = "Mia und Pia", Pferd = "Doppeldecker", Rasse = "zottelgnuff", Farbe = "braun", Zug = "kichernd", Talent = 0.4, Liebling = "rennen" },
            new RcompStarter { Id = "gustav", Name = "Gärtner Gustav", Pferd = "Rasenmäher", Rasse = "moosmaehne", Farbe = "apricot", Zug = "geerdet", Talent = 0.35, Liebling = "gelaende" },
            new RcompStarter { Id = "klara", Name = "Königin Klara", Pferd = "Glitzerpüppi", Rasse = "miniknopf", Farbe = "weiss", Zug = "majestaetisch", Talent = 0.82, Liebling = "schau" },
            new RcompStarter { Id = "hein", Name = "Hufschmied Hein", Pferd = "Amboss", Rasse = "federsprung", Farbe = "schwarz", Zug = "felsenruhig", Talent = 0.76, Liebling = "springen" },
        };

        public static RcompStarter Bot(string id) => Roster.FirstOrDefault(b => b.Id == id);

        public static double Koennen(JObject balance, RcompStarter eintrag, string klasse, string disz)
        {
            var (von, bis) = RanchCompKatalog.BotBand(balance, klasse);
            var basis = von + (bis - von) * Math.Max(0.0, Math.Min(1.0, eintrag.Talent));
            basis += eintrag.Liebling == disz ? LieblingBonus : -FremdMalus;
            return Math.Max(0.05, Math.Min(0.98, basis));
        }

        /// <summary>The field of a class: <paramref name="anzahl"/> bots drawn by Fisher-Yates over the seed.</summary>
        public static List<RcompStarter> Starterfeld(JObject balance, string klasse, string disz, int seed, int anzahl = 5)
        {
            var rng = new GoobyRng(seed);
            var indices = Enumerable.Range(0, Roster.Length).ToList();
            for (var i = indices.Count - 1; i > 0; i--)
            {
                var j = (int)(rng.Next() * (i + 1));
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
            var feld = new List<RcompStarter>();
            for (var i = 0; i < Math.Min(anzahl, indices.Count); i++)
            {
                var starter = Roster[indices[i]].Copy();
                starter.Detail = new JObject();
                starter.Koennen = Koennen(balance, Roster[indices[i]], klasse, disz);
                feld.Add(starter);
            }
            return feld;
        }

        /// <summary>Player proxy for fairness tests: 0.55·riding quality + 0.45·normalised discipline stats.</summary>
        public static double SpielerKoennen(JObject balance, string disz, IReadOnlyDictionary<string, double> stats, double fahr)
        {
            var summe = 0.0;
            var anzahl = 0;
            if (RanchCompKatalog.Disziplin(balance, disz)["stats"] is JArray keys)
                foreach (var k in keys)
                {
                    summe += RideStats.Stat(stats, (string)k);
                    anzahl++;
                }
            var statNorm = anzahl == 0 ? 0.474 : (summe / anzahl - 1.0) / 19.0;
            return Math.Max(0.0, Math.Min(1.0, SpielerFahrGewicht * Math.Max(0.0, Math.Min(1.0, fahr)) + SpielerStatGewicht * statNorm));
        }

        /// <summary>A whole simulated run through the real scoring modules → (value, time, detail).</summary>
        public static RcompErgebnis SimuliereLauf(JObject balance, string disz, string klasse, double koennenWert, int seed)
        {
            var rng = new GoobyRng(seed);
            var k = Math.Max(0.0, Math.Min(1.0, koennenWert));
            switch (disz)
            {
                case "springen": return SimSpringen(balance, klasse, k, rng);
                case "dressur": return SimDressur(k, rng);
                case "gelaende": return SimGelaende(balance, klasse, k, rng);
                case "rennen": return SimRennen(k, rng);
                case "trail": return SimTrail(k, rng);
                case "schau": return SimSchau(k, rng);
                case "tonnen": return SimTonnen(klasse, k, rng);
            }
            return new RcompErgebnis();
        }

        private static RcompErgebnis SimSpringen(JObject balance, string klasse, double k, GoobyRng rng)
        {
            var n = RcompWertung.Springen.HindernisAnzahl(klasse);
            var fehler = Math.Pow(1.0 - k, 1.6);
            var abwuerfe = Binomial(rng, n, fehler * 0.38);
            var verweigerungen = Binomial(rng, n, fehler * 0.05);
            var perfekte = Binomial(rng, Math.Max(0, n - abwuerfe), 0.15 + k * 0.5);
            var richtzeit = RanchCompKatalog.RichtzeitS(balance, "springen", klasse);
            var zeit = richtzeit * (0.92 + (1.0 - k) * 0.22 + rng.Next() * 0.08);
            var score = RcompWertung.Springen.Score(abwuerfe, verweigerungen, zeit, richtzeit, perfekte);
            return new RcompErgebnis { Wert = score, ZeitS = zeit, Detail = new JObject { ["abwuerfe"] = abwuerfe, ["verweigerungen"] = verweigerungen, ["perfekte"] = perfekte } };
        }

        private static RcompErgebnis SimDressur(double k, GoobyRng rng)
        {
            var punkte = new List<double>();
            var fehlerGesamt = 0;
            for (var f = 0; f < RcompWertung.Dressur.Figuren.Length; f++)
            {
                var dMittel = (1.0 - k) * 0.8 * (0.6 + rng.Next() * 0.8);
                var gangartfehler = rng.Next() < (1.0 - k) * 0.3 ? 1 : 0;
                fehlerGesamt += gangartfehler;
                punkte.Add(RcompWertung.Dressur.FigurPunkte(dMittel, gangartfehler));
            }
            var takt = rng.Next() < k * 0.85;
            return new RcompErgebnis
            {
                Wert = RcompWertung.Dressur.Gesamt(punkte, takt),
                Detail = new JObject { ["figuren"] = new JArray(punkte), ["takt"] = takt, ["gangartfehler"] = fehlerGesamt },
            };
        }

        private static RcompErgebnis SimGelaende(JObject balance, string klasse, double k, GoobyRng rng)
        {
            var tore = RcompWertung.Gelaende.TorAnzahl(klasse);
            var richtzeit = RanchCompKatalog.RichtzeitS(balance, "gelaende", klasse);
            var zeit = richtzeit * (0.88 + (1.0 - k) * 0.3 + rng.Next() * 0.08);
            var verpasst = Binomial(rng, tore, Math.Pow(1.0 - k, 2.0) * 0.12);
            return new RcompErgebnis { Wert = RcompWertung.Gelaende.WertungS(zeit, verpasst), ZeitS = zeit, Detail = new JObject { ["verpasst"] = verpasst, ["tore"] = tore } };
        }

        private static RcompErgebnis SimRennen(double k, GoobyRng rng)
        {
            var zeit = 0.0;
            for (var r = 0; r < RcompWertung.Rennen.Runden; r++)
            {
                var runde = RennenRundeBasisS * (1.14 - 0.26 * k + (rng.Next() - 0.5) * 0.05);
                if (rng.Next() < 0.25 + k * 0.4) runde -= RennenRundeBasisS * RcompWertung.Rennen.WindschattenBoost * 0.6;
                zeit += runde;
            }
            return new RcompErgebnis { Wert = zeit, ZeitS = zeit, Detail = new JObject { ["runden"] = RcompWertung.Rennen.Runden } };
        }

        private static RcompErgebnis SimTrail(double k, GoobyRng rng)
        {
            var aufgaben = new List<int>();
            for (var a = 0; a < RcompWertung.Trail.AufgabenAnzahl; a++)
            {
                var ausgelassen = rng.Next() < Math.Pow(1.0 - k, 2.5) * 0.15;
                var beruehrungen = Binomial(rng, 3, (1.0 - k) * 0.42);
                aufgaben.Add(RcompWertung.Trail.AufgabePunkte(beruehrungen, ausgelassen));
            }
            var zeit = 118.0 - k * 34.0 + rng.Next() * 12.0;
            return new RcompErgebnis { Wert = RcompWertung.Trail.Gesamt(aufgaben, zeit), ZeitS = zeit, Detail = new JObject { ["aufgaben"] = new JArray(aufgaben) } };
        }

        private static RcompErgebnis SimSchau(double k, GoobyRng rng)
        {
            var pflege = Math.Max(0.0, Math.Min(100.0, 52.0 + k * 42.0 + rng.Next() * 8.0));
            var stil = Math.Max(0.0, Math.Min(100.0, 24.0 + k * 62.0 + rng.Next() * 12.0));
            var treffer = Binomial(rng, RcompWertung.Schau.KuerKommandos, 0.3 + k * 0.62);
            return new RcompErgebnis
            {
                Wert = RcompWertung.Schau.Gesamt(pflege, stil, RcompWertung.Schau.Kuer(treffer)),
                Detail = new JObject { ["pflege"] = pflege, ["stil"] = stil, ["kuer_treffer"] = treffer },
            };
        }

        private static RcompErgebnis SimTonnen(string klasse, double k, GoobyRng rng)
        {
            var ideal = RcompWertung.Tonnen.Idealzeit(klasse);
            var zeit = ideal * (0.94 + (1.0 - k) * 0.3 + rng.Next() * 0.08);
            var umgeworfen = Binomial(rng, RcompWertung.Tonnen.TonnenAnzahl, Math.Pow(1.0 - k, 2.0) * 0.25);
            return new RcompErgebnis { Wert = RcompWertung.Tonnen.WertungS(zeit, umgeworfen), ZeitS = zeit, Detail = new JObject { ["umgeworfen"] = umgeworfen } };
        }

        public static int Binomial(GoobyRng rng, int n, double p)
        {
            var treffer = 0;
            var w = Math.Max(0.0, Math.Min(1.0, p));
            for (var i = 0; i < Math.Max(0, n); i++) if (rng.Next() < w) treffer++;
            return treffer;
        }
    }

    /// <summary>
    /// Tournament day orchestration (comp_turnier.gd): bots are simulated BEFORE the player's run (deterministic, no rubber
    /// band), then finishing order → place → reward (gold, XP, ribbon, trophy, league points) → ceremony.
    /// </summary>
    public static class RanchCompTurnier
    {
        public const int BotsJeTurnier = 5;

        public sealed class Lohn
        {
            public int Gold, Xp, LigaPunkte;
            public bool Schleife;
            public string Trophaee = "";
        }

        public sealed class Bericht
        {
            public bool Aufgestiegen, SchleifeNeu, TrophaeeNeu;
            public string NeueKlasse = "holz";
            public int Gold, Xp, LigaPunkte;
        }

        public static List<RcompStarter> BotsSimulieren(JObject balance, string disziplin, string klasse, int seed)
        {
            var feld = RanchCompBots.Starterfeld(balance, klasse, disziplin, seed, BotsJeTurnier);
            for (var i = 0; i < feld.Count; i++)
            {
                var lauf = RanchCompBots.SimuliereLauf(balance, disziplin, klasse, feld[i].Koennen, seed + 101 * (i + 1));
                feld[i].Wert = lauf.Wert;
                feld[i].ZeitS = lauf.ZeitS;
                feld[i].Detail = lauf.Detail;
            }
            return feld;
        }

        /// <summary>Final standings: the player and the bots sorted in the discipline's direction (stable).</summary>
        public static List<RcompStarter> Endstand(JObject balance, string disziplin, IEnumerable<RcompStarter> bots, double spielerWert, double spielerZeitS, string spielerPferd)
        {
            var liste = bots.Select(b =>
            {
                var c = b.Copy();
                c.IstSpieler = false;
                return c;
            }).ToList();
            liste.Add(new RcompStarter { Id = "spieler", IstSpieler = true, Wert = spielerWert, ZeitS = spielerZeitS, Pferd = spielerPferd, Name = "" });
            return RanchCompKatalog.ZeitGewinnt(balance, disziplin) ? liste.OrderBy(s => s.Wert).ToList() : liste.OrderByDescending(s => s.Wert).ToList();
        }

        public static int SpielerPlatz(IList<RcompStarter> stand)
        {
            for (var i = 0; i < stand.Count; i++) if (stand[i].IstSpieler) return i + 1;
            return stand.Count;
        }

        public static Lohn Belohnung(JObject balance, string disziplin, string klasse, int platz, bool turniertag, double heimGoldMult = 1.0)
        {
            double gold = RanchCompKatalog.GoldFuerPlatz(balance, klasse, platz);
            if (turniertag) gold *= RanchCompLiga.BonusGoldMult(balance);
            gold *= Math.Max(1.0, heimGoldMult);
            return new Lohn
            {
                Gold = (int)Math.Round(gold, MidpointRounding.AwayFromZero),
                Xp = RanchCompKatalog.XpFuerTeilnahme(balance, klasse),
                LigaPunkte = RanchCompLiga.PunkteFuerPlatz(balance, platz),
                Schleife = RanchCompKatalog.SchleifeVerdient(balance, platz),
                Trophaee = RanchCompLiga.TrophaeeFuer(disziplin, klasse, platz),
            };
        }

        /// <summary>Books a tournament: league points/promotion, ribbon, trophy, counters in ranch.comp; gold into economy.coins.</summary>
        public static Bericht Verbuche(JObject state, JObject balance, string disziplin, string klasse, int platz, Lohn lohn)
        {
            var comp = RanchCompState.Lese(state);
            var punkte = (JObject)comp["punkte"];
            punkte[klasse] = (int)RanchCompKatalog.Num(punkte[klasse], 0.0) + lohn.LigaPunkte;
            comp["teilnahmen"] = (int)RanchCompKatalog.Num(comp["teilnahmen"], 0.0) + 1;
            if (platz == 1) comp["siege"] = (int)RanchCompKatalog.Num(comp["siege"], 0.0) + 1;
            var aufstieg = (aufgestiegen: false, klasse: (string)comp["klasse"]);
            if (klasse == (string)comp["klasse"])
            {
                aufstieg = RanchCompLiga.PruefeAufstieg(balance, klasse, (int)punkte[klasse]);
                if (aufstieg.aufgestiegen) comp["klasse"] = aufstieg.klasse;
            }
            var schleifen = (JObject)comp["schleifen"];
            var schlKey = RanchCompLiga.SchleifenKey(disziplin, klasse);
            var schleifeNeu = false;
            if (lohn.Schleife)
            {
                var bisher = (int)RanchCompKatalog.Num(schleifen[schlKey], 99.0);
                if (platz < bisher)
                {
                    schleifen[schlKey] = platz;
                    schleifeNeu = true;
                }
            }
            var trophaeen = (JArray)comp["trophaeen"];
            var trophaeeNeu = lohn.Trophaee != "" && !trophaeen.Any(t => (string)t == lohn.Trophaee);
            if (trophaeeNeu) trophaeen.Add(lohn.Trophaee);
            RanchCompState.Schreibe(state, comp);
            if (state != null && lohn.Gold > 0 && state["economy"] is JObject economy) economy["coins"] = (int)RanchCompKatalog.Num(economy["coins"], 0.0) + lohn.Gold;
            return new Bericht
            {
                Aufgestiegen = aufstieg.aufgestiegen, NeueKlasse = aufstieg.klasse, SchleifeNeu = schleifeNeu, TrophaeeNeu = trophaeeNeu,
                Gold = lohn.Gold, Xp = lohn.Xp, LigaPunkte = lohn.LigaPunkte,
            };
        }

        /// <summary>The best horse in the stable (highest level, then bond) → (id, horse) or null without horses.</summary>
        public static (string id, JObject pferd)? BestesPferd(JObject state)
        {
            if (!(state?["ranch"]?["tiere"]?["pferde"] is JObject pferde)) return null;
            string besteId = null;
            JObject bestes = null;
            var besteWertung = -1.0;
            foreach (var p in pferde.Properties())
            {
                if (!(p.Value is JObject pferd)) continue;
                var wertung = RanchCompKatalog.Num(pferd["level"], 1.0) * 1000.0 + RanchCompKatalog.Num(pferd["bindung"], 0.0);
                if (wertung > besteWertung)
                {
                    besteWertung = wertung;
                    besteId = p.Name;
                    bestes = pferd;
                }
            }
            return besteId == null ? ((string, JObject)?)null : (besteId, bestes);
        }

        /// <summary>The store variant of <see cref="Verbuche"/>: books under one update and announces ranch + economy.</summary>
        public static Bericht Verbuche(GameStateStore store, JObject balance, string disziplin, string klasse, int platz, Lohn lohn)
        {
            Bericht bericht = null;
            store.Update(s => bericht = Verbuche(s, balance, disziplin, klasse, platz, lohn));
            store.NotifySliceChanged("ranch");
            store.NotifySliceChanged("economy");
            return bericht;
        }
    }
}
