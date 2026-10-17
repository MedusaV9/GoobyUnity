using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The world side of the HUD eye (port of interaction_spotlight.gd's rim pulse): every interactable piece of the room
    /// brightens towards white in a slow pulse through a property block on its own renderers — the shared materials stay
    /// untouched and clearing hands every renderer back exactly as it was.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private readonly List<(Renderer renderer, int index, Color baseColor)> _glow = new List<(Renderer, int, Color)>();
        private static MaterialPropertyBlock _glowBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>A tap on a piece without a ray (the eye's badge): a second tap on the running shower rinses, on the TV zaps.</summary>
        public void TapMarker(FurnitureMarker m)
        {
            if (m == null || Room == null) return;
            if (Routine == Interactables.Dusche && _routineAt == m) { FinishShower(); return; }
            if (Routine == Interactables.Fernseher && _routineAt == m) { Tv?.Zap(); Sfx.PlayUi("soft_tick"); return; }
            FurnitureTapped?.Invoke(m, m.Bounds.center);
        }

        /// <summary>A pat without a ray (the eye's heart over Gooby).</summary>
        public void PokeGooby()
        {
            if (Gooby != null && Gooby.gameObject.activeInHierarchy && !GoobyAway) GoobyTapped?.Invoke();
        }

        /// <summary>The interactable pieces of the staged room (empty in the garden or without a room).</summary>
        public List<FurnitureMarker> InteractMarkers()
        {
            var outp = new List<FurnitureMarker>();
            if (Room == null || InGarden) return outp;
            foreach (var m in Room.Markers()) if (m != null) outp.Add(m);
            return outp;
        }

        public void SetInteractGlow(IEnumerable<FurnitureMarker> markers)
        {
            ClearInteractGlow();
            if (Room == null) return;
            foreach (var m in markers)
            {
                var view = Room.ViewOf(m.Uid);
                if (view == null) continue;
                foreach (var r in view.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (var i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].HasProperty(BaseColorId)) _glow.Add((r, i, mats[i].GetColor(BaseColorId)));
                }
            }
        }

        /// <summary>0 = the piece's own colour lifted a little, 1 = the brightest beat of the pulse.</summary>
        public void PulseInteractGlow(float beat)
        {
            _glowBlock ??= new MaterialPropertyBlock();
            var lift = 0.12f + 0.2f * Mathf.Clamp01(beat);
            foreach (var (r, i, c) in _glow)
            {
                if (r == null) continue;
                _glowBlock.Clear();
                _glowBlock.SetColor(BaseColorId, Color.Lerp(c, Color.white, lift));
                r.SetPropertyBlock(_glowBlock, i);
            }
        }

        public void ClearInteractGlow()
        {
            _glowBlock ??= new MaterialPropertyBlock();
            _glowBlock.Clear();
            foreach (var (r, i, _) in _glow)
                if (r != null) r.SetPropertyBlock(_glowBlock, i);
            _glow.Clear();
        }
    }
}
