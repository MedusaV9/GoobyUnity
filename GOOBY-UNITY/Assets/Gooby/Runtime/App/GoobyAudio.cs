using System.Collections.Generic;
using Gooby.Core.Audio;
using Gooby.Core.Life;
using Gooby.Core.Random;
using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Music, ambience beds, stingers and Gooby's voice — port of music_director.gd, klangbett.gd and gooby_voice.gd.
    /// Every frame it reads the sound scene of the UI stack (<see cref="UI.UiRoot.SoundScene"/>): the scene picks the
    /// context track (Core <see cref="MusicRegistry"/>, 1.5 s crossfade) and the bed layers (Core <see cref="SoundScape"/>,
    /// 2.5 s fades). Unknown scenes keep what plays. Stingers and babble duck music and beds. In the car the radio
    /// replaces the context music while it is on. Volumes follow the settings live (music slider for music and
    /// stingers, effects slider for beds and voice); 0 is silence.
    /// </summary>
    public sealed class GoobyAudio : MonoBehaviour
    {
        public const float CrossfadeSec = 1.5f;
        public const float BedFadeSec = 2.5f;
        /// <summary>The music sits 7.5 dB under the effects — any lower and it vanishes under the game hits on a phone speaker.</summary>
        public const float MusicBase = 0.42f;
        /// <summary>Stingers ride +6 dB over the bed of music (STINGER_BOOST_DB).</summary>
        public const float StingerBoost = 2.0f;
        /// <summary>Beds stay well under music and effects (≈ −34 dBFS in Godot).</summary>
        public const float BedBase = 0.3f;
        public const float VoiceBase = 0.5f;

        private GameHost _host;
        private readonly AudioSource[] _music = new AudioSource[2];
        private int _active;
        private float _fade = 1f;
        private float _offStart;
        private AudioSource _stinger;
        private float _stingerLeft;
        private readonly Dictionary<string, AudioSource> _beds = new Dictionary<string, AudioSource>();
        private readonly Dictionary<string, float> _bedTargets = new Dictionary<string, float>();
        private readonly AudioSource[] _voice = new AudioSource[4];
        private int _voiceNext;
        private readonly List<GoobyBabble.Syllable> _babble = new List<GoobyBabble.Syllable>();
        private float _babbleT;
        private double _duck = 1.0;
        private readonly GoobyRng _rng = new GoobyRng(System.Environment.TickCount);
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        private string _scene;
        private string _context;
        private RadioQueue _radio;

        /// <summary>The track playing now ("" = none) and the context it stands for.</summary>
        public string Track { get; private set; } = "";
        public string Context => _context;
        public string Scene => _scene;
        /// <summary>The radio station that is on (null = off).</summary>
        public string RadioStation => _radio?.Station;
        public bool Babbling => _babble.Count > 0;

        public static GoobyAudio Create(GameHost host)
        {
            var audio = host.gameObject.AddComponent<GoobyAudio>();
            audio._host = host;
            for (var i = 0; i < 2; i++) audio._music[i] = audio.NewSource(true);
            audio._stinger = audio.NewSource(false);
            for (var i = 0; i < audio._voice.Length; i++) audio._voice[i] = audio.NewSource(false);
            return audio;
        }

        private AudioSource NewSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            s.volume = 0f;
            return s;
        }

        private float MusicVolume => _host?.Settings == null ? 0f : Mathf.Clamp01((float)_host.Settings.MusicVolume);
        private float SfxVolume => _host?.Settings == null ? 0f : Mathf.Clamp01((float)_host.Settings.SfxVolume);

        private void Update()
        {
            if (_host == null || _host.Ui == null) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            var scene = _host.Ui.SoundScene;
            if (scene != null && scene != _scene) EnterScene(scene);
            if (scene != null && _context != null && (_scene == "home/living" || _scene == "home/bedroom")) RefreshSleep();
            TickRadio();
            TickBabble(dt);
            _stingerLeft = Mathf.Max(0f, _stingerLeft - dt);
            _duck = SoundScape.DuckStep(_duck, SoundScape.DuckTarget(Babbling, _stingerLeft), dt);
            TickMusic(dt);
            TickBeds(dt);
        }

        private void EnterScene(string scene)
        {
            _scene = scene;
            var context = SoundScape.MusicContext(scene, Sleep.IsSleeping(_host.Store?.State));
            if (context != null && context != _context)
            {
                _context = context;
                // the radio plays on in the car and at home (the home radio); a place or a minigame brings its own music
                if (_radio != null && scene != "city" && !scene.StartsWith("home")) StopRadio(false);
                if (_radio == null) PlayTrack(MusicRegistry.TrackFor(context));
            }
            var beds = _liveBeds.TryGetValue(scene, out var live) ? live : SoundScape.BedPlan(scene);
            if (beds != null)
            {
                ApplyBeds(beds);
                Debug.Log("[ambience] " + scene + " → " + (beds.Count == 0 ? "off" : string.Join(",", beds.Keys)));
            }
        }

        private readonly Dictionary<string, IReadOnlyDictionary<string, double>> _liveBeds = new Dictionary<string, IReadOnlyDictionary<string, double>>();

        /// <summary>
        /// A scene whose ambience follows its own world (the ranch: zone, weather, hour) sets its layers live: they apply while
        /// that scene plays and come back with it. Null forgets them (the screen is gone).
        /// </summary>
        public void SetBeds(string scene, IReadOnlyDictionary<string, double> plan)
        {
            if (string.IsNullOrEmpty(scene)) return;
            if (plan == null)
            {
                _liveBeds.Remove(scene);
                return;
            }
            _liveBeds[scene] = plan;
            if (scene == _scene) ApplyBeds(plan);
        }

        /// <summary>The live target of a bed layer (0 when off) — QA.</summary>
        public float BedTarget(string layer) => _bedTargets.TryGetValue(layer, out var v) ? v : 0f;

        private void ApplyBeds(IReadOnlyDictionary<string, double> beds)
        {
            foreach (var key in new List<string>(_bedTargets.Keys)) _bedTargets[key] = 0f;
            foreach (var kv in beds) _bedTargets[kv.Key] = (float)kv.Value;
        }

        private void RefreshSleep()
        {
            var context = SoundScape.MusicContext(_scene, Sleep.IsSleeping(_host.Store?.State));
            if (context == null || context == _context || _radio != null) return;
            _context = context;
            PlayTrack(MusicRegistry.TrackFor(context));
        }

        private AudioClip Clip(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (_clips.TryGetValue(resourcePath, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null) Debug.LogWarning("[music] clip missing: " + resourcePath);
            _clips[resourcePath] = clip;
            return clip;
        }

        /// <summary>Crossfades to a track ("" fades the music out).</summary>
        private void PlayTrack(string trackId)
        {
            trackId = trackId ?? "";
            if (trackId == Track) return;
            Debug.Log("[music] context " + (_context ?? "-") + " → track " + (trackId.Length == 0 ? "(none)" : trackId));
            Track = trackId;
            var clip = trackId.Length == 0 ? null : Clip(MusicRegistry.ResourcePath(trackId));
            _offStart = _music[_active].isPlaying ? _music[_active].volume : 0f;
            _active = 1 - _active;
            var next = _music[_active];
            next.Stop();
            next.clip = clip;
            next.loop = _radio == null;
            next.volume = 0f;
            if (clip != null) next.Play();
            _fade = 0f;
        }

        private void TickMusic(float dt)
        {
            _fade = Mathf.Min(1f, _fade + dt / CrossfadeSec);
            var on = _music[_active];
            var off = _music[1 - _active];
            var master = MusicVolume;
            var trim = Mathf.Pow(10f, (float)(MusicRegistry.TrimDb(Track) + (_radio == null ? SoundScape.MusicLiftDb(_context) : 0.0)) / 20f);
            var level = master * MusicBase * trim * (float)_duck;
            on.volume = level * _fade;
            off.volume = _offStart * (1f - _fade);
            if (off.isPlaying && _fade >= 1f) off.Stop();
            _stinger.volume = master * MusicBase * StingerBoost;
        }

        private void TickBeds(float dt)
        {
            var master = SfxVolume * BedBase * (float)_duck;
            foreach (var kv in _bedTargets)
            {
                if (!_beds.TryGetValue(kv.Key, out var src))
                {
                    if (kv.Value <= 0f) continue;
                    if (!SoundScape.BedClips.TryGetValue(kv.Key, out var clipId)) continue;
                    src = NewSource(true);
                    src.clip = Clip("gooby-audio/ambient/" + clipId);
                    if (src.clip == null) continue;
                    src.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, src.clip.length - 1f));
                    src.Play();
                    _beds[kv.Key] = src;
                }
                var target = kv.Value * master;
                src.volume = Mathf.MoveTowards(src.volume, target, dt / BedFadeSec);
                if (src.volume <= 0.0005f && kv.Value <= 0f && src.isPlaying) src.Pause();
                else if (kv.Value > 0f && !src.isPlaying) src.UnPause();
            }
        }

        // ── stingers ─────────────────────────────────────────────────────────

        /// <summary>A reward jingle over the music ("stinger-levelup", "stinger-results"); music and beds duck under it.</summary>
        public void PlayStinger(string trackId)
        {
            if (MusicVolume <= 0.001f) return;
            var clip = Clip(MusicRegistry.ResourcePath(trackId));
            if (clip == null) return;
            _stinger.Stop();
            _stinger.clip = clip;
            _stinger.Play();
            _stingerLeft = clip.length + 0.4f;
            Debug.Log("[music] stinger " + trackId);
        }

        // ── Gooby's voice ────────────────────────────────────────────────────

        /// <summary>Gooby babbles a line (cuts a running one); emotion lifts or lowers the pitch.</summary>
        public void Say(string text, string emotion = "neutral")
        {
            _babble.Clear();
            if (SfxVolume <= 0.001f) return;
            _babble.AddRange(GoobyBabble.Plan(text, emotion, _rng));
            _babbleT = 0f;
        }

        private void TickBabble(float dt)
        {
            if (_babble.Count == 0) return;
            _babbleT += dt;
            while (_babble.Count > 0 && _babble[0].At <= _babbleT)
            {
                var s = _babble[0];
                _babble.RemoveAt(0);
                var clip = Clip("gooby-audio/voice/" + s.Clip);
                if (clip == null) continue;
                var src = _voice[_voiceNext];
                _voiceNext = (_voiceNext + 1) % _voice.Length;
                src.pitch = (float)s.Pitch;
                src.volume = SfxVolume * VoiceBase;
                src.clip = clip;
                src.Play();
            }
        }

        // ── car radio ────────────────────────────────────────────────────────

        /// <summary>Off → the first station the level unlocks → the next one → … → off. Returns the station (null = off).</summary>
        public string CycleRadio(int level)
        {
            var stations = MusicRegistry.Stations();
            var unlocked = stations.FindAll(s => s.unlockLevel <= Mathf.Max(1, level));
            var at = _radio == null ? -1 : unlocked.FindIndex(s => s.id == _radio.Station);
            if (at + 1 >= unlocked.Count)
            {
                StopRadio(true);
                return null;
            }
            _radio = new RadioQueue(unlocked[at + 1].id, level, _rng);
            Debug.Log("[radio] " + _radio.Station);
            PlayTrack(_radio.Next());
            return _radio.Station;
        }

        /// <summary>The home radio: tunes to <paramref name="station"/> (if the level unlocks it); true when it plays.</summary>
        public bool PlayRadio(string station, int level)
        {
            if (!RadioQueue.StationUnlocked(station, Mathf.Max(1, level))) return false;
            if (_radio != null && _radio.Station == station) return true;
            _radio = new RadioQueue(station, level, _rng);
            Debug.Log("[radio] " + _radio.Station);
            PlayTrack(_radio.Next());
            return true;
        }

        /// <summary>The next song of the station that plays (nothing when the radio is off).</summary>
        public void SkipRadio()
        {
            if (_radio == null) return;
            Debug.Log("[radio] skip");
            PlayTrack(_radio.Next());
        }

        /// <summary>Radio off: the scene's own music comes back.</summary>
        public void RadioOff() => StopRadio(true);

        /// <summary>Title of the song playing now (registry title; null without music).</summary>
        public string NowPlayingTitle => MusicRegistry.Get(Track)?.Title;

        private void StopRadio(bool backToContext)
        {
            if (_radio == null) return;
            _radio = null;
            Debug.Log("[radio] off");
            if (backToContext) PlayTrack(MusicRegistry.TrackFor(_context));
        }

        private void TickRadio()
        {
            if (_radio == null) return;
            var src = _music[_active];
            if (src.clip != null && !src.isPlaying && src.timeSamples == 0 && _fade >= 1f) PlayTrack(_radio.Next());
        }
    }
}
