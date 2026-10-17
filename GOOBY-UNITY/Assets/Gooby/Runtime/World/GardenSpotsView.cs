using System.Collections.Generic;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The workbench finds on the bed field (<see cref="GardenSpots"/>): a forked twig with a leaf for a stick, a fan of
    /// three green leaves for a leaf bundle, each on its free soil tile with a soft glint now and then so they are
    /// noticed. A tap picks one (the world books it); the find hops up, spins and shrinks away. Every find carries a
    /// generous collider + <see cref="SpotMarker"/> that wins over the bed tile underneath.
    /// </summary>
    public sealed class GardenSpotsView
    {
        public const float FlySeconds = 0.6f;

        public GameObject Root { get; }

        private readonly GardenBeds _beds;
        private readonly Dictionary<Cell, GameObject> _spots = new Dictionary<Cell, GameObject>();
        private readonly List<(Transform t, Vector3 from, float age)> _flying = new List<(Transform, Vector3, float)>();
        private float _glint = 1.2f;
        private int _glintNext;

        public GardenSpotsView(Transform parent, GardenBeds beds)
        {
            _beds = beds;
            Root = new GameObject("Spots");
            Root.transform.SetParent(parent, false);
        }

        public int Count => _spots.Count;

        public IEnumerable<Cell> Cells => _spots.Keys;

        public void Rebuild(IEnumerable<GardenSpots.Spot> spots)
        {
            var want = new Dictionary<Cell, string>();
            foreach (var s in spots) want[s.At] = s.Material;
            foreach (var cell in new List<Cell>(_spots.Keys))
            {
                if (want.TryGetValue(cell, out var m) && _spots[cell].name == "Spot:" + m) continue;
                Object.Destroy(_spots[cell]);
                _spots.Remove(cell);
            }
            foreach (var kv in want)
                if (!_spots.ContainsKey(kv.Key)) _spots[kv.Key] = Make(kv.Key, kv.Value);
        }

        private GameObject Make(Cell at, string material)
        {
            var go = new GameObject("Spot:" + material);
            go.transform.SetParent(Root.transform, false);
            var yaw = (at.X * 47 + at.Y * 83) % 360;
            go.transform.position = _beds.TileCenter(at) + Vector3.up * 0.005f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<SpotMarker>().Cell = at;
            var hit = go.AddComponent<BoxCollider>();
            hit.center = new Vector3(0f, 0.12f, 0f);
            hit.size = new Vector3(0.46f, 0.3f, 0.46f);
            var t = go.transform;
            var leaf = ArtLibrary.Solid(ArtLibrary.Hex("#7FCF9B"), 0.3f);
            if (material == "blatt")
            {
                var stem = ArtLibrary.Cylinder(t, "Stem", t.position + new Vector3(0f, 0.03f, 0f), 0.012f, 0.12f, ArtLibrary.Solid(ArtLibrary.Hex("#5FB884"), 0.2f));
                stem.transform.SetParent(t, true);
                stem.transform.rotation = t.rotation * Quaternion.Euler(80f, 0f, 0f);
                foreach (var (a, c) in new[] { (-38f, "#9EDB86"), (0f, "#7FCF9B"), (38f, "#B6E89A") })
                {
                    var blade = ArtLibrary.Sphere(t, "Leaf", t.position, 0.5f, ArtLibrary.Solid(ArtLibrary.Hex(c), 0.35f));
                    blade.transform.SetParent(t, true);
                    blade.transform.localRotation = Quaternion.Euler(0f, a, 0f);
                    blade.transform.localPosition = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.035f, 0.1f);
                    blade.transform.localScale = new Vector3(0.11f, 0.03f, 0.2f);
                }
            }
            else
            {
                var bark = ArtLibrary.Solid(ArtLibrary.Hex("#E2B27C"), 0.15f);
                var stick = ArtLibrary.Cylinder(t, "Stick", t.position + new Vector3(0f, 0.03f, 0f), 0.032f, 0.42f, bark);
                stick.transform.SetParent(t, true);
                stick.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                stick.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                var twig = ArtLibrary.Cylinder(t, "Twig", t.position, 0.016f, 0.16f, bark);
                twig.transform.SetParent(t, true);
                twig.transform.localRotation = Quaternion.Euler(0f, -35f, 90f);
                twig.transform.localPosition = new Vector3(0.08f, 0.03f, 0.05f);
                var tip = ArtLibrary.Sphere(t, "Leaf", t.position, 0.5f, leaf);
                tip.transform.SetParent(t, true);
                tip.transform.localPosition = new Vector3(0.15f, 0.04f, 0.11f);
                tip.transform.localScale = new Vector3(0.09f, 0.025f, 0.06f);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        /// <summary>Takes a find off the field; it hops, spins and shrinks (or just goes with reduced motion). Returns where it lay.</summary>
        public Vector3 Remove(Cell at, bool fly)
        {
            if (!_spots.TryGetValue(at, out var go)) return Vector3.zero;
            _spots.Remove(at);
            var pos = go.transform.position;
            Object.Destroy(go.GetComponent<Collider>());
            if (fly) _flying.Add((go.transform, pos, 0f));
            else Object.Destroy(go);
            return pos;
        }

        public void Tick(float dt, bool reducedMotion)
        {
            _glint -= dt;
            if (_glint <= 0f && _spots.Count > 0)
            {
                _glint = reducedMotion ? 6f : 2.4f;
                var i = 0;
                foreach (var go in _spots.Values)
                    if (i++ == _glintNext % _spots.Count) { Effects.Sparkle(go.transform.position + Vector3.up * 0.1f, 0.16f, 5); break; }
                _glintNext++;
            }
            for (var i = _flying.Count - 1; i >= 0; i--)
            {
                var (t, from, age) = _flying[i];
                age += dt;
                var k = Mathf.Clamp01(age / FlySeconds);
                if (t == null) { _flying.RemoveAt(i); continue; }
                t.position = from + Vector3.up * Mathf.Sin(k * Mathf.PI * 0.7f) * 0.8f;
                t.Rotate(0f, 900f * dt, 0f, Space.World);
                t.localScale = Vector3.one * Mathf.Lerp(1.2f, 0.1f, k * k);
                if (k >= 1f) { Object.Destroy(t.gameObject); _flying.RemoveAt(i); continue; }
                _flying[i] = (t, from, age);
            }
        }
    }

    /// <summary>Tap target of a workbench find on the bed field (the world picks it).</summary>
    public sealed class SpotMarker : MonoBehaviour
    {
        public Cell Cell;
    }
}
