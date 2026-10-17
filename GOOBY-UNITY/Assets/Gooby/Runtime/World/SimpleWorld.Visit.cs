using System.IO;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Net;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// House visits (port of visit_scene.gd / visit_manager.gd). The guest's stage shows the host's rooms from the
    /// snapshot (`PUT /api/house` layout: {v, goobyName, rooms{id:{items}}}) through a throw-away in-memory store — the
    /// own save is never touched. Both sides show the other Gooby as a second avatar driven by the POS relay; rooms are
    /// staged centred on the origin on every client, so world XZ maps 1:1 between host and guest.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        /// <summary>Guest side: the stage shows a friend's house.</summary>
        public bool Visiting => _visitStore != null;

        /// <summary>Host side: a friend walks through our house.</summary>
        public bool Hosting { get; private set; }

        /// <summary>Where the guest steps in (a little south of the living-room rug, clear of the host's crate).</summary>
        private static readonly Vector3 VisitArrivalOffset = new Vector3(0.9f, 0f, -1.1f);

        private GameStateStore _visitStore;

        /// <summary>The store whose rooms are on stage: the host's snapshot while visiting, the own save otherwise.</summary>
        private GameStateStore HouseStore => _visitStore ?? _gs;

        /// <summary>Guest: stages the host's house from a snapshot. False when the layout is unusable or a mode blocks it.</summary>
        public bool EnterVisit(JObject layout, string scratchDir)
        {
            if (_home == null || _gs == null || Build != null || _interior != null) return false;
            if (!(layout?["rooms"] is JObject rooms) || rooms.Count == 0) return false;
            var store = new GameStateStore();
            store.Ticks = () => 0;
            try
            {
                Directory.CreateDirectory(scratchDir);
                var path = Path.Combine(scratchDir, "visit_house.json");
                if (File.Exists(path)) File.Delete(path);
                store.Initialize(path, 0);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[visit] cannot prepare the visit store: " + e.Message);
                return false;
            }
            var unlocked = new JArray();
            var clean = new JObject();
            foreach (var p in rooms.Properties())
            {
                if (RoomDefs.Get(p.Name) == null || p.Name == "garden") continue;
                var items = (p.Value as JObject)?["items"] as JArray ?? new JArray();
                clean[p.Name] = new JObject { ["items"] = items.DeepClone() };
                if ((p.Value as JObject)?["girlanden"] is JArray girlanden)
                {
                    var good = new JArray();
                    foreach (var g in girlanden) if (Girlanden.Gueltig(g)) good.Add(g.DeepClone());
                    if (good.Count > 0) clean[p.Name]["girlanden"] = good;
                }
                unlocked.Add(p.Name);
            }
            if (clean[RoomNavigation.DefaultRoom] == null) return false;
            store.Update(s =>
            {
                var home = J.EnsureDict(s, HomeState.SliceId);
                home["rooms"] = clean;
                home["unlockedRooms"] = unlocked;
                home["storage"] = new JArray();
                home["currentRoom"] = RoomNavigation.DefaultRoom;
                J.EnsureDict(s, "meta")["goobyNickname"] = J.Str(layout["goobyName"], "Gooby");
            });
            if (Hosting) EndHosting();
            if (InGarden && _garden != null) _garden.Root.SetActive(false);
            _home.SetActive(true);
            _visitStore = store;
            RemovePeer();
            StageRoom(RoomNavigation.DefaultRoom);
            PlaceGoobyAt(Room.Center + VisitArrivalOffset, "idle-loop");
            SetHomeCamera();
            Debug.Log("[visit] entered house of " + J.Str(layout["goobyName"], "?") + " rooms=" + clean.Count);
            RoomChanged?.Invoke(RoomId);
            return true;
        }

        /// <summary>Guest: back to the own house (the room the save remembers).</summary>
        public void LeaveVisit()
        {
            if (!Visiting) return;
            _visitStore = null;
            RemovePeer();
            _move = Vector2.zero;
            NappingOnCouch = false;
            _selfieHold = 0f;
            var roomId = RoomNavigation.CurrentRoom(_gs);
            if (roomId == "garden") roomId = RoomNavigation.DefaultRoom;
            StageRoom(roomId);
            PlaceGooby(false);
            SetHomeCamera();
            Debug.Log("[visit] left, back home in " + roomId);
            RoomChanged?.Invoke(RoomId);
        }

        /// <summary>
        /// Guest: the host built (BUILD_DELTA) — applied best effort to our copy of its room; the staged room shows it at once,
        /// other rooms when we walk in. A piece placed over our own cell sends us to the room's door (Doc C §3.4 point 5).
        /// </summary>
        public bool ApplyVisitDelta(JObject delta)
        {
            if (!Visiting || delta == null) return false;
            var roomId = J.Str(delta["roomId"], RoomId);
            if (roomId.Length == 0) roomId = RoomId;
            var def = RoomDefs.Get(roomId);
            if (def == null || roomId == "garden") return false;
            var grid = HomeState.LoadRoomGrid(_visitStore, roomId);
            var staged = roomId == RoomId && Room != null && !InGarden;
            var occupant = new Cell(-99, -99);
            if (staged && Gooby != null)
            {
                var origin = WalkOrigin;
                var p = Gooby.position;
                occupant = new Cell(Mathf.FloorToInt((p.x - origin.x) / 0.5f), Mathf.FloorToInt((origin.z + grid.Height * 0.5f - p.z) / 0.5f));
            }
            var applied = VisitBuild.Apply(grid, delta, occupant);
            if (!applied.Ok)
            {
                Debug.Log("[visit] build delta skipped: " + J.Str(delta["op"]) + " " + J.Str(delta["item"]) + " → " + applied.Reason);
                return false;
            }
            HomeState.SaveRoomGrid(_visitStore, roomId, grid);
            if (staged)
            {
                var placed = grid.GetItem(applied.Uid);
                if (placed == null)
                {
                    if (!Room.RemoveItemView(applied.Uid)) Room.Refresh(HouseStore);
                }
                else
                {
                    Room.AddItemView(placed);
                    var at = PropPlaceholder.CellCenter(Room.Origin, grid.Height, placed.At, placed.Def.FootprintW, placed.Def.FootprintH, placed.Rot);
                    Effects.Sparkle(at + Vector3.up * 0.35f, 0.45f);
                }
                InvalidateWalkGrid();
                if (applied.Teleport)
                {
                    _move = Vector2.zero;
                    var (gx, gy) = VisitBuild.DoorSpot(def, grid);
                    PlaceGoobyAt(WorldOf(def, Room.Origin, gx, gy), "idle-loop");
                }
            }
            Debug.Log("[visit] build delta " + J.Str(delta["op"]) + " " + J.Str(delta["item"]) + " in " + roomId + (applied.Teleport ? " (teleport)" : ""));
            BuildDeltaApplied?.Invoke(roomId, applied.Teleport);
            return true;
        }

        /// <summary>Guest: a host build delta landed (room, whether it moved us to the door).</summary>
        public event System.Action<string, bool> BuildDeltaApplied;

        /// <summary>Items of a staged house room as "itemId@x,y/rot" (QA state: the guest sees the host's changes).</summary>
        public string[] RoomItemsOf(string roomId)
        {
            if (HouseStore == null || RoomDefs.Get(roomId) == null) return new string[0];
            var grid = HomeState.LoadRoomGrid(HouseStore, roomId);
            var list = new System.Collections.Generic.List<string>();
            foreach (var t in grid.ToItemsArray())
            {
                if (!(t is JObject o) || !(o["at"] is JArray at) || at.Count < 2) continue;
                list.Add(J.Str(o["item"]) + "@" + J.Int(at[0]) + "," + J.Int(at[1]) + (o["wall"] != null ? "/" + J.Str(o["wall"]) : "/" + J.Int(o["rot"])));
            }
            list.Sort(System.StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>Host: a guest arrived — the lawn is not part of a visit, the host waits in the house.</summary>
        public void BeginHosting()
        {
            Hosting = true;
            if (InGarden) EnterRoom(RoomNavigation.DefaultRoom);
            Debug.Log("[visit] hosting");
        }

        public void EndHosting()
        {
            Hosting = false;
            RemovePeer();
            _peerCouchNap = false;
            _selfieHold = 0f;
            Debug.Log("[visit] hosting ended");
        }

        // ── the other Gooby ─────────────────────────────────────────────────

        private Transform _peer;
        private GoobyAvatar _peerAvatar;
        private bool _peerLoading;
        private Vector3 _peerTarget;
        private string _peerAnim = "idle-loop", _peerRoom = "", _peerClip = "";
        private float _peerYaw = 180f;
        private bool _peerHasPose;
        private System.Collections.Generic.Dictionary<string, string> _peerLook;

        /// <summary>The peer's Gooby (null while not in the staged room) — the HUD pins the name tag to it.</summary>
        public Transform Peer => _peer != null && _peer.gameObject.activeSelf ? _peer : null;

        /// <summary>The peer's Gooby naps (board-08 crate) or sleeps — sleepy eyes and a Zzz instead of a wave.</summary>
        public bool PeerNapping => Peer != null && IsNapClip(_peerAnim);

        private static bool IsNapClip(string clip) => clip == "sit-loop" || clip == "sleep-loop";

        /// <summary>Peer head bone (or a point above the stand-in) for name tags and emote bubbles.</summary>
        public Vector3 PeerHead
        {
            get
            {
                if (_peer == null) return Vector3.zero;
                var bone = _peerAvatar != null ? _peerAvatar.Bone("head") : null;
                return bone != null ? bone.position + Vector3.up * 0.42f : _peer.position + Vector3.up * 1.45f;
            }
        }

        /// <summary>Own head bone (or a point above the stand-in) — the visit HUD floats the nap Zzz there.</summary>
        public Vector3 OwnHead
        {
            get
            {
                if (Gooby == null) return Vector3.zero;
                var bone = Avatar != null ? Avatar.Bone("head") : null;
                return bone != null ? bone.position + Vector3.up * 0.34f : Gooby.position + Vector3.up * (CrateNapping ? 0.82f : 1.25f);
            }
        }

        /// <summary>Latest POS of the peer (world XZ + anim + room id).</summary>
        public void SetPeerPose(float x, float z, string anim, string roomId)
        {
            _peerTarget = new Vector3(x, 0f, z);
            _peerAnim = AnimClip(anim);
            _peerRoom = roomId ?? "";
            if (!_peerHasPose && _peer != null) _peer.position = _peerTarget;
            _peerHasPose = true;
            EnsurePeer();
        }

        /// <summary>The peer's outfit (LOOK relay: equipped map incl. fur).</summary>
        public void SetPeerLook(System.Collections.Generic.Dictionary<string, string> equipped)
        {
            _peerLook = equipped;
            if (_peerAvatar != null && equipped != null) _peerAvatar.Wear(equipped);
        }

        /// <summary>Emote relay: heart (sparkles + hop), wave, hop.</summary>
        public void PeerEmote(string id)
        {
            if (_peer == null) return;
            switch (id)
            {
                case "heart":
                    Effects.Sparkle(PeerHead, 0.5f, 26);
                    _peerAvatar?.Play("hop");
                    break;
                case "wave":
                    _peerAvatar?.Play("wave");
                    break;
                case global::Gooby.Core.Net.SelfieAblauf.RelayEmote:
                    // the friend takes a "Snap A Gooby!": their Gooby lifts the phone for the countdown
                    _peerAvatar?.SetEmotion("happy");
                    _peerAvatar?.Play("phone_up-loop", 0.2f);
                    _peerClip = "";
                    _peerEmoteHold = 4.6f;
                    return;
                default:
                    _peerAvatar?.Play("hop");
                    break;
            }
            _peerClip = "";
            _peerEmoteHold = 1.1f;
        }

        /// <summary>Guest: our Gooby is curled up on the host's couch (or the floor) — the couch rule of couch_logic.gd.</summary>
        public bool NappingOnCouch { get; private set; }

        private bool _peerCouchNap;

        /// <summary>
        /// Guest: our Gooby goes to the living room's couch and naps on it (staging the living room first when he is
        /// elsewhere); without a couch he naps on the floor where he stands. Returns the couch cell and whether it is a
        /// floor nap.
        /// </summary>
        public (Cell cell, bool floor) StartCouchNap()
        {
            if (Gooby == null || NappingOnCouch) return (new Cell(0, 0), true);
            NappingOnCouch = true;
            var couch = CouchLogic.CouchSuchen(HomeState.RoomItems(HouseStore, CouchLogic.Wohnzimmer));
            if (!couch.ok) { LieDown(); return (new Cell(0, 0), true); }
            if (RoomId != CouchLogic.Wohnzimmer)
            {
                StageRoom(CouchLogic.Wohnzimmer);
                PlaceGoobyAt(Room.Center, "idle-loop");
                RoomChanged?.Invoke(RoomId);
            }
            var marker = Room?.Markers().FirstOrDefault(m => m != null && m.ItemId == couch.itemId);
            if (marker == null) { LieDown(); return (couch.cell, false); }
            WalkGoobyTo(ApproachPoint(marker, 0.2f), () =>
            {
                if (!NappingOnCouch || Gooby == null) return;
                HaltWalk();
                Gooby.position = new Vector3(marker.Bounds.center.x, 0f, marker.Bounds.center.z) + marker.Front * 0.05f;
                _goobyYaw = Mathf.Atan2(marker.Front.x, marker.Front.z) * Mathf.Rad2Deg;
                ApplyWalkPose();
                Gooby.position += Vector3.up * Mathf.Clamp(marker.Bounds.size.y * 0.42f, 0.2f, 0.45f);
                LieDown();
            }, 9f);
            return (couch.cell, false);
        }

        private void LieDown()
        {
            Avatar?.SetEmotion("sleepy");
            Avatar?.Play("sleep-loop", 0.3f);
        }

        /// <summary>Guest: woken up — he hops down where he lay and is himself again.</summary>
        public void EndCouchNap()
        {
            if (!NappingOnCouch) return;
            NappingOnCouch = false;
            CancelAutoWalk();
            if (Gooby != null) PlaceGoobyAt(new Vector3(Gooby.position.x, 0f, Gooby.position.z) + Vector3.back * 0.45f, "idle-loop");
            Avatar?.SetEmotion("happy");
        }

        /// <summary>Host: the visiting Gooby sleeps on our couch (its POS anim says sleep) — lie it at seat height.</summary>
        public void SetPeerCouchNap(bool on) => _peerCouchNap = on;

        private float _selfieHold;
        private Vector3 _selfieSpot;

        /// <summary>
        /// "Snap A Gooby!" (snap_a_gooby.gd): our Gooby faces the camera and lifts the phone, the friend's Gooby hops into
        /// the picture beside him and faces the camera too, for <paramref name="seconds"/> (then the POS relay leads again).
        /// </summary>
        public void SelfiePose(float seconds)
        {
            if (Gooby == null) return;
            _walkTarget = null;
            _move = Vector2.zero;
            Gooby.rotation = Quaternion.Euler(0f, 180f, 0f);
            Avatar?.SetEmotion("happy");
            Avatar?.Play("phone_up-loop", 0.2f);
            if (Peer == null) return;
            _selfieSpot = new Vector3(Gooby.position.x + 0.9f, 0f, Gooby.position.z + 0.15f);
            _selfieHold = seconds;
            _peerAvatar?.SetEmotion("happy");
            _peerAvatar?.Play("hop");
            _peerClip = "";
            _peerEmoteHold = 0.7f;
        }

        /// <summary>After the click: our Gooby lowers the phone, the friend's Gooby walks back to where its owner has it.</summary>
        public void SelfieRelax()
        {
            _selfieHold = 0f;
            Avatar?.Play("idle-loop", 0.25f);
        }

        /// <summary>Own emote: the same visual on our Gooby.</summary>
        public void OwnEmote(string id)
        {
            if (Gooby == null) return;
            if (id == "heart")
            {
                var bone = Avatar != null ? Avatar.Bone("head") : null;
                Effects.Sparkle(bone != null ? bone.position + Vector3.up * 0.42f : Gooby.position + Vector3.up * 1.4f, 0.5f, 26);
                Bounce();
            }
            else if (id == "wave") Avatar?.Play("wave");
            else Bounce();
        }

        /// <summary>Own pose for the POS relay: floor XZ, a short anim name and the staged room.</summary>
        public (float x, float z, string anim) OwnPose
        {
            get
            {
                if (Gooby == null) return (0f, 0f, "idle");
                var anim = _walking ? "walk" : InCrate ? "sit" : Sleeping ? "sleep" : "idle";
                return (Gooby.position.x, Gooby.position.z, anim);
            }
        }

        private bool Sleeping => Avatar != null && Avatar.Clip == "sleep-loop";

        private float _peerEmoteHold;

        private static string AnimClip(string anim)
        {
            var a = (anim ?? "").ToLowerInvariant();
            if (a.Contains("walk") || a.Contains("run")) return "walk-loop";
            if (a.Contains("sit") || a.Contains("nap")) return "sit-loop";
            if (a.Contains("sleep")) return "sleep-loop";
            return "idle-loop";
        }

        private void EnsurePeer()
        {
            if (_peer != null || _peerLoading || _root == null) return;
            var start = _peerHasPose ? _peerTarget : (Room != null ? Room.Center : Vector3.zero);
            // stand-in first (instant), the rigged avatar swaps in when the GLB is ready
            _peer = BuildGooby(_root.transform, start + Vector3.up * 0.6f);
            _peer.name = "PeerGooby";
            if (!AssetLoader.Available) return;
            _peerLoading = true;
            _ = SwapInPeerAsync();
        }

        private async System.Threading.Tasks.Task SwapInPeerAsync()
        {
            var avatar = await GoobyAvatar.CreateAsync(_root.transform, _peerTarget);
            _peerLoading = false;
            if (avatar == null) return;
            if (_peer == null) { Object.Destroy(avatar.gameObject); return; } // the visit ended while importing
            avatar.name = "PeerAvatar";
            avatar.transform.position = new Vector3(_peer.position.x, 0f, _peer.position.z);
            avatar.transform.rotation = Quaternion.Euler(0f, _peerYaw, 0f);
            avatar.SetEmotion(IsNapClip(_peerAnim) ? "sleepy" : "happy");
            avatar.Play(_peerAnim);
            _peerClip = _peerAnim;
            if (_peerLook != null) avatar.Wear(_peerLook);
            var wasActive = _peer.gameObject.activeSelf;
            Object.Destroy(_peer.gameObject);
            _peer = avatar.transform;
            _peerAvatar = avatar;
            _peer.gameObject.SetActive(wasActive);
        }

        private void RemovePeer()
        {
            if (_peer != null) Object.Destroy(_peer.gameObject);
            _peer = null;
            _peerAvatar = null;
            _peerHasPose = false;
            _peerRoom = "";
            _peerClip = "";
            _peerYaw = 180f;
        }

        /// <summary>Glides the peer toward its last POS (the relay runs at 5 Hz), faces the walk direction.</summary>
        private void TickPeer(float dt)
        {
            if (_peer == null) return;
            var active = (Visiting || Hosting) && _peerHasPose && _interior == null && _peerRoom == RoomId && !InGarden;
            if (_peer.gameObject.activeSelf != active) _peer.gameObject.SetActive(active);
            if (!active) return;
            var standIn = _peerAvatar == null;
            var selfie = _selfieHold > 0f;
            if (selfie) _selfieHold -= dt;
            var target = selfie ? _selfieSpot : _peerTarget;
            var pos = new Vector3(_peer.position.x, 0f, _peer.position.z);
            var delta = target - pos;
            var dist = delta.magnitude;
            if (dist > 4f) pos = target; // room change / lag spike: jump instead of sliding through walls
            else pos = Vector3.Lerp(pos, target, 1f - Mathf.Exp(-9f * dt));
            if (selfie && dist <= 0.15f) _peerYaw = Mathf.LerpAngle(_peerYaw, 180f, 1f - Mathf.Exp(-8f * dt));
            else if (dist > 0.03f) _peerYaw = Mathf.LerpAngle(_peerYaw, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-10f * dt));
            // POS carries no heading: a Gooby napping in the crate faces the room, as on its owner's screen
            else if (IsNapClip(_peerAnim)) _peerYaw = Mathf.LerpAngle(_peerYaw, 180f, 1f - Mathf.Exp(-6f * dt));
            var y = standIn ? 0.6f : 0f;
            if (_peerAnim == "sit-loop" && !standIn) y = 0.42f;
            if (_peerAnim == "sleep-loop" && _peerCouchNap && !standIn) y = 0.38f;
            _peer.position = new Vector3(pos.x, y, pos.z);
            _peer.rotation = Quaternion.Euler(0f, _peerYaw, 0f) * (standIn ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);
            if (_peerEmoteHold > 0f) { _peerEmoteHold -= dt; return; }
            var clip = dist > 0.12f ? "walk-loop" : _peerAnim == "walk-loop" ? "idle-loop" : _peerAnim;
            if (_peerAvatar != null && clip != _peerClip)
            {
                if (IsNapClip(clip) != IsNapClip(_peerClip)) _peerAvatar.SetEmotion(IsNapClip(clip) ? "sleepy" : "happy");
                _peerAvatar.Play(clip);
                _peerClip = clip;
            }
        }
    }
}
