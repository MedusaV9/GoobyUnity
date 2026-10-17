using System.Collections.Generic;
using Gooby.Core.Meta;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Gooby.Unity.App
{
    /// <summary>
    /// The colour-vision aid of the settings (port of color_filter.gd, which ran one screen-space pass over UI and 3D):
    /// the 3D image goes through a global volume of its own — URP's channel mixer carries the daltonising matrix of
    /// <see cref="ColorVision"/>, colour adjustments the high-contrast boost — and the UI panel through a UI Toolkit
    /// filter (Hidden/Gooby/UIColorVision, matrix and contrast in one pass). Both off costs nothing: no volume weight,
    /// no filter on the panel.
    /// </summary>
    public static class ColorVisionFilter
    {
        /// <summary>Above the look's PostVolume (priority 10), so the aid wins wherever both set a value.</summary>
        public const float VolumePriority = 50f;
        /// <summary>URP contrast (−100..100 on a log curve) that reads like color_filter.gd's 1.22 around mid grey.</summary>
        public const float HighContrastUrp = 26f;

        private static Volume _volume;
        private static ChannelMixer _mixer;
        private static ColorAdjustments _adjust;
        private static Material _uiMaterial;
        private static FilterFunctionDefinition _uiDefinition;

        /// <summary>Applies the settings to the world's volumes and the UI panel; call at boot and on every change.</summary>
        public static void Apply(AppSettings settings, Transform host, VisualElement uiRoot)
        {
            if (settings == null) return;
            var mode = ColorVision.Normalize(settings.ColorVisionMode);
            var contrast = settings.HighContrast;
            Apply3D(host, mode, contrast);
            ApplyUi(uiRoot, mode, contrast);
            Debug.Log("[look] colour vision " + mode + (contrast ? " + high contrast" : ""));
        }

        private static void Apply3D(Transform host, string mode, bool contrast)
        {
            var on = ColorVision.Active(mode, contrast);
            if (_volume == null)
            {
                if (!on) return;
                var go = new GameObject("ColorVisionVolume");
                if (host != null) go.transform.SetParent(host, false);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = VolumePriority;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "ColorVision (runtime)";
                _mixer = profile.Add<ChannelMixer>(true);
                _adjust = profile.Add<ColorAdjustments>(false);
                _volume.sharedProfile = profile;
            }
            _volume.weight = on ? 1f : 0f;
            var m = ColorVision.Matrix(mode);
            float P(int i) => (float)(m[i] * 100.0);
            _mixer.active = mode != ColorVision.Aus;
            _mixer.redOutRedIn.Override(P(0)); _mixer.redOutGreenIn.Override(P(1)); _mixer.redOutBlueIn.Override(P(2));
            _mixer.greenOutRedIn.Override(P(3)); _mixer.greenOutGreenIn.Override(P(4)); _mixer.greenOutBlueIn.Override(P(5));
            _mixer.blueOutRedIn.Override(P(6)); _mixer.blueOutGreenIn.Override(P(7)); _mixer.blueOutBlueIn.Override(P(8));
            _adjust.active = contrast;
            _adjust.contrast.Override(HighContrastUrp);
        }

        private static void ApplyUi(VisualElement root, string mode, bool contrast)
        {
            if (root == null) return;
            if (!ColorVision.Active(mode, contrast) || !EnsureUiFilter())
            {
                root.style.filter = new StyleList<FilterFunction>(StyleKeyword.None);
                return;
            }
            var m = ColorVision.Matrix(mode);
            _uiMaterial.SetVector("_RowR", new Vector4((float)m[0], (float)m[1], (float)m[2], 0f));
            _uiMaterial.SetVector("_RowG", new Vector4((float)m[3], (float)m[4], (float)m[5], 0f));
            _uiMaterial.SetVector("_RowB", new Vector4((float)m[6], (float)m[7], (float)m[8], 0f));
            _uiMaterial.SetFloat("_Contrast", contrast ? (float)ColorVision.HighContrast : 1f);
            root.style.filter = new StyleList<FilterFunction>(new List<FilterFunction> { new FilterFunction(_uiDefinition) });
        }

        private static bool EnsureUiFilter()
        {
            if (_uiDefinition != null) return true;
            var shader = Resources.Load<Shader>("gooby-ui/Shaders/UIColorVision");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[look] UIColorVision shader missing — the colour-vision aid covers the 3D image only");
                return false;
            }
            _uiMaterial = new Material(shader) { name = "UIColorVision (runtime)" };
            _uiDefinition = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
            _uiDefinition.name = "gooby-colorvision";
            _uiDefinition.filterName = "gooby-colorvision";
            _uiDefinition.passes = new[] { new PostProcessingPass { material = _uiMaterial, passIndex = 0 } };
            return true;
        }
    }
}
