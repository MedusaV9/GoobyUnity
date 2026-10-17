using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Einstellungen → Grafik → Qualität (board 07): "schoen" is the full board look (MSAA 4×, HDR, soft sun shadows,
    /// diorama depth of field, bloom); "sparsam" keeps the grade, palette and light budget but renders at a lower
    /// internal resolution without MSAA, bloom and depth of field, with hard, shorter shadows — for older phones and
    /// software rasterisers (QA's llvmpipe needed seconds per frame at full quality). "auto" picks sparsam on software
    /// renderers and phones with little memory. QA override: <c>-gooby-graphics sparsam|schoen</c>.
    /// </summary>
    public static class GraphicsQuality
    {
        public static string Active { get; private set; } = "schoen";

        /// <summary>False under "sparsam": SceneDressing leaves the depth of field off.</summary>
        public static bool DepthOfFieldAllowed => Active != "sparsam";

        /// <summary>Particle budget 0..1 (weather): "sparsam" draws fewer drops and flakes, still enough to read as rain.</summary>
        public static float ParticleFactor => Active == "sparsam" ? 0.7f : 1f;

        public static string Resolve(string preset)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "-gooby-graphics") preset = args[i + 1];
            if (preset == "sparsam" || preset == "schoen") return preset;
            var gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            var software = gpu.Contains("llvmpipe") || gpu.Contains("softpipe") || gpu.Contains("swiftshader") || gpu.Contains("lavapipe");
            var weakPhone = Application.isMobilePlatform && SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 2600;
            return software || weakPhone ? "sparsam" : "schoen";
        }

        public static void Apply(string preset, Camera camera, SceneDressing.Rig rig)
        {
            Active = Resolve(preset);
            var low = Active == "sparsam";
            // the pipeline asset is shared project data: only a player may retune it (the editor would save it to disk)
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = low ? 0.6f : 1f;
                urp.msaaSampleCount = low ? 1 : 4;
                urp.shadowDistance = low ? 30f : 90f;
                urp.supportsHDR = !low;
            }
            if (camera != null)
            {
                camera.allowHDR = !low;
                camera.allowMSAA = !low;
                var data = camera.GetUniversalAdditionalCameraData();
                data.antialiasing = low ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;
            }
            if (rig != null)
            {
                if (rig.Sun != null) rig.Sun.shadows = low ? LightShadows.Hard : LightShadows.Soft;
                if (rig.Profile != null && rig.Profile.TryGet<Bloom>(out var bloom)) bloom.active = !low;
                if (low && rig.DepthOfField != null) rig.DepthOfField.active = false;
            }
            QualitySettings.shadowCascades = low ? 1 : QualitySettings.shadowCascades;
            Debug.Log("[gfx] preset " + preset + " → " + Active + " (" + SystemInfo.graphicsDeviceName + ")");
        }
    }
}
