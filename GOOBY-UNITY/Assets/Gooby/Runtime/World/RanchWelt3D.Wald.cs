using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The region's mass of green (the Unity cut of ranch_streu.gd): low-poly trees — round oaks, double-crowned limes,
    /// stacked pines, slim poplars, bare moor trees — built as flat-shaded facets and merged per zone into a handful of
    /// meshes, so the little forest really is a forest; and around the rider a ring of grass tufts and flowers that is
    /// rebuilt as he rides (deterministic per 6 m cell), so the meadow is never bare paint under the hooves.
    /// </summary>
    public sealed partial class RanchWelt3D
    {
        public enum BaumArt { Eiche, Linde, Kiefer, Pappel, Kahl }

        private static readonly Color[] Kronen = { Hex(0x5E9F4E), Hex(0x6FAE5B), Hex(0x7DBE63), Hex(0x86C46A) };
        private static readonly Color[] Nadeln = { Hex(0x3F7A4F), Hex(0x4E8A55), Hex(0x5A9660) };
        private static readonly Color Rinde = Hex(0x8C613D), RindeKahl = Hex(0x7D6A55);

        private readonly Dictionary<string, List<(Vector3 p, float s, float yaw, BaumArt art, int farbe)>> _baeume =
            new Dictionary<string, List<(Vector3, float, float, BaumArt, int)>>();

        /// <summary>A tree to be merged into its zone's forest mesh (region x, z; scale ≈ height / 7 m).</summary>
        private void Baum(string zone, double x, double z, float scale, BaumArt art, System.Random rng)
        {
            if (!_baeume.TryGetValue(zone, out var list)) _baeume[zone] = list = new List<(Vector3, float, float, BaumArt, int)>();
            list.Add((L(x, RanchGelaende.Hoehe(x, z) - 0.1, z), scale, (float)rng.NextDouble() * 360f, art, rng.Next(8)));
            Stamm(x, z, (art == BaumArt.Kiefer ? 0.22f : 0.32f) * scale);
        }

        // ── trunks: the rider goes round them, the camera climbs over the crowns ──

        private const float StammZelle = 16f;
        private readonly Dictionary<long, List<(double x, double z, float r)>> _staemme = new Dictionary<long, List<(double, double, float)>>();

        private static long StammKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        /// <summary>Registers a trunk (also of a placed tree model) for the rider's collision.</summary>
        private void Stamm(double x, double z, float r)
        {
            var key = StammKey(Mathf.FloorToInt((float)(x / StammZelle)), Mathf.FloorToInt((float)(z / StammZelle)));
            if (!_staemme.TryGetValue(key, out var list)) _staemme[key] = list = new List<(double, double, float)>();
            list.Add((x, z, r));
        }

        /// <summary>The nearest trunk's gap at (x, z): distance minus its radius (∞ far from all).</summary>
        private double StammAbstand(double x, double z, double reichweite)
        {
            var best = double.PositiveInfinity;
            var c0 = Mathf.FloorToInt((float)((x - reichweite) / StammZelle));
            var c1 = Mathf.FloorToInt((float)((x + reichweite) / StammZelle));
            var d0 = Mathf.FloorToInt((float)((z - reichweite) / StammZelle));
            var d1 = Mathf.FloorToInt((float)((z + reichweite) / StammZelle));
            for (var cz = d0; cz <= d1; cz++)
                for (var cx = c0; cx <= c1; cx++)
                {
                    if (!_staemme.TryGetValue(StammKey(cx, cz), out var list)) continue;
                    foreach (var (sx, sz, r) in list)
                        best = Math.Min(best, Math.Sqrt((sx - x) * (sx - x) + (sz - z) * (sz - z)) - r);
                }
            return best;
        }

        /// <summary>Many trees at free spots of a rect (the forest's mass), keeping clear of <paramref name="extra"/>.</summary>
        private void Wald(string zone, RanchKarte.Rect rect, int n, System.Random rng, Func<System.Random, BaumArt> art, float sMin, float sMax, Func<double, double, bool> extra = null, double rand = 2.5)
        {
            // crowns reach 3 m out: keep the trunks clear of the paths, or the chase camera rides through the leaves
            rand = Math.Max(rand, 6.5);
            var gesetzt = 0;
            for (var v = 0; v < n * 4 && gesetzt < n; v++)
            {
                var x = rect.X + rng.NextDouble() * rect.W;
                var z = rect.Z + rng.NextDouble() * rect.D;
                if (!Frei(x, z, rand) || (extra != null && !extra(x, z))) continue;
                Baum(zone, x, z, sMin + (float)rng.NextDouble() * (sMax - sMin), art(rng), rng);
                gesetzt++;
            }
        }

        private sealed class Bauer
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<int> T = new List<int>();

            public void Dreieck(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                var i = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                N.Add(n); N.Add(n); N.Add(n);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
            }

            /// <summary>A closed prism/cone ring: base radius r0 at y0, top radius r1 at y1 (r1 = 0 → a cone).</summary>
            public void Ring(Vector3 c, float r0, float y0, float r1, float y1, int seiten, float drehung)
            {
                for (var i = 0; i < seiten; i++)
                {
                    var a0 = drehung + i * Mathf.PI * 2f / seiten;
                    var a1 = drehung + (i + 1) * Mathf.PI * 2f / seiten;
                    var p0 = c + new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0);
                    var p1 = c + new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
                    var q0 = c + new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1);
                    var q1 = c + new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);
                    if (r1 > 0.001f)
                    {
                        Dreieck(p0, q0, q1);
                        Dreieck(p0, q1, p1);
                    }
                    else Dreieck(p0, q0, p1);
                }
            }

            private static readonly Vector3[] Ico = Icosaeder();
            private static readonly int[] IcoF =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            private static Vector3[] Icosaeder()
            {
                var t = (1f + Mathf.Sqrt(5f)) / 2f;
                var v = new[]
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                };
                for (var i = 0; i < v.Length; i++) v[i] = v[i].normalized;
                return v;
            }

            /// <summary>A faceted blob (an icosahedron scaled per axis, turned by <paramref name="rot"/>).</summary>
            public void Blob(Vector3 c, Vector3 r, Quaternion rot)
            {
                for (var f = 0; f < IcoF.Length; f += 3)
                {
                    var a = c + rot * Vector3.Scale(Ico[IcoF[f]], r);
                    var b = c + rot * Vector3.Scale(Ico[IcoF[f + 1]], r);
                    var d = c + rot * Vector3.Scale(Ico[IcoF[f + 2]], r);
                    Dreieck(a, b, d);
                }
            }

            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Every zone's trees as one mesh per colour, under the zone's group (they sleep with it).</summary>
        private void BaueWaelder()
        {
            var mats = new Dictionary<Color, Material>();
            Material Mat(Color c)
            {
                if (mats.TryGetValue(c, out var m)) return m;
                m = new Material(ArtLibrary.Solid(c, 0.1f)) { name = "Baum" + ColorUtility.ToHtmlStringRGB(c) };
                _owned.Add(m);
                mats[c] = m;
                return m;
            }
            foreach (var kv in _baeume)
            {
                var bauer = new Dictionary<Color, Bauer>();
                Bauer B(Color c) => bauer.TryGetValue(c, out var b) ? b : bauer[c] = new Bauer();
                foreach (var (p, s, yaw, art, farbe) in kv.Value)
                {
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    var dreh = yaw * Mathf.Deg2Rad;
                    switch (art)
                    {
                        case BaumArt.Eiche:
                            B(Rinde).Ring(p, 0.32f * s, 0f, 0.22f * s, 3.1f * s, 6, dreh);
                            B(Kronen[farbe % 4]).Blob(p + new Vector3(0f, 4.4f * s, 0f), new Vector3(2.5f, 2.1f, 2.5f) * s, rot);
                            break;
                        case BaumArt.Linde:
                            B(Rinde).Ring(p, 0.3f * s, 0f, 0.2f * s, 2.8f * s, 6, dreh);
                            B(Kronen[farbe % 4]).Blob(p + rot * new Vector3(-0.7f * s, 3.9f * s, 0f), new Vector3(1.9f, 1.7f, 1.9f) * s, rot);
                            B(Kronen[(farbe + 1) % 4]).Blob(p + rot * new Vector3(0.9f * s, 4.6f * s, 0.3f * s), new Vector3(1.7f, 1.6f, 1.7f) * s, rot);
                            break;
                        case BaumArt.Kiefer:
                            B(Rinde).Ring(p, 0.22f * s, 0f, 0.14f * s, 1.6f * s, 5, dreh);
                            var nadel = Nadeln[farbe % 3];
                            B(nadel).Ring(p, 1.9f * s, 1.3f * s, 0f, 4.4f * s, 7, dreh);
                            B(nadel).Ring(p, 1.45f * s, 3.2f * s, 0f, 6.0f * s, 7, dreh + 0.3f);
                            B(nadel).Ring(p, 0.95f * s, 4.9f * s, 0f, 7.4f * s, 7, dreh + 0.6f);
                            break;
                        case BaumArt.Pappel:
                            B(Rinde).Ring(p, 0.22f * s, 0f, 0.15f * s, 2.2f * s, 5, dreh);
                            B(Kronen[(farbe % 2) + 2]).Blob(p + new Vector3(0f, 5.2f * s, 0f), new Vector3(1.2f, 3.6f, 1.2f) * s, rot);
                            break;
                        case BaumArt.Kahl:
                            B(RindeKahl).Ring(p, 0.26f * s, 0f, 0.1f * s, 4.2f * s, 5, dreh);
                            for (var a = 0; a < 3; a++)
                            {
                                var ast = rot * Quaternion.Euler(0f, a * 120f, 50f);
                                var start = p + new Vector3(0f, (2.2f + a * 0.6f) * s, 0f);
                                B(RindeKahl).Ring(start, 0.09f * s, 0f, 0.03f * s, 0.01f, 4, 0f);
                                var ende = start + ast * new Vector3(0f, 1.6f * s, 0f);
                                B(RindeKahl).Dreieck(start + new Vector3(0.08f * s, 0f, 0f), ende, start - new Vector3(0.08f * s, 0f, 0f));
                                B(RindeKahl).Dreieck(start - new Vector3(0.08f * s, 0f, 0f), ende, start + new Vector3(0.08f * s, 0f, 0f));
                            }
                            break;
                    }
                }
                var g = Gruppe(kv.Key);
                foreach (var b in bauer)
                {
                    var mesh = b.Value.Mesh("Wald:" + kv.Key);
                    _owned.Add(mesh);
                    var go = new GameObject("Wald:" + kv.Key);
                    go.transform.SetParent(g, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = Mat(b.Key);
                    mr.shadowCastingMode = ShadowCastingMode.On;
                }
            }
            _baeume.Clear();
        }

        // ── grass and flowers round the rider ──

        private const float StreuZelle = 6f;
        private const int StreuRadiusZellen = 9;
        private MeshFilter _streu;
        private Mesh _streuMesh;
        private (int x, int z) _streuMitte = (int.MinValue, 0);
        private static readonly Color[] Halme = { Hex(0x7FBF5E), Hex(0x8FCB66), Hex(0x6FAE55) };
        private static readonly Color[] Blueten = { Hex(0xFFE36E), Hex(0xFFFFFF), Hex(0xF6A3C1), Hex(0xB89BE6) };

        private void StreuTick()
        {
            var cx = Mathf.FloorToInt((float)(_x / (StreuZelle * 2f)));
            var cz = Mathf.FloorToInt((float)(_z / (StreuZelle * 2f)));
            if (cx == _streuMitte.x && cz == _streuMitte.z) return;
            _streuMitte = (cx, cz);
            if (_streu == null)
            {
                var go = new GameObject("Streu");
                go.transform.SetParent(_root, false);
                _streu = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[Halme.Length + Blueten.Length];
                for (var i = 0; i < Halme.Length; i++) mats[i] = new Material(ArtLibrary.Solid(Halme[i], 0.05f));
                for (var i = 0; i < Blueten.Length; i++) mats[Halme.Length + i] = new Material(ArtLibrary.Solid(Blueten[i], 0.2f));
                foreach (var m in mats) _owned.Add(m);
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                _streuMesh = new Mesh { name = "Streu", indexFormat = IndexFormat.UInt32 };
                _owned.Add(_streuMesh);
                _streu.sharedMesh = _streuMesh;
            }
            var halme = new Bauer[Halme.Length];
            var blueten = new Bauer[Blueten.Length];
            for (var i = 0; i < halme.Length; i++) halme[i] = new Bauer();
            for (var i = 0; i < blueten.Length; i++) blueten[i] = new Bauer();
            var mx = Mathf.FloorToInt((float)(_x / StreuZelle));
            var mz = Mathf.FloorToInt((float)(_z / StreuZelle));
            for (var j = -StreuRadiusZellen; j <= StreuRadiusZellen; j++)
                for (var i = -StreuRadiusZellen; i <= StreuRadiusZellen; i++)
                {
                    if (i * i + j * j > StreuRadiusZellen * StreuRadiusZellen) continue;
                    var gx = mx + i;
                    var gz = mz + j;
                    var h = (uint)(gx * 73856093) ^ (uint)(gz * 19349663);
                    h ^= h >> 13;
                    h *= 0x5bd1e995;
                    h ^= h >> 15;
                    var n = (int)(h % 4);
                    for (var k = 0; k < n; k++)
                    {
                        var hk = h * (uint)(k + 7) * 2654435761u;
                        var x = (gx + ((hk >> 4) & 255) / 256f) * StreuZelle;
                        var z = (gz + ((hk >> 12) & 255) / 256f) * StreuZelle;
                        var dWeg = RanchGelaende.WegAbstand(x, z, out var halb);
                        if (dWeg < halb + 0.6) continue;
                        var y = RanchGelaende.Hoehe(x, z);
                        if (y < -0.5 || y > 70.0 || RanchGelaende.BachKerbe(x, z) > 0.2) continue;
                        var p = L(x, y - 0.05, z);
                        var groesse = 0.55f + ((hk >> 20) & 15) / 30f;
                        var dreh = ((hk >> 8) & 63) / 10f;
                        if (((hk >> 24) & 7) == 0)
                        {
                            var stiel = p + new Vector3(0f, 0.32f * groesse, 0f);
                            halme[0].Ring(p, 0.03f, 0f, 0.02f, 0.32f * groesse, 3, dreh);
                            blueten[(hk >> 27) % (uint)Blueten.Length].Blob(stiel, new Vector3(0.14f, 0.09f, 0.14f), Quaternion.Euler(0f, dreh * 57f, 0f));
                        }
                        else halme[(hk >> 26) % (uint)Halme.Length].Ring(p, 0.26f * groesse, 0f, 0f, 0.62f * groesse, 4, dreh);
                    }
                }
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var subs = new List<List<int>>();
            foreach (var b in halme) Anhaengen(b, verts, norms, subs);
            foreach (var b in blueten) Anhaengen(b, verts, norms, subs);
            _streuMesh.Clear();
            _streuMesh.SetVertices(verts);
            _streuMesh.SetNormals(norms);
            _streuMesh.subMeshCount = subs.Count;
            for (var s = 0; s < subs.Count; s++) _streuMesh.SetTriangles(subs[s], s);
            _streuMesh.RecalculateBounds();
        }

        private static void Anhaengen(Bauer b, List<Vector3> verts, List<Vector3> norms, List<List<int>> subs)
        {
            var off = verts.Count;
            verts.AddRange(b.V);
            norms.AddRange(b.N);
            var t = new List<int>(b.T.Count);
            foreach (var i in b.T) t.Add(i + off);
            subs.Add(t);
        }
    }
}
