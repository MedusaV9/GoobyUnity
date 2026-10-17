using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The region's ground (the Unity side of ranch_terrain.gd): a worker thread samples the height model on a 6 m grid and
    /// paints the whole region into one colour map (≈2 m a pixel) — zone moods, paths in sand, the trails to the
    /// discoveries in earth, wet sand at the shores, the brook's pebble bed, rock on steep slopes and snow on the peaks,
    /// lavender rows, the golden corn field, the arena's sand; the main thread then uploads the map and builds the terrain
    /// in 192 m chunks, nearest first, a few per frame. Water: the lake, the bay, the mountain lake, the moor ponds and the
    /// brook as a ribbon on its water line.
    /// </summary>
    public sealed partial class RanchWelt3D
    {
        private const float Raster = 6f;
        private const int ChunkQuads = 32, TexW = 1024;

        private sealed class GelaendeDaten
        {
            public int Nx, Nz, TexH;
            public double MinX, MinZ, W, D;
            public float[] H;
            public Color32[] Pixel;
        }

        private Task<GelaendeDaten> _bau;
        private GelaendeDaten _daten;
        private Material _bodenMat;
        private readonly List<(int cx, int cz, float dist)> _chunkPlan = new List<(int, int, float)>();
        private int _chunkNext, _chunkTotal;
        private bool _chunksFertig;
        private float _bauStart;

        private void StarteGelaende()
        {
            var g = RanchKarte.Grenzen;
            var wege = WegSegmente();
            _bauStart = Time.realtimeSinceStartup;
            _bau = Task.Run(() => Rechne(g, wege));
        }

        /// <summary>Paths (sand) and trails (earth) as segments with their half widths, for the painter.</summary>
        private static List<(V2d a, V2d b, float halb, bool trampel)> WegSegmente()
        {
            var out_ = new List<(V2d, V2d, float, bool)>();
            foreach (var w in RanchKarte.Wege)
                for (var i = 0; i + 1 < w.Punkte.Count; i++) out_.Add((w.Punkte[i], w.Punkte[i + 1], (float)(w.Breite * 0.5), false));
            foreach (var p in RanchEntdeckungen.AllePfade)
                for (var i = 0; i + 1 < p.Punkte.Length; i++) out_.Add((p.Punkte[i], p.Punkte[i + 1], (float)(p.Breite * 0.5), true));
            return out_;
        }

        private static GelaendeDaten Rechne(RanchKarte.Rect g, List<(V2d a, V2d b, float halb, bool trampel)> wege)
        {
            var d = new GelaendeDaten { MinX = g.X, MinZ = g.Z, W = g.W, D = g.D };
            d.Nx = (int)Math.Ceiling(g.W / Raster) + 1;
            d.Nz = (int)Math.Ceiling(g.D / Raster) + 1;
            d.H = new float[d.Nx * d.Nz];
            for (var j = 0; j < d.Nz; j++)
                for (var i = 0; i < d.Nx; i++)
                    d.H[j * d.Nx + i] = (float)RanchGelaende.Hoehe(g.X + i * Raster, g.Z + j * Raster);
            d.TexH = (int)Math.Round(TexW * g.D / g.W);
            d.Pixel = new Color32[TexW * d.TexH];
            var maler = new Maler(d, wege);
            for (var py = 0; py < d.TexH; py++)
            {
                // texture row 0 is the bottom: the region's south edge (max z)
                var z = g.Z + g.D * (1.0 - (py + 0.5) / d.TexH);
                for (var px = 0; px < TexW; px++)
                {
                    var x = g.X + g.W * (px + 0.5) / TexW;
                    d.Pixel[py * TexW + px] = maler.Farbe(x, z);
                }
            }
            return d;
        }

        /// <summary>Bilinear height from the grid (the painter and the props stand on the same ground as the mesh).</summary>
        private static float GridHoehe(GelaendeDaten d, double x, double z)
        {
            var fx = (float)((x - d.MinX) / Raster);
            var fz = (float)((z - d.MinZ) / Raster);
            var i = Mathf.Clamp((int)fx, 0, d.Nx - 2);
            var j = Mathf.Clamp((int)fz, 0, d.Nz - 2);
            var tx = Mathf.Clamp01(fx - i);
            var tz = Mathf.Clamp01(fz - j);
            var a = Mathf.Lerp(d.H[j * d.Nx + i], d.H[j * d.Nx + i + 1], tx);
            var b = Mathf.Lerp(d.H[(j + 1) * d.Nx + i], d.H[(j + 1) * d.Nx + i + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>The region's colours (pastel like ranch_entdecker_karte.gd's moods, a shade deeper for the ground).</summary>
        private sealed class Maler
        {
            private readonly GelaendeDaten _d;
            private readonly List<(V2d a, V2d b, float halb, bool trampel)>[] _zellen;
            private readonly int _zw, _zh;
            private const float Zelle = 64f;
            private readonly List<(RanchKarte.Rect rect, Color farbe, string id)> _zonen = new List<(RanchKarte.Rect, Color, string)>();
            private readonly RanchKarte.Rect _lavendel, _korn, _arena;
            private readonly V2d _bucht;
            private readonly double _buchtR;

            private static readonly Color Wiese = Hex(0x8EC679), Sand = Hex(0xE6CFA0), Erde = Hex(0xC4A177), Nass = Hex(0xD5C39A);
            private static readonly Color Kies = Hex(0xB7AE98), Fels = Hex(0x9C9A92), Schnee = Hex(0xF3F6F8), Grund = Hex(0x6E9E9A);

            public Maler(GelaendeDaten d, List<(V2d a, V2d b, float halb, bool trampel)> wege)
            {
                _d = d;
                _zw = (int)Math.Ceiling(d.W / Zelle) + 1;
                _zh = (int)Math.Ceiling(d.D / Zelle) + 1;
                _zellen = new List<(V2d, V2d, float, bool)>[_zw * _zh];
                foreach (var s in wege)
                {
                    var r = s.halb + 3f;
                    var x0 = Cell(Math.Min(s.a.X, s.b.X) - r, d.MinX, _zw);
                    var x1 = Cell(Math.Max(s.a.X, s.b.X) + r, d.MinX, _zw);
                    var z0 = Cell(Math.Min(s.a.Z, s.b.Z) - r, d.MinZ, _zh);
                    var z1 = Cell(Math.Max(s.a.Z, s.b.Z) + r, d.MinZ, _zh);
                    for (var cz = z0; cz <= z1; cz++)
                        for (var cx = x0; cx <= x1; cx++)
                            (_zellen[cz * _zw + cx] ?? (_zellen[cz * _zw + cx] = new List<(V2d, V2d, float, bool)>())).Add(s);
                }
                foreach (var z in RanchKarte.Zonen)
                    _zonen.Add((z.Rect, Boden(z.Stimmung), z.Id));
                var blume = RanchKarte.ZoneVon("blumenwiese");
                _lavendel = RechtVon(blume?.Daten?["lavendel_rect"]);
                _korn = RechtVon(RanchKarte.ZoneVon("kornfeld")?.Daten?["feld_rect"]);
                var arena = RanchKarte.ZoneVon("turnierplatz")?.Daten?["arena"] as Newtonsoft.Json.Linq.JArray;
                _arena = arena != null && arena.Count >= 4
                    ? new RanchKarte.Rect((double)arena[0] - (double)arena[2] * 0.5, (double)arena[1] - (double)arena[3] * 0.5, (double)arena[2], (double)arena[3])
                    : new RanchKarte.Rect(0, 0, 0, 0);
                var strand = RanchKarte.ZoneVon("strand");
                _bucht = strand?.Punkt("bucht_mitte") ?? new V2d(0, 0);
                _buchtR = strand?.Zahl("bucht_radius", 80.0) ?? 80.0;
            }

            private static RanchKarte.Rect RechtVon(Newtonsoft.Json.Linq.JToken t) =>
                t is Newtonsoft.Json.Linq.JArray a && a.Count >= 4 ? new RanchKarte.Rect((double)a[0], (double)a[1], (double)a[2], (double)a[3]) : new RanchKarte.Rect(0, 0, 0, 0);

            private static int Cell(double v, double min, int n) => Mathf.Clamp((int)((v - min) / Zelle), 0, n - 1);

            /// <summary>Ground colour of a mood: the map's pastels made grassier.</summary>
            private static Color Boden(string stimmung)
            {
                switch (stimmung)
                {
                    case "heimat": return Hex(0x92C779);
                    case "weite": return Hex(0x98CC7B);
                    case "schatten": return Hex(0x72AA5F);
                    case "ruhe": return Hex(0x8BC882);
                    case "panorama": return Hex(0x9AC274);
                    case "frisch": return Hex(0x84C37D);
                    case "geheimnis": return Hex(0x93AF75);
                    case "fest": return Hex(0x96CB7C);
                    case "dorf": return Hex(0x98C57A);
                    case "gipfel": return Hex(0x8DAE75);
                    case "duft": return Hex(0x97BC7C);
                    case "nebel": return Hex(0x7F9C6E);
                    case "sage": return Hex(0x94AF75);
                    case "ferien": return Hex(0xC3C888);
                    case "ernte": return Hex(0x91C36E);
                    case "gold": return Hex(0xABC56B);
                    default: return Wiese;
                }
            }

            public Color32 Farbe(double x, double z)
            {
                var h = GridHoehe(_d, x, z);
                var c = Wiese;
                foreach (var (rect, farbe, _) in _zonen)
                {
                    var innen = Math.Min(Math.Min(x - rect.X, rect.EndX - x), Math.Min(z - rect.Z, rect.EndZ - z));
                    if (innen <= -40.0) continue;
                    var w = Mathf.Clamp01((float)((innen + 40.0) / 80.0));
                    c = Color.Lerp(c, farbe, w * w * (3f - 2f * w));
                }
                // fields and the arena
                if (_lavendel.Enthaelt(x, z)) c = Math.Sin(x * 0.9) > 0.15 ? Hex(0xA98BD0) : Hex(0x8DB27C);
                else if (_korn.Enthaelt(x, z)) c = Math.Sin(z * 0.8 + Math.Sin(x * 0.05) * 2.0) > -0.3 ? Hex(0xE8C860) : Hex(0xD6B04C);
                else if (_arena.W > 0 && _arena.Enthaelt(x, z)) c = Hex(0xE7D2A4);
                // the beach around the bay
                var bd = _bucht.Abstand(x, z);
                if (bd < _buchtR * 1.45) c = Color.Lerp(c, Sand, Mathf.Clamp01((float)((_buchtR * 1.45 - bd) / 18.0)));
                // patches at three scales and a dry yellow cast here and there, so the meadows are not flat paint
                var n1 = Math.Sin(x * 0.045 + Math.Sin(z * 0.031) * 2.2) * Math.Cos(z * 0.052 - Math.Sin(x * 0.027) * 1.7);
                var n2 = Math.Sin(x * 0.17 + z * 0.13 + Math.Sin(z * 0.09) * 1.5) * Math.Cos(z * 0.21 - x * 0.07);
                var n3 = Math.Sin(x * 0.61 + 1.7) * Math.Sin(z * 0.53 - 0.4);
                var hell = (float)(n1 * 0.07 + n2 * 0.05 + n3 * 0.03);
                var gelb = (float)Math.Max(0.0, n2 * 0.6 + n1 * 0.4) * 0.07f;
                c = new Color(c.r * (1f + hell) + gelb, c.g * (1f + hell * 0.85f) + gelb * 0.55f, c.b * (1f + hell * 0.6f) - gelb * 0.35f);
                // rock on steep slopes and high up, snow on the peaks
                var sx = GridHoehe(_d, x + Raster, z) - GridHoehe(_d, x - Raster, z);
                var sz = GridHoehe(_d, x, z + Raster) - GridHoehe(_d, x, z - Raster);
                var steil = Mathf.Sqrt(sx * sx + sz * sz) / (2f * Raster);
                var fels = Mathf.Clamp01((steil - 0.45f) / 0.25f);
                if (h > 55f) fels = Mathf.Max(fels, Mathf.Clamp01((h - 55f) / 25f) * 0.75f);
                if (fels > 0f) c = Color.Lerp(c, Fels, fels);
                if (h > 88f) c = Color.Lerp(c, Schnee, Mathf.Clamp01((h - 88f) / 8f));
                // shores, the brook's bed, under water
                if (h < -0.4f) c = Color.Lerp(c, Nass, Mathf.Clamp01((-0.4f - h) / 0.6f));
                if (h < (float)RanchGelaende.WasserHoehe - 0.2f) c = Color.Lerp(Nass, Grund, Mathf.Clamp01((float)RanchGelaende.WasserHoehe - 0.2f - h));
                var kerbe = (float)RanchGelaende.BachKerbe(x, z);
                if (kerbe > 0.15f) c = Color.Lerp(c, Kies, Mathf.Clamp01((kerbe - 0.15f) / 0.5f));
                // paths and trails on top
                var zelle = _zellen[Cell(z, _d.MinZ, _zh) * _zw + Cell(x, _d.MinX, _zw)];
                if (zelle != null)
                    foreach (var s in zelle)
                    {
                        var dd = (float)RanchGelaende.AbstandZuStrecke(x, z, s.a, s.b);
                        var rand = s.trampel ? 0.8f : 1.6f;
                        if (dd >= s.halb + rand) continue;
                        var w = Mathf.Clamp01((s.halb + rand - dd) / rand);
                        c = Color.Lerp(c, s.trampel ? Erde : Sand, w * (s.trampel ? 0.85f : 1f));
                    }
                return (Color32)c;
            }
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>Main thread: take the worker's data in, then a few chunks a frame, nearest to the rider first.</summary>
        private void BaueWeiter()
        {
            if (_chunksFertig) return;
            if (_daten == null)
            {
                if (_bau == null || !_bau.IsCompleted) return;
                if (_bau.IsFaulted)
                {
                    Debug.LogError("[ranchwelt] terrain: " + _bau.Exception?.GetBaseException());
                    _chunksFertig = true;
                    return;
                }
                _daten = _bau.Result;
                var tex = new Texture2D(TexW, _daten.TexH, TextureFormat.RGBA32, true) { name = "RanchWeltBoden", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                tex.SetPixels32(_daten.Pixel);
                tex.Apply(true, true);
                _daten.Pixel = null;
                _owned.Add(tex);
                _bodenMat = new Material(ArtLibrary.Solid(Color.white, 0.04f)) { name = "RanchWeltBoden", mainTexture = tex };
                if (_bodenMat.HasProperty("_BaseMap")) _bodenMat.SetTexture("_BaseMap", tex);
                _owned.Add(_bodenMat);
                var ncx = (_daten.Nx - 2) / ChunkQuads + 1;
                var ncz = (_daten.Nz - 2) / ChunkQuads + 1;
                for (var cz = 0; cz < ncz; cz++)
                    for (var cx = 0; cx < ncx; cx++)
                    {
                        var mx = _daten.MinX + (cx + 0.5) * ChunkQuads * Raster;
                        var mz = _daten.MinZ + (cz + 0.5) * ChunkQuads * Raster;
                        _chunkPlan.Add((cx, cz, (float)Math.Sqrt((mx - _x) * (mx - _x) + (mz - _z) * (mz - _z))));
                    }
                _chunkPlan.Sort((a, b) => a.dist.CompareTo(b.dist));
                _chunkTotal = _chunkPlan.Count;
                BaueWasser();
                Debug.Log("[ranchwelt] terrain data in " + (Time.realtimeSinceStartup - _bauStart).ToString("0.00") + " s, " + _chunkTotal + " chunks");
                return;
            }
            var budget = Time.realtimeSinceStartup + 0.008f;
            while (_chunkNext < _chunkTotal && (Time.realtimeSinceStartup < budget || _chunkNext < 4))
            {
                var (cx, cz, _) = _chunkPlan[_chunkNext++];
                BaueChunk(cx, cz);
            }
            if (_chunkNext >= _chunkTotal)
            {
                _chunksFertig = true;
                Debug.Log("[ranchwelt] terrain built in " + (Time.realtimeSinceStartup - _bauStart).ToString("0.00") + " s");
            }
        }

        private void BaueChunk(int cx, int cz)
        {
            var d = _daten;
            var i0 = cx * ChunkQuads;
            var j0 = cz * ChunkQuads;
            var ni = Mathf.Min(ChunkQuads, d.Nx - 1 - i0);
            var nj = Mathf.Min(ChunkQuads, d.Nz - 1 - j0);
            if (ni <= 0 || nj <= 0) return;
            var verts = new Vector3[(ni + 1) * (nj + 1)];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            for (var j = 0; j <= nj; j++)
                for (var i = 0; i <= ni; i++)
                {
                    var gi = i0 + i;
                    var gj = j0 + j;
                    var x = d.MinX + gi * Raster;
                    var z = d.MinZ + gj * Raster;
                    var k = j * (ni + 1) + i;
                    verts[k] = L(x, d.H[gj * d.Nx + gi], z);
                    var hl = d.H[gj * d.Nx + Mathf.Max(0, gi - 1)];
                    var hr = d.H[gj * d.Nx + Mathf.Min(d.Nx - 1, gi + 1)];
                    var hn = d.H[Mathf.Max(0, gj - 1) * d.Nx + gi];
                    var hs = d.H[Mathf.Min(d.Nz - 1, gj + 1) * d.Nx + gi];
                    // local z is −region z: the slope toward south points to local −z
                    normals[k] = new Vector3(hl - hr, 2f * Raster, hs - hn).normalized;
                    uvs[k] = new Vector2((float)((x - d.MinX) / d.W), (float)(1.0 - (z - d.MinZ) / d.D));
                }
            var tris = new int[ni * nj * 6];
            var t = 0;
            for (var j = 0; j < nj; j++)
                for (var i = 0; i < ni; i++)
                {
                    var a = j * (ni + 1) + i;
                    var b = a + 1;
                    var c = a + ni + 1;
                    var e = c + 1;
                    // seen from above with local z = −region z: (a, b, c) and (b, e, c) wind clockwise
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = e; tris[t++] = c;
                }
            var mesh = new Mesh { name = "RanchBoden" + cx + "_" + cz };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            var go = new GameObject("Boden" + cx + "_" + cz);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _bodenMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        // ── water ──

        private void BaueWasser()
        {
            var wasser = new Material(ArtLibrary.Solid(new Color(0.49f, 0.78f, 0.89f), 0.9f)) { name = "RanchWasser" };
            var moorWasser = new Material(ArtLibrary.Solid(new Color(0.42f, 0.55f, 0.5f), 0.85f)) { name = "RanchMoorWasser" };
            _owned.Add(wasser);
            _owned.Add(moorWasser);
            var see = RanchKarte.ZoneVon("see");
            if (see != null) Scheibe("See", see.Punkt("see_mitte"), see.Zahl("see_radius") * 1.75, RanchGelaende.WasserHoehe, wasser);
            var strand = RanchKarte.ZoneVon("strand");
            if (strand != null) Scheibe("Bucht", strand.Punkt("bucht_mitte"), strand.Zahl("bucht_radius") * 1.6, RanchGelaende.WasserHoehe, wasser);
            var berg = RanchKarte.ZoneVon("bergmassiv");
            if (berg != null) Scheibe("Bergsee", berg.Punkt("bergsee_mitte"), berg.Zahl("bergsee_radius") * 1.5, RanchGelaende.BergseeWasser, wasser);
            var moor = RanchKarte.ZoneVon("moor");
            if (moor != null)
                foreach (var p in RanchKarte.Punkte(moor.Daten?["tuempel"])) Scheibe("Tuempel", p, 13.0, -0.66, moorWasser);
            Bachband(wasser);
        }

        private void Scheibe(string name, V2d mitte, double radius, double y, Material mat)
        {
            const int n = 48;
            var verts = new Vector3[n + 1];
            var tris = new int[n * 3];
            verts[0] = L(mitte.X, y, mitte.Z);
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2f / n;
                verts[i + 1] = L(mitte.X + Math.Cos(a) * radius, y, mitte.Z + Math.Sin(a) * radius);
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + (i + 1) % n;
                tris[i * 3 + 2] = 1 + i;
            }
            var mesh = new Mesh { name = name, vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
            {
                for (var i = 0; i < n; i++) { var k = tris[i * 3 + 1]; tris[i * 3 + 1] = tris[i * 3 + 2]; tris[i * 3 + 2] = k; }
                mesh.triangles = tris;
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            var go = new GameObject("Wasser:" + name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>The brook as a ribbon on its water line, every 4 m along the polyline.</summary>
        private void Bachband(Material mat)
        {
            var pts = RanchKarte.BachPunkte;
            var breite = global::Gooby.Core.Json.J.NumOr(RanchKarte.Bach["breite"], 7.0);
            var links = new List<Vector3>();
            var rechts = new List<Vector3>();
            for (var s = 0; s + 1 < pts.Count; s++)
            {
                var a = pts[s];
                var b = pts[s + 1];
                var len = a.Abstand(b);
                var steps = Math.Max(1, (int)(len / 4.0));
                var dx = (b.X - a.X) / len;
                var dz = (b.Z - a.Z) / len;
                for (var k = 0; k < steps || (s + 2 == pts.Count && k == steps); k++)
                {
                    var t = (double)k / steps;
                    var x = a.X + (b.X - a.X) * t;
                    var z = a.Z + (b.Z - a.Z) * t;
                    var y = RanchGelaende.BachWasserspiegel(x, z) + 0.02;
                    var half = breite * 0.42;
                    links.Add(L(x - dz * half, y, z + dx * half));
                    rechts.Add(L(x + dz * half, y, z - dx * half));
                }
            }
            var verts = new Vector3[links.Count * 2];
            for (var i = 0; i < links.Count; i++) { verts[i * 2] = links[i]; verts[i * 2 + 1] = rechts[i]; }
            var tris = new List<int>();
            for (var i = 0; i + 1 < links.Count; i++)
            {
                var a = i * 2;
                tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            var mesh = new Mesh { name = "Bach", vertices = verts, triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            var flip = false;
            foreach (var nrm in mesh.normals) { flip = nrm.y < 0f; break; }
            if (flip)
            {
                var arr = tris.ToArray();
                for (var i = 0; i < arr.Length; i += 3) { var k = arr[i + 1]; arr[i + 1] = arr[i + 2]; arr[i + 2] = k; }
                mesh.triangles = arr;
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            var go = new GameObject("Wasser:Bach");
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
