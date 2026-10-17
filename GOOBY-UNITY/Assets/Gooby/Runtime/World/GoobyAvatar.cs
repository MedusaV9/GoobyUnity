using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The Blender-built Gooby (tools/blender/build_gooby.sh → StreamingAssets/gooby/character/gooby.glb):
    /// 21 clips (idle-loop, walk-loop, hop, sit-loop, sleep-loop, wave, celebrate, dance-loop, refuse, …) and
    /// 14 shape keys (emotion_* , blink, mouth_open, eye_width, eye_size, ear_len). Falls back to the procedural
    /// stand-in when the GLB or glTFast is unavailable.
    /// </summary>
    public sealed class GoobyAvatar : MonoBehaviour
    {
        public static readonly string[] Emotions = { "neutral", "happy", "sad", "sleepy", "ecstatic", "angry", "scared", "dizzy" };

        /// <summary>
        /// emotion_sleepy lids are 0.60 (tools/blender/gooby_build/gooby_params.py). Blend shapes add, so holding
        /// blink at this weight brings the total squash to a closed cartoon lid (≈1.15: a thin curved line like the
        /// crate nap of board 01 — 0.45 still left a dark slit that read as a squint) without punching through the
        /// head (sleepy 1 + blink 1 ≈ 1.6). The idle blink cycle would otherwise overwrite blink back to 0.
        /// </summary>
        public const float SleepyBlinkHold = 0.55f;

        private Animation _anim;
        private SkinnedMeshRenderer _skin;
        private readonly Dictionary<string, int> _shapes = new Dictionary<string, int>();
        private string _clip = "";
        private string _emotion = "neutral";
        private float _blinkTimer = 2.5f, _blinkPhase = -1f;
        private float _babble;
        private Transform _icePack;

        public bool Loaded { get; private set; }

        public static async System.Threading.Tasks.Task<GoobyAvatar> CreateAsync(Transform parent, Vector3 position)
        {
            // a clone of the one imported, rigged template (AssetLoader.WarmUpCharacter): fourteen passers-by and the
            // player cost one glTF parse, and they stand in the street the moment it opens
            var go = await AssetLoader.InstanceAsync(AssetLoader.CharacterPath, parent, "Gooby", legacyAnimation: true);
            if (go == null) return null;
            go.transform.localPosition = position;
            var avatar = go.AddComponent<GoobyAvatar>();
            avatar.Bind();
            return avatar;
        }

        private void Bind()
        {
            _anim = GetComponentInChildren<Animation>();
            _skin = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_skin != null && _skin.sharedMesh != null)
                for (var i = 0; i < _skin.sharedMesh.blendShapeCount; i++) _shapes[_skin.sharedMesh.GetBlendShapeName(i)] = i;
            Loaded = _skin != null;
            Play("idle-loop");
            SetEmotion("happy");
            // built in the bind pose (before the first animated frame), then they ride the head bone
            EnsureIcePack();
            ShowIcePack(false);
            EnsureCareProps();
        }

        public void Play(string clip, float fade = 0.25f)
        {
            if (_anim == null) return;
            var looping = clip.EndsWith("-loop");
            if (_clip == clip && looping) return;
            var state = _anim[clip];
            if (state == null) return;
            state.wrapMode = looping ? WrapMode.Loop : WrapMode.Once;
            if (!looping && _clip == clip)
            {
                state.time = 0f;
                _anim.Play(clip);
            }
            else _anim.CrossFade(clip, fade);
            _clip = clip;
            if (!looping) _anim.PlayQueued("idle-loop", QueueMode.CompleteOthers);
        }

        /// <summary>Playback rate of a clip (the walk cycle follows the actual walking speed).</summary>
        public void SetClipSpeed(string clip, float speed)
        {
            if (_anim == null) return;
            var state = _anim[clip];
            if (state != null) state.speed = speed;
        }

        /// <summary>Short mouth-open pulse (cashier tapping at the till).</summary>
        public void BabblePulse()
        {
            SetShape("mouth_open", 0.6f);
            _babble = 0.22f;
        }

        /// <summary>Holds the mouth open (0..1) until set again — the GOB NOM catcher waits with it wide.</summary>
        public void SetMouthOpen(float weight)
        {
            _babble = 0f;
            SetShape("mouth_open", Mathf.Clamp01(weight));
        }

        public void SetEmotion(string emotion)
        {
            if (_skin == null) return;
            // glTFast adds morph frames at weight 1.0 → weights live in 0..1, not Unity's usual 0..100
            var wasSleepy = _emotion == "sleepy";
            foreach (var e in Emotions) SetShape("emotion_" + e, e == emotion ? 1f : 0f);
            _emotion = emotion;
            if (emotion == "sleepy")
            {
                _blinkPhase = -1f;
                SetShape("blink", SleepyBlinkHold);
            }
            else if (wasSleepy)
            {
                SetShape("blink", 0f);
                _blinkTimer = Random.Range(2.5f, 5f);
            }
            ShowIcePack(emotion == "sleepy" || _careGrade >= 2);
        }

        /// <summary>Mood 0..100 → emotion morph (mirrors gooby_reactions.gd thresholds).</summary>
        public void SetMood(double mood, bool sleeping, bool sick)
        {
            if (sleeping) { SetEmotion("sleepy"); Play("sleep-loop"); return; }
            if (sick) { SetEmotion("dizzy"); return; }
            SetEmotion(mood >= 80 ? "ecstatic" : mood >= 55 ? "happy" : mood >= 30 ? "neutral" : "sad");
            if (_clip == "sleep-loop") Play("idle-loop");
        }

        /// <summary>Character-editor morphs from meta.charMorphs (eyes_apart/eye_scale/ear_len/chubby → 0..1).</summary>
        public void ApplyMorphs(double eyeWidth, double eyeSize, double earLength, double chubby)
        {
            SetShape("eye_width", (float)eyeWidth);
            SetShape("eye_size", (float)eyeSize);
            SetShape("ear_length", (float)earLength);
            SetShape("body_squeeze_door", 0f);
            _chubbyWidth = 1f + (float)chubby * 0.15f;
            transform.localScale = Vector3.one;
            ApplyWidth();
        }

        /// <summary>
        /// meta.charMorphs in the save's range (eyes_apart −1..1, eye_scale / ear_len 0.7..1.4 around 1, chubby 0..1) onto
        /// the shape keys like gooby_rig.gd set_morph: neutral lands on delta 0. Chubby widens the body; idempotent.
        /// </summary>
        public void ApplySavedMorphs(Newtonsoft.Json.Linq.JToken morphs)
        {
            double V(string key, double neutral)
            {
                var v = morphs?[key];
                return v != null && (v.Type == Newtonsoft.Json.Linq.JTokenType.Float || v.Type == Newtonsoft.Json.Linq.JTokenType.Integer) ? (double)v : neutral;
            }
            SetShape("eye_width", Mathf.Clamp((float)V("eyes_apart", 0.0), -1f, 1f));
            SetShape("eye_size", (Mathf.Clamp((float)V("eye_scale", 1.0), 0.7f, 1.4f) - 1f) / 0.35f);
            SetShape("ear_length", (Mathf.Clamp((float)V("ear_len", 1.0), 0.7f, 1.4f) - 1f) / 0.25f);
            var chubby = Mathf.Clamp01((float)V("chubby", 0.0));
            _chubbyWidth = 1f + chubby * 0.15f;
            ApplyWidth();
        }

        private float _chubbyWidth = 1f, _weightWidth = 1f;

        /// <summary>The weight's body width (Weight.BodyScale: 0.93 slim … 1.14 round) on top of the editor's chubbiness.</summary>
        public void SetBodyWidth(float k)
        {
            if (Mathf.Approximately(k, _weightWidth)) return;
            _weightWidth = k;
            ApplyWidth();
        }

        public float BodyWidth => _weightWidth;

        /// <summary>X/Z follow the height: only the width changes, a uniform scale set by the scene stays.</summary>
        private void ApplyWidth()
        {
            var s = transform.localScale.y;
            var w = s * _chubbyWidth * _weightWidth;
            transform.localScale = new Vector3(w, s, w);
        }

        // ── care look (gooby_rig.gd set_care / sneeze; CareLook decides what shows) ──

        // bind-pose places on the face (tools/blender gooby_params NOSE / EYE × RIG_SCALE): the nose, and the skin under each eye
        private static readonly Vector3 NoseAt = new Vector3(0f, 0.638f, 0.287f);
        private static readonly Vector3 UnderEyeAt = new Vector3(0.107f, 0.598f, 0.30f);
        private static readonly Color PaleTint = new Color(0.80f, 0.88f, 0.82f);
        private static readonly Color NoseColor = new Color(0.91f, 0.42f, 0.37f);
        private static readonly Color EyeBagColor = new Color(0.42f, 0.36f, 0.52f);

        private int _careGrade;
        private Transform _careNose;
        private readonly List<Renderer> _careBags = new List<Renderer>();
        private Material _skinMat;
        private Color? _skinBase;
        private float _sneezeT = -1f, _yawnT = -1f;
        private float _sneezeBaseY;

        /// <summary>
        /// Gooby's care state on his body: pale by <paramref name="pale"/> (queasy half, sick fully), a sniffly red nose,
        /// the ice pack when properly sick (it also rides the sleepy face of the crate nap) and soft rings under the eyes
        /// (0 = none, else their strength).
        /// </summary>
        public void SetCare(int grade, float pale, bool nose, float eyeBags)
        {
            _careGrade = grade;
            ApplyPale(pale);
            if (_careNose != null) _careNose.gameObject.SetActive(nose);
            var alpha = Mathf.Round((0.2f + 0.4f * Mathf.Clamp01(eyeBags)) * 20f) / 20f;
            foreach (var bag in _careBags)
            {
                if (bag == null) continue;
                bag.gameObject.SetActive(eyeBags > 0f);
                if (eyeBags > 0f) bag.sharedMaterial = ArtLibrary.Transparent(new Color(EyeBagColor.r, EyeBagColor.g, EyeBagColor.b, alpha));
            }
            ShowIcePack(_emotion == "sleepy" || grade >= 2);
        }

        public int CareGrade => _careGrade;
        public bool SniffleNoseShown => _careNose != null && _careNose.gameObject.activeSelf;
        public bool EyeBagsShown => _careBags.Count > 0 && _careBags[0] != null && _careBags[0].gameObject.activeSelf;

        /// <summary>The fur's own colour times a mix towards the pale green-grey (the material instance of this Gooby only).</summary>
        private void ApplyPale(float k)
        {
            if (_skin == null) return;
            if (_skinMat == null) _skinMat = _skin.material;
            var prop = _skinMat.HasProperty("baseColorFactor") ? "baseColorFactor" : _skinMat.HasProperty("_BaseColor") ? "_BaseColor" : null;
            if (prop == null) return;
            if (_skinBase == null) _skinBase = _skinMat.GetColor(prop);
            var b = _skinBase.Value;
            var t = Color.Lerp(Color.white, PaleTint, Mathf.Clamp01(k));
            _skinMat.SetColor(prop, new Color(b.r * t.r, b.g * t.g, b.b * t.b, b.a));
        }

        private void EnsureCareProps()
        {
            if (_careNose != null) return;
            var head = Bone("head") ?? transform;
            var noseGroup = new GameObject("Care:nose");
            noseGroup.transform.SetParent(transform, false);
            noseGroup.transform.localPosition = NoseAt;
            var nose = ArtLibrary.Sphere(noseGroup.transform, "Nose", noseGroup.transform.position, 0.037f, ArtLibrary.Solid(NoseColor, 0.55f), false);
            nose.transform.localPosition = Vector3.zero;
            nose.transform.localScale = new Vector3(0.085f, 0.063f, 0.06f);
            noseGroup.transform.SetParent(head, true);
            noseGroup.SetActive(false);
            _careNose = noseGroup.transform;
            foreach (var side in new[] { -1f, 1f })
            {
                var bagGroup = new GameObject(side < 0f ? "Care:eyebag_r" : "Care:eyebag_l");
                bagGroup.transform.SetParent(transform, false);
                bagGroup.transform.localPosition = new Vector3(side * UnderEyeAt.x, UnderEyeAt.y, UnderEyeAt.z);
                bagGroup.transform.localRotation = Quaternion.Euler(0f, side * 20f, 0f);
                var bag = ArtLibrary.Sphere(bagGroup.transform, "Bag", bagGroup.transform.position, 0.034f, ArtLibrary.Transparent(new Color(EyeBagColor.r, EyeBagColor.g, EyeBagColor.b, 0.4f)), false);
                bag.transform.localPosition = Vector3.zero;
                bag.transform.localScale = new Vector3(0.082f, 0.03f, 0.03f);
                bagGroup.transform.SetParent(head, true);
                bagGroup.SetActive(false);
                _careBags.Add(bag.GetComponent<Renderer>());
            }
        }

        /// <summary>Achoo: the mouth pops open and the body puffs up and squashes back (nothing moves with reduced motion).</summary>
        public void Sneeze(bool reducedMotion)
        {
            BabblePulse();
            if (reducedMotion || _sneezeT >= 0f) return;
            _sneezeBaseY = transform.localScale.y;
            _sneezeT = 0f;
        }

        /// <summary>A big yawn: eyes squeeze half shut, the mouth opens wide and slowly closes again.</summary>
        public void Yawn()
        {
            if (_emotion == "sleepy") return;
            _babble = 0f;
            _yawnT = 0f;
        }

        public bool Yawning => _yawnT >= 0f;

        private void TickCareMoments()
        {
            if (_sneezeT >= 0f)
            {
                // 0.16 s up to 1.06, 0.09 s down to 0.86, 0.22 s back (gooby_rig.gd sneeze tween)
                _sneezeT += Time.deltaTime;
                float f;
                if (_sneezeT < 0.16f) f = Mathf.Lerp(1f, 1.06f, _sneezeT / 0.16f);
                else if (_sneezeT < 0.25f) f = Mathf.Lerp(1.06f, 0.86f, (_sneezeT - 0.16f) / 0.09f);
                else if (_sneezeT < 0.47f) f = Mathf.Lerp(0.86f, 1f, (_sneezeT - 0.25f) / 0.22f);
                else { f = 1f; _sneezeT = -1f; }
                var w = _sneezeBaseY * _chubbyWidth * _weightWidth;
                transform.localScale = new Vector3(w, _sneezeBaseY * f, w);
            }
            if (_yawnT >= 0f)
            {
                _yawnT += Time.deltaTime;
                const float total = 1.8f;
                var u = Mathf.Clamp01(_yawnT / total);
                // opens over the first third, holds, then closes; the lids follow at half strength
                var open = u < 0.3f ? u / 0.3f : u < 0.65f ? 1f : 1f - (u - 0.65f) / 0.35f;
                SetShape("mouth_open", 0.9f * open);
                SetShape("blink", 0.55f * open);
                if (_yawnT >= total) { _yawnT = -1f; SetShape("mouth_open", 0f); SetShape("blink", 0f); }
            }
        }

        private void SetShape(string name, float weight)
        {
            if (_skin != null && _shapes.TryGetValue(name, out var idx)) _skin.SetBlendShapeWeight(idx, weight);
        }

        private void Update()
        {
            if (_skin == null) return;
            TickCareMoments();
            if (_yawnT >= 0f) return;
            if (_babble > 0f)
            {
                _babble -= Time.deltaTime;
                if (_babble <= 0f) SetShape("mouth_open", 0f);
            }
            // crate-nap / sleep: hold the lids shut (board 08). Do not run the idle blink cycle on top.
            if (_emotion == "sleepy")
            {
                SetShape("blink", SleepyBlinkHold);
                return;
            }
            // blink: 120 ms close, every ~2.5–5 s
            if (_blinkPhase < 0f)
            {
                _blinkTimer -= Time.deltaTime;
                if (_blinkTimer <= 0f) { _blinkPhase = 0f; _blinkTimer = Random.Range(2.5f, 5f); }
            }
            else
            {
                _blinkPhase += Time.deltaTime / 0.12f;
                var w = _blinkPhase < 0.5f ? _blinkPhase * 2f : Mathf.Max(0f, 2f - _blinkPhase * 2f);
                SetShape("blink", w);
                if (_blinkPhase >= 1f) { SetShape("blink", 0f); _blinkPhase = -1f; }
            }
        }

        /// <summary>
        /// Board 08 crate-nap: a pale-blue ice pack on the crown (Godot <c>_care_ice_pack</c> geometry, shown whenever
        /// the face is sleepy — night sleep uses the same morph). Hidden again when the emotion leaves sleepy.
        /// </summary>
        private void ShowIcePack(bool on)
        {
            if (on) EnsureIcePack();
            if (_icePack != null) _icePack.gameObject.SetActive(on);
        }

        private void EnsureIcePack()
        {
            if (_icePack != null) return;
            var head = Bone("head") ?? transform;
            var group = new GameObject("Care:ice");
            // same trick as Outfit hats: place at the bind-pose crown, then parent to the head bone
            group.transform.SetParent(transform, false);
            group.transform.localPosition = new Vector3(0.0f, 0.90f, 0.30f);
            group.transform.localRotation = Quaternion.Euler(-6f, 8f, 8f);
            var ice = ArtLibrary.Solid(new Color(0.72f, 0.88f, 0.97f), 0.42f);
            var knot = ArtLibrary.Solid(new Color(0.92f, 0.97f, 1f), 0.38f);
            var bag = ArtLibrary.Sphere(group.transform, "Bag", group.transform.position, 0.13f, ice, false);
            bag.transform.localPosition = Vector3.zero;
            bag.transform.localScale = new Vector3(0.28f, 0.16f, 0.26f);
            var cap = ArtLibrary.Cylinder(group.transform, "Knot", group.transform.position, 0.04f, 0.055f, knot, false);
            cap.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            group.transform.SetParent(head, true);
            _icePack = group.transform;
            Debug.Log("[care] ice parent=" + head.name + " world=" + group.transform.position.ToString("F2")
                + " head=" + head.position.ToString("F2"));
        }

        public string Emotion => _emotion;
        public string Clip => _clip;

        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();

        /// <summary>A rig node by its Blender bone name ("head", "chest", …); null when the GLB lacks it.</summary>
        public Transform Bone(string name)
        {
            if (_bones.TryGetValue(name, out var cached) && cached != null) return cached;
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == name) { _bones[name] = t; return t; }
            return null;
        }

        /// <summary>Wears the equipped cosmetics (kategorie → id) and repaints the fur; see <see cref="Outfit"/>.</summary>
        public void Wear(Dictionary<string, string> equipped) => Outfit.Apply(this, equipped);
    }
}
