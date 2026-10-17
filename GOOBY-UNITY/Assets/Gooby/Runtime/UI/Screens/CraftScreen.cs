using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Cosmetics;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The workbench in the garden workshop (port of craft_panel.gd): on the left the workshop in 3D — Gooby at the bench,
    /// the selected piece on its podium — with the material stock below it; on the right the recipes, each with what it
    /// needs (have/need per material) and whether it is ready, needs a blueprint from Bodo or misses material (with where
    /// to get it). "Bauen!" lets Gooby hammer for the recipe's time (max 6 s) while the piece grows; then materials go and
    /// the pieces land in storage, ready to place. Bodo's packs (boards, sticks, nails …) move into the workshop stock
    /// when the bench opens. Crafting never costs energy; closing mid-way cancels without using anything.
    /// </summary>
    public sealed class CraftScreen : UiScreen
    {
        private const float MaxCraftS = 6f;

        private readonly GameHost _host;
        private readonly Juice _juice = new Juice();
        private VisualElement _stageField, _mats, _list, _detail;
        private Label _hint, _detailName;
        private VisualElement _progress, _progressFill;
        private Button _build, _place;
        private WorkshopStage _stage;
        private string _selected;
        private float _craftT = -1f, _craftDur;
        private string _lastItem;
        private readonly Dictionary<string, int> _shown = new Dictionary<string, int>();

        public CraftScreen(GameHost host)
        {
            _host = host;
            Id = "werkstatt";
        }

        private bool Reduced => _host.Settings.ReducedMotion;

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);
            var win = Ux.El("gb-win", "gb-craft");
            win.name = "craft-sheet";
            Ux.Abs(win, 56, 22, 56, 22);
            win.Add(CounterPanel.Head("toolbox", new Color(1f, 0.9f, 0.76f), t.T("craft.titel"), t.T("nl.werkstatt.untertitel"), Close, J.Int(J.Path(_host.Store.State, "economy", "coins"))).Row);
            var body = Ux.Row("gb-craft-body");
            win.Add(body);

            var left = Ux.Col("gb-craft-left");
            _stageField = Ux.El("gb-craft-stage");
            _stageField.name = "craft-stage";
            left.Add(_stageField);
            _mats = Ux.Row("gb-craft-mats");
            left.Add(_mats);
            body.Add(left);

            var right = Ux.Col("gb-craft-right");
            right.Add(CounterPanel.Section("hammer", t.T("craft.rezepte")));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gb-win-scroll");
            _list = Ux.Col("gb-craft-list");
            scroll.Add(_list);
            right.Add(scroll);
            _detail = Ux.Col("gb-craft-detail");
            _detailName = Ux.Text("", "gb-craft-detail-name");
            _detail.Add(_detailName);
            _hint = Ux.Text("", "gb-craft-hint");
            _hint.name = "craft-hint";
            _detail.Add(_hint);
            _progress = Ux.Bar(0f, out _progressFill);
            _progress.AddToClassList("gb-craft-progress");
            _progress.style.display = DisplayStyle.None;
            _detail.Add(_progress);
            var actions = Ux.Row("gb-craft-actions");
            _place = Ux.IconTextBtn("move", t.T("nl.werkstatt.aufstellen"), PlaceNow, "gb-btn-secondary", "gb-btn-sm");
            _place.name = "craft-place";
            _place.style.display = DisplayStyle.None;
            actions.Add(_place);
            _build = Ux.IconTextBtn("hammer", t.T("craft.bauen"), StartCraft, "gb-btn-primary", "gb-craft-build");
            _build.name = "craft-build";
            actions.Add(_build);
            _detail.Add(actions);
            right.Add(_detail);
            body.Add(right);
            root.Add(win);
            root.Add(_juice.Layer);

            var moved = CraftState.UebernehmeBaumarktEinkaeufe(_host.Store);
            if (moved > 0) _host.Ui.Toast(t.T("nl.werkstatt.uebernommen", ("n", moved)), "toolbox");
            var states = CraftState.RecipeStates(_host.Store);
            _selected = (states.FirstOrDefault(s => s.check.Ok).recipe ?? states.FirstOrDefault().recipe)?.Id;
            Refresh();
            Sfx.PlayUi("soft_open");
            Debug.Log("[craft] open recipes=" + states.Count + " ready=" + states.Count(s => s.check.Ok) + " workshop=" + CraftState.WerkstattGebaut(_host.Store));
        }

        public override void OnShown()
        {
            if (_stage != null || !MgStage3D.Supported) return;
            var outfit = CosmeticsState.EquippedMap(J.Dict(_host.Store.State["cosmetics"]));
            _stage = new WorkshopStage(_stageField, 560f, 470f, outfit);
            var recipe = CraftCatalog.Recipe(_selected);
            if (recipe != null) _stage.ShowPiece(recipe.Item);
        }

        public override void OnHidden()
        {
            if (_host.Ui.IsMounted(this)) return;
            _stage?.Destroy();
            _stage = null;
        }

        private void Close()
        {
            if (_craftT >= 0f) Debug.Log("[craft] cancelled " + _selected);
            _craftT = -1f;
            Sfx.PlayUi("soft_close");
            _host.Ui.Pop();
        }

        // ── list and detail ──

        private void Refresh()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            var gs = _host.Store;
            var mats = CraftState.Materials(gs);

            _mats.Clear();
            foreach (var m in CraftCatalog.Materials())
            {
                var n = CraftLogic.CountOf(mats, m.Id);
                var chip = Ux.Row("gb-craft-mat");
                chip.name = "craft-mat-" + m.Id;
                chip.Add(Ux.Icon("mat_" + m.Id));
                chip.Add(Ux.Text(Ux.FormatNumber(n), "gb-craft-mat-n"));
                if (n == 0) chip.AddToClassList("gb-craft-mat--leer");
                _mats.Add(chip);
                if (_shown.TryGetValue(m.Id, out var before) && before != n) _juice.ScalePop(chip, 1.25f, 0.28f);
                _shown[m.Id] = n;
            }

            _list.Clear();
            foreach (var (recipe, check) in CraftState.RecipeStates(gs))
            {
                var row = Ux.Row("gb-craft-recipe");
                row.name = "craft-recipe-" + recipe.Id;
                if (recipe.Id == _selected) row.AddToClassList("gb-craft-recipe--selected");
                if (check.Reason == CraftLogic.ReasonBlueprint) row.AddToClassList("gb-craft-recipe--locked");
                var thumb = Ux.El("gb-craft-thumb");
                thumb.Add(Ux.ItemThumb(recipe.Item, "toolbox"));
                row.Add(thumb);
                var texts = Ux.Col("gb-craft-texts");
                var name = t.T("craft.rezept." + recipe.Id);
                texts.Add(Ux.Text(name, "gb-craft-name"));
                var needs = Ux.Row("gb-craft-needs");
                foreach (var kv in recipe.Materialien)
                {
                    var have = CraftLogic.CountOf(mats, kv.Key);
                    var need = Ux.Row("gb-craft-need");
                    if (have < kv.Value) need.AddToClassList("gb-craft-need--kurz");
                    need.Add(Ux.Icon("mat_" + kv.Key));
                    need.Add(Ux.Text(Mathf.Min(have, 99) + "/" + kv.Value));
                    needs.Add(need);
                }
                texts.Add(needs);
                row.Add(texts);
                var status = check.Ok ? CounterPanel.Status(t.T("nl.werkstatt.bereit"), "gb-ware-status--on")
                    : check.Reason == CraftLogic.ReasonBlueprint ? CounterPanel.Status(t.T("nl.werkstatt.bauplan"))
                    : CounterPanel.Status(check.Reason == CraftLogic.ReasonStation ? t.T("nl.werkstatt.keine_werkstatt") : t.T("nl.werkstatt.fehlt"), "gb-craft-status--fehlt");
                status.AddToClassList("gb-craft-status");
                row.Add(status);
                var id = recipe.Id;
                row.RegisterCallback<ClickEvent>(_ => Select(id));
                _list.Add(row);
            }
            RefreshDetail();
        }

        private void Select(string id)
        {
            if (_craftT >= 0f || id == _selected) return;
            _selected = id;
            _place.style.display = DisplayStyle.None;
            Sfx.PlayUi("soft_chip");
            _stage?.ShowPiece(CraftCatalog.Recipe(id)?.Item);
            Refresh();
        }

        private void RefreshDetail()
        {
            var t = _host.Strings;
            var locale = _host.Settings.Locale;
            var recipe = CraftCatalog.Recipe(_selected);
            if (recipe == null)
            {
                _detailName.text = "";
                _hint.text = t.T("craft.kein_rezept");
                _build.SetEnabled(false);
                return;
            }
            var check = CraftState.Pruefen(_host.Store, recipe.Id);
            var def = FurnitureCatalog.Def(recipe.Item);
            _detailName.text = (def?.DisplayName(locale) ?? t.T("craft.rezept." + recipe.Id)) + (recipe.Count > 1 ? "  ×" + recipe.Count : "");
            _build.SetEnabled(check.Ok && _craftT < 0f);
            if (_craftT >= 0f) return;
            if (check.Ok)
            {
                _hint.text = t.T("nl.werkstatt.hinweis_bereit", ("sek", Mathf.CeilToInt(Mathf.Min(MaxCraftS, (float)recipe.CraftSek))));
                return;
            }
            switch (check.Reason)
            {
                case CraftLogic.ReasonStation: _hint.text = t.T("craft.werkstatt_fehlt"); break;
                case CraftLogic.ReasonBlueprint: _hint.text = t.T("nl.werkstatt.bauplan_hinweis"); break;
                case CraftLogic.ReasonStorage: _hint.text = t.T("craft.lager_voll"); break;
                default:
                    var lines = new List<string>();
                    foreach (var kv in check.Missing)
                    {
                        var m = CraftCatalog.Material(kv.Key);
                        lines.Add(t.T("nl.werkstatt.fehlt_zeile", ("material", m?.Name(locale) ?? kv.Key), ("n", kv.Value), ("quelle", t.T("craft.quelle." + (m?.Quelle ?? "garten-spawn")))));
                    }
                    var kosten = CraftState.BaumarktKosten(recipe, CraftState.Materials(_host.Store));
                    if (kosten > 0) lines.Add(t.T("craft.kosten_baumarkt", ("preis", kosten)));
                    _hint.text = string.Join("\n", lines);
                    break;
            }
        }

        // ── crafting ──

        private void StartCraft()
        {
            if (_craftT >= 0f || _selected == null) return;
            var check = CraftState.Pruefen(_host.Store, _selected);
            if (!check.Ok)
            {
                Sfx.PlayUi("soft_error");
                _juice.ScalePop(_hint, 1.08f, 0.2f);
                RefreshDetail();
                return;
            }
            var recipe = CraftCatalog.Recipe(_selected);
            _craftDur = Reduced ? 0.2f : Mathf.Min(MaxCraftS, Mathf.Max(0.8f, (float)recipe.CraftSek));
            _craftT = 0f;
            _place.style.display = DisplayStyle.None;
            _progress.style.display = DisplayStyle.Flex;
            _progressFill.style.width = Length.Percent(0f);
            _hint.text = _host.Strings.T("craft.laeuft");
            _build.SetEnabled(false);
            _list.SetEnabled(false);
            _stage?.StartCraft(_craftDur);
            Debug.Log("[craft] start " + recipe.Id + " " + _craftDur.ToString("0.0") + "s");
        }

        private void FinishCraft()
        {
            var t = _host.Strings;
            _craftT = -1f;
            _progress.style.display = DisplayStyle.None;
            _list.SetEnabled(true);
            var res = CraftState.Craft(_host.Store, _selected);
            if (!res.ok)
            {
                Sfx.PlayUi("soft_error");
                Refresh();
                return;
            }
            var name = FurnitureCatalog.Def(res.item)?.DisplayName(_host.Settings.Locale) ?? res.item;
            _stage?.FinishCraft(Reduced);
            Sfx.Play("game_star");
            Sfx.PlayUi("soft_confirm");
            _host.Ui.Confetti(Reduced ? 12 : 46);
            _lastItem = res.item;
            Refresh();
            _hint.text = t.T("craft.fertig", ("name", res.count > 1 ? res.count + "× " + name : name));
            _juice.ScalePop(_hint, 1.12f, 0.3f);
            _place.style.display = DisplayStyle.Flex;
            Debug.Log("[craft] done " + res.item + " x" + res.count + " storage=" + StorageLogic.CountOf(HomeState.Storage(_host.Store), res.item));
        }

        /// <summary>Hands the fresh piece to build mode of the room Gooby is in (the lawn when the bench was opened in the garden).</summary>
        private void PlaceNow()
        {
            if (string.IsNullOrEmpty(_lastItem)) return;
            var roomId = _host.World?.RoomId ?? "living";
            _host.Ui.Pop();
            _host.Ui.Push(new BuildModeScreen(_host, roomId, preselect: _lastItem));
        }

        public override void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _juice.Tick(dt);
            _stage?.Tick(dt, Reduced);
            if (_craftT < 0f) return;
            _craftT += dt;
            _progressFill.style.width = Length.Percent(Mathf.Clamp01(_craftT / _craftDur) * 100f);
            if (_craftT >= _craftDur) FinishCraft();
        }
    }
}
