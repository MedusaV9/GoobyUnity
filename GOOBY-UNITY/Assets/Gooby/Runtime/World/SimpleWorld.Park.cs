using Gooby.Core.Time;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>The walkable Funkelpark as a stage of the world (FunkelparkScene): entering from the street, walking, rides, camera.</summary>
    public sealed partial class SimpleWorld
    {
        private static readonly Color ParkSky = new Color(0.72f, 0.86f, 0.98f);
        private FunkelparkScene _park;
        private bool _parkFromStreet;
        private bool _parkWalking;
        private float _parkSpeed, _parkStep;
        private Vector3 _parkDir = Vector3.forward;
        private bool _parkCamSnap;

        /// <summary>The park on stage (null elsewhere).</summary>
        public FunkelparkScene Park => _park;

        /// <summary>Walks through the park gate: the street hides, the park is built far off, Gooby stands inside the gate. Call inside a fade.</summary>
        public void EnterPark()
        {
            if (_park != null) return;
            _parkFromStreet = _city != null && _city.Root.activeSelf;
            _interior?.Destroy();
            _interior = null;
            _city?.Root.SetActive(false);
            _park = new FunkelparkScene(_root.transform);
            SceneDressing.SetIndoor(Look, false);
            SceneDressing.SetWindowCookie(Look, false);
            SceneDressing.SetFocus(Look, 28f, 80f);
            Camera.backgroundColor = ApplyTimeOfDay(ParkSky);
            PlaceGoobyAt(FunkelparkScene.Origin + FunkelparkScene.Entrance, "idle-loop");
            if (Gooby != null) Gooby.rotation = Quaternion.identity;
            _parkWalking = false;
            _parkSpeed = 0f;
            _parkCamSnap = true;
        }

        /// <summary>Back out through the gate onto the street (the car waits); Gooby returns to his spot at home.</summary>
        public void LeavePark()
        {
            if (_park == null) return;
            if (_park.Riding) _park.StopRide();
            _park.Destroy();
            _park = null;
            PlaceGooby(InGarden);
            if (_parkFromStreet && _city != null) SetCityScene(true);
            else if (InGarden) { EnsureGarden(); _buildCam = 0; ApplyBuildCamera(); }
            else SetHomeCamera();
        }

        /// <summary>Starts a ride with Gooby on board; the camera cuts to the ride's shot.</summary>
        public bool StartParkRide(string id)
        {
            if (_park == null || Gooby == null || !_park.StartRide(id, Gooby)) return false;
            if (_parkWalking) { _parkWalking = false; Avatar?.Play("sit-loop"); }
            else Avatar?.Play("sit-loop");
            Avatar?.SetEmotion("ecstatic");
            _parkSpeed = 0f;
            _parkCamSnap = true;
            return true;
        }

        /// <summary>Ends the ride ("Aussteigen"); Gooby stands at the ride's entrance.</summary>
        public void StopParkRide()
        {
            if (_park == null || !_park.Riding) return;
            _park.StopRide();
        }

        /// <summary>After a ride (the scene's RideEnded): back on his feet, the camera follows him again.</summary>
        public void ParkRideFinished()
        {
            Avatar?.Play("idle-loop");
            Avatar?.SetEmotion("happy");
            _parkCamSnap = true;
        }

        private void TickPark(float dt)
        {
            if (_park == null) return;
            if (_park.Riding)
            {
                // on the bumper car the stick steers the car (screen up = +Z, like walking)
                _park.SetDrive(new Vector2(_move.x, -_move.y));
            }
            else WalkPark(dt);
            _park.Tick(dt, ReducedMotion);
            if (Gooby == null || Camera == null) return;
            var (pos, rot, fov) = _park.CameraFor(Gooby.position);
            if (_parkCamSnap || ReducedMotion)
            {
                _parkCamSnap = false;
                Camera.transform.SetPositionAndRotation(pos, rot);
                Camera.fieldOfView = fov;
                return;
            }
            var k = 1f - Mathf.Exp(-(_park.Riding ? 9f : 5f) * dt);
            Camera.transform.position = Vector3.Lerp(Camera.transform.position, pos, k);
            Camera.transform.rotation = Quaternion.Slerp(Camera.transform.rotation, rot, k);
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, fov, k);
        }

        private void WalkPark(float dt)
        {
            if (Gooby == null) return;
            var stick = _move.sqrMagnitude >= 0.01f;
            var dir = stick ? new Vector3(_move.x, 0f, -_move.y) : Vector3.zero;
            var target = 2.8f * Mathf.Clamp01(dir.magnitude);
            _parkSpeed = Mathf.MoveTowards(_parkSpeed, target, (target > _parkSpeed ? 9f : 12f) * dt);
            if (stick) _parkDir = dir.normalized;
            if (_parkSpeed <= 0.02f)
            {
                if (_parkWalking) { _parkWalking = false; _parkSpeed = 0f; Avatar?.Play("idle-loop"); }
                return;
            }
            var next = _park.ClampWalk(Gooby.position, Gooby.position + _parkDir * _parkSpeed * dt);
            Gooby.position = next;
            Gooby.rotation = Quaternion.Slerp(Gooby.rotation, Quaternion.LookRotation(_parkDir), 1f - Mathf.Exp(-12f * dt));
            if (!_parkWalking) { _parkWalking = true; Avatar?.Play("walk-loop"); }
            Avatar?.SetClipSpeed("walk-loop", Mathf.Lerp(0.6f, 1.15f, _parkSpeed / 2.8f));
            _parkStep += _parkSpeed * dt;
            if (_parkStep >= 0.45f)
            {
                _parkStep = 0f;
                Effects.Puff(Gooby.position - _parkDir * 0.12f + Vector3.up * 0.04f, new Color(0.86f, 0.78f, 0.66f, 0.8f), 3, 0.18f, 0.45f, 0.55f, -_parkDir * 0.6f + Vector3.up, 0.7f);
                global::Gooby.Unity.App.Sfx.Play("step_tap", UnityEngine.Random.Range(-1.2f, 1.2f), 0.8f);
            }
        }
    }
}
