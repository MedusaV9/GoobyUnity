using System;
using Gooby.Core.Garden;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The garage on the bed field (port of garage_prop.gd): the Blender body (`garage`), the roller door as its own
    /// GLB (`garage_door`, origin at the top edge — rolling up is a Y scale down to a slim band under the lintel) and
    /// the car in use parked inside, nose to the door. The door rolls itself when told to (<see cref="RollTo"/>); the
    /// drive-off sets door and car frame by frame from <see cref="GarageLogic.Abfahrt"/>.
    /// </summary>
    public sealed class GarageRig : MonoBehaviour
    {
        /// <summary>The Blender garage is 1.9 × 2.9 m; the field's cells are 0.5 m, so the 2×3 structure stands at half scale.</summary>
        public const float ModelScale = 0.5f;
        /// <summary>build_scenery.py: GARAGE_SLAB + GARAGE_TOR_H (door top) and −GARAGE_DOOR_Y (Blender front −Y → Unity +Z).</summary>
        public const float DoorTop = 1.56f;
        public const float DoorZ = 1.39f;
        public const float SlabTop = 0.06f;
        /// <summary>Rolled up, a band of the blade stays under the lintel (garage_prop.gd TOR_MIN_SCALE).</summary>
        public const float DoorRolledScale = 0.08f;
        /// <summary>The parked car's length and spot in model units (garage_prop.gd AUTO_LAENGE / AUTO_PARK_Z / ABFAHRT_STRECKE).</summary>
        public const float CarLength = 2.2f;
        public const float CarParkZ = -0.15f;
        public const float CarRunZ = 3.4f;
        /// <summary>Length of car_hero.glb (build_vehicles.py), the model every player car is dressed from.</summary>
        public const float HeroCarLength = 3.9f;

        public Transform Model, Door, Car;

        private float _open;
        private float _from, _to, _t = 1f;
        private Action _rolled;

        /// <summary>0 = down, 1 = rolled up.</summary>
        public float Open => _open;
        public bool Rolling => _t < 1f;

        /// <summary>Puts the door at <paramref name="open"/> at once (after a rebuild, during the drive-off).</summary>
        public void SetOpen(float open)
        {
            _open = Mathf.Clamp01(open);
            _t = 1f;
            _rolled = null;
            Apply();
        }

        /// <summary>Rolls the door from where it is to <paramref name="target"/> (eased, one roll); a new call mid-way turns it round.</summary>
        public void RollTo(float target, Action rolled = null)
        {
            _from = _open;
            _to = Mathf.Clamp01(target);
            _t = 0f;
            _rolled = rolled;
        }

        /// <summary>The car along its run out of the door: 0 parked … 1 out on the field.</summary>
        public void SetCarRun(float run)
        {
            if (Car != null) Car.localPosition = new Vector3(0f, SlabTop, CarParkZ + CarRunZ * Mathf.Clamp01(run));
        }

        /// <summary>World point just behind the car (a puff when the engine catches).</summary>
        public Vector3 Tailpipe => Car != null ? Car.TransformPoint(new Vector3(0f, 0.35f, -HeroCarLength * 0.5f)) : transform.position;

        public void Step(float dt)
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + dt / (float)GarageLogic.TorDauerS);
            _open = Mathf.Lerp(_from, _to, 0.5f - 0.5f * Mathf.Cos(Mathf.PI * _t));
            Apply();
            if (_t < 1f) return;
            var done = _rolled;
            _rolled = null;
            done?.Invoke();
        }

        private void Update() => Step(Time.deltaTime);

        private void Apply()
        {
            if (Door != null) Door.localScale = new Vector3(1f, Mathf.Lerp(1f, DoorRolledScale, _open), 1f);
        }
    }
}
