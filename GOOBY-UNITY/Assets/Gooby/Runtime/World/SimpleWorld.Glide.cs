using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// A soft camera glide inside a place (the market stall sheet and the market day look at the own stall, then back to
    /// the door view). Reduced motion cuts straight to the target.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private Vector3 _glideFrom, _glideTo;
        private Quaternion _glideRotFrom, _glideRotTo;
        private float _glideT = -1f, _glideDur, _glideFovFrom, _glideFovTo;

        /// <summary>Glides the camera to a pose; a positive <paramref name="fov"/> zooms along, otherwise the lens stays.</summary>
        public void GlideCamera(Vector3 pos, Quaternion rot, float seconds, float fov = -1f)
        {
            if (Camera == null) return;
            var toFov = fov > 0f ? fov : Camera.fieldOfView;
            if (ReducedMotion || seconds <= 0.01f)
            {
                Camera.transform.SetPositionAndRotation(pos, rot);
                Camera.fieldOfView = toFov;
                _glideT = -1f;
                return;
            }
            _glideFrom = Camera.transform.position;
            _glideRotFrom = Camera.transform.rotation;
            _glideFovFrom = Camera.fieldOfView;
            _glideTo = pos;
            _glideRotTo = rot;
            _glideFovTo = toFov;
            _glideDur = seconds;
            _glideT = 0f;
        }

        /// <summary>Back to the place's door view.</summary>
        public void GlideToDoorView(float seconds)
        {
            if (_interior != null) GlideCamera(_interior.CameraPosition, _interior.CameraRotation, seconds);
        }

        private void TickGlide(float dt)
        {
            if (_glideT < 0f || Camera == null) return;
            _glideT = Mathf.Min(1f, _glideT + dt / Mathf.Max(0.01f, _glideDur));
            var k = Mathf.SmoothStep(0f, 1f, _glideT);
            Camera.transform.SetPositionAndRotation(Vector3.Lerp(_glideFrom, _glideTo, k), Quaternion.Slerp(_glideRotFrom, _glideRotTo, k));
            Camera.fieldOfView = Mathf.Lerp(_glideFovFrom, _glideFovTo, k);
            if (_glideT >= 1f) _glideT = -1f;
        }
    }
}
