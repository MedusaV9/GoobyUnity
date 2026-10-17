using System.Collections.Generic;

namespace Gooby.Core.City
{
    /// <summary>
    /// Travel cutscene timings (port of reise_cutscene.gd, Doc E §3.2): 5-shot departure, a shortened return,
    /// skip after two seconds, reduced-motion text cards. Pure numbers so the screen, the 3D set and the tests
    /// agree on when a shot ends and which string keys the cards show.
    /// </summary>
    public static class TravelCutscene
    {
        public const string ModusAbflug = "abflug";
        public const string ModusRueckkehr = "rueckkehr";
        public const double SkipAfterS = 2.0;
        public const double CardHoldS = 1.6;

        public sealed class Shot
        {
            public string Id;
            public double Seconds;
        }

        /// <summary>Godot wait totals: door 3.4+0.8, driveway 3.0, taxi 2.8+2.6, city 4.0, airport 5.5.</summary>
        public static readonly Shot[] AbflugShots =
        {
            new Shot { Id = "door", Seconds = 4.2 },
            new Shot { Id = "driveway", Seconds = 3.0 },
            new Shot { Id = "taxi", Seconds = 5.4 },
            new Shot { Id = "city", Seconds = 4.0 },
            new Shot { Id = "airport", Seconds = 5.5 },
        };

        /// <summary>Return: landing 4.0, walk 3.7, celebrate 3.0 (~12 s).</summary>
        public static readonly Shot[] RueckkehrShots =
        {
            new Shot { Id = "landing", Seconds = 4.0 },
            new Shot { Id = "walk", Seconds = 3.7 },
            new Shot { Id = "celebrate", Seconds = 3.0 },
        };

        public static Shot[] Shots(string modus) => modus == ModusRueckkehr ? RueckkehrShots : AbflugShots;

        public static string[] CardKeys(string modus) => modus == ModusRueckkehr
            ? new[] { "travel.cutscene.karte_rueckkehr" }
            : new[]
            {
                "travel.cutscene.karte_1", "travel.cutscene.karte_2", "travel.cutscene.karte_3",
                "travel.cutscene.karte_4", "travel.cutscene.karte_5",
            };

        public static double TotalSeconds(string modus, bool reducedMotion)
        {
            if (reducedMotion) return CardKeys(modus).Length * CardHoldS;
            var sum = 0.0;
            foreach (var s in Shots(modus)) sum += s.Seconds;
            return sum;
        }

        /// <summary>Shot under the playhead (null once finished). Skip jumps to the end.</summary>
        public static Shot At(string modus, double t, bool skip)
        {
            if (skip) return null;
            var acc = 0.0;
            foreach (var s in Shots(modus))
            {
                acc += s.Seconds;
                if (t < acc) return s;
            }
            return null;
        }

        public static int CardIndex(double t, int count)
        {
            if (count <= 0) return 0;
            var i = (int)(t / CardHoldS);
            if (i < 0) i = 0;
            if (i >= count) i = count - 1;
            return i;
        }

        public static bool Finished(string modus, double t, bool skip, bool reducedMotion)
        {
            if (skip && t >= SkipAfterS) return true;
            return t >= TotalSeconds(modus, reducedMotion);
        }

        public static bool SkipVisible(double t) => t >= SkipAfterS;
    }
}
