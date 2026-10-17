using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI
{
    /// <summary>Tiny builder helpers so screens read like markup while staying compile-checked C#.</summary>
    public static class Ux
    {
        private static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>();

        public static Texture2D IconTexture(string name)
        {
            if (IconCache.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("gooby-ui/Icons/icon_" + name);
            if (t == null) Debug.LogWarning("[ui] icon missing: " + name);
            IconCache[name] = t;
            return t;
        }

        private static readonly Dictionary<string, Texture2D> ImageCache = new Dictionary<string, Texture2D>();

        /// <summary>Painted UI images (backdrops, logo, Gooby portrait/sprite) from Resources/gooby-ui/Images.</summary>
        public static Texture2D ImageTexture(string name)
        {
            if (ImageCache.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("gooby-ui/Images/" + name);
            if (t == null) Debug.LogWarning("[ui] image missing: " + name);
            ImageCache[name] = t;
            return t;
        }

        /// <summary>Element showing an image scaled to fit (aspect kept).</summary>
        public static VisualElement Image(string name, float width, float height)
        {
            var e = new VisualElement { name = "img-" + name };
            e.style.width = width;
            e.style.height = height;
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            var tex = ImageTexture(name);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>Full-bleed painted backdrop behind a screen (cover-scaled, never blocks input).</summary>
        public static VisualElement Backdrop(string name)
        {
            var e = new VisualElement { name = "backdrop-" + name };
            Abs(e, 0, 0, 0, 0);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            e.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            e.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            var tex = ImageTexture(name);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>Gooby's face in a round white-rimmed badge (profile capsule, speech bubble, talk button).</summary>
        /// <summary>Rendered prop thumbnail (tools/blender/build_props.py --thumbs) or a fallback icon.</summary>
        public static VisualElement ItemThumb(string itemId, string fallbackIcon)
        {
            var archetype = Gooby.Unity.World.AssetLoader.ArchetypeFor(itemId);
            var tex = string.IsNullOrEmpty(archetype) ? null : Resources.Load<Texture2D>("gooby-art/thumbs/" + archetype);
            if (tex == null) return Icon(fallbackIcon);
            var e = El("gb-icon", "gb-item-thumb");
            e.style.backgroundImage = new StyleBackground(tex);
            return e;
        }

        public static VisualElement Portrait(float size)
        {
            var e = El("gb-avatar");
            e.style.width = size;
            e.style.height = size;
            e.style.backgroundColor = new Color(1f, 0.93f, 0.86f);
            var tex = ImageTexture("gooby_portrait");
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            return e;
        }

        public static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static VisualElement Row(params string[] classes) => El(Prepend("gb-row", classes));
        public static VisualElement Col(params string[] classes) => El(Prepend("gb-col", classes));

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static VisualElement Icon(string name, params string[] classes)
        {
            var e = El(Prepend("gb-icon", classes));
            var tex = IconTexture(name);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            return e;
        }

        public static VisualElement IconTile(string icon, Color? tint = null)
        {
            var tile = El("gb-icon-tile");
            if (tint.HasValue) tile.style.backgroundColor = tint.Value;
            tile.Add(Icon(icon));
            return tile;
        }

        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.ClearClassList();
            b.AddToClassList("gb-btn");
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        /// <summary>Pill button with a leading icon and a label laid out side by side (Button.text stays empty).</summary>
        public static Button IconTextBtn(string icon, string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = "" };
            b.ClearClassList();
            b.AddToClassList("gb-btn");
            foreach (var c in classes) b.AddToClassList(c);
            b.Add(Icon(icon));
            var label = Text(text, "gb-btn-label");
            label.pickingMode = PickingMode.Ignore;
            b.Add(label);
            return b;
        }

        public static Button IconBtn(string icon, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = "" };
            b.ClearClassList();
            b.AddToClassList("gb-btn-icon");
            foreach (var c in classes) b.AddToClassList(c);
            b.Add(Icon(icon));
            return b;
        }

        public static Label Badge(string text)
        {
            var l = Text(text, "gb-badge");
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static VisualElement Pill(string icon, string value, out Label valueLabel, Action onPlus = null)
        {
            var pill = El("gb-pill");
            pill.Add(Icon(icon));
            valueLabel = Text(value, "gb-pill-wert");
            pill.Add(valueLabel);
            if (onPlus != null)
            {
                var plus = IconBtn("plus", onPlus, "gb-btn-icon-sm");
                plus.style.width = 30; plus.style.height = 30; plus.style.marginLeft = 8;
                plus.style.borderTopWidth = 0; plus.style.borderBottomWidth = 0;
                plus.style.borderLeftWidth = 0; plus.style.borderRightWidth = 0;
                plus.style.backgroundColor = Color.clear;
                pill.Add(plus);
            }
            return pill;
        }

        public static VisualElement Bar(float ratio, out VisualElement fill, params string[] fillClasses)
        {
            var bar = El("gb-bar");
            fill = El(Prepend("gb-bar-fill", fillClasses));
            fill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);
            bar.Add(fill);
            return bar;
        }

        public static void SetBar(VisualElement fill, double ratio) =>
            fill.style.width = Length.Percent(Mathf.Clamp01((float)ratio) * 100f);

        /// <summary>Concept toggle (board 06): green track when on, sliding white knob.</summary>
        public static VisualElement Toggle(bool on, Action<bool> onChange)
        {
            var track = El("gb-toggle-track");
            if (on) track.AddToClassList("gb-toggle-track--on");
            track.Add(El("gb-toggle-knob"));
            var state = on;
            track.RegisterCallback<ClickEvent>(_ =>
            {
                state = !state;
                track.EnableInClassList("gb-toggle-track--on", state);
                onChange(state);
            });
            return track;
        }

        /// <summary>Concept slider (board 06): green fill, white knob; drag/click anywhere on the track.</summary>
        public static VisualElement Slider(double value01, Action<double> onChange, Label valueLabel = null, Func<double, string> format = null)
        {
            var wrap = El("gb-slider");
            var track = El("gb-slider-track");
            var fill = El("gb-slider-fill");
            var knob = El("gb-slider-knob");
            track.Add(fill);
            track.Add(knob);
            wrap.Add(track);
            var v = Mathf.Clamp01((float)value01);

            void Layout()
            {
                fill.style.width = Length.Percent(v * 100f);
                knob.style.left = Length.Percent(v * 100f);
                knob.style.marginLeft = -15;
                if (valueLabel != null) valueLabel.text = format != null ? format(v) : Mathf.RoundToInt(v * 100f) + "%";
            }

            void FromPointer(Vector2 local)
            {
                var w = Mathf.Max(1f, track.resolvedStyle.width);
                v = Mathf.Clamp01(local.x / w);
                Layout();
                onChange(v);
            }

            var dragging = false;
            track.RegisterCallback<PointerDownEvent>(e => { dragging = true; track.CapturePointer(e.pointerId); FromPointer(e.localPosition); });
            track.RegisterCallback<PointerMoveEvent>(e => { if (dragging) FromPointer(e.localPosition); });
            track.RegisterCallback<PointerUpEvent>(e => { dragging = false; track.ReleasePointer(e.pointerId); });
            wrap.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            Layout();
            return wrap;
        }

        /// <summary>Segmented control: one active chip. Returns a setter to sync the active id.</summary>
        public static VisualElement Segment(IList<(string id, string label)> options, string activeId, Action<string> onPick, out Action<string> setActive)
        {
            var seg = El("gb-segment");
            var chips = new Dictionary<string, Button>();
            foreach (var (id, label) in options)
            {
                var chip = new Button(() => onPick(id)) { text = label };
                chip.ClearClassList();
                chip.AddToClassList("gb-chip");
                if (id == activeId) chip.AddToClassList("gb-chip--active");
                chips[id] = chip;
                seg.Add(chip);
            }
            setActive = id =>
            {
                foreach (var kv in chips) kv.Value.EnableInClassList("gb-chip--active", kv.Key == id);
            };
            return seg;
        }

        /// <summary>Dropdown look (board 07): value + chevron; cycles through the options on click.</summary>
        public static VisualElement Dropdown(IList<(string id, string label)> options, string activeId, Action<string> onPick)
        {
            var dd = El("gb-dropdown");
            var idx = 0;
            for (var i = 0; i < options.Count; i++) if (options[i].id == activeId) idx = i;
            var label = Text(options[idx].label, "gb-nowrap");
            dd.Add(label);
            dd.Add(Icon("chevron_down"));
            dd.RegisterCallback<ClickEvent>(_ =>
            {
                idx = (idx + 1) % options.Count;
                label.text = options[idx].label;
                onPick(options[idx].id);
            });
            return dd;
        }

        public static VisualElement SettingRow(string title, string hint, VisualElement control)
        {
            var row = El("gb-setting-row");
            var texts = Col("gb-grow");
            texts.Add(Text(title, "gb-setting-titel"));
            if (!string.IsNullOrEmpty(hint)) texts.Add(Text(hint, "gb-setting-hint"));
            row.Add(texts);
            control.style.marginLeft = 16;
            row.Add(control);
            return row;
        }

        public static VisualElement Group(string icon, string title)
        {
            var g = El("gb-group");
            var head = El("gb-group-head");
            head.Add(Icon(icon));
            head.Add(Text(title));
            g.Add(head);
            return g;
        }

        public static void Abs(VisualElement e, float? left = null, float? top = null, float? right = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        public static string FormatNumber(long n) => n.ToString("N0", new System.Globalization.CultureInfo("de-DE"));

        private static string[] Prepend(string first, string[] rest)
        {
            var arr = new string[rest.Length + 1];
            arr[0] = first;
            Array.Copy(rest, 0, arr, 1, rest.Length);
            return arr;
        }
    }
}
