using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.World
{
    /// <summary>
    /// The player's Gooby as the companion of every minigame frame: the rigged avatar in the player's outfit on a tiny
    /// set far from the world (beside the <see cref="MgStage3D"/> spot), filmed by its own camera into a RenderTexture
    /// that becomes the background of one UI element at a time — the pregame window, the round's HUD badge or the band
    /// beside a column field, the results card. The hosts' events drive it (<see cref="Cheer"/>, <see cref="Wince"/>,
    /// <see cref="Celebrate"/> …): a clip of the rig, the face, and a squash-bounce hop on top; between reactions it
    /// idles, glances around and settles back to its resting face. Background alpha stays 0, so the element's own
    /// colour (or the card behind it) shows around him. Several can live at once (the companion and a game's player
    /// figure), each on its own <c>slot</c> along the x axis, out of the others' cameras and lights.
    /// </summary>
    public sealed class MgBuddy3D
    {
        public static readonly Vector3 BaseOrigin = MgStage3D.Origin + new Vector3(-60f, 0f, 0f);
        /// <summary>Distance between slots: beyond the 6-m reach of the point lights and far outside each narrow camera.</summary>
        public const float SlotSpacing = 10f;

        public Vector3 Origin { get; }
        /// <summary>Idle glances and waves between reactions (off for a game's player figure).</summary>
        public bool Ambient { get; set; } = true;
        /// <summary>Turn of the figure: 0 faces the camera, 180 shows his back (views from behind him).</summary>
        public float BaseYaw { get; set; }
        /// <summary>The loop he returns to between reactions (idle; dance-loop for the dancer, walk-loop for a runner).</summary>
        public string RestClip { get; set; } = "idle-loop";

        public enum Framing
        {
            /// <summary>Head and shoulders, square — the round HUD badge.</summary>
            Portrait,
            /// <summary>Head to toe with room for a hop — windows and bands.</summary>
            Full,
        }

        private const float Fov = 24f;

        public GameObject Root { get; }
        public Camera Camera { get; }
        public RenderTexture Texture { get; private set; }
        public GoobyAvatar Gooby { get; private set; }

        private VisualElement _target;
        private Framing _framing = Framing.Full;
        private float _aspect = 1f;
        private int _pixelHeight;
        private float _facing, _facingNow;
        private bool _walking;
        private float _hop, _hopHeight = 0.22f, _faceHold, _idleIn = 5f, _twirl, _sway;
        private string _rest = "happy", _loop = "idle-loop";
        private string _pendingClip, _pendingFace;
        private readonly Transform _body, _shadow;

        public MgBuddy3D(Dictionary<string, string> outfit, int slot = 0)
        {
            Origin = BaseOrigin + new Vector3(-SlotSpacing * slot, 0f, 0f);
            Root = new GameObject("MgBuddy3D");
            Object.DontDestroyOnLoad(Root);
            Root.transform.position = Origin;
            var camGo = new GameObject("MgBuddyCamera");
            camGo.transform.SetParent(Root.transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Camera.fieldOfView = Fov;
            Camera.nearClipPlane = 0.2f;
            Camera.farClipPlane = 30f;
            Camera.allowHDR = false;
            Camera.allowMSAA = false;
            Camera.enabled = false;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(Camera);
            data.renderPostProcessing = false;
            data.renderShadows = false;
            // his own warm key and cool fill as local lights: they reach nothing of the world 400 m away, so the room
            // behind the pregame/results window keeps its light (a studio lease would switch its sun off)
            PointLight("BuddyKey", new Vector3(-1.1f, 1.9f, -1.5f), new Color(1f, 0.93f, 0.82f), 5.2f);
            PointLight("BuddyFill", new Vector3(1.5f, 1.1f, -1.6f), new Color(0.8f, 0.87f, 1f), 2.2f);
            PointLight("BuddyRim", new Vector3(0.4f, 1.6f, 1.2f), new Color(1f, 0.97f, 0.9f), 1.6f);

            _body = new GameObject("Body").transform;
            _body.SetParent(Root.transform, false);
            // a soft contact shadow under his feet (alpha-blended dot), so he stands on the card instead of floating
            var shadowMat = new Material(ArtLibrary.Particles(Effects.Dot(), false)) { name = "buddy_shadow" };
            var tint = new Color(0.32f, 0.22f, 0.14f, 0.32f);
            if (shadowMat.HasProperty("_BaseColor")) shadowMat.SetColor("_BaseColor", tint);
            shadowMat.color = tint;
            _shadow = ArtLibrary.Quad(Root.transform, "Shadow", Origin + new Vector3(0f, 0.005f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(1.1f, 0.7f), shadowMat).transform;
            Frame();
            _ = SpawnAsync(outfit);
        }

        private void PointLight(string name, Vector3 offset, Color color, float intensity)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(Root.transform, false);
            l.transform.position = Origin + offset;
            l.type = LightType.Point;
            l.range = 6f;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }

        private async System.Threading.Tasks.Task SpawnAsync(Dictionary<string, string> outfit)
        {
            var avatar = await GoobyAvatar.CreateAsync(_body, Vector3.zero);
            if (avatar == null) return;
            if (Root == null || _body == null) { Object.Destroy(avatar.gameObject); return; }
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            avatar.transform.localScale = Vector3.one;
            if (outfit != null) avatar.Wear(outfit);
            Gooby = avatar;
            avatar.SetEmotion(_pendingFace ?? _rest);
            var clip = _pendingClip ?? RestLoop;
            avatar.Play(clip, 0f);
            _loop = clip.EndsWith("-loop") ? clip : "idle-loop";
            _pendingClip = _pendingFace = null;
        }

        /// <summary>
        /// Shows the companion as <paramref name="target"/>'s background (a previous target is released).
        /// <paramref name="pixelHeight"/> fixes the texture height (0: a share of the screen height).
        /// </summary>
        public void Bind(VisualElement target, Framing framing, float aspect = 0f, int pixelHeight = 0)
        {
            if (_target != null && _target != target) _target.style.backgroundImage = StyleKeyword.Null;
            _target = target;
            _framing = framing;
            _pixelHeight = pixelHeight;
            _aspect = aspect > 0.05f ? aspect : framing == Framing.Portrait ? 1f : 0.8f;
            Frame();
            Resize();
            Camera.enabled = target != null;
        }

        /// <summary>Forgets <paramref name="target"/> if it is the current one (its element is going away).</summary>
        public void Unbind(VisualElement target)
        {
            if (target == null || target != _target) return;
            _target.style.backgroundImage = StyleKeyword.Null;
            _target = null;
            Camera.enabled = false;
        }

        public bool BoundTo(VisualElement target) => target != null && target == _target;

        private void Frame()
        {
            // the camera looks along +z at the Gooby, who faces it from the origin
            var portrait = _framing == Framing.Portrait;
            var centreY = portrait ? 0.70f : 0.68f;
            var height = portrait ? 0.98f : 1.52f;
            var distance = height * 0.5f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            Camera.transform.position = Origin + new Vector3(0f, centreY + (portrait ? 0.02f : 0.1f), -distance);
            Camera.transform.rotation = Quaternion.Euler(portrait ? 1.5f : 3.5f, 0f, 0f);
            Camera.aspect = _aspect;
        }

        private void Resize()
        {
            if (_target == null) return;
            var h = _pixelHeight > 0 ? _pixelHeight
                : _framing == Framing.Portrait
                    ? Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.16f), 128, 256)
                    : Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.5f), 256, 720);
            var w = Mathf.Max(64, Mathf.RoundToInt(h * _aspect));
            if (Texture == null || Texture.width != w || Texture.height != h)
            {
                var old = Texture;
                Texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "MgBuddy", antiAliasing = 1 };
                Texture.Create();
                Camera.targetTexture = Texture;
                if (old != null) { old.Release(); Object.Destroy(old); }
            }
            Camera.aspect = _aspect;
            _target.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(Texture));
        }

        /// <summary>
        /// Locomotion of a player figure: walking plays the walk cycle and turns him three quarters towards
        /// <paramref name="direction"/> (−1 left, +1 right on screen); standing turns him back to the camera.
        /// </summary>
        public void Locomote(bool walking, float direction)
        {
            _facing = walking ? Mathf.Clamp(direction, -1f, 1f) * 55f : 0f;
            if (walking == _walking) return;
            _walking = walking;
            if (_hop <= 0f && _faceHold <= 0f || walking) Clip(walking ? "walk-loop" : RestLoop);
        }

        // ── reactions ───────────────────────────────────────────────────────

        /// <summary>His resting face (between reactions): happy, or sleepy when he is too tired to play.</summary>
        public void Rest(string emotion)
        {
            _rest = string.IsNullOrEmpty(emotion) ? "happy" : emotion;
            if (_faceHold <= 0f) Face(_rest);
            if (_rest == "sleepy") Clip("sit-loop");
        }

        /// <summary>Hello on a window: a wave and a small hop.</summary>
        public void Wave()
        {
            if (_rest == "sleepy") return;
            Clip("wave");
            Hop(0.5f);
            Face("happy", 1.6f);
        }

        /// <summary>A gain: a hop that grows with the streak; big gains or long streaks light the face up.</summary>
        public void Cheer(int amount, int streak)
        {
            var big = amount >= 10 || streak >= 4;
            Hop(Mathf.Clamp(0.45f + streak * 0.12f, 0.45f, 1f));
            Face(big ? "ecstatic" : "happy", big ? 1.4f : 0.9f);
            if (streak >= 3 && streak % 3 == 0) Clip("hop");
        }

        /// <summary>A loss: the face falls; a big loss gets the head shake.</summary>
        public void Wince(bool big)
        {
            _hop = 0f;
            Face(big ? "dizzy" : "sad", big ? 1.6f : 1.1f);
            if (big) Clip("refuse");
        }

        /// <summary>A streak step / milestone: the arms go up.</summary>
        public void Hype()
        {
            Clip("celebrate");
            Hop(0.9f);
            Face("ecstatic", 1.8f);
            if (!Effects.Reduced) _twirl = 1f;
        }

        /// <summary>LOS!: ready, a bounce.</summary>
        public void Ready()
        {
            Hop(0.6f);
            Face("happy", 0.8f);
        }

        /// <summary>The round is over: arms up (a dance for a record), or the sad head shake for a round without points.</summary>
        public void Celebrate(bool won, bool record)
        {
            if (won)
            {
                Clip(record ? "dance-loop" : "celebrate");
                Hop(1f);
                Face("ecstatic", record ? 6f : 2.4f);
                if (record && !Effects.Reduced) _twirl = 1f;
            }
            else
            {
                Clip("refuse");
                Face("sad", 3f);
            }
        }

        private string RestLoop => _rest == "sleepy" ? "sit-loop" : RestClip;

        /// <summary>Back to the rest loop (a dance ends when its card closes).</summary>
        public void Settle()
        {
            Clip(RestLoop);
            _faceHold = 0f;
            Face(_rest);
        }

        private void Hop(float strength)
        {
            if (Effects.Reduced) strength *= 0.4f;
            _hop = 1f;
            _hopHeight = 0.26f * strength;
        }

        private void Face(string emotion, float hold = 0f)
        {
            _faceHold = hold;
            if (Gooby == null) { _pendingFace = emotion; return; }
            if (Gooby.Emotion != emotion) Gooby.SetEmotion(emotion);
        }

        private void Clip(string clip)
        {
            _idleIn = Random.Range(5f, 9f);
            // one-shots fall back to the idle loop inside the rig (GoobyAvatar queues it)
            _loop = clip.EndsWith("-loop") ? clip : "idle-loop";
            if (Gooby == null) { _pendingClip = clip; return; }
            Gooby.Play(clip, 0.15f);
        }

        /// <summary>Per frame (real time): the hop arc and squash, the idle sway, the face settling back, glances.</summary>
        public void Tick(float dt)
        {
            if (Root == null) return;
            if (_target != null) Resize();
            if (_hop > 0f) _hop = Mathf.Max(0f, _hop - dt * 2.6f);
            var arc = Mathf.Sin(_hop * Mathf.PI);
            var y = arc * _hopHeight;
            // squash on take-off and landing, stretch at the top of the arc
            var phase = 1f - _hop;
            var squash = _hop > 0f ? 1f + (phase < 0.15f || phase > 0.85f ? -0.1f : 0.06f * arc) : 1f;
            _sway += dt;
            var sway = Effects.Reduced ? 0f : Mathf.Sin(_sway * 1.3f) * 4f;
            if (_twirl > 0f) _twirl = Mathf.Max(0f, _twirl - dt * 1.7f);
            var spin = 360f * Mathf.SmoothStep(0f, 1f, 1f - _twirl);
            _body.localPosition = new Vector3(0f, y, 0f);
            _body.localScale = new Vector3(2f - squash, squash, 2f - squash);
            _facingNow = Mathf.LerpAngle(_facingNow, _facing, 1f - Mathf.Exp(-10f * dt));
            _body.localRotation = Quaternion.Euler(0f, BaseYaw + sway + _facingNow + (_twirl > 0f ? spin : 0f), 0f);
            var shadowScale = Mathf.Lerp(1f, 0.7f, Mathf.Clamp01(y / 0.3f));
            _shadow.localScale = new Vector3(1.1f * shadowScale, 0.7f * shadowScale, 1f);
            if (_faceHold > 0f)
            {
                _faceHold -= dt;
                if (_faceHold <= 0f) Face(_rest);
            }
            if (Gooby == null) return;
            // a reaction's one-shot ends in the rig's idle loop: a figure on the move (or a dancer) picks its loop back up
            var want = _walking ? "walk-loop" : RestLoop;
            if (_loop != want && _hop <= 0f && _faceHold <= 0f) Clip(want);
            _idleIn -= dt;
            if (Ambient && _idleIn <= 0f && _faceHold <= 0f && _hop <= 0f && _rest != "sleepy" && _loop == "idle-loop")
            {
                _idleIn = Random.Range(6f, 11f);
                Gooby.Play(Random.value < 0.6f ? "idle_lookaround" : "wave", 0.25f);
            }
        }

        public void Destroy()
        {
            if (_target != null) _target.style.backgroundImage = StyleKeyword.Null;
            _target = null;
            if (Camera != null) Camera.targetTexture = null;
            if (Texture != null) { Texture.Release(); Object.Destroy(Texture); Texture = null; }
            if (Root != null) Object.Destroy(Root);
            Gooby = null;
        }
    }
}
