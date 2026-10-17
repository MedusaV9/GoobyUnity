using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Build mode for the garden (board 05) and the house rooms (board 09). Both share one BuildSession
    /// (storage cards → tap to place, focus a placed item → move/rotate/duplicate/remove, undo, commit) and
    /// differ only in their chrome:
    ///   garden: back + title pill, daily quest, leaves pill, category rail, camera/grid/rotate/undo, pink "Fertig",
    ///           storage sheet with filter chips;
    ///   room:   back + "Bau-Modus" pill, undo/redo + "Speichern", rail Möbel/Deko/Struktur/Teppiche/Sonstiges,
    ///           Raster toggle / Kamera / Löschen cards, Gooby's speech bubble, chips + item cards, "Spielen &amp; Testen".
    /// The focused item gets a green selection frame with rotate/duplicate/remove buttons projected from 3D.
    /// </summary>
    public sealed class BuildModeScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly Dictionary<string, Button> _rail = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _filters = new Dictionary<string, Button>();
        private VisualElement _cards, _filterRow, _frame, _frameButtons, _frameRotate, _gridToggle;
        private Label _lagerLabel, _hint, _questProgress, _bubble;
        private string _filter = "alle";
        private int _page;
        private BuildSession _session;
        private bool _gridOn = true;

        /// <summary>Boards 05/09 show the storage as one row of eight cards with a chevron to page through the rest.</summary>
        private const int CardsPerPage = 8;

        private readonly struct RailEntry
        {
            public readonly string Id, Icon, Key;
            public readonly string[] Cats;
            public RailEntry(string id, string icon, string key, string[] cats) { Id = id; Icon = icon; Key = key; Cats = cats; }
        }

        /// <summary>Garden rail (board 05) → furniture catalog categories.</summary>
        private static readonly RailEntry[] GardenRail =
        {
            new RailEntry("deko", "leaf", "nl.garten.kat_deko", new[] { "deko", "bilder", "girlanden", "party", "unterhaltung" }),
            new RailEntry("pflanzen", "sprout", "nl.garten.kat_pflanzen", new[] { "pflanzen" }),
            new RailEntry("wege", "path", "nl.garten.kat_wege", new[] { "teppiche" }),
            new RailEntry("zaeune", "fence", "nl.garten.kat_zaeune", new[] { "garten" }),
            new RailEntry("moebel", "sofa", "nl.garten.kat_moebel", new[] { "sitzen", "tische", "regale", "schlafen", "kueche", "bad", "buero", "wohnen", "fenster" }),
            new RailEntry("spezial", "star", "nl.garten.kat_spezial", new[] { "lampen" }),
        };

        /// <summary>Chip order of the garden storage bar on board 05 (the rail keeps its own order).</summary>
        private static readonly string[] GardenChipOrder = { "pflanzen", "moebel", "deko", "wege", "zaeune", "spezial" };

        /// <summary>Board 05 "Alle" row: Kleiner Busch, Tulpen rot/weiß, Baum, Bank, Steinweg, Laterne, Kiste — then the rest.</summary>
        private static readonly string[] GardenLeadItems =
        {
            "gardenBush", "flowerRed", "flowerWhite", "treeSapling",
            "gardenBench", "pathStonesShort", "gardenLantern", "crateWood"
        };

        /// <summary>Board 09 "Alle" row: Sofa, Tisch, Pflanze, Teppich, Regal, Lampe, Bett, Kommode — then leftovers.</summary>
        private static readonly string[] RoomLeadItems =
        {
            "loungeSofa", "tableCoffee", "pottedPlant", "rugRound",
            "bookcaseClosedWide", "lampRoundFloor", "bedKaro", "cabinetBed"
        };

        /// <summary>Room rail (board 09).</summary>
        private static readonly RailEntry[] RoomRail =
        {
            new RailEntry("moebel", "sofa", "nl.bau.kat_moebel", new[] { "sitzen", "tische", "schlafen", "regale", "kueche", "bad", "buero", "wohnen" }),
            new RailEntry("deko", "leaf", "nl.bau.kat_deko", new[] { "deko", "bilder", "girlanden", "party", "unterhaltung", "lampen" }),
            new RailEntry("struktur", "house", "nl.bau.kat_struktur", new[] { "fenster" }),
            new RailEntry("teppiche", "rug", "nl.bau.kat_teppiche", new[] { "teppiche" }),
            new RailEntry("sonstiges", "star", "nl.bau.kat_sonstiges", new[] { "pflanzen", "garten" }),
        };

        /// <summary>Room filter chips (board 09): id → catalog categories (empty = all).</summary>
        private static readonly (string id, string key, string[] cats)[] RoomChips =
        {
            ("alle", "nl.bau.chip_alle", new string[0]),
            ("sofas", "nl.bau.chip_sofas", new[] { "sitzen" }),
            ("tische", "nl.bau.chip_tische", new[] { "tische", "wohnen" }),
            ("stuehle", "nl.bau.chip_stuehle", new[] { "sitzen" }),
            ("betten", "nl.bau.chip_betten", new[] { "schlafen" }),
            ("regale", "nl.bau.chip_regale", new[] { "regale", "kueche", "bad", "buero" }),
            ("pflanzen", "nl.bau.chip_pflanzen", new[] { "pflanzen", "garten" }),
            ("teppiche", "nl.bau.chip_teppiche", new[] { "teppiche" }),
            ("deko", "nl.bau.chip_deko", new[] { "deko", "bilder", "girlanden", "party", "unterhaltung", "fenster" }),
            ("lichter", "nl.bau.chip_lichter", new[] { "lampen" }),
        };

        private readonly string _roomId;
        private bool IsGarden => _roomId == "garden";
        private RailEntry[] Rail => IsGarden ? GardenRail : RoomRail;

        private readonly string _preselect;

        /// <param name="preselect">storage item to start with in hand (the inventory sheet's "Aufstellen").</param>
        public BuildModeScreen(GameHost host, string roomId = "garden", string preselect = null)
        {
            _host = host;
            _roomId = roomId;
            _preselect = preselect;
            Id = "build-mode";
            Opaque = true;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.pickingMode = PickingMode.Ignore;
            if (IsGarden) _host.World?.SetBuildCamera(true);
            else _host.World?.SetRoomBuildCamera(true);
            _session = _host.World?.BeginBuild(_host.Store, _roomId);
            if (_session != null) _session.Changed += OnSessionChanged;

            // tap catcher (lowest layer): taps on the floor place / focus / move items
            var catcher = new VisualElement { pickingMode = PickingMode.Position };
            Ux.Abs(catcher, 0, 0, 0, 0);
            catcher.RegisterCallback<PointerUpEvent>(_ => OnWorldTap());
            root.Add(catcher);

            // selection frame for the focused item (projected from world space each frame)
            _frame = Ux.El("gb-select-frame");
            _frame.pickingMode = PickingMode.Ignore;
            _frame.style.display = DisplayStyle.None;
            foreach (var (cls, icon) in new[] { ("gb-select-arrow--n", "arrow_up"), ("gb-select-arrow--s", "arrow_down"), ("gb-select-arrow--w", "arrow_left"), ("gb-select-arrow--e", "arrow_right") })
            {
                var arrow = Ux.El("gb-select-arrow", cls);
                arrow.Add(Ux.Icon(icon));
                _frame.Add(arrow);
            }
            root.Add(_frame);
            _frameButtons = Ux.Col("gb-select-tools");
            _frameButtons.style.display = DisplayStyle.None;
            _frameRotate = Ux.IconBtn("rotate", () => _session?.RotateFocused(), "gb-btn-icon-sm");
            _frameButtons.Add(_frameRotate);
            _frameButtons.Add(Ux.IconBtn("copy", () => { if (_session != null && !_session.DuplicateFocused()) SetHint(t.T("nl.bau.nichts_uebrig")); }, "gb-btn-icon-sm"));
            _frameButtons.Add(Ux.IconBtn("trash", () => _session?.StoreFocused(), "gb-btn-icon-sm", "gb-btn-icon-pink"));
            root.Add(_frameButtons);

            BuildHeader(root);
            BuildRail(root);
            if (IsGarden) BuildGardenTools(root);
            else BuildRoomTools(root);
            BuildSheet(root);

            BuildFilters();
            RefreshRail();
            RefreshFilters();
            RefreshCards();
            RefreshLager();
            // the inventory sheet hands over an item to start with in hand (cards exist now, so the hint can update)
            if (_session != null && !string.IsNullOrEmpty(_preselect) && HomeState.Storage(_host.Store).Any(r => J.Str(r["item"]) == _preselect && J.Int(r["count"]) > 0))
            {
                _session.Select(_preselect);
                var def = FurnitureCatalog.Def(_preselect);
                if (def != null) SetHint(t.T("nl.garten.tippe_zum_platzieren", ("name", def.DisplayName(_host.Settings.Locale))));
                RefreshCards();
            }
        }

        // ── chrome ───────────────────────────────────────────────────────────

        private void BuildHeader(VisualElement root)
        {
            var t = _host.Strings;
            // boards 05/09: a cream back disc, then the dark capsule with the house disc, title and hint
            var back = Ux.IconBtn("back", () => Close(commit: true), "gb-build-back");
            back.name = "build-back";
            Ux.Abs(back, 16, 14);
            root.Add(back);
            var head = Ux.El("gb-build-head", "gb-row");
            Ux.Abs(head, 88, 12);
            var homeTile = Ux.El("gb-build-head-icon");
            homeTile.Add(Ux.Icon("house"));
            head.Add(homeTile);
            var hcol = Ux.Col();
            hcol.style.marginLeft = 12; hcol.style.marginRight = 6;
            var title = IsGarden ? t.T("nl.garten.titel") : t.T("nl.bau.titel");
            hcol.Add(Ux.Text(title, "gb-h2"));
            _hint = Ux.Text(IsGarden ? t.T("nl.garten.untertitel") : t.T("nl.bau.untertitel"), "gb-sm", "gb-mild");
            hcol.Add(_hint);
            head.Add(hcol);
            root.Add(head);

            var tr = Ux.Row();
            Ux.Abs(tr, null, 12, 16);
            if (IsGarden)
            {
                // daily quest card (top centre) — garden dailies first (Gieß-Runde), not memory/care
                var stack = Ux.Col();
                Ux.Abs(stack, null, 8);
                stack.style.left = Length.Percent(50);
                stack.style.translate = new Translate(Length.Percent(-50), 0);
                stack.style.alignItems = Align.Center;
                var quest = Ux.El("gb-card", "gb-row");
                quest.Add(Ux.IconTile("star", new Color(1f, 0.93f, 0.7f)));
                var qcol = Ux.Col();
                qcol.style.marginLeft = 10; qcol.style.marginRight = 14;
                qcol.Add(Ux.Text(t.T("nl.garten.tagesquest"), "gb-sm", "gb-bold", "gb-mild"));
                var gardenRow = DailyQuestService.PickGardenCard(_host.Quests?.Board());
                string qTitle;
                string qProgress;
                if (gardenRow != null)
                {
                    qTitle = t.HasKey(gardenRow.Def.TitleKey) ? t.T(gardenRow.Def.TitleKey) : gardenRow.Def.Id;
                    qProgress = $"{gardenRow.Progress}/{gardenRow.Target}";
                }
                else
                {
                    qTitle = t.T("nl.garten.giess_runde");
                    var round = _host.Quests != null ? _host.Quests.WateringRound() : (progress: 0, target: 4);
                    qProgress = $"{round.progress}/{round.target}";
                }
                qcol.Add(Ux.Text(qTitle, "gb-lg", "gb-bold"));
                quest.Add(qcol);
                _questProgress = Ux.Text(qProgress, "gb-h3");
                quest.Add(_questProgress);
                var chev = Ux.Icon("chevron");
                chev.style.width = 18; chev.style.height = 18; chev.style.marginLeft = 8;
                quest.Add(chev);
                stack.Add(quest);

                // board 05: mint pill under the quest after Gooby's crate-nap unlocks Kartonversteck
                if (StickerRules.IsUnlocked(_host.Store.State, "ev_karton_gooby"))
                {
                    var banner = Ux.El("gb-toast", "gb-toast--sticker");
                    banner.style.marginTop = 8;
                    var tick = Ux.Icon("check");
                    tick.style.unityBackgroundImageTintColor = new Color(0.31f, 0.72f, 0.55f);
                    banner.Add(tick);
                    var sticker = _host.Stickers?.Catalog.FirstOrDefault(d => d.Id == "ev_karton_gooby");
                    var name = sticker != null && !string.IsNullOrEmpty(sticker.NameDe) ? sticker.NameDe : "Kartonversteck";
                    banner.Add(Ux.Text(t.T("nl.garten.sticker_neu", ("name", name)), "gb-bold"));
                    stack.Add(banner);
                }
                root.Add(stack);

                // "Gestalten" outside: the cottage's walls, roof, door and shutters
                var houseStyle = Ux.IconTextBtn("palette", t.T("customize.title"), () => _host.Ui.Push(new CustomizeScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
                houseStyle.name = "build-gestalten";
                houseStyle.style.marginRight = 10;
                tr.Add(houseStyle);
                var leafPill = Ux.Pill("leaf", Ux.FormatNumber(J.Long(J.GetPath(_host.Store.State, "leaves.count"))), out _, () => { });
                leafPill.style.marginRight = 10;
                tr.Add(leafPill);
                if (Debug.isDebugBuild || Application.isEditor) { var dev = Ux.Text("DEV", "gb-dev"); dev.style.marginRight = 10; tr.Add(dev); }
                var clockCard = Ux.El("gb-card", "gb-row");
                clockCard.style.paddingTop = 6; clockCard.style.paddingBottom = 6;
                var now = DateTimeOffset.FromUnixTimeMilliseconds(_host.Store.Clock.NowMs()).ToLocalTime();
                var ccol = Ux.Col();
                ccol.Add(Ux.Text(now.ToString("HH:mm"), "gb-h3"));
                ccol.Add(Ux.Text(now.ToString("ddd, d. MMM", new System.Globalization.CultureInfo("de-DE")).Replace(".,", ","), "gb-sm", "gb-mild"));
                clockCard.Add(ccol);
                var sun = Ux.Icon("sun", "gb-icon-lg");
                sun.style.marginLeft = 10;
                clockCard.Add(sun);
                tr.Add(clockCard);
            }
            else
            {
                // board 09: undo / redo / DEV / Speichern — plus "Gestalten" (walls & floors of this room)
                var style = Ux.IconTextBtn("palette", t.T("customize.title"), () => _host.Ui.Push(new CustomizeScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
                style.name = "build-gestalten";
                style.style.marginRight = 10;
                tr.Add(style);
                // layout presets "Raum speichern" (preset_sheet.gd): three named layouts per room
                var presets = Ux.IconTextBtn("book", t.T("build.preset.knopf"), OpenPresets, "gb-btn-secondary", "gb-btn-sm");
                presets.name = "build-presets";
                presets.style.marginRight = 10;
                tr.Add(presets);
                var undo = Ux.IconBtn("undo", () => { if (_session != null && !_session.Undo()) SetHint(t.T("nl.garten.nichts_rueckgaengig")); }, "gb-btn-icon-sm");
                undo.style.marginRight = 8;
                tr.Add(undo);
                var redo = Ux.IconBtn("redo", () => { }, "gb-btn-icon-sm");
                redo.style.opacity = 0.45f;
                redo.style.marginRight = 10;
                tr.Add(redo);
                if (Debug.isDebugBuild || Application.isEditor) { var dev = Ux.Text("DEV", "gb-dev"); dev.style.marginRight = 10; tr.Add(dev); }
                var save = Ux.IconTextBtn("check", t.T("nl.bau.speichern"), () => Close(commit: true), "gb-btn-primary", "gb-btn-sm");
                save.name = "build-save";
                save.style.minWidth = 150;
                tr.Add(save);

                // Gooby's excited bubble (board 09, centre-left)
                var bubble = Ux.El("gb-bubble");
                Ux.Abs(bubble, 232, 250);
                bubble.style.paddingLeft = 14; bubble.style.paddingRight = 18;
                _bubble = Ux.Text(t.T("nl.bau.gooby_freut"), "gb-lg", "gb-semibold");
                bubble.Add(_bubble);
                var sparkle = Ux.Icon("sparkle");
                sparkle.style.width = 22; sparkle.style.height = 22; sparkle.style.marginLeft = 6;
                bubble.Add(sparkle);
                root.Add(bubble);
            }
            root.Add(tr);
        }

        private void BuildRail(VisualElement root)
        {
            var t = _host.Strings;
            var rail = Ux.Col();
            Ux.Abs(rail, 16, 96);
            foreach (var r in Rail)
            {
                var local = r.Id;
                var b = new Button(() => SetFilter(local)) { text = "" };
                b.ClearClassList();
                b.AddToClassList("gb-rail-tab");
                if (!IsGarden) b.AddToClassList("gb-rail-tab--pink");
                b.Add(Ux.Icon(r.Icon));
                b.Add(Ux.Text(t.T(r.Key)));
                _rail[r.Id] = b;
                rail.Add(b);
            }
            root.Add(rail);
        }

        private void BuildGardenTools(VisualElement root)
        {
            var t = _host.Strings;
            var tools = Ux.Col();
            Ux.Abs(tools, null, 200, 16);
            tools.style.alignItems = Align.FlexEnd;
            var cam = Ux.IconBtn("camera", () => _host.World?.CycleBuildCamera());
            cam.style.marginBottom = 10;
            tools.Add(cam);
            var grid = Ux.IconBtn("grid", ToggleGrid);
            grid.style.marginBottom = 10;
            tools.Add(grid);
            var rotate = Ux.IconBtn("rotate", () => _session?.RotateFocused());
            rotate.style.marginBottom = 10;
            tools.Add(rotate);
            var undo = Ux.IconBtn("undo", () => { if (_session != null && !_session.Undo()) SetHint(t.T("nl.garten.nichts_rueckgaengig")); });
            undo.style.marginBottom = 10;
            tools.Add(undo);
            var presets = Ux.IconBtn("book", OpenPresets);
            presets.name = "build-presets";
            presets.style.marginBottom = 18;
            tools.Add(presets);
            var done = Ux.El("gb-action", "gb-action--pink");
            done.style.width = 96; done.style.height = 96;
            done.Add(Ux.Icon("check"));
            done.Add(Ux.Text(t.T("nl.garten.fertig")));
            done.RegisterCallback<ClickEvent>(_ => Close(commit: true));
            tools.Add(done);
            root.Add(tools);
        }

        private void BuildRoomTools(VisualElement root)
        {
            var t = _host.Strings;
            var tools = Ux.Col();
            Ux.Abs(tools, null, 96, 16);
            tools.style.alignItems = Align.FlexEnd;

            var raster = Ux.El("gb-tool-card");
            raster.Add(Ux.Icon("grid", "gb-icon-lg"));
            raster.Add(Ux.Text(t.T("nl.bau.raster"), "gb-sm", "gb-bold"));
            _gridToggle = Ux.Toggle(_gridOn, on => { _gridOn = on; _host.World?.SetRoomGrid(on); });
            _gridToggle.AddToClassList("gb-toggle-track--pink");
            _gridToggle.style.marginTop = 6;
            _gridToggle.style.scale = new Scale(new Vector3(0.8f, 0.8f, 1f));
            raster.Add(_gridToggle);
            tools.Add(raster);

            var cam = Ux.El("gb-tool-card");
            cam.Add(Ux.Icon("camera", "gb-icon-lg"));
            cam.Add(Ux.Text(t.T("nl.bau.kamera"), "gb-sm", "gb-bold"));
            cam.RegisterCallback<ClickEvent>(_ => _host.World?.CycleRoomCamera());
            tools.Add(cam);

            var del = Ux.El("gb-tool-card", "gb-tool-card--pink");
            del.Add(Ux.Icon("trash", "gb-icon-lg"));
            del.Add(Ux.Text(t.T("nl.bau.loeschen"), "gb-sm", "gb-bold"));
            del.RegisterCallback<ClickEvent>(_ => { if (_session != null && !_session.StoreFocused()) SetHint(t.T("nl.bau.fokus")); });
            tools.Add(del);
            root.Add(tools);

            // "Gitter aus" mini toggle with Gooby's face (board 09, above Spielen & Testen)
            var gitter = Ux.El("gb-action");
            Ux.Abs(gitter, null, null, 40, 176);
            gitter.style.width = 84; gitter.style.height = 84;
            gitter.style.backgroundColor = new Color(1f, 1f, 1f, 0.94f);
            gitter.Add(Ux.Portrait(34));
            var gl = Ux.Text(t.T("nl.bau.gitter_aus"), "gb-sm", "gb-bold");
            gl.style.color = new Color(0.23f, 0.21f, 0.31f);
            gitter.Add(gl);
            var up = Ux.Icon("chevron_up");
            up.style.width = 12; up.style.height = 12;
            gitter.Add(up);
            gitter.RegisterCallback<ClickEvent>(_ => ToggleGrid());
            root.Add(gitter);

            // Spielen & Testen (bottom right, big pink)
            var play = Ux.El("gb-action", "gb-action--pink");
            Ux.Abs(play, null, null, 20, 24);
            play.style.width = 128; play.style.height = 128;
            var playIcon = Ux.Icon("play");
            playIcon.style.width = 40; playIcon.style.height = 40;
            play.Add(playIcon);
            var pl = Ux.Text(t.T("nl.bau.spielen_testen"), "gb-bold");
            pl.style.color = Color.white; pl.style.fontSize = 13; pl.style.whiteSpace = WhiteSpace.Normal; pl.style.unityTextAlign = TextAnchor.MiddleCenter;
            pl.style.width = 100;
            play.Add(pl);
            play.RegisterCallback<ClickEvent>(_ => Close(commit: true));
            root.Add(play);

            // joystick (board 09, bottom left) — decorative here, Gooby waits outside the room while building
            var joy = Ux.El("gb-joy-ring");
            Ux.Abs(joy, 24, null, null, 22);
            joy.style.width = 116; joy.style.height = 116;
            joy.Add(Ux.El("gb-joy-knob"));
            joy.style.display = _host.Settings.ShowControls ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(joy);
        }

        private void BuildSheet(VisualElement root)
        {
            var t = _host.Strings;
            var sheet = Ux.El("gb-sheet");
            sheet.style.left = IsGarden ? 112 : 176;
            sheet.style.right = IsGarden ? 112 : 168;
            var srow = Ux.Row();
            if (IsGarden)
            {
                var lagerIcon = Ux.Icon("inventory");
                lagerIcon.style.marginRight = 6;
                srow.Add(lagerIcon);
                var lagerCol = Ux.Col();
                lagerCol.style.marginRight = 16;
                lagerCol.Add(Ux.Text(t.T("nl.garten.lager"), "gb-sm", "gb-mild"));
                _lagerLabel = Ux.Text("", "gb-md", "gb-bold");
                lagerCol.Add(_lagerLabel);
                srow.Add(lagerCol);
            }
            else
            {
                _lagerLabel = Ux.Text("", "gb-sm", "gb-mild");
                _lagerLabel.style.marginRight = 12;
                srow.Add(_lagerLabel);
                // more furniture: the GOOBEA store right from the storage row (bought pieces land in the storage)
                var shop = Ux.IconTextBtn("sofa", t.T("build.moebel_kaufen"), () => _host.Ui.Push(new GoobeaScreen(_host)), "gb-btn-secondary", "gb-btn-sm");
                shop.name = "bau-goobea";
                shop.style.marginRight = 12;
                srow.Add(shop);
            }
            _filterRow = Ux.Row("gb-grow");
            srow.Add(_filterRow);
            if (IsGarden) srow.Add(Ux.IconBtn("search", () => { }, "gb-btn-icon-sm"));
            sheet.Add(srow);
            _cards = Ux.Row();
            _cards.style.marginTop = 12;
            _cards.style.flexWrap = Wrap.NoWrap;
            _cards.style.alignItems = Align.Center;
            _cards.style.minHeight = 132; // one card row (.gb-item-card) — an empty category must not drop the chips
            sheet.Add(_cards);
            root.Add(sheet);
        }

        private void SetFilter(string id)
        {
            _filter = id;
            _page = 0;
            RefreshRail();
            RefreshFilters();
            RefreshCards();
        }

        private void BuildFilters()
        {
            var t = _host.Strings;
            _filterRow.Clear();
            _filters.Clear();
            var chips = new List<(string id, string key)>();
            if (IsGarden)
            {
                chips.Add(("alle", "nl.garten.filter_alle"));
                // board 05 orders the chips differently from the rail: Pflanzen, Möbel, Deko, Wege, Zäune, Spezial
                foreach (var id in GardenChipOrder) chips.Add(Rail.Where(r => r.Id == id).Select(r => (r.Id, r.Key)).First());
            }
            else chips.AddRange(RoomChips.Select(c => (c.id, c.key)));
            foreach (var (id, key) in chips)
            {
                var local = id;
                var chip = new Button(() => SetFilter(local)) { text = t.T(key) };
                chip.ClearClassList();
                chip.AddToClassList("gb-chip");
                if (!IsGarden) chip.AddToClassList("gb-chip--pinkable");
                _filters[id] = chip;
                _filterRow.Add(chip);
            }
        }

        // ── behaviour ────────────────────────────────────────────────────────

        private void ToggleGrid()
        {
            _gridOn = !_gridOn;
            if (IsGarden) _host.World?.ToggleGrid();
            else _host.World?.SetRoomGrid(_gridOn);
        }

        private void OnWorldTap()
        {
            if (_session == null || _host.World?.Camera == null) return;
            var ray = _host.World.Camera.ScreenPointToRay(Input.mousePosition);
            _session.TapAt(ray);
        }

        private void OnSessionChanged()
        {
            var t = _host.Strings;
            switch (_session.LastMessage)
            {
                case "placed": SetHint(t.T("nl.garten.platziert")); break;
                case "stored": SetHint(t.T(IsGarden ? "nl.garten.eingelagert" : "nl.bau.geloescht")); break;
                case "undone": SetHint(t.T("nl.garten.rueckgaengig")); break;
                case "storage_full": SetHint(t.T("nl.garten.lager_voll")); break;
                case "storage_empty": SetHint(t.T("nl.bau.nichts_uebrig")); break;
                case "mandatory": SetHint(t.T("nl.garten.pflichtmoebel")); break;
                case "focused": SetHint(t.T("nl.bau.fokus")); break;
                case "moved": SetHint(t.T("nl.bau.verschoben")); break;
                case "rotated": SetHint(t.T("nl.bau.gedreht")); break;
                case "wall": SetHint(t.T("nl.bau.an_die_wand")); break;
                case "wall_flat": SetHint(t.T("nl.bau.haengt_flach")); break;
                case GridData.ReasonWindow: SetHint(t.T("nl.bau.fenster")); break;
                case GridData.ReasonOccupied: SetHint(t.T("nl.garten.belegt")); break;
                case GridData.ReasonBlocked: SetHint(t.T("nl.garten.tuerzone")); break;
                case GridData.ReasonOob: case "oob": SetHint(t.T("nl.garten.ausserhalb")); break;
                case GridData.ReasonNeedsSurface: SetHint(t.T("nl.garten.braucht_tisch")); break;
                case "empty": SetHint(IsGarden ? t.T("nl.garten.untertitel") : t.T("nl.bau.untertitel")); break;
                case "girlande_b": SetHint(t.T("build.girlande.punkt_b")); break;
                case "girlande_haengt": SetHint(t.T("build.girlande.haengt")); Sfx.PlayUi("soft_open"); break;
                case "girlande_ungueltig": SetHint(t.T("build.girlande.ungueltig")); break;
                case "girlande_entfernt": SetHint(t.T("build.girlande.entfernt")); break;
                case "girlande_drinnen": SetHint(t.T("build.girlande.nur_drinnen")); break;
            }
            RefreshCards();
            RefreshLager();
            if (!IsGarden)
                _host.World?.SetRoomWallView(FurnitureCatalog.Def(_session.SelectedItem)?.Layer == GridLayer.Wall || _session.Focused?.Wall != null);
            UpdateFrame();
        }

        private void SetHint(string text) => _hint.text = text;

        private void RefreshLager()
        {
            var used = HomeState.StoragePointsUsed(_host.Store);
            var cap = HomeState.StorageCapacity(_host.Store);
            _lagerLabel.text = _host.Strings.T("build.lager_kurz", ("used", used), ("cap", cap));
        }

        private void RefreshRail()
        {
            foreach (var kv in _rail) kv.Value.EnableInClassList("gb-rail-tab--active", kv.Key == _filter);
        }

        private void RefreshFilters()
        {
            foreach (var kv in _filters) kv.Value.EnableInClassList("gb-chip--active", kv.Key == _filter);
        }

        private string RailFor(string kategorie)
        {
            foreach (var r in Rail) if (Array.IndexOf(r.Cats, kategorie) >= 0) return r.Id;
            return Rail[0].Id;
        }

        private string IconFor(string railId)
        {
            foreach (var r in Rail) if (r.Id == railId) return r.Icon;
            return "sparkle";
        }

        /// <summary>Catalog categories that read as lawn gear rather than living-room furniture.</summary>
        private static bool Outdoor(FurnitureDef def) => def.Kategorie == "garten" || def.Kategorie == "pflanzen";

        private bool PassesFilter(FurnitureDef def)
        {
            if (_filter == "alle") return true;
            if (IsGarden) return RailFor(def.Kategorie) == _filter;
            foreach (var r in RoomRail) if (r.Id == _filter) return Array.IndexOf(r.Cats, def.Kategorie) >= 0;
            foreach (var c in RoomChips)
            {
                if (c.id != _filter) continue;
                if (c.cats.Length > 0 && Array.IndexOf(c.cats, def.Kategorie) < 0) return false;
                // board 09 splits seating: sofas and armchairs take several cells, chairs and stools one
                var upholstered = def.FootprintW * def.FootprintH > 1;
                if (c.id == "sofas") return upholstered;
                if (c.id == "stuehle") return !upholstered;
                return true;
            }
            return true;
        }

        private void RefreshCards()
        {
            _cards.Clear();
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            // board 05/09: the storage bar leads with what belongs on this stage (lawn: plants, paths, garden gear)
            var storage = HomeState.Storage(_host.Store)
                .Select(row => (row, def: FurnitureCatalog.Def(J.Str(row["item"]))))
                .Where(r => r.def != null && J.Int(r.row["count"]) > 0 && PassesFilter(r.def))
                .OrderBy(r =>
                {
                    var leadList = IsGarden ? GardenLeadItems : RoomLeadItems;
                    var lead = Array.IndexOf(leadList, r.def.Id);
                    if (lead >= 0) return lead;
                    return leadList.Length + (Outdoor(r.def) == IsGarden ? 0 : 1);
                })
                .ToList();
            // one row per page; a card that ran out (last copy placed) may leave the current page empty → step back
            var pages = Math.Max(1, (storage.Count + CardsPerPage - 1) / CardsPerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            if (_page > 0) _cards.Add(Pager(-1));
            foreach (var (row, def) in storage.Skip(_page * CardsPerPage).Take(CardsPerPage))
            {
                var id = J.Str(row["item"]);
                var count = J.Int(row["count"]);
                var card = Ux.El("gb-item-card");
                if (!IsGarden) card.AddToClassList("gb-item-card--pinkable");
                var selected = _session != null && _session.SelectedItem == id;
                if (selected) card.AddToClassList("gb-item-card--selected");
                card.Add(ItemThumb(id, RailFor(def.Kategorie)));
                card.Add(Ux.Text(CardName(def, id, locale), "gb-nowrap"));
                card.Add(Ux.Badge(count.ToString()));
                if (selected)
                {
                    var check = Ux.El("gb-item-check");
                    check.Add(Ux.Icon("check"));
                    card.Add(check);
                }
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    _session?.Select(selected ? null : id);
                    var garland = !selected && Girlanden.Ist(def);
                    SetHint(selected ? (IsGarden ? t.T("nl.garten.untertitel") : t.T("nl.bau.untertitel"))
                        : garland ? t.T(IsGarden ? "build.girlande.nur_drinnen" : "build.girlande.punkt_a")
                        : t.T("nl.garten.tippe_zum_platzieren", ("name", def.DisplayName(locale))));
                    RefreshCards();
                    UpdateFrame();
                });
                _cards.Add(card);
            }
            if ((_page + 1) * CardsPerPage < storage.Count) _cards.Add(Pager(+1));
            if (storage.Count == 0) _cards.Add(Ux.Text(t.T("nl.garten.lager_leer"), "gb-md", "gb-mild"));
        }

        /// <summary>Round chevron at the row's end (board 05); the back chevron is the same icon mirrored.</summary>
        private Button Pager(int dir)
        {
            var b = Ux.IconBtn("chevron", () => { _page += dir; RefreshCards(); }, "gb-btn-icon-sm");
            b.style.flexShrink = 0;
            b.style.marginRight = dir < 0 ? 12 : 0;
            if (dir < 0) b[0].style.scale = new Scale(new Vector3(-1f, 1f, 1f));
            return b;
        }

        private VisualElement ItemThumb(string itemId, string railId) => Ux.ItemThumb(itemId, IconFor(railId));

        /// <summary>Boards 05/09 name the starter kit in the storage bar (catalog titles stay for the shop).</summary>
        private string CardName(FurnitureDef def, string id, string locale)
        {
            if (IsGarden)
            {
                switch (id)
                {
                    case "gardenBush": return locale == "en" ? "Small Bush" : "Kleiner Busch";
                    case "flowerRed": return locale == "en" ? "Tulips (Red)" : "Tulpen (Rot)";
                    case "flowerWhite": return locale == "en" ? "Tulips (White)" : "Tulpen (Weiß)";
                    case "flowerYellow": return locale == "en" ? "Tulips (Yellow)" : "Tulpen (Gelb)";
                    case "treeSapling": return locale == "en" ? "Tree (Small)" : "Baum (Klein)";
                    case "gardenBench": return locale == "en" ? "Wooden Bench" : "Holzbank";
                    case "pathStonesShort": return locale == "en" ? "Stepping Stones" : "Steinweg";
                    case "gardenLantern": return locale == "en" ? "Lantern" : "Laterne";
                    case "crateWood": return locale == "en" ? "Wooden Crate" : "Holzkiste";
                    default: return def.DisplayName(locale);
                }
            }
            switch (id)
            {
                case "loungeSofa": return locale == "en" ? "Sweet Sofa" : "Süßes Sofa";
                case "tableCoffee": return locale == "en" ? "Wooden Table" : "Holztisch";
                case "pottedPlant": return locale == "en" ? "Houseplant" : "Zimmerpflanze";
                case "rugRound": return locale == "en" ? "Flower Rug" : "Blumen-Teppich";
                case "bookcaseClosedWide": return locale == "en" ? "Bookshelf" : "Bücherregal";
                case "lampRoundFloor": return locale == "en" ? "Floor Lamp" : "Stehlampe";
                case "bedKaro": return locale == "en" ? "Single Bed" : "Einzelbett";
                case "bedSingle": return locale == "en" ? "Cozy Bed" : "Kuschelbett";
                case "cabinetBed": return locale == "en" ? "Nightstand" : "Kommode";
                default: return def.DisplayName(locale);
            }
        }

        // ── selection frame ─────────────────────────────────────────────────

        public override void Update() => UpdateFrame();

        /// <summary>Back from the GOOBEA store: the storage cards show what was just bought.</summary>
        public override void OnShown()
        {
            if (_cards == null) return;
            RefreshCards();
            RefreshLager();
        }

        /// <summary>Projects the focused item's footprint into panel space: frame around it, tool buttons to its right.</summary>
        private void UpdateFrame()
        {
            var item = _session?.Focused;
            var cam = _host.World?.Camera;
            if (item == null || cam == null || _frame == null)
            {
                if (_frame != null) _frame.style.display = DisplayStyle.None;
                if (_frameButtons != null) _frameButtons.style.display = DisplayStyle.None;
                return;
            }
            var (w, h) = GridData.RotatedFootprint(item.Def.FootprintW, item.Def.FootprintH, item.Rot);
            var center = _session.CellCenter(item.At, item.Def.FootprintW, item.Def.FootprintH, item.Rot);
            var hx = w * (float)GridData.CellSize * 0.5f;
            var hz = h * (float)GridData.CellSize * 0.5f;
            var top = PropPlaceholder.HeightFor(item.Def) + (item.Def.Layer == GridLayer.Surface ? 0.45f : 0f);
            var bottom = 0f;
            // pictures hang on a wall and lamps from the ceiling: frame what is actually there
            var view = _session.FocusedView;
            var renderers = view != null && (item.Wall != null || item.Def.Layer == GridLayer.Ceiling) ? view.GetComponentsInChildren<Renderer>() : null;
            if (renderers != null && renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                center = new Vector3(b.center.x, 0f, b.center.z);
                hx = b.extents.x;
                hz = b.extents.z;
                bottom = b.min.y;
                top = b.max.y;
            }
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var dx in new[] { -hx, hx })
                foreach (var dz in new[] { -hz, hz })
                    foreach (var dy in new[] { bottom, top })
                    {
                        var p = ToPanel(cam, center + new Vector3(dx, dy, dz));
                        min = Vector2.Min(min, p);
                        max = Vector2.Max(max, p);
                    }
            const float pad = 10f;
            _frame.style.display = DisplayStyle.Flex;
            _frame.style.left = min.x - pad;
            _frame.style.top = min.y - pad;
            _frame.style.width = max.x - min.x + 2 * pad;
            _frame.style.height = max.y - min.y + 2 * pad;
            _frameButtons.style.display = DisplayStyle.Flex;
            _frameButtons.style.left = max.x + pad + 10;
            _frameButtons.style.top = min.y - pad;
            // a picture hangs flat on its wall: nothing to turn
            _frameRotate.style.display = item.Wall != null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement PanelRoot => _host.Ui.SafeArea;

        private Vector2 ToPanel(Camera cam, Vector3 world)
        {
            var sp = cam.WorldToScreenPoint(world);
            var panel = RuntimePanelUtils.ScreenToPanel(PanelRoot.panel, new Vector2(sp.x, Screen.height - sp.y));
            return PanelRoot.WorldToLocal(panel);
        }

        private void Close(bool commit)
        {
            EndSession(commit);
            _host.Ui.Pop();
        }

        /// <summary>The layout presets of this room; an applied one reloads the session from the save.</summary>
        public void OpenPresets()
        {
            Sfx.PlayUi("soft_open");
            _host.Ui.Push(new PresetSheetScreen(_host, _roomId, ReloadSession));
        }

        /// <summary>The save changed under the session (a preset swapped the layout): commit, then build the room anew.</summary>
        private void ReloadSession()
        {
            if (_session != null) _session.Changed -= OnSessionChanged;
            _host.World?.EndBuild(commit: true);
            _session = _host.World?.BeginBuild(_host.Store, _roomId);
            if (_session != null) _session.Changed += OnSessionChanged;
            _host.World?.InvalidateWalkGrid();
            RefreshCards();
            RefreshLager();
            UpdateFrame();
        }

        private void EndSession(bool commit)
        {
            if (_session != null) _session.Changed -= OnSessionChanged;
            _host.World?.EndBuild(commit);
            _session = null;
            if (IsGarden) _host.World?.SetBuildCamera(false);
            else _host.World?.SetRoomBuildCamera(false);
        }

        public override void OnHidden()
        {
            // popped from outside (a visit starts or ends, PopToRoot): the session must not outlive its screen —
            // placed pieces stay (storage and grid were saved together on every step)
            if (_session != null && !_host.Ui.IsOpen(this)) EndSession(commit: true);
        }
    }
}
