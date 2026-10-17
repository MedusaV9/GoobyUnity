using System.Collections.Generic;
using Gooby.Core.Home;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// A garland under the ceiling (port of girlande.gd): the cord hangs as a catenary between two ceiling points and
    /// carries pastel pennants, warm fairy lights (two real lights after dark, the bulbs glow) or pastel pompoms, evenly
    /// spaced along the cord.
    /// </summary>
    public static class GarlandView
    {
        private const int Segments = 12;
        private const int MaxLights = 2;
        private static readonly Color CordColour = new Color(0.55f, 0.45f, 0.38f);
        private static readonly string[] PennantColours = { "#FF98C1", "#FFD166", "#98E7E1", "#7FB8FF", "#FFC9A6" };
        private static readonly string[] PompomColours = { "#FFB3C7", "#FFE29A", "#A8E6CF", "#C7A6F2", "#FFC9A6" };
        private static Mesh _pennant;

        public static GameObject Build(Transform parent, string typ, Vector3 a, Vector3 b, bool night)
        {
            var root = new GameObject("Girlande:" + typ);
            root.transform.SetParent(parent, false);
            var t = root.transform;
            var points = new List<Vector3>();
            foreach (var p in Catenary.Points((a.x, a.y, a.z), (b.x, b.y, b.z), Segments)) points.Add(new Vector3((float)p.x, (float)p.y, (float)p.z));
            var cord = ArtLibrary.Solid(CordColour, 0.1f);
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var seg = ArtLibrary.Cylinder(t, "Cord", (points[i] + points[i + 1]) * 0.5f, 0.006f, Vector3.Distance(points[i], points[i + 1]), cord, false);
                seg.transform.rotation = Quaternion.FromToRotation(Vector3.up, points[i + 1] - points[i]);
            }
            foreach (var end in new[] { a, b })
            {
                ArtLibrary.Cylinder(t, "Rose", end + Vector3.up * 0.03f, 0.04f, 0.015f, cord, false);
                ArtLibrary.Sphere(t, "Hook", end, 0.022f, cord, false);
            }
            var spacing = typ == Girlanden.Lichter ? 0.24f : typ == Girlanden.Pompons ? 0.2f : 0.22f;
            var lights = 0;
            var stops = Stops(points, spacing);
            for (var i = 0; i < stops.Count; i++)
            {
                var (p, tangent) = stops[i];
                switch (typ)
                {
                    case Girlanden.Lichter:
                    {
                        var bulb = night ? ArtLibrary.Glow(new Color(2.4f, 2.0f, 1.1f)) : ArtLibrary.Solid(ArtLibrary.Hex("#FFE8A8"), 0.6f);
                        ArtLibrary.Sphere(t, "Bulb", p + Vector3.down * 0.045f, 0.032f, bulb, false);
                        // night: a couple of real lights along the chain (at its thirds), the rest is glow
                        if (night && lights < MaxLights && i == Mathf.RoundToInt((lights + 1) * (stops.Count - 1) / (float)(MaxLights + 1)))
                        {
                            var l = new GameObject("GarlandLight").AddComponent<Light>();
                            l.transform.SetParent(t, false);
                            l.transform.position = p + Vector3.down * 0.1f;
                            l.type = LightType.Point;
                            l.color = ArtLibrary.Hex("FFD9A0");
                            l.intensity = 0.9f;
                            l.range = 2.6f;
                            l.shadows = LightShadows.None;
                            lights++;
                        }
                        break;
                    }
                    case Girlanden.Pompons:
                        ArtLibrary.Sphere(t, "Pompom", p + Vector3.down * 0.055f, i % 2 == 0 ? 0.052f : 0.044f, ArtLibrary.Solid(ArtLibrary.Hex(PompomColours[i % PompomColours.Length]), 0.05f), false);
                        break;
                    default:
                    {
                        var flag = new GameObject("Pennant");
                        flag.transform.SetParent(t, false);
                        flag.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.Cross(tangent, Vector3.up), Vector3.up));
                        flag.AddComponent<MeshFilter>().sharedMesh = PennantMesh();
                        var r = flag.AddComponent<MeshRenderer>();
                        r.sharedMaterial = ArtLibrary.Solid(ArtLibrary.Hex(PennantColours[i % PennantColours.Length]), 0.08f);
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        break;
                    }
                }
            }
            return root;
        }

        /// <summary>Points every <paramref name="spacing"/> metres along the cord (half a step in from each end) with the cord's direction there.</summary>
        private static List<(Vector3 point, Vector3 tangent)> Stops(List<Vector3> points, float spacing)
        {
            var stops = new List<(Vector3, Vector3)>();
            var total = 0f;
            for (var i = 0; i + 1 < points.Count; i++) total += Vector3.Distance(points[i], points[i + 1]);
            var n = Mathf.Max(1, Mathf.FloorToInt(total / spacing));
            var step = total / n;
            var seg = 0;
            var segStart = 0f;
            for (var k = 0; k < n; k++)
            {
                var s = step * (k + 0.5f);
                while (seg < points.Count - 2 && segStart + Vector3.Distance(points[seg], points[seg + 1]) < s)
                {
                    segStart += Vector3.Distance(points[seg], points[seg + 1]);
                    seg++;
                }
                var len = Mathf.Max(1e-5f, Vector3.Distance(points[seg], points[seg + 1]));
                var u = Mathf.Clamp01((s - segStart) / len);
                var dir = points[seg + 1] - points[seg];
                var flat = new Vector3(dir.x, 0f, dir.z);
                stops.Add((Vector3.Lerp(points[seg], points[seg + 1], u), flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.right));
            }
            return stops;
        }

        /// <summary>A pennant: a triangle hanging from its top edge, double-sided (x along the cord, y up, point down).</summary>
        private static Mesh PennantMesh()
        {
            if (_pennant != null) return _pennant;
            const float w = 0.16f, h = 0.2f;
            var v = new[]
            {
                new Vector3(-w * 0.5f, 0f, 0f), new Vector3(w * 0.5f, 0f, 0f), new Vector3(0f, -h, 0f),
                new Vector3(-w * 0.5f, 0f, 0f), new Vector3(w * 0.5f, 0f, 0f), new Vector3(0f, -h, 0f),
            };
            _pennant = new Mesh { name = "Pennant" };
            _pennant.vertices = v;
            _pennant.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.forward, Vector3.forward, Vector3.forward };
            _pennant.triangles = new[] { 0, 1, 2, 3, 5, 4 };
            _pennant.RecalculateBounds();
            return _pennant;
        }
    }
}
