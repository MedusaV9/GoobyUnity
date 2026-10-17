using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// Funkelpark (port of park/park_state.gd + the ride/stall bookings of funkelpark.gd): rides cost coins and a
    /// little energy and give fun; the snack alley sells fair food into the tray. Counters live in the `park`
    /// slice (visits, nightVisit, rides, candyBought) for the sticker page and Gooby's memories.
    /// </summary>
    public static class ParkLogic
    {
        public const double RideEnergie = 2.0;
        public const double RideSpass = 12.0;
        public const int MaxCount = 99999;
        public const double NachtAb = 19.0, NachtBis = 6.0;

        public sealed class Ride
        {
            public string Id, NameKey, TextKey;
            public int Preis;        // 0 = watching only (Autoscooter)
            public bool Zaehlt;      // has a counter in the save (web PARK_RIDE_IDS: coaster, wheel)
        }

        public sealed class Stall
        {
            public string Id, Tint;
            public int Preis;
        }

        public static readonly Ride[] Rides =
        {
            new Ride { Id = "coaster", NameKey = "park.coaster.name", TextKey = "park.coaster.beschreibung", Preis = 15, Zaehlt = true },
            new Ride { Id = "wheel", NameKey = "park.wheel.name", TextKey = "park.wheel.beschreibung", Preis = 10, Zaehlt = true },
            new Ride { Id = "karussell", NameKey = "park.karussell.name", TextKey = "park.karussell.beschreibung", Preis = 5 },
            new Ride { Id = "scooter", NameKey = "park.scooter.name", TextKey = "park.scooter.beschreibung", Preis = 0 },
        };

        public static readonly Stall[] Stalls =
        {
            new Stall { Id = "cottonCandy", Preis = 12, Tint = "#F781B0" },
            new Stall { Id = "softServe", Preis = 18, Tint = "#9BD7E8" },
            new Stall { Id = "waffle", Preis = 20, Tint = "#F5C518" },
        };

        public static Ride RideById(string id)
        {
            foreach (var r in Rides) if (r.Id == id) return r;
            return null;
        }

        public static bool IstNacht(double stunde) => stunde >= NachtAb || stunde < NachtBis;

        /// <summary>Normalized park slice (counters clamped, rides whitelisted).</summary>
        public static JObject SliceOf(JObject state)
        {
            var d = new JObject { ["visits"] = 0, ["nightVisit"] = false, ["rides"] = new JObject { ["coaster"] = 0, ["wheel"] = 0 }, ["handsUp"] = 0, ["candyBought"] = 0 };
            if (!(state?["park"] is JObject raw)) return d;
            d["visits"] = Clamp(raw["visits"]);
            d["nightVisit"] = J.IsTrue(raw["nightVisit"]);
            var rides = raw["rides"] as JObject;
            foreach (var r in Rides) if (r.Zaehlt) d["rides"][r.Id] = Clamp(rides?[r.Id]);
            d["handsUp"] = Clamp(raw["handsUp"]);
            d["candyBought"] = Clamp(raw["candyBought"]);
            return d;
        }

        private static int Clamp(JToken v) => J.ClampI(J.Int(v), 0, MaxCount);

        /// <summary>A plaza visit (entering the park): visits+1, night latches nightVisit.</summary>
        public static void BesuchBuchen(GameStateStore gs, bool nacht)
        {
            if (gs == null) return;
            gs.Update(state =>
            {
                var s = SliceOf(state);
                s["visits"] = Math.Min(MaxCount, J.Int(s["visits"]) + 1);
                if (nacht) s["nightVisit"] = true;
                state["park"] = s;
            });
            gs.NotifySliceChanged("park");
        }

        public static int Fahrten(GameStateStore gs, string rideId) => J.Int(SliceOf(gs?.State)["rides"][rideId]);

        public static bool KannFahren(GameStateStore gs, Ride ride) =>
            gs != null && ride != null && ShopLogic.Coins(gs) >= ride.Preis && !Stats.IsExhausted(J.Dict(J.Path(gs.State, "gooby", "stats")));

        /// <summary>A ride: coins out, a little energy out, fun in, counter up. "coins" / "tired" as the reason when refused.</summary>
        public static (bool ok, string reason) Fahren(GameStateStore gs, Ride ride)
        {
            if (gs == null || ride == null) return (false, "unknown");
            if (ShopLogic.Coins(gs) < ride.Preis) return (false, "coins");
            if (Stats.IsExhausted(J.Dict(J.Path(gs.State, "gooby", "stats")))) return (false, "tired");
            gs.Update(state =>
            {
                if (ride.Preis > 0 && !Economy.Spend(J.EnsureDict(state, "economy"), ride.Preis, "funkelpark")) return;
                var gooby = J.EnsureDict(state, "gooby");
                if (ride.Preis > 0)
                    gooby["stats"] = Stats.ApplyDeltas(J.Dict(gooby["stats"]), new Dictionary<string, double> { ["energy"] = -RideEnergie, ["fun"] = RideSpass });
                var s = SliceOf(state);
                if (ride.Zaehlt) s["rides"][ride.Id] = Math.Min(MaxCount, J.Int(s["rides"][ride.Id]) + 1);
                state["park"] = s;
            });
            gs.NotifySliceChanged("economy");
            gs.NotifySliceChanged("gooby");
            gs.NotifySliceChanged("park");
            return (true, "");
        }

        /// <summary>Snack alley: fair food into inventory.food, candyBought+1.</summary>
        public static bool Naschen(GameStateStore gs, Stall stall)
        {
            if (gs == null || stall == null || ShopLogic.Coins(gs) < stall.Preis) return false;
            var ok = false;
            gs.Update(state =>
            {
                if (!Economy.Spend(J.EnsureDict(state, "economy"), stall.Preis, "naschgasse")) return;
                var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                food[stall.Id] = J.Int(food[stall.Id]) + 1;
                var s = SliceOf(state);
                s["candyBought"] = Math.Min(MaxCount, J.Int(s["candyBought"]) + 1);
                state["park"] = s;
                ok = true;
            });
            if (ok) { gs.NotifySliceChanged("economy"); gs.NotifySliceChanged("inventory"); gs.NotifySliceChanged("park"); }
            return ok;
        }
    }
}
