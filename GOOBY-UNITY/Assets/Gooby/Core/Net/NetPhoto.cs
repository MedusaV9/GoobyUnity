using System;
using System.IO;
using System.IO.Compression;

namespace Gooby.Core.Net
{
    /// <summary>
    /// Photo rules of GOOBY-SERVER mail.js (letters with a photo, InstantGooby posts): only JPEG (FF D8 FF) or PNG
    /// (89 50 4E 47), at most <see cref="MaxBytes"/> decoded, sent as Base64 in the REST JSON body. Plus an engine-free
    /// PNG encoder for the placeholder picture that headless QA players (-batchmode -nographics) send instead of a
    /// real camera shot.
    /// </summary>
    public static class NetPhoto
    {
        /// <summary>Server default GOOBY_MAX_PHOTO_KB (config.js).</summary>
        public const int MaxPhotoKb = 512;
        public const int MaxBytes = MaxPhotoKb * 1024;
        /// <summary>InstantGooby caption limit (mail.js CAPTION_MAX).</summary>
        public const int CaptionMax = 120;

        public const string NoPhoto = "NO_PHOTO";
        public const string BadPhoto = "BAD_PHOTO";
        public const string TooLarge = "PHOTO_TOO_LARGE";

        public static bool IsJpeg(byte[] b) => b != null && b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

        public static bool IsPng(byte[] b) => b != null && b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

        /// <summary>Same magic-byte check as the server's isImage().</summary>
        public static bool IsImage(byte[] b) => IsJpeg(b) || IsPng(b);

        /// <summary>"" when the server will take it, else NO_PHOTO / BAD_PHOTO / PHOTO_TOO_LARGE.</summary>
        public static string Check(byte[] b)
        {
            if (b == null || b.Length == 0) return NoPhoto;
            if (!IsImage(b)) return BadPhoto;
            if (b.Length > MaxBytes) return TooLarge;
            return "";
        }

        public static string ToBase64(byte[] b) => b == null ? "" : Convert.ToBase64String(b);

        /// <summary>Decodes the server's photoB64; null when empty or not Base64.</summary>
        public static byte[] FromBase64(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                var b = Convert.FromBase64String(s);
                return b.Length > 0 ? b : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ placeholder

        /// <summary>
        /// A cute stand-in picture (pastel sky, meadow, a round cream Gooby with eyes and blush) as PNG — used when no
        /// GPU can render the world (headless QA), so photo letters and feed posts still travel end to end. The seed
        /// shifts the sky tint and the Gooby's position so two clients' pictures differ.
        /// </summary>
        public static byte[] Placeholder(int width = 320, int height = 240, int seed = 0)
        {
            width = Math.Max(8, Math.Min(1024, width));
            height = Math.Max(8, Math.Min(1024, height));
            var rgb = new byte[width * height * 3];
            var rnd = new System.Random(seed);
            // sky: warm cream → blossom pink, tint nudged by the seed
            var skyTop = new[] { 255, 236 - rnd.Next(0, 20), 214 + rnd.Next(0, 30) };
            var skyLow = new[] { 255, 214, 222 };
            var grass = new[] { 150, 205 + rnd.Next(0, 20), 140 };
            var grassDark = new[] { 124, 184, 118 };
            var fur = new[] { 255, 244, 224 };
            var furShade = new[] { 238, 218, 190 };
            var eye = new[] { 74, 66, 88 };
            var blush = new[] { 255, 170, 180 };
            var horizon = height * 62 / 100;
            var cx = width / 2 + (rnd.Next(0, 61) - 30) * width / 320;
            var r = Math.Min(width, height) * 22 / 100;
            var cy = horizon - r / 3;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int[] c;
                    if (y < horizon)
                    {
                        var t = y / (double)Math.Max(1, horizon);
                        c = Lerp(skyTop, skyLow, t);
                    }
                    else
                    {
                        // meadow with soft diagonal mowing stripes
                        c = ((x + y * 2) / 18) % 2 == 0 ? grass : grassDark;
                    }
                    var dx = x - cx;
                    var dy = y - cy;
                    var d2 = dx * dx + dy * dy;
                    if (d2 <= r * r)
                    {
                        c = dy > r / 3 && d2 > (r * 8 / 10) * (r * 8 / 10) ? furShade : fur;
                        var er = Math.Max(2, r / 9);
                        var ey = cy - r / 6;
                        foreach (var ex in new[] { cx - r / 3, cx + r / 3 })
                        {
                            if ((x - ex) * (x - ex) + (y - ey) * (y - ey) <= er * er) c = eye;
                            var by = cy + r / 8;
                            var br = Math.Max(2, r / 7);
                            var bx = ex + (ex < cx ? -r / 8 : r / 8);
                            if ((x - bx) * (x - bx) + (y - by) * (y - by) <= br * br && c != eye) c = blush;
                        }
                    }
                    var i = (y * width + x) * 3;
                    rgb[i] = (byte)c[0];
                    rgb[i + 1] = (byte)c[1];
                    rgb[i + 2] = (byte)c[2];
                }
            }
            return EncodePng(width, height, rgb);
        }

        private static int[] Lerp(int[] a, int[] b, double t) => new[]
        {
            (int)Math.Round(a[0] + (b[0] - a[0]) * t),
            (int)Math.Round(a[1] + (b[1] - a[1]) * t),
            (int)Math.Round(a[2] + (b[2] - a[2]) * t),
        };

        /// <summary>RGB8 (row-major, top row first, 3 bytes per pixel) → PNG (colour type 2, filter 0 per row, zlib).</summary>
        public static byte[] EncodePng(int width, int height, byte[] rgb)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgb == null || rgb.Length < width * height * 3) throw new ArgumentException("rgb too short", nameof(rgb));
            var raw = new byte[(width * 3 + 1) * height];
            for (var y = 0; y < height; y++)
            {
                var o = y * (width * 3 + 1);
                raw[o] = 0; // filter: none
                Buffer.BlockCopy(rgb, y * width * 3, raw, o + 1, width * 3);
            }
            byte[] deflated;
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
                deflated = ms.ToArray();
            }
            var zlib = new byte[deflated.Length + 6];
            zlib[0] = 0x78;
            zlib[1] = 0x9C;
            Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);
            WriteBe(zlib, zlib.Length - 4, Adler32(raw));

            using (var png = new MemoryStream())
            {
                png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13];
                WriteBe(ihdr, 0, (uint)width);
                WriteBe(ihdr, 4, (uint)height);
                ihdr[8] = 8; // bit depth
                ihdr[9] = 2; // colour type RGB
                Chunk(png, "IHDR", ihdr);
                Chunk(png, "IDAT", zlib);
                Chunk(png, "IEND", new byte[0]);
                return png.ToArray();
            }
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteBe(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            var td = new byte[4 + data.Length];
            for (var i = 0; i < 4; i++) td[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            var crc = new byte[4];
            WriteBe(crc, 0, Crc32(td));
            s.Write(crc, 0, 4);
        }

        private static void WriteBe(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }

        private static uint[] _crcTable;

        public static uint Crc32(byte[] data)
        {
            if (_crcTable == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    var c = n;
                    for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                _crcTable = t;
            }
            var crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}
