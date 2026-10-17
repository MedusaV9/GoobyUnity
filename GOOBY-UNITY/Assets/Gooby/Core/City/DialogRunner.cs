using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// Port of city/dialog_runner.gd: pure evaluation of the place dialog trees under city/dialoge/*.json
    /// (tierarzt, goobytheke, rehwei, …). Tree format: {id, start, nodes:{id:{sprecher, text (string|array),
    /// optionen:[{text, next, cond}], effekt:[string], next, ende}}}. cond: "flag:x" / "!flag:x";
    /// effekt: "flag:x" (set), "flag_weg:x" (clear), "item:id" (inventory +1), "laden" (open the shop).
    /// The runner knows neither the UI nor the store — only a flag dictionary; the view applies the effects.
    /// </summary>
    public sealed class DialogRunner
    {
        public const string DialogePath = "city/dialoge/";

        public sealed class Option
        {
            public string Text, Next;
            public int Index;
        }

        public sealed class Effekt
        {
            public string Typ, Name; // "flag" | "item" | "laden"
            public bool Wert;
        }

        public JObject Baum { get; }
        public Dictionary<string, bool> Flags { get; }
        public string Aktuell { get; private set; }

        public DialogRunner(JObject baum, Dictionary<string, bool> flags = null)
        {
            Baum = baum ?? new JObject();
            Flags = flags ?? new Dictionary<string, bool>();
            Aktuell = J.Str(Baum["start"]);
        }

        /// <summary>Loads a tree by place id; a non-German locale prefers city/dialoge/&lt;locale&gt;/&lt;id&gt; when it exists.</summary>
        public static JObject Laden(string ortId, string locale = "de")
        {
            if (string.IsNullOrEmpty(ortId)) return null;
            if (!string.IsNullOrEmpty(locale) && locale != "de")
            {
                var localized = ContentSource.TryLoadObject(DialogePath + locale + "/" + ortId);
                if (localized != null) return localized;
            }
            return ContentSource.TryLoadObject(DialogePath + ortId);
        }

        public static bool CondOk(string cond, Dictionary<string, bool> flags)
        {
            if (string.IsNullOrEmpty(cond)) return true;
            if (cond.StartsWith("!flag:", StringComparison.Ordinal)) return !(flags.TryGetValue(cond.Substring(6), out var neg) && neg);
            if (cond.StartsWith("flag:", StringComparison.Ordinal)) return flags.TryGetValue(cond.Substring(5), out var pos) && pos;
            return false;
        }

        public static Effekt EffektParsen(string effekt)
        {
            if (string.IsNullOrEmpty(effekt)) return null;
            if (effekt.StartsWith("flag_weg:", StringComparison.Ordinal)) return new Effekt { Typ = "flag", Name = effekt.Substring(9), Wert = false };
            if (effekt.StartsWith("flag:", StringComparison.Ordinal)) return new Effekt { Typ = "flag", Name = effekt.Substring(5), Wert = true };
            if (effekt.StartsWith("item:", StringComparison.Ordinal)) return new Effekt { Typ = "item", Name = effekt.Substring(5), Wert = true };
            if (effekt == "laden") return new Effekt { Typ = "laden", Name = "", Wert = true };
            return null;
        }

        public bool IstGeladen => Knoten[Aktuell] is JObject;

        private JObject Knoten => Baum["nodes"] as JObject ?? new JObject();

        private JObject KnotenAktuell => Knoten[Aktuell] as JObject ?? new JObject();

        public string Sprecher => J.Str(KnotenAktuell["sprecher"]);

        /// <summary>Text lines of the current node (a single string becomes one line).</summary>
        public List<string> Text()
        {
            var raw = KnotenAktuell["text"];
            var zeilen = new List<string>();
            if (raw is JArray arr) foreach (var z in arr) zeilen.Add(J.Str(z));
            else if (raw != null && raw.Type != JTokenType.Null) zeilen.Add(J.Str(raw));
            return zeilen;
        }

        /// <summary>Visible options (cond-filtered) with their index in the node's list.</summary>
        public List<Option> Optionen()
        {
            var outp = new List<Option>();
            if (!(KnotenAktuell["optionen"] is JArray raw)) return outp;
            for (var i = 0; i < raw.Count; i++)
            {
                if (!(raw[i] is JObject o)) continue;
                if (CondOk(J.Str(o["cond"]), Flags)) outp.Add(new Option { Text = J.Str(o["text"]), Next = J.Str(o["next"]), Index = i });
            }
            return outp;
        }

        /// <summary>Parsed effects of the current node — the view applies them once when the node is entered.</summary>
        public List<Effekt> Effekte()
        {
            var outp = new List<Effekt>();
            if (!(KnotenAktuell["effekt"] is JArray raw)) return outp;
            foreach (var e in raw)
            {
                var parsed = EffektParsen(J.Str(e));
                if (parsed != null) outp.Add(parsed);
            }
            return outp;
        }

        public string AutoNext => J.Str(KnotenAktuell["next"]);

        /// <summary>Follows the node's own "next" (nodes without options). False when there is none.</summary>
        public bool Weiter()
        {
            var ziel = AutoNext;
            if (ziel.Length == 0 || !(Knoten[ziel] is JObject)) return false;
            Aktuell = ziel;
            FlagsAnwenden();
            return true;
        }

        /// <summary>End node: ende:true, or neither options nor auto-next.</summary>
        public bool IstEnde => J.IsTrue(KnotenAktuell["ende"]) || (Optionen().Count == 0 && AutoNext.Length == 0);

        /// <summary>Picks a visible option; the target node's flag effects are mirrored into Flags so follow-up conds hold.</summary>
        public bool Waehlen(int optionsIndex)
        {
            var sichtbar = Optionen();
            if (optionsIndex < 0 || optionsIndex >= sichtbar.Count) return false;
            var ziel = sichtbar[optionsIndex].Next;
            if (!(Knoten[ziel] is JObject)) return false;
            Aktuell = ziel;
            FlagsAnwenden();
            return true;
        }

        private void FlagsAnwenden()
        {
            foreach (var e in Effekte())
            {
                if (e.Typ != "flag") continue;
                if (e.Wert) Flags[e.Name] = true;
                else Flags.Remove(e.Name);
            }
        }
    }
}
