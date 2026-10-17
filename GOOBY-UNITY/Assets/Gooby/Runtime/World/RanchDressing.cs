using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Set dressing shared by the 3D ranch games (competition arena, obstacle course, herding meadow): the comp_arena.gd
    /// palette, low-poly hills and clouds in a ring around the set, bunting chains, flat rings and pennants as small
    /// cached meshes, and the farm models under StreamingAssets/gooby/ranch.
    /// </summary>
    public static class RanchDressing
    {
        public static readonly Color Holz = new Color(0.79f, 0.55f, 0.35f), HolzDunkel = new Color(0.55f, 0.38f, 0.24f);
        public static readonly Color Creme = new Color(0.93f, 0.9f, 0.82f), Rot = new Color(0.91f, 0.4f, 0.42f);
        public static readonly Color[] Fahnen = { new Color(0.91f, 0.4f, 0.42f), new Color(0.93f, 0.9f, 0.82f), new Color(1f, 0.83f, 0.3f), new Color(0.37f, 0.66f, 0.63f) };

        private static Mesh _pennant, _blob;
        private static readonly Dictionary<string, Mesh> Rings = new Dictionary<string, Mesh>();

        public static string ModelPath(string model) => Path.Combine(AssetLoader.Root, "ranch", model + ".glb");

        public static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A double-sided pennant triangle hanging from its mast at the origin, pointing along +X.</summary>
        public static Mesh PennantMesh()
        {
            if (_pennant != null) return _pennant;
            var v = new[] { new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0f, 0f) };
            _pennant = new Mesh { name = "Wimpel" };
            _pennant.SetVertices(new List<Vector3> { v[0], v[1], v[2], v[0], v[2], v[1] });
            _pennant.SetTriangles(new[] { 0, 1, 2, 3, 4, 5 }, 0);
            _pennant.RecalculateNormals();
            _pennant.RecalculateBounds();
            return _pennant;
        }

        /// <summary>A flat ring (or a disc for inner 0) lying on the ground, facing up.</summary>
        public static Transform Ring(Transform parent, string name, float inner, float outer, Material mat)
        {
            var key = inner.ToString("0.000") + ":" + outer.ToString("0.000");
            if (!Rings.TryGetValue(key, out var mesh) || mesh == null)
            {
                const int n = 40;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                for (var i = 0; i <= n; i++)
                {
                    var a = i * Mathf.PI * 2f / n;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    verts.Add(d * inner);
                    verts.Add(d * outer);
                    if (i == n) continue;
                    var b = i * 2;
                    tris.AddRange(new[] { b, b + 3, b + 1, b, b + 2, b + 3 });
                }
                mesh = new Mesh { name = "Ring" };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                Rings[key] = mesh;
            }
            return MeshObject(parent, name, mesh, mat).transform;
        }

        /// <summary>A unit-diameter low-poly sphere (14 × 8, a third of Unity's primitive) for far hills, clouds and knobs.</summary>
        public static GameObject Blob(Transform parent, string name, Vector3 center, Material mat)
        {
            if (_blob == null)
            {
                const int seg = 14, rings = 8;
                var verts = new List<Vector3>();
                var tris = new List<int>();
                for (var r = 0; r <= rings; r++)
                {
                    var v = Mathf.PI * r / rings;
                    for (var k = 0; k <= seg; k++)
                    {
                        var u = Mathf.PI * 2f * k / seg;
                        verts.Add(new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)) * 0.5f);
                    }
                }
                for (var r = 0; r < rings; r++)
                    for (var k = 0; k < seg; k++)
                    {
                        var a = r * (seg + 1) + k;
                        var b = a + seg + 1;
                        tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                    }
                _blob = new Mesh { name = "Blob" };
                _blob.SetVertices(verts);
                _blob.SetTriangles(tris, 0);
                _blob.RecalculateNormals();
                _blob.RecalculateBounds();
            }
            var go = MeshObject(parent, name, _blob, mat);
            go.transform.position = center;
            return go;
        }

        /// <summary>
        /// Two rings of soft hills (the far one paler) and clouds around a stage-local centre, a sun toward the key light,
        /// all merged into a handful of draws.
        /// </summary>
        public static void Horizon(MgStage3D s, Vector3 centre, float radius, int seed = 13)
        {
            var rng = new System.Random(seed);
            var hill = ArtLibrary.Solid(new Color(0.52f, 0.7f, 0.45f), 0.04f);
            var hillFar = ArtLibrary.Solid(new Color(0.62f, 0.76f, 0.6f), 0.02f);
            var parts = new List<GameObject>();
            for (var i = 0; i < 22; i++)
            {
                var a = i / 22f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.12f;
                var far = i % 2 == 0;
                var r = radius * (far ? 1.24f + (float)rng.NextDouble() * 0.12f : 1f + (float)rng.NextDouble() * 0.09f);
                var size = far ? 70f + (float)rng.NextDouble() * 30f : 46f + (float)rng.NextDouble() * 20f;
                var h = far ? 26f + (float)rng.NextDouble() * 14f : 16f + (float)rng.NextDouble() * 10f;
                var go = Blob(s.Root.transform, "Huegel", MgStage3D.At(centre.x + Mathf.Cos(a) * r, -2f, centre.z + Mathf.Sin(a) * r), far ? hillFar : hill);
                go.transform.localScale = new Vector3(size, h * 2f, size * 0.6f);
                go.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                parts.Add(go);
            }
            var cloud = ArtLibrary.Glow(new Color(1f, 1f, 1f));
            for (var i = 0; i < 8; i++)
            {
                var a = i / 8f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.4f;
                var r = radius * (0.9f + (float)rng.NextDouble() * 0.25f);
                var c = MgStage3D.At(centre.x + Mathf.Cos(a) * r, 34f + (float)rng.NextDouble() * 16f, centre.z + Mathf.Sin(a) * r);
                var size = 5f + (float)rng.NextDouble() * 4f;
                foreach (var (dx, dy, rr) in new[] { (-0.9f, 0f, 0.75f), (0f, 0.28f, 1f), (0.95f, 0.02f, 0.7f) })
                {
                    var puff = Blob(s.Root.transform, "Wolke", c + Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f) * new Vector3(dx, dy, 0f) * size, cloud);
                    puff.transform.localScale = new Vector3(rr * 2f * size, rr * 1.3f * size, rr * 1.2f * size);
                    parts.Add(puff);
                }
            }
            s.Merge(s.Root.transform, parts, "Himmel");
            s.Sun(centre + new Vector3(-62f, 88f, -108f), 8f);
        }

        /// <summary>A farm model (barn, silo, windmill …) of the ranch folder at a world position.</summary>
        public static Transform Farm(MgStage3D s, Transform parent, string model, Vector3 world, float scale, float yaw)
        {
            var t = s.Glb(ModelPath(model), world, scale, yaw);
            if (parent != null) t.SetParent(parent, true);
            return t;
        }

        /// <summary>parcours_deko.gd wimpel_kette: pennants in the four flag colours along a sagging line (world points).</summary>
        public static List<GameObject> Bunting(Transform parent, Vector3 from, Vector3 to, float sag = 0.4f, float spacing = 0.85f)
        {
            var list = new List<GameObject>();
            var count = Mathf.Max(5, Mathf.RoundToInt(Vector3.Distance(from, to) / spacing));
            var along = to - from;
            along.y = 0f;
            along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.right;
            // the pennant's tip (its +X) hangs down, its base runs along the line, its face looks across it
            var hang = Quaternion.LookRotation(-Vector3.Cross(Vector3.up, along), along);
            for (var i = 0; i < count; i++)
            {
                var t = (i + 0.5f) / count;
                var flag = MeshObject(parent, "Wimpel", PennantMesh(), ArtLibrary.Solid(Fahnen[i % Fahnen.Length], 0.2f)).transform;
                flag.position = Vector3.Lerp(from, to, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * sag);
                flag.rotation = hang;
                flag.localScale = new Vector3(0.48f, 0.42f, 1f);
                list.Add(flag.gameObject);
            }
            return list;
        }

        /// <summary>Turns off shadow casting under a root (far trees, barns, the stands: beyond the shadow distance or in the haze).</summary>
        public static void QuietShadows(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>
    /// A grandstand of Gooby fans (comp_arena.gd baue_publikum, with the real Gooby): seated on their benches in mixed
    /// fur and hats, swaying to the beat; a good moment brings them up on their feet to cheer, then they sit back down.
    /// </summary>
    public sealed class RanchCrowd
    {
        private static readonly string[] Furs = { "caramel", "fell_minze", "snow", "fell_lavendel", "fell_himmel", "fell_zuckerwatte", "fell_kakao" };
        private static readonly string[] Hats = { "partyHat", null, "strawHat", "cap", null, "partyHat" };

        private sealed class Fan
        {
            public GoobyAvatar Gooby;
            public Vector3 Seat;
            public float SitIn, Phase;
        }

        private readonly MgStage3D _stage;
        private readonly List<Fan> _fans = new List<Fan>();
        private readonly float _yaw;
        private float _t;

        public RanchCrowd(MgStage3D stage, IList<Vector3> seats, float faceYaw)
        {
            _stage = stage;
            _yaw = faceYaw;
            for (var i = 0; i < seats.Count; i++)
            {
                var fan = new Fan { Seat = seats[i], Phase = i * 1.7f };
                _fans.Add(fan);
                _ = Spawn(fan, i);
            }
        }

        private async System.Threading.Tasks.Task Spawn(Fan fan, int i)
        {
            var root = _stage.Root;
            if (root == null) return;
            var g = await GoobyAvatar.CreateAsync(root.transform, fan.Seat);
            if (g == null) return;
            if (_stage.Root == null || !_fans.Contains(fan)) { Object.Destroy(g.gameObject); return; }
            g.transform.position = fan.Seat;
            g.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            g.transform.localScale = Vector3.one * (0.62f + (i % 4) * 0.04f);
            var equipped = new Dictionary<string, string> { ["fell"] = Furs[i % Furs.Length] };
            var hat = Hats[i % Hats.Length];
            if (hat != null) equipped["hut"] = hat;
            g.Wear(equipped);
            g.Play("sit-loop", 0f);
            g.SetEmotion(i % 3 == 0 ? "ecstatic" : "happy");
            RanchDressing.QuietShadows(g.transform);
            fan.Gooby = g;
        }

        /// <summary>Takes the fans off the stands (a new course builds its own grandstand).</summary>
        public void Destroy()
        {
            foreach (var fan in _fans) if (fan.Gooby != null) Object.Destroy(fan.Gooby.gameObject);
            _fans.Clear();
        }

        /// <param name="cheer">A good moment just happened (every fan stands up when <paramref name="big"/>, else two of three).</param>
        public void Tick(float dt, bool cheer, bool big = false)
        {
            _t += dt;
            for (var i = 0; i < _fans.Count; i++)
            {
                var fan = _fans[i];
                var g = fan.Gooby;
                if (g == null) continue;
                if (cheer && (big || i % 3 != 1))
                {
                    g.Play(i % 2 == 0 ? "celebrate" : "wave", 0.15f);
                    g.SetEmotion("ecstatic");
                    fan.SitIn = 1.5f + (i % 4) * 0.2f;
                }
                if (fan.SitIn > 0f && (fan.SitIn -= dt) <= 0f)
                {
                    g.Play("sit-loop", 0.3f);
                    g.SetEmotion(i % 3 == 0 ? "ecstatic" : "happy");
                }
                var sway = Effects.Reduced ? 0f : Mathf.Sin(_t * 2.3f + fan.Phase) * 4f;
                g.transform.rotation = Quaternion.Euler(0f, _yaw, sway);
                g.transform.position = fan.Seat;
            }
        }
    }
}
