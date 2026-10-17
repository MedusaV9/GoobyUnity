using System;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;

namespace Gooby.Core.Garden
{
    /// <summary>
    /// The garage (port of garage_logic.gd + the timing of garage_prop.gd): a one-time upgrade on the garden field,
    /// a 2×3 structure like the greenhouse, bought for coins; the car in use parks inside. The roller door is a
    /// runtime-only state machine (zu → oeffnet → offen → schliesst → zu, a tap mid-way turns it round); only the
    /// structure is saved (home.garden). <see cref="Abfahrt"/> is the drive-off before the city as a pure timeline.
    /// </summary>
    public static class GarageLogic
    {
        public const string Kind = "garage";
        public const int Preis = 1200;

        public const string ReasonSchonGebaut = "schon_gebaut";
        public const string ReasonZuTeuer = "zu_teuer";

        public const string Zu = "zu";
        public const string Oeffnet = "oeffnet";
        public const string Offen = "offen";
        public const string Schliesst = "schliesst";

        /// <summary>One roll of the door (garage_prop.gd TOR_DAUER_S).</summary>
        public const double TorDauerS = 0.7;
        /// <summary>The car pulls out once the door is up, accelerating (garage_prop.gd ABFAHRT_DAUER_S).</summary>
        public const double FahrtDauerS = 1.6;
        /// <summary>A beat between door and car, and after the car before the city takes over.</summary>
        public const double PauseS = 0.1;

        public static (int w, int h) Footprint => GardenGrid.StructureSize(Kind);

        public static bool Gebaut(GameStateStore gs) => gs != null && GardenState.Grid(gs).StructuresOfKind(Kind).Count > 0;

        public static bool KannKaufen(int coins) => coins >= Preis;

        /// <summary>The garage's structure entry (null = none built).</summary>
        public static GardenStructure Struktur(GameStateStore gs)
        {
            if (gs == null) return null;
            var list = GardenState.Grid(gs).StructuresOfKind(Kind);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>Could the garage stand here now? schon_gebaut, a grid reason or zu_teuer — in the order of <see cref="Kaufen"/>.</summary>
        public static PlaceCheck Pruefen(GameStateStore gs, Cell at, int rot = 0)
        {
            var grid = GardenState.Grid(gs);
            if (grid.StructuresOfKind(Kind).Count > 0) return PlaceCheck.Bad(ReasonSchonGebaut);
            var check = grid.CanPlaceStructure(Kind, at, rot);
            if (!check.Ok) return check;
            return KannKaufen(Coins(gs)) ? PlaceCheck.Good : PlaceCheck.Bad(ReasonZuTeuer);
        }

        /// <summary>Buys and places the garage. Fail-closed: every check first, then structure and payment in one go.</summary>
        public static PlaceCheck Kaufen(GameStateStore gs, Cell at, int rot = 0)
        {
            var check = Pruefen(gs, at, rot);
            if (!check.Ok) return check;
            var grid = GardenState.Grid(gs);
            grid.PlaceStructure(Kind, at, rot);
            GardenState.SaveGrid(gs, grid);
            gs.Update(state => Economy.Spend(J.EnsureDict(state, "economy"), Preis, "garage_bau"));
            gs.NotifySliceChanged("home");
            gs.NotifySliceChanged("economy");
            return PlaceCheck.Good;
        }

        /// <summary>The next state after a tap on the door (also mid-roll: the door turns round).</summary>
        public static string Toggle(string zustand) => zustand == Zu || zustand == Schliesst ? Oeffnet : Schliesst;

        /// <summary>The state once the roll has run out.</summary>
        public static string Ende(string zustand) => zustand == Oeffnet ? Offen : zustand == Schliesst ? Zu : zustand;

        /// <summary>How far the door is meant to be rolled up: 1 while opening or open, 0 otherwise.</summary>
        public static double ZielAnteil(string zustand) => zustand == Oeffnet || zustand == Offen ? 1.0 : 0.0;

        /// <summary>Open far enough to drive out.</summary>
        public static bool IstOffen(string zustand) => zustand == Offen;

        /// <summary>The whole drive-off: door roll, pause, the car's run, pause.</summary>
        public static double AbfahrtDauerS(bool reduziert) => reduziert ? 0.0 : TorDauerS + PauseS + FahrtDauerS + PauseS;

        /// <summary>
        /// The drive-off at <paramref name="t"/> seconds: how far the door is up (from <paramref name="torStart"/>, eased
        /// in and out), how far along its run the car is (0..1, accelerating like an ease-in quad) and whether the city may
        /// take over. Reduced motion jumps to the end at once.
        /// </summary>
        public static (double tor, double fahrt, bool fertig) Abfahrt(double t, double torStart = 0.0, bool reduziert = false)
        {
            if (reduziert) return (1.0, 1.0, true);
            t = Math.Max(0.0, t);
            var torT = Math.Min(1.0, t / TorDauerS);
            var tor = torStart + (1.0 - torStart) * (0.5 - 0.5 * Math.Cos(Math.PI * torT));
            var fahrtT = Math.Min(1.0, Math.Max(0.0, (t - TorDauerS - PauseS) / FahrtDauerS));
            return (tor, fahrtT * fahrtT, t >= AbfahrtDauerS(false));
        }

        private static int Coins(GameStateStore gs) => J.Int(J.Path(gs.State, "economy", "coins"));
    }
}
