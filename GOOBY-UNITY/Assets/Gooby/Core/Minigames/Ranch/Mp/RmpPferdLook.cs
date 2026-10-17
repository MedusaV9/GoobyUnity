using System;
using System.Text.RegularExpressions;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch.Mp
{
    /// <summary>
    /// The horse a rider brings into a ranch round (race, tag, trail ride, visit): its name, its coat (the save's colour id
    /// or the bred hex colours, the misty coat when it wears it), the fluffy look and what it wears in the three gear slots.
    /// Sent as the cosmetic room message PFERD to everyone in the room; the body has the shape of a save horse
    /// (<c>farbe</c>, <c>farbeHex</c>, <c>maehneHex</c>, <c>look</c>) plus <c>gear</c>, so the coat readers of the farm take
    /// it as it is. What comes in is rebuilt field by field from the known values — anything else falls away.
    /// </summary>
    public static class RmpPferdLook
    {
        public const string Kind = "PFERD";
        public const int MaxNameLen = 20;

        private static readonly Regex FarbId = new Regex("^[a-z_]{1,16}$");
        private static readonly Regex Hex = new Regex("^#[0-9A-Fa-f]{6}$");

        /// <summary>The look of a horse of the save (null: no such horse).</summary>
        public static JObject Von(JObject state, string pferdId)
        {
            var pferd = RanchHof.Pferd(state, pferdId);
            if (pferd == null) return null;
            var gear = new JObject();
            foreach (var slot in RanchHof.GearSlots) gear[slot] = RanchWirtschaft.GearAn(state, pferdId, slot);
            var roh = new JObject
            {
                ["name"] = pferd["name"],
                ["farbe"] = pferd["farbe"],
                ["farbeHex"] = pferd["farbeHex"],
                ["maehneHex"] = pferd["maehneHex"],
                ["look"] = pferd["look"],
                ["gear"] = gear,
            };
            return Sauber(roh);
        }

        /// <summary>A received body rebuilt from the known fields (null when it is not an object).</summary>
        public static JObject Sauber(JToken body)
        {
            if (!(body is JObject roh)) return null;
            var aus = new JObject
            {
                ["name"] = Kurz(J.Str(roh["name"]).Trim()),
                ["farbe"] = FarbId.IsMatch(J.Str(roh["farbe"])) ? J.Str(roh["farbe"]) : "braun",
            };
            var fell = J.Str(roh["farbeHex"]);
            var maehne = J.Str(roh["maehneHex"]);
            if (Hex.IsMatch(fell))
            {
                aus["farbeHex"] = fell;
                if (Hex.IsMatch(maehne)) aus["maehneHex"] = maehne;
            }
            var look = new JObject();
            if (J.Str(J.Path(roh, "look", "fell")) == RanchBelohnungen.Nebel) look["fell"] = RanchBelohnungen.Nebel;
            if (J.IsTrue(J.Path(roh, "look", RanchBelohnungen.Flausch))) look[RanchBelohnungen.Flausch] = true;
            if (look.Count > 0) aus["look"] = look;
            var gear = new JObject();
            foreach (var slot in RanchHof.GearSlots)
            {
                var farbe = J.Str(J.Path(roh, "gear", slot), null);
                gear[slot] = farbe != null && GearGibtEs(slot, farbe) ? (JToken)farbe : JValue.CreateNull();
            }
            aus["gear"] = gear;
            return aus;
        }

        /// <summary>What the look puts in a gear slot (null: nothing).</summary>
        public static string Gear(JObject look, string slot) => J.Str(J.Path(look, "gear", slot), null);

        private static bool GearGibtEs(string slot, string farbe) =>
            Array.IndexOf(RanchWirtschaft.GearFarben, farbe) >= 0
            || RanchWirtschaft.GearSonder.TryGetValue(slot, out var sonder) && Array.IndexOf(sonder, farbe) >= 0;

        private static string Kurz(string s) => s.Length <= MaxNameLen ? s : s.Substring(0, MaxNameLen);
    }
}
