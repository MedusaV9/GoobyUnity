using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// POW! toy shop (port of pow_angebote.gd): three daily offers drawn deterministically from the pool by the
    /// local calendar day (same day → same toys and discounts everywhere, no server), and the GOOBY-Knipse camera,
    /// sold once as the gate for the photo mode (dialog flag `kamera_gekauft`). Godot stacked every toy in
    /// inventory.items and never used them; in Unity the five decorative pieces are furniture (<see cref="DekoMoebel"/>,
    /// straight into the build storage) and the rest are toys Gooby plays with at home (<see cref="Life.Spielzeug"/>).
    /// </summary>
    public static class PowLogic
    {
        public const string KameraItem = "kamera";
        public const string FlagKamera = "kamera_gekauft";
        public const int Slots = 3;
        public const string Ok = "ok", Pleite = "pleite", LagerVoll = "lager_voll";

        /// <summary>POW! ware id → furniture id of the piece that stands in the room (catalog price 0: only POW! sells it).</summary>
        public static readonly IReadOnlyDictionary<string, string> DekoMoebel = new Dictionary<string, string>
        {
            ["poster_gooby"] = "pow_poster", ["lavalampe"] = "pow_lavalampe", ["diskokugel"] = "pow_diskokugel",
            ["sammelfigur"] = "pow_sammelfigur", ["pluesch_moehre"] = "pow_pluesch_moehre",
        };

        /// <summary>The furniture a POW! ware becomes, or null for a toy.</summary>
        public static string MoebelVon(string wareId) => wareId != null && DekoMoebel.TryGetValue(wareId, out var id) ? id : null;

        public sealed class Angebot
        {
            public Ware Ware;
            public int Rabatt;      // percent
            public int PreisNeu;
        }

        private static JObject Daten(string path) => ContentSource.LoadObject(path);

        public static Ware Kamera(string path = ShopLogic.PowPath)
        {
            var raw = Daten(path)["kamera"] as JObject;
            if (raw == null) return null;
            var w = Ware.From(raw);
            w.Kategorie = "kamera";
            w.Einmalig = true;
            return w;
        }

        public static List<Ware> Pool(string path = ShopLogic.PowPath)
        {
            var outp = new List<Ware>();
            foreach (var t in Daten(path)["pool"] as JArray ?? new JArray())
                if (t is JObject o && J.Str(o["id"]).Length > 0)
                {
                    var w = Ware.From(o);
                    w.Kategorie = "spielzeug";
                    outp.Add(w);
                }
            return outp;
        }

        /// <summary>Day seed of a local calendar day: YYYYMMDD.</summary>
        public static int TagSeed(long nowMs, int biasMinutes)
        {
            var (y, m, d, _) = Time.GameClock.LocalDateTime(nowMs, biasMinutes);
            return y * 10000 + m * 100 + d;
        }

        /// <summary>Seconds until the offers change (local midnight).</summary>
        public static int RestSekundenBisWechsel(long nowMs, int biasMinutes)
        {
            var secs = (long)(nowMs / 1000.0) + biasMinutes * 60L;
            var seitMitternacht = (int)(secs % 86400L);
            if (seitMitternacht < 0) seitMitternacht += 86400;
            return 86400 - seitMitternacht;
        }

        public static int PreisMitRabatt(int preis, int rabattProzent) =>
            Math.Max(1, (int)Math.Round(preis * (1.0 - rabattProzent / 100.0), MidpointRounding.AwayFromZero));

        /// <summary>The three offers for a day seed: pairwise different toys, stable order, discounts 20/30/50 %.</summary>
        public static List<Angebot> Angebote(int seed, string path = ShopLogic.PowPath)
        {
            var outp = new List<Angebot>();
            var uebrig = Pool(path);
            if (uebrig.Count == 0) return outp;
            var rabatte = new List<int>();
            foreach (var r in Daten(path)["rabatte"] as JArray ?? new JArray()) rabatte.Add(J.Int(r));
            if (rabatte.Count == 0) rabatte.AddRange(new[] { 20, 30, 50 });
            var rng = new GoobyRng(seed);
            for (var slot = 0; slot < Slots && uebrig.Count > 0; slot++)
            {
                var index = J.ClampI((int)(rng.Next() * uebrig.Count), 0, uebrig.Count - 1);
                var ware = uebrig[index];
                uebrig.RemoveAt(index);
                var rabatt = rabatte[slot % rabatte.Count];
                outp.Add(new Angebot { Ware = ware, Rabatt = rabatt, PreisNeu = PreisMitRabatt(ware.Preis, rabatt) });
            }
            return outp;
        }

        public static bool HatKamera(GameStateStore gs) => J.Int(J.Path(gs?.State, "inventory", "items", KameraItem)) > 0;

        /// <summary>
        /// Buys an offer at its discounted price, atomically: coins out AND the piece in, or nothing. Toys stack in
        /// inventory.items, the decorative pieces go into the build storage (a full storage refuses before paying).
        /// </summary>
        public static string KaufeAngebot(JObject state, Angebot a)
        {
            if (state == null || a?.Ware == null || J.Int(J.Path(state, "economy", "coins")) < a.PreisNeu) return Pleite;
            var moebel = MoebelVon(a.Ware.Id);
            if (moebel != null && !PasstInsLager(state, moebel)) return LagerVoll;
            if (!Economy.Spend(J.EnsureDict(state, "economy"), a.PreisNeu, "pow")) return Pleite;
            if (moebel != null) InsLager(state, moebel);
            else
            {
                var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
                items[a.Ware.Inventar] = J.Int(items[a.Ware.Inventar]) + 1;
            }
            return Ok;
        }

        /// <summary><see cref="KaufeAngebot(JObject, Angebot)"/> on the store; the reason for the counter's line.</summary>
        public static string KaufeAngebotGrund(GameStateStore gs, Angebot a)
        {
            if (gs == null) return Pleite;
            var r = Pleite;
            gs.Update(state => r = KaufeAngebot(state, a));
            if (r == Ok)
            {
                gs.NotifySliceChanged("inventory");
                gs.NotifySliceChanged(HomeState.SliceId);
            }
            return r;
        }

        public static bool KaufeAngebot(GameStateStore gs, Angebot a) => KaufeAngebotGrund(gs, a) == Ok;

        /// <summary>True while decorative pieces from before still wait in inventory.items (<see cref="DekoNachholen"/>).</summary>
        public static bool DekoWartet(JObject state)
        {
            foreach (var id in DekoMoebel.Keys)
                if (J.Int(J.Path(state, "inventory", "items", id)) > 0) return true;
            return false;
        }

        /// <summary>
        /// Decorative pieces bought before they were furniture sit in inventory.items: they move into the build storage
        /// once (as many as fit; the rest waits for room). Returns how many moved.
        /// </summary>
        public static int DekoNachholen(JObject state)
        {
            if (!(J.Path(state, "inventory", "items") is JObject items)) return 0;
            var moved = 0;
            foreach (var kv in DekoMoebel)
            {
                var n = J.Int(items[kv.Key]);
                while (n > 0 && PasstInsLager(state, kv.Value))
                {
                    InsLager(state, kv.Value);
                    n--;
                    moved++;
                }
                if (n > 0) items[kv.Key] = n;
                else items.Remove(kv.Key);
            }
            return moved;
        }

        private static bool PasstInsLager(JObject state, string moebel)
        {
            var storage = J.Path(state, HomeState.SliceId, "storage") as JArray ?? new JArray();
            var capacity = J.Int(J.Path(state, HomeState.SliceId, "storageCapacity"), ShedLogic.BasisKapazitaet);
            return StorageLogic.CanAdd(storage, moebel, HomeState.DefsAsJson(), capacity);
        }

        private static void InsLager(JObject state, string moebel)
        {
            var home = J.EnsureDict(state, HomeState.SliceId);
            if (!(home["storage"] is JArray storage)) home["storage"] = storage = new JArray();
            StorageLogic.Add(storage, moebel);
        }

        /// <summary>The camera: once; sets the dialog flag so Bäm changes his lines.</summary>
        public static bool KaufeKamera(GameStateStore gs, string path = ShopLogic.PowPath)
        {
            var kamera = Kamera(path);
            if (gs == null || kamera == null || HatKamera(gs)) return false;
            if (!ShopLogic.Kaufe(gs, kamera)) return false;
            CityState.SetFlag(gs, FlagKamera, true);
            return true;
        }
    }
}
