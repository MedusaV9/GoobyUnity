using System;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Gooby's soul moments (port of tests/unit/test_soul_triggers.gd + test_soul_service.gd): the triggers' bounds
    /// (dayparts, absence, Easter, holidays, the snow season, the frequency brake) and the one moment on entering —
    /// rituals beat greetings, decide → book → decide gates, a jump over a milestone stays quiet, greetings and
    /// weather comments come once a day and yield to the brake, rituals never do.
    /// </summary>
    public class SoulTests
    {
        private const long MsD = Soul.MsPerDay;

        private static JArray Defs() => JArray.Parse(@"[
            {""id"":""gruss_morgen"",""kind"":""gruss"",""text_keys"":[""k.morgen""]},
            {""id"":""gruss_abend"",""kind"":""gruss"",""text_keys"":[""k.abend""]},
            {""id"":""gruss_gefreut"",""kind"":""gruss"",""text_keys"":[""k.gefreut""]},
            {""id"":""gruss_eingeschnappt"",""kind"":""gruss"",""text_keys"":[""k.miffed""]},
            {""id"":""gruss_vermisst"",""kind"":""gruss"",""text_keys"":[""k.vermisst""]},
            {""id"":""ritual_geburtstag_spieler"",""kind"":""ritual"",""text_keys"":[""k.geb""],""aktion"":""konfetti""},
            {""id"":""ritual_geburtstag_gooby"",""kind"":""ritual"",""text_keys"":[""k.ggeb""]},
            {""id"":""ritual_jubilaeum"",""kind"":""ritual"",""text_keys"":[""k.jub""]},
            {""id"":""ritual_halloween"",""kind"":""ritual"",""text_keys"":[""k.hallo""]},
            {""id"":""ritual_erster_schnee"",""kind"":""ritual"",""text_keys"":[""k.schnee""]},
            {""id"":""wetter_regen"",""kind"":""wetter"",""text_keys"":[""k.regen""]}
        ]");

        private static JObject NewSlice() => Soul.Slice(new JObject());

        private static long MsOf(int year, int month, int day, int hour) =>
            new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        private static Soul.Ctx Ctx(int year, int month, int day, int hour = 12, long gapMs = 0, string wetter = "sonne") => new Soul.Ctx
        {
            NowMs = MsOf(year, month, day, hour), Year = year, Month = month, Day = day, Hour = hour, GapMs = gapMs,
            WetterTyp = wetter, Schnee = wetter == "schnee", PlayerName = "Mira", Nickname = "Gooby",
        };

        // ── triggers ──

        [Test]
        public void DaypartBounds()
        {
            Assert.That(Soul.Daypart(4), Is.EqualTo("nacht"));
            Assert.That(Soul.Daypart(5), Is.EqualTo("morgen"));
            Assert.That(Soul.Daypart(10), Is.EqualTo("morgen"));
            Assert.That(Soul.Daypart(11), Is.EqualTo("mittag"));
            Assert.That(Soul.Daypart(15), Is.EqualTo("nachmittag"));
            Assert.That(Soul.Daypart(18), Is.EqualTo("abend"));
            Assert.That(Soul.Daypart(22), Is.EqualTo("abend"));
            Assert.That(Soul.Daypart(23), Is.EqualTo("nacht"));
        }

        [Test]
        public void AbsenceThresholds()
        {
            Assert.That(Soul.AbsenceKind(5 * Soul.MsPerHour), Is.EqualTo(""));
            Assert.That(Soul.AbsenceKind(6 * Soul.MsPerHour), Is.EqualTo("gefreut"));
            Assert.That(Soul.AbsenceKind(48 * Soul.MsPerHour), Is.EqualTo("eingeschnappt"));
            Assert.That(Soul.AbsenceKind(168 * Soul.MsPerHour), Is.EqualTo("vermisst"));
        }

        [Test]
        public void EasterOfKnownYearsAndHolidays()
        {
            Assert.That(Soul.EasterMonthDay(2024), Is.EqualTo((3, 31)));
            Assert.That(Soul.EasterMonthDay(2025), Is.EqualTo((4, 20)));
            Assert.That(Soul.EasterMonthDay(2026), Is.EqualTo((4, 5)));
            Assert.That(Soul.HolidayFor(2026, 4, 5), Is.EqualTo("ostern"));
            Assert.That(Soul.HolidayFor(2026, 12, 26), Is.EqualTo("weihnachten"));
            Assert.That(Soul.HolidayFor(2026, 10, 31), Is.EqualTo("halloween"));
            Assert.That(Soul.HolidayFor(2026, 7, 26), Is.EqualTo(""));
        }

        [Test]
        public void SnowSeasonSpansTheNewYear()
        {
            Assert.That(Soul.SnowSeason(2026, 12), Is.EqualTo("2026"));
            Assert.That(Soul.SnowSeason(2027, 1), Is.EqualTo("2026"));
            Assert.That(Soul.SnowSeason(2027, 2), Is.EqualTo("2026"));
        }

        [Test]
        public void TheBrakeKeepsAGapAndADailyCap()
        {
            var amb = new JObject { ["day"] = "2026-07-26", ["count"] = 3, ["lastAt"] = 1_000_000L };
            Assert.That(Soul.AmbientAllowed(amb, 1_000_000L + 60_000L, "2026-07-26"), Is.False, "90 s apart");
            Assert.That(Soul.AmbientAllowed(amb, 1_000_000L + 91_000L, "2026-07-26"), Is.True);
            var full = new JObject { ["day"] = "2026-07-26", ["count"] = 12, ["lastAt"] = 0L };
            Assert.That(Soul.AmbientAllowed(full, 9_000_000L, "2026-07-26"), Is.False, "12 a day");
            Assert.That(Soul.AmbientAllowed(full, 9_000_000L, "2026-07-27"), Is.True, "a new day resets the cap");
        }

        // ── the moment on entering ──

        [Test]
        public void ThePlayersBirthdayBeatsTheGreeting()
        {
            var slice = NewSlice();
            slice["playerBirthday"] = new JObject { ["month"] = 3, ["day"] = 14 };
            slice["firstMetAt"] = MsOf(2026, 1, 1, 12);
            var m = Soul.DecideEnter(slice, Defs(), Ctx(2026, 3, 14, 8), 0.9);
            Assert.That(m.Id, Is.EqualTo("ritual_geburtstag_spieler"));
            Assert.That(m.Kind, Is.EqualTo("ritual"));
            Assert.That(m.Args["name"], Is.EqualTo("Mira"));
            Assert.That(m.Aktion, Is.EqualTo("konfetti"));
        }

        [Test]
        public void DecideBookDecideGatesTheRitualAndNextYearItIsBack()
        {
            var slice = NewSlice();
            slice["playerBirthday"] = new JObject { ["month"] = 3, ["day"] = 14 };
            slice["firstMetAt"] = MsOf(2026, 1, 1, 12);
            var ctx = Ctx(2026, 3, 14, 8);
            Soul.BookEnter(slice, Soul.DecideEnter(slice, Defs(), ctx, 0.9), ctx);
            Assert.That(Soul.DecideEnter(slice, Defs(), ctx, 0.9)?.Id, Is.Not.EqualTo("ritual_geburtstag_spieler"), "not twice today");
            Assert.That(Soul.DecideEnter(slice, Defs(), Ctx(2027, 3, 14, 8), 0.9)?.Id, Is.EqualTo("ritual_geburtstag_spieler"), "next year again");
        }

        [Test]
        public void GoobysBirthdayIsTheMoveInDay()
        {
            var slice = NewSlice();
            slice["firstMetAt"] = MsOf(2025, 7, 26, 10);
            Assert.That(Soul.DecideEnter(slice, Defs(), Ctx(2026, 7, 26, 12), 0.9)?.Id, Is.EqualTo("ritual_geburtstag_gooby"));
            var fresh = NewSlice();
            fresh["firstMetAt"] = MsOf(2026, 7, 26, 10);
            Assert.That(Soul.DecideEnter(fresh, Defs(), Ctx(2026, 7, 26, 12), 0.9)?.Id, Is.Not.EqualTo("ritual_geburtstag_gooby"), "the move-in day itself is no birthday");
        }

        [Test]
        public void TheHundredthDayIsCelebratedAndAJumpOverItStaysQuiet()
        {
            var start = MsOf(2026, 1, 1, 12);
            var slice = NewSlice();
            slice["firstMetAt"] = start;
            var ctx = Ctx(2026, 4, 11, 12);
            ctx.NowMs = start + 100 * MsD;
            var m = Soul.DecideEnter(slice, Defs(), ctx, 0.9);
            Assert.That(m.Id, Is.EqualTo("ritual_jubilaeum"));
            Assert.That(m.Args["tage"], Is.EqualTo(100));
            var slice2 = NewSlice();
            slice2["firstMetAt"] = start;
            var ctx2 = Ctx(2026, 4, 15, 12);
            ctx2.NowMs = start + 104 * MsD;
            Assert.That(Soul.DecideEnter(slice2, Defs(), ctx2, 0.9)?.Id, Is.Not.EqualTo("ritual_jubilaeum"), "skipped = quiet");
        }

        [Test]
        public void HalloweenAndTheFirstSnowOncePerSeason()
        {
            var slice = NewSlice();
            slice["firstMetAt"] = MsOf(2026, 1, 2, 12);
            var ctx = Ctx(2026, 10, 31, 18);
            var m = Soul.DecideEnter(slice, Defs(), ctx, 0.9);
            Assert.That(m.Id, Is.EqualTo("ritual_halloween"));
            Soul.BookEnter(slice, m, ctx);
            var schneeCtx = Ctx(2026, 12, 10, 9, 0, "schnee");
            var schnee = Soul.DecideEnter(slice, Defs(), schneeCtx, 0.9);
            Assert.That(schnee.Id, Is.EqualTo("ritual_erster_schnee"));
            Soul.BookEnter(slice, schnee, schneeCtx);
            Assert.That(Soul.DecideEnter(slice, Defs(), Ctx(2027, 1, 20, 9, 0, "schnee"), 0.9)?.Id, Is.Not.EqualTo("ritual_erster_schnee"), "season already celebrated");
        }

        [Test]
        public void OneGreetingADayByTheTimeOfDay()
        {
            var slice = NewSlice();
            slice["firstMetAt"] = MsOf(2026, 7, 20, 12);
            var ctx = Ctx(2026, 7, 26, 8);
            var m = Soul.DecideEnter(slice, Defs(), ctx, 0.9);
            Assert.That(m.Id, Is.EqualTo("gruss_morgen"));
            Soul.BookEnter(slice, m, ctx);
            var abend = Ctx(2026, 7, 26, 19);
            abend.NowMs = ctx.NowMs + 11 * Soul.MsPerHour;
            Assert.That(Soul.DecideEnter(slice, Defs(), abend, 0.9)?.Id, Is.Not.EqualTo("gruss_abend"), "one greeting a day");
        }

        [Test]
        public void AnAbsenceGreetsBeforeTheTimeOfDay()
        {
            var slice = NewSlice();
            slice["firstMetAt"] = MsOf(2026, 7, 1, 12);
            Assert.That(Soul.DecideEnter(slice, Defs(), Ctx(2026, 7, 26, 8, 3 * MsD), 0.9).Id, Is.EqualTo("gruss_eingeschnappt"));
            Assert.That(Soul.DecideEnter(slice, Defs(), Ctx(2026, 7, 26, 8, 8 * MsD), 0.9).Id, Is.EqualTo("gruss_vermisst"));
        }

        [Test]
        public void TheBrakeBlocksAmbientMomentsButNoRitual()
        {
            var slice = NewSlice();
            slice["playerBirthday"] = new JObject { ["month"] = 7, ["day"] = 26 };
            slice["firstMetAt"] = MsOf(2026, 7, 1, 12);
            var ctx = Ctx(2026, 7, 26, 8);
            slice["ambient"] = new JObject { ["day"] = "2026-07-26", ["count"] = 3, ["lastAt"] = ctx.NowMs - 1_000 };
            var m = Soul.DecideEnter(slice, Defs(), ctx, 0.9);
            Assert.That(m.Id, Is.EqualTo("ritual_geburtstag_spieler"), "rituals ignore the brake");
            Soul.BookEnter(slice, m, ctx);
            Assert.That(Soul.DecideEnter(slice, Defs(), ctx, 0.9), Is.Null, "the greeting waits for the brake");
        }

        [Test]
        public void TheSameRainIsCommentedOnceADay()
        {
            var slice = NewSlice();
            slice["firstMetAt"] = MsOf(2026, 7, 1, 12);
            slice["lastGreetDay"] = "2026-07-26";
            var ctx = Ctx(2026, 7, 26, 14, 0, "regen");
            var m = Soul.DecideEnter(slice, Defs(), ctx, 0.9);
            Assert.That(m.Id, Is.EqualTo("wetter_regen"));
            Soul.BookEnter(slice, m, ctx);
            Assert.That(Soul.DecideEnter(slice, Defs(), Ctx(2026, 7, 26, 16, 0, "regen"), 0.9)?.Id, Is.Not.EqualTo("wetter_regen"));
        }

        [Test]
        public void TheVisitStampSetsFirstMetOnceAndReturnsTheGap()
        {
            var state = new JObject { ["meta"] = new JObject { ["createdAt"] = 1_000L } };
            Assert.That(Soul.TouchVisit(state, 5_000L), Is.EqualTo(0L), "first visit");
            Assert.That((long)state["soul"]["firstMetAt"], Is.EqualTo(1_000L), "the move-in day");
            Assert.That(Soul.TouchVisit(state, 9_000L), Is.EqualTo(4_000L));
            Assert.That((long)state["soul"]["firstMetAt"], Is.EqualTo(1_000L), "set once");
        }

        [Test]
        public void ThePackDefsLoadAndCoverTheGreetings()
        {
            Fixtures.InstallContentSource();
            var defs = Soul.Defs();
            foreach (var id in new[] { "gruss_morgen", "gruss_nacht", "gruss_vermisst", "ritual_geburtstag_gooby", "wetter_schnee" })
                Assert.That(Soul.DefById(defs, id), Is.Not.Null, id);
        }
    }
}
