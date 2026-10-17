using System.Linq;
using Gooby.Core.Audio;
using Gooby.Core.Life;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The ranch's ambience mix, music contexts and hoof beats (test_ranchaudio_ambience.gd / _musik.gd), and its weather seed.</summary>
    public class RanchKlangTests
    {
        private static RanchWetter.Zustand W(string typ, double intensitaet, double wind) =>
            new RanchWetter.Zustand { Typ = typ, Intensitaet = intensitaet, Wind = wind };

        private static readonly RanchWetter.Zustand Sonne = W("sonne", 0.5, 0.3);
        private static readonly RanchWetter.Zustand Regen = W("regen", 0.7, 0.5);
        private static readonly RanchWetter.Zustand Gewitter = W("gewitter", 0.9, 0.8);

        [Test]
        public void ASunnyDayHasBirdsNotCrickets()
        {
            var mix = RanchKlang.AmbienceZiel("hof", Sonne, 12.0);
            Assert.That(mix["voegel"], Is.GreaterThan(0.0));
            Assert.That(mix["grillen"], Is.EqualTo(0.0));
            Assert.That(mix["regen"], Is.EqualTo(0.0));
            Assert.That(mix["gewitter"], Is.EqualTo(0.0));
            Assert.That(mix["wind"], Is.GreaterThan(0.0), "a little wind is always in the air");
        }

        [Test]
        public void AtNightTheCricketsSing()
        {
            var mix = RanchKlang.AmbienceZiel("hof", Sonne, 22.0);
            Assert.That(mix["grillen"], Is.GreaterThan(0.0));
            Assert.That(mix["voegel"], Is.EqualTo(0.0));
        }

        [Test]
        public void TheWeatherChangesTheMix()
        {
            var sonne = RanchKlang.AmbienceZiel("hof", Sonne, 12.0);
            var regen = RanchKlang.AmbienceZiel("hof", Regen, 12.0);
            var gewitter = RanchKlang.AmbienceZiel("hof", Gewitter, 12.0);
            Assert.That(regen["regen"], Is.GreaterThan(0.0));
            Assert.That(regen["voegel"], Is.EqualTo(0.0), "birds fall silent in the rain");
            Assert.That(gewitter["gewitter"], Is.GreaterThan(0.0));
            Assert.That(sonne["gewitter"], Is.EqualTo(0.0));
            Assert.That(gewitter["wind"], Is.GreaterThan(sonne["wind"]));
            Assert.That(RanchKlang.AmbienceZiel("hof", Regen, 23.0)["grillen"], Is.EqualTo(0.0), "rain silences the crickets too");
            Assert.That(RanchKlang.AmbienceZiel("hof", W("nebel", 0.8, 0.2), 10.0)["voegel"], Is.EqualTo(0.2).Within(1e-9), "a few birds in the fog");
        }

        [Test]
        public void TheZoneChangesTheMix()
        {
            var hof = RanchKlang.AmbienceZiel("hof", Sonne, 12.0);
            var bach = RanchKlang.AmbienceZiel("bachlauf", Sonne, 12.0);
            var see = RanchKlang.AmbienceZiel("see", Sonne, 12.0);
            var wald = RanchKlang.AmbienceZiel("waeldchen", Sonne, 12.0);
            var kamm = RanchKlang.AmbienceZiel("huegelkamm", Sonne, 12.0);
            Assert.That(hof["bach"], Is.EqualTo(0.0));
            Assert.That(bach["bach"], Is.GreaterThan(see["bach"]));
            Assert.That(see["bach"], Is.GreaterThan(0.0));
            Assert.That(wald["voegel"], Is.GreaterThan(hof["voegel"]));
            Assert.That(kamm["wind"], Is.GreaterThan(hof["wind"]));
        }

        [Test]
        public void TheBrookFadesWithDistance()
        {
            Assert.That(RanchKlang.EntfernungGain(0.0, 60.0), Is.EqualTo(1.0).Within(1e-4));
            Assert.That(RanchKlang.EntfernungGain(30.0, 60.0), Is.EqualTo(0.5).Within(1e-4));
            Assert.That(RanchKlang.EntfernungGain(90.0, 60.0), Is.EqualTo(0.0).Within(1e-4));
            var nah = RanchKlang.AmbienceZiel("hof", Sonne, 12.0, 10.0);
            var fern = RanchKlang.AmbienceZiel("hof", Sonne, 12.0, 50.0);
            var weg = RanchKlang.AmbienceZiel("hof", Sonne, 12.0, 200.0);
            Assert.That(nah["bach"], Is.GreaterThan(fern["bach"]));
            Assert.That(fern["bach"], Is.GreaterThan(0.0));
            Assert.That(weg["bach"], Is.EqualTo(0.0));
        }

        [Test]
        public void EveryGainStaysBetweenZeroAndOne()
        {
            foreach (var zone in new[] { "hof", "bachlauf", "see", "waeldchen", "huegelkamm", "weidetal" })
                foreach (var wetter in new[] { Sonne, Regen, Gewitter, W("nebel", 1.0, 1.0), null })
                    foreach (var stunde in new[] { 3.0, 12.0, 21.0 })
                    {
                        var mix = RanchKlang.AmbienceZiel(zone, wetter, stunde);
                        Assert.That(mix.Keys, Is.EquivalentTo(RanchKlang.Ebenen));
                        Assert.That(mix.Values.All(g => g >= 0.0 && g <= 1.0), Is.True, zone + "/" + wetter?.Typ + "/" + stunde);
                    }
        }

        [Test]
        public void TheMixBecomesLiveBedsWithEveryClip()
        {
            var betten = RanchKlang.Betten("hof", Gewitter, 15.0);
            Assert.That(betten.Keys, Is.EquivalentTo(RanchKlang.Ebenen.Select(e => "ranch_" + e)));
            Assert.That(betten.Keys.All(k => SoundScape.BedClips.ContainsKey(k)), Is.True, "every layer has its loop");
            Assert.That(betten["ranch_gewitter"], Is.GreaterThan(RanchKlang.AmbienceZiel("hof", Gewitter, 15.0)["gewitter"]), "the storm sits 4 dB up");
            Assert.That(betten["ranch_voegel"], Is.EqualTo(0.0));
        }

        [Test]
        public void PlacesMapToTheirMusic()
        {
            Assert.That(RanchKlang.MusikKontextFuer("ranch/hof", 12.0), Is.EqualTo("ranch"));
            Assert.That(RanchKlang.MusikKontextFuer("ranch/dorf", 12.0), Is.EqualTo("ranch"));
            Assert.That(RanchKlang.MusikKontextFuer("ranch/welt", 12.0), Is.EqualTo("ranch_reiten"));
            Assert.That(RanchKlang.MusikKontextFuer("ranch/fahrt", 12.0), Is.EqualTo("ranch_reiten"));
            Assert.That(RanchKlang.MusikKontextFuer("ranch/turnier", 12.0), Is.EqualTo("ranch_turnier"));
            Assert.That(RanchKlang.MusikKontextFuer("ranch/hof", 22.0), Is.EqualTo("ranch_nacht"));
            Assert.That(RanchKlang.MusikKontextFuer("ranch/welt", 5.5), Is.EqualTo("ranch_nacht"), "the night beats riding");
            Assert.That(RanchKlang.MusikKontextFuer("ranch/turnier", 23.0), Is.EqualTo("ranch_turnier"), "the tournament beats the night");
            Assert.That(RanchKlang.MusikKontextFuer("city", 12.0), Is.EqualTo(""));
            Assert.That(SoundScape.MusicContext("ranch/nacht", false), Is.EqualTo("ranch_nacht"));
        }

        [Test]
        public void ARoundWithFewEffectsGetsItsMusicLifted()
        {
            Assert.That(SoundScape.MusicLiftDb(SoundScape.MusicContext("mg/rocketRescue", false)), Is.EqualTo(5.0));
            Assert.That(SoundScape.MusicLiftDb("game:starHopper"), Is.EqualTo(0.0), "the same track elsewhere stays");
            Assert.That(SoundScape.MusicLiftDb(null), Is.EqualTo(0.0));
        }

        [Test]
        public void HoofBeatsFollowTheGround()
        {
            Assert.That(RanchKlang.HufIdFuer("sand"), Is.EqualTo("ranch_huf_sand"));
            Assert.That(RanchKlang.HufIdFuer("holz"), Is.EqualTo("ranch_huf_holz"));
            Assert.That(RanchKlang.HufIdFuer("matsch"), Is.EqualTo("ranch_huf_gras"));
            Assert.That(RanchKlang.HufIdFuer("lava"), Is.EqualTo("ranch_huf_gras"), "unknown ground falls back to grass");
            Assert.That(RanchKlang.UntergrundInfo("wasser").TempoMult, Is.EqualTo(0.85));
            Assert.That(RanchKlang.UntergrundInfo("lava").Sound, Is.EqualTo("ranch_huf_gras"));
            Assert.That(RanchKlang.Hufschlaege(0.1, 0.4), Is.EqualTo(0));
            Assert.That(RanchKlang.Hufschlaege(0.4, 0.6), Is.EqualTo(1));
            Assert.That(RanchKlang.Hufschlaege(0.0, 0.99), Is.EqualTo(1));
            Assert.That(RanchKlang.Hufschlaege(0.9, 0.2), Is.EqualTo(0), "a wrapped phase counts in two halves by the caller");
            Assert.That(RanchKlang.ReaktionId("begruessung"), Is.EqualTo("ranch_wiehern_a"));
            Assert.That(RanchKlang.ReaktionId("??"), Is.EqualTo(""));
        }

        [Test]
        public void TheWeatherSeedIsStablePerSaveAndHealed()
        {
            var s = new JObject();
            Assert.That(RanchWeltState.WetterSeed(s), Is.EqualTo(RanchWeltState.KartenSeed));
            s["ranch"] = new JObject { ["wetter"] = new JObject { ["seed"] = "kaputt" } };
            Assert.That(RanchWeltState.WetterSeed(s), Is.EqualTo(RanchWeltState.KartenSeed));
            s["ranch"]["wetter"]["seed"] = 77;
            Assert.That(RanchWeltState.WetterSeed(s), Is.EqualTo(77));
            var a = RanchWeltState.Wetter(s, "2026-10-11", 14.5);
            var b = RanchWetter.ZustandBei("2026-10-11", 14.5, 77);
            Assert.That(a.Typ, Is.EqualTo(b.Typ));
            Assert.That(a.Intensitaet, Is.EqualTo(b.Intensitaet));
        }
    }
}
