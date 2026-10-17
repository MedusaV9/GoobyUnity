using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D garden of the Schneckenpost (port of snail_mail_stage3d.gd): little cottages and a burrow, puddles, flowers
    /// and the red mailbox stand on a mowed summer lawn (stripes!) with a fence, hedge and trees at its far end; the
    /// post-snail crawls the drawn path as a 3D figure with an envelope on its shell, the path itself lies on the grass as a
    /// sandy ribbon, and the player's Gooby waits at the mailbox and waves. Every anchor comes from the 2D view (the
    /// engine's WorldToView) and is laid on the ground with MgStage3D.GroundPoint, so the path lies exactly under the
    /// finger and input stays as it was.
    /// </summary>
    public sealed partial class SnailMailScreen
    {
        private World.MgStage3D _stage;
        private SnailMail.Level _builtLevel;
        private int _builtTarget = -1;
        private readonly List<Transform> _levelPieces = new List<Transform>();
        private readonly List<Transform> _flowers3d = new List<Transform>();
        private Transform _snail, _snailShell, _beacon, _mailbox;
        private MeshFilter _ribbon;
        private Mesh _ribbonMesh;
        private int _ribbonCount = -1;
        private SnailMail.SmoothPath _ribbonPath;
        private readonly List<Vector3> _ribbonVerts = new List<Vector3>();
        private readonly List<int> _ribbonTris = new List<int>();
        private float _beaconPulse;

        private static readonly string[] Cottages = { "cottage", "cottage_blue", "cottage_pink", "cottage_green" };
        private static readonly float[] CottageWidth = { 9.0f, 7.6f, 7.2f, 8.4f };

        protected override bool StageHasGooby => _stage != null;

        /// <summary>Engine point → the lawn under its 2D spot.</summary>
        private Vector3 G(double x, double y, float lift = 0f)
        {
            var v = WorldToView(x, y);
            var p = _stage.GroundPoint(v.x * ViewW / Mathf.Max(1f, FieldW), v.y * ViewH / Mathf.Max(1f, FieldH), 0f);
            return p + Vector3.up * lift;
        }

        /// <summary>Ground metres per engine unit around an engine point (perspective: the far end of the lawn is bigger).</summary>
        private float MetresPerUnit(double x, double y) => Vector3.Distance(G(x - 0.25, y), G(x + 0.25, y)) * 2f;

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.62f, 0.84f, 0.98f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 9.5f, -8f), 46f, 46f);
            var root = _stage.Root.transform;
            // the lawn with mowed stripes running away from the camera
            _stage.Ground(0f, World.ArtLibrary.Hex("#A8D894"), "grass", 60f, 70f, 16f);
            var stripe = World.ArtLibrary.Pbr("grass", 2.4f, World.ArtLibrary.Hex("#96CC84"), 0.05f);
            for (var i = -6; i <= 6; i += 2)
                World.ArtLibrary.Box(root, "MowStripe", World.MgStage3D.At(i * 0.9f, 0.003f, 16f), new Vector3(0.9f, 0.006f, 70f), stripe, false);
            // the far end of the lawn: picket fence, hedge and round trees, a few bushes and clover round the field
            var farZ = _stage.GroundPoint(ViewW * 0.5, ViewH * 0.1, 0f).z - World.MgStage3D.Origin.z;
            _stage.Fence("picket_fence", 0f, farZ, -9f, 9f, 1.9f, 0.9f);
            _stage.Row("hedge_long", 0f, farZ + 1.3f, -8f, 8f, 4, 1f, 0f, 0.2f, 13);
            foreach (var (x, z, s) in new[] { (-3.2f, 3.5f, 1.5f), (2.8f, 4.2f, 1.7f), (-7f, 4f, 1.6f), (6.8f, 3.4f, 1.5f) })
                _stage.PlaceScenery("tree_round", World.MgStage3D.At(x, 0f, farZ + z), x * 23f, s);
            var nearL = _stage.GroundPoint(ViewW * 0.03, ViewH * 0.9, 0f);
            var nearR = _stage.GroundPoint(ViewW * 0.97, ViewH * 0.9, 0f);
            _stage.PlaceScenery("bush_flowers", nearL + Vector3.left * 0.4f, 30f, 0.8f);
            _stage.PlaceScenery("bush_flowers", nearR + Vector3.right * 0.4f, -40f, 0.75f);
            _stage.Meadow(0f, -4.5f, 4.5f, farZ - 1.5f, farZ - 0.4f, 18, 10, 41);
            // the mailbox and the snail; the path ribbon; the beacon over the target house
            var post = G(_game.Tune.PostX, _game.Tune.PostY);
            _mailbox = _stage.PlaceScenery("mailbox_red", post, 180f, 0.95f);
            BuildSnail(root);
            var ribbonGo = new GameObject("PathRibbon");
            ribbonGo.transform.SetParent(root, false);
            _ribbon = ribbonGo.AddComponent<MeshFilter>();
            _ribbonMesh = new Mesh { name = "SnailPath" };
            _ribbonMesh.MarkDynamic();
            _ribbon.sharedMesh = _ribbonMesh;
            var rr = ribbonGo.AddComponent<MeshRenderer>();
            rr.sharedMaterial = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F2D79A"), 0.15f);
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beacon = new GameObject("Beacon").transform;
            _beacon.SetParent(root, false);
            var beam = _stage.Cone(Vector3.zero, 0.35f, 2.2f, World.ArtLibrary.Transparent(new Color(1f, 0.9f, 0.45f, 0.22f))).transform;
            beam.SetParent(_beacon, false);
            beam.localPosition = Vector3.zero;
            var star = World.ArtLibrary.Sphere(_beacon, "Star", Vector3.zero, 0.09f, World.ArtLibrary.Glow(new Color(1.6f, 1.3f, 0.5f)), false).transform;
            star.localPosition = new Vector3(0f, 1.1f, 0f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnPostman(post + new Vector3(0.75f, 0f, -0.1f), outfit);
        }

        private async System.Threading.Tasks.Task SpawnPostman(Vector3 at, Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(at, 0.68f, outfit);
            if (g == null || _stage == null) return;
            g.transform.rotation = Quaternion.Euler(0f, 160f, 0f);
            g.Play("wave", 0.1f);
            g.SetEmotion("happy");
        }

        /// <summary>The post-snail: a soft body with eye stalks, a spiral shell and a red-sealed envelope riding on it.</summary>
        private void BuildSnail(Transform root)
        {
            _snail = new GameObject("Snail").transform;
            _snail.SetParent(root, false);
            var skin = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#E8C79A"), 0.45f);
            var body = World.ArtLibrary.Sphere(_snail, "Body", Vector3.zero, 0.5f, skin).transform;
            body.localPosition = new Vector3(0f, 0.05f, 0.02f);
            body.localScale = new Vector3(0.14f, 0.09f, 0.34f);
            var head = World.ArtLibrary.Sphere(_snail, "Head", Vector3.zero, 0.5f, skin).transform;
            head.localPosition = new Vector3(0f, 0.1f, 0.17f);
            head.localScale = Vector3.one * 0.11f;
            foreach (var s in new[] { -1f, 1f })
            {
                var stalk = World.ArtLibrary.Cylinder(_snail, "Stalk", Vector3.zero, 0.008f, 0.1f, skin, false).transform;
                stalk.localPosition = new Vector3(s * 0.03f, 0.18f, 0.19f);
                stalk.localRotation = Quaternion.Euler(-20f, 0f, s * -15f);
                var eye = World.ArtLibrary.Sphere(_snail, "Eye", Vector3.zero, 0.018f, World.ArtLibrary.Solid(new Color(0.15f, 0.12f, 0.12f), 0.6f), false).transform;
                eye.localPosition = new Vector3(s * 0.045f, 0.23f, 0.21f);
            }
            _snailShell = new GameObject("Shell").transform;
            _snailShell.SetParent(_snail, false);
            _snailShell.localPosition = new Vector3(0f, 0.15f, -0.05f);
            var shell = World.ArtLibrary.Sphere(_snailShell, "ShellBall", Vector3.zero, 0.5f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#C07A4A"), 0.5f)).transform;
            shell.localPosition = Vector3.zero;
            shell.localScale = new Vector3(0.13f, 0.2f, 0.2f);
            var spiral = _stage.Torus(_snailShell, "Spiral", Vector3.zero, 0.06f, 0.014f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8E5432"), 0.4f)).transform;
            spiral.localPosition = new Vector3(0.07f, 0f, 0f);
            spiral.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var envelope = World.ArtLibrary.Box(_snailShell, "Envelope", Vector3.zero, new Vector3(0.16f, 0.015f, 0.11f), World.ArtLibrary.Solid(new Color(0.98f, 0.96f, 0.92f), 0.2f)).transform;
            envelope.localPosition = new Vector3(0f, 0.11f, 0f);
            envelope.localRotation = Quaternion.Euler(0f, 0f, 8f);
            var seal = World.ArtLibrary.Sphere(envelope, "Seal", Vector3.zero, 0.5f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#DC383D"), 0.4f), false).transform;
            seal.localPosition = new Vector3(0f, 0.6f, 0f);
            seal.localScale = new Vector3(0.18f, 0.8f, 0.26f);
        }

        /// <summary>Rebuilds houses, puddles and flowers when the level (or its target) changes.</summary>
        private void BuildLevel(SnailMail.Level lvl)
        {
            foreach (var t in _levelPieces) if (t != null) Object.Destroy(t.gameObject);
            _levelPieces.Clear();
            _flowers3d.Clear();
            _builtLevel = lvl;
            _builtTarget = lvl.TargetIdx;
            var root = _stage.Root.transform;
            for (var i = 0; i < lvl.Houses.Count; i++)
            {
                var h = lvl.Houses[i];
                var at = G(h.X, h.Y);
                var mpu = MetresPerUnit(h.X, h.Y);
                if (h.Kind == "burrow")
                {
                    var mound = World.ArtLibrary.Sphere(root, "Burrow", at, 0.5f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#B08A62"), 0.15f)).transform;
                    mound.localScale = new Vector3(0.62f, 0.36f, 0.5f) * mpu;
                    var hole = World.RanchDressing.Ring(root, "Hole", 0f, 0.14f * mpu, World.ArtLibrary.Solid(new Color(0.22f, 0.15f, 0.1f), 0.1f));
                    hole.position = at + new Vector3(0f, 0.11f * mpu, -0.22f * mpu);
                    hole.rotation = Quaternion.Euler(-70f, 0f, 0f);
                    _levelPieces.Add(mound);
                    _levelPieces.Add(hole);
                    continue;
                }
                var kind = i % Cottages.Length;
                var house = _stage.PlaceScenery(Cottages[kind], at, 180f, 0.56f * 1.35f * mpu / CottageWidth[kind]);
                _levelPieces.Add(house);
            }
            foreach (var pd in lvl.Puddles)
            {
                var at = G(pd.X, pd.Y, 0.012f);
                var mpu = MetresPerUnit(pd.X, pd.Y);
                var water = World.RanchDressing.Ring(root, "Puddle", 0f, (float)pd.R * mpu, World.ArtLibrary.Solid(new Color(0.42f, 0.66f, 0.9f), 0.92f));
                water.position = at;
                water.localScale = new Vector3(1f, 1f, 0.78f);
                var shine = World.RanchDressing.Ring(root, "Shine", 0f, (float)pd.R * mpu * 0.3f, World.ArtLibrary.Transparent(new Color(0.85f, 0.95f, 1f, 0.5f)));
                shine.position = at + new Vector3(-(float)pd.R * mpu * 0.25f, 0.004f, (float)pd.R * mpu * 0.2f);
                _levelPieces.Add(water);
                _levelPieces.Add(shine);
            }
            string[] kinds = { "flower_redA", "flower_yellowA", "flower_purpleA" };
            for (var i = 0; i < lvl.Flowers.Count; i++)
            {
                var f = lvl.Flowers[i];
                var flower = _stage.Prop(kinds[i % kinds.Length], G(f.X, f.Y), 2.6f, i * 63f);
                _levelPieces.Add(flower);
                _flowers3d.Add(flower);
            }
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            if (g.Level != _builtLevel || g.Level.TargetIdx != _builtTarget) BuildLevel(g.Level);
            for (var i = 0; i < _flowers3d.Count; i++) _flowers3d[i].gameObject.SetActive(!g.Picked.Contains(i));
            // the beacon over the target house
            var lvl = g.Level;
            if (lvl.TargetIdx >= 0 && lvl.TargetIdx < lvl.Houses.Count)
            {
                var h = lvl.Houses[lvl.TargetIdx];
                _beaconPulse += dt;
                var mpu = MetresPerUnit(h.X, h.Y);
                _beacon.gameObject.SetActive(true);
                _beacon.position = G(h.X, h.Y) + Vector3.up * (0.6f * mpu + (ReducedMotion ? 0f : Mathf.Sin(_beaconPulse * 3f) * 0.05f));
                _beacon.localScale = Vector3.one * mpu * 0.55f;
                _beacon.rotation = Quaternion.Euler(180f, _beaconPulse * 50f, 0f);
            }
            else _beacon.gameObject.SetActive(false);
            UpdateRibbon();
            // the snail crawls (blinks while it retreats after a splash)
            var hide = g.Phase == SnailMailGame.PhaseRetreat && (int)(g.RetreatLeft * 8.0) % 2 == 1;
            _snail.gameObject.SetActive(!hide);
            var at = G(g.SnailX, g.SnailY);
            var ahead = G(g.SnailX + System.Math.Cos(g.SnailAngle) * 0.05, g.SnailY + System.Math.Sin(g.SnailAngle) * 0.05);
            var dir = ahead - at;
            dir.y = 0f;
            _snail.position = at;
            if (dir.sqrMagnitude > 1e-8f) _snail.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var crawl = g.Phase == SnailMailGame.PhaseFollow && !ReducedMotion ? Mathf.Sin(_show * 9f) * 0.06f : 0f;
            _snail.localScale = new Vector3(1f, 1f, 1f + crawl) * MetresPerUnit(g.SnailX, g.SnailY) * 0.9f;
            var gb = _stage.Gooby;
            if (gb != null && g.Phase == SnailMailGame.PhaseFollow)
            {
                var to = at - gb.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) gb.transform.rotation = Quaternion.Slerp(gb.transform.rotation, Quaternion.LookRotation(to.normalized), Mathf.Min(1f, dt * 4f));
            }
        }

        /// <summary>The drawn path as a sandy ribbon on the lawn — rebuilt only when the path changes.</summary>
        private void UpdateRibbon()
        {
            var g = _game;
            var pts = PreviewPts();
            var count = pts?.Count ?? 0;
            if (g.Path == _ribbonPath && count == _ribbonCount) return;
            _ribbonPath = g.Path;
            _ribbonCount = count;
            _ribbonVerts.Clear();
            _ribbonTris.Clear();
            _ribbonMesh.Clear();
            if (count < 2) return;
            var o = _stage.Root.transform;
            var prev = Vector3.zero;
            for (var i = 0; i < count; i++)
            {
                var p = G(pts[i].X, pts[i].Y, 0.015f);
                var next = i + 1 < count ? G(pts[i + 1].X, pts[i + 1].Y, 0.015f) : p + (p - prev);
                var back = i > 0 ? prev : p - (next - p);
                var t = next - back;
                t.y = 0f;
                var side = t.sqrMagnitude > 1e-8f ? Vector3.Cross(Vector3.up, t.normalized) : Vector3.right;
                var w = 0.07f * MetresPerUnit(pts[i].X, pts[i].Y);
                _ribbonVerts.Add(o.InverseTransformPoint(p - side * w));
                _ribbonVerts.Add(o.InverseTransformPoint(p + side * w));
                if (i > 0)
                {
                    var k = i * 2;
                    _ribbonTris.AddRange(new[] { k - 2, k, k - 1, k - 1, k, k + 1 });
                }
                prev = p;
            }
            _ribbonMesh.SetVertices(_ribbonVerts);
            _ribbonMesh.SetTriangles(_ribbonTris, 0);
            _ribbonMesh.RecalculateNormals();
            _ribbonMesh.RecalculateBounds();
        }

        private void StageSplash()
        {
            if (_stage == null) return;
            if (!ReducedMotion) World.Effects.Puff(_snail.position + Vector3.up * 0.1f, new Color(0.55f, 0.75f, 0.95f, 0.9f), 14, 0.08f, 1.4f, 0.5f, Vector3.up, 1f);
            _stage.Gooby?.SetEmotion("scared");
        }

        private void StageFlower()
        {
            if (_stage == null || ReducedMotion) return;
            World.Effects.Sparkle(_snail.position + Vector3.up * 0.15f, 0.2f, 10);
        }

        private void StageDelivered(bool dry)
        {
            if (_stage == null) return;
            if (!ReducedMotion) World.Effects.Sparkle(_snail.position + Vector3.up * 0.2f, 0.4f, dry ? 22 : 10);
            var gb = _stage.Gooby;
            if (gb == null) return;
            gb.Play(dry ? "celebrate" : "wave", 0.1f);
            gb.SetEmotion(dry ? "ecstatic" : "happy");
        }

        private void DropStage()
        {
            _levelPieces.Clear(); _flowers3d.Clear();
            _builtLevel = null;
            _builtTarget = _ribbonCount = -1;
            _ribbonPath = null;
            if (_ribbonMesh != null) Object.Destroy(_ribbonMesh);
            _ribbonMesh = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
