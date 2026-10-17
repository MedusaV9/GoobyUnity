using System.Collections.Generic;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Board-06 scenery kit for the portrait minigame fields ("weiche Formen, liebevolle Details"): soft gradient skies,
    /// puffy clouds, rounded hills, meadow strips with grass tufts and dot flowers, wooden fences, pillow tiles and a
    /// kitchen window — plain UI Toolkit shapes (classes in gooby-mgfields.uss, colours inline per host), built once in
    /// BuildPlayField. Every piece ignores the pointer (the hosts read <c>e.localPosition</c> of the picked element) and
    /// lives in a <see cref="Layer"/> inserted before the host's own elements, so the engine's objects always draw on top.
    /// Nothing here touches gameplay: hosts may translate a few whole groups per frame (parallax), never re-layout.
    /// </summary>
    internal static class MgScenery
    {
        public const string LayerClass = "gb-mgf-layer";

        /// <summary>#RRGGBB as a colour (board 06 swatches read straight from the guide).</summary>
        public static Color Hex(uint rgb, float a = 1f) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        /// <summary>A full-field scenery layer: behind everything the host added so far, after earlier scenery layers.</summary>
        public static VisualElement Layer(VisualElement field, string name)
        {
            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            layer.AddToClassList(LayerClass);
            Ux.Abs(layer, 0, 0, 0, 0);
            var at = 0;
            foreach (var c in field.Children())
            {
                if (c.ClassListContains(LayerClass)) at++;
                else break;
            }
            field.Insert(at, layer);
            return layer;
        }

        /// <summary>A pointer-transparent absolute group (a strip that scrolls as one translate, a cloud, a fence).</summary>
        public static VisualElement Group(VisualElement parent, string cls = null)
        {
            var g = new VisualElement { pickingMode = PickingMode.Ignore };
            g.AddToClassList("gb-mgf-group");
            if (cls != null) g.AddToClassList(cls);
            g.style.position = Position.Absolute;
            parent.Add(g);
            return g;
        }

        /// <summary>A filled absolute shape, optionally fully rounded.</summary>
        public static VisualElement Shape(VisualElement parent, Color color, float radius = 0f, string cls = null)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("gb-mgf-shape");
            if (cls != null) e.AddToClassList(cls);
            e.style.position = Position.Absolute;
            e.style.backgroundColor = color;
            if (radius > 0f) Round(e, radius);
            parent.Add(e);
            return e;
        }

        public static VisualElement Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r; e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
            return e;
        }

        public static VisualElement RoundTop(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            return e;
        }

        public static VisualElement RoundBottom(VisualElement e, float r)
        {
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
            return e;
        }

        /// <summary>The chunky toy outline of the board (all four sides).</summary>
        public static VisualElement Outline(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
            e.style.borderTopColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c; e.style.borderRightColor = c;
            return e;
        }

        /// <summary>Percent placement inside the parent (null = leave unset).</summary>
        public static VisualElement Pct(VisualElement e, float? left, float? top, float? right = null, float? bottom = null)
        {
            if (left.HasValue) e.style.left = Length.Percent(left.Value);
            if (top.HasValue) e.style.top = Length.Percent(top.Value);
            if (right.HasValue) e.style.right = Length.Percent(right.Value);
            if (bottom.HasValue) e.style.bottom = Length.Percent(bottom.Value);
            return e;
        }

        public static VisualElement Size(VisualElement e, float w, float h)
        {
            e.style.width = w; e.style.height = h;
            return e;
        }

        // ── sky ──────────────────────────────────────────────────────────────

        private static readonly Dictionary<long, Texture2D> Gradients = new Dictionary<long, Texture2D>();

        private static long Key(Color a, Color b)
        {
            var x = (Color32)a; var y = (Color32)b;
            return ((long)x.r << 56) | ((long)x.g << 48) | ((long)x.b << 40) | ((long)x.a << 32) | ((long)y.r << 24) | ((long)y.g << 16) | ((long)y.b << 8) | y.a;
        }

        /// <summary>A tiny vertical gradient texture (cached per colour pair), stretched by the element — one quad, no bands.</summary>
        private static Texture2D GradientTex(Color top, Color bottom)
        {
            var key = Key(top, bottom);
            if (Gradients.TryGetValue(key, out var tex) && tex != null) return tex;
            const int H = 32;
            tex = new Texture2D(2, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[2 * H];
            // texture row 0 is the bottom of the drawn image
            for (var y = 0; y < H; y++) { var c = Color.Lerp(bottom, top, y / (H - 1f)); px[y * 2] = c; px[y * 2 + 1] = c; }
            tex.SetPixels(px);
            tex.Apply(false, true);
            Gradients[key] = tex;
            return tex;
        }

        /// <summary>A soft top→bottom gradient filling the parent between two heights (percent of the parent).</summary>
        public static VisualElement Gradient(VisualElement parent, Color top, Color bottom, float fromPct = 0f, float toPct = 100f)
        {
            var e = Shape(parent, Color.clear, 0f, "gb-mgf-sky");
            Pct(e, 0, fromPct, 0);
            e.style.height = Length.Percent(toPct - fromPct);
            e.style.backgroundImage = new StyleBackground(GradientTex(top, bottom));
            e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            return e;
        }

        /// <summary>A round sun (or moon) with a soft halo ring.</summary>
        public static VisualElement Sun(VisualElement parent, float size, Color core, Color halo)
        {
            var g = Group(parent, "gb-mgf-sun");
            Size(g, size * 1.6f, size * 1.6f);
            var h = Shape(g, halo, size * 0.8f, "gb-mgf-sun-halo");
            Ux.Abs(h, 0, 0, 0, 0);
            var c = Shape(g, core, size * 0.5f, "gb-mgf-sun-core");
            c.style.left = size * 0.3f; c.style.top = size * 0.3f; Size(c, size, size);
            return g;
        }

        /// <summary>A puffy cloud: three round puffs on a flat pill with a soft blue underside (width <paramref name="w"/>).</summary>
        public static VisualElement Cloud(VisualElement parent, float w, Color? tint = null)
        {
            var col = tint ?? Color.white;
            var g = Group(parent, "gb-mgf-cloud");
            Size(g, w, w * 0.52f);
            Puff(g, col, w * 0.12f, w * 0.1f, w * 0.28f);
            Puff(g, col, w * 0.32f, w * 0.1f, w * 0.42f);
            Puff(g, col, w * 0.6f, w * 0.1f, w * 0.3f);
            var b = Shape(g, col, w * 0.13f, "gb-mgf-puff");
            b.style.left = 0; b.style.right = 0; b.style.bottom = 0; b.style.height = w * 0.26f;
            b.style.borderBottomWidth = 3; b.style.borderBottomColor = new Color(0.66f, 0.78f, 0.95f, 0.55f);
            return g;
        }

        private static void Puff(VisualElement g, Color col, float left, float bottom, float d)
        {
            var p = Shape(g, col, d * 0.5f, "gb-mgf-puff");
            p.style.left = left; p.style.bottom = bottom; Size(p, d, d);
        }

        // ── land ─────────────────────────────────────────────────────────────

        /// <summary>A soft rounded hill (w × h) with a lighter rim along its crown; the caller places left/bottom.</summary>
        public static VisualElement Hill(VisualElement parent, float w, float h, Color color, Color rim)
        {
            var e = Shape(parent, color, 0f, "gb-mgf-hill");
            Size(e, w, h);
            RoundTop(e, Mathf.Min(w * 0.5f, h));
            e.style.borderTopWidth = 4; e.style.borderLeftWidth = 3; e.style.borderRightWidth = 3;
            e.style.borderTopColor = rim; e.style.borderLeftColor = rim; e.style.borderRightColor = rim;
            return e;
        }

        /// <summary>A grass tuft: three rounded blades fanning from one foot (22 × 18 at scale 1); the caller places it.</summary>
        public static VisualElement Tuft(VisualElement parent, Color color, float scale = 1f)
        {
            var g = Group(parent, "gb-mgf-tuft");
            Size(g, 22f * scale, 18f * scale);
            Blade(g, color, 3f * scale, 13f * scale, -24f, scale);
            Blade(g, color, 8f * scale, 18f * scale, 0f, scale);
            Blade(g, color, 13f * scale, 13f * scale, 24f, scale);
            return g;
        }

        private static void Blade(VisualElement g, Color color, float left, float h, float rot, float scale)
        {
            var b = Shape(g, color, 0f, "gb-mgf-blade");
            b.style.left = left; b.style.bottom = 0; Size(b, 6f * scale, h);
            RoundTop(b, 3f * scale);
            b.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100), 0f);
            b.style.rotate = new Rotate(rot);
        }

        /// <summary>A little dot flower on a stem (petal disc with a sunny centre, 16 × 26 at scale 1).</summary>
        public static VisualElement Flower(VisualElement parent, Color petal, float scale = 1f)
        {
            var g = Group(parent, "gb-mgf-flower");
            Size(g, 16f * scale, 26f * scale);
            var stem = Shape(g, Hex(0x5FA866), 1.5f * scale);
            stem.style.left = 6.5f * scale; stem.style.bottom = 0; Size(stem, 3f * scale, 14f * scale);
            var head = Shape(g, petal, 8f * scale, "gb-mgf-flower-head");
            head.style.left = 0; head.style.top = 0; Size(head, 16f * scale, 16f * scale);
            Outline(head, 2f * scale, new Color(1f, 1f, 1f, 0.85f));
            var eye = Shape(g, Hex(0xFFD166), 3f * scale);
            eye.style.left = 5f * scale; eye.style.top = 5f * scale; Size(eye, 6f * scale, 6f * scale);
            return g;
        }

        /// <summary>A painted sprite from Resources/gooby-art (plant_bush, plant_daisies, plant_tree_round …), aspect kept.</summary>
        public static VisualElement Sprite(VisualElement parent, string art, float w, float h)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore, name = "mgf-" + art };
            e.AddToClassList("gb-mgf-sprite");
            e.style.position = Position.Absolute;
            Size(e, w, h);
            var tex = ArtLibrary.Tex(art);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            e.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Bottom);
            parent.Add(e);
            return e;
        }

        /// <summary>A wooden picket fence across the parent (full width, <paramref name="h"/> tall); the caller sets bottom/top.</summary>
        public static VisualElement Fence(VisualElement parent, int posts, float h, Color wood, Color rim)
        {
            var g = Group(parent, "gb-mgf-fence");
            g.style.left = 0; g.style.right = 0; g.style.height = h;
            for (var i = 0; i < 2; i++)
            {
                var rail = Shape(g, wood, 3.5f, "gb-mgf-rail");
                rail.style.left = -6; rail.style.right = -6; rail.style.height = 8;
                rail.style.top = Length.Percent(i == 0 ? 26 : 62);
                Outline(rail, 2f, rim);
            }
            for (var i = 0; i < posts; i++)
            {
                var post = Shape(g, wood, 0f, "gb-mgf-post");
                post.style.left = Length.Percent((i + 0.5f) * 100f / posts);
                post.style.translate = new Translate(Length.Percent(-50), 0);
                post.style.top = 0; post.style.bottom = 0; post.style.width = 13;
                RoundTop(post, 6.5f);
                RoundBottom(post, 2f);
                Outline(post, 2f, rim);
            }
            return g;
        }

        /// <summary>A grid of pillow tiles (rounded, rimmed) filling the parent's box; the caller places and sizes the group.</summary>
        public static VisualElement Tiles(VisualElement parent, int cols, int rows, Color a, Color b, Color grout, Color rim)
        {
            var g = Group(parent, "gb-mgf-tiles");
            g.style.backgroundColor = grout;
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var cell = Group(g);
                    Pct(cell, c * 100f / cols, r * 100f / rows);
                    cell.style.width = Length.Percent(100f / cols); cell.style.height = Length.Percent(100f / rows);
                    var tile = Shape(cell, (r + c) % 2 == 0 ? a : b, 7f, "gb-mgf-tile");
                    Ux.Abs(tile, 2, 2, 2, 2);
                    tile.style.borderBottomColor = rim;
                }
            return g;
        }

        /// <summary>A kitchen window: wooden frame, sky with a cloud and a hill, cross bars, pink curtains and a sill (w × h).</summary>
        public static VisualElement Window(VisualElement parent, float w, float h)
        {
            var wood = Hex(0xE8C9A0); var woodRim = Hex(0xC8A27A);
            var g = Group(parent, "gb-mgf-window");
            Size(g, w, h);
            var pane = Shape(g, Hex(0xCFE6FF), 14f, "gb-mgf-pane");
            Ux.Abs(pane, 0, 0, 0, 0);
            Gradient(pane, Hex(0xB7D8FF), Hex(0xEAF4FF));
            var hill = Hill(pane, w * 0.9f, h * 0.3f, Hex(0xA9DD9B), Hex(0xC6F39A));
            hill.style.left = -w * 0.15f; hill.style.bottom = -4;
            var cloud = Cloud(pane, w * 0.36f);
            cloud.style.left = w * 0.46f; cloud.style.top = h * 0.14f;
            var barV = Shape(g, wood, 3f); barV.style.left = w * 0.5f - 4f; barV.style.top = 0; barV.style.bottom = 0; barV.style.width = 8;
            var barH = Shape(g, wood, 3f); barH.style.top = h * 0.5f - 4f; barH.style.left = 0; barH.style.right = 0; barH.style.height = 8;
            var frame = Shape(g, Color.clear, 14f, "gb-mgf-frame");
            Ux.Abs(frame, 0, 0, 0, 0);
            Outline(frame, 7f, wood);
            var inner = Shape(g, Color.clear, 9f);
            Ux.Abs(inner, 5, 5, 5, 5);
            Outline(inner, 2f, woodRim);
            for (var s = 0; s < 2; s++)
            {
                var curtain = Shape(g, Hex(0xFF9BC1), 0f, "gb-mgf-curtain");
                curtain.style.top = -6; curtain.style.width = w * 0.2f; curtain.style.height = h * 0.8f;
                if (s == 0) curtain.style.left = -8; else curtain.style.right = -8;
                RoundBottom(curtain, w * 0.1f);
                RoundTop(curtain, 5f);
                Outline(curtain, 2f, Hex(0xE97FA6));
            }
            var rod = Shape(g, woodRim, 4f); rod.style.left = -16; rod.style.right = -16; rod.style.top = -12; rod.style.height = 8;
            var sill = Shape(g, wood, 5f, "gb-mgf-sill");
            sill.style.left = -14; sill.style.right = -14; sill.style.bottom = -12; sill.style.height = 14;
            Outline(sill, 2f, woodRim);
            return g;
        }

        // ── painted pieces: Painter2D into the element's own box, drawn once per layout (never per frame) ──

        /// <summary>A pointer-transparent absolute element that paints <paramref name="draw"/> into its box (w × h from layout).</summary>
        public static VisualElement Painted(VisualElement parent, System.Action<Painter2D, Rect> draw, string cls = null)
        {
            var e = new PaintedPiece(draw);
            if (cls != null) e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        private sealed class PaintedPiece : VisualElement
        {
            public PaintedPiece(System.Action<Painter2D, Rect> draw)
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("gb-mgf-painted");
                style.position = Position.Absolute;
                generateVisualContent += ctx =>
                {
                    var r = contentRect;
                    if (r.width < 1f || r.height < 1f) return;
                    var p = ctx.painter2D;
                    p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
                    draw(p, r);
                };
            }
        }

        public static Color Darker(Color c, float k) => new Color(c.r * (1f - k), c.g * (1f - k), c.b * (1f - k), c.a);
        public static Color Lighter(Color c, float k) => Color.Lerp(c, new Color(1f, 1f, 1f, c.a), k);
        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static Vector2 Quad(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            var u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        /// <summary>Deterministic 0..1 hash for scattered details (stars, pebbles, speckles).</summary>
        public static float Hash(int i, int salt)
        {
            unchecked
            {
                var h = (uint)(i * 374761393 + salt * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static void CirclePath(Painter2D p, Vector2 c, float r)
        {
            p.MoveTo(c + new Vector2(r, 0f));
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
        }

        private static void EllipsePath(Painter2D p, Vector2 c, float rx, float ry, float skipFrom = 0f, float skipTo = 0f)
        {
            const int N = 28;
            var first = true;
            for (var i = 0; i <= N; i++)
            {
                var deg = 360f * i / N;
                if (skipTo > skipFrom && deg > skipFrom && deg < skipTo) continue;
                var a = deg * Mathf.Deg2Rad;
                var pt = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                if (first) { p.MoveTo(pt); first = false; } else p.LineTo(pt);
            }
        }

        /// <summary>A round dot (painted, cheap), e.g. a pebble or a bulb, centred in its own box.</summary>
        public static void Disc(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillColor = col;
            p.BeginPath(); CirclePath(p, c, Mathf.Max(0.5f, r)); p.ClosePath(); p.Fill();
        }

        /// <summary>A filled ellipse (painted) with an optional rim.</summary>
        public static void Oval(Painter2D p, Vector2 c, float rx, float ry, Color col, Color? rim = null, float rimW = 2f)
        {
            p.fillColor = col;
            p.BeginPath(); EllipsePath(p, c, rx, ry); p.ClosePath(); p.Fill();
            if (rim.HasValue) { p.strokeColor = rim.Value; p.lineWidth = rimW; p.Stroke(); }
        }

        /// <summary>Pennant bunting on a sagging cord across the box: <paramref name="flags"/> soft triangles hang below it.</summary>
        public static VisualElement Bunting(VisualElement parent, int flags, float sag, Color[] colors, Color cord)
        {
            return Painted(parent, (p, r) =>
            {
                var a = new Vector2(0f, 3f); var b = new Vector2(r.width, 3f); var c = new Vector2(r.width * 0.5f, 3f + sag * 2f);
                var flagH = Mathf.Max(8f, r.height - sag - 6f);
                for (var i = 0; i < flags; i++)
                {
                    var p0 = Quad(a, c, b, (i + 0.14f) / flags); var p1 = Quad(a, c, b, (i + 0.86f) / flags);
                    var tip = (p0 + p1) * 0.5f + new Vector2(0f, flagH);
                    var col = colors[i % colors.Length];
                    p.fillColor = col;
                    p.BeginPath(); p.MoveTo(p0); p.LineTo(p1); p.LineTo(tip); p.ClosePath(); p.Fill();
                    p.strokeColor = Darker(col, 0.16f); p.lineWidth = 2f; p.Stroke();
                }
                p.strokeColor = cord; p.lineWidth = 2.5f;
                p.BeginPath(); p.MoveTo(a); p.QuadraticCurveTo(c, b); p.Stroke();
            }, "gb-mgf-bunting");
        }

        /// <summary>A string of fairy lights on a sagging wire: bulbs with a soft halo in the given colours.</summary>
        public static VisualElement StringLights(VisualElement parent, int bulbs, float sag, Color[] colors, Color wire)
        {
            return Painted(parent, (p, r) =>
            {
                var a = new Vector2(0f, 3f); var b = new Vector2(r.width, 3f); var c = new Vector2(r.width * 0.5f, 3f + sag * 2f);
                p.strokeColor = wire; p.lineWidth = 2f;
                p.BeginPath(); p.MoveTo(a); p.QuadraticCurveTo(c, b); p.Stroke();
                for (var i = 0; i < bulbs; i++)
                {
                    var at = Quad(a, c, b, (i + 0.5f) / bulbs) + new Vector2(0f, 7f);
                    var col = colors[i % colors.Length];
                    Disc(p, at, 11f, Alpha(col, 0.22f));
                    p.fillColor = Darker(wire, 0f);
                    p.BeginPath(); p.MoveTo(at + new Vector2(-2.5f, -8f)); p.LineTo(at + new Vector2(2.5f, -8f)); p.LineTo(at + new Vector2(2.5f, -4f)); p.LineTo(at + new Vector2(-2.5f, -4f)); p.ClosePath(); p.Fill();
                    Disc(p, at, 5f, col);
                    Disc(p, at + new Vector2(-1.5f, -1.5f), 1.8f, new Color(1f, 1f, 1f, 0.75f));
                }
            }, "gb-mgf-lights");
        }

        /// <summary>Soft water: a fill whose top edge is a row of round crests (<paramref name="amp"/> tall) with a light crest line.</summary>
        public static VisualElement Waves(VisualElement parent, int crests, float amp, Color fill, Color crest)
        {
            return Painted(parent, (p, r) =>
            {
                var w = r.width; var h = r.height; var step = w / crests;
                p.fillColor = fill;
                WavePath(p, crests, step, amp);
                p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0f, h)); p.ClosePath(); p.Fill();
                p.strokeColor = crest; p.lineWidth = 3f;
                WavePath(p, crests, step, amp);
                p.Stroke();
            }, "gb-mgf-waves");
        }

        private static void WavePath(Painter2D p, int crests, float step, float amp)
        {
            p.BeginPath(); p.MoveTo(new Vector2(0f, amp));
            for (var i = 0; i < crests; i++) p.QuadraticCurveTo(new Vector2((i + 0.5f) * step, -amp), new Vector2((i + 1) * step, amp));
        }

        /// <summary>A valance / awning: fills the box from the top, its bottom edge a row of round scallops with a rim.</summary>
        public static VisualElement Scallop(VisualElement parent, int bumps, float depth, Color fill, Color rim)
        {
            return Painted(parent, (p, r) =>
            {
                var w = r.width; var h = r.height; var step = w / bumps; var y = h - depth;
                p.fillColor = fill;
                p.BeginPath(); p.MoveTo(new Vector2(0f, 0f)); p.LineTo(new Vector2(w, 0f)); p.LineTo(new Vector2(w, y));
                for (var i = bumps - 1; i >= 0; i--) p.QuadraticCurveTo(new Vector2((i + 0.5f) * step, h + depth), new Vector2(i * step, y));
                p.ClosePath(); p.Fill();
                p.strokeColor = rim; p.lineWidth = 3f;
                p.BeginPath(); p.MoveTo(new Vector2(w, y));
                for (var i = bumps - 1; i >= 0; i--) p.QuadraticCurveTo(new Vector2((i + 0.5f) * step, h + depth), new Vector2(i * step, y));
                p.Stroke();
            }, "gb-mgf-scallop");
        }

        /// <summary>Gingham cloth: a base with translucent bands both ways (their crossings read darker), cells of <paramref name="cell"/> px.</summary>
        public static VisualElement Gingham(VisualElement parent, float cell, Color baseC, Color band)
        {
            return Painted(parent, (p, r) =>
            {
                FillRect(p, 0f, 0f, r.width, r.height, baseC);
                for (var x = 0f; x < r.width; x += cell * 2f) FillRect(p, x, 0f, cell, r.height, band);
                for (var y = 0f; y < r.height; y += cell * 2f) FillRect(p, 0f, y, r.width, cell, band);
            }, "gb-mgf-gingham");
        }

        /// <summary>A checker grid of <paramref name="cols"/> × <paramref name="rows"/> cells with thin grout lines.</summary>
        public static VisualElement Checker(VisualElement parent, int cols, int rows, Color a, Color b, Color? grout = null)
        {
            return Painted(parent, (p, r) =>
            {
                var cw = r.width / cols; var ch = r.height / rows;
                FillRect(p, 0f, 0f, r.width, r.height, a);
                for (var y = 0; y < rows; y++)
                    for (var x = 0; x < cols; x++)
                        if ((x + y) % 2 == 1) FillRect(p, x * cw, y * ch, cw, ch, b);
                if (!grout.HasValue) return;
                p.strokeColor = grout.Value; p.lineWidth = 2f;
                for (var x = 1; x < cols; x++) { p.BeginPath(); p.MoveTo(new Vector2(x * cw, 0f)); p.LineTo(new Vector2(x * cw, r.height)); p.Stroke(); }
                for (var y = 1; y < rows; y++) { p.BeginPath(); p.MoveTo(new Vector2(0f, y * ch)); p.LineTo(new Vector2(r.width, y * ch)); p.Stroke(); }
            }, "gb-mgf-checker");
        }

        public static void FillRect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); p.Fill();
        }

        /// <summary>A soft light beam: a trapezoid from a narrow top to the full box bottom, three nested layers of <paramref name="c"/>.</summary>
        public static VisualElement Beam(VisualElement parent, Color c, float topFrac = 0.2f)
        {
            return Painted(parent, (p, r) =>
            {
                var w = r.width; var h = r.height;
                for (var i = 0; i < 3; i++)
                {
                    var k = 1f - i * 0.28f;
                    var tw = w * topFrac * k * 0.5f; var bw = w * k * 0.5f;
                    p.fillColor = c;
                    p.BeginPath(); p.MoveTo(new Vector2(w * 0.5f - tw, 0f)); p.LineTo(new Vector2(w * 0.5f + tw, 0f));
                    p.LineTo(new Vector2(w * 0.5f + bw, h)); p.LineTo(new Vector2(w * 0.5f - bw, h)); p.ClosePath(); p.Fill();
                }
            }, "gb-mgf-beam");
        }

        /// <summary>Scattered dots (stars, sand speckles, pebbles) seeded by <paramref name="seed"/>, radius rMin..rMax.</summary>
        public static VisualElement Dots(VisualElement parent, int n, Color c, float rMin, float rMax, int seed)
        {
            return Painted(parent, (p, r) =>
            {
                for (var i = 0; i < n; i++)
                {
                    var at = new Vector2(Hash(i, seed) * r.width, Hash(i, seed + 17) * r.height);
                    Disc(p, at, Mathf.Lerp(rMin, rMax, Hash(i, seed + 31)), c);
                }
            }, "gb-mgf-dots");
        }

        /// <summary>A little four-point twinkle star (painted), size = box.</summary>
        public static VisualElement Twinkle(VisualElement parent, float size, Color c)
        {
            var e = Painted(parent, (p, r) =>
            {
                var m = new Vector2(r.width * 0.5f, r.height * 0.5f); var s = r.width * 0.5f; var k = s * 0.22f;
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(m + new Vector2(0f, -s)); p.LineTo(m + new Vector2(k, -k)); p.LineTo(m + new Vector2(s, 0f)); p.LineTo(m + new Vector2(k, k));
                p.LineTo(m + new Vector2(0f, s)); p.LineTo(m + new Vector2(-k, k)); p.LineTo(m + new Vector2(-s, 0f)); p.LineTo(m + new Vector2(-k, -k));
                p.ClosePath(); p.Fill();
            }, "gb-mgf-twinkle");
            Size(e, size, size);
            return e;
        }

        /// <summary>Soft fog / a bush row: one union of round puffs on a flat base (single fill, no doubled alpha).</summary>
        public static VisualElement Puffs(VisualElement parent, int puffs, Color c, Color? rim = null, int seed = 1)
        {
            return Painted(parent, (p, r) =>
            {
                var w = r.width; var h = r.height; var step = w / puffs;
                p.fillColor = c;
                p.BeginPath();
                var baseTop = h * 0.5f;
                p.MoveTo(new Vector2(0f, baseTop)); p.LineTo(new Vector2(w, baseTop)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0f, h)); p.ClosePath();
                for (var i = 0; i < puffs; i++)
                {
                    var rr = step * (0.62f + 0.22f * Hash(i, seed));
                    var cy = Mathf.Max(rr, h * (0.42f + 0.18f * Hash(i, seed + 5)));
                    CirclePath(p, new Vector2((i + 0.5f) * step, Mathf.Min(cy, h - 1f)), Mathf.Min(rr, h * 0.5f));
                    p.ClosePath();
                }
                p.Fill(FillRule.NonZero);
                if (!rim.HasValue) return;
                // a lighter highlight along the crowns
                p.strokeColor = rim.Value; p.lineWidth = 3f;
                for (var i = 0; i < puffs; i++)
                {
                    var rr = Mathf.Min(step * (0.62f + 0.22f * Hash(i, seed)), h * 0.5f) - 4f;
                    var cy = Mathf.Min(Mathf.Max(rr + 4f, h * (0.42f + 0.18f * Hash(i, seed + 5))), h - 1f);
                    if (rr < 4f) continue;
                    p.BeginPath(); p.Arc(new Vector2((i + 0.5f) * step, cy), rr, Angle.Degrees(205f), Angle.Degrees(290f)); p.Stroke();
                }
            }, "gb-mgf-puffs");
        }

        /// <summary>Cattail reeds: <paramref name="stems"/> leaning stems with long leaves and brown velvet heads across the box.</summary>
        public static VisualElement Reeds(VisualElement parent, int stems, Color stem, Color head, int seed = 3)
        {
            return Painted(parent, (p, r) =>
            {
                var w = r.width; var h = r.height;
                for (var i = 0; i < stems; i++)
                {
                    var x = w * (i + 0.5f) / stems + (Hash(i, seed) - 0.5f) * w / stems * 0.6f;
                    var top = h * (0.05f + 0.35f * Hash(i, seed + 3));
                    var lean = (Hash(i, seed + 7) - 0.5f) * 18f;
                    var foot = new Vector2(x, h); var tip = new Vector2(x + lean, top);
                    p.strokeColor = stem; p.lineWidth = 3f;
                    p.BeginPath(); p.MoveTo(foot); p.QuadraticCurveTo(new Vector2(x, (h + top) * 0.5f), tip); p.Stroke();
                    // a long leaf curling off the stem
                    p.fillColor = stem;
                    var side = i % 2 == 0 ? 1f : -1f;
                    p.BeginPath(); p.MoveTo(foot + new Vector2(-2f, 0f));
                    p.QuadraticCurveTo(new Vector2(x + side * 16f, h - (h - top) * 0.4f), new Vector2(x + side * 20f, h - (h - top) * 0.7f));
                    p.QuadraticCurveTo(new Vector2(x + side * 6f, h - (h - top) * 0.35f), foot + new Vector2(3f, 0f));
                    p.ClosePath(); p.Fill();
                    if (i % 3 == 1) continue;
                    p.strokeColor = head; p.lineWidth = 8f;
                    var h0 = Quad(foot, new Vector2(x, (h + top) * 0.5f), tip, 0.8f);
                    var h1 = Quad(foot, new Vector2(x, (h + top) * 0.5f), tip, 0.95f);
                    p.BeginPath(); p.MoveTo(h0); p.LineTo(h1); p.Stroke();
                }
            }, "gb-mgf-reeds");
        }

        /// <summary>A lily pad (ellipse with its notch) and optionally a pink bloom; the box is the pad.</summary>
        public static VisualElement LilyPad(VisualElement parent, float w, Color pad, Color rim, Color? bloom = null)
        {
            var e = Painted(parent, (p, r) =>
            {
                var c = new Vector2(r.width * 0.5f, r.height * 0.5f);
                var rx = r.width * 0.5f - 2f; var ry = r.height * 0.5f - 2f;
                p.fillColor = pad;
                p.BeginPath(); p.MoveTo(c); EllipsePathFrom(p, c, rx, ry, 18f, 342f); p.ClosePath(); p.Fill();
                p.strokeColor = rim; p.lineWidth = 2f; p.Stroke();
                p.strokeColor = Alpha(rim, 0.6f); p.lineWidth = 1.5f;
                for (var i = 0; i < 4; i++)
                {
                    var a = (70f + i * 70f) * Mathf.Deg2Rad;
                    p.BeginPath(); p.MoveTo(c); p.LineTo(c + new Vector2(Mathf.Cos(a) * rx * 0.75f, Mathf.Sin(a) * ry * 0.75f)); p.Stroke();
                }
                if (!bloom.HasValue) return;
                var bc = c + new Vector2(-rx * 0.25f, -ry * 0.2f);
                for (var i = 0; i < 5; i++)
                {
                    var a = (i * 72f - 90f) * Mathf.Deg2Rad;
                    Oval(p, bc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rx * 0.2f, rx * 0.2f, ry * 0.26f, bloom.Value, Lighter(bloom.Value, 0.5f), 1.5f);
                }
                Disc(p, bc, rx * 0.12f, Hex(0xFFD166));
            }, "gb-mgf-lily");
            Size(e, w, w * 0.62f);
            return e;
        }

        private static void EllipsePathFrom(Painter2D p, Vector2 c, float rx, float ry, float fromDeg, float toDeg)
        {
            const int N = 24;
            for (var i = 0; i <= N; i++)
            {
                var a = Mathf.Lerp(fromDeg, toDeg, i / (float)N) * Mathf.Deg2Rad;
                p.LineTo(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
            }
        }

        /// <summary>A painted triangle roof / flag filling the box (apex top centre) with a chunky rim.</summary>
        public static VisualElement Roof(VisualElement parent, Color c, Color rim)
        {
            return Painted(parent, (p, r) =>
            {
                p.fillColor = c;
                p.BeginPath(); p.MoveTo(new Vector2(r.width * 0.5f, 2f)); p.LineTo(new Vector2(r.width - 2f, r.height - 2f)); p.LineTo(new Vector2(2f, r.height - 2f)); p.ClosePath(); p.Fill();
                p.strokeColor = rim; p.lineWidth = 3f; p.Stroke();
            }, "gb-mgf-roof");
        }

        /// <summary>A rounded tombstone (w × h) with a soft shine, an engraved cross or plaque and moss at its foot.</summary>
        public static VisualElement Tombstone(VisualElement parent, float w, float h, Color stone, Color rim, bool cross)
        {
            var g = Group(parent, "gb-mgf-tomb");
            Size(g, w, h);
            var body = Shape(g, stone, 0f, "gb-mgf-tomb-body");
            Ux.Abs(body, 0, 0, 0, 0);
            RoundTop(body, w * 0.5f); RoundBottom(body, 3f);
            Outline(body, 3f, rim);
            var shine = Shape(g, Lighter(stone, 0.35f), 3f);
            shine.style.left = w * 0.18f; shine.style.top = h * 0.2f; Size(shine, 4f, h * 0.45f);
            if (cross)
            {
                var v = Shape(g, rim, 1.5f); v.style.left = w * 0.5f - 2f; v.style.top = h * 0.2f; Size(v, 4f, h * 0.38f);
                var hb = Shape(g, rim, 1.5f); hb.style.left = w * 0.5f - w * 0.17f; hb.style.top = h * 0.3f; Size(hb, w * 0.34f, 4f);
            }
            else
            {
                var plaque = Shape(g, Darker(stone, 0.1f), 3f); plaque.style.left = w * 0.25f; plaque.style.top = h * 0.32f; Size(plaque, w * 0.5f, h * 0.22f);
                Outline(plaque, 1.5f, rim);
            }
            var moss = Shape(g, Hex(0x7FB58A), 5f);
            moss.style.left = -4f; moss.style.right = -4f; moss.style.bottom = -3f; moss.style.height = 9f;
            return g;
        }

        /// <summary>A little hanging lantern with a warm halo (s ≈ body width); the caller places the group.</summary>
        public static VisualElement Lantern(VisualElement parent, float s, Color glass, Color frame, Color glow)
        {
            var g = Group(parent, "gb-mgf-lantern");
            Size(g, s * 2.4f, s * 2.6f);
            var halo = Shape(g, glow, s * 1.2f, "gb-mgf-lantern-halo");
            halo.style.left = 0; halo.style.top = s * 0.2f; Size(halo, s * 2.4f, s * 2.4f);
            var cap = Shape(g, frame, 0f); cap.style.left = s * 0.75f; cap.style.top = s * 0.45f; Size(cap, s * 0.9f, s * 0.35f); RoundTop(cap, s * 0.3f);
            var body = Shape(g, glass, s * 0.28f); body.style.left = s * 0.7f; body.style.top = s * 0.78f; Size(body, s, s * 1.1f);
            Outline(body, 2.5f, frame);
            var foot = Shape(g, frame, 2f); foot.style.left = s * 0.78f; foot.style.top = s * 1.86f; Size(foot, s * 0.84f, s * 0.2f);
            return g;
        }

        /// <summary>A small house (w × h): rounded wall, painted roof, door, a cross window and a chimney.</summary>
        public static VisualElement House(VisualElement parent, float w, float h, Color wall, Color roof, Color trim)
        {
            var g = Group(parent, "gb-mgf-house");
            Size(g, w, h);
            var chimney = Shape(g, Darker(roof, 0.1f), 3f); chimney.style.left = w * 0.66f; chimney.style.top = h * 0.06f; Size(chimney, w * 0.12f, h * 0.24f);
            Outline(chimney, 2f, Darker(roof, 0.28f));
            var body = Shape(g, wall, 6f, "gb-mgf-house-wall");
            body.style.left = w * 0.08f; body.style.right = w * 0.08f; body.style.bottom = 0; body.style.top = h * 0.4f;
            Outline(body, 3f, Darker(wall, 0.22f));
            var r = Roof(g, roof, Darker(roof, 0.25f));
            r.style.left = -w * 0.02f; r.style.right = -w * 0.02f; r.style.top = h * 0.08f; r.style.height = h * 0.4f;
            var door = Shape(g, trim, 0f); door.style.left = w * 0.42f; door.style.bottom = 0; Size(door, w * 0.18f, h * 0.3f); RoundTop(door, w * 0.09f);
            Outline(door, 2f, Darker(trim, 0.25f));
            var win = Shape(g, Hex(0xCFE6FF), 3f); win.style.left = w * 0.17f; win.style.top = h * 0.52f; Size(win, w * 0.18f, h * 0.18f);
            Outline(win, 2.5f, Color.white);
            var bar = Shape(win, Color.white, 0f); bar.style.left = Length.Percent(44); bar.style.top = 0; bar.style.bottom = 0; bar.style.width = 2.5f;
            return g;
        }
    }
}
