using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    public class PowSpielzeugTests
    {
        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        private static JObject State(int coins = 500, double fun = 40)
        {
            return new JObject
            {
                ["economy"] = new JObject { ["coins"] = coins },
                ["gooby"] = new JObject { ["stats"] = new JObject { ["hunger"] = 60, ["energy"] = 70, ["hygiene"] = 50, ["fun"] = fun } },
                ["home"] = new JObject { ["storage"] = new JArray() },
                ["inventory"] = new JObject { ["items"] = new JObject() },
            };
        }

        private static PowLogic.Angebot Offer(string id, int price)
        {
            var ware = PowLogic.Pool().First(w => w.Id == id);
            return new PowLogic.Angebot { Ware = ware, Rabatt = 0, PreisNeu = price };
        }

        private static int Stored(JObject s, string item) =>
            (J.Path(s, "home", "storage") as JArray ?? new JArray()).Where(r => J.Str(r["item"]) == item).Sum(r => J.Int(r["count"]));

        [Test]
        public void DecoGoesIntoTheBuildStorageAndToysIntoTheBox()
        {
            Assert.That(PowLogic.DekoMoebel.Values.All(id => FurnitureCatalog.Def(id) != null), "every POW! deco piece is in the catalog");
            Assert.That(PowLogic.DekoMoebel.Values.All(id => FurnitureCatalog.Def(id).Preis == 0), "price 0: only POW! sells them");
            Assert.That(PowLogic.Pool().Count(w => PowLogic.MoebelVon(w.Id) == null), Is.EqualTo(Spielzeug.Alle.Count),
                "every other pool ware is a toy of the box");
            Assert.That(Spielzeug.Alle.All(d => PowLogic.Pool().Any(w => w.Id == d.Id && w.Inventar == d.Id)));

            var s = State();
            Assert.That(PowLogic.KaufeAngebot(s, Offer("lavalampe", 45)), Is.EqualTo(PowLogic.Ok));
            Assert.That(Stored(s, "pow_lavalampe"), Is.EqualTo(1));
            Assert.That(J.Int(J.Path(s, "inventory", "items", "lavalampe")), Is.EqualTo(0), "not a toy any more");
            Assert.That(PowLogic.KaufeAngebot(s, Offer("flummi", 8)), Is.EqualTo(PowLogic.Ok));
            Assert.That(Spielzeug.Anzahl(s, "flummi"), Is.EqualTo(1));
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(500 - 45 - 8));
            Assert.That(PowLogic.KaufeAngebot(State(coins: 7), Offer("flummi", 8)), Is.EqualTo(PowLogic.Pleite));
        }

        [Test]
        public void AFullStorageRefusesTheDecoBeforePaying()
        {
            var s = State();
            J.EnsureDict(s, "home")["storageCapacity"] = 2;
            StorageLogic.Add((JArray)s["home"]["storage"], "bear");
            StorageLogic.Add((JArray)s["home"]["storage"], "bear");
            Assert.That(PowLogic.KaufeAngebot(s, Offer("diskokugel", 60)), Is.EqualTo(PowLogic.LagerVoll));
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(500), "nothing paid");
            Assert.That(PowLogic.KaufeAngebot(s, Offer("gummiente", 20)), Is.EqualTo(PowLogic.Ok), "toys do not need the storage");
        }

        [Test]
        public void DecoBoughtAsAToyMovesIntoTheStorageOnce()
        {
            var s = State();
            var items = (JObject)s["inventory"]["items"];
            items["lavalampe"] = 2;
            items["poster_gooby"] = 1;
            items["flummi"] = 1;
            Assert.That(PowLogic.DekoNachholen(s), Is.EqualTo(3));
            Assert.That(Stored(s, "pow_lavalampe"), Is.EqualTo(2));
            Assert.That(Stored(s, "pow_poster"), Is.EqualTo(1));
            Assert.That(items["lavalampe"], Is.Null);
            Assert.That(J.Int(items["flummi"]), Is.EqualTo(1), "toys stay in the box");
            Assert.That(PowLogic.DekoNachholen(s), Is.EqualTo(0), "only once");

            var voll = State();
            J.EnsureDict(voll, "home")["storageCapacity"] = 1;
            ((JObject)voll["inventory"]["items"])["sammelfigur"] = 2;
            Assert.That(PowLogic.DekoNachholen(voll), Is.EqualTo(1), "as many as fit");
            Assert.That(J.Int(J.Path(voll, "inventory", "items", "sammelfigur")), Is.EqualTo(1), "the rest waits for room");
        }

        [Test]
        public void AToyGivesItsFullFunOnceADayThenAThird()
        {
            var s = State(fun: 40);
            ((JObject)s["inventory"]["items"])["flummi"] = 1;
            Assert.That(Spielzeug.Kiste(s).Select(k => k.Def.Id), Is.EqualTo(new[] { "flummi" }));
            var first = Spielzeug.Spielen(s, "flummi", "2026-10-12");
            Assert.That(first.Grund, Is.EqualTo(Spielzeug.Ok));
            Assert.That(first.Frisch, Is.True);
            Assert.That(first.Spass, Is.EqualTo(14));
            Assert.That(J.Num(J.Path(s, "gooby", "stats", "fun")), Is.EqualTo(54));
            Assert.That(J.Num(J.Path(s, "gooby", "stats", "energy")), Is.EqualTo(68), "playing tires a little");
            var again = Spielzeug.Spielen(s, "flummi", "2026-10-12");
            Assert.That(again.Frisch, Is.False);
            Assert.That(again.Spass, Is.EqualTo(5), "a third, rounded");
            Assert.That(Spielzeug.Anzahl(s, "flummi"), Is.EqualTo(1), "the ball stays");
            Assert.That(Spielzeug.Spielen(s, "flummi", "2026-10-13").Spass, Is.EqualTo(14), "a new day, a new game");
            Assert.That(J.Int(J.Path(s, "achievements", "counters", "toyPlays")), Is.EqualTo(3));
        }

        [Test]
        public void TheCrackerIsUsedUpAndASleepingOrHappyGoobySaysNo()
        {
            var s = State(fun: 30);
            var items = (JObject)s["inventory"]["items"];
            items["knallbonbon"] = 1;
            items["wasserpistole"] = 1;
            var bang = Spielzeug.Spielen(s, "knallbonbon", "2026-10-12");
            Assert.That(bang.Rest, Is.EqualTo(0));
            Assert.That(items["knallbonbon"], Is.Null, "the cracker is gone");
            Assert.That(Spielzeug.Spielen(s, "knallbonbon", "2026-10-12").Grund, Is.EqualTo(Spielzeug.Keins));
            Assert.That(Spielzeug.Spielen(s, "wasserpistole", "2026-10-12").Grund, Is.EqualTo(Spielzeug.Ok));
            Assert.That(J.Num(J.Path(s, "gooby", "stats", "hygiene")), Is.EqualTo(56), "the water pistol washes a little");
            Assert.That(Spielzeug.Ablehnung(s, "gummiente"), Is.EqualTo(Spielzeug.Keins));

            J.EnsureDict(J.EnsureDict(s, "gooby"), "sleep")["sleeping"] = true;
            Assert.That(Spielzeug.Ablehnung(s, "wasserpistole"), Is.EqualTo(Spielzeug.Schlaeft));
            var happy = State(fun: 98);
            ((JObject)happy["inventory"]["items"])["flummi"] = 1;
            Assert.That(Spielzeug.Spielen(happy, "flummi", "2026-10-12").Grund, Is.EqualTo(Spielzeug.GutGelaunt));
            Assert.That(Spielzeug.Anzahl(happy, "flummi"), Is.EqualTo(1));
        }
    }
}
