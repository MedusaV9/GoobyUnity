using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames;
using Gooby.Core.Minigames.Ranch;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// The ranch competitions (RW-5) — ports of test_rcomp_wertung/ghost/state/liga/bots/turnier.gd and the stat part of
    /// test_rpferd_ride.gd, plus a cross-check against values printed by the Godot modules themselves
    /// (Fixtures/rcomp_golden.json: routes, courses, bot fields, simulated runs, tournament-day plans, a ghost recording).
    /// </summary>
    public class RanchCompTests
    {
        private JObject _bal;
        private JObject _golden;

        [OneTimeSetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            _bal = RanchCompKatalog.LoadBalance();
            _golden = Fixtures.LoadObject("rcomp_golden.json");
        }

        private static Dictionary<string, double> Stats(params (string k, double v)[] werte) => werte.ToDictionary(w => w.k, w => w.v);

        // ── wertung (test_rcomp_wertung.gd) ──

        [Test]
        public void SpringenDocFormelSterneUndKurs()
        {
            Assert.That(RcompWertung.Springen.Score(0, 0, 60.0, 60.0, 0), Is.EqualTo(1000));
            Assert.That(RcompWertung.Springen.Score(1, 0, 60.0, 60.0, 0), Is.EqualTo(960));
            Assert.That(RcompWertung.Springen.Score(0, 1, 60.0, 60.0, 0), Is.EqualTo(980));
            Assert.That(RcompWertung.Springen.Score(0, 0, 70.0, 60.0, 0), Is.EqualTo(950));
            Assert.That(RcompWertung.Springen.Score(0, 0, 55.0, 60.0, 0), Is.EqualTo(1000));
            Assert.That(RcompWertung.Springen.Score(0, 0, 60.0, 60.0, 3), Is.EqualTo(1045));
            Assert.That(RcompWertung.Springen.Score(2, 1, 75.0, 60.0, 2), Is.EqualTo(855));
            Assert.That(RcompWertung.Springen.Score(30, 10, 300.0, 60.0, 0), Is.EqualTo(0));
            Assert.That(RcompWertung.Springen.Sterne(900), Is.EqualTo(3));
            Assert.That(RcompWertung.Springen.Sterne(899), Is.EqualTo(2));
            Assert.That(RcompWertung.Springen.Sterne(750), Is.EqualTo(2));
            Assert.That(RcompWertung.Springen.Sterne(549), Is.EqualTo(0));
            Assert.That(RcompWertung.Springen.HindernisAnzahl("holz"), Is.EqualTo(8));
            Assert.That(RcompWertung.Springen.HindernisAnzahl("sternenklasse"), Is.EqualTo(14));
        }

        [Test]
        public void DressurDocFormelUndTakt()
        {
            Assert.That(RcompWertung.Dressur.FigurPunkte(0.0, 0), Is.EqualTo(100.0).Within(1e-9));
            Assert.That(RcompWertung.Dressur.FigurPunkte(0.75, 0), Is.EqualTo(50.0).Within(1e-6));
            Assert.That(RcompWertung.Dressur.FigurPunkte(0.0, 1), Is.EqualTo(75.0).Within(1e-6));
            Assert.That(RcompWertung.Dressur.FigurPunkte(0.3, 1), Is.EqualTo(55.0).Within(1e-6));
            Assert.That(RcompWertung.Dressur.FigurPunkte(3.0, 2), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(RcompWertung.Dressur.Gesamt(new[] { 100.0, 100.0, 100.0, 100.0, 100.0 }, true), Is.EqualTo(110.0).Within(1e-9));
            Assert.That(RcompWertung.Dressur.Gesamt(new[] { 80.0, 60.0 }, false), Is.EqualTo(70.0).Within(1e-9));
            Assert.That(RcompWertung.Dressur.Gesamt(new double[0], true), Is.EqualTo(0.0));
            Assert.That(RcompWertung.Dressur.TaktOk(new[] { 0.0, -250.0, 250.0 }), Is.True);
            Assert.That(RcompWertung.Dressur.TaktOk(new[] { 0.0, 251.0 }), Is.False);
            Assert.That(RcompWertung.Dressur.TaktOk(new double[0]), Is.True);
            Assert.That(RcompWertung.Dressur.Figuren.Length, Is.EqualTo(5));
        }

        [Test]
        public void GelaendeRennenTrailSchauTonnen()
        {
            Assert.That(RcompWertung.Gelaende.WertungS(100.0, 2), Is.EqualTo(116.0).Within(1e-9));
            Assert.That(RcompWertung.Gelaende.Sterne(100.0, 100.0), Is.EqualTo(3));
            Assert.That(RcompWertung.Gelaende.Sterne(110.0, 100.0), Is.EqualTo(2));
            Assert.That(RcompWertung.Gelaende.Sterne(125.0, 100.0), Is.EqualTo(1));
            Assert.That(RcompWertung.Gelaende.Sterne(126.0, 100.0), Is.EqualTo(0));
            Assert.That(RcompWertung.Gelaende.TorAnzahl("sternenklasse"), Is.EqualTo(15));

            var sortiert = RcompWertung.Rennen.Reihenfolge(new[] { ("a", 81.0), ("b", 79.5), ("dnf", 0.0), ("c", 80.0) });
            Assert.That(sortiert[0].id, Is.EqualTo("b"));
            Assert.That(sortiert[1].id, Is.EqualTo("c"));
            Assert.That(sortiert[3].id, Is.EqualTo("dnf"), "DNF goes last");
            Assert.That(RcompWertung.Rennen.PlatzVon(sortiert, "a"), Is.EqualTo(3));
            Assert.That(RcompWertung.Rennen.PlatzVon(sortiert, "nix"), Is.EqualTo(0));
            var z = new RcompWertung.Rennen.Zustand();
            z = RcompWertung.Rennen.StepWindschatten(z, true, 0.5);
            Assert.That(RcompWertung.Rennen.TempoMult(z), Is.EqualTo(1.0));
            z = RcompWertung.Rennen.StepWindschatten(z, true, 0.6);
            Assert.That(RcompWertung.Rennen.TempoMult(z), Is.EqualTo(1.03).Within(1e-9), "+3 % after 1 s");
            z = RcompWertung.Rennen.StepWindschatten(z, false, 2.9);
            Assert.That(RcompWertung.Rennen.TempoMult(z), Is.EqualTo(1.03).Within(1e-9), "the boost holds 3 s");
            z = RcompWertung.Rennen.StepWindschatten(z, false, 0.2);
            Assert.That(RcompWertung.Rennen.TempoMult(z), Is.EqualTo(1.0));
            Assert.That(RcompWertung.Rennen.ImFenster(1.5, 0.5), Is.True);
            Assert.That(RcompWertung.Rennen.ImFenster(2.5, 0.0), Is.False);
            Assert.That(RcompWertung.Rennen.ImFenster(1.0, 2.0), Is.False);
            Assert.That(RcompWertung.Rennen.ImFenster(-0.5, 0.0), Is.False);

            Assert.That(RcompWertung.Trail.AufgabePunkte(1, false), Is.EqualTo(8));
            Assert.That(RcompWertung.Trail.AufgabePunkte(6, false), Is.EqualTo(0));
            Assert.That(RcompWertung.Trail.AufgabePunkte(0, true), Is.EqualTo(0));
            Assert.That(RcompWertung.Trail.Zeitbonus(90.0), Is.EqualTo(20));
            Assert.That(RcompWertung.Trail.Zeitbonus(95.5), Is.EqualTo(14));
            Assert.That(RcompWertung.Trail.Zeitbonus(200.0), Is.EqualTo(0));
            Assert.That(RcompWertung.Trail.Gesamt(new[] { 10, 10, 10, 10, 10, 10 }, 80.0), Is.EqualTo(80));
            Assert.That(RcompWertung.Trail.Gesamt(new[] { 8, 10, 6, 0, 10, 10 }, 100.0), Is.EqualTo(54));
            Assert.That(RcompWertung.Trail.Sterne(70), Is.EqualTo(3));
            Assert.That(RcompWertung.Trail.Sterne(39), Is.EqualTo(0));

            Assert.That(RcompWertung.Schau.Pflege(80.0, 60.0), Is.EqualTo(70.0).Within(1e-9));
            Assert.That(RcompWertung.Schau.Pflege(120.0, -10.0), Is.EqualTo(50.0).Within(1e-9));
            Assert.That(RcompWertung.Schau.Stil(new[] { "legendaer", "episch", "selten" }, true, true), Is.EqualTo(59.0).Within(1e-9));
            Assert.That(RcompWertung.Schau.Stil(new string[0], false, false, 1.1), Is.EqualTo(0.0));
            Assert.That(RcompWertung.Schau.KuerTreffer(-300.0), Is.True);
            Assert.That(RcompWertung.Schau.KuerTreffer(301.0), Is.False);
            Assert.That(RcompWertung.Schau.Kuer(5), Is.EqualTo(100.0));
            Assert.That(RcompWertung.Schau.Gesamt(70.0, 59.0, 60.0), Is.EqualTo(63.7).Within(1e-6));

            Assert.That(RcompWertung.Tonnen.WertungS(20.0, 1), Is.EqualTo(25.0).Within(1e-9));
            Assert.That(RcompWertung.Tonnen.Idealzeit("holz"), Is.EqualTo(24.0));
            Assert.That(RcompWertung.Tonnen.Idealzeit("sternenklasse"), Is.EqualTo(17.0));
            Assert.That(RcompWertung.Tonnen.Sterne(24.0, 24.0), Is.EqualTo(3));
            Assert.That(RcompWertung.Tonnen.Sterne(27.59, 24.0), Is.EqualTo(2));
            Assert.That(RcompWertung.Tonnen.Sterne(32.39, 24.0), Is.EqualTo(1));
            Assert.That(RcompWertung.Tonnen.Sterne(33.0, 24.0), Is.EqualTo(0));
        }

        // ── riding stats (test_rpferd_ride.gd) ──

        [Test]
        public void RideStatsFormeln()
        {
            Assert.That(RideStats.Zieltempo("galopp", Stats(("tempo", 10))), Is.EqualTo(8.5).Within(1e-9));
            Assert.That(RideStats.Zieltempo("galopp", Stats(("tempo", 20))), Is.EqualTo(8.5 * 1.15).Within(1e-6));
            Assert.That(RideStats.Zieltempo("galopp", Stats(("tempo", 1))), Is.EqualTo(8.5 * 0.865).Within(1e-6));
            Assert.That(RideStats.Zieltempo("galopp", Stats(("tempo", 10)), 1.06), Is.EqualTo(8.5 * 1.06).Within(1e-6));
            Assert.That(RideStats.Zieltempo("toelt", Stats(("tempo", 20))), Is.EqualTo(5.8).Within(1e-9));
            Assert.That(RideStats.Zieltempo("trab", RideStats.Leer), Is.EqualTo(4.2).Within(1e-9));
            Assert.That(RideStats.GangartHoch("trab", true), Is.EqualTo("toelt"));
            Assert.That(RideStats.GangartHoch("toelt", true), Is.EqualTo("galopp"));
            Assert.That(RideStats.GangartHoch("trab"), Is.EqualTo("galopp"));
            Assert.That(RideStats.GangartRunter("galopp", true), Is.EqualTo("toelt"));
            Assert.That(RideStats.GangartRunter("galopp"), Is.EqualTo("trab"));
            Assert.That(RideStats.GangartRunter("toelt"), Is.EqualTo("trab"));
            Assert.That(RideStats.AccelAuf("galopp", 0.5), Is.EqualTo(RideFeel.KickAccel));
            Assert.That(RideStats.AccelAuf("galopp", 0.0), Is.EqualTo(3.0));
            Assert.That(RideStats.AccelAuf("schritt", 1.0), Is.EqualTo(2.0));
            Assert.That(RideStats.AusdauerMax(Stats(("ausdauer", 1))), Is.EqualTo(55.0).Within(1e-9));
            Assert.That(RideStats.AusdauerMax(Stats(("ausdauer", 20))), Is.EqualTo(150.0).Within(1e-9));
            Assert.That(RideStats.GaloppVerbrauch(Stats(("ausdauer", 20))), Is.EqualTo(6.3).Within(1e-6));
            Assert.That(RideStats.StepAusdauer(50.0, "toelt", 1.0, Stats(("ausdauer", 10))), Is.EqualTo(47.0).Within(1e-6));
            Assert.That(RideStats.StepAusdauer(149.0, "stand", 1.0, Stats(("ausdauer", 20))), Is.EqualTo(150.0).Within(1e-6));
            Assert.That(RideStats.GangartNachAusdauer("toelt", 15.0, false), Is.EqualTo("trab"));
            Assert.That(RideStats.GangartNachAusdauer("toelt", 15.0, true), Is.EqualTo("toelt"));
            Assert.That(RideStats.GangartNachAusdauer("galopp", 0.0, true), Is.EqualTo("trab"));
            var basis = RideStats.SteerYawRate(1.0, 8.5, Stats(("wendigkeit", 10)));
            Assert.That(basis, Is.EqualTo(1.7 * 0.7).Within(1e-6));
            Assert.That(RideStats.SteerYawRate(1.0, 8.5, Stats(("wendigkeit", 20))), Is.EqualTo(1.7 * 1.2 * (1.0 - 0.24)).Within(1e-6));
            Assert.That(RideStats.SteerYawRate(1.0, 0.1, RideStats.Leer), Is.EqualTo(0.0));
            Assert.That(Math.Abs(RideStats.SteerYawRate(99.0, 4.0, Stats(("wendigkeit", 20)))), Is.LessThanOrEqualTo(RideFeel.SteerRateCapRadS));
            Assert.That(RideStats.SprungWertung(1.1, 6.0), Is.EqualTo("perfekt"));
            Assert.That(RideStats.SprungWertung(0.9, 6.0), Is.EqualTo("perfekt"));
            Assert.That(RideStats.SprungWertung(1.3, 6.0), Is.EqualTo("perfekt"));
            Assert.That(RideStats.SprungWertung(0.6, 6.0), Is.EqualTo("gut"));
            Assert.That(RideStats.SprungWertung(1.85, 6.0), Is.EqualTo("gut"));
            Assert.That(RideStats.SprungWertung(2.5, 6.0), Is.EqualTo("daneben"));
            Assert.That(RideStats.SprungWertung(0.2, 6.0), Is.EqualTo("daneben"));
            Assert.That(RideStats.SprungWertung(1.5, 8.0, 50.0), Is.EqualTo("perfekt"));
            Assert.That(RideStats.SprungWertung(1.5, 0.0, 50.0), Is.EqualTo("gut"));
            Assert.That(RideStats.SprungVy(Stats(("sprungkraft", 20))), Is.EqualTo(5.4).Within(1e-6));
            Assert.That(RideStats.UntergrundTempoMult("matsch"), Is.EqualTo(0.9).Within(1e-9));
            Assert.That(RideStats.UntergrundTempoMult("wasser"), Is.EqualTo(0.85).Within(1e-9));
            Assert.That(RideStats.UntergrundTempoMult("matsch", 0.5), Is.EqualTo(0.95).Within(1e-6));
            Assert.That(RideStats.ZweiterWindMoeglich(5.0, 0.0, false), Is.True);
            Assert.That(RideStats.ZweiterWindMoeglich(5.0, 0.0, true), Is.False);
            Assert.That(RideStats.ZweiterWindMoeglich(15.0, 0.0, false), Is.False);
            Assert.That(RideStats.ZweiterWindMoeglich(5.0, 3.0, false), Is.False);
            Assert.That(RideStats.ScheuChance(Stats(("gelassenheit", 20))), Is.EqualTo(0.075).Within(1e-6));
            Assert.That(RideStats.Lenkassistent(1.0, 0.0, 0.9), Is.EqualTo(0.7).Within(1e-6));
        }

        [Test]
        public void ControllerKickSprungWertungUndZweiterWind()
        {
            var c = new RideController();
            c.GaitUp();
            c.GaitUp();
            Assert.That(c.Gait, Is.EqualTo("trab"));
            c.GaitUp();
            Assert.That(c.Gait, Is.EqualTo("galopp"));
            Assert.That(c.KickRest, Is.GreaterThan(0.0), "galloping off fires the kick");

            var s = new RideController();
            s.SetHindernisse(new[] { new V2(0f, -1.1f) });
            s.SetGait("galopp");
            s.Tempo = 8.0;
            var wertungen = new List<(string, int)>();
            s.SprungGewertet += (w, p) => wertungen.Add((w, p));
            Assert.That(s.Jump(), Is.True);
            Assert.That(wertungen, Has.Count.EqualTo(1));
            Assert.That(wertungen[0].Item1, Is.EqualTo("perfekt"), "1.1 m before the obstacle is the perfect zone");
            Assert.That(wertungen[0].Item2, Is.EqualTo(RideFeel.SprungPerfektPunkte));
            Assert.That(s.NaechstesHindernisM(), Is.EqualTo(1.1).Within(1e-4));
            s.Step(1.0 / 60.0);
            Assert.That(s.InLuft, Is.True);
            Assert.That(s.Jump(), Is.False, "no second takeoff in the air");

            var w2 = new RideController();
            w2.SetBindung(60.0);
            w2.Ausdauer = 5.0;
            w2.Tempo = 0.0;
            Assert.That(w2.ZweiterWindBereit, Is.True);
            Assert.That(w2.ZweiterWind(), Is.True);
            Assert.That(w2.Ausdauer, Is.EqualTo(40.0).Within(1e-6), "bond level 6 = +35");
            Assert.That(w2.ZweiterWind(), Is.False, "once per ride");
            Assert.That(w2.AusdauerMaxWert, Is.EqualTo(100.0));
        }

        [Test]
        public void ControllerRittGallopZehrtUndLenktRechtsNachPlusX()
        {
            var c = new RideController { Pos = new V2(0f, 0f), Heading = 0.0 };
            c.SetBounds(V2.Zero, new V2(500f, 500f));
            c.SetGait("galopp");
            for (var i = 0; i < 120; i++) c.Step(1.0 / 60.0);
            Assert.That(c.Tempo, Is.GreaterThan(6.0), "accelerates toward the gallop");
            Assert.That(c.Pos.Y, Is.LessThan(-5f), "heading 0 rides toward −z");
            Assert.That(c.Ausdauer, Is.LessThan(100.0), "the gallop drains stamina");
            var x0 = c.Pos.X;
            c.SteerInput(1.0);
            for (var i = 0; i < 60; i++) c.Step(1.0 / 60.0);
            Assert.That(c.Pos.X, Is.GreaterThan(x0 + 1f), "steering right turns toward +x");
            c.SetGait("galopp");
            c.Ausdauer = 0.5;
            var erschoepft = false;
            c.Erschoepft += () => erschoepft = true;
            for (var i = 0; i < 30; i++) c.Step(1.0 / 60.0);
            Assert.That(erschoepft, Is.True);
            Assert.That(c.Gait, Is.EqualTo("trab"), "an empty tank falls back to trot");
        }

        // ── ghost (test_rcomp_ghost.gd) ──

        private static RanchCompGhost.Recorder Beispiel()
        {
            var rec = RanchCompGhost.NeuerRecorder("tonnen", new V2(12f, -5f));
            const double dt = 1.0 / 60.0;
            var t = 0.0;
            while (t < 30.0)
            {
                t += dt;
                var w = t * 0.4;
                var springt = t > 4.0 && t < 4.6;
                RanchCompGhost.Tick(rec, dt, new V2(12.0 + Math.Cos(w) * 9.0, -5.0 + Math.Sin(w) * 9.0), springt ? 0.8 : 0.0, w + Math.PI * 0.5, t > 2.0 ? "galopp" : "trab", springt);
            }
            return rec;
        }

        [Test]
        public void GhostRasterBudgetRoundtripUndInterpolation()
        {
            var rec = Beispiel();
            Assert.That(Math.Abs(rec.Samples.Count - 300), Is.LessThanOrEqualTo(1));
            var bytes = RanchCompGhost.Encode(rec);
            Assert.That(bytes.Length, Is.EqualTo(RanchCompGhost.HeaderBytes + rec.Samples.Count * RanchCompGhost.SampleBytes));
            Assert.That(bytes.Length / RanchCompGhost.DauerS(rec), Is.LessThanOrEqualTo(130.0));
            var geist = RanchCompGhost.Decode(bytes);
            Assert.That(geist.Ok, Is.True);
            Assert.That(geist.Disziplin, Is.EqualTo("tonnen"));
            foreach (var idx in new[] { 0, 60, 150, 299 })
            {
                var w = (idx + 1.0) / RanchCompGhost.Hz * 0.4;
                var soll = new V2(12.0 + Math.Cos(w) * 9.0, -5.0 + Math.Sin(w) * 9.0);
                Assert.That(geist.Samples[idx].Pos.DistanceTo(soll), Is.LessThan(0.08f), "sample " + idx);
            }
            Assert.That(geist.Samples[42].Springt, Is.True);
            Assert.That(geist.Samples[60].Gangart, Is.EqualTo("galopp"));
            var b64 = RanchCompGhost.ToB64(rec);
            Assert.That(RanchCompGhost.FromB64(b64).Samples.Count, Is.EqualTo(rec.Samples.Count));
            Assert.That(RanchCompGhost.FromB64("").Ok, Is.False);
            Assert.That(RanchCompGhost.FromB64("not base64!").Ok, Is.False);
            Assert.That(RanchCompGhost.Decode(new byte[] { 1, 2, 3 }).Ok, Is.False);
            var falsch = RanchCompGhost.Encode(rec);
            falsch[0] = 0xFF;
            Assert.That(RanchCompGhost.Decode(falsch).Ok, Is.False);
            Assert.That(RanchCompGhost.Decode(RanchCompGhost.Encode(rec).Take(40).ToArray()).Ok, Is.False);

            var zwei = RanchCompGhost.NeuerRecorder("zeit", V2.Zero);
            RanchCompGhost.Tick(zwei, 0.1, V2.Zero, 0.0, 0.0, "trab");
            RanchCompGhost.Tick(zwei, 0.1, new V2(1f, 0f), 0.0, 0.0, "trab");
            var g = RanchCompGhost.Decode(RanchCompGhost.Encode(zwei));
            Assert.That(RanchCompGhost.ZustandBei(g, 0.05).Pos.X, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(RanchCompGhost.ZustandBei(g, 99.0).Pos.X, Is.EqualTo(1f).Within(0.01f));
            Assert.That(RanchCompGhost.ZustandBei(new RanchCompGhost.Geist(), 1.0).Gangart, Is.EqualTo("stand"));

            var wrap = RanchCompGhost.NeuerRecorder("zeit", V2.Zero);
            RanchCompGhost.Tick(wrap, 0.1, V2.Zero, 0.0, 2.0 * Math.PI - 0.1, "schritt");
            RanchCompGhost.Tick(wrap, 0.1, V2.Zero, 0.0, 0.1, "schritt");
            var h = RcompKurs.Fposmod(RanchCompGhost.ZustandBei(RanchCompGhost.Decode(RanchCompGhost.Encode(wrap)), 0.05).Heading, 2.0 * Math.PI);
            Assert.That(h < 0.15 || h > 2.0 * Math.PI - 0.15, Is.True, "short way over 0");
        }

        [Test]
        public void GhostIstByteGleichZuGodot()
        {
            var rec = RanchCompGhost.NeuerRecorder("tonnen", new V2(12f, -5f));
            var t = 0.0;
            while (t < 2.0)
            {
                t += 1.0 / 60.0;
                var w = t * 0.4;
                var springt = t > 1.0 && t < 1.3;
                RanchCompGhost.Tick(rec, 1.0 / 60.0, new V2(12.0 + Math.Cos(w) * 9.0, -5.0 + Math.Sin(w) * 9.0), springt ? 0.8 : 0.0, w + Math.PI * 0.5, t > 0.5 ? "galopp" : "trab", springt);
            }
            Assert.That(RanchCompGhost.ToB64(rec), Is.EqualTo((string)_golden["ghost_b64"]));
        }

        // ── save slice (test_rcomp_state.gd) ──

        [Test]
        public void StateDefaultsHeilungUndNachbarn()
        {
            var comp = RanchCompState.Lese(new JObject());
            Assert.That((string)comp["klasse"], Is.EqualTo("holz"));
            Assert.That(((JObject)comp["punkte"]).Count, Is.EqualTo(5));
            Assert.That(comp["arcade"]["tonnen"], Is.Not.Null);
            Assert.That(comp["arcade"]["zeit"], Is.Not.Null);
            Assert.That((string)RanchCompState.Lese(null)["klasse"], Is.EqualTo("holz"));

            var kaputt = JObject.Parse(@"{""klasse"":""diamant"",""punkte"":{""holz"":-5,""bronze"":""zehn""},""schleifen"":{""springen_holz"":2,""quatsch"":9},
                ""trophaeen"":[""pokal_holz"",""pokal_holz"",42],""teilnahmen"":-3,""geister"":{""tonnen"":{""b64"":""""},""nix"":{""b64"":""abc""},""gelaende"":{""b64"":""abc""}},
                ""arcade"":""kaputt"",""fremd_von_anderem_agenten"":{""bleibt"":true}}");
            var heil = RanchCompState.Normalize(kaputt);
            Assert.That((string)heil["klasse"], Is.EqualTo("holz"));
            Assert.That((int)heil["punkte"]["holz"], Is.EqualTo(0));
            Assert.That((int)heil["punkte"]["bronze"], Is.EqualTo(0));
            Assert.That(((JObject)heil["schleifen"]).Count, Is.EqualTo(1));
            Assert.That(((JArray)heil["trophaeen"]).Count, Is.EqualTo(1));
            Assert.That((int)heil["teilnahmen"], Is.EqualTo(0));
            Assert.That(heil["geister"]["tonnen"], Is.Null);
            Assert.That(heil["geister"]["nix"], Is.Null);
            Assert.That(heil["geister"]["gelaende"], Is.Not.Null);
            Assert.That(heil["arcade"]["tonnen"], Is.Not.Null);
            Assert.That(heil["fremd_von_anderem_agenten"], Is.Not.Null, "foreign keys survive");

            var state = JObject.Parse(@"{""ranch"":{""welt"":{""entdeckt"":[""hof""]},""tiere"":{""pferde"":{}}}}");
            var c = RanchCompState.Lese(state);
            c["teilnahmen"] = 3;
            RanchCompState.Schreibe(state, c);
            Assert.That((string)state["ranch"]["welt"]["entdeckt"][0], Is.EqualTo("hof"));
            Assert.That((int)state["ranch"]["comp"]["teilnahmen"], Is.EqualTo(3));
        }

        [Test]
        public void StateGeistNurBeiVerbesserungUndArcade()
        {
            var s = new JObject();
            Assert.That(RanchCompState.GeistSpeichern(s, "tonnen", "AAAA", 25.0, 25.0, "2026-07-26", "", true), Is.True);
            Assert.That(RanchCompState.GeistSpeichern(s, "tonnen", "BBBB", 27.0, 27.0, "", "", true), Is.False);
            Assert.That((string)RanchCompState.Geist(s, "tonnen")["b64"], Is.EqualTo("AAAA"));
            Assert.That(RanchCompState.GeistSpeichern(s, "tonnen", "CCCC", 22.5, 22.5, "", "", true), Is.True);
            Assert.That((string)RanchCompState.Geist(s, "tonnen")["b64"], Is.EqualTo("CCCC"));
            Assert.That(RanchCompState.GeistSpeichern(s, "springen", "P1", 900.0, 0.0, "", "", false), Is.True);
            Assert.That(RanchCompState.GeistSpeichern(s, "springen", "P2", 800.0, 0.0, "", "", false), Is.False);
            Assert.That(RanchCompState.GeistSpeichern(s, "springen", "P3", 950.0, 0.0, "", "", false), Is.True);
            Assert.That(RanchCompState.GeistSpeichern(s, "tonnen", "", 1.0, 1.0, "", "", true), Is.False);
            Assert.That(RanchCompState.GeistSpeichern(s, "zeit_k3", "Z", 50.0, 50.0, "", "", true), Is.True, "time-trial ghosts per route");
            Assert.That((string)RanchCompState.Geist(s, "zeit_k3")["b64"], Is.EqualTo("Z"));

            var a = new JObject();
            Assert.That(RanchCompState.ArcadeMaxUnlocked(a, "tonnen"), Is.EqualTo(1));
            var win = RanchCompState.ArcadeWin(a, "tonnen", 1, 2, 240, true);
            Assert.That(win.firstClear, Is.True);
            Assert.That(RanchCompState.ArcadeStars(a, "tonnen", 1), Is.EqualTo(2));
            Assert.That(RanchCompState.ArcadeBest(a, "tonnen", 1), Is.EqualTo(240));
            Assert.That(RanchCompState.ArcadeMaxUnlocked(a, "tonnen"), Is.EqualTo(2));
            var schlechter = RanchCompState.ArcadeWin(a, "tonnen", 1, 1, 300, true);
            Assert.That(schlechter.firstClear, Is.False);
            Assert.That(schlechter.newBest, Is.False);
            Assert.That(RanchCompState.ArcadeStars(a, "tonnen", 1), Is.EqualTo(2));
            Assert.That(RanchCompState.ArcadeBest(a, "tonnen", 1), Is.EqualTo(240));
            Assert.That(RanchCompState.ArcadeWin(a, "tonnen", 1, 3, 200, true).newBest, Is.True);
            Assert.That(RanchCompState.ArcadeBest(a, "tonnen", 1), Is.EqualTo(200));
            RanchCompState.ArcadeWin(a, "zeit", 1, 1, 100);
            Assert.That(RanchCompState.ArcadeWin(a, "zeit", 1, 1, 150).newBest, Is.True);
        }

        // ── catalogue / league (test_rcomp_liga.gd) ──

        [Test]
        public void KatalogGoldRichtungUndRichtzeiten()
        {
            Assert.That(RanchCompKatalog.GoldFuerPlatz(_bal, "holz", 1), Is.EqualTo(40));
            Assert.That(RanchCompKatalog.GoldFuerPlatz(_bal, "holz", 2), Is.EqualTo(24));
            Assert.That(RanchCompKatalog.GoldFuerPlatz(_bal, "holz", 3), Is.EqualTo(14));
            Assert.That(RanchCompKatalog.GoldFuerPlatz(_bal, "holz", 5), Is.EqualTo(4));
            Assert.That(RanchCompKatalog.GoldFuerPlatz(_bal, "sternenklasse", 1), Is.EqualTo(160));
            Assert.That(RanchCompKatalog.GoldFuerPlatz(_bal, "gold", 2), Is.EqualTo(72));
            Assert.That(RanchCompKatalog.ZeitGewinnt(_bal, "springen"), Is.False);
            Assert.That(RanchCompKatalog.ZeitGewinnt(_bal, "schau"), Is.False);
            Assert.That(RanchCompKatalog.ZeitGewinnt(_bal, "gelaende"), Is.True);
            Assert.That(RanchCompKatalog.ZeitGewinnt(_bal, "tonnen"), Is.True);
            Assert.That(RanchCompKatalog.ZeitGewinnt(_bal, "rennen"), Is.True);
            Assert.That(RanchCompKatalog.RichtzeitS(_bal, "tonnen", "holz"), Is.EqualTo(24.0));
            Assert.That(RanchCompKatalog.RichtzeitS(_bal, "tonnen", "sternenklasse"), Is.EqualTo(17.0));
            Assert.That(RanchCompKatalog.RichtzeitS(_bal, "dressur", "holz"), Is.EqualTo(0.0));
            for (var i = 0; i < RanchCompKatalog.Klassen.Length - 1; i++)
                Assert.That(RanchCompKatalog.RichtzeitS(_bal, "tonnen", RanchCompKatalog.Klassen[i + 1]), Is.LessThan(RanchCompKatalog.RichtzeitS(_bal, "tonnen", RanchCompKatalog.Klassen[i])));
            foreach (var k in RanchCompKatalog.Klassen) Assert.That(RanchCompKatalog.XpFuerTeilnahme(_bal, k), Is.EqualTo(RanchCompKatalog.XpJeTeilnahme[k]), k);
            foreach (var d in RanchCompKatalog.Disziplinen) Assert.That((string)RanchCompKatalog.Disziplin(_bal, d)["id"], Is.EqualTo(d));
        }

        [Test]
        public void LigaPunkteAufstiegStartrechtUndTurniertag()
        {
            Assert.That(new[] { 1, 2, 3, 4, 5, 6, 0 }.Select(p => RanchCompLiga.PunkteFuerPlatz(_bal, p)), Is.EqualTo(new[] { 10, 7, 5, 3, 1, 1, 0 }));
            var knapp = RanchCompLiga.PruefeAufstieg(_bal, "holz", RanchCompLiga.AufstiegAb(_bal, "holz") - 1);
            Assert.That(knapp.aufgestiegen, Is.False);
            var auf = RanchCompLiga.PruefeAufstieg(_bal, "holz", RanchCompLiga.AufstiegAb(_bal, "holz"));
            Assert.That(auf.aufgestiegen, Is.True);
            Assert.That(auf.klasse, Is.EqualTo("bronze"));
            Assert.That(RanchCompLiga.PruefeAufstieg(_bal, "sternenklasse", 9999).aufgestiegen, Is.False);
            foreach (var k in RanchCompKatalog.Klassen) Assert.That(RanchCompLiga.PruefeAufstieg(_bal, k, 0).klasse, Is.EqualTo(k));
            Assert.That(RanchCompLiga.StartErlaubt(_bal, "holz", "holz", 1), Is.True);
            Assert.That(RanchCompLiga.StartErlaubt(_bal, "holz", "bronze", 20), Is.False);
            Assert.That(RanchCompLiga.StartErlaubt(_bal, "gold", "gold", 17), Is.False);
            Assert.That(RanchCompLiga.StartErlaubt(_bal, "gold", "gold", 18), Is.True);
            Assert.That(RanchCompLiga.StartErlaubt(_bal, "gold", "holz", 3), Is.True);
            Assert.That(RanchCompLiga.AufstiegFortschritt(_bal, "sternenklasse", 0), Is.EqualTo(1.0));
            var a = RanchCompLiga.TurniertagPlan(_bal, "2026-07-20", 4711);
            Assert.That(RanchCompLiga.TurniertagPlan(_bal, "2026-07-22", 4711), Is.EqualTo(a), "same week, same plan");
            Assert.That(a, Has.Count.EqualTo(3));
            Assert.That(a.Distinct().Count(), Is.EqualTo(3));
            Assert.That(RanchCompLiga.BonusGoldMult(_bal), Is.EqualTo(1.25));
            Assert.That(RanchCompLiga.SchleifenKey("springen", "holz"), Is.EqualTo("springen_holz"));
            Assert.That(RanchCompLiga.TrophaeeFuer("tonnen", "sternenklasse", 1), Is.EqualTo("stern_tonnen"));
            Assert.That(RanchCompLiga.TrophaeeFuer("tonnen", "silber", 2), Is.EqualTo("pokal_silber"));
            Assert.That(RanchCompLiga.TrophaeeFuer("tonnen", "silber", 4), Is.EqualTo(""));
        }

        // ── bots / tournament (test_rcomp_bots.gd, test_rcomp_turnier.gd) ──

        [Test]
        public void BotsRosterBaenderDeterminismusUndMonotonie()
        {
            Assert.That(RanchCompBots.Roster.Select(b => b.Id).Distinct().Count(), Is.EqualTo(RanchCompBots.Roster.Length));
            foreach (var b in RanchCompBots.Roster)
            {
                Assert.That(b.Name, Is.Not.Empty);
                Assert.That(b.Pferd, Is.Not.Empty);
                Assert.That(b.Talent, Is.InRange(0.0, 1.0));
                Assert.That(RanchCompKatalog.Disziplinen, Does.Contain(b.Liebling));
            }
            var rosalinde = RanchCompBots.Bot("rosalinde");
            var holz = RanchCompBots.Koennen(_bal, rosalinde, "holz", "dressur");
            Assert.That(RanchCompBots.Koennen(_bal, rosalinde, "sternenklasse", "dressur"), Is.GreaterThan(holz));
            Assert.That(RanchCompBots.Koennen(_bal, rosalinde, "holz", "springen"), Is.GreaterThan(holz));
            var feldA = RanchCompBots.Starterfeld(_bal, "silber", "tonnen", 20260726);
            Assert.That(feldA.Select(s => s.Id), Is.EqualTo(RanchCompBots.Starterfeld(_bal, "silber", "tonnen", 20260726).Select(s => s.Id)));
            Assert.That(feldA.Select(s => s.Id).Distinct().Count(), Is.EqualTo(5));
            foreach (var d in RanchCompKatalog.Disziplinen)
            {
                var kleiner = RanchCompKatalog.ZeitGewinnt(_bal, d);
                double schwach = 0, stark = 0;
                for (var i = 0; i < 40; i++)
                {
                    schwach += RanchCompBots.SimuliereLauf(_bal, d, "silber", 0.25, 7000 + i).Wert;
                    stark += RanchCompBots.SimuliereLauf(_bal, d, "silber", 0.85, 7000 + i).Wert;
                }
                Assert.That(kleiner ? stark < schwach : stark > schwach, Is.True, d);
            }
            Assert.That(RanchCompBots.SpielerKoennen(_bal, "schau", RideStats.Leer, 0.5), Is.GreaterThan(0.0));
        }

        private double SiegQuote(string klasse, double statWert, double fahr, bool podium)
        {
            var stats = Stats(("tempo", statWert), ("sprungkraft", statWert), ("wendigkeit", statWert), ("ausdauer", statWert), ("gelassenheit", statWert));
            var disziplinen = new[] { "tonnen", "springen", "gelaende" };
            var treffer = 0;
            for (var i = 0; i < 40; i++)
            {
                var d = disziplinen[i % 3];
                var seed = 31000 + i * 17;
                var bots = RanchCompTurnier.BotsSimulieren(_bal, d, klasse, seed);
                var lauf = RanchCompBots.SimuliereLauf(_bal, d, klasse, RanchCompBots.SpielerKoennen(_bal, d, stats, fahr), seed + 5);
                var platz = RanchCompTurnier.SpielerPlatz(RanchCompTurnier.Endstand(_bal, d, bots, lauf.Wert, lauf.ZeitS, ""));
                if (podium ? platz <= 3 : platz == 1) treffer++;
            }
            return treffer / 40.0;
        }

        [Test]
        public void FairnessUeberDieKlassen()
        {
            Assert.That(SiegQuote("holz", 8, 0.55, false), Is.GreaterThanOrEqualTo(0.6), "wood forgives");
            Assert.That(SiegQuote("sternenklasse", 8, 0.55, false), Is.LessThanOrEqualTo(0.25), "star class wants a strong horse");
            Assert.That(SiegQuote("sternenklasse", 18, 0.9, true), Is.GreaterThanOrEqualTo(0.5));
            Assert.That(SiegQuote("gold", 8, 0.55, true), Is.LessThan(0.5));
        }

        [Test]
        public void TurnierEndstandBelohnungUndVerbuchung()
        {
            var stand = RanchCompTurnier.Endstand(_bal, "springen", new[] { new RcompStarter { Id = "a", Wert = 900.0 }, new RcompStarter { Id = "b", Wert = 950.0 } }, 930.0, 0.0, "");
            Assert.That(stand[0].Id, Is.EqualTo("b"));
            Assert.That(RanchCompTurnier.SpielerPlatz(stand), Is.EqualTo(2));
            var zeit = RanchCompTurnier.Endstand(_bal, "tonnen", new[] { new RcompStarter { Id = "a", Wert = 24.0 }, new RcompStarter { Id = "b", Wert = 21.5 } }, 20.9, 20.9, "");
            Assert.That(RanchCompTurnier.SpielerPlatz(zeit), Is.EqualTo(1));
            var a = RanchCompTurnier.BotsSimulieren(_bal, "tonnen", "holz", 777);
            Assert.That(a.Select(s => s.Id), Is.EqualTo(RanchCompTurnier.BotsSimulieren(_bal, "tonnen", "holz", 777).Select(s => s.Id)));
            Assert.That(a, Has.Count.EqualTo(RanchCompTurnier.BotsJeTurnier));

            var basis = RanchCompTurnier.Belohnung(_bal, "tonnen", "holz", 1, false);
            Assert.That((basis.Gold, basis.Xp, basis.LigaPunkte, basis.Schleife, basis.Trophaee), Is.EqualTo((40, 60, 10, true, "pokal_holz")));
            Assert.That(RanchCompTurnier.Belohnung(_bal, "tonnen", "holz", 1, true).Gold, Is.EqualTo(50));
            Assert.That(RanchCompTurnier.Belohnung(_bal, "tonnen", "holz", 1, false, 1.15).Gold, Is.EqualTo(46));
            Assert.That(RanchCompTurnier.Belohnung(_bal, "tonnen", "sternenklasse", 1, true, 1.15).Gold, Is.EqualTo(230));
            var vierter = RanchCompTurnier.Belohnung(_bal, "tonnen", "holz", 4, false);
            Assert.That((vierter.Schleife, vierter.Trophaee, vierter.Gold), Is.EqualTo((false, "", 4)));

            var s = JObject.Parse(@"{""economy"":{""coins"":100}}");
            var bericht = RanchCompTurnier.Verbuche(s, _bal, "tonnen", "holz", 1, basis);
            Assert.That((int)s["economy"]["coins"], Is.EqualTo(140));
            Assert.That((int)s["ranch"]["comp"]["punkte"]["holz"], Is.EqualTo(10));
            Assert.That((int)s["ranch"]["comp"]["teilnahmen"], Is.EqualTo(1));
            Assert.That((int)s["ranch"]["comp"]["siege"], Is.EqualTo(1));
            Assert.That((bericht.Aufgestiegen, bericht.SchleifeNeu, bericht.TrophaeeNeu), Is.EqualTo((false, true, true)));
            var b2 = RanchCompTurnier.Verbuche(s, _bal, "tonnen", "holz", 1, basis);
            Assert.That((int)s["ranch"]["comp"]["punkte"]["holz"], Is.EqualTo(20));
            Assert.That((b2.SchleifeNeu, b2.TrophaeeNeu), Is.EqualTo((false, false)));
            var b3 = RanchCompTurnier.Verbuche(s, _bal, "tonnen", "holz", 1, basis);
            Assert.That((b3.Aufgestiegen, b3.NeueKlasse), Is.EqualTo((true, "bronze")));
            Assert.That((string)s["ranch"]["comp"]["klasse"], Is.EqualTo("bronze"));
            RanchCompTurnier.Verbuche(s, _bal, "tonnen", "holz", 5, RanchCompTurnier.Belohnung(_bal, "tonnen", "holz", 5, false));
            Assert.That((string)s["ranch"]["comp"]["klasse"], Is.EqualTo("bronze"), "no relegation");

            var t = new JObject();
            RanchCompTurnier.Verbuche(t, _bal, "springen", "holz", 3, RanchCompTurnier.Belohnung(_bal, "springen", "holz", 3, false));
            Assert.That((int)t["ranch"]["comp"]["schleifen"]["springen_holz"], Is.EqualTo(3));
            Assert.That(RanchCompTurnier.Verbuche(t, _bal, "springen", "holz", 1, RanchCompTurnier.Belohnung(_bal, "springen", "holz", 1, false)).SchleifeNeu, Is.True);

            var stall = JObject.Parse(@"{""ranch"":{""tiere"":{""pferde"":{""p1"":{""name"":""Wolke"",""level"":2,""bindung"":10},""p2"":{""name"":""Keks"",""level"":2,""bindung"":40}}}}}");
            Assert.That(RanchCompTurnier.BestesPferd(stall)?.id, Is.EqualTo("p2"));
            Assert.That(RanchCompTurnier.BestesPferd(new JObject()), Is.Null);
        }

        // ── arcade ladders (comp_arcade.gd) ──

        [Test]
        public void ArcadeLeiterUndScore()
        {
            Assert.That(RanchCompArcade.TonnenIdealS(1), Is.EqualTo(26.0));
            Assert.That(RanchCompArcade.TonnenIdealS(10), Is.EqualTo(17.0));
            Assert.That(RanchCompArcade.TonnenIdealS(99), Is.EqualTo(17.0));
            Assert.That(RanchCompArcade.ZeitGeistKey(3), Is.EqualTo("zeit_k3"));
            Assert.That(RanchCompArcade.Score(3, 20.0, 24.0, true), Is.EqualTo(40 + 75 + 12 + 30));
            Assert.That(RanchCompArcade.Score(1, 99.0, 24.0, false), Is.EqualTo(65));
        }

        // ── Godot cross-check (Fixtures/rcomp_golden.json) ──

        private static void AssertTore(List<RcompKurs.Tor> tore, JArray soll, string was)
        {
            Assert.That(tore, Has.Count.EqualTo(soll.Count), was);
            for (var i = 0; i < tore.Count; i++)
            {
                var s = (JArray)soll[i];
                Assert.That(tore[i].Pos.X, Is.EqualTo((float)s[0]).Within(1e-4f), was + " x " + i);
                Assert.That(tore[i].Pos.Y, Is.EqualTo((float)s[1]).Within(1e-4f), was + " z " + i);
                Assert.That(tore[i].Quer.X, Is.EqualTo((float)s[2]).Within(1e-5f), was + " qx " + i);
                Assert.That(tore[i].Quer.Y, Is.EqualTo((float)s[3]).Within(1e-5f), was + " qz " + i);
            }
        }

        [Test]
        public void GodotGoldKurseUndZielzeiten()
        {
            var ziel = (JArray)_golden["zeit_ziel"];
            for (var level = 1; level <= 10; level++) Assert.That(RanchCompArcade.ZeitZielS(level), Is.EqualTo((double)ziel[level - 1]), "zeit_ziel_s " + level);
            foreach (var level in new[] { 1, 5, 10 }) AssertTore(RanchCompArcade.ZeitRoute(level), (JArray)_golden["zeit_route"][level.ToString()], "zeit_route " + level);
            AssertTore(RcompKurs.Gelaende("silber", 1234), (JArray)_golden["gelaende_silber_1234"], "gelaende");
            var spr = RcompKurs.Springen("silber");
            var sollSpr = (JArray)_golden["springen_silber"];
            Assert.That(spr, Has.Count.EqualTo(sollSpr.Count));
            for (var i = 0; i < spr.Count; i++)
            {
                Assert.That(spr[i].Pos.X, Is.EqualTo((float)sollSpr[i][0]).Within(1e-4f));
                Assert.That(spr[i].Pos.Y, Is.EqualTo((float)sollSpr[i][1]).Within(1e-4f));
                Assert.That(spr[i].Heading, Is.EqualTo((double)sollSpr[i][2]).Within(1e-9));
            }
            var ss = new[] { 0.0, 10.0, 40.0, 60.0, 100.0, 150.0, 200.0, 250.0 };
            for (var i = 0; i < ss.Length; i++)
            {
                var p = RcompKurs.RennenPunkt(ss[i]);
                Assert.That(p.X, Is.EqualTo((float)_golden["rennen_punkt"][i][0]).Within(1e-4f), "oval x " + ss[i]);
                Assert.That(p.Y, Is.EqualTo((float)_golden["rennen_punkt"][i][1]).Within(1e-4f), "oval z " + ss[i]);
            }
            Assert.That(RcompKurs.RennenSBei(new V2(20f, -15f)), Is.EqualTo((double)_golden["rennen_s_bei"]));
            Assert.That(RcompKurs.RennenUmfang(), Is.EqualTo((double)_golden["rennen_umfang"]).Within(1e-9));
        }

        [Test]
        public void GodotGoldBotsSimulationUndPlaene()
        {
            var feld = RanchCompBots.Starterfeld(_bal, "silber", "tonnen", 20260726);
            var sollFeld = (JArray)_golden["starterfeld"];
            for (var i = 0; i < feld.Count; i++)
            {
                Assert.That(feld[i].Id, Is.EqualTo((string)sollFeld[i][0]));
                Assert.That(feld[i].Koennen, Is.EqualTo((double)sollFeld[i][1]).Within(1e-12));
            }
            foreach (var d in RanchCompKatalog.Disziplinen)
            {
                var lauf = RanchCompBots.SimuliereLauf(_bal, d, "bronze", 0.5, 42);
                Assert.That(lauf.Wert, Is.EqualTo((double)_golden["simuliere_lauf"][d][0]).Within(1e-9), "simuliere_lauf " + d);
                Assert.That(lauf.ZeitS, Is.EqualTo((double)_golden["simuliere_lauf"][d][1]).Within(1e-9), "zeit " + d);
                var bots = RanchCompTurnier.BotsSimulieren(_bal, d, "bronze", 777);
                var soll = (JArray)_golden["bots_simulieren"][d];
                Assert.That(bots, Has.Count.EqualTo(soll.Count));
                for (var i = 0; i < bots.Count; i++)
                {
                    Assert.That(bots[i].Id, Is.EqualTo((string)soll[i][0]), d);
                    Assert.That(bots[i].Wert, Is.EqualTo((double)soll[i][1]).Within(1e-9), d + " wert " + bots[i].Id);
                    Assert.That(bots[i].ZeitS, Is.EqualTo((double)soll[i][2]).Within(1e-9), d + " zeit " + bots[i].Id);
                }
            }
            Assert.That(RanchCompLiga.TurniertagPlan(_bal, "2026-07-20", 4711), Is.EqualTo(((JArray)_golden["plan_a"]).Select(t => (string)t)));
            Assert.That(RanchCompLiga.TurniertagPlan(_bal, "2026-10-01", 777), Is.EqualTo(((JArray)_golden["plan_b"]).Select(t => (string)t)));
            var schau = new RcompRichterSchau();
            schau.Setup(1234);
            Assert.That(schau.Kommandos.Select(k => k.Id), Is.EqualTo(((JArray)_golden["schau_kommandos"]).Select(t => (string)t)));
        }

        // ── judges through the run driver ──

        [Test]
        public void TonnenRichterKleeblattRammenUndHeim()
        {
            var r = new RcompRichterTonnen();
            var vorher = new V2(9f + 3f, -2f);
            for (var a = 0; a <= 72; a++)
            {
                var w = a / 72.0 * 2.0 * Math.PI;
                var jetzt = new V2(9.0 + Math.Cos(w) * 3.0, -2.0 + Math.Sin(w) * 3.0);
                r.Tick(vorher, jetzt, "galopp", false, 0.05);
                vorher = jetzt;
            }
            Assert.That(r.Idx, Is.EqualTo(1), "a full circle around barrel 1 counts");
            var e = r.Tick(new V2(-9f, -0.5f), new V2(-9f, -1.5f), "trab", false, 0.05);
            Assert.That(e.Any(x => x.Typ == "tonne_um" && x.Index == 1), Is.True, "bumping tips the barrel");
            Assert.That(r.Umgeworfen, Is.EqualTo(1));
            r.Idx = 3;
            var heim = r.Tick(new V2(0f, 12f), new V2(0f, 14f), "galopp", false, 0.05);
            Assert.That(heim.Any(x => x.Typ == "ziel"), Is.True);
            Assert.That(r.Fertig, Is.True);
            Assert.That(r.Ergebnis(20.0).Wert, Is.EqualTo(25.0));
        }

        [Test]
        public void LaufSpringenWertetSprungUeberHindernisAlsSauber()
        {
            var lauf = new RcompLauf(new RcompLauf.Config { Disziplin = "springen", Klasse = "holz", Balance = _bal });
            var springen = (RcompRichterSpringen)lauf.Richter;
            var events = new List<RcompEvent>();
            lauf.Ereignis += events.Add;
            lauf.Starte();
            lauf.Controller.SetGait("trab");
            lauf.Controller.GaitUp();
            var ziel = springen.Kurs[0].Pos;
            for (var i = 0; i < 600 && springen.Idx == 0; i++)
            {
                var d = ziel.X - lauf.Controller.Pos.X;
                if (d > 0.9f && d < 1.4f && !lauf.Controller.InLuft) lauf.Controller.Jump();
                lauf.Tick(1.0 / 60.0);
            }
            Assert.That(springen.Idx, Is.EqualTo(1));
            Assert.That(events.Any(e => e.Typ == "perfekt" || e.Typ == "hindernis_ok"), Is.True, "an airborne crossing clears the obstacle");
            Assert.That(springen.Abwuerfe, Is.EqualTo(0));
        }

        [Test]
        public void LaufZeichnetGeistAufUndSpieltIhnAb()
        {
            var lauf = new RcompLauf(new RcompLauf.Config { Disziplin = "tonnen", Balance = _bal });
            lauf.Starte();
            lauf.Controller.SetGait("trab");
            for (var i = 0; i < 300; i++) lauf.Tick(1.0 / 60.0);
            Assert.That(lauf.Laeuft, Is.True);
            var rec = typeof(RcompLauf).GetField("_rec", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(lauf) as RanchCompGhost.Recorder;
            Assert.That(rec.Samples.Count, Is.EqualTo(50).Within(1));
            var b64 = RanchCompGhost.ToB64(rec);
            var zwei = new RcompLauf(new RcompLauf.Config { Disziplin = "tonnen", Balance = _bal, GeistB64 = b64 });
            Assert.That(zwei.Geist, Is.Not.Null);
            zwei.Starte();
            for (var i = 0; i < 120; i++) zwei.Tick(1.0 / 60.0);
            var g = zwei.GeistZustand();
            Assert.That(g.HasValue, Is.True);
            Assert.That(g.Value.Pos.Y, Is.LessThan(13f), "the ghost rides north like the recorded run");
        }

        [Test]
        public void SchauTippTrifftImFensterUndMeldetEreignis()
        {
            var lauf = new RcompLauf(new RcompLauf.Config { Disziplin = "schau", Seed = 7, Pflege = 80.0, Stil = 50.0 });
            var events = new List<RcompEvent>();
            lauf.Ereignis += events.Add;
            lauf.Starte();
            Assert.That(lauf.Tippe().Typ, Is.EqualTo("zu_frueh"));
            var schau = (RcompRichterSchau)lauf.Richter;
            while (!schau.Fertig)
            {
                lauf.Tick(0.05);
                if (schau.AktuellesKommando != null && Math.Abs(schau.Countdown) < 0.05) lauf.Tippe();
            }
            Assert.That(schau.Treffer, Is.EqualTo(5));
            Assert.That(events.Count(e => e.Typ == "treffer"), Is.EqualTo(5));
            Assert.That(lauf.Ergebnis.Wert, Is.EqualTo(0.4 * 80.0 + 0.3 * 50.0 + 0.3 * 100.0).Within(1e-9));
        }
    }
}
