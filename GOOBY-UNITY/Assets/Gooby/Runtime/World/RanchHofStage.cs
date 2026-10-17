using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The Gooby ranch farm as a 3D diorama behind the farm UI (a compact cut of ranch_hof_scene.gd's farm): the
    /// grooming spot in front with the selected horse side-on and the player's Gooby at its head, the trough and the hay
    /// stack (one bale per bale in the store), the white paddock behind with the other horses, three apple trees whose
    /// apples hang there when ripe, the hay meadow (tall and golden when it can be mown, stubble after), the open
    /// stable, the barn, silo, water tower and windmill, cows and sheep on the back pasture and Kiki at her coop. The
    /// screen taps through <see cref="Pick"/> (horses, trees, meadow, trough, stable, hay stack). Horses walk between the
    /// paddock and the grooming spot, eat, drink, shake their heads and sleep at night.
    /// </summary>
    public sealed partial class RanchHofStage
    {
        public const float ViewH = 720f;

        public enum Ziel { Nichts, Pferd, Baum, Wiese, Trog, Stall, Ballen, Kraehe, Schmied, Karotte, Person }

        /// <summary>A tap box on a stage piece.</summary>
        public sealed class Tap : MonoBehaviour
        {
            public Ziel Ziel;
            public string Id = "";
            public int Index;
        }

        public static readonly Vector3 CareSpot = new Vector3(-0.7f, 0f, 1.8f);
        public static readonly Vector3 GoobySpot = new Vector3(1.75f, 0f, 0.95f);
        public static readonly Vector3 TroughSpot = new Vector3(1.25f, 0f, 2.95f);
        public static readonly Vector3 BalesSpot = new Vector3(3.6f, 0f, 2.7f);
        public static readonly Vector3 GateSpot = new Vector3(0f, 0f, 4.6f);
        public static readonly Vector3 FieldCentre = new Vector3(5.6f, 0f, 13.2f);
        public static readonly Vector3 StableSpot = new Vector3(-1.8f, 0f, 16.2f);
        public static readonly Vector3[] TreeSpots = { new Vector3(-7.6f, 0f, 4.8f), new Vector3(-9.7f, 0f, 8.6f), new Vector3(-6.3f, 0f, 9.9f) };
        private static readonly Vector3[] PaddockSpots =
        {
            new Vector3(-3.4f, 0f, 7.4f), new Vector3(3.0f, 0f, 8.0f), new Vector3(0.2f, 0f, 9.6f),
            new Vector3(-1.6f, 0f, 6.3f), new Vector3(4.4f, 0f, 6.2f), new Vector3(-4.6f, 0f, 9.4f),
        };
        private static readonly float[] PaddockYaw = { 160f, 215f, 185f, 140f, 230f, 120f };
        /// <summary>
        /// Night (ranch_offline.gd: 21–7 o'clock at the stable): like ranch_hof_scene.gd's _stall_plaetze the first three
        /// sleep in a row in front of the stable, more horses inside it.
        /// </summary>
        private static readonly Vector3[] StableBoxes =
        {
            new Vector3(-2.8f, 0f, -3.5f), new Vector3(0f, 0f, -3.7f), new Vector3(2.8f, 0f, -3.5f),
            new Vector3(-1.0f, 0f, 0.3f), new Vector3(1.0f, 0f, 0.9f), new Vector3(0f, 0f, 1.8f),
        };
        private static readonly float[] StableYaw = { 84f, 96f, 88f, 180f, 200f, 170f };
        /// <summary>
        /// Round the hitching rail's left end and along the paddock's front rail through the gate — two lanes, so the
        /// horse going out and the one coming in pass side by side.
        /// </summary>
        private static readonly Vector3[] CareToGate =
        {
            new Vector3(-3.9f, 0f, 1.7f), new Vector3(-3.9f, 0f, 4.15f), new Vector3(-0.45f, 0f, 4.15f), new Vector3(-0.45f, 0f, 5.5f),
        };
        private static readonly Vector3[] GateToCare =
        {
            new Vector3(0.45f, 0f, 5.5f), new Vector3(0.45f, 0f, 3.65f), new Vector3(-3.5f, 0f, 3.65f), new Vector3(-3.5f, 0f, 1.9f),
        };
        /// <summary>Through the paddock's back gate to the stable yard and back.</summary>
        private static readonly Vector3[] GateToStable = { new Vector3(StableSpot.x - 0.4f, 0f, 9.4f), new Vector3(StableSpot.x - 0.4f, 0f, 11.4f) };
        private static readonly Vector3[] StableToGate = { new Vector3(StableSpot.x + 0.4f, 0f, 11.4f), new Vector3(StableSpot.x + 0.4f, 0f, 9.4f) };
        private const float FieldW = 6.2f, FieldD = 3.6f, WalkSpeed = 1.6f;

        public MgStage3D Stage { get; }
        public GoobyAvatar Gooby { get; private set; }

        private sealed class Horse
        {
            public string Id;
            public RanchHorse3D View;
            public Vector3 Home, Pos;
            public float Yaw, HomeYaw, TargetYaw;
            public readonly List<Vector3> Path = new List<Vector3>();
            public Action Arrived;
            public bool Sleeping, SleepShown;
        }

        private readonly Transform _root;
        private readonly Dictionary<string, Horse> _horses = new Dictionary<string, Horse>();
        private readonly List<Transform>[] _apples = { new List<Transform>(), new List<Transform>(), new List<Transform>() };
        private readonly List<Transform> _bales = new List<Transform>();
        private readonly List<Transform> _tallGrass = new List<Transform>();
        private readonly List<Transform> _muck = new List<Transform>();
        private Transform _water, _fieldStubble, _hen, _brushProp, _treat;
        private Vector3 _treatFrom, _treatTo;
        private float _treatT = -1f, _t, _goobyHop, _brushT = -1f;
        private string _selected;
        private bool _night, _destroyed;

        private static Vector3 At(float x, float y, float z) => MgStage3D.At(x, y, z);
        private static Vector3 At(Vector3 local) => MgStage3D.Origin + local;
        private static Material M(string hex, float smooth = 0.25f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);
        private static string Model(string name) => Path.Combine(AssetLoader.Root, "ranch", name + ".glb");

        public RanchHofStage(UnityEngine.UIElements.VisualElement field, Dictionary<string, string> outfit, bool night)
        {
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            _night = night;
            Stage = new MgStage3D(ViewH * aspect, ViewH, ArtLibrary.Hex(night ? "#3A4A78" : "#BFE3F7"));
            Stage.Bind(field);
            Stage.Aim(new Vector3(0.2f, 5.3f, -8.6f), 20f, 36f);
            _root = Stage.Root.transform;
            BuildLand();
            BuildPaddock();
            BuildBuildings();
            BuildOrchard();
            BuildMeadow();
            BuildCareSpot();
            BuildFarmAnimals();
            if (night) BuildLanterns();
            SetVisible(true);
            _ = SpawnGoobyAsync(outfit);
        }

        /// <summary>Covered by another screen (a ride): the farm camera rests and the studio goes back to daylight.</summary>
        public void SetVisible(bool on)
        {
            Stage.Park(!on);
            if (!_night) return;
            if (on) MgStudio.Night();
            else MgStudio.Day();
        }

        /// <summary>At night a lantern on the hitching post and the stable's warm glow.</summary>
        private void BuildLanterns()
        {
            foreach (var (pos, range, intensity) in new[] { (new Vector3(-3.2f, 1.45f, CareSpot.z + 1.2f), 7f, 2.2f), (StableSpot + new Vector3(1.4f, 2.1f, -2.7f), 9f, 2.6f) })
            {
                var glow = ArtLibrary.Sphere(_root, "Lantern", At(pos), 0.14f, ArtLibrary.Glow(new Color(1.4f, 1.05f, 0.55f)), false);
                var light = new GameObject("LanternLight").AddComponent<Light>();
                light.transform.SetParent(glow.transform, false);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.8f, 0.5f);
                light.range = range;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
            }
        }

        /// <summary>A stage point → the field's own pixels (y down).</summary>
        public Vector2 ToField(Vector3 local, float fieldW, float fieldH) => Stage.ToField(At(local), fieldW, fieldH);

        /// <summary>Over the selected horse's back (for hearts and floats).</summary>
        public Vector3 HorseTop => _selected != null && _horses.TryGetValue(_selected, out var h) ? h.Pos + Vector3.up * 2.0f : CareSpot + Vector3.up * 2f;

        public Vector3 TreeTop(int i) => TreeSpots[Mathf.Clamp(i, 0, 2)] + Vector3.up * 3.2f;

        // ── the land ──

        private void BuildLand()
        {
            Stage.Ground(0f, ArtLibrary.Hex(_night ? "#6F9A6A" : "#A8D58A"), "grass", 110f, 90f, 22f);
            ArtLibrary.Box(_root, "Path", At(0.4f, 0.012f, -4f), new Vector3(2.6f, 0.02f, 12f), M("#E8D6AA", 0.05f), false);
            ArtLibrary.Box(_root, "CarePad", At(CareSpot.x + 1.2f, 0.016f, CareSpot.z - 0.1f), new Vector3(7.4f, 0.02f, 3.4f), M("#E2CB98", 0.05f), false);
            ArtLibrary.Box(_root, "PaddockSand", At(0f, 0.01f, 7.6f), new Vector3(12f, 0.02f, 6f), M("#D9C597", 0.04f), false);
            Stage.Hills(0f, 40f, 8, ArtLibrary.Hex(_night ? "#5E7C70" : "#9CC98C"), 7f, 110f, 0f, 5);
            Stage.Hills(0f, 52f, 6, ArtLibrary.Hex(_night ? "#4E6670" : "#B9DDB0"), 10f, 140f, 8f, 9);
            Stage.Row("tree_round_big", 0f, 27f, -26f, 26f, 9, 1.5f, 0f, 2.5f, 11);
            Stage.Row("tree_poplar", 0f, 31f, -30f, 30f, 7, 1.4f, 0f, 3f, 12);
            Stage.Clouds(5, 13f, 46f, 70f, 2.2f, 4);
            if (!_night) Stage.Sun(new Vector3(-24f, 22f, 60f), 3.2f);
            else Stage.Sun(new Vector3(20f, 20f, 60f), 2.2f, new Color(1.1f, 1.08f, 0.95f));
            Stage.Meadow(0f, -14f, 14f, -6f, -1f, 70, 10, 21);
            Stage.Meadow(0f, -16f, -8f, 0f, 14f, 50, 8, 22);
            Stage.Meadow(0f, 9f, 16f, -2f, 9f, 45, 7, 23);
        }

        /// <summary>The white paddock rail behind the grooming spot, its gate open to the front.</summary>
        private void BuildPaddock()
        {
            var white = M("#FFF8EE", 0.3f);
            const float x0 = -6f, x1 = 6f, z0 = 4.6f, z1 = 10.6f;
            void Run(Vector3 a, Vector3 b, float? gapX)
            {
                var len = Vector3.Distance(a, b);
                var dir = (b - a).normalized;
                var posts = Mathf.Max(1, Mathf.RoundToInt(len / 1.5f));
                for (var i = 0; i <= posts; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)posts);
                    if (gapX.HasValue && Mathf.Abs(p.x - gapX.Value) < 1.0f) continue;
                    ArtLibrary.Box(_root, "Post", At(p.x, 0.55f, p.z), new Vector3(0.14f, 1.1f, 0.14f), white);
                }
                foreach (var y in new[] { 0.52f, 0.92f })
                {
                    if (!gapX.HasValue)
                    {
                        var rail = ArtLibrary.Box(_root, "Rail", At((a + b) * 0.5f + Vector3.up * y), new Vector3(0.07f, 0.09f, len), white);
                        rail.transform.rotation = Quaternion.LookRotation(dir);
                        continue;
                    }
                    var g = gapX.Value;
                    foreach (var (s, e) in new[] { (a, new Vector3(g - 1.0f, 0f, a.z)), (new Vector3(g + 1.0f, 0f, a.z), b) })
                    {
                        var l = Vector3.Distance(s, e);
                        var rail = ArtLibrary.Box(_root, "Rail", At((s + e) * 0.5f + Vector3.up * y), new Vector3(0.07f, 0.09f, l), white);
                        rail.transform.rotation = Quaternion.LookRotation((e - s).normalized);
                    }
                }
            }
            Run(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), GateSpot.x);
            Run(new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), StableSpot.x);
            Run(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), null);
            Run(new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), null);
            // the gate swung open against the rail
            var gate = Stage.Glb(Model("fence_gate"), At(-1.1f, 0f, z0 - 0.1f), 2.6f, 115f);
            gate.name = "Gate";
            var sign = ArtLibrary.Box(_root, "PaddockSign", At(1.4f, 1.25f, z0 - 0.08f), new Vector3(0.9f, 0.34f, 0.05f), M("#C99A6B", 0.2f));
            sign.transform.rotation = Quaternion.Euler(0f, 0f, -3f);
            ArtLibrary.Box(_root, "SignHeart", At(1.4f, 1.25f, z0 - 0.12f), new Vector3(0.16f, 0.14f, 0.02f), M("#F28CA0", 0.3f), false);
        }

        private void BuildBuildings()
        {
            var stable = Stage.Glb(Model("scheune_offen"), At(StableSpot), 0.78f, 0f);
            stable.name = "Stable";
            AddTap(stable.gameObject, Ziel.Stall, "", 0, new Vector3(0f, 2.3f, 0f) / 0.78f, new Vector3(5.6f, 4.6f, 6f) / 0.78f);
            for (var i = 0; i < 3; i++)
            {
                var muck = ArtLibrary.Sphere(_root, "Muck", At(StableSpot.x + 2.9f + i * 0.5f, 0.05f, StableSpot.z - 1.4f + (i % 2) * 0.4f), 0.32f, M("#8A6A45", 0.1f), false);
                muck.transform.localScale = new Vector3(0.7f, 0.26f, 0.55f);
                _muck.Add(muck.transform);
            }
            Stage.Glb(Model("scheune"), At(7.4f, 0f, 20.5f), 0.72f, -12f);
            Stage.Glb(Model("silo"), At(12.4f, 0f, 21.2f), 0.62f, 0f);
            Stage.Glb(Model("wasserturm"), At(-15.5f, 0f, 22f), 0.55f, 20f);
            Stage.Glb(Model("windmuehle_turm"), At(18f, 0f, 27f), 0.62f, -20f);
            Stage.Glb(Model("huehnerstall"), At(-12.2f, 0f, 13.2f), 0.8f, 40f);
            Stage.Glb(Model("brunnen"), At(-5.3f, 0f, 2.2f), 0.55f, 10f);
            Stage.Glb(Model("log"), At(9.8f, 0f, 3.6f), 2.6f, 70f);
            Stage.Glb(Model("stump_round"), At(-3.4f, 0f, -1.4f), 2.4f, 0f);
            Stage.Glb(Model("rock_largeA"), At(10.8f, 0f, -0.4f), 2.6f, 30f);
            foreach (var (x, z, s) in new[] { (-11.5f, 2.5f, 1.6f), (9.5f, 9.8f, 1.5f), (-5.2f, -3.6f, 1.3f), (6.6f, -3.2f, 1.2f) })
                _ = TintWhenLoaded(Stage.Glb(Model(s > 1.4f ? "plant_bushLarge" : "plant_bush"), At(x, 0f, z), s * 2.8f, x * 13f), ArtLibrary.Hex("#7FBF5E"));
        }

        // ── apple trees and the hay meadow ──

        private void BuildOrchard()
        {
            var red = M("#E8574C", 0.55f);
            var leaf = M("#6FB05A", 0.2f);
            for (var i = 0; i < TreeSpots.Length; i++)
            {
                var scale = 3.4f + i * 0.25f;
                var tree = Stage.Glb(Model("tree_oak"), At(TreeSpots[i]), scale, i * 70f);
                tree.name = "AppleTree" + i;
                _ = TintWhenLoaded(tree, ArtLibrary.Hex(i == 1 ? "#86C46A" : "#7DBE63"), "leaf");
                AddTap(tree.gameObject, Ziel.Baum, "", i, new Vector3(0f, 2.2f, 0f) / scale, new Vector3(2.4f, 4.4f, 2.4f) / scale);
                for (var k = 0; k < 5; k++)
                {
                    var a = k * 72f * Mathf.Deg2Rad + i;
                    var p = TreeSpots[i] + new Vector3(Mathf.Cos(a) * 0.95f, 2.25f + (k % 3) * 0.35f, Mathf.Sin(a) * 0.8f - 0.45f);
                    var apple = ArtLibrary.Sphere(_root, "Apple", At(p), 0.14f, red, false);
                    ArtLibrary.Box(apple.transform, "Leaf", apple.transform.position + new Vector3(0.05f, 0.14f, 0f), new Vector3(0.09f, 0.03f, 0.05f), leaf, false);
                    _apples[i].Add(apple.transform);
                }
            }
        }

        private void BuildMeadow()
        {
            var c = FieldCentre;
            var soil = ArtLibrary.Box(_root, "MeadowSoil", At(c.x, 0.015f, c.z), new Vector3(FieldW + 0.4f, 0.03f, FieldD + 0.4f), M("#C9B27A", 0.05f), false);
            AddTap(soil, Ziel.Wiese, "", 0, Vector3.up * 0.5f, new Vector3(1f, 30f, 1f));
            var stubble = new GameObject("Stubble").transform;
            stubble.SetParent(_root, false);
            var stubbleMat = M("#D8C27A", 0.05f);
            var rng = new System.Random(31);
            for (var i = 0; i < 70; i++)
            {
                var p = c + new Vector3(((float)rng.NextDouble() - 0.5f) * FieldW, 0f, ((float)rng.NextDouble() - 0.5f) * FieldD);
                Stage.Cone(At(p), 0.05f, 0.12f, stubbleMat).transform.SetParent(stubble, true);
            }
            _fieldStubble = stubble;
            var gold = new[] { ArtLibrary.Hex("#E9C35A"), ArtLibrary.Hex("#F2D47A"), ArtLibrary.Hex("#D8B04A") };
            for (var i = 0; i < 46; i++)
            {
                var p = c + new Vector3(((float)rng.NextDouble() - 0.5f) * FieldW, 0f, ((float)rng.NextDouble() - 0.5f) * FieldD);
                var tuft = Stage.Glb(Model("grass_large"), At(p), 2.4f + (float)rng.NextDouble() * 0.8f, (float)rng.NextDouble() * 360f);
                _tallGrass.Add(tuft);
                _ = TintWhenLoaded(tuft, gold[i % gold.Length]);
            }
            // a little fence and a sign at the meadow's front edge
            var wood = M("#B98A5E", 0.2f);
            for (var x = -FieldW * 0.5f; x <= FieldW * 0.5f + 0.01f; x += FieldW / 4f)
                ArtLibrary.Box(_root, "MeadowPost", At(c.x + x, 0.35f, c.z - FieldD * 0.5f - 0.35f), new Vector3(0.1f, 0.7f, 0.1f), wood);
            ArtLibrary.Box(_root, "MeadowRail", At(c.x, 0.55f, c.z - FieldD * 0.5f - 0.35f), new Vector3(FieldW, 0.07f, 0.06f), wood);
        }

        /// <summary>
        /// Glb models arrive async; once their renderers exist the materials take a tint (the meadow's gold, the apple
        /// trees' fresh green on the Kenney leaves) — only materials whose name contains <paramref name="only"/>, if given.
        /// </summary>
        internal static async Task TintWhenLoaded(Transform anchor, Color c, string only = null)
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

        // ── the grooming spot ──

        private void BuildCareSpot()
        {
            var trough = Stage.Glb(Model("trog"), At(TroughSpot), 1.25f, 0f);
            trough.name = "Trough";
            AddTap(trough.gameObject, Ziel.Trog, "", 0, new Vector3(0f, 0.4f, 0f) / 1.25f, new Vector3(1.6f, 1f, 0.9f) / 1.25f);
            var water = ArtLibrary.Box(_root, "TroughWater", At(TroughSpot.x, 0.36f, TroughSpot.z), new Vector3(1.05f, 0.03f, 0.42f), ArtLibrary.Transparent(new Color(0.45f, 0.72f, 0.95f, 0.85f)), false);
            _water = water.transform;
            // a hitching rail along the pad's back
            var wood = M("#B98A5E", 0.2f);
            foreach (var x in new[] { -3.2f, 1.6f })
                ArtLibrary.Box(_root, "HitchPost", At(x, 0.55f, CareSpot.z + 1.25f), new Vector3(0.13f, 1.1f, 0.13f), wood);
            ArtLibrary.Box(_root, "HitchRail", At(-0.8f, 1.0f, CareSpot.z + 1.25f), new Vector3(4.9f, 0.09f, 0.09f), wood);
            var stool = ArtLibrary.Box(_root, "BrushStool", At(3.1f, 0.25f, 0.6f), new Vector3(0.6f, 0.5f, 0.45f), M("#C99A6B", 0.2f));
            stool.name = "BrushStool";
            _brushProp = Stage.Glb(Model("buerste"), At(3.1f, 0.52f, 0.6f), 1.3f, 30f);
            var bales = new GameObject("HayStack").transform;
            bales.SetParent(_root, false);
            bales.position = At(BalesSpot);
            AddTap(bales.gameObject, Ziel.Ballen, "", 0, new Vector3(0f, 0.7f, 0f), new Vector3(2.2f, 1.6f, 1.4f));
            for (var i = 0; i < 8; i++)
            {
                var row = i < 4 ? 0 : i < 7 ? 1 : 2;
                var col = i < 4 ? i : i < 7 ? i - 4 : 0;
                var n = row == 0 ? 4 : row == 1 ? 3 : 1;
                var p = BalesSpot + new Vector3((col - (n - 1) * 0.5f) * 0.62f, row * 0.42f, (row % 2) * 0.05f);
                var bale = Stage.Glb(Model("heuballen"), At(p), 0.62f, 90f + (i % 2) * 4f);
                bale.gameObject.SetActive(false);
                _bales.Add(bale);
            }
        }

        private void BuildFarmAnimals()
        {
            foreach (var (x, z, yaw) in new[] { (-9.8f, 15.4f, 100f), (-7.2f, 17.6f, 160f) })
                SpawnGrazer("kuh", new Vector3(x, 0f, z), yaw, 0.85f);
            foreach (var (x, z, yaw) in new[] { (9.4f, 16.6f, 200f), (11.0f, 17.4f, 160f), (12.4f, 16.2f, 230f) })
                SpawnGrazer("schaf", new Vector3(x, 0f, z), yaw, 0.8f);
            // Kiki, the hen: a round body, a comb and a beak, pecking at her coop
            var hen = new GameObject("Kiki").transform;
            hen.SetParent(_root, false);
            hen.position = At(-10.4f, 0f, 11.2f);
            ArtLibrary.Sphere(hen, "Body", hen.position + new Vector3(0f, 0.28f, 0f), 0.24f, M("#F2C14E", 0.2f));
            ArtLibrary.Sphere(hen, "Head", hen.position + new Vector3(0f, 0.55f, -0.16f), 0.13f, M("#F2C14E", 0.2f));
            ArtLibrary.Box(hen, "Comb", hen.position + new Vector3(0f, 0.7f, -0.16f), new Vector3(0.05f, 0.08f, 0.12f), M("#E8574C", 0.3f), false);
            ArtLibrary.Box(hen, "Beak", hen.position + new Vector3(0f, 0.54f, -0.3f), new Vector3(0.06f, 0.05f, 0.08f), M("#F29A3A", 0.3f), false);
            _hen = hen;
        }

        /// <summary>A Quaternius cow or sheep (they look down their own −Z) grazing on its loop, head down.</summary>
        private void SpawnGrazer(string model, Vector3 pos, float yaw, float scale)
        {
            var anchor = new GameObject("Grazer:" + model).transform;
            anchor.SetParent(_root, false);
            anchor.SetPositionAndRotation(At(pos), Quaternion.Euler(0f, yaw, 0f));
            anchor.localScale = Vector3.one * scale;
            _ = LoadGrazer(anchor, model);
        }

        private static async Task LoadGrazer(Transform anchor, string model)
        {
            var go = await AssetLoader.InstanceAsync(Model(model), anchor, model, legacyAnimation: true);
            if (go == null) return;
            if (anchor == null) { UnityEngine.Object.Destroy(go); return; }
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one;
            var anim = go.GetComponentInChildren<Animation>();
            if (anim == null) return;
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (var clip in new[] { "Eating", "Idle_Headlow", "Idle" })
            {
                var state = anim[clip];
                if (state == null) continue;
                state.wrapMode = WrapMode.Loop;
                state.time = UnityEngine.Random.value * state.length;
                anim.Play(clip);
                break;
            }
        }

        private void AddTap(GameObject go, Ziel ziel, string id, int index, Vector3 centre, Vector3 size)
        {
            var col = go.AddComponent<BoxCollider>();
            col.center = centre;
            col.size = size;
            var tap = go.AddComponent<Tap>();
            tap.Ziel = ziel;
            tap.Id = id;
            tap.Index = index;
        }

        private async Task SpawnGoobyAsync(Dictionary<string, string> outfit)
        {
            var avatar = await Stage.SpawnGoobyAsync(At(GoobySpot), 1f, outfit);
            if (avatar == null || _destroyed) return;
            Gooby = avatar;
            avatar.transform.rotation = Quaternion.Euler(0f, -95f, 0f);
            avatar.SetEmotion("happy");
        }

        // ── horses ──

        /// <summary>The 3D horse of an id on stage (null when it is not there).</summary>
        public RanchHorse3D HorseView(string id) => _horses.TryGetValue(id ?? "", out var h) ? h.View : null;

        private Transform _schleife;

        /// <summary>The competition quest's bronze ribbon on the hitching post at the grooming spot.</summary>
        public void SetSchleife(bool an)
        {
            if (an == (_schleife != null)) return;
            if (!an)
            {
                UnityEngine.Object.Destroy(_schleife.gameObject);
                _schleife = null;
                return;
            }
            _schleife = new GameObject("BronzeSchleife").transform;
            _schleife.SetParent(_root, false);
            _schleife.position = At(-3.2f, 0f, CareSpot.z + 1.25f - 0.08f);
            RanchBauTeile.Rosette(_schleife, new Vector3(0f, 0.86f, 0f), 0, 0.32f);
            RanchBauTeile.Verschmelze(_schleife);
        }

        /// <summary>Puts the save's horses on stage (new ones appear in the paddock, gone ones leave; foals at their phase's size).</summary>
        public void SetHorses(IReadOnlyList<(string id, Color fell, Color maehne, float scale)> horses)
        {
            var keep = new HashSet<string>();
            for (var i = 0; i < horses.Count; i++)
            {
                var (id, fell, maehne, scale) = horses[i];
                keep.Add(id);
                if (_horses.TryGetValue(id, out var known))
                {
                    known.View.SetCoat(fell, maehne);
                    known.View.transform.localScale = Vector3.one * scale;
                    continue;
                }
                var spot = _night ? StableSpot + StableBoxes[i % StableBoxes.Length] : PaddockSpots[i % PaddockSpots.Length];
                var yaw = _night ? StableYaw[i % StableYaw.Length] : PaddockYaw[i % PaddockYaw.Length];
                var view = RanchHorse3D.Create(_root, "Horse:" + id, fell, maehne);
                view.transform.localScale = Vector3.one * scale;
                var h = new Horse { Id = id, View = view, Home = spot, Pos = spot, Yaw = yaw, HomeYaw = yaw };
                h.TargetYaw = h.Yaw;
                view.transform.SetPositionAndRotation(At(h.Pos), Quaternion.Euler(0f, h.Yaw, 0f));
                AddTap(view.gameObject, Ziel.Pferd, id, i, new Vector3(0f, 1.1f, 0f), new Vector3(0.9f, 2.0f, 2.4f));
                _horses[id] = h;
                h.Sleeping = _night;
            }
            foreach (var id in new List<string>(_horses.Keys))
            {
                if (keep.Contains(id)) continue;
                if (_horses[id].View != null) UnityEngine.Object.Destroy(_horses[id].View.gameObject);
                _horses.Remove(id);
            }
        }

        /// <summary>
        /// The selected horse walks out to the grooming spot; the one before goes back to the paddock (by day) or through
        /// the paddock's back gate to the stable (at night).
        /// </summary>
        public void Select(string id, bool instant = false)
        {
            if (id == _selected || !_horses.TryGetValue(id ?? "", out var next)) return;
            var prevId = _selected;
            _selected = id;
            if (prevId != null && _horses.TryGetValue(prevId, out var prev))
            {
                if (instant)
                {
                    Place(prev, prev.Home, prev.HomeYaw);
                    if (_night) LieDown(prev);
                }
                else WalkTo(prev, RouteOut(prev.Home), prev.HomeYaw, () => LieDown(prev));
            }
            if (next.Sleeping) { next.Sleeping = false; next.SleepShown = false; next.View.Action("idle-loop", 0.1f); }
            if (instant) Place(next, CareSpot, 90f);
            else WalkTo(next, RouteIn(), 90f, () => Nicker());
        }

        /// <summary>Grooming spot → paddock gate (→ back gate → stable yard at night) → the horse's spot, and the way back in.</summary>
        private List<Vector3> RouteOut(Vector3 home)
        {
            var path = new List<Vector3>(CareToGate);
            if (_night) path.AddRange(GateToStable);
            path.Add(home);
            return path;
        }

        private List<Vector3> RouteIn()
        {
            var path = new List<Vector3>();
            if (_night) path.AddRange(StableToGate);
            path.AddRange(GateToCare);
            path.Add(CareSpot);
            return path;
        }

        /// <summary>Back in its box at night the horse lies down again (the clip starts once the model has loaded).</summary>
        private void LieDown(Horse h)
        {
            if (!_night || h.Id == _selected) return;
            h.Sleeping = true;
            h.SleepShown = false;
        }

        private void Place(Horse h, Vector3 pos, float yaw)
        {
            h.Path.Clear();
            h.Pos = pos;
            h.Yaw = h.TargetYaw = yaw;
            h.View.transform.SetPositionAndRotation(At(pos), Quaternion.Euler(0f, yaw, 0f));
            h.View.Gait("stand");
        }

        private static void WalkTo(Horse h, IEnumerable<Vector3> path, float endYaw, Action arrived)
        {
            h.Path.Clear();
            h.Path.AddRange(path);
            h.TargetYaw = endYaw;
            h.Arrived = arrived;
            h.View.Gait("schritt", 1f);
        }

        public bool SelectedArrived => _selected != null && _horses.TryGetValue(_selected, out var h) && h.Path.Count == 0;

        public string Selected => _selected;

        /// <summary>Each horse's stage spot and whether it walks or sleeps (the QA dump).</summary>
        public IEnumerable<(string id, Vector3 pos, bool walking, bool sleeping)> HorseStates()
        {
            foreach (var h in _horses.Values) yield return (h.Id, h.Pos, h.Path.Count > 0, h.Sleeping);
        }

        private void Nicker()
        {
            if (_selected == null || !_horses.TryGetValue(_selected, out var h)) return;
            h.View.Action("kopfschuetteln", 0f, 1f);
        }

        // ── care beats ──

        /// <summary>Hay, an apple or a carrot: Gooby hops, the treat arcs from his hands to the mouth, the horse eats.</summary>
        public void Feed(string futter)
        {
            var h = Sel();
            if (h == null) return;
            GoobyBeat("hop");
            if (_treat != null) UnityEngine.Object.Destroy(_treat.gameObject);
            var from = GoobySpot + new Vector3(-0.3f, 0.9f, 0.1f);
            var mouth = h.Pos + new Vector3(1.05f, 0.95f, -0.05f);
            GameObject treat;
            switch (futter)
            {
                case "apfel":
                    treat = ArtLibrary.Sphere(_root, "Treat", At(from), 0.13f, M("#E8574C", 0.55f), false);
                    break;
                case "karotte":
                    treat = Stage.Cone(At(from), 0.07f, 0.34f, M("#F29A3A", 0.3f), false);
                    treat.transform.rotation = Quaternion.Euler(0f, 0f, 120f);
                    break;
                case "hafer":
                    treat = ArtLibrary.Box(_root, "Treat", At(from), new Vector3(0.3f, 0.34f, 0.24f), M("#E8D6AA", 0.1f), false);
                    ArtLibrary.Box(treat.transform, "Tie", treat.transform.position + Vector3.up * 0.18f, new Vector3(0.16f, 0.05f, 0.14f), M("#B98A5E", 0.2f), false);
                    break;
                case "leckerli":
                    treat = ArtLibrary.Sphere(_root, "Treat", At(from), 0.1f, M("#F28CA0", 0.4f), false);
                    break;
                default:
                    treat = ArtLibrary.Box(_root, "Treat", At(from), new Vector3(0.4f, 0.26f, 0.3f), M("#E9C35A", 0.1f), false);
                    break;
            }
            _treat = treat.transform;
            _treatFrom = from;
            var amBoden = futter == "heu" || futter == "hafer";
            _treatTo = amBoden ? h.Pos + new Vector3(1.0f, 0.15f, -0.15f) : mouth;
            _treatT = 0f;
            h.View.Action("fressen-loop", amBoden ? 2.6f : 1.6f);
        }

        public void Drink()
        {
            var h = Sel();
            if (h == null) return;
            GoobyBeat("wave");
            h.View.Action("fressen-loop", 2.2f);
            if (_water != null) Effects.Puff(_water.position + Vector3.up * 0.1f, new Color(0.62f, 0.84f, 1f, 0.9f), 10, 0.07f, 1.1f, 0.6f, Vector3.up, 0.8f);
        }

        /// <summary>Gooby brushes along the horse's back: a trail of sparkles, then a happy head shake.</summary>
        public void Brush()
        {
            var h = Sel();
            if (h == null) return;
            GoobyBeat("celebrate");
            _brushT = 0f;
            if (_brushProp != null) _brushProp.gameObject.SetActive(false);
            h.View.Action("idle-loop", 1.3f);
        }

        public void MuckOut(bool clean)
        {
            foreach (var m in _muck)
            {
                if (m == null) continue;
                if (m.gameObject.activeSelf && clean) Effects.Puff(m.position + Vector3.up * 0.2f, new Color(0.75f, 0.62f, 0.45f, 0.9f), 8, 0.1f, 1.2f, 0.7f, Vector3.up, 0.9f);
                m.gameObject.SetActive(!clean);
            }
            if (clean) GoobyBeat("hop");
        }

        /// <summary>The stable's muck heaps show how dirty it is (one per 25 points under 100).</summary>
        public void SetStall(double sauberkeit)
        {
            var dirty = Mathf.Clamp(Mathf.CeilToInt((float)(100.0 - sauberkeit) / 25f), 0, _muck.Count);
            for (var i = 0; i < _muck.Count; i++) if (_muck[i] != null) _muck[i].gameObject.SetActive(i < dirty);
        }

        public void SetBales(int heu)
        {
            for (var i = 0; i < _bales.Count; i++) if (_bales[i] != null) _bales[i].gameObject.SetActive(i < heu);
        }

        public void SetMeadow(bool ripe)
        {
            foreach (var g in _tallGrass) if (g != null) g.gameObject.SetActive(ripe);
            if (_fieldStubble != null) _fieldStubble.gameObject.SetActive(!ripe);
        }

        public void Mow()
        {
            foreach (var g in _tallGrass)
                if (g != null && g.gameObject.activeSelf)
                    Effects.Puff(g.position + Vector3.up * 0.4f, new Color(0.93f, 0.82f, 0.45f, 0.9f), 2, 0.08f, 1.0f, 0.6f, Vector3.up, 1f);
            SetMeadow(false);
            GoobyBeat("celebrate");
        }

        public void SetApples(int tree, bool ripe)
        {
            if (tree < 0 || tree >= _apples.Length) return;
            foreach (var a in _apples[tree]) if (a != null) a.gameObject.SetActive(ripe);
        }

        public void Pick(int tree)
        {
            if (tree < 0 || tree >= _apples.Length) return;
            foreach (var a in _apples[tree])
                if (a != null && a.gameObject.activeSelf) Effects.Puff(a.position, new Color(1f, 0.55f, 0.5f, 0.9f), 3, 0.06f, 0.8f, 0.5f, Vector3.up, 1f);
            SetApples(tree, false);
            GoobyBeat("hop");
        }

        public void HorseReact(string clip, float seconds = 0f) => Sel()?.View.Action(clip, seconds);

        private Horse Sel() => _selected != null && _horses.TryGetValue(_selected, out var h) ? h : null;

        private void GoobyBeat(string clip)
        {
            if (Gooby == null) return;
            Gooby.Play(clip);
            _goobyHop = clip == "hop" ? 0.7f : clip == "celebrate" ? 1.6f : 1.1f;
        }

        // ── taps and time ──

        /// <summary>What lies under a field point (px, y down): the nearest tap box along the camera ray.</summary>
        public (Ziel ziel, string id, int index) Pick(Vector2 fieldPoint, float fieldW, float fieldH)
        {
            if (Stage.Camera == null || fieldW <= 0f || fieldH <= 0f) return (Ziel.Nichts, "", 0);
            Physics.SyncTransforms();
            var ray = Stage.Camera.ViewportPointToRay(new Vector3(fieldPoint.x / fieldW, 1f - fieldPoint.y / fieldH, 0f));
            var hits = Physics.RaycastAll(ray, 140f);
            Tap best = null;
            var bestD = float.MaxValue;
            foreach (var hit in hits)
            {
                var tap = hit.collider != null ? hit.collider.GetComponent<Tap>() : null;
                if (tap == null || hit.distance >= bestD) continue;
                best = tap;
                bestD = hit.distance;
            }
            return best == null ? (Ziel.Nichts, "", 0) : (best.Ziel, best.Id, best.Index);
        }

        public void Tick(float dt, bool reduced)
        {
            if (_destroyed || Stage.Parked) return;
            _t += dt;
            TickEvents(dt);
            foreach (var h in _horses.Values)
            {
                if (h.View == null) continue;
                if (h.Sleeping && !h.SleepShown && h.View.Loaded)
                {
                    h.SleepShown = true;
                    h.View.Action("schlafen-loop", 100000f);
                }
                if (h.Path.Count > 0)
                {
                    var target = h.Path[0];
                    var d = target - h.Pos;
                    d.y = 0f;
                    var step = WalkSpeed * dt;
                    if (d.magnitude <= step)
                    {
                        h.Pos = target;
                        h.Path.RemoveAt(0);
                        if (h.Path.Count == 0)
                        {
                            h.View.Gait("stand");
                            var cb = h.Arrived;
                            h.Arrived = null;
                            cb?.Invoke();
                        }
                    }
                    else
                    {
                        h.Pos += d.normalized * step;
                        h.Yaw = Mathf.MoveTowardsAngle(h.Yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 220f * dt);
                    }
                }
                else h.Yaw = Mathf.MoveTowardsAngle(h.Yaw, h.TargetYaw, 160f * dt);
                h.View.transform.SetPositionAndRotation(At(h.Pos), Quaternion.Euler(0f, h.Yaw, 0f));
            }
            if (_treat != null && _treatT >= 0f)
            {
                _treatT += dt / 0.55f;
                var k = Mathf.Clamp01(_treatT);
                var p = Vector3.Lerp(_treatFrom, _treatTo, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.7f;
                _treat.position = At(p);
                if (k >= 1f)
                {
                    Effects.Puff(At(_treatTo), new Color(1f, 0.9f, 0.6f, 0.9f), 6, 0.06f, 0.9f, 0.5f, Vector3.up, 0.8f);
                    UnityEngine.Object.Destroy(_treat.gameObject);
                    _treat = null;
                    _treatT = -1f;
                }
            }
            if (_brushT >= 0f)
            {
                var h = Sel();
                _brushT += dt;
                if (h != null && !reduced && Mathf.Repeat(_brushT, 0.18f) < dt)
                {
                    var along = Mathf.PingPong(_brushT * 1.6f, 1.6f) - 0.8f;
                    Effects.Sparkle(At(h.Pos + new Vector3(along, 1.45f, -0.35f)), 0.25f, 5);
                }
                if (_brushT > 1.3f)
                {
                    _brushT = -1f;
                    if (_brushProp != null) _brushProp.gameObject.SetActive(true);
                    h?.View.Action("kopfschuetteln");
                }
            }
            if (_goobyHop > 0f && (_goobyHop -= dt) <= 0f && Gooby != null) Gooby.Play("idle-loop");
            if (!reduced)
            {
                if (_hen != null)
                {
                    var peck = Mathf.Repeat(_t, 2.4f) < 0.35f;
                    _hen.localRotation = Quaternion.Euler(peck ? 28f : 0f, Mathf.Sin(_t * 0.7f) * 50f, 0f);
                }
            }
        }

        public void Destroy()
        {
            _destroyed = true;
            Stage.Destroy();
        }
    }
}
