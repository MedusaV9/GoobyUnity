using System;
using System.Collections.Generic;
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
    /// Friendship with the shopkeepers (rnpc_dialog.gd + rnpc_freundschaft.gd in each shop sheet): five hearts under the
    /// greeting, "Plaudern" (greeting by time of day, then their small talk, from their own dialog tree), "Geschenk"
    /// (hay, apples, carrots, oats or treats — favourites count more, the reaction is theirs), stories from three and
    /// five hearts, and their shop discount from two hearts.
    /// </summary>
    public sealed partial class RanchDorfScreen
    {
        private Label _grussLabel;
        private VisualElement _herzen, _geschenkRow;
        private readonly Dictionary<string, JObject> _dialoge = new Dictionary<string, JObject>();

        private JObject Dialog(string npc)
        {
            if (_dialoge.TryGetValue(npc, out var d)) return d;
            var lang = _host.Settings.Locale == "en" ? "en" : "de";
            var asset = Resources.Load<TextAsset>("gooby/ranch_npc/" + lang + "/" + npc) ?? Resources.Load<TextAsset>("gooby/ranch_npc/de/" + npc);
            d = asset != null ? JObject.Parse(asset.text) : new JObject();
            _dialoge[npc] = d;
            return d;
        }

        /// <summary>A node's text (one line or several), following its chain for stories.</summary>
        private string Zeile(string npc, string knoten, int kette = 1)
        {
            var nodes = J.Dict(Dialog(npc)["nodes"]);
            var teile = new List<string>();
            var k = knoten;
            for (var i = 0; i < kette && nodes[k] is JObject n; i++)
            {
                if (n["text"] is JArray a) teile.AddRange(a.Select(t => J.Str(t)));
                else teile.Add(J.Str(n["text"]));
                var next = J.Str(n["next"]);
                if (next.Length == 0 || next == "hub" || next == "tschuess") break;
                k = next;
            }
            return string.Join(" ", teile.Where(t => t.Length > 0)).Replace("{spieler}", J.Str(J.Path(State, "meta", "playerName"), "Gooby"));
        }

        private VisualElement FreundZeile(string laden)
        {
            var def = RanchNpc.VomLaden(laden);
            var row = Ux.Row("gb-ranchdorf-freund");
            if (def == null) return row;
            _herzen = Ux.Row("gb-ranchdorf-herzen");
            row.Add(_herzen);
            var npc = def.Id;
            var reden = Ux.IconTextBtn("chat", T("rdorf.freund.plaudern"), () => Plaudern(npc), "gb-btn-secondary", "gb-btn-sm");
            reden.name = "ranchdorf-plaudern";
            row.Add(reden);
            var schenken = Ux.IconTextBtn("gift", T("rdorf.freund.geschenk"), () => GeschenkeZeigen(npc), "gb-btn-secondary", "gb-btn-sm");
            schenken.name = "ranchdorf-schenken";
            schenken.style.marginLeft = 8;
            row.Add(schenken);
            var herzen = RanchNpc.HerzenVon(State, npc);
            if (herzen >= 3)
            {
                var story = Ux.IconTextBtn("book", T("rdorf.freund.geschichte"), () => Erzaehlen(npc), "gb-btn-secondary", "gb-btn-sm");
                story.name = "ranchdorf-geschichte";
                story.style.marginLeft = 8;
                row.Add(story);
            }
            var rabatt = RanchNpc.Rabatt(State, laden);
            if (rabatt > 0)
            {
                var badge = Ux.Text(T("rdorf.freund.rabatt", ("n", rabatt)), "gb-ranchhof-chip");
                badge.style.backgroundColor = ArtLibrary.Hex("#FFE7A3");
                badge.style.marginLeft = 8;
                row.Add(badge);
            }
            HerzenZeigen(npc);
            return row;
        }

        private void HerzenZeigen(string npc)
        {
            if (_herzen == null) return;
            _herzen.Clear();
            var n = RanchNpc.HerzenVon(State, npc);
            for (var i = 0; i < RanchNpc.HerzMax; i++)
            {
                var h = Ux.Icon("heart");
                h.AddToClassList("gb-ranchdorf-herz");
                h.EnableInClassList("gb-ranchdorf-herz--an", i < n);
                _herzen.Add(h);
            }
        }

        private void Plaudern(string npc)
        {
            var tag = Today;
            var now = Now;
            var ersteMal = J.Str(J.Path(State, RanchHof.SliceId, RanchNpc.Key, npc, "geredetTag")) != tag;
            (int vorher, int nachher) res = (0, 0);
            _host.Store.Update(s => res = RanchNpc.Reden(s, npc, tag, now), GameHost.TicksMs);
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            string knoten;
            if (ersteMal)
            {
                var stunde = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime().Hour;
                knoten = res.nachher >= 4 ? "gruss_freund" : stunde < 11 ? "gruss_morgen" : stunde >= 18 ? "gruss_abend" : "gruss_tag";
            }
            else knoten = new[] { "smalltalk_a", "smalltalk_b", "smalltalk_c" }[(int)(now / 1000 % 3)];
            if (_grussLabel != null) _grussLabel.text = "„" + Zeile(npc, knoten) + "“";
            _stage?.Greet(RanchNpc.Fuer(npc)?.Laden);
            Sfx.PlayUi("soft_tick");
            HerzenZeigen(npc);
            HerzAufstieg(npc, res.vorher, res.nachher);
            Debug.Log("[ranchdorf] plaudern " + npc + " → " + RanchNpc.Punkte(State, npc).ToString("0.#"));
        }

        private void GeschenkeZeigen(string npc)
        {
            _geschenkRow?.RemoveFromHierarchy();
            _geschenkRow = Ux.Row("gb-ranchhof-shoprow", "gb-ranchdorf-geschenke");
            foreach (var item in RanchNpc.Geschenke)
            {
                var it = item;
                var n = RanchPflege.Vorrat(State, item);
                var icon = item == "heu" ? "hay" : item == "apfel" ? "food_apple" : item == "karotte" ? "food_carrot" : item == "hafer" ? "sprout" : "candy";
                var b = Ux.IconTextBtn(icon, T("rhof.futter." + item) + " · " + n, () => Schenken(npc, it), "gb-btn-secondary", "gb-ranchhof-shopbtn");
                b.name = "ranchdorf-gib-" + item;
                b.SetEnabled(n > 0);
                _geschenkRow.Add(b);
            }
            _grussLabel?.parent?.parent?.Insert(_grussLabel.parent.parent.IndexOf(_grussLabel.parent) + 2, _geschenkRow);
            Sfx.PlayUi("soft_open");
        }

        private void Schenken(string npc, string item)
        {
            var tag = Today;
            var now = Now;
            (string reaktion, int vorher, int nachher) res = (null, 0, 0);
            _host.Store.Update(s => res = RanchNpc.Schenken(s, npc, item, tag, now), GameHost.TicksMs);
            if (res.reaktion == null) { Sfx.PlayUi("soft_error"); return; }
            _host.Store.NotifySliceChanged(RanchHof.SliceId);
            _host.Store.NotifySliceChanged("inventory");
            _geschenkRow?.RemoveFromHierarchy();
            _geschenkRow = null;
            if (_grussLabel != null) _grussLabel.text = "„" + Zeile(npc, "geschenk_" + res.reaktion) + "“";
            _stage?.Cheer(RanchNpc.Fuer(npc)?.Laden);
            Sfx.PlayUi(res.reaktion == "liebt" ? "soft_levelup" : "soft_collect");
            _juice.Burst(new Vector2(Root.worldBound.center.x, Root.worldBound.height * 0.3f), new Color(1f, 0.55f, 0.66f), res.reaktion == "liebt" ? 18 : 10);
            HerzenZeigen(npc);
            HerzAufstieg(npc, res.vorher, res.nachher);
            RefreshPills();
            Debug.Log("[ranchdorf] geschenk " + npc + " " + item + " → " + res.reaktion);
        }

        private void Erzaehlen(string npc)
        {
            var knoten = RanchNpc.HerzenVon(State, npc) >= 5 ? "geschichte_2" : "geschichte_1";
            if (_grussLabel != null) _grussLabel.text = "„" + Zeile(npc, knoten, 4) + "“";
            Sfx.PlayUi("soft_open");
        }

        /// <summary>A new heart: toast with what it brings (small talk, the discount, stories).</summary>
        private void HerzAufstieg(string npc, int vorher, int nachher)
        {
            if (nachher <= vorher) return;
            var def = RanchNpc.Fuer(npc);
            var name = T("rdorf.npc." + def.Laden);
            var key = nachher == 2 && def.RabattProzent > 0 ? "rdorf.freund.neu_rabatt" : nachher == 3 || nachher == 5 ? "rdorf.freund.neu_geschichte" : "rdorf.freund.neu_herz";
            _juice.Confetti(40);
            Sfx.PlayUi("soft_levelup");
            _host.Ui.Toast(T(key, ("name", name), ("n", def.RabattProzent), ("herzen", nachher)), "heart");
        }
    }
}
