using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Gooby's lasting mood (port of soul_mood.gd, SEELE-2): ONE value 0..100 that moves over hours and colours the
    /// resting face, the idle pace and pick, and the voice. Its target is the stats truth (<see cref="Stats.Mood"/>
    /// with the early-wake debuff); the value approaches it exponentially with a 90-minute half-life, and small event
    /// bumps (feeding, petting, a reunion) move it at once, capped at ±8. Pure statics; lives in soul.stimmung.
    /// </summary>
    public static class SoulMood
    {
        public const double DefaultWert = 62.0;
        public const double HalbwertMin = 90.0;
        public const double MaxSchrittMin = 12.0 * 60.0;
        public const double StossMax = 8.0;
        public const double ErschoepftEnergie = 15.0;

        /// <summary>Bands in rising order (index gates in idle defs).</summary>
        public static readonly string[] BandOrdnung = { "miserable", "grumpy", "neutral", "happy", "ecstatic" };

        /// <summary>Band → rig emotion ("angry" IS the web's grumpy).</summary>
        public static string BandEmotion(string band)
        {
            switch (band)
            {
                case "ecstatic": return "ecstatic";
                case "happy": return "happy";
                case "grumpy": return "angry";
                case "miserable": return "sad";
                default: return "neutral";
            }
        }

        public static JObject Default() => new JObject { ["wert"] = DefaultWert, ["aktualisiertMs"] = 0L };

        /// <summary>Self-heal: broken types fall back to the defaults, the value is clamped to 0..100.</summary>
        public static JObject Normalize(JToken raw)
        {
            var outp = Default();
            if (!(raw is JObject o)) return outp;
            var wert = J.NumOr(o["wert"], double.NaN);
            if (J.Finite(wert)) outp["wert"] = J.Clamp(wert, 0.0, 100.0);
            if (J.IsNum(o["aktualisiertMs"])) outp["aktualisiertMs"] = Math.Max(0L, (long)J.Num(o["aktualisiertMs"]));
            return outp;
        }

        public static double Wert(JToken stimmung) => J.Num(Normalize(stimmung)["wert"]);

        /// <summary>Where the mood wants to go: the web mood formula incl. the early-wake debuff.</summary>
        public static double Ziel(JObject stats, double grumpyDebuff = 0.0) => Stats.Mood(stats, grumpyDebuff);

        /// <summary>One step towards the target (exponential, clamped dt); a NEW stimmung object.</summary>
        public static JObject Advance(JToken stimmung, double zielWert, long nowMs)
        {
            var s = Normalize(stimmung);
            var last = J.Long(s["aktualisiertMs"]);
            var dtMin = last > 0 && nowMs > last ? Math.Min((nowMs - last) / 60000.0, MaxSchrittMin) : 0.0;
            var anteil = 1.0 - Math.Pow(0.5, dtMin / HalbwertMin);
            var wert = J.Num(s["wert"]);
            wert += (J.Clamp(zielWert, 0.0, 100.0) - wert) * anteil;
            return new JObject { ["wert"] = J.Clamp(wert, 0.0, 100.0), ["aktualisiertMs"] = nowMs };
        }

        /// <summary>Event bump (fed +, left alone −), capped at ±<see cref="StossMax"/>.</summary>
        public static JObject Bump(JToken stimmung, double delta, long nowMs)
        {
            var s = Normalize(stimmung);
            var wert = J.Num(s["wert"]) + J.Clamp(delta, -StossMax, StossMax);
            return new JObject { ["wert"] = J.Clamp(wert, 0.0, 100.0), ["aktualisiertMs"] = Math.Max(J.Long(s["aktualisiertMs"]), nowMs) };
        }

        public static string Band(double wert) => Stats.MoodBand(wert);

        public static int BandIndex(string band) => Math.Max(0, Array.IndexOf(BandOrdnung, band));

        /// <summary>The face BETWEEN moments: sick → sad, exhausted → sleepy, else the band's face.</summary>
        public static string RuheEmotion(double wert, JObject stats, int krankGrad = 0)
        {
            if (krankGrad > 0) return "sad";
            var energie = stats?["energy"];
            if (J.IsNum(energie) && J.Num(energie) <= ErschoepftEnergie) return "sleepy";
            return BandEmotion(Band(wert));
        }

        /// <summary>Factor on the idle pace: a low mood is slower (longer pauses), a good one livelier.</summary>
        public static double IdleTaktFaktor(double wert) => 1.8 + (0.85 - 1.8) * J.Clamp(wert, 0.0, 100.0) / 100.0;

        /// <summary>Voice parameters: a miserable Gooby speaks lower, slower and shorter.</summary>
        public static (double pitch, double tempo, double laenge) Stimme(double wert)
        {
            var t = J.Clamp(wert, 0.0, 100.0) / 100.0;
            return (0.9 + (1.08 - 0.9) * t, 0.8 + (1.1 - 0.8) * t, 0.55 + (1.0 - 0.55) * J.Clamp(t * 1.6, 0.0, 1.0));
        }

        /// <summary>Mood gate of a content def ("mindest_laune"/"hoechst_laune" band ids): no dancing when miserable.</summary>
        public static bool DefErlaubt(JObject def, string band)
        {
            var index = BandIndex(band);
            var min = J.Str(def?["mindest_laune"]);
            if (min != "" && index < BandIndex(min)) return false;
            var max = J.Str(def?["hoechst_laune"]);
            if (max != "" && index > BandIndex(max)) return false;
            return true;
        }
    }

    /// <summary>
    /// Life WITH intent (port of soul_intent.gd, SEELE-2): Gooby does things BECAUSE he wants something — hungry → to
    /// the fridge (or the kitchen door), bored → his toy or the TV, tired → towards the bed, grubby → the bath, rain →
    /// the window. The thresholds sit ABOVE the low warnings so the intent shows before the alarm. Only targets that
    /// exist in the room count — wanting without a visible where looks broken, not alive.
    /// </summary>
    public static class SoulIntent
    {
        public const double SchwelleHunger = 35.0;
        public const double SchwelleSpass = 30.0;
        public const double SchwelleEnergie = 25.0;
        public const double SchwelleHygiene = 30.0;
        public const double RegenDrang = 4.0;
        public const long CooldownMs = 240_000;

        /// <summary>Targets per intent in priority order: "item:&lt;prefix&gt;" | "tuer:&lt;room&gt;" | "fenster".</summary>
        public static readonly (string id, string[] ziele)[] Ziele =
        {
            ("absicht_hunger", new[] { "item:kitchenFridge", "tuer:kitchen" }),
            ("absicht_langeweile", new[] { "item:bear", "item:television" }),
            ("absicht_muede", new[] { "item:bed", "tuer:bedroom" }),
            ("absicht_dreckig", new[] { "item:bathtub", "item:shower", "tuer:bathroom" }),
            ("absicht_regen", new[] { "fenster" }),
        };

        public sealed class Absicht
        {
            public string Id, ZielArt, Ziel;
            public double Drang;
        }

        /// <summary>Every pressing intent, most urgent first (stable). <paramref name="vorhanden"/>: targets this room resolves.</summary>
        public static List<Absicht> Kandidaten(JObject stats, bool regen, bool schlaeft, ICollection<string> vorhanden)
        {
            var outp = new List<Absicht>();
            if (schlaeft) return outp;
            Biete(outp, vorhanden, "absicht_hunger", SchwelleHunger - Stat(stats, "hunger"));
            Biete(outp, vorhanden, "absicht_muede", SchwelleEnergie - Stat(stats, "energy"));
            Biete(outp, vorhanden, "absicht_dreckig", SchwelleHygiene - Stat(stats, "hygiene"));
            Biete(outp, vorhanden, "absicht_langeweile", SchwelleSpass - Stat(stats, "fun"));
            if (regen) Biete(outp, vorhanden, "absicht_regen", RegenDrang);
            return outp.OrderByDescending(a => a.Drang).ToList();
        }

        /// <summary>The ONE intent for now (null: no need, or all of them cooling down).</summary>
        public static Absicht Waehle(JObject stats, bool regen, bool schlaeft, ICollection<string> vorhanden, IDictionary<string, long> cooldowns, long nowMs)
        {
            foreach (var k in Kandidaten(stats, regen, schlaeft, vorhanden))
                if (cooldowns == null || !cooldowns.TryGetValue(k.Id, out var until) || nowMs >= until) return k;
            return null;
        }

        private static void Biete(List<Absicht> outp, ICollection<string> vorhanden, string id, double drang)
        {
            if (drang <= 0.0) return;
            foreach (var (absicht, ziele) in Ziele)
            {
                if (absicht != id) continue;
                foreach (var eintrag in ziele)
                {
                    if (eintrag != "fenster" && (vorhanden == null || !vorhanden.Contains(eintrag))) continue;
                    var teile = eintrag.Split(':');
                    outp.Add(new Absicht { Id = id, Drang = drang, ZielArt = teile[0], Ziel = teile.Length > 1 ? teile[1] : "" });
                    return;
                }
            }
        }

        private static double Stat(JObject stats, string key) => J.IsNum(stats?[key]) ? J.Num(stats[key]) : 100.0;
    }

    /// <summary>
    /// Staged feelings (port of soul_feelings.gd, FEEL-AC): real events (thunder, a find, being caught at an intent, a
    /// new record, the favourite food, the dark, greetings, tickling, new furniture, tiredness, neglect) map to an
    /// emotion and a priority, behind their own brake — 20 s between two, 25 a day, 4 min before the same emotion
    /// again, state feelings (dark, tired, sad) once a day. Lives in soul.feelings.
    /// </summary>
    public static class SoulFeelings
    {
        public const long MinGapMs = 20_000;
        public const int MaxProTag = 25;
        public const long JeEmotionGapMs = 240_000;
        public const double DonnerMinS = 25.0;
        public const double DonnerMaxS = 70.0;
        public const double MuedeEnergie = 25.0;
        public const int DunkelAbH = 22;
        public const int DunkelBisH = 6;

        /// <summary>event → (emotion, prio 1 ambient / 2 notable / 3 strong, gate "tag" = once a day).</summary>
        public static readonly IReadOnlyDictionary<string, (string emotion, int prio, string gate)> Ereignisse =
            new Dictionary<string, (string, int, string)>
            {
                ["donner"] = ("schreck", 3, ""),
                ["umgefallen"] = ("schreck", 3, ""),
                ["fund"] = ("ueberraschung", 2, ""),
                ["geschenk"] = ("ueberraschung", 2, ""),
                ["ertappt"] = ("verlegenheit", 2, ""),
                ["rekord"] = ("stolz", 3, ""),
                ["lieblingsessen"] = ("verliebtheit", 3, ""),
                ["essen"] = ("freude", 1, ""),
                ["dunkelheit"] = ("angst", 1, "tag"),
                ["gruss_gefreut"] = ("freude", 2, ""),
                ["gruss_vermisst"] = ("begeisterung", 2, ""),
                ["gruss_eingeschnappt"] = ("trotz", 2, ""),
                ["kitzeln"] = ("begeisterung", 1, ""),
                ["streichel_bonus"] = ("freude", 1, ""),
                ["muede"] = ("muedigkeit", 1, "tag"),
                ["vernachlaessigt"] = ("traurigkeit", 2, "tag"),
                ["neues_moebel"] = ("neugier", 1, ""),
            };

        public static string EmotionFuer(string ereignis) => Ereignisse.TryGetValue(ereignis ?? "", out var d) ? d.emotion : "";

        public static int Prio(string ereignis) => Ereignisse.TryGetValue(ereignis ?? "", out var d) ? d.prio : 0;

        public static JObject Default() => new JObject
        {
            ["day"] = "", ["count"] = 0, ["lastAt"] = 0L, ["je"] = new JObject(), ["gates"] = new JObject(), ["bestMax"] = 0,
        };

        public static JObject Normalize(JToken raw)
        {
            var outp = Default();
            if (!(raw is JObject f)) return outp;
            if (J.IsString(f["day"])) outp["day"] = f["day"];
            outp["count"] = Math.Max(0, (int)J.Num(f["count"]));
            outp["lastAt"] = Math.Max(0L, (long)J.Num(f["lastAt"]));
            outp["bestMax"] = Math.Max(0, (int)J.Num(f["bestMax"]));
            foreach (var key in new[] { "je", "gates" })
                if (f[key] is JObject map) outp[key] = map.DeepClone();
            return outp;
        }

        /// <summary>May this event stage a feeling NOW? Gap (prio 3 overrides), day cap, per-emotion gap, day gate.</summary>
        public static bool Erlaubt(JToken feelings, string ereignis, long nowMs, string today)
        {
            if (!Ereignisse.TryGetValue(ereignis ?? "", out var def)) return false;
            var f = Normalize(feelings);
            var last = J.Long(f["lastAt"]);
            if (def.prio < 3 && last > 0 && nowMs - last < MinGapMs) return false;
            var count = J.Str(f["day"]) == today ? J.Int(f["count"]) : 0;
            if (count >= MaxProTag) return false;
            var emotionLast = J.Long(f["je"]?[def.emotion]);
            if (emotionLast > 0 && nowMs - emotionLast < JeEmotionGapMs) return false;
            if (def.gate == "tag" && J.Str(f["gates"]?[ereignis]) == today) return false;
            return true;
        }

        /// <summary>Bookkeeping after staging (a new day resets the count); a NEW feelings object.</summary>
        public static JObject Buche(JToken feelings, string ereignis, long nowMs, string today)
        {
            var outp = Normalize(feelings);
            if (J.Str(outp["day"]) != today)
            {
                outp["day"] = today;
                outp["count"] = 0;
            }
            outp["count"] = J.Int(outp["count"]) + 1;
            outp["lastAt"] = nowMs;
            if (Ereignisse.TryGetValue(ereignis ?? "", out var def))
            {
                ((JObject)outp["je"])[def.emotion] = nowMs;
                if (def.gate == "tag") ((JObject)outp["gates"])[ereignis] = today;
            }
            return outp;
        }

        public static double DonnerIntervallS(double zufall01) => DonnerMinS + (DonnerMaxS - DonnerMinS) * J.Clamp(zufall01, 0.0, 1.0);

        public static bool IstDunkel(int hour) => hour >= DunkelAbH || hour < DunkelBisH;

        public static bool IstMuede(JObject stats) => J.NumOr(stats?["energy"], 100.0) < MuedeEnergie;

        /// <summary>The best minigame value over every best board (a rise above feelings.bestMax is a NEW record).</summary>
        public static int RekordMax(JObject state)
        {
            if (!(J.Path(state, "minigames", "legacy") is JObject legacy)) return 0;
            var top = 0;
            foreach (var board in new[] { "best", "endlessBest" })
                if (legacy[board] is JObject e)
                    foreach (var p in e.Properties()) top = Math.Max(top, (int)J.Num(p.Value));
            if (legacy["bestByDiff"] is JObject byDiff)
                foreach (var spiel in byDiff.Properties())
                    if (spiel.Value is JObject stufen)
                        foreach (var p in stufen.Properties()) top = Math.Max(top, (int)J.Num(p.Value));
            return top;
        }

        /// <summary>Favourite food: fed at least <paramref name="minAnzahl"/> times AND the most given.</summary>
        public static bool IstLieblingsessen(JObject foodGiven, string foodId, int minAnzahl)
        {
            var anzahl = (int)J.Num(foodGiven?[foodId]);
            if (anzahl < minAnzahl) return false;
            return foodGiven.Properties().All(p => (int)J.Num(p.Value) <= anzahl);
        }
    }
}
