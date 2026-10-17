using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The GOOBEA furniture store (port of ikea_screen.gd + furniture_showcase.gd + shop_purchase.gd): left the search,
    /// the category chips and the shelf of pieces (cheapest first), right the showroom — the piece turning on a podium
    /// in 3D (drag to turn it, zoom), its colours as swatches (recoloured live), how many floor cells it takes and its
    /// storage weight, the price and "Kaufen". Bought furniture goes into the storage in its colour; build mode places
    /// it from there and the room keeps the colour.
    /// </summary>
    public sealed class GoobeaScreen : UiScreen
    {
        private const float ShowW = 760f, ShowH = 360f;

        private readonly GameHost _host;
        private readonly Juice _juice = new Juice();
        private string _kategorie = "", _query = "", _selected, _variant = FurnitureVariants.DefaultId;
        private VisualElement _list, _chips, _show, _swatches, _coinsPill, _lagerPill;
        private Label _name, _meta, _price, _coins, _lager, _variantLabel, _hint;
        private Button _buy;
        private MgStage3D _stage;
        private Transform _turntable, _model;
        private float _spin = 25f, _drag, _zoom = 1f, _baseScale = 1f;
        private int _loadToken;
        private bool _dragging;
        private float _lastX;

        public GoobeaScreen(GameHost host)
        {
            _host = host;
            Id = "goobea";
            Opaque = true;
        }

        public override string SoundScene => "city/ort/ikea";

        private string T(string key, params (string, object)[] args) => _host.Strings.T("shop.ikea." + key, args);

        private string Name(FurnitureDef d) => d.DisplayName(_host.Settings.Locale);

        private static string IconFor(string kategorie)
        {
            switch (kategorie)
            {
                case "sitzen": return "sofa";
                case "tische": return "grid";
                case "schlafen": return "moon";
                case "kueche": return "teacup";
                case "bad": return "toothbrush";
                case "buero": return "monitor";
                case "lampen": return "bulb";
                case "pflanzen": case "garten": return "sprout";
                case "teppiche": return "rug";
                case "regale": return "books";
                case "bilder": return "photos_white";
                case "party": case "girlanden": return "star";
                case "unterhaltung": return "gamepad";
                case "fenster": return "home";
                default: return "house";
            }
        }

        private static Color TintFor(string kategorie)
        {
            switch (kategorie)
            {
                case "sitzen": case "wohnen": return new Color(1f, 0.86f, 0.86f);
                case "kueche": case "tische": return new Color(1f, 0.93f, 0.75f);
                case "bad": return new Color(0.8f, 0.92f, 1f);
                case "pflanzen": case "garten": return new Color(0.82f, 0.94f, 0.82f);
                case "schlafen": return new Color(0.88f, 0.85f, 1f);
                default: return new Color(0.95f, 0.9f, 0.82f);
            }
        }

        protected override void Build(VisualElement root)
        {
            root.AddToClassList("gb-goobea");
            var back = Ux.IconBtn("back_white", Quit, "gb-place-back");
            back.name = "goobea-quit";
            Ux.Abs(back, 16, 16);
            root.Add(back);
            var head = Ux.Row("gb-card", "gb-goobea-head");
            Ux.Abs(head, 84, 12);
            head.Add(Ux.IconTile("sofa", new Color(0.31f, 0.47f, 0.84f)));
            var hcol = Ux.Col();
            hcol.style.marginLeft = 10;
            hcol.Add(Ux.Text(T("title"), "gb-place-title"));
            hcol.Add(Ux.Text(T("untertitel"), "gb-sm", "gb-mild"));
            head.Add(hcol);
            root.Add(head);
            var pills = Ux.Row("gb-goobye-pills");
            Ux.Abs(pills, null, 14, 16);
            _lagerPill = Ux.Pill("inventory", "", out _lager);
            _lagerPill.name = "goobea-lager";
            pills.Add(_lagerPill);
            _coinsPill = Ux.Pill("coin", "", out _coins);
            _coinsPill.name = "goobea-coins";
            pills.Add(_coinsPill);
            root.Add(pills);

            var body = Ux.Row("gb-goobea-body");
            var left = Ux.Col("gb-card", "gb-goobea-left");
            var search = new TextField { name = "goobea-suche" };
            search.AddToClassList("gb-textfield");
            search.textEdition.placeholder = T("search");
            search.RegisterValueChangedCallback(e => { _query = e.newValue ?? ""; RefreshList(); });
            left.Add(search);
            var chipScroll = new ScrollView(ScrollViewMode.Horizontal) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            chipScroll.AddToClassList("gb-goobea-chipscroll");
            _chips = Ux.Row("gb-goobea-chips");
            chipScroll.Add(_chips);
            left.Add(chipScroll);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-goobea-scroll");
            _list = Ux.Row("gb-goobea-list");
            scroll.Add(_list);
            left.Add(scroll);
            body.Add(left);

            var right = Ux.Col("gb-card", "gb-goobea-right");
            _show = new VisualElement { name = "goobea-show" };
            _show.AddToClassList("gb-goobea-show");
            _show.RegisterCallback<PointerDownEvent>(e => { _dragging = true; _lastX = e.position.x; _show.CapturePointer(e.pointerId); });
            _show.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                _drag += (e.position.x - _lastX) * 0.6f;
                _lastX = e.position.x;
            });
            _show.RegisterCallback<PointerUpEvent>(e => { _dragging = false; _show.ReleasePointer(e.pointerId); });
            right.Add(_show);
            var info = Ux.Row("gb-goobea-info");
            var icol = Ux.Col();
            icol.style.flexGrow = 1;
            _name = Ux.Text("", "gb-h2");
            _name.name = "goobea-name";
            icol.Add(_name);
            _meta = Ux.Text("", "gb-sm", "gb-mild");
            icol.Add(_meta);
            info.Add(icol);
            var zoom = Ux.Slider(0.5, v => _zoom = 0.75f + (float)v * 0.6f);
            zoom.AddToClassList("gb-goobea-zoom");
            info.Add(Ux.Text(T("zoom"), "gb-sm", "gb-mild"));
            info.Add(zoom);
            right.Add(info);
            var swatchRow = Ux.Row("gb-goobea-swatchrow");
            _variantLabel = Ux.Text("", "gb-md", "gb-bold");
            swatchRow.Add(_variantLabel);
            _swatches = Ux.Row("gb-goobea-swatches");
            swatchRow.Add(_swatches);
            right.Add(swatchRow);
            var buyRow = Ux.Row("gb-goobea-buyrow");
            _hint = Ux.Text("", "gb-sm", "gb-mild");
            _hint.style.flexGrow = 1;
            _hint.style.whiteSpace = WhiteSpace.Normal;
            buyRow.Add(_hint);
            _price = Ux.Text("", "gb-goobea-price");
            buyRow.Add(_price);
            _buy = Ux.IconTextBtn("coin", T("buy"), Kaufen, "gb-btn-confirm", "gb-goobea-buy");
            _buy.name = "goobea-kaufen";
            buyRow.Add(_buy);
            right.Add(buyRow);
            body.Add(right);
            root.Add(body);
            root.Add(_juice.Layer);

            if (MgStage3D.Supported)
            {
                _stage = new MgStage3D(ShowW, ShowH, ArtLibrary.Hex("#FFF4E4")) { HeightShare = ShowH / 740f };
                _stage.Bind(_show);
                _stage.Aim(new Vector3(0f, 1.25f, -4.4f), 9f, 32f);
                BuildShowroom();
            }
            _host.Store.CoinsChanged += OnCoins;
            RefreshChips();
            RefreshList();
            RefreshWallet();
            var first = MoebelShop.Filter("", _kategorie).FirstOrDefault();
            if (first != null) Select(first.Id);
            Debug.Log("[goobea] auf: " + MoebelShop.Filter("", "").Count + " Möbel in " + MoebelShop.Categories().Count + " Kategorien");
        }

        public override void OnHidden()
        {
            _host.Store.CoinsChanged -= OnCoins;
            if (_host.Ui.IsMounted(this)) return;
            _stage?.Destroy();
            _stage = null;
        }

        private void OnCoins(int _) => RefreshWallet();

        // ── the showroom ──

        private void BuildShowroom()
        {
            var root = _stage.Root.transform;
            ArtLibrary.Box(root, "Floor", MgStage3D.At(0f, -0.05f, 1f), new Vector3(14f, 0.1f, 10f), ArtLibrary.Pbr("wood_floor", 1.4f, ArtLibrary.Hex("#E9C9A0"), 0.25f), false);
            ArtLibrary.Box(root, "Wall", MgStage3D.At(0f, 2.5f, 3.2f), new Vector3(14f, 5f, 0.1f), ArtLibrary.Solid(ArtLibrary.Hex("#DCE6FA"), 0.05f), false);
            ArtLibrary.Box(root, "WallBand", MgStage3D.At(0f, 2.9f, 3.14f), new Vector3(14f, 0.32f, 0.04f), ArtLibrary.Solid(ArtLibrary.Hex("#FFD100"), 0.2f), false);
            ArtLibrary.Cylinder(root, "Podium", MgStage3D.At(0f, 0.08f, 0.6f), 1.25f, 0.16f, ArtLibrary.Solid(ArtLibrary.Hex("#FFFFFF"), 0.35f));
            ArtLibrary.Cylinder(root, "PodiumRim", MgStage3D.At(0f, 0.16f, 0.6f), 1.27f, 0.02f, ArtLibrary.Solid(ArtLibrary.Hex("#4E79D6"), 0.3f), false);
            _stage.HomeProp("plant_tall", MgStage3D.At(-2.9f, 0f, 2.4f), 0.7f);
            _stage.HomeProp("lamp_floor", MgStage3D.At(2.9f, 0f, 2.4f), 0.8f);
            var turn = new GameObject("Turntable").transform;
            turn.SetParent(root, false);
            turn.position = MgStage3D.At(0f, 0.17f, 0.6f);
            _turntable = turn;
        }

        private async void ShowModel(FurnitureDef def)
        {
            if (_stage == null || _turntable == null || def == null) return;
            var token = ++_loadToken;
            if (_model != null) UnityEngine.Object.Destroy(_model.gameObject);
            _model = null;
            var archetype = AssetLoader.ArchetypeFor(def.Id);
            GameObject go = null;
            if (archetype.Length > 0) go = await AssetLoader.PropInstanceAsync(archetype, _turntable);
            if (token != _loadToken || _turntable == null) { if (go != null) UnityEngine.Object.Destroy(go); return; }
            if (go == null)
            {
                go = ArtLibrary.Box(_turntable, "Stand-in", _turntable.position + Vector3.up * 0.4f, new Vector3(0.9f, 0.8f, 0.6f), ArtLibrary.Solid(TintFor(def.Kategorie), 0.3f));
            }
            go.transform.SetParent(_turntable, true);
            go.transform.localRotation = Quaternion.identity;
            // fit the piece to the podium: the longest side about 1.6 m, standing on the top
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                _baseScale = longest > 1e-3f ? 1.6f / longest : 1f;
                go.transform.localScale *= _baseScale;
                rs = go.GetComponentsInChildren<Renderer>();
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += new Vector3(_turntable.position.x - b.center.x, _turntable.position.y - b.min.y, _turntable.position.z - b.center.z);
            }
            _model = go.transform;
            VariantTint.Apply(go, _variant);
            Effects.Sparkle(_turntable.position + Vector3.up * 0.8f, 0.6f, _host.Settings.ReducedMotion ? 4 : 14);
        }

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            if (_turntable == null) return;
            if (!_dragging && !_host.Settings.ReducedMotion) _drag += _spin * dt;
            _turntable.rotation = Quaternion.Euler(0f, 160f + _drag, 0f);
            _turntable.localScale = Vector3.one * _zoom;
        }

        // ── the shelf ──

        private void RefreshChips()
        {
            _chips.Clear();
            void Chip(string id, string label)
            {
                var b = new Button(() => { _kategorie = id; Sfx.PlayUi("soft_toggle"); RefreshChips(); RefreshList(); }) { text = label, name = "goobea-kat-" + (id.Length == 0 ? "alle" : id) };
                b.ClearClassList();
                b.AddToClassList("gb-chip");
                b.EnableInClassList("gb-chip--active", id == _kategorie);
                _chips.Add(b);
            }
            Chip("", T("alle"));
            foreach (var k in MoebelShop.Categories()) Chip(k, _host.Strings.T("shop.kategorie." + k));
        }

        private void RefreshList()
        {
            _list.Clear();
            var items = MoebelShop.Filter(_query, _kategorie);
            if (items.Count == 0)
            {
                _list.Add(CounterPanel.Empty(T("leer"), ""));
                return;
            }
            foreach (var d in items)
            {
                var def = d;
                var card = Ux.Row("gb-goobea-item");
                card.name = "goobea-item-" + def.Id;
                card.EnableInClassList("gb-goobea-item--sel", def.Id == _selected);
                card.Add(Ux.IconTile(IconFor(def.Kategorie), TintFor(def.Kategorie)));
                var col = Ux.Col();
                col.style.flexGrow = 1;
                col.style.flexShrink = 1;
                var n = Ux.Text(Name(def), "gb-sm", "gb-bold");
                n.style.whiteSpace = WhiteSpace.Normal;
                col.Add(n);
                col.Add(Ux.Text(T("muenzen", ("n", def.Preis)), "gb-xs", "gb-goobea-itemprice"));
                card.Add(col);
                card.RegisterCallback<ClickEvent>(_ => { Sfx.PlayUi("soft_tap"); Select(def.Id); });
                _list.Add(card);
            }
        }

        private void RefreshWallet()
        {
            var state = _host.Store.State;
            _coins.text = Ux.FormatNumber(J.Int(J.Path(state, "economy", "coins")));
            var cap = J.Int(J.Path(state, HomeState.SliceId, "storageCapacity"), 100);
            _lager.text = T("lager", ("frei", MoebelShop.StorageFree(state)), ("gesamt", cap));
            RefreshBuy();
        }

        // ── the piece ──

        private void Select(string id)
        {
            var def = FurnitureCatalog.Def(id);
            if (def == null) return;
            _selected = id;
            _variant = FurnitureVariants.DefaultId;
            _name.text = Name(def);
            _meta.text = T("felder", ("x", def.FootprintW), ("y", def.FootprintH)) + " · " + T("lagerwert", ("n", def.Lagerwert)) + " · " + _host.Strings.T("shop.kategorie." + def.Kategorie);
            foreach (var c in _list.Children()) c.EnableInClassList("gb-goobea-item--sel", c.name == "goobea-item-" + id);
            RefreshSwatches(def);
            RefreshBuy();
            ShowModel(def);
        }

        private void RefreshSwatches(FurnitureDef def)
        {
            _swatches.Clear();
            foreach (var v in FurnitureVariants.IdsFor(def))
            {
                var id = v;
                var (r, g, b) = FurnitureVariants.Palette[id];
                var sw = new Button(() => SetVariant(id)) { name = "goobea-farbe-" + id };
                sw.ClearClassList();
                sw.AddToClassList("gb-goobea-swatch");
                sw.EnableInClassList("gb-goobea-swatch--on", id == _variant);
                sw.style.backgroundColor = id == FurnitureVariants.DefaultId ? new Color(0.96f, 0.93f, 0.87f) : new Color((float)r, (float)g, (float)b);
                if (id == FurnitureVariants.DefaultId)
                {
                    var leaf = Ux.Icon("leaf");
                    leaf.pickingMode = PickingMode.Ignore;
                    sw.Add(leaf);
                }
                _swatches.Add(sw);
            }
            _variantLabel.text = T("variants") + ": " + _host.Strings.T("shop.variant." + _variant);
        }

        private void SetVariant(string id)
        {
            _variant = id;
            Sfx.PlayUi("soft_chip");
            if (_model != null) VariantTint.Apply(_model.gameObject, id);
            var def = FurnitureCatalog.Def(_selected);
            if (def != null) RefreshSwatches(def);
        }

        private void RefreshBuy()
        {
            var def = FurnitureCatalog.Def(_selected);
            if (def == null || _buy == null) return;
            _price.text = T("muenzen", ("n", def.Preis));
            var check = MoebelShop.Check(_host.Store.State, def.Id);
            _buy.EnableInClassList("gb-btn-confirm", check == MoebelShop.Ok);
            _buy.EnableInClassList("gb-btn-secondary", check != MoebelShop.Ok);
            var owned = StorageLogic.CountOf(HomeState.Storage(_host.Store), def.Id);
            _hint.text = check == MoebelShop.Pleite ? T("zu_teuer") : check == MoebelShop.Voll ? T("lager_voll") : owned > 0 ? T("im_lager", ("n", owned)) : "";
        }

        private void Kaufen()
        {
            var def = FurnitureCatalog.Def(_selected);
            if (def == null) return;
            var result = MoebelShop.Unbekannt;
            var variant = _variant;
            _host.Store.Update(s => result = MoebelShop.Kaufe(s, def.Id, variant), GameHost.TicksMs);
            if (result != MoebelShop.Ok)
            {
                Sfx.PlayUi("soft_error");
                _host.Ui.Toast(T(result == MoebelShop.Pleite ? "zu_teuer" : result == MoebelShop.Voll ? "lager_voll" : "fehler"), "close");
                RefreshBuy();
                return;
            }
            _host.Store.NotifySliceChanged(HomeState.SliceId);
            _host.Store.NotifySliceChanged("economy");
            Sfx.PlayUi("soft_buy");
            var from = _show.worldBound.center;
            _host.Ui.FlyIcons(from, _lagerPill, "inventory", 3, _ => _juice.ScalePop(_lagerPill, 1.15f, 0.18f));
            if (_turntable != null) Effects.Sparkle(_turntable.position + Vector3.up * 0.9f, 0.8f, _host.Settings.ReducedMotion ? 5 : 24);
            _host.Ui.Toast(T("gekauft", ("name", Name(def))), "sofa");
            RefreshWallet();
            Debug.Log("[goobea] gekauft " + def.Id + " (" + variant + ") für " + def.Preis);
        }

        private void Quit()
        {
            Sfx.PlayUi("soft_back");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }

        /// <summary>QA: "goobea kat sitzen | suche sofa | waehle loungeSofa | farbe rose | kaufen".</summary>
        public void Qa(string[] raw)
        {
            var parts = string.Join(" ", raw).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var a1 = parts.Length > 1 ? parts[1] : "";
            var a2 = parts.Length > 2 ? parts[2] : "";
            switch (a1)
            {
                case "kat": _kategorie = a2 == "alle" ? "" : a2; RefreshChips(); RefreshList(); break;
                case "suche": _query = a2; Root.Q<TextField>("goobea-suche")?.SetValueWithoutNotify(a2); RefreshList(); break;
                case "waehle": Select(a2); break;
                case "farbe": SetVariant(a2); break;
                case "kaufen": Kaufen(); break;
            }
            Debug.Log("[qa] goobea " + a1 + " " + a2 + " → " + _selected + " (" + _variant + ")");
        }
    }
}
