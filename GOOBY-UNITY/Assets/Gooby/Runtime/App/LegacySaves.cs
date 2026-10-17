using System;
using System.Collections.Generic;
using System.IO;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.App
{
    /// <summary>
    /// Finds save files of the earlier GOOBY versions on this device and applies a transfer
    /// (docs/godot-rewrite/SAVE-TRANSFER.md, ways A + B):
    ///   * Godot 5.x (`user://save_v5.json`): on iOS Godot's user:// IS the Documents folder, which is exactly
    ///     Unity's Application.persistentDataPath when the app keeps the bundle id com.permissionmaxed.gooby;
    ///     on desktop the Godot data dir (~/.local/share/godot/app_userdata/GOOBY) is checked as well;
    ///   * the web/Capacitor app (v0–v4): it mirrored every save into NSUserDefaults under
    ///     `CapacitorStorage.gooby.save` — Unity's PlayerPrefs on iOS read the very same defaults domain.
    /// Every import first backs the current save up to save_v5.pre_import.json, so nothing is lost.
    /// </summary>
    public sealed class LegacySaves
    {
        public const string CapacitorKey = "CapacitorStorage.gooby.save";
        public const string GodotFile = "save_v5.json";
        public const string PreImportBackup = "save_v5.pre_import.json";
        public const string ImportedMarker = "legacy_import_done.txt";

        public sealed class Candidate
        {
            public string Source;      // "godot" | "web"
            public string Label;       // human readable location
            public string Text;        // raw JSON
            public SaveTransfer.Result Preview;
        }

        private readonly GameHost _host;

        public LegacySaves(GameHost host)
        {
            _host = host;
        }

        private string DataDir => _host.DataDir;

        /// <summary>Godot save locations in probe order (first hit wins).</summary>
        public IEnumerable<string> GodotSavePaths()
        {
            yield return Path.Combine(Application.persistentDataPath, GodotFile);                 // iOS: Godot user:// == Documents
            yield return Path.Combine(Application.persistentDataPath, "..", GodotFile);
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "share", "godot", "app_userdata", "GOOBY", GodotFile);      // Linux
                yield return Path.Combine(home, "Library", "Application Support", "Godot", "app_userdata", "GOOBY", GodotFile); // macOS
            }
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData)) yield return Path.Combine(appData, "Godot", "app_userdata", "GOOBY", GodotFile); // Windows
            var extra = Environment.GetEnvironmentVariable("GOOBY_LEGACY_SAVE");
            if (!string.IsNullOrEmpty(extra)) yield return extra;                                    // QA / tests
        }

        /// <summary>Whether an earlier import already happened (the offer is shown once).</summary>
        public bool AlreadyImported => File.Exists(Path.Combine(DataDir, ImportedMarker));

        /// <summary>Looks for an older save; null when nothing usable is around.</summary>
        public Candidate Find(long nowMs)
        {
            foreach (var path in GodotSavePaths())
            {
                try
                {
                    var full = Path.GetFullPath(path);
                    if (string.Equals(full, Path.GetFullPath(Path.Combine(DataDir, GodotFile)), StringComparison.Ordinal)) continue; // that is our own save
                    if (!File.Exists(full)) continue;
                    var text = File.ReadAllText(full);
                    var preview = SaveTransfer.ImportText(text, nowMs);
                    if (preview.Ok) return new Candidate { Source = "godot", Label = full, Text = text, Preview = preview };
                    Debug.LogWarning("[legacy] unreadable Godot save at " + full + ": " + preview.Error);
                }
                catch (Exception e) { Debug.LogWarning("[legacy] probe failed for " + path + ": " + e.Message); }
            }
            try
            {
                var web = PlayerPrefs.GetString(CapacitorKey, "");
                if (!string.IsNullOrEmpty(web))
                {
                    var preview = SaveTransfer.ImportText(web, nowMs);
                    if (preview.Ok) return new Candidate { Source = "web", Label = "NSUserDefaults " + CapacitorKey, Text = web, Preview = preview };
                    Debug.LogWarning("[legacy] unreadable web save in preferences: " + preview.Error);
                }
            }
            catch (Exception e) { Debug.LogWarning("[legacy] PlayerPrefs probe failed: " + e.Message); }
            return null;
        }

        /// <summary>Validates pasted text (raw JSON or GOOBY5 code) without changing anything.</summary>
        public SaveTransfer.Result Preview(string text, long nowMs) => SaveTransfer.ImportText(text, nowMs);

        /// <summary>Backs up the current save, replaces the state and persists immediately. Returns the report.</summary>
        public bool Apply(SaveTransfer.Result result, out string error)
        {
            error = "";
            if (!result.Ok || result.State == null) { error = result.Error; return false; }
            try
            {
                Directory.CreateDirectory(DataDir);
                var current = Path.Combine(DataDir, GodotFile);
                if (File.Exists(current)) File.Copy(current, Path.Combine(DataDir, PreImportBackup), overwrite: true);
                _host.Store.ImportState((JObject)result.State.DeepClone());
                Gooby.Core.Home.HomeState.EnsureInitialized(_host.Store);
                _host.Quests?.EnsureRoll(); // the imported board may be empty or from another day
                File.WriteAllText(Path.Combine(DataDir, ImportedMarker), DateTimeOffset.UtcNow.ToString("o"));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError("[legacy] import failed: " + e);
                return false;
            }
        }

        /// <summary>The current state as a GOOBY5 code (device change new app → new app, or back to Godot).</summary>
        public string ExportCode() => SaveTransfer.ExportCode(_host.Store.State);

        /// <summary>Short summary line for the preview card: level · coins · stickers · outfits.</summary>
        public static string Summary(JObject report, Func<string, (string, object)[], string> t)
        {
            var level = J.Int(report?["level"], 1);
            var coins = J.Long(report?["coins"]);
            var stickers = J.Int(report?["stickers"]);
            var outfits = J.Int(report?["outfits"]);
            return t("nl.transfer.zusammenfassung", new (string, object)[] { ("level", level), ("coins", coins), ("stickers", stickers), ("outfits", outfits) });
        }
    }
}
