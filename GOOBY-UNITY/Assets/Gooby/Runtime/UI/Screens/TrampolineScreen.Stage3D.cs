using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D gym of Trampolin-Tricks (port of trampoline_stage3d.gd): a round trampoline with padded frame, springs and
    /// legs on the plank floor, a cream hall with a mint wainscot, warm windows onto the garden, wall bars, pennants and a
    /// bench of Gooby fans who jump up for the big moments. The player's Gooby bounces metre-true over the mat (game height
    /// = stage metres), stretches on the way up, squashes on impact and really flips, spins and twists in the air; every
    /// landing dips the mat and rolls dust, a butt landing leaves him squashed with stars circling. Two glowing bars on the
    /// poles beside the mat mark the ×2 and ×3 heights. The camera reproduces the 2D layout (mat at 80 % of the field,
    /// 4.8 m over 72 % of its height), so the gauge, chips and floats of the canvas stay where they were.
    /// </summary>
    public sealed partial class TrampolineScreen
    {
        private World.MgStage3D _stage;
        private World.RanchCrowd _crowd;
        private Transform _cloth, _shadowDisc, _shockRing, _stars;
        private readonly List<Material> _tierMats = new List<Material>();
        private readonly List<Transform> _tierBars = new List<Transform>();
        private readonly List<Material> _ownedMats = new List<Material>();
        private Material _shockMat;
        private float _squash, _trickYaw, _starsSpin;
        private bool _crowdNow, _crowdBig;
        private string _shownTrick = "";

        private const float MatTop = 0.55f, MatR = 1.15f, GoobyScale = 0.95f;
        /// <summary>View pixels per game metre and the mat's view row in the 2D layout (Ppu = 72 % of the height over 4.8 m).</summary>
        private static float ViewPpm => (float)(ViewH * 0.72 / ViewHeightM);

        protected override bool StageHasGooby => _stage != null;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, World.ArtLibrary.Hex("#FFF3E3"));
            _stage.Bind(Field);
            // camera: 1 m = ViewPpm view pixels on the plane through the trampoline, the mat top 2 m under the frame centre
            var halfM = (float)(ViewH / ViewPpm) * 0.5f;
            var dist = halfM / Mathf.Tan(World.MgStage3D.Fov * 0.5f * Mathf.Deg2Rad);
            var centreY = MatTop + (float)(ViewH * 0.8 - ViewH * 0.5) / ViewPpm;
            _stage.Aim(new Vector3(0f, centreY, -dist), 0f, World.MgStage3D.Fov);
            BuildHall();
            BuildTrampoline();
            BuildTiers();
            var seats = new List<Vector3>();
            foreach (var (x, tier) in new[] { (-1.55f, 0), (-1.0f, 1), (1.05f, 0), (1.6f, 1), (-0.45f, 1), (0.5f, 1) })
                seats.Add(World.MgStage3D.At(x, 0.42f + tier * 0.42f, 2.15f + tier * 0.5f));
            _crowd = new World.RanchCrowd(_stage, seats, 180f);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnJumper(outfit);
        }

        private async System.Threading.Tasks.Task SpawnJumper(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(World.MgStage3D.At(0f, MatTop, 0f), GoobyScale, outfit);
            if (g == null || _stage == null) return;
            g.Play("idle-loop");
            g.SetEmotion("happy");
        }

        private void BuildHall()
        {
            var root = _stage.Root.transform;
            World.ArtLibrary.Box(root, "Floor", World.MgStage3D.At(0f, -0.1f, -1.5f), new Vector3(14f, 0.2f, 10f),
                World.ArtLibrary.PbrSlab("wood_floor", new Vector2(14f, 10f), 2f, World.ArtLibrary.Hex("#E8C49A"), 0.35f), false);
            // court lines on the floor
            var line = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F7F0E4"), 0.3f);
            World.ArtLibrary.Box(root, "Line", World.MgStage3D.At(0f, 0.004f, 1.55f), new Vector3(14f, 0.008f, 0.06f), line, false);
            World.ArtLibrary.Box(root, "Line", World.MgStage3D.At(0f, 0.004f, -1.7f), new Vector3(14f, 0.008f, 0.06f), line, false);
            var wallZ = 3.2f;
            World.ArtLibrary.Box(root, "Wall", World.MgStage3D.At(0f, 4f, wallZ + 0.1f), new Vector3(14f, 8f, 0.2f),
                World.ArtLibrary.PbrSlab("plaster", new Vector2(14f, 8f), 3f, World.ArtLibrary.Hex("#FFF1E0"), 0.1f), false);
            World.ArtLibrary.Box(root, "Wainscot", World.MgStage3D.At(0f, 0.65f, wallZ - 0.02f), new Vector3(14f, 1.3f, 0.06f),
                World.ArtLibrary.PbrSlab("wood_planks", new Vector2(14f, 1.3f), 0.9f, World.ArtLibrary.Hex("#CDEFE0"), 0.2f), false);
            World.ArtLibrary.Box(root, "Rail", World.MgStage3D.At(0f, 1.33f, wallZ - 0.06f), new Vector3(14f, 0.08f, 0.1f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6E4C8"), 0.3f), false);
            World.ArtLibrary.Box(root, "Skirting", World.MgStage3D.At(0f, 0.06f, wallZ - 0.06f), new Vector3(14f, 0.12f, 0.08f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#E3CBA8"), 0.3f), false);
            foreach (var x in new[] { -1.15f, 1.15f }) Window(root, x, 4.15f, wallZ);
            World.RanchDressing.Bunting(root, World.MgStage3D.At(-2.6f, 5.75f, wallZ - 0.3f), World.MgStage3D.At(2.6f, 5.75f, wallZ - 0.3f), 0.38f, 0.42f);
            // wall bars on the left, a pinboard poster on the right
            var bar = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#D7A574"), 0.35f);
            var bars = new GameObject("WallBars").transform;
            bars.SetParent(root, false);
            foreach (var x in new[] { -2.05f, -1.55f }) World.ArtLibrary.Box(bars, "Post", World.MgStage3D.At(x, 1.6f, wallZ - 0.12f), new Vector3(0.08f, 3.2f, 0.08f), bar, false);
            for (var i = 0; i < 9; i++) World.ArtLibrary.Cylinder(bars, "Rung", World.MgStage3D.At(-1.8f, 0.3f + i * 0.34f, wallZ - 0.12f), 0.025f, 0.5f, bar, false).transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            _stage.MergeChildren(bars, "WallBars");
            World.ArtLibrary.Box(root, "Poster", World.MgStage3D.At(1.85f, 2.35f, wallZ - 0.03f), new Vector3(0.8f, 1.05f, 0.03f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FFD166"), 0.2f), false);
            World.ArtLibrary.Box(root, "PosterStar", World.MgStage3D.At(1.85f, 2.45f, wallZ - 0.05f), new Vector3(0.36f, 0.36f, 0.02f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF8B8B"), 0.2f), false).transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            // bleacher benches for the fans and two potted plants at the floor line
            var wood = World.ArtLibrary.Pbr("wood_planks", 1.2f, World.ArtLibrary.Hex("#D9A77A"), 0.25f);
            for (var tier = 0; tier < 2; tier++)
            {
                World.ArtLibrary.Box(root, "Bench", World.MgStage3D.At(0f, 0.36f + tier * 0.42f, 2.2f + tier * 0.5f), new Vector3(4.6f, 0.08f, 0.42f), wood);
                World.ArtLibrary.Box(root, "Riser", World.MgStage3D.At(0f, 0.16f + tier * 0.42f, 2.42f + tier * 0.5f), new Vector3(4.6f, 0.32f + tier * 0.42f, 0.06f), wood, false);
            }
            _stage.HomeProp("monstera", World.MgStage3D.At(-2.3f, 0f, 1.2f), 1.1f, 20f);
            _stage.HomeProp("plant_tall", World.MgStage3D.At(2.35f, 0f, 1.0f), 1.0f, -30f);
            // soft sun shafts from the windows onto the floor
            var beam = World.ArtLibrary.Transparent(new Color(1f, 0.92f, 0.7f, 0.08f));
            foreach (var x in new[] { -1.15f, 1.15f })
            {
                var shaft = World.ArtLibrary.Box(root, "Shaft", World.MgStage3D.At(x * 0.85f, 2.4f, 2f), new Vector3(0.9f, 4.4f, 0.02f), beam, false).transform;
                shaft.rotation = Quaternion.Euler(-28f, 0f, x > 0f ? 8f : -8f);
            }
        }

        /// <summary>A tall window onto the garden: sky pane, a hedge with blossoms, white cross bars, a sill and pink curtains.</summary>
        private void Window(Transform root, float x, float y, float wallZ)
        {
            const float w = 1.15f, h = 1.55f;
            var z = wallZ - 0.02f;
            World.ArtLibrary.Box(root, "Pane", World.MgStage3D.At(x, y, z + 0.01f), new Vector3(w, h, 0.02f), World.ArtLibrary.Glow(new Color(0.78f, 0.9f, 1.08f)), false);
            World.ArtLibrary.Box(root, "Hedge", World.MgStage3D.At(x, y - h * 0.32f, z - 0.005f), new Vector3(w, h * 0.36f, 0.02f), World.ArtLibrary.Glow(new Color(0.55f, 0.82f, 0.52f)), false);
            for (var i = 0; i < 4; i++)
                World.ArtLibrary.Sphere(root, "Blossom", World.MgStage3D.At(x - w * 0.36f + i * w * 0.24f, y - h * 0.2f - (i % 2) * 0.1f, z - 0.02f), 0.035f, World.ArtLibrary.Glow(new Color(1.05f, 0.62f, 0.78f)), false);
            var frame = World.ArtLibrary.Solid(Color.white, 0.4f);
            World.ArtLibrary.Box(root, "BarV", World.MgStage3D.At(x, y, z - 0.03f), new Vector3(0.06f, h, 0.04f), frame, false);
            World.ArtLibrary.Box(root, "BarH", World.MgStage3D.At(x, y, z - 0.03f), new Vector3(w, 0.06f, 0.04f), frame, false);
            foreach (var (dx, dy, sx, sy) in new[] { (0f, h * 0.5f, w + 0.1f, 0.08f), (0f, -h * 0.5f, w + 0.1f, 0.08f), (-w * 0.5f, 0f, 0.08f, h), (w * 0.5f, 0f, 0.08f, h) })
                World.ArtLibrary.Box(root, "Frame", World.MgStage3D.At(x + dx, y + dy, z - 0.03f), new Vector3(sx, sy, 0.06f), frame, false);
            World.ArtLibrary.Box(root, "Sill", World.MgStage3D.At(x, y - h * 0.5f - 0.06f, z - 0.09f), new Vector3(w + 0.24f, 0.07f, 0.18f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6E4C8"), 0.3f));
            var curtain = World.ArtLibrary.Pbr("fabric", 0.6f, World.ArtLibrary.Hex("#FF9BC1"), 0.15f);
            foreach (var s in new[] { -1f, 1f })
            {
                var c = World.ArtLibrary.Box(root, "Curtain", World.MgStage3D.At(x + s * (w * 0.5f + 0.1f), y + 0.08f, z - 0.08f), new Vector3(0.24f, h * 1.05f, 0.05f), curtain, false).transform;
                c.rotation = Quaternion.Euler(0f, 0f, s * -3f);
            }
            World.ArtLibrary.Cylinder(root, "Rod", World.MgStage3D.At(x, y + h * 0.5f + 0.16f, z - 0.1f), 0.02f, w + 0.6f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#C8A27A"), 0.4f), false).transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private void BuildTrampoline()
        {
            var root = _stage.Root.transform;
            var steel = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#9AA6BC"), 0.6f);
            for (var i = 0; i < 6; i++)
            {
                var a = (i + 0.5f) * Mathf.PI * 2f / 6f;
                World.ArtLibrary.Cylinder(root, "Leg", World.MgStage3D.At(Mathf.Cos(a) * (MatR + 0.12f), MatTop * 0.5f - 0.03f, Mathf.Sin(a) * (MatR + 0.12f)), 0.035f, MatTop - 0.06f, steel);
            }
            World.ArtLibrary.Cylinder(root, "FrameRing", World.MgStage3D.At(0f, MatTop - 0.06f, 0f), MatR + 0.2f, 0.06f, steel);
            // padded cover over the springs, springs as little silver bars between frame and mat
            var pad = World.RanchDressing.Ring(root, "Pad", MatR - 0.02f, MatR + 0.22f, World.ArtLibrary.Pbr("fabric", 0.5f, World.ArtLibrary.Hex("#4A7FC4"), 0.2f));
            pad.position = World.MgStage3D.At(0f, MatTop + 0.012f, 0f);
            var padRim = World.ArtLibrary.Cylinder(root, "PadRim", World.MgStage3D.At(0f, MatTop - 0.02f, 0f), MatR + 0.22f, 0.06f, World.ArtLibrary.Solid(World.ArtLibrary.Hex("#5A8FD4"), 0.25f), false);
            padRim.transform.localScale = new Vector3((MatR + 0.22f) * 2f, 0.03f, (MatR + 0.22f) * 2f);
            _cloth = World.ArtLibrary.Cylinder(root, "Mat", World.MgStage3D.At(0f, MatTop - 0.01f, 0f), MatR, 0.02f, World.ArtLibrary.Pbr("fabric", 0.35f, World.ArtLibrary.Hex("#3E4D6A"), 0.25f), false).transform;
            _shadowDisc = World.RanchDressing.Ring(root, "Shadow", 0f, 0.42f, World.ArtLibrary.Transparent(new Color(0.05f, 0.05f, 0.12f, 0.35f)));
            _shockMat = new Material(World.ArtLibrary.Transparent(new Color(1f, 1f, 1f, 0.6f)));
            _ownedMats.Add(_shockMat);
            _shockRing = World.RanchDressing.Ring(root, "Shock", 0.86f, 1f, _shockMat);
            _shockRing.gameObject.SetActive(false);
            // dizzy stars for the butt landing
            _stars = new GameObject("Stars").transform;
            _stars.SetParent(root, false);
            var gold = World.ArtLibrary.Glow(new Color(1.4f, 1.15f, 0.45f));
            for (var i = 0; i < 5; i++)
            {
                var a = i * Mathf.PI * 2f / 5f;
                var star = World.ArtLibrary.Sphere(_stars, "Star", Vector3.zero, 0.06f, gold, false).transform;
                star.localPosition = new Vector3(Mathf.Cos(a) * 0.32f, 0f, Mathf.Sin(a) * 0.32f);
            }
            _stars.gameObject.SetActive(false);
        }

        /// <summary>Two poles beside the mat with a glowing bar at the ×2 and ×3 apex heights; a bar lights once the jump reached it.</summary>
        private void BuildTiers()
        {
            var root = _stage.Root.transform;
            var pole = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F6E4C8"), 0.4f);
            var heights = new[] { (float)_game.Tune.Tier2Apex, (float)_game.Tune.Tier3Apex };
            var colours = new[] { new Color(0.4f, 0.85f, 0.65f), new Color(1f, 0.78f, 0.35f) };
            foreach (var x in new[] { -1.62f, 1.62f })
                World.ArtLibrary.Cylinder(root, "Pole", World.MgStage3D.At(x, (MatTop + heights[1] + 0.4f) * 0.5f, 0.35f), 0.035f, MatTop + heights[1] + 0.4f, pole, false);
            for (var i = 0; i < 2; i++)
            {
                var m = new Material(World.ArtLibrary.Glow(colours[i]));
                _ownedMats.Add(m);
                _tierMats.Add(m);
                foreach (var x in new[] { -1.62f, 1.62f })
                {
                    var b = World.ArtLibrary.Box(root, "Tier", World.MgStage3D.At(x, MatTop + heights[i], 0.35f), new Vector3(0.34f, 0.07f, 0.07f), m, false).transform;
                    _tierBars.Add(b);
                }
            }
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            var gb = _stage.Gooby;
            // the mat dips with the impact and the shock ring rolls out over it
            var dip = _shock * 0.3f;
            _cloth.position = World.MgStage3D.At(0f, MatTop - 0.01f - dip, 0f);
            _cloth.localScale = new Vector3(MatR * 2f * (1f + dip * 0.3f), 0.01f, MatR * 2f * (1f + dip * 0.3f));
            var h = (float)g.Height;
            _shadowDisc.position = World.MgStage3D.At(0f, MatTop + 0.015f - dip * 0.6f, 0f);
            _shadowDisc.localScale = Vector3.one * Mathf.Clamp(1.25f - h * 0.18f, 0.35f, 1.25f);
            _shockRing.gameObject.SetActive(_shock > 0.05f);
            if (_shock > 0.05f)
            {
                _shockRing.position = World.MgStage3D.At(0f, MatTop + 0.02f - dip * 0.5f, 0f);
                _shockRing.localScale = Vector3.one * (0.3f + (1f - _shock) * MatR * 1.4f);
                var c = _shockMat.color;
                c.a = _shock * 0.6f;
                _shockMat.color = c;
                if (_shockMat.HasProperty("_BaseColor")) _shockMat.SetColor("_BaseColor", c);
            }
            // the height bars glow once reached and flash on the tier-up
            var tiers = new[] { g.Tune.Tier2Apex, g.Tune.Tier3Apex };
            for (var i = 0; i < 2; i++)
            {
                var reached = g.Apex >= tiers[i];
                var glow = reached ? 1f + 0.3f * Mathf.Sin(_show * 7f) : 0.35f;
                glow += _tierFlash * 1.5f;
                var baseC = i == 0 ? new Color(0.4f, 0.85f, 0.65f) : new Color(1f, 0.78f, 0.35f);
                var col = baseC * glow;
                col.a = 1f;
                _tierMats[i].color = col;
                if (_tierMats[i].HasProperty("_BaseColor")) _tierMats[i].SetColor("_BaseColor", col);
                if (_tierMats[i].HasProperty("_EmissionColor")) _tierMats[i].SetColor("_EmissionColor", col);
                for (var k = 0; k < 2; k++) _tierBars[i * 2 + k].localScale = new Vector3(0.34f, 0.07f, 0.07f) * (1f + (reached ? _tierFlash * 0.5f : 0f));
            }
            // the crowd: a combo or a ×3 tier brings everyone up, a boost two of three (one frame of cheer, they sit back by themselves)
            _crowd?.Tick(dt, _crowdNow, _crowdBig);
            _crowdNow = _crowdBig = false;
            _stars.gameObject.SetActive(g.Staggering);
            if (gb == null) return;
            gb.transform.position = World.MgStage3D.At(0f, MatTop + h - dip * (h < 0.05f ? 1f : 0f), 0f);
            // tricks: the flip cartwheels in the screen plane, the spin whirls about the up axis, the twist rolls and turns
            var spin = (float)g.TrickSpin * Mathf.Rad2Deg;
            Quaternion rot;
            if (g.Tricking > 0.0)
            {
                if (_shownTrick != g.LastTrick)
                {
                    _shownTrick = g.LastTrick;
                    gb.SetEmotion("ecstatic");
                }
                rot = g.LastTrick == "flip" ? Quaternion.Euler(0f, 0f, -spin) * Quaternion.Euler(0f, 180f, 0f)
                    : g.LastTrick == "spin" ? Quaternion.Euler(0f, 180f + spin, 0f)
                    : Quaternion.Euler(0f, 0f, spin) * Quaternion.Euler(0f, 180f + spin * 0.5f, 0f);
                _trickYaw = 0f;
            }
            else
            {
                if (_shownTrick.Length > 0) { _shownTrick = ""; gb.SetEmotion(g.Staggering ? "dizzy" : "happy"); }
                rot = Quaternion.Slerp(gb.transform.rotation, Quaternion.Euler(0f, 180f + _trickYaw, 0f), Mathf.Min(1f, dt * 14f));
            }
            gb.transform.rotation = rot;
            // stretch rising, squash on impact; slumped after a butt landing
            _squash = Mathf.Max(0f, _squash - dt * 5f);
            if (_shock > 0.9f) _squash = 1f;
            Vector3 scale;
            if (g.Staggering) scale = Vector3.Lerp(gb.transform.localScale / GoobyScale, new Vector3(1.16f, 0.78f, 1.16f), Mathf.Min(1f, dt * 10f));
            else
            {
                var nearMat = Mathf.Clamp01(1f - h * 1.6f);
                var s = Mathf.Clamp((float)g.Vy * 0.022f, -0.1f, 0.18f) - _squash * 0.3f * nearMat;
                scale = new Vector3(1f - s * 0.5f, 1f + s, 1f - s * 0.5f);
            }
            gb.transform.localScale = scale * GoobyScale;
            if (g.Staggering)
            {
                _starsSpin += dt * 4f;
                _stars.position = gb.transform.position + Vector3.up * 1.05f;
                _stars.rotation = Quaternion.Euler(0f, _starsSpin * Mathf.Rad2Deg, 0f);
            }
        }

        private void StageLanded(float strength)
        {
            if (_stage == null || ReducedMotion) return;
            World.Effects.Puff(World.MgStage3D.At(0f, MatTop + 0.08f, -0.35f), new Color(0.95f, 0.92f, 0.85f, 0.85f), strength > 0.75f ? 12 : 6, 0.16f, 1.1f, 0.5f, Vector3.up, 0.9f);
        }

        private void StageBoost()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("celebrate", 0.1f);
            gb.SetEmotion("ecstatic");
            _crowdNow = true;
            if (!ReducedMotion) World.Effects.Sparkle(World.MgStage3D.At(0f, MatTop + 0.3f, -0.2f), 0.6f, 18);
        }

        private void StageButt()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("dizzy");
            gb.Play("refuse", 0.1f);
            if (!ReducedMotion) World.Effects.Puff(World.MgStage3D.At(0f, MatTop + 0.1f, 0f), new Color(0.8f, 0.68f, 0.52f, 0.9f), 10, 0.2f, 1.2f, 0.6f, Vector3.up, 1f);
        }

        private void StageCheer(bool big)
        {
            _crowdNow = true;
            _crowdBig |= big;
            var gb = _stage?.Gooby;
            if (gb != null && big && !ReducedMotion) World.Effects.Sparkle(gb.transform.position + Vector3.up * 0.6f, 0.5f, 24);
        }

        private void DropStage()
        {
            _crowd?.Destroy();
            _crowd = null;
            _stage?.Destroy();
            _stage = null;
            foreach (var m in _ownedMats) if (m != null) Object.Destroy(m);
            _ownedMats.Clear();
            _tierMats.Clear();
            _tierBars.Clear();
            _shownTrick = "";
            _squash = 0f;
            _crowdNow = _crowdBig = false;
        }
    }
}
