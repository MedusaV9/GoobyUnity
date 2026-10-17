using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The place dialog trees (city/dialoge) and the city flags they leave behind.</summary>
    public class DialogRunnerTests
    {
        private const long NowMs = 1_768_478_400_000L;

        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            return store;
        }

        [Test]
        public void CondsAndEffectsParseLikeTheGodotRunner()
        {
            var flags = new Dictionary<string, bool> { { "rezept_tropfen", true } };
            Assert.That(DialogRunner.CondOk("", flags), Is.True);
            Assert.That(DialogRunner.CondOk("flag:rezept_tropfen", flags), Is.True);
            Assert.That(DialogRunner.CondOk("!flag:rezept_tropfen", flags), Is.False);
            Assert.That(DialogRunner.CondOk("flag:gesund", flags), Is.False);
            Assert.That(DialogRunner.CondOk("!flag:gesund", flags), Is.True);
            Assert.That(DialogRunner.CondOk("mond:voll", flags), Is.False, "unknown conds never pass");

            var set = DialogRunner.EffektParsen("flag:vet_cure");
            Assert.That((set.Typ, set.Name, set.Wert), Is.EqualTo(("flag", "vet_cure", true)));
            var clear = DialogRunner.EffektParsen("flag_weg:rezept_tropfen");
            Assert.That((clear.Typ, clear.Name, clear.Wert), Is.EqualTo(("flag", "rezept_tropfen", false)));
            var item = DialogRunner.EffektParsen("item:medicine");
            Assert.That((item.Typ, item.Name), Is.EqualTo(("item", "medicine")));
            Assert.That(DialogRunner.EffektParsen("laden").Typ, Is.EqualTo("laden"));
            Assert.That(DialogRunner.EffektParsen("tanzen"), Is.Null);
        }

        [Test]
        public void EveryPlaceTreeLoadsInBothLanguagesWithTheSameNodes()
        {
            foreach (var id in new[] { "tierarzt", "goobytheke", "rehwei" })
            {
                var de = DialogRunner.Laden(id);
                var en = DialogRunner.Laden(id, "en");
                Assert.That(de, Is.Not.Null, id + " (de)");
                Assert.That(en, Is.Not.Null, id + " (en)");
                Assert.That(ReferenceEquals(de, en), Is.False, id + ": the English tree is its own file");
                var deNodes = ((JObject)de["nodes"]).Properties().Select(p => p.Name).OrderBy(n => n).ToList();
                var enNodes = ((JObject)en["nodes"]).Properties().Select(p => p.Name).OrderBy(n => n).ToList();
                Assert.That(enNodes, Is.EqualTo(deNodes), id + ": node ids match across languages");
                Assert.That(new DialogRunner(de).IstGeladen, Is.True);
                Assert.That(new DialogRunner(DialogRunner.Laden(id, "fr")).IstGeladen, Is.True, id + ": unknown locale falls back to German");
            }
            Assert.That(DialogRunner.Laden("kiosk"), Is.Null, "no tree for an unknown place");
        }

        [Test]
        public void TheVetTreeBranchesOnHealthAndMirrorsFlagEffects()
        {
            var healthy = new DialogRunner(DialogRunner.Laden("tierarzt"), new Dictionary<string, bool> { { "gesund", true } });
            Assert.That(healthy.Aktuell, Is.EqualTo("empfang"));
            Assert.That(healthy.Sprecher, Is.EqualTo("moehrchen"));
            Assert.That(healthy.Text().Count, Is.EqualTo(2), "two lines of welcome");
            var options = healthy.Optionen().Select(o => o.Next).ToList();
            Assert.That(options, Is.EqualTo(new[] { "gesund_lob", "checkup_frage", "vorstellung" }), "sick options hidden for a healthy Gooby");
            Assert.That(healthy.IstEnde, Is.False);

            var sick = new DialogRunner(DialogRunner.Laden("tierarzt"), new Dictionary<string, bool> { { "schwer_krank", true } });
            Assert.That(sick.Optionen()[0].Next, Is.EqualTo("untersuchung"));
            Assert.That(sick.Waehlen(0), Is.True);
            Assert.That(sick.Aktuell, Is.EqualTo("untersuchung"));
            Assert.That(sick.Effekte().Select(e => e.Name), Is.EqualTo(new[] { "vet_untersuchung" }));
            Assert.That(sick.Flags.ContainsKey("vet_untersuchung"), Is.True, "flag effects of the entered node are mirrored");
            Assert.That(sick.Optionen(), Is.Empty);
            Assert.That(sick.AutoNext, Is.EqualTo("diagnose"));
            Assert.That(sick.Weiter(), Is.True);
            var diagnose = sick.Optionen().Select(o => o.Next).ToList();
            Assert.That(diagnose, Is.EqualTo(new[] { "behandlung", "rezept", "ruhe" }), "without a prescription the 'already have one' branch is hidden");
            Assert.That(sick.Waehlen(1), Is.True);
            Assert.That(sick.Aktuell, Is.EqualTo("rezept"));
            Assert.That(sick.Effekte().Select(e => e.Name), Is.EqualTo(new[] { "rezept_tropfen", "vet_rezept" }));
            Assert.That(sick.Flags["rezept_tropfen"], Is.True);
            Assert.That(sick.Weiter(), Is.True);
            Assert.That(sick.Aktuell, Is.EqualTo("nachsorge"));
            Assert.That(sick.IstEnde, Is.True);
            Assert.That(sick.Weiter(), Is.False, "nothing after the end node");
            Assert.That(sick.Waehlen(0), Is.False, "no options at the end");
        }

        [Test]
        public void ThePharmacyRedeemsThePrescriptionForMedicine()
        {
            var without = new DialogRunner(DialogRunner.Laden("goobytheke"));
            Assert.That(without.Optionen().Select(o => o.Next), Is.EqualTo(new[] { "ratschlag", "laden" }), "no prescription → no redeem option");
            Assert.That(without.Waehlen(1), Is.True);
            Assert.That(without.Effekte().Single().Typ, Is.EqualTo("laden"));
            Assert.That(without.IstEnde, Is.True);

            var with = new DialogRunner(DialogRunner.Laden("goobytheke"), new Dictionary<string, bool> { { "rezept_tropfen", true } });
            Assert.That(with.Optionen()[0].Next, Is.EqualTo("rezept_einloesen"));
            Assert.That(with.Waehlen(0), Is.True);
            var effects = with.Effekte();
            Assert.That(effects.Select(e => (e.Typ, e.Name, e.Wert)), Is.EqualTo(new[] { ("flag", "rezept_tropfen", false), ("item", "medicine", true) }));
            Assert.That(with.Flags.ContainsKey("rezept_tropfen"), Is.False, "the prescription is used up");
            Assert.That(with.IstEnde, Is.True);
        }

        [Test]
        public void CityFlagsAndItemsLandInTheSave()
        {
            var gs = Store();
            Assert.That(CityState.Flags(gs), Is.Empty);
            CityState.SetFlag(gs, "rezept_tropfen", true);
            Assert.That(CityState.HasFlag(gs, "rezept_tropfen"), Is.True);
            Assert.That(ShopLogic.HatRezept(gs), Is.True, "the shop reads the same flag");
            Assert.That(CityState.Flags(gs), Is.EqualTo(new Dictionary<string, bool> { { "rezept_tropfen", true } }));
            CityState.SetFlag(gs, "rezept_tropfen", false);
            Assert.That(CityState.HasFlag(gs, "rezept_tropfen"), Is.False);
            Assert.That(CityState.IsMarker("vet_cure"), Is.True);
            Assert.That(CityState.IsMarker("rezept_tropfen"), Is.False);

            Assert.That(CityState.GiveItem(gs, "medicine"), Is.EqualTo(1));
            Assert.That(CityState.GiveItem(gs, "medicine"), Is.EqualTo(2));
            Assert.That(J.Int(J.Path(gs.State, "inventory", "items", "medicine")), Is.EqualTo(2));
        }

        [Test]
        public void MedicineAndTheVetHealAQueasyGooby()
        {
            var gs = Store();
            gs.Update(s => J.EnsureDict(J.EnsureDict(s, "gooby"), "health")["state"] = "sick");
            Assert.That(Health.Grade(J.Path(gs.State, "gooby", "health")), Is.EqualTo(2));
            var none = new Health.CareResult();
            gs.Update(s => none = Health.UseMedicineState(s, NowMs));
            Assert.That((none.Ok, none.Reason), Is.EqualTo((false, "none")), "no medicine in the bag yet");
            CityState.GiveItem(gs, "medicine");
            var dose = new Health.CareResult();
            gs.Update(s => dose = Health.UseMedicineState(s, NowMs));
            Assert.That(dose.Ok, Is.True);
            Assert.That(Health.Grade(J.Path(gs.State, "gooby", "health")), Is.EqualTo(1), "sick → queasy");
            Assert.That(J.Int(J.Path(gs.State, "inventory", "items", "medicine")), Is.EqualTo(0));

            var poor = new Health.CareResult();
            gs.Update(s => poor = Health.PayVet(s, "cure", NowMs));
            Assert.That((poor.Ok, poor.Reason), Is.EqualTo((false, "coins")), "a fresh save's 110 coins do not cover the 120-coin cure");
            gs.Update(s => s["economy"]["coins"] = 1000);
            var coins = J.Int(J.Path(gs.State, "economy", "coins"));
            var cure = new Health.CareResult();
            gs.Update(s => cure = Health.PayVet(s, "cure", NowMs));
            Assert.That(cure.Ok, Is.True);
            Assert.That(J.Int(J.Path(gs.State, "economy", "coins")), Is.EqualTo(coins - Health.VetCurePrice));
            Assert.That(Health.Grade(J.Path(gs.State, "gooby", "health")), Is.EqualTo(0), "the vet heals completely");
            var again = new Health.CareResult();
            gs.Update(s => again = Health.PayVet(s, "cure", NowMs));
            Assert.That((again.Ok, again.Reason), Is.EqualTo((false, "healthy")), "a healthy Gooby is not treated");
        }
    }
}
