using System.Collections.Generic;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The ranch's weather on a 3D set (the farm, Hufingen) — the stage half of ranch_wetter_controller.gd over the shared
    /// day plan (<see cref="RanchWetter"/>): rain and drizzle as streaks with splash rings over the yard, the studio light
    /// dimmed under the clouds, fog as a haze over the back of the set, lightning as a short flash of the key light with the
    /// thunder a moment later, and puddles that grow with the ground's wetness and shrink as it dries. Reduced motion keeps
    /// the light, the haze and the puddles, without particles or flashes.
    /// </summary>
    public sealed class StageWeather
    {
        public const float FlashPauseMin = 3.5f, FlashPauseMax = 9f, FlashSeconds = 0.16f, FlashBoost = 2.6f;
        /// <summary>Puddles show from this wetness on and reach their full size at <see cref="PuddleFull"/>.</summary>
        public const float PuddleFrom = 0.12f, PuddleFull = 0.6f;

        private readonly MgStage3D _stage;
        private readonly WeatherFx _fx;
        private readonly List<(Transform t, Vector2 size)> _puddles = new List<(Transform, Vector2)>();
        private readonly System.Random _rng = new System.Random(77);
        private float _flashIn = 2.5f, _flashLeft, _thunderIn = -1f, _puddleNow, _puddleTarget;
        private bool _active = true;
        private static Texture2D _puddleTex;

        public RanchWetter.Zustand Zustand { get; private set; } = new RanchWetter.Zustand { Typ = "sonne", LichtFaktor = 1.0 };
        public WetterFx.Plan Plan { get; private set; } = new WetterFx.Plan();
        /// <summary>Lightning flashes so far (QA).</summary>
        public int Flashes { get; private set; }
        public bool Raining => _fx.Raining;
        public bool Splashing => _fx.Splashing;
        public bool Foggy => _active && RenderSettings.fog && Plan.Nebel > 0.05;
        public int PuddlesShown { get; private set; }

        public StageWeather(MgStage3D stage, Vector3 centerLocal, Vector3 extents, float amount, IEnumerable<Vector3> puddleSpots)
        {
            _stage = stage;
            _fx = new WeatherFx(stage.Root.transform, 811) { Quiet = true };
            _fx.Place(WeatherFx.Stage.Garden, stage.Root.transform.position + MgStage3D.Origin + centerLocal, extents, 9f, amount);
            var mat = ArtLibrary.Particles(PuddleTexture(), false);
            var i = 0;
            foreach (var spot in puddleSpots)
            {
                var w = 1.2f + (i % 3) * 0.5f;
                var go = ArtLibrary.Quad(stage.Root.transform, "Puddle", stage.Root.transform.position + MgStage3D.Origin + spot + Vector3.up * 0.03f,
                    Quaternion.Euler(-90f, i * 53f % 180f, 0f), new Vector2(w, w * 0.6f), mat);
                go.SetActive(false);
                _puddles.Add((go.transform, new Vector2(w, w * 0.6f)));
                i++;
            }
        }

        /// <summary>The weather of this moment (or a forced one); <paramref name="reduced"/> drops particles and flashes.</summary>
        public void Update(RanchWetter.Zustand zustand, bool reduced)
        {
            Zustand = zustand ?? new RanchWetter.Zustand { Typ = "sonne", LichtFaktor = 1.0 };
            var lage = new SoulWetter.Lage { Typ = Zustand.Typ, Regen = Zustand.Regnet, Schnee = false, Basis = Zustand };
            Plan = WetterFx.Fuer(lage, false, reduced);
            _fx.Apply(_active ? Plan : new WetterFx.Plan(), GraphicsQuality.ParticleFactor);
            _puddleTarget = Mathf.InverseLerp(PuddleFrom, PuddleFull, (float)Zustand.Naesse);
            if (Zustand.Naesse < PuddleFrom) _puddleTarget = 0f;
            if (_active) ApplyLight(0f);
        }

        /// <summary>Covered by another screen: the studio and the fog go back to clear, the particles rest.</summary>
        public void SetActive(bool on)
        {
            _active = on;
            _fx.Apply(on ? Plan : new WetterFx.Plan(), GraphicsQuality.ParticleFactor);
            if (on)
            {
                ApplyLight(0f);
                return;
            }
            _flashLeft = 0f;
            _thunderIn = -1f;
            MgStudio.Weather(1f, 1f);
            RenderSettings.fog = false;
        }

        public void Tick(float dt)
        {
            if (!_active || dt <= 0f) return;
            _fx.Tick(dt, _stage.Camera, null);
            TickPuddles(dt);
            TickFlash(dt);
        }

        private void ApplyLight(float flash)
        {
            MgStudio.Weather((float)Plan.SonnenFaktor, (float)Plan.AmbientFaktor, flash, (float)Plan.Grau);
            var haze = (float)Plan.Nebel;
            RenderSettings.fog = haze > 0.05f;
            if (!RenderSettings.fog) return;
            // the build keeps only the exp² fog variants (GraphicsSettings m_FogKeepExp2); the set is ~30 m deep:
            // thick fog swallows the stable and the hills, a rain haze only softens them
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Lerp(0.004f, 0.046f, haze);
            var sky = _stage.Camera != null ? _stage.Camera.backgroundColor : new Color(0.75f, 0.8f, 0.86f);
            RenderSettings.fogColor = Color.Lerp(sky, new Color(0.84f, 0.86f, 0.9f), sky.grayscale < 0.4f ? 0.3f : 0.65f);
        }

        private void TickPuddles(float dt)
        {
            _puddleNow = Mathf.MoveTowards(_puddleNow, _puddleTarget, dt * 0.25f);
            var shown = 0;
            foreach (var (t, size) in _puddles)
            {
                var k = Mathf.SmoothStep(0f, 1f, _puddleNow);
                var on = k > 0.03f;
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
                if (!on) continue;
                t.localScale = new Vector3(size.x * k, size.y * k, 1f);
                shown++;
            }
            PuddlesShown = shown;
        }

        private void TickFlash(float dt)
        {
            if (_thunderIn >= 0f)
            {
                _thunderIn -= dt;
                if (_thunderIn < 0f) Sfx.Play("amb_donner", Random.Range(-1.5f, 0.5f), 0.9f);
            }
            if (!Plan.Blitz)
            {
                if (_flashLeft > 0f) { _flashLeft = 0f; ApplyLight(0f); }
                return;
            }
            _flashIn -= dt;
            if (_flashIn <= 0f)
            {
                _flashIn = Mathf.Lerp(FlashPauseMin, FlashPauseMax, (float)_rng.NextDouble());
                _flashLeft = FlashSeconds;
                _thunderIn = Mathf.Lerp(0.4f, 1.8f, (float)_rng.NextDouble());
                Flashes++;
            }
            if (_flashLeft <= 0f) return;
            _flashLeft -= dt;
            MgStudio.Weather((float)Plan.SonnenFaktor, (float)Plan.AmbientFaktor, _flashLeft > 0f ? FlashBoost * Mathf.Clamp01(_flashLeft / FlashSeconds) : 0f, (float)Plan.Grau);
        }

        /// <summary>A forced weather like ranch_wetter_controller.gd's override ("regenbogen" = sun over wet ground).</summary>
        public static RanchWetter.Zustand Erzwungen(string typ)
        {
            if (string.IsNullOrEmpty(typ)) return null;
            var naesse = 0.0;
            var regenbogen = false;
            if (typ == "regenbogen")
            {
                typ = "sonne";
                naesse = 0.5;
                regenbogen = true;
            }
            else if (System.Array.IndexOf(RanchWetter.RegenTypen, typ) >= 0) naesse = 0.8;
            if (System.Array.IndexOf(RanchWetter.Typen, typ) < 0) return null;
            return new RanchWetter.Zustand
            {
                Typ = typ, Vorher = typ, Blend = 1.0, Intensitaet = 0.85, Wind = typ == "gewitter" ? 0.9 : 0.45,
                Bewoelkung = RanchWetter.BewoelkungVon(typ), Naesse = naesse, Regenbogen = regenbogen,
                LichtFaktor = RanchWetter.LichtVon(typ), NebelDichte = typ == "nebel" ? 0.8 : 0.0, NameKey = "rwelt.wetter." + typ,
            };
        }

        /// <summary>A puddle seen from above: slate water with the sky's light on its rim, soft at the edge.</summary>
        private static Texture2D PuddleTexture()
        {
            if (_puddleTex != null) return _puddleTex;
            const int n = 64;
            _puddleTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "fx_puddle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            var water = new Color(0.42f, 0.5f, 0.62f);
            var sheen = new Color(0.82f, 0.88f, 0.96f);
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = (x + 0.5f) / n * 2f - 1f;
                    var dy = (y + 0.5f) / n * 2f - 1f;
                    // a slightly lumpy outline so the puddles do not read as discs
                    var ang = Mathf.Atan2(dy, dx);
                    var edge = 0.86f + 0.07f * Mathf.Sin(ang * 3f + 0.6f) + 0.04f * Mathf.Sin(ang * 5f + 2.1f);
                    var r = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    var a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.82f, 1f, r)) * 0.72f;
                    var rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.12f) * Mathf.Clamp01(0.4f - dy * 0.6f);
                    var c = Color.Lerp(water, sheen, rim * 0.8f);
                    px[y * n + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
                }
            _puddleTex.SetPixels32(px);
            _puddleTex.Apply(false, true);
            return _puddleTex;
        }
    }
}
