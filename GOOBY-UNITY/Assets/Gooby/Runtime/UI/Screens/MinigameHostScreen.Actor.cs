using System;
using Gooby.Core.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The player's 3D Gooby as a game's own figure. Hosts that draw Gooby as a 2D portrait (bunny hop, trampoline, the
    /// runner, the fishing jetty …) hand that element to <see cref="FollowGooby"/>; from then on the rigged avatar in the
    /// player's outfit takes its place every frame — position, size, rotation and pop scale — while the portrait stays
    /// hidden underneath as it was (hitboxes and host logic are untouched). He walks when the portrait moves sideways and
    /// reacts to the round like the companion (gains, losses, streaks, LOS!, the end). Without a GPU nothing changes.
    /// </summary>
    public abstract partial class MinigameHostScreen
    {
        private World.MgBuddy3D _actor;
        private VisualElement _actorPortrait, _actorView;
        private float _actorScale = 1.7f;
        private bool _actorGrounded, _actorKnown;
        private Vector2 _actorLast;

        /// <summary>True while a host's portrait is shown as the 3D Gooby (the frame's HUD badge then stays off).</summary>
        protected bool GoobyFollowed => _actor != null && _actorPortrait != null;

        /// <summary>
        /// FieldW/FieldH before the field's first layout pass: the frame always lays the field out at its design box
        /// (<see cref="DesignSize"/>), so a 3D stage can place its pieces under the 2D tap targets right in BuildPlayField.
        /// </summary>
        protected void EnsureFieldSize()
        {
            if (FieldW > 0f && FieldH > 0f) return;
            var design = DesignSize(ViewSize.w / ViewSize.h);
            FieldW = design.x;
            FieldH = design.y;
        }

        /// <summary>
        /// Shows the player's 3D Gooby in place of <paramref name="portrait"/> (an element of the play field the host
        /// positions with style left/top). <paramref name="scale"/>: figure height over the portrait size;
        /// <paramref name="grounded"/>: feet on the portrait's lower edge and a walk cycle when it moves sideways (off for
        /// flying/bouncing figures, which are centred on it instead); <paramref name="fromBehind"/>: the view looks over his
        /// shoulder (he shows his back); <paramref name="restClip"/>: his loop between reactions (dance-loop, walk-loop …).
        /// </summary>
        protected void FollowGooby(VisualElement portrait, float scale = 1.7f, bool grounded = true, bool fromBehind = false, string restClip = null)
        {
            if (portrait == null || portrait.parent == null || !World.MgStage3D.Supported || Host == null || Host.Store == null) return;
            if (_actor == null)
            {
                try
                {
                    var outfit = Gooby.Core.Cosmetics.CosmeticsState.EquippedMap(J.Dict(Host.Store.State["cosmetics"]));
                    _actor = new World.MgBuddy3D(outfit, slot: 1) { Ambient = false };
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[mg] 3D Gooby for " + Id + ": " + e.Message);
                    _actor = null;
                    return;
                }
            }
            _actorView?.RemoveFromHierarchy();
            _actorPortrait = portrait;
            _actorScale = scale;
            _actorGrounded = grounded;
            _actorKnown = false;
            _actorView = new VisualElement { name = "mg-gooby-3d", pickingMode = PickingMode.Ignore };
            _actorView.style.position = Position.Absolute;
            portrait.parent.Add(_actorView);
            _actorView.PlaceInFront(portrait);
            portrait.style.opacity = 0f;
            var size = PortraitSize(portrait);
            _actorView.style.width = size * scale * 0.8f;
            _actorView.style.height = size * scale;
            _actor.Bind(_actorView, World.MgBuddy3D.Framing.Full, 0.8f, Mathf.Clamp(Mathf.RoundToInt(size * scale * 2.2f), 128, 512));
            _actor.BaseYaw = fromBehind ? 180f : 0f;
            _actor.RestClip = string.IsNullOrEmpty(restClip) ? "idle-loop" : restClip;
            _actor.Rest("happy");
            _actor.Settle();
            SyncActor(0f);
        }

        private static float PortraitSize(VisualElement p)
        {
            var w = p.style.width;
            if (w.keyword == StyleKeyword.Undefined && w.value.unit == LengthUnit.Pixel && w.value.value > 1f) return w.value.value;
            var r = p.layout.width;
            return float.IsNaN(r) || r <= 1f ? 64f : r;
        }

        private static float Px(StyleLength l, float fallback) =>
            l.keyword == StyleKeyword.Undefined && l.value.unit == LengthUnit.Pixel ? l.value.value : fallback;

        /// <summary>Per frame after the host's render: the figure follows the portrait (also once right after binding).</summary>
        private void SyncActor(float dt)
        {
            if (_actor == null || _actorPortrait == null || _actorView == null) return;
            var p = _actorPortrait;
            if (p.panel == null || p.resolvedStyle.display == DisplayStyle.None || p.style.display == DisplayStyle.None)
            {
                _actorView.style.display = DisplayStyle.None;
                return;
            }
            _actorView.style.display = DisplayStyle.Flex;
            // hosts that blink or fade the portrait would bring the flat face back over the figure
            if (p.style.opacity != 0f) p.style.opacity = 0f;
            var size = PortraitSize(p);
            var x = Px(p.style.left, p.layout.x);
            var y = Px(p.style.top, p.layout.y);
            var tr = p.style.translate;
            if (tr.keyword == StyleKeyword.Undefined)
            {
                if (tr.value.x.unit == LengthUnit.Pixel) x += tr.value.x.value;
                if (tr.value.y.unit == LengthUnit.Pixel) y += tr.value.y.value;
            }
            if (float.IsNaN(x) || float.IsNaN(y)) return;
            var w = size * _actorScale * 0.8f;
            var h = size * _actorScale;
            var cx = x + size * 0.5f;
            // the figure's body centre sits 59 % down its frame, its feet 5 % above the frame's bottom (MgBuddy3D full framing)
            var top = _actorGrounded ? y + size - h * 0.95f : y + size * 0.5f - h * 0.59f;
            _actorView.style.width = w;
            _actorView.style.height = h;
            _actorView.style.left = cx - w * 0.5f;
            _actorView.style.top = top;
            _actorView.style.rotate = p.style.rotate;
            _actorView.style.scale = p.style.scale;
            if (dt > 0f)
            {
                var pos = new Vector2(cx, y);
                if (_actorKnown)
                {
                    var vx = (pos.x - _actorLast.x) / Mathf.Max(dt, 1e-3f);
                    _actor.Locomote(_actorGrounded && Mathf.Abs(vx) > 40f, Mathf.Sign(vx));
                }
                _actorLast = pos;
                _actorKnown = true;
            }
        }

        private void DropActor()
        {
            if (_actorPortrait != null) _actorPortrait.style.opacity = StyleKeyword.Null;
            _actorView?.RemoveFromHierarchy();
            _actor?.Destroy();
            _actor = null;
            _actorPortrait = _actorView = null;
            _actorKnown = false;
        }
    }
}
