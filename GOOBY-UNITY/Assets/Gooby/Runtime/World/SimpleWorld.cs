using Gooby.Core.City;
using Gooby.Core.Cosmetics;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Time;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The 3D world behind the UI: the current room of the house (RoomView from the save), garden (GardenScene)
    /// and city street (CityScene), one camera, the rendering rig (SceneDressing) and Gooby. Screens only talk to
    /// this facade (cameras, build sessions, driving, mood, doors), never to the scene builders directly.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        public Camera Camera { get; private set; }
        public Transform Gooby { get; private set; }
        public SceneDressing.Rig Look { get; private set; }

        private GameObject _root;
        private GameObject _home;
        private GameObject _crate;
        private GardenScene _garden;
        private CityScene _city;
        private int _buildCam;
        private float _bounce;

        public static SimpleWorld Create()
        {
            var w = new SimpleWorld();
            w._root = new GameObject("World");
            Object.DontDestroyOnLoad(w._root);

            var camGo = new GameObject("MainCamera");
            camGo.transform.SetParent(w._root.transform);
            camGo.tag = "MainCamera";
            w.Camera = camGo.AddComponent<Camera>();
            w.Camera.clearFlags = CameraClearFlags.SolidColor;
            w.Camera.backgroundColor = ArtLibrary.Hex("BFE3FF");
            w.Camera.fieldOfView = 38f;
            w.Camera.nearClipPlane = 0.1f;
            w.Camera.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();
            w.Look = SceneDressing.Create(w._root.transform, w.Camera);
            return w;
        }

        // ── home ────────────────────────────────────────────────────────────

        /// <summary>The room currently on stage (null while the garden is the stage).</summary>
        public RoomView Room { get; private set; }

        /// <summary>Id of the place Gooby is in: a house room or "garden".</summary>
        public string RoomId { get; private set; } = RoomNavigation.DefaultRoom;

        public bool InGarden => RoomId == "garden";

        /// <summary>Raised after the stage switched to another room (HUD shows the room name).</summary>
        public event System.Action<string> RoomChanged;

        /// <summary>
        /// Raised when Gooby walks into a door zone or the player taps a door. The HUD wraps the switch in a fade
        /// and calls <see cref="PassDoor"/>; with no listener the world switches immediately.
        /// </summary>
        public event System.Action<RoomDefs.Door> DoorReached;

        private Gooby.Core.State.GameStateStore _gs;

        /// <summary>Rooms are staged one at a time and centred on the world origin, like the Godot room scenes.</summary>
        public static Vector3 OriginFor(RoomDefs.Room def) => def == null
            ? new Vector3(-3f, 0f, -2.5f)
            : new Vector3(-def.GridW * 0.25f, 0f, -def.GridH * 0.25f);

        /// <summary>Grid units (x east, y south, fractional) → world position on the floor of the given room.</summary>
        public static Vector3 WorldOf(RoomDefs.Room def, Vector3 origin, double gx, double gy) =>
            new Vector3(origin.x + (float)(gx * GridData.CellSize), 0f, origin.z + (def?.GridH ?? 10) * (float)GridData.CellSize - (float)(gy * GridData.CellSize));

        public void BuildHome(Gooby.Core.State.GameStateStore gs)
        {
            if (_gs != gs)
            {
                if (_gs != null) _gs.SliceChanged -= OnSliceChanged;
                _gs = gs;
                _gs.SliceChanged += OnSliceChanged;
            }
            if (_home != null) return;
            _home = new GameObject("Home");
            _home.transform.SetParent(_root.transform);
            _crate = BuildCrate(_home.transform);
            var roomId = RoomNavigation.CurrentRoom(gs);
            if (roomId == "garden") roomId = RoomNavigation.DefaultRoom; // the house is the stage after a boot
            StageRoom(roomId);
            // Gooby lives under the world root, not the house: he also stands on the lawn while the house is hidden
            var start = roomId == RoomNavigation.DefaultRoom ? CratePos + Vector3.up * 0.3f : Room.Center + Vector3.up * 0.6f;
            Gooby = BuildGooby(_root.transform, start);
            RefreshAway();
            SetHomeCamera();
            MaybeUnlockKartonSticker();
            _ = SwapInAvatarAsync();
        }

        /// <summary>
        /// Board 08 crate-nap unlocks the Kartonversteck sticker (event hook <c>karton_gooby</c>). Default
        /// move-in starts Gooby in the living-room crate, so the garden-build banner of board 05 can show
        /// after a fresh boot — including <c>-gooby-open garden</c>, which never sits him down again.
        /// </summary>
        private void MaybeUnlockKartonSticker()
        {
            if (_gs == null) return;
            if (J.IsTrue(J.Path(_gs.State, "stickers", "hooks", "karton_gooby"))) return;
            StickerRules.FireEventHook(_gs, "karton_gooby");
        }

        private void OnSliceChanged(string id, Newtonsoft.Json.Linq.JToken _)
        {
            if (id == "cosmetics") ApplyOutfit();
            if (id == "home" && Beds != null && _garden != null && _garden.Root.activeSelf && Build == null) RefreshBeds();
            if (id == "city" && _city != null) PaintPlayerCar();
            if (id == "vacation") RefreshAway();
        }

        /// <summary>Gooby is on a trip (vacation phase away/returnReady/overdue): the house, the lawn and the shops are without him.</summary>
        public bool GoobyAway { get; private set; }

        /// <summary>
        /// Port of gooby_spawn.gd: while the vacation slice says he is away, no Gooby stands in the rooms — the stand-in
        /// and the imported avatar alike stay hidden (the crate of board 08 stays where it is); picked up at the airport,
        /// he is back in the room at once.
        /// </summary>
        private void RefreshAway()
        {
            GoobyAway = _gs != null && Travel.IstWeg(_gs);
            // visiting him on holiday he stands in the holiday scene
            var show = !GoobyAway || _vacation != null;
            if (Gooby != null && Gooby.gameObject.activeSelf != show) Gooby.gameObject.SetActive(show);
        }

        /// <summary>The car in use (Autohaus Blechbert): its paint and body shape on the street's hero car.</summary>
        private void PaintPlayerCar()
        {
            if (_city == null || _gs == null) return;
            var (auto, farbe) = AutoKatalog.AktivesAuto(_gs);
            _city.DressCar(auto?.Id ?? "sedan", ArtLibrary.Hex(farbe));
        }

        /// <summary>Builds the RoomView for a room (replacing the staged one) without touching Gooby or the camera.</summary>
        private void StageRoom(string roomId)
        {
            var def = RoomDefs.Get(roomId);
            if (Room != null) Object.Destroy(Room.Root);
            if (_roomGrid != null) { Object.Destroy(_roomGrid); _roomGrid = null; }
            Room = new RoomView(HouseStore, roomId, _home.transform, OriginFor(def));
            RoomId = roomId;
            // every living room has the board-08 crate at CratePos: a guest sees the host's crate (and the host napping in it)
            _crate.SetActive(roomId == RoomNavigation.DefaultRoom);
            InvalidateWalkGrid();
            SceneDressing.PlaceWindowSun(Look, Room.WidthM * 0.5f, Room.DepthM);
        }

        /// <summary>
        /// Switches the stage to a room or the garden. Coming through a door puts Gooby just inside the matching
        /// door of the new room; otherwise he sits in the crate (living room) or stands in the middle.
        /// </summary>
        public bool EnterRoom(string roomId, RoomDefs.Door arriveThrough = null)
        {
            var def = RoomDefs.Get(roomId);
            if (def == null || _gs == null || _home == null || Build != null) return false;
            if (Visiting && roomId == "garden") return false; // the host's lawn is not part of a house visit
            EndRoutine();
            _move = Vector2.zero;
            if (roomId == "garden")
            {
                EnsureGarden();
                _garden.Grid.SetActive(false);
                _home.SetActive(false);
                RoomId = "garden";
                RoomNavigation.SetCurrentRoom(HouseStore, roomId);
                InvalidateWalkGrid();
                if (arriveThrough != null)
                {
                    var (gx, gy) = RoomNavigation.FreeArrival(def, arriveThrough, HomeState.LoadRoomGrid(HouseStore, roomId));
                    PlaceGoobyAt(WorldOf(def, GardenOrigin, gx, gy), "idle-loop");
                }
                else PlaceGoobyAt(GardenGoobyPos, "idle-loop");
                _buildCam = 0;
                ApplyBuildCamera();
                RoomChanged?.Invoke(roomId);
                return true;
            }
            if (InGarden) { _garden.Root.SetActive(false); _home.SetActive(true); }
            if (Room == null || Room.RoomId != roomId) StageRoom(roomId);
            RoomId = roomId;
            RoomNavigation.SetCurrentRoom(HouseStore, roomId);
            if (arriveThrough != null)
            {
                var (gx, gy) = RoomNavigation.FreeArrival(def, arriveThrough, HomeState.LoadRoomGrid(HouseStore, roomId));
                PlaceGoobyAt(WorldOf(def, Room.Origin, gx, gy), "idle-loop");
            }
            else if (Visiting) PlaceGoobyAt(Room.Center + VisitArrivalOffset, "idle-loop");
            else if (roomId == RoomNavigation.DefaultRoom) PlaceGoobyAt(CratePos, "sit-loop");
            else PlaceGoobyAt(Room.Center, "idle-loop");
            SetHomeCamera();
            RoomChanged?.Invoke(roomId);
            return true;
        }

        /// <summary>Walks through a door: the target room is staged and Gooby arrives at its counterpart.</summary>
        public bool PassDoor(RoomDefs.Door door)
        {
            if (door == null || !RoomNavigation.CanPass(HouseStore, door)) return false;
            return EnterRoom(door.To, RoomNavigation.Counterpart(door));
        }

        private float _doorCooldown;

        /// <summary>QA: Gooby uses the first door of the staged room he may pass (as if it had been tapped).</summary>
        public bool QaUseDoor()
        {
            if (Room == null) return false;
            foreach (var marker in Room.Root.GetComponentsInChildren<DoorMarker>())
            {
                if (marker.Door == null || !RoomNavigation.CanPass(HouseStore, marker.Door)) continue;
                OnDoor(marker.Door);
                return true;
            }
            return false;
        }

        private void OnDoor(RoomDefs.Door door)
        {
            _move = Vector2.zero;
            _doorCooldown = 1.2f;
            if (_walking) { _walking = false; Avatar?.Play("idle-loop"); }
            if (DoorReached != null) DoorReached(door);
            else PassDoor(door);
        }

        /// <summary>
        /// A tap on the world (not on UI): a door of the staged room (or the cottage door in the garden) walks Gooby
        /// through; a fallen leaf on the lawn is picked; a bed on the lawn opens its sheet.
        /// </summary>
        public bool TapWorld(Vector2 screenPos)
        {
            if (Camera == null || Build != null || _doorCooldown > 0f) return false;
            var ray = Camera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 80f)) return false;
            if (_interior == null && TapGooby(hit)) return true;
            if (InGarden)
            {
                var leaf = hit.collider.GetComponentInParent<LeafMarker>();
                if (leaf != null) return PickLeaf(leaf.Id);
                var spot = hit.collider.GetComponentInParent<SpotMarker>();
                if (spot != null) return PickSpot(spot.Cell);
                var bed = hit.collider.GetComponentInParent<BedMarker>();
                if (bed != null) { BedTapped?.Invoke(bed.Bed); return true; }
            }
            else if (!Visiting && _interior == null && TapFurniture(hit)) return true;
            var marker = hit.collider.GetComponentInParent<DoorMarker>();
            if (marker == null || marker.Door == null || !RoomNavigation.CanPass(HouseStore, marker.Door)) return false;
            OnDoor(marker.Door);
            return true;
        }

        /// <summary>Gooby's gift crate (board 08): open wooden slat box with a folded blanket inside.</summary>
        private static GameObject BuildCrate(Transform parent)
        {
            var crate = new GameObject("Crate");
            crate.transform.SetParent(parent, false);
            var t = crate.transform;
            var wood = ArtLibrary.Pbr("wood_planks", 0.4f, ArtLibrary.Hex("#E2B889"), 0.18f, 0.55f);
            var woodDark = ArtLibrary.Pbr("wood_planks", 0.4f, ArtLibrary.Hex("#C99A6B"), 0.18f, 0.55f);
            var p = CratePos + new Vector3(0f, -0.02f, 0f);
            ArtLibrary.Box(t, "Bottom", p + new Vector3(0f, -0.27f, 0f), new Vector3(0.92f, 0.06f, 0.92f), woodDark);
            for (var i = 0; i < 3; i++)
            {
                var y = -0.16f + i * 0.18f;
                ArtLibrary.Box(t, "SlatB", p + new Vector3(0f, y, 0.44f), new Vector3(0.92f, 0.14f, 0.045f), wood, false);
                ArtLibrary.Box(t, "SlatF", p + new Vector3(0f, y - 0.04f, -0.44f), new Vector3(0.92f, 0.14f, 0.045f), wood, false);
                ArtLibrary.Box(t, "SlatL", p + new Vector3(-0.44f, y, 0f), new Vector3(0.045f, 0.14f, 0.92f), wood, false);
                ArtLibrary.Box(t, "SlatR", p + new Vector3(0.44f, y, 0f), new Vector3(0.045f, 0.14f, 0.92f), wood, false);
            }
            ArtLibrary.Box(t, "PostFL", p + new Vector3(-0.44f, 0.02f, -0.44f), new Vector3(0.07f, 0.58f, 0.07f), woodDark, false);
            ArtLibrary.Box(t, "PostFR", p + new Vector3(0.44f, 0.02f, -0.44f), new Vector3(0.07f, 0.58f, 0.07f), woodDark, false);
            ArtLibrary.Box(t, "PostBL", p + new Vector3(-0.44f, 0.02f, 0.44f), new Vector3(0.07f, 0.58f, 0.07f), woodDark, false);
            ArtLibrary.Box(t, "PostBR", p + new Vector3(0.44f, 0.02f, 0.44f), new Vector3(0.07f, 0.58f, 0.07f), woodDark, false);
            ArtLibrary.Box(t, "Blanket", p + new Vector3(0f, -0.18f, 0f), new Vector3(0.78f, 0.14f, 0.78f),
                ArtLibrary.Pbr("fabric", 0.35f, ArtLibrary.Hex("#FFC9D6"), 0.08f, 0.4f), false);
            return crate;
        }

        /// <summary>Replaces the procedural stand-in with the Blender-built rigged Gooby when the GLB loads.</summary>
        public GoobyAvatar Avatar { get; private set; }

        private async System.Threading.Tasks.Task SwapInAvatarAsync()
        {
            if (!AssetLoader.Available) return;
            var avatar = await GoobyAvatar.CreateAsync(_root.transform, CratePos);
            if (avatar == null || Gooby == null) return;
            // the GLB imports for seconds while the game goes on (a QA boot may already stand in a shop): the avatar
            // takes over wherever the stand-in is NOW — board 08's crate only when he is still sitting in it at home
            var at = new Vector3(Gooby.position.x, 0f, Gooby.position.z);
            var inCrate = _interior == null && RoomId == RoomNavigation.DefaultRoom && Vector3.Distance(at, new Vector3(CratePos.x, 0f, CratePos.z)) < 0.3f;
            avatar.transform.position = inCrate ? CratePos : at;
            avatar.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // glTFast negates X only: the rig still faces +Z, camera sits at -Z
            avatar.transform.localScale = Vector3.one * 1.0f;
            if (inCrate) { avatar.Play("sit-loop"); avatar.SetEmotion("sleepy"); }
            else avatar.Play("idle-loop");
            Object.Destroy(Gooby.gameObject);
            Gooby = avatar.transform;
            Avatar = avatar;
            ApplyOutfit();
            RefreshAway();   // imported while he was booked onto a flight: the avatar stays away too
        }

        /// <summary>Dresses the avatar from the cosmetics slice (equipped hat/glasses/neck/back + fur).</summary>
        public void ApplyOutfit()
        {
            if (Avatar == null || _gs == null) return;
            Avatar.Wear(CosmeticsState.EquippedMap(J.Dict(_gs.State["cosmetics"])));
            Avatar.ApplySavedMorphs(J.Path(_gs.State, "meta", "charMorphs"));
        }

        /// <summary>
        /// Board 08: Gooby is in the living-room gift crate (XZ near <see cref="CratePos"/>) and not walking.
        /// The HUD shows Zzz and the sleepy sit pose without flipping Sleep.IsSleeping (night still uses that).
        /// </summary>
        public bool InCrate => !Visiting && _interior == null && !InGarden && RoomId == RoomNavigation.DefaultRoom && Gooby != null
            && Vector3.Distance(new Vector3(Gooby.position.x, 0f, Gooby.position.z), new Vector3(CratePos.x, 0f, CratePos.z)) < 0.45f;

        public bool CrateNapping => InCrate && !_walking && !CrateAwake;

        /// <summary>Mood/sleep/sick from the store → emotion morph + clip (no-op on the stand-in).</summary>
        public void SetMood(double mood, bool sleeping, bool sick)
        {
            if (Avatar == null) return;
            if (sleeping) { Avatar.SetMood(mood, true, sick); return; }
            if (CrateNapping && !sick)
            {
                Avatar.SetEmotion("sleepy");
                Avatar.Play("sit-loop");
                return;
            }
            Avatar.SetMood(mood, false, sick);
        }

        public void PlayClip(string clip) => Avatar?.Play(clip);

        private static Transform BuildGooby(Transform parent, Vector3 pos)
        {
            var g = new GameObject("Gooby");
            g.transform.SetParent(parent);
            g.transform.position = pos;
            var creme = ArtLibrary.Solid(ArtLibrary.Hex("FFF8EE"), 0.1f);
            var pink = ArtLibrary.Solid(ArtLibrary.Hex("FFC9A6"), 0.1f);
            var body = ArtLibrary.Sphere(g.transform, "Body", pos, 0.55f, creme);
            body.transform.localScale = new Vector3(0.9f, 0.75f, 0.9f);
            ArtLibrary.Sphere(g.transform, "Head", pos + new Vector3(0, 0.45f, 0.1f), 0.42f, creme);
            foreach (var s in new[] { -1f, 1f })
            {
                var ear = ArtLibrary.Sphere(g.transform, "Ear", pos + new Vector3(s * 0.22f, 0.95f, 0.05f), 0.16f, creme);
                ear.transform.localScale = new Vector3(0.16f, 0.5f, 0.16f);
                var inner = ArtLibrary.Sphere(g.transform, "EarIn", pos + new Vector3(s * 0.22f, 0.95f, 0.13f), 0.09f, pink);
                inner.transform.localScale = new Vector3(0.07f, 0.22f, 0.07f);
                ArtLibrary.Sphere(g.transform, "Eye", pos + new Vector3(s * 0.14f, 0.5f, 0.42f), 0.06f, ArtLibrary.Solid(ArtLibrary.Hex("3B3B3B"), 0.6f));
                ArtLibrary.Sphere(g.transform, "Cheek", pos + new Vector3(s * 0.26f, 0.4f, 0.36f), 0.07f, ArtLibrary.Solid(ArtLibrary.Hex("FF98C1"), 0.1f));
                ArtLibrary.Sphere(g.transform, "Paw", pos + new Vector3(s * 0.3f, -0.3f, 0.3f), 0.16f, creme);
            }
            ArtLibrary.Sphere(g.transform, "Nose", pos + new Vector3(0f, 0.42f, 0.48f), 0.05f, ArtLibrary.Solid(ArtLibrary.Hex("FF8B8B"), 0.2f));
            g.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the camera, which looks along +Z
            return g.transform;
        }

        // cell (5,5) of the living room: the centre of the round rug, between coffee table, sofa and armchair (board 08)
        private static readonly Vector3 CratePos = new Vector3(-0.25f, 0.42f, -0.25f);

        /// <summary>Gooby's spot on the lawn (board 05): centre of the garden grid, a little towards the camera.</summary>
        public static Vector3 GardenGoobyPos => GardenOrigin + new Vector3(GardenCellsX * 0.25f, 0f, GardenCellsY * 0.25f - 0.5f);

        /// <summary>Puts Gooby on a floor spot facing the camera; the stand-in keeps its own height offset.</summary>
        private void PlaceGoobyAt(Vector3 floorPos, string clip)
        {
            if (Gooby == null) return;
            if (_walking) { _walking = false; }
            _walkSpeed = 0f; _goobyYaw = 180f; _leanRoll = _leanPitch = 0f; _settleLeft = 0f; _walkDir = Vector3.back;
            if (Avatar != null)
            {
                Gooby.position = floorPos;
                Gooby.rotation = Quaternion.Euler(0f, 180f, 0f);
                Avatar.Play(clip);
                if (clip == "sit-loop") Avatar.SetEmotion("sleepy");
                else Avatar.SetEmotion("happy");
            }
            else
            {
                Gooby.position = floorPos + Vector3.up * (clip == "sit-loop" ? 0.3f : 0.6f);
                Gooby.rotation = Quaternion.Euler(0f, 180f, 0f);
            }
        }

        /// <summary>Gooby sits in the living-room crate (board 08) or stands on the lawn in garden build mode (board 05).</summary>
        private void PlaceGooby(bool garden)
        {
            if (garden) PlaceGoobyAt(GardenGoobyPos, "idle-loop");
            else if (RoomId == RoomNavigation.DefaultRoom)
            {
                PlaceGoobyAt(CratePos, "sit-loop");
                MaybeUnlockKartonSticker();
            }
            else if (Room != null) PlaceGoobyAt(Room.Center, "idle-loop");
        }

        /// <summary>
        /// Board 08: three-quarter view from the south, low enough to see the back wall's pictures and door. The
        /// distance scales with the room so the kitchen and bathroom fill the frame like the living room does.
        /// </summary>
        private void SetHomeCamera()
        {
            var c = Room != null ? Room.Center : Vector3.zero;
            var k = HomeCamScale;
            Camera.transform.position = c + new Vector3(-0.3f * k, 4.5f * k, -5.5f * k);
            Camera.transform.rotation = Quaternion.Euler(37f, 0f, 0f);
            Camera.fieldOfView = 38f;
            Camera.backgroundColor = ArtLibrary.Hex("F3E6D6");
            SceneDressing.SetDioramaFocus(Look, 7.1f * k);   // board 01: the rug sharp, corner plants and back wall soft
            SceneDressing.SetIndoor(Look, true);
        }

        private float HomeCamScale => Room != null ? Mathf.Clamp(Mathf.Max(Room.WidthM / 6f, Room.DepthM / 5f), 0.7f, 1.3f) : 1f;

        /// <summary>Wardrobe close-up: Gooby fills the left third of the frame so the outfit sheet can sit on the right.</summary>
        public void SetWardrobeCamera(bool on)
        {
            if (!on) { if (InGarden) { _buildCam = 0; ApplyBuildCamera(); } else SetHomeCamera(); return; }
            var g = Gooby != null ? Gooby.position : Vector3.zero;
            Camera.transform.position = g + new Vector3(-0.5f, 1.7f, -4.3f);
            Camera.transform.LookAt(g + new Vector3(1.15f, 0.8f, 0f));
            Camera.fieldOfView = 34f;
            SceneDressing.SetFocus(Look, 3.5f, 8f);
        }

        public void Bounce()
        {
            _bounce = 1f;
            Avatar?.Play("hop");
            _landingIn = 0.32f;   // the hop clip touches down about a third of a second in
        }

        private float _landingIn = -1f, _stepDist;

        /// <summary>Dust colour of the floor under Gooby: pale wood dust indoors, bits of grass on the lawn.</summary>
        private Color FloorDust => InGarden ? new Color(0.74f, 0.88f, 0.55f, 0.55f) : new Color(0.95f, 0.9f, 0.8f, 0.5f);

        /// <summary>Hop landing: a ring of dust rolls out from under Gooby once the clip touches down.</summary>
        private void TickLanding(float dt)
        {
            if (_landingIn < 0f) return;
            _landingIn -= dt;
            if (_landingIn >= 0f || Gooby == null || !Gooby.gameObject.activeInHierarchy) return;
            if (_interior == null && (_city == null || !_city.Root.activeSelf))
                Effects.LandingRing(new Vector3(Gooby.position.x, InCrate ? CratePos.y : 0f, Gooby.position.z), FloorDust);
        }

        private Vector2 _move;
        private bool _walking;
        private const float WalkSpeedMps = 1.6f;
        private float _walkSpeed, _goobyYaw = 180f, _leanRoll, _leanPitch, _settleLeft;
        private Vector3 _walkDir = Vector3.back;

        /// <summary>HUD joystick (x right, y down in UI space) → Gooby walks the staged room's grid (walkable cells only).</summary>
        public void SetMove(Vector2 uiDir)
        {
            var stick = uiDir.sqrMagnitude > 0.01f;
            // in the shower, on the loo or at the sink Gooby is busy; from a seat or the TV the stick gets him up
            if (stick && (Routine == global::Gooby.Core.Home.Interactables.Dusche || Routine == global::Gooby.Core.Home.Interactables.Klo || Routine == global::Gooby.Core.Home.Interactables.Zahnputz)) return;
            if (stick)
            {
                CancelAutoWalk();
                if (Routine != RoutineNone) StandUp();
            }
            if (_walkTarget == null || stick) _move = uiDir;
        }

        private RoomDefs.Room WalkRoomDef => RoomDefs.Get(RoomId);
        private Vector3 WalkOrigin => InGarden ? GardenOrigin : Room?.Origin ?? Vector3.zero;

        /// <summary>
        /// Gooby leaves the crate and walks; releasing the stick near the crate puts him back to the board-08 nap.
        /// The speed eases in and out (9 m/s² up, 12 down), the heading swings round at k = 12/s instead of snapping,
        /// and the body leans into the turn and a touch forward with speed, settling upright again once he stands.
        /// </summary>
        private void Walk(float dt)
        {
            if (_doorCooldown > 0f) _doorCooldown -= dt;
            var def = WalkRoomDef;
            if (Gooby == null || def == null || (Room == null && !InGarden) || Build != null || _park != null || ShakeHold)
            {
                if (_walking) { _walking = false; _walkSpeed = 0f; StopWalkingPose(); }
                return;
            }
            // screen right = +X, screen up (UI -y) = +Z (camera looks along +Z)
            var stick = _move.sqrMagnitude >= 0.01f;
            var dir = stick ? new Vector3(_move.x, 0f, -_move.y) : Vector3.zero;
            var targetSpeed = WalkSpeedMps * CareSpeed * Mathf.Clamp01(dir.magnitude);
            _walkSpeed = Mathf.MoveTowards(_walkSpeed, targetSpeed, (targetSpeed > _walkSpeed ? 9f : 12f) * dt);
            if (stick) _walkDir = dir.normalized;
            if (_walkSpeed <= 0.02f)
            {
                if (_walking) { _walking = false; _walkSpeed = 0f; _settleLeft = 0.6f; StopWalkingPose(); }
                SettleLean(dt);
                return;
            }
            var speed = _walkSpeed;
            var next = Gooby.position + _walkDir * speed * dt;
            var grid = _walkGrid ?? (_walkGrid = HomeState.LoadRoomGrid(HouseStore, RoomId));
            var origin = WalkOrigin;
            Cell CellOf(Vector3 p) => new Cell(Mathf.FloorToInt((p.x - origin.x) / 0.5f), Mathf.FloorToInt((origin.z + grid.Height * 0.5f - p.z) / 0.5f));
            bool Free(Vector3 p) { var c = CellOf(p); return grid.InBounds(c) && grid.Walkable(c); }
            var here = Gooby.position;
            if (!Free(next))
            {
                // slide along the free axis; a Gooby standing on a blocked cell (crate on furniture) may always leave
                var slideX = new Vector3(next.x, here.y, here.z);
                var slideZ = new Vector3(here.x, here.y, next.z);
                if (Free(slideX)) next = slideX;
                else if (Free(slideZ)) next = slideZ;
                else if (Free(here) || !grid.InBounds(CellOf(next)))
                {
                    if (_walking) { _walking = false; _walkSpeed = 0f; _settleLeft = 0.6f; StopWalkingPose(); }
                    return;
                }
            }
            Gooby.position = new Vector3(next.x, 0f, next.z);
            // a small puff of floor dust every other step, kicked back from the heel
            _stepDist += speed * dt;
            if (_stepDist >= 0.42f)
            {
                _stepDist = 0f;
                Effects.Puff(Gooby.position - _walkDir * 0.12f + Vector3.up * 0.04f, FloorDust, 3, 0.18f, 0.45f, 0.55f, -_walkDir * 0.6f + Vector3.up, 0.7f);
                global::Gooby.Unity.App.Sfx.Play("step_tap", UnityEngine.Random.Range(-1.2f, 1.2f), 0.8f);
            }
            var prevYaw = _goobyYaw;
            _goobyYaw = Mathf.LerpAngle(prevYaw, Mathf.Atan2(_walkDir.x, _walkDir.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-12f * dt));
            var yawRate = dt > 0f ? Mathf.DeltaAngle(prevYaw, _goobyYaw) / dt : 0f;
            var k = 1f - Mathf.Exp(-10f * dt);
            _leanRoll = Mathf.Lerp(_leanRoll, Mathf.Clamp(-yawRate * 0.012f, -9f, 9f), k);
            _leanPitch = Mathf.Lerp(_leanPitch, speed / WalkSpeedMps * 4f, k);
            ApplyWalkPose();
            if (!_walking) { _walking = true; Avatar?.Play("walk-loop"); }
            Avatar?.SetClipSpeed("walk-loop", Mathf.Lerp(0.6f, 1.1f, speed / WalkSpeedMps));
            // stepping into a door zone takes Gooby through the door (not on a walk to a ball or a piece of furniture)
            if (_doorCooldown <= 0f && _walkTarget == null)
            {
                var door = RoomNavigation.DoorAt(def, CellOf(Gooby.position));
                if (door != null && RoomNavigation.CanPass(HouseStore, door)) OnDoor(door);
            }
        }

        private GridData _walkGrid;

        /// <summary>
        /// Ends a walk on the spot without the stop pose: an arrival that seats or lays Gooby down must not be followed by the
        /// glide-out of the walk and its idle clip (it replaced the sit or sleep pose a frame later).
        /// </summary>
        private void HaltWalk()
        {
            _walking = false;
            _walkSpeed = 0f;
            _settleLeft = 0f;
            _move = Vector2.zero;
        }

        private void StopWalkingPose()
        {
            if (InCrate)
            {
                PlaceGoobyAt(CratePos, "sit-loop");
                return;
            }
            Avatar?.Play("idle-loop");
        }

        /// <summary>Heading plus the lean: yaw first, then the tilt in Gooby's own frame (the stand-in faces the other way).</summary>
        private void ApplyWalkPose()
        {
            Gooby.rotation = Quaternion.Euler(0f, _goobyYaw, 0f) * Quaternion.Euler(_leanPitch, 0f, _leanRoll)
                * (Avatar != null ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f));
        }

        /// <summary>Just stopped: the lean eases back to upright over the next half second, then the pose is left alone.</summary>
        private void SettleLean(float dt)
        {
            if (_settleLeft <= 0f || Gooby == null) return;
            _settleLeft -= dt;
            var k = 1f - Mathf.Exp(-10f * dt);
            _leanRoll = Mathf.Lerp(_leanRoll, 0f, k);
            _leanPitch = Mathf.Lerp(_leanPitch, 0f, k);
            if (_settleLeft <= 0f) { _leanRoll = 0f; _leanPitch = 0f; }
            if (!InCrate) ApplyWalkPose();
        }

        /// <summary>Call after build mode / room changes so walking uses the current furniture.</summary>
        public void InvalidateWalkGrid() => _walkGrid = null;

        public void Tick(float dt)
        {
            TickLife(dt);
            TickInteract(dt);
            TickCare(dt);
            Walk(dt);
            TickPeer(dt);
            TickLanding(dt);
            TickTimeOfDay(dt);
            TickWeather(dt);
            // the street keeps living while a sheet is open over it (the drive step itself pauses with the car)
            if (_city != null && _city.Root.activeSelf && _interior == null)
            {
                var v = Drive != null ? (float)(Drive.Kmh / 3.6) : 0f;
                _city.TickLife(dt, LocalHour, Drive != null ? new Vector3((float)Drive.ForwardX * v, 0f, (float)Drive.ForwardZ * v) : Vector3.zero);
            }
            if (InGarden && Build == null) TickGarden();
            if (_garden != null && _garden.Root.activeSelf && _interior == null) _garden.TickLife(dt, LocalHour);
            TickLeaves(dt);
            TickSpots(dt);
            TickBall(dt);
            TickFeeding(dt);
            TickToys(dt);
            TickGarage(dt);
            TickMoment(dt);
            _interior?.Tick(dt, ReducedMotion);
            TickGlide(dt);
            TickPark(dt);
            TickVacation(dt);
            TickStationHop(dt);
            TickGooberando();
            TickShake(dt);
            if (Gooby == null || Avatar != null) return;
            if (_bounce > 0f)
            {
                _bounce = Mathf.Max(0f, _bounce - dt * 2.5f);
                var s = 1f + Mathf.Sin(_bounce * Mathf.PI) * 0.12f;
                Gooby.localScale = new Vector3(s, 2f - s, s);
            }
            else
            {
                var breathe = 1f + Mathf.Sin(Time.time * 2.2f) * 0.02f;
                Gooby.localScale = new Vector3(1f, breathe, 1f);
            }
        }

        // ── build mode / garden ─────────────────────────────────────────────

        /// <summary>Garden build mode on/off. Leaving it returns to wherever Gooby was: the lawn or the staged room.</summary>
        public void SetBuildCamera(bool on)
        {
            if (!on)
            {
                if (InGarden)
                {
                    _garden?.Grid.SetActive(false);
                    PlaceGooby(garden: true);
                    _buildCam = 0;
                    ApplyBuildCamera();
                    return;
                }
                _home?.SetActive(true);
                SetHomeCamera();
                _garden?.Root.SetActive(false);
                return;
            }
            EnsureGarden();
            _garden.Grid.SetActive(true);
            _home?.SetActive(false); // the house stands west of the lawn and would poke into the garden frame
            PlaceGooby(garden: true);
            _buildCam = 0;
            ApplyBuildCamera();
        }

        public void CycleBuildCamera()
        {
            _buildCam = (_buildCam + 1) % 3;
            ApplyBuildCamera();
        }

        /// <summary>
        /// Board 05: steep three-quarter view from the south. The north fence sits a third down the frame with the
        /// cottage wall above it (roof cut by the top edge), the near rows run under the storage bar; the second
        /// camera is the top-down plan for reaching every cell, the third a low look from the west.
        /// </summary>
        private void ApplyBuildCamera()
        {
            SceneDressing.SetIndoor(Look, false);
            Camera.backgroundColor = ApplyTimeOfDay(ArtLibrary.Hex("7FB8FF"));
            var c = GardenOrigin + new Vector3(GardenCellsX * 0.25f, 0f, GardenCellsY * 0.25f);
            switch (_buildCam)
            {
                case 0: Camera.transform.position = c + new Vector3(0f, 13.9f, -10.7f); Camera.transform.rotation = Quaternion.Euler(50f, 0f, 0f); Camera.fieldOfView = 34f; break;
                // plan view: the lawn fills the band above the storage bar (screen centre lies 1.9 m south of the lawn centre)
                case 1: Camera.transform.position = c + new Vector3(0f, 21.2f, -1.9f); Camera.transform.rotation = Quaternion.Euler(89f, 0f, 0f); Camera.fieldOfView = 36f; break;
                default: Camera.transform.position = c + new Vector3(-10.5f, 7.5f, 0f); Camera.transform.rotation = Quaternion.Euler(38f, 90f, 0f); Camera.fieldOfView = 40f; break;
            }
            // boards 05/09: tilt-shift — the lawn sharp, the cottage roof above and the near fence below soften
            SceneDressing.SetDioramaFocus(Look, Vector3.Distance(Camera.transform.position, c), 70f, 4f);
        }

        /// <summary>Garden room grid from rooms.json (28×14 cells × 0.5 m = 14 m × 7 m lawn) centred at x=12, z=0; the north-west corner is the grid origin.</summary>
        public static int GardenCellsX => RoomDefs.Get("garden")?.GridW ?? 28;
        public static int GardenCellsY => RoomDefs.Get("garden")?.GridH ?? 14;
        public static Vector3 GardenOrigin => new Vector3(12f - GardenCellsX * 0.25f, 0f, -GardenCellsY * 0.25f);

        /// <summary>The garden's saved items (trees, bench, flowers) as props on the lawn outside build mode.</summary>
        private RoomView _gardenItems;

        /// <summary>The vegetable beds (GardenState) in the lawn's south-east corner.</summary>
        public GardenBeds Beds { get; private set; }

        /// <summary>Raised when the player taps a bed tile on the lawn (HUD opens the bed sheet).</summary>
        public event System.Action<Cell> BedTapped;

        /// <summary>The same as a finger on that bed tile (QA tours).</summary>
        public void TapBed(Cell bed) => BedTapped?.Invoke(bed);

        private float _gardenTickTimer;

        private void EnsureGarden()
        {
            if (_garden != null) { _garden.Root.SetActive(true); RefreshLeaves(); GrowSpots(); _houseStylePending = true; return; }
            _garden = new GardenScene(_root.transform, GardenOrigin, GardenCellsX, GardenCellsY);
            _houseStylePending = true;
            _gardenItems = new RoomView(_gs, "garden", _garden.Root.transform, GardenOrigin, ArtLibrary.Lit, buildShell: false);
            Beds = new GardenBeds(_garden.Root.transform, GardenOrigin, GardenCellsY);
            MarkCottageDoor();
            TickGarden(force: true);
            RefreshLeaves();
        }

        /// <summary>The cottage door is tappable like the room doors: an invisible box with a DoorMarker over the door leaf and step.</summary>
        private void MarkCottageDoor()
        {
            var door = RoomDefs.Get("garden")?.Doors.Find(d => d.To == RoomNavigation.DefaultRoom);
            if (door == null) return;
            var tap = new GameObject("Door:" + door.Id);
            tap.transform.SetParent(_garden.Root.transform, false);
            tap.transform.position = _garden.DoorStep;
            tap.AddComponent<DoorMarker>().Door = door;
            var hit = tap.AddComponent<BoxCollider>();
            hit.center = new Vector3(0f, 1.1f, 0.35f);
            hit.size = new Vector3(1.4f, 2.3f, 1.3f);
        }

        private double NowS => _gs != null ? _gs.Clock.NowMs() / 1000.0 : 0.0;

        /// <summary>Lets the crops grow by the elapsed time and redraws the beds (on entering, every minute, after actions).</summary>
        public void TickGarden(bool force = false)
        {
            if (Beds == null || _gs == null) return;
            _gardenTickTimer -= UnityEngine.Time.unscaledDeltaTime;
            if (!force && _gardenTickTimer > 0f) return;
            _gardenTickTimer = 60f;
            GardenState.Tick(_gs, NowS);
            if (Beds.Refresh(_gs, NowS)) _gardenItems?.Refresh(_gs);
            GrowSpots();
        }

        /// <summary>
        /// Redraw after planting / watering / harvesting / building (the store's home slice changed). An expanded
        /// field may have pushed lawn furniture into storage, so the garden's items re-sync too.
        /// </summary>
        public void RefreshBeds()
        {
            if (Beds == null || _gs == null) return;
            if (Beds.Refresh(_gs, NowS)) _gardenItems?.Refresh(_gs);
            InvalidateWalkGrid();
            RefreshLeaves();
            RefreshSpots();
        }

        public Transform GardenRoot => _garden?.Root.transform;

        public void ToggleGrid()
        {
            if (_garden != null) _garden.Grid.SetActive(!_garden.Grid.activeSelf);
        }

        public Shader LitShader => ArtLibrary.Lit;

        /// <summary>Starts (or returns) the build session for a room, rendering the saved grid. House rooms are staged first.</summary>
        public BuildSession BeginBuild(Gooby.Core.State.GameStateStore gs, string roomId = "garden")
        {
            if (Build != null && Build.RoomId == roomId) return Build;
            if (Build != null) EndBuild(commit: true);
            if (roomId == "garden")
            {
                EnsureGarden();
                _gardenItems?.SetItemsVisible(false); // the session renders the lawn's items itself
                Build = new BuildSession(gs, "garden", GardenRoot, GardenOrigin, ArtLibrary.Lit);
                _leafView?.Root.SetActive(false);
                _spotView?.Root.SetActive(false);
            }
            else
            {
                if (_home == null) BuildHome(gs);
                if (Room == null || Room.RoomId != roomId) EnterRoom(roomId);
                Room.SetItemsVisible(false); // the session renders the room's items itself; walls and floor stay
                Build = new BuildSession(gs, roomId, _home.transform, Room.Origin, ArtLibrary.Lit);
            }
            var session = Build;
            session.Delta += (op, item, cell, rot) => HostBuildDelta?.Invoke(session.RoomId, op, item, cell, rot);
            if (roomId != "garden") session.Changed += () => { if (Room != null && Room.RoomId == session.RoomId) Room.UpdateWindows(session.Grid); };
            session.GarlandsChanged += () =>
            {
                if (Room != null && Room.RoomId == session.RoomId) Room.RefreshGarlands();
                GarlandSpanned = true;
            };
            return Build;
        }

        /// <summary>A grid change of the running build session (room, op, item, cell, rot) — relayed to a visiting friend.</summary>
        public event System.Action<string, string, string, Cell, int> HostBuildDelta;

        public BuildSession Build { get; private set; }

        /// <summary>A garland went up while building: back home Gooby has something to say about it (once).</summary>
        public bool GarlandSpanned { get; set; }

        public void EndBuild(bool commit)
        {
            if (Build == null) return;
            var roomId = Build.RoomId;
            if (commit) Build.Commit();
            else while (Build.Undo()) { }
            Build.Dispose();
            Build = null;
            if (roomId != "garden" && Room != null)
            {
                Room.Refresh(HouseStore);
                Room.SetItemsVisible(true);
            }
            if (roomId == "garden" && _gardenItems != null)
            {
                _gardenItems.Refresh(_gs);
                _gardenItems.SetItemsVisible(true);
                RefreshLeaves();
                RefreshSpots();
            }
            InvalidateWalkGrid();
        }

        private int _roomCam;
        private GameObject _roomGrid;

        /// <summary>Camera for editing a house room: high, from the south, the whole grid in view (board 09).</summary>
        public void SetRoomBuildCamera(bool on)
        {
            if (!on) { _roomWallView = false; SetHomeCamera(); SetRoomGrid(false); PlaceGooby(garden: false); return; }
            _roomCam = 0;
            _roomWallView = false;
            ApplyRoomCamera();
            SetRoomGrid(true);
            // board 09: Gooby stands on the front of the rug, clear of the crate (InCrate radius 0.45 m)
            if (Room != null) PlaceGoobyAt(Room.Center + new Vector3(-0.85f, 0f, -0.85f), "idle-loop");
        }

        public void CycleRoomCamera()
        {
            _roomCam = (_roomCam + 1) % 3;
            ApplyRoomCamera();
        }

        private bool _roomWallView;

        /// <summary>
        /// While a picture, mirror or wall lamp is picked or focused, the default room camera stands lower and further
        /// back: pictures hang at about 1.7 m and the steep floor view cuts the walls off below the ceiling.
        /// </summary>
        public void SetRoomWallView(bool on)
        {
            if (_roomWallView == on) return;
            _roomWallView = on;
            if (_roomCam == 0) ApplyRoomCamera();
        }

        private void ApplyRoomCamera()
        {
            var c = Room != null ? Room.Center : Vector3.zero;
            var k = Room != null ? Mathf.Clamp(Mathf.Max(Room.WidthM / 6f, Room.DepthM / 5f), 0.7f, 1.3f) : 1f;
            SceneDressing.SetRoomBuildGrade(Look);
            Camera.backgroundColor = ArtLibrary.Hex("CFE6FF");   // sky above the hedge, not a beige void
            Camera.fieldOfView = 40f;
            switch (_roomCam)
            {
                case 0 when _roomWallView: Camera.transform.position = c + new Vector3(0f, 4.3f * k, -6.6f * k); Camera.transform.rotation = Quaternion.Euler(27f, 0f, 0f); break;
                case 0: Camera.transform.position = c + new Vector3(0f, 6.4f * k, -5.6f * k); Camera.transform.rotation = Quaternion.Euler(50f, 0f, 0f); break;
                case 1: Camera.transform.position = c + new Vector3(0f, 8.5f * k, -0.2f); Camera.transform.rotation = Quaternion.Euler(89f, 0f, 0f); break;
                default: Camera.transform.position = c + new Vector3(-4.6f * k, 4.6f * k, -4.2f * k); Camera.transform.rotation = Quaternion.Euler(44f, 42f, 0f); break;
            }
            // board 02: the diorama's floor sharp, the hedge around the shell soft
            SceneDressing.SetDioramaFocus(Look, Vector3.Distance(Camera.transform.position, c), 70f, 4f);
        }

        /// <summary>Translucent cell grid over the room floor while building (board 09 "Raster").</summary>
        public void SetRoomGrid(bool on)
        {
            if (!on) { if (_roomGrid != null) _roomGrid.SetActive(false); return; }
            if (Room == null) return;
            if (_roomGrid == null)
            {
                _roomGrid = new GameObject("RoomGrid");
                _roomGrid.transform.SetParent(_home.transform, false);
                var line = ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.45f));
                var w = Room.WidthM; var d = Room.DepthM;
                var origin = Room.Origin;
                var cells = 0.5f;
                for (var x = 0; x <= Mathf.RoundToInt(w / cells); x++)
                    ArtLibrary.Quad(_roomGrid.transform, "GX", origin + new Vector3(x * cells, 0.014f, d * 0.5f), Quaternion.Euler(-90f, 0f, 0f), new Vector2(0.018f, d), line);
                for (var y = 0; y <= Mathf.RoundToInt(d / cells); y++)
                    ArtLibrary.Quad(_roomGrid.transform, "GZ", origin + new Vector3(w * 0.5f, 0.014f, y * cells), Quaternion.Euler(-90f, 0f, 0f), new Vector2(w, 0.018f), line);
            }
            _roomGrid.SetActive(true);
        }

        // ── time of day (outdoor stages) ────────────────────────────────────

        private float _todTimer;
        private Color _daySky = ArtLibrary.Hex("7FB8FF");

        /// <summary>Local hour of the game clock (pinned/offset clocks move the sun with them).</summary>
        public double LocalHour
        {
            get
            {
                if (_gs == null) return 12.0;
                var now = _gs.Clock.NowMs();
                return DayPhase.LocalHour(now, _gs.Clock.UtcOffsetMinutes(now));
            }
        }

        public bool Outdoors => _vacation == null && (_park != null || (_interior != null ? _interior.Outdoors : (_city != null && _city.Root.activeSelf)) || (_garden != null && _garden.Root.activeSelf && _interior == null));

        // ── inside a place ──────────────────────────────────────────────────

        private InteriorScene _interior;
        private bool _interiorFromStreet;

        /// <summary>The place interior on stage (null on the street / at home).</summary>
        public InteriorScene Interior => _interior;

        /// <summary>
        /// Walks into a place: the street is hidden, the interior of the place is built far off and the camera
        /// looks in from its door; Gooby stands in front of the counter. Call inside a fade.
        /// </summary>
        public void EnterInterior(DriveModel.Place place)
        {
            if (place == null) return;
            EndRoutine();
            if (_interior == null) _interiorFromStreet = _city != null && _city.Root.activeSelf;
            _interior?.Destroy();
            _city?.Root.SetActive(false);
            _interior = new InteriorScene(_root.transform, place);
            ApplyInteriorView();
        }

        /// <summary>Light, camera and Gooby's spot for the place on stage (entering it, or back from a holiday visit).</summary>
        private void ApplyInteriorView()
        {
            if (_interior == null) return;
            if (_interior.Place.Id == "raumstation")
            {
                SceneDressing.SetSpace(Look);
                SceneDressing.SetWindowCookie(Look, false);
                SceneDressing.SetFocus(Look, 18f, 42f);
            }
            else
            {
                SceneDressing.SetIndoor(Look, !_interior.Outdoors);
                SceneDressing.SetWindowCookie(Look, false);   // board 10's shops are lit high-key by their ceiling lamps
                SceneDressing.SetFocus(Look, 14f, 60f);
            }
            Camera.transform.position = _interior.CameraPosition;
            Camera.transform.rotation = _interior.CameraRotation;
            Camera.fieldOfView = 42f;
            Camera.backgroundColor = _interior.Outdoors ? ApplyTimeOfDay(_interior.Sky) : _interior.Sky;
            PlaceGoobyAt(_interior.GoobySpot, "idle-loop");
        }

        /// <summary>Back out onto the street (the car waits at the kerb); Gooby returns to his spot at home.</summary>
        public void LeaveInterior()
        {
            if (_interior == null) return;
            _interior.Destroy();
            _interior = null;
            PlaceGooby(InGarden);
            if (_interiorFromStreet && _city != null) SetCityScene(true);
            else if (InGarden) { EnsureGarden(); _buildCam = 0; ApplyBuildCamera(); }
            else SetHomeCamera();
        }

        /// <summary>Sun, sky and lamps for the current hour on whatever outdoor stage is up; returns the sky colour.</summary>
        private Color ApplyTimeOfDay(Color daySky)
        {
            _daySky = daySky;
            _todTimer = 30f;
            var hour = LocalHour;
            var sky = SceneDressing.SetTimeOfDay(Look, hour, daySky);
            var lamps = DayPhase.LampsOn(hour);
            _city?.SetNight(lamps);
            _garden?.SetNight(lamps);
            _park?.SetNight(lamps);
            // distance haze only over the town: the garden is 30 m deep and every interior is a closed room
            SceneDressing.SetHaze(_city != null && _city.Root.activeSelf && _interior == null, sky);
            return ApplyWeatherLight(hour, sky);
        }

        /// <summary>Re-applies the outdoor light every 30 s so a long stay on the street sees dusk fall.</summary>
        private void TickTimeOfDay(float dt)
        {
            if (!Outdoors || Look == null) return;
            _todTimer -= dt;
            if (_todTimer > 0f) return;
            Camera.backgroundColor = ApplyTimeOfDay(_daySky);
        }

        // ── city ────────────────────────────────────────────────────────────

        /// <summary>The street simulation (Core DriveModel); survives leaving and re-entering the drive screen.</summary>
        public DriveModel.State Drive { get; private set; }

        /// <summary>AppSettings.ReducedMotion, set by GameHost each frame (walkers skip waves, sparks stay off).</summary>
        public bool ReducedMotion { get; set; }

        /// <summary>True for the DriveStep that just honked a near-miss; the drive HUD toasts and clears it.</summary>
        public bool LastNearMiss { get; private set; }

        public void SetCityScene(bool on)
        {
            if (_interior != null && !on) { _interior.Destroy(); _interior = null; PlaceGooby(InGarden); }
            if (!on)
            {
                _city?.Root.SetActive(false);
                if (InGarden) { EnsureGarden(); _buildCam = 0; ApplyBuildCamera(); }
                else SetHomeCamera();
                return;
            }
            if (_interior != null) { _interior.Destroy(); _interior = null; PlaceGooby(InGarden); }
            ParkGarageCar();
            if (_city == null)
            {
                _city = new CityScene(_root.transform, LocalHour);
                Drive = new DriveModel.State();
                PaintPlayerCar();
            }
            _city.Root.SetActive(true);
            SceneDressing.SetFocus(Look, 60f, 140f);
            SceneDressing.SetIndoor(Look, false);
            Camera.backgroundColor = ApplyTimeOfDay(ArtLibrary.Hex("6FB2F2"));
            Camera.farClipPlane = 1200f;
            _camYaw = (float)Drive.HeadingDeg;
            DriveTick(0f);
        }

        private float _camYaw, _followLog;

        /// <summary>Advances the simulation with the screen's input and moves car + chase camera.</summary>
        public void DriveStep(DriveModel.Input input, float dt)
        {
            if (Drive == null) return;
            LastNearMiss = false;
            // the ambient car in front is something to drive behind (and around), not through
            if (_city?.Life != null && input.Throttle > 0.0 && !input.Brake)
            {
                var leader = _city.Life.LeaderAhead(new Vector3((float)Drive.X, 0f, (float)Drive.Z), new Vector3((float)Drive.ForwardX, 0f, (float)Drive.ForwardZ));
                if (leader.HasValue)
                {
                    input.FollowGap = leader.Value.gap; input.FollowKmh = leader.Value.kmh;
                    _followLog -= dt;
                    if (_followLog <= 0f) { _followLog = 1f; Debug.Log($"[drive] following gap={leader.Value.gap:F1} m lead={leader.Value.kmh:F0} km/h own={Drive.Kmh:F0}"); }
                }
                if (LifeRedAhead()) input.RedLight = true;
            }
            DriveModel.Step(Drive, input, dt);
            if (_carRig != null) _carRig.Braking = input.Brake;
            DriveTick(dt);
            if (_city?.Life != null)
                LastNearMiss = _city.Life.PollNearMiss(new Vector3((float)Drive.X, 0f, (float)Drive.Z), Drive.Kmh / 3.6, dt, ReducedMotion);
            // QA heartbeat: the car's tile, position, heading and speed once a second (tools/playtest/wait_drive.py anchors the tours on it)
            _posLog -= dt;
            if (_posLog <= 0f)
            {
                _posLog = 1f;
                var tile = CityGrid.Tile(Drive.X, Drive.Z);
                Debug.Log($"[drive] at=({Drive.X:F1},{Drive.Z:F1}) tile=({tile.r},{tile.c}) heading={Drive.HeadingDeg:F0} kmh={Drive.Kmh:F0} red={(input.RedLight ? 1 : 0)}");
            }
        }

        private bool LifeRedAhead() =>
            _city?.Life != null && _city.Life.RedAheadOfPlayer(Drive.X, Drive.Z, Drive.ForwardX, Drive.ForwardZ);

        private float _posLog;
        private TravelCutsceneScene _cutscene;
        private Vector3 _preCutsceneCamPos;
        private Quaternion _preCutsceneCamRot;
        private bool _cutsceneHidGooby;

        /// <summary>GOOB-1 low-G hop: the player's Gooby bounces higher and slower than on the ground.</summary>
        private void TickStationHop(float dt)
        {
            if (Gooby == null || _interior == null || _interior.Place.Id != "raumstation") return;
            if (ReducedMotion) { Gooby.position = _interior.GoobySpot; return; }
            var hop = Raumstation.HopParameter(Raumstation.HopBasisHoehe, Raumstation.HopBasisDauer);
            var u = (Time.time % (float)hop.Dauer) / (float)hop.Dauer;
            var y = (float)hop.Hoehe * Mathf.Sin(u * Mathf.PI);
            Gooby.position = _interior.GoobySpot + Vector3.up * y;
        }

        /// <summary>Catch-up the delivery slice and pose the kitchen→home van when the street is up.</summary>
        private void TickGooberando()
        {
            if (_gs == null) return;
            var now = _gs.Clock.NowMs();
            var caught = GooberandoLogic.CatchUp(_gs, now);
            foreach (var e in caught.Events)
                Debug.Log("[gooberando] " + e.Typ + (!string.IsNullOrEmpty(e.GerichtId) ? " " + e.GerichtId : ""));
            if (_city?.Life == null) return;
            var slice = GooberandoLogic.SliceOf(_gs.State);
            var st = J.Str(slice["state"]);
            if (st == GooberandoLogic.StateIdle || st.Length == 0)
            {
                _city.Life.SetDelivery(null);
                return;
            }
            var start = GooberandoRestaurants.StreetTile(J.Str(slice["restaurantId"]));
            var home = (CityGrid.HomeStreet[0], CityGrid.HomeStreet[1]);
            var route = GooberandoFahrerSim.RouteWorld(start, home);
            var pose = GooberandoFahrerSim.At(route, (long)J.Num(slice["bestelltAt"]), (long)J.Num(slice["fertigAt"]), now);
            _city.Life.SetDelivery(pose);
        }

        public void EnterTravelCutscene(string modus)
        {
            _preCutsceneCamPos = Camera.transform.position;
            _preCutsceneCamRot = Camera.transform.rotation;
            _interior?.Root.SetActive(false);
            _city?.Root.SetActive(false);
            _cutscene?.Destroy();
            _cutscene = new TravelCutsceneScene(_root.transform, modus);
            SceneDressing.SetIndoor(Look, false);
            SceneDressing.SetWindowCookie(Look, false);
            SceneDressing.SetFocus(Look, 40f, 90f);
            SceneDressing.SetHaze(false, _cutscene.Sky);   // the stage sets are a few metres deep
            Camera.transform.position = _cutscene.CameraPosition;
            Camera.transform.rotation = _cutscene.CameraRotation;
            Camera.backgroundColor = _cutscene.Sky;
            Camera.fieldOfView = 50f;
            if (Gooby != null && Gooby.gameObject.activeSelf) { Gooby.gameObject.SetActive(false); _cutsceneHidGooby = true; }
        }

        public void TravelCutsceneShot(string id)
        {
            if (_cutscene == null) return;
            _cutscene.ApplyShot(id);
            Camera.transform.position = _cutscene.CameraPosition;
            Camera.transform.rotation = _cutscene.CameraRotation;
        }

        public void TickTravelCutscene(float dt)
        {
            if (_cutscene == null) return;
            _cutscene.Tick(dt);
            Camera.transform.position = _cutscene.CameraPosition;
            Camera.transform.rotation = _cutscene.CameraRotation;
        }

        public void LeaveTravelCutscene()
        {
            _cutscene?.Destroy();
            _cutscene = null;
            if (_cutsceneHidGooby && Gooby != null && !GoobyAway) Gooby.gameObject.SetActive(true);
            _cutsceneHidGooby = false;
            if (_interior != null)
            {
                _interior.Root.SetActive(true);
                Camera.transform.position = _interior.CameraPosition;
                Camera.transform.rotation = _interior.CameraRotation;
                Camera.backgroundColor = _interior.Sky;
                Camera.fieldOfView = 42f;
                if (_interior.Place.Id == "raumstation") SceneDressing.SetSpace(Look);
                else SceneDressing.SetIndoor(Look, !_interior.Outdoors);
                PlaceGoobyAt(_interior.GoobySpot, "idle-loop");
                return;
            }
            Camera.transform.position = _preCutsceneCamPos;
            Camera.transform.rotation = _preCutsceneCamRot;
            if (_city != null && _city.Root.activeSelf) { _city.Root.SetActive(true); Camera.backgroundColor = ApplyTimeOfDay(_daySky); DriveTick(0f); }
            else SetHomeCamera();
        }

        private VehicleRig _carRig;
        private float _camDist = 7.0f, _camHeight = 3.1f, _camFov = 56f, _camAccel, _prevDriveV;

        /// <summary>
        /// Board 03: chase camera low behind the car, pitched only ≈7° down so the eye line sits at ≈38 % of the frame
        /// height and a fifth of the frame is sky above the painted ridge (10° down put the ridge at the top edge and
        /// showed no sky at all). The camera swings
        /// round behind the car with a smoothed yaw (k = 4/s), so a turn at a crossing reads as the town turning past,
        /// and peeks a few degrees into the bend the stick is asking for. It tucks in close and narrow at a standstill,
        /// hangs back and widens as the car picks up speed (board 03 is the cruising frame) and is pulled back a little
        /// further under acceleration, closing up again under the brakes. dt = 0 snaps everything (scene entry).
        /// </summary>
        /// <summary>The street car in another model and paint (the dealer's test drive); <see cref="RestoreStreetCar"/> puts ours back.</summary>
        public void DressStreetCar(string modelId, string hex) => _city?.DressCar(modelId, ArtLibrary.Hex(hex));

        public void RestoreStreetCar() => PaintPlayerCar();

        /// <summary>The car stopped at a place's kerb, along the street with the door on its right.</summary>
        public void ParkAt(DriveModel.Place place)
        {
            if (place == null || Drive == null) return;
            Drive.X = place.X;
            Drive.Z = place.Z;
            Drive.Heading = System.Math.Atan2(place.FaceZ, -place.FaceX);
            Drive.Kmh = 0.0;
            DriveTick(0f);
        }

        /// <summary>A friend's drive seen from the passenger seat: his car where his last pose says, our chase camera behind it.</summary>
        public void PlaceCarRemote(double x, double z, double headingDeg, double kmh, float dt)
        {
            if (_city == null || Drive == null) return;
            Drive.X = x;
            Drive.Z = z;
            Drive.Heading = headingDeg * System.Math.PI / 180.0;
            Drive.Kmh = kmh;
            DriveTick(dt);
        }

        public void DriveTick(float dt)
        {
            if (_city == null || !_city.Root.activeSelf || Drive == null) return;
            var car = _city.Car;
            car.position = new Vector3((float)Drive.X, 0f, (float)Drive.Z);
            car.rotation = Quaternion.Euler(0f, (float)Drive.HeadingDeg, 0f);
            if (_carRig == null) _carRig = car.GetComponent<VehicleRig>();
            _carRig?.SetSteer((float)Drive.SteerSmoothed);

            var v = (float)(Drive.Kmh / 3.6);
            var snap = dt <= 0f;
            var accel = snap ? 0f : Mathf.Clamp((v - _prevDriveV) / dt, -12f, 8f);
            _prevDriveV = v;
            _camAccel = snap ? 0f : Mathf.Lerp(_camAccel, accel, 1f - Mathf.Exp(-3f * dt));
            var speed01 = Mathf.Clamp01(Mathf.Abs(v) / (float)(DriveModel.CruiseKmh / 3.6));
            // board 03's car fills ≈14 % of the frame width: a little closer than the 7–8.2 m that gave 10 %
            var distTarget = Mathf.Lerp(6.3f, 7.4f, speed01) + Mathf.Clamp(_camAccel * 0.14f, -0.6f, 0.5f);
            var heightTarget = Mathf.Lerp(2.95f, 3.25f, speed01);
            var fovTarget = Mathf.Lerp(52f, 58f, speed01);
            var ease = snap ? 1f : 1f - Mathf.Exp(-3.5f * dt);
            _camDist = Mathf.Lerp(_camDist, distTarget, ease);
            _camHeight = Mathf.Lerp(_camHeight, heightTarget, ease);
            _camFov = Mathf.Lerp(_camFov, fovTarget, ease);

            var heading = (float)Drive.HeadingDeg;
            var lead = v > 0.5f ? (float)Drive.SteerSmoothed * 5f : 0f;
            _camYaw = snap ? heading : Mathf.LerpAngle(_camYaw, heading + lead, 1f - Mathf.Exp(-4f * dt));
            var back = new Vector3(Mathf.Sin(_camYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_camYaw * Mathf.Deg2Rad));
            Camera.transform.position = car.position - back * _camDist + Vector3.up * _camHeight;
            Camera.transform.rotation = Quaternion.Euler(6.5f + (_camHeight - 2.95f) * 3f, _camYaw, 0f);
            Camera.fieldOfView = _camFov;
            _city.FollowBackdrop(Camera.transform.position, _camYaw);
        }
    }

    /// <summary>Tag on a door group so a tap on it can be resolved to the room door it stands for.</summary>
    public sealed class DoorMarker : MonoBehaviour
    {
        [System.NonSerialized] public RoomDefs.Door Door;
    }
}
