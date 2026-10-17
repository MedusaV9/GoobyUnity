using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The ranch quests (rquest_engine.gd / rquest_state.gd / rquest_katalog.gd tests): catalog, states, events, waits, rewards.</summary>
    public class RanchQuestsTests
    {
        private const long T0 = 1_790_000_000_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            RanchQuestKatalog.ResetCache();
        }

        private static JObject Spieler() => new JObject
        {
            ["economy"] = new JObject { ["coins"] = 100 },
            ["ranch"] = new JObject { ["gekauft"] = true, ["wirtschaft"] = new JObject { ["lager"] = new JObject { ["apfel"] = 1 } } },
        };

        [Test]
        public void TheCatalogHasTheTenChaptersAndTheSideQuests()
        {
            var haupt = RanchQuestKatalog.Hauptreihe;
            Assert.That(haupt.Count, Is.EqualTo(10));
            Assert.That(haupt.Select(q => q.Kapitel), Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(RanchQuestKatalog.Alle.Count(q => q.Typ == RanchQuestKatalog.Neben), Is.EqualTo(27));
            Assert.That(RanchQuestKatalog.Geber.Count, Is.EqualTo(13));
            foreach (var q in RanchQuestKatalog.Alle)
            {
                Assert.That(q.Ziele, Is.Not.Empty, q.Id);
                Assert.That(q.Ziele.All(z => RanchQuestKatalog.ZielTypen.Contains(z.Typ)), Is.True, q.Id);
                Assert.That(RanchNpc.Fuer(q.Geber), Is.Not.Null, "every giver is a ranch person: " + q.Geber);
            }
        }

        [Test]
        public void TheFirstChapterOpensTheRest()
        {
            var s = Spieler();
            var h1 = RanchQuestKatalog.Fuer("haupt_01");
            var h2 = RanchQuestKatalog.Fuer("haupt_02");
            Assert.That(RanchQuests.Status(s, h1), Is.EqualTo(RanchQuests.Verfuegbar));
            Assert.That(RanchQuests.Status(s, h2), Is.EqualTo(RanchQuests.Gesperrt));
            Assert.That(RanchQuests.Kapitel(s), Is.EqualTo(1));
            Assert.That(RanchQuests.Annehmen(s, "haupt_01", T0), Is.True);
            Assert.That(RanchQuests.Annehmen(s, "haupt_01", T0), Is.False, "once");
            Assert.That(RanchQuests.Status(s, h1), Is.EqualTo(RanchQuests.Aktiv));
        }

        [Test]
        public void GoalsCountInOrderAndOnlyTheCurrentOne()
        {
            var s = Spieler();
            RanchQuests.Annehmen(s, "haupt_01", T0);
            var q = RanchQuestKatalog.Fuer("haupt_01");
            Assert.That(RanchQuests.Melde(s, "gehe_zu", "stall", T0), Is.Empty, "the stable is the second goal");
            Assert.That(RanchQuests.Melde(s, "sprich_mit", "rosi", T0), Is.EqualTo(new[] { "haupt_01" }));
            Assert.That(RanchQuests.AktuellesZiel(s, q).Typ, Is.EqualTo("gehe_zu"));
            RanchQuests.Melde(s, "gehe_zu", "stall", T0);
            RanchQuests.Melde(s, "pflege", "ausmisten", T0);
            RanchQuests.Melde(s, "sammle", "hufeisen", T0, 2);
            Assert.That(RanchQuests.Fortschritt(s, q).ist, Is.EqualTo(2));
            Assert.That(RanchQuests.Item(s, "hufeisen"), Is.EqualTo(2), "collected items land in the bag");
            RanchQuests.Melde(s, "sammle", "hufeisen", T0);
            Assert.That(RanchQuests.Status(s, q), Is.EqualTo(RanchQuests.Erfuellbar));
        }

        [Test]
        public void HandingInPaysCoinsItemsHeartsAndOpensTheNextChapter()
        {
            var s = Spieler();
            RanchQuests.Annehmen(s, "haupt_01", T0);
            foreach (var (typ, wert, n) in new[] { ("sprich_mit", "rosi", 1), ("gehe_zu", "stall", 1), ("pflege", "ausmisten", 1), ("sammle", "hufeisen", 3) })
                RanchQuests.Melde(s, typ, wert, T0, n);
            var b = RanchQuests.Abgeben(s, "haupt_01", T0);
            Assert.That(b, Is.Not.Null);
            Assert.That(b.Muenzen, Is.EqualTo(150));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(250));
            Assert.That(J.Int(s["ranch"]["wirtschaft"]["lager"]["apfel"]), Is.EqualTo(3), "two apples into the farm store");
            Assert.That(RanchNpc.Punkte(s, "rosi"), Is.EqualTo(8.0));
            Assert.That(RanchQuests.Abgeben(s, "haupt_01", T0), Is.Null, "once");
            Assert.That(RanchQuests.Status(s, RanchQuestKatalog.Fuer("haupt_01")), Is.EqualTo(RanchQuests.Erledigt));
            Assert.That(RanchQuests.Kapitel(s), Is.EqualTo(2));
            Assert.That(RanchQuests.Status(s, RanchQuestKatalog.Fuer("haupt_02")), Is.EqualTo(RanchQuests.Verfuegbar));
            Assert.That(RanchQuests.Status(s, RanchQuestKatalog.Fuer("neben_rosi_1")), Is.EqualTo(RanchQuests.Verfuegbar), "side quests open behind the chapter");
        }

        [Test]
        public void AWaitParksTheQuestUntilItsTime()
        {
            var s = Spieler();
            s["ranch"]["quests"] = new JObject { ["erledigt"] = new JArray("haupt_01", "haupt_02", "haupt_03") };
            Assert.That(RanchQuests.Annehmen(s, "haupt_04", T0), Is.True);
            RanchQuests.Melde(s, "sprich_mit", "selma", T0);
            RanchQuests.Melde(s, "sammle", "wolle", T0, 2);
            var q = RanchQuestKatalog.Fuer("haupt_04");
            Assert.That(RanchQuests.Status(s, q), Is.EqualTo(RanchQuests.Wartend));
            Assert.That(RanchQuests.BereitIn(s, q, T0), Is.EqualTo(480 * 60_000L));
            Assert.That(RanchQuests.Melde(s, "sprich_mit", "eisenhuf", T0), Is.Empty, "events wait while the quest waits");
            Assert.That(RanchQuests.Tick(s, T0 + 479 * 60_000L), Is.Empty);
            Assert.That(RanchQuests.Tick(s, T0 + 480 * 60_000L), Is.EqualTo(new[] { "haupt_04" }));
            RanchQuests.Melde(s, "sprich_mit", "eisenhuf", T0);
            var b = RanchQuests.Abgeben(s, "haupt_04", T0);
            Assert.That(b.Freischalten, Is.EqualTo(new[] { "sattel_kartoffel" }));
            Assert.That(RanchQuests.Freigeschaltet(s), Contains.Item("sattel_kartoffel"));
        }

        [Test]
        public void HeartsGateSideQuestsAndPlacesCountInCompetitions()
        {
            var s = Spieler();
            var saat = RanchQuestKatalog.Fuer("neben_rosi_saat");
            Assert.That(RanchQuests.Status(s, saat), Is.EqualTo(RanchQuests.Gesperrt));
            RanchNpc.Gutschreiben(s, "rosi", 45, T0);
            Assert.That(RanchQuests.Status(s, saat), Is.EqualTo(RanchQuests.Verfuegbar), "three hearts with Rosi");
            s["ranch"]["quests"] = new JObject { ["erledigt"] = new JArray("haupt_01", "haupt_02", "haupt_03", "haupt_04", "haupt_05") };
            RanchQuests.Annehmen(s, "neben_punktabzug_1", T0);
            RanchNpc.Gutschreiben(s, "punktabzug", 45, T0);
            RanchQuests.Annehmen(s, "neben_punktabzug_2", T0);
            Assert.That(RanchQuests.Melde(s, "gewinne_wettbewerb", "parcours", T0, 1, 4), Is.Empty, "fourth place is not enough");
            Assert.That(RanchQuests.Melde(s, "gewinne_wettbewerb", "parcours", T0, 1, 2), Is.EqualTo(new[] { "neben_punktabzug_2" }));
            Assert.That(RanchQuests.VomGeber(s, "punktabzug", RanchQuests.Erfuellbar).Select(q => q.Id), Is.EqualTo(new[] { "neben_punktabzug_2" }));
        }

        [Test]
        public void TheDaysThingsToCollectLieOnLandAndArePickedUpOnce()
        {
            RanchKarte.ResetCache();
            var heute = RanchSammeln.FuerTag("2026-10-11");
            Assert.That(heute.Count, Is.EqualTo(31));
            Assert.That(heute.Select(st => (st.X, st.Z)), Is.EqualTo(RanchSammeln.FuerTag("2026-10-11").Select(st => (st.X, st.Z))), "deterministic");
            Assert.That(heute.Select(st => st.X), Is.Not.EqualTo(RanchSammeln.FuerTag("2026-10-12").Select(st => st.X)), "a new day, new spots");
            Assert.That(heute.All(st => RanchKarte.IstBegehbar(st.X, st.Z)), Is.True);
            Assert.That(heute.Select(st => st.Item).Distinct(), Is.EquivalentTo(new[] { "hufeisen", "blume", "beere", "brett", "schluessel", "wolle" }));
            var s = Spieler();
            RanchQuests.Annehmen(s, "haupt_01", T0);
            foreach (var (typ, wert) in new[] { ("sprich_mit", "rosi"), ("gehe_zu", "stall"), ("pflege", "ausmisten") }) RanchQuests.Melde(s, typ, wert, T0);
            var eisen = heute.First(st => st.Item == "hufeisen");
            Assert.That(RanchSammeln.Aufsammeln(s, "2026-10-11", eisen, T0), Is.EqualTo(new[] { "haupt_01" }));
            Assert.That(RanchSammeln.Aufsammeln(s, "2026-10-11", eisen, T0), Is.Null, "once a day");
            Assert.That(RanchSammeln.Aufgesammelt(s, "2026-10-11"), Is.EqualTo(new[] { eisen.Id }));
            Assert.That(RanchSammeln.Aufgesammelt(s, "2026-10-12"), Is.Empty, "tomorrow it lies there again");
            Assert.That(RanchQuests.Item(s, "hufeisen"), Is.EqualTo(1));
        }

        [Test]
        public void TheSaveHealsUnknownQuests()
        {
            var s = Spieler();
            s["ranch"]["quests"] = new JObject { ["aktiv"] = new JObject { ["gibtsnicht"] = new JObject(), ["haupt_01"] = new JObject { ["status"] = "aktiv" } }, ["erledigt"] = "kaputt" };
            var slice = RanchQuests.Slice(s);
            Assert.That(((JObject)slice["aktiv"]).Properties().Select(p => p.Name), Is.EqualTo(new[] { "haupt_01" }));
            Assert.That(slice["erledigt"], Is.InstanceOf<JArray>());
        }
    }
}
