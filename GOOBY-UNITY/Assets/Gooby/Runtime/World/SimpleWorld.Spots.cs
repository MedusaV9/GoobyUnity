using System;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Random;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Sticks and leaf bundles for the workbench on the bed field (<see cref="GardenSpots"/> → <see cref="GardenSpotsView"/>):
    /// spawned and grown back whenever the garden is staged and on the garden's minute tick, redrawn from the save after
    /// every bed change, hidden in build mode. A tap picks one into home.materials; the HUD hears <see cref="SpotPicked"/>.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private GardenSpotsView _spotView;
        private readonly GoobyRng _spotRng = new GoobyRng(Environment.TickCount);

        /// <summary>A find was picked: its material id ("stock" | "blatt").</summary>
        public event Action<string> SpotPicked;

        public GardenSpotsView Spots => _spotView;

        /// <summary>Lets missing finds spawn and picked ones grow back (writes the save), then redraws.</summary>
        private void GrowSpots()
        {
            if (_gs == null || Beds == null) return;
            GardenSpots.Refresh(_gs, NowS, _spotRng);
            RefreshSpots();
        }

        private void RefreshSpots()
        {
            if (_garden == null || _gs == null || Beds == null) return;
            if (_spotView == null) _spotView = new GardenSpotsView(_garden.Root.transform, Beds);
            _spotView.Rebuild(GardenSpots.Offene(_gs));
            _spotView.Root.SetActive(Build == null);
        }

        /// <summary>Picks the find on a bed cell (tap, QA). False when nothing lies there.</summary>
        public bool PickSpot(Cell at)
        {
            if (_gs == null || Visiting) return false;
            var material = GardenSpots.Sammeln(_gs, at, NowS);
            if (string.IsNullOrEmpty(material)) return false;
            var pos = _spotView != null ? _spotView.Remove(at, fly: !ReducedMotion) : Vector3.zero;
            if (pos != Vector3.zero) Effects.Sparkle(pos + Vector3.up * 0.12f, 0.25f, ReducedMotion ? 6 : 14);
            Sfx.Play("soft_collect", UnityEngine.Random.Range(-1f, 2f));
            Debug.Log("[spots] pick " + at.X + "," + at.Y + " " + material + " → " + CraftState.MaterialCount(_gs, material));
            SpotPicked?.Invoke(material);
            return true;
        }

        private void TickSpots(float dt)
        {
            if (_spotView == null || !InGarden || Build != null || _garden == null || !_garden.Root.activeSelf) return;
            _spotView.Tick(dt, ReducedMotion);
        }
    }
}
