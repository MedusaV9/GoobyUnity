using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Garden;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The player's own stall on the market square (port of wochenmarkt.gd's Eigenstand): where the yellow stall stood,
    /// a counter under a sun-and-cream awning with a "Goobys Stand" board. The stocked wares lie on it in little crates
    /// (the food models where the kit has them, else produce in the crop's colour; workshop pieces as their prop), Gooby
    /// stands behind it on a step in a red apron, and on market day three customer Goobys queue in front: the first one
    /// hops when she buys or shakes her head and walks on, and the queue moves up. The stall sits just outside the door
    /// view; the stall sheet and the market day glide the camera over (<see cref="StallCameraPosition"/>).
    /// </summary>
    public sealed partial class InteriorScene
    {
        private static readonly Vector3 StallAt = new Vector3(4.2f, 0f, -2.0f);
        private const float StallYaw = -22f;
        private static readonly Vector3[] Queue = { new Vector3(0.1f, 0f, 1.3f), new Vector3(-0.75f, 0f, 2.05f), new Vector3(0.65f, 0f, 2.6f) };
        private static readonly Vector3 QueueExit = new Vector3(2.6f, 0f, 2.2f);
        private static readonly (string fell, string hut, string hals)[] Kunden =
        {
            ("caramel", "beret", null), ("fell_himmel", "cap", "scarfRed"), ("rose", "strawHat", null),
        };

        private Transform _stall, _wareRoot;
        private GoobyAvatar _stallGooby;
        private bool _stallGoobyLoading, _customersLoading;
        private string _waresKey;
        private int _waresToken;
        private readonly List<Customer> _customers = new List<Customer>();
        private float _cheerLeft;

        private sealed class Customer
        {
            public GoobyAvatar Avatar;
            public Vector3 From, To;
            public float T = 1f, Dur = 0.45f, ActLeft;
            public int Slot;
        }

        public bool HasOwnStall => _stall != null;

        /// <summary>Camera spot for the stall sheet: in front of the stall, aimed past its right side so it fills the left half.</summary>
        public Vector3 StallCameraPosition => _stall != null ? StallPoint(new Vector3(-0.9f, 2.05f, 5.2f)) : CameraPosition;
        public Quaternion StallCameraRotation => _stall != null ? Quaternion.LookRotation(StallPoint(new Vector3(-1.45f, 0.95f, 0.3f)) - StallCameraPosition, Vector3.up) : CameraRotation;

        /// <summary>Market day: the stall in the middle of the picture, the queue in front of it.</summary>
        public Vector3 StallDayCameraPosition => _stall != null ? StallPoint(new Vector3(-0.35f, 2.8f, 6.3f)) : CameraPosition;
        public Quaternion StallDayCameraRotation => _stall != null ? Quaternion.LookRotation(StallPoint(new Vector3(0f, 1.0f, 0.8f)) - StallDayCameraPosition, Vector3.up) : CameraRotation;

        /// <summary>Just above the counter's front edge — where the coins of a sale pop up.</summary>
        public Vector3 StallCounterWorld => _stall != null ? StallPoint(new Vector3(0.1f, 1.25f, 0.62f)) : Origin;

        private Vector3 StallPoint(Vector3 local) => _stall.TransformPoint(local);

        private GameObject StallBox(string name, Vector3 local, Vector3 size, Material m, bool shadows = true) =>
            ArtLibrary.Box(_stall, name, StallPoint(local), size, m, shadows);

        private void BuildOwnStall()
        {
            _stall = new GameObject("OwnStall").transform;
            _stall.SetParent(_t, false);
            _stall.position = Origin + StallAt;
            _stall.rotation = Quaternion.Euler(0f, StallYaw, 0f);
            var wood = ArtLibrary.Pbr("wood_planks", 0.5f, ArtLibrary.Hex("#E2B889"), 0.18f, 0.55f);
            var dark = ArtLibrary.Pbr("wood_planks", 0.5f, ArtLibrary.Hex("#B98B5E"), 0.18f, 0.55f);
            var cream = Solid("#FFF8EE", 0.2f);
            var sun = Solid("#FFD166", 0.25f);
            foreach (var x in new[] { -1.08f, 1.08f })
                foreach (var z in new[] { -0.58f, 0.58f })
                    StallBox("Post", new Vector3(x, 1.2f, z), new Vector3(0.09f, 2.4f, 0.09f), dark);
            StallBox("Counter", new Vector3(0f, 0.88f, 0.05f), new Vector3(2.32f, 0.08f, 1.28f), wood);
            StallBox("Front", new Vector3(0f, 0.44f, 0.66f), new Vector3(2.32f, 0.84f, 0.06f), cream);
            StallBox("FrontStripe", new Vector3(0f, 0.64f, 0.7f), new Vector3(2.32f, 0.16f, 0.02f), sun, false);
            StallBox("Shelf", new Vector3(0f, 0.3f, 0.05f), new Vector3(2.2f, 0.05f, 1.15f), dark);
            for (var i = 0; i < 6; i++)
            {
                var stripe = StallBox("Awning", new Vector3(-1.125f + i * 0.45f, 2.45f, 0.14f), new Vector3(0.45f, 0.05f, 1.6f), i % 2 == 0 ? sun : cream);
                stripe.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
                var scallop = ArtLibrary.Sphere(_stall, "Scallop", StallPoint(new Vector3(-1.125f + i * 0.45f, 2.22f, 0.93f)), 0.21f, i % 2 == 0 ? sun : cream, false);
                scallop.transform.localScale = new Vector3(0.44f, 0.2f, 0.05f);
            }
            StallBox("SignBoard", new Vector3(0f, 2.86f, 0.5f), new Vector3(1.56f, 0.52f, 0.05f), Solid("#8B5E3C", 0.2f));
            var lang = App.GameHost.Instance != null && App.GameHost.Instance.Settings.Locale == "en" ? "en" : "de";
            ArtLibrary.Quad(_stall, "Sign", StallPoint(new Vector3(0f, 2.86f, 0.53f)), _stall.rotation, new Vector2(1.46f, 0.44f), ArtLibrary.Decal("stall_gooby_" + lang));
            StallBox("Step", new Vector3(0f, 0.2f, -0.98f), new Vector3(1.3f, 0.4f, 0.75f), Solid("#D9B48A", 0.1f));
            StallBox("CashBox", new Vector3(0.9f, 0.99f, -0.3f), new Vector3(0.3f, 0.14f, 0.22f), Solid("#FF8B8B", 0.35f));
            StallBox("CashLid", new Vector3(0.9f, 1.07f, -0.3f), new Vector3(0.32f, 0.03f, 0.24f), Solid("#F26D6D", 0.35f));
            _wareRoot = new GameObject("Wares").transform;
            _wareRoot.SetParent(_stall, false);
        }

        /// <summary>Puts the stocked wares on the counter (up to eight crates in two rows); unchanged stock keeps the crates.</summary>
        public void SetStallWares(IList<MarktSim.Slot> slots)
        {
            if (_stall == null) return;
            var key = string.Join("|", (slots ?? new List<MarktSim.Slot>()).Select(s => s.Ware + ":" + Mathf.Min(s.Menge, 4)));
            if (key == _waresKey) return;
            _waresKey = key;
            var token = ++_waresToken;
            for (var i = _wareRoot.childCount - 1; i >= 0; i--) Object.Destroy(_wareRoot.GetChild(i).gameObject);
            if (slots == null) return;
            var crate = ArtLibrary.Pbr("wood_planks", 0.4f, ArtLibrary.Hex("#C99A6B"), 0.18f, 0.55f);
            for (var i = 0; i < Mathf.Min(slots.Count, 8); i++)
            {
                var s = slots[i];
                var local = new Vector3(-0.8f + (i % 4) * 0.53f, 0.92f, i < 4 ? 0.3f : -0.18f);
                var w = MarktWaren.Ware(s.Ware);
                if (w != null && w.Lager == "storage")
                {
                    var anchor = Anchor("Piece:" + s.Ware, local);
                    _ = FitModel(AssetLoader.PropPath(AssetLoader.ArchetypeFor(s.Ware)), anchor, 0.42f, token);
                    continue;
                }
                var box = ArtLibrary.Box(_wareRoot, "Crate", StallPoint(local + new Vector3(0f, 0.06f, 0f)), new Vector3(0.44f, 0.12f, 0.32f), crate);
                box.transform.SetParent(_wareRoot, true);
                var n = Mathf.Clamp(s.Menge, 1, 4);
                var model = FoodModel(s.Ware);
                for (var k = 0; k < n; k++)
                {
                    var p = local + new Vector3(-0.12f + (k % 2) * 0.24f, 0.15f + (k / 2) * 0.05f, -0.06f + (k / 2) * 0.1f);
                    if (model != null)
                    {
                        var a = Anchor("Food:" + s.Ware, p);
                        a.localRotation = Quaternion.Euler(0f, k * 70f, 0f);
                        _ = FitModel(Path.Combine(AssetLoader.Root, "minigame", model + ".glb"), a, 0.17f, token);
                    }
                    else
                    {
                        var ball = ArtLibrary.Sphere(_wareRoot, "Produce", StallPoint(p), 0.075f, Solid(ProduceColor(s.Ware), 0.35f));
                        ball.transform.SetParent(_wareRoot, true);
                    }
                }
            }
        }

        private Transform Anchor(string name, Vector3 local)
        {
            var a = new GameObject(name).transform;
            a.SetParent(_wareRoot, false);
            a.localPosition = local;
            return a;
        }

        private static string FoodModel(string ware)
        {
            switch (ware)
            {
                case "carrot": case "weltraumMoehre": return "carrot";
                case "tomato": return "tomato";
                case "salad": return "salad";
                case "watermelon": return "watermelon";
                case "pumpkin": return "crop_pumpkin";
                default: return null;
            }
        }

        private static Color ProduceColor(string ware)
        {
            if (ware == "kraeuter") return ArtLibrary.Hex("#7FCF9B");
            var crop = GardenCrops.Crop(ware);
            return crop != null ? ArtLibrary.Hex(crop.Farbe) : ArtLibrary.Hex("#FFB46B");
        }

        private async System.Threading.Tasks.Task FitModel(string path, Transform anchor, float max, int token)
        {
            var go = await AssetLoader.InstanceAsync(path, anchor, Path.GetFileNameWithoutExtension(path));
            if (go == null) return;
            if (anchor == null || token != _waresToken) { Object.Destroy(go); return; }
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (size > 0.001f) go.transform.localScale = Vector3.one * (max / size);
        }

        /// <summary>Gooby behind his counter in the player's outfit and a red apron (spawned once, on demand).</summary>
        public async System.Threading.Tasks.Task ShowStallGoobyAsync(Dictionary<string, string> outfit)
        {
            if (_stall == null || _stallGooby != null || _stallGoobyLoading) return;
            _stallGoobyLoading = true;
            var spot = StallPoint(new Vector3(0f, 0.4f, -0.95f));
            var avatar = await GoobyAvatar.CreateAsync(_t, spot);
            _stallGoobyLoading = false;
            if (avatar == null || Root == null) return;
            avatar.transform.position = spot;
            avatar.transform.rotation = _stall.rotation;
            avatar.transform.localScale = Vector3.one * 1.3f;
            if (outfit != null) avatar.Wear(outfit);
            avatar.Play("idle-loop");
            avatar.SetEmotion("happy");
            Apron(avatar);
            _stallGooby = avatar;
        }

        private static void Apron(GoobyAvatar avatar)
        {
            var root = avatar.transform;
            var red = ArtLibrary.Solid(ArtLibrary.Hex("#E2574C"), 0.25f);
            var light = ArtLibrary.Solid(ArtLibrary.Hex("#F7E6C8"), 0.25f);
            var s = root.lossyScale.x;
            var apron = new GameObject("Apron").transform;
            apron.SetParent(root, false);
            apron.localPosition = new Vector3(0f, 0.33f, 0.335f);
            apron.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            void Part(string n, Vector3 p, Vector3 size, Material m)
            {
                var go = ArtLibrary.Box(apron, n, apron.TransformPoint(p), size * s, m, false);
                go.transform.SetParent(apron, true);
            }
            Part("Bib", Vector3.zero, new Vector3(0.36f, 0.3f, 0.03f), red);
            Part("Pocket", new Vector3(0f, -0.06f, 0.018f), new Vector3(0.2f, 0.1f, 0.012f), light);
            Part("Strap", new Vector3(0f, 0.2f, -0.02f), new Vector3(0.05f, 0.14f, 0.025f), red);
            var spine = avatar.Bone("spine");
            if (spine != null) apron.SetParent(spine, true);
        }

        /// <summary>Three customer Goobys queue in front of the stall for the market day.</summary>
        public async System.Threading.Tasks.Task SpawnCustomersAsync()
        {
            if (_stall == null || _customers.Count > 0 || _customersLoading) return;
            _customersLoading = true;
            for (var i = 0; i < Queue.Length; i++)
            {
                var pos = StallPoint(Queue[i]);
                var avatar = await GoobyAvatar.CreateAsync(_t, pos);
                if (avatar == null || Root == null) break;
                avatar.transform.position = pos;
                avatar.transform.rotation = _stall.rotation * Quaternion.Euler(0f, 180f, 0f);
                avatar.transform.localScale = Vector3.one * 1.2f;
                var look = Kunden[i % Kunden.Length];
                var equipped = new Dictionary<string, string> { ["fell"] = look.fell };
                if (look.hut != null) equipped["hut"] = look.hut;
                if (look.hals != null) equipped["hals"] = look.hals;
                avatar.Wear(equipped);
                avatar.Play("idle-loop");
                avatar.SetEmotion("happy");
                _customers.Add(new Customer { Avatar = avatar, From = pos, To = pos, Slot = i });
            }
            _customersLoading = false;
        }

        public bool CustomersReady => _customers.Count == Queue.Length;

        /// <summary>One customer at the counter: she buys (hop, Gooby beams) or shakes her head; then the queue moves up.</summary>
        public void CustomerStep(bool bought, bool reducedMotion)
        {
            var front = _customers.FirstOrDefault(c => c.Slot == 0);
            if (front == null) return;
            if (bought)
            {
                front.Avatar.SetEmotion("ecstatic");
                front.Avatar.Play("hop", 0.1f);
                Effects.Sparkle(StallPoint(new Vector3(0.1f, 1.15f, 0.65f)), 0.25f, reducedMotion ? 4 : 12);
                if (_stallGooby != null)
                {
                    _stallGooby.SetEmotion("ecstatic");
                    _cheerLeft = 0.6f;
                }
            }
            else
            {
                front.Avatar.SetEmotion("neutral");
                front.Avatar.Play("refuse", 0.1f);
            }
            front.ActLeft = reducedMotion ? 0.05f : 0.32f;
        }

        public void CheerStall()
        {
            if (_stallGooby == null) return;
            _stallGooby.SetEmotion("ecstatic");
            _stallGooby.Play("celebrate", 0.12f);
        }

        public void ClearCustomers()
        {
            foreach (var c in _customers) if (c.Avatar != null) Object.Destroy(c.Avatar.gameObject);
            _customers.Clear();
        }

        private void Move(Customer c, Vector3 local, float seconds)
        {
            c.From = c.Avatar.transform.position;
            c.To = StallPoint(local);
            c.T = 0f;
            c.Dur = seconds;
            c.Avatar.Play("walk-loop", 0.12f);
        }

        private void TickStall(float dt, bool reducedMotion)
        {
            if (_stall == null || dt <= 0f) return;
            if (_cheerLeft > 0f)
            {
                _cheerLeft -= dt;
                if (_cheerLeft <= 0f && _stallGooby != null) _stallGooby.SetEmotion("happy");
            }
            foreach (var c in _customers)
            {
                if (c.Avatar == null) continue;
                if (c.ActLeft > 0f)
                {
                    c.ActLeft -= dt;
                    if (c.ActLeft <= 0f)
                    {
                        // the queue moves up: the front walks off round the side to the back, the others step forward
                        foreach (var o in _customers)
                        {
                            o.Slot = (o.Slot + Queue.Length - 1) % Queue.Length;
                            Move(o, o.Slot == Queue.Length - 1 ? QueueExit : Queue[o.Slot], reducedMotion ? 0.05f : 0.42f);
                        }
                        break;
                    }
                }
                if (c.T >= 1f) continue;
                c.T = Mathf.Min(1f, c.T + dt / Mathf.Max(0.01f, c.Dur));
                var p = Vector3.Lerp(c.From, c.To, Mathf.SmoothStep(0f, 1f, c.T));
                c.Avatar.transform.position = p;
                var dir = c.To - c.From;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f && c.T < 0.95f) c.Avatar.transform.rotation = Quaternion.Slerp(c.Avatar.transform.rotation, Quaternion.LookRotation(dir), 0.3f);
                if (c.T >= 1f)
                {
                    if (c.Slot == Queue.Length - 1 && Vector3.Distance(c.To, StallPoint(QueueExit)) < 0.05f)
                    {
                        // round the side and into the back of the queue
                        Move(c, Queue[c.Slot], reducedMotion ? 0.05f : 0.35f);
                        continue;
                    }
                    c.Avatar.transform.rotation = _stall.rotation * Quaternion.Euler(0f, 180f, 0f);
                    c.Avatar.Play("idle-loop", 0.15f);
                    c.Avatar.SetEmotion("happy");
                }
            }
        }
    }
}
