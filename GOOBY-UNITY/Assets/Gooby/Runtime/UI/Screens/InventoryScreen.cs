using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Inventar" (HUD nav): the furniture in storage with thumbnails, counts and filter chips, plus the fridge
    /// count. Tapping an item hands it over to build mode of the room Gooby is in ("Aufstellen"), so the sheet
    /// is the way from the shed to the floor.
    /// </summary>
    public sealed class InventoryScreen : UiScreen
    {
        private readonly GameHost _host;
        private VisualElement _cards;
        private Label _title;
        private string _filter = "alle";

        private static readonly (string id, string key, string[] cats)[] Chips =
        {
            ("alle", "nl.bau.chip_alle", new string[0]),
            ("sofas", "nl.bau.chip_sofas", new[] { "sitzen" }),
            ("tische", "nl.bau.chip_tische", new[] { "tische", "wohnen" }),
            ("betten", "nl.bau.chip_betten", new[] { "schlafen" }),
            ("regale", "nl.bau.chip_regale", new[] { "regale", "kueche", "bad", "buero" }),
            ("pflanzen", "nl.bau.chip_pflanzen", new[] { "pflanzen", "garten" }),
            ("teppiche", "nl.bau.chip_teppiche", new[] { "teppiche" }),
            ("deko", "nl.bau.chip_deko", new[] { "deko", "bilder", "girlanden", "party", "unterhaltung", "fenster" }),
            ("lichter", "nl.bau.chip_lichter", new[] { "lampen" }),
            ("andenken", "nl.inventar.chip_andenken", new string[0]),
        };

        public InventoryScreen(GameHost host)
        {
            _host = host;
            Id = "inventory";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);

            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.style.left = 120; sheet.style.right = 120;
            sheet.style.maxHeight = Length.Percent(78);
            var head = CounterPanel.Head("inventory", new Color(1f, 0.93f, 0.75f), "", t.T("nl.inventar.untertitel"), () => _host.Ui.Pop());
            _title = head.Title;
            // Goobay: sell what is in storage to the neighbours
            var sell = Ux.IconTextBtn("gift", t.T("nl.goobay.verkaufen"), () => { _host.Ui.Pop(); _host.Ui.Push(new GoobayScreen(_host)); }, "gb-btn-secondary", "gb-btn-sm");
            sell.name = "inventory-goobay";
            sell.style.marginRight = 10;
            head.Row.Insert(head.Row.childCount - 1, sell);
            sheet.Add(head.Row);

            var body = Ux.Col("gb-win-body");
            var chips = Ux.Row("gb-win-tabs");
            foreach (var (id, key, _) in Chips)
            {
                var chipId = id;
                var chip = Ux.Btn(t.T(key), () => { _filter = chipId; RefreshChips(chips); RefreshCards(); }, "gb-chip");
                chip.name = "chip-" + id;
                chips.Add(chip);
            }
            body.Add(chips);
            RefreshChips(chips);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _cards = Ux.El("gb-win-grid");
            scroll.Add(_cards);
            body.Add(scroll);

            // fridge line: snacks live in the care sheet
            var foot = Ux.Row("gb-win-foot");
            var snacks = FoodCatalog.InventoryEntries(_host.Store.State).Sum(e => e.Count);
            foot.Add(Ux.Icon("carrot"));
            foot.Add(Ux.Text(t.T("nl.inventar.kuehlschrank", ("anzahl", snacks))));
            body.Add(foot);
            sheet.Add(body);
            root.Add(sheet);
            RefreshCards();
        }

        private void RefreshChips(VisualElement chips)
        {
            foreach (var (id, _, _) in Chips) chips.Q("chip-" + id)?.EnableInClassList("gb-chip--active", id == _filter);
        }

        private bool Passes(FurnitureDef def)
        {
            foreach (var (id, _, cats) in Chips) if (id == _filter) return cats.Length == 0 || Array.IndexOf(cats, def.Kategorie) >= 0;
            return true;
        }

        private void RefreshCards()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            var gs = _host.Store;
            _title.text = t.T("nl.inventar.titel", ("used", HomeState.StoragePointsUsed(gs)), ("cap", HomeState.StorageCapacity(gs)));
            _cards.Clear();
            if (_filter == "andenken") { RefreshKeepsakes(); return; }
            var shown = 0;
            var stored = 0;
            foreach (var row in HomeState.Storage(gs))
            {
                var id = J.Str(row["item"]);
                var count = J.Int(row["count"]);
                var def = FurnitureCatalog.Def(id);
                if (def == null || count <= 0) continue;
                stored++;
                if (!Passes(def)) continue;
                // white tile with the prop render on a warm thumb, count tag, "Aufstellen" pill (the whole card is the button)
                var card = CounterPanel.Ware(Ux.ItemThumb(id, "sofa"), new Color(1f, 0.95f, 0.88f), def.DisplayName(locale), null,
                    t.T("fuettern.vorrat_badge", ("anzahl", count)), "gb-ware--sm", "gb-ware--tap");
                card.name = "store-" + id;
                card.Add(CounterPanel.Go(t.T("nl.inventar.aufstellen")));
                card.RegisterCallback<ClickEvent>(_ => PlaceFromStorage(id));
                _cards.Add(card);
                shown++;
            }
            if (shown == 0)
                _cards.Add(stored == 0
                    ? CounterPanel.Empty(t.T("nl.inventar.leer"), t.T("nl.inventar.leer_hint"), row: true)
                    : CounterPanel.Empty(t.T("nl.inventar.leer_filter"), t.T("nl.inventar.untertitel"), row: true));
        }

        /// <summary>Keepsakes from the holidays: the day's finds at the souvenir spots and the souvenirs Gooby brought home.</summary>
        private void RefreshKeepsakes()
        {
            var t = _host.Strings;
            var list = Urlaub.Sammlung(_host.Store.State);
            foreach (var a in list)
            {
                var sub = t.T(a.Typ == "fund" ? "nl.inventar.andenken_fund" : "nl.inventar.andenken_mitbringsel", ("ziel", t.T("travel.ziel." + a.ZielId)));
                var card = CounterPanel.Ware(HomecomingScreen.SouvenirIcon(a.Typ), a.Typ == "fund" ? new Color(0.88f, 0.95f, 1f) : new Color(1f, 0.92f, 0.95f), a.Typ == "fund" ? t.T("urlaub.souvenir." + a.ZielId) : t.T("nl.inventar.andenken_typ." + a.Typ), sub,
                    a.Anzahl > 1 ? t.T("fuettern.vorrat_badge", ("anzahl", a.Anzahl)) : null);
                card.name = "keepsake-" + a.ItemId;
                _cards.Add(card);
            }
            if (list.Count == 0) _cards.Add(CounterPanel.Empty(t.T("nl.inventar.andenken_leer"), t.T("nl.reise.hinweis"), row: true));
        }

        /// <summary>Hands the item to build mode of the room Gooby stands in (or the lawn) — replaces this sheet.</summary>
        private void PlaceFromStorage(string itemId)
        {
            var roomId = _host.World?.RoomId ?? "living";
            _host.Ui.Pop();
            _host.Ui.Push(new BuildModeScreen(_host, roomId, preselect: itemId));
        }
    }
}
