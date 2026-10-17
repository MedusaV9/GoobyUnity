using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Particle effects of the new look — the small environmental reactions the concept boards imply and a flat
    /// render lacks: dust under Gooby's feet, the puff of a hop, the sparkle of a placed item, petals and leaves
    /// drifting over the lawn, fireflies after dark, motes in the window sun, exhaust behind the car. Everything is
    /// built from two procedural textures and the two particle materials; one-shots share pooled systems.
    /// With <see cref="Reduced"/> (AppSettings "weniger Bewegung") the ambient systems stay off and bursts are tiny.
    /// </summary>
    public static class Effects
    {
        public static bool Reduced { get; set; }

        private static Texture2D _dot, _petal;
        private static ParticleSystem _puffs, _glints;

        /// <summary>Soft round dot: a radial alpha falloff (the all-purpose puff).</summary>
        public static Texture2D Dot()
        {
            if (_dot != null) return _dot;
            const int n = 64;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "fx_dot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = (x + 0.5f) / n * 2f - 1f;
                    var dy = (y + 0.5f) / n * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.25f, 1f, r));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            _dot.SetPixels32(px);
            _dot.Apply(false, true);
            return _dot;
        }

        /// <summary>Petal / small leaf: a soft ellipse with a faint darker vein, tinted per particle.</summary>
        public static Texture2D Petal()
        {
            if (_petal != null) return _petal;
            const int n = 64;
            _petal = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "fx_petal", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = ((x + 0.5f) / n * 2f - 1f) / 0.55f;
                    var dy = (y + 0.5f) / n * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.7f, 1f, r));
                    var vein = Mathf.Abs(dx) < 0.12f ? 0.88f : 1f;   // a whisper of a mid-rib
                    var v = (byte)(255f * vein);
                    px[y * n + x] = new Color32(v, v, v, (byte)(a * 255f));
                }
            _petal.SetPixels32(px);
            _petal.Apply(false, true);
            return _petal;
        }

        // ── one-shots ────────────────────────────────────────────────────────

        /// <summary>Pooled looping system with no emission of its own: bursts are pushed into it with Emit.</summary>
        private static ParticleSystem Pool(ref ParticleSystem ps, string name, Texture2D tex, bool additive, bool grow)
        {
            if (ps != null) return ps;
            var go = new GameObject("FX:" + name);
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);   // EditMode tests place furniture too
            ps = NewSystem(go);
            var main = ps.main;
            main.maxParticles = 400;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = 0f;
            FadeOut(ps, grow);
            Renderer(ps, tex, additive);
            Start(ps);
            return ps;
        }

        /// <summary>Plays now and again whenever its object is re-enabled (petals by day, fireflies by night, motes indoors).</summary>
        private static void Start(ParticleSystem ps)
        {
            var main = ps.main;
            main.playOnAwake = true;
            ps.Play();
        }

        /// <summary>Alpha fades to nothing over the lifetime; size either swells (a puff) or shrinks (a glint).</summary>
        private static void FadeOut(ParticleSystem ps, bool grow)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, grow
                ? new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.25f))
                : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));
        }

        private static void Renderer(ParticleSystem ps, Texture2D tex, bool additive)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = ArtLibrary.Particles(tex, additive);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.alignment = ParticleSystemRenderSpace.View;
        }

        /// <summary>
        /// Soft puff: <paramref name="count"/> particles from <paramref name="at"/>, drifting along <paramref name="dir"/>
        /// (spread by <paramref name="spread"/>, 0..1) at up to <paramref name="speed"/> m/s, living <paramref name="life"/> s.
        /// </summary>
        public static void Puff(Vector3 at, Color color, int count, float size, float speed, float life, Vector3 dir, float spread = 0.6f)
        {
            var ps = Pool(ref _puffs, "puffs", Dot(), false, true);
            if (Reduced) count = Mathf.Max(1, count / 3);
            var p = new ParticleSystem.EmitParams();
            for (var i = 0; i < count; i++)
            {
                var jitter = Random.insideUnitSphere;
                p.position = at + jitter * size * 0.35f;
                p.velocity = (dir.normalized + jitter * spread).normalized * speed * Random.Range(0.45f, 1f);
                p.startSize = size * Random.Range(0.7f, 1.3f);
                p.startLifetime = life * Random.Range(0.75f, 1.15f);
                p.startColor = color;
                p.rotation = Random.Range(0f, 360f);
                p.angularVelocity = Random.Range(-40f, 40f);
                ps.Emit(p, 1);
            }
        }

        /// <summary>Hop: a ring of dust that rolls outwards along the floor from the landing spot.</summary>
        public static void LandingRing(Vector3 at, Color color, float radius = 0.45f, int count = 10)
        {
            var ps = Pool(ref _puffs, "puffs", Dot(), false, true);
            if (Reduced) count = 3;
            var p = new ParticleSystem.EmitParams();
            for (var i = 0; i < count; i++)
            {
                var a = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
                var out3 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                p.position = at + out3 * radius * 0.35f + Vector3.up * 0.04f;
                p.velocity = out3 * radius * 2.2f + Vector3.up * 0.25f;
                p.startSize = radius * Random.Range(0.35f, 0.55f);
                p.startLifetime = Random.Range(0.45f, 0.7f);
                p.startColor = color;
                p.rotation = Random.Range(0f, 360f);
                ps.Emit(p, 1);
            }
        }

        /// <summary>Placed / rewarded: warm glints rising from a spot (board 09's "Ohh, wird das schön? ✨").</summary>
        public static void Sparkle(Vector3 at, float radius = 0.4f, int count = 22)
        {
            var ps = Pool(ref _glints, "glints", Dot(), true, false);
            if (Reduced) count = 6;
            var p = new ParticleSystem.EmitParams();
            for (var i = 0; i < count; i++)
            {
                var off = Random.insideUnitSphere;
                off.y = Mathf.Abs(off.y);
                p.position = at + off * radius;
                p.velocity = Vector3.up * Random.Range(0.4f, 1.1f) + new Vector3(off.x, 0f, off.z) * 0.45f;
                p.startSize = Random.Range(0.1f, 0.22f);
                p.startLifetime = Random.Range(0.7f, 1.1f);
                p.startColor = i % 3 == 0 ? new Color(1f, 0.95f, 0.75f) : i % 3 == 1 ? new Color(1f, 0.82f, 0.45f) : new Color(1f, 0.7f, 0.8f);
                ps.Emit(p, 1);
            }
        }

        // ── ambient systems ──────────────────────────────────────────────────

        /// <summary>
        /// Petals and small leaves drifting down over a lawn box (world-space bounds), tumbling in a light breeze.
        /// Pink, white and leaf green; a couple of dozen alive at a time. Off with reduced motion.
        /// </summary>
        public static ParticleSystem Petals(Transform parent, Bounds box)
        {
            var ps = Ambient(parent, "Petals", Petal(), false, box, 2.2f, 8f, 12f);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.17f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.74f), new Color(0.55f, 0.84f, 0.55f));
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.012f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);
            rot.y = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            rot.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.25f, -0.1f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.3f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.World;   // petals lie in the wind, not flat to the camera
            Start(ps);
            return ps;
        }

        /// <summary>Fireflies over the lawn at night: tiny warm additive glints that wander and pulse.</summary>
        public static ParticleSystem Fireflies(Transform parent, Bounds box)
        {
            var ps = Ambient(parent, "Fireflies", Dot(), true, box, 1.6f, 5f, 9f);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.45f), new Color(0.85f, 1f, 0.55f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            // all three axes in the same (two-constants) mode: Unity rejects a mix
            vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.12f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.4f;
            // pulse: bright, dark, bright again within a life, then out
            var col = ps.colorOverLifetime;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.15f, 0.45f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            Start(ps);
            return ps;
        }

        /// <summary>
        /// Motes in a shaft of window sun: a slow cone of faint cream specks from <paramref name="from"/> along
        /// <paramref name="dir"/>, so the light pool on the floor reads as light in the air (board 08).
        /// </summary>
        public static ParticleSystem SunMotes(Transform parent, Vector3 from, Vector3 dir, float length, float radius)
        {
            var go = new GameObject("FX:SunMotes");
            go.transform.SetParent(parent, false);
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var ps = NewSystem(go);
            var main = ps.main;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new Color(1f, 0.93f, 0.78f, 0.85f);
            main.maxParticles = 120;
            main.gravityModifier = 0.002f;
            var em = ps.emission;
            em.rateOverTime = Reduced ? 0f : 14f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = radius;
            shape.length = length;
            shape.radiusThickness = 1f;
            shape.arc = 360f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.06f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.2f;
            FadeOut(ps, false);
            Renderer(ps, Dot(), true);
            Start(ps);
            return ps;
        }

        /// <summary>Campfire flames: warm additive tongues rising from a small disc, shrinking as they climb.</summary>
        public static ParticleSystem Campfire(Transform parent, Vector3 worldPos)
        {
            var go = new GameObject("FX:Campfire");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = NewSystem(go);
            var main = ps.main;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.85f, 0.3f, 0.9f));
            main.maxParticles = 80;
            var em = ps.emission;
            em.rateOverTime = Reduced ? 6f : 36f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.14f;
            FadeOut(ps, false);
            Renderer(ps, Dot(), true);
            Start(ps);
            return ps;
        }

        /// <summary>Exhaust: a thin trail of grey-blue puffs; the caller drives the rate with the throttle.</summary>
        public static ParticleSystem Exhaust(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("FX:Exhaust");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // out the back
            var ps = NewSystem(go);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.22f);
            main.startColor = new Color(0.8f, 0.82f, 0.88f, 0.34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 60;
            main.gravityModifier = -0.03f;   // warm gas lifts
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.04f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            FadeOut(ps, true);
            Renderer(ps, Dot(), false);
            Start(ps);
            return ps;
        }

        /// <summary>Emission rate of a continuous system (0 switches it off without stopping the particles alive).</summary>
        public static void SetRate(ParticleSystem ps, float perSecond)
        {
            if (ps == null) return;
            var em = ps.emission;
            var rate = Reduced ? 0f : perSecond;
            em.rateOverTime = rate;
            // a looping system played with nothing to emit does not count as playing; wake it with the first rate
            if (rate > 0f && !ps.isPlaying) ps.Play();
        }

        /// <summary>
        /// A stopped, cleared system to configure. AddComponent starts a default system at once, and a prewarm on
        /// Play would fill the box with metre-sized white particles from the defaults — so every builder configures
        /// first and calls <see cref="ParticleSystem.Play()"/> last.
        /// </summary>
        private static ParticleSystem NewSystem(GameObject go)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            return ps;
        }

        /// <summary>Configured but not yet playing: the caller sets sizes and colours, then plays (prewarmed).</summary>
        private static ParticleSystem Ambient(Transform parent, string name, Texture2D tex, bool additive, Bounds box, float rate, float lifeMin, float lifeMax)
        {
            var go = new GameObject("FX:" + name);
            go.transform.SetParent(parent, false);
            go.transform.position = box.center;
            var ps = NewSystem(go);
            var main = ps.main;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = 0f;
            main.maxParticles = 80;
            var em = ps.emission;
            em.rateOverTime = Reduced ? 0f : rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box.size;
            FadeOut(ps, false);
            Renderer(ps, tex, additive);
            return ps;
        }
    }
}
