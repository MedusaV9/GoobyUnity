using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>In-memory fault-injecting save I/O (mirror of tests/tools/fake_save_io.gd).</summary>
    public sealed class MemorySaveIo : ISaveIo
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public bool FailWrite;
        public bool FailRename;
        public string CrashAfterRenameOf; // simulate a crash right after renaming this path (no further ops)
        public bool Crashed;

        public bool Exists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) => Files[path];

        public bool WriteAllTextFlushed(string path, string text, out string error)
        {
            if (Crashed) { error = "crashed"; return false; }
            if (FailWrite) { error = "disk full"; return false; }
            Files[path] = text;
            error = "";
            return true;
        }

        public bool Rename(string from, string to, out string error)
        {
            if (Crashed) { error = "crashed"; return false; }
            if (FailRename) { error = "EACCES"; return false; }
            Files[to] = Files[from];
            Files.Remove(from);
            if (from == CrashAfterRenameOf) Crashed = true;
            error = "";
            return true;
        }

        public bool Remove(string path, out string error)
        {
            if (Crashed) { error = "crashed"; return false; }
            Files.Remove(path);
            error = "";
            return true;
        }
    }

    public class SaveTests
    {
        private const long Now = 1_768_478_400_000L;

        private static SaveManager NewManager(MemorySaveIo io) => new SaveManager { SavePath = "user/save_v5.json", Io = io };

        [Test]
        public void DefaultStateHasSchemaShape()
        {
            var s = SaveSchema.DefaultState(Now);
            Assert.That(J.Int(s["v"]), Is.EqualTo(5));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(100));
            Assert.That(J.Num(s["gooby"]["stats"]["hunger"]), Is.EqualTo(80.0));
            Assert.That(((JArray)s["garden"]["grid"]).Count, Is.EqualTo(6));
            Assert.That(J.Int(s["inventory"]["food"]["carrot"]), Is.EqualTo(3));
            Assert.That(J.Long(s["gooby"]["lastTickAt"]), Is.EqualTo(Now));
            Assert.That(J.Str(s["vacation"]["phase"]), Is.EqualTo("none"));
            Assert.That(((JObject)s["achievements"]["counters"]).Count, Is.EqualTo(33));
        }

        [Test]
        public void RoundTripIsByteStableAndNormalizeIsIdempotent()
        {
            var s = SaveSchema.DefaultState(Now);
            var json = SaveManager.Serialize(s);
            var back = SaveSchema.Normalize(JObject.Parse(json), Now);
            Assert.That(back.Ok, Is.True, back.Error);
            Assert.That(SaveManager.Serialize(back.State), Is.EqualTo(json));
        }

        [Test]
        public void NormalizeClampsLeavesAndKeepsInventoryVerbatim()
        {
            var raw = SaveSchema.DefaultState(Now);
            raw["gooby"]["stats"]["hunger"] = 250;
            raw["gooby"]["stats"]["fun"] = "62";      // JS Number("62") → 62
            raw["gooby"]["weight"] = 1000;
            raw["economy"]["coins"] = -5;
            raw["progression"]["level"] = 99;
            raw["inventory"]["food"] = new JObject { ["apple"] = 2 }; // consumed carrots must stay consumed
            raw["radio"]["station"] = "pirate";
            raw["codes"]["lockUntil"] = Now + 10L * SaveSchema.FutureStampSlackMs;
            raw["park"]["rides"] = new JObject { ["coaster"] = 3, ["ufo"] = 9 };
            var res = SaveSchema.Normalize(raw, Now);
            Assert.That(res.Ok, Is.True, res.Error);
            var s = res.State;
            Assert.That(J.Num(s["gooby"]["stats"]["hunger"]), Is.EqualTo(100.0));
            Assert.That(J.Num(s["gooby"]["stats"]["fun"]), Is.EqualTo(62.0));
            Assert.That(J.Num(s["gooby"]["weight"]), Is.EqualTo(95.0));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(0));
            Assert.That(J.Int(s["progression"]["level"]), Is.EqualTo(Leveling.MaxLevel));
            Assert.That(s["inventory"]["food"]["carrot"], Is.Null);
            Assert.That(J.Int(s["inventory"]["food"]["apple"]), Is.EqualTo(2));
            Assert.That(J.Str(s["radio"]["station"]), Is.EqualTo("bordmusik"));
            Assert.That(J.Num(s["codes"]["lockUntil"]), Is.EqualTo(Now + (double)SaveSchema.FutureStampSlackMs));
            Assert.That(J.Int(s["park"]["rides"]["coaster"]), Is.EqualTo(3));
            Assert.That(s["park"]["rides"]["ufo"], Is.Null);
        }

        [Test]
        public void StructuralMismatchIsCorrupt()
        {
            var raw = SaveSchema.DefaultState(Now);
            raw["economy"] = new JArray(1, 2, 3);
            var res = SaveSchema.Normalize(raw, Now);
            Assert.That(res.Ok, Is.False);
            Assert.That(res.Error, Does.Contain("save.economy must be an object"));
            var raw2 = SaveSchema.DefaultState(Now);
            raw2["v"] = 6;
            Assert.That(SaveSchema.Normalize(raw2, Now).Ok, Is.False);
            var raw3 = SaveSchema.DefaultState(Now);
            raw3["gooby"]["grumpyUntil"] = new JObject();
            Assert.That(SaveSchema.Normalize(raw3, Now).Ok, Is.False);
        }

        [Test]
        public void AdditiveSlicesLandInDefaultsAndNormalize()
        {
            SaveSchema.RegisterSlice("testPack", () => new JObject { ["n"] = 1 },
                raw => new JObject { ["n"] = J.Int(J.Dict(raw)["n"], 1) + 100 });
            try
            {
                var s = SaveSchema.DefaultState(Now);
                Assert.That(J.Int(s["testPack"]["n"]), Is.EqualTo(1));
                var res = SaveSchema.Normalize(s, Now);
                Assert.That(J.Int(res.State["testPack"]["n"]), Is.EqualTo(101));
            }
            finally
            {
                SaveSchema.UnregisterSlice("testPack");
            }
        }

        [Test]
        public void FirstBootIsFreshAndSaveRotatesBackups()
        {
            var io = new MemorySaveIo();
            var m = NewManager(io);
            var r = m.LoadState(Now);
            Assert.That(r.Fresh, Is.True);
            Assert.That(r.Recovered, Is.False);
            var state = r.State;
            for (var i = 1; i <= 5; i++)
            {
                state["economy"]["coins"] = 100 + i;
                Assert.That(m.SaveNow(state), Is.True);
            }
            Assert.That(io.Files.ContainsKey("user/save_v5.json.tmp"), Is.False);
            Assert.That(J.Int(JObject.Parse(io.Files["user/save_v5.json"])["economy"]["coins"]), Is.EqualTo(105));
            Assert.That(J.Int(JObject.Parse(io.Files["user/save_v5.json.bak1"])["economy"]["coins"]), Is.EqualTo(104));
            Assert.That(J.Int(JObject.Parse(io.Files["user/save_v5.json.bak2"])["economy"]["coins"]), Is.EqualTo(103));
            Assert.That(J.Int(JObject.Parse(io.Files["user/save_v5.json.bak3"])["economy"]["coins"]), Is.EqualTo(102));
            Assert.That(io.Files.ContainsKey("user/save_v5.json.bak4"), Is.False);
        }

        [Test]
        public void CorruptSaveRecoversFromNewestGoodBackup()
        {
            var io = new MemorySaveIo();
            var m = NewManager(io);
            var state = m.LoadState(Now).State;
            state["economy"]["coins"] = 777;
            m.SaveNow(state);
            state["economy"]["coins"] = 888;
            m.SaveNow(state);
            io.Files["user/save_v5.json"] = "{ not json";
            var r = NewManager(io).LoadState(Now);
            Assert.That(r.Recovered, Is.True);
            Assert.That(r.Source, Is.EqualTo("bak1"));
            Assert.That(J.Int(r.State["economy"]["coins"]), Is.EqualTo(777));
            Assert.That(io.Files["user/save_v5.json.corrupt"], Is.EqualTo("{ not json"));
        }

        [Test]
        public void CrashBetweenRotationAndRenamePrefersTmp()
        {
            var io = new MemorySaveIo();
            var m = NewManager(io);
            var state = m.LoadState(Now).State;
            state["economy"]["coins"] = 1;
            m.SaveNow(state);
            io.CrashAfterRenameOf = "user/save_v5.json"; // rotation moves save → .bak1, then "crash" before tmp → save
            state["economy"]["coins"] = 2;
            Assert.That(m.SaveNow(state), Is.False);
            Assert.That(io.Files.ContainsKey("user/save_v5.json"), Is.False);
            io.Crashed = false;
            var r = NewManager(io).LoadState(Now);
            Assert.That(r.Recovered, Is.True);
            Assert.That(r.Source, Is.EqualTo("tmp"));
            Assert.That(J.Int(r.State["economy"]["coins"]), Is.EqualTo(2));
        }

        [Test]
        public void WriteFailureKeepsOldSaveAndStaysDirty()
        {
            var io = new MemorySaveIo();
            var m = NewManager(io);
            var state = m.LoadState(Now).State;
            state["economy"]["coins"] = 5;
            m.SaveNow(state);
            io.FailWrite = true;
            string failed = null;
            m.SaveFailed += reason => failed = reason;
            m.MarkDirty(0);
            state["economy"]["coins"] = 6;
            Assert.That(m.AutosaveTick(state, 1000), Is.False);
            Assert.That(failed, Does.Contain("fehlgeschlagen"));
            Assert.That(m.IsDirty, Is.True);
            Assert.That(J.Int(JObject.Parse(io.Files["user/save_v5.json"])["economy"]["coins"]), Is.EqualTo(5));
            Assert.That(m.AutosaveTick(state, 1500), Is.False, "retry only after another debounce window");
            io.FailWrite = false;
            Assert.That(m.AutosaveTick(state, 1000 + SaveManager.DefaultDebounceMs), Is.True);
            Assert.That(m.IsDirty, Is.False);
        }

        [Test]
        public void LegacySaveIsMigratedOnLoad()
        {
            var io = new MemorySaveIo();
            io.Files["user/save_v5.json"] = "{\"v\":4,\"coins\":10}";
            var r = NewManager(io).LoadState(Now);
            Assert.That(r.Recovered, Is.False);
            Assert.That(r.Fresh, Is.False);
            Assert.That(J.Int(r.State["economy"]["coins"]), Is.EqualTo(10 + SaveMigration.MovingBonusCoins));
            Assert.That(io.Files.ContainsKey("user/save_v5.json.corrupt"), Is.False);
        }

        [Test]
        public void StoreEmitsWatchedDiffsAndCatchesUpOffline()
        {
            var io = new MemorySaveIo();
            var store = new GameStateStore();
            store.Clock.Pin(Now);
            store.Clock.SetUtcOffsetMinutes(0);
            var coinsSeen = new List<int>();
            var levelSeen = new List<int>();
            store.CoinsChanged += c => coinsSeen.Add(c);
            store.LevelChanged += (l, _) => levelSeen.Add(l);
            var loaded = false;
            store.StateLoaded += (fresh, recovered) => loaded = fresh && !recovered;
            store.Manager.Io = io;
            store.Initialize("user/save_v5.json", 0);
            Assert.That(loaded, Is.True);
            Assert.That(coinsSeen, Is.EqualTo(new List<int> { 100 }));
            Assert.That(levelSeen, Is.EqualTo(new List<int> { 1 }));

            store.Update(s => Economy.Award((JObject)s["economy"], 50), 10);
            Assert.That(coinsSeen[coinsSeen.Count - 1], Is.EqualTo(150));
            store.Update(s => s["economy"]["coinsSpent"] = 1, 20);
            Assert.That(coinsSeen.Count, Is.EqualTo(2), "unrelated writes do not emit coins");

            // 2 h offline: awake decay at 0.3x → hunger 80 - 0.35*120*0.3 = 67.4
            store.Clock.Advance(2 * 3600 * 1000);
            List<string> events = null;
            store.GoobyEvents += e => events = e;
            store.OnResume(30);
            Assert.That(J.Num(store.GetValue("gooby.stats.hunger")), Is.EqualTo(67.4).Within(1e-9));
            Assert.That(store.GetValue("gooby.lastTickAt", 0L), Is.EqualTo(store.Clock.NowMs()));
            Assert.That(events, Is.Null.Or.Empty);

            store.OnPauseOrQuit();
            Assert.That(J.Int(JObject.Parse(io.Files["user/save_v5.json"])["economy"]["coins"]), Is.EqualTo(150));
        }

        [Test]
        public void LiveTickWakesSleepingGoobyWithGrants()
        {
            var state = SaveSchema.DefaultState(Now);
            state["gooby"]["stats"]["energy"] = 20;
            Assert.That(Sleep.StartSleepState(state, Now), Is.True);
            var wakeAt = J.Long(state["gooby"]["sleep"]["wakeAt"]);
            Assert.That(wakeAt, Is.EqualTo(Now + 24 * 60000L)); // ceil(30*(80/100)) = 24 min
            var events = GoobyTicker.LiveTick(state, wakeAt + 1000);
            Assert.That(events, Does.Contain("wokeUp"));
            Assert.That(J.IsTrue(state["gooby"]["sleep"]["sleeping"]), Is.False);
            Assert.That(J.Num(state["progression"]["xp"]), Is.EqualTo(10.0));
            Assert.That(J.Int(state["achievements"]["counters"]["sleeps"]), Is.EqualTo(1));
            Assert.That(J.Num(state["gooby"]["stats"]["energy"]), Is.EqualTo(100.0));
        }

        [Test]
        public void GameClockLocalDayHonoursOffset()
        {
            var clock = new Gooby.Core.Time.GameClock();
            clock.Pin(Now); // 2026-01-15T12:00:00Z
            clock.SetUtcOffsetMinutes(0);
            Assert.That(clock.LocalDay(), Is.EqualTo("2026-01-15"));
            clock.SetUtcOffsetMinutes(13 * 60);
            Assert.That(clock.LocalDay(), Is.EqualTo("2026-01-16"));
            clock.SetUtcOffsetMinutes(-13 * 60);
            Assert.That(clock.LocalDay(), Is.EqualTo("2026-01-14"));
        }
    }
}
