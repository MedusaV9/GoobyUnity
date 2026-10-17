using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Visible weather outdoors — port of wetter_fx.gd: rain streaks slanting in the wind with splash rings on the ground,
    /// tumbling snow flakes, a lightning flash (a short exposure spike of the grade, no strobe) with the thunder a moment
    /// later, and the rain loop. Everything follows a <see cref="WetterFx.Plan"/> from the day's plan — nothing is rolled
    /// here except the pause between two flashes. One particle system per effect, sized by the graphics quality; reduced
    /// motion arrives as a plan without particles. The garden pins the box over the lawn, the street lets it ride with the
    /// camera; at home only the rain is heard, quietly, as through a window.
    /// </summary>
    public sealed class WeatherFx
    {
        public enum Stage { Off, Garden, Street, Home }

        /// <summary>Particles alive at full strength (wetter_fx.gd amounts); a stage multiplies them (the street box is larger).</summary>
        public const int RainAlive = 420, SnowAlive = 300, SplashAlive = 160;
        public const float SnowLife = 9f, SplashLife = 0.55f, RainSpeed = 19f;
        public const float FlashPauseMin = 3.5f, FlashPauseMax = 9f, FlashSeconds = 0.16f, FlashEv = 1.1f;
        /// <summary>The loop's level: rain 0.7 × strength, a thunderstorm 0.85; at home a third of it.</summary>
        public const float RainLoopLevel = 0.7f, StormLoopLevel = 0.85f, HomeDamping = 0.35f;

        public readonly GameObject Root;
        private readonly ParticleSystem _rain, _splash, _snow;
        private readonly AudioSource _loop, _thunder;
        private readonly System.Random _rng;
        private WetterFx.Plan _plan = new WetterFx.Plan();
        private Stage _stage = Stage.Off;
        private Vector3 _center, _extents;
        private float _height, _amount = 1f, _rainLife = 0.6f, _particleFactor = 1f, _flashIn = 5f, _flashLeft, _thunderIn = -1f, _baseEv;
        private static Texture2D _streak, _ring;

        public WetterFx.Plan Plan => _plan;
        public Stage Where => _stage;
        public bool Raining => _rain.emission.enabled && _rain.isPlaying;
        public bool Snowing => _snow.emission.enabled && _snow.isPlaying;
        public bool Splashing => _splash.emission.enabled && _splash.isPlaying;
        /// <summary>Lightning flashes so far (QA).</summary>
        public int Flashes { get; private set; }
        public float LoopVolume => _loop.isPlaying ? _loop.volume : 0f;
        /// <summary>No rain loop of its own: the ranch hears its rain through its ambience layers.</summary>
        public bool Quiet { get; set; }

        public WeatherFx(Transform parent, int seed)
        {
            _rng = new System.Random(seed);
            Root = new GameObject("Weather");
            Root.transform.SetParent(parent, false);
            _rain = NewSystem("Rain");
            _splash = NewSystem("RainSplash");
            _snow = NewSystem("Snow");
            ConfigureRain();
            ConfigureSplash();
            ConfigureSnow();
            _loop = Root.AddComponent<AudioSource>();
            _loop.loop = true;
            _loop.playOnAwake = false;
            _loop.spatialBlend = 0f;
            _loop.volume = 0f;
            _loop.clip = Resources.Load<AudioClip>("gooby-audio/sfx/amb_regen");
            _thunder = Root.AddComponent<AudioSource>();
            _thunder.playOnAwake = false;
            _thunder.spatialBlend = 0f;
            _thunder.clip = Resources.Load<AudioClip>("gooby-audio/sfx/amb_donner");
            Root.SetActive(false);
        }

        /// <summary>
        /// Where the weather falls: the box (half extents) around <paramref name="center"/>, starting <paramref name="height"/>
        /// up, with <paramref name="amount"/> × the base particle counts.
        /// </summary>
        public void Place(Stage stage, Vector3 center, Vector3 extents, float height, float amount = 1f)
        {
            _stage = stage;
            _center = center;
            _extents = extents;
            _height = height;
            _amount = amount;
            // a drop lives exactly its fall: under the ground it would only cost fill rate and thin out the visible rain
            _rainLife = (height + 0.4f) / RainSpeed;
            var main = _rain.main;
            main.startLifetime = _rainLife;
            Root.SetActive(stage != Stage.Off);
            Root.transform.position = center;
            SetBox(_rain, new Vector3(extents.x * 2f, 1f, extents.z * 2f), height);
            SetBox(_snow, new Vector3(extents.x * 2f, 1f, extents.z * 2f), height * 0.7f);
            SetBox(_splash, new Vector3(extents.x * 1.6f, 0.02f, extents.z * 1.6f), 0.04f);
            Refresh();
        }

        public void Apply(WetterFx.Plan plan, float particleFactor)
        {
            _plan = plan ?? new WetterFx.Plan();
            _particleFactor = Mathf.Clamp01(particleFactor);
            if (!_plan.Blitz) EndFlash();
            Refresh();
        }

        /// <summary>The street box rides with the camera, a little ahead of it; flashes and the loop's fade run here.</summary>
        public void Tick(float dt, Camera camera, SceneDressing.Rig rig)
        {
            if (_stage == Stage.Off || dt <= 0f) return;
            if (_stage == Stage.Street && camera != null)
            {
                var fwd = camera.transform.forward;
                fwd.y = 0f;
                var ahead = fwd.sqrMagnitude > 1e-4f ? fwd.normalized * (_extents.z * 0.45f) : Vector3.zero;
                Root.transform.position = new Vector3(camera.transform.position.x, _center.y, camera.transform.position.z) + ahead;
            }
            TickFlash(dt, rig);
            TickSound(dt);
        }

        private bool Outdoors => _stage == Stage.Garden || _stage == Stage.Street;

        private void Refresh()
        {
            var k = (float)_plan.Staerke * _particleFactor * _amount;
            Emit(_rain, Outdoors && _plan.Regen, RainAlive * k, _rainLife);
            Emit(_splash, Outdoors && _plan.Spritzer, SplashAlive * k, SplashLife);
            Emit(_snow, Outdoors && _plan.Schnee, SnowAlive * k, SnowLife);
        }

        private static void Emit(ParticleSystem ps, bool on, float alive, float life)
        {
            var count = Mathf.Max(on ? 12 : 0, Mathf.RoundToInt(alive));
            var main = ps.main;
            main.maxParticles = Mathf.Max(1, count);
            var em = ps.emission;
            em.enabled = on;
            em.rateOverTime = on ? count / life : 0f;
            if (on && !ps.isPlaying) ps.Play();
            else if (!on && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // ── lightning + sound ──

        private void TickFlash(float dt, SceneDressing.Rig rig)
        {
            if (_thunderIn >= 0f)
            {
                _thunderIn -= dt;
                if (_thunderIn < 0f && _thunder.clip != null && Sfx.MasterVolume > 0f)
                {
                    _thunder.volume = Sfx.MasterVolume * (_stage == Stage.Home ? HomeDamping : 0.9f);
                    _thunder.Play();
                }
            }
            if (!_plan.Blitz || rig == null) return;
            _flashIn -= dt;
            if (_flashIn <= 0f)
            {
                _flashIn = Mathf.Lerp(FlashPauseMin, FlashPauseMax, (float)_rng.NextDouble());
                if (_flashLeft <= 0f) _baseEv = rig.ColorAdjustments.postExposure.value;
                _flashLeft = FlashSeconds;
                _thunderIn = Mathf.Lerp(0.4f, 1.8f, (float)_rng.NextDouble());
                Flashes++;
            }
            if (_flashLeft <= 0f) return;
            _flashLeft -= dt;
            if (!Outdoors) return;
            var share = Mathf.Clamp01(_flashLeft / FlashSeconds);
            rig.ColorAdjustments.postExposure.Override(_flashLeft > 0f ? _baseEv + FlashEv * share : _baseEv);
            _rigInFlash = _flashLeft > 0f ? rig : null;
        }

        private SceneDressing.Rig _rigInFlash;

        private void EndFlash()
        {
            if (_rigInFlash != null) _rigInFlash.ColorAdjustments.postExposure.Override(_baseEv);
            _rigInFlash = null;
            _flashLeft = 0f;
        }

        private void TickSound(float dt)
        {
            if (_loop.clip == null) return;
            var target = 0f;
            if (_plan.Regen && !Quiet) target = (_plan.Blitz ? StormLoopLevel : RainLoopLevel * (float)_plan.Staerke) * Sfx.MasterVolume;
            if (_stage == Stage.Home) target *= HomeDamping;
            var now = _loop.isPlaying ? _loop.volume : 0f;
            var next = Mathf.MoveTowards(now, target, dt * 0.6f);
            if (next < 0.005f)
            {
                if (_loop.isPlaying) _loop.Stop();
                return;
            }
            _loop.volume = next;
            if (!_loop.isPlaying) _loop.Play();
        }

        // ── the particle systems ──

        private ParticleSystem NewSystem(string name)
        {
            var go = new GameObject("Weather:" + name);
            go.transform.SetParent(Root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static void SetBox(ParticleSystem ps, Vector3 size, float y)
        {
            ps.transform.localPosition = new Vector3(0f, y, 0f);
            var shape = ps.shape;
            shape.scale = size;
        }

        /// <summary>Streaks: stretched along their fall, slanting a little with the wind (wetter_fx.gd direction 0.14/−1/0.06).</summary>
        private void ConfigureRain()
        {
            var main = _rain.main;
            main.prewarm = true;
            main.startLifetime = _rainLife;
            main.startSize = 0.07f;
            main.startColor = new Color(0.8f, 0.86f, 0.96f, 0.8f);
            var vel = _rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
            vel.y = new ParticleSystem.MinMaxCurve(-RainSpeed - 3f, -RainSpeed + 3f);
            vel.z = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            var r = _rain.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1f;
            r.velocityScale = 0.042f;
            r.sharedMaterial = ArtLibrary.Particles(Streak(), false);
        }

        /// <summary>Splash rings: flat on the ground, growing from a dot and fading in half a second.</summary>
        private void ConfigureSplash()
        {
            var main = _splash.main;
            main.startLifetime = SplashLife;
            main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.8f);
            main.startColor = new Color(0.88f, 0.94f, 1f, 0.8f);
            var size = _splash.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(1f, 1f)));
            var col = _splash.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = _splash.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sharedMaterial = ArtLibrary.Particles(Ring(), false);
        }

        /// <summary>Snow: soft flakes drifting down at a metre a second, turning, swaying in a light breeze.</summary>
        private void ConfigureSnow()
        {
            var main = _snow.main;
            main.duration = SnowLife;   // the prewarm simulates one duration: a full fall from the top of the box
            main.prewarm = true;
            main.startLifetime = SnowLife;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.19f);
            main.startColor = new Color(1f, 1f, 1f, 0.92f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var vel = _snow.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.5f, -0.75f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.3f);
            var rot = _snow.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            var noise = _snow.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.25f;
            var r = _snow.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = ArtLibrary.Particles(Effects.Dot(), false);
        }

        /// <summary>A soft drop streak: transparent at both ends, densest a little above the middle.</summary>
        private static Texture2D Streak()
        {
            if (_streak != null) return _streak;
            const int w = 8, h = 64;
            _streak = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "fx_streak", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var v = (y + 0.5f) / h;
                    var along = v < 0.42f ? v / 0.42f : (1f - v) / 0.58f;
                    var across = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                    var a = Mathf.Clamp01(along) * Mathf.SmoothStep(0f, 1f, across);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            _streak.SetPixels32(px);
            _streak.Apply(false, true);
            return _streak;
        }

        /// <summary>The splash ring: an annulus (wetter_fx.gd ring 0.17–0.24 of the radius) with soft edges.</summary>
        private static Texture2D Ring()
        {
            if (_ring != null) return _ring;
            const int n = 64;
            _ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "fx_ring", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = (x + 0.5f) / n * 2f - 1f;
                    var dy = (y + 0.5f) / n * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.12f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            _ring.SetPixels32(px);
            _ring.Apply(false, true);
            return _ring;
        }
    }
}
