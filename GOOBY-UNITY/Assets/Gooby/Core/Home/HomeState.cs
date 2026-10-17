using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// home slice access — port of scripts/home/home_state.gd: first-move default layout, room grid load/save with
    /// self-healing leftovers, storage API with capacity, flags, uid counter, shed upgrade, room unlocks.
    /// </summary>
    public static class HomeState
    {
        public const string SliceId = "home";
        public static readonly string[] DefaultUnlocked = { "hall", "living", "kitchen", "bathroom", "bedroom" };
        public const string FlagBedPlaced = "bedPlaced";

        private static bool _registered;

        /// <summary>Registers the home slice (defaults + self-heal) with the save schema — mirrors HomeState.register_slice().</summary>
        public static void RegisterSlice()
        {
            if (_registered) return;
            _registered = true;
            Save.SaveSchema.RegisterSlice(SliceId, DefaultSlice, NormalizeSlice);
        }

        public static JObject DefaultSlice() => new JObject
        {
            ["v"] = 1,
            ["rooms"] = new JObject(),
            ["unlockedRooms"] = new JArray(DefaultUnlocked),
            ["storage"] = new JArray(),
            ["storageCapacity"] = ShedLogic.BasisKapazitaet,
            ["movingDay"] = false,
            ["nextUid"] = 1,
            ["flags"] = new JObject(),
            ["materials"] = new JObject(),
            ["blueprints"] = new JArray(),
            ["shedStufe"] = 0,
            ["garden"] = Garden.GardenState.DefaultGarden(),
            ["lieferungen"] = new JArray(),
            ["presets"] = new JObject(),
        };

        public static JObject NormalizeSlice(JToken raw)
        {
            var home = raw as JObject ?? new JObject();
            home["v"] = Math.Max(1, J.Int(home["v"], 1));
            home["nextUid"] = Math.Max(1, J.Int(home["nextUid"], 1));
            home["storageCapacity"] = Math.Max(0, J.Int(home["storageCapacity"], ShedLogic.BasisKapazitaet));
            home["movingDay"] = J.IsTrue(home["movingDay"]);
            if (!(home["rooms"] is JObject)) home["rooms"] = new JObject();
            if (!(home["unlockedRooms"] is JArray)) home["unlockedRooms"] = new JArray(DefaultUnlocked);
            if (!(home["storage"] is JArray)) home["storage"] = new JArray();
            if (!(home["flags"] is JObject)) home["flags"] = new JObject();
            var storage = (JArray)home["storage"];
            for (var i = storage.Count - 1; i >= 0; i--)
                if (!(storage[i] is JObject e) || J.Str(e["item"]).Length == 0) storage.RemoveAt(i);
            if (!(home["materials"] is JObject)) home["materials"] = new JObject();
            foreach (var p in ((JObject)home["materials"]).Properties().ToList()) home["materials"][p.Name] = Math.Max(0, J.Int(p.Value));
            if (!(home["blueprints"] is JArray)) home["blueprints"] = new JArray();
            if (!(home["lieferungen"] is JArray)) home["lieferungen"] = new JArray();
            home["shedStufe"] = ShedLogic.ClampStufe(J.Int(home["shedStufe"]));
            if (!(home["presets"] is JObject)) home["presets"] = new JObject();
            home["garden"] = Garden.GardenState.Normalize(home["garden"]);
            Girlanden.Heilen(home);
            LayoutPresets.Heilen(home);
            return home;
        }

        /// <summary>First move-in: stamp the default layout into every room and the start storage (bed lives in storage).</summary>
        public static void EnsureInitialized(GameStateStore gs)
        {
            if (J.Path(gs.State, "home", "rooms") is JObject rooms && rooms.Count > 0) return;
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, SliceId);
                var roomsObj = new JObject();
                var nextUid = Math.Max(1, J.Int(home["nextUid"], 1));
                foreach (var roomId in RoomDefs.Ids())
                {
                    var items = new JArray();
                    foreach (var entry in RoomDefs.DefaultLayout(roomId))
                    {
                        var stamped = (JObject)entry.DeepClone();
                        stamped["uid"] = $"i-{nextUid:D6}";
                        nextUid++;
                        items.Add(stamped);
                    }
                    roomsObj[roomId] = new JObject { ["items"] = items };
                }
                home["rooms"] = roomsObj;
                home["nextUid"] = nextUid;
                if (!(home["flags"] is JObject)) home["flags"] = new JObject();
                var storage = home["storage"] as JArray ?? new JArray();
                foreach (var entry in RoomDefs.DefaultStorage())
                {
                    if (!(entry is JObject e)) continue;
                    var n = Math.Max(1, J.Int(e["count"], 1));
                    for (var i = 0; i < n; i++) StorageLogic.Add(storage, J.Str(e["item"]), J.Str(e["variant"], "default"));
                }
                home["storage"] = storage;
                var unlocked = home["unlockedRooms"] as JArray ?? new JArray(DefaultUnlocked);
                if (!unlocked.Any(t => t.ToString() == "garden")) unlocked.Add("garden");
                home["unlockedRooms"] = unlocked;
            });
            gs.NotifySliceChanged(SliceId);
        }

        public static JArray RoomItems(GameStateStore gs, string roomId) => J.Path(gs.State, "home", "rooms", roomId, "items") as JArray ?? new JArray();

        /// <summary>Rebuilds the room grid; unknown/colliding rows go to storage (unknown ids as __unknown__ + origId) and the healed room is persisted.</summary>
        public static GridData LoadRoomGrid(GameStateStore gs, string roomId)
        {
            var room = RoomDefs.Get(roomId);
            var w = room?.GridW ?? 8;
            var h = room?.GridH ?? 8;
            var entries = (JArray)RoomItems(gs, roomId).DeepClone();
            var defs = FurnitureCatalog.Defs();
            var rebooked = NormalizeCeilingEntries(entries, defs, w, h);
            // garlands are spanned between two ceiling cells (Girlanden), never a grid piece: an old one-cell garland goes back to the storage
            var garlands = new JArray();
            for (var i = entries.Count - 1; i >= 0; i--)
                if (entries[i] is JObject g && defs.TryGetValue(J.Str(g["item"]), out var gd) && Girlanden.Ist(gd)) { garlands.Add(g); entries.RemoveAt(i); }
            var blocked = room != null ? RoomDefs.BlockedCells(room) : new List<(int x, int y)>();
            if (roomId == "garden") blocked.AddRange(Garden.GardenField.BlockedCells(gs)); // the vegetable beds keep their soil
            var result = GridData.FromSave(entries, defs, w, h,
                room != null ? blocked.Select(c => new Cell(c.x, c.y)) : null,
                room != null ? WallDoorSpans(room) : null,
                room != null ? ExteriorWalls(room) : null,
                room != null ? WallWindowSpans(room) : null);
            foreach (var g in garlands) result.Leftovers.Add(g);
            if (result.Leftovers.Count > 0)
            {
                gs.Update(state =>
                {
                    var storage = J.EnsureDict(state, SliceId)["storage"] as JArray;
                    if (storage == null) { storage = new JArray(); state[SliceId]["storage"] = storage; }
                    foreach (var entry in result.Leftovers)
                    {
                        if (!(entry is JObject e)) continue;
                        var itemId = J.Str(e["item"]);
                        if (FurnitureCatalog.Def(itemId) == null)
                            storage.Add(new JObject { ["item"] = StorageLogic.UnknownItem, ["origId"] = itemId, ["variant"] = "default", ["count"] = 1 });
                        else StorageLogic.Add(storage, itemId);
                    }
                });
            }
            if (rebooked || result.Leftovers.Count > 0) SaveRoomGrid(gs, roomId, result.Grid);
            return result.Grid;
        }

        public static Dictionary<string, List<(int, int)>> WallDoorSpans(RoomDefs.Room room)
        {
            var spans = new Dictionary<string, List<(int, int)>>();
            foreach (var door in room.Doors)
            {
                if (!spans.TryGetValue(door.Wall, out var list)) spans[door.Wall] = list = new List<(int, int)>();
                list.Add((door.Offset, door.Offset + RoomDefs.DoorWidth));
            }
            return spans;
        }

        /// <summary>The room's own windows per wall (rooms.json "windows": wall, offset, size in slots).</summary>
        public static Dictionary<string, List<(int, int)>> WallWindowSpans(RoomDefs.Room room)
        {
            var spans = new Dictionary<string, List<(int, int)>>();
            foreach (var win in room.Raw?["windows"] as JArray ?? new JArray())
            {
                if (!(win is JObject w)) continue;
                var wall = J.Str(w["wall"], "N");
                var from = J.Int(w["offset"]);
                if (!spans.TryGetValue(wall, out var list)) spans[wall] = list = new List<(int, int)>();
                list.Add((from, from + Math.Max(1, J.Int(w["size"], 2))));
            }
            return spans;
        }

        public static Dictionary<string, string> ExteriorWalls(RoomDefs.Room room)
        {
            var outp = new Dictionary<string, string>();
            if (room.Raw?["walls"] is JObject walls)
                foreach (var p in walls.Properties())
                    if (Array.IndexOf(GridData.Walls, p.Name) >= 0) outp[p.Name] = p.Value.ToString();
            return outp;
        }

        /// <summary>Legacy wall-anchored ceiling rows → ceiling cells (W13B). Returns true when anything changed.</summary>
        public static bool NormalizeCeilingEntries(JArray entries, IReadOnlyDictionary<string, FurnitureDef> defs, int w, int h)
        {
            var changed = false;
            foreach (var entry in entries)
            {
                if (!(entry is JObject e) || e["wall"] == null) continue;
                if (!defs.TryGetValue(J.Str(e["item"]), out var def) || def.Layer != GridLayer.Ceiling) continue;
                var at = e["at"] as JArray;
                var offset = at != null && at.Count > 0 ? J.Int(at[0]) : 0;
                var cell = CeilingCellForWall(J.Str(e["wall"]), offset, w, h);
                var x = J.ClampI(cell.X, 0, Math.Max(0, w - def.FootprintW));
                var y = J.ClampI(cell.Y, 0, Math.Max(0, h - def.FootprintH));
                e.Remove("wall");
                e["at"] = new JArray(x, y);
                e["rot"] = 0;
                changed = true;
            }
            return changed;
        }

        private static Cell CeilingCellForWall(string wall, int offset, int w, int h)
        {
            switch (wall)
            {
                case "S": return new Cell(offset, h - 1);
                case "W": return new Cell(0, offset);
                case "E": return new Cell(w - 1, offset);
            }
            return new Cell(offset, 0);
        }

        public static void SaveRoomGrid(GameStateStore gs, string roomId, GridData grid)
        {
            gs.Update(state =>
            {
                var rooms = J.EnsureDict(J.EnsureDict(state, SliceId), "rooms");
                var room = J.EnsureDict(rooms, roomId);
                room["items"] = grid.ToItemsArray();
            });
            gs.NotifySliceChanged(SliceId);
        }

        public static JArray Storage(GameStateStore gs) => J.Path(gs.State, "home", "storage") as JArray ?? new JArray();
        public static int StorageCapacity(GameStateStore gs) => J.Int(J.Path(gs.State, "home", "storageCapacity"), 100);
        public static int StoragePointsUsed(GameStateStore gs) => StorageLogic.PointsUsed(Storage(gs), DefsAsJson());

        private static JObject _defsJson;
        public static JObject DefsAsJson()
        {
            if (_defsJson != null) return _defsJson;
            _defsJson = new JObject();
            foreach (var kv in FurnitureCatalog.Defs()) _defsJson[kv.Key] = new JObject { ["lagerwert"] = kv.Value.Lagerwert };
            return _defsJson;
        }

        public static bool StoreItem(GameStateStore gs, string itemId, string variant = StorageLogic.DefaultVariant)
        {
            if (!StorageLogic.CanAdd(Storage(gs), itemId, DefsAsJson(), StorageCapacity(gs))) return false;
            gs.Update(state => StorageLogic.Add(StorageOf(state), itemId, string.IsNullOrEmpty(variant) ? StorageLogic.DefaultVariant : variant));
            gs.NotifySliceChanged(SliceId);
            return true;
        }

        public static bool TakeFromStorage(GameStateStore gs, string itemId) => TakeVariant(gs, itemId) != null;

        /// <summary>Takes one piece of any colour (the plain one first); the colour taken, or null when none is left.</summary>
        public static string TakeVariant(GameStateStore gs, string itemId)
        {
            if (StorageLogic.CountOf(Storage(gs), itemId) <= 0) return null;
            string taken = null;
            gs.Update(state => taken = StorageLogic.TakeAny(StorageOf(state), itemId));
            gs.NotifySliceChanged(SliceId);
            return taken;
        }

        private static JArray StorageOf(JObject state)
        {
            var home = J.EnsureDict(state, SliceId);
            if (!(home["storage"] is JArray)) home["storage"] = new JArray();
            return (JArray)home["storage"];
        }

        public static int ShedStufe(GameStateStore gs) => ShedLogic.ClampStufe(J.Int(J.Path(gs.State, "home", "shedStufe")));

        public static int UpgradeShed(GameStateStore gs)
        {
            var stufe = ShedStufe(gs);
            var preis = ShedLogic.UpgradePreis(stufe);
            var coins = J.Int(J.Path(gs.State, "economy", "coins"));
            if (!ShedLogic.KannUpgraden(stufe, coins)) return stufe;
            var ziel = stufe + 1;
            gs.Update(state =>
            {
                if (!Economy.Spend(J.EnsureDict(state, "economy"), preis, "shed_upgrade")) return;
                var home = J.EnsureDict(state, SliceId);
                home["shedStufe"] = ziel;
                home["storageCapacity"] = ShedLogic.Kapazitaet(ziel);
            });
            gs.NotifySliceChanged(SliceId);
            return ShedStufe(gs);
        }

        public static string NextUid(GameStateStore gs)
        {
            var value = Math.Max(1, J.Int(J.Path(gs.State, "home", "nextUid"), 1));
            gs.Update(state => J.EnsureDict(state, SliceId)["nextUid"] = value + 1);
            return $"i-{value:D6}";
        }

        public static bool Flag(GameStateStore gs, string name) => J.IsTrue(J.Path(gs.State, "home", "flags", name));

        public static void SetFlag(GameStateStore gs, string name, bool value)
        {
            gs.Update(state => J.EnsureDict(J.EnsureDict(state, SliceId), "flags")[name] = value);
            gs.NotifySliceChanged(SliceId);
        }

        public static bool IsRoomUnlocked(GameStateStore gs, string roomId)
        {
            if (RoomDefs.Get(roomId) == null) return false;
            var unlocked = J.Path(gs.State, "home", "unlockedRooms") as JArray;
            return (unlocked != null && unlocked.Any(t => t.ToString() == roomId)) || roomId == "garden";
        }
    }
}
