using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Haptic accents for the minigame frame, graded by the settings' strength (dezent | normal | stark):
    /// <list type="bullet">
    /// <item>Tap — a hit. Android: a short one-shot (6/12/22 ms). iOS has no light impact without a native plugin: none.</item>
    /// <item>Win — a round that ends with points. Android: 30/45/70 ms. iOS: the long buzz only at "stark".</item>
    /// <item>Success / Warn — new record, level-up / a lost life, heavy miss. Android: 0/60/100 and 0/80/130 ms
    /// ("dezent" keeps only the taps). iOS: <c>Handheld.Vibrate</c> unless "dezent".</item>
    /// </list>
    /// Buzzes keep <see cref="MinGapSec"/> apart (a burst of misses must not rattle the phone), taps
    /// <see cref="TapGapSec"/>. Desktop, editor and tests never vibrate; without a GameHost every call is a no-op.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Shortest gap between two buzzes (a burst of misses must not rattle the phone).</summary>
        public const float MinGapSec = 0.35f;
        /// <summary>Shortest gap between two hit taps (a burst of hits lands as one tick).</summary>
        public const float TapGapSec = 0.06f;
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        private static float _last = -10f, _lastTap = -10f;
#endif

        /// <summary>The settings' strength ("dezent", "normal", "stark"); null without a GameHost.</summary>
        private static string Strength
        {
            get
            {
                var settings = GameHost.Instance != null ? GameHost.Instance.Settings : null;
                return settings != null ? settings.HapticStrength : null;
            }
        }

        /// <summary>The big moments buzz (every strength but "dezent").</summary>
        public static bool Enabled
        {
            get
            {
                var s = Strength;
                return s != null && s != "dezent";
            }
        }

        /// <summary>Light tick on a hit.</summary>
        public static void Tap()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var s = Strength;
            if (s == null || !Application.isPlaying) return;
            var now = Time.unscaledTime;
            if (now - _lastTap < TapGapSec && now >= _lastTap) return;
            _lastTap = now;
            AndroidOneShot(s == "stark" ? 22 : s == "dezent" ? 6 : 12, s == "stark" ? 200 : s == "dezent" ? 60 : 120);
#endif
        }

        /// <summary>A round that ends with points.</summary>
        public static void Win()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var s = Strength;
            if (s == null) return;
            Buzz(s == "stark" ? 70 : s == "dezent" ? 30 : 45, allowDezent: true);
#elif UNITY_IOS && !UNITY_EDITOR
            if (Strength == "stark") Buzz(0);
#endif
        }

        /// <summary>New record, level-up.</summary>
        public static void Success() => Buzz(Strength == "stark" ? 100 : 60);

        /// <summary>A lost life, a heavy miss.</summary>
        public static void Warn() => Buzz(Strength == "stark" ? 130 : 80);

        private static void Buzz(int androidMs, bool allowDezent = false)
        {
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            var s = Strength;
            if (s == null || (!allowDezent && s == "dezent") || !Application.isPlaying) return;
            var now = Time.unscaledTime;
            if (now - _last < MinGapSec && now >= _last) return;
            _last = now;
            _lastTap = now;
#if UNITY_ANDROID
            AndroidOneShot(androidMs, s == "stark" ? 255 : 180);
#else
            Handheld.Vibrate();
#endif
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _vibrator;
        private static int _sdk = -1;
        private static bool _broken;

        /// <summary>
        /// android.os.Vibrator one-shot (VibrationEffect from API 26, the plain duration before). Any JNI failure
        /// falls back to Handheld.Vibrate once and switches the short effects off (a missing vibrator stays quiet).
        /// </summary>
        private static void AndroidOneShot(int ms, int amplitude)
        {
            if (_broken || ms <= 0) return;
            try
            {
                if (_vibrator == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                        _sdk = version.GetStatic<int>("SDK_INT");
                    if (_vibrator == null || !_vibrator.Call<bool>("hasVibrator")) { _broken = true; return; }
                }
                if (_sdk >= 26)
                {
                    using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, Mathf.Clamp(amplitude, 1, 255)))
                        _vibrator.Call("vibrate", effect);
                }
                else _vibrator.Call("vibrate", (long)ms);
            }
            catch (System.Exception e)
            {
                _broken = true;
                Debug.LogWarning("[haptics] vibrator unavailable: " + e.Message);
                Handheld.Vibrate();
            }
        }
#endif
    }
}
