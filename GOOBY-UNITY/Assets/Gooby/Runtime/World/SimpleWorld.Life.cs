using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The world side of Gooby's own life at home (port of the visuals in gooby_reactions.gd / seele_runner.gd): a tap
    /// target on Gooby, the spots his intents and idle acts walk to (a piece by item prefix or id, the door towards a
    /// room, the window wall, his crate), the pillow-tower gag and a check whether he is free for a life of his own.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        /// <summary>The player tapped Gooby (pets him).</summary>
        public event Action GoobyTapped;

        private CapsuleCollider _goobyHit;
        private GameObject _tower;
        private float _towerLeft;

        /// <summary>A capsule around Gooby for taps (the stand-in and the imported avatar alike).</summary>
        private void EnsureGoobyCollider()
        {
            if (Gooby == null || (_goobyHit != null && _goobyHit.transform == Gooby)) return;
            if (!Gooby.TryGetComponent(out _goobyHit)) _goobyHit = Gooby.gameObject.AddComponent<CapsuleCollider>();
            _goobyHit.isTrigger = true;
            _goobyHit.center = new Vector3(0f, Avatar != null ? 0.55f : 0f, 0f);
            _goobyHit.radius = 0.42f;
            _goobyHit.height = 1.2f;
            if (Gooby.GetComponent<GoobyMarker>() == null) Gooby.gameObject.AddComponent<GoobyMarker>();
        }

        private bool TapGooby(RaycastHit hit)
        {
            if (hit.collider.GetComponentInParent<GoobyMarker>() == null || GoobyAway) return false;
            GoobyTapped?.Invoke();
            return true;
        }

        /// <summary>
        /// Free for a life of his own: at home (a room or the lawn), not visiting, not away, no build mode, no routine, no
        /// walk underway and nobody steering him.
        /// </summary>
        public bool LifeFree => Gooby != null && Gooby.gameObject.activeInHierarchy && !GoobyAway && !Visiting && Build == null
            && _interior == null && _park == null && _vacation == null && (_city == null || !_city.Root.activeSelf) && (Room != null || InGarden)
            && Routine == RoutineNone && _walkTarget == null && _move.sqrMagnitude < 0.01f && !_walking && !BallInPlay && !Feeding && !PlayingToy
            && !ShakeHold;

        /// <summary>The room view on stage (the lawn's own view in the garden).</summary>
        public RoomView StageView => InGarden ? _gardenItems : Room;

        /// <summary>Gooby's head in world space (bubble symbols, hearts).</summary>
        public Vector3 GoobyHead
        {
            get
            {
                var bone = Avatar != null ? Avatar.Bone("head") : null;
                if (bone != null) return bone.position + Vector3.up * 0.3f;
                return Gooby != null ? Gooby.position + Vector3.up * (CrateNapping ? 0.9f : 1.25f) : Vector3.zero;
            }
        }

        /// <summary>The pieces on stage whose item id starts with <paramref name="prefix"/> (or equals it when exact).</summary>
        public List<RoomView.Piece> PiecesMatching(string prefix, bool exact = false) =>
            (StageView?.Pieces() ?? Enumerable.Empty<RoomView.Piece>())
                .Where(p => exact ? p.ItemId == prefix : p.ItemId.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        public List<RoomView.Piece> AllPieces() => (StageView?.Pieces() ?? Enumerable.Empty<RoomView.Piece>()).ToList();

        /// <summary>Where Gooby stands to use a piece: in front of it, on the nearest free cell.</summary>
        public Vector3 SpotAt(RoomView.Piece piece) => ApproachPoint(piece.Bounds, piece.Front, 0.4f);

        /// <summary>The door gag: Gooby wedged in the frame of <paramref name="door"/>, his back to the room, pushing.</summary>
        public void WedgeInDoor(RoomDefs.Door door)
        {
            if (Room == null || Gooby == null || door == null) return;
            foreach (var marker in Room.Root.GetComponentsInChildren<DoorMarker>())
            {
                if (marker.Door != door) continue;
                var inward = marker.transform.rotation * Vector3.back;
                inward.y = 0f;
                inward.Normalize();
                var p = marker.transform.position + inward * 0.35f;
                _walkTarget = null;
                _walking = false;
                _move = Vector2.zero;
                Gooby.position = new Vector3(p.x, Gooby.position.y, p.z);
                Gooby.rotation = Quaternion.LookRotation(-inward);
                Avatar?.Play("idle-loop");
                return;
            }
        }

        /// <summary>A spot just inside the door that leads to <paramref name="toRoom"/> (null: no such door here).</summary>
        public Vector3? DoorSpot(string toRoom)
        {
            if (Room == null || InGarden) return null;
            foreach (var marker in Room.Root.GetComponentsInChildren<DoorMarker>())
            {
                if (marker.Door == null || marker.Door.To != toRoom) continue;
                // the door's local -Z points into the room; stop short of its 2×2-cell zone so he does not walk through
                var inward = marker.transform.rotation * Vector3.back;
                inward.y = 0f;
                var p = marker.transform.position + inward.normalized * 1.25f;
                return new Vector3(p.x, 0f, p.z);
            }
            return null;
        }

        /// <summary>
        /// Where Gooby looks out: the free cell nearest the north wall under a window (the wall's middle in a room
        /// without one). Rows further from the wall count double, so he stands close under the sill.
        /// </summary>
        public Vector3? WindowSpot()
        {
            if (Room == null || InGarden) return null;
            var grid = _walkGrid ?? (_walkGrid = HomeState.LoadRoomGrid(HouseStore, RoomId));
            var origin = WalkOrigin;
            var windows = Room.WindowCenters();
            var wantX = windows.Count > 0 ? windows[0] : Room.Center.x;
            Vector3? best = null;
            var bestScore = float.MaxValue;
            for (var y = 0; y < Mathf.Min(grid.Height, 6); y++)
                for (var x = 0; x < grid.Width; x++)
                {
                    var c = new Cell(x, y);
                    if (!grid.InBounds(c) || !grid.Walkable(c)) continue;
                    var p = new Vector3(origin.x + (x + 0.5f) * 0.5f, 0f, origin.z + grid.Height * 0.5f - (y + 0.5f) * 0.5f);
                    var score = Mathf.Abs(p.x - wantX) + y * 1.0f;
                    if (score < bestScore) { bestScore = score; best = p; }
                }
            return best;
        }

        /// <summary>The window Gooby looks out of from a spot (a point up on the north wall above it).</summary>
        public Vector3 WindowLook(Vector3 spot)
        {
            var windows = Room?.WindowCenters();
            var x = windows != null && windows.Count > 0 ? windows[0] : spot.x;
            return new Vector3(x, 1.6f, spot.z + 3f);
        }

        private GobtyTv _glanceTv;
        private float _glanceLeft;
        private RoomView _glanceRoom;

        /// <summary>
        /// Gooby's TV idle act: GOB.TY plays on the room's TV for a while (no routine, no camera glide). False without a
        /// TV here or while the TV routine runs.
        /// </summary>
        public bool TvGlance(Func<string, string> t, float seconds)
        {
            if (Room == null || Routine != RoutineNone || Tv != null) return false;
            var m = Room.Markers().FirstOrDefault(x => x != null && x.Kind == Interactables.Fernseher);
            if (m == null) return false;
            _glanceTv?.Destroy();
            var now = _gs.Clock.NowMs();
            var tomorrow = DateTimeOffset.FromUnixTimeMilliseconds(now + 86_400_000L).ToLocalTime().ToString("yyyy-MM-dd");
            _glanceTv = new GobtyTv(m, Room.Root.transform, t, tomorrow, (int)(now / 3_600_000L) % GobtyTv.Clips.Length);
            _glanceRoom = Room;
            _glanceLeft = seconds;
            Sfx.PlayUi("soft_toggle");
            return true;
        }

        private void TickGlance(float dt)
        {
            if (_glanceTv == null) return;
            _glanceLeft -= dt;
            if (_glanceLeft > 0f && _glanceRoom == Room && Routine == RoutineNone && Build == null)
            {
                _glanceTv.Tick(dt);
                return;
            }
            _glanceTv.Destroy();
            _glanceTv = null;
            _glanceRoom = null;
        }

        /// <summary>Back from a holiday: his suitcase and the teal souvenir bag stand next to him for a while.</summary>
        public void HomecomingProps()
        {
            if (Gooby == null) return;
            if (_tower != null) UnityEngine.Object.Destroy(_tower);
            _tower = new GameObject("HomecomingLuggage");
            _tower.transform.SetParent(StageView != null ? StageView.Root.transform : _root.transform, true);
            var at = Gooby.position + Gooby.right * 0.6f + Vector3.back * 0.15f;
            ArtLibrary.Box(_tower.transform, "Suitcase", at + Vector3.up * 0.23f, new Vector3(0.34f, 0.46f, 0.16f), ArtLibrary.Solid(ArtLibrary.Hex("#C9846B"), 0.25f));
            ArtLibrary.Box(_tower.transform, "Handle", at + Vector3.up * 0.49f, new Vector3(0.14f, 0.05f, 0.05f), ArtLibrary.Solid(ArtLibrary.Hex("#8A5A44"), 0.25f));
            var bag = at + Gooby.right * 0.34f;
            ArtLibrary.Box(_tower.transform, "Bag", bag + Vector3.up * 0.15f, new Vector3(0.26f, 0.3f, 0.18f), ArtLibrary.Solid(ArtLibrary.Hex("#59C9B9"), 0.2f));
            ArtLibrary.Box(_tower.transform, "BagHandle", bag + Vector3.up * 0.34f, new Vector3(0.16f, 0.08f, 0.02f), ArtLibrary.Solid(ArtLibrary.Hex("#3B3630"), 0.2f));
            Effects.Sparkle(bag + Vector3.up * 0.4f, 0.3f, ReducedMotion ? 6 : 20);
            _towerLeft = 12f;
        }

        private float _crateAwakeUntil;

        /// <summary>A tap wakes him in his crate: he stands up happy for a while, then curls up again.</summary>
        public void WakeInCrate(float seconds)
        {
            if (!InCrate || _walking || Avatar == null) return;
            _crateAwakeUntil = Time.time + seconds;
            Avatar.SetEmotion("happy");
            Avatar.Play("idle-loop");
        }

        public bool CrateAwake => Time.time < _crateAwakeUntil;

        private void TickCrateNap()
        {
            if (_crateAwakeUntil <= 0f || Time.time < _crateAwakeUntil) return;
            _crateAwakeUntil = 0f;
            if (InCrate && !_walking && Routine == RoutineNone) PlaceGoobyAt(CratePos, "sit-loop");
        }

        /// <summary>His crate in the living room (board 08), where he naps between his doings.</summary>
        public Vector3? CrateSpot => !InGarden && RoomId == RoomNavigation.DefaultRoom && Room != null ? new Vector3(CratePos.x, 0f, CratePos.z) : (Vector3?)null;

        /// <summary>Turns Gooby towards a world point (a piece he looks at, the window).</summary>
        public void LookAtPoint(Vector3 p) => FaceTowards(p);

        /// <summary>The pillow-tower gag: three soft blocks stacked next to Gooby, gone after ten seconds.</summary>
        public void PillowTower()
        {
            if (Gooby == null) return;
            if (_tower != null) UnityEngine.Object.Destroy(_tower);
            _tower = new GameObject("SoulTower");
            _tower.transform.SetParent(StageView != null ? StageView.Root.transform : _root.transform, true);
            var colors = new[] { "#F4BFCD", "#59C9B9", "#FFD166" };
            var side = Gooby.position + Gooby.right * 0.62f + Vector3.forward * -0.1f;
            for (var i = 0; i < 3; i++)
            {
                var s = 0.4f - 0.06f * i;
                var block = ArtLibrary.Box(_tower.transform, "Pillow" + i, new Vector3(side.x, 0.11f + 0.23f * i, side.z), new Vector3(s, 0.2f, s),
                    ArtLibrary.Pbr("fabric", 0.35f, ArtLibrary.Hex(colors[i]), 0.08f, 0.4f), false);
                block.transform.rotation = Quaternion.Euler(0f, i * 17f - 12f, 0f);
            }
            Effects.Sparkle(side + Vector3.up * 0.5f, 0.35f, ReducedMotion ? 6 : 18);
            _towerLeft = 10f;
        }

        private void TickLife(float dt)
        {
            EnsureGoobyCollider();
            TickCrateNap();
            TickGlance(dt);
            if (_tower == null) return;
            _towerLeft -= dt;
            if (_towerLeft > 0f) return;
            UnityEngine.Object.Destroy(_tower);
            _tower = null;
        }
    }
}
