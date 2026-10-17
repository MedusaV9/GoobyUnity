using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gooby.Unity.App
{
    /// <summary>
    /// The photos of the camera app (port of foto_modus.gd's gallery folder): JPEGs under &lt;DataDir&gt;/photos, newest
    /// first, at most <see cref="MaxPhotos"/> (the oldest go). A snapshot renders the world camera into its own
    /// RenderTexture — the UI never ends up on a photo. Headless players store a generated placeholder instead.
    /// </summary>
    public static class PhotoAlbum
    {
        public const int MaxPhotos = 60;
        public const int Width = 800, Height = 450;

        public static string Folder(GameHost host) => Path.Combine(host.DataDir, "photos");

        /// <summary>All photo files, newest first.</summary>
        public static List<string> List(GameHost host)
        {
            var dir = Folder(host);
            var files = new List<string>();
            if (!Directory.Exists(dir)) return files;
            files.AddRange(Directory.GetFiles(dir, "*.jpg"));
            files.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
            return files;
        }

        /// <summary>Takes a photo with <paramref name="cam"/> (frame painted in by <paramref name="decorate"/>) and stores it; returns its path.</summary>
        public static string Take(GameHost host, Camera cam, Action<Texture2D> decorate = null)
        {
            var dir = Folder(host);
            Directory.CreateDirectory(dir);
            var name = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".jpg";
            var path = Path.Combine(dir, name);
            Texture2D tex = null;
            try
            {
                tex = PhotoCapture.Headless || cam == null ? Placeholder() : Render(cam);
                decorate?.Invoke(tex);
                File.WriteAllBytes(path, tex.EncodeToJPG(88));
                Debug.Log("[photo] saved " + name + " " + tex.width + "x" + tex.height);
            }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            var all = List(host);
            for (var i = MaxPhotos; i < all.Count; i++) File.Delete(all[i]);
            return path;
        }

        public static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>Loads a stored photo as a texture (the caller destroys it).</summary>
        public static Texture2D Load(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            return tex.LoadImage(File.ReadAllBytes(path)) ? tex : null;
        }

        private static Texture2D Render(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prevTarget;
                }
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply(false);
                return tex;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Texture2D Placeholder()
        {
            var tex = new Texture2D(Width / 4, Height / 4, TextureFormat.RGB24, false);
            var px = new Color32[tex.width * tex.height];
            for (var y = 0; y < tex.height; y++)
                for (var x = 0; x < tex.width; x++)
                    px[y * tex.width + x] = Color32.Lerp(new Color32(255, 214, 186, 255), new Color32(127, 184, 255, 255), y / (float)tex.height);
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>Paints a frame style into the picture: "polaroid" (cream border, thick foot), "herzen" (pink border with hearts).</summary>
        public static void Frame(Texture2D tex, string style)
        {
            if (tex == null || style == null || style == "ohne") return;
            var w = tex.width; var h = tex.height;
            var px = tex.GetPixels32();
            var border = Mathf.Max(4, w / 40);
            var foot = style == "polaroid" ? border * 4 : border;
            var col = style == "polaroid" ? new Color32(255, 250, 240, 255) : new Color32(255, 160, 190, 255);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var edge = x < border || x >= w - border || y >= h - border || y < foot;
                    if (edge) px[y * w + x] = col;
                }
            if (style == "herzen")
            {
                var heart = new Color32(255, 255, 255, 255);
                for (var i = 0; i < 14; i++)
                {
                    var cx = i * w / 14 + w / 28;
                    Dot(px, w, h, cx, border / 2, Mathf.Max(2, border / 3), heart);
                    Dot(px, w, h, cx, h - border / 2 - 1, Mathf.Max(2, border / 3), heart);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
        }

        private static void Dot(Color32[] px, int w, int h, int cx, int cy, int r, Color32 c)
        {
            for (var y = cy - r; y <= cy + r; y++)
                for (var x = cx - r; x <= cx + r; x++)
                    if (x >= 0 && y >= 0 && x < w && y < h && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[y * w + x] = c;
        }
    }
}
