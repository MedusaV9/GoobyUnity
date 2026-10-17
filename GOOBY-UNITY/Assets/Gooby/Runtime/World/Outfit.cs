using System.Collections.Generic;
using Gooby.Core.Cosmetics;
using Gooby.Core.Json;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Wearables from the cosmetics catalog (93 items, five categories) built from primitives and hung on the
    /// avatar's bones, the way the Godot version assembled them from its `build` recipes: hats on the head top,
    /// glasses on the face, neck pieces at the head pivot, back pieces behind the chest. Fur colours repaint the
    /// palette texture (tools/blender/gooby_build/gooby_params.py PALETTE) so body, belly and inner ears change.
    /// Anchor heights are the recipe values × RIG_SCALE (0.7646): head top 0.96 m, head centre 0.68 m, head
    /// pivot 0.52 m — the avatar's root sits at the feet and looks along its local +Z.
    /// </summary>
    public static class Outfit
    {
        private const float HeadTopY = 0.962f, HeadCenterY = 0.681f, HeadPivotY = 0.524f, HeadRz = 0.271f, HeadRx = 0.293f;
        private static readonly Vector3 HeadTop = new Vector3(0f, HeadTopY, 0.015f);
        private static readonly Vector3 HeadCenter = new Vector3(0f, HeadCenterY, 0.015f);
        private static readonly Vector3 Face = new Vector3(0f, 0.672f, HeadRz + 0.03f);
        private static readonly Vector3 Neck = new Vector3(0f, HeadPivotY - 0.01f, 0.02f);
        private static readonly Vector3 Back = new Vector3(0f, 0.42f, -0.30f);
        private const string Prefix = "Outfit:";

        /// <summary>Palette cells of the avatar texture in reading order (4×4 grid, 64 px cells) — mirrors PALETTE_ORDER.</summary>
        private static readonly (string name, string hex)[] Palette =
        {
            ("body", "#F8EAD3"), ("belly", "#FFF9EC"), ("earInner", "#F4A6B4"), ("nose", "#E88BA0"),
            ("cheek", "#F7B9C4"), ("eye", "#2E2624"), ("eyeShine", "#FFFFFF"), ("pawPad", "#F3B7C3"),
            ("tooth", "#FFFFFF"), ("mouth", "#4A2B33"), ("sprout", "#7FCF9B"), ("sproutDark", "#5FB884"),
        };

        /// <summary>Rebuilds every worn piece from the equipped map (kategorie → id) and repaints the fur.</summary>
        public static void Apply(GoobyAvatar avatar, Dictionary<string, string> equipped)
        {
            if (avatar == null) return;
            Clear(avatar);
            foreach (var kv in equipped)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                var def = CosmeticsCatalog.ById(kv.Value);
                if (def == null) continue;
                if (def.Kategorie == CosmeticsCatalog.Fell) { ApplyFur(avatar, def); continue; }
                Build(avatar, def);
            }
            if (!equipped.TryGetValue(CosmeticsCatalog.Fell, out var fur) || string.IsNullOrEmpty(fur) || CosmeticsCatalog.ById(fur) == null)
                ApplyFur(avatar, CosmeticsCatalog.ById(CosmeticsState.StandardFell));
        }

        public static void Clear(GoobyAvatar avatar)
        {
            var doomed = new List<GameObject>();
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(Prefix)) doomed.Add(t.gameObject);
            foreach (var go in doomed) if (go != null) Object.Destroy(go);
        }

        // ── fur ──────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, Texture2D> FurTextures = new Dictionary<string, Texture2D>();

        /// <summary>Palette texture with body / belly / inner-ear cells taken from the fur's three colours.</summary>
        public static Texture2D FurPalette(CosmeticDef fur)
        {
            var key = fur?.Id ?? CosmeticsState.StandardFell;
            if (FurTextures.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "gooby_palette:" + key };
            var colors = new Color32[256 * 256];
            for (var i = 0; i < Palette.Length; i++)
            {
                var col = i % 4; var row = i / 4; // row 0 = top of the image
                var c = ArtLibrary.Hex(Palette[i].hex);
                if (fur != null && fur.Farben.Count >= 1 && Palette[i].name == "body") c = ArtLibrary.Hex(fur.Farben[0]);
                if (fur != null && fur.Farben.Count >= 2 && Palette[i].name == "belly") c = ArtLibrary.Hex(fur.Farben[1]);
                if (fur != null && fur.Farben.Count >= 3 && Palette[i].name == "earInner") c = ArtLibrary.Hex(fur.Farben[2]);
                Color32 c32 = c;
                for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                    colors[(255 - (row * 64 + y)) * 256 + col * 64 + x] = c32; // texture rows run bottom-up
            }
            var body = ArtLibrary.Hex(fur != null && fur.Farben.Count >= 1 ? fur.Farben[0] : Palette[0].hex);
            for (var i = Palette.Length; i < 16; i++) // unused cells keep the body colour like the Blender export
            {
                var col = i % 4; var row = i / 4;
                Color32 c32 = body;
                for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                    colors[(255 - (row * 64 + y)) * 256 + col * 64 + x] = c32;
            }
            tex.SetPixels32(colors);
            tex.Apply(false, true);
            FurTextures[key] = tex;
            return tex;
        }

        private static void ApplyFur(GoobyAvatar avatar, CosmeticDef fur)
        {
            var skin = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null) return;
            var tex = FurPalette(fur);
            var mat = skin.material; // instance: only this Gooby changes colour
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("baseColorTexture")) mat.SetTexture("baseColorTexture", tex);
        }

        // ── wearables ────────────────────────────────────────────────────────

        private static Color C(CosmeticDef def, int i) => ArtLibrary.Hex(def.Farben.Count > i ? def.Farben[i] : def.Farben.Count > 0 ? def.Farben[0] : "#FFFFFF");

        private static Material M(CosmeticDef def, int i, float smooth = 0.3f) => ArtLibrary.Solid(C(def, i), smooth);

        /// <summary>Builds the piece in a group at its anchor and hands the group to the bone so it rides the animation.</summary>
        private static void Build(GoobyAvatar avatar, CosmeticDef def)
        {
            var root = avatar.transform;
            string bone; Vector3 anchor;
            switch (def.Kategorie)
            {
                case "hut": bone = "head"; anchor = HeadTop; break;
                case "brille": bone = "head"; anchor = Face; break;
                case "hals": bone = "chest"; anchor = Neck; break;
                default: bone = "chest"; anchor = Back; break;
            }
            var group = new GameObject(Prefix + def.Id);
            group.transform.SetParent(root, false);
            group.transform.localPosition = anchor;
            group.transform.localRotation = Quaternion.identity;
            BuildLoose(group.transform, def);
            var target = avatar.Bone(bone) ?? root;
            group.transform.SetParent(target, true);
        }

        /// <summary>The piece alone under <paramref name="t"/>, around its anchor point (wardrobe pictures).</summary>
        public static void BuildLoose(Transform t, CosmeticDef def)
        {
            switch (def.Kategorie)
            {
                case "hut": BuildHat(t, def); break;
                case "brille": BuildGlasses(t, def); break;
                case "hals": BuildNeck(t, def); break;
                default: BuildBack(t, def); break;
            }
        }

        private static GameObject Box(Transform t, Vector3 local, Vector3 size, Material m, Vector3? euler = null)
        {
            var go = ArtLibrary.Box(t, "P", Vector3.zero, size, m, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            return go;
        }

        private static GameObject Ball(Transform t, Vector3 local, float r, Material m, Vector3? scale = null)
        {
            var go = ArtLibrary.Sphere(t, "P", Vector3.zero, r, m, false);
            go.transform.localPosition = local;
            if (scale.HasValue) go.transform.localScale = Vector3.Scale(go.transform.localScale, scale.Value);
            return go;
        }

        private static GameObject Tube(Transform t, Vector3 local, float r, float h, Material m, Vector3? euler = null)
        {
            var go = ArtLibrary.Cylinder(t, "P", Vector3.zero, r, h, m, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            return go;
        }

        /// <summary>Cone standing on its base (apex up), as a tapered cylinder mesh.</summary>
        private static GameObject Cone(Transform t, Vector3 local, float r, float h, Material m, Vector3? euler = null)
        {
            var go = new GameObject("P");
            go.transform.SetParent(t, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = ConeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(r, h, r);
            return go;
        }

        private static Mesh _cone;

        /// <summary>Unit cone: base radius 1 at y=0, apex at y=1, 24 segments, flat-shaded base.</summary>
        private static Mesh ConeMesh
        {
            get
            {
                if (_cone != null) return _cone;
                const int n = 24;
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
                for (var i = 0; i < n; i++)
                {
                    var a0 = i / (float)n * Mathf.PI * 2f; var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                    var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var nrm = ((p0 + p1) * 0.5f + Vector3.up * 0.5f).normalized;
                    var b = verts.Count;
                    verts.Add(p0); verts.Add(Vector3.up); verts.Add(p1);
                    norms.Add(nrm); norms.Add(nrm); norms.Add(nrm);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    b = verts.Count;
                    verts.Add(p1); verts.Add(Vector3.zero); verts.Add(p0);
                    norms.Add(Vector3.down); norms.Add(Vector3.down); norms.Add(Vector3.down);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                }
                _cone = new Mesh { name = "OutfitCone" };
                _cone.SetVertices(verts); _cone.SetNormals(norms); _cone.SetTriangles(tris, 0);
                _cone.RecalculateBounds();
                return _cone;
            }
        }

        private static void BuildHat(Transform t, CosmeticDef def)
        {
            var p = def.Params;
            switch (def.Build)
            {
                case "kegel":
                    Cone(t, new Vector3(0f, -0.02f, 0f), 0.13f, 0.30f, M(def, 0));
                    Ball(t, new Vector3(0f, 0.29f, 0f), 0.035f, M(def, 1));
                    Tube(t, new Vector3(0f, -0.02f, 0f), 0.135f, 0.02f, M(def, 1));
                    break;
                case "kappe":
                    Ball(t, new Vector3(0f, -0.09f, 0f), 0.235f, M(def, 0), new Vector3(1.02f, 0.62f, 1f));
                    if (J.Str(p["kappe"]) == "schirm") Box(t, new Vector3(0f, -0.11f, 0.24f), new Vector3(0.24f, 0.02f, 0.16f), M(def, 1));
                    else Tube(t, new Vector3(0f, -0.13f, 0f), 0.24f, 0.05f, M(def, 1));
                    break;
                case "zylinder":
                    var kh = (float)J.NumOr(p["krone_h"], 0.18); var kr = (float)J.NumOr(p["krone_r"], 0.1); var br = (float)J.NumOr(p["krempe_r"], 0.15);
                    Tube(t, new Vector3(0f, -0.01f, 0f), br * 1.4f, 0.02f, M(def, 0));
                    Tube(t, new Vector3(0f, kh * 0.7f, 0f), kr * 1.4f, kh * 1.4f, M(def, 0));
                    Tube(t, new Vector3(0f, 0.03f, 0f), kr * 1.42f, 0.03f, M(def, 1));
                    break;
                case "krone":
                    var zacken = Mathf.Max(3, J.Int(p["zacken"], 5)); var radius = (float)J.NumOr(p["radius"], 0.09) * 1.5f; var hoehe = (float)J.NumOr(p["hoehe"], 0.08) * 1.4f;
                    Tube(t, new Vector3(0f, -0.03f, 0f), radius, hoehe * 0.6f, M(def, 0, 0.7f));
                    for (var i = 0; i < zacken; i++)
                    {
                        var a = i / (float)zacken * Mathf.PI * 2f;
                        Cone(t, new Vector3(Mathf.Cos(a) * radius * 0.85f, -0.03f + hoehe * 0.3f, Mathf.Sin(a) * radius * 0.85f), 0.03f, hoehe * 0.8f, M(def, 0, 0.7f));
                        if (i % 2 == 0) Ball(t, new Vector3(Mathf.Cos(a) * radius, -0.03f + hoehe * 0.25f, Mathf.Sin(a) * radius), 0.016f, M(def, 1, 0.8f));
                    }
                    break;
                case "kranz":
                    for (var i = 0; i < 12; i++)
                    {
                        var a = i / 12f * Mathf.PI * 2f;
                        var pos = new Vector3(Mathf.Cos(a) * 0.2f, -0.06f, Mathf.Sin(a) * 0.2f);
                        Ball(t, pos, 0.028f, M(def, i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : 2));
                    }
                    Tube(t, new Vector3(0f, -0.06f, 0f), 0.2f, 0.02f, M(def, 0));
                    break;
                case "chefhut":
                    Tube(t, new Vector3(0f, 0.02f, 0f), 0.16f, 0.08f, M(def, 1));
                    Ball(t, new Vector3(0f, 0.14f, 0f), 0.19f, M(def, 0), new Vector3(1f, 0.75f, 1f));
                    break;
                case "dreispitz":
                    Box(t, new Vector3(0f, -0.02f, 0f), new Vector3(0.46f, 0.05f, 0.34f), M(def, 0), new Vector3(0f, 0f, 0f));
                    Box(t, new Vector3(0f, 0.04f, 0f), new Vector3(0.3f, 0.09f, 0.22f), M(def, 0));
                    Ball(t, new Vector3(0f, 0.06f, 0.11f), 0.03f, M(def, 1));
                    break;
                case "helm":
                    Ball(t, new Vector3(0f, -0.08f, 0f), 0.245f, M(def, 0, 0.7f), new Vector3(1f, 0.7f, 1f));
                    if (J.Str(p["helm"]) == "hoerner")
                        foreach (var s in new[] { -1f, 1f }) Cone(t, new Vector3(s * 0.2f, -0.02f, 0f), 0.04f, 0.16f, M(def, 1), new Vector3(0f, 0f, -s * 40f));
                    else Tube(t, new Vector3(0f, -0.15f, 0f), 0.25f, 0.03f, M(def, 1, 0.6f));
                    break;
                case "kuerbis":
                    Ball(t, new Vector3(0f, 0.02f, 0f), 0.15f, M(def, 0), new Vector3(1.15f, 0.85f, 1.15f));
                    Tube(t, new Vector3(0f, 0.16f, 0f), 0.02f, 0.06f, M(def, 1));
                    break;
                case "ananas":
                    Ball(t, new Vector3(0f, 0.08f, 0f), 0.13f, M(def, 0), new Vector3(1f, 1.3f, 1f));
                    for (var i = 0; i < 5; i++)
                    {
                        var a = i / 5f * Mathf.PI * 2f;
                        Cone(t, new Vector3(Mathf.Cos(a) * 0.04f, 0.22f, Mathf.Sin(a) * 0.04f), 0.03f, 0.14f, M(def, 1), new Vector3(Mathf.Sin(a) * 25f, 0f, -Mathf.Cos(a) * 25f));
                    }
                    break;
                case "torte":
                    Tube(t, new Vector3(0f, 0.0f, 0f), 0.16f, 0.07f, M(def, 0));
                    Tube(t, new Vector3(0f, 0.07f, 0f), 0.11f, 0.06f, M(def, 1));
                    Ball(t, new Vector3(0f, 0.13f, 0f), 0.03f, M(def, 2));
                    break;
                default: // any hat recipe without a special build: a soft cap
                    Ball(t, new Vector3(0f, -0.08f, 0f), 0.23f, M(def, 0), new Vector3(1f, 0.6f, 1f));
                    break;
            }
        }

        private static void BuildGlasses(Transform t, CosmeticDef def)
        {
            var frame = M(def, 0); var lens = ArtLibrary.Transparent(new Color(C(def, 1).r, C(def, 1).g, C(def, 1).b, 0.55f));
            var eyeX = 0.115f;
            switch (def.Build)
            {
                case "augenklappe":
                    Ball(t, new Vector3(eyeX, 0f, 0f), 0.055f, frame, new Vector3(1f, 1f, 0.35f));
                    Box(t, new Vector3(0f, 0.02f, -0.12f), new Vector3(0.5f, 0.02f, 0.02f), M(def, 1), new Vector3(0f, 0f, 12f));
                    break;
                case "monokel":
                    Ball(t, new Vector3(eyeX, 0f, 0f), 0.06f, frame, new Vector3(1f, 1f, 0.2f));
                    Ball(t, new Vector3(eyeX, 0f, 0.004f), 0.05f, lens, new Vector3(1f, 1f, 0.15f));
                    Tube(t, new Vector3(eyeX + 0.05f, -0.08f, 0f), 0.004f, 0.14f, frame, new Vector3(0f, 0f, 20f));
                    break;
                case "visier":
                    Box(t, new Vector3(0f, 0.01f, 0f), new Vector3(0.36f, 0.09f, 0.03f), lens);
                    Box(t, new Vector3(0f, 0.065f, 0f), new Vector3(0.38f, 0.02f, 0.035f), frame);
                    break;
                case "goggles":
                    foreach (var s in new[] { -1f, 1f })
                    {
                        Ball(t, new Vector3(s * eyeX, 0f, 0f), 0.068f, frame, new Vector3(1f, 1f, 0.5f));
                        Ball(t, new Vector3(s * eyeX, 0f, 0.012f), 0.05f, lens, new Vector3(1f, 1f, 0.3f));
                    }
                    Box(t, new Vector3(0f, 0f, 0f), new Vector3(0.08f, 0.02f, 0.02f), frame);
                    Box(t, new Vector3(0f, 0f, -0.16f), new Vector3(0.52f, 0.03f, 0.02f), M(def, 2));
                    break;
                case "scherzbrille":
                    foreach (var s in new[] { -1f, 1f })
                    {
                        Ball(t, new Vector3(s * eyeX, 0f, 0f), 0.06f, frame, new Vector3(1f, 1f, 0.18f));
                        Ball(t, new Vector3(s * eyeX, 0f, 0.004f), 0.048f, lens, new Vector3(1f, 1f, 0.12f));
                    }
                    Box(t, new Vector3(0f, 0f, 0f), new Vector3(0.06f, 0.012f, 0.012f), frame);
                    Ball(t, new Vector3(0f, -0.05f, 0.02f), 0.035f, M(def, 1));
                    Box(t, new Vector3(0f, -0.085f, 0.01f), new Vector3(0.12f, 0.02f, 0.03f), ArtLibrary.Solid(ArtLibrary.Hex("#3A2E2E"), 0.2f));
                    break;
                default: // "brille": round or square frames
                    var round = J.Str(p_form(def)) != "eckig";
                    foreach (var s in new[] { -1f, 1f })
                    {
                        if (round)
                        {
                            Ball(t, new Vector3(s * eyeX, 0f, 0f), 0.06f, frame, new Vector3(1f, 1f, 0.18f));
                            Ball(t, new Vector3(s * eyeX, 0f, 0.004f), 0.048f, lens, new Vector3(1f, 1f, 0.12f));
                        }
                        else
                        {
                            Box(t, new Vector3(s * eyeX, 0f, 0f), new Vector3(0.12f, 0.1f, 0.012f), frame);
                            Box(t, new Vector3(s * eyeX, 0f, 0.004f), new Vector3(0.1f, 0.08f, 0.012f), lens);
                        }
                    }
                    Box(t, new Vector3(0f, 0f, 0f), new Vector3(0.06f, 0.012f, 0.012f), frame);
                    foreach (var s in new[] { -1f, 1f }) Box(t, new Vector3(s * 0.2f, 0.01f, -0.1f), new Vector3(0.012f, 0.012f, 0.22f), frame);
                    break;
            }
        }

        private static Newtonsoft.Json.Linq.JToken p_form(CosmeticDef def) => def.Params["form"];

        private static void BuildNeck(Transform t, CosmeticDef def)
        {
            switch (def.Build)
            {
                case "fliege":
                    foreach (var s in new[] { -1f, 1f }) Box(t, new Vector3(s * 0.055f, 0f, 0.2f), new Vector3(0.08f, 0.06f, 0.03f), M(def, 0), new Vector3(0f, 0f, s * 8f));
                    Ball(t, new Vector3(0f, 0f, 0.21f), 0.022f, M(def, 1));
                    break;
                case "halsband":
                    Tube(t, Vector3.zero, 0.25f, 0.035f, M(def, 0));
                    Ball(t, new Vector3(0f, -0.04f, 0.24f), 0.03f, M(def, 1, 0.8f));
                    break;
                case "kopfhoerer":
                    Tube(t, new Vector3(0f, 0.36f, 0f), 0.3f, 0.025f, M(def, 0), new Vector3(90f, 0f, 0f));
                    foreach (var s in new[] { -1f, 1f }) Ball(t, new Vector3(s * 0.3f, 0.2f, 0.0f), 0.07f, M(def, 1), new Vector3(0.5f, 1f, 1f));
                    break;
                case "kragen":
                    Tube(t, new Vector3(0f, 0.01f, 0f), 0.3f, 0.03f, M(def, 0));
                    for (var i = 0; i < 10; i++)
                    {
                        var a = i / 10f * Mathf.PI * 2f;
                        Ball(t, new Vector3(Mathf.Cos(a) * 0.3f, 0.01f, Mathf.Sin(a) * 0.3f), 0.04f, M(def, i % 2 == 0 ? 0 : 1), new Vector3(1f, 0.5f, 1f));
                    }
                    break;
                case "krawatte":
                    Box(t, new Vector3(0f, -0.02f, 0.22f), new Vector3(0.05f, 0.05f, 0.03f), M(def, 1));
                    Box(t, new Vector3(0f, -0.14f, 0.26f), new Vector3(0.07f, 0.22f, 0.02f), M(def, 0), new Vector3(-12f, 0f, 0f));
                    break;
                case "tuch":
                    Tube(t, Vector3.zero, 0.25f, 0.04f, M(def, 0));
                    Box(t, new Vector3(0f, -0.1f, 0.2f), new Vector3(0.16f, 0.16f, 0.02f), M(def, 1), new Vector3(-15f, 0f, 45f));
                    break;
                case "umhang":
                    Tube(t, Vector3.zero, 0.25f, 0.035f, M(def, 1));
                    Box(t, new Vector3(0f, -0.26f, -0.27f), new Vector3(0.5f, 0.5f, 0.02f), M(def, 0), new Vector3(8f, 0f, 0f));
                    break;
                default: // "schal": thick ring with a hanging tail
                    Tube(t, new Vector3(0f, 0.01f, 0f), 0.27f, 0.09f, M(def, 0));
                    Box(t, new Vector3(0.1f, -0.16f, 0.2f), new Vector3(0.09f, 0.26f, 0.05f), M(def, 0), new Vector3(-10f, 0f, 0f));
                    Box(t, new Vector3(0.1f, -0.3f, 0.22f), new Vector3(0.09f, 0.04f, 0.05f), M(def, 1), new Vector3(-10f, 0f, 0f));
                    break;
            }
        }

        private static void BuildBack(Transform t, CosmeticDef def)
        {
            switch (def.Build)
            {
                case "ballon":
                    Tube(t, new Vector3(0.12f, 0.3f, 0f), 0.004f, 0.6f, M(def, 1));
                    Ball(t, new Vector3(0.12f, 0.68f, 0f), 0.14f, M(def, 0, 0.6f), new Vector3(1f, 1.15f, 1f));
                    break;
                case "brett":
                    Box(t, new Vector3(0f, 0.1f, -0.05f), new Vector3(0.28f, 0.9f, 0.04f), M(def, 0), new Vector3(0f, 0f, 12f));
                    Box(t, new Vector3(0f, 0.1f, -0.03f), new Vector3(0.06f, 0.8f, 0.03f), M(def, 1), new Vector3(0f, 0f, 12f));
                    break;
                case "fluegel":
                    var fee = J.Str(def.Params["form"]) == "fee";
                    foreach (var s in new[] { -1f, 1f })
                    {
                        Ball(t, new Vector3(s * 0.22f, 0.12f, -0.04f), 0.2f, M(def, 0), new Vector3(1f, fee ? 1.3f : 0.6f, 0.15f)).transform.localRotation = Quaternion.Euler(0f, 0f, s * (fee ? -25f : -15f));
                        if (fee) Ball(t, new Vector3(s * 0.16f, -0.1f, -0.04f), 0.12f, M(def, 1), new Vector3(1f, 1.2f, 0.15f));
                    }
                    break;
                case "gitarre":
                    Box(t, new Vector3(0f, 0f, -0.06f), new Vector3(0.3f, 0.38f, 0.08f), M(def, 0), new Vector3(0f, 0f, 20f));
                    Box(t, new Vector3(-0.12f, 0.36f, -0.06f), new Vector3(0.05f, 0.4f, 0.04f), M(def, 1), new Vector3(0f, 0f, 20f));
                    break;
                case "jetpack":
                    foreach (var s in new[] { -1f, 1f })
                    {
                        Tube(t, new Vector3(s * 0.11f, 0f, -0.04f), 0.08f, 0.34f, M(def, 0, 0.7f));
                        Cone(t, new Vector3(s * 0.11f, -0.25f, -0.04f), 0.07f, 0.09f, M(def, 1), new Vector3(180f, 0f, 0f));
                    }
                    Box(t, new Vector3(0f, 0.05f, -0.04f), new Vector3(0.14f, 0.1f, 0.06f), M(def, 2));
                    break;
                case "panzer":
                    Ball(t, new Vector3(0f, 0.02f, 0.02f), 0.3f, M(def, 0), new Vector3(1f, 0.75f, 0.6f));
                    Tube(t, new Vector3(0f, 0.02f, 0.1f), 0.3f, 0.03f, M(def, 2), new Vector3(90f, 0f, 0f));
                    break;
                case "propeller":
                    Box(t, new Vector3(0f, 0f, -0.04f), new Vector3(0.22f, 0.26f, 0.1f), M(def, 0, 0.6f));
                    Tube(t, new Vector3(0f, 0f, -0.12f), 0.02f, 0.06f, M(def, 1), new Vector3(90f, 0f, 0f));
                    Box(t, new Vector3(0f, 0f, -0.15f), new Vector3(0.42f, 0.04f, 0.01f), M(def, 2));
                    Box(t, new Vector3(0f, 0f, -0.15f), new Vector3(0.04f, 0.42f, 0.01f), M(def, 2));
                    break;
                case "rueckenschleife":
                    foreach (var s in new[] { -1f, 1f }) Box(t, new Vector3(s * 0.12f, 0.02f, -0.03f), new Vector3(0.18f, 0.12f, 0.05f), M(def, 0), new Vector3(0f, 0f, s * 12f));
                    Ball(t, new Vector3(0f, 0.02f, -0.04f), 0.04f, M(def, 1));
                    Box(t, new Vector3(-0.06f, -0.14f, -0.03f), new Vector3(0.06f, 0.2f, 0.03f), M(def, 0), new Vector3(0f, 0f, -12f));
                    Box(t, new Vector3(0.06f, -0.14f, -0.03f), new Vector3(0.06f, 0.2f, 0.03f), M(def, 0), new Vector3(0f, 0f, 12f));
                    break;
                case "schirm":
                    Tube(t, new Vector3(0.08f, 0.08f, -0.03f), 0.012f, 0.7f, M(def, 2), new Vector3(0f, 0f, 20f));
                    Ball(t, new Vector3(-0.05f, 0.46f, -0.03f), 0.16f, M(def, 0), new Vector3(1f, 0.5f, 1f));
                    break;
                case "schwert":
                    Box(t, new Vector3(0.05f, 0.1f, -0.04f), new Vector3(0.05f, 0.62f, 0.015f), M(def, 0, 0.7f), new Vector3(0f, 0f, 18f));
                    Box(t, new Vector3(0.14f, -0.16f, -0.04f), new Vector3(0.16f, 0.03f, 0.04f), M(def, 2, 0.8f), new Vector3(0f, 0f, 18f));
                    Box(t, new Vector3(0.17f, -0.26f, -0.04f), new Vector3(0.04f, 0.14f, 0.04f), M(def, 1), new Vector3(0f, 0f, 18f));
                    break;
                default: // "rucksack": a small pack with a flap
                    Box(t, new Vector3(0f, 0f, -0.03f), new Vector3(0.26f, 0.3f, 0.14f), M(def, 0));
                    Box(t, new Vector3(0f, 0.12f, -0.06f), new Vector3(0.27f, 0.08f, 0.15f), M(def, 1));
                    Ball(t, new Vector3(0f, 0.06f, -0.11f), 0.025f, M(def, 2));
                    break;
            }
        }
    }
}
