using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;
using UnityEngine.UIElements;
using P = Gooby.Unity.UI.Screens.MgPaint;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>The camera of a top-down riding view: world (x, z) → panel pixels, metres → pixels, headings → screen angles.</summary>
    public interface IRideCam
    {
        Vector2 Px(V2 world);
        float M(double metres);
        float ScreenAngle(double heading);
        bool Near(V2 world, double margin);
        double Anim { get; }
    }

    /// <summary>
    /// Horses, riders and Gooby from above — shared by the competition stage (<see cref="RcompRideView"/>) and the ranch
    /// multiplayer meadow (<see cref="RmpRideView"/>), so every rider looks the same whoever draws it.
    /// </summary>
    public static class RidePaint
    {
        public static readonly Color Outline = MgScenery.Hex(0x4A3B36);
        public static readonly Color Cream = MgScenery.Hex(0xF9EDD6);
        public static readonly Color Shadow = new Color(0.16f, 0.12f, 0.08f, 0.18f);

        /// <summary>A horse seen from above (body, neck, head, mane, tail, legs) with Gooby in the saddle when <paramref name="rider"/>.</summary>
        public static void Horse(Painter2D p, IRideCam cam, V2 pos, double heading, double y, (Color hell, Color dunkel) fell, string gait, float alpha, bool rider)
        {
            if (!cam.Near(pos, 4.0)) return;
            var art = 1.55f * (1f + (float)y * 0.38f);
            var c = cam.Px(pos);
            var ang = cam.ScreenAngle(heading);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var side = new Vector2(-dir.y, dir.x);
            float A(double m) => cam.M(m) * art;
            var hz = (float)RideFeel.SchrittHz(gait ?? "stand");
            var phase = (float)cam.Anim * hz * Mathf.PI * 2f;
            var bob = gait == "stand" ? 0f : Mathf.Sin(phase * 2f) * A(0.03);
            var shadowOff = new Vector2(cam.M(0.25), cam.M(0.35)) * (1f + (float)y * 1.6f);
            var outline = new Color(Outline.r, Outline.g, Outline.b, alpha);
            Color Fa(Color col) => new Color(col.r, col.g, col.b, col.a * alpha);
            P.RotBlob(p, c + shadowOff, A(0.95), A(0.5), ang, new Color(Shadow.r, Shadow.g, Shadow.b, Shadow.a * alpha));
            var stride = gait == "stand" ? 0f : A(0.16);
            for (var i = 0; i < 4; i++)
            {
                var vorne = i < 2;
                var sd = i % 2 == 0 ? 1f : -1f;
                var swing = Mathf.Sin(phase + (vorne ? 0f : Mathf.PI) + (sd > 0 ? 0f : Mathf.PI * 0.5f)) * stride;
                var leg = c + dir * ((vorne ? A(0.55) : -A(0.5)) + swing) + side * (sd * A(0.32));
                P.RotBlob(p, leg, A(0.13), A(0.09), ang, Fa(fell.dunkel));
            }
            P.Line(p, c - dir * A(0.85), c - dir * A(1.25) + side * (Mathf.Sin(phase) * A(0.12)), A(0.16), Fa(fell.dunkel));
            P.RotBlob(p, c, A(0.95) + A(0.05), A(0.44) + A(0.05), ang, outline);
            P.RotBlob(p, c, A(0.95), A(0.44), ang, Fa(fell.hell));
            var kopf = c + dir * (A(1.05) + bob);
            P.RotBlob(p, c + dir * A(0.72), A(0.3), A(0.2), ang, Fa(fell.hell));
            P.RotBlob(p, kopf, A(0.36) + A(0.04), A(0.21) + A(0.04), ang, outline);
            P.RotBlob(p, kopf, A(0.36), A(0.21), ang, Fa(fell.hell));
            P.RotBlob(p, kopf + dir * A(0.2), A(0.15), A(0.15), ang, Fa(Color.Lerp(fell.hell, fell.dunkel, 0.35f)));
            foreach (var sd in new[] { -1f, 1f }) P.RotBlob(p, kopf - dir * A(0.16) + side * (sd * A(0.13)), A(0.08), A(0.05), ang + sd * 0.5f, Fa(fell.dunkel));
            P.Line(p, c + dir * A(0.9), c + dir * A(0.3), A(0.13), Fa(fell.dunkel));
            if (!rider) return;
            P.RotBlob(p, c - dir * A(0.05), A(0.32), A(0.36), ang, Fa(MgScenery.Hex(0xE8676B)));
            Gooby(p, c - dir * A(0.12) - new Vector2(0f, bob), ang, A(1.0), 0f);
        }

        /// <summary>A bot rider from above: jacket in the rider's colour and a round head.</summary>
        public static void Rider(Painter2D p, IRideCam cam, V2 pos, double heading, Color jacke)
        {
            if (!cam.Near(pos, 4.0)) return;
            var c = cam.Px(pos);
            var ang = cam.ScreenAngle(heading);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var art = 1.55f;
            P.RotBlob(p, c - dir * (cam.M(0.1) * art), cam.M(0.32) * art, cam.M(0.38) * art, ang, Outline);
            P.RotBlob(p, c - dir * (cam.M(0.1) * art), cam.M(0.28) * art, cam.M(0.34) * art, ang, jacke);
            P.Circle(p, c - dir * (cam.M(0.12) * art), cam.M(0.2) * art, Outline);
            P.Circle(p, c - dir * (cam.M(0.12) * art), cam.M(0.17) * art, MgScenery.Hex(0xF2D3B3));
            P.Circle(p, c - dir * (cam.M(0.2) * art), cam.M(0.12) * art, MgScenery.Hex(0x6B4A2F));
        }

        /// <summary>Gooby from above: cream head, lop ears, the green sprout, two dots of eyes toward <paramref name="ang"/>.</summary>
        public static void Gooby(Painter2D p, Vector2 g, float ang, float scale, float wobble)
        {
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var side = new Vector2(-dir.y, dir.x);
            var s = scale;
            foreach (var sd in new[] { -1f, 1f })
            {
                P.RotBlob(p, g + side * (sd * 0.27f * s) - dir * (0.05f * s), 0.11f * s + 0.03f * s, 0.21f * s + 0.03f * s, ang + Mathf.PI * 0.5f + sd * (0.35f + wobble), Outline);
                P.RotBlob(p, g + side * (sd * 0.27f * s) - dir * (0.05f * s), 0.11f * s, 0.21f * s, ang + Mathf.PI * 0.5f + sd * (0.35f + wobble), Cream);
            }
            P.Circle(p, g, 0.31f * s, Outline);
            P.Circle(p, g, 0.27f * s, Cream);
            P.Circle(p, g + dir * (0.13f * s) + side * (0.09f * s), 0.04f * s, Outline);
            P.Circle(p, g + dir * (0.13f * s) - side * (0.09f * s), 0.04f * s, Outline);
            P.RotBlob(p, g - dir * (0.24f * s), 0.09f * s, 0.05f * s, ang, MgScenery.Hex(0x8ED081));
        }
    }
}
