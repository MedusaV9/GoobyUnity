using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D street-ball cage of Korbwurf (port of basket_bounce.gd's park stage and basket_bounce_scenery.gd): a
    /// terracotta court with cream lines inside a chain-link fence, park lamps, Gooby fans on benches along the back and
    /// round fans standing behind the fence, then a hedge, round trees, hills, clouds and the sun. The hoop is real — pole,
    /// arm, board with its pink target square, an orange rim that flinches when the ball clangs and a net that whips down
    /// on a basket — and backs off and slides with the engine's hoop; the court's lines go with it. The player's Gooby
    /// stands beside the spot, leans back while the finger pulls, swings on the throw and turns round to celebrate. A made
    /// ball drops through the net and bounces before the next one pops onto the spot. The camera reproduces the painted
    /// projection (height, pitch, focal length and the pull-back as the hoop retreats), so the canvas's aim line, trail and
    /// sparks stay on target.
    /// </summary>
    public sealed partial class BasketBounceScreen
    {
        private World.MgStage3D _stage;
        private World.RanchCrowd _fans;
        private Transform _court, _hoop, _rim, _net, _ball, _ballSpin, _ballShadow, _standing;
        private Material _rimMat, _ballMat;
        private readonly List<Material> _ownedMats = new List<Material>();
        private Vector3 _dropPos, _dropVel;
        private float _dropLeft, _popIn = 1f, _goobyYaw = GoobyAimYaw, _hopT = -1f, _hopH, _lean, _fireEmit, _stageT;
        private bool _crowdCue, _crowdAll, _celebrating;

        private const float GoobyScale = 1.15f, GoobyAimYaw = 112f, CourtNear = 14f;
        private const float WindupLeanDeg = 11.5f;
        private static readonly Color RimOrange = new Color(0.94f, 0.35f, 0.22f);
        private static readonly Color BoardPink = new Color(0.95f, 0.45f, 0.66f);

        protected override bool StageHasGooby => _stage != null;

        private static float StageFov => 2f * Mathf.Atan(0.5f / 0.95f) * Mathf.Rad2Deg;

        /// <summary>Engine point (x right, y up, z towards the player) → stage world (the stage's z runs away from the camera).</summary>
        private static Vector3 P3(double x, double y, double z) => World.MgStage3D.At((float)x, (float)y, (float)-z);

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#A3D2F6"));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, (float)CamY, -(float)CamZ), CamPitchDeg, StageFov);
            var root = _stage.Root.transform;
            var tune = _game.Tune;
            _stage.Ground(0f, World.ArtLibrary.Hex("#7DBA6A"), "grass", 90f, 90f, 22f);
            BuildCourt(root, tune);
            BuildCage(root);
            BuildPark();
            BuildHoop(root, tune);
            BuildBall(root, tune);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnShooter(outfit);
        }

        private async System.Threading.Tasks.Task SpawnShooter(Dictionary<string, string> outfit)
        {
            var tune = _game.Tune;
            var g = await _stage.SpawnGoobyAsync(ShooterSpot(tune), GoobyScale, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            g.SetEmotion("happy");
            g.transform.rotation = Quaternion.Euler(0f, GoobyAimYaw, 0f);
        }

        private static Vector3 ShooterSpot(BasketBounce.Tune tune) => P3(tune.SpawnX - 0.64, 0, tune.SpawnZ + 0.16);

        // ── the court: a slab and its lines in hoop-relative coordinates (the court node follows the hoop's depth) ──

        private void BuildCourt(Transform root, BasketBounce.Tune tune)
        {
            _court = new GameObject("Court").transform;
            _court.SetParent(root, false);
            _court.position = P3(0, 0, tune.SpawnZ - tune.DistStart);
            // the slab reaches from 2.2 m behind the hoop to past the bottom of the view, its top 2 cm over the lawn
            const float far = 2.2f;
            var slab = World.ArtLibrary.Box(_court, "Slab", Vector3.zero, Vector3.one,
                World.ArtLibrary.PbrSlab("asphalt", new Vector2(8.4f, far + CourtNear), 2.2f, World.ArtLibrary.Hex("#E4A378"), 0.12f), false).transform;
            slab.localPosition = new Vector3(0f, -0.01f, (far - CourtNear) * 0.5f);
            slab.localScale = new Vector3(8.4f, 0.06f, far + CourtNear);
            var lines = new GameObject("Lines").transform;
            lines.SetParent(_court, false);
            var cream = World.ArtLibrary.Solid(new Color(0.99f, 0.97f, 0.93f), 0.25f);
            void L(float x0, float z0, float x1, float z1)
            {
                var a = _court.position + new Vector3(x0, 0.026f, z0);
                var b = _court.position + new Vector3(x1, 0.026f, z1);
                var seg = World.ArtLibrary.Box(lines, "Line", (a + b) * 0.5f, new Vector3(0.09f, 0.012f, Vector3.Distance(a, b) + 0.09f), cream, false).transform;
                seg.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            }
            void Arc(float cz, float r, float from, float to, int steps)
            {
                for (var i = 0; i < steps; i++)
                {
                    var a0 = Mathf.Lerp(from, to, i / (float)steps);
                    var a1 = Mathf.Lerp(from, to, (i + 1f) / steps);
                    L(Mathf.Sin(a0) * r, cz - Mathf.Cos(a0) * r, Mathf.Sin(a1) * r, cz - Mathf.Cos(a1) * r);
                }
            }
            // stage z runs away from the camera: the baseline lies 2 m behind the hoop (+z), the key reaches 2.6 m towards the player (−z)
            L(-4f, 2f, 4f, 2f);
            L(-4f, 2f, -4f, -CourtNear);
            L(4f, 2f, 4f, -CourtNear);
            L(-1.25f, 2f, -1.25f, -2.6f);
            L(1.25f, 2f, 1.25f, -2.6f);
            L(-1.25f, -2.6f, 1.25f, -2.6f);
            Arc(-2.6f, 1.25f, -Mathf.PI * 0.5f, Mathf.PI * 0.5f, 12);
            Arc(0f, 4.4f, -1.1f, 1.1f, 22);
            _stage.MergeChildren(lines, "Lines");
            // the shooting spot stays put on the court
            var spot = World.RanchDressing.Ring(root, "Spot", 0f, 0.46f, World.ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.4f)));
            spot.position = P3(tune.SpawnX, 0.03, tune.SpawnZ);
        }

        // ── the cage: posts, rails and a faint chain-link pane along the back, lamps, benches with fans ──

        private void BuildCage(Transform root)
        {
            const float fenceZ = 7.3f, fenceH = 2.5f, fenceX = 5.2f;
            var cage = new GameObject("Cage").transform;
            cage.SetParent(root, false);
            var post = World.ArtLibrary.Solid(new Color(0.5f, 0.55f, 0.6f), 0.55f);
            for (var i = 0; i <= 6; i++)
            {
                var x = -fenceX + 2f * fenceX * i / 6f;
                World.ArtLibrary.Cylinder(cage, "Post", World.MgStage3D.At(x, fenceH * 0.5f, fenceZ), 0.05f, fenceH, post, false);
            }
            foreach (var y in new[] { fenceH, fenceH * 0.52f, 0.08f })
            {
                var rail = World.ArtLibrary.Cylinder(cage, "Rail", World.MgStage3D.At(0f, y, fenceZ), 0.035f, fenceX * 2f, post, false).transform;
                rail.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
            // the mesh: diagonal wires, thin but many (merged into one draw)
            var wire = World.ArtLibrary.Solid(new Color(0.72f, 0.77f, 0.82f), 0.5f);
            const float pitch = 0.42f;
            var len = fenceH / Mathf.Sin(45f * Mathf.Deg2Rad);
            for (var x = -fenceX - fenceH; x < fenceX; x += pitch)
            {
                foreach (var dir in new[] { 1f, -1f })
                {
                    var x0 = dir > 0f ? x : x + fenceH;
                    var a = new Vector3(x0, 0.05f, fenceZ);
                    var b = new Vector3(x0 + dir * fenceH, fenceH, fenceZ);
                    // clip to the fence's width
                    var t0 = 0f; var t1 = 1f;
                    var dx = b.x - a.x;
                    if (Mathf.Abs(dx) > 1e-4f)
                    {
                        var ta = (-fenceX - a.x) / dx; var tb = (fenceX - a.x) / dx;
                        t0 = Mathf.Max(t0, Mathf.Min(ta, tb)); t1 = Mathf.Min(t1, Mathf.Max(ta, tb));
                    }
                    if (t1 - t0 < 0.02f) continue;
                    var p0 = Vector3.Lerp(a, b, t0); var p1 = Vector3.Lerp(a, b, t1);
                    var seg = World.ArtLibrary.Box(cage, "Wire", World.MgStage3D.At(0f, 0f, 0f) + (p0 + p1) * 0.5f, new Vector3(0.022f, Vector3.Distance(p0, p1), 0.012f), wire, false).transform;
                    seg.rotation = Quaternion.FromToRotation(Vector3.up, (p1 - p0).normalized);
                }
            }
            _stage.MergeChildren(cage, "Cage");
            foreach (var x in new[] { -3.9f, 3.9f }) _stage.PlaceScenery("lamp_post", World.MgStage3D.At(x, 0f, fenceZ - 0.45f), 180f, 1f);
            // two benches along the back fence, six Gooby fans on them
            var seats = new List<Vector3>();
            foreach (var bx in new[] { -2.05f, 2.05f })
            {
                _stage.PlaceScenery("park_bench", World.MgStage3D.At(bx, 0f, fenceZ - 0.55f), 180f, 1f);
                for (var k = -1; k <= 1; k++) seats.Add(World.MgStage3D.At(bx + k * 0.56f, 0.44f, fenceZ - 0.62f));
            }
            _fans = new World.RanchCrowd(_stage, seats, 180f);
            // round fans standing behind the fence (merged, they hop together on a basket)
            _standing = new GameObject("Standing").transform;
            _standing.SetParent(root, false);
            _standing.position = World.MgStage3D.At(0f, 0f, 0f);
            string[] furs = { "#F5A59A", "#A3C8F2", "#F8D88A", "#B9E0A0", "#E1B3E8", "#F7F3EE", "#E8B27A" };
            for (var i = 0; i < 9; i++)
            {
                var x = -4.4f + 8.8f * i / 8f + 0.3f * Mathf.Sin(i * 2.2f);
                var fur = World.ArtLibrary.Solid(World.ArtLibrary.Hex(furs[i % furs.Length]), 0.2f);
                var size = 0.92f + (i % 3) * 0.06f;
                var body = World.ArtLibrary.Sphere(_standing, "Body", World.MgStage3D.At(x, 0.42f * size, fenceZ + 0.8f), 0.3f * size, fur, false).transform;
                body.localScale = new Vector3(0.64f, 0.84f, 0.56f) * size;
                World.ArtLibrary.Sphere(_standing, "Head", World.MgStage3D.At(x, 1.02f * size, fenceZ + 0.78f), 0.24f * size, fur, false);
                foreach (var side in new[] { -1f, 1f })
                    World.ArtLibrary.Sphere(_standing, "Ear", World.MgStage3D.At(x + side * 0.16f * size, 1.24f * size, fenceZ + 0.8f), 0.075f * size, fur, false);
            }
            _stage.MergeChildren(_standing, "Fans");
        }

        // ── the park beyond: hedge, flowering bushes, round trees, hills, clouds, the sun ──

        private void BuildPark()
        {
            _stage.Row("hedge_long", 0f, 9.4f, -11f, 11f, 6, 1f, 0f, 0.2f, 11);
            _stage.Row("bush_flowers", 0f, 8.6f, -9f, 9f, 5, 1.1f, 0f, 0.6f, 12);
            _stage.Row("tree_round_big", 0f, 12.5f, -13f, 13f, 5, 0.95f, 0f, 1.2f, 13);
            _stage.Row("tree_round", 0f, 17f, -18f, 18f, 7, 1.05f, 0f, 1.6f, 14);
            _stage.Hills(0f, 34f, 6, World.ArtLibrary.Hex("#9FCF92"), 6f, 80f, 0f, 21);
            _stage.Hills(0f, 48f, 5, World.ArtLibrary.Hex("#C3E2C6"), 9f, 110f, 10f, 22);
            _stage.Clouds(6, 10.5f, 34f, 36f, 2.2f, 23);
            _stage.Sun(new Vector3(-6.5f, 15.5f, 50f), 2.4f);
        }

        // ── the hoop: one node at the engine's hoop, the board 0.62 m behind the rim ──

        private void BuildHoop(Transform root, BasketBounce.Tune tune)
        {
            _hoop = new GameObject("Hoop").transform;
            _hoop.SetParent(root, false);
            _hoop.position = World.MgStage3D.At(0f, 0f, 0f);
            var gap = (float)tune.BoardGap;
            var bb = (float)tune.BoardBottomY;
            var bh = (float)tune.BoardH;
            var bw = (float)tune.BoardW;
            var rimY = (float)tune.RimY;
            var o = _hoop.position;
            var pole = World.ArtLibrary.Solid(new Color(0.58f, 0.6f, 0.64f), 0.5f);
            var poleH = bb + bh * 0.6f;
            var poleZ = gap + 0.55f;
            World.ArtLibrary.Cylinder(_hoop, "Pole", o + new Vector3(0f, poleH * 0.5f, poleZ), 0.09f, poleH, pole);
            World.ArtLibrary.Cylinder(_hoop, "Foot", o + new Vector3(0f, 0.07f, poleZ), 0.42f, 0.14f, World.ArtLibrary.Solid(new Color(0.5f, 0.52f, 0.56f), 0.6f));
            World.ArtLibrary.Box(_hoop, "Arm", o + new Vector3(0f, bb + 0.42f, gap + 0.275f), new Vector3(0.12f, 0.1f, 0.62f), pole);
            World.ArtLibrary.Box(_hoop, "Board", o + new Vector3(0f, bb + bh * 0.5f, gap), new Vector3(bw, bh, 0.07f), World.ArtLibrary.Solid(new Color(0.99f, 0.98f, 0.95f), 0.45f));
            World.ArtLibrary.Box(_hoop, "Frame", o + new Vector3(0f, bb + bh * 0.5f, gap + 0.045f), new Vector3(bw + 0.1f, bh + 0.1f, 0.04f), World.ArtLibrary.Solid(BoardPink, 0.5f));
            var square = World.ArtLibrary.Solid(new Color(0.95f, 0.42f, 0.6f), 0.5f);
            World.ArtLibrary.Box(_hoop, "Square", o + new Vector3(0f, bb + 0.24f, gap - 0.04f), new Vector3(0.62f, 0.05f, 0.02f), square, false);
            World.ArtLibrary.Box(_hoop, "Square", o + new Vector3(0f, bb + 0.92f, gap - 0.04f), new Vector3(0.62f, 0.05f, 0.02f), square, false);
            World.ArtLibrary.Box(_hoop, "Square", o + new Vector3(-0.3f, bb + 0.58f, gap - 0.04f), new Vector3(0.05f, 0.72f, 0.02f), square, false);
            World.ArtLibrary.Box(_hoop, "Square", o + new Vector3(0.3f, bb + 0.58f, gap - 0.04f), new Vector3(0.05f, 0.72f, 0.02f), square, false);
            World.ArtLibrary.Box(_hoop, "Neck", o + new Vector3(0f, rimY, gap * 0.5f), new Vector3(0.12f, 0.06f, gap), World.ArtLibrary.Solid(RimOrange * 0.8f, 0.5f));
            _rimMat = new Material(World.ArtLibrary.Solid(RimOrange, 0.55f));
            _ownedMats.Add(_rimMat);
            _rim = _stage.Torus(_hoop, "Rim", o + new Vector3(0f, rimY, 0f), (float)tune.RimR, (float)tune.RimTube * 1.3f, _rimMat, true).transform;
            // the net: fourteen cords from the rim down to a narrower ring, two rings across
            _net = new GameObject("Net").transform;
            _net.SetParent(_hoop, false);
            _net.position = o + new Vector3(0f, rimY, 0f);
            var cord = World.ArtLibrary.Solid(new Color(1f, 1f, 1f), 0.3f);
            var r = (float)tune.RimR;
            for (var i = 0; i < 14; i++)
            {
                var a = Mathf.PI * 2f * i / 14f;
                var top = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                var bottom = new Vector3(Mathf.Sin(a + 0.24f) * r * 0.56f, -0.46f, Mathf.Cos(a + 0.24f) * r * 0.56f);
                var c = World.ArtLibrary.Box(_net, "Cord", _net.position + (top + bottom) * 0.5f, new Vector3(0.016f, Vector3.Distance(top, bottom), 0.016f), cord, false).transform;
                c.rotation = Quaternion.FromToRotation(Vector3.up, (top - bottom).normalized);
            }
            _stage.Torus(_net, "NetRing", _net.position + new Vector3(0f, -0.23f, 0f), r * 0.78f, 0.009f, cord);
            _stage.Torus(_net, "NetRing", _net.position + new Vector3(0f, -0.46f, 0f), r * 0.56f, 0.009f, cord);
            _stage.MergeChildren(_net, "Net");
        }

        // ── the ball: an orange sphere with two seams, spinning under a pivot, and its blob shadow ──

        private void BuildBall(Transform root, BasketBounce.Tune tune)
        {
            var br = (float)tune.BallR;
            _ball = new GameObject("Ball").transform;
            _ball.SetParent(root, false);
            _ball.position = P3(tune.SpawnX, tune.SpawnY, tune.SpawnZ);
            _ballSpin = new GameObject("Spin").transform;
            _ballSpin.SetParent(_ball, false);
            _ballMat = new Material(World.ArtLibrary.Solid(BallColor, 0.38f));
            _ownedMats.Add(_ballMat);
            World.ArtLibrary.Sphere(_ballSpin, "Leather", _ball.position, br, _ballMat);
            var seam = World.ArtLibrary.Solid(Seam, 0.4f);
            _stage.Torus(_ballSpin, "Seam", _ball.position, br * 1.002f, 0.011f, seam);
            _stage.Torus(_ballSpin, "Seam", _ball.position, br * 1.002f, 0.011f, seam).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _stage.Torus(_ballSpin, "Seam", _ball.position, br * 1.002f, 0.011f, seam).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            _ballShadow = World.RanchDressing.Ring(root, "BallShadow", 0f, br * 1.15f, World.ArtLibrary.Transparent(new Color(0.1f, 0.12f, 0.08f, 0.3f)));
        }

        // ── every frame ──

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _stageT += dt;
            var tune = _game.Tune;
            var hoop = _game.HoopNow();
            // the camera backs off with the hoop and shakes with the canvas (its pixel offsets as angles at the same focal length)
            _stage.Camera.transform.position = P3(0, CamY, CamZ);
            var pitch = CamPitchDeg; var yaw = 0f;
            if (_shake > 0f && !ReducedMotion)
            {
                var focal = Mathf.Max(1f, Focal);
                yaw = -Mathf.Atan2(Mathf.Sin(Time.unscaledTime * 80f) * _shake * 60f, focal) * Mathf.Rad2Deg;
                pitch -= Mathf.Atan2(Mathf.Cos(Time.unscaledTime * 70f) * _shake * 40f, focal) * Mathf.Rad2Deg;
            }
            _stage.Camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            _court.position = P3(0, 0, hoop.Z);
            _hoop.position = P3(hoop.X, 0, hoop.Z);
            // the rim flinches and flashes pale when hit; the net whips down and swings out after a basket
            var rf = Mathf.Clamp01(_rimPulse / 0.28f);
            _rim.localScale = Vector3.one * (float)tune.RimR * (1f + 0.14f * rf);
            var rc = Color.Lerp(RimOrange, Color.white, rf * 0.7f);
            _rimMat.color = rc;
            if (_rimMat.HasProperty("_BaseColor")) _rimMat.SetColor("_BaseColor", rc);
            var nf = Mathf.Clamp01(_netPulse / 0.45f);
            var wave = Mathf.Sin((1f - nf) * Mathf.PI * 3f) * nf;
            _net.localScale = new Vector3(1f + 0.1f * nf, 1f + 0.42f * Mathf.Max(0f, wave) + 0.1f * nf, 1f + 0.1f * nf);
            _net.localRotation = Quaternion.Euler(0f, 0f, 0.08f * wave * Mathf.Rad2Deg);
            RenderBall(dt, tune);
            RenderShooter(dt, tune);
            // the standing fans hop on a basket, everyone sways a little
            var hop = ReducedMotion ? 0f : 0.16f * _crowdPulse * Mathf.Abs(Mathf.Sin((1f - _crowdPulse) * Mathf.PI * 4f));
            _standing.position = World.MgStage3D.At(0f, (ReducedMotion ? 0f : 0.025f * Mathf.Sin(_stageT * 2.1f)) + hop, 0f);
            _fans?.Tick(dt, _crowdCue, _crowdAll);
            _crowdCue = _crowdAll = false;
        }

        private void RenderBall(float dt, BasketBounce.Tune tune)
        {
            var br = (float)tune.BallR;
            Vector3 pos;
            var spinning = false;
            if (_game.Phase == BasketBounceGame.PhaseFly && _game.Ball != null)
            {
                pos = new Vector3((float)_game.Ball.Px, (float)_game.Ball.Py, (float)_game.Ball.Pz);
                spinning = true;
            }
            else if (_game.Phase == BasketBounceGame.PhaseReset && _dropLeft > 0f)
            {
                // after the shot the ball keeps falling: through the net, onto the court, a bounce or two
                _dropLeft -= dt;
                _dropVel.y -= (float)tune.Gravity * dt;
                _dropPos += _dropVel * dt;
                if (_dropPos.y < br)
                {
                    _dropPos.y = br;
                    if (_dropVel.y < 0f) _dropVel.y = -_dropVel.y * 0.55f;
                    _dropVel.x *= 0.82f; _dropVel.z *= 0.82f;
                }
                pos = _dropPos;
                spinning = true;
                _popIn = 0f;
            }
            else
            {
                // back on the spot: pops in, then bobs gently in Gooby's reach
                _popIn = Mathf.Min(1f, _popIn + dt * 5f);
                pos = new Vector3((float)tune.SpawnX, (float)tune.SpawnY + (ReducedMotion ? 0f : Mathf.Sin(_stageT * 3f) * 0.03f), (float)tune.SpawnZ);
            }
            _ball.position = P3(pos.x, pos.y, pos.z);
            var pop = Mathf.SmoothStep(0.2f, 1f, _popIn);
            // a clang squashes the ball a moment
            var rf = Mathf.Clamp01(_rimPulse / 0.28f);
            var squash = 1f - 0.22f * rf;
            _ball.localScale = new Vector3(2f - squash, squash, 2f - squash) * pop;
            if (spinning && !ReducedMotion) _ballSpin.Rotate(Vector3.right, -540f * dt, Space.Self);
            // the shadow on the court: smaller and fainter the higher the ball
            var k = Mathf.Clamp(1.4f - pos.y * 0.12f, 0.5f, 1.4f);
            _ballShadow.position = P3(pos.x, 0.035, pos.z);
            _ballShadow.localScale = Vector3.one * k * pop;
            // on fire: the leather glows and sheds sparks in flight
            var fire = _game.OnFire;
            var glow = fire ? new Color(1f, 0.42f, 0.1f) * (0.55f + 0.25f * Mathf.Sin(_stageT * 12f)) : Color.black;
            if (_ballMat.HasProperty("_EmissionColor"))
            {
                if (fire) _ballMat.EnableKeyword("_EMISSION");
                _ballMat.SetColor("_EmissionColor", glow);
            }
            if (fire && _game.Phase == BasketBounceGame.PhaseFly && !ReducedMotion)
            {
                _fireEmit -= dt;
                if (_fireEmit <= 0f)
                {
                    _fireEmit = 0.05f;
                    World.Effects.Puff(_ball.position, new Color(1f, 0.62f, 0.2f, 0.9f), 2, 0.09f, 0.4f, 0.35f, Vector3.up, 0.6f);
                }
            }
        }

        private void RenderShooter(float dt, BasketBounce.Tune tune)
        {
            var gb = _stage.Gooby;
            if (gb == null) return;
            // the wind-up: he leans back as the finger pulls (stronger pull, deeper lean)
            var target = 0f;
            if (_game.Dragging)
            {
                var dx = _game.DragTo.x - _game.DragFrom.x; var dy = _game.DragTo.y - _game.DragFrom.y;
                target = WindupLeanDeg * Mathf.Clamp01((float)System.Math.Sqrt(dx * dx + dy * dy) / 220f);
            }
            _lean = Mathf.Lerp(_lean, target, Mathf.Min(1f, dt * 15f));
            // facing: towards the ball and hoop while aiming, round to the camera while celebrating
            var yawTarget = _celebrating ? 196f : GoobyAimYaw;
            if (_celebrating && _game.Aiming) { _celebrating = false; gb.SetEmotion("happy"); }
            _goobyYaw = Mathf.LerpAngle(_goobyYaw, yawTarget, Mathf.Min(1f, dt * 8f));
            var lift = 0f;
            if (_hopT >= 0f)
            {
                _hopT += dt;
                lift = _hopT < 0.5f ? _hopH * Mathf.Sin(_hopT / 0.5f * Mathf.PI) : 0f;
                if (_hopT >= 0.5f) _hopT = -1f;
            }
            gb.transform.position = ShooterSpot(tune) + Vector3.up * lift;
            gb.transform.rotation = Quaternion.Euler(0f, 0f, _lean) * Quaternion.Euler(0f, _goobyYaw, 0f);
        }

        // ── moments ──

        private void StageLaunched()
        {
            _popIn = 1f;
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("wave", 0.08f);
            gb.SetEmotion("ecstatic");
        }

        private void StageResolved(bool made, bool swish)
        {
            var b = _game.Ball;
            if (b != null)
            {
                _dropPos = new Vector3((float)b.Px, (float)b.Py, (float)b.Pz);
                _dropVel = new Vector3((float)b.Vx, (float)b.Vy, (float)b.Vz);
                if (made) { _dropVel.x *= 0.2f; _dropVel.z *= 0.2f; _dropVel.y = Mathf.Min(_dropVel.y, -1.2f); }
                _dropLeft = 0.9f;
            }
            var gb = _stage?.Gooby;
            if (made)
            {
                _crowdCue = true;
                _crowdAll |= swish || _game.OnFire;
                if (!ReducedMotion) World.Effects.Sparkle(P3(_game.HoopNow().X, _game.Tune.RimY - 0.3, _game.HoopNow().Z), 0.55f, swish ? 28 : 18);
                if (gb == null) return;
                _celebrating = true;
                gb.Play("celebrate", 0.1f);
                gb.SetEmotion("ecstatic");
                _hopT = 0f;
                _hopH = swish ? 0.5f : 0.34f;
                return;
            }
            if (!ReducedMotion && b != null)
                World.Effects.Puff(P3(b.Px, 0.05, b.Pz), new Color(0.85f, 0.76f, 0.62f, 0.85f), 12, 0.12f, 1.2f, 0.6f, Vector3.up, 0.9f);
            if (gb == null) return;
            gb.SetEmotion("sad");
        }

        private void DropStage()
        {
            _fans?.Destroy();
            _fans = null;
            foreach (var m in _ownedMats) if (m != null) Object.Destroy(m);
            _ownedMats.Clear();
            _stage?.Destroy();
            _stage = null;
            _celebrating = false;
            _hopT = -1f;
            _dropLeft = 0f;
            _popIn = 1f;
        }
    }
}
