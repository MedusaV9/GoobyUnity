using System;
using System.Collections.Generic;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI
{
    /// <summary>A full-screen UI Toolkit screen (HUD, sheet, menu). Build once, update per frame.</summary>
    public abstract class UiScreen
    {
        public VisualElement Root { get; private set; }
        public string Id { get; protected set; } = "screen";
        /// <summary>Full-screen screens hide whatever is underneath them (sheets/popups leave it visible).</summary>
        public bool Opaque { get; protected set; }

        public VisualElement Mount()
        {
            Root = new VisualElement { name = Id };
            Root.AddToClassList("gb-root");
            Root.style.position = Position.Absolute;
            Root.style.left = 0; Root.style.right = 0; Root.style.top = 0; Root.style.bottom = 0;
            Build(Root);
            return Root;
        }

        protected abstract void Build(VisualElement root);
        public virtual void OnShown() { }
        public virtual void OnHidden() { }
        public virtual void Update() { }

        /// <summary>
        /// The sound scene this screen stands for (Core <c>SoundScape</c> keys: "home/living", "city", "city/ort/mart",
        /// "arcade", "mg/&lt;game&gt;" …). Null keeps the music and ambience of the screen underneath (sheets, menus).
        /// </summary>
        public virtual string SoundScene => null;
    }

    /// <summary>
    /// Owns the runtime UIDocument + PanelSettings (created from code so no scene GUIDs are needed),
    /// applies the GOOBY theme, safe-area padding and the UI-scale setting, and swaps screens.
    /// </summary>
    public sealed class UiRoot
    {
        public const float ReferenceWidth = 1600f;
        public const float ReferenceHeight = 740f;

        public UIDocument Document { get; private set; }
        public PanelSettings Panel { get; private set; }
        public VisualElement SafeArea { get; private set; }
        public UiScreen Current { get; private set; }
        public int Depth => _stack.Count;

        /// <summary>True while the screen is still on the stack — in OnHidden this tells "covered by a sheet" from "removed".</summary>
        public bool IsMounted(UiScreen screen) => screen != null && _stack.Contains(screen);

        /// <summary>The sound scene of the topmost screen that names one (null while nothing does).</summary>
        public string SoundScene
        {
            get
            {
                foreach (var s in _stack)
                {
                    var key = s.SoundScene;
                    if (!string.IsNullOrEmpty(key)) return key;
                }
                return null;
            }
        }

        private readonly Stack<UiScreen> _stack = new Stack<UiScreen>();
        private readonly AppSettings _settings;
        private Rect _lastSafe;
        private Vector2Int _lastScreen;

        private UiRoot(AppSettings settings)
        {
            _settings = settings;
        }

        public static UiRoot Create(GameObject host, AppSettings settings)
        {
            var root = new UiRoot(settings);
            root.Panel = ScriptableObject.CreateInstance<PanelSettings>();
            root.Panel.name = "GoobyPanelSettings";
            var theme = Resources.Load<ThemeStyleSheet>("gooby-ui/Theme/GoobyTheme");
            if (theme != null) root.Panel.themeStyleSheet = theme;
            else Debug.LogWarning("[ui] GoobyTheme.tss not found in Resources/gooby-ui/Theme");
            root.Panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            root.Panel.referenceResolution = new Vector2Int((int)ReferenceWidth, (int)ReferenceHeight);
            root.Panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            root.Panel.match = 0.5f;
            root.Panel.scale = (float)settings.UiScale;
            root.Panel.sortingOrder = 100;
            InstallFallbackFont(root.Panel);

            root.Document = host.AddComponent<UIDocument>();
            root.Document.panelSettings = root.Panel;

            root.SafeArea = new VisualElement { name = "safe-area" };
            root.SafeArea.style.flexGrow = 1;
            root.SafeArea.style.position = Position.Absolute;
            root.Document.rootVisualElement.Add(root.SafeArea);
            root.ApplySafeArea(force: true);
            // every tapped button answers with a soft tap — unless its own handler just made a sound (it runs first)
            root.Document.rootVisualElement.RegisterCallback<ClickEvent>(e =>
            {
                for (var el = e.target as VisualElement; el != null; el = el.parent)
                {
                    if (!(el is Button) && !el.ClassListContains("gb-action") && !el.ClassListContains("gb-ware--tap")) continue;
                    if (el.enabledInHierarchy && !Sfx.PlayedWithin(0.06f)) Sfx.PlayUi("soft_tap");
                    return;
                }
            });
            settings.Changed += key =>
            {
                if (key == "uiScale") root.Panel.scale = (float)settings.UiScale;
            };
            return root;
        }

        /// <summary>
        /// Nunito has no coin sign ᴳ, no ★ ✈ → ♥ ▶ …: a symbol subset of DejaVu Sans Bold (tools/ui/build_fallback_font.py)
        /// becomes the panel's fallback font, so those glyphs render instead of tofu boxes.
        /// </summary>
        private static void InstallFallbackFont(PanelSettings panel)
        {
            var font = Resources.Load<Font>("gooby-ui/Fonts/GoobyFallback");
            if (font == null) { Debug.LogWarning("[ui] fallback font missing"); return; }
            var asset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font);
            if (asset == null) { Debug.LogWarning("[ui] fallback font asset could not be created"); return; }
            var settings = panel.textSettings != null ? panel.textSettings : ScriptableObject.CreateInstance<PanelTextSettings>();
            if (settings.fallbackFontAssets == null) settings.fallbackFontAssets = new System.Collections.Generic.List<UnityEngine.TextCore.Text.FontAsset>();
            settings.fallbackFontAssets.Add(asset);
            // monochrome emoji (Noto Emoji subset, tools/ui/build_emoji_font.py) after the symbols: renders in the text
            // colour wherever the colour emoji font below is missing or cannot be rasterised (Linux: CBDT bitmaps)
            var emojiFont = Resources.Load<Font>("gooby-ui/Fonts/GoobyEmoji");
            var emojiAsset = emojiFont != null ? UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(emojiFont) : null;
            if (emojiAsset != null) settings.fallbackFontAssets.Add(emojiAsset);
            else Debug.LogWarning("[ui] emoji fallback font missing");
            InstallEmojiFont(settings);
            panel.textSettings = settings;
        }

        /// <summary>
        /// Emoji typed on a phone keyboard (letters, InstantGooby captions, names): where the OS colour emoji font can be
        /// rasterised it becomes the panel's emoji fallback (colour); otherwise GoobyEmoji draws them in the text colour.
        /// </summary>
        private static void InstallEmojiFont(PanelTextSettings settings)
        {
            try
            {
                foreach (var path in Font.GetPathsToOSFonts())
                {
                    var file = System.IO.Path.GetFileName(path).ToLowerInvariant();
                    if (!(file.Contains("coloremoji") || file.Contains("color-emoji") || file == "seguiemj.ttf")) continue;
                    var emoji = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(path, 0, 90, 6,
                        UnityEngine.TextCore.LowLevel.GlyphRenderMode.COLOR, 1024, 1024);
                    if (emoji == null) continue;
                    if (settings.emojiFallbackTextAssets == null) settings.emojiFallbackTextAssets = new System.Collections.Generic.List<UnityEngine.TextCore.Text.TextAsset>();
                    settings.emojiFallbackTextAssets.Add(emoji);
                    Debug.Log("[ui] emoji font " + path);
                    return;
                }
                Debug.Log("[ui] no colour emoji font on this system");
            }
            catch (System.Exception e) { Debug.LogWarning("[ui] emoji font failed: " + e.Message); }
        }

        /// <summary>Replace the current screen (stack cleared).</summary>
        public void Show(UiScreen screen)
        {
            while (_stack.Count > 0) PopInternal();
            Push(screen);
        }

        /// <summary>Overlay a screen on top of the current one (e.g. settings over the HUD).</summary>
        public void Push(UiScreen screen)
        {
            Current?.OnHidden();
            _stack.Push(screen);
            Current = screen;
            SafeArea.Add(screen.Mount());
            RefreshVisibility();
            screen.OnShown();
            if (!screen.Opaque && _stack.Count > 1 && !Sfx.PlayedWithin(0.06f)) Sfx.PlayUi("soft_open");
            Debug.Log($"[ui] push {screen.GetType().Name} depth={_stack.Count}");
        }

        public void Pop()
        {
            if (_stack.Count <= 1) return;
            if (!_stack.Peek().Opaque && !Sfx.PlayedWithin(0.06f)) Sfx.PlayUi("soft_close");
            PopInternal();
            Current = _stack.Peek();
            RefreshVisibility();
            Current.OnShown();
        }

        /// <summary>Still somewhere in the stack (a sheet may cover it) — false once popped.</summary>
        public bool IsOpen(UiScreen screen) => _stack.Contains(screen);

        /// <summary>Back to the bottom screen (the HUD) — used by "Nach Hause".</summary>
        public void PopToRoot()
        {
            if (_stack.Count <= 1) return;
            while (_stack.Count > 1) PopInternal();
            Current = _stack.Peek();
            RefreshVisibility();
            Current.OnShown();
        }

        /// <summary>Everything from the top down to (and including) the first opaque screen is visible; the rest is hidden.</summary>
        private void RefreshVisibility()
        {
            var visible = true;
            foreach (var s in _stack) // Stack enumerates top → bottom
            {
                if (s.Root != null) s.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (s.Opaque) visible = false;
            }
        }

        private void PopInternal()
        {
            var top = _stack.Pop();
            top.OnHidden();
            top.Root?.RemoveFromHierarchy();
            Current = _stack.Count > 0 ? _stack.Peek() : null;
            Debug.Log($"[ui] pop {top.GetType().Name} depth={_stack.Count}");
        }

        public void Update()
        {
            ApplySafeArea(force: false);
            // a screen may push or pop during its Update (the street parks at a place and opens its sheet),
            // so iterate over a snapshot of the stack
            foreach (var s in _stack.ToArray()) s.Update();
            TickFade(Time.unscaledDeltaTime);
            _cheerFx?.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.25f));
        }

        // ── big moments (level up) above every screen ──

        private Juice _cheerFx;
        private VisualElement _cheer;

        /// <summary>True while a celebration card is up (tests, QA).</summary>
        public bool Celebrating => _cheer != null && _cheer.panel != null;

        /// <summary>Confetti over every screen without a card — the small wins (today's parcel, a finished task).</summary>
        public void Confetti(int count = 60)
        {
            if (Document == null) return;
            _cheerFx ??= new Juice();
            var layer = _cheerFx.Layer;
            if (layer.parent == null || (_cheer == null && layer.parent != Document.rootVisualElement))
            {
                layer.RemoveFromHierarchy();
                Document.rootVisualElement.Add(layer);
            }
            layer.BringToFront();
            _cheerFx.Confetti(count);
        }

        /// <summary>Reward icons flying into a HUD pill above every screen (coins after a round or from today's parcel).</summary>
        public void FlyIcons(Vector2 fromPanel, VisualElement target, string icon, int n, Action<int> onLand = null)
        {
            if (Document == null) { for (var i = 1; i <= Mathf.Max(1, n); i++) onLand?.Invoke(i); return; }
            FxLayer();
            _cheerFx.FlyIcons(fromPanel, target, icon, n, onLand);
        }

        /// <summary>A "+1" that pops and drifts up at a panel position above every screen (a coin Gooby found).</summary>
        public void FloatText(Vector2 panelPos, string text, Color color, float size = 30f)
        {
            if (Document == null) return;
            FxLayer();
            _cheerFx.FloatText(panelPos, text, color, size);
        }

        private void FxLayer()
        {
            _cheerFx ??= new Juice();
            var layer = _cheerFx.Layer;
            if (layer.parent == null || (_cheer == null && layer.parent != Document.rootVisualElement))
            {
                layer.RemoveFromHierarchy();
                Document.rootVisualElement.Add(layer);
            }
            layer.BringToFront();
        }

        /// <summary>
        /// A big moment above every screen: a cream card with a sun medal pops in over a soft veil while confetti rains;
        /// a tap or <paramref name="seconds"/> ends it. One at a time (a newer one replaces the older).
        /// </summary>
        public void Celebrate(string title, string line, string icon = "star", float seconds = 3.2f)
        {
            if (Document == null) return;
            _cheer?.RemoveFromHierarchy();
            var layer = new VisualElement { name = "celebration", pickingMode = PickingMode.Position };
            layer.AddToClassList("gb-cheer");
            layer.AddToClassList("gb-cheer--pre");
            var card = Ux.El("gb-cheer-card");
            var medal = Ux.El("gb-cheer-medal");
            medal.Add(Ux.Icon(icon, "gb-cheer-icon"));
            card.Add(medal);
            card.Add(Ux.Text(title, "gb-cheer-title"));
            var text = Ux.Text(line, "gb-cheer-line");
            text.style.whiteSpace = WhiteSpace.Normal;
            card.Add(text);
            layer.Add(card);
            _cheerFx ??= new Juice();
            layer.Add(_cheerFx.Layer);
            Document.rootVisualElement.Add(layer);
            _cheer = layer;
            layer.schedule.Execute(() =>
            {
                layer.RemoveFromClassList("gb-cheer--pre");
                if (_cheer == layer) _cheerFx.Confetti(90);
            }).StartingIn(40);
            void Close()
            {
                if (_cheer != layer || layer.ClassListContains("gb-cheer--out")) return;
                layer.AddToClassList("gb-cheer--out");
                layer.schedule.Execute(() =>
                {
                    layer.RemoveFromHierarchy();
                    if (_cheer == layer) _cheer = null;
                }).StartingIn(280);
            }
            layer.RegisterCallback<ClickEvent>(_ => Close());
            layer.schedule.Execute(Close).StartingIn((long)(seconds * 1000f));
        }

        // ── global notifications (online events reach the player on every screen) ──

        private VisualElement _notices;

        private VisualElement NoticeLane()
        {
            if (_notices != null) return _notices;
            _notices = new VisualElement { name = "notices", pickingMode = PickingMode.Ignore };
            _notices.AddToClassList("gb-online");
            _notices.AddToClassList("gb-notice-lane");
            _notices.style.position = Position.Absolute;
            _notices.style.top = 64;
            _notices.style.left = Length.Percent(50);
            _notices.style.translate = new Translate(Length.Percent(-50), 0);
            _notices.style.alignItems = Align.Center;
            Document.rootVisualElement.Add(_notices);
            return _notices;
        }

        /// <summary>
        /// The toast pill (board 06 look): the icon sits in a tinted disc, the text wraps instead of overflowing the
        /// pill. Shared by <see cref="Toast"/> and the visit HUD's own lane.
        /// </summary>
        public static VisualElement BuildNotice(string text, string icon)
        {
            var toast = Ux.El("gb-toast", "gb-notice");
            toast.pickingMode = PickingMode.Ignore;
            var disc = Ux.El("gb-notice-icon", NoticeTint(icon));
            disc.pickingMode = PickingMode.Ignore;
            disc.Add(Ux.Icon(icon));
            toast.Add(disc);
            var l = Ux.Text(text);
            l.pickingMode = PickingMode.Ignore;
            toast.Add(l);
            toast.style.marginBottom = 6;
            return toast;
        }

        /// <summary>Slide/fade the notice in now and out shortly before it is removed after <paramref name="seconds"/>.</summary>
        public static void AnimateNotice(VisualElement toast, float seconds)
        {
            toast.AddToClassList("gb-notice--pre");
            toast.schedule.Execute(() => toast.RemoveFromClassList("gb-notice--pre")).StartingIn(16);
            var ms = (long)(seconds * 1000);
            toast.schedule.Execute(() => toast.AddToClassList("gb-notice--out")).StartingIn(System.Math.Max(16, ms - 240));
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(ms);
        }

        private static string NoticeTint(string icon)
        {
            switch (icon)
            {
                case "heart": case "gift": case "friends": return "gb-notice-icon--rosa";
                case "mail": case "coin": case "sparkle": case "star": return "gb-notice-icon--sonne";
                case "house": case "home": case "leaf": case "sprout": return "gb-notice-icon--mint";
                case "user": case "moon": case "cloud": case "globe": return "gb-notice-icon--blau";
                default: return "gb-notice-icon--sand";
            }
        }

        private readonly Queue<(string text, string icon, float seconds)> _toastQueue = new Queue<(string, string, float)>();
        private const int MaxVisibleToasts = 2, MaxQueuedToasts = 6;

        /// <summary>
        /// A short toast above every screen. Calm by design: at most two at a time, the rest wait their turn in order
        /// (a burst of rewards reads one by one instead of a stack), and a message already shown or waiting is not repeated.
        /// </summary>
        public void Toast(string text, string icon = "leaf", float seconds = 3.2f)
        {
            if (string.IsNullOrEmpty(text)) return;
            var lane = NoticeLane();
            foreach (var q in _toastQueue) if (q.text == text) return;
            for (var i = 0; i < lane.childCount; i++)
                if (lane[i].ClassListContains("gb-notice") && lane[i].userData as string == text) return;
            if (VisibleToasts(lane) >= MaxVisibleToasts)
            {
                if (_toastQueue.Count < MaxQueuedToasts) _toastQueue.Enqueue((text, icon, seconds));
                Debug.Log("[ui] toast queued (" + _toastQueue.Count + "): " + text);
                return;
            }
            ShowToastNow(lane, text, icon, seconds);
        }

        private void ShowToastNow(VisualElement lane, string text, string icon, float seconds)
        {
            Debug.Log("[ui] toast: " + text);
            seconds *= GameHost.Instance != null && GameHost.Instance.Settings != null ? GameHost.Instance.Settings.HintScale : 1f;
            lane.BringToFront();
            var toast = BuildNotice(text, icon);
            toast.userData = text;
            lane.Add(toast);
            AnimateNotice(toast, seconds);
            // the next waiting toast follows once this one has left (a frame later: the lane has let go of it by then)
            toast.RegisterCallback<DetachFromPanelEvent>(_ => lane.schedule.Execute(() => PumpToasts(lane)));
        }

        private void PumpToasts(VisualElement lane)
        {
            while (_toastQueue.Count > 0 && VisibleToasts(lane) < MaxVisibleToasts)
            {
                var (text, icon, seconds) = _toastQueue.Dequeue();
                ShowToastNow(lane, text, icon, seconds);
            }
        }

        private static int VisibleToasts(VisualElement lane)
        {
            var n = 0;
            for (var i = 0; i < lane.childCount; i++) if (lane[i].ClassListContains("gb-notice")) n++;
            return n;
        }

        /// <summary>
        /// A small question card above every screen (a friend wants to visit): Gooby's face with the topic icon, the
        /// question, a draining timer bar and two pills (Später / Reinlassen); auto-declines after
        /// <paramref name="seconds"/>. Returns a closer so the caller can withdraw it (request expired).
        /// </summary>
        public System.Action Prompt(string icon, string title, string text, string yes, System.Action onYes, string no, System.Action onNo, float seconds = 28f)
        {
            var lane = NoticeLane();
            lane.BringToFront();
            // wrapper: soft board-06 shadow behind the card, pops in
            var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
            wrap.AddToClassList("gb-notice-wrap");
            wrap.AddToClassList("gb-notice-wrap--pre");
            var shadow = Ux.El("gb-shadow");
            shadow.pickingMode = PickingMode.Ignore;
            wrap.Add(shadow);
            var card = Ux.El("gb-notice-card");
            card.pickingMode = PickingMode.Position;
            wrap.Add(card);

            var head = Ux.Row("gb-notice-head");
            var face = Ux.Portrait(60);
            face.AddToClassList("gb-notice-face");
            var badge = Ux.El("gb-notice-face-badge");
            badge.Add(Ux.Icon(icon));
            face.Add(badge);
            head.Add(face);
            var col = Ux.Col("gb-notice-texts");
            col.Add(Ux.Text(title, "gb-notice-title"));
            col.Add(Ux.Text(text, "gb-notice-text"));
            head.Add(col);
            card.Add(head);

            var timer = Ux.El("gb-notice-timer");
            var fill = Ux.El("gb-notice-timer-fill");
            timer.Add(fill);
            card.Add(timer);

            var row = Ux.Row("gb-notice-btns");
            var done = false;
            void Close() { if (done) return; done = true; wrap.RemoveFromHierarchy(); }
            row.Add(Ux.IconTextBtn("close", no, () => { if (done) return; Close(); onNo?.Invoke(); }, "gb-btn-secondary", "gb-btn-sm"));
            row.Add(Ux.IconTextBtn("check", yes, () => { if (done) return; Close(); onYes?.Invoke(); }, "gb-btn-confirm", "gb-btn-sm"));
            card.Add(row);
            lane.Add(wrap);
            wrap.schedule.Execute(() => wrap.RemoveFromClassList("gb-notice-wrap--pre")).StartingIn(16);
            var shownAt = Time.realtimeSinceStartup;
            var total = Mathf.Max(0.1f, seconds);
            wrap.schedule.Execute(() => fill.style.width = Length.Percent(Mathf.Clamp01(1f - (Time.realtimeSinceStartup - shownAt) / total) * 100f))
                .Every(100).Until(() => done);
            wrap.schedule.Execute(() => { if (done) return; Close(); onNo?.Invoke(); }).StartingIn((long)(seconds * 1000));
            return Close;
        }

        // ── scene fade (room changes) ───────────────────────────────────────

        private VisualElement _fade;
        private float _fadeT, _fadeSeconds;
        private int _fadeDir;
        private System.Action _fadePeak;

        public bool Fading => _fadeDir != 0;

        /// <summary>
        /// Cream curtain over everything: fades in, runs <paramref name="atPeak"/> while the screen is covered
        /// (the world swaps rooms there), fades out again. Input is blocked while the curtain is up.
        /// </summary>
        public void FadeThrough(System.Action atPeak, float seconds = 0.32f)
        {
            if (_fade == null)
            {
                _fade = new VisualElement { name = "fade" };
                _fade.style.position = Position.Absolute;
                _fade.style.left = 0; _fade.style.right = 0; _fade.style.top = 0; _fade.style.bottom = 0;
                _fade.style.backgroundColor = new Color(1f, 0.953f, 0.902f);
                Document.rootVisualElement.Add(_fade);
            }
            _fade.BringToFront();
            _fade.style.opacity = 0f;
            _fade.style.display = DisplayStyle.Flex;
            _fade.pickingMode = PickingMode.Position;
            _fadePeak = atPeak;
            _fadeSeconds = Mathf.Max(0.05f, seconds);
            _fadeT = 0f;
            _fadeDir = 1;
            Sfx.Play("travel_whoosh_auf");
        }

        private void TickFade(float dt)
        {
            if (_fadeDir == 0 || _fade == null) return;
            _fadeT += dt / _fadeSeconds;
            if (_fadeDir > 0 && _fadeT >= 1f)
            {
                _fade.style.opacity = 1f;
                var peak = _fadePeak; _fadePeak = null;
                peak?.Invoke();
                _fadeDir = -1; _fadeT = 0f;
                return;
            }
            if (_fadeDir < 0 && _fadeT >= 1f)
            {
                _fadeDir = 0;
                _fade.style.display = DisplayStyle.None;
                _fade.pickingMode = PickingMode.Ignore;
                return;
            }
            _fade.style.opacity = _fadeDir > 0 ? _fadeT : 1f - _fadeT;
        }

        /// <summary>Insets the UI by the device safe area (notch / Dynamic Island / home indicator).</summary>
        private void ApplySafeArea(bool force)
        {
            var safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == _lastSafe && size == _lastScreen) return;
            _lastSafe = safe;
            _lastScreen = size;
            var panelSize = Document.rootVisualElement.layout.size;
            if (float.IsNaN(panelSize.x) || panelSize.x <= 0) panelSize = new Vector2(ReferenceWidth, ReferenceHeight);
            var sx = panelSize.x / Mathf.Max(1, Screen.width);
            var sy = panelSize.y / Mathf.Max(1, Screen.height);
            SafeArea.style.left = safe.xMin * sx;
            SafeArea.style.right = (Screen.width - safe.xMax) * sx;
            SafeArea.style.bottom = safe.yMin * sy;
            SafeArea.style.top = (Screen.height - safe.yMax) * sy;
        }
    }
}
