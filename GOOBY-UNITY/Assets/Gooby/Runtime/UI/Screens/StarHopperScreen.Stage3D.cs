using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D space corridor of the Sternenhüpfer (port of star_hopper_stage.gd): the chase camera looks down three glowing
    /// lanes, Gooby pilots a little speeder that slides and banks between them, real space-kit meteors tumble towards him,
    /// stars and golden carrots spin in the lanes, the shield is a bubble around the ship, a shower paints its danger lanes
    /// red and the wormhole tints the void violet. Stars stream past for the speed. Taps stay left/right halves of the
    /// field, so the stage frames the flight freely (only the look changes).
    /// </summary>
    public sealed partial class StarHopperScreen
    {
        private World.MgStage3D _stage;
        private Transform _ship3d, _shipTilt, _shield, _flame;
        private readonly List<Transform> _rungs = new List<Transform>();
        private readonly List<Transform> _laneWarn3d = new List<Transform>();
        private readonly List<Transform> _streaks = new List<Transform>();
        private readonly Dictionary<StarHopperGame.Meteor, Transform> _meteors3d = new Dictionary<StarHopperGame.Meteor, Transform>();
        private readonly Dictionary<StarHopperGame.Pickup, Transform> _pickups3d = new Dictionary<StarHopperGame.Pickup, Transform>();
        private float _emoteLeft;
        private const float LaneGap = 0.8f, MetresScale = 0.42f;
        private static readonly string[] Rocks = { "meteor", "meteor_detailed", "meteor_half" };

        protected override bool StageHasGooby => _stage != null;

        private static float LaneX3(double lane) => (float)(lane - 1.0) * LaneGap;

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewSize.w, ViewSize.h, new Color(0.13f, 0.1f, 0.27f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 2.5f, -5.2f), 15f, 56f);
            var root = _stage.Root.transform;
            // the lanes: three glowing rails with rungs that stream towards the camera
            var rail = World.ArtLibrary.Glow(new Color(0.55f, 0.6f, 1.2f));
            for (var l = 0; l <= 3; l++)
                World.ArtLibrary.Box(root, "Rail", World.MgStage3D.At((l - 1.5f) * LaneGap, -0.25f, 18f), new Vector3(0.035f, 0.035f, 44f), rail, false);
            var rung = World.ArtLibrary.Glow(new Color(0.42f, 0.38f, 0.9f));
            for (var i = 0; i < 22; i++) _rungs.Add(World.ArtLibrary.Box(root, "Rung", World.MgStage3D.At(0f, -0.26f, i * 2f), new Vector3(LaneGap * 3f, 0.02f, 0.06f), rung, false).transform);
            var warn = World.ArtLibrary.Transparent(new Color(1f, 0.3f, 0.3f, 0.22f));
            for (var l = 0; l < 3; l++)
            {
                var w = World.ArtLibrary.Box(root, "Warn", World.MgStage3D.At(LaneX3(l), -0.24f, 18f), new Vector3(LaneGap * 0.92f, 0.02f, 44f), warn, false).transform;
                w.gameObject.SetActive(false);
                _laneWarn3d.Add(w);
            }
            // the void: far stars, two nebula glows, the ringed planet and a lilac moon
            var rng = new System.Random(9);
            var star = World.ArtLibrary.Glow(new Color(1.3f, 1.25f, 1.1f));
            for (var i = 0; i < 70; i++)
                World.ArtLibrary.Sphere(root, "Star", World.MgStage3D.At((float)(rng.NextDouble() - 0.5) * 60f, (float)rng.NextDouble() * 30f - 4f, 50f + (float)rng.NextDouble() * 10f), 0.06f + (float)rng.NextDouble() * 0.1f, star, false);
            // stars streaming past close by (the speed)
            var streak = World.ArtLibrary.Glow(new Color(0.9f, 0.9f, 1.2f));
            for (var i = 0; i < 26; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var s = World.ArtLibrary.Box(root, "Streak", World.MgStage3D.At(side * (2.2f + (float)rng.NextDouble() * 4f), (float)rng.NextDouble() * 5f - 1.5f, (float)rng.NextDouble() * 40f), new Vector3(0.03f, 0.03f, 0.9f), streak, false).transform;
                _streaks.Add(s);
            }
            var nebA = World.ArtLibrary.Sphere(root, "Nebula", World.MgStage3D.At(-9f, 8f, 46f), 9f, World.ArtLibrary.Transparent(new Color(1f, 0.55f, 0.85f, 0.12f)), false);
            nebA.transform.localScale = new Vector3(22f, 12f, 4f);
            var nebB = World.ArtLibrary.Sphere(root, "Nebula", World.MgStage3D.At(10f, 3f, 48f), 9f, World.ArtLibrary.Transparent(new Color(0.45f, 0.9f, 0.95f, 0.1f)), false);
            nebB.transform.localScale = new Vector3(20f, 10f, 4f);
            World.ArtLibrary.Sphere(root, "Planet", World.MgStage3D.At(-5.5f, 7.5f, 40f), 2.6f, World.ArtLibrary.Solid(new Color(1f, 0.7f, 0.54f), 0.3f), false);
            var ring = World.ArtLibrary.Cylinder(root, "Ring", World.MgStage3D.At(-5.5f, 7.5f, 40f), 4.2f, 0.06f, World.ArtLibrary.Transparent(new Color(1f, 0.89f, 0.64f, 0.75f)), false);
            ring.transform.rotation = Quaternion.Euler(70f, 0f, -16f);
            World.ArtLibrary.Sphere(root, "Moon", World.MgStage3D.At(6f, 10f, 42f), 1.1f, World.ArtLibrary.Solid(new Color(0.86f, 0.84f, 0.97f), 0.2f), false);
            // the ship: a Kenney speeder with Gooby at the controls, a flame and the shield bubble
            _ship3d = new GameObject("Ship").transform;
            _ship3d.SetParent(root, false);
            _ship3d.position = World.MgStage3D.At(0f, 0f, 0f);
            _shipTilt = new GameObject("Tilt").transform;
            _shipTilt.SetParent(_ship3d, false);
            var craft = _stage.Prop("craft_speederA", _ship3d.position, 0.55f, 180f);
            craft.SetParent(_shipTilt, true);
            _flame = World.ArtLibrary.Sphere(_shipTilt, "Flame", _ship3d.position + new Vector3(0f, 0.12f, -0.75f), 0.16f, World.ArtLibrary.Glow(new Color(1.6f, 0.9f, 0.4f)), false).transform;
            _flame.SetParent(_shipTilt, true);
            _shield = World.ArtLibrary.Sphere(_ship3d, "Shield", _ship3d.position + Vector3.up * 0.35f, 0.9f, World.ArtLibrary.Transparent(new Color(0.5f, 0.85f, 1f, 0.25f)), false).transform;
            _shield.SetParent(_ship3d, true);
            _shield.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnPilot(outfit);
        }

        private async System.Threading.Tasks.Task SpawnPilot(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(_ship3d.position, 0.55f, outfit);
            if (g == null || _stage == null) return;
            g.transform.SetParent(_shipTilt, true);
            g.transform.localPosition = new Vector3(0f, 0.16f, 0.02f);
            g.transform.localRotation = Quaternion.identity;
            g.Play("sit-loop");
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            var travel = (float)g.Traveled * MetresScale;
            for (var i = 0; i < _rungs.Count; i++)
                _rungs[i].position = World.MgStage3D.At(0f, -0.26f, Mathf.Repeat(i * 2f - travel, 44f) - 2f);
            for (var i = 0; i < _streaks.Count; i++)
            {
                var p = _streaks[i].position;
                var z = Mathf.Repeat(i * 1.6f - travel * 2.2f, 42f) - 2f;
                _streaks[i].position = new Vector3(p.x, p.y, World.MgStage3D.Origin.z + z);
            }
            for (var l = 0; l < 3; l++) _laneWarn3d[l].gameObject.SetActive(g.ShowerState != "idle" && g.ShowerSafeLane != l);
            // the ship slides and banks; the flame grows with the speed
            _ship3d.position = World.MgStage3D.At(LaneX3(g.LaneVisual), 0f, 0f);
            _shipTilt.localRotation = Quaternion.Euler(0f, 0f, (float)(g.Lane - g.LaneVisual) * 26f + Mathf.Sin(_pulse * 2f) * 2f);
            _shipTilt.localPosition = new Vector3(0f, Mathf.Sin(_pulse * 3f) * 0.05f, 0f);
            _flame.localScale = Vector3.one * (0.32f + Mathf.Sin(_pulse * 30f) * 0.05f + (float)(g.Speed - 11.0) * 0.02f);
            _shield.gameObject.SetActive(g.Shielded);
            // meteors tumble down their lanes
            var live = new HashSet<StarHopperGame.Meteor>(g.Meteors);
            foreach (var m in g.Meteors)
            {
                if (!_meteors3d.TryGetValue(m, out var tr))
                {
                    tr = _stage.Prop(Rocks[_meteors3d.Count % Rocks.Length], Vector3.zero, m.Approach > 0.0 ? 1.15f : 0.95f);
                    _meteors3d[m] = tr;
                }
                tr.position = World.MgStage3D.At(LaneX3(m.Lane), 0.2f, (float)(m.M - g.Traveled) * MetresScale);
                tr.rotation = Quaternion.Euler((float)m.Spin * Mathf.Rad2Deg + _pulse * 60f, _pulse * 40f, 0f);
            }
            List<StarHopperGame.Meteor> dead = null;
            foreach (var kv in _meteors3d) if (!live.Contains(kv.Key)) (dead ??= new List<StarHopperGame.Meteor>()).Add(kv.Key);
            if (dead != null) foreach (var d in dead) { if (_meteors3d[d] != null) Object.Destroy(_meteors3d[d].gameObject); _meteors3d.Remove(d); }
            var livePk = new HashSet<StarHopperGame.Pickup>(g.Pickups);
            foreach (var p in g.Pickups)
            {
                if (!_pickups3d.TryGetValue(p, out var tr)) _pickups3d[p] = tr = SpawnPickup(p.Kind);
                tr.position = World.MgStage3D.At(LaneX3(p.Lane), 0.35f + Mathf.Sin(_pulse * 3f + p.Lane) * 0.08f, (float)(p.M - g.Traveled) * MetresScale);
                tr.rotation = Quaternion.Euler(0f, _pulse * 120f, 0f);
            }
            List<StarHopperGame.Pickup> deadPk = null;
            foreach (var kv in _pickups3d) if (!livePk.Contains(kv.Key)) (deadPk ??= new List<StarHopperGame.Pickup>()).Add(kv.Key);
            if (deadPk != null) foreach (var d in deadPk) { if (_pickups3d[d] != null) Object.Destroy(_pickups3d[d].gameObject); _pickups3d.Remove(d); }
            _stage.Camera.backgroundColor = g.WormholeLeft > 0.0 ? new Color(0.3f, 0.12f, 0.4f) : new Color(0.13f, 0.1f, 0.27f);
            var gb = _stage.Gooby;
            if (gb != null && _emoteLeft > 0f && (_emoteLeft -= dt) <= 0f) gb.SetEmotion("happy");
        }

        private Transform SpawnPickup(string kind)
        {
            if (kind == "gold")
            {
                var carrot = _stage.Prop("carrot", Vector3.zero, 1.0f);
                World.ArtLibrary.Sphere(carrot, "Glow", carrot.position + Vector3.up * 0.25f, 0.3f, World.ArtLibrary.Glow(new Color(1.6f, 1.2f, 0.45f)), false).transform.SetParent(carrot, true);
                return carrot;
            }
            var t = new GameObject(kind).transform;
            t.SetParent(_stage.Root.transform, false);
            if (kind == "shield")
            {
                var bubble = World.ArtLibrary.Sphere(t, "Bubble", Vector3.zero, 0.26f, World.ArtLibrary.Transparent(new Color(0.55f, 0.9f, 1f, 0.45f)), false);
                bubble.transform.SetParent(t, false);
                var core = World.ArtLibrary.Sphere(t, "Heart", Vector3.zero, 0.1f, World.ArtLibrary.Glow(new Color(1.4f, 0.6f, 0.75f)), false);
                core.transform.SetParent(t, false);
                return t;
            }
            // a chunky five-point star: two crossed squashed boxes rotated in fives
            var gold = World.ArtLibrary.Glow(new Color(1.6f, 1.3f, 0.45f));
            for (var i = 0; i < 5; i++)
            {
                var arm = World.ArtLibrary.Box(t, "Arm", Vector3.zero, new Vector3(0.09f, 0.3f, 0.06f), gold, false);
                arm.transform.SetParent(t, false);
                arm.transform.localRotation = Quaternion.Euler(0f, 0f, i * 72f);
                arm.transform.localPosition = arm.transform.localRotation * new Vector3(0f, 0.11f, 0f);
            }
            return t;
        }

        private void StageEvent(string what)
        {
            var g = _stage?.Gooby;
            if (g == null) return;
            switch (what)
            {
                case "pickup": g.SetEmotion("happy"); if (!ReducedMotion) World.Effects.Sparkle(_ship3d.position + Vector3.up * 0.6f, 0.4f, 18); break;
                case "shield": g.SetEmotion("ecstatic"); break;
                case "hit": g.SetEmotion("dizzy"); if (!ReducedMotion) World.Effects.Puff(_ship3d.position + Vector3.up * 0.4f, new Color(0.8f, 0.75f, 0.85f), 14, 0.12f, 2f, 0.5f, Vector3.up, 1.4f); break;
                default: g.SetEmotion("scared"); break;
            }
            _emoteLeft = 1f;
        }

        private void DropStage()
        {
            _rungs.Clear(); _laneWarn3d.Clear(); _streaks.Clear(); _meteors3d.Clear(); _pickups3d.Clear();
            _stage?.Destroy();
            _stage = null;
        }
    }
}
