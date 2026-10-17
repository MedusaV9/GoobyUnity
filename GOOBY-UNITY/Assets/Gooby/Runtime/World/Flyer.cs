using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// A small flyer from the scenery models (bird_body_* / bird_wing_*, butterfly_body / butterfly_wing_*): the body and
    /// two wings on pivots at its shoulders, the right one mirrored (the wing models reach out along −X). Garden and ranch
    /// birds and butterflies; the owner moves <see cref="Root"/> and beats the wings with <see cref="Flap"/>.
    /// </summary>
    public sealed class Flyer
    {
        public Transform Root, WingL, WingR;
        public float Radius, Height, Speed, Phase, A, B;
        public Vector3 Anchor;

        public static Flyer Make(Transform parent, string body, string wing, float scale, Vector3 shoulder)
        {
            var root = new GameObject("Flyer:" + body).transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;
            Scenery.Place(root, body, Vector3.zero, 0f);
            var f = new Flyer { Root = root };
            f.WingL = new GameObject("WingL").transform;
            f.WingL.SetParent(root, false);
            f.WingL.localPosition = new Vector3(-shoulder.x, shoulder.y, shoulder.z);
            Scenery.Place(f.WingL, wing, Vector3.zero, 0f);
            f.WingR = new GameObject("WingR").transform;
            f.WingR.SetParent(root, false);
            f.WingR.localPosition = shoulder;
            Scenery.Place(f.WingR, wing, Vector3.zero, 0f, new Vector3(-1f, 1f, 1f));
            return f;
        }

        public static Flyer Bird(Transform parent, bool slate, float scale) =>
            Make(parent, slate ? "bird_body_slate" : "bird_body_brown", slate ? "bird_wing_slate" : "bird_wing_brown", scale, new Vector3(0.05f, 0.03f, 0f));

        public static Flyer Butterfly(Transform parent, int colour, float scale) =>
            Make(parent, "butterfly_body", colour % 3 == 0 ? "butterfly_wing_rose" : colour % 3 == 1 ? "butterfly_wing_yellow" : "butterfly_wing_lilac", scale, new Vector3(0.006f, 0.004f, 0f));

        /// <summary>Both wings by <paramref name="angle"/> (radians, positive = down).</summary>
        public void Flap(float angle)
        {
            WingL.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
            WingR.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
        }
    }
}
