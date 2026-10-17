using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D vegetable garden of the Möhrenwache (port of carrot_guard_stage3d.gd): nine earth mounds on a soil bed in a
    /// summer meadow, seen at a slant from above; 3D moles rise from their holes with a squash and a spray of earth, the
    /// mole king wears a golden crown. The carrots still in the bed stand in a row behind the mounds and lean over when a
    /// mole steals one. Gooby keeps watch behind the bed with a wooden mallet and swings it at every bonk. The mounds are
    /// laid under their 2D tap rectangles by rays through the tilted camera, so taps stay exactly as they are.
    /// </summary>
    public sealed partial class CarrotGuardScreen
    {
        private World.MgStage3D _stage;
        private readonly List<Transform> _mounds3d = new List<Transform>();
        private readonly List<Transform> _moles3d = new List<Transform>();
        private readonly List<Transform> _carrots3d = new List<Transform>();
        private Transform _king3d, _mallet;
        private float _moundR = 0.6f, _swingT, _emoteLeft, _stageT;
        private int _swingHole = -1;
        private Vector3 _guardAt;
        private bool _laidOut;

        protected override bool StageHasGooby => _stage != null;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewSize.w, ViewSize.h, new Color(0.62f, 0.8f, 0.95f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 10.5f, -8f), 40f, 52f);
            var s = _stage;
            s.Ground(0f, new Color(0.62f, 0.8f, 0.5f), "grass", 80f, 70f, 14f);
            s.Painting("bg_town_horizon", 0f, 34f, 22f);
            s.Fence("log_fence", 0f, 15f, -10f, 10f, 2f, 1f);
            s.Row("tree_round_big", 0f, 18.5f, -12f, 12f, 5, 1.6f, 0f, 1.2f, 21);
            s.Row("tree_round", 0f, 12.5f, -9.6f, -8.4f, 2, 1.4f, 0f, 0.6f, 22);
            s.PlaceScenery("garden_shed", World.MgStage3D.At(7.5f, 0f, 13f), 200f, 1.2f);
            s.Meadow(0f, -8f, 8f, 9f, 14f, 30, 14, 23);
            s.Meadow(0f, -6f, 6f, -6f, -2.6f, 16, 8, 24);
            s.Prop("crop_pumpkin", World.MgStage3D.At(-1.4f, 0f, -3.0f), 1.4f, 30f);
            s.PlaceScenery("watering_can", World.MgStage3D.At(1.3f, 0f, -3.1f), -40f, 1f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnGuard(outfit);
        }

        private async System.Threading.Tasks.Task SpawnGuard(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(1.5f, 0f, 9f), 2.3f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            // the wooden mallet in his right paw (avatar space: it follows him, the swing turns it about the paw)
            _mallet = new GameObject("MalletPivot").transform;
            _mallet.SetParent(g.transform, false);
            _mallet.localPosition = new Vector3(0.34f, 0.42f, 0.28f);
            var wood = World.ArtLibrary.Solid(new Color(0.72f, 0.52f, 0.34f), 0.25f);
            var handle = World.ArtLibrary.Cylinder(_mallet, "Handle", Vector3.zero, 0.03f, 0.56f, wood);
            handle.transform.SetParent(_mallet, false);
            handle.transform.localPosition = new Vector3(0f, 0.26f, 0f);
            handle.transform.localScale = new Vector3(0.06f, 0.28f, 0.06f);
            var head = World.ArtLibrary.Cylinder(_mallet, "Head", Vector3.zero, 0.1f, 0.3f, World.ArtLibrary.Solid(new Color(0.84f, 0.62f, 0.4f), 0.25f));
            head.transform.SetParent(_mallet, false);
            head.transform.localPosition = new Vector3(0f, 0.58f, 0f);
            head.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            head.transform.localScale = new Vector3(0.2f, 0.15f, 0.2f);
        }

        /// <summary>Mounds, moles, the king and the carrot row under their 2D places (once the field has its size).</summary>
        private void LayoutStage()
        {
            if (_stage == null || FieldW <= 0f || _laidOut) return;
            _laidOut = true;
            var sx = (float)ViewSize.w / FieldW;
            var sy = (float)ViewSize.h / FieldH;
            var root = _stage.Root.transform;
            var soil = World.ArtLibrary.Solid(new Color(0.45f, 0.3f, 0.2f), 0.05f);
            var dirt = World.ArtLibrary.Solid(new Color(0.56f, 0.39f, 0.25f), 0.05f);
            var hole = World.ArtLibrary.Solid(new Color(0.2f, 0.13f, 0.09f), 0.02f);
            float lx = float.MaxValue, hx = float.MinValue, lz = float.MaxValue, hz = float.MinValue;
            for (var i = 0; i < 9; i++)
            {
                var c = MoundCentre(i);
                var p = _stage.GroundPoint(c.x * sx, c.y * sy);
                _moundR = Mathf.Clamp(_stage.GroundMetres(c.x * sx, c.y * sy, MoundW * sx) * 0.5f, 0.3f, 1.5f);
                var m = new GameObject("Mound").transform;
                m.SetParent(root, false);
                m.position = p;
                var heap = World.ArtLibrary.Sphere(m, "Heap", p, 1f, dirt);
                heap.transform.localScale = new Vector3(_moundR * 2f, _moundR * 0.7f, _moundR * 1.7f);
                var pit = World.ArtLibrary.Cylinder(m, "Hole", p + Vector3.up * (_moundR * 0.33f), _moundR * 0.55f, 0.02f, hole, false);
                pit.transform.localScale = new Vector3(_moundR * 1.1f, 0.01f, _moundR * 0.9f);
                _mounds3d.Add(m);
                _moles3d.Add(SpawnMole(false));
                lx = Mathf.Min(lx, p.x); hx = Mathf.Max(hx, p.x); lz = Mathf.Min(lz, p.z); hz = Mathf.Max(hz, p.z);
            }
            _king3d = SpawnMole(true);
            // the soil bed with a log edging under the mounds
            var cx = (lx + hx) * 0.5f; var cz = (lz + hz) * 0.5f;
            var w = hx - lx + _moundR * 3.4f; var d = hz - lz + _moundR * 4.6f;
            World.ArtLibrary.Box(root, "Bed", new Vector3(cx, World.MgStage3D.Origin.y + 0.02f, cz + _moundR * 0.5f), new Vector3(w, 0.06f, d), soil, false);
            var edge = World.ArtLibrary.Solid(new Color(0.76f, 0.57f, 0.38f), 0.2f);
            World.ArtLibrary.Box(root, "EdgeN", new Vector3(cx, World.MgStage3D.Origin.y + 0.1f, cz + _moundR * 0.5f + d * 0.5f), new Vector3(w + 0.3f, 0.2f, 0.18f), edge);
            World.ArtLibrary.Box(root, "EdgeS", new Vector3(cx, World.MgStage3D.Origin.y + 0.1f, cz + _moundR * 0.5f - d * 0.5f), new Vector3(w + 0.3f, 0.2f, 0.18f), edge);
            World.ArtLibrary.Box(root, "EdgeW", new Vector3(cx - w * 0.5f, World.MgStage3D.Origin.y + 0.1f, cz + _moundR * 0.5f), new Vector3(0.18f, 0.2f, d), edge);
            World.ArtLibrary.Box(root, "EdgeE", new Vector3(cx + w * 0.5f, World.MgStage3D.Origin.y + 0.1f, cz + _moundR * 0.5f), new Vector3(0.18f, 0.2f, d), edge);
            // the carrots still in the bed: a row along the far edge (the 2D row sits at the top of the field)
            var n = _game.Tune.Carrots;
            for (var i = 0; i < n; i++)
            {
                var f = n == 1 ? 0.5f : i / (float)(n - 1);
                var p = new Vector3(Mathf.Lerp(lx - _moundR * 0.6f, hx + _moundR * 0.6f, f), World.MgStage3D.Origin.y + 0.04f, cz + _moundR * 0.5f + d * 0.5f - 0.45f);
                _carrots3d.Add(_stage.Prop("crop_carrot", p, 1.15f, i * 47f));
            }
            // Gooby keeps watch at the far right of the bed
            _guardAt = new Vector3(cx + (hx - cx) * 0.62f, World.MgStage3D.Origin.y, cz + _moundR * 0.5f + d * 0.5f + 0.75f);
        }

        /// <summary>A mole: a furry dome with a pink snout, bead eyes, white teeth and little paws; the king purple with a crown.</summary>
        private Transform SpawnMole(bool king)
        {
            var root = new GameObject(king ? "King" : "Mole").transform;
            root.SetParent(_stage.Root.transform, false);
            var r = _moundR * (king ? 0.8f : 0.64f);
            var fur = World.ArtLibrary.Solid(king ? new Color(0.48f, 0.36f, 0.56f) : new Color(0.52f, 0.41f, 0.35f), 0.15f);
            GameObject P(GameObject go, Vector3 local, Vector3 scale) { go.transform.SetParent(root, false); go.transform.localPosition = local; go.transform.localScale = scale; return go; }
            P(World.ArtLibrary.Sphere(root, "Body", Vector3.zero, 0.5f, fur), new Vector3(0f, r * 0.9f, 0f), new Vector3(r * 2f, r * 2.2f, r * 1.8f));
            P(World.ArtLibrary.Sphere(root, "Snout", Vector3.zero, 0.5f, World.ArtLibrary.Solid(new Color(1f, 0.6f, 0.64f), 0.4f)), new Vector3(0f, r * 1.2f, -r * 0.88f), new Vector3(r * 0.5f, r * 0.36f, r * 0.4f));
            var eye = World.ArtLibrary.Solid(new Color(0.1f, 0.08f, 0.08f), 0.8f);
            P(World.ArtLibrary.Sphere(root, "EyeL", Vector3.zero, 0.5f, eye, false), new Vector3(-r * 0.36f, r * 1.5f, -r * 0.78f), Vector3.one * r * 0.2f);
            P(World.ArtLibrary.Sphere(root, "EyeR", Vector3.zero, 0.5f, eye, false), new Vector3(r * 0.36f, r * 1.5f, -r * 0.78f), Vector3.one * r * 0.2f);
            P(World.ArtLibrary.Box(root, "Teeth", Vector3.zero, Vector3.one, World.ArtLibrary.Solid(Color.white, 0.4f), false), new Vector3(0f, r * 0.95f, -r * 0.9f), new Vector3(r * 0.22f, r * 0.18f, r * 0.05f));
            var paw = World.ArtLibrary.Solid(new Color(0.95f, 0.78f, 0.72f), 0.2f);
            P(World.ArtLibrary.Sphere(root, "PawL", Vector3.zero, 0.5f, paw), new Vector3(-r * 0.62f, r * 0.55f, -r * 0.75f), new Vector3(r * 0.45f, r * 0.25f, r * 0.35f));
            P(World.ArtLibrary.Sphere(root, "PawR", Vector3.zero, 0.5f, paw), new Vector3(r * 0.62f, r * 0.55f, -r * 0.75f), new Vector3(r * 0.45f, r * 0.25f, r * 0.35f));
            if (king)
            {
                var gold = World.ArtLibrary.Solid(new Color(1f, 0.8f, 0.25f), 0.7f);
                P(World.ArtLibrary.Cylinder(root, "Crown", Vector3.zero, 0.5f, 1f, gold), new Vector3(0f, r * 2.05f, 0f), new Vector3(r * 0.9f, r * 0.18f, r * 0.9f));
                for (var i = 0; i < 5; i++)
                {
                    var a = i * Mathf.PI * 2f / 5f;
                    var spike = _stage.Cone(Vector3.zero, r * 0.12f, r * 0.32f, gold);
                    spike.transform.SetParent(root, false);
                    spike.transform.localPosition = new Vector3(Mathf.Sin(a) * r * 0.4f, r * 2.2f, Mathf.Cos(a) * r * 0.4f);
                }
                P(World.ArtLibrary.Sphere(root, "Jewel", Vector3.zero, 0.5f, World.ArtLibrary.Glow(new Color(1.4f, 0.4f, 0.55f)), false), new Vector3(0f, r * 2.1f, -r * 0.44f), Vector3.one * r * 0.16f);
            }
            root.gameObject.SetActive(false);
            return root;
        }

        private void PoseMole(Transform mole, int hole, float up, bool flicker, bool king)
        {
            if (hole < 0 || hole >= _mounds3d.Count) { mole.gameObject.SetActive(false); return; }
            mole.gameObject.SetActive(true);
            var basePos = _mounds3d[hole].position;
            var squash = up < 1f ? 1f + (1f - up) * 0.25f : 1f + Mathf.Sin(_stageT * (king ? 8f : 11f)) * 0.03f;
            mole.position = basePos + Vector3.up * (_moundR * (up * 0.75f - 0.75f));
            mole.localScale = new Vector3(squash, 1f / squash, squash);
            mole.rotation = Quaternion.Euler(0f, 180f + Mathf.Sin(_stageT * 3f + hole) * 12f, 0f);
            if (flicker && !ReducedMotion) mole.localScale *= 0.92f + 0.08f * Mathf.Sin(_stageT * 25f);
        }

        private void RenderStage()
        {
            LayoutStage();
            if (!_laidOut) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            for (var i = 0; i < _moles3d.Count; i++) _moles3d[i].gameObject.SetActive(false);
            foreach (var m in _game.Moles) PoseMole(_moles3d[m.Hole], m.Hole, (float)m.Up, m.Left < 0.25, false);
            var king = _game.TheKing;
            if (king != null) PoseMole(_king3d, king.Hole, (float)king.Up, false, true);
            else _king3d.gameObject.SetActive(false);
            // a stolen carrot leans over and fades into the soil
            for (var i = 0; i < _carrots3d.Count; i++)
            {
                var gone = i >= _game.Carrots;
                var c = _carrots3d[i];
                c.localRotation = Quaternion.Slerp(c.localRotation, Quaternion.Euler(gone ? 70f : 0f, i * 47f, 0f), 1f - Mathf.Exp(-6f * dt));
                var target = gone ? 0.35f : 1.15f;
                c.localScale = Vector3.one * Mathf.MoveTowards(c.localScale.x, target, dt * 1.5f);
            }
            // the mallet swing: up, a quick chop towards the camera, back
            if (_mallet != null)
            {
                _swingT = Mathf.Max(0f, _swingT - dt * 3.2f);
                var k = _swingT > 0.5f ? (1f - _swingT) * 2f : _swingT * 2f;
                _mallet.localRotation = Quaternion.Euler(-95f * k, 0f, -12f);
            }
            var g = _stage.Gooby;
            if (g != null)
            {
                var lean = _swingHole >= 0 && _swingT > 0f ? Mathf.Clamp((_mounds3d[_swingHole].position.x - g.transform.position.x) * 8f, -25f, 25f) : 0f;
                g.transform.position = _guardAt;
                g.transform.rotation = Quaternion.Euler(0f, 210f - lean, Mathf.Sin(_stageT * 2.6f) * 1.7f);
                if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) g.SetEmotion("happy");
            }
        }

        private void StageBonk(int hole, bool king, bool down)
        {
            if (_stage == null || !_laidOut || hole < 0 || hole >= _mounds3d.Count) return;
            _swingT = 1f;
            _swingHole = hole;
            var at = _mounds3d[hole].position + Vector3.up * (_moundR * 0.6f);
            if (!ReducedMotion)
            {
                World.Effects.Puff(at, new Color(0.6f, 0.44f, 0.3f, 0.9f), 10, _moundR * 0.16f, 1.6f, 0.45f, Vector3.up * 1.8f, 1.1f);
                World.Effects.Puff(at + Vector3.up * 0.3f, new Color(1f, 0.92f, 0.55f), 8, _moundR * 0.1f, 2f, 0.4f, Vector3.up * 2f, 1.3f);
            }
            var g = _stage.Gooby;
            g?.SetEmotion(down ? "ecstatic" : "happy");
            if (down) { g?.Play("celebrate"); if (!ReducedMotion) World.Effects.Sparkle(at + Vector3.up * 0.5f, 0.7f, 30); }
            _emoteLeft = down ? 1.6f : 0.6f;
        }

        private void StageSteal(int hole)
        {
            if (_stage == null || !_laidOut || hole < 0 || hole >= _mounds3d.Count) return;
            if (!ReducedMotion) World.Effects.Puff(_mounds3d[hole].position + Vector3.up * 0.4f, new Color(1f, 0.6f, 0.3f), 8, _moundR * 0.12f, 1.5f, 0.5f, Vector3.up * 1.5f, 1f);
            _stage.Gooby?.SetEmotion("scared");
            _emoteLeft = 1.2f;
        }

        private void DropStage()
        {
            _mounds3d.Clear(); _moles3d.Clear(); _carrots3d.Clear();
            _king3d = null; _mallet = null;
            _laidOut = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
