using System.Collections.Generic;
using Gooby.Core.City;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The inside of a place along Sonnental's street (port of ort_scene.gd + the orte/*.gd interiors): a 14×10 m
    /// stage far away from home and street — floor, back wall in the place's pastel, sign board with the lettering,
    /// windows, ceiling lamps — dressed per kind (shop shelves with goods and a counter, practice with reception and
    /// exam bed, post office with parcel racks, the market square with stalls, the showroom with cars on podiums,
    /// the park plaza with ticket booth and wheel, the airport hall with departure board and the plane outside).
    /// The shopkeeper is a Gooby of his own fur and outfit; the player's Gooby stands in front of the counter. The
    /// camera sits at the door looking in (Godot: (0, 2, 4.2), −12°).
    /// </summary>
    public sealed partial class InteriorScene
    {
        public static readonly Vector3 Origin = new Vector3(200f, 0f, 0f);

        public GameObject Root { get; }
        public DriveModel.Place Place { get; }
        public bool Outdoors { get; }
        public GoobyAvatar Npc { get; private set; }
        // board 10: camera at the door, almost level — wall lettering in the upper third, the cashier facing the
        // lens behind the counter. The player's Gooby stands in front of the counter, seen from behind a little left of
        // the middle (+X is screen-left) — his ears and head show above the dialog card like on all four board-10 panels.
        // GOOB-1 uses Godot's station camera (0, 2.0, 4.2) looking at the panorama window.
        public Vector3 CameraPosition => Place.Id == "raumstation" ? Origin + new Vector3(0f, 1.85f, 4.6f) : Origin + new Vector3(0f, 1.22f, 3.7f);
        // Unity +X looks down; Godot's −12° on a −Z camera is a slight look-down at the panorama window.
        public Quaternion CameraRotation => Place.Id == "raumstation"
            ? Quaternion.LookRotation(new Vector3(0.08f, 0.12f, -1f), Vector3.up)
            : Quaternion.Euler(2f, 180f, 0f);
        public Vector3 GoobySpot => Place.Id == "raumstation" ? Origin + new Vector3(0f, 0f, 1.2f) : Origin + new Vector3(0.6f, 0f, 0.6f);
        public Color Sky { get; }

        private readonly Transform _t;
        private double _tapAcc, _waveAcc;

        /// <summary>Shopkeeper per place: fur + outfit (cosmetics catalog ids) and the emotion he greets with.</summary>
        private static readonly Dictionary<string, (string fell, string hut, string brille, string hals, string ruecken, string emotion)> Npcs =
            new Dictionary<string, (string, string, string, string, string, string)>
        {
            { "mart", ("caramel", "beret", null, "hals_schleife", null, "happy") },
            { "goobyman", ("golden", null, "starGlasses", "cape", null, "ecstatic") },
            { "tierarzt", ("snow", null, "readingGlasses", "bowtie", null, "happy") },
            { "gouhbus", ("ash", "hut_melone", "monocle", null, null, "neutral") },
            { "goobytheke", ("cream", null, "roundGlasses", "pearlNecklace", null, "happy") },
            { "post", ("fell_himmel", "cap", null, "hals_schluesselband", null, "happy") },
            { "pow", ("fell_zuckerwatte", "partyHat", "stars3D", null, null, "ecstatic") },
            { "baumarkt", ("fell_kakao", "hut_bauhelm", null, "bandana", null, "happy") },
            { "autohaus", ("midnight", null, "sunglasses", "hals_krawatte", null, "happy") },
            { "wochenmarkt", ("fell_minze", "strawHat", null, "scarfRed", null, "happy") },
            { "funkelpark", ("rose", "hut_kroenchen", null, null, "balloonRed", "ecstatic") },
            { "flughafen", ("fell_himmel", "hut_kapitaen", null, "hals_krawatte", null, "neutral") },
            { "raumstation", ("fell_lavendel", "hut_kapitaen", null, null, null, "happy") },
        };

        private static readonly string[] SkuAccents = { "#FFF8EE", "#3B364F", "#6B4F3A", "#FFFFFF", "#FFE066" };

        public InteriorScene(Transform parent, DriveModel.Place place)
        {
            Place = place;
            Root = new GameObject("Interior:" + place.Id);
            Root.transform.SetParent(parent, false);
            Root.transform.position = Origin;
            _t = Root.transform;
            Outdoors = place.Id == "wochenmarkt" || place.Id == "funkelpark";
            Sky = place.Id == "raumstation" ? ArtLibrary.Hex("040510") : Outdoors ? ArtLibrary.Hex("8CC4F0") : ArtLibrary.Hex("FAF0E4");

            if (place.Id == "raumstation")
            {
                BuildStation();
            }
            else
            {
                var tint = string.IsNullOrEmpty(place.Tint) ? ArtLibrary.Hex("#FFF1DC") : ArtLibrary.Hex(place.Tint);
                var wall = Color.Lerp(tint, ArtLibrary.Hex("FFF6EA"), 0.62f);
                var wainscot = Color.Lerp(tint, ArtLibrary.Hex("FFF6EA"), 0.3f);
                BuildShell(wall, wainscot);
                switch (place.Id)
                {
                    case "tierarzt": case "gouhbus": BuildPractice(place.Id == "tierarzt"); break;
                    case "post": BuildPost(); break;
                    case "wochenmarkt": BuildMarket(); break;
                    case "autohaus": BuildShowroom(); break;
                    case "funkelpark": BuildPark(); break;
                    case "flughafen": BuildAirport(); break;
                    default: BuildShop(place.Id); break;
                }
            }
            _ = SpawnNpcAsync(place.Id);
        }

        /// <summary>Cashier cadence (Godot KassenNpc): babble every 2.4 s, wave every 11 s. Reduced motion skips the wave.</summary>
        public void Tick(float dt, bool reducedMotion)
        {
            TickStall(dt, reducedMotion);
            TickLife(dt);
            TickVet(dt);
            if (Npc == null || dt <= 0f) return;
            if (Place.Id == "raumstation") return; // orbit is quiet: no till, no doorbell (Godot kasse=false)
            var action = CashierNpc.Step(ref _tapAcc, ref _waveAcc, dt, reducedMotion);
            if (action == null) return;
            Debug.Log($"[interior] cashier {action} {Place.Id}");
            if (action == "tap") Npc.BabblePulse();
            else if (action == "wave") Npc.Play("wave");
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }

        // ── shell ──────────────────────────────────────────────────────────────

        private Material Solid(string hex, float smooth = 0.18f) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);
        private Material Solid(Color c, float smooth = 0.18f) => ArtLibrary.Solid(c, smooth);
        private GameObject Box(string name, Vector3 at, Vector3 size, Material m, bool shadows = true) => ArtLibrary.Box(_t, name, Origin + at, size, m, shadows);
        private GameObject Cyl(string name, Vector3 at, float r, float h, Material m) => ArtLibrary.Cylinder(_t, name, Origin + at, r, h, m);
        private GameObject Sphere(string name, Vector3 at, float r, Material m) => ArtLibrary.Sphere(_t, name, Origin + at, r, m);

        private void BuildShell(Color wall, Color wainscot)
        {
            if (Outdoors)
            {
                var grass = ArtLibrary.PbrSlab("grass", new Vector2(40f, 40f), 2.4f, ArtLibrary.Hex("#D8E6C8"), 0.04f, 0.7f);
                Box("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(40f, 0.2f, 40f), grass, false);
                var path = ArtLibrary.PbrSlab("paving", new Vector2(9f, 7f), 1.6f, ArtLibrary.Hex("#EBDFCC"), 0.12f, 0.8f);
                Box("Path", new Vector3(0f, 0.005f, 0.6f), new Vector3(9f, 0.02f, 7f), path, false);
                var horizonPos = Origin + new Vector3(0f, -3f, -40f);
                var horizon = ArtLibrary.Horizon(_t, "bg_town_horizon", horizonPos, 30f);
                horizon.rotation = Quaternion.Euler(0f, 180f, 0f);
                return;
            }
            // floors per kind of place (board 10): wooden boards in the shops, pale tiles in the practices, a glossy
            // showroom floor at the dealer and in the airport hall
            var glossy = Place.Id == "autohaus" || Place.Id == "flughafen";
            var floor = Place.Typ == "praxis" ? ArtLibrary.PbrSlab("tiles", new Vector2(14f, 11f), 1.1f, ArtLibrary.Hex("#EAF4EE"), 0.5f, 0.5f)
                : glossy ? ArtLibrary.PbrSlab("concrete", new Vector2(14f, 11f), 2.2f, ArtLibrary.Hex("#EDEBF0"), 0.85f, 0.25f)
                : ArtLibrary.PbrSlab("wood_floor", new Vector2(14f, 11f), 1.7f, ArtLibrary.Hex("#EFD6B4"), 0.4f, 0.75f);
            Box("Floor", new Vector3(0f, -0.1f, 0.5f), new Vector3(14f, 0.2f, 11f), floor, false);
            var wallMat = ArtLibrary.PbrSlab("plaster", new Vector2(14f, 5f), 2.4f, wall, 0.05f, 0.5f);
            if (Place.Id == "flughafen")
            {
                // the hall's back wall leaves a big opening onto the apron (x 1.0…6.4, y 1.4…3.6): the plane stands outside
                Box("BackWall", new Vector3(-3.0f, 2.5f, -4.15f), new Vector3(8f, 5f, 0.3f), wallMat);
                Box("BackWall", new Vector3(6.7f, 2.5f, -4.15f), new Vector3(0.6f, 5f, 0.3f), wallMat);
                Box("BackWall", new Vector3(3.7f, 4.3f, -4.15f), new Vector3(5.4f, 1.4f, 0.3f), wallMat);
                Box("BackWall", new Vector3(3.7f, 0.7f, -4.15f), new Vector3(5.4f, 1.4f, 0.3f), wallMat);
                Box("ApronGlass", new Vector3(3.7f, 2.5f, -4.15f), new Vector3(5.4f, 2.2f, 0.04f), ArtLibrary.Transparent(new Color(0.8f, 0.92f, 1f, 0.22f)), false);
                foreach (var x in new[] { 1.9f, 3.7f, 5.5f }) Box("ApronMullion", new Vector3(x, 2.5f, -4.12f), new Vector3(0.08f, 2.2f, 0.06f), Solid("#FFFFFF"), false);
                Box("ApronSill", new Vector3(3.7f, 1.4f, -4.0f), new Vector3(5.6f, 0.1f, 0.3f), Solid("#FFFFFF", 0.2f));
                var apron = ArtLibrary.Textured("tex_tiles", new Vector2(8f, 6f), ArtLibrary.Hex("#C9CED6"));
                Box("Apron", new Vector3(0f, -0.1f, -14f), new Vector3(40f, 0.2f, 20f), apron, false);
                Box("ApronGrass", new Vector3(0f, -0.09f, -26f), new Vector3(60f, 0.2f, 6f), ArtLibrary.Textured("tex_grass", new Vector2(10f, 1f), ArtLibrary.Hex("#DCEBC4")), false);
                ArtLibrary.Quad(_t, "Sky", Origin + new Vector3(0f, 8f, -30f), Quaternion.identity, new Vector2(80f, 22f), Solid("#BFE3FF", 0f));
            }
            else
            {
                Box("BackWall", new Vector3(0f, 2.5f, -4.15f), new Vector3(14f, 5f, 0.3f), wallMat);
            }
            Box("Wainscot", new Vector3(0f, 0.55f, -3.98f), new Vector3(14f, 1.1f, 0.06f), Solid(wainscot, 0.1f));
            Box("Skirting", new Vector3(0f, 0.06f, -3.94f), new Vector3(14f, 0.12f, 0.08f), Solid("#FFFFFF", 0.1f));
            foreach (var s in new[] { -1f, 1f })
                Box("SideWall", new Vector3(s * 7.15f, 2.5f, 0.5f), new Vector3(0.3f, 5f, 11f), Solid(Color.Lerp(wall, Color.white, 0.2f), 0.05f));
            // windows on the back wall left and right of the sign, daylight behind them (the airport has the apron instead)
            var glass = Solid("#CFE6F7", 0.6f);
            foreach (var x in new[] { -4.6f, 4.6f })
            {
                if (Place.Id == "flughafen" && x > 0f) continue;
                Box("WindowFrame", new Vector3(x, 2.55f, -3.96f), new Vector3(1.7f, 1.4f, 0.1f), Solid("#FFFFFF", 0.2f));
                Box("WindowGlass", new Vector3(x, 2.55f, -3.9f), new Vector3(1.5f, 1.2f, 0.04f), glass, false);
                Box("WindowBar", new Vector3(x, 2.55f, -3.86f), new Vector3(0.06f, 1.2f, 0.02f), Solid("#FFFFFF"), false);
                Box("WindowBar", new Vector3(x, 2.55f, -3.86f), new Vector3(1.5f, 0.06f, 0.02f), Solid("#FFFFFF"), false);
            }
            SignBoard(new Vector3(Place.Id == "flughafen" ? -1.6f : 0f, 2.45f, -3.9f), Place.Id == "flughafen" ? 3.2f : 4.2f);
            // board 10: a ceiling closes the room, recessed spots glow above the counter, pendant lamps light it
            Box("Ceiling", new Vector3(0f, 5.02f, 0.5f), new Vector3(14.4f, 0.1f, 11.4f), Solid(Color.Lerp(wall, Color.white, 0.6f), 0.05f), false);
            var spot = ArtLibrary.Glow(new Color(2.2f, 2.0f, 1.7f));
            foreach (var x in new[] { -4.5f, -1.5f, 1.5f, 4.5f })
                foreach (var z in new[] { -2.4f, 1.2f })
                {
                    var disc = Cyl("Spot", new Vector3(x, 4.96f, z), 0.22f, 0.02f, spot);
                    disc.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            // board 10: dark dome pendants peek in at the top edge of the door camera, left and right of the wall title
            // (x ±2.35 lies outside the letters, y 2.74 at z −0.85 ≈ 18.5° up — the frame ends at 19°). The cream globes
            // that hung in front of the title hid "GOOBY MART"/"TIERARZT".
            float[] lampXs = Place.Id == "autohaus" || Place.Id == "flughafen" ? new float[0] : new[] { -2.35f, 2.35f };
            var shade = Solid("#3F3A45", 0.35f);
            var bulb = ArtLibrary.Glow(new Color(2.55f, 2.22f, 1.62f));
            foreach (var x in lampXs)
            {
                Cyl("LampCord", new Vector3(x, 3.87f, -0.85f), 0.012f, 2.26f, Solid("#3F3A45"));
                var dome = Sphere("LampShade", new Vector3(x, 2.74f, -0.85f), 0.2f, shade);
                dome.transform.localScale = new Vector3(0.46f, 0.26f, 0.46f);
                Sphere("LampBulb", new Vector3(x, 2.64f, -0.85f), 0.075f, bulb);
                var light = new GameObject("CeilingLight").AddComponent<Light>();
                light.transform.SetParent(_t, false);
                light.transform.position = Origin + new Vector3(x, 2.5f, -0.85f);
                light.type = LightType.Point;
                light.color = ArtLibrary.Hex("FFE2B8");
                light.intensity = 0.9f;
                light.range = 7.5f;
                light.shadows = LightShadows.None;
            }
            if (Place.Id != "flughafen")
            {
                // potted plants flanking the back wall and a pastel poster on each side wall (board 10)
                Plant(new Vector3(-6.4f, 0f, -3.3f), 0.55f);
                Plant(new Vector3(6.4f, 0f, -3.3f), 0.5f);
                Poster(new Vector3(-6.98f, 2.4f, 1.6f), PosterOf(Place.Id, 0), new Vector2(1.0f, 1.32f), yaw: 90f);
                Poster(new Vector3(6.98f, 2.4f, 1.6f), PosterOf(Place.Id, 1), new Vector2(1.0f, 1.32f), yaw: -90f);
                // a soft rug in front of the counter where the customers stand
                var rug = ArtLibrary.PbrSlab("carpet", new Vector2(4.2f, 2.2f), 0.9f, Color.Lerp(tintOf(Place), Color.white, 0.45f), 0.04f, 0.8f);
                Box("Rug", new Vector3(0.6f, 0.012f, 0.4f), new Vector3(4.2f, 0.024f, 2.2f), rug, false);
            }
        }

        private static Color tintOf(DriveModel.Place p) => string.IsNullOrEmpty(p.Tint) ? ArtLibrary.Hex("#FFF1DC") : ArtLibrary.Hex(p.Tint);

        /// <summary>Wall posters per place (tools/art/build_art.py): the shops' slogans, the vet's paws, the market's chalk board …</summary>
        private static string PosterOf(string id, int side)
        {
            switch (id)
            {
                case "tierarzt": case "gouhbus": case "goobytheke": return side == 0 ? "poster_pfoten" : "poster_moehre";
                case "autohaus": case "flughafen": return side == 0 ? "poster_wege" : "poster_ziele";
                case "wochenmarkt": return "poster_regional";
                default: return side == 0 ? "poster_good" : "poster_moehre";
            }
        }

        /// <summary>
        /// The place's name in big white letters straight on the coloured back wall with its slogan underneath
        /// (board 10: "GOOBY MART — Alles, was du brauchst!"); the street-sign lettering stays as the tests' marker.
        /// </summary>
        private void SignBoard(Vector3 at, float width)
        {
            var w = width + 1.8f;
            // white letters on the saturated walls, slate letters on the pale ones (dealer, airport, post, practice)
            var wallTint = tintOf(Place);
            var pale = 0.299f * wallTint.r + 0.587f * wallTint.g + 0.114f * wallTint.b > 0.8f;
            ArtLibrary.Quad(_t, "WallTitle", Origin + at + new Vector3(0f, 0.42f, 0.02f), Quaternion.identity, new Vector2(w, w / 3f), ArtLibrary.Decal("wall_" + Place.Id + (pale ? "_dark" : "")));
            // the street-sign lettering stays on the stage (inside the wall, out of sight) as the marker the tests look for
            ArtLibrary.Quad(_t, "Lettering", Origin + at + new Vector3(0f, -0.6f, -0.3f), Quaternion.identity, new Vector2(0.02f, 0.02f), ArtLibrary.Decal(CityScene.SignOf(Place.Id)));
        }

        /// <summary>
        /// Sales counter across the room in front of the shopkeeper, with a till. The boxes are the fallback; the
        /// Blender counter (<paramref name="model"/>: shop_counter 3.4 m with till, vet_counter 2.8 m stadium desk,
        /// counter_long 4.4 m, post_counter 4.2 m with the glass partition, checkin_desk 4.6 m with belt and screen) takes
        /// over once loaded — same footprint, top at 0.96 m, front and top re-tinted. <paramref name="extra"/> are the
        /// place's own primitives the model bakes in (partition, letters, check-in screen): they hide with the boxes.
        /// </summary>
        private void Counter(Vector3 at, float width, string front, string top = "#D9B48A", bool till = true, string model = "shop_counter", IList<GameObject> extra = null)
        {
            var frontMat = ArtLibrary.Pbr("plaster", 1.4f, ArtLibrary.Hex(front), 0.14f, 0.5f);
            var topMat = ArtLibrary.Pbr("wood_planks", 1.2f, ArtLibrary.Hex(top), 0.3f, 0.6f);
            var boxes = new List<GameObject>
            {
                Box("Counter", at + new Vector3(0f, 0.45f, 0f), new Vector3(width, 0.9f, 0.8f), frontMat),
                Box("CounterTop", at + new Vector3(0f, 0.93f, 0f), new Vector3(width + 0.1f, 0.06f, 0.9f), topMat),
                Box("CounterBase", at + new Vector3(0f, 0.05f, 0f), new Vector3(width + 0.06f, 0.1f, 0.86f), Solid("#6B4F3A", 0.1f)),
                // a little pot plant and the card reader on the top (board 10)
                Cyl("CounterPot", at + new Vector3(-width * 0.38f, 1.04f, 0.15f), 0.08f, 0.16f, Solid("#E7A67A", 0.2f)),
                Sphere("CounterLeaves", at + new Vector3(-width * 0.38f, 1.2f, 0.15f), 0.12f, Solid("#6FBF5A", 0.1f)),
                Box("CardReader", at + new Vector3(width * 0.12f, 1.0f, 0.22f), new Vector3(0.12f, 0.08f, 0.16f), Solid("#3F3A45", 0.4f), false),
            };
            if (till)
            {
                boxes.Add(Box("Till", at + new Vector3(width * 0.35f, 1.12f, -0.1f), new Vector3(0.42f, 0.32f, 0.36f), Solid("#3F3A45", 0.3f)));
                boxes.Add(Box("TillScreen", at + new Vector3(width * 0.35f, 1.2f, 0.09f), new Vector3(0.3f, 0.16f, 0.02f), Solid("#98E7E1", 0.5f), false));
            }
            if (extra != null) boxes.AddRange(extra);
            // the models bake the till only into shop_counter: a till-less counter keeps the till-less fallback's look
            if (till != (model == "shop_counter")) return;
            if (!ModelWidths.TryGetValue(model, out var modelWidth)) return;
            Model(model, at, 0f, new Vector3(width / modelWidth, 1f, 1f), boxes, ("gb_tresenfront", frontMat), ("gb_tresenplatte", topMat));
        }

        /// <summary>Nominal widths of the Blender interior pieces (build_scenery.py) — the runtime scales X to the footprint.</summary>
        private static readonly Dictionary<string, float> ModelWidths = new Dictionary<string, float>
        {
            { "shop_counter", 3.4f }, { "vet_counter", 2.8f }, { "counter_long", 4.4f },
            { "shelf_unit", 1.9f }, { "shelf_unit_wide", 2.3f },
            { "post_counter", 4.2f }, { "checkin_desk", 4.6f }, { "parcel_shelf", 2.0f }, { "med_cabinet", 1.8f },
        };

        /// <summary>
        /// A board 10 furniture model from build_scenery.py (StreamingAssets/gooby/scenery) standing in for primitive
        /// boxes: the anchor sits at its final transform right away (origin at the floor centre, front +Z at yaw 0), the
        /// <paramref name="fallback"/> primitives keep the room complete while glTFast imports the GLB and are switched
        /// off — not destroyed, so <c>Find("Counter")</c> and the tests still see them — once the clone is in. Without
        /// glTFast or the file the primitives simply stay. <paramref name="slots"/> swaps the model's palette materials
        /// by name ("gb_tresenfront" …) for the place's own tint.
        /// </summary>
        private GameObject Model(string archetype, Vector3 at, float yaw, Vector3 scale, IList<GameObject> fallback, params (string palette, Material mat)[] slots)
        {
            var anchor = new GameObject("Model:" + archetype);
            anchor.transform.SetParent(_t, false);
            anchor.transform.position = Origin + at;
            anchor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.transform.localScale = scale;
            _ = SwapInModelAsync(anchor.transform, archetype, fallback, slots);
            return anchor;
        }

        private static async System.Threading.Tasks.Task SwapInModelAsync(Transform anchor, string archetype, IList<GameObject> fallback, (string palette, Material mat)[] slots)
        {
            var clone = await AssetLoader.SceneryInstanceAsync(archetype, anchor);
            if (clone == null) return;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            if (slots != null && slots.Length > 0)
                foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    var changed = false;
                    for (var i = 0; i < mats.Length; i++)
                    {
                        var name = mats[i] != null ? mats[i].name : "";
                        foreach (var (palette, mat) in slots)
                            if (mat != null && name.StartsWith(palette, System.StringComparison.Ordinal)) { mats[i] = mat; changed = true; break; }
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            if (fallback != null)
                foreach (var go in fallback)
                    if (go != null) go.SetActive(false);
        }

        /// <summary>
        /// Wall shelf: uprights, three planks, packed SKUs. Board 10 MART is cereal boxes with two-tone
        /// fronts (yellow/pink/mint), not a row of unlabeled pastel bottles. <paramref name="jars"/>
        /// biases a shelf toward glass (pharmacy / vet) but still keeps boxes. <paramref name="yaw"/>
        /// turns the whole shelf (90° = against the left wall, facing the room). <paramref name="stocked"/> names a
        /// Blender piece with its goods baked in (parcel_shelf, med_cabinet): then carcass AND goods are its fallback.
        /// </summary>
        private void Shelf(Vector3 at, float width, string[] goods, bool jars = false, float yaw = 0f, string stocked = null)
        {
            var first = _t.childCount;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var wood = ArtLibrary.Pbr("wood_planks", 1.2f, ArtLibrary.Hex("#E2BE8E"), 0.2f, 0.7f);
            var dark = ArtLibrary.Pbr("wood_planks", 1.2f, ArtLibrary.Hex("#C49564"), 0.2f, 0.7f);
            GameObject B(string name, Vector3 local, Vector3 size, Material m, bool shadows = true)
            {
                var go = Box(name, at + rot * local, size, m, shadows);
                go.transform.rotation = rot;
                return go;
            }
            // the carcass boxes are the fallback for the Blender shelf_unit (same plank tops 0.575/1.175/1.775 m, 0.42 m
            // deep); the goods below stay runtime primitives either way — the tests count the Sku/Jar/Bottle renderers
            var carcass = new List<GameObject>();
            foreach (var s in new[] { -1f, 1f })
                carcass.Add(B("ShelfPost", new Vector3(s * (width * 0.5f - 0.04f), 1.0f, 0f), new Vector3(0.08f, 2.0f, 0.4f), dark));
            carcass.Add(B("ShelfBack", new Vector3(0f, 1.1f, -0.19f), new Vector3(width, 2.1f, 0.03f), wood, false));
            for (var level = 0; level < 3; level++)
            {
                var y = 0.55f + level * 0.6f;
                carcass.Add(B("ShelfPlank", new Vector3(0f, y, 0f), new Vector3(width, 0.05f, 0.42f), wood));
                var n = Mathf.Max(6, Mathf.RoundToInt(width / 0.17f));
                for (var i = 0; i < n; i++)
                {
                    var c = goods[(i + level * 3) % goods.Length];
                    var accent = SkuAccents[(i + level) % SkuAccents.Length];
                    var x = -width * 0.5f + 0.12f + i * (width - 0.24f) / Mathf.Max(1, n - 1);
                    var roll = (i * 3 + level * 2) % 7;
                    var asJar = jars ? roll >= 3 : roll == 5;
                    var asBottle = !asJar && (jars ? roll == 2 : roll == 6);
                    if (asJar)
                    {
                        var h = 0.16f + (i % 3) * 0.03f;
                        var r = 0.046f + (i % 2) * 0.008f;
                        Cyl("Jar", at + rot * new Vector3(x, y + 0.03f + h * 0.5f, 0.02f), r, h, Solid(c, 0.55f));
                        Cyl("Lid", at + rot * new Vector3(x, y + 0.03f + h + 0.012f, 0.02f), r + 0.006f, 0.022f, Solid(accent, 0.2f));
                        B("Label", new Vector3(x, y + 0.03f + h * 0.46f, r + 0.006f), new Vector3(r * 1.75f, h * 0.46f, 0.006f), Solid(accent, 0.1f), false);
                        B("LabelBand", new Vector3(x, y + 0.03f + h * 0.38f, r + 0.01f), new Vector3(r * 1.75f, h * 0.14f, 0.006f), Solid(c, 0.1f), false);
                    }
                    else if (asBottle)
                    {
                        var h = 0.22f + (i % 3) * 0.04f;
                        var r = 0.036f + (i % 2) * 0.006f;
                        Cyl("Bottle", at + rot * new Vector3(x, y + 0.03f + h * 0.5f, 0.02f), r, h, Solid(c, 0.45f));
                        Cyl("Cap", at + rot * new Vector3(x, y + 0.03f + h + 0.014f, 0.02f), r + 0.004f, 0.024f, Solid("#3B364F", 0.2f));
                        B("Label", new Vector3(x, y + 0.03f + h * 0.42f, r + 0.006f), new Vector3(r * 1.8f, h * 0.38f, 0.006f), Solid(accent, 0.1f), false);
                    }
                    else
                    {
                        var bw = 0.135f + (i % 3) * 0.02f;
                        var bh = 0.24f + (i % 4) * 0.04f;
                        var bd = 0.10f + (i % 2) * 0.025f;
                        B("Sku", new Vector3(x, y + 0.03f + bh * 0.5f, 0f), new Vector3(bw, bh, bd), Solid(c, 0.16f));
                        B("SkuFace", new Vector3(x, y + 0.03f + bh * 0.46f, bd * 0.5f + 0.004f), new Vector3(bw * 0.88f, bh * 0.70f, 0.008f), Solid(accent, 0.12f), false);
                        B("SkuBand", new Vector3(x, y + 0.03f + bh * 0.34f, bd * 0.5f + 0.01f), new Vector3(bw * 0.88f, bh * 0.18f, 0.008f), Solid(c, 0.12f), false);
                        B("SkuMark", new Vector3(x - bw * 0.18f, y + 0.03f + bh * 0.68f, bd * 0.5f + 0.012f), new Vector3(bw * 0.28f, bw * 0.22f, 0.008f), Solid((i % 2) == 0 ? "#FFD166" : "#FF6B8A", 0.2f), false);
                    }
                }
            }
            // after the loop: a cached template swaps in synchronously and must find every carcass box in the list
            if (stocked != null)
            {
                var all = new List<GameObject>();
                for (var i = first; i < _t.childCount; i++) all.Add(_t.GetChild(i).gameObject);
                Model(stocked, at, yaw, new Vector3(width / ModelWidths[stocked], 1f, 1f), all);
                return;
            }
            var unit = width > 2.05f ? "shelf_unit_wide" : "shelf_unit";
            Model(unit, at, yaw, new Vector3(width / ModelWidths[unit], 1f, 1f), carcass);
        }

        /// <summary>
        /// Glass-door cooler (build_scenery.py cooler = two-door drinks, cooler_med = one-door medicine: 1.95 m, 0.8 m per door, 0.66 m deep,
        /// front +Z) against the back wall; a white cabinet with a pale pane and the mint header stands in until it loads.
        /// </summary>
        private void Cooler(string archetype, Vector3 at, int doors, float scale = 1f)
        {
            var w = (doors * 0.8f + 0.08f) * scale;
            var h = 1.95f * scale;
            var d = 0.66f * scale;
            var boxes = new List<GameObject>
            {
                Box("Cooler", at + new Vector3(0f, (h - 0.27f * scale) * 0.5f, 0f), new Vector3(w, h - 0.27f * scale, d), Solid("#FFFFFF", 0.3f)),
                Box("CoolerGlass", at + new Vector3(0f, 0.93f * scale, d * 0.5f + 0.01f), new Vector3(w - 0.14f, 1.45f * scale, 0.02f), Solid("#DDF1FF", 0.8f), false),
                Box("CoolerHeader", at + new Vector3(0f, h - 0.135f * scale, 0.03f), new Vector3(w + 0.04f, 0.27f * scale, d + 0.1f), Solid("#98E7E1", 0.15f)),
            };
            Model(archetype, at, 0f, Vector3.one * scale, boxes);
        }

        /// <summary>A wooden produce crate heaped with round fruit (board 10's market and Mart foregrounds).</summary>
        private void FruitCrate(Vector3 at, string fruit, float yaw = 0f, float w = 0.7f)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var wood = ArtLibrary.Pbr("wood_planks", 0.8f, ArtLibrary.Hex("#D9B48A"), 0.2f, 0.7f);
            var crate = Box("Crate", at + new Vector3(0f, 0.16f, 0f), new Vector3(w, 0.32f, w * 0.72f), wood);
            crate.transform.rotation = rot;
            foreach (var s in new[] { -1f, 1f })
            {
                var rim = Box("CrateRim", at + rot * new Vector3(0f, 0.3f, s * (w * 0.36f)), new Vector3(w + 0.02f, 0.06f, 0.04f), Solid("#B98B5E", 0.2f));
                rim.transform.rotation = rot;
            }
            var m = Solid(fruit, 0.35f);
            var cols = Mathf.Max(2, Mathf.RoundToInt(w / 0.19f));
            for (var i = 0; i < cols; i++)
                for (var j = 0; j < 2; j++)
                {
                    var p = new Vector3(-w * 0.5f + 0.13f + i * (w - 0.26f) / Mathf.Max(1, cols - 1), 0.36f, (j - 0.5f) * w * 0.34f);
                    Sphere("Fruit", at + rot * p, 0.085f, m);
                    if ((i + j) % 2 == 0) Sphere("Fruit", at + rot * (p + new Vector3(0.07f, 0.13f, 0f)), 0.08f, m);
                }
        }

        /// <summary>Waiting lounge: a sofa, a low table with two magazines and a mug (the vet's and the pharmacy's waiting room).</summary>
        private void Lounge(Vector3 at, float yaw, string sofaTint)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            // taller/longer so mint reads above the dialog; z stays ~1.4 (closer sinks under the card).
            // karosage is the home sage throw on this GLB — tint it with the lounge so the vet sofa is plain mint.
            var sofa = Prop("sofa", at, yaw, 2.35f, 0.92f, 1.28f);
            var tint = sofa.AddComponent<PropTint>();
            tint.Body = ArtLibrary.Hex(sofaTint);
            tint.Prefixes = new[] { "pbr:fabric", "rose", "pfirsich", "blatt", "karosage" };
            tint.Apply();
            Prop("table_low", at + rot * new Vector3(0f, 0f, 1.1f), yaw, 1.0f, 0.6f, 0.5f);
            var top = at + rot * new Vector3(0f, 0.47f, 1.1f);
            Box("Magazine", top + rot * new Vector3(-0.18f, 0.01f, 0f), new Vector3(0.24f, 0.015f, 0.32f), Solid("#FF98C1", 0.2f), false).transform.rotation = rot * Quaternion.Euler(0f, 12f, 0f);
            Box("Magazine", top + rot * new Vector3(0.14f, 0.01f, 0.04f), new Vector3(0.24f, 0.015f, 0.32f), Solid(sofaTint, 0.2f), false).transform.rotation = rot * Quaternion.Euler(0f, -8f, 0f);
            Cyl("Mug", top + rot * new Vector3(0.32f, 0.06f, -0.16f), 0.045f, 0.1f, Solid("#FFFFFF", 0.4f));
        }

        /// <summary>Furniture prop from the Blender catalog scaled to a footprint (front towards +Z at yaw 0).</summary>
        private GameObject Prop(string archetype, Vector3 at, float yaw, float w, float d, float heightScale = -1f)
        {
            var anchor = new GameObject("Prop:" + archetype);
            anchor.transform.SetParent(_t, false);
            anchor.transform.position = Origin + at;
            anchor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var h = heightScale > 0f ? heightScale : Mathf.Lerp(Mathf.Min(w, d), Mathf.Max(w, d), 0.5f);
            anchor.transform.localScale = new Vector3(w, h, d);
            _ = AssetLoader.PropInstanceAsync(archetype, anchor.transform);
            return anchor;
        }

        private void Plant(Vector3 at, float scale = 0.5f) => Prop("plant_tall", at, 0f, scale, scale, scale * 2.6f);

        /// <summary>Trailing ivy in a terracotta pot — board 10 MART hangs these in the upper corners of the door shot.</summary>
        private void HangIvy(Vector3 pot, float scale = 1f)
        {
            var terracotta = Solid("#E7A67A", 0.18f);
            var leafA = Solid("#6FBF6A", 0.12f);
            var leafB = Solid("#4E9A58", 0.12f);
            Cyl("IvyWire", pot + Vector3.up * (0.22f * scale), 0.008f * scale, 0.44f * scale, Solid("#C99A6B"));
            Cyl("IvyPot", pot, 0.11f * scale, 0.12f * scale, terracotta);
            Cyl("IvyRim", pot + Vector3.up * (0.05f * scale), 0.13f * scale, 0.03f * scale, terracotta);
            for (var i = 0; i < 9; i++)
            {
                var a = i * 0.7f;
                var drop = (0.10f + (i % 5) * 0.13f) * scale;
                Sphere("Ivy", pot + new Vector3(Mathf.Sin(a) * 0.13f * scale, -drop, Mathf.Cos(a) * 0.07f * scale),
                    0.065f * scale, i % 2 == 0 ? leafA : leafB);
            }
        }

        private void Poster(Vector3 at, string texture, Vector2 size, string frame = "#FFFFFF", float yaw = 0f)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var frameBox = Box("PosterFrame", at, new Vector3(size.x + 0.12f, size.y + 0.12f, 0.05f), Solid(frame, 0.2f));
            frameBox.transform.rotation = rot;
            ArtLibrary.Quad(_t, "Poster", Origin + at + rot * new Vector3(0f, 0f, 0.04f), rot, size, ArtLibrary.Decal(texture));
        }

        // ── kinds ──────────────────────────────────────────────────────────────

        private void BuildShop(string id)
        {
            string[] goods;
            var jars = false;
            switch (id)
            {
                case "goobyman": goods = new[] { "#98E7E1", "#FF98C1", "#FFFFFF", "#7FB8FF", "#FFD166" }; break;
                case "pow": goods = new[] { "#FF8B8B", "#FFD166", "#7FB8FF", "#7FCF9B", "#FF98C1", "#B58CE4" }; break;
                case "baumarkt": goods = new[] { "#D9B48A", "#A8A29E", "#F2913D", "#6B4F3A", "#C9CED6" }; break;
                case "goobytheke": goods = new[] { "#FFFFFF", "#98E7E1", "#FF8B8B", "#FFF8EE" }; jars = true; break;
                default: goods = new[] { "#FF8B8B", "#F2913D", "#7FCF9B", "#FFD166", "#FF98C1", "#FFF4CC" }; break;
            }
            Shelf(new Vector3(-5.3f, 0f, -3.7f), 1.9f, goods, jars);
            Shelf(new Vector3(-3.15f, 0f, -3.7f), 1.9f, goods, jars);
            // MART: the drinks cooler takes the back bay left of the counter (the Godot REHWEI's Kühlschrank-Zeile was on
            // this side; x ±5.3 hides behind the side shelves and the tab column, x −3.15 would cover the GOOD poster)
            if (id == "mart") Cooler("cooler", new Vector3(3.15f, 0f, -3.56f), 2);
            else Shelf(new Vector3(3.15f, 0f, -3.7f), 1.9f, goods, jars);
            Shelf(new Vector3(5.3f, 0f, -3.7f), 1.9f, goods, jars);
            // the pharmacy keeps its medicine fridge behind the counter, right of the pharmacist (between shelf and step)
            if (id == "goobytheke") Cooler("cooler_med", new Vector3(-1.74f, 0f, -3.56f), 1);
            // board 10: the side walls carry shelves too, so the shop is full of goods wherever the eye lands
            // (the 42° camera sees only ±5 m at mid-room, so they stand well in from the walls, clear of the back shelves)
            Shelf(new Vector3(-5.1f, 0f, -2.2f), 2.2f, goods, jars, 90f);
            Shelf(new Vector3(5.1f, 0f, -2.2f), 2.2f, goods, jars, -90f);
            Counter(new Vector3(0f, 0f, -1.35f), 3.4f, id == "goobytheke" ? "#FFFFFF" : id == "pow" ? "#FFD166" : id == "baumarkt" ? "#B98B5E" : "#E8C9A0");
            // shopping baskets by the counter
            foreach (var (bx, bz, bc) in new[] { (-2.3f, -0.9f, "#FF8B8B"), (-2.3f, -0.55f, "#7FB8FF") })
                Box("Basket", new Vector3(bx, 0.14f, bz), new Vector3(0.44f, 0.28f, 0.3f), Solid(bc, 0.3f));
            switch (id)
            {
                case "mart":
                    // board 10: "GOOD THINGS HAPPEN HERE" on the back wall right of the title, packed produce left
                    Poster(new Vector3(-3.2f, 2.58f, -3.92f), "poster_good", new Vector2(1.15f, 1.48f), "#F26D6D");
                    Box("ProduceTable", new Vector3(2.6f, 0.28f, 0.55f), new Vector3(1.8f, 0.56f, 1.0f), Solid("#D9B48A", 0.15f));
                    FruitCrate(new Vector3(2.05f, 0.56f, 0.42f), "#FF6B6B", 8f, 0.72f);
                    FruitCrate(new Vector3(2.85f, 0.56f, 0.38f), "#F2913D", -12f, 0.68f);
                    FruitCrate(new Vector3(2.45f, 0.56f, 0.82f), "#FFD166", 16f, 0.62f);
                    FruitCrate(new Vector3(2.35f, 0f, 1.55f), "#FF6B6B", 8f, 0.9f);
                    FruitCrate(new Vector3(2.35f, 0.34f, 1.55f), "#7FCF9B", -6f, 0.9f);
                    FruitCrate(new Vector3(3.05f, 0f, 0.85f), "#F2913D", -14f, 0.8f);
                    FruitCrate(new Vector3(1.7f, 0f, 2.15f), "#FFD166", 20f, 0.7f);
                    FruitCrate(new Vector3(2.95f, 0f, 1.95f), "#FF8B8B", -18f, 0.82f);
                    FruitCrate(new Vector3(1.55f, 0f, 2.55f), "#7FCF9B", 28f, 0.72f);
                    Prop("crate", new Vector3(-3.4f, 0f, 0.2f), 20f, 0.6f, 0.6f);
                    Prop("crate", new Vector3(-4.0f, 0f, -0.3f), -10f, 0.6f, 0.6f);
                    HangIvy(new Vector3(2.28f, 2.92f, -2.05f), 1.08f);
                    HangIvy(new Vector3(-2.28f, 2.92f, -2.05f), 0.98f);
                    // bags + candy on the counter (concept till is busy, not a bare plank)
                    foreach (var (bx, bz, bc) in new[] { (-1.12f, -0.98f, "#EBD9C6"), (-0.92f, -0.90f, "#FFF8EE"), (0.72f, -1.02f, "#FFD166") })
                        Box("PaperBag", new Vector3(bx, 1.08f, bz), new Vector3(0.15f, 0.22f, 0.09f), Solid(bc, 0.15f));
                    Cyl("CandyJar", new Vector3(0.48f, 1.10f, -1.08f), 0.07f, 0.18f, Solid("#FF8B8B", 0.45f));
                    Cyl("CandyLid", new Vector3(0.48f, 1.20f, -1.08f), 0.074f, 0.02f, Solid("#FFF8EE", 0.2f));
                    break;
                case "goobyman":
                    Prop("cabinet", new Vector3(-6.2f, 0f, -1.4f), 90f, 1.2f, 0.6f);
                    var heroYellow = Solid("#FFD166", 0.25f);
                    var hero = Box("HeroPodium", new Vector3(5.2f, 0.2f, -1.2f), new Vector3(1.4f, 0.4f, 1.4f), heroYellow);
                    Model("podium", new Vector3(5.2f, 0f, -1.2f), 0f, new Vector3(0.36f, 2f, 0.36f), new[] { hero }, ("gb_podium", heroYellow));
                    Sphere("HeroStar", new Vector3(5.2f, 1.0f, -1.2f), 0.4f, Solid("#FF98C1", 0.4f));
                    Poster(new Vector3(-2.8f, 2.7f, -3.9f), "pic_gooby", new Vector2(0.7f, 0.7f), "#FFD166");
                    break;
                case "pow":
                    for (var i = 0; i < 4; i++)
                    {
                        var c = new[] { "#FF8B8B", "#7FB8FF", "#FFD166", "#7FCF9B" }[i];
                        Cyl("BalloonString", new Vector3(-5.6f + i * 0.5f, 2.2f, -0.8f), 0.008f, 1.4f, Solid("#3F3A45"));
                        Sphere("Balloon", new Vector3(-5.6f + i * 0.5f, 3.1f, -0.8f), 0.28f, Solid(c, 0.5f));
                    }
                    Prop("bear", new Vector3(5.4f, 0f, -0.9f), 20f, 0.8f, 0.8f);
                    Prop("box", new Vector3(6.2f, 0f, -2.2f), 0f, 0.7f, 0.7f);
                    Box("CameraCase", new Vector3(-1.0f, 1.08f, -1.35f), new Vector3(0.5f, 0.3f, 0.34f), Solid("#3F3A45", 0.4f));
                    Cyl("CameraLens", new Vector3(-1.0f, 1.1f, -1.13f), 0.09f, 0.08f, Solid("#7FB8FF", 0.7f));
                    break;
                case "baumarkt":
                    for (var i = 0; i < 5; i++)
                        Box("Plank", new Vector3(-6.0f + (i % 2) * 0.3f, 0.06f + i * 0.09f, 0.2f), new Vector3(0.26f, 0.08f, 2.4f), Solid("#D9B48A", 0.1f));
                    foreach (var (x, c) in new[] { (5.0f, "#FF8B8B"), (5.5f, "#7FB8FF"), (6.0f, "#FFD166") })
                        Cyl("PaintTin", new Vector3(x, 0.2f, -0.6f), 0.2f, 0.4f, Solid(c, 0.3f));
                    Box("SawBench", new Vector3(5.4f, 0.4f, -2.2f), new Vector3(1.6f, 0.8f, 0.7f), Solid("#B98B5E", 0.1f));
                    Prop("crate", new Vector3(-5.8f, 0f, -1.5f), 0f, 0.7f, 0.7f);
                    break;
                case "goobytheke":
                    Box("RedCross", new Vector3(2.7f, 2.6f, -3.88f), new Vector3(0.7f, 0.2f, 0.04f), Solid("#FF6B6B", 0.2f), false);
                    Box("RedCross", new Vector3(2.7f, 2.6f, -3.88f), new Vector3(0.2f, 0.7f, 0.04f), Solid("#FF6B6B", 0.2f), false);
                    Prop("cabinet", new Vector3(-6.2f, 0f, -1.6f), 90f, 1.2f, 0.6f);
                    Prop("chair", new Vector3(5.6f, 0f, 0.4f), -30f, 0.5f, 0.5f);
                    Prop("chair", new Vector3(6.3f, 0f, -0.4f), -60f, 0.5f, 0.5f);
                    break;
            }
            Plant(id == "mart" ? new Vector3(-6.4f, 0f, 3.4f) : new Vector3(-6.3f, 0f, 2.2f));
            Prop("rug", new Vector3(0f, 0f, 0.9f), 0f, 3.0f, 1.6f, 1f);
        }

        private void BuildPractice(bool vet)
        {
            // board 10: a mint reception desk with the paw plaque, a waiting lounge by the door, the practice cross
            Counter(new Vector3(vet ? 0f : -1.4f, 0f, -1.4f), 2.8f, vet ? "#9ED9C8" : "#FFFFFF", vet ? "#FFFFFF" : "#98E7E1", till: false, model: "vet_counter");
            var reception = Box("Reception", new Vector3(vet ? 0.85f : -1.4f, 1.08f, -1.52f), new Vector3(vet ? 0.55f : 0.9f, 0.28f, 0.32f), Solid("#FFF8EE", 0.3f));
            Model("reception_monitor", new Vector3(vet ? 0.85f : -1.4f, 0.96f, -1.52f), 0f, Vector3.one, new[] { reception });
            // the vet's vaccine fridge on the back wall between the counter and the medicine shelf (under the paw poster)
            if (vet) Cooler("cooler_med", new Vector3(-3.55f, 0f, -3.56f), 1, 0.86f);
            if (vet)
            {
                var paw = ArtLibrary.Quad(_t, "PawPlaque", Origin + new Vector3(0.85f, 0.55f, -0.98f), Quaternion.identity, new Vector2(0.4f, 0.4f), ArtLibrary.Decal("poster_pfoten"));
                paw.transform.rotation = Quaternion.identity;
            }
            // camera yaw 180, 42° vFOV: world +X is left. Floor objects closer to the door sink
            // under the dialog card; z≈1.4 keeps the mint lounge in the left third above it.
            Lounge(new Vector3(2.28f, 0f, 1.42f), 18f, "#9ED9C8");
            Prop("chair", new Vector3(2.62f, 0f, 0.70f), 28f, 0.55f, 0.5f);
            if (vet) Poster(new Vector3(-3.35f, 2.58f, -3.92f), "poster_pfoten", new Vector2(1.2f, 1.5f), "#3E7A5A");
            // exam table stays on the back-right (−X) so the lounge reads in the left third: build_scenery.py exam_table
            // turned 90° — drawers + paw towards the room, head wedge to the back wall, the step towards the door
            var bed = Prop("bed", new Vector3(-5.15f, 0f, -2.55f), -90f, 2.0f, 1.0f);
            // (x −4.45 / z −2.3: the head end clears the deeper med_cabinet and the long side reaches into the door shot)
            Model("exam_table", new Vector3(-4.45f, 0f, -2.3f), 90f, Vector3.one, new[] { bed });
            Prop("lamp_floor", new Vector3(-5.4f, 0f, -3.2f), 0f, 0.5f, 0.5f, 1.2f);
            // med_cabinet: mint cupboard + worktop + open jar shelves, all baked (the primitive shelf is its fallback)
            Shelf(new Vector3(-5.2f, 0f, -3.7f), 1.8f, new[] { "#FFFFFF", "#98E7E1", "#FF8B8B", "#FFF8EE" }, jars: true, stocked: "med_cabinet");
            // waiting_chairs against the back wall left of the desk: the only free strip the lounge and the tab column
            // leave in the door shot (x≈2–3.8 at the wall; further left hides behind the tabs)
            var seatMint = Solid("#7FD3C1", 0.25f);
            Model("waiting_chairs", new Vector3(2.9f, 0f, -3.45f), 0f, Vector3.one, new[]
            {
                Box("WaitingSeat", new Vector3(2.9f, 0.44f, -3.47f), new Vector3(1.7f, 0.08f, 0.48f), seatMint),
                Box("WaitingBack", new Vector3(2.9f, 0.77f, -3.7f), new Vector3(1.7f, 0.38f, 0.08f), seatMint),
                Box("WaitingBeam", new Vector3(2.9f, 0.2f, -3.45f), new Vector3(1.4f, 0.36f, 0.08f), Solid("#E3E7EE", 0.8f)),
            });
            Box("RedCross", new Vector3(-5.2f, 2.75f, -3.88f), new Vector3(0.7f, 0.2f, 0.04f), Solid("#FF6B6B", 0.2f), false);
            Box("RedCross", new Vector3(-5.2f, 2.75f, -3.88f), new Vector3(0.2f, 0.7f, 0.04f), Solid("#FF6B6B", 0.2f), false);
            if (!vet) Poster(new Vector3(3.2f, 2.5f, -3.9f), "pic_gooby", new Vector2(0.8f, 0.8f), "#98E7E1");
            Poster(new Vector3(-2.9f, 2.5f, -3.9f), "pic_leaf", new Vector2(0.6f, 0.6f));
            // a chair at the reception and a scale on the floor
            Prop("chair", new Vector3(-4.6f, 0f, -0.6f), 60f, 0.5f, 0.5f);
            Cyl("Scale", new Vector3(5.6f, 0.05f, 0.6f), 0.35f, 0.1f, Solid("#FFFFFF", 0.3f));
            Cyl("ScaleDial", new Vector3(5.6f, 0.11f, 0.6f), 0.14f, 0.02f, Solid("#98E7E1", 0.4f));
            Plant(new Vector3(6.3f, 0f, 1.8f));
            Prop("rug", new Vector3(0f, 0f, 0.9f), 0f, 2.6f, 1.4f, 1f);
        }

        private void BuildPost()
        {
            // glass partition with a service window, letters and a stamp pad on the counter: primitives for the fallback,
            // build_scenery.py post_counter bakes them all (partition at z −1.45, letters x −1.4…−0.84, pad x 1.3)
            var postBits = new List<GameObject>
            {
                Box("Partition", new Vector3(0f, 1.55f, -1.45f), new Vector3(4.2f, 1.2f, 0.04f), ArtLibrary.Transparent(new Color(0.85f, 0.93f, 1f, 0.35f)), false),
                Box("PartitionTop", new Vector3(0f, 2.17f, -1.45f), new Vector3(4.3f, 0.06f, 0.08f), Solid("#6B4F3A", 0.2f)),
                Box("ServiceGap", new Vector3(0f, 1.25f, -1.45f), new Vector3(1.2f, 0.6f, 0.06f), Solid("#FFF8EE", 0.1f), false),
                Box("StampPad", new Vector3(1.3f, 0.99f, -1.2f), new Vector3(0.24f, 0.06f, 0.16f), Solid("#3F3A45", 0.3f), false),
            };
            for (var i = 0; i < 3; i++)
                postBits.Add(Box("Letter", new Vector3(-1.4f + i * 0.28f, 0.97f, -1.1f), new Vector3(0.3f, 0.01f, 0.2f), Solid("#FFFFFF", 0.1f), false));
            Counter(new Vector3(0f, 0f, -1.4f), 4.2f, "#FFD98A", "#D9B48A", till: false, model: "post_counter", extra: postBits);
            // parcel racks left and right (parcel_shelf: parcels baked in), a yellow letterbox, the clock
            var parcels = new[] { "#D9B48A", "#C8B7A1", "#EBD9C6", "#B98B5E" };
            Shelf(new Vector3(-5.3f, 0f, -3.7f), 1.9f, parcels, stocked: "parcel_shelf");
            Shelf(new Vector3(-3.15f, 0f, -3.7f), 1.9f, parcels, stocked: "parcel_shelf");
            Shelf(new Vector3(4.2f, 0f, -3.7f), 2.4f, parcels, stocked: "parcel_shelf");
            // mailbox_post left of the counter in front of the left rack (at x 6 / z −0.6 it stood outside the door shot)
            var box = new Vector3(3.0f, 0f, -2.2f);
            var boxRot = Quaternion.Euler(0f, -25f, 0f);
            var letterbox = new[]
            {
                Box("Letterbox", box + new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 1.1f, 0.42f), Solid("#FFD98A", 0.3f)),
                Box("LetterboxLid", box + new Vector3(0f, 1.15f, 0f), new Vector3(0.54f, 0.12f, 0.46f), Solid("#FFD98A", 0.3f)),
                Box("LetterboxSlot", box + boxRot * new Vector3(0f, 0.85f, 0.24f), new Vector3(0.3f, 0.04f, 0.03f), Solid("#3F3A45"), false),
            };
            foreach (var go in letterbox) go.transform.rotation = boxRot;
            Model("mailbox_post", box, -25f, Vector3.one, letterbox);
            var clock = Cyl("Clock", new Vector3(2.9f, 2.6f, -3.88f), 0.34f, 0.05f, Solid("#FFFFFF", 0.3f));
            clock.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Box("ClockHand", new Vector3(2.9f, 2.7f, -3.84f), new Vector3(0.03f, 0.22f, 0.01f), Solid("#3F3A45"), false);
            Box("ClockHand", new Vector3(3.0f, 2.6f, -3.84f), new Vector3(0.18f, 0.03f, 0.01f), Solid("#3F3A45"), false);
            Prop("crate", new Vector3(-6.2f, 0f, -0.5f), 0f, 0.7f, 0.7f);
            Plant(new Vector3(6.3f, 0f, 1.8f));
            Prop("rug", new Vector3(0f, 0f, 0.9f), 0f, 3.0f, 1.6f, 1f);
        }

        private void BuildMarket()
        {
            // Greta's stall in the middle, two more at the sides, crates and produce between, hedges and trees behind
            Scenery.Place(_t, "market_stall_green", new Vector3(0f, 0f, -2.6f), 0f);
            Scenery.Place(_t, "market_stall_red", new Vector3(-4.4f, 0f, -2.2f), 18f);
            BuildOwnStall();
            // hanging stall sign stays by the left (world +X) yellow awning — "REGIONAL FRISCH LECKER"
            Scenery.Place(_t, "market_sign", new Vector3(6.75f, 0f, -0.75f), -38f, 0.95f);
            ArtLibrary.Quad(_t, "Lettering", Origin + new Vector3(6.75f, 1.5f, -0.75f) + Quaternion.Euler(0f, -38f, 0f) * new Vector3(0f, 0f, 0.12f), Quaternion.Euler(0f, -38f, 0f), new Vector2(1.55f, 0.62f), ArtLibrary.Decal("poster_regional"));
            // board 10: wooden hours board hanging on Greta's green stall (the readable WOCHENMARKT / Sa 8–14 Uhr)
            var stallHours = new Vector3(0f, 2.08f, -1.78f);
            Box("StallHoursFrame", stallHours, new Vector3(1.36f, 0.50f, 0.05f), Solid("#8B5E3C", 0.2f));
            ArtLibrary.Quad(_t, "StallHours", Origin + stallHours + new Vector3(0f, 0f, 0.04f), Quaternion.identity, new Vector2(1.24f, 0.40f), ArtLibrary.Decal("stall_hours"));
            Scenery.Place(_t, "garden_crate", new Vector3(2.4f, 0f, 0.4f), 30f, 0.8f);
            Scenery.Place(_t, "street_planter", new Vector3(-6.0f, 0f, 0.6f), 0f);
            Scenery.Place(_t, "tulips", new Vector3(6.0f, 0f, 0.8f), 40f, 1.2f);
            foreach (var x in new[] { -7f, -3.5f, 3.5f, 7f }) Scenery.Place(_t, "hedge", new Vector3(x, 0f, -5.4f), 0f, 0.95f);
            Scenery.Place(_t, "tree_round_big", new Vector3(-8.5f, 0f, -6.5f), 20f, 0.95f);
            Scenery.Place(_t, "tree_round_big", new Vector3(8.5f, 0f, -6.8f), 70f, 1.05f);
            Scenery.Place(_t, "tree_round", new Vector3(-4.5f, 0f, -9f), 0f, 1.1f);
            Scenery.Place(_t, "tree_round", new Vector3(4.8f, 0f, -9.5f), 50f, 1.15f);
            Scenery.Place(_t, "bush_flowers", new Vector3(-6.4f, 0f, -3.9f), 0f, 0.9f);
            Scenery.Place(_t, "bush_flowers", new Vector3(6.6f, 0f, -4.1f), 90f, 0.85f);
            Scenery.Place(_t, "lamp_post", new Vector3(6.4f, 0f, 2.6f), 180f, 0.8f);
            Scenery.Place(_t, "lamp_post", new Vector3(-6.6f, 0f, 2.4f), 0f, 0.8f);
            Scenery.Place(_t, "dove", new Vector3(3.2f, 0.4f, 0.4f), 200f);
            // board 10: the town fountain behind the middle stall, its jet catching the light
            var stone = ArtLibrary.Pbr("concrete", 1.6f, ArtLibrary.Hex("#E4DDD2"), 0.2f, 0.6f);
            var water = Solid("#8FD0E8", 0.9f);
            Cyl("FountainBasin", new Vector3(0f, 0.35f, -8.6f), 2.4f, 0.7f, stone);
            Cyl("FountainWater", new Vector3(0f, 0.66f, -8.6f), 2.2f, 0.1f, water);
            Cyl("FountainStem", new Vector3(0f, 1.6f, -8.6f), 0.3f, 1.9f, stone);
            var bowl = Cyl("FountainBowl", new Vector3(0f, 2.55f, -8.6f), 1.1f, 0.3f, stone);
            bowl.transform.localScale = new Vector3(2.2f, 0.15f, 2.2f);
            Cyl("FountainBowlWater", new Vector3(0f, 2.68f, -8.6f), 0.95f, 0.06f, water);
            Cyl("FountainTop", new Vector3(0f, 3.0f, -8.6f), 0.14f, 0.6f, stone);
            Sphere("FountainJet", new Vector3(0f, 3.45f, -8.6f), 0.22f, ArtLibrary.Glow(new Color(1.4f, 1.9f, 2.2f)));
            foreach (var s in new[] { -1f, 1f })
                Cyl("FountainSplash", new Vector3(s * 0.45f, 3.05f, -8.6f), 0.05f, 0.5f, water).transform.rotation = Quaternion.Euler(0f, 0f, s * 35f);
            // string lights swinging between the stalls and heaped produce crates at their feet
            var bulb = ArtLibrary.Glow(new Color(2.4f, 2.0f, 1.2f));
            for (var i = 0; i < 14; i++)
            {
                var u = i / 13f;
                Sphere("StringBulb", new Vector3(Mathf.Lerp(-6.2f, 6.2f, u), 3.05f - Mathf.Sin(u * Mathf.PI) * 0.45f, -0.9f), 0.07f, bulb);
            }
            Box("StringWire", new Vector3(0f, 2.85f, -0.9f), new Vector3(12.4f, 0.012f, 0.012f), Solid("#3F3A45"), false);
            FruitCrate(new Vector3(-2.2f, 0f, 0.35f), "#FF6B6B", 12f, 0.95f);
            FruitCrate(new Vector3(-2.2f, 0.34f, 0.35f), "#7FCF9B", -8f, 0.95f);
            FruitCrate(new Vector3(-1.35f, 0f, 1.05f), "#F2913D", -10f, 0.85f);
            FruitCrate(new Vector3(1.55f, 0f, 0.55f), "#FFD166", 18f, 0.8f);
            FruitCrate(new Vector3(-5.2f, 0f, 0.2f), "#FFD166", 30f, 0.7f);
            FruitCrate(new Vector3(4.4f, 0f, 0.15f), "#B58CE4", -25f, 0.7f);
            FruitCrate(new Vector3(-0.95f, 0f, -1.35f), "#FF6B6B", 14f, 0.85f);
            FruitCrate(new Vector3(0.90f, 0f, -1.25f), "#FFD166", -10f, 0.8f);
            FruitCrate(new Vector3(-0.15f, 0.02f, -1.72f), "#FF6B6B", 6f, 0.78f);
            FruitCrate(new Vector3(0.55f, 0.02f, -1.68f), "#F2913D", -16f, 0.72f);
            foreach (var (fx, fy, fz, fc) in new[] {
                (-0.55f, 1.08f, -1.95f, "#FF6B6B"), (0.15f, 1.12f, -1.92f, "#FFD166"),
                (0.70f, 1.06f, -1.98f, "#7FCF9B"), (-0.20f, 1.22f, -1.88f, "#F2913D"),
                (-0.85f, 1.04f, -1.82f, "#FF8B8B"), (0.40f, 1.18f, -1.78f, "#F2C14E"),
                (0.05f, 1.28f, -1.70f, "#FF6B6B"), (-0.40f, 1.14f, -1.64f, "#7FCF9B") })
                Sphere("StallFruit", new Vector3(fx, fy, fz), 0.11f, Solid(fc, 0.35f));
            // board 10: a modest standing chalkboard to the right of Greta's stall — not a wall filling the right third.
            // A-frame GLB is edge-on from the door, so this is boxes facing +Z (camera looks −Z).
            var chalk = new Vector3(-2.08f, 0.88f, -0.62f);
            var chalkTilt = Quaternion.Euler(-10f, 6f, 0f);
            var wood = Solid("#6B4F3A", 0.15f);
            foreach (var s in new[] { -1f, 1f })
            {
                var leg = Box("ChalkLeg", chalk + new Vector3(s * 0.22f, -0.48f, 0.04f), new Vector3(0.055f, 1.05f, 0.055f), wood);
                leg.transform.rotation = Quaternion.Euler(-16f, 6f, s * 5f);
            }
            var frame = Box("ChalkFrame", chalk, new Vector3(0.62f, 1.08f, 0.05f), Solid("#4A3A2C", 0.2f));
            frame.transform.rotation = chalkTilt;
            ArtLibrary.Quad(_t, "ChalkHours", Origin + chalk + chalkTilt * new Vector3(0f, 0f, 0.035f), chalkTilt, new Vector2(0.54f, 0.96f), ArtLibrary.Decal("chalkboard_markt"));
        }

        private void BuildShowroom()
        {
            // glass band over the back wall, two cars on podiums, Benzino at his desk, tyre stack and a poster
            Box("ShowroomGlass", new Vector3(0f, 2.9f, -3.9f), new Vector3(9f, 1.6f, 0.04f), Solid("#CFE6F7", 0.6f), false);
            // board 10: city through the showroom windows (the cyan band alone read as a closed beige hall)
            foreach (var x in new[] { -4.6f, 4.6f })
                ArtLibrary.Quad(_t, "CityView", Origin + new Vector3(x, 2.55f, -3.87f), Quaternion.identity,
                    new Vector2(1.48f, 1.18f), ArtLibrary.Backdrop("bg_town_horizon"));
            foreach (var (x, z, yaw, col) in new[] { (2.45f, -1.15f, -32f, "#F2C14E"), (-1.92f, -1.25f, 16f, "#4E79D6") })
            {
                // board 10: silver drum + light ring + cream disc (build_scenery.py podium, top at 0.2 m like the cylinder)
                var cream = Solid("#FFF8EE", 0.3f);
                var disc = Cyl("Podium", new Vector3(x, 0.1f, z), 2.0f, 0.2f, cream);
                Model("podium", new Vector3(x, 0f, z), 0f, Vector3.one, new[] { disc }, ("gb_podium", cream));
                var car = new GameObject("ShowroomCar").transform;
                car.SetParent(_t, false);
                car.position = Origin + new Vector3(x, 0.2f, z);
                car.rotation = Quaternion.Euler(0f, yaw, 0f);
                car.localScale = Vector3.one * 0.82f;
                CityScene.TintCar(car, ArtLibrary.Hex(col));
                _ = AssetLoader.InstanceAsync(AssetLoader.VehiclePath("car_hero"), car, "car_hero");
            }
            // sales_desk: white pedestal on the left, laptop back + brochures + toy car towards the customer
            var desk = Prop("desk", new Vector3(0f, 0f, -1.5f), 0f, 1.6f, 0.8f);
            Prop("chair", new Vector3(0f, 0f, -2.3f), 0f, 0.5f, 0.5f);
            var brochures = Box("Brochures", new Vector3(-0.4f, 0.79f, -1.4f), new Vector3(0.36f, 0.04f, 0.26f), Solid("#FF8B8B", 0.2f), false);
            Model("sales_desk", new Vector3(0f, 0f, -1.5f), 0f, Vector3.one, new[] { desk, brochures });
            // tyre_stack in the gap between the yellow car and the desk at the back wall (at x −6.2 / z −0.6 it stood
            // outside the door shot)
            var tyres = new List<GameObject>();
            for (var i = 0; i < 4; i++)
            {
                var tyre = Cyl("Tyre", new Vector3(1.35f, 0.1f + i * 0.2f, -3.45f), 0.36f, 0.2f, Solid("#33303A", 0.2f));
                tyre.transform.localScale = new Vector3(0.72f, 0.1f, 0.72f);
                tyres.Add(tyre);
            }
            Model("tyre_stack", new Vector3(1.35f, 0f, -3.45f), -10f, Vector3.one, tyres);
            // board 10: "Kleine Wege, große Ziele" on the back-right wall (not a second roll-up off-camera)
            Poster(new Vector3(-3.78f, 2.18f, -3.92f), "poster_ziele", new Vector2(0.78f, 1.05f), "#F4F4F8");
            Plant(new Vector3(-6.3f, 0f, 1.6f));
            // board 10: a ring light over the desk, roll-up banners on stands ("Kleine Wege, große Ziele" / "Neue Wege warten")
            // an open ring (a filled disc hid "AUTOHAUS BLECHBERT"), high enough that its back arc runs above the letters
            var ring = ArtLibrary.Glow(new Color(2.4f, 2.3f, 2.0f));
            var railMat = Solid("#EDE8DF", 0.55f);
            const float ringY = 3.5f, ringR = 1.52f;
            Cyl("RingCord", new Vector3(0f, (5f + ringY) * 0.5f, -1.5f), 0.02f, 5f - ringY, Solid("#3F3A45", 0.4f));
            for (var i = 0; i < 20; i++)
            {
                var a = i / 20f * Mathf.PI * 2f;
                var b = (i + 1) / 20f * Mathf.PI * 2f;
                var pa = new Vector3(Mathf.Cos(a) * ringR, ringY, -1.5f + Mathf.Sin(a) * ringR);
                var pb = new Vector3(Mathf.Cos(b) * ringR, ringY, -1.5f + Mathf.Sin(b) * ringR);
                var seg = Cyl("RingRail", (pa + pb) * 0.5f, 0.035f, (pb - pa).magnitude, railMat);
                seg.transform.rotation = Quaternion.FromToRotation(Vector3.up, pb - pa);
                Sphere("RingLight", pa, 0.065f, ring);
            }
            var ringFill = new GameObject("CeilingLight").AddComponent<Light>();
            ringFill.transform.SetParent(_t, false);
            ringFill.transform.position = Origin + new Vector3(0f, ringY - 0.25f, -1.5f);
            ringFill.type = LightType.Point;
            ringFill.color = ArtLibrary.Hex("FFF4E0");
            ringFill.intensity = 1.15f;
            ringFill.range = 8f;
            ringFill.shadows = LightShadows.None;
            // board 10: "Neue Wege warten" roll-up to the right of the blue car (not over the hood)
            foreach (var (x, z, yaw, tex, size) in new[] {
                (-3.38f, -0.72f, 8f, "poster_wege", new Vector2(0.54f, 1.48f)),
            })
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var stand = Box("BannerStand", new Vector3(x, 0.03f, z), new Vector3(0.72f, 0.06f, 0.28f), Solid("#C9CED6", 0.5f));
                stand.transform.rotation = rot;
                Poster(new Vector3(x, 1.05f, z), tex, size, "#F4F4F8", yaw);
            }
        }

        private void BuildPark()
        {
            // plaza: ticket booth left, the snack stall of the Naschgasse right, bunting, the wheel and carousel behind
            Scenery.Place(_t, "park_gate", new Vector3(0f, 0f, -9.5f), 180f, 0.9f);
            Scenery.Place(_t, "ferris_wheel", new Vector3(-5.5f, 0f, -17f), 0f, 1.1f);
            Scenery.Place(_t, "market_stall_yellow", new Vector3(4.6f, 0f, -2.6f), -18f);
            // ticket booth (build_scenery.py ticket_booth: window at y 1.46 over the ledge, striped front and plaza side)
            var booth = new List<GameObject>
            {
                Box("Booth", new Vector3(-3.4f, 1.15f, -2.6f), new Vector3(1.8f, 2.3f, 1.5f), Solid("#FFF8EE", 0.2f)),
                Box("BoothRoof", new Vector3(-3.4f, 2.4f, -2.6f), new Vector3(2.1f, 0.16f, 1.8f), Solid("#FFD166", 0.2f)),
                Box("BoothWindow", new Vector3(-3.4f, 1.5f, -1.83f), new Vector3(1.0f, 0.7f, 0.04f), Solid("#CFE6F7", 0.6f), false),
                Box("BoothLedge", new Vector3(-3.4f, 1.1f, -1.8f), new Vector3(1.2f, 0.08f, 0.16f), Solid("#D9B48A", 0.2f)),
            };
            var peak = ArtLibrary.Cylinder(_t, "BoothPeak", Origin + new Vector3(-3.4f, 2.85f, -2.6f), 1.1f, 0.7f, Solid("#F781B0", 0.2f));
            peak.transform.localScale = new Vector3(2.2f, 0.35f, 2.2f);
            booth.Add(peak);
            for (var i = 0; i < 4; i++) booth.Add(Box("BoothStripe", new Vector3(-4.0f + i * 0.4f, 0.5f, -1.82f), new Vector3(0.4f, 1.0f, 0.03f), Solid(i % 2 == 0 ? "#F781B0" : "#FFFFFF", 0.1f), false));
            Model("ticket_booth", new Vector3(-3.4f, 0f, -2.6f), 0f, Vector3.one, booth);
            // carousel: platform, pole, roof, four ponies (carousel_small: same r 2.6 deck, roof 3.2–4.15 m, ponies at 1.75 m)
            var carousel = new List<GameObject>
            {
                Cyl("CarouselBase", new Vector3(5.5f, 0.15f, -8.5f), 2.6f, 0.3f, Solid("#FFF8EE", 0.3f)),
                Cyl("CarouselPole", new Vector3(5.5f, 2.0f, -8.5f), 0.12f, 3.6f, Solid("#FFD166", 0.3f)),
            };
            var croof = ArtLibrary.Cylinder(_t, "CarouselRoof", Origin + new Vector3(5.5f, 3.6f, -8.5f), 2.9f, 0.9f, Solid("#F781B0", 0.2f));
            croof.transform.localScale = new Vector3(5.8f, 0.45f, 5.8f);
            carousel.Add(croof);
            for (var i = 0; i < 4; i++)
            {
                var a = i * Mathf.PI * 0.5f;
                var p = new Vector3(5.5f + Mathf.Cos(a) * 1.7f, 0.9f, -8.5f + Mathf.Sin(a) * 1.7f);
                carousel.Add(Cyl("PonyPole", p + Vector3.up * 0.9f, 0.03f, 2.4f, Solid("#FFD166", 0.3f)));
                var pony = Sphere("Pony", p, 0.32f, Solid(new[] { "#FFFFFF", "#B58CE4", "#98E7E1", "#FF98C1" }[i], 0.3f));
                pony.transform.localScale = new Vector3(0.9f, 0.6f, 0.45f);
                carousel.Add(pony);
            }
            Model("carousel_small", new Vector3(5.5f, 0f, -8.5f), 0f, Vector3.one, carousel);
            // bunting between two posts and a bunch of balloons at the booth
            foreach (var x in new[] { -5.5f, 5.5f }) Cyl("BuntingPost", new Vector3(x, 1.6f, 0.4f), 0.05f, 3.2f, Solid("#3F3A45"));
            for (var i = 0; i < 12; i++)
            {
                var u = i / 11f;
                var p = new Vector3(Mathf.Lerp(-5.5f, 5.5f, u), 3.15f - Mathf.Sin(u * Mathf.PI) * 0.5f, 0.4f);
                Box("Pennant", p, new Vector3(0.26f, 0.32f, 0.02f), Solid(new[] { "#FFD166", "#7FB8FF", "#FF8B8B", "#98E7E1" }[i % 4], 0.1f), false);
            }
            for (var i = 0; i < 3; i++)
            {
                Cyl("BalloonString", new Vector3(-2.2f + i * 0.25f, 2.2f, -1.4f), 0.008f, 1.3f, Solid("#3F3A45"));
                Sphere("Balloon", new Vector3(-2.2f + i * 0.25f, 3.0f + i * 0.15f, -1.4f), 0.3f, Solid(new[] { "#FF8B8B", "#FFD166", "#7FB8FF" }[i], 0.5f));
            }
            Scenery.Place(_t, "tree_round_big", new Vector3(-9f, 0f, -6f), 30f, 1.0f);
            Scenery.Place(_t, "tree_round_big", new Vector3(10f, 0f, -4f), 80f, 0.9f);
            Scenery.Place(_t, "bush_flowers", new Vector3(-6.4f, 0f, -1.2f), 0f, 0.9f);
            Scenery.Place(_t, "lamp_post", new Vector3(-6.6f, 0f, 2.4f), 0f, 0.8f);
        }

        private void BuildAirport()
        {
            // check-in counter, departure board, luggage trolley, window band with the apron and the plane outside
            // checkin_desk: desk + luggage belt on the right (Unity −X), the customer screen baked in at x 1.4
            var screen = new[]
            {
                Box("CheckInScreen", new Vector3(1.4f, 1.3f, -1.5f), new Vector3(0.5f, 0.4f, 0.06f), Solid("#3F3A45", 0.3f)),
                Box("CheckInGlow", new Vector3(1.4f, 1.3f, -1.46f), new Vector3(0.42f, 0.32f, 0.01f), Solid("#98E7E1", 0.6f), false),
            };
            Counter(new Vector3(0f, 0f, -1.4f), 4.6f, "#FFFFFF", "#8FD0E8", till: false, model: "checkin_desk", extra: screen);
            // departure_board: origin on the floor under it, back 4 cm behind the anchor (flush with the wainscot)
            var board = new List<GameObject> { Box("Board", new Vector3(-5.0f, 1.3f, -3.9f), new Vector3(3.0f, 1.2f, 0.08f), Solid("#3E4A3F", 0.2f)) };
            for (var i = 0; i < 4; i++)
            {
                board.Add(Box("BoardRow", new Vector3(-5.8f, 1.7f - i * 0.26f, -3.85f), new Vector3(1.0f, 0.15f, 0.01f), Solid(i % 2 == 0 ? "#FFD166" : "#FFF8EE", 0.3f), false));
                board.Add(Box("BoardRow", new Vector3(-4.3f, 1.7f - i * 0.26f, -3.85f), new Vector3(0.9f, 0.15f, 0.01f), Solid("#7FCF9B", 0.3f), false));
            }
            Model("departure_board", new Vector3(-5.0f, 0f, -3.9f), 0f, Vector3.one, board);
            Scenery.Place(_t, "plane_small", new Vector3(4.2f, 0f, -9.5f), 40f, 0.85f);
            Scenery.Place(_t, "tree_small", new Vector3(-1.5f, 0f, -11f), 0f, 1.2f);
            // luggage_cart under the departure board right of the desk (at x −5.4 / z 0.4 it stood outside the door shot),
            // a bench, a plant; GOOBY FREE takes the apron window on the left
            var cart = new Vector3(-3.2f, 0f, -2.8f);
            var cartRot = Quaternion.Euler(0f, 18f, 0f);
            var trolley = new[]
            {
                Box("Trolley", cart + new Vector3(0f, 0.22f, 0f), new Vector3(0.95f, 0.06f, 0.6f), Solid("#C9CED6", 0.4f)),
                Box("Case", cart + new Vector3(0f, 0.36f, 0f), new Vector3(0.72f, 0.22f, 0.46f), Solid("#FF8B8B", 0.3f)),
                Box("Case", cart + cartRot * new Vector3(-0.08f, 0.71f, -0.05f), new Vector3(0.4f, 0.5f, 0.22f), Solid("#7FB8FF", 0.3f)),
            };
            foreach (var go in trolley) go.transform.rotation = cartRot;
            Model("luggage_cart", cart, 18f, Vector3.one, trolley);
            GoobyFreeStand(new Vector3(3.15f, 0f, -3.0f), -14f);
            Prop("bench", new Vector3(5.4f, 0f, 0.6f), 90f, 1.5f, 0.5f);
            Plant(new Vector3(6.3f, 0f, 2.0f));
            Prop("rug", new Vector3(0f, 0f, 0.9f), 0f, 3.2f, 1.6f, 1f);
        }

        /// <summary>
        /// GOOBY FREE (flughafen.gd _baue_gfree_stand): the duty-free at the gate — a blue counter with a gold stripe, a
        /// cream shelf with the five exclusive pieces on it (the real catalog models, at the scale FitProp gives them in a
        /// room), a cookie jar and boxed sandwiches on the counter, and the night-blue board with gold letters on top.
        /// </summary>
        private void GoobyFreeStand(Vector3 at, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            GameObject B(string name, Vector3 local, Vector3 size, Material m, bool shadows = true)
            {
                var go = Box(name, at + rot * local, size, m, shadows);
                go.transform.rotation = rot;
                return go;
            }
            var blue = ArtLibrary.Pbr("plaster", 1.4f, ArtLibrary.Hex("#5B86C4"), 0.2f, 0.5f);
            var cream = ArtLibrary.Pbr("wood_planks", 1.2f, ArtLibrary.Hex("#F3E9D8"), 0.25f, 0.5f);
            var gold = Solid("#F2C14E", 0.6f);
            var navy = Solid("#25356B", 0.3f);
            B("GfreeCounter", new Vector3(0f, 0.45f, 0.4f), new Vector3(1.7f, 0.9f, 0.6f), blue);
            B("GfreeCounterTop", new Vector3(0f, 0.925f, 0.4f), new Vector3(1.8f, 0.05f, 0.7f), cream);
            B("GfreeStripe", new Vector3(0f, 0.7f, 0.705f), new Vector3(1.7f, 0.07f, 0.01f), gold, false);
            B("GfreeBack", new Vector3(0f, 1.2f, -0.24f), new Vector3(1.8f, 2.4f, 0.06f), cream);
            foreach (var s in new[] { -1f, 1f })
                B("GfreePost", new Vector3(s * 0.88f, 1.2f, -0.04f), new Vector3(0.06f, 2.4f, 0.42f), blue);
            foreach (var y in new[] { 1.3f, 1.86f })
            {
                B("GfreePlank", new Vector3(0f, y, -0.04f), new Vector3(1.72f, 0.05f, 0.42f), cream);
                B("GfreeEdge", new Vector3(0f, y, 0.175f), new Vector3(1.72f, 0.06f, 0.012f), gold, false);
            }
            B("GfreeBoard", new Vector3(0f, 2.62f, -0.18f), new Vector3(1.86f, 0.5f, 0.06f), navy);
            ArtLibrary.Quad(_t, "GfreeSign", Origin + at + rot * new Vector3(0f, 2.62f, -0.145f), rot, new Vector2(1.8f, 0.464f), ArtLibrary.Decal("sign_gfree"));
            // 2×1 pieces: FitProp's (1.0, 0.75, 0.5) per footprint, 1×1 pieces (0.5, 0.5, 0.5) — here at shelf size
            Prop("gfree_shuttle", at + rot * new Vector3(-0.42f, 1.325f, -0.02f), yaw, 0.84f, 0.42f, 0.63f);
            Prop("gfree_flitzer", at + rot * new Vector3(0.42f, 1.325f, -0.02f), yaw, 0.8f, 0.4f, 0.6f);
            Prop("gfree_meteorit", at + rot * new Vector3(-0.54f, 1.885f, -0.02f), yaw, 0.46f, 0.46f, 0.46f);
            Prop("gfree_mondstein", at + rot * new Vector3(0.0f, 1.885f, -0.02f), yaw, 0.46f, 0.46f, 0.46f);
            Prop("gfree_sternmoehre", at + rot * new Vector3(0.54f, 1.885f, -0.02f), yaw, 0.44f, 0.44f, 0.44f);
            Cyl("GfreeJar", at + rot * new Vector3(-0.5f, 1.06f, 0.4f), 0.11f, 0.22f, ArtLibrary.Transparent(new Color(0.85f, 0.93f, 1f, 0.35f)));
            Cyl("GfreeJarLid", at + rot * new Vector3(-0.5f, 1.185f, 0.4f), 0.115f, 0.03f, gold);
            var cookie = Solid("#C98E52", 0.2f);
            for (var i = 0; i < 5; i++)
            {
                var c = Cyl("GfreeCookie", at + rot * new Vector3(-0.5f + (i % 2 == 0 ? -0.03f : 0.035f), 0.97f + i * 0.035f, 0.4f + (i % 3 - 1) * 0.03f), 0.06f, 0.025f, cookie);
                c.transform.rotation = rot * Quaternion.Euler(8f * (i % 3 - 1), 0f, 10f * (i % 2 == 0 ? 1f : -1f));
            }
            var bread = Solid("#F2D9A8", 0.2f);
            var filling = Solid("#7FCF9B", 0.25f);
            for (var i = 0; i < 2; i++)
            {
                var p = new Vector3(0.18f + i * 0.3f, 0.99f, 0.42f);
                B("GfreeSandwich", p, new Vector3(0.22f, 0.07f, 0.14f), bread);
                B("GfreeFilling", p + new Vector3(0f, 0f, 0.005f), new Vector3(0.23f, 0.018f, 0.145f), filling, false);
                B("GfreeTag", p + new Vector3(0f, 0.06f, 0.075f), new Vector3(0.08f, 0.05f, 0.006f), gold, false);
            }
        }

        /// <summary>
        /// GOOB-1 (raumstation.gd _baue_raum): metal hull, panorama window, Earth, two arcade terminals, Astro-Snack
        /// automaton and a glowing photo-spot ring. No shop shell — the station is not a pastel laden.
        /// </summary>
        private void BuildStation()
        {
            var metal = ArtLibrary.Pbr("concrete", 2.2f, ArtLibrary.Hex("515868"), 0.4f, 0.45f);
            Box("Metallboden", new Vector3(0f, -0.1f, 0f), new Vector3(14f, 0.2f, 10f), metal, false);
            var hull = ArtLibrary.Pbr("plaster", 2.0f, ArtLibrary.Hex("C7CCD9"), 0.12f, 0.3f);
            Box("Hull", new Vector3(-4.5f, 2.5f, -4.0f), new Vector3(5f, 5f, 0.3f), hull);
            Box("Hull", new Vector3(4.5f, 2.5f, -4.0f), new Vector3(5f, 5f, 0.3f), hull);
            Box("Hull", new Vector3(0f, 4.1f, -4.0f), new Vector3(4f, 1.8f, 0.3f), hull);
            Box("Hull", new Vector3(0f, 0.5f, -4.0f), new Vector3(4f, 1.0f, 0.3f), hull);
            foreach (var s in new[] { -1f, 1f })
                Box("HullSide", new Vector3(s * 7.0f, 2.5f, 0.5f), new Vector3(0.3f, 5f, 10f), hull);
            Box("HullCeiling", new Vector3(0f, 5.05f, 0.5f), new Vector3(14.4f, 0.12f, 10.4f), Solid("#9AA3B5", 0.2f), false);
            var frame = Solid("#F2F4FF", 0.25f);
            Box("WindowFrame", new Vector3(-2.05f, 2.1f, -3.95f), new Vector3(0.14f, 2.4f, 0.34f), frame);
            Box("WindowFrame", new Vector3(2.05f, 2.1f, -3.95f), new Vector3(0.14f, 2.4f, 0.34f), frame);
            Box("WindowFrame", new Vector3(0f, 3.25f, -3.95f), new Vector3(4.2f, 0.14f, 0.34f), frame);
            Box("WindowFrame", new Vector3(0f, 0.95f, -3.95f), new Vector3(4.2f, 0.14f, 0.34f), frame);
            Box("WindowGlass", new Vector3(0f, 2.1f, -4.02f), new Vector3(3.9f, 2.2f, 0.04f), ArtLibrary.Transparent(new Color(0.55f, 0.7f, 0.95f, 0.18f)), false);

            var space = ArtLibrary.Glow(new Color(0.02f, 0.03f, 0.08f));
            ArtLibrary.Quad(_t, "Starfield", Origin + new Vector3(0f, 2.6f, -18f), Quaternion.identity, new Vector2(40f, 18f), space);
            var star = ArtLibrary.Glow(new Color(1.6f, 1.7f, 2.2f));
            for (var i = 0; i < 48; i++)
            {
                var a = i * 0.73f;
                var x = Mathf.Sin(a) * (4.5f + (i % 5) * 0.7f);
                var y = 1.2f + (i % 7) * 0.45f;
                var z = -8.5f - (i % 4) * 1.1f;
                Sphere("Star", new Vector3(x, y, z), 0.04f + (i % 3) * 0.015f, star);
            }
            var earthMat = ArtLibrary.Glow(new Color(0.35f, 0.62f, 0.95f));
            var earth = Sphere("Erde", new Vector3(1.2f, 2.6f, -11f), 1.6f, earthMat);
            earth.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Box("Continent", new Vector3(1.55f, 2.85f, -9.55f), new Vector3(0.7f, 0.45f, 0.08f), ArtLibrary.Glow(new Color(0.35f, 0.7f, 0.4f)), false);

            StationTerminal("TerminalRocketRescue", new Vector3(-3.4f, 0f, -3.2f), 18f, ArtLibrary.Hex("F2784B"));
            StationTerminal("TerminalStarHopper", new Vector3(3.4f, 0f, -3.2f), -18f, ArtLibrary.Hex("7FD1F0"));
            StationAutomat(new Vector3(5.2f, 0f, -1.6f), -35f);
            for (var i = 0; i < 16; i++)
            {
                var a = i * Mathf.PI * 2f / 16f;
                Box("FotoSpot", new Vector3(-1.4f + Mathf.Cos(a) * 0.39f, 0.03f, -2.6f + Mathf.Sin(a) * 0.39f),
                    new Vector3(0.1f, 0.04f, 0.1f), ArtLibrary.Glow(new Color(2.2f, 1.8f, 0.5f)), false);
            }
            var lamp = new GameObject("CabinLight").AddComponent<Light>();
            lamp.transform.SetParent(_t, false);
            lamp.transform.position = Origin + new Vector3(0f, 3.2f, -0.6f);
            lamp.type = LightType.Point;
            lamp.color = ArtLibrary.Hex("FFF2DB");
            lamp.intensity = 1.15f;
            lamp.range = 12f;
            lamp.shadows = LightShadows.None;
            Debug.Log("[interior] station goob-1 hull earth terminals");
        }

        private void StationTerminal(string name, Vector3 pos, float yaw, Color accent)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_t, false);
            root.position = Origin + pos;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            var body = ArtLibrary.Box(root, "Korpus", root.position + Vector3.up * 0.85f, new Vector3(0.95f, 1.7f, 0.65f), Solid("#292C38", 0.35f));
            body.transform.SetParent(root, true);
            var cap = ArtLibrary.Box(root, "Blende", root.position + Vector3.up * 1.81f, new Vector3(0.95f, 0.22f, 0.7f), ArtLibrary.Glow(accent * 1.4f), false);
            cap.transform.SetParent(root, true);
            ArtLibrary.Quad(root, "Screen", root.position + new Vector3(0f, 1.25f, 0.34f), Quaternion.Euler(-8f, yaw, 0f), new Vector2(0.72f, 0.54f), ArtLibrary.Glow(new Color(0.55f, 0.7f, 1.1f)));
        }

        private void StationAutomat(Vector3 pos, float yaw)
        {
            var root = new GameObject("AstroAutomat").transform;
            root.SetParent(_t, false);
            root.position = Origin + pos;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            var body = ArtLibrary.Box(root, "Korpus", root.position + Vector3.up * 0.95f, new Vector3(0.9f, 1.9f, 0.7f), Solid("#D94D52", 0.35f));
            body.transform.SetParent(root, true);
            ArtLibrary.Quad(root, "Fenster", root.position + new Vector3(0f, 1.25f, 0.36f), Quaternion.Euler(0f, yaw, 0f), new Vector2(0.6f, 1.0f), ArtLibrary.Transparent(new Color(0.75f, 0.88f, 0.95f, 0.85f)));
            Sphere("Moehre", pos + new Vector3(0f, 1.15f, 0.15f), 0.12f, Solid("#F2784B", 0.3f));
        }

        // ── the shopkeeper ─────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task SpawnNpcAsync(string id)
        {
            // behind a counter or stall the shopkeeper stands on a step so head and shoulders show over the top
            var spot = Place.Id == "raumstation" ? new Vector3(0.6f, 0f, -1.8f)
                : Place.Id == "wochenmarkt" ? new Vector3(0f, 0f, -3.05f)
                : Place.Id == "funkelpark" ? new Vector3(-3.4f, 0f, -0.9f)
                : new Vector3(0f, 0f, -1.95f);
            var step = Place.Id != "funkelpark" && Place.Id != "raumstation";
            if (step) Box("Step", spot + new Vector3(0f, 0.22f, 0f), new Vector3(1.4f, 0.44f, 1.0f), Solid("#D9B48A", 0.1f));
            var stand = spot + (step ? new Vector3(0f, 0.44f, 0f) : Vector3.zero);
            var avatar = await GoobyAvatar.CreateAsync(_t, Origin + stand);
            if (avatar == null || Root == null) return;
            avatar.transform.position = Origin + stand;
            avatar.transform.rotation = Quaternion.identity; // faces +Z: the door and the camera
            avatar.transform.localScale = Vector3.one * 1.42f;
            Npc = avatar;
            if (!Npcs.TryGetValue(id, out var look)) look = ("cream", null, null, null, null, "happy");
            var equipped = new Dictionary<string, string> { ["fell"] = look.fell };
            if (look.hut != null) equipped["hut"] = look.hut;
            if (look.brille != null) equipped["brille"] = look.brille;
            if (look.hals != null) equipped["hals"] = look.hals;
            if (look.ruecken != null) equipped["ruecken"] = look.ruecken;
            avatar.Wear(equipped);
            avatar.SetEmotion(look.emotion);
            avatar.Play("idle-loop");
        }
    }
}
