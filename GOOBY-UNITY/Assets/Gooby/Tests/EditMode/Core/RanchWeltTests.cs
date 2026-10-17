using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The open ranch region: the map (test_rwelt_karte.gd), the height model (test_rwelt_gelaende.gd), the save
    /// (test_rwelt_state.gd), the discoveries and the explorer map (test_w19_entdecker_karte.gd).
    /// </summary>
    public class RanchWeltTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            RanchKarte.ResetCache();
        }

        private static JObject Spieler() => new JObject { ["economy"] = new JObject { ["coins"] = 100 } };

        [Test]
        public void TheMapIsSound()
        {
            Assert.That(RanchKarte.Zonen.Count, Is.EqualTo(16));
            Assert.That(RanchKarte.Wege.Count, Is.EqualTo(21));
            Assert.That(RanchKarte.Probleme(), Is.Empty);
            Assert.That(RanchKarte.SeedWert, Is.EqualTo(RanchWeltState.KartenSeed));
        }

        [Test]
        public void ZonesAndTheGreenLandBetween()
        {
            Assert.That(RanchKarte.ZoneBei(0, 0), Is.EqualTo("hof"));
            Assert.That(RanchKarte.ZoneBei(-380, 90), Is.EqualTo("weidetal"));
            Assert.That(RanchKarte.ZoneBei(500, 270), Is.EqualTo("see"));
            Assert.That(RanchKarte.ZoneBei(160, -510), Is.EqualTo("huegelkamm"));
            Assert.That(RanchKarte.ZoneBei(0, -250), Is.EqualTo(""));
        }

        [Test]
        public void PathsRunBothWaysAndReachEveryZone()
        {
            var hin = RanchKarte.Wegpunkte("hof", "see");
            var zurueck = RanchKarte.Wegpunkte("see", "hof");
            Assert.That(hin.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(zurueck.Count, Is.EqualTo(hin.Count));
            Assert.That(hin[0].X, Is.EqualTo(zurueck[zurueck.Count - 1].X));
            Assert.That(RanchKarte.Wegpunkte("see", "waeldchen"), Is.Empty);
            var gesehen = new HashSet<string> { "hof" };
            var offen = new Queue<string>(new[] { "hof" });
            while (offen.Count > 0)
                foreach (var n in RanchKarte.Nachbarn(offen.Dequeue()))
                    if (gesehen.Add(n)) offen.Enqueue(n);
            Assert.That(gesehen, Is.EquivalentTo(RanchKarte.ZonenIds()), "every zone is reachable from the farm");
        }

        [Test]
        public void WaterAndTheWorldEdgeBlockButFordAndBridgeCross()
        {
            Assert.That(RanchKarte.IstBegehbar(0, 100), Is.True);
            Assert.That(RanchKarte.IstBegehbar(500, 270), Is.False, "the middle of the lake");
            Assert.That(RanchKarte.IstBegehbar(9999, 0), Is.False);
            Assert.That(RanchKarte.IstBegehbar(RanchKarte.Furt.X, RanchKarte.Furt.Z), Is.True);
            Assert.That(RanchKarte.IstBegehbar(RanchKarte.Bruecke.X, RanchKarte.Bruecke.Z), Is.True);
        }

        [Test]
        public void TheFarmIsFlatTheRidgeHighTheLakeUnderWater()
        {
            var basis = RanchKarte.ZoneVon("hof").HoeheBasis;
            foreach (var (x, z) in new[] { (0.0, 0.0), (-46.0, 24.0), (38.0, -18.0), (120.0, 100.0) })
                Assert.That(RanchGelaende.Hoehe(x, z), Is.EqualTo(basis).Within(0.35), "flat at " + x + "," + z);
            var punkt = RanchKarte.ZoneVon("huegelkamm").Punkt("aussichtspunkt");
            var oben = RanchGelaende.Hoehe(punkt.X, punkt.Z);
            Assert.That(oben, Is.GreaterThan(14.0));
            Assert.That(oben, Is.GreaterThan(RanchGelaende.Hoehe(0, 0) + 12.0));
            var see = RanchKarte.ZoneVon("see").Punkt("see_mitte");
            Assert.That(RanchGelaende.Hoehe(see.X, see.Z), Is.LessThan(RanchGelaende.WasserHoehe - 1.0));
            Assert.That(RanchGelaende.IstWasser(see.X, see.Z), Is.True);
            var min = double.MaxValue;
            var max = double.MinValue;
            for (var x = -650; x <= 650; x += 50)
                for (var z = -600; z <= 600; z += 50)
                {
                    var h = RanchGelaende.Hoehe(x, z);
                    min = Math.Min(min, h);
                    max = Math.Max(max, h);
                }
            Assert.That(max - min, Is.GreaterThan(10.0), "no flat board");
        }

        [Test]
        public void TheBrookIsNotchedAndTheFordShallow()
        {
            var mitte = RanchKarte.BachPunkte[4];
            var bett = RanchGelaende.Hoehe(mitte.X, mitte.Z);
            var ufer = RanchGelaende.Hoehe(mitte.X + 14.0, mitte.Z);
            Assert.That(bett, Is.LessThan(ufer - 0.8));
            Assert.That(RanchGelaende.IstWasser(mitte.X, mitte.Z), Is.True);
            Assert.That(RanchGelaende.IstWasser(RanchKarte.Furt.X, RanchKarte.Furt.Z), Is.False);
            Assert.That(RanchGelaende.BachWasserspiegel(mitte.X, mitte.Z), Is.GreaterThan(bett));
        }

        [Test]
        public void TheLandIsGentleToRideAndTheMountainsRise()
        {
            var schlimmste = 0.0;
            for (var x = -600; x <= 600; x += 24)
                for (var z = -600; z <= 600; z += 24)
                    schlimmste = Math.Max(schlimmste, Math.Abs(RanchGelaende.Hoehe(x + 2.0, z) - RanchGelaende.Hoehe(x, z)) / 2.0);
            Assert.That(schlimmste, Is.LessThan(0.6), "no cliffs on paths and pastures");
            Assert.That(RanchGelaende.Hoehe(123.4, -321.9), Is.EqualTo(RanchGelaende.Hoehe(123.4, -321.9)));
            var n = RanchGelaende.Normale(120, -390);
            Assert.That(n.y, Is.GreaterThan(0.5));
            Assert.That(Math.Sqrt(n.x * n.x + n.y * n.y + n.z * n.z), Is.EqualTo(1.0).Within(0.001));
            var plateau = RanchKarte.ZoneVon("bergmassiv").Punkt("plateau_mitte");
            Assert.That(RanchGelaende.Hoehe(plateau.X, plateau.Z), Is.GreaterThan(50.0), "the mountain plateau");
            var deck = RanchGelaende.Bruecken()[0];
            var mx = (deck.a.X + deck.b.X) / 2.0;
            var mz = (deck.a.Z + deck.b.Z) / 2.0;
            Assert.That(RanchGelaende.ReitHoehe(mx, mz), Is.GreaterThan(RanchGelaende.Hoehe(mx, mz) + 3.0), "the bridge carries over the gorge");
        }

        [Test]
        public void ZonesRiddenIntoAreSavedOnce()
        {
            var s = Spieler();
            Assert.That(RanchWeltState.EntdeckteZonen(s), Is.EqualTo(new[] { "hof" }));
            Assert.That(RanchWeltState.EntdeckeZone(s, "see"), Is.True);
            Assert.That(RanchWeltState.EntdeckeZone(s, "see"), Is.False, "idempotent");
            Assert.That(RanchWeltState.EntdeckeZone(s, "mond"), Is.False, "unknown zones are ignored");
            Assert.That(RanchWeltState.EntdeckteZonen(s), Is.EqualTo(new[] { "hof", "see" }));
            s["ranch"]["welt"]["entdeckt"] = new JArray("see", "see", "quatsch");
            Assert.That(RanchWeltState.EntdeckteZonen(s), Is.EqualTo(new[] { "hof", "see" }), "healed on read");
        }

        [Test]
        public void APlaceIsFoundOnceAndPaysItsCoins()
        {
            var s = Spieler();
            Assert.That(RanchEntdeckungen.AlleOrte.Length, Is.EqualTo(16));
            Assert.That(RanchEntdeckungen.AlleOrte.Select(o => o.Id).Distinct().Count(), Is.EqualTo(16));
            var baum = RanchEntdeckungen.Fundort("alter_baum");
            Assert.That(RanchEntdeckungen.FundBei(s, baum.Pos.X + 30, baum.Pos.Z), Is.Null, "you have to really ride there");
            Assert.That(RanchEntdeckungen.FundBei(s, baum.Pos.X + 10, baum.Pos.Z).Id, Is.EqualTo("alter_baum"));
            var r = RanchEntdeckungen.Entdecke(s, "alter_baum");
            Assert.That(r.neu, Is.True);
            Assert.That(r.muenzen, Is.EqualTo(30));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(130));
            Assert.That(RanchEntdeckungen.Entdecke(s, "alter_baum").neu, Is.False);
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(130), "paid once");
            Assert.That(RanchEntdeckungen.FundBei(s, baum.Pos.X, baum.Pos.Z), Is.Null, "found places stay quiet");
            foreach (var o in RanchEntdeckungen.AlleOrte)
                Assert.That(RanchKarte.Grenzen.Enthaelt(o.Pos.X, o.Pos.Z), Is.True, o.Id);
        }

        [Test]
        public void TheExplorerMapHidesUntilFoundAndBadgesOnce()
        {
            var s = Spieler();
            var pins = RanchEntdeckerKarte.Fundorte(s);
            Assert.That(pins.Count, Is.EqualTo(16));
            Assert.That(pins.All(p => !p.Entdeckt && !p.Neu), Is.True);
            var wasserfall = pins.First(p => p.Ort.Id == "wasserfall");
            Assert.That(wasserfall.ZeigPos.X, Is.EqualTo(270.0));
            Assert.That(wasserfall.ZeigPos.Z, Is.EqualTo(-450.0), "the hidden pin sits on the coarse grid");
            RanchEntdeckungen.Entdecke(s, "wasserfall");
            wasserfall = RanchEntdeckerKarte.Fundorte(s).First(p => p.Ort.Id == "wasserfall");
            Assert.That(wasserfall.Entdeckt && wasserfall.Neu, Is.True);
            Assert.That(wasserfall.ZeigPos.X, Is.EqualTo(252.0));
            RanchEntdeckerKarte.MarkiereGesehen(s, new[] { "wasserfall" });
            Assert.That(RanchEntdeckerKarte.NeueFunde(s), Is.Empty, "the NEU badge once");
            var f = RanchEntdeckerKarte.Fortschritt(s);
            Assert.That((f.funde, f.fundeGesamt, f.zonen, f.zonenGesamt), Is.EqualTo((1, 16, 1, 16)));
        }

        [Test]
        public void TheCompassPointsTheWay()
        {
            var o = new V2d(0, 0);
            Assert.That(RanchEntdeckerKarte.RichtungKey(o, new V2d(0, -100)), Is.EqualTo("n"));
            Assert.That(RanchEntdeckerKarte.RichtungKey(o, new V2d(100, 0)), Is.EqualTo("o"));
            Assert.That(RanchEntdeckerKarte.RichtungKey(o, new V2d(100, -100)), Is.EqualTo("no"));
            Assert.That(RanchEntdeckerKarte.RichtungKey(o, new V2d(-100, 100)), Is.EqualTo("sw"));
            Assert.That(RanchEntdeckerKarte.RichtungKey(o, o), Is.EqualTo(""));
            Assert.That(RanchEntdeckerKarte.HinweisZone(new V2d(252, -470)), Is.EqualTo("huegelkamm"));
            Assert.That(RanchEntdeckerKarte.HinweisZone(new V2d(245, -250)), Is.EqualTo("bachlauf"));
            foreach (var zone in RanchKarte.Zonen)
                Assert.That(RanchEntdeckerKarte.StimmungFarben.ContainsKey(zone.Stimmung), Is.True, zone.Id);
        }
    }
}
