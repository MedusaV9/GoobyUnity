using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The 3D harbour canal of the Hafen-Hüpfer (port of harbor_hopper_world.gd): a chase camera follows the cutter with the
    /// player's Gooby at the helm up a teal canal between stone quays with bollards, lanterns and pastel harbour houses,
    /// wooden piers reaching in from the banks; foam waves roll towards the boat with a glittering gold sweet spot, crates
    /// bob, red-and-white life rings float, red buoys sway, the horn cone flashes ahead, a gull circles the cutter and
    /// flaps off with a crate. Crates on deck stack up as he collects them. Engine metres are stage metres: x across the
    /// canal, z ahead (relative to the boat, so the water streams past).
    /// </summary>
    public sealed partial class HarborHopperScreen
    {
        private World.MgStage3D _stage;
        private Transform _boat3d, _cone, _gull, _deck, _houses, _bollards;
        private readonly Dictionary<HarborHopper.Item, Transform> _items3d = new Dictionary<HarborHopper.Item, Transform>();
        private readonly Dictionary<HarborHopper.Pier, Transform> _piers3d = new Dictionary<HarborHopper.Pier, Transform>();
        private readonly Dictionary<HarborHopper.Wave, Transform> _waves3d = new Dictionary<HarborHopper.Wave, Transform>();
        private readonly Dictionary<string, Stack<Transform>> _pool = new Dictionary<string, Stack<Transform>>();
        private readonly HashSet<object> _seen = new HashSet<object>();
        private readonly List<object> _drop = new List<object>();
        private readonly List<Transform> _deckCrates = new List<Transform>();
        private Material _coneMat;
        private float _clock;

        private const float HouseStep = 9f, HouseRows = 11, BollardStep = 8f;
        private static readonly string[] Houses = { "house_blue", "house_pink", "house_yellow", "house_cream", "house_salmon", "house_green" };

        protected override bool StageHasGooby => _stage != null;

        private static Vector3 H(double x, double relZ, float y = 0f) => World.MgStage3D.At((float)x, y, (float)relZ);

        private void BuildStage()
        {
            _stage = new World.MgStage3D(ViewW, ViewH, new Color(0.7f, 0.86f, 0.98f));
            _stage.Bind(Field);
            _stage.Aim(new Vector3(0f, 3.6f, -7.2f), 12f, 52f);
            _stage.Camera.farClipPlane = 160f;
            var root = _stage.Root.transform;
            var half = (float)_game.Tune.ChannelHalfW;
            // water, quays and the land behind them
            World.ArtLibrary.Box(root, "Water", H(0, 60, -0.12f), new Vector3(half * 2f + 1.6f, 0.1f, 160f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#7FC9CF"), 0.88f), false);
            foreach (var s in new[] { -1f, 1f })
            {
                World.ArtLibrary.Box(root, "Quay", H(s * (half + 2.4f), 60, 0.12f), new Vector3(3.2f, 0.6f, 160f), World.ArtLibrary.PbrSlab("paving", new Vector2(3.2f, 160f), 1.2f, World.ArtLibrary.Hex("#E8DCCB"), 0.2f), false);
                World.ArtLibrary.Box(root, "QuayWall", H(s * (half + 0.82f), 60, -0.1f), new Vector3(0.12f, 0.55f, 160f), World.ArtLibrary.Pbr("paving", 0.9f, World.ArtLibrary.Hex("#C9BBA8"), 0.15f), false);
            }
            // the land beside the quays (never across the canal, or it would cover the water)
            foreach (var s in new[] { -1f, 1f })
                World.ArtLibrary.Box(root, "Land", H(s * (half + 34f), 60, 0.33f), new Vector3(60f, 0.1f, 170f), World.ArtLibrary.PbrSlab("grass", new Vector2(60f, 170f), 2.4f, World.ArtLibrary.Hex("#A9D89A"), 0.04f, 0.6f), false);
            _houses = new GameObject("Houses").transform;
            _houses.SetParent(root, false);
            for (var k = 0; k < HouseRows; k++)
                foreach (var s in new[] { -1f, 1f })
                {
                    var row = new GameObject("Row").transform;
                    row.SetParent(_houses, false);
                    _stage.PlaceScenery(Houses[(k * 2 + (s > 0f ? 1 : 0)) % Houses.Length], H(s * (half + 7.5f), 0, 0.42f), s > 0f ? -90f : 90f, 1f).SetParent(row, true);
                    if (k % 2 == 0) _stage.PlaceScenery("tree_round", H(s * (half + 3.4f), 4.5f, 0.42f), k * 30f, 1.1f).SetParent(row, true);
                    else _stage.PlaceScenery("lamp_post", H(s * (half + 3.4f), 4.5f, 0.42f), 0f, 0.9f).SetParent(row, true);
                    row.localPosition = new Vector3(0f, 0f, k * HouseStep);
                }
            _bollards = new GameObject("Bollards").transform;
            _bollards.SetParent(root, false);
            var iron = World.ArtLibrary.Solid(World.ArtLibrary.Hex("#5E6672"), 0.5f);
            for (var i = 0; i < 20; i++)
                foreach (var s in new[] { -1f, 1f })
                {
                    World.ArtLibrary.Cylinder(_bollards, "Bollard", H(s * (half + 1.15f), i * BollardStep, 0.62f), 0.14f, 0.36f, iron);
                    World.ArtLibrary.Cylinder(_bollards, "Cap", H(s * (half + 1.15f), i * BollardStep, 0.82f), 0.2f, 0.06f, iron, false);
                }
            _stage.MergeChildren(_bollards, "Bollards");
            _stage.Hills(0.38f, 150f, 9, new Color(0.66f, 0.8f, 0.7f), 14f, 160f, 0f, 41);
            _stage.Clouds(6, 18f, 130f, 110f, 4f, 42);
            _stage.Sun(new Vector3(18f, 26f, 150f), 5f);
            // the cutter: Kenney fishing boat, the horn cone, the gull and the deck crates
            _boat3d = new GameObject("Cutter").transform;
            _boat3d.SetParent(root, false);
            var hull = _stage.Prop("boat-fishing-small", Vector3.zero, 0.62f, 0f);
            hull.SetParent(_boat3d, false);
            hull.localPosition = new Vector3(0f, -0.1f, 0f);
            _deck = new GameObject("Deck").transform;
            _deck.SetParent(_boat3d, false);
            _deck.localPosition = new Vector3(0f, 0.55f, -0.65f);
            _coneMat = new Material(World.ArtLibrary.Transparent(new Color(1f, 0.92f, 0.5f, 0.3f)));
            var cone = new GameObject("HornCone").transform;
            cone.SetParent(root, false);
            var coneMesh = World.ArtLibrary.Box(cone, "Cone", Vector3.zero, Vector3.one, _coneMat, false).transform;
            coneMesh.localPosition = new Vector3(0f, 0.05f, 0.5f);
            _cone = cone;
            _cone.gameObject.SetActive(false);
            _gull = _stage.PlaceScenery("dove", H(0, 6, 3f), 0f, 1.4f);
            _gull.gameObject.SetActive(false);
            var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(Gooby.Core.Json.J.Dict(Host.Store.State["cosmetics"]));
            _ = SpawnCaptain(outfit);
        }

        private async System.Threading.Tasks.Task SpawnCaptain(Dictionary<string, string> outfit)
        {
            var g = await _stage.SpawnGoobyAsync(_boat3d.position, 0.62f, outfit);
            if (g == null || _stage == null) return;
            g.transform.SetParent(_boat3d, true);
            g.transform.localPosition = new Vector3(0f, 0.42f, 0.15f);
            g.transform.localRotation = Quaternion.identity;
            g.Play("idle-loop", 0f);
            g.SetEmotion("happy");
        }

        private Transform Take(string kind)
        {
            if (_pool.TryGetValue(kind, out var st) && st.Count > 0) { var t = st.Pop(); t.gameObject.SetActive(true); return t; }
            Transform made;
            var root = _stage.Root.transform;
            switch (kind)
            {
                case "crate":
                    made = _stage.PlaceScenery("shop_crate", Vector3.zero, 0f, 1.15f);
                    break;
                case "ring":
                {
                    // a white life ring with four red bands
                    made = new GameObject("ring").transform;
                    made.SetParent(root, false);
                    var ring = _stage.Torus(made, "Ring", made.position, 0.62f, 0.16f, World.ArtLibrary.Solid(Color.white, 0.4f)).transform;
                    ring.localPosition = Vector3.zero;
                    for (var k = 0; k < 4; k++)
                    {
                        var band = World.ArtLibrary.Box(made, "Band", Vector3.zero, new Vector3(0.2f, 0.34f, 0.36f), World.ArtLibrary.Solid(World.ArtLibrary.Hex("#F26D6D"), 0.4f), false).transform;
                        var a = k * 90f + 45f;
                        band.localPosition = Quaternion.Euler(0f, a, 0f) * new Vector3(0.62f, 0f, 0f);
                        band.localRotation = Quaternion.Euler(0f, a, 0f);
                    }
                    break;
                }
                case "pier":
                    made = new GameObject("pier").transform;
                    made.SetParent(root, false);
                    var plank = World.ArtLibrary.Pbr("wood_planks", 0.8f, World.ArtLibrary.Hex("#D9A77A"), 0.25f);
                    World.ArtLibrary.Box(made, "Deck", Vector3.zero, Vector3.one, plank).transform.localPosition = Vector3.zero;
                    break;
                case "wave":
                    made = new GameObject("wave").transform;
                    made.SetParent(root, false);
                    var foam = World.ArtLibrary.Box(made, "Foam", Vector3.zero, new Vector3(1f, 0.14f, 0.7f), World.ArtLibrary.Solid(new Color(0.97f, 0.99f, 1f), 0.6f), false).transform;
                    foam.localPosition = Vector3.zero;
                    var sweet = World.ArtLibrary.Box(made, "Sweet", Vector3.zero, new Vector3(1f, 0.2f, 0.8f), World.ArtLibrary.Glow(new Color(1.4f, 1.2f, 0.5f)), false).transform;
                    sweet.name = "Sweet";
                    break;
                default:
                    made = _stage.Prop("buoy", Vector3.zero, 0.75f);
                    break;
            }
            made.name = kind;
            return made;
        }

        private void Retire(Transform t)
        {
            if (t == null) return;
            t.gameObject.SetActive(false);
            if (!_pool.TryGetValue(t.name, out var st)) _pool[t.name] = st = new Stack<Transform>();
            st.Push(t);
        }

        private void RenderStage()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _clock += dt;
            var g = _game;
            var s = g.Engine.S;
            var half = g.Tune.ChannelHalfW;
            var travel = (float)s.Z;
            for (var i = 0; i < _houses.childCount; i++)
            {
                var row = _houses.GetChild(i);
                var k = i / 2;
                row.localPosition = new Vector3(0f, 0f, Mathf.Repeat(k * HouseStep - travel + (i % 2) * 4.5f, HouseStep * HouseRows) - 8f);
            }
            _bollards.localPosition = new Vector3(0f, 0f, -Mathf.Repeat(travel, BollardStep) - 4f);
            _seen.Clear();
            foreach (var item in g.Engine.Items)
            {
                var rel = item.Z - s.Z;
                if (item.Gone || rel < -4.0 || rel > 70.0) continue;
                _seen.Add(item);
                if (!_items3d.TryGetValue(item, out var tr)) _items3d[item] = tr = Take(item.Type == "crate" || item.Type == "ring" ? item.Type : "buoy");
                var bob = Mathf.Sin(_clock * 3f + (float)item.Z) * 0.06f;
                tr.position = H(item.X, rel, (item.Type == "crate" ? 0.05f : 0.02f) + bob);
                tr.rotation = item.Type == "ring" ? Quaternion.Euler(Mathf.Sin(_clock * 2f + (float)item.Z) * 8f, (float)item.Z * 37f, 0f) : Quaternion.Euler(Mathf.Sin(_clock * 1.6f + (float)item.Z) * 6f, (float)item.Z * 23f, 0f);
            }
            foreach (var pier in g.Engine.Piers)
            {
                var rel = pier.Z - s.Z;
                if (rel < -4.0 || rel > 70.0) continue;
                _seen.Add(pier);
                if (!_piers3d.TryGetValue(pier, out var tr)) _piers3d[pier] = tr = Take("pier");
                var inner = half - g.Tune.PierReachM;
                var x0 = pier.Side * (half + 0.8);
                var len = (float)System.Math.Abs(x0 - pier.Side * inner);
                tr.position = H((x0 + pier.Side * inner) * 0.5, rel, 0.3f);
                tr.localScale = new Vector3(len, 0.18f, (float)g.Tune.PierDepthM);
            }
            foreach (var wave in g.Engine.Waves)
            {
                var rel = wave.Z - s.Z;
                if (rel < -3.0 || rel > 70.0) continue;
                _seen.Add(wave);
                if (!_waves3d.TryGetValue(wave, out var tr)) _waves3d[wave] = tr = Take("wave");
                tr.position = H(0, rel, 0.02f + Mathf.Sin(_clock * 3f + (float)wave.Z) * 0.04f);
                var foam = tr.GetChild(0);
                foam.localScale = new Vector3((float)half * 2f, 0.14f, 0.7f);
                var sweet = tr.GetChild(1);
                sweet.gameObject.SetActive(!wave.Ridden);
                sweet.localPosition = new Vector3((float)wave.SweetX, 0.03f, 0f);
                sweet.localScale = new Vector3((float)g.Tune.SweetHalfW * 2f * (0.92f + 0.08f * Mathf.Sin(_clock * 8f)), 0.2f, 0.8f);
            }
            Sweep(_items3d); Sweep(_piers3d); Sweep(_waves3d);
            // the cutter heels into its turns and blinks after a bump; crates pile on deck
            _boat3d.position = H(s.X, 0.0, Mathf.Sin(_clock * 2.4f) * 0.04f);
            _boat3d.rotation = Quaternion.Euler(Mathf.Sin(_clock * 1.7f) * 2f, (float)s.Vx * 4f, Mathf.Clamp(-(float)s.Vx * 5f, -14f, 14f));
            _boat3d.gameObject.SetActive(!(s.IframesT > 0.0 && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.4f));
            SyncDeck(Mathf.Min(4, s.Crates));
            if (s.BoostT > 0.0 && !ReducedMotion && Random.value < 0.6f)
                World.Effects.Puff(_boat3d.position + new Vector3(0f, 0.1f, -1.3f), new Color(1f, 1f, 1f, 0.8f), 2, 0.2f, 1.6f, 0.5f, Vector3.back + Vector3.up * 0.3f, 0.6f);
            _cone.gameObject.SetActive(_hornFlash > 0.0);
            if (_hornFlash > 0.0)
            {
                var len = (float)g.Tune.HornConeM;
                var width = (float)(g.Tune.HornConeBase * 2.0 + g.Tune.HornConeM * g.Tune.HornConeSpread);
                _cone.position = _boat3d.position + Vector3.up * 0.4f + Vector3.forward * 0.8f;
                _cone.localScale = new Vector3(width, 0.6f, len);
                var c = new Color(1f, 0.92f, 0.5f, (float)(_hornFlash / 0.5) * 0.35f);
                _coneMat.color = c;
                if (_coneMat.HasProperty("_BaseColor")) _coneMat.SetColor("_BaseColor", c);
            }
            // the gull circles the cutter while it warns, then flaps off over the houses
            _gull.gameObject.SetActive(_gullMode != "");
            if (_gullMode == "circle")
            {
                var a = (float)_gullT * 3f;
                _gull.position = _boat3d.position + new Vector3(Mathf.Cos(a) * 1.6f, 2.2f + Mathf.Sin(a * 2f) * 0.2f, Mathf.Sin(a) * 1.6f);
                _gull.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
            else if (_gullMode == "leave")
            {
                var k = (float)_gullT;
                _gull.position = _boat3d.position + new Vector3(k * 4f, 2.2f + k * 3f, k * 3f);
            }
        }

        private void SyncDeck(int crates)
        {
            while (_deckCrates.Count < crates)
            {
                var c = _stage.PlaceScenery("shop_crate", _deck.position, 0f, 0.42f);
                c.SetParent(_deck, true);
                var i = _deckCrates.Count;
                c.localPosition = new Vector3((i % 2) * 0.38f - 0.19f, (i / 2) * 0.32f, 0f);
                c.localRotation = Quaternion.Euler(0f, i * 17f, 0f);
                _deckCrates.Add(c);
            }
            while (_deckCrates.Count > crates)
            {
                var last = _deckCrates[_deckCrates.Count - 1];
                _deckCrates.RemoveAt(_deckCrates.Count - 1);
                if (last != null) Object.Destroy(last.gameObject);
            }
        }

        private void Sweep<TKey>(Dictionary<TKey, Transform> map)
        {
            _drop.Clear();
            foreach (var kv in map) if (!_seen.Contains(kv.Key)) _drop.Add(kv.Key);
            foreach (TKey k in _drop) { Retire(map[k]); map.Remove(k); }
        }

        private void StageMood(string emotion, bool sparkle = false)
        {
            var gb = _stage?.Gooby;
            if (gb == null) return;
            gb.SetEmotion(emotion);
            if (sparkle && !ReducedMotion) World.Effects.Sparkle(_boat3d.position + Vector3.up * 1.1f, 0.5f, 14);
        }

        private void StageBump()
        {
            StageMood("dizzy");
            if (_stage != null && !ReducedMotion) World.Effects.Puff(_boat3d.position + Vector3.up * 0.3f, new Color(0.85f, 0.95f, 1f, 0.9f), 14, 0.2f, 2f, 0.5f, Vector3.up, 1f);
        }

        private void DropStage()
        {
            _items3d.Clear(); _piers3d.Clear(); _waves3d.Clear(); _pool.Clear(); _seen.Clear(); _drop.Clear(); _deckCrates.Clear();
            if (_coneMat != null) Object.Destroy(_coneMat);
            _coneMat = null;
            _stage?.Destroy();
            _stage = null;
        }
    }
}
