using System;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// What Gooby's care state looks like at home (port of the rules in pflege_runner.gd and gooby_rig.gd set_care):
    /// queasy turns him a little pale with a sniffly nose, properly sick paler still with the ice pack; tiredness (and
    /// any sickness) shows as rings under the eyes; both slow his waddle, but he always still gets everywhere. Now and
    /// then a tired Gooby yawns and a poorly one sneezes. Pure: the caller rolls the timers with its own random.
    /// </summary>
    public static class CareLook
    {
        /// <summary>Pale tint at full strength (mixed in by <see cref="PaleAmount"/>).</summary>
        public static readonly (double r, double g, double b) PaleTint = (0.80, 0.88, 0.82);
        public const double PaleQueasy = 0.5, PaleSick = 1.0;
        /// <summary>Rings under the eyes from this strength on; a poorly Gooby always shows them at least this strongly.</summary>
        public const double EyeBagsFrom = 0.3, EyeBagsWhenPoorly = 0.45;
        public const double SlowTired = 0.35, SpeedQueasy = 0.85, SpeedSick = 0.6;
        public const double YawnFrom = 0.35;
        public const double YawnMinSec = 18, YawnMaxSec = 32;
        public const double SneezeQueasyMinSec = 22, SneezeQueasyMaxSec = 40, SneezeSickMinSec = 12, SneezeSickMaxSec = 24;
        /// <summary>Share of yawns and sneezes Gooby also says something about.</summary>
        public const double YawnLineChance = 0.35, SneezeLineChance = 0.5;

        /// <summary>The care inputs of a v5 state: health grade (0 healthy, 1 queasy, 2 sick), tiredness 0..1, weight.</summary>
        public readonly struct Input
        {
            public readonly int Grade;
            public readonly double Tired;
            public readonly double Weight;
            public readonly bool Sleeping;

            public Input(int grade, double tired, double weight, bool sleeping)
            {
                Grade = grade; Tired = tired; Weight = weight; Sleeping = sleeping;
            }
        }

        public static Input Of(JObject state)
        {
            var gooby = J.Dict(state?["gooby"]);
            var grade = Math.Max(0, Health.Grade(gooby["health"]));
            var tired = Sleep.Tiredness01(gooby["stats"] as JObject);
            var weight = Life.Weight.ClampWeight(gooby["weight"]);
            var sleeping = Sleep.IsSleeping(Sleep.FlatOf(state ?? new JObject()));
            return new Input(grade, tired, weight, sleeping);
        }

        public static double PaleAmount(int grade) => grade >= 2 ? PaleSick : grade >= 1 ? PaleQueasy : 0.0;

        public static bool SniffleNose(int grade) => grade >= 1;

        public static bool IcePack(int grade) => grade >= 2;

        /// <summary>Strength of the rings under the eyes, 0 when they are not shown; asleep he shows none.</summary>
        public static double EyeBags(int grade, double tired01, bool sleeping)
        {
            var strength = sleeping ? 0.0 : J.Clamp(tired01, 0.0, 1.0);
            if (grade > 0) strength = Math.Max(strength, EyeBagsWhenPoorly);
            return strength >= EyeBagsFrom ? strength : 0.0;
        }

        /// <summary>Walking speed factor: tiredness slows linearly, sickness caps it — never below <see cref="SpeedSick"/>.</summary>
        public static double SpeedMult(int grade, double tired01)
        {
            var v = 1.0 - SlowTired * J.Clamp(tired01, 0.0, 1.0);
            if (grade >= 2) v = Math.Min(v, SpeedSick);
            else if (grade >= 1) v = Math.Min(v, SpeedQueasy);
            return v;
        }

        public static bool Yawns(Input c) => !c.Sleeping && c.Tired >= YawnFrom;

        public static bool Sneezes(Input c) => !c.Sleeping && c.Grade >= 1;

        /// <summary>Seconds until the next yawn for a roll r in [0, 1).</summary>
        public static double NextYawn(double r) => YawnMinSec + (YawnMaxSec - YawnMinSec) * r;

        /// <summary>Seconds until the next sneeze for a roll r in [0, 1): a properly sick Gooby sneezes more often.</summary>
        public static double NextSneeze(int grade, double r) => grade >= 2
            ? SneezeSickMinSec + (SneezeSickMaxSec - SneezeSickMinSec) * r
            : SneezeQueasyMinSec + (SneezeQueasyMaxSec - SneezeQueasyMinSec) * r;
    }
}
