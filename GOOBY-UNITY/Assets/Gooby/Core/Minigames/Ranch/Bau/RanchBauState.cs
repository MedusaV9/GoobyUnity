using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The ranch build mode's save (port of ranch_bau_state.gd): the additive key <c>ranch.bau</c> — items on the grid,
    /// unlocked zones, facility levels, the store of decoration bought at the furniture barn, the migration flag and the
    /// uid sequence — healed on every read. Every purchase is atomic: check, then gold and the change in one step, or
    /// nothing. Building costs gold, never energy. Moving a placed piece is free (a Unity addition; the grid always
    /// could). The old four extensions (more boxes, riding arena, pasture fence) map onto grid facilities once.
    /// </summary>
    public static class RanchBauState
    {
        public const string Key = "bau", SpendReason = "ranch_bau";
        public const string FehlerUnbekannt = "unbekannt", FehlerSchonGebaut = "schonGebaut", FehlerZuTeuer = "zuTeuer", FehlerPlatz = "keinPlatz",
            FehlerNichtGebaut = "nichtGebaut", FehlerAusgebaut = "vollAusgebaut", FehlerSchonFrei = "schonFrei", FehlerNurBelohnung = "nurBelohnung";

        /// <summary>Where the migration sets the old facilities (start zone: x 0..9, y 6..15).</summary>
        public static readonly IReadOnlyDictionary<string, Zelle> MigrationsPlaetze = new Dictionary<string, Zelle>
        {
            ["stallboxen"] = new Zelle(1, 7), ["weide"] = new Zelle(5, 7), ["parcours"] = new Zelle(0, 11),
        };

        public sealed class Ergebnis
        {
            public bool Ok;
            public string Fehler = "", Uid = "";
            public int Kosten, Erstattung, Stufe;

            /// <summary>A pulled-down reward piece went back into the build store.</summary>
            public bool InsLager;

            public static Ergebnis Nein(string fehler, int kosten = 0, int stufe = 0) => new Ergebnis { Fehler = fehler, Kosten = kosten, Stufe = stufe };
        }

        public static JObject DefaultBau() => new JObject
        {
            ["v"] = 1, ["items"] = new JArray(), ["zonen"] = new JArray("start"), ["anlagen"] = new JObject(), ["lager"] = new JObject(),
            ["bestandMigriert"] = false, ["uidSeq"] = 1,
        };

        /// <summary>Self-heal: types repaired, valid data (and unknown keys) kept verbatim.</summary>
        public static JObject NormalizeBau(JToken raw)
        {
            var bau = raw is JObject o ? (JObject)o.DeepClone() : DefaultBau();
            bau["v"] = Math.Max(1, (int)J.NumOr(bau["v"], 1.0));
            if (!(bau["items"] is JArray)) bau["items"] = new JArray();
            var zonen = bau["zonen"] as JArray ?? new JArray();
            if (!zonen.Any(t => J.Str(t) == "start")) zonen.Insert(0, "start");
            bau["zonen"] = zonen;
            var anlagen = new JObject();
            foreach (var p in J.Dict(bau["anlagen"]).Properties())
                anlagen[p.Name] = new JObject { ["stufe"] = Math.Max(1, (int)J.NumOr(p.Value is JObject e ? e["stufe"] : null, 1.0)) };
            bau["anlagen"] = anlagen;
            var lager = new JObject();
            foreach (var p in J.Dict(bau["lager"]).Properties())
            {
                var anzahl = Math.Max(0, (int)J.NumOr(p.Value, 0.0));
                if (anzahl > 0) lager[p.Name] = anzahl;
            }
            bau["lager"] = lager;
            bau["bestandMigriert"] = J.IsTrue(bau["bestandMigriert"]);
            bau["uidSeq"] = Math.Max(1, (int)J.NumOr(bau["uidSeq"], 1.0));
            return bau;
        }

        /// <summary>The healed <c>ranch.bau</c> for reading.</summary>
        public static JObject Lese(JObject state) => NormalizeBau(J.Path(state, RanchHof.SliceId, Key));

        /// <summary><c>ranch.bau</c> inside a write, healed and stored back (bau_im_state).</summary>
        public static JObject Im(JObject state)
        {
            var ranch = J.EnsureDict(state, RanchHof.SliceId);
            var bau = NormalizeBau(ranch[Key]);
            ranch[Key] = bau;
            return bau;
        }

        public static bool Vorhanden(JObject state) => J.Path(state, RanchHof.SliceId, Key) is JObject;

        /// <summary>The unlocked zones' rects (for the grid).</summary>
        public static List<ZellRect> ZonenRects(JObject bau, JObject balance)
        {
            var tabelle = RanchBauKatalog.Zonen(balance);
            var o = new List<ZellRect>();
            foreach (var id in (bau?["zonen"] as JArray ?? new JArray()).Select(t => J.Str(t)))
            {
                var zone = tabelle.FirstOrDefault(z => z.Id == id);
                if (zone != null) o.Add(zone.Rect);
            }
            return o;
        }

        public static bool ZoneFrei(JObject bau, string zoneId) => (bau?["zonen"] as JArray ?? new JArray()).Any(t => J.Str(t) == zoneId);

        /// <summary>The grid rebuilt from the save.</summary>
        public static (RanchGridData grid, JArray leftovers) GridVon(JObject state, JObject balance = null) => GridAus(Lese(state), balance);

        public static (RanchGridData grid, JArray leftovers) GridAus(JObject bau, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            return RanchGridData.FromSave(bau["items"] as JArray, RanchBauKatalog.Defs(bal), RanchBauKatalog.GridGroesse(bal), ZonenRects(bau, bal));
        }

        /// <summary>A facility's current level (0 = not built).</summary>
        public static int AnlageStufe(JObject bau, string anlageId)
        {
            var anlagen = J.Dict(bau?["anlagen"]);
            if (!anlagen.ContainsKey(anlageId ?? "")) return 0;
            return Math.Max(1, (int)J.NumOr(J.Dict(anlagen[anlageId])["stufe"], 1.0));
        }

        public static int LagerAnzahl(JObject bau, string itemId) => (int)J.NumOr(J.Dict(bau?["lager"])[itemId ?? ""], 0.0);

        /// <summary>Places a cell item (facility, decoration, ground). Decoration from the store is free; a reward only comes from it.</summary>
        public static Ergebnis Platziere(JObject state, string itemId, Zelle at, int rot, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            var def = RanchBauKatalog.Def(bal, itemId);
            if (state == null || def == null || def.Kante) return Ergebnis.Nein(FehlerUnbekannt);
            var bau = Lese(state);
            if (def.Kategorie == "anlage")
            {
                if (def.Upgrade) return Ergebnis.Nein(FehlerUnbekannt);
                if (AnlageStufe(bau, itemId) > 0) return Ergebnis.Nein(FehlerSchonGebaut);
            }
            var ausLager = LagerAnzahl(bau, itemId) > 0;
            if (def.Belohnung && !ausLager) return Ergebnis.Nein(FehlerNurBelohnung);
            var check = GridAus(bau, bal).grid.CanPlace(def, at, rot);
            if (!check.ok) return Ergebnis.Nein(check.reason);
            return Buche(state, def, at, RanchGridData.PosMod(rot, 4), null, ausLager ? 0 : def.Kosten, ausLager);
        }

        /// <summary>Puts a fence on an edge (any side, normalised).</summary>
        public static Ergebnis PlatziereKante(JObject state, string itemId, Zelle cell, string seite, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            var def = RanchBauKatalog.Def(bal, itemId);
            if (state == null || def == null || !def.Kante) return Ergebnis.Nein(FehlerUnbekannt);
            var check = GridVon(state, bal).grid.CanPlaceKante(def, cell, seite);
            if (!check.ok) return Ergebnis.Nein(check.reason);
            var norm = RanchGridData.NormalizeKante(cell, seite);
            return Buche(state, def, norm.cell, 0, norm.seite, def.Kosten, false);
        }

        /// <summary>Moves or turns a placed cell item — free; the grid checks the new spot (the item itself never blocks).</summary>
        public static Ergebnis Verschiebe(JObject state, string uid, Zelle at, int rot, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            if (state == null) return Ergebnis.Nein(FehlerUnbekannt);
            var grid = GridVon(state, bal).grid;
            var item = grid.GetItem(uid);
            if (item == null || item.Kante != null) return Ergebnis.Nein(FehlerUnbekannt);
            var check = grid.MoveItem(uid, at, rot);
            if (!check.ok) return Ergebnis.Nein(check.reason);
            var b = Im(state);
            var entry = (b["items"] as JArray).OfType<JObject>().FirstOrDefault(e => J.Str(e["uid"]) == uid);
            if (entry == null) return Ergebnis.Nein(FehlerUnbekannt);
            entry["at"] = new JArray(at.X, at.Y);
            entry["rot"] = RanchGridData.PosMod(rot, 4);
            return new Ergebnis { Ok = true, Uid = uid };
        }

        /// <summary>What a demolition would pay back (abriss_erstattung × every level paid, floored).</summary>
        public static int ErstattungFuer(JObject bau, string uid, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            var entry = (bau?["items"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(e => J.Str(e["uid"]) == uid);
            var def = entry != null ? RanchBauKatalog.Def(bal, J.Str(entry["item"])) : null;
            if (def == null) return -1;
            if (def.Belohnung) return 0;
            var bezahlt = def.Kosten;
            if (def.Kategorie == "anlage")
            {
                var stufen = RanchBauKatalog.StufenKosten(bal, def.Id);
                bezahlt = stufen.Take(Math.Min(AnlageStufe(bau, def.Id), stufen.Length)).Sum();
            }
            return (int)Math.Floor(bezahlt * RanchBauKatalog.AbrissErstattung(bal));
        }

        /// <summary>Demolition with a partial refund — a reward piece is never lost: it goes back into the build store.</summary>
        public static Ergebnis Entferne(JObject state, string uid, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            if (state == null) return Ergebnis.Nein(FehlerUnbekannt);
            var bau = Lese(state);
            var erstattung = ErstattungFuer(bau, uid, bal);
            if (erstattung < 0) return Ergebnis.Nein(FehlerUnbekannt);
            var b = Im(state);
            var items = (JArray)b["items"];
            var entry = items.OfType<JObject>().First(e => J.Str(e["uid"]) == uid);
            var def = RanchBauKatalog.Def(bal, J.Str(entry["item"]));
            b["items"] = new JArray(items.Where(t => !(t is JObject e) || J.Str(e["uid"]) != uid));
            if (def.Kategorie == "anlage") ((JObject)b["anlagen"]).Remove(def.Id);
            if (def.Belohnung) LagerHinzu(b, def.Id);
            if (erstattung > 0) Economy.Award(J.EnsureDict(state, "economy"), erstattung, SpendReason);
            return new Ergebnis { Ok = true, Uid = uid, Erstattung = erstattung, InsLager = def.Belohnung };
        }

        /// <summary>Buys a facility's next level. The upgrade kind (pasture fence) buys level 1 directly — it has no placement.</summary>
        public static Ergebnis Ausbauen(JObject state, string anlageId, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            var stufen = RanchBauKatalog.StufenKosten(bal, anlageId);
            if (state == null || stufen.Length == 0) return Ergebnis.Nein(FehlerUnbekannt);
            var istUpgrade = RanchBauKatalog.Def(bal, anlageId)?.Upgrade == true;
            var stufe = AnlageStufe(Lese(state), anlageId);
            if (stufe == 0 && !istUpgrade) return Ergebnis.Nein(FehlerNichtGebaut);
            if (stufe >= stufen.Length) return Ergebnis.Nein(FehlerAusgebaut, 0, stufe);
            var kosten = RanchBauKatalog.NaechsteStufeKosten(bal, anlageId, stufe);
            if (!Economy.Spend(J.EnsureDict(state, "economy"), kosten, SpendReason)) return Ergebnis.Nein(FehlerZuTeuer, kosten, stufe);
            ((JObject)Im(state)["anlagen"])[anlageId] = new JObject { ["stufe"] = stufe + 1 };
            return new Ergebnis { Ok = true, Stufe = stufe + 1, Kosten = kosten };
        }

        /// <summary>Buys a grid zone free (north meadow, east field).</summary>
        public static Ergebnis ZoneFreischalten(JObject state, string zoneId, JObject balance = null)
        {
            var bal = balance ?? RanchBauKatalog.Balance;
            var zone = RanchBauKatalog.ZoneVon(bal, zoneId);
            if (state == null || zone == null) return Ergebnis.Nein(FehlerUnbekannt);
            if (ZoneFrei(Lese(state), zoneId)) return Ergebnis.Nein(FehlerSchonFrei);
            if (!Economy.Spend(J.EnsureDict(state, "economy"), zone.Kosten, SpendReason)) return Ergebnis.Nein(FehlerZuTeuer, zone.Kosten);
            var zonen = (JArray)Im(state)["zonen"];
            if (!zonen.Any(t => J.Str(t) == zoneId)) zonen.Add(zoneId);
            return new Ergebnis { Ok = true, Kosten = zone.Kosten };
        }

        /// <summary>Decoration into the build store (the furniture barn delivers here) — no purchase; the shop books it.</summary>
        public static void LagerHinzu(JObject bau, string itemId, int anzahl = 1)
        {
            var lager = bau["lager"] as JObject ?? new JObject();
            lager[itemId] = (int)J.NumOr(lager[itemId], 0.0) + Math.Max(1, anzahl);
            bau["lager"] = lager;
        }

        /// <summary>
        /// The old purchases of <c>ranch.wirtschaft.ausbau</c> (boxes 1..3, riding arena, pasture fence) mapped 1:1 onto grid
        /// facilities, once — no progress lost; the stable boxes and the pasture at level 1 are always included. Free.
        /// </summary>
        public static bool MigriereBestand(JObject state, JObject balance = null)
        {
            if (state == null || J.IsTrue(Lese(state)["bestandMigriert"])) return false;
            var bal = balance ?? RanchBauKatalog.Balance;
            var defs = RanchBauKatalog.Defs(bal);
            var alt = J.Path(state, RanchHof.SliceId, "wirtschaft", "ausbau") as JObject ?? new JObject();
            var plan = new List<(string id, int stufe)> { ("stallboxen", J.ClampI((int)J.NumOr(alt["boxen"], 1.0), 1, 4)), ("weide", 1) };
            if (J.IsTrue(alt["reitplatz"])) plan.Add(("parcours", 1));
            var b = Im(state);
            var anlagen = (JObject)b["anlagen"];
            foreach (var (id, stufe) in plan)
            {
                if (anlagen.ContainsKey(id) || !defs.ContainsKey(id)) continue;
                var at = MigrationsPlaetze.TryGetValue(id, out var p) ? p : new Zelle(0, 6);
                ((JArray)b["items"]).Add(new JObject { ["uid"] = NeueUid(b), ["item"] = id, ["at"] = new JArray(at.X, at.Y), ["rot"] = 0 });
                anlagen[id] = new JObject { ["stufe"] = stufe };
            }
            if (J.IsTrue(alt["weidezaun"]) && !anlagen.ContainsKey("weidezaun")) anlagen["weidezaun"] = new JObject { ["stufe"] = 1 };
            b["bestandMigriert"] = true;
            return true;
        }

        private static string NeueUid(JObject b)
        {
            var seq = (int)J.NumOr(b["uidSeq"], 1.0);
            b["uidSeq"] = seq + 1;
            return "rb-" + seq.ToString("D6");
        }

        private static Ergebnis Buche(JObject state, RanchBauDef def, Zelle at, int rot, string kante, int kosten, bool ausLager)
        {
            if (!Economy.Spend(J.EnsureDict(state, "economy"), kosten, SpendReason)) return Ergebnis.Nein(FehlerZuTeuer, kosten);
            var b = Im(state);
            var uid = NeueUid(b);
            var entry = new JObject { ["uid"] = uid, ["item"] = def.Id, ["at"] = new JArray(at.X, at.Y) };
            if (kante != null) entry["kante"] = kante;
            else entry["rot"] = rot;
            ((JArray)b["items"]).Add(entry);
            if (def.Kategorie == "anlage") ((JObject)b["anlagen"])[def.Id] = new JObject { ["stufe"] = 1 };
            if (ausLager)
            {
                var lager = (JObject)b["lager"];
                var rest = (int)J.NumOr(lager[def.Id], 0.0) - 1;
                if (rest > 0) lager[def.Id] = rest;
                else lager.Remove(def.Id);
            }
            return new Ergebnis { Ok = true, Uid = uid, Kosten = kosten };
        }
    }
}
