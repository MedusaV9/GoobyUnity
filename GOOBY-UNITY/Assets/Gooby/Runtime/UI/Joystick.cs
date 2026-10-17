using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI
{
    /// <summary>
    /// Concept-style virtual joystick (ring + knob): pointer drag inside the ring yields a direction vector in -1..1.
    /// Works with mouse and touch through UI Toolkit pointer events (pointer capture keeps the drag alive outside the ring).
    /// </summary>
    public sealed class Joystick : VisualElement
    {
        private readonly VisualElement _knob;
        private int _pointerId = -1;
        private Vector2 _value;

        /// <summary>Direction in the UI plane (x right, y down), magnitude ≤ 1; zero when released.</summary>
        public Vector2 Value => _value;
        public event Action<Vector2> Changed;

        public Joystick()
        {
            AddToClassList("gb-joy-ring");
            // boards 03/08: four soft white triangles inside the ring show the stick's directions (rotated by the stylesheet)
            foreach (var cls in new[] { "gb-joy-arrow--n", "gb-joy-arrow--s", "gb-joy-arrow--w", "gb-joy-arrow--e" })
            {
                var a = Ux.Icon("joy_tri", "gb-joy-arrow", cls);
                a.pickingMode = PickingMode.Ignore;
                Add(a);
            }
            _knob = new VisualElement { pickingMode = PickingMode.Ignore };
            _knob.AddToClassList("gb-joy-knob");
            Add(_knob);
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCancelEvent>(_ => Release());
            RegisterCallback<PointerCaptureOutEvent>(_ => Release());
        }

        private void OnDown(PointerDownEvent e)
        {
            _pointerId = e.pointerId;
            this.CapturePointer(_pointerId);
            Update(e.localPosition);
            e.StopPropagation();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId != _pointerId) return;
            Update(e.localPosition);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != _pointerId) return;
            this.ReleasePointer(_pointerId);
            Release();
        }

        private void Release()
        {
            _pointerId = -1;
            _value = Vector2.zero;
            _knob.style.translate = new Translate(0, 0);
            Changed?.Invoke(_value);
        }

        private void Update(Vector2 local)
        {
            var r = resolvedStyle.width * 0.5f;
            if (r <= 0f) return;
            var centre = new Vector2(r, resolvedStyle.height * 0.5f);
            var d = (local - centre) / (r * 0.7f);
            if (d.magnitude > 1f) d.Normalize();
            _value = d;
            var travel = r * 0.45f;
            _knob.style.translate = new Translate(d.x * travel, d.y * travel);
            Changed?.Invoke(_value);
        }
    }
}
