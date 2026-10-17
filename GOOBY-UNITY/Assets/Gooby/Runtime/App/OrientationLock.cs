using System;
using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Device orientation (port of the Godot OrientationService lock): the app is landscape; a portrait minigame turns
    /// the phone to portrait while it is open, so its full-phone view (390×844) fills the screen instead of standing as a
    /// narrow column in the middle. The iOS Info.plist allows portrait for exactly this — outside the games auto-rotation
    /// stays limited to the two landscape sides.
    /// Desktop players do not rotate; with <c>-gooby-rotate</c> they emulate it by resizing the window to a phone held
    /// upright (QA videos).
    /// </summary>
    public static class OrientationLock
    {
        /// <summary>"landscape" or "portrait" — what the last call asked for.</summary>
        public static string Current { get; private set; } = "";

        private static readonly bool EmulateOnDesktop = Array.IndexOf(Environment.GetCommandLineArgs(), "-gooby-rotate") >= 0;
        private static Vector2Int _desktopLandscape;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = "";
            _desktopLandscape = Vector2Int.zero;
        }

        /// <summary>The app's own orientation: landscape, both sides.</summary>
        public static void AppDefault() => Apply("landscape");

        public static void Apply(string orientation)
        {
            orientation = orientation == "portrait" ? "portrait" : "landscape";
            if (orientation == Current) return;
            var first = Current.Length == 0;
            Current = orientation;
            if (!first || orientation != "landscape") Debug.Log("[orientation] " + orientation);
            if (Application.isMobilePlatform)
            {
                if (orientation == "portrait")
                {
                    Screen.orientation = ScreenOrientation.Portrait;
                    return;
                }
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                // leave the locked portrait at once, then let the phone pick its landscape side
                if (!first) Screen.orientation = ScreenOrientation.LandscapeLeft;
                Screen.orientation = ScreenOrientation.AutoRotation;
                return;
            }
            if (!EmulateOnDesktop) return;
            if (orientation == "portrait")
            {
                _desktopLandscape = new Vector2Int(Screen.width, Screen.height);
                var h = Mathf.Clamp(Screen.currentResolution.height - 90, 480, 1560);
                Screen.SetResolution(Mathf.RoundToInt(h * 390f / 844f), h, FullScreenMode.Windowed);
            }
            else if (_desktopLandscape.x > 0)
            {
                Screen.SetResolution(_desktopLandscape.x, _desktopLandscape.y, FullScreenMode.Windowed);
            }
        }
    }
}
