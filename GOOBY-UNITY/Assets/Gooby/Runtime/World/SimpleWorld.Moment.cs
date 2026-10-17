using Gooby.Core.Life;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// A strong feeling on stage (port of moment_regie.gd): the camera punches in on Gooby — a narrower lens and a slight
    /// turn towards his head so the zoom never crops him off — time breathes slow for a moment, and the vignette glows in
    /// the feeling's colour. Lens, view, time scale and vignette come back exactly; a camera cut (room change) mid-way
    /// ends the moment without undoing the new view. Reduced motion keeps only the colour.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private float _momentT = -1f;
        private float _momentLastAt = -1000f;
        private float _momentFov, _momentVignette;
        private Vector3 _momentCamPos;
        private Quaternion _momentCamRot;
        private Color _momentTint, _momentVignetteColor;
        private bool _momentSlow;

        public bool MomentRunning => _momentT >= 0f;

        /// <summary>Stages <paramref name="emotion"/> when it is a strong one and the last moment is 45 s ago.</summary>
        public bool PlayMoment(string emotion)
        {
            if (!MomentRegie.Stark(emotion) || Camera == null || Look?.Vignette == null || _momentT >= 0f || _glideT >= 0f) return false;
            var now = Time.unscaledTime;
            if (!MomentRegie.Bereit(_momentLastAt, now)) return false;
            _momentLastAt = now;
            _momentT = 0f;
            _momentFov = Camera.fieldOfView;
            _momentCamPos = Camera.transform.position;
            _momentCamRot = Camera.transform.rotation;
            _momentVignette = Look.Vignette.intensity.value;
            _momentVignetteColor = Look.Vignette.color.value;
            _momentTint = ArtLibrary.Hex(MomentRegie.Farbe(emotion));
            _momentSlow = !ReducedMotion;
            if (_momentSlow) Time.timeScale = (float)MomentRegie.Zeitlupe;
            Debug.Log("[seele] moment " + emotion);
            return true;
        }

        private void TickMoment(float dt)
        {
            if (_momentT < 0f) return;
            _momentT += dt;
            var cut = (Camera.transform.position - _momentCamPos).sqrMagnitude > 1e-6f;
            if (_momentSlow && (_momentT >= MomentRegie.ZeitlupeS || cut)) EndSlowMotion();
            var w = (float)MomentRegie.Gewicht(_momentT);
            if (cut || _momentT >= MomentRegie.DauerS)
            {
                if (!cut)
                {
                    Camera.fieldOfView = _momentFov;
                    Camera.transform.rotation = _momentCamRot;
                }
                Look.Vignette.intensity.Override(_momentVignette);
                Look.Vignette.color.Override(_momentVignetteColor);
                _momentT = -1f;
                return;
            }
            if (!ReducedMotion)
            {
                Camera.fieldOfView = _momentFov * (float)MomentRegie.Zoom(_momentT);
                if (Gooby != null)
                {
                    var head = Gooby.position + Vector3.up * 0.55f;
                    var toward = Quaternion.LookRotation(head - _momentCamPos, Vector3.up);
                    Camera.transform.rotation = Quaternion.Slerp(_momentCamRot, toward, (float)MomentRegie.Blick * w);
                }
            }
            Look.Vignette.intensity.Override(_momentVignette + 0.16f * w);
            Look.Vignette.color.Override(Color.Lerp(_momentVignetteColor, _momentTint, w));
        }

        private void EndSlowMotion()
        {
            _momentSlow = false;
            if (Mathf.Approximately(Time.timeScale, (float)MomentRegie.Zeitlupe)) Time.timeScale = 1f;
        }
    }
}
