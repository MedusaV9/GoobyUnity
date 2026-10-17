using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Breeding and foals (ports of test_horse_breeding.gd's rules): the gate, the 48-h pregnancy with its care
    /// checkpoints, the foal rolled from the seed with the parents' genes, the growth phases, and the birth on the farm.
    /// </summary>
    public class RanchZuchtTests
    {
        private const long T0 = 1_800_000_000_000L;
        private const long H = 3_600_000L;

        private static JObject Farm()
        {
            var s = new JObject
            {
                ["progression"] = new JObject { ["level"] = 20 },
                ["economy"] = new JObject { ["coins"] = RanchHof.Preis },
            };
            Assert.That(RanchKauf.Kaufe(s, T0), Is.EqualTo(RanchKauf.Ok));
            foreach (var id in RanchHof.PferdIds(s))
            {
                var p = RanchHof.Pferd(s, id);
                p["xp"] = RanchPferd.XpSummeBis(8);
                p["level"] = 8;
                p["bindung"] = 60.0;
                p["werte"] = new JObject { ["hunger"] = 95.0, ["durst"] = 95.0, ["sauberkeit"] = 95.0 };
            }
            return s;
        }

        [Test]
        public void TheGateAsksForLevelBondMoodAndRest()
        {
            var zucht = new JObject { ["traechtigkeiten"] = new JObject(), ["ruhezeitBis"] = new JObject() };
            var gut = new JObject { ["level"] = 8, ["bindung"] = 45.0 };
            Assert.That(RanchZucht.ZuchtErlaubt("a", gut, gut, 60.0, zucht, T0), Is.EqualTo((true, "")));
            Assert.That(RanchZucht.ZuchtErlaubt("a", new JObject { ["level"] = 7, ["bindung"] = 90.0 }, gut, 99.0, zucht, T0).fehler, Is.EqualTo("level"));
            Assert.That(RanchZucht.ZuchtErlaubt("a", gut, new JObject { ["level"] = 9, ["bindung"] = 44.0 }, 99.0, zucht, T0).fehler, Is.EqualTo("bindung"));
            Assert.That(RanchZucht.ZuchtErlaubt("a", gut, gut, 59.0, zucht, T0).fehler, Is.EqualTo("laune"));
            zucht["ruhezeitBis"]["a"] = T0 + 1;
            Assert.That(RanchZucht.ZuchtErlaubt("a", gut, gut, 99.0, zucht, T0).fehler, Is.EqualTo("ruhezeit"));
            RanchZucht.TraechtigkeitStarten(zucht, "b", gut, T0, 1);
            Assert.That(RanchZucht.ZuchtErlaubt("b", gut, gut, 99.0, zucht, T0).fehler, Is.EqualTo("traechtig"));
            RanchZucht.TraechtigkeitStarten(zucht, "c", gut, T0, 2);
            Assert.That(RanchZucht.ZuchtErlaubt("d", gut, gut, 99.0, zucht, T0).fehler, Is.EqualTo("voll"), "at most two at once");
        }

        [Test]
        public void CareCheckpointsShortenThePregnancy()
        {
            var zucht = new JObject { ["traechtigkeiten"] = new JObject(), ["ruhezeitBis"] = new JObject() };
            RanchZucht.TraechtigkeitStarten(zucht, "a", new JObject(), T0, 7);
            Assert.That(RanchZucht.CheckpointFaellig(RanchZucht.Traechtigkeit(zucht, "a"), T0), Is.EqualTo(0));
            Assert.That(RanchZucht.CheckpointPflegen(zucht, "a", T0), Is.True);
            Assert.That(RanchZucht.CheckpointPflegen(zucht, "a", T0 + H), Is.False, "one per window");
            Assert.That(RanchZucht.CheckpointPflegen(zucht, "a", T0 + 30 * H), Is.True, "a missed one does not hurt — the next window is open");
            Assert.That(RanchZucht.GeburtIn(zucht, "a", T0), Is.EqualTo(44 * H));
            Assert.That(RanchZucht.GeburtBereit(zucht, "a", T0 + 43 * H), Is.False);
            Assert.That(RanchZucht.GeburtBereit(zucht, "a", T0 + 44 * H), Is.True);
            Assert.That(RanchZucht.DauerMs(9), Is.EqualTo(38 * H), "at least 38 h");
        }

        [Test]
        public void TheFoalTakesAfterItsParentsAndGrowsUp()
        {
            var weiss = new JObject { ["farbe"] = "weiss", ["rasse"] = "puschelhufer", ["stats"] = new JObject { ["tempo"] = 12 } };
            var a = RanchZucht.WuerfleFohlen(weiss, weiss, 42);
            Assert.That(J.Str(a["farbe"]), Is.EqualTo("weiss"), "two white parents (h+/h+) give a white foal");
            Assert.That(RanchZucht.WuerfleFohlen(weiss, weiss, 42).ToString(), Is.EqualTo(a.ToString()), "same seed, same foal");
            var schwarz = new JObject { ["farbe"] = "schwarz" };
            Assert.That(J.Str(RanchZucht.WuerfleFohlen(schwarz, schwarz, 3)["farbe"]), Is.EqualTo("schwarz"));
            Assert.That(J.Str(a["rasse"]), Is.EqualTo("puschelhufer"), "pure breed stays");
            var t = J.Int(a["stats"]["tempo"]);
            Assert.That(t, Is.InRange(11, 15), "mean ± the dice");
            Assert.That(((JArray)a["charakter"]).Count, Is.EqualTo(2));
            var mix = RanchZucht.WuerfleFohlen(new JObject { ["rasse"] = "knuffpony" }, new JObject { ["rasse"] = "flitzewind" }, 5);
            Assert.That(new[] { "puschelmix", "knuffpony", "flitzewind" }, Does.Contain(J.Str(mix["rasse"])));

            Assert.That(RanchZucht.AlterFuer(T0, T0 + 1 * 24 * H), Is.EqualTo("fohlen"));
            Assert.That(RanchZucht.AlterFuer(T0, T0 + 4 * 24 * H), Is.EqualTo("jaehrling"));
            Assert.That(RanchZucht.AlterFuer(T0, T0 + 8 * 24 * H), Is.EqualTo("jungpferd"));
            Assert.That(RanchZucht.AlterFuer(T0, T0 + 11 * 24 * H), Is.EqualTo("ausgewachsen"));
            Assert.That(RanchZucht.AlterFuer(0, T0), Is.EqualTo("ausgewachsen"));
            Assert.That(RanchZucht.PhasenSkala("fohlen"), Is.EqualTo(0.55));
            Assert.That(RanchZucht.Reitbar("jaehrling"), Is.False);
            Assert.That(RanchZucht.Reitbar("jungpferd"), Is.True);
        }

        [Test]
        public void ABirthOnTheFarm()
        {
            var s = Farm();
            Assert.That(RanchZucht.Decken(s, "tier_pferd_wolke", "tier_pferd_wolke", T0).fehler, Is.EqualTo("selbe"));
            Assert.That(RanchZucht.Decken(s, "tier_pferd_wolke", "tier_pferd_karamell", T0), Is.EqualTo((true, "")));
            Assert.That(RanchZucht.Geburt(s, "tier_pferd_wolke", T0 + 10 * H), Is.Null, "not yet");
            Assert.That(RanchZucht.Fuersorge(s, "tier_pferd_wolke", T0 + H), Is.True);
            var id = RanchZucht.Geburt(s, "tier_pferd_wolke", T0 + 46 * H);
            Assert.That(id, Is.Not.Null);
            var fohlen = RanchHof.Pferd(s, id);
            Assert.That(J.Str(fohlen["alter"]), Is.EqualTo("fohlen"));
            Assert.That(((JArray)fohlen["eltern"]).Select(e => J.Str(e)), Is.EqualTo(new[] { "tier_pferd_wolke", "tier_pferd_karamell" }));
            Assert.That(J.Str(fohlen["ahnen"]["mutter"]["ref"]), Is.EqualTo("tier_pferd_wolke"));
            Assert.That(RanchZucht.FohlenNamen, Does.Contain(J.Str(fohlen["name"])));
            Assert.That(RanchHof.PferdIds(s).Count, Is.EqualTo(4));
            Assert.That(RanchZucht.Decken(s, "tier_pferd_wolke", "tier_pferd_beere", T0 + 47 * H).fehler, Is.EqualTo("ruhezeit"), "the mare rests five days");
            Assert.That(RanchZucht.Decken(s, id, "tier_pferd_beere", T0 + 47 * H).fehler, Is.EqualTo("jung"), "a foal does not breed");
        }
    }
}
