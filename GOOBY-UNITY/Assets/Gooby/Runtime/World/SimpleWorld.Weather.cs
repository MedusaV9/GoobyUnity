using System;
using System.Globalization;
using Gooby.Core.Life;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The home weather made visible (SoulWetter → <see cref="WetterFx"/> → <see cref="WeatherFx"/>): rain, splash rings,
    /// snow and lightning over the garden lawn and along the street, the rain loop quietly at home; the outdoor light dims
    /// and greys under clouds (<see cref="SceneDressing.ApplyWeather"/>). Interiors, minigames (world camera off) and the
    /// travel cutscene stay dry. The plan follows the game clock — for a screenshot of a weather, start the player with
    /// <c>GOOBY_FAKETIME</c> at a matching hour (no debug switch).
    /// </summary>
    public sealed partial class SimpleWorld
    {
        public const float WeatherRefreshSec = 20f;

        private WeatherFx _weather;
        private float _weatherTimer;

        /// <summary>The weather of the current hour (also behind the outdoor light).</summary>
        public WetterFx.Plan Weather { get; private set; } = new WetterFx.Plan();
        public WeatherFx WeatherView => _weather;

        private WetterFx.Plan WeatherPlan(bool indoor)
        {
            if (_gs == null) return new WetterFx.Plan();
            var now = _gs.Clock.NowMs();
            var local = DateTimeOffset.FromUnixTimeMilliseconds(now).ToOffset(TimeSpan.FromMinutes(_gs.Clock.UtcOffsetMinutes(now)));
            var lage = SoulWetter.Zustand(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.Hour + local.Minute / 60.0 + local.Second / 3600.0);
            return WetterFx.Fuer(lage, indoor, ReducedMotion);
        }

        private WeatherFx.Stage WeatherStage()
        {
            if (Camera == null || !Camera.enabled || _cutscene != null || _interior != null || _vacation != null) return WeatherFx.Stage.Off;
            if (_city != null && _city.Root.activeSelf) return WeatherFx.Stage.Street;
            if (InGarden && _garden != null && _garden.Root.activeSelf) return WeatherFx.Stage.Garden;
            return _home != null && _home.activeSelf ? WeatherFx.Stage.Home : WeatherFx.Stage.Off;
        }

        private void TickWeather(float dt)
        {
            var stage = WeatherStage();
            if (_weather == null && stage == WeatherFx.Stage.Off) return;
            if (_weather == null) _weather = new WeatherFx(_root.transform, 4711);
            if (stage != _weather.Where)
            {
                switch (stage)
                {
                    case WeatherFx.Stage.Garden: _weather.Place(stage, _garden.WeatherCenter, _garden.WeatherExtents, 9f, 1f); break;
                    // the chase camera sees the drops nearest to it: a box close around it, densely filled
                    case WeatherFx.Stage.Street: _weather.Place(stage, Vector3.zero, new Vector3(16f, 1f, 16f), 12f, 2.6f); break;
                    default: _weather.Place(stage, Vector3.zero, Vector3.one, 1f); break;
                }
                _weatherTimer = 0f;
            }
            _weatherTimer -= dt;
            if (_weatherTimer <= 0f)
            {
                _weatherTimer = WeatherRefreshSec;
                Weather = WeatherPlan(stage == WeatherFx.Stage.Home);
                _weather.Apply(Weather, GraphicsQuality.ParticleFactor);
                _garden?.SetWet(Weather.Nass);
            }
            _weather.Tick(dt, Camera, Look);
        }

        /// <summary>The weather on the outdoor light of the hour (after the sky, the lamps and the town haze).</summary>
        private Color ApplyWeatherLight(double hour, Color sky)
        {
            if (_interior != null) return sky;
            Weather = WeatherPlan(false);
            var overcast = SceneDressing.Overcast(Weather);
            _city?.SetOvercast(overcast);
            _garden?.SetOvercast(overcast);
            return SceneDressing.ApplyWeather(Look, Weather, hour, sky, garden: _city == null || !_city.Root.activeSelf);
        }
    }
}
