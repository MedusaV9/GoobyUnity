using Gooby.Core.Json;
using Gooby.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "Spieglein, Spieglein" at the bathroom mirror (port of spiegel.gd): four sliders shape Gooby's face — eye distance,
    /// eye size, ear length and chubby cheeks — written to meta.charMorphs (the character editor's save contract) and
    /// shown on the avatar at once while the camera looks at him like in the wardrobe.
    /// </summary>
    public sealed class MirrorScreen : UiScreen
    {
        private static readonly (string id, string label, double min, double max, double neutral)[] Morphs =
        {
            ("eyes_apart", "bad.spiegel.augenabstand", -1.0, 1.0, 0.0),
            ("eye_scale", "bad.spiegel.augengroesse", 0.7, 1.4, 1.0),
            ("ear_len", "bad.spiegel.ohrlaenge", 0.7, 1.4, 1.0),
            ("chubby", "bad.spiegel.pausbacken", 0.0, 1.0, 0.0),
        };

        private readonly GameHost _host;

        public MirrorScreen(GameHost host)
        {
            _host = host;
            Id = "spiegel";
        }

        protected override void Build(VisualElement root)
        {
            var t = _host.Strings;
            root.AddToClassList("gb-sheets");
            root.pickingMode = PickingMode.Ignore;
            var sheet = Ux.El("gb-sheet", "gb-win-sheet");
            sheet.name = "mirror-sheet";
            sheet.style.left = Length.Percent(52); sheet.style.right = 24;
            sheet.style.top = 90;
            sheet.style.borderBottomLeftRadius = 0; sheet.style.borderBottomRightRadius = 0;
            sheet.Add(CounterPanel.Head("user", new Color(0.86f, 0.94f, 1f), t.T("bad.spiegel.titel"), t.T("nl.interakt.spiegel_hinweis"), Close).Row);
            var body = Ux.Col("gb-win-body");
            var morphs = J.Path(_host.Store.State, "meta", "charMorphs") as JObject ?? new JObject();
            foreach (var m in Morphs)
            {
                var local = m;
                var row = Ux.Col("gb-mirror-row");
                row.Add(Ux.Text(t.T(m.label), "gb-bold"));
                var value = J.NumOr(morphs[m.id], m.neutral);
                var slider = Ux.Slider((value - m.min) / (m.max - m.min), v => Set(local.id, local.min + v * (local.max - local.min)));
                slider.name = "mirror-" + m.id;
                row.Add(slider);
                body.Add(row);
            }
            var reset = Ux.IconTextBtn("undo", t.T("nl.interakt.spiegel_zurueck"), Reset, "gb-btn-secondary", "gb-btn-sm");
            reset.name = "mirror-reset";
            body.Add(reset);
            sheet.Add(body);
            root.Add(sheet);
        }

        public override void OnShown()
        {
            _host.World?.FaceCamera();
            _host.World?.SetWardrobeCamera(true);
        }

        public override void OnHidden() => _host.World?.SetWardrobeCamera(false);

        private void Set(string id, double value)
        {
            _host.Store.Update(s => J.EnsureDict(J.EnsureDict(s, "meta"), "charMorphs")[id] = System.Math.Round(value, 3), GameHost.TicksMs);
            _host.World?.ApplyOutfit();
        }

        private void Reset()
        {
            _host.Store.Update(s =>
            {
                var morphs = J.EnsureDict(J.EnsureDict(s, "meta"), "charMorphs");
                foreach (var m in Morphs) morphs[m.id] = m.neutral;
            }, GameHost.TicksMs);
            _host.World?.ApplyOutfit();
            Sfx.PlayUi("soft_back");
            _host.Ui.Pop();
            _host.Ui.Push(new MirrorScreen(_host));
        }

        private void Close()
        {
            Sfx.PlayUi("soft_close");
            _host.Ui.Pop();
        }
    }
}
