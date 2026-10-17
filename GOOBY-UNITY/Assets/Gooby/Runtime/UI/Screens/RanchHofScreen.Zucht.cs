using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Breeding on the farm (horse_breeding.gd in the care screen): the selected mare gets a foal by a second grown horse
    /// ("Hof &amp; Laden" → Zucht), the card shows the pregnancy with its countdown and the care checkpoint ("Bauch
    /// streicheln", 2 h sooner), the birth happens when you are at the farm (confetti, the foal in the paddock at its
    /// phase's size), and young horses show how long until the next phase and cannot be ridden yet.
    /// </summary>
    public sealed partial class RanchHofScreen
    {
        private void RefreshZucht(JObject pferd)
        {
            if (_zuchtRow == null) return;
            if (!_gekauft || pferd == null || _sel == null) { _zuchtRow.style.display = DisplayStyle.None; return; }
            var zucht = RanchZucht.Lese(State);
            var now = Now;
            var t = RanchZucht.Traechtigkeit(zucht, _sel);
            if (t != null)
            {
                _zuchtRow.style.display = DisplayStyle.Flex;
                _zuchtLabel.text = T("rhof.zucht.traechtig", ("zeit", Dauer(RanchZucht.GeburtIn(zucht, _sel, now))));
                _fuersorgeBtn.style.display = RanchZucht.CheckpointFaellig(t, now) >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _stammBtn.style.display = DisplayStyle.None;
                return;
            }
            var alter = RanchZucht.Alter(pferd, now, RanchBauEffekte.AufzuchtTempo(State));
            _fuersorgeBtn.style.display = DisplayStyle.None;
            _stammBtn.style.display = pferd["ahnen"] is JObject ? DisplayStyle.Flex : DisplayStyle.None;
            if (alter == "ausgewachsen")
            {
                _zuchtRow.style.display = pferd["ahnen"] is JObject ? DisplayStyle.Flex : DisplayStyle.None;
                _zuchtLabel.text = "";
                return;
            }
            var idx = System.Array.FindIndex(RanchZucht.Phasen, ph => ph.alter == alter);
            var naechste = idx >= 0 && idx + 1 < RanchZucht.Phasen.Length ? RanchZucht.Phasen[idx + 1].alter : "ausgewachsen";
            var tage = Mathf.Max(1, Mathf.CeilToInt((float)RanchZucht.TageBisNaechstePhase(J.Long(pferd["geborenAm"]), now, RanchBauEffekte.AufzuchtTempo(State))));
            _zuchtRow.style.display = DisplayStyle.Flex;
            _zuchtLabel.text = T("rhof.zucht.waechst", ("alter", T("rhof.zucht.alter." + alter)), ("tage", tage), ("naechste", T("rhof.zucht.alter." + naechste)));
        }

        private void Fuersorge()
        {
            if (_sel == null) return;
            var id = _sel;
            var ok = false;
            var now = Now;
            _host.Store.Update(s => ok = RanchZucht.Fuersorge(s, id, now), GameHost.TicksMs);
            if (!ok) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _stage?.Brush();
            Sfx.Play("ranch_striegeln", 0f, 0.7f);
            _juice.Burst(FieldPoint(_stage != null ? _stage.HorseTop : Vector3.zero), new Color(1f, 0.55f, 0.66f), 14);
            Sag(T("rhof.zucht.fuersorge_ok", ("name", PferdName(id))));
            Debug.Log("[ranchhof] fuersorge " + id);
            RefreshCard();
        }

        /// <summary>Every due birth happens now, with a celebration; the foal is then selected.</summary>
        private void Geburten()
        {
            if (!_gekauft || Root == null || _host.Ui.Current != this) return;
            var zucht = RanchZucht.Lese(State);
            var now = Now;
            foreach (var stute in J.Dict(zucht["traechtigkeiten"]).Properties().Select(p => p.Name).ToList())
            {
                if (!RanchZucht.GeburtBereit(zucht, stute, now)) continue;
                string fohlen = null;
                _host.Store.Update(s => fohlen = RanchZucht.Geburt(s, stute, now), GameHost.TicksMs);
                if (fohlen == null) continue;
                _host.Store.NotifySliceChanged(RanchHof.SliceId);
                var name = PferdName(fohlen);
                BuildHorses();
                Waehle(fohlen);
                _juice.Confetti(110);
                Sfx.PlayUi("soft_levelup");
                Sfx.Play("ranch_wiehern_a", 4f, 0.8f);
                _host.Ui.Toast(T("rhof.zucht.geboren", ("name", name)), "heart");
                Sag(T("rhof.zucht.geboren", ("name", name)));
                Debug.Log("[ranchhof] geburt " + stute + " → " + fohlen);
                RefreshAll();
            }
        }

        private bool ReitbarSel()
        {
            var p = RanchHof.Pferd(State, _sel);
            if (p == null || RanchZucht.Reitbar(RanchZucht.Alter(p, Now, RanchBauEffekte.AufzuchtTempo(State)))) return true;
            Sfx.PlayUi("soft_error");
            Sag(T("rhof.zucht.jung", ("name", PferdName(_sel))));
            return false;
        }

        private void ZuchtSection(VisualElement body)
        {
            if (_sel == null) return;
            body.Add(Ux.Text(T("rhof.zucht.titel"), "gb-ranchhof-sec"));
            var zucht = RanchZucht.Lese(State);
            if (RanchZucht.Traechtigkeit(zucht, _sel) != null)
            {
                body.Add(Ux.Text(T("rhof.zucht.traechtig", ("zeit", Dauer(RanchZucht.GeburtIn(zucht, _sel, Now)))), "gb-md"));
                return;
            }
            var now = Now;
            var partner = RanchHof.PferdIds(State).Where(id => id != _sel && RanchZucht.Reitbar(RanchZucht.Alter(RanchHof.Pferd(State, id), now, RanchBauEffekte.AufzuchtTempo(State)))).ToList();
            if (partner.Count == 0) { body.Add(Ux.Text(T("rhof.zucht.keiner"), "gb-sm", "gb-mild")); return; }
            body.Add(Ux.Text(T("rhof.zucht.text", ("stute", PferdName(_sel))), "gb-sm", "gb-mild"));
            var row = Ux.Row("gb-ranchhof-shoprow");
            foreach (var id in partner)
            {
                var hengst = id;
                var b = Ux.IconTextBtn("heart", T("rhof.zucht.decken", ("name", PferdName(id))), () => Decken(hengst), "gb-btn-secondary", "gb-ranchhof-shopbtn");
                b.name = "ranchhof-decken-" + id;
                row.Add(b);
            }
            body.Add(row);
        }

        private void Decken(string hengstId)
        {
            if (_sel == null) return;
            var stute = _sel;
            (bool ok, string fehler) res = (false, "");
            var now = Now;
            _host.Store.Update(s => res = RanchZucht.Decken(s, stute, hengstId, now), GameHost.TicksMs);
            if (!res.ok)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(T("rhof.zucht.fehler." + res.fehler, ("name", PferdName(stute))), "heart");
                Debug.Log("[ranchhof] zucht nein: " + res.fehler);
                return;
            }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            CloseOverlay();
            _juice.Confetti(40);
            Sfx.PlayUi("soft_levelup");
            _stage?.HorseReact("kopfschuetteln");
            Sag(T("rhof.zucht.start"));
            Debug.Log("[ranchhof] zucht " + stute + " + " + hengstId);
            RefreshAll();
        }

        /// <summary>
        /// The pedigree (stammbaum_panel.gd): three generations as coat portraits — no gene letters anywhere — with the
        /// grandparents from the parents' snapshots on top, mother and father, and the foal with its "Geerbt" chips
        /// (lucky rolls per stat, the glitter secret) and its two character traits.
        /// </summary>
        private void ZeigeStammbaum()
        {
            var p = RanchHof.Pferd(State, _sel);
            if (p == null) return;
            var body = OpenSheet("ranchhof-stammbaum", "heart", new Color(1f, 0.8f, 0.86f), T("rpferd.stammbaum.titel"), PferdName(_sel));
            var ahnen = J.Dict(p["ahnen"]);
            var mutter = ahnen["mutter"] as JObject;
            var vater = ahnen["vater"] as JObject;
            var oben = Ux.Row("gb-ranchhof-stammrow");
            foreach (var elter in new[] { mutter, vater })
                foreach (var rolle in new[] { "mutter", "vater" })
                    oben.Add(Portrait(J.Dict(elter?["ahnen"])[rolle] as JObject, "rpferd.stammbaum." + rolle, true));
            body.Add(oben);
            var mitte = Ux.Row("gb-ranchhof-stammrow");
            mitte.Add(Portrait(mutter, "rpferd.stammbaum.mutter", false));
            mitte.Add(Portrait(vater, "rpferd.stammbaum.vater", false));
            body.Add(mitte);
            var unten = Ux.Row("gb-ranchhof-stammrow");
            unten.Add(Portrait(p, "rpferd.stammbaum.fohlen", false));
            body.Add(unten);
            var chips = Ux.Row("gb-ranchhof-shoprow", "gb-ranchhof-center");
            chips.style.justifyContent = Justify.Center;
            if (mutter != null || vater != null)
            {
                foreach (var k in RanchPferd.StatKeys)
                {
                    var schnitt = System.Math.Round((Stat(mutter, k) + Stat(vater, k)) / 2.0, System.MidpointRounding.AwayFromZero);
                    if (Stat(p, k) > schnitt) chips.Add(Chip(T("rpferd.stammbaum.erbe_stat", ("stat", T("rpferd.stat." + k))), "#FFE7A3"));
                }
                if (RanchGene.Allele(J.Dict(p["gene"]), "glitzer", "g0").Contains("gx")) chips.Add(Chip(T("rpferd.stammbaum.erbe_glitzer"), "#E3D8FF"));
            }
            foreach (var zug in (p["charakter"] as JArray ?? new JArray()).Select(z => J.Str(z)).Where(z => z.Length > 0))
                chips.Add(Chip(T("rpferd.zug." + zug), "#D8F0D0"));
            if (chips.childCount > 0)
            {
                body.Add(Ux.Text(T("rpferd.stammbaum.erbe_titel"), "gb-ranchhof-sec", "gb-ranchhof-center"));
                body.Add(chips);
            }
            Sfx.PlayUi("soft_open");
        }

        private static double Stat(JObject pferd, string key) => System.Math.Max(1.0, System.Math.Min(20.0, J.NumOr(J.Dict(pferd?["stats"])[key], 10.0)));

        private VisualElement Portrait(JObject pferd, string rolleKey, bool klein)
        {
            var col = Ux.Col("gb-ranchhof-portrait");
            col.EnableInClassList("gb-ranchhof-portrait--klein", klein);
            col.Add(Ux.Text(T(rolleKey), "gb-xs", "gb-mild"));
            var disc = Ux.El("gb-ranchhof-horse-disc");
            disc.style.backgroundColor = pferd != null ? Fell(pferd).fell : new Color(0.9f, 0.88f, 0.84f);
            disc.Add(Ux.Icon(pferd != null ? "horseshoe" : "search"));
            col.Add(disc);
            var key = J.Str(pferd?["nameKey"]);
            var name = pferd == null ? T("rpferd.stammbaum.unbekannt") : key.Length > 0 ? T(key) : J.Str(pferd["name"], T("rpferd.stammbaum.unbekannt"));
            col.Add(Ux.Text(name, "gb-sm", "gb-bold"));
            return col;
        }

        private static VisualElement Chip(string text, string hex)
        {
            var chip = Ux.Text(text, "gb-ranchhof-chip");
            chip.style.backgroundColor = ArtLibrary.Hex(hex);
            chip.style.marginBottom = 6;
            return chip;
        }

        /// <summary>QA: "ranch zucht &lt;hengst&gt; | fuersorge | geburt".</summary>
        private void QaZucht(string cmd, string a1)
        {
            switch (cmd)
            {
                case "zucht": Decken(RanchHof.PferdIds(State).FirstOrDefault(id => id.EndsWith(a1)) ?? a1); break;
                case "fuersorge": Fuersorge(); break;
                case "geburt": Geburten(); break;
                case "stammbaum": ZeigeStammbaum(); break;
            }
        }
    }
}
