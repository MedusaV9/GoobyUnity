using System.Threading.Tasks;
using Gooby.Core.City;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// 3D reel for the travel cutscene (reise_cutscene.gd shots): a far stage (x=400) with the town house,
    /// the rigged Gooby, the taxi GLB, an avenue of scenery houses and the airport terminal + plane.
    /// The screen drives the shot id; this class poses the meshes and the camera.
    /// </summary>
    public sealed class TravelCutsceneScene
    {
        public static readonly Vector3 Origin = new Vector3(400f, 0f, 0f);

        public GameObject Root { get; }
        public Vector3 CameraPosition { get; private set; }
        public Quaternion CameraRotation { get; private set; }
        public Color Sky => ArtLibrary.Hex("73AEE8");
        public Vector3 GoobySpot => Origin + new Vector3(0f, 0f, 2f);

        private readonly Transform _t, _set;
        private Transform _taxi, _plane, _gooby;
        private GoobyAvatar _avatar;
        private string _shot;
        private float _shotT;
        private bool _hopped, _hiddenGooby;

        public TravelCutsceneScene(Transform parent, string modus)
        {
            Root = new GameObject("TravelCutscene");
            Root.transform.SetParent(parent, false);
            Root.transform.position = Origin;
            _t = Root.transform;
            _set = new GameObject("Set").transform;
            _set.SetParent(_t, false);
            CameraPosition = Origin + new Vector3(4f, 2.2f, 4f);
            CameraRotation = Quaternion.LookRotation((Origin + new Vector3(0f, 1f, -2f)) - CameraPosition, Vector3.up);
            if (modus == TravelCutscene.ModusRueckkehr) ApplyShot("landing");
            else ApplyShot("door");
        }

        public void ApplyShot(string id)
        {
            _shot = id;
            _shotT = 0f;
            _hopped = _hiddenGooby = false;
            _avatar = null;
            ClearSet();
            var grass = ArtLibrary.PbrSlab("grass", new Vector2(80f, 80f), 4f, ArtLibrary.Hex("8FBB90"), 0.08f, 0.6f);
            ArtLibrary.Box(_set, "Ground", Origin + new Vector3(0f, -0.1f, 0f), new Vector3(80f, 0.2f, 80f), grass, false);
            // the town painting behind every shot (the cameras look towards −Z), folded wings towards the lens
            var horizonAt = Origin + new Vector3(0f, -4f, -62f);
            ArtLibrary.Horizon(_set, "bg_town_horizon", horizonAt, 42f).RotateAround(horizonAt, Vector3.up, 180f);
            switch (id)
            {
                case "door":
                    HouseYard();
                    _gooby = SpawnGooby(new Vector3(0f, 0f, -2f), "walk-loop");
                    CameraPosition = Origin + new Vector3(4f, 2.2f, 4f);
                    CameraRotation = Quaternion.LookRotation(Origin + new Vector3(0f, 1f, -2f) - CameraPosition, Vector3.up);
                    break;
                case "driveway":
                    HouseYard();
                    _gooby = SpawnGooby(new Vector3(0f, 0f, 2f), "wave");
                    CameraPosition = Origin + new Vector3(4f, 2.2f, 4f);
                    CameraRotation = Quaternion.LookRotation(Origin + new Vector3(0f, 1f, 0f) - CameraPosition, Vector3.up);
                    break;
                case "taxi":
                    Road(new Vector3(0f, 0f, 0f), 40f, 6f);
                    Pavement(new Vector3(0f, 0f, 4.2f), 40f, 2.4f);
                    Scenery.Place(_set, "tree_tall", new Vector3(-10f, 0f, -8f), 20f, 1.1f);
                    Scenery.Place(_set, "tree_small", new Vector3(8f, 0f, -7f), -15f, 0.9f);
                    Scenery.Place(_set, "house_cream", new Vector3(-12f, 0f, -10f), 15f, 0.95f);
                    _gooby = SpawnGooby(new Vector3(2f, 0f, 1.5f), "idle-loop");
                    _taxi = Taxi(new Vector3(-18f, 0f, 0f));
                    CameraPosition = Origin + new Vector3(6f, 2.4f, 6f);
                    CameraRotation = Quaternion.LookRotation(Origin + new Vector3(0f, 0.8f, 0f) - CameraPosition, Vector3.up);
                    break;
                case "city":
                    Road(new Vector3(0f, 0f, 0f), 80f, 8f);
                    Pavement(new Vector3(0f, 0f, -5.6f), 80f, 3.2f);
                    Pavement(new Vector3(0f, 0f, 5.6f), 80f, 3.2f);
                    var houses = new[] { "house_salmon", "house_blue", "house_cream", "shop_mart", "house_yellow", "house_pink", "house_green" };
                    for (var i = 0; i < houses.Length; i++)
                        Scenery.Place(_set, houses[i], new Vector3(-30f + i * 10f, 0f, -12f), 0f, 1.05f);
                    Scenery.Place(_set, "tree_tall", new Vector3(-16f, 0f, -6.5f), 0f, 1.05f);
                    Scenery.Place(_set, "tree_tall", new Vector3(8f, 0f, -6.5f), 12f, 1.12f);
                    for (var x = -24f; x <= 24f; x += 12f) Scenery.Place(_set, "lamp_post", new Vector3(x + 3f, 0f, -4.6f), 0f, 1f);
                    Scenery.Place(_set, "church_tower", new Vector3(22f, 0f, -16f), 0f, 1.2f);
                    _taxi = Taxi(new Vector3(-20f, 0.2f, 2.5f));
                    CameraPosition = Origin + new Vector3(0f, 16f, 22f);
                    CameraRotation = Quaternion.LookRotation(Origin - CameraPosition, Vector3.up);
                    break;
                case "airport":
                case "landing":
                    Apron();
                    _plane = PlacePlane(id == "landing" ? LandFrom : RollFrom);
                    _plane.rotation = id == "landing" ? Quaternion.Euler(6f, -53f, 0f) : Quaternion.Euler(0f, 90f, 0f);
                    CameraPosition = Origin + new Vector3(0f, 2.2f, 16f);
                    CameraRotation = Quaternion.LookRotation(Origin + new Vector3(0f, 4f, -10f) - CameraPosition, Vector3.up);
                    break;
                case "walk":
                case "celebrate":
                    Apron();
                    _gooby = SpawnGooby(id == "walk" ? new Vector3(-4f, 0f, -6f) : new Vector3(0f, 0f, 4f),
                        id == "walk" ? "walk-loop" : "celebrate");
                    CameraPosition = Origin + new Vector3(0f, 2.4f, 12f);
                    CameraRotation = Quaternion.LookRotation(Origin + new Vector3(0f, 1f, 0f) - CameraPosition, Vector3.up);
                    break;
            }
        }

        public void Tick(float dt)
        {
            _shotT += dt;
            if (_sock != null) _sock.rotation = Quaternion.Euler(0f, 18f * Mathf.Sin(_shotT * 1.3f), -6f + 4f * Mathf.Sin(_shotT * 2.1f));
            if (_shot == "driveway")
            {
                var u = Mathf.Clamp01(_shotT / 2.8f);
                u = 1f - (1f - u) * (1f - u);
                CameraPosition = Origin + Vector3.Lerp(new Vector3(4f, 2.2f, 4f), new Vector3(-4f, 2.0f, 5f), u);
                CameraRotation = Quaternion.LookRotation(Origin + new Vector3(0f, 1f, 0f) - CameraPosition, Vector3.up);
            }
            if (_taxi != null && (_shot == "taxi" || _shot == "city"))
            {
                var from = _shot == "taxi" ? -18f : -20f;
                var to = _shot == "taxi" ? 0f : 20f;
                var u = Mathf.Clamp01(_shotT / (_shot == "taxi" ? 2.6f : 3.8f));
                u = 1f - (1f - u) * (1f - u);
                var p = _taxi.position;
                p.x = Origin.x + Mathf.Lerp(from, to, u);
                _taxi.position = p;
            }
            if (_plane != null && _shot == "airport")
            {
                // roll along the runway in front of the terminal, then lift off over the town and bank away
                var u = Mathf.Clamp01(_shotT / 4.6f);
                var roll = Mathf.Clamp01(u / 0.5f);
                var x = Mathf.Lerp(RollFrom.x, LiftOff.x, roll * roll);
                var climb = Mathf.Clamp01((u - 0.5f) / 0.5f);
                var p = new Vector3(x, 0f, RollFrom.z);
                if (climb > 0f) p = Vector3.Lerp(LiftOff, ClimbTo, climb * climb * (3f - 2f * climb));
                _plane.position = Origin + p;
                _plane.rotation = Quaternion.Euler(-14f * Mathf.Sin(climb * Mathf.PI * 0.5f), 90f - 25f * climb, 8f * climb);
            }
            if (_plane != null && _shot == "landing")
            {
                var u = 1f - (1f - Mathf.Clamp01(_shotT / 4.5f)) * (1f - Mathf.Clamp01(_shotT / 4.5f));
                _plane.position = Origin + Vector3.Lerp(LandFrom, LandTo, u);
            }
            if (_gooby != null && _shot == "door")
            {
                var u = Mathf.Clamp01(_shotT / 3.2f);
                _gooby.position = Origin + new Vector3(0f, 0f, Mathf.Lerp(-2f, 2f, u));
                if (!_hopped && _shotT >= 3.2f)
                {
                    _hopped = true;
                    _avatar?.Play("hop");
                }
            }
            if (_gooby != null && _shot == "taxi" && _shotT >= 2.8f)
            {
                var u = Mathf.Clamp01((_shotT - 2.8f) / 1.4f);
                _gooby.position = Origin + Vector3.Lerp(new Vector3(2f, 0f, 1.5f), new Vector3(0.3f, 0.2f, 0.6f), u);
                if (u >= 1f && !_hiddenGooby)
                {
                    _hiddenGooby = true;
                    _gooby.gameObject.SetActive(false);
                }
            }
            if (_gooby != null && _shot == "walk")
            {
                var u = Mathf.Clamp01(_shotT / 3.5f);
                _gooby.position = Origin + Vector3.Lerp(new Vector3(-4f, 0f, -6f), new Vector3(0f, 0f, 6f), u);
            }
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }

        private void ClearSet()
        {
            for (var i = _set.childCount - 1; i >= 0; i--) Object.Destroy(_set.GetChild(i).gameObject);
            _taxi = _plane = _gooby = _sock = null;
            _avatar = null;
        }

        private void HouseYard()
        {
            Scenery.Place(_set, "house_salmon", new Vector3(0f, 0f, -6f), 0f, 1.15f);
            Scenery.Place(_set, "picket_fence", new Vector3(-4.5f, 0f, -1.8f), 90f, 1f);
            Scenery.Place(_set, "picket_fence", new Vector3(4.5f, 0f, -1.8f), 90f, 1f);
            Scenery.Place(_set, "tree_tall", new Vector3(-6.5f, 0f, -4f), 25f, 1.05f);
            Scenery.Place(_set, "tree_small", new Vector3(6f, 0f, -3.5f), -10f, 0.95f);
            Scenery.Place(_set, "mailbox_red", new Vector3(2.4f, 0f, 1.2f), 0f, 1f);
        }

        /// <summary>Takeoff: rolls in from the left on the runway in front of the terminal, lifts off right of centre.</summary>
        private static readonly Vector3 RollFrom = new Vector3(-22f, 0f, -4f), LiftOff = new Vector3(6f, 0f, -4f), ClimbTo = new Vector3(34f, 16f, -26f);
        private static readonly Vector3 LandFrom = new Vector3(18f, 14f, -22f), LandTo = new Vector3(-6f, 0f, -4f);

        /// <summary>A flat surface over the grass (top 2 cm up, so the two never fight for the same pixels).</summary>
        private void Overlay(string name, Vector3 local, Vector2 size, Material m, float lift = 0.02f) =>
            ArtLibrary.Box(_set, name, Origin + new Vector3(local.x, lift - 0.08f, local.z), new Vector3(size.x, 0.16f, size.y), m, false);

        /// <summary>Asphalt along X with dashed white centre line.</summary>
        private void Road(Vector3 local, float length, float width)
        {
            Overlay("Road", local, new Vector2(length, width), ArtLibrary.Pbr("concrete", 4f, ArtLibrary.Hex("6F737A"), 0.12f, 0.4f));
            var paint = ArtLibrary.Solid(ArtLibrary.Hex("F4F1E8"), 0.3f);
            for (var x = -length * 0.5f + 1.5f; x < length * 0.5f; x += 4f)
                Overlay("RoadDash", local + new Vector3(x, 0f, 0f), new Vector2(2f, 0.18f), paint, 0.03f);
        }

        private void Pavement(Vector3 local, float length, float width) =>
            Overlay("Pavement", local, new Vector2(length, width), ArtLibrary.Pbr("concrete", 2f, ArtLibrary.Hex("D9D2C5"), 0.1f, 0.5f), 0.05f);

        /// <summary>The airport: apron in front of the terminal, a runway with its centre dashes and edge lights, a windsock.</summary>
        private void Apron()
        {
            Overlay("Apron", new Vector3(0f, 0f, -8f), new Vector2(80f, 22f), ArtLibrary.Pbr("concrete", 5f, ArtLibrary.Hex("C5CAD0"), 0.4f, 0.3f));
            Overlay("Runway", new Vector3(0f, 0f, -4f), new Vector2(80f, 6f), ArtLibrary.Pbr("concrete", 4f, ArtLibrary.Hex("5E636B"), 0.12f, 0.4f), 0.03f);
            var paint = ArtLibrary.Solid(ArtLibrary.Hex("F4F1E8"), 0.3f);
            for (var x = -38f; x < 40f; x += 5f) Overlay("RunwayDash", new Vector3(x, 0f, -4f), new Vector2(2.6f, 0.25f), paint, 0.04f);
            var yellow = ArtLibrary.Solid(ArtLibrary.Hex("F2C14E"), 0.3f);
            Overlay("TaxiLine", new Vector3(0f, 0f, -9f), new Vector2(60f, 0.2f), yellow, 0.035f);
            var lamp = ArtLibrary.Glow(new Color(1.4f, 1.1f, 0.5f));
            for (var x = -36f; x <= 36f; x += 6f)
                foreach (var z in new[] { -7.2f, -0.8f })
                    ArtLibrary.Sphere(_set, "RunwayLight", Origin + new Vector3(x, 0.08f, z), 0.08f, lamp, false);
            Scenery.Place(_set, "airport_terminal", new Vector3(0f, 0f, -14f), 0f, 1.5f);
            Scenery.Place(_set, "tree_round_big", new Vector3(-17f, 0f, -16f), 0f, 1.1f);
            Scenery.Place(_set, "tree_round", new Vector3(15f, 0f, -17f), 30f, 1.2f);
            Scenery.Place(_set, "luggage_cart", new Vector3(-6f, 0f, -10.5f), 15f, 1.1f);
            // windsock: a pole with an orange-white striped sock that sways in Tick
            ArtLibrary.Cylinder(_set, "WindsockPole", Origin + new Vector3(10f, 2f, -10f), 0.06f, 4f, ArtLibrary.Solid(ArtLibrary.Hex("C9CED6"), 0.5f));
            _sock = new GameObject("Windsock").transform;
            _sock.SetParent(_set, false);
            _sock.position = Origin + new Vector3(10f, 3.85f, -10f);
            for (var i = 0; i < 4; i++)
            {
                var ring = ArtLibrary.Cylinder(_sock, "Sock", _sock.position + new Vector3(0.35f + i * 0.42f, 0f, 0f), 0.22f - i * 0.03f, 0.42f, ArtLibrary.Solid(ArtLibrary.Hex(i % 2 == 0 ? "F2784B" : "FFF8EE"), 0.2f), false);
                ring.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        private Transform _sock;

        private Transform Taxi(Vector3 local)
        {
            var go = new GameObject("Taxi").transform;
            go.SetParent(_set, false);
            go.position = Origin + local;
            go.rotation = Quaternion.Euler(0f, 90f, 0f);
            CityScene.TintCar(go, ArtLibrary.Hex("F5C542"));
            var hull = ArtLibrary.Box(go, "Body", go.position + Vector3.up * 0.55f, new Vector3(3.2f, 1.0f, 1.5f), ArtLibrary.Solid(ArtLibrary.Hex("F5C542"), 0.35f));
            _ = HideWhenLoaded(go, hull, AssetLoader.VehiclePath("car_taxi"), "car_taxi");
            return go;
        }

        private Transform PlacePlane(Vector3 local)
        {
            var go = new GameObject("Plane").transform;
            go.SetParent(_set, false);
            go.position = Origin + local;
            go.rotation = Quaternion.Euler(0f, -35f, 0f);
            var mat = ArtLibrary.Solid(ArtLibrary.Hex("3F4754"), 0.25f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Fuselage";
            body.transform.SetParent(go, false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(1.6f, 3.5f, 1.6f);
            body.GetComponent<Renderer>().sharedMaterial = mat;
            var col = body.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var wing = ArtLibrary.Box(go, "Wing", go.position, new Vector3(9f, 0.2f, 1.6f), mat);
            var tail = ArtLibrary.Box(go, "Tail", go.position + new Vector3(0f, 0.9f, 3f), new Vector3(0.2f, 1.8f, 1.2f), mat);
            _ = FillPlaneAsync(go, body, wing, tail);
            return go;
        }

        private static async Task FillPlaneAsync(Transform go, GameObject fuselage, GameObject wing, GameObject tail)
        {
            var clone = await AssetLoader.SceneryInstanceAsync("plane_small", go);
            if (clone == null || go == null) return;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one * 1.5f;
            if (fuselage != null) fuselage.SetActive(false);
            if (wing != null) wing.SetActive(false);
            if (tail != null) tail.SetActive(false);
        }

        private Transform SpawnGooby(Vector3 local, string clip)
        {
            var go = new GameObject("CutGooby").transform;
            go.SetParent(_set, false);
            go.position = Origin + local;
            var cream = ArtLibrary.Solid(ArtLibrary.Hex("F3E0C8"), 0.35f);
            ArtLibrary.Sphere(go, "FallbackBody", go.position + Vector3.up * 0.55f, 0.42f, cream);
            ArtLibrary.Sphere(go, "FallbackHead", go.position + Vector3.up * 1.15f, 0.32f, cream);
            _ = SwapGoobyAsync(go, clip);
            return go;
        }

        private async Task SwapGoobyAsync(Transform root, string clip)
        {
            var avatar = await GoobyAvatar.CreateAsync(root, Vector3.zero);
            if (avatar == null || root == null) return;
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            avatar.Play(clip);
            avatar.SetEmotion(clip == "celebrate" ? "ecstatic" : "happy");
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var c = root.GetChild(i);
                if (c.name.StartsWith("Fallback")) c.gameObject.SetActive(false);
            }
            _avatar = avatar;
        }

        private static async Task HideWhenLoaded(Transform parent, GameObject hull, string path, string name)
        {
            var clone = await AssetLoader.InstanceAsync(path, parent, name);
            if (clone == null || hull == null) return;
            hull.SetActive(false);
        }
    }
}
