using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The obstacle course in 3D (port of parcours_game.gd + parcours_deko.gd): a sand lane through the meadow between
    /// two fences, a grandstand of Gooby fans by the start, the blue start flag and the finish arch with bunting, the
    /// obstacles of the course — striped vertical, hedge with flowers, two-pole oxer, water pool — whose body tips
    /// over on a knock, a glowing takeoff ring at the ideal point that turns gold while a jump would clear. The horse
    /// trots or gallops with Gooby in the saddle and lifts its nose over the fences; the camera rides behind and to the
    /// side like Godot's (7.2 m back, 3.4 m up and out). Course metres run along stage x, Godot's z is mirrored.
    /// </summary>
    public sealed partial class RanchParcoursScreen
    {
        private const float CamBack = 7.2f, CamSide = 3.4f, CamUp = 3.4f, RiderScale = 0.72f;

        private World.MgStage3D _stage;
        private Transform _course, _takeoff, _decor;
        private RanchHorse3D _horse;
        private GoobyAvatar _rider;
        private RanchCrowd _stands;
        private JObject _builtKurs;
        private RanchParcoursGame.Hindernis _builtFirst;
        private readonly List<(Transform kipp, RanchParcoursGame.Hindernis h)> _obstacles = new List<(Transform, RanchParcoursGame.Hindernis)>();
        private readonly HashSet<RanchParcoursGame.Hindernis> _rated = new HashSet<RanchParcoursGame.Hindernis>();
        private float _stageT, _dustT, _emoteLeft, _riderBackIn, _cheer;
        private double _prevY;
        private bool _camSnap = true, _wasInAir, _cheerBig;
        private int _quietPasses;
        private float _quietIn;
        private bool _fogOn;
        private FogMode _fogMode;
        private Color _fogColor;
        private float _fogDensity;

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 P3(double x, float y = 0f, double z = 0.0) => World.MgStage3D.At((float)x, y, (float)z);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.6f, 0.78f, 0.95f));
            _stage.Bind(Field);
            _stage.Camera.farClipPlane = 420f;
            _stage.Camera.nearClipPlane = 0.2f;
            _fogOn = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.82f, 0.9f, 0.94f);
            RenderSettings.fogDensity = 0.0036f;
            _stage.Ground(0f, new Color(0.6f, 0.78f, 0.47f), "grass", 600f, 360f, 0f);
            RanchDressing.Horizon(_stage, new Vector3(170f, 0f, 0f), 250f, 29);
            var farm = new GameObject("Hof").transform;
            farm.SetParent(_stage.Root.transform, false);
            RanchDressing.Farm(_stage, farm, "scheune", P3(60, 0f, 64), 2.4f, 200f);
            RanchDressing.Farm(_stage, farm, "silo", P3(78, 0f, 70), 2.3f, 0f);
            RanchDressing.Farm(_stage, farm, "windmuehle_turm", P3(210, 0f, 82), 2.4f, 160f);
            RanchDressing.Farm(_stage, farm, "wasserturm", P3(-36, 0f, 40), 2.2f, 30f);
            _decor = farm;
            // the farm's own horse jumps once there is a farm (coat, look and gear), else the palomino of the arcade
            var state = Host.Store.State;
            var reitPferd = RanchHof.IstGekauft(state) ? RanchHof.ReitPferd(state) : null;
            var (fell, maehne) = reitPferd != null ? RanchPferdLook.Fell(RanchHof.Pferd(state, reitPferd), "palomino") : (MgScenery.Hex(0xD9A066), MgScenery.Hex(0x8A5A33));
            _horse = RanchHorse3D.Create(_stage.Root.transform, "Pferd", fell, maehne);
            RanchPferdLook.Kleide(_horse, state, reitPferd);
            _horse.Hufe = "sand";
            _horse.transform.position = P3(0.0);
            _horse.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _takeoff = RanchDressing.Ring(_stage.Root.transform, "Absprung", 0.42f, 0.66f, ArtLibrary.Glow(new Color(1.25f, 0.98f, 0.4f)));
            _takeoff.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnRider(outfit);
            _quietPasses = 10;
            _quietIn = 0.3f;
        }

        private async System.Threading.Tasks.Task SpawnRider(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(P3(0.0, 1.3f), RiderScale, outfit);
            if (g == null || _stage == null) return;
            _rider = g;
            _horse.Mount(g.transform);
            g.Play("sit-loop", 0f);
        }

        /// <summary>The lane, fences, grandstand, start, finish, obstacles and meadow of one course.</summary>
        private void BuildCourse(JObject kurs)
        {
            _builtKurs = kurs;
            _builtFirst = _game.Hindernisse.Count > 0 ? _game.Hindernisse[0] : null;
            if (_course != null) Object.Destroy(_course.gameObject);
            _stands?.Destroy();
            _stands = null;
            _obstacles.Clear();
            _rated.Clear();
            _camSnap = true;
            _course = new GameObject("Kurs").transform;
            _course.SetParent(_stage.Root.transform, false);
            if (kurs == null) return;
            var laenge = (float)RanchHerde.Num(kurs, "laenge_m", 200.0);
            var s = _stage;
            var statics = new List<GameObject>
            {
                ArtLibrary.Box(_course, "Bahn", P3(laenge * 0.5f, 0.02f), new Vector3(laenge + 20f, 0.04f, 3.4f), ArtLibrary.Textured("tex_sand", new Vector2((laenge + 20f) / 3.2f, 1.06f), new Color(0.98f, 0.93f, 0.85f), 0.04f), false),
                ArtLibrary.Box(_course, "Mittellinie", P3(laenge * 0.5f, 0.045f), new Vector3(laenge + 20f, 0.01f, 0.16f), ArtLibrary.Solid(new Color(0.93f, 0.85f, 0.66f), 0.05f), false),
            };
            // parcours_deko.gd _streckenzaun: posts every 3 m both sides, two cream rails
            var post = ArtLibrary.Solid(RanchDressing.HolzDunkel, 0.08f);
            var rail = ArtLibrary.Solid(RanchDressing.Creme, 0.2f);
            for (var x = -14f; x <= laenge + 22f; x += 3f)
                foreach (var z in new[] { -4.6f, 4.6f })
                    statics.Add(ArtLibrary.Box(_course, "Pfosten", P3(x, 0.52f, z), new Vector3(0.14f, 1.05f, 0.14f), post));
            foreach (var z in new[] { -4.6f, 4.6f })
                foreach (var h in new[] { 0.42f, 0.82f })
                    statics.Add(ArtLibrary.Box(_course, "Latte", P3(laenge * 0.5f + 4f, h, z), new Vector3(laenge + 36f, 0.08f, 0.06f), rail, false));
            // the grandstand by the start (behind the far fence), its masts and bunting over the lane
            var tx = laenge * 0.12f;
            for (var stufe = 0; stufe < 3; stufe++)
                statics.Add(ArtLibrary.Box(_course, "Stufe", P3(tx, 0.3f + stufe * 0.55f, 8.2f + stufe * 1.15f), new Vector3(9f, 0.55f, 1.15f), ArtLibrary.Solid(stufe % 2 == 0 ? RanchDressing.Holz : RanchDressing.HolzDunkel, 0.1f)));
            var roof = ArtLibrary.Box(_course, "Dach", P3(tx, 3f, 9.35f), new Vector3(9.6f, 0.16f, 4.2f), ArtLibrary.Solid(new Color(0.95f, 0.69f, 0.3f), 0.2f));
            roof.transform.rotation = Quaternion.Euler(-6f, 0f, 0f);
            statics.Add(roof);
            foreach (var px in new[] { -4.4f, 4.4f })
                statics.Add(ArtLibrary.Cylinder(_course, "Stuetze", P3(tx + px, 1.5f, 7.6f), 0.08f, 3f, rail));
            foreach (var z in new[] { -4.6f, 4.6f })
                statics.Add(ArtLibrary.Cylinder(_course, "Mast", P3(tx, 1.65f, z), 0.08f, 3.3f, rail));
            statics.AddRange(RanchDressing.Bunting(_course, P3(tx, 3.1f, -4.6f), P3(tx, 3.1f, 4.6f)));
            // start flag (blue) and the finish arch with chequered bunting
            statics.Add(ArtLibrary.Cylinder(_course, "Startmast", P3(0.0, 1.6f, -2.2f), 0.07f, 3.2f, rail));
            var flag = RanchDressing.MeshObject(_course, "Startfahne", RanchDressing.PennantMesh(), ArtLibrary.Solid(new Color(0.44f, 0.72f, 0.91f), 0.2f)).transform;
            flag.position = P3(0.0, 2.9f, -2.2f);
            flag.localScale = new Vector3(1.1f, 0.7f, 1f);
            statics.Add(flag.gameObject);
            foreach (var z in new[] { -2.2f, 2.2f })
                statics.Add(ArtLibrary.Cylinder(_course, "Zielpfosten", P3(laenge, 1.6f, z), 0.14f, 3.2f, ArtLibrary.Solid(new Color(0.93f, 0.55f, 0.5f), 0.2f)));
            statics.Add(ArtLibrary.Box(_course, "Ziellatte", P3(laenge, 3.2f, 0.0), new Vector3(0.16f, 0.16f, 4.8f), rail, false));
            statics.AddRange(RanchDressing.Bunting(_course, P3(laenge, 3.15f, -2.2f), P3(laenge, 3.15f, 2.2f), 0.3f, 0.6f));
            // the obstacles: a darker sand anchor, wing posts with pennants, and the body that tips on a knock
            foreach (var h in _game.Hindernisse) statics.AddRange(BuildObstacle(h));
            // meadow: darker grass patches, trees and bushes beyond the fences
            var rng = new System.Random(917);
            var patch = ArtLibrary.Solid(new Color(0.5f, 0.7f, 0.4f), 0.03f);
            for (var i = 0; i < 40; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var size = 0.8f + (float)rng.NextDouble() * 2.2f;
                var p = RanchDressing.Ring(_course, "Fleck", 0f, 0.5f, patch);
                p.position = P3(rng.NextDouble() * (laenge + 30.0) - 12.0, 0.012f, side * (5.6f + (float)rng.NextDouble() * 12f));
                p.localScale = new Vector3(size, 1f, size * (0.6f + (float)rng.NextDouble() * 0.5f));
                statics.Add(p.gameObject);
            }
            s.Merge(_course, statics, "Strecke");
            var trees = new[] { "tree_round_big", "tree_round", "tree_poplar", "tree_big" };
            for (var i = 0; i < 26; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var x = rng.NextDouble() * (laenge + 40.0) - 16.0;
                var z = side * (9f + (float)rng.NextDouble() * 9f);
                if (side > 0f && x > tx - 7.0 && x < tx + 7.0) continue;
                s.PlaceScenery(trees[i % trees.Length], P3(x, 0f, z), i * 41f % 360f, 1.6f + (float)rng.NextDouble() * 0.8f).SetParent(_course, true);
            }
            for (var i = 0; i < 18; i++)
            {
                var side = i % 3 == 0 ? 1f : -1f;
                s.PlaceScenery(i % 2 == 0 ? "bush_flowers" : "bush_big", P3(rng.NextDouble() * (laenge + 20.0) - 8.0, 0f, side * (5.6f + (float)rng.NextDouble() * 2.5f)), i * 53f % 360f, 1.1f).SetParent(_course, true);
            }
            var seats = new List<Vector3>();
            var seatRng = new System.Random(431);
            for (var i = 0; i < 9; i++)
            {
                var reihe = i % 3;
                seats.Add(P3(tx - 3.6f + i / 3 * 3.2f + (float)seatRng.NextDouble() * 1.2f, 0.58f + reihe * 0.55f, 8.1f + reihe * 1.15f));
            }
            _stands = new RanchCrowd(_stage, seats, 180f);
            _quietPasses = 10;
            _quietIn = 0.3f;
        }

        /// <summary>parcours_game.gd _hindernis_node: the static parts are returned for merging, the tipping body stays live.</summary>
        private List<GameObject> BuildObstacle(RanchParcoursGame.Hindernis h)
        {
            var statics = new List<GameObject>();
            var breite = (float)(RanchParcours.HindernisBreite.TryGetValue(h.Typ, out var b) ? b : 0.4);
            var x = (float)h.At;
            statics.Add(ArtLibrary.Box(_course, "Anker", P3(x, 0.046f), new Vector3(Mathf.Max(1f, breite + 0.5f), 0.01f, 3.6f), ArtLibrary.Solid(new Color(0.72f, 0.6f, 0.42f), 0.05f), false));
            foreach (var z in new[] { -1.7f, 1.7f })
            {
                statics.Add(ArtLibrary.Box(_course, "Staender", P3(x, 0.55f, z), new Vector3(0.18f, 1.1f, 0.18f), ArtLibrary.Solid(RanchDressing.Holz, 0.1f)));
                var w = RanchDressing.MeshObject(_course, "Wimpel", RanchDressing.PennantMesh(), ArtLibrary.Solid(new Color(1f, 0.83f, 0.3f), 0.2f)).transform;
                w.position = P3(x, 1.3f, z);
                w.rotation = Quaternion.Euler(0f, 0f, -90f);
                w.localScale = new Vector3(0.34f, 0.3f, 1f);
                statics.Add(w.gameObject);
            }
            var kipp = new GameObject("Kipp").transform;
            kipp.SetParent(_course, false);
            kipp.position = P3(x);
            var parts = new List<GameObject>();
            switch (h.Typ)
            {
                case "hecke":
                    parts.Add(ArtLibrary.Box(kipp, "Hecke", P3(x, 0.36f), new Vector3(breite, 0.72f, 3f), ArtLibrary.Solid(new Color(0.31f, 0.6f, 0.25f), 0.08f)));
                    for (var k = 0; k < 3; k++)
                    {
                        var kuppe = RanchDressing.Blob(kipp, "Kuppe", P3(x, 0.74f, -1.0 + k), ArtLibrary.Solid(new Color(0.37f, 0.68f, 0.3f), 0.08f));
                        kuppe.transform.localScale = Vector3.one * 0.62f;
                        parts.Add(kuppe);
                        var bluete = RanchDressing.Blob(kipp, "Bluete", P3(x - 0.1f, 0.98f, -0.8 + k * 0.85), ArtLibrary.Solid(new Color(1f, 0.56f, 0.69f), 0.3f));
                        bluete.transform.localScale = Vector3.one * 0.14f;
                        parts.Add(bluete);
                    }
                    break;
                case "wasser":
                    parts.Add(ArtLibrary.Box(kipp, "Becken", P3(x, 0.05f), new Vector3(breite, 0.1f, 3.2f), ArtLibrary.Solid(new Color(0.24f, 0.58f, 0.83f), 0.75f), false));
                    parts.Add(ArtLibrary.Box(kipp, "Glanz", P3(x + breite * 0.18f, 0.11f), new Vector3(breite * 0.5f, 0.02f, 2.9f), ArtLibrary.Solid(new Color(0.49f, 0.75f, 0.92f), 0.85f), false));
                    parts.Add(ArtLibrary.Box(kipp, "Kante", P3(x - breite * 0.5f, 0.3f), new Vector3(0.08f, 0.6f, 3.2f), ArtLibrary.Solid(RanchDressing.Creme, 0.2f)));
                    break;
                case "oxer":
                    parts.AddRange(StripedPole(kipp, x - breite * 0.5f, 0.62f, new Color(0.91f, 0.55f, 0.63f)));
                    parts.AddRange(StripedPole(kipp, x + breite * 0.5f, 0.78f, new Color(0.37f, 0.66f, 0.63f)));
                    break;
                default:
                    parts.AddRange(StripedPole(kipp, x, 0.7f, new Color(0.85f, 0.34f, 0.31f)));
                    break;
            }
            _stage.Merge(kipp, parts, "Koerper");
            _obstacles.Add((kipp, h));
            return statics;
        }

        /// <summary>A competition pole of five alternating cream/colour segments across the lane.</summary>
        private static List<GameObject> StripedPole(Transform parent, float x, float y, Color color)
        {
            var list = new List<GameObject>();
            const float seg = 3.4f / 5f;
            for (var i = 0; i < 5; i++)
                list.Add(ArtLibrary.Box(parent, "Segment", P3(x, y, -1.7f + (i + 0.5f) * seg), new Vector3(0.13f, 0.13f, seg), ArtLibrary.Solid(i % 2 == 0 ? RanchDressing.Creme : color, 0.3f)));
            return list;
        }

        // ── per frame ──

        private void RenderStage()
        {
            if (_stage == null) return;
            var g = _game;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            var kurs = g.Kurs ?? RanchParcours.KursById(g.Kurse, Mathf.Max(1, g.KursId));
            // every Open deals fresh obstacles (also a retry of the same course): rebuild when the first one changes
            var first = g.Hindernisse.Count > 0 ? g.Hindernisse[0] : null;
            if (!ReferenceEquals(kurs, _builtKurs) || !ReferenceEquals(first, _builtFirst)) BuildCourse(kurs);
            if (_quietPasses > 0 && (_quietIn -= dt) <= 0f)
            {
                RanchDressing.QuietShadows(_course);
                RanchDressing.QuietShadows(_decor);
                _quietPasses--;
                _quietIn = 0.75f;
            }
            var playing = g.Phase != RanchParcoursGame.PhaseSelect && g.Kurs != null;
            var x = playing ? g.X : 0.0;
            var y = playing ? g.JumpY : 0.0;
            var vy = dt > 0f ? (y - _prevY) / dt : 0.0;
            _prevY = y;
            var t = _horse.transform;
            t.position = P3(x, (float)y);
            var pitch = g.InLuft ? Mathf.Clamp((float)vy * 4.5f, -16f, 16f) : 0f;
            t.rotation = Quaternion.Euler(-pitch, 90f, 0f);
            var gait = !playing || g.Phase == RanchParcoursGame.PhaseWon ? "schritt" : g.Galopp ? "galopp" : "trab";
            if (!playing && g.Phase == RanchParcoursGame.PhaseSelect) gait = "stand";
            var ziel = RideFeel.Tempo.TryGetValue(gait, out var zt) ? zt : 0.0;
            _horse.Gait(gait, ziel > 0.01 && playing ? (float)(g.Tempo / ziel) : 1f);
            if (g.InLuft && !_wasInAir) _horse.Action("sprung", 0f, 1.2f);
            if (!g.InLuft && _wasInAir && !ReducedMotion) Effects.LandingRing(P3(x, 0.05f), new Color(0.86f, 0.76f, 0.6f, 0.8f), 0.8f, 12);
            _wasInAir = g.InLuft;
            if (playing && !g.InLuft && g.Tempo > 0.5)
            {
                _dustT -= dt;
                if (_dustT <= 0f)
                {
                    _dustT = g.Galopp ? 0.1f : 0.18f;
                    Effects.Puff(P3(x - 1.1, 0.15f, (Random.value - 0.5f) * 0.6f), new Color(0.88f, 0.78f, 0.62f, 0.75f), 3, g.Galopp ? 0.6f : 0.45f, 0.7f, 0.7f, new Vector3(-1f, 0.6f, 0f), 0.5f);
                }
            }
            UpdateTakeoff(g, x);
            UpdateObstacles(dt);
            UpdateRiderMood(g, dt);
            var cheer = _cheer > 0f;
            _stands?.Tick(dt, cheer, _cheerBig);
            _cheer = 0f;
            _cheerBig = false;
            UpdateCamera(g, x, dt);
            _camSnap = false;
        }

        private void UpdateTakeoff(RanchParcoursGame g, double x)
        {
            var next = g.Phase == RanchParcoursGame.PhasePlay ? g.NextObstacle() : null;
            if (next == null || next.Rating.HasValue || g.InLuft || g.IdealTakeoff(next) - x > 26.0)
            {
                _takeoff.gameObject.SetActive(false);
                return;
            }
            var ideal = g.IdealTakeoff(next);
            var window = System.Math.Abs(x - ideal) < RanchParcours.Base.PerfektM;
            _takeoff.gameObject.SetActive(true);
            _takeoff.position = P3(ideal, 0.07f);
            var pulse = window && !ReducedMotion ? 1.14f + 0.14f * Mathf.Sin(_stageT * 14f) : 1f;
            _takeoff.localScale = new Vector3(pulse * 1.3f, 1f, pulse * 1.3f);
            var r = _takeoff.GetComponent<Renderer>();
            var want = window ? ArtLibrary.Glow(new Color(1.25f, 0.98f, 0.4f)) : ArtLibrary.Solid(new Color(0.96f, 0.93f, 0.84f), 0.2f);
            if (r.sharedMaterial != want) r.sharedMaterial = want;
        }

        private void UpdateObstacles(float dt)
        {
            foreach (var (kipp, h) in _obstacles)
            {
                if (kipp == null) continue;
                // a knocked fence or hedge tips forward; a missed water jump splashes instead
                var target = h.Knocked && h.Typ != "wasser" ? Quaternion.Euler(0f, 0f, -24f) : Quaternion.identity;
                kipp.localRotation = Quaternion.Slerp(kipp.localRotation, target, 1f - Mathf.Exp(-10f * dt));
                if (!h.Rating.HasValue && !h.Knocked || !_rated.Add(h) || ReducedMotion) continue;
                if (h.Knocked && h.Typ == "wasser") Effects.Puff(P3(h.At, 0.3f), new Color(0.6f, 0.82f, 1f, 0.9f), 18, 0.32f, 2.6f, 0.7f, Vector3.up, 0.7f);
                else if (h.Rating.HasValue && h.Rating.Value.Qualitaet == "perfekt") Effects.Sparkle(P3(h.At, 1.4f), 0.6f, 22);
            }
        }

        private void UpdateRiderMood(RanchParcoursGame g, float dt)
        {
            if (_rider == null) return;
            if (_riderBackIn > 0f && (_riderBackIn -= dt) <= 0f) _rider.Play("sit-loop", 0.2f);
            if (_emoteLeft > 0f) { _emoteLeft -= dt; return; }
            var mood = g.Galopp ? "ecstatic" : "happy";
            if (_rider.Emotion != mood) _rider.SetEmotion(mood);
        }

        /// <summary>A rated jump on the stage: Gooby cheers (and the stands with him) or flinches at a knock.</summary>
        private void StageRated(string quality)
        {
            if (_rider == null) return;
            switch (quality)
            {
                case "perfekt":
                    _rider.SetEmotion("ecstatic");
                    _rider.Play("celebrate", 0.15f);
                    _riderBackIn = 1.2f;
                    _emoteLeft = 1.4f;
                    _cheer = 1f;
                    break;
                case "gut":
                    _rider.SetEmotion("happy");
                    _emoteLeft = 0.8f;
                    break;
                default:
                    _rider.SetEmotion("scared");
                    _emoteLeft = 1.2f;
                    break;
            }
        }

        /// <summary>Over the finish: Gooby dances, the whole grandstand is up.</summary>
        private void StageWon()
        {
            _cheer = 1f;
            _cheerBig = true;
            if (_rider == null) return;
            _rider.SetEmotion("ecstatic");
            _rider.Play("celebrate", 0.15f);
            _riderBackIn = 1.4f;
            _emoteLeft = 2.5f;
        }

        private void UpdateCamera(RanchParcoursGame g, double x, float dt)
        {
            var cam = _stage.Camera.transform;
            var target = P3(x - CamBack, CamUp, -CamSide);
            var f = _camSnap ? 1f : (float)RideFeel.CamFollowFactor(dt);
            cam.position = Vector3.Lerp(cam.position, target, f);
            cam.rotation = Quaternion.LookRotation(P3(x + 4.0, 1f) - cam.position, Vector3.up);
            var tempo = g.Phase == RanchParcoursGame.PhasePlay ? g.Tempo : 0.0;
            var fov = Mathf.Lerp(58f, 66f, Mathf.Clamp01((float)((tempo - 4.2) / (8.5 - 4.2)))) * 0.86f;
            _stage.Camera.fieldOfView = _camSnap ? fov : Mathf.Lerp(_stage.Camera.fieldOfView, fov, 1f - Mathf.Exp(-3f * dt));
        }

        /// <summary>A stage point → the play field's pixels (2D floats and bursts over the 3D view).</summary>
        private Vector2 StageToField(double x, float y) => _stage.ToField(P3(x, y), FieldW, FieldH);

        private void DropStage()
        {
            if (_stage == null) return;
            RenderSettings.fog = _fogOn;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogDensity = _fogDensity;
            _stands?.Destroy();
            _stands = null;
            _stage.Destroy();
            _stage = null;
            _horse = null;
            _rider = null;
            _course = null;
            _builtKurs = null;
            _builtFirst = null;
            _obstacles.Clear();
            _rated.Clear();
        }
    }
}
