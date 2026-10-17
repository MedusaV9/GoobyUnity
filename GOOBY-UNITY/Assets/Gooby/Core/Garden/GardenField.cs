using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    /// <summary>
    /// Where the vegetable beds (GardenState grid, 1 bed = 1 furniture cell = 0.5 m) lie on the lawn: anchored in
    /// the south-east corner of the garden room with a one-cell margin to the fence, growing west/north as the
    /// garden is upgraded (6×5 → 12×10). Bed cells are blocked for furniture so nothing gets built on the soil.
    /// </summary>
    public static class GardenField
    {
        public const int Margin = 1;

        /// <summary>Top-left furniture cell (x east, y south) of the bed field for a garden of w×h beds.</summary>
        public static (int x, int y) Origin(RoomDefs.Room garden, int bedW, int bedH)
        {
            var roomW = garden?.GridW ?? 28;
            var roomH = garden?.GridH ?? 14;
            return (System.Math.Max(0, roomW - Margin - bedW), System.Math.Max(0, roomH - Margin - bedH));
        }

        public static (int x, int y) Origin(GameStateStore gs)
        {
            var size = GardenState.Slice(gs)["size"] as JArray;
            var w = size != null && size.Count > 0 ? J.Int(size[0], 6) : 6;
            var h = size != null && size.Count > 1 ? J.Int(size[1], 5) : 5;
            return Origin(RoomDefs.Get("garden"), w, h);
        }

        /// <summary>Furniture-grid cells covered by the beds (blocked for building, walkable for Gooby like door zones).</summary>
        public static List<(int x, int y)> BlockedCells(GameStateStore gs)
        {
            var cells = new List<(int, int)>();
            var garden = RoomDefs.Get("garden");
            if (garden == null) return cells;
            var size = GardenState.Slice(gs)["size"] as JArray;
            var w = size != null && size.Count > 0 ? J.Int(size[0], 6) : 6;
            var h = size != null && size.Count > 1 ? J.Int(size[1], 5) : 5;
            var (ox, oy) = Origin(garden, w, h);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (ox + x < garden.GridW && oy + y < garden.GridH) cells.Add((ox + x, oy + y));
            return cells;
        }

        /// <summary>Bed cell (GardenState grid) for a furniture cell, or null when the cell is plain lawn.</summary>
        public static Cell? BedAt(GameStateStore gs, Cell furnitureCell)
        {
            var size = GardenState.Slice(gs)["size"] as JArray;
            var w = size != null && size.Count > 0 ? J.Int(size[0], 6) : 6;
            var h = size != null && size.Count > 1 ? J.Int(size[1], 5) : 5;
            var (ox, oy) = Origin(RoomDefs.Get("garden"), w, h);
            var bx = furnitureCell.X - ox;
            var by = furnitureCell.Y - oy;
            if (bx < 0 || by < 0 || bx >= w || by >= h) return null;
            return new Cell(bx, by);
        }

        /// <summary>Growth stage 0..Stufen of a bed and whether it is watered right now; (-1, false) for an empty bed.</summary>
        public static (int stage, int stages, bool watered, bool ripe) Status(JObject cellData, double nowS)
        {
            var crop = GardenCrops.Crop(J.Str(cellData?["crop"]));
            var watered = cellData != null && J.Num(cellData["watered_until"]) > nowS;
            if (crop == null) return (-1, 0, watered, false);
            var stage = J.Int(cellData["stage"]);
            return (stage, crop.Stufen, watered, stage >= crop.Stufen);
        }
    }
}
