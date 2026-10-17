using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The ball in the living room (port of ball.gd over <see cref="BallLogic"/>): it lies on a free spot right of the
    /// room's middle; press on it and flick to throw — it arcs, bounces and rolls; in reach Gooby trots over, nudges it
    /// home with his head and hearts pop (+3 fun). The HUD forwards press / drag / release on the world here.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private const float BallGrabRadius = 0.35f;

        private readonly BallLogic _ballLogic = new BallLogic();
        private GameObject _ball;
        private Transform _ballMesh;
        private Vector3 _ballSpot;
        private bool _ballHeld;
        private readonly List<(Vector2 pos, float t)> _flick = new List<(Vector2, float)>();
        private readonly System.Random _ballRng = new System.Random();

        /// <summary>A fetch: (fun gained, line key).</summary>
        public event System.Action<double, string> BallFetched;

        public bool BallActive => _ball != null && _ball.activeSelf;

        /// <summary>The ball is held, flying, rolling or being fetched.</summary>
        public bool BallInPlay => BallActive && (_ballHeld || _ballLogic.Zustand != BallLogic.Ruht);

        private void EnsureBall()
        {
            var want = RoomId == "living" && Room != null && !Visiting && Build == null;
            if (!want)
            {
                if (_ball != null) _ball.SetActive(false);
                _ballHeld = false;
                return;
            }
            if (_ball == null)
            {
                _ball = new GameObject("Ball");
                var col = _ball.AddComponent<SphereCollider>();
                col.radius = BallGrabRadius;
                var body = ArtLibrary.Sphere(_ball.transform, "BallBody", Vector3.zero, (float)BallLogic.Radius, ArtLibrary.Solid(ArtLibrary.Hex("#FF8B8B"), 0.45f));
                body.transform.SetParent(_ball.transform, false);
                body.transform.localPosition = Vector3.zero;
                var band = ArtLibrary.Sphere(body.transform, "Band", Vector3.zero, (float)BallLogic.Radius, ArtLibrary.Solid(ArtLibrary.Hex("#FFF8EE"), 0.45f));
                band.transform.SetParent(body.transform, false);
                band.transform.localPosition = Vector3.zero;
                band.transform.localScale = new Vector3(1.03f, 0.32f, 1.03f);
                _ballMesh = body.transform;
            }
            if (!_ball.activeSelf || _ball.transform.parent != Room.Root.transform)
            {
                _ball.transform.SetParent(Room.Root.transform, true);
                _ballSpot = BallSpot();
                _ballLogic.Reset();
                _ball.SetActive(true);
                PlaceBall();
            }
        }

        /// <summary>A free floor cell near 62 % / 66 % of the room (right of the middle, in front of the camera).</summary>
        private Vector3 BallSpot()
        {
            var grid = _walkGrid ?? (_walkGrid = HomeState.LoadRoomGrid(HouseStore, RoomId));
            var origin = WalkOrigin;
            var want = new Cell(Mathf.RoundToInt(grid.Width * 0.62f), Mathf.RoundToInt(grid.Height * 0.66f));
            var best = want;
            var bestD = int.MaxValue;
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    var c = new Cell(want.X + dx, want.Y + dy);
                    if (!grid.InBounds(c) || !grid.Walkable(c)) continue;
                    var d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = c; }
                }
            return new Vector3(origin.x + (best.X + 0.5f) * 0.5f, 0f, origin.z + grid.Height * 0.5f - (best.Y + 0.5f) * 0.5f);
        }

        private void PlaceBall()
        {
            if (_ball == null) return;
            _ball.transform.position = _ballSpot + new Vector3((float)_ballLogic.X, (float)_ballLogic.Y, (float)_ballLogic.Z);
        }

        /// <summary>Press on the world: grabs the ball when the ray meets it (the HUD then streams the drag).</summary>
        public bool PointerDownWorld(Vector2 screenPos)
        {
            if (!BallActive || Camera == null || _ballLogic.Zustand != BallLogic.Ruht) return false;
            var ray = Camera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) || hit.collider.gameObject != _ball) return false;
            _ballHeld = true;
            _flick.Clear();
            _flick.Add((screenPos, Time.unscaledTime));
            return true;
        }

        public void PointerDragWorld(Vector2 screenPos)
        {
            if (!_ballHeld) return;
            _flick.Add((screenPos, Time.unscaledTime));
            if (_flick.Count > 6) _flick.RemoveAt(0);
        }

        /// <summary>Release after a grab: the last samples give the flick (px/s, screen y down). False when nothing was held.</summary>
        public bool PointerUpWorld(Vector2 screenPos)
        {
            if (!_ballHeld) return false;
            _ballHeld = false;
            _flick.Add((screenPos, Time.unscaledTime));
            var first = _flick[Mathf.Max(0, _flick.Count - 4)];
            var last = _flick[_flick.Count - 1];
            var dt = Mathf.Max(0.016f, last.t - first.t);
            var v = (last.pos - first.pos) / dt;
            // Input.mousePosition has y up; the logic wants screen y down
            if (v.magnitude < 80f) return true;
            if (_ballLogic.Werfen(v.x, -v.y))
            {
                Sfx.Play("game_whoosh", Random.Range(-1f, 1f), 0.6f);
                Debug.Log("[ball] throw " + v.x.ToString("0") + "," + (-v.y).ToString("0"));
            }
            return true;
        }

        /// <summary>QA: a throw as if flicked up and a little right.</summary>
        public bool QaThrowBall() => BallActive && _ballLogic.Werfen(260, -720);

        private void TickBall(float dt)
        {
            EnsureBall();
            if (!BallActive) return;
            var state = _ballLogic.Zustand;
            if (state != BallLogic.Fliegt && state != BallLogic.BringtZurueck) return;
            var before = _ball.transform.position;
            var (bounced, resting) = _ballLogic.Step(dt);
            PlaceBall();
            var moved = _ball.transform.position - before;
            if (_ballMesh != null && moved.sqrMagnitude > 1e-6f && !ReducedMotion)
                _ballMesh.Rotate(Vector3.Cross(Vector3.up, moved.normalized), moved.magnitude / (float)BallLogic.Radius * Mathf.Rad2Deg, Space.World);
            if (bounced) Sfx.Play("game_pop", Random.Range(-3f, 0f), 0.45f);
            if (!resting) return;
            var dist = Gooby != null ? Vector2.Distance(new Vector2(Gooby.position.x, Gooby.position.z), new Vector2(_ball.transform.position.x, _ball.transform.position.z)) : 99f;
            var landing = _ballLogic.LandungVerarbeiten(dist, _gs.Clock.NowMs());
            if (landing != BallLogic.LandungApport) return;
            if (Routine != RoutineNone || Gooby == null) { _ballLogic.ApportAbgebrochen(); return; }
            var target = _ball.transform.position;
            WalkGoobyTo(new Vector3(target.x, 0f, target.z) - (target - Gooby.position).normalized * 0.25f, () =>
            {
                if (_ballLogic.Zustand != BallLogic.GoobyHolt) return;
                FaceTowards(_ball.transform.position);
                Avatar?.Play("hop", 0.1f);
                _ballLogic.Kopfstoss(_gs.Clock.NowMs());
                var gained = 0.0;
                _gs.Update(s => gained = BallLogic.ApplyApportReward(s));
                _gs.NotifySliceChanged("gooby");
                _gs.NotifySliceChanged("achievements");
                for (var i = 0; i < 3; i++) Effects.Sparkle(Gooby.position + Vector3.up * (0.7f + i * 0.12f), 0.2f, ReducedMotion ? 3 : 8);
                Sfx.Play("soft_collect");
                BallFetched?.Invoke(gained, BallLogic.SpruchKeys[_ballRng.Next(BallLogic.SpruchKeys.Length)]);
                Debug.Log("[ball] fetched fun+" + gained.ToString("0"));
            }, 4f);
        }
    }
}
