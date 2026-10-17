using System;
using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Time;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Home interactables in the world (port of interactables_host.gd and the W3d pieces): a tap on a marked piece of
    /// furniture raises <see cref="FurnitureTapped"/> (the HUD offers what it can do), and the routines play out here —
    /// Gooby walks over (the joystick path, so walls and furniture still hold), showers behind a curtain as a wobbling
    /// silhouette with steam, goes to the loo, brushes his teeth at the sink, settles in front of the TV for GOB.TY or
    /// sits down on a seat. <see cref="Routine"/> tells the HUD which buttons to show; lamps follow the day phase.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        public const string RoutineNone = "";

        public event Action<FurnitureMarker, Vector3> FurnitureTapped;
        /// <summary>The running routine changed (HUD buttons: rinse, TV off, stand up …).</summary>
        public event Action<string> RoutineChanged;
        /// <summary>A routine finished: (kind, stat gained).</summary>
        public event Action<string, double> RoutineDone;
        /// <summary>A line Gooby says during a routine (string key).</summary>
        public event Action<string> RoutineSays;

        public string Routine { get; private set; } = RoutineNone;
        public GobtyTv Tv { get; private set; }

        private Vector3? _walkTarget;
        private float _walkTimeout;
        private Action _arrived;
        private FurnitureMarker _routineAt;
        private GameObject _curtain;
        private readonly List<Transform> _silhouette = new List<Transform>();
        private float _routineT, _steamIn, _soundIn, _funIn, _lampCheckIn;
        private bool _peeked;
        private int _tvFunTicks;
        private (Vector3 pos, Quaternion rot)? _preTvCam;

        /// <summary>Gooby turns to the camera (the mirror: he looks at himself, i.e. at the player).</summary>
        public void FaceCamera()
        {
            if (Gooby == null || Camera == null) return;
            FaceTowards(Camera.transform.position);
        }

        /// <summary>Walks Gooby to a floor point along the walkable grid (gives up after a while); then runs <paramref name="arrived"/>.</summary>
        public void WalkGoobyTo(Vector3 floorPos, Action arrived, float timeout = 6f)
        {
            if (Gooby == null) { arrived?.Invoke(); return; }
            _walkTarget = new Vector3(floorPos.x, 0f, floorPos.z);
            _walkTimeout = timeout;
            _arrived = arrived;
        }

        /// <summary>The player took the stick: the walk and any seated routine end.</summary>
        public void CancelAutoWalk()
        {
            if (_walkTarget == null) return;
            _walkTarget = null;
            _arrived = null;
            _move = Vector2.zero;
        }

        private void TickAutoWalk(float dt)
        {
            if (_walkTarget == null || Gooby == null) return;
            var d = _walkTarget.Value - Gooby.position;
            d.y = 0f;
            _walkTimeout -= dt;
            if (d.magnitude < 0.12f || _walkTimeout <= 0f)
            {
                _walkTarget = null;
                _move = Vector2.zero;
                var done = _arrived;
                _arrived = null;
                done?.Invoke();
                return;
            }
            var n = d.normalized * Mathf.Clamp01(d.magnitude / 0.35f + 0.35f);
            _move = new Vector2(n.x, -n.z);
        }

        /// <summary>A walkable floor point in front of the piece (the side it faces), nearest to where Gooby stands.</summary>
        public Vector3 ApproachPoint(FurnitureMarker m, float distance = 0.45f) => ApproachPoint(m.Bounds, m.Front, distance);

        /// <summary>A walkable floor point on the <paramref name="front"/> side of bounds <paramref name="b"/> (nearest free cell).</summary>
        public Vector3 ApproachPoint(Bounds b, Vector3 front, float distance = 0.45f)
        {
            var reach = Mathf.Abs(front.z) > 0.5f ? b.extents.z : b.extents.x;
            var want = new Vector3(b.center.x, 0f, b.center.z) + front * (reach + distance);
            var grid = _walkGrid ?? (_walkGrid = HomeState.LoadRoomGrid(HouseStore, RoomId));
            var origin = WalkOrigin;
            Cell CellOf(Vector3 p) => new Cell(Mathf.FloorToInt((p.x - origin.x) / 0.5f), Mathf.FloorToInt((origin.z + grid.Height * 0.5f - p.z) / 0.5f));
            Vector3 CenterOf(Cell c) => new Vector3(origin.x + (c.X + 0.5f) * 0.5f, 0f, origin.z + grid.Height * 0.5f - (c.Y + 0.5f) * 0.5f);
            var start = CellOf(want);
            if (grid.InBounds(start) && grid.Walkable(start)) return want;
            var best = want;
            var bestD = float.MaxValue;
            for (var dy = -3; dy <= 3; dy++)
                for (var dx = -3; dx <= 3; dx++)
                {
                    var c = new Cell(start.X + dx, start.Y + dy);
                    if (!grid.InBounds(c) || !grid.Walkable(c)) continue;
                    var p = CenterOf(c);
                    var score = (p - want).sqrMagnitude;
                    if (score < bestD) { bestD = score; best = p; }
                }
            return best;
        }

        private bool TapFurniture(RaycastHit hit)
        {
            if (InGarden || Room == null) return false;
            var marker = hit.collider.GetComponent<FurnitureMarker>();
            if (marker == null) return false;
            // a second tap on the running shower rinses, on the TV zaps
            if (Routine == Interactables.Dusche && _routineAt == marker) { FinishShower(); return true; }
            if (Routine == Interactables.Fernseher && _routineAt == marker) { Tv?.Zap(); Sfx.PlayUi("soft_tick"); return true; }
            FurnitureTapped?.Invoke(marker, hit.point);
            return true;
        }

        private void SetRoutine(string kind, FurnitureMarker at)
        {
            Routine = kind;
            _routineAt = at;
            _routineT = 0f;
            _peeked = false;
            RoutineChanged?.Invoke(kind);
        }

        private void ShowGooby(bool on)
        {
            if (Gooby != null) Gooby.gameObject.SetActive(on);
        }

        // ── bathroom ──

        public void StartShower(FurnitureMarker m)
        {
            if (Routine != RoutineNone || m == null) return;
            SetRoutine(Interactables.Dusche, m);
            BadState.SetShowerStarted(_gs, _gs.Clock.NowMs());
            WalkGoobyTo(ApproachPoint(m), () =>
            {
                if (Routine != Interactables.Dusche) return;
                ShowGooby(false);
                BuildCurtain(m, silhouette: true);
                RoutineSays?.Invoke("bad.dusche.start");
                _steamIn = 0f;
                _soundIn = 0f;
            });
        }

        public void FinishShower()
        {
            if (Routine != Interactables.Dusche) return;
            var at = _routineAt;
            ClearCurtain();
            ShowGooby(true);
            if (at != null) PlaceGoobyAt(ApproachPoint(at), "idle-loop");
            Avatar?.Play("hop");
            var gained = BadState.MarkWashed(_gs);
            Effects.Sparkle((Gooby != null ? Gooby.position : at.Bounds.center) + Vector3.up * 0.6f, 0.45f, ReducedMotion ? 8 : 26);
            Sfx.Play("soft_sticker");
            SetRoutine(RoutineNone, null);
            RoutineSays?.Invoke("bad.dusche.fertig");
            RoutineDone?.Invoke(Interactables.Dusche, gained);
            Debug.Log("[interact] shower done hygiene+" + gained.ToString("0"));
        }

        public void StartKlo(FurnitureMarker m)
        {
            if (Routine != RoutineNone || m == null) return;
            SetRoutine(Interactables.Klo, m);
            BadState.EnsureKloTimer(_gs, _gs.Clock.NowMs());
            WalkGoobyTo(ApproachPoint(m), () =>
            {
                if (Routine != Interactables.Klo) return;
                ShowGooby(false);
                BuildCurtain(m, silhouette: false);
                RoutineSays?.Invoke("bad.klo.gang");
            });
        }

        private void FinishKlo()
        {
            var at = _routineAt;
            ClearCurtain();
            ShowGooby(true);
            if (at != null) PlaceGoobyAt(ApproachPoint(at), "idle-loop");
            Avatar?.Play("hop");
            Sfx.Play("care_spuelung");
            BadState.MarkKloDone(_gs, _gs.Clock.NowMs());
            SetRoutine(RoutineNone, null);
            RoutineSays?.Invoke("bad.klo.fertig");
            RoutineDone?.Invoke(Interactables.Klo, 0);
        }

        /// <summary>Gooby steps to the sink and holds the brush (brush_teeth-loop); the HUD runs the rubbing and ends it.</summary>
        public void StartBrushing(FurnitureMarker m)
        {
            if (Routine != RoutineNone || m == null) return;
            SetRoutine(Interactables.Zahnputz, m);
            WalkGoobyTo(ApproachPoint(m, 0.32f), () =>
            {
                if (Routine != Interactables.Zahnputz || Gooby == null) return;
                FaceTowards(m.Bounds.center);
                Avatar?.Play("brush_teeth-loop", 0.15f);
                RoutineSays?.Invoke("bad.zahnputz.rubbel");
            });
        }

        /// <summary>One rubbing stroke: a little foam at Gooby's mouth and the brushing sound.</summary>
        public void BrushStroke()
        {
            if (Routine != Interactables.Zahnputz || Gooby == null) return;
            if (!ReducedMotion) Effects.Puff(Gooby.position + Vector3.up * 0.62f + Gooby.forward * 0.25f, Color.white, 3, 0.07f, 0.4f, 0.5f, Vector3.up, 0.8f);
            Sfx.Play("care_buersten", UnityEngine.Random.Range(-1f, 1f), 0.8f);
        }

        public void FinishBrushing(bool broke)
        {
            if (Routine != Interactables.Zahnputz) return;
            var gained = BadState.MarkBrushed(_gs, broke);
            // zahnputz.gd: fresh teeth show as a buff for three hours
            if (!broke && _gs != null) global::Gooby.Core.Life.GoobyBuffs.Grant(_gs, global::Gooby.Core.Life.GoobyBuffs.FrischeZaehne, "hygiene", 10.0, 3.0, _gs.Clock.NowMs());
            if (Gooby != null) Effects.Sparkle(Gooby.position + Vector3.up * 0.7f, 0.35f, ReducedMotion ? 6 : 20);
            Avatar?.Play("celebrate", 0.12f);
            Sfx.Play("soft_sticker");
            SetRoutine(RoutineNone, null);
            RoutineSays?.Invoke(broke ? "bad.zahnputz.bruch" : "bad.zahnputz.fertig");
            RoutineDone?.Invoke(Interactables.Zahnputz, gained);
        }

        // ── TV and seats ──

        public void StartTv(FurnitureMarker m, Func<string, string> t)
        {
            if (Routine != RoutineNone || m == null) return;
            _tvFunTicks = 0;
            _funIn = 8f;
            Tv?.Destroy();
            _glanceTv?.Destroy();
            _glanceTv = null;
            var tomorrow = DateTimeOffset.FromUnixTimeMilliseconds(_gs.Clock.NowMs() + 86_400_000L).ToLocalTime().ToString("yyyy-MM-dd");
            Tv = new GobtyTv(m, Room.Root.transform, t, tomorrow, (int)(_gs.Clock.NowMs() / 3_600_000L) % GobtyTv.Clips.Length);
            SetRoutine(Interactables.Fernseher, m);
            Sfx.PlayUi("soft_toggle");
            WalkGoobyTo(ApproachPoint(m, 1.35f), () =>
            {
                if (Routine != Interactables.Fernseher || Gooby == null) return;
                HaltWalk();
                FaceTowards(m.Bounds.center);
                Avatar?.Play("sit-loop", 0.2f);
                Avatar?.SetEmotion("happy");
                // over Gooby's shoulder onto the screen; StopTv glides back
                _preTvCam = (Camera.transform.position, Camera.transform.rotation);
                var screen = m.Bounds.center + m.Front * 0.1f + Vector3.up * m.Bounds.size.y * 0.1f;
                var eye = Gooby.position + m.Front * 1.05f + Vector3.up * 1.15f + Vector3.Cross(Vector3.up, m.Front) * 0.35f;
                GlideCamera(eye, Quaternion.LookRotation(screen - eye, Vector3.up), 1.1f);
            });
        }

        public void StopTv()
        {
            if (Routine != Interactables.Fernseher) return;
            Tv?.Destroy();
            Tv = null;
            if (_preTvCam.HasValue) { GlideCamera(_preTvCam.Value.pos, _preTvCam.Value.rot, 0.8f); _preTvCam = null; }
            Avatar?.Play("idle-loop", 0.2f);
            Sfx.PlayUi("soft_toggle");
            SetRoutine(RoutineNone, null);
            RoutineSays?.Invoke("gobty.aus");
            RoutineDone?.Invoke(Interactables.Fernseher, _tvFunTicks * 3);
        }

        public void StartSitting(FurnitureMarker m)
        {
            if (Routine != RoutineNone || m == null) return;
            SetRoutine(Interactables.Sitzen, m);
            WalkGoobyTo(ApproachPoint(m, 0.2f), () =>
            {
                if (Routine != Interactables.Sitzen || Gooby == null) return;
                HaltWalk();
                Gooby.position = new Vector3(m.Bounds.center.x, 0f, m.Bounds.center.z) + m.Front * 0.05f;
                _goobyYaw = Mathf.Atan2(m.Front.x, m.Front.z) * Mathf.Rad2Deg;
                ApplyWalkPose();
                Gooby.position += Vector3.up * Mathf.Clamp(m.Bounds.size.y * 0.42f, 0.2f, 0.45f);
                Avatar?.Play("sit-loop", 0.2f);
            });
        }

        /// <summary>Gets up from a seat or the TV spot (the stick also ends it).</summary>
        public void StandUp()
        {
            if (Routine == Interactables.Fernseher) { StopTv(); return; }
            if (Routine != Interactables.Sitzen) return;
            var at = _routineAt;
            SetRoutine(RoutineNone, null);
            if (at != null) PlaceGoobyAt(ApproachPoint(at), "idle-loop");
        }

        // ── lamps ──

        public bool ToggleLamp(FurnitureMarker m)
        {
            if (m == null || Room == null) return false;
            var on = !BadState.LightOn(_gs, m.Uid);
            BadState.SetLightOn(_gs, m.Uid, on);
            Room.ApplyLamps(DayPhase.LampsOn(LocalHour));
            Sfx.PlayUi("soft_toggle");
            return on;
        }

        // ── shared bits ──

        private void FaceTowards(Vector3 p)
        {
            if (Gooby == null) return;
            var d = p - Gooby.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            _goobyYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            ApplyWalkPose();
        }

        private void BuildCurtain(FurnitureMarker m, bool silhouette)
        {
            ClearCurtain();
            var b = m.Bounds;
            var front = m.Front;
            var across = Mathf.Abs(front.z) > 0.5f ? b.size.x : b.size.z;
            var reach = Mathf.Abs(front.z) > 0.5f ? b.extents.z : b.extents.x;
            _curtain = new GameObject("Curtain");
            _curtain.transform.SetParent(Room.Root.transform, false);
            var center = new Vector3(b.center.x, 0f, b.center.z) + front * (reach + 0.08f);
            _curtain.transform.SetPositionAndRotation(center, Quaternion.LookRotation(front));
            var cloth = ArtLibrary.Transparent(new Color(0.8f, 0.91f, 0.96f, 0.86f));
            var w = Mathf.Max(0.9f, across);
            // a curtain of soft folds on a rod
            for (var i = 0; i < 7; i++)
            {
                var x = -w * 0.5f + (i + 0.5f) * w / 7f;
                var fold = ArtLibrary.Box(_curtain.transform, "Fold", _curtain.transform.TransformPoint(new Vector3(x, 0.92f, (i % 2) * 0.03f)), new Vector3(w / 7f + 0.02f, 1.62f, 0.025f), cloth, false);
                fold.transform.SetParent(_curtain.transform, true);
            }
            var rod = ArtLibrary.Cylinder(_curtain.transform, "Rod", _curtain.transform.TransformPoint(new Vector3(0f, 1.78f, 0.01f)), 0.018f, w + 0.1f, ArtLibrary.Solid(ArtLibrary.Hex("#C9D0DC"), 0.6f), false);
            rod.transform.SetParent(_curtain.transform, true);
            rod.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            _silhouette.Clear();
            if (!silhouette) return;
            var shadow = ArtLibrary.Transparent(new Color(0.22f, 0.17f, 0.16f, 0.5f));
            foreach (var (n, p, s) in new[]
            {
                ("Body", new Vector3(0f, 0.5f, -0.18f), new Vector3(0.5f, 0.6f, 0.08f)),
                ("Head", new Vector3(0f, 0.98f, -0.18f), new Vector3(0.44f, 0.4f, 0.08f)),
                ("EarL", new Vector3(-0.16f, 1.28f, -0.18f), new Vector3(0.1f, 0.38f, 0.06f)),
                ("EarR", new Vector3(0.16f, 1.28f, -0.18f), new Vector3(0.1f, 0.38f, 0.06f)),
            })
            {
                var part = ArtLibrary.Sphere(_curtain.transform, n, _curtain.transform.TransformPoint(p), 0.5f, shadow, false);
                part.transform.SetParent(_curtain.transform, true);
                part.transform.localScale = s;
                _silhouette.Add(part.transform);
            }
        }

        private void ClearCurtain()
        {
            if (_curtain != null) UnityEngine.Object.Destroy(_curtain);
            _curtain = null;
            _silhouette.Clear();
        }

        private bool _houseStylePending;
        private float _houseStyleIn;

        /// <summary>Re-applies the cottage's "Gestalten" colours (now, or once its model has loaded).</summary>
        public void ApplyHouseStyle() => _houseStylePending = true;

        private void TickInteract(float dt)
        {
            TickAutoWalk(dt);
            if (_houseStylePending && _garden != null)
            {
                _houseStyleIn -= dt;
                if (_houseStyleIn <= 0f)
                {
                    _houseStyleIn = 0.5f;
                    _garden.ApplyAussen(HouseStore);
                    if (_garden.ApplyHouseStyle(HouseStyle.Haus(HouseStore))) _houseStylePending = false;
                }
            }
            _lampCheckIn -= dt;
            if (_lampCheckIn <= 0f && Room != null)
            {
                _lampCheckIn = 20f;
                Room.ApplyLamps(DayPhase.LampsOn(LocalHour));
            }
            TickNougatCamera(dt);
            if (Routine == RoutineNone || _walkTarget != null) return;
            _routineT += dt;
            switch (Routine)
            {
                case Interactables.Nougat:
                    TickNougat();
                    break;
                case Interactables.Dusche:
                    _steamIn -= dt;
                    _soundIn -= dt;
                    if (_curtain != null && _steamIn <= 0f && !ReducedMotion)
                    {
                        _steamIn = 0.35f;
                        Effects.Puff(_curtain.transform.TransformPoint(new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 1.75f, -0.2f)), new Color(1f, 1f, 1f, 0.5f), 3, 0.3f, 0.35f, 1.4f, Vector3.up, 0.6f);
                    }
                    if (_soundIn <= 0f) { _soundIn = 2.6f; Sfx.Play("care_wasser", 0f, 0.7f); }
                    for (var i = 0; i < _silhouette.Count; i++)
                        if (_silhouette[i] != null && !ReducedMotion)
                            _silhouette[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_routineT * 3.4f + i * 0.3f) * 7f);
                    if (!_peeked && _routineT > 12f) { _peeked = true; RoutineSays?.Invoke("bad.dusche.peek" + UnityEngine.Random.Range(1, 4)); }
                    if (_routineT > 40f) FinishShower();
                    break;
                case Interactables.Klo:
                    if (_routineT > 3f) FinishKlo();
                    break;
                case Interactables.Fernseher:
                    Tv?.Tick(dt);
                    _funIn -= dt;
                    if (_funIn <= 0f && _tvFunTicks < 6)
                    {
                        _funIn = 8f;
                        _tvFunTicks++;
                        _gs.Update(s =>
                        {
                            var gooby = global::Gooby.Core.Json.J.EnsureDict(s, "gooby");
                            gooby["stats"] = global::Gooby.Core.Life.Stats.ApplyDeltas(global::Gooby.Core.Json.J.Dict(gooby["stats"]), new Dictionary<string, double> { { "fun", 3.0 } });
                        });
                        _gs.NotifySliceChanged("gooby");
                    }
                    break;
            }
        }

        /// <summary>
        /// The care sheet's "wash": the shower or bath of the staged room if it has one, else a quick wash on the spot
        /// (a little less hygiene, bubbles and a hop). False while another routine runs.
        /// </summary>
        public bool WashHere()
        {
            if (Routine != RoutineNone || _gs == null) return false;
            if (Room != null)
                foreach (var m in Room.Markers())
                    if (m != null && m.Kind == Interactables.Dusche) { StartShower(m); return true; }
            var gained = BadState.MarkWashed(_gs, 15.0);
            if (Gooby != null && !ReducedMotion)
                for (var i = 0; i < 3; i++)
                    Effects.Puff(Gooby.position + Vector3.up * (0.4f + i * 0.2f), new Color(0.85f, 0.95f, 1f, 0.8f), 6, 0.12f, 0.6f, 0.9f, Vector3.up, 1f);
            Bounce();
            Sfx.Play("care_wasser");
            RoutineSays?.Invoke("bad.dusche.fertig");
            RoutineDone?.Invoke(Interactables.Dusche, gained);
            return true;
        }

        /// <summary>QA: taps the first piece of a kind in the staged room as if the player did.</summary>
        public bool QaTapFurniture(string kind)
        {
            if (Room == null) return false;
            foreach (var m in Room.Markers())
                if (m != null && m.Kind == kind) { FurnitureTapped?.Invoke(m, m.Bounds.center); return true; }
            return false;
        }

        /// <summary>Leaving the room (door, garden, a place) ends whatever Gooby was doing.</summary>
        public void EndRoutine()
        {
            _walkTarget = null;
            _arrived = null;
            _preNougatCam = null;
            if (Routine == RoutineNone) return;
            if (Routine == Interactables.Nougat) AbortNougat();
            Tv?.Destroy();
            Tv = null;
            _preTvCam = null;
            _glideT = -1f;
            ClearCurtain();
            ShowGooby(true);
            SetRoutine(RoutineNone, null);
        }
    }
}
