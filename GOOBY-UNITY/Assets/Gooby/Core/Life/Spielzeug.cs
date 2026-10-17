using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Gooby's toy box: the POW! toys that are not furniture (<see cref="City.PowLogic.DekoMoebel"/>) stay in
    /// inventory.items and he plays with them at home — a short scene on the stage, then the play is booked here. A toy
    /// gives its full fun the first time a day and a third after that (the same toy again is less exciting); the cracker
    /// is used up. Playing costs a little energy, the water pistol leaves him a bit cleaner. Asleep, or already as happy
    /// as he gets, he says no. State: gooby.spielzeug.heute {toy: local day}, achievements.counters.toyPlays.
    /// </summary>
    public static class Spielzeug
    {
        public const string Ok = "ok", Keins = "keins", Schlaeft = "schlaeft", GutGelaunt = "gut_gelaunt", Unbekannt = "unbekannt";
        public const double EnergieKosten = 2.0, SpassVoll = 97.0;

        public sealed class Def
        {
            /// <summary>Ware id of the POW! shelf and key in inventory.items.</summary>
            public string Id;
            /// <summary>The stage sequence (SimpleWorld.Toys).</summary>
            public string Szene;
            public double Spass, Sauber;
            public bool Verbrauch;
        }

        /// <summary>The toy box in shelf order (cheap to dear).</summary>
        public static readonly IReadOnlyList<Def> Alle = new[]
        {
            new Def { Id = "flummi", Szene = "huepfen", Spass = 14 },
            new Def { Id = "knallbonbon", Szene = "knall", Spass = 24, Verbrauch = true },
            new Def { Id = "quietschhammer", Szene = "quietschen", Spass = 16 },
            new Def { Id = "stickerheft", Szene = "sticker", Spass = 15 },
            new Def { Id = "gummiente", Szene = "ente", Spass = 18 },
            new Def { Id = "riesenpuzzle", Szene = "puzzle", Spass = 20 },
            new Def { Id = "wasserpistole", Szene = "spritzen", Spass = 22, Sauber = 6 },
        };

        public static Def DefVon(string id)
        {
            foreach (var d in Alle)
                if (d.Id == id) return d;
            return null;
        }

        public static int Anzahl(JObject state, string id) => J.Int(J.Path(state, "inventory", "items", id));

        /// <summary>What is in the box: the toys he owns, in shelf order.</summary>
        public static List<(Def Def, int Anzahl)> Kiste(JObject state)
        {
            var outp = new List<(Def, int)>();
            foreach (var d in Alle)
            {
                var n = Anzahl(state, d.Id);
                if (n > 0) outp.Add((d, n));
            }
            return outp;
        }

        public static bool HeuteGespielt(JObject state, string id, string tag) =>
            !string.IsNullOrEmpty(tag) && J.Str(J.Path(state, "gooby", "spielzeug", "heute", id)) == tag;

        /// <summary>The fun a toy brings now: full the first time today, a third after.</summary>
        public static double SpassFuer(JObject state, Def def, string tag) =>
            HeuteGespielt(state, def.Id, tag) ? Math.Round(def.Spass / 3.0) : def.Spass;

        /// <summary>Why not right now (null = he can play): nothing of it, asleep, already as happy as he gets.</summary>
        public static string Ablehnung(JObject state, string id)
        {
            if (DefVon(id) == null) return Unbekannt;
            if (Anzahl(state, id) <= 0) return Keins;
            if (Sleep.IsSleeping(Sleep.FlatOf(state))) return Schlaeft;
            if (J.NumOr(J.Path(state, "gooby", "stats", "fun"), 50.0) >= SpassVoll) return GutGelaunt;
            return null;
        }

        public sealed class Ergebnis
        {
            public string Grund = Ok;
            public double Spass;
            /// <summary>The first play with this toy today.</summary>
            public bool Frisch;
            /// <summary>How many of the toy are left (the cracker counts down).</summary>
            public int Rest;
        }

        /// <summary>Books a play at the end of its scene: fun, energy, maybe hygiene, the cracker used up, today's mark.</summary>
        public static Ergebnis Spielen(JObject state, string id, string tag)
        {
            var nein = Ablehnung(state, id);
            if (nein != null) return new Ergebnis { Grund = nein };
            var def = DefVon(id);
            var frisch = !HeuteGespielt(state, id, tag);
            var spass = SpassFuer(state, def, tag);
            var gooby = J.EnsureDict(state, "gooby");
            var deltas = new Dictionary<string, double> { ["fun"] = spass, ["energy"] = -EnergieKosten };
            if (def.Sauber > 0) deltas["hygiene"] = def.Sauber;
            gooby["stats"] = Stats.ApplyDeltas(J.Dict(gooby["stats"]), deltas);
            if (!string.IsNullOrEmpty(tag)) J.EnsureDict(J.EnsureDict(gooby, "spielzeug"), "heute")[id] = tag;
            var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
            var rest = J.Int(items[id]);
            if (def.Verbrauch)
            {
                rest--;
                if (rest > 0) items[id] = rest;
                else items.Remove(id);
            }
            var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
            counters["toyPlays"] = J.Int(counters["toyPlays"]) + 1;
            return new Ergebnis { Spass = spass, Frisch = frisch, Rest = Math.Max(0, rest) };
        }
    }
}
