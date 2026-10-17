using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The ranch horse of ranch_pferd.gd in 3D: the Blender-built pferd.glb (13 bones; clips idle-loop, schritt-loop,
    /// trab-loop, galopp-loop, sprung, fressen-loop, kopfschuetteln, schlafen-loop, blinzeln) wearing its coat as a 4×4
    /// palette texture of the design pastels — the GLB's embedded palette is double-encoded and comes out too dark, the
    /// same deviation Godot makes. The anchor's +Z is the nose. A mounted rider sits on the saddle point of the body
    /// bone, so he rocks with the gait. Gear (saddle, pad, halter) and the fluffy look hang on bone anchors in the mesh's
    /// bind space (<see cref="RanchGear3D"/>), so they move with every clip. The model loads async; without glTFast a few
    /// primitives stand in.
    /// </summary>
    public sealed class RanchHorse3D : MonoBehaviour
    {
        public const float BackY = 1.42f;
        public static string ModelPath => Path.Combine(AssetLoader.Root, "ranch", "pferd.glb");

        /// <summary>The saddle in the GLB's model space (ranch_pferd.gd mounts Gooby at 0, 1.3, 0.1 under the horse root).</summary>
        private static readonly Vector3 SeatModel = new Vector3(0f, 1.3f, 0.1f);

        private static readonly Dictionary<string, Material> Coats = new Dictionary<string, Material>();
        private static readonly Color Hoof = Hex(0x6B5A52), Cheek = Hex(0xF9C6CF), EyeInk = Hex(0x3A2E2E), EyeWhite = Color.white;
        private static readonly Color Mouth = Hex(0x4A2B33), EarInner = Hex(0xF6A8B8), White = Hex(0xFDFDF7), Teal = Hex(0x5FA8A0);

        private Animation _anim;
        private SkinnedMeshRenderer _skin;
        private Transform _model, _body, _rider;
        private Vector3 _seatInBody, _forwardInBody, _upInBody;
        private bool _boneSeat;
        private Material _material;
        private bool _ghost;
        private string _gaitClip = "idle-loop", _playing = "", _gait = "stand";
        private readonly Hufschlag _hufschlag = new Hufschlag();
        private float _gaitSpeed = 1f, _actionLeft, _blinkIn = 2.5f, _blinkLeft;
        private int _blinkShape = -1;
        private Color _fell = Hex(0xB98A5E), _maehne = Hex(0x6E4B2E);
        private readonly Dictionary<string, (Transform bone, Matrix4x4 bind)> _bones = new Dictionary<string, (Transform, Matrix4x4)>();
        private readonly Dictionary<string, string> _gear = new Dictionary<string, string>();
        private readonly Dictionary<string, Transform> _gearRoots = new Dictionary<string, Transform>();
        private readonly List<Transform> _flauschRoots = new List<Transform>();
        private bool _flausch;

        public bool Loaded { get; private set; }

        /// <summary>What the horse wears now (slot → colour id), for QA and tests.</summary>
        public IReadOnlyDictionary<string, string> Gear => _gear;

        public bool Flausch => _flausch;

        /// <summary>
        /// The ground under a ridden horse (ride_feel.gd UNTERGRUND: "wiese", "sand", "holz" …): its hoof beats are heard with
        /// the gait, silent during a jump or another action. Null = a horse in the background, silent.
        /// </summary>
        public string Hufe { get; set; }

        /// <summary>Hoof beats heard so far (QA).</summary>
        public int HufBeats => _hufschlag.Beats;

        /// <summary>A horse in the given coat; its anchor stands at once, the model follows when the GLB is in.</summary>
        public static RanchHorse3D Create(Transform parent, string name, Color fell, Color maehne)
        {
            var horse = Spawn(parent, name, Coat(fell, maehne), false);
            horse._fell = fell;
            horse._maehne = maehne;
            return horse;
        }

        /// <summary>The see-through ghost of the best run (comp_geist.gd): one pale glassy tint, no shadow.</summary>
        public static RanchHorse3D CreateGhost(Transform parent, Color tint) =>
            Spawn(parent, "Geist", ArtLibrary.Transparent(tint), true);

        private static RanchHorse3D Spawn(Transform parent, string name, Material material, bool ghost)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var horse = go.AddComponent<RanchHorse3D>();
            horse._material = material;
            horse._ghost = ghost;
            _ = horse.LoadAsync();
            return horse;
        }

        /// <summary>
        /// ranch_pferd.gd _fell_material: the 4×4 palette of ranch_stil.py (row 0 coat / light coat / mane / hoof, row 1
        /// cheek / eye ink / eye white / dark coat, row 2 mouth / inner ear / white / teal accent, row 3 coat), one shared
        /// material per colour pair. glTFast flips V, so the palette's top row is the texture's last.
        /// </summary>
        public static Material Coat(Color fell, Color maehne)
        {
            var key = ColorUtility.ToHtmlStringRGB(fell) + ColorUtility.ToHtmlStringRGB(maehne);
            if (Coats.TryGetValue(key, out var cached) && cached != null) return cached;
            var dark = new Color(fell.r * 0.88f, fell.g * 0.88f, fell.b * 0.88f);
            var cells = new[]
            {
                new[] { fell, Color.Lerp(fell, Hex(0xFFF6E8), 0.62f), maehne, Hoof },
                new[] { Cheek, EyeInk, EyeWhite, dark },
                new[] { Mouth, EarInner, White, Teal },
                new[] { fell, fell, fell, fell },
            };
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, false) { name = "RanchCoat:" + key, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                    tex.SetPixel(x, 3 - y, cells[y][x]);
            tex.Apply(false, true);
            var m = new Material(ArtLibrary.Solid(Color.white, 0.08f)) { name = "RanchCoat:" + key, mainTexture = tex };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            Coats[key] = m;
            return m;
        }

        public void SetCoat(Color fell, Color maehne)
        {
            var neu = fell != _fell || maehne != _maehne;
            _fell = fell;
            _maehne = maehne;
            _material = Coat(fell, maehne);
            ApplyMaterial();
            if (neu && _flausch) BuildFlausch();
        }

        /// <summary>Puts a piece of gear on (a colour id: the shop five, "kartoffel", "schleife") or takes it off (null).</summary>
        public void SetGear(string slot, string farbe)
        {
            if (_ghost || System.Array.IndexOf(GearSlots, slot) < 0) return;
            farbe = string.IsNullOrEmpty(farbe) ? null : farbe;
            _gear.TryGetValue(slot, out var alt);
            if (alt == farbe) return;
            if (farbe == null) _gear.Remove(slot);
            else _gear[slot] = farbe;
            if (_bones.Count == 0) return;
            BuildGear(slot);
            if (slot == "decke") BuildGear("sattel");
        }

        /// <summary>The fluffy look (forelock, mane puffs, tail pompom, feathered fetlocks) in the mane colour.</summary>
        public void SetFlausch(bool an)
        {
            if (_ghost || an == _flausch) return;
            _flausch = an;
            if (_bones.Count > 0) BuildFlausch();
        }

        private static readonly string[] GearSlots = { "sattel", "decke", "halfter" };

        private void BuildGear(string slot)
        {
            if (_gearRoots.TryGetValue(slot, out var alt) && alt != null) Destroy(alt.gameObject);
            _gearRoots.Remove(slot);
            if (!_gear.TryGetValue(slot, out var farbe)) return;
            var anker = Anker(slot == "halfter" ? "head" : "body", "Gear_" + slot);
            if (anker == null) return;
            switch (slot)
            {
                case "sattel": RanchGear3D.Sattel(anker, farbe, _gear.ContainsKey("decke")); break;
                case "decke": RanchGear3D.Decke(anker, farbe); break;
                default: RanchGear3D.Halfter(anker, farbe); break;
            }
            _gearRoots[slot] = anker;
        }

        private void BuildFlausch()
        {
            foreach (var t in _flauschRoots)
                if (t != null) Destroy(t.gameObject);
            _flauschRoots.Clear();
            if (!_flausch) return;
            void Mit(string bone, System.Action<Transform> bau)
            {
                var a = Anker(bone, "Flausch_" + bone);
                if (a == null) return;
                bau(a);
                _flauschRoots.Add(a);
            }
            Mit("head", a => RanchGear3D.FlauschKopf(a, _maehne));
            Mit("neck", a => RanchGear3D.FlauschHals(a, _maehne));
            Mit("tail.02", a => RanchGear3D.FlauschSchweif(a, _maehne));
            foreach (var bein in new[] { "leg.FL", "leg.FR", "leg.BL", "leg.BR" })
            {
                if (!_bones.TryGetValue(bein, out var b)) continue;
                var gelenk = b.bind.inverse.MultiplyPoint3x4(Vector3.zero);
                Mit(bein, a => RanchGear3D.FlauschFessel(a, new Vector2(gelenk.x, gelenk.z), _fell));
            }
        }

        /// <summary>A holder under a bone whose local space is the mesh's bind space (bindpose = mesh → bone).</summary>
        private Transform Anker(string bone, string name)
        {
            if (!_bones.TryGetValue(bone, out var b) || b.bone == null) return null;
            var t = new GameObject(name).transform;
            t.SetParent(b.bone, false);
            t.localPosition = b.bind.GetColumn(3);
            t.localRotation = b.bind.rotation;
            t.localScale = b.bind.lossyScale;
            return t;
        }

        /// <summary>The gait loop ("stand", "schritt", "trab", "toelt", "galopp"); <paramref name="speed"/> stretches the stride.</summary>
        public void Gait(string gait, float speed = 1f)
        {
            _gait = gait ?? "stand";
            _gaitClip = gait == "schritt" ? "schritt-loop" : gait == "trab" || gait == "toelt" ? "trab-loop" : gait == "galopp" ? "galopp-loop" : "idle-loop";
            _gaitSpeed = _gaitClip == "idle-loop" ? 1f : Mathf.Clamp(speed, 0.35f, 1.4f);
            if (_actionLeft <= 0f) PlayClip(_gaitClip, _gaitSpeed);
        }

        /// <summary>A one-off clip ("sprung", "kopfschuetteln", "fressen-loop" …) for <paramref name="seconds"/> (0 = the clip's length), then back to the gait.</summary>
        public void Action(string clip, float seconds = 0f, float speed = 1f)
        {
            if (_anim == null || _anim[clip] == null) return;
            var state = _anim[clip];
            _actionLeft = seconds > 0f ? seconds : state.length / Mathf.Max(0.05f, speed);
            _playing = "";
            PlayClip(clip, speed, 0.12f);
        }

        /// <summary>Cuts a running one-off clip short and goes back to the gait.</summary>
        public void EndAction()
        {
            if (_actionLeft <= 0f) return;
            _actionLeft = 0f;
            PlayClip(_gaitClip, _gaitSpeed, 0.2f);
        }

        public bool InAction => _actionLeft > 0f;

        /// <summary>Seats a rider (Gooby faces his own +Z) on the saddle; he follows the body bone every frame.</summary>
        public void Mount(Transform rider)
        {
            _rider = rider;
            PlaceRider();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            var model = await AssetLoader.InstanceAsync(ModelPath, transform, "Pferd", legacyAnimation: true);
            if (this == null)
            {
                if (model != null) Destroy(model);
                return;
            }
            if (model == null)
            {
                BuildStandIn();
                return;
            }
            _model = model.transform;
            _model.localPosition = Vector3.zero;
            _model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _model.localScale = Vector3.one;
            _anim = model.GetComponentInChildren<Animation>();
            _skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (_anim != null)
            {
                _anim.playAutomatically = false;
                _anim.cullingType = AnimationCullingType.AlwaysAnimate;
            }
            if (_skin != null && _skin.sharedMesh != null)
            {
                _blinkShape = _skin.sharedMesh.GetBlendShapeIndex("augen_zu");
                var bones = _skin.bones;
                var binds = _skin.sharedMesh.bindposes;
                for (var i = 0; i < bones.Length && i < binds.Length; i++)
                    if (bones[i] != null) _bones[bones[i].name] = (bones[i], binds[i]);
                for (var i = 0; i < bones.Length && i < binds.Length; i++)
                {
                    if (bones[i] == null || bones[i].name != "body") continue;
                    _body = bones[i];
                    // the saddle point and the model's forward/up as the body bone carried them at bind time (the mesh
                    // and the model root share a space): a point rigidly riding on the body
                    var seatMesh = new Vector3(-SeatModel.x, SeatModel.y, SeatModel.z);
                    _seatInBody = binds[i].MultiplyPoint3x4(seatMesh);
                    _forwardInBody = binds[i].MultiplyVector(Vector3.back);
                    _upInBody = binds[i].MultiplyVector(Vector3.up);
                    _boneSeat = true;
                    break;
                }
            }
            ApplyMaterial();
            foreach (var slot in GearSlots) BuildGear(slot);
            if (_flausch) BuildFlausch();
            Loaded = true;
            _playing = "";
            PlayClip(_actionLeft > 0f ? "idle-loop" : _gaitClip, _gaitSpeed, 0f);
            PlaceRider();
        }

        private void ApplyMaterial()
        {
            if (_model == null || _material == null) return;
            // only the horse's own skin: gear and fluff on the bones keep their colours
            foreach (var r in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++) mats[i] = _material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = _ghost ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        private void PlayClip(string clip, float speed, float fade = 0.25f)
        {
            if (_anim == null) return;
            var state = _anim[clip];
            if (state == null) return;
            state.speed = speed;
            if (_playing == clip) return;
            state.wrapMode = clip.EndsWith("-loop") ? WrapMode.Loop : WrapMode.ClampForever;
            if (fade <= 0f || string.IsNullOrEmpty(_playing)) _anim.Play(clip);
            else _anim.CrossFade(clip, fade);
            if (!clip.EndsWith("-loop")) state.time = 0f;
            _playing = clip;
        }

        private void Update()
        {
            if (_actionLeft > 0f && (_actionLeft -= Time.deltaTime) <= 0f) PlayClip(_gaitClip, _gaitSpeed, 0.2f);
            if (Hufe != null && _actionLeft <= 0f) _hufschlag.Tick(Time.deltaTime, _gait, Hufe, _gaitSpeed);
        }

        private void LateUpdate()
        {
            if (_skin != null && _blinkShape >= 0)
            {
                // the gait loops key the lids open every frame; the blink is laid on top after the animation pass
                if (_blinkLeft > 0f) _blinkLeft -= Time.deltaTime;
                else if ((_blinkIn -= Time.deltaTime) <= 0f)
                {
                    _blinkLeft = 0.13f;
                    _blinkIn = Random.Range(2.4f, 5.2f);
                }
                _skin.SetBlendShapeWeight(_blinkShape, _blinkLeft > 0f ? 1f : 0f);
            }
            PlaceRider();
        }

        private void PlaceRider()
        {
            if (_rider == null) return;
            if (_boneSeat && _body != null)
            {
                _rider.position = _body.TransformPoint(_seatInBody);
                var forward = _body.TransformDirection(_forwardInBody);
                var up = _body.TransformDirection(_upInBody);
                // only a third of the body's pitch and roll reaches the rider: he sways with the stride without tipping
                _rider.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(forward, up), 0.35f);
                return;
            }
            _rider.position = transform.TransformPoint(new Vector3(0f, 1.3f, -0.1f));
            _rider.rotation = transform.rotation;
        }

        /// <summary>Box body, neck and head, four legs: the horse without glTFast (compile-check builds).</summary>
        private void BuildStandIn()
        {
            var mat = _ghost ? _material : ArtLibrary.Solid(new Color(0.73f, 0.54f, 0.37f), 0.1f);
            _model = new GameObject("PferdStandIn").transform;
            _model.SetParent(transform, false);
            ArtLibrary.Box(_model, "Rumpf", transform.TransformPoint(0f, 1.05f, 0f), new Vector3(0.7f, 0.62f, 1.7f), mat);
            ArtLibrary.Box(_model, "Hals", transform.TransformPoint(0f, 1.5f, 0.85f), new Vector3(0.34f, 0.8f, 0.36f), mat);
            ArtLibrary.Box(_model, "Kopf", transform.TransformPoint(0f, 1.85f, 1.12f), new Vector3(0.32f, 0.34f, 0.62f), mat);
            foreach (var (x, z) in new[] { (-0.24f, 0.62f), (0.24f, 0.62f), (-0.24f, -0.62f), (0.24f, -0.62f) })
                ArtLibrary.Box(_model, "Bein", transform.TransformPoint(x, 0.4f, z), new Vector3(0.16f, 0.8f, 0.16f), mat);
            Loaded = true;
            PlaceRider();
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
