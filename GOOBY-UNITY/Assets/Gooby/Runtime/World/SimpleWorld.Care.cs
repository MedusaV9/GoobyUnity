using System;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Gooby's care state made visible (port of pflege_runner.gd): every two seconds the save sets his body width from
    /// the weight, the pale look, sniffly nose, ice pack and eye rings (<see cref="CareLook"/>) and the pace of his
    /// waddle; while he is free at home a tired Gooby yawns now and then and a poorly one sneezes. It is his own body,
    /// so it reads the player's store even on a visit. Never blocking, never punishing.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private const float CareRefreshSec = 2f;
        private float _careRefresh, _yawnIn = -1f, _sneezeIn = -1f;
        private CareLook.Input _care;

        /// <summary>Walking pace from tiredness and sickness (1 = full speed).</summary>
        public float CareSpeed { get; private set; } = 1f;

        /// <summary>A yawn ("gaehnen") or a sneeze ("hatschi") just happened — the HUD may let Gooby say something.</summary>
        public event Action<string> CareMoment;

        private void TickCare(float dt)
        {
            if (Avatar == null || _gs == null) return;
            _careRefresh -= dt;
            if (_careRefresh <= 0f) RefreshCare();
            if (!LifeFree || CrateNapping || _care.Sleeping)
            {
                // nothing builds up while he is busy: the next one comes a while after he is free again
                if (_yawnIn < 4f) _yawnIn = 4f + UnityEngine.Random.Range(0f, 4f);
                if (_sneezeIn < 4f) _sneezeIn = 4f + UnityEngine.Random.Range(0f, 4f);
                return;
            }
            if (CareLook.Yawns(_care))
            {
                _yawnIn -= dt;
                if (_yawnIn <= 0f)
                {
                    _yawnIn = (float)CareLook.NextYawn(UnityEngine.Random.value);
                    Avatar.Yawn();
                    Sfx.Play("pet_squish", -6.2f, 0.75f);
                    Debug.Log("[care] yawn tired=" + _care.Tired.ToString("F2"));
                    CareMoment?.Invoke("gaehnen");
                }
            }
            if (CareLook.Sneezes(_care))
            {
                _sneezeIn -= dt;
                if (_sneezeIn <= 0f)
                {
                    _sneezeIn = (float)CareLook.NextSneeze(_care.Grade, UnityEngine.Random.value);
                    Avatar.Sneeze(ReducedMotion);
                    Sfx.Play("pet_squish", 7.6f, 0.85f);
                    Debug.Log("[care] sneeze grade=" + _care.Grade);
                    CareMoment?.Invoke("hatschi");
                }
            }
        }

        /// <summary>Reads the save now (every two seconds anyway; QA and the HUD call it right after a change).</summary>
        public void RefreshCare()
        {
            _careRefresh = CareRefreshSec;
            if (Avatar == null || _gs == null) return;
            var was = _care;
            _care = CareLook.Of(_gs.State);
            Avatar.SetCare(_care.Grade, (float)CareLook.PaleAmount(_care.Grade), CareLook.SniffleNose(_care.Grade),
                (float)CareLook.EyeBags(_care.Grade, _care.Tired, _care.Sleeping));
            // the width only while no scene squashes him (feeding, the door, the nougat lock keep their own scale)
            if (LifeFree || _care.Sleeping) Avatar.SetBodyWidth((float)Weight.BodyScale(_care.Weight));
            CareSpeed = (float)CareLook.SpeedMult(_care.Grade, _care.Tired);
            if (was.Grade != _care.Grade || Math.Abs(was.Weight - _care.Weight) >= 5.0)
                Debug.Log("[care] grade=" + _care.Grade + " tired=" + _care.Tired.ToString("F2") + " weight=" + _care.Weight.ToString("F0")
                    + " width=" + Avatar.BodyWidth.ToString("F2") + " speed=" + CareSpeed.ToString("F2"));
        }
    }
}
