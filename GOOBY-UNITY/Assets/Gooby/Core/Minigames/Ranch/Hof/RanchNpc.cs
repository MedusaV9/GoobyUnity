using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Friendship with the people of Hufingen (port of rnpc_freundschaft.gd with the ranch_npcs.json entries of the five
    /// shopkeepers): points 0..100 → hearts 0..5 at 10/25/45/70/100. Talking gives +3 once a day (+0.5 after), a gift
    /// +12 when loved, +6 when liked, +3 otherwise (+1 for more gifts that day). After seven days away a point a day
    /// fades (at most 10) — never below the heart already reached; nothing ever takes points. Unlocks per heart: small
    /// talk, a discount in the keeper's shop, stories. Gifts are what the ranch has: hay, apples, carrots, oats, treats
    /// (Godot's oat cookies are the treats here). State in <c>ranch.npc.&lt;id&gt;</c>.
    /// </summary>
    public static class RanchNpc
    {
        public const string Key = "npc";
        public static readonly int[] HerzSchwellen = { 0, 10, 25, 45, 70, 100 };
        public const int HerzMax = 5, KarenzTage = 7;
        public const double RedenPunkte = 3.0, RedenTrost = 0.5, GeschenkLiebt = 12.0, GeschenkMag = 6.0, GeschenkNormal = 3.0, GeschenkTrost = 1.0,
            VerfallProTag = 1.0, VerfallMax = 10.0;
        private const long MsProTag = 86_400_000L;

        public sealed class Def
        {
            public string Id, Laden;
            public string[] Liebt, Mag;
            /// <summary>Discount in the keeper's shop from heart 2 on (0 = none).</summary>
            public int RabattProzent;
        }

        public static readonly Def[] Leute =
        {
            new Def { Id = "selma", Laden = RanchDorf.Reitladen, Liebt = new string[0], Mag = new string[0], RabattProzent = 5 },
            new Def { Id = "marta", Laden = RanchDorf.Futterhof, Liebt = new string[0], Mag = new[] { "apfel" }, RabattProzent = 5 },
            new Def { Id = "alwin", Laden = RanchDorf.Moebelscheune, Liebt = new[] { "heu" }, Mag = new[] { "apfel", "leckerli" }, RabattProzent = 0 },
            new Def { Id = "wilma", Laden = RanchDorf.Pferdehaendlerin, Liebt = new[] { "heu" }, Mag = new[] { "apfel", "karotte" }, RabattProzent = 5 },
            new Def { Id = "eisenhuf", Laden = RanchDorf.Schmiede, Liebt = new[] { "leckerli" }, Mag = new[] { "apfel" }, RabattProzent = 10 },
            // the people on the farm and out in the region (ranch_npcs.json; Godot's cookies, jam and berries are treats here)
            new Def { Id = "rosi", Liebt = new[] { "apfel" }, Mag = new[] { "karotte", "heu" } },
            new Def { Id = "moehrchen", Liebt = new[] { "karotte" }, Mag = new[] { "apfel" } },
            new Def { Id = "timmi", Liebt = new[] { "leckerli" }, Mag = new[] { "apfel" } },
            new Def { Id = "miez", Liebt = new[] { "leckerli" }, Mag = new string[0] },
            new Def { Id = "luna", Liebt = new[] { "hafer" }, Mag = new[] { "karotte" } },
            new Def { Id = "knips", Liebt = new[] { "apfel" }, Mag = new[] { "leckerli" } },
            new Def { Id = "punktabzug", Liebt = new[] { "leckerli" }, Mag = new string[0] },
            new Def { Id = "eilfried", Liebt = new[] { "leckerli" }, Mag = new[] { "apfel" } },
        };

        public static readonly string[] Geschenke = { "heu", "apfel", "karotte", "hafer", "leckerli" };

        public static Def Fuer(string id) => Array.Find(Leute, d => d.Id == id);

        public static Def VomLaden(string laden) => Array.Find(Leute, d => d.Laden == laden);

        public static int Herzen(double punkte)
        {
            var stufe = 0;
            for (var i = 0; i < HerzSchwellen.Length; i++) if (punkte >= HerzSchwellen[i]) stufe = i;
            return Math.Min(stufe, HerzMax);
        }

        public static double PunkteBisNaechste(double punkte)
        {
            var stufe = Herzen(punkte);
            return stufe >= HerzMax ? 0.0 : Math.Max(0.0, HerzSchwellen[stufe + 1] - punkte);
        }

        public static double PunkteNachVerfall(double punkte, long letzteMs, long nowMs)
        {
            if (letzteMs <= 0 || nowMs <= letzteMs) return punkte;
            var tage = (double)(nowMs - letzteMs) / MsProTag;
            if (tage <= KarenzTage) return punkte;
            var verfall = Math.Min((tage - KarenzTage) * VerfallProTag, VerfallMax);
            return Math.Max(HerzSchwellen[Herzen(punkte)], punkte - verfall);
        }

        public static JObject Freund(JObject state, string id)
        {
            var npc = J.EnsureDict(J.EnsureDict(state, RanchHof.SliceId), Key);
            if (!(npc[id ?? ""] is JObject f)) { f = new JObject { ["punkte"] = 0.0, ["letzteBegegnungAt"] = 0, ["geredetTag"] = "", ["geschenkTag"] = "" }; npc[id ?? ""] = f; }
            return f;
        }

        public static double Punkte(JObject state, string id) => J.NumOr(J.Path(state, RanchHof.SliceId, Key, id ?? "", "punkte"), 0.0);

        public static int HerzenVon(JObject state, string id) => Herzen(Punkte(state, id));

        private static void Frisch(JObject f, long nowMs)
        {
            f["punkte"] = PunkteNachVerfall(J.NumOr(f["punkte"], 0.0), J.Long(f["letzteBegegnungAt"]), nowMs);
            f["letzteBegegnungAt"] = nowMs;
        }

        /// <summary>A chat; returns the hearts before and after.</summary>
        public static (int vorher, int nachher) Reden(JObject state, string id, string tag, long nowMs)
        {
            var f = Freund(state, id);
            Frisch(f, nowMs);
            var vorher = Herzen(J.NumOr(f["punkte"], 0.0));
            var gewinn = J.Str(f["geredetTag"]) != (tag ?? "") ? RedenPunkte : RedenTrost;
            f["geredetTag"] = tag ?? "";
            f["punkte"] = Math.Max(0.0, Math.Min(100.0, J.NumOr(f["punkte"], 0.0) + gewinn));
            return (vorher, Herzen(J.NumOr(f["punkte"], 0.0)));
        }

        /// <summary>Friendship points from elsewhere (a quest's reward); returns the hearts after.</summary>
        public static int Gutschreiben(JObject state, string id, double punkte, long nowMs)
        {
            var f = Freund(state, id);
            Frisch(f, nowMs);
            f["punkte"] = Math.Max(0.0, Math.Min(100.0, J.NumOr(f["punkte"], 0.0) + Math.Max(0.0, punkte)));
            return Herzen(J.NumOr(f["punkte"], 0.0));
        }

        public static string Reaktion(Def def, string item) =>
            def == null ? "normal" : Array.IndexOf(def.Liebt, item) >= 0 ? "liebt" : Array.IndexOf(def.Mag, item) >= 0 ? "mag" : "normal";

        /// <summary>
        /// A gift from the ranch's stock (hay/apples from the farm store, carrots from the fridge, oats/treats from the
        /// village bag); null reaction = nothing of that left.
        /// </summary>
        public static (string reaktion, int vorher, int nachher) Schenken(JObject state, string id, string item, string tag, long nowMs)
        {
            if (Array.IndexOf(Geschenke, item) < 0 || RanchPflege.Vorrat(state, item) <= 0) return (null, 0, 0);
            switch (item)
            {
                case "heu":
                case "apfel":
                    var lager = RanchHof.Lager(state);
                    lager[item] = J.Int(lager[item]) - 1;
                    break;
                case "karotte":
                    var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                    food["carrot"] = J.Int(food["carrot"]) - 1;
                    break;
                default:
                    var futter = (JObject)RanchDorf.Im(state)["futter"];
                    futter[item] = J.Int(futter[item]) - 1;
                    break;
            }
            var f = Freund(state, id);
            Frisch(f, nowMs);
            var vorher = Herzen(J.NumOr(f["punkte"], 0.0));
            var def = Fuer(id);
            var reaktion = Reaktion(def, item);
            var gewinn = J.Str(f["geschenkTag"]) != (tag ?? "")
                ? reaktion == "liebt" ? GeschenkLiebt : reaktion == "mag" ? GeschenkMag : GeschenkNormal
                : GeschenkTrost;
            f["geschenkTag"] = tag ?? "";
            f["punkte"] = Math.Max(0.0, Math.Min(100.0, J.NumOr(f["punkte"], 0.0) + gewinn));
            return (reaktion, vorher, Herzen(J.NumOr(f["punkte"], 0.0)));
        }

        /// <summary>The discount (percent) the keeper of <paramref name="laden"/> gives right now.</summary>
        public static int Rabatt(JObject state, string laden)
        {
            var def = VomLaden(laden);
            return def != null && def.RabattProzent > 0 && HerzenVon(state, def.Id) >= 2 ? def.RabattProzent : 0;
        }

        public static int MitRabatt(int preis, int prozent) => prozent <= 0 ? preis : (int)Math.Round(preis * (100 - prozent) / 100.0, MidpointRounding.AwayFromZero);
    }
}
