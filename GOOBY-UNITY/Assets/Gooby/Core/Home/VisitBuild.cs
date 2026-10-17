using System;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// Building while a friend visits (visit_logic.gd apply_build_delta, Doc C §3.4 point 5): every place/remove of the
    /// HOST travels as ROOM_MSG kind BUILD_DELTA {op:"place"|"remove", item, cell:[x,y], rot, roomId} — the format of
    /// visit_service.gd, so Godot and Unity clients understand each other — and the GUEST applies it best effort to its copy
    /// of the host's room (refusals are swallowed; the BUILD_START warning came first). A piece placed over the guest's
    /// own cell sends the guest to the room's first door (visit_room_view.gd spawn_pos). Wall pieces stay out (they have no
    /// floor cell).
    /// </summary>
    public static class VisitBuild
    {
        public const string KindStart = "BUILD_START", KindDelta = "BUILD_DELTA";
        public const string OpPlace = "place", OpRemove = "remove";
        public const string ReasonBadDelta = "bad_delta", ReasonNotFound = "not_found";

        private static readonly GridLayer[] RemoveOrder = { GridLayer.Surface, GridLayer.Floor, GridLayer.Rug };

        public struct Applied
        {
            public bool Ok, Teleport;
            public string Reason, Uid, RoomId;
        }

        public static JObject Delta(string roomId, string op, string itemId, Cell cell, int rot) => new JObject
        {
            ["op"] = op ?? "",
            ["item"] = itemId ?? "",
            ["cell"] = new JArray(cell.X, cell.Y),
            ["rot"] = rot,
            ["roomId"] = roomId ?? "",
        };

        /// <summary>Applies one delta to <paramref name="grid"/>; <paramref name="occupant"/> = the guest's own cell in that room.</summary>
        public static Applied Apply(GridData grid, JObject delta, Cell occupant)
        {
            var o = new Applied { Ok = false, Reason = ReasonBadDelta, Uid = "", RoomId = J.Str(delta?["roomId"]) };
            if (grid == null || delta == null || !(delta["cell"] is JArray c) || c.Count < 2) return o;
            var cell = new Cell(J.Int(c[0]), J.Int(c[1]));
            switch (J.Str(delta["op"]))
            {
                case OpPlace:
                {
                    var def = FurnitureCatalog.Def(J.Str(delta["item"]));
                    if (def == null || def.Layer == GridLayer.Wall || def.Layer == GridLayer.Ceiling)
                    {
                        o.Reason = GridData.ReasonUnknownItem;
                        return o;
                    }
                    var rot = ((J.Int(delta["rot"]) % 4) + 4) % 4;
                    var uid = "visit-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                    var placed = grid.Place(def, cell, rot, uid);
                    if (!placed.Ok)
                    {
                        o.Reason = placed.Reason;
                        return o;
                    }
                    o.Ok = true;
                    o.Reason = GridData.ReasonOk;
                    o.Uid = uid;
                    foreach (var taken in GridData.CellsFor(cell, def.FootprintW, def.FootprintH, rot))
                        if (taken == occupant) { o.Teleport = true; break; }
                    return o;
                }
                case OpRemove:
                {
                    // the host names the piece it took away: a table moved from under a lamp must not take the lamp
                    // instead; senders without an item id get the top-most piece (visit_logic.gd order)
                    var want = J.Str(delta["item"]);
                    foreach (var layer in RemoveOrder)
                    {
                        var uid = grid.ItemAt(cell, layer);
                        if (uid == "" || want.Length > 0 && grid.GetItem(uid)?.Def.Id != want) continue;
                        grid.RemoveItem(uid);
                        o.Ok = true;
                        o.Reason = GridData.ReasonOk;
                        o.Uid = uid;
                        return o;
                    }
                    o.Reason = ReasonNotFound;
                    return o;
                }
            }
            return o;
        }

        /// <summary>Where a guest stands after a conflict: just inside the room's first door, stepped aside from furniture.</summary>
        public static (double x, double y) DoorSpot(RoomDefs.Room room, GridData grid)
        {
            if (room == null) return (0, 0);
            if (room.Doors == null || room.Doors.Count == 0) return (room.GridW * 0.5, room.GridH * 0.5);
            return RoomNavigation.FreeArrival(room, room.Doors[0], grid);
        }
    }
}
