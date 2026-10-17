using System;
using System.Collections.Generic;
using Gooby.Core.Life;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// GOB.TY on the living-room TV (port of gobty/clip_defs.gd + tv_stage.gd, simplified): a tiny puppet studio far off
    /// stage, filmed by its own camera into a texture that lies on the TV's screen. Five shows rotate — GOOBY NEWS (an
    /// anchor at the desk, rotating silly headlines), cooking with Küchen-Gooby (the stew goes PUFF), GOOBY SPORT (the
    /// bouncy-ball final, someone falls over), the weather with Wolke Wuschel (tomorrow's real home weather) and the
    /// good-night channel (moon, stars, a sleeping Gooby, then the test card). The studio has its own light, so the
    /// room's lighting stays as it is. <see cref="Banner"/> is the show's caption line for the HUD.
    /// </summary>
    public sealed class GobtyTv
    {
        public static readonly string[] Clips = { "news", "kochen", "sport", "wetter", "nacht" };
        public const float ClipSeconds = 14f;
        private static readonly Vector3 StudioOrigin = new Vector3(-800f, 0f, 0f);

        private readonly GameObject _root, _screen;
        private readonly Camera _cam;
        private readonly RenderTexture _rt;
        private readonly Func<string, string> _t;
        private readonly string _tomorrow;
        private Transform _set;
        private readonly List<GoobyAvatar> _actors = new List<GoobyAvatar>();
        private readonly List<Transform> _props = new List<Transform>();
        private int _clip, _rotation, _token;
        private float _time, _bannerIn;
        private int _bannerStep;
        private bool _puffed, _fell;

        /// <summary>The current show's caption (title, headline …) — changes raise <see cref="Changed"/>.</summary>
        public string Title { get; private set; }
        public string Banner { get; private set; }
        public string ClipId => Clips[_clip];
        public event Action Changed;

        public GobtyTv(FurnitureMarker tv, Transform parent, Func<string, string> t, string tomorrow, int startClip = 0)
        {
            _t = t;
            _tomorrow = tomorrow;
            _root = new GameObject("GobtyStudio");
            _root.transform.position = StudioOrigin;
            _rt = new RenderTexture(512, 288, 16, RenderTextureFormat.ARGB32) { name = "GobTy", antiAliasing = 1 };
            _rt.Create();
            var camGo = new GameObject("GobtyCamera");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.fieldOfView = 34f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 25f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            camGo.transform.position = StudioOrigin + new Vector3(0f, 1.15f, -3.3f);
            camGo.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_cam);
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var key = new GameObject("StudioLight").AddComponent<Light>();
            key.transform.SetParent(_root.transform, false);
            key.transform.position = StudioOrigin + new Vector3(0.6f, 2.6f, -2.2f);
            key.type = LightType.Point;
            key.range = 9f;
            key.intensity = 2.2f;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.shadows = LightShadows.None;

            // the screen: an unlit quad over the TV's front, a touch above its middle
            var b = tv.Bounds;
            var front = tv.Front;
            var across = Mathf.Abs(front.z) > 0.5f ? b.size.x : b.size.z;
            var depth = Mathf.Abs(front.z) > 0.5f ? b.extents.z : b.extents.x;
            var w = across * 0.78f;
            var h = Mathf.Min(b.size.y * 0.62f, w * 0.5625f);
            w = h / 0.5625f;
            _screen = ArtLibrary.Quad(parent, "GobtyScreen", b.center + front * (depth + 0.012f) + Vector3.up * b.size.y * 0.1f,
                Quaternion.LookRotation(front), new Vector2(w, h), ArtLibrary.Screen(_rt));
            _clip = ((startClip % Clips.Length) + Clips.Length) % Clips.Length;
            BuildClip();
        }

        public void Zap()
        {
            _clip = (_clip + 1) % Clips.Length;
            if (_clip == 0) _rotation++;
            BuildClip();
        }

        private Transform Box(string name, Vector3 local, Vector3 size, Color c, bool glow = false)
        {
            var go = ArtLibrary.Box(_set, name, StudioOrigin + local, size, glow ? ArtLibrary.Glow(c) : ArtLibrary.Solid(c, 0.2f), false);
            return go.transform;
        }

        private Transform Ball(string name, Vector3 local, float r, Color c, bool glow = false)
        {
            var go = ArtLibrary.Sphere(_set, name, StudioOrigin + local, r, glow ? ArtLibrary.Glow(c) : ArtLibrary.Solid(c, 0.3f), false);
            return go.transform;
        }

        private void BuildClip()
        {
            _token++;
            foreach (var a in _actors) if (a != null) UnityEngine.Object.Destroy(a.gameObject);
            _actors.Clear();
            _props.Clear();
            if (_set != null) UnityEngine.Object.Destroy(_set.gameObject);
            _set = new GameObject("Set:" + ClipId).transform;
            _set.SetParent(_root.transform, false);
            _time = 0f;
            _bannerStep = 0;
            _bannerIn = 0f;
            _puffed = _fell = false;
            Title = _t("gobty.clip." + ClipId);
            switch (ClipId)
            {
                case "news":
                    _cam.backgroundColor = ArtLibrary.Hex("#2E4A8C");
                    Box("Wall", new Vector3(0f, 1.5f, 1.6f), new Vector3(7f, 3.4f, 0.1f), ArtLibrary.Hex("#3D63B8"));
                    Box("Logo", new Vector3(1.25f, 1.95f, 1.5f), new Vector3(0.9f, 0.6f, 0.05f), ArtLibrary.Hex("#FFD166"), true);
                    Box("Desk", new Vector3(0f, 0.42f, 0.2f), new Vector3(2.0f, 0.84f, 0.7f), ArtLibrary.Hex("#FFF8EE"));
                    Box("DeskStripe", new Vector3(0f, 0.6f, -0.16f), new Vector3(2.0f, 0.12f, 0.02f), ArtLibrary.Hex("#FF8B8B"));
                    Box("Papers", new Vector3(-0.45f, 0.86f, 0.05f), new Vector3(0.36f, 0.04f, 0.26f), Color.white);
                    _ = Actor(new Vector3(0f, 0f, 0.75f), 1.05f, "snow", "hut_melone", "readingGlasses", "hals_krawatte");
                    break;
                case "kochen":
                    _cam.backgroundColor = ArtLibrary.Hex("#FFE9D2");
                    Box("Tiles", new Vector3(0f, 1.5f, 1.6f), new Vector3(7f, 3.4f, 0.1f), ArtLibrary.Hex("#E4F6EA"));
                    Box("Counter", new Vector3(0f, 0.45f, 0.2f), new Vector3(2.4f, 0.9f, 0.8f), ArtLibrary.Hex("#C98E55"));
                    _props.Add(ArtLibrary.Cylinder(_set, "Pot", StudioOrigin + new Vector3(0.45f, 1.05f, 0.1f), 0.24f, 0.3f, ArtLibrary.Solid(ArtLibrary.Hex("#A8B3C4"), 0.6f)).transform);
                    _props.Add(Ball("Stew", new Vector3(0.45f, 1.2f, 0.1f), 0.2f, ArtLibrary.Hex("#8B5E3C")));
                    _ = Actor(new Vector3(-0.35f, 0f, 0.75f), 1.0f, "caramel", "beret", null, "bandana");
                    break;
                case "sport":
                    _cam.backgroundColor = ArtLibrary.Hex("#BFE6FF");
                    Box("Field", new Vector3(0f, -0.05f, 1.5f), new Vector3(9f, 0.1f, 6f), ArtLibrary.Hex("#7FCF9B"));
                    Box("Line", new Vector3(0f, 0.01f, 0.4f), new Vector3(0.06f, 0.02f, 4f), Color.white);
                    _props.Add(Ball("Ball", new Vector3(0f, 0.25f, 0.6f), 0.18f, ArtLibrary.Hex("#FF8B8B")));
                    _ = Actor(new Vector3(-0.85f, 0f, 0.9f), 0.85f, "fell_himmel", "cap", null, null);
                    _ = Actor(new Vector3(0.85f, 0f, 0.9f), 0.85f, "rose", "partyHat", null, null);
                    break;
                case "wetter":
                    _cam.backgroundColor = ArtLibrary.Hex("#DCEBFF");
                    Box("Map", new Vector3(0.55f, 1.45f, 1.4f), new Vector3(2.4f, 1.5f, 0.06f), ArtLibrary.Hex("#7FB8FF"));
                    Box("Land", new Vector3(0.35f, 1.35f, 1.36f), new Vector3(1.4f, 0.9f, 0.02f), ArtLibrary.Hex("#9EDB86"));
                    for (var i = 0; i < 4; i++) WeatherSymbol(i);
                    // Wolke Wuschel: a puffy cloud with eyes
                    var cloud = new GameObject("Wuschel").transform;
                    cloud.SetParent(_set, false);
                    cloud.position = StudioOrigin + new Vector3(-1.05f, 1.2f, 0.6f);
                    foreach (var (dx, dy, r) in new[] { (-0.22f, 0f, 0.22f), (0f, 0.1f, 0.28f), (0.24f, 0f, 0.2f) })
                        ArtLibrary.Sphere(cloud, "Puff", cloud.position + new Vector3(dx, dy, 0f), r, ArtLibrary.Solid(Color.white, 0.2f), false);
                    foreach (var dx in new[] { -0.08f, 0.08f })
                        ArtLibrary.Sphere(cloud, "Eye", cloud.position + new Vector3(dx, 0.12f, -0.26f), 0.035f, ArtLibrary.Solid(ArtLibrary.Hex("#3B364F"), 0.6f), false);
                    _props.Add(cloud);
                    break;
                default: // nacht
                    _cam.backgroundColor = ArtLibrary.Hex("#14183A");
                    _props.Add(Ball("Moon", new Vector3(1.3f, 2.0f, 2.2f), 0.32f, new Color(1.6f, 1.5f, 1.1f), true));
                    for (var i = 0; i < 18; i++)
                        _props.Add(Ball("Star", new Vector3(-2.2f + (i * 0.77f) % 4.4f, 1.3f + (i * 0.41f) % 1.6f, 2.6f), 0.025f + (i % 3) * 0.01f, new Color(1.8f, 1.8f, 2.2f), true));
                    Box("Bed", new Vector3(0f, 0.22f, 0.7f), new Vector3(1.4f, 0.44f, 0.8f), ArtLibrary.Hex("#FFC9A6"));
                    Box("Blanket", new Vector3(0.15f, 0.47f, 0.7f), new Vector3(1.0f, 0.08f, 0.82f), ArtLibrary.Hex("#B58CE4"));
                    _ = Actor(new Vector3(-0.35f, 0.44f, 0.75f), 0.8f, "cream", null, null, null, sleep: true);
                    break;
            }
            NextBanner();
            Changed?.Invoke();
        }

        private void WeatherSymbol(int i)
        {
            var hours = new[] { 8, 12, 16, 20 };
            var typ = SoulWetter.Zustand(_tomorrow, hours[i]).Typ;
            var p = new Vector3(-0.15f + i * 0.45f, 1.55f + (i % 2) * 0.12f, 1.3f);
            if (typ == "sonne") Ball("Sun", p, 0.13f, new Color(1.9f, 1.5f, 0.5f), true);
            else
            {
                Ball("Cloud", p, 0.13f, Color.white);
                Ball("Cloud2", p + new Vector3(0.12f, -0.03f, 0f), 0.1f, Color.white);
                if (typ == "regen" || typ == "niesel" || typ == "gewitter")
                    for (var k = 0; k < 3; k++) Ball("Drop", p + new Vector3(-0.08f + k * 0.08f, -0.18f, 0f), 0.025f, ArtLibrary.Hex("#3E7BD6"));
                if (typ == "schnee")
                    for (var k = 0; k < 3; k++) Ball("Flake", p + new Vector3(-0.08f + k * 0.08f, -0.18f, 0f), 0.03f, Color.white, true);
            }
        }

        private async System.Threading.Tasks.Task Actor(Vector3 local, float scale, string fell, string hut, string brille, string hals, bool sleep = false)
        {
            var token = _token;
            var pos = StudioOrigin + local;
            var a = await GoobyAvatar.CreateAsync(_set, pos);
            if (a == null) return;
            if (token != _token || _set == null) { UnityEngine.Object.Destroy(a.gameObject); return; }
            a.transform.position = pos;
            a.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            a.transform.localScale = Vector3.one * scale;
            var outfit = new Dictionary<string, string> { ["fell"] = fell };
            if (hut != null) outfit["hut"] = hut;
            if (brille != null) outfit["brille"] = brille;
            if (hals != null) outfit["hals"] = hals;
            a.Wear(outfit);
            if (sleep)
            {
                a.transform.rotation = Quaternion.Euler(0f, 180f, 90f);
                a.SetEmotion("sleepy");
                a.Play("sleep-loop");
            }
            else
            {
                a.SetEmotion("happy");
                a.Play("idle-loop");
            }
            _actors.Add(a);
        }

        private void NextBanner()
        {
            string line;
            switch (ClipId)
            {
                case "news":
                    line = _bannerStep == 0 ? _t("gobty.news.intro") : _t("gobty.news.schlagzeile" + (((_rotation * 3 + _bannerStep - 1) % 10) + 1));
                    break;
                case "wetter":
                    if (_bannerStep == 0) line = _t("gobty.wetter.intro");
                    else
                    {
                        var hour = new[] { 8, 12, 16, 20 }[(_bannerStep - 1) % 4];
                        var typ = SoulWetter.Zustand(_tomorrow, hour).Typ;
                        line = _t("gobty.wetter.banner").Replace("{stunde}", hour.ToString()).Replace("{wetter}", _t("gobty.wetter.typ." + typ));
                    }
                    break;
                default:
                    line = _t("gobty." + ClipId + ".b" + Mathf.Min(3, _bannerStep + 1));
                    break;
            }
            Banner = line;
            _bannerStep++;
            _bannerIn = ClipSeconds / (ClipId == "news" || ClipId == "wetter" ? 4.2f : 3.1f);
            Changed?.Invoke();
        }

        public void Tick(float dt)
        {
            if (_root == null) return;
            _time += dt;
            _bannerIn -= dt;
            if (_bannerIn <= 0f) NextBanner();
            switch (ClipId)
            {
                case "news":
                    if (_actors.Count > 0 && Mathf.Repeat(_time, 3.2f) < dt) _actors[0].BabblePulse();
                    break;
                case "kochen":
                    if (_props.Count > 1 && _props[1] != null) _props[1].localScale = Vector3.one * 0.4f * (1f + 0.08f * Mathf.Sin(_time * 6f));
                    if (!_puffed && _time > ClipSeconds * 0.4f)
                    {
                        _puffed = true;
                        Effects.Puff(StudioOrigin + new Vector3(0.45f, 1.35f, 0.1f), ArtLibrary.Hex("#8B8B8B"), 26, 0.35f, 1.4f, 1.2f, Vector3.up, 0.9f);
                        if (_actors.Count > 0) { _actors[0].SetEmotion("scared"); _actors[0].Play("hop", 0.1f); }
                    }
                    break;
                case "sport":
                    if (_props.Count > 0 && _props[0] != null)
                    {
                        var x = Mathf.Sin(_time * 1.6f) * 0.75f;
                        _props[0].position = StudioOrigin + new Vector3(x, 0.25f + Mathf.Abs(Mathf.Sin(_time * 4.8f)) * 0.9f, 0.6f);
                    }
                    if (Mathf.Repeat(_time, 1.3f) < dt)
                        foreach (var a in _actors) if (a != null) a.Play("hop", 0.1f);
                    if (!_fell && _time > ClipSeconds * 0.6f && _actors.Count > 1 && _actors[1] != null)
                    {
                        _fell = true;
                        _actors[1].transform.rotation = Quaternion.Euler(0f, 180f, 70f);
                        _actors[1].SetEmotion("dizzy");
                    }
                    break;
                case "wetter":
                    if (_props.Count > 0 && _props[0] != null) _props[0].position = StudioOrigin + new Vector3(-1.05f, 1.2f + Mathf.Sin(_time * 1.8f) * 0.06f, 0.6f);
                    break;
                default:
                    if (_time > ClipSeconds * 0.8f) _cam.backgroundColor = Color.Lerp(_cam.backgroundColor, ArtLibrary.Hex("#888888"), dt * 2f);
                    break;
            }
            if (_time >= ClipSeconds) Zap();
        }

        public void Destroy()
        {
            _token++;
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_screen != null) UnityEngine.Object.Destroy(_screen);
        }
    }
}
