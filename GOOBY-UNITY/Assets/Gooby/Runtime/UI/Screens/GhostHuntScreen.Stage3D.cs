using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D moonlit graveyard of the Geisterjagd (port of ghost_hunt_scenery.gd): rounded gravestones, glowing jack-o'-
    /// lanterns and the crypt stand on a violet-green lawn under a big moon and stars, with an iron fence, hills and two
    /// lantern posts; sheet ghosts rise out of their spots (bobbing, eyes blinking) and puff away when caught. A decoy
    /// pumpkin glows and jiggles while it flickers. Gooby stands in the front holding a warm lantern and reacts to every
    /// catch and fright. Spots lie under their 2D places (ground rays) and every ghost is solved onto the depth of its
    /// spot so it covers its 2D tap box exactly.
    /// </summary>
    public sealed partial class GhostHuntScreen
    {
        private World.MgStage3D _stage;
        private readonly Dictionary<int, Transform> _ghosts3d = new Dictionary<int, Transform>();
        private readonly List<Transform> _decoys3d = new List<Transform>();
        private readonly List<Vector3> _spotBase = new List<Vector3>();
        private readonly List<float> _spotMpp = new List<float>();
        private Material _ghostMat, _eyeMat, _stoneMat, _stoneDark, _pumpkinMat, _pumpkinGlow, _stemMat;
        private Light _lantern;
        private bool _laidOut;
        private float _emoteLeft, _hop;

        protected override bool StageHasGooby => _stage != null;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewSize.w, ViewSize.h, new Color(0.19f, 0.15f, 0.36f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 8.2f, -9f), 34f, 50f);
            _ghostMat = World.ArtLibrary.Solid(new Color(0.97f, 0.97f, 1f), 0.4f);
            _eyeMat = World.ArtLibrary.Solid(new Color(0.15f, 0.12f, 0.25f), 0.6f);
            _stoneMat = World.ArtLibrary.Solid(new Color(0.66f, 0.63f, 0.8f), 0.15f);
            _stoneDark = World.ArtLibrary.Solid(new Color(0.46f, 0.43f, 0.62f), 0.15f);
            _pumpkinMat = World.ArtLibrary.Solid(new Color(0.95f, 0.52f, 0.18f), 0.35f);
            _pumpkinGlow = World.ArtLibrary.Glow(new Color(1.5f, 1.05f, 0.35f));
            _stemMat = World.ArtLibrary.Solid(new Color(0.35f, 0.5f, 0.3f), 0.2f);
            _stage.Ground(0f, new Color(0.33f, 0.5f, 0.47f), "grass", 80f, 80f, 20f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnLanternGooby(outfit);
        }

        private async System.Threading.Tasks.Task SpawnLanternGooby(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(1.4f, 0f, -1f), 1.5f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            var lamp = new GameObject("Lantern").transform;
            lamp.SetParent(g.transform, false);
            lamp.localPosition = new Vector3(-0.36f, 0.48f, 0.3f);
            var frame = World.ArtLibrary.Solid(new Color(0.3f, 0.26f, 0.34f), 0.4f);
            void P(GameObject go, Vector3 local, Vector3 scale) { go.transform.SetParent(lamp, false); go.transform.localPosition = local; go.transform.localScale = scale; }
            P(World.ArtLibrary.Box(lamp, "Cage", Vector3.zero, Vector3.one, World.ArtLibrary.Transparent(new Color(1f, 0.9f, 0.6f, 0.35f)), false), Vector3.zero, new Vector3(0.16f, 0.2f, 0.16f));
            P(World.ArtLibrary.Sphere(lamp, "Flame", Vector3.zero, 0.5f, World.ArtLibrary.Glow(new Color(1.6f, 1.2f, 0.5f)), false), Vector3.zero, Vector3.one * 0.09f);
            P(World.ArtLibrary.Box(lamp, "Lid", Vector3.zero, Vector3.one, frame, false), new Vector3(0f, 0.12f, 0f), new Vector3(0.2f, 0.04f, 0.2f));
            P(World.ArtLibrary.Box(lamp, "Base", Vector3.zero, Vector3.one, frame, false), new Vector3(0f, -0.11f, 0f), new Vector3(0.2f, 0.03f, 0.2f));
            var lightGo = new GameObject("LanternLight");
            lightGo.transform.SetParent(lamp, false);
            _lantern = lightGo.AddComponent<Light>();
            _lantern.type = LightType.Point;
            _lantern.color = new Color(1f, 0.82f, 0.5f);
            _lantern.range = 5f;
            _lantern.intensity = 2.2f;
            _lantern.shadows = LightShadows.None;
        }

        private Vector3 FieldToView(Vector2 field) => new Vector3(field.x * (float)ViewSize.w / FieldW, field.y * (float)ViewSize.h / FieldH, 0f);

        /// <summary>The graveyard under the 2D layout: spots on their ground points, the backdrop beyond the far row.</summary>
        private void LayoutStage()
        {
            if (_stage == null || FieldW <= 0f || _laidOut) return;
            _laidOut = true;
            var root = _stage.Root.transform;
            for (var i = 0; i < GhostHunt.Spots.Length; i++)
            {
                var spot = GhostHunt.Spots[i];
                var v = FieldToView(SpotAt(i));
                var basePt = _stage.GroundPoint(v.x, v.y);
                var mpp = _stage.GroundMetres(v.x, v.y, 100.0) / 100f * (float)ViewSize.w / FieldW;
                _spotBase.Add(basePt);
                _spotMpp.Add(mpp);
                var sc = DepthScale(spot.Z);
                if (spot.Kind == "pumpkin") Pumpkin3D(basePt, 54f * mpp * sc, false);
                else if (spot.Kind == "crypt") Crypt(basePt, 120f * mpp * sc);
                else Gravestone(basePt, 46f * mpp * sc, 58f * mpp * sc, i);
            }
            for (var i = 0; i < GhostHunt.DecoySpots.Length; i++)
            {
                var (_, x, z) = GhostHunt.DecoySpots[i];
                var v = FieldToView(Project(x, z));
                var p = _stage.GroundPoint(v.x, v.y);
                var mpp = _stage.GroundMetres(v.x, v.y, 100.0) / 100f * (float)ViewSize.w / FieldW;
                var d = Pumpkin3D(p, 54f * mpp * DepthScale(z), true);
                d.gameObject.SetActive(false);
                _decoys3d.Add(d);
            }
            // beyond the far row: the iron fence, hills, the moon, stars and two lantern posts
            var far = _stage.GroundPoint(ViewSize.w * 0.5, ViewSize.h * 0.3);
            var fz = far.z - World.MgStage3D.Origin.z;
            var iron = World.ArtLibrary.Solid(new Color(0.32f, 0.27f, 0.48f), 0.3f);
            for (var x = -9f; x <= 9f; x += 0.55f)
            {
                World.ArtLibrary.Box(root, "Bar", World.MgStage3D.At(x, 0.75f, fz + 1.5f), new Vector3(0.07f, 1.5f, 0.07f), iron, false);
                _stage.Cone(World.MgStage3D.At(x, 1.5f, fz + 1.5f), 0.07f, 0.16f, iron);
            }
            World.ArtLibrary.Box(root, "Rail", World.MgStage3D.At(0f, 1.25f, fz + 1.5f), new Vector3(18f, 0.07f, 0.07f), iron, false);
            World.ArtLibrary.Box(root, "Rail2", World.MgStage3D.At(0f, 0.35f, fz + 1.5f), new Vector3(18f, 0.07f, 0.07f), iron, false);
            _stage.Hills(0f, fz + 9f, 6, new Color(0.3f, 0.25f, 0.5f), 4.5f, 40f, 0f, 31);
            _stage.Hills(0f, fz + 15f, 5, new Color(0.38f, 0.32f, 0.6f), 7f, 52f, 0f, 32);
            _stage.Sun(new Vector3(3.5f, 13.5f, fz + 30f), 2.4f, new Color(1.3f, 1.22f, 0.98f));
            var star = World.ArtLibrary.Glow(new Color(1.3f, 1.25f, 1f));
            var rng = new System.Random(5);
            for (var i = 0; i < 40; i++)
                World.ArtLibrary.Sphere(root, "Star", World.MgStage3D.At((float)(rng.NextDouble() - 0.5) * 34f, 9f + (float)rng.NextDouble() * 12f, fz + 34f), 0.06f + (float)rng.NextDouble() * 0.08f, star, false);
            foreach (var x in new[] { -2.9f, 2.9f })
            {
                var postZ = (_spotBase[0].z + _spotBase[8].z) * 0.5f - World.MgStage3D.Origin.z;
                World.ArtLibrary.Cylinder(root, "Post", World.MgStage3D.At(x, 1.1f, postZ), 0.06f, 2.2f, iron);
                World.ArtLibrary.Box(root, "Lamp", World.MgStage3D.At(x, 2.35f, postZ), new Vector3(0.3f, 0.36f, 0.3f), World.ArtLibrary.Glow(new Color(1.4f, 1.1f, 0.55f)), false);
            }
            // a few tufts in the lawn
            _stage.Meadow(0f, -4f, 4f, _spotBase[0].z - World.MgStage3D.Origin.z - 1.5f, fz, 30, 0, 33, new Color(0.24f, 0.4f, 0.37f));
        }

        private Transform Gravestone(Vector3 basePt, float w, float h, int seed)
        {
            var t = new GameObject("Grave").transform;
            t.SetParent(_stage.Root.transform, false);
            t.position = basePt;
            t.rotation = Quaternion.Euler(0f, (seed * 37 % 17) - 8f, (seed * 13 % 9) - 4f);
            void P(GameObject go, Vector3 local, Vector3 scale) { go.transform.SetParent(t, false); go.transform.localPosition = local; go.transform.localScale = scale; go.transform.localRotation = Quaternion.identity; }
            var d = w * 0.35f;
            P(World.ArtLibrary.Box(t, "Slab", Vector3.zero, Vector3.one, _stoneMat), new Vector3(0f, (h - w * 0.5f) * 0.5f, 0f), new Vector3(w, h - w * 0.5f, d));
            var top = World.ArtLibrary.Cylinder(t, "Top", Vector3.zero, 0.5f, 1f, _stoneMat);
            P(top, new Vector3(0f, h - w * 0.5f, 0f), new Vector3(w, d * 0.5f, w));
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            P(World.ArtLibrary.Box(t, "CrossV", Vector3.zero, Vector3.one, _stoneDark, false), new Vector3(0f, h * 0.6f, -d * 0.52f), new Vector3(w * 0.1f, h * 0.36f, 0.02f));
            P(World.ArtLibrary.Box(t, "CrossH", Vector3.zero, Vector3.one, _stoneDark, false), new Vector3(0f, h * 0.66f, -d * 0.52f), new Vector3(w * 0.36f, h * 0.07f, 0.02f));
            return t;
        }

        private Transform Pumpkin3D(Vector3 basePt, float w, bool decoy)
        {
            var t = new GameObject(decoy ? "Decoy" : "Pumpkin").transform;
            t.SetParent(_stage.Root.transform, false);
            t.position = basePt;
            t.rotation = Quaternion.Euler(0f, 180f, 0f);
            void P(GameObject go, Vector3 local, Vector3 scale) { go.transform.SetParent(t, false); go.transform.localPosition = local; go.transform.localScale = scale; }
            for (var i = -1; i <= 1; i++) P(World.ArtLibrary.Sphere(t, "Lobe", Vector3.zero, 0.5f, _pumpkinMat), new Vector3(i * w * 0.22f, w * 0.36f, 0f), new Vector3(w * (i == 0 ? 0.62f : 0.5f), w * 0.72f, w * 0.62f));
            P(World.ArtLibrary.Cylinder(t, "Stem", Vector3.zero, 0.5f, 1f, _stemMat), new Vector3(0f, w * 0.75f, 0f), new Vector3(w * 0.1f, w * 0.1f, w * 0.1f));
            // carved face: two triangle-ish eyes and a grin that glow
            P(World.ArtLibrary.Box(t, "EyeL", Vector3.zero, Vector3.one, _pumpkinGlow, false), new Vector3(-w * 0.14f, w * 0.44f, w * 0.3f), new Vector3(w * 0.12f, w * 0.1f, 0.02f));
            P(World.ArtLibrary.Box(t, "EyeR", Vector3.zero, Vector3.one, _pumpkinGlow, false), new Vector3(w * 0.14f, w * 0.44f, w * 0.3f), new Vector3(w * 0.12f, w * 0.1f, 0.02f));
            P(World.ArtLibrary.Box(t, "Grin", Vector3.zero, Vector3.one, _pumpkinGlow, false), new Vector3(0f, w * 0.26f, w * 0.31f), new Vector3(w * 0.3f, w * 0.06f, 0.02f));
            return t;
        }

        private void Crypt(Vector3 basePt, float w)
        {
            var t = new GameObject("Crypt").transform;
            t.SetParent(_stage.Root.transform, false);
            t.position = basePt;
            void P(GameObject go, Vector3 local, Vector3 scale) { go.transform.SetParent(t, false); go.transform.localPosition = local; go.transform.localScale = scale; }
            var h = w * 0.62f;
            P(World.ArtLibrary.Box(t, "Body", Vector3.zero, Vector3.one, World.ArtLibrary.Solid(new Color(0.6f, 0.57f, 0.77f), 0.15f)), new Vector3(0f, h * 0.5f, w * 0.3f), new Vector3(w, h, w * 0.6f));
            var roof = World.ArtLibrary.Box(t, "Roof", Vector3.zero, Vector3.one, _stoneDark);
            P(roof, new Vector3(0f, h + w * 0.12f, w * 0.3f), new Vector3(w * 0.78f, w * 0.78f, w * 0.66f));
            roof.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            roof.transform.localScale = new Vector3(w * 0.5f, w * 0.5f, w * 0.66f);
            P(World.ArtLibrary.Box(t, "Door", Vector3.zero, Vector3.one, World.ArtLibrary.Solid(new Color(0.23f, 0.19f, 0.4f), 0.1f), false), new Vector3(0f, h * 0.36f, -0.01f), new Vector3(w * 0.27f, h * 0.72f, 0.04f));
        }

        private void RenderStage()
        {
            LayoutStage();
            if (!_laidOut) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var s = _game.State;
            var live = new HashSet<int>();
            foreach (var g in s.Ghosts)
            {
                live.Add(g.Id);
                if (!_ghosts3d.TryGetValue(g.Id, out var tr)) _ghosts3d[g.Id] = tr = SpawnGhost();
                var rise = (float)_game.Rise(g);
                var sc = DepthScale(GhostHunt.Spots[g.Spot].Z);
                var at = SpotAt(g.Spot);
                var lift = rise * 62f * sc;
                var h2d = Mathf.Max(8f, 72f * Mathf.Lerp(0.15f, 1f, rise));
                var centre = FieldToView(new Vector2(at.x + Mathf.Sin(_pulse * 3f + g.Id) * 3f, at.y - 20f - lift + h2d * 0.5f));
                var p = _stage.PlanePoint(centre.x, centre.y, _spotBase[g.Spot].z - 0.05f);
                var size = 72f * sc * _spotMpp[g.Spot];
                tr.position = p + Vector3.down * size * 0.45f;
                tr.localScale = new Vector3(size, size * Mathf.Lerp(0.3f, 1f, rise), size);
                tr.rotation = Quaternion.Euler(0f, 180f + Mathf.Sin(_pulse * 2f + g.Id) * 15f, Mathf.Sin(_pulse * 3f + g.Id) * 6f);
            }
            List<int> dead = null;
            foreach (var kv in _ghosts3d) if (!live.Contains(kv.Key)) (dead ??= new List<int>()).Add(kv.Key);
            if (dead != null)
                foreach (var id in dead)
                {
                    var tr = _ghosts3d[id];
                    if (tr != null)
                    {
                        if (!ReducedMotion) World.Effects.Puff(tr.position + Vector3.up * tr.localScale.y * 0.5f, new Color(0.95f, 0.95f, 1f, 0.9f), 10, tr.localScale.x * 0.14f, 1.4f, 0.5f, Vector3.up, 1.2f);
                        Object.Destroy(tr.gameObject);
                    }
                    _ghosts3d.Remove(id);
                }
            for (var i = 0; i < _decoys3d.Count; i++)
            {
                var f = s.Flickers.Find(x => x.Decoy == i);
                var d = _decoys3d[i];
                d.gameObject.SetActive(f != null);
                if (f == null) continue;
                var k = ReducedMotion ? 0f : Mathf.Sin(_pulse * 14f);
                d.localRotation = Quaternion.Euler(0f, 180f + k * 8f, k * 5f);
            }
            if (_lantern != null) _lantern.intensity = s.LanternT > 0.0 ? 4.5f : 2.2f + Mathf.Sin(_pulse * 5f) * 0.15f;
            var gb = _stage.Gooby;
            if (gb == null) return;
            _hop = Mathf.Max(0f, _hop - dt * 2.4f);
            var front = _stage.GroundPoint(ViewSize.w * 0.78, ViewSize.h * 0.9);
            gb.transform.position = new Vector3(front.x, World.MgStage3D.Origin.y + Mathf.Sin(_hop * Mathf.PI) * 0.35f, front.z + 0.4f);
            gb.transform.rotation = Quaternion.Euler(0f, 160f, Mathf.Sin(_pulse * 2f) * 2f);
            if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) gb.SetEmotion("happy");
        }

        private Transform SpawnGhost()
        {
            var t = new GameObject("Ghost").transform;
            t.SetParent(_stage.Root.transform, false);
            void P(GameObject go, Vector3 local, Vector3 scale) { go.transform.SetParent(t, false); go.transform.localPosition = local; go.transform.localScale = scale; }
            P(World.ArtLibrary.Sphere(t, "Head", Vector3.zero, 0.5f, _ghostMat), new Vector3(0f, 0.68f, 0f), new Vector3(0.8f, 0.8f, 0.7f));
            P(World.ArtLibrary.Cylinder(t, "Sheet", Vector3.zero, 0.5f, 1f, _ghostMat), new Vector3(0f, 0.34f, 0f), new Vector3(0.8f, 0.34f, 0.7f));
            for (var i = -1; i <= 1; i++) P(World.ArtLibrary.Sphere(t, "Hem", Vector3.zero, 0.5f, _ghostMat), new Vector3(i * 0.26f, 0.02f, 0f), new Vector3(0.3f, 0.24f, 0.3f));
            P(World.ArtLibrary.Sphere(t, "EyeL", Vector3.zero, 0.5f, _eyeMat, false), new Vector3(-0.15f, 0.74f, -0.31f), new Vector3(0.12f, 0.18f, 0.06f));
            P(World.ArtLibrary.Sphere(t, "EyeR", Vector3.zero, 0.5f, _eyeMat, false), new Vector3(0.15f, 0.74f, -0.31f), new Vector3(0.12f, 0.18f, 0.06f));
            P(World.ArtLibrary.Sphere(t, "Mouth", Vector3.zero, 0.5f, _eyeMat, false), new Vector3(0f, 0.55f, -0.33f), new Vector3(0.1f, 0.12f, 0.05f));
            return t;
        }

        private void StageCatch(bool good, bool scary)
        {
            var g = _stage?.Gooby;
            if (g == null) return;
            g.SetEmotion(scary ? "scared" : good ? "ecstatic" : "dizzy");
            if (good && !ReducedMotion) _hop = 1f;
            _emoteLeft = 1f;
        }

        private void DropStage()
        {
            _ghosts3d.Clear(); _decoys3d.Clear(); _spotBase.Clear(); _spotMpp.Clear();
            _lantern = null;
            _laidOut = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
