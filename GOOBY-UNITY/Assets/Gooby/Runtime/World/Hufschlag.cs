using System;
using Gooby.Core.Audio;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The hoof beats of a ridden horse (ride_controller.gd _spiele_hufschlaege): two per stride at the gait's stride rate
    /// (<see cref="RideFeel.NickHz"/>), with the ground's sound, level and pitch (<see cref="RanchKlang.UntergrundInfo"/>),
    /// 6 dB softer at a walk, a little pitch jitter, silent while standing or in the air.
    /// </summary>
    public sealed class Hufschlag
    {
        private double _phase;

        /// <summary>Beats heard so far (QA).</summary>
        public int Beats { get; private set; }

        public void Tick(float dt, string gait, string untergrund, float stride = 1f, bool inAir = false)
        {
            if (dt <= 0f || string.IsNullOrEmpty(gait) || gait == "stand" || !RideFeel.NickHz.TryGetValue(gait, out var hz)) return;
            var before = _phase;
            _phase += hz * Mathf.Clamp(stride, 0.35f, 1.4f) * Math.Min(dt, 0.25f);
            if (_phase > 1000.0)
            {
                _phase -= 1000.0;
                before -= 1000.0;
            }
            if (inAir || Math.Floor(_phase * 2.0) == Math.Floor(before * 2.0)) return;
            var info = RanchKlang.UntergrundInfo(untergrund);
            var db = (float)info.VolDb - (gait == "schritt" ? 6f : 0f);
            var pitch = Mathf.Max(0.5f, (float)info.Pitch + UnityEngine.Random.Range(-0.05f, 0.05f));
            Sfx.Play(info.Sound, 12f * Mathf.Log(pitch, 2f), Mathf.Min(1f, Mathf.Pow(10f, db / 20f)));
            if (++Beats == 1) Debug.Log("[huf] " + gait + " auf " + untergrund + " → " + info.Sound);
        }
    }
}
