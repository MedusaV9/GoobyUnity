using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The riding village Hufingen (ports of dorf_state.gd, dorf_wirtschaft.gd and dorf_haendler.gd): the healed village
    /// key, the trader's daily rotation and her buying/selling with the stable's boxes, the feed yard (hay into the farm
    /// store, oats and treats, buying the harvest back), the smithy's horseshoes and the furniture barn.
    /// </summary>
    public class RanchDorfTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            Gooby.Core.Home.FurnitureCatalog.ResetCache();
        }

        private const long Mittag = (3L * 24 + 12) * 3_600_000L;
        private const string Tag = "2026-10-11";

        private static JObject Ranch(int coins)
        {
            var s = new JObject
            {
                ["progression"] = new JObject { ["level"] = 20 },
                ["economy"] = new JObject { ["coins"] = coins + RanchHof.Preis },
                ["inventory"] = new JObject { ["food"] = new JObject { ["carrot"] = 2 } },
                ["home"] = new JObject { ["storage"] = new JArray(), ["storageCapacity"] = 100 },
            };
            Assert.That(RanchKauf.Kaufe(s, Mittag), Is.EqualTo(RanchKauf.Ok));
            return s;
        }

        private static int Coins(JObject s) => J.Int(s["economy"]["coins"]);

        [Test]
        public void VillageKeyHealsAndIsDiscoveredOnce()
        {
            var roh = new JObject { ["entdeckt"] = "ja", ["futter"] = new JObject { ["hafer"] = -3, ["leckerli"] = 2 }, ["hufeisen"] = new JObject { ["owned"] = new JArray("hufeisen_gold", "hufeisen_gold", 7) }, ["eigen"] = 1 };
            var dorf = RanchDorf.Normalize(roh);
            Assert.That(J.IsTrue(dorf["entdeckt"]), Is.False);
            Assert.That(J.Int(dorf["futter"]["hafer"]), Is.EqualTo(0));
            Assert.That(J.Int(dorf["futter"]["leckerli"]), Is.EqualTo(2));
            Assert.That(((JArray)dorf["hufeisen"]["owned"]).Select(t => J.Str(t)), Is.EqualTo(new[] { "hufeisen_gold" }));
            Assert.That(J.Int(dorf["eigen"]), Is.EqualTo(1), "unknown keys stay");

            var s = Ranch(0);
            Assert.That(RanchDorf.IstEntdeckt(s), Is.False);
            Assert.That(RanchDorf.Entdecke(s, Mittag), Is.True);
            Assert.That(RanchDorf.Entdecke(s, Mittag + 1), Is.False);
            Assert.That(J.Long(s["ranch"]["dorf"]["entdecktAm"]), Is.EqualTo(Mittag));
            Assert.That(RanchHof.PferdIds(s).Count, Is.EqualTo(3), "the farm stays untouched");
        }

        [Test]
        public void TheTradersOfferRotatesDailyWithoutFomo()
        {
            var heute = DorfHaendler.TagesAngebot(Tag, RanchDorf.Pool, 3);
            Assert.That(heute.Count, Is.EqualTo(3));
            Assert.That(heute.Select(p => p.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(DorfHaendler.TagesAngebot(Tag, RanchDorf.Pool, 3).Select(p => p.Id), Is.EqualTo(heute.Select(p => p.Id)), "same day, same horses");
            var gesehen = new HashSet<string>();
            var anders = false;
            for (var d = 1; d <= 30; d++)
            {
                var tag = "2026-11-" + d.ToString("00");
                var angebot = DorfHaendler.TagesAngebot(tag, RanchDorf.Pool, 3).Select(p => p.Id).ToList();
                anders |= !angebot.SequenceEqual(heute.Select(p => p.Id));
                foreach (var id in angebot) gesehen.Add(id);
            }
            Assert.That(anders, Is.True, "other days bring other horses");
            Assert.That(gesehen.Count, Is.EqualTo(RanchDorf.Pool.Length), "every horse comes back");
            Assert.That(DorfHaendler.TagesAngebot(Tag, new RanchDorf.PoolPferd[0], 3), Is.Empty);
        }

        [Test]
        public void BuyingAndSellingHorsesWithTheStablesBoxes()
        {
            var s = Ranch(2000);
            var angebot = DorfHaendler.Angebot(s, Tag);
            var erstes = angebot[0];
            Assert.That(DorfHaendler.PferdKaufen(s, erstes.Id, Tag).code, Is.EqualTo(DorfHaendler.StallVoll), "three start horses fill the first two boxes");
            Assert.That(RanchWirtschaft.AusbauKaufen(s, "boxen2"), Is.EqualTo(RanchWirtschaft.Ok));
            var vorher = Coins(s);
            Assert.That(DorfHaendler.PferdKaufen(s, "gibtsnicht", Tag).code, Is.EqualTo(DorfHaendler.NichtImAngebot));
            var (code, preis) = DorfHaendler.PferdKaufen(s, erstes.Id, Tag);
            Assert.That(code, Is.EqualTo(DorfHaendler.Ok));
            Assert.That(preis, Is.EqualTo(erstes.Preis));
            Assert.That(Coins(s), Is.EqualTo(vorher - erstes.Preis));
            var neu = RanchHof.Pferd(s, erstes.Id);
            Assert.That(J.Int(neu["kaufpreis"]), Is.EqualTo(erstes.Preis));
            Assert.That(J.Str(neu["farbeHex"]), Is.EqualTo(erstes.Farbe));
            Assert.That(J.Str(neu["nameKey"]), Is.EqualTo(erstes.NameKey));
            Assert.That((double)neu["werte"]["hunger"], Is.EqualTo(80.0), "care works right away");
            Assert.That(DorfHaendler.Angebot(s, Tag).Any(p => p.Id == erstes.Id), Is.False);
            Assert.That(DorfHaendler.StallVollFuer(s), Is.True, "four of four boxes");
            Assert.That(J.Int(s["ranch"]["dorf"]["pferdeGekauft"]), Is.EqualTo(1));

            Assert.That(DorfHaendler.Verkaufspreis(s, erstes.Id), Is.EqualTo(erstes.Preis / 2));
            Assert.That(DorfHaendler.Verkaufspreis(s, "tier_pferd_wolke"), Is.EqualTo(150), "start horses count 300");
            RanchWirtschaft.GearKaufen(s, "sattel_rot");
            RanchWirtschaft.GearAnlegen(s, "tier_pferd_wolke", "sattel", "rot");
            vorher = Coins(s);
            Assert.That(DorfHaendler.PferdVerkaufen(s, "tier_pferd_wolke"), Is.EqualTo((DorfHaendler.Ok, 150)));
            Assert.That(Coins(s), Is.EqualTo(vorher + 150));
            Assert.That(RanchHof.Pferd(s, "tier_pferd_wolke"), Is.Null);
            Assert.That(J.Path(s, "ranch", "wirtschaft", "gear", "equippedByHorse", "tier_pferd_wolke"), Is.Null, "its gear is put away");
            Assert.That(DorfHaendler.Angebot(s, Tag).Any(p => p.Id == erstes.Id), Is.False, "bought today stays out until tomorrow");

            foreach (var id in RanchHof.PferdIds(s).Skip(1).ToList()) DorfHaendler.PferdVerkaufen(s, id);
            Assert.That(RanchHof.PferdIds(s).Count, Is.EqualTo(1));
            Assert.That(DorfHaendler.PferdVerkaufen(s, RanchHof.PferdIds(s)[0]).code, Is.EqualTo(DorfHaendler.LetztesPferd));

            var arm = Ranch(RanchWirtschaft.AusbauPreis("boxen2"));
            Assert.That(RanchWirtschaft.AusbauKaufen(arm, "boxen2"), Is.EqualTo(RanchWirtschaft.Ok));
            var teuer = DorfHaendler.Angebot(arm, Tag)[0];
            Assert.That(DorfHaendler.PferdKaufen(arm, teuer.Id, Tag).code, Is.EqualTo(DorfHaendler.ZuTeuer));
            Assert.That(RanchHof.PferdIds(arm).Count, Is.EqualTo(3), "nothing changed");
        }

        [Test]
        public void FeedYardSmithyAndFurnitureBarn()
        {
            var s = Ranch(100);
            var heu = J.Int(RanchHof.Lager(s)["heu"]);
            Assert.That(DorfWirtschaft.FutterKaufen(s, "heu_bund"), Is.EqualTo((DorfWirtschaft.Ok, 35, 5)));
            Assert.That(J.Int(RanchHof.Lager(s)["heu"]), Is.EqualTo(heu + 5));
            Assert.That(DorfWirtschaft.FutterKaufen(s, "hafer").code, Is.EqualTo(DorfWirtschaft.Ok));
            Assert.That(DorfWirtschaft.FutterKaufen(s, "leckerli").code, Is.EqualTo(DorfWirtschaft.Ok));
            Assert.That(RanchDorf.Futter(s, "hafer"), Is.EqualTo(1));
            Assert.That(RanchPflege.Vorrat(s, "leckerli"), Is.EqualTo(1));
            Assert.That(Coins(s), Is.EqualTo(100 - 35 - 12 - 6));
            Assert.That(DorfWirtschaft.FutterKaufen(s, "gold").code, Is.EqualTo(DorfWirtschaft.Unbekannt));

            var apfel = J.Int(RanchHof.Lager(s)["apfel"]);
            Assert.That(DorfWirtschaft.ErnteVerkaufen(s, "apfel", apfel + 1).code, Is.EqualTo(DorfWirtschaft.LagerLeer));
            var vorher = Coins(s);
            Assert.That(DorfWirtschaft.ErnteVerkaufen(s, "apfel", apfel), Is.EqualTo((DorfWirtschaft.Ok, 4 * apfel)));
            Assert.That(Coins(s), Is.EqualTo(vorher + 4 * apfel));
            Assert.That(DorfWirtschaft.ErnteVerkaufen(s, "heu", 2).erloes, Is.EqualTo(10), "below the shop price");

            var reich = Ranch(2000);
            Assert.That(DorfWirtschaft.SchmiedeKaufen(reich, "hufeisen_bronze"), Is.EqualTo((DorfWirtschaft.Ok, 60)));
            Assert.That(DorfWirtschaft.SchmiedeKaufen(reich, "hufeisen_bronze").code, Is.EqualTo(DorfWirtschaft.SchonGekauft));
            Assert.That(DorfWirtschaft.HufeisenAnlegen(reich, "tier_pferd_beere", "hufeisen_silber"), Is.EqualTo(DorfWirtschaft.NichtGekauft));
            Assert.That(DorfWirtschaft.HufeisenAnlegen(reich, "tier_pferd_beere", "hufeisen_bronze"), Is.EqualTo(DorfWirtschaft.Ok));
            Assert.That(RanchDorf.HufeisenAn(reich, "tier_pferd_beere"), Is.EqualTo("hufeisen_bronze"));
            Assert.That(DorfWirtschaft.HufeisenAnlegen(reich, "tier_pferd_beere", ""), Is.EqualTo(DorfWirtschaft.Ok));
            Assert.That(RanchDorf.HufeisenAn(reich, "tier_pferd_beere"), Is.Null);
            Assert.That(DorfWirtschaft.HufeisenAnlegen(reich, "gibtsnicht", "hufeisen_bronze"), Is.EqualTo(DorfWirtschaft.KeinPferd));

            Assert.That(DorfWirtschaft.MoebelKaufen(reich, "table"), Is.EqualTo((DorfWirtschaft.Ok, 150)));
            Assert.That(Gooby.Core.Home.Lieferung.Stueck(reich), Is.EqualTo(1), "the van brings it home");
            Gooby.Core.Home.Lieferung.Einlagern(reich);
            Assert.That(Gooby.Core.Home.StorageLogic.CountOf((JArray)reich["home"]["storage"], "table"), Is.EqualTo(1));
            Assert.That(DorfWirtschaft.MoebelKaufen(reich, "bedDouble").code, Is.EqualTo(DorfWirtschaft.Unbekannt), "only the barn's six pieces");
        }

        [Test]
        public void OatsAndTreatsAreFedAtTheFarm()
        {
            var s = Ranch(100);
            Assert.That(RanchPflege.Pflegen(s, "tier_pferd_beere", RanchPflege.Hafer, Mittag, Tag).Code, Is.EqualTo(RanchWirtschaft.LagerLeer));
            DorfWirtschaft.FutterKaufen(s, "hafer");
            DorfWirtschaft.FutterKaufen(s, "leckerli");
            var pferd = RanchHof.Pferd(s, "tier_pferd_beere");
            pferd["werte"]["hunger"] = 40.0;
            var hafer = RanchPflege.Pflegen(s, "tier_pferd_beere", RanchPflege.Hafer, Mittag, Tag);
            Assert.That(hafer.Code, Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That((double)hafer.Werte["hunger"], Is.EqualTo(80.0).Within(1e-9), "oats fill 40");
            Assert.That(hafer.Gewinn, Is.EqualTo(2.0).Within(1e-9));
            var leckerli = RanchPflege.Pflegen(s, "tier_pferd_beere", RanchPflege.Leckerli, Mittag, Tag);
            Assert.That(leckerli.Gewinn, Is.EqualTo(3.0).Within(1e-9), "a treat bonds like brushing");
            Assert.That(RanchDorf.Futter(s, "hafer") + RanchDorf.Futter(s, "leckerli"), Is.EqualTo(0));
        }
    }
}
