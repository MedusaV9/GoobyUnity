using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>Room definitions + default layouts (home/rooms.json, home/default_layouts.json) — port of room_defs.gd.</summary>
    public static class RoomDefs
    {
        public const string RoomsPath = "home/rooms";
        public const string LayoutsPath = "home/default_layouts";
        public const int DoorWidth = 2;
        public const int DoorDepth = 2;

        public sealed class Door
        {
            public string Id, To, ToDoor, Wall;
            public int Offset;
        }

        public sealed class Room
        {
            public string Id, NameKey, FloorColor, WallColor;
            public int GridW, GridH;
            public List<Door> Doors = new List<Door>();
            public JObject Raw;
        }

        private static Dictionary<string, Room> _rooms;
        private static JObject _layouts;

        public static IReadOnlyDictionary<string, Room> Rooms()
        {
            EnsureLoaded();
            return _rooms;
        }

        public static Room Get(string roomId) => Rooms().TryGetValue(roomId ?? "", out var r) ? r : null;

        public static List<string> Ids() => Rooms().Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();

        public static Door GetDoor(string roomId, string doorId) => Get(roomId)?.Doors.FirstOrDefault(d => d.Id == doorId);

        /// <summary>Cells a door occupies inside the room (2 wide × 2 deep from the wall).</summary>
        public static List<(int x, int y)> DoorZone(Room room, Door door)
        {
            var cells = new List<(int, int)>();
            for (var i = 0; i < DoorWidth; i++)
            for (var d = 0; d < DoorDepth; d++)
            {
                switch (door.Wall)
                {
                    case "N": cells.Add((door.Offset + i, d)); break;
                    case "S": cells.Add((door.Offset + i, room.GridH - 1 - d)); break;
                    case "W": cells.Add((d, door.Offset + i)); break;
                    case "E": cells.Add((room.GridW - 1 - d, door.Offset + i)); break;
                }
            }
            return cells;
        }

        public static List<(int x, int y)> BlockedCells(Room room)
        {
            var cells = new List<(int, int)>();
            foreach (var door in room.Doors) cells.AddRange(DoorZone(room, door));
            return cells;
        }

        /// <summary>Default placements of a room (save-format entries WITHOUT uid).</summary>
        public static JArray DefaultLayout(string roomId)
        {
            EnsureLoaded();
            return J.Dict(_layouts["rooms"])[roomId] as JArray ?? new JArray();
        }

        /// <summary>Start storage (moving day: the bed lives HERE, not in the bedroom).</summary>
        public static JArray DefaultStorage()
        {
            EnsureLoaded();
            return _layouts["storage"] as JArray ?? new JArray();
        }

        public static HashSet<string> DefaultItemSet()
        {
            var set = new HashSet<string>();
            foreach (var id in Ids())
                foreach (var entry in DefaultLayout(id))
                    if (entry is JObject o) set.Add(J.Str(o["item"]));
            return set;
        }

        public static void ResetCache()
        {
            _rooms = null;
            _layouts = null;
        }

        private static void EnsureLoaded()
        {
            if (_rooms != null) return;
            _rooms = new Dictionary<string, Room>();
            var raw = ContentSource.LoadObject(RoomsPath);
            foreach (var entry in raw["rooms"] as JArray ?? new JArray())
            {
                if (!(entry is JObject o) || string.IsNullOrEmpty(J.Str(o["id"]))) continue;
                var grid = o["grid"] as JArray;
                var room = new Room
                {
                    Id = J.Str(o["id"]), NameKey = J.Str(o["name_key"]),
                    FloorColor = J.Str(o["floor_color"], "#C9A36B"), WallColor = J.Str(o["wall_color"], "#FFF6EC"),
                    GridW = grid != null && grid.Count > 0 ? J.Int(grid[0], 8) : 8,
                    GridH = grid != null && grid.Count > 1 ? J.Int(grid[1], 8) : 8,
                    Raw = o,
                };
                foreach (var d in o["doors"] as JArray ?? new JArray())
                {
                    if (!(d is JObject dd)) continue;
                    room.Doors.Add(new Door { Id = J.Str(dd["id"]), To = J.Str(dd["to"]), ToDoor = J.Str(dd["to_door"]), Wall = J.Str(dd["wall"], "N"), Offset = J.Int(dd["offset"]) });
                }
                _rooms[room.Id] = room;
            }
            _layouts = ContentSource.LoadObject(LayoutsPath);
        }
    }
}
