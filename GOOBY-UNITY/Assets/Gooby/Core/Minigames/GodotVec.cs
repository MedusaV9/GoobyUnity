using System;

namespace Gooby.Core.Minigames
{
    /// <summary>
    /// Godot's <c>Vector2</c> as the GDScript logic sees it (GOB NOM, ranch herding): two float32 components, every
    /// operation rounded to float32 (a GDScript float — a double — is narrowed before it meets a vector). Solution plans
    /// and bot certifications are timed to the tick, so the simulations must move exactly like the reference.
    /// </summary>
    public readonly struct V2 : IEquatable<V2>
    {
        public readonly float X, Y;

        public V2(float x, float y) { X = x; Y = y; }
        public V2(double x, double y) { X = (float)x; Y = (float)y; }

        public static readonly V2 Zero = new V2(0f, 0f);

        public static V2 operator +(V2 a, V2 b) => new V2(a.X + b.X, a.Y + b.Y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.X - b.X, a.Y - b.Y);
        public static V2 operator -(V2 a) => new V2(-a.X, -a.Y);
        public static V2 operator *(V2 a, float s) => new V2(a.X * s, a.Y * s);
        public static V2 operator *(V2 a, double s) => a * (float)s;
        public static V2 operator /(V2 a, float s) => new V2(a.X / s, a.Y / s);
        public static V2 operator /(V2 a, double s) => a / (float)s;

        public float LengthSquared => X * X + Y * Y;
        public float Length => MathF.Sqrt(X * X + Y * Y);
        public float Dot(V2 o) => X * o.X + Y * o.Y;
        public float Cross(V2 o) => X * o.Y - Y * o.X;

        public float DistanceTo(V2 o)
        {
            var dx = X - o.X;
            var dy = Y - o.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public V2 Normalized()
        {
            var l = X * X + Y * Y;
            if (l == 0f) return this;
            l = MathF.Sqrt(l);
            return new V2(X / l, Y / l);
        }

        public V2 LimitLength(float len)
        {
            var l = Length;
            if (!(l > 0f && len < l)) return this;
            var v = new V2(X / l, Y / l);
            return new V2(v.X * len, v.Y * len);
        }

        public V2 Lerp(V2 to, float w) => new V2(X + (to.X - X) * w, Y + (to.Y - Y) * w);

        /// <summary>Godot move_toward: step toward a target by at most delta, landing on it when closer.</summary>
        public V2 MoveToward(V2 to, float delta)
        {
            var vd = to - this;
            var len = vd.Length;
            return len <= delta || len < GodotMath.CmpEpsilon ? to : this + vd / len * delta;
        }

        public V2 Clamp(V2 min, V2 max) => new V2(X < min.X ? min.X : X > max.X ? max.X : X, Y < min.Y ? min.Y : Y > max.Y ? max.Y : Y);

        public bool IsEqualApprox(V2 o) => GodotMath.IsEqualApprox(X, o.X) && GodotMath.IsEqualApprox(Y, o.Y);

        public bool IsFinite => !float.IsNaN(X) && !float.IsInfinity(X) && !float.IsNaN(Y) && !float.IsInfinity(Y);

        public bool Equals(V2 o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is V2 o && Equals(o);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => "(" + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", " + Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Godot core helpers the simulation relies on (Math::is_equal_approx, Rect2, Geometry2D).</summary>
    public static class GodotMath
    {
        public const float CmpEpsilon = 0.00001f;

        public static bool IsEqualApprox(float a, float b)
        {
            if (a == b) return true;
            var tolerance = CmpEpsilon * Math.Abs(a);
            if (tolerance < CmpEpsilon) tolerance = CmpEpsilon;
            return Math.Abs(a - b) < tolerance;
        }

        /// <summary>Rect2(x, y, w, h).has_point — float32, right/bottom edges exclusive.</summary>
        public static bool RectHasPoint(float x, float y, float w, float h, V2 p)
        {
            if (p.X < x) return false;
            if (p.Y < y) return false;
            if (p.X >= x + w) return false;
            if (p.Y >= y + h) return false;
            return true;
        }

        /// <summary>Geometry2D.segment_intersects_segment (Godot 4) — true when segment a crosses segment b.</summary>
        public static bool SegmentsIntersect(V2 fromA, V2 toA, V2 fromB, V2 toB)
        {
            var b = toA - fromA;
            var c = fromB - fromA;
            var d = toB - fromA;
            var abLen = b.Dot(b);
            if (abLen <= 0f) return false;
            var bn = b / abLen;
            c = new V2(c.X * bn.X + c.Y * bn.Y, c.Y * bn.X - c.X * bn.Y);
            d = new V2(d.X * bn.X + d.Y * bn.Y, d.Y * bn.X - d.X * bn.Y);
            if ((c.Y < -CmpEpsilon && d.Y < -CmpEpsilon) || (c.Y > CmpEpsilon && d.Y > CmpEpsilon)) return false;
            if (IsEqualApprox(c.Y, d.Y)) return false;
            var abPos = d.X + (c.X - d.X) * d.Y / (d.Y - c.Y);
            return !(abPos < 0f || abPos > 1f);
        }
    }
}
