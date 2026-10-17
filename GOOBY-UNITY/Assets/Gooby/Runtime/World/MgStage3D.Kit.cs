using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Set dressing shared by the minigame dioramas (port of the Godot _3db/_3dc stage helpers: ground, hill rows, tree
    /// and fence rows, clouds, sun, flower scatter) and the tilted-camera mapping for table/board games seen from above.
    /// Positions are stage-local (relative to <see cref="Origin"/>); everything hangs under <see cref="Root"/> and dies
    /// with the stage.
    /// </summary>
    public sealed partial class MgStage3D
    {
        private static Mesh _cone;

        /// <summary>Stage-local position → world.</summary>
        public static Vector3 At(float x, float y, float z) => Origin + new Vector3(x, y, z);

        /// <summary>
        /// Moves the camera off the default front view (stage-local position, pitch down in degrees, vertical FOV). The
        /// engine's view space then maps onto the ground through <see cref="GroundPoint"/> instead of <see cref="ViewToWorld"/>.
        /// </summary>
        public void Aim(Vector3 localPos, float pitchDeg, float fov, float yawDeg = 0f)
        {
            Camera.transform.position = Origin + localPos;
            Camera.transform.rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
            Camera.fieldOfView = fov;
        }

        /// <summary>
        /// Engine view point (px, y down) → the point on the horizontal plane y = <paramref name="groundY"/> (stage-local)
        /// that the camera shows there: a 3D piece placed at it sits exactly under the 2D tap target. Valid after
        /// <see cref="Bind"/> (the camera's aspect is the view's).
        /// </summary>
        public Vector3 GroundPoint(double vx, double vy, float groundY = 0f)
        {
            var ray = Camera.ViewportPointToRay(new Vector3((float)vx / ViewW, 1f - (float)vy / ViewH, 0f));
            var planeY = Origin.y + groundY;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return ray.origin;
            var t = (planeY - ray.origin.y) / ray.direction.y;
            return ray.origin + ray.direction * Mathf.Max(0f, t);
        }

        /// <summary>
        /// Engine view point (px, y down) → the point on the upright plane z = <paramref name="worldZ"/> (world) the camera
        /// shows there: a 3D figure standing at that depth then covers its 2D tap box exactly.
        /// </summary>
        public Vector3 PlanePoint(double vx, double vy, float worldZ)
        {
            var ray = Camera.ViewportPointToRay(new Vector3((float)vx / ViewW, 1f - (float)vy / ViewH, 0f));
            if (Mathf.Abs(ray.direction.z) < 1e-4f) return ray.origin;
            var t = (worldZ - ray.origin.z) / ray.direction.z;
            return ray.origin + ray.direction * Mathf.Max(0f, t);
        }

        /// <summary>A world point → the play field's own pixels (y down), for 2D juice (floats, bursts) over a free camera.</summary>
        public Vector2 ToField(Vector3 world, float fieldW, float fieldH)
        {
            var v = Camera.WorldToViewportPoint(world);
            return new Vector2(v.x * fieldW, (1f - v.y) * fieldH);
        }

        /// <summary>World metres covered by <paramref name="px"/> view pixels around a view point on the ground plane.</summary>
        public float GroundMetres(double vx, double vy, double px, float groundY = 0f) =>
            Vector3.Distance(GroundPoint(vx - px * 0.5, vy, groundY), GroundPoint(vx + px * 0.5, vy, groundY));

        // ── set dressing ─────────────────────────────────────────────────────

        /// <summary>A wide flat ground slab (PBR grass/soil/sand/… tinted) whose top face lies at <paramref name="y"/>.</summary>
        public GameObject Ground(float y, Color tint, string surface = "grass", float width = 70f, float depth = 60f, float zCentre = 18f)
        {
            var mat = ArtLibrary.PbrSlab(surface, new Vector2(width, depth), 2.4f, tint, 0.04f, 0.6f);
            return ArtLibrary.Box(Root.transform, "Ground", At(0f, y - 1f, zCentre), new Vector3(width, 2f, depth), mat, false);
        }

        /// <summary>Soft rounded hills (squashed spheres) in a row behind the set; far rows paler.</summary>
        public void Hills(float groundY, float z, int count, Color color, float height, float spread, float x0 = 0f, int seed = 1)
        {
            var mat = ArtLibrary.Solid(color, 0.05f);
            var rng = new System.Random(seed);
            for (var i = 0; i < count; i++)
            {
                var x = x0 - spread * 0.5f + spread * (i + 0.5f) / count + (float)(rng.NextDouble() - 0.5) * spread / count * 0.6f;
                var w = spread / count * (1.5f + (float)rng.NextDouble() * 0.6f);
                var h = height * (0.75f + (float)rng.NextDouble() * 0.5f);
                var hill = ArtLibrary.Sphere(Root.transform, "Hill", At(x, groundY, z), 1f, mat, false);
                hill.transform.localScale = new Vector3(w, h * 2f, w * 0.5f);
            }
        }

        /// <summary>A row of Blender scenery pieces (tree_round, tree_poplar, picket_fence, hedge …) from x0 to x1.</summary>
        public void Row(string archetype, float groundY, float z, float x0, float x1, int count, float scale = 1f, float yaw = 0f, float jitter = 0.3f, int seed = 2)
        {
            var rng = new System.Random(seed);
            for (var i = 0; i < count; i++)
            {
                var f = count == 1 ? 0.5f : i / (float)(count - 1);
                var x = Mathf.Lerp(x0, x1, f) + (float)(rng.NextDouble() - 0.5) * jitter;
                var zz = z + (float)(rng.NextDouble() - 0.5) * jitter;
                var s = scale * (0.85f + (float)rng.NextDouble() * 0.3f);
                PlaceScenery(archetype, At(x, groundY, zz), yaw + (float)(rng.NextDouble() - 0.5) * 30f * (jitter > 0f ? 1f : 0f), s);
            }
        }

        /// <summary>A fence line of <paramref name="archetype"/> segments (picket_fence ≈ 1.9 m, log_fence ≈ 2 m) along x.</summary>
        public void Fence(string archetype, float groundY, float z, float x0, float x1, float segment = 1.9f, float scale = 1f, float yaw = 0f)
        {
            for (var x = x0; x <= x1 + 0.01f; x += segment * scale) PlaceScenery(archetype, At(x, groundY, z), yaw, scale);
        }

        /// <summary>Puffy unlit clouds (three squashed spheres each) drifting nowhere — far behind the set.</summary>
        public List<Transform> Clouds(int count, float y, float z, float spread, float size = 1f, int seed = 3)
        {
            var list = new List<Transform>();
            var mat = ArtLibrary.Glow(new Color(1f, 1f, 1f));
            var rng = new System.Random(seed);
            for (var i = 0; i < count; i++)
            {
                var c = new GameObject("Cloud").transform;
                c.SetParent(Root.transform, false);
                c.position = At(-spread * 0.5f + spread * (i + 0.5f) / count + (float)(rng.NextDouble() - 0.5) * 2f, y + (float)rng.NextDouble() * 1.6f, z);
                var s = size * (0.8f + (float)rng.NextDouble() * 0.5f);
                foreach (var (dx, dy, r) in new[] { (-0.9f, 0f, 0.75f), (0f, 0.25f, 1f), (0.95f, 0.02f, 0.7f) })
                {
                    var puff = ArtLibrary.Sphere(c, "Puff", c.position + new Vector3(dx, dy, 0f) * s, r * s, mat, false);
                    puff.transform.localScale = new Vector3(r * 2f * s, r * 1.4f * s, r * 1.2f * s);
                }
                list.Add(c);
            }
            return list;
        }

        /// <summary>A warm glowing sun disc far behind the set.</summary>
        public GameObject Sun(Vector3 local, float radius, Color? color = null) =>
            ArtLibrary.Sphere(Root.transform, "Sun", At(local.x, local.y, local.z), radius, ArtLibrary.Glow(color ?? new Color(1.25f, 1.08f, 0.62f)), false);

        /// <summary>The painted valley backdrop (bg_town_horizon …) far behind the set, bottom edge a little under the ground.</summary>
        public Transform Painting(string texture, float groundY, float z, float height) =>
            ArtLibrary.Horizon(Root.transform, texture, At(0f, groundY - height * 0.08f, z), height);

        /// <summary>Little grass tufts (low cones) and minigame flower models scattered over a ground rectangle.</summary>
        public void Meadow(float groundY, float x0, float x1, float z0, float z1, int tufts, int flowers, int seed = 4, Color? tuft = null)
        {
            var rng = new System.Random(seed);
            var mat = ArtLibrary.Solid(tuft ?? new Color(0.36f, 0.62f, 0.3f), 0.05f);
            for (var i = 0; i < tufts; i++)
            {
                var x = Mathf.Lerp(x0, x1, (float)rng.NextDouble());
                var z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
                var h = 0.16f + (float)rng.NextDouble() * 0.18f;
                Cone(At(x, groundY, z), 0.06f + h * 0.2f, h, mat);
            }
            var kinds = new[] { "flower_redA", "flower_yellowA", "flower_purpleA", "flower_A", "flower_B" };
            for (var i = 0; i < flowers; i++)
            {
                var x = Mathf.Lerp(x0, x1, (float)rng.NextDouble());
                var z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
                Prop(kinds[rng.Next(kinds.Length)], At(x, groundY, z), 0.6f + (float)rng.NextDouble() * 0.4f, (float)rng.NextDouble() * 360f);
            }
        }

        /// <summary>A low-poly cone standing on <paramref name="basePos"/> (grass tufts, party hats, traffic cones).</summary>
        public GameObject Cone(Vector3 basePos, float radius, float height, Material mat, bool shadows = false)
        {
            var go = new GameObject("Cone");
            go.transform.SetParent(Root.transform, false);
            go.transform.position = basePos;
            go.transform.localScale = new Vector3(radius, height, radius);
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static readonly Dictionary<int, Mesh> Tori = new Dictionary<int, Mesh>();

        /// <summary>
        /// A torus lying flat (its axis is up) centred on <paramref name="center"/>: ring radius <paramref name="radius"/> m,
        /// tube radius <paramref name="tube"/> m (hoops, rims, life buoys, glowing gates). Turn it to stand upright.
        /// </summary>
        public GameObject Torus(Transform parent, string name, Vector3 center, float radius, float tube, Material mat, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : Root.transform, false);
            go.transform.position = center;
            go.transform.localScale = Vector3.one * radius;
            go.AddComponent<MeshFilter>().sharedMesh = TorusMesh(Mathf.Clamp(tube / Mathf.Max(0.001f, radius), 0.01f, 0.9f));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Unit torus (ring radius 1) with a tube of <paramref name="tube"/>, cached per hundredth of the ratio.</summary>
        private static Mesh TorusMesh(float tube)
        {
            var key = Mathf.RoundToInt(tube * 100f);
            if (Tori.TryGetValue(key, out var cached) && cached != null) return cached;
            tube = key / 100f;
            const int n = 36, m = 10;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (var i = 0; i <= n; i++)
            {
                var a = i * Mathf.PI * 2f / n;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (var j = 0; j <= m; j++)
                {
                    var b = j * Mathf.PI * 2f / m;
                    var normal = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    verts.Add(dir + normal * tube);
                    normals.Add(normal);
                    if (i == n || j == m) continue;
                    var k = i * (m + 1) + j;
                    tris.AddRange(new[] { k, k + 1, k + m + 2, k, k + m + 2, k + m + 1 });
                }
            }
            var mesh = new Mesh { name = "MgTorus" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Tori[key] = mesh;
            return mesh;
        }

        /// <summary>Unit cone: base radius 1 on y = 0, tip at y = 1, seven sides (flat shaded).</summary>
        private static Mesh ConeMesh()
        {
            if (_cone != null) return _cone;
            const int n = 7;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (var i = 0; i < n; i++)
            {
                var a0 = i * Mathf.PI * 2f / n;
                var a1 = (i + 1) * Mathf.PI * 2f / n;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var b = verts.Count;
                verts.Add(p1); verts.Add(p0); verts.Add(Vector3.up);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                b = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(Vector3.zero);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            _cone = new Mesh { name = "MgCone" };
            _cone.SetVertices(verts);
            _cone.SetTriangles(tris, 0);
            _cone.RecalculateNormals();
            _cone.RecalculateBounds();
            return _cone;
        }

        /// <summary>
        /// Folds static primitives into one mesh per material under <paramref name="parent"/> (fence posts, grandstand
        /// tiers, a race oval of two hundred rails): one draw instead of hundreds, which is what a phone — and the
        /// software rasteriser of the QA runs — pays per object. Parts that cannot be read are left as they are; the
        /// merged meshes die with the stage. Returns the new renderers (one per material).
        /// </summary>
        public List<MeshRenderer> Merge(Transform parent, IEnumerable<GameObject> parts, string name = "Merged")
        {
            var groups = new Dictionary<Material, (List<CombineInstance> list, bool shadows)>();
            var toLocal = parent.worldToLocalMatrix;
            foreach (var go in parts)
            {
                if (go == null || !go.TryGetComponent<MeshFilter>(out var mf) || !go.TryGetComponent<MeshRenderer>(out var mr)) continue;
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || mr.sharedMaterial == null) continue;
                var key = mr.sharedMaterial;
                if (!groups.TryGetValue(key, out var g)) g = (new List<CombineInstance>(), false);
                for (var sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    g.list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sub, transform = toLocal * go.transform.localToWorldMatrix });
                g.shadows |= mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                groups[key] = g;
                Object.Destroy(go);
            }
            var made = new List<MeshRenderer>();
            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = name + ":" + kv.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.list.ToArray(), true, true);
                _owned.Add(mesh);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = kv.Value.shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                made.Add(r);
            }
            return made;
        }

        /// <summary>Every direct child of <paramref name="root"/> merged into it (see <see cref="Merge(Transform, IEnumerable{GameObject}, string)"/>).</summary>
        public List<MeshRenderer> MergeChildren(Transform root, string name = "Merged")
        {
            var parts = new List<GameObject>();
            foreach (Transform child in root) parts.Add(child.gameObject);
            return Merge(root, parts, name);
        }

        /// <summary>
        /// A minigame GLB fitted like the Godot stages' <c>Models.node</c>: once loaded its largest edge (or, with
        /// <paramref name="byHeight"/>, its height) measures <paramref name="size"/> m in the parent's units, centred over
        /// the anchor with its base on it (or, with <paramref name="ground"/> off, its middle — flying pieces). The anchor
        /// comes back at once and stands upright (world yaw only).
        /// </summary>
        public Transform PropFit(string model, Vector3 worldPos, float size, float yaw = 0f, bool byHeight = false, Transform parent = null, bool ground = true)
        {
            var anchor = new GameObject("MgFit:" + model).transform;
            anchor.SetParent(parent != null ? parent : Root.transform, false);
            anchor.position = worldPos;
            anchor.rotation = Quaternion.Euler(0f, yaw, 0f);
            _ = FitAsync(anchor, System.IO.Path.Combine(AssetLoader.Root, "minigame", model + ".glb"), model, size, byHeight, ground);
            return anchor;
        }

        private static async System.Threading.Tasks.Task FitAsync(Transform anchor, string path, string name, float size, bool byHeight, bool ground)
        {
            var clone = await AssetLoader.InstanceAsync(path, anchor, name);
            if (clone == null) return;
            if (anchor == null) { Object.Destroy(clone); return; }
            var t = clone.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var toLocal = t.worldToLocalMatrix;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var mf in clone.GetComponentsInChildren<MeshFilter>(true)) Grow(mf.sharedMesh, toLocal * mf.transform.localToWorldMatrix, ref min, ref max);
            foreach (var sk in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Grow(sk.sharedMesh, toLocal * sk.transform.localToWorldMatrix, ref min, ref max);
            if (min.x > max.x) return;
            var ext = max - min;
            var edge = byHeight ? ext.y : Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z));
            var k = size / Mathf.Max(0.001f, edge);
            var c = (min + max) * 0.5f;
            t.localScale = Vector3.one * k;
            t.localPosition = new Vector3(-c.x * k, (ground ? -min.y : -c.y) * k, -c.z * k);
        }

        private static void Grow(Mesh mesh, Matrix4x4 m, ref Vector3 min, ref Vector3 max)
        {
            if (mesh == null) return;
            var b = mesh.bounds;
            for (var i = 0; i < 8; i++)
            {
                var p = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        /// <summary>Puts the spawned Gooby (if he is in) at a stage-local spot facing the camera, turned by <paramref name="yaw"/>.</summary>
        public void PoseGooby(Vector3 local, float yaw = 0f, float? scale = null)
        {
            if (Gooby == null) return;
            Gooby.transform.position = Origin + local;
            Gooby.transform.rotation = Quaternion.Euler(0f, 180f + yaw, 0f);
            if (scale.HasValue) Gooby.transform.localScale = Vector3.one * scale.Value;
        }
    }
}
