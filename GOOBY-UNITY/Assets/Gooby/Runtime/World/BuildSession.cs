using System;
using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.State;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// A build-mode session for one room: owns the GridData rebuilt from the save, renders every placed item as a
    /// footprint-sized placeholder (category-coloured until the Blender props land), places items from storage on
    /// tapped cells, supports rotate/undo (undo returns the item to storage) and commits the grid back into home.rooms.
    /// World mapping: cell (x,y) → world (Origin.x + x*0.5, 0, Origin.z + y*0.5); Origin is the room's north-west corner.
    /// </summary>
    public sealed class BuildSession
    {
        public string RoomId { get; }
        public GridData Grid { get; private set; }
        public Vector3 Origin { get; }
        public string SelectedItem { get; private set; }
        public int Rotation { get; private set; }
        public string LastMessage { get; private set; } = "";
        public int PlacedThisSession => _undo.Count;

        public event Action Changed;
        /// <summary>Every grid change as (op place|remove, item id, cell, rot) — the host relays it to a visiting friend (BUILD_DELTA).</summary>
        public event Action<string, string, Cell, int> Delta;
        /// <summary>A garland went up or came down (the room view redraws them; they are not grid pieces).</summary>
        public event Action GarlandsChanged;

        /// <summary>The first ceiling cell of a garland being spanned (girlanden_bau.gd's point A), null while none.</summary>
        public Cell? GarlandStart { get; private set; }
        private GameObject _garlandMark;
        private const string GarlandUndo = "girlande:";

        private readonly GameStateStore _gs;
        private readonly Transform _parent;
        private readonly Dictionary<string, GameObject> _views = new Dictionary<string, GameObject>();
        /// <summary>Pieces whose Blender model has replaced the placeholder (surface pieces stand on their support's model).</summary>
        private readonly HashSet<string> _modelIn = new HashSet<string>();
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Shader _lit;
        private GameObject _preview;

        public BuildSession(GameStateStore gs, string roomId, Transform parent, Vector3 origin, Shader lit)
        {
            _gs = gs;
            RoomId = roomId;
            _parent = parent;
            Origin = origin;
            _lit = lit;
            HomeState.EnsureInitialized(gs);
            Grid = HomeState.LoadRoomGrid(gs, roomId);
            foreach (var item in Grid.Items) SpawnView(item);
        }

        public void Select(string itemId)
        {
            SelectedItem = itemId;
            if (!string.IsNullOrEmpty(itemId)) FocusedUid = null;
            SetGarlandStart(null);
            UpdatePreview(null);
            Changed?.Invoke();
        }

        public void Rotate()
        {
            Rotation = (Rotation + 1) % 4;
            Changed?.Invoke();
        }

        public Cell CellAt(Vector3 world) => new Cell(
            Mathf.FloorToInt((world.x - Origin.x) / (float)GridData.CellSize),
            Mathf.FloorToInt((Origin.z + Grid.Height * (float)GridData.CellSize - world.z) / (float)GridData.CellSize));

        public Vector3 CellCenter(Cell at, int fw, int fh, int rot) => PropPlaceholder.CellCenter(Origin, Grid.Height, at, fw, fh, rot);

        /// <summary>Placed item that is currently focused (board 09: green frame + rotate/duplicate/delete).</summary>
        public string FocusedUid { get; private set; }
        public GridItem Focused => string.IsNullOrEmpty(FocusedUid) ? null : Grid.GetItem(FocusedUid);
        public GameObject FocusedView => !string.IsNullOrEmpty(FocusedUid) && _views.TryGetValue(FocusedUid, out var v) ? v : null;

        /// <summary>
        /// Screen tap → place the selected storage item (pictures, mirrors and wall lamps on the wall the tap points at);
        /// with nothing selected, tap a placed item to focus it (the floor, a picture on a wall, a lamp from the ceiling),
        /// tap a free cell — or a free spot on a wall for a picture — while an item is focused to move it there, tap
        /// empty ground to clear the focus.
        /// </summary>
        public bool TapAt(Ray ray)
        {
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out var enter)) return false;
            var cell = CellAt(ray.GetPoint(enter));
            string wall = null;
            var along = 0f;
            var onWall = HasWalls && WallAt(ray, out wall, out along);
            if (!string.IsNullOrEmpty(SelectedItem))
            {
                if (Girlanden.Ist(SelectedItem)) return GarlandTap(ray);
                if (FurnitureCatalog.Def(SelectedItem)?.Layer != GridLayer.Wall) return Grid.InBounds(cell) ? Place(cell) : Refuse("oob");
                return onWall ? PlaceOnWall(wall, along) : Refuse(HasWalls ? "wall" : "oob");
            }
            if (HasWalls && TakeDownGarlandAt(ray)) return true;
            var hit = Grid.InBounds(cell) ? ItemAtCell(cell) : "";
            if (hit == "" && onWall)
            {
                hit = WallItemAt(wall, along);
                if (hit == "" && Focused?.Wall != null) return MoveFocusedOnWall(wall, along);
            }
            if (hit != "" && hit != FocusedUid) { FocusedUid = hit; LastMessage = "focused"; Changed?.Invoke(); return true; }
            if (hit == "" && Focused != null && Focused.Wall == null && Grid.InBounds(cell)) return MoveFocused(cell);
            if (hit == "") { ClearFocus(); LastMessage = Grid.InBounds(cell) ? "empty" : "oob"; Changed?.Invoke(); return false; }
            return true;
        }

        private bool Refuse(string message)
        {
            LastMessage = message;
            Changed?.Invoke();
            return false;
        }

        // ── garlands (girlanden_bau.gd): two taps under the ceiling span one, a tap on an end takes it down ──

        /// <summary>The cell under the point where the tap meets the ceiling (the camera looks down through the open room).</summary>
        private Cell CeilingCell(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            var plane = new Plane(Vector3.up, new Vector3(0f, Origin.y + PropPlaceholder.CeilingHeight - 0.05f, 0f));
            if (!plane.Raycast(ray, out var enter)) return new Cell(-1, -1);
            point = ray.GetPoint(enter);
            return CellAt(point);
        }

        private bool GarlandTap(Ray ray)
        {
            if (!HasWalls) return Refuse("girlande_drinnen");
            var cell = CeilingCell(ray, out _);
            if (!Grid.InBounds(cell)) return Refuse("girlande_ungueltig");
            if (GarlandStart == null)
            {
                SetGarlandStart(cell);
                LastMessage = "girlande_b";
                Changed?.Invoke();
                return true;
            }
            if (GarlandStart.Value.Equals(cell)) return Refuse("girlande_ungueltig");
            var typ = SelectedItem;
            var result = Girlanden.Spannen(_gs, RoomId, typ, GarlandStart.Value, cell, Grid.Width, Grid.Height);
            SetGarlandStart(null);
            if (result != Girlanden.Gespannt) return Refuse(result == Girlanden.Ungueltig ? "girlande_ungueltig" : result);
            _undo.Push(GarlandUndo + typ);
            GarlandsChanged?.Invoke();
            Effects.Sparkle(PropPlaceholder.CellCenter(Origin, Grid.Height, cell, 1, 1, 0) + Vector3.up * (PropPlaceholder.CeilingHeight - 0.3f), 0.45f);
            LastMessage = "girlande_haengt";
            if (StorageLogic.CountOf(HomeState.Storage(_gs), typ) <= 0) SelectedItem = null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>A tap close to a garland's end (under the ceiling) takes the garland down into the storage.</summary>
        private bool TakeDownGarlandAt(Ray ray)
        {
            var liste = Girlanden.Liste(_gs, RoomId);
            if (liste.Count == 0) return false;
            CeilingCell(ray, out var point);
            if (point == Vector3.zero) return false;
            var best = -1;
            var bestDist = 0.45f;
            for (var i = 0; i < liste.Count; i++)
            {
                if (!Girlanden.Gueltig(liste[i])) continue;
                foreach (var key in new[] { "zelle_a", "zelle_b" })
                {
                    var end = PropPlaceholder.CellCenter(Origin, Grid.Height, Girlanden.Zelle(liste[i], key), 1, 1, 0);
                    var d = Vector2.Distance(new Vector2(end.x, end.z), new Vector2(point.x, point.z));
                    if (d < bestDist) { bestDist = d; best = i; }
                }
            }
            if (best < 0) return false;
            if (!Girlanden.Abnehmen(_gs, RoomId, best))
            {
                Refuse("storage_full");
                return true;
            }
            ClearFocus();
            GarlandsChanged?.Invoke();
            LastMessage = "girlande_entfernt";
            Changed?.Invoke();
            return true;
        }

        private void SetGarlandStart(Cell? cell)
        {
            GarlandStart = cell;
            if (_garlandMark != null) { UnityEngine.Object.Destroy(_garlandMark); _garlandMark = null; }
            if (cell == null) return;
            // a little glowing hook where the first end will hang
            var p = PropPlaceholder.CellCenter(Origin, Grid.Height, cell.Value, 1, 1, 0) + Vector3.up * (PropPlaceholder.CeilingHeight - 0.05f);
            _garlandMark = new GameObject("GarlandStart");
            _garlandMark.transform.SetParent(_parent, false);
            ArtLibrary.Sphere(_garlandMark.transform, "Hook", p, 0.06f, ArtLibrary.Glow(new Color(1.6f, 1.25f, 0.6f)), false);
            ArtLibrary.Cylinder(_garlandMark.transform, "Drop", p + Vector3.down * 0.35f, 0.012f, 0.7f, ArtLibrary.Transparent(new Color(1f, 0.85f, 0.45f, 0.5f)), false);
        }

        /// <summary>Rooms have walls to hang things on; the lawn does not.</summary>
        private bool HasWalls => RoomId != "garden";

        /// <summary>
        /// The wall the tap points at: the ray against the planes of the north, west and east walls (the south side is open
        /// to the camera), hit between floor and ceiling; `along` = metres along the wall in the grid's order (north from
        /// the west, west and east from the north).
        /// </summary>
        public bool WallAt(Ray ray, out string wall, out float along)
        {
            wall = null;
            along = 0f;
            var w = Grid.Width * (float)GridData.CellSize;
            var d = Grid.Height * (float)GridData.CellSize;
            var best = float.MaxValue;
            foreach (var (id, plane) in new[]
            {
                ("N", new Plane(Vector3.back, new Vector3(0f, 0f, Origin.z + d))),
                ("W", new Plane(Vector3.right, new Vector3(Origin.x, 0f, 0f))),
                ("E", new Plane(Vector3.left, new Vector3(Origin.x + w, 0f, 0f))),
            })
            {
                if (!plane.Raycast(ray, out var enter) || enter >= best) continue;
                var p = ray.GetPoint(enter);
                var a = id == "N" ? p.x - Origin.x : Origin.z + d - p.z;
                if (p.y < 0f || p.y > PropPlaceholder.CeilingHeight || a < 0f || a > (id == "N" ? w : d)) continue;
                best = enter;
                wall = id;
                along = a;
            }
            return wall != null;
        }

        /// <summary>The first slot of a piece `span` slots wide centred on `along`, kept on the wall.</summary>
        private int WallOffset(string wall, float along, int span) =>
            Mathf.Clamp(Mathf.FloorToInt(along / (float)GridData.CellSize) - span / 2, 0, Mathf.Max(0, Grid.WallWidth(wall) - span));

        /// <summary>The picture hanging at that spot (the ray meets the wall itself, so the slot is exact — a free slot beside a
        /// picture stays free to move another one into).</summary>
        private string WallItemAt(string wall, float along) => Grid.WallItemAt(wall, WallOffset(wall, along, 1));

        /// <summary>The selected wall piece from the storage onto the wall (same bookkeeping as <see cref="Place"/>).</summary>
        public bool PlaceOnWall(string wall, float along)
        {
            var def = FurnitureCatalog.Def(SelectedItem);
            if (def == null) return Refuse(GridData.ReasonUnknownItem);
            if (StorageLogic.CountOf(HomeState.Storage(_gs), def.Id) <= 0) return Refuse("storage_empty");
            var offset = WallOffset(wall, along, Mathf.Max(1, def.WallSize));
            var check = Grid.CanPlaceWall(def, wall, offset);
            if (!check.Ok) return Refuse(check.Reason);
            var uid = HomeState.NextUid(_gs);
            Grid.PlaceWall(def, wall, offset, uid);
            var variant = HomeState.TakeVariant(_gs, def.Id);
            if (!FurnitureVariants.IsNeutral(variant)) Grid.GetItem(uid).Variant = variant;
            SpawnView(Grid.GetItem(uid));
            if (_views.TryGetValue(uid, out var view) && view != null) Effects.Sparkle(view.transform.position, 0.35f);
            _undo.Push(uid);
            HomeState.SaveRoomGrid(_gs, RoomId, Grid);
            LastMessage = "placed";
            if (StorageLogic.CountOf(HomeState.Storage(_gs), def.Id) <= 0) SelectedItem = null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Moves the focused picture to another free spot on a wall.</summary>
        public bool MoveFocusedOnWall(string wall, float along)
        {
            var item = Focused;
            if (item == null || item.Wall == null) return false;
            var offset = WallOffset(wall, along, Mathf.Max(1, item.Def.WallSize));
            var check = Grid.CanPlaceWall(item.Def, wall, offset, item.Uid);
            if (!check.Ok) return Refuse(check.Reason);
            var def = item.Def;
            var uid = item.Uid;
            var variant = item.Variant;
            Grid.RemoveItem(uid);
            DestroyView(uid);
            Grid.PlaceWall(def, wall, offset, uid);
            Grid.GetItem(uid).Variant = variant;
            SpawnView(Grid.GetItem(uid));
            HomeState.SaveRoomGrid(_gs, RoomId, Grid);
            LastMessage = "moved";
            Changed?.Invoke();
            return true;
        }

        public void ClearFocus()
        {
            FocusedUid = null;
        }

        private string ItemAtCell(Cell cell)
        {
            var uid = Grid.ItemAt(cell, GridLayer.Surface);
            if (uid == "") uid = Grid.ItemAt(cell, GridLayer.Floor);
            if (uid == "") uid = Grid.ItemAt(cell, GridLayer.Rug);
            if (uid == "") uid = Grid.ItemAt(cell, GridLayer.Ceiling);
            return uid;
        }

        /// <summary>Moves the focused item to another cell (same rotation); refused placements keep it where it is.</summary>
        public bool MoveFocused(Cell cell)
        {
            var item = Focused;
            if (item == null) return false;
            var check = Grid.CanPlace(item.Def, cell, item.Rot, item.Uid);
            if (!check.Ok) { LastMessage = check.Reason; Changed?.Invoke(); return false; }
            var def = item.Def; var rot = item.Rot; var uid = item.Uid; var from = item.At; var variant = item.Variant;
            Grid.RemoveItem(uid);
            DestroyView(uid);
            Grid.Place(def, cell, rot, uid);
            Grid.GetItem(uid).Variant = variant;
            SpawnView(Grid.GetItem(uid));
            HomeState.SaveRoomGrid(_gs, RoomId, Grid);
            Delta?.Invoke(VisitBuild.OpRemove, def.Id, from, rot);
            Delta?.Invoke(VisitBuild.OpPlace, def.Id, cell, rot);
            LastMessage = "moved";
            Changed?.Invoke();
            return true;
        }

        /// <summary>Rotates the focused item in place (falls back to the old cell when the turned footprint does not fit).</summary>
        public bool RotateFocused()
        {
            var item = Focused;
            if (item == null) { Rotate(); return true; }
            if (item.Wall != null) return Refuse("wall_flat");
            var next = (item.Rot + 1) % 4;
            var check = Grid.CanPlace(item.Def, item.At, next, item.Uid);
            if (!check.Ok) { LastMessage = check.Reason; Changed?.Invoke(); return false; }
            var def = item.Def; var at = item.At; var uid = item.Uid; var was = item.Rot; var variant = item.Variant;
            Grid.RemoveItem(uid);
            DestroyView(uid);
            Grid.Place(def, at, next, uid);
            Grid.GetItem(uid).Variant = variant;
            SpawnView(Grid.GetItem(uid));
            HomeState.SaveRoomGrid(_gs, RoomId, Grid);
            Delta?.Invoke(VisitBuild.OpRemove, def.Id, at, was);
            Delta?.Invoke(VisitBuild.OpPlace, def.Id, at, next);
            LastMessage = "rotated";
            Changed?.Invoke();
            return true;
        }

        /// <summary>Focused item back into storage (same rules as picking up).</summary>
        public bool StoreFocused()
        {
            var item = Focused;
            if (item == null) return false;
            var ok = Store(item.Uid);
            if (ok) FocusedUid = null;
            return ok;
        }

        /// <summary>"Duplizieren": selects another copy of the focused item from storage for placement, if one is left.</summary>
        public bool DuplicateFocused()
        {
            var item = Focused;
            if (item == null) return false;
            if (StorageLogic.CountOf(HomeState.Storage(_gs), item.Def.Id) <= 0) { LastMessage = "storage_empty"; Changed?.Invoke(); return false; }
            Rotation = item.Rot;
            Select(item.Def.Id);
            return true;
        }

        public bool Place(Cell cell)
        {
            var def = FurnitureCatalog.Def(SelectedItem);
            if (def == null) { LastMessage = GridData.ReasonUnknownItem; Changed?.Invoke(); return false; }
            if (StorageLogic.CountOf(HomeState.Storage(_gs), def.Id) <= 0) { LastMessage = "storage_empty"; Changed?.Invoke(); return false; }
            var check = Grid.CanPlace(def, cell, Rotation);
            if (!check.Ok) { LastMessage = check.Reason; Changed?.Invoke(); return false; }
            var uid = HomeState.NextUid(_gs);
            Grid.Place(def, cell, Rotation, uid);
            var variant = HomeState.TakeVariant(_gs, def.Id);
            if (!FurnitureVariants.IsNeutral(variant)) Grid.GetItem(uid).Variant = variant;
            SpawnView(Grid.GetItem(uid));
            Effects.Sparkle(CellCenter(cell, def.FootprintW, def.FootprintH, Rotation) + Vector3.up * 0.35f, 0.45f);
            _undo.Push(uid);
            HomeState.SaveRoomGrid(_gs, RoomId, Grid); // transactional: storage and grid never drift apart on a crash
            Delta?.Invoke(VisitBuild.OpPlace, def.Id, cell, Rotation);
            LastMessage = "placed";
            if (def.Pflicht == "bett") HomeState.SetFlag(_gs, HomeState.FlagBedPlaced, true);
            if (StorageLogic.CountOf(HomeState.Storage(_gs), def.Id) <= 0) SelectedItem = null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>A placed piece back into the storage (floor, table top, wall or ceiling).</summary>
        private bool Store(string uid)
        {
            if (string.IsNullOrEmpty(uid)) { LastMessage = "empty"; Changed?.Invoke(); return false; }
            var item = Grid.GetItem(uid);
            if (item == null) { LastMessage = "empty"; Changed?.Invoke(); return false; }
            if (FurnitureCatalog.IsLastOfMandatorySlot(Grid.ToItemsArray(), uid)) { LastMessage = "mandatory"; Changed?.Invoke(); return false; }
            if (!HomeState.StoreItem(_gs, item.Def.Id, VariantOf(item))) { LastMessage = "storage_full"; Changed?.Invoke(); return false; }
            Grid.RemoveItem(uid);
            DestroyView(uid);
            if (FocusedUid == uid) FocusedUid = null;
            HomeState.SaveRoomGrid(_gs, RoomId, Grid);
            if (item.Wall == null) Delta?.Invoke(VisitBuild.OpRemove, item.Def.Id, item.At, item.Rot);
            LastMessage = "stored";
            Changed?.Invoke();
            return true;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var uid = _undo.Pop();
            if (uid.StartsWith(GarlandUndo, StringComparison.Ordinal))
            {
                // the last garland of that kind spanned in this room comes down again
                var liste = Girlanden.Liste(_gs, RoomId);
                var typ = uid.Substring(GarlandUndo.Length);
                for (var i = liste.Count - 1; i >= 0; i--)
                {
                    if (Gooby.Core.Json.J.Str(liste[i]["typ"]) != typ) continue;
                    if (!Girlanden.Abnehmen(_gs, RoomId, i)) return false;
                    GarlandsChanged?.Invoke();
                    LastMessage = "undone";
                    Changed?.Invoke();
                    return true;
                }
                // already taken down by hand: undo the step before it
                return Undo();
            }
            var item = Grid.GetItem(uid);
            if (item == null) return false;
            Grid.RemoveItem(uid);
            DestroyView(uid);
            HomeState.StoreItem(_gs, item.Def.Id, VariantOf(item));
            HomeState.SaveRoomGrid(_gs, RoomId, Grid);
            if (item.Wall == null) Delta?.Invoke(VisitBuild.OpRemove, item.Def.Id, item.At, item.Rot);
            LastMessage = "undone";
            Changed?.Invoke();
            return true;
        }

        private static string VariantOf(GridItem item) => FurnitureVariants.IsNeutral(item.Variant) ? StorageLogic.DefaultVariant : item.Variant;

        public void Commit()
        {
            HomeState.SaveRoomGrid(_gs, RoomId, Grid);
            _undo.Clear();
        }

        public void Dispose()
        {
            foreach (var v in _views.Values) if (v != null) UnityEngine.Object.Destroy(v);
            _views.Clear();
            _modelIn.Clear();
            if (_preview != null) UnityEngine.Object.Destroy(_preview);
            SetGarlandStart(null);
        }

        public void UpdatePreview(Cell? hover)
        {
            if (_preview != null) { UnityEngine.Object.Destroy(_preview); _preview = null; }
            var def = FurnitureCatalog.Def(SelectedItem);
            if (def == null || hover == null) return;
            var ok = Grid.CanPlace(def, hover.Value, Rotation).Ok;
            _preview = MakeView(def, hover.Value, Rotation, ok ? new Color(0.5f, 0.9f, 0.6f, 0.55f) : new Color(1f, 0.45f, 0.45f, 0.55f), transparent: true);
            _preview.name = "Preview";
        }

        private void SpawnView(GridItem item)
        {
            if (item == null) return;
            if (item.Wall != null)
            {
                // wall items stay where they hang (board 09 shows the gallery while building); the session edits floor items only
                var wallItem = WallProps.Make(_parent, item, Origin, Grid.Width * (float)GridData.CellSize, Grid.Height * (float)GridData.CellSize, _gs?.State);
                if (wallItem != null) { wallItem.name = "Item:" + item.Uid; _views[item.Uid] = wallItem; }
                return;
            }
            var textured = PropPlaceholder.MakeTextured(_parent, item.Def, item.At, item.Rot, Origin, Grid.Height);
            if (textured != null)
            {
                textured.name = "Item:" + item.Uid;
                _views[item.Uid] = textured;
                // stepping stones / door mat: the quad is the placeholder of their Blender model (PropPlaceholder.HasRugModel)
                if (PropPlaceholder.HasRugModel(item.Def.Id)) _ = SwapInPropAsync(item, textured);
                return;
            }
            var go = MakeView(item.Def, item.At, item.Rot, ColorFor(item.Def), transparent: false);
            go.name = "Item:" + item.Uid;
            _views[item.Uid] = go;
            VariantTint.Apply(go, item.Variant);
            _ = SwapInPropAsync(item, go);
        }

        /// <summary>Replaces the placeholder box with the Blender prop (props_manifest archetype) once it loads.</summary>
        private async System.Threading.Tasks.Task SwapInPropAsync(GridItem item, GameObject placeholder)
        {
            var archetype = AssetLoader.ArchetypeFor(item.Def.Id);
            var prop = await AssetLoader.PropInstanceAsync(archetype, _parent);
            if (prop == null) return;
            if (placeholder == null || !_views.TryGetValue(item.Uid, out var current) || current != placeholder)
            {
                UnityEngine.Object.Destroy(prop);
                return;
            }
            var (w, h) = GridData.RotatedFootprint(item.Def.FootprintW, item.Def.FootprintH, item.Rot);
            var sx = w * (float)GridData.CellSize;
            var sz = h * (float)GridData.CellSize;
            PropPlaceholder.FitProp(prop, item.Def, item.At, item.Rot, Origin, Grid.Height);
            VariantTint.Apply(prop, item.Variant);
            prop.name = "Item:" + item.Uid;
            _views[item.Uid] = prop;
            _modelIn.Add(item.Uid);
            UnityEngine.Object.Destroy(placeholder);
            Seat(item);
            foreach (var dep in new List<GridItem>(Grid.Items))
                if (PropPlaceholder.SupportOf(dep, Grid.Items) == item) Seat(dep);
        }

        /// <summary>Stands a surface piece's model on its support's model once both are in (RoomView does the same).</summary>
        private void Seat(GridItem item)
        {
            if (!_modelIn.Contains(item.Uid) || !_views.TryGetValue(item.Uid, out var view) || view == null) return;
            var support = PropPlaceholder.SupportOf(item, Grid.Items);
            if (support == null || !_modelIn.Contains(support.Uid) || !_views.TryGetValue(support.Uid, out var under) || under == null) return;
            PropPlaceholder.SeatOnSupport(view, support, under);
        }

        private void DestroyView(string uid)
        {
            if (_views.TryGetValue(uid, out var go) && go != null) UnityEngine.Object.Destroy(go);
            _views.Remove(uid);
            _modelIn.Remove(uid);
        }

        private GameObject MakeView(FurnitureDef def, Cell at, int rot, Color color, bool transparent)
        {
            if (!transparent) return PropPlaceholder.Make(_parent, def, at, rot, Origin, Grid.Height, _lit);
            var (w, h) = GridData.RotatedFootprint(def.FootprintW, def.FootprintH, rot);
            var size = new Vector3(w * (float)GridData.CellSize * 0.92f, HeightFor(def), h * (float)GridData.CellSize * 0.92f);
            var go = GameObject.CreatePrimitive(def.Kategorie == "pflanzen" || def.Kategorie == "garten" && def.FootprintW == 1 ? PrimitiveType.Sphere : PrimitiveType.Cube);
            go.transform.SetParent(_parent);
            var center = CellCenter(at, def.FootprintW, def.FootprintH, rot);
            go.transform.position = center + Vector3.up * size.y * 0.5f;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            var shader = _lit ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = 3000;
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
            }
            r.material = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            return go;
        }

        private static float HeightFor(FurnitureDef def)
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

        private static Color ColorFor(FurnitureDef def)
        {
            switch (def.Kategorie)
            {
                case "pflanzen": case "garten": return Hex("7FCF9B");
                case "sitzen": case "schlafen": return Hex("FF8B8B");
                case "tische": case "regale": return Hex("D9B48A");
                case "lampen": return Hex("FFD166");
                case "teppiche": return Hex("F6E9D7");
                case "kueche": return Hex("EBD9C6");
                case "bad": return Hex("B7D8FF");
                case "party": return Hex("FF98C1");
            }
            return Hex("C8B7A1");
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
