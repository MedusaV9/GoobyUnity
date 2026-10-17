using Gooby.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace Gooby.Tests.World
{
    /// <summary>
    /// The particle builders of the new look must actually produce particles of the configured size and colour:
    /// a system played before its configuration (prewarm from the defaults) once filled the lawn with metre-sized
    /// white "petals", and a continuous system that never emits is invisible in the player.
    /// </summary>
    public class EffectsTests
    {
        private GameObject _root;

        [SetUp]
        public void Setup()
        {
            Effects.Reduced = false;
            _root = new GameObject("fx-root");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private static ParticleSystem.Particle[] Alive(ParticleSystem ps)
        {
            var buf = new ParticleSystem.Particle[ps.main.maxParticles];
            var n = ps.GetParticles(buf);
            System.Array.Resize(ref buf, n);
            return buf;
        }

        [Test]
        public void PetalsArePetalSizedAndTinted()
        {
            var ps = Effects.Petals(_root.transform, new Bounds(Vector3.zero, new Vector3(10f, 2f, 6f)));
            ps.Simulate(3f, true, true);
            var alive = Alive(ps);
            Assert.That(alive.Length, Is.GreaterThan(3), "petals should be drifting after three seconds");
            foreach (var p in alive)
            {
                Assert.That(p.startSize, Is.InRange(0.09f, 0.18f), "a petal is a few centimetres, not the 1 m default");
                // Particle.startColor is a Color32: white would be 3 × 255
                Assert.That(p.startColor.r + p.startColor.g + p.startColor.b, Is.LessThan(740), "petals are tinted, not white");
            }
        }

        [Test]
        public void FirefliesGlowAtNight()
        {
            var ps = Effects.Fireflies(_root.transform, new Bounds(Vector3.zero, new Vector3(10f, 1f, 6f)));
            ps.Simulate(3f, true, true);
            Assert.That(Alive(ps).Length, Is.GreaterThan(2));
        }

        [Test]
        public void SunMotesDriftDownTheShaft()
        {
            var ps = Effects.SunMotes(_root.transform, new Vector3(3f, 2.4f, 1f), new Vector3(-1f, -1f, 0f), 4f, 0.4f);
            ps.Simulate(4f, true, true);
            var alive = Alive(ps);
            Assert.That(alive.Length, Is.GreaterThan(10));
            foreach (var p in alive) Assert.That(p.startSize, Is.LessThan(0.1f));
        }

        [Test]
        public void ExhaustFollowsTheRate()
        {
            var ps = Effects.Exhaust(_root.transform, new Vector3(-0.5f, 0.3f, -1.9f));
            ps.Simulate(2f, true, false);
            Assert.That(Alive(ps).Length, Is.EqualTo(0), "no throttle, no smoke");
            // Unity does not count a looping system with nothing to emit as playing: the first rate must wake it
            Effects.SetRate(ps, 8f);
            Assert.That(ps.isPlaying, Is.True, "smoke needs a playing system, whatever Play() did with a zero rate");
            ps.Simulate(1.5f, true, false);
            Assert.That(Alive(ps).Length, Is.GreaterThan(3), "smoke once the rig sets a rate");
        }

        [Test]
        public void AmbientSystemsAreLeftPlaying()
        {
            var petals = Effects.Petals(_root.transform, new Bounds(Vector3.zero, new Vector3(10f, 2f, 6f)));
            Assert.That(petals.isPlaying, Is.True);
            Assert.That(petals.main.playOnAwake, Is.True, "re-enabled at dawn, the petals must start by themselves");
            var motes = Effects.SunMotes(_root.transform, Vector3.up * 2f, Vector3.down, 2f, 0.3f);
            Assert.That(motes.isPlaying, Is.True);
        }

        [Test]
        public void ReducedMotionKeepsAmbientSystemsQuiet()
        {
            Effects.Reduced = true;
            var ps = Effects.Petals(_root.transform, new Bounds(Vector3.zero, new Vector3(10f, 2f, 6f)));
            ps.Simulate(3f, true, true);
            Assert.That(Alive(ps).Length, Is.EqualTo(0));
            Effects.Reduced = false;
        }

        [Test]
        public void BurstsEmitIntoThePooledSystems()
        {
            Effects.Puff(Vector3.zero, Color.white, 6, 0.2f, 0.5f, 0.6f, Vector3.up);
            Effects.Sparkle(Vector3.zero);
            var puffs = GameObject.Find("FX:puffs")?.GetComponent<ParticleSystem>();
            var glints = GameObject.Find("FX:glints")?.GetComponent<ParticleSystem>();
            Assert.That(puffs, Is.Not.Null);
            Assert.That(glints, Is.Not.Null);
            Assert.That(puffs.particleCount, Is.EqualTo(6));
            Assert.That(glints.particleCount, Is.EqualTo(22));
        }
    }
}
