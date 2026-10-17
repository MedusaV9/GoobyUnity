using System.Linq;
using Gooby.Core.Cosmetics;
using Gooby.Core.Json;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Outfits" (board 08 action bar) — the Godot wardrobe: five categories, every catalog item as a card with a
    /// picture of the piece (<see cref="Gooby.Unity.World.CosmeticPreview"/>) and its price; owned pieces toggle on/off,
    /// others are bought with coins (level gate).
    /// Each change runs through CosmeticsState inside the store, and the avatar behind the sheet dresses live.
    /// </summary>
    public sealed class OutfitScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _cards, _chips;
        private Label _hint, _coins;
        private string _kategorie = "hut";

        public OutfitScreen(GameHost host)
        {
            _host = host;
            Id = "outfits";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            // the sheet keeps the left half free so Gooby stays visible while trying things on
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 560; sheet.style.right = 24;
            sheet.style.top = 90;
            sheet.style.borderBottomLeftRadius = 0; sheet.style.borderBottomRightRadius = 0;
            var head = CounterPanel.Head("shirt", new Color(0.85f, 0.93f, 1f), t.T("wardrobe.titel"), t.T("nl.outfits.untertitel"), () => _host.Ui.Pop(),
                J.Int(J.Dict(_host.Store.State["economy"])["coins"]));
            _hint = head.Sub;
            _coins = head.Coins;
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");

            _chips = Ux.Row("gb-win-tabs");
            foreach (var kat in CosmeticsCatalog.Kategorien)
            {
                var id = kat;
                var chip = Ux.Btn(t.T("wardrobe.kat." + kat), () => { _kategorie = id; RefreshChips(); RefreshCards(); }, "gb-chip");
                chip.name = "chip-" + kat;
                _chips.Add(chip);
            }
            body.Add(_chips);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _cards = Ux.El("gb-win-grid");
            scroll.Add(_cards);
            body.Add(scroll);
            sheet.Add(body);
            root.Add(sheet);
            RefreshChips();
            RefreshCards();
            _host.World?.SetWardrobeCamera(true);
        }

        public override void OnShown() => _host.World?.SetWardrobeCamera(true);

        public override void OnHidden() => _host.World?.SetWardrobeCamera(false);

        /// <summary>Cards whose picture is still being baked: swapped in as soon as it is there.</summary>
        private readonly System.Collections.Generic.Dictionary<string, VisualElement> _pending = new System.Collections.Generic.Dictionary<string, VisualElement>();

        public override void Update()
        {
            if (_pending.Count == 0) return;
            foreach (var id in _pending.Keys.ToList())
            {
                var tex = Gooby.Unity.World.CosmeticPreview.Get(id);
                if (tex == null) continue;
                var pic = _pending[id];
                pic.Clear();
                pic.style.backgroundImage = new StyleBackground(tex);
                _pending.Remove(id);
            }
        }

        private void RefreshChips()
        {
            foreach (var kat in CosmeticsCatalog.Kategorien) _chips.Q("chip-" + kat)?.EnableInClassList("gb-chip--active", kat == _kategorie);
        }

        private JObject Slice => J.Dict(_host.Store.State["cosmetics"]);

        private void RefreshCards()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            var slice = Slice;
            var level = _host.Store.GetValue("progression.level", 1);
            var coins = J.Int(J.Dict(_host.Store.State["economy"])["coins"]);
            _cards.Clear();
            if (_coins != null) _coins.text = Ux.FormatNumber(coins);
            var equipped = CosmeticsState.Equipped(slice, _kategorie);
            _pending.Clear();
            foreach (var def in CosmeticsCatalog.ByKategorie(_kategorie).OrderBy(d => d.Standard ? 0 : 1).ThenBy(d => d.Preis).ThenBy(d => d.Id, System.StringComparer.Ordinal))
            {
                var owned = CosmeticsState.IsOwned(slice, def.Id);
                var worn = equipped == def.Id;
                // tile: the piece itself on a pastel of its first colour (its colour dots until the picture is baked), name, then price / state
                var pic = Ux.El("gb-outfit-pic");
                pic.style.position = Position.Absolute;
                pic.style.left = 4; pic.style.right = 4; pic.style.top = 2; pic.style.bottom = 2;
                pic.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                pic.style.alignItems = Align.Center;
                pic.style.justifyContent = Justify.Center;
                var tex = Gooby.Unity.World.CosmeticPreview.Get(def.Id);
                if (tex != null) pic.style.backgroundImage = new StyleBackground(tex);
                else
                {
                    var swatches = Ux.El("gb-ware-swatches");
                    foreach (var hex in def.Farben.Take(3))
                    {
                        var sw = Ux.El("gb-swatch");
                        sw.style.backgroundColor = Gooby.Unity.World.ArtLibrary.Hex(hex);
                        swatches.Add(sw);
                    }
                    pic.Add(swatches);
                    _pending[def.Id] = pic;
                }
                var tint = def.Farben.Count > 0 ? CounterPanel.Pastel(Gooby.Unity.World.ArtLibrary.Hex(def.Farben[0]), 0.72f) : new Color(0.94f, 0.9f, 0.85f);
                var card = CounterPanel.Ware(pic, tint, def.Name(locale), null, null, "gb-ware--sm", "gb-ware--tap");
                card.name = "outfit-" + def.Id;
                if (worn) card.AddToClassList("gb-ware--selected");
                if (worn) card.Add(CounterPanel.Status(t.T("wardrobe.angelegt"), "gb-ware-status--on"));
                else if (owned) card.Add(CounterPanel.Status(t.T("wardrobe.besessen")));
                else if (level < def.MinLevel) card.Add(CounterPanel.Status(t.T("wardrobe.level", ("n", def.MinLevel)), "gb-ware-status--warn"));
                else card.Add(CounterPanel.Price(def.Preis));
                if (!owned && level >= def.MinLevel && coins < def.Preis) card.AddToClassList("gb-outfit-card--locked");
                if (!owned && level < def.MinLevel) card.AddToClassList("gb-outfit-card--locked");
                if (worn) { var check = Ux.El("gb-item-check"); check.Add(Ux.Icon("check")); card.Add(check); }
                var id = def.Id;
                card.RegisterCallback<ClickEvent>(_ => Tap(id));
                _cards.Add(card);
            }
        }

        /// <summary>Owned → toggle worn/unworn; not owned → buy (level + coins) and wear right away.</summary>
        private void Tap(string id)
        {
            var t = _host.Strings;
            var def = CosmeticsCatalog.ById(id);
            if (def == null) return;
            var level = _host.Store.GetValue("progression.level", 1);
            CosmeticResult result;
            if (CosmeticsState.IsOwned(Slice, id))
            {
                result = CosmeticsState.ApplyToState(_host.Store, (slice, _) => CosmeticsState.Toggle(slice, id));
                _hint.text = result.Ok && CosmeticsState.Equipped(Slice, def.Kategorie) == id
                    ? t.T("nl.outfits.angelegt", ("name", def.Name(_host.Settings.Locale)))
                    : t.T("nl.outfits.abgelegt", ("name", def.Name(_host.Settings.Locale)));
            }
            else
            {
                result = CosmeticsState.ApplyToState(_host.Store, (slice, econ) =>
                {
                    var buy = CosmeticsState.Buy(slice, econ, id, level);
                    if (buy.Ok) CosmeticsState.Equip(slice, id);
                    return buy;
                });
                _hint.text = result.Ok ? t.T("nl.outfits.gekauft", ("name", def.Name(_host.Settings.Locale)), ("preis", def.Preis))
                    : result.Grund == "zu_teuer" ? t.T("nl.outfits.zu_teuer", ("preis", def.Preis))
                    : result.Grund == "level_zu_niedrig" ? t.T("wardrobe.level", ("n", def.MinLevel))
                    : t.T("nl.outfits.untertitel");
                if (result.Ok) _host.Store.NotifySliceChanged("achievements");
            }
            if (result.Ok) _host.World?.Bounce();
            RefreshCards();
        }
    }
}
