using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// What the shop counters promise on their tabs (Unity): every purchase stamps the shop's loyalty card — ten stamps
    /// and the shopkeeper hands over a present from the shelf; one ware a day is the deal with the star, 30 % off; the till
    /// keeps a receipt of the last purchases. Kept per shop under <c>city.treue.&lt;shop&gt;</c>; the deal and the presents
    /// are picked from the shop's main shelf by the day, the same on every device.
    /// </summary>
    public static class ShopTreue
    {
        public const int KarteStempel = 10, BonZeilen = 6;
        public const double AngebotRabatt = 0.3;

        /// <summary>The shops with a card, a deal and a till (their counter is <see cref="ShopLogic"/> or POW!'s).</summary>
        public static readonly string[] Laeden = { "mart", "goobyman", "goobytheke", "pow", "baumarkt" };

        public static bool HatKarte(string laden) => Array.IndexOf(Laeden, laden) >= 0;

        private static JObject Karte(JObject state, string laden) => J.Path(state, "city", "treue", laden ?? "") as JObject;

        private static JObject KarteIm(JObject state, string laden) => J.EnsureDict(J.EnsureDict(J.EnsureDict(state, "city"), "treue"), laden);

        public static int Stempel(JObject state, string laden) => Math.Max(0, Math.Min(KarteStempel, J.Int(Karte(state, laden)?["stempel"])));

        public static int Geschenke(JObject state, string laden) => Math.Max(0, J.Int(Karte(state, laden)?["geschenke"]));

        public static bool Voll(JObject state, string laden) => Stempel(state, laden) >= KarteStempel;

        public sealed class BonZeile
        {
            public string Id;
            public int Preis;
            public long At;
            public bool Angebot;
        }

        /// <summary>The receipt: the last purchases at this shop, newest last.</summary>
        public static List<BonZeile> Bon(JObject state, string laden) =>
            (Karte(state, laden)?["bon"] as JArray ?? new JArray()).OfType<JObject>()
                .Select(o => new BonZeile { Id = J.Str(o["id"]), Preis = J.Int(o["preis"]), At = J.Long(o["at"]), Angebot = J.IsTrue(o["angebot"]) }).ToList();

        /// <summary>A purchase at a shop (inside the purchase's update): one stamp (the card stops at ten) and a receipt line.</summary>
        public static void Buche(JObject state, string laden, string wareId, int preis, long nowMs, bool angebot = false)
        {
            if (state == null || !HatKarte(laden)) return;
            var karte = KarteIm(state, laden);
            karte["stempel"] = Math.Min(KarteStempel, J.Int(karte["stempel"]) + 1);
            var bon = karte["bon"] as JArray ?? new JArray();
            bon.Add(new JObject { ["id"] = wareId ?? "", ["preis"] = Math.Max(0, preis), ["at"] = nowMs, ["angebot"] = angebot });
            while (bon.Count > BonZeilen) bon.RemoveAt(0);
            karte["bon"] = bon;
        }

        /// <summary>The shop's main shelf for deals and presents (Baumarkt: its material packs; POW!: its toy pool).</summary>
        public static List<Ware> Regal(string path) =>
            (path == ShopLogic.BaumarktPath ? ShopLogic.Materialien(path) : path == ShopLogic.PowPath ? PowLogic.Pool(path) : ShopLogic.Waren(path))
                .Where(w => !w.NurEinmal && !w.IsEffekt && !w.BrauchtRezept && w.Preis > 0).ToList();

        /// <summary>The day's deal of a shop (null on an empty shelf): one ware picked by shop and calendar day.</summary>
        public static Ware Angebot(string path, string laden, string tag)
        {
            var regal = Regal(path);
            return regal.Count == 0 ? null : regal[(int)(Fnv(laden + "|" + tag) % (uint)regal.Count)];
        }

        public static int AngebotPreis(Ware ware) => ware == null ? 0 : Math.Max(1, (int)Math.Round(ware.Preis * (1.0 - AngebotRabatt), MidpointRounding.AwayFromZero));

        /// <summary>The present for the n-th full card of a shop: something from the dearer half of the shelf.</summary>
        public static Ware GeschenkWare(string path, string laden, int nr)
        {
            var regal = Regal(path).OrderByDescending(w => w.Preis).ThenBy(w => w.Id, StringComparer.Ordinal).ToList();
            if (regal.Count == 0) return null;
            var auswahl = regal.Take(Math.Max(1, (regal.Count + 1) / 2)).ToList();
            return auswahl[(int)(Fnv(laden + "|geschenk|" + nr) % (uint)auswahl.Count)];
        }

        /// <summary>A full card for its present: the ware lands in the fridge or the store, the card starts again. Null = not full.</summary>
        public static Ware GeschenkAbholen(JObject state, string laden, string path)
        {
            if (state == null || !Voll(state, laden)) return null;
            var karte = KarteIm(state, laden);
            var nr = J.Int(karte["geschenke"]);
            var ware = GeschenkWare(path, laden, nr);
            if (ware == null) return null;
            var inventory = J.EnsureDict(state, "inventory");
            if (ware.IsFood)
            {
                var food = J.EnsureDict(inventory, "food");
                food[ware.Id] = J.Int(food[ware.Id]) + ware.Menge;
            }
            else
            {
                var items = J.EnsureDict(inventory, "items");
                items[ware.Inventar] = J.Int(items[ware.Inventar]) + ware.Menge;
            }
            karte["stempel"] = 0;
            karte["geschenke"] = nr + 1;
            return ware;
        }

        private static uint Fnv(string s)
        {
            var h = 2166136261u;
            foreach (var b in Encoding.UTF8.GetBytes(s ?? ""))
            {
                h ^= b;
                h *= 16777619u;
            }
            return h;
        }
    }
}
