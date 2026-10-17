using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The walkable Funkelpark (port of funkelpark.gd + ferris_wheel.gd, karussell.gd, coaster_ride.gd, autoscooter.gd):
    /// entrance gate with the ticket booth, a plaza with paths, benches and balloons, the turning Riesenrad, the
    /// Funkel-Karussell, the Funkel-Looping roller coaster on a generated track, the Autoscooter arena and the
    /// Naschgasse stalls; visitor Goobys stroll, string lights glow after dark. Staged far from the world at
    /// <see cref="Origin"/>. Gooby walks with the stick; a ride takes him along (camera work per ride) and reports
    /// captions and its end. Plain view state — prices, energy and counters are booked by the screen (Core ParkLogic).
    /// </summary>
    public sealed class FunkelparkScene
    {
        public static readonly Vector3 Origin = new Vector3(400f, 0f, 0f);
        /// <summary>The walkable box: the camera trails 8 m behind Gooby, so the south edge keeps it inside the gate.</summary>
        public const float WalkMinX = -17.5f, WalkMaxX = 17.5f, WalkMinZ = -6.5f, WalkMaxZ = 13.6f;
        public static readonly Vector3 Entrance = new Vector3(0f, 0f, -5f);

        public GameObject Root { get; }
        public string ActiveRide { get; private set; } = "";
        public bool Riding => ActiveRide.Length > 0;
        /// <summary>Bumps in the current Autoscooter ride.</summary>
        public int Bumps { get; private set; }

        /// <summary>A caption key for the HUD (park.coaster.lift, park.wheel.apex …).</summary>
        public event Action<string> Caption;
        /// <summary>A ride ended (rideId); Gooby stands on the plaza again.</summary>
        public event Action<string> RideEnded;
        /// <summary>A bumper-car bump (for sound, haptics and the counter).</summary>
        public event Action Bumped;

        private readonly Transform _t;
        private readonly List<(Vector3 c, float r)> _blockers = new List<(Vector3, float)>();
        private float _time;

        public FunkelparkScene(Transform parent)
        {
            Root = new GameObject("Funkelpark");
            Root.transform.SetParent(parent, false);
            _t = Root.transform;
            BuildGround();
            BuildEntrance();
            BuildWheel();
            BuildCarousel();
            BuildCoaster();
            BuildScooter();
            BuildAlley();
            BuildDressing();
            ArtLibrary.Horizon(_t, "bg_town_horizon", Origin + new Vector3(0f, -4f, 70f), 46f);
            _ = SpawnVisitorsAsync();
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }

        private Vector3 W(Vector3 local) => Origin + local;
        private Vector3 W(float x, float y, float z) => Origin + new Vector3(x, y, z);

        // ── ground, paths, entrance ───────────────────────────────────────────

        private void BuildGround()
        {
            var lawn = ArtLibrary.PbrSlab("grass", new Vector2(120f, 120f), 2.4f, ArtLibrary.Hex("#8FC48E"), 0.04f, 0.7f);
            ArtLibrary.Box(_t, "Lawn", W(0f, -0.1f, 8f), new Vector3(120f, 0.2f, 120f), lawn, false);
            var paving = ArtLibrary.Hex("#E9D7BC");
            ArtLibrary.Box(_t, "PathNS", W(0f, 0.005f, 3f), new Vector3(3.2f, 0.02f, 28f), ArtLibrary.PbrSlab("paving", new Vector2(3.2f, 28f), 1.6f, paving), false);
            ArtLibrary.Box(_t, "PathEW", W(0f, 0.006f, 0f), new Vector3(30f, 0.02f, 3.2f), ArtLibrary.PbrSlab("paving", new Vector2(30f, 3.2f), 1.6f, paving), false);
            ArtLibrary.Cylinder(_t, "Plaza", W(0f, 0.008f, 0f), 6.2f, 0.02f, ArtLibrary.PbrSlab("paving", new Vector2(12.4f, 12.4f), 1.6f, ArtLibrary.Hex("#F1E1C8")), false);
            // the fountain bed in the middle of the plaza
            ArtLibrary.Cylinder(_t, "FountainRim", W(0f, 0.25f, 0f), 1.9f, 0.5f, ArtLibrary.Solid(ArtLibrary.Hex("#FFF3E2"), 0.2f));
            ArtLibrary.Cylinder(_t, "FountainWater", W(0f, 0.46f, 0f), 1.65f, 0.1f, ArtLibrary.Solid(ArtLibrary.Hex("#9BD7E8"), 0.85f), false);
            ArtLibrary.Cylinder(_t, "FountainPillar", W(0f, 0.9f, 0f), 0.28f, 1.1f, ArtLibrary.Solid(ArtLibrary.Hex("#F781B0"), 0.3f));
            ArtLibrary.Sphere(_t, "FountainTop", W(0f, 1.55f, 0f), 0.36f, ArtLibrary.Solid(ArtLibrary.Hex("#FFD166"), 0.4f));
            _blockers.Add((W(0f, 0f, 0f), 2.3f));
        }

        private void BuildEntrance()
        {
            Scenery.Place(_t, "park_gate", W(0f, 0f, -16f), 180f, 1.15f);
            Scenery.Place(_t, "ticket_booth", W(-4.8f, 0f, -14.6f), 200f);
            Scenery.Place(_t, "welcome_sign", W(4.8f, 0f, -14.8f), 165f, 0.9f);
            Balloons(W(2.4f, 0f, -7.8f), 7);
            Balloons(W(-2.4f, 0f, -7.8f), 6);
        }

        private void Balloons(Vector3 at, int n)
        {
            var colors = new[] { "#F781B0", "#FFD166", "#9BD7E8", "#B58CE4", "#8FD06C", "#FF9E7D", "#FFF8EE" };
            var anchor = new GameObject("Balloons").transform;
            anchor.SetParent(_t, false);
            anchor.position = at;
            var wire = ArtLibrary.Solid(ArtLibrary.Hex("#6B645C"), 0.1f);
            for (var i = 0; i < n; i++)
            {
                var a = i * 2.39996f;
                var top = new Vector3(Mathf.Cos(a) * 0.35f, 2.3f + (i % 3) * 0.28f, Mathf.Sin(a) * 0.35f);
                var s = ArtLibrary.Cylinder(anchor, "String", top * 0.5f, 0.008f, top.magnitude, wire, false);
                s.transform.localPosition = top * 0.5f;
                s.transform.localRotation = Quaternion.FromToRotation(Vector3.up, top);
                var b = ArtLibrary.Sphere(anchor, "Balloon", Vector3.zero, 0.24f, ArtLibrary.Solid(ArtLibrary.Hex(colors[i % colors.Length]), 0.55f), false);
                b.transform.localPosition = top + Vector3.up * 0.2f;
                b.transform.localScale = new Vector3(0.44f, 0.54f, 0.44f);
            }
            _balloons.Add(anchor);
        }

        private readonly List<Transform> _balloons = new List<Transform>();

        // ── Riesenrad ─────────────────────────────────────────────────────────

        private static readonly Vector3 WheelBase = new Vector3(-10f, 0f, 9.5f);
        private const float WheelHub = 6.0f, WheelRadius = 4.6f;
        private Transform _rotor;
        private readonly Transform[] _gondolas = new Transform[8];
        private float _wheelAngle = -112.5f;

        private void BuildWheel()
        {
            Scenery.Place(_t, "ferris_stand", W(WheelBase), 0f);
            _rotor = Scenery.Place(_t, "ferris_rotor", W(WheelBase + Vector3.up * WheelHub), 0f).transform;
            var tints = new[] { "a", "b", "c", "d" };
            for (var i = 0; i < 8; i++)
                _gondolas[i] = Scenery.Place(_t, "ferris_gondola_" + tints[i % 4], W(WheelBase), 0f).transform;
            PlaceGondolas();
            _blockers.Add((W(WheelBase), 3.4f));
        }

        private Vector3 GondolaPin(int i)
        {
            var a = (22.5f + 45f * i) * Mathf.Deg2Rad;
            return Quaternion.Euler(0f, 0f, _wheelAngle) * new Vector3(Mathf.Cos(a) * WheelRadius, Mathf.Sin(a) * WheelRadius, 0f);
        }

        private void PlaceGondolas()
        {
            if (_rotor != null) _rotor.localRotation = Quaternion.Euler(0f, 0f, _wheelAngle);
            for (var i = 0; i < 8; i++)
                if (_gondolas[i] != null) _gondolas[i].position = W(WheelBase + Vector3.up * WheelHub) + GondolaPin(i);
        }

        // ── Karussell ─────────────────────────────────────────────────────────

        private static readonly Vector3 CarouselAt = new Vector3(10f, 0f, 8f);
        private Transform _carousel;
        private float _carouselYaw;
        private float _carouselSpeed = 26f;
        private const float CarouselSeat = 1.02f;

        private void BuildCarousel()
        {
            _carousel = Scenery.Place(_t, "carousel_small", W(CarouselAt), 0f, 1.25f).transform;
            _blockers.Add((W(CarouselAt), 3.7f));
        }

        // ── Achterbahn (generated track) ─────────────────────────────────────

        private static readonly Vector3[] TrackPoints =
        {
            new Vector3(-6f, 1.1f, 17f), new Vector3(2f, 1.1f, 17f), new Vector3(8f, 4.6f, 17.4f), new Vector3(13f, 10.5f, 19f),
            new Vector3(16f, 11.2f, 23f), new Vector3(13.5f, 3.2f, 28.5f), new Vector3(6f, 7.2f, 31f), new Vector3(-1.5f, 2.6f, 31.5f),
            new Vector3(-8.5f, 6.4f, 29.5f), new Vector3(-15f, 3.2f, 26f), new Vector3(-15.5f, 5f, 20.5f), new Vector3(-11.5f, 1.6f, 17f),
        };

        private readonly List<Vector3> _track = new List<Vector3>();
        private readonly List<float> _trackS = new List<float>();
        private float _trackLength;
        private Transform _coasterCar;
        private float _coasterS, _coasterV;
        private int _coasterPhase;
        private float _clack;
        private const float LiftStart = 8f, LiftEndFraction = 0.33f;

        private void BuildCoaster()
        {
            const int samples = 360;
            for (var i = 0; i <= samples; i++)
            {
                var u = i / (float)samples * TrackPoints.Length;
                _track.Add(W(CatmullClosed(TrackPoints, u)));
            }
            _trackS.Add(0f);
            for (var i = 1; i < _track.Count; i++) _trackS.Add(_trackS[i - 1] + Vector3.Distance(_track[i - 1], _track[i]));
            _trackLength = _trackS[_trackS.Count - 1];

            var rails = new GameObject("CoasterRails");
            rails.transform.SetParent(_t, false);
            rails.AddComponent<MeshFilter>().sharedMesh = RailMesh();
            rails.AddComponent<MeshRenderer>().sharedMaterial = ArtLibrary.Solid(ArtLibrary.Hex("#F781B0"), 0.45f);
            var tieMat = ArtLibrary.Solid(ArtLibrary.Hex("#FFF3E2"), 0.2f);
            var postMat = ArtLibrary.Solid(ArtLibrary.Hex("#FFD166"), 0.35f);
            var parts = new List<GameObject>();
            var nextTie = 0f;
            var nextPost = 2f;
            for (var s = 0f; s < _trackLength; s += 0.5f)
            {
                var (p, fwd, up) = TrackFrame(s);
                if (s >= nextTie)
                {
                    nextTie += 1.1f;
                    var tie = ArtLibrary.Box(_t, "Tie", p - up * 0.08f, new Vector3(1.15f, 0.07f, 0.16f), tieMat, false);
                    tie.transform.rotation = Quaternion.LookRotation(fwd, up);
                    parts.Add(tie);
                }
                if (s >= nextPost && p.y - Origin.y > 1.4f)
                {
                    nextPost += 4.2f;
                    var h = p.y - Origin.y - 0.1f;
                    parts.Add(ArtLibrary.Cylinder(_t, "Post", new Vector3(p.x, Origin.y + h * 0.5f, p.z), 0.09f, h, postMat, false));
                }
            }
            Combine("CoasterTies", parts, tieMat, postMat);
            // the station: a pink-roofed platform at the start of the track
            ArtLibrary.Box(_t, "StationDeck", W(-2f, 0.45f, 15.8f), new Vector3(9.5f, 0.9f, 2.2f), ArtLibrary.Solid(ArtLibrary.Hex("#FFF3E2"), 0.15f));
            ArtLibrary.Box(_t, "StationRoof", W(-2f, 3.3f, 17f), new Vector3(10.4f, 0.22f, 4.0f), ArtLibrary.Solid(ArtLibrary.Hex("#F781B0"), 0.3f));
            foreach (var x in new[] { -6.6f, 2.6f })
                ArtLibrary.Cylinder(_t, "StationPost", W(x, 1.7f, 15.3f), 0.1f, 3.3f, postMat, false);
            _coasterCar = Scenery.Place(_t, "coaster_car", _track[0], 0f).transform;
            PlaceCoasterCar(0f);
            for (var x = -16f; x <= 16f; x += 2.6f) _blockers.Add((W(x, 0f, 17.5f), 1.6f));
        }

        /// <summary>Two rails as one tube mesh along the sampled track.</summary>
        private Mesh RailMesh()
        {
            const int sides = 6;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var steps = Mathf.CeilToInt(_trackLength / 0.4f);
            foreach (var side in new[] { -0.46f, 0.46f })
            {
                var start = verts.Count;
                for (var i = 0; i <= steps; i++)
                {
                    var (p, fwd, up) = TrackFrame(i / (float)steps * _trackLength);
                    var right = Vector3.Cross(up, fwd).normalized;
                    var c = p + right * side;
                    for (var k = 0; k < sides; k++)
                    {
                        var a = k / (float)sides * Mathf.PI * 2f;
                        verts.Add(c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.075f);
                    }
                }
                for (var i = 0; i < steps; i++)
                    for (var k = 0; k < sides; k++)
                    {
                        int a = start + i * sides + k, b = start + i * sides + (k + 1) % sides;
                        int c = a + sides, d = b + sides;
                        tris.AddRange(new[] { a, c, b, b, c, d });
                    }
            }
            var mesh = new Mesh { name = "CoasterRails", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Folds many primitives into one or two meshes (one draw call per material) — the track has hundreds.</summary>
        private void Combine(string name, List<GameObject> parts, params Material[] materials)
        {
            foreach (var mat in materials)
            {
                var list = new List<CombineInstance>();
                foreach (var go in parts)
                {
                    var r = go.GetComponent<MeshRenderer>();
                    if (r == null || r.sharedMaterial != mat) continue;
                    list.Add(new CombineInstance { mesh = go.GetComponent<MeshFilter>().sharedMesh, transform = go.transform.localToWorldMatrix });
                }
                if (list.Count == 0) continue;
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(list.ToArray(), true, true);
                var go2 = new GameObject(name);
                go2.transform.SetParent(_t, false);
                go2.AddComponent<MeshFilter>().sharedMesh = mesh;
                go2.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            foreach (var go in parts) UnityEngine.Object.Destroy(go);
        }

        private static Vector3 CatmullClosed(Vector3[] pts, float u)
        {
            var n = pts.Length;
            var i = Mathf.FloorToInt(u);
            var t = u - i;
            Vector3 p0 = pts[((i - 1) % n + n) % n], p1 = pts[i % n], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Point, forward and up of the track at arc length s (wraps).</summary>
        private (Vector3 p, Vector3 fwd, Vector3 up) TrackFrame(float s)
        {
            s = Mathf.Repeat(s, _trackLength);
            var i = _trackS.BinarySearch(s);
            if (i < 0) i = Mathf.Max(0, ~i - 1);
            i = Mathf.Clamp(i, 0, _track.Count - 2);
            var seg = _trackS[i + 1] - _trackS[i];
            var f = seg > 1e-4f ? (s - _trackS[i]) / seg : 0f;
            var p = Vector3.Lerp(_track[i], _track[i + 1], f);
            var fwd = (_track[i + 1] - _track[i]).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            var up = Vector3.Cross(fwd, right.normalized).normalized;
            return (p, fwd, up);
        }

        private float TrackHeight(float s) => TrackFrame(s).p.y - Origin.y;

        private void PlaceCoasterCar(float s)
        {
            if (_coasterCar == null) return;
            var (p, fwd, up) = TrackFrame(s);
            _coasterCar.position = p;
            _coasterCar.rotation = Quaternion.LookRotation(fwd, up);
        }

        // ── Autoscooter ───────────────────────────────────────────────────────

        private static readonly Vector3 ArenaAt = new Vector3(11f, 0f, -3.5f);
        private const float ArenaRadius = 5f, CarRadius = 0.85f;

        private sealed class Bumper
        {
            public Transform T;
            public Vector2 Pos, Vel, Target;
            public float Heading, Retarget;
        }

        private readonly List<Bumper> _bumpers = new List<Bumper>();
        private Vector2 _drive;
        private float _bumpCooldown;
        private readonly System.Random _rng = new System.Random(20260808);

        private void BuildScooter()
        {
            ArtLibrary.Cylinder(_t, "ArenaFloor", W(ArenaAt + Vector3.up * 0.05f), ArenaRadius + 0.2f, 0.1f, ArtLibrary.Solid(ArtLibrary.Hex("#5B5470"), 0.55f));
            var pink = ArtLibrary.Solid(ArtLibrary.Hex("#F781B0"), 0.3f);
            var cream = ArtLibrary.Solid(ArtLibrary.Hex("#FFF3E2"), 0.2f);
            var parts = new List<GameObject>();
            for (var i = 0; i < 36; i++)
            {
                var a = i / 36f * Mathf.PI * 2f;
                var p = W(ArenaAt + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ArenaRadius + 0.35f) + Vector3.up * 0.32f);
                var b = ArtLibrary.Box(_t, "Rail", p, new Vector3(0.95f, 0.5f, 0.22f), i % 2 == 0 ? pink : cream, false);
                b.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                parts.Add(b);
            }
            // four corner posts with light strings across instead of a roof — the ride camera looks down onto the floor
            var postTops = new List<Vector3>();
            foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                parts.Add(ArtLibrary.Cylinder(_t, "Post", W(ArenaAt + new Vector3(x * 4.4f, 1.6f, z * 4.4f)), 0.1f, 3.2f, pink, false));
                postTops.Add(W(ArenaAt + new Vector3(x * 4.4f, 3.15f, z * 4.4f)));
            }
            Combine("ArenaDressing", parts, pink, cream);
            Chain(postTops[0], postTops[2], 9, 0.7f);
            Chain(postTops[1], postTops[3], 9, 0.7f);
            for (var i = 0; i < 5; i++)
            {
                var a = i / 5f * Mathf.PI * 2f;
                var pos = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.6f;
                var car = new Bumper { Pos = pos, Heading = a * Mathf.Rad2Deg + 90f, Target = pos };
                car.T = Scenery.Place(_t, "bumper_car", W(ArenaAt + new Vector3(pos.x, 0.1f, pos.y)), car.Heading).transform;
                _bumpers.Add(car);
            }
            _blockers.Add((W(ArenaAt), ArenaRadius + 0.7f));
        }

        private void TickScooter(float dt, bool playerDrives)
        {
            _bumpCooldown -= dt;
            for (var i = 0; i < _bumpers.Count; i++)
            {
                var c = _bumpers[i];
                Vector2 want;
                if (i == 0 && playerDrives)
                {
                    want = _drive.sqrMagnitude > 0.02f ? _drive.normalized * 3.2f * Mathf.Clamp01(_drive.magnitude) : Vector2.zero;
                }
                else
                {
                    c.Retarget -= dt;
                    if (c.Retarget <= 0f || (c.Target - c.Pos).sqrMagnitude < 0.6f)
                    {
                        var a = (float)_rng.NextDouble() * Mathf.PI * 2f;
                        c.Target = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (float)(_rng.NextDouble() * (ArenaRadius - 1.4f));
                        c.Retarget = 2.5f + (float)_rng.NextDouble() * 2.5f;
                    }
                    want = (c.Target - c.Pos).normalized * 2.1f;
                }
                c.Vel = Vector2.Lerp(c.Vel, want, 1f - Mathf.Exp(-2.4f * dt));
                c.Pos += c.Vel * dt;
                if (c.Pos.magnitude > ArenaRadius - CarRadius)
                {
                    var n = c.Pos.normalized;
                    c.Pos = n * (ArenaRadius - CarRadius);
                    var vn = Vector2.Dot(c.Vel, n);
                    if (vn > 0f) c.Vel -= 1.6f * vn * n;
                }
            }
            for (var i = 0; i < _bumpers.Count; i++)
                for (var j = i + 1; j < _bumpers.Count; j++)
                {
                    var a = _bumpers[i];
                    var b = _bumpers[j];
                    var d = b.Pos - a.Pos;
                    var dist = d.magnitude;
                    if (dist >= CarRadius * 2f || dist < 1e-4f) continue;
                    var n = d / dist;
                    var push = (CarRadius * 2f - dist) * 0.5f;
                    a.Pos -= n * push;
                    b.Pos += n * push;
                    var rel = Vector2.Dot(b.Vel - a.Vel, n);
                    if (rel < 0f)
                    {
                        a.Vel += n * rel;
                        b.Vel -= n * rel;
                    }
                    if (playerDrives && (i == 0 || j == 0) && _bumpCooldown <= 0f && -rel > 0.6f)
                    {
                        _bumpCooldown = 0.45f;
                        Bumps++;
                        var hit = W(ArenaAt + new Vector3((a.Pos.x + b.Pos.x) * 0.5f, 0.6f, (a.Pos.y + b.Pos.y) * 0.5f));
                        Effects.Puff(hit, new Color(1f, 0.85f, 0.4f), 8, 0.2f, 0.5f, 0.4f, Vector3.up * 1.5f, 1f);
                        Bumped?.Invoke();
                    }
                }
            foreach (var c in _bumpers)
            {
                if (c.Vel.sqrMagnitude > 0.05f) c.Heading = Mathf.LerpAngle(c.Heading, Mathf.Atan2(c.Vel.x, c.Vel.y) * Mathf.Rad2Deg, 1f - Mathf.Exp(-6f * dt));
                c.T.position = W(ArenaAt + new Vector3(c.Pos.x, 0.1f, c.Pos.y));
                c.T.rotation = Quaternion.Euler(0f, c.Heading, 0f);
            }
        }

        // ── Naschgasse, trees, lamps, lights ─────────────────────────────────

        public static readonly Vector3 AlleyAt = new Vector3(-11.5f, 0f, -3f);

        private void BuildAlley()
        {
            var stalls = new[] { "market_stall_red", "market_stall_yellow", "market_stall_green" };
            for (var i = 0; i < 3; i++)
            {
                var at = AlleyAt + new Vector3(0f, 0f, -3.2f + i * 3.2f);
                Scenery.Place(_t, stalls[i], W(at), 90f, 0.9f);
                _blockers.Add((W(at), 1.5f));
            }
            Scenery.Place(_t, "cafe_set", W(-7.2f, 0f, -5.8f), 30f);
            _blockers.Add((W(-7.2f, 0f, -5.8f), 1.2f));
        }

        private readonly List<Vector3> _bulbs = new List<Vector3>();
        private GameObject _nightLights;
        private bool _night;

        private void BuildDressing()
        {
            var trees = new[] { (-19f, -6f), (-18f, 3f), (-20f, 12f), (19f, 2f), (20f, 11f), (18f, -9f), (-8f, -15f), (8f, -15f), (-14f, -12f), (14f, -13f), (22f, 20f), (-22f, 22f) };
            for (var i = 0; i < trees.Length; i++)
            {
                var (x, z) = trees[i];
                Scenery.Place(_t, i % 3 == 0 ? "tree_round_big" : "tree_round", W(x, 0f, z), i * 47f, 1.1f + (i % 4) * 0.12f);
                _blockers.Add((W(x, 0f, z), 1.3f));
            }
            var lamps = new[] { (-4.6f, -5f), (4.6f, -5f), (-6.8f, 4.5f), (6.8f, 4.5f), (-4.6f, 9.8f), (4.6f, 9.8f) };
            var tops = new List<Vector3>();
            foreach (var (x, z) in lamps)
            {
                Scenery.Place(_t, "lamp_post_pink", W(x, 0f, z), 0f);
                _blockers.Add((W(x, 0f, z), 0.4f));
                tops.Add(W(x, 3.1f, z));
            }
            Chain(tops[0], tops[2], 9, 0.5f);
            Chain(tops[1], tops[3], 9, 0.5f);
            Chain(tops[2], tops[4], 7, 0.4f);
            Chain(tops[3], tops[5], 7, 0.4f);
            Chain(tops[0], tops[1], 11, 0.6f);
            Chain(tops[4], tops[5], 11, 0.6f);
            foreach (var (x, z, yaw) in new[] { (-3.6f, 3.4f, 135f), (3.6f, 3.4f, 225f), (-3.6f, -3.4f, 45f), (3.6f, -3.4f, -45f) })
            {
                Scenery.Place(_t, "park_bench", W(x, 0f, z), yaw);
                _blockers.Add((W(x, 0f, z), 0.9f));
            }
            foreach (var (x, z) in new[] { (-6f, -8f), (6f, -8f), (-14f, 6.5f), (15f, -10f), (-2f, 12.2f), (2.4f, 12.2f) })
                Scenery.Place(_t, "flower_bed", W(x, 0f, z), x * 13f);
            foreach (var (x, z) in new[] { (-17f, -1f), (17f, 6f), (-9f, -12.5f), (9f, -12.5f) })
                Scenery.Place(_t, "bush_flowers", W(x, 0f, z), z * 11f);
        }

        private void Chain(Vector3 from, Vector3 to, int bulbs, float sag)
        {
            var wire = ArtLibrary.Solid(ArtLibrary.Hex("#3F3A45"), 0.1f);
            var colors = new[] { new Color(2.4f, 2.0f, 1.1f), new Color(2.4f, 1.3f, 1.8f), new Color(1.2f, 2.0f, 2.4f) };
            var prev = from;
            for (var i = 1; i <= bulbs + 1; i++)
            {
                var u = i / (float)(bulbs + 1);
                var p = Vector3.Lerp(from, to, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * sag;
                var seg = ArtLibrary.Cylinder(_t, "Wire", (prev + p) * 0.5f, 0.012f, Vector3.Distance(prev, p), wire, false);
                seg.transform.rotation = Quaternion.FromToRotation(Vector3.up, p - prev);
                if (i <= bulbs)
                {
                    ArtLibrary.Sphere(_t, "Bulb", p + Vector3.down * 0.1f, 0.1f, ArtLibrary.Glow(colors[i % colors.Length]), false);
                    _bulbs.Add(p + Vector3.down * 0.1f);
                }
                prev = p;
            }
        }

        /// <summary>After dark every fourth bulb casts a warm pool (created on first use, like the garden's lights).</summary>
        public void SetNight(bool on)
        {
            if (_night == on && (_nightLights != null || !on)) return;
            _night = on;
            if (_nightLights == null && on)
            {
                _nightLights = new GameObject("NightLights");
                _nightLights.transform.SetParent(_t, false);
                for (var i = 0; i < _bulbs.Count; i += 4)
                {
                    var l = new GameObject("BulbLight").AddComponent<Light>();
                    l.transform.SetParent(_nightLights.transform, false);
                    l.transform.position = _bulbs[i];
                    l.type = LightType.Point;
                    l.range = 6f;
                    l.intensity = 1.6f;
                    l.color = new Color(1f, 0.82f, 0.6f);
                    l.shadows = LightShadows.None;
                }
            }
            if (_nightLights != null) _nightLights.SetActive(on);
        }

        // ── visitors ──────────────────────────────────────────────────────────

        private sealed class Visitor
        {
            public GoobyAvatar Avatar;
            public Vector3 Target;
            public float Wait;
            public bool Walking;
        }

        private readonly List<Visitor> _visitors = new List<Visitor>();
        private static readonly Vector3[] Spots =
        {
            new Vector3(-3f, 0f, -6f), new Vector3(3f, 0f, -6.5f), new Vector3(-5.5f, 0f, 1.5f), new Vector3(5.5f, 0f, 2f),
            new Vector3(-7.5f, 0f, 7f), new Vector3(6.5f, 0f, 6.5f), new Vector3(0f, 0f, 9.5f), new Vector3(-8.5f, 0f, -3f),
            new Vector3(7f, 0f, -3.5f), new Vector3(-1.5f, 0f, 5f),
        };

        private async System.Threading.Tasks.Task SpawnVisitorsAsync()
        {
            var looks = new[]
            {
                ("caramel", "partyHat", "balloonRed"), ("fell_minze", "strawHat", null), ("snow", "cap", null), ("fell_lavendel", null, "balloonRed"),
            };
            for (var i = 0; i < looks.Length; i++)
            {
                var start = W(Spots[(i * 3) % Spots.Length]);
                var avatar = await GoobyAvatar.CreateAsync(_t, start);
                if (avatar == null || Root == null) return;
                avatar.transform.position = start;
                avatar.transform.localScale = Vector3.one * 1.25f;
                var (fur, hat, back) = looks[i];
                var equipped = new Dictionary<string, string> { ["fell"] = fur };
                if (hat != null) equipped["hut"] = hat;
                if (back != null) equipped["ruecken"] = back;
                avatar.Wear(equipped);
                avatar.SetEmotion(i % 2 == 0 ? "happy" : "ecstatic");
                avatar.Play("idle-loop");
                _visitors.Add(new Visitor { Avatar = avatar, Target = start, Wait = 1f + i * 1.3f });
            }
        }

        private void TickVisitors(float dt)
        {
            foreach (var v in _visitors)
            {
                if (v.Avatar == null) continue;
                var tr = v.Avatar.transform;
                if (v.Wait > 0f)
                {
                    v.Wait -= dt;
                    if (v.Wait <= 0f)
                    {
                        v.Target = W(Spots[_rng.Next(Spots.Length)]);
                        v.Walking = true;
                        v.Avatar.Play("walk-loop");
                    }
                    continue;
                }
                var to = v.Target - tr.position;
                to.y = 0f;
                if (to.magnitude < 0.25f)
                {
                    v.Walking = false;
                    v.Wait = 2.5f + (float)_rng.NextDouble() * 4f;
                    v.Avatar.Play("idle-loop");
                    continue;
                }
                var step = to.normalized * Mathf.Min(to.magnitude, 1.3f * dt);
                tr.position += step;
                tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(to.normalized), 1f - Mathf.Exp(-8f * dt));
            }
        }

        // ── walking ───────────────────────────────────────────────────────────

        /// <summary>Gooby's next free spot on the park ground (inside the fence ring, around rides, trees and stalls).</summary>
        public Vector3 ClampWalk(Vector3 from, Vector3 to)
        {
            var local = to - Origin;
            local = new Vector3(Mathf.Clamp(local.x, WalkMinX, WalkMaxX), 0f, Mathf.Clamp(local.z, WalkMinZ, WalkMaxZ));
            var p = Origin + local;
            foreach (var (c, r) in _blockers)
            {
                var d = p - c;
                d.y = 0f;
                var min = r + 0.35f;
                if (d.sqrMagnitude < min * min) p = c + (d.sqrMagnitude > 1e-4f ? d.normalized : (from - c).normalized) * min;
            }
            p.y = Origin.y;
            return p;
        }

        /// <summary>The ride whose entrance is within reach of Gooby (for the contextual "Fahren" pill), "" if none.</summary>
        public string RideNear(Vector3 gooby)
        {
            var best = "";
            var bestD = 3.6f;
            void Check(string id, Vector3 entrance)
            {
                var d = Vector3.Distance(new Vector3(gooby.x, 0f, gooby.z), W(entrance));
                if (d < bestD) { bestD = d; best = id; }
            }
            Check("wheel", WheelBase + new Vector3(0f, 0f, -4.4f));
            Check("karussell", CarouselAt + new Vector3(0f, 0f, -4.4f));
            Check("coaster", new Vector3(-2f, 0f, 14.2f));
            Check("scooter", ArenaAt + new Vector3(-6.2f, 0f, 0f));
            Check("snacks", AlleyAt + new Vector3(2.4f, 0f, 0f));
            return best;
        }

        // ── rides ─────────────────────────────────────────────────────────────

        private Transform _gooby;
        private Transform _goobyParentBefore;
        private float _rideT;
        private bool _apexSaid, _dropSaid, _hillsSaid, _brakeSaid;

        /// <summary>Starts a ride with Gooby on board (wheel, karussell, coaster, scooter). False when one already runs.</summary>
        public bool StartRide(string id, Transform gooby)
        {
            if (Riding || gooby == null) return false;
            ActiveRide = id;
            _gooby = gooby;
            _goobyParentBefore = gooby.parent;
            _rideT = 0f;
            _apexSaid = _dropSaid = _hillsSaid = _brakeSaid = false;
            switch (id)
            {
                case "wheel":
                    _wheelAngle = -112.5f;
                    PlaceGondolas();
                    Seat(_gondolas[0], new Vector3(0f, -1.28f, -0.05f), 180f);
                    Caption?.Invoke("park.wheel.board");
                    break;
                case "karussell":
                    // pony 0 of carousel_small (Blender 45° at r 1.75 → model −x/−z), seat on its back, facing along the turn
                    Seat(_carousel, new Vector3(-1.24f, CarouselSeat, -1.24f), 225f);
                    Caption?.Invoke("park.karussell.beschreibung");
                    break;
                case "coaster":
                    _coasterS = 0f;
                    _coasterV = 0f;
                    _coasterPhase = 0;
                    PlaceCoasterCar(0f);
                    Seat(_coasterCar, new Vector3(0f, 0.58f, 0.12f), 0f);
                    Caption?.Invoke("park.coaster.board");
                    break;
                case "scooter":
                    Bumps = 0;
                    _drive = Vector2.zero;
                    Seat(_bumpers[0].T, new Vector3(0f, 0.42f, 0.28f), 0f);
                    Caption?.Invoke("nl.park.scooter.fahr_hint");
                    break;
                default:
                    ActiveRide = "";
                    return false;
            }
            return true;
        }

        private void Seat(Transform seat, Vector3 local, float yaw)
        {
            if (seat == null) return;
            _gooby.SetParent(seat, false);
            _gooby.localPosition = local;
            _gooby.localRotation = Quaternion.Euler(0f, yaw, 0f);
            _gooby.localScale = Vector3.one / Mathf.Max(0.01f, seat.lossyScale.x);
        }

        /// <summary>Ends the running ride now (the HUD's "Aussteigen"); Gooby is put back at the ride's entrance.</summary>
        public void StopRide()
        {
            if (!Riding) return;
            var id = ActiveRide;
            ActiveRide = "";
            if (_gooby != null)
            {
                _gooby.SetParent(_goobyParentBefore, true);
                _gooby.localScale = Vector3.one;
                _gooby.position = ClampWalk(W(Entrance), EntranceOf(id));
                _gooby.rotation = Quaternion.Euler(0f, 180f, 0f);
            }
            if (id == "coaster") PlaceCoasterCar(0f);
            RideEnded?.Invoke(id);
        }

        private Vector3 EntranceOf(string id)
        {
            switch (id)
            {
                case "wheel": return W(WheelBase + new Vector3(0f, 0f, -4.6f));
                case "karussell": return W(CarouselAt + new Vector3(0f, 0f, -4.4f));
                case "coaster": return W(-2f, 0f, 13.6f);
                case "scooter": return W(ArenaAt + new Vector3(-6.4f, 0f, 0f));
                default: return W(Entrance);
            }
        }

        /// <summary>The stick while driving a bumper car (world x/z, length ≤ 1).</summary>
        public void SetDrive(Vector2 stick) => _drive = Vector2.ClampMagnitude(stick, 1f);

        public void Tick(float dt, bool reducedMotion)
        {
            _time += dt;
            var ride = ActiveRide;
            // the wheel and the carousel keep turning slowly while nobody rides them
            var wheelSpeed = ride == "wheel" ? 360f / 26f : 6f;
            _wheelAngle += wheelSpeed * dt;
            PlaceGondolas();
            _carouselSpeed = Mathf.MoveTowards(_carouselSpeed, ride == "karussell" ? 44f : 20f, 18f * dt);
            _carouselYaw += _carouselSpeed * dt;
            if (_carousel != null) _carousel.localRotation = Quaternion.Euler(0f, _carouselYaw, 0f);
            TickScooter(dt, ride == "scooter");
            TickVisitors(dt);
            foreach (var b in _balloons)
                if (b != null) b.localRotation = Quaternion.Euler(Mathf.Sin(_time * 1.3f + b.position.x) * 4f, 0f, Mathf.Cos(_time * 1.1f) * 4f);
            if (!Riding) return;
            _rideT += dt;
            switch (ride)
            {
                case "wheel":
                    var top = (_wheelAngle + 112.5f) % 360f;
                    if (!_apexSaid && top >= 170f) { _apexSaid = true; Caption?.Invoke("park.wheel.apex"); }
                    if (_rideT >= 26f) { Caption?.Invoke("park.wheel.done"); StopRide(); }
                    break;
                case "karussell":
                    if (_gooby != null) _gooby.localPosition = new Vector3(_gooby.localPosition.x, CarouselSeat + Mathf.Sin(_rideT * 2.6f) * 0.12f, _gooby.localPosition.z);
                    if (_rideT >= 18f) { Caption?.Invoke("park.karussell.done"); StopRide(); }
                    break;
                case "coaster":
                    TickCoaster(dt);
                    break;
                case "scooter":
                    if (_rideT >= 30f) { Caption?.Invoke("nl.park.scooter.done"); StopRide(); }
                    break;
            }
        }

        private void TickCoaster(float dt)
        {
            var liftEnd = _trackLength * LiftEndFraction;
            if (_coasterPhase == 0)
            {
                // roll out of the station, then the chain lift clicks the car up
                _coasterV = Mathf.MoveTowards(_coasterV, _coasterS < LiftStart ? 3f : 2.6f, 2f * dt);
                if (_coasterS >= LiftStart && _coasterS < liftEnd)
                {
                    _clack -= dt;
                    if (_clack <= 0f) { _clack = 0.32f; global::Gooby.Unity.App.Sfx.Play("soft_tick", UnityEngine.Random.Range(-0.5f, 0.5f), 0.7f); }
                    if (!_apexSaid) { _apexSaid = true; Caption?.Invoke("park.coaster.lift"); }
                }
                if (_coasterS >= liftEnd) _coasterPhase = 1;
            }
            else
            {
                // gravity after the crest (energy from the top, a little friction), never slower than a crawl
                var topH = TrackHeight(liftEnd);
                var h = TrackHeight(_coasterS);
                var v = Mathf.Sqrt(Mathf.Max(0f, 2.6f * 2.6f + 2f * 9.81f * (topH - h))) * 0.86f;
                _coasterV = Mathf.Max(3.4f, v);
                if (!_dropSaid) { _dropSaid = true; Caption?.Invoke("park.coaster.drop"); global::Gooby.Unity.App.Sfx.Play("game_whoosh"); }
                if (!_hillsSaid && _coasterS > _trackLength * 0.55f) { _hillsSaid = true; Caption?.Invoke("park.coaster.hills"); }
                if (_coasterS > _trackLength - 14f)
                {
                    _coasterV = Mathf.Lerp(_coasterV, 1.6f, Mathf.InverseLerp(_trackLength - 14f, _trackLength - 2f, _coasterS));
                    if (!_brakeSaid) { _brakeSaid = true; Caption?.Invoke("park.coaster.brake"); }
                }
            }
            _coasterS += _coasterV * dt;
            if (_coasterS >= _trackLength)
            {
                PlaceCoasterCar(0f);
                Caption?.Invoke("park.coaster.done");
                StopRide();
                return;
            }
            PlaceCoasterCar(_coasterS);
        }

        // ── camera ────────────────────────────────────────────────────────────

        /// <summary>Where the camera wants to be this frame: following Gooby on the plaza, or the ride's own shot.</summary>
        public (Vector3 pos, Quaternion rot, float fov) CameraFor(Vector3 gooby)
        {
            switch (ActiveRide)
            {
                case "wheel":
                {
                    var g = _gondolas[0] != null ? _gondolas[0].position : gooby;
                    var pos = g + new Vector3(0f, 0.2f, -4.2f);
                    return (pos, Quaternion.LookRotation(g + new Vector3(0f, -1.1f, 0f) - pos), 50f);
                }
                case "karussell":
                {
                    var c = W(CarouselAt + Vector3.up * 1.4f);
                    var phi = Mathf.Sin(_rideT * 0.35f) * 0.6f;
                    var pos = c + new Vector3(Mathf.Sin(phi) * 9.5f, 3.8f, -Mathf.Cos(phi) * 9.5f);
                    return (pos, Quaternion.LookRotation(c - pos), 46f);
                }
                case "coaster":
                {
                    var (p, fwd, up) = TrackFrame(_coasterS);
                    var pos = p + up * 1.75f - fwd * 1.6f;
                    var look = TrackFrame(_coasterS + 7f).p + up * 0.6f;
                    return (pos, Quaternion.LookRotation(look - pos, up), 62f);
                }
                case "scooter":
                {
                    var c = W(ArenaAt);
                    var pos = c + new Vector3(0f, 9f, -9.5f);
                    return (pos, Quaternion.LookRotation(c + Vector3.up * 0.3f - pos), 42f);
                }
                default:
                {
                    var target = gooby + new Vector3(0f, 0.8f, 2.6f);
                    var pos = gooby + new Vector3(0f, 8.6f, -8.2f);
                    return (pos, Quaternion.LookRotation(target - pos), 48f);
                }
            }
        }
    }
}
