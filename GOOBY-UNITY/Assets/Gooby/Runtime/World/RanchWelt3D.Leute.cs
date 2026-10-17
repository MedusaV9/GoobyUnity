using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The people out in the region, on their day's routes (rnpc_routine.gd): Luna the wild-horse whisperer at the edge of
    /// the Weidetal and up on the hill crest at midday, Knips the photographer on the hill, at the lake's lookout and at the
    /// arena, Herr Punktabzug the judge at the arena with a lunch break by the lake, Eilfried the postman at the farm gate,
    /// in the farmyard and by the lake — and Timmi from the farm comes down to the lake at noon; at home they are not out.
    /// Plus the day's things to collect (<see cref="RanchSammeln"/>): golden horseshoes, flowers, berries, boards, the rusty
    /// key and tufts of wool, bobbing and turning so they catch the eye from afar.
    /// </summary>
    public sealed partial class RanchWelt3D
    {
        private readonly Dictionary<string, Transform> _leute = new Dictionary<string, Transform>();
        private readonly Dictionary<string, (double x, double z)> _leutePos = new Dictionary<string, (double, double)>();
        private readonly Dictionary<string, string> _leuteOrt = new Dictionary<string, string>();
        private readonly Dictionary<string, (Transform t, RanchSammeln.Stelle stelle)> _sammel = new Dictionary<string, (Transform, RanchSammeln.Stelle)>();
        private readonly Dictionary<string, float> _sammelY = new Dictionary<string, float>();

        /// <summary>The people met out riding.</summary>
        public static readonly string[] RegionLeute = { "luna", "knips", "punktabzug", "eilfried", "timmi" };

        /// <summary>The routines' places in the region (x, z); the farm's own places and the houses are elsewhere.</summary>
        public static readonly IReadOnlyDictionary<string, (double x, double z)> RegionOrte = new Dictionary<string, (double, double)>
        {
            ["waldrand"] = (-572.0, 214.0), ["huegel"] = (126.0, -384.0), ["teich"] = (604.0, 94.0), ["reitplatz"] = (74.0, 452.0),
            ["tor"] = (66.0, 236.0), ["hof_mitte"] = (4.0, 66.0),
        };

        private Dictionary<string, Dictionary<string, string>> _leuteLooks;

        public void ZeigeLeute(Dictionary<string, Dictionary<string, string>> looks) => _leuteLooks = looks ?? new Dictionary<string, Dictionary<string, string>>();

        /// <summary>
        /// Everyone to the place their day has them at <paramref name="stunde"/> (a few metres apart when several meet by the
        /// lake); away from the region they are not drawn and not met.
        /// </summary>
        public void TickLeute(double stunde)
        {
            TiereZeit(stunde, null);
            if (_leuteLooks == null) return;
            var proOrt = new Dictionary<string, int>();
            foreach (var id in RegionLeute)
            {
                var ort = RanchNpcRoutine.StationBei(id, stunde)?.Ort ?? "";
                if (!RegionOrte.TryGetValue(ort, out var basis))
                {
                    if (_leute.TryGetValue(id, out var weg) && weg != null) weg.gameObject.SetActive(false);
                    _leutePos.Remove(id);
                    _leuteOrt.Remove(id);
                    continue;
                }
                proOrt.TryGetValue(ort, out var k);
                proOrt[ort] = k + 1;
                if (_leuteOrt.TryGetValue(id, out var alt) && alt == ort && _leute.TryGetValue(id, out var da) && da != null)
                {
                    da.gameObject.SetActive(true);
                    continue;
                }
                var (x, z) = (basis.x + k * 3.4, basis.z - k * 2.2);
                var zone = RanchKarte.ZoneBei(x, z);
                var g = Gruppe(zone.Length > 0 ? zone : RanchEntdeckerKarte.HinweisZone(new V2d(x, z)));
                if (!_leute.TryGetValue(id, out var anchor) || anchor == null)
                {
                    anchor = new GameObject("Leute:" + id).transform;
                    _leute[id] = anchor;
                    _ = SpawnPersonAsync(anchor, _leuteLooks.TryGetValue(id, out var look) ? look : null);
                }
                anchor.SetParent(g, false);
                anchor.position = Boden(x, z);
                anchor.rotation = Quaternion.Euler(0f, (float)((x * 37.0 + z * 11.0) % 360.0), 0f);
                anchor.gameObject.SetActive(true);
                _leutePos[id] = (x, z);
                _leuteOrt[id] = ort;
                Debug.Log("[ranchwelt] leute " + id + " → " + ort);
            }
        }

        /// <summary>Where a person is now (null = not out in the region).</summary>
        public string LeuteOrt(string id) => _leuteOrt.TryGetValue(id ?? "", out var o) ? o : null;

        private async Task SpawnPersonAsync(Transform anchor, Dictionary<string, string> look)
        {
            var avatar = await GoobyAvatar.CreateAsync(anchor, anchor.position);
            if (avatar == null || _destroyed || anchor == null) return;
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            avatar.transform.localScale = Vector3.one * 1.1f;
            if (look != null) avatar.Wear(look);
            avatar.Play("idle-loop");
            avatar.SetEmotion("happy");
        }

        /// <summary>The person within <paramref name="r"/> metres of (x, z), nearest first ("" = nobody).</summary>
        public string PersonBei(double x, double z, double r)
        {
            var best = "";
            var bestD = r;
            foreach (var kv in _leutePos)
            {
                var (px, pz) = kv.Value;
                var d = Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            return best;
        }

        /// <summary>Over a person's head (for the quest marker), region coordinates with height; null when not out.</summary>
        public (double x, double y, double z)? PersonKopf(string id)
        {
            if (!_leutePos.TryGetValue(id ?? "", out var p)) return null;
            return (p.x, RanchGelaende.Hoehe(p.x, p.z) + 2.05, p.z);
        }

        // ── the things to collect ──

        public void ZeigeSammeln(IEnumerable<RanchSammeln.Stelle> stellen, ICollection<string> schon)
        {
            foreach (var s in stellen)
            {
                if (schon != null && schon.Contains(s.Id) || _sammel.ContainsKey(s.Id)) continue;
                var zone = RanchKarte.ZoneBei(s.X, s.Z);
                var g = Gruppe(zone.Length > 0 ? zone : RanchEntdeckerKarte.HinweisZone(new V2d(s.X, s.Z)));
                var root = new GameObject("Sammel:" + s.Id).transform;
                root.SetParent(g, false);
                root.position = Boden(s.X, s.Z, 0.55);
                BaueSache(root, s.Item);
                ArtLibrary.Cylinder(root, "Glanz", root.position - Vector3.up * 0.5f, 0.75f, 0.02f, ArtLibrary.Glow(new Color(1.25f, 1.1f, 0.6f)), false);
                _sammel[s.Id] = (root, s);
                _sammelY[s.Id] = root.position.y;
            }
        }

        private void BaueSache(Transform root, string item)
        {
            var p = root.position;
            switch (item)
            {
                case "hufeisen":
                    var gold = M("#E8C060", 0.7f);
                    ArtLibrary.Box(root, "Bogen", p + new Vector3(0f, 0.18f, 0f), new Vector3(0.5f, 0.1f, 0.1f), gold);
                    ArtLibrary.Box(root, "Schenkel", p + new Vector3(-0.22f, -0.05f, 0f), new Vector3(0.1f, 0.42f, 0.1f), gold);
                    ArtLibrary.Box(root, "Schenkel", p + new Vector3(0.22f, -0.05f, 0f), new Vector3(0.1f, 0.42f, 0.1f), gold);
                    break;
                case "blume":
                    Glb(root, Ranch(new[] { "flower_redA", "flower_yellowA", "flower_purpleA" }[Math.Abs(root.name.GetHashCode()) % 3]), 0, 0, 3.2f, 0f).localPosition = new Vector3(0f, -0.55f, 0f);
                    break;
                case "beere":
                    foreach (var (dx, dy, c) in new[] { (-0.1f, 0f, "#7A3B8F"), (0.1f, 0.02f, "#C0394B"), (0f, 0.14f, "#7A3B8F") })
                        ArtLibrary.Sphere(root, "Beere", p + new Vector3(dx, dy, 0f), 0.11f, M(c, 0.6f), false);
                    ArtLibrary.Sphere(root, "Blatt", p + new Vector3(0f, -0.14f, 0f), 0.18f, M("#5E9F4E"), false).transform.localScale = new Vector3(0.4f, 0.12f, 0.3f);
                    break;
                case "brett":
                    var b = ArtLibrary.Box(root, "Brett", p, new Vector3(1.3f, 0.1f, 0.32f), M("#B98A5E"));
                    b.transform.rotation = Quaternion.Euler(0f, 0f, 25f);
                    break;
                case "schluessel":
                    var rost = M("#B8752E", 0.5f);
                    ArtLibrary.Cylinder(root, "Ring", p + new Vector3(-0.22f, 0f, 0f), 0.13f, 0.05f, rost, false).transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    ArtLibrary.Box(root, "Schaft", p + new Vector3(0.08f, 0f, 0f), new Vector3(0.42f, 0.06f, 0.06f), rost);
                    ArtLibrary.Box(root, "Bart", p + new Vector3(0.24f, -0.06f, 0f), new Vector3(0.08f, 0.12f, 0.06f), rost);
                    break;
                case "wolle":
                    var weiss = M("#F7F4EE", 0.05f);
                    foreach (var (dx, dy, r) in new[] { (-0.12f, 0f, 0.16f), (0.12f, 0f, 0.15f), (0f, 0.12f, 0.17f) })
                        ArtLibrary.Sphere(root, "Wolle", p + new Vector3(dx, dy, 0f), r, weiss, false);
                    break;
            }
        }

        /// <summary>The spot within reach of the rider (null = none).</summary>
        public RanchSammeln.Stelle Aufsammelbar()
        {
            foreach (var kv in _sammel)
            {
                var s = kv.Value.stelle;
                if (Math.Sqrt((s.X - _x) * (s.X - _x) + (s.Z - _z) * (s.Z - _z)) <= RanchSammeln.Radius) return s;
            }
            return null;
        }

        /// <summary>Picked up: a sparkle where it was, then gone.</summary>
        public void Weg(string id)
        {
            if (!_sammel.TryGetValue(id, out var e)) return;
            if (e.t != null)
            {
                Effects.Sparkle(e.t.position, 0.8f, 26);
                UnityEngine.Object.Destroy(e.t.gameObject);
            }
            _sammel.Remove(id);
        }

        public int SammelZahl => _sammel.Count;

        private void TickSammeln()
        {
            var dreh = Quaternion.Euler(0f, _t * 70f, 0f);
            foreach (var kv in _sammel)
            {
                var t = kv.Value.t;
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                t.rotation = dreh;
                var p = t.position;
                p.y = _sammelY[kv.Key] + Mathf.Sin(_t * 2.4f + kv.Key.Length) * 0.12f;
                t.position = p;
            }
        }
    }
}
