using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D stadium of Torwart-Gooby (port of goalie_gooby_scenery.gd): seen from behind the striker like the old painted
    /// view — stands full of Gooby fans under a striped awning with bunting, the striped pitch with its boxes and spot, the
    /// goal with posts, crossbar and a net that bulges after a goal. A Gooby striker in a cap runs up and kicks a real ball
    /// (lobs arc, rollers skid); the player's Gooby keeps goal, sliding and diving into the swiped lane — up for lobs, down
    /// for rollers. The camera reproduces the 2D projection (same height, distance, pitch and focal length) with the engine's
    /// z mirrored, so the telegraph ring, the save ring and the goal pips of the front canvas stay on target.
    /// </summary>
    public sealed partial class GoalieGoobyScreen
    {
        private World.MgStage3D _stage;
        private World.RanchCrowd _fans;
        private Transform _ball, _net, _striker;
        private World.GoobyAvatar _strikerAvatar;
        private bool _crowdCue, _crowdAll, _strikerKicked;
        private string _strikerClip = "";
        private readonly List<Transform> _rows = new List<Transform>();

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 P3(double x, double y, double z) => World.MgStage3D.At((float)x, (float)y, (float)-z);

        private void BuildStage()
        {
            EnsureFieldSize();
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.68f, 0.85f, 0.98f));
            _stage.Bind(Field);
            var fov = 2f * Mathf.Atan(0.5f / 1.7f) * Mathf.Rad2Deg;
            _stage.Aim(new Vector3(0f, (float)CamY, (float)-CamZ), CamPitchDeg, fov);
            var root = _stage.Root.transform;
            // the pitch: mowed stripes, lines, the boxes and the spot
            _stage.Ground(0f, World.ArtLibrary.Hex("#8FCF7C"), "grass", 70f, 60f, -10f);
            var light = World.ArtLibrary.Pbr("grass", 2.4f, World.ArtLibrary.Hex("#A6DB8C"), 0.05f);
            for (var i = 0; i < 9; i++)
                World.ArtLibrary.Box(root, "Stripe", P3(0, 0.004, -6.0 + i * 3.2 + 0.8), new Vector3(60f, 0.008f, 1.6f), light, false);
            var line = World.ArtLibrary.Solid(new Color(0.98f, 0.98f, 0.95f), 0.3f);
            var lines = new GameObject("Lines").transform;
            lines.SetParent(root, false);
            void L(double x0, double z0, double x1, double z1)
            {
                var a = P3(x0, 0.012, z0); var b = P3(x1, 0.012, z1);
                var c = (a + b) * 0.5f;
                var len = Vector3.Distance(a, b);
                var seg = World.ArtLibrary.Box(lines, "Line", c, new Vector3(0.1f, 0.01f, len + 0.1f), line, false).transform;
                seg.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            }
            L(-14, 0, 14, 0);
            foreach (var (hw, d) in new[] { (GoalHalfW + 1.6, 3.0), (GoalHalfW + 5.0, 8.6) })
            {
                L(-hw, 0, -hw, d); L(hw, 0, hw, d); L(-hw, d, hw, d);
            }
            _stage.MergeChildren(lines, "Lines");
            World.ArtLibrary.Cylinder(root, "Spot", P3(0, 0.012, SpotZ), 0.12f, 0.01f, line, false);
            BuildGoal(root);
            BuildStands(root);
            // the ball: white with dark patches
            _ball = World.ArtLibrary.Sphere(root, "Ball", Vector3.zero, (float)BallR, World.ArtLibrary.Solid(Color.white, 0.45f)).transform;
            var patch = World.ArtLibrary.Solid(new Color(0.2f, 0.2f, 0.25f), 0.4f);
            foreach (var dir in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                var spot = World.ArtLibrary.Sphere(_ball, "Patch", Vector3.zero, 0.5f, patch, false).transform;
                spot.localPosition = dir * 0.42f;
                spot.localScale = Vector3.one * 0.3f;
            }
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnKeeper(outfit);
            _ = SpawnStriker();
        }

        private void BuildGoal(Transform root)
        {
            var post = World.ArtLibrary.Solid(new Color(0.98f, 0.98f, 0.98f), 0.6f);
            var gh = (float)GoalH;
            var hw = (float)GoalHalfW;
            foreach (var s in new[] { -1f, 1f }) World.ArtLibrary.Cylinder(root, "Post", P3(s * hw, gh * 0.5, 0), 0.07f, gh, post);
            var bar = World.ArtLibrary.Cylinder(root, "Crossbar", P3(0, gh, 0), 0.07f, hw * 2f + 0.14f, post).transform;
            bar.rotation = Quaternion.Euler(0f, 0f, 90f);
            _net = new GameObject("Net").transform;
            _net.SetParent(root, false);
            _net.position = P3(0, 0, 0);
            var mesh = World.ArtLibrary.Solid(new Color(1f, 1f, 1f), 0.2f);
            const float netZ = 1.4f;
            for (var i = 0; i <= 12; i++)
            {
                var x = -hw + hw * 2f * i / 12f;
                var back = World.ArtLibrary.Box(_net, "NetV", Vector3.zero, new Vector3(0.025f, gh, 0.025f), mesh, false).transform;
                back.localPosition = new Vector3(x, gh * 0.5f, netZ);
                var roof = World.ArtLibrary.Box(_net, "NetR", Vector3.zero, new Vector3(0.025f, 0.025f, netZ), mesh, false).transform;
                roof.localPosition = new Vector3(x, gh * 0.99f, netZ * 0.5f);
            }
            for (var i = 0; i <= 6; i++)
            {
                var y = gh * i / 6f;
                World.ArtLibrary.Box(_net, "NetH", Vector3.zero, new Vector3(hw * 2f, 0.025f, 0.025f), mesh, false).transform.localPosition = new Vector3(0f, y, netZ);
                foreach (var s in new[] { -1f, 1f })
                    World.ArtLibrary.Box(_net, "NetS", Vector3.zero, new Vector3(0.025f, 0.025f, netZ), mesh, false).transform.localPosition = new Vector3(s * hw, y, netZ * 0.5f);
            }
            _stage.MergeChildren(_net, "Net");
        }

        /// <summary>
        /// Tiers behind the goal under a striped awning with a scalloped hem, bunting above the crowd and pastel ad boards
        /// along the pitch edge. Sixteen Gooby fans cheer from the middle; round fans in fur colours fill the rest of every
        /// row (merged per row, so a cheer bobs a whole row in one move).
        /// </summary>
        private void BuildStands(Transform root)
        {
            var tier = World.ArtLibrary.Pbr("wood_planks", 1.2f, World.ArtLibrary.Hex("#D9A77A"), 0.25f);
            var front = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#B98A5E"), 0.2f);
            var seats = new List<Vector3>();
            _rows.Clear();
            for (var r = 0; r < 4; r++)
            {
                var z = 6.2f + r * 1.1f;
                var y = 0.5f + r * 0.8f;
                World.ArtLibrary.Box(root, "Tier", World.MgStage3D.At(0f, y - 0.25f, z), new Vector3(36f, 0.5f, 1.1f), tier, false);
                World.ArtLibrary.Box(root, "Riser", World.MgStage3D.At(0f, (y - 0.5f) * 0.5f, z - 0.55f), new Vector3(36f, y - 0.5f + 0.01f, 0.08f), front, false);
                var stars = new List<float>();
                for (var i = 0; i < 4; i++)
                {
                    var x = -7.5f + i * 5f + (r % 2) * 2.4f;
                    stars.Add(x);
                    seats.Add(World.MgStage3D.At(x, y, z));
                }
                _rows.Add(FillerRow(root, r, y, z, stars));
            }
            World.ArtLibrary.Box(root, "BackWall", World.MgStage3D.At(0f, 3.8f, 10.8f), new Vector3(38f, 7.6f, 0.3f), World.ArtLibrary.Pbr("plaster", 3f, World.ArtLibrary.Hex("#F6E4C8"), 0.1f), false);
            // the awning slopes up to the back wall; its hem is the top edge of the view, like the painted one
            var awning = new GameObject("Awning").transform;
            awning.SetParent(root, false);
            var pink = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF9BC1"), 0.3f);
            var cream = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFF6EA"), 0.3f);
            for (var s = 0; s < 18; s++)
            {
                var x = -19.8f + s * 2.2f + 1.1f;
                var mat = s % 2 == 0 ? pink : cream;
                var stripe = World.ArtLibrary.Box(awning, "Stripe", World.MgStage3D.At(x, 5.95f, 9.2f), new Vector3(2.2f, 0.08f, 3.4f), mat, false).transform;
                stripe.rotation = Quaternion.Euler(-21f, 0f, 0f);
                var hem = World.ArtLibrary.Sphere(awning, "Hem", World.MgStage3D.At(x, 5.32f, 7.62f), 0.5f, mat, false).transform;
                hem.localScale = new Vector3(2.2f, 0.62f, 0.12f);
            }
            _stage.MergeChildren(awning, "Awning");
            World.RanchDressing.Bunting(root, World.MgStage3D.At(-15f, 4.7f, 5.7f), World.MgStage3D.At(15f, 4.7f, 5.7f), 0.55f, 0.9f);
            World.RanchDressing.Bunting(root, World.MgStage3D.At(-15f, 4.1f, 5.5f), World.MgStage3D.At(15f, 4.1f, 5.5f), 0.4f, 1.2f);
            string[] ads = { "#FFD166", "#9BE7E1", "#FF9BC1", "#B7D8FF", "#C6F39A" };
            var boards = new GameObject("AdBoards").transform;
            boards.SetParent(root, false);
            var stripeMat = World.ArtLibrary.Solid(new Color(1f, 1f, 1f), 0.3f);
            for (var i = 0; i < 12; i++)
            {
                var x = -17f + i * 3.1f;
                World.ArtLibrary.Box(boards, "AdBoard", World.MgStage3D.At(x, 0.45f, 4.6f), new Vector3(2.9f, 0.9f, 0.12f), World.ArtLibrary.Solid(World.ArtLibrary.Hex(ads[i % ads.Length]), 0.3f), false);
                World.ArtLibrary.Box(boards, "AdStripe", World.MgStage3D.At(x, 0.5f, 4.53f), new Vector3(1.5f, 0.22f, 0.02f), stripeMat, false);
            }
            _stage.MergeChildren(boards, "AdBoards");
            _fans = new World.RanchCrowd(_stage, seats, 180f);
        }

        private static readonly string[] FillerFurs = { "#E8B27A", "#A8E6CF", "#F7F3EE", "#C9B6F2", "#A9D6F5", "#F7B8D2", "#9C6B4E", "#FFD98A" };

        /// <summary>One row of round fans (body, head, ears) beside the Gooby fans, merged into one mesh per fur.</summary>
        private Transform FillerRow(Transform root, int r, float y, float z, List<float> stars)
        {
            var row = new GameObject("FanRow" + r).transform;
            row.SetParent(root, false);
            row.position = World.MgStage3D.At(0f, 0f, 0f);
            for (var i = 0; i < 30; i++)
            {
                var x = -17.4f + i * 1.2f + (r % 2) * 0.6f;
                var near = false;
                foreach (var s in stars) near |= Mathf.Abs(s - x) < 0.95f;
                if (near) continue;
                var fur = World.ArtLibrary.Solid(World.ArtLibrary.Hex(FillerFurs[(i * 3 + r * 5) % FillerFurs.Length]), 0.2f);
                var size = 0.92f + ((i * 7 + r) % 5) * 0.04f;
                var body = World.ArtLibrary.Sphere(row, "Body", World.MgStage3D.At(x, y + 0.3f * size, z), 0.32f * size, fur, false).transform;
                body.localScale = new Vector3(0.66f, 0.6f, 0.56f) * size;
                World.ArtLibrary.Sphere(row, "Head", World.MgStage3D.At(x, y + 0.78f * size, z - 0.04f), 0.25f * size, fur, false);
                foreach (var side in new[] { -1f, 1f })
                    World.ArtLibrary.Sphere(row, "Ear", World.MgStage3D.At(x + side * 0.17f * size, y + 1.0f * size, z), 0.08f * size, fur, false);
            }
            _stage.MergeChildren(row, "Fans");
            return row;
        }

        private async System.Threading.Tasks.Task SpawnKeeper(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(P3(0, 0, 0.15), 1.25f, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            g.SetEmotion("happy");
        }

        private async System.Threading.Tasks.Task SpawnStriker()
        {
            var anchor = new GameObject("Striker").transform;
            anchor.SetParent(_stage.Root.transform, false);
            _striker = anchor;
            var g = await World.GoobyAvatar.CreateAsync(anchor, anchor.position);
            if (g == null) return;
            if (_stage == null || anchor == null || _striker != anchor) { Object.Destroy(g.gameObject); return; }
            g.transform.SetParent(anchor, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = Vector3.one * 0.98f;
            g.Wear(new Dictionary<string, string> { ["fell"] = "fell_kakao", ["hut"] = "cap" });
            g.Play("idle-loop");
            _strikerAvatar = g;
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var kick = _game.Current;
            // the ball
            var ball = BallWorld();
            _ball.position = P3(ball.x, ball.y, ball.z);
            _ball.rotation = Quaternion.Euler((float)_game.KickProgress * 720f + _show * 40f, _show * 30f, 0f);
            // the striker runs up during the telegraph and kicks
            var run = kick != null && !_game.BallInFlight ? (float)_game.TelegraphProgress : kick != null ? 1f : 0f;
            if (_striker != null)
            {
                _striker.position = P3(1.45, 0, SpotZ + StrikerRun * (1.0 - run) + 0.35);
                _striker.rotation = Quaternion.Euler(0f, -12f, 0f);
                var clip = kick != null && !_game.BallInFlight && run < 1f ? "walk-loop" : "idle-loop";
                if (_strikerAvatar != null && clip != _strikerClip) { _strikerClip = clip; _strikerAvatar.Play(clip, 0.12f); }
                if (_strikerAvatar != null && _game.BallInFlight && !_strikerKicked) { _strikerKicked = true; _strikerAvatar.Play("hop", 0.05f); }
                if (!_game.BallInFlight) _strikerKicked = false;
            }
            // the net bulges after a goal
            _net.localScale = new Vector3(1f, 1f, 1f + _netRipple * 0.18f * Mathf.Abs(Mathf.Sin(_show * 30f)));
            // the keeper slides and dives: up dives lift him, down dives lay him low, the lane tilts him (a positive roll
            // after the half turn leans him to the right of the view)
            var gb = _stage.Gooby;
            if (gb != null)
            {
                var d = ShownDive;
                var diving = d != null;
                var feet = Mathf.Max(0f, (_keeperY - 0.9f) * 0.9f);
                var low = diving && d.V == "down" ? (d.Lane > 2 ? 40f : d.Lane < 2 ? -40f : 0f) : 0f;
                var lean = diving ? (d.Lane - 2) * 26f + low : 0f;
                gb.transform.position = P3(_keeperX, feet, 0.15);
                gb.transform.rotation = Quaternion.Euler(0f, 180f, lean);
            }
            for (var r = 0; r < _rows.Count; r++)
            {
                var bob = ReducedMotion ? 0f : Mathf.Abs(Mathf.Sin(_show * 8f + r * 0.9f)) * _crowd * 0.12f;
                _rows[r].position = World.MgStage3D.At(0f, bob, 0f);
            }
            _fans?.Tick(dt, _crowdCue, _crowdAll);
            _crowdCue = _crowdAll = false;
            // the camera shakes with the painted overlay (its pixel offsets as angles at the same focal length)
            var pitch = CamPitchDeg; var yaw = 0f;
            if (_shake > 0f && !ReducedMotion)
            {
                var focal = Mathf.Max(1f, Focal);
                yaw = -Mathf.Atan2(Mathf.Sin(Time.unscaledTime * 90f) * _shake * 50f, focal) * Mathf.Rad2Deg;
                pitch -= Mathf.Atan2(Mathf.Cos(Time.unscaledTime * 75f) * _shake * 30f, focal) * Mathf.Rad2Deg;
            }
            _stage.Camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void StageSaved(bool super)
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("ecstatic");
            gb.Play("celebrate", 0.1f);
            _crowdCue = true;
            _crowdAll |= super;
            if (!ReducedMotion) World.Effects.Sparkle(_ball.position, 0.5f, super ? 26 : 14);
        }

        private void StageConceded()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("sad");
            if (!ReducedMotion) World.Effects.Puff(_ball.position, new Color(1f, 1f, 1f, 0.8f), 10, 0.15f, 1.2f, 0.5f, Vector3.back, 0.8f);
        }

        /// <summary>A new kick: the keeper sets himself again (the last save's grin or goal's frown wears off).</summary>
        private void StageKick()
        {
            _stage?.Gooby?.SetEmotion("happy");
        }

        private void StageCheer()
        {
            _crowdCue = true;
            _crowdAll = true;
        }

        private void DropStage()
        {
            _fans?.Destroy();
            _fans = null;
            _rows.Clear();
            _strikerAvatar = null;
            _striker = null;
            _strikerClip = "";
            _strikerKicked = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
