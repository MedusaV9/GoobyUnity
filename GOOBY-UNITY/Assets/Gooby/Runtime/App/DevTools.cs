using Gooby.Core.State;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.App
{
    /// <summary>
    /// The developer toolbox's live parts (port of dev_zeit.gd + perf_overlay.gd): a clock offset — the game clock is
    /// pinned to "now + offset" every frame so time keeps running, offset 0 hands it back to real time — and a small
    /// frame-rate badge in the corner. Both off by default and never saved.
    /// </summary>
    public static class DevTools
    {
        public const long HourMs = 3_600_000L;

        public static long OffsetMs { get; private set; }
        private static bool _pinned;
        private static Label _fps;
        private static float _fpsClock;
        private static int _fpsFrames;

        public static void AddOffset(long ms) => OffsetMs = System.Math.Max(0L, OffsetMs + ms);

        public static void ResetOffset() => OffsetMs = 0L;

        /// <summary>Every frame from GameHost: pins the clock while an offset is set, unpins once it is back at 0.</summary>
        public static void Tick(GameStateStore store, float dt)
        {
            if (store != null)
            {
                if (OffsetMs > 0)
                {
                    store.Clock.Pin(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + OffsetMs);
                    _pinned = true;
                }
                else if (_pinned)
                {
                    store.Clock.Unpin();
                    _pinned = false;
                }
            }
            if (_fps == null) return;
            _fpsFrames++;
            _fpsClock += dt;
            if (_fpsClock < 0.5f) return;
            _fps.text = Mathf.RoundToInt(_fpsFrames / _fpsClock) + " FPS";
            _fpsClock = 0f;
            _fpsFrames = 0;
        }

        public static bool FpsShown => _fps != null;

        /// <summary>The frame-rate badge in the bottom-left corner, above every screen.</summary>
        public static void ShowFps(UIDocument doc, bool on)
        {
            if (!on || doc == null)
            {
                _fps?.RemoveFromHierarchy();
                _fps = null;
                return;
            }
            if (_fps != null) return;
            _fps = new Label("– FPS") { name = "dev-fps", pickingMode = PickingMode.Ignore };
            _fps.AddToClassList("gb-dev-fps");
            doc.rootVisualElement.Add(_fps);
        }
    }
}
