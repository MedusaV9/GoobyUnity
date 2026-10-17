using System;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The shake secret (port of shake_secret.gd): at home in a room the phone's accelerometer feeds <see cref="ShakeLogic"/>
    /// and the house answers in three stages — 1 it wobbles (a small camera shake, dust trickling from the ceiling, a rumble,
    /// Gooby startled), 2 harder (Gooby ducks and grips the floor), 3 he is flung through the room flailing and complains,
    /// back to happy after five seconds. Reduced motion keeps the camera and Gooby still. Stage 1 counts the episode
    /// (achievements counter "shakes") and wakes him from a crate nap, stage 3 counts "shakeStage3" (the secret sticker
    /// "Ganz blümerant") and pauses the secret for ten minutes. Never in build mode, on a visit, in the garden or while he
    /// sleeps in bed.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private const float ShakeComplainSec = 5f, ShakeHopSec = 0.45f;

        private readonly ShakeLogic _shakeLogic = new ShakeLogic();
        private int _shakeStage;
        private long _shakeStage3Ms;
        private bool _shakeBusy, _shakeGrip;
        private float _wobbleLeft, _wobbleStrength, _wobbleTick;
        private Vector3 _wobbleOffset, _wobbleWritten;

        /// <summary>A line for the HUD bubble (shake.stufe1 / stufe2 / schrei / beschwerde / wieder_gut).</summary>
        public event Action<string> ShakeLine;

        /// <summary>QA: synthetic samples in m/s² instead of the phone's accelerometer.</summary>
        public Func<Vector3> ShakeSource { get; set; }

        /// <summary>Gooby is held by the shake (gripping the floor or in the air): no walking, no life of his own.</summary>
        public bool ShakeHold => _shakeBusy || _shakeGrip;

        public int ShakeStage => _shakeStage;

        private bool ShakeAllowed => ShakeBlockers().Length == 0;

        /// <summary>What keeps the secret quiet right now ("" = it listens); QA prints it.</summary>
        public string ShakeBlockers()
        {
            var b = new System.Text.StringBuilder();
            if (Gooby == null || Avatar == null) b.Append("nogooby ");
            if (GoobyAway) b.Append("away ");
            if (Visiting) b.Append("visiting ");
            if (Build != null) b.Append("build ");
            if (_interior != null || _park != null || _vacation != null || (_city != null && _city.Root.activeSelf)) b.Append("outside ");
            if (Room == null || InGarden) b.Append("noroom ");
            if (Feeding) b.Append("feeding ");
            if (_care.Sleeping) b.Append("sleeping ");
            return b.ToString().Trim();
        }

        private void TickShake(float dt)
        {
            TickWobble(dt);
            if (_shakeBusy) { TickStageThree(dt); return; }
            if (!ShakeAllowed)
            {
                if (_shakeStage > 0) CalmDown();
                return;
            }
            var now = _gs?.Clock.NowMs() ?? 0L;
            if (!ShakeLogic.CooldownReady(_shakeStage3Ms, now))
            {
                _shakeLogic.Reset();
                return;
            }
            Vector3 a;
            if (ShakeSource != null) a = ShakeSource();
            else if (SystemInfo.supportsAccelerometer) a = Input.acceleration * 9.81f;
            else return;
            var stage = _shakeLogic.Feed(a.x, a.y, a.z, dt);
            var reached = _shakeStage;
            while (reached < stage)
            {
                reached++;
                _shakeStage = reached;
                EnterShakeStage(reached, now);
                if (_shakeBusy) return;
            }
            if (stage == 0 && _shakeStage > 0) CalmDown();
        }

        private void EnterShakeStage(int stage, long now)
        {
            Debug.Log("[shake] stage " + stage);
            switch (stage)
            {
                case 1:
                    // a nap in the crate is over: he startles awake in it
                    if (InCrate) WakeInCrate(30f);
                    BumpCounter("shakes");
                    Sfx.Play("amb_donner", 6f, 0.55f);
                    Wobble(0.035f, 0.3f);
                    Dust(24);
                    Avatar?.SetEmotion("scared");
                    ShakeLine?.Invoke("shake.stufe1");
                    break;
                case 2:
                    Sfx.Play("amb_donner", 3f, 0.8f);
                    Sfx.Play("game_hit", -4f, 0.5f);
                    Wobble(0.07f, 0.45f);
                    Dust(40);
                    _walkTarget = null;
                    _move = Vector2.zero;
                    Avatar?.SetEmotion("scared");
                    Avatar?.Play("grip_floor-loop", 0.15f);
                    _shakeGrip = true;
                    ShakeLine?.Invoke("shake.stufe2");
                    break;
                case 3:
                    BumpCounter("shakeStage3");
                    _shakeStage3Ms = now;
                    _shakeLogic.Reset();
                    BeginStageThree();
                    break;
            }
        }

        private int _flightHop = -1;
        private float _flightT, _complainLeft, _flightSpin;
        private Vector3 _flightFrom, _flightTo, _flightStart;

        /// <summary>Flung through the room in three hops (reduced motion: only the complaint), then complaining, then happy again.</summary>
        private void BeginStageThree()
        {
            _shakeBusy = true;
            _shakeGrip = false;
            if (!ReducedMotion && Gooby != null)
            {
                ShakeLine?.Invoke("shake.schrei");
                Sfx.Play("pet_squish", 4f, 0.9f);
                Wobble(0.1f, 0.6f);
                Avatar?.Play("ragdoll_flail-loop", 0.1f);
                _flightStart = Gooby.position;
                NextHop(0);
                return;
            }
            _flightHop = -1;
            ShakeLine?.Invoke("shake.beschwerde");
            _complainLeft = ShakeComplainSec;
        }

        private void NextHop(int hop)
        {
            _flightHop = hop;
            _flightT = 0f;
            _flightFrom = Gooby.position;
            _flightTo = hop < 2
                ? _flightStart + new Vector3(UnityEngine.Random.Range(-1.4f, 1.4f), UnityEngine.Random.Range(0.9f, 1.8f), UnityEngine.Random.Range(-1f, 1f))
                : _flightStart;
            _flightSpin = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        }

        private void TickStageThree(float dt)
        {
            if (_flightHop >= 0)
            {
                if (Gooby == null) { _flightHop = -1; _complainLeft = ShakeComplainSec; return; }
                _flightT += dt;
                Gooby.position = Vector3.Lerp(_flightFrom, _flightTo, Mathf.SmoothStep(0f, 1f, _flightT / ShakeHopSec));
                Gooby.rotation = Quaternion.Euler(0f, Gooby.eulerAngles.y + _flightSpin * 540f * dt, 0f);
                if (_flightT < ShakeHopSec) return;
                Gooby.position = _flightTo;
                Sfx.Play(_flightHop < 2 ? "toy_boing" : "toy_knall", UnityEngine.Random.Range(-1f, 2f), 0.7f);
                if (_flightHop < 2) { NextHop(_flightHop + 1); return; }
                _flightHop = -1;
                Effects.LandingRing(Gooby.position, new Color(0.87f, 0.8f, 0.68f, 0.8f), 0.55f, 12);
                Avatar?.Play("idle-loop", 0.2f);
                Avatar?.SetEmotion("dizzy");
                ShakeLine?.Invoke("shake.beschwerde");
                _complainLeft = ShakeComplainSec;
                return;
            }
            _complainLeft -= dt;
            if (_complainLeft > 0f) return;
            Avatar?.SetEmotion("happy");
            Avatar?.Play("idle-loop", 0.25f);
            ShakeLine?.Invoke("shake.wieder_gut");
            _shakeBusy = false;
            _shakeStage = 0;
            Debug.Log("[shake] done, paused for 10 min");
        }

        /// <summary>The episode fades before stage 3: Gooby lets go of the floor and is himself again.</summary>
        private void CalmDown()
        {
            _shakeStage = 0;
            if (_shakeGrip) Avatar?.Play("idle-loop", 0.25f);
            _shakeGrip = false;
            Avatar?.SetEmotion("happy");
        }

        private void BumpCounter(string key)
        {
            if (_gs == null) return;
            _gs.Update(state =>
            {
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                counters[key] = J.Int(counters[key]) + 1;
            }, GameHost.TicksMs);
            _gs.NotifySliceChanged("achievements");
        }

        /// <summary>Dust trickling from the ceiling over Gooby (a few puffs spread over a 2 × 2 m patch).</summary>
        private void Dust(int amount)
        {
            if (ReducedMotion || Gooby == null) return;
            var c = Gooby.position;
            for (var i = 0; i < 5; i++)
            {
                var at = new Vector3(c.x + UnityEngine.Random.Range(-1.1f, 1.1f), PropPlaceholder.CeilingHeight - 0.05f, c.z + UnityEngine.Random.Range(-0.9f, 0.9f));
                Effects.Puff(at, new Color(0.87f, 0.8f, 0.68f, 0.75f), amount / 5, 0.06f, 0.7f, 1.6f, Vector3.down, 0.35f);
            }
        }

        private void Wobble(float strength, float seconds)
        {
            if (ReducedMotion || Camera == null) return;
            _wobbleStrength = Mathf.Max(_wobbleStrength, strength);
            _wobbleLeft = Mathf.Max(_wobbleLeft, seconds);
        }

        /// <summary>Additive camera jitter that survives whoever sets the camera's base pose (glides, moments).</summary>
        private void TickWobble(float dt)
        {
            if (Camera == null || (_wobbleLeft <= 0f && _wobbleOffset == Vector3.zero)) return;
            var next = Vector3.zero;
            if (_wobbleLeft > 0f)
            {
                _wobbleLeft -= dt;
                _wobbleTick -= dt;
                if (_wobbleTick <= 0f)
                {
                    _wobbleTick = 0.05f;
                    next = UnityEngine.Random.insideUnitSphere * _wobbleStrength;
                }
                else next = _wobbleOffset;
                if (_wobbleLeft <= 0f) { next = Vector3.zero; _wobbleStrength = 0f; }
            }
            var pos = Camera.transform.position;
            var basePos = pos == _wobbleWritten ? pos - _wobbleOffset : pos;
            _wobbleOffset = next;
            Camera.transform.position = basePos + next;
            _wobbleWritten = Camera.transform.position;
        }
    }
}
