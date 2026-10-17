using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.World
{
    /// <summary>
    /// A minigame's 3D diorama (port of the Godot stage kits _3da/_3db_stage): a small set far from the world with its
    /// own camera rendering into a RenderTexture that becomes the background of the frame's play field — the 2D HUD,
    /// floats and banners stay on top, input stays on the field. The engine keeps its view space (ViewW×ViewH px);
    /// <see cref="ViewToWorld"/> maps it onto the plane z = 0 at 100 px per metre, which the camera frames exactly, so
    /// anything placed with it lands where the engine has it. Props come from the CC0/Blender minigame GLBs, Gooby is
    /// the rigged avatar in the player's outfit. While a stage lives it holds a lease on the <see cref="MgStudio"/> light
    /// (the world camera is off during a round); <see cref="Destroy"/> returns it.
    /// </summary>
    public sealed partial class MgStage3D
    {
        public static readonly Vector3 Origin = new Vector3(-400f, 0f, 0f);

        /// <summary>False on a null graphics device (batch-mode tests, headless QA players): the hosts keep their 2D field.</summary>
        public static bool Supported => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        public const float MetresPerPx = 0.01f;
        public const float Fov = 30f;

        public GameObject Root { get; }
        public Camera Camera { get; }
        public RenderTexture Texture { get; private set; }
        public float ViewW { get; }
        public float ViewH { get; }
        public GoobyAvatar Gooby { get; private set; }

        private VisualElement _field;

        /// <summary>Share of the screen height the texture renders at (a stage inside a sheet needs less than a full-screen field).</summary>
        public float HeightShare { get; set; } = 1f;

        public MgStage3D(double viewW, double viewH, Color sky)
        {
            ViewW = (float)viewW;
            ViewH = (float)viewH;
            Root = new GameObject("MgStage3D");
            Object.DontDestroyOnLoad(Root);
            var h = ViewH * MetresPerPx;
            var camGo = new GameObject("MgStageCamera");
            camGo.transform.SetParent(Root.transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = sky;
            Camera.fieldOfView = Fov;
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 90f;
            Camera.allowHDR = false;
            Camera.allowMSAA = false;
            var distance = h * 0.5f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            camGo.transform.position = Origin + new Vector3(0f, h * 0.5f, -distance);
            camGo.transform.rotation = Quaternion.identity;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(Camera);
            data.renderPostProcessing = false;
            data.renderShadows = true;
            MgStudio.Acquire();
            _leased = true;
        }

        private bool _leased;
        private readonly List<Object> _owned = new List<Object>();

        /// <summary>Engine view point (px, y down) → stage world (z = depth: + behind the play plane, − in front).</summary>
        public Vector3 ViewToWorld(double x, double y, float depth = 0f) =>
            Origin + new Vector3(((float)x - ViewW * 0.5f) * MetresPerPx, (ViewH - (float)y) * MetresPerPx, depth);

        /// <summary>
        /// Every stage builds around the same <see cref="Origin"/>, so a stage under another screen's stage would show
        /// through it: parked, the whole set moves far below the other camera's reach and its own camera rests. Callers
        /// stop placing things by world position while parked.
        /// </summary>
        public void Park(bool parked)
        {
            if (Root != null) Root.transform.position = parked ? ParkOffset : Vector3.zero;
            if (Camera != null) Camera.enabled = !parked;
        }

        public bool Parked => Root != null && Root.transform.position == ParkOffset;

        private static readonly Vector3 ParkOffset = new Vector3(0f, -1000f, 0f);

        /// <summary>Shows the stage as the field's background; the texture follows the field's on-screen size.</summary>
        public void Bind(VisualElement field)
        {
            _field = field;
            Resize();
        }

        private void Resize()
        {
            if (_field == null) return;
            var aspect = ViewW / ViewH;
            // "sparsam" renders the world at 0.6× (GraphicsQuality); the stages follow, or old phones pay full price here
            var scale = GraphicsQuality.Active == "sparsam" ? 0.6f : 1f;
            var h = Mathf.Clamp(Mathf.RoundToInt(Screen.height * scale * Mathf.Clamp01(HeightShare)), 360, 1600);
            var w = Mathf.Max(64, Mathf.RoundToInt(h * aspect));
            if (Texture != null && Texture.width == w && Texture.height == h) return;
            var old = Texture;
            Texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "MgStage", antiAliasing = 1 };
            Texture.Create();
            Camera.targetTexture = Texture;
            Camera.aspect = aspect;
            _field.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(Texture));
            if (old != null) { old.Release(); Object.Destroy(old); }
        }

        /// <summary>A minigame GLB (StreamingAssets/gooby/minigame) at a stage position; the anchor is returned at once.</summary>
        public Transform Prop(string model, Vector3 worldPos, float scale = 1f, float yaw = 0f)
        {
            var anchor = new GameObject("Mg:" + model).transform;
            anchor.SetParent(Root.transform, false);
            anchor.position = worldPos;
            anchor.rotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.localScale = Vector3.one * scale;
            _ = FillAsync(anchor, Path.Combine(AssetLoader.Root, "minigame", model + ".glb"), model);
            return anchor;
        }

        /// <summary>Any static GLB under StreamingAssets (full path) at a stage position — the ranch's barns, bales and troughs.</summary>
        public Transform Glb(string path, Vector3 worldPos, float scale = 1f, float yaw = 0f)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var anchor = new GameObject("Glb:" + name).transform;
            anchor.SetParent(Root.transform, false);
            anchor.position = worldPos;
            anchor.rotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.localScale = Vector3.one * scale;
            _ = FillAsync(anchor, path, name);
            return anchor;
        }

        /// <summary>A Blender furniture/garden prop GLB (StreamingAssets/gooby/props: rock, mushroom, flower …).</summary>
        public Transform HomeProp(string archetype, Vector3 worldPos, float scale = 1f, float yaw = 0f)
        {
            var anchor = new GameObject("MgProp:" + archetype).transform;
            anchor.SetParent(Root.transform, false);
            anchor.position = worldPos;
            anchor.rotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.localScale = Vector3.one * scale;
            _ = FillAsync(anchor, AssetLoader.PropPath(archetype), archetype);
            return anchor;
        }

        /// <summary>A Blender scenery GLB (trees, fences, flowers …) at a stage position.</summary>
        public Transform PlaceScenery(string archetype, Vector3 worldPos, float yaw = 0f, float scale = 1f)
        {
            var go = Scenery.Place(Root.transform, archetype, worldPos - Root.transform.position, yaw, scale);
            return go.transform;
        }

        private static async System.Threading.Tasks.Task FillAsync(Transform anchor, string path, string name)
        {
            var clone = await AssetLoader.InstanceAsync(path, anchor, name);
            if (clone == null) return;
            if (anchor == null) { Object.Destroy(clone); return; }
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
        }

        /// <summary>The player's Gooby (fur, outfit) on the stage; null until the clone is in (async).</summary>
        public async System.Threading.Tasks.Task<GoobyAvatar> SpawnGoobyAsync(Vector3 worldPos, float scale, Dictionary<string, string> outfit)
        {
            var avatar = await GoobyAvatar.CreateAsync(Root.transform, worldPos);
            if (avatar == null || Root == null) return null;
            avatar.transform.position = worldPos;
            avatar.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            avatar.transform.localScale = Vector3.one * scale;
            if (outfit != null) avatar.Wear(outfit);
            avatar.Play("idle-loop");
            avatar.SetEmotion("happy");
            Gooby = avatar;
            return avatar;
        }

        public void Destroy()
        {
            if (_leased) MgStudio.Release();
            _leased = false;
            if (_field != null) _field.style.backgroundImage = StyleKeyword.Null;
            _field = null;
            if (Camera != null) Camera.targetTexture = null;
            if (Texture != null) { Texture.Release(); Object.Destroy(Texture); Texture = null; }
            if (Root != null) Object.Destroy(Root);
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear();
        }
    }
}
