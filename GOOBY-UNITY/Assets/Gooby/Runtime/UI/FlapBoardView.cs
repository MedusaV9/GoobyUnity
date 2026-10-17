using System.Collections.Generic;
using Gooby.Core.City;
using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI
{
    /// <summary>
    /// The split-flap departure board on screen (port of flap_board.gd's view): a dark board, every letter on its own
    /// little flap with a hairline split, yellow capitals. A row that gets a new text clatters through the flap alphabet
    /// (<see cref="FlapBoard.Sequenz"/>, one frame per 35 ms, rows a beat apart) with a soft tick; reduced motion shows
    /// the text at once. Each row can carry a trailing element (the booking pill).
    /// </summary>
    public sealed class FlapBoardView
    {
        private sealed class Row
        {
            public Label[] Cells;
            public string Shown = "";
            public List<string> Frames;
            public float Wait, Clock;
            public int Frame;
        }

        public VisualElement Root { get; }
        private readonly VisualElement _rows;
        private readonly Label _note;

        /// <summary>A small line at the board's top right (the travel desk shows the Weltengooby progress there).</summary>
        public void SetNote(string text) => _note.text = text ?? "";
        private readonly List<Row> _all = new List<Row>();
        private readonly int _width;
        private float _tickIn;

        public bool Animating
        {
            get
            {
                foreach (var r in _all) if (r.Frames != null) return true;
                return false;
            }
        }

        public FlapBoardView(string title, int[] spalten, params string[] heads)
        {
            _width = 0;
            foreach (var s in spalten) _width += s;
            Root = Ux.El("gb-flap");
            Root.name = "flap-board";
            var top = Ux.Row("gb-flap-top");
            top.Add(Ux.Text(title, "gb-flap-title"));
            var right = Ux.Row("gb-flap-top-right");
            _note = Ux.Text("", "gb-flap-note");
            _note.name = "flap-note";
            right.Add(_note);
            right.Add(Ux.El("gb-flap-lamp"));
            top.Add(right);
            Root.Add(top);
            var head = Ux.Row("gb-flap-head");
            for (var i = 0; i < spalten.Length && i < heads.Length; i++)
            {
                if (spalten[i] <= 0) continue;
                var h = Ux.Text(heads[i], "gb-flap-head-cell");
                h.style.width = spalten[i] * CellPitch;
                head.Add(h);
            }
            Root.Add(head);
            _rows = Ux.Col("gb-flap-rows");
            Root.Add(_rows);
        }

        /// <summary>Width of one flap plus its gap (keep in step with .gb-flap-cell in gooby-screens.uss).</summary>
        public const float CellPitch = 16f;

        /// <summary>A new row (blank flaps); <paramref name="trailing"/> sits at its right end. Returns the row index.</summary>
        public int AddRow(string name, VisualElement trailing = null)
        {
            var el = Ux.Row("gb-flap-row");
            el.name = name;
            var cells = new Label[_width];
            var strip = Ux.Row("gb-flap-strip");
            for (var i = 0; i < _width; i++)
            {
                var cell = Ux.El("gb-flap-cell");
                cell.pickingMode = PickingMode.Ignore;
                var ch = new Label(" ") { pickingMode = PickingMode.Ignore };
                ch.AddToClassList("gb-flap-char");
                cell.Add(ch);
                var split = Ux.El("gb-flap-split");
                split.pickingMode = PickingMode.Ignore;
                cell.Add(split);
                strip.Add(cell);
                cells[i] = ch;
            }
            el.Add(strip);
            if (trailing != null)
            {
                trailing.AddToClassList("gb-flap-trail");
                el.Add(trailing);
            }
            _rows.Add(el);
            _all.Add(new Row { Cells = cells, Shown = new string(' ', _width) });
            return _all.Count - 1;
        }

        /// <summary>The row's target text; it clatters there from what it shows now (at once when <paramref name="animate"/> is off).</summary>
        public void SetText(int row, string text, bool animate)
        {
            if (row < 0 || row >= _all.Count) return;
            var r = _all[row];
            var target = (text ?? "").PadRight(_width).Substring(0, _width);
            if (!animate)
            {
                r.Frames = null;
                Show(r, target);
                return;
            }
            var from = r.Frames != null && r.Frame > 0 ? r.Frames[r.Frame - 1] : r.Shown;
            if (from == target && r.Frames == null) return;
            r.Frames = FlapBoard.Sequenz(from, target);
            r.Frame = 0;
            r.Clock = 0f;
            r.Wait = row * 0.06f;
            if (r.Frames.Count == 0) r.Frames = null;
        }

        public void Tick(float dt)
        {
            var busy = false;
            foreach (var r in _all)
            {
                if (r.Frames == null) continue;
                if (r.Wait > 0f) { r.Wait -= dt; busy = true; continue; }
                r.Clock += dt;
                var steps = Mathf.Min(r.Frames.Count - r.Frame, Mathf.FloorToInt(r.Clock / (float)FlapBoard.SchrittS));
                if (steps <= 0) { busy = true; continue; }
                r.Clock -= steps * (float)FlapBoard.SchrittS;
                r.Frame += steps;
                Show(r, r.Frames[r.Frame - 1]);
                if (r.Frame >= r.Frames.Count) r.Frames = null;
                else busy = true;
            }
            if (!busy) return;
            _tickIn -= dt;
            if (_tickIn > 0f) return;
            _tickIn = 0.07f;
            Sfx.Play("soft_tick", Random.Range(-2f, 2f), 0.35f);
        }

        private static void Show(Row r, string text)
        {
            for (var i = 0; i < r.Cells.Length && i < text.Length; i++)
            {
                var c = text[i].ToString();
                if (r.Cells[i].text != c) r.Cells[i].text = c;
            }
            r.Shown = text;
        }
    }
}
