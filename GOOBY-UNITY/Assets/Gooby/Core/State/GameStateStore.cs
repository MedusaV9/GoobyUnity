using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.Time;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.State
{
    /// <summary>
    /// Central store — port of scripts/state/game_state.gd (frozen slice API, watched-value diffing,
    /// debounced autosave, live tick + offline catch-up). Engine-free: the Unity host pumps
    /// <see cref="Process"/> with frame deltas and monotonic ticks and forwards pause/resume.
    /// </summary>
    public sealed class GameStateStore
    {
        public const double LiveTickIntervalSec = 5.0;

        public event Action<bool, bool> StateLoaded;            // fresh, recovered
        public event Action<int> CoinsChanged;
        public event Action<JObject> StatsChanged;
        public event Action<int, double> LevelChanged;          // level, xpRatio
        public event Action<double> XpChanged;
        public event Action<string, string> VacationChanged;    // phase, destId
        public event Action<string, JToken> SliceChanged;
        public event Action<List<string>> GoobyEvents;

        public GameClock Clock { get; } = new GameClock();
        public SaveManager Manager { get; } = new SaveManager();

        private JObject _state = new JObject();
        private bool _loaded;
        private double _tickAccum;

        public bool IsLoaded => _loaded;

        /// <summary>Direct state reference — READ ONLY. Writes go through SetValue/Update.</summary>
        public JObject State => _state;

        public void RegisterSlice(string id, Func<JObject> defaultFactory, Func<JToken, JObject> normalize = null) =>
            SaveSchema.RegisterSlice(id, defaultFactory, normalize);

        /// <summary>Load (or reload) the save; savePath override = headless tests.</summary>
        public void Initialize(string savePath, long ticksMs)
        {
            Manager.SavePath = savePath;
            Manager.Reset();
            var res = Manager.LoadState(Clock.NowMs());
            _state = res.State;
            var events = GoobyTicker.CatchUp(_state, Clock.NowMs());
            _loaded = true;
            _tickAccum = 0.0;
            Manager.MarkDirty(ticksMs);
            StateLoaded?.Invoke(res.Fresh, res.Recovered);
            EmitWatched(SnapshotWatchedEmpty());
            if (events.Count > 0) GoobyEvents?.Invoke(events);
        }

        /// <summary>Frame pump: live tick every LiveTickIntervalSec + debounced autosave.</summary>
        public void Process(double deltaSec, long ticksMs)
        {
            if (!_loaded) return;
            _tickAccum += deltaSec;
            if (_tickAccum >= LiveTickIntervalSec)
            {
                _tickAccum = 0.0;
                RunLiveTick(ticksMs);
            }
            Manager.AutosaveTick(_state, ticksMs);
        }

        public void OnPauseOrQuit()
        {
            if (_loaded) Manager.FlushIfDirty(_state);
        }

        public void OnResume(long ticksMs)
        {
            if (_loaded) RunCatchUp(ticksMs);
        }

        public void RunCatchUp(long ticksMs)
        {
            var before = SnapshotWatched();
            var events = GoobyTicker.CatchUp(_state, Clock.NowMs());
            EmitWatched(before);
            Manager.MarkDirty(ticksMs);
            if (events.Count > 0) GoobyEvents?.Invoke(events);
        }

        public void RunLiveTick(long ticksMs)
        {
            var before = SnapshotWatched();
            var events = GoobyTicker.LiveTick(_state, Clock.NowMs());
            EmitWatched(before);
            Manager.MarkDirty(ticksMs);
            if (events.Count > 0) GoobyEvents?.Invoke(events);
        }

        public JToken GetValue(string path) => J.GetPath(_state, path);

        public T GetValue<T>(string path, T fallback)
        {
            var tok = J.GetPath(_state, path);
            if (tok == null || tok.Type == JTokenType.Null) return fallback;
            try { return tok.Value<T>(); }
            catch { return fallback; }
        }

        public void SetValue(string path, JToken value, long ticksMs) =>
            Update(s => J.SetPath(s, path, value), ticksMs);

        /// <summary>Monotonic milliseconds used for autosave debounce; the host installs its own clock.</summary>
        public Func<long> Ticks { get; set; } = () => System.Diagnostics.Stopwatch.GetTimestamp() / (System.Diagnostics.Stopwatch.Frequency / 1000);

        public void Update(Action<JObject> mutator) => Update(mutator, Ticks());

        /// <summary>Atomic-style mutation: diff watched values, emit, schedule autosave.</summary>
        public void Update(Action<JObject> mutator, long ticksMs)
        {
            var before = SnapshotWatched();
            mutator(_state);
            EmitWatched(before);
            Manager.MarkDirty(ticksMs);
        }

        public void NotifySliceChanged(string sliceId) => SliceChanged?.Invoke(sliceId, _state[sliceId]);

        public void ImportState(JObject newState)
        {
            var before = SnapshotWatched();
            _state = newState;
            EmitWatched(before);
            SaveNow();
        }

        public bool SaveNow() => Manager.SaveNow(_state);

        /// <summary>Onboarding `completed(profile)` contract (frozen).</summary>
        public void ApplyOnboardingProfile(JObject profile, long ticksMs)
        {
            Update(s =>
            {
                var meta = J.EnsureDict(s, "meta");
                var playerName = J.Str(profile["player_name"]).Trim();
                if (playerName.Length > 0) meta["playerName"] = playerName.Length > 24 ? playerName.Substring(0, 24) : playerName;
                var nickname = J.Str(profile["gooby_nickname"]).Trim();
                meta["goobyNickname"] = nickname.Length > 0 ? nickname : "Gooby";
                if (profile["editor"] is JObject editor)
                {
                    var morphs = J.EnsureDict(meta, "charMorphs");
                    foreach (var k in new[] { "eyes_apart", "eye_scale", "ear_len", "chubby" })
                        if (editor[k] != null) morphs[k] = editor[k].DeepClone();
                }
                J.EnsureDict(s, "onboarding")["done"] = true;
            }, ticksMs);
            NotifySliceChanged("meta");
            NotifySliceChanged("onboarding");
        }

        public void MarkNews50Seen(long ticksMs)
        {
            Update(s => J.EnsureDict(s, "onboarding")["whatsNew5Seen"] = true, ticksMs);
            NotifySliceChanged("onboarding");
        }

        /// <summary>XP ring ratio for the HUD (full at MAX_LEVEL).</summary>
        public double XpRatio()
        {
            var level = GetValue("progression.level", 1);
            if (level >= Leveling.MaxLevel) return 1.0;
            var xp = GetValue("progression.xp", 0.0);
            return J.Clamp(xp / Leveling.XpToNext(level), 0.0, 1.0);
        }

        // ── watched-value diffing (web store event pattern) ─────────────────

        private struct Watched
        {
            public JToken Coins;
            public JToken Stats;
            public JToken Level;
            public JToken Xp;
            public JToken VacationPhase;
            public string VacationDest;
        }

        private Watched SnapshotWatched()
        {
            var gooby = J.Dict(_state["gooby"]);
            var vac = J.Dict(_state["vacation"]);
            return new Watched
            {
                Coins = J.Dict(_state["economy"])["coins"] ?? 0,
                Stats = J.Dict(gooby["stats"]).DeepClone(),
                Level = J.Dict(_state["progression"])["level"] ?? 1,
                Xp = J.Dict(_state["progression"])["xp"] ?? 0,
                VacationPhase = vac["phase"] ?? "none",
                VacationDest = J.Str(vac["destId"]),
            };
        }

        private static Watched SnapshotWatchedEmpty() => new Watched
        {
            Coins = null, Stats = null, Level = null, Xp = null, VacationPhase = null, VacationDest = "",
        };

        private void EmitWatched(Watched before)
        {
            var now = SnapshotWatched();
            if (!J.Same(now.Coins, before.Coins)) CoinsChanged?.Invoke(J.Int(now.Coins));
            if (!J.Same(now.Stats, before.Stats)) StatsChanged?.Invoke((JObject)now.Stats);
            if (!J.Same(now.Level, before.Level) || !J.Same(now.Xp, before.Xp))
            {
                if (!J.Same(now.Xp, before.Xp)) XpChanged?.Invoke(J.Num(now.Xp));
                LevelChanged?.Invoke(J.Int(now.Level, 1), XpRatio());
            }
            if (!J.Same(now.VacationPhase, before.VacationPhase))
                VacationChanged?.Invoke(J.Str(now.VacationPhase, "none"), now.VacationDest);
        }
    }
}
