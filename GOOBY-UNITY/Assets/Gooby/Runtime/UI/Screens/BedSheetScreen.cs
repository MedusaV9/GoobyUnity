using System;
using System.Linq;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// One cell of the bed field (tapped on the lawn). Empty → pick a crop to plant (seed crops show their seed
    /// count) or build a structure here for coins (greenhouse, sprinkler, tree, shed) and expand the field;
    /// planted → growth stage, water it (counts the "Gieß-Runde" daily quest through GardenState.Giessen) or
    /// harvest when ripe; a structure → what it does, wood from a ripe tree, shed upgrade. Every action goes
    /// through GardenState / GardenWorld so the 3D field redraws from the save.
    /// </summary>
    public sealed class BedSheetScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly Cell _bed;
        private readonly Action<string> _say;
        private VisualElement _frame, _sheet; // the bottom sheet and its body column (rebuilt from the save)

        /// <summary>What the sheet offers to build (GardenWorld.Kaufbar).</summary>
        public static readonly string[] Bauwerke = { "werkstatt", "gewaechshaus", "sprinkler", "baum" };

        private static string BuildIcon(string kind) => kind == "baum" ? "leaf" : kind == "sprinkler" ? "rain" : kind == "werkstatt" ? "toolbox" : "house";

        public BedSheetScreen(GameHost host, Cell bed, Action<string> say)
        {
            _host = host;
            _bed = bed;
            _say = say;
            Id = "bed";
        }

        private double NowS => _host.Store.Clock.NowMs() / 1000.0;

        protected override void Build(VisualElement root)
        {
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => _host.Ui.Pop());
            root.Add(scrim);
            root.AddToClassList("gb-sheets");
            _frame = Ux.El("gb-sheet", "gb-win-sheet");
            _frame.style.left = 260; _frame.style.right = 260;
            _frame.style.maxHeight = Length.Percent(90);
            root.Add(_frame);
            Rebuild();
        }

        private void Rebuild()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            _frame.Clear();
            var grid = GardenState.Grid(_host.Store);
            var data = grid.Cell(_bed);
            var (stage, stages, watered, ripe) = GardenField.Status(data, NowS);
            var crop = GardenCrops.Crop(J.Str(data["crop"]));
            var structure = grid.StructureAt(_bed);
            var inGreenhouse = structure != null && structure.Kind == "gewaechshaus" && grid.GreenhouseCells().Contains(_bed);
            var solid = structure != null && !inGreenhouse; // greenhouse beds behave like beds; every other structure shows its own card

            var title = solid ? StructureName(structure.Kind) : crop != null ? crop.DisplayName(locale) : t.T("nl.beet.leer_titel");
            _frame.Add(CounterPanel.Head(solid ? "hammer" : "sprout", solid ? new Color(1f, 0.93f, 0.8f) : new Color(0.87f, 0.96f, 0.9f), title,
                t.T("nl.beet.position", ("x", _bed.X + 1), ("y", _bed.Y + 1)), () => _host.Ui.Pop(), J.Int(J.Path(_host.Store.State, "economy", "coins"))).Row);
            // the body scrolls when the plant picker and the build cards together outgrow the screen
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _sheet = Ux.Col("gb-win-body");
            scroll.Add(_sheet);
            _frame.Add(scroll);

            if (solid) { BuildStructureCard(t, structure); return; }
            if (inGreenhouse)
            {
                var note = Ux.Row("gb-win-note");
                note.Add(Ux.IconTile("house"));
                note.Add(Ux.Text(t.T("nl.beet.im_gewaechshaus"), "gb-win-note-text"));
                _sheet.Add(note);
            }
            if (crop == null)
            {
                BuildPlantPicker(t, locale, inGreenhouse);
                if (!inGreenhouse) BuildBuildSection(t);
                return;
            }

            // growth: bar + stage in a white card, water state as a note
            var status = Ux.Row("gb-bed-grow");
            status.Add(Ux.IconTile(ripe ? "carrot" : "sprout", new Color(0.87f, 0.96f, 0.9f)));
            status.Add(Ux.Bar(stages > 0 ? stage / (float)stages : 0f, out _));
            var stageText = Ux.Text(ripe ? t.T("nl.beet.reif") : t.T("nl.beet.stufe", ("stufe", stage), ("stufen", stages)), "gb-bed-stage");
            if (ripe) stageText.AddToClassList("gb-bed-stage--ripe");
            status.Add(stageText);
            _sheet.Add(status);
            var sprinkled = grid.SprinklerCells().Contains(_bed);
            var water = Ux.Row("gb-win-note");
            if (!watered && !sprinkled) water.AddToClassList("gb-win-note--sand");
            water.Add(Ux.IconTile(watered || sprinkled ? "rain" : "sun"));
            water.Add(Ux.Text(sprinkled ? t.T("nl.beet.sprinkler_nass") : watered ? t.T("nl.beet.nass") : t.T("nl.beet.trocken"), "gb-win-note-text"));
            _sheet.Add(water);

            var buttons = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var giessen = Ux.IconTextBtn("rain", t.T("nl.beet.giessen"), Water, "gb-btn-teal", "gb-btn-sm");
            giessen.name = "water";
            giessen.SetEnabled(!watered && !sprinkled);
            buttons.Add(giessen);
            if (ripe)
            {
                var ernten = Ux.IconTextBtn("carrot", t.T("nl.beet.ernten", ("menge", crop.Ernte)), Harvest, "gb-btn-confirm", "gb-btn-sm");
                ernten.name = "harvest";
                buttons.Add(ernten);
            }
            _sheet.Add(buttons);
        }

        private string StructureName(string kind) => _host.Strings.T(kind == GarageLogic.Kind ? "garage.name" : "garten.struktur." + kind);

        private void BuildPlantPicker(Gooby.Core.I18n.I18n t, string locale, bool inGreenhouse)
        {
            _sheet.Add(Ux.Text(t.T("nl.beet.pflanzen_hinweis"), "gb-win-lead"));
            var cards = Ux.El("gb-win-grid");
            foreach (var crop in GardenCrops.Plantable(inGreenhouse).OrderBy(c => c.TotalMinutes))
            {
                var seeds = GardenState.SamenCount(_host.Store, crop.Id);
                // a seed dot in the crop's colour on a pastel of it, grow time, seed count / exotic tag
                var seed = Ux.El("gb-ware-seed");
                seed.style.backgroundColor = Gooby.Unity.World.ArtLibrary.Hex(crop.Farbe);
                var card = CounterPanel.Ware(seed, CounterPanel.Pastel(Gooby.Unity.World.ArtLibrary.Hex(crop.Farbe), 0.78f), crop.DisplayName(locale),
                    t.T("nl.beet.dauer", ("min", crop.TotalMinutes)), null, "gb-ware--sm", "gb-ware--tap");
                card.name = "crop-" + crop.Id;
                if (seeds >= 0) card.Add(CounterPanel.Tag(t.T("fuettern.vorrat_badge", ("anzahl", seeds)), seeds == 0 ? "gb-ware-tag--rosa" : "gb-ware-tag--mint"));
                if (crop.Exot) card.Add(CounterPanel.Tag(t.T("nl.beet.exot"), "gb-ware-tag--left", "gb-ware-tag--sonne"));
                if (seeds == 0) card.AddToClassList("gb-outfit-card--locked");
                card.Add(CounterPanel.Go(t.T("nl.beet.pflanzen")));
                var id = crop.Id;
                card.RegisterCallback<ClickEvent>(_ => Plant(id));
                cards.Add(card);
            }
            _sheet.Add(cards);
        }

        /// <summary>"Bauen": greenhouse / sprinkler / tree here for coins, the shed (or its upgrade) and a bigger field.</summary>
        private void BuildBuildSection(Gooby.Core.I18n.I18n t)
        {
            var gs = _host.Store;
            _sheet.Add(CounterPanel.Section("hammer", t.T("garten.bauen"), t.T("nl.beet.bauen_hinweis")));

            var cards = Ux.El("gb-win-grid");
            foreach (var kind in Bauwerke)
            {
                var (w, h) = GardenGrid.StructureSize(kind);
                var check = GardenWorld.Pruefen(gs, kind, _bed);
                var card = CounterPanel.Ware(BuildIcon(kind), new Color(1f, 0.93f, 0.8f), StructureName(kind),
                    t.T("nl.beet.felder", ("w", w), ("h", h)), null, "gb-ware--sm", "gb-ware--tap");
                card.name = "build-" + kind;
                card.Add(CounterPanel.Price(GardenWorld.Preis(kind)));
                if (!check.Ok) card.AddToClassList("gb-outfit-card--locked");
                var local = kind;
                card.RegisterCallback<ClickEvent>(_ => BuildStructure(local));
                cards.Add(card);
            }
            // the garage (garage_logic.gd): once per garden, the car in use parks inside
            var garageBuilt = GarageLogic.Gebaut(gs);
            var (gw, gh) = GarageLogic.Footprint;
            var garage = CounterPanel.Ware("car", new Color(1f, 0.93f, 0.8f), t.T("garage.name"),
                garageBuilt ? t.T("garage.gebaut") : t.T("nl.garage.karte", ("w", gw), ("h", gh)), null, "gb-ware--sm", "gb-ware--tap");
            garage.name = "build-garage";
            if (!garageBuilt) garage.Add(CounterPanel.Price(GarageLogic.Preis));
            if (!GarageLogic.Pruefen(gs, _bed).Ok) garage.AddToClassList("gb-outfit-card--locked");
            garage.RegisterCallback<ClickEvent>(_ => BuildGarage());
            cards.Add(garage);
            // the shed: first purchase stands here, later ones upgrade it (ShedLogic prices)
            var stufe = HomeState.ShedStufe(gs);
            var shedPreis = ShedLogic.UpgradePreis(stufe);
            if (shedPreis > 0)
            {
                var hasShed = GardenState.Grid(gs).StructuresOfKind("shed").Count > 0;
                var card = CounterPanel.Ware("inventory", new Color(1f, 0.93f, 0.8f), hasShed ? t.T("nl.beet.schuppen_ausbau", ("stufe", stufe + 1)) : StructureName("shed"),
                    t.T("nl.beet.effekt_shed", ("bonus", ShedLogic.Bonus[stufe + 1])), null, "gb-ware--sm", "gb-ware--tap");
                card.name = "build-shed";
                card.Add(CounterPanel.Price(shedPreis));
                if (shedPreis > J.Int(J.Path(gs.State, "economy", "coins")) || (!hasShed && !GardenState.Grid(gs).CanPlaceStructure("shed", _bed).Ok)) card.AddToClassList("gb-outfit-card--locked");
                card.RegisterCallback<ClickEvent>(_ => BuildShed());
                cards.Add(card);
            }
            // fence parts from the workbench / the market (garden_world.gd zaun_setzen): one piece along this bed's north edge
            var teile = GardenWorld.Zaunteile(gs);
            var fence = CounterPanel.Ware("fence", new Color(1f, 0.93f, 0.8f), t.T("garten.zaun"), t.T("nl.beet.zaun_effekt"), null, "gb-ware--sm", "gb-ware--tap");
            fence.name = "build-zaun";
            fence.Add(CounterPanel.Status(t.T("nl.beet.zaun_teile", ("n", teile)), teile > 0 ? "gb-ware-status--on" : null));
            if (teile <= 0) fence.AddToClassList("gb-outfit-card--locked");
            fence.RegisterCallback<ClickEvent>(_ => BuildFence());
            cards.Add(fence);
            _sheet.Add(cards);

            var preis = GardenState.NextStufePreis(gs);
            var foot = Ux.Row("gb-win-actions", "gb-win-actions--end");
            foot.style.marginTop = 4;
            var expand = Ux.IconTextBtn("plus", preis > 0 ? t.T("garten.erweitern", ("preis", preis)) : t.T("garten.max_ausgebaut"), Expand, "gb-btn-secondary", "gb-btn-sm");
            expand.name = "expand";
            expand.SetEnabled(preis > 0 && preis <= J.Int(J.Path(gs.State, "economy", "coins")));
            foot.Add(expand);
            _sheet.Add(foot);
        }

        /// <summary>A structure's card: what it does, wood from a tree, shed upgrade.</summary>
        private void BuildStructureCard(Gooby.Core.I18n.I18n t, GardenStructure s)
        {
            var gs = _host.Store;
            string effekt;
            switch (s.Kind)
            {
                case "sprinkler": effekt = t.T("nl.beet.effekt_sprinkler"); break;
                case "baum": effekt = t.T("nl.beet.effekt_baum", ("n", GardenWorld.BaumHolz)); break;
                case "shed": effekt = t.T("nl.beet.effekt_shed", ("bonus", ShedLogic.Bonus[HomeState.ShedStufe(gs)])); break;
                case "werkstatt": effekt = t.T("nl.beet.effekt_werkstatt"); break;
                case GarageLogic.Kind:
                    var (auto, farbe) = Gooby.Core.City.AutoKatalog.AktivesAuto(gs);
                    effekt = t.T("nl.beet.effekt_garage", ("auto", auto?.NameDe ?? ""), ("farbe", Gooby.Core.City.AutoKatalog.FarbName(farbe)));
                    break;
                default: effekt = t.T("nl.beet.effekt_gewaechshaus"); break;
            }
            var line = Ux.Row("gb-win-note", "gb-win-note--white");
            line.Add(Ux.IconTile(s.Kind == "baum" ? "leaf" : s.Kind == "sprinkler" ? "rain" : s.Kind == "shed" ? "inventory" : s.Kind == "werkstatt" ? "toolbox" : s.Kind == GarageLogic.Kind ? "car" : "house", new Color(1f, 0.93f, 0.8f)));
            line.Add(Ux.Text(effekt, "gb-win-note-text"));
            _sheet.Add(line);
            var buttons = Ux.Row("gb-win-actions", "gb-win-actions--end");
            if (s.Kind == "baum")
            {
                var ripe = GardenWorld.BaumReif(gs, s.At, NowS);
                var rest = GardenWorld.BaumRestS(gs, s.At, NowS);
                if (!ripe)
                {
                    buttons.Add(Ux.Text(t.T("nl.beet.baum_rest", ("h", Mathf.CeilToInt((float)(rest / 3600.0)))), "gb-win-actions-hint"));
                }
                var holz = Ux.IconTextBtn("leaf", t.T("garten.baum_ernten"), () => HarvestWood(s.At), "gb-btn-confirm", "gb-btn-sm");
                holz.name = "wood";
                holz.SetEnabled(ripe);
                buttons.Add(holz);
            }
            if (s.Kind == "werkstatt")
            {
                var bench = Ux.IconTextBtn("hammer", t.T("nl.werkstatt.oeffnen"), OpenWorkbench, "gb-btn-primary", "gb-btn-sm");
                bench.name = "workbench";
                buttons.Add(bench);
            }
            if (s.Kind == GarageLogic.Kind)
            {
                var offen = GarageLogic.ZielAnteil(_host.World?.Beds?.GarageZustand ?? GarageLogic.Zu) > 0.5;
                var door = Ux.IconTextBtn(offen ? "arrow_down" : "arrow_up", t.T(offen ? "garage.tor_zu" : "garage.tor_auf"), ToggleDoor, "gb-btn-secondary", "gb-btn-sm");
                door.name = "garage-door";
                buttons.Add(door);
                var go = Ux.IconTextBtn("car", t.T("nl.garage.losfahren"), DriveOff, "gb-btn-primary", "gb-btn-sm");
                go.name = "garage-drive";
                buttons.Add(go);
            }
            if (s.Kind == "shed" && ShedLogic.UpgradePreis(HomeState.ShedStufe(gs)) > 0)
            {
                var up = Ux.IconTextBtn("plus", t.T("shed.upgrade", ("preis", ShedLogic.UpgradePreis(HomeState.ShedStufe(gs)))), BuildShed, "gb-btn-secondary", "gb-btn-sm");
                up.name = "shed-upgrade";
                up.SetEnabled(ShedLogic.KannUpgraden(HomeState.ShedStufe(gs), J.Int(J.Path(gs.State, "economy", "coins"))));
                buttons.Add(up);
            }
            _sheet.Add(buttons);
        }

        private void Plant(string cropId)
        {
            var t = _host.Strings;
            if (!GardenState.Pflanzen(_host.Store, _bed, cropId))
            {
                _say?.Invoke(t.T(GardenState.SamenCount(_host.Store, cropId) == 0 ? "nl.beet.keine_samen" : "nl.beet.geht_nicht"));
                return;
            }
            _host.Store.NotifySliceChanged("achievements");
            GardenLeaves.ForCare(_host.Store, "plant");
            _say?.Invoke(t.T("nl.beet.gepflanzt", ("name", GardenCrops.Crop(cropId).DisplayName(_host.Settings.Locale))));
            _host.World?.RefreshBeds();
            _host.World?.Bounce();
            Rebuild();
        }

        /// <summary>Waters the bed: eight hours of full growth and one tick on the waterings counter (Gieß-Runde).</summary>
        private void Water()
        {
            var t = _host.Strings;
            if (!GardenState.Giessen(_host.Store, _bed, NowS)) return;
            _host.Store.NotifySliceChanged("achievements");
            GardenLeaves.ForCare(_host.Store, "water");
            var waterings = J.Int(J.Path(_host.Store.State, "achievements", "counters", "waterings"));
            _say?.Invoke(t.T("nl.beet.gegossen", ("n", waterings)));
            _host.World?.RefreshBeds();
            Rebuild();
        }

        private void Harvest()
        {
            var t = _host.Strings;
            var crop = GardenCrops.Crop(J.Str(GardenState.Grid(_host.Store).Cell(_bed)["crop"]));
            var menge = GardenState.Ernten(_host.Store, _bed);
            if (menge <= 0) return;
            _host.Store.NotifySliceChanged("achievements");
            GardenLeaves.ForCare(_host.Store, "harvest");
            _say?.Invoke(t.T("nl.beet.geerntet", ("menge", menge), ("name", crop?.DisplayName(_host.Settings.Locale) ?? "")));
            _host.World?.RefreshBeds();
            _host.World?.Bounce();
            Rebuild();
        }

        /// <summary>Buys a structure with this cell as its anchor (the greenhouse door is chosen automatically).</summary>
        private void BuildStructure(string kind)
        {
            var t = _host.Strings;
            var check = GardenWorld.Bauen(_host.Store, kind, _bed);
            if (!check.Ok)
            {
                _say?.Invoke(t.T(check.Reason == GardenWorld.ReasonZuTeuer ? "shed.zu_teuer" : "garten.kein_platz"));
                return;
            }
            _say?.Invoke(t.T("nl.beet.gebaut", ("name", StructureName(kind))));
            _host.World?.RefreshBeds();
            _host.World?.Bounce();
            Rebuild();
        }

        /// <summary>Buys the garage with this cell as its anchor (public for the flow tests).</summary>
        public void BuildGarage()
        {
            var t = _host.Strings;
            var check = GarageLogic.Kaufen(_host.Store, _bed);
            if (!check.Ok)
            {
                Sfx.Play("soft_error");
                _say?.Invoke(t.T(check.Reason == GarageLogic.ReasonSchonGebaut ? "garage.schon_gebaut" : check.Reason == GarageLogic.ReasonZuTeuer ? "shed.zu_teuer" : "garten.kein_platz"));
                return;
            }
            Sfx.Play("build_hammer");
            Sfx.Play("soft_buy");
            _host.Ui.Confetti();
            _say?.Invoke(t.T("garage.fertig"));
            _host.World?.RefreshBeds();
            _host.World?.Bounce();
            Rebuild();
        }

        /// <summary>The roller door the other way; the button follows its target.</summary>
        public void ToggleDoor()
        {
            _host.World?.ToggleGarageDoor();
            Rebuild();
        }

        /// <summary>Off into town through the garage door (the sheet steps aside so the drive-off can be seen).</summary>
        public void DriveOff()
        {
            _host.Ui.Pop();
            HomeHudScreen.DriveOff(_host, _say);
        }

        private void BuildShed()
        {
            var t = _host.Strings;
            var check = GardenWorld.ShedBauen(_host.Store, _bed);
            if (!check.Ok)
            {
                _say?.Invoke(t.T(check.Reason == GardenWorld.ReasonZuTeuer ? "shed.zu_teuer" : check.Reason == GardenWorld.ReasonSchonGebaut ? "shed.max" : "garten.kein_platz"));
                return;
            }
            _say?.Invoke(t.T("shed.fertig"));
            _host.World?.RefreshBeds();
            _host.World?.Bounce();
            Rebuild();
        }

        /// <summary>A fence piece along the north edge of this bed (public for the flow tests).</summary>
        public void BuildFence()
        {
            var t = _host.Strings;
            var check = GardenWorld.ZaunSetzen(_host.Store, _bed, "E", 1);
            if (!check.Ok)
            {
                _say?.Invoke(t.T(check.Reason == GardenWorld.ReasonKeineZaunteile ? "garten.zaun_fehlt" : "garten.kein_platz"));
                return;
            }
            _say?.Invoke(t.T("nl.beet.zaun_gesetzt"));
            _host.World?.RefreshBeds();
            Rebuild();
        }

        private void Expand()
        {
            var t = _host.Strings;
            var check = GardenWorld.Erweitern(_host.Store);
            if (!check.Ok)
            {
                _say?.Invoke(t.T(check.Reason == GardenWorld.ReasonZuTeuer ? "shed.zu_teuer" : "garten.max_ausgebaut"));
                return;
            }
            _say?.Invoke(t.T("garten.erweitert"));
            _host.World?.RefreshBeds();
            _host.World?.Bounce();
            Rebuild();
        }

        /// <summary>The workbench sheet replaces this one (the garden stays the backdrop).</summary>
        public void OpenWorkbench()
        {
            _host.Ui.Pop();
            _host.Ui.Push(new CraftScreen(_host));
        }

        /// <summary>Wood from a ripe tree into home.materials (the tree regrows); public for the flow tests.</summary>
        public void HarvestWood(Cell at)
        {
            var t = _host.Strings;
            var n = GardenWorld.BaumErnten(_host.Store, at, NowS);
            if (n <= 0) { _say?.Invoke(t.T("garten.baum_jung")); return; }
            _say?.Invoke(t.T("garten.baum_reif", ("menge", n)));
            _host.World?.RefreshBeds();
            _host.World?.Bounce();
            Rebuild();
        }
    }
}
