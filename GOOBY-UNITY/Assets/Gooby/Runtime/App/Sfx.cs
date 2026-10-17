using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// One-shot sound effects (port of feel_sfx.gd + the soft UI family of sfx_map.gd): a hidden
    /// DontDestroyOnLoad object with a pool of eight 2D AudioSources, clips from
    /// <c>Resources/gooby-audio/sfx/&lt;id&gt;</c> (cached; a missing clip stays silent and logs once), the same id at
    /// most once per 40 ms (cluster guard), volume × the id's mix trim × <see cref="AppSettings.SfxVolume"/>.
    /// Volume 0 is silence: no call plays, nothing loads, and the voices still ringing are cut the moment the
    /// setting drops to 0 (settings slider or the pause modal's Ton-Aus). Without a GameHost (EditMode tests, before
    /// the boot) every call is a no-op.
    /// </summary>
    public static class Sfx
    {
        public const int PoolSize = 8;
        public const float DebounceSec = 0.04f;
        /// <summary>The combo ladder: +1 semitone per streak step, capped at an octave (feel_sfx.gd combo_pitch).</summary>
        public const int ComboSemitoneCap = 12;

        /// <summary>
        /// Mix trims in dB per id (feel_sfx.gd SOUNDS / sfx_map.gd volume_db) — unknown ids play at 0 dB. The constant UI
        /// sounds sit 4–5 dB under the Godot values: their files are the hottest of the set (−12 … −14 dB RMS), and every
        /// button tap and sheet plays one, so at the old trims a menu was louder than the music and the game hits.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, float> Trims = new Dictionary<string, float>
        {
            ["game_hit"] = -5f, ["game_pop"] = -6f, ["game_miss"] = -5f, ["game_combo"] = -4f, ["game_count"] = -9f,
            ["game_countdown"] = -5f, ["game_go"] = -3f, ["game_win"] = -3f, ["game_lose"] = -5f, ["game_star"] = -4f,
            ["game_record"] = -3f, ["game_coin"] = -7f, ["game_whoosh"] = -9f, ["game_perfect"] = -4f,
            ["soft_tap"] = -9f, ["soft_chip"] = -10f, ["soft_back"] = -8f, ["soft_confirm"] = -6f, ["soft_error"] = -7f,
            ["soft_open"] = -11f, ["soft_close"] = -11f, ["soft_toggle"] = -9f, ["soft_tick"] = -11f, ["soft_buy"] = -3f,
            ["soft_coins"] = -4f, ["soft_levelup"] = -2f, ["soft_sticker"] = -4f, ["soft_toast"] = -6f, ["soft_win"] = -3.5f,
            ["soft_collect"] = -4f, ["soft_care_erfolg"] = -4.5f,
            ["care_wasser"] = -6f, ["care_buersten"] = -1f, ["care_spuelung"] = -4f, ["pet_squish"] = -7f, ["step_tap"] = -12f,
            ["nom_nom"] = -5.5f, ["travel_whoosh_auf"] = -3f, ["travel_whoosh_zu"] = -3f, ["build_hammer"] = -5f,
            ["ranch_wiehern_a"] = -5f, ["ranch_wiehern_b"] = -5f, ["ranch_schnauben_a"] = -6f, ["ranch_schnauben_b"] = -6f,
            ["ranch_sattel"] = -6f, ["ranch_striegeln"] = -6f, ["ranch_heu"] = -7f,
            ["ranch_fanfare"] = -4f, ["ranch_fanfare_sieg"] = -3f, ["ranch_menge_jubel"] = -6f,
            ["garage_rolltor"] = -6f, ["car_start"] = -8f, ["car_hupe"] = -9f,
        };

        private static GameObject _go;
        private static AudioSource[] _pool;
        private static int _next;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        /// <summary>The settings whose Changed event cuts the voices at volume 0 (hooked once per settings object).</summary>
        private static AppSettings _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // fast enter-play-mode keeps statics alive: forget the destroyed pool and the debounce clock
            _go = null; _pool = null; _next = 0;
            Clips.Clear(); Missing.Clear(); LastPlayed.Clear();
            LastPlayAt = -1f;
            _hooked = null;
        }

        /// <summary>Master factor from the settings (0 with no GameHost — the call is then silent).</summary>
        public static float MasterVolume
        {
            get
            {
                var settings = GameHost.Instance != null ? GameHost.Instance.Settings : null;
                return settings == null ? 0f : Mathf.Clamp01((float)settings.SfxVolume);
            }
        }

        /// <summary>Unscaled time of the last sound that started (−1 = none yet).</summary>
        public static float LastPlayAt { get; private set; } = -1f;

        /// <summary>True when a sound started within the last <paramref name="seconds"/> (the click's own handler made one).</summary>
        public static bool PlayedWithin(float seconds) => LastPlayAt >= 0f && Time.unscaledTime - LastPlayAt <= seconds;

        /// <summary>Pitch factor of a streak on the combo ladder (streak 1 = root, +1 semitone per step, cap +12).</summary>
        public static float ComboSemitones(int streak) => Mathf.Clamp(streak - 1, 0, ComboSemitoneCap);

        /// <summary>Plays a feel/UI sound by id; <paramref name="pitchSemitones"/> shifts it (+12 = an octave up).</summary>
        public static void Play(string id, float pitchSemitones = 0f, float volume = 1f)
        {
            if (string.IsNullOrEmpty(id) || !Application.isPlaying) return;
            var master = MasterVolume;
            if (master <= 0.001f || volume <= 0.001f) return;
            var now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(id, out var last) && now - last < DebounceSec && now >= last) return;
            LastPlayed[id] = now;
            var clip = Clip(id);
            if (clip == null) return;
            var src = NextSource();
            if (src == null) return;
            LastPlayAt = now;
            HookSettings();
            var trim = Trims.TryGetValue(id, out var db) ? Mathf.Pow(10f, db / 20f) : 1f;
            src.clip = clip;
            src.pitch = Mathf.Clamp(Mathf.Pow(2f, pitchSemitones / 12f), 0.25f, 4f);
            src.volume = Mathf.Clamp01(master * trim * volume);
            src.Play();
        }

        /// <summary>Cuts every voice still ringing (mute, a screen that must fall silent at once). Safe without a pool.</summary>
        public static void StopAll()
        {
            if (_pool == null) return;
            foreach (var s in _pool)
            {
                if (s != null && s.isPlaying) s.Stop();
            }
        }

        /// <summary>Volume dropped to 0 in the settings: the one-shots still ringing stop too, not only the next calls.</summary>
        private static void HookSettings()
        {
            var settings = GameHost.Instance != null ? GameHost.Instance.Settings : null;
            if (settings == null || ReferenceEquals(settings, _hooked)) return;
            _hooked = settings;
            settings.Changed += key =>
            {
                if (key == "sfxVolume" && settings.SfxVolume <= 0.001) StopAll();
            };
        }

        /// <summary>The soft UI family (soft_tap, soft_back, soft_confirm …) with a touch of pitch jitter against monotony.</summary>
        public static void PlayUi(string id = "soft_tap") => Play(id, Random.Range(-0.3f, 0.3f));

        /// <summary>
        /// Loads and decodes clips ahead of their first use (the clips import without preload: the first "3" of a
        /// countdown would otherwise decode on the spot). No-op without a GameHost, outside play mode or muted.
        /// </summary>
        public static void Preload(params string[] ids)
        {
            if (ids == null || !Application.isPlaying || MasterVolume <= 0.001f) return;
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var clip = Clip(id);
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            }
        }

        /// <summary>Loads (and caches) the clip of an id; null when the file is missing (logged once).</summary>
        public static AudioClip Clip(string id)
        {
            if (Clips.TryGetValue(id, out var clip)) return clip;
            if (Missing.Contains(id)) return null;
            clip = Resources.Load<AudioClip>("gooby-audio/sfx/" + id);
            if (clip == null)
            {
                Missing.Add(id);
                Debug.LogWarning("[sfx] clip missing: gooby-audio/sfx/" + id);
                return null;
            }
            Clips[id] = clip;
            return clip;
        }

        private static AudioSource NextSource()
        {
            if (_go == null || _pool == null)
            {
                _go = new GameObject("GoobySfx") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(_go);
                _pool = new AudioSource[PoolSize];
                for (var i = 0; i < PoolSize; i++)
                {
                    var s = _go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.loop = false;
                    s.spatialBlend = 0f;
                    s.ignoreListenerPause = true;
                    _pool[i] = s;
                }
            }
            // a free voice first; with all eight busy the oldest round-robin slot is cut
            for (var i = 0; i < PoolSize; i++)
            {
                var s = _pool[(_next + i) % PoolSize];
                if (s != null && !s.isPlaying) { _next = (_next + i + 1) % PoolSize; return s; }
            }
            var steal = _pool[_next];
            _next = (_next + 1) % PoolSize;
            return steal;
        }
    }
}
