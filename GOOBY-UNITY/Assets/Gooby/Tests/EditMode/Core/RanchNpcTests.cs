using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Friendship with Hufingen's shopkeepers (port of test_rnpc_freundschaft.gd's rules) and their shop discounts.</summary>
    public class RanchNpcTests
    {
        private const long T0 = 1_800_000_000_000L;
        private const long Tag = 86_400_000L;

        [Test]
        public void HeartsTalkGiftsAndGentleFading()
        {
            Assert.That(RanchNpc.Herzen(0), Is.EqualTo(0));
            Assert.That(RanchNpc.Herzen(10), Is.EqualTo(1));
            Assert.That(RanchNpc.Herzen(69.9), Is.EqualTo(3));
            Assert.That(RanchNpc.Herzen(100), Is.EqualTo(5));
            Assert.That(RanchNpc.PunkteBisNaechste(20), Is.EqualTo(5));
            Assert.That(RanchNpc.PunkteNachVerfall(30, T0, T0 + 7 * Tag), Is.EqualTo(30), "seven days of grace");
            Assert.That(RanchNpc.PunkteNachVerfall(30, T0, T0 + 10 * Tag), Is.EqualTo(27).Within(1e-9));
            Assert.That(RanchNpc.PunkteNachVerfall(27, T0, T0 + 60 * Tag), Is.EqualTo(25), "never below the reached heart");

            var s = new JObject { ["progression"] = new JObject { ["level"] = 20 }, ["economy"] = new JObject { ["coins"] = RanchHof.Preis } };
            RanchKauf.Kaufe(s, T0);
            Assert.That(RanchNpc.Reden(s, "wilma", "d1", T0), Is.EqualTo((0, 0)));
            Assert.That(RanchNpc.Punkte(s, "wilma"), Is.EqualTo(3));
            RanchNpc.Reden(s, "wilma", "d1", T0);
            Assert.That(RanchNpc.Punkte(s, "wilma"), Is.EqualTo(3.5), "talking twice a day only consoles");
            var heu = J.Int(RanchHof.Lager(s)["heu"]);
            var (reaktion, vorher, nachher) = RanchNpc.Schenken(s, "wilma", "heu", "d1", T0);
            Assert.That(reaktion, Is.EqualTo("liebt"));
            Assert.That((vorher, nachher), Is.EqualTo((0, 1)));
            Assert.That(RanchNpc.Punkte(s, "wilma"), Is.EqualTo(15.5));
            Assert.That(J.Int(RanchHof.Lager(s)["heu"]), Is.EqualTo(heu - 1), "the gift comes out of the store");
            Assert.That(RanchNpc.Schenken(s, "wilma", "apfel", "d1", T0).reaktion, Is.EqualTo("mag"));
            Assert.That(RanchNpc.Punkte(s, "wilma"), Is.EqualTo(16.5), "a second gift that day counts one");
            Assert.That(RanchNpc.Schenken(s, "wilma", "hafer", "d1", T0).reaktion, Is.Null, "no oats in the bag");
        }

        [Test]
        public void FriendsGiveADiscountInTheirShop()
        {
            var s = new JObject { ["progression"] = new JObject { ["level"] = 20 }, ["economy"] = new JObject { ["coins"] = RanchHof.Preis + 2000 } };
            RanchKauf.Kaufe(s, T0);
            Assert.That(RanchNpc.Rabatt(s, RanchDorf.Schmiede), Is.EqualTo(0));
            RanchNpc.Freund(s, "eisenhuf")["punkte"] = 25.0;
            Assert.That(RanchNpc.Rabatt(s, RanchDorf.Schmiede), Is.EqualTo(10), "two hearts");
            var vorher = J.Int(s["economy"]["coins"]);
            Assert.That(DorfWirtschaft.SchmiedeKaufen(s, "hufeisen_silber"), Is.EqualTo((DorfWirtschaft.Ok, 270)));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(vorher - 270));
            Assert.That(RanchNpc.Rabatt(s, RanchDorf.Moebelscheune), Is.EqualTo(0), "Alwin gives stories, not discounts");
            Assert.That(RanchNpc.MitRabatt(35, 5), Is.EqualTo(33));
        }
    }
}
