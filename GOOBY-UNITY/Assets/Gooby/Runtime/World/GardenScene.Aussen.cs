using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.State;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// "Gestalten" outside, the rest of the cottage and the plot (port of house_exterior.gd + garten_haus.gd): the house
    /// number plaque beside the door (wooden, enamel or modern, any number 1–99), the mailbox (classic, round, wooden,
    /// designer), a canopy or awning over the door, the fence round the lawn (picket, split-rail, metal, hedge or none),
    /// the path from the door (slabs, round stones, bricks, chequered) and the ground of the lawn (short or tall grass,
    /// wildflowers, gravel, stone slabs, sand, forest floor) — each in its palette colour. Parts the player never chose
    /// keep the garden's built look (log fence, stepping stones, the red mailbox, the board's lawn).
    /// </summary>
    public sealed partial class GardenScene
    {
        private Renderer _lawn;
        private Material _lawnBuilt;
        private readonly List<GameObject> _builtFence = new List<GameObject>();
        private readonly List<GameObject> _builtStones = new List<GameObject>();
        private GameObject _builtMailbox;
        private Transform _aussen;
        private string _aussenKey;

        /// <summary>The cottage's front wall (its door and steps face the lawn).</summary>
        private float FacadeZ => North + 0.86f;

        private static Color Farbe(string id) =>
            !string.IsNullOrEmpty(id) && HouseStyle.Palette.TryGetValue(id, out var hex) ? ArtLibrary.Hex(hex) : ArtLibrary.Hex("#FFF6EC");

        private static Material M(Color c, float smooth = 0.25f) => ArtLibrary.Solid(c, smooth);

        /// <summary>Rebuilds the outside parts from the save (cheap no-op when nothing changed).</summary>
        public void ApplyAussen(GameStateStore gs)
        {
            var key = "";
            foreach (var (art, _, _, _) in HouseStyle.Teile)
            {
                var (id, farbe) = HouseStyle.TeilWahl(gs, art);
                key += art + ":" + (HouseStyle.TeilGewaehlt(gs, art) ? id + "/" + farbe : "-") + ";";
            }
            key += "nr:" + HouseStyle.HausnummerZahl(gs);
            if (key == _aussenKey) return;
            _aussenKey = key;
            if (_aussen != null) Object.Destroy(_aussen.gameObject);
            _aussen = new GameObject("Aussen").transform;
            _aussen.SetParent(Root.transform, false);

            var nr = HouseStyle.TeilWahl(gs, HouseStyle.Hausnummer);
            BuildHausnummer(nr.id, HouseStyle.HausnummerZahl(gs));
            var vordach = HouseStyle.TeilWahl(gs, HouseStyle.Vordach);
            BuildVordach(vordach.id, vordach.farbe);

            var briefkasten = HouseStyle.TeilGewaehlt(gs, HouseStyle.Briefkasten);
            if (_builtMailbox != null) _builtMailbox.SetActive(!briefkasten);
            if (briefkasten) { var (id, farbe) = HouseStyle.TeilWahl(gs, HouseStyle.Briefkasten); BuildBriefkasten(id, farbe); }

            var zaun = HouseStyle.TeilGewaehlt(gs, HouseStyle.Zaun);
            foreach (var f in _builtFence) if (f != null) f.SetActive(!zaun);
            if (zaun) { var (id, farbe) = HouseStyle.TeilWahl(gs, HouseStyle.Zaun); BuildZaun(id, farbe); }

            var weg = HouseStyle.TeilGewaehlt(gs, HouseStyle.Weg);
            foreach (var s in _builtStones) if (s != null) s.SetActive(!weg);
            if (weg) { var (id, farbe) = HouseStyle.TeilWahl(gs, HouseStyle.Weg); BuildWeg(id, farbe); }

            var grund = HouseStyle.TeilGewaehlt(gs, HouseStyle.Grund);
            if (_lawn != null) _lawn.sharedMaterial = _lawnBuilt;
            if (grund) { var (id, farbe) = HouseStyle.TeilWahl(gs, HouseStyle.Grund); BuildGrund(id, farbe); }
        }

        // ── the house number: a plaque right of the door with seven-segment digits ──

        private static readonly string[] Segmente = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };

        private void BuildHausnummer(string stil, int zahl)
        {
            // sized to be read from the garden camera, not to scale
            var at = new Vector3(CottageX + 1.12f, 1.6f, FacadeZ - 0.03f);
            Color board, ink;
            switch (stil)
            {
                case "emaille":
                    board = ArtLibrary.Hex("#FBFAF6");
                    ink = ArtLibrary.Hex("#2E4A8C");
                    ArtLibrary.Box(_aussen, "NummerRand", at + Vector3.forward * 0.006f, new Vector3(0.62f, 0.44f, 0.02f), M(ink, 0.5f), false);
                    break;
                case "modern":
                    board = ArtLibrary.Hex("#3F3A45");
                    ink = ArtLibrary.Hex("#FFFFFF");
                    break;
                default:
                    board = ArtLibrary.Hex("#C9A36B");
                    ink = ArtLibrary.Hex("#4A3B36");
                    break;
            }
            ArtLibrary.Box(_aussen, "NummerSchild", at, new Vector3(0.58f, 0.4f, 0.025f), M(board, stil == "emaille" ? 0.6f : 0.25f), false);
            var text = Mathf.Clamp(zahl, 1, 99).ToString();
            var inkMat = M(ink, 0.4f);
            const float w = 0.12f, h = 0.12f, t = 0.032f;
            for (var d = 0; d < text.Length; d++)
            {
                var center = at + new Vector3((d - (text.Length - 1) * 0.5f) * 0.2f, 0f, -0.02f);
                // a lone "1" stands in the middle of its cell instead of hugging the right edge
                var shift = text[d] == '1' ? -w * 0.5f : 0f;
                foreach (var seg in Segmente[text[d] - '0'])
                {
                    Vector3 off, size;
                    switch (seg)
                    {
                        case 'a': off = new Vector3(0f, h, 0f); size = new Vector3(w + t, t, t); break;
                        case 'g': off = Vector3.zero; size = new Vector3(w + t, t, t); break;
                        case 'd': off = new Vector3(0f, -h, 0f); size = new Vector3(w + t, t, t); break;
                        case 'b': off = new Vector3(w * 0.5f + shift, h * 0.5f, 0f); size = new Vector3(t, h, t); break;
                        case 'c': off = new Vector3(w * 0.5f + shift, -h * 0.5f, 0f); size = new Vector3(t, h, t); break;
                        case 'e': off = new Vector3(-w * 0.5f, -h * 0.5f, 0f); size = new Vector3(t, h, t); break;
                        default: off = new Vector3(-w * 0.5f, h * 0.5f, 0f); size = new Vector3(t, h, t); break;
                    }
                    ArtLibrary.Box(_aussen, "Ziffer", center + off, size, inkMat, false);
                }
            }
        }

        // ── the canopy over the door ──

        private void BuildVordach(string stil, string farbe)
        {
            if (stil == "keins" || string.IsNullOrEmpty(stil)) return;
            var c = Farbe(farbe);
            var center = new Vector3(CottageX, 2.5f, FacadeZ - 0.36f);
            // the front edge (towards the lawn, −Z) drops: a negative pitch lifts +Z (the wall side)
            var pitch = stil == "vordach_holz" ? 14f : 24f;
            var tilt = Quaternion.Euler(-pitch, 0f, 0f);
            var vorn = tilt * new Vector3(0f, 0f, -0.43f);
            if (stil == "vordach_holz")
            {
                ArtLibrary.Box(_aussen, "Vordach", center, new Vector3(1.8f, 0.07f, 0.78f), M(c, 0.3f)).transform.rotation = tilt;
                foreach (var s in new[] { -0.75f, 0.75f })
                {
                    var bracket = ArtLibrary.Box(_aussen, "Konsole", new Vector3(CottageX + s, 2.3f, FacadeZ - 0.2f), new Vector3(0.06f, 0.06f, 0.48f), M(c * 0.85f, 0.3f));
                    bracket.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
                }
                return;
            }
            var streifen = stil == "markise_gestreift";
            const int n = 8;
            for (var i = 0; i < n; i++)
            {
                var col = streifen && i % 2 == 1 ? ArtLibrary.Hex("#FFFDF8") : c;
                var strip = ArtLibrary.Box(_aussen, "Markise", center + new Vector3((i - (n - 1) * 0.5f) * 0.22f, 0f, 0f), new Vector3(0.22f, 0.035f, 0.86f), M(col, 0.15f), false);
                strip.transform.rotation = tilt;
                var flap = ArtLibrary.Box(_aussen, "Volant", center + vorn + new Vector3((i - (n - 1) * 0.5f) * 0.22f, -0.07f, 0f), new Vector3(0.22f, 0.14f, 0.02f), M(col, 0.15f), false);
                flap.transform.rotation = Quaternion.identity;
            }
            foreach (var s in new[] { -0.86f, 0.86f })
                ArtLibrary.Box(_aussen, "Gestaenge", new Vector3(CottageX + s, 2.42f, FacadeZ - 0.4f), new Vector3(0.025f, 0.025f, 0.8f), M(ArtLibrary.Hex("#9AA3AD"), 0.6f), false)
                    .transform.rotation = tilt;
        }

        // ── the mailbox (left of the door, facing the lawn) ──

        private void BuildBriefkasten(string stil, string farbe)
        {
            var c = Farbe(farbe);
            var foot = new Vector3(CottageX - 1.55f, 0f, North + 0.62f);
            var post = M(ArtLibrary.Hex("#8A6642"), 0.2f);
            var dark = M(ArtLibrary.Hex("#3F3A45"), 0.3f);
            switch (stil)
            {
                case "kugel":
                    ArtLibrary.Cylinder(_aussen, "Pfosten", foot + Vector3.up * 0.5f, 0.03f, 1.0f, M(ArtLibrary.Hex("#9AA3AD"), 0.6f));
                    ArtLibrary.Sphere(_aussen, "Kugel", foot + Vector3.up * 1.18f, 0.24f, M(c, 0.5f));
                    ArtLibrary.Box(_aussen, "Schlitz", foot + new Vector3(0f, 1.24f, -0.22f), new Vector3(0.18f, 0.03f, 0.04f), dark, false);
                    break;
                case "holz":
                    ArtLibrary.Box(_aussen, "Pfosten", foot + Vector3.up * 0.5f, new Vector3(0.09f, 1.0f, 0.09f), post);
                    ArtLibrary.Box(_aussen, "Kasten", foot + Vector3.up * 1.12f, new Vector3(0.42f, 0.32f, 0.36f), M(c, 0.2f));
                    foreach (var s in new[] { -1f, 1f })
                        ArtLibrary.Box(_aussen, "Dach", foot + new Vector3(s * 0.11f, 1.34f, 0f), new Vector3(0.26f, 0.03f, 0.42f), M(c * 0.8f, 0.2f)).transform.rotation = Quaternion.Euler(0f, 0f, -s * 28f);
                    ArtLibrary.Box(_aussen, "Schlitz", foot + new Vector3(0f, 1.18f, -0.185f), new Vector3(0.22f, 0.03f, 0.02f), dark, false);
                    break;
                case "modern":
                    ArtLibrary.Box(_aussen, "Saeule", foot + Vector3.up * 0.55f, new Vector3(0.34f, 1.1f, 0.22f), M(c, 0.45f));
                    ArtLibrary.Box(_aussen, "Schlitz", foot + new Vector3(0f, 0.95f, -0.115f), new Vector3(0.24f, 0.03f, 0.02f), dark, false);
                    ArtLibrary.Box(_aussen, "Streifen", foot + new Vector3(0f, 0.7f, -0.115f), new Vector3(0.34f, 0.02f, 0.02f), M(ArtLibrary.Hex("#C9CED6"), 0.8f), false);
                    break;
                default:
                    ArtLibrary.Box(_aussen, "Pfosten", foot + Vector3.up * 0.5f, new Vector3(0.08f, 1.0f, 0.08f), post);
                    ArtLibrary.Box(_aussen, "Kasten", foot + Vector3.up * 1.1f, new Vector3(0.32f, 0.24f, 0.46f), M(c, 0.35f));
                    ArtLibrary.Cylinder(_aussen, "Bogen", foot + Vector3.up * 1.22f, 0.16f, 0.46f, M(c, 0.35f)).transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    ArtLibrary.Box(_aussen, "Fahne", foot + new Vector3(0.18f, 1.28f, 0.05f), new Vector3(0.02f, 0.2f, 0.12f), M(ArtLibrary.Hex("#E8574C"), 0.4f), false);
                    break;
            }
        }

        // ── the fence round the lawn ──

        /// <summary>The fence's straight runs (same lines as the built log fence: three sides and the north stubs).</summary>
        private IEnumerable<(Vector3 a, Vector3 b)> FenceRuns()
        {
            var x0 = West - FenceInset; var x1 = East + FenceInset;
            var z0 = South - FenceInset; var z1 = North + FenceInset;
            yield return (new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0));
            yield return (new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1));
            yield return (new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1));
            yield return (new Vector3(x0, 0f, z1), new Vector3(CottageX - CottageHalfWidth, 0f, z1));
            yield return (new Vector3(CottageX + CottageHalfWidth, 0f, z1), new Vector3(x1, 0f, z1));
        }

        private void BuildZaun(string stil, string farbe)
        {
            if (stil == "keins" || string.IsNullOrEmpty(stil)) return;
            var c = Farbe(farbe);
            foreach (var (a, b) in FenceRuns())
            {
                var run = b - a;
                var len = run.magnitude;
                if (len < 0.4f) continue;
                var dir = run / len;
                var yaw = Mathf.Abs(dir.x) > 0.5f ? 0f : 90f;
                switch (stil)
                {
                    case "metall":
                        MetallZaun(a, dir, len, c);
                        break;
                    default:
                    {
                        var (archetype, seg) = stil == "hecke" ? ("hedge_long", 6.24f) : stil == "staketen" ? ("log_fence", 1.48f) : ("picket_fence", 2.0f);
                        var n = Mathf.Max(1, Mathf.RoundToInt(len / seg));
                        var step = len / n;
                        for (var i = 0; i < n; i++)
                            PlaceTinted(archetype, a + dir * (step * (i + 0.5f)), yaw, new Vector3(step / seg, stil == "hecke" ? 0.85f : 1f, stil == "hecke" ? 0.75f : 1f), c);
                        break;
                    }
                }
            }
        }

        private void MetallZaun(Vector3 a, Vector3 dir, float len, Color c)
        {
            var m = M(c, 0.55f);
            var posts = Mathf.Max(2, Mathf.RoundToInt(len / 0.75f) + 1);
            for (var i = 0; i < posts; i++)
            {
                var p = a + dir * (len * i / (posts - 1));
                ArtLibrary.Box(_aussen, "Pfosten", p + Vector3.up * 0.48f, new Vector3(0.04f, 0.96f, 0.04f), m);
                ArtLibrary.Sphere(_aussen, "Knauf", p + Vector3.up * 0.99f, 0.035f, m, false);
            }
            foreach (var y in new[] { 0.18f, 0.55f, 0.88f })
            {
                var rail = ArtLibrary.Box(_aussen, "Stange", a + dir * (len * 0.5f) + Vector3.up * y, new Vector3(len, 0.025f, 0.025f), m, false);
                rail.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir);
            }
        }

        /// <summary>A Blender scenery piece (fence, hedge) in a palette colour — the brightness of its materials stays.</summary>
        private async void PlaceTinted(string archetype, Vector3 pos, float yaw, Vector3 scale, Color tint)
        {
            var parent = _aussen;
            var go = await AssetLoader.SceneryInstanceAsync(archetype, parent);
            if (go == null) return;
            if (parent == null) { Object.Destroy(go); return; }
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var mat in mats)
                {
                    var orig = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
                    var lum = 0.2126f * orig.r + 0.7152f * orig.g + 0.0722f * orig.b;
                    var tinted = tint * Mathf.Clamp(lum / 0.62f, 0.55f, 1.12f);
                    tinted.a = orig.a;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tinted);
                    mat.color = tinted;
                }
                r.materials = mats;
            }
        }

        private async void PlaceProp(string archetype, Vector3 pos, float yaw, float scale)
        {
            var parent = _aussen;
            var go = await AssetLoader.PropInstanceAsync(archetype, parent);
            if (go == null) return;
            if (parent == null) { Object.Destroy(go); return; }
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
        }

        // ── the path from the door ──

        private void BuildWeg(string stil, string farbe)
        {
            if (stil == "keins" || string.IsNullOrEmpty(stil)) return;
            var c = Farbe(farbe);
            var hell = Color.Lerp(c, Color.white, 0.45f);
            for (var i = 0; i < 8; i++)
            {
                var p = new Vector3(CottageX, 0.02f, North + 0.55f - i * 0.62f);
                var jitter = Quaternion.Euler(0f, ((i * 37) % 9) - 4f, 0f);
                switch (stil)
                {
                    case "rund":
                        ArtLibrary.Cylinder(_aussen, "Trittstein", p + new Vector3(i % 2 == 0 ? -0.14f : 0.14f, 0f, 0f), 0.3f, 0.05f, M(c, 0.2f), false);
                        break;
                    case "ziegel":
                        foreach (var s in new[] { -0.15f, 0.15f })
                            ArtLibrary.Box(_aussen, "Ziegel", p + new Vector3(s, 0f, 0f), i % 2 == 0 ? new Vector3(0.28f, 0.05f, 0.56f) : new Vector3(0.28f, 0.05f, 0.56f), M(i % 2 == 0 ? c : c * 0.9f, 0.15f), false)
                                .transform.rotation = jitter;
                        break;
                    case "schach":
                        ArtLibrary.Box(_aussen, "Platte", p, new Vector3(0.56f, 0.05f, 0.56f), M(i % 2 == 0 ? c : hell, 0.25f), false);
                        break;
                    default:
                        ArtLibrary.Box(_aussen, "Platte", p, new Vector3(0.56f, 0.05f, 0.56f), M(c, 0.2f), false).transform.rotation = jitter;
                        break;
                }
            }
        }

        // ── the ground of the lawn ──

        private void BuildGrund(string stil, string farbe)
        {
            if (_lawn == null) return;
            var size = new Vector2(LawnW + 0.8f, LawnD + 0.8f);
            var c = Farbe(farbe);
            Material m;
            switch (stil)
            {
                case "kies": m = ArtLibrary.PbrSlab("concrete", size, 1.2f, c, 0.05f, 0.9f); break;
                case "steinplatten": m = ArtLibrary.PbrSlab("paving", size, 1.6f, c, 0.12f, 0.8f); break;
                case "sand": m = ArtLibrary.Textured("tex_sand", size / 2.5f, c, 0.05f); break;
                case "waldboden": m = ArtLibrary.PbrSlab("leaves", size, 2.0f, Color.Lerp(c, Color.white, 0.25f), 0.05f, 0.8f); break;
                default: m = ArtLibrary.PbrSlab("grass", size, 2.4f, Color.Lerp(ArtLibrary.Hex("#9CC49E"), c, 0.55f), 0.04f, 0.7f); break;
            }
            _lawn.sharedMaterial = m;
            if (stil != "rasen_lang" && stil != "wildblumen") return;
            // tall grass and wildflowers: a few tufts and blooms along the lawn's edges (the middle stays free to build on)
            for (var i = 0; i < 26; i++)
            {
                var u = Mathf.Repeat(i * 0.6180339f, 1f);
                var edge = i % 4;
                var p = edge == 0 ? new Vector3(West + 0.35f + u * (LawnW - 0.7f), 0f, South + 0.3f)
                    : edge == 1 ? new Vector3(West + 0.3f, 0f, South + 0.35f + u * (LawnD - 0.7f))
                    : edge == 2 ? new Vector3(East - 0.3f, 0f, South + 0.35f + u * (LawnD - 0.7f))
                    : new Vector3(West + 0.35f + u * (LawnW - 0.7f), 0f, North - 0.3f);
                if (stil == "wildblumen")
                    ArtLibrary.Plant(_aussen, i % 3 == 0 ? "plant_tulips_red" : i % 3 == 1 ? "plant_daisies" : "plant_tulips_white", p, 0.34f, i * 41f);
                else
                    PlaceProp("grass_tuft", p, i * 53f, 0.55f);
            }
        }
    }
}
