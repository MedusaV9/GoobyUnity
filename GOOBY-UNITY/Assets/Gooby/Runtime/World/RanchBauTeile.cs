using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Gooby.Core.Minigames.Ranch;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The build pieces' 3D look (port of ranch_bau_visuals.gd in the GOOBY pastels, richer): footprint-exact, centred on
    /// the footprint with the front facing −z, and the LEVEL shows — more stable doors, more hay, a self-drinker and
    /// then a pond, a heater and a dryer at the wash place, more arms on the walker, windows and a flag on the riding
    /// hall, more jumps and a timing tower in the jump course, hearts on the breeding stable, more rows, a roof and more
    /// spectators on the stand; fences turn white, get a second rail and flowers with the pasture-fence upgrade. Farm
    /// and prop models (hay bales, trough, jumps, foal, saddle, flowers, trees) fill in, fitted to their spot. The
    /// static primitives of a piece fold into one mesh per material.
    /// </summary>
    public static class RanchBauTeile
    {
        private static readonly Color SchRot = ArtLibrary.Hex("#D96C57"), HolzHell = ArtLibrary.Hex("#E8C49A"), HolzDunkel = ArtLibrary.Hex("#B58A5F"),
            Creme = ArtLibrary.Hex("#F2E9DC"), Teal = ArtLibrary.Hex("#5FA8A0"), HausCreme = ArtLibrary.Hex("#F2E3C9"), Heu = ArtLibrary.Hex("#E8C96E"),
            Wiese = new Color(0.58f, 0.74f, 0.46f), WieseSatt = new Color(0.48f, 0.7f, 0.38f), Sand = new Color(0.89f, 0.8f, 0.62f),
            Schotter = new Color(0.78f, 0.74f, 0.66f), Pflaster = new Color(0.72f, 0.66f, 0.6f), Rosa = new Color(0.93f, 0.6f, 0.7f),
            Gelb = new Color(0.95f, 0.85f, 0.45f), Metall = new Color(0.62f, 0.64f, 0.68f), Glas = new Color(0.72f, 0.86f, 0.92f),
            Erde = ArtLibrary.Hex("#8A6A4A"), Stein = ArtLibrary.Hex("#B9B4AC"), Lila = ArtLibrary.Hex("#A98BD8"), Haut = ArtLibrary.Hex("#F4D3B5");

        private static Material M(Color c, float smooth = 0.15f) => ArtLibrary.Solid(c, smooth);
        private static string Ranch(string n) => Path.Combine(AssetLoader.Root, "ranch", n + ".glb");

        // ── the pieces ──

        /// <summary>
        /// Builds a cell piece under <paramref name="root"/> (centred, front −z) at its level; the ribbon wall shows
        /// <paramref name="schleifen"/> (1 gold, 2 silver, 3 bronze, 0 the quest's bronze ribbon with its star).
        /// </summary>
        public static void Baue(Transform root, RanchBauDef def, int stufe, IReadOnlyList<int> schleifen = null)
        {
            var w = def.Footprint.X * 3f;
            var d = def.Footprint.Y * 3f;
            stufe = Mathf.Max(1, stufe);
            switch (def.Id)
            {
                case "stallboxen": Stallboxen(root, w, d, stufe); break;
                case "weide": Weide(root, w, d, stufe); break;
                case "wasserstelle": Wasserstelle(root, stufe); break;
                case "heulager": Heulager(root, w, d, stufe); break;
                case "waschplatz": Waschplatz(root, w, d, stufe); break;
                case "fuehranlage": Fuehranlage(root, w, d, stufe); break;
                case "reithalle": Reithalle(root, w, d, stufe); break;
                case "parcours": Parcours(root, w, d, stufe); break;
                case "sattelkammer": Sattelkammer(root, w, d, stufe); break;
                case "zuchtstall": Zuchtstall(root, w, d, stufe); break;
                case "fohlenweide": Fohlenweide(root, w, d); break;
                case "tribuene": Tribuene(root, w, d, stufe); break;
                case "beet_blumen": Beet(root, false); break;
                case "beet_kraeuter": Beet(root, true); break;
                case "bank_holz": Bank(root); break;
                case "laterne": Laterne(root); break;
                case "fahne": Fahne(root, Vector3.zero, SchRot); break;
                case "blumenkuebel": Kuebel(root); break;
                case "vogelhaus": Vogelhaus(root); break;
                case "heuballen_deko": Glb(root, Ranch("heuballen"), new Vector3(-0.3f, 0f, 0.2f), 80f, 1.9f); Glb(root, Ranch("heuballen"), new Vector3(0.9f, 0f, -0.7f), 20f, 1.2f); break;
                case "wegweiser": Wegweiser(root); break;
                case "strohpuppe": Strohpuppe(root, Vector3.zero); break;
                case "windspiel": Windspiel(root); break;
                case "deko_mistgabel_gold": GoldMistgabel(root); break;
                case "deko_schleifenwand": Schleifenwand(root, schleifen); break;
                case "girlande_ranchfest": Girlande(root); break;
                case "weg_schotter": Schotterweg(root); break;
                case "weg_pflaster": Pflasterweg(root); break;
                case "sand": Sandboden(root); break;
                case "blumenwiese": Blumenwiese(root); break;
                default: Q(root, new Vector3(0f, 0.5f, 0f), new Vector3(w * 0.9f, 1f, d * 0.9f), HolzHell); break;
            }
            Verschmelze(root);
        }

        /// <summary>A fence on an edge: 3 m along local x, centred; the pasture-fence level makes it white, doubles the rail, adds flowers.</summary>
        public static void Zaun(Transform root, int weidezaunStufe)
        {
            var holz = weidezaunStufe < 1 ? HolzDunkel : Creme;
            foreach (var s in new[] { -1f, 1f }) Q(root, new Vector3(s * 1.5f, 0.55f, 0f), new Vector3(0.18f, 1.1f, 0.18f), holz);
            Q(root, new Vector3(0f, 0.86f, 0f), new Vector3(3f, 0.13f, 0.09f), holz);
            if (weidezaunStufe >= 2) Q(root, new Vector3(0f, 0.46f, 0f), new Vector3(3f, 0.13f, 0.09f), holz);
            else Q(root, new Vector3(0f, 0.46f, 0f), new Vector3(3f, 0.08f, 0.07f), holz);
            if (weidezaunStufe >= 3)
            {
                K(root, new Vector3(-1.5f, 1.16f, 0f), 0.15f, Rosa);
                K(root, new Vector3(1.5f, 1.16f, 0f), 0.15f, Gelb);
            }
            Verschmelze(root);
        }

        private static void Stallboxen(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.05f, 0f), new Vector3(w - 0.2f, 0.1f, d - 0.2f), Sand, false);
            float bw = w - 1.2f, bd = d - 1.0f, h = 3.1f;
            Q(r, new Vector3(0f, h * 0.5f, 0f), new Vector3(bw, h, bd), HolzHell);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Q(r, new Vector3(sx * bw * 0.5f, h * 0.5f, sz * bd * 0.5f), new Vector3(0.3f, h + 0.1f, 0.3f), HolzDunkel);
            Q(r, new Vector3(0f, h - 0.05f, 0f), new Vector3(bw + 0.1f, 0.2f, bd + 0.1f), HolzDunkel);
            Dach(r, new Vector3(0f, h, 0f), w + 0.1f, 1.9f, d + 0.2f, true, Teal);
            Q(r, new Vector3(0f, h + 1.97f, 0f), new Vector3(0.3f, 0.18f, d + 0.3f), Creme);
            var zf = -bd * 0.5f - 0.04f;
            Q(r, new Vector3(0f, 1.2f, zf), new Vector3(2.1f, 2.4f, 0.1f), HolzDunkel);
            foreach (var a in new[] { 48f, -48f })
            {
                var brace = Q(r, new Vector3(0f, 1.2f, zf - 0.05f), new Vector3(0.14f, 2.9f, 0.04f), Creme);
                brace.transform.localRotation = Quaternion.Euler(0f, 0f, a);
            }
            var fenster = Z(r, new Vector3(0f, h + 0.75f, -(d + 0.2f) * 0.5f - 0.02f), 0.38f, 0.06f, Glas);
            fenster.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Q(r, new Vector3(0f, 2.72f, zf - 0.06f), new Vector3(1.5f, 0.36f, 0.06f), Creme);
            // box doors on the long sides: two per level (2/4/6/8 places)
            var tore = 2 * Mathf.Clamp(stufe, 1, 4);
            for (var i = 0; i < tore; i++)
            {
                var sx = i < 4 ? -1f : 1f;
                var k = i % 4;
                var z = -bd * 0.5f + 1.2f + k * (bd - 2.4f) / 3f;
                var x = sx * (bw * 0.5f + 0.05f);
                Q(r, new Vector3(x, 1.0f, z), new Vector3(0.1f, 1.9f, 1.25f), HolzDunkel);
                Q(r, new Vector3(x + sx * 0.03f, 1.45f, z), new Vector3(0.06f, 0.1f, 1.25f), Creme);
                K(r, new Vector3(x + sx * 0.05f, 1.75f, z + 0.35f), 0.07f, Metall);
            }
            if (stufe >= 4)
            {
                Q(r, new Vector3(0f, h + 2.4f, 0f), new Vector3(0.07f, 0.9f, 0.07f), Metall);
                Q(r, new Vector3(0f, h + 2.7f, 0f), new Vector3(0.07f, 0.07f, 1.0f), Metall);
                K(r, new Vector3(0f, h + 2.05f, 0f), 0.14f, Gelb);
            }
        }

        private static void Weide(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.06f, 0f), new Vector3(w - 0.2f, 0.12f, d - 0.2f), stufe >= 2 ? WieseSatt : Wiese, false);
            var rng = new System.Random(77);
            // soft tufts of grass, lusher from level 2
            for (var i = 0; i < (stufe >= 2 ? 16 : 9); i++)
            {
                var p = new Vector3((float)(rng.NextDouble() - 0.5) * (w - 2f), 0.16f, (float)(rng.NextDouble() - 0.5) * (d - 2f));
                var tuff = K(r, p, 0.32f, i % 3 == 0 ? Wiese : ArtLibrary.Hex(stufe >= 2 ? "#6FB552" : "#86C46A"));
                tuff.transform.localScale = new Vector3(0.75f, 0.32f, 0.6f);
            }
            Glb(r, Ranch("tree_oak"), new Vector3(-w * 0.5f + 2.2f, 0.1f, d * 0.5f - 2.2f), 30f, 4.6f, "#7DBE63", "leaf");
            // a salt lick on its post
            Q(r, new Vector3(w * 0.5f - 1.4f, 0.45f, -d * 0.5f + 1.6f), new Vector3(0.14f, 0.7f, 0.14f), HolzDunkel);
            Q(r, new Vector3(w * 0.5f - 1.4f, 0.86f, -d * 0.5f + 1.6f), new Vector3(0.36f, 0.22f, 0.36f), Stein);
            if (stufe >= 3)
            {
                var blumen = new[] { "flower_redA", "flower_yellowA", "flower_purpleA" };
                for (var i = 0; i < 10; i++)
                    Glb(r, Ranch(blumen[i % 3]), new Vector3((float)(rng.NextDouble() - 0.5) * (w - 2f), 0.1f, (float)(rng.NextDouble() - 0.5) * (d - 2f)), i * 37f, 0.9f);
                for (var i = 0; i < 6; i++)
                {
                    var p = new Vector3((float)(rng.NextDouble() - 0.5) * (w - 3f), 0.3f, (float)(rng.NextDouble() - 0.5) * (d - 3f));
                    var busch = K(r, p, 0.35f, i % 2 == 0 ? Lila : WieseSatt);
                    busch.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);
                }
            }
        }

        private static void Wasserstelle(Transform r, int stufe)
        {
            if (stufe >= 3)
            {
                Z(r, new Vector3(0f, 0.15f, 0f), 1.38f, 0.3f, Stein);
                Z(r, new Vector3(0f, 0.17f, 0f), 1.18f, 0.3f, new Color(0.45f, 0.68f, 0.86f));
                foreach (var (x, z) in new[] { (0.4f, -0.3f), (-0.5f, 0.35f) })
                {
                    var blatt = Z(r, new Vector3(x, 0.33f, z), 0.24f, 0.02f, WieseSatt);
                    blatt.transform.localScale = new Vector3(0.48f, 0.01f, 0.48f);
                }
                K(r, new Vector3(0.45f, 0.38f, -0.3f), 0.07f, Rosa);
                for (var i = 0; i < 5; i++)
                    Q(r, new Vector3(-1.05f + i * 0.08f, 0.6f, 0.9f - i * 0.12f), new Vector3(0.04f, 0.9f + (i % 2) * 0.3f, 0.04f), WieseSatt, false);
            }
            else Glb(r, Ranch("trog"), new Vector3(-0.1f, 0f, 0.1f), 0f, 2.4f);
            if (stufe >= 2)
            {
                Q(r, new Vector3(1.05f, 0.85f, -0.75f), new Vector3(0.2f, 1.7f, 0.2f), Metall);
                Q(r, new Vector3(1.05f, 1.55f, -0.45f), new Vector3(0.08f, 0.08f, 0.6f), Metall);
                Q(r, new Vector3(0.88f, 1.15f, -0.75f), new Vector3(0.3f, 0.1f, 0.1f), Metall);
                K(r, new Vector3(1.05f, 1.78f, -0.75f), 0.15f, Teal);
            }
        }

        private static void Heulager(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.05f, 0f), new Vector3(w - 0.4f, 0.1f, d - 0.4f), ArtLibrary.Hex("#D9B98A"), false);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Q(r, new Vector3(sx * (w * 0.5f - 0.5f), 1.6f, sz * (d * 0.5f - 0.5f)), new Vector3(0.3f, 3.2f, 0.3f), HolzDunkel);
            Q(r, new Vector3(0f, 3.2f, 0f), new Vector3(w - 0.6f, 0.2f, d - 0.6f), HolzDunkel);
            Dach(r, new Vector3(0f, 3.25f, 0f), w + 0.3f, 1.4f, d + 0.3f, false, SchRot);
            var ballen = 2 * Mathf.Clamp(stufe, 1, 3);
            for (var i = 0; i < ballen; i++)
            {
                var reihe = i / 3;
                var k = i % 3;
                Glb(r, Ranch("heuballen"), new Vector3(-1.4f + k * 1.4f, 0.1f + (reihe >= 1 ? 1.1f : 0f), reihe == 1 ? 0.3f : 0.6f - reihe * 0.2f), 90f, 1.5f);
            }
            if (stufe >= 3)
            {
                var rutsche = Q(r, new Vector3(w * 0.5f - 0.2f, 1.7f, -0.6f), new Vector3(0.9f, 0.1f, 3.4f), HolzHell);
                rutsche.transform.localRotation = Quaternion.Euler(-48f, 90f, 0f);
            }
            var gabel = Q(r, new Vector3(-w * 0.5f + 0.75f, 1.0f, -d * 0.5f + 0.8f), new Vector3(0.06f, 2.0f, 0.06f), HolzHell);
            gabel.transform.localRotation = Quaternion.Euler(12f, 0f, 8f);
        }

        private static void Waschplatz(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.05f, 0f), new Vector3(w - 0.3f, 0.1f, d - 0.3f), Pflaster, false);
            for (var i = 0; i < 4; i++)
                Q(r, new Vector3(-w * 0.5f + 0.8f + i * 1.5f, 0.11f, 0f), new Vector3(0.05f, 0.02f, d - 0.6f), Stein, false);
            var puddle = Z(r, new Vector3(0.2f, 0.11f, 0.1f), 1.3f, 0.02f, new Color(0.55f, 0.75f, 0.9f));
            puddle.transform.localScale = new Vector3(2.6f, 0.01f, 1.9f);
            Q(r, new Vector3(0f, 0.12f, 0.1f), new Vector3(0.35f, 0.02f, 0.35f), ArtLibrary.Hex("#5E5A55"), false);
            Q(r, new Vector3(-w * 0.5f + 0.9f, 1.25f, -d * 0.5f + 0.9f), new Vector3(0.22f, 2.5f, 0.22f), Metall);
            Q(r, new Vector3(-w * 0.5f + 1.4f, 2.4f, -d * 0.5f + 0.9f), new Vector3(1.1f, 0.12f, 0.12f), Metall);
            var schlauch = Z(r, new Vector3(-w * 0.5f + 0.9f, 0.9f, -d * 0.5f + 0.72f), 0.32f, 0.12f, ArtLibrary.Hex("#7FBF5E"));
            schlauch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Q(r, new Vector3(0f, 0.8f, d * 0.5f - 0.5f), new Vector3(0.2f, 1.6f, 0.2f), HolzDunkel);
            var ring = Z(r, new Vector3(0f, 1.3f, d * 0.5f - 0.62f), 0.15f, 0.04f, Metall);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (stufe >= 2)
            {
                Q(r, new Vector3(w * 0.5f - 0.9f, 0.75f, d * 0.5f - 0.8f), new Vector3(0.9f, 1.5f, 0.6f), SchRot);
                Z(r, new Vector3(w * 0.5f - 0.75f, 1.75f, d * 0.5f - 0.8f), 0.12f, 0.6f, Metall);
                Q(r, new Vector3(w * 0.5f - 0.9f, 1.0f, d * 0.5f - 1.12f), new Vector3(0.4f, 0.25f, 0.04f), Gelb);
            }
            if (stufe >= 3)
            {
                Q(r, new Vector3(w * 0.5f - 0.9f, 0.55f, -d * 0.5f + 0.8f), new Vector3(1.0f, 1.1f, 0.6f), Teal);
                Q(r, new Vector3(w * 0.5f - 0.9f, 0.55f, -d * 0.5f + 0.48f), new Vector3(0.04f, 0.9f, 0.04f), Creme);
                var foehn = Z(r, new Vector3(w * 0.5f - 2.1f, 1.3f, -d * 0.5f + 0.7f), 0.38f, 0.12f, Creme);
                foehn.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Q(r, new Vector3(w * 0.5f - 2.1f, 0.6f, -d * 0.5f + 0.8f), new Vector3(0.1f, 1.2f, 0.1f), Metall);
            }
        }

        private static void Fuehranlage(Transform r, float w, float d, int stufe)
        {
            var rad = Mathf.Min(w, d) * 0.45f;
            Z(r, new Vector3(0f, 0.05f, 0f), rad, 0.1f, Sand);
            Z(r, new Vector3(0f, 0.07f, 0f), rad - 1.6f, 0.1f, WieseSatt);
            const int n = 16;
            for (var i = 0; i < n; i++)
            {
                var a0 = i * Mathf.PI * 2f / n;
                var a1 = (i + 1) * Mathf.PI * 2f / n;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (rad + 0.05f);
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (rad + 0.05f);
                Q(r, p0 + Vector3.up * 0.55f, new Vector3(0.14f, 1.1f, 0.14f), Creme);
                var rail = Q(r, (p0 + p1) * 0.5f + Vector3.up * 0.85f, new Vector3(Vector3.Distance(p0, p1), 0.1f, 0.07f), Creme);
                rail.transform.localRotation = Quaternion.LookRotation(p1 - p0) * Quaternion.Euler(0f, 90f, 0f);
            }
            Z(r, new Vector3(0f, 1.8f, 0f), 0.24f, 3.6f, HolzDunkel);
            var dreh = Kind(r, "Dreh");
            dreh.localPosition = new Vector3(0f, 3.1f, 0f);
            dreh.gameObject.AddComponent<Dreher>().Grad = 24f;
            Z(dreh, Vector3.up * 0.45f, 0.42f, 0.12f, Teal);
            var arme = 2 + Mathf.Clamp(stufe, 1, 3);
            for (var i = 0; i < arme; i++)
            {
                var a = i * 360f / arme;
                var richtung = Quaternion.Euler(0f, a, 0f);
                var arm = Q(dreh, richtung * new Vector3(0f, 0f, rad * 0.5f), new Vector3(0.12f, 0.12f, rad), HolzHell);
                arm.transform.localRotation = richtung;
                Q(dreh, richtung * new Vector3(0f, -0.9f, rad - 0.3f), new Vector3(0.04f, 1.8f, 0.04f), Creme, false);
            }
        }

        private static void Reithalle(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.04f, 0f), new Vector3(w - 0.2f, 0.08f, d - 0.2f), Sand, false);
            float bw = w - 0.8f, bd = d - 0.8f, h = 5f;
            Q(r, new Vector3(0f, h * 0.5f, 0f), new Vector3(bw, h, bd), HausCreme);
            Q(r, new Vector3(0f, 0.35f, 0f), new Vector3(bw + 0.08f, 0.7f, bd + 0.08f), HolzDunkel);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Q(r, new Vector3(sx * bw * 0.5f, h * 0.5f, sz * bd * 0.5f), new Vector3(0.36f, h + 0.1f, 0.36f), HolzDunkel);
            Dach(r, new Vector3(0f, h, 0f), d + 0.3f, 2.6f, w + 0.3f, false, Teal);
            // the roof's ridge cap, dark eaves and a little cupola in the middle break up the big slopes
            Q(r, new Vector3(0f, h + 2.62f, 0f), new Vector3(w + 0.4f, 0.2f, 0.34f), Creme);
            foreach (var sz in new[] { -1f, 1f }) Q(r, new Vector3(0f, h + 0.05f, sz * (d + 0.3f) * 0.5f), new Vector3(w + 0.4f, 0.16f, 0.2f), ArtLibrary.Hex("#3F7F78"));
            Q(r, new Vector3(0f, h + 3.1f, 0f), new Vector3(1.3f, 1.0f, 1.3f), HausCreme);
            Q(r, new Vector3(0f, h + 3.1f, -0.66f), new Vector3(0.6f, 0.55f, 0.04f), Glas);
            Dach(r, new Vector3(0f, h + 3.6f, 0f), 1.7f, 0.8f, 1.7f, true, Teal);
            Q(r, new Vector3(0f, h + 4.7f, 0f), new Vector3(0.06f, 0.7f, 0.06f), Metall);
            K(r, new Vector3(0f, h + 4.45f, 0f), 0.12f, Gelb);
            var zf = -bd * 0.5f - 0.05f;
            Q(r, new Vector3(0f, 2.1f, zf), new Vector3(3.8f, 4.2f, 0.1f), HolzDunkel);
            foreach (var a in new[] { 42f, -42f })
            {
                var brace = Q(r, new Vector3(0f, 2.1f, zf - 0.05f), new Vector3(0.18f, 5.4f, 0.04f), Creme);
                brace.transform.localRotation = Quaternion.Euler(0f, 0f, a);
            }
            Q(r, new Vector3(0f, 4.55f, zf - 0.05f), new Vector3(3.0f, 0.55f, 0.06f), Creme);
            Q(r, new Vector3(0f, 4.55f, zf - 0.09f), new Vector3(2.4f, 0.14f, 0.03f), Teal);
            if (stufe >= 2)
            {
                foreach (var sx in new[] { -1f, 1f })
                {
                    Q(r, new Vector3(sx * (bw * 0.25f + 1.1f), 3.4f, zf), new Vector3(bw * 0.5f - 3.2f, 1.0f, 0.06f), Glas);
                    Q(r, new Vector3(sx * (bw * 0.5f + 0.03f), 3.4f, 0f), new Vector3(0.06f, 1.0f, bd - 1.6f), Glas);
                }
            }
            if (stufe >= 3) Fahne(r, new Vector3(-bw * 0.5f + 0.8f, h + 2.45f, 0f), SchRot);
        }

        private static void Parcours(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.05f, 0f), new Vector3(w - 0.2f, 0.1f, d - 0.2f), Sand, false);
            // the white arena rail, open at the front for the way in
            var halb = (w - 0.6f) * 0.5f;
            for (var i = 0; i <= 10; i++)
            {
                var f = -halb + i * (2f * halb / 10f);
                foreach (var (x, z) in new[] { (f, -halb), (f, halb), (-halb, f), (halb, f) })
                {
                    if (Mathf.Abs(z + halb) < 0.01f && Mathf.Abs(x) < 1.6f) continue;
                    Q(r, new Vector3(x, 0.45f, z), new Vector3(0.12f, 0.9f, 0.12f), Color.white);
                }
            }
            foreach (var (c, s) in new[]
            {
                (new Vector3(-halb * 0.5f - 0.8f, 0.75f, -halb), new Vector3(halb - 1.6f, 0.09f, 0.07f)), (new Vector3(halb * 0.5f + 0.8f, 0.75f, -halb), new Vector3(halb - 1.6f, 0.09f, 0.07f)),
                (new Vector3(0f, 0.75f, halb), new Vector3(2f * halb, 0.09f, 0.07f)), (new Vector3(-halb, 0.75f, 0f), new Vector3(0.07f, 0.09f, 2f * halb)),
                (new Vector3(halb, 0.75f, 0f), new Vector3(0.07f, 0.09f, 2f * halb)),
            })
                Q(r, c, s, Color.white);
            var spruenge = new (Vector3 p, float yaw, string modell)[]
            {
                (new Vector3(-3.6f, 0.1f, -2.4f), 0f, "hindernis_a"), (new Vector3(3.4f, 0.1f, -3.4f), 90f, "hindernis_b"), (new Vector3(3.8f, 0.1f, 3.2f), 0f, "hindernis_c"),
                (new Vector3(-3.4f, 0.1f, 3.8f), 90f, "hindernis_a"), (new Vector3(0f, 0.1f, 0.2f), 45f, "hindernis_b"),
            };
            for (var i = 0; i < 2 + Mathf.Clamp(stufe, 1, 3); i++) Glb(r, Ranch(spruenge[i].modell), spruenge[i].p, spruenge[i].yaw, 3.2f);
            if (stufe >= 3)
            {
                var t = new Vector3(halb - 1.0f, 0f, -halb + 1.2f);
                Q(r, t + Vector3.up * 1.7f, new Vector3(1.0f, 3.4f, 1.0f), Metall);
                Q(r, t + Vector3.up * 3.55f, new Vector3(1.3f, 0.3f, 1.3f), SchRot);
                var uhr = Z(r, t + new Vector3(0f, 2.7f, -0.52f), 0.32f, 0.05f, Color.white);
                uhr.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Q(r, t + new Vector3(0f, 2.78f, -0.56f), new Vector3(0.04f, 0.22f, 0.02f), ArtLibrary.Hex("#3B3630"), false);
            }
            foreach (var (x, z, c) in new[] { (-halb, -halb, SchRot), (halb, -halb, Teal), (-halb, halb, Gelb), (halb, halb, SchRot) })
            {
                Q(r, new Vector3(x, 1.0f, z), new Vector3(0.06f, 2.0f, 0.06f), Metall);
                Q(r, new Vector3(x + 0.3f, 1.8f, z), new Vector3(0.55f, 0.32f, 0.03f), c, false);
            }
        }

        private static void Sattelkammer(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.04f, 0f), new Vector3(w - 0.3f, 0.08f, d - 0.3f), ArtLibrary.Hex("#D9C6A2"), false);
            float bw = w - 1.4f, bd = d - 2.0f, h = 2.7f, cz = 0.45f;
            Q(r, new Vector3(0f, h * 0.5f, cz), new Vector3(bw, h, bd), HolzHell);
            foreach (var sx in new[] { -1f, 1f })
                Q(r, new Vector3(sx * bw * 0.5f, h * 0.5f, cz - bd * 0.5f), new Vector3(0.24f, h + 0.1f, 0.24f), HolzDunkel);
            Dach(r, new Vector3(0f, h, cz), bw + 0.6f, 1.4f, bd + 0.8f, true, Creme);
            var zf = cz - bd * 0.5f - 0.05f;
            Q(r, new Vector3(0.5f, 1.0f, zf), new Vector3(1.0f, 2.0f, 0.08f), HolzDunkel);
            K(r, new Vector3(0.85f, 1.0f, zf - 0.06f), 0.06f, Gelb);
            Q(r, new Vector3(-0.2f, 2.35f, zf - 0.04f), new Vector3(1.6f, 0.32f, 0.05f), SchRot);
            // a saddle on its rack by the door
            Q(r, new Vector3(1.75f, 0.5f, -d * 0.5f + 0.9f), new Vector3(0.12f, 1.0f, 0.12f), HolzDunkel);
            Q(r, new Vector3(1.75f, 0.98f, -d * 0.5f + 0.9f), new Vector3(0.7f, 0.1f, 0.24f), HolzDunkel);
            Glb(r, Ranch("sattel"), new Vector3(1.75f, 1.03f, -d * 0.5f + 0.9f), 90f, 0.8f);
            if (stufe >= 2)
            {
                Q(r, new Vector3(-1.0f, 1.55f, zf), new Vector3(0.85f, 0.75f, 0.06f), Glas);
                foreach (var sx in new[] { -1f, 1f }) Q(r, new Vector3(-1.0f + sx * 0.6f, 1.55f, zf - 0.02f), new Vector3(0.32f, 0.8f, 0.05f), Teal);
            }
            if (stufe >= 3) Strohpuppe(r, new Vector3(-1.9f, 0f, -d * 0.5f + 0.8f));
        }

        private static void Zuchtstall(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.04f, 0f), new Vector3(w - 0.3f, 0.08f, d - 0.3f), Sand, false);
            float bw = w - 1.2f, bd = d - 1.2f, h = 3.5f;
            Q(r, new Vector3(0f, h * 0.5f, 0f), new Vector3(bw, h, bd), SchRot);
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Q(r, new Vector3(sx * bw * 0.5f, h * 0.5f, sz * bd * 0.5f), new Vector3(0.26f, h + 0.1f, 0.26f), Creme);
            Q(r, new Vector3(0f, h - 0.05f, 0f), new Vector3(bw + 0.1f, 0.18f, bd + 0.1f), Creme);
            Dach(r, new Vector3(0f, h, 0f), bw + 1.0f, 2.4f, bd + 0.8f, true, Creme);
            var zf = -bd * 0.5f - 0.05f;
            Q(r, new Vector3(0f, 1.3f, zf), new Vector3(2.4f, 2.6f, 0.1f), SchRot);
            foreach (var (c, s) in new[]
            {
                (new Vector3(0f, 2.62f, zf - 0.04f), new Vector3(2.6f, 0.16f, 0.05f)), (new Vector3(0f, 0.02f, zf - 0.04f), new Vector3(2.6f, 0.16f, 0.05f)),
                (new Vector3(-1.25f, 1.3f, zf - 0.04f), new Vector3(0.16f, 2.6f, 0.05f)), (new Vector3(1.25f, 1.3f, zf - 0.04f), new Vector3(0.16f, 2.6f, 0.05f)),
            })
                Q(r, c, s, Creme);
            foreach (var a in new[] { 47f, -47f })
            {
                var brace = Q(r, new Vector3(0f, 1.3f, zf - 0.06f), new Vector3(0.14f, 3.4f, 0.04f), Creme);
                brace.transform.localRotation = Quaternion.Euler(0f, 0f, a);
            }
            var giebel = Z(r, new Vector3(0f, h + 0.9f, -(bd + 0.8f) * 0.5f - 0.02f), 0.42f, 0.06f, Creme);
            giebel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (var i = 0; i < Mathf.Clamp(stufe, 1, 2); i++)
                Herz(r, new Vector3((i - (stufe - 1) * 0.5f) * 1.6f, h + 2.75f, 0f), 0.42f);
        }

        private static void Fohlenweide(Transform r, float w, float d)
        {
            Q(r, new Vector3(0f, 0.06f, 0f), new Vector3(w - 0.2f, 0.12f, d - 0.2f), WieseSatt, false);
            Glb(r, Ranch("fohlen"), new Vector3(-0.6f, 0.12f, -0.3f), 210f, 1.7f);
            // a little lean-to for shade
            foreach (var sx in new[] { -1f, 1f })
            {
                Q(r, new Vector3(sx * 1.4f + 1.8f, 0.9f, d * 0.5f - 1.2f), new Vector3(0.18f, 1.8f, 0.18f), HolzDunkel);
                Q(r, new Vector3(sx * 1.4f + 1.8f, 1.3f, d * 0.5f - 2.6f), new Vector3(0.18f, 1.0f, 0.18f), HolzDunkel);
            }
            var dach = Q(r, new Vector3(1.8f, 1.65f, d * 0.5f - 1.9f), new Vector3(3.4f, 0.12f, 1.9f), SchRot);
            dach.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            var rng = new System.Random(31);
            var blumen = new[] { "flower_redA", "flower_yellowA", "flower_purpleA" };
            for (var i = 0; i < 7; i++)
                Glb(r, Ranch(blumen[i % 3]), new Vector3((float)(rng.NextDouble() - 0.5) * (w - 1.5f), 0.12f, (float)(rng.NextDouble() - 0.5) * (d - 1.5f)), i * 51f, 0.8f);
            K(r, new Vector3(1.4f, 0.4f, -1.6f), 0.28f, SchRot);
        }

        private static void Tribuene(Transform r, float w, float d, int stufe)
        {
            Q(r, new Vector3(0f, 0.04f, 0f), new Vector3(w - 0.2f, 0.08f, d - 0.2f), ArtLibrary.Hex("#D9C6A2"), false);
            var reihen = 1 + Mathf.Clamp(stufe, 1, 2);
            for (var i = 0; i < reihen; i++)
            {
                var top = 0.55f + i * 0.6f;
                var z = -d * 0.5f + 1.0f + i * 1.25f;
                Q(r, new Vector3(0f, top * 0.5f, z), new Vector3(w - 0.6f, top, 1.25f), i % 2 == 0 ? HolzHell : HolzDunkel);
                Q(r, new Vector3(0f, top + 0.04f, z - 0.25f), new Vector3(w - 0.5f, 0.08f, 0.5f), Creme);
            }
            if (stufe >= 2)
            {
                foreach (var sx in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f })
                        Q(r, new Vector3(sx * (w * 0.5f - 0.5f), 1.7f, sz * (d * 0.5f - 0.5f)), new Vector3(0.18f, 3.4f, 0.18f), HolzDunkel);
                var dach = Q(r, new Vector3(0f, 3.45f, 0.2f), new Vector3(w - 0.2f, 0.16f, d - 0.6f), Teal);
                dach.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
                for (var i = 0; i < 6; i++) Q(r, new Vector3(-w * 0.5f + 1f + i * (w - 2f) / 5f, 3.2f, -d * 0.5f + 0.5f), new Vector3(0.7f, 0.4f, 0.03f), i % 2 == 0 ? SchRot : Gelb, false);
            }
            // the spectators: little pastel people on the rows
            var farben = new[] { SchRot, Teal, Gelb, Lila, Rosa, ArtLibrary.Hex("#7FB0E0") };
            var zuschauer = stufe >= 2 ? 12 : 6;
            var rng = new System.Random(5);
            for (var i = 0; i < zuschauer; i++)
            {
                var reihe = i % reihen;
                var platz = i / reihen;
                var x = -w * 0.5f + 1.3f + platz * (w - 2.6f) / Mathf.Max(1, (zuschauer + reihen - 1) / reihen - 1) + (float)(rng.NextDouble() - 0.5) * 0.3f;
                var y = 0.55f + reihe * 0.6f + 0.08f;
                var z = -d * 0.5f + 0.95f + reihe * 1.25f;
                Q(r, new Vector3(x, y + 0.28f, z), new Vector3(0.42f, 0.56f, 0.3f), farben[i % farben.Length], false);
                K(r, new Vector3(x, y + 0.74f, z), 0.17f, Haut);
            }
        }

        // ── decoration ──

        private static void Beet(Transform r, bool kraeuter)
        {
            foreach (var (c, s) in new[]
            {
                (new Vector3(0f, 0.18f, -1.05f), new Vector3(2.3f, 0.36f, 0.16f)), (new Vector3(0f, 0.18f, 1.05f), new Vector3(2.3f, 0.36f, 0.16f)),
                (new Vector3(-1.07f, 0.18f, 0f), new Vector3(0.16f, 0.36f, 2.1f)), (new Vector3(1.07f, 0.18f, 0f), new Vector3(0.16f, 0.36f, 2.1f)),
            })
                Q(r, c, s, HolzDunkel);
            Q(r, new Vector3(0f, 0.2f, 0f), new Vector3(2.0f, 0.3f, 2.0f), Erde, false);
            if (kraeuter)
            {
                for (var i = 0; i < 6; i++)
                {
                    var busch = K(r, new Vector3(-0.6f + (i % 3) * 0.6f, 0.48f, i < 3 ? -0.45f : 0.45f), 0.3f, i % 3 == 1 ? Lila : WieseSatt);
                    busch.transform.localScale = new Vector3(0.55f, 0.45f, 0.55f);
                }
            }
            else
            {
                var blumen = new[] { "flower_redA", "flower_yellowA", "flower_purpleA", "flower_redA", "flower_yellowA" };
                for (var i = 0; i < 5; i++)
                    Glb(r, Ranch(blumen[i]), new Vector3(-0.6f + (i % 3) * 0.6f, 0.34f, i < 3 ? -0.4f : 0.4f), i * 70f, 0.75f);
            }
        }

        private static void Bank(Transform r)
        {
            Q(r, new Vector3(0f, 0.46f, 0.05f), new Vector3(1.9f, 0.12f, 0.55f), HolzHell);
            var lehne = Q(r, new Vector3(0f, 0.8f, 0.32f), new Vector3(1.9f, 0.5f, 0.1f), HolzHell);
            lehne.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            foreach (var sx in new[] { -0.75f, 0.75f })
            {
                Q(r, new Vector3(sx, 0.22f, 0.05f), new Vector3(0.14f, 0.44f, 0.48f), HolzDunkel);
                Q(r, new Vector3(sx, 0.58f, 0.05f), new Vector3(0.1f, 0.06f, 0.55f), HolzDunkel);
            }
            Z(r, new Vector3(1.25f, 0.22f, 0.2f), 0.2f, 0.44f, ArtLibrary.Hex("#D58A62"));
            K(r, new Vector3(1.25f, 0.55f, 0.2f), 0.22f, Rosa);
        }

        private static void Laterne(Transform r)
        {
            Z(r, new Vector3(0f, 0.12f, 0f), 0.28f, 0.24f, Stein);
            Q(r, new Vector3(0f, 1.2f, 0f), new Vector3(0.14f, 2.2f, 0.14f), ArtLibrary.Hex("#4B4F57"));
            Q(r, new Vector3(0f, 2.35f, 0f), new Vector3(0.46f, 0.08f, 0.46f), ArtLibrary.Hex("#4B4F57"));
            Q(r, new Vector3(0f, 2.85f, 0f), new Vector3(0.52f, 0.08f, 0.52f), ArtLibrary.Hex("#4B4F57"));
            var glas = K(r, new Vector3(0f, 2.6f, 0f), 0.22f, Gelb);
            glas.GetComponent<Renderer>().sharedMaterial = ArtLibrary.Glow(new Color(1.3f, 1.05f, 0.55f));
            K(r, new Vector3(0f, 3.0f, 0f), 0.09f, ArtLibrary.Hex("#4B4F57"));
        }

        private static void Fahne(Transform r, Vector3 fuss, Color farbe)
        {
            Z(r, fuss + new Vector3(0f, 0.1f, 0f), 0.24f, 0.2f, Stein);
            Q(r, fuss + new Vector3(0f, 1.65f, 0f), new Vector3(0.1f, 3.3f, 0.1f), HolzDunkel);
            K(r, fuss + new Vector3(0f, 3.34f, 0f), 0.1f, Gelb);
            var tuch = Kind(r, "Fahne");
            tuch.localPosition = fuss + new Vector3(0.05f, 2.95f, 0f);
            tuch.gameObject.AddComponent<Wedler>();
            Q(tuch, new Vector3(0.55f, 0f, 0f), new Vector3(1.05f, 0.62f, 0.04f), farbe, false);
            Q(tuch, new Vector3(0.55f, 0f, -0.025f), new Vector3(0.5f, 0.14f, 0.01f), Creme, false);
        }

        private static void Kuebel(Transform r)
        {
            Z(r, new Vector3(0f, 0.32f, 0f), 0.46f, 0.64f, ArtLibrary.Hex("#D58A62"));
            Z(r, new Vector3(0f, 0.66f, 0f), 0.5f, 0.08f, ArtLibrary.Hex("#C77852"));
            Z(r, new Vector3(0f, 0.68f, 0f), 0.42f, 0.04f, Erde);
            Glb(r, Ranch("flower_redA"), new Vector3(-0.15f, 0.68f, 0f), 0f, 0.7f);
            Glb(r, Ranch("flower_yellowA"), new Vector3(0.18f, 0.68f, 0.1f), 90f, 0.6f);
            Glb(r, Ranch("flower_purpleA"), new Vector3(0f, 0.68f, -0.2f), 200f, 0.6f);
        }

        private static void Vogelhaus(Transform r)
        {
            Q(r, new Vector3(0f, 0.9f, 0f), new Vector3(0.12f, 1.8f, 0.12f), HolzDunkel);
            Q(r, new Vector3(0f, 1.95f, 0f), new Vector3(0.58f, 0.5f, 0.5f), HolzHell);
            Dach(r, new Vector3(0f, 2.2f, 0f), 0.78f, 0.36f, 0.66f, true, Teal);
            var loch = Z(r, new Vector3(0f, 2.0f, -0.26f), 0.09f, 0.03f, ArtLibrary.Hex("#4A3A2A"));
            loch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Q(r, new Vector3(0f, 1.82f, -0.33f), new Vector3(0.04f, 0.04f, 0.18f), HolzDunkel);
            K(r, new Vector3(0.12f, 2.6f, 0f), 0.1f, ArtLibrary.Hex("#7FB0E0"));
            K(r, new Vector3(0.12f, 2.68f, -0.06f), 0.06f, ArtLibrary.Hex("#7FB0E0"));
            Q(r, new Vector3(0.12f, 2.67f, -0.13f), new Vector3(0.03f, 0.03f, 0.06f), Gelb, false);
        }

        private static void Wegweiser(Transform r)
        {
            Q(r, new Vector3(0f, 1.0f, 0f), new Vector3(0.14f, 2.0f, 0.14f), HolzDunkel);
            foreach (var (y, yaw, x) in new[] { (1.75f, 0f, 0.42f), (1.35f, 180f, -0.38f), (0.95f, 60f, 0.3f) })
            {
                var schild = Kind(r, "Schild");
                schild.localPosition = new Vector3(0f, y, 0f);
                schild.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Q(schild, new Vector3(0.45f, 0f, 0f), new Vector3(0.8f, 0.24f, 0.06f), Creme);
                var spitze = Q(schild, new Vector3(0.88f, 0f, 0f), new Vector3(0.17f, 0.17f, 0.06f), Creme);
                spitze.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                Q(schild, new Vector3(0.4f, 0f, -0.035f), new Vector3(0.5f, 0.05f, 0.01f), HolzDunkel, false);
            }
        }

        private static void Strohpuppe(Transform r, Vector3 fuss)
        {
            Q(r, fuss + new Vector3(0f, 0.95f, 0f), new Vector3(0.12f, 1.9f, 0.12f), HolzDunkel);
            Q(r, fuss + new Vector3(0f, 1.4f, 0f), new Vector3(1.3f, 0.1f, 0.1f), HolzDunkel);
            var koerper = K(r, fuss + new Vector3(0f, 1.15f, 0f), 0.42f, Heu);
            koerper.transform.localScale = new Vector3(0.75f, 0.95f, 0.6f);
            Q(r, fuss + new Vector3(0f, 1.25f, -0.2f), new Vector3(0.24f, 0.24f, 0.04f), Teal, false);
            K(r, fuss + new Vector3(0f, 1.88f, 0f), 0.27f, HausCreme);
            Z(r, fuss + new Vector3(0f, 2.12f, 0f), 0.4f, 0.04f, HolzHell);
            Z(r, fuss + new Vector3(0f, 2.25f, 0f), 0.22f, 0.26f, HolzHell);
            foreach (var sx in new[] { -0.08f, 0.08f }) K(r, fuss + new Vector3(sx, 1.92f, -0.24f), 0.035f, ArtLibrary.Hex("#3B3630"));
            foreach (var sx in new[] { -0.7f, 0.7f })
            {
                var hand = K(r, fuss + new Vector3(sx, 1.4f, 0f), 0.12f, Heu);
                hand.transform.localScale = new Vector3(0.3f, 0.2f, 0.2f);
            }
        }

        private static void Windspiel(Transform r)
        {
            Q(r, new Vector3(0f, 1.1f, 0f), new Vector3(0.12f, 2.2f, 0.12f), HolzDunkel);
            Q(r, new Vector3(0.35f, 2.15f, 0f), new Vector3(0.8f, 0.08f, 0.08f), HolzDunkel);
            var spiel = Kind(r, "Spiel");
            spiel.localPosition = new Vector3(0.7f, 2.1f, 0f);
            spiel.gameObject.AddComponent<Dreher>().Grad = 30f;
            Z(spiel, new Vector3(0f, -0.05f, 0f), 0.24f, 0.04f, Teal);
            for (var i = 0; i < 5; i++)
            {
                var a = i * Mathf.PI * 2f / 5f;
                Z(spiel, new Vector3(Mathf.Cos(a) * 0.17f, -0.35f - (i % 2) * 0.1f, Mathf.Sin(a) * 0.17f), 0.035f, 0.45f + (i % 3) * 0.08f, Metall);
            }
        }

        // ── the quest rewards ──

        private static readonly Color Gold = ArtLibrary.Hex("#F2C230"), Silber = ArtLibrary.Hex("#D8DCE4"), Bronze = ArtLibrary.Hex("#C98A4E");

        /// <summary>The golden pitchfork on its plinth (Rosi's reward), with a brass plaque and a velvet cushion.</summary>
        private static void GoldMistgabel(Transform r)
        {
            Q(r, new Vector3(0f, 0.08f, 0f), new Vector3(1.0f, 0.16f, 1.0f), Stein);
            Q(r, new Vector3(0f, 0.42f, 0f), new Vector3(0.74f, 0.56f, 0.74f), HolzDunkel);
            Q(r, new Vector3(0f, 0.72f, 0f), new Vector3(0.84f, 0.06f, 0.84f), HolzHell);
            var kissen = K(r, new Vector3(0f, 0.79f, 0f), 0.3f, ArtLibrary.Hex("#C0485E"));
            kissen.transform.localScale = new Vector3(0.66f, 0.14f, 0.66f);
            Q(r, new Vector3(0f, 0.46f, -0.38f), new Vector3(0.42f, 0.18f, 0.02f), Gold);
            var gabel = Kind(r, "Gabel");
            gabel.localPosition = new Vector3(0f, 0.84f, 0f);
            gabel.localRotation = Quaternion.Euler(-6f, 0f, 9f);
            var glanz = ArtLibrary.Solid(Gold, 0.78f);
            var stiel = ArtLibrary.Cylinder(gabel, "Stiel", gabel.position, 0.04f, 1.5f, glanz, true);
            stiel.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            stiel.transform.localScale = new Vector3(0.08f, 0.75f, 0.08f);
            var quer = ArtLibrary.Box(gabel, "Quer", gabel.position, Vector3.one, glanz, true);
            quer.transform.localPosition = new Vector3(0f, 1.52f, 0f);
            quer.transform.localScale = new Vector3(0.42f, 0.06f, 0.06f);
            for (var i = 0; i < 4; i++)
            {
                var zinke = ArtLibrary.Cylinder(gabel, "Zinke", gabel.position, 0.022f, 0.42f, glanz, true);
                zinke.transform.localPosition = new Vector3(-0.18f + i * 0.12f, 1.74f, 0f);
                zinke.transform.localScale = new Vector3(0.044f, 0.21f, 0.044f);
                var spitze = ArtLibrary.Sphere(gabel, "Spitze", gabel.position, 0.03f, ArtLibrary.Glow(new Color(1.35f, 1.1f, 0.45f)), false);
                spitze.transform.localPosition = new Vector3(-0.18f + i * 0.12f, 1.96f, 0f);
            }
            var ring = Kind(r, "Glitzer");
            ring.localPosition = new Vector3(0f, 2.3f, 0f);
            ring.gameObject.AddComponent<Dreher>().Grad = 40f;
            for (var i = 0; i < 3; i++)
            {
                var a = i * Mathf.PI * 2f / 3f;
                var funke = ArtLibrary.Sphere(ring, "Funke", ring.position, 0.045f, ArtLibrary.Glow(new Color(1.4f, 1.2f, 0.6f)), false);
                funke.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.4f, (i % 2) * 0.12f, Mathf.Sin(a) * 0.4f);
            }
        }

        /// <summary>The ribbon wall (Alwin's reward): a little roofed board with every ribbon the farm has won.</summary>
        private static void Schleifenwand(Transform r, IReadOnlyList<int> schleifen)
        {
            foreach (var sx in new[] { -1.15f, 1.15f })
            {
                Q(r, new Vector3(sx, 1.05f, 0.1f), new Vector3(0.16f, 2.1f, 0.16f), HolzDunkel);
                K(r, new Vector3(sx, 2.14f, 0.1f), 0.1f, Gelb);
            }
            Q(r, new Vector3(0f, 1.25f, 0.1f), new Vector3(2.3f, 1.35f, 0.08f), HausCreme);
            foreach (var (c, s) in new[]
            {
                (new Vector3(0f, 1.95f, 0.05f), new Vector3(2.42f, 0.1f, 0.12f)), (new Vector3(0f, 0.55f, 0.05f), new Vector3(2.42f, 0.1f, 0.12f)),
            })
                Q(r, c, s, HolzHell);
            Dach(r, new Vector3(0f, 2.0f, 0.1f), 0.7f, 0.32f, 2.7f, false, Teal);
            Q(r, new Vector3(0f, 2.36f, 0.1f), new Vector3(2.8f, 0.07f, 0.12f), Creme);
            Herz(r, new Vector3(0f, 2.18f, -0.18f), 0.16f);
            var liste = schleifen ?? new List<int>();
            const int spalten = 4, zeilen = 3;
            for (var i = 0; i < spalten * zeilen; i++)
            {
                var p = new Vector3(-0.84f + (i % spalten) * 0.56f, 1.66f - (i / spalten) * 0.44f, 0.04f);
                if (i < liste.Count) Rosette(r, p, liste[i], 0.36f);
                else K(r, p + new Vector3(0f, 0.1f, 0f), 0.025f, HolzDunkel);
            }
        }

        /// <summary>
        /// A prize rosette facing −z: pleated ring, cream centre, button and two tails; 1 gold, 2 silver, 3 bronze, 0 the
        /// quest's bronze ribbon with a golden star in the middle.
        /// </summary>
        public static void Rosette(Transform r, Vector3 mitte, int platz, float g)
        {
            var farbe = platz == 1 ? Gold : platz == 2 ? Silber : Bronze;
            var hell = Color.Lerp(farbe, Color.white, 0.25f);
            foreach (var s in new[] { -1f, 1f })
            {
                var band = Q(r, mitte + new Vector3(s * g * 0.14f, -g * 0.5f, 0.01f), new Vector3(g * 0.24f, g * 0.85f, 0.012f), farbe, false);
                band.transform.localRotation = Quaternion.Euler(0f, 0f, s * 11f);
            }
            for (var i = 0; i < 14; i++)
            {
                var a = i * Mathf.PI * 2f / 14f;
                var falte = K(r, mitte + new Vector3(Mathf.Cos(a) * g * 0.36f, Mathf.Sin(a) * g * 0.36f, 0f), g * 0.13f, hell);
                falte.transform.localScale = new Vector3(g * 0.26f, g * 0.26f, 0.03f);
            }
            var scheibe = Z(r, mitte + new Vector3(0f, 0f, -0.012f), g * 0.3f, 0.02f, Creme);
            scheibe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var knopf = Z(r, mitte + new Vector3(0f, 0f, -0.026f), g * 0.17f, 0.02f, platz == 0 ? Gelb : farbe);
            knopf.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (platz == 0)
            {
                var stern = Q(r, mitte + new Vector3(0f, 0f, -0.04f), new Vector3(g * 0.17f, g * 0.17f, 0.012f), Bronze, false);
                stern.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
        }

        /// <summary>The ranch-fair bunting (Rosi's chapter reward): two flagged poles, a sagging string of pastel pennants and lights.</summary>
        private static void Girlande(Transform r)
        {
            const float halb = 1.3f, oben = 2.45f, durchhang = 0.42f;
            foreach (var sx in new[] { -halb, halb })
            {
                Z(r, new Vector3(sx, 0.1f, 0f), 0.2f, 0.2f, Stein);
                Q(r, new Vector3(sx, oben * 0.5f + 0.1f, 0f), new Vector3(0.12f, oben + 0.2f, 0.12f), Creme);
                K(r, new Vector3(sx, oben + 0.25f, 0f), 0.09f, Gelb);
                foreach (var sz in new[] { -1f, 1f })
                {
                    var schleife = K(r, new Vector3(sx + sz * 0.1f, oben - 0.05f, -0.05f), 0.08f, Rosa);
                    schleife.transform.localScale = new Vector3(0.16f, 0.1f, 0.05f);
                }
            }
            var farben = new[] { Rosa, Gelb, Teal, SchRot, Lila, ArtLibrary.Hex("#7FB0E0") };
            Vector3 Kette(float t) => new Vector3(Mathf.Lerp(-halb, halb, t), oben - durchhang * (1f - Mathf.Pow(2f * t - 1f, 2f)), 0f);
            const int glieder = 16;
            for (var i = 0; i < glieder; i++)
            {
                var a = Kette(i / (float)glieder);
                var b = Kette((i + 1) / (float)glieder);
                var d = b - a;
                var seil = Q(r, (a + b) * 0.5f, new Vector3(d.magnitude + 0.01f, 0.018f, 0.018f), HolzDunkel, false);
                seil.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            for (var i = 1; i < 10; i++)
            {
                var p = Kette(i / 10f);
                var wimpel = Dach(r, p, 0.26f, 0.3f, 0.02f, true, farben[i % farben.Length]);
                wimpel.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
                if (i % 2 == 1)
                {
                    var licht = K(r, Kette((i + 0.5f) / 10f) + new Vector3(0f, -0.06f, 0f), 0.045f, Gelb);
                    licht.GetComponent<Renderer>().sharedMaterial = ArtLibrary.Glow(new Color(1.3f, 1.05f, 0.55f));
                }
            }
        }

        // ── ground ──

        private static void Schotterweg(Transform r)
        {
            Q(r, new Vector3(0f, 0.04f, 0f), new Vector3(2.98f, 0.08f, 2.98f), Schotter, false);
            var rng = new System.Random(3);
            for (var i = 0; i < 9; i++)
            {
                var stein = K(r, new Vector3((float)(rng.NextDouble() - 0.5) * 2.5f, 0.09f, (float)(rng.NextDouble() - 0.5) * 2.5f), 0.12f, Stein);
                stein.transform.localScale = new Vector3(0.24f, 0.08f, 0.18f);
            }
        }

        private static void Pflasterweg(Transform r)
        {
            Q(r, new Vector3(0f, 0.03f, 0f), new Vector3(2.98f, 0.06f, 2.98f), ArtLibrary.Hex("#A89C8E"), false);
            var hell = Pflaster;
            var dunkel = ArtLibrary.Hex("#A9998A");
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 3; x++)
                    Q(r, new Vector3(-1f + x + (y % 2) * 0.12f, 0.08f, -1f + y), new Vector3(0.9f, 0.06f, 0.9f), (x + y) % 2 == 0 ? hell : dunkel, false);
        }

        private static void Sandboden(Transform r)
        {
            Q(r, new Vector3(0f, 0.04f, 0f), new Vector3(2.98f, 0.08f, 2.98f), Sand, false);
            for (var i = 0; i < 3; i++) Q(r, new Vector3(0f, 0.085f, -0.9f + i * 0.9f), new Vector3(2.2f, 0.01f, 0.06f), ArtLibrary.Hex("#EFE0C2"), false);
        }

        private static void Blumenwiese(Transform r)
        {
            Q(r, new Vector3(0f, 0.05f, 0f), new Vector3(2.98f, 0.1f, 2.98f), WieseSatt, false);
            var rng = new System.Random(9);
            for (var i = 0; i < 8; i++)
                K(r, new Vector3((float)(rng.NextDouble() - 0.5) * 2.4f, 0.16f, (float)(rng.NextDouble() - 0.5) * 2.4f), 0.11f, i % 3 == 0 ? Rosa : i % 3 == 1 ? Gelb : Lila);
        }

        // ── helpers ──

        private static void Herz(Transform r, Vector3 mitte, float g)
        {
            var rot = Rosa;
            K(r, mitte + new Vector3(-g * 0.42f, g * 0.25f, 0f), g * 0.5f, rot);
            K(r, mitte + new Vector3(g * 0.42f, g * 0.25f, 0f), g * 0.5f, rot);
            var spitze = Q(r, mitte + new Vector3(0f, -g * 0.15f, 0f), new Vector3(g * 0.95f, g * 0.95f, g * 0.6f), rot);
            spitze.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private static Transform Kind(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static GameObject Q(Transform p, Vector3 c, Vector3 s, Color farbe, bool schatten = true)
        {
            var go = ArtLibrary.Box(p, "Q", p.position, s, M(farbe), schatten);
            go.transform.localPosition = c;
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        private static GameObject K(Transform p, Vector3 c, float radius, Color farbe)
        {
            var go = ArtLibrary.Sphere(p, "K", p.position, radius, M(farbe, 0.25f), true);
            go.transform.localPosition = c;
            return go;
        }

        private static GameObject Z(Transform p, Vector3 c, float radius, float hoehe, Color farbe)
        {
            var go = ArtLibrary.Cylinder(p, "Z", p.position, radius, hoehe, M(farbe), true);
            go.transform.localPosition = c;
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        private static Mesh _prisma;

        /// <summary>A gable roof: base at <paramref name="basis"/>, <paramref name="breite"/> across the ridge, the ridge along z (or x).</summary>
        private static GameObject Dach(Transform p, Vector3 basis, float breite, float hoehe, float laenge, bool firstLaengsZ, Color farbe)
        {
            var go = new GameObject("Dach");
            go.transform.SetParent(p, false);
            go.transform.localPosition = basis;
            go.transform.localRotation = firstLaengsZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            go.transform.localScale = new Vector3(breite, hoehe, laenge);
            go.AddComponent<MeshFilter>().sharedMesh = Prisma();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = M(farbe, 0.1f);
            return go;
        }

        /// <summary>The unit roof prism: x −0.5..0.5 across, y 0..1 up to the ridge, z −0.5..0.5 along it (flat shaded).</summary>
        private static Mesh Prisma()
        {
            if (_prisma != null) return _prisma;
            var v = new List<Vector3>();
            var t = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            Vector3 a0 = new Vector3(-0.5f, 0f, -0.5f), a1 = new Vector3(-0.5f, 0f, 0.5f), b0 = new Vector3(0.5f, 0f, -0.5f), b1 = new Vector3(0.5f, 0f, 0.5f);
            Vector3 f0 = new Vector3(0f, 1f, -0.5f), f1 = new Vector3(0f, 1f, 0.5f);
            Tri(a0, f1, f0); Tri(a0, a1, f1);
            Tri(b1, f0, f1); Tri(b1, b0, f0);
            Tri(a0, f0, b0);
            Tri(b1, f1, a1);
            _prisma = new Mesh { name = "RanchBauPrisma" };
            _prisma.SetVertices(v);
            _prisma.SetTriangles(t, 0);
            _prisma.RecalculateNormals();
            _prisma.RecalculateBounds();
            return _prisma;
        }

        /// <summary>A farm/prop GLB fitted into <paramref name="groesse"/> metres across, standing on <paramref name="lokal"/>.</summary>
        public static Transform Glb(Transform parent, string path, Vector3 lokal, float yaw, float groesse, string tint = null, string nur = null)
        {
            var anchor = new GameObject("Glb:" + Path.GetFileNameWithoutExtension(path)).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = lokal;
            anchor.localRotation = Quaternion.Euler(0f, yaw, 0f);
            _ = FitAsync(anchor, path, groesse);
            if (tint != null) _ = RanchHofStage.TintWhenLoaded(anchor, ArtLibrary.Hex(tint), nur);
            return anchor;
        }

        private static async Task FitAsync(Transform anchor, string path, float groesse)
        {
            var clone = await AssetLoader.InstanceAsync(path, anchor, Path.GetFileNameWithoutExtension(path));
            if (clone == null) return;
            if (anchor == null) { Object.Destroy(clone); return; }
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            var toAnchor = anchor.worldToLocalMatrix;
            var b = new Bounds();
            var erst = true;
            foreach (var mf in clone.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = toAnchor * mf.transform.localToWorldMatrix;
                for (var i = 0; i < 8; i++)
                {
                    var ecke = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (erst) { b = new Bounds(ecke, Vector3.zero); erst = false; }
                    else b.Encapsulate(ecke);
                }
            }
            if (erst) return;
            var quer = Mathf.Max(b.size.x, b.size.z);
            if (quer < 1e-4f) return;
            var s = groesse / quer;
            clone.transform.localScale = Vector3.one * s;
            clone.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
        }

        /// <summary>
        /// Folds the direct primitive children of a piece into one mesh per material (fences by the dozen, a stand of
        /// boards); animated groups and model anchors are children without a mesh of their own and stay as they are.
        /// </summary>
        public static void Verschmelze(Transform root)
        {
            var gruppen = new Dictionary<Material, (List<CombineInstance> teile, bool schatten)>();
            var toLocal = root.worldToLocalMatrix;
            var weg = new List<GameObject>();
            foreach (Transform c in root)
            {
                if (!c.TryGetComponent<MeshFilter>(out var mf) || !c.TryGetComponent<MeshRenderer>(out var mr)) continue;
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || mr.sharedMaterial == null) continue;
                if (!gruppen.TryGetValue(mr.sharedMaterial, out var g)) g = (new List<CombineInstance>(), false);
                g.teile.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = 0, transform = toLocal * c.localToWorldMatrix });
                g.schatten |= mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                gruppen[mr.sharedMaterial] = g;
                weg.Add(c.gameObject);
            }
            if (weg.Count < 2) return;
            foreach (var go in weg)
            {
                go.SetActive(false);
                Object.Destroy(go);
            }
            foreach (var kv in gruppen)
            {
                var mesh = new Mesh { name = "Teil:" + kv.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.teile.ToArray(), true, true);
                var go = new GameObject("Verschmolzen");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = kv.Value.schatten ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                go.AddComponent<MeshBesitzer>().Mesh = mesh;
            }
        }

        /// <summary>Destroys a merged mesh with its piece.</summary>
        public sealed class MeshBesitzer : MonoBehaviour
        {
            public Mesh Mesh;
            private void OnDestroy() { if (Mesh != null) Destroy(Mesh); }
        }

        /// <summary>Turns a group round its local y (the walker's arms, a wind chime).</summary>
        public sealed class Dreher : MonoBehaviour
        {
            public float Grad = 20f;
            private void Update() => transform.Rotate(0f, Grad * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        /// <summary>A flag cloth swaying on its pole.</summary>
        public sealed class Wedler : MonoBehaviour
        {
            private float _t, _basis;
            private void Start() { _basis = transform.localEulerAngles.y; _t = Random.value * 10f; }
            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                var e = transform.localEulerAngles;
                transform.localEulerAngles = new Vector3(e.x, _basis + Mathf.Sin(_t * 2.6f) * 14f, Mathf.Sin(_t * 3.7f) * 3f);
            }
        }
    }
}
