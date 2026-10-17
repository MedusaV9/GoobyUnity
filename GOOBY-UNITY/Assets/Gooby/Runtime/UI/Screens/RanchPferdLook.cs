using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>How a horse of the save looks in 3D: its coat (or the misty coat it wears), its gear and the fluffy look.</summary>
    public static class RanchPferdLook
    {
        public static (Color fell, Color maehne) Fell(JObject pferd, string fallback = "braun")
        {
            if (RanchBelohnungen.TraegtLook(pferd, RanchBelohnungen.Nebel)) return RcompRideView.FellVon(RanchBelohnungen.Nebel);
            var hex = J.Str(pferd?["farbeHex"]);
            if (hex.Length > 0) return (ArtLibrary.Hex(hex), ArtLibrary.Hex(J.Str(pferd["maehneHex"], "#6E4B2E")));
            return RcompRideView.FellVon(J.Str(pferd?["farbe"], fallback));
        }

        /// <summary>Dresses a horse view as the save says (no id = bare).</summary>
        public static void Kleide(RanchHorse3D view, JObject state, string pferdId)
        {
            if (view == null) return;
            var pferd = RanchHof.Pferd(state, pferdId);
            foreach (var slot in RanchHof.GearSlots) view.SetGear(slot, pferd != null ? RanchWirtschaft.GearAn(state, pferdId, slot) : null);
            view.SetFlausch(RanchBelohnungen.TraegtLook(pferd, RanchBelohnungen.Flausch));
        }
    }
}
