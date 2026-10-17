using System;
using System.Collections.Generic;
using System.Globalization;
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
    public sealed partial class RanchBauScreen
    {
        private VisualElement _head, _coinPill, _drawer, _tabs, _aktionen, _info, _chipsLayer, _modal, _hint;
        private ScrollView _strip;
        private Label _titel, _sub, _coinsLabel, _hintLabel, _bauenLabel;
        private Button _bauenBtn, _drehenBtn;
        private bool _drawerZu;
        private string _kategorie = "anlage";
        private readonly Dictionary<string, Button> _tabBtns = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _zoneChips = new Dictionary<string, Button>();

        private static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            ["stallboxen"] = "house", ["weide"] = "sprout", ["wasserstelle"] = "bucket", ["heulager"] = "hay", ["waschplatz"] = "brush",
            ["fuehranlage"] = "redo", ["reithalle"] = "horseshoe", ["parcours"] = "race_flag", ["sattelkammer"] = "toolbox", ["zuchtstall"] = "heart",
            ["fohlenweide"] = "leaf", ["tribuene"] = "trophy", ["weidezaun"] = "fence", ["beet_blumen"] = "flower", ["beet_kraeuter"] = "leaf_pair",
            ["bank_holz"] = "sofa", ["laterne"] = "bulb", ["fahne"] = "race_flag", ["blumenkuebel"] = "flower", ["vogelhaus"] = "home",
            ["heuballen_deko"] = "hay", ["wegweiser"] = "sign", ["strohpuppe"] = "user", ["windspiel"] = "sparkle", ["weg_schotter"] = "path",
            ["weg_pflaster"] = "grid", ["sand"] = "sun", ["blumenwiese"] = "flower", ["zaun_holz"] = "fence",
            ["deko_mistgabel_gold"] = "pitchfork", ["deko_schleifenwand"] = "rosette", ["girlande_ranchfest"] = "bunting",
        };

        private static readonly Dictionary<string, (string icon, string farbe)> KategorieLook = new Dictionary<string, (string, string)>
        {
            ["anlage"] = ("house", "#FFE7C2"), ["deko"] = ("flower", "#FBD3DC"), ["boden"] = ("path", "#EDE5D6"), ["zaun"] = ("fence", "#D8E6F7"),
        };

        private static string IconVon(string id) => Icons.TryGetValue(id ?? "", out var i) ? i : "hammer";

        private static Color FarbeVon(string kategorie) => ArtLibrary.Hex(KategorieLook.TryGetValue(kategorie ?? "", out var k) ? k.farbe : "#FFF4E2");

        private void BaueUi(VisualElement root)
        {
            var back = Ux.IconBtn("back_white", Quit, "gb-place-back");
            back.name = "rbau-zu";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            _head = Ux.Row("gb-card", "gb-ranchhof-head", "gb-rbau-head");
            _head.name = "rbau-kopf";
            Ux.Abs(_head, 84, 12);
            _head.Add(Ux.IconTile("hammer", ArtLibrary.Hex("#FFE7C2")));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            _titel = Ux.Text(T("rbau.titel"), "gb-place-title");
            hcol.Add(_titel);
            _sub = Ux.Text("", "gb-sm", "gb-mild");
            _sub.name = "rbau-untertitel";
            hcol.Add(_sub);
            _head.Add(hcol);
            root.Add(_head);

            var pills = Ux.Row("gb-ranchhof-pills");
            Ux.Abs(pills, null, 14, 16);
            var effekte = Ux.IconBtn("sparkle", ZeigeEffekte, "gb-rbau-rund");
            effekte.name = "rbau-effekte";
            pills.Add(effekte);
            _coinPill = Ux.Pill("coin", "0", out _coinsLabel);
            _coinPill.name = "rbau-coins";
            pills.Add(_coinPill);
            root.Add(pills);

            var cam = Ux.Col("gb-rbau-kamera");
            cam.name = "rbau-kamera";
            foreach (var (name, icon, aktion, spiegel) in new (string, string, Action, bool)[]
            {
                ("rbau-dreh-links", "rotate", () => Kamera(-1), false), ("rbau-dreh-rechts", "rotate", () => Kamera(1), true),
            })
            {
                var b = Ux.IconBtn(icon, aktion, "gb-rbau-rund");
                b.name = name;
                if (spiegel) b.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
                cam.Add(b);
            }
            foreach (var (name, zeichen, faktor) in new[] { ("rbau-zoom-rein", "+", 1f / 1.3f), ("rbau-zoom-raus", "\u2212", 1.3f) })
            {
                var f = faktor;
                var b = new Button(() => { _welt?.Zoom(f); Sfx.PlayUi("soft_tick"); }) { name = name, text = zeichen };
                b.ClearClassList();
                b.AddToClassList("gb-btn-icon");
                b.AddToClassList("gb-rbau-rund");
                b.AddToClassList("gb-rbau-zoom");
                cam.Add(b);
            }
            root.Add(cam);

            _chipsLayer = new VisualElement { name = "rbau-zonen", pickingMode = PickingMode.Ignore };
            Ux.Abs(_chipsLayer, 0, 0, 0, 0);
            root.Add(_chipsLayer);

            _drawer = Ux.Col("gb-card", "gb-rbau-drawer");
            _drawer.name = "rbau-drawer";
            _tabs = Ux.Row("gb-rbau-tabs");
            foreach (var kat in RanchBauKatalog.Kategorien)
            {
                var k = kat;
                var tab = new Button(() => Kategorie(k)) { name = "rbau-tab-" + kat };
                tab.ClearClassList();
                tab.AddToClassList("gb-rbau-tab");
                var ic = Ux.Icon(KategorieLook[kat].icon);
                ic.pickingMode = PickingMode.Ignore;
                tab.Add(ic);
                var l = Ux.Text(T("rbau.kategorie." + kat), "gb-rbau-tab-text");
                l.pickingMode = PickingMode.Ignore;
                tab.Add(l);
                _tabs.Add(tab);
                _tabBtns[kat] = tab;
            }
            _drawer.Add(_tabs);
            _strip = new ScrollView(ScrollViewMode.Horizontal) { name = "rbau-strip" };
            _strip.AddToClassList("gb-rbau-strip");
            _strip.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _strip.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _drawer.Add(_strip);
            root.Add(_drawer);

            _hint = Ux.Row("gb-card", "gb-rbau-hint");
            _hint.name = "rbau-hint";
            _hint.pickingMode = PickingMode.Ignore;
            _hint.Add(Ux.Icon("hand"));
            _hintLabel = Ux.Text(T("rbau.ui.tipp"), "gb-sm");
            _hintLabel.style.marginLeft = 8;
            _hint.Add(_hintLabel);
            root.Add(_hint);

            _aktionen = Ux.Row("gb-card", "gb-rbau-aktionen");
            _aktionen.name = "rbau-aktionen";
            var abbrechen = Ux.IconTextBtn("close", T("rbau.ui.abbrechen"), Abbrechen, "gb-btn-secondary", "gb-rbau-aktion");
            abbrechen.name = "rbau-abbrechen";
            _aktionen.Add(abbrechen);
            _drehenBtn = Ux.IconTextBtn("rotate", T("rbau.ui.drehen"), Drehen, "gb-btn-secondary", "gb-rbau-aktion");
            _drehenBtn.name = "rbau-drehen";
            _aktionen.Add(_drehenBtn);
            _bauenBtn = new Button(Bauen) { name = "rbau-bauen" };
            _bauenBtn.ClearClassList();
            _bauenBtn.AddToClassList("gb-btn");
            _bauenBtn.AddToClassList("gb-btn-confirm");
            _bauenBtn.AddToClassList("gb-rbau-bauen");
            var hammer = Ux.Icon("hammer");
            hammer.pickingMode = PickingMode.Ignore;
            _bauenBtn.Add(hammer);
            _bauenLabel = Ux.Text("", "gb-rbau-bauen-text");
            _bauenLabel.pickingMode = PickingMode.Ignore;
            _bauenBtn.Add(_bauenLabel);
            _aktionen.Add(_bauenBtn);
            root.Add(_aktionen);

            _info = Ux.Col("gb-card", "gb-rbau-info");
            _info.name = "rbau-info";
            _info.style.display = DisplayStyle.None;
            root.Add(_info);
            Kategorie(_kategorie, false);
        }

        // ── the drawer ──

        private void Kategorie(string kat, bool klang = true)
        {
            if (kat == _kategorie && klang && _modus == Modus.Schauen)
            {
                // tapping the open tab folds the drawer away (and back)
                _drawerZu = !_drawerZu;
                RefreshModus();
                Sfx.PlayUi("soft_toggle");
                return;
            }
            _kategorie = kat;
            _drawerZu = false;
            foreach (var kv in _tabBtns) kv.Value.EnableInClassList("gb-rbau-tab--an", kv.Key == kat);
            FuelleStrip();
            if (klang) Sfx.PlayUi("soft_chip");
            RefreshModus();
        }

        private void FuelleStrip()
        {
            if (_strip == null) return;
            _strip.Clear();
            var ids = RanchBauKatalog.Ids(Bal, _kategorie);
            if (_kategorie == "zaun") ids.Insert(0, "weidezaun");
            if (_kategorie == "anlage") ids.Remove("weidezaun");
            // facilities in a playful order: what a young farm needs first
            if (_kategorie == "anlage") ids = ids.OrderBy(id => RanchBauKatalog.StufenKosten(Bal, id).FirstOrDefault()).ThenBy(id => id, StringComparer.Ordinal).ToList();
            // quest rewards waiting in the store lead, the ones still to earn close the row
            if (_kategorie == "deko") ids = ids.OrderBy(id => !Def(id).Belohnung ? 1 : RanchBauState.LagerAnzahl(_bau, id) > 0 ? 0 : UidVon(_bau, id) != null ? 2 : 3).ToList();
            foreach (var id in ids) _strip.Add(KarteFuer(id));
        }

        private Button KarteFuer(string id)
        {
            var def = Def(id);
            var b = new Button(() => Karte(id)) { name = "rbau-karte-" + id };
            b.ClearClassList();
            b.AddToClassList("gb-rbau-karte");
            var tile = Ux.IconTile(IconVon(id), FarbeVon(def.Kategorie));
            tile.pickingMode = PickingMode.Ignore;
            b.Add(tile);
            var name = Ux.Text(T(def.NameKey), "gb-rbau-karte-name");
            name.EnableInClassList("gb-rbau-karte-name--lang", T(def.NameKey).Split(' ', '-').Max(w => w.Length) > 13);
            name.pickingMode = PickingMode.Ignore;
            b.Add(name);
            var preis = Ux.Row("gb-rbau-karte-preis");
            preis.pickingMode = PickingMode.Ignore;
            var stufe = RanchBauState.AnlageStufe(_bau, id);
            var lager = RanchBauState.LagerAnzahl(_bau, id);
            string text;
            b.EnableInClassList("gb-rbau-karte--belohnung", def.Belohnung);
            if (def.Belohnung && lager <= 0)
            {
                var steht = UidVon(_bau, id) != null;
                text = steht ? T("rbau.ui.steht") : T("rbau.ui.belohnung_quest");
                b.EnableInClassList("gb-rbau-karte--gebaut", steht);
                b.EnableInClassList("gb-rbau-karte--gesperrt", !steht);
                var marke = Ux.Icon(steht ? "check" : "star");
                marke.pickingMode = PickingMode.Ignore;
                preis.Add(marke);
            }
            else if (def.Upgrade)
            {
                var naechste = RanchBauKatalog.NaechsteStufeKosten(Bal, id, stufe);
                text = naechste < 0 ? T("rbau.stufe_max") : stufe > 0 ? T("rbau.ui.stufe_kurz", ("n", stufe)) + " · " + naechste + " G" : naechste + " G";
                if (naechste >= 0 && Coins < naechste) b.AddToClassList("gb-rbau-karte--teuer");
            }
            else if (def.Kategorie == "anlage" && stufe > 0)
            {
                text = T("rbau.stufe", ("n", stufe)) + "/" + RanchBauKatalog.MaxStufe(Bal, id);
                b.AddToClassList("gb-rbau-karte--gebaut");
            }
            else if (lager > 0)
            {
                text = T("rbau.ui.lager", ("n", lager));
                b.AddToClassList("gb-rbau-karte--lager");
            }
            else
            {
                text = def.Kosten + " G";
                if (Coins < def.Kosten) b.AddToClassList("gb-rbau-karte--teuer");
            }
            if (!def.Belohnung && !(def.Kategorie == "anlage" && stufe > 0) && lager <= 0 && !(def.Upgrade && stufe >= RanchBauKatalog.MaxStufe(Bal, id)))
            {
                var coin = Ux.Icon("coin");
                coin.pickingMode = PickingMode.Ignore;
                preis.Add(coin);
            }
            var pl = Ux.Text(text, "gb-rbau-karte-preistext");
            pl.pickingMode = PickingMode.Ignore;
            preis.Add(pl);
            b.Add(preis);
            if (_def != null && _def.Id == id) b.AddToClassList("gb-rbau-karte--an");
            return b;
        }

        // ── modes on screen ──

        private void RefreshModus()
        {
            if (_drawer == null) return;
            var setzt = _modus != Modus.Schauen;
            _drawer.style.display = setzt || _drawerZu ? DisplayStyle.None : DisplayStyle.Flex;
            _tabs.style.display = DisplayStyle.Flex;
            _aktionen.style.display = setzt ? DisplayStyle.Flex : DisplayStyle.None;
            _drehenBtn.style.display = setzt && _modus != Modus.Linie ? DisplayStyle.Flex : DisplayStyle.None;
            _hint.EnableInClassList("gb-rbau-hint--oben", setzt);
            _hintLabel.text = _modus switch
            {
                Modus.Setzen => T("rbau.ui.tipp_platz"),
                Modus.Verschieben => T("rbau.ui.tipp_verschieben"),
                Modus.Linie => T("rbau.ui.tipp_linie"),
                _ => T("rbau.ui.tipp"),
            };
            _hint.style.display = setzt || _grid.Items.Count <= 3 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_drawerZu && !setzt) _hint.style.display = DisplayStyle.None;
            RefreshAktion();
            if (setzt) FuelleStrip();
        }

        private void RefreshAktion()
        {
            if (_bauenLabel == null) return;
            if (_modus == Modus.Verschieben)
            {
                _bauenLabel.text = T("rbau.ui.hierhin");
                _bauenBtn.SetEnabled(GhostOk);
                return;
            }
            if (_modus == Modus.Linie)
            {
                var n = LinieAnzahl;
                _bauenLabel.text = T("rbau.ui.bauen_n", ("n", n), ("preis", n * (_def?.Kosten ?? 0)));
                _bauenBtn.SetEnabled(n > 0);
                return;
            }
            var preis = Preis;
            _bauenLabel.text = preis > 0 ? T("rbau.ui.bauen", ("preis", preis)) : T("rbau.ui.aufstellen");
            _bauenBtn.SetEnabled(GhostOk);
            _bauenBtn.EnableInClassList("gb-rbau-bauen--teuer", preis > Coins);
        }

        private void RefreshAll()
        {
            if (_coinsLabel == null) return;
            _coinsLabel.text = Ux.FormatNumber(Coins);
            _sub.text = T("rbau.ui.untertitel", ("stil", RanchBauEffekte.DekoStilpunkte(_bau, Bal)), ("max", RanchBauEffekte.DekoStilMax(Bal)),
                ("boxen", RanchBauEffekte.BoxenKapazitaet(State, RanchWirtschaft.BoxenKapazitaeten[RanchWirtschaft.Boxen(State) - 1])),
                ("heu", RanchBauEffekte.HeuKapazitaet(_bau, Bal)));
            FuelleStrip();
            BaueChips();
            if (_auswahlUid != null) ZeigeInfo(_auswahlUid);
            RefreshModus();
        }

        // ── selection and the info card ──

        /// <summary>Selects a placed piece (null clears): outline in 3D, its card on the right.</summary>
        private void Waehle(string uid)
        {
            var item = uid != null ? _grid.GetItem(uid) : null;
            _auswahlUid = item?.Uid;
            _welt?.Auswahl(item);
            if (item == null) { _info.style.display = DisplayStyle.None; return; }
            Sfx.PlayUi("soft_tap");
            ZeigeInfo(item.Uid);
        }

        private void ZeigeInfo(string uid)
        {
            var item = _grid.GetItem(uid);
            if (item == null) { _info.style.display = DisplayStyle.None; _auswahlUid = null; return; }
            _info.Clear();
            _info.style.display = DisplayStyle.Flex;
            var def = item.Def;
            var id = def.Id;
            var kopf = Ux.Row("gb-rbau-info-kopf");
            kopf.Add(Ux.IconTile(IconVon(id), FarbeVon(def.Kategorie)));
            var col = Ux.Col();
            col.style.marginLeft = 10;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(Ux.Text(T(def.NameKey), "gb-h2"));
            var stufe = RanchBauState.AnlageStufe(_bau, id);
            var max = RanchBauKatalog.MaxStufe(Bal, id);
            if (def.Kategorie == "anlage") col.Add(Sterne(stufe, max));
            else col.Add(Ux.Text(T("rbau.kategorie." + def.Kategorie), "gb-sm", "gb-mild"));
            kopf.Add(col);
            var zu = Ux.IconBtn("close", () => Waehle(null), "gb-btn-icon-sm");
            zu.name = "rbau-info-zu";
            kopf.Add(zu);
            _info.Add(kopf);
            if (def.Kategorie == "anlage")
            {
                var nutzen = Ux.Text(T("rbau.nutzen." + id, ("n", NutzenZahl(id))), "gb-md");
                nutzen.style.whiteSpace = WhiteSpace.Normal;
                nutzen.style.marginTop = 8;
                _info.Add(nutzen);
                var liste = Ux.Col("gb-rbau-stufen");
                for (var i = 1; i <= max; i++)
                {
                    var row = Ux.Row("gb-rbau-stufe");
                    row.EnableInClassList("gb-rbau-stufe--da", i <= stufe);
                    row.EnableInClassList("gb-rbau-stufe--naechste", i == stufe + 1);
                    var marke = Ux.Text(i <= stufe ? "✓" : i.ToString(), "gb-rbau-stufe-marke");
                    row.Add(marke);
                    var text = Ux.Text(T("rbau.stufen." + id + "." + i), "gb-sm");
                    text.style.whiteSpace = WhiteSpace.Normal;
                    text.style.flexShrink = 1;
                    row.Add(text);
                    liste.Add(row);
                }
                _info.Add(liste);
                var naechste = RanchBauKatalog.NaechsteStufeKosten(Bal, id, stufe);
                if (naechste >= 0)
                {
                    var aus = Ux.IconTextBtn("star", T("rbau.ausbauen", ("preis", naechste)), Ausbauen, "gb-btn-confirm", "gb-rbau-info-btn");
                    aus.name = "rbau-ausbauen";
                    if (Coins < naechste) aus.AddToClassList("gb-rbau-bauen--teuer");
                    _info.Add(aus);
                }
                else _info.Add(Ux.Text(T("rbau.stufe_max"), "gb-md", "gb-bold", "gb-rbau-max"));
            }
            else if (def.Kategorie == "deko")
            {
                var stil = Ux.Text(T("rbau.ui.effekt_stil", ("n", RanchBauEffekte.DekoStilpunkte(_bau, Bal)), ("max", RanchBauEffekte.DekoStilMax(Bal))), "gb-md");
                stil.style.marginTop = 8;
                _info.Add(stil);
                if (def.Belohnung)
                {
                    var info = Ux.Text(T("rbau.ui.belohnung_info"), "gb-sm", "gb-mild");
                    info.style.whiteSpace = WhiteSpace.Normal;
                    info.style.marginTop = 4;
                    _info.Add(info);
                }
                if (id == "deko_schleifenwand")
                {
                    var wand = Ux.Text(T("rbau.ui.schleifen", ("n", RanchBelohnungen.Wandschleifen(State).Count)), "gb-sm");
                    wand.name = "rbau-schleifen";
                    wand.style.marginTop = 4;
                    _info.Add(wand);
                }
            }
            var reihe = Ux.Row("gb-rbau-info-reihe");
            if (item.Kante == null)
            {
                var versch = Ux.IconTextBtn("move", T("rbau.ui.verschieben"), Verschieben, "gb-btn-secondary", "gb-btn-sm");
                versch.name = "rbau-verschieben";
                reihe.Add(versch);
            }
            var abriss = def.Belohnung
                ? Ux.IconTextBtn("inventory", T("rbau.ui.abbau_ja"), () => FrageAbriss(uid), "gb-btn-secondary", "gb-btn-sm")
                : Ux.IconTextBtn("trash", T("rbau.abriss"), () => FrageAbriss(uid), "gb-btn-secondary", "gb-btn-sm");
            abriss.name = "rbau-abriss";
            reihe.Add(abriss);
            _info.Add(reihe);
        }

        private int NutzenZahl(string id)
        {
            switch (id)
            {
                case "stallboxen": return RanchBauEffekte.BoxenKapazitaet(_bau, Bal);
                case "heulager": return RanchBauEffekte.HeuKapazitaet(_bau, Bal);
                case "tribuene": return RanchBauEffekte.TribueneZuschauer(_bau, Bal);
                default: return 0;
            }
        }

        private VisualElement Sterne(int stufe, int max)
        {
            var row = Ux.Row("gb-rbau-sterne");
            for (var i = 1; i <= max; i++)
            {
                var s = Ux.El("gb-rbau-stern");
                s.EnableInClassList("gb-rbau-stern--an", i <= stufe);
                row.Add(s);
            }
            row.Add(Ux.Text(T("rbau.stufe", ("n", stufe)), "gb-sm", "gb-mild", "gb-rbau-sterne-text"));
            return row;
        }

        // ── the zones' price chips ──

        private void BaueChips()
        {
            if (_chipsLayer == null) return;
            _chipsLayer.Clear();
            _zoneChips.Clear();
            foreach (var zone in RanchBauKatalog.Zonen(Bal))
            {
                if (RanchBauState.ZoneFrei(_bau, zone.Id)) continue;
                var z = zone.Id;
                var chip = new Button(() => FrageZone(z)) { name = "rbau-zone-" + z };
                chip.ClearClassList();
                chip.AddToClassList("gb-rbau-zone");
                var ic = Ux.Icon("lock_white");
                ic.pickingMode = PickingMode.Ignore;
                chip.Add(ic);
                var l = Ux.Text(T("rbau.ui.zone_chip", ("name", T("rbau.zone." + z)), ("preis", zone.Kosten)), "gb-rbau-zone-text");
                l.pickingMode = PickingMode.Ignore;
                chip.Add(l);
                chip.EnableInClassList("gb-rbau-zone--teuer", Coins < zone.Kosten);
                _chipsLayer.Add(chip);
                _zoneChips[z] = chip;
            }
        }

        private void PlatziereChips()
        {
            if (_welt == null || _zoneChips.Count == 0 || float.IsNaN(FeldW) || FeldW <= 0f) return;
            foreach (var kv in _zoneChips)
            {
                var zone = RanchBauKatalog.ZoneVon(Bal, kv.Key);
                var cx = (zone.Rect.X + zone.Rect.W * 0.5) * RanchGridData.CellSize;
                var cz = (zone.Rect.Y + zone.Rect.H * 0.5) * RanchGridData.CellSize;
                var p = _welt.ToField(cx, 0.5, cz, FeldW, FeldH);
                var sichtbar = _welt.Sichtbar(cx, 0.5, cz) && p.x > 40f && p.x < FeldW - 40f && p.y > 60f && p.y < FeldH - 40f && _modus == Modus.Schauen;
                kv.Value.style.display = sichtbar ? DisplayStyle.Flex : DisplayStyle.None;
                kv.Value.style.left = p.x;
                kv.Value.style.top = p.y;
            }
        }

        // ── dialogs ──

        private void Frage(string icon, string text, string ja, Action jaAktion, string name, string nein = null)
        {
            SchliesseModal();
            _modal = new VisualElement { name = "rbau-modal" };
            _modal.AddToClassList("gb-rbau-modal");
            Ux.Abs(_modal, 0, 0, 0, 0);
            _modal.RegisterCallback<PointerDownEvent>(e => { if (e.target == _modal) { SchliesseModal(); Sfx.PlayUi("soft_close"); } });
            var card = Ux.Col("gb-card", "gb-rbau-frage");
            card.name = name;
            card.Add(Ux.IconTile(icon, ArtLibrary.Hex(icon.EndsWith("_white") ? "#5B5470" : "#FFE7C2")));
            var l = Ux.Text(text, "gb-h3");
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 10;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(l);
            var row = Ux.Row("gb-rbau-frage-reihe");
            var neinBtn = Ux.IconTextBtn("close", nein ?? T("rbau.ui.abbrechen"), () => { SchliesseModal(); Sfx.PlayUi("soft_close"); }, "gb-btn-secondary");
            neinBtn.name = "rbau-nein";
            row.Add(neinBtn);
            var jaBtn = Ux.IconTextBtn("check", ja, () => { SchliesseModal(); jaAktion(); }, "gb-btn-confirm");
            jaBtn.name = "rbau-ja";
            row.Add(jaBtn);
            card.Add(row);
            _modal.Add(card);
            Root.Add(_modal);
            _juice.ScalePop(card, 1.05f, 0.2f);
            Sfx.PlayUi("soft_open");
        }

        private void SchliesseModal()
        {
            _modal?.RemoveFromHierarchy();
            _modal = null;
        }

        private void FrageAbriss(string uid)
        {
            var item = _grid.GetItem(uid);
            if (item == null) return;
            var n = RanchBauState.ErstattungFuer(_bau, uid, Bal);
            if (item.Def.Kategorie != "anlage") { Abreissen(uid); return; }
            Frage("trash", T("rbau.ui.abriss_frage", ("name", T(item.Def.NameKey)), ("n", n)), T("rbau.ui.abriss_ja"), () => Abreissen(uid), "rbau-frage-abriss", T("rbau.ui.behalten"));
        }

        private void FrageZone(string zoneId)
        {
            var zone = RanchBauKatalog.ZoneVon(Bal, zoneId);
            if (zone == null) return;
            Frage("lock_white", T("rbau.ui.zone_frage", ("name", T("rbau.zone." + zoneId)), ("preis", zone.Kosten)), T("rbau.ui.zone_ja"), () => ZoneFrei(zoneId), "rbau-frage-zone");
        }

        private void FrageWeidezaun()
        {
            var stufe = RanchBauState.AnlageStufe(_bau, "weidezaun");
            var preis = RanchBauKatalog.NaechsteStufeKosten(Bal, "weidezaun", stufe);
            if (preis < 0) { _host.Ui.Toast(T("rbau.fehler.vollAusgebaut"), "fence"); return; }
            Frage("fence", T("rbau.item.weidezaun") + ": " + T("rbau.stufen.weidezaun." + (stufe + 1)), T("rbau.ausbauen", ("preis", preis)), WeidezaunAusbauen, "rbau-frage-weidezaun");
        }

        /// <summary>What the farm can do now: every built facility at its level, the boxes, the hay store and the style.</summary>
        private void ZeigeEffekte()
        {
            SchliesseModal();
            Sfx.PlayUi("soft_open");
            _modal = new VisualElement { name = "rbau-modal" };
            _modal.AddToClassList("gb-rbau-modal");
            Ux.Abs(_modal, 0, 0, 0, 0);
            _modal.RegisterCallback<PointerDownEvent>(e => { if (e.target == _modal) { SchliesseModal(); Sfx.PlayUi("soft_close"); } });
            var card = Ux.Col("gb-card", "gb-rbau-effekte");
            card.name = "rbau-effekte-karte";
            var kopf = Ux.Row("gb-rbau-info-kopf");
            kopf.Add(Ux.IconTile("sparkle", ArtLibrary.Hex("#FFF0B8")));
            var t = Ux.Text(T("rbau.ui.effekte"), "gb-h2");
            t.style.marginLeft = 10;
            t.style.flexGrow = 1;
            kopf.Add(t);
            var zu = Ux.IconBtn("close", () => { SchliesseModal(); Sfx.PlayUi("soft_close"); }, "gb-btn-icon-sm");
            zu.name = "rbau-effekte-zu";
            kopf.Add(zu);
            card.Add(kopf);
            var chips = Ux.Row("gb-rbau-effekt-chips");
            foreach (var (icon, text) in new[]
            {
                ("house", T("rbau.ui.effekt_boxen", ("n", RanchWirtschaft.BoxenKapazitaet(State)))),
                ("hay", T("rbau.ui.effekt_heu", ("n", RanchBauEffekte.HeuKapazitaet(_bau, Bal)))),
                ("sparkle", T("rbau.ui.effekt_stil", ("n", RanchBauEffekte.DekoStilpunkte(_bau, Bal)), ("max", RanchBauEffekte.DekoStilMax(Bal)))),
                ("brush", T("rbau.ui.effekt_putzen", ("n", RanchBauEffekte.StriegelWert(_bau, Bal)))),
            })
            {
                var c = Ux.Row("gb-rbau-effekt-chip");
                c.Add(Ux.Icon(icon));
                c.Add(Ux.Text(text, "gb-sm", "gb-bold"));
                chips.Add(c);
            }
            card.Add(chips);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-rbau-effekte-scroll");
            var gebaut = RanchBauKatalog.Ids(Bal, "anlage").Where(id => RanchBauState.AnlageStufe(_bau, id) > 0).ToList();
            if (gebaut.Count == 0) scroll.Add(Ux.Text(T("rbau.ui.leer"), "gb-md", "gb-mild"));
            foreach (var id in gebaut)
            {
                var stufe = RanchBauState.AnlageStufe(_bau, id);
                var row = Ux.Row("gb-rbau-effekt-zeile");
                row.Add(Ux.IconTile(IconVon(id), FarbeVon("anlage")));
                var col = Ux.Col();
                col.style.marginLeft = 10;
                col.style.flexShrink = 1;
                col.Add(Ux.Text(T("rbau.item." + id) + " · " + T("rbau.stufe", ("n", stufe)), "gb-md", "gb-bold"));
                var text = Ux.Text(string.Join(" ", Enumerable.Range(1, stufe).Select(i => T("rbau.stufen." + id + "." + i))), "gb-sm", "gb-mild");
                text.style.whiteSpace = WhiteSpace.Normal;
                col.Add(text);
                row.Add(col);
                scroll.Add(row);
            }
            card.Add(scroll);
            _modal.Add(card);
            Root.Add(_modal);
            _juice.ScalePop(card, 1.04f, 0.2f);
        }

        // ── QA ──

        /// <summary>
        /// "rbau kat deko | waehle reithalle | ghost 5 11 [rot] | drehen | bauen | abbrechen | linie x0 y0 x1 y1 | tippe x y |
        /// ausbauen | verschieben | abriss | zone nord | weidezaun | kamera yaw dist gx gz | effekte | zu".
        /// </summary>
        public void Qa(string[] roh)
        {
            // QaDriver hands over at most three parts; the arguments of a placement need every word
            var parts = new[] { roh.Length > 0 ? roh[0] : "rbau" }.Concat(string.Join(" ", roh.Skip(1)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            var a1 = parts.Length > 1 ? parts[1] : "";
            int N(int i, int fallback = 0) => parts.Length > i && int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
            float F(int i, float fallback) => parts.Length > i && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
            switch (a1)
            {
                case "kat": Kategorie(parts.Length > 2 ? parts[2] : "anlage"); break;
                case "waehle": Karte(parts.Length > 2 ? parts[2] : ""); break;
                case "ghost":
                    if (parts.Length > 4) _ghostRot = N(4) % 4;
                    SetzeGhost(new Zelle(N(2), N(3)));
                    break;
                case "drehen": Drehen(); break;
                case "bauen": Bauen(); break;
                case "abbrechen": Abbrechen(); break;
                case "linie":
                    if (_modus != Modus.Linie || _def == null) break;
                    if (_def.Kante)
                    {
                        _strichStart = (N(2), N(3));
                        Strich(N(4) * RanchGridData.CellSize, N(5) * RanchGridData.CellSize);
                        UebernimmStrich();
                    }
                    else
                    {
                        _radiert = false;
                        Male(new Zelle(N(2), N(3)));
                        MaleBis(N(4) * RanchGridData.CellSize + 1.5, N(5) * RanchGridData.CellSize + 1.5);
                    }
                    break;
                case "tippe": TippeWelt(N(2) * RanchGridData.CellSize + 1.5, N(3) * RanchGridData.CellSize + 1.5); break;
                case "ausbauen": Ausbauen(); break;
                case "verschieben": Verschieben(); break;
                case "abriss": if (_auswahlUid != null) Abreissen(_auswahlUid); break;
                case "zone": ZoneFrei(parts.Length > 2 ? parts[2] : "nord"); break;
                case "weidezaun": WeidezaunAusbauen(); break;
                case "kamera": _welt?.SetzeKamera(F(2, 0f), F(3, 54f), F(4, 24f), F(5, 30f)); break;
                case "effekte": ZeigeEffekte(); break;
                case "frage": if (parts.Length > 2 && parts[2] == "zone") FrageZone(parts.Length > 3 ? parts[3] : "nord"); else if (_auswahlUid != null) FrageAbriss(_auswahlUid); break;
                case "zu": SchliesseModal(); Waehle(null); break;
                case "drawer": _drawerZu = !_drawerZu; RefreshModus(); break;
                default: Debug.LogWarning("[qa] rbau: unknown " + a1); break;
            }
            Debug.Log("[qa] rbau " + string.Join(" ", parts.Skip(1)));
        }

        public JObject QaState()
        {
            var o = new JObject
            {
                ["modus"] = _modus.ToString(),
                ["kategorie"] = _kategorie,
                ["def"] = _def?.Id ?? "",
                ["ghost"] = new JArray(_ghostAt.X, _ghostAt.Y, _ghostRot),
                ["ghostOk"] = GhostOk,
                ["linie"] = _modus == Modus.Linie ? LinieAnzahl : 0,
                ["auswahl"] = _auswahlUid ?? "",
                ["teile"] = _grid?.Items.Count ?? 0,
                ["coins"] = Coins,
                ["zonen"] = new JArray(((JArray)_bau["zonen"]).Select(t => J.Str(t))),
                ["stil"] = RanchBauEffekte.DekoStilpunkte(_bau, Bal),
                ["boxen"] = RanchWirtschaft.BoxenKapazitaet(State),
                ["modal"] = _modal != null,
                ["kamera"] = _welt != null ? new JArray(Math.Round(_welt.Yaw, 1), Math.Round(_welt.Dist, 1)) : new JArray(),
            };
            var anlagen = new JObject();
            foreach (var p in J.Dict(_bau["anlagen"]).Properties()) anlagen[p.Name] = RanchBauState.AnlageStufe(_bau, p.Name);
            o["anlagen"] = anlagen;
            return o;
        }
    }
}
