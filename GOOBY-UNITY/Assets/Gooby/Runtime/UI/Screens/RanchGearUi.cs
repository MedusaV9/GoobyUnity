using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The gear rows of the farm sheet and the Hufingen saddlery: per slot the five shop colours (the price until bought)
    /// and the quest pieces the player owns (gold rim, star); the Look row (misty coat, fluff) once a look is unlocked;
    /// the saddle room's outfit sets. The screens bring the actions.
    /// </summary>
    public static class RanchGearUi
    {
        private static string T(GameHost host, string key, params (string, object)[] args) => host.Strings.T(key, args);

        public static void Reihen(GameHost host, VisualElement body, string pferdId, string prefix, Func<string, string, int> preis, Action<string, string> tippen)
        {
            var state = host.Store.State;
            foreach (var slot in RanchHof.GearSlots)
            {
                var gr = Ux.Row("gb-ranchhof-gearrow");
                gr.Add(Ux.Text(T(host, "ranchplay.gear." + slot), "gb-md", "gb-bold", "gb-ranchhof-gearname"));
                var an = pferdId != null ? RanchWirtschaft.GearAn(state, pferdId, slot) : null;
                foreach (var farbe in RanchWirtschaft.GearFarbenFuer(state, slot))
                {
                    var (s, f) = (slot, farbe);
                    var gid = slot + "_" + farbe;
                    var owned = RanchWirtschaft.GearBesitzt(state, gid);
                    var quest = RanchWirtschaft.IstSonder(slot, farbe);
                    var chip = Chip(prefix + "-gear-" + gid, RanchGear3D.Hex(farbe), owned ? T(host, "ranchplay.gearfarbe." + farbe) : preis(slot, farbe) + " ᴳ", quest, () => tippen(s, f));
                    chip.EnableInClassList("gb-ranchhof-gear--an", an == farbe);
                    chip.EnableInClassList("gb-ranchhof-gear--zu", !owned);
                    gr.Add(chip);
                }
                body.Add(gr);
            }
        }

        /// <summary>The look chips of the selected horse (only the unlocked looks; nothing before the first).</summary>
        public static void Look(GameHost host, VisualElement body, string pferdId, string prefix, Action<string> tippen)
        {
            var state = host.Store.State;
            var pferd = RanchHof.Pferd(state, pferdId);
            var looks = new[] { RanchBelohnungen.Nebel, RanchBelohnungen.Flausch }.Where(l => RanchBelohnungen.Hat(state, RanchBelohnungen.LookUnlock(l))).ToList();
            if (looks.Count == 0 || pferd == null) return;
            var gr = Ux.Row("gb-ranchhof-gearrow");
            gr.name = prefix + "-look";
            gr.Add(Ux.Text(T(host, "ranchplay.look.titel"), "gb-md", "gb-bold", "gb-ranchhof-gearname"));
            foreach (var look in looks)
            {
                var l = look;
                var punkt = look == RanchBelohnungen.Nebel ? "#B9C4D2" : "#FFF1DA";
                var chip = Chip(prefix + "-look-" + look, punkt, T(host, "ranchplay.look." + look), true, () => tippen(l));
                chip.EnableInClassList("gb-ranchhof-gear--an", RanchBelohnungen.TraegtLook(pferd, look));
                gr.Add(chip);
            }
            body.Add(gr);
        }

        /// <summary>The saddle room's outfit sets: what each holds, Save (what the horse wears now), Put on.</summary>
        public static void Sets(GameHost host, VisualElement body, string pferdId, string prefix, Action<int> speichern, Action<int> anlegen)
        {
            var state = host.Store.State;
            var plaetze = RanchBauEffekte.OutfitSets(state);
            body.Add(Ux.Text(T(host, "ranchplay.sets.titel"), "gb-ranchhof-sec"));
            if (plaetze <= 0)
            {
                body.Add(Ux.Text(T(host, "ranchplay.sets.gesperrt"), "gb-sm", "gb-mild", "gb-ranchhof-sethint"));
                return;
            }
            for (var i = 0; i < plaetze; i++)
            {
                var platz = i;
                var set = RanchWirtschaft.OutfitSet(state, i);
                var gr = Ux.Row("gb-ranchhof-gearrow");
                gr.name = prefix + "-set-" + i;
                gr.Add(Ux.Text(T(host, "ranchplay.sets.set", ("n", i + 1)), "gb-md", "gb-bold", "gb-ranchhof-gearname"));
                var inhalt = Ux.Row("gb-ranchhof-gear");
                inhalt.pickingMode = PickingMode.Ignore;
                var teile = set == null ? new List<string>() : RanchHof.GearSlots.Select(s => J.Str(set[s], null)).Where(f => f != null).ToList();
                foreach (var farbe in teile)
                {
                    var dot = Ux.El("gb-ranchhof-gear-dot");
                    dot.style.backgroundColor = ArtLibrary.Hex(RanchGear3D.Hex(farbe));
                    inhalt.Add(dot);
                }
                inhalt.Add(Ux.Text(set == null ? T(host, "ranchplay.sets.leer") : TeileText(host, set), "gb-sm"));
                gr.Add(inhalt);
                var save = Ux.IconTextBtn("check", T(host, "ranchplay.sets.speichern"), () => speichern(platz), "gb-btn-secondary", "gb-btn-sm");
                save.name = prefix + "-set-speichern-" + i;
                save.SetEnabled(pferdId != null);
                gr.Add(save);
                var put = Ux.IconTextBtn("shirt", T(host, "ranchplay.sets.anlegen"), () => anlegen(platz), "gb-btn-confirm", "gb-btn-sm");
                put.name = prefix + "-set-anlegen-" + i;
                put.SetEnabled(set != null && pferdId != null);
                gr.Add(put);
                body.Add(gr);
            }
        }

        /// <summary>"Sattel Rot · Decke Blau" — or "nothing on".</summary>
        public static string TeileText(GameHost host, JObject set)
        {
            var teile = RanchHof.GearSlots.Where(s => J.Str(set?[s], null) != null)
                .Select(s => T(host, "ranchplay.gear." + s) + " " + T(host, "ranchplay.gearfarbe." + J.Str(set[s]))).ToList();
            return teile.Count > 0 ? string.Join(" · ", teile) : T(host, "ranchplay.sets.nichts");
        }

        private static Button Chip(string name, string hex, string text, bool quest, Action onClick)
        {
            var chip = new Button(onClick) { name = name };
            chip.ClearClassList();
            chip.AddToClassList("gb-ranchhof-gear");
            chip.EnableInClassList("gb-ranchhof-gear--quest", quest);
            var dot = Ux.El("gb-ranchhof-gear-dot");
            dot.style.backgroundColor = ArtLibrary.Hex(hex);
            dot.pickingMode = PickingMode.Ignore;
            chip.Add(dot);
            var lbl = Ux.Text(text, "gb-sm");
            lbl.pickingMode = PickingMode.Ignore;
            chip.Add(lbl);
            if (quest)
            {
                var stern = Ux.Icon("star", "gb-ranchhof-gear-stern");
                stern.pickingMode = PickingMode.Ignore;
                chip.Add(stern);
            }
            return chip;
        }
    }
}
