using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D toy-room circuit of the Spielzeug-Rennen (port of toy_racer_world.gd + _track.gd): the engine's track samples
    /// become a real orange track ribbon with red-and-white kerbs — bumps and loops included — on a checkered rug in
    /// Gooby's room, giant toys and furniture round it, the finish gate at the line. Four karts race on it, the player's
    /// Gooby at the wheel of his and three Gooby friends in theirs; rainbow item boxes spin in their rows, dropped building
    /// blocks lie where they fell, drift sparks change colour with the charge, a boost flames out the back. A chase camera
    /// rides behind Gooby's kart and rolls with the track through the loops. Engine coordinates are right-handed metres;
    /// they map with z mirrored, so the lateral the finger steers towards stays on the screen's right.
    /// </summary>
    public sealed partial class ToyRacerScreen
    {
        private World.MgStage3D _stage;
        private readonly List<Transform> _karts3d = new List<Transform>();
        private readonly List<World.GoobyAvatar> _drivers = new List<World.GoobyAvatar>();
        private readonly List<Transform> _boxes3d = new List<Transform>();
        private readonly Dictionary<ToyRacer.Block, Transform> _blocks3d = new Dictionary<ToyRacer.Block, Transform>();
        private readonly Stack<Transform> _blockPool = new Stack<Transform>();
        private readonly HashSet<ToyRacer.Block> _liveBlocks = new HashSet<ToyRacer.Block>();
        private readonly List<ToyRacer.Block> _deadBlocks = new List<ToyRacer.Block>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Transform _shield;
        private Vector3 _camPos, _camUp = Vector3.up;
        private bool _camSet;
        private float _sparkEmit, _clock;

        private static readonly string[] KartModels = { "race", "sedan-sports", "hatchback-sports", "police" };
        private static readonly string[] DriverFurs = { "fell_minze", "fell_himmel", "fell_zuckerwatte" };
        private const float KartScale = 0.3f;

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 U(ToyRacer.V3 v) => World.MgStage3D.Origin + new Vector3((float)v.X, (float)v.Y, (float)-v.Z);
        private static Vector3 D(ToyRacer.V3 v) => new Vector3((float)v.X, (float)v.Y, (float)-v.Z);

        private static ToyRacer.V3 RightOf(ToyRacer.V3 t, ToyRacer.V3 up)
        {
            var rx = t.Y * up.Z - t.Z * up.Y;
            var ry = t.Z * up.X - t.X * up.Z;
            var rz = t.X * up.Y - t.Y * up.X;
            var len = System.Math.Sqrt(rx * rx + ry * ry + rz * rz);
            return len > 1e-6 ? new ToyRacer.V3(rx / len, ry / len, rz / len) : new ToyRacer.V3(1.0, 0.0, 0.0);
        }

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#F6E6D6"));
            _stage.Bind(Field);
            _stage.Camera.fieldOfView = 58f;
            _stage.Camera.farClipPlane = 140f;
            var root = _stage.Root.transform;
            var track = _game.Race.Track;
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var s in track.Samples) { minX = System.Math.Min(minX, s.P.X); maxX = System.Math.Max(maxX, s.P.X); minZ = System.Math.Min(minZ, -s.P.Z); maxZ = System.Math.Max(maxZ, -s.P.Z); }
            var cx = (float)(minX + maxX) * 0.5f; var cz = (float)(minZ + maxZ) * 0.5f;
            var w = (float)(maxX - minX) + 6f; var d = (float)(maxZ - minZ) + 6f;
            // the room: plank floor, the checkered rug, walls with giant furniture and toys
            World.ArtLibrary.Box(root, "Floor", World.MgStage3D.At(cx, -0.06f, cz), new Vector3(w + 60f, 0.1f, d + 60f), World.ArtLibrary.PbrSlab("wood_floor", new Vector2(w + 60f, d + 60f), 4f, World.ArtLibrary.Hex("#E9C9A3"), 0.35f), false);
            World.ArtLibrary.Box(root, "RugBorder", World.MgStage3D.At(cx, -0.03f, cz), new Vector3(w + 1.2f, 0.04f, d + 1.2f), World.ArtLibrary.Pbr("carpet", 1.2f, World.ArtLibrary.Hex("#E7A8A0"), 0.05f), false);
            var rugA = World.ArtLibrary.Pbr("carpet", 1.2f, World.ArtLibrary.Hex("#F6D9C8"), 0.05f);
            var rugB = World.ArtLibrary.Pbr("carpet", 1.2f, World.ArtLibrary.Hex("#F1C4B0"), 0.05f);
            var rug = new GameObject("Rug").transform;
            rug.SetParent(root, false);
            const float cell = 2f;
            for (var x = -w * 0.5f; x < w * 0.5f; x += cell)
                for (var z = -d * 0.5f; z < d * 0.5f; z += cell)
                    World.ArtLibrary.Box(rug, "RugCell", World.MgStage3D.At(cx + x + cell * 0.5f, -0.009f, cz + z + cell * 0.5f), new Vector3(cell, 0.02f, cell), ((int)((x + w) / cell) + (int)((z + d) / cell)) % 2 == 0 ? rugA : rugB, false);
            _stage.MergeChildren(rug, "Rug");
            var wall = World.ArtLibrary.PbrSlab("plaster", new Vector2(120f, 30f), 4f, World.ArtLibrary.Hex("#FFF1E0"), 0.1f);
            World.ArtLibrary.Box(root, "WallN", World.MgStage3D.At(cx, 14f, cz + d * 0.5f + 22f), new Vector3(w + 60f, 30f, 0.4f), wall, false);
            World.ArtLibrary.Box(root, "WallE", World.MgStage3D.At(cx + w * 0.5f + 22f, 14f, cz), new Vector3(0.4f, 30f, d + 60f), wall, false);
            World.ArtLibrary.Box(root, "WallW", World.MgStage3D.At(cx - w * 0.5f - 22f, 14f, cz), new Vector3(0.4f, 30f, d + 60f), wall, false);
            World.ArtLibrary.Box(root, "WallS", World.MgStage3D.At(cx, 14f, cz - d * 0.5f - 22f), new Vector3(w + 60f, 30f, 0.4f), wall, false);
            foreach (var (prop, x, z, yaw, s) in new[] { ("sofa", 0f, 1f, 180f, 9f), ("bear", -0.42f, 0.42f, 150f, 12f), ("lamp_floor", 0.45f, 0.48f, 0f, 9f), ("books", 0.5f, -0.4f, 30f, 10f), ("plant_tall", -0.5f, -0.45f, 0f, 9f), ("crate", -0.48f, 0.05f, 20f, 9f), ("radio", 0.52f, 0.1f, -80f, 10f), ("table_low", 0.05f, -0.52f, 0f, 8f) })
                _stage.HomeProp(prop, World.MgStage3D.At(cx + x * (w + 26f), 0f, cz + z * (d + 26f)), s, yaw);
            BuildTrackMeshes(root, track);
            var gate = _stage.Prop("gate-finish", Vector3.zero, 1f);
            var s0 = track.Samples[0];
            gate.position = U(s0.P);
            gate.rotation = Quaternion.LookRotation(D(s0.T), D(s0.Up));
            // item boxes: one per box of every row
            foreach (var row in track.ItemRows)
                for (var b = 0; b < row.Boxes.Count; b++)
                {
                    var box = _stage.Prop("item-box", Vector3.zero, 0.7f);
                    _boxes3d.Add(box);
                }
            for (var k = 0; k < _game.Race.Karts.Count; k++)
            {
                var kart = _stage.Prop(KartModels[k % KartModels.Length], Vector3.zero, KartScale);
                _karts3d.Add(kart);
                if (k > 0) _ = SpawnDriver(kart, k);
            }
            _shield = World.ArtLibrary.Sphere(root, "Shield", Vector3.zero, 0.5f, World.ArtLibrary.Transparent(new Color(0.55f, 0.85f, 1f, 0.25f)), false).transform;
            _shield.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnRacer(outfit);
        }

        /// <summary>The deck (orange, darker bands) and kerbs (red/white) as two meshes along the samples, loops included.</summary>
        private void BuildTrackMeshes(Transform root, ToyRacer.Track track)
        {
            var n = track.Samples.Count;
            var half = (float)_game.Tune.TrackHalfW;
            var deckV = new List<Vector3>(); var deckA = new List<int>(); var deckB = new List<int>();
            var kerbV = new List<Vector3>(); var kerbRed = new List<int>(); var kerbWhite = new List<int>();
            for (var i = 0; i <= n; i++)
            {
                var s = track.Samples[i % n];
                var p = D(s.P);
                var up = D(s.Up).normalized;
                var r = D(RightOf(s.T, s.Up)).normalized;
                deckV.Add(p - r * half + up * 0.006f);
                deckV.Add(p + r * half + up * 0.006f);
                foreach (var side in new[] { -1f, 1f })
                {
                    kerbV.Add(p + r * side * half + up * 0.004f);
                    kerbV.Add(p + r * side * (half + 0.1f) + up * 0.004f);
                    kerbV.Add(p + r * side * half + up * 0.07f);
                    kerbV.Add(p + r * side * (half + 0.1f) + up * 0.07f);
                }
                if (i == 0) continue;
                var a = (i - 1) * 2; var b = i * 2;
                var list = (i - 1) % 8 < 4 ? deckA : deckB;
                list.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                var ka = (i - 1) * 8; var kb = i * 8;
                var kl = ((i - 1) / 3) % 2 == 0 ? kerbRed : kerbWhite;
                for (var side = 0; side < 2; side++)
                {
                    // the kerb's top and inner face, both windings (the strip is seen from above and from the deck)
                    var o = side * 4;
                    kl.AddRange(new[] { ka + o + 2, kb + o + 3, ka + o + 3, ka + o + 2, kb + o + 2, kb + o + 3 });
                    kl.AddRange(new[] { ka + o + 2, ka + o + 3, kb + o + 3, ka + o + 2, kb + o + 3, kb + o + 2 });
                    kl.AddRange(new[] { ka + o, kb + o + 2, ka + o + 2, ka + o, kb + o, kb + o + 2 });
                    kl.AddRange(new[] { ka + o, ka + o + 2, kb + o + 2, ka + o, kb + o + 2, kb + o });
                }
            }
            MeshPart(root, "TrackDeck", deckV, new[] { deckA, deckB }, new[] { World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF9F43"), 0.45f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F08A2E"), 0.45f) });
            MeshPart(root, "TrackKerbs", kerbV, new[] { kerbRed, kerbWhite }, new[] { World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F26D6D"), 0.4f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFF6EC"), 0.4f) });
        }

        private void MeshPart(Transform root, string name, List<Vector3> verts, List<int>[] subs, Material[] mats)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.subMeshCount = subs.Length;
            for (var i = 0; i < subs.Length; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = World.MgStage3D.Origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private async System.Threading.Tasks.Task SpawnRacer(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(_karts3d[0].position, 0.36f, outfit);
            if (g == null || _stage == null) return;
            g.transform.SetParent(_karts3d[0], true);
            g.transform.localPosition = new Vector3(0f, 0.45f, -0.15f);
            g.transform.localRotation = Quaternion.identity;
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
        }

        private async System.Threading.Tasks.Task SpawnDriver(Transform kart, int k)
        {
            var g = await World.GoobyAvatar.CreateAsync(kart, kart.position);
            if (g == null) return;
            if (_stage == null || kart == null) { Object.Destroy(g.gameObject); return; }
            g.transform.SetParent(kart, false);
            g.transform.localPosition = new Vector3(0f, 0.45f, -0.15f);
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = Vector3.one * (0.36f / KartScale);
            g.Wear(new Dictionary<string, string> { ["fell"] = DriverFurs[(k - 1) % DriverFurs.Length] });
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
            World.RanchDressing.QuietShadows(g.transform);
            _drivers.Add(g);
        }

        private Transform NewBlock()
        {
            if (_blockPool.Count > 0) { var t = _blockPool.Pop(); t.gameObject.SetActive(true); return t; }
            var b = new GameObject("Block").transform;
            b.SetParent(_stage.Root.transform, false);
            var col = World.ArtLibrary.Solid(World.ArtLibrary.Hex(_blocks3d.Count % 2 == 0 ? "#F26D6D" : "#7FB8FF"), 0.45f);
            World.ArtLibrary.Box(b, "Brick", Vector3.zero, new Vector3(0.32f, 0.2f, 0.32f), col).transform.localPosition = new Vector3(0f, 0.1f, 0f);
            foreach (var (x, z) in new[] { (-0.08f, -0.08f), (0.08f, -0.08f), (-0.08f, 0.08f), (0.08f, 0.08f) })
                World.ArtLibrary.Cylinder(b, "Stud", Vector3.zero, 0.05f, 0.05f, col, false).transform.localPosition = new Vector3(x, 0.22f, z);
            return b;
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _clock += dt;
            var race = _game.Race;
            var track = race.Track;
            for (var k = 0; k < race.Karts.Count && k < _karts3d.Count; k++)
            {
                var kart = race.Karts[k];
                var f = ToyRacer.PointAt(track, kart.S);
                var right = RightOf(f.T, f.Up);
                var pos = U(f.P) + D(right) * (float)kart.Lateral;
                var t = _karts3d[k];
                t.position = pos;
                var rot = Quaternion.LookRotation(D(f.T), D(f.Up));
                var slide = kart.Drifting ? Mathf.Sign((float)(kart.TargetLateral - kart.Lateral)) * 18f : 0f;
                var spin = kart.StunT > 0.0 ? (float)kart.StunT * 720f : 0f;
                t.rotation = rot * Quaternion.Euler(0f, slide + spin, 0f);
            }
            // item boxes spin in their rows until a kart takes them
            var bi = 0;
            foreach (var row in track.ItemRows)
            {
                var f = ToyRacer.PointAt(track, row.S);
                var right = RightOf(f.T, f.Up);
                foreach (var box in row.Boxes)
                {
                    if (bi >= _boxes3d.Count) break;
                    var tr = _boxes3d[bi++];
                    tr.gameObject.SetActive(box.RespawnT <= 0.0);
                    tr.position = U(f.P) + D(right) * (float)box.Lat + D(f.Up) * (0.25f + Mathf.Sin(_clock * 4f + bi) * 0.04f);
                    tr.rotation = Quaternion.Euler(20f, _clock * 120f + bi * 30f, 10f);
                }
            }
            _liveBlocks.Clear();
            foreach (var block in race.Blocks)
            {
                _liveBlocks.Add(block);
                if (!_blocks3d.TryGetValue(block, out var tr)) _blocks3d[block] = tr = NewBlock();
                var f = ToyRacer.PointAt(track, block.S);
                tr.position = U(f.P) + D(RightOf(f.T, f.Up)) * (float)block.Lat;
                tr.rotation = Quaternion.LookRotation(D(f.T), D(f.Up));
            }
            _deadBlocks.Clear();
            foreach (var kv in _blocks3d) if (!_liveBlocks.Contains(kv.Key)) _deadBlocks.Add(kv.Key);
            foreach (var d in _deadBlocks) { var tr = _blocks3d[d]; tr.gameObject.SetActive(false); _blockPool.Push(tr); _blocks3d.Remove(d); }
            var me = _game.Player;
            var mine = _karts3d[0];
            _shield.gameObject.SetActive(me.Shield);
            if (me.Shield) _shield.position = mine.position + mine.up * 0.25f;
            Effects(me, mine, dt);
            ChaseCamera(me, mine, dt);
        }

        /// <summary>Drift sparks in the charge colour from the rear wheels; a flame and puffs while the boost lasts.</summary>
        private void Effects(ToyRacer.Kart me, Transform mine, float dt)
        {
            if (ReducedMotion) return;
            _sparkEmit -= dt;
            if (_sparkEmit > 0f) return;
            _sparkEmit = 0.05f;
            var rear = mine.position - mine.forward * 0.36f + mine.up * 0.05f;
            if (me.Drifting && me.DriftCharge > 0.05)
            {
                var c = ChargeColor((float)me.DriftCharge);
                World.Effects.Puff(rear + mine.right * 0.14f, c, 2, 0.06f, 1.4f, 0.3f, -mine.forward + mine.up, 0.6f);
                World.Effects.Puff(rear - mine.right * 0.14f, c, 2, 0.06f, 1.4f, 0.3f, -mine.forward + mine.up, 0.6f);
            }
            if (me.BoostT > 0.0) World.Effects.Puff(rear, new Color(1f, 0.6f, 0.3f, 0.9f), 3, 0.1f, 2f, 0.3f, -mine.forward, 0.3f);
        }

        /// <summary>Behind and above the kart in its own frame (so the camera rolls through a loop), eased towards its spot.</summary>
        private void ChaseCamera(ToyRacer.Kart me, Transform mine, float dt)
        {
            var f = ToyRacer.PointAt(_game.Race.Track, me.S);
            var fwd = D(f.T).normalized;
            var up = D(f.Up).normalized;
            var want = mine.position - fwd * 2.3f + up * 1.05f;
            var k = _camSet ? 1f - Mathf.Exp(-6f * dt) : 1f;
            _camPos = Vector3.Lerp(_camPos, want, k);
            _camUp = Vector3.Slerp(_camUp, up, k).normalized;
            _camSet = true;
            var look = mine.position + fwd * 1.6f + up * 0.2f;
            _stage.Camera.transform.position = _camPos;
            _stage.Camera.transform.rotation = Quaternion.LookRotation(look - _camPos, _camUp);
        }

        private void StageEvent(ToyRacer.Ev ev)
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            if (ev.Kart != 0) return;
            switch (ev.Type)
            {
                case "boost": case "turbo": case "overtake": case "lap": gb.SetEmotion("ecstatic"); break;
                case "blockHit": case "offtrack": gb.SetEmotion("dizzy"); break;
                case "pickup": case "shield": gb.SetEmotion("happy"); break;
                case "finish": gb.SetEmotion("ecstatic"); gb.Play("celebrate", 0.1f); break;
            }
        }

        private void DropStage()
        {
            _karts3d.Clear(); _drivers.Clear(); _boxes3d.Clear(); _blocks3d.Clear(); _blockPool.Clear(); _liveBlocks.Clear(); _deadBlocks.Clear();
            foreach (var m in _meshes) if (m != null) Object.Destroy(m);
            _meshes.Clear();
            _camSet = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
