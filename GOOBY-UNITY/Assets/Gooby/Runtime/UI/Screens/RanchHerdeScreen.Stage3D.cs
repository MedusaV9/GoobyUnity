using System.Collections.Generic;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Sheep herding in 3D (port of herde_game.gd): the fenced paddock seen from the south at 54° (framed to the field
    /// like Godot's fit), mowing stripes and flowers, the sandy pen in the north with its signal-yellow gate posts and
    /// bunting, a small grandstand of Gooby fans; the flock of real sheep (Quaternius, idle/walk/run) that hop when they
    /// make it into the pen; Gooby riding the ranch horse to wherever the finger points, the flee radius as a soft ring
    /// on the grass, the target flag with its touch-down pulse. Taps are cast onto the ground through the camera, so a
    /// finger on a sheep means that sheep. Core (x, z) maps to stage (x, −z): north is away from the camera.
    /// </summary>
    public sealed partial class RanchHerdeScreen
    {
        // the far framing camera shows actors small: horse, rider and sheep stand a size up (the flock's spacing is the simulation's)
        private const float HorseScale = 1.3f, RiderScale = 0.72f * HorseScale, SheepScale = 1.08f, Pitch = 54f, Fov = 40f;

        private sealed class Sheep
        {
            public Transform Anchor;
            public Animation Anim;
            public string Clip = "";
            public bool WasDrin;
            public float Yaw;
        }

        private World.MgStage3D _stage;
        private Transform _paddock, _ring, _flag, _pulse;
        private RanchHorse3D _horse;
        private GoobyAvatar _rider;
        private RanchCrowd _stands;
        private JObject _builtLevel;
        private object _builtFlock;
        private readonly List<Sheep> _sheep = new List<Sheep>();
        private V2 _riderPrev3;
        private float _riderYaw3, _stageT, _pulseT, _emoteLeft, _riderBackIn, _cheer, _dustT;
        private bool _cheerBig, _camSnap = true;

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 Pt(double x, double z, float y = 0f) => World.MgStage3D.At((float)x, y, -(float)z);
        private static Vector3 Pt(V2 p, float y = 0f) => Pt(p.X, p.Y, y);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.62f, 0.8f, 0.95f));
            _stage.Bind(Field);
            _stage.Camera.farClipPlane = 360f;
            _stage.Ground(-0.02f, new Color(0.58f, 0.76f, 0.46f), "grass", 300f, 260f, 0f);
            RanchDressing.Horizon(_stage, Vector3.zero, 120f, 47);
            var farm = new GameObject("Hof").transform;
            farm.SetParent(_stage.Root.transform, false);
            RanchDressing.Farm(_stage, farm, "scheune", Pt(-24.0, -28.0), 2.2f, 160f);
            RanchDressing.Farm(_stage, farm, "silo", Pt(-14.0, -32.0), 2f, 0f);
            RanchDressing.Farm(_stage, farm, "windmuehle_turm", Pt(30.0, -30.0), 2.1f, 200f);
            _horse = RanchHorse3D.Create(_stage.Root.transform, "Pferd", MgScenery.Hex(0xC58B5A), MgScenery.Hex(0x6E4A2E));
            _horse.Hufe = "wiese";
            _horse.transform.localScale = Vector3.one * HorseScale;
            _ring = RanchDressing.Ring(_stage.Root.transform, "Fluchtring", 0.96f, 1f, ArtLibrary.Transparent(new Color(1f, 0.98f, 0.9f, 0.4f)));
            _flag = new GameObject("Zielfahne").transform;
            _flag.SetParent(_stage.Root.transform, false);
            ArtLibrary.Cylinder(_flag, "Stab", Vector3.zero, 0.035f, 0.9f, ArtLibrary.Solid(RanchDressing.HolzDunkel, 0.1f), false).transform.localPosition = new Vector3(0f, 0.45f, 0f);
            var tuch = RanchDressing.MeshObject(_flag, "Tuch", RanchDressing.PennantMesh(), ArtLibrary.Solid(new Color(1f, 0.55f, 0.55f), 0.2f)).transform;
            tuch.localPosition = new Vector3(0f, 0.8f, 0f);
            tuch.localScale = new Vector3(0.5f, 0.32f, 1f);
            _pulse = RanchDressing.Ring(_flag, "Puls", 0.32f, 0.45f, ArtLibrary.Glow(new Color(1.2f, 1f, 0.55f)));
            _pulse.localPosition = new Vector3(0f, 0.04f, 0f);
            _flag.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnRider(outfit);
        }

        private async System.Threading.Tasks.Task SpawnRider(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(Pt(0.0, 6.0, 1.3f), RiderScale, outfit);
            if (g == null || _stage == null) return;
            _rider = g;
            _horse.Mount(g.transform);
            g.Play("sit-loop", 0f);
        }

        /// <summary>The paddock of one level: field, fence, mowing stripes, pen with the gate, stands and meadow.</summary>
        private void BuildPaddock(JObject level)
        {
            _builtLevel = level;
            if (_paddock != null) Object.Destroy(_paddock.gameObject);
            _stands?.Destroy();
            _stands = null;
            _paddock = new GameObject("Weide").transform;
            _paddock.SetParent(_stage.Root.transform, false);
            _camSnap = true;
            var (hxD, hzD) = RanchHerde.Feld(level);
            float hx = (float)hxD, hz = (float)hzD;
            var parts = new List<GameObject>();
            for (var i = 0; i < 12; i++)
            {
                var x0 = -hx + i * hx * 2f / 12f;
                parts.Add(ArtLibrary.Box(_paddock, "Streifen", Pt(x0 + hx / 12f, 0.0, 0.004f), new Vector3(hx * 2f / 12f, 0.008f, hz * 2f), ArtLibrary.Solid(i % 2 == 0 ? new Color(0.56f, 0.76f, 0.43f) : new Color(0.51f, 0.71f, 0.39f), 0.04f), false));
            }
            var rail = ArtLibrary.Solid(RanchDressing.Creme, 0.2f);
            foreach (var h in new[] { 0.34f, 0.68f })
            {
                parts.Add(ArtLibrary.Box(_paddock, "Latte", Pt(0.0, -hz, h), new Vector3(hx * 2f + 0.2f, 0.09f, 0.07f), rail));
                parts.Add(ArtLibrary.Box(_paddock, "Latte", Pt(0.0, hz, h), new Vector3(hx * 2f + 0.2f, 0.09f, 0.07f), rail));
                parts.Add(ArtLibrary.Box(_paddock, "Latte", Pt(-hx, 0.0, h), new Vector3(0.07f, 0.09f, hz * 2f + 0.2f), rail));
                parts.Add(ArtLibrary.Box(_paddock, "Latte", Pt(hx, 0.0, h), new Vector3(0.07f, 0.09f, hz * 2f + 0.2f), rail));
            }
            var post = ArtLibrary.Solid(RanchDressing.HolzDunkel, 0.08f);
            var nx = Mathf.Max(2, (int)(hx * 2f / 3f));
            var nz = Mathf.Max(2, (int)(hz * 2f / 3f));
            for (var i = 0; i <= nx; i++)
                foreach (var z in new[] { -hz, hz })
                    parts.Add(ArtLibrary.Box(_paddock, "Pfosten", Pt(-hx + i * hx * 2f / nx, z, 0.42f), new Vector3(0.14f, 0.85f, 0.14f), post));
            for (var i = 1; i < nz; i++)
                foreach (var x in new[] { -hx, hx })
                    parts.Add(ArtLibrary.Box(_paddock, "Pfosten", Pt(x, -hz + i * hz * 2f / nz, 0.42f), new Vector3(0.14f, 0.85f, 0.14f), post));
            // the pen: sandy floor, walls with a gap for the gate, signal-yellow gate posts and bunting
            var p = RanchHerde.Pferch.Of(level);
            float px = (float)p.X, pz = (float)p.Z, w = (float)p.W, t = (float)p.T, tor = (float)p.Tor;
            var zaun = ArtLibrary.Solid(new Color(0.72f, 0.53f, 0.36f), 0.1f);
            parts.Add(ArtLibrary.Box(_paddock, "Pferchboden", Pt(px, pz, 0.025f), new Vector3(w, 0.05f, t), ArtLibrary.Textured("tex_sand", new Vector2(w / 2.4f, t / 2.4f), new Color(1f, 0.92f, 0.78f), 0.04f), false));
            parts.Add(ArtLibrary.Box(_paddock, "Wand", Pt(px, pz - t * 0.5f, 0.45f), new Vector3(w, 0.9f, 0.18f), zaun));
            parts.Add(ArtLibrary.Box(_paddock, "Wand", Pt(px - w * 0.5f, pz, 0.45f), new Vector3(0.18f, 0.9f, t), zaun));
            parts.Add(ArtLibrary.Box(_paddock, "Wand", Pt(px + w * 0.5f, pz, 0.45f), new Vector3(0.18f, 0.9f, t), zaun));
            var sued = pz + t * 0.5f;
            var seg = (w - tor) * 0.5f;
            if (seg > 0.05f)
            {
                parts.Add(ArtLibrary.Box(_paddock, "Wand", Pt(px - tor * 0.5f - seg * 0.5f, sued, 0.45f), new Vector3(seg, 0.9f, 0.18f), zaun));
                parts.Add(ArtLibrary.Box(_paddock, "Wand", Pt(px + tor * 0.5f + seg * 0.5f, sued, 0.45f), new Vector3(seg, 0.9f, 0.18f), zaun));
            }
            foreach (var sx in new[] { -1f, 1f })
            {
                parts.Add(ArtLibrary.Box(_paddock, "Torpfosten", Pt(px + sx * tor * 0.5f, sued, 0.8f), new Vector3(0.24f, 1.6f, 0.24f), ArtLibrary.Solid(new Color(0.93f, 0.72f, 0.35f), 0.25f)));
                var knob = RanchDressing.Blob(_paddock, "Knauf", Pt(px + sx * tor * 0.5f, sued, 1.66f), ArtLibrary.Solid(new Color(1f, 0.83f, 0.3f), 0.4f));
                knob.transform.localScale = Vector3.one * 0.3f;
                parts.Add(knob);
            }
            parts.AddRange(RanchDressing.Bunting(_paddock, Pt(px - tor * 0.5f, sued, 1.62f), Pt(px + tor * 0.5f, sued, 1.62f), 0.22f, 0.5f));
            // the trough and hay in the pen: where the sheep want to be
            var hay = RanchDressing.Farm(_stage, _paddock, "heuballen", Pt(px - w * 0.32f, pz - t * 0.22f), 0.8f, 15f);
            RanchDressing.Farm(_stage, _paddock, "trog", Pt(px + w * 0.28f, pz - t * 0.3f), 0.8f, 0f);
            _ = hay;
            // meadow: flowers inside the field, trees and bushes outside
            var rng = new System.Random(613);
            var bloom = new[] { new Color(1f, 1f, 1f), new Color(1f, 0.88f, 0.42f), new Color(1f, 0.62f, 0.72f) };
            for (var i = 0; i < 34; i++)
            {
                var fx = -hx + 0.6f + (float)rng.NextDouble() * (hx * 2f - 1.2f);
                var fz = -hz + 0.6f + (float)rng.NextDouble() * (hz * 2f - 1.2f);
                if (RanchHerde.IstImPferch(level, new V2(fx, fz), -0.6)) continue;
                var f = RanchDressing.Blob(_paddock, "Bluete", Pt(fx, fz, 0.06f), ArtLibrary.Solid(bloom[i % bloom.Length], 0.2f));
                f.transform.localScale = new Vector3(0.16f, 0.1f, 0.16f);
                parts.Add(f);
            }
            _stage.Merge(_paddock, parts, "Weide");
            var trees = new[] { "tree_round_big", "tree_round", "tree_poplar", "tree_big" };
            for (var i = 0; i < 18; i++)
            {
                var a = i / 18f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.2f;
                var tx = Mathf.Cos(a) * (hx + 4f + (float)rng.NextDouble() * 6f);
                var tz = Mathf.Sin(a) * (hz + 4f + (float)rng.NextDouble() * 5f);
                if (tz > hz) continue;
                _stage.PlaceScenery(trees[i % trees.Length], Pt(tx, tz), i * 37f % 360f, 1.4f + (float)rng.NextDouble() * 0.6f).SetParent(_paddock, true);
            }
            for (var i = 0; i < 10; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                _stage.PlaceScenery(i % 3 == 0 ? "bush_flowers" : "bush_big", Pt(side * (hx + 1.6f), -hz + 1f + (float)rng.NextDouble() * hz * 1.6f), i * 51f % 360f, 0.95f).SetParent(_paddock, true);
            }
            // a small grandstand outside the north fence, on the side away from the pen
            var gx = (p.X <= 0.0 ? 1f : -1f) * hx * 0.55f;
            var stand = new List<GameObject>();
            for (var stufe = 0; stufe < 2; stufe++)
                stand.Add(ArtLibrary.Box(_paddock, "Stufe", Pt(gx, -hz - 2.2f - stufe * 1.05f, 0.27f + stufe * 0.5f), new Vector3(7f, 0.5f, 1.05f), ArtLibrary.Solid(stufe % 2 == 0 ? RanchDressing.Holz : RanchDressing.HolzDunkel, 0.1f)));
            _stage.Merge(_paddock, stand, "Tribuene");
            var seats = new List<Vector3>();
            var seatRng = new System.Random(227);
            for (var i = 0; i < 8; i++)
            {
                var reihe = i % 2;
                seats.Add(Pt(gx - 2.8f + i / 2 * 1.9f + (float)seatRng.NextDouble() * 0.6f, -hz - 2.2f - reihe * 1.05f + 0.1f, 0.52f + reihe * 0.5f));
            }
            _stands = new RanchCrowd(_stage, seats, 180f);
            FrameCamera(hx, hz);
        }

        /// <summary>Godot's stage fit: a 54° camera from the south, pulled back until all four corners sit in the view.</summary>
        private void FrameCamera(float hx, float hz)
        {
            var cam = _stage.Camera;
            cam.fieldOfView = Fov;
            var back = new Vector3(0f, Mathf.Sin(Pitch * Mathf.Deg2Rad), -Mathf.Cos(Pitch * Mathf.Deg2Rad));
            var centre = Pt(0.0, 0.6);
            var corners = new[] { Pt(-hx, -hz), Pt(hx, -hz), Pt(-hx, hz), Pt(hx, hz) };
            float lo = 4f, hi = 120f;
            for (var it = 0; it < 24; it++)
            {
                var d = (lo + hi) * 0.5f;
                cam.transform.position = centre + back * d;
                cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
                var inside = true;
                foreach (var c in corners)
                {
                    var v = cam.WorldToViewportPoint(c);
                    // keep the top strip free for the round HUD and the counter chip
                    if (v.z <= 0f || v.x < 0.03f || v.x > 0.97f || v.y < 0.04f || v.y > 0.86f) { inside = false; break; }
                }
                if (inside) hi = d; else lo = d;
            }
            cam.transform.position = centre + back * hi;
        }

        /// <summary>One sheep: the GLB with its own Animation (Idle/Walk/Run/Jump), every fifth one in dark wool.</summary>
        private Sheep SpawnSheep(int index)
        {
            var anchor = new GameObject("Schaf" + index).transform;
            anchor.SetParent(_stage.Root.transform, false);
            anchor.localScale = Vector3.one * SheepScale * (0.9f + (index * 37 % 10) / 50f);
            var sheep = new Sheep { Anchor = anchor };
            _ = LoadSheep(sheep, index % 5 == 4);
            return sheep;
        }

        private static async System.Threading.Tasks.Task LoadSheep(Sheep sheep, bool dark)
        {
            var go = await AssetLoader.InstanceAsync(RanchDressing.ModelPath("schaf"), sheep.Anchor, "Schaf", legacyAnimation: true);
            if (go == null) return;
            if (sheep.Anchor == null) { Object.Destroy(go); return; }
            go.transform.localPosition = Vector3.zero;
            // the Quaternius sheep looks down its own −Z
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sheep.Anim = go.GetComponentInChildren<Animation>();
            if (sheep.Anim != null) sheep.Anim.playAutomatically = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!dark) continue;
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.StartsWith("White")) mats[i] = ArtLibrary.Solid(new Color(0.46f, 0.41f, 0.39f), 0.05f);
                r.sharedMaterials = mats;
            }
            sheep.Clip = "";
            PlaySheep(sheep, "Idle", 1f);
        }

        private static void PlaySheep(Sheep s, string clip, float speed)
        {
            if (s.Anim == null) return;
            var state = s.Anim[clip];
            if (state == null) return;
            state.speed = speed;
            if (s.Clip == clip) return;
            state.wrapMode = clip == "Jump" ? WrapMode.Once : WrapMode.Loop;
            s.Anim.CrossFade(clip, 0.2f);
            if (clip == "Jump") s.Anim.CrossFadeQueued("Idle", 0.2f);
            s.Clip = clip;
        }

        private void RenderStage()
        {
            if (_stage == null) return;
            var g = _game;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            var level = g.Level ?? RanchHerde.LevelById(g.Levels, Mathf.Max(1, g.LevelId));
            if (!ReferenceEquals(level, _builtLevel)) BuildPaddock(level);
            var playing = g.Level != null && g.Phase != RanchHerdeGame.PhaseSelect;
            SyncFlock(playing ? g.Schafe : null, dt);
            PoseRider(g, playing, dt);
            if (_ring != null)
            {
                _ring.gameObject.SetActive(playing);
                var r = (float)g.Tune.FluchtRadius * (1f + (ReducedMotion ? 0f : 0.025f * Mathf.Sin(_stageT * 2.4f)));
                _ring.position = Pt(g.Reiter, 0.05f);
                _ring.localScale = new Vector3(r, 1f, r);
            }
            UpdateFlag(g, playing, dt);
            UpdateRiderMood(dt);
            _stands?.Tick(dt, _cheer > 0f, _cheerBig);
            _cheer = 0f;
            _cheerBig = false;
            _camSnap = false;
        }

        /// <summary>Sheep follow the simulation (by index: the step hands back a fresh list); a sheep that makes it in hops.</summary>
        private void SyncFlock(List<RanchHerde.Schaf> flock, float dt)
        {
            var n = flock?.Count ?? 0;
            if (!ReferenceEquals(flock?.Count > 0 ? (object)_game.Level : null, _builtFlock) || _sheep.Count != n)
            {
                foreach (var s in _sheep) if (s.Anchor != null) Object.Destroy(s.Anchor.gameObject);
                _sheep.Clear();
                for (var i = 0; i < n; i++) _sheep.Add(SpawnSheep(i));
                _builtFlock = n > 0 ? (object)_game.Level : null;
            }
            for (var i = 0; i < n; i++)
            {
                var s = flock[i];
                var sheep = _sheep[i];
                var speed = s.Vel.Length;
                if (speed > 0.2f) sheep.Yaw = Mathf.Atan2(s.Vel.X, -s.Vel.Y) * Mathf.Rad2Deg;
                var hop = ReducedMotion ? 0f : Mathf.Abs(Mathf.Sin(_stageT * 6f + (float)s.Phase)) * 0.06f * Mathf.Min(1f, speed);
                sheep.Anchor.position = Pt(s.Pos, hop);
                sheep.Anchor.rotation = Quaternion.Slerp(sheep.Anchor.rotation, Quaternion.Euler(0f, sheep.Yaw, 0f), 1f - Mathf.Exp(-10f * dt));
                if (s.Drin && !sheep.WasDrin)
                {
                    sheep.Clip = "";
                    PlaySheep(sheep, "Jump", 1.1f);
                    if (!ReducedMotion) Effects.Sparkle(Pt(s.Pos, 0.9f), 0.5f, 16);
                }
                else if (sheep.Clip != "Jump" || sheep.Anim == null || !sheep.Anim.IsPlaying("Jump"))
                    PlaySheep(sheep, s.Drin || speed < 0.3f ? "Idle" : speed > 2f ? "Run" : "Walk", s.Drin ? 1f : Mathf.Clamp(speed / (speed > 2f ? 3f : 1.2f), 0.6f, 1.4f));
                sheep.WasDrin = s.Drin;
            }
        }

        private void PoseRider(RanchHerdeGame g, bool playing, float dt)
        {
            var reiter = playing ? g.Reiter : new V2(0f, (float)RanchHerde.Feld(_builtLevel).z * 0.55f);
            var moved = reiter - _riderPrev3;
            var speed = dt > 0f ? moved.Length / dt : 0f;
            if (moved.Length > 0.002f) _riderYaw3 = Mathf.Atan2(moved.X, -moved.Y) * Mathf.Rad2Deg;
            _riderPrev3 = reiter;
            var t = _horse.transform;
            t.position = Pt(reiter);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, _riderYaw3, 0f), _camSnap ? 1f : 1f - Mathf.Exp(-12f * dt));
            var tempo = (float)g.Tune.ReiterTempo;
            var gait = speed > tempo * 0.7f ? "galopp" : speed > 0.3f ? "trab" : "stand";
            var ziel = RideFeel.Tempo.TryGetValue(gait, out var zt) ? zt : 0.0;
            _horse.Gait(gait, ziel > 0.01 ? speed / (float)ziel : 1f);
            if (gait != "galopp" || ReducedMotion) return;
            _dustT -= dt;
            if (_dustT > 0f) return;
            _dustT = 0.12f;
            var back = -(t.rotation * Vector3.forward);
            Effects.Puff(t.position + back * 1.1f + Vector3.up * 0.12f, new Color(0.87f, 0.8f, 0.68f, 0.6f), 2, 0.4f, 0.8f, 0.6f, back + Vector3.up * 0.7f, 0.5f);
        }

        private void UpdateFlag(RanchHerdeGame g, bool playing, float dt)
        {
            var show = playing && g.Phase == RanchHerdeGame.PhasePlay && (g.Ziel - g.Reiter).Length > 0.6f;
            _flag.gameObject.SetActive(show);
            if (!show) return;
            _flag.position = Pt(g.Ziel);
            _pulseT = Mathf.Max(0f, _pulseT - dt * 2.6f);
            _pulse.gameObject.SetActive(_pulseT > 0f);
            var s = 1f + (1f - _pulseT) * 1.6f;
            _pulse.localScale = new Vector3(s, 1f, s);
        }

        /// <summary>A finger on the meadow: the flag drops in with a pulse ring (the simulation picks the herding point).</summary>
        private void StageTap() => _pulseT = 1f;

        /// <summary>Panel point → meadow metres through the stage camera (the 2D mapping when there is no stage).</summary>
        private V2 ToWorld3D(Vector3 panel)
        {
            var local = Field.WorldToLocal(panel);
            var ground = _stage.GroundPoint(local.x * ViewW / Mathf.Max(1f, FieldW), local.y * ViewH / Mathf.Max(1f, FieldH));
            return new V2(ground.x - World.MgStage3D.Origin.x, -(ground.z - World.MgStage3D.Origin.z));
        }

        private void UpdateRiderMood(float dt)
        {
            if (_rider == null) return;
            if (_riderBackIn > 0f && (_riderBackIn -= dt) <= 0f) _rider.Play("sit-loop", 0.2f);
            if (_emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) _rider.SetEmotion("happy");
        }

        private void StageSheepIn(bool last)
        {
            _cheer = 1f;
            _cheerBig = last;
            if (_rider == null) return;
            _rider.SetEmotion("ecstatic");
            _emoteLeft = 1.2f;
        }

        private void StageEnd(bool won)
        {
            if (_rider == null) return;
            if (won)
            {
                _rider.SetEmotion("ecstatic");
                _rider.Play("celebrate", 0.15f);
                _riderBackIn = 1.4f;
                _cheer = 1f;
                _cheerBig = true;
            }
            else _rider.SetEmotion("sad");
            _emoteLeft = 2.5f;
        }

        private Vector2 StageToField(V2 world, float y) => _stage.ToField(Pt(world, y), FieldW, FieldH);

        private void DropStage()
        {
            if (_stage == null) return;
            _stands?.Destroy();
            _stands = null;
            _stage.Destroy();
            _stage = null;
            _sheep.Clear();
            _horse = null;
            _rider = null;
            _paddock = null;
            _builtLevel = null;
            _builtFlock = null;
        }
    }
}
