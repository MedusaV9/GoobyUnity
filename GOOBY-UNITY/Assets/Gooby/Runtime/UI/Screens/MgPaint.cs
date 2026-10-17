using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>Painter2D primitives shared by the painted minigame fields (filled circles, rotated blobs, polygons, strokes).</summary>
    public static class MgPaint
    {
        public static void Circle(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.Arc(c, Mathf.Max(0.3f, r), Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        public static void Ring(Painter2D p, Vector2 c, float r, float width, Color color)
        {
            p.strokeColor = color;
            p.lineWidth = Mathf.Max(0.6f, width);
            p.BeginPath();
            p.Arc(c, Mathf.Max(0.3f, r), Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        public static void Blob(Painter2D p, Vector2 c, float rx, float ry, Color color) => RotBlob(p, c, rx, ry, 0f, color);

        public static void RotBlob(Painter2D p, Vector2 c, float rx, float ry, float angle, Color color)
        {
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);
            p.fillColor = color;
            p.BeginPath();
            for (var i = 0; i < 20; i++)
            {
                var a = Mathf.PI * 2f * i / 20f;
                var x = Mathf.Cos(a) * rx;
                var y = Mathf.Sin(a) * ry;
                var pt = c + new Vector2(x * cos - y * sin, x * sin + y * cos);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        /// <summary>A blob with a dark outline (the sticker look of the minigames).</summary>
        public static void OutlinedBlob(Painter2D p, Vector2 c, float rx, float ry, float outline, Color fill, Color edge)
        {
            Blob(p, c, rx + outline, ry + outline, edge);
            Blob(p, c, rx, ry, fill);
        }

        public static void Rect(Painter2D p, Vector2 a, Vector2 b, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(a); p.LineTo(new Vector2(b.x, a.y)); p.LineTo(b); p.LineTo(new Vector2(a.x, b.y));
            p.ClosePath();
            p.Fill();
        }

        public static void RoundRect(Painter2D p, Vector2 a, Vector2 b, float r, Color color)
        {
            r = Mathf.Min(r, Mathf.Abs(b.x - a.x) * 0.5f, Mathf.Abs(b.y - a.y) * 0.5f);
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(new Vector2(a.x + r, a.y));
            p.LineTo(new Vector2(b.x - r, a.y));
            p.ArcTo(new Vector2(b.x, a.y), new Vector2(b.x, a.y + r), r);
            p.LineTo(new Vector2(b.x, b.y - r));
            p.ArcTo(new Vector2(b.x, b.y), new Vector2(b.x - r, b.y), r);
            p.LineTo(new Vector2(a.x + r, b.y));
            p.ArcTo(new Vector2(a.x, b.y), new Vector2(a.x, b.y - r), r);
            p.LineTo(new Vector2(a.x, a.y + r));
            p.ArcTo(new Vector2(a.x, a.y), new Vector2(a.x + r, a.y), r);
            p.ClosePath();
            p.Fill();
        }

        public static void Poly(Painter2D p, Vector2[] pts, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (var i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }

        public static void Line(Painter2D p, Vector2 a, Vector2 b, float width, Color color)
        {
            p.strokeColor = color;
            p.lineWidth = Mathf.Max(0.6f, width);
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        public static void Arc(Painter2D p, Vector2 c, float r, float fromDeg, float toDeg, float width, Color color)
        {
            p.strokeColor = color;
            p.lineWidth = Mathf.Max(0.6f, width);
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(c, Mathf.Max(0.3f, r), Angle.Degrees(fromDeg), Angle.Degrees(toDeg));
            p.Stroke();
        }

        public static void Star(Painter2D p, Vector2 c, float r, Color color, float inner = 0.45f)
        {
            var pts = new Vector2[10];
            for (var i = 0; i < 10; i++)
            {
                var a = -Mathf.PI / 2f + Mathf.PI * 2f * i / 10f;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? r : r * inner);
            }
            Poly(p, pts, color);
        }

        /// <summary>A canvas element that paints through a callback (pointer-transparent).</summary>
        public sealed class Canvas : VisualElement
        {
            public Action<Painter2D> Paint;

            public Canvas()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += ctx => Paint?.Invoke(ctx.painter2D);
            }
        }
    }
}
