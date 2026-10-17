using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Motion of a streamed vehicle GLB (tools/blender/build_vehicles.py: root → "body" + "wheel_fl/fr/rl/rr").
    /// The anchor it sits on is placed by the drive model or the traffic loops; the rig reads how that anchor moved
    /// since the last frame and animates everything the placement leaves stiff: the wheels spin with the speed, the
    /// front pair steers, the body rolls out of turns and pitches under braking, and the tail lamps light up when
    /// the car slows (the hero car also feeds its brake pedal in). At night the head and tail lamps glow.
    /// Nothing here moves the anchor, so the gameplay position and heading stay exactly the model's.
    /// </summary>
    public sealed class VehicleRig : MonoBehaviour
    {
        /// <summary>Set by the town's time of day: head lamps and tail lamps of every car glow.</summary>
        public static bool Night { get; set; }

        /// <summary>Hero car: the brake pedal (lamps light even before the deceleration shows).</summary>
        public bool Braking;

        /// <summary>Hero car: a thin exhaust trail, thicker when pulling away (the ambient cars do without).</summary>
        public bool Exhaust;
        private ParticleSystem _exhaust;

        private const float WheelBaseM = 2.1f;
        private const float MaxSteerDeg = 30f;
        private const float MaxSpeedMps = 40f;        // anything faster is a teleport, not a move
        private const float TeleportM = 6f;

        private Transform _body;
        private readonly List<Transform> _wheels = new List<Transform>();
        private readonly List<float> _radius = new List<float>();
        private readonly List<bool> _front = new List<bool>();
        private readonly List<float> _spin = new List<float>();
        private readonly List<Material> _tail = new List<Material>();
        private readonly List<Material> _head = new List<Material>();

        private bool _hasLast;
        private Vector3 _lastPos;
        private float _lastYaw, _lastSpeed;
        private float _speed, _accel, _yawRate, _steerDeg, _pitch, _roll;
        private float? _steerInput;
        private int _lampState = -1;
        private int _settle;

        /// <summary>Signed speed along the car's forward axis (m/s), as inferred from the anchor's movement.</summary>
        public float Speed => _speed;

        /// <summary>Hero car: steering input −1..1; ambient cars infer it from their yaw rate.</summary>
        public void SetSteer(float steer) => _steerInput = Mathf.Clamp(steer, -1f, 1f);

        private void OnEnable()
        {
            // a car switched on again (traffic schedule, scene re-entry) starts from rest, not from a 200 m jump
            _hasLast = false;
            _speed = _accel = _yawRate = 0f;
        }

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            // the GLB streams in within the first seconds: look for its nodes now and then until they are there
            if (_wheels.Count == 0 && ((++_settle % 10) != 0 || !Bind())) return;

            var pos = transform.position;
            var yaw = transform.eulerAngles.y;
            if (!_hasLast || (pos - _lastPos).sqrMagnitude > TeleportM * TeleportM)
            {
                _lastPos = pos; _lastYaw = yaw; _lastSpeed = 0f; _hasLast = true;
                _speed = _accel = _yawRate = 0f;
            }
            var v = Mathf.Clamp(Vector3.Dot(pos - _lastPos, transform.forward) / dt, -MaxSpeedMps, MaxSpeedMps);
            var yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
            var accel = (v - _lastSpeed) / dt;
            _lastPos = pos; _lastYaw = yaw; _lastSpeed = v;

            // the readings are per-frame differences of a stepped simulation; a little low-pass keeps the body from buzzing
            var kFast = 1f - Mathf.Exp(-14f * dt);
            var kSlow = 1f - Mathf.Exp(-6f * dt);
            _speed = Mathf.Lerp(_speed, v, kFast);
            _yawRate = Mathf.Lerp(_yawRate, yawRate, kFast);
            _accel = Mathf.Lerp(_accel, Mathf.Clamp(accel, -14f, 10f), kSlow);

            TickWheels(dt);
            TickBody(dt);
            TickLamps();
            if (_exhaust != null)
                Effects.SetRate(_exhaust, 1.5f + Mathf.Abs(_speed) * 0.35f + Mathf.Max(0f, _accel) * 2.5f);
        }

        private void TickWheels(float dt)
        {
            // Ackermann-ish: the front wheels point where the yaw rate says the car is going; the hero car's stick wins
            var targetSteer = _steerInput.HasValue
                ? _steerInput.Value * MaxSteerDeg
                : Mathf.Clamp(Mathf.Atan2(_yawRate * Mathf.Deg2Rad * WheelBaseM, Mathf.Max(1.5f, Mathf.Abs(_speed))) * Mathf.Rad2Deg, -MaxSteerDeg, MaxSteerDeg);
            _steerDeg = Mathf.Lerp(_steerDeg, targetSteer, 1f - Mathf.Exp(-10f * dt));
            for (var i = 0; i < _wheels.Count; i++)
            {
                var w = _wheels[i];
                if (w == null) continue;
                // +X rotation carries the top of the wheel towards +Z: forward
                _spin[i] = Mathf.Repeat(_spin[i] + _speed / _radius[i] * Mathf.Rad2Deg * dt, 360f);
                w.localRotation = Quaternion.Euler(_spin[i], _front[i] ? _steerDeg : 0f, 0f);
            }
        }

        private void TickBody(float dt)
        {
            if (_body == null) return;
            // braking dips the nose (+X), a hard pull-away lifts it; the body leans out of the bend (a left turn,
            // yaw rate < 0, rolls the roof towards +X = right)
            var pitch = Mathf.Clamp(-_accel * 0.5f, -2.2f, 3.6f);
            var roll = Mathf.Clamp(_yawRate * Mathf.Deg2Rad * _speed * 0.9f, -4.5f, 4.5f);
            var k = 1f - Mathf.Exp(-7f * dt);
            _pitch = Mathf.Lerp(_pitch, pitch, k);
            _roll = Mathf.Lerp(_roll, roll, k);
            _body.localRotation = Quaternion.Euler(_pitch, 0f, _roll);
        }

        // saturated reds: a hotter emission would tonemap the lamps to pink-white under the bloom
        private static readonly Color BrakeGlow = new Color(1f, 0.1f, 0.08f) * 1.8f;
        private static readonly Color NightTail = new Color(0.9f, 0.1f, 0.1f) * 1.1f;
        private static readonly Color NightHead = new Color(1f, 0.92f, 0.7f) * 2.2f;

        private void TickLamps()
        {
            // a real slow-down, not the jitter of a stepped simulation read at a few frames per second
            var braking = Braking || (_accel < -2.5f && Mathf.Abs(_speed) > 0.5f);
            var state = (braking ? 1 : 0) | (Night ? 2 : 0);
            if (state == _lampState) return;
            _lampState = state;
            foreach (var m in _tail) Emit(m, braking ? BrakeGlow : Night ? NightTail : Color.black);
            foreach (var m in _head) Emit(m, Night ? NightHead : Color.black);
        }

        private static void Emit(Material m, Color c)
        {
            if (m == null) return;
            if (c.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c); }
            else { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
        }

        /// <summary>Finds the model's nodes once the clone is under the anchor. False while it is still streaming.</summary>
        private bool Bind()
        {
            _wheels.Clear(); _radius.Clear(); _front.Clear(); _spin.Clear(); _tail.Clear(); _head.Clear();
            _body = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                if (t.name == "body") _body = t;
                else if (t.name.StartsWith("wheel_"))
                {
                    var r = t.GetComponentInChildren<Renderer>();
                    _wheels.Add(t);
                    _radius.Add(r != null ? Mathf.Max(0.12f, r.bounds.extents.y) : 0.32f);
                    _front.Add(t.name.Length > 6 && t.name[6] == 'f');
                    _spin.Add(0f);
                }
            }
            if (_wheels.Count == 0) return false;
            if (Exhaust && _exhaust == null) _exhaust = Effects.Exhaust(transform, new Vector3(-0.52f, 0.3f, -1.95f));
            if (_body != null)
            {
                // per-car material instances (CarPaint does the same, so both work on the one instanced set)
                foreach (var r in _body.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.materials)
                    {
                        if (m == null) continue;
                        if (m.name.StartsWith("gb_ruecklicht")) _tail.Add(m);
                        else if (m.name.StartsWith("gb_scheinwerfer")) _head.Add(m);
                    }
            }
            _lampState = -1;
            return true;
        }
    }
}
