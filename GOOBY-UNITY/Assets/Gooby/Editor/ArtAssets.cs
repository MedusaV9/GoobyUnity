using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gooby.Editor
{
    /// <summary>
    /// Generates the serialized rendering assets the runtime look depends on. URP strips every shader variant
    /// and post-processing shader that no *asset* in the project references (GlobalSettings: StripUnusedVariants /
    /// StripUnusedPostProcessingVariants), so materials and the volume profile created purely from code lose
    /// alpha-clip, depth of field, … in player builds. Running this once (idempotent) and committing the output
    /// keeps the variants in the build:
    ///
    ///   Unity -batchmode -quit -projectPath GOOBY-UNITY -executeMethod Gooby.Editor.ArtAssets.Generate
    ///
    /// Output: Assets/Gooby/Art/Resources/gooby-art/{GoobyLook.asset, materials/*.mat}
    /// </summary>
    public static class ArtAssets
    {
        private const string Root = "Assets/Gooby/Art/Resources/gooby-art";
        private const string MaterialsDir = Root + "/materials";

        [MenuItem("GOOBY/Generate art assets")]
        public static void Generate()
        {
            Directory.CreateDirectory(MaterialsDir);
            CreateProfile();
            CreateMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[art-assets] generated volume profile + materials under " + Root);
        }

        private static void CreateProfile()
        {
            var path = Root + "/GoobyLook.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            // one override of every effect the runtime rig drives (SceneDressing) so URP keeps their shaders
            Ensure<Bloom>(profile, path, b => { b.intensity.Override(0.35f); b.threshold.Override(1.05f); b.scatter.Override(0.65f); });
            Ensure<Tonemapping>(profile, path, t => t.mode.Override(TonemappingMode.Neutral));
            Ensure<ColorAdjustments>(profile, path, c => { c.postExposure.Override(0.12f); c.contrast.Override(6f); c.saturation.Override(12f); });
            Ensure<WhiteBalance>(profile, path, w => w.temperature.Override(8f));
            Ensure<Vignette>(profile, path, v => { v.intensity.Override(0.22f); v.smoothness.Override(0.55f); });
            Ensure<DepthOfField>(profile, path, d =>
            {
                d.mode.Override(DepthOfFieldMode.Gaussian);
                d.gaussianStart.Override(9f);
                d.gaussianEnd.Override(16f);
                d.gaussianMaxRadius.Override(1f);
                d.highQualitySampling.Override(true);
            });
            EditorUtility.SetDirty(profile);
        }

        private static void Ensure<T>(VolumeProfile profile, string path, System.Action<T> init) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) { existing.active = true; return; }
            var c = profile.Add<T>(true);
            init(c);
            AssetDatabase.AddObjectToAsset(c, path);
        }

        private static void CreateMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            var opaque = Material("lit_opaque", lit);
            opaque.SetFloat("_Smoothness", 0.15f);

            var cutout = Material("lit_cutout", lit);
            cutout.SetFloat("_AlphaClip", 1f);
            cutout.SetFloat("_Cutoff", 0.45f);
            cutout.SetFloat("_Cull", (float)CullMode.Off);
            cutout.SetFloat("_Smoothness", 0.05f);
            cutout.EnableKeyword("_ALPHATEST_ON");
            cutout.renderQueue = (int)RenderQueue.AlphaTest;

            var transparent = Material("lit_transparent", lit);
            transparent.SetFloat("_Surface", 1f);
            transparent.SetFloat("_Blend", 0f);
            transparent.SetFloat("_ZWrite", 0f);
            transparent.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            transparent.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            transparent.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            transparent.renderQueue = (int)RenderQueue.Transparent;

            // tinted CC0 surfaces with a tangent-space normal map (ArtLibrary.Pbr / MaterialDresser): the _NORMALMAP
            // variant only survives stripping when an asset references a normal texture
            var pbr = Material("lit_pbr", lit);
            pbr.SetFloat("_Smoothness", 0.2f);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/pbr/plaster_n.png");
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/pbr/plaster_c.jpg");
            if (normal != null) { pbr.SetTexture("_BumpMap", normal); pbr.SetFloat("_BumpScale", 1f); pbr.EnableKeyword("_NORMALMAP"); }
            else Debug.LogWarning("[art-assets] pbr/plaster_n.png missing — run tools/art/fetch_pbr.py first");
            if (color != null) pbr.SetTexture("_BaseMap", color);

            Material("unlit_backdrop", unlit);
            var emissive = Material("lit_emissive", lit);
            emissive.EnableKeyword("_EMISSION");
            emissive.SetColor("_EmissionColor", new Color(1f, 0.9f, 0.7f));
            emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // Effects.cs: soft alpha-blended puffs (dust, petals, exhaust) and additive glints (sparkles, fireflies).
            // The particle shader only ships when a material asset references it.
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? unlit;
            var puff = Material("particle_alpha", particles);
            puff.SetFloat("_Surface", 1f);
            puff.SetFloat("_Blend", 0f);
            puff.SetFloat("_ZWrite", 0f);
            puff.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            puff.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            puff.SetFloat("_Cull", (float)CullMode.Off);
            puff.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            puff.renderQueue = (int)RenderQueue.Transparent;
            var glint = Material("particle_additive", particles);
            glint.SetFloat("_Surface", 1f);
            glint.SetFloat("_Blend", 2f);
            glint.SetFloat("_ZWrite", 0f);
            glint.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glint.SetFloat("_DstBlend", (float)BlendMode.One);
            glint.SetFloat("_Cull", (float)CullMode.Off);
            glint.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glint.renderQueue = (int)RenderQueue.Transparent + 10;
        }

        private static Material Material(string name, Shader shader)
        {
            var path = MaterialsDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
