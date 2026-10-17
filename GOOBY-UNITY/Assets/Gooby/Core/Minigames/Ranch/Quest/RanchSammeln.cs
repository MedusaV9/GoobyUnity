using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The things to collect out in the ranch region for the quests' "sammle" goals: horseshoes by the paths, flowers on
    /// the meadows, berries at the forest's edge and by the brook, boards and the rusty key at the old barn, wool by the
    /// farm's sheep. Where they lie is drawn anew each day from the date (deterministic, every device the same), on
    /// walkable ground off the water; what was picked up that day stays gone (<c>ranch.quests.aufgesammelt</c>).
    /// </summary>
    public static class RanchSammeln
    {
        public const double Radius = 3.5;

        public sealed class Stelle
        {
            public string Id, Item;
            public double X, Z;
        }

        private static readonly (string item, int n, string[] zonen)[] Plan =
        {
            ("hufeisen", 7, new[] { "hof", "weidetal", "turnierplatz", "huegelkamm" }),
            ("blume", 8, new[] { "blumenwiese", "weidetal", "hof" }),
            ("beere", 6, new[] { "waeldchen", "bachlauf" }),
            ("brett", 4, new[] { "scheune_alt" }),
            ("schluessel", 1, new[] { "scheune_alt" }),
            ("wolle", 5, new[] { "hof" }),
        };

        /// <summary>The day's spots (the same list for the same date).</summary>
        public static List<Stelle> FuerTag(string tag)
        {
            var rng = new GodotRng(GodotRng.StringHash("sammeln|" + (tag ?? "")));
            var out_ = new List<Stelle>();
            foreach (var (item, n, zonen) in Plan)
            {
                for (var i = 0; i < n; i++)
                {
                    var zone = RanchKarte.ZoneVon(zonen[i % zonen.Length]);
                    if (zone == null) continue;
                    var (x, z) = Punkt(item, zone, rng);
                    out_.Add(new Stelle { Id = item + "_" + i, Item = item, X = x, Z = z });
                }
            }
            return out_;
        }

        private static (double x, double z) Punkt(string item, RanchKarte.Zone zone, GodotRng rng)
        {
            for (var versuch = 0; versuch < 40; versuch++)
            {
                double x, z;
                if (item == "hufeisen")
                {
                    // by a path through the zone, a few metres off its edge
                    var wege = RanchKarte.Wege.Where(w => w.Von == zone.Id || w.Nach == zone.Id).ToList();
                    var w = wege[rng.RandIRange(0, wege.Count - 1)];
                    var k = rng.RandIRange(0, w.Punkte.Count - 2);
                    var t = rng.RandF();
                    var a = w.Punkte[k];
                    var b = w.Punkte[k + 1];
                    var len = Math.Max(0.01, a.Abstand(b));
                    var seite = rng.RandF() < 0.5 ? -1.0 : 1.0;
                    var ab = w.Breite * 0.5 + 2.0;
                    x = a.X + (b.X - a.X) * t - (b.Z - a.Z) / len * ab * seite;
                    z = a.Z + (b.Z - a.Z) * t + (b.X - a.X) / len * ab * seite;
                }
                else if (item == "brett" || item == "schluessel")
                {
                    var s = zone.Punkt("scheune");
                    var winkel = rng.RandF() * Math.PI * 2.0;
                    var r = item == "schluessel" ? 9.0 : 12.0 + rng.RandF() * 10.0;
                    x = s.X + Math.Cos(winkel) * r;
                    z = s.Z + Math.Sin(winkel) * r;
                }
                else if (item == "wolle")
                {
                    // by the farm's sheep pasture
                    x = 82.0 + rng.RandF() * 34.0;
                    z = 136.0 + rng.RandF() * 26.0;
                }
                else
                {
                    x = zone.Rect.X + 20.0 + rng.RandF() * (zone.Rect.W - 40.0);
                    z = zone.Rect.Z + 20.0 + rng.RandF() * (zone.Rect.D - 40.0);
                }
                if (RanchKarte.IstBegehbar(x, z) && !RanchGelaende.IstWasser(x, z) && RanchGelaende.Normale(x, z).y > 0.8) return (x, z);
            }
            return (zone.Spawn.X, zone.Spawn.Z + 4.0);
        }

        /// <summary>Spots already picked up today.</summary>
        public static List<string> Aufgesammelt(JObject state, string tag)
        {
            var a = J.Path(state, RanchHof.SliceId, RanchQuests.Key, "aufgesammelt") as JObject;
            return a != null && J.Str(a["tag"]) == tag ? (a["ids"] as JArray ?? new JArray()).Select(t => J.Str(t)).ToList() : new List<string>();
        }

        /// <summary>Picks a spot up (once a day): the item goes to the quest bag and counts for "sammle". Null = gone already.</summary>
        public static List<string> Aufsammeln(JObject state, string tag, Stelle stelle, long nowMs)
        {
            var slice = RanchQuests.Slice(state);
            var a = slice["aufgesammelt"] as JObject;
            if (a == null || J.Str(a["tag"]) != tag) slice["aufgesammelt"] = a = new JObject { ["tag"] = tag, ["ids"] = new JArray() };
            var ids = (JArray)a["ids"];
            if (ids.Any(t => J.Str(t) == stelle.Id)) return null;
            ids.Add(stelle.Id);
            return RanchQuests.Melde(state, "sammle", stelle.Item, nowMs);
        }
    }
}
