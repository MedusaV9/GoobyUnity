using Gooby.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace Gooby.Tests.World
{
    /// <summary>
    /// The street's distance haze is a render setting, so a stale value would follow Gooby into a room or the
    /// garden; these pin down that it is only ever on when asked, and that it fades towards a lighter sky, never
    /// towards grey (a grey haze is what made the first outdoor scenes read as overcast).
    /// </summary>
    public class SceneDressingTests
    {
        private bool _fog;
        private FogMode _mode;
        private float _density;
        private Color _color;

        [SetUp]
        public void Remember()
        {
            _fog = RenderSettings.fog;
            _mode = RenderSettings.fogMode;
            _density = RenderSettings.fogDensity;
            _color = RenderSettings.fogColor;
        }

        [TearDown]
        public void Restore()
        {
            RenderSettings.fog = _fog;
            RenderSettings.fogMode = _mode;
            RenderSettings.fogDensity = _density;
            RenderSettings.fogColor = _color;
        }

        [Test]
        public void HazeIsFaintExp2TowardsALighterSky()
        {
            var sky = new Color(0.44f, 0.70f, 0.95f);
            SceneDressing.SetHaze(true, sky);
            Assert.That(RenderSettings.fog, Is.True);
            Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.ExponentialSquared));
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(SceneDressing.HazeDensity));
            var fog = RenderSettings.fogColor;
            Assert.That(fog.r, Is.GreaterThan(sky.r), "haze lifts towards white");
            Assert.That(fog.b, Is.GreaterThanOrEqualTo(sky.b - 0.001f));
            Assert.That(fog.b - fog.r, Is.GreaterThan(0.3f), "…but stays a blue sky tone, not grey");
            // ≈24 % at the 330 m horizon painting, ≈13 % at the clouds: subtle, never a fog bank
            var atHorizon = 1f - Mathf.Exp(-Mathf.Pow(330f * SceneDressing.HazeDensity, 2f));
            var atClouds = 1f - Mathf.Exp(-Mathf.Pow(230f * SceneDressing.HazeDensity, 2f));
            Assert.That(atHorizon, Is.InRange(0.18f, 0.32f));
            Assert.That(atClouds, Is.InRange(0.08f, 0.2f));
        }

        [Test]
        public void HazeOffLeavesNoFog()
        {
            SceneDressing.SetHaze(true, Color.cyan);
            SceneDressing.SetHaze(false, Color.cyan);
            Assert.That(RenderSettings.fog, Is.False);
        }
    }
}
