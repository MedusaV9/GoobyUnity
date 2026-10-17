using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Minigames.Ranch;
using Gooby.Tests.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// The tournament league session: the QA rider enters all seven disciplines in the wood class through the real ride
    /// physics and judges — every run finishes cleanly, the standings/reward/booking follow comp_turnier.gd, the ceremony
    /// comes for podium places, each discipline once per session, and the league promotes after enough points.
    /// </summary>
    public class RanchTurnierGameTests
    {
        [OneTimeSetUp]
        public void Content() => Fixtures.InstallContentSource();

        private static void Bis(RanchTurnierGame game, Func<bool> ziel, double maxSec = 400.0)
        {
            var guard = 0;
            while (!ziel() && guard++ < 60 * maxSec)
            {
                game.ApplyBot();
                game.Tick(1.0 / 60.0);
            }
        }

        [Test]
        public void BotReitetAlleSiebenDisziplinenSauber()
        {
            var game = new RanchTurnierGame("normal", 4242, null, "2026-10-01");
            Assert.That(game.Plan, Has.Count.EqualTo(3));
            var report = new List<string>();
            foreach (var d in RanchCompKatalog.Disziplinen)
            {
                Bis(game, () => game.Phase == RanchTurnierGame.PhaseErgebnis && game.Disziplin == d);
                Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseErgebnis), d + " finishes");
                var e = game.LastErgebnis;
                report.Add($"{d}: wert={e.Wert:0.0} zeit={e.ZeitS:0.0} platz={game.LastPlatz} {e.Detail.ToString(Newtonsoft.Json.Formatting.None)}");
                switch (d)
                {
                    case "springen": Assert.That(((int)e.Detail["abwuerfe"], (int)e.Detail["verweigerungen"]), Is.EqualTo((0, 0)), "clean round"); break;
                    case "dressur":
                        Assert.That(((JArray)e.Detail["figuren"]).Count, Is.EqualTo(5));
                        Assert.That(e.Wert, Is.GreaterThan(40.0), "following the line scores (the deviation counts on the figure only)");
                        break;
                    case "rennen": Assert.That(e.ZeitS, Is.InRange(80.0, 125.0), "3 laps with the gallop/walk rhythm"); break;
                    case "gelaende": Assert.That((int)e.Detail["verpasst"], Is.EqualTo(0)); break;
                    case "trail": Assert.That(((JArray)e.Detail["aufgaben"]).Count, Is.EqualTo(6)); break;
                    case "schau": Assert.That((int)e.Detail["treffer"], Is.EqualTo(5)); break;
                    case "tonnen": Assert.That((int)e.Detail["umgeworfen"], Is.EqualTo(0)); break;
                }
                Assert.That(game.Gespielt[d], Is.EqualTo(game.LastPlatz));
                Bis(game, () => game.Phase == RanchTurnierGame.PhaseMenu || game.Finished);
            }
            TestContext.WriteLine(string.Join("\n", report));
            Bis(game, () => game.Finished, 5.0);
            Assert.That(game.Finished, Is.True, "all seven ridden → the bot says Fertig");
            Assert.That(game.FinalScore, Is.EqualTo(game.SessionScore));
            Assert.That((int)game.Comp["teilnahmen"], Is.EqualTo(7));
            Assert.That((int)game.Save["economy"]["coins"], Is.GreaterThan(0), "gold booked into economy.coins");
        }

        [Test]
        public void MenueKlassenEinmalJeDisziplinZeremonieUndAufstieg()
        {
            var game = new RanchTurnierGame("normal", 99, null, "2026-10-01");
            Assert.That(game.Klasse, Is.EqualTo("holz"));
            Assert.That(game.StartErlaubt("bronze"), Is.False, "the league class comes first");
            game.SetKlasse("bronze");
            Assert.That(game.Klasse, Is.EqualTo("holz"));
            Assert.That(game.PferdName, Is.Null, "no horse: the loan horse rides");
            Assert.That(game.OeffneEinweisung("tonnen"), Is.True);
            Assert.That(game.Bots, Has.Count.EqualTo(RanchCompTurnier.BotsJeTurnier), "the field rides before the player");
            Assert.That(game.Richtzeit, Is.EqualTo(24.0));
            game.ZurueckZumMenu();
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseMenu));
            Bis(game, () => game.Phase == RanchTurnierGame.PhaseErgebnis);
            Assert.That(game.Disziplin, Is.EqualTo("springen"), "the bot starts at the top of the menu");
            Assert.That(game.LastPlatz, Is.EqualTo(1));
            Assert.That(game.LastBericht.LigaPunkte, Is.EqualTo(10));
            Assert.That(game.LastPunkte, Is.EqualTo(RanchTurnierGame.ScoreBasis + 10 * RanchTurnierGame.ScoreJeLigaPunkt));
            Assert.That(game.LastGeistNeu, Is.True);
            game.NachErgebnis();
            Assert.That(game.Phase, Is.EqualTo(RanchTurnierGame.PhaseZeremonie), "a podium place gets the ceremony");
            game.ZeremonieWeiter();
            Assert.That(game.OeffneEinweisung("springen"), Is.False, "every discipline once per session");
            Assert.That(game.Stand.First().IstSpieler, Is.True);

            var store = new Gooby.Core.State.GameStateStore();
            store.Clock.Pin(1790000000000L);
            store.Manager.Io = new MemorySaveIo();
            store.Initialize("user/save_v5.json", 0);
            store.Update(st => st["ranch"] = JObject.Parse(@"{""comp"":{""klasse"":""bronze"",""punkte"":{""holz"":30}},""tiere"":{""pferde"":{""p1"":{""name"":""Keks"",""level"":6,""bindung"":40}}}}"));
            var stall = new RanchTurnierGame("normal", 5, store);
            Assert.That((stall.PferdName, stall.PferdLevel, stall.LigaKlasse), Is.EqualTo(("Keks", 6, "bronze")), "the best horse of the stable rides");
            Assert.That(stall.StartErlaubt("bronze"), Is.True, "league bronze + horse level 6 ≥ 5");
            Assert.That(stall.StartErlaubt("silber"), Is.False);
            stall.SetKlasse("bronze");
            Assert.That(stall.Klasse, Is.EqualTo("bronze"));
            var coins = (int)store.State["economy"]["coins"];
            Bis(stall, () => stall.Phase == RanchTurnierGame.PhaseErgebnis);
            Assert.That((int)store.State["economy"]["coins"], Is.EqualTo(coins + stall.LastBericht.Gold), "gold lands in the real save");
            Assert.That((int)store.State["ranch"]["comp"]["punkte"]["bronze"], Is.EqualTo(stall.LastBericht.LigaPunkte));
        }
    }
}
