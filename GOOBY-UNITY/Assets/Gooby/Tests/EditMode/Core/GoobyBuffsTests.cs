using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Timed buffs on Gooby's needs (port of buffs.gd and urlaubs_bonus.gd's "erholt").</summary>
    public class GoobyBuffsTests
    {
        private const long Now = 1_791_158_400_000L;

        [Test]
        public void OnePerIdReplacedNotStacked()
        {
            var slice = GoobyBuffs.DefaultSlice();
            GoobyBuffs.Add(slice, "spass_plus", "fun", 10, 5, Now);
            GoobyBuffs.Add(slice, "spass_plus", "fun", 10, 8, Now);
            GoobyBuffs.Add(slice, GoobyBuffs.FrischeZaehne, "hygiene", 10, 3, Now);
            Assert.That(((JArray)slice["aktiv"]).Count, Is.EqualTo(2));
            Assert.That(J.Long(slice["aktiv"][0]["until_ms"]), Is.EqualTo(Now + 8 * GoobyBuffs.MsPerHour), "granting again extends");
            Assert.That(GoobyBuffs.StatBonus(slice, "fun", Now), Is.EqualTo(10.0));
            Assert.That(GoobyBuffs.StatBonus(slice, "energy", Now), Is.EqualTo(0.0));
        }

        [Test]
        public void ExpiredOnesStopCountingAndArePruned()
        {
            var slice = GoobyBuffs.DefaultSlice();
            GoobyBuffs.Add(slice, "a", "fun", 5, 1, Now);
            GoobyBuffs.Add(slice, "b", "fun", 7, 3, Now);
            var later = Now + 2 * GoobyBuffs.MsPerHour;
            Assert.That(GoobyBuffs.Active(slice, later).Count, Is.EqualTo(1));
            Assert.That(GoobyBuffs.StatBonus(slice, "fun", later), Is.EqualTo(7.0));
            Assert.That(GoobyBuffs.Prune(slice, later), Is.EqualTo(1));
            Assert.That(((JArray)slice["aktiv"]).Count, Is.EqualTo(1));
        }

        [Test]
        public void NormalizeDropsJunk()
        {
            var slice = GoobyBuffs.NormalizeSlice(JObject.Parse(@"{""aktiv"":[{""id"":""ok"",""stat"":""fun"",""wert"":1,""until_ms"":5},{""stat"":""fun""},""x""]}"));
            Assert.That(((JArray)slice["aktiv"]).Count, Is.EqualTo(1));
            Assert.That(((JArray)GoobyBuffs.NormalizeSlice(JToken.Parse("3"))["aktiv"]).Count, Is.EqualTo(0));
        }

        [Test]
        public void TheHolidaysRestRunsExactlyAsLongAsTheBrake()
        {
            Fixtures.InstallContentSource();
            GoobyBuffs.RegisterSlice();
            var gs = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            gs.Clock.Pin(Now);
            gs.Initialize("user/save_v5.json", 0);
            Assert.That(GoobyBuffs.SyncErholt(gs, Now), Is.False, "no holiday, no rest");
            var bis = Now + 48 * GoobyBuffs.MsPerHour;
            gs.Update(s => J.EnsureDict(s, "vacation")["erholtBis"] = bis);
            Assert.That(GoobyBuffs.SyncErholt(gs, Now), Is.True);
            var slice = GoobyBuffs.SliceOf(gs.State);
            Assert.That(GoobyBuffs.StatBonus(slice, "energy", Now), Is.EqualTo(5.0));
            Assert.That(J.Long(slice["aktiv"][0]["until_ms"]), Is.EqualTo(bis).Within(1000));
            Assert.That(GoobyBuffs.SyncErholt(gs, Now + 1000), Is.False, "already there: not granted again");
            Assert.That(GoobyBuffs.StatBonus(GoobyBuffs.SliceOf(gs.State), "energy", bis + 1), Is.EqualTo(0.0), "ends with the brake");
        }
    }
}
