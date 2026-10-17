using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The garden of board 05: a wide, shallow lawn (the 28×14 build grid) inside a palisade of chunky
    /// timber posts, the cottage right behind the north fence with stepping stones leading from its door onto the
    /// grass, the potting bench, crate and stump beside it, tulips and flowering bushes along the fence,
    /// string lights on dark posts down the east side, big round trees framing everything and the painted
    /// horizon behind. All dressing stays outside the fenced lawn so the grid remains the player's; the
    /// BuildSession renders their own items on top. City firs (`tree_big` / `tree_small`) stay on the avenue.
    /// </summary>
    public sealed partial class GardenScene
    {
        public GameObject Root { get; }
        public GameObject Grid { get; }
        public Vector3 Origin { get; }
        public int CellsX { get; }
        public int CellsY { get; }

        private const float Cell = 0.5f;
        private const float FenceInset = 0.35f;

        private float LawnW => CellsX * Cell;
        private float LawnD => CellsY * Cell;
        private Vector3 LawnCenter => Origin + new Vector3(LawnW * 0.5f, 0f, LawnD * 0.5f);
        private float West => Origin.x;
        private float East => Origin.x + LawnW;
        private float South => Origin.z;
        private float North => Origin.z + LawnD;

        public GardenScene(Transform parent, Vector3 origin, int cellsX, int cellsY)
        {
            Origin = origin;
            CellsX = cellsX;
            CellsY = cellsY;
            Root = new GameObject("Garden");
            Root.transform.SetParent(parent, false);
            var t = Root.transform;

            // the lawn is the pale, mown build area of board 05 (on screen ≈ #90AE64); the meadow around it stays a
            // deeper green. The tints are minty on purpose: the grass map is near-neutral grain and the warm outdoor
            // light + grade take roughly a third of the blue out of every green (see SceneDressing.OutdoorWarmth)
            var grass = ArtLibrary.PbrSlab("grass", new Vector2(LawnW + 0.8f, LawnD + 0.8f), 2.4f, ArtLibrary.Hex("#9CC49E"), 0.04f, 0.7f);
            _lawn = ArtLibrary.Box(t, "Lawn", LawnCenter + Vector3.down * 0.1f, new Vector3(LawnW + 0.8f, 0.2f, LawnD + 0.8f), grass, false).GetComponent<Renderer>();
            _lawnBuilt = grass;
            var meadow = ArtLibrary.PbrSlab("grass", new Vector2(140f, 140f), 2.4f, ArtLibrary.Hex("#7FB088"), 0.04f, 0.7f);
            ArtLibrary.Box(t, "Meadow", LawnCenter + Vector3.down * 0.12f, new Vector3(140f, 0.2f, 140f), meadow, false);

            BuildFence(t);
            BuildCottage(t);
            BuildPlanting(t);
            BuildStringLights(t);
            BuildLife(t);

            ArtLibrary.Horizon(t, "bg_town_horizon", LawnCenter + new Vector3(0f, -3f, 62f), 44f);

            // petals and leaves drift across the lawn by day (board 05's breeze); fireflies take over after dark
            var air = new Bounds(LawnCenter + Vector3.up * 1.6f, new Vector3(LawnW + 3f, 1.6f, LawnD + 3f));
            _petals = Effects.Petals(t, air);
            _fireflies = Effects.Fireflies(t, new Bounds(LawnCenter + Vector3.up * 0.9f, new Vector3(LawnW + 2f, 1.2f, LawnD + 2f)));
            _fireflies.gameObject.SetActive(false);

            Grid = BuildGrid(t);
        }

        private ParticleSystem _petals, _fireflies;
        private bool _wet;

        /// <summary>The weather box over the lawn (WeatherFx): centred on it, a few metres beyond the fence on every side.</summary>
        public Vector3 WeatherCenter => LawnCenter;
        public Vector3 WeatherExtents => new Vector3(LawnW * 0.5f + 4f, 1f, LawnD * 0.5f + 4f);

        /// <summary>Rain or snow: no petals in the breeze and no fireflies at night until it stops.</summary>
        public void SetWet(bool wet)
        {
            if (_wet == wet) return;
            _wet = wet;
            UpdateAmbientFx();
        }

        private void UpdateAmbientFx()
        {
            if (_petals != null) _petals.gameObject.SetActive(!_night && !_wet);
            if (_fireflies != null) _fireflies.gameObject.SetActive(_night && !_wet);
        }

        /// <summary>Half width of the cottage plus its corner; the north palisade only runs from the corners to here.</summary>
        private const float CottageHalfWidth = 4.6f;

        /// <summary>The cottage sits left of centre (board 05: house top-left, big trees and light posts top-right).</summary>
        private float CottageX => LawnCenter.x - 2.5f;

        /// <summary>Foot of the cottage door (door step, just behind the lawn's north edge) — the way back inside.</summary>
        public Vector3 DoorStep => new Vector3(CottageX, 0f, North + 0.9f);

        /// <summary>Palisade in 1.5 m runs just outside the lawn; the cottage itself closes the middle of the north side.</summary>
        private void BuildFence(Transform t)
        {
            var x0 = West - FenceInset; var x1 = East + FenceInset;
            var z0 = South - FenceInset; var z1 = North + FenceInset;
            var segs = Mathf.RoundToInt((x1 - x0) / 1.5f);
            var step = (x1 - x0) / segs;
            for (var i = 0; i < segs; i++)
            {
                var x = x0 + step * (i + 0.5f);
                var s = step / 1.5f;
                _builtFence.Add(Scenery.Place(t, "log_fence", new Vector3(x, 0f, z0), 0f, s));
            }
            // north: two short runs between the corners and the cottage walls
            NorthRun(t, x0, CottageX - CottageHalfWidth, z1);
            NorthRun(t, CottageX + CottageHalfWidth, x1, z1);
            var segsZ = Mathf.RoundToInt((z1 - z0) / 1.5f);
            var stepZ = (z1 - z0) / segsZ;
            for (var i = 0; i < segsZ; i++)
            {
                var z = z0 + stepZ * (i + 0.5f);
                var s = stepZ / 1.5f;
                _builtFence.Add(Scenery.Place(t, "log_fence", new Vector3(x0, 0f, z), 90f, s));
                _builtFence.Add(Scenery.Place(t, "log_fence", new Vector3(x1, 0f, z), 90f, s));
            }
        }

        private void NorthRun(Transform t, float xa, float xb, float z)
        {
            var run = xb - xa;
            if (run < 0.4f) return;
            var segs = Mathf.Max(1, Mathf.RoundToInt(run / 1.5f));
            var step = run / segs;
            for (var i = 0; i < segs; i++)
                _builtFence.Add(Scenery.Place(t, "log_fence", new Vector3(xa + step * (i + 0.5f), 0f, z), 0f, step / 1.5f));
        }

        /// <summary>
        /// Cottage facade one metre behind the lawn's north edge; the strip in between holds the door step,
        /// mailbox, potting bench, crate and stump of board 05, and the stepping stones cross it onto the grass.
        /// </summary>
        private void BuildCottage(Transform t)
        {
            var cx = CottageX;
            _cottage = Scenery.Place(t, "cottage", new Vector3(cx, 0f, North + 3.85f), 180f).transform;
            // board 05: a run of round stones from the cottage door to Gooby's patch in the lawn centre
            for (var i = 0; i < 8; i++)
                _builtStones.Add(Scenery.Place(t, "stepping_stone", new Vector3(cx + (i % 2 == 0 ? -0.16f : 0.16f), 0f, North + 0.55f - i * 0.62f), i * 70f, 0.85f));
            _builtMailbox = Scenery.Place(t, "mailbox_red", new Vector3(cx - 1.55f, 0f, North + 0.62f), 180f, 0.9f);
            Scenery.Place(t, "workbench", new Vector3(cx + 2.9f, 0f, North + 0.6f), 180f);
            // the Gieß-Runde's watering can on the strip right of the potting bench, spout towards the door (off the grid)
            Scenery.Place(t, "watering_can", new Vector3(cx + 3.95f, 0f, North + 0.42f), -20f, 1.1f);
            Scenery.Place(t, "garden_crate", new Vector3(cx - 3.55f, 0f, North + 0.62f), 8f);
            Scenery.Place(t, "tree_stump", new Vector3(cx - 2.55f, 0f, North + 0.5f), 0f);
        }

        private void BuildPlanting(Transform t)
        {
            var c = LawnCenter;
            var cx = CottageX;
            // flowering bushes: beside the cottage corners and outside the fence
            foreach (var (x, z, s) in new[] { (cx - CottageHalfWidth - 0.75f, North + 0.75f, 0.8f), (cx + CottageHalfWidth + 0.8f, North + 0.7f, 0.75f),
                                              (West - 1.1f, North + 0.9f, 0.9f), (East + 1.1f, North + 1.0f, 0.95f),
                                              (West - 1.0f, South - 0.9f, 0.9f), (East + 1.2f, South - 0.7f, 0.85f),
                                              (East + 1.0f, South + 1.6f, 0.7f), (West - 1.05f, c.z + 0.4f, 0.75f) })
                Scenery.Place(t, "bush_flowers", new Vector3(x, 0f, z), x * 31f, s);
            // tulips and flower clumps along the outside of the fence
            foreach (var (x, z) in new[] { (West - 0.85f, c.z - 1.6f), (West - 0.9f, North - 0.9f), (East + 0.9f, c.z - 0.3f) })
                Scenery.Place(t, "tulips", new Vector3(x, 0f, z), z * 23f, 1.15f);
            foreach (var (x, z) in new[] { (West - 0.8f, South + 1.1f), (West - 0.75f, c.z + 1.6f), (West - 1.6f, c.z - 0.6f),
                                           (East + 0.8f, North - 0.6f), (East + 0.75f, c.z - 1.8f), (East + 1.7f, c.z + 1.0f),
                                           (cx - 5.9f, North + 0.5f), (cx + 5.9f, North + 0.45f), (cx + 1.35f, North + 0.5f) })
                Scenery.Place(t, "flower_patch", new Vector3(x, 0f, z), x * 47f + z * 11f, 1.1f);
            // board 05: tulips and daisies along the INSIDE of the west palisade (the layout also plants some)
            var innerZ = new[] { South + 1.2f, South + 2.1f, South + 3.0f, c.z - 0.6f, c.z + 0.5f, c.z + 1.6f, North - 1.8f, North - 0.9f };
            for (var i = 0; i < innerZ.Length; i++)
            {
                var z = innerZ[i];
                var tex = i % 3 == 0 ? "plant_tulips_red" : i % 3 == 1 ? "plant_tulips_white" : "plant_daisies";
                ArtLibrary.Plant(t, tex, new Vector3(West + 0.55f, 0f, z), i % 3 == 2 ? 0.42f : 0.5f, i * 37f);
            }
            foreach (var (x, z) in new[] { (West + 1.15f, North - 0.55f), (West + 1.7f, North - 0.5f), (cx - 1.1f, North - 0.45f), (cx + 1.2f, North - 0.5f) })
                ArtLibrary.Plant(t, "plant_daisies", new Vector3(x, 0f, z), 0.4f, x * 19f);

            // board 05: slatted crate with the bunny badge inside the west palisade (not only by the cottage)
            Scenery.Place(t, "garden_crate", new Vector3(West + 1.35f, 0f, c.z + 0.15f), 18f, 0.95f);

            // a dove on the west palisade (by day; GardenScene.Leben)
            _dove = Scenery.Place(t, "dove", new Vector3(West - FenceInset, 0.9f, c.z + 0.9f), 120f);
            // big trees: two giants whose crowns lean over the side fences and dapple the lawn (board 05), a pair
            // flanking the cottage, two at the near corners and smaller ones further out
            foreach (var (x, z, s) in new[] { (West - 2.7f, c.z + 1.2f, 1.7f), (East + 3.0f, c.z - 0.4f, 1.75f),
                                              (West - 1.9f, North + 2.6f, 1.15f), (East + 2.2f, North + 2.9f, 1.25f),
                                              (West - 3.4f, South - 1.9f, 1.05f), (East + 3.6f, South - 1.1f, 1.1f),
                                              (cx + 8.2f, North + 4.6f, 1.35f), (c.x - 10.5f, North + 5.5f, 1.1f), (c.x + 10.8f, North + 5.0f, 1.15f) })
                Scenery.Place(t, "tree_round_big", new Vector3(x, 0f, z), x * 7f, s);
            foreach (var (x, z) in new[] { (West - 5.2f, c.z - 1.5f), (East + 5.4f, c.z + 2.5f), (c.x - 13f, North + 1.5f), (c.x + 13.5f, North + 2f) })
                Scenery.Place(t, "tree_round", new Vector3(x, 0f, z), x * 5f, 0.95f);
            // hedges: a row along the near side and clumps beside the cottage
            foreach (var (x, z) in new[] { (West - 3.6f, c.z - 2.2f), (East + 3.8f, North + 0.4f), (cx - 7.5f, North + 3.0f), (cx + 7.8f, North + 3.2f),
                                           (West + 1.8f, South - 1.25f), (c.x - 1.2f, South - 1.3f), (c.x + 2.4f, South - 1.25f), (East - 1.6f, South - 1.3f) })
                Scenery.Place(t, "hedge", new Vector3(x, 0f, z), 0f, 0.9f);
            foreach (var (x, z, s) in new[] { (West - 1.5f, South - 1.4f, 1.1f), (East + 1.7f, South - 1.5f, 1.0f), (West + 4.6f, South - 1.35f, 0.85f), (East - 4.4f, South - 1.4f, 0.9f) })
                Scenery.Place(t, "bush_big", new Vector3(x, 0f, z), x * 13f, s);
        }

        /// <summary>Two dark posts on the east fence with a sagging chain of warm bulbs, plus a run up to the cottage.</summary>
        private void BuildStringLights(Transform t)
        {
            var x = East + FenceInset + 0.25f;
            var a = new Vector3(x, 0f, South + 1.0f);
            var b = new Vector3(x, 0f, North - 0.4f);
            var cottageEave = new Vector3(CottageX + CottageHalfWidth - 0.3f, 2.85f, North + 1.15f);
            Scenery.Place(t, "light_post", a, -90f);
            Scenery.Place(t, "light_post", b, -90f);
            Chain(t, a + Vector3.up * 2.25f, b + Vector3.up * 2.25f, 11, 0.7f);
            Chain(t, b + Vector3.up * 2.25f, cottageEave, 7, 0.4f);
        }

        private readonly System.Collections.Generic.List<Vector3> _bulbs = new System.Collections.Generic.List<Vector3>();
        private GameObject _nightLights;
        private bool _night;

        private void Chain(Transform t, Vector3 from, Vector3 to, int bulbs, float sag)
        {
            var wire = ArtLibrary.Solid(ArtLibrary.Hex("#3F3A45"), 0.1f);
            var glow = ArtLibrary.Glow(new Color(2.4f, 2.0f, 1.1f));
            var prev = from;
            for (var i = 1; i <= bulbs + 1; i++)
            {
                var u = i / (float)(bulbs + 1);
                var p = Vector3.Lerp(from, to, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * sag;
                var seg = ArtLibrary.Cylinder(t, "Wire", (prev + p) * 0.5f, 0.012f, Vector3.Distance(prev, p), wire, false);
                seg.transform.rotation = Quaternion.FromToRotation(Vector3.up, p - prev);
                if (i <= bulbs)
                {
                    ArtLibrary.Sphere(t, "Bulb", p + Vector3.down * 0.1f, 0.11f, glow, false);
                    _bulbs.Add(p + Vector3.down * 0.1f);
                }
                prev = p;
            }
        }

        /// <summary>Night on the lawn: every third bulb of the string lights casts a warm pool (created on first use).</summary>
        private Transform _cottage;
        private readonly Dictionary<Material, Color> _builtColors = new Dictionary<Material, Color>();

        /// <summary>Cottage material (Blender name) → home.style.haus key: walls, roof, door, shutters.</summary>
        private static readonly (string material, string key)[] CottageParts =
        {
            ("hausweiss", "fassade"), ("dachterra", "dachFarbe"), ("tuer", "tuerFarbe"), ("fensterladenbraun", "fensterFarbe"),
        };

        /// <summary>
        /// "Gestalten" outside: the cottage's walls, roof, door and shutters in the chosen palette colours (null = built
        /// look). False while the GLB is still loading — call again.
        /// </summary>
        public bool ApplyHouseStyle(Dictionary<string, string> haus)
        {
            if (_cottage == null || _cottage.childCount == 0) return false;
            foreach (var r in _cottage.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.materials)
                {
                    var name = m.name.Replace(" (Instance)", "");
                    if (name.StartsWith("gb_", System.StringComparison.Ordinal)) name = name.Substring(3);
                    foreach (var (material, key) in CottageParts)
                    {
                        if (!name.StartsWith(material, System.StringComparison.Ordinal)) continue;
                        if (!_builtColors.ContainsKey(m)) _builtColors[m] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                        var c = haus != null && haus.TryGetValue(key, out var farbe) ? ArtLibrary.Hex(Gooby.Core.Home.HouseStyle.Palette[farbe]) : _builtColors[m];
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                        m.color = c;
                    }
                }
            return true;
        }

        public void SetNight(bool on)
        {
            if (_night == on && (_nightLights != null || !on)) return;
            _night = on;
            if (_nightLights == null && on)
            {
                _nightLights = new GameObject("NightLights");
                _nightLights.transform.SetParent(Root.transform, false);
                for (var i = 0; i < _bulbs.Count; i += 3)
                {
                    var l = new GameObject("BulbLight").AddComponent<Light>();
                    l.transform.SetParent(_nightLights.transform, false);
                    l.transform.position = _bulbs[i];
                    l.type = LightType.Point;
                    l.color = ArtLibrary.Hex("FFD9A0");
                    l.intensity = 1.6f;
                    l.range = 5f;
                    l.shadows = LightShadows.None;
                }
                var porch = new GameObject("PorchLight").AddComponent<Light>();
                porch.transform.SetParent(_nightLights.transform, false);
                porch.transform.position = new Vector3(CottageX, 2.4f, North + 0.6f);
                porch.type = LightType.Point;
                porch.color = ArtLibrary.Hex("FFE2B0");
                porch.intensity = 2.2f;
                porch.range = 7f;
                porch.shadows = LightShadows.None;
            }
            if (_nightLights != null) _nightLights.SetActive(on);
            ApplyHorizonTint();
            UpdateAmbientFx();
        }

        private float _overcast;

        /// <summary>Weather (0 sunny … 1 thunderstorm): the painted town on the horizon turns toward rain-cloud grey.</summary>
        public void SetOvercast(float overcast)
        {
            overcast = Mathf.Clamp01(overcast);
            if (Mathf.Abs(overcast - _overcast) < 0.01f) return;
            _overcast = overcast;
            ApplyHorizonTint();
        }

        private void ApplyHorizonTint()
        {
            var tint = _night ? ArtLibrary.Hex("5A648C") : Color.white;
            ArtLibrary.TintBackdrop("bg_town_horizon", Color.Lerp(tint, _night ? ArtLibrary.Hex("454B66") : ArtLibrary.Hex("A7AFBD"), _overcast * 0.85f));
        }

        public bool Night => _night;

        /// <summary>Hairline cells over the lawn (toggled by "Raster") and the brighter patch where Gooby stands (board 05).</summary>
        private GameObject BuildGrid(Transform t)
        {
            var grid = new GameObject("Grid");
            grid.transform.SetParent(t, false);
            var line = ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.52f));
            var patch = ArtLibrary.Transparent(new Color(0.42f, 0.94f, 0.16f, 0.86f));
            var mark = ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.88f));
            var flat = Quaternion.Euler(-90f, 0f, 0f);
            for (var x = 0; x <= CellsX; x++)
                ArtLibrary.Quad(grid.transform, "GX", Origin + new Vector3(x * Cell, 0.012f, LawnD * 0.5f), flat, new Vector2(0.014f, LawnD), line);
            for (var y = 0; y <= CellsY; y++)
                ArtLibrary.Quad(grid.transform, "GZ", Origin + new Vector3(LawnW * 0.5f, 0.012f, y * Cell), flat, new Vector2(LawnW, 0.014f), line);
            // 6×4-cell patch centred on Gooby's spot, with small crosses at its inner cell corners (build mode only)
            var pc = SimpleWorld.GardenGoobyPos;
            var size = new Vector2(6 * Cell, 4 * Cell);
            var patchGo = new GameObject("Patch");
            patchGo.transform.SetParent(grid.transform, false);
            ArtLibrary.Quad(patchGo.transform, "Area", new Vector3(pc.x, 0.018f, pc.z), flat, size, patch);
            for (var i = 1; i < 6; i++)
                for (var j = 1; j < 4; j++)
                {
                    var p = new Vector3(pc.x - size.x * 0.5f + i * Cell, 0.01f, pc.z - size.y * 0.5f + j * Cell);
                    ArtLibrary.Quad(patchGo.transform, "Mark", p, flat, new Vector2(0.09f, 0.014f), mark);
                    ArtLibrary.Quad(patchGo.transform, "Mark", p, flat, new Vector2(0.014f, 0.09f), mark);
                }
            return grid;
        }
    }
}
