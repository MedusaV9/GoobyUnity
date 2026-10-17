using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.State;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The vegetable beds of GardenState on the lawn: one raised soil tile per bed (darker while watered), a
    /// wooden edging around the field, and the crop in its growth stage — sprout, growing plant, ripe fruit in the
    /// crop's colour. Every tile carries a collider + BedMarker so a tap on it opens the bed sheet.
    /// </summary>
    public sealed class GardenBeds
    {
        public GameObject Root { get; }
        private readonly Vector3 _origin;
        private readonly int _roomH;
        private readonly Dictionary<Cell, GameObject> _tiles = new Dictionary<Cell, GameObject>();
        private GameObject _crops;
        private GameObject _edging;
        private (int x, int y) _fieldOrigin;
        private int _w, _h;

        private const float Cell = 0.5f;
        private const float SoilHeight = 0.09f;

        /// <summary>The garage on the field (rebuilt with every refresh), null while none is built.</summary>
        public GarageRig Garage { get; private set; }

        /// <summary>The roller door's state machine (GarageLogic): runtime only, it outlives the rebuilds but not the session.</summary>
        public string GarageZustand = GarageLogic.Zu;

        public GardenBeds(Transform parent, Vector3 gardenOrigin, int roomH)
        {
            _origin = gardenOrigin;
            _roomH = roomH;
            Root = new GameObject("Beds");
            Root.transform.SetParent(parent, false);
        }

        /// <summary>World centre of a bed tile (top of the soil).</summary>
        public Vector3 TileCenter(Cell bed)
        {
            var fx = _fieldOrigin.x + bed.X;
            var fy = _fieldOrigin.y + bed.Y;
            return new Vector3(_origin.x + (fx + 0.5f) * Cell, SoilHeight, _origin.z + _roomH * Cell - (fy + 0.5f) * Cell);
        }

        /// <summary>
        /// Rebuilds soil, edging, crops and structures from the save (call after planting/watering/harvest/tick/
        /// building). Returns true when the field changed size (the garden was expanded) so callers can re-sync
        /// the lawn's furniture and walk grid.
        /// </summary>
        public bool Refresh(GameStateStore gs, double nowS)
        {
            var grid = GardenState.Grid(gs);
            var t = Root.transform;
            var resized = false;
            if (_w != grid.Width || _h != grid.Height || _tiles.Count == 0)
            {
                resized = _tiles.Count > 0;
                foreach (var go in _tiles.Values) if (go != null) Object.Destroy(go);
                _tiles.Clear();
                if (_edging != null) Object.Destroy(_edging);
                _w = grid.Width; _h = grid.Height;
                _fieldOrigin = GardenField.Origin(RoomDefs.Get("garden"), _w, _h);
                var soil = ArtLibrary.Solid(ArtLibrary.Hex("#7A5236"), 0.05f);
                for (var y = 0; y < _h; y++)
                for (var x = 0; x < _w; x++)
                {
                    var bed = new Cell(x, y);
                    var c = TileCenter(bed);
                    var tile = ArtLibrary.Box(t, "Bed:" + x + ":" + y, c + Vector3.down * SoilHeight * 0.5f, new Vector3(Cell - 0.06f, SoilHeight, Cell - 0.06f), soil, false);
                    tile.AddComponent<BoxCollider>().size = new Vector3(1f, 1.4f, 1f);
                    tile.AddComponent<BedMarker>().Bed = bed;
                    _tiles[bed] = tile;
                }
                _edging = new GameObject("Edging");
                _edging.transform.SetParent(t, false);
                var wood = ArtLibrary.Solid(ArtLibrary.Hex("#B98D62"), 0.25f);
                var min = TileCenter(new Cell(0, 0)); var max = TileCenter(new Cell(_w - 1, _h - 1));
                var cx = (min.x + max.x) * 0.5f; var cz = (min.z + max.z) * 0.5f;
                var fw = _w * Cell + 0.1f; var fd = _h * Cell + 0.1f;
                ArtLibrary.Box(_edging.transform, "EdgeN", new Vector3(cx, 0.07f, cz + fd * 0.5f), new Vector3(fw + 0.08f, 0.14f, 0.08f), wood, false);
                ArtLibrary.Box(_edging.transform, "EdgeS", new Vector3(cx, 0.07f, cz - fd * 0.5f), new Vector3(fw + 0.08f, 0.14f, 0.08f), wood, false);
                ArtLibrary.Box(_edging.transform, "EdgeW", new Vector3(cx - fw * 0.5f, 0.07f, cz), new Vector3(0.08f, 0.14f, fd), wood, false);
                ArtLibrary.Box(_edging.transform, "EdgeE", new Vector3(cx + fw * 0.5f, 0.07f, cz), new Vector3(0.08f, 0.14f, fd), wood, false);
            }
            if (_crops != null) Object.Destroy(_crops);
            _crops = new GameObject("Crops");
            _crops.transform.SetParent(t, false);
            var dry = ArtLibrary.Solid(ArtLibrary.Hex("#7A5236"), 0.05f);
            var wet = ArtLibrary.Solid(ArtLibrary.Hex("#4E3220"), 0.2f);
            var sprinkled = grid.SprinklerCells();
            foreach (var kv in _tiles)
            {
                var data = grid.Cell(kv.Key);
                // garden_grid.gd cell kinds: a "path" cell is flagstone to walk on, not soil to plant — no bed sheet on a tap
                var path = J.Str(data["kind"]) == "path";
                kv.Value.GetComponent<BoxCollider>().enabled = !path;
                if (path) { kv.Value.GetComponent<Renderer>().sharedMaterial = Flagstone(); continue; }
                var (stage, stages, watered, ripe) = GardenField.Status(data, nowS);
                kv.Value.GetComponent<Renderer>().sharedMaterial = watered || sprinkled.Contains(kv.Key) ? wet : dry;
                if (stage < 0) continue;
                BuildCrop(_crops.transform, TileCenter(kv.Key), GardenCrops.Crop(J.Str(data["crop"])), stage, stages, ripe);
            }
            Garage = null;
            foreach (var s in grid.Structures) BuildStructure(_crops.transform, grid, s, gs, nowS);
            foreach (var e in grid.Edges) BuildFence(_crops.transform, e);
            return resized;
        }

        private static Material _flagstone, _fenceWood;
        private static Material Flagstone() => _flagstone ??= ArtLibrary.PbrSlab("paving", new Vector2(Cell, Cell), 0.5f, ArtLibrary.Hex("#CFC4B8"), 0.12f, 0.8f);

        /// <summary>
        /// A fence edge of the GardenGrid (garden_grid.gd `edges`): "E" runs along the north side of the cells from
        /// <c>From</c> eastwards, "S" along their west side southwards — Len cells long. Built as a little picket run
        /// (posts every 12 cm, two rails) in fence wood, so an edge of any length fits exactly.
        /// </summary>
        private void BuildFence(Transform parent, GardenEdge e)
        {
            _fenceWood ??= ArtLibrary.Pbr("wood_planks", 0.6f, ArtLibrary.Hex("#C9A272"), 0.18f, 0.7f);
            var horizontal = e.Dir == "E";
            var start = TileCenter(e.From);
            var span = e.Len * Cell;
            var centre = horizontal
                ? new Vector3(start.x - Cell * 0.5f + span * 0.5f, 0f, start.z + Cell * 0.5f)
                : new Vector3(start.x - Cell * 0.5f, 0f, start.z + Cell * 0.5f - span * 0.5f);
            var g = new GameObject("Fence:" + e.From.X + ":" + e.From.Y + ":" + e.Dir + ":" + e.Len);
            g.transform.SetParent(parent, false);
            g.transform.position = centre;
            // ArtLibrary places in world space: the run's axis is x for "E", z for "S"
            var along = horizontal ? Vector3.right : Vector3.forward;
            var across = horizontal ? Vector3.forward : Vector3.right;
            Vector3 Size(float length, float thick, float h) => horizontal ? new Vector3(length, h, thick) : new Vector3(thick, h, length);
            const float height = 0.42f;
            var posts = Mathf.Max(2, Mathf.RoundToInt(span / 0.12f) + 1);
            for (var i = 0; i < posts; i++)
            {
                var x = -span * 0.5f + span * i / (posts - 1);
                ArtLibrary.Box(g.transform, "Post", centre + along * x + Vector3.up * (height * 0.5f), Size(0.035f, 0.03f, height), _fenceWood, false);
            }
            foreach (var y in new[] { height * 0.35f, height * 0.78f })
                ArtLibrary.Box(g.transform, "Rail", centre + across * 0.02f + Vector3.up * y, Size(span + 0.02f, 0.02f, 0.025f), _fenceWood, false);
        }

        /// <summary>World-space footprint of a structure: centre on the soil and size in metres.</summary>
        private (Vector3 center, Vector2 size) Footprint(GardenStructure s)
        {
            var cells = GardenGrid.StructureCells(s.Kind, s.At, s.Rot);
            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var max = new Vector3(float.MinValue, 0f, float.MinValue);
            foreach (var c in cells)
            {
                var p = TileCenter(c);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            return (new Vector3((min.x + max.x) * 0.5f, SoilHeight, (min.z + max.z) * 0.5f), new Vector2(max.x - min.x + Cell, max.z - min.z + Cell));
        }

        /// <summary>
        /// GardenState structures on the field: glass greenhouse (door on its outer side, beds inside stay tappable),
        /// sprinkler post with its water ring, wood tree (logs at the foot when ripe), shed and workshop huts. Solid
        /// structures carry a collider + BedMarker of their anchor cell so a tap opens their sheet.
        /// </summary>
        private void BuildStructure(Transform parent, GardenGrid grid, GardenStructure s, GameStateStore gs, double nowS)
        {
            var (c, size) = Footprint(s);
            var group = new GameObject("Structure:" + s.Kind);
            group.transform.SetParent(parent, false);
            group.transform.position = c;
            var g = group.transform;
            switch (s.Kind)
            {
                case "gewaechshaus": BuildGreenhouse(g, c, size, s); return; // glass has no collider: taps reach the beds inside
                case "sprinkler": BuildSprinkler(g, c, grid, s); break;
                case "baum": BuildTree(g, c, GardenWorld.BaumReif(gs, s.At, nowS)); break;
                case "werkstatt": BuildHut(g, c, size, ArtLibrary.Hex("#E7C9A3"), ArtLibrary.Hex("#7A5237"), workshop: true); break;
                case GarageLogic.Kind: Garage = BuildGarage(g, s, gs); break;
                default: BuildHut(g, c, size, ArtLibrary.Hex("#F3E4D2"), ArtLibrary.Hex("#C96F5A"), workshop: false); break;
            }
            var hit = group.AddComponent<BoxCollider>();
            var tall = s.Kind == GarageLogic.Kind ? 1.3f : 1.0f;
            hit.center = new Vector3(0f, tall * 0.5f - 0.05f, 0f);
            hit.size = new Vector3(size.x, tall, size.y);
            group.AddComponent<BedMarker>().Bed = s.At;
        }

        /// <summary>
        /// The garage (Blender `garage` + `garage_door`) at half scale on its 2×3 cells, the door towards the camera
        /// (south; a turned footprint faces the lawn in the west), and the car in use — paint and body shape as on the
        /// street — parked inside, nose to the door. The door snaps to where its state machine ended.
        /// </summary>
        private GarageRig BuildGarage(Transform g, GardenStructure s, GameStateStore gs)
        {
            var rig = g.gameObject.AddComponent<GarageRig>();
            var model = new GameObject("GarageModel").transform;
            model.SetParent(g, false);
            model.localRotation = Quaternion.Euler(0f, s.Rot % 2 == 1 ? -90f : 180f, 0f);
            model.localScale = Vector3.one * GarageRig.ModelScale;
            Scenery.Place(model, "garage", Vector3.zero);
            rig.Model = model;
            rig.Door = Scenery.Place(model, "garage_door", new Vector3(0f, GarageRig.DoorTop, GarageRig.DoorZ)).transform;
            var (auto, farbe) = AutoKatalog.AktivesAuto(gs);
            var car = new GameObject("GarageCar").transform;
            car.SetParent(model, false);
            car.localScale = CityScene.CarShape(auto?.Id) * (GarageRig.CarLength / GarageRig.HeroCarLength);
            car.gameObject.AddComponent<VehicleRig>();
            CityScene.TintCar(car, ArtLibrary.Hex(farbe));
            _ = AssetLoader.InstanceAsync(AssetLoader.VehiclePath("car_hero"), car, "car_hero");
            rig.Car = car;
            rig.SetCarRun(0f);
            rig.SetOpen((float)GarageLogic.ZielAnteil(GarageLogic.Ende(GarageZustand)));
            GarageZustand = GarageLogic.Ende(GarageZustand);
            return rig;
        }

        private static void BuildGreenhouse(Transform g, Vector3 c, Vector2 size, GardenStructure s)
        {
            var frame = ArtLibrary.Solid(Color.white, 0.4f);
            // pale, mostly see-through glass: the beds and crops inside stay readable, the roof does not blow out in the sun
            var glass = ArtLibrary.Transparent(new Color(0.72f, 0.9f, 1f, 0.22f));
            var roofGlass = ArtLibrary.Transparent(new Color(0.72f, 0.9f, 1f, 0.16f));
            const float wallH = 0.62f, ridgeH = 0.98f, post = 0.035f;
            var hx = size.x * 0.5f; var hz = size.y * 0.5f;
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                ArtLibrary.Box(g, "Post", c + new Vector3(sx * (hx - post * 0.5f), wallH * 0.5f, sz * (hz - post * 0.5f)), new Vector3(post, wallH, post), frame, false);
            // glass walls and a gabled glass roof (ridge along the longer side)
            ArtLibrary.Box(g, "GlassN", c + new Vector3(0f, wallH * 0.5f, hz), new Vector3(size.x, wallH, 0.01f), glass, false);
            ArtLibrary.Box(g, "GlassS", c + new Vector3(0f, wallH * 0.5f, -hz), new Vector3(size.x, wallH, 0.01f), glass, false);
            ArtLibrary.Box(g, "GlassW", c + new Vector3(-hx, wallH * 0.5f, 0f), new Vector3(0.01f, wallH, size.y), glass, false);
            ArtLibrary.Box(g, "GlassE", c + new Vector3(hx, wallH * 0.5f, 0f), new Vector3(0.01f, wallH, size.y), glass, false);
            // top rail around the walls (four bars, the roof stays open glass above them)
            ArtLibrary.Box(g, "RailN", c + new Vector3(0f, wallH, hz), new Vector3(size.x + 0.02f, post, post), frame, false);
            ArtLibrary.Box(g, "RailS", c + new Vector3(0f, wallH, -hz), new Vector3(size.x + 0.02f, post, post), frame, false);
            ArtLibrary.Box(g, "RailW", c + new Vector3(-hx, wallH, 0f), new Vector3(post, post, size.y + 0.02f), frame, false);
            ArtLibrary.Box(g, "RailE", c + new Vector3(hx, wallH, 0f), new Vector3(post, post, size.y + 0.02f), frame, false);
            var alongX = size.x >= size.y;
            var half = alongX ? hz : hx;
            var slope = Mathf.Sqrt(half * half + (ridgeH - wallH) * (ridgeH - wallH));
            var tilt = Mathf.Atan2(ridgeH - wallH, half) * Mathf.Rad2Deg;
            foreach (var side in new[] { -1f, 1f })
            {
                var mid = c + new Vector3(alongX ? 0f : side * half * 0.5f, (wallH + ridgeH) * 0.5f, alongX ? side * half * 0.5f : 0f);
                var paneRot = alongX ? Quaternion.Euler(side * tilt, 0f, 0f) : Quaternion.Euler(0f, 0f, -side * tilt); // +Z / +X end dips outward
                var pane = ArtLibrary.Box(g, "Roof", mid, new Vector3(alongX ? size.x + 0.04f : slope, 0.012f, alongX ? slope : size.y + 0.04f), roofGlass, false);
                pane.transform.rotation = paneRot;
                // white glazing bar down the middle of each pane
                var bar = ArtLibrary.Box(g, "RoofBar", mid, new Vector3(alongX ? post * 0.8f : slope, post * 0.8f, alongX ? slope : post * 0.8f), frame, false);
                bar.transform.rotation = paneRot;
            }
            ArtLibrary.Box(g, "Ridge", c + new Vector3(0f, ridgeH, 0f), new Vector3(alongX ? size.x + 0.04f : post, post, alongX ? post : size.y + 0.04f), frame, false);
            // door: white frame on the outer face of the door cell
            if (!s.Door.HasValue) return;
            var cells = GardenGrid.StructureCells(s.Kind, s.At, s.Rot);
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var cell in cells) { minX = Mathf.Min(minX, cell.X); maxX = Mathf.Max(maxX, cell.X); minY = Mathf.Min(minY, cell.Y); maxY = Mathf.Max(maxY, cell.Y); }
            var d = s.Door.Value;
            // the door cell's offset from the footprint centre (grid y runs south = -Z)
            var local = new Vector3((d.X - (minX + maxX) * 0.5f) * Cell, 0f, -(d.Y - (minY + maxY) * 0.5f) * Cell);
            Vector3 dc;
            Quaternion rot;
            if (d.Y == maxY) { dc = c + new Vector3(local.x, 0f, -hz); rot = Quaternion.identity; }       // south face (towards the camera)
            else if (d.Y == minY) { dc = c + new Vector3(local.x, 0f, hz); rot = Quaternion.identity; }
            else if (d.X == maxX) { dc = c + new Vector3(hx, 0f, local.z); rot = Quaternion.Euler(0f, 90f, 0f); }
            else { dc = c + new Vector3(-hx, 0f, local.z); rot = Quaternion.Euler(0f, 90f, 0f); }
            var doorFrame = ArtLibrary.Box(g, "DoorFrame", dc + Vector3.up * 0.26f, new Vector3(0.3f, 0.52f, post), frame, false);
            doorFrame.transform.rotation = rot;
            var doorGlass = ArtLibrary.Box(g, "DoorGlass", dc + Vector3.up * 0.26f, new Vector3(0.24f, 0.46f, post * 0.6f), ArtLibrary.Transparent(new Color(0.6f, 0.85f, 1f, 0.5f)), false);
            doorGlass.transform.rotation = rot;
        }

        private void BuildSprinkler(Transform g, Vector3 c, GardenGrid grid, GardenStructure s)
        {
            var metal = ArtLibrary.Solid(ArtLibrary.Hex("#8FA3B5"), 0.6f);
            var teal = ArtLibrary.Solid(ArtLibrary.Hex("#5FBFB0"), 0.5f);
            ArtLibrary.Cylinder(g, "Post", c + Vector3.up * 0.18f, 0.025f, 0.36f, metal, false);
            ArtLibrary.Sphere(g, "Head", c + Vector3.up * 0.38f, 0.05f, teal, false);
            for (var i = 0; i < 4; i++)
            {
                var a = i * 90f * Mathf.Deg2Rad;
                var arm = ArtLibrary.Cylinder(g, "Arm", c + new Vector3(Mathf.Cos(a) * 0.08f, 0.39f, Mathf.Sin(a) * 0.08f), 0.008f, 0.12f, metal, false);
                arm.transform.rotation = Quaternion.Euler(0f, -i * 90f, 90f);
            }
            // reach of the sprinkler (radius 1 bed): a soft water sheen on every bed it keeps wet (inside the field only)
            var sheen = ArtLibrary.Transparent(new Color(0.55f, 0.8f, 1f, 0.16f));
            for (var dy = -GardenGrid.SprinklerRadius; dy <= GardenGrid.SprinklerRadius; dy++)
            for (var dx = -GardenGrid.SprinklerRadius; dx <= GardenGrid.SprinklerRadius; dx++)
            {
                var cell = s.At + new Cell(dx, dy);
                if (!grid.InBounds(cell)) continue;
                ArtLibrary.Quad(g, "Sheen", TileCenter(cell) + Vector3.up * 0.004f, Quaternion.Euler(-90f, 0f, 0f), new Vector2(Cell - 0.06f, Cell - 0.06f), sheen);
            }
        }

        private static void BuildTree(Transform g, Vector3 c, bool ripe)
        {
            var bark = ArtLibrary.Solid(ArtLibrary.Hex("#8B6044"), 0.2f);
            var leaf = ArtLibrary.Solid(ArtLibrary.Hex("#6FBF5A"), 0.1f);
            var leafDark = ArtLibrary.Solid(ArtLibrary.Hex("#4F9C44"), 0.1f);
            ArtLibrary.Cylinder(g, "Trunk", c + Vector3.up * 0.25f, 0.045f, 0.5f, bark);
            ArtLibrary.Sphere(g, "Crown", c + Vector3.up * 0.62f, 0.24f, leaf);
            ArtLibrary.Sphere(g, "CrownTop", c + new Vector3(0.06f, 0.8f, -0.04f), 0.16f, leafDark);
            ArtLibrary.Sphere(g, "CrownSide", c + new Vector3(-0.14f, 0.55f, 0.08f), 0.15f, leafDark);
            if (!ripe) return;
            for (var i = 0; i < 3; i++)
            {
                var log = ArtLibrary.Cylinder(g, "Log", c + new Vector3(-0.12f + i * 0.09f, 0.03f, -0.16f), 0.03f, 0.16f, bark, false);
                log.transform.rotation = Quaternion.Euler(90f, 20f * i, 0f);
            }
        }

        /// <summary>Shed (cream, terracotta roof) or workshop (timber, dark roof, tool board over the door).</summary>
        private static void BuildHut(Transform g, Vector3 c, Vector2 size, Color wallColor, Color roofColor, bool workshop)
        {
            var wall = ArtLibrary.Solid(wallColor, 0.1f);
            var roof = ArtLibrary.Solid(roofColor, 0.2f);
            var wood = ArtLibrary.Solid(ArtLibrary.Hex("#7A5237"), 0.3f);
            const float wallH = 0.6f;
            var w = size.x - 0.06f; var d = size.y - 0.06f;
            ArtLibrary.Box(g, "Walls", c + Vector3.up * wallH * 0.5f, new Vector3(w, wallH, d), wall);
            var alongX = w >= d;
            var half = (alongX ? d : w) * 0.5f + 0.06f;
            const float rise = 0.3f;
            var slope = Mathf.Sqrt(half * half + rise * rise);
            var tilt = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            foreach (var side in new[] { -1f, 1f })
            {
                var mid = c + new Vector3(alongX ? 0f : side * half * 0.5f, wallH + rise * 0.5f, alongX ? side * half * 0.5f : 0f);
                var pane = ArtLibrary.Box(g, "Roof", mid, new Vector3(alongX ? w + 0.12f : slope, 0.04f, alongX ? slope : d + 0.12f), roof);
                pane.transform.rotation = alongX ? Quaternion.Euler(side * tilt, 0f, 0f) : Quaternion.Euler(0f, 0f, -side * tilt); // +Z / +X end dips outward
            }
            // door on the south face, a small window beside it
            ArtLibrary.Box(g, "Door", c + new Vector3(-w * 0.15f, 0.22f, -d * 0.5f - 0.005f), new Vector3(0.2f, 0.44f, 0.02f), wood, false);
            ArtLibrary.Box(g, "Window", c + new Vector3(w * 0.22f, 0.36f, -d * 0.5f - 0.005f), new Vector3(0.16f, 0.14f, 0.02f), ArtLibrary.Solid(ArtLibrary.Hex("#CFE6FF"), 0.9f), false);
            if (!workshop) return;
            // tool board with three hanging tools
            ArtLibrary.Box(g, "Board", c + new Vector3(w * 0.22f, 0.2f, -d * 0.5f - 0.02f), new Vector3(0.26f, 0.08f, 0.03f), wood, false);
            foreach (var (x, col) in new[] { (-0.08f, "#8FA3B5"), (0f, "#FF8B8B"), (0.08f, "#E8C36A") })
                ArtLibrary.Box(g, "Tool", c + new Vector3(w * 0.22f + x, 0.13f, -d * 0.5f - 0.03f), new Vector3(0.03f, 0.1f, 0.02f), ArtLibrary.Solid(ArtLibrary.Hex(col), 0.5f), false);
        }

        /// <summary>Sprout → leafy plant that gains height per stage → ripe: fruit in the crop colour on top.</summary>
        private static void BuildCrop(Transform parent, Vector3 at, CropDef crop, int stage, int stages, bool ripe)
        {
            var leaf = ArtLibrary.Solid(ArtLibrary.Hex("#6FBF5A"), 0.1f);
            var leafDark = ArtLibrary.Solid(ArtLibrary.Hex("#4F9C44"), 0.1f);
            var fruit = ArtLibrary.Solid(ArtLibrary.Hex(crop?.Farbe ?? "#F2913D"), 0.35f);
            var grow = Mathf.Clamp01(stages > 0 ? stage / (float)stages : 0f);
            var group = new GameObject("Crop:" + (crop?.Id ?? "?"));
            group.transform.SetParent(parent, false);
            group.transform.position = at;
            var g = group.transform;
            if (stage == 0)
            {
                ArtLibrary.Sphere(g, "Sprout", at + Vector3.up * 0.04f, 0.035f, leaf, false).transform.localScale = new Vector3(0.05f, 0.08f, 0.05f);
                foreach (var s in new[] { -1f, 1f })
                    ArtLibrary.Sphere(g, "Leaf", at + new Vector3(s * 0.03f, 0.08f, 0f), 0.03f, leaf, false).transform.localScale = new Vector3(0.05f, 0.02f, 0.03f);
                return;
            }
            var height = Mathf.Lerp(0.12f, 0.3f, grow);
            ArtLibrary.Cylinder(g, "Stem", at + Vector3.up * height * 0.5f, 0.012f, height, leafDark, false);
            for (var i = 0; i < 3 + stage; i++)
            {
                var a = i * 137f * Mathf.Deg2Rad;
                var r = 0.07f + 0.04f * grow;
                var leafGo = ArtLibrary.Sphere(g, "Leaf", at + new Vector3(Mathf.Cos(a) * r, height * (0.45f + 0.15f * (i % 2)), Mathf.Sin(a) * r), 0.05f, i % 2 == 0 ? leaf : leafDark, false);
                leafGo.transform.localScale = new Vector3(0.11f + 0.05f * grow, 0.035f, 0.07f + 0.03f * grow);
                leafGo.transform.rotation = Quaternion.Euler(0f, -i * 137f, 18f);
            }
            if (ripe)
            {
                var n = Mathf.Clamp(crop?.Ernte ?? 1, 1, 4);
                for (var i = 0; i < n; i++)
                {
                    var a = i / (float)n * Mathf.PI * 2f;
                    ArtLibrary.Sphere(g, "Fruit", at + new Vector3(Mathf.Cos(a) * 0.07f, height + 0.03f, Mathf.Sin(a) * 0.07f), 0.045f, fruit, false);
                }
            }
            else if (stage >= stages - 1 && stages > 1)
                ArtLibrary.Sphere(g, "Bud", at + Vector3.up * (height + 0.02f), 0.025f, fruit, false); // nearly there: first colour shows
        }
    }

    /// <summary>Tag on a bed tile so a world tap resolves to its GardenState cell.</summary>
    public sealed class BedMarker : MonoBehaviour
    {
        [System.NonSerialized] public Cell Bed;
    }
}
