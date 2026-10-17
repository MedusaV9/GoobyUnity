using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Unity.App;
using Gooby.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// "What can I tap?" — the HUD eye (port of interaction_spotlight.gd): while it is on, every interactable piece of the
    /// room wears a round badge with what it does (the lamp a bulb, the fridge a carrot, the bed a moon …) and brightens in
    /// a slow pulse; a piece off screen shows its badge at the edge with an arrow towards it; Gooby wears a heart. A tap on
    /// a badge is a tap on the piece (or a pat for Gooby). The eye goes out on its own when the room changes or a sheet
    /// covers the home.
    /// </summary>
    public sealed partial class HomeHudScreen
    {
        private VisualElement _eyeBtn, _spotLayer, _spotGooby;
        private readonly List<(FurnitureMarker marker, VisualElement badge)> _spots = new List<(FurnitureMarker, VisualElement)>();
        private string _spotRoom;
        private float _spotBeat;

        public bool SpotlightOn => _spotLayer != null;

        /// <summary>The badge icon of an interactable kind (the same glyphs as the action bubble), null when it has none.</summary>
        public static string SpotIcon(string kind)
        {
            switch (kind)
            {
                case Interactables.Licht: return "bulb";
                case Interactables.Dusche: return "rain";
                case Interactables.Klo: return "home";
                case Interactables.Zahnputz: return "sparkle";
                case Interactables.Spiegel: return "user";
                case Interactables.Bett: return "moon";
                case Interactables.Kuehlschrank: return "carrot";
                case Interactables.Radio: return "speaker";
                case Interactables.Fernseher: return "play";
                case Interactables.Sitzen: return "sofa";
                case Interactables.Nougat: return "candy";
                case Interactables.Postkarten: return "mail";
                default: return null;
            }
        }

        private void BuildSpotlight(VisualElement root)
        {
            var eye = Ux.IconBtn("eye_white", ToggleSpotlight, "gb-btn-icon-sm", "gb-hud-eye");
            eye.name = "hud-eye";
            // left of the talk disc (150 × 124 at right/bottom 28), level with its middle: the right card stack (parcel,
            // holiday, event) grows down to about 600 px and used to run under an eye placed above the disc
            Ux.Abs(eye, null, null, 190, 68);
            _eyeBtn = eye;
            root.Add(eye);
        }

        public void ToggleSpotlight()
        {
            if (SpotlightOn) { EndSpotlight(); Sfx.PlayUi("soft_toggle"); return; }
            StartSpotlight();
        }

        private void StartSpotlight()
        {
            var w = _host.World;
            var t = _host.Strings;
            if (w == null || AwayBlocks()) return;
            var markers = w.InteractMarkers();
            markers.RemoveAll(m => SpotIcon(m.Kind) == null);
            if (markers.Count == 0)
            {
                Sfx.PlayUi("soft_error");
                Say(t.T(w.InGarden ? "nl.hud.auge_garten" : "nl.hud.auge_leer"));
                return;
            }
            _spotLayer = new VisualElement { name = "spot-layer", pickingMode = PickingMode.Ignore };
            _spotLayer.AddToClassList("gb-spot-layer");
            // behind the HUD's cards and bubbles: a badge under the task card waits there instead of covering the text
            Root.Insert(0, _spotLayer);
            _spotRoom = w.RoomId;
            _spotBeat = 0f;
            foreach (var m in markers)
            {
                var marker = m;
                var badge = SpotBadge(SpotIcon(m.Kind), () => TapSpot(marker));
                badge.name = "spot-" + m.Kind + "-" + m.Uid;
                _spotLayer.Add(badge);
                _spots.Add((m, badge));
            }
            if (w.Gooby != null && w.Gooby.gameObject.activeInHierarchy)
            {
                _spotGooby = SpotBadge("heart", TapSpotGooby);
                _spotGooby.name = "spot-gooby";
                _spotGooby.AddToClassList("gb-spot--gooby");
                _spotLayer.Add(_spotGooby);
            }
            w.SetInteractGlow(markers);
            _eyeBtn?.AddToClassList("gb-hud-eye--on");
            Sfx.PlayUi("soft_toggle");
            Say(t.T("nl.hud.auge_hinweis", ("n", markers.Count)));
            TickSpotlight(0f);
            Debug.Log("[interact] eye on: " + markers.Count + " pieces");
        }

        private VisualElement SpotBadge(string icon, System.Action tap)
        {
            var badge = Ux.El("gb-spot");
            badge.pickingMode = PickingMode.Position;
            var ring = Ux.El("gb-spot-ring");
            ring.pickingMode = PickingMode.Ignore;
            badge.Add(ring);
            var disc = Ux.El("gb-spot-disc");
            disc.pickingMode = PickingMode.Ignore;
            disc.Add(Ux.Icon(icon));
            badge.Add(disc);
            var arrow = Ux.El("gb-spot-arrow");
            arrow.pickingMode = PickingMode.Ignore;
            arrow.Add(Ux.Icon("arrow_right"));
            badge.Add(arrow);
            badge.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); tap(); });
            return badge;
        }

        private void TapSpot(FurnitureMarker m)
        {
            EndSpotlight();
            _host.World?.TapMarker(m);
        }

        private void TapSpotGooby()
        {
            EndSpotlight();
            _host.World?.PokeGooby();
        }

        private void EndSpotlight()
        {
            if (_spotLayer == null) return;
            _spotLayer.RemoveFromHierarchy();
            _spotLayer = null;
            _spotGooby = null;
            _spots.Clear();
            _host.World?.ClearInteractGlow();
            _eyeBtn?.RemoveFromClassList("gb-hud-eye--on");
        }

        private void TickSpotlight(float dt)
        {
            if (_spotLayer == null) return;
            var w = _host.World;
            if (w == null || w.Room == null || w.InGarden || w.RoomId != _spotRoom || _host.Ui.Current != this || w.Camera == null || Root.panel == null)
            {
                EndSpotlight();
                return;
            }
            _spotBeat += dt;
            var beat = 0.5f + 0.5f * Mathf.Sin(_spotBeat * 3.6f);
            w.PulseInteractGlow(beat);
            var ringScale = new Scale(Vector3.one * (0.94f + 0.14f * beat));
            foreach (var (_, badge) in _spots) badge.Q(className: "gb-spot-ring").style.scale = ringScale;
            if (_spotGooby != null) _spotGooby.Q(className: "gb-spot-ring").style.scale = ringScale;
            var size = new Vector2(Root.layout.width, Root.layout.height);
            foreach (var (m, badge) in _spots)
                if (m != null) PlaceSpot(badge, m.Bounds.center + Vector3.up * (m.Bounds.extents.y + 0.1f), w.Camera, size);
            if (_spotGooby != null && w.Gooby != null) PlaceSpot(_spotGooby, w.GoobyHead + Vector3.up * 0.32f, w.Camera, size);
        }

        /// <summary>Over its piece while on screen; otherwise at the screen edge, its arrow pointing towards the piece.</summary>
        private void PlaceSpot(VisualElement badge, Vector3 world, Camera cam, Vector2 size)
        {
            const float half = 26f, edge = 40f, top = 110f;
            var behind = cam.WorldToScreenPoint(world).z < 0f;
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, world, cam);
            if (behind) p = new Vector2(size.x - p.x, size.y + 400f);
            var clamped = new Vector2(Mathf.Clamp(p.x, edge, size.x - edge), Mathf.Clamp(p.y, top, size.y - edge));
            var outside = behind || (clamped - p).sqrMagnitude > 1f;
            badge.style.left = clamped.x - half;
            badge.style.top = clamped.y - half;
            var arrow = badge.Q(className: "gb-spot-arrow");
            if (arrow == null) return;
            arrow.style.display = outside ? DisplayStyle.Flex : DisplayStyle.None;
            if (outside)
            {
                var d = p - clamped;
                arrow.style.rotate = new Rotate(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
        }

        /// <summary>QA: the eye on/off and how many badges it shows.</summary>
        public int QaSpotlight(bool on)
        {
            if (on != SpotlightOn) ToggleSpotlight();
            return _spots.Count;
        }
    }
}
