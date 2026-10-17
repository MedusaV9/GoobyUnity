using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// Autohaus Blechbert (port of auto_katalog.gd): the car catalog (city/cars.json — one owning list for the
    /// dealer and the street), ownership with the chosen paint (save: city.autos = {id: "#hex"}) and the car in
    /// use (city.aktivesAuto). The start sedan always belongs to the player. Stats 1..10 (3 = neutral) scale the
    /// cruise speed of the DriveModel a little.
    /// </summary>
    public static class AutoKatalog
    {
        public sealed class Auto
        {
            public string Id, NameDe, Spruch;
            public int Preis;
            public bool Start;
            public List<string> Farben = new List<string>();
            public int Speed, Handling, Boost;
        }

        private static JObject Daten(string path) => ContentSource.LoadObject(path);

        public static List<Auto> Autos(string path = ShopLogic.CarsPath)
        {
            var outp = new List<Auto>();
            foreach (var t in Daten(path)["autos"] as JArray ?? new JArray())
            {
                if (!(t is JObject o) || J.Str(o["id"]).Length == 0) continue;
                var a = new Auto
                {
                    Id = J.Str(o["id"]), NameDe = J.Str(o["name_de"], J.Str(o["id"])), Spruch = J.Str(o["spruch_de"]),
                    Preis = J.Int(o["preis"]), Start = J.IsTrue(o["start"]),
                    Speed = J.Int(J.Path(o, "stats", "speed"), 3), Handling = J.Int(J.Path(o, "stats", "handling"), 3), Boost = J.Int(J.Path(o, "stats", "boost"), 3),
                };
                foreach (var f in o["farben"] as JArray ?? new JArray()) a.Farben.Add(J.Str(f));
                outp.Add(a);
            }
            return outp;
        }

        public static Auto Get(string id, string path = ShopLogic.CarsPath)
        {
            foreach (var a in Autos(path)) if (a.Id == id) return a;
            return null;
        }

        public static string StartAutoId(string path = ShopLogic.CarsPath)
        {
            foreach (var a in Autos(path)) if (a.Start) return a.Id;
            var all = Autos(path);
            return all.Count > 0 ? all[0].Id : "";
        }

        /// <summary>Display name of a paint (#hex → "Möhrenrot"), the hex itself when unknown.</summary>
        public static string FarbName(string hex, string path = ShopLogic.CarsPath) => J.Str(J.Path(Daten(path), "farben_namen", hex), hex);

        /// <summary>Owned cars with their paint, the start car always included in its first colour.</summary>
        public static Dictionary<string, string> Besitz(GameStateStore gs, string path = ShopLogic.CarsPath)
        {
            var outp = new Dictionary<string, string>();
            var start = Get(StartAutoId(path), path);
            if (start != null) outp[start.Id] = start.Farben.Count > 0 ? start.Farben[0] : "#FFF4E6";
            if (J.Path(gs?.State, "city", "autos") is JObject eigene)
                foreach (var kv in eigene)
                    if (Get(kv.Key, path) != null) outp[kv.Key] = J.Str(kv.Value, outp.TryGetValue(kv.Key, out var f) ? f : "#FFF4E6");
            return outp;
        }

        public static bool Besitzt(GameStateStore gs, string id, string path = ShopLogic.CarsPath) => Besitz(gs, path).ContainsKey(id);

        /// <summary>The car in use with its paint — falls back to the start car when the chosen one is unknown.</summary>
        public static (Auto auto, string farbe) AktivesAuto(GameStateStore gs, string path = ShopLogic.CarsPath)
        {
            var besitz = Besitz(gs, path);
            var gewaehlt = J.Str(J.Path(gs?.State, "city", "aktivesAuto"));
            if (gewaehlt.Length == 0 || !besitz.ContainsKey(gewaehlt)) gewaehlt = StartAutoId(path);
            var auto = Get(gewaehlt, path);
            return (auto, besitz.TryGetValue(gewaehlt, out var f) ? f : "#FFF4E6");
        }

        public static bool KannKaufen(GameStateStore gs, string id, string farbe, string path = ShopLogic.CarsPath)
        {
            var a = Get(id, path);
            if (gs == null || a == null || Besitzt(gs, id, path) || !a.Farben.Contains(farbe)) return false;
            return ShopLogic.Coins(gs) >= a.Preis;
        }

        /// <summary>Buys the car in a paint and drives off in it.</summary>
        public static bool Kaufe(GameStateStore gs, string id, string farbe, string path = ShopLogic.CarsPath)
        {
            if (!KannKaufen(gs, id, farbe, path)) return false;
            var a = Get(id, path);
            var ok = false;
            gs.Update(state =>
            {
                if (!Economy.Spend(J.EnsureDict(state, "economy"), a.Preis, "autohaus")) return;
                var city = J.EnsureDict(state, "city");
                J.EnsureDict(city, "autos")[id] = farbe;
                city["aktivesAuto"] = id;
                ok = true;
            });
            if (ok) { gs.NotifySliceChanged("economy"); gs.NotifySliceChanged("city"); }
            return ok;
        }

        /// <summary>Repaints an owned car (free — Benzino paints while you watch).</summary>
        public static bool Lackieren(GameStateStore gs, string id, string farbe, string path = ShopLogic.CarsPath)
        {
            var a = Get(id, path);
            if (gs == null || a == null || !Besitzt(gs, id, path) || !a.Farben.Contains(farbe)) return false;
            gs.Update(state => J.EnsureDict(J.EnsureDict(state, "city"), "autos")[id] = farbe);
            gs.NotifySliceChanged("city");
            return true;
        }

        /// <summary>Takes an owned car out of the driveway.</summary>
        public static bool Waehle(GameStateStore gs, string id, string path = ShopLogic.CarsPath)
        {
            if (gs == null || !Besitzt(gs, id, path)) return false;
            gs.Update(state => J.EnsureDict(state, "city")["aktivesAuto"] = id);
            gs.NotifySliceChanged("city");
            return true;
        }

        /// <summary>Cruise-speed multiplier of a speed stat: 3 → 1.0, 6 → 1.3 (+10 % per point, clamped 0.8..1.7).</summary>
        public static double TempoFaktor(int speed) => Math.Max(0.8, Math.Min(1.7, 1.0 + (speed - 3) * 0.1));
    }
}
