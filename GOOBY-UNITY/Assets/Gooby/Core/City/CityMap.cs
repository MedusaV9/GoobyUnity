using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// The authored town plan of Sonnental (city/city_map.json, Doc E §1.2): the places (`orte`) with their kind,
    /// district, tiles and facade (tint, awning). The lattice itself lives in <see cref="CityGrid"/>; DriveModel puts
    /// every place at its plan tile with a parking spot on its street tile, CityScene dresses the facades from it.
    /// </summary>
    public static class CityMap
    {
        public const string Path = "city/city_map";

        public sealed class Ort
        {
            public string Id, NameKey, Typ, Distrikt, Glb, Tint;
            public bool Awning;
            public int[] Tile, Strasse;             // first tile (r, c) and the street tile in front of the door
            public List<int[]> Tiles = new List<int[]>();   // every tile the place occupies
            public Oeffnung Oeffnung;   // null = always open
        }

        /// <summary>Opening rule (optional per place): weekdays 0 = Sunday … 6 = Saturday, hours [von, bis).</summary>
        public sealed class Oeffnung
        {
            public int[] Wochentage = new int[0];
            public double VonStunde = 0.0, BisStunde = 24.0;

            public bool OffenAn(int wochentag, double stunde)
            {
                if (Wochentage.Length > 0 && System.Array.IndexOf(Wochentage, wochentag) < 0) return false;
                return stunde >= VonStunde && stunde < BisStunde;
            }

            public bool NurSamstag => Wochentage.Length == 1 && Wochentage[0] == 6;
        }

        public const int Samstag = 6;

        /// <summary>Open right now? Places without a rule are always open.</summary>
        public static bool IstOffen(Ort ort, int wochentag, double stunde) => ort?.Oeffnung == null || ort.Oeffnung.OffenAn(wochentag, stunde);

        /// <summary>Local weekday (0 = Sunday) and hour-of-day with fraction for an epoch-ms and a UTC bias.</summary>
        public static (int wochentag, double stunde) LokaleZeit(long nowMs, int biasMinutes)
        {
            var secs = (long)(nowMs / 1000.0) + biasMinutes * 60L;
            var d = System.DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
            return ((int)d.DayOfWeek, d.Hour + d.Minute / 60.0);
        }

        /// <summary>"Why closed?" text key for the street pill (city.ort.nur_samstag for the market).</summary>
        public static string GeschlossenKey(Ort ort) => ort?.Oeffnung != null && ort.Oeffnung.NurSamstag ? "city.ort.nur_samstag" : "city.ort.geschlossen";

        public static int TageBisSamstag(int wochentag) => ((Samstag - wochentag) % 7 + 7) % 7;

        /// <summary>
        /// Places one can walk into while closed: the market square is a public square — Greta only buys on Saturday
        /// mornings, but the player's own stall can be stocked any day for the next market.
        /// </summary>
        public static bool ImmerBetretbar(string mapId) => mapId == "wochenmarkt";

        private static List<Ort> _orte;

        public static IReadOnlyList<Ort> Orte
        {
            get
            {
                if (_orte != null) return _orte;
                var list = new List<Ort>();
                if (!ContentSource.Installed) return list; // not cached: the plan is read once content is wired up
                var map = ContentSource.TryLoadObject(Path);
                foreach (var t in map?["orte"] as JArray ?? new JArray())
                {
                    if (!(t is JObject o) || J.Str(o["id"]).Length == 0) continue;
                    var fassade = o["fassade"] as JObject;
                    list.Add(new Ort
                    {
                        Id = J.Str(o["id"]),
                        NameKey = J.Str(o["name_key"], "city.ort." + J.Str(o["id"])),
                        Typ = J.Str(o["typ"], "laden"),
                        Distrikt = J.Str(o["distrikt"]),
                        Glb = J.Str(fassade?["glb"]),
                        Tint = J.Str(fassade?["tint"]),
                        Awning = J.IsTrue(fassade?["awning"]),
                        Tile = Ints(o["tiles"] is JArray tiles && tiles.Count > 0 ? tiles[0] : null),
                        Tiles = TileList(o["tiles"]),
                        Strasse = Ints(o["strasse"]),
                        Oeffnung = o["oeffnung"] is JObject oe
                            ? new Oeffnung { Wochentage = Ints(oe["wochentage"]), VonStunde = J.NumOr(oe["von_stunde"], 0.0), BisStunde = J.NumOr(oe["bis_stunde"], 24.0) }
                            : null,
                    });
                }
                _orte = list;
                return _orte;
            }
        }

        public static Ort Get(string id)
        {
            foreach (var o in Orte) if (o.Id == id) return o;
            return null;
        }

        /// <summary>Forget the cached plan (tests swap the content source).</summary>
        public static void Reset() => _orte = null;

        private static int[] Ints(JToken t)
        {
            if (!(t is JArray a)) return new int[0];
            var outp = new int[a.Count];
            for (var i = 0; i < a.Count; i++) outp[i] = J.Int(a[i]);
            return outp;
        }

        private static List<int[]> TileList(JToken t)
        {
            var outp = new List<int[]>();
            foreach (var tile in t as JArray ?? new JArray())
            {
                var pair = Ints(tile);
                if (pair.Length >= 2) outp.Add(pair);
            }
            return outp;
        }
    }
}
