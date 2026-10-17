using System.Linq;
using Gooby.Core.Minigames;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of test_w19_geist_rekord.gd's pure part: recording, budget, interpolation, record replacement.</summary>
    public class GeistRekordTests
    {
        private const double Eps = 1e-6;

        private static int[] Kurve(JObject snap) => ((JArray)snap["kurve"]).Select(t => t.Value<int>()).ToArray();

        [Test]
        public void RecordingIsDeterministic()
        {
            JObject first = null;
            for (var lauf = 0; lauf < 2; lauf++)
            {
                var rec = new GeistRekord();
                rec.Starte(null);
                rec.MeldeLiveScore();
                rec.Tick(0.4, 1);
                rec.Tick(0.4, 2);
                rec.Tick(0.4, 3);
                rec.Tick(0.8, 5);
                rec.Tick(1.0, 8);
                var snap = rec.Snapshot(9);
                if (first == null) { first = snap; continue; }
                Assert.That(JToken.DeepEquals(first, snap), "same ticks, same snapshot");
            }
            Assert.That(Kurve(first), Is.EqualTo(new[] { 0, 3, 5, 8 }), "samples at the 1-Hz boundaries");
            Assert.That(first["score"].Value<int>(), Is.EqualTo(9));
            Assert.That(first["schritt_sec"].Value<double>(), Is.EqualTo(1.0).Within(Eps));
            Assert.That(first["dauer_sec"].Value<double>(), Is.EqualTo(3.0).Within(Eps));
        }

        [Test]
        public void BudgetCompactsAndClamps()
        {
            var rec = new GeistRekord();
            rec.Starte(null);
            rec.MeldeLiveScore();
            for (var i = 0; i < 500; i++) rec.Tick(1.0, i);
            var snap = rec.Snapshot(500);
            Assert.That(Kurve(snap).Length, Is.LessThanOrEqualTo(GeistRekord.MaxStuetzen));
            Assert.That(snap["schritt_sec"].Value<double>(), Is.EqualTo(4.0).Within(Eps), "compacted twice");
            Assert.That(GeistRekord.WertBei(snap, 320.0), Is.EqualTo(319.0).Within(Eps), "the time axis survives");

            var neg = new GeistRekord();
            neg.Starte(null);
            neg.MeldeLiveScore();
            neg.Tick(1.0, -7);
            var snap2 = neg.Snapshot(-3);
            Assert.That(Kurve(snap2), Is.EqualTo(new[] { 0, 0 }), "a negative sample is 0");
            Assert.That(snap2["score"].Value<int>(), Is.EqualTo(0));

            var still = new GeistRekord();
            still.Starte(null);
            Assert.That(still.Snapshot(42), Is.Null, "no live score, no snapshot");
        }

        [Test]
        public void InterpolatesAtAndBetweenSamples()
        {
            var rekord = JObject.Parse("{\"score\": 30.0, \"schritt_sec\": 1.0, \"dauer_sec\": 4.0, \"kurve\": [0.0, 10.0, 20.0]}");
            Assert.That(GeistRekord.WertBei(rekord, 0.0), Is.EqualTo(0.0).Within(Eps));
            Assert.That(GeistRekord.WertBei(rekord, 1.0), Is.EqualTo(10.0).Within(Eps));
            Assert.That(GeistRekord.WertBei(rekord, 1.5), Is.EqualTo(15.0).Within(Eps));
            Assert.That(GeistRekord.WertBei(rekord, 2.0), Is.EqualTo(20.0).Within(Eps));
            Assert.That(GeistRekord.WertBei(rekord, 3.0), Is.EqualTo(25.0).Within(Eps), "runs out to the final score");
            Assert.That(GeistRekord.WertBei(rekord, 99.0), Is.EqualTo(30.0).Within(Eps));
            Assert.That(GeistRekord.DeltaFuer(rekord, 1.5, 20), Is.EqualTo(5));
            Assert.That(GeistRekord.DeltaFuer(rekord, 1.5, 10), Is.EqualTo(-5));
            Assert.That(GeistRekord.WertBei(new JObject(), 3.0), Is.EqualTo(0.0), "an invalid record never throws");
            Assert.That(GeistRekord.DeltaText(12), Is.EqualTo("+12"));
            Assert.That(GeistRekord.DeltaText(-5), Is.EqualTo("\u22125"));
            Assert.That(GeistRekord.DeltaText(0), Is.EqualTo("±0"));
        }

        [Test]
        public void ReplacesTheCurveExactlyOnANewRecord()
        {
            var state = new JObject { ["minigames"] = new JObject() };
            var erst = JObject.Parse("{\"score\": 50, \"schritt_sec\": 1.0, \"dauer_sec\": 2.0, \"kurve\": [0, 20, 50]}");
            Assert.That(GeistRekord.UebernehmeRekord(state, "x", erst), Is.True, "the first run lays the curve");
            Assert.That(Kurve(GeistRekord.RekordVon(state, "x")), Is.EqualTo(new[] { 0, 20, 50 }));
            var schwach = JObject.Parse("{\"score\": 40, \"schritt_sec\": 1.0, \"dauer_sec\": 1.0, \"kurve\": [0, 40]}");
            Assert.That(GeistRekord.UebernehmeRekord(state, "x", schwach), Is.False);
            var gleich = JObject.Parse("{\"score\": 50, \"schritt_sec\": 1.0, \"dauer_sec\": 1.0, \"kurve\": [0, 50]}");
            Assert.That(GeistRekord.UebernehmeRekord(state, "x", gleich), Is.False, "a tie does not replace");
            Assert.That(Kurve(GeistRekord.RekordVon(state, "x")), Is.EqualTo(new[] { 0, 20, 50 }));
            var stark = JObject.Parse("{\"score\": 60, \"schritt_sec\": 1.0, \"dauer_sec\": 1.0, \"kurve\": [0, 60]}");
            Assert.That(GeistRekord.UebernehmeRekord(state, "x", stark), Is.True);
            Assert.That(Kurve(GeistRekord.RekordVon(state, "x")), Is.EqualTo(new[] { 0, 60 }));
            Assert.That(GeistRekord.UebernehmeRekord(state, "x", new JObject()), Is.False);
            Assert.That(GeistRekord.UebernehmeRekord(state, "x", null), Is.False);
            var quatsch = JObject.Parse("{\"minigames\": {\"geist\": {\"x\": \"quatsch\"}}}");
            Assert.That(GeistRekord.RekordVon(quatsch, "x"), Is.Null);
        }

        [Test]
        public void TheChipComparesAgainstTheReferenceOnlyWhenOneExists()
        {
            var flach = JObject.Parse("{\"score\": 100, \"schritt_sec\": 1.0, \"dauer_sec\": 10.0, \"kurve\": [0,0,0,0,0,0,0,0,0,0,0]}");
            var rec = new GeistRekord();
            rec.Starte(null);
            Assert.That(rec.HatReferenz, Is.False);
            rec.Starte(flach);
            Assert.That(rec.HatReferenz && !rec.HatLiveScore, "the chip waits for the first live score");
            rec.MeldeLiveScore();
            rec.Tick(2.0, 7);
            Assert.That(rec.DeltaAktuell(7), Is.EqualTo(7), "the flat ghost is still at 0 after two seconds");
            Assert.That(rec.Geschlagen(100), Is.False, "a tie does not beat the ghost");
            Assert.That(rec.Geschlagen(101), Is.True);
        }
    }
}
