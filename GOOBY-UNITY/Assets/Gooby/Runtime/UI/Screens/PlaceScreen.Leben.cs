using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>The place's life (ort_leben.gd): the interior's visitors start once, their lines float over their heads.</summary>
    public sealed partial class PlaceScreen
    {
        private InteriorScene _lebenInterior;

        private void StartLeben()
        {
            var interior = _host.World?.Interior;
            if (interior == null || interior.Place != _place || Root == null) return;
            if (_lebenInterior != interior)
            {
                if (_lebenInterior != null) _lebenInterior.Spricht -= Spricht;
                _lebenInterior = interior;
                interior.Spricht += Spricht;
            }
            var now = _host.Store.Clock.NowMs();
            var stunde = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime().Hour;
            interior.StartLife(_host.Store.Clock.LocalDay(now), stunde, _host.Settings.ReducedMotion, Zeilen);
        }

        private IReadOnlyList<string> Zeilen(string key)
        {
            var items = _host.Strings.Items(key);
            if (items != null && items.Count > 0) return items.Select(t => J.Str(t)).ToList();
            return _host.Strings.HasKey(key) ? new[] { _host.Strings.T(key) } : (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>A visitor's line as a little bubble over their head (3.2 s, ort_leben.gd SPRUCH_DAUER_S).</summary>
        private void Spricht(Vector3 welt, string zeile, string name)
        {
            var cam = _host.World?.Camera;
            if (cam == null || Root?.panel == null || _host.Ui.Current != this) return;
            var sp = cam.WorldToScreenPoint(welt);
            if (sp.z <= 0f) return;
            var p = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(sp.x, Screen.height - sp.y));
            var bubble = Ux.Col("gb-card", "gb-ort-spruch");
            bubble.pickingMode = PickingMode.Ignore;
            if (!string.IsNullOrEmpty(name)) bubble.Add(Ux.Text(name, "gb-xs", "gb-bold", "gb-ort-spruch-name"));
            var l = Ux.Text(zeile, "gb-sm");
            l.style.whiteSpace = WhiteSpace.Normal;
            bubble.Add(l);
            bubble.style.left = p.x - 110f;
            bubble.style.top = p.y - 70f;
            Root.Add(bubble);
            bubble.schedule.Execute(() => bubble.RemoveFromHierarchy()).StartingIn(3200);
        }
    }
}
