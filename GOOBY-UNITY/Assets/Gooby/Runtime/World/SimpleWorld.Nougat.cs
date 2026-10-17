using System;
using Gooby.Core.Home;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The Nougatschleuse sequence (port of nougatschleuse.gd's crank-and-dollop, ~2.8 s): Gooby waddles under the
    /// spout, cranks three squeaks on a rising pitch with a squash each, a dollop of nougat drops from the spout onto
    /// his snout (splat, nom), he hops — then <see cref="StartNougat"/>'s callback books the effects.
    /// </summary>
    public sealed partial class SimpleWorld
    {
        private static readonly float[] CrankAt = { 0.05f, 0.75f, 1.45f };
        private static readonly float[] CrankPitch = { -4f, -1.5f, 1f };
        private const float DollopAt = 2.1f, DollopFall = 0.32f, NougatEnd = 2.8f;

        private Action _nougatDone;
        private int _nougatStep;
        private bool _nougatSplat;
        private Transform _dollop;
        private Vector3 _dollopFrom, _goobyScale;
        private (Vector3 pos, Quaternion rot, float fov)? _preNougatCam;
        private float _nougatCamBackIn;

        public bool StartNougat(FurnitureMarker m, Action done)
        {
            if (Routine != RoutineNone || m == null || Gooby == null) return false;
            _nougatDone = done;
            _nougatStep = 0;
            _nougatSplat = false;
            _goobyScale = Gooby.localScale;
            SetRoutine(Interactables.Nougat, m);
            WalkGoobyTo(ApproachPoint(m, 0.2f), () =>
            {
                FaceTowards(m.Bounds.center);
                Avatar?.SetEmotion("ecstatic");
                Avatar?.Play("idle-loop");
                NougatCloseUp(m);
            }, 5f);
            return true;
        }

        /// <summary>The room camera leans in at a three-quarter angle on the chute and Gooby under it; it glides back a moment after the nom.</summary>
        private void NougatCloseUp(FurnitureMarker m)
        {
            if (Camera == null || Gooby == null || Room == null) return;
            _nougatCamBackIn = 0f;
            if (!_preNougatCam.HasValue) _preNougatCam = (Camera.transform.position, Camera.transform.rotation, Camera.fieldOfView);
            var front = m.Front;
            var side = Vector3.Cross(Vector3.up, front);
            var focus = Vector3.Lerp(m.Bounds.center, Gooby.position + Vector3.up * 0.55f, 0.5f);
            var o = Room.Origin;
            // the room has no south wall (the dollhouse cut-away): on a side wall the lens stays south of the chute and
            // looks north-ish when there is room for it, otherwise it swings out towards the middle of the room
            if (Mathf.Abs(side.z) > 0.5f && focus.z - o.z > 1.6f) { if (side.z > 0f) side = -side; }
            else if (Vector3.Dot(Room.Center - focus, side) < 0f) side = -side;
            var eye = focus + front * 2.4f + side * 1.9f + Vector3.up * 1.0f;
            eye.x = Mathf.Clamp(eye.x, o.x + 0.5f, o.x + Room.WidthM - 0.5f);
            eye.z = Mathf.Clamp(eye.z, o.z + 0.5f, o.z + Room.DepthM - 0.5f);
            GlideCamera(eye, Quaternion.LookRotation(focus - eye, Vector3.up), 0.75f, 32f);
            SceneDressing.SetDioramaFocus(Look, Vector3.Distance(eye, focus));
        }

        private void TickNougatCamera(float dt)
        {
            if (!_preNougatCam.HasValue || Routine == Interactables.Nougat) return;
            _nougatCamBackIn -= dt;
            if (_nougatCamBackIn > 0f && _walkTarget == null) return;
            GlideCamera(_preNougatCam.Value.pos, _preNougatCam.Value.rot, 0.8f, _preNougatCam.Value.fov);
            SceneDressing.SetDioramaFocus(Look, 7.1f * HomeCamScale);
            _preNougatCam = null;
        }

        private void TickNougat()
        {
            var m = _routineAt;
            if (Gooby == null || m == null) { EndNougat(); return; }
            while (_nougatStep < CrankAt.Length && _routineT >= CrankAt[_nougatStep])
            {
                Sfx.Play("game_hit", CrankPitch[_nougatStep]);
                _nougatStep++;
            }
            var wobble = 0f;
            if (!ReducedMotion && _routineT < CrankAt[CrankAt.Length - 1] + 0.7f)
            {
                var phase = Mathf.Repeat(_routineT - CrankAt[0], 0.7f) / 0.7f;
                wobble = Mathf.Sin(phase * Mathf.PI) * 0.1f;
            }
            Gooby.localScale = new Vector3(_goobyScale.x * (1f + wobble * 0.4f), _goobyScale.y * (1f - wobble), _goobyScale.z * (1f + wobble * 0.4f));
            if (_dollop == null && !_nougatSplat && _nougatStep >= CrankAt.Length && _routineT >= DollopAt)
            {
                var b = m.Bounds;
                _dollopFrom = new Vector3(b.center.x, b.min.y + 0.12f, b.center.z) + m.Front * (Mathf.Abs(m.Front.z) > 0.5f ? b.extents.z : b.extents.x) * 0.6f;
                _dollop = ArtLibrary.Sphere(Room.Root.transform, "Dollop", _dollopFrom, 0.07f, ArtLibrary.Solid(ArtLibrary.Hex("#6B3A1F"), 0.75f), false).transform;
                Sfx.Play("game_pop", -2f);
            }
            if (_dollop != null)
            {
                var k = Mathf.Clamp01((_routineT - DollopAt) / DollopFall);
                var to = Gooby.position + Vector3.up * 0.75f;
                _dollop.position = Vector3.Lerp(_dollopFrom, to, k * k);
                if (k >= 1f)
                {
                    Effects.Puff(to, new Color(0.42f, 0.23f, 0.12f, 0.9f), 8, 0.06f, 0.8f, 0.45f, Vector3.up, 0.9f);
                    Sfx.Play("nom_nom");
                    UnityEngine.Object.Destroy(_dollop.gameObject);
                    _dollop = null;
                    _nougatSplat = true;
                    Avatar?.Play("hop");
                }
            }
            if (_routineT >= NougatEnd && _dollop == null) EndNougat();
        }

        /// <summary>Leaving mid-crank: Gooby unsquashed, no dollop, nothing booked.</summary>
        private void AbortNougat()
        {
            if (Gooby != null && _goobyScale != Vector3.zero) Gooby.localScale = _goobyScale;
            if (_dollop != null) UnityEngine.Object.Destroy(_dollop.gameObject);
            _dollop = null;
            _nougatDone = null;
        }

        private void EndNougat()
        {
            if (Gooby != null && _goobyScale != Vector3.zero) Gooby.localScale = _goobyScale;
            if (_dollop != null) UnityEngine.Object.Destroy(_dollop.gameObject);
            _dollop = null;
            var done = _nougatDone;
            _nougatDone = null;
            _nougatCamBackIn = 1.6f;
            SetRoutine(RoutineNone, null);
            Avatar?.SetEmotion("happy");
            done?.Invoke();
        }
    }
}
