using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// GOOB-1 space station (port of OrtRaumstation / raumstation.gd): not on the town plan — the airport shuttle
    /// appears only after <c>vacation.visited.space</c> is latched. Low-G hop is a pair of tween numbers (higher and
    /// slower than on the ground); the Astro-Snack automaton sells the Weltraum-Möhre at the authored surcharge.
    /// </summary>
    public static class Raumstation
    {
        public const string Id = "raumstation";
        public const string SpielRocket = "rocketRescue";
        public const string SpielStar = "starHopper";
        public const string MoehreId = "weltraumMoehre";
        public const int MoehrePreis = 14;
        public const double HopBasisHoehe = 0.22;
        public const double HopBasisDauer = 0.9;
        public const double LowGHoeheMult = 2.2;
        public const double LowGDauerMult = 1.8;

        public static bool Freigeschaltet(JObject state)
        {
            var visited = Vacation.SliceOf(state)["visited"] as JObject;
            return J.IsTrue(visited?["space"]);
        }

        /// <summary>Low-G hop: higher (×2.2) and slower (×1.8) than the ground hop — pure tween numbers.</summary>
        public static (double Hoehe, double Dauer) HopParameter(double basisHoehe, double basisDauer) =>
            (basisHoehe * LowGHoeheMult, basisDauer * LowGDauerMult);

        public static bool KameraFrei(JObject state) => J.IsTrue(J.Path(state, "camera", "owned"));

        /// <summary>
        /// A synthetic place (never in <see cref="DriveModel.Places"/> / city_map.json). Parking numbers sit at the
        /// airport kerb so QA <c>-gooby-open raumstation</c> can park the car; the interior is reached by shuttle.
        /// </summary>
        public static DriveModel.Place Place()
        {
            var airport = DriveModel.PlaceById("flughafen");
            var p = new DriveModel.Place
            {
                Id = Id, MapId = Id, NameKey = "city.ort.raumstation", Icon = "compass",
                Dialog = "raumstation", Counter = DriveModel.CounterStation, Shop = null,
                Typ = "station", Tint = "#B8A7E8", Distrikt = "orbit", Side = -1,
            };
            if (airport != null)
            {
                p.X = airport.X; p.Z = airport.Z; p.Yaw = airport.Yaw;
                p.FaceX = airport.FaceX; p.FaceZ = airport.FaceZ;
                p.Tile = airport.Tile; p.StreetTile = airport.StreetTile;
            }
            return p;
        }

        /// <summary>Astro-Snack automaton: Weltraum-Möhre into inventory.food at the authored surcharge.</summary>
        public static bool KaufeMoehre(GameStateStore gs)
        {
            var ware = new Ware { Id = MoehreId, NameDe = "Weltraum-Möhre", NameEn = "Space carrot", Preis = MoehrePreis, Menge = 1 };
            return ShopLogic.Kaufe(gs, ware);
        }
    }
}
