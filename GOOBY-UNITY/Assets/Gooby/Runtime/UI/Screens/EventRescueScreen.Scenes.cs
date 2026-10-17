using System.Collections.Generic;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The two events with a scene of their own (gewitter_szene.gd, mumie_szene.gd). Thunderstorm fear: the scene card is
    /// a dark room, Gooby is gone — only two eyes somewhere in the dark; a flashlight cone follows the finger, lightning
    /// lights everything for a blink with the thunder, a tap on the eyes inside the light finds him, three gentle strokes
    /// calm him and he falls asleep. Toilet-paper mummy: five paper bands wrap Gooby, every tap unwraps one (it lands
    /// crumpled in the scene card), after the last he hops free.
    /// </summary>
    public sealed partial class EventRescueScreen
    {
        private Label _hint;
        private VisualElement _buddyCol;
        private string _thanksKey;

        // ── thunderstorm ──
        private const float TorchRadius = 70f;
        private static Texture2D _torchTex;
        private VisualElement _dark, _eyes, _flash, _found;
        private Vector2 _torch = new Vector2(-1f, -1f);
        private float _lightningIn = 1.6f, _flashT;
        private int _stormStage, _pets;

        private void BuildStorm()
        {
            var t = _host.Strings;
            _scene.style.backgroundColor = new Color(0.12f, 0.13f, 0.25f);
            _scene.style.overflow = Overflow.Hidden;
            // the room in the dark: a sofa, a lamp and the window with rain streaks, only ever seen in the torch or a flash
            Silhouette(8f, 60f, 34f, 22f, new Color(0.55f, 0.36f, 0.45f));
            Silhouette(14f, 52f, 22f, 10f, new Color(0.62f, 0.42f, 0.52f));
            Silhouette(78f, 30f, 4f, 50f, new Color(0.46f, 0.42f, 0.38f));
            Silhouette(72f, 22f, 16f, 10f, new Color(0.95f, 0.85f, 0.55f));
            var window = Silhouette(40f, 8f, 26f, 30f, new Color(0.35f, 0.48f, 0.72f));
            for (var i = 0; i < 6; i++)
            {
                var drop = new VisualElement { pickingMode = PickingMode.Ignore };
                drop.style.position = Position.Absolute;
                drop.style.left = Length.Percent(8f + i * 15f); drop.style.top = Length.Percent(10f + (i % 3) * 25f);
                drop.style.width = 2; drop.style.height = 16;
                drop.style.backgroundColor = new Color(0.8f, 0.9f, 1f, 0.7f);
                window.Add(drop);
            }
            // Gooby's eyes somewhere in the dark
            _eyes = new VisualElement { name = "storm-eyes" };
            _eyes.style.position = Position.Absolute;
            _eyes.style.left = Length.Percent(Random.Range(12f, 78f));
            _eyes.style.top = Length.Percent(Random.Range(18f, 66f));
            _eyes.style.width = 70; _eyes.style.height = 34;
            _eyes.style.flexDirection = FlexDirection.Row;
            _eyes.style.justifyContent = Justify.SpaceBetween;
            foreach (var _ in new[] { 0, 1 })
            {
                var eye = new VisualElement { pickingMode = PickingMode.Ignore };
                eye.style.width = 28; eye.style.height = 32;
                eye.style.borderTopLeftRadius = eye.style.borderTopRightRadius = eye.style.borderBottomLeftRadius = eye.style.borderBottomRightRadius = 14;
                eye.style.backgroundColor = new Color(1f, 0.97f, 0.85f);
                eye.style.justifyContent = Justify.Center; eye.style.alignItems = Align.Center;
                var pupil = new VisualElement { pickingMode = PickingMode.Ignore };
                pupil.style.width = 14; pupil.style.height = 18;
                pupil.style.borderTopLeftRadius = pupil.style.borderTopRightRadius = pupil.style.borderBottomLeftRadius = pupil.style.borderBottomRightRadius = 7;
                pupil.style.backgroundColor = new Color(0.16f, 0.12f, 0.14f);
                eye.Add(pupil);
                _eyes.Add(eye);
            }
            _eyes.RegisterCallback<PointerDownEvent>(e => { TapEyes(); e.StopPropagation(); });
            // the warm cone of the torch, then the dark with its hole (kept centred on the finger)
            _glow = new VisualElement { name = "storm-glow", pickingMode = PickingMode.Ignore };
            _glow.style.position = Position.Absolute;
            _glow.style.width = TorchRadius * 2.6f; _glow.style.height = TorchRadius * 2.6f;
            _glow.style.backgroundImage = new StyleBackground(GlowTexture());
            _scene.Add(_glow);
            _dark = new VisualElement { name = "storm-dark", pickingMode = PickingMode.Ignore };
            _dark.style.position = Position.Absolute;
            _dark.style.backgroundImage = new StyleBackground(TorchTexture());
            _scene.Add(_dark);
            // only two eyes shine in the dark (gewitter_szene.gd) — a tap counts once the torch is on them
            _scene.Add(_eyes);
            _flash = new VisualElement { name = "storm-flash", pickingMode = PickingMode.Ignore };
            Ux.Abs(_flash, 0, 0, 0, 0);
            _flash.style.backgroundColor = new Color(0.92f, 0.95f, 1f);
            _flash.style.opacity = 0f;
            _scene.Add(_flash);
            _scene.RegisterCallback<PointerMoveEvent>(e => _torch = e.localPosition);
            _scene.RegisterCallback<PointerDownEvent>(e => { _torch = e.localPosition; if (_stormStage == 1) Pet(); });
            // he is gone: no Gooby beside the card until he is found
            if (_buddyCol != null) _buddyCol.style.display = DisplayStyle.None;
            _hint.text = t.T("events.gewitter.bubble");
            _left = 1;
        }

        private VisualElement Silhouette(float left, float top, float w, float h, Color c)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(left); e.style.top = Length.Percent(top);
            e.style.width = Length.Percent(w); e.style.height = Length.Percent(h);
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = 10;
            e.style.backgroundColor = c;
            _scene.Add(e);
            return e;
        }

        private static Texture2D _glowTex;
        private VisualElement _glow;

        /// <summary>The torch's warm light: a soft yellow disc fading out to its rim.</summary>
        private static Texture2D GlowTexture()
        {
            if (_glowTex != null) return _glowTex;
            const int n = 128;
            _glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "torch_glow" };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = (x + 0.5f) / n - 0.5f; var dy = (y + 0.5f) / n - 0.5f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    var a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color32(255, 236, 170, (byte)(a * a * 0.55f * 255));
                }
            _glowTex.SetPixels32(px);
            _glowTex.Apply();
            return _glowTex;
        }

        /// <summary>Dark everywhere, a soft round hole in the middle (alpha 0 inside the cone, 0.94 beyond its rim).</summary>
        private static Texture2D TorchTexture()
        {
            if (_torchTex != null) return _torchTex;
            const int n = 512;
            _torchTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "torch" };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = (x + 0.5f) / n - 0.5f; var dy = (y + 0.5f) / n - 0.5f;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.075f, d)) * 0.94f;
                    px[y * n + x] = new Color32(10, 10, 26, (byte)(a * 255));
                }
            _torchTex.SetPixels32(px);
            _torchTex.Apply();
            return _torchTex;
        }

        private bool EyesInLight()
        {
            if (_eyes == null || _torch.x < 0f) return false;
            var c = _eyes.layout.center;
            return Vector2.Distance(c, _torch) <= TorchRadius + 10f || _flashT > 0.05f;
        }

        private void TapEyes()
        {
            if (_stormStage != 0 || _done) return;
            if (!EyesInLight()) { Sfx.PlayUi("soft_tick"); return; }
            _stormStage = 1;
            Sfx.PlayUi("soft_open");
            Haptics.Tap();
            // the lights come back on: Gooby crouches where the eyes were, scared
            _dark.style.display = DisplayStyle.None;
            _glow.style.display = DisplayStyle.None;
            _scene.style.backgroundColor = new Color(0.32f, 0.33f, 0.5f);
            _found = Ux.Portrait(110);
            _found.name = "storm-gooby";
            _found.style.position = Position.Absolute;
            _found.style.left = _eyes.style.left; _found.style.top = _eyes.style.top;
            _found.style.translate = new Translate(-20, -40);
            _eyes.RemoveFromHierarchy();
            _scene.Add(_found);
            if (_buddyCol != null) _buddyCol.style.display = DisplayStyle.Flex;
            _buddy?.Rest("scared");
            _say?.Invoke(_host.Strings.T("events.gewitter.gefunden"));
            _hint.text = _host.Strings.T("events.gewitter.streicheln");
            Debug.Log("[ereignis] gewitter: found");
        }

        private void Pet()
        {
            if (_stormStage != 1 || _done) return;
            _pets++;
            Sfx.Play("pet_squish", Random.Range(-1f, 2f), 0.8f);
            Haptics.Tap();
            if (_found != null) _fx.Burst(_found.worldBound.center, new Color(1f, 0.55f, 0.66f), 12);
            _buddy?.Cheer(1, 0);
            if (_pets < 3) return;
            _stormStage = 2;
            _buddy?.Rest("sleepy");
            _thanksKey = "events.gewitter.danke";
            Debug.Log("[ereignis] gewitter: calmed");
            Finish();
        }

        private void TickStorm(float dt)
        {
            if (_dark == null || _stormStage != 0) return;
            var w = _scene.resolvedStyle.width; var h = _scene.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 1f) return;
            if (_torch.x < 0f) _torch = new Vector2(w * 0.5f, h * 0.5f);
            // the hole is clear to 0.04 of the texture's side and dark from 0.075: sized so the cone keeps about TorchRadius
            // pixels, and the square (1400 px) still covers the whole card wherever the finger is
            var side = TorchRadius / 0.05f;
            _dark.style.width = side; _dark.style.height = side;
            _dark.style.left = _torch.x - side * 0.5f; _dark.style.top = _torch.y - side * 0.5f;
            var g = TorchRadius * 1.3f;
            _glow.style.left = _torch.x - g; _glow.style.top = _torch.y - g;
            _lightningIn -= dt;
            if (_lightningIn <= 0f)
            {
                _lightningIn = Random.Range(2.6f, 5.2f);
                _flashT = 0.35f;
                Sfx.Play("amb_donner", Random.Range(-2f, 2f), 0.7f);
                Haptics.Tap();
            }
            if (_flashT > 0f)
            {
                _flashT = Mathf.Max(0f, _flashT - dt);
                // a double flicker, then the dark again
                var k = _flashT > 0.2f ? 0.85f : _flashT > 0.12f ? 0.15f : _flashT / 0.12f * 0.7f;
                if (_host.Settings.ReducedMotion) k *= 0.4f;
                _flash.style.opacity = k;
            }
        }

        // ── toilet-paper mummy ──
        private readonly List<VisualElement> _wraps = new List<VisualElement>();
        private int _crumpled;

        private void BuildMummy()
        {
            var t = _host.Strings;
            _hint.text = t.T("events.mumie.bubble");
            // the bands go over the 3D Gooby beside the card, or over his portrait on it
            VisualElement host;
            if (_buddyCol != null) host = _buddyCol;
            else
            {
                var portrait = Ux.Portrait(150);
                portrait.style.position = Position.Absolute;
                portrait.style.left = Length.Percent(36); portrait.style.top = Length.Percent(12);
                _scene.Add(portrait);
                host = portrait;
            }
            var layer = new VisualElement { name = "mummy-wraps", pickingMode = PickingMode.Ignore };
            Ux.Abs(layer, 0, 0, 0, 0);
            host.Add(layer);
            const int bands = 5;
            for (var i = 0; i < bands; i++)
            {
                var band = new VisualElement { pickingMode = PickingMode.Ignore };
                band.style.position = Position.Absolute;
                band.style.left = Length.Percent(12 + (i % 2) * 4); band.style.right = Length.Percent(12 + ((i + 1) % 2) * 4);
                band.style.top = Length.Percent(22 + i * 13);
                band.style.height = 26;
                band.style.borderTopLeftRadius = band.style.borderTopRightRadius = band.style.borderBottomLeftRadius = band.style.borderBottomRightRadius = 9;
                band.style.backgroundColor = new Color(1f, 1f, 0.98f, 0.97f);
                band.style.borderBottomWidth = 3; band.style.borderBottomColor = new Color(0.86f, 0.84f, 0.8f);
                band.style.rotate = new Rotate((i % 2 == 0 ? 1f : -1f) * Random.Range(4f, 9f));
                layer.Add(band);
                _wraps.Add(band);
            }
            host.pickingMode = PickingMode.Position;
            host.RegisterCallback<PointerDownEvent>(e => { Unwrap(); e.StopPropagation(); });
            _scene.RegisterCallback<PointerDownEvent>(_ => Unwrap());
            _buddy?.Rest("sad");
            _left = bands;
        }

        private void Unwrap()
        {
            if (_done || _wraps.Count == 0) return;
            var band = _wraps[0];
            _wraps.RemoveAt(0);
            var from = band.worldBound.center;
            band.RemoveFromHierarchy();
            _fx.Burst(from, Color.white, 14);
            Sfx.Play("toy_spritz", Random.Range(2f, 5f), 0.6f);
            Haptics.Tap();
            _buddy?.Cheer(1, 0);
            // the paper lands crumpled on the card
            var ball = new VisualElement { pickingMode = PickingMode.Ignore };
            ball.style.position = Position.Absolute;
            ball.style.left = Length.Percent(10 + _crumpled * 17); ball.style.top = Length.Percent(Random.Range(62f, 74f));
            ball.style.width = 44; ball.style.height = 36;
            ball.style.borderTopLeftRadius = ball.style.borderTopRightRadius = ball.style.borderBottomLeftRadius = ball.style.borderBottomRightRadius = 16;
            ball.style.backgroundColor = new Color(1f, 1f, 0.97f);
            ball.style.borderBottomWidth = 3; ball.style.borderBottomColor = new Color(0.84f, 0.82f, 0.78f);
            ball.style.rotate = new Rotate(Random.Range(-30f, 30f));
            _scene.Add(ball);
            _crumpled++;
            _left = _wraps.Count;
            if (_left > 0)
            {
                if (_left == 2) _say?.Invoke(_host.Strings.T("events.mumie.wickel"));
                return;
            }
            _thanksKey = "events.mumie.danke";
            Debug.Log("[ereignis] mumie: unwrapped");
            Finish();
        }

        private void TickScenes(float dt) => TickStorm(dt);

        /// <summary>QA: "finden" (the torch onto the eyes and a tap), "streicheln", "wickeln".</summary>
        public void QaAction(string action)
        {
            switch (action)
            {
                case "finden":
                    if (_eyes != null) { _torch = _eyes.layout.center; TapEyes(); }
                    break;
                case "streicheln": Pet(); break;
                case "wickeln": Unwrap(); break;
            }
        }
    }
}
