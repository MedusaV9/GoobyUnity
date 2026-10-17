using System.Collections.Generic;
using Gooby.Core.Cosmetics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Wardrobe pictures. The pieces are primitives built at runtime, so there are no icon files: one hidden stage with
    /// its own camera and lights bakes them one after another into cached textures, a few per frame so opening a tab
    /// never stalls (the Godot wardrobe's CosmeticPreview). Fur has no mesh and gets a painted Gooby head in its three
    /// colours. Players without a GPU bake nothing and the cards keep their colour dots.
    /// </summary>
    public sealed class CosmeticPreview : MonoBehaviour
    {
        public const int Size = 192;
        private const int PerFrame = 3;
        private const int StageLayer = 30;
        /// <summary>
        /// Where the camera looks from, seen from the piece: hats from the front, a little from above and the side;
        /// glasses almost head-on (from the side their frames shrink to lines); neck pieces lower so a scarf reads as a
        /// ring with its tail; back pieces from behind, where Gooby carries them.
        /// </summary>
        private static Vector3 ViewOf(CosmeticDef def)
        {
            switch (def.Kategorie)
            {
                case "brille": return new Vector3(0.2f, 0.1f, 1f);
                case "hals": return new Vector3(0.4f, 0.22f, 1f);
                case "ruecken": return new Vector3(-0.55f, 0.32f, -1f);
                default: return new Vector3(0.5f, 0.32f, 1f);
            }
        }
        private static readonly Vector3 Stage = new Vector3(0f, -400f, 0f);
        private static readonly Color Ambient = new Color(1f, 0.965f, 0.92f) * 0.62f;

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static CosmeticPreview _instance;
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _queued = new HashSet<string>();
        private Camera _cam;
        private Light _key, _fill;
        private RenderTexture _rt;

        /// <summary>The finished picture, or null — then it is queued and turns up a few frames later.</summary>
        public static Texture2D Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Cache.TryGetValue(id, out var tex) && tex != null) return tex;
            var def = CosmeticsCatalog.ById(id);
            if (def == null) return null;
            if (def.Kategorie == CosmeticsCatalog.Fell) return Cache[id] = FurTile(def);
            if (Gooby.Unity.App.PhotoCapture.Headless) return null;
            var baker = Instance;
            if (baker._queued.Add(id)) baker._queue.Enqueue(id);
            return null;
        }

        /// <summary>Number of pictures baked so far (QA).</summary>
        public static int Baked => Cache.Count;

        /// <summary>QA: every finished picture as id.png in <paramref name="dir"/> (the textures themselves are GPU-only).</summary>
        public static int Dump(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var n = 0;
            foreach (var kv in Cache)
            {
                if (kv.Value == null) continue;
                var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
                var active = RenderTexture.active;
                Graphics.Blit(kv.Value, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                copy.Apply();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, kv.Key + ".png"), copy.EncodeToPNG());
                Destroy(copy);
                n++;
            }
            return n;
        }

        private static CosmeticPreview Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("CosmeticPreview");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<CosmeticPreview>();
                _instance.Setup();
                return _instance;
            }
        }

        private void Setup()
        {
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask = 1 << StageLayer;
            _cam.allowHDR = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "outfit-preview" };
            _key = MakeLight("PreviewKey", new Vector3(0.35f, 0.85f, 0.6f), 1.05f, new Color(1f, 0.97f, 0.92f));
            _fill = MakeLight("PreviewFill", new Vector3(-0.8f, 0.2f, -0.25f), 0.45f, new Color(0.9f, 0.94f, 1f));
        }

        /// <param name="from">where the light comes from, seen from the piece</param>
        private Light MakeLight(string name, Vector3 from, float intensity, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.LookRotation(-from.normalized, Vector3.up);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << StageLayer;
            light.enabled = false;
            return light;
        }

        private void Update()
        {
            for (var n = 0; n < PerFrame && _queue.Count > 0; n++)
            {
                var id = _queue.Dequeue();
                _queued.Remove(id);
                if (Cache.ContainsKey(id)) continue;
                var def = CosmeticsCatalog.ById(id);
                if (def == null) continue;
                try
                {
                    var tex = Bake(def);
                    if (tex != null) Cache[id] = tex;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[outfit] preview " + id + " failed: " + e.Message);
                }
            }
        }

        private Texture2D Bake(CosmeticDef def)
        {
            var holder = new GameObject("Preview:" + def.Id);
            holder.transform.position = Stage;
            try
            {
                Outfit.BuildLoose(holder.transform, def);
                // a scarf or collar alone is a flat disc: it gets a body to sit on (headphones a head)
                if (def.Kategorie == "hals")
                {
                    var head = def.Build == "kopfhoerer";
                    ArtLibrary.Sphere(holder.transform, "Body", Stage + new Vector3(0f, head ? 0.3f : -0.15f, 0f), head ? 0.29f : 0.25f,
                        ArtLibrary.Solid(ArtLibrary.Hex("#F8EAD3"), 0.15f), false);
                }
                var renderers = holder.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;
                foreach (var r in renderers)
                {
                    r.gameObject.layer = StageLayer;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                var radius = Mathf.Max(0.05f, b.extents.magnitude);
                var dir = ViewOf(def).normalized;
                var rot = Quaternion.LookRotation(-dir, Vector3.up);
                var right = rot * Vector3.right;
                var up = rot * Vector3.up;
                // frame what the camera actually sees of the piece: the corners of every part projected onto the picture
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var r in renderers)
                {
                    var rb = r.bounds;
                    for (var i = 0; i < 8; i++)
                    {
                        var corner = rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)) - b.center;
                        var q = new Vector2(Vector3.Dot(corner, right), Vector3.Dot(corner, up));
                        min = Vector2.Min(min, q);
                        max = Vector2.Max(max, q);
                    }
                }
                var mid = (min + max) * 0.5f;
                var center = b.center + right * mid.x + up * mid.y;
                _cam.orthographicSize = Mathf.Max(0.03f, Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f * 1.14f);
                _cam.nearClipPlane = 0.01f;
                _cam.farClipPlane = radius * 12f;
                _cam.transform.SetPositionAndRotation(center + dir * radius * 4f, rot);
                return RenderPicture();
            }
            finally
            {
                // Destroy waits for the end of the frame: the next picture of this frame must not see this piece
                holder.SetActive(false);
                Destroy(holder);
            }
        }

        /// <summary>One render of the stage with its own light: the world's lights, ambient and fog sit it out.</summary>
        private Texture2D RenderPicture()
        {
            var off = new List<Light>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.enabled && l != _key && l != _fill) { l.enabled = false; off.Add(l); }
            var probe = RenderSettings.ambientProbe;
            var fog = RenderSettings.fog;
            var sun = RenderSettings.sun;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(Ambient);
            RenderSettings.ambientProbe = sh;
            RenderSettings.fog = false;
            RenderSettings.sun = _key;
            _key.enabled = _fill.enabled = true;
            _cam.enabled = true;
            var active = RenderTexture.active;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = _rt };
                if (RenderPipeline.SupportsRenderRequest(_cam, request)) RenderPipeline.SubmitRenderRequest(_cam, request);
                else
                {
                    _cam.targetTexture = _rt;
                    _cam.Render();
                    _cam.targetTexture = null;
                }
                RenderTexture.active = _rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "outfit-preview", wrapMode = TextureWrapMode.Clamp };
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply(true, true);
                return tex;
            }
            finally
            {
                RenderTexture.active = active;
                _cam.enabled = false;
                _key.enabled = _fill.enabled = false;
                RenderSettings.ambientProbe = probe;
                RenderSettings.fog = fog;
                RenderSettings.sun = sun;
                foreach (var l in off) if (l != null) l.enabled = true;
            }
        }

        // ── fur: a painted Gooby head in body, belly and inner-ear colour ──

        private static readonly Color Ink = new Color(0.23f, 0.21f, 0.31f);
        private static readonly Color Sprout = new Color(0.50f, 0.81f, 0.61f);

        private static Texture2D FurTile(CosmeticDef def)
        {
            var body = ArtLibrary.Hex(def.Farben.Count > 0 ? def.Farben[0] : "#F8EAD3");
            var belly = ArtLibrary.Hex(def.Farben.Count > 1 ? def.Farben[1] : "#FFF9EC");
            var ear = ArtLibrary.Hex(def.Farben.Count > 2 ? def.Farben[2] : "#F4A6B4");
            var rim = Color.Lerp(body, Ink, 0.38f);
            var px = new Color[Size * Size];
            var pixel = 2f / Size;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                // y up, the tile spans −1..1
                var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);
                var head = Ellipse(p, new Vector2(0f, 0.02f), new Vector2(0.52f, 0.48f));
                var earL = Ellipse(Rotate(p - new Vector2(-0.5f, -0.22f), -14f), Vector2.zero, new Vector2(0.19f, 0.44f));
                var earR = Ellipse(Rotate(p - new Vector2(0.5f, -0.22f), 14f), Vector2.zero, new Vector2(0.19f, 0.44f));
                var shape = Mathf.Min(head, Mathf.Min(earL, earR));
                var stem = Capsule(p, new Vector2(0f, 0.46f), new Vector2(0.015f, 0.66f), 0.028f);
                var leafL = Ellipse(Rotate(p - new Vector2(-0.1f, 0.68f), 28f), Vector2.zero, new Vector2(0.12f, 0.06f));
                var leafR = Ellipse(Rotate(p - new Vector2(0.11f, 0.71f), -24f), Vector2.zero, new Vector2(0.13f, 0.065f));
                var sprout = Mathf.Min(stem, Mathf.Min(leafL, leafR));

                var c = new Color(0f, 0f, 0f, 0f);
                c = Over(c, rim, Cover(Mathf.Min(shape, sprout) - 0.035f, pixel));
                c = Over(c, Sprout, Cover(sprout, pixel));
                c = Over(c, body, Cover(shape, pixel));
                var innerL = Ellipse(Rotate(p - new Vector2(-0.5f, -0.27f), -14f), Vector2.zero, new Vector2(0.09f, 0.31f));
                var innerR = Ellipse(Rotate(p - new Vector2(0.5f, -0.27f), 14f), Vector2.zero, new Vector2(0.09f, 0.31f));
                c = Over(c, ear, Cover(Mathf.Min(innerL, innerR), pixel));
                // the head sits in front of the ears: its outline is drawn again over them
                c = Over(c, rim, Cover(head - 0.03f, pixel));
                c = Over(c, body, Cover(head, pixel));
                c = Over(c, belly, Cover(Ellipse(p, new Vector2(0f, -0.2f), new Vector2(0.27f, 0.18f)), pixel));
                c = Over(c, ear, 0.55f * Cover(Mathf.Min(Ellipse(p, new Vector2(-0.31f, -0.1f), new Vector2(0.08f, 0.06f)), Ellipse(p, new Vector2(0.31f, -0.1f), new Vector2(0.08f, 0.06f))), pixel));
                c = Over(c, Ink, Cover(Mathf.Min(Ellipse(p, new Vector2(-0.18f, 0.04f), new Vector2(0.055f, 0.065f)), Ellipse(p, new Vector2(0.18f, 0.04f), new Vector2(0.055f, 0.065f))), pixel));
                c = Over(c, Color.white, Cover(Mathf.Min(Ellipse(p, new Vector2(-0.165f, 0.07f), new Vector2(0.018f, 0.018f)), Ellipse(p, new Vector2(0.195f, 0.07f), new Vector2(0.018f, 0.018f))), pixel));
                c = Over(c, new Color(0.91f, 0.55f, 0.63f), Cover(Ellipse(p, new Vector2(0f, -0.12f), new Vector2(0.05f, 0.034f)), pixel));
                px[y * Size + x] = c;
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "outfit-fur:" + def.Id, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Approximate signed distance to an ellipse (negative inside), in tile units.</summary>
        private static float Ellipse(Vector2 p, Vector2 center, Vector2 radii)
        {
            var q = new Vector2((p.x - center.x) / radii.x, (p.y - center.y) / radii.y);
            return (q.magnitude - 1f) * Mathf.Min(radii.x, radii.y);
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var pa = p - a; var ba = b - a;
            var h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            var a = degrees * Mathf.Deg2Rad;
            var cs = Mathf.Cos(a); var sn = Mathf.Sin(a);
            return new Vector2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
        }

        /// <summary>Anti-aliased coverage of a shape from its signed distance.</summary>
        private static float Cover(float distance, float pixel) => Mathf.Clamp01(0.5f - distance / pixel);

        private static Color Over(Color under, Color top, float alpha)
        {
            if (alpha <= 0f) return under;
            var a = alpha * top.a;
            var outA = a + under.a * (1f - a);
            if (outA <= 0f) return new Color(0f, 0f, 0f, 0f);
            var rgb = (new Vector3(top.r, top.g, top.b) * a + new Vector3(under.r, under.g, under.b) * under.a * (1f - a)) / outA;
            return new Color(rgb.x, rgb.y, rgb.z, outA);
        }
    }
}
