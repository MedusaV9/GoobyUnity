using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The whole town: every place of the plan on the street, every dialog tree walkable, every counter's rules.</summary>
    public class CityPlacesTests
    {
        private const long NowMs = 1_768_478_400_000L; // Thu 2026-01-15 12:00 UTC

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            CityMap.Reset();
        }

        private static GameStateStore Store(int coins = 5000)
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            store.Update(s => { J.EnsureDict(s, "economy")["coins"] = coins; });
            return store;
        }

        [Test]
        public void EveryPlaceOfTheTownPlanIsOnTheStreetExactlyOnce()
        {
            var plan = CityMap.Orte;
            Assert.That(plan.Count, Is.EqualTo(12), "city_map.json lists twelve places");
            var street = DriveModel.Places.Where(p => p.Side < 0).ToList();
            Assert.That(street.Select(p => p.MapId).OrderBy(x => x), Is.EqualTo(plan.Select(o => o.Id).OrderBy(x => x)), "the lap covers the plan, nothing twice");
            Assert.That(DriveModel.Places.Count(p => p.Id == "home"), Is.EqualTo(1));
            foreach (var p in street)
            {
                var ort = CityMap.Get(p.MapId);
                Assert.That(p.Typ, Is.EqualTo(ort.Typ), p.Id + " keeps the plan's kind");
                Assert.That(p.Tint, Is.EqualTo(ort.Tint), p.Id + " wears the plan's facade tint");
                Assert.That(p.Awning, Is.EqualTo(ort.Awning), p.Id + " awning as planned");
                Assert.That(p.NameKey, Is.Not.Empty);
                Assert.That(p.Dialog != null || p.HasCounter, Is.True, p.Id + " has something to do");
            }
            Assert.That(DriveModel.PlaceById("wochenmarkt").Typ, Is.EqualTo("markt"));
            Assert.That(DriveModel.PlaceById("mart").Tint, Is.EqualTo("#E8524A"), "REHWEI's red front from the plan");
            Assert.That(DriveModel.PlaceById("funkelpark").Counter, Is.EqualTo(DriveModel.CounterPark));
            Assert.That(DriveModel.PlaceById("flughafen").Counter, Is.EqualTo(DriveModel.CounterTravel));
        }

        [Test]
        public void TheMarketOpensOnSaturdayMornings()
        {
            var markt = CityMap.Get("wochenmarkt");
            Assert.That(markt.Oeffnung, Is.Not.Null);
            Assert.That(CityMap.IstOffen(markt, CityMap.Samstag, 10.5), Is.True);
            Assert.That(CityMap.IstOffen(markt, CityMap.Samstag, 7.9), Is.False, "not before eight");
            Assert.That(CityMap.IstOffen(markt, CityMap.Samstag, 14.0), Is.False, "closes at two");
            Assert.That(CityMap.IstOffen(markt, 1, 10.0), Is.False, "Monday: only the sign");
            Assert.That(CityMap.IstOffen(CityMap.Get("rehwei"), 1, 3.0), Is.True, "places without a rule are always open");
            Assert.That(CityMap.GeschlossenKey(markt), Is.EqualTo("city.ort.nur_samstag"));
            Assert.That(CityMap.GeschlossenKey(CityMap.Get("post")), Is.EqualTo("city.ort.geschlossen"));
            Assert.That(CityMap.TageBisSamstag(1), Is.EqualTo(5));
            Assert.That(CityMap.TageBisSamstag(6), Is.EqualTo(0));
            var (wochentag, stunde) = CityMap.LokaleZeit(NowMs, 60); // Thursday 13:00 in Berlin
            Assert.That(wochentag, Is.EqualTo(4));
            Assert.That(stunde, Is.EqualTo(13.0).Within(1e-9));
        }

        [Test]
        public void EveryDialogTreeWalksToAnEndWithKnownEffects()
        {
            var withTree = DriveModel.Places.Where(p => p.Dialog != null).ToList();
            Assert.That(withTree.Select(p => p.Dialog), Is.EquivalentTo(new[] { "rehwei", "goobyman", "tierarzt", "gouhbus", "goobytheke", "post", "pow", "baumarkt", "autohaus", "wochenmarkt" }));
            foreach (var place in withTree)
            foreach (var locale in new[] { "de", "en" })
            {
                var tree = DialogRunner.Laden(place.Dialog, locale);
                Assert.That(tree, Is.Not.Null, place.Dialog + " (" + locale + ")");
                var nodes = (JObject)tree["nodes"];
                var start = J.Str(tree["start"]);
                Assert.That(nodes[start], Is.Not.Null, place.Dialog + ": start node exists");
                var seenLaden = false;
                foreach (var prop in nodes.Properties())
                {
                    var node = (JObject)prop.Value;
                    Assert.That(J.Str(node["sprecher"]), Is.Not.Empty, place.Dialog + "/" + prop.Name + " has a speaker");
                    var next = J.Str(node["next"]);
                    if (next.Length > 0) Assert.That(nodes[next], Is.Not.Null, place.Dialog + "/" + prop.Name + " → " + next);
                    foreach (var e in node["effekt"] as JArray ?? new JArray())
                    {
                        var parsed = DialogRunner.EffektParsen(J.Str(e));
                        Assert.That(parsed, Is.Not.Null, place.Dialog + "/" + prop.Name + ": effect " + e + " is known");
                        seenLaden |= parsed.Typ == "laden";
                    }
                    foreach (var o in node["optionen"] as JArray ?? new JArray())
                    {
                        Assert.That(nodes[J.Str(o["next"])], Is.Not.Null, place.Dialog + "/" + prop.Name + ": option → " + o["next"]);
                        var cond = J.Str(o["cond"]);
                        if (cond.Length > 0) Assert.That(cond.StartsWith("flag:") || cond.StartsWith("!flag:"), Is.True, place.Dialog + ": cond " + cond);
                    }
                }
                // every reachable node ends or leads on; the counter is reachable where the place has one
                var runner = new DialogRunner(tree, new Dictionary<string, bool> { { "krank", true } });
                var visited = new HashSet<string>();
                var frontier = new Stack<string> { };
                frontier.Push(start);
                while (frontier.Count > 0)
                {
                    var id = frontier.Pop();
                    if (!visited.Add(id)) continue;
                    var node = (JObject)nodes[id];
                    var next = J.Str(node["next"]);
                    var options = node["optionen"] as JArray ?? new JArray();
                    Assert.That(J.IsTrue(node["ende"]) || next.Length > 0 || options.Count > 0, Is.True, place.Dialog + "/" + id + " is a dead end without ende:true");
                    if (next.Length > 0) frontier.Push(next);
                    foreach (var o in options) frontier.Push(J.Str(o["next"]));
                }
                Assert.That(visited.Count, Is.GreaterThanOrEqualTo(3), place.Dialog + ": the start reaches the tree");
                Assert.That(visited.Any(id => J.IsTrue(nodes[id]["ende"])), Is.True, place.Dialog + ": an end node is reachable");
                Assert.That(runner.IstGeladen, Is.True);
                if (place.HasCounter && place.Counter != DriveModel.CounterPark && place.Counter != DriveModel.CounterTravel)
                    Assert.That(seenLaden, Is.True, place.Dialog + " opens its counter somewhere in the tree");
            }
        }

        [Test]
        public void PowOffersAreDeterministicPerDayAndTheCameraSellsOnce()
        {
            var seed = PowLogic.TagSeed(NowMs, 60);
            Assert.That(seed, Is.EqualTo(20260115));
            var a = PowLogic.Angebote(seed);
            var b = PowLogic.Angebote(seed);
            Assert.That(a.Count, Is.EqualTo(3));
            Assert.That(a.Select(x => x.Ware.Id), Is.EqualTo(b.Select(x => x.Ware.Id)), "same day → same toys");
            Assert.That(a.Select(x => x.Ware.Id).Distinct().Count(), Is.EqualTo(3), "pairwise different");
            Assert.That(a.Select(x => x.Rabatt), Is.EqualTo(new[] { 20, 30, 50 }));
            foreach (var x in a) Assert.That(x.PreisNeu, Is.EqualTo(PowLogic.PreisMitRabatt(x.Ware.Preis, x.Rabatt)).And.GreaterThanOrEqualTo(1));
            Assert.That(PowLogic.Angebote(seed + 1).Select(x => x.Ware.Id), Is.Not.EqualTo(a.Select(x => x.Ware.Id)), "tomorrow is different");
            Assert.That(PowLogic.PreisMitRabatt(15, 50), Is.EqualTo(8), "rounded half up");
            Assert.That(PowLogic.RestSekundenBisWechsel(NowMs, 60), Is.EqualTo(11 * 3600), "13:00 local → eleven hours to midnight");

            var store = Store();
            var coins = ShopLogic.Coins(store);
            Assert.That(PowLogic.KaufeAngebot(store, a[0]), Is.True);
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(coins - a[0].PreisNeu));
            var moebel = PowLogic.MoebelVon(a[0].Ware.Id);
            if (moebel != null) Assert.That(HomeState.Storage(store).Any(r => J.Str(r["item"]) == moebel), Is.True, "deco goes into the build storage");
            else Assert.That(J.Int(J.Path(store.State, "inventory", "items", a[0].Ware.Inventar)), Is.EqualTo(1));

            Assert.That(PowLogic.HatKamera(store), Is.False);
            Assert.That(PowLogic.Kamera().Preis, Is.EqualTo(250));
            Assert.That(PowLogic.KaufeKamera(store), Is.True);
            Assert.That(PowLogic.HatKamera(store), Is.True);
            Assert.That(CityState.HasFlag(store, PowLogic.FlagKamera), Is.True, "Bäm's tree branches on the flag");
            Assert.That(PowLogic.KaufeKamera(store), Is.False, "sold once");
            Assert.That(ShopLogic.Ausverkauft(store, PowLogic.Kamera()), Is.True);
            var broke = Store(coins: 3);
            Assert.That(PowLogic.KaufeAngebot(broke, a[0]), Is.False);
        }

        [Test]
        public void TheMarketPaysTheBonusAndPushesPricesDown()
        {
            Assert.That(MarktLogic.Marktpreis("radish"), Is.EqualTo(7), "6 + 15 % rounded");
            Assert.That(MarktLogic.Marktpreis("salad"), Is.EqualTo(12));
            Assert.That(MarktLogic.Marktpreis("gold"), Is.EqualTo(0), "unknown crop");
            Assert.That(MarktLogic.Stueckpreis("salad", 0), Is.EqualTo(12));
            Assert.That(MarktLogic.Stueckpreis("salad", 4), Is.EqualTo(10), "four sold → 80 %");
            Assert.That(MarktLogic.Stueckpreis("salad", 40), Is.EqualTo(6), "floor at half");
            Assert.That(MarktLogic.Erloes("salad", 3, 0), Is.EqualTo(12 + 11 + 11));

            var heute = MarktLogic.TagesKey(NowMs, 60);
            Assert.That(heute, Is.EqualTo("2026-01-15"));
            var store = Store(coins: 0);
            store.Update(s => J.EnsureDict(s, "inventory")["food"] = new JObject { ["salad"] = 5, ["cake"] = 2 }); // cake is no harvest
            var angebot = MarktLogic.AngebotDesSpielers(store, heute);
            Assert.That(angebot.Select(a => a.Sorte.Id), Is.EqualTo(new[] { "salad" }), "only harvest in the basket is offered");
            Assert.That(angebot[0].Vorrat, Is.EqualTo(5));
            Assert.That(angebot[0].Preis, Is.EqualTo(12));

            var (ok, menge, erloes) = MarktLogic.Verkaufen(store, heute, "salad", 3);
            Assert.That((ok, menge, erloes), Is.EqualTo((true, 3, 34)));
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(34));
            Assert.That(J.Int(J.Path(store.State, "inventory", "food", "salad")), Is.EqualTo(2));
            Assert.That(MarktLogic.HeuteVerkauft(store, heute, "salad"), Is.EqualTo(3));
            Assert.That(J.Int(J.Path(store.State, "achievements", "counters", "sells")), Is.EqualTo(3), "marketDay sticker counter");
            Assert.That(MarktLogic.AngebotDesSpielers(store, heute)[0].Preis, Is.EqualTo(10), "the next unit is cheaper");
            var (ok2, menge2, _) = MarktLogic.Verkaufen(store, heute, "salad", 9);
            Assert.That((ok2, menge2), Is.EqualTo((true, 2)), "sells what is there");
            Assert.That(MarktLogic.Verkaufen(store, heute, "salad", 1).ok, Is.False, "basket empty");
            Assert.That(MarktLogic.HeuteVerkauft(store, "2026-01-16", "salad"), Is.EqualTo(0), "a new day resets the counter");
        }

        [Test]
        public void TheCarDealerSellsPaintsAndSwitchesCars()
        {
            var autos = AutoKatalog.Autos();
            Assert.That(autos.Select(a => a.Id), Is.EqualTo(new[] { "sedan", "hatchback-sports", "suv", "van" }));
            Assert.That(AutoKatalog.StartAutoId(), Is.EqualTo("sedan"));
            Assert.That(AutoKatalog.FarbName("#E8524A"), Is.EqualTo("Möhrenrot"));
            var store = Store(coins: 500);
            Assert.That(AutoKatalog.Besitz(store).Keys, Is.EqualTo(new[] { "sedan" }), "the start car is always in the driveway");
            var (aktiv, farbe) = AutoKatalog.AktivesAuto(store);
            Assert.That((aktiv.Id, farbe), Is.EqualTo(("sedan", "#E8524A")));
            Assert.That(AutoKatalog.KannKaufen(store, "van", "#F2C14E"), Is.False, "780 coins for the van");
            Assert.That(AutoKatalog.KannKaufen(store, "hatchback-sports", "#4FBF8B"), Is.False, "not offered in green");
            Assert.That(AutoKatalog.KannKaufen(store, "hatchback-sports", "#4A3B36"), Is.True);
            Assert.That(AutoKatalog.Kaufe(store, "hatchback-sports", "#4A3B36"), Is.True);
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(50));
            Assert.That(AutoKatalog.AktivesAuto(store).auto.Id, Is.EqualTo("hatchback-sports"), "drives off in the new car");
            Assert.That(AutoKatalog.AktivesAuto(store).farbe, Is.EqualTo("#4A3B36"));
            Assert.That(AutoKatalog.Kaufe(store, "hatchback-sports", "#E8524A"), Is.False, "already owned");
            Assert.That(AutoKatalog.Lackieren(store, "hatchback-sports", "#E8524A"), Is.True);
            Assert.That(AutoKatalog.AktivesAuto(store).farbe, Is.EqualTo("#E8524A"));
            Assert.That(AutoKatalog.Lackieren(store, "van", "#E8524A"), Is.False, "cannot paint what you do not own");
            Assert.That(AutoKatalog.Waehle(store, "sedan"), Is.True);
            Assert.That(AutoKatalog.AktivesAuto(store).auto.Id, Is.EqualTo("sedan"));
            Assert.That(AutoKatalog.TempoFaktor(3), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(AutoKatalog.TempoFaktor(6), Is.EqualTo(1.3).Within(1e-9));
            Assert.That(AutoKatalog.TempoFaktor(20), Is.EqualTo(1.7).Within(1e-9), "clamped");
        }

        [Test]
        public void TheAirportBooksAndPicksUpATrip()
        {
            Assert.That(Travel.Ziele.Length, Is.EqualTo(9));
            var info = Travel.Info("beach", 100);
            Assert.That((info.Preis, info.Tage, info.SouvenirCoins, info.KannZahlen, info.NameKey), Is.EqualTo((180, 3, 30, false, "travel.ziel.beach")));
            Assert.That(Travel.Info("moon", 100), Is.Null);

            var store = Store(coins: 200);
            Assert.That(Travel.IstWeg(store), Is.False);
            Assert.That(Travel.Buchen(store, "space", NowMs), Is.False, "350 coins for space");
            Assert.That(Travel.Buchen(store, "beach", NowMs), Is.True);
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(20));
            Assert.That(Travel.IstWeg(store), Is.True);
            Assert.That(Travel.Phase(store, NowMs), Is.EqualTo(Vacation.PhaseAway));
            Assert.That(Travel.RestTage(store, NowMs), Is.EqualTo(3));
            Assert.That(Travel.Buchen(store, "harbor", NowMs), Is.False, "one trip at a time");
            Assert.That(Travel.Abholen(store, NowMs + Vacation.MsPerDay).Ok, Is.False, "still away");

            var back = NowMs + 3 * Vacation.MsPerDay + 1000;
            Assert.That(Travel.Phase(store, back), Is.EqualTo(Vacation.PhaseReturnReady));
            var r = Travel.Abholen(store, back);
            Assert.That((r.Ok, r.ZielId, r.SouvenirCoins, r.Postkarten), Is.EqualTo((true, "beach", 30, 2)), "two full days away → two postcards");
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(50));
            Assert.That(Travel.IstWeg(store), Is.False);
            var v = Vacation.SliceOf(store.State);
            Assert.That(J.Int(v["trips"]), Is.EqualTo(1));
            Assert.That(J.IsTrue(v["visited"]["beach"]), Is.True, "stamped in the pass");
            Assert.That(Vacation.ErholungsBoostAktiv(v, back + 1000), Is.True, "48 h recovery boost");
            Assert.That(J.Str(v["heimkehrZiel"]), Is.EqualTo("beach"));
        }

        [Test]
        public void TheParkChargesRidesAndSellsSnacks()
        {
            var store = Store(coins: 100);
            store.Update(s => J.EnsureDict(J.EnsureDict(s, "gooby"), "stats")["energy"] = 60);
            var fun0 = J.Num(J.Path(store.State, "gooby", "stats", "fun"));
            ParkLogic.BesuchBuchen(store, nacht: false);
            Assert.That(J.Int(ParkLogic.SliceOf(store.State)["visits"]), Is.EqualTo(1));
            Assert.That(J.IsTrue(ParkLogic.SliceOf(store.State)["nightVisit"]), Is.False);

            var coaster = ParkLogic.RideById("coaster");
            Assert.That(ParkLogic.KannFahren(store, coaster), Is.True);
            Assert.That(ParkLogic.Fahren(store, coaster), Is.EqualTo((true, "")));
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(85));
            Assert.That(J.Num(J.Path(store.State, "gooby", "stats", "energy")), Is.EqualTo(58.0).Within(1e-9));
            Assert.That(J.Num(J.Path(store.State, "gooby", "stats", "fun")), Is.EqualTo(System.Math.Min(100.0, fun0 + 12.0)).Within(1e-9));
            Assert.That(ParkLogic.Fahrten(store, "coaster"), Is.EqualTo(1));
            Assert.That(ParkLogic.Fahren(store, ParkLogic.RideById("scooter")), Is.EqualTo((true, "")), "watching is free");
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(85));

            store.Update(s => J.EnsureDict(J.EnsureDict(s, "gooby"), "stats")["energy"] = 10);
            Assert.That(ParkLogic.Fahren(store, coaster), Is.EqualTo((false, "tired")));
            store.Update(s => { J.EnsureDict(J.EnsureDict(s, "gooby"), "stats")["energy"] = 60; s["economy"]["coins"] = 5; });
            Assert.That(ParkLogic.Fahren(store, coaster), Is.EqualTo((false, "coins")));

            store.Update(s => s["economy"]["coins"] = 40);
            Assert.That(ParkLogic.Naschen(store, ParkLogic.Stalls[0]), Is.True);
            Assert.That(J.Int(J.Path(store.State, "inventory", "food", "cottonCandy")), Is.EqualTo(1));
            Assert.That(J.Int(ParkLogic.SliceOf(store.State)["candyBought"]), Is.EqualTo(1));
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(28));
            Assert.That(ParkLogic.IstNacht(19.5), Is.True);
            Assert.That(ParkLogic.IstNacht(12.0), Is.False);
            ParkLogic.BesuchBuchen(store, nacht: true);
            Assert.That(J.IsTrue(ParkLogic.SliceOf(store.State)["nightVisit"]), Is.True, "night latches");
        }

        [Test]
        public void GoobymanAndBaumarktCountersFollowTheirRules()
        {
            var drogerie = ShopLogic.Waren(ShopLogic.GoobymanPath);
            Assert.That(drogerie.Select(w => w.Id), Does.Contain("schlafmaske").And.Contain("zahnbuerste_standard"));
            var maske = ShopLogic.Find(drogerie, "schlafmaske");
            Assert.That(maske.NurEinmal, Is.True, "the sleep mask is einmalig");
            var pflaster = ShopLogic.Find(drogerie, "goobyman_pflaster");
            Assert.That(pflaster.IsEffekt, Is.True);
            Assert.That(pflaster.IsFood, Is.False, "not a snack although it has no inventar key");
            var store = Store();
            Assert.That(ShopLogic.Kaufe(store, maske), Is.True);
            Assert.That(ShopLogic.Ausverkauft(store, maske), Is.True);
            Assert.That(ShopLogic.Kaufe(store, maske), Is.False, "one mask is enough");
            var buerste = ShopLogic.Find(drogerie, "zahnbuerste_standard");
            Assert.That(ShopLogic.Kaufe(store, buerste), Is.True);
            Assert.That(ShopLogic.Kaufe(store, buerste), Is.True, "brushes stack");
            Assert.That(ShopLogic.Vorrat(store, buerste), Is.EqualTo(2));
            store.Update(s => J.EnsureDict(J.EnsureDict(s, "gooby"), "health")["junkScore"] = 8.0);
            Assert.That(ShopLogic.Kaufe(store, pflaster), Is.True);
            Assert.That(J.Num(J.Path(store.State, "gooby", "health", "junkScore")), Is.EqualTo(3.0).Within(1e-9), "heals five pressure points on the spot");
            Assert.That(ShopLogic.PflasterHeuteVerbraucht(store), Is.True);
            Assert.That(ShopLogic.Kaufe(store, pflaster), Is.False, "one plaster a day");

            var material = ShopLogic.Materialien();
            var bretter = ShopLogic.Find(material, "bretter");
            Assert.That((bretter.Menge, bretter.Preis, bretter.Kategorie), Is.EqualTo((5, 30, "material")));
            var coins = ShopLogic.Coins(store);
            Assert.That(ShopLogic.Kaufe(store, bretter), Is.True);
            Assert.That(J.Int(J.Path(store.State, "inventory", "items", "bretter")), Is.EqualTo(5), "a pack of five boards");
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(coins - 30));
            var plaene = ShopLogic.Bauplaene();
            Assert.That(plaene.Count, Is.EqualTo(7));
            var werkbank = ShopLogic.Find(plaene, "bauplan_werkbank");
            Assert.That(werkbank.NurEinmal, Is.True);
            Assert.That(ShopLogic.Kaufe(store, werkbank), Is.True);
            Assert.That(ShopLogic.Kaufe(store, werkbank), Is.False, "blueprints are bought once");
            Assert.That(J.Int(J.Path(store.State, "inventory", "items", werkbank.Inventar)), Is.EqualTo(1));
        }
    }
}
