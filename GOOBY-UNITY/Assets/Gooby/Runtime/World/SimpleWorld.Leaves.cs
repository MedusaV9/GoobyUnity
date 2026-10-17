using System;
using System.Collections.Generic;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Fallen leaves on the lawn (<see cref="GardenLeaves"/> → <see cref="GardenLeavesView"/>): laid out from the save
    /// whenever the garden is staged, after building and when the day turns; hidden in build mode. A tap on a leaf or
    /// Gooby walking over it picks it (once per leaf and day). The HUD hears <see cref="LeafPicked"/> for its toast; the
    /// pills follow the "leaves" slice.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        public const float LeafPickRadius = 0.42f;

        private GardenLeavesView _leafView;
        private string _leafDay;
        private float _leafDayCheck;

        /// <summary>A leaf was picked: (leaves gained, golden).</summary>
        public event Action<int, bool> LeafPicked;

        public GardenLeavesView Leaves => _leafView;

        private void RefreshLeaves()
        {
            if (_garden == null || _gs == null) return;
            var def = RoomDefs.Get("garden");
            if (_leafView == null) _leafView = new GardenLeavesView(_garden.Root.transform, def, GardenOrigin);
            _leafDay = _gs.Clock.LocalDay();
            var lying = GardenLeaves.Lying(_gs, _leafDay, def?.GridW ?? GardenCellsX, def?.GridH ?? GardenCellsY, LeafFree());
            _leafView.Rebuild(GardenLeaves.Season(_leafDay), lying);
            _leafView.Root.SetActive(Build == null);
        }

        /// <summary>Lawn cells a leaf may lie on: walkable, not a bed, not right where Gooby steps onto the lawn.</summary>
        private Func<int, int, bool> LeafFree()
        {
            var grid = HomeState.LoadRoomGrid(_gs, "garden");
            var beds = new HashSet<(int, int)>(GardenField.BlockedCells(_gs));
            var spawn = GardenCellOf(GardenGoobyPos);
            return (x, y) =>
            {
                var c = new Cell(x, y);
                return grid.InBounds(c) && grid.Walkable(c) && !beds.Contains((x, y))
                       && (Math.Abs(x - spawn.X) > 1 || Math.Abs(y - spawn.Y) > 1);
            };
        }

        private static Cell GardenCellOf(Vector3 p) =>
            new Cell(Mathf.FloorToInt((p.x - GardenOrigin.x) / 0.5f), Mathf.FloorToInt((GardenOrigin.z + GardenCellsY * 0.5f - p.z) / 0.5f));

        /// <summary>Picks a lying leaf by id (tap, Gooby's feet, QA). False when it is gone or the day's leaves are used up.</summary>
        public bool PickLeaf(string id)
        {
            if (_leafView == null || _gs == null || Visiting) return false;
            var spot = _leafView.Find(id);
            if (spot == null) return false;
            var gained = GardenLeaves.Pick(_gs, _leafDay, spot.Value);
            var at = _leafView.Remove(id, fly: gained > 0 && !ReducedMotion);
            if (gained <= 0) return false;
            var golden = spot.Value.Golden;
            Effects.Sparkle(at + Vector3.up * 0.1f, golden ? 0.5f : 0.25f, golden ? 28 : 10);
            Sfx.Play(golden ? "game_star" : "game_coin", golden ? 0f : UnityEngine.Random.Range(1f, 4f), 0.8f);
            Debug.Log("[leaves] pick " + id + " +" + gained + (golden ? " golden" : "") + " → " + GardenLeaves.Count(_gs));
            LeafPicked?.Invoke(gained, golden);
            return true;
        }

        private void TickLeaves(float dt)
        {
            if (_leafView == null || !InGarden || Build != null || _garden == null || !_garden.Root.activeSelf) return;
            _leafView.Tick(dt, ReducedMotion);
            _leafDayCheck -= dt;
            if (_leafDayCheck <= 0f)
            {
                _leafDayCheck = 5f;
                if (_gs != null && _gs.Clock.LocalDay() != _leafDay) { RefreshLeaves(); return; }
            }
            if (Gooby == null || Visiting) return;
            var near = _leafView.Near(Gooby.position, LeafPickRadius);
            if (near != null) PickLeaf(near.Value.Id);
        }
    }
}
