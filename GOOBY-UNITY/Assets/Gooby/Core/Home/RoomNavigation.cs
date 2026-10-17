using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.State;

namespace Gooby.Core.Home
{
    /// <summary>
    /// Walking between rooms (the Godot house switched scenes per door): which door a cell belongs to, where a
    /// walker arrives after passing through it, and the persisted current room. Engine-free so the Unity world,
    /// the HUD and the tests share one notion of "through the door".
    /// </summary>
    public static class RoomNavigation
    {
        public const string DefaultRoom = "living";

        /// <summary>The door whose 2×2 zone contains the cell, or null when the cell is plain floor.</summary>
        public static RoomDefs.Door DoorAt(RoomDefs.Room room, Cell cell)
        {
            if (room == null) return null;
            foreach (var door in room.Doors)
                if (RoomDefs.DoorZone(room, door).Any(c => c.x == cell.X && c.y == cell.Y)) return door;
            return null;
        }

        /// <summary>The door in the target room that the given door leads to (falls back to any door pointing back).</summary>
        public static RoomDefs.Door Counterpart(RoomDefs.Door door)
        {
            var target = RoomDefs.Get(door?.To);
            if (target == null) return null;
            return target.Doors.FirstOrDefault(d => d.Id == door.ToDoor) ?? target.Doors.FirstOrDefault(d => d.To == RoomOf(door)?.Id);
        }

        /// <summary>The room a door belongs to.</summary>
        public static RoomDefs.Room RoomOf(RoomDefs.Door door) =>
            door == null ? null : RoomDefs.Rooms().Values.FirstOrDefault(r => r.Doors.Contains(door));

        /// <summary>
        /// Cell centre (in grid units, x east / y south) where a walker stands after coming through a door: the middle
        /// of the door's width, one cell past the door zone so the same door does not fire again immediately.
        /// </summary>
        public static (double x, double y) ArrivalPoint(RoomDefs.Room room, RoomDefs.Door door)
        {
            var mid = door.Offset + RoomDefs.DoorWidth * 0.5;
            var depth = RoomDefs.DoorDepth + 0.5;
            switch (door.Wall)
            {
                case "N": return (mid, depth);
                case "S": return (mid, room.GridH - depth);
                case "W": return (depth, mid);
                default: return (room.GridW - depth, mid);
            }
        }

        /// <summary>
        /// Arrival point that respects the furniture: the ideal cell if it is free, otherwise the nearest walkable
        /// cell (breadth-first, four-connected) outside any door zone — so nobody lands on the bed by the door.
        /// </summary>
        public static (double x, double y) FreeArrival(RoomDefs.Room room, RoomDefs.Door door, GridData grid)
        {
            var (ix, iy) = ArrivalPoint(room, door);
            return NearestFree(room, grid, ix, iy);
        }

        /// <summary>
        /// A grid point (cells, may be fractional) if its cell is walkable and outside every door zone, otherwise the
        /// centre of the nearest such cell (breadth-first, four-connected).
        /// </summary>
        public static (double x, double y) NearestFree(RoomDefs.Room room, GridData grid, double x, double y)
        {
            if (grid == null) return (x, y);
            var start = new Cell((int)System.Math.Floor(x), (int)System.Math.Floor(y));
            var zones = new HashSet<Cell>();
            foreach (var d in room.Doors) foreach (var (zx, zy) in RoomDefs.DoorZone(room, d)) zones.Add(new Cell(zx, zy));
            if (grid.Walkable(start) && !zones.Contains(start)) return (x, y);
            var seen = new HashSet<Cell> { start };
            var queue = new Queue<Cell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (grid.Walkable(c) && !zones.Contains(c)) return (c.X + 0.5, c.Y + 0.5);
                foreach (var n in new[] { new Cell(c.X + 1, c.Y), new Cell(c.X - 1, c.Y), new Cell(c.X, c.Y + 1), new Cell(c.X, c.Y - 1) })
                    if (grid.InBounds(n) && seen.Add(n)) queue.Enqueue(n);
            }
            return (x, y);
        }

        /// <summary>Whether a door may be used: the target room exists and is unlocked (the garden always is).</summary>
        public static bool CanPass(GameStateStore gs, RoomDefs.Door door) =>
            door != null && RoomDefs.Get(door.To) != null && HomeState.IsRoomUnlocked(gs, door.To);

        /// <summary>The persisted room the player is in (home.currentRoom), validated against the room table.</summary>
        public static string CurrentRoom(GameStateStore gs)
        {
            var id = J.Str(J.Path(gs.State, HomeState.SliceId, "currentRoom"), DefaultRoom);
            return RoomDefs.Get(id) != null && HomeState.IsRoomUnlocked(gs, id) ? id : DefaultRoom;
        }

        public static void SetCurrentRoom(GameStateStore gs, string roomId)
        {
            if (RoomDefs.Get(roomId) == null) return;
            gs.Update(state => J.EnsureDict(state, HomeState.SliceId)["currentRoom"] = roomId);
        }

        /// <summary>Every door must point to an existing room whose counterpart door leads back — the house graph is closed.</summary>
        public static List<string> ValidateGraph()
        {
            var problems = new List<string>();
            foreach (var room in RoomDefs.Rooms().Values)
            foreach (var door in room.Doors)
            {
                var target = RoomDefs.Get(door.To);
                if (target == null) { problems.Add($"{room.Id}/{door.Id}: room '{door.To}' missing"); continue; }
                var back = target.Doors.FirstOrDefault(d => d.Id == door.ToDoor);
                if (back == null) problems.Add($"{room.Id}/{door.Id}: door '{door.ToDoor}' missing in '{door.To}'");
                else if (back.To != room.Id) problems.Add($"{room.Id}/{door.Id}: '{back.Id}' leads to '{back.To}', not back");
                var zone = RoomDefs.DoorZone(room, door);
                if (zone.Any(c => c.x < 0 || c.y < 0 || c.x >= room.GridW || c.y >= room.GridH))
                    problems.Add($"{room.Id}/{door.Id}: zone leaves the grid");
            }
            return problems;
        }
    }
}
