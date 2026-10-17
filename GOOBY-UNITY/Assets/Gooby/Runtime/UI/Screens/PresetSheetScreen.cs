using System;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Raum speichern" in build mode (port of preset_sheet.gd): the room's three layout slots with their mini preview
    /// ("12 Möbel · 2 Girlanden"), a name field and Speichern / Überschreiben, Anwenden and Löschen per slot — applying and
    /// deleting ask "Sicher?" on the first tap. <see cref="LayoutPresets"/> (Core) holds the data and the safety net; after
    /// an apply the build session reloads the room (<paramref name="applied"/>).
    /// </summary>
    public sealed class PresetSheetScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly string _roomId;
        private readonly Action _applied;
        private CounterPanel.Parts _ui;
        private TextField _name;
        private Label _feedback;
        private string _armed = "";

        public PresetSheetScreen(GameHost host, string roomId, Action applied)
        {
            _host = host;
            _roomId = roomId;
            _applied = applied;
            Id = "presets";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            var room = t.T(RoomDefs.Get(_roomId)?.NameKey ?? "home.raum." + _roomId);
            _ui = CounterPanel.Build(root, _host, "book", new Color(0.93f, 0.9f, 1f), t.T("build.preset.titel"), room, () => _host.Ui.Pop());
            var head = Ux.Row();
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 10;
            _name = new TextField { name = "preset-name", maxLength = LayoutPresets.NameMax };
            _name.AddToClassList("gb-textfield");
            _name.style.flexGrow = 1;
            _name.textEdition.placeholder = t.T("build.preset.name_platzhalter");
            head.Add(_name);
            _ui.Content.Insert(_ui.Content.IndexOf(_ui.Scroll), head);
            _feedback = Ux.Text("", "gb-md", "gb-bold");
            _feedback.name = "preset-feedback";
            _feedback.style.whiteSpace = WhiteSpace.Normal;
            _feedback.style.marginTop = 8;
            _ui.Content.Add(_feedback);
            Refresh();
        }

        private void Refresh()
        {
            var t = _host.Strings;
            _ui.Body.Clear();
            _ui.Coins.text = Ux.FormatNumber(Gooby.Core.City.ShopLogic.Coins(_host.Store));
            var slots = LayoutPresets.SlotsOf(_host.Store, _roomId);
            for (var i = 0; i < slots.Count; i++) _ui.Body.Add(SlotRow(i, slots[i]));
        }

        private VisualElement SlotRow(int index, Newtonsoft.Json.Linq.JObject slot)
        {
            var t = _host.Strings;
            var frei = LayoutPresets.Frei(slot);
            var row = Ux.Row("gb-card");
            row.name = "preset-slot-" + index;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;
            row.style.paddingTop = 10; row.style.paddingBottom = 10;
            row.Add(Ux.IconTile(frei ? "plus" : "book", frei ? new Color(0.94f, 0.92f, 0.88f) : new Color(0.93f, 0.9f, 1f)));
            var text = Ux.Col();
            text.style.flexGrow = 1;
            text.style.marginLeft = 12;
            text.Add(Ux.Text(frei ? t.T("build.preset.frei") : J.Str(slot["name"]), "gb-lg", "gb-bold"));
            if (!frei)
            {
                var (moebel, girlanden) = LayoutPresets.Zusammenfassung(slot);
                text.Add(Ux.Text(t.T(girlanden == 1 ? "build.preset.info_eine" : "build.preset.info", ("moebel", moebel), ("girlanden", girlanden)), "gb-sm", "gb-mild"));
            }
            row.Add(text);
            var save = Ux.IconTextBtn("check", t.T(frei ? "build.preset.speichern" : "build.preset.ueberschreiben"), () => Save(index), frei ? "gb-btn-primary" : "gb-btn-secondary", "gb-btn-sm");
            save.name = "preset-save-" + index;
            save.style.marginLeft = 8;
            row.Add(save);
            if (frei) return row;
            var applyArmed = _armed == "anwenden:" + index;
            var apply = Ux.IconTextBtn("sparkle", t.T(applyArmed ? "build.preset.sicher" : "build.preset.anwenden"), () => Apply(index), applyArmed ? "gb-btn-primary" : "gb-btn-secondary", "gb-btn-sm");
            apply.name = "preset-apply-" + index;
            apply.style.marginLeft = 8;
            row.Add(apply);
            var deleteArmed = _armed == "loeschen:" + index;
            var delete = deleteArmed
                ? Ux.IconTextBtn("trash", t.T("build.preset.sicher"), () => Delete(index), "gb-btn-primary", "gb-btn-sm")
                : Ux.IconBtn("trash", () => Delete(index), "gb-btn-icon-sm");
            delete.name = "preset-delete-" + index;
            delete.style.marginLeft = 8;
            row.Add(delete);
            return row;
        }

        /// <summary>Speichern / Überschreiben with the name from the field (an existing slot keeps its name when the field is empty).</summary>
        public bool Save(int index)
        {
            var t = _host.Strings;
            _armed = "";
            var name = _name?.value ?? "";
            if (name.Trim().Length == 0) name = J.Str(LayoutPresets.SlotsOf(_host.Store, _roomId)[index]["name"]);
            var reason = LayoutPresets.Speichern(_host.Store, _roomId, index, name);
            Feedback(reason == "" ? t.T("build.preset.gespeichert") : reason == LayoutPresets.NameLeer ? t.T("build.preset.name_hinweis") : reason);
            if (reason == "") { Sfx.PlayUi("soft_open"); if (_name != null) _name.value = ""; }
            else Sfx.PlayUi("soft_error");
            Debug.Log("[presets] save " + _roomId + " #" + index + " → " + (reason == "" ? "ok" : reason));
            Refresh();
            return reason == "";
        }

        /// <summary>Anwenden: the first tap arms "Sicher?", the second swaps the layout.</summary>
        public bool Apply(int index, bool confirmed = false)
        {
            var t = _host.Strings;
            if (!confirmed && _armed != "anwenden:" + index)
            {
                _armed = "anwenden:" + index;
                Refresh();
                return false;
            }
            _armed = "";
            var plan = LayoutPresets.Anwenden(_host.Store, _roomId, index);
            if (!plan.Ok)
            {
                Sfx.PlayUi("soft_error");
                Feedback(t.T(plan.Reason == LayoutPresets.LagerVoll ? "build.preset.lager_voll" : "build.preset.frei"));
            }
            else
            {
                Sfx.PlayUi("soft_confirm");
                Feedback(plan.Fehlend > 0 ? t.T("build.preset.fehlend", ("anzahl", plan.Fehlend)) : t.T("build.preset.angewendet"));
                _applied?.Invoke();
            }
            Debug.Log("[presets] apply " + _roomId + " #" + index + " → " + (plan.Ok ? "ok fehlend=" + plan.Fehlend : plan.Reason));
            Refresh();
            return plan.Ok;
        }

        /// <summary>Löschen: the first tap arms "Sicher?", the second empties the slot.</summary>
        public bool Delete(int index, bool confirmed = false)
        {
            if (!confirmed && _armed != "loeschen:" + index)
            {
                _armed = "loeschen:" + index;
                Refresh();
                return false;
            }
            _armed = "";
            var ok = LayoutPresets.Loeschen(_host.Store, _roomId, index) == "";
            if (ok) Feedback(_host.Strings.T("build.preset.geloescht"));
            Debug.Log("[presets] delete " + _roomId + " #" + index + " → " + ok);
            Refresh();
            return ok;
        }

        private void Feedback(string text)
        {
            if (_feedback != null) _feedback.text = text;
        }
    }
}
