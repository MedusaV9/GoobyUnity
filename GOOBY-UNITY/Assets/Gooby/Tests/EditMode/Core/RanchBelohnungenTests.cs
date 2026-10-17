using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The ranch quests' unlocks as real things (<see cref="RanchBelohnungen"/>): reward decoration only from the build
    /// store, quest gear in the cupboard, looks on the horse, the ribbon wall, plus the saddle room's outfit sets.
    /// </summary>
    public class RanchBelohnungenTests
    {
        private const long T0 = 1_790_000_000_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            RanchQuestKatalog.ResetCache();
            RanchBauKatalog.ResetCache();
        }

        private static JObject Hof()
        {
            var s = new JObject
            {
                ["economy"] = new JObject { ["coins"] = 3000 },
                ["progression"] = new JObject { ["level"] = 20 },
            };
            Assert.That(RanchKauf.Kaufe(s, T0), Is.EqualTo(RanchKauf.Ok));
            return s;
        }

        private static int Coins(JObject s) => J.Int(s["economy"]["coins"]);

        private static void Frei(JObject s, params string[] ids)
        {
            var slice = RanchQuests.Slice(s);
            foreach (var id in ids) ((JArray)slice["freigeschaltet"]).Add(id);
        }

        [Test]
        public void EveryUnlockOfThePackBecomesAThing()
        {
            var unlocks = RanchQuestKatalog.Alle.SelectMany(q => q.Freischalten).Distinct().ToList();
            Assert.That(unlocks.Count, Is.EqualTo(8));
            foreach (var id in unlocks)
            {
                var def = RanchBelohnungen.Fuer(id);
                Assert.That(def, Is.Not.Null, id);
                Assert.That(RanchBelohnungen.Quelle(id), Is.Not.Null, id + " has its quest");
                if (def.Art == RanchBelohnungen.Deko)
                {
                    var bau = RanchBauKatalog.Def(null, def.Ziel);
                    Assert.That(bau?.Kategorie, Is.EqualTo("deko"), id);
                    Assert.That(bau.Belohnung, Is.True, id + " is not for sale");
                    Assert.That(RanchDorf.RanchDeko, Has.No.Member(def.Ziel), id + " is not on the barn's shelf");
                }
                if (def.Art == RanchBelohnungen.Gear)
                {
                    var teile = def.Ziel.Split('_');
                    Assert.That(RanchWirtschaft.IstSonder(teile[0], teile[1]), Is.True, id);
                }
            }
            Assert.That(RanchBauKatalog.Belohnungen(null), Is.EquivalentTo(new[] { "deko_mistgabel_gold", "deko_schleifenwand", "girlande_ranchfest" }));
        }

        [Test]
        public void HandingInPutsTheSaddleIntoTheCupboardOnce()
        {
            var s = Hof();
            s["ranch"]["quests"] = new JObject { ["erledigt"] = new JArray("haupt_01", "haupt_02", "haupt_03") };
            RanchQuests.Annehmen(s, "haupt_04", T0);
            RanchQuests.Melde(s, "sprich_mit", "selma", T0);
            RanchQuests.Melde(s, "sammle", "wolle", T0, 2);
            RanchQuests.Tick(s, T0 + 480 * 60_000L);
            RanchQuests.Melde(s, "sprich_mit", "eisenhuf", T0);
            var b = RanchQuests.Abgeben(s, "haupt_04", T0);
            Assert.That(b.Geliefert, Is.EqualTo(new[] { "sattel_kartoffel" }));
            Assert.That(RanchWirtschaft.GearBesitzt(s, "sattel_kartoffel"), Is.True);
            Assert.That(RanchWirtschaft.GearFarbenFuer(s, "sattel").Last(), Is.EqualTo("kartoffel"), "its chip follows the five colours");
            Assert.That(RanchWirtschaft.GearFarbenFuer(s, "decke"), Has.No.Member("schleife"), "not earned yet");
            var coins = Coins(s);
            Assert.That(RanchWirtschaft.GearKaufen(s, "sattel_kartoffel"), Is.EqualTo(RanchWirtschaft.Unbekannt), "no shop sells it");
            Assert.That(Coins(s), Is.EqualTo(coins));
            var pferd = RanchHof.PferdIds(s)[0];
            Assert.That(RanchWirtschaft.GearAnlegen(s, pferd, "sattel", "kartoffel"), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.GearAn(s, pferd, "sattel"), Is.EqualTo("kartoffel"));
            Assert.That(RanchBelohnungen.Liefern(s), Is.Empty, "delivered once");
            Assert.That(((JArray)s["ranch"]["wirtschaft"]["gear"]["owned"]).Count(t => J.Str(t) == "sattel_kartoffel"), Is.EqualTo(1));
        }

        [Test]
        public void SavesFromBeforeTheRewardsGetThemOnce()
        {
            var s = Hof();
            Frei(s, "deko_mistgabel_gold", "schabracke_schleife", "fellmuster_nebel", "unbekannt_aus_einem_pack");
            Assert.That(RanchBelohnungen.Liefern(s), Is.EqualTo(new[] { "deko_mistgabel_gold", "schabracke_schleife", "fellmuster_nebel" }));
            Assert.That(RanchBauState.LagerAnzahl(RanchBauState.Lese(s), "deko_mistgabel_gold"), Is.EqualTo(1));
            Assert.That(RanchWirtschaft.GearBesitzt(s, "decke_schleife"), Is.True);
            Assert.That(RanchBelohnungen.Liefern(s), Is.Empty);
            Assert.That(RanchBauState.LagerAnzahl(RanchBauState.Lese(s), "deko_mistgabel_gold"), Is.EqualTo(1), "no second golden pitchfork");
        }

        [Test]
        public void RewardDecorationComesOnlyFromTheStoreAndGoesBackIntoIt()
        {
            var s = Hof();
            var coins = Coins(s);
            var nein = RanchBauState.Platziere(s, "deko_schleifenwand", new Zelle(4, 8), 0);
            Assert.That(nein.Ok, Is.False);
            Assert.That(nein.Fehler, Is.EqualTo(RanchBauState.FehlerNurBelohnung));
            Frei(s, "deko_schleifenwand");
            RanchBelohnungen.Liefern(s);
            var ja = RanchBauState.Platziere(s, "deko_schleifenwand", new Zelle(4, 8), 1);
            Assert.That(ja.Ok, Is.True);
            Assert.That(ja.Kosten, Is.EqualTo(0));
            Assert.That(Coins(s), Is.EqualTo(coins));
            Assert.That(RanchBauState.LagerAnzahl(RanchBauState.Lese(s), "deko_schleifenwand"), Is.EqualTo(0));
            Assert.That(RanchBauEffekte.DekoStilpunkte(RanchBauState.Lese(s), null), Is.EqualTo(1), "a reward counts as five pieces");
            Assert.That(RanchBauState.ErstattungFuer(RanchBauState.Lese(s), ja.Uid), Is.EqualTo(0));
            var weg = RanchBauState.Entferne(s, ja.Uid);
            Assert.That(weg.Ok && weg.InsLager, Is.True);
            Assert.That(weg.Erstattung, Is.EqualTo(0));
            Assert.That(Coins(s), Is.EqualTo(coins));
            Assert.That(RanchBauState.LagerAnzahl(RanchBauState.Lese(s), "deko_schleifenwand"), Is.EqualTo(1), "never lost");
            var normal = RanchBauState.Platziere(s, "laterne", new Zelle(5, 9), 0);
            Assert.That(normal.Ok, Is.True);
            Assert.That(RanchBauState.Entferne(s, normal.Uid).InsLager, Is.False, "bought decoration is pulled down for half its price");
        }

        [Test]
        public void LooksNeedTheirUnlockAndLeaveTheCoatAlone()
        {
            var s = Hof();
            var id = RanchHof.PferdIds(s)[0];
            var farbe = J.Str(RanchHof.Pferd(s, id)["farbe"]);
            Assert.That(RanchBelohnungen.SetzeLook(s, id, RanchBelohnungen.Nebel, true), Is.EqualTo(RanchBelohnungen.Gesperrt));
            Assert.That(RanchBelohnungen.SetzeLook(s, "gibtsnicht", RanchBelohnungen.Nebel, true), Is.EqualTo(RanchBelohnungen.Unbekannt));
            Frei(s, "fellmuster_nebel", "fohlen_flauschig");
            Assert.That(RanchBelohnungen.SetzeLook(s, id, RanchBelohnungen.Nebel, true), Is.EqualTo(RanchBelohnungen.Ok));
            Assert.That(RanchBelohnungen.SetzeLook(s, id, RanchBelohnungen.Flausch, true), Is.EqualTo(RanchBelohnungen.Ok));
            var pferd = RanchHof.Pferd(s, id);
            Assert.That(RanchBelohnungen.FellId(pferd), Is.EqualTo(RanchBelohnungen.Nebel));
            Assert.That(RanchBelohnungen.TraegtLook(pferd, RanchBelohnungen.Flausch), Is.True);
            Assert.That(J.Str(pferd["farbe"]), Is.EqualTo(farbe), "the genes keep the real coat");
            RanchHof.Slice(s);
            Assert.That(RanchBelohnungen.TraegtLook(RanchHof.Pferd(s, id), RanchBelohnungen.Nebel), Is.True, "the look survives healing");
            RanchBelohnungen.SetzeLook(s, id, RanchBelohnungen.Nebel, false);
            RanchBelohnungen.SetzeLook(s, id, RanchBelohnungen.Flausch, false);
            Assert.That(RanchHof.Pferd(s, id).ContainsKey("look"), Is.False);
            Assert.That(RanchBelohnungen.FellId(RanchHof.Pferd(s, id)), Is.EqualTo(farbe));
        }

        [Test]
        public void QuestGearShinesAtTheShow()
        {
            var pferd = new JObject { ["werte"] = new JObject { ["hunger"] = 80, ["durst"] = 80, ["sauberkeit"] = 80 }, ["bindung"] = 50 };
            var (_, normal) = RcompRichterSchau.BasisAusPferd(pferd, new JObject { ["sattel"] = "rot" });
            var (_, quest) = RcompRichterSchau.BasisAusPferd(pferd, new JObject { ["sattel"] = "kartoffel" });
            var (_, nichts) = RcompRichterSchau.BasisAusPferd(pferd, new JObject { ["sattel"] = null, ["decke"] = "" });
            Assert.That(normal, Is.EqualTo(7.0));
            Assert.That(quest, Is.EqualTo(11.0), "a quest piece counts as epic");
            Assert.That(nichts, Is.EqualTo(0.0));
        }

        [Test]
        public void OutfitSetsLiveInTheSaddleRoom()
        {
            var s = Hof();
            var ids = RanchHof.PferdIds(s);
            var (a, b) = (ids[0], ids[1]);
            RanchWirtschaft.GearKaufen(s, "sattel_rot");
            RanchWirtschaft.GearKaufen(s, "decke_blau");
            RanchWirtschaft.GearAnlegen(s, a, "sattel", "rot");
            RanchWirtschaft.GearAnlegen(s, a, "decke", "blau");
            Assert.That(RanchBauEffekte.OutfitSets(s), Is.EqualTo(0));
            Assert.That(RanchWirtschaft.OutfitSpeichern(s, 0, a), Is.EqualTo(RanchWirtschaft.Gesperrt), "no saddle room, no sets");
            ((JObject)RanchBauState.Im(s)["anlagen"])["sattelkammer"] = new JObject { ["stufe"] = 1 };
            Assert.That(RanchBauEffekte.OutfitSets(s), Is.EqualTo(1));
            Assert.That(RanchWirtschaft.OutfitAnlegen(s, 0, b), Is.EqualTo(RanchWirtschaft.Leer));
            Assert.That(RanchWirtschaft.OutfitSpeichern(s, 0, a), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.OutfitSpeichern(s, 1, a), Is.EqualTo(RanchWirtschaft.Gesperrt), "one set on level 1");
            RanchWirtschaft.GearAnlegen(s, b, "halfter", null);
            Assert.That(RanchWirtschaft.OutfitAnlegen(s, 0, b), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.GearAn(s, b, "sattel"), Is.EqualTo("rot"));
            Assert.That(RanchWirtschaft.GearAn(s, b, "decke"), Is.EqualTo("blau"));
            Assert.That(RanchWirtschaft.GearAn(s, b, "halfter"), Is.Null);
            ((JObject)RanchBauState.Im(s)["anlagen"])["sattelkammer"] = new JObject { ["stufe"] = 2 };
            Assert.That(RanchBauEffekte.OutfitSets(s), Is.EqualTo(3), "extra places from level 2");
            ((JArray)s["ranch"]["wirtschaft"]["gear"]["owned"]).Remove(((JArray)s["ranch"]["wirtschaft"]["gear"]["owned"]).First(t => J.Str(t) == "decke_blau"));
            RanchHof.Slice(s);
            Assert.That(J.Str(RanchWirtschaft.OutfitSet(s, 0)["sattel"]), Is.EqualTo("rot"), "sets survive healing");
            RanchWirtschaft.OutfitAnlegen(s, 0, a);
            Assert.That(RanchWirtschaft.GearAn(s, a, "decke"), Is.Null, "a piece no longer owned stays off");
        }

        [Test]
        public void TheRibbonWallShowsTheFarmsRibbonsBestFirst()
        {
            var s = Hof();
            s["ranch"]["comp"] = new JObject { ["schleifen"] = new JObject { ["springen_holz"] = 3, ["rennen_holz"] = 1, ["schau_bronze"] = 2, ["kaputt"] = 7 } };
            Assert.That(RanchBelohnungen.Wandschleifen(s), Is.EqualTo(new[] { 1, 2, 3 }));
            Frei(s, "schleife_bronze");
            Assert.That(RanchBelohnungen.Wandschleifen(s), Is.EqualTo(new[] { 1, 2, 3, 0 }), "the quest's bronze ribbon hangs last, with its star");
        }
    }
}
