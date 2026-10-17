using System.IO;
using System.Linq;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The ranch people's daily routes (rnpc_routine.gd): stations, the night wrap, the walk's origin, the next change.</summary>
    public class RanchNpcRoutineTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            RanchNpcRoutine.ResetCache();
        }

        [Test]
        public void EveryPersonHasADayOfTwoToFourStations()
        {
            foreach (var id in new[] { "rosi", "moehrchen", "timmi", "miez", "luna", "knips", "punktabzug", "eilfried", "selma", "marta", "alwin", "wilma", "eisenhuf" })
            {
                var r = RanchNpcRoutine.Routine(id);
                Assert.That(r.Count, Is.InRange(2, 4), id);
                Assert.That(r.Select(s => s.Von), Is.Ordered, id);
            }
            Assert.That(RanchNpcRoutine.Routine("niemand"), Is.Empty);
        }

        [Test]
        public void RosisDayLikeGodot()
        {
            var r = RanchNpcRoutine.Routine("rosi");
            Assert.That(RanchNpcRoutine.StationBei(r, 6.0).Ort, Is.EqualTo("stall"));
            Assert.That(RanchNpcRoutine.StationBei(r, 12.5).Ort, Is.EqualTo("trog"));
            Assert.That(RanchNpcRoutine.StationBei(r, 14.0).Taetigkeit, Is.EqualTo("arbeiten"));
            Assert.That(RanchNpcRoutine.StationBei(r, 23.0).Ort, Is.EqualTo("haus"));
            Assert.That(RanchNpcRoutine.StationBei(r, 3.0).Ort, Is.EqualTo("haus"), "before six the evening's station holds");
            Assert.That(RanchNpcRoutine.Vorher(r, RanchNpcRoutine.StationBei(r, 12.5)).Ort, Is.EqualTo("stall"), "she walks from the stable to the trough");
            Assert.That(RanchNpcRoutine.Vorher(r, RanchNpcRoutine.StationBei(r, 6.0)).Ort, Is.EqualTo("haus"), "and in the morning from the house");
            var naechste = RanchNpcRoutine.Naechste(r, 12.5);
            Assert.That((naechste.Ort, naechste.Von), Is.EqualTo(("koppel_pferde", 14.0)));
            Assert.That(RanchNpcRoutine.Naechste(r, 23.0).Ort, Is.EqualTo("stall"), "after the evening comes the morning");
        }

        [Test]
        public void EveryPlaceOfTheRoutinesHasItsNameInBothLanguages()
        {
            foreach (var loc in new[] { "de", "en" })
            {
                var s = JObject.Parse(File.ReadAllText(Path.Combine(Fixtures.ResourcesRoot, "strings", loc, "ranch_quest_ui.json")));
                foreach (var id in new[] { "rosi", "moehrchen", "timmi", "miez", "luna", "knips", "punktabzug", "eilfried" })
                    foreach (var station in RanchNpcRoutine.Routine(id))
                        Assert.That(s["rquest"]?["bei"]?[station.Ort], Is.Not.Null, loc + ": " + id + " " + station.Ort);
            }
        }

        [Test]
        public void TheTimeOnAStationWrapsOverMidnight()
        {
            var r = RanchNpcRoutine.Routine("luna");
            var nacht = RanchNpcRoutine.StationBei(r, 1.0);
            Assert.That(nacht.Von, Is.EqualTo(22.0));
            Assert.That(RanchNpcRoutine.SeitStunden(nacht, 1.0), Is.EqualTo(3.0));
            Assert.That(RanchNpcRoutine.SeitStunden(RanchNpcRoutine.StationBei(r, 10.25), 10.25), Is.EqualTo(0.25));
        }
    }
}
