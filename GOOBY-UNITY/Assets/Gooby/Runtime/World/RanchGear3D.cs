using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The horse's gear in 3D (port of gear_meshes.gd + ranch_pferd.gd equip, shaped to pferd.glb): a western saddle
    /// with horn, cantle, flaps, stirrups and girth; a saddle pad draped down the barrel with a cream hem; a halter with
    /// noseband, cheek straps, brass rings and crown piece — in the five shop colours, plus the quest pieces: the potato
    /// sack (burlap, a printed potato on each flap, rope girth, wooden stirrups, a potato for a horn) and the pink pad
    /// with bows. And the fluffy look: forelock, mane puffs, a tail pompom and feathered fetlocks.
    /// Everything is built in the horse mesh's own space (nose at −z, back at y ≈ 1.42, barrel ≈ an ellipse round
    /// (0, 0.945) with half-axes 0.49 × 0.475; eyes at y 1.6–1.8, cheeks out to |x| 0.36) under anchors that ride the
    /// bones, then folded into one mesh per material.
    /// </summary>
    public static class RanchGear3D
    {
        private static readonly Vector2 Rumpf = new Vector2(0f, 0.945f);
        private const float RumpfA = 0.49f, RumpfB = 0.475f;
        private static readonly Color Creme = ArtLibrary.Hex("#FFF4E2"), Metall = ArtLibrary.Hex("#C9CED6"), Messing = ArtLibrary.Hex("#E8C25A"),
            Leder = ArtLibrary.Hex("#7A5236"), Jute = ArtLibrary.Hex("#C9A46A"), JuteDunkel = ArtLibrary.Hex("#8E6B3E"), Seil = ArtLibrary.Hex("#B08850"),
            Knolle = ArtLibrary.Hex("#D9B26F"), Holz = ArtLibrary.Hex("#B58A5F"), Rose = ArtLibrary.Hex("#E0507A"), Weiss = ArtLibrary.Hex("#FFFFFF");

        /// <summary>The chip and leather colour of a gear colour id (the shop five and the quest pieces).</summary>
        public static string Hex(string farbe)
        {
            switch (farbe)
            {
                case "rot": return "#E8574C";
                case "blau": return "#5C8CC7";
                case "gruen": return "#6FB05A";
                case "lila": return "#A98BD8";
                case "gold": return "#F2C230";
                case "kartoffel": return "#C9A46A";
                case "schleife": return "#F4A3C0";
                default: return "#B58A5F";
            }
        }

        private static Material M(Color c, float glanz = 0.22f) => ArtLibrary.Solid(c, glanz);

        private static Color Dunkel(Color c, float f = 0.72f) => new Color(c.r * f, c.g * f, c.b * f, 1f);

        // ── the saddle ──

        /// <summary>The saddle on the back (body anchor); <paramref name="mitDecke"/> = a pad lies under it already.</summary>
        public static void Sattel(Transform t, string farbe, bool mitDecke)
        {
            var kartoffel = farbe == "kartoffel";
            var basis = kartoffel ? Jute : ArtLibrary.Hex(Hex(farbe));
            var glanz = farbe == "gold" ? 0.55f : kartoffel ? 0.05f : 0.32f;
            var leder = M(basis, glanz);
            var rand = M(kartoffel ? JuteDunkel : Dunkel(basis), glanz);
            if (!mitDecke)
            {
                var vlies = K(t, new Vector3(0f, 1.425f, 0.08f), M(Creme, 0.05f));
                vlies.transform.localScale = new Vector3(0.62f, 0.06f, 0.78f);
            }
            var sitz = K(t, new Vector3(0f, 1.47f, 0.08f), leder);
            sitz.transform.localScale = new Vector3(0.5f, 0.13f, 0.64f);
            var hinten = K(t, new Vector3(0f, 1.53f, 0.36f), rand);
            hinten.transform.localScale = new Vector3(0.4f, 0.17f, 0.12f);
            var vorn = K(t, new Vector3(0f, 1.53f, -0.2f), rand);
            vorn.transform.localScale = new Vector3(0.24f, 0.15f, 0.14f);
            if (kartoffel)
            {
                var knolle = K(t, new Vector3(0f, 1.63f, -0.22f), M(Knolle, 0.1f));
                knolle.transform.localScale = new Vector3(0.11f, 0.08f, 0.13f);
                foreach (var (x, z) in new[] { (0.03f, -0.27f), (-0.025f, -0.2f) })
                    K(t, new Vector3(x, 1.66f, z), M(JuteDunkel, 0.1f)).transform.localScale = Vector3.one * 0.018f;
            }
            else
            {
                Z(t, new Vector3(0f, 1.6f, -0.21f), 0.028f, 0.1f, rand);
                var kappe = Z(t, new Vector3(0f, 1.655f, -0.21f), 0.05f, 0.025f, farbe == "gold" ? M(Messing, 0.6f) : rand);
                kappe.transform.localRotation = Quaternion.identity;
            }
            var buegelFarbe = kartoffel ? M(Holz, 0.1f) : farbe == "gold" ? M(Messing, 0.6f) : M(Metall, 0.5f);
            foreach (var s in new[] { 1f, -1f })
            {
                // the flap follows the barrel outside the pad
                Bogen(t, 0.08f, 62f, 8f, 4, 0.035f, 0.32f, 0.07f, rand, s);
                if (kartoffel)
                {
                    // the printed potato on the sack flap
                    var mitte = Punkt(36f, 0.095f, s);
                    var druck = K(t, new Vector3(mitte.x, mitte.y, 0.08f), M(Knolle, 0.1f));
                    druck.transform.localScale = new Vector3(0.11f, 0.02f, 0.14f);
                    druck.transform.localRotation = Tangente(36f, s);
                }
                var unten = Punkt(8f, 0.075f, s);
                Q(t, new Vector3(unten.x + s * 0.02f, unten.y - 0.09f, 0.06f), new Vector3(0.022f, 0.19f, 0.045f), kartoffel ? M(Seil, 0.05f) : rand);
                var bx = unten.x + s * 0.03f;
                Q(t, new Vector3(bx, unten.y - 0.205f, 0.06f), new Vector3(0.045f, 0.028f, 0.15f), buegelFarbe);
                foreach (var dz in new[] { -0.06f, 0.06f }) Q(t, new Vector3(bx, unten.y - 0.165f, 0.06f + dz), new Vector3(0.03f, 0.1f, 0.026f), buegelFarbe);
            }
            // the girth round the belly, under the flaps
            Bogen(t, -0.02f, 6f, -186f, 10, 0.03f, 0.075f, 0.02f, kartoffel ? M(Seil, 0.05f) : rand);
            if (kartoffel)
                foreach (var s in new[] { 1f, -1f })
                {
                    var knoten = Punkt(-30f, 0.035f, s);
                    K(t, new Vector3(knoten.x, knoten.y, -0.02f), M(Seil, 0.05f)).transform.localScale = Vector3.one * 0.06f;
                }
            RanchBauTeile.Verschmelze(t);
        }

        // ── the saddle pad ──

        /// <summary>The saddle pad draped over the barrel (body anchor): colour, cream hem; the bow pad pink with rose bows.</summary>
        public static void Decke(Transform t, string farbe)
        {
            var schleife = farbe == "schleife";
            var stoff = M(ArtLibrary.Hex(Hex(farbe)), 0.08f);
            var saum = M(schleife ? Weiss : Creme, 0.08f);
            Bogen(t, 0.1f, 20f, 160f, 10, 0.03f, 0.76f, 0.028f, stoff);
            foreach (var z in new[] { -0.28f, 0.48f }) Bogen(t, z, 20f, 160f, 10, 0.042f, 0.045f, 0.03f, saum);
            foreach (var s in new[] { 1f, -1f })
            {
                var p = Punkt(20f, 0.034f, s);
                var hem = Q(t, new Vector3(p.x, p.y, 0.1f), new Vector3(0.045f, 0.05f, 0.8f), saum);
                hem.transform.localRotation = Tangente(20f, s);
                if (schleife) Schleife(t, s);
                else
                {
                    // a little star patch on each side
                    var stern = Punkt(40f, 0.045f, s);
                    var flicken = K(t, new Vector3(stern.x, stern.y, 0.36f), saum);
                    flicken.transform.localScale = new Vector3(0.02f, 0.1f, 0.1f);
                    flicken.transform.localRotation = Quaternion.Euler(0f, 0f, s > 0 ? 40f : 140f);
                }
            }
            RanchBauTeile.Verschmelze(t);
        }

        private static void Schleife(Transform t, float s)
        {
            var p = Punkt(30f, 0.06f, s);
            var bow = new GameObject("Schleife").transform;
            bow.SetParent(t, false);
            bow.localPosition = new Vector3(p.x, p.y, 0.38f);
            bow.localRotation = Quaternion.LookRotation(new Vector3(s * Mathf.Cos(30f * Mathf.Deg2Rad), Mathf.Sin(30f * Mathf.Deg2Rad), 0f), Vector3.up);
            var m = M(Rose, 0.3f);
            foreach (var seite in new[] { -1f, 1f })
            {
                var schlaufe = K(bow, new Vector3(seite * 0.075f, 0.02f, 0f), m);
                schlaufe.transform.localScale = new Vector3(0.13f, 0.085f, 0.045f);
                schlaufe.transform.localRotation = Quaternion.Euler(0f, 0f, seite * -18f);
                var band = Q(bow, new Vector3(seite * 0.035f, -0.075f, 0f), new Vector3(0.035f, 0.12f, 0.014f), m);
                band.transform.localRotation = Quaternion.Euler(0f, 0f, seite * 16f);
            }
            K(bow, new Vector3(0f, 0.02f, 0.006f), m).transform.localScale = new Vector3(0.055f, 0.055f, 0.05f);
            RanchBauTeile.Verschmelze(bow);
        }

        // ── the halter ──

        /// <summary>The halter on the head (head anchor, mesh space): noseband below the eyes, cheek straps outside the cheeks, crown piece behind the ears.</summary>
        public static void Halfter(Transform t, string farbe)
        {
            var leder = M(farbe == "kartoffel" ? Seil : ArtLibrary.Hex(Hex(farbe)), 0.3f);
            var ring = M(Messing, 0.6f);
            Ellipse(t, new Vector2(0f, 1.455f), 0.29f, 0.135f, -1.2f, 0f, 360f, 14, 0.032f, 0.05f, 0.012f, leder);
            Ellipse(t, new Vector2(0f, 1.6f), 0.3f, 0.27f, -0.7f, 0f, 180f, 9, 0.032f, 0.05f, 0.012f, leder);
            foreach (var s in new[] { 1f, -1f })
            {
                Strap(t, new Vector3(s * 0.3f, 1.46f, -1.19f), new Vector3(s * 0.375f, 1.47f, -1.17f), leder, Vector3.back);
                Strap(t, new Vector3(s * 0.375f, 1.47f, -1.17f), new Vector3(s * 0.312f, 1.62f, -0.72f), leder, new Vector3(s, 0f, 0f));
                K(t, new Vector3(s * 0.372f, 1.468f, -1.172f), ring).transform.localScale = Vector3.one * 0.06f;
            }
            K(t, new Vector3(0f, 1.31f, -1.2f), ring).transform.localScale = Vector3.one * 0.07f;
            RanchBauTeile.Verschmelze(t);
        }

        /// <summary>A flat strap from a to b, its broad side facing <paramref name="aussen"/>.</summary>
        private static void Strap(Transform t, Vector3 a, Vector3 b, Material m, Vector3 aussen)
        {
            var d = b - a;
            var go = Q(t, (a + b) * 0.5f, new Vector3(0.045f, 0.024f, d.magnitude + 0.02f), m);
            go.transform.localRotation = Quaternion.LookRotation(d, aussen);
        }

        // ── the fluffy look ──

        /// <summary>Forelock on the head anchor (mesh space), in the mane colour.</summary>
        public static void FlauschKopf(Transform t, Color maehne)
        {
            var m = M(maehne, 0.02f);
            foreach (var (p, r) in new[]
            {
                (new Vector3(0f, 2.0f, -1.0f), 0.09f), (new Vector3(-0.08f, 1.98f, -1.04f), 0.075f), (new Vector3(0.08f, 1.98f, -1.04f), 0.075f),
                (new Vector3(0f, 1.94f, -1.12f), 0.075f), (new Vector3(0.045f, 1.885f, -1.17f), 0.055f), (new Vector3(-0.04f, 1.9f, -1.16f), 0.05f),
            })
                K(t, p, m).transform.localScale = Vector3.one * r * 2f;
            RanchBauTeile.Verschmelze(t);
        }

        /// <summary>Puffs along the mane crest (neck anchor).</summary>
        public static void FlauschHals(Transform t, Color maehne)
        {
            var m = M(maehne, 0.02f);
            foreach (var (p, r) in new[]
            {
                (new Vector3(0.02f, 2.0f, -0.78f), 0.085f), (new Vector3(-0.025f, 2.01f, -0.65f), 0.09f), (new Vector3(0.02f, 1.94f, -0.53f), 0.08f),
                (new Vector3(-0.02f, 1.86f, -0.42f), 0.072f), (new Vector3(0.015f, 1.73f, -0.32f), 0.064f), (new Vector3(0f, 1.67f, -0.22f), 0.055f),
            })
                K(t, p, m).transform.localScale = Vector3.one * r * 2f;
            RanchBauTeile.Verschmelze(t);
        }

        /// <summary>A pompom on the tail tip (tail.02 anchor).</summary>
        public static void FlauschSchweif(Transform t, Color maehne)
        {
            var m = M(maehne, 0.02f);
            foreach (var (p, r) in new[]
            {
                (new Vector3(0f, 0.74f, 1.0f), 0.13f), (new Vector3(0.07f, 0.82f, 0.97f), 0.095f), (new Vector3(-0.07f, 0.82f, 0.97f), 0.095f),
                (new Vector3(0f, 0.66f, 1.05f), 0.1f), (new Vector3(0f, 0.86f, 1.04f), 0.08f),
            })
                K(t, p, m).transform.localScale = Vector3.one * r * 2f;
            RanchBauTeile.Verschmelze(t);
        }

        /// <summary>Feathered fetlocks just above the hoof (a leg anchor; <paramref name="mitte"/> = the leg's joint in mesh x/z).</summary>
        public static void FlauschFessel(Transform t, Vector2 mitte, Color fell)
        {
            var m = M(Color.Lerp(fell, Weiss, 0.72f), 0.02f);
            for (var i = 0; i < 7; i++)
            {
                var a = i * Mathf.PI * 2f / 7f;
                K(t, new Vector3(mitte.x + Mathf.Cos(a) * 0.12f, 0.21f + (i % 2) * 0.025f, mitte.y + Mathf.Sin(a) * 0.12f), m).transform.localScale = Vector3.one * 0.13f;
            }
            RanchBauTeile.Verschmelze(t);
        }

        // ── helpers ──

        /// <summary>A point on the barrel's ellipse at <paramref name="grad"/> (0 = the side, 90 = the top), pushed out by <paramref name="aussen"/>; s mirrors the side.</summary>
        private static Vector2 Punkt(float grad, float aussen, float s = 1f)
        {
            var a = grad * Mathf.Deg2Rad;
            return new Vector2(s * (RumpfA + aussen) * Mathf.Cos(a), Rumpf.y + (RumpfB + aussen) * Mathf.Sin(a));
        }

        /// <summary>The turn that lays a piece flat on the barrel at <paramref name="grad"/> (local x along the curve, y outward).</summary>
        private static Quaternion Tangente(float grad, float s)
        {
            var d = Punkt(grad + 1f, 0f, s) - Punkt(grad - 1f, 0f, s);
            return Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        /// <summary>A strap of boxes along the barrel's ellipse at depth <paramref name="z"/>; returns the last piece.</summary>
        private static GameObject Bogen(Transform t, float z, float von, float bis, int n, float dicke, float tiefe, float aussen, Material m, float s = 1f)
        {
            GameObject letzte = null;
            for (var i = 0; i < n; i++)
            {
                var p0 = Punkt(Mathf.Lerp(von, bis, i / (float)n), aussen, s);
                var p1 = Punkt(Mathf.Lerp(von, bis, (i + 1) / (float)n), aussen, s);
                letzte = Segment(t, p0, p1, z, dicke, tiefe, m);
            }
            return letzte;
        }

        /// <summary>A ring (or arc) of boxes round an ellipse in the x/y plane at depth <paramref name="z"/>.</summary>
        private static void Ellipse(Transform t, Vector2 mitte, float a, float b, float z, float von, float bis, int n, float dicke, float tiefe, float aussen, Material m)
        {
            for (var i = 0; i < n; i++)
            {
                var w0 = Mathf.Lerp(von, bis, i / (float)n) * Mathf.Deg2Rad;
                var w1 = Mathf.Lerp(von, bis, (i + 1) / (float)n) * Mathf.Deg2Rad;
                var p0 = new Vector2(mitte.x + (a + aussen) * Mathf.Cos(w0), mitte.y + (b + aussen) * Mathf.Sin(w0));
                var p1 = new Vector2(mitte.x + (a + aussen) * Mathf.Cos(w1), mitte.y + (b + aussen) * Mathf.Sin(w1));
                Segment(t, p0, p1, z, dicke, tiefe, m);
            }
        }

        private static GameObject Segment(Transform t, Vector2 p0, Vector2 p1, float z, float dicke, float tiefe, Material m)
        {
            var d = p1 - p0;
            var mitte = (p0 + p1) * 0.5f;
            var go = Q(t, new Vector3(mitte.x, mitte.y, z), new Vector3(d.magnitude + dicke * 0.7f, dicke, tiefe), m);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return go;
        }

        private static GameObject Q(Transform p, Vector3 c, Vector3 s, Material m)
        {
            var go = ArtLibrary.Box(p, "Q", p.position, s, m, false);
            go.transform.localPosition = c;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = s;
            return go;
        }

        private static GameObject K(Transform p, Vector3 c, Material m)
        {
            var go = ArtLibrary.Sphere(p, "K", p.position, 0.5f, m, false);
            go.transform.localPosition = c;
            go.transform.localScale = Vector3.one;
            return go;
        }

        private static GameObject Z(Transform p, Vector3 c, float radius, float hoehe, Material m)
        {
            var go = ArtLibrary.Cylinder(p, "Z", p.position, radius, hoehe, m, false);
            go.transform.localPosition = c;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(radius * 2f, hoehe * 0.5f, radius * 2f);
            return go;
        }
    }
}
