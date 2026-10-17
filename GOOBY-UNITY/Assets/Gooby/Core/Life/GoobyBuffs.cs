using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Timed bonuses on Gooby's needs (port of buffs.gd): {id, stat, wert, until_ms} in the "buffs" slice, shown as chips
    /// on the stats sheet — a rescued event's reward, freshly brushed teeth, the rest after a holiday. One per id: granting
    /// again replaces the entry and only extends it. Pure helpers take the slice; <see cref="Grant"/> goes through the store.
    /// </summary>
    public static class GoobyBuffs
    {
        public const string SliceId = "buffs";
        public const long MsPerHour = 3_600_000L;
        /// <summary>The holiday's rest (urlaubs_bonus.gd): +5 energy shown until vacation.erholtBis.</summary>
        public const string Erholt = "erholt";
        /// <summary>Freshly brushed teeth (zahnputz.gd): +10 hygiene for three hours.</summary>
        public const string FrischeZaehne = "frische_zaehne";

        public static void RegisterSlice() => Save.SaveSchema.RegisterSlice(SliceId, DefaultSlice, NormalizeSlice);

        public static JObject DefaultSlice() => new JObject { ["aktiv"] = new JArray() };

        public static JObject NormalizeSlice(JToken raw)
        {
            var slice = raw as JObject ?? DefaultSlice();
            if (!(slice["aktiv"] is JArray aktiv)) slice["aktiv"] = aktiv = new JArray();
            for (var i = aktiv.Count - 1; i >= 0; i--)
                if (!(aktiv[i] is JObject b) || J.Str(b["id"]).Length == 0) aktiv.RemoveAt(i);
            return slice;
        }

        public static JObject SliceOf(JObject state) => NormalizeSlice(state?[SliceId]?.DeepClone());

        /// <summary>Sets or replaces the buff of that id (no stacking); returns the entry.</summary>
        public static JObject Add(JObject slice, string id, string stat, double wert, double dauerH, long nowMs)
        {
            var buff = new JObject { ["id"] = id, ["stat"] = stat, ["wert"] = wert, ["until_ms"] = nowMs + (long)(dauerH * MsPerHour) };
            if (!(slice["aktiv"] is JArray aktiv)) slice["aktiv"] = aktiv = new JArray();
            for (var i = 0; i < aktiv.Count; i++)
                if (J.Str(aktiv[i]?["id"]) == id) { aktiv[i] = buff; return buff; }
            aktiv.Add(buff);
            return buff;
        }

        public static List<JObject> Active(JObject slice, long nowMs)
        {
            var result = new List<JObject>();
            foreach (var b in slice?["aktiv"] as JArray ?? new JArray())
                if (b is JObject o && J.Long(o["until_ms"]) > nowMs) result.Add(o);
            return result;
        }

        /// <summary>Drops the expired ones; returns how many went.</summary>
        public static int Prune(JObject slice, long nowMs)
        {
            if (!(slice?["aktiv"] is JArray aktiv)) return 0;
            var removed = 0;
            for (var i = aktiv.Count - 1; i >= 0; i--)
                if (J.Long(aktiv[i]?["until_ms"]) <= nowMs) { aktiv.RemoveAt(i); removed++; }
            return removed;
        }

        /// <summary>The summed bonus on a need right now.</summary>
        public static double StatBonus(JObject slice, string stat, long nowMs)
        {
            var total = 0.0;
            foreach (var b in Active(slice, nowMs))
                if (J.Str(b["stat"]) == stat) total += J.Num(b["wert"]);
            return total;
        }

        public static void Grant(GameStateStore gs, string id, string stat, double wert, double dauerH, long nowMs)
        {
            if (gs == null || string.IsNullOrEmpty(id)) return;
            gs.Update(state =>
            {
                var slice = NormalizeSlice(state[SliceId]);
                Prune(slice, nowMs);
                Add(slice, id, stat, wert, dauerH, nowMs);
                state[SliceId] = slice;
            });
            gs.NotifySliceChanged(SliceId);
        }

        /// <summary>
        /// The holiday's rest as a buff (urlaubs_bonus.gd): while vacation.erholtBis lies ahead and the "erholt" buff is
        /// missing or ends clearly earlier (a minute of tolerance), it is granted to end exactly then. True when granted.
        /// </summary>
        public static bool SyncErholt(GameStateStore gs, long nowMs)
        {
            var v = Vacation.SliceOf(gs.State);
            if (!Vacation.ErholungsBoostAktiv(v, nowMs)) return false;
            var bis = J.Long(v["erholtBis"]);
            foreach (var b in Active(SliceOf(gs.State), nowMs))
                if (J.Str(b["id"]) == Erholt && J.Long(b["until_ms"]) >= bis - 60_000L) return false;
            Grant(gs, Erholt, "energy", 5.0, (bis - nowMs) / (double)MsPerHour, nowMs);
            return true;
        }
    }
}
