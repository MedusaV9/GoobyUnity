using System.Collections.Generic;
using Gooby.Core.Home;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The "Gestalten" wall and floor patterns as textures (customize_materials.gd): each (pattern, palette colour) pair
    /// is drawn once from <see cref="HouseStyle.Wert"/> + <see cref="HouseStyle.Blend"/> into a 128 px tile and cached;
    /// the same texture feeds the room surfaces (tiled in world metres) and the option tiles of the screen.
    /// </summary>
    public static class SurfaceTextures
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string muster, string farbe)
        {
            var key = muster + "|" + farbe;
            if (Cache.TryGetValue(key, out var tex) && tex != null) return tex;
            var n = HouseStyle.TexPx;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "pattern:" + key, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 2 };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var (r, g, b) = HouseStyle.Blend(HouseStyle.Wert(muster, x, y), farbe);
                    px[(n - 1 - y) * n + x] = new Color32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), 255);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            Cache[key] = tex;
            return tex;
        }

        /// <summary>A surface of <paramref name="sizeMetres"/> (a box face maps 0..1) tiled at the pattern's metres per tile.</summary>
        public static Material Surface(string muster, string farbe, Vector2 sizeMetres, float smoothness = 0.12f)
        {
            var meter = HouseStyle.MeterProKachel.TryGetValue(muster, out var m) ? (float)m : 1f;
            var tiling = new Vector2(Mathf.Round(sizeMetres.x / meter * 4f) / 4f, Mathf.Round(sizeMetres.y / meter * 4f) / 4f);
            return ArtLibrary.Textured(Get(muster, farbe), muster + "|" + farbe, tiling, smoothness);
        }
    }
}
