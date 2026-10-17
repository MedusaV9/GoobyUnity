using System;
using System.IO;
using System.Text;
using Gooby.Core.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Save
{
    /// <summary>I/O facade of the save manager; tests inject fault-injecting fakes (full disk, rename failure, crash windows).</summary>
    public interface ISaveIo
    {
        bool Exists(string path);
        string ReadAllText(string path);
        /// <summary>Write + flush the whole text; returns false on any failure (never leaves a half file as "ok").</summary>
        bool WriteAllTextFlushed(string path, string text, out string error);
        bool Rename(string from, string to, out string error);
        bool Remove(string path, out string error);
    }

    public sealed class FileSaveIo : ISaveIo
    {
        public bool Exists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

        public bool WriteAllTextFlushed(string path, string text, out string error)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(text);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                error = "";
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public bool Rename(string from, string to, out string error)
        {
            try
            {
                File.Copy(from, to, true);
                File.Delete(from);
                error = "";
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public bool Remove(string path, out string error)
        {
            try
            {
                File.Delete(path);
                error = "";
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }

    public struct LoadResult
    {
        public JObject State;
        public bool Fresh;
        public bool Recovered;
        public string Source;
    }

    /// <summary>
    /// Save manager — port of scripts/state/save_manager.gd: atomic tmp+rename, autosave debounce,
    /// 3-generation backup rotation, corruption recovery. Never fails hard on load.
    /// </summary>
    public sealed class SaveManager
    {
        public const int BackupGenerations = 3;
        public const int DefaultDebounceMs = 800;

        public string SavePath { get; set; } = "save_v5.json";
        public int DebounceMs { get; set; } = DefaultDebounceMs;
        public ISaveIo Io { get; set; } = new FileSaveIo();
        public Action<string> Warn { get; set; } = _ => { };
        public event Action<string> SaveFailed;

        private long _dirtyAtTicks = -1;
        private string _lastError = "";

        public LoadResult LoadState(long nowMs)
        {
            if (!Io.Exists(SavePath)) return RecoverMissing(nowMs);
            var raw = Io.ReadAllText(SavePath);
            var parsed = ParseAndNormalize(raw, nowMs);
            if (parsed.Ok) return new LoadResult { State = parsed.State, Fresh = false, Recovered = false, Source = "save" };
            Warn($"[save_manager] corrupt save ({parsed.Error}) — trying backups");
            BackupCorrupt(raw);
            var fromBackup = RecoverFromBackups(nowMs);
            if (fromBackup.HasValue) return fromBackup.Value;
            return new LoadResult { State = SaveSchema.DefaultState(nowMs), Fresh = false, Recovered = true, Source = "fresh" };
        }

        private LoadResult RecoverMissing(long nowMs)
        {
            var tmp = SavePath + ".tmp";
            if (Io.Exists(tmp))
            {
                var tmpParsed = ParseAndNormalize(Io.ReadAllText(tmp), nowMs);
                if (tmpParsed.Ok)
                {
                    Warn("[save_manager] save fehlt — aus .tmp wiederhergestellt");
                    return new LoadResult { State = tmpParsed.State, Fresh = false, Recovered = true, Source = "tmp" };
                }
            }
            var fromBackup = RecoverFromBackups(nowMs);
            if (fromBackup.HasValue)
            {
                Warn($"[save_manager] save fehlt — aus {fromBackup.Value.Source} wiederhergestellt");
                return fromBackup.Value;
            }
            return new LoadResult { State = SaveSchema.DefaultState(nowMs), Fresh = true, Recovered = false, Source = "fresh" };
        }

        private LoadResult? RecoverFromBackups(long nowMs)
        {
            for (var gen = 1; gen <= BackupGenerations; gen++)
            {
                var bak = BackupPath(gen);
                if (!Io.Exists(bak)) continue;
                var parsed = ParseAndNormalize(Io.ReadAllText(bak), nowMs);
                if (parsed.Ok) return new LoadResult { State = parsed.State, Fresh = false, Recovered = true, Source = "bak" + gen };
            }
            return null;
        }

        /// <summary>Persist immediately (atomic tmp+rename with rotation). false = NOT persisted (reason in LastError).</summary>
        public bool SaveNow(JObject state)
        {
            var json = Serialize(state);
            var tmp = SavePath + ".tmp";
            if (!Io.WriteAllTextFlushed(tmp, json, out var writeError))
                return Fail($"Schreiben nach {tmp} fehlgeschlagen ({writeError})");
            RotateBackups();
            if (!Io.Rename(tmp, SavePath, out var renameError))
                return Fail($"rename {tmp} → {SavePath} fehlgeschlagen ({renameError})");
            _dirtyAtTicks = -1;
            _lastError = "";
            return true;
        }

        /// <summary>
        /// Canonical JSON: integral floats are written as integers (JS has no int/float split, so
        /// <c>0.0</c> from a clamp must serialize as <c>0</c> exactly like the web/Godot saves).
        /// </summary>
        public static string Serialize(JObject state)
        {
            var clone = J.CloneObj(state);
            CanonicalizeNumbers(clone);
            return clone.ToString(Formatting.None);
        }

        public static void CanonicalizeNumbers(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Value is JValue v) prop.Value = CanonicalValue(v);
                        else CanonicalizeNumbers(prop.Value);
                    }
                    break;
                case JArray arr:
                    for (var i = 0; i < arr.Count; i++)
                    {
                        if (arr[i] is JValue v) arr[i] = CanonicalValue(v);
                        else CanonicalizeNumbers(arr[i]);
                    }
                    break;
            }
        }

        private static JValue CanonicalValue(JValue v)
        {
            if (v.Type != JTokenType.Float) return v;
            var d = v.Value<double>();
            if (double.IsNaN(d) || double.IsInfinity(d)) return new JValue(0);
            if (Math.Floor(d) == d && Math.Abs(d) < 9.007199254740992e15) return new JValue((long)d);
            return v;
        }

        /// <summary>Forget debounce/error state (fresh manager semantics without losing the injected I/O).</summary>
        public void Reset()
        {
            _dirtyAtTicks = -1;
            _lastError = "";
        }

        public string LastError => _lastError;

        public void MarkDirty(long ticksMs)
        {
            if (_dirtyAtTicks < 0) _dirtyAtTicks = ticksMs;
        }

        public bool IsDirty => _dirtyAtTicks >= 0;

        /// <summary>Returns true when a flush happened; on failure the state stays dirty and retries after another window.</summary>
        public bool AutosaveTick(JObject state, long ticksMs)
        {
            if (_dirtyAtTicks < 0 || ticksMs - _dirtyAtTicks < DebounceMs) return false;
            if (SaveNow(state)) return true;
            _dirtyAtTicks = ticksMs;
            return false;
        }

        public bool FlushIfDirty(JObject state)
        {
            if (_dirtyAtTicks < 0) return false;
            return SaveNow(state);
        }

        public string CorruptPath => SavePath + ".corrupt";

        public string BackupPath(int generation) => $"{SavePath}.bak{generation}";

        private bool Fail(string reason)
        {
            _lastError = reason;
            Warn("[save_manager] Save fehlgeschlagen: " + reason);
            SaveFailed?.Invoke(reason);
            return false;
        }

        private void RotateBackups()
        {
            if (!Io.Exists(SavePath)) return;
            var last = BackupPath(BackupGenerations);
            if (Io.Exists(last) && !Io.Remove(last, out var removeError))
                Warn($"[save_manager] Rotation: remove {last} ({removeError})");
            for (var gen = BackupGenerations - 1; gen >= 1; gen--)
            {
                var from = BackupPath(gen);
                if (Io.Exists(from) && !Io.Rename(from, BackupPath(gen + 1), out var shiftError))
                    Warn($"[save_manager] Rotation: rename {from} → {BackupPath(gen + 1)} ({shiftError})");
            }
            if (!Io.Rename(SavePath, BackupPath(1), out var bak1Error))
                Warn($"[save_manager] Rotation: rename {SavePath} → {BackupPath(1)} ({bak1Error}) — Generation übersprungen");
        }

        private void BackupCorrupt(string raw)
        {
            if (!Io.WriteAllTextFlushed(CorruptPath, raw, out _))
                Warn($"[save_manager] .corrupt nicht schreibbar ({CorruptPath})");
        }

        /// <summary>parse → migrate (v&lt;5) → normalize.</summary>
        public SaveSchema.NormalizeResult ParseAndNormalize(string raw, long nowMs)
        {
            JToken parsed;
            try
            {
                parsed = JToken.Parse(raw);
            }
            catch (Exception e)
            {
                return new SaveSchema.NormalizeResult { Ok = false, State = new JObject(), Error = "invalid JSON: " + e.Message };
            }
            if (!(parsed is JObject obj))
                return new SaveSchema.NormalizeResult { Ok = false, State = new JObject(), Error = "save is not an object" };
            var v = obj["v"];
            var vIsNum = J.IsNum(v);
            if (v == null || v.Type == JTokenType.Null || (vIsNum && (int)v.Value<double>() < SaveSchema.SchemaVersion))
            {
                var migrated = SaveMigration.MigrateAny(obj, nowMs);
                if (!migrated.Ok)
                    return new SaveSchema.NormalizeResult { Ok = false, State = new JObject(), Error = migrated.Error };
                obj = migrated.State;
            }
            return SaveSchema.Normalize(obj, nowMs);
        }
    }
}
