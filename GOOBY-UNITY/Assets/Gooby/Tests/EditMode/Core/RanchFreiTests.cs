using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Ranch;
using Gooby.Core.Minigames.Ranch.Mp;
using Gooby.Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Ranch visit and trail ride (ranchmp.js besuch/ausritt) through the open region: the ranch metadata for a visit
    /// (port of rmp_ranch_meta.gd: build, validate, heal — with the host's build grid), and the free ride — start on the
    /// yard ring, no checkpoints, zones discovered once, water stops the horse, the bot's trail along the path and home.
    /// </summary>
    public class RanchFreiTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            RanchKarte.ResetCache();
            RanchBauKatalog.ResetCache();
        }

        private static JObject RanchState() => JObject.Parse(@"{
            ""ranch"": {
                ""wirtschaft"": { ""ausbau"": { ""boxen"": 7, ""reitplatz"": true, ""weidezaun"": ""ja"" } },
                ""tiere"": { ""pferde"": {
                    ""p1"": { ""name"": ""Sternchen mit dem sehr langen Namen"", ""rasse"": ""fjordi"", ""level"": 140, ""farbe"": ""palomino"" },
                    ""p2"": { ""name"": ""Kekse"", ""level"": 3 },
                    ""kaputt"": 7 } },
                ""comp"": { ""trophaeen"": [""gold_springen"", 5, ""silber_dressur""], ""schleifen"": { ""springen"": 1, ""trail"": 2 } }
            } }");

        [Test]
        public void MetaIsBuiltFromTheRanchAndClamped()
        {
            var meta = RmpRanchMeta.Build(RanchState(), "Alice", "Flausch");
            Assert.That(((string)meta["name"], (string)meta["goobyName"]), Is.EqualTo(("Alice", "Flausch")));
            Assert.That((int)meta["ausbau"]["boxen"], Is.EqualTo(3), "1..3 stalls");
            Assert.That(((bool)meta["ausbau"]["reitplatz"], (bool)meta["ausbau"]["weidezaun"]), Is.EqualTo((true, false)), "only true booleans count");
            var pferde = (JArray)meta["pferde"];
            Assert.That(pferde.Count, Is.EqualTo(2), "broken horse entries are skipped");
            Assert.That(((string)pferde[0]["name"]).Length, Is.EqualTo(RmpRanchMeta.MaxNameLen));
            Assert.That((int)pferde[0]["level"], Is.EqualTo(99));
            Assert.That(((string)pferde[1]["rasse"], (string)pferde[1]["farbe"]), Is.EqualTo(("puschelhufer", "braun")), "defaults");
            Assert.That(meta["trophaeen"].Select(t => (string)t), Is.EqualTo(new[] { "gold_springen", "silber_dressur" }));
            Assert.That((int)meta["schleifen"], Is.EqualTo(2));
            Assert.That(RmpRanchMeta.Validate(meta).ok, Is.True);
            var empty = RmpRanchMeta.Build(new JObject(), "", "");
            Assert.That(((string)empty["name"], ((JArray)empty["pferde"]).Count, (int)empty["ausbau"]["boxen"]), Is.EqualTo(("Gooby-Fan", 0, 1)), "a fresh save is a small ranch");
        }

        [Test]
        public void ForeignMetaIsValidatedAndHealed()
        {
            Assert.That(RmpRanchMeta.Validate(new JArray()).reason, Is.EqualTo("BAD_META"));
            Assert.That(RmpRanchMeta.Validate(new JObject { ["pferde"] = "nein" }).reason, Is.EqualTo("BAD_META"));
            Assert.That(RmpRanchMeta.Validate(new JObject { ["pferde"] = new JArray(Enumerable.Range(0, 13).Select(i => new JObject())) }).reason, Is.EqualTo("TOO_MANY_HORSES"));
            Assert.That(RmpRanchMeta.Validate(new JObject { ["pferde"] = new JArray(), ["x"] = new string('a', 17000) }).reason, Is.EqualTo("TOO_LARGE"));
            var healed = RmpRanchMeta.Normalize(JObject.Parse(@"{""name"":42,""ausbau"":{""boxen"":0},""pferde"":[{""name"":""Wolke"",""level"":""hoch""},""quatsch""],""trophaeen"":[1,""t1""],""schleifen"":-4}"));
            Assert.That((string)healed["name"], Is.EqualTo("?"));
            Assert.That((int)healed["ausbau"]["boxen"], Is.EqualTo(1));
            Assert.That(((JArray)healed["pferde"]).Count, Is.EqualTo(1));
            Assert.That(((string)healed["pferde"][0]["name"], (int)healed["pferde"][0]["level"]), Is.EqualTo(("Wolke", 1)));
            Assert.That(healed["trophaeen"].Select(t => (string)t), Is.EqualTo(new[] { "t1" }));
            Assert.That((int)healed["schleifen"], Is.EqualTo(0));
            Assert.That(RmpRanchMeta.Normalize(null)["pferde"], Is.Empty);
        }

        [Test]
        public void TheFreeRideStartsOnTheYardRingInTheRegion()
        {
            Assert.That(RmpFrei.OrtBei(RmpFrei.HofMitte), Is.EqualTo("hof"), "the yard lies in the farm zone");
            Assert.That(RmpFrei.OrtBei(RmpFrei.StartPosition(0, 2)), Is.EqualTo("hof"), "riders start in the yard");
            Assert.That(RmpFrei.OrtBei(new V2(0f, 250f)), Is.EqualTo(""), "the open land between the zones");
            Assert.That(RmpFrei.OrtBei(new V2(120f, 380f)), Is.EqualTo("turnierplatz"));
            var grenzen = RanchKarte.Grenzen;
            Assert.That((double)(RmpFrei.FeldMitte.X - RmpFrei.FeldHalb.X), Is.EqualTo(grenzen.X), "the region's west edge");
            Assert.That((double)(RmpFrei.FeldMitte.X + RmpFrei.FeldHalb.X), Is.EqualTo(grenzen.EndX), "the region's east edge");
            Assert.That((double)(RmpFrei.FeldMitte.Y - RmpFrei.FeldHalb.Y), Is.EqualTo(grenzen.Z), "the region's north edge");
            Assert.That((double)(RmpFrei.FeldMitte.Y + RmpFrei.FeldHalb.Y), Is.EqualTo(grenzen.EndZ), "the region's south edge");
            Assert.That((RmpFrei.StartPosition(0, 4) - RmpFrei.StartPosition(1, 4)).Length, Is.GreaterThan(4.0), "start places apart");
            for (var i = 0; i < 4; i++)
            {
                Assert.That((RmpFrei.StartPosition(i, 4) - RmpFrei.HofMitte).Length, Is.LessThan(RmpFrei.HofRingRadius - 2.0), "inside the yard ring");
                Assert.That(RmpFrei.Begehbar(RmpFrei.StartPosition(i, 4)), Is.True);
            }
            for (var i = 0; i < 8; i++)
            {
                var pferd = RmpFrei.BesuchPferdPos(i, 8);
                Assert.That(pferd.Y, Is.LessThan(RmpFrei.HofMitte.Y), "a visit's horses stand on the north side, facing the riders");
                Assert.That((pferd - RmpFrei.HofMitte).Length, Is.LessThan(RmpFrei.HofRingRadius));
            }
            Assert.That(RmpKurse.MenuModi, Is.EqualTo(new[] { "rennen", "parcours", "fangen", "ausritt", "besuch" }));
            Assert.That(RmpKurse.IstFrei("ausritt") && RmpKurse.IstFrei("besuch") && !RmpKurse.IstFrei("rennen"), Is.True);
        }

        [Test]
        public void RidersStartLookingAcrossTheYardToTheFarm()
        {
            var players = new List<RmpPlayer> { new RmpPlayer { Code = "GOOBY-AAAA", Name = "Alice" } };
            var lauf = new RmpLauf(RmpKurse.Besuch, "", players, "GOOBY-AAAA");
            var start = lauf.Controller.Pos;
            Assert.That(start.Y, Is.GreaterThan(RmpFrei.HofMitte.Y), "the south half of the ring (north = −z)");
            lauf.Controller.GaitUp();
            for (var i = 0; i < 60; i++) lauf.Tick(1.0 / 60.0, true);
            Assert.That(lauf.Controller.Pos.Y, Is.LessThan(start.Y - 0.5), "riding off goes north, toward the horses and the farm");
            Assert.That((lauf.Controller.Pos - RmpFrei.HofMitte).Length, Is.LessThan((start - RmpFrei.HofMitte).Length), "across the ring");
        }

        [Test]
        public void DeepWaterStopsTheHorse()
        {
            var players = new List<RmpPlayer> { new RmpPlayer { Code = "GOOBY-AAAA", Name = "Alice" } };
            var lauf = new RmpLauf(RmpKurse.Ausritt, "", players, "GOOBY-AAAA");
            V2? ufer = null;
            for (var x = 300.0; x < 700.0 && ufer == null; x += 2.0)
                if (!RmpFrei.Begehbar(new V2(x, 260.0))) ufer = new V2(x - 6.0, 260.0);
            Assert.That(ufer, Is.Not.Null, "the lake lies east of the farm");
            lauf.Controller.Pos = ufer.Value;
            lauf.Controller.Heading = RmpKurse.HeadingTo(ufer.Value, ufer.Value + new V2(10f, 0f));
            lauf.Controller.SetGait("galopp");
            for (var i = 0; i < 60 * 4; i++) lauf.Tick(1.0 / 60.0, true);
            Assert.That(RmpFrei.Begehbar(lauf.Controller.Pos), Is.True, "the horse stops at the shore");
            Assert.That(lauf.Controller.Pos.X, Is.LessThan(ufer.Value.X + 8.0));
        }

        [Test]
        public void TheFreeRideDiscoversTheZonesOnItsTrailAndRidesHome()
        {
            var players = new List<RmpPlayer> { new RmpPlayer { Code = "GOOBY-AAAA", Name = "Alice" }, new RmpPlayer { Code = "GOOBY-BBBB", Name = "Bob" } };
            var lauf = new RmpLauf(RmpKurse.Ausritt, "", players, "GOOBY-AAAA");
            Assert.That(lauf.IsFree && !lauf.IsRace, Is.True);
            Assert.That(lauf.CheckpointCount, Is.EqualTo(0));
            Assert.That(lauf.Entdeckt, Is.EquivalentTo(new[] { "hof" }), "the farm is known from the start");
            var found = new List<string>();
            lauf.OrtEntdeckt += found.Add;
            var checkpoints = 0;
            lauf.CheckpointDue += _ => checkpoints++;
            lauf.FinishDue += () => checkpoints++;
            for (var i = 0; i < 60 * 120 && found.Count < RmpFrei.BotZiele.Length; i++)
            {
                RmpBot.Drive(lauf, "", i * 16L);
                lauf.Tick(1.0 / 60.0, true);
            }
            Assert.That(found, Is.EqualTo(RmpFrei.BotZiele), "the trail reaches the tournament grounds along the path");
            Assert.That(checkpoints, Is.EqualTo(0), "no checkpoints on a free ride");
            Assert.That(lauf.TagTarget(0), Is.Null);
            for (var i = 0; i < 60 * 120; i++)
            {
                RmpBot.Drive(lauf, "", i * 16L);
                lauf.Tick(1.0 / 60.0, true);
            }
            Assert.That((lauf.Controller.Pos - RmpFrei.HofMitte).Length, Is.LessThan(RmpFrei.HofRingRadius + 8.0), "afterwards the bot rides home to the yard");
            Assert.That(found.Distinct().Count(), Is.EqualTo(found.Count), "each zone once");
        }

        [Test]
        public void AVisitCarriesTheHostsBuild()
        {
            var state = RanchState();
            Assert.That(RmpRanchMeta.Build(state, "Alice", "Flausch")["bau"], Is.Null, "no build grid yet, nothing to show");
            state["economy"] = new JObject { ["coins"] = 5000 };
            Assert.That(RanchBauState.MigriereBestand(state), Is.True);
            Assert.That(RanchBauState.Platziere(state, "laterne", new Zelle(4, 8), 0).Ok, Is.True);
            Assert.That(RanchBauState.PlatziereKante(state, "zaun_holz", new Zelle(3, 8), "S").Ok, Is.True);
            Assert.That(RanchBauState.Ausbauen(state, "weide").Ok, Is.True);
            var meta = RmpRanchMeta.Build(state, "Alice", "Flausch");
            Assert.That(RmpRanchMeta.Validate(meta).ok, Is.True);
            var gast = RmpRanchMeta.BauFuerGast(RmpRanchMeta.Normalize(JObject.Parse(meta.ToString())));
            Assert.That(gast, Is.Not.Null);
            var grid = RanchBauState.GridAus(gast).grid;
            Assert.That(grid.Items.Count, Is.EqualTo(RanchBauState.GridVon(state).grid.Items.Count), "every piece comes over");
            Assert.That(grid.ItemAt(new Zelle(4, 8), RanchGridData.Layer.Objekt), Is.Not.Empty, "the lantern");
            Assert.That(grid.KanteItemAt(new Zelle(3, 9), "N"), Is.Not.Empty, "the fence on its edge");
            Assert.That(RanchBauState.AnlageStufe(gast, "stallboxen"), Is.EqualTo(RanchBauState.AnlageStufe(RanchBauState.Lese(state), "stallboxen")), "the levels");

            var boese = JObject.Parse(@"{""pferde"":[],""bau"":{""teile"":[[""laterne"",99,2,0],[""weg_sand"",1,""x"",0],[""zaun_holz"",3,9,""Q""],[""bank_holz"",2,9,7]],""stufen"":{""weide"":44,""x"":""hoch""},""zonen"":[3,""nord""]}}");
            var geheilt = RmpRanchMeta.Normalize(boese);
            Assert.That(((JArray)geheilt["bau"]["teile"]).Count, Is.EqualTo(1), "only the sound piece stays");
            Assert.That((int)geheilt["bau"]["teile"][0][3], Is.EqualTo(3), "turns clamp to 0..3");
            Assert.That((int)geheilt["bau"]["stufen"]["weide"], Is.EqualTo(9));
            Assert.That(geheilt["bau"]["zonen"].Select(t => (string)t), Is.EqualTo(new[] { "start", "nord" }));
        }
    }
}
