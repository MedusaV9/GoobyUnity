using System.IO;
using System.Linq;
using Gooby.Core.Audio;
using Gooby.Core.Minigames;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Music registry, scene sound mapping, radio queue and babble — ports of the Godot audio contracts
    /// (test_fix4_music*.gd, test_w13_radio_gates.gd, test_j5_klangbett.gd, test_ef2_voice.gd).
    /// </summary>
    public class AudioCoreTests
    {
        [SetUp]
        public void Install()
        {
            Fixtures.InstallContentSource();
            MusicRegistry.ResetCache();
        }

        [Test]
        public void EveryRequiredContextHasATrackAndItsFileShips()
        {
            var required = new[] { "home", "home_night", "garden", "city", "shop", "vet", "arcade", "vacation", "room:kitchen",
                "room:bathroom", "room:bedroom", "ranch", "ranch_reiten", "ranch_turnier", "ranch_nacht", "ranch_menue", "park", "space" };
            var audioRoot = Path.GetFullPath(Path.Combine(Fixtures.ResourcesRoot, "..", "..", "..", "Art", "Resources"));
            foreach (var ctx in required)
            {
                var id = MusicRegistry.TrackFor(ctx);
                Assert.That(id, Is.Not.Empty, ctx);
                var file = Path.Combine(audioRoot, MusicRegistry.ResourcePath(id) + ".ogg");
                Assert.That(File.Exists(file), Is.True, ctx + " → " + file);
            }
        }

        [Test]
        public void EveryTrackIsMeasuredAndTheTrimsBringScenesWithinTwoDecibels()
        {
            var levels = new System.Collections.Generic.List<double>();
            var loudness = Gooby.Core.Content.ContentSource.LoadObject(MusicRegistry.LoudnessPath);
            foreach (var t in MusicRegistry.Tracks.Values)
            {
                var m = loudness["tracks"]?[t.Id];
                Assert.That(m, Is.Not.Null, t.Id + " has no measured loudness (tools/audio/measure_loudness.py)");
                var trim = MusicRegistry.LoudnessTrimDb(t.Id);
                if (t.Category == "Stinger") { Assert.That(trim, Is.EqualTo(0.0), t.Id); continue; }
                Assert.That(trim, Is.LessThanOrEqualTo(MusicRegistry.MaxLiftDb), t.Id);
                // a lift never pushes the true peak past −1 dB
                if (trim > 0) Assert.That((double)m["peak_db"] + trim, Is.LessThanOrEqualTo(-1.0 + 1e-9), t.Id);
                levels.Add((double)m["lufs"] + trim);
            }
            Assert.That(levels.Max() - levels.Min(), Is.LessThanOrEqualTo(2.0), "loudness spread after the trims");
        }

        [Test]
        public void GodotContextsResolveLikeTheWebRegistry()
        {
            Assert.That(MusicRegistry.TrackFor("home"), Is.EqualTo("room-cloud-hopper-s-day-off"));
            Assert.That(MusicRegistry.TrackFor("room:bedroom"), Is.EqualTo("room-pixie-puddle-awake"));
            Assert.That(MusicRegistry.TrackFor("room:bedroom", sleeping: true), Is.EqualTo("room-pixie-puddle-sleeping"));
            Assert.That(MusicRegistry.TrackFor("home_night"), Is.EqualTo("room-pixie-puddle-sleeping"));
            Assert.That(MusicRegistry.TrackFor("vacation"), Is.EqualTo("bordmusik-vacation-day"));
            Assert.That(MusicRegistry.TrackFor("vet"), Is.EqualTo("location-sanfte-pfoten"));
            Assert.That(MusicRegistry.TrackFor("game:toyRacer"), Is.EqualTo("game-spielzeugflitzer"));
            Assert.That(MusicRegistry.TrackFor("stinger-levelup"), Is.Empty, "stingers are no context tracks");
            Assert.That(MusicRegistry.IsStinger("stinger-levelup"), Is.True);
            Assert.That(MusicRegistry.TrackFor("nowhere"), Is.Empty);
        }

        [Test]
        public void EveryMinigameHasMusicAndMostHaveTheirOwn()
        {
            var arcade = MusicRegistry.TrackFor("arcade");
            var games = MinigameRegistry.AllGames();
            var own = 0;
            foreach (var g in games)
            {
                var id = MusicRegistry.TrackFor("game:" + g.Id);
                Assert.That(MusicRegistry.Get(id), Is.Not.Null, g.Id);
                if (id != arcade) own++;
            }
            Assert.That(own, Is.GreaterThanOrEqualTo(games.Count - 4), "only a handful may share the arcade loop");
        }

        [Test]
        public void StationsAreGatedByLevelAndNeverCarryStingers()
        {
            Assert.That(MusicRegistry.Stations().Select(s => s.id), Is.EquivalentTo(new[] { "bordmusik", "gooby-fm", "recap-fm", "game-fm", "alle" }));
            Assert.That(RadioQueue.StationUnlocked("game-fm", 7), Is.False);
            Assert.That(RadioQueue.StationUnlocked("game-fm", 8), Is.True);
            foreach (var s in MusicRegistry.Stations())
                Assert.That(MusicRegistry.StationTrackIds(s.id).Any(MusicRegistry.IsStinger), Is.False, s.id);
            Assert.That(MusicRegistry.StationTrackIds("gooby-fm").Count, Is.EqualTo(16));
        }

        [Test]
        public void RadioPlaysEveryTrackOnceBeforeRepeating()
        {
            var q = new RadioQueue("gooby-fm", 30, new GoobyRng(7));
            var n = q.Playable().Count;
            var first = Enumerable.Range(0, n).Select(_ => q.Next()).ToList();
            Assert.That(first.Distinct().Count(), Is.EqualTo(n));
            var next = q.Next();
            Assert.That(next, Is.Not.EqualTo(first[n - 1]), "no repeat across the reshuffle");
            Assert.That(new RadioQueue("gooby-fm", 30, new GoobyRng(7)).Next(), Is.EqualTo(first[0]), "deterministic per seed");
        }

        [Test]
        public void ScenesMapToMusicAndBeds()
        {
            Assert.That(SoundScape.MusicContext("home/living", false), Is.EqualTo("home"));
            Assert.That(SoundScape.MusicContext("home/living", true), Is.EqualTo("home_night"));
            Assert.That(SoundScape.MusicContext("home/garden", false), Is.EqualTo("garden"));
            Assert.That(SoundScape.MusicContext("city/ort/tierarzt", false), Is.EqualTo("vet"));
            Assert.That(SoundScape.MusicContext("city/ort/mart", false), Is.EqualTo("shop"));
            Assert.That(SoundScape.MusicContext("city/ort/flughafen", false), Is.EqualTo("vacation"));
            Assert.That(SoundScape.MusicContext("city/ort/funkelpark", false), Is.EqualTo("park"));
            Assert.That(SoundScape.MusicContext("mg/bubblePop", false), Is.EqualTo("game:bubblePop"));
            Assert.That(SoundScape.MusicContext("ranch/turnier", false), Is.EqualTo("ranch_turnier"));
            Assert.That(SoundScape.MusicContext("ranch/hof", false), Is.EqualTo("ranch"));
            Assert.That(SoundScape.MusicContext("ranch/dorf", false), Is.EqualTo("ranch"));
            Assert.That(SoundScape.MusicContext("ranch/hof/nacht", false), Is.EqualTo("ranch_nacht"));
            Assert.That(SoundScape.MusicContext("ranch/reiten", false), Is.EqualTo("ranch_reiten"));
            Assert.That(SoundScape.BedPlan("ranch/hof"), Is.Empty, "the ranch drives its layers live");
            Assert.That(SoundScape.MusicContext("settings", false), Is.Null, "unknown keys keep what plays");

            Assert.That(SoundScape.BedPlan("home/living")["kamin"], Is.EqualTo(0.85));
            Assert.That(SoundScape.BedPlan("home/garden").Keys, Is.EquivalentTo(new[] { "voegel", "wind" }));
            Assert.That(SoundScape.BedPlan("city/ort/post")["laden"], Is.EqualTo(0.7), "interiors fall back to the shop room tone");
            Assert.That(SoundScape.BedPlan("mg/carrotCatch"), Is.Empty, "minigames bring their own sound");
            Assert.That(SoundScape.BedPlan("settings"), Is.Null);
            foreach (var clip in SoundScape.BedClips.Values)
            {
                var file = Path.GetFullPath(Path.Combine(Fixtures.ResourcesRoot, "..", "..", "..", "Art", "Resources", "gooby-audio", "ambient", clip + ".ogg"));
                Assert.That(File.Exists(file), Is.True, file);
            }
        }

        [Test]
        public void DuckingDipsFastAndRecoversSlowly()
        {
            Assert.That(SoundScape.DuckTarget(true, 0), Is.EqualTo(SoundScape.DuckFactor));
            Assert.That(SoundScape.DuckTarget(false, 0.5), Is.EqualTo(SoundScape.DuckFactor));
            Assert.That(SoundScape.DuckTarget(false, 0), Is.EqualTo(1.0));
            Assert.That(SoundScape.DuckStep(1.0, 0.25, 0.25), Is.EqualTo(0.25).Within(1e-9), "down in 0.25 s");
            Assert.That(SoundScape.DuckStep(0.25, 1.0, 0.45), Is.EqualTo(0.625).Within(1e-9), "half way up after 0.45 s");
        }

        [Test]
        public void BabbleIsDeterministicPerWordAndRisesOnQuestions()
        {
            var a = GoobyBabble.Plan("Hallo Gooby", "neutral", new GoobyRng(3));
            var b = GoobyBabble.Plan("Hallo Gooby", "neutral", new GoobyRng(3));
            Assert.That(a.Select(s => s.Clip), Is.EqualTo(b.Select(s => s.Clip)));
            Assert.That(a.Count, Is.InRange(GoobyBabble.MinSyllables, GoobyBabble.MaxSyllables));
            Assert.That(a[1].At, Is.EqualTo(1.0 / GoobyBabble.Rate).Within(1e-9));
            Assert.That(GoobyBabble.Plan("...", "neutral", new GoobyRng(3)), Is.Empty);
            var statement = GoobyBabble.Plan("Wirklich", "neutral", new GoobyRng(9));
            var question = GoobyBabble.Plan("Wirklich?", "neutral", new GoobyRng(9));
            Assert.That(question.Last().Pitch, Is.GreaterThan(statement.Last().Pitch), "a question ends higher");
            var happy = GoobyBabble.Plan("Juhu", "happy", new GoobyRng(1));
            var sad = GoobyBabble.Plan("Juhu", "sad", new GoobyRng(1));
            Assert.That(happy[0].Pitch, Is.GreaterThan(sad[0].Pitch));
        }
    }
}
