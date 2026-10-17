using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// "Raum speichern" layout presets (port of layout_presets_logic.gd): per room up to <see cref="Slots"/> named snapshots
    /// of every placed piece (all layers, walls included) and the garlands, in <c>home.presets[roomId]</c> — a slot is {}
    /// (free) or {"name", "items", "girlanden"}. Applying swaps the layout with a safety net: pieces the player no longer
    /// owns (neither placed nor in the storage) stay away and are counted as missing, placed pieces the preset does not use
    /// go into the storage — if that would overflow it, nothing changes at all. Every placed piece gets a fresh uid.
    /// </summary>
    public static class LayoutPresets
    {
        public const int Slots = 3;
        public const int NameMax = 24;
        public const string LagerVoll = "lager_voll", SlotUngueltig = "slot_ungueltig", NameLeer = "name_leer", PresetLeer = "preset_leer";

        public sealed class Plan
        {
            public bool Ok;
            public string Reason = "";
            public JArray Items = new JArray(), Girlanden = new JArray(), Storage = new JArray();
            public int Fehlend, NextUid;
        }

        /// <summary>The room's slots, always exactly <see cref="Slots"/> objects ({} = free).</summary>
        public static List<JObject> SlotsOf(GameStateStore gs, string roomId)
        {
            var raw = J.Path(gs.State, "home", "presets", roomId) as JArray;
            var outp = new List<JObject>();
            for (var i = 0; i < Slots; i++) outp.Add(raw != null && i < raw.Count && raw[i] is JObject o ? o : new JObject());
            return outp;
        }

        public static bool Frei(JObject slot) => slot == null || J.Str(slot["name"]).Length == 0;

        /// <summary>Mini preview: pieces and garlands in a slot.</summary>
        public static (int moebel, int girlanden) Zusammenfassung(JObject preset) =>
            ((preset?["items"] as JArray)?.Count ?? 0, (preset?["girlanden"] as JArray)?.Count ?? 0);

        /// <summary>Saves the room's current layout into <paramref name="slot"/> (overwrites); "" = ok, else the reason.</summary>
        public static string Speichern(GameStateStore gs, string roomId, int slot, string name)
        {
            if (slot < 0 || slot >= Slots) return SlotUngueltig;
            var sauber = (name ?? "").Trim();
            if (sauber.Length > NameMax) sauber = sauber.Substring(0, NameMax);
            if (sauber.Length == 0) return NameLeer;
            var preset = new JObject
            {
                ["name"] = sauber,
                ["items"] = HomeState.RoomItems(gs, roomId).DeepClone(),
                ["girlanden"] = Home.Girlanden.Liste(gs, roomId).DeepClone(),
            };
            WriteSlot(gs, roomId, slot, preset);
            return "";
        }

        public static string Loeschen(GameStateStore gs, string roomId, int slot)
        {
            if (slot < 0 || slot >= Slots) return SlotUngueltig;
            WriteSlot(gs, roomId, slot, new JObject());
            return "";
        }

        /// <summary>Plans the swap (pure) and commits it in one step when it works.</summary>
        public static Plan Anwenden(GameStateStore gs, string roomId, int slot)
        {
            if (slot < 0 || slot >= Slots) return new Plan { Reason = SlotUngueltig };
            var preset = SlotsOf(gs, roomId)[slot];
            if (Frei(preset)) return new Plan { Reason = PresetLeer };
            var room = RoomDefs.Get(roomId);
            var w = room?.GridW ?? 8;
            var h = room?.GridH ?? 8;
            var blocked = room != null ? RoomDefs.BlockedCells(room).Select(c => new Cell(c.x, c.y)).ToList() : new List<Cell>();
            if (roomId == "garden") blocked.AddRange(Garden.GardenField.BlockedCells(gs).Select(c => new Cell(c.x, c.y)));
            var plan = PlanApply(preset, HomeState.RoomItems(gs, roomId), Home.Girlanden.Liste(gs, roomId), HomeState.Storage(gs),
                HomeState.StorageCapacity(gs), FurnitureCatalog.Defs(), w, h, blocked,
                room != null ? HomeState.WallDoorSpans(room) : null, room != null ? HomeState.ExteriorWalls(room) : null,
                room != null ? HomeState.WallWindowSpans(room) : null,
                Math.Max(1, J.Int(J.Path(gs.State, "home", "nextUid"), 1)), roomId != "garden");
            if (!plan.Ok) return plan;
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, HomeState.SliceId);
                var r = J.EnsureDict(J.EnsureDict(home, "rooms"), roomId);
                r["items"] = plan.Items;
                if (roomId != "garden") r["girlanden"] = plan.Girlanden;
                home["storage"] = plan.Storage;
                home["nextUid"] = plan.NextUid;
            });
            gs.NotifySliceChanged(HomeState.SliceId);
            return plan;
        }

        /// <summary>
        /// The pure swap plan (touches none of its inputs): everything placed now joins the storage first, the preset takes
        /// its pieces from that pool (colour variant kept when there is one), the grid re-checks every spot (pieces that no
        /// longer fit go back into the pool), garlands need two cells in the room. Fails only on a storage overflow.
        /// </summary>
        public static Plan PlanApply(JObject preset, JArray placed, JArray garlandsNow, JArray storage, int capacity,
            IReadOnlyDictionary<string, FurnitureDef> defs, int w, int h, IEnumerable<Cell> blocked,
            Dictionary<string, List<(int, int)>> doors, Dictionary<string, string> exterior, Dictionary<string, List<(int, int)>> windows,
            int nextUid, bool garlands = true)
        {
            var pool = (JArray)(storage ?? new JArray()).DeepClone();
            foreach (var entry in placed ?? new JArray())
                if (entry is JObject e && J.Str(e["item"]).Length > 0) StorageLogic.Add(pool, J.Str(e["item"]), VariantOf(e));
            foreach (var g in garlandsNow ?? new JArray())
                if (g is JObject go && J.Str(go["typ"]).Length > 0) StorageLogic.Add(pool, J.Str(go["typ"]));
            var fehlend = 0;
            var uid = nextUid;
            var kandidaten = new JArray();
            foreach (var entry in preset?["items"] as JArray ?? new JArray())
            {
                if (!(entry is JObject e)) continue;
                var itemId = J.Str(e["item"]);
                var wanted = VariantOf(e);
                string variant = null;
                if (itemId.Length > 0 && StorageLogic.Take(pool, itemId, wanted)) variant = wanted;
                else if (itemId.Length > 0) variant = StorageLogic.TakeAny(pool, itemId);
                if (variant == null) { fehlend++; continue; }
                var stamped = (JObject)e.DeepClone();
                stamped["uid"] = $"i-{uid:D6}";
                uid++;
                if (FurnitureVariants.IsNeutral(variant)) stamped.Remove("variant");
                else stamped["variant"] = variant;
                kandidaten.Add(stamped);
            }
            var result = GridData.FromSave(kandidaten, defs, w, h, blocked, doors, exterior, windows);
            foreach (var leftover in result.Leftovers)
            {
                fehlend++;
                if (leftover is JObject lo && defs.ContainsKey(J.Str(lo["item"]))) StorageLogic.Add(pool, J.Str(lo["item"]), VariantOf(lo));
            }
            var neueGirlanden = new JArray();
            if (garlands)
                foreach (var g in preset?["girlanden"] as JArray ?? new JArray())
                {
                    if (!Anwendbar(g, w, h) || !StorageLogic.Take(pool, J.Str(g["typ"]))) { fehlend++; continue; }
                    neueGirlanden.Add(g.DeepClone());
                }
            var weights = new JObject();
            foreach (var kv in defs) weights[kv.Key] = new JObject { ["lagerwert"] = kv.Value.Lagerwert };
            if (StorageLogic.PointsUsed(pool, weights) > capacity) return new Plan { Reason = LagerVoll };
            return new Plan { Ok = true, Items = result.Grid.ToItemsArray(), Girlanden = neueGirlanden, Storage = pool, Fehlend = fehlend, NextUid = uid };
        }

        /// <summary>Load-time heal: every room keeps exactly <see cref="Slots"/> slots; broken slots empty, broken rows drop out.</summary>
        public static void Heilen(JObject home)
        {
            if (home == null) return;
            if (!(home["presets"] is JObject presets)) { home["presets"] = new JObject(); return; }
            foreach (var p in presets.Properties().ToList())
            {
                var roh = p.Value as JArray;
                var sauber = new JArray();
                for (var i = 0; i < Slots; i++) sauber.Add(Geheilt(roh != null && i < roh.Count ? roh[i] : null));
                presets[p.Name] = sauber;
            }
        }

        private static JObject Geheilt(JToken roh)
        {
            if (!(roh is JObject o)) return new JObject();
            var name = J.Str(o["name"]).Trim();
            if (name.Length == 0) return new JObject();
            var items = new JArray();
            foreach (var e in o["items"] as JArray ?? new JArray())
                if (e is JObject eo && J.Str(eo["item"]).Length > 0) items.Add(eo);
            var girlanden = new JArray();
            foreach (var g in o["girlanden"] as JArray ?? new JArray())
                if (Home.Girlanden.Gueltig(g)) girlanden.Add(g);
            return new JObject { ["name"] = name, ["items"] = items, ["girlanden"] = girlanden };
        }

        private static bool Anwendbar(JToken g, int w, int h)
        {
            if (!Home.Girlanden.Gueltig(g)) return false;
            var a = Home.Girlanden.Zelle(g, "zelle_a");
            var b = Home.Girlanden.Zelle(g, "zelle_b");
            if (a.Equals(b)) return false;
            foreach (var c in new[] { a, b })
                if (c.X < 0 || c.Y < 0 || c.X >= w || c.Y >= h) return false;
            return true;
        }

        private static string VariantOf(JObject entry)
        {
            var v = J.Str(entry?["variant"]);
            return v.Length == 0 ? StorageLogic.DefaultVariant : v;
        }

        private static void WriteSlot(GameStateStore gs, string roomId, int slot, JObject preset)
        {
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, HomeState.SliceId);
                if (!(home["presets"] is JObject presets)) home["presets"] = presets = new JObject();
                if (!(presets[roomId] is JArray liste) || liste.Count != Slots)
                {
                    liste = new JArray();
                    for (var i = 0; i < Slots; i++) liste.Add(new JObject());
                    presets[roomId] = liste;
                }
                liste[slot] = preset;
            });
            gs.NotifySliceChanged(HomeState.SliceId);
        }
    }
}
