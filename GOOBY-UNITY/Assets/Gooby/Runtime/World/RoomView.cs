using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.State;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Renders one room of the save (home.rooms.&lt;id&gt;.items) — floor/walls from RoomDefs, every placed item as a
    /// Blender prop (props_manifest archetype via glTFast) with a category-coloured placeholder until it loads.
    /// The same BuildSession that runs the garden can edit this room because both use the RoomDefs grid mapping:
    /// cell (x,y) → Origin + (x·0.5, 0, y·0.5).
    ///
    /// Shell look (board 08/09): honey plank floor, cream walls over a white wainscot with a wooden rail and
    /// skirting, a panelled door and a curtained window on the north wall, wall items (pictures, shelves) on
    /// their wall slots. The camera sits south of the room looking +Z, so the south wall is left open.
    /// </summary>
    public sealed class RoomView
    {
        public string RoomId { get; }
        public Vector3 Origin { get; }
        public GameObject Root { get; }
        public RoomDefs.Room Def { get; }

        private const float WallHeight = PropPlaceholder.CeilingHeight;
        private const float WallThickness = 0.16f;
        private const float WainscotHeight = 0.92f;

        private readonly Dictionary<string, GameObject> _views = new Dictionary<string, GameObject>();
        /// <summary>The placed pieces by uid and the ones whose Blender model has replaced the placeholder (surface seating).</summary>
        private readonly Dictionary<string, GridItem> _gridItems = new Dictionary<string, GridItem>();
        private readonly HashSet<string> _modelIn = new HashSet<string>();
        // tap boxes of the interactable pieces (world-aligned, beside the scaled props) and the lamps' lights, by uid
        private readonly Dictionary<string, GameObject> _taps = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Light> _lamps = new Dictionary<string, Light>();
        private GameStateStore _gs;
        private bool _night;

        /// <param name="buildShell">false for the garden: the lawn, fence and cottage come from GardenScene, only the saved items are rendered.</param>
        public RoomView(GameStateStore gs, string roomId, Transform parent, Vector3 origin, Shader lit = null, bool buildShell = true)
        {
            RoomId = roomId;
            Origin = origin;
            Def = RoomDefs.Get(roomId);
            Root = new GameObject("Room:" + roomId);
            Root.transform.SetParent(parent, false);
            _items = new GameObject("Items");
            _items.transform.SetParent(Root.transform, false);
            HomeState.EnsureInitialized(gs);
            _gs = gs;
            if (buildShell) BuildShell();
            Refresh(gs);
            if (buildShell) ApplyStyle(gs);
        }

        private readonly GameObject _items;

        public float WidthM => (Def?.GridW ?? 12) * (float)GridData.CellSize;
        public float DepthM => (Def?.GridH ?? 10) * (float)GridData.CellSize;
        public Vector3 Center => Origin + new Vector3(WidthM * 0.5f, 0f, DepthM * 0.5f);
        private float North => Origin.z + DepthM;
        private float South => Origin.z;
        private float West => Origin.x;
        private float East => Origin.x + WidthM;

        private void BuildShell()
        {
            var w = WidthM;
            var d = DepthM;
            var t = Root.transform;
            var wallColor = ArtLibrary.Hex(Def?.WallColor ?? "#FFF6EC");
            // plaster grain on the walls (board 08: soft, matte, a hint of relief in the raking window light)
            var wall = ArtLibrary.PbrSlab("plaster", new Vector2(w, WallHeight), 2.4f, wallColor, 0.05f, 0.5f);
            var wallSide = ArtLibrary.PbrSlab("plaster", new Vector2(d, WallHeight), 2.4f, wallColor, 0.05f, 0.5f);
            var wainscot = ArtLibrary.Solid(ArtLibrary.Hex("#FFFDF8"), 0.12f);
            var trim = ArtLibrary.Solid(ArtLibrary.Hex("#E9D6BE"), 0.2f);
            var wood = ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.25f);

            // floor: honey planks (1 texture tile ≈ 1.2 m so the boards read at the diorama distance) in the living
            // room and bedroom, ceramic tiles (2 per metre) in kitchen and bathroom — both tinted with rooms.json
            var tiled = Def?.Id == "kitchen" || Def?.Id == "bathroom";
            var floor = tiled
                ? ArtLibrary.PbrSlab("tiles", new Vector2(w, d), 1.1f, FloorTint(Def.FloorColor, 0.35f) * 1.08f, 0.45f, 0.6f)
                : ArtLibrary.PbrSlab("wood_floor", new Vector2(w, d), 1.7f, Def == null || Def.Id == "living" ? ArtLibrary.Hex("#F2CFA0") : FloorTint(Def.FloorColor, 0.55f) * 1.1f, 0.38f, 0.75f);
            ArtLibrary.Box(t, "Floor", Center + new Vector3(0f, -0.1f, 0f), new Vector3(w + 2f * WallThickness, 0.2f, d + 2f * WallThickness), floor, shadows: false);
            BuildSurround(t);

            // walls N / W / E (south stays open towards the camera)
            var wallY = WallHeight * 0.5f;
            ArtLibrary.Box(t, "WallN", new Vector3(Center.x, wallY, North + WallThickness * 0.5f), new Vector3(w + 2f * WallThickness, WallHeight, WallThickness), wall);
            ArtLibrary.Box(t, "WallW", new Vector3(West - WallThickness * 0.5f, wallY, Center.z), new Vector3(WallThickness, WallHeight, d), wallSide);
            ArtLibrary.Box(t, "WallE", new Vector3(East + WallThickness * 0.5f, wallY, Center.z), new Vector3(WallThickness, WallHeight, d), wallSide);

            // wainscot panelling with a rail, plus wooden skirting (boards 08/09)
            const float inset = 0.03f;
            var wy = WainscotHeight * 0.5f;
            ArtLibrary.Box(t, "WainscotN", new Vector3(Center.x, wy, North - inset * 0.5f), new Vector3(w, WainscotHeight, inset), wainscot, false);
            ArtLibrary.Box(t, "WainscotW", new Vector3(West + inset * 0.5f, wy, Center.z), new Vector3(inset, WainscotHeight, d), wainscot, false);
            ArtLibrary.Box(t, "WainscotE", new Vector3(East - inset * 0.5f, wy, Center.z), new Vector3(inset, WainscotHeight, d), wainscot, false);
            ArtLibrary.Box(t, "RailN", new Vector3(Center.x, WainscotHeight + 0.02f, North - 0.03f), new Vector3(w, 0.05f, 0.06f), trim, false);
            ArtLibrary.Box(t, "RailW", new Vector3(West + 0.03f, WainscotHeight + 0.02f, Center.z), new Vector3(0.06f, 0.05f, d), trim, false);
            ArtLibrary.Box(t, "RailE", new Vector3(East - 0.03f, WainscotHeight + 0.02f, Center.z), new Vector3(0.06f, 0.05f, d), trim, false);
            ArtLibrary.Box(t, "SkirtingN", new Vector3(Center.x, 0.06f, North - 0.035f), new Vector3(w, 0.12f, 0.07f), wood, false);
            ArtLibrary.Box(t, "SkirtingW", new Vector3(West + 0.035f, 0.06f, Center.z), new Vector3(0.07f, 0.12f, d), wood, false);
            ArtLibrary.Box(t, "SkirtingE", new Vector3(East - 0.035f, 0.06f, Center.z), new Vector3(0.07f, 0.12f, d), wood, false);
            // vertical panel grooves on the wainscot every half metre
            var groove = ArtLibrary.Solid(ArtLibrary.Hex("#EFE4D6"), 0.1f);
            for (var i = 1; i < Mathf.RoundToInt(w / 0.5f); i++)
                ArtLibrary.Box(t, "GrooveN", new Vector3(West + i * 0.5f, wy + 0.02f, North - inset - 0.004f), new Vector3(0.025f, WainscotHeight - 0.2f, 0.008f), groove, false);

            if (Def == null) return;
            foreach (var win in Def.Raw?["windows"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
            {
                if (Gooby.Core.Json.J.Str(win["wall"], "N") != "N") continue;
                var off = Gooby.Core.Json.J.Int(win["offset"]);
                var size = Gooby.Core.Json.J.Int(win["size"], 2);
                var group = new GameObject("Window:N:" + off);
                group.transform.SetParent(t, false);
                _windows.Add(("N", off, off + size, group));
                BuildWindow(group.transform, West + (off + size * 0.5f) * 0.5f, size * 0.5f);
            }
            foreach (var door in Def.Doors)
            {
                // door slot: two cells along its wall; the group's -Z points into the room. Grid y runs south, so a
                // wall offset on W/E counts from the north end.
                var along = (door.Offset + 1f) * 0.5f;
                switch (door.Wall)
                {
                    case "N": BuildDoor(new Vector3(West + along, 0f, North - 0.005f), Quaternion.identity, door); break;
                    case "W": BuildDoor(new Vector3(West + 0.005f, 0f, North - along), Quaternion.Euler(0f, -90f, 0f), door); break;
                    case "E": BuildDoor(new Vector3(East - 0.005f, 0f, North - along), Quaternion.Euler(0f, 90f, 0f), door); break;
                }
            }
            if (Def.Id == "living") DressLiving(t);
        }

        /// <summary>World x of each north-wall window's middle (rooms.json "windows").</summary>
        public List<float> WindowCenters()
        {
            var outp = new List<float>();
            foreach (var win in Def?.Raw?["windows"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
            {
                if (Gooby.Core.Json.J.Str(win["wall"], "N") != "N") continue;
                outp.Add(West + (Gooby.Core.Json.J.Int(win["offset"]) + Gooby.Core.Json.J.Int(win["size"], 2) * 0.5f) * 0.5f);
            }
            return outp;
        }

        /// <summary>Lightens a room's floor colour towards white so the texture's own shading stays visible.</summary>
        private static Color FloorTint(string hex, float towardsWhite) => Color.Lerp(ArtLibrary.Hex(hex ?? "#C9A36B"), Color.white, towardsWhite);

        private readonly List<(string wall, int from, int toExcl, GameObject view)> _windows = new List<(string, int, int, GameObject)>();

        /// <summary>A window piece hung over one of the room's own windows replaces it: the built-in one steps aside.</summary>
        public void UpdateWindows(GridData grid)
        {
            if (grid == null) return;
            foreach (var (wall, from, toExcl, view) in _windows)
                if (view != null) view.SetActive(!grid.WindowReplaced(wall, from, toExcl));
        }

        private void BuildWindow(Transform t, float cx, float width)
        {
            var z = North - 0.005f;
            var frame = ArtLibrary.Solid(Color.white, 0.3f);
            var glass = ArtLibrary.Solid(ArtLibrary.Hex("#CFE6FF"), 0.9f);
            var curtain = ArtLibrary.Solid(ArtLibrary.Hex("#FFC9D6"), 0.1f);
            var living = Def?.Id == "living";
            var h = living ? 1.32f : 1.15f;
            var y = living ? 1.82f : 1.75f;
            var visW = living ? width * 1.28f : width;
            ArtLibrary.Box(t, "WindowFrame", new Vector3(cx, y, z - 0.02f), new Vector3(visW + 0.16f, h + 0.16f, 0.06f), frame, false);
            ArtLibrary.Box(t, "WindowGlass", new Vector3(cx, y, z - 0.03f), new Vector3(visW, h, 0.02f), glass, false);
            ArtLibrary.Box(t, "WindowBarV", new Vector3(cx, y, z - 0.05f), new Vector3(0.04f, h, 0.02f), frame, false);
            ArtLibrary.Box(t, "WindowBarH", new Vector3(cx, y, z - 0.05f), new Vector3(visW, 0.04f, 0.02f), frame, false);
            ArtLibrary.Box(t, "WindowSill", new Vector3(cx, y - h * 0.5f - 0.1f, z - 0.08f), new Vector3(visW + 0.3f, 0.05f, 0.16f), frame);
            ArtLibrary.Box(t, "CurtainRod", new Vector3(cx, y + h * 0.5f + 0.16f, z - 0.1f), new Vector3(visW + 0.7f, 0.03f, 0.03f), ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.3f), false);
            // pleated drapes with tie-backs (build_scenery `curtain`, 0.34 × 1.82 m, hem at its origin, gathered towards
            // its +X side); the boxes stay as the fallback. Yaw 180 turns the model's front into the room, so +X of the
            // model is world +X: the right panel keeps it, the left one is mirrored — both tie-backs sit outwards.
            var cw = living ? 0.32f : 0.26f;
            var ch = h + 0.5f;
            foreach (var side in new[] { -1f, 1f })
            {
                var at = new Vector3(cx + side * (visW * 0.5f + 0.12f), y + 0.05f, z - 0.1f);
                var box = ArtLibrary.Box(t, "Curtain", at, new Vector3(cw, ch, 0.1f), curtain);
                Scenery.Place(t, "curtain", new Vector3(at.x, at.y - ch * 0.5f, z - 0.05f), 180f,
                    new Vector3(side * cw / 0.34f, ch / 1.82f, 1.1f), box);
            }
            if (!living) return;
            var sillY = y - h * 0.5f - 0.02f;
            ArtLibrary.Plant(t, "plant_bush", new Vector3(cx - visW * 0.22f, sillY, z - 0.18f), 0.38f, 18f);
            ArtLibrary.Plant(t, "plant_daisies", new Vector3(cx + visW * 0.2f, sillY, z - 0.16f), 0.32f, 70f);
        }

        /// <summary>
        /// Panelled wooden door built in a local frame: origin at the floor on the wall surface, -Z into the room.
        /// Carries a collider + DoorMarker so a tap on it walks Gooby through (SimpleWorld.TapWorld).
        /// </summary>
        private void BuildDoor(Vector3 at, Quaternion facing, RoomDefs.Door door)
        {
            var group = new GameObject("Door:" + door.Id);
            group.transform.SetParent(Root.transform, false);
            group.transform.SetPositionAndRotation(at, facing);
            group.AddComponent<DoorMarker>().Door = door;
            var hit = group.AddComponent<BoxCollider>();
            hit.center = new Vector3(0f, 1.05f, -0.12f);
            hit.size = new Vector3(1.1f, 2.2f, 0.3f);
            var t = group.transform;
            var doorMat = ArtLibrary.Solid(ArtLibrary.Hex("#7A5237"), 0.3f);
            var panel = ArtLibrary.Solid(ArtLibrary.Hex("#8B6044"), 0.3f);
            var frame = ArtLibrary.Solid(ArtLibrary.Hex("#FFFDF8"), 0.2f);
            const float dw = 0.92f, dh = 2.08f;
            Local(ArtLibrary.Box(t, "DoorFrame", Vector3.zero, new Vector3(dw + 0.18f, dh + 0.1f, 0.05f), frame, false), new Vector3(0f, dh * 0.5f + 0.02f, -0.015f));
            Local(ArtLibrary.Box(t, "Door", Vector3.zero, new Vector3(dw, dh, 0.04f), doorMat, false), new Vector3(0f, dh * 0.5f, -0.03f));
            Local(ArtLibrary.Box(t, "DoorPanelTop", Vector3.zero, new Vector3(dw - 0.24f, 0.7f, 0.02f), panel, false), new Vector3(0f, 1.45f, -0.055f));
            Local(ArtLibrary.Box(t, "DoorPanelBottom", Vector3.zero, new Vector3(dw - 0.24f, 0.7f, 0.02f), panel, false), new Vector3(0f, 0.55f, -0.055f));
            Local(ArtLibrary.Sphere(t, "DoorKnob", Vector3.zero, 0.035f, ArtLibrary.Solid(ArtLibrary.Hex("#E8C36A"), 0.7f), false), new Vector3(dw * 0.5f - 0.12f, 1.02f, -0.08f));
        }

        private static void Local(GameObject go, Vector3 localPos)
        {
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
        }

        /// <summary>Re-syncs the placed items with the save (call after build mode).</summary>
        public void Refresh(GameStateStore gs)
        {
            foreach (var go in _views.Values) if (go != null) Object.Destroy(go);
            _views.Clear();
            _pieces.Clear();
            _gridItems.Clear();
            _modelIn.Clear();
            foreach (var go in _taps.Values) if (go != null) Object.Destroy(go);
            _taps.Clear();
            _lamps.Clear();
            _gs = gs;
            var grid = HomeState.LoadRoomGrid(gs, RoomId);
            foreach (var item in grid.Items) AddItemView(item);
            UpdateWindows(grid);
            RefreshGarlands();
        }

        /// <summary>One placed piece appears (a visit build delta) — the rest of the room stays as it is.</summary>
        public void AddItemView(GridItem item)
        {
            if (item == null) return;
            RemoveItemView(item.Uid);
            _pieces[item.Uid] = (item.Def.Id, item.Rot, item.Wall != null);
            _gridItems[item.Uid] = item;
            var parent = _items.transform;
            if (item.Wall != null)
            {
                var wallItem = WallProps.Make(parent, item, Origin, WidthM, DepthM, _gs?.State);
                if (wallItem != null) { wallItem.name = "Item:" + item.Uid; _views[item.Uid] = wallItem; Tag(item, wallItem); }
                return;
            }
            var custom = PropPlaceholder.MakeTextured(parent, item.Def, item.At, item.Rot, Origin, Def?.GridH ?? 10);
            if (custom != null)
            {
                custom.name = "Item:" + item.Uid;
                _views[item.Uid] = custom;
                Tag(item, custom);
                if (PropPlaceholder.HasRugModel(item.Def.Id)) _ = SwapAsync(item, custom);
                return;
            }
            var placeholder = PropPlaceholder.Make(parent, item.Def, item.At, item.Rot, Origin, Def?.GridH ?? 10, ArtLibrary.Lit);
            placeholder.name = "Item:" + item.Uid;
            VariantTint.Apply(placeholder, item.Variant);
            _views[item.Uid] = placeholder;
            Tag(item, placeholder);
            _ = SwapAsync(item, placeholder);
        }

        /// <summary>One placed piece goes away (a visit build delta); false when no view carries that uid.</summary>
        public bool RemoveItemView(string uid)
        {
            if (string.IsNullOrEmpty(uid) || !_views.TryGetValue(uid, out var go)) return false;
            if (go != null) Object.Destroy(go);
            _views.Remove(uid);
            _pieces.Remove(uid);
            _gridItems.Remove(uid);
            _modelIn.Remove(uid);
            if (_taps.TryGetValue(uid, out var tap) && tap != null) Object.Destroy(tap);
            _taps.Remove(uid);
            _lamps.Remove(uid);
            return true;
        }

        /// <summary>Number of placed pieces on stage (QA state).</summary>
        public int ItemViewCount => _views.Count;

        private readonly Dictionary<string, (string itemId, int rot, bool wall)> _pieces = new Dictionary<string, (string, int, bool)>();

        public readonly struct Piece
        {
            public readonly string Uid, ItemId;
            public readonly int Rot;
            public readonly Bounds Bounds;
            public Piece(string uid, string itemId, int rot, Bounds bounds) { Uid = uid; ItemId = itemId; Rot = rot; Bounds = bounds; }
            /// <summary>The way the piece faces: rot 0 looks at the camera (−Z).</summary>
            public Vector3 Front => PropPlaceholder.Rotation(Rot) * Vector3.forward;
        }

        /// <summary>The floor pieces on stage with their world bounds as they stand now (wall pictures left out).</summary>
        public IEnumerable<Piece> Pieces()
        {
            foreach (var kv in _pieces)
            {
                if (kv.Value.wall || !_views.TryGetValue(kv.Key, out var go) || go == null) continue;
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                yield return new Piece(kv.Key, kv.Value.itemId, kv.Value.rot, b);
            }
        }

        private async System.Threading.Tasks.Task SwapAsync(GridItem item, GameObject placeholder)
        {
            var prop = await AssetLoader.PropInstanceAsync(AssetLoader.ArchetypeFor(item.Def.Id), _items.transform);
            if (prop == null) return;
            if (placeholder == null || !_views.TryGetValue(item.Uid, out var current) || current != placeholder)
            {
                Object.Destroy(prop);
                return;
            }
            PropPlaceholder.FitProp(prop, item.Def, item.At, item.Rot, Origin, Def?.GridH ?? 10);
            VariantTint.Apply(prop, item.Variant);
            // POW!'s disco ball turns slowly on its cord, its coloured facets twinkle as they come round
            if (item.Def.Id == "pow_diskokugel") prop.AddComponent<RanchBauTeile.Dreher>().Grad = 28f;
            prop.name = "Item:" + item.Uid;
            _views[item.Uid] = prop;
            _modelIn.Add(item.Uid);
            Object.Destroy(placeholder);
            Seat(item);
            Tag(item, prop);
            // the pieces already standing on this one find its real top now
            foreach (var dep in new List<GridItem>(_gridItems.Values))
                if (PropPlaceholder.SupportOf(dep, _gridItems.Values) == item && Seat(dep)) Tag(dep, _views[dep.Uid]);
        }

        /// <summary>Stands a surface piece's model on its support's model once both are in; false when it stays where it is.</summary>
        private bool Seat(GridItem item)
        {
            if (!_modelIn.Contains(item.Uid) || !_views.TryGetValue(item.Uid, out var view) || view == null) return false;
            var support = PropPlaceholder.SupportOf(item, _gridItems.Values);
            if (support == null || !_modelIn.Contains(support.Uid) || !_views.TryGetValue(support.Uid, out var under) || under == null) return false;
            PropPlaceholder.SeatOnSupport(view, support, under);
            return true;
        }

        /// <summary>
        /// Interactable pieces (Interactables.Kind) get a tap box around their visible bounds with a
        /// <see cref="FurnitureMarker"/>; lamps also get a warm light that is on at night unless switched off.
        /// </summary>
        private void Tag(GridItem item, GameObject view)
        {
            if (_taps.TryGetValue(item.Uid, out var old) && old != null) Object.Destroy(old);
            _taps.Remove(item.Uid);
            _lamps.Remove(item.Uid);
            var kind = Interactables.Kind(item.Def);
            if (kind.Length == 0 || view == null) return;
            var renderers = view.GetComponentsInChildren<Renderer>();
            var b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(view.transform.position + Vector3.up * 0.4f, Vector3.one * 0.6f);
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var tap = new GameObject("Tap:" + item.Uid);
            tap.transform.SetParent(_items.transform, true);
            tap.transform.SetPositionAndRotation(b.center, Quaternion.identity);
            var col = tap.AddComponent<BoxCollider>();
            col.size = b.size + new Vector3(0.12f, 0.12f, 0.12f);
            var marker = tap.AddComponent<FurnitureMarker>();
            marker.Uid = item.Uid;
            marker.ItemId = item.Def.Id;
            marker.Kind = kind;
            marker.Rot = string.IsNullOrEmpty(item.Wall) ? item.Rot : WallFacing(item.Wall);
            marker.Bounds = b;
            _taps[item.Uid] = tap;
            if (kind != Interactables.Licht) return;
            var lamp = new GameObject("LampLight").AddComponent<Light>();
            lamp.transform.SetParent(tap.transform, false);
            lamp.transform.position = new Vector3(b.center.x, b.max.y - Mathf.Min(0.15f, b.size.y * 0.25f), b.center.z);
            lamp.type = LightType.Point;
            var lava = item.Def.Id == "pow_lavalampe";
            lamp.color = ArtLibrary.Hex(lava ? "#FF8FC0" : "#FFD49A");
            lamp.range = lava ? 2.4f : 3.2f;
            lamp.intensity = 1.5f;
            lamp.shadows = LightShadows.None;
            lamp.enabled = _night && BadState.LightOn(_gs, item.Uid);
            _lamps[item.Uid] = lamp;
        }

        /// <summary>The floor rot whose front (<see cref="PropPlaceholder.Rotation"/>) points from that wall into the room.</summary>
        private static int WallFacing(string wall) => wall == "W" ? 1 : wall == "S" ? 2 : wall == "E" ? 3 : 0;

        private readonly Dictionary<Renderer, Material> _builtLook = new Dictionary<Renderer, Material>();

        /// <summary>"Gestalten": the room's chosen wall and floor (HouseStyle) over its built look; no entry = the built look.</summary>
        public void ApplyStyle(GameStateStore gs) => ShowStyle(HouseStyle.Room(gs, RoomId));

        /// <summary>Shows a style without saving it (the screen's try-on); null = the built look.</summary>
        public void ShowStyle(HouseStyle.RoomStyle style)
        {
            if (Root == null) return;
            var wand = style != null ? HouseStyle.Get(HouseStyle.Wand, style.Wand) : null;
            var boden = style != null ? HouseStyle.Get(HouseStyle.Boden, style.Boden) : null;
            void Set(string name, string muster, string farbe, Vector2 size)
            {
                var tr = Root.transform.Find(name);
                var r = tr != null ? tr.GetComponent<Renderer>() : null;
                if (r == null) return;
                if (!_builtLook.ContainsKey(r)) _builtLook[r] = r.sharedMaterial;
                r.sharedMaterial = muster == null ? _builtLook[r] : SurfaceTextures.Surface(muster, farbe, size);
            }
            Set("WallN", wand?.Muster, style?.WandFarbe, new Vector2(WidthM, WallHeight));
            Set("WallW", wand?.Muster, style?.WandFarbe, new Vector2(DepthM, WallHeight));
            Set("WallE", wand?.Muster, style?.WandFarbe, new Vector2(DepthM, WallHeight));
            Set("Floor", boden?.Muster, style?.BodenFarbe, new Vector2(WidthM, DepthM));
        }

        /// <summary>Lamps glow at night (DayPhase), each unless the player switched it off.</summary>
        public void ApplyLamps(bool night)
        {
            var changed = _night != night;
            _night = night;
            foreach (var kv in _lamps)
                if (kv.Value != null) kv.Value.enabled = night && BadState.LightOn(_gs, kv.Key);
            if (changed) RefreshGarlands();
        }

        private GameObject _garlands;

        /// <summary>The room's garlands from the save (they stay up in build mode, where the session edits only the grid).</summary>
        public void RefreshGarlands()
        {
            if (_gs == null || RoomId == "garden") return;
            if (_garlands != null) Object.Destroy(_garlands);
            _garlands = new GameObject("Garlands");
            _garlands.transform.SetParent(Root.transform, false);
            var gridH = Def?.GridH ?? 10;
            foreach (var entry in Girlanden.Liste(_gs, RoomId))
            {
                if (!Girlanden.Gueltig(entry)) continue;
                GarlandView.Build(_garlands.transform, Gooby.Core.Json.J.Str(entry["typ"]),
                    CeilingPoint(Girlanden.Zelle(entry, "zelle_a"), gridH), CeilingPoint(Girlanden.Zelle(entry, "zelle_b"), gridH), _night);
            }
        }

        public int GarlandCount => _garlands == null ? 0 : _garlands.transform.childCount;

        /// <summary>Where a garland end hangs: the cell's middle just under the ceiling.</summary>
        public Vector3 CeilingPoint(Cell cell, int gridH) =>
            PropPlaceholder.CellCenter(Origin, gridH, cell, 1, 1, 0) + Vector3.up * (PropPlaceholder.CeilingHeight - 0.05f);

        public bool LampLit(string uid) => _lamps.TryGetValue(uid, out var l) && l != null && l.enabled;

        public FurnitureMarker Marker(string uid) => _taps.TryGetValue(uid, out var tap) && tap != null ? tap.GetComponent<FurnitureMarker>() : null;

        /// <summary>The shown piece of an item (its renderers), null while none.</summary>
        public GameObject ViewOf(string uid) => uid != null && _views.TryGetValue(uid, out var go) ? go : null;

        public IEnumerable<FurnitureMarker> Markers()
        {
            foreach (var tap in _taps.Values) if (tap != null) yield return tap.GetComponent<FurnitureMarker>();
        }

        public void SetVisible(bool on) => Root.SetActive(on);

        /// <summary>Hides only the placed items (build mode renders its own views); walls, floor and door stay.</summary>
        public void SetItemsVisible(bool on) => _items.SetActive(on);

        /// <summary>
        /// Board 08 extras that are not save items: hanging ivy baskets, umbrella stand by the north door, calendar
        /// on the east wall, coffee-table mug, stuffed bunny on the bed, and the two leafy plants that frame the
        /// camera. Sofa throw and daisy live on the `sofa` GLB (boards 08/09). Layout furniture stays in
        /// default_layouts.json. No ceiling beams: boards 01/02 show the back wall — door top, pictures, shelves —
        /// clean up to the wall cap, and the three cross-room beams cut right through that from both cameras.
        /// </summary>
        private void DressLiving(Transform t)
        {
            var hangY = 2.15f;
            HangingBasket(t, new Vector3(West + 1.25f, hangY, North - 0.42f), 1.05f, 12f);
            HangingBasket(t, new Vector3(West + 2.05f, hangY, North - 0.35f), 0.92f, 48f);
            HangingBasket(t, new Vector3(Center.x - 0.15f, hangY, North - 0.38f), 1.0f, 80f);
            HangingBasket(t, new Vector3(East - 1.45f, hangY, North - 0.4f), 1.08f, 22f);
            HangingBasket(t, new Vector3(East - 0.55f, hangY, North - 0.7f), 0.88f, 110f);
            HangingBasket(t, new Vector3(East - 0.42f, hangY, Center.z + 0.55f), 0.95f, 64f);
            HangingBasket(t, new Vector3(East - 0.48f, hangY, Center.z - 0.55f), 0.9f, 150f);
            HangingBasket(t, new Vector3(West + 0.42f, hangY, Center.z + 0.2f), 0.86f, 200f);

            UmbrellaStand(t, new Vector3(West + 0.72f, 0f, North - 0.42f));
            WallCalendar(t, new Vector3(East - 0.06f, 1.72f, Center.z + 0.85f));
            CoffeeMug(t, new Vector3(-1.22f, 0.5f, -0.12f));
            StuffedBunny(t, new Vector3(1.55f, 0.52f, 1.55f));

            ArtLibrary.Plant(t, "plant_bush", new Vector3(West + 0.28f, 0f, South - 0.28f), 1.45f, 8f);
            ArtLibrary.Plant(t, "plant_bush", new Vector3(East - 0.28f, 0f, South - 0.32f), 1.38f, 52f);
        }

        /// <summary>
        /// Boards 02/08: the room is a diorama standing in its garden. A lawn runs out under the walls and a hedge
        /// of bushes, flowers and round trees hugs the outside of the shell, so the high build camera and the
        /// corners of the home frame show greenery rather than a bare backdrop.
        /// </summary>
        private void BuildSurround(Transform t)
        {
            // a little minty still: the room's key light and warm ambient take some blue out of anything outside the
            // window panes; with the ungraded whites of the no-tonemapper grade this lands near board 02's mid green
            var lawn = ArtLibrary.PbrSlab("grass", new Vector2(60f, 60f), 2.4f, ArtLibrary.Hex("#7CC48C"), 0.04f, 0.7f);
            ArtLibrary.Box(t, "Lawn", Center + new Vector3(0f, -0.21f, 0f), new Vector3(60f, 0.2f, 60f), lawn, shadows: false);
            var seed = (Def?.Id ?? "living").GetHashCode();
            float Jitter(int i, float amp) => Mathf.Sin(seed * 0.37f + i * 2.399f) * amp;
            // hedge along the back and both flanks, a metre off the walls so it does not poke through them
            var back = North + WallThickness + 0.9f;
            for (var i = 0; i < 4; i++)
            {
                var x = West - 0.6f + (WidthM + 1.2f) * (i + 0.5f) / 4f;
                Scenery.Place(t, i % 2 == 0 ? "bush_big" : "bush_flowers", new Vector3(x + Jitter(i, 0.25f), 0f, back + Jitter(i + 9, 0.2f)), 20f * i, 1.15f + Jitter(i + 3, 0.1f));
            }
            for (var i = 0; i < 3; i++)
            {
                var z = South + 0.6f + (DepthM - 0.4f) * (i + 0.5f) / 3f;
                Scenery.Place(t, i == 1 ? "bush_flowers" : "bush_big", new Vector3(West - WallThickness - 0.95f + Jitter(i + 20, 0.2f), 0f, z), 30f * i + 15f, 1.05f + Jitter(i + 5, 0.1f));
                Scenery.Place(t, i == 1 ? "bush_flowers" : "bush_big", new Vector3(East + WallThickness + 0.95f + Jitter(i + 30, 0.2f), 0f, z), 30f * i + 40f, 1.05f + Jitter(i + 7, 0.1f));
            }
            // round trees at the back corners frame the room like the crowns on the boards
            Scenery.Place(t, "tree_round", new Vector3(West - 1.7f, 0f, back + 1.1f), 12f, 0.95f);
            Scenery.Place(t, "tree_round", new Vector3(East + 1.6f, 0f, back + 1.3f), 200f, 1.05f);
            ArtLibrary.Plant(t, "plant_flowers_pink", new Vector3(West - 0.9f, 0f, South + 0.35f), 0.5f, 25f);
            ArtLibrary.Plant(t, "plant_daisies", new Vector3(East + 0.95f, 0f, South + 0.3f), 0.45f, 80f);
        }

        /// <summary>
        /// Board 08 hanging plants: terracotta pot on a thin hanger, trailing ivy of flattened leaf blobs —
        /// not crossed bush billboards, which read as round shrubs in the south camera.
        /// </summary>
        private static void HangingBasket(Transform t, Vector3 pot, float scale, float yaw)
        {
            var terracotta = ArtLibrary.Solid(ArtLibrary.Hex("#E7A67A"), 0.18f);
            var hanger = ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.28f);
            var soil = ArtLibrary.Solid(ArtLibrary.Hex("#6B4A32"), 0.08f);
            var leafA = ArtLibrary.Solid(ArtLibrary.Hex("#6FBF6A"), 0.12f);
            var leafB = ArtLibrary.Solid(ArtLibrary.Hex("#8FDC7A"), 0.12f);
            var leafC = ArtLibrary.Solid(ArtLibrary.Hex("#4E9A58"), 0.12f);
            var hangLen = 0.32f * scale;
            ArtLibrary.Cylinder(t, "HangWire", pot + Vector3.up * (hangLen * 0.5f + 0.05f * scale), 0.008f * scale, hangLen, hanger, false);
            var hook = ArtLibrary.Sphere(t, "HangHook", pot + Vector3.up * (hangLen + 0.05f * scale), 0.025f * scale, hanger, false);
            hook.transform.localScale = new Vector3(0.05f * scale, 0.022f * scale, 0.05f * scale);
            ArtLibrary.Cylinder(t, "HangPot", pot, 0.10f * scale, 0.11f * scale, terracotta, false);
            ArtLibrary.Cylinder(t, "HangRim", pot + Vector3.up * (0.05f * scale), 0.12f * scale, 0.028f * scale, terracotta, false);
            var dirt = ArtLibrary.Sphere(t, "HangSoil", pot + Vector3.up * (0.04f * scale), 0.09f * scale, soil, false);
            dirt.transform.localScale = new Vector3(0.19f * scale, 0.04f * scale, 0.19f * scale);

            var greens = new[] { leafA, leafB, leafC };
            var rad = yaw * Mathf.Deg2Rad;
            for (var i = 0; i < 6; i++)
            {
                var a = rad + i * 1.047f;
                var pos = pot + new Vector3(Mathf.Sin(a) * 0.08f * scale, 0.07f * scale, Mathf.Cos(a) * 0.08f * scale);
                var crown = ArtLibrary.Sphere(t, "IvyCrown", pos, 0.055f * scale, greens[i % 3], false);
                crown.transform.localScale = new Vector3(0.15f * scale, 0.07f * scale, 0.12f * scale);
            }
            for (var vine = 0; vine < 3; vine++)
            {
                var a = rad + vine * 2.094f;
                var lean = 0.055f + vine * 0.018f;
                for (var n = 0; n < 6; n++)
                {
                    var drop = (0.1f + n * 0.12f) * scale;
                    var pos = pot + new Vector3(Mathf.Sin(a) * lean * n * scale, -drop, Mathf.Cos(a) * lean * n * scale);
                    var leaf = ArtLibrary.Sphere(t, "Ivy", pos, 0.05f * scale, greens[(vine + n) % 3], false);
                    var w = (0.15f - n * 0.012f) * scale;
                    leaf.transform.localScale = new Vector3(w, 0.05f * scale, w * 0.78f);
                    leaf.transform.rotation = Quaternion.Euler(16f + n * 10f, yaw + vine * 40f, vine * 18f - n * 6f);
                }
            }
        }

        private static void UmbrellaStand(Transform t, Vector3 at)
        {
            var wood = ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.25f);
            ArtLibrary.Cylinder(t, "Stand", at + Vector3.up * 0.22f, 0.1f, 0.44f, wood);
            ArtLibrary.Cylinder(t, "UmbStickY", at + new Vector3(0.04f, 0.62f, 0.02f), 0.02f, 0.85f, ArtLibrary.Solid(ArtLibrary.Hex("#E8C36A"), 0.35f), false);
            var yCanopy = ArtLibrary.Sphere(t, "UmbY", at + new Vector3(0.04f, 1.05f, 0.02f), 0.16f, ArtLibrary.Solid(ArtLibrary.Hex("#FFE566"), 0.12f), false);
            yCanopy.transform.localScale = new Vector3(0.34f, 0.11f, 0.34f);
            ArtLibrary.Cylinder(t, "UmbStickT", at + new Vector3(-0.05f, 0.58f, -0.03f), 0.02f, 0.78f, ArtLibrary.Solid(ArtLibrary.Hex("#7FCF9B"), 0.3f), false);
            var tCanopy = ArtLibrary.Sphere(t, "UmbT", at + new Vector3(-0.05f, 0.98f, -0.03f), 0.15f, ArtLibrary.Solid(ArtLibrary.Hex("#98E7A8"), 0.12f), false);
            tCanopy.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);
        }

        private static void WallCalendar(Transform t, Vector3 pos)
        {
            var frame = ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.25f);
            var paper = ArtLibrary.Solid(ArtLibrary.Hex("#FFFDF8"), 0.08f);
            var ink = ArtLibrary.Solid(ArtLibrary.Hex("#E7A67A"), 0.1f);
            ArtLibrary.Box(t, "CalFrame", pos, new Vector3(0.04f, 0.46f, 0.34f), frame, false);
            ArtLibrary.Box(t, "CalPaper", pos + new Vector3(-0.025f, 0f, 0f), new Vector3(0.02f, 0.4f, 0.28f), paper, false);
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++)
                    ArtLibrary.Box(t, "CalCell", pos + new Vector3(-0.03f, 0.1f - r * 0.1f, -0.08f + c * 0.08f),
                        new Vector3(0.01f, 0.06f, 0.06f), ink, false);
        }

        private static void CoffeeMug(Transform t, Vector3 at)
        {
            var cream = ArtLibrary.Solid(ArtLibrary.Hex("#FFF6EC"), 0.2f);
            ArtLibrary.Cylinder(t, "Mug", at, 0.045f, 0.08f, cream, false);
            ArtLibrary.Box(t, "MugHandle", at + new Vector3(0.055f, 0f, 0f), new Vector3(0.02f, 0.05f, 0.035f), cream, false);
        }

        private static void StuffedBunny(Transform t, Vector3 at)
        {
            var creme = ArtLibrary.Solid(ArtLibrary.Hex("#FFF8EE"), 0.1f);
            var pink = ArtLibrary.Solid(ArtLibrary.Hex("#FFC9A6"), 0.1f);
            ArtLibrary.Sphere(t, "ToyBody", at, 0.09f, creme, false);
            ArtLibrary.Sphere(t, "ToyHead", at + new Vector3(0f, 0.1f, 0.02f), 0.07f, creme, false);
            foreach (var s in new[] { -1f, 1f })
            {
                var ear = ArtLibrary.Sphere(t, "ToyEar", at + new Vector3(s * 0.04f, 0.2f, 0.01f), 0.03f, creme, false);
                ear.transform.localScale = new Vector3(0.045f, 0.12f, 0.035f);
                ArtLibrary.Sphere(t, "ToyCheek", at + new Vector3(s * 0.045f, 0.09f, 0.07f), 0.018f, pink, false);
            }
        }
    }

    /// <summary>
    /// Wall-slot items (pictures, postcards, shelves, wall lamps): flat panels hung at eye height. The postcard wall and
    /// the souvenir shelf read the save (<paramref name="state"/> of <see cref="Make"/>): the newest postcards of the
    /// archive pinned to cork, one little souvenir per destination Gooby has visited (postkarten_props.gd).
    /// </summary>
    public static class WallProps
    {
        private const float HangHeight = 1.6f;

        public static GameObject Make(Transform parent, GridItem item, Vector3 origin, float widthM, float depthM, Newtonsoft.Json.Linq.JObject state = null)
        {
            var span = Mathf.Max(1, item.Def.WallSize) * (float)GridData.CellSize;
            var offset = (item.At.X + Mathf.Max(1, item.Def.WallSize) * 0.5f) * (float)GridData.CellSize;
            Vector3 pos;
            Quaternion rot;
            const float lift = 0.035f; // off the wall surface
            // slots count like the grid's door spans — along N and S from the west, along W and E from the north —
            // otherwise a picture the grid keeps clear of a door is drawn on top of it
            switch (item.Wall)
            {
                case "N": pos = new Vector3(origin.x + offset, HangHeight, origin.z + depthM - lift); rot = Quaternion.identity; break;
                case "S": pos = new Vector3(origin.x + offset, HangHeight, origin.z + lift); rot = Quaternion.Euler(0f, 180f, 0f); break;
                case "W": pos = new Vector3(origin.x + lift, HangHeight, origin.z + depthM - offset); rot = Quaternion.Euler(0f, -90f, 0f); break;
                default: pos = new Vector3(origin.x + widthM - lift, HangHeight, origin.z + depthM - offset); rot = Quaternion.Euler(0f, 90f, 0f); break;
            }
            var go = new GameObject("Wall:" + item.Def.Id);
            go.transform.SetParent(parent, false);
            // the root stays unturned: every part is built facing −Z and turned round pos once at the end — a turned root
            // would turn the boxes a second time and stand them across the east and west walls
            go.transform.position = pos;
            var t = go.transform;
            if (item.Def.Id == Gooby.Core.Life.Nougat.FurnitureId)
            {
                BuildNougat(t, pos);
                foreach (Transform child in t) child.RotateAround(pos, Vector3.up, rot.eulerAngles.y);
                _ = SwapNougatAsync(go, pos, rot);
                return go;
            }
            switch (item.Def.Kategorie)
            {
                case "bilder":
                    switch (item.Def.Id)
                    {
                        case "chalkboardMenu":
                            ArtLibrary.Box(t, "Board", pos, new Vector3(span - 0.06f, 0.6f, 0.05f), ArtLibrary.Solid(ArtLibrary.Hex("#3F3A36"), 0.2f));
                            break;
                        case "souvenirRegal": BuildSouvenirShelf(t, pos, span, state != null ? Gooby.Core.Life.Postcards.SouvenirsVon(state) : new List<string>()); break;
                        case "pow_poster": BuildPoster(t, pos); break;
                        case "postkartenWand": BuildPostcards(t, pos, span, state != null ? Gooby.Core.Life.Postcards.ArchiveOf(state) : new Newtonsoft.Json.Linq.JArray()); break;
                        default: BuildPicture(t, pos, span, item); break;
                    }
                    break;
                case "lampen":
                    ArtLibrary.Box(t, "Arm", pos, new Vector3(0.06f, 0.06f, 0.18f), ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.3f), false);
                    ArtLibrary.Sphere(t, "Shade", pos + Vector3.back * 0.14f, 0.13f, ArtLibrary.Solid(ArtLibrary.Hex("#FFE7B3"), 0.2f), false);
                    break;
                case "fenster":
                    ArtLibrary.Box(t, "Glass", pos, new Vector3(span - 0.1f, 0.9f, 0.02f), ArtLibrary.Solid(ArtLibrary.Hex("#CFE6FF"), 0.9f), false);
                    ArtLibrary.Box(t, "Frame", pos + Vector3.forward * 0.005f, new Vector3(span + 0.04f, 1.02f, 0.05f), ArtLibrary.Solid(Color.white, 0.3f), false);
                    break;
                default:
                    ArtLibrary.Box(t, "Shelf", pos, new Vector3(span - 0.06f, 0.05f, 0.24f), ArtLibrary.Solid(ArtLibrary.Hex("#C99A6B"), 0.3f));
                    break;
            }
            // wall items are rotated as a group after their children were placed in world space
            foreach (Transform child in t) child.RotateAround(pos, Vector3.up, rot.eulerAngles.y);
            return go;
        }

        /// <summary>
        /// The Nougatschleuse until its model is in: a chocolate back plate, the glass tank full of nougat under a gold
        /// band, the spout over a drip tray and the crank with its red knob on the right (built facing −Z, at 0.5–1.4 m).
        /// </summary>
        private static void BuildNougat(Transform t, Vector3 pos)
        {
            var c = new Vector3(pos.x, 0.95f, pos.z);
            var brown = ArtLibrary.Solid(ArtLibrary.Hex("#6B4226"), 0.35f);
            var steel = ArtLibrary.Solid(ArtLibrary.Hex("#C9CED6"), 0.7f);
            ArtLibrary.Box(t, "NougatPlate", c + Vector3.back * 0.03f, new Vector3(0.78f, 0.9f, 0.06f), brown);
            ArtLibrary.Box(t, "NougatTank", c + new Vector3(0f, 0.14f, -0.1f), new Vector3(0.52f, 0.44f, 0.12f), ArtLibrary.Transparent(new Color(0.9f, 0.96f, 1f, 0.45f)), false);
            ArtLibrary.Box(t, "NougatFill", c + new Vector3(0f, 0.07f, -0.1f), new Vector3(0.46f, 0.28f, 0.09f), ArtLibrary.Solid(ArtLibrary.Hex("#7A4A2E"), 0.8f), false);
            ArtLibrary.Box(t, "NougatBand", c + new Vector3(0f, 0.4f, -0.08f), new Vector3(0.7f, 0.08f, 0.04f), ArtLibrary.Solid(ArtLibrary.Hex("#F2C230"), 0.5f), false);
            ArtLibrary.Cylinder(t, "NougatSpout", c + new Vector3(0f, -0.2f, -0.13f), 0.05f, 0.16f, steel);
            ArtLibrary.Box(t, "NougatTray", c + new Vector3(0f, -0.41f, -0.12f), new Vector3(0.36f, 0.04f, 0.18f), steel);
            var disc = ArtLibrary.Cylinder(t, "NougatCrank", c + new Vector3(0.44f, 0.05f, -0.06f), 0.09f, 0.04f, ArtLibrary.Solid(ArtLibrary.Hex("#E8C55B"), 0.5f));
            disc.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            ArtLibrary.Box(t, "NougatArm", c + new Vector3(0.47f, -0.03f, -0.06f), new Vector3(0.03f, 0.18f, 0.03f), steel, false);
            ArtLibrary.Sphere(t, "NougatKnob", c + new Vector3(0.5f, -0.12f, -0.06f), 0.045f, ArtLibrary.Solid(ArtLibrary.Hex("#E8574C"), 0.4f), false);
        }

        /// <summary>The real model (the Godot GLB, front +Z, origin on the floor) replaces the stand-in once loaded.</summary>
        private static async System.Threading.Tasks.Task SwapNougatAsync(GameObject go, Vector3 pos, Quaternion rot)
        {
            var model = await AssetLoader.InstanceAsync(AssetLoader.PropPath(Gooby.Core.Life.Nougat.FurnitureId), go != null ? go.transform : null, "NougatModel");
            if (model == null) return;
            if (go == null) { Object.Destroy(model); return; }
            foreach (Transform child in go.transform)
                if (child != model.transform) child.gameObject.SetActive(false);
            model.transform.SetPositionAndRotation(pos + rot * new Vector3(0f, -pos.y, -0.09f), rot * Quaternion.Euler(0f, 180f, 0f));
            PaintNougatTank(model);
        }

        private static Texture _nougatSource;
        private static Texture2D _nougatPainted;

        /// <summary>
        /// The tank uses the prop palette's ink cell (build_nougat_prop.py had no brown to pick); it is repainted in the
        /// web nougat #5C3A21 so it reads as chocolate instead of a black ball. The palette is read back from the GPU once.
        /// </summary>
        private static void PaintNougatTank(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    var prop = m == null ? null : m.HasProperty("baseColorTexture") ? "baseColorTexture" : m.HasProperty("_BaseMap") ? "_BaseMap" : null;
                    var src = prop != null ? m.GetTexture(prop) : null;
                    if (src == null) continue;
                    if (_nougatPainted == null || _nougatSource != src)
                    {
                        _nougatSource = src;
                        _nougatPainted = Repaint(src, new Color32(0x5C, 0x3A, 0x21, 255));
                    }
                    m.SetTexture(prop, _nougatPainted);
                }
                r.materials = mats;
            }
        }

        private static Texture2D Repaint(Texture src, Color32 ink)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { name = "nougat_palette" };
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            for (var i = 0; i < px.Length; i++)
                if (px[i].r < 40 && px[i].g < 32 && px[i].b < 30) px[i] = new Color32(ink.r, ink.g, ink.b, px[i].a);
            copy.SetPixels32(px);
            copy.filterMode = src.filterMode;
            copy.wrapMode = src.wrapMode;
            copy.Apply(false, true);
            return copy;
        }

        /// <summary>The four painted motifs (build_art: pic_*.png); a frame's motif follows its wall slot so a cluster never repeats.</summary>
        private static readonly string[] Motifs = { "pic_gooby", "pic_cottage", "pic_carrots", "pic_leaf" };

        private static string MotifFor(GridItem item, int shift = 0)
        {
            var wall = item.Wall == "N" ? 0 : item.Wall == "E" ? 1 : item.Wall == "S" ? 2 : 3;
            return Motifs[(item.At.X * 3 + wall + shift) % Motifs.Length];
        }

        /// <summary>Painting facing -Z (into the room): a quad with the motif texture in front of a wooden frame and mat.</summary>
        private static void Painting(Transform t, Vector3 center, float width, float height, string motif, Color frameColor, float frameWidth = 0.035f)
        {
            var frame = ArtLibrary.Solid(frameColor, 0.3f);
            var mat = ArtLibrary.Solid(ArtLibrary.Hex("#FFFDF8"), 0.1f);
            ArtLibrary.Box(t, "Frame", center, new Vector3(width, height, 0.035f), frame, false);
            ArtLibrary.Box(t, "Mat", center + Vector3.back * 0.015f, new Vector3(width - frameWidth * 2f, height - frameWidth * 2f, 0.03f), mat, false);
            ArtLibrary.Quad(t, "Art", center + Vector3.back * 0.04f, Quaternion.Euler(0f, 180f, 0f),
                new Vector2(width - frameWidth * 2f - 0.03f, height - frameWidth * 2f - 0.03f), ArtLibrary.Textured(motif, Vector2.one, Color.white, 0.05f));
        }

        /// <summary>POW!'s poster: the print on white paper, a hair askew, held by two strips of tape at the top corners.</summary>
        private static void BuildPoster(Transform t, Vector3 pos)
        {
            const float w = 0.42f, h = 0.56f;
            var c = pos + Vector3.down * 0.02f;
            ArtLibrary.Box(t, "PosterPaper", c, new Vector3(w + 0.03f, h + 0.03f, 0.006f), ArtLibrary.Solid(ArtLibrary.Hex("#FFFDF8"), 0.1f), false);
            ArtLibrary.Quad(t, "PosterArt", c + Vector3.back * 0.006f, Quaternion.Euler(0f, 180f, 0f), new Vector2(w, h),
                ArtLibrary.Textured("pic_pow_poster", Vector2.one, Color.white, 0.05f));
            var tape = ArtLibrary.Solid(ArtLibrary.Hex("#F3E7C9"), 0.2f);
            foreach (var side in new[] { -1f, 1f })
            {
                var strip = ArtLibrary.Box(t, "PosterTape", c + new Vector3(side * (w * 0.5f - 0.02f), h * 0.5f + 0.005f, -0.008f), new Vector3(0.1f, 0.03f, 0.004f), tape, false);
                strip.transform.rotation = Quaternion.Euler(0f, 0f, -side * 28f);
            }
            foreach (Transform child in t) child.RotateAround(c, Vector3.forward, 1.2f);
        }

        /// <summary>Board 08: framed art in honey wood; the motif changes with the slot so a cluster reads as a gallery.</summary>
        private static void BuildPicture(Transform t, Vector3 pos, float span, GridItem item)
        {
            var size = Mathf.Min(0.46f, span - 0.06f);
            Painting(t, pos, size, size, MotifFor(item), ArtLibrary.Hex("#C99A6B"));
        }

        /// <summary>Accent of a destination on its postcards and stamps (postkarten_props.gd DEST_AKZENT, web recipes).</summary>
        public static readonly IReadOnlyDictionary<string, string> DestAccent = new Dictionary<string, string>
        {
            ["beach"] = "#F6D3BD", ["meadowTrip"] = "#F7A8C4", ["bigCity"] = "#A7B6D4", ["space"] = "#C3C9DA", ["harbor"] = "#E86A5E",
            ["spookGarden"] = "#F0973F", ["bakery"] = "#F6B8D0", ["nightSky"] = "#CFE3F7", ["toyRoom"] = "#F2C94C",
        };

        /// <summary>Cards on the cork board (two rows of three, postkarten_props.gd WAND_MAX_KARTEN).</summary>
        public const int WallCards = 6;

        /// <summary>
        /// The postcard wall (postkarten_props.gd postkartenwand_mit): a cork board in a wooden frame with the newest
        /// cards of the archive pinned on it — paper, the destination's colour band and a red pin, each a little askew.
        /// </summary>
        private static void BuildPostcards(Transform t, Vector3 pos, float span, Newtonsoft.Json.Linq.JArray archive)
        {
            var w = Mathf.Min(1.0f, span - 0.06f);
            const float h = 0.72f;
            var wood = ArtLibrary.Solid(ArtLibrary.Hex("#8A6642"), 0.3f);
            ArtLibrary.Box(t, "Cork", pos, new Vector3(w, h, 0.025f), ArtLibrary.Pbr("plaster", 0.6f, ArtLibrary.Hex("#C9A377"), 0.05f, 0.9f));
            foreach (var s in new[] { -1f, 1f })
            {
                ArtLibrary.Box(t, "CorkFrame", pos + new Vector3(s * (w + 0.05f) * 0.5f, 0f, -0.004f), new Vector3(0.05f, h + 0.1f, 0.035f), wood, false);
                ArtLibrary.Box(t, "CorkFrame", pos + new Vector3(0f, s * (h + 0.05f) * 0.5f, -0.004f), new Vector3(w + 0.1f, 0.05f, 0.035f), wood, false);
            }
            var paper = ArtLibrary.Solid(ArtLibrary.Hex("#FFFAF2"), 0.1f);
            var ink = ArtLibrary.Solid(ArtLibrary.Hex("#B9AFA3"), 0.1f);
            var pin = ArtLibrary.Solid(ArtLibrary.Hex("#E0655F"), 0.45f);
            // before the first trip two greeting cards from Sonnental keep the board from looking bare
            var bands = new List<string>();
            var first = Mathf.Max(0, archive.Count - WallCards);
            for (var i = first; i < archive.Count; i++)
            {
                var dest = archive[i] is Newtonsoft.Json.Linq.JObject entry ? Gooby.Core.Json.J.Str(entry["destId"]) : "";
                bands.Add(DestAccent.TryGetValue(dest, out var hex) ? hex : "#DCCFBE");
            }
            if (bands.Count == 0) bands.AddRange(new[] { "#FFC9A6", "#B7D8FF" });
            for (var i = 0; i < bands.Count; i++)
            {
                var accent = ArtLibrary.Solid(ArtLibrary.Hex(bands[i]), 0.15f);
                var row = i / 3;
                var col = i % 3;
                var c = pos + new Vector3((-0.3f + col * 0.3f) * w, 0.17f - row * 0.3f, -0.02f);
                var parts = new List<GameObject>
                {
                    ArtLibrary.Box(t, "Card", c, new Vector3(0.24f, 0.17f, 0.008f), paper, false),
                    ArtLibrary.Box(t, "CardBand", c + new Vector3(0f, 0.055f, -0.0015f), new Vector3(0.24f, 0.045f, 0.009f), accent, false),
                    ArtLibrary.Box(t, "CardLine", c + new Vector3(-0.03f, -0.005f, -0.006f), new Vector3(0.14f, 0.01f, 0.002f), ink, false),
                    ArtLibrary.Box(t, "CardLine", c + new Vector3(-0.045f, -0.035f, -0.006f), new Vector3(0.11f, 0.01f, 0.002f), ink, false),
                    ArtLibrary.Box(t, "CardStamp", c + new Vector3(0.08f, -0.025f, -0.006f), new Vector3(0.045f, 0.055f, 0.003f), accent, false),
                    ArtLibrary.Sphere(t, "Pin", c + new Vector3(0f, 0.075f, -0.01f), 0.012f, pin, false),
                };
                var tilt = i % 2 == 0 ? 3.4f : -2.9f;
                foreach (var p in parts) p.transform.RotateAround(c, Vector3.forward, tilt);
            }
        }

        /// <summary>One shape of a souvenir mini (web souvenirShelf.js MINI_SPECS): local position above the plank.</summary>
        private readonly struct MiniPart
        {
            public readonly string Shape, Color;
            public readonly Vector3 Size, At, Scale, Rot;
            public MiniPart(string shape, Vector3 size, string color, Vector3 at, Vector3? scale = null, Vector3? rot = null)
            {
                Shape = shape; Size = size; Color = color; At = at; Scale = scale ?? Vector3.one; Rot = rot ?? Vector3.zero;
            }
        }

        private static readonly Dictionary<string, MiniPart[]> Minis = new Dictionary<string, MiniPart[]>
        {
            ["beach"] = new[]
            {
                new MiniPart("sphere", new Vector3(0.048f, 0f, 0f), "#F6D3BD", new Vector3(0f, 0.028f, 0f), new Vector3(1f, 0.58f, 0.85f)),
                new MiniPart("sphere", new Vector3(0.013f, 0f, 0f), "#FFF4E4", new Vector3(0.028f, 0.013f, 0.032f)),
            },
            ["meadowTrip"] = new[]
            {
                new MiniPart("cyl", new Vector3(0.026f, 0.03f, 0.055f), "#DDEEDF", new Vector3(0f, 0.0275f, 0f)),
                new MiniPart("cyl", new Vector3(0.004f, 0.004f, 0.05f), "#7FB069", new Vector3(0f, 0.08f, 0f)),
                new MiniPart("sphere", new Vector3(0.019f, 0f, 0f), "#F7A8C4", new Vector3(0f, 0.112f, 0f)),
                new MiniPart("sphere", new Vector3(0.008f, 0f, 0f), "#FFE28A", new Vector3(0f, 0.112f, 0.017f)),
            },
            ["bigCity"] = new[]
            {
                new MiniPart("box", new Vector3(0.105f, 0.012f, 0.05f), "#C9CFDD", new Vector3(0f, 0.006f, 0f)),
                new MiniPart("box", new Vector3(0.026f, 0.062f, 0.026f), "#8FA1C0", new Vector3(-0.032f, 0.043f, 0.004f)),
                new MiniPart("box", new Vector3(0.026f, 0.095f, 0.026f), "#A7B6D4", new Vector3(0f, 0.0595f, -0.006f)),
                new MiniPart("box", new Vector3(0.026f, 0.048f, 0.026f), "#7C8DB0", new Vector3(0.033f, 0.036f, 0.006f)),
            },
            ["space"] = new[]
            {
                new MiniPart("sphere", new Vector3(0.042f, 0f, 0f), "#C3C9DA", new Vector3(0f, 0.036f, 0f)),
                new MiniPart("sphere", new Vector3(0.012f, 0f, 0f), "#98A1BA", new Vector3(0.02f, 0.06f, 0.014f)),
            },
            ["harbor"] = new[]
            {
                new MiniPart("cyl", new Vector3(0.02f, 0.027f, 0.06f), "#E86A5E", new Vector3(0f, 0.03f, 0f)),
                new MiniPart("cyl", new Vector3(0.016f, 0.02f, 0.045f), "#FFF6E8", new Vector3(0f, 0.0825f, 0f)),
                new MiniPart("cyl", new Vector3(0.013f, 0.013f, 0.018f), "#FFE28A", new Vector3(0f, 0.114f, 0f)),
                new MiniPart("cone", new Vector3(0.018f, 0.026f, 0f), "#E86A5E", new Vector3(0f, 0.136f, 0f)),
            },
            ["spookGarden"] = new[]
            {
                new MiniPart("sphere", new Vector3(0.042f, 0f, 0f), "#F0973F", new Vector3(0f, 0.033f, 0f), new Vector3(1f, 0.78f, 1f)),
                new MiniPart("cyl", new Vector3(0.006f, 0.008f, 0.022f), "#7B9A56", new Vector3(0.004f, 0.073f, 0f), null, new Vector3(0f, 0f, 0.25f)),
            },
            ["bakery"] = new[]
            {
                new MiniPart("cyl", new Vector3(0.03f, 0.023f, 0.038f), "#E8C9A0", new Vector3(0f, 0.019f, 0f)),
                new MiniPart("sphere", new Vector3(0.03f, 0f, 0f), "#F6B8D0", new Vector3(0f, 0.05f, 0f), new Vector3(1f, 0.72f, 1f)),
                new MiniPart("sphere", new Vector3(0.009f, 0f, 0f), "#DE5449", new Vector3(0f, 0.078f, 0f)),
            },
            ["nightSky"] = new[]
            {
                new MiniPart("cyl", new Vector3(0.027f, 0.027f, 0.062f), "#CFE3F7", new Vector3(0f, 0.031f, 0f)),
                new MiniPart("cyl", new Vector3(0.029f, 0.029f, 0.012f), "#A9805A", new Vector3(0f, 0.068f, 0f)),
                new MiniPart("sphere", new Vector3(0.017f, 0f, 0f), "#FFD966", new Vector3(0f, 0.089f, 0f), new Vector3(1f, 0.8f, 1f)),
            },
            ["toyRoom"] = new[]
            {
                new MiniPart("box", new Vector3(0.048f, 0.048f, 0.048f), "#F2C94C", new Vector3(0f, 0.024f, 0f), null, new Vector3(0f, 0.26f, 0f)),
                new MiniPart("box", new Vector3(0.036f, 0.036f, 0.036f), "#7FB3E8", new Vector3(0.006f, 0.066f, 0.004f), null, new Vector3(0f, -0.35f, 0f)),
            },
        };

        /// <summary>Minis read ×1.2 bigger than the web recipe (postkarten_props.gd MINI_SCALE), still inside a slot.</summary>
        private const float MiniScale = 1.2f;

        /// <summary>
        /// The souvenir shelf (web souvenirShelf.js via postkarten_props.gd): a plank on brackets with a back board and one
        /// fixed slot per destination; every destination Gooby has visited puts its little souvenir there — a shell, a
        /// flower pot, a skyline, a moon rock, a lighthouse, a pumpkin, a cupcake, a star jar, toy blocks.
        /// </summary>
        private static void BuildSouvenirShelf(Transform t, Vector3 pos, float span, IList<string> visited)
        {
            var plankW = Mathf.Min(1.5f, span - 0.06f);
            const float plankT = 0.035f, plankD = 0.17f, backH = 0.24f, backT = 0.022f;
            var boardY = pos.y - 0.12f;
            var plankZ = pos.z - plankD * 0.5f;
            ArtLibrary.Box(t, "Board", new Vector3(pos.x, boardY, plankZ), new Vector3(plankW, plankT, plankD), ArtLibrary.Solid(ArtLibrary.Hex("#B98A62"), 0.3f));
            ArtLibrary.Box(t, "ShelfBack", new Vector3(pos.x, boardY + backH * 0.5f, pos.z - backT * 0.5f), new Vector3(plankW, backH, backT), ArtLibrary.Solid(ArtLibrary.Hex("#CBA478"), 0.25f), false);
            foreach (var s in new[] { -1f, 1f })
                ArtLibrary.Box(t, "Bracket", new Vector3(pos.x + s * (plankW * 0.5f - 0.12f), boardY - 0.07f, pos.z - 0.06f), new Vector3(0.03f, 0.1f, 0.1f), ArtLibrary.Solid(ArtLibrary.Hex("#8A6642"), 0.3f), false);
            var top = boardY + plankT * 0.5f;
            if (visited == null || visited.Count == 0)
            {
                // nothing brought home yet: books, a little plant and a standing frame wait on the plank
                var bookColors = new[] { "#FF8B8B", "#7FCF9B", "#B7D8FF" };
                for (var i = 0; i < 3; i++)
                    ArtLibrary.Box(t, "Book", new Vector3(pos.x - plankW * 0.5f + 0.12f + i * 0.045f, top + 0.09f, plankZ), new Vector3(0.035f, 0.18f - i * 0.015f, 0.13f), ArtLibrary.Solid(ArtLibrary.Hex(bookColors[i]), 0.2f), false);
                ArtLibrary.Cylinder(t, "Pot", new Vector3(pos.x, top + 0.04f, plankZ), 0.045f, 0.08f, ArtLibrary.Solid(ArtLibrary.Hex("#E7A67A"), 0.2f), false);
                ArtLibrary.Sphere(t, "Leaves", new Vector3(pos.x, top + 0.13f, plankZ), 0.065f, ArtLibrary.Solid(ArtLibrary.Hex("#6FBF5A"), 0.1f), false);
                Painting(t, new Vector3(pos.x + plankW * 0.5f - 0.14f, top + 0.08f, plankZ + 0.01f), 0.14f, 0.14f, "pic_gooby", ArtLibrary.Hex("#FFFDF8"), 0.012f);
                return;
            }
            var pitch = 0.15f * plankW / 1.5f;
            var ids = Gooby.Core.Life.Postcards.DestIds;
            for (var i = 0; i < ids.Length; i++)
            {
                if (visited == null || !visited.Contains(ids[i]) || !Minis.TryGetValue(ids[i], out var parts)) continue;
                var slot = new Vector3(pos.x + (-0.6f * plankW / 1.5f) + i * pitch, top, plankZ - 0.01f);
                foreach (var p in parts)
                {
                    var at = slot + new Vector3(p.At.x, p.At.y, -p.At.z) * MiniScale;
                    var m = ArtLibrary.Solid(ArtLibrary.Hex(p.Color), 0.2f);
                    GameObject go;
                    switch (p.Shape)
                    {
                        case "box":
                            go = ArtLibrary.Box(t, "Souvenir:" + ids[i], at, p.Size * MiniScale, m, false);
                            break;
                        case "cyl":
                            go = ArtLibrary.Cylinder(t, "Souvenir:" + ids[i], at, (p.Size.x + p.Size.y) * 0.5f * MiniScale, p.Size.z * MiniScale, m, false);
                            break;
                        case "cone":
                            go = ArtLibrary.Cylinder(t, "Souvenir:" + ids[i], at, p.Size.x * 0.55f * MiniScale, p.Size.y * MiniScale, m, false);
                            break;
                        default:
                            go = ArtLibrary.Sphere(t, "Souvenir:" + ids[i], at, p.Size.x * MiniScale, m, false);
                            break;
                    }
                    go.transform.localScale = Vector3.Scale(go.transform.localScale, p.Scale);
                    go.transform.rotation = Quaternion.Euler(p.Rot * Mathf.Rad2Deg);
                }
            }
        }
    }

    /// <summary>Shared placeholder/prop fitting for BuildSession + RoomView.</summary>
    public static class PropPlaceholder
    {
        /// <summary>
        /// Grid → world: x east, y south. Unity is left-handed, so the room is laid out with its north edge at the
        /// far (+Z) side and the camera looks along +Z; that keeps east on screen-right like the Godot rooms.
        /// </summary>
        public static Vector3 CellCenter(Vector3 origin, int gridH, Cell at, int fw, int fh, int rot)
        {
            var c = GridData.WorldCenter(at, fw, fh, rot);
            return new Vector3(origin.x + (float)c.x, 0f, origin.z + gridH * (float)GridData.CellSize - (float)c.z);
        }

        /// <summary>glTFast only negates X: a Blender prop's front (-Y) ends up at Unity +Z, so rot 0 turns it towards the camera (-Z).</summary>
        public static Quaternion Rotation(int rot) => Quaternion.Euler(0f, 180f - rot * 90f, 0f);

        public static void FitProp(GameObject prop, FurnitureDef def, Cell at, int rot, Vector3 origin, int gridH)
        {
            var sx = def.FootprintW * (float)GridData.CellSize;
            var sz = def.FootprintH * (float)GridData.CellSize;
            prop.transform.position = CellCenter(origin, gridH, at, def.FootprintW, def.FootprintH, rot);
            prop.transform.rotation = Rotation(rot);
            var y = def.Layer == GridLayer.Rug ? 1f : Mathf.Lerp(Mathf.Min(sx, sz), Mathf.Max(sx, sz), 0.5f);
            prop.transform.localScale = new Vector3(sx, y, sz);
            if (def.Layer == GridLayer.Surface) prop.transform.position += Vector3.up * 0.45f;
            if (def.Layer == GridLayer.Ceiling) HangFromCeiling(prop, origin.y);
        }

        /// <summary>Model-unit height of the top board on models with decor baked onto it (their bounds would lift pieces onto the decor).</summary>
        private static readonly Dictionary<string, float> BakedTops = new Dictionary<string, float> { ["table_low"] = 0.43f };

        /// <summary>The floor piece with a surface (table, cabinet, shelf, crate …) under a surface piece's cell, or null.</summary>
        public static GridItem SupportOf(GridItem item, IEnumerable<GridItem> items)
        {
            if (item?.Def == null || item.Def.Layer != GridLayer.Surface) return null;
            foreach (var other in items)
            {
                if (other?.Def == null || other == item || other.Wall != null || other.Def.Layer != GridLayer.Floor || !other.Def.Surface) continue;
                if (GridData.CellsFor(other.At, other.Def.FootprintW, other.Def.FootprintH, other.Rot).Contains(item.At)) return other;
            }
            return null;
        }

        /// <summary>
        /// Stands a surface piece's model on its support's model. <see cref="FitProp"/> lifts every surface piece by a flat
        /// 45 cm — that buried a goldfish bowl in the 69-cm TV dresser and lamps in the kitchen cabinets and let the bear float
        /// over its crate; the support's top (its bounds, or the board under baked decor) decides instead.
        /// </summary>
        public static void SeatOnSupport(GameObject piece, GridItem support, GameObject supportModel)
        {
            if (piece == null || support == null || supportModel == null) return;
            float top;
            if (BakedTops.TryGetValue(AssetLoader.ArchetypeFor(support.Def.Id), out var board))
                top = supportModel.transform.position.y + board * supportModel.transform.lossyScale.y;
            else
            {
                var rs = supportModel.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                top = b.max.y;
            }
            var p = piece.transform.position;
            piece.transform.position = new Vector3(p.x, top, p.z);
        }

        /// <summary>Height of the rooms' ceiling (the walls' top) above the floor.</summary>
        public const float CeilingHeight = 2.6f;

        /// <summary>
        /// Ceiling pieces (lamps, lanterns, garlands, the disco ball) are modelled with the cord at the top: at the shared
        /// prop scale they would stand at waist height, so the whole piece is lifted until its top meets the ceiling.
        /// </summary>
        private static void HangFromCeiling(GameObject prop, float floorY)
        {
            var renderers = prop.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            prop.transform.position += Vector3.up * (floorY + CeilingHeight - 0.02f - b.max.y);
        }

        /// <summary>
        /// Rug-layer items with their own Blender model (board 05's round stepping stones `path_tile`, the leaf door mat
        /// `doormat` of boards 08/09): the flat quad from <see cref="MakeTextured"/> is only their placeholder and the
        /// caller swaps the GLB in once it loads — before, the Steinweg lay as cream squares on the lawn.
        /// </summary>
        public static bool HasRugModel(string itemId)
        {
            if (itemId == "rugRound") return false;
            var archetype = AssetLoader.ArchetypeFor(itemId);
            return archetype == "path_tile" || archetype == "doormat";
        }

        /// <summary>Items with a painted texture (the round leaf rug of board 08) are built here instead of a GLB.</summary>
        public static GameObject MakeTextured(Transform parent, FurnitureDef def, Cell at, int rot, Vector3 origin, int gridH)
        {
            if (def.Layer != GridLayer.Rug) return null;
            var (w, h) = GridData.RotatedFootprint(def.FootprintW, def.FootprintH, rot);
            var center = CellCenter(origin, gridH, at, def.FootprintW, def.FootprintH, rot) + Vector3.up * 0.012f;
            var size = new Vector2(w * (float)GridData.CellSize, h * (float)GridData.CellSize);
            var go = new GameObject("Rug:" + def.Id);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var round = def.Id == "rugRound";
            var mat = round ? ArtLibrary.Cutout("tex_rug_round") : ArtLibrary.Solid(RugColor(def.Id), 0.05f);
            var quad = ArtLibrary.Quad(go.transform, "Top", center, Quaternion.Euler(-90f, 0f, 0f), size, mat);
            quad.transform.SetParent(go.transform, true);
            if (!round)
            {
                // fabric border for the rectangular rugs (board 08: cream mats with a darker rim)
                var rim = ArtLibrary.Solid(RugColor(def.Id) * 0.9f, 0.05f);
                ArtLibrary.Box(go.transform, "Rim", center - Vector3.up * 0.004f, new Vector3(size.x + 0.06f, 0.012f, size.y + 0.06f), rim, false);
            }
            return go;
        }

        private static Color RugColor(string id)
        {
            switch (id)
            {
                case "rugDoormat": return ArtLibrary.Hex("#E7D2B8");
                case "rugSquare": return ArtLibrary.Hex("#FFE0E6");
                case "rugRounded": return ArtLibrary.Hex("#DDEBD5");
                default: return ArtLibrary.Hex("#F6E9D7");
            }
        }

        public static GameObject Make(Transform parent, FurnitureDef def, Cell at, int rot, Vector3 origin, int gridH, Shader lit)
        {
            var (w, h) = GridData.RotatedFootprint(def.FootprintW, def.FootprintH, rot);
            var size = new Vector3(w * (float)GridData.CellSize * 0.92f, HeightFor(def), h * (float)GridData.CellSize * 0.92f);
            var center = CellCenter(origin, gridH, at, def.FootprintW, def.FootprintH, rot);
            var pos = center + Vector3.up * size.y * 0.5f + (def.Layer == GridLayer.Surface ? Vector3.up * 0.45f : Vector3.zero);
            return ArtLibrary.Box(parent, "Placeholder:" + def.Id, pos, size, ArtLibrary.Solid(ColorFor(def)));
        }

        public static float HeightFor(FurnitureDef def)
        {
            switch (def.Layer)
            {
                case GridLayer.Rug: return 0.02f;
                case GridLayer.Surface: return 0.25f;
                case GridLayer.Ceiling: return 0.2f;
            }
            switch (def.Kategorie)
            {
                case "schlafen": return 0.5f;
                case "sitzen": return 0.55f;
                case "tische": return 0.45f;
                case "regale": return 1.6f;
                case "lampen": return 1.4f;
                case "pflanzen": return 0.7f;
                case "garten": return 0.6f;
                case "kueche": return 0.9f;
                case "bad": return 0.7f;
            }
            return 0.5f;
        }

        public static Color ColorFor(FurnitureDef def)
        {
            switch (def.Kategorie)
            {
                case "pflanzen": case "garten": return ArtLibrary.Hex("7FCF9B");
                case "sitzen": case "schlafen": return ArtLibrary.Hex("FF8B8B");
                case "tische": case "regale": return ArtLibrary.Hex("D9B48A");
                case "lampen": return ArtLibrary.Hex("FFD166");
                case "teppiche": return ArtLibrary.Hex("F6E9D7");
                case "kueche": return ArtLibrary.Hex("EBD9C6");
                case "bad": return ArtLibrary.Hex("B7D8FF");
                case "party": return ArtLibrary.Hex("FF98C1");
            }
            return ArtLibrary.Hex("C8B7A1");
        }
    }
}
