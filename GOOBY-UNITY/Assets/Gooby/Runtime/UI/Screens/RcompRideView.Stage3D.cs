using System;
using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The competitions in 3D (port of comp_lauf.gd + comp_arena.gd): the sand ring with its fence and entry gap, the
    /// grandstand full of cheering Goobys, corner flags, trees, hills and a farm skyline in the haze; the discipline's
    /// course (barrels, flag gates, jumps with falling poles, trail stations, dressage dots, the grass oval, the show
    /// stage); the player's horse in its coat with Gooby riding on the saddle, the glassy ghost of the best run, the race
    /// bots, the gold marker over the next target. The camera chases from behind like ranch_ride_controller.gd (5.4 m
    /// back, 2.7 m up, FOV with the tempo), so the floating stick steers exactly as in the painted view. Core
    /// coordinates (x, z) map to stage (x, −z): the left-handed stage then shows the same handedness as Godot.
    /// </summary>
    public sealed partial class RcompRideView
    {
        public const double StageW = 960.0, StageH = 540.0;
        private const float CamBack = 4.6f, CamHeight = 2.4f, RiderScale = 0.72f;
        private const int RiderNone = -1, RiderRiding = 1, RiderShow = 2, RiderIdle = 3;

        private static readonly Color Sky3 = new Color(0.6f, 0.78f, 0.95f), Haze3 = new Color(0.8f, 0.88f, 0.96f);
        private static readonly Color Holz3 = new Color(0.79f, 0.55f, 0.35f), HolzDunkel3 = new Color(0.55f, 0.38f, 0.24f);
        private static readonly Color Creme3 = new Color(0.93f, 0.9f, 0.82f), Rot3 = new Color(0.91f, 0.4f, 0.42f), Blau3 = new Color(0.36f, 0.55f, 0.78f);
        private static readonly Color Salbei3 = new Color(0.72f, 0.84f, 0.66f), Rose3 = new Color(0.93f, 0.56f, 0.64f);
        private static readonly Color GoldGlow = new Color(1.35f, 1.05f, 0.42f);
        private static readonly string[] JockeyFurs = { "snow", "fell_lavendel", "fell_himmel", "fell_zuckerwatte", "fell_kakao", "caramel", "fell_minze" };

        private MgStage3D _stage;
        private Transform _course, _decor, _skyline, _marker, _activeLine, _activeRing;
        private float _quietShadowsIn;
        private int _quietPasses;
        private RanchHorse3D _horse, _ghost;
        private GoobyAvatar _rider;
        private RcompLauf _stageLauf;
        private bool _stageReady, _camSnap = true, _wasInAir;
        private int _riderMode = RiderNone;
        private float _stageT, _dustT, _emoteLeft, _riderBackIn, _orbit;
        private double _jubelSeen;
        private readonly List<(RanchHorse3D horse, GoobyAvatar rider)> _botRiders = new List<(RanchHorse3D, GoobyAvatar)>();
        private readonly List<Transform> _barrels = new List<Transform>(), _poles = new List<Transform>();
        private readonly List<Renderer[]> _items = new List<Renderer[]>();
        private int[] _itemState = Array.Empty<int>();
        private readonly List<(Transform t, int figur, int punkt)> _dots = new List<(Transform, int, int)>();
        private readonly List<Transform> _figures = new List<Transform>();
        private RanchCrowd _stands;
        private readonly List<Transform> _pennants = new List<Transform>();
        private bool _fogOn;
        private FogMode _fogMode;
        private Color _fogColor;
        private float _fogDensity;

        /// <summary>True while the 3D arena replaces the painted one (a GPU is there).</summary>
        public bool Has3D => _stage != null;

        private static Vector3 Pt(V2 p, float y = 0f) => MgStage3D.At(p.X, y, -p.Y);
        private static Vector3 Pt(double x, double z, float y = 0f) => MgStage3D.At((float)x, y, -(float)z);
        private static Vector3 Dir(V2 d) => new Vector3(d.X, 0f, -d.Y);
        private static float YawOf(double heading) => -(float)(heading * Mathf.Rad2Deg);
        private static Vector3 Forward(double heading) => new Vector3(-(float)Math.Sin(heading), 0f, (float)Math.Cos(heading));
        private static Quaternion Along(V2 d) => Quaternion.LookRotation(Dir(d).sqrMagnitude > 1e-6f ? Dir(d).normalized : Vector3.forward, Vector3.up);

        // ── build ──

        private void BuildStage()
        {
            _stage = new MgStage3D(StageW, StageH, Sky3);
            _stage.Bind(_field);
            _stage.Camera.farClipPlane = 360f;
            _stage.Camera.nearClipPlane = 0.2f;
            _fogOn = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Haze3;
            RenderSettings.fogDensity = 0.0048f;
            BuildGround();
            BuildFence();
            BuildGrandstand();
            BuildCornerFlags();
            BuildSkyline();
            _horse = RanchHorse3D.Create(_stage.Root.transform, "Pferd", MgScenery.Hex(0xB98A5E), MgScenery.Hex(0x6E4B2E));
            _horse.transform.position = Pt(0.0, 14.0);
            _marker = _stage.Cone(Vector3.zero, 0.34f, 0.72f, ArtLibrary.Glow(new Color(1.2f, 0.92f, 0.36f))).transform;
            _marker.gameObject.SetActive(false);
            _activeLine = ArtLibrary.Box(_stage.Root.transform, "Ziellinie", Vector3.zero, Vector3.one, ArtLibrary.Glow(GoldGlow), false).transform;
            _activeLine.gameObject.SetActive(false);
            _activeRing = RanchDressing.Ring(_stage.Root.transform, "Zielring", 1.12f, 1.36f, ArtLibrary.Glow(GoldGlow));
            _activeRing.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(_host.Store.State["cosmetics"]));
            _ = SpawnRider(outfit);
            _quietShadowsIn = 0.3f;
            _quietPasses = 10;
            _stageReady = true;
        }

        private async System.Threading.Tasks.Task SpawnRider(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(Pt(1.6, 13.0), RiderScale, outfit);
            if (g == null || _stage == null) return;
            _rider = g;
            _riderMode = RiderNone;
            g.SetEmotion("happy");
        }

        private void BuildGround()
        {
            var s = _stage;
            s.Ground(0f, new Color(0.6f, 0.78f, 0.47f), "grass", 420f, 380f, 0f);
            const float x0 = RcompKurs.ArenaX, z0 = RcompKurs.ArenaZ, w = RcompKurs.ArenaW, t = RcompKurs.ArenaT;
            var cx = x0 + w * 0.5f;
            var cz = z0 + t * 0.5f;
            var root = new GameObject("Platz").transform;
            root.SetParent(s.Root.transform, false);
            var sand = ArtLibrary.Textured("tex_sand", new Vector2(w / 3.2f, t / 3.2f), new Color(0.98f, 0.93f, 0.85f), 0.04f);
            ArtLibrary.Box(root, "Sand", Pt(cx, cz, 0.02f), new Vector3(w, 0.04f, t), sand, false);
            ArtLibrary.Box(root, "SandRand", Pt(cx, cz, 0.012f), new Vector3(w + 1.2f, 0.024f, t + 1.2f), ArtLibrary.Solid(new Color(0.78f, 0.66f, 0.48f), 0.04f), false);
            var spur = ArtLibrary.Solid(new Color(0.84f, 0.73f, 0.56f), 0.03f);
            foreach (var seite in new[] { -1f, 1f })
            {
                ArtLibrary.Box(root, "Hufspur", Pt(cx, seite * (z0 + t - 0.75f), 0.043f), new Vector3(w - 1f, 0.006f, 1.3f), spur, false);
                ArtLibrary.Box(root, "Hufspur", Pt(seite * (x0 + w - 0.75f), cz, 0.043f), new Vector3(1.3f, 0.006f, t - 4f), spur, false);
            }
            s.MergeChildren(root);
        }

        /// <summary>comp_arena.gd baue_zaun: posts every 2 m, two cream rails, a 6 m entry gap in the south side.</summary>
        private void BuildFence()
        {
            var root = new GameObject("Zaun").transform;
            root.SetParent(_stage.Root.transform, false);
            const float x0 = RcompKurs.ArenaX, z0 = RcompKurs.ArenaZ, x1 = RcompKurs.ArenaX + RcompKurs.ArenaW, z1 = RcompKurs.ArenaZ + RcompKurs.ArenaT;
            var post = ArtLibrary.Solid(HolzDunkel3, 0.08f);
            var rail = ArtLibrary.Solid(Creme3, 0.2f);
            void Post(float x, float z) => ArtLibrary.Box(root, "Pfosten", Pt(x, z, 0.55f), new Vector3(0.14f, 1.1f, 0.14f), post);
            for (var x = x0; x <= x1 + 0.01f; x += 2f)
            {
                Post(x, z0);
                if (Mathf.Abs(x) > 3f) Post(x, z1);
            }
            for (var z = z0 + 2f; z <= z1 - 1.99f; z += 2f)
            {
                Post(x0, z);
                Post(x1, z);
            }
            foreach (var h in new[] { 0.45f, 0.9f })
            {
                ArtLibrary.Box(root, "Latte", Pt(0f, z0, h), new Vector3(x1 - x0, 0.09f, 0.06f), rail);
                ArtLibrary.Box(root, "Latte", Pt((x0 - 3f) * 0.5f, z1, h), new Vector3(-3f - x0, 0.09f, 0.06f), rail);
                ArtLibrary.Box(root, "Latte", Pt((x1 + 3f) * 0.5f, z1, h), new Vector3(x1 - 3f, 0.09f, 0.06f), rail);
                ArtLibrary.Box(root, "Latte", Pt(x0, 0f, h), new Vector3(0.06f, 0.09f, z1 - z0), rail);
                ArtLibrary.Box(root, "Latte", Pt(x1, 0f, h), new Vector3(0.06f, 0.09f, z1 - z0), rail);
            }
            // the entry gate: two tall posts and a red arch high above the chase camera (it starts barrel runs right here)
            foreach (var sx in new[] { -3.2f, 3.2f }) ArtLibrary.Box(root, "Torpfosten", Pt(sx, z1, 2.05f), new Vector3(0.24f, 4.1f, 0.24f), ArtLibrary.Solid(Holz3, 0.1f));
            ArtLibrary.Box(root, "Torbogen", Pt(0f, z1, 4.05f), new Vector3(6.8f, 0.4f, 0.2f), ArtLibrary.Solid(Rot3, 0.2f));
            _stage.MergeChildren(root);
            _stage.Glb(System.IO.Path.Combine(AssetLoader.Root, "ranch", "heuballen.glb"), Pt(-5.4f, z1 + 1.3f), 1.1f, 20f);
            _stage.Glb(System.IO.Path.Combine(AssetLoader.Root, "ranch", "heuballen.glb"), Pt(-6.5f, z1 + 2.2f), 1f, -10f);
            _stage.Glb(System.IO.Path.Combine(AssetLoader.Root, "ranch", "trog.glb"), Pt(5.6f, z1 + 1.6f), 1.1f, 90f);
        }

        /// <summary>comp_arena.gd baue_tribuene: three tiers, a slanted roof with bunting and a dozen Goobys on the benches.</summary>
        private void BuildGrandstand()
        {
            var basisZ = RcompKurs.ArenaZ - 4.5f;
            var root = new GameObject("Tribuene").transform;
            root.SetParent(_stage.Root.transform, false);
            for (var stufe = 0; stufe < 3; stufe++)
                ArtLibrary.Box(root, "Stufe", Pt(0f, basisZ - stufe * 1.15f, 0.3f + stufe * 0.55f), new Vector3(26f, 0.55f, 1.2f), ArtLibrary.Solid(stufe % 2 == 0 ? Holz3 : HolzDunkel3, 0.1f));
            ArtLibrary.Box(root, "Rueckwand", Pt(0f, basisZ - 3.1f, 1.7f), new Vector3(26.4f, 3.4f, 0.2f), ArtLibrary.Solid(new Color(0.96f, 0.92f, 0.84f), 0.1f));
            var roof = ArtLibrary.Box(root, "Dach", Pt(0f, basisZ - 1.2f, 3.55f), new Vector3(27f, 0.18f, 4.8f), ArtLibrary.Solid(Rot3, 0.25f));
            roof.transform.rotation = Quaternion.Euler(-7f, 0f, 0f);
            foreach (var x in new[] { -12.8f, -4.3f, 4.3f, 12.8f })
                ArtLibrary.Cylinder(root, "Stuetze", Pt(x, basisZ + 0.9f, 1.6f), 0.09f, 3.2f, ArtLibrary.Solid(Creme3, 0.2f));
            var farben = new[] { Rot3, Creme3, new Color(1f, 0.83f, 0.3f), new Color(0.37f, 0.66f, 0.63f) };
            for (var i = 0; i < 26; i++)
            {
                var flag = RanchDressing.MeshObject(root, "Wimpel", RanchDressing.PennantMesh(), ArtLibrary.Solid(farben[i % farben.Length], 0.2f)).transform;
                flag.position = Pt(-12.75f + i * 1.02f, basisZ + 1.15f, 3.12f);
                flag.localScale = new Vector3(0.5f, 0.55f, 1f);
                flag.localRotation = Quaternion.Euler(0f, 0f, -90f);
            }
            _stage.MergeChildren(root);
            // six fans always come; the farm's own stand brings its spectators along (6 / 12 more)
            var fans = Mathf.Clamp(6 + RanchBauEffekte.TribueneZuschauer(_host.Store.State), 6, 18);
            var seats = new List<Vector3>();
            for (var i = 0; i < fans; i++)
            {
                var reihe = i % 3;
                var platz = -11.5f + i / 3f * (23f / 6f) + (float)((i * 37 % 10) / 10.0) * 1.1f;
                seats.Add(Pt(platz, basisZ - reihe * 1.15f + 0.1f, 0.58f + reihe * 0.55f));
            }
            _stands = new RanchCrowd(_stage, seats, 180f);
            Debug.Log("[turnier] tribuene fans=" + fans);
        }

        private void BuildCornerFlags()
        {
            const float x0 = RcompKurs.ArenaX, z0 = RcompKurs.ArenaZ, x1 = RcompKurs.ArenaX + RcompKurs.ArenaW, z1 = RcompKurs.ArenaZ + RcompKurs.ArenaT;
            var mast = ArtLibrary.Solid(Creme3, 0.25f);
            var tuch = ArtLibrary.Solid(new Color(0.95f, 0.69f, 0.3f), 0.2f);
            var masts = new List<GameObject>();
            foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
            {
                masts.Add(ArtLibrary.Cylinder(_stage.Root.transform, "Fahnenmast", Pt(x, z, 2.1f), 0.08f, 4.2f, mast));
                masts.Add(RanchDressing.Blob(_stage.Root.transform, "Knauf", Pt(x, z, 4.25f), ArtLibrary.Solid(new Color(1f, 0.83f, 0.3f), 0.5f)));
                masts[masts.Count - 1].transform.localScale = Vector3.one * 0.24f;
                var pivot = new GameObject("Fahne").transform;
                pivot.SetParent(_stage.Root.transform, false);
                pivot.position = Pt(x, z, 4.05f);
                var r = RanchDressing.MeshObject(pivot, "Tuch", RanchDressing.PennantMesh(), tuch);
                r.transform.localScale = new Vector3(1.25f, 0.75f, 1f);
                _pennants.Add(pivot);
            }
            _stage.Merge(_stage.Root.transform, masts, "Fahnenmasten");
        }

        /// <summary>Trees in two rings, hills and a farm skyline (barn, silo, windmill, water tower) in the haze, clouds and a sun.</summary>
        private void BuildSkyline()
        {
            var s = _stage;
            RanchDressing.Horizon(s, Vector3.zero, 165f);
            _skyline = new GameObject("Hof").transform;
            _skyline.SetParent(s.Root.transform, false);
            void Farm(string model, float x, float z, float scale, float yaw) => RanchDressing.Farm(s, _skyline, model, Pt(x, z), scale, yaw);
            Farm("scheune", -112f, -42f, 2.2f, 70f);
            Farm("silo", -104f, -62f, 2.2f, 0f);
            Farm("windmuehle_turm", 118f, 30f, 2.3f, -100f);
            Farm("wasserturm", 104f, -78f, 2.1f, 30f);
            Farm("scheune", 36f, 108f, 2f, 180f);
            Farm("silo", 52f, 112f, 1.9f, 0f);
            var rng = new System.Random(17);
            for (var i = 0; i < 9; i++) Farm("zaun_holz_a", -60f + i * 14f + (float)rng.NextDouble() * 4f, 100f, 1.2f, 0f);
        }

        /// <summary>
        /// The far set dressing (trees, bushes, barns) throws no shadows: they sit beyond the shadow
        /// distance or in the haze anyway, and the cast pass was most of a software-rendered frame. Their GLBs arrive
        /// over the first seconds, so this runs a few times after a (re)build.
        /// </summary>
        private void QuietShadows()
        {
            RanchDressing.QuietShadows(_decor);
            RanchDressing.QuietShadows(_skyline);
        }

        /// <summary>Trees and bushes of the run (the painter's seeded rings, so both views stand in the same meadow).</summary>
        private void BuildDecor()
        {
            if (_decor != null) UnityEngine.Object.Destroy(_decor.gameObject);
            _decor = new GameObject("Deko").transform;
            _decor.SetParent(_stage.Root.transform, false);
            var kinds = new[] { "tree_round_big", "tree_round", "tree_poplar", "tree_big" };
            void Tree(V2 pos, float scale, int i)
            {
                if (OnTrack(pos, 7.5)) return;
                var t = _stage.PlaceScenery(kinds[i % kinds.Length], Pt(pos), i * 47f % 360f, scale);
                t.SetParent(_decor, true);
            }
            if (_treesFar != null) for (var i = 0; i < _treesFar.Length; i++) if (i % 3 != 2) Tree(_treesFar[i].pos, 2.1f * _treesFar[i].scale, i);
            if (_treesNear != null) for (var i = 0; i < _treesNear.Length; i++) Tree(_treesNear[i].pos, 2.2f * _treesNear[i].scale, i + 1);
            if (_bushes != null)
                for (var i = 0; i < _bushes.Length; i += 2)
                {
                    if (OnTrack(_bushes[i].pos, 5.5)) continue;
                    var t = _stage.PlaceScenery(i % 3 == 0 ? "bush_flowers" : "bush_big", Pt(_bushes[i].pos), i * 61f % 360f, 1.4f * _bushes[i].scale + 0.4f);
                    t.SetParent(_decor, true);
                }
            var rng = new System.Random(5);
            var kinds2 = new[] { "flower_redA", "flower_yellowA", "flower_purpleA" };
            for (var i = 0; i < 26; i++)
            {
                var a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var r = 36f + (float)rng.NextDouble() * 22f;
                var pos = new V2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.75f);
                if (RcompKurs.InArena(pos) || OnTrack(pos, 4.5)) continue;
                _stage.Prop(kinds2[i % kinds2.Length], Pt(pos), 0.85f, i * 33f).SetParent(_decor, true);
            }
        }

        /// <summary>Within <paramref name="margin"/> metres of the grass oval's centre line (the track is 6.8 m wide).</summary>
        private static bool OnTrack(V2 p, double margin)
        {
            var umfang = RcompKurs.RennenUmfang();
            for (var s = 0.0; s < umfang; s += 2.0)
                if ((RcompKurs.RennenPunkt(s) - p).Length < margin) return true;
            return false;
        }

        // ── course per discipline ──

        private void RebuildCourse(RcompLauf lauf)
        {
            _stageLauf = lauf;
            if (_course != null) UnityEngine.Object.Destroy(_course.gameObject);
            foreach (var (h, r) in _botRiders)
            {
                if (h != null) UnityEngine.Object.Destroy(h.gameObject);
                if (r != null) UnityEngine.Object.Destroy(r.gameObject);
            }
            _botRiders.Clear();
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost.gameObject);
            _ghost = null;
            _barrels.Clear();
            _poles.Clear();
            _items.Clear();
            _dots.Clear();
            _figures.Clear();
            _camSnap = true;
            _wasInAir = false;
            _quietShadowsIn = 0.3f;
            _quietPasses = 10;
            _course = new GameObject("Kurs").transform;
            _course.SetParent(_stage.Root.transform, false);
            if (lauf == null) return;
            BuildDecor();
            var fell = FellVon(lauf.Farbe);
            _horse.SetCoat(fell.hell, fell.dunkel);
            RanchPferdLook.Kleide(_horse, _host.Store.State, Ride is RanchTurnierGame turnier ? turnier.PferdId : null);
            switch (lauf.Richter)
            {
                case RcompRichterTonnen t: BuildBarrels(t); break;
                case RcompRichterGelaende g: BuildGates(g.Tore); break;
                case RcompRichterSpringen s: BuildJumps(s.Kurs); break;
                case RcompRichterTrail tr: BuildTrail(tr); break;
                case RcompRichterDressur d: BuildDressage(d); break;
                case RcompRichterSchau _: BuildShowStage(); break;
            }
            if (lauf.Disziplin == "rennen") BuildOval();
            for (var i = 0; i < lauf.Bots.Count; i++)
            {
                var bot = lauf.Bots[i];
                var bf = FellVon(bot.Farbe);
                var horse = RanchHorse3D.Create(_stage.Root.transform, "Bot" + i, bf.hell, bf.dunkel);
                horse.transform.position = Pt(bot.Pos);
                _botRiders.Add((horse, null));
                _ = SpawnBotRider(i, horse);
            }
            _itemState = new int[_items.Count];
            for (var i = 0; i < _itemState.Length; i++) _itemState[i] = -1;
            _riderMode = RiderNone;
        }

        private async System.Threading.Tasks.Task SpawnBotRider(int i, RanchHorse3D horse)
        {
            var g = await GoobyAvatar.CreateAsync(_stage.Root.transform, horse.transform.position);
            if (g == null) return;
            if (horse == null || _stage == null) { UnityEngine.Object.Destroy(g.gameObject); return; }
            g.transform.localScale = Vector3.one * RiderScale * 0.95f;
            g.Wear(new Dictionary<string, string> { ["fell"] = JockeyFurs[i % JockeyFurs.Length], ["hut"] = "cap" });
            g.Play("sit-loop", 0f);
            g.SetEmotion("happy");
            horse.Mount(g.transform);
            for (var k = 0; k < _botRiders.Count; k++)
                if (_botRiders[k].horse == horse) _botRiders[k] = (horse, g);
        }

        private void BuildBarrels(RcompRichterTonnen t)
        {
            var blau = ArtLibrary.Solid(Blau3, 0.35f);
            var band = ArtLibrary.Solid(Creme3, 0.3f);
            var deckel = ArtLibrary.Solid(new Color(0.27f, 0.44f, 0.66f), 0.3f);
            for (var i = 0; i < t.Tonnen.Length; i++)
            {
                var pivot = new GameObject("Tonne").transform;
                pivot.SetParent(_course, false);
                pivot.position = Pt(t.Tonnen[i]);
                ArtLibrary.Cylinder(pivot, "Fass", pivot.position + Vector3.up * 0.525f, 0.44f, 1.05f, blau);
                ArtLibrary.Cylinder(pivot, "Band", pivot.position + Vector3.up * 0.3f, 0.452f, 0.08f, band);
                ArtLibrary.Cylinder(pivot, "Band", pivot.position + Vector3.up * 0.76f, 0.452f, 0.08f, band);
                ArtLibrary.Cylinder(pivot, "Deckel", pivot.position + Vector3.up * 1.055f, 0.4f, 0.02f, deckel);
                _stage.MergeChildren(pivot, "Tonne");
                _barrels.Add(pivot);
            }
            var start = new List<GameObject> { ArtLibrary.Box(_course, "Startlinie", Pt(RcompKurs.TonnenStart, 0.05f), new Vector3(10f, 0.02f, 0.5f), ArtLibrary.Solid(Creme3, 0.2f), false) };
            foreach (var sx in new[] { -5.3f, 5.3f })
            {
                start.Add(ArtLibrary.Cylinder(_course, "Startpfosten", Pt(RcompKurs.TonnenStart + new V2(sx, 0f), 0.75f), 0.07f, 1.5f, ArtLibrary.Solid(Creme3, 0.3f)));
                start.Add(ArtLibrary.Sphere(_course, "Startknauf", Pt(RcompKurs.TonnenStart + new V2(sx, 0f), 1.55f), 0.13f, ArtLibrary.Solid(Rot3, 0.4f)));
            }
            _stage.Merge(_course, start, "Start");
        }

        /// <summary>Flag gates: two masts with pennants and a banner overhead; the colour tells next (gold), done (sage), missed (rose).</summary>
        private void BuildGates(List<RcompKurs.Tor> tore)
        {
            var halb = (float)RcompRichterGelaende.TorHalbbreite;
            var mast = ArtLibrary.Solid(Creme3, 0.25f);
            var masts = new List<GameObject>();
            for (var i = 0; i < tore.Count; i++)
            {
                var tor = tore[i];
                var a = tor.Pos + tor.Quer * halb;
                var b = tor.Pos - tor.Quer * halb;
                masts.Add(ArtLibrary.Cylinder(_course, "Mast", Pt(a, 1.25f), 0.08f, 2.5f, mast));
                masts.Add(ArtLibrary.Cylinder(_course, "Mast", Pt(b, 1.25f), 0.08f, 2.5f, mast));
                var banner = ArtLibrary.Box(_course, "Banner", Pt(tor.Pos, 2.32f), new Vector3(0.06f, 0.42f, halb * 2f), ArtLibrary.Solid(Rot3, 0.2f));
                banner.transform.rotation = Along(tor.Quer);
                var renderers = new List<Renderer> { banner.GetComponent<Renderer>() };
                foreach (var (fuss, seite) in new[] { (a, 1f), (b, -1f) })
                {
                    var pivot = new GameObject("Wimpel").transform;
                    pivot.SetParent(_course, false);
                    pivot.position = Pt(fuss, 2.05f);
                    pivot.rotation = Quaternion.LookRotation(Dir(tor.Quer * seite)) * Quaternion.Euler(0f, -90f, 0f);
                    var r = RanchDressing.MeshObject(pivot, "Tuch", RanchDressing.PennantMesh(), ArtLibrary.Solid(Rot3, 0.2f));
                    r.transform.localScale = new Vector3(0.9f, 0.62f, 1f);
                    renderers.Add(r.GetComponent<Renderer>());
                    _pennants.Add(pivot);
                }
                _items.Add(renderers.ToArray());
            }
            _stage.Merge(_course, masts, "Masten");
        }

        /// <summary>The show-jumping course: wing posts, a red-and-cream striped top pole that falls on a knock, a cream lower pole.</summary>
        private void BuildJumps(List<RcompKurs.Tor> kurs)
        {
            var holz = ArtLibrary.Solid(Holz3, 0.12f);
            var weiss = ArtLibrary.Solid(new Color(0.97f, 0.96f, 0.92f), 0.2f);
            var rot = ArtLibrary.Solid(Rot3, 0.3f);
            var creme = ArtLibrary.Solid(Creme3, 0.3f);
            var gruen = ArtLibrary.Solid(new Color(0.42f, 0.66f, 0.38f), 0.1f);
            var standing = new List<GameObject>();
            for (var i = 0; i < kurs.Count; i++)
            {
                var h = kurs[i];
                var root = new GameObject("Hindernis").transform;
                root.SetParent(_course, false);
                root.position = Pt(h.Pos);
                root.rotation = Along(h.Quer);
                foreach (var seite in new[] { -1f, 1f })
                {
                    standing.Add(Local(ArtLibrary.Box(root, "Pfosten", Vector3.zero, new Vector3(0.2f, 1.2f, 0.2f), holz), root, new Vector3(0f, 0.6f, seite * 1.9f)));
                    standing.Add(Local(ArtLibrary.Box(root, "Fluegel", Vector3.zero, new Vector3(0.08f, 0.9f, 0.55f), weiss), root, new Vector3(0f, 0.55f, seite * 2.25f)));
                    standing.Add(Local(ArtLibrary.Box(root, "Kasten", Vector3.zero, new Vector3(0.5f, 0.28f, 0.5f), gruen), root, new Vector3(0f, 0.14f, seite * 2.9f)));
                }
                standing.Add(Local(ArtLibrary.Cylinder(root, "Unterstange", Vector3.zero, 0.05f, 3.8f, creme), root, new Vector3(0f, 0.4f, 0f), Quaternion.Euler(90f, 0f, 0f)));
                var pole = new GameObject("Stange").transform;
                pole.SetParent(root, false);
                pole.localPosition = new Vector3(0f, 0.74f, 0f);
                var reds = new List<GameObject>();
                var creams = new List<GameObject>();
                for (var k = 0; k < 4; k++)
                {
                    var seg = ArtLibrary.Cylinder(pole, "Segment", Vector3.zero, 0.065f, 0.95f, k % 2 == 0 ? rot : creme);
                    Local(seg, pole, new Vector3(0f, 0f, -1.425f + k * 0.95f), Quaternion.Euler(90f, 0f, 0f));
                    (k % 2 == 0 ? reds : creams).Add(seg);
                }
                _stage.Merge(pole, creams, "Creme");
                _poles.Add(pole);
                _items.Add(MergeOrKeep(pole, reds, "Rot"));
            }
            _stage.Merge(_course, standing, "Staender");
        }

        private void BuildTrail(RcompRichterTrail tr)
        {
            var halb = (float)RcompKurs.TrailLaengeM * 0.5f;
            var props = new List<GameObject>();
            for (var i = 0; i < tr.Stationen.Length; i++)
            {
                var st = tr.Stationen[i];
                var entlang = new V2(Math.Cos(st.Winkel), Math.Sin(st.Winkel));
                var quer = RcompRichterTrail.Quer(st);
                var rot = Along(entlang);
                var rails = new List<GameObject>();
                foreach (var seite in new[] { -1.0, 1.0 })
                {
                    var rail = ArtLibrary.Box(_course, "Stange", Pt(st.Pos + quer * ((st.Breite + 0.35) * seite), 0.14f), new Vector3(0.16f, 0.22f, halb * 2f), ArtLibrary.Solid(Holz3, 0.12f));
                    rail.transform.rotation = rot;
                    rails.Add(rail);
                }
                switch (st.Id)
                {
                    case "plane":
                        var plane = ArtLibrary.Box(_course, "Plane", Pt(st.Pos, 0.05f), new Vector3((float)st.Breite * 1.8f, 0.02f, halb * 1.6f), ArtLibrary.Solid(new Color(0.48f, 0.7f, 1f), 0.7f), false);
                        plane.transform.rotation = rot;
                        props.Add(plane);
                        break;
                    case "bruecke":
                        for (var k = -3; k <= 3; k++)
                        {
                            var plank = ArtLibrary.Box(_course, "Planke", Pt(st.Pos + entlang * (k * 0.8), 0.1f + (3 - Math.Abs(k)) * 0.03f), new Vector3((float)st.Breite * 2f, 0.1f, 0.7f), ArtLibrary.Solid(k % 2 == 0 ? HolzDunkel3 : Holz3, 0.1f));
                            plank.transform.rotation = rot;
                            props.Add(plank);
                        }
                        break;
                    case "slalom":
                        for (var k = -1; k <= 1; k++)
                        {
                            props.Add(ArtLibrary.Cylinder(_course, "Slalom", Pt(st.Pos + entlang * (k * 1.8), 0.8f), 0.07f, 1.6f, ArtLibrary.Solid(Rot3, 0.3f)));
                            props.Add(ArtLibrary.Sphere(_course, "Kappe", Pt(st.Pos + entlang * (k * 1.8), 1.62f), 0.1f, ArtLibrary.Solid(Creme3, 0.3f)));
                        }
                        break;
                    case "kreisel":
                        RanchDressing.Ring(_course, "Kreisel", (float)st.Breite * 0.8f - 0.08f, (float)st.Breite * 0.8f + 0.08f, ArtLibrary.Solid(Creme3, 0.2f)).position = Pt(st.Pos, 0.05f);
                        break;
                }
                _items.Add(MergeOrKeep(_course, rails, "Stangen"));
            }
            _stage.Merge(_course, props, "Stationen");
        }

        /// <summary>Each figure as flat discs on the sand joined by a raked path; only the figure being ridden shows.</summary>
        private void BuildDressage(RcompRichterDressur d)
        {
            var disc = ArtLibrary.Solid(new Color(0.99f, 0.95f, 0.8f), 0.2f);
            var path = ArtLibrary.Solid(new Color(0.97f, 0.9f, 0.72f), 0.1f);
            _figures.Clear();
            for (var f = 0; f < d.Figuren.Count; f++)
            {
                var root = new GameObject("Figur" + f).transform;
                root.SetParent(_course, false);
                var punkte = d.Figuren[f].Punkte;
                var spur = new List<GameObject>();
                for (var i = 0; i < punkte.Count; i++)
                {
                    var dot = RanchDressing.Ring(root, "Punkt", 0f, 0.42f, disc);
                    dot.position = Pt(punkte[i], 0.05f);
                    _dots.Add((dot, f, i));
                    if (i + 1 >= punkte.Count) continue;
                    var seg = ArtLibrary.Box(root, "Spur", Pt((punkte[i] + punkte[i + 1]) * 0.5f, 0.045f), new Vector3(0.14f, 0.008f, (punkte[i + 1] - punkte[i]).Length), path, false);
                    seg.transform.rotation = Along(punkte[i + 1] - punkte[i]);
                    spur.Add(seg);
                }
                _stage.Merge(root, spur, "Spur");
                _figures.Add(root);
            }
            // the letters' boards around the ring (A K E H C M B F), plain white markers on short posts
            const float x0 = RcompKurs.ArenaX + 0.8f, z0 = RcompKurs.ArenaZ + 0.8f, x1 = RcompKurs.ArenaX + RcompKurs.ArenaW - 0.8f, z1 = RcompKurs.ArenaZ + RcompKurs.ArenaT - 0.8f;
            var boards = new List<GameObject>();
            foreach (var (x, z) in new[] { (0f, z1), (x0 + 6f, z1), (x1 - 6f, z1), (x0, 0f), (x1, 0f), (x0 + 6f, z0), (x1 - 6f, z0), (0f, z0) })
            {
                boards.Add(ArtLibrary.Box(_course, "Buchstabe", Pt(x, z, 0.45f), new Vector3(0.5f, 0.5f, 0.5f), ArtLibrary.Solid(new Color(0.98f, 0.97f, 0.94f), 0.2f)));
                boards.Add(ArtLibrary.Box(_course, "Streifen", Pt(x, z, 0.62f), new Vector3(0.52f, 0.1f, 0.52f), ArtLibrary.Solid(Rot3, 0.2f)));
            }
            _stage.Merge(_course, boards, "Buchstaben");
        }

        private void BuildOval()
        {
            var umfang = RcompKurs.RennenUmfang();
            var segmente = (int)(umfang / 4.0);
            var band = ArtLibrary.Solid(new Color(0.55f, 0.72f, 0.38f), 0.05f);
            var pfosten = ArtLibrary.Solid(Creme3, 0.3f);
            var oval = new GameObject("Oval").transform;
            oval.SetParent(_course, false);
            V2 prevA = default, prevB = default;
            for (var i = 0; i <= segmente; i++)
            {
                var s = umfang * i / segmente;
                var punkt = RcompKurs.RennenPunkt(s);
                var tangente = (RcompKurs.RennenPunkt(s + 0.8) - punkt).Normalized();
                var quer = new V2(-tangente.Y, tangente.X);
                var a = punkt + quer * 3.4;
                var b = punkt - quer * 3.4;
                if (i < segmente)
                {
                    var seg = ArtLibrary.Box(oval, "Bahn", Pt(punkt, 0.03f), new Vector3(6.2f, 0.03f, 4.6f), band, false);
                    seg.transform.rotation = Along(tangente);
                    ArtLibrary.Box(oval, "Pfosten", Pt(a, 0.45f), new Vector3(0.12f, 0.9f, 0.12f), pfosten);
                    ArtLibrary.Box(oval, "Pfosten", Pt(b, 0.45f), new Vector3(0.12f, 0.9f, 0.12f), pfosten);
                }
                if (i > 0)
                {
                    Rail(prevA, a);
                    Rail(prevB, b);
                }
                prevA = a;
                prevB = b;
            }
            ArtLibrary.Box(oval, "Startlinie", Pt(RcompKurs.RennenPunkt(0.0), 0.06f), new Vector3(0.5f, 0.02f, 6.4f), ArtLibrary.Solid(Creme3, 0.2f), false);
            _stage.MergeChildren(oval, "Oval");

            void Rail(V2 from, V2 to)
            {
                var mid = (from + to) * 0.5f;
                var rail = ArtLibrary.Box(oval, "Ralle", Pt(mid, 0.82f), new Vector3(0.06f, 0.06f, (to - from).Length), pfosten, false);
                rail.transform.rotation = Along(to - from);
            }
        }

        private void BuildShowStage()
        {
            ArtLibrary.Cylinder(_course, "Buehne", Pt(0.0, 0.0, 0.07f), 4.3f, 0.14f, ArtLibrary.Solid(new Color(0.97f, 0.9f, 0.75f), 0.2f));
            RanchDressing.Ring(_course, "Buehnenrand", 4.3f, 4.55f, ArtLibrary.Solid(Rot3, 0.25f)).position = Pt(0.0, 0.0, 0.075f);
            RanchDressing.Ring(_course, "Spot", 0f, 2.6f, ArtLibrary.Glow(new Color(1.08f, 1.02f, 0.86f))).position = Pt(0.0, 0.0, 0.15f);
        }

        // ── per frame ──

        private void TickStage(double dt)
        {
            if (_stage == null || !_stageReady) return;
            var fdt = Mathf.Min((float)dt, 0.1f);
            var lauf = Ride?.Lauf;
            if (!ReferenceEquals(lauf, _stageLauf)) RebuildCourse(lauf);
            _stageT += fdt;
            if (_quietPasses > 0 && (_quietShadowsIn -= fdt) <= 0f)
            {
                QuietShadows();
                _quietPasses--;
                _quietShadowsIn = 0.75f;
            }
            var c = lauf?.Controller;
            if (c != null) PoseRiding(c, fdt);
            else if (lauf != null) PoseShow(lauf);
            else PoseIdle(fdt);
            PoseGhost(lauf);
            PoseBots(lauf);
            UpdateCourse(lauf, fdt);
            UpdateMarker(lauf);
            UpdateCrowd(fdt);
            UpdateRiderMood(c, fdt);
            WavePennants();
            UpdateCamera(lauf, fdt);
            _camSnap = false;
        }

        private void WavePennants()
        {
            for (var i = 0; i < _pennants.Count; i++)
            {
                var p = _pennants[i];
                if (p == null || p.childCount == 0) continue;
                var cloth = p.GetChild(0);
                cloth.localRotation = Quaternion.Euler(0f, Mathf.Sin(_stageT * 3f + i * 0.9f) * 16f, 0f);
            }
        }

        /// <summary>Gooby in the saddle (riding), beside the horse waving to the judges (show), or next to it at the gate (menus).</summary>
        private void SetRiderMode(int mode)
        {
            if (_rider == null || _riderMode == mode) return;
            _riderMode = mode;
            if (mode == RiderRiding)
            {
                _rider.transform.localScale = Vector3.one * RiderScale;
                _horse.Mount(_rider.transform);
                _rider.Play("sit-loop", 0f);
                return;
            }
            _horse.Mount(null);
            var show = mode == RiderShow;
            _rider.transform.position = show ? Pt(1.6, -1.2) : Pt(0.2, 14.0);
            _rider.transform.rotation = Quaternion.Euler(0f, show ? 200f : 210f, 0f);
            _rider.transform.localScale = Vector3.one * (show ? 0.82f : 0.8f);
            _rider.Play(show ? "wave" : "idle-loop");
        }

        private void PoseRiding(RideController c, float dt)
        {
            SetRiderMode(RiderRiding);
            var t = _horse.transform;
            t.position = Pt(c.Pos, (float)c.Y);
            var lean = Effects.Reduced ? 0f : -(float)c.SteerSmoothed * Mathf.Clamp01((float)(c.Tempo / 8.0)) * 7f;
            t.rotation = Quaternion.Euler(0f, YawOf(c.Heading), lean);
            var ziel = RideFeel.Tempo.TryGetValue(c.Gait, out var z) ? z : 0.0;
            _horse.Hufe = c.InLuft ? null : c.Untergrund ?? "wiese";
            _horse.Gait(c.Gait, ziel > 0.01 ? (float)(c.Tempo / ziel) : 1f);
            if (c.InLuft && !_wasInAir) _horse.Action("sprung", 0f, 1.2f);
            if (!c.InLuft && _wasInAir && !Effects.Reduced) Effects.LandingRing(Pt(c.Pos, 0.05f), new Color(0.86f, 0.76f, 0.6f, 0.8f), 0.8f, 12);
            _wasInAir = c.InLuft;
            var anteil = (float)RideFeel.StaubAnteil(c.Gait);
            if (anteil > 0f && !c.InLuft && c.Active)
            {
                _dustT -= dt;
                if (_dustT <= 0f)
                {
                    _dustT = Mathf.Lerp(0.24f, 0.09f, anteil);
                    var back = -Forward(c.Heading);
                    var side = Vector3.Cross(Vector3.up, back).normalized * (UnityEngine.Random.value - 0.5f) * 0.7f;
                    var inArena = RcompKurs.InArena(c.Pos);
                    var colour = inArena ? new Color(0.88f, 0.78f, 0.62f, 0.75f) : new Color(0.78f, 0.82f, 0.62f, 0.55f);
                    Effects.Puff(t.position + back * 1.15f + side + Vector3.up * 0.15f, colour, inArena ? 3 : 2, 0.42f + anteil * 0.3f, 0.7f, 0.75f, back + Vector3.up * 0.6f, 0.5f);
                }
            }
        }

        private void PoseShow(RcompLauf lauf)
        {
            var t = _horse.transform;
            t.position = Pt(lauf.SchauPferdPos);
            var aktion = lauf.Richter is RcompRichterSchau s && s.Angesagt && !Effects.Reduced ? Mathf.Sin(_stageT * 8f) * 4f : 0f;
            t.rotation = Quaternion.Euler(0f, 180f + aktion, 0f);
            _horse.Gait("stand");
            SetRiderMode(RiderShow);
            if (_rider != null)
            {
                var toHorse = t.position - _rider.transform.position;
                toHorse.y = 0f;
                if (toHorse.sqrMagnitude > 0.01f)
                    _rider.transform.rotation = Quaternion.Slerp(_rider.transform.rotation, Quaternion.LookRotation(Vector3.Lerp(Vector3.back, toHorse.normalized, 0.35f)), 0.1f);
            }
        }

        private void PoseIdle(float dt)
        {
            var t = _horse.transform;
            t.position = Pt(-2.0, 14.5);
            t.rotation = Quaternion.Euler(0f, 160f, 0f);
            _horse.Gait("stand");
            SetRiderMode(RiderIdle);
        }

        private void PoseGhost(RcompLauf lauf)
        {
            var g = lauf?.GeistZustand();
            if (g == null)
            {
                if (_ghost != null) _ghost.gameObject.SetActive(false);
                return;
            }
            if (_ghost == null) _ghost = RanchHorse3D.CreateGhost(_stage.Root.transform, new Color(0.72f, 0.86f, 1f, 0.42f));
            _ghost.gameObject.SetActive(true);
            var z = g.Value;
            _ghost.transform.position = Pt(z.Pos, (float)z.Y);
            _ghost.transform.rotation = Quaternion.Euler(0f, YawOf(z.Heading), 0f);
            _ghost.Gait(z.Gangart ?? "galopp");
        }

        private void PoseBots(RcompLauf lauf)
        {
            if (lauf == null) return;
            for (var i = 0; i < _botRiders.Count && i < lauf.Bots.Count; i++)
            {
                var bot = lauf.Bots[i];
                var horse = _botRiders[i].horse;
                if (horse == null) continue;
                horse.transform.position = Pt(bot.Pos);
                horse.transform.rotation = Quaternion.Euler(0f, YawOf(bot.Heading), 0f);
                var fertig = lauf.Zeit >= bot.ZeitS;
                horse.Gait(fertig ? "schritt" : "galopp", fertig ? 1f : 1.05f);
            }
        }

        private void UpdateCourse(RcompLauf lauf, float dt)
        {
            if (lauf == null) return;
            var pulse = 1f + 0.12f * Mathf.Sin(_stageT * 5f);
            var line = false;
            switch (lauf.Richter)
            {
                case RcompRichterTonnen t:
                    for (var i = 0; i < _barrels.Count; i++)
                    {
                        var um = t.IstUmgeworfen(i);
                        var b = _barrels[i];
                        var target = um ? Quaternion.Euler(0f, i * 70f, 84f) : Quaternion.identity;
                        b.rotation = Quaternion.Slerp(b.rotation, target, 1f - Mathf.Exp(-10f * dt));
                        var p = b.position;
                        p.y = Mathf.Lerp(p.y, um ? MgStage3D.Origin.y + 0.44f : MgStage3D.Origin.y, 1f - Mathf.Exp(-10f * dt));
                        b.position = p;
                    }
                    if (t.Idx < t.Tonnen.Length)
                    {
                        _activeRing.gameObject.SetActive(true);
                        _activeRing.position = Pt(t.Tonnen[t.Idx], 0.06f);
                        _activeRing.localScale = new Vector3(pulse, 1f, pulse);
                    }
                    else _activeRing.gameObject.SetActive(false);
                    break;
                case RcompRichterGelaende g:
                    for (var i = 0; i < _items.Count && i < g.Tore.Count; i++)
                        SetItem(i, i < g.Idx ? (_verpasst.Contains(i) ? 3 : 2) : i == g.Idx ? 1 : 0, Rot3);
                    if (g.Idx < g.Tore.Count)
                    {
                        var tor = g.Tore[g.Idx];
                        ShowLine(tor.Pos, tor.Quer, (float)RcompRichterGelaende.TorHalbbreite * 2f, pulse);
                        line = true;
                    }
                    break;
                case RcompRichterSpringen s:
                    for (var i = 0; i < _items.Count && i < s.Kurs.Count; i++)
                    {
                        var ab = _abgeworfen.Contains(i);
                        SetItem(i, ab ? 3 : i < s.Idx ? 2 : i == s.Idx ? 1 : 0, Rot3);
                        var pole = _poles[i];
                        var lp = ab ? new Vector3(0.45f, 0.1f, 0.15f) : new Vector3(0f, 0.74f, 0f);
                        pole.localPosition = Vector3.Lerp(pole.localPosition, lp, 1f - Mathf.Exp(-9f * dt));
                        pole.localRotation = Quaternion.Slerp(pole.localRotation, ab ? Quaternion.Euler(8f, 0f, 18f) : Quaternion.identity, 1f - Mathf.Exp(-9f * dt));
                    }
                    if (s.Idx < s.Kurs.Count)
                    {
                        var h = s.Kurs[s.Idx];
                        var richtung = new V2(Math.Sin(h.Heading), Math.Cos(h.Heading)) * -1.0;
                        ShowLine(h.Pos - richtung * 1.2, h.Quer, 3.2f, pulse);
                        line = true;
                    }
                    break;
                case RcompRichterTrail tr:
                    for (var i = 0; i < _items.Count && i < tr.Stationen.Length; i++) SetItem(i, i < tr.Idx ? 2 : i == tr.Idx ? 1 : 0, Holz3);
                    break;
                case RcompRichterDressur d:
                    for (var f = 0; f < _figures.Count; f++)
                        if (_figures[f] != null && _figures[f].gameObject.activeSelf != (f == d.FigurIdx)) _figures[f].gameObject.SetActive(f == d.FigurIdx);
                    foreach (var (dot, f, i) in _dots)
                    {
                        if (dot == null || f != d.FigurIdx) continue;
                        var next = i == d.PunktIdx;
                        dot.localScale = next ? new Vector3(pulse * 1.15f, 1f, pulse * 1.15f) : Vector3.one;
                        var r = dot.GetComponent<Renderer>();
                        var want = next ? ArtLibrary.Glow(GoldGlow) : i < d.PunktIdx ? ArtLibrary.Solid(Salbei3, 0.3f) : ArtLibrary.Solid(new Color(0.99f, 0.95f, 0.8f), 0.2f);
                        if (r.sharedMaterial != want) r.sharedMaterial = want;
                    }
                    break;
            }
            if (!line) _activeLine.gameObject.SetActive(false);
        }

        private void ShowLine(V2 mitte, V2 quer, float laenge, float pulse)
        {
            _activeLine.gameObject.SetActive(true);
            _activeLine.position = Pt(mitte, 0.06f);
            _activeLine.rotation = Along(quer);
            _activeLine.localScale = new Vector3(0.26f * pulse, 0.02f, laenge);
        }

        /// <summary>0 upcoming (the item's own colour), 1 next (gold), 2 done (sage), 3 missed or knocked (rose).</summary>
        private void SetItem(int i, int state, Color baseColor)
        {
            if (i >= _itemState.Length || _itemState[i] == state) return;
            _itemState[i] = state;
            var mat = state == 1 ? ArtLibrary.Solid(new Color(1f, 0.82f, 0.3f), 0.35f) : state == 2 ? ArtLibrary.Solid(Salbei3, 0.25f) : state == 3 ? ArtLibrary.Solid(Rose3, 0.25f) : ArtLibrary.Solid(baseColor, 0.25f);
            foreach (var r in _items[i]) if (r != null) r.sharedMaterial = mat;
        }

        private void UpdateMarker(RcompLauf lauf)
        {
            var ziel = lauf?.Controller != null ? lauf.MarkerZiel() : null;
            _marker.gameObject.SetActive(ziel.HasValue);
            if (!ziel.HasValue) return;
            var bob = Effects.Reduced ? 0f : Mathf.Sin(_stageT * Mathf.PI * 1.6f) * 0.18f;
            _marker.position = Pt(ziel.Value, 2.2f + 0.72f + bob);
            _marker.rotation = Quaternion.Euler(180f, _stageT * 126f, 0f);
        }

        private void UpdateCrowd(float dt)
        {
            var jubelStart = _jubel > _jubelSeen + 0.5;
            _jubelSeen = _jubel;
            _stands?.Tick(dt, jubelStart, _jubel > 2.0);
        }

        private void UpdateRiderMood(RideController c, float dt)
        {
            if (_rider == null) return;
            if (_riderBackIn > 0f && (_riderBackIn -= dt) <= 0f && _riderMode == RiderRiding) _rider.Play("sit-loop", 0.2f);
            if (_emoteLeft > 0f)
            {
                _emoteLeft -= dt;
                return;
            }
            if (c == null) return;
            var mood = c.ErschoepftRest > 0.0 ? "dizzy" : c.Gait == "galopp" ? "ecstatic" : "happy";
            if (_rider.Emotion != mood) _rider.SetEmotion(mood);
        }

        /// <summary>The judge's events on the stage: Gooby cheers or flinches, sparkles at the barrel or the gate.</summary>
        private void StageEvent(RcompEvent e)
        {
            if (_stage == null || _rider == null) return;
            var lauf = Ride?.Lauf;
            switch (e.Typ)
            {
                case "perfekt": case "ziel": case "treffer": case "figur_ok": case "runde":
                    _rider.SetEmotion("ecstatic");
                    if (_riderMode == RiderRiding) { _rider.Play("celebrate", 0.15f); _riderBackIn = 1.3f; }
                    _emoteLeft = 1.6f;
                    if (!Effects.Reduced) Effects.Sparkle(_horse.transform.position + Vector3.up * 2.2f, 0.7f, e.Typ == "ziel" ? 40 : 24);
                    break;
                case "tonne_ok": case "tor_ok": case "hindernis_ok": case "aufgabe_ok": case "gangart_ok": case "windschatten":
                    _rider.SetEmotion("ecstatic");
                    _emoteLeft = 1.1f;
                    if (!Effects.Reduced) Effects.Sparkle(_horse.transform.position + Vector3.up * 2f, 0.5f, 14);
                    break;
                case "abwurf": case "verweigert": case "tor_verpasst": case "tonne_um": case "beruehrung": case "daneben": case "aufgabe_ausgelassen":
                    _rider.SetEmotion(e.Typ == "verweigert" ? "dizzy" : "scared");
                    _emoteLeft = 1.3f;
                    break;
            }
            if (e.Typ == "treffer" && lauf?.Richter is RcompRichterSchau)
            {
                var clip = e.Id == "verbeugen" || e.Id == "kompliment" ? "fressen-loop" : e.Id == "steigen" ? "sprung" : "kopfschuetteln";
                _horse.Action(clip, clip == "fressen-loop" ? 1.4f : 0f);
                _rider.Play("celebrate", 0.15f);
            }
        }

        private void UpdateCamera(RcompLauf lauf, float dt)
        {
            var cam = _stage.Camera.transform;
            var c = lauf?.Controller;
            if (c != null)
            {
                var ground = Pt(c.Pos, (float)c.Y * 0.5f);
                var fwd = Forward(c.Heading);
                var target = ground - fwd * CamBack + Vector3.up * CamHeight;
                var f = _camSnap ? 1f : (float)RideFeel.CamFollowFactor(dt);
                cam.position = Vector3.Lerp(cam.position, target, f);
                var look = ground + Vector3.up * 1.35f + fwd * 2.2f;
                cam.rotation = Quaternion.LookRotation(look - cam.position, Vector3.up);
                var fov = Mathf.Lerp(58f, 66f, Mathf.Clamp01((float)((c.Tempo - 4.2) / (8.5 - 4.2)))) * 0.92f;
                _stage.Camera.fieldOfView = _camSnap ? fov : Mathf.Lerp(_stage.Camera.fieldOfView, fov, 1f - Mathf.Exp(-3f * dt));
                return;
            }
            if (lauf != null)
            {
                cam.position = MgStage3D.At(0f, 3.6f, -9.5f);
                cam.rotation = Quaternion.LookRotation(MgStage3D.At(0f, 1.3f, 0f) - cam.position, Vector3.up);
                _stage.Camera.fieldOfView = 42f;
                return;
            }
            _orbit += dt * (Effects.Reduced ? 0f : 0.06f);
            var a = 3.6f + _orbit;
            cam.position = MgStage3D.At(Mathf.Sin(a) * 30f, 9f, Mathf.Cos(a) * 30f);
            cam.rotation = Quaternion.LookRotation(MgStage3D.At(0f, 1f, 0f) - cam.position, Vector3.up);
            _stage.Camera.fieldOfView = 48f;
        }

        // ── overlay: an arrow on the frame toward a target out of view ──

        private void PaintOverlay3D(Painter2D p, RcompLauf lauf)
        {
            if (!(lauf.MarkerZiel() is V2 ziel) || lauf.Controller == null) return;
            var cam = _stage.Camera;
            var world = Pt(ziel, 2.4f);
            var v = cam.WorldToViewportPoint(world);
            var px = new Vector2(v.x * W, (1f - v.y) * H);
            if (v.z > 0f && px.x > 30f && px.x < W - 30f && px.y > 60f && px.y < H - 30f) return;
            var local = cam.transform.InverseTransformPoint(world);
            var dir = new Vector2(local.x, -local.z);
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            var a = new Vector2(W * 0.5f, H * 0.62f);
            float x0 = 40f, x1 = W - 40f, y0 = 92f, y1 = H - 40f;
            var tx = dir.x > 1e-4f ? (x1 - a.x) / dir.x : dir.x < -1e-4f ? (x0 - a.x) / dir.x : float.MaxValue;
            var ty = dir.y > 1e-4f ? (y1 - a.y) / dir.y : dir.y < -1e-4f ? (y0 - a.y) / dir.y : float.MaxValue;
            var edge = a + dir * Mathf.Max(0f, Mathf.Min(tx, ty));
            var side = new Vector2(-dir.y, dir.x);
            var bob = Effects.Reduced ? 0f : Mathf.Sin(_stageT * 6f) * 3f;
            edge += dir * bob;
            P.Poly(p, new[] { edge + dir * 24f, edge - dir * 7f + side * 17f, edge - dir * 7f - side * 17f }, Outline);
            P.Poly(p, new[] { edge + dir * 19f, edge - dir * 4f + side * 12f, edge - dir * 4f - side * 12f }, Gold);
        }

        // ── helpers ──

        /// <summary>The parts as one merged renderer per material — or, if they could not be merged, their own renderers.</summary>
        private Renderer[] MergeOrKeep(Transform parent, List<GameObject> parts, string name)
        {
            var kept = new List<Renderer>();
            foreach (var go in parts) if (go != null && go.TryGetComponent<Renderer>(out var r)) kept.Add(r);
            var made = _stage.Merge(parent, parts, name);
            return made.Count > 0 ? made.ToArray() : kept.ToArray();
        }

        private static GameObject Local(GameObject go, Transform parent, Vector3 localPos, Quaternion? localRot = null)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            return go;
        }

        /// <summary>Ends the 3D stage: fog back as the world had it, the stage and its lease gone.</summary>
        private void DropStage()
        {
            if (_stage == null) return;
            RenderSettings.fog = _fogOn;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogDensity = _fogDensity;
            _stage.Destroy();
            _stage = null;
            _stageReady = false;
            _riderMode = RiderNone;
            _rider = null;
            _horse = null;
            _ghost = null;
            _botRiders.Clear();
            _stands = null;
            _pennants.Clear();
            _barrels.Clear();
            _poles.Clear();
            _items.Clear();
            _dots.Clear();
            _figures.Clear();
        }
    }
}
