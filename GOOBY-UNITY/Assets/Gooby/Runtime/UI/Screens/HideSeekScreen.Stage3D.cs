using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D garden of Guck-guck-Garten (port of hide_seek_garden3d.gd): a steep mown hillside seen from a flat camera, so
    /// all four rows of hideouts sit at the same depth and size; each hideout (a flowering bush, a stump with a log and a
    /// toadstool, two terracotta pots) stands on a bed of earth ringed with pebbles exactly where its tap cell is — the
    /// cell centres are cast onto the slope and the props scale with the projected cell. Bunnies, mice and bears in pastel
    /// pop out of the ground when they peek (a golden flash calls the eye), look around and blink, and hop for joy when
    /// found; a tap puffs leaves. Over the ridge: a picket fence, flowering bushes, round trees, hills and clouds, two
    /// butterflies over the beds. The player's Gooby is the seeker, big in the foreground, his face following the round.
    /// The camera floats down from a raised view during the intro.
    /// </summary>
    public sealed partial class HideSeekScreen
    {
        private World.MgStage3D _stage;
        private readonly List<Transform> _holders = new List<Transform>();
        private readonly List<Transform> _props3d = new List<Transform>();
        private readonly List<Transform> _critters3d = new List<Transform>();
        private readonly List<Transform[]> _eyes3d = new List<Transform[]>();
        private readonly List<(Transform node, Vector3 home, float phase, Transform left, Transform right)> _flies =
            new List<(Transform, Vector3, float, Transform, Transform)>();
        private readonly float[] _hops = new float[12];
        private readonly bool[] _peeked = new bool[12];
        private string _mood = "";
        private float _stageT;
        private bool _settled;

        private const float Slope = 1.35f, SlopeBase = -0.65f, BedTopZ = 3.9f, BedLen = 13f, CellRef = 1.75f, HopSec = 0.7f, CritterScale = 1.2f;
        private const float CamPitchDeg = 16f, CamFovDeg = 51.4f, IntroPitchDeg = 7f;
        private static readonly Vector3 CamPos = new Vector3(0f, 3.6f, -12f);
        private static readonly Vector3 IntroLift = new Vector3(0f, 2.4f, -3.4f);
        private static readonly Color[] CritterTints =
        {
            new Color(1f, 0.66f, 0.78f), new Color(0.5f, 0.83f, 0.76f), new Color(1f, 0.85f, 0.48f), new Color(0.73f, 0.66f, 1f), new Color(1f, 0.69f, 0.54f),
        };

        protected override bool StageHasGooby => _stage != null;

        private static float SlopeAngle => Mathf.Atan(Slope) * Mathf.Rad2Deg;

        /// <summary>The slope's height at a stage-local depth (it rises away from the camera).</summary>
        private static float SlopeY(float z) => Slope * z + SlopeBase;

        /// <summary>A view point (px, y down) cast onto the hillside (stage world).</summary>
        private Vector3 SlopePoint(double vx, double vy)
        {
            var ray = _stage.Camera.ViewportPointToRay(new Vector3((float)vx / (float)ViewW, 1f - (float)vy / (float)ViewH, 0f));
            var o = ray.origin - World.MgStage3D.Origin;
            var d = ray.direction;
            var denom = d.y - Slope * d.z;
            if (Mathf.Abs(denom) < 0.0005f) return ray.origin;
            var t = (SlopeBase - o.y + Slope * o.z) / denom;
            return ray.origin + d * Mathf.Clamp(t, 0.5f, 90f);
        }

        /// <summary>The hideout centres in view pixels (the host's 3×4 grid at the design size).</summary>
        private static Vector2 ViewSpot(int spot)
        {
            var col = spot % 3; var row = spot / 3;
            var cellW = (float)ViewW * 0.9f / 3f;
            var cellH = (float)ViewH * 0.48f / 4f;
            var originX = ((float)ViewW - cellW * 3f) * 0.5f;
            return new Vector2(originX + (col + 0.5f) * cellW, (float)ViewH * 0.14f + (row + 0.55f) * cellH);
        }

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#8CC6EE"));
            _stage.Bind(Field);
            _stage.Aim(CamPos, CamPitchDeg, CamFovDeg);
            var root = _stage.Root.transform;
            BuildBank(root);
            BuildSkyline(root);
            BuildButterflies(root);
            var cellW = (float)ViewW * 0.9f / 3f;
            var cellH = (float)ViewH * 0.48f / 4f;
            for (var i = 0; i < _game.Spots && i < 12; i++)
            {
                var c = ViewSpot(i);
                var here = SlopePoint(c.x, c.y);
                var side = SlopePoint(c.x + cellW * 0.5f, c.y);
                var below = SlopePoint(c.x, c.y + cellH * 0.5f);
                var span = Mathf.Min(Vector3.Distance(here, side), Vector3.Distance(here, below) * 1.35f);
                BuildSpot(root, i, here, Mathf.Max(0.3f, span) / CellRef);
            }
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnSeeker(outfit);
            Establish();
        }

        // ── the hillside: a tilted lawn with mown stripes, tufts, flowers and toadstools ──

        private void BuildBank(Transform root)
        {
            var ang = SlopeAngle;
            var rad = ang * Mathf.Deg2Rad;
            var top = World.MgStage3D.At(0f, SlopeY(BedTopZ), BedTopZ);
            var down = new Vector3(0f, -Mathf.Sin(rad), -Mathf.Cos(rad));
            var normal = new Vector3(0f, Mathf.Cos(rad), -Mathf.Sin(rad));
            var tilt = Quaternion.Euler(-ang, 0f, 0f);
            var lawn = World.ArtLibrary.Box(root, "Bank", top + down * (BedLen * 0.5f) - normal * 0.05f, new Vector3(70f, 0.1f, BedLen),
                World.ArtLibrary.PbrSlab("grass", new Vector2(70f, BedLen), 2.4f, World.ArtLibrary.Hex("#7FBD6B"), 0.04f), false).transform;
            lawn.rotation = tilt;
            var stripes = new GameObject("Stripes").transform;
            stripes.SetParent(root, false);
            var light = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#93CC78"), 0.04f);
            for (var i = 0; i < 12; i++)
            {
                var s = World.ArtLibrary.Box(stripes, "Stripe", top + down * (BedLen * 0.5f) + normal * 0.012f + Vector3.right * (-19.25f + i * 3.5f), new Vector3(1.45f, 0.01f, BedLen), light, false).transform;
                s.rotation = tilt;
            }
            _stage.MergeChildren(stripes, "Stripes");
            // tufts and flowers on the slope (none in the seeker's corner, they would tower in the near plane)
            var rng = new System.Random(4711);
            var tuft = World.ArtLibrary.Solid(new Color(0.36f, 0.6f, 0.3f), 0.05f);
            var tufts = new GameObject("Tufts").transform;
            tufts.SetParent(root, false);
            for (var i = 0; i < 70; i++)
            {
                var x = Mathf.Lerp(-9f, 9f, (float)rng.NextDouble());
                var z = Mathf.Lerp(-3.4f, 3.7f, (float)rng.NextDouble());
                var h = 0.16f + (float)rng.NextDouble() * 0.2f;
                _stage.Cone(World.MgStage3D.At(x, SlopeY(z) - 0.03f, z), 0.06f + h * 0.22f, h, tuft).transform.SetParent(tufts, true);
            }
            _stage.MergeChildren(tufts, "Tufts");
            string[] flowers = { "flower_redA", "flower_yellowA", "flower_purpleA" };
            for (var i = 0; i < 26; i++)
            {
                var x = Mathf.Lerp(-6.5f, 6.5f, (float)rng.NextDouble());
                var z = Mathf.Lerp(-3.3f, 3.6f, (float)rng.NextDouble());
                if (x < -1.2f && z < -1.6f) continue;
                _stage.PropFit(flowers[i % flowers.Length], World.MgStage3D.At(x, SlopeY(z) - 0.06f, z), 0.32f + (float)rng.NextDouble() * 0.14f, (float)rng.NextDouble() * 360f);
            }
            for (var i = 0; i < 6; i++)
            {
                var x = Mathf.Lerp(-5.5f, 5.5f, (float)rng.NextDouble());
                var z = Mathf.Lerp(-2.6f, 3.4f, (float)rng.NextDouble());
                _stage.PropFit("mushroom_red", World.MgStage3D.At(x, SlopeY(z) - 0.04f, z), 0.3f, (float)rng.NextDouble() * 360f);
            }
            // hedge borders down both sides of the beds
            foreach (var sx in new[] { -3.25f, 3.25f })
                for (var z = -2.6f; z <= 3.5f; z += 0.85f)
                    _stage.PropFit("plant_bush", World.MgStage3D.At(sx + Mathf.Sin(z * 3.1f) * 0.12f, SlopeY(z) - 0.1f, z), 1.0f, z * 70f);
        }

        // ── over the ridge: a plateau with a picket fence, flowering bushes, round trees, a bird; hills, clouds and the sun ──

        private void BuildSkyline(Transform root)
        {
            var ridgeY = SlopeY(BedTopZ);
            World.ArtLibrary.Box(root, "Plateau", World.MgStage3D.At(0f, ridgeY - 0.5f, BedTopZ + 25f), new Vector3(110f, 1f, 50f),
                World.ArtLibrary.PbrSlab("grass", new Vector2(110f, 50f), 3f, World.ArtLibrary.Hex("#8CC878"), 0.04f), false);
            _stage.Fence("picket_fence", ridgeY, BedTopZ + 0.7f, -14f, 14f, 1.9f, 1f);
            _stage.Row("bush_flowers", ridgeY, BedTopZ + 1.6f, -10f, 10f, 6, 1.1f, 0f, 0.6f, 31);
            _stage.Row("tree_round_big", ridgeY, BedTopZ + 5f, -13f, 13f, 6, 1.15f, 0f, 1.2f, 32);
            _stage.Row("tree_round", ridgeY, BedTopZ + 9f, -18f, 18f, 8, 1.25f, 0f, 1.5f, 33);
            _stage.PropFit("bird", World.MgStage3D.At(-1.7f, ridgeY + 1.02f, BedTopZ + 0.7f), 0.42f, 140f);
            _stage.Hills(ridgeY, 34f, 6, World.ArtLibrary.Hex("#A6D3B4"), 6f, 90f, 0f, 34);
            _stage.Clouds(5, ridgeY + 9f, 44f, 44f, 2.4f, 35);
            _stage.Sun(new Vector3(7f, ridgeY + 14f, 60f), 2.6f);
        }

        private void BuildButterflies(Transform root)
        {
            _flies.Clear();
            foreach (var (tint, home, phase) in new[] { (new Color(1f, 0.72f, 0.3f), new Vector3(-1.6f, 3.2f, 1.6f), 0f), (new Color(0.72f, 0.6f, 1f), new Vector3(1.8f, 2.2f, 0.4f), 2.6f) })
            {
                var node = new GameObject("Butterfly").transform;
                node.SetParent(root, false);
                node.position = World.MgStage3D.At(home.x, home.y, home.z);
                var mat = World.ArtLibrary.Solid(tint, 0.2f);
                Transform Wing(float side)
                {
                    var hinge = new GameObject("Wing").transform;
                    hinge.SetParent(node, false);
                    var w = World.ArtLibrary.Sphere(hinge, "Membrane", node.position, 0.5f, mat, false).transform;
                    w.localPosition = new Vector3(side * 0.2f, 0f, 0f);
                    w.localScale = new Vector3(0.4f, 0.02f, 0.32f);
                    return hinge;
                }
                World.ArtLibrary.Sphere(node, "Body", node.position, 0.05f, World.ArtLibrary.Solid(new Color(0.3f, 0.22f, 0.2f), 0.2f), false).transform.localScale = new Vector3(0.06f, 0.06f, 0.26f);
                _flies.Add((node, home, phase, Wing(-1f), Wing(1f)));
            }
        }

        // ── a hideout: bed, pebble ring, prop and its critter, all under one holder scaled to the cell ──

        private void BuildSpot(Transform root, int index, Vector3 at, float scale)
        {
            var holder = new GameObject("Spot" + index).transform;
            holder.SetParent(root, false);
            holder.position = at;
            holder.localScale = Vector3.one * scale;
            _holders.Add(holder);
            var tilt = Quaternion.Euler(-SlopeAngle, 0f, 0f);
            var bed = World.ArtLibrary.Cylinder(holder, "Bed", at, 0.86f, 0.12f, World.ArtLibrary.Solid(new Color(0.53f, 0.4f, 0.29f), 0.05f), false).transform;
            bed.localPosition = new Vector3(0f, 0.04f, -0.04f);
            bed.localScale = new Vector3(1.72f, 0.06f, 1.72f);
            bed.rotation = tilt;
            var rng = new System.Random(7300 + index);
            for (var i = 0; i < 9; i++)
            {
                var around = Mathf.PI * 2f * (i + (float)rng.NextDouble() * 0.35f) / 9f;
                var r = 0.98f + (float)rng.NextDouble() * 0.1f;
                var local = tilt * new Vector3(Mathf.Cos(around) * r, 0.02f, Mathf.Sin(around) * r) + new Vector3(0f, 0f, -0.04f);
                _stage.PropFit("rock_smallA", holder.TransformPoint(local), 0.3f * (0.75f + (float)rng.NextDouble() * 0.45f), (float)rng.NextDouble() * 360f, false, holder);
            }
            var critter = BuildCritter(holder, index);
            _critters3d.Add(critter);
            var prop = new GameObject("Prop").transform;
            prop.SetParent(holder, false);
            prop.localPosition = new Vector3(0f, -0.2f, 0f);
            _props3d.Add(prop);
            // the game's own garden pieces (Blender, 1 unit = 1 m, front towards +z): a flowering bush, a stump with a log
            // and a toadstool, the slatted crate with the bunny badge turned to the camera
            switch ((index + index / 3) % 3)
            {
                case 0:
                    World.Scenery.Place(prop, "bush_flowers", new Vector3(0f, 0.12f, 0.05f), index * 40f, 1f);
                    break;
                case 1:
                    World.Scenery.Place(prop, "tree_stump", new Vector3(0f, 0.15f, 0.05f), index * 25f, 2f);
                    _stage.PropFit("log", prop.TransformPoint(new Vector3(0.7f, 0.12f, -0.42f)), 1.1f, -28f, false, prop);
                    _stage.PropFit("mushroom_red", prop.TransformPoint(new Vector3(-0.66f, 0.12f, -0.42f)), 0.45f, 30f, false, prop);
                    break;
                default:
                    World.Scenery.Place(prop, "garden_crate", new Vector3(0f, 0.15f, 0.1f), 180f + (index % 2 == 0 ? 12f : -12f), 1.55f);
                    _stage.PropFit("pot_small", prop.TransformPoint(new Vector3(0.82f, 0.1f, -0.5f)), 0.6f, 12f, false, prop);
                    break;
            }
        }

        /// <summary>A pastel critter in its hideout's frame: bunny (long ears, cotton tail), mouse (dish ears, string tail) or bear (button ears, snout).</summary>
        private Transform BuildCritter(Transform holder, int index)
        {
            var c = new GameObject("Critter").transform;
            c.SetParent(holder, false);
            c.localPosition = new Vector3(0f, -1f, -0.72f);
            c.localScale = Vector3.one * CritterScale;
            var tint = CritterTints[index % CritterTints.Length];
            var fur = World.ArtLibrary.Solid(tint, 0.15f);
            var bright = World.ArtLibrary.Solid(Color.Lerp(tint, Color.white, 0.4f), 0.15f);
            Transform Blob(string name, Material m, Vector3 local, Vector3 size)
            {
                var b = World.ArtLibrary.Sphere(c, name, c.position, 0.5f, m, false).transform;
                b.localPosition = local;
                b.localScale = size;
                return b;
            }
            Blob("Body", fur, new Vector3(0f, 0.4f, 0f), new Vector3(0.92f, 0.8f, 0.92f));
            Blob("Head", fur, new Vector3(0f, 0.98f, -0.04f), new Vector3(0.68f, 0.66f, 0.68f));
            var eyeMat = World.ArtLibrary.Solid(new Color(0.16f, 0.12f, 0.18f), 0.5f);
            var eyes = new[] { Blob("Eye", eyeMat, new Vector3(-0.14f, 1.04f, -0.31f), Vector3.one * 0.14f), Blob("Eye", eyeMat, new Vector3(0.14f, 1.04f, -0.31f), Vector3.one * 0.14f) };
            _eyes3d.Add(eyes);
            switch ((index + 2 * (index / 3)) % 3)
            {
                case 0:
                    foreach (var s in new[] { -1f, 1f })
                        Blob("Ear", fur, new Vector3(s * 0.18f, 1.4f, 0.02f), new Vector3(0.2f, 0.7f, 0.2f)).localRotation = Quaternion.Euler(0f, 0f, -s * 13f);
                    Blob("Tail", bright, new Vector3(0f, 0.48f, 0.42f), Vector3.one * 0.3f);
                    break;
                case 1:
                    foreach (var s in new[] { -1f, 1f }) Blob("Ear", fur, new Vector3(s * 0.26f, 1.3f, 0.02f), new Vector3(0.34f, 0.34f, 0.15f));
                    Blob("Tail", fur, new Vector3(0f, 0.32f, 0.5f), new Vector3(0.08f, 0.08f, 0.62f)).localRotation = Quaternion.Euler(-32f, 0f, 0f);
                    break;
                default:
                    foreach (var s in new[] { -1f, 1f }) Blob("Ear", fur, new Vector3(s * 0.2f, 1.34f, 0f), Vector3.one * 0.24f);
                    Blob("Snout", bright, new Vector3(0f, 0.92f, -0.32f), new Vector3(0.26f, 0.2f, 0.26f));
                    Blob("Tail", bright, new Vector3(0f, 0.44f, 0.4f), Vector3.one * 0.2f);
                    break;
            }
            c.gameObject.SetActive(false);
            return c;
        }

        // ── the seeker: big in the foreground at the lower left, 19 % of the view tall ──

        private async System.Threading.Tasks.Task SpawnSeeker(Dictionary<string, string> outfit)
        {
            var anchor = new Vector2((float)ViewW * 0.27f, (float)ViewH * 0.905f);
            var feet = SlopePoint(anchor.x, anchor.y);
            var g = await _stage.SpawnGoobyAsync(feet, 1f, outfit);
            if (g == null || _stage == null) return;
            // measure the rig at scale 1, then size it so it stands 19 % of the view tall on the camera's final pose
            var height = 1f;
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                height = Mathf.Max(0.3f, b.size.y);
            }
            var cam = _stage.Camera.transform;
            var keepPos = cam.position; var keepRot = cam.rotation;
            _stage.Aim(CamPos, CamPitchDeg, CamFovDeg);
            var a = _stage.Camera.WorldToViewportPoint(feet);
            var b1 = _stage.Camera.WorldToViewportPoint(feet + Vector3.up);
            cam.position = keepPos; cam.rotation = keepRot;
            var pxPerMetre = Mathf.Max(1f, (b1.y - a.y) * (float)ViewH);
            g.transform.localScale = Vector3.one * ((float)ViewH * 0.19f / pxPerMetre / height);
            g.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            g.Play("idle-loop");
            _mood = "";
        }

        // ── every frame ──

        private void Establish()
        {
            // the intro floats the camera down from a raised view of the garden into the seeker's pose
            var k = 1f - Mathf.Clamp01((float)(_game.IntroLeft / HideSeekGame.IntroSec));
            var e = ReducedMotion ? 0f : (1f - k) * (1f - k);
            _stage.Aim(CamPos + IntroLift * e, CamPitchDeg + IntroPitchDeg * e, CamFovDeg);
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            var g = _game;
            if (!_settled)
            {
                Establish();
                _settled = g.IntroLeft <= 0.0;
            }
            for (var i = 0; i < _critters3d.Count; i++)
            {
                var rise = (float)g.RiseOf(i);
                var peeking = g.IsPeeking(i);
                if (peeking && !_peeked[i] && !ReducedMotion) World.Effects.Sparkle(_holders[i].TransformPoint(new Vector3(0f, 1.5f, -0.9f)), 0.35f, 12);
                _peeked[i] = peeking;
                if (_hops[i] > 0f) _hops[i] = Mathf.Max(0f, _hops[i] - dt);
                var critter = _critters3d[i];
                var shown = rise > 0.001f;
                if (critter.gameObject.activeSelf != shown) critter.gameObject.SetActive(shown);
                if (shown)
                {
                    var hop = 0f;
                    if (_hops[i] > 0f)
                    {
                        var t = 1f - _hops[i] / HopSec;
                        hop = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * 0.5f * (1f - t * 0.6f);
                    }
                    critter.localPosition = new Vector3(0f, -1.2f + rise * 1.55f + hop, -0.72f);
                    var look = ReducedMotion ? 0f : Mathf.Sin(_stageT * (2.2f + rise * 2.4f) + i) * (0.25f + rise * 0.35f);
                    critter.localRotation = Quaternion.Euler(0f, look * Mathf.Rad2Deg, 0f);
                    var lid = Mathf.Repeat(_stageT * 0.9f + i * 1.37f, 3.1f) < 0.13f ? 0.15f : 1f;
                    foreach (var eye in _eyes3d[i]) eye.localScale = new Vector3(0.14f, 0.14f * lid, 0.14f);
                }
                // a tap wobbles the hideout
                var shake = (float)g.Shake[i];
                _props3d[i].localRotation = Quaternion.Euler(0f, 0f, shake <= 0f ? 0f : Mathf.Sin(shake * 42f) * 0.5f * shake * Mathf.Rad2Deg);
            }
            var gb = _stage.Gooby;
            if (gb != null)
            {
                var mood = Mood();
                if (mood != _mood) { _mood = mood; gb.SetEmotion(mood); }
            }
            foreach (var (node, home, phase, left, right) in _flies)
            {
                var t = _stageT * 0.55f + phase;
                node.position = World.MgStage3D.At(home.x + Mathf.Sin(t) * 1.6f, home.y + Mathf.Sin(t * 2f) * 0.4f, home.z - Mathf.Cos(t) * 0.9f);
                node.rotation = Quaternion.Euler(0f, Mathf.Cos(t) * 50f, 0f);
                var flap = ReducedMotion ? 20f : Mathf.Sin(_stageT * 14f + phase) * 55f;
                left.localRotation = Quaternion.Euler(0f, 0f, flap);
                right.localRotation = Quaternion.Euler(0f, 0f, -flap);
            }
        }

        /// <summary>The seeker's face: glee when a critter peeks, nerves late in a wave, joy or gloom between waves.</summary>
        private string Mood()
        {
            var g = _game;
            if (g.Finished) return "sad";
            if (g.Phase == "serve") return g.HiddenCount == 0 ? "ecstatic" : "sad";
            if (g.WaveT > g.WaveSec * 0.8) return "scared";
            foreach (var c in g.Critters.Values) if (!c.Found && c.PeekT > 0.0) return "ecstatic";
            return "happy";
        }

        // ── moments ──

        private void StageFound(int spot)
        {
            if (spot < 0 || spot >= _holders.Count) return;
            _hops[spot] = HopSec;
            if (!ReducedMotion)
                World.Effects.Puff(_holders[spot].TransformPoint(new Vector3(0f, 0.7f, -0.7f)), CritterTints[spot % CritterTints.Length], 14, 0.13f, 2.4f, 0.8f, Vector3.up, 1.2f);
            _stage.Gooby?.Play("hop", 0.1f);
        }

        private void StageEmpty(int spot)
        {
            if (spot < 0 || spot >= _holders.Count || ReducedMotion) return;
            World.Effects.Puff(_holders[spot].TransformPoint(new Vector3(0f, 0.7f, -0.7f)), new Color(0.62f, 0.78f, 0.46f, 0.9f), 12, 0.12f, 2f, 0.7f, Vector3.up, 1.2f);
        }

        private void StageWaveCleared()
        {
            _stage?.Gooby?.Play("wave", 0.1f);
        }

        private void DropStage()
        {
            _holders.Clear();
            _props3d.Clear();
            _critters3d.Clear();
            _eyes3d.Clear();
            _flies.Clear();
            for (var i = 0; i < _hops.Length; i++) { _hops[i] = 0f; _peeked[i] = false; }
            _mood = "";
            _settled = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
