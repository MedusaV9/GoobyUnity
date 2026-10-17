using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The ranch build mode: the grid (test_rbau_grid.gd), the save and its atomic purchases (test_rbau_state.gd), the
    /// level effects (test_rbau_effekte.gd), the economy balance (test_rbau_bilanz.gd), the texts (test_rbau_texte.gd)
    /// and the Unity wiring of the effects into the farm (boxes, hay store, brushing, pasture, walker, breeding,
    /// tournaments, gear, the furniture barn's decoration).
    /// </summary>
    public class RanchBauTests
    {
        private static readonly ZellRect StartZone = new ZellRect(0, 6, 10, 10);
        private const long Mittag = (3L * 24 + 12) * 3_600_000L;
        private const string Tag = "2026-10-11";

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            RanchBauKatalog.ResetCache();
        }

        private static JObject Bal => RanchBauKatalog.Balance;

        private static JObject Spieler(int coins) => new JObject { ["economy"] = new JObject { ["coins"] = coins } };

        private static int Coins(JObject s) => J.Int(s["economy"]["coins"]);

        private static JObject Ranch(int coins)
        {
            var s = new JObject
            {
                ["progression"] = new JObject { ["level"] = 20 },
                ["economy"] = new JObject { ["coins"] = coins + RanchHof.Preis },
                ["inventory"] = new JObject { ["food"] = new JObject { ["carrot"] = 2 } },
            };
            Assert.That(RanchKauf.Kaufe(s, Mittag), Is.EqualTo(RanchKauf.Ok));
            return s;
        }

        private static JObject BauMit(params (string id, int stufe)[] anlagen)
        {
            var bau = RanchBauState.DefaultBau();
            foreach (var (id, stufe) in anlagen) bau["anlagen"][id] = new JObject { ["stufe"] = stufe };
            return bau;
        }

        private static Dictionary<string, RanchBauDef> TestDefs() => new Dictionary<string, RanchBauDef>
        {
            ["stall"] = new RanchBauDef { Id = "stall", Kategorie = "anlage", Layer = RanchGridData.Layer.Objekt, Footprint = new Zelle(2, 3), Kosten = 100 },
            ["weg"] = new RanchBauDef { Id = "weg", Kategorie = "boden", Layer = RanchGridData.Layer.Boden, Footprint = new Zelle(1, 1), Kosten = 10 },
            ["zaun"] = new RanchBauDef { Id = "zaun", Kategorie = "zaun", Layer = RanchGridData.Layer.Objekt, Footprint = new Zelle(1, 1), Kante = true, Kosten = 20 },
        };

        private static RanchGridData Grid() => new RanchGridData(new Zelle(16, 16), new[] { StartZone });

        // ── the grid ──

        [Test]
        public void PlacingAndCollisions()
        {
            var grid = Grid();
            var stall = TestDefs()["stall"];
            Assert.That(grid.Place(stall, new Zelle(1, 7), 0, "a").ok, Is.True);
            var again = grid.CanPlace(stall, new Zelle(2, 8), 0);
            Assert.That(again.ok, Is.False);
            Assert.That(again.reason, Is.EqualTo(RanchGridData.ReasonOccupied));
            Assert.That(grid.ItemAt(new Zelle(2, 9), RanchGridData.Layer.Objekt), Is.EqualTo("a"));
            Assert.That(grid.ItemAt(new Zelle(4, 9), RanchGridData.Layer.Objekt), Is.EqualTo(""));
        }

        [Test]
        public void RotationTurnsTheFootprint()
        {
            Assert.That(RanchGridData.RotatedFootprint(new Zelle(2, 3), 0), Is.EqualTo(new Zelle(2, 3)));
            Assert.That(RanchGridData.RotatedFootprint(new Zelle(2, 3), 1), Is.EqualTo(new Zelle(3, 2)));
            Assert.That(RanchGridData.RotatedFootprint(new Zelle(2, 3), 2), Is.EqualTo(new Zelle(2, 3)));
            Assert.That(RanchGridData.RotatedFootprint(new Zelle(2, 3), 7), Is.EqualTo(new Zelle(3, 2)));
            var grid = Grid();
            var stall = TestDefs()["stall"];
            Assert.That(grid.CanPlace(stall, new Zelle(8, 14), 1).ok, Is.False, "turned, too wide");
            Assert.That(grid.CanPlace(stall, new Zelle(7, 14), 1).ok, Is.True, "fits turned");
        }

        [Test]
        public void LockedZonesAndBounds()
        {
            var grid = Grid();
            var weg = TestDefs()["weg"];
            var oben = grid.CanPlace(weg, new Zelle(3, 3), 0);
            Assert.That(oben.ok, Is.False);
            Assert.That(oben.reason, Is.EqualTo(RanchGridData.ReasonGesperrt));
            Assert.That(grid.CanPlace(weg, new Zelle(-1, 8), 0).reason, Is.EqualTo(RanchGridData.ReasonOob));
            var offen = new RanchGridData(new Zelle(16, 16), new[] { StartZone, new ZellRect(0, 0, 10, 6) });
            Assert.That(offen.CanPlace(weg, new Zelle(3, 3), 0).ok, Is.True);
        }

        [Test]
        public void GroundAndObjectLayersAreSeparate()
        {
            var grid = Grid();
            Assert.That(grid.Place(TestDefs()["weg"], new Zelle(2, 8), 0, "w").ok, Is.True);
            Assert.That(grid.Place(TestDefs()["stall"], new Zelle(2, 8), 0, "s").ok, Is.True, "objects stand ON the ground");
            Assert.That(grid.CanPlace(TestDefs()["weg"], new Zelle(2, 8), 0).ok, Is.False, "ground on ground collides");
        }

        [Test]
        public void EdgesNormalise()
        {
            Assert.That(RanchGridData.NormalizeKante(new Zelle(3, 4), "S"), Is.EqualTo((new Zelle(3, 5), "N")));
            Assert.That(RanchGridData.NormalizeKante(new Zelle(3, 4), "E"), Is.EqualTo((new Zelle(4, 4), "W")));
            Assert.That(RanchGridData.NormalizeKante(new Zelle(3, 4), "N"), Is.EqualTo((new Zelle(3, 4), "N")));
        }

        [Test]
        public void AFenceOnAnEdgeAndTheSameEdgeCollides()
        {
            var grid = Grid();
            var zaun = TestDefs()["zaun"];
            Assert.That(grid.PlaceKante(zaun, new Zelle(3, 8), "S", "z1").ok, Is.True);
            var doppelt = grid.CanPlaceKante(zaun, new Zelle(3, 9), "N");
            Assert.That(doppelt.ok, Is.False, "S(3,8) and N(3,9) are ONE edge");
            Assert.That(doppelt.reason, Is.EqualTo(RanchGridData.ReasonOccupied));
            Assert.That(grid.KanteItemAt(new Zelle(3, 8), "S"), Is.EqualTo("z1"));
            Assert.That(grid.KanteItemAt(new Zelle(3, 9), "N"), Is.EqualTo("z1"));
            Assert.That(grid.CanPlaceKante(zaun, new Zelle(3, 8), "N").ok, Is.True, "another edge is free");
        }

        [Test]
        public void FencesOnlyAtUnlockedCells()
        {
            var grid = Grid();
            var zaun = TestDefs()["zaun"];
            Assert.That(grid.CanPlaceKante(zaun, new Zelle(3, 2), "N").ok, Is.False, "locked zone");
            Assert.That(grid.CanPlaceKante(zaun, new Zelle(3, 6), "N").ok, Is.True, "the start zone's rim");
            Assert.That(grid.Place(TestDefs()["stall"], new Zelle(1, 7), 0, "s").ok, Is.True);
            Assert.That(grid.CanPlaceKante(zaun, new Zelle(1, 7), "W").ok, Is.True, "a cell item never blocks an edge");
        }

        [Test]
        public void APastureRingStakesOutARect()
        {
            var ring = RanchGridData.WeideRing(new ZellRect(2, 8, 3, 2));
            Assert.That(ring.Count, Is.EqualTo(10));
            var grid = Grid();
            var zaun = TestDefs()["zaun"];
            for (var i = 0; i < ring.Count; i++) Assert.That(grid.PlaceKante(zaun, ring[i].cell, ring[i].seite, "r" + i).ok, Is.True, "ring edge " + i);
            Assert.That(RanchGridData.WeideRing(new ZellRect(2, 8, 3, 2), 0).Count, Is.EqualTo(9), "a gate gap");
        }

        [Test]
        public void SaveRoundtripAndLeftovers()
        {
            var grid = Grid();
            Assert.That(grid.Place(TestDefs()["stall"], new Zelle(1, 7), 1, "a").ok, Is.True);
            Assert.That(grid.PlaceKante(TestDefs()["zaun"], new Zelle(3, 8), "E", "b").ok, Is.True);
            var items = grid.ToItemsArray();
            Assert.That(items.Count, Is.EqualTo(2));
            var wieder = RanchGridData.FromSave(items, TestDefs(), new Zelle(16, 16), new[] { StartZone });
            Assert.That(wieder.leftovers.Count, Is.EqualTo(0));
            Assert.That(wieder.grid.GetItem("a").Rot, Is.EqualTo(1));
            Assert.That(wieder.grid.KanteItemAt(new Zelle(3, 8), "E"), Is.EqualTo("b"));
            items.Add(new JObject { ["uid"] = "x", ["item"] = "ufo", ["at"] = new JArray(5, 9), ["rot"] = 0 });
            items.Add(new JObject { ["uid"] = "y", ["item"] = "stall", ["at"] = new JArray(1, 7), ["rot"] = 0 });
            Assert.That(RanchGridData.FromSave(items, TestDefs(), new Zelle(16, 16), new[] { StartZone }).leftovers.Count, Is.EqualTo(2), "unknown + conflict");
        }

        [Test]
        public void MovingChecksCollisions()
        {
            var grid = Grid();
            Assert.That(grid.Place(TestDefs()["stall"], new Zelle(1, 7), 0, "a").ok, Is.True);
            Assert.That(grid.Place(TestDefs()["weg"], new Zelle(6, 8), 0, "w").ok, Is.True);
            Assert.That(grid.MoveItem("a", new Zelle(4, 10), 1).ok, Is.True);
            Assert.That(grid.ItemAt(new Zelle(1, 7), RanchGridData.Layer.Objekt), Is.EqualTo(""), "the old cells are free");
            Assert.That(grid.MoveItem("a", new Zelle(4, 10), 0).ok, Is.True, "turning in place ignores itself");
            Assert.That(grid.MoveItem("a", new Zelle(9, 14), 0).ok, Is.False, "2×3 sticks out of the zone");
        }

        [Test]
        public void WorldCoordinates()
        {
            Assert.That(RanchGridData.WorldCenter(new Zelle(0, 0), new Zelle(2, 2), 0), Is.EqualTo((3.0, 3.0)));
            Assert.That(RanchGridData.CellOf(3.1, 8.9), Is.EqualTo(new Zelle(1, 2)));
            var kante = RanchGridData.KanteWorld(new Zelle(2, 2), "S");
            Assert.That((kante.x, kante.z, kante.rot), Is.EqualTo((7.5, 9.0, 0.0)));
            Assert.That(RanchGridData.NearestKante(7.4, 8.9), Is.EqualTo((new Zelle(2, 3), "N")));
        }

        // ── the save ──

        [Test]
        public void NormalizeHealsBrokenData()
        {
            var bau = RanchBauState.NormalizeBau(JObject.Parse("{\"items\":\"kaputt\",\"zonen\":[],\"uidSeq\":-3,\"eigen\":7}"));
            Assert.That(((JArray)bau["items"]).Count, Is.EqualTo(0));
            Assert.That(((JArray)bau["zonen"]).Select(t => J.Str(t)), Is.EqualTo(new[] { "start" }));
            Assert.That(J.Int(bau["uidSeq"]), Is.EqualTo(1));
            Assert.That(J.Int(bau["eigen"]), Is.EqualTo(7), "unknown keys stay");
            var ok = RanchBauState.NormalizeBau(JObject.Parse("{\"anlagen\":{\"weide\":{\"stufe\":2}},\"lager\":{\"bank_holz\":2,\"kaputt\":0}}"));
            Assert.That(RanchBauState.AnlageStufe(ok, "weide"), Is.EqualTo(2));
            Assert.That(J.Dict(ok["lager"]).Properties().Select(p => p.Name), Is.EqualTo(new[] { "bank_holz" }));
        }

        [Test]
        public void PlacingBooksGoldAtomically()
        {
            var s = Spieler(500);
            var res = RanchBauState.Platziere(s, "heulager", new Zelle(1, 7), 0);
            Assert.That(res.Ok, Is.True);
            Assert.That(res.Kosten, Is.EqualTo(200));
            Assert.That(Coins(s), Is.EqualTo(300));
            var bau = RanchBauState.Lese(s);
            Assert.That(((JArray)bau["items"]).Count, Is.EqualTo(1));
            Assert.That(RanchBauState.AnlageStufe(bau, "heulager"), Is.EqualTo(1));
            Assert.That(res.Uid, Is.EqualTo("rb-000001"));
        }

        [Test]
        public void TooExpensiveChangesNothing()
        {
            var s = Spieler(199);
            var res = RanchBauState.Platziere(s, "heulager", new Zelle(1, 7), 0);
            Assert.That(res.Ok, Is.False);
            Assert.That(res.Fehler, Is.EqualTo(RanchBauState.FehlerZuTeuer));
            Assert.That(Coins(s), Is.EqualTo(199));
            Assert.That(((JArray)RanchBauState.Lese(s)["items"]).Count, Is.EqualTo(0));
        }

        [Test]
        public void AFacilityOnceDecorationAsOftenAsYouLike()
        {
            var s = Spieler(9999);
            Assert.That(RanchBauState.Platziere(s, "heulager", new Zelle(1, 7), 0).Ok, Is.True);
            Assert.That(RanchBauState.Platziere(s, "heulager", new Zelle(5, 7), 0).Fehler, Is.EqualTo(RanchBauState.FehlerSchonGebaut));
            Assert.That(RanchBauState.Platziere(s, "bank_holz", new Zelle(4, 8), 0).Ok, Is.True);
            Assert.That(RanchBauState.Platziere(s, "bank_holz", new Zelle(5, 8), 0).Ok, Is.True);
        }

        [Test]
        public void ACollisionBlocksBeforeBooking()
        {
            var s = Spieler(9999);
            Assert.That(RanchBauState.Platziere(s, "heulager", new Zelle(1, 7), 0).Ok, Is.True);
            var vorher = Coins(s);
            var drauf = RanchBauState.Platziere(s, "bank_holz", new Zelle(1, 7), 0);
            Assert.That(drauf.Ok, Is.False);
            Assert.That(drauf.Fehler, Is.EqualTo(RanchGridData.ReasonOccupied));
            Assert.That(Coins(s), Is.EqualTo(vorher));
        }

        [Test]
        public void AFenceOnAnEdgeIsBooked()
        {
            var s = Spieler(100);
            Assert.That(RanchBauState.PlatziereKante(s, "zaun_holz", new Zelle(3, 8), "S").Ok, Is.True);
            Assert.That(Coins(s), Is.EqualTo(80));
            var eintrag = (JObject)((JArray)RanchBauState.Lese(s)["items"])[0];
            Assert.That(J.Str(eintrag["kante"]), Is.EqualTo("N"));
            Assert.That(((JArray)eintrag["at"]).Select(t => J.Int(t)), Is.EqualTo(new[] { 3, 9 }));
            Assert.That(RanchBauState.PlatziereKante(s, "zaun_holz", new Zelle(3, 9), "N").Ok, Is.False);
            Assert.That(RanchBauState.PlatziereKante(s, "bank_holz", new Zelle(4, 8), "N").Fehler, Is.EqualTo(RanchBauState.FehlerUnbekannt));
            Assert.That(RanchBauState.Platziere(s, "zaun_holz", new Zelle(4, 8), 0).Fehler, Is.EqualTo(RanchBauState.FehlerUnbekannt));
        }

        [Test]
        public void DemolitionRefundsHalf()
        {
            var s = Spieler(1000);
            var res = RanchBauState.Platziere(s, "waschplatz", new Zelle(2, 8), 0);
            Assert.That(Coins(s), Is.EqualTo(700));
            var weg = RanchBauState.Entferne(s, res.Uid);
            Assert.That(weg.Ok, Is.True);
            Assert.That(weg.Erstattung, Is.EqualTo(150));
            Assert.That(Coins(s), Is.EqualTo(850));
            Assert.That(((JArray)RanchBauState.Lese(s)["items"]).Count, Is.EqualTo(0));
            Assert.That(RanchBauState.AnlageStufe(RanchBauState.Lese(s), "waschplatz"), Is.EqualTo(0));
        }

        [Test]
        public void DemolitionAfterUpgradesRefundsEveryLevel()
        {
            var s = Spieler(5000);
            var res = RanchBauState.Platziere(s, "waschplatz", new Zelle(2, 8), 0);
            Assert.That(RanchBauState.Ausbauen(s, "waschplatz").Ok, Is.True);
            Assert.That(RanchBauState.ErstattungFuer(RanchBauState.Lese(s), res.Uid), Is.EqualTo(500), "the preview");
            Assert.That(RanchBauState.Entferne(s, res.Uid).Erstattung, Is.EqualTo(500), "(300 + 700) / 2");
        }

        [Test]
        public void UpgradeLevelsAndGates()
        {
            var s = Spieler(10000);
            Assert.That(RanchBauState.Ausbauen(s, "heulager").Fehler, Is.EqualTo(RanchBauState.FehlerNichtGebaut));
            Assert.That(RanchBauState.Platziere(s, "heulager", new Zelle(1, 7), 0).Ok, Is.True);
            var s2 = RanchBauState.Ausbauen(s, "heulager");
            Assert.That((s2.Ok, s2.Stufe, s2.Kosten), Is.EqualTo((true, 2, 500)));
            Assert.That(RanchBauState.Ausbauen(s, "heulager").Stufe, Is.EqualTo(3));
            Assert.That(RanchBauState.Ausbauen(s, "heulager").Fehler, Is.EqualTo(RanchBauState.FehlerAusgebaut));
            Assert.That(Coins(s), Is.EqualTo(10000 - 200 - 500 - 1000));
        }

        [Test]
        public void ThePastureFenceUpgradeNeedsNoPlacement()
        {
            var s = Spieler(1000);
            Assert.That(RanchBauState.Platziere(s, "weidezaun", new Zelle(1, 7), 0).Fehler, Is.EqualTo(RanchBauState.FehlerUnbekannt));
            var s1 = RanchBauState.Ausbauen(s, "weidezaun");
            Assert.That((s1.Ok, s1.Kosten), Is.EqualTo((true, 250)));
            Assert.That(RanchBauState.AnlageStufe(RanchBauState.Lese(s), "weidezaun"), Is.EqualTo(1));
        }

        [Test]
        public void UnlockingAZone()
        {
            var s = Spieler(1000);
            var nord = RanchBauState.ZoneFreischalten(s, "nord");
            Assert.That((nord.Ok, nord.Kosten), Is.EqualTo((true, 800)));
            Assert.That(Coins(s), Is.EqualTo(200));
            Assert.That(RanchBauState.ZoneFreischalten(s, "nord").Fehler, Is.EqualTo(RanchBauState.FehlerSchonFrei));
            Assert.That(RanchBauState.ZoneFreischalten(s, "ost").Fehler, Is.EqualTo(RanchBauState.FehlerZuTeuer));
            Assert.That(RanchBauState.Platziere(s, "bank_holz", new Zelle(3, 3), 0).Ok, Is.True, "the north meadow is buildable now");
        }

        [Test]
        public void PlacingFromTheStoreIsFree()
        {
            var s = Spieler(0);
            RanchBauState.LagerHinzu(RanchBauState.Im(s), "bank_holz", 2);
            var res = RanchBauState.Platziere(s, "bank_holz", new Zelle(4, 8), 0);
            Assert.That((res.Ok, res.Kosten), Is.EqualTo((true, 0)));
            Assert.That(RanchBauState.LagerAnzahl(RanchBauState.Lese(s), "bank_holz"), Is.EqualTo(1));
            Assert.That(RanchBauState.Platziere(s, "bank_holz", new Zelle(5, 8), 0).Ok, Is.True);
            Assert.That(J.Dict(RanchBauState.Lese(s)["lager"]).Count, Is.EqualTo(0));
            Assert.That(RanchBauState.Platziere(s, "bank_holz", new Zelle(6, 8), 0).Ok, Is.False, "the third costs gold again");
        }

        [Test]
        public void MovingIsFreeAndChecked()
        {
            var s = Spieler(2000);
            var stall = RanchBauState.Platziere(s, "heulager", new Zelle(1, 7), 0);
            Assert.That(RanchBauState.Platziere(s, "bank_holz", new Zelle(6, 8), 0).Ok, Is.True);
            var vorher = Coins(s);
            Assert.That(RanchBauState.Verschiebe(s, stall.Uid, new Zelle(5, 8), 0).Fehler, Is.EqualTo(RanchGridData.ReasonOccupied));
            Assert.That(RanchBauState.Verschiebe(s, stall.Uid, new Zelle(2, 12), 1).Ok, Is.True);
            Assert.That(Coins(s), Is.EqualTo(vorher), "moving costs nothing");
            var grid = RanchBauState.GridVon(s).grid;
            Assert.That(grid.GetItem(stall.Uid).At, Is.EqualTo(new Zelle(2, 12)));
            Assert.That(grid.GetItem(stall.Uid).Rot, Is.EqualTo(1));
            Assert.That(RanchBauState.Verschiebe(s, "rb-999999", new Zelle(2, 12), 0).Fehler, Is.EqualTo(RanchBauState.FehlerUnbekannt));
        }

        [Test]
        public void TheMigrationTakesOverTheOldExtensions()
        {
            var s = Spieler(0);
            s["ranch"] = JObject.Parse("{\"wirtschaft\":{\"ausbau\":{\"boxen\":3,\"reitplatz\":true,\"weidezaun\":true}}}");
            Assert.That(RanchBauState.MigriereBestand(s), Is.True);
            var bau = RanchBauState.Lese(s);
            Assert.That(RanchBauState.AnlageStufe(bau, "stallboxen"), Is.EqualTo(3));
            Assert.That(RanchBauState.AnlageStufe(bau, "weide"), Is.EqualTo(1));
            Assert.That(RanchBauState.AnlageStufe(bau, "parcours"), Is.EqualTo(1));
            Assert.That(RanchBauState.AnlageStufe(bau, "weidezaun"), Is.EqualTo(1));
            Assert.That(Coins(s), Is.EqualTo(0));
            Assert.That(RanchBauState.MigriereBestand(s), Is.False, "once");
            Assert.That(RanchBauState.GridVon(s).leftovers.Count, Is.EqualTo(0), "every migrated spot is valid");
        }

        [Test]
        public void AContentPackIsAdditive()
        {
            var basis = RanchBauKatalog.Balance;
            var mitPack = RanchBauKatalog.MitPack(basis, JObject.Parse("{\"deko\":{\"windspiel\":{\"kosten\":110}}}"));
            Assert.That(RanchBauKatalog.Ids(basis, "deko"), Does.Not.Contain("windspiel"));
            Assert.That(RanchBauKatalog.Ids(mitPack, "deko"), Does.Contain("windspiel"));
            Assert.That(RanchBauKatalog.Def(mitPack, "windspiel").Kosten, Is.EqualTo(110));
            foreach (var d in RanchBauKatalog.Defs(basis).Values)
                Assert.That(RanchBauKatalog.Def(mitPack, d.Id).Kosten, Is.EqualTo(d.Kosten), d.Id + " unchanged");
            var s = Spieler(150);
            Assert.That(RanchBauState.Platziere(s, "windspiel", new Zelle(4, 8), 0, mitPack).Ok, Is.True);
            Assert.That(Coins(s), Is.EqualTo(40));
        }

        // ── the effects ──

        [Test]
        public void StableBoxesGrowPerLevel()
        {
            Assert.That(RanchBauEffekte.BoxenKapazitaet(BauMit(), Bal), Is.EqualTo(2));
            Assert.That(RanchBauEffekte.BoxenKapazitaet(BauMit(("stallboxen", 1)), Bal), Is.EqualTo(2));
            Assert.That(RanchBauEffekte.BoxenKapazitaet(BauMit(("stallboxen", 2)), Bal), Is.EqualTo(4));
            Assert.That(RanchBauEffekte.BoxenKapazitaet(BauMit(("stallboxen", 3)), Bal), Is.EqualTo(6));
            Assert.That(RanchBauEffekte.BoxenKapazitaet(BauMit(("stallboxen", 4)), Bal), Is.EqualTo(8));
        }

        [Test]
        public void TheHayBarnStoresMore()
        {
            Assert.That(RanchBauEffekte.HeuKapazitaet(BauMit(), Bal), Is.EqualTo(10));
            Assert.That(RanchBauEffekte.HeuKapazitaet(BauMit(("heulager", 1)), Bal), Is.EqualTo(12));
            Assert.That(RanchBauEffekte.HeuKapazitaet(BauMit(("heulager", 3)), Bal), Is.EqualTo(48));
        }

        [Test]
        public void TheRidingHallAndTheWalker()
        {
            Assert.That(RanchBauEffekte.TrainingBeiRegen(BauMit(), Bal), Is.False);
            Assert.That(RanchBauEffekte.TrainingBeiRegen(BauMit(("reithalle", 1)), Bal), Is.True);
            Assert.That(RanchBauEffekte.DressurXpMult(BauMit(("reithalle", 1)), Bal), Is.EqualTo(1.2).Within(1e-9));
            Assert.That(RanchBauEffekte.FuehranlageXpProTag(BauMit(), Bal), Is.EqualTo(0));
            Assert.That(RanchBauEffekte.FuehranlageXpProTag(BauMit(("fuehranlage", 1)), Bal), Is.EqualTo(40));
            Assert.That(RanchBauEffekte.FuehranlageXpProTag(BauMit(("fuehranlage", 3)), Bal), Is.EqualTo(80));
        }

        [Test]
        public void CareMultipliers()
        {
            Assert.That(RanchBauEffekte.StriegelWert(BauMit(), Bal), Is.EqualTo(35));
            Assert.That(RanchBauEffekte.StriegelWert(BauMit(("waschplatz", 1)), Bal), Is.EqualTo(50));
            Assert.That(RanchBauEffekte.WarmwasserBonus(BauMit(("waschplatz", 1)), Bal), Is.EqualTo(0));
            Assert.That(RanchBauEffekte.WarmwasserBonus(BauMit(("waschplatz", 2)), Bal), Is.EqualTo(2));
            Assert.That(RanchBauEffekte.HungerMult(BauMit(), Bal), Is.EqualTo(1.0));
            Assert.That(RanchBauEffekte.HungerMult(BauMit(("weide", 2)), Bal), Is.EqualTo(0.85).Within(1e-9));
            Assert.That(RanchBauEffekte.DurstMult(BauMit(("wasserstelle", 1)), Bal), Is.EqualTo(0.7).Within(1e-9));
            Assert.That(RanchBauEffekte.SelbsttraenkeAktiv(BauMit(("wasserstelle", 1)), Bal), Is.False);
            Assert.That(RanchBauEffekte.SelbsttraenkeAktiv(BauMit(("wasserstelle", 2)), Bal), Is.True);
            Assert.That(RanchBauEffekte.SauberkeitMult(BauMit(("weidezaun", 3)), Bal), Is.EqualTo(0.6).Within(1e-9));
        }

        [Test]
        public void TheStandBringsAnAudienceAndGold()
        {
            Assert.That(RanchBauEffekte.TribueneZuschauer(BauMit(), Bal), Is.EqualTo(0));
            Assert.That(RanchBauEffekte.TribueneZuschauer(BauMit(("tribuene", 1)), Bal), Is.EqualTo(6));
            Assert.That(RanchBauEffekte.TribueneZuschauer(BauMit(("tribuene", 2)), Bal), Is.EqualTo(12));
            Assert.That(RanchBauEffekte.HeimGoldMult(BauMit(("tribuene", 2)), Bal), Is.EqualTo(1.15).Within(1e-9));
        }

        [Test]
        public void StylePointsAreCapped()
        {
            var bau = RanchBauState.DefaultBau();
            Assert.That(RanchBauEffekte.DekoStilpunkte(bau, Bal), Is.EqualTo(0));
            for (var i = 0; i < 12; i++) ((JArray)bau["items"]).Add(new JObject { ["uid"] = "d" + i, ["item"] = "bank_holz", ["at"] = new JArray(i, 7), ["rot"] = 0 });
            Assert.That(RanchBauEffekte.DekoStilpunkte(bau, Bal), Is.EqualTo(2));
            for (var i = 0; i < 60; i++) ((JArray)bau["items"]).Add(new JObject { ["uid"] = "e" + i, ["item"] = "laterne", ["at"] = new JArray(i, 8), ["rot"] = 0 });
            Assert.That(RanchBauEffekte.DekoStilpunkte(bau, Bal), Is.EqualTo(10));
            var nurBoden = RanchBauState.DefaultBau();
            for (var i = 0; i < 10; i++) ((JArray)nurBoden["items"]).Add(new JObject { ["uid"] = "b" + i, ["item"] = "weg_schotter", ["at"] = new JArray(i, 7), ["rot"] = 0 });
            Assert.That(RanchBauEffekte.DekoStilpunkte(nurBoden, Bal), Is.EqualTo(0));
        }

        [Test]
        public void TheSummaryHasEveryValue()
        {
            var alles = RanchBauEffekte.Zusammenfassung(BauMit(("stallboxen", 4), ("weide", 3), ("wasserstelle", 3), ("heulager", 3), ("waschplatz", 3),
                ("fuehranlage", 3), ("reithalle", 3), ("parcours", 3), ("tribuene", 2), ("weidezaun", 3)), Bal);
            Assert.That(J.Int(alles["boxen_kapazitaet"]), Is.EqualTo(8));
            Assert.That(J.Int(alles["heu_kapazitaet"]), Is.EqualTo(48));
            Assert.That(J.IsTrue(alles["training_bei_regen"]), Is.True);
            Assert.That(J.Int(alles["fuehranlage_xp_pro_tag"]), Is.EqualTo(80));
            Assert.That(J.Int(alles["tribuene_zuschauer"]), Is.EqualTo(12));
            Assert.That(J.Num(alles["parcours_coin_mult"]), Is.EqualTo(1.1).Within(1e-9));
            Assert.That(J.Int(alles["laune_bonus_pro_h"]), Is.EqualTo(1));
        }

        // ── the economy balance ──

        private static double Grenze(string key) => RanchBauKatalog.GrindGrenze(Bal, key) * RanchBauKatalog.EinnahmenProStunde(Bal);

        [Test]
        public void TheIncomeModelIsPlausible()
        {
            var proStunde = RanchBauKatalog.EinnahmenProStunde(Bal);
            Assert.That(proStunde, Is.InRange(200, 1000));
        }

        [Test]
        public void DecorationGroundAndFencesAreImpulseBuys()
        {
            var limits = new Dictionary<string, double> { ["deko"] = Grenze("deko"), ["boden"] = Grenze("boden"), ["zaun"] = Grenze("zaun_kante") };
            foreach (var d in RanchBauKatalog.Defs(Bal).Values.Where(d => limits.ContainsKey(d.Kategorie)))
                Assert.That(d.Kosten, Is.LessThanOrEqualTo(limits[d.Kategorie]), d.Id);
        }

        [Test]
        public void FacilityLevelsStayReachable()
        {
            foreach (var id in RanchBauKatalog.Ids(Bal, "anlage"))
            {
                var stufen = RanchBauKatalog.StufenKosten(Bal, id);
                Assert.That(stufen.Length, Is.GreaterThanOrEqualTo(1), id);
                Assert.That(stufen[0], Is.LessThanOrEqualTo(Grenze("anlage_stufe1")), id + " level 1");
                for (var i = 0; i < stufen.Length; i++)
                {
                    Assert.That(stufen[i], Is.LessThanOrEqualTo(Grenze("anlage_stufe_max")), id + " level " + (i + 1));
                    if (i > 0) Assert.That(stufen[i], Is.GreaterThanOrEqualTo(stufen[i - 1]), id + ": prices rise");
                }
            }
        }

        [Test]
        public void ZonesStayReachable()
        {
            var zonen = RanchBauKatalog.Zonen(Bal);
            Assert.That(zonen.Select(z => z.Id), Is.EqualTo(new[] { "start", "nord", "ost" }));
            Assert.That(zonen[0].Kosten, Is.EqualTo(0));
            foreach (var z in zonen) Assert.That(z.Kosten, Is.LessThanOrEqualTo(Grenze("zone")), z.Id);
        }

        [Test]
        public void HorsePricesStayReachable()
        {
            Assert.That(RanchDorf.Pool.Length, Is.GreaterThanOrEqualTo(6));
            foreach (var p in RanchDorf.Pool) Assert.That(p.Preis, Is.InRange(1, (int)Grenze("pferd_teuer")), p.Id);
            Assert.That(RanchDorf.Pool.Min(p => p.Preis), Is.LessThanOrEqualTo(Grenze("pferd_guenstig")));
        }

        [Test]
        public void BuyingAndSellingNeverMakesMoney()
        {
            foreach (var w in RanchDorf.FutterWaren.Where(w => w.Id.StartsWith("heu")))
                Assert.That(RanchDorf.Ankauf["heu"], Is.LessThan(w.Preis / (double)Math.Max(1, w.Menge)), w.Id);
            Assert.That(RanchDorf.VerkaufAnteil, Is.GreaterThan(0.0).And.LessThan(1.0));
            Assert.That(RanchBauKatalog.AbrissErstattung(Bal), Is.GreaterThan(0.0).And.LessThan(1.0));
        }

        [Test]
        public void TheBaseHayStoreTakesTheHayBundle()
        {
            var basis = RanchBauEffekte.HeuKapazitaet(RanchBauState.DefaultBau(), Bal);
            var startHeu = J.Int(RanchHof.NormalizeWirtschaft(null)["lager"]["heu"]);
            var bund = RanchDorf.FutterWaren.First(w => w.Id == "heu_bund").Menge;
            Assert.That(startHeu + bund, Is.LessThanOrEqualTo(basis));
        }

        // ── the texts ──

        private static Dictionary<string, string> Flach(string locale)
        {
            var o = new Dictionary<string, string>();
            void Walk(string prefix, JObject node)
            {
                foreach (var p in node.Properties())
                {
                    var pfad = prefix.Length == 0 ? p.Name : prefix + "." + p.Name;
                    if (p.Value is JObject kind) Walk(pfad, kind);
                    else o[pfad] = J.Str(p.Value);
                }
            }
            Walk("", JObject.Parse(File.ReadAllText(Path.Combine(Fixtures.ResourcesRoot, "strings", locale, "ranch_bau.json"))));
            return o;
        }

        [Test]
        public void TextsMatchBetweenGermanAndEnglish()
        {
            var de = Flach("de");
            var en = Flach("en");
            Assert.That(de.Keys, Is.EquivalentTo(en.Keys));
            var ph = new Regex("\\{([A-Za-z0-9_]+)\\}");
            foreach (var key in de.Keys)
            {
                var a = ph.Matches(de[key]).Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
                var b = ph.Matches(en[key]).Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
                Assert.That(b, Is.EqualTo(a), key + ": placeholders");
                foreach (var text in new[] { de[key], en[key] })
                    Assert.That(text.All(c => c < 0x2600 || c > 0x27BF) && !text.Any(char.IsSurrogate), Is.True, key + ": no emoji");
            }
        }

        [Test]
        public void EveryBuildItemHasItsTexts()
        {
            var de = Flach("de");
            foreach (var d in RanchBauKatalog.Defs(Bal).Values) Assert.That(de.ContainsKey(d.NameKey), Is.True, d.NameKey);
            foreach (var z in RanchBauKatalog.Zonen(Bal)) Assert.That(de.ContainsKey("rbau.zone." + z.Id), Is.True, z.Id);
            foreach (var id in RanchBauKatalog.Ids(Bal, "anlage"))
            {
                Assert.That(de.ContainsKey("rbau.nutzen." + id), Is.True, "nutzen " + id);
                for (var i = 1; i <= RanchBauKatalog.MaxStufe(Bal, id); i++) Assert.That(de.ContainsKey("rbau.stufen." + id + "." + i), Is.True, id + " level " + i);
            }
            foreach (var code in new[] { RanchBauState.FehlerZuTeuer, RanchBauState.FehlerSchonGebaut, RanchBauState.FehlerNichtGebaut, RanchBauState.FehlerAusgebaut,
                RanchBauState.FehlerUnbekannt, RanchBauState.FehlerSchonFrei, RanchBauState.FehlerNurBelohnung, RanchGridData.ReasonOob, RanchGridData.ReasonGesperrt,
                RanchGridData.ReasonOccupied })
                Assert.That(de.ContainsKey("rbau.fehler." + code), Is.True, code);
            foreach (var id in RanchDorf.RanchDeko) Assert.That(RanchBauKatalog.Def(Bal, id)?.Kategorie, Is.EqualTo("deko"), id);
        }

        // ── the wiring into the farm ──

        [Test]
        public void WithoutABuildEverythingStaysAsBefore()
        {
            var s = Ranch(1000);
            Assert.That(RanchBauState.Vorhanden(s), Is.False);
            Assert.That(RanchWirtschaft.BoxenKapazitaet(s), Is.EqualTo(2));
            Assert.That(RanchBauEffekte.HeuKapazitaet(s), Is.EqualTo(int.MaxValue));
            Assert.That(RanchWirtschaft.WeideSauberkeitMult(s), Is.EqualTo(1.0));
            Assert.That(RanchBauEffekte.HeimGoldMult(s), Is.EqualTo(1.0));
            Assert.That(RanchBauEffekte.FuehranlageTag(s, Tag, Mittag), Is.Empty);
        }

        [Test]
        public void TheCapacityTakesTheBigger()
        {
            var s = Ranch(5000);
            s["ranch"]["wirtschaft"]["ausbau"]["boxen"] = 3;
            Assert.That(RanchWirtschaft.BoxenKapazitaet(s), Is.EqualTo(6));
            Assert.That(RanchBauState.MigriereBestand(s), Is.True);
            Assert.That(RanchWirtschaft.BoxenKapazitaet(s), Is.EqualTo(6));
            Assert.That(RanchBauState.Ausbauen(s, "stallboxen").Ok, Is.True);
            Assert.That(RanchWirtschaft.BoxenKapazitaet(s), Is.EqualTo(8));
            var uid = J.Str(((JArray)RanchBauState.Lese(s)["items"]).OfType<JObject>().First(e => J.Str(e["item"]) == "stallboxen")["uid"]);
            Assert.That(RanchBauState.Entferne(s, uid).Ok, Is.True);
            Assert.That(RanchWirtschaft.BoxenKapazitaet(s), Is.EqualTo(6), "the old boxes never get lost");
            Assert.That(DorfHaendler.Kapazitaet(s), Is.EqualTo(6));
        }

        [Test]
        public void TheHayStoreLimitsBuyingHay()
        {
            var s = Ranch(1000);
            RanchBauState.MigriereBestand(s);
            Assert.That(J.Int(s["ranch"]["wirtschaft"]["lager"]["heu"]), Is.EqualTo(4));
            Assert.That(RanchWirtschaft.HeuKaufen(s, 6), Is.EqualTo(RanchWirtschaft.Ok));
            var coins = Coins(s);
            Assert.That(RanchWirtschaft.HeuKaufen(s, 1), Is.EqualTo(RanchWirtschaft.LagerVoll));
            Assert.That(DorfWirtschaft.FutterKaufen(s, "heu_bund").code, Is.EqualTo(DorfWirtschaft.LagerVoll));
            Assert.That(Coins(s), Is.EqualTo(coins), "a full store costs nothing");
            Assert.That(RanchBauState.Platziere(s, "heulager", new Zelle(5, 12), 0).Ok, Is.True);
            Assert.That(RanchWirtschaft.HeuKaufen(s, 2), Is.EqualTo(RanchWirtschaft.Ok), "12 bales now");
            Assert.That(DorfWirtschaft.FutterKaufen(s, "hafer").code, Is.EqualTo(DorfWirtschaft.Ok), "oats are not hay");
        }

        [Test]
        public void TheWashPlaceBrushesBetterAndWarmsOnceADay()
        {
            var s = Ranch(5000);
            RanchBauState.MigriereBestand(s);
            var id = RanchHof.PferdIds(s)[0];
            s["ranch"]["tiere"]["pferde"][id]["werte"]["sauberkeit"] = 10.0;
            var ohne = RanchPflege.Pflegen(s, id, RanchPflege.Striegeln, Mittag, Tag);
            Assert.That(J.Num(ohne.Werte["sauberkeit"]), Is.EqualTo(45.0));
            Assert.That(ohne.Warmwasser, Is.EqualTo(0));
            Assert.That(RanchBauState.Platziere(s, "waschplatz", new Zelle(5, 12), 0).Ok, Is.True);
            Assert.That(RanchBauState.Ausbauen(s, "waschplatz").Ok, Is.True);
            var bindung = J.Num(RanchHof.Pferd(s, id)["bindung"]);
            var mit = RanchPflege.Pflegen(s, id, RanchPflege.Striegeln, Mittag + 1000, Tag);
            Assert.That(J.Num(mit.Werte["sauberkeit"]), Is.EqualTo(95.0));
            Assert.That(mit.Warmwasser, Is.EqualTo(2));
            Assert.That(J.Num(RanchHof.Pferd(s, id)["bindung"]), Is.EqualTo(bindung + 3.0 + 2.0).Within(1e-9), "brushing 3 + warm water 2");
            Assert.That(RanchPflege.Pflegen(s, id, RanchPflege.Striegeln, Mittag + 2000, Tag).Warmwasser, Is.EqualTo(0), "once a day");
            Assert.That(RanchPflege.Pflegen(s, id, RanchPflege.Striegeln, Mittag + 86_400_000L, "2026-10-12").Warmwasser, Is.EqualTo(2), "a new day");
        }

        [Test]
        public void ThePastureFacilitiesWorkWhileTheHorsesGraze()
        {
            var tiere = new JObject
            {
                ["lastTickAt"] = Mittag,
                ["pferde"] = new JObject { ["p"] = new JObject { ["werte"] = new JObject { ["hunger"] = 95.0, ["durst"] = 52.0, ["sauberkeit"] = 30.0 } } },
            };
            var bis = Mittag + 4L * 3_600_000L;
            var ohne = J.Dict(RanchOffline.Simulate(tiere, bis).Tiere["pferde"]["p"]["werte"]);
            var boni = new RanchBauEffekte.Weide { Hunger = 0.85, Durst = 0.7, DurstBoden = 50.0, KraeuterProH = 1.0 };
            var mit = J.Dict(RanchOffline.Simulate(tiere, bis, 0, 1.0, boni).Tiere["pferde"]["p"]["werte"]);
            Assert.That(J.Num(ohne["hunger"]), Is.EqualTo(95.0 - 0.25 * 240 * 0.3).Within(1e-9));
            Assert.That(J.Num(mit["hunger"]), Is.EqualTo(95.0 - 0.25 * 240 * 0.3 * 0.85).Within(1e-9), "lush grass; above the herbs' cap");
            Assert.That(J.Num(ohne["durst"]), Is.LessThan(50.0));
            Assert.That(J.Num(mit["durst"]), Is.EqualTo(50.0), "the self-drinker");
            Assert.That(J.Num(mit["sauberkeit"]), Is.EqualTo(J.Num(ohne["sauberkeit"]) + 4.0).Within(1e-9), "four hours of herbs");
        }

        [Test]
        public void TheWalkerTrainsOnceADay()
        {
            var s = Ranch(9000);
            RanchBauState.MigriereBestand(s);
            Assert.That(RanchBauState.Platziere(s, "fuehranlage", new Zelle(5, 12), 0).Ok, Is.True);
            Assert.That(RanchBauState.Ausbauen(s, "fuehranlage").Ok, Is.True);
            var ids = RanchHof.PferdIds(s);
            s["ranch"]["tiere"]["pferde"][ids[0]]["xp"] = RanchPferd.XpSummeBis(5);
            s["ranch"]["tiere"]["pferde"][ids[0]]["level"] = 5;
            var training = RanchBauEffekte.FuehranlageTag(s, Tag, Mittag);
            Assert.That(training.Count, Is.EqualTo(2), "level 2: two places");
            Assert.That(training.Select(t => t.PferdId), Does.Not.Contain(ids[0]), "the lowest levels first");
            Assert.That(training.All(t => t.Buchung.Gewinn == 60.0), Is.True);
            Assert.That(J.Num(RanchHof.Pferd(s, training[0].PferdId)["xp"]), Is.EqualTo(60.0));
            Assert.That(RanchBauEffekte.FuehranlageTag(s, Tag, Mittag + 1000), Is.Empty, "once a day");
            Assert.That(RanchBauEffekte.FuehranlageTag(s, "2026-10-12", Mittag + 86_400_000L).Count, Is.EqualTo(2));
        }

        [Test]
        public void TheBreedingStableAndTheFoalPasture()
        {
            var zucht = JObject.Parse("{\"traechtigkeiten\":{\"a\":{},\"b\":{}},\"ruhezeitBis\":{}}");
            var pferd = JObject.Parse("{\"level\":10,\"bindung\":80}");
            Assert.That(RanchZucht.ZuchtErlaubt("c", pferd, pferd, 90.0, zucht, Mittag).fehler, Is.EqualTo("voll"));
            Assert.That(RanchZucht.ZuchtErlaubt("c", pferd, pferd, 90.0, zucht, Mittag, 1).ok, Is.True);
            var s = Ranch(9000);
            RanchBauState.MigriereBestand(s);
            Assert.That(RanchBauEffekte.Traechtigkeiten(s), Is.EqualTo(0));
            Assert.That(RanchBauState.Platziere(s, "zuchtstall", new Zelle(5, 12), 0).Ok, Is.True);
            Assert.That(RanchBauEffekte.Traechtigkeiten(s), Is.EqualTo(1));
            var geboren = Mittag - (long)(2.7 * 86_400_000L);
            Assert.That(RanchZucht.AlterFuer(geboren, Mittag), Is.EqualTo("fohlen"));
            Assert.That(RanchBauEffekte.AufzuchtTempo(s), Is.EqualTo(1.0));
            Assert.That(RanchBauState.Platziere(s, "fohlenweide", new Zelle(1, 12), 0).Ok, Is.True);
            Assert.That(RanchBauEffekte.AufzuchtTempo(s), Is.EqualTo(1.15).Within(1e-9));
            Assert.That(RanchZucht.AlterFuer(geboren, Mittag, RanchBauEffekte.AufzuchtTempo(s)), Is.EqualTo("jaehrling"));
            Assert.That(RanchZucht.TageBisNaechstePhase(geboren, Mittag, 1.15), Is.EqualTo((7.0 - 2.7 * 1.15) / 1.15).Within(1e-6));
        }

        [Test]
        public void TournamentsGearAndTheParcours()
        {
            var s = Ranch(20000);
            RanchBauState.MigriereBestand(s);
            Assert.That(RanchBauEffekte.TurnierXpMult(s, "dressur"), Is.EqualTo(1.0));
            Assert.That(RanchBauState.Platziere(s, "reithalle", new Zelle(6, 11), 0).Fehler, Is.EqualTo(RanchGridData.ReasonGesperrt), "5×4 reaches into the east field");
            Assert.That(RanchBauState.Platziere(s, "reithalle", new Zelle(5, 11), 0).Ok, Is.True);
            Assert.That(RanchBauEffekte.TurnierXpMult(s, "dressur"), Is.EqualTo(1.2).Within(1e-9));
            Assert.That(RanchBauEffekte.TurnierXpMult(s, "rennen"), Is.EqualTo(1.0));
            Assert.That(RanchBauState.ZoneFreischalten(s, "ost").Ok, Is.True);
            Assert.That(RanchBauEffekte.TribueneZuschauer(s), Is.EqualTo(0));
            Assert.That(RanchBauState.Platziere(s, "tribuene", new Zelle(10, 0), 0).Ok, Is.True);
            Assert.That(RanchBauEffekte.HeimGoldMult(s), Is.EqualTo(1.0), "level 1: an audience, no bonus yet");
            Assert.That(RanchBauEffekte.TribueneZuschauer(s), Is.EqualTo(6), "six fans come along to the tournament");
            Assert.That(RanchBauState.Ausbauen(s, "tribuene").Ok, Is.True);
            Assert.That(RanchBauEffekte.HeimGoldMult(s), Is.EqualTo(1.15).Within(1e-9));
            Assert.That(RanchBauEffekte.TribueneZuschauer(s), Is.EqualTo(12));
            var lohn = RanchCompTurnier.Belohnung(RanchCompKatalog.LoadBalance(), "springen", "holz", 1, false, RanchBauEffekte.HeimGoldMult(s));
            var basis = RanchCompTurnier.Belohnung(RanchCompKatalog.LoadBalance(), "springen", "holz", 1, false);
            Assert.That(lohn.Gold, Is.GreaterThan(basis.Gold));
            Assert.That(RanchWirtschaft.GearPreisFuer(s, "sattel", "rot"), Is.EqualTo(120));
            Assert.That(RanchBauState.Platziere(s, "sattelkammer", new Zelle(12, 4), 0).Ok, Is.True);
            Assert.That(RanchWirtschaft.GearPreisFuer(s, "sattel", "rot"), Is.EqualTo(108));
            var coins = Coins(s);
            Assert.That(RanchWirtschaft.GearKaufen(s, "sattel_rot"), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(Coins(s), Is.EqualTo(coins - 108));
            Assert.That(RanchBauEffekte.ParcoursCoinMult(s, 1.0), Is.EqualTo(1.0));
            Assert.That(RanchBauState.Platziere(s, "parcours", new Zelle(0, 11), 0).Ok, Is.True);
            Assert.That(RanchBauEffekte.ParcoursCoinMult(s, 1.0), Is.EqualTo(1.1).Within(1e-9));
            Assert.That(RanchBauEffekte.TurnierXpMult(s, "springen"), Is.EqualTo(1.0), "level 1 jumps give no XP bonus yet");
            Assert.That(RanchBauState.Ausbauen(s, "parcours").Ok, Is.True);
            Assert.That(RanchBauEffekte.TurnierXpMult(s, "springen"), Is.EqualTo(1.2).Within(1e-9));
        }

        [Test]
        public void TheFurnitureBarnSellsDecorationIntoTheStore()
        {
            var s = Ranch(500);
            var coins = Coins(s);
            var res = DorfWirtschaft.DekoKaufen(s, "bank_holz");
            Assert.That((res.code, res.preis), Is.EqualTo((DorfWirtschaft.Ok, 80)));
            Assert.That(Coins(s), Is.EqualTo(coins - 80));
            Assert.That(RanchBauState.LagerAnzahl(RanchBauState.Lese(s), "bank_holz"), Is.EqualTo(1));
            Assert.That(DorfWirtschaft.DekoKaufen(s, "heuballen_deko").code, Is.EqualTo(DorfWirtschaft.Unbekannt), "not on the barn's shelf");
            Assert.That(DorfWirtschaft.DekoKaufen(s, "stallboxen").code, Is.EqualTo(DorfWirtschaft.Unbekannt));
            var platz = RanchBauState.Platziere(s, "bank_holz", new Zelle(4, 14), 0);
            Assert.That((platz.Ok, platz.Kosten), Is.EqualTo((true, 0)), "set up for free");
        }
    }
}
