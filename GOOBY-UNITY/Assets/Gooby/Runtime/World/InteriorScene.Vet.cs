using System.Collections.Generic;
using Gooby.Unity.App;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Dr. Möhrchen's examination on the stage (tierarzt.gd _untersuchung_sequenz had three toasts and a sneeze): three
    /// beats over the player's Gooby in front of the desk, seen from behind — the vet reaches over with the carrot
    /// stethoscope (tube from her paw to a carrot at Gooby's shoulder, a heart beating over his head), the thermometer
    /// beside his head whose red column climbs (higher when he is ill), the ear-wiggle test with sparks at both ears.
    /// A sick Gooby sneezes a puff of little clouds. A treatment ends in rising hearts and a cheer, a check-up in a wave.
    /// Every prop hangs under a root at his head scaled to his measured width, so the props keep their place next to
    /// him whatever size the avatar has.
    /// </summary>
    public sealed partial class InteriorScene
    {
        /// <summary>The width (ears out) the offsets are drawn for; a bigger or smaller Gooby scales them.</summary>
        private const float VetRefWidth = 1.15f;

        private GameObject _vetProp;
        private Transform _vetHeart, _vetColumn;
        private float _vetT, _vetColumnTarget, _vetK = 1f;
        private Vector3 _vetColumnBase;
        private Quaternion _vetTilt = Quaternion.identity;
        private readonly List<(Transform t, Vector3 v, float life, float max, Vector3 scale)> _vetFx = new List<(Transform, Vector3, float, float, Vector3)>();
        private float _sneezeMouth;
        private GoobyAvatar _sneezePatient;

        /// <summary>Where the patient's head is (bone, else above his feet) and how big he is relative to the drawing.</summary>
        private static (Vector3 head, float k) Measure(GoobyAvatar patient)
        {
            if (patient == null) return (Vector3.zero, 1f);
            var bone = patient.Bone("head");
            var head = bone != null ? bone.position : patient.transform.position + Vector3.up * 1.05f;
            var renderers = patient.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return (head, 1f);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return (head, Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) / VetRefWidth, 0.5f, 2f));
        }

        private Transform PropRoot(string name, Vector3 head, float k)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_t, false);
            root.position = head;
            root.localScale = Vector3.one * k;
            return root;
        }

        private static Material Paint(string hex, float smooth) => ArtLibrary.Solid(ArtLibrary.Hex(hex), smooth);

        /// <summary>One beat of the examination (0 listen, 1 temperature, 2 ears); returns where a reading may float up.</summary>
        public Vector3 VetStep(int step, GoobyAvatar patient, bool sick)
        {
            ClearVetProp();
            _vetT = 0f;
            var (head, k) = Measure(patient);
            var anchor = head + Vector3.up * 0.7f * k;
            _vetK = k;
            var p = PropRoot("VetProp" + step, head, k);
            _vetProp = p.gameObject;
            Vector3 At(float x, float y, float z) => head + new Vector3(x, y, z) * k;
            switch (step)
            {
                case 0:
                {
                    // the carrot chest piece at his right shoulder (world −X is screen-right), the tube up to the vet's paw
                    var piece = At(-0.62f, -0.1f, 0.18f);
                    var carrot = ArtLibrary.Cylinder(p, "StethCarrot", piece, 0.11f, 0.09f, Paint("#FF9E5E", 0.35f), false);
                    carrot.transform.rotation = Quaternion.Euler(80f, 0f, 0f);
                    foreach (var s in new[] { -1f, 1f })
                        ArtLibrary.Sphere(p, "StethLeaf", piece + new Vector3(s * 0.035f, 0.08f, 0f) * k, 0.04f, Paint("#7FCF9B", 0.2f), false);
                    var paw = (Npc != null ? Npc.transform.position : Origin + new Vector3(0f, 0.44f, -1.95f)) + new Vector3(-0.45f, 1.2f, 0.5f);
                    var tube = Paint("#FFF8EE", 0.3f);
                    for (var i = 0; i <= 18; i++)
                    {
                        var f = i / 18f;
                        var at = Vector3.Lerp(paw, piece, f) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 0.45f;
                        ArtLibrary.Sphere(p, "StethTube", at, 0.03f, tube, false);
                    }
                    _vetHeart = Heart(p, At(0f, 0.55f, 0f), 0.11f);
                    Npc?.SetEmotion("neutral");
                    Npc?.Play("wave");
                    if (sick) Sneeze(patient, head, k);
                    break;
                }
                case 1:
                {
                    // thermometer standing beside his head (screen-left), the red column climbs in TickVet
                    var at = At(0.62f, 0.38f, 0.18f);
                    var tilt = Quaternion.Euler(0f, 0f, -14f);
                    var glass = ArtLibrary.Cylinder(p, "ThermoGlass", at, 0.07f, 0.66f, Paint("#EEF6FF", 0.85f), false);
                    glass.transform.rotation = tilt;
                    var bulbAt = at + tilt * new Vector3(0f, -0.35f, 0f) * k;
                    ArtLibrary.Sphere(p, "ThermoBulb", bulbAt, 0.09f, Paint("#FF5A5A", 0.5f), false);
                    // the column sits a hair in front of the glass so it reads through the white body
                    bulbAt += new Vector3(0f, 0f, 0.075f) * k;
                    var column = ArtLibrary.Cylinder(p, "ThermoColumn", bulbAt, 0.028f, 0.02f, Paint("#FF5A5A", 0.5f), false);
                    column.transform.rotation = tilt;
                    _vetColumn = column.transform;
                    _vetColumnBase = bulbAt;
                    _vetTilt = tilt;
                    _vetColumnTarget = sick ? 0.56f : 0.36f;
                    anchor = at + Vector3.up * 0.48f * k;
                    for (var i = 0; i < 5; i++)
                    {
                        var tick = ArtLibrary.Box(p, "ThermoTick", at + tilt * new Vector3(0.04f, -0.2f + i * 0.1f, 0.075f) * k, new Vector3(0.05f, 0.012f, 0.01f), Paint("#3B364F", 0.2f), false);
                        tick.transform.rotation = tilt;
                    }
                    patient?.SetEmotion(sick ? "dizzy" : "happy");
                    break;
                }
                default:
                {
                    // ear wiggle: Gooby waves his ears, sparks pop at both tips
                    patient?.Play("wave");
                    patient?.SetEmotion("happy");
                    Npc?.SetEmotion("happy");
                    var glow = ArtLibrary.Glow(new Color(1.6f, 1.35f, 0.5f));
                    foreach (var s in new[] { -1f, 1f })
                        for (var i = 0; i < 4; i++)
                        {
                            var spark = ArtLibrary.Sphere(p, "EarSpark", At(s * 0.42f, 0.45f, 0f), 0.035f, glow, false);
                            AddFx(spark.transform, new Vector3(s * (0.5f + i * 0.25f), 1.2f - i * 0.2f, (i - 1.5f) * 0.3f) * k, 0.8f);
                        }
                    break;
                }
            }
            Debug.Log("[vet] step " + step + (sick ? " sick" : "") + " k=" + k.ToString("F2"));
            return anchor;
        }

        /// <summary>After paying: hearts rise round him and he cheers (treatment), or he and the vet wave (check-up).</summary>
        public void VetFinish(GoobyAvatar patient, bool cured)
        {
            ClearVetProp();
            var (head, k) = Measure(patient);
            var fx = PropRoot("VetFinish", head, k);
            if (cured)
            {
                patient?.Play("celebrate");
                patient?.SetEmotion("ecstatic");
                for (var i = 0; i < 9; i++)
                {
                    var a = i * 0.7f;
                    var h = Heart(fx, head + new Vector3(Mathf.Cos(a) * 0.55f, -0.05f + (i % 3) * 0.18f, 0.15f) * k, 0.07f + (i % 3) * 0.02f);
                    AddFx(h, new Vector3(Mathf.Cos(a) * 0.25f, 0.8f + (i % 4) * 0.15f, 0f) * k, 2.2f);
                }
            }
            else
            {
                patient?.Play("wave");
                var glow = ArtLibrary.Glow(new Color(0.6f, 1.5f, 0.8f));
                for (var i = 0; i < 6; i++)
                {
                    var s = ArtLibrary.Sphere(fx, "CheckSpark", head + new Vector3((i - 2.5f) * 0.18f, 0.45f, 0.1f) * k, 0.04f, glow, false);
                    AddFx(s.transform, new Vector3((i - 2.5f) * 0.18f, 0.9f, 0f) * k, 1.4f);
                }
            }
            Npc?.Play("wave");
            Npc?.SetEmotion("happy");
            Debug.Log("[vet] finish " + (cured ? "cured" : "checkup"));
        }

        private void AddFx(Transform t, Vector3 velocity, float life) => _vetFx.Add((t, velocity, 0f, life, t.localScale));

        /// <summary>A little 3D heart: two balls and a turned cube, facing the camera.</summary>
        private static Transform Heart(Transform parent, Vector3 at, float size)
        {
            var root = new GameObject("VetHeart").transform;
            root.SetParent(parent, false);
            root.position = at;
            var red = ArtLibrary.Glow(new Color(1.5f, 0.35f, 0.45f));
            var k = parent.lossyScale.x;
            foreach (var s in new[] { -1f, 1f })
                ArtLibrary.Sphere(root, "HeartLobe", at + new Vector3(s * size * 0.5f, size * 0.25f, 0f) * k, size * 0.62f, red, false);
            var tip = ArtLibrary.Box(root, "HeartTip", at + new Vector3(0f, -size * 0.25f, 0f) * k, Vector3.one * size * 0.95f, red, false);
            tip.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            return root;
        }

        /// <summary>The sneeze: mouth wide, a jolt, a puff of little clouds and a squeak (tierarzt.gd: patient.sneeze()).</summary>
        private void Sneeze(GoobyAvatar patient, Vector3 head, float k)
        {
            if (patient == null) return;
            patient.SetMouthOpen(1f);
            patient.Play("refuse");
            Sfx.Play("pet_squish", 7.5f, 0.8f);
            var puff = ArtLibrary.Solid(new Color(0.96f, 0.97f, 1f), 0.1f);
            for (var i = 0; i < 7; i++)
            {
                var s = ArtLibrary.Sphere(_vetProp.transform, "SneezePuff", head + new Vector3((i - 3) * 0.12f, -0.1f, -0.2f) * k, 0.06f + (i % 3) * 0.025f, puff, false);
                AddFx(s.transform, new Vector3((i - 3) * 0.4f, 0.3f + (i % 2) * 0.25f, -0.5f) * k, 0.9f);
            }
            _sneezeMouth = 0.35f;
            _sneezePatient = patient;
        }

        private void ClearVetProp()
        {
            if (_vetProp != null) Object.Destroy(_vetProp);
            _vetProp = null;
            _vetHeart = null;
            _vetColumn = null;
        }

        private void TickVet(float dt)
        {
            if (dt <= 0f) return;
            _vetT += dt;
            if (_vetHeart != null) _vetHeart.localScale = Vector3.one * (1f + 0.22f * Mathf.Max(0f, Mathf.Sin(_vetT * Mathf.PI * 2.4f)));
            if (_vetColumn != null)
            {
                // the column grows out of the bulb along the tilted glass (a primitive cylinder scales from its middle)
                var h = Mathf.Lerp(0.02f, _vetColumnTarget, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_vetT / 1.2f)));
                _vetColumn.localScale = new Vector3(0.056f, h * 0.5f, 0.056f);
                _vetColumn.position = _vetColumnBase + _vetTilt * Vector3.up * (h * 0.5f * _vetK);
            }
            if (_sneezeMouth > 0f)
            {
                _sneezeMouth -= dt;
                if (_sneezeMouth <= 0f && _sneezePatient != null) _sneezePatient.SetMouthOpen(0f);
            }
            for (var i = _vetFx.Count - 1; i >= 0; i--)
            {
                var (t, v, life, max, scale) = _vetFx[i];
                if (t == null) { _vetFx.RemoveAt(i); continue; }
                life += dt;
                if (life >= max) { Object.Destroy(t.gameObject); _vetFx.RemoveAt(i); continue; }
                t.position += v * dt;
                t.localScale = scale * Mathf.Lerp(1f, 0.25f, life / max);
                _vetFx[i] = (t, v * (1f - 0.8f * dt), life, max, scale);
            }
        }
    }
}
