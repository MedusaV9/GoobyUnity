using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The ranch's three tasks a day (rquest_katalog.gd tagesaufgaben, adapted to the farm and Hufingen).</summary>
    public class RanchTageTests
    {
        private const string Tag = "2026-10-11";

        private static JObject Spieler() => new JObject
        {
            ["economy"] = new JObject { ["coins"] = 100 },
            ["minigames"] = new JObject { ["plays"] = new JObject { ["ranchTonnen"] = 4, ["carrotCatch"] = 9 } },
        };

        [Test]
        public void ThreeTasksADayDrawnFromTheDate()
        {
            var heute = RanchTage.FuerTag(Tag);
            Assert.That(heute.Count, Is.EqualTo(RanchTage.Slots));
            Assert.That(heute.Select(a => a.Id).Distinct().Count(), Is.EqualTo(RanchTage.Slots));
            Assert.That(RanchTage.FuerTag(Tag).Select(a => a.Id), Is.EqualTo(heute.Select(a => a.Id)));
            var gesehen = new HashSet<string>();
            for (var d = 1; d <= 28; d++) foreach (var a in RanchTage.FuerTag("2026-11-" + d.ToString("00"))) gesehen.Add(a.Id);
            Assert.That(gesehen.Count, Is.EqualTo(RanchTage.Pool.Length), "every task comes round");
        }

        [Test]
        public void ProgressFinishesAndPaysOnce()
        {
            var s = Spieler();
            var tag = Enumerable.Range(1, 60).Select(d => "2026-12-" + (d % 28 + 1).ToString("00") + (d > 28 ? "x" + d : ""))
                .First(t => RanchTage.FuerTag(t).Any(a => a.Id == "tages_aepfel"));
            Assert.That(RanchTage.Heute(s, tag).All(x => x.Fortschritt == 0 && !x.Fertig), Is.True);
            Assert.That(RanchTage.Melde(s, tag, "sammle:apfel", 1), Is.Empty);
            Assert.That(RanchTage.Melde(s, tag, "sammle:apfel", 2), Is.EqualTo(new[] { "tages_aepfel" }));
            var stand = RanchTage.Heute(s, tag).First(x => x.Aufgabe.Id == "tages_aepfel");
            Assert.That(stand.Fortschritt, Is.EqualTo(2), "capped at the goal");
            Assert.That(RanchTage.OffenAbzuholen(s, tag), Is.EqualTo(1));
            Assert.That(RanchTage.Abholen(s, tag, "tages_aepfel"), Is.EqualTo(40));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(140));
            Assert.That(RanchTage.Abholen(s, tag, "tages_aepfel"), Is.EqualTo(0), "once");
            Assert.That(RanchTage.Melde(s, tag, "sammle:apfel", 1), Is.Empty);

            var morgen = tag + "+1";
            Assert.That(RanchTage.Heute(s, morgen).All(x => x.Fortschritt == 0 && !x.Abgeholt), Is.True, "a new day starts fresh");
        }

        [Test]
        public void RanchGamesCountFromTheDaysStart()
        {
            var s = Spieler();
            var tag = Enumerable.Range(1, 200).Select(d => "2027-01-" + d).First(t => RanchTage.FuerTag(t).Any(a => a.Id == "tages_reiten"));
            RanchTage.Im(s, tag);
            Assert.That(RanchTage.Heute(s, tag).First(x => x.Aufgabe.Id == "tages_reiten").Fertig, Is.False, "earlier plays do not count");
            s["minigames"]["plays"]["carrotCatch"] = 12;
            Assert.That(RanchTage.Heute(s, tag).First(x => x.Aufgabe.Id == "tages_reiten").Fertig, Is.False, "other games do not count");
            s["minigames"]["plays"]["ranchHerde"] = 1;
            Assert.That(RanchTage.Heute(s, tag).First(x => x.Aufgabe.Id == "tages_reiten").Fertig, Is.True);
            Assert.That(RanchTage.Abholen(s, tag, "tages_reiten"), Is.EqualTo(50));
        }
    }
}
