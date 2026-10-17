using System;
using System.Collections.Generic;
using Gooby.Core.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Snapshots of the game view for photo letters and InstantGooby: the world camera renders into a small
    /// RenderTexture (320×240, no UI on it) and goes out as JPEG, well under the server's 512 KB. Headless players
    /// (<c>-batchmode -nographics</c>) have no GPU, they send <see cref="NetPhoto.Placeholder"/> instead so QA tours
    /// still carry a real picture end to end. Also turns received JPEG/PNG bytes back into textures (small LRU cache).
    /// </summary>
    public static class PhotoCapture
    {
        public const int Width = 320;
        public const int Height = 240;
        public const int JpgQuality = 80;

        /// <summary>No GPU to render with: batch mode or the null graphics device.</summary>
        public static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        /// <summary>JPEG of what the world camera sees; the placeholder PNG when headless or the capture fails.</summary>
        public static byte[] Capture(Camera cam, int seed, out bool placeholder)
        {
            placeholder = false;
            if (!Headless && cam != null)
            {
                try
                {
                    var jpg = RenderJpg(cam);
                    if (NetPhoto.Check(jpg).Length == 0)
                    {
                        Debug.Log("[photo] captured " + Width + "x" + Height + " jpg " + (jpg.Length / 1024) + " KB");
                        return jpg;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[photo] capture failed, using placeholder: " + e.Message);
                }
            }
            placeholder = true;
            var png = NetPhoto.Placeholder(Width, Height, seed);
            Debug.Log("[photo] placeholder png " + (png.Length / 1024) + " KB (headless=" + Headless + ")");
            return png;
        }

        private static byte[] RenderJpg(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prevTarget;
                }
                RenderTexture.active = rt;
                tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply(false);
                var quality = JpgQuality;
                var jpg = tex.EncodeToJPG(quality);
                while (jpg != null && jpg.Length > NetPhoto.MaxBytes && quality > 30)
                {
                    quality -= 15;
                    jpg = tex.EncodeToJPG(quality);
                }
                return jpg;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>JPEG/PNG bytes → texture (null when they do not decode).</summary>
        public static Texture2D ToTexture(byte[] bytes)
        {
            if (!NetPhoto.IsImage(bytes)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "gooby-photo" };
                if (tex.LoadImage(bytes, true)) return tex;
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[photo] decode failed: " + e.Message);
            }
            return null;
        }
    }

    /// <summary>Decoded photos by photoId (letters + feed), oldest destroyed first.</summary>
    public sealed class PhotoTextureCache
    {
        public const int Max = 40;
        private readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        private readonly LinkedList<string> _order = new LinkedList<string>();

        public Texture2D Get(string id) => id != null && _tex.TryGetValue(id, out var t) && t != null ? t : null;

        public void Put(string id, Texture2D tex)
        {
            if (string.IsNullOrEmpty(id) || tex == null || _tex.ContainsKey(id)) return;
            _tex[id] = tex;
            _order.AddLast(id);
            while (_order.Count > Max)
            {
                var old = _order.First.Value;
                _order.RemoveFirst();
                if (_tex.TryGetValue(old, out var t) && t != null) UnityEngine.Object.Destroy(t);
                _tex.Remove(old);
            }
        }
    }
}
