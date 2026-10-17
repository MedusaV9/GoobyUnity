using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Gooby.Core.Life;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Gooby's holiday as a place to visit (port of urlaubs_ort.gd, W15/URLAUB): one script, four scene kinds built far
    /// off at <see cref="Origin"/> — the BEACH (sand, sea with rolling foam, palms, umbrella, deck chair, sandcastle; the
    /// harbour adds a pier, boats, buoys and a lighthouse), the MOUNTAINS (meadow, snowy peaks, pines, tent, campfire;
    /// the spook garden at dusk with mushrooms and lanterns, the night sky with stars), the CITY (cobbles, a row of
    /// houses, café sets under string lights, a fountain; the bakery with pastries, the toy room with blocks and
    /// balloons) and SPACE (moon dust, craters, the Earth in a black sky, a little rocket). Two holiday Goobys hop about;
    /// five tap spots (shells, flowers, star splinters) wait for the mini activity and a little chest glitters at the
    /// souvenir spot. The camera stands still, every destination has its own light (a fixed hour).
    /// </summary>
    public sealed class VacationScene
    {
        public static readonly Vector3 Origin = new Vector3(600f, 0f, 0f);

        public GameObject Root { get; }
        public string DestId { get; }
        public string Archetyp { get; }
        /// <summary>The hour the scene is lit at (each destination its own mood).</summary>
        public double Hour { get; }
        public Color Sky { get; }

        public Vector3 GoobySpot => Origin + new Vector3(0.2f, 0f, 0.9f);
        public Vector3 CameraPosition => Origin + new Vector3(0f, 2.25f, -4.6f);
        public Quaternion CameraRotation => Quaternion.LookRotation(Origin + new Vector3(0f, 0.8f, 1.4f) - CameraPosition, Vector3.up);
        public Vector3 ChestSpot => Origin + new Vector3(2.15f, 0f, 1.6f);

        private readonly Transform _t;
        private readonly List<Transform> _spots = new List<Transform>();
        private readonly List<GoobyAvatar> _tourists = new List<GoobyAvatar>();
        private readonly List<float> _hopIn = new List<float>();
        private readonly List<Transform> _waves = new List<Transform>();
        private readonly List<Transform> _spin = new List<Transform>();
        private readonly List<Transform> _glints = new List<Transform>();
        private Light _fireLight;
        private Transform _chestLid;
        private float _t0, _chestOpen, _fireBase;
        private bool _destroyed;

        private static readonly Vector3[] SpotLocal =
        {
            new Vector3(-2.3f, 0f, 0.4f), new Vector3(-1.2f, 0f, 2.3f), new Vector3(1.25f, 0f, 2.7f), new Vector3(2.6f, 0f, 0.3f), new Vector3(-0.6f, 0f, -0.5f),
        };

        public VacationScene(Transform parent, string destId)
        {
            DestId = destId;
            Archetyp = Urlaub.ArchetypFuer(destId);
            if (Archetyp == "") Archetyp = Urlaub.Strand;
            Root = new GameObject("Vacation:" + destId);
            Root.transform.SetParent(parent, false);
            _t = Root.transform;
            switch (destId)
            {
                case "harbor": Hour = 16.5; Sky = ArtLibrary.Hex("#8CCBF2"); break;
                case "spookGarden": Hour = 19.6; Sky = ArtLibrary.Hex("#B7A3D9"); break;
                case "nightSky": Hour = 22.5; Sky = ArtLibrary.Hex("#22305C"); break;
                case "bigCity": Hour = 18.8; Sky = ArtLibrary.Hex("#F3B58C"); break;
                case "bakery": Hour = 10.0; Sky = ArtLibrary.Hex("#A9D8F5"); break;
                case "toyRoom": Hour = 12.5; Sky = ArtLibrary.Hex("#B9E3FA"); break;
                case "space": Hour = 23.0; Sky = ArtLibrary.Hex("#0B1024"); break;
                case "meadowTrip": Hour = 15.0; Sky = ArtLibrary.Hex("#9ED3F7"); break;
                default: Hour = 14.5; Sky = ArtLibrary.Hex("#86CFF5"); break;
            }
            switch (Archetyp)
            {
                case Urlaub.Berge: BuildMountains(); break;
                case Urlaub.Stadt: BuildCity(); break;
                case Urlaub.Weltraum: BuildSpace(); break;
                default: BuildBeach(); break;
            }
            BuildChest();
            BuildSpots();
        }

        // ── shared pieces ──

        private Vector3 P(float x, float y, float z) => Origin + new Vector3(x, y, z);

        private static Material M(string hex, float smooth = 0.18f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);

        /// <summary>A vertical sky gradient far behind everything (unlit, so the hour's light does not dim it).</summary>
        private void SkyWall(string top, string bottom, float z = 46f)
        {
            var tex = new Texture2D(2, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "vacation_sky" };
            var a = ArtLibrary.Hex(top);
            var b = ArtLibrary.Hex(bottom);
            for (var y = 0; y < 128; y++)
            {
                var c = Color.Lerp(b, a, Mathf.SmoothStep(0f, 1f, y / 127f));
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply();
            ArtLibrary.Quad(_t, "Sky", P(0f, 14f, z), Quaternion.Euler(0f, 180f, 0f), new Vector2(150f, 40f), ArtLibrary.Screen(tex));
        }

        private GameObject Ground(string hexOrSurface, Color tint, bool pbr, float depthFrom = -9f, float depthTo = 30f)
        {
            var depth = depthTo - depthFrom;
            var mat = pbr ? ArtLibrary.Pbr(hexOrSurface, 2.2f, tint, 0.1f, 0.6f) : ArtLibrary.Solid(tint, 0.1f);
            return ArtLibrary.Box(_t, "Ground", P(0f, -0.05f, depthFrom + depth * 0.5f), new Vector3(90f, 0.1f, depth), mat, false);
        }

        private void Model(string name, Vector3 at, float scale = 1f, float yaw = 0f) =>
            Glb(Path.Combine(AssetLoader.Root, "minigame", name + ".glb"), name, at, scale, yaw);

        private void Prop(string archetype, Vector3 at, float scale = 1f, float yaw = 0f) =>
            Glb(AssetLoader.PropPath(archetype), archetype, at, scale, yaw);

        private void Glb(string path, string name, Vector3 at, float scale, float yaw)
        {
            var anchor = new GameObject("Model:" + name).transform;
            anchor.SetParent(_t, false);
            anchor.position = at;
            anchor.rotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.localScale = Vector3.one * scale;
            _ = FillAsync(anchor, path, name);
        }

        private static async Task FillAsync(Transform anchor, string path, string name)
        {
            var clone = await AssetLoader.InstanceAsync(path, anchor, name);
            if (clone == null) return;
            if (anchor == null) { Object.Destroy(clone); return; }
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
        }

        private void Place(string archetype, Vector3 at, float yaw = 0f, float scale = 1f) =>
            Scenery.Place(_t, archetype, at - _t.position, yaw, scale);

        /// <summary>A cone (mountain, tent roof, snow cap) as a mesh: base at <paramref name="basePos"/>, tip straight up.</summary>
        private GameObject Cone(string name, Vector3 basePos, float radius, float height, Material mat, int segments = 28, float squashZ = 1f)
        {
            var mesh = new Mesh { name = "Cone" };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (var i = 0; i < segments; i++)
            {
                var a0 = i * Mathf.PI * 2f / segments;
                var a1 = (i + 1) * Mathf.PI * 2f / segments;
                var v0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius * squashZ);
                var v1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius * squashZ);
                var tip = new Vector3(0f, height, 0f);
                var b = verts.Count;
                verts.Add(v0); verts.Add(tip); verts.Add(v1);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(_t, false);
            go.transform.position = basePos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go;
        }

        /// <summary>A triangular tent: two sloped roof panels and a darker door triangle at the front.</summary>
        private void Tent(Vector3 at, float yaw, string hex)
        {
            var tent = new GameObject("Tent").transform;
            tent.SetParent(_t, false);
            tent.position = at;
            tent.rotation = Quaternion.Euler(0f, yaw, 0f);
            var cloth = M(hex, 0.1f);
            foreach (var s in new[] { -1f, 1f })
            {
                var panel = ArtLibrary.Box(tent, "Roof", at, new Vector3(0.05f, 1.45f, 1.9f), cloth);
                panel.transform.SetParent(tent, true);
                panel.transform.localPosition = new Vector3(s * 0.42f, 0.6f, 0f);
                panel.transform.localRotation = Quaternion.Euler(0f, 0f, s * 35f);
            }
            var door = ArtLibrary.Box(tent, "Door", at, new Vector3(0.5f, 0.85f, 0.04f), M("#5A3B2E"), false);
            door.transform.SetParent(tent, true);
            door.transform.localPosition = new Vector3(0f, 0.42f, -0.93f);
            door.transform.localRotation = Quaternion.identity;
        }

        private void Cloud(Vector3 at, float size)
        {
            var white = ArtLibrary.Glow(new Color(0.98f, 0.98f, 1f));
            for (var i = 0; i < 4; i++)
            {
                var c = ArtLibrary.Sphere(_t, "Cloud", at + new Vector3((i - 1.5f) * size * 0.7f, (i % 2) * size * 0.25f, 0f), size * (0.7f + (i % 2) * 0.3f), white, false);
                c.transform.localScale = new Vector3(c.transform.localScale.x * 1.4f, c.transform.localScale.y * 0.7f, c.transform.localScale.z);
            }
        }

        private void Palm(Vector3 at, float lean, float height)
        {
            var trunk = M("#9A6B43", 0.1f);
            var leaf = M("#3E9B4F", 0.15f);
            var p = at;
            const int n = 7;
            for (var i = 0; i < n; i++)
            {
                var k = i / (float)(n - 1);
                var next = at + new Vector3(lean * k * k, height * (i + 1) / n, 0f);
                var seg = ArtLibrary.Cylinder(_t, "Trunk", (p + next) * 0.5f, 0.12f - k * 0.035f, Vector3.Distance(p, next) + 0.04f, trunk);
                seg.transform.rotation = Quaternion.FromToRotation(Vector3.up, (next - p).normalized);
                p = next;
            }
            for (var i = 0; i < 7; i++)
            {
                var yaw = i * 360f / 7f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var l = ArtLibrary.Sphere(_t, "Leaf", p + dir * 0.75f + Vector3.down * 0.18f, 0.5f, leaf);
                l.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(18f, 0f, 0f);
                l.transform.localScale = new Vector3(0.42f, 0.1f, 1.7f);
            }
            for (var i = 0; i < 3; i++)
                ArtLibrary.Sphere(_t, "Coconut", p + new Vector3(Mathf.Cos(i * 2.1f) * 0.16f, -0.22f, Mathf.Sin(i * 2.1f) * 0.16f), 0.1f, M("#6B4A2E"));
        }

        private void StringLights(Vector3 from, Vector3 to, int bulbs, float sag)
        {
            var colors = new[] { "#FFD27A", "#FF9AB5", "#9FE3D4", "#FFF1B8" };
            for (var i = 0; i <= bulbs; i++)
            {
                var k = i / (float)bulbs;
                var p = Vector3.Lerp(from, to, k) + Vector3.down * Mathf.Sin(k * Mathf.PI) * sag;
                ArtLibrary.Sphere(_t, "Bulb", p, 0.06f, ArtLibrary.Glow(ArtLibrary.Hex(colors[i % colors.Length]) * 1.4f), false);
            }
        }

        // ── beach / harbour ──

        private void BuildBeach()
        {
            var harbor = DestId == "harbor";
            SkyWall(harbor ? "#5FA8E8" : "#5BB8F0", "#FFE7C2");
            Ground("concrete", ArtLibrary.Hex("#F3DCA8"), true, -9f, 7.5f);
            var sea = ArtLibrary.Box(_t, "Sea", P(0f, -0.08f, 26f), new Vector3(150f, 0.05f, 38f), M(harbor ? "#3E9CC9" : "#45B5DA", 0.86f), false);
            ArtLibrary.Box(_t, "Shallows", P(0f, -0.06f, 8.6f), new Vector3(150f, 0.04f, 2.6f), M("#8ED9E6", 0.8f), false);
            for (var i = 0; i < 3; i++)
            {
                var foam = ArtLibrary.Box(_t, "Foam", P(0f, -0.02f, 7.55f + i * 0.9f), new Vector3(150f, 0.02f, 0.12f), ArtLibrary.Glow(new Color(1f, 1f, 1f)), false);
                _waves.Add(foam.transform);
            }
            ArtLibrary.Sphere(_t, "Sun", P(-13f, 7.2f, 44f), 2.4f, ArtLibrary.Glow(new Color(1.3f, 1.12f, 0.75f)), false);
            Cloud(P(9f, 6.8f, 43f), 1.3f);
            Cloud(P(-3f, 8.1f, 44f), 1f);
            Palm(P(-4.2f, 0f, 2.8f), 0.9f, 3.6f);
            Palm(P(4.6f, 0f, 3.8f), -1.1f, 4.1f);
            Palm(P(-6.5f, 0f, 5.5f), 0.6f, 3.2f);
            // umbrella + deck chair + towel + sandcastle
            var pole = P(-1.7f, 0f, 1.9f);
            ArtLibrary.Cylinder(_t, "Pole", pole + Vector3.up * 1.05f, 0.04f, 2.1f, M("#F7F2EA"));
            for (var i = 0; i < 8; i++)
            {
                var wedge = ArtLibrary.Sphere(_t, "Canopy", pole + Vector3.up * 2.05f, 0.95f, M(i % 2 == 0 ? "#E84D5B" : "#FFF6EC"));
                wedge.transform.rotation = Quaternion.Euler(0f, i * 45f, 0f);
                wedge.transform.localScale = new Vector3(0.62f, 0.32f, 1.9f);
            }
            var chair = P(-2.35f, 0f, 1.25f);
            ArtLibrary.Box(_t, "ChairSeat", chair + new Vector3(0f, 0.28f, 0f), new Vector3(0.55f, 0.06f, 0.9f), M("#4FA3D9"));
            var back = ArtLibrary.Box(_t, "ChairBack", chair + new Vector3(0f, 0.5f, 0.5f), new Vector3(0.55f, 0.06f, 0.55f), M("#FFF6EC"));
            back.transform.rotation = Quaternion.Euler(-55f, 0f, 0f);
            ArtLibrary.Box(_t, "Towel", P(0.9f, 0.01f, 2.2f), new Vector3(0.8f, 0.02f, 1.5f), M("#FF8FB1", 0.05f), false);
            ArtLibrary.Box(_t, "TowelStripe", P(0.9f, 0.022f, 2.2f), new Vector3(0.8f, 0.02f, 0.2f), M("#FFF6EC", 0.05f), false);
            var castle = P(1.9f, 0f, 3.1f);
            var sand = M("#D9B97C", 0.05f);
            ArtLibrary.Box(_t, "CastleBase", castle + new Vector3(0f, 0.16f, 0f), new Vector3(0.9f, 0.32f, 0.6f), sand);
            foreach (var x in new[] { -0.4f, 0.4f })
            {
                ArtLibrary.Cylinder(_t, "Tower", castle + new Vector3(x, 0.32f, 0f), 0.14f, 0.64f, sand);
                Cone("TowerTop", castle + new Vector3(x, 0.64f, 0f), 0.16f, 0.2f, sand, 12);
            }
            ArtLibrary.Cylinder(_t, "Bucket", castle + new Vector3(0.75f, 0.12f, -0.3f), 0.11f, 0.24f, M("#F2C230"));
            ArtLibrary.Sphere(_t, "BeachBall", P(-0.9f, 0.18f, 3.1f), 0.18f, M("#FF6F61", 0.5f));
            if (!harbor) return;
            // the harbour: a pier into the sea, boats, buoys, a lighthouse on the point
            var plank = ArtLibrary.Pbr("wood_planks", 0.6f, ArtLibrary.Hex("#C49A6C"), 0.15f, 0.6f);
            ArtLibrary.Box(_t, "Pier", P(-5.2f, 0.32f, 10.5f), new Vector3(1.6f, 0.12f, 9f), plank);
            for (var i = 0; i < 5; i++)
                foreach (var x in new[] { -5.9f, -4.5f })
                    ArtLibrary.Cylinder(_t, "Post", P(x, 0.05f, 6.6f + i * 2f), 0.08f, 0.7f, M("#7A5A3C"));
            Model("boat-fishing-small", P(-2.6f, -0.05f, 12f), 1.4f, -70f);
            Model("boat-row-small", P(3.5f, -0.05f, 10.5f), 1.3f, 40f);
            Model("buoy-flag", P(6.5f, -0.05f, 14f), 1.2f);
            Model("buoy", P(-0.5f, -0.05f, 16f), 1.2f);
            var lh = P(9.5f, 0f, 18f);
            for (var i = 0; i < 5; i++)
                ArtLibrary.Cylinder(_t, "Lighthouse", lh + Vector3.up * (0.5f + i), 0.55f - i * 0.05f, 1f, M(i % 2 == 0 ? "#F5F1EA" : "#E2574C"));
            ArtLibrary.Sphere(_t, "Lamp", lh + Vector3.up * 5.25f, 0.38f, ArtLibrary.Glow(new Color(1.4f, 1.2f, 0.6f)), false);
        }

        // ── mountains / meadow / spook garden / night sky ──

        private void BuildMountains()
        {
            var night = DestId == "nightSky";
            var spook = DestId == "spookGarden";
            SkyWall(night ? "#0D1438" : spook ? "#6D4F9C" : "#5EB0EC", night ? "#3A3F78" : spook ? "#F2A9A0" : "#E8F4FF");
            Ground("grass", ArtLibrary.Hex(night ? "#4B6E4A" : spook ? "#6E8C5A" : "#9BD06F"), true);
            var rock = M(night ? "#4A4F73" : spook ? "#6A5A84" : "#7F8FA6", 0.1f);
            var snow = M(night ? "#C9D3F2" : "#FBFDFF", 0.3f);
            (float x, float z, float r, float h)[] peaks = { (-15f, 30f, 8f, 7.2f), (-3f, 33f, 9.5f, 9.4f), (9f, 31f, 8.5f, 8f), (20f, 32f, 9f, 7f), (-25f, 32f, 8f, 6.4f) };
            foreach (var (x, z, r, h) in peaks)
            {
                Cone("Peak", P(x, -0.1f, z), r, h, rock, 32, 0.55f);
                Cone("Snow", P(x, h * 0.7f - 0.1f, z - r * 0.08f), r * 0.32f, h * 0.31f, snow, 24, 0.55f);
            }
            foreach (var (x, z, s) in new[] { (-6f, 6.5f, 0.62f), (-8.5f, 10f, 0.75f), (6.6f, 7f, 0.66f), (9.5f, 11f, 0.8f), (-3.6f, 12.5f, 0.55f), (3.8f, 13f, 0.6f), (-12f, 13f, 0.8f), (13f, 14f, 0.75f) })
                Place(s > 0.7f ? "tree_tall" : "tree_big", P(x, 0f, z), x * 13f, s);
            if (night)
            {
                ArtLibrary.Sphere(_t, "Moon", P(10f, 7.4f, 44f), 1.8f, ArtLibrary.Glow(new Color(1.15f, 1.12f, 0.95f)), false);
                var rng = new System.Random(7);
                for (var i = 0; i < 90; i++)
                    ArtLibrary.Sphere(_t, "Star", P((float)(rng.NextDouble() * 120 - 60), (float)(rng.NextDouble() * 7 + 3.5), 45.5f), (float)(rng.NextDouble() * 0.08 + 0.05), ArtLibrary.Glow(new Color(1.3f, 1.3f, 1.2f)), false);
            }
            else
            {
                ArtLibrary.Sphere(_t, "Sun", P(-14f, 7.6f, 44f), 2.2f, ArtLibrary.Glow(spook ? new Color(1.3f, 0.75f, 0.5f) : new Color(1.3f, 1.15f, 0.8f)), false);
                Cloud(P(6f, 7.8f, 43f), 1.2f);
                Cloud(P(-6f, 6.6f, 43.5f), 0.9f);
            }
            Tent(P(-2.6f, 0f, 2.9f), 18f, spook ? "#7D5BA6" : "#F08A3C");
            // campfire: a ring of stones, crossed logs, flames and a warm flickering light
            var fire = P(1.4f, 0f, 2.4f);
            for (var i = 0; i < 9; i++)
            {
                var a = i * Mathf.PI * 2f / 9f;
                ArtLibrary.Sphere(_t, "Stone", fire + new Vector3(Mathf.Cos(a) * 0.42f, 0.06f, Mathf.Sin(a) * 0.42f), 0.1f, M("#8E8A85", 0.1f));
            }
            for (var i = 0; i < 3; i++)
            {
                var log = ArtLibrary.Cylinder(_t, "Log", fire + Vector3.up * 0.09f, 0.06f, 0.6f, M("#6B4A2E"));
                log.transform.rotation = Quaternion.Euler(0f, i * 60f, 80f);
            }
            Effects.Campfire(_t, fire + Vector3.up * 0.12f);
            _fireLight = new GameObject("FireLight").AddComponent<Light>();
            _fireLight.transform.SetParent(_t, false);
            _fireLight.transform.position = fire + Vector3.up * 0.6f;
            _fireLight.type = LightType.Point;
            _fireLight.color = ArtLibrary.Hex("#FFB25C");
            _fireLight.range = 5f;
            _fireBase = night || spook ? 2.4f : 1.2f;
            _fireLight.intensity = _fireBase;
            _fireLight.shadows = LightShadows.None;
            Place("tree_stump", P(2.4f, 0f, 2.1f), 20f, 0.8f);
            Place("tree_stump", P(0.5f, 0f, 3.4f), 70f, 0.75f);
            if (DestId == "meadowTrip") Model("windmill", P(-9f, 0f, 14f), 2.2f, 30f);
            if (spook)
            {
                Model("mushroom_red", P(-4.4f, 0f, 1.6f), 1.5f);
                Model("mushroom_red", P(4.6f, 0f, 2.4f), 1.2f, 50f);
                Prop("pumpkin", P(3.2f, 0f, 1.3f), 0.6f, 10f);
                Prop("pumpkin", P(-3.7f, 0f, 4.4f), 0.75f, 80f);
                for (var i = 0; i < 4; i++)
                    ArtLibrary.Sphere(_t, "Lantern", P(-3f + i * 2f, 1.8f + (i % 2) * 0.3f, 5f), 0.12f, ArtLibrary.Glow(new Color(0.8f, 1.3f, 0.7f)), false);
            }
        }

        // ── city / bakery / toy room ──

        private void BuildCity()
        {
            var dusk = DestId == "bigCity";
            SkyWall(dusk ? "#5A6FC2" : "#5DB2EE", dusk ? "#FFB98A" : "#EAF6FF");
            Ground("paving", ArtLibrary.Hex(DestId == "toyRoom" ? "#F6EEDF" : "#F0E7D8"), true);
            string[] houses = { "house_pink", "terrace_a", "house_green", "terrace_c", "house_yellow", "terrace_b", "house_blue", "terrace_d", "house_cream", "house_salmon" };
            for (var i = 0; i < houses.Length; i++)
                Place(houses[i], P(-18f + i * 4.1f, 0f, 9.5f), 180f, 1f);
            if (dusk)
                for (var i = 0; i < 6; i++)
                    ArtLibrary.Box(_t, "Tower", P(-22f + i * 9f, 7f + (i % 3) * 3f, 30f), new Vector3(3.5f, 14f + (i % 3) * 6f, 3f), M("#6E78A8", 0.3f), false);
            Place("cafe_set", P(-2.6f, 0f, 2.6f), 160f, 1f);
            Place("cafe_set", P(2.8f, 0f, 3.2f), 200f, 1f);
            Place("lamp_post", P(-4.6f, 0f, 3.6f), 0f, 1f);
            Place("lamp_post", P(4.6f, 0f, 4.2f), 0f, 1f);
            StringLights(P(-4.6f, 2.7f, 3.6f), P(4.6f, 2.7f, 4.2f), 18, 0.5f);
            Model("fountain", P(0.4f, 0f, 6.2f), 1.6f);
            if (DestId == "bakery")
            {
                Model("croissant", P(-2.6f, 0.78f, 2.6f), 2.2f, 30f);
                Model("cake", P(2.8f, 0.78f, 3.2f), 1.6f);
                Place("sandwich_board", P(-4.2f, 0f, 1.6f), 150f, 1f);
            }
            if (DestId == "toyRoom")
            {
                var colors = new[] { "#FF6F61", "#4FA3D9", "#F2C230", "#7BC67E", "#B98BE0" };
                for (var i = 0; i < 9; i++)
                    ArtLibrary.Box(_t, "Block", P(-4f + (i % 3) * 0.5f, 0.2f + (i / 3) * 0.4f, 1.4f), Vector3.one * 0.38f, M(colors[i % colors.Length], 0.4f));
                for (var i = 0; i < 4; i++)
                {
                    var b = P(3.6f + i * 0.35f, 2.4f + (i % 2) * 0.35f, 2f);
                    ArtLibrary.Sphere(_t, "Balloon", b, 0.26f, M(colors[(i + 1) % colors.Length], 0.6f));
                    ArtLibrary.Cylinder(_t, "String", b + Vector3.down * 1.1f, 0.006f, 1.9f, M("#FFFFFF"), false);
                }
            }
        }

        // ── space ──

        private void BuildSpace()
        {
            SkyWall("#05070F", "#1B2350");
            Ground("concrete", ArtLibrary.Hex("#CEC8DC"), true, -9f, 40f);
            // earthshine: a soft cool light over the landing spot so the visitors are not just silhouettes
            var shine = new GameObject("Earthshine").AddComponent<Light>();
            shine.transform.SetParent(_t, false);
            shine.transform.position = P(0.5f, 3.2f, -1.2f);
            shine.type = LightType.Point;
            shine.color = ArtLibrary.Hex("#DDE6FF");
            shine.range = 9f;
            shine.intensity = 2.2f;
            shine.shadows = LightShadows.None;
            var rng = new System.Random(11);
            for (var i = 0; i < 140; i++)
                ArtLibrary.Sphere(_t, "Star", P((float)(rng.NextDouble() * 130 - 65), (float)(rng.NextDouble() * 8 + 1.5), 45.5f), (float)(rng.NextDouble() * 0.09 + 0.04), ArtLibrary.Glow(new Color(1.3f, 1.3f, 1.25f)), false);
            var earth = ArtLibrary.Sphere(_t, "Earth", P(-9f, 6.2f, 42f), 2.8f, ArtLibrary.Glow(ArtLibrary.Hex("#3F7FD8") * 1.1f), false);
            _spin.Add(earth.transform);
            for (var i = 0; i < 5; i++)
            {
                var land = ArtLibrary.Sphere(earth.transform, "Land", P(-9f, 6.2f, 42f), 1f, ArtLibrary.Glow(ArtLibrary.Hex("#5DBB63") * 1.1f), false);
                land.transform.SetParent(earth.transform, false);
                land.transform.localPosition = Quaternion.Euler(rng.Next(-50, 50), i * 72f, 0f) * Vector3.forward * 0.42f;
                land.transform.localScale = new Vector3(0.35f, 0.3f, 0.2f);
            }
            for (var i = 0; i < 7; i++)
            {
                var c = P((float)(rng.NextDouble() * 16 - 8), -0.04f, (float)(rng.NextDouble() * 12 + 1));
                var crater = ArtLibrary.Cylinder(_t, "Crater", c, 0.5f + (float)rng.NextDouble() * 0.7f, 0.04f, M("#9C96AE", 0.05f), false);
                crater.transform.localScale = new Vector3(crater.transform.localScale.x, 0.02f, crater.transform.localScale.z);
            }
            Model("meteor_detailed", P(-3.4f, 0f, 3.2f), 1.4f, 20f);
            Model("meteor", P(3.9f, 0f, 4.4f), 1.2f, 70f);
            // a small rocket with a round window
            var r0 = P(-2.4f, 0f, 3.6f);
            ArtLibrary.Cylinder(_t, "Body", r0 + Vector3.up * 1.2f, 0.42f, 2.4f, M("#F5F1EA", 0.4f));
            Cone("Nose", r0 + Vector3.up * 2.4f, 0.42f, 0.8f, M("#E2574C", 0.4f));
            ArtLibrary.Sphere(_t, "Window", r0 + new Vector3(0f, 1.65f, -0.38f), 0.16f, ArtLibrary.Glow(new Color(0.6f, 1.1f, 1.4f)), false);
            for (var i = 0; i < 3; i++)
            {
                var fin = ArtLibrary.Box(_t, "Fin", r0 + Quaternion.Euler(0f, i * 120f + 30f, 0f) * new Vector3(0f, 0.3f, 0.48f), new Vector3(0.06f, 0.6f, 0.4f), M("#E2574C", 0.4f));
                fin.transform.rotation = Quaternion.Euler(0f, i * 120f + 30f, 0f);
            }
            var flag = P(2.2f, 0f, 2.6f);
            ArtLibrary.Cylinder(_t, "FlagPole", flag + Vector3.up * 0.8f, 0.02f, 1.6f, M("#DDDDDD", 0.5f));
            ArtLibrary.Box(_t, "Flag", flag + new Vector3(0.3f, 1.4f, 0f), new Vector3(0.6f, 0.38f, 0.02f), M("#FF8FB1", 0.2f), false);
        }

        // ── the chest at the souvenir spot, the tap spots ──

        private void BuildChest()
        {
            var at = ChestSpot;
            var wood = M("#9A6037", 0.2f);
            var gold = M("#E8C36A", 0.6f);
            ArtLibrary.Box(_t, "Chest", at + Vector3.up * 0.16f, new Vector3(0.56f, 0.32f, 0.38f), wood);
            ArtLibrary.Box(_t, "ChestBand", at + new Vector3(0f, 0.16f, -0.192f), new Vector3(0.08f, 0.32f, 0.01f), gold, false);
            var lid = new GameObject("ChestLid").transform;
            lid.SetParent(_t, false);
            lid.position = at + new Vector3(0f, 0.32f, 0.19f);
            var lidBox = ArtLibrary.Box(_t, "Lid", lid.position + new Vector3(0f, 0.06f, -0.19f), new Vector3(0.58f, 0.12f, 0.4f), wood);
            lidBox.transform.SetParent(lid, true);
            _chestLid = lid;
        }

        private void BuildSpots()
        {
            for (var i = 0; i < SpotLocal.Length; i++)
            {
                var root = new GameObject("Spot" + i).transform;
                root.SetParent(_t, false);
                root.position = Origin + SpotLocal[i];
                switch (Archetyp)
                {
                    case Urlaub.Berge:
                        var petals = new[] { "#FF7BA9", "#B98BE0", "#FFD166", "#FF9F68", "#8FD3FF" }[i];
                        ArtLibrary.Cylinder(root, "Stem", root.position + Vector3.up * 0.14f, 0.015f, 0.28f, M("#4E9A3C"), false).transform.SetParent(root, true);
                        for (var k = 0; k < 5; k++)
                            ArtLibrary.Sphere(root, "Petal", root.position + new Vector3(Mathf.Cos(k * 1.256f) * 0.07f, 0.3f, Mathf.Sin(k * 1.256f) * 0.07f), 0.055f, M(petals), false).transform.SetParent(root, true);
                        ArtLibrary.Sphere(root, "Heart", root.position + Vector3.up * 0.3f, 0.04f, M("#FFE066"), false).transform.SetParent(root, true);
                        break;
                    case Urlaub.Weltraum:
                        var star = ArtLibrary.Sphere(root, "Splinter", root.position + Vector3.up * 0.12f, 0.09f, ArtLibrary.Glow(new Color(1.4f, 1.25f, 0.5f)), false);
                        star.transform.SetParent(root, true);
                        star.transform.localScale = new Vector3(0.14f, 0.22f, 0.14f);
                        break;
                    default:
                        var shell = ArtLibrary.Sphere(root, "Shell", root.position + Vector3.up * 0.05f, 0.14f, M(i % 2 == 0 ? "#FF9EBB" : "#FFF4E2", 0.6f), false);
                        shell.transform.SetParent(root, true);
                        shell.transform.localScale = new Vector3(0.32f, 0.11f, 0.26f);
                        for (var k = 0; k < 4; k++)
                        {
                            var rib = ArtLibrary.Box(root, "Rib", root.position + new Vector3((k - 1.5f) * 0.06f, 0.1f, 0f), new Vector3(0.012f, 0.012f, 0.22f), M("#E07B9B"), false);
                            rib.transform.SetParent(root, true);
                            rib.transform.localRotation = Quaternion.Euler(0f, (k - 1.5f) * 14f, 0f);
                        }
                        break;
                }
                var glint = ArtLibrary.Sphere(root, "Glint", root.position + Vector3.up * 0.42f, 0.045f, ArtLibrary.Glow(new Color(1.4f, 1.3f, 0.9f)), false);
                glint.transform.SetParent(root, true);
                _glints.Add(glint.transform);
                var col = root.gameObject.AddComponent<SphereCollider>();
                col.radius = 0.38f;
                col.center = Vector3.up * 0.15f;
                root.gameObject.AddComponent<VacationSpotMarker>().Index = i;
                root.gameObject.SetActive(false);
                _spots.Add(root);
            }
        }

        /// <summary>The mini activity starts: the five spots appear (the city has none — its gag is the café order).</summary>
        public void ShowSpots()
        {
            if (Archetyp == Urlaub.Stadt) return;
            foreach (var s in _spots)
                if (s != null) s.gameObject.SetActive(true);
        }

        public Vector3 SpotWorld(int i) => i >= 0 && i < _spots.Count ? _spots[i].position + Vector3.up * 0.25f : Origin;

        /// <summary>A found spot pops (sparkle) and goes.</summary>
        public void TakeSpot(int i, bool reduced)
        {
            if (i < 0 || i >= _spots.Count || _spots[i] == null || !_spots[i].gameObject.activeSelf) return;
            Effects.Sparkle(_spots[i].position + Vector3.up * 0.2f, 0.25f, reduced ? 6 : 18);
            _spots[i].gameObject.SetActive(false);
        }

        public bool SpotsVisible => _spots.Exists(s => s != null && s.gameObject.activeSelf);

        /// <summary>The chest opens for today's find.</summary>
        public void OpenChest(bool reduced)
        {
            _chestOpen = 1f;
            Effects.Sparkle(ChestSpot + Vector3.up * 0.5f, 0.3f, reduced ? 8 : 26);
        }

        /// <summary>Two holiday Goobys in their own holiday clothes hop about (spawned once the GLB is in).</summary>
        public async Task SpawnTouristsAsync()
        {
            (Vector3 at, float yaw, Dictionary<string, string> look)[] who =
            {
                (P(-3.2f, 0f, 3.6f), 150f, Look(0)),
                (P(3.3f, 0f, 2.6f), 210f, Look(1)),
            };
            foreach (var (at, yaw, look) in who)
            {
                var avatar = await GoobyAvatar.CreateAsync(_t, at);
                if (avatar == null || _destroyed) { if (avatar != null) Object.Destroy(avatar.gameObject); return; }
                avatar.transform.position = at;
                avatar.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                avatar.transform.localScale = Vector3.one * 0.9f;
                avatar.Wear(look);
                avatar.Play("idle-loop");
                avatar.SetEmotion("happy");
                _tourists.Add(avatar);
                _hopIn.Add(Random.Range(1.5f, 4f));
            }
        }

        private Dictionary<string, string> Look(int i)
        {
            switch (Archetyp)
            {
                case Urlaub.Berge: return i == 0 ? new Dictionary<string, string> { ["fell"] = "caramel", ["hut"] = "beanie", ["ruecken"] = "backpackTiny" } : new Dictionary<string, string> { ["fell"] = "fell_himmel", ["hals"] = "winterScarf" };
                case Urlaub.Stadt: return i == 0 ? new Dictionary<string, string> { ["fell"] = "rose", ["hut"] = "beret", ["hals"] = "scarfStriped" } : new Dictionary<string, string> { ["fell"] = "ash", ["hals"] = "bowtie" };
                case Urlaub.Weltraum: return new Dictionary<string, string> { ["fell"] = i == 0 ? "fell_lavendel" : "fell_minze", ["hut"] = "spaceHelm" };
                default: return i == 0 ? new Dictionary<string, string> { ["fell"] = "fell_pfirsich", ["hut"] = "strawHat", ["hals"] = "flowerLei" } : new Dictionary<string, string> { ["fell"] = "fell_minze", ["brille"] = "sunglasses", ["ruecken"] = "surfBoard" };
            }
        }

        /// <summary>The visited Gooby's own holiday clothes on top of his fur (hat, glasses, neck, back for this kind of trip).</summary>
        public Dictionary<string, string> GoobyHolidayLook(string fell)
        {
            var look = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(fell)) look["fell"] = fell;
            switch (DestId)
            {
                case "harbor": look["hut"] = "hut_kapitaen"; look["hals"] = "hals_muschel"; break;
                case "meadowTrip": look["hut"] = "flowerCrown"; look["ruecken"] = "backpackTiny"; break;
                case "spookGarden": look["hut"] = "hut_hexe"; look["ruecken"] = "backpackTiny"; break;
                case "nightSky": look["hut"] = "beanie"; look["hals"] = "winterScarf"; break;
                case "bigCity": look["hut"] = "beret"; look["brille"] = "sunglasses"; break;
                case "bakery": look["hut"] = "chefToque"; break;
                case "toyRoom": look["hut"] = "partyHat"; look["ruecken"] = "balloonRed"; break;
                case "space": look["hut"] = "spaceHelm"; break;
                default: look["hut"] = "strawHat"; look["brille"] = "sunglasses"; look["hals"] = "flowerLei"; break;
            }
            return look;
        }

        public void Tick(float dt, bool reduced)
        {
            _t0 += dt;
            for (var i = 0; i < _waves.Count; i++)
            {
                var w = _waves[i];
                if (w == null) continue;
                var phase = _t0 * 0.55f + i * 2.1f;
                w.position = new Vector3(w.position.x, -0.02f, Origin.z + 7.55f + i * 0.9f + (reduced ? 0f : Mathf.Sin(phase) * 0.35f));
            }
            foreach (var s in _spin)
                if (s != null) s.Rotate(Vector3.up, dt * 4f, Space.World);
            if (_fireLight != null && !reduced)
                _fireLight.intensity = _fireBase * (0.8f + 0.4f * Mathf.PerlinNoise(_t0 * 5f, 0.3f));
            for (var i = 0; i < _spots.Count; i++)
            {
                var s = _spots[i];
                if (s == null || !s.gameObject.activeSelf || reduced) continue;
                s.localRotation = Quaternion.Euler(0f, Mathf.Sin(_t0 * 1.3f + i) * 14f, 0f);
                if (i < _glints.Count && _glints[i] != null)
                {
                    var pulse = 0.6f + 0.5f * Mathf.Abs(Mathf.Sin(_t0 * 2.4f + i * 1.3f));
                    _glints[i].localScale = Vector3.one * 0.09f * pulse;
                    _glints[i].position = s.position + Vector3.up * (0.42f + Mathf.Sin(_t0 * 1.7f + i) * 0.05f);
                }
            }
            if (_chestOpen > 0f && _chestLid != null)
            {
                _chestOpen = Mathf.Max(0f, _chestOpen - dt * 0.12f);
                var open = _chestOpen > 0.05f ? Mathf.Min(1f, (1f - _chestOpen) * 6f) : Mathf.Max(0f, _chestOpen * 20f);
                _chestLid.localRotation = Quaternion.Euler(-70f * open, 0f, 0f);
            }
            for (var i = 0; i < _tourists.Count; i++)
            {
                var a = _tourists[i];
                if (a == null) continue;
                _hopIn[i] -= dt;
                if (_hopIn[i] > 0f) continue;
                _hopIn[i] = Random.Range(2.5f, 5.5f);
                a.Play(Random.value < 0.25f ? "wave" : "hop");
            }
        }

        public void Destroy()
        {
            _destroyed = true;
            if (Root != null) Object.Destroy(Root);
        }
    }
}
