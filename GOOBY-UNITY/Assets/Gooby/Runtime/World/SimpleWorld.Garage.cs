using System;
using Gooby.Core.Garden;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The garage on the lawn (port of garden_host.gd rolltor_toggle + garage_abfahrt.gd): a tap turns the roller door,
    /// and the way into town from the garden goes through it — Gooby hops in, the door rolls up, the engine catches,
    /// a toot, the car pulls out, then the city. The drive-off follows <see cref="GarageLogic.Abfahrt"/>.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private Action _garageThen;
        private float _garageT;
        private float _garageTorStart;
        private bool _garageStarted, _garageCaught, _garageTooted;
        /// <summary>The car has left; it is parked again once the city has the camera (<see cref="ParkGarageCar"/>).</summary>
        private bool _garageGone;

        /// <summary>Engine sound and toot along the drive-off (seconds from its start).</summary>
        private const float GarageStartAt = 0.3f, GarageCatchAt = 0.8f, GarageTootAt = 1.15f;

        /// <summary>A garage stands on the field and the lawn is on stage, so its drive-off can be watched.</summary>
        public bool GarageInView => InGarden && Build == null && _interior == null && Beds?.Garage != null && _garden != null && _garden.Root.activeSelf;

        /// <summary>The car is on its way out (the HUD ignores a second "Karte").</summary>
        public bool GarageLeaving => _garageThen != null;

        /// <summary>Where the garage's walls are on screen (QA taps it like a finger would), null without one.</summary>
        public Vector2? GarageScreenPoint()
        {
            if (!GarageInView || Camera == null) return null;
            var p = Camera.WorldToScreenPoint(Beds.Garage.transform.position + Vector3.up * 0.45f);
            return p.z > 0f ? new Vector2(p.x, p.y) : (Vector2?)null;
        }

        /// <summary>Tap on the garage or the sheet's button: the door rolls the other way (also mid-roll).</summary>
        public bool ToggleGarageDoor()
        {
            var rig = Beds?.Garage;
            if (rig == null || GarageLeaving) return false;
            Beds.GarageZustand = GarageLogic.Toggle(Beds.GarageZustand);
            rig.RollTo((float)GarageLogic.ZielAnteil(Beds.GarageZustand), () =>
            {
                if (Beds != null) Beds.GarageZustand = GarageLogic.Ende(Beds.GarageZustand);
            });
            Sfx.Play("garage_rolltor");
            return true;
        }

        /// <summary>
        /// Into town from the lawn: with the garage in view the car leaves through its door first, then
        /// <paramref name="then"/> runs (the city). Anywhere else, or with reduced motion, it runs at once.
        /// </summary>
        public void LeaveByGarage(Action then)
        {
            if (then == null || GarageLeaving) return;
            if (!GarageInView || ReducedMotion) { then(); return; }
            _garageThen = then;
            _garageT = 0f;
            _garageTorStart = Beds.Garage.Open;
            _garageStarted = _garageCaught = _garageTooted = _garageGone = false;
            Beds.GarageZustand = GarageLogic.Oeffnet;
            if (_garageTorStart < 0.99f) Sfx.Play("garage_rolltor");
            EndRoutine();
            _move = Vector2.zero;
            _walking = false;
            if (Gooby != null && Gooby.gameObject.activeSelf)
            {
                // he hops into the car: a puff where he stood
                Effects.Puff(Gooby.position + Vector3.up * 0.45f, Color.white, 10, 0.12f, 0.9f, 0.55f, Vector3.up, 1.2f);
                Effects.Sparkle(Gooby.position + Vector3.up * 0.5f, 0.35f, 12);
                Gooby.gameObject.SetActive(false);
            }
        }

        private void TickGarage(float dt)
        {
            if (_garageThen == null) return;
            _garageT += dt;
            if (_garageGone)
            {
                // whoever took over without opening the city still gets the lawn back after a moment
                if (_garageT > GarageLogic.AbfahrtDauerS(false) + 2.0) ParkGarageCar();
                return;
            }
            var rig = Beds?.Garage;
            var (tor, fahrt, fertig) = GarageLogic.Abfahrt(_garageT, _garageTorStart);
            if (rig != null)
            {
                rig.SetOpen((float)tor);
                rig.SetCarRun((float)fahrt);
                if (!_garageStarted && _garageT >= GarageStartAt) { _garageStarted = true; Sfx.Play("car_start"); }
                if (!_garageCaught && _garageT >= GarageCatchAt)
                {
                    _garageCaught = true;
                    Effects.Puff(rig.Tailpipe, new Color(0.92f, 0.92f, 0.95f), 6, 0.07f, 0.35f, 0.7f, Vector3.up, 0.9f);
                }
                if (!_garageTooted && _garageT >= GarageTootAt) { _garageTooted = true; Sfx.Play("car_hupe"); }
            }
            if (!fertig) return;
            _garageGone = true;
            _garageThen();
        }

        /// <summary>
        /// Behind the city (SetCityScene): the car back in its bay, the door down, Gooby back on the lawn for the way
        /// home. A no-op unless a drive-off ran.
        /// </summary>
        public void ParkGarageCar()
        {
            if (_garageThen == null) return;
            _garageThen = null;
            _garageGone = false;
            if (Beds != null)
            {
                Beds.GarageZustand = GarageLogic.Zu;
                Beds.Garage?.SetCarRun(0f);
                Beds.Garage?.SetOpen(0f);
            }
            RefreshAway();
        }
    }
}
