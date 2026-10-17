using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Gooby's holiday as an experience (ports of urlaubs_besuch.gd, urlaubs_aktivitaeten.gd and heimkehr_logik.gd):
    /// while he is away you can visit him in one of four scenes (beach, mountains, city, space); there you pet him, take a
    /// holiday photo, play a small activity (collect shells, find mountain flowers, order at the café, find star
    /// splinters) and pick up the day's find at the souvenir spot. Once picked up and home again he brings exactly one
    /// souvenir (a snow globe, a fridge magnet or a pennant of the destination — chosen from the trip's own data) and,
    /// for 24 hours, the two others wait at the airport. Pure statics over the state; time is handed in.
    /// </summary>
    public static class Urlaub
    {
        public const string Strand = "strand", Berge = "berge", Stadt = "stadt", Weltraum = "weltraum";

        public static readonly IReadOnlyDictionary<string, string> ZielArchetyp = new Dictionary<string, string>
        {
            ["beach"] = Strand, ["harbor"] = Strand,
            ["meadowTrip"] = Berge, ["spookGarden"] = Berge, ["nightSky"] = Berge,
            ["bigCity"] = Stadt, ["bakery"] = Stadt, ["toyRoom"] = Stadt,
            ["space"] = Weltraum,
        };

        public const int TapAnzahl = 5;
        public const double FunStreicheln = 4.0;
        public const double FunMiniFertig = 6.0;
        public const double FunSouvenir = 3.0;
        public const int SouvenirCoins = 5;

        public static string ArchetypFuer(string destId) => destId != null && ZielArchetyp.TryGetValue(destId, out var a) ? a : "";

        /// <summary>A visit is possible only while Gooby is really there (phase away with a known destination).</summary>
        public static bool Verfuegbar(JObject state, long nowMs)
        {
            var v = Vacation.SliceOf(state);
            return Vacation.PhaseAt(v, nowMs) == Vacation.PhaseAway && ZielArchetyp.ContainsKey(J.Str(v["destId"]));
        }

        public static string Ziel(JObject state) => J.Str(Vacation.SliceOf(state)["destId"]);

        /// <summary>Remembers a visit (vacation.besuche) — the trip itself stays untouched.</summary>
        public static void MerkeBesuch(JObject state, string destId)
        {
            if (state == null || !ZielArchetyp.ContainsKey(destId ?? "")) return;
            var v = Vacation.SliceOf(state);
            ((JObject)v["besuche"])[destId] = true;
            state["vacation"] = v;
        }

        // ── activities ──

        /// <summary>Tap spots (shells, flowers, star splinters): each counts once; done when all are found.</summary>
        public sealed class TapSpots
        {
            public readonly int Gesamt;
            private readonly HashSet<int> _getroffen = new HashSet<int>();

            public TapSpots(int anzahl = TapAnzahl) { Gesamt = Math.Max(1, anzahl); }

            public int Rest => Gesamt - _getroffen.Count;
            public bool Fertig => Rest <= 0;
            public bool Getroffen(int index) => _getroffen.Contains(index);

            /// <summary>Taps spot <paramref name="index"/>; false for a second tap or an index outside.</summary>
            public bool Tippe(int index)
            {
                if (index < 0 || index >= Gesamt || _getroffen.Contains(index)) return false;
                _getroffen.Add(index);
                return true;
            }
        }

        public static int SouvenirTag(long nowMs) => (int)Math.Floor(nowMs / (double)Vacation.MsPerDay);

        /// <summary>One find a day at the souvenir spot (UTC day index).</summary>
        public static bool SouvenirBereit(JObject state, long nowMs) => J.Int(Vacation.SliceOf(state)["souvenirTag"]) != SouvenirTag(nowMs);

        public static string SouvenirItemId(string destId) => "souvenir_" + destId;

        /// <summary>Picks the day's find: day latch, the item into inventory.items, +5 coins. False when today's is taken.</summary>
        public static bool SouvenirEinloesen(JObject state, string destId, long nowMs)
        {
            if (state == null || !SouvenirBereit(state, nowMs) || !ZielArchetyp.ContainsKey(destId ?? "")) return false;
            var v = Vacation.SliceOf(state);
            v["souvenirTag"] = SouvenirTag(nowMs);
            state["vacation"] = v;
            AddItem(state, SouvenirItemId(destId));
            Economy.Award(J.EnsureDict(state, "economy"), SouvenirCoins, "souvenir");
            FunBonus(state, FunSouvenir);
            return true;
        }

        /// <summary>The café gag's next silly order (rotation, never empty for a non-empty list).</summary>
        public static int BestellungIndex(int zaehler, int anzahl) => anzahl <= 0 ? -1 : ((zaehler % anzahl) + anzahl) % anzahl;

        public static void FunBonus(JObject state, double delta)
        {
            if (state == null || delta <= 0.0) return;
            var gooby = J.EnsureDict(state, "gooby");
            gooby["stats"] = Stats.ApplyDeltas(J.Dict(gooby["stats"]), new Dictionary<string, double> { ["fun"] = delta });
        }

        // ── homecoming (heimkehr_logik.gd) ──

        public static readonly string[] Typen = { "schneekugel", "magnet", "wimpel" };
        public static readonly IReadOnlyDictionary<string, int> TypPreise = new Dictionary<string, int> { ["schneekugel"] = 55, ["magnet"] = 35, ["wimpel"] = 45 };
        public const long FensterMs = 24 * 3_600_000L;
        public const int SpruchAnzahl = 2;
        public const string KaufOk = "ok", KaufZu = "geschlossen", KaufPleite = "pleite";

        /// <summary>Is the reunion still to be celebrated? Only when Gooby is home, a pickup was stamped and not yet celebrated.</summary>
        public static bool HeimkehrAusstehend(JObject state)
        {
            var v = Vacation.SliceOf(state);
            if (J.Str(v["phase"]) != Vacation.PhaseNone) return false;
            if (J.Long(v["heimkehrAt"]) <= 0 || J.IsTrue(v["heimkehrGefeiert"])) return false;
            return ZielArchetyp.ContainsKey(J.Str(v["heimkehrZiel"]));
        }

        /// <summary>FNV-1a over the text: the same trip always brings the same souvenir (no OS randomness).</summary>
        private static uint Hash(string text)
        {
            var h = 2166136261u;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(text)) { h ^= b; h *= 16777619u; }
            return h;
        }

        public static string MitbringselTyp(string zielId, long abflugMs) => Typen[Hash(zielId + "|" + abflugMs) % (uint)Typen.Length];

        public static string MitbringselId(string zielId, string typ) => "mitbringsel_" + zielId + "_" + typ;

        public static string SpruchKey(string zielId, long abflugMs) => "heimkehr.spruch." + (Hash("spruch|" + zielId + "|" + abflugMs) % SpruchAnzahl + 1);

        public sealed class Heimkehr
        {
            public bool Ok;
            public string ItemId = "", Typ = "", ZielId = "", SpruchKey = "";
        }

        /// <summary>Celebrates the reunion exactly once: the latch, and the souvenir into inventory.items.</summary>
        public static Heimkehr Feiern(JObject state)
        {
            var r = new Heimkehr();
            if (state == null || !HeimkehrAusstehend(state)) return r;
            var v = Vacation.SliceOf(state);
            var ziel = J.Str(v["heimkehrZiel"]);
            var abflug = J.Long(v["heimkehrAbflug"]);
            r.Typ = MitbringselTyp(ziel, abflug);
            r.ItemId = MitbringselId(ziel, r.Typ);
            r.ZielId = ziel;
            r.SpruchKey = SpruchKey(ziel, abflug);
            v["heimkehrGefeiert"] = true;
            state["vacation"] = v;
            AddItem(state, r.ItemId);
            r.Ok = true;
            return r;
        }

        /// <summary>The 24-hour souvenir window at the airport (hangs only on the pickup, not on the celebration).</summary>
        public static bool FensterOffen(JObject state, long nowMs)
        {
            var v = Vacation.SliceOf(state);
            var ab = J.Long(v["heimkehrAt"]);
            if (ab <= 0 || !ZielArchetyp.ContainsKey(J.Str(v["heimkehrZiel"]))) return false;
            return nowMs >= ab && nowMs < ab + FensterMs;
        }

        public static int FensterRestH(JObject state, long nowMs)
        {
            if (!FensterOffen(state, nowMs)) return 0;
            var ab = J.Long(Vacation.SliceOf(state)["heimkehrAt"]);
            return (int)Math.Ceiling((ab + FensterMs - nowMs) / 3_600_000.0);
        }

        public sealed class Ware
        {
            public string Id, Typ, ZielId;
            public int Preis;
        }

        /// <summary>The window's offer: the TWO kinds Gooby did not bring (empty once it closes).</summary>
        public static List<Ware> FensterSortiment(JObject state, long nowMs)
        {
            var outp = new List<Ware>();
            if (!FensterOffen(state, nowMs)) return outp;
            var v = Vacation.SliceOf(state);
            var ziel = J.Str(v["heimkehrZiel"]);
            var mitgebracht = MitbringselTyp(ziel, J.Long(v["heimkehrAbflug"]));
            foreach (var typ in Typen)
                if (typ != mitgebracht) outp.Add(new Ware { Id = MitbringselId(ziel, typ), Typ = typ, ZielId = ziel, Preis = TypPreise[typ] });
            return outp;
        }

        /// <summary>Buys a window piece: coins out AND the piece in, or nothing ("ok" | "geschlossen" | "pleite").</summary>
        public static string Kaufe(JObject state, string wareId, long nowMs)
        {
            var ware = FensterSortiment(state, nowMs).FirstOrDefault(w => w.Id == wareId);
            if (ware == null) return KaufZu;
            if (!Economy.Spend(J.EnsureDict(state, "economy"), ware.Preis, "gooby_free")) return KaufPleite;
            AddItem(state, ware.Id);
            return KaufOk;
        }

        // ── keepsakes ──

        public sealed class Andenken
        {
            public string ItemId, ZielId, Typ;
            public int Anzahl;
        }

        /// <summary>Every keepsake in inventory.items: the day's finds (souvenir_*) and the souvenirs (mitbringsel_*).</summary>
        public static List<Andenken> Sammlung(JObject state)
        {
            var outp = new List<Andenken>();
            if (!(J.Path(state, "inventory", "items") is JObject items)) return outp;
            foreach (var p in items.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var n = (int)J.Num(p.Value);
                if (n <= 0) continue;
                if (p.Name.StartsWith("souvenir_", StringComparison.Ordinal))
                {
                    var ziel = p.Name.Substring("souvenir_".Length);
                    if (ZielArchetyp.ContainsKey(ziel)) outp.Add(new Andenken { ItemId = p.Name, ZielId = ziel, Typ = "fund", Anzahl = n });
                }
                else if (p.Name.StartsWith("mitbringsel_", StringComparison.Ordinal))
                {
                    var rest = p.Name.Substring("mitbringsel_".Length);
                    var cut = rest.LastIndexOf('_');
                    if (cut <= 0) continue;
                    var ziel = rest.Substring(0, cut);
                    var typ = rest.Substring(cut + 1);
                    if (ZielArchetyp.ContainsKey(ziel) && Array.IndexOf(Typen, typ) >= 0)
                        outp.Add(new Andenken { ItemId = p.Name, ZielId = ziel, Typ = typ, Anzahl = n });
                }
            }
            return outp;
        }

        private static void AddItem(JObject state, string itemId)
        {
            var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
            items[itemId] = J.Int(items[itemId]) + 1;
        }
    }
}
