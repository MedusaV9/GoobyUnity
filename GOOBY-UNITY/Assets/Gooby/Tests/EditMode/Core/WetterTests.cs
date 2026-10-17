using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Life;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Weather (port of tests/unit/test_rwelt_wetter.gd + the soul_wetter rules): the day plan comes from date + seed,
    /// covers 24 h without gaps, knows every type, thunder only in the afternoon/evening, transitions are soft, wetness
    /// builds up and dries, the rainbow comes in the evening after rain; at home the winter turns rain into snow and the
    /// ticker's oracle reports rain or snow outside.
    /// </summary>
    public class WetterTests
    {
        private const long Seed = 20260726;
        private const string Datum = "2026-07-26";

        [Test]
        public void DayPlanIsDeterministic()
        {
            var a = string.Join(",", RanchWetter.Tagesplan(Datum, Seed));
            Assert.That(string.Join(",", RanchWetter.Tagesplan(Datum, Seed)), Is.EqualTo(a), "same date + seed = same plan");
            Assert.That(string.Join(",", RanchWetter.Tagesplan("2026-07-27", Seed)), Is.Not.EqualTo(a), "another date = another plan");
            Assert.That(string.Join(",", RanchWetter.Tagesplan(Datum, Seed + 1)), Is.Not.EqualTo(a), "another seed = another plan");
        }

        [Test]
        public void DayPlanCovers24HoursWithoutGaps()
        {
            var plan = RanchWetter.Tagesplan(Datum, Seed);
            Assert.That(plan.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(plan[0].Von, Is.EqualTo(0.0).Within(0.001));
            Assert.That(plan[plan.Count - 1].Bis, Is.EqualTo(24.0).Within(0.001));
            for (var i = 0; i < plan.Count - 1; i++)
            {
                Assert.That(plan[i].Bis, Is.EqualTo(plan[i + 1].Von).Within(0.001), "no gap at " + i);
                Assert.That(plan[i].Typ, Is.Not.EqualTo(plan[i + 1].Typ), "never the same type twice");
            }
        }

        [Test]
        public void EveryWeatherTypeOccurs()
        {
            var seen = new HashSet<string>();
            for (var tag = 0; tag < 60; tag++)
            {
                var datum = $"2026-{1 + tag / 28:D2}-{1 + tag % 28:D2}";
                foreach (var s in RanchWetter.Tagesplan(datum, Seed)) seen.Add(s.Typ);
            }
            Assert.That(seen, Is.EquivalentTo(RanchWetter.Typen));
        }

        [Test]
        public void ThunderOnlyStartsInTheAfternoonOrEvening()
        {
            for (var tag = 0; tag < 40; tag++)
                foreach (var s in RanchWetter.Tagesplan($"2026-06-{1 + tag % 28:D2}", Seed + tag))
                    if (s.Typ == "gewitter") Assert.That(s.Von, Is.InRange(13.0, 21.9999), "thunder starts at " + s.Von);
        }

        [Test]
        public void MomentStaysInItsRanges()
        {
            for (var i = 0; i < 96; i++)
            {
                var z = RanchWetter.ZustandBei(Datum, i * 0.25, Seed);
                Assert.That(RanchWetter.Typen, Has.Member(z.Typ));
                Assert.That(z.Blend, Is.InRange(0.0, 1.0));
                Assert.That(z.Bewoelkung, Is.InRange(0.0, 1.0));
                Assert.That(z.Naesse, Is.InRange(0.0, 1.0));
                Assert.That(z.LichtFaktor, Is.GreaterThan(0.0), "never pitch dark by day");
                Assert.That(z.NameKey, Is.EqualTo("rwelt.wetter." + z.Typ));
            }
        }

        [Test]
        public void TransitionsAreSoft()
        {
            const double step = 2.0 / 60.0;
            var before = RanchWetter.ZustandBei(Datum, 0.0, Seed);
            for (var h = step; h < 24.0; h += step)
            {
                var now = RanchWetter.ZustandBei(Datum, h, Seed);
                Assert.That(Math.Abs(now.Bewoelkung - before.Bewoelkung), Is.LessThanOrEqualTo(0.25), "jump at " + h.ToString("0.00"));
                before = now;
            }
        }

        [Test]
        public void WetnessBuildsUpAndDries()
        {
            var plan = new List<RanchWetter.Segment>
            {
                new RanchWetter.Segment { Typ = "sonne", Von = 0.0, Bis = 10.0, Intensitaet = 1.0, Wind = 0.2 },
                new RanchWetter.Segment { Typ = "regen", Von = 10.0, Bis = 12.0, Intensitaet = 1.0, Wind = 0.5 },
                new RanchWetter.Segment { Typ = "sonne", Von = 12.0, Bis = 24.0, Intensitaet = 1.0, Wind = 0.2 },
            };
            Assert.That(RanchWetter.Naesse(plan, 9.5), Is.EqualTo(0.0).Within(0.001), "dry before the rain");
            Assert.That(RanchWetter.Naesse(plan, 10.1), Is.GreaterThan(0.2), "gets wet");
            Assert.That(RanchWetter.Naesse(plan, 11.0), Is.EqualTo(1.0).Within(0.001), "soaked in the rain");
            Assert.That(RanchWetter.Naesse(plan, 12.5), Is.GreaterThan(RanchWetter.Naesse(plan, 13.5)), "dries");
            Assert.That(RanchWetter.Naesse(plan, 15.0), Is.EqualTo(0.0).Within(0.001), "dry again after 2 h");
        }

        [Test]
        public void RainbowComesInTheEveningAfterRain()
        {
            Assert.That(RanchWetter.IstRegenbogen("sonne", 0.5, 18.0), Is.True);
            Assert.That(RanchWetter.IstRegenbogen("regen", 0.9, 18.0), Is.False, "not IN the rain");
            Assert.That(RanchWetter.IstRegenbogen("sonne", 0.5, 11.0), Is.False, "not at noon");
            Assert.That(RanchWetter.IstRegenbogen("sonne", 0.0, 18.0), Is.False, "not without wetness");
        }

        [Test]
        public void GustIsContinuousAndPositive()
        {
            for (var i = 0; i < 40; i++) Assert.That(RanchWetter.Boe(i * 0.35, 0.8), Is.InRange(0.0, 1.3));
            Assert.That(RanchWetter.Boe(3.0, 0.0), Is.EqualTo(0.0).Within(0.001), "no wind, no gust");
        }

        [Test]
        public void HomeWeatherTurnsRainToSnowInWinter()
        {
            Assert.That(SoulWetter.MonatVon("2026-12-03"), Is.EqualTo(12));
            Assert.That(SoulWetter.MonatVon("kaputt"), Is.EqualTo(0), "never crash on junk");
            Assert.That(SoulWetter.IstWinter("2027-01-15"), Is.True);
            Assert.That(SoulWetter.IstWinter("2026-07-15"), Is.False);
            var summerRain = 0;
            var winterSnow = 0;
            for (var d = 1; d <= 28; d++)
            {
                for (var h = 0; h < 24; h++)
                {
                    var summer = SoulWetter.Zustand($"2026-07-{d:D2}", h);
                    Assert.That(summer.Schnee, Is.False, "no snow in July");
                    if (summer.Regen) summerRain++;
                    var winter = SoulWetter.Zustand($"2027-01-{d:D2}", h);
                    Assert.That(winter.Regen, Is.False, "January rain falls as snow");
                    if (winter.Schnee)
                    {
                        winterSnow++;
                        Assert.That(winter.Typ, Is.EqualTo("schnee"));
                        Assert.That(RanchWetter.RegenTypen, Has.Member(winter.Basis.Typ));
                    }
                }
            }
            Assert.That(summerRain, Is.GreaterThan(0), "it rains some summer hours");
            Assert.That(winterSnow, Is.GreaterThan(0), "it snows some winter hours");
            var a = SoulWetter.Zustand("2026-10-02", 9.5);
            Assert.That(SoulWetter.Zustand("2026-10-02", 9.5).Typ, Is.EqualTo(a.Typ), "stable per day and hour");
        }

        [Test]
        public void OracleReportsRainOrSnowOutside()
        {
            var oracle = new SoulWetter.Oracle();
            var hours = Enumerable.Range(0, 28 * 24).Select(i => ($"2026-07-{1 + i / 24:D2}", (double)(i % 24))).ToList();
            Assert.That(hours.Any(x => oracle.IsWetOutside(x.Item1, x.Item2)), Is.True);
            Assert.That(hours.Any(x => !oracle.IsWetOutside(x.Item1, x.Item2)), Is.True);
            foreach (var (datum, h) in hours.Take(48))
            {
                var lage = SoulWetter.Zustand(datum, h);
                Assert.That(oracle.IsWetOutside(datum, h), Is.EqualTo(lage.Regen || lage.Schnee));
            }
        }

        private static SoulWetter.Lage Lage(string typ, double intensitaet = 0.8, bool winter = false)
        {
            var regnet = Array.IndexOf(RanchWetter.RegenTypen, typ) >= 0;
            var basis = new RanchWetter.Zustand
            {
                Typ = typ, Vorher = typ, Blend = 1.0, Intensitaet = intensitaet,
                LichtFaktor = RanchWetter.LichtVon(typ), Bewoelkung = RanchWetter.BewoelkungVon(typ),
                NebelDichte = typ == "nebel" ? 0.9 : typ == "regen" ? 0.18 : 0.0,
            };
            return new SoulWetter.Lage { Typ = winter && regnet ? "schnee" : typ, Regen = regnet && !winter, Schnee = regnet && winter, Basis = basis };
        }

        [Test]
        public void FxPlanMapsTheWeather()
        {
            var sonne = WetterFx.Fuer(Lage("sonne"), false, false);
            Assert.That((sonne.Regen, sonne.Schnee, sonne.Blitz, sonne.Nebel, sonne.SonnenFaktor), Is.EqualTo((false, false, false, 0.0, 1.0)), "sun: nothing falls, full light");
            var regen = WetterFx.Fuer(Lage("regen"), false, false);
            Assert.That((regen.Regen, regen.Spritzer, regen.Blitz, regen.Nass), Is.EqualTo((true, true, false, true)), "rain with splash rings outdoors");
            var gewitter = WetterFx.Fuer(Lage("gewitter"), false, false);
            Assert.That((gewitter.Regen, gewitter.Blitz), Is.EqualTo((true, true)), "a thunderstorm rains and flashes");
            var schnee = WetterFx.Fuer(Lage("regen", winter: true), false, false);
            Assert.That((schnee.Schnee, schnee.Regen, schnee.Spritzer), Is.EqualTo((true, false, false)), "winter: snow instead of rain");
            Assert.That(WetterFx.Fuer(Lage("nebel"), false, false).Nebel, Is.GreaterThan(0.5), "fog density");
            var niesel = WetterFx.Fuer(Lage("niesel"), false, false);
            Assert.That(niesel.Staerke, Is.LessThan(regen.Staerke), "drizzle is lighter than rain");
            Assert.That(WetterFx.Fuer(Lage("regen"), false, false).Staerke, Is.EqualTo(regen.Staerke), "deterministic");
            Assert.That(gewitter.SonnenFaktor, Is.LessThan(regen.SonnenFaktor).And.LessThan(1.0), "the darker the sky, the weaker the sun");
            Assert.That(gewitter.AmbientFaktor, Is.GreaterThan(gewitter.SonnenFaktor), "ambient dims less than the sun (75 % vs 85 %)");
            Assert.That(schnee.SonnenFaktor, Is.GreaterThan(regen.SonnenFaktor), "a snow day is lit like an overcast day, brighter than rain");
            Assert.That(regen.Grau, Is.EqualTo(RanchWetter.BewoelkungVon("regen") * 0.55).Within(1e-9));
            Assert.That(WetterFx.Fuer(null, false, false).Typ, Is.EqualTo("sonne"));
        }

        [Test]
        public void FxPlanIndoorAndReducedMotion()
        {
            var innen = WetterFx.Fuer(Lage("regen"), true, false);
            Assert.That((innen.Regen, innen.Spritzer), Is.EqualTo((true, false)), "indoors: streaks, no splash rings");
            var reduced = WetterFx.Fuer(Lage("gewitter"), false, true);
            Assert.That((reduced.Regen, reduced.Spritzer, reduced.Blitz), Is.EqualTo((false, false, false)), "reduced motion: no particles, no flash");
            Assert.That(WetterFx.Fuer(Lage("regen", winter: true), false, true).Schnee, Is.False, "reduced motion: no snow");
            Assert.That(WetterFx.Fuer(Lage("nebel"), false, true).Nebel, Is.GreaterThan(0.5), "the (static) fog stays");
            Assert.That(reduced.SonnenFaktor, Is.LessThan(1.0), "the light still dims");
        }

        [Test]
        public void FxPlanFollowsTheRealHomePlan()
        {
            var nass = 0;
            for (var tag = 1; tag <= 28; tag++)
            {
                var datum = "2026-09-" + tag.ToString("00");
                for (var h = 0.0; h < 24.0; h += 1.0)
                {
                    var lage = SoulWetter.Zustand(datum, h);
                    var plan = WetterFx.Fuer(lage, false, false);
                    Assert.That(plan.Regen, Is.EqualTo(lage.Regen), datum + " " + h);
                    Assert.That(plan.Schnee, Is.False, "no snow in September");
                    Assert.That(plan.SonnenFaktor, Is.InRange(0.3, 1.0));
                    if (plan.Regen) nass++;
                }
            }
            Assert.That(nass, Is.GreaterThan(24), "some rainy hours in four weeks");
        }
    }
}
