using System;
using System.IO;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Live settings (port target: scripts/core/app_settings.gd). Persisted as settings.json next to the
    /// save. Keys mirror the concept's settings screen (board 07): language, minigame orientation,
    /// handedness, show controls, UI scale, colour-blind palette, audio volumes, graphics preset.
    /// </summary>
    public sealed class AppSettings
    {
        public const string FileName = "settings.json";

        public event Action<string> Changed;

        public string Locale { get; private set; } = "de";
        /// <summary>"auto" | "landscape" | "portrait"</summary>
        public string MinigameOrientation { get; private set; } = "auto";
        /// <summary>"right" | "left"</summary>
        public string Handedness { get; private set; } = "right";
        public bool ShowControls { get; private set; } = true;
        /// <summary>0.8 … 1.3; until the player moves the slider it follows the device (<see cref="DeviceUiScale"/>).</summary>
        public double UiScale { get; private set; } = 1.0;
        /// <summary>The player chose <see cref="UiScale"/> on the slider (otherwise the device default applies).</summary>
        public bool UiScaleChosen { get; private set; }

        /// <summary>
        /// The UI scale a device starts with. The boards are drawn for a roomy canvas: at 1.0 an iPhone in landscape
        /// (a short side under 3.6 in) shows the 14-px body text at about 8 pt and the 44-px pills at 26 pt, below
        /// Apple's 44-pt touch size — phones start at 1.25, small tablets at 1.15, everything else at 1.0.
        /// </summary>
        public static double DeviceUiScale()
        {
            if (!UnityEngine.Application.isMobilePlatform) return 1.0;
            var dpi = UnityEngine.Screen.dpi;
            if (dpi <= 1f) return 1.15;
            var shortInches = Math.Min(UnityEngine.Screen.width, UnityEngine.Screen.height) / dpi;
            return shortInches < 3.6 ? 1.25 : shortInches < 5.2 ? 1.15 : 1.0;
        }
        /// <summary>Colour-vision aid (Core ColorVision): "aus" | "protan" | "deutan" | "tritan".</summary>
        public string ColorVisionMode { get; private set; } = Gooby.Core.Meta.ColorVision.Aus;
        public bool HighContrast { get; private set; } = false;
        /// <summary>"normal" | "lang": toasts and banners stay longer on screen.</summary>
        public string HintDuration { get; private set; } = "normal";
        /// <summary>The version whose "Neu in GOOBY" highlights the player has closed ("" = none yet).</summary>
        public string WhatsNewSeen { get; private set; } = "";
        /// <summary>The one-time "Deine Knöpfe" card on the home HUD was confirmed (app_settings hints.hud_actions_seen).</summary>
        public bool HudButtonsSeen { get; private set; }
        /// <summary>The developer toolbox was unlocked (seven taps on the version).</summary>
        public bool DevUnlocked { get; private set; }
        public bool ReducedMotion { get; private set; } = false;
        public double MusicVolume { get; private set; } = 0.8;
        public double SfxVolume { get; private set; } = 1.0;
        public string HapticStrength { get; private set; } = "normal"; // dezent | normal | stark
        public string GraphicsPreset { get; private set; } = "auto";  // auto | sparsam | schoen
        /// <summary>Multiplayer server the player typed in: "192.168.0.10", "host:8080", "wss://gooby.example" ("" = offline).</summary>
        public string ServerAddress { get; private set; } = "";
        /// <summary>Optional join secret of the server (GOOBY_JOIN_SECRET).</summary>
        public string ServerSecret { get; private set; } = "";
        /// <summary>Connect to <see cref="ServerAddress"/> at boot and after network drops.</summary>
        public bool OnlineAuto { get; private set; } = true;

        private string _path;

        public void Load(string directory)
        {
            _path = Path.Combine(directory, FileName);
            UiScale = DeviceUiScale();
            if (!File.Exists(_path)) return;
            try
            {
                var o = JObject.Parse(File.ReadAllText(_path));
                Locale = J.Str(o["locale"], Locale) == "en" ? "en" : "de";
                MinigameOrientation = Pick(J.Str(o["minigameOrientation"], MinigameOrientation), "auto", "landscape", "portrait");
                Handedness = Pick(J.Str(o["handedness"], Handedness), "right", "left");
                ShowControls = o["showControls"] == null ? ShowControls : J.IsTrue(o["showControls"]);
                // a scale saved before the device default existed (always 1.0) is no choice of the player's
                UiScaleChosen = J.IsTrue(o["uiScaleChosen"]);
                if (UiScaleChosen) UiScale = J.Clamp(J.NumOr(o["uiScale"], UiScale), 0.8, 1.3);
                // the old on/off switch of the colour-blind palette (it had no effect) becomes the common deutan aid
                ColorVisionMode = Gooby.Core.Meta.ColorVision.Normalize(J.Str(o["colorVision"], J.IsTrue(o["colorBlind"]) ? Gooby.Core.Meta.ColorVision.Deutan : ColorVisionMode));
                HighContrast = J.IsTrue(o["highContrast"]);
                HintDuration = Pick(J.Str(o["hintDuration"], HintDuration), "normal", "lang");
                WhatsNewSeen = Clip(J.Str(o["whatsNewSeen"]), 32);
                HudButtonsSeen = J.IsTrue(o["hudButtonsSeen"]);
                DevUnlocked = J.IsTrue(o["devUnlocked"]);
                ReducedMotion = J.IsTrue(o["reducedMotion"]);
                MusicVolume = J.Clamp(J.NumOr(o["musicVolume"], MusicVolume), 0.0, 1.0);
                SfxVolume = J.Clamp(J.NumOr(o["sfxVolume"], SfxVolume), 0.0, 1.0);
                HapticStrength = Pick(J.Str(o["hapticStrength"], HapticStrength), "normal", "dezent", "stark");
                GraphicsPreset = Pick(J.Str(o["graphicsPreset"], GraphicsPreset), "auto", "sparsam", "schoen");
                ServerAddress = Clip(J.Str(o["serverAddress"], ServerAddress), 200);
                ServerSecret = Clip(J.Str(o["serverSecret"], ServerSecret), 128);
                OnlineAuto = o["onlineAuto"] == null ? OnlineAuto : J.IsTrue(o["onlineAuto"]);
            }
            catch (Exception)
            {
                // corrupt settings never block the boot — defaults win
            }
        }

        public JObject ToJson() => new JObject
        {
            ["locale"] = Locale,
            ["minigameOrientation"] = MinigameOrientation,
            ["handedness"] = Handedness,
            ["showControls"] = ShowControls,
            ["uiScale"] = UiScale,
            ["uiScaleChosen"] = UiScaleChosen,
            ["colorVision"] = ColorVisionMode,
            ["highContrast"] = HighContrast,
            ["hintDuration"] = HintDuration,
            ["whatsNewSeen"] = WhatsNewSeen,
            ["hudButtonsSeen"] = HudButtonsSeen,
            ["devUnlocked"] = DevUnlocked,
            ["reducedMotion"] = ReducedMotion,
            ["musicVolume"] = MusicVolume,
            ["sfxVolume"] = SfxVolume,
            ["hapticStrength"] = HapticStrength,
            ["graphicsPreset"] = GraphicsPreset,
            ["serverAddress"] = ServerAddress,
            ["serverSecret"] = ServerSecret,
            ["onlineAuto"] = OnlineAuto,
        };

        public void Save()
        {
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, ToJson().ToString());
            }
            catch (Exception)
            {
                // settings are best-effort; the save itself has the robust path
            }
        }

        public void SetLocale(string v) => Set(ref v, v == "en" ? "en" : "de", "locale", x => Locale = x);
        public void SetMinigameOrientation(string v) => Set(ref v, Pick(v, "auto", "landscape", "portrait"), "minigameOrientation", x => MinigameOrientation = x);
        public void SetHandedness(string v) => Set(ref v, Pick(v, "right", "left"), "handedness", x => Handedness = x);
        public void SetShowControls(bool v) { ShowControls = v; Persist("showControls"); }
        public void SetUiScale(double v) { UiScale = Math.Round(J.Clamp(v, 0.8, 1.3), 2); UiScaleChosen = true; Persist("uiScale"); }
        public void SetColorVision(string v) => Set(ref v, Gooby.Core.Meta.ColorVision.Normalize(v), "colorVision", x => ColorVisionMode = x);
        public void SetHighContrast(bool v) { HighContrast = v; Persist("highContrast"); }
        public void SetHintDuration(string v) => Set(ref v, Pick(v, "normal", "lang"), "hintDuration", x => HintDuration = x);
        public void SetWhatsNewSeen(string version) { WhatsNewSeen = Clip(version, 32); Persist("whatsNewSeen"); }
        public void SetHudButtonsSeen(bool v) { HudButtonsSeen = v; Persist("hudButtonsSeen"); }
        public void SetDevUnlocked(bool v) { DevUnlocked = v; Persist("devUnlocked"); }

        /// <summary>Factor on how long toasts and banners stay ("lang": notification_service.gd's 8 s instead of 4 s).</summary>
        public float HintScale => HintDuration == "lang" ? 2f : 1f;
        public void SetReducedMotion(bool v) { ReducedMotion = v; Persist("reducedMotion"); }
        public void SetMusicVolume(double v) { MusicVolume = J.Clamp(v, 0, 1); Persist("musicVolume"); }
        public void SetSfxVolume(double v) { SfxVolume = J.Clamp(v, 0, 1); Persist("sfxVolume"); }
        public void SetHapticStrength(string v) => Set(ref v, Pick(v, "normal", "dezent", "stark"), "hapticStrength", x => HapticStrength = x);
        public void SetGraphicsPreset(string v) => Set(ref v, Pick(v, "auto", "sparsam", "schoen"), "graphicsPreset", x => GraphicsPreset = x);

        public void SetServer(string address, string secret)
        {
            ServerAddress = Clip(address, 200);
            ServerSecret = Clip(secret, 128);
            Persist("server");
        }

        public void SetOnlineAuto(bool v) { OnlineAuto = v; Persist("onlineAuto"); }

        private static string Clip(string v, int max)
        {
            v = (v ?? "").Trim();
            return v.Length > max ? v.Substring(0, max) : v;
        }

        private void Set(ref string raw, string normalized, string key, Action<string> assign)
        {
            assign(normalized);
            Persist(key);
        }

        private void Persist(string key)
        {
            Save();
            Changed?.Invoke(key);
        }

        private static string Pick(string value, params string[] allowed)
        {
            foreach (var a in allowed) if (a == value) return value;
            return allowed[0];
        }
    }
}
