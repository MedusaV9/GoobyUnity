using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The studio light every minigame 3D set shares (<see cref="MgStage3D"/>, <see cref="MgBuddy3D"/>): the first lease
    /// pauses the world's suns and puts up a warm key from the upper left (the boards' light), a cool fill and a soft
    /// trilight ambience; the last release gives the world its suns and ambience back. Leases nest, so a stage and the
    /// companion Gooby can live at the same time and end in any order.
    /// </summary>
    public static class MgStudio
    {
        private static int _leases;
        private static GameObject _rig;
        private static readonly List<(Light light, bool enabled)> WorldLights = new List<(Light, bool)>();
        private static UnityEngine.Rendering.AmbientMode _mode;
        private static Color _sky, _equator, _ground, _flat;
        private static float _intensity;
        private static bool _fog;

        public static bool Active => _leases > 0;

        public static void Acquire()
        {
            if (++_leases == 1)
            {
                WorldLights.Clear();
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { WorldLights.Add((l, l.enabled)); l.enabled = false; }
                _mode = RenderSettings.ambientMode;
                _sky = RenderSettings.ambientSkyColor;
                _equator = RenderSettings.ambientEquatorColor;
                _ground = RenderSettings.ambientGroundColor;
                _flat = RenderSettings.ambientLight;
                _intensity = RenderSettings.ambientIntensity;
                _fog = RenderSettings.fog;
            }
            // the rig is rebuilt when a scene unload took it while a lease was still out
            if (_rig != null) return;
            _rig = new GameObject("MgStudio");
            Object.DontDestroyOnLoad(_rig);
            Key("MgKey", new Color(1f, 0.95f, 0.86f), 1.15f, Quaternion.Euler(42f, 28f, 0f), true);
            Key("MgFill", new Color(0.78f, 0.86f, 1f), 0.35f, Quaternion.Euler(20f, -140f, 0f), false);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.84f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.86f, 0.82f, 0.76f);
            RenderSettings.ambientGroundColor = new Color(0.55f, 0.52f, 0.46f);
            RenderSettings.fog = false;
            Paint();
        }

        /// <summary>A moonlit studio (the ranch at night): a cool dim key and fill, blue ambience. <see cref="Day"/> undoes it.</summary>
        public static void Night()
        {
            _night = true;
            Paint();
        }

        /// <summary>Back to the boards' daylight (another set leases the studio while a moonlit one is covered).</summary>
        public static void Day()
        {
            _night = false;
            Paint();
        }

        /// <summary>
        /// The weather over a set (wetter_fx.gd light profile): the key × <paramref name="sun"/>, the ambience ×
        /// <paramref name="ambient"/>, both greyed by the cloud cover <paramref name="grey"/> (the sun colour toward a cool
        /// white like ranch_wetter_controller.gd), and <paramref name="flash"/> added to the key for a lightning frame.
        /// (1, 1, 0, 0) is clear.
        /// </summary>
        public static void Weather(float sun, float ambient, float flash = 0f, float grey = 0f)
        {
            _sun = Mathf.Clamp(sun, 0.1f, 1.5f);
            _ambient = Mathf.Clamp(ambient, 0.1f, 1.5f);
            _flash = Mathf.Max(0f, flash);
            _grey = Mathf.Clamp01(grey);
            Paint();
        }

        /// <summary>The key light's intensity now (QA: night, weather and flash included; 0 without a rig).</summary>
        public static float KeyIntensity
        {
            get
            {
                if (_rig == null) return 0f;
                foreach (var l in _rig.GetComponentsInChildren<Light>()) if (l.name == "MgKey") return l.intensity;
                return 0f;
            }
        }

        private static bool _night;
        private static float _sun = 1f, _ambient = 1f, _flash, _grey;

        private static void Paint()
        {
            if (_rig == null) return;
            foreach (var l in _rig.GetComponentsInChildren<Light>())
            {
                var key = l.name == "MgKey";
                if (_night)
                {
                    l.color = key ? new Color(0.66f, 0.74f, 1f) : new Color(0.46f, 0.52f, 0.86f);
                    l.intensity = key ? 0.62f : 0.28f;
                }
                else
                {
                    l.color = key ? new Color(1f, 0.95f, 0.86f) : new Color(0.78f, 0.86f, 1f);
                    l.intensity = key ? 1.15f : 0.35f;
                }
                l.intensity *= key ? _sun : Mathf.Lerp(1f, _sun, 0.5f);
                l.color = Color.Lerp(l.color, Overcast, _grey);
                if (key) l.intensity += _flash;
            }
            var sky = _night ? new Color(0.34f, 0.4f, 0.66f) : new Color(0.78f, 0.84f, 0.95f);
            var equator = _night ? new Color(0.3f, 0.33f, 0.5f) : new Color(0.86f, 0.82f, 0.76f);
            var ground = _night ? new Color(0.18f, 0.2f, 0.3f) : new Color(0.55f, 0.52f, 0.46f);
            var lift = 1f + _flash * 0.35f;
            RenderSettings.ambientSkyColor = Color.Lerp(sky, sky.grayscale * Overcast, _grey * 0.5f) * _ambient * lift;
            RenderSettings.ambientEquatorColor = Color.Lerp(equator, equator.grayscale * Overcast, _grey * 0.5f) * _ambient * lift;
            RenderSettings.ambientGroundColor = ground * _ambient * lift;
        }

        private static readonly Color Overcast = new Color(0.82f, 0.84f, 0.88f);

        public static void Release()
        {
            if (_leases <= 0) return;
            if (--_leases > 0) return;
            foreach (var (light, enabled) in WorldLights)
                if (light != null) light.enabled = enabled;
            WorldLights.Clear();
            RenderSettings.ambientMode = _mode;
            RenderSettings.ambientSkyColor = _sky;
            RenderSettings.ambientEquatorColor = _equator;
            RenderSettings.ambientGroundColor = _ground;
            RenderSettings.ambientLight = _flat;
            RenderSettings.ambientIntensity = _intensity;
            RenderSettings.fog = _fog;
            if (_rig != null) Object.Destroy(_rig);
            _rig = null;
            _night = false;
            _sun = _ambient = 1f;
            _flash = _grey = 0f;
        }

        private static void Key(string name, Color c, float intensity, Quaternion rot, bool shadows)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(_rig.transform, false);
            l.transform.rotation = rot;
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.shadows = !shadows ? LightShadows.None : GraphicsQuality.Active == "sparsam" ? LightShadows.Hard : LightShadows.Soft;
            l.shadowStrength = 0.55f;
        }
    }
}
