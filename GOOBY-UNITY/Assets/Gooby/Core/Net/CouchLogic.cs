using Gooby.Core.Home;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Net
{
    /// <summary>
    /// The visitor's couch rule (port of couch_logic.gd): when it is evening at the host's (from 21:00, the hour travels
    /// in the host's POS) and both Goobys are nearly out of energy (≤ 10), the guest's Gooby curls up on the living room's
    /// couch and naps. Unknown values (−1: no sync yet, an older client) switch the rule off — nobody falls asleep for
    /// lack of data. The NAP room message carries {on, cell: [x, y], boden}; boden = no couch, a floor nap instead.
    /// </summary>
    public static class CouchLogic
    {
        public const int AbendStunde = 21;
        public const double EnergieSchwelle = 10.0;
        public const string Wohnzimmer = "living", PflichtCouch = "couch";

        public static bool SollSchlafen(int hostStunde, double gastEnergie, double hostEnergie)
        {
            if (hostStunde < 0 || gastEnergie < 0.0 || hostEnergie < 0.0) return false;
            if (hostStunde < AbendStunde) return false;
            return gastEnergie <= EnergieSchwelle && hostEnergie <= EnergieSchwelle;
        }

        /// <summary>The first couch (catalog "pflicht": "couch") among the living room's floor pieces; ok false without one.</summary>
        public static (bool ok, Cell cell, string itemId) CouchSuchen(JArray livingItems)
        {
            foreach (var entry in livingItems ?? new JArray())
            {
                if (!(entry is JObject e) || e["wall"] != null || !(e["at"] is JArray at) || at.Count < 2) continue;
                var def = FurnitureCatalog.Def(J.Str(e["item"]));
                if (def == null || def.Pflicht != PflichtCouch) continue;
                return (true, new Cell(J.Int(at[0]), J.Int(at[1])), def.Id);
            }
            return (false, new Cell(0, 0), "");
        }

        public static JObject NapPayload(bool an, Cell cell, bool boden) =>
            new JObject { ["on"] = an, ["cell"] = new JArray(cell.X, cell.Y), ["boden"] = boden };

        /// <summary>Robust NAP parse: "on" must be a bool, unknown fields are ignored.</summary>
        public static (bool ok, bool on, Cell cell, bool boden) ParseNap(JToken body)
        {
            if (!(body is JObject o) || o["on"]?.Type != JTokenType.Boolean) return (false, false, new Cell(0, 0), false);
            var boden = o["boden"]?.Type == JTokenType.Boolean && o["boden"].Value<bool>();
            var cell = o["cell"] is JArray c && c.Count >= 2 ? new Cell(J.Int(c[0]), J.Int(c[1])) : new Cell(0, 0);
            return (true, o["on"].Value<bool>(), cell, boden);
        }
    }
}
