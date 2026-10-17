using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// The airport counter (port of city/travel/reise_logic.gd over the Vacation slice): confirm a destination,
    /// book it (coins out, phase away with the timestamps), and pick Gooby up when he is back (souvenir coins in,
    /// postcards counted, destination stamped in the pass, recovery boost, Weltengooby latch on 9/9).
    /// </summary>
    public static class Travel
    {
        /// <summary>Destination order of the flap board (reise_logic.gd ZIELE).</summary>
        public static readonly string[] Ziele = { "beach", "harbor", "meadowTrip", "spookGarden", "bakery", "bigCity", "nightSky", "toyRoom", "space" };

        public sealed class Bestaetigung
        {
            public string ZielId, NameKey;
            public int Preis, Tage, SouvenirCoins;
            public bool KannZahlen;
        }

        public sealed class Rueckkehr
        {
            public bool Ok, WeltengoobyNeu;
            public string ZielId = "";
            public int SouvenirCoins, Postkarten;
        }

        public static Bestaetigung Info(string zielId, int coins)
        {
            if (!Vacation.TryGetDestination(zielId, out var d)) return null;
            return new Bestaetigung { ZielId = zielId, NameKey = "travel.ziel." + zielId, Preis = d.Price, Tage = d.Days, SouvenirCoins = d.SouvenirCoins, KannZahlen = coins >= d.Price };
        }

        public static string Phase(GameStateStore gs, long nowMs) => Vacation.PhaseAt(Vacation.SliceOf(gs?.State), nowMs);

        public static bool IstWeg(GameStateStore gs) => Vacation.IsAway(gs?.State);

        /// <summary>Days (rounded up) until Gooby is back; 0 when he is home or waiting to be picked up.</summary>
        public static int RestTage(GameStateStore gs, long nowMs)
        {
            var v = Vacation.SliceOf(gs?.State);
            if (Vacation.PhaseAt(v, nowMs) != Vacation.PhaseAway) return 0;
            return (int)Math.Ceiling(Math.Max(0.0, J.Num(v["returnAt"]) - nowMs) / Vacation.MsPerDay);
        }

        /// <summary>Books a trip: pays the price, sets phase away with return and pickup timestamps (trips/archive/visited stay).</summary>
        public static bool Buchen(GameStateStore gs, string zielId, long nowMs)
        {
            if (gs == null || !Vacation.TryGetDestination(zielId, out var d)) return false;
            var v = Vacation.SliceOf(gs.State);
            if (J.Str(v["phase"]) != Vacation.PhaseNone) return false;
            var ok = false;
            gs.Update(state =>
            {
                if (!Economy.Spend(J.EnsureDict(state, "economy"), d.Price, "reise")) return;
                var slice = Vacation.SliceOf(state);
                var returnAt = nowMs + d.Days * Vacation.MsPerDay;
                slice["phase"] = Vacation.PhaseAway;
                slice["destId"] = zielId;
                slice["bookedAt"] = nowMs;
                slice["returnAt"] = returnAt;
                slice["pickupBy"] = returnAt + Vacation.PickupWindowMs;
                slice["postcards"] = 0;
                state["vacation"] = slice;
                ok = true;
            });
            if (ok) { gs.NotifySliceChanged("economy"); gs.NotifySliceChanged("vacation"); }
            return ok;
        }

        /// <summary>Picks Gooby up (returnReady or overdue): souvenir coins, postcards, pass stamp, boost, trips+1 → home.</summary>
        public static Rueckkehr Abholen(GameStateStore gs, long nowMs)
        {
            var result = new Rueckkehr();
            if (gs == null) return result;
            var v = Vacation.SliceOf(gs.State);
            var phase = Vacation.PhaseAt(v, nowMs);
            if (phase != Vacation.PhaseReturnReady && phase != Vacation.PhaseOverdue) return result;
            var zielId = J.Str(v["destId"]);
            Vacation.TryGetDestination(zielId, out var d);
            result.Postkarten = Vacation.PostcardsDue(v, nowMs);
            result.SouvenirCoins = d.SouvenirCoins;
            result.ZielId = zielId;
            gs.Update(state =>
            {
                var slice = Vacation.SliceOf(state);
                ((JObject)slice["visited"])[zielId] = true;
                slice["heimkehrZiel"] = zielId;
                slice["heimkehrAbflug"] = (long)J.Num(slice["bookedAt"]);
                slice["heimkehrAt"] = nowMs;
                slice["heimkehrGefeiert"] = false;
                slice["phase"] = Vacation.PhaseNone;
                slice["destId"] = "";
                slice["bookedAt"] = 0;
                slice["returnAt"] = 0;
                slice["pickupBy"] = 0;
                slice["postcards"] = 0;
                slice["trips"] = J.Int(slice["trips"]) + 1;
                slice["erholtBis"] = nowMs + Vacation.ErholungsBoostMs;
                if (J.Num(slice["weltengoobyAt"]) <= 0.0 && Vacation.AlleZieleBesucht(slice))
                {
                    slice["weltengoobyAt"] = nowMs;
                    result.WeltengoobyNeu = true;
                }
                state["vacation"] = slice;
                if (phase == Vacation.PhaseOverdue) Economy.Spend(J.EnsureDict(state, "economy"), Vacation.TaxiFee, "taxi");
                Economy.Award(J.EnsureDict(state, "economy"), d.SouvenirCoins, "souvenir");
            });
            gs.NotifySliceChanged("economy");
            gs.NotifySliceChanged("vacation");
            result.Ok = true;
            return result;
        }
    }
}
