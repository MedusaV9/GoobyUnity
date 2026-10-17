using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Gooby's chatter lines (port of soul_linien.gd, W14/VOICE): categories of keys "&lt;prefix&gt;&lt;n&gt;"
    /// (strings soul_lines.json + fuettern.json), a pick by roll that never repeats one of the last five of a category
    /// (memory in soul.linien), the five-step "long time no see" ladder and the idle chatter's weather share.
    /// </summary>
    public static class SoulLinien
    {
        public const int GedaechtnisMax = 5;
        public const double PlauderWetterAnteil = 0.35;

        public static readonly IReadOnlyDictionary<string, (string prefix, int anzahl)> Kategorien = new Dictionary<string, (string, int)>
        {
            ["gruss.morgen"] = ("soul.linie.gruss.morgen.", 3),
            ["gruss.mittag"] = ("soul.linie.gruss.mittag.", 2),
            ["gruss.nachmittag"] = ("soul.linie.gruss.nachmittag.", 2),
            ["gruss.abend"] = ("soul.linie.gruss.abend.", 3),
            ["gruss.nacht"] = ("soul.linie.gruss.nacht.", 2),
            ["wetter.sonne"] = ("soul.linie.wetter.sonne.", 2),
            ["wetter.wolken"] = ("soul.linie.wetter.wolken.", 1),
            ["wetter.niesel"] = ("soul.linie.wetter.niesel.", 1),
            ["wetter.regen"] = ("soul.linie.wetter.regen.", 1),
            ["wetter.gewitter"] = ("soul.linie.wetter.gewitter.", 1),
            ["wetter.nebel"] = ("soul.linie.wetter.nebel.", 2),
            ["wetter.schnee"] = ("soul.linie.wetter.schnee.", 2),
            ["w13.ball"] = ("soul.linie.w13.ball.", 6),
            ["w13.nougat"] = ("soul.linie.w13.nougat.", 6),
            ["w13.gobty"] = ("soul.linie.w13.gobty.", 6),
            ["w13.girlande"] = ("soul.linie.w13.girlande.", 4),
            ["w13.galaxie"] = ("soul.linie.w13.galaxie.", 4),
            ["w13.raumstation"] = ("soul.linie.w13.raumstation.", 6),
            ["w13.ranch"] = ("soul.linie.w13.ranch.", 8),
            ["fuettern.obst"] = ("fuettern.kommentar.obst.", 4),
            ["fuettern.gemuese"] = ("fuettern.kommentar.gemuese.", 4),
            ["fuettern.suess"] = ("fuettern.kommentar.suess.", 4),
            ["fuettern.deftig"] = ("fuettern.kommentar.deftig.", 4),
            ["minispiel.rekord"] = ("soul.linie.minispiel.rekord.", 4),
            ["minispiel.knapp"] = ("soul.linie.minispiel.knapp.", 4),
            ["minispiel.verloren"] = ("soul.linie.minispiel.verloren.", 4),
            ["selbstgespraech"] = ("soul.linie.selbstgespraech.", 15),
            ["feier.sticker"] = ("soul.linie.feier.sticker.", 5),
            ["feier.erfolg"] = ("soul.linie.feier.erfolg.", 5),
            ["wiedersehen"] = ("soul.linie.wiedersehen.", 5),
            ["markt.stand"] = ("soul.linie.markt.stand.", 4),
        };

        /// <summary>From this gap (hours) on, step n (1..5) of the reunion ladder holds.</summary>
        public static readonly double[] WiedersehenStufenH = { 6.0, 24.0, 48.0, 168.0, 720.0 };

        public static List<string> KeysOf(string kategorie)
        {
            if (kategorie == null || !Kategorien.TryGetValue(kategorie, out var def)) return new List<string>();
            return Enumerable.Range(1, def.anzahl).Select(n => def.prefix + n).ToList();
        }

        /// <summary>A line by roll; the last five shown of the category are blocked (a small pool frees its oldest).</summary>
        public static string Waehle(string kategorie, JObject gedaechtnis, double roll)
        {
            var pool = KeysOf(kategorie);
            if (pool.Count == 0) return "";
            var zuletzt = Zuletzt(gedaechtnis, kategorie);
            var frei = pool.Where(k => !zuletzt.Contains(k)).ToList();
            if (frei.Count == 0)
            {
                foreach (var key in zuletzt)
                    if (pool.Contains(key)) return key;
                return pool[0];
            }
            return frei[(int)(J.Clamp(roll, 0.0, 0.999999) * frei.Count)];
        }

        /// <summary>Books a shown key into the memory (a NEW map).</summary>
        public static JObject Merke(JObject gedaechtnis, string kategorie, string key)
        {
            var outp = gedaechtnis != null ? (JObject)gedaechtnis.DeepClone() : new JObject();
            var zuletzt = Zuletzt(outp, kategorie);
            zuletzt.Remove(key);
            zuletzt.Add(key);
            while (zuletzt.Count > GedaechtnisMax) zuletzt.RemoveAt(0);
            outp[kategorie] = new JArray(zuletzt);
            return outp;
        }

        /// <summary>The reunion ladder's key for a visit gap ("" below step 1 — the daily greetings take over).</summary>
        public static string WiedersehenKey(long gapMs)
        {
            var hours = gapMs / 3_600_000.0;
            var stufe = WiedersehenStufenH.Count(s => hours >= s);
            return stufe <= 0 ? "" : "soul.linie.wiedersehen." + stufe;
        }

        /// <summary>Idle chatter: on notable weather Gooby sometimes comments on it, else he talks to himself.</summary>
        public static string PlauderKategorie(string wetterTyp, double roll)
        {
            var wetter = "wetter." + wetterTyp;
            return wetterTyp != "wolken" && Kategorien.ContainsKey(wetter) && roll < PlauderWetterAnteil ? wetter : "selbstgespraech";
        }

        public static int GesamtAnzahl() => Kategorien.Values.Sum(d => d.anzahl);

        private static List<string> Zuletzt(JObject gedaechtnis, string kategorie) =>
            gedaechtnis?[kategorie] is JArray arr ? arr.Select(t => J.Str(t)).ToList() : new List<string>();
    }

    /// <summary>
    /// Mini dialogues (port of gooby_gespraech.gd's pure tree navigation, W14/VOICE): when Gooby says a soul moment, two
    /// answer chips may appear; the answer nudges the mood, is remembered in soul.gespraeche and triggers a follow-up
    /// line — at most two levels deep, data-driven by content/soul/data/gespraeche.json.
    /// </summary>
    public static class SoulGespraech
    {
        public const string Pfad = "content/soul/data/gespraeche";
        public const int MaxEbenen = 2;
        public const float ChipTimeoutS = 12f;
        public const float Ebene2PauseS = 2.4f;

        public static JArray Lade() => ContentSource.LoadObject(Pfad)["gespraeche"] as JArray ?? new JArray();

        /// <summary>The dialogue docked to a moment id (null: none).</summary>
        public static JObject FuerAnlass(JArray gespraeche, string anlassId)
        {
            if (gespraeche == null) return null;
            foreach (var g in gespraeche)
                if (g is JObject o && o["anlass"] is JArray anlaesse && anlaesse.Any(a => J.Str(a) == anlassId)) return o;
            return null;
        }

        public static List<JObject> Antworten(JObject ebene) =>
            (ebene?["antworten"] as JArray ?? new JArray()).OfType<JObject>().ToList();

        /// <summary>An answer's follow-up key, the variant by roll ("" when it has none).</summary>
        public static string FolgeKey(JObject antwort, double roll)
        {
            if (!(antwort?["folge_keys"] is JArray keys) || keys.Count == 0) return "";
            return J.Str(keys[(int)(J.Clamp(roll, 0.0, 0.999999) * keys.Count)]);
        }

        /// <summary>The next chip level of an answer (null: the talk is over).</summary>
        public static JObject NaechsteEbene(JObject antwort) => antwort?["weiter"] is JObject w && w.HasValues ? w : null;

        /// <summary>The dialogue an answer belongs to, from its label key domain ("soul_gespraech.&lt;id&gt;.…").</summary>
        public static string GespraechIdOf(JObject antwort)
        {
            var teile = J.Str(antwort?["label_key"]).Split('.');
            return teile.Length >= 2 ? teile[1] : "";
        }

        /// <summary>Books the answer (a NEW map) — Gooby remembers HOW you answered last time.</summary>
        public static JObject MerkeAntwort(JObject gefuehrt, string gespraechId, JObject antwort, long nowMs)
        {
            var outp = gefuehrt != null ? (JObject)gefuehrt.DeepClone() : new JObject();
            var anzahl = J.Int((outp[gespraechId] as JObject)?["anzahl"]);
            outp[gespraechId] = new JObject
            {
                ["antwort"] = J.Str(antwort?["id"]),
                ["erinnerung"] = J.Str(antwort?["erinnerung"]),
                ["beiMs"] = nowMs,
                ["anzahl"] = anzahl + 1,
            };
            return outp;
        }
    }

    /// <summary>
    /// The over-petting gag (port of streichel_uebermut.gd, W15/VOICE2): MORE than ten pets inside 30 s make Gooby fend
    /// you off once, cutely, and ask for a break; then two quiet minutes. Time is handed in.
    /// </summary>
    public sealed class StreichelUebermut
    {
        public const int MaxPets = 10;
        public const long FensterMs = 30_000;
        public const long CooldownMs = 120_000;

        private readonly List<long> _stamps = new List<long>();
        private long _stillBisMs;

        /// <summary>Books one pet; true = the gag may fire NOW (exactly once per burst).</summary>
        public bool Registriere(long nowMs)
        {
            _stamps.Add(nowMs);
            _stamps.RemoveAll(s => nowMs - s >= FensterMs);
            if (nowMs < _stillBisMs || _stamps.Count <= MaxPets) return false;
            _stillBisMs = nowMs + CooldownMs;
            _stamps.Clear();
            return true;
        }
    }
}
