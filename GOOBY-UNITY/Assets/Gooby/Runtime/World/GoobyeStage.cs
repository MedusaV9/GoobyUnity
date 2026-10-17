using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The Goo und Bye corner store as a 3D diorama behind the store UI (port of laden_scene.gd's set, grown): warm planks,
    /// a mint wainscot under the GOO UND BYE lettering, the shop window, the glass door with its bell (customers come in
    /// through it), the produce stand with five crates — each slot shows its real wares (Kenney food models) up to eight
    /// pieces, chilled wares on ice — the cooler, the back shelf, the till counter with the register and the player's
    /// Gooby behind it, the goods-in corner with crates and the hand truck. The store visibly grows with its level:
    /// bunting and the pester-power jar, fruit crates, the bread corner, string lights and the glowing XXL sign.
    /// Customers are <see cref="Kunde"/> walkers the screen scripts step by step; they also run without a stage (batch
    /// players), only invisibly.
    /// </summary>
    public sealed class GoobyeStage
    {
        public const float ViewH = 720f;
        public const int Slots = 5;
        public static readonly float[] SlotX = { -3.3f, -2.48f, -1.66f, -0.84f, -0.02f };
        public const float StandZ = 0.95f, StandTop = 0.62f;
        public static readonly Vector3 Door = new Vector3(4.8f, 0f, 3.55f);
        /// <summary>Customers come in along the back lane (behind the till, in front of the back shelf) …</summary>
        public static readonly Vector3 BackLane = new Vector3(4.2f, 0f, 2.85f);
        /// <summary>… pick from behind the stand facing the room, round its right end to the till and leave by the front aisle.</summary>
        public static readonly Vector3 StandEnd = new Vector3(0.95f, 0f, 1.7f);
        public static readonly Vector3 QueueSpot = new Vector3(0.9f, 0f, 0.1f);
        public static readonly Vector3 Aisle = new Vector3(4.4f, 0f, -0.05f);
        public static readonly Vector3 PaySpot = new Vector3(1.55f, 0f, 0.42f);
        private static readonly Vector3 KeeperSpot = new Vector3(2.15f, 0f, 2.25f);
        private const float CounterX = 2.6f, CounterZ = 1.5f, CounterTop = 1.0f;

        public MgStage3D Stage { get; }
        public GoobyAvatar Keeper { get; private set; }

        private readonly Transform _root;
        private readonly Transform[] _slotRoots = new Transform[Slots];
        private readonly string[] _slotWare = new string[Slots];
        private readonly bool[] _slotCold = new bool[Slots];
        private readonly List<Transform>[] _pieces = new List<Transform>[Slots];
        private readonly Renderer[] _crateRenderers = new Renderer[Slots];
        private readonly List<Transform> _counterGoods = new List<Transform>();
        private readonly List<Transform> _deliveryCrates = new List<Transform>();
        private readonly List<GameObject>[] _levelDeco = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
        private readonly List<Renderer> _lamps = new List<Renderer>();
        private Renderer _openSign;
        private Transform _doorPanel, _bell, _register;
        private float _t, _doorOpen, _bellSwing, _registerFlash, _keeperHop;
        private int _level;
        private bool _open, _destroyed;

        private static Vector3 At(float x, float y, float z) => MgStage3D.At(x, y, z);
        private static Vector3 At(Vector3 local) => MgStage3D.Origin + local;
        private static Material M(string hex, float smooth = 0.25f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);

        public GoobyeStage(UnityEngine.UIElements.VisualElement field, Dictionary<string, string> outfit, int level)
        {
            var aspect = Mathf.Clamp((float)Screen.width / Mathf.Max(1, Screen.height), 1.3f, 2.4f);
            Stage = new MgStage3D(ViewH * aspect, ViewH, ArtLibrary.Hex("#F6E7D2"));
            Stage.Bind(field);
            Stage.Aim(new Vector3(0f, 3.6f, -9.6f), 11f, 30f);
            _root = Stage.Root.transform;
            for (var i = 0; i < Slots; i++) _pieces[i] = new List<Transform>();
            BuildRoom();
            BuildStand();
            BuildCounter();
            BuildCorners();
            BuildLevelDeco();
            SetLevel(level);
            _ = SpawnKeeperAsync(outfit);
        }

        /// <summary>A stage point → the field's own pixels (y down).</summary>
        public Vector2 ToField(Vector3 local, float fieldW, float fieldH) => Stage.ToField(At(local), fieldW, fieldH);

        /// <summary>Where the slot chip sits: a price tag on the stand's front under its crate (the space above stays free for faces).</summary>
        public static Vector3 SlotChipPoint(int i) => new Vector3(SlotX[Mathf.Clamp(i, 0, Slots - 1)], StandTop * 0.42f, StandZ - 0.5f);

        /// <summary>Where a customer stands to pick from slot i: behind the stand, facing the room.</summary>
        public static Vector3 SlotBack(int i) => new Vector3(SlotX[Mathf.Clamp(i, 0, Slots - 1)], 0f, StandZ + 0.75f);

        public static Vector3 LaneAt(int i) => new Vector3(SlotX[Mathf.Clamp(i, 0, Slots - 1)], 0f, BackLane.z);

        public static Vector3 RegisterPoint => new Vector3(CounterX + 0.6f, CounterTop + 0.7f, CounterZ - 0.1f);

        /// <summary>The deal sign stands like a sandwich board at the stand's left corner.</summary>
        public static Vector3 SignPoint => new Vector3(-4.15f, 0.05f, 0.2f);

        public static Vector3 DeliveryPoint => new Vector3(-5.0f, 1.0f, 1.7f);

        // ── the room ──

        private void BuildRoom()
        {
            ArtLibrary.Box(_root, "Floor", At(0f, -0.05f, 0f), new Vector3(28f, 0.1f, 16f), ArtLibrary.Pbr("wood_floor", 1.6f, ArtLibrary.Hex("#E9C9A0"), 0.25f), false);
            ArtLibrary.Box(_root, "Wall", At(0f, 3.0f, 4.25f), new Vector3(28f, 6f, 0.2f), ArtLibrary.Pbr("plaster", 2.5f, ArtLibrary.Hex("#FFF2DD"), 0.05f), false);
            ArtLibrary.Box(_root, "Wainscot", At(0f, 0.6f, 4.13f), new Vector3(28f, 1.2f, 0.06f), M("#A7DCD6", 0.15f), false);
            ArtLibrary.Box(_root, "WainscotRail", At(0f, 1.22f, 4.09f), new Vector3(28f, 0.07f, 0.08f), M("#7FC8C0", 0.3f), false);
            ArtLibrary.Box(_root, "Skirting", At(0f, 0.06f, 4.08f), new Vector3(28f, 0.12f, 0.06f), M("#6FB3AB", 0.3f), false);
            // the lettering: GOO UND BYE on the back wall, the ear apostrophe painted in
            ArtLibrary.Quad(_root, "WallTitle", At(0f, 3.3f, 4.1f), Quaternion.Euler(0f, 180f, 0f), new Vector2(4.2f, 1.4f), ArtLibrary.Decal("wall_goobye_dark"));
            // shop window left: frame, the street's light behind the pane, an awning valance inside
            var frame = M("#FFFFFF", 0.3f);
            ArtLibrary.Box(_root, "WindowPane", At(-5.2f, 1.95f, 4.12f), new Vector3(2.3f, 1.55f, 0.03f), ArtLibrary.Glow(ArtLibrary.Hex("#CFE9FF") * 0.9f), false);
            ArtLibrary.Box(_root, "WindowHedge", At(-5.2f, 1.33f, 4.1f), new Vector3(2.3f, 0.3f, 0.02f), M("#8CC97A", 0.1f), false);
            foreach (var (x, y, w, h) in new[] { (-5.2f, 2.75f, 2.5f, 0.1f), (-5.2f, 1.15f, 2.5f, 0.12f), (-6.4f, 1.95f, 0.1f, 1.7f), (-4.0f, 1.95f, 0.1f, 1.7f), (-5.2f, 1.95f, 0.06f, 1.55f) })
                ArtLibrary.Box(_root, "WindowFrame", At(x, y, 4.07f), new Vector3(w, h, 0.08f), frame, false);
            for (var i = 0; i < 7; i++)
            {
                var stripe = ArtLibrary.Box(_root, "Valance", At(-6.3f + i * 0.37f, 2.92f, 4.0f), new Vector3(0.36f, 0.26f, 0.05f), M(i % 2 == 0 ? "#7FC8C0" : "#FFF6EA", 0.2f), false);
                stripe.transform.rotation = Quaternion.Euler(-12f, 0f, 0f);
            }
            // the door: teal frame, glass panel (slides when someone comes), the bell, an OFFEN sign, the doormat
            ArtLibrary.Box(_root, "DoorFrameL", At(Door.x - 0.72f, 1.2f, 4.08f), new Vector3(0.12f, 2.4f, 0.12f), M("#5FB1A8", 0.3f));
            ArtLibrary.Box(_root, "DoorFrameR", At(Door.x + 0.72f, 1.2f, 4.08f), new Vector3(0.12f, 2.4f, 0.12f), M("#5FB1A8", 0.3f));
            ArtLibrary.Box(_root, "DoorFrameTop", At(Door.x, 2.42f, 4.08f), new Vector3(1.56f, 0.14f, 0.12f), M("#5FB1A8", 0.3f));
            ArtLibrary.Box(_root, "DoorOutside", At(Door.x, 1.18f, 4.14f), new Vector3(1.32f, 2.36f, 0.02f), ArtLibrary.Glow(ArtLibrary.Hex("#E6F4FF") * 0.95f), false);
            var panel = new GameObject("DoorPanel").transform;
            panel.SetParent(_root, false);
            panel.position = At(Door.x, 0f, 4.04f);
            ArtLibrary.Box(panel, "DoorGlass", panel.position + new Vector3(0f, 1.15f, 0f), new Vector3(1.28f, 2.2f, 0.03f), ArtLibrary.Transparent(new Color(0.86f, 0.95f, 1f, 0.45f)), false).transform.SetParent(panel, true);
            ArtLibrary.Box(panel, "DoorBar", panel.position + new Vector3(0.45f, 1.05f, -0.04f), new Vector3(0.06f, 0.5f, 0.05f), M("#C9CED6", 0.7f), false).transform.SetParent(panel, true);
            _doorPanel = panel;
            var sign = ArtLibrary.Box(_root, "OpenSign", At(Door.x, 1.75f, 4.0f), new Vector3(0.5f, 0.24f, 0.03f), M("#E8574C", 0.2f), false);
            _openSign = sign.GetComponent<Renderer>();
            var bell = new GameObject("Bell").transform;
            bell.SetParent(_root, false);
            bell.position = At(Door.x - 0.5f, 2.32f, 3.96f);
            ArtLibrary.Sphere(bell, "BellCup", bell.position + Vector3.down * 0.1f, 0.08f, M("#F2C230", 0.8f), false).transform.SetParent(bell, true);
            _bell = bell;
            ArtLibrary.Box(_root, "Doormat", At(Door.x, 0.01f, 3.35f), new Vector3(1.3f, 0.02f, 0.75f), M("#B98B5E", 0.1f), false);
            // pendant lamps over the stand and the till
            foreach (var x in new[] { -2.9f, -0.6f, 2.6f })
            {
                ArtLibrary.Cylinder(_root, "Cord", At(x, 4.9f, 1.4f), 0.012f, 1.6f, M("#3B3630"), false);
                ArtLibrary.Cylinder(_root, "Shade", At(x, 4.08f, 1.4f), 0.32f, 0.16f, M("#7FC8C0", 0.3f), false);
                var bulb = ArtLibrary.Sphere(_root, "Bulb", At(x, 3.95f, 1.4f), 0.13f, ArtLibrary.Glow(ArtLibrary.Hex("#FFE2A8") * 1.1f), false);
                _lamps.Add(bulb.GetComponent<Renderer>());
            }
        }

        // ── the produce stand: five crates on a long table ──

        private void BuildStand()
        {
            var wood = ArtLibrary.Pbr("wood_planks", 1.2f, ArtLibrary.Hex("#D9B48A"), 0.2f, 0.7f);
            var dark = ArtLibrary.Pbr("wood_planks", 1.2f, ArtLibrary.Hex("#B98B5E"), 0.2f, 0.7f);
            ArtLibrary.Box(_root, "StandTop", At(-1.66f, StandTop - 0.04f, StandZ), new Vector3(4.3f, 0.08f, 1.05f), wood);
            ArtLibrary.Box(_root, "StandFront", At(-1.66f, StandTop * 0.5f, StandZ - 0.45f), new Vector3(4.2f, StandTop - 0.08f, 0.06f), M("#7FC8C0", 0.2f));
            ArtLibrary.Box(_root, "StandBand", At(-1.66f, StandTop - 0.2f, StandZ - 0.49f), new Vector3(4.2f, 0.08f, 0.02f), M("#FFF6EA", 0.2f), false);
            foreach (var x in new[] { -3.76f, 0.44f })
                ArtLibrary.Box(_root, "StandLeg", At(x, StandTop * 0.5f, StandZ), new Vector3(0.1f, StandTop, 0.95f), dark);
            for (var i = 0; i < Slots; i++)
            {
                var slot = new GameObject("Slot" + i).transform;
                slot.SetParent(_root, false);
                slot.position = At(SlotX[i], StandTop, StandZ + 0.05f);
                slot.rotation = Quaternion.Euler(-16f, 0f, 0f);
                var bottom = ArtLibrary.Box(slot, "CrateBottom", slot.TransformPoint(0f, 0.02f, 0f), new Vector3(0.74f, 0.04f, 0.52f), wood);
                bottom.transform.rotation = slot.rotation;
                bottom.transform.SetParent(slot, true);
                _crateRenderers[i] = bottom.GetComponent<Renderer>();
                foreach (var (lx, lz, sx, sz, h) in new[] { (0f, -0.25f, 0.74f, 0.04f, 0.1f), (0f, 0.25f, 0.74f, 0.04f, 0.2f), (-0.36f, 0f, 0.04f, 0.52f, 0.16f), (0.36f, 0f, 0.04f, 0.52f, 0.16f) })
                {
                    var side = ArtLibrary.Box(slot, "CrateSide", slot.TransformPoint(lx, h * 0.5f + 0.02f, lz), new Vector3(sx, h, sz), dark);
                    side.transform.rotation = slot.rotation;
                    side.transform.SetParent(slot, true);
                }
                _slotRoots[i] = slot;
                _slotWare[i] = "";
            }
        }

        // ── the till ──

        private void BuildCounter()
        {
            ArtLibrary.Box(_root, "Counter", At(CounterX, CounterTop * 0.5f, CounterZ), new Vector3(2.4f, CounterTop - 0.04f, 0.85f), M("#7FC8C0", 0.2f));
            ArtLibrary.Box(_root, "CounterTop", At(CounterX, CounterTop - 0.02f, CounterZ), new Vector3(2.5f, 0.06f, 0.95f), M("#F4EFE6", 0.45f));
            ArtLibrary.Box(_root, "CounterBand", At(CounterX, CounterTop - 0.22f, CounterZ - 0.44f), new Vector3(2.4f, 0.08f, 0.02f), M("#FFF6EA", 0.2f), false);
            ArtLibrary.Box(_root, "Belt", At(CounterX - 0.55f, CounterTop + 0.015f, CounterZ - 0.05f), new Vector3(1.0f, 0.03f, 0.42f), M("#4A4F55", 0.5f), false);
            _register = Stage.Prop("cash_register", At(CounterX + 0.6f, CounterTop, CounterZ + 0.05f), 0.5f, 180f);
            // a little shelf of bags behind the till
            foreach (var (bx, bc) in new[] { (CounterX + 0.05f, "#EBD9C6"), (CounterX + 0.22f, "#FFF8EE") })
                ArtLibrary.Box(_root, "PaperBag", At(bx, CounterTop + 0.12f, CounterZ + 0.25f), new Vector3(0.15f, 0.22f, 0.1f), M(bc, 0.15f));
        }

        // ── the back and the corners ──

        private void BuildCorners()
        {
            Stage.Glb(System.IO.Path.Combine(AssetLoader.Root, "scenery", "cooler.glb"), At(-2.0f, 0f, 3.72f), 1f, 180f);
            Stage.Glb(System.IO.Path.Combine(AssetLoader.Root, "scenery", "shelf_unit.glb"), At(1.4f, 0f, 3.82f), 1f, 180f);
            // a few things on the back shelf so it reads as a grocer's
            foreach (var (m, x, y, s) in new[] { ("bread", 0.85f, 0.6f, 0.45f), ("croissant", 1.35f, 0.6f, 0.45f), ("soda", 1.85f, 0.6f, 0.6f), ("cookie", 0.9f, 1.2f, 0.9f), ("cupcake", 1.4f, 1.2f, 0.45f), ("soda", 1.9f, 1.2f, 0.6f), ("apple", 1.0f, 1.8f, 0.4f), ("cheese", 1.6f, 1.8f, 0.4f) })
                Stage.Prop(m, At(x, y, 3.82f), s, 180f + (x * 37f) % 40f - 20f);
            // goods-in corner: cardboard boxes, the hand truck, a poster for the house brand
            foreach (var (x, y, z, yaw) in new[] { (-5.75f, 0f, 2.75f, 8f), (-5.2f, 0f, 3.05f, -12f), (-5.55f, 0.46f, 2.85f, 20f) })
                Karton(At(x, y, z), yaw);
            var steel = M("#9AA3AD", 0.6f);
            ArtLibrary.Box(_root, "TruckPlate", At(-4.35f, 0.03f, 2.3f), new Vector3(0.5f, 0.04f, 0.32f), steel);
            foreach (var s in new[] { -0.22f, 0.22f })
            {
                var rail = ArtLibrary.Box(_root, "TruckRail", At(-4.35f + s, 0.6f, 2.45f), new Vector3(0.04f, 1.2f, 0.04f), steel);
                rail.transform.rotation = Quaternion.Euler(-10f, 0f, 0f);
                var wheel = ArtLibrary.Cylinder(_root, "TruckWheel", At(-4.35f + s, 0.11f, 2.47f), 0.11f, 0.06f, M("#2F2B2A", 0.4f));
                wheel.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
            ArtLibrary.Quad(_root, "HouseBrand", At(-3.6f, 2.35f, 4.1f), Quaternion.Euler(0f, 180f, 0f), new Vector2(0.95f, 1.26f), ArtLibrary.Decal("poster_goobye_ware"));
            ArtLibrary.Quad(_root, "Carrot", At(3.3f, 2.35f, 4.1f), Quaternion.Euler(0f, 180f, 0f), new Vector2(0.95f, 1.26f), ArtLibrary.Decal("poster_moehre"));
            Stage.HomeProp("plant_tall", At(-6.6f, 0f, 3.4f), 0.75f);
            Stage.HomeProp("monstera", At(6.7f, 0f, 3.6f), 0.9f);
        }

        /// <summary>A taped cardboard box (0.46 m) — the goods-in stack and the crates of a delivery.</summary>
        private Transform Karton(Vector3 world, float yaw)
        {
            var box = new GameObject("Karton").transform;
            box.SetParent(_root, false);
            box.position = world;
            box.rotation = Quaternion.Euler(0f, yaw, 0f);
            ArtLibrary.Box(box, "KartonBody", box.TransformPoint(0f, 0.22f, 0f), new Vector3(0.56f, 0.44f, 0.46f), M("#D9A86C", 0.15f));
            ArtLibrary.Box(box, "KartonTape", box.TransformPoint(0f, 0.445f, 0f), new Vector3(0.58f, 0.012f, 0.1f), M("#C08A4E", 0.3f), false);
            ArtLibrary.Box(box, "KartonLabel", box.TransformPoint(0.1f, 0.26f, -0.232f), new Vector3(0.18f, 0.12f, 0.01f), M("#FFF6EA", 0.1f), false);
            return box;
        }

        // ── the store grows with its level ──

        private void BuildLevelDeco()
        {
            // 2 Minimarkt: bunting across the room and the pester-power jar at the till
            for (var i = 0; i <= 18; i++)
            {
                var x = -6f + i * (12f / 18f);
                var y = 3.05f - Mathf.Sin(i / 18f * Mathf.PI) * 0.35f;
                var flag = ArtLibrary.Box(_root, "Bunting", At(x, y, 3.2f), new Vector3(0.2f, 0.2f, 0.02f), M(new[] { "#FF8B8B", "#FFD166", "#7FC8C0", "#B58CE4" }[i % 4], 0.2f), false);
                flag.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                _levelDeco[1].Add(flag);
            }
            _levelDeco[1].Add(ArtLibrary.Cylinder(_root, "PesterJar", At(CounterX - 1.05f, CounterTop + 0.13f, CounterZ - 0.25f), 0.1f, 0.24f, ArtLibrary.Transparent(new Color(0.95f, 0.97f, 1f, 0.5f)), false));
            for (var i = 0; i < 6; i++)
                _levelDeco[1].Add(ArtLibrary.Sphere(_root, "PesterSweet", At(CounterX - 1.05f + Mathf.Cos(i) * 0.05f, CounterTop + 0.05f + (i / 3) * 0.07f, CounterZ - 0.25f + Mathf.Sin(i) * 0.05f), 0.035f, M(new[] { "#FF8B8B", "#FFD166", "#7FB8FF" }[i % 3], 0.5f), false));
            _levelDeco[1].Add(Stage.HomeProp("plant", At(-0.2f, 0f, 3.75f), 0.55f).gameObject);
            // 3 Frischemarkt: fruit crates right of the door and a chalkboard sandwich sign by the aisle
            foreach (var (x, y, z, fruit) in new[] { (5.95f, 0f, 2.75f, "#FF6B6B"), (6.65f, 0f, 2.6f, "#FFD166"), (6.3f, 0.32f, 2.68f, "#7FCF9B") })
            {
                _levelDeco[2].Add(ArtLibrary.Box(_root, "FreshCrate", At(x, y + 0.16f, z), new Vector3(0.7f, 0.32f, 0.45f), M("#D9B48A", 0.2f)));
                for (var j = 0; j < 6; j++)
                    _levelDeco[2].Add(ArtLibrary.Sphere(_root, "FreshFruit", At(x - 0.24f + (j % 3) * 0.24f, y + 0.38f, z - 0.1f + (j / 3) * 0.2f), 0.1f, M(fruit, 0.4f), false));
            }
            var board = Stage.Glb(System.IO.Path.Combine(AssetLoader.Root, "scenery", "sandwich_board.glb"), At(6.0f, 0f, 1.3f), 1f, 200f);
            _levelDeco[2].Add(board.gameObject);
            // 4 Supermarkt: the bread corner — a little table with a basket of loaves and croissants
            const float bx = -5.35f, bz = 0.85f;
            _levelDeco[3].Add(ArtLibrary.Box(_root, "BreadTop", At(bx, 0.76f, bz), new Vector3(1.0f, 0.06f, 0.62f), M("#E2BE8E", 0.25f)));
            _levelDeco[3].Add(ArtLibrary.Box(_root, "BreadCloth", At(bx, 0.6f, bz - 0.3f), new Vector3(1.02f, 0.3f, 0.02f), M("#FF8B8B", 0.15f)));
            foreach (var (lx, lz) in new[] { (-0.42f, -0.24f), (0.42f, -0.24f), (-0.42f, 0.24f), (0.42f, 0.24f) })
                _levelDeco[3].Add(ArtLibrary.Box(_root, "BreadLeg", At(bx + lx, 0.37f, bz + lz), new Vector3(0.06f, 0.74f, 0.06f), M("#B98B5E", 0.2f)));
            _levelDeco[3].Add(ArtLibrary.Cylinder(_root, "BreadBasket", At(bx, 0.85f, bz), 0.36f, 0.12f, M("#C99A6B", 0.15f)));
            foreach (var (m, dx, s) in new[] { ("bread", -0.16f, 0.5f), ("croissant", 0.15f, 0.45f), ("bread", 0.04f, 0.45f) })
                _levelDeco[3].Add(Stage.Prop(m, At(bx + dx, 0.92f, bz + dx * 0.4f), s, dx * 200f).gameObject);
            // 5 XXL: string lights along the top and the glowing XXL sign over the door
            for (var i = 0; i < 24; i++)
            {
                var x = -6.8f + i * (13.6f / 23f);
                var y = 4.35f - Mathf.Abs(Mathf.Sin(i * 0.9f)) * 0.18f;
                _levelDeco[4].Add(ArtLibrary.Sphere(_root, "StringLight", At(x, y, 3.9f), 0.06f, ArtLibrary.Glow(ArtLibrary.Hex(new[] { "#FFE066", "#FF98C1", "#98E7E1" }[i % 3]) * 1.4f), false));
            }
            _levelDeco[4].Add(ArtLibrary.Quad(_root, "XxlSign", At(Door.x, 2.95f, 4.05f), Quaternion.Euler(0f, 180f, 0f), new Vector2(1.6f, 0.62f), ArtLibrary.Decal("sign_goobye_xxl")));
            _levelDeco[4].Add(ArtLibrary.Box(_root, "XxlGlow", At(Door.x, 2.95f, 4.08f), new Vector3(1.75f, 0.74f, 0.02f), ArtLibrary.Glow(ArtLibrary.Hex("#FF98C1") * 0.7f), false));
        }

        public void SetLevel(int level)
        {
            _level = Mathf.Clamp(level, 1, 5);
            for (var l = 1; l < _levelDeco.Length; l++)
                foreach (var go in _levelDeco[l])
                    if (go != null) go.SetActive(_level > l);
        }

        /// <summary>The level-up beat: the new decoration pops in with sparkles.</summary>
        public void LevelUp(int level)
        {
            SetLevel(level);
            var i = Mathf.Clamp(level - 1, 0, _levelDeco.Length - 1);
            foreach (var go in _levelDeco[i])
                if (go != null && UnityEngine.Random.value < 0.3f) Effects.Sparkle(go.transform.position, 0.3f, 6);
            Keeper?.Play("celebrate");
            Keeper?.SetEmotion("ecstatic");
        }

        public void SetOpen(bool open)
        {
            _open = open;
            if (_openSign != null) _openSign.material.color = ArtLibrary.Hex(open ? "#6FCF8F" : "#E8574C");
            foreach (var l in _lamps)
                if (l != null) l.material.color = ArtLibrary.Hex("#FFE2A8") * (open ? 1.45f : 0.95f);
        }

        // ── the slots ──

        private static readonly Dictionary<string, (string model, float scale, float tilt)> Foods = new Dictionary<string, (string, float, float)>
        {
            ["apple"] = ("apple", 0.34f, 0f), ["banana"] = ("banana", 0.32f, 0f), ["strawberry"] = ("strawberry", 0.5f, 0f),
            ["watermelon"] = ("watermelon", 0.34f, 0f), ["carrot"] = ("carrot", 0.5f, 70f), ["tomato"] = ("tomato", 1.0f, 0f),
            ["salad"] = ("salad", 0.32f, 0f), ["pumpkin"] = ("crop_pumpkin", 0.4f, 0f), ["bread"] = ("bread", 0.4f, 0f),
            ["croissant"] = ("croissant", 0.37f, 0f), ["muffin"] = ("muffin", 0.55f, 0f), ["donut-sprinkles"] = ("donut-sprinkles", 0.34f, 0f),
            ["cookie"] = ("cookie", 0.78f, 0f), ["cupcakePink"] = ("cupcake", 0.4f, 0f), ["cake"] = ("cake", 0.3f, 0f),
            ["cheese"] = ("cheese", 0.34f, 0f), ["ice-cream"] = ("ice-cream", 0.45f, 0f), ["sundae"] = ("ice-cream", 0.45f, 0f),
            ["gb_goo_cola"] = ("soda", 0.62f, 0f), ["cinnamonRoll"] = ("waffle", 0.5f, 0f),
        };

        /// <summary>One piece of a ware: its Kenney model, or a small primitive in its group's colour and shape.</summary>
        private Transform Piece(string ware, string farbe, string form, Vector3 world, Quaternion rot)
        {
            if (Foods.TryGetValue(ware, out var f))
            {
                var t = Stage.Prop(f.model, world, f.scale);
                t.rotation = rot * Quaternion.Euler(f.tilt, (world.x * 977f) % 50f - 25f, 0f);
                return t;
            }
            var anchor = new GameObject("Piece:" + ware).transform;
            anchor.SetParent(_root, false);
            anchor.position = world;
            anchor.rotation = rot;
            var c = M(string.IsNullOrEmpty(farbe) ? "#CCCCCC" : farbe, 0.35f);
            switch (ware)
            {
                case "grapes":
                    for (var i = 0; i < 6; i++)
                        ArtLibrary.Sphere(anchor, "Grape", world + rot * new Vector3((i % 3 - 1) * 0.045f, 0.04f + (i / 3) * 0.05f, (i % 2) * 0.03f), 0.035f, M("#9B6FD1", 0.5f), false).transform.SetParent(anchor, true);
                    break;
                case "corn":
                    ArtLibrary.Sphere(anchor, "Cob", world + rot * new Vector3(0f, 0.05f, 0f), 0.05f, M("#F7D354", 0.3f), false).transform.SetParent(anchor, true);
                    anchor.GetChild(0).localScale = new Vector3(0.09f, 0.09f, 0.22f);
                    break;
                case "lollypop":
                    ArtLibrary.Cylinder(anchor, "Stick", world + rot * new Vector3(0f, 0.08f, 0f), 0.008f, 0.16f, M("#FFFFFF"), false).transform.SetParent(anchor, true);
                    var disc = ArtLibrary.Cylinder(anchor, "Pop", world + rot * new Vector3(0f, 0.17f, 0f), 0.06f, 0.02f, M("#FF6FA8", 0.5f), false);
                    disc.transform.rotation = rot * Quaternion.Euler(90f, 0f, 0f);
                    disc.transform.SetParent(anchor, true);
                    break;
                case "gb_moehrenperlen":
                    ArtLibrary.Cylinder(anchor, "Jar", world + rot * new Vector3(0f, 0.07f, 0f), 0.05f, 0.14f, ArtLibrary.Transparent(new Color(1f, 0.7f, 0.4f, 0.75f)), false).transform.SetParent(anchor, true);
                    ArtLibrary.Cylinder(anchor, "Lid", world + rot * new Vector3(0f, 0.15f, 0f), 0.053f, 0.02f, M("#F2C94C", 0.4f), false).transform.SetParent(anchor, true);
                    break;
                default:
                    // boxes and bars: cereal (house brand), chocolate, candy bars
                    var w = ware == "gb_hoppel_pops" ? new Vector3(0.12f, 0.17f, 0.05f) : new Vector3(0.13f, 0.03f, 0.07f);
                    var body = ArtLibrary.Box(anchor, "Pack", world + rot * new Vector3(0f, w.y * 0.5f, 0f), w, ware == "chocolate" ? M("#7A4A2E") : c, false);
                    body.transform.rotation = rot;
                    body.transform.SetParent(anchor, true);
                    if (ware == "gb_hoppel_pops")
                    {
                        var face = ArtLibrary.Sphere(anchor, "Face", world + rot * new Vector3(0f, 0.1f, -0.03f), 0.035f, M("#FFF6EA", 0.2f), false);
                        face.transform.SetParent(anchor, true);
                    }
                    break;
            }
            return anchor;
        }

        /// <summary>Shows a slot's ware and count — pieces are rebuilt only when the ware changes, otherwise added or popped.</summary>
        public void SetSlot(int i, string ware, int menge, bool cold, string farbe, string form)
        {
            if (i < 0 || i >= Slots || _destroyed) return;
            ware = ware ?? "";
            menge = ware.Length == 0 ? 0 : Mathf.Clamp(menge, 0, 8);
            if (_slotWare[i] != ware)
            {
                foreach (var p in _pieces[i]) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
                _pieces[i].Clear();
                _slotWare[i] = ware;
            }
            if (_slotCold[i] != cold && _crateRenderers[i] != null)
                _crateRenderers[i].material.color = ArtLibrary.Hex(cold ? "#CFE9FF" : "#E2BE8E");
            _slotCold[i] = cold;
            var slot = _slotRoots[i];
            while (_pieces[i].Count < menge)
            {
                var n = _pieces[i].Count;
                var local = new Vector3(((n % 4) - 1.5f) * 0.165f, 0.05f + (n / 4) * 0.02f, (n / 4 == 0 ? -0.1f : 0.11f));
                var piece = Piece(ware, farbe, form, slot.TransformPoint(local), slot.rotation);
                piece.SetParent(slot, true);
                _pieces[i].Add(piece);
            }
            while (_pieces[i].Count > menge)
            {
                var last = _pieces[i][_pieces[i].Count - 1];
                _pieces[i].RemoveAt(_pieces[i].Count - 1);
                if (last == null) continue;
                Effects.Puff(last.position + Vector3.up * 0.08f, new Color(1f, 0.96f, 0.85f, 0.8f), 3, 0.06f, 0.5f, 0.35f, Vector3.up, 0.5f);
                UnityEngine.Object.Destroy(last.gameObject);
            }
        }

        /// <summary>A stocking beat: a little hop of the crate and sparkles.</summary>
        public void SlotSparkle(int i)
        {
            if (i < 0 || i >= Slots || _slotRoots[i] == null) return;
            Effects.Sparkle(_slotRoots[i].position + Vector3.up * 0.25f, 0.35f, 14);
        }

        // ── the till's goods ──

        /// <summary>The customer's basket lands on the belt, one piece per position.</summary>
        public void PutOnBelt(IList<string> wares, Func<string, (string farbe, string form)> look)
        {
            ClearBelt();
            for (var k = 0; k < wares.Count && k < 10; k++)
            {
                var (farbe, form) = look(wares[k]);
                var world = At(CounterX - 0.95f + (k % 5) * 0.2f, CounterTop + 0.04f, CounterZ - 0.14f + (k / 5) * 0.18f);
                _counterGoods.Add(Piece(wares[k], farbe, form, world, Quaternion.Euler(0f, 180f, 0f)));
            }
        }

        /// <summary>One beep: the next piece slides into the bag, the register blinks.</summary>
        public void Beep(int k)
        {
            _registerFlash = 0.18f;
            if (k < 0 || k >= _counterGoods.Count || _counterGoods[k] == null) return;
            Effects.Puff(_counterGoods[k].position + Vector3.up * 0.06f, new Color(1f, 0.95f, 0.6f, 0.9f), 2, 0.05f, 0.6f, 0.3f, Vector3.up, 0.4f);
            UnityEngine.Object.Destroy(_counterGoods[k].gameObject);
            _counterGoods[k] = null;
            Keeper?.BabblePulse();
        }

        public void ClearBelt()
        {
            foreach (var g in _counterGoods) if (g != null) UnityEngine.Object.Destroy(g.gameObject);
            _counterGoods.Clear();
        }

        public void KeeperCheer(bool big)
        {
            if (Keeper == null) return;
            Keeper.SetEmotion(big ? "ecstatic" : "happy");
            Keeper.Play(big ? "celebrate" : "hop");
            _keeperHop = big ? 0f : 0.5f;
        }

        public void KeeperWave()
        {
            if (Keeper == null) return;
            Keeper.SetEmotion("happy");
            Keeper.Play("wave");
        }

        /// <summary>The door slides and the bell swings (someone comes or goes).</summary>
        public void Ring()
        {
            _doorOpen = 1.2f;
            _bellSwing = 1f;
        }

        // ── the delivery ──

        /// <summary>Crates from the wholesale run stacked by the door (0 = none).</summary>
        public void SetDelivery(int crates)
        {
            var n = Mathf.Clamp(crates, 0, 9);
            while (_deliveryCrates.Count > n)
            {
                var c = _deliveryCrates[_deliveryCrates.Count - 1];
                _deliveryCrates.RemoveAt(_deliveryCrates.Count - 1);
                if (c == null) continue;
                Effects.Puff(c.position + Vector3.up * 0.15f, new Color(0.95f, 0.85f, 0.65f, 0.9f), 4, 0.1f, 0.8f, 0.4f, Vector3.up, 0.6f);
                UnityEngine.Object.Destroy(c.gameObject);
            }
            while (_deliveryCrates.Count < n)
            {
                var k = _deliveryCrates.Count;
                var c = Karton(At(-5.55f + (k % 3) * 0.6f, (k / 3) * 0.45f, 1.55f - (k % 2) * 0.1f), (k * 23f) % 30f - 15f);
                Effects.Puff(c.position + Vector3.up * 0.2f, new Color(0.95f, 0.85f, 0.65f, 0.8f), 3, 0.08f, 0.5f, 0.35f, Vector3.up, 0.5f);
                _deliveryCrates.Add(c);
            }
        }

        // ── the keeper ──

        private async Task SpawnKeeperAsync(Dictionary<string, string> outfit)
        {
            var keeper = await Stage.SpawnGoobyAsync(At(KeeperSpot), 1.65f, outfit);
            if (keeper == null || _destroyed) return;
            keeper.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            keeper.SetEmotion("happy");
            Keeper = keeper;
        }

        // ── customers ──

        /// <summary>
        /// A customer walking a script: go to a point, face a way, wait, play a clip, run an action. Positions are
        /// stage-local; without an avatar (no stage, or still loading) the walk still takes its time.
        /// </summary>
        public sealed class Kunde
        {
            private enum Art { Geh, Dreh, Warte, WarteBis, Tu, Clip }

            private struct Schritt
            {
                public Art Art;
                public Vector3 Ziel;
                public float Wert;
                public Action Aktion;
                public Func<bool> Bedingung;
                public string Clip, Emotion;
            }

            public readonly string Archetyp;
            public GoobyAvatar Avatar, Kind;
            public Vector3 Pos;
            public float Yaw = 180f;
            public bool Fertig { get; private set; }
            private readonly List<Schritt> _schritte = new List<Schritt>();
            private int _i;
            private float _warte, _spawn;
            private const float Tempo = 2.5f;

            public Kunde(string archetyp, Vector3 start)
            {
                Archetyp = archetyp;
                Pos = start;
            }

            public Kunde Geh(Vector3 ziel) { _schritte.Add(new Schritt { Art = Art.Geh, Ziel = ziel }); return this; }
            public Kunde Dreh(float yaw) { _schritte.Add(new Schritt { Art = Art.Dreh, Wert = yaw }); return this; }
            public Kunde Warte(float s) { _schritte.Add(new Schritt { Art = Art.Warte, Wert = s }); return this; }
            public Kunde WarteBis(Func<bool> bedingung) { _schritte.Add(new Schritt { Art = Art.WarteBis, Bedingung = bedingung }); return this; }
            public Kunde Tu(Action a) { _schritte.Add(new Schritt { Art = Art.Tu, Aktion = a }); return this; }
            public Kunde Clip(string clip, string emotion = null) { _schritte.Add(new Schritt { Art = Art.Clip, Clip = clip, Emotion = emotion }); return this; }

            public Vector3 Kopf => Pos + Vector3.up * 1.6f;

            public void Tick(float dt, float speed)
            {
                _spawn = Mathf.Min(1f, _spawn + dt * 3f * speed);
                var walking = false;
                var guard = 0;
                while (!Fertig && guard++ < 8)
                {
                    if (_i >= _schritte.Count) { Fertig = true; break; }
                    var s = _schritte[_i];
                    if (s.Art == Art.Geh)
                    {
                        var to = s.Ziel - Pos;
                        to.y = 0f;
                        var step = Tempo * speed * dt;
                        if (to.magnitude <= step) { Pos = s.Ziel; _i++; continue; }
                        Pos += to.normalized * step;
                        Yaw = Mathf.MoveTowardsAngle(Yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 720f * dt * speed);
                        walking = true;
                        break;
                    }
                    if (s.Art == Art.Dreh)
                    {
                        Yaw = Mathf.MoveTowardsAngle(Yaw, s.Wert, 540f * dt * speed);
                        if (Mathf.Abs(Mathf.DeltaAngle(Yaw, s.Wert)) < 1f) _i++;
                        break;
                    }
                    if (s.Art == Art.Warte)
                    {
                        _warte += dt * speed;
                        if (_warte >= s.Wert) { _warte = 0f; _i++; continue; }
                        break;
                    }
                    if (s.Art == Art.WarteBis)
                    {
                        if (s.Bedingung == null || s.Bedingung()) { _i++; continue; }
                        break;
                    }
                    if (s.Art == Art.Clip)
                    {
                        if (Avatar != null)
                        {
                            Avatar.Play(s.Clip);
                            if (s.Emotion != null) Avatar.SetEmotion(s.Emotion);
                        }
                        _i++;
                        continue;
                    }
                    _i++;
                    s.Aktion?.Invoke();
                }
                Place(Avatar, Pos, 1f, walking);
                if (Kind != null) Place(Kind, Pos + Quaternion.Euler(0f, Yaw, 0f) * new Vector3(0.62f, 0f, -0.25f), 0.62f, walking);
            }

            private void Place(GoobyAvatar a, Vector3 pos, float scale, bool walking)
            {
                if (a == null) return;
                a.transform.position = MgStage3D.Origin + pos;
                a.transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
                var s = 1.35f * scale * Mathf.SmoothStep(0.2f, 1f, _spawn);
                a.transform.localScale = Vector3.one * s;
                if (walking && a.Clip != "walk-loop") a.Play("walk-loop");
                else if (!walking && a.Clip == "walk-loop") a.Play("idle-loop");
            }
        }

        /// <summary>Puts the customer's Gooby on the stage (async); the family brings its little one.</summary>
        public async Task AttachAsync(Kunde k, Dictionary<string, string> look, Dictionary<string, string> kindLook)
        {
            var a = await GoobyAvatar.CreateAsync(_root, At(k.Pos));
            if (a == null) return;
            if (_destroyed || k.Fertig) { UnityEngine.Object.Destroy(a.gameObject); return; }
            a.Wear(look);
            a.SetEmotion("happy");
            k.Avatar = a;
            if (kindLook == null) return;
            var kid = await GoobyAvatar.CreateAsync(_root, At(k.Pos));
            if (kid == null) return;
            if (_destroyed || k.Fertig) { UnityEngine.Object.Destroy(kid.gameObject); return; }
            kid.Wear(kindLook);
            kid.SetEmotion("ecstatic");
            k.Kind = kid;
        }

        public void Remove(Kunde k)
        {
            if (k == null) return;
            if (k.Avatar != null) UnityEngine.Object.Destroy(k.Avatar.gameObject);
            if (k.Kind != null) UnityEngine.Object.Destroy(k.Kind.gameObject);
            k.Avatar = null;
            k.Kind = null;
        }

        public void Tick(float dt)
        {
            _t += dt;
            if (_doorPanel != null)
            {
                _doorOpen = Mathf.Max(0f, _doorOpen - dt);
                var target = _doorOpen > 0f ? 1.15f : 0f;
                var p = _doorPanel.position;
                var x = Mathf.MoveTowards(p.x - MgStage3D.Origin.x, Door.x + target, dt * 3f);
                _doorPanel.position = new Vector3(MgStage3D.Origin.x + x, p.y, p.z);
            }
            if (_bell != null)
            {
                _bellSwing = Mathf.Max(0f, _bellSwing - dt * 0.8f);
                _bell.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_t * 18f) * 22f * _bellSwing);
            }
            if (_register != null)
            {
                _registerFlash = Mathf.Max(0f, _registerFlash - dt);
                _register.localScale = Vector3.one * (0.5f * (1f + _registerFlash * 0.25f));
            }
            if (Keeper != null)
            {
                var y = 0f;
                if (_keeperHop > 0f) { _keeperHop = Mathf.Max(0f, _keeperHop - dt); y = Mathf.Sin((0.5f - _keeperHop) / 0.5f * Mathf.PI) * 0.18f; }
                Keeper.transform.position = At(KeeperSpot) + Vector3.up * y;
            }
            if (_open && _level >= 5)
                foreach (var go in _levelDeco[4])
                    if (go != null && go.name == "XxlGlow") go.transform.localScale = new Vector3(1.75f, 0.74f, 0.02f) * (1f + Mathf.Sin(_t * 3f) * 0.03f);
        }

        public void Destroy()
        {
            _destroyed = true;
            Stage.Destroy();
        }
    }
}
