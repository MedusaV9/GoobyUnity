using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// Three ranch tasks a day (port of rquest_katalog.gd's tagesaufgaben with the C5 rule: no streak, no decay — whoever
    /// stays away misses nothing). Godot's daily pool needs the open world for half its tasks (lake ride, photo spot, NPC
    /// chats); Unity keeps mucking out, apples and brushing and adds what the farm and Hufingen offer: feeding, mowing, a
    /// visit to the village and a ranch game. The day's three are drawn like Godot (Godot's RNG seeded with the date's
    /// hash, pop_at). Progress lives in <c>ranch.tage</c> and resets with the day; the ranch games count from the save's
    /// play counters since the day began.
    /// </summary>
    public static class RanchTage
    {
        public const string Key = "tage";
        public const int Slots = 3;
        private const string Reason = "ranch_tage";

        public sealed class Aufgabe
        {
            public string Id, Ereignis, Icon;
            public int Ziel, Muenzen;
        }

        public sealed class Stand
        {
            public Aufgabe Aufgabe;
            public int Fortschritt;
            public bool Fertig, Abgeholt;
        }

        public static readonly Aufgabe[] Pool =
        {
            new Aufgabe { Id = "tages_ausmisten", Ereignis = "pflege:ausmisten", Ziel = 1, Muenzen = 40, Icon = "pitchfork" },
            new Aufgabe { Id = "tages_aepfel", Ereignis = "sammle:apfel", Ziel = 2, Muenzen = 40, Icon = "food_apple" },
            new Aufgabe { Id = "tages_striegeln", Ereignis = "pflege:striegeln", Ziel = 1, Muenzen = 40, Icon = "brush" },
            new Aufgabe { Id = "tages_fuettern", Ereignis = "pflege:fuettern", Ziel = 3, Muenzen = 40, Icon = "hay" },
            new Aufgabe { Id = "tages_heu", Ereignis = "sammle:heu", Ziel = 3, Muenzen = 40, Icon = "sprout" },
            new Aufgabe { Id = "tages_dorf", Ereignis = "dorf", Ziel = 1, Muenzen = 50, Icon = "map" },
            new Aufgabe { Id = "tages_reiten", Ereignis = "reiten", Ziel = 1, Muenzen = 50, Icon = "horseshoe" },
        };

        /// <summary>The ranch games whose plays count for "reiten".</summary>
        public static readonly string[] RanchSpiele = { "ranchTurnier", "ranchTonnen", "ranchZeit", "ranchParcours", "ranchHerde" };

        /// <summary>The day's tasks (tagesaufgaben): Godot's RNG seeded with the date's hash, Slots × pop_at(randi_range).</summary>
        public static List<Aufgabe> FuerTag(string datum)
        {
            var pool = Pool.ToList();
            if (pool.Count <= Slots) return pool;
            var rng = new GodotRng(GodotRng.StringHash(datum ?? ""));
            var out_ = new List<Aufgabe>();
            for (var i = 0; i < Slots; i++)
            {
                var k = rng.RandIRange(0, pool.Count - 1);
                out_.Add(pool[k]);
                pool.RemoveAt(k);
            }
            return out_;
        }

        public static int RanchSpielePlays(JObject state) =>
            RanchSpiele.Sum(id => (int)J.NumOr(J.Path(state, "minigames", "plays", id), 0.0));

        /// <summary><c>ranch.tage</c> for <paramref name="tag"/> — a new day starts fresh (and remembers the game counter).</summary>
        public static JObject Im(JObject state, string tag)
        {
            var ranch = J.EnsureDict(state, RanchHof.SliceId);
            var t = ranch[Key] as JObject;
            if (t == null || J.Str(t["tag"]) != (tag ?? ""))
            {
                t = new JObject { ["tag"] = tag ?? "", ["stand"] = new JObject(), ["abgeholt"] = new JArray(), ["reitenBasis"] = RanchSpielePlays(state) };
                ranch[Key] = t;
            }
            if (!(t["stand"] is JObject)) t["stand"] = new JObject();
            if (!(t["abgeholt"] is JArray)) t["abgeholt"] = new JArray();
            return t;
        }

        /// <summary>Today's three with their progress (read-only: a save from yesterday reads as a fresh day).</summary>
        public static List<Stand> Heute(JObject state, string tag)
        {
            var t = J.Path(state, RanchHof.SliceId, Key) as JObject;
            var heute = t != null && J.Str(t["tag"]) == (tag ?? "");
            var reiten = heute ? Math.Max(0, RanchSpielePlays(state) - J.Int(t["reitenBasis"])) : 0;
            return FuerTag(tag).Select(a =>
            {
                var n = a.Ereignis == "reiten" ? reiten : heute ? J.Int(J.Path(t, "stand", a.Id)) : 0;
                return new Stand
                {
                    Aufgabe = a,
                    Fortschritt = Math.Min(n, a.Ziel),
                    Fertig = n >= a.Ziel,
                    Abgeholt = heute && ((JArray)t["abgeholt"]).Any(x => J.Str(x) == a.Id),
                };
            }).ToList();
        }

        /// <summary>Something happened on the ranch; returns the ids of today's tasks this finished.</summary>
        public static List<string> Melde(JObject state, string tag, string ereignis, int n = 1)
        {
            var fertig = new List<string>();
            if (state == null || n <= 0 || string.IsNullOrEmpty(ereignis)) return fertig;
            var vorher = Heute(state, tag).ToDictionary(s => s.Aufgabe.Id, s => s.Fertig);
            var t = Im(state, tag);
            var stand = (JObject)t["stand"];
            foreach (var a in FuerTag(tag))
            {
                if (a.Ereignis != ereignis || a.Ereignis == "reiten") continue;
                stand[a.Id] = Math.Min(a.Ziel, J.Int(stand[a.Id]) + n);
            }
            foreach (var s in Heute(state, tag))
                if (s.Fertig && !vorher[s.Aufgabe.Id]) fertig.Add(s.Aufgabe.Id);
            return fertig;
        }

        /// <summary>Collects a finished task's coins once; returns the coins (0 = not finished, already taken or unknown).</summary>
        public static int Abholen(JObject state, string tag, string id)
        {
            var s = Heute(state, tag).FirstOrDefault(x => x.Aufgabe.Id == id);
            if (s == null || !s.Fertig || s.Abgeholt) return 0;
            var t = Im(state, tag);
            ((JArray)t["abgeholt"]).Add(id);
            Economy.Award(J.EnsureDict(state, "economy"), s.Aufgabe.Muenzen, Reason);
            return s.Aufgabe.Muenzen;
        }

        public static int OffenAbzuholen(JObject state, string tag) => Heute(state, tag).Count(s => s.Fertig && !s.Abgeholt);
    }
}
