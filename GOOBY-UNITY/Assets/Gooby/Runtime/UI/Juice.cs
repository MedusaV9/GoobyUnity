using System;
using System.Collections.Generic;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI
{
    /// <summary>
    /// Shared game-feel kit for UI Toolkit screens (port of juice_kit.gd): float texts, scale pops, trauma shake,
    /// spark bursts and confetti on one Painter2D canvas, a full-screen flash, fade-outs and number count-ups.
    /// Everything is advanced by <see cref="Tick"/> from the owner's Update with Time.unscaledDeltaTime — no
    /// scheduler steps, no tweens that outlive the owner. Allocation-light: float labels are pooled, particles are
    /// structs in one list, the canvas only repaints while something is alive. Reduced Motion (settings): no shake,
    /// no pops or flashes, a third of the particles, float texts fade in place.
    /// </summary>
    public sealed class Juice
    {
        /// <summary>juice_kit.gd SHAKE_MAX_OFFSET / SHAKE_DECAY: offset = trauma² × 14 px, trauma −2,2 per second.</summary>
        public const float ShakeMaxOffset = 14f, ShakeDecay = 2.2f;
        public const int MaxFloats = 24, MaxParticles = 360;

        /// <summary>The overlay (full rect of its parent, never takes input). The owner adds it where it belongs.</summary>
        public VisualElement Layer { get; }
        /// <summary>Element the shake moves (px translate) unless <see cref="ApplyShake"/> is set.</summary>
        public VisualElement ShakeTarget { get; set; }
        /// <summary>Custom shake writer (e.g. a field that is centred with a percent translate); gets Vector2.zero at rest.</summary>
        public Action<Vector2> ApplyShake { get; set; }

        private readonly ParticleCanvas _canvas;
        private readonly VisualElement _flash;
        private readonly List<FloatItem> _floats = new List<FloatItem>();
        private readonly Stack<Label> _labelPool = new Stack<Label>();
        private readonly List<PopItem> _pops = new List<PopItem>();
        private readonly List<FadeItem> _fades = new List<FadeItem>();
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly List<Counter> _counters = new List<Counter>();
        private readonly System.Random _rng = new System.Random(20260916);
        private float _trauma;
        private bool _shaken;
        private float _flashAge, _flashDur;
        private bool _canvasDirty, _flashOn;

        private struct FloatItem { public Label L; public float Age, Life, Rise; public bool Static; }
        private struct PopItem { public VisualElement E; public float Age, Dur, Amount; }
        private struct FadeItem { public VisualElement E; public float Age, Dur; }
        private struct Particle { public Vector2 P, V; public float Life, Max, Size, Spin, SpinV, Gravity; public Color C; public bool Confetti; }
        private sealed class Counter { public Label L; public int From, To, Last, Steps; public float Age, Dur; public Action<int> OnTick; public Func<int, string> Format; }

        /// <summary>The confetti palette (board 01/06: strawberry, sun, teal, sky, leaf, rose).</summary>
        public static readonly Color[] Palette =
        {
            new Color(1f, 0.545f, 0.545f), new Color(1f, 0.82f, 0.4f), new Color(0.243f, 0.812f, 0.698f),
            new Color(0.498f, 0.722f, 1f), new Color(0.498f, 0.812f, 0.608f), new Color(1f, 0.596f, 0.757f),
        };

        public Juice()
        {
            Layer = new VisualElement { name = "juice", pickingMode = PickingMode.Ignore };
            Layer.style.position = Position.Absolute;
            Layer.style.left = 0; Layer.style.top = 0; Layer.style.right = 0; Layer.style.bottom = 0;
            _flash = new VisualElement { name = "juice-flash", pickingMode = PickingMode.Ignore };
            _flash.style.position = Position.Absolute;
            _flash.style.left = 0; _flash.style.top = 0; _flash.style.right = 0; _flash.style.bottom = 0;
            _flash.style.display = DisplayStyle.None;
            Layer.Add(_flash);
            _canvas = new ParticleCanvas(this);
            Layer.Add(_canvas);
        }

        public static bool ReducedMotion
        {
            get
            {
                var settings = GameHost.Instance != null ? GameHost.Instance.Settings : null;
                return settings != null && settings.ReducedMotion;
            }
        }

        // ── float texts ─────────────────────────────────────────────────────

        /// <summary>A label that pops in and drifts up while fading (juice_kit float_text) at a panel position, on the overlay.</summary>
        public void FloatText(Vector2 panelPos, string text, Color color, float size = 30f, float rise = 64f)
        {
            var local = Layer.panel != null ? Layer.WorldToLocal(panelPos) : panelPos;
            FloatTextIn(Layer, local, text, color, size, rise);
        }

        /// <summary>Same, inside another container at its local position (a play field keeps its floats clipped and shaking with it).</summary>
        public void FloatTextIn(VisualElement parent, Vector2 localPos, string text, Color color, float size = 30f, float rise = 64f)
        {
            if (parent == null || string.IsNullOrEmpty(text)) return;
            if (_floats.Count >= MaxFloats) RemoveFloat(0);
            var l = _labelPool.Count > 0 ? _labelPool.Pop() : NewFloatLabel();
            l.text = text;
            l.style.color = color;
            l.style.fontSize = size;
            l.style.left = localPos.x - 150f;
            l.style.top = localPos.y - size * 0.75f;
            l.style.opacity = 1f;
            l.style.translate = new Translate(0f, 0f);
            l.style.scale = new Scale(new Vector3(0.7f, 0.7f, 1f));
            parent.Add(l);
            _floats.Add(new FloatItem { L = l, Age = 0f, Life = 0.8f, Rise = rise, Static = ReducedMotion });
        }

        private static Label NewFloatLabel()
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("gb-float");
            l.style.position = Position.Absolute;
            l.style.width = 300f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            return l;
        }

        private void RemoveFloat(int i)
        {
            var l = _floats[i].L;
            _floats.RemoveAt(i);
            l.RemoveFromHierarchy();
            if (_labelPool.Count < MaxFloats) _labelPool.Push(l);
        }

        // ── pops, fades, flash, shake ───────────────────────────────────────

        /// <summary>Scale pop around the centre (juice_kit scale_pop): up to <paramref name="amount"/>, springs back. Repeats restart, never stack.</summary>
        public void ScalePop(VisualElement e, float amount = 1.18f, float seconds = 0.22f)
        {
            if (e == null || ReducedMotion) return;
            for (var i = 0; i < _pops.Count; i++)
            {
                if (_pops[i].E != e) continue;
                _pops[i] = new PopItem { E = e, Age = 0f, Dur = Mathf.Max(0.05f, seconds), Amount = amount };
                return;
            }
            _pops.Add(new PopItem { E = e, Age = 0f, Dur = Mathf.Max(0.05f, seconds), Amount = amount });
        }

        /// <summary>Fades an element's opacity to 0 and hides it (display none) at the end; reduced motion hides at once.</summary>
        public void FadeOut(VisualElement e, float seconds = 0.2f)
        {
            if (e == null) return;
            for (var i = _fades.Count - 1; i >= 0; i--) if (_fades[i].E == e) _fades.RemoveAt(i);
            if (ReducedMotion || seconds <= 0.01f) { e.style.display = DisplayStyle.None; return; }
            _fades.Add(new FadeItem { E = e, Age = 0f, Dur = seconds });
        }

        /// <summary>Stops a running fade (the element is shown again by its owner).</summary>
        public void CancelFade(VisualElement e)
        {
            for (var i = _fades.Count - 1; i >= 0; i--) if (_fades[i].E == e) _fades.RemoveAt(i);
            if (e != null) e.style.opacity = 1f;
        }

        /// <summary>Full-screen colour flash that fades out over <paramref name="seconds"/> (juice_kit hit_flash).</summary>
        public void Flash(Color color, float seconds = 0.11f)
        {
            if (ReducedMotion) return;
            _flashDur = Mathf.Max(0.02f, seconds);
            _flashAge = 0f;
            _flash.style.backgroundColor = color;
            _flash.style.opacity = 1f;
            _flash.style.display = DisplayStyle.Flex;
            _flashOn = true;
        }

        /// <summary>Adds trauma (0..1); the offset is trauma² × 14 px and decays by 2,2 per second.</summary>
        public void Shake(float trauma)
        {
            if (ReducedMotion || trauma <= 0f) return;
            _trauma = Mathf.Clamp01(_trauma + trauma);
        }

        // ── particles ───────────────────────────────────────────────────────

        /// <summary>Spark burst at a panel position (juice_kit burst): squares flying out and falling, 0,55 s.</summary>
        public void Burst(Vector2 panelPos, Color color, int n = 14)
        {
            var at = Layer.panel != null ? Layer.WorldToLocal(panelPos) : panelPos;
            n = ReducedMotion ? Mathf.Max(1, n / 3) : n;
            for (var i = 0; i < n && _particles.Count < MaxParticles; i++)
            {
                var a = Rand(0f, Mathf.PI * 2f);
                var sp = Rand(140f, 300f);
                var c = Color.Lerp(color, Color.white, Rand(0f, 0.35f));
                _particles.Add(new Particle
                {
                    P = at, V = new Vector2(Mathf.Cos(a) * sp, Mathf.Sin(a) * sp - 120f), Life = 0.55f, Max = 0.55f,
                    Size = Rand(2.5f, 5f), Spin = Rand(0f, 6.28f), SpinV = Rand(-8f, 8f), Gravity = 640f, C = c,
                });
            }
            _canvasDirty = true;
        }

        /// <summary>Confetti rain from the top edge over the whole layer (juice_kit confetti).</summary>
        public void Confetti(int n = 70)
        {
            var w = Layer.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0f) w = UiRoot.ReferenceWidth;
            n = ReducedMotion ? Mathf.Max(1, n / 3) : n;
            for (var i = 0; i < n && _particles.Count < MaxParticles; i++)
            {
                var life = Rand(1.6f, 2.3f);
                _particles.Add(new Particle
                {
                    P = new Vector2(Rand(0f, w), Rand(-60f, -8f)), V = new Vector2(Rand(-70f, 70f), Rand(60f, 210f)), Life = life, Max = life,
                    Size = Rand(5f, 9f), Spin = Rand(0f, 6.28f), SpinV = Rand(-4.5f, 4.5f), Gravity = 380f,
                    C = Palette[_rng.Next(Palette.Length)], Confetti = true,
                });
            }
            _canvasDirty = true;
        }

        // ── reward flights ─────────────────────────────────────────────────

        /// <summary>
        /// reward_flug.gd: <paramref name="n"/> small icons (coin, leaf …) leave <paramref name="fromPanel"/> in a little
        /// fan, swoop along a curve to the centre of <paramref name="target"/> (the HUD pill) one after another, and pop it
        /// as each lands; <paramref name="onLand"/> runs once per landing with that icon's 1-based flight number (icons
        /// land in any order). Reduced motion or no target: no flight, all n landings at once.
        /// </summary>
        public void FlyIcons(Vector2 fromPanel, VisualElement target, string icon, int n, Action<int> onLand = null)
        {
            n = Mathf.Clamp(n, 1, 12);
            if (target == null || target.panel == null || Layer.panel == null || ReducedMotion)
            {
                for (var i = 1; i <= n; i++) onLand?.Invoke(i);
                ScalePop(target, 1.12f);
                return;
            }
            var from = Layer.WorldToLocal(fromPanel);
            for (var i = 0; i < n; i++)
            {
                var img = Ux.Icon(icon, "gb-fly-icon");
                img.pickingMode = PickingMode.Ignore;
                img.style.position = Position.Absolute;
                img.style.width = FlyIconSize;
                img.style.height = FlyIconSize;
                img.style.left = from.x - FlyIconSize * 0.5f;
                img.style.top = from.y - FlyIconSize * 0.5f;
                img.style.opacity = 0f;
                Layer.Add(img);
                var a = (i - (n - 1) * 0.5f) * 0.38f + Rand(-0.12f, 0.12f);
                var spread = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * Rand(46f, 82f);
                _flyers.Add(new Flyer { E = img, Target = target, From = from, Spread = spread, Delay = i * 0.06f, Dur = Rand(0.62f, 0.78f), Index = i + 1, OnLand = onLand });
            }
        }

        private const float FlyIconSize = 40f;

        private struct Flyer { public VisualElement E, Target; public Vector2 From, Spread; public float Age, Delay, Dur; public int Index; public Action<int> OnLand; }
        private readonly List<Flyer> _flyers = new List<Flyer>();

        private void TickFlyers(float dt)
        {
            for (var i = _flyers.Count - 1; i >= 0; i--)
            {
                var f = _flyers[i];
                f.Age += dt;
                if (f.Target == null || f.Target.panel == null || f.E.parent == null)
                {
                    f.E.RemoveFromHierarchy();
                    _flyers.RemoveAt(i);
                    continue;
                }
                var t = (f.Age - f.Delay) / f.Dur;
                if (t < 0f) { _flyers[i] = f; continue; }
                var tb = f.Target.worldBound;
                var to = Layer.WorldToLocal(tb.center);
                if (t >= 1f)
                {
                    f.E.RemoveFromHierarchy();
                    _flyers.RemoveAt(i);
                    ScalePop(f.Target, 1.16f, 0.18f);
                    f.OnLand?.Invoke(f.Index);
                    continue;
                }
                // first a puff outwards, then an eased swoop over a raised control point into the pill
                var burst = f.From + f.Spread * EaseOutCubic(Mathf.Clamp01(t / 0.28f));
                var u = Mathf.Clamp01((t - 0.18f) / 0.82f);
                var e = u * u * (3f - 2f * u);
                var ctrl = Vector2.Lerp(burst, to, 0.5f) + new Vector2(0f, -90f);
                var p = (1f - e) * (1f - e) * burst + 2f * (1f - e) * e * ctrl + e * e * to;
                var s = t < 0.15f ? Mathf.Lerp(0.4f, 1.1f, t / 0.15f) : Mathf.Lerp(1.1f, 0.75f, e);
                f.E.style.left = p.x - FlyIconSize * 0.5f;
                f.E.style.top = p.y - FlyIconSize * 0.5f;
                f.E.style.scale = new Scale(new Vector3(s, s, 1f));
                f.E.style.opacity = t < 0.08f ? t / 0.08f : 1f;
                _flyers[i] = f;
            }
        }

        // ── counters ────────────────────────────────────────────────────────

        /// <summary>
        /// Counts a label from → to over <paramref name="seconds"/> in at most 24 audible steps (juice_kit count_to);
        /// <paramref name="onTick"/> gets every shown value, <paramref name="format"/> writes the text (default: the number).
        /// Reduced motion writes the end value at once (one tick).
        /// </summary>
        public void CountTo(Label label, int from, int to, float seconds, Action<int> onTick = null, Func<int, string> format = null)
        {
            if (label == null) return;
            for (var i = _counters.Count - 1; i >= 0; i--) if (_counters[i].L == label) _counters.RemoveAt(i);
            if (ReducedMotion || from == to || seconds <= 0.05f)
            {
                label.text = format != null ? format(to) : to.ToString();
                onTick?.Invoke(to);
                return;
            }
            label.text = format != null ? format(from) : from.ToString();
            _counters.Add(new Counter { L = label, From = from, To = to, Last = from, Steps = Mathf.Min(Mathf.Abs(to - from), 24), Age = 0f, Dur = seconds, OnTick = onTick, Format = format });
        }

        // ── frame ───────────────────────────────────────────────────────────

        /// <summary>Longest frame the kit follows in real time (the minigame frame's cap) — slow players must not see slow motion.</summary>
        public const float MaxTickSec = 0.25f;

        /// <summary>Advances everything by one frame (call with Time.unscaledDeltaTime; clamped to <see cref="MaxTickSec"/>).</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, MaxTickSec);
            for (var i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                f.Age += dt;
                if (f.Age >= f.Life || f.L.parent == null) { RemoveFloat(i); continue; }
                var k = f.Age / f.Life;
                var s = f.Static ? 1f : k < 0.12f ? Mathf.Lerp(0.7f, 1.15f, k / 0.12f) : k < 0.3f ? Mathf.Lerp(1.15f, 1f, (k - 0.12f) / 0.18f) : 1f;
                f.L.style.scale = new Scale(new Vector3(s, s, 1f));
                if (!f.Static) f.L.style.translate = new Translate(0f, -f.Rise * EaseOutCubic(k));
                f.L.style.opacity = 1f - k * k;
                _floats[i] = f;
            }
            for (var i = _pops.Count - 1; i >= 0; i--)
            {
                var p = _pops[i];
                p.Age += dt;
                if (p.Age >= p.Dur || p.E.panel == null) { p.E.style.scale = StyleKeyword.Null; _pops.RemoveAt(i); continue; }
                var k = p.Age / p.Dur;
                var bump = k < 0.3f ? EaseOutQuad(k / 0.3f) : 1f - EaseOutBack((k - 0.3f) / 0.7f);
                var s = Mathf.Max(0.8f, 1f + (p.Amount - 1f) * bump);
                p.E.style.scale = new Scale(new Vector3(s, s, 1f));
                _pops[i] = p;
            }
            for (var i = _fades.Count - 1; i >= 0; i--)
            {
                var f = _fades[i];
                f.Age += dt;
                if (f.Age >= f.Dur) { f.E.style.opacity = 0f; f.E.style.display = DisplayStyle.None; _fades.RemoveAt(i); continue; }
                f.E.style.opacity = 1f - f.Age / f.Dur;
                _fades[i] = f;
            }
            if (_flashOn)
            {
                _flashAge += dt;
                if (_flashAge >= _flashDur) { _flash.style.display = DisplayStyle.None; _flashOn = false; }
                else _flash.style.opacity = 1f - EaseOutQuad(_flashAge / _flashDur);
            }
            TickShake(dt);
            TickCounters(dt);
            TickFlyers(dt);
            if (_particles.Count > 0)
            {
                for (var i = _particles.Count - 1; i >= 0; i--)
                {
                    var p = _particles[i];
                    p.Life -= dt;
                    if (p.Life <= 0f) { _particles.RemoveAt(i); continue; }
                    p.V.y += p.Gravity * dt;
                    if (p.Confetti) { p.V.x += Mathf.Sin(p.Spin * 1.7f) * 30f * dt; p.V *= 1f - 0.6f * dt; }
                    p.P += p.V * dt;
                    p.Spin += p.SpinV * dt;
                    _particles[i] = p;
                }
                _canvasDirty = true;
            }
            if (_canvasDirty)
            {
                _canvas.MarkDirtyRepaint();
                // one more repaint after the last particle died clears the canvas, then it rests
                _canvasDirty = _particles.Count > 0;
            }
        }

        private void TickShake(float dt)
        {
            if (_trauma <= 0f)
            {
                if (_shaken) { Write(Vector2.zero); _shaken = false; }
                return;
            }
            _trauma = Mathf.Max(0f, _trauma - ShakeDecay * dt);
            var amount = _trauma * _trauma * ShakeMaxOffset;
            Write(new Vector2(Rand(-amount, amount), Rand(-amount, amount)));
            _shaken = true;
        }

        private void Write(Vector2 offset)
        {
            if (ApplyShake != null) ApplyShake(offset);
            else if (ShakeTarget != null) ShakeTarget.style.translate = new Translate(offset.x, offset.y);
        }

        private void TickCounters(float dt)
        {
            for (var i = _counters.Count - 1; i >= 0; i--)
            {
                var c = _counters[i];
                c.Age += dt;
                var k = Mathf.Clamp01(c.Age / c.Dur);
                var step = Mathf.FloorToInt(EaseOutCubic(k) * c.Steps + 1e-4f);
                var v = k >= 1f ? c.To : c.From + Mathf.RoundToInt((c.To - c.From) * (float)step / Mathf.Max(1, c.Steps));
                if (v != c.Last)
                {
                    c.Last = v;
                    c.L.text = c.Format != null ? c.Format(v) : v.ToString();
                    c.OnTick?.Invoke(v);
                }
                if (k >= 1f) _counters.RemoveAt(i);
            }
        }

        /// <summary>
        /// Drops every float, particle, pop, fade, count-up (written to its end value) and the flash/shake.
        /// <paramref name="keepParticles"/>: sparks and confetti keep flying (the end moment's confetti over the results card).
        /// </summary>
        public void Clear(bool keepParticles = false)
        {
            for (var i = _floats.Count - 1; i >= 0; i--) RemoveFloat(i);
            foreach (var p in _pops) p.E.style.scale = StyleKeyword.Null;
            _pops.Clear();
            foreach (var f in _fades) { f.E.style.opacity = 0f; f.E.style.display = DisplayStyle.None; }
            _fades.Clear();
            foreach (var c in _counters) c.L.text = c.Format != null ? c.Format(c.To) : c.To.ToString();
            _counters.Clear();
            foreach (var f in _flyers) { f.E.RemoveFromHierarchy(); f.OnLand?.Invoke(f.Index); }
            _flyers.Clear();
            if (!keepParticles)
            {
                if (_particles.Count > 0) { _particles.Clear(); _canvas.MarkDirtyRepaint(); }
                _canvasDirty = false;
            }
            _flash.style.display = DisplayStyle.None;
            _flashOn = false;
            _trauma = 0f;
            if (_shaken) { Write(Vector2.zero); _shaken = false; }
        }

        public bool HasParticles => _particles.Count > 0;

        // ── painting ────────────────────────────────────────────────────────

        private void Paint(Painter2D p)
        {
            for (var i = 0; i < _particles.Count; i++)
            {
                var q = _particles[i];
                var c = q.C;
                c.a *= Mathf.Clamp01(q.Life / q.Max * (q.Confetti ? 3f : 1.4f));
                p.fillColor = c;
                var cs = Mathf.Cos(q.Spin); var sn = Mathf.Sin(q.Spin);
                // confetti flutters: its height follows the spin like a turning paper strip
                var hw = q.Size * 0.5f;
                var hh = q.Confetti ? q.Size * 0.32f * (0.35f + 0.65f * Mathf.Abs(Mathf.Cos(q.Spin * 1.3f))) : q.Size * 0.5f;
                var ax = new Vector2(cs * hw, sn * hw);
                var ay = new Vector2(-sn * hh, cs * hh);
                p.BeginPath();
                p.MoveTo(q.P - ax - ay);
                p.LineTo(q.P + ax - ay);
                p.LineTo(q.P + ax + ay);
                p.LineTo(q.P - ax + ay);
                p.ClosePath();
                p.Fill();
            }
        }

        private sealed class ParticleCanvas : VisualElement
        {
            private readonly Juice _owner;

            public ParticleCanvas(Juice owner)
            {
                _owner = owner;
                name = "juice-particles";
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
                generateVisualContent += ctx => _owner.Paint(ctx.painter2D);
            }
        }

        // ── easing / random ─────────────────────────────────────────────────

        private float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        public static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float EaseOutCubic(float t) { var u = 1f - Mathf.Clamp01(t); return 1f - u * u * u; }
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            var u = Mathf.Clamp01(t) - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
