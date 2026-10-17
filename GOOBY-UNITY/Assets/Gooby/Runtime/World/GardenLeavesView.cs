using System.Collections.Generic;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The day's fallen leaves on the garden lawn (<see cref="GardenLeaves"/>): each lies in the grass a hand's breadth
    /// above it, tinted in the season's colours (the golden one glints now and then), and stirs a little in the breeze.
    /// A picked leaf spins up and away. The world decides which leaves lie and picks them (tap or Gooby walking over);
    /// this only draws them. Every leaf carries a generous collider + <see cref="LeafMarker"/> for taps.
    /// </summary>
    public sealed class GardenLeavesView
    {
        public const float LeafLength = 0.5f;
        public const float FlySeconds = 0.75f;

        public GameObject Root { get; }

        private readonly RoomDefs.Room _def;
        private readonly Vector3 _origin;
        private readonly List<Leaf> _leaves = new List<Leaf>();
        private readonly List<Flying> _flying = new List<Flying>();
        private float _glintTimer = 1.5f;

        private sealed class Leaf
        {
            public GardenLeaves.Spot Spot;
            public GameObject Go;
            public Transform Blade;
            public Quaternion Rest;
            public float Phase;
        }

        private struct Flying
        {
            public Transform T;
            public Vector3 From;
            public float Age, Spin, Yaw;
        }

        public GardenLeavesView(Transform parent, RoomDefs.Room def, Vector3 origin)
        {
            _def = def;
            _origin = origin;
            Root = new GameObject("Leaves");
            Root.transform.SetParent(parent, false);
        }

        /// <summary>The leaves still lying (spot + world position on the lawn).</summary>
        public IEnumerable<(GardenLeaves.Spot spot, Vector3 at)> Lying
        {
            get { foreach (var l in _leaves) yield return (l.Spot, l.Go.transform.position); }
        }

        public int Count => _leaves.Count;

        public Vector3 WorldOf(GardenLeaves.Spot s) => SimpleWorld.WorldOf(_def, _origin, s.X, s.Y);

        /// <summary>
        /// Season palettes (slot 0..3), all light and warm enough to stand out on the lawn — no green on green: spring
        /// brings fresh lime and a blossom pink, summer straw and apricot. The golden leaf is sun-yellow.
        /// </summary>
        public static Color Tint(string season, int slot, bool golden)
        {
            if (golden) return ArtLibrary.Hex("#FFD84A");
            string[] hex;
            switch (season)
            {
                case "herbst": hex = new[] { "#FF8A3D", "#F1553E", "#F6A93A", "#E0703F" }; break;
                case "winter": hex = new[] { "#E3AA6A", "#D69456", "#ECBE80", "#CC8A58" }; break;
                case "fruehling": hex = new[] { "#C4EA74", "#FFB3C8", "#DDF08A", "#FF9DB5" }; break;
                default: hex = new[] { "#D5EC6E", "#F3D46B", "#FFB46B", "#E9E27A" }; break;
            }
            return ArtLibrary.Hex(hex[Mathf.Clamp(slot, 0, 3)]);
        }

        public void Rebuild(string season, IEnumerable<GardenLeaves.Spot> spots)
        {
            foreach (var l in _leaves) Object.Destroy(l.Go);
            _leaves.Clear();
            foreach (var s in spots) _leaves.Add(Make(season, s));
        }

        private Leaf Make(string season, GardenLeaves.Spot s)
        {
            var go = new GameObject("Leaf:" + s.Id);
            go.transform.SetParent(Root.transform, false);
            go.transform.position = WorldOf(s) + Vector3.up * 0.03f;
            go.transform.rotation = Quaternion.Euler(0f, (float)s.YawDeg, 0f);
            go.AddComponent<LeafMarker>().Id = s.Id;
            var hit = go.AddComponent<BoxCollider>();
            hit.center = new Vector3(0f, 0.08f, 0f);
            hit.size = new Vector3(0.72f, 0.2f, 0.72f);

            // face up (the quad faces +Z), stem on the grass and the tip lifted a little, so the sun throws a sliver of
            // shadow under it
            var size = s.Golden ? LeafLength * 1.15f : LeafLength;
            var mat = ArtLibrary.Cutout(LeafTexture(), "leaf", Tint(season, s.Tint, s.Golden));
            var rest = Quaternion.Euler(-90f + 9f, 0f, (s.Tint - 1.5f) * 4f);
            var blade = ArtLibrary.Quad(go.transform, "Blade", go.transform.position, go.transform.rotation * rest, new Vector2(size * 0.72f, size), mat, shadows: true);
            blade.transform.SetParent(go.transform, true);
            return new Leaf { Spot = s, Go = go, Blade = blade.transform, Rest = blade.transform.localRotation, Phase = (float)(s.YawDeg * 0.0174533) };
        }

        public bool Has(string id) => _leaves.Exists(l => l.Spot.Id == id);

        public GardenLeaves.Spot? Find(string id)
        {
            var l = _leaves.Find(x => x.Spot.Id == id);
            return l == null ? (GardenLeaves.Spot?)null : l.Spot;
        }

        /// <summary>The nearest lying leaf within <paramref name="radius"/> metres of a floor position (Gooby's feet).</summary>
        public GardenLeaves.Spot? Near(Vector3 floorPos, float radius)
        {
            Leaf best = null;
            var bestD = radius * radius;
            foreach (var l in _leaves)
            {
                var p = l.Go.transform.position;
                var d = (p.x - floorPos.x) * (p.x - floorPos.x) + (p.z - floorPos.z) * (p.z - floorPos.z);
                if (d <= bestD) { bestD = d; best = l; }
            }
            return best?.Spot;
        }

        /// <summary>Takes a leaf off the lawn: it spins up and fades away (or just goes, when <paramref name="fly"/> is off).</summary>
        public Vector3 Remove(string id, bool fly)
        {
            var i = _leaves.FindIndex(l => l.Spot.Id == id);
            if (i < 0) return Vector3.zero;
            var leaf = _leaves[i];
            _leaves.RemoveAt(i);
            var at = leaf.Go.transform.position;
            Object.Destroy(leaf.Go.GetComponent<Collider>());
            if (fly) _flying.Add(new Flying { T = leaf.Go.transform, From = at, Spin = leaf.Spot.Tint % 2 == 0 ? 540f : -540f, Yaw = (float)leaf.Spot.YawDeg });
            else Object.Destroy(leaf.Go);
            return at;
        }

        public void Tick(float dt, bool reducedMotion)
        {
            var t = Time.time;
            if (!reducedMotion)
                foreach (var l in _leaves)
                {
                    // a breath of wind lifts the free edge now and then
                    var stir = Mathf.Max(0f, Mathf.Sin(t * 0.9f + l.Phase)) * Mathf.Sin(t * 5.3f + l.Phase * 3f);
                    l.Blade.localRotation = l.Rest * Quaternion.Euler(stir * 7f, 0f, stir * 3f);
                }
            _glintTimer -= dt;
            if (_glintTimer <= 0f)
            {
                _glintTimer = 2.2f;
                foreach (var l in _leaves)
                    if (l.Spot.Golden) Effects.Sparkle(l.Go.transform.position + Vector3.up * 0.06f, 0.18f, 5);
            }
            for (var i = _flying.Count - 1; i >= 0; i--)
            {
                var f = _flying[i];
                f.Age += dt;
                var k = Mathf.Clamp01(f.Age / FlySeconds);
                if (f.T == null) { _flying.RemoveAt(i); continue; }
                f.T.position = f.From + new Vector3(0f, Mathf.Sin(k * Mathf.PI * 0.5f) * 1.1f, 0f);
                f.T.rotation = Quaternion.Euler(0f, f.Yaw + f.Spin * k, 0f);
                f.T.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.2f, k * k);
                if (k >= 1f) { Object.Destroy(f.T.gameObject); _flying.RemoveAt(i); continue; }
                _flying[i] = f;
            }
        }

        private static Texture2D _tex;

        /// <summary>
        /// A light leaf to be tinted: pointed oval with a gently lobed edge, a stem, a darker mid-rib and side veins,
        /// and a darker rim that keeps it readable as a small collectible on the grass. Drawn once at startup (like
        /// <see cref="Effects.Petal"/>), with mipmaps for the distance.
        /// </summary>
        public static Texture2D LeafTexture()
        {
            if (_tex != null) return _tex;
            const int n = 128;
            _tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "fx_leaf", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var u = (x + 0.5f) / n * 2f - 1f;   // across
                    var v = (y + 0.5f) / n * 2f - 1f;   // stem (−1) → tip (+1)
                    var t = Mathf.InverseLerp(-0.8f, 0.96f, v);
                    var w = t <= 0f || t >= 1f ? 0f : 0.66f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.85f)), 0.75f);
                    w *= 1f + 0.07f * Mathf.Sin(t * Mathf.PI * 9f);
                    var au = Mathf.Abs(u);
                    var inBlade = au < w;
                    var inStem = au < 0.035f && v >= -0.98f && v < -0.7f;
                    if (!inBlade && !inStem) { px[y * n + x] = new Color32(255, 255, 255, 0); continue; }
                    var lum = 0.97f;
                    if (inBlade)
                    {
                        var edge = au / Mathf.Max(0.001f, w);
                        lum -= 0.13f * Mathf.Pow(edge, 3f);
                        if (edge > 0.86f || t < 0.035f || t > 0.965f) lum = 0.58f;
                        else if (au < 0.03f) lum = 0.74f;
                        else
                        {
                            var vein = Mathf.Repeat((v - au * 0.85f) * 5.2f, 1f);
                            if (vein < 0.09f && au < w * 0.86f) lum -= 0.13f;
                        }
                        if (u < 0f) lum -= 0.04f;
                    }
                    else lum = 0.7f;
                    var b = (byte)(Mathf.Clamp01(lum) * 255f);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            _tex.SetPixels32(px);
            _tex.Apply(true, true);
            return _tex;
        }
    }

    /// <summary>Tap target of a fallen leaf on the lawn (the world picks it).</summary>
    public sealed class LeafMarker : MonoBehaviour
    {
        public string Id;
    }
}
