using System.Collections.Generic;
using Gooby.Core.Minigames.Play;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D street of the Gooby-Renner (port of runner_world.gd): a chase camera follows the player's Gooby down a
    /// three-lane town street — asphalt with scrolling dashes, pavements, pastel town houses, round trees and lamps both
    /// sides, hills and clouds ahead. Cones, crates, barriers, scaffolds and real cars come at him in their lanes; coins spin
    /// and mystery boxes glow where the engine has them; he runs, jumps, slides, stumbles and blinks after a hit, a magnet
    /// ring and a shield bubble show the power-ups. Engine z (negative ahead) is stage depth ahead of him; the swipes
    /// stay the field's.
    /// </summary>
    public sealed partial class RunnerScreen
    {
        private World.MgStage3D _stage;
        private readonly Dictionary<RunnerGame.Obstacle, Transform> _obs3d = new Dictionary<RunnerGame.Obstacle, Transform>();
        private readonly Dictionary<RunnerGame.Coin, Transform> _coins3d = new Dictionary<RunnerGame.Coin, Transform>();
        private readonly Dictionary<RunnerGame.Mystery, Transform> _boxes3d = new Dictionary<RunnerGame.Mystery, Transform>();
        private readonly Dictionary<string, Stack<Transform>> _obsPool = new Dictionary<string, Stack<Transform>>();
        private readonly Stack<Transform> _coinPool = new Stack<Transform>(), _boxPool = new Stack<Transform>();
        private readonly HashSet<object> _live = new HashSet<object>();
        private readonly List<object> _dead = new List<object>();
        private readonly List<Transform> _houses = new List<Transform>();
        private Transform _dashes, _verge, _magnet, _shieldBubble;
        private float _runClip = -1f;
        private bool _slideShown;

        private const float HouseStep = 9.5f, HouseRows = 11, DashStep = 3.2f, VergeStep = 4f;
        private static readonly string[] Houses = { "house_pink", "house_yellow", "house_blue", "house_cream", "house_green", "house_salmon" };
        private static readonly string[] Cars = { "sedan-sports", "taxi", "hatchback-sports", "police" };

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 At(double x, double y, double z) => World.MgStage3D.At((float)x, (float)y, (float)z);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.66f, 0.83f, 0.98f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 2.6f, -4.6f), 13f, 50f);
            var root = _stage.Root.transform;
            // ground, road, pavements and the scrolling dashes
            _stage.Ground(-0.02f, World.ArtLibrary.Hex("#9CCF8C"), "grass", 60f, 140f, 60f);
            World.ArtLibrary.Box(root, "Road", At(0f, -0.04f, 55f), new Vector3(4.6f, 0.1f, 130f), World.ArtLibrary.PbrSlab("asphalt", new Vector2(4.6f, 130f), 3f, World.ArtLibrary.Hex("#A9A6C2"), 0.25f), false);
            foreach (var s in new[] { -1f, 1f })
            {
                World.ArtLibrary.Box(root, "Pavement", At(s * 3.15f, 0.02f, 55f), new Vector3(1.7f, 0.14f, 130f), World.ArtLibrary.PbrSlab("paving", new Vector2(1.7f, 130f), 1.2f, World.ArtLibrary.Hex("#E8DCCB"), 0.2f), false);
                World.ArtLibrary.Box(root, "Kerb", At(s * 2.32f, 0.04f, 55f), new Vector3(0.12f, 0.16f, 130f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#D7CDBF"), 0.3f), false);
            }
            _dashes = new GameObject("Dashes").transform;
            _dashes.SetParent(root, false);
            var paint = World.ArtLibrary.Solid(new Color(0.97f, 0.92f, 0.6f), 0.4f);
            for (var i = 0; i < 34; i++)
                foreach (var x in new[] { -0.55f, 0.55f })
                    World.ArtLibrary.Box(_dashes, "Dash", At(x, 0.015f, i * DashStep), new Vector3(0.09f, 0.01f, 1.4f), paint, false);
            _stage.MergeChildren(_dashes, "Dashes");
            // houses, trees and lamps that stream past both sides
            for (var i = 0; i < HouseRows; i++)
                foreach (var s in new[] { -1f, 1f })
                {
                    var row = new GameObject("Row").transform;
                    row.SetParent(root, false);
                    var house = Houses[(i * 2 + (s > 0f ? 1 : 0)) % Houses.Length];
                    _stage.PlaceScenery(house, At(s * 8.4f, 0f, 0f), s > 0f ? -90f : 90f, 1f).SetParent(row, true);
                    _stage.PlaceScenery(i % 2 == 0 ? "tree_round" : "lamp_post", At(s * 4.4f, 0.08f, HouseStep * 0.5f), 0f, i % 2 == 0 ? 0.9f : 1f).SetParent(row, true);
                    row.localPosition = new Vector3(0f, 0f, i * HouseStep);
                    _houses.Add(row);
                }
            _verge = new GameObject("Verge").transform;
            _verge.SetParent(root, false);
            for (var i = 0; i < 26; i++)
                foreach (var s in new[] { -1f, 1f })
                    _stage.Prop(i % 3 == 0 ? "flower_redA" : i % 3 == 1 ? "flower_yellowA" : "grass_large", At(s * (4.05f + (i % 2) * 0.25f), 0.08f, i * VergeStep), 1.4f, i * 41f).SetParent(_verge, true);
            _stage.Hills(-0.02f, 120f, 9, new Color(0.66f, 0.82f, 0.62f), 9f, 140f, 0f, 3);
            _stage.Hills(-0.02f, 100f, 7, new Color(0.56f, 0.76f, 0.52f), 6f, 110f, 0f, 4);
            _stage.Clouds(6, 14f, 110f, 90f, 3.2f, 8);
            _stage.Sun(new Vector3(-26f, 26f, 140f), 4f);
            _magnet = _stage.Torus(null, "Magnet", Vector3.zero, 0.75f, 0.05f, World.ArtLibrary.Glow(new Color(0.55f, 1f, 1.3f))).transform;
            _magnet.gameObject.SetActive(false);
            _shieldBubble = World.ArtLibrary.Sphere(root, "Shield", Vector3.zero, 0.7f, World.ArtLibrary.Transparent(new Color(0.55f, 0.85f, 1f, 0.22f)), false).transform;
            _shieldBubble.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnRunner(outfit);
        }

        private async System.Threading.Tasks.Task SpawnRunner(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(At(0f, 0f, 0f), 0.95f, outfit);
            if (g == null || _stage == null) return;
            g.transform.rotation = Quaternion.identity;
            g.Play("walk-loop", 0f);
            g.SetEmotion("happy");
        }

        private Transform TakeObstacle(string kind)
        {
            if (_obsPool.TryGetValue(kind, out var pool) && pool.Count > 0)
            {
                var t = pool.Pop();
                t.gameObject.SetActive(true);
                return t;
            }
            switch (kind)
            {
                case "cone": return _stage.Prop("construction-cone", Vector3.zero, 1.6f);
                case "box": return _stage.Prop("box_A", Vector3.zero, 1.6f);
                case "barrier": return _stage.Prop("construction-barrier", Vector3.zero, 1.7f);
                case "overhead":
                {
                    var scaffold = new GameObject("Scaffold").transform;
                    scaffold.SetParent(_stage.Root.transform, false);
                    var steel = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#8C96A6"), 0.6f);
                    var plank = World.ArtLibrary.Pbr("wood_planks", 0.8f, World.ArtLibrary.Hex("#E0B07A"), 0.25f);
                    foreach (var s in new[] { -0.5f, 0.5f })
                        World.ArtLibrary.Box(scaffold, "Post", Vector3.zero, new Vector3(0.08f, 1.3f, 0.08f), steel).transform.localPosition = new Vector3(s, 0.65f, 0f);
                    World.ArtLibrary.Box(scaffold, "Board", Vector3.zero, new Vector3(1.15f, 0.12f, 0.5f), plank).transform.localPosition = new Vector3(0f, 1.28f, 0f);
                    var stripe = World.ArtLibrary.Box(scaffold, "Stripe", Vector3.zero, new Vector3(1.15f, 0.12f, 0.04f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#FF8B8B"), 0.4f), false);
                    stripe.transform.localPosition = new Vector3(0f, 1.28f, -0.26f);
                    return scaffold;
                }
                default: return _stage.Prop(Cars[Random.Range(0, Cars.Length)], Vector3.zero, 2.1f, 0f);
            }
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var g = _game;
            var run = (float)g.Meters;
            _dashes.localPosition = new Vector3(0f, 0f, -Mathf.Repeat(run, DashStep));
            _verge.localPosition = new Vector3(0f, 0f, -Mathf.Repeat(run, VergeStep));
            for (var i = 0; i < _houses.Count; i++)
            {
                var row = i / 2;
                var z = Mathf.Repeat(row * HouseStep - run, HouseStep * HouseRows) - HouseStep;
                _houses[i].localPosition = new Vector3(0f, 0f, z);
            }
            _live.Clear();
            foreach (var ob in g.Obstacles)
            {
                if (ob.Z < RunnerGame.SpawnZ + 2.0 || ob.Z > RunnerGame.DespawnZ) continue;
                _live.Add(ob);
                if (!_obs3d.TryGetValue(ob, out var tr)) { tr = TakeObstacle(ob.Kind); tr.name = ob.Kind; _obs3d[ob] = tr; }
                tr.position = At(g.Tune.LaneX[ob.Lane], 0f, -ob.Z);
                tr.rotation = Quaternion.Euler(0f, ob.Kind == "car" ? 180f : (float)ob.Yaw * Mathf.Rad2Deg, 0f);
            }
            foreach (var c in g.CoinsOnRoad)
            {
                if (c.Z < RunnerGame.SpawnZ + 2.0 || c.Z > RunnerGame.DespawnZ) continue;
                _live.Add(c);
                if (!_coins3d.TryGetValue(c, out var tr))
                {
                    if (_coinPool.Count > 0) { tr = _coinPool.Pop(); tr.gameObject.SetActive(true); }
                    else tr = _stage.Prop("item-coin-gold", Vector3.zero, 1.8f);
                    _coins3d[c] = tr;
                }
                tr.position = At(g.Tune.LaneX[c.Lane], c.Y + 0.05, -c.Z);
                tr.rotation = Quaternion.Euler(0f, _show * 200f + c.Lane * 40f, 0f);
            }
            foreach (var m in g.Mysteries)
            {
                if (m.Z < RunnerGame.SpawnZ + 2.0 || m.Z > RunnerGame.DespawnZ) continue;
                _live.Add(m);
                if (!_boxes3d.TryGetValue(m, out var tr))
                {
                    if (_boxPool.Count > 0) { tr = _boxPool.Pop(); tr.gameObject.SetActive(true); }
                    else
                    {
                        tr = _stage.Prop("item-box", Vector3.zero, 2.2f);
                        World.ArtLibrary.Sphere(tr, "Glow", tr.position + Vector3.up * 0.25f, 0.42f, World.ArtLibrary.Transparent(new Color(0.55f, 0.72f, 1f, 0.25f)), false);
                    }
                    _boxes3d[m] = tr;
                }
                tr.position = At(g.Tune.LaneX[m.Lane], 0.45f + Mathf.Sin(_show * 3f) * 0.08f, -m.Z);
                tr.rotation = Quaternion.Euler(0f, _show * 90f, 0f);
            }
            Retire(_obs3d, t => (_obsPool.TryGetValue(t.name, out var p) ? p : _obsPool[t.name] = new Stack<Transform>()));
            Retire(_coins3d, _ => _coinPool);
            Retire(_boxes3d, _ => _boxPool);
            PlaceRunner(dt);
        }

        private void Retire<TKey>(Dictionary<TKey, Transform> map, System.Func<Transform, Stack<Transform>> pool)
        {
            _dead.Clear();
            foreach (var kv in map) if (!_live.Contains(kv.Key)) _dead.Add(kv.Key);
            foreach (TKey k in _dead)
            {
                var t = map[k];
                if (t != null) { t.gameObject.SetActive(false); pool(t).Push(t); }
                map.Remove(k);
            }
        }

        /// <summary>Gooby runs in his lane (the run cycle follows the speed), hops on a jump, ducks on a slide, blinks after a hit.</summary>
        private void PlaceRunner(float dt)
        {
            var gb = _stage.Gooby;
            var g = _game;
            var at = At(g.LaneX, g.PlayerY, 0.0);
            _magnet.gameObject.SetActive(g.MagnetT > 0.0);
            if (g.MagnetT > 0.0)
            {
                _magnet.position = at + Vector3.up * 0.5f;
                _magnet.rotation = Quaternion.Euler(0f, _show * 120f, 0f);
            }
            _shieldBubble.gameObject.SetActive(g.Shield);
            if (g.Shield) _shieldBubble.position = at + Vector3.up * 0.5f;
            if (gb == null) return;
            gb.gameObject.SetActive(!(g.Invuln > 0.0 && (int)(g.Invuln * 12.0) % 2 == 1));
            gb.transform.position = at;
            var lean = (float)(g.Tune.LaneX[g.LaneNow()] - g.LaneX) * 18f;
            gb.transform.rotation = Quaternion.Euler(0f, lean, 0f);
            var speed = Mathf.Clamp((float)g.Speed / 9f, 0.8f, 2.2f);
            if (!Mathf.Approximately(speed, _runClip)) { _runClip = speed; gb.SetClipSpeed("walk-loop", speed * 1.6f); }
            if (g.Sliding != _slideShown)
            {
                _slideShown = g.Sliding;
                gb.Play(g.Sliding ? "sit-loop" : "walk-loop", 0.08f);
            }
            gb.transform.localScale = g.Sliding ? new Vector3(1.15f, 0.7f, 1.15f) * 0.95f : Vector3.one * 0.95f;
        }

        private void StageJump()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.Play("hop", 0.05f);
            gb.Play("walk-loop", 0.5f);
        }

        private void StageCoin()
        {
            var gb = _stage?.Gooby;
            if (gb == null || ReducedMotion) return;
            World.Effects.Sparkle(gb.transform.position + Vector3.up * 0.7f, 0.3f, 8);
        }

        private void StageMood(string emotion)
        {
            _stage?.Gooby?.SetEmotion(emotion);
        }

        private void StageStumble()
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion("dizzy");
            if (!ReducedMotion) World.Effects.Puff(gb.transform.position + Vector3.up * 0.2f, new Color(0.75f, 0.7f, 0.62f, 0.9f), 10, 0.18f, 1.4f, 0.5f, Vector3.up, 1f);
        }

        private void DropStage()
        {
            _obs3d.Clear(); _coins3d.Clear(); _boxes3d.Clear(); _obsPool.Clear(); _coinPool.Clear(); _boxPool.Clear();
            _live.Clear(); _dead.Clear(); _houses.Clear();
            _runClip = -1f;
            _slideShown = false;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
