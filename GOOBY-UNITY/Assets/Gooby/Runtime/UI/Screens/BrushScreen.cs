using Gooby.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// Brushing teeth at the sink (port of zahnputz.gd's rubbing gesture): Gooby holds the brush (brush_teeth-loop) while
    /// the player rubs the big brush on the pad back and forth — every turn of direction is a stroke with foam and the
    /// brushing sound, and the bar fills with the distance rubbed (about five seconds of honest rubbing). Full → shiny
    /// teeth, hygiene, the teeth_brushed counter; now and then the old brush gives up (2 %).
    /// </summary>
    public sealed class BrushScreen : UiScreen
    {
        private const float RubNeeded = 2600f;
        private const float BreakChance = 0.02f;

        private readonly GameHost _host;
        private VisualElement _pad, _brush, _fill;
        private bool _down, _done;
        private float _rub, _lastX, _dir;

        public BrushScreen(GameHost host)
        {
            _host = host;
            Id = "zaehne";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            root.pickingMode = PickingMode.Ignore;
            var card = Ux.Col("gb-card", "gb-brush-card");
            card.name = "brush-card";
            var head = Ux.Row("gb-brush-head");
            head.Add(Ux.IconTile("sparkle", new Color(0.86f, 0.94f, 1f)));
            var hint = Ux.Text(t.T("bad.zahnputz.rubbel"), "gb-brush-hint");
            head.Add(hint);
            var close = Ux.IconBtn("close", Cancel, "gb-btn-icon-sm", "gb-btn-secondary");
            close.name = "brush-close";
            head.Add(close);
            card.Add(head);
            _pad = Ux.El("gb-brush-pad");
            _pad.name = "brush-pad";
            _brush = Ux.Icon("toothbrush", "gb-brush-icon");
            _brush.pickingMode = PickingMode.Ignore;
            _pad.Add(_brush);
            card.Add(_pad);
            var bar = Ux.Bar(0f, out _fill, "gb-bar-fill--blau");
            bar.AddToClassList("gb-brush-bar");
            card.Add(bar);
            root.Add(card);

            _pad.RegisterCallback<PointerDownEvent>(e => { _down = true; _lastX = e.localPosition.x; _pad.CapturePointer(e.pointerId); });
            _pad.RegisterCallback<PointerMoveEvent>(e => { if (_down) Rub(e.localPosition.x); });
            _pad.RegisterCallback<PointerUpEvent>(e => { _down = false; _pad.ReleasePointer(e.pointerId); });
        }

        private void Rub(float x)
        {
            if (_done) return;
            var dx = x - _lastX;
            _lastX = x;
            if (Mathf.Abs(dx) < 0.5f) return;
            var dir = Mathf.Sign(dx);
            if (dir != _dir)
            {
                _dir = dir;
                _host.World?.BrushStroke();
            }
            _rub += Mathf.Abs(dx);
            _brush.style.left = Mathf.Clamp(x - 40f, 0f, Mathf.Max(0f, _pad.layout.width - 80f));
            _fill.style.width = Length.Percent(Mathf.Clamp01(_rub / RubNeeded) * 100f);
            if (_rub >= RubNeeded) Finish();
        }

        /// <summary>QA: rub to the end.</summary>
        public void QaRub()
        {
            for (var i = 0; i < 40 && !_done; i++) Rub(i % 2 == 0 ? 20f : 300f);
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            var broke = Random.value < BreakChance;
            _host.World?.FinishBrushing(broke);
            Debug.Log("[interact] brushed broke=" + broke);
            _host.Ui.Pop();
        }

        private void Cancel()
        {
            _done = true;
            _host.World?.EndRoutine();
            Sfx.PlayUi("soft_close");
            _host.Ui.Pop();
        }
    }
}
