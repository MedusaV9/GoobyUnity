using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The riding village Hufingen as a 3D diorama behind the village UI (a compact cut of hufingen_szene.gd): a sandy
    /// square with the fountain, the five shops in a half circle facing it (Quaternius barns like Godot's LAEDEN table),
    /// each with a counter, a coloured awning and its shopkeeper Gooby, the horse trader's corral in front of the open
    /// barn with today's three horses in their coats, bunting, the windmill and hills behind. You arrive riding: Gooby
    /// trots in on the selected horse along the road from the left, ties it to the hitching post and gets down. Taps go
    /// through <see cref="Pick"/> (shops, trader horses, the fountain).
    /// </summary>
    public sealed class RanchDorfStage
    {
        public const float ViewH = 720f;
        private const float ShopRadius = 12f, RiderScale = 0.72f, TrotSpeed = 3.4f, WalkSpeed = 1.6f;

        public enum Ziel { Nichts, Laden, Pferd, Brunnen }

        public sealed class Tap : MonoBehaviour
        {
            public Ziel Ziel;
            public string Id = "";
        }

        public static readonly Vector3 Plaza = new Vector3(0f, 0f, 7f);
        private const float PlazaRadius = 8.6f;
        public static readonly Vector3 HitchSpot = new Vector3(-4.6f, 0f, 2.2f);
        public static readonly Vector3 PlayerSpot = new Vector3(-2.4f, 0f, 1.3f);
        private static readonly Vector3[] RideInPath =
        {
            new Vector3(-26f, 0f, -1.2f), new Vector3(-14f, 0f, -0.2f), new Vector3(-8.2f, 0f, 1.4f), HitchSpot,
        };
        private static readonly Vector3[] CorralSpots = { new Vector3(-2.1f, 0f, 12.9f), new Vector3(0.1f, 0f, 13.8f), new Vector3(2.3f, 0f, 12.8f) };
        private static readonly float[] CorralYaw = { 115f, 95f, 70f };
        private static readonly Vector3 BuyerSpot = HitchSpot + new Vector3(-0.4f, 0f, 1.7f);

        private static readonly (string id, string glb, float scale, float theta)[] Shops =
        {
            (RanchDorf.Reitladen, "scheune_klein", 0.75f, -64f),
            (RanchDorf.Futterhof, "silo_haus", 0.9f, -32f),
            (RanchDorf.Pferdehaendlerin, "scheune_offen", 0.8f, 0f),
            (RanchDorf.Schmiede, "scheune", 0.58f, 32f),
            (RanchDorf.Moebelscheune, "scheune_gross", 0.6f, 64f),
        };

        public static readonly Dictionary<string, string> ShopColor = new Dictionary<string, string>
        {
            [RanchDorf.Reitladen] = "#E98CA0", [RanchDorf.Futterhof] = "#F2C14E", [RanchDorf.Pferdehaendlerin] = "#F29A3A",
            [RanchDorf.Schmiede] = "#7FA7D9", [RanchDorf.Moebelscheune] = "#8FC98A",
        };

        private static readonly Dictionary<string, Dictionary<string, string>> KeeperLook = new Dictionary<string, Dictionary<string, string>>
        {
            [RanchDorf.Reitladen] = new Dictionary<string, string> { ["fell"] = "rose", ["hut"] = "cap" },
            [RanchDorf.Futterhof] = new Dictionary<string, string> { ["fell"] = "caramel", ["hut"] = "strawHat" },
            [RanchDorf.Pferdehaendlerin] = new Dictionary<string, string> { ["fell"] = "fell_himmel", ["hut"] = "strawHat", ["hals"] = "scarfRed" },
            [RanchDorf.Schmiede] = new Dictionary<string, string> { ["fell"] = "ash", ["hut"] = "beanie" },
            [RanchDorf.Moebelscheune] = new Dictionary<string, string> { ["fell"] = "ash", ["hut"] = "beret", ["hals"] = "bowtie" },
        };

        public MgStage3D Stage { get; }
        public GoobyAvatar Gooby { get; private set; }
        public bool Riding => _ride != null && _ride.Path.Count > 0;

        /// <summary>The horse Gooby rode in on (tied to the post after arriving).</summary>
        public RanchHorse3D RideView => _ride?.View;
        public event Action Arrived;

        private sealed class Walker
        {
            public string Id;
            public RanchHorse3D View;
            public Vector3 Pos;
            public float Yaw, EndYaw, Speed = WalkSpeed;
            public readonly List<Vector3> Path = new List<Vector3>();
            public Action Done;
        }

        private readonly Transform _root;
        private readonly bool _night;
        private readonly Dictionary<string, Walker> _offer = new Dictionary<string, Walker>();
        private readonly Dictionary<string, GoobyAvatar> _keepers = new Dictionary<string, GoobyAvatar>();
        private readonly List<Walker> _bought = new List<Walker>();
        private Walker _ride;
        private Transform _fountain;
        private Vector3 _camFrom, _camTo;
        private float _camT = -1f, _t, _goobyBeat;
        private bool _destroyed, _mounted;

        private static Vector3 At(float x, float y, float z) => MgStage3D.At(x, y, z);
        private static Vector3 At(Vector3 local) => MgStage3D.Origin + local;
        private static Material M(string hex, float smooth = 0.25f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);
        private static string Model(string name) => Path.Combine(AssetLoader.Root, "ranch", name + ".glb");

        /// <summary>The square seen from the front (where the camera rests after the ride-in).</summary>
        private static readonly Vector3 SquareCam = new Vector3(0.4f, 6.6f, -7.6f);
        private const float SquarePitch = 22f, SquareFov = 42f;

        public RanchDorfStage(UnityEngine.UIElements.VisualElement field, Dictionary<string, string> outfit, bool night)
        {
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            _night = night;
            Stage = new MgStage3D(ViewH * aspect, ViewH, ArtLibrary.Hex(night ? "#3A4A78" : "#C4E6F8"));
            Stage.Bind(field);
            _root = Stage.Root.transform;
            Aim(SquareCam, SquarePitch, SquareFov);
            BuildLand();
            BuildShops();
            BuildSquare();
            if (night) BuildLanterns();
            if (_night) MgStudio.Night();
            _ = SpawnGoobyAsync(outfit);
        }

        public void SetVisible(bool on)
        {
            Stage.Park(!on);
            if (!_night) return;
            if (on) MgStudio.Night();
            else MgStudio.Day();
        }

        public static Vector3 ShopPos(string id)
        {
            foreach (var s in Shops)
                if (s.id == id) return Plaza + new Vector3(Mathf.Sin(s.theta * Mathf.Deg2Rad), 0f, Mathf.Cos(s.theta * Mathf.Deg2Rad)) * ShopRadius;
            return Plaza;
        }

        private static float ShopTheta(string id)
        {
            foreach (var s in Shops) if (s.id == id) return s.theta;
            return 0f;
        }

        /// <summary>Where a shop's name tag floats (over its counter).</summary>
        public static Vector3 TagPoint(string id) => id == RanchDorf.Pferdehaendlerin ? new Vector3(0f, 3.4f, 15.6f) : Counter(id) + Vector3.up * 3.1f;

        private static Vector3 Counter(string id) => Plaza + (ShopPos(id) - Plaza).normalized * (ShopRadius - 5.4f);

        public Vector2 ToField(Vector3 local, float fieldW, float fieldH) => Stage.ToField(At(local), fieldW, fieldH);

        // ── the land ──

        private void BuildLand()
        {
            Stage.Ground(0f, ArtLibrary.Hex(_night ? "#6F9A6A" : "#A6D489"), "grass", 120f, 90f, 22f);
            var plaza = ArtLibrary.Cylinder(_root, "Plaza", At(Plaza.x, 0.01f, Plaza.z), PlazaRadius, 0.02f, M("#E6D2A2", 0.05f), false);
            plaza.name = "Plaza";
            ArtLibrary.Cylinder(_root, "PlazaRim", At(Plaza.x, 0.008f, Plaza.z), PlazaRadius + 0.5f, 0.016f, M("#CDB582", 0.05f), false);
            foreach (var (a, b) in new[] { (RideInPath[0], RideInPath[1]), (RideInPath[1], RideInPath[2]), (RideInPath[2], HitchSpot + new Vector3(-0.6f, 0f, -0.2f)) })
            {
                var len = Vector3.Distance(a, b);
                var road = ArtLibrary.Box(_root, "Road", At((a + b) * 0.5f + Vector3.up * 0.012f), new Vector3(2.8f, 0.02f, len + 1.2f), M("#E2CC98", 0.05f), false);
                road.transform.rotation = Quaternion.LookRotation((b - a).normalized);
            }
            Stage.Hills(0f, 44f, 8, ArtLibrary.Hex(_night ? "#5E7C70" : "#9CC98C"), 8f, 120f, 0f, 15);
            Stage.Hills(0f, 58f, 6, ArtLibrary.Hex(_night ? "#4E6670" : "#B9DDB0"), 12f, 150f, 6f, 16);
            Stage.Row("tree_round_big", 0f, 30f, -30f, 30f, 10, 1.6f, 0f, 2.5f, 17);
            Stage.Row("tree_poplar", 0f, 35f, -34f, 34f, 8, 1.5f, 0f, 3f, 18);
            Stage.Clouds(5, 14f, 50f, 80f, 2.4f, 6);
            if (!_night) Stage.Sun(new Vector3(26f, 24f, 64f), 3.2f);
            else Stage.Sun(new Vector3(-22f, 22f, 64f), 2.2f, new Color(1.1f, 1.08f, 0.95f));
            Stage.Meadow(0f, -26f, -11f, -8f, 4f, 60, 14, 25);
            Stage.Meadow(0f, 11f, 26f, -8f, 6f, 60, 14, 26);
            Stage.Glb(Model("windmuehle"), At(19f, 0f, 27f), 0.75f, -30f);
            Stage.Glb(Model("silo"), At(-19f, 0f, 24f), 0.62f, 10f);
            foreach (var (x, z, s) in new[] { (-16.5f, 6f, 3.4f), (16.8f, 7.5f, 3.6f), (-13.5f, 16f, 3.2f), (14f, 18f, 3.3f) })
                _ = TintWhenLoaded(Stage.Glb(Model("tree_oak"), At(x, 0f, z), s, x * 9f), ArtLibrary.Hex("#7DBE63"), "leaf");
            foreach (var (x, z) in new[] { (-9.6f, -0.6f), (9.4f, -0.4f), (-11.2f, 4.8f), (11.4f, 4.4f) })
                _ = TintWhenLoaded(Stage.Glb(Model("plant_bushLarge"), At(x, 0f, z), 3.6f, x * 13f), ArtLibrary.Hex("#7FBF5E"));
            var flowers = new[] { "flower_redA", "flower_yellowA", "flower_purpleA" };
            for (var i = 0; i < 14; i++)
            {
                var a = i * 0.45f + 0.2f;
                var p = Plaza + new Vector3(Mathf.Cos(a + Mathf.PI) * (PlazaRadius + 0.9f), 0f, Mathf.Sin(a + Mathf.PI) * (PlazaRadius + 0.9f));
                if (p.z > 3.5f || Mathf.Abs(p.x - RideInPath[2].x) < 2.5f) continue;
                Stage.Glb(Model(flowers[i % 3]), At(p), 2.4f, i * 40f);
            }
        }

        private void BuildSquare()
        {
            _fountain = Stage.Glb(Model("brunnen"), At(Plaza), 1.05f, 20f);
            _fountain.name = "Fountain";
            AddTap(_fountain.gameObject, Ziel.Brunnen, "", new Vector3(0f, 1f, 0f), new Vector3(2.4f, 2.4f, 2.4f));
            // the hitching post the player's horse is tied to, and two benches
            var wood = M("#B98A5E", 0.2f);
            foreach (var dx in new[] { -1.3f, 1.3f })
                ArtLibrary.Box(_root, "HitchPost", At(HitchSpot.x + dx, 0.55f, HitchSpot.z + 0.85f), new Vector3(0.13f, 1.1f, 0.13f), wood);
            ArtLibrary.Box(_root, "HitchRail", At(HitchSpot.x, 1.0f, HitchSpot.z + 0.85f), new Vector3(2.8f, 0.09f, 0.09f), wood);
            foreach (var (x, z, yaw) in new[] { (4.4f, 2.4f, -25f), (-2.2f, 9.9f, 160f) })
            {
                var bench = new GameObject("Bench").transform;
                bench.SetParent(_root, false);
                bench.position = At(x, 0f, z);
                bench.rotation = Quaternion.Euler(0f, yaw, 0f);
                ArtLibrary.Box(bench, "Seat", bench.TransformPoint(new Vector3(0f, 0.45f, 0f)), new Vector3(1.6f, 0.08f, 0.45f), wood).transform.rotation = bench.rotation;
                ArtLibrary.Box(bench, "Back", bench.TransformPoint(new Vector3(0f, 0.75f, 0.2f)), new Vector3(1.6f, 0.4f, 0.06f), wood).transform.rotation = bench.rotation;
                foreach (var lx in new[] { -0.65f, 0.65f })
                    ArtLibrary.Box(bench, "Leg", bench.TransformPoint(new Vector3(lx, 0.22f, 0f)), new Vector3(0.08f, 0.44f, 0.4f), wood).transform.rotation = bench.rotation;
            }
            // the trader's corral in front of the open barn
            var white = M("#FFF8EE", 0.3f);
            const float x0 = -3.6f, x1 = 3.6f, z0 = 11.4f, z1 = 15.2f;
            foreach (var (a, b) in new[] { (new Vector3(x0, 0f, z0), new Vector3(-1.1f, 0f, z0)), (new Vector3(1.1f, 0f, z0), new Vector3(x1, 0f, z0)), (new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1)), (new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1)) })
            {
                var len = Vector3.Distance(a, b);
                var posts = Mathf.Max(1, Mathf.RoundToInt(len / 1.3f));
                for (var i = 0; i <= posts; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)posts);
                    ArtLibrary.Box(_root, "CorralPost", At(p.x, 0.5f, p.z), new Vector3(0.12f, 1.0f, 0.12f), white);
                }
                foreach (var y in new[] { 0.45f, 0.85f })
                {
                    var rail = ArtLibrary.Box(_root, "CorralRail", At((a + b) * 0.5f + Vector3.up * y), new Vector3(0.06f, 0.08f, len), white);
                    rail.transform.rotation = Quaternion.LookRotation((b - a).normalized);
                }
            }
            ArtLibrary.Box(_root, "CorralSand", At(0f, 0.014f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.02f, z1 - z0), M("#D9C597", 0.04f), false);
            // bunting from shop to shop over the square
            for (var i = 0; i + 1 < Shops.Length; i++)
                Bunting(Counter(Shops[i].id) + Vector3.up * 3.3f, Counter(Shops[i + 1].id) + Vector3.up * 3.3f, i);
        }

        private void Bunting(Vector3 a, Vector3 b, int seed)
        {
            var colors = new[] { "#F28CA0", "#F2C14E", "#8FC98A", "#7FA7D9", "#F29A3A" };
            var len = Vector3.Distance(a, b);
            var line = ArtLibrary.Box(_root, "BuntingLine", At((a + b) * 0.5f + Vector3.down * 0.25f), new Vector3(0.025f, 0.025f, len), M("#8A6A45", 0.2f), false);
            line.transform.rotation = Quaternion.LookRotation((b - a).normalized);
            var n = Mathf.Max(3, Mathf.RoundToInt(len / 0.7f));
            for (var i = 1; i < n; i++)
            {
                var k = i / (float)n;
                var p = Vector3.Lerp(a, b, k) + Vector3.down * (0.25f + Mathf.Sin(k * Mathf.PI) * 0.45f);
                var flag = ArtLibrary.Box(_root, "Pennant", At(p + Vector3.down * 0.18f), new Vector3(0.26f, 0.26f, 0.02f), M(colors[(i + seed) % colors.Length], 0.3f), false);
                flag.transform.rotation = Quaternion.LookRotation((b - a).normalized) * Quaternion.Euler(0f, 90f, 45f);
            }
        }

        // ── the shops ──

        private void BuildShops()
        {
            foreach (var (id, glb, scale, theta) in Shops)
            {
                var pos = ShopPos(id);
                var house = Stage.Glb(Model(glb), At(pos), scale, theta);
                house.name = "Shop:" + id;
                AddTap(house.gameObject, Ziel.Laden, id, new Vector3(0f, 2.6f, 0f) / scale, new Vector3(6f, 5.2f, 6f) / scale);
                var colour = ShopColor[id];
                if (id == RanchDorf.Pferdehaendlerin)
                {
                    SpawnKeeper(id, new Vector3(-4.5f, 0f, 11.6f), 141f);
                    var sign = ArtLibrary.Box(_root, "TraderSign", At(4.3f, 1.25f, 11.25f), new Vector3(1.1f, 0.45f, 0.06f), M(colour, 0.3f));
                    sign.transform.rotation = Quaternion.Euler(0f, 0f, 3f);
                    ArtLibrary.Box(_root, "TraderSignPost", At(4.3f, 0.5f, 11.3f), new Vector3(0.1f, 1.0f, 0.1f), M("#B98A5E", 0.2f));
                    continue;
                }
                var c = Counter(id);
                var face = Quaternion.Euler(0f, theta, 0f);
                var counter = ArtLibrary.Box(_root, "Counter:" + id, At(c + Vector3.up * 0.48f), new Vector3(2.2f, 0.96f, 0.7f), M("#C99A6B", 0.2f));
                counter.transform.rotation = face;
                var top = ArtLibrary.Box(_root, "CounterTop", At(c + Vector3.up * 0.99f), new Vector3(2.4f, 0.08f, 0.85f), M("#E8D6AA", 0.2f));
                top.transform.rotation = face;
                var stripe = ArtLibrary.Box(_root, "CounterStripe", At(c + face * new Vector3(0f, 0.62f, -0.36f)), new Vector3(2.21f, 0.18f, 0.02f), M(colour, 0.3f), false);
                stripe.transform.rotation = face;
                // a striped awning on two posts behind the counter
                foreach (var dx in new[] { -1.05f, 1.05f })
                    ArtLibrary.Box(_root, "AwningPost", At(c + face * new Vector3(dx, 1.25f, 0.95f)), new Vector3(0.09f, 2.5f, 0.09f), M("#FFF8EE", 0.3f)).transform.rotation = face;
                for (var k = 0; k < 6; k++)
                {
                    var strip = ArtLibrary.Box(_root, "Awning", At(c + face * new Vector3(-1.1f + 0.44f * k + 0.22f, 2.42f, 0.45f)), new Vector3(0.44f, 0.05f, 1.15f), M(k % 2 == 0 ? colour : "#FFF8EE", 0.3f));
                    strip.transform.rotation = face * Quaternion.Euler(-14f, 0f, 0f);
                }
                Wares(id, c, face);
                SpawnKeeper(id, c + face * new Vector3(0.2f, 0f, 0.95f), theta + 180f);
            }
        }

        /// <summary>A few wares on each counter so the shops read from afar.</summary>
        private void Wares(string id, Vector3 c, Quaternion face)
        {
            Vector3 P(float x, float y, float z) => At(c + face * new Vector3(x, 1.03f + y, z));
            switch (id)
            {
                case RanchDorf.Reitladen:
                    Stage.Glb(Model("sattel"), P(-0.55f, 0f, 0f), 1.1f, ShopTheta(id) + 90f);
                    ArtLibrary.Box(_root, "Blanket", P(0.55f, 0.06f, 0f), new Vector3(0.6f, 0.12f, 0.45f), M("#7FA7D9", 0.2f)).transform.rotation = face;
                    break;
                case RanchDorf.Futterhof:
                    Stage.Glb(Model("heuballen"), P(-0.6f, 0f, 0f), 0.55f, ShopTheta(id) + 90f);
                    ArtLibrary.Box(_root, "OatSack", P(0.35f, 0.2f, 0f), new Vector3(0.36f, 0.42f, 0.3f), M("#E8D6AA", 0.1f)).transform.rotation = face;
                    ArtLibrary.Sphere(_root, "Apple", P(0.85f, 0.08f, 0.05f), 0.09f, M("#E8574C", 0.5f));
                    ArtLibrary.Sphere(_root, "Apple", P(0.95f, 0.08f, -0.12f), 0.09f, M("#E8574C", 0.5f));
                    break;
                case RanchDorf.Schmiede:
                    ArtLibrary.Box(_root, "Anvil", P(-0.4f, 0.15f, 0f), new Vector3(0.6f, 0.3f, 0.3f), M("#5D6470", 0.6f)).transform.rotation = face;
                    foreach (var (x, hex) in new[] { (0.3f, "#C98A4E"), (0.62f, "#D8DCE4"), (0.94f, "#F2C230") })
                        ArtLibrary.Cylinder(_root, "Shoe", P(x, 0.02f, 0f), 0.13f, 0.04f, M(hex, 0.7f));
                    break;
                case RanchDorf.Moebelscheune:
                    ArtLibrary.Box(_root, "Chair", P(-0.5f, 0.22f, 0f), new Vector3(0.42f, 0.44f, 0.42f), M("#8FC98A", 0.2f)).transform.rotation = face;
                    ArtLibrary.Cylinder(_root, "Lamp", P(0.45f, 0.3f, 0f), 0.05f, 0.6f, M("#5D6470", 0.4f));
                    ArtLibrary.Sphere(_root, "LampShade", P(0.45f, 0.62f, 0f), 0.16f, M("#FFF1C9", 0.3f));
                    break;
            }
        }

        /// <summary>A shopkeeper Gooby at a stage spot, turned to <paramref name="yaw"/> (he looks down his own +Z).</summary>
        private void SpawnKeeper(string id, Vector3 local, float yaw) => _ = SpawnKeeperAsync(id, local, yaw);

        private async Task SpawnKeeperAsync(string id, Vector3 local, float yaw)
        {
            var avatar = await GoobyAvatar.CreateAsync(_root, At(local));
            if (avatar == null || _destroyed) return;
            avatar.transform.position = At(local);
            avatar.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            avatar.transform.localScale = Vector3.one * 0.95f;
            avatar.Wear(KeeperLook[id]);
            avatar.Play("idle-loop");
            avatar.SetEmotion("happy");
            _keepers[id] = avatar;
        }

        private void BuildLanterns()
        {
            foreach (var (pos, range) in new[] { (Plaza + new Vector3(-6.5f, 2.4f, 1.5f), 9f), (Plaza + new Vector3(6.5f, 2.4f, 1.5f), 9f), (new Vector3(0f, 2.6f, 11f), 8f) })
            {
                ArtLibrary.Box(_root, "LanternPost", At(pos.x, pos.y * 0.5f, pos.z), new Vector3(0.1f, pos.y, 0.1f), M("#5D4A3A", 0.3f));
                var glow = ArtLibrary.Sphere(_root, "Lantern", At(pos + Vector3.up * 0.12f), 0.16f, ArtLibrary.Glow(new Color(1.4f, 1.05f, 0.55f)), false);
                var light = new GameObject("LanternLight").AddComponent<Light>();
                light.transform.SetParent(glow.transform, false);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.8f, 0.5f);
                light.range = range;
                light.intensity = 2.4f;
                light.shadows = LightShadows.None;
            }
        }

        private async Task SpawnGoobyAsync(Dictionary<string, string> outfit)
        {
            var avatar = await GoobyAvatar.CreateAsync(_root, At(PlayerSpot));
            if (avatar == null || _destroyed) return;
            avatar.transform.position = At(PlayerSpot);
            avatar.transform.rotation = Quaternion.Euler(0f, 160f, 0f);
            if (outfit != null) avatar.Wear(outfit);
            avatar.Play("idle-loop");
            avatar.SetEmotion("happy");
            Gooby = avatar;
            if (_ride != null && Riding) MountGooby();
        }

        private static async Task TintWhenLoaded(Transform anchor, Color c, string only = null)
        {
            for (var i = 0; i < 120 && anchor != null && anchor.GetComponentInChildren<Renderer>() == null; i++) await Task.Delay(50);
            if (anchor == null) return;
            foreach (var r in anchor.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (only != null && m.name.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", c);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                }
                r.materials = mats;
            }
        }

        private void AddTap(GameObject go, Ziel ziel, string id, Vector3 centre, Vector3 size)
        {
            var col = go.AddComponent<BoxCollider>();
            col.center = centre;
            col.size = size;
            var tap = go.AddComponent<Tap>();
            tap.Ziel = ziel;
            tap.Id = id;
        }

        // ── arriving on horseback ──

        /// <summary>
        /// Gooby trots in on his horse along the road (the first visit takes the long way with the camera riding along),
        /// ties it up at the post and gets down; <see cref="Arrived"/> fires at the post.
        /// </summary>
        public void RideIn(string id, Color fell, Color maehne, bool lang)
        {
            var view = RanchHorse3D.Create(_root, "Ride:" + id, fell, maehne);
            var start = lang ? 0 : 2;
            _ride = new Walker { Id = id, View = view, Pos = RideInPath[start] + (lang ? Vector3.zero : new Vector3(-3f, 0f, -0.6f)), Speed = TrotSpeed, EndYaw = 90f };
            for (var i = start + 1; i < RideInPath.Length; i++) _ride.Path.Add(RideInPath[i]);
            _ride.Yaw = 90f;
            _ride.Done = Dismount;
            view.transform.SetPositionAndRotation(At(_ride.Pos), Quaternion.Euler(0f, _ride.Yaw, 0f));
            view.Hufe = "sand";
            view.Gait("trab", 1f);
            if (Gooby != null) MountGooby();
            if (lang) FollowCam(true);
        }

        private void MountGooby()
        {
            if (Gooby == null || _ride == null || _mounted) return;
            _mounted = true;
            Gooby.transform.localScale = Vector3.one * RiderScale;
            _ride.View.Mount(Gooby.transform);
            Gooby.Play("sit-loop", 0f);
        }

        private void Dismount()
        {
            if (_ride?.View != null) _ride.View.Mount(null);
            _ride?.View?.Action("kopfschuetteln");
            if (Gooby != null)
            {
                Gooby.transform.localScale = Vector3.one;
                Gooby.transform.SetPositionAndRotation(At(PlayerSpot), Quaternion.Euler(0f, 160f, 0f));
                Gooby.Play("wave");
                _goobyBeat = 1.4f;
            }
            _mounted = false;
            Effects.Puff(At(PlayerSpot + Vector3.up * 0.1f), new Color(0.9f, 0.82f, 0.6f, 0.9f), 8, 0.08f, 1.0f, 0.5f, Vector3.up, 0.9f);
            GlideCam(SquareCam, 1.4f);
            Arrived?.Invoke();
        }

        private const float RidePitch = 14f, RideFov = 38f;
        private Vector3 _camPos = SquareCam;
        private float _camPitch = SquarePitch, _camFov = SquareFov, _camPitchFrom, _camFovFrom, _camDur = 1f;

        private void Aim(Vector3 local, float pitch, float fov)
        {
            _camPos = local;
            _camPitch = pitch;
            _camFov = fov;
            Stage.Aim(local, pitch, fov);
        }

        /// <summary>The camera rides along beside and behind the horse (eased, a snap for the first frame).</summary>
        private void FollowCam(bool snap)
        {
            if (_ride == null) return;
            var want = _ride.Pos + new Vector3(1.8f, 3.4f, -8.4f);
            Aim(snap ? want : Vector3.Lerp(_camPos, want, 0.12f), RidePitch, RideFov);
            _following = true;
        }

        private void GlideCam(Vector3 to, float seconds)
        {
            _following = false;
            _camFrom = _camPos;
            _camPitchFrom = _camPitch;
            _camFovFrom = _camFov;
            _camTo = to;
            _camT = 0f;
            _camDur = Mathf.Max(0.1f, seconds);
        }

        private bool _following;

        // ── the trader's horses ──

        /// <summary>Today's offer stands in the corral (horses no longer on offer walk away, new ones appear).</summary>
        public void SetOffer(IReadOnlyList<(string id, Color fell, Color maehne)> horses)
        {
            var keep = new HashSet<string>();
            for (var i = 0; i < horses.Count && i < CorralSpots.Length; i++)
            {
                var (id, fell, maehne) = horses[i];
                keep.Add(id);
                if (_offer.ContainsKey(id)) continue;
                var view = RanchHorse3D.Create(_root, "Offer:" + id, fell, maehne);
                var w = new Walker { Id = id, View = view, Pos = CorralSpots[i], Yaw = CorralYaw[i], EndYaw = CorralYaw[i] };
                view.transform.SetPositionAndRotation(At(w.Pos), Quaternion.Euler(0f, w.Yaw, 0f));
                AddTap(view.gameObject, Ziel.Pferd, id, new Vector3(0f, 1.1f, 0f), new Vector3(0.9f, 2.0f, 2.4f));
                _offer[id] = w;
            }
            foreach (var id in new List<string>(_offer.Keys))
            {
                if (keep.Contains(id)) continue;
                if (_offer[id].View != null) UnityEngine.Object.Destroy(_offer[id].View.gameObject);
                _offer.Remove(id);
            }
        }

        public Vector3 OfferTop(string id) => _offer.TryGetValue(id ?? "", out var w) ? w.Pos + Vector3.up * 2.1f : new Vector3(0f, 2.4f, 13f);

        /// <summary>A bought horse leaves the corral through its gate and joins yours at the hitching post.</summary>
        public void Bought(string id)
        {
            if (!_offer.TryGetValue(id ?? "", out var w)) return;
            _offer.Remove(id);
            if (w.View != null && w.View.TryGetComponent<Tap>(out var tap)) tap.Ziel = Ziel.Nichts;
            w.View?.Action("kopfschuetteln");
            w.Path.Add(new Vector3(0f, 0f, 12.2f));
            w.Path.Add(new Vector3(0f, 0f, 10.2f));
            w.Path.Add(new Vector3(-3.6f, 0f, 4.4f));
            w.Path.Add(BuyerSpot + new Vector3(-0.8f * _bought.Count, 0f, 0.6f * _bought.Count));
            w.EndYaw = 100f;
            w.View?.Gait("schritt", 1f);
            _bought.Add(w);
            Cheer(RanchDorf.Pferdehaendlerin);
            GoobyBeat("celebrate");
        }

        // ── people ──

        public void Greet(string shopId)
        {
            if (_keepers.TryGetValue(shopId ?? "", out var k) && k != null) k.Play("wave");
        }

        public void Cheer(string shopId)
        {
            if (_keepers.TryGetValue(shopId ?? "", out var k) && k != null) k.Play("celebrate");
            GoobyBeat("hop");
        }

        public void Wish()
        {
            if (_fountain == null) return;
            var top = _fountain.position + Vector3.up * 1.6f;
            Effects.Puff(top, new Color(0.62f, 0.84f, 1f, 0.9f), 12, 0.07f, 1.4f, 0.7f, Vector3.up, 0.7f);
            Effects.Sparkle(top, 0.6f, 16);
            GoobyBeat("hop");
        }

        private void GoobyBeat(string clip)
        {
            if (Gooby == null || _mounted) return;
            Gooby.Play(clip);
            _goobyBeat = clip == "hop" ? 0.7f : 1.6f;
        }

        // ── taps and time ──

        public (Ziel ziel, string id) Pick(Vector2 fieldPoint, float fieldW, float fieldH)
        {
            if (Stage.Camera == null || fieldW <= 0f || fieldH <= 0f) return (Ziel.Nichts, "");
            Physics.SyncTransforms();
            var ray = Stage.Camera.ViewportPointToRay(new Vector3(fieldPoint.x / fieldW, 1f - fieldPoint.y / fieldH, 0f));
            Tap best = null;
            var bestD = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, 160f))
            {
                var tap = hit.collider != null ? hit.collider.GetComponent<Tap>() : null;
                if (tap == null || tap.Ziel == Ziel.Nichts || hit.distance >= bestD) continue;
                best = tap;
                bestD = hit.distance;
            }
            return best == null ? (Ziel.Nichts, "") : (best.Ziel, best.Id);
        }

        private static void Step(Walker w, float dt)
        {
            if (w?.View == null) return;
            if (w.Path.Count > 0)
            {
                var d = w.Path[0] - w.Pos;
                d.y = 0f;
                var step = w.Speed * dt;
                if (d.magnitude <= step)
                {
                    w.Pos = w.Path[0];
                    w.Path.RemoveAt(0);
                    if (w.Path.Count == 0)
                    {
                        w.View.Gait("stand");
                        var cb = w.Done;
                        w.Done = null;
                        cb?.Invoke();
                    }
                }
                else
                {
                    w.Pos += d.normalized * step;
                    w.Yaw = Mathf.MoveTowardsAngle(w.Yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 200f * dt);
                }
            }
            else w.Yaw = Mathf.MoveTowardsAngle(w.Yaw, w.EndYaw, 140f * dt);
            w.View.transform.SetPositionAndRotation(At(w.Pos), Quaternion.Euler(0f, w.Yaw, 0f));
        }

        public void Tick(float dt, bool reduced)
        {
            if (_destroyed || Stage.Parked) return;
            _t += dt;
            Step(_ride, dt);
            if (_following && Riding) FollowCam(false);
            foreach (var w in _bought) Step(w, dt);
            foreach (var w in _offer.Values) Step(w, dt);
            if (_camT >= 0f && Stage.Camera != null)
            {
                _camT = Mathf.Min(1f, _camT + dt / _camDur);
                var k = Mathf.SmoothStep(0f, 1f, _camT);
                Aim(Vector3.Lerp(_camFrom, _camTo, k), Mathf.Lerp(_camPitchFrom, SquarePitch, k), Mathf.Lerp(_camFovFrom, SquareFov, k));
                if (_camT >= 1f) _camT = -1f;
            }
            if (_goobyBeat > 0f && (_goobyBeat -= dt) <= 0f && Gooby != null && !_mounted) Gooby.Play("idle-loop");
            if (!reduced && Mathf.Repeat(_t, 9f) < dt)
            {
                foreach (var w in _offer.Values)
                    if (UnityEngine.Random.value < 0.4f) w.View?.Action(UnityEngine.Random.value < 0.5f ? "kopfschuetteln" : "fressen-loop", 1.6f);
            }
        }

        public void Destroy()
        {
            _destroyed = true;
            if (_night) MgStudio.Day();
            Stage.Destroy();
        }
    }
}
