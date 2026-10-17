// Stand-ins for the URP / Core RP API that Gooby.Unity and Gooby.Editor call. The compile checks only include this file
// when the imported project's Library/ScriptAssemblies is missing (fresh checkout, CI core-tests job); with an imported
// project, and in every real editor build, the code compiles against the actual packages. A newly used URP member needs
// a matching stub here, or the licence-free check fails on fresh checkouts.
using System.Collections.Generic;

namespace UnityEngine.Rendering
{
    public class VolumeParameter<T>
    {
        public T value;
        public bool overrideState;
        public virtual void Override(T x) { overrideState = true; value = x; }
    }

    public abstract class VolumeComponent : ScriptableObject
    {
        public bool active = true;
    }

    public class VolumeProfile : ScriptableObject
    {
        public List<VolumeComponent> components = new List<VolumeComponent>();
        public bool TryGet<T>(out T component) where T : VolumeComponent { component = null; return false; }
        public T Add<T>(bool overrides = false) where T : VolumeComponent => CreateInstance<T>();
    }

    public class Volume : MonoBehaviour
    {
        public bool isGlobal;
        public float priority;
        public float weight = 1f;
        public VolumeProfile sharedProfile;
        public VolumeProfile profile;
    }
}

namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode { None, Neutral, ACES }
    public enum DepthOfFieldMode { Off, Gaussian, Bokeh }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }

    public sealed class Bloom : VolumeComponent
    {
        public VolumeParameter<float> intensity = new VolumeParameter<float>(), threshold = new VolumeParameter<float>(), scatter = new VolumeParameter<float>();
        public VolumeParameter<Color> tint = new VolumeParameter<Color>();
    }

    public sealed class Tonemapping : VolumeComponent
    {
        public VolumeParameter<TonemappingMode> mode = new VolumeParameter<TonemappingMode>();
    }

    public sealed class ColorAdjustments : VolumeComponent
    {
        public VolumeParameter<float> postExposure = new VolumeParameter<float>(), contrast = new VolumeParameter<float>(), saturation = new VolumeParameter<float>(), hueShift = new VolumeParameter<float>();
        public VolumeParameter<Color> colorFilter = new VolumeParameter<Color>();
    }

    public sealed class ChannelMixer : VolumeComponent
    {
        public VolumeParameter<float> redOutRedIn = new VolumeParameter<float>(), redOutGreenIn = new VolumeParameter<float>(), redOutBlueIn = new VolumeParameter<float>();
        public VolumeParameter<float> greenOutRedIn = new VolumeParameter<float>(), greenOutGreenIn = new VolumeParameter<float>(), greenOutBlueIn = new VolumeParameter<float>();
        public VolumeParameter<float> blueOutRedIn = new VolumeParameter<float>(), blueOutGreenIn = new VolumeParameter<float>(), blueOutBlueIn = new VolumeParameter<float>();
    }

    public sealed class WhiteBalance : VolumeComponent
    {
        public VolumeParameter<float> temperature = new VolumeParameter<float>(), tint = new VolumeParameter<float>();
    }

    public sealed class Vignette : VolumeComponent
    {
        public VolumeParameter<float> intensity = new VolumeParameter<float>(), smoothness = new VolumeParameter<float>();
        public VolumeParameter<Color> color = new VolumeParameter<Color>();
    }

    public sealed class DepthOfField : VolumeComponent
    {
        public VolumeParameter<DepthOfFieldMode> mode = new VolumeParameter<DepthOfFieldMode>();
        public VolumeParameter<float> gaussianStart = new VolumeParameter<float>(), gaussianEnd = new VolumeParameter<float>(), gaussianMaxRadius = new VolumeParameter<float>();
        public VolumeParameter<float> focusDistance = new VolumeParameter<float>(), focalLength = new VolumeParameter<float>(), aperture = new VolumeParameter<float>();
        public VolumeParameter<int> bladeCount = new VolumeParameter<int>();
        public VolumeParameter<bool> highQualitySampling = new VolumeParameter<bool>();
    }

    public class UniversalAdditionalCameraData : MonoBehaviour
    {
        public bool renderPostProcessing, renderShadows, dithering;
        public AntialiasingMode antialiasing;
    }

    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera camera) => null;
    }

    public class UniversalAdditionalLightData : MonoBehaviour
    {
        public Vector2 lightCookieSize = Vector2.one, lightCookieOffset;
        public bool usePipelineSettings = true;
    }

    public static class LightExtensions
    {
        public static UniversalAdditionalLightData GetUniversalAdditionalLightData(this Light light) => null;
    }

    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public float renderScale = 1f, shadowDistance = 50f;
        public int msaaSampleCount = 1;
        public bool supportsHDR = true;
        public System.ReadOnlySpan<ScriptableRendererData> rendererDataList => new ScriptableRendererData[0];
        protected override RenderPipeline CreatePipeline() => null;
    }

    public abstract class ScriptableRendererData : ScriptableObject
    {
        public System.Collections.Generic.List<ScriptableRendererFeature> rendererFeatures { get; } = new System.Collections.Generic.List<ScriptableRendererFeature>();
    }

    public abstract class ScriptableRendererFeature : ScriptableObject
    {
        public bool isActive { get; private set; } = true;
        public void SetActive(bool active) => isActive = active;
    }
}
