using System;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Gooby asks for your birthday (port of soul_birthday_panel.gd, G4/P23): a small card with −/+ steppers for the day
    /// and the month (touch-sized, no spin boxes), "Merk es dir!" and a polite way out. Always dismissable — the scrim,
    /// back and "Verrat ich nicht" just close it.
    /// </summary>
    public sealed class BirthdayScreen : UiScreen
    {
        private readonly GameHost _host;
        private readonly Action<int, int> _saved;
        private int _day = 1, _month = 1;
        private Label _dayLabel, _monthLabel;

        public BirthdayScreen(GameHost host, Action<int, int> saved)
        {
            _host = host;
            _saved = saved;
            Id = "birthday";
            var today = DateTime.Now;
            _day = today.Day;
            _month = today.Month;
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            var scrim = Ux.El("gb-scrim");
            scrim.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(scrim);
            var sheet = Ux.El("gb-sheet", "gb-win-sheet", "gb-birthday");
            sheet.name = "birthday-card";
            var head = CounterPanel.Head("gift", new Color(1f, 0.88f, 0.9f), t.T("soul.geburtstag_panel.titel"), t.T("soul.frage.geburtstag"), Close);
            sheet.Add(head.Row);
            var body = Ux.Col("gb-win-body");
            var row = Ux.Row("gb-birthday-steppers");
            row.Add(Stepper(t.T("soul.geburtstag_panel.tag"), "day", () => Step(ref _day, -1, DaysIn(_month)), () => Step(ref _day, 1, DaysIn(_month)), out _dayLabel));
            row.Add(Stepper(t.T("soul.geburtstag_panel.monat"), "month", () => { Step(ref _month, -1, 12); ClampDay(); }, () => { Step(ref _month, 1, 12); ClampDay(); }, out _monthLabel));
            body.Add(row);
            var actions = Ux.Row("gb-win-actions", "gb-win-actions--end");
            var later = Ux.Btn(t.T("soul.geburtstag_panel.abbrechen"), Close, "gb-btn-secondary");
            later.name = "birthday-later";
            later.style.marginRight = 10;
            actions.Add(later);
            var save = Ux.IconTextBtn("heart", t.T("soul.geburtstag_panel.speichern"), Save, "gb-btn-primary");
            save.name = "birthday-save";
            actions.Add(save);
            body.Add(actions);
            sheet.Add(body);
            root.Add(sheet);
            Refresh();
        }

        private VisualElement Stepper(string title, string name, Action minus, Action plus, out Label value)
        {
            var col = Ux.Col("gb-card", "gb-birthday-stepper");
            col.Add(Ux.Text(title, "gb-md", "gb-mild"));
            var line = Ux.Row();
            line.style.alignItems = Align.Center;
            var down = Ux.Btn("−", () => { minus(); Sfx.PlayUi("soft_tick"); Refresh(); }, "gb-btn-secondary", "gb-birthday-step");
            down.name = "birthday-" + name + "-minus";
            line.Add(down);
            value = Ux.Text("", "gb-h1", "gb-birthday-value");
            line.Add(value);
            var up = Ux.Btn("+", () => { plus(); Sfx.PlayUi("soft_tick"); Refresh(); }, "gb-btn-secondary", "gb-birthday-step");
            up.name = "birthday-" + name + "-plus";
            line.Add(up);
            col.Add(line);
            return col;
        }

        private static int DaysIn(int month) => DateTime.DaysInMonth(2024, month);

        private static void Step(ref int v, int d, int max) => v = (v - 1 + d + max) % max + 1;

        private void ClampDay() => _day = Mathf.Min(_day, DaysIn(_month));

        private void Refresh()
        {
            if (_dayLabel != null) _dayLabel.text = _day.ToString();
            if (_monthLabel != null)
            {
                var culture = _host.Strings.Locale == "en" ? new System.Globalization.CultureInfo("en-GB") : new System.Globalization.CultureInfo("de-DE");
                _monthLabel.text = culture.DateTimeFormat.GetMonthName(_month);
            }
        }

        /// <summary>QA: set the date directly before saving.</summary>
        public void QaSet(int day, int month)
        {
            _month = Mathf.Clamp(month, 1, 12);
            _day = Mathf.Clamp(day, 1, DaysIn(_month));
            Refresh();
        }

        private void Save()
        {
            Sfx.PlayUi("soft_confirm");
            if (_host.Ui.Current == this) _host.Ui.Pop();
            _saved?.Invoke(_month, _day);
        }

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            if (_host.Ui.Current == this) _host.Ui.Pop();
        }
    }
}
